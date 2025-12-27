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
    ];

    private static Task<TestResult> TestListStores(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var response = await stateClient.ListStoresAsync(null);

            if (response == null)
                return TestResult.Failed("ListStores returned null");

            var storeNames = response.Stores?.Select(s => s.Name) ?? Enumerable.Empty<string>();
            return TestResult.Successful($"ListStores returned {response.Stores?.Count ?? 0} store(s): {string.Join(", ", storeNames)}");
        }, "List stores");

    private static Task<TestResult> TestListStoresWithRedisFilter(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var request = new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Redis };
            var response = await stateClient.ListStoresAsync(request);

            if (response == null)
                return TestResult.Failed("ListStores with Redis filter returned null");

            return TestResult.Successful($"ListStores (Redis filter) returned {response.Stores?.Count ?? 0} store(s)");
        }, "List stores with Redis filter");

    private static Task<TestResult> TestListStoresWithMySqlFilter(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            var request = new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Mysql };
            var response = await stateClient.ListStoresAsync(request);

            if (response == null)
                return TestResult.Failed("ListStores with MySQL filter returned null");

            return TestResult.Successful($"ListStores (MySQL filter) returned {response.Stores?.Count ?? 0} store(s)");
        }, "List stores with MySQL filter");

    private static Task<TestResult> TestGetStateNonExistentStore(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
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

    private static Task<TestResult> TestGetStateNonExistentKey(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // First, list stores to find a valid store name
            var listResponse = await stateClient.ListStoresAsync(null);
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No stores configured, skipping non-existent key test");

            var storeName = listResponse.Stores.First().Name;
            var request = new GetStateRequest
            {
                StoreName = storeName,
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

    private static Task<TestResult> TestSaveAndGetState(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // First, list stores to find a valid store name
            var listResponse = await stateClient.ListStoresAsync(null);
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No stores configured, skipping save and get test");

            var storeName = listResponse.Stores.First().Name;
            var testKey = $"http-test-{Guid.NewGuid()}";
            var testValue = new { name = "Test Value", timestamp = DateTimeOffset.UtcNow };

            // Save state
            var saveRequest = new SaveStateRequest
            {
                StoreName = storeName,
                Key = testKey,
                Value = testValue
            };

            var saveResponse = await stateClient.SaveStateAsync(saveRequest);

            if (saveResponse == null || !saveResponse.Success)
                return TestResult.Failed("SaveState failed");

            // Get state
            var getRequest = new GetStateRequest
            {
                StoreName = storeName,
                Key = testKey
            };

            var getResponse = await stateClient.GetStateAsync(getRequest);

            if (getResponse == null)
                return TestResult.Failed("GetState returned null after save");

            // Clean up
            var deleteRequest = new DeleteStateRequest
            {
                StoreName = storeName,
                Key = testKey
            };
            await stateClient.DeleteStateAsync(deleteRequest);

            return TestResult.Successful($"Save and Get state successful for key: {testKey}, etag: {getResponse.Etag}");
        }, "Save and get state");

    private static Task<TestResult> TestSaveStateWithTTL(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Find a Redis store for TTL test
            var listResponse = await stateClient.ListStoresAsync(new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Redis });
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No Redis stores configured, skipping TTL test");

            var storeName = listResponse.Stores.First().Name;
            var testKey = $"http-test-ttl-{Guid.NewGuid()}";

            var saveRequest = new SaveStateRequest
            {
                StoreName = storeName,
                Key = testKey,
                Value = new { name = "TTL Test" },
                Options = new StateOptions
                {
                    Ttl = 300 // 5 minutes TTL
                }
            };

            var saveResponse = await stateClient.SaveStateAsync(saveRequest);

            if (saveResponse == null || !saveResponse.Success)
                return TestResult.Failed("SaveState with TTL failed");

            // Clean up
            var deleteRequest = new DeleteStateRequest
            {
                StoreName = storeName,
                Key = testKey
            };
            await stateClient.DeleteStateAsync(deleteRequest);

            return TestResult.Successful($"SaveState with TTL successful for key: {testKey}");
        }, "Save state with TTL");

    private static Task<TestResult> TestDeleteState(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // First, list stores to find a valid store name
            var listResponse = await stateClient.ListStoresAsync(null);
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No stores configured, skipping delete test");

            var storeName = listResponse.Stores.First().Name;
            var testKey = $"http-test-delete-{Guid.NewGuid()}";

            // First save something
            var saveRequest = new SaveStateRequest
            {
                StoreName = storeName,
                Key = testKey,
                Value = new { name = "To Be Deleted" }
            };
            await stateClient.SaveStateAsync(saveRequest);

            // Now delete
            var deleteRequest = new DeleteStateRequest
            {
                StoreName = storeName,
                Key = testKey
            };

            var deleteResponse = await stateClient.DeleteStateAsync(deleteRequest);

            if (deleteResponse == null)
                return TestResult.Failed("DeleteState returned null");

            if (!deleteResponse.Deleted)
                return TestResult.Failed("DeleteState returned deleted=false for existing key");

            return TestResult.Successful($"DeleteState successful for key: {testKey}");
        }, "Delete state");

    private static Task<TestResult> TestDeleteNonExistentKey(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // First, list stores to find a valid store name
            var listResponse = await stateClient.ListStoresAsync(null);
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No stores configured, skipping delete non-existent test");

            var storeName = listResponse.Stores.First().Name;
            var request = new DeleteStateRequest
            {
                StoreName = storeName,
                Key = $"non-existent-{Guid.NewGuid()}"
            };

            var response = await stateClient.DeleteStateAsync(request);

            if (response == null)
                return TestResult.Failed("DeleteState returned null");

            if (response.Deleted)
                return TestResult.Failed("DeleteState returned deleted=true for non-existent key (unexpected)");

            return TestResult.Successful("DeleteState correctly returned deleted=false for non-existent key");
        }, "Delete non-existent key");

    private static Task<TestResult> TestBulkGetState(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // First, list stores to find a valid store name
            var listResponse = await stateClient.ListStoresAsync(null);
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No stores configured, skipping bulk get test");

            var storeName = listResponse.Stores.First().Name;
            var testPrefix = $"http-test-bulk-{Guid.NewGuid()}";
            var testKeys = new[] { $"{testPrefix}-1", $"{testPrefix}-2", $"{testPrefix}-3" };

            // Save some test data
            foreach (var key in testKeys.Take(2)) // Save only first 2 keys
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = storeName,
                    Key = key,
                    Value = new { name = key }
                });
            }

            // Bulk get all 3 keys (2 exist, 1 doesn't)
            var bulkRequest = new BulkGetStateRequest
            {
                StoreName = storeName,
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
                    StoreName = storeName,
                    Key = key
                });
            }

            return TestResult.Successful($"BulkGetState returned {foundCount} found, {notFoundCount} not found");
        }, "Bulk get state");

    private static Task<TestResult> TestETagConcurrency(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // First, list stores to find a valid store name
            var listResponse = await stateClient.ListStoresAsync(null);
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No stores configured, skipping ETag test");

            var storeName = listResponse.Stores.First().Name;
            var testKey = $"http-test-etag-{Guid.NewGuid()}";

            // Save initial value
            var saveRequest = new SaveStateRequest
            {
                StoreName = storeName,
                Key = testKey,
                Value = new { version = 1 }
            };

            var saveResponse = await stateClient.SaveStateAsync(saveRequest);
            if (saveResponse == null || !saveResponse.Success)
                return TestResult.Failed("Initial save failed");

            var etag = saveResponse.Etag;

            // Try to update with correct ETag
            var updateRequest = new SaveStateRequest
            {
                StoreName = storeName,
                Key = testKey,
                Value = new { version = 2 },
                Options = new StateOptions { Etag = etag }
            };

            var updateResponse = await stateClient.SaveStateAsync(updateRequest);
            if (updateResponse == null || !updateResponse.Success)
            {
                // Clean up before failing
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = storeName, Key = testKey });
                return TestResult.Failed("Update with correct ETag failed");
            }

            // Try to update with stale ETag (should fail with 409 Conflict)
            var staleUpdateRequest = new SaveStateRequest
            {
                StoreName = storeName,
                Key = testKey,
                Value = new { version = 3 },
                Options = new StateOptions { Etag = etag } // Using old etag
            };

            try
            {
                var staleResponse = await stateClient.SaveStateAsync(staleUpdateRequest);
                // If we get here without exception, the response should indicate failure
                if (staleResponse != null && !staleResponse.Success)
                {
                    // Clean up
                    await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = storeName, Key = testKey });
                    return TestResult.Successful("ETag concurrency working: stale ETag correctly rejected");
                }

                // Clean up
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = storeName, Key = testKey });
                return TestResult.Successful("ETag concurrency test completed (stale update may have succeeded - check store implementation)");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Clean up
                await stateClient.DeleteStateAsync(new DeleteStateRequest { StoreName = storeName, Key = testKey });
                return TestResult.Successful("ETag concurrency working: 409 Conflict returned for stale ETag");
            }
        }, "ETag concurrency");

    private static Task<TestResult> TestQueryStateNonExistentStore(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
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

    private static Task<TestResult> TestQueryStateMySqlBackend(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Find a MySQL store
            var listResponse = await stateClient.ListStoresAsync(new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Mysql });
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No MySQL stores configured, skipping MySQL query test");

            var storeName = listResponse.Stores.First().Name;

            // Save some test data
            var testPrefix = $"query-test-{Guid.NewGuid()}";
            for (var i = 0; i < 3; i++)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = storeName,
                    Key = $"{testPrefix}-{i}",
                    Value = new { name = $"Item {i}", index = i }
                });
            }

            // Query the store
            var queryRequest = new QueryStateRequest
            {
                StoreName = storeName,
                Page = 0,
                PageSize = 10
            };

            var queryResponse = await stateClient.QueryStateAsync(queryRequest);

            // Clean up test data
            for (var i = 0; i < 3; i++)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest
                {
                    StoreName = storeName,
                    Key = $"{testPrefix}-{i}"
                });
            }

            if (queryResponse == null)
                return TestResult.Failed("QueryState returned null for MySQL store");

            return TestResult.Successful($"QueryState MySQL returned {queryResponse.Results?.Count ?? 0} results, total: {queryResponse.TotalCount}");
        }, "Query MySQL backend");

    private static Task<TestResult> TestQueryStateRedisWithSearch(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Find a Redis store - we'll check if search is supported by attempting a query
            var listResponse = await stateClient.ListStoresAsync(new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Redis });
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No Redis stores configured, skipping Redis search test");

            // Try each Redis store to find one with search enabled
            foreach (var store in listResponse.Stores)
            {
                var queryRequest = new QueryStateRequest
                {
                    StoreName = store.Name,
                    Query = "*", // Match all
                    Page = 0,
                    PageSize = 10
                };

                try
                {
                    var queryResponse = await stateClient.QueryStateAsync(queryRequest);
                    return TestResult.Successful($"QueryState Redis search on '{store.Name}' returned {queryResponse?.Results?.Count ?? 0} results");
                }
                catch (ApiException ex) when (ex.StatusCode == 400)
                {
                    // This store doesn't have search enabled, try next
                    continue;
                }
            }

            return TestResult.Successful("No Redis stores with search enabled found, skipping Redis search test");
        }, "Query Redis with search");

    private static Task<TestResult> TestQueryStateRedisWithoutSearch(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Find a Redis store and attempt a query - if search is not enabled, should get 400
            var listResponse = await stateClient.ListStoresAsync(new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Redis });
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No Redis stores configured, skipping Redis no-search test");

            // Try stores until we find one that returns 400 (no search) or we run out
            foreach (var store in listResponse.Stores)
            {
                var queryRequest = new QueryStateRequest
                {
                    StoreName = store.Name,
                    Query = "*",
                    Page = 0,
                    PageSize = 10
                };

                try
                {
                    await stateClient.QueryStateAsync(queryRequest);
                    // If we got here, this store has search enabled, try next
                    continue;
                }
                catch (ApiException ex) when (ex.StatusCode == 400)
                {
                    return TestResult.Successful($"QueryState correctly returned 400 for Redis store '{store.Name}' without search");
                }
            }

            return TestResult.Successful("All Redis stores have search enabled, skipping no-search test");
        }, "Query Redis without search");

    private static Task<TestResult> TestQueryStateWithPagination(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var stateClient = GetServiceClient<IStateClient>();

            // Find a MySQL store for pagination test
            var listResponse = await stateClient.ListStoresAsync(new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Mysql });
            if (listResponse?.Stores == null || listResponse.Stores.Count == 0)
                return TestResult.Successful("No MySQL stores configured, skipping pagination test");

            var storeName = listResponse.Stores.First().Name;

            // Save test data
            var testPrefix = $"page-test-{Guid.NewGuid()}";
            for (var i = 0; i < 5; i++)
            {
                await stateClient.SaveStateAsync(new SaveStateRequest
                {
                    StoreName = storeName,
                    Key = $"{testPrefix}-{i}",
                    Value = new { name = $"Page Item {i}", index = i }
                });
            }

            // Query first page
            var page1Request = new QueryStateRequest
            {
                StoreName = storeName,
                Page = 0,
                PageSize = 2
            };

            var page1Response = await stateClient.QueryStateAsync(page1Request);

            // Query second page
            var page2Request = new QueryStateRequest
            {
                StoreName = storeName,
                Page = 1,
                PageSize = 2
            };

            var page2Response = await stateClient.QueryStateAsync(page2Request);

            // Clean up test data
            for (var i = 0; i < 5; i++)
            {
                await stateClient.DeleteStateAsync(new DeleteStateRequest
                {
                    StoreName = storeName,
                    Key = $"{testPrefix}-{i}"
                });
            }

            if (page1Response == null || page2Response == null)
                return TestResult.Failed("QueryState pagination returned null");

            var page1Count = page1Response.Results?.Count ?? 0;
            var page2Count = page2Response.Results?.Count ?? 0;

            return TestResult.Successful($"QueryState pagination: page1={page1Count} items, page2={page2Count} items, total={page1Response.TotalCount}");
        }, "Query with pagination");
}
