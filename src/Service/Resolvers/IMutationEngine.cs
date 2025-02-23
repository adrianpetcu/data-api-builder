// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Models;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Interface for execution of GraphQL mutations against a database.
    /// </summary>
    public interface IMutationEngine
    {
        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">Middleware context of the mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result and a metadata object required to resolve the result</returns>
        public Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context,
            IDictionary<string, object?> parameters);

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of REST mutation request.</param>
        /// <returns>IActionResult</returns>
        public Task<IActionResult?> ExecuteAsync(RestRequestContext context);

        /// <summary>
        /// Executes the stored procedure as a mutation query and returns result as JSON asynchronously.
        /// Execution will be identical regardless of mutation operation, but result returned will differ
        /// </summary>
        public Task<IActionResult?> ExecuteAsync(StoredProcedureRequestContext context);

        /// <summary>
        /// Authorization check on mutation fields provided in a GraphQL Mutation request.
        /// </summary>
        /// <param name="context">Middleware context of the mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <param name="entityName">entity name</param>
        /// <param name="mutationOperation">mutation operation</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public void AuthorizeMutationFields(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string entityName,
            EntityActionOperation mutationOperation);
    }
}
