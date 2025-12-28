using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for documentation service API endpoints.
/// Tests the documentation service APIs through the Connect service WebSocket binary protocol.
/// </summary>
public class DocumentationWebSocketTestHandler : IServiceTestHandler
{
    private const string TEST_NAMESPACE = "ws-test-docs";

    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            // CRUD operations
            new ServiceTest(TestCreateDocumentViaWebSocket, "Documentation - Create (WebSocket)", "WebSocket",
                "Test document creation via WebSocket binary protocol"),
            new ServiceTest(TestGetDocumentViaWebSocket, "Documentation - Get (WebSocket)", "WebSocket",
                "Test document retrieval via WebSocket binary protocol"),
            new ServiceTest(TestUpdateDocumentViaWebSocket, "Documentation - Update (WebSocket)", "WebSocket",
                "Test document update via WebSocket binary protocol"),
            new ServiceTest(TestDeleteDocumentViaWebSocket, "Documentation - Delete (WebSocket)", "WebSocket",
                "Test document soft-delete via WebSocket binary protocol"),

            // Search operations
            new ServiceTest(TestSearchDocumentationViaWebSocket, "Documentation - Search (WebSocket)", "WebSocket",
                "Test keyword search via WebSocket binary protocol"),
            new ServiceTest(TestQueryDocumentationViaWebSocket, "Documentation - Query (WebSocket)", "WebSocket",
                "Test natural language query via WebSocket binary protocol"),

            // Trashcan operations
            new ServiceTest(TestRecoverDocumentViaWebSocket, "Documentation - Recover (WebSocket)", "WebSocket",
                "Test document recovery from trashcan via WebSocket binary protocol"),

            // Complete lifecycle
            new ServiceTest(TestDocumentLifecycleViaWebSocket, "Documentation - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete document lifecycle via WebSocket: create -> update -> search -> delete -> recover -> purge"),

            // Additional operations
            new ServiceTest(TestListDocumentsViaWebSocket, "Documentation - List (WebSocket)", "WebSocket",
                "Test document listing with pagination via WebSocket binary protocol"),
            new ServiceTest(TestGetBySlugViaWebSocket, "Documentation - Get by Slug (WebSocket)", "WebSocket",
                "Test document retrieval by slug via WebSocket binary protocol"),
            new ServiceTest(TestSuggestRelatedViaWebSocket, "Documentation - Suggest Related (WebSocket)", "WebSocket",
                "Test related topic suggestions via WebSocket binary protocol"),
            new ServiceTest(TestNamespaceStatsViaWebSocket, "Documentation - Namespace Stats (WebSocket)", "WebSocket",
                "Test namespace statistics via WebSocket binary protocol"),
            new ServiceTest(TestBulkUpdateViaWebSocket, "Documentation - Bulk Update (WebSocket)", "WebSocket",
                "Test bulk document updates via WebSocket binary protocol"),
            new ServiceTest(TestBulkDeleteViaWebSocket, "Documentation - Bulk Delete (WebSocket)", "WebSocket",
                "Test bulk document deletion via WebSocket binary protocol"),
            new ServiceTest(TestImportDocumentationViaWebSocket, "Documentation - Import (WebSocket)", "WebSocket",
                "Test documentation import via WebSocket binary protocol"),

            // Repository binding operations
            new ServiceTest(TestRepositoryBindViaWebSocket, "Documentation - Repo Bind (WebSocket)", "WebSocket",
                "Test repository binding via WebSocket binary protocol"),
            new ServiceTest(TestRepositorySyncViaWebSocket, "Documentation - Repo Sync (WebSocket)", "WebSocket",
                "Test repository sync via WebSocket binary protocol"),
            new ServiceTest(TestRepositoryStatusViaWebSocket, "Documentation - Repo Status (WebSocket)", "WebSocket",
                "Test repository status via WebSocket binary protocol"),
            new ServiceTest(TestRepositoryUnbindViaWebSocket, "Documentation - Repo Unbind (WebSocket)", "WebSocket",
                "Test repository unbind via WebSocket binary protocol"),

            // Archive operations
            new ServiceTest(TestArchiveCreateViaWebSocket, "Documentation - Archive Create (WebSocket)", "WebSocket",
                "Test archive creation via WebSocket binary protocol"),
            new ServiceTest(TestArchiveListViaWebSocket, "Documentation - Archive List (WebSocket)", "WebSocket",
                "Test archive listing via WebSocket binary protocol"),
            new ServiceTest(TestArchiveDeleteViaWebSocket, "Documentation - Archive Delete (WebSocket)", "WebSocket",
                "Test archive deletion via WebSocket binary protocol"),
        };
    }

    private void TestCreateDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Create Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/create via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
                    return false;
                }

                var uniqueSlug = $"ws-create-{DateTime.Now.Ticks}";

                try
                {
                    Console.WriteLine("   Invoking /documentation/create...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            slug = uniqueSlug,
                            title = $"WebSocket Test Document {uniqueSlug}",
                            content = "Content created via WebSocket edge test.",
                            category = "GettingStarted",
                            tags = new[] { "test", "websocket" }
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var documentIdStr = createJson?["documentId"]?.GetValue<string>();
                    var slug = createJson?["slug"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(documentIdStr))
                    {
                        Console.WriteLine("   Failed to create document - no documentId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created document: {documentIdStr} ({slug})");
                    return slug == uniqueSlug;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation create test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation create test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation create test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestGetDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Get Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/get via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var uniqueSlug = $"ws-get-{DateTime.Now.Ticks}";

                try
                {
                    // Create a document first
                    Console.WriteLine("   Creating document for get test...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            slug = uniqueSlug,
                            title = "Get Test Document",
                            content = "Content for get test."
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var documentIdStr = createJson?["documentId"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(documentIdStr))
                    {
                        Console.WriteLine("   Failed to create document for get test");
                        return false;
                    }

                    // Now retrieve it
                    Console.WriteLine($"   Invoking /documentation/get for {documentIdStr}...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/get",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var docNode = getJson?["document"]?.AsObject();
                    var retrievedId = docNode?["documentId"]?.GetValue<string>();
                    var retrievedTitle = docNode?["title"]?.GetValue<string>();

                    Console.WriteLine($"   Retrieved document: {retrievedId} ({retrievedTitle})");
                    return retrievedId == documentIdStr && retrievedTitle == "Get Test Document";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation get test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation get test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation get test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestUpdateDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Update Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/update via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var uniqueSlug = $"ws-update-{DateTime.Now.Ticks}";

                try
                {
                    // Create a document first
                    Console.WriteLine("   Creating document for update test...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            slug = uniqueSlug,
                            title = "Original Title",
                            content = "Original content."
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var documentIdStr = createJson?["documentId"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(documentIdStr))
                    {
                        Console.WriteLine("   Failed to create document for update test");
                        return false;
                    }

                    // Update it
                    Console.WriteLine($"   Invoking /documentation/update for {documentIdStr}...");
                    var updateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/update",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            documentId = documentIdStr,
                            title = "Updated Title",
                            content = "Updated content."
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var updateJson = JsonNode.Parse(updateResponse.GetRawText())?.AsObject();
                    var updatedId = updateJson?["documentId"]?.GetValue<string>();

                    Console.WriteLine($"   Updated document: {updatedId}");

                    // Verify by getting it again
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/get",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var docNode = getJson?["document"]?.AsObject();
                    var retrievedTitle = docNode?["title"]?.GetValue<string>();

                    Console.WriteLine($"   Verified title: {retrievedTitle}");
                    return retrievedTitle == "Updated Title";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation update test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation update test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation update test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestDeleteDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Delete Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/delete via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var uniqueSlug = $"ws-delete-{DateTime.Now.Ticks}";

                try
                {
                    // Create a document
                    Console.WriteLine("   Creating document for delete test...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            slug = uniqueSlug,
                            title = "Delete Test Document",
                            content = "Content for delete test."
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var documentIdStr = createJson?["documentId"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(documentIdStr))
                    {
                        Console.WriteLine("   Failed to create document for delete test");
                        return false;
                    }

                    // Delete it
                    Console.WriteLine($"   Invoking /documentation/delete for {documentIdStr}...");
                    var deleteResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/delete",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var deleteJson = JsonNode.Parse(deleteResponse.GetRawText())?.AsObject();
                    var deletedId = deleteJson?["documentId"]?.GetValue<string>();
                    var recoverableUntil = deleteJson?["recoverableUntil"]?.GetValue<string>();

                    Console.WriteLine($"   Deleted document: {deletedId}");
                    Console.WriteLine($"   Recoverable until: {recoverableUntil}");

                    return deletedId == documentIdStr && !string.IsNullOrEmpty(recoverableUntil);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation delete test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation delete test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation delete test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestSearchDocumentationViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Search Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/search via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var testNamespace = $"ws-search-{DateTime.Now.Ticks}";

                try
                {
                    // Create a document with searchable content
                    Console.WriteLine("   Creating document for search test...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = testNamespace,
                            slug = "api-reference-doc",
                            title = "API Reference Guide",
                            content = "Complete API reference documentation for all endpoints."
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    // Search for it
                    Console.WriteLine("   Invoking /documentation/search...");
                    var searchResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/search",
                        new
                        {
                            @namespace = testNamespace,
                            searchTerm = "API reference"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var searchJson = JsonNode.Parse(searchResponse.GetRawText())?.AsObject();
                    var totalResults = searchJson?["totalResults"]?.GetValue<int>() ?? 0;
                    var results = searchJson?["results"]?.AsArray();

                    Console.WriteLine($"   Search returned {totalResults} results");

                    return results != null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation search test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation search test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation search test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestQueryDocumentationViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Query Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/query via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var testNamespace = $"ws-query-{DateTime.Now.Ticks}";

                try
                {
                    // Create a document
                    Console.WriteLine("   Creating document for query test...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = testNamespace,
                            slug = "auth-guide",
                            title = "Authentication Guide",
                            content = "How to authenticate using OAuth2 providers."
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    // Query for it
                    Console.WriteLine("   Invoking /documentation/query...");
                    var queryResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/query",
                        new
                        {
                            @namespace = testNamespace,
                            query = "how do I authenticate with OAuth"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var queryJson = JsonNode.Parse(queryResponse.GetRawText())?.AsObject();
                    var totalResults = queryJson?["totalResults"]?.GetValue<int>() ?? 0;
                    var results = queryJson?["results"]?.AsArray();

                    Console.WriteLine($"   Query returned {totalResults} results");

                    return results != null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation query test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation query test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation query test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRecoverDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Recover Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/recover via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var uniqueSlug = $"ws-recover-{DateTime.Now.Ticks}";

                try
                {
                    // Create a document
                    Console.WriteLine("   Creating document for recover test...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            slug = uniqueSlug,
                            title = "Recover Test Document",
                            content = "Content for recover test."
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var documentIdStr = createJson?["documentId"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(documentIdStr))
                    {
                        Console.WriteLine("   Failed to create document for recover test");
                        return false;
                    }

                    // Delete it
                    Console.WriteLine($"   Deleting document {documentIdStr}...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/delete",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    // Recover it
                    Console.WriteLine($"   Invoking /documentation/recover for {documentIdStr}...");
                    var recoverResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/recover",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var recoverJson = JsonNode.Parse(recoverResponse.GetRawText())?.AsObject();
                    var recoveredId = recoverJson?["documentId"]?.GetValue<string>();

                    Console.WriteLine($"   Recovered document: {recoveredId}");

                    // Verify it's accessible again
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/get",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var docNode = getJson?["document"]?.AsObject();
                    var retrievedId = docNode?["documentId"]?.GetValue<string>();

                    Console.WriteLine($"   Verified recovery: {retrievedId}");
                    return recoveredId == documentIdStr && retrievedId == documentIdStr;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation recover test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation recover test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation recover test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestDocumentLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete document lifecycle via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var testId = DateTime.Now.Ticks;
                var testNamespace = $"ws-lifecycle-{testId}";

                try
                {
                    // Step 1: Create document
                    Console.WriteLine("   Step 1: Creating document...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = testNamespace,
                            slug = $"lifecycle-{testId}",
                            title = "Lifecycle Test Document",
                            content = "Initial content for lifecycle testing.",
                            category = "GettingStarted",
                            tags = new[] { "lifecycle", "test" }
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var documentIdStr = createJson?["documentId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(documentIdStr))
                    {
                        Console.WriteLine("   Failed to create document");
                        return false;
                    }
                    Console.WriteLine($"   Created document: {documentIdStr}");

                    // Step 2: Update document
                    Console.WriteLine("   Step 2: Updating document...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/update",
                        new
                        {
                            @namespace = testNamespace,
                            documentId = documentIdStr,
                            title = "Updated Lifecycle Document",
                            content = "Updated content."
                        },
                        timeout: TimeSpan.FromSeconds(5));
                    Console.WriteLine("   Document updated");

                    // Step 3: Search for document
                    Console.WriteLine("   Step 3: Searching for document...");
                    var searchResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/search",
                        new
                        {
                            @namespace = testNamespace,
                            searchTerm = "lifecycle"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var searchJson = JsonNode.Parse(searchResponse.GetRawText())?.AsObject();
                    var totalResults = searchJson?["totalResults"]?.GetValue<int>() ?? 0;
                    if (totalResults == 0)
                    {
                        Console.WriteLine("   Search found no results");
                        return false;
                    }
                    Console.WriteLine($"   Search found {totalResults} results");

                    // Step 4: Delete document
                    Console.WriteLine("   Step 4: Deleting document...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/delete",
                        new
                        {
                            @namespace = testNamespace,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5));
                    Console.WriteLine("   Document deleted to trashcan");

                    // Step 5: List trashcan
                    Console.WriteLine("   Step 5: Verifying in trashcan...");
                    var trashResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/trashcan",
                        new
                        {
                            @namespace = testNamespace
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var trashJson = JsonNode.Parse(trashResponse.GetRawText())?.AsObject();
                    var items = trashJson?["items"]?.AsArray();
                    var inTrashcan = items?.Any(i =>
                        i?.AsObject()?["documentId"]?.GetValue<string>() == documentIdStr) ?? false;

                    if (!inTrashcan)
                    {
                        Console.WriteLine("   Document not found in trashcan");
                        return false;
                    }
                    Console.WriteLine("   Document found in trashcan");

                    // Step 6: Recover document
                    Console.WriteLine("   Step 6: Recovering document...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/recover",
                        new
                        {
                            @namespace = testNamespace,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5));
                    Console.WriteLine("   Document recovered");

                    // Step 7: Verify recovery
                    Console.WriteLine("   Step 7: Verifying recovery...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/get",
                        new
                        {
                            @namespace = testNamespace,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var docNode = getJson?["document"]?.AsObject();
                    if (docNode == null)
                    {
                        Console.WriteLine("   Recovery verification failed");
                        return false;
                    }
                    Console.WriteLine("   Recovery verified");

                    // Step 8: Delete and purge
                    Console.WriteLine("   Step 8: Deleting and purging permanently...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/delete",
                        new
                        {
                            @namespace = testNamespace,
                            documentId = documentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    var purgeResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/purge",
                        new
                        {
                            @namespace = testNamespace,
                            documentIds = new[] { documentIdStr }
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var purgeJson = JsonNode.Parse(purgeResponse.GetRawText())?.AsObject();
                    var purgedCount = purgeJson?["purgedCount"]?.GetValue<int>() ?? 0;
                    Console.WriteLine($"   Purged {purgedCount} documents");

                    return purgedCount == 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Lifecycle test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation complete lifecycle test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation complete lifecycle test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation lifecycle test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestListDocumentsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation List Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/list via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var testNamespace = $"ws-list-{DateTime.Now.Ticks}";

                try
                {
                    // Create multiple documents
                    Console.WriteLine("   Creating documents for list test...");
                    for (int i = 1; i <= 3; i++)
                    {
                        await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/documentation/create",
                            new
                            {
                                @namespace = testNamespace,
                                slug = $"list-doc-{i}",
                                title = $"List Test Document {i}",
                                content = $"Content for document {i}.",
                                category = "GettingStarted"
                            },
                            timeout: TimeSpan.FromSeconds(5));
                    }

                    // List documents
                    Console.WriteLine("   Invoking /documentation/list...");
                    var listResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/list",
                        new
                        {
                            @namespace = testNamespace,
                            page = 1,
                            pageSize = 10
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var listJson = JsonNode.Parse(listResponse.GetRawText())?.AsObject();
                    var documents = listJson?["documents"]?.AsArray();
                    var totalCount = listJson?["totalCount"]?.GetValue<int>() ?? 0;

                    Console.WriteLine($"   List returned {totalCount} documents");
                    return documents != null && totalCount >= 3;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation list test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation list test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation list test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestGetBySlugViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Get by Slug Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/get with slug via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var uniqueSlug = $"ws-slug-{DateTime.Now.Ticks}";

                try
                {
                    // Create a document
                    Console.WriteLine("   Creating document for slug test...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            slug = uniqueSlug,
                            title = "Slug Test Document",
                            content = "Content for slug test."
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    // Get by slug (not documentId)
                    Console.WriteLine($"   Invoking /documentation/get with slug={uniqueSlug}...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/get",
                        new
                        {
                            @namespace = TEST_NAMESPACE,
                            slug = uniqueSlug
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var docNode = getJson?["document"]?.AsObject();
                    var retrievedSlug = docNode?["slug"]?.GetValue<string>();
                    var retrievedTitle = docNode?["title"]?.GetValue<string>();

                    Console.WriteLine($"   Retrieved document: {retrievedSlug} ({retrievedTitle})");
                    return retrievedSlug == uniqueSlug && retrievedTitle == "Slug Test Document";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation get by slug test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation get by slug test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation get by slug test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestSuggestRelatedViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Suggest Related Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/suggest via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var testNamespace = $"ws-suggest-{DateTime.Now.Ticks}";

                try
                {
                    // Create related documents with shared tags
                    Console.WriteLine("   Creating related documents...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = testNamespace,
                            slug = "main-doc",
                            title = "Main Document",
                            content = "Main document content.",
                            category = "GettingStarted",
                            tags = new[] { "api", "reference" }
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var mainDocId = JsonNode.Parse(createResponse.GetRawText())?["documentId"]?.GetValue<string>();

                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = testNamespace,
                            slug = "related-doc",
                            title = "Related Document",
                            content = "Related document content.",
                            category = "GettingStarted",
                            tags = new[] { "api", "examples" }
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    // Get suggestions
                    Console.WriteLine("   Invoking /documentation/suggest...");
                    var suggestResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/suggest",
                        new
                        {
                            @namespace = testNamespace,
                            suggestionSource = "Document_id",
                            sourceValue = mainDocId
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var suggestJson = JsonNode.Parse(suggestResponse.GetRawText())?.AsObject();
                    var suggestions = suggestJson?["suggestions"]?.AsArray();

                    Console.WriteLine($"   Received {suggestions?.Count ?? 0} suggestions");
                    return suggestions != null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation suggest related test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation suggest related test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation suggest related test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestNamespaceStatsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Namespace Stats Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/stats via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var testNamespace = $"ws-stats-{DateTime.Now.Ticks}";

                try
                {
                    // Create documents in different categories
                    Console.WriteLine("   Creating documents for stats test...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = testNamespace,
                            slug = "getting-started-doc",
                            title = "Getting Started",
                            content = "Content.",
                            category = "GettingStarted"
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = testNamespace,
                            slug = "tutorial-doc",
                            title = "Tutorial",
                            content = "Content.",
                            category = "tutorials"
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    // Get stats
                    Console.WriteLine("   Invoking /documentation/stats...");
                    var statsResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/stats",
                        new
                        {
                            @namespace = testNamespace
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var statsJson = JsonNode.Parse(statsResponse.GetRawText())?.AsObject();
                    var documentCount = statsJson?["documentCount"]?.GetValue<int>() ?? 0;
                    var categoryCounts = statsJson?["categoryCounts"]?.AsObject();

                    Console.WriteLine($"   Namespace has {documentCount} documents");
                    Console.WriteLine($"   Categories: {categoryCounts?.ToJsonString()}");

                    return documentCount >= 2 && categoryCounts != null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation namespace stats test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation namespace stats test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation namespace stats test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestBulkUpdateViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Bulk Update Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/bulk-update via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var testNamespace = $"ws-bulk-update-{DateTime.Now.Ticks}";
                var documentIds = new List<string>();

                try
                {
                    // Create multiple documents
                    Console.WriteLine("   Creating documents for bulk update...");
                    for (int i = 1; i <= 3; i++)
                    {
                        var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/documentation/create",
                            new
                            {
                                @namespace = testNamespace,
                                slug = $"bulk-update-{i}",
                                title = $"Bulk Update Document {i}",
                                content = $"Original content {i}."
                            },
                            timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                        var docId = JsonNode.Parse(createResponse.GetRawText())?["documentId"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(docId))
                        {
                            documentIds.Add(docId);
                        }
                    }

                    // Bulk update (add tags to all)
                    Console.WriteLine($"   Invoking /documentation/bulk-update for {documentIds.Count} documents...");
                    var bulkResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/bulk-update",
                        new
                        {
                            @namespace = testNamespace,
                            documentIds = documentIds,
                            tags = new[] { "bulk-updated", "test" }
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var bulkJson = JsonNode.Parse(bulkResponse.GetRawText())?.AsObject();
                    var succeeded = bulkJson?["succeeded"]?.AsArray();
                    var failed = bulkJson?["failed"]?.AsArray();

                    Console.WriteLine($"   Succeeded: {succeeded?.Count ?? 0}, Failed: {failed?.Count ?? 0}");
                    return succeeded?.Count == documentIds.Count && (failed?.Count ?? 0) == 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation bulk update test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation bulk update test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation bulk update test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestBulkDeleteViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Bulk Delete Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/bulk-delete via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var testNamespace = $"ws-bulk-delete-{DateTime.Now.Ticks}";
                var documentIds = new List<string>();

                try
                {
                    // Create multiple documents
                    Console.WriteLine("   Creating documents for bulk delete...");
                    for (int i = 1; i <= 3; i++)
                    {
                        var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/documentation/create",
                            new
                            {
                                @namespace = testNamespace,
                                slug = $"bulk-delete-{i}",
                                title = $"Bulk Delete Document {i}",
                                content = $"Content {i}."
                            },
                            timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                        var docId = JsonNode.Parse(createResponse.GetRawText())?["documentId"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(docId))
                        {
                            documentIds.Add(docId);
                        }
                    }

                    // Bulk delete
                    Console.WriteLine($"   Invoking /documentation/bulk-delete for {documentIds.Count} documents...");
                    var bulkResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/bulk-delete",
                        new
                        {
                            @namespace = testNamespace,
                            documentIds = documentIds
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var bulkJson = JsonNode.Parse(bulkResponse.GetRawText())?.AsObject();
                    var succeeded = bulkJson?["succeeded"]?.AsArray();
                    var failed = bulkJson?["failed"]?.AsArray();

                    Console.WriteLine($"   Succeeded: {succeeded?.Count ?? 0}, Failed: {failed?.Count ?? 0}");
                    return succeeded?.Count == documentIds.Count && (failed?.Count ?? 0) == 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation bulk delete test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation bulk delete test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation bulk delete test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestImportDocumentationViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Import Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/import via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var testNamespace = $"ws-import-{DateTime.Now.Ticks}";

                try
                {
                    // Import multiple documents at once
                    Console.WriteLine("   Invoking /documentation/import...");
                    var importResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/import",
                        new
                        {
                            @namespace = testNamespace,
                            documents = new[]
                            {
                                new
                                {
                                    slug = "import-doc-1",
                                    title = "Imported Document 1",
                                    content = "Content for imported document 1.",
                                    category = "GettingStarted"
                                },
                                new
                                {
                                    slug = "import-doc-2",
                                    title = "Imported Document 2",
                                    content = "Content for imported document 2.",
                                    category = "tutorials"
                                },
                                new
                                {
                                    slug = "import-doc-3",
                                    title = "Imported Document 3",
                                    content = "Content for imported document 3.",
                                    category = "ApiReference"
                                }
                            }
                        },
                        timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

                    var importJson = JsonNode.Parse(importResponse.GetRawText())?.AsObject();
                    var created = importJson?["created"]?.GetValue<int>() ?? 0;
                    var updated = importJson?["updated"]?.GetValue<int>() ?? 0;
                    var failed = importJson?["failed"]?.AsArray();

                    Console.WriteLine($"   Import results: created={created}, updated={updated}, failed={failed?.Count ?? 0}");

                    // Verify by listing
                    var listResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/list",
                        new
                        {
                            @namespace = testNamespace
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var listJson = JsonNode.Parse(listResponse.GetRawText())?.AsObject();
                    var totalCount = listJson?["totalCount"]?.GetValue<int>() ?? 0;

                    Console.WriteLine($"   Verified: {totalCount} documents in namespace");
                    return created == 3 && totalCount == 3;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation import test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation import test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation import test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    #region Repository Binding Tests

    private const string REPO_TEST_NAMESPACE = "ws-repo-test";
    private const string FIXTURE_REPO_URL = "file:///fixtures/test-docs-repo";

    private void TestRepositoryBindViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Repository Bind Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/repo/bind via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                try
                {
                    // First unbind if already bound (cleanup from previous run)
                    try
                    {
                        await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/documentation/repo/unbind",
                            new
                            {
                                @namespace = REPO_TEST_NAMESPACE,
                                deleteDocuments = true
                            },
                            timeout: TimeSpan.FromSeconds(5));
                    }
                    catch
                    {
                        // Ignore - namespace may not be bound
                    }

                    // Bind the fixture repository
                    Console.WriteLine($"   Binding fixture repository to namespace {REPO_TEST_NAMESPACE}...");
                    var bindResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/repo/bind",
                        new
                        {
                            @namespace = REPO_TEST_NAMESPACE,
                            repositoryUrl = FIXTURE_REPO_URL,
                            branch = "main",
                            filePatterns = new[] { "**/*.md" },
                            excludePatterns = new[] { "drafts/**" },
                            categoryMapping = new Dictionary<string, string>
                            {
                                { "guides/", "Guide" },
                                { "api/", "Reference" },
                                { "tutorials/", "Tutorial" }
                            },
                            defaultCategory = "Other"
                        },
                        timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

                    var bindJson = JsonNode.Parse(bindResponse.GetRawText())?.AsObject();
                    var bindingIdStr = bindJson?["bindingId"]?.GetValue<string>();
                    var responseNamespace = bindJson?["namespace"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(bindingIdStr))
                    {
                        Console.WriteLine("   BindRepository returned no bindingId");
                        return false;
                    }

                    Console.WriteLine($"   Bound repository: bindingId={bindingIdStr}, namespace={responseNamespace}");
                    return responseNamespace == REPO_TEST_NAMESPACE;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation repo bind test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation repo bind test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation repo bind test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRepositorySyncViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Repository Sync Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/repo/sync via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                try
                {
                    Console.WriteLine($"   Syncing repository for namespace {REPO_TEST_NAMESPACE}...");
                    var syncResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/repo/sync",
                        new
                        {
                            @namespace = REPO_TEST_NAMESPACE,
                            force = true
                        },
                        timeout: TimeSpan.FromSeconds(30))).GetResultOrThrow();

                    var syncJson = JsonNode.Parse(syncResponse.GetRawText())?.AsObject();
                    var syncIdStr = syncJson?["syncId"]?.GetValue<string>();
                    var status = syncJson?["status"]?.GetValue<string>();
                    var docsCreated = syncJson?["documentsCreated"]?.GetValue<int>() ?? 0;
                    var docsUpdated = syncJson?["documentsUpdated"]?.GetValue<int>() ?? 0;
                    var commitHash = syncJson?["commitHash"]?.GetValue<string>();

                    Console.WriteLine($"   Sync result: status={status}, created={docsCreated}, updated={docsUpdated}");

                    if (status == "Failed")
                    {
                        var errorMessage = syncJson?["errorMessage"]?.GetValue<string>();
                        Console.WriteLine($"   Sync failed: {errorMessage}");
                        return false;
                    }

                    // Expect at least 3 documents from fixture (README, guides/getting-started, api/reference, tutorials/first-tutorial)
                    // Excluding drafts/
                    return docsCreated >= 3 || docsUpdated >= 3;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation repo sync test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation repo sync test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation repo sync test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRepositoryStatusViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Repository Status Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/repo/status via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                try
                {
                    Console.WriteLine($"   Getting status for namespace {REPO_TEST_NAMESPACE}...");
                    var statusResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/repo/status",
                        new
                        {
                            @namespace = REPO_TEST_NAMESPACE
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var statusJson = JsonNode.Parse(statusResponse.GetRawText())?.AsObject();
                    var binding = statusJson?["binding"]?.AsObject();

                    if (binding == null)
                    {
                        Console.WriteLine("   No binding found in status response");
                        return false;
                    }

                    var bindingStatus = binding?["status"]?.GetValue<string>();
                    var documentCount = binding?["documentCount"]?.GetValue<int>() ?? 0;
                    var repositoryUrl = binding?["repositoryUrl"]?.GetValue<string>();

                    Console.WriteLine($"   Status: status={bindingStatus}, documentCount={documentCount}, repositoryUrl={repositoryUrl}");

                    return bindingStatus == "Synced" && documentCount >= 3;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation repo status test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation repo status test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation repo status test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRepositoryUnbindViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Repository Unbind Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/repo/unbind via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                try
                {
                    Console.WriteLine($"   Unbinding repository from namespace {REPO_TEST_NAMESPACE}...");
                    var unbindResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/repo/unbind",
                        new
                        {
                            @namespace = REPO_TEST_NAMESPACE,
                            deleteDocuments = true
                        },
                        timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

                    var unbindJson = JsonNode.Parse(unbindResponse.GetRawText())?.AsObject();
                    var responseNamespace = unbindJson?["namespace"]?.GetValue<string>();
                    var documentsDeleted = unbindJson?["documentsDeleted"]?.GetValue<int>() ?? 0;

                    Console.WriteLine($"   Unbind result: namespace={responseNamespace}, documentsDeleted={documentsDeleted}");

                    // Verify binding is gone by checking status returns 404
                    try
                    {
                        await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/documentation/repo/status",
                            new { @namespace = REPO_TEST_NAMESPACE },
                            timeout: TimeSpan.FromSeconds(5));

                        Console.WriteLine("   Expected 404 for non-existent binding, but got success");
                        return false;
                    }
                    catch (Exception statusEx) when (statusEx.Message.Contains("404") || statusEx.Message.Contains("NotFound"))
                    {
                        Console.WriteLine("   Verified: binding no longer exists (404)");
                    }

                    return responseNamespace == REPO_TEST_NAMESPACE && documentsDeleted >= 3;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation repo unbind test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation repo unbind test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation repo unbind test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    #endregion

    #region Archive Tests

    private const string ARCHIVE_TEST_NAMESPACE = "ws-archive-test";
    private static Guid _wsCreatedArchiveId = Guid.Empty;

    private void TestArchiveCreateViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Archive Create Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/repo/archive/create via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                try
                {
                    // First create a test document
                    Console.WriteLine($"   Creating test document in namespace {ARCHIVE_TEST_NAMESPACE}...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/create",
                        new
                        {
                            @namespace = ARCHIVE_TEST_NAMESPACE,
                            slug = "archive-ws-test-doc",
                            title = "Archive WebSocket Test Doc",
                            category = "Other",
                            content = "Test document for archive WebSocket testing"
                        },
                        timeout: TimeSpan.FromSeconds(10));

                    // Create archive
                    Console.WriteLine($"   Creating archive for namespace {ARCHIVE_TEST_NAMESPACE}...");
                    var archiveResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/repo/archive/create",
                        new
                        {
                            @namespace = ARCHIVE_TEST_NAMESPACE,
                            description = "WebSocket test archive"
                        },
                        timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

                    var archiveJson = JsonNode.Parse(archiveResponse.GetRawText())?.AsObject();
                    var archiveIdStr = archiveJson?["archiveId"]?.GetValue<string>();
                    var docCount = archiveJson?["documentCount"]?.GetValue<int>() ?? 0;
                    var sizeBytes = archiveJson?["sizeBytes"]?.GetValue<int>() ?? 0;

                    Console.WriteLine($"   Created archive: id={archiveIdStr}, docs={docCount}, size={sizeBytes}");

                    if (string.IsNullOrEmpty(archiveIdStr) || !Guid.TryParse(archiveIdStr, out var archiveId))
                    {
                        Console.WriteLine("   CreateArchive returned invalid ID");
                        return false;
                    }

                    _wsCreatedArchiveId = archiveId;
                    return docCount >= 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation archive create test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation archive create test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation archive create test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestArchiveListViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Archive List Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/repo/archive/list via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                try
                {
                    Console.WriteLine($"   Listing archives for namespace {ARCHIVE_TEST_NAMESPACE}...");
                    var listResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/repo/archive/list",
                        new
                        {
                            @namespace = ARCHIVE_TEST_NAMESPACE,
                            limit = 10,
                            offset = 0
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var listJson = JsonNode.Parse(listResponse.GetRawText())?.AsObject();
                    var total = listJson?["total"]?.GetValue<int>() ?? 0;
                    var archives = listJson?["archives"]?.AsArray();

                    Console.WriteLine($"   Found {total} archive(s), array has {archives?.Count ?? 0} items");

                    return total >= 1 && archives != null && archives.Count >= 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation archive list test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation archive list test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation archive list test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestArchiveDeleteViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Archive Delete Test (WebSocket) ===");
        Console.WriteLine("Testing /documentation/repo/archive/delete via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                try
                {
                    if (_wsCreatedArchiveId == Guid.Empty)
                    {
                        Console.WriteLine("   No archive ID from previous test, skipping");
                        return true; // Skip, not fail
                    }

                    Console.WriteLine($"   Deleting archive {_wsCreatedArchiveId}...");
                    var deleteResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/documentation/repo/archive/delete",
                        new
                        {
                            archiveId = _wsCreatedArchiveId
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var deleteJson = JsonNode.Parse(deleteResponse.GetRawText())?.AsObject();
                    var deleted = deleteJson?["deleted"]?.GetValue<bool>() ?? false;

                    Console.WriteLine($"   Delete result: deleted={deleted}");

                    // Cleanup: delete the test document
                    try
                    {
                        var viewResponse = (await adminClient.InvokeAsync<object?, JsonElement>(
                            "GET",
                            $"/documentation/slug/archive-ws-test-doc?ns={ARCHIVE_TEST_NAMESPACE}",
                            null,
                            timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                        var viewJson = JsonNode.Parse(viewResponse.GetRawText())?.AsObject();
                        var docIdStr = viewJson?["documentId"]?.GetValue<string>();
                        if (Guid.TryParse(docIdStr, out var docId))
                        {
                            await adminClient.InvokeAsync<object, JsonElement>(
                                "POST",
                                "/documentation/delete",
                                new
                                {
                                    @namespace = ARCHIVE_TEST_NAMESPACE,
                                    documentId = docId,
                                    permanent = true
                                },
                                timeout: TimeSpan.FromSeconds(5));
                            Console.WriteLine("   Cleaned up test document");
                        }
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }

                    return deleted;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Documentation archive delete test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Documentation archive delete test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Documentation archive delete test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    #endregion
}
