// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class CosmosMutationEngine : IMutationEngine
    {
        private readonly CosmosClientProvider _clientProvider;
        private readonly ISqlMetadataProvider _metadataProvider;
        private readonly IAuthorizationResolver _authorizationResolver;

        public CosmosMutationEngine(
            CosmosClientProvider clientProvider,
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver)
        {
            _clientProvider = clientProvider;
            _metadataProvider = metadataProvider;
            _authorizationResolver = authorizationResolver;
        }

        private async Task<JObject> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> queryArgs, CosmosOperationMetadata resolver)
        {
            // TODO: add support for all mutation types
            // we only support CreateOrUpdate (Upsert) for now

            if (queryArgs == null)
            {
                // TODO: in which scenario the queryArgs is empty
                throw new ArgumentNullException(nameof(queryArgs));
            }

            CosmosClient? client = _clientProvider.Client;
            if (client is null)
            {
                throw new DataApiBuilderException(
                    message: "Cosmos DB has not been properly initialized",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
            }

            Container container = client.GetDatabase(resolver.DatabaseName)
                                        .GetContainer(resolver.ContainerName);

            // If authorization fails, an exception will be thrown and request execution halts.
            string graphQLType = context.Selection.Field.Type.NamedType().Name.Value;
            string entityName = _metadataProvider.GetEntityName(graphQLType);
            AuthorizeMutationFields(context, queryArgs, entityName, resolver.OperationType);

            ItemResponse<JObject>? response = resolver.OperationType switch
            {
                EntityActionOperation.UpdateGraphQL => await HandleUpdateAsync(queryArgs, container),
                EntityActionOperation.Create => await HandleCreateAsync(queryArgs, container),
                EntityActionOperation.Delete => await HandleDeleteAsync(queryArgs, container),
                _ => throw new NotSupportedException($"unsupported operation type: {resolver.OperationType}")
            };

            return response.Resource;
        }

        /// <inheritdoc/>
        public void AuthorizeMutationFields(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string entityName,
            EntityActionOperation mutationOperation)
        {
            string role = string.Empty;
            if (context.ContextData.TryGetValue(key: AuthorizationResolver.CLIENT_ROLE_HEADER, out object? value) && value is StringValues stringVals)
            {
                role = stringVals.ToString();
            }

            if (string.IsNullOrEmpty(role))
            {
                throw new DataApiBuilderException(
                    message: "No ClientRoleHeader available to perform authorization.",
                    statusCode: HttpStatusCode.Unauthorized,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }

            List<string> inputArgumentKeys;
            if (mutationOperation != EntityActionOperation.Delete)
            {
                inputArgumentKeys = BaseSqlQueryStructure.GetSubArgumentNamesFromGQLMutArguments(MutationBuilder.INPUT_ARGUMENT_NAME, parameters);
            }
            else
            {
                inputArgumentKeys = parameters.Keys.ToList();
            }

            bool isAuthorized = mutationOperation switch
            {
                EntityActionOperation.UpdateGraphQL =>
                    _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: role, operation: EntityActionOperation.Update, inputArgumentKeys),
                EntityActionOperation.Create =>
                    _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName: role, operation: mutationOperation, inputArgumentKeys),
                EntityActionOperation.Delete => true,// Field level authorization is not supported for delete mutations. A requestor must be authorized
                                                     // to perform the delete operation on the entity to reach this point.
                _ => throw new DataApiBuilderException(
                                        message: "Invalid operation for GraphQL Mutation, must be Create, UpdateGraphQL, or Delete",
                                        statusCode: HttpStatusCode.BadRequest,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest
                                        ),
            };
            if (!isAuthorized)
            {
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.GRAPHQL_MUTATION_FIELD_AUTHZ_FAILURE,
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                );
            }
        }

        private static async Task<ItemResponse<JObject>> HandleDeleteAsync(IDictionary<string, object?> queryArgs, Container container)
        {
            string? partitionKey = null;
            string? id = null;

            if (queryArgs.TryGetValue(QueryBuilder.ID_FIELD_NAME, out object? idObj))
            {
                id = idObj?.ToString();
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("id field is mandatory");
            }

            if (queryArgs.TryGetValue(QueryBuilder.PARTITION_KEY_FIELD_NAME, out object? partitionKeyObj))
            {
                partitionKey = partitionKeyObj?.ToString();
            }

            if (string.IsNullOrEmpty(partitionKey))
            {
                throw new InvalidDataException("Partition Key field is mandatory");
            }

            return await container.DeleteItemAsync<JObject>(id, new PartitionKey(partitionKey));
        }

        private static async Task<ItemResponse<JObject>> HandleCreateAsync(IDictionary<string, object?> queryArgs, Container container)
        {
            object? item = queryArgs[CreateMutationBuilder.INPUT_ARGUMENT_NAME];

            JObject? input;
            // Variables were provided to the mutation
            if (item is Dictionary<string, object?>)
            {
                input = (JObject?)ParseVariableInputItem(item);
            }
            else
            {
                // An inline argument was set
                input = (JObject?)ParseInlineInputItem(item);
            }

            if (input is null)
            {
                throw new InvalidDataException("Input Item field is invalid");
            }

            return await container.CreateItemAsync(input);
        }

        private static async Task<ItemResponse<JObject>> HandleUpdateAsync(IDictionary<string, object?> queryArgs, Container container)
        {
            string? partitionKey = null;
            string? id = null;

            if (queryArgs.TryGetValue(QueryBuilder.ID_FIELD_NAME, out object? idObj))
            {
                id = idObj?.ToString();
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("id field is mandatory");
            }

            if (queryArgs.TryGetValue(QueryBuilder.PARTITION_KEY_FIELD_NAME, out object? partitionKeyObj))
            {
                partitionKey = partitionKeyObj?.ToString();
            }

            if (string.IsNullOrEmpty(partitionKey))
            {
                throw new InvalidDataException("Partition Key field is mandatory");
            }

            object? item = queryArgs[CreateMutationBuilder.INPUT_ARGUMENT_NAME];

            JObject? input;
            // Variables were provided to the mutation
            if (item is Dictionary<string, object?>)
            {
                input = (JObject?)ParseVariableInputItem(item);
            }
            else
            {
                // An inline argument was set
                input = (JObject?)ParseInlineInputItem(item);
            }

            if (input is null)
            {
                throw new InvalidDataException("Input Item field is invalid");
            }
            else
            {
                return await container.ReplaceItemAsync<JObject>(input, id, new PartitionKey(partitionKey), new ItemRequestOptions());
            }
        }

        /// <summary>
        /// The method is for parsing the mutation input object with nested inner objects when input is passed in as variables.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private static object? ParseVariableInputItem(object? item)
        {
            if (item is Dictionary<string, object?> inputItem)
            {
                JObject? createInput = new();

                foreach (string key in inputItem.Keys)
                {
                    if (inputItem.TryGetValue(key, out object? value) && value != null)
                    {
                        createInput.Add(new JProperty(key, JToken.FromObject(inputItem.GetValueOrDefault(key)!)));
                    }
                }

                return createInput;
            }

            return item;
        }

        /// <summary>
        /// The method is for parsing the mutation input object with nested inner objects when input is passing inline.
        /// </summary>
        /// <param name="item"> In the form of ObjectFieldNode, or List<ObjectFieldNode></param>
        /// <returns>In the form of JObject</returns>
        private static object? ParseInlineInputItem(object? item)
        {
            JObject? createInput = new();

            if (item is ObjectFieldNode node)
            {
                createInput.Add(new JProperty(node.Name.Value, ParseInlineInputItem(node.Value.Value)));
                return createInput;
            }

            if (item is List<ObjectFieldNode> nodeList)
            {
                foreach (ObjectFieldNode subfield in nodeList)
                {
                    createInput.Add(new JProperty(subfield.Name.Value, ParseInlineInputItem(subfield.Value.Value)));
                }

                return createInput;
            }

            // For nested array objects
            if (item is List<IValueNode> nodeArray)
            {
                JArray jarrayObj = new();

                foreach (IValueNode subfield in nodeArray)
                {
                    jarrayObj.Add(ParseInlineInputItem(subfield.Value));
                }

                return jarrayObj;
            }

            return item;
        }

        /// <summary>
        /// Executes the mutation query and return result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context,
            IDictionary<string, object?> parameters)
        {
            string graphQLType = context.Selection.Field.Type.NamedType().Name.Value;
            string entityName = _metadataProvider.GetEntityName(graphQLType);
            string databaseName = _metadataProvider.GetSchemaName(entityName);
            string containerName = _metadataProvider.GetDatabaseObjectName(entityName);

            string graphqlMutationName = context.Selection.Field.Name.Value;
            EntityActionOperation mutationOperation =
                MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);

            CosmosOperationMetadata mutation = new(databaseName, containerName, mutationOperation);
            // TODO: we are doing multiple round of serialization/deserialization
            // fixme
            JObject jObject = await ExecuteAsync(context, parameters, mutation);
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            return new Tuple<JsonDocument?, IMetadata?>((jObject is null) ? null! : JsonDocument.Parse(jObject.ToString()), null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        }

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of REST mutation request.</param>
        /// <returns>JSON object result</returns>
        public Task<IActionResult?> ExecuteAsync(RestRequestContext context)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public Task<IActionResult?> ExecuteAsync(StoredProcedureRequestContext context)
        {
            throw new NotImplementedException();
        }
    }
}
