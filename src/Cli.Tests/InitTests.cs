// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Reflection;
using Azure.DataApiBuilder.Config.ObjectModel;
using Cli.Commands;

namespace Cli.Tests
{
    /// <summary>
    /// Test for config file initialization.
    /// </summary>
    [TestClass]
    public class InitTests
        : VerifyBase
    {
        private IFileSystem? _fileSystem;
        private RuntimeConfigLoader? _runtimeConfigLoader;

        [TestInitialize]
        public void TestInitialize()
        {
            MockFileSystem fileSystem = new();

            fileSystem.AddFile(
                fileSystem.Path.Combine(
                    fileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                    "dab.draft.schema.json"),
                new MockFileData("{ \"additionalProperties\": {\"version\": \"https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json\"} }"));

            _fileSystem = fileSystem;

            _runtimeConfigLoader = new RuntimeConfigLoader(_fileSystem);

            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });

            SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
            SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _fileSystem = null;
            _runtimeConfigLoader = null;
        }

        /// <summary>
        /// Test the simple init config for mssql database. PG and MySQL should be similar.
        /// There is no need for a separate test.
        /// </summary>
        [TestMethod]
        public Task MsSQLDatabase()
        {
            InitOptions options = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: true,
                hostMode: HostMode.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restPath: "rest-api",
                config: TEST_RUNTIME_CONFIG_FILE);

            return ExecuteVerifyTest(options);
        }

        /// <summary>
        /// Test the simple init config for cosmosdb_postgresql database.
        /// </summary>
        [TestMethod]
        public Task CosmosDbPostgreSqlDatabase()
        {
            InitOptions options = new(
                databaseType: DatabaseType.CosmosDB_PostgreSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostMode.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restPath: "/rest-endpoint",
                config: TEST_RUNTIME_CONFIG_FILE);

            return ExecuteVerifyTest(options);
        }

        /// <summary>
        /// Test to verify creation of initial config without providing
        /// connection-string
        /// </summary>
        [TestMethod]
        public Task TestInitializingConfigWithoutConnectionString()
        {
            InitOptions options = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: null,
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostMode.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            return ExecuteVerifyTest(options);
        }

        /// <summary>
        /// Test cosmosdb_nosql specifc settings like cosmosdb_nosql-database, cosmosdb_nosql-container, cosmos-schema file.
        /// </summary>
        [TestMethod]
        public Task CosmosDbNoSqlDatabase()
        {
            // Mock the schema file. It can be empty as we are not testing the schema file contents in this test.
            ((MockFileSystem)_fileSystem!).AddFile(TEST_SCHEMA_FILE, new MockFileData(""));

            InitOptions options = new(
                databaseType: DatabaseType.CosmosDB_NoSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: "testdb",
                cosmosNoSqlContainer: "testcontainer",
                graphQLSchemaPath: TEST_SCHEMA_FILE,
                setSessionContext: false,
                hostMode: HostMode.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            return ExecuteVerifyTest(options);
        }

        /// <summary>
        /// Verify that if graphQLSchema file is not present, config file won't be generated.
        /// It will show an error stating the graphQL schema file not found.
        /// </summary>
        [DataRow("no-schema.gql", false, DisplayName = "FAIL: GraphQL Schema file not available.")]
        [DataRow(TEST_SCHEMA_FILE, true, DisplayName = "PASS: GraphQL Schema file available.")]
        [DataTestMethod]
        public void VerifyGraphQLSchemaFileAvailabilityForCosmosDB(
            string schemaFileName,
            bool expectSuccess
        )
        {
            if (expectSuccess is true)
            {
                // If we expect the file, then add it to the mock file system.
                ((MockFileSystem)_fileSystem!).AddFile(schemaFileName, new MockFileData(""));
            }

            InitOptions options = new(
                databaseType: DatabaseType.CosmosDB_NoSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: "somedb",
                cosmosNoSqlContainer: "somecontainer",
                graphQLSchemaPath: schemaFileName,
                setSessionContext: false,
                hostMode: HostMode.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            Assert.AreEqual(expectSuccess, TryCreateRuntimeConfig(options, _runtimeConfigLoader!, _fileSystem!, out RuntimeConfig? _));
        }

        /// <summary>
        /// Verify that if either database or graphQLSchema is null or empty, we will get error.
        /// </summary>
        [DataRow(null, "testcontainer", "", false, DisplayName = "Both database and schema are either null or empty.")]
        [DataRow("", "testcontainer", "testschema", false, DisplayName = "database is empty.")]
        [DataRow("testDatabase", "testcontainer", "", false, DisplayName = "database is provided, Schema is null.")]
        [DataRow("testDatabase", null, "", false, DisplayName = "database is provided, container and Schema is null/empty.")]
        [DataRow("testDatabase", null, TEST_SCHEMA_FILE, true, DisplayName = "database and schema provided, container is null/empty.")]
        [DataTestMethod]
        public void VerifyRequiredOptionsForCosmosDbNoSqlDatabase(
            string? cosmosDatabase,
            string? cosmosContainer,
            string? graphQLSchema,
            bool expectedResult)
        {
            if (!string.IsNullOrEmpty(graphQLSchema))
            {
                // Mock the schema file. It can be empty as we are not testing the schema file contents in this test.
                ((MockFileSystem)_fileSystem!).AddFile(graphQLSchema, new MockFileData(""));
            }

            InitOptions options = new(
                databaseType: DatabaseType.CosmosDB_NoSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: cosmosDatabase,
                cosmosNoSqlContainer: cosmosContainer,
                graphQLSchemaPath: graphQLSchema,
                setSessionContext: false,
                hostMode: HostMode.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            Assert.AreEqual(expectedResult, TryCreateRuntimeConfig(options, _runtimeConfigLoader!, _fileSystem!, out RuntimeConfig? _));
        }

        /// <summary>
        /// Verify that if both REST and GraphQL is disabled, we will get error.
        /// </summary>
        [DataRow(true, true, false, DisplayName = "Both REST and GraphQL disabled.")]
        [DataRow(true, false, true, DisplayName = "REST disabled, and GraphQL enabled.")]
        [DataRow(false, true, true, DisplayName = "REST enabled, and GraphQL disabled.")]
        [DataRow(false, false, true, DisplayName = "Both REST and GraphQL are enabled.")]
        [DataTestMethod]
        public void EnsureFailureWhenBothRestAndGraphQLAreDisabled(
            bool RestDisabled,
            bool GraphQLDisabled,
            bool expectedResult)
        {
            InitOptions options = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostMode.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restDisabled: RestDisabled,
                graphqlDisabled: GraphQLDisabled,
                config: TEST_RUNTIME_CONFIG_FILE);

            Assert.AreEqual(expectedResult, TryCreateRuntimeConfig(options, _runtimeConfigLoader!, _fileSystem!, out RuntimeConfig? _));
        }

        /// <summary>
        /// Test to verify creation of initial config with special characters
        /// such as [!,@,#,$,%,^,&,*, ,(,)] in connection-string.
        /// </summary>
        [TestMethod]
        public Task TestSpecialCharactersInConnectionString()
        {
            InitOptions options = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "A!string@with#some$special%characters^to&check*proper(serialization)including space.",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostMode.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            return ExecuteVerifyTest(options);
        }

        /// <summary>
        /// Test to verify that an error is thrown when user tries to
        /// initialize a config with a file name that already exists.
        /// </summary>
        [TestMethod]
        public void EnsureFailureOnReInitializingExistingConfig()
        {
            InitOptions options = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostMode.Development,
                corsOrigin: new List<string>() { },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            // Config generated successfully for the first time.
            Assert.AreEqual(true, TryGenerateConfig(options, _runtimeConfigLoader!, _fileSystem!));

            // Error is thrown because the config file with the same name
            // already exists.
            Assert.AreEqual(false, TryGenerateConfig(options, _runtimeConfigLoader!, _fileSystem!));
        }

        /// <summary>
        /// Test to verify the config is correctly generated with different Authentication providers.
        /// Audience and Issuer are needed only when the provider is JWT.
        /// Example:
        /// 1. With EasyAuth or Simulator
        /// "authentication": {
        ///     "provider": "StaticWebApps/AppService/Simulator"
        /// }
        ///
        /// 2. With JWT provider
        /// "authentication": {
        ///     "provider": "AzureAD"
        ///      "Jwt":
        ///      {
        ///          "Audience": "aud",
        ///          "Issuer": "iss"
        ///      }
        /// }
        /// </summary>
        [DataTestMethod]
        [DataRow("StaticWebApps", null, null, DisplayName = "StaticWebApps with no audience and no issuer specified.")]
        [DataRow("AppService", null, null, DisplayName = "AppService with no audience and no issuer specified.")]
        [DataRow("Simulator", null, null, DisplayName = "Simulator with no audience and no issuer specified.")]
        [DataRow("AzureAD", "aud-xxx", "issuer-xxx", DisplayName = "AzureAD with both audience and issuer specified.")]
        public Task EnsureCorrectConfigGenerationWithDifferentAuthenticationProviders(
            string authenticationProvider,
            string? audience,
            string? issuer)
        {
            InitOptions options = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostMode.Production,
                corsOrigin: null,
                authenticationProvider: authenticationProvider,
                audience: audience,
                issuer: issuer,
                config: TEST_RUNTIME_CONFIG_FILE);

            // Create VerifySettings and add all arguments to the method as parameters
            VerifySettings verifySettings = new();
            verifySettings.UseHashedParameters(authenticationProvider, audience, issuer);
            return ExecuteVerifyTest(options, verifySettings);
        }

        /// <summary>
        /// Test to verify that error is thrown when user tries to
        /// initialize a config with a file name that already exists
        /// but with different case.
        /// </summary>
        [TestMethod]
        public void EnsureFailureReInitializingExistingConfigWithDifferentCase()
        {
            // Should PASS, new file is being created
            InitOptions initOptionsWithAllLowerCaseFileName = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: true,
                hostMode: HostMode.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restPath: "rest-api",
                config: TEST_RUNTIME_CONFIG_FILE);
            Assert.AreEqual(true, TryGenerateConfig(initOptionsWithAllLowerCaseFileName, _runtimeConfigLoader!, _fileSystem!));

            // same file with all uppercase letters
            InitOptions initOptionsWithAllUpperCaseFileName = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: true,
                hostMode: HostMode.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restPath: "rest-api",
                config: TEST_RUNTIME_CONFIG_FILE.ToUpper());
            // Platform Dependent
            // Windows,MacOs: Should FAIL - File Exists is Case insensitive
            // Unix: Should PASS - File Exists is Case sensitive
            Assert.AreEqual(
                expected: PlatformID.Unix.Equals(Environment.OSVersion.Platform) ? true : false,
                actual: TryGenerateConfig(initOptionsWithAllUpperCaseFileName, _runtimeConfigLoader!, _fileSystem!));
        }

        [TestMethod]
        public Task RestPathWithoutStartingSlashWillHaveItAdded()
        {
            InitOptions options = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostMode.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restPath: "abc",
                config: TEST_RUNTIME_CONFIG_FILE);

            return ExecuteVerifyTest(options);
        }

        [TestMethod]
        public Task GraphQLPathWithoutStartingSlashWillHaveItAdded()
        {
            InitOptions options = new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostMode.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                graphQLPath: "abc",
                config: TEST_RUNTIME_CONFIG_FILE);

            return ExecuteVerifyTest(options);
        }

        private Task ExecuteVerifyTest(InitOptions options, VerifySettings? settings = null)
        {
            Assert.IsTrue(TryCreateRuntimeConfig(options, _runtimeConfigLoader!, _fileSystem!, out RuntimeConfig? runtimeConfig));

            return Verify(runtimeConfig, settings);
        }
    }
}
