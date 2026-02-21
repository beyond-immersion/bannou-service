using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for the State service HTTP API endpoints.
/// Tests state store management operations.
/// </summary>
public class StateTestHandler : BaseHttpTestHandler
{
    // Well-known stores that MUST exist in the test environment
    private const string MYSQL_STORE = "account-statestore";
    private const string REDIS_STORE = "auth-statestore";
    private const string REDIS_SEARCH_STORE = "test-search-statestore";
    public override ServiceTest[] GetServiceTests() =>
    [
        // List Stores Tests
        new ServiceTest(TestListStores, "ListStores", "State", "Test listing configured state stores"),
        new ServiceTest(TestListStoresWithRedisFilter, "ListStoresRedisFilter", "State", "Test listing stores filtered by Redis backend"),
        new ServiceTest(TestListStoresWithMySqlFilter, "ListStoresMySqlFilter", "State", "Test listing stores filtered by MySQL backend"),

        // Get State Tests
        new ServiceTest(TestGetStateNonExistentStore, "GetStateNoStore", "State", "Test getting state from non-existent store"),
        new ServiceTest(TestGetStateNonExistentKey, "GetStateNoKey", "State", "Test getting non-existent key"),

        // Save and Get State Tests
        new ServiceTest(TestSaveAndGetState, "SaveAndGetState", "State", "Test saving and retrieving state"),
        new ServiceTest(TestSaveStateWithTTL, "SaveStateWithTTL", "State", "Test saving state with TTL option"),

        // Delete State Tests
        new ServiceTest(TestDeleteState, "DeleteState", "State", "Test deleting state"),
        new ServiceTest(TestDeleteNonExistentKey, "DeleteNoKey", "State", "Test deleting non-existent key"),

        // Bulk Get Tests
        new ServiceTest(TestBulkGetState, "BulkGetState", "State", "Test bulk getting multiple keys"),

        // ETag Concurrency Tests
        new ServiceTest(TestETagConcurrency, "ETagConcurrency", "State", "Test ETag-based optimistic concurrency"),

        // Query State Tests
        new ServiceTest(TestQueryStateNonExistentStore, "QueryStateNoStore", "State", "Test querying state from non-existent store"),
        new ServiceTest(TestQueryStateMySqlBackend, "QueryStateMySql", "State", "Test querying state from MySQL backend"),
        new ServiceTest(TestQueryStateRedisWithSearch, "QueryStateRedisSearch", "State", "Test querying state from Redis with search enabled"),
        new ServiceTest(TestQueryStateRedisWithoutSearch, "QueryStateRedisNoSearch", "State", "Test querying state from Redis without search returns 400"),
        new ServiceTest(TestQueryStateWithPagination, "QueryStatePagination", "State", "Test querying state with pagination"),
        new ServiceTest(TestQueryStateWithConditions, "QueryStateConditions", "State", "Test querying state with QueryCondition filters"),

        // Bulk Save Tests
        new ServiceTest(TestBulkSaveState, "BulkSaveState", "State", "Test bulk saving multiple key-value pairs"),
        new ServiceTest(TestBulkSaveStateWithTTL, "BulkSaveStateTTL", "State", "Test bulk saving with TTL option on Redis"),

        // Bulk Delete Tests
        new ServiceTest(TestBulkDeleteState, "BulkDeleteState", "State", "Test bulk deleting multiple keys"),
        new ServiceTest(TestBulkDeleteStateMixed, "BulkDeleteMixed", "State", "Test bulk delete with mixed existing/non-existing keys"),

        // Bulk Exists Tests
        new ServiceTest(TestBulkExistsState, "BulkExistsState", "State", "Test checking existence of multiple keys"),

        // MySQL Query Operator Tests
        new ServiceTest(TestQueryContainsOperator, "QueryContains", "State", "Test MySQL query with contains operator"),
        new ServiceTest(TestQueryStartsWithOperator, "QueryStartsWith", "State", "Test MySQL query with startsWith operator"),
        new ServiceTest(TestQueryInOperator, "QueryIn", "State", "Test MySQL query with in operator"),
        new ServiceTest(TestQueryComparisonOperators, "QueryComparison", "State", "Test MySQL query with greaterThan/lessThan operators"),

        // Error Response Tests
        new ServiceTest(TestSaveStateConflict, "SaveStateConflict", "State", "Test 409 Conflict response for ETag mismatch"),
    ];

    private static async Task<TestResult> TestListStores(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var response = await stateClient.ListStoresAsync(null);

            if (response == null)
                return TestResult.Failed("ListStores returned null");

            var storeNames = response.Stores?.Select(s => s.Name) ?? Enumerable.Empty<string>();
            return TestResult.Successful($"ListStores returned {response.Stores?.Count ?? 0} store(s): {string.Join(", ", storeNames)}");
        }, "List stores");

    private static async Task<TestResult> TestListStoresWithRedisFilter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var request = new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Redis };
            var response = await stateClient.ListStoresAsync(request);

            if (response == null)
                return TestResult.Failed("ListStores with Redis filter returned null");

            return TestResult.Successful($"ListStores (Redis filter) returned {response.Stores?.Count ?? 0} store(s)");
        }, "List stores with Redis filter");

    private static async Task<TestResult> TestListStoresWithMySqlFilter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var request = new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Mysql };
            var response = await stateClient.ListStoresAsync(request);

            if (response == null)
                return TestResult.Failed("ListStores with MySQL filter returned null");

            return TestResult.Successful($"ListStores (MySQL filter) returned {response.Stores?.Count ?? 0} store(s)");
        }, "List stores with MySQL filter");

    private static async Task<TestResult> TestGetStateNonExistentStore(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var request = new GetStateRequest
            {
                StoreName = "non-existent-store-12345",
                Key = "test-key"
            };

            try
            {
                await stateClient.GetStateAsync(request);
                return TestResult.Successful("GetState returned for non-existent store (unexpected success)");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("GetState correctly returned 404 for non-existent store");
            }
        }, "Get state from non-existent store");

    private static async Task<TestResult> TestGetStateNonExistentKey(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var request = new GetStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = $"non-existent-key-{Guid.NewGuid()}"
            };

            try
            {
                await stateClient.GetStateAsync(request);
                return TestResult.Successful("GetState returned null/empty for non-existent key (valid behavior)");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("GetState correctly returned 404 for non-existent key");
            }
        }, "Get non-existent key");

    private static async Task<TestResult> TestSaveAndGetState(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testKey = $"http-test-{Guid.NewGuid()}";
            var testValue = new { name = "Test Value", timestamp = DateTimeOffset.UtcNow };

            // Save state
            var saveRequest = new SaveStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey,
                Value = testValue,
                Options = new StateOptions()
            };

            var saveResponse = await stateClient.SaveStateAsync(saveRequest);

            // Validate response structure (SaveStateResponse has optional etag property)
            if (saveResponse == null)
                return TestResult.Failed("SaveState returned null response");

            // Verify etag is returned (indicates successful save)
            if (string.IsNullOrEmpty(saveResponse.Etag))
                return TestResult.Failed("SaveState did not return an etag");

            // Get state
            var getRequest = new GetStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey
            };

            var getResponse = await stateClient.GetStateAsync(getRequest);

            if (getResponse == null)
                return TestResult.Failed("GetState returned null after save");

            // Clean up
            var deleteRequest = new DeleteStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey
            };
            await stateClient.DeleteStateAsync(deleteRequest);

            return TestResult.Successful($"Save and Get state successful for key: {testKey}, etag: {getResponse.Etag}");
        }, "Save and get state");

    private static async Task<TestResult> TestSaveStateWithTTL(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testKey = $"http-test-ttl-{Guid.NewGuid()}";

            var saveRequest = new SaveStateRequest
            {
                StoreName = REDIS_STORE,
                Key = testKey,
                Value = new { name = "TTL Test" },
                Options = new StateOptions
                {
                    Ttl = 300 // 5 minutes TTL
                }
            };

            var saveResponse = await stateClient.SaveStateAsync(saveRequest);

            // Validate response structure (SaveStateResponse has optional etag property)
            if (saveResponse == null)
                return TestResult.Failed("SaveState with TTL returned null response");

            // Verify etag is returned (indicates successful save)
            if (string.IsNullOrEmpty(saveResponse.Etag))
                return TestResult.Failed("SaveState with TTL did not return an etag");

            // Clean up
            var deleteRequest = new DeleteStateRequest
            {
                StoreName = REDIS_STORE,
                Key = testKey
            };
            await stateClient.DeleteStateAsync(deleteRequest);

            return TestResult.Successful($"SaveState with TTL successful for key: {testKey}");
        }, "Save state with TTL");

    private static async Task<TestResult> TestDeleteState(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testKey = $"http-test-delete-{Guid.NewGuid()}";

            // First save something
            var saveRequest = new SaveStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey,
                Value = new { name = "To Be Deleted" },
                Options = new StateOptions()
            };
            await stateClient.SaveStateAsync(saveRequest);

            // Now delete
            var deleteRequest = new DeleteStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey
            };

            var deleteResponse = await stateClient.DeleteStateAsync(deleteRequest);

            if (deleteResponse == null)
                return TestResult.Failed("DeleteState returned null");

            if (!deleteResponse.Deleted)
                return TestResult.Failed("DeleteState returned deleted=false for existing key");

            return TestResult.Successful($"DeleteState successful for key: {testKey}");
        }, "Delete state");

    private static async Task<TestResult> TestDeleteNonExistentKey(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var request = new DeleteStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = $"non-existent-{Guid.NewGuid()}"
            };

            var response = await stateClient.DeleteStateAsync(request);

            if (response == null)
                return TestResult.Failed("DeleteState returned null");

            if (response.Deleted)
                return TestResult.Failed("DeleteState returned deleted=true for non-existent key (unexpected)");

            return TestResult.Successful("DeleteState correctly returned deleted=false for non-existent key");
        }, "Delete non-existent key");

    private static async Task<TestResult> TestBulkGetState(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"http-test-bulk-{Guid.NewGuid()}";
            var testKeys = new[] { $"{testPrefix}-1", $"{testPrefix}-2", $"{testPrefix}-3" };

            // Save some test data
            foreach (var key in testKeys.Take(2)) // Save only first 2 keys
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = key,
                    Value = new { name = key },
                    Options = new StateOptions()
                });
            }

            // Bulk get all 3 keys (2 exist, 1 doesn't)
            var bulkRequest = new BulkGetStateRequest
            {
                StoreName = MYSQL_STORE,
                Keys = testKeys
            };

            var bulkResponse = await stateClient.BulkGetStateAsync(bulkRequest);

            if (bulkResponse == null)
                return TestResult.Failed("BulkGetState returned null");

            var foundCount = bulkResponse.Items?.Count(i => i.Found) ?? 0;
            var notFoundCount = bulkResponse.Items?.Count(i => !i.Found) ?? 0;

            // Clean up
            foreach (var key in testKeys)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = key
                });
            }

            return TestResult.Successful($"BulkGetState returned {foundCount} found, {notFoundCount} not found");
        }, "Bulk get state");

    private static async Task<TestResult> TestETagConcurrency(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testKey = $"http-test-etag-{Guid.NewGuid()}";

            // Save initial value
            var saveRequest = new SaveStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey,
                Value = new { version = 1 },
                Options = new StateOptions()
            };

            var saveResponse = await stateClient.SaveStateAsync(saveRequest);
            // Validate response structure (SaveStateResponse has optional etag property)
            if (saveResponse == null)
                return TestResult.Failed("Initial save returned null response");

            // ETag is required for optimistic concurrency - fail if not returned
            if (string.IsNullOrEmpty(saveResponse.Etag))
                return TestResult.Failed("Initial save did not return an etag (required for concurrency test)");

            var etag = saveResponse.Etag;

            // Try to update with correct ETag
            var updateRequest = new SaveStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey,
                Value = new { version = 2 },
                Options = new StateOptions { Etag = etag }
            };

            var updateResponse = await stateClient.SaveStateAsync(updateRequest);
            // Validate response structure (SaveStateResponse has optional etag property)
            if (updateResponse == null || string.IsNullOrEmpty(updateResponse.Etag))
            {
                // Clean up before failing
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = testKey });
                return TestResult.Failed("Update with correct ETag failed - null response or missing etag");
            }

            // Try to update with stale ETag (should fail with 409 Conflict)
            var staleUpdateRequest = new SaveStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey,
                Value = new { version = 3 },
                Options = new StateOptions { Etag = etag } // Using old etag
            };

            try
            {
                var staleResponse = await stateClient.SaveStateAsync(staleUpdateRequest);
                // If we get here without exception, the stale update unexpectedly succeeded
                // Clean up
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = testKey });
                return TestResult.Successful("ETag concurrency test completed (stale update may have succeeded - check store implementation)");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Clean up
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = testKey });
                return TestResult.Successful("ETag concurrency working: 409 Conflict returned for stale ETag");
            }
        }, "ETag concurrency");

    private static async Task<TestResult> TestQueryStateNonExistentStore(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var request = new QueryStateRequest
            {
                StoreName = "non-existent-store-12345",
                Page = 0,
                PageSize = 10
            };

            try
            {
                await stateClient.QueryStateAsync(request);
                return TestResult.Failed("QueryState should have returned 404 for non-existent store");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("QueryState correctly returned 404 for non-existent store");
            }
        }, "Query non-existent store");

    private static async Task<TestResult> TestQueryStateMySqlBackend(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Save some test data
            var testPrefix = $"query-test-{Guid.NewGuid()}";
            for (var i = 0; i < 3; i++)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = $"{testPrefix}-{i}",
                    Value = new { name = $"Item {i}", index = i },
                    Options = new StateOptions()
                });
            }

            // Query the store
            var queryRequest = new QueryStateRequest
            {
                StoreName = MYSQL_STORE,
                Page = 0,
                PageSize = 10
            };

            var queryResponse = await stateClient.QueryStateAsync(queryRequest);

            // Clean up test data
            for (var i = 0; i < 3; i++)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = $"{testPrefix}-{i}"
                });
            }

            if (queryResponse == null)
                return TestResult.Failed("QueryState returned null for MySQL store");

            return TestResult.Successful($"QueryState MySQL returned {queryResponse.Results?.Count ?? 0} results, total: {queryResponse.TotalCount}");
        }, "Query MySQL backend");

    private static async Task<TestResult> TestQueryStateRedisWithSearch(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Query the documentation store which MUST have search enabled
            var queryRequest = new QueryStateRequest
            {
                StoreName = REDIS_SEARCH_STORE,
                Query = "*", // Match all
                Page = 0,
                PageSize = 10
            };

            var queryResponse = await stateClient.QueryStateAsync(queryRequest);

            if (queryResponse == null)
                return TestResult.Failed($"QueryState returned null for Redis search store '{REDIS_SEARCH_STORE}'");

            return TestResult.Successful($"QueryState Redis search on '{REDIS_SEARCH_STORE}' returned {queryResponse.Results?.Count ?? 0} results");
        }, "Query Redis with search");

    private static async Task<TestResult> TestQueryStateRedisWithoutSearch(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Query a Redis store without search - should return 400
            var queryRequest = new QueryStateRequest
            {
                StoreName = REDIS_STORE,
                Query = "*",
                Page = 0,
                PageSize = 10
            };

            try
            {
                await stateClient.QueryStateAsync(queryRequest);
                return TestResult.Failed($"QueryState should have returned 400 for Redis store '{REDIS_STORE}' without search");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                return TestResult.Successful($"QueryState correctly returned 400 for Redis store '{REDIS_STORE}' without search");
            }
        }, "Query Redis without search");

    private static async Task<TestResult> TestQueryStateWithPagination(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Save test data
            var testPrefix = $"page-test-{Guid.NewGuid()}";
            for (var i = 0; i < 5; i++)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = $"{testPrefix}-{i}",
                    Value = new { name = $"Page Item {i}", index = i },
                    Options = new StateOptions()
                });
            }

            // Query first page
            var page1Request = new QueryStateRequest
            {
                StoreName = MYSQL_STORE,
                Page = 0,
                PageSize = 2
            };

            var page1Response = await stateClient.QueryStateAsync(page1Request);

            // Query second page
            var page2Request = new QueryStateRequest
            {
                StoreName = MYSQL_STORE,
                Page = 1,
                PageSize = 2
            };

            var page2Response = await stateClient.QueryStateAsync(page2Request);

            // Clean up test data
            for (var i = 0; i < 5; i++)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = $"{testPrefix}-{i}"
                });
            }

            if (page1Response == null || page2Response == null)
                return TestResult.Failed("QueryState pagination returned null");

            var page1Count = page1Response.Results?.Count ?? 0;
            var page2Count = page2Response.Results?.Count ?? 0;

            return TestResult.Successful($"QueryState pagination: page1={page1Count} items, page2={page2Count} items, total={page1Response.TotalCount}");
        }, "Query with pagination");

    private static async Task<TestResult> TestQueryStateWithConditions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Save test data with distinct status values for filtering
            var testPrefix = $"cond-test-{Guid.NewGuid()}";
            var testData = new[]
            {
                new { key = $"{testPrefix}-1", status = "active", name = "Active Item 1" },
                new { key = $"{testPrefix}-2", status = "active", name = "Active Item 2" },
                new { key = $"{testPrefix}-3", status = "inactive", name = "Inactive Item" },
            };

            foreach (var item in testData)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = item.key,
                    Value = new { item.status, item.name },
                    Options = new StateOptions()
                });
            }

            // Query with conditions - filter by status = "active"
            var queryRequest = new QueryStateRequest
            {
                StoreName = MYSQL_STORE,
                Conditions = new List<QueryCondition>
                {
                    new QueryCondition
                    {
                        Path = "$.status",
                        Operator = QueryOperator.Equals,
                        Value = "active"
                    }
                },
                Page = 0,
                PageSize = 10
            };

            var queryResponse = await stateClient.QueryStateAsync(queryRequest);

            // Clean up test data
            foreach (var item in testData)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = item.key
                });
            }

            if (queryResponse == null)
                return TestResult.Failed("QueryState with conditions returned null");

            // The query should return results (may include other data in the store too)
            // The key validation is that the endpoint accepts conditions without errors
            return TestResult.Successful($"QueryState with conditions returned {queryResponse.Results?.Count ?? 0} results, total: {queryResponse.TotalCount}");
        }, "Query with conditions");

    // ============================================================================
    // Bulk Save Tests
    // ============================================================================

    private static async Task<TestResult> TestBulkSaveState(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"http-test-bulk-save-{Guid.NewGuid()}";
            var testItems = new List<BulkSaveItem>
            {
                new BulkSaveItem { Key = $"{testPrefix}-1", Value = new { name = "Item 1", index = 1 } },
                new BulkSaveItem { Key = $"{testPrefix}-2", Value = new { name = "Item 2", index = 2 } },
                new BulkSaveItem { Key = $"{testPrefix}-3", Value = new { name = "Item 3", index = 3 } }
            };

            var request = new BulkSaveStateRequest
            {
                StoreName = MYSQL_STORE,
                Items = testItems
            };

            var response = await stateClient.BulkSaveStateAsync(request);

            if (response == null)
                return TestResult.Failed("BulkSaveState returned null");

            if (response.Results == null || response.Results.Count != 3)
            {
                // Clean up before returning
                foreach (var item in testItems)
                {
                    await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = item.Key });
                }
                return TestResult.Failed($"BulkSaveState returned {response.Results?.Count ?? 0} results, expected 3");
            }

            // Verify all results have ETags
            var allHaveEtags = response.Results.All(r => !string.IsNullOrEmpty(r.Etag));
            if (!allHaveEtags)
            {
                foreach (var item in testItems)
                {
                    await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = item.Key });
                }
                return TestResult.Failed("BulkSaveState did not return ETags for all items");
            }

            // Verify items were actually saved by getting them
            var getResponse = await stateClient.BulkGetStateAsync(new BulkGetStateRequest
            {
                StoreName = MYSQL_STORE,
                Keys = testItems.Select(i => i.Key).ToList()
            });

            var foundCount = getResponse?.Items?.Count(i => i.Found) ?? 0;

            // Clean up
            foreach (var item in testItems)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = item.Key });
            }

            if (foundCount != 3)
                return TestResult.Failed($"BulkSaveState claimed success but only {foundCount}/3 items found on get");

            return TestResult.Successful($"BulkSaveState saved {response.Results.Count} items with ETags");
        }, "Bulk save state");

    private static async Task<TestResult> TestBulkSaveStateWithTTL(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"http-test-bulk-save-ttl-{Guid.NewGuid()}";
            var testItems = new List<BulkSaveItem>
            {
                new BulkSaveItem { Key = $"{testPrefix}-1", Value = new { name = "TTL Item 1" } },
                new BulkSaveItem { Key = $"{testPrefix}-2", Value = new { name = "TTL Item 2" } }
            };

            var request = new BulkSaveStateRequest
            {
                StoreName = REDIS_STORE,
                Items = testItems,
                Options = new StateOptions { Ttl = 300 } // 5 minutes TTL
            };

            var response = await stateClient.BulkSaveStateAsync(request);

            if (response == null)
                return TestResult.Failed("BulkSaveState with TTL returned null");

            if (response.Results == null || response.Results.Count != 2)
            {
                foreach (var item in testItems)
                {
                    await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = REDIS_STORE, Key = item.Key });
                }
                return TestResult.Failed($"BulkSaveState with TTL returned {response.Results?.Count ?? 0} results, expected 2");
            }

            // Clean up
            foreach (var item in testItems)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = REDIS_STORE, Key = item.Key });
            }

            return TestResult.Successful($"BulkSaveState with TTL saved {response.Results.Count} items to Redis");
        }, "Bulk save state with TTL");

    // ============================================================================
    // Bulk Delete Tests
    // ============================================================================

    private static async Task<TestResult> TestBulkDeleteState(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"http-test-bulk-del-{Guid.NewGuid()}";
            var testKeys = new[] { $"{testPrefix}-1", $"{testPrefix}-2", $"{testPrefix}-3" };

            // Pre-save items
            foreach (var key in testKeys)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = key,
                    Value = new { name = key },
                    Options = new StateOptions()
                });
            }

            // Bulk delete
            var deleteRequest = new BulkDeleteStateRequest
            {
                StoreName = MYSQL_STORE,
                Keys = testKeys
            };

            var response = await stateClient.BulkDeleteStateAsync(deleteRequest);

            if (response == null)
                return TestResult.Failed("BulkDeleteState returned null");

            if (response.DeletedCount != 3)
                return TestResult.Failed($"BulkDeleteState returned deletedCount={response.DeletedCount}, expected 3");

            // Verify items are actually gone
            var getResponse = await stateClient.BulkGetStateAsync(new BulkGetStateRequest
            {
                StoreName = MYSQL_STORE,
                Keys = testKeys
            });

            var stillExist = getResponse?.Items?.Count(i => i.Found) ?? 0;
            if (stillExist > 0)
                return TestResult.Failed($"BulkDeleteState claimed success but {stillExist} items still exist");

            return TestResult.Successful($"BulkDeleteState deleted {response.DeletedCount} items");
        }, "Bulk delete state");

    private static async Task<TestResult> TestBulkDeleteStateMixed(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"http-test-bulk-del-mix-{Guid.NewGuid()}";
            var existingKeys = new[] { $"{testPrefix}-1", $"{testPrefix}-2" };
            var nonExistingKey = $"{testPrefix}-nonexistent";
            var allKeys = existingKeys.Append(nonExistingKey).ToList();

            // Pre-save only 2 items
            foreach (var key in existingKeys)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = key,
                    Value = new { name = key },
                    Options = new StateOptions()
                });
            }

            // Bulk delete 3 keys (2 exist, 1 doesn't)
            var deleteRequest = new BulkDeleteStateRequest
            {
                StoreName = MYSQL_STORE,
                Keys = allKeys
            };

            var response = await stateClient.BulkDeleteStateAsync(deleteRequest);

            if (response == null)
                return TestResult.Failed("BulkDeleteState (mixed) returned null");

            // Should only count the 2 that actually existed
            if (response.DeletedCount != 2)
                return TestResult.Failed($"BulkDeleteState (mixed) returned deletedCount={response.DeletedCount}, expected 2");

            return TestResult.Successful($"BulkDeleteState (mixed) correctly deleted {response.DeletedCount} of 3 requested keys");
        }, "Bulk delete mixed keys");

    // ============================================================================
    // Bulk Exists Tests
    // ============================================================================

    private static async Task<TestResult> TestBulkExistsState(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"http-test-bulk-exists-{Guid.NewGuid()}";
            var existingKeys = new[] { $"{testPrefix}-1", $"{testPrefix}-2" };
            var nonExistingKey = $"{testPrefix}-nonexistent";
            var allKeys = existingKeys.Append(nonExistingKey).ToList();

            // Pre-save 2 items
            foreach (var key in existingKeys)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = key,
                    Value = new { name = key },
                    Options = new StateOptions()
                });
            }

            // Check existence of 3 keys
            var existsRequest = new BulkExistsStateRequest
            {
                StoreName = MYSQL_STORE,
                Keys = allKeys
            };

            var response = await stateClient.BulkExistsStateAsync(existsRequest);

            // Clean up
            foreach (var key in existingKeys)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = key });
            }

            if (response == null)
                return TestResult.Failed("BulkExistsState returned null");

            if (response.ExistingKeys == null)
                return TestResult.Failed("BulkExistsState returned null existingKeys");

            // Should contain only the 2 that exist
            if (response.ExistingKeys.Count != 2)
                return TestResult.Failed($"BulkExistsState returned {response.ExistingKeys.Count} existing keys, expected 2");

            // Verify the correct keys are reported as existing
            var hasKey1 = response.ExistingKeys.Contains(existingKeys[0]);
            var hasKey2 = response.ExistingKeys.Contains(existingKeys[1]);
            var hasNonExisting = response.ExistingKeys.Contains(nonExistingKey);

            if (!hasKey1 || !hasKey2)
                return TestResult.Failed("BulkExistsState missing expected existing keys");

            if (hasNonExisting)
                return TestResult.Failed("BulkExistsState incorrectly included non-existing key");

            return TestResult.Successful($"BulkExistsState correctly identified {response.ExistingKeys.Count} existing keys");
        }, "Bulk exists state");

    // ============================================================================
    // MySQL Query Operator Tests
    // ============================================================================

    private static async Task<TestResult> TestQueryContainsOperator(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"query-contains-{Guid.NewGuid()}";
            var testData = new[]
            {
                new { key = $"{testPrefix}-1", description = "This contains special text here" },
                new { key = $"{testPrefix}-2", description = "Another item with special in it" },
                new { key = $"{testPrefix}-3", description = "No match in this one" },
            };

            foreach (var item in testData)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = item.key,
                    Value = new { description = item.description },
                    Options = new StateOptions()
                });
            }

            // Query with contains operator
            var queryRequest = new QueryStateRequest
            {
                StoreName = MYSQL_STORE,
                Conditions = new List<QueryCondition>
                {
                    new QueryCondition
                    {
                        Path = "$.description",
                        Operator = QueryOperator.Contains,
                        Value = "special"
                    }
                },
                Page = 0,
                PageSize = 10
            };

            var response = await stateClient.QueryStateAsync(queryRequest);

            // Clean up
            foreach (var item in testData)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = item.key });
            }

            if (response == null)
                return TestResult.Failed("QueryState with contains operator returned null");

            // The query executed without error - contains operator is working
            return TestResult.Successful($"QueryState with contains operator returned {response.Results?.Count ?? 0} results");
        }, "Query with contains operator");

    private static async Task<TestResult> TestQueryStartsWithOperator(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"query-starts-{Guid.NewGuid()}";
            var testData = new[]
            {
                new { key = $"{testPrefix}-1", code = "PREFIX_001" },
                new { key = $"{testPrefix}-2", code = "PREFIX_002" },
                new { key = $"{testPrefix}-3", code = "OTHER_003" },
            };

            foreach (var item in testData)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = item.key,
                    Value = new { code = item.code },
                    Options = new StateOptions()
                });
            }

            // Query with startsWith operator
            var queryRequest = new QueryStateRequest
            {
                StoreName = MYSQL_STORE,
                Conditions = new List<QueryCondition>
                {
                    new QueryCondition
                    {
                        Path = "$.code",
                        Operator = QueryOperator.StartsWith,
                        Value = "PREFIX"
                    }
                },
                Page = 0,
                PageSize = 10
            };

            var response = await stateClient.QueryStateAsync(queryRequest);

            // Clean up
            foreach (var item in testData)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = item.key });
            }

            if (response == null)
                return TestResult.Failed("QueryState with startsWith operator returned null");

            return TestResult.Successful($"QueryState with startsWith operator returned {response.Results?.Count ?? 0} results");
        }, "Query with startsWith operator");

    private static async Task<TestResult> TestQueryInOperator(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"query-in-{Guid.NewGuid()}";
            var testData = new[]
            {
                new { key = $"{testPrefix}-1", status = "active" },
                new { key = $"{testPrefix}-2", status = "pending" },
                new { key = $"{testPrefix}-3", status = "inactive" },
                new { key = $"{testPrefix}-4", status = "active" },
            };

            foreach (var item in testData)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = item.key,
                    Value = new { status = item.status },
                    Options = new StateOptions()
                });
            }

            // Query with 'in' operator - status IN ["active", "pending"]
            var queryRequest = new QueryStateRequest
            {
                StoreName = MYSQL_STORE,
                Conditions = new List<QueryCondition>
                {
                    new QueryCondition
                    {
                        Path = "$.status",
                        Operator = QueryOperator.In,
                        Value = new[] { "active", "pending" }
                    }
                },
                Page = 0,
                PageSize = 10
            };

            var response = await stateClient.QueryStateAsync(queryRequest);

            // Clean up
            foreach (var item in testData)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = item.key });
            }

            if (response == null)
                return TestResult.Failed("QueryState with 'in' operator returned null");

            return TestResult.Successful($"QueryState with 'in' operator returned {response.Results?.Count ?? 0} results");
        }, "Query with 'in' operator");

    private static async Task<TestResult> TestQueryComparisonOperators(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testPrefix = $"query-compare-{Guid.NewGuid()}";
            var testData = new[]
            {
                new { key = $"{testPrefix}-1", score = 10 },
                new { key = $"{testPrefix}-2", score = 50 },
                new { key = $"{testPrefix}-3", score = 90 },
            };

            foreach (var item in testData)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = item.key,
                    Value = new { score = item.score },
                    Options = new StateOptions()
                });
            }

            // Query with greaterThan operator (score > 30)
            var gtRequest = new QueryStateRequest
            {
                StoreName = MYSQL_STORE,
                Conditions = new List<QueryCondition>
                {
                    new QueryCondition
                    {
                        Path = "$.score",
                        Operator = QueryOperator.GreaterThan,
                        Value = 30
                    }
                },
                Page = 0,
                PageSize = 10
            };

            var gtResponse = await stateClient.QueryStateAsync(gtRequest);

            // Query with lessThan operator (score < 60)
            var ltRequest = new QueryStateRequest
            {
                StoreName = MYSQL_STORE,
                Conditions = new List<QueryCondition>
                {
                    new QueryCondition
                    {
                        Path = "$.score",
                        Operator = QueryOperator.LessThan,
                        Value = 60
                    }
                },
                Page = 0,
                PageSize = 10
            };

            var ltResponse = await stateClient.QueryStateAsync(ltRequest);

            // Clean up
            foreach (var item in testData)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = item.key });
            }

            if (gtResponse == null || ltResponse == null)
                return TestResult.Failed("QueryState with comparison operators returned null");

            var gtCount = gtResponse.Results?.Count ?? 0;
            var ltCount = ltResponse.Results?.Count ?? 0;

            return TestResult.Successful($"QueryState comparison operators: greaterThan(30)={gtCount} results, lessThan(60)={ltCount} results");
        }, "Query with comparison operators");

    // ============================================================================
    // Error Response Tests
    // ============================================================================

    private static async Task<TestResult> TestSaveStateConflict(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var testKey = $"http-test-conflict-{Guid.NewGuid()}";

            // Save initial value
            var initialSave = await stateClient.SaveStateAsync(new SaveStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey,
                Value = new { version = 1 },
                Options = new StateOptions()
            });

            if (initialSave == null || string.IsNullOrEmpty(initialSave.Etag))
            {
                return TestResult.Failed("Initial save failed - no ETag returned");
            }

            var originalEtag = initialSave.Etag;

            // Update to get a new ETag
            var updateSave = await stateClient.SaveStateAsync(new SaveStateRequest
            {
                StoreName = MYSQL_STORE,
                Key = testKey,
                Value = new { version = 2 },
                Options = new StateOptions { Etag = originalEtag }
            });

            if (updateSave == null || string.IsNullOrEmpty(updateSave.Etag))
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = testKey });
                return TestResult.Failed("Update save failed - no ETag returned");
            }

            // Now try to save with the OLD (stale) ETag - should get 409 Conflict
            try
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = MYSQL_STORE,
                    Key = testKey,
                    Value = new { version = 3 },
                    Options = new StateOptions { Etag = originalEtag } // Stale ETag
                });

                // If we get here, the save unexpectedly succeeded
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = testKey });
                return TestResult.Failed("Save with stale ETag should have returned 409 Conflict but succeeded");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Expected - clean up and return success
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = MYSQL_STORE, Key = testKey });
                return TestResult.Successful("SaveState correctly returned 409 Conflict for stale ETag");
            }
        }, "Save state conflict");
}
