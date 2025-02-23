// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.AuthenticationHelpers;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using Azure.DataApiBuilder.Service.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Services.OpenAPI;
using Azure.DataApiBuilder.Service.Tests.Authorization;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using HotChocolate;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;
using Npgsql;
using VerifyMSTest;
using static Azure.DataApiBuilder.Config.RuntimeConfigLoader;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    [TestClass]
    public class ConfigurationTests
        : VerifyBase
    {
        private const string COSMOS_ENVIRONMENT = TestCategory.COSMOSDBNOSQL;
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;
        private const string MYSQL_ENVIRONMENT = TestCategory.MYSQL;
        private const string POSTGRESQL_ENVIRONMENT = TestCategory.POSTGRESQL;
        private const string POST_STARTUP_CONFIG_ENTITY = "Book";
        private const string POST_STARTUP_CONFIG_ENTITY_SOURCE = "books";
        private const string POST_STARTUP_CONFIG_ROLE = "PostStartupConfigRole";
        private const string COSMOS_DATABASE_NAME = "config_db";
        private const string CUSTOM_CONFIG_FILENAME = "custom-config.json";
        private const string OPENAPI_SWAGGER_ENDPOINT = "swagger";
        private const string OPENAPI_DOCUMENT_ENDPOINT = "openapi";
        private const string BROWSER_USER_AGENT_HEADER = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36";
        private const string BROWSER_ACCEPT_HEADER = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9";

        private const int RETRY_COUNT = 5;
        private const int RETRY_WAIT_SECONDS = 1;

        // TODO: Remove the old endpoint once we've updated all callers to use the new one.
        private const string CONFIGURATION_ENDPOINT = "/configuration";
        private const string CONFIGURATION_ENDPOINT_V2 = "/configuration/v2";

        /// <summary>
        /// A valid REST API request body with correct parameter types for all the fields.
        /// </summary>
        public const string REQUEST_BODY_WITH_CORRECT_PARAM_TYPES = @"
                    {
                        ""title"": ""New book"",
                        ""publisher_id"": 1234
                    }
                ";

        /// <summary>
        /// An invalid REST API request body with incorrect parameter type for publisher_id field.
        /// </summary>
        public const string REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES = @"
                    {
                        ""title"": ""New book"",
                        ""publisher_id"": ""one""
                    }
                ";

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// When updating config during runtime is possible, then For invalid config the Application continues to
        /// accept request with status code of 503.
        /// But if invalid config is provided during startup, ApplicationException is thrown
        /// and application exits.
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { }, true, DisplayName = "No config returns 503 - config file flag absent")]
        [DataRow(new string[] { "--ConfigFileName=" }, true, DisplayName = "No config returns 503 - empty config file option")]
        [DataRow(new string[] { }, false, DisplayName = "Throws Application exception")]
        [TestMethod("Validates that queries before runtime is configured returns a 503 in hosting scenario whereas an application exception when run through CLI")]
        public async Task TestNoConfigReturnsServiceUnavailable(
            string[] args,
            bool isUpdateableRuntimeConfig)
        {
            TestServer server;

            try
            {
                if (isUpdateableRuntimeConfig)
                {
                    server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(args));
                }
                else
                {
                    server = new(Program.CreateWebHostBuilder(args));
                }

                HttpClient httpClient = server.CreateClient();
                HttpResponseMessage result = await httpClient.GetAsync("/graphql");
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, result.StatusCode);
            }
            catch (Exception e)
            {
                Assert.IsFalse(isUpdateableRuntimeConfig);
                Assert.AreEqual(typeof(ApplicationException), e.GetType());
                Assert.AreEqual(
                    $"Could not initialize the engine with the runtime config file: {DEFAULT_CONFIG_FILE_NAME}",
                    e.Message);
            }
        }

        /// <summary>
        /// Verify that https redirection is disabled when --no-https-redirect flag is passed  through CLI.
        /// We check if IsHttpsRedirectionDisabled is set to true with --no-https-redirect flag.
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { "" }, false, DisplayName = "Https redirection allowed")]
        [DataRow(new string[] { Startup.NO_HTTPS_REDIRECT_FLAG }, true, DisplayName = "Http redirection disabled")]
        [TestMethod("Validates that https redirection is disabled when --no-https-redirect option is used when engine is started through CLI")]
        public void TestDisablingHttpsRedirection(
            string[] args,
            bool expectedIsHttpsRedirectionDisabled)
        {
            Program.CreateWebHostBuilder(args).Build();
            Assert.AreEqual(expectedIsHttpsRedirectionDisabled, Program.IsHttpsRedirectionDisabled);
        }

        /// <summary>
        /// Checks correct serialization and deserialization of Source Type from
        /// Enum to String and vice-versa.
        /// Consider both cases for source as an object and as a string
        /// </summary>
        [DataTestMethod]
        [DataRow(true, EntitySourceType.StoredProcedure, "stored-procedure", DisplayName = "source is a stored-procedure")]
        [DataRow(true, EntitySourceType.Table, "table", DisplayName = "source is a table")]
        [DataRow(true, EntitySourceType.View, "view", DisplayName = "source is a view")]
        [DataRow(false, null, null, DisplayName = "source is just string")]
        public void TestCorrectSerializationOfSourceObject(
            bool isDatabaseObjectSource,
            EntitySourceType sourceObjectType,
            string sourceTypeName)
        {
            RuntimeConfig runtimeConfig;
            if (isDatabaseObjectSource)
            {
                EntitySource entitySource = new(
                    Type: sourceObjectType,
                    Object: "sourceName",
                    Parameters: null,
                    KeyFields: null
                );
                runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                    entityName: "MyEntity",
                    entitySource: entitySource,
                    roleName: "Anonymous",
                    operation: EntityActionOperation.All
                );
            }
            else
            {
                string entitySource = "sourceName";
                runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                    entityName: "MyEntity",
                    entitySource: entitySource,
                    roleName: "Anonymous",
                    operation: EntityActionOperation.All
                );
            }

            string runtimeConfigJson = runtimeConfig.ToJson();

            if (isDatabaseObjectSource)
            {
                Assert.IsTrue(runtimeConfigJson.Contains(sourceTypeName));
            }

            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(runtimeConfigJson, out RuntimeConfig deserializedRuntimeConfig));

            Assert.IsTrue(deserializedRuntimeConfig.Entities.ContainsKey("MyEntity"));
            Assert.AreEqual("sourceName", deserializedRuntimeConfig.Entities["MyEntity"].Source.Object);

            if (isDatabaseObjectSource)
            {
                Assert.AreEqual(sourceObjectType, deserializedRuntimeConfig.Entities["MyEntity"].Source.Type);
            }
            else
            {
                Assert.AreEqual(EntitySourceType.Table, deserializedRuntimeConfig.Entities["MyEntity"].Source.Type);
            }
        }

        [TestMethod("Validates that once the configuration is set, the config controller isn't reachable."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestConflictAlreadySetConfiguration(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            _ = await httpClient.PostAsync(configurationEndpoint, content);
            ValidateCosmosDbSetup(server);

            HttpResponseMessage result = await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates that the config controller returns a conflict when using local configuration."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestConflictLocalConfiguration(string configurationEndpoint)
        {
            Environment.SetEnvironmentVariable
                (ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            ValidateCosmosDbSetup(server);

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            HttpResponseMessage result =
                await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates setting the configuration at runtime."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestSettingConfigurations(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);
        }

        [TestMethod("Validates an invalid configuration returns a bad request."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestInvalidConfigurationAtRuntime(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint, "invalidString");

            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.BadRequest, postResult.StatusCode);
        }

        [TestMethod("Validates a failure in one of the config updated handlers returns a bad request."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestSettingFailureConfigurations(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            RuntimeConfigProvider runtimeConfigProvider = server.Services.GetService<RuntimeConfigProvider>();
            runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add((_, _) =>
            {
                return Task.FromResult(false);
            });

            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);

            Assert.AreEqual(HttpStatusCode.BadRequest, postResult.StatusCode);
        }

        [TestMethod("Validates that the configuration endpoint doesn't return until all configuration loaded handlers have executed."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestLongRunningConfigUpdatedHandlerConfigurations(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            RuntimeConfigProvider runtimeConfigProvider = server.Services.GetService<RuntimeConfigProvider>();
            bool taskHasCompleted = false;
            runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add(async (_, _) =>
            {
                await Task.Delay(1000);
                taskHasCompleted = true;
                return true;
            });

            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);

            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);
            Assert.IsTrue(taskHasCompleted);
        }

        /// <summary>
        /// Tests that sending configuration to the DAB engine post-startup will properly hydrate
        /// the AuthorizationResolver by:
        /// 1. Validate that pre-configuration hydration requests result in 503 Service Unavailable
        /// 2. Validate that custom configuration hydration succeeds.
        /// 3. Validate that request to protected entity without role membership triggers Authorization Resolver
        /// to reject the request with HTTP 403 Forbidden.
        /// 4. Validate that request to protected entity with required role membership passes authorization requirements
        /// and succeeds with HTTP 200 OK.
        /// Note: This test is database engine agnostic, though requires denoting a database environment to fetch a usable
        /// connection string to complete the test. Most applicable to CI/CD test execution.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod("Validates setting the AuthN/Z configuration post-startup during runtime.")]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestSqlSettingPostStartupConfigurations(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            RuntimeConfig configuration = AuthorizationHelpers.InitRuntimeConfig(
                entityName: POST_STARTUP_CONFIG_ENTITY,
                entitySource: POST_STARTUP_CONFIG_ENTITY_SOURCE,
                roleName: POST_STARTUP_CONFIG_ROLE,
                operation: EntityActionOperation.Read,
                includedCols: new HashSet<string>() { "*" });

            JsonContent content = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration, configurationEndpoint);

            HttpResponseMessage preConfigHydrationResult =
                await httpClient.GetAsync($"/{POST_STARTUP_CONFIG_ENTITY}");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, preConfigHydrationResult.StatusCode);

            HttpResponseMessage preConfigOpenApiDocumentExistence =
                await httpClient.GetAsync($"{RestRuntimeOptions.DEFAULT_PATH}/{OPENAPI_DOCUMENT_ENDPOINT}");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, preConfigOpenApiDocumentExistence.StatusCode);

            // SwaggerUI (OpenAPI user interface) is not made available in production/hosting mode.
            HttpResponseMessage preConfigOpenApiSwaggerEndpointAvailability =
                await httpClient.GetAsync($"/{OPENAPI_SWAGGER_ENDPOINT}");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, preConfigOpenApiSwaggerEndpointAvailability.StatusCode);

            HttpStatusCode responseCode = await HydratePostStartupConfiguration(httpClient, content, configurationEndpoint);

            // When the authorization resolver is properly configured, authorization will have failed
            // because no auth headers are present.
            Assert.AreEqual(
                expected: HttpStatusCode.Forbidden,
                actual: responseCode,
                message: "Configuration not yet hydrated after retry attempts..");

            // Sends a GET request to a protected entity which requires a specific role to access.
            // Authorization will pass because proper auth headers are present.
            HttpRequestMessage message = new(method: HttpMethod.Get, requestUri: $"api/{POST_STARTUP_CONFIG_ENTITY}");
            string swaTokenPayload = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(
                addAuthenticated: true,
                specificRole: POST_STARTUP_CONFIG_ROLE);
            message.Headers.Add(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, swaTokenPayload);
            message.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, POST_STARTUP_CONFIG_ROLE);
            HttpResponseMessage authorizedResponse = await httpClient.SendAsync(message);
            Assert.AreEqual(expected: HttpStatusCode.OK, actual: authorizedResponse.StatusCode);

            // OpenAPI document is created during config hydration and
            // is made available after config hydration completes.
            HttpResponseMessage postConfigOpenApiDocumentExistence =
                await httpClient.GetAsync($"{RestRuntimeOptions.DEFAULT_PATH}/{OPENAPI_DOCUMENT_ENDPOINT}");
            Assert.AreEqual(HttpStatusCode.OK, postConfigOpenApiDocumentExistence.StatusCode);

            // SwaggerUI (OpenAPI user interface) is not made available in production/hosting mode.
            // HTTP 400 - BadRequest because when SwaggerUI is disabled, the endpoint is not mapped
            // and the request is processed and failed by the RestService.
            HttpResponseMessage postConfigOpenApiSwaggerEndpointAvailability =
                await httpClient.GetAsync($"/{OPENAPI_SWAGGER_ENDPOINT}");
            Assert.AreEqual(HttpStatusCode.BadRequest, postConfigOpenApiSwaggerEndpointAvailability.StatusCode);
        }

        [TestMethod("Validates that local CosmosDB_NoSQL settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public void TestLoadingLocalCosmosSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates access token is correctly loaded when Account Key is not present for Cosmos."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestLoadingAccessTokenForCosmosClient(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint, null, true);

            HttpResponseMessage authorizedResponse = await httpClient.PostAsync(configurationEndpoint, content);

            Assert.AreEqual(expected: HttpStatusCode.OK, actual: authorizedResponse.StatusCode);
            CosmosClientProvider cosmosClientProvider = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
            Assert.IsNotNull(cosmosClientProvider);
            Assert.IsNotNull(cosmosClientProvider.Client);
        }

        [TestMethod("Validates that local MsSql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.MSSQL)]
        public void TestLoadingLocalMsSqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(MsSqlQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<SqlConnection>));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(MsSqlMetadataProvider));
        }

        [TestMethod("Validates that local PostgreSql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.POSTGRESQL)]
        public void TestLoadingLocalPostgresSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, POSTGRESQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(PostgresQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<NpgsqlConnection>));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(PostgreSqlMetadataProvider));
        }

        [TestMethod("Validates that local MySql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.MYSQL)]
        public void TestLoadingLocalMySqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MYSQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(MySqlQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<MySqlConnection>));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(MySqlMetadataProvider));
        }

        [TestMethod("Validates that trying to override configs that are already set fail."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestOverridingLocalSettingsFails(string configurationEndpoint)
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();

            JsonContent config = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            HttpResponseMessage postResult = await client.PostAsync(configurationEndpoint, config);
            Assert.AreEqual(HttpStatusCode.Conflict, postResult.StatusCode);
        }

        [TestMethod("Validates that setting the configuration at runtime will instantiate the proper classes."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestSettingConfigurationCreatesCorrectClasses(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            HttpResponseMessage postResult = await client.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            ValidateCosmosDbSetup(server);
            RuntimeConfigProvider configProvider = server.Services.GetService<RuntimeConfigProvider>();

            Assert.IsNotNull(configProvider, "Configuration Provider shouldn't be null after setting the configuration at runtime.");
            Assert.IsTrue(configProvider.TryGetConfig(out RuntimeConfig configuration), "TryGetConfig should return true when the config is set.");
            Assert.IsNotNull(configuration, "Config returned should not be null.");

            ConfigurationPostParameters expectedParameters = GetCosmosConfigurationParameters();
            Assert.AreEqual(DatabaseType.CosmosDB_NoSQL, configuration.DataSource.DatabaseType, "Expected CosmosDB_NoSQL database type after configuring the runtime with CosmosDB_NoSQL settings.");
            Assert.AreEqual(expectedParameters.Schema, configuration.DataSource.GetTypedOptions<CosmosDbNoSQLDataSourceOptions>().GraphQLSchema, "Expected the schema in the configuration to match the one sent to the configuration endpoint.");

            // Don't use Assert.AreEqual, because a failure will print the entire connection string in the error message.
            Assert.IsTrue(expectedParameters.ConnectionString == configuration.DataSource.ConnectionString, "Expected the connection string in the configuration to match the one sent to the configuration endpoint.");
            string db = configuration.DataSource.GetTypedOptions<CosmosDbNoSQLDataSourceOptions>().Database;
            Assert.AreEqual(COSMOS_DATABASE_NAME, db, "Expected the database name in the runtime config to match the one sent to the configuration endpoint.");
        }

        [TestMethod("Validates that an exception is thrown if there's a null model in filter parser.")]
        public void VerifyExceptionOnNullModelinFilterParser()
        {
            ODataParser parser = new();
            try
            {
                // FilterParser has no model so we expect exception
                parser.GetFilterClause(filterQueryString: string.Empty, resourcePath: string.Empty);
                Assert.Fail();
            }
            catch (DataApiBuilderException exception)
            {
                Assert.AreEqual("The runtime has not been initialized with an Edm model.", exception.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
            }
        }

        /// <summary>
        /// This test reads the dab-config.MsSql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of MsSql config file succeeds."), TestCategory(TestCategory.MSSQL)]
        public Task TestReadingRuntimeConfigForMsSql()
        {
            return ConfigFileDeserializationValidationHelper(File.ReadAllText($"{RuntimeConfigLoader.CONFIGFILE_NAME}.{MSSQL_ENVIRONMENT}{RuntimeConfigLoader.CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// This test reads the dab-config.MySql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of MySql config file succeeds."), TestCategory(TestCategory.MYSQL)]
        public Task TestReadingRuntimeConfigForMySql()
        {
            return ConfigFileDeserializationValidationHelper(File.ReadAllText($"{RuntimeConfigLoader.CONFIGFILE_NAME}.{MYSQL_ENVIRONMENT}{RuntimeConfigLoader.CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// This test reads the dab-config.PostgreSql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of PostgreSql config file succeeds."), TestCategory(TestCategory.POSTGRESQL)]
        public Task TestReadingRuntimeConfigForPostgreSql()
        {
            return ConfigFileDeserializationValidationHelper(File.ReadAllText($"{RuntimeConfigLoader.CONFIGFILE_NAME}.{POSTGRESQL_ENVIRONMENT}{RuntimeConfigLoader.CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// This test reads the dab-config.CosmosDb_NoSql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of the CosmosDB_NoSQL config file succeeds."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public Task TestReadingRuntimeConfigForCosmos()
        {
            return ConfigFileDeserializationValidationHelper(File.ReadAllText($"{RuntimeConfigLoader.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigLoader.CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// Helper method to validate the deserialization of the "entities" section of the config file
        /// This is used in unit tests that validate the deserialization of the config files
        /// </summary>
        /// <param name="runtimeConfig"></param>
        private Task ConfigFileDeserializationValidationHelper(string jsonString)
        {
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(jsonString, out RuntimeConfig runtimeConfig), "Deserialization of the config file failed.");
            return Verify(runtimeConfig);
        }

        /// <summary>
        /// This function verifies command line configuration provider takes higher
        /// precedence than default configuration file dab-config.json
        /// </summary>
        [TestMethod("Validates command line configuration provider."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public void TestCommandLineConfigurationProvider()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            string[] args = new[]
            {
                $"--ConfigFileName={RuntimeConfigLoader.CONFIGFILE_NAME}." +
                $"{COSMOS_ENVIRONMENT}{RuntimeConfigLoader.CONFIG_EXTENSION}"
            };

            TestServer server = new(Program.CreateWebHostBuilder(args));

            ValidateCosmosDbSetup(server);
        }

        /// <summary>
        /// This function verifies the environment variable DAB_ENVIRONMENT
        /// takes precedence than ASPNETCORE_ENVIRONMENT for the configuration file.
        /// </summary>
        [TestMethod("Validates precedence is given to DAB_ENVIRONMENT environment variable name."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public void TestRuntimeEnvironmentVariable()
        {
            Environment.SetEnvironmentVariable(
                ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            Environment.SetEnvironmentVariable(
                RuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);

            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates the runtime configuration file."), TestCategory(TestCategory.MSSQL)]
        public void TestConfigIsValid()
        {
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);
            RuntimeConfigLoader configPath = TestHelper.GetRuntimeConfigLoader();
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(configPath);

            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            IConfigValidator configValidator =
                new RuntimeConfigValidator(
                    configProvider,
                    new MockFileSystem(),
                    configValidatorLogger.Object);

            configValidator.ValidateConfig();
            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Set the connection string to an invalid value and expect the service to be unavailable
        /// since without this env var, it would be available - guaranteeing this env variable
        /// has highest precedence irrespective of what the connection string is in the config file.
        /// Verifying the Exception thrown.
        /// </summary>
        [TestMethod($"Validates that environment variable {RuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING} has highest precedence."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public void TestConnectionStringEnvVarHasHighestPrecedence()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            Environment.SetEnvironmentVariable(
                RuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING,
                "Invalid Connection String");

            try
            {
                TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
                _ = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
                Assert.Fail($"{RuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING} is not given highest precedence");
            }
            catch (Exception e)
            {
                Assert.AreEqual(typeof(ApplicationException), e.GetType());
                Assert.AreEqual(
                    $"Could not initialize the engine with the runtime config file: " +
                    $"{RuntimeConfigLoader.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigLoader.CONFIG_EXTENSION}",
                    e.Message);
            }
        }

        /// <summary>
        /// Test to verify the precedence logic for config file based on Environment variables.
        /// </summary>
        [DataTestMethod]
        [DataRow("HostTest", "Test", false, $"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}", DisplayName = "hosting and dab environment set, without considering overrides.")]
        [DataRow("HostTest", "", false, $"{CONFIGFILE_NAME}.HostTest{CONFIG_EXTENSION}", DisplayName = "only hosting environment set, without considering overrides.")]
        [DataRow("", "Test1", false, $"{CONFIGFILE_NAME}.Test1{CONFIG_EXTENSION}", DisplayName = "only dab environment set, without considering overrides.")]
        [DataRow("", "Test2", true, $"{CONFIGFILE_NAME}.Test2.overrides{CONFIG_EXTENSION}", DisplayName = "only dab environment set, considering overrides.")]
        [DataRow("HostTest1", "", true, $"{CONFIGFILE_NAME}.HostTest1.overrides{CONFIG_EXTENSION}", DisplayName = "only hosting environment set, considering overrides.")]
        public void TestGetConfigFileNameForEnvironment(
            string hostingEnvironmentValue,
            string environmentValue,
            bool considerOverrides,
            string expectedRuntimeConfigFile)
        {
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(expectedRuntimeConfigFile, new MockFileData(string.Empty));
            RuntimeConfigLoader runtimeConfigLoader = new(fileSystem);

            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, hostingEnvironmentValue);
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
            string actualRuntimeConfigFile = runtimeConfigLoader.GetFileNameForEnvironment(hostingEnvironmentValue, considerOverrides);
            Assert.AreEqual(expectedRuntimeConfigFile, actualRuntimeConfigFile);
        }

        /// <summary>
        /// Test different graphql endpoints in different host modes
        /// when accessed interactively via browser.
        /// </summary>
        /// <param name="endpoint">The endpoint route</param>
        /// <param name="hostMode">The mode in which the service is executing.</param>
        /// <param name="expectedStatusCode">Expected Status Code.</param>
        /// <param name="expectedContent">The expected phrase in the response body.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow("/graphql/", HostMode.Development, HttpStatusCode.OK, "Banana Cake Pop",
            DisplayName = "GraphQL endpoint with no query in development mode.")]
        [DataRow("/graphql", HostMode.Production, HttpStatusCode.BadRequest,
            "Either the parameter query or the parameter id has to be set",
            DisplayName = "GraphQL endpoint with no query in production mode.")]
        [DataRow("/graphql/ui", HostMode.Development, HttpStatusCode.NotFound,
            DisplayName = "Default BananaCakePop in development mode.")]
        [DataRow("/graphql/ui", HostMode.Production, HttpStatusCode.NotFound,
            DisplayName = "Default BananaCakePop in production mode.")]
        [DataRow("/graphql?query={book_by_pk(id: 1){title}}",
            HostMode.Development, HttpStatusCode.Moved,
            DisplayName = "GraphQL endpoint with query in development mode.")]
        [DataRow("/graphql?query={book_by_pk(id: 1){title}}",
            HostMode.Production, HttpStatusCode.OK, "data",
            DisplayName = "GraphQL endpoint with query in production mode.")]
        [DataRow(RestController.REDIRECTED_ROUTE, HostMode.Development, HttpStatusCode.BadRequest,
            "GraphQL request redirected to favicon.ico.",
            DisplayName = "Redirected endpoint in development mode.")]
        [DataRow(RestController.REDIRECTED_ROUTE, HostMode.Production, HttpStatusCode.BadRequest,
            "GraphQL request redirected to favicon.ico.",
            DisplayName = "Redirected endpoint in production mode.")]
        public async Task TestInteractiveGraphQLEndpoints(
            string endpoint,
            HostMode HostMode,
            HttpStatusCode expectedStatusCode,
            string expectedContent = "")
        {
            const string CUSTOM_CONFIG = "custom-config.json";
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);
            FileSystem fileSystem = new();
            RuntimeConfigLoader loader = new(fileSystem);
            loader.TryLoadKnownConfig(out RuntimeConfig config);

            RuntimeConfig configWithCustomHostMode = config with
            {
                Runtime = config.Runtime with
                {
                    Host = config.Runtime.Host with { Mode = HostMode }
                }
            };
            File.WriteAllText(CUSTOM_CONFIG, configWithCustomHostMode.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            {
                HttpRequestMessage request = new(HttpMethod.Get, endpoint);

                // Adding the following headers simulates an interactive browser request.
                request.Headers.Add("user-agent", BROWSER_USER_AGENT_HEADER);
                request.Headers.Add("accept", BROWSER_ACCEPT_HEADER);

                HttpResponseMessage response = await client.SendAsync(request);
                Assert.AreEqual(expectedStatusCode, response.StatusCode);
                string actualBody = await response.Content.ReadAsStringAsync();
                Assert.IsTrue(actualBody.Contains(expectedContent));

                TestHelper.UnsetAllDABEnvironmentVariables();
            }
        }

        /// <summary>
        /// Tests that the custom path rewriting middleware properly rewrites the
        /// first segment of a path (/segment1/.../segmentN) when the segment matches
        /// the custom configured GraphQLEndpoint.
        /// Note: The GraphQL service is always internally mapped to /graphql
        /// </summary>
        /// <param name="graphQLConfiguredPath">The custom configured GraphQL path in configuration</param>
        /// <param name="requestPath">The path used in the web request executed in the test.</param>
        /// <param name="expectedStatusCode">Expected Http success/error code</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow("/graphql", "/gql", HttpStatusCode.BadRequest, DisplayName = "Request to non-configured graphQL endpoint is handled by REST controller.")]
        [DataRow("/graphql", "/graphql", HttpStatusCode.OK, DisplayName = "Request to configured default GraphQL endpoint succeeds, path not rewritten.")]
        [DataRow("/gql", "/gql/additionalURLsegment", HttpStatusCode.OK, DisplayName = "GraphQL request path (with extra segments) rewritten to match internally set GraphQL endpoint /graphql.")]
        [DataRow("/gql", "/gql", HttpStatusCode.OK, DisplayName = "GraphQL request path rewritten to match internally set GraphQL endpoint /graphql.")]
        [DataRow("/gql", "/api/book", HttpStatusCode.NotFound, DisplayName = "Non-GraphQL request's path is not rewritten and is handled by REST controller.")]
        [DataRow("/gql", "/graphql", HttpStatusCode.NotFound, DisplayName = "Requests to default/internally set graphQL endpoint fail when configured endpoint differs.")]
        public async Task TestPathRewriteMiddlewareForGraphQL(
            string graphQLConfiguredPath,
            string requestPath,
            HttpStatusCode expectedStatusCode)
        {
            GraphQLRuntimeOptions graphqlOptions = new(Path: graphQLConfiguredPath);

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), new());

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, new());
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[] { $"--ConfigFileName={CUSTOM_CONFIG}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            string query = @"{
                    book_by_pk(id: 1) {
                       id,
                       title,
                       publisher_id
                    }
                }";

            var payload = new { query };

            HttpRequestMessage request = new(HttpMethod.Post, requestPath)
            {
                Content = JsonContent.Create(payload)
            };

            HttpResponseMessage response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(expectedStatusCode, response.StatusCode);
        }

        /// <summary>
        /// Validates the error message that is returned for REST requests with incorrect parameter type
        /// when the engine is running in Production mode. The error messages in Production mode is
        /// very generic to not reveal information about the underlying database objects backing the entity.
        /// This test runs against a MsSql database. However, generic error messages will be returned in Production
        /// mode when run against PostgreSql and MySql databases.
        /// </summary>
        /// <param name="requestType">Type of REST request</param>
        /// <param name="requestPath">Endpoint for the REST request</param>
        /// <param name="expectedErrorMessage">Right error message that should be shown to the end user</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(SupportedHttpVerb.Get, "/api/Book/id/one", null, "Invalid value provided for field: id", DisplayName = "Validates the error message for a GET request with incorrect primary key parameter type on a table in production mode")]
        [DataRow(SupportedHttpVerb.Get, "/api/books_view_all/id/one", null, "Invalid value provided for field: id", DisplayName = "Validates the error message for a GET request with incorrect primary key parameter type on a view in production mode")]
        [DataRow(SupportedHttpVerb.Get, "/api/GetBook?id=one", REQUEST_BODY_WITH_CORRECT_PARAM_TYPES, "Invalid value provided for field: id", DisplayName = "Validates the error message for a GET request on a stored-procedure with incorrect parameter type in production mode")]
        [DataRow(SupportedHttpVerb.Get, "/api/GQLmappings/column1/one", null, "Invalid value provided for field: column1", DisplayName = "Validates the error message for a GET request with incorrect primary key parameter type with alias defined for primary key column on a table in production mode")]
        [DataRow(SupportedHttpVerb.Post, "/api/Book", REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES, "Invalid value provided for field: publisher_id", DisplayName = "Validates the error message for a POST request with incorrect parameter type in the request body on a table in production mode")]
        [DataRow(SupportedHttpVerb.Put, "/api/Book/id/one", REQUEST_BODY_WITH_CORRECT_PARAM_TYPES, "Invalid value provided for field: id", DisplayName = "Validates the error message for a PUT request with incorrect primary key parameter type on a table in production mode")]
        [DataRow(SupportedHttpVerb.Put, "/api/Book/id/1", REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES, "Invalid value provided for field: publisher_id", DisplayName = "Validates the error message for a bad PUT request with incorrect parameter type in the request body on a table in production mode")]
        [DataRow(SupportedHttpVerb.Patch, "/api/Book/id/one", REQUEST_BODY_WITH_CORRECT_PARAM_TYPES, "Invalid value provided for field: id", DisplayName = "Validates the error message for a PATCH request with incorrect primary key parameter type on a table in production mode")]
        [DataRow(SupportedHttpVerb.Patch, "/api/Book/id/1", REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES, "Invalid value provided for field: publisher_id", DisplayName = "Validates the error message for a PATCH request with incorrect parameter type in the request body on a table in production mode")]
        [DataRow(SupportedHttpVerb.Delete, "/api/Book/id/one", REQUEST_BODY_WITH_CORRECT_PARAM_TYPES, "Invalid value provided for field: id", DisplayName = "Validates the error message for a DELETE request with incorrect primary key parameter type on a table in production mode")]
        public async Task TestGenericErrorMessageForRestApiInProductionMode(
            SupportedHttpVerb requestType,
            string requestPath,
            string requestBody,
            string expectedErrorMessage)
        {
            const string CUSTOM_CONFIG = "custom-config.json";
            TestHelper.ConstructNewConfigWithSpecifiedHostMode(CUSTOM_CONFIG, HostMode.Production, TestCategory.MSSQL);
            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                HttpMethod httpMethod = SqlTestHelper.ConvertRestMethodToHttpMethod(requestType);
                HttpRequestMessage request;
                if (requestType is SupportedHttpVerb.Get || requestType is SupportedHttpVerb.Delete)
                {
                    request = new(httpMethod, requestPath);
                }
                else
                {
                    request = new(httpMethod, requestPath)
                    {
                        Content = JsonContent.Create(requestBody)
                    };
                }

                HttpResponseMessage response = await client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.IsTrue(body.Contains(expectedErrorMessage));
            }
        }

        /// <summary>
        /// Tests that the when Rest or GraphQL is disabled Globally,
        /// any requests made will get a 404 response.
        /// </summary>
        /// <param name="isRestEnabled">The custom configured REST enabled property in configuration.</param>
        /// <param name="isGraphQLEnabled">The custom configured GraphQL enabled property in configuration.</param>
        /// <param name="expectedStatusCodeForREST">Expected HTTP status code code for the Rest request</param>
        /// <param name="expectedStatusCodeForGraphQL">Expected HTTP status code code for the GraphQL request</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(true, true, HttpStatusCode.OK, HttpStatusCode.OK, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Both Rest and GraphQL endpoints enabled globally")]
        [DataRow(true, false, HttpStatusCode.OK, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest enabled and GraphQL endpoints disabled globally")]
        [DataRow(false, true, HttpStatusCode.NotFound, HttpStatusCode.OK, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest disabled and GraphQL endpoints enabled globally")]
        [DataRow(true, true, HttpStatusCode.OK, HttpStatusCode.OK, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Both Rest and GraphQL endpoints enabled globally")]
        [DataRow(true, false, HttpStatusCode.OK, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest enabled and GraphQL endpoints disabled globally")]
        [DataRow(false, true, HttpStatusCode.NotFound, HttpStatusCode.OK, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest disabled and GraphQL endpoints enabled globally")]
        public async Task TestGlobalFlagToEnableRestAndGraphQLForHostedAndNonHostedEnvironment(
            bool isRestEnabled,
            bool isGraphQLEnabled,
            HttpStatusCode expectedStatusCodeForREST,
            HttpStatusCode expectedStatusCodeForGraphQL,
            string configurationEndpoint)
        {
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: isGraphQLEnabled);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: isRestEnabled);

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), new());

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions);
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            // Non-Hosted Scenario
            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string query = @"{
                    book_by_pk(id: 1) {
                       id,
                       title,
                       publisher_id
                    }
                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);
                Assert.AreEqual(expectedStatusCodeForGraphQL, graphQLResponse.StatusCode);

                HttpRequestMessage restRequest = new(HttpMethod.Get, "/api/Book");
                HttpResponseMessage restResponse = await client.SendAsync(restRequest);
                Assert.AreEqual(expectedStatusCodeForREST, restResponse.StatusCode);
            }

            // Hosted Scenario
            // Instantiate new server with no runtime config for post-startup configuration hydration tests.
            using (TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>())))
            using (HttpClient client = server.CreateClient())
            {
                JsonContent content = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration, configurationEndpoint);

                HttpResponseMessage postResult =
                await client.PostAsync(configurationEndpoint, content);
                Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

                HttpStatusCode restResponseCode = await GetRestResponsePostConfigHydration(client);

                Assert.AreEqual(expected: expectedStatusCodeForREST, actual: restResponseCode);

                HttpStatusCode graphqlResponseCode = await GetGraphQLResponsePostConfigHydration(client);

                Assert.AreEqual(expected: expectedStatusCodeForGraphQL, actual: graphqlResponseCode);

            }
        }

        /// <summary>
        /// Engine supports config with some views that do not have keyfields specified in the config for MsSQL.
        /// This Test validates that support. It creates a custom config with a view and no keyfields specified.
        /// It checks both Rest and GraphQL queries are tested to return Success.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task TestEngineSupportViewsWithoutKeyFieldsInConfigForMsSQL()
        {
            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), new());
            Entity viewEntity = new(
                Source: new("books_view_all", EntitySourceType.Table, null, null),
                Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                GraphQL: new("", ""),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
            );

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new(), viewEntity, "books_view_all");

            const string CUSTOM_CONFIG = "custom-config.json";

            File.WriteAllText(
                CUSTOM_CONFIG,
                configuration.ToJson());

            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string query = @"{
                    books_view_alls {
                        items{
                            id
                            title
                        }
                    }
                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);
                Assert.AreEqual(HttpStatusCode.OK, graphQLResponse.StatusCode);
                string body = await graphQLResponse.Content.ReadAsStringAsync();
                Assert.IsFalse(body.Contains("errors")); // In GraphQL, All errors end up in the errors array, no matter what kind of error they are.

                HttpRequestMessage restRequest = new(HttpMethod.Get, "/api/books_view_all");
                HttpResponseMessage restResponse = await client.SendAsync(restRequest);
                Assert.AreEqual(HttpStatusCode.OK, restResponse.StatusCode);
            }
        }

        /// <summary>
        /// Tests that Startup.cs properly handles EasyAuth authentication configuration.
        /// AppService as Identity Provider while in Production mode will result in startup error.
        /// An Azure AppService environment has environment variables on the host which indicate
        /// the environment is, in fact, an AppService environment.
        /// </summary>
        /// <param name="hostMode">HostMode in Runtime config - Development or Production.</param>
        /// <param name="authType">EasyAuth auth type - AppService or StaticWebApps.</param>
        /// <param name="setEnvVars">Whether to set the AppService host environment variables.</param>
        /// <param name="expectError">Whether an error is expected.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(HostMode.Development, EasyAuthType.AppService, false, false, DisplayName = "AppService Dev - No EnvVars - No Error")]
        [DataRow(HostMode.Development, EasyAuthType.AppService, true, false, DisplayName = "AppService Dev - EnvVars - No Error")]
        [DataRow(HostMode.Production, EasyAuthType.AppService, false, true, DisplayName = "AppService Prod - No EnvVars - Error")]
        [DataRow(HostMode.Production, EasyAuthType.AppService, true, false, DisplayName = "AppService Prod - EnvVars - Error")]
        [DataRow(HostMode.Development, EasyAuthType.StaticWebApps, false, false, DisplayName = "SWA Dev - No EnvVars - No Error")]
        [DataRow(HostMode.Development, EasyAuthType.StaticWebApps, true, false, DisplayName = "SWA Dev - EnvVars - No Error")]
        [DataRow(HostMode.Production, EasyAuthType.StaticWebApps, false, false, DisplayName = "SWA Prod - No EnvVars - No Error")]
        [DataRow(HostMode.Production, EasyAuthType.StaticWebApps, true, false, DisplayName = "SWA Prod - EnvVars - No Error")]
        public void TestProductionModeAppServiceEnvironmentCheck(HostMode hostMode, EasyAuthType authType, bool setEnvVars, bool expectError)
        {
            // Clears or sets App Service Environment Variables based on test input.
            Environment.SetEnvironmentVariable(AppServiceAuthenticationInfo.APPSERVICESAUTH_ENABLED_ENVVAR, setEnvVars ? "true" : null);
            Environment.SetEnvironmentVariable(AppServiceAuthenticationInfo.APPSERVICESAUTH_IDENTITYPROVIDER_ENVVAR, setEnvVars ? "AzureActiveDirectory" : null);
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);

            FileSystem fileSystem = new();
            RuntimeConfigLoader loader = new(fileSystem);

            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(loader);
            RuntimeConfig config = configProvider.GetConfig();

            // Setup configuration
            AuthenticationOptions AuthenticationOptions = new(Provider: authType.ToString(), null);
            RuntimeOptions runtimeOptions = new(
                Rest: new(),
                GraphQL: new(),
                Host: new(null, AuthenticationOptions, hostMode)
            );
            RuntimeConfig configWithCustomHostMode = config with { Runtime = runtimeOptions };

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configWithCustomHostMode.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            // This test only checks for startup errors, so no requests are sent to the test server.
            try
            {
                using TestServer server = new(Program.CreateWebHostBuilder(args));
                Assert.IsFalse(expectError, message: "Expected error faulting AppService config in production mode.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(expectError, message: ex.Message);
                Assert.AreEqual(AppServiceAuthenticationInfo.APPSERVICE_PROD_MISSING_ENV_CONFIG, ex.Message);
            }
        }

        /// <summary>
        /// Integration test that validates schema introspection requests fail
        /// when allow-introspection is false in the runtime configuration.
        /// TestCategory is required for CI/CD pipeline to inject a connection string.
        /// </summary>
        /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/6b2cfc94695cb65e2f68f5d8deb576e48397a98a/src/HotChocolate/Core/src/Abstractions/ErrorCodes.cs#L287"/>
        [TestCategory(TestCategory.MSSQL)]
        [DataTestMethod]
        [DataRow(false, true, "Introspection is not allowed for the current request.", CONFIGURATION_ENDPOINT, DisplayName = "Disabled introspection returns GraphQL error.")]
        [DataRow(true, false, null, CONFIGURATION_ENDPOINT, DisplayName = "Enabled introspection does not return introspection forbidden error.")]
        [DataRow(false, true, "Introspection is not allowed for the current request.", CONFIGURATION_ENDPOINT_V2, DisplayName = "Disabled introspection returns GraphQL error.")]
        [DataRow(true, false, null, CONFIGURATION_ENDPOINT_V2, DisplayName = "Enabled introspection does not return introspection forbidden error.")]
        public async Task TestSchemaIntrospectionQuery(bool enableIntrospection, bool expectError, string errorMessage, string configurationEndpoint)
        {
            GraphQLRuntimeOptions graphqlOptions = new(AllowIntrospection: enableIntrospection);
            RestRuntimeOptions restRuntimeOptions = new();

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), new());

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions);
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                await ExecuteGraphQLIntrospectionQueries(server, client, expectError);
            }

            // Instantiate new server with no runtime config for post-startup configuration hydration tests.
            using (TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>())))
            using (HttpClient client = server.CreateClient())
            {
                JsonContent content = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration, configurationEndpoint);
                HttpStatusCode responseCode = await HydratePostStartupConfiguration(client, content, configurationEndpoint);

                Assert.AreEqual(expected: HttpStatusCode.OK, actual: responseCode, message: "Configuration hydration failed.");

                await ExecuteGraphQLIntrospectionQueries(server, client, expectError);
            }
        }

        /// <summary>
        /// Indirectly tests IsGraphQLReservedName(). Runtime config provided to engine which will
        /// trigger SqlMetadataProvider PopulateSourceDefinitionAsync() to pull column metadata from
        /// the table "graphql_incompatible." That table contains columns which collide with reserved GraphQL
        /// introspection field names which begin with double underscore (__).
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [DataTestMethod]
        [DataRow(true, true, "__typeName", "__introspectionField", true, DisplayName = "Name violation, fails since no proper mapping set.")]
        [DataRow(true, true, "__typeName", "columnMapping", false, DisplayName = "Name violation, but OK since proper mapping set.")]
        [DataRow(false, true, null, null, false, DisplayName = "Name violation, but OK since GraphQL globally disabled.")]
        [DataRow(true, false, null, null, false, DisplayName = "Name violation, but OK since GraphQL disabled for entity.")]
        public void TestInvalidDatabaseColumnNameHandling(
            bool globalGraphQLEnabled,
            bool entityGraphQLEnabled,
            string columnName,
            string columnMapping,
            bool expectError)
        {
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: globalGraphQLEnabled);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: true);

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), new());

            // Configure Entity for testing
            Dictionary<string, string> mappings = new()
            {
                { "__introspectionName", "conformingIntrospectionName" }
            };

            if (!string.IsNullOrWhiteSpace(columnMapping))
            {
                mappings.Add(columnName, columnMapping);
            }

            Entity entity = new(
                Source: new("graphql_incompatible", EntitySourceType.Table, null, null),
                Rest: new(Array.Empty<SupportedHttpVerb>(), Enabled: false),
                GraphQL: new("graphql_incompatible", "graphql_incompatibles", entityGraphQLEnabled),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: mappings
            );

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, entity, "graphqlNameCompat");
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            try
            {
                using TestServer server = new(Program.CreateWebHostBuilder(args));
                Assert.IsFalse(expectError, message: "Expected startup to fail.");
            }
            catch (Exception ex)
            {
                Assert.IsTrue(expectError, message: "Startup was not expected to fail. " + ex.Message);
            }
        }

        /// <summary>
        /// Test different Swagger endpoints in different host modes when accessed interactively via browser.
        /// Two pass request scheme:
        /// 1 - Send get request to expected Swagger endpoint /swagger
        /// Response - Internally Swagger sends HTTP 301 Moved Permanently with Location header
        /// pointing to exact Swagger page (/swagger/index.html)
        /// 2 - Send GET request to path referred to by Location header in previous response
        /// Response - Successful loading of SwaggerUI HTML, with reference to endpoint used
        /// to retrieve OpenAPI document. This test ensures that Swagger components load, but
        /// does not confirm that a proper OpenAPI document was created.
        /// </summary>
        /// <param name="customRestPath">The custom REST route</param>
        /// <param name="hostModeType">The mode in which the service is executing.</param>
        /// <param name="expectsError">Whether to expect an error.</param>
        /// <param name="expectedStatusCode">Expected Status Code.</param>
        /// <param name="expectedOpenApiTargetContent">Snippet of expected HTML to be emitted from successful page load.
        /// This should note the openapi route that Swagger will use to retrieve the OpenAPI document.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow("/api", HostMode.Development, false, HttpStatusCode.OK, "{\"urls\":[{\"url\":\"/api/openapi\"", DisplayName = "SwaggerUI enabled in development mode.")]
        [DataRow("/custompath", HostMode.Development, false, HttpStatusCode.OK, "{\"urls\":[{\"url\":\"/custompath/openapi\"", DisplayName = "SwaggerUI enabled with custom REST path in development mode.")]
        [DataRow("/api", HostMode.Production, true, HttpStatusCode.BadRequest, "", DisplayName = "SwaggerUI disabled in production mode.")]
        [DataRow("/custompath", HostMode.Production, true, HttpStatusCode.BadRequest, "", DisplayName = "SwaggerUI disabled in production mode with custom REST path.")]
        public async Task OpenApi_InteractiveSwaggerUI(
            string customRestPath,
            HostMode hostModeType,
            bool expectsError,
            HttpStatusCode expectedStatusCode,
            string expectedOpenApiTargetContent)
        {
            string swaggerEndpoint = "/swagger";
            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), new());

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource: dataSource, new(), new(Path: customRestPath));
            configuration = configuration
                with
            {
                Runtime = configuration.Runtime
                with
                {
                    Host = configuration.Runtime.Host
                with
                    { Mode = hostModeType }
                }
            };
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(
                CUSTOM_CONFIG,
                configuration.ToJson());

            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                HttpRequestMessage initialRequest = new(HttpMethod.Get, swaggerEndpoint);

                // Adding the following headers simulates an interactive browser request.
                initialRequest.Headers.Add("user-agent", BROWSER_USER_AGENT_HEADER);
                initialRequest.Headers.Add("accept", BROWSER_ACCEPT_HEADER);

                HttpResponseMessage response = await client.SendAsync(initialRequest);
                if (expectsError)
                {
                    // Redirect(HTTP 301) and follow up request to the returned path
                    // do not occur in a failure scenario. Only HTTP 400 (Bad Request)
                    // is expected.
                    Assert.AreEqual(expectedStatusCode, response.StatusCode);
                }
                else
                {
                    // Swagger endpoint internally configured to reroute from /swagger to /swagger/index.html
                    Assert.AreEqual(HttpStatusCode.MovedPermanently, response.StatusCode);

                    HttpRequestMessage followUpRequest = new(HttpMethod.Get, response.Headers.Location);
                    HttpResponseMessage followUpResponse = await client.SendAsync(followUpRequest);
                    Assert.AreEqual(expectedStatusCode, followUpResponse.StatusCode);

                    // Validate that Swagger requests OpenAPI document using REST path defined in runtime config.
                    string actualBody = await followUpResponse.Content.ReadAsStringAsync();
                    Assert.AreEqual(true, actualBody.Contains(expectedOpenApiTargetContent));
                }
            }
        }

        /// <summary>
        /// Validates the OpenAPI documentor behavior when enabling and disabling the global REST endpoint
        /// for the DAB engine.
        /// Global REST enabled:
        /// - GET to /openapi returns the created OpenAPI document and succeeds with 200 OK.
        /// Global REST disabled:
        /// - GET to /openapi fails with 404 Not Found.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, false, DisplayName = "Global REST endpoint enabled - successful OpenAPI doc retrieval")]
        [DataRow(false, true, DisplayName = "Global REST endpoint disabled - OpenAPI doc does not exist - HTTP404 NotFound.")]
        [TestCategory(TestCategory.MSSQL)]
        public async Task OpenApi_GlobalEntityRestPath(bool globalRestEnabled, bool expectsError)
        {
            // At least one entity is required in the runtime config for the engine to start.
            // Even though this entity is not under test, it must be supplied to the config
            // file creation function.
            Entity requiredEntity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Rest: new(Array.Empty<SupportedHttpVerb>(), Enabled: false),
                GraphQL: new("book", "books"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Dictionary<string, Entity> entityMap = new()
            {
                { "Book", requiredEntity }
            };

            CreateCustomConfigFile(globalRestEnabled: globalRestEnabled, entityMap);

            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            // Setup and send GET request
            HttpRequestMessage readOpenApiDocumentRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{OPENAPI_DOCUMENT_ENDPOINT}");
            HttpResponseMessage response = await client.SendAsync(readOpenApiDocumentRequest);

            // Validate response
            if (expectsError)
            {
                Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            }
            else
            {
                // Process response body
                string responseBody = await response.Content.ReadAsStringAsync();
                Dictionary<string, JsonElement> responseProperties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseBody);

                // Validate response body
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                ValidateOpenApiDocTopLevelPropertiesExist(responseProperties);
            }
        }

        /// <summary>
        /// Validates the behavior of the OpenApiDocumentor when the runtime config has entities with
        /// REST endpoint enabled and disabled.
        /// Enabled -> path should be created
        /// Disabled -> path not created and is excluded from OpenApi document.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public async Task OpenApi_EntityLevelRestEndpoint()
        {
            // Create the entities under test.
            Entity restEnabledEntity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                GraphQL: new("", "", false),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Entity restDisabledEntity = new(
                Source: new("publishers", EntitySourceType.Table, null, null),
                Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS, Enabled: false),
                GraphQL: new("publisher", "publishers", true),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Dictionary<string, Entity> entityMap = new()
            {
                { "Book", restEnabledEntity },
                { "Publisher", restDisabledEntity }
            };

            CreateCustomConfigFile(globalRestEnabled: true, entityMap);

            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            // Setup and send GET request
            HttpRequestMessage readOpenApiDocumentRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{OpenApiDocumentor.OPENAPI_ROUTE}");
            HttpResponseMessage response = await client.SendAsync(readOpenApiDocumentRequest);

            // Parse response metadata
            string responseBody = await response.Content.ReadAsStringAsync();
            Dictionary<string, JsonElement> responseProperties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseBody);

            // Validate response metadata
            ValidateOpenApiDocTopLevelPropertiesExist(responseProperties);
            JsonElement pathsElement = responseProperties[OpenApiDocumentorConstants.TOPLEVELPROPERTY_PATHS];

            // Validate that paths were created for the entity with REST enabled.
            Assert.IsTrue(pathsElement.TryGetProperty("/Book", out _));
            Assert.IsTrue(pathsElement.TryGetProperty("/Book/id/{id}", out _));

            // Validate that paths were not created for the entity with REST disabled.
            Assert.IsFalse(pathsElement.TryGetProperty("/Publisher", out _));
            Assert.IsFalse(pathsElement.TryGetProperty("/Publisher/id/{id}", out _));

            JsonElement componentsElement = responseProperties[OpenApiDocumentorConstants.TOPLEVELPROPERTY_COMPONENTS];
            Assert.IsTrue(componentsElement.TryGetProperty(OpenApiDocumentorConstants.PROPERTY_SCHEMAS, out JsonElement componentSchemasElement));
            // Validate that components were created for the entity with REST enabled.
            Assert.IsTrue(componentSchemasElement.TryGetProperty("Book_NoPK", out _));
            Assert.IsTrue(componentSchemasElement.TryGetProperty("Book", out _));

            // Validate that components were not created for the entity with REST disabled.
            Assert.IsFalse(componentSchemasElement.TryGetProperty("Publisher_NoPK", out _));
            Assert.IsFalse(componentSchemasElement.TryGetProperty("Publisher", out _));
        }

        /// <summary>
        /// Helper function to write custom configuration file. with minimal REST/GraphQL global settings
        /// using the supplied entities.
        /// </summary>
        /// <param name="globalRestEnabled">flag to enable or disabled REST globally.</param>
        /// <param name="entityMap">Collection of entityName -> Entity object.</param>
        private static void CreateCustomConfigFile(bool globalRestEnabled, Dictionary<string, Entity> entityMap)
        {
            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), new());

            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: dataSource,
                Runtime: new(
                    Rest: new(Enabled: globalRestEnabled),
                    GraphQL: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap));

            File.WriteAllText(
                path: CUSTOM_CONFIG_FILENAME,
                contents: runtimeConfig.ToJson());
        }

        /// <summary>
        /// Validates that all the OpenAPI description document's top level properties exist.
        /// A failure here indicates that there was an undetected failure creating the OpenAPI document.
        /// </summary>
        /// <param name="responseComponents">Represent a deserialized JSON result from retrieving the OpenAPI document</param>
        private static void ValidateOpenApiDocTopLevelPropertiesExist(Dictionary<string, JsonElement> responseProperties)
        {
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_OPENAPI));
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_INFO));
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_SERVERS));
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_PATHS));
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_COMPONENTS));
        }

        /// <summary>
        /// Validates that schema introspection requests fail when allow-introspection is false in the runtime configuration.
        /// </summary>
        /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/6b2cfc94695cb65e2f68f5d8deb576e48397a98a/src/HotChocolate/Core/src/Abstractions/ErrorCodes.cs#L287"/>
        private static async Task ExecuteGraphQLIntrospectionQueries(TestServer server, HttpClient client, bool expectError)
        {
            string graphQLQueryName = "__schema";
            string graphQLQuery = @"{
                __schema {
                    types {
                        name
                    }
                }
            }";

            string expectedErrorMessageFragment = "Introspection is not allowed for the current request.";

            try
            {
                RuntimeConfigProvider configProvider = server.Services.GetRequiredService<RuntimeConfigProvider>();

                JsonElement actual = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                    client,
                    configProvider,
                    query: graphQLQuery,
                    queryName: graphQLQueryName,
                    variables: null,
                    clientRoleHeader: null
                    );

                if (expectError)
                {
                    SqlTestHelper.TestForErrorInGraphQLResponse(
                        response: actual.ToString(),
                        message: expectedErrorMessageFragment,
                        statusCode: ErrorCodes.Validation.IntrospectionNotAllowed
                    );
                }
            }
            catch (Exception ex)
            {
                // ExecuteGraphQLRequestAsync will raise an exception when no "data" key
                // exists in the GraphQL JSON response.
                Assert.Fail(message: "No schema metadata in GraphQL response." + ex.Message);
            }
        }

        private static JsonContent GetJsonContentForCosmosConfigRequest(string endpoint, string config = null, bool useAccessToken = false)
        {
            if (CONFIGURATION_ENDPOINT == endpoint)
            {
                ConfigurationPostParameters configParams = GetCosmosConfigurationParameters();
                if (config != null)
                {
                    configParams = configParams with { Configuration = config };
                }

                if (useAccessToken)
                {
                    configParams = configParams with
                    {
                        ConnectionString = "AccountEndpoint=https://localhost:8081/;",
                        AccessToken = GenerateMockJwtToken()
                    };
                }

                return JsonContent.Create(configParams);
            }
            else if (CONFIGURATION_ENDPOINT_V2 == endpoint)
            {
                ConfigurationPostParametersV2 configParams = GetCosmosConfigurationParametersV2();
                if (config != null)
                {
                    configParams = configParams with { Configuration = config };
                }

                if (useAccessToken)
                {
                    // With an invalid access token, when a new instance of CosmosClient is created with that token, it
                    // won't throw an exception.  But when a graphql request is coming in, that's when it throws a 401
                    // exception. To prevent this, CosmosClientProvider parses the token and retrieves the "exp" property
                    // from the token, if it's not valid, then we will throw an exception from our code before it
                    // initiating a client. Uses a valid fake JWT access token for testing purposes.
                    RuntimeConfig overrides = new(null, new DataSource(DatabaseType.CosmosDB_NoSQL, "AccountEndpoint=https://localhost:8081/;", new()), null, null);

                    configParams = configParams with
                    {
                        ConfigurationOverrides = overrides.ToJson(),
                        AccessToken = GenerateMockJwtToken()
                    };
                }

                return JsonContent.Create(configParams);
            }
            else
            {
                throw new ArgumentException($"Unexpected configuration endpoint. {endpoint}");
            }
        }

        private static string GenerateMockJwtToken()
        {
            string mySecret = "PlaceholderPlaceholder";
            SymmetricSecurityKey mySecurityKey = new(Encoding.ASCII.GetBytes(mySecret));

            JwtSecurityTokenHandler tokenHandler = new();
            SecurityTokenDescriptor tokenDescriptor = new()
            {
                Subject = new ClaimsIdentity(new Claim[] { }),
                Expires = DateTime.UtcNow.AddMinutes(5),
                Issuer = "http://mysite.com",
                Audience = "http://myaudience.com",
                SigningCredentials = new SigningCredentials(mySecurityKey, SecurityAlgorithms.HmacSha256Signature)
            };

            SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private static ConfigurationPostParameters GetCosmosConfigurationParameters()
        {
            string cosmosFile = $"{RuntimeConfigLoader.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigLoader.CONFIG_EXTENSION}";
            return new(
                File.ReadAllText(cosmosFile),
                File.ReadAllText("schema.gql"),
                $"AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;Database={COSMOS_DATABASE_NAME}",
                AccessToken: null);
        }

        private static ConfigurationPostParametersV2 GetCosmosConfigurationParametersV2()
        {
            string cosmosFile = $"{RuntimeConfigLoader.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigLoader.CONFIG_EXTENSION}";
            RuntimeConfig overrides = new(
                null,
                new DataSource(DatabaseType.CosmosDB_NoSQL, $"AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;Database={COSMOS_DATABASE_NAME}", new()),
                null,
                null);

            return new(
                File.ReadAllText(cosmosFile),
                overrides.ToJson(),
                File.ReadAllText("schema.gql"),
                AccessToken: null);
        }

        /// <summary>
        /// Helper used to create the post-startup configuration payload sent to configuration controller.
        /// Adds entity used to hydrate authorization resolver post-startup and validate that hydration succeeds.
        /// Additional pre-processing performed acquire database connection string from a local file.
        /// </summary>
        /// <returns>ConfigurationPostParameters object.</returns>
        private static JsonContent GetPostStartupConfigParams(string environment, RuntimeConfig runtimeConfig, string configurationEndpoint)
        {
            string connectionString = GetConnectionStringFromEnvironmentConfig(environment);

            string serializedConfiguration = runtimeConfig.ToJson();

            if (configurationEndpoint == CONFIGURATION_ENDPOINT)
            {
                ConfigurationPostParameters returnParams = new(
                    Configuration: serializedConfiguration,
                    Schema: null,
                    ConnectionString: connectionString,
                    AccessToken: null);
                return JsonContent.Create(returnParams);
            }
            else if (configurationEndpoint == CONFIGURATION_ENDPOINT_V2)
            {
                RuntimeConfig overrides = new(null, new DataSource(DatabaseType.MSSQL, connectionString, new()), null, null);

                ConfigurationPostParametersV2 returnParams = new(
                    Configuration: serializedConfiguration,
                    ConfigurationOverrides: overrides.ToJson(),
                    Schema: null,
                    AccessToken: null);

                return JsonContent.Create(returnParams);
            }
            else
            {
                throw new InvalidOperationException("Invalid configurationEndpoint");
            }
        }

        /// <summary>
        /// Hydrates configuration after engine has started and triggers service instantiation
        /// by executing HTTP requests against the engine until a non-503 error is received.
        /// </summary>
        /// <param name="httpClient">Client used for request execution.</param>
        /// <param name="config">Post-startup configuration</param>
        /// <returns>ServiceUnavailable if service is not successfully hydrated with config</returns>
        private static async Task<HttpStatusCode> HydratePostStartupConfiguration(HttpClient httpClient, JsonContent content, string configurationEndpoint)
        {
            // Hydrate configuration post-startup
            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            return await GetRestResponsePostConfigHydration(httpClient);
        }

        /// <summary>
        /// Executing REST requests against the engine until a non-503 error is received.
        /// </summary>
        /// <param name="httpClient">Client used for request execution.</param>
        /// <returns>ServiceUnavailable if service is not successfully hydrated with config,
        /// else the response code from the REST request</returns>
        private static async Task<HttpStatusCode> GetRestResponsePostConfigHydration(HttpClient httpClient)
        {
            // Retry request RETRY_COUNT times in 1 second increments to allow required services
            // time to instantiate and hydrate permissions.
            int retryCount = RETRY_COUNT;
            HttpStatusCode responseCode = HttpStatusCode.ServiceUnavailable;
            while (retryCount > 0)
            {
                // Spot test authorization resolver utilization to ensure configuration is used.
                HttpResponseMessage postConfigHydrationResult =
                    await httpClient.GetAsync($"api/{POST_STARTUP_CONFIG_ENTITY}");
                responseCode = postConfigHydrationResult.StatusCode;

                if (postConfigHydrationResult.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    retryCount--;
                    Thread.Sleep(TimeSpan.FromSeconds(RETRY_WAIT_SECONDS));
                    continue;
                }

                break;
            }

            return responseCode;
        }

        /// <summary>
        /// Executing GraphQL POST requests against the engine until a non-503 error is received.
        /// </summary>
        /// <param name="httpClient">Client used for request execution.</param>
        /// <returns>ServiceUnavailable if service is not successfully hydrated with config,
        /// else the response code from the GRAPHQL request</returns>
        private static async Task<HttpStatusCode> GetGraphQLResponsePostConfigHydration(HttpClient httpClient)
        {
            // Retry request RETRY_COUNT times in 1 second increments to allow required services
            // time to instantiate and hydrate permissions.
            int retryCount = RETRY_COUNT;
            HttpStatusCode responseCode = HttpStatusCode.ServiceUnavailable;
            while (retryCount > 0)
            {
                string query = @"{
                    book_by_pk(id: 1) {
                       id,
                       title,
                       publisher_id
                    }
                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage graphQLResponse = await httpClient.SendAsync(graphQLRequest);
                responseCode = graphQLResponse.StatusCode;

                if (responseCode == HttpStatusCode.ServiceUnavailable)
                {
                    retryCount--;
                    Thread.Sleep(TimeSpan.FromSeconds(RETRY_WAIT_SECONDS));
                    continue;
                }

                break;
            }

            return responseCode;
        }

        /// <summary>
        /// Instantiate minimal runtime config with custom global settings.
        /// </summary>
        /// <param name="dataSource">DataSource to pull connection string required for engine start.</param>
        /// <returns></returns>
        public static RuntimeConfig InitMinimalRuntimeConfig(
            DataSource dataSource,
            GraphQLRuntimeOptions graphqlOptions,
            RestRuntimeOptions restOptions,
            Entity entity = null,
            string entityName = null)
        {
            entity ??= new(
                Source: new("books", EntitySourceType.Table, null, null),
                Rest: null,
                GraphQL: new(Singular: "book", Plural: "books"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
                );

            entityName ??= "Book";

            Dictionary<string, Entity> entityMap = new()
            {
                { entityName, entity }
            };

            return new(
                Schema: "IntegrationTestMinimalSchema",
                DataSource: dataSource,
                Runtime: new(restOptions, graphqlOptions, new(null, null)),
                Entities: new(entityMap)
            );
        }

        /// <summary>
        /// Gets PermissionSetting object allowed to perform all actions.
        /// </summary>
        /// <param name="roleName">Name of role to assign to permission</param>
        /// <returns>PermissionSetting</returns>
        public static EntityPermission GetMinimalPermissionConfig(string roleName)
        {
            EntityAction actionForRole = new(
                Action: EntityActionOperation.All,
                Fields: null,
                Policy: new()
            );

            return new EntityPermission(
                Role: roleName,
                Actions: new[] { actionForRole }
            );
        }

        /// <summary>
        /// Reads configuration file for defined environment to acquire the connection string.
        /// CI/CD Pipelines and local environments may not have connection string set as environment variable.
        /// </summary>
        /// <param name="environment">Environment such as TestCategory.MSSQL</param>
        /// <returns>Connection string</returns>
        public static string GetConnectionStringFromEnvironmentConfig(string environment)
        {
            FileSystem fileSystem = new();
            string sqlFile = new RuntimeConfigLoader(fileSystem).GetFileNameForEnvironment(environment, considerOverrides: true);
            string configPayload = File.ReadAllText(sqlFile);

            RuntimeConfigLoader.TryParseConfig(configPayload, out RuntimeConfig runtimeConfig);

            return runtimeConfig.DataSource.ConnectionString;
        }

        private static void ValidateCosmosDbSetup(TestServer server)
        {
            object metadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(metadataProvider, typeof(CosmosSqlMetadataProvider));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(CosmosQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(CosmosMutationEngine));

            CosmosClientProvider cosmosClientProvider = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
            Assert.IsNotNull(cosmosClientProvider);
            Assert.IsNotNull(cosmosClientProvider.Client);
        }

        private bool HandleException<T>(Exception e) where T : Exception
        {
            if (e is AggregateException aggregateException)
            {
                aggregateException.Handle(HandleException<T>);
                return true;
            }
            else if (e is T)
            {
                return true;
            }

            return false;
        }
    }
}
