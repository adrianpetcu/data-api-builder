// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Find
{
    /// <summary>
    /// Test GET REST Api validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class FindApiTestBase : RestApiTestBase
    {
        protected static readonly int _numRecordsReturnedFromTieBreakTable = 2;

        public abstract string GetDefaultSchema();
        public abstract string GetDefaultSchemaForEdmModel();

        #region Positive Tests
        /// <summary>
        /// Tests the REST Api for FindById operation without a query string.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTest()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindByIdTest))
            );
        }

        /// <summary>
        /// Tests the REST Api for find many operation on stored procedure
        /// Stored procedure result is not necessarily json.
        /// </summary>
        [TestMethod]
        public virtual async Task FindManyStoredProcedureTest()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationProcedureFindMany_EntityName,
                operationType: EntityActionOperation.Execute,
                restHttpVerb: SupportedHttpVerb.Get,
                sqlQuery: GetQuery("FindManyStoredProcedureTest"),
                expectJson: false
                );
        }

        /// <summary>
        /// Tests the REST Api for find one operation using required parameter
        /// For Find operations, parameters must be passed in query string
        /// </summary>
        [TestMethod]
        public virtual async Task FindOneStoredProcedureTestUsingParameter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?id=1",
                entityNameOrPath: _integrationProcedureFindOne_EntityName,
                operationType: EntityActionOperation.Execute,
                restHttpVerb: SupportedHttpVerb.Get,
                sqlQuery: GetQuery("FindOneStoredProcedureTestUsingParameter"),
                expectJson: false
                );
        }

        /// <summary>
        /// Tests the REST Api for Find operations on empty result sets
        /// 1. GET an entity with no rows (empty table)
        /// 2. GET an entity with rows, filtered to none by query parameter
        /// Should be a 200 response with an empty array
        /// </summary>
        [TestMethod]
        public async Task FindEmptyResultSet()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _emptyTableEntityName,
                sqlQuery: GetQuery("FindEmptyTable")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=1 ne 1",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindEmptyResultSetWithQueryFilter")
            );
        }

        /// <summary>
        /// Tests the Rest Api to validate that unique unicode
        /// characters work in queries.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task FindOnTableWithUniqueCharacters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationUniqueCharactersEntity,
                sqlQuery: GetQuery("FindOnTableWithUniqueCharacters"));
        }

        ///<summary>
        /// Tests the Rest Api for GET operations on Database Views,
        /// either simple or composite.
        ///</summary>
        [TestMethod]
        public virtual async Task FindOnViews()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entityNameOrPath: _simple_all_books,
                sqlQuery: GetQuery("FindViewAll")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=book_id",
                entityNameOrPath: _book_view_with_key_and_mapping,
                sqlQuery: GetQuery("FindViewWithKeyAndMapping")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/2/pieceid/1",
                queryString: string.Empty,
                entityNameOrPath: _simple_subset_stocks,
                sqlQuery: GetQuery("FindViewSelected")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2/pub_id/1234",
                queryString: string.Empty,
                entityNameOrPath: _composite_subset_bookPub,
                sqlQuery: GetQuery("FindBooksPubViewComposite")
            );
        }

        ///<summary>
        /// Tests the Rest Api for GET operations on Database Views,
        /// either simple or composite,having filter clause.
        ///</summary>
        [TestMethod]
        public virtual async Task FindTestWithQueryStringOnViews()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id ge 4",
                entityNameOrPath: _simple_all_books,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneGeFilterOnView")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: "?$select=id,title",
                entityNameOrPath: _simple_all_books,
                sqlQuery: GetQuery("FindByIdTestWithQueryStringFieldsOnView")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=pieceid eq 1",
                entityNameOrPath: _simple_subset_stocks,
                sqlQuery: GetQuery("FindTestWithFilterQueryStringOneEqFilterOnView")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (categoryid gt 1)",
                entityNameOrPath: _simple_subset_stocks,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneNotFilterOnView")
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter= id lt 5",
                entityNameOrPath: _composite_subset_bookPub,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLtFilterOnView")
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithQueryStringFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: "?$select=id,title",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindByIdTestWithQueryStringFields))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with 1 field
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringOneField()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=id",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringOneField))
            );

        }

        /// <summary>
        /// Tests the REST Api for Find operation with an empty query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringAllFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringAllFields))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a single filter, executes
        /// a test for all of the comparison operators and the unary NOT operator.
        /// </summary>
        [TestMethod]
        public async Task FindTestsWithFilterQueryStringOneOpFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq 1",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryStringOneEqFilter")
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=2 eq id",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryStringValueFirstOneEqFilter")
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id gt 3",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneGtFilter")
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id ge 4",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneGeFilter")
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 5",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLtFilter")
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id le 4",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLeFilter")
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id ne 3",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneNeFilter")
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (id lt 2)",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneNotFilter")
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (title eq null)",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneRightNullEqFilter")
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=null ne title",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLeftNullNeFilter")
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with two filters separated with AND
        /// comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringSingleAndFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 3 and id gt 1",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringSingleAndFilter))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with two filters separated with OR
        /// comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringSingleOrFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 3 or id gt 4",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringSingleOrFilter))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with AND.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleAndFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 4 and id gt 1 and title ne 'Awesome book'",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleAndFilters))
            );

        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleOrFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq 1 or id eq 2 or id eq 3",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleOrFilters))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with AND and those comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleAndOrFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=(id gt 2 and id lt 4) or (title eq 'Awesome book')",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleAndOrFilters))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with AND OR and including NOT.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleNotAndOrFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=(not (id lt 3) or id lt 4) or not (title eq 'Awesome book')",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleNotAndOrFilters))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation where we compare one field
        /// to the bool returned from another comparison.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringBoolResultFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq (publisher_id gt 1)",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringSingleAndFilter)),
                exceptionExpected: true,
                expectedErrorMessage: "A binary operator with incompatible types was detected. " +
                    "Found operand types 'Edm.Int32' and 'Edm.Boolean' for operator kind 'Equal'.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        [TestMethod]
        public async Task FindTestWithPrimaryKeyContainingForeignKey()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/567/book_id/1",
                queryString: "?$select=id,content",
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithPrimaryKeyContainingForeignKey))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned with a single primary
        /// key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstSingleKeyPagination()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstSingleKeyPagination)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Validates that a proper nextLink is created for FindMany requests which do not
        /// restrict results with query parameters. Engine default paging mechanisms are used
        /// when > 100 records will be present in result set.
        /// expectedAfterQueryString starts with ?$, and not &$, because it is the only query parameter.
        /// </summary>
        [TestMethod]
        public async Task FindTest_NoQueryParams_PaginationNextLink()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":100,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"bookmarks\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationPaginationEntityName,
                sqlQuery: GetQuery(nameof(FindTest_NoQueryParams_PaginationNextLink)),
                expectedAfterQueryString: $"?$after={Uri.EscapeDataString(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Validates that a proper nextLink is created for FindMany requests which do not
        /// restrict results with query parameters. Engine default paging mechanisms are used
        /// when > 100 records will be present in result set.
        /// expectedAfterQueryString starts with &$, and not ?$, because it is
        /// 1) Not the only query parameter.
        /// 2) Not the first query parameter.
        /// </summary>
        [TestMethod]
        public async Task FindTest_OrderByNotFirstQueryParam_PaginationNextLink()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":100,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"bookmarks\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=id",
                entityNameOrPath: _integrationPaginationEntityName,
                sqlQuery: GetQuery(nameof(FindTest_OrderByNotFirstQueryParam_PaginationNextLink)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned with multiple column
        /// primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstMultiKeyPagination()
        {
            string after = $"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1",
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithFirstMultiKeyPagination)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $after to
        /// get the desired page with a single column primary key.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithAfterSingleKeyPagination()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":7,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$after={Uri.EscapeDataString(after)}",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithAfterSingleKeyPagination))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $after to
        /// get the desired page with a multi column primary key.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithAfterMultiKeyPagination()
        {
            string after = $"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithAfterMultiKeyPagination))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using pagination
        /// to verify that we return an $after cursor that has the
        /// single primary key column.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithPaginationVerifSinglePrimaryKeyInAfter()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$first=1",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithPaginationVerifSinglePrimaryKeyInAfter)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using pagination
        /// to verify that we return an $after cursor that has all the
        /// multiple primary key columns.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithPaginationVerifMultiplePrimaryKeysInAfter()
        {
            string after = $"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$first=1",
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithPaginationVerifMultiplePrimaryKeysInAfter)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using sorting
        /// with integer type and null values.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithIntTypeNullValuesOrderByAsc()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=typeid,int_types&$orderby=int_types",
                entityNameOrPath: _integrationTypeEntity,
                sqlQuery: GetQuery(nameof(FindTestWithIntTypeNullValuesOrderByAsc))
            );
        }

        /// <summary>
        /// Validates that a proper nextLink is created for FindMany requests which do not
        /// restrict results with query parameters AND the entity under test has mapped columns, including primary key column(s).
        /// Engine default paging mechanisms are used when > 100 records will be present in result set.
        /// expectedAfterQueryString starts with ?$, and not &$, because it is the only query parameter.
        /// </summary>
        [TestMethod]
        public async Task FindMany_MappedColumn_NoOrderByQueryParameter()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":100,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"mappedbookmarks\",\"ColumnName\":\"bkid\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationMappedPaginationEntityName,
                sqlQuery: GetQuery(nameof(FindMany_MappedColumn_NoOrderByQueryParameter)),
                paginated: true,
                expectedAfterQueryString: $"?$after={Uri.EscapeDataString(after)}"
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation for all records.
        /// order by title in ascending order.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringAllFieldsOrderByAsc()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=title",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringAllFieldsOrderByAsc))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation for all records.
        /// Uses entity with mapped columns, and order by title in ascending order.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringAllFieldsMappedEntityOrderByAsc()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=fancyName",
                entityNameOrPath: _integrationMappingDifferentEntity,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringAllFieldsMappedEntityOrderByAsc))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation for all records
        /// when there is a space in the column name.
        /// order by "ID Number" in ascending order.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringSpaceInNamesOrderByAsc()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby='ID Number'",
                entityNameOrPath: _integrationEntityHasColumnWithSpace,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringSpaceInNamesOrderByAsc))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// Last Name. Validate that the "after" section in the respond
        /// is well formed.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstAndSpacedColumnOrderBy()
        {
            string after = $"[{{\"Value\":\"Belfort\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"brokers\",\"ColumnName\":\"Last Name\"}}," +
                            $"{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"brokers\",\"ColumnName\":\"ID Number\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby='Last Name'",
                entityNameOrPath: _integrationEntityHasColumnWithSpace,
                sqlQuery: GetQuery(nameof(FindTestWithFirstAndSpacedColumnOrderBy)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true

            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation for all records
        /// order by publisher_id in descending order.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringAllFieldsOrderByDesc()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=publisher_id desc",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringAllFieldsOrderByDesc))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by title,
        /// with a single primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstSingleKeyPaginationAndOrderBy()
        {
            string after = $"[{{\"Value\":\"Also Awesome book\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"title\"}}," +
                            $"{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=title",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstSingleKeyPaginationAndOrderBy)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by id,
        /// the single primary key column in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstSingleKeyIncludedInOrderByAndPagination()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=id",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstSingleKeyIncludedInOrderByAndPagination)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned to two and then
        /// sorting by id.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstTwoOrderByAndPagination()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=2&$orderby=id",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstTwoOrderByAndPagination)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned to two and verify
        /// that with tie breaking we form the correct $after,
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy()
        {
            string after = $"[{{\"Value\":\"2001-01-01\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"authors\",\"ColumnName\":\"birthdate\"}}," +
                            $"{{\"Value\":\"Aniruddh\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"authors\",\"ColumnName\":\"name\"}}," +
                            $"{{\"Value\":125,\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"authors\",\"ColumnName\":\"id\"}}]";
            after = $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$first=2&$orderby=birthdate, name, id desc",
                entityNameOrPath: _integrationTieBreakEntity,
                sqlQuery: GetQuery(nameof(FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy)),
                expectedAfterQueryString: after,
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned to two and verifying
        /// that with a $after that tie breaks we return the correct
        /// result.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy()
        {
            string after = "[{\"Value\":\"2001-01-01\",\"Direction\":0,\"ColumnName\":\"birthdate\"}," +
                            "{\"Value\":\"Aniruddh\",\"Direction\":0,\"ColumnName\":\"name\"}," +
                            "{\"Value\":125,\"Direction\":0,\"ColumnName\":\"id\"}]";
            after = $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$first=2&$orderby=birthdate, name, id{after}",
                entityNameOrPath: _integrationTieBreakEntity,
                sqlQuery: GetQuery(nameof(FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy)),
                verifyNumRecords: _numRecordsReturnedFromTieBreakTable
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// id descending, book_id ascending which make up the entire
        /// composite primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination()
        {
            string after = $"[{{\"Value\":569,\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}," +
                            $"{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=id desc, book_id",
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// book_id, one of the column that make up the composite
        /// primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination()
        {
            string after = $"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=book_id",
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// publisher_id in descending order, which will tie between
        /// 2 records, then sorting by title in descending order to
        /// break the tie.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstAndMultiColumnOrderBy()
        {
            string after = $"[{{\"Value\":2345,\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"publisher_id\"}}," +
                            $"{{\"Value\":\"US history in a nutshell\",\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"title\"}}," +
                            $"{{\"Value\":4,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=publisher_id desc, title desc",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstAndMultiColumnOrderBy)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// publisher_id in descending order, which will tie between
        /// 2 records, then the primary key should be used to break the tie.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstAndTiedColumnOrderBy()
        {
            string after = $"[{{\"Value\":2345,\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"publisher_id\"}}," +
                            $"{{\"Value\":3,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=publisher_id desc",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstAndTiedColumnOrderBy)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using with $orderby
        /// where if order of the columns in the orderby must be maintained
        /// to generate the correct result.
        /// </summary>
        [TestMethod]
        public async Task FindTestVerifyMaintainColumnOrderForOrderBy()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=id desc, publisher_id",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestVerifyMaintainColumnOrderForOrderBy))
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=publisher_id, id desc",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestVerifyMaintainColumnOrderForOrderByInReverse")
            );

        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// content, with multiple column primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstMultiKeyPaginationAndOrderBy()
        {
            string after = $"[{{\"Value\":\"Indeed a great book\",\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"content\"}}," +
                            $"{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=content desc",
                entityNameOrPath: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithFirstMultiKeyPaginationAndOrderBy)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields. Verify that we return the
        /// correct names from the mapping, as well as the names
        /// of the unmapped fields.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithMappedFieldsToBeReturned()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(FindTestWithMappedFieldsToBeReturned))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields. Verify that we return the
        /// correct name from a single mapped field without
        /// unmapped fields included.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithSingleMappedFieldsToBeReturned()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=Scientific Name",
                entityNameOrPath: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(FindTestWithSingleMappedFieldsToBeReturned))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields. Verify that we return the
        /// correct name from a single unmapped field without
        /// mapped fields included.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithUnMappedFieldsToBeReturned()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=treeId",
                entityNameOrPath: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(FindTestWithUnMappedFieldsToBeReturned))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields that are different from another
        /// entity which shares the same source table.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithDifferentMappedFieldsAndFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=fancyName eq 'Tsuga terophylla'",
                entityNameOrPath: _integrationMappingDifferentEntity,
                sqlQuery: GetQuery(nameof(FindTestWithDifferentMappedFieldsAndFilter))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields that are different from another
        /// entity which shares the same source table, and we include
        /// orderby in the request.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithDifferentMappedFieldsAndOrderBy()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=fancyName",
                entityNameOrPath: _integrationMappingDifferentEntity,
                sqlQuery: GetQuery(nameof(FindTestWithDifferentMappedFieldsAndOrderBy))
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields that are different from another
        /// entity which shares the same source table, and we include
        /// orderby in the request, along with pagination.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy()
        {
            string after = $"[{{\"Value\":\"Pseudotsuga menziesii\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"trees\",\"ColumnName\":\"fancyName\"}}," +
                            $"{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"trees\",\"ColumnName\":\"treeId\"}}]";
            string queryStringBase = "?$first=1&$orderby=fancyName";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: queryStringBase,
                entityNameOrPath: _integrationMappingDifferentEntityPath,
                sqlQuery: GetQuery(nameof(FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy)),
                expectedAfterQueryString: $"&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields that are different from another
        /// entity which shares the same source table, and we include
        /// after in the request, along with orderby.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy()
        {
            string after = $"[{{\"Value\":\"Pseudotsuga menziesii\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"trees\",\"ColumnName\":\"fancyName\"}}," +
                            $"{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"trees\",\"ColumnName\":\"treeId\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$orderby=fancyName,treeId&$after={Uri.EscapeDataString(SqlPaginationUtil.Base64Encode(after))}",
                entityNameOrPath: _integrationMappingDifferentEntity,
                sqlQuery: GetQuery(nameof(FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy))
            );
        }

        /// <summary>
        /// Tests that the FIND operation can only read the rows which are accessible after applying the
        /// security policy which uses data from session context.
        /// </summary>
        [TestMethod]
        public virtual Task FindTestOnTableWithSecurityPolicy()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Test to validate that the database policy filter ("@item.id ne 1234 or @item.id gt 1940") is added to the query for FIND operation,
        /// and the corresponding rows are filtered out (not returned) from the result.
        /// </summary>
        [TestMethod]
        public async Task FindTestOnTableWithDatabasePolicy()
        {
            await SetupAndRunRestApiTest(
               primaryKeyRoute: null,
               queryString: string.Empty,
               entityNameOrPath: _foreignKeyEntityName,
               sqlQuery: GetQuery("FindManyTestWithDatabasePolicy"),
               clientRoleHeader: "database_policy_tester"
            );

            await SetupAndRunRestApiTest(
               primaryKeyRoute: "id/1234",
               queryString: string.Empty,
               entityNameOrPath: _foreignKeyEntityName,
               sqlQuery: GetQuery("FindInAccessibleRowWithDatabasePolicy"),
               clientRoleHeader: "database_policy_tester"
           );
        }

        /// <summary>
        /// Validates that Find API request ignores predicates present in the request body
        /// </summary>
        [TestMethod]
        public async Task FindApiTestWithPredicatesInRequestBody()
        {
            string requestBody = @"
            {
                ""$filter"": ""id le 4""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                requestBody: requestBody,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithQueryStringAllFields")
            );
        }

        /// <summary>
        /// Validates that Find API request ignores the predicates present in the request body
        /// and considers only the predicates present in the URI for constructing the response.
        /// </summary>
        [TestMethod]
        public async Task FindApiTestWithPredicatesInUriAndRequestBody()
        {
            string requestBody = @"
            {
                ""$filter"": ""id ge 4""
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id le 4",
                requestBody: requestBody,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLeFilter")
            );
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for Find operation on a stored procedure with a
        /// non-empty primary key route
        /// Expect a 400 Bad Request to be returned
        /// </summary>
        [TestMethod]
        public virtual async Task FindStoredProcedureWithNonEmptyPrimaryKeyRoute()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _integrationProcedureFindMany_EntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Execute,
                restHttpVerb: SupportedHttpVerb.Get,
                exceptionExpected: true,
                expectedErrorMessage: "Primary key route not supported for this entity.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Validates that parameters of a SP sent through the request body are ignored and
        /// results in a 400 Bad Request due to missing required parameters. 
        /// </summary>
        [TestMethod]
        public virtual async Task FindApiTestForSPWithRequiredParamsInRequestBody()
        {
            string requestBody = @"
            {
                ""id"": 1
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                requestBody: requestBody,
                entityNameOrPath: _integrationProcedureFindOne_EntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Execute,
                restHttpVerb: SupportedHttpVerb.Get,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid request. Missing required procedure parameters: id for entity: {_integrationProcedureFindOne_EntityName}",
                expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests the REST Api for Find operations on a stored procedure missing a required parameter
        /// Expect a 400 Bad Request to be returned
        /// </summary>
        [TestMethod]
        public virtual async Task FindStoredProcedureWithMissingParameter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationProcedureFindOne_EntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Execute,
                restHttpVerb: SupportedHttpVerb.Get,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid request. Missing required procedure parameters: id for entity: {_integrationProcedureFindOne_EntityName}",
                expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests the REST Api for Find operations on a stored procedure with extraneous parameters supplied
        /// Expect a 400 Bad Request to be returned
        /// </summary>
        [TestMethod]
        public virtual async Task FindStoredProcedureWithNonexistentParameter()
        {
            // On an entity that takes no parameters
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?param=value",
                entityNameOrPath: _integrationProcedureFindMany_EntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Execute,
                restHttpVerb: SupportedHttpVerb.Get,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid request. Contained unexpected fields: param for entity: {_integrationProcedureFindMany_EntityName}",
                expectedStatusCode: HttpStatusCode.BadRequest
                );

            // On an entity that takes parameters other than those supplied
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?id=1&param=value",
                entityNameOrPath: _integrationProcedureFindOne_EntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Execute,
                restHttpVerb: SupportedHttpVerb.Get,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid request. Contained unexpected fields: param for entity: {_integrationProcedureFindOne_EntityName}",
                expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first=0
        /// to request 0 records, which should throw a DataApiBuilder
        /// Exception.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstZeroSingleKeyPagination()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=0",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid number of items requested, $first must be an integer greater than 0. Actual value: 0",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operations using keywords
        /// of which is not currently supported, verify
        /// we throw a DataApiBuilder exception with the correct
        /// error response.
        /// </summary>
        [DataTestMethod]
        [DataRow("startswith", "(title, 'Awesome')", "eq true")]
        [DataRow("endswith", "(title, 'book')", "eq true")]
        [DataRow("indexof", "(title, 'Awe')", "eq 0")]
        [DataRow("length", "(title)", "gt 5")]
        public async Task FindTestWithUnsupportedFilterKeywords(string keyword, string value, string compareTo)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$filter={keyword}{value} {compareTo}",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "$filter query parameter is not well formed.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        [TestMethod]
        public virtual async Task FindTestWithInvalidFieldsInQueryStringOnViews()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=pq ge 4",
                entityNameOrPath: _simple_all_books,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Could not find a property named 'pq' on type 'default_namespace.{_simple_all_books}.{GetDefaultSchemaForEdmModel()}books_view_all'.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=pq le 4",
                entityNameOrPath: _simple_subset_stocks,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Could not find a property named 'pq' on type 'default_namespace.{_simple_subset_stocks}.{GetDefaultSchemaForEdmModel()}stocks_view_selected'.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (titl gt 1)",
                entityNameOrPath: _composite_subset_bookPub,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Could not find a property named 'titl' on type 'default_namespace.{_composite_subset_bookPub}.{GetDefaultSchemaForEdmModel()}books_publishers_view_composite'.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with an invalid Primary Key Route.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidPrimaryKeyRoute()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "The request is invalid since it contains a primary key with no value specified.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with non-explicit Primary Key
        /// explicit FindById: GET /<entity>/<primarykeyname>/<primarykeyvalue>/
        /// implicit FindById: GET /<entity>/<primarykeyvalue>/
        /// Expected behavior: 400 Bad Request
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestImplicitPrimaryKey()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "1",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "Support for url template with implicit primary key field names is not yet added.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/5671",
                queryString: "?$select=id,content",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid field to be returned requested: content",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithInvalidRoute()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _nonExistentStocksEntityPathName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid Entity path: {_nonExistentStocksEntityPathName}.",
                expectedStatusCode: HttpStatusCode.NotFound,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation on an entity that does not exist
        /// No sqlQuery provided as error should be thrown prior to database query
        /// Expects a 404 Not Found error
        ///
        /// Also tests on an entity with a case mismatch (nonexistent since entities are case-sensitive)
        /// I.e. the case of the entity defined in config does not match the case of the entity requested
        /// EX: entity defined as `Book` in config but `book` resource requested (GET https://localhost:5001/book)
        /// </summary>
        [TestMethod]
        public async Task FindNonExistentEntity()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _nonExistentEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid Entity path: {_nonExistentEntityName}.",
                expectedStatusCode: HttpStatusCode.NotFound,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
            );

            // Case sensitive test
            string integrationEntityNameIncorrectCase = _integrationEntityName.Any(char.IsUpper) ?
                _integrationEntityName.ToLowerInvariant() : _integrationEntityName.ToUpperInvariant();

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: integrationEntityNameIncorrectCase,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid Entity path: {integrationEntityNameIncorrectCase}.",
                expectedStatusCode: HttpStatusCode.NotFound,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
            );

        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has an invalid field
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=id,null",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid Field name: null or white space",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has $select
        /// with no parameters.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithEmptySelectFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid Field name: null or white space",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an invalid OrderByDirection.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidOrderByDirection()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=id random",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: HttpUtility.UrlDecode("Syntax error at position 9 in \u0027id random\u0027."),
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an invalid column name for sorting.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidOrderByColumn()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=Pinecone",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Could not find a property named 'Pinecone' on type 'default_namespace.{_integrationEntityName}.{GetDefaultSchemaForEdmModel()}books'.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an invalid column name for sorting
        /// that contains spaces.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidOrderBySpaceInColumn()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby='Large Pinecone'",
                entityNameOrPath: _integrationEntityHasColumnWithSpace,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid orderby column requested: Large Pinecone.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Regression test to verify we have the correct exception when an invalid
        /// query param is used.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidQueryParam()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?orderby=id",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid Query Parameter: orderby",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a null for sorting.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidOrderByNullQueryParam()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=null",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "OrderBy property is not supported.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Verifies that we throw exception when primary key
        /// route contains an exposed name that maps to a
        /// backing column name that does not exist in the
        /// table.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindOneTestWithInvalidMapping()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "hazards/black_mold_spores",
                queryString: string.Empty,
                entityNameOrPath: _integrationBrokenMappingEntity,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "The request is invalid since the primary keys: spores requested were not found in the entity definition.",
                expectedStatusCode: HttpStatusCode.NotFound,
                expectedSubStatusCode: "EntityNotFound"
                );
        }

        /// <summary>
        /// Verifies that we throw exception when field
        /// requested is an exposed name that maps to a
        /// backing column name that does not exist in
        /// the table.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithInvalidMapping()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=hazards",
                entityNameOrPath: _integrationBrokenMappingEntity,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: "Invalid field to be returned requested: hazards",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
                );
        }

        /// <summary>
        /// Validate that we throw exception when we have a mapped
        /// field but we do not use the correct mapping that
        /// has been assigned.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithConflictingMappedFieldsToBeReturned()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=species",
                entityNameOrPath: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(FindTestWithUnMappedFieldsToBeReturned)),
                exceptionExpected: true,
                expectedErrorMessage: "Invalid field to be returned requested: species",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Validates that query strings with keyless parameters
        /// result in a bad request, HTTP 400.
        /// ?$ -> $ is interpreted as the value of query string parameter null.
        /// ?=12 -> 12 is interpreted as the value query string parameter empty string.
        /// </summary>
        /// <param name="queryString"></param>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow("?$", DisplayName = "Null key, value $ present")]
        [DataRow("?$key", DisplayName = "Null key, value $key present")]
        [DataRow("?=12", DisplayName = "Empty string key, value 12 present")]
        [DataRow("?   =12", DisplayName = "Whitespace string key, value 12 present")]
        [DataRow("?$select=Scientific Name&$key2", DisplayName = "Valid Param1, Param2: Null key, value $key2 present")]
        [DataRow("?$ &=12", DisplayName = "Param1: Null key, Param2: Empty string key")]
        public async Task FindTestWithInvalidQueryStringNoKey(string queryString)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: queryString,
                entityNameOrPath: _integrationMappingEntity,
                sqlQuery: null,
                exceptionExpected: true,
                expectedErrorMessage: "A query parameter without a key is not supported.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests that a cast failure of primary key value type results in HTTP 400 Bad Request.
        /// e.g. Attempt to cast a string '{}' to the 'id' column type of int will fail.
        /// </summary>
        [TestMethod]
        public async Task FindWithUncastablePKValue()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/{}",
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: null,
                exceptionExpected: true,
                expectedErrorMessage: "Parameter \"{}\" cannot be resolved as column \"id\" with type \"Int32\".",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with attempts at
        /// Sql Injection in the primary key route.
        /// </summary>
        [DataTestMethod]
        [DataRow(" WHERE 1=1/*", true)]
        [DataRow("id WHERE 1=1/*", true)]
        [DataRow(" UNION SELECT * FROM books/*", true)]
        [DataRow("id UNION SELECT * FROM books/*", true)]
        [DataRow("; SELECT * FROM information_schema.tables/*", true)]
        [DataRow("id; SELECT * FROM information_schema.tables/*", true)]
        [DataRow("; SELECT * FROM v$version/*", true)]
        [DataRow("id; SELECT * FROM v$version/*", true)]
        [DataRow("id; DROP TABLE books;/*", true)]
        [DataRow(" WHERE 1=1--", false)]
        [DataRow("id WHERE 1=1--", false)]
        [DataRow(" UNION SELECT * FROM books--", false)]
        [DataRow("id UNION SELECT * FROM books--", false)]
        [DataRow("; SELECT * FROM information_schema.tables--", false)]
        [DataRow("id; SELECT * FROM information_schema.tables--", false)]
        [DataRow("; SELECT * FROM v$version--", false)]
        [DataRow("id; SELECT * FROM v$version--", false)]
        [DataRow("id; DROP TABLE books;--", false)]
        public async Task FindByIdTestWithSqlInjectionInPKRoute(string sqlInjection, bool slashStar)
        {
            string message = slashStar ? "Support for url template with implicit primary key field names is not yet added." :
                $"Parameter \"{sqlInjection}\" cannot be resolved as column \"id\" with type \"Int32\".";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"id/{sqlInjection}",
                queryString: $"?$select=id",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: message,
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with attempts at
        /// Sql Injection in the query string.
        /// </summary>
        [DataTestMethod]
        [DataRow(" WHERE 1=1/*")]
        [DataRow(" WHERE 1=1--")]
        [DataRow("id WHERE 1=1/*")]
        [DataRow("id WHERE 1=1--")]
        [DataRow(" UNION SELECT * FROM books/*")]
        [DataRow(" UNION SELECT * FROM books--")]
        [DataRow("id UNION SELECT * FROM books/*")]
        [DataRow("id UNION SELECT * FROM books--")]
        [DataRow("; SELECT * FROM information_schema.tables/*")]
        [DataRow("; SELECT * FROM information_schema.tables--")]
        [DataRow("id; SELECT * FROM information_schema.tables/*")]
        [DataRow("id; SELECT * FROM information_schema.tables--")]
        [DataRow("; SELECT * FROM v$version/*")]
        [DataRow("; SELECT * FROM v$version--")]
        [DataRow("id; SELECT * FROM v$version/*")]
        [DataRow("id; SELECT * FROM v$version--")]
        [DataRow("id; DROP TABLE books;")]
        public async Task FindByIdTestWithSqlInjectionInQueryString(string sqlInjection)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/5671",
                queryString: $"?$select={sqlInjection}",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid field to be returned requested: {sqlInjection}",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with attempts at
        /// Sql Injection in the query string.
        /// </summary>
        [DataTestMethod]
        [DataRow(" WHERE 1=1/*")]
        [DataRow(" WHERE 1=1--")]
        [DataRow("id WHERE 1=1/*")]
        [DataRow("id WHERE 1=1--")]
        [DataRow(" UNION SELECT * FROM books/*")]
        [DataRow(" UNION SELECT * FROM books--")]
        [DataRow("id UNION SELECT * FROM books/*")]
        [DataRow("id UNION SELECT * FROM books--")]
        [DataRow("; SELECT * FROM information_schema.tables/*")]
        [DataRow("; SELECT * FROM information_schema.tables--")]
        [DataRow("id; SELECT * FROM information_schema.tables/*")]
        [DataRow("id; SELECT * FROM information_schema.tables--")]
        [DataRow("; SELECT * FROM v$version/*")]
        [DataRow("; SELECT * FROM v$version--")]
        [DataRow("id; SELECT * FROM v$version/*")]
        [DataRow("id; SELECT * FROM v$version--")]
        [DataRow("id; DROP TABLE books;")]
        public async Task FindManyTestWithSqlInjectionInQueryString(string sqlInjection)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$select={sqlInjection}",
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                exceptionExpected: true,
                expectedErrorMessage: $"Invalid field to be returned requested: {sqlInjection}",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        #endregion
    }
}
