using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Documentation;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for documentation service API endpoints.
/// Tests the documentation service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
/// </summary>
public class DocumentationWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "DOC";
    private const string Description = "Documentation";
    private const string TEST_NAMESPACE = "ws-test-docs";

    public override ServiceTest[] GetServiceTests() =>
    [
        // CRUD operations
        new ServiceTest(TestCreateDocumentViaWebSocket, "Documentation - Create (WebSocket)", "WebSocket",
            "Test document creation via typed proxy"),
        new ServiceTest(TestGetDocumentViaWebSocket, "Documentation - Get (WebSocket)", "WebSocket",
            "Test document retrieval via typed proxy"),
        new ServiceTest(TestUpdateDocumentViaWebSocket, "Documentation - Update (WebSocket)", "WebSocket",
            "Test document update via typed proxy"),
        new ServiceTest(TestDeleteDocumentViaWebSocket, "Documentation - Delete (WebSocket)", "WebSocket",
            "Test document soft-delete via typed proxy"),

        // Search operations
        new ServiceTest(TestSearchDocumentationViaWebSocket, "Documentation - Search (WebSocket)", "WebSocket",
            "Test keyword search via typed proxy"),
        new ServiceTest(TestQueryDocumentationViaWebSocket, "Documentation - Query (WebSocket)", "WebSocket",
            "Test natural language query via typed proxy"),

        // Trashcan operations
        new ServiceTest(TestRecoverDocumentViaWebSocket, "Documentation - Recover (WebSocket)", "WebSocket",
            "Test document recovery from trashcan via typed proxy"),

        // Complete lifecycle
        new ServiceTest(TestDocumentLifecycleViaWebSocket, "Documentation - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete document lifecycle via typed proxy: create -> update -> search -> delete -> recover -> purge"),

        // Additional operations
        new ServiceTest(TestListDocumentsViaWebSocket, "Documentation - List (WebSocket)", "WebSocket",
            "Test document listing with pagination via typed proxy"),
        new ServiceTest(TestGetBySlugViaWebSocket, "Documentation - Get by Slug (WebSocket)", "WebSocket",
            "Test document retrieval by slug via typed proxy"),
        new ServiceTest(TestSuggestRelatedViaWebSocket, "Documentation - Suggest Related (WebSocket)", "WebSocket",
            "Test related topic suggestions via typed proxy"),
        new ServiceTest(TestNamespaceStatsViaWebSocket, "Documentation - Namespace Stats (WebSocket)", "WebSocket",
            "Test namespace statistics via typed proxy"),
        new ServiceTest(TestBulkUpdateViaWebSocket, "Documentation - Bulk Update (WebSocket)", "WebSocket",
            "Test bulk document updates via typed proxy"),
        new ServiceTest(TestBulkDeleteViaWebSocket, "Documentation - Bulk Delete (WebSocket)", "WebSocket",
            "Test bulk document deletion via typed proxy"),
        new ServiceTest(TestImportDocumentationViaWebSocket, "Documentation - Import (WebSocket)", "WebSocket",
            "Test documentation import via typed proxy"),

        // Repository binding operations
        new ServiceTest(TestRepositoryBindViaWebSocket, "Documentation - Repo Bind (WebSocket)", "WebSocket",
            "Test repository binding via typed proxy"),
        new ServiceTest(TestRepositorySyncViaWebSocket, "Documentation - Repo Sync (WebSocket)", "WebSocket",
            "Test repository sync via typed proxy"),
        new ServiceTest(TestRepositoryStatusViaWebSocket, "Documentation - Repo Status (WebSocket)", "WebSocket",
            "Test repository status via typed proxy"),
        new ServiceTest(TestRepositoryUnbindViaWebSocket, "Documentation - Repo Unbind (WebSocket)", "WebSocket",
            "Test repository unbind via typed proxy"),

        // Archive operations
        new ServiceTest(TestArchiveCreateViaWebSocket, "Documentation - Archive Create (WebSocket)", "WebSocket",
            "Test archive creation via typed proxy"),
        new ServiceTest(TestArchiveListViaWebSocket, "Documentation - Archive List (WebSocket)", "WebSocket",
            "Test archive listing via typed proxy"),
        new ServiceTest(TestArchiveDeleteViaWebSocket, "Documentation - Archive Delete (WebSocket)", "WebSocket",
            "Test archive deletion via typed proxy"),
    ];

    #region Helper Methods

    /// <summary>
    /// Creates a test document using typed proxy and returns its ID.
    /// </summary>
    private async Task<Guid?> CreateTestDocumentAsync(BannouClient adminClient, string slugSuffix, string title = "Test Document")
    {
        var uniqueSlug = $"ws-{slugSuffix}-{DateTime.Now.Ticks}";

        var response = await adminClient.Documentation.CreateDocumentAsync(new CreateDocumentRequest
        {
            Namespace = TEST_NAMESPACE,
            Slug = uniqueSlug,
            Title = $"{title} {uniqueSlug}",
            Content = $"Content for {title}.",
            Category = DocumentCategory.GettingStarted,
            Tags = new List<string> { "test", "websocket" }
        }, timeout: TimeSpan.FromSeconds(5));

        if (!response.IsSuccess || response.Result == null)
        {
            Console.WriteLine($"   Failed to create test document: {FormatError(response.Error)}");
            return null;
        }

        return response.Result.DocumentId;
    }

    #endregion

    #region CRUD Operations

    private void TestCreateDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Create Test (WebSocket) ===");
        Console.WriteLine("Testing document creation via typed proxy...");

        RunWebSocketTest("Documentation create test", async adminClient =>
        {
            var uniqueSlug = $"ws-create-{DateTime.Now.Ticks}";

            Console.WriteLine("   Creating document via typed proxy...");
            var response = await adminClient.Documentation.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Slug = uniqueSlug,
                Title = $"WebSocket Test Document {uniqueSlug}",
                Content = "Content created via WebSocket edge test.",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "test", "websocket" }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to create document: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Created document: {result.DocumentId} ({result.Slug})");
            return result.Slug == uniqueSlug;
        });
    }

    private void TestGetDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Get Test (WebSocket) ===");
        Console.WriteLine("Testing document retrieval via typed proxy...");

        RunWebSocketTest("Documentation get test", async adminClient =>
        {
            // Create a document first
            Console.WriteLine("   Creating document for get test...");
            var documentId = await CreateTestDocumentAsync(adminClient, "get", "Get Test Document");
            if (documentId == null)
                return false;

            // Now retrieve it
            Console.WriteLine($"   Retrieving document {documentId}...");
            var response = await adminClient.Documentation.GetDocumentAsync(new GetDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = documentId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to get document: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Retrieved document: {result.Document?.DocumentId} ({result.Document?.Title})");
            return result.Document?.DocumentId == documentId;
        });
    }

    private void TestUpdateDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Update Test (WebSocket) ===");
        Console.WriteLine("Testing document update via typed proxy...");

        RunWebSocketTest("Documentation update test", async adminClient =>
        {
            // Create a document first
            Console.WriteLine("   Creating document for update test...");
            var documentId = await CreateTestDocumentAsync(adminClient, "update", "Original Title");
            if (documentId == null)
                return false;

            // Update it
            Console.WriteLine($"   Updating document {documentId}...");
            var response = await adminClient.Documentation.UpdateDocumentAsync(new UpdateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = documentId.Value,
                Title = "Updated Title",
                Content = "Updated content."
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to update document: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Updated document: {result.DocumentId}");
            return result.DocumentId == documentId.Value;
        });
    }

    private void TestDeleteDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Delete Test (WebSocket) ===");
        Console.WriteLine("Testing document soft-delete via typed proxy...");

        RunWebSocketTest("Documentation delete test", async adminClient =>
        {
            // Create a document first
            Console.WriteLine("   Creating document for delete test...");
            var documentId = await CreateTestDocumentAsync(adminClient, "delete", "Delete Test");
            if (documentId == null)
                return false;

            // Delete it
            Console.WriteLine($"   Deleting document {documentId}...");
            var response = await adminClient.Documentation.DeleteDocumentAsync(new DeleteDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = documentId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to delete document: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Deleted document, recoverable until: {result.RecoverableUntil}");
            return result.DocumentId == documentId;
        });
    }

    #endregion

    #region Search Operations

    private void TestSearchDocumentationViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Search Test (WebSocket) ===");
        Console.WriteLine("Testing keyword search via typed proxy...");

        RunWebSocketTest("Documentation search test", async adminClient =>
        {
            // Create a document first with unique content
            var uniqueKeyword = $"uniquekeyword{DateTime.Now.Ticks}";
            Console.WriteLine($"   Creating document with keyword: {uniqueKeyword}...");

            var createResponse = await adminClient.Documentation.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Slug = $"ws-search-{DateTime.Now.Ticks}",
                Title = $"Search Test Document {uniqueKeyword}",
                Content = $"This document contains the {uniqueKeyword} for testing.",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "test", "search" }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to create document: {FormatError(createResponse.Error)}");
                return false;
            }

            // Wait for indexing
            await Task.Delay(500);

            // Search for it
            Console.WriteLine($"   Searching for keyword: {uniqueKeyword}...");
            var response = await adminClient.Documentation.SearchDocumentationAsync(new SearchDocumentationRequest
            {
                Namespace = TEST_NAMESPACE,
                SearchTerm = uniqueKeyword,
                MaxResults = 10
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to search: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Search results: {result.TotalResults} found");
            return result.TotalResults > 0;
        });
    }

    private void TestQueryDocumentationViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Query Test (WebSocket) ===");
        Console.WriteLine("Testing natural language query via typed proxy...");

        RunWebSocketTest("Documentation query test", async adminClient =>
        {
            // Create a document first
            Console.WriteLine("   Creating document for query test...");
            await CreateTestDocumentAsync(adminClient, "query", "Query Test Document");

            // Query it
            Console.WriteLine("   Querying for test documents...");
            var response = await adminClient.Documentation.QueryDocumentationAsync(new QueryDocumentationRequest
            {
                Namespace = TEST_NAMESPACE,
                Query = "What test documents are available?",
                MaxResults = 5
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to query: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Query results: {result.Results?.Count ?? 0} found");
            Console.WriteLine($"   Summary: {result.VoiceSummary?.Substring(0, Math.Min(100, result.VoiceSummary?.Length ?? 0))}...");
            return result.Results != null;
        });
    }

    #endregion

    #region Trashcan Operations

    private void TestRecoverDocumentViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Recover Test (WebSocket) ===");
        Console.WriteLine("Testing document recovery from trashcan via typed proxy...");

        RunWebSocketTest("Documentation recover test", async adminClient =>
        {
            // Create and delete a document
            Console.WriteLine("   Creating document for recovery test...");
            var documentId = await CreateTestDocumentAsync(adminClient, "recover", "Recovery Test");
            if (documentId == null)
                return false;

            Console.WriteLine($"   Deleting document {documentId}...");
            var deleteResponse = await adminClient.Documentation.DeleteDocumentAsync(new DeleteDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = documentId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!deleteResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to delete document: {FormatError(deleteResponse.Error)}");
                return false;
            }

            // Recover it
            Console.WriteLine($"   Recovering document {documentId}...");
            var response = await adminClient.Documentation.RecoverDocumentAsync(new RecoverDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = documentId.Value
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to recover document: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Recovered document: {response.Result.DocumentId}");
            return response.Result.DocumentId == documentId;
        });
    }

    #endregion

    #region Lifecycle Test

    private void TestDocumentLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete document lifecycle via typed proxy...");

        RunWebSocketTest("Documentation complete lifecycle test", async adminClient =>
        {
            var uniqueSlug = $"ws-lifecycle-{DateTime.Now.Ticks}";

            // Step 1: Create
            Console.WriteLine("   Step 1: Creating document...");
            var createResponse = await adminClient.Documentation.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Slug = uniqueSlug,
                Title = "Lifecycle Test Document",
                Content = "Initial content for lifecycle test.",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "lifecycle", "test" }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create document: {FormatError(createResponse.Error)}");
                return false;
            }

            var documentId = createResponse.Result.DocumentId;
            Console.WriteLine($"   Created document: {documentId}");

            // Step 2: Update
            Console.WriteLine("   Step 2: Updating document...");
            var updateResponse = await adminClient.Documentation.UpdateDocumentAsync(new UpdateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = documentId,
                Title = "Updated Lifecycle Test Document",
                Content = "Updated content for lifecycle test."
            }, timeout: TimeSpan.FromSeconds(5));

            if (!updateResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to update document: {FormatError(updateResponse.Error)}");
                return false;
            }
            Console.WriteLine("   Updated successfully");

            // Step 3: Search
            Console.WriteLine("   Step 3: Searching for document...");
            await Task.Delay(500); // Wait for indexing
            var searchResponse = await adminClient.Documentation.SearchDocumentationAsync(new SearchDocumentationRequest
            {
                Namespace = TEST_NAMESPACE,
                SearchTerm = "lifecycle",
                MaxResults = 10
            }, timeout: TimeSpan.FromSeconds(5));

            if (!searchResponse.IsSuccess || searchResponse.Result == null)
            {
                Console.WriteLine($"   Failed to search: {FormatError(searchResponse.Error)}");
                return false;
            }
            Console.WriteLine($"   Search found {searchResponse.Result.TotalResults} results");

            // Step 4: Delete
            Console.WriteLine("   Step 4: Deleting document...");
            var deleteResponse = await adminClient.Documentation.DeleteDocumentAsync(new DeleteDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = documentId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!deleteResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to delete document: {FormatError(deleteResponse.Error)}");
                return false;
            }
            Console.WriteLine("   Deleted successfully");

            // Step 5: Verify in trashcan
            Console.WriteLine("   Step 5: Checking trashcan...");
            var trashResponse = await adminClient.Documentation.ListTrashcanAsync(new ListTrashcanRequest
            {
                Namespace = TEST_NAMESPACE,
                PageSize = 50
            }, timeout: TimeSpan.FromSeconds(5));

            if (!trashResponse.IsSuccess || trashResponse.Result == null)
            {
                Console.WriteLine($"   Failed to list trashcan: {FormatError(trashResponse.Error)}");
                return false;
            }
            Console.WriteLine($"   Trashcan has {trashResponse.Result.TotalCount} items");

            // Step 6: Recover
            Console.WriteLine("   Step 6: Recovering document...");
            var recoverResponse = await adminClient.Documentation.RecoverDocumentAsync(new RecoverDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = documentId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!recoverResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to recover document: {FormatError(recoverResponse.Error)}");
                return false;
            }
            Console.WriteLine("   Recovered successfully");

            // Step 7: Delete again and purge
            Console.WriteLine("   Step 7: Deleting and purging...");
            await adminClient.Documentation.DeleteDocumentAsync(new DeleteDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = documentId
            }, timeout: TimeSpan.FromSeconds(5));

            var purgeResponse = await adminClient.Documentation.PurgeTrashcanAsync(new PurgeTrashcanRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentIds = new List<Guid> { documentId }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!purgeResponse.IsSuccess || purgeResponse.Result == null)
            {
                Console.WriteLine($"   Failed to purge: {FormatError(purgeResponse.Error)}");
                return false;
            }
            Console.WriteLine($"   Purged {purgeResponse.Result.PurgedCount} document(s)");

            return true;
        });
    }

    #endregion

    #region Additional Operations

    private void TestListDocumentsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation List Test (WebSocket) ===");
        Console.WriteLine("Testing document listing via typed proxy...");

        RunWebSocketTest("Documentation list test", async adminClient =>
        {
            // Create a document first
            Console.WriteLine("   Creating document for list test...");
            await CreateTestDocumentAsync(adminClient, "list", "List Test Document");

            // List documents
            Console.WriteLine("   Listing documents...");
            var response = await adminClient.Documentation.ListDocumentsAsync(new ListDocumentsRequest
            {
                Namespace = TEST_NAMESPACE,
                Category = DocumentCategory.GettingStarted,
                PageSize = 10
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to list documents: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Listed {result.Documents?.Count ?? 0} documents (total: {result.TotalCount})");

            if (result.Documents == null || result.TotalCount < 1)
            {
                Console.WriteLine("   FAILED: Expected at least 1 document in listing");
                return false;
            }

            return true;
        });
    }

    private void TestGetBySlugViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Get by Slug Test (WebSocket) ===");
        Console.WriteLine("Testing document retrieval by slug via typed proxy...");

        RunWebSocketTest("Documentation get by slug test", async adminClient =>
        {
            var uniqueSlug = $"ws-getslug-{DateTime.Now.Ticks}";

            // Create a document
            Console.WriteLine($"   Creating document with slug: {uniqueSlug}...");
            var createResponse = await adminClient.Documentation.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Slug = uniqueSlug,
                Title = "Get By Slug Test",
                Content = "Content for slug test.",
                Category = DocumentCategory.GettingStarted
            }, timeout: TimeSpan.FromSeconds(5));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to create document: {FormatError(createResponse.Error)}");
                return false;
            }

            // Retrieve by slug
            Console.WriteLine($"   Retrieving by slug: {uniqueSlug}...");
            var response = await adminClient.Documentation.GetDocumentAsync(new GetDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Slug = uniqueSlug
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to get document by slug: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Retrieved: {response.Result.Document?.Title}");
            return response.Result.Document?.Slug == uniqueSlug;
        });
    }

    private void TestSuggestRelatedViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Suggest Related Test (WebSocket) ===");
        Console.WriteLine("Testing related topic suggestions via typed proxy...");

        RunWebSocketTest("Documentation suggest related test", async adminClient =>
        {
            // Create a document first
            Console.WriteLine("   Creating document for suggest test...");
            var documentId = await CreateTestDocumentAsync(adminClient, "suggest", "Suggest Test");
            if (documentId == null)
                return false;

            // Get suggestions
            Console.WriteLine("   Getting related suggestions...");
            var response = await adminClient.Documentation.SuggestRelatedTopicsAsync(new SuggestRelatedRequest
            {
                Namespace = TEST_NAMESPACE,
                SuggestionSource = SuggestionSource.DocumentId,
                SourceValue = documentId.Value.ToString(),
                MaxSuggestions = 5
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to get suggestions: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Suggestions: {result.Suggestions?.Count ?? 0}");
            Console.WriteLine($"   Voice prompt: {result.VoicePrompt?.Substring(0, Math.Min(50, result.VoicePrompt?.Length ?? 0))}...");
            return result.Suggestions != null;
        });
    }

    private void TestNamespaceStatsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Namespace Stats Test (WebSocket) ===");
        Console.WriteLine("Testing namespace statistics via typed proxy...");

        RunWebSocketTest("Documentation namespace stats test", async adminClient =>
        {
            // Create a document first to ensure namespace exists
            Console.WriteLine("   Creating document to ensure namespace exists...");
            await CreateTestDocumentAsync(adminClient, "stats", "Stats Test");

            // Get stats
            Console.WriteLine("   Getting namespace stats...");
            var response = await adminClient.Documentation.GetNamespaceStatsAsync(new GetNamespaceStatsRequest
            {
                Namespace = TEST_NAMESPACE
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to get stats: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Total documents: {result.DocumentCount}");
            Console.WriteLine($"   Trashed documents: {result.TrashcanCount}");
            Console.WriteLine($"   Categories: {result.CategoryCounts?.Count ?? 0}");

            if (result.DocumentCount < 1)
            {
                Console.WriteLine("   FAILED: Expected at least 1 document in namespace stats");
                return false;
            }

            return result.CategoryCounts != null;
        });
    }

    private void TestBulkUpdateViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Bulk Update Test (WebSocket) ===");
        Console.WriteLine("Testing bulk document updates via typed proxy...");

        RunWebSocketTest("Documentation bulk update test", async adminClient =>
        {
            // Create multiple documents
            Console.WriteLine("   Creating documents for bulk update...");
            var doc1Id = await CreateTestDocumentAsync(adminClient, "bulk1", "Bulk Test 1");
            var doc2Id = await CreateTestDocumentAsync(adminClient, "bulk2", "Bulk Test 2");

            if (doc1Id == null || doc2Id == null)
                return false;

            // Bulk update
            Console.WriteLine("   Performing bulk update...");
            var response = await adminClient.Documentation.BulkUpdateDocumentsAsync(new BulkUpdateRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentIds = new List<Guid> { doc1Id.Value, doc2Id.Value },
                AddTags = new List<string> { "bulk-updated" }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to bulk update: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Updated {response.Result.Succeeded.Count} documents");
            return response.Result.Succeeded.Count == 2;
        });
    }

    private void TestBulkDeleteViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Bulk Delete Test (WebSocket) ===");
        Console.WriteLine("Testing bulk document deletion via typed proxy...");

        RunWebSocketTest("Documentation bulk delete test", async adminClient =>
        {
            // Create multiple documents
            Console.WriteLine("   Creating documents for bulk delete...");
            var doc1Id = await CreateTestDocumentAsync(adminClient, "bulkdel1", "Bulk Delete 1");
            var doc2Id = await CreateTestDocumentAsync(adminClient, "bulkdel2", "Bulk Delete 2");

            if (doc1Id == null || doc2Id == null)
                return false;

            // Bulk delete
            Console.WriteLine("   Performing bulk delete...");
            var response = await adminClient.Documentation.BulkDeleteDocumentsAsync(new BulkDeleteRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentIds = new List<Guid> { doc1Id.Value, doc2Id.Value }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to bulk delete: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Deleted {response.Result.Succeeded.Count} documents");
            return response.Result.Succeeded.Count == 2;
        });
    }

    private void TestImportDocumentationViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Import Test (WebSocket) ===");
        Console.WriteLine("Testing documentation import via typed proxy...");

        RunWebSocketTest("Documentation import test", async adminClient =>
        {
            Console.WriteLine("   Importing documentation...");
            var response = await adminClient.Documentation.ImportDocumentationAsync(new ImportDocumentationRequest
            {
                Namespace = TEST_NAMESPACE,
                Documents = new List<ImportDocument>
                {
                    new ImportDocument
                    {
                        Slug = $"import-test-{DateTime.Now.Ticks}",
                        Title = "Imported Document",
                        Content = "Content from import test.",
                        Category = DocumentCategory.GettingStarted
                    }
                }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to import: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Imported: {response.Result.Created}, Skipped: {response.Result.Skipped}");
            return response.Result.Created > 0;
        });
    }

    #endregion

    #region Repository Binding Operations

    private void TestRepositoryBindViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Repo Bind Test (WebSocket) ===");
        Console.WriteLine("Testing repository binding via typed proxy...");

        RunWebSocketTest("Documentation repo bind test", async adminClient =>
        {
            var testNamespace = $"ws-repo-bind-{DateTime.Now.Ticks}";

            Console.WriteLine($"   Binding repository to namespace: {testNamespace}...");
            var response = await adminClient.Documentation.BindRepositoryAsync(new BindRepositoryRequest
            {
                Owner = "ws-edge-tester",
                Namespace = testNamespace,
                RepositoryUrl = "https://github.com/test/docs.git",
                Branch = "main"
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to bind repository: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Bound repository, binding ID: {response.Result.BindingId}");
            var passed = response.Result.BindingId != Guid.Empty;

            // Clean up: unbind so background scheduler doesn't retry a repo that requires auth
            await adminClient.Documentation.UnbindRepositoryAsync(new UnbindRepositoryRequest
            {
                Namespace = testNamespace
            }, timeout: TimeSpan.FromSeconds(5));

            return passed;
        });
    }

    private void TestRepositorySyncViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Repo Sync Test (WebSocket) ===");
        Console.WriteLine("Testing repository sync via typed proxy...");

        RunWebSocketTest("Documentation repo sync test", async adminClient =>
        {
            // First create a binding
            var testNamespace = $"ws-repo-sync-{DateTime.Now.Ticks}";

            var bindResponse = await adminClient.Documentation.BindRepositoryAsync(new BindRepositoryRequest
            {
                Owner = "ws-edge-tester",
                Namespace = testNamespace,
                RepositoryUrl = "https://github.com/test/docs.git",
                Branch = "main"
            }, timeout: TimeSpan.FromSeconds(5));

            if (!bindResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to create binding for sync test: {FormatError(bindResponse.Error)}");
                return false;
            }

            Console.WriteLine("   Triggering repository sync...");
            var response = await adminClient.Documentation.SyncRepositoryAsync(new SyncRepositoryRequest
            {
                Namespace = testNamespace
            }, timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to sync: {FormatError(response.Error)}");
                // Clean up binding even on failure
                await adminClient.Documentation.UnbindRepositoryAsync(new UnbindRepositoryRequest
                {
                    Namespace = testNamespace
                }, timeout: TimeSpan.FromSeconds(5));
                return false;
            }

            Console.WriteLine($"   Sync status: {response.Result.Status}");
            Console.WriteLine($"   Documents created: {response.Result.DocumentsCreated}, updated: {response.Result.DocumentsUpdated}, deleted: {response.Result.DocumentsDeleted}");

            // The test repo (https://github.com/test/docs.git) requires authentication,
            // so the sync should fail. Validate that the response correctly reports failure.
            var passed = response.Result.SyncId != Guid.Empty
                && response.Result.Status == SyncStatus.Failed
                && !string.IsNullOrEmpty(response.Result.ErrorMessage);

            if (!passed)
            {
                Console.WriteLine($"   UNEXPECTED: Expected sync to fail (repo requires auth), but got status={response.Result.Status}, errorMessage={response.Result.ErrorMessage}");
            }

            // Clean up: unbind so background scheduler doesn't retry a repo that requires auth
            await adminClient.Documentation.UnbindRepositoryAsync(new UnbindRepositoryRequest
            {
                Namespace = testNamespace
            }, timeout: TimeSpan.FromSeconds(5));

            return passed;
        });
    }

    private void TestRepositoryStatusViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Repo Status Test (WebSocket) ===");
        Console.WriteLine("Testing repository status via typed proxy...");

        RunWebSocketTest("Documentation repo status test", async adminClient =>
        {
            // First create a binding
            var testNamespace = $"ws-repo-status-{DateTime.Now.Ticks}";

            var bindResponse = await adminClient.Documentation.BindRepositoryAsync(new BindRepositoryRequest
            {
                Owner = "ws-edge-tester",
                Namespace = testNamespace,
                RepositoryUrl = "https://github.com/test/docs.git",
                Branch = "main"
            }, timeout: TimeSpan.FromSeconds(5));

            if (!bindResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to create binding for status test: {FormatError(bindResponse.Error)}");
                return false;
            }

            Console.WriteLine("   Getting repository status...");
            var response = await adminClient.Documentation.GetRepositoryStatusAsync(new RepositoryStatusRequest
            {
                Namespace = testNamespace
            }, timeout: TimeSpan.FromSeconds(5));

            // Clean up: unbind so background scheduler doesn't retry a repo that requires auth
            await adminClient.Documentation.UnbindRepositoryAsync(new UnbindRepositoryRequest
            {
                Namespace = testNamespace
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to get status: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Binding ID: {response.Result.Binding?.BindingId}");
            Console.WriteLine($"   Repository: {response.Result.Binding?.RepositoryUrl}");
            return response.Result.Binding != null;
        });
    }

    private void TestRepositoryUnbindViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Repo Unbind Test (WebSocket) ===");
        Console.WriteLine("Testing repository unbind via typed proxy...");

        RunWebSocketTest("Documentation repo unbind test", async adminClient =>
        {
            // First create a binding
            var testNamespace = $"ws-repo-unbind-{DateTime.Now.Ticks}";

            var bindResponse = await adminClient.Documentation.BindRepositoryAsync(new BindRepositoryRequest
            {
                Owner = "ws-edge-tester",
                Namespace = testNamespace,
                RepositoryUrl = "https://github.com/test/docs.git",
                Branch = "main"
            }, timeout: TimeSpan.FromSeconds(5));

            if (!bindResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to create binding for unbind test: {FormatError(bindResponse.Error)}");
                return false;
            }

            Console.WriteLine("   Unbinding repository...");
            var response = await adminClient.Documentation.UnbindRepositoryAsync(new UnbindRepositoryRequest
            {
                Namespace = testNamespace
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to unbind: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Unbound successfully, documents deleted: {response.Result.DocumentsDeleted}");
            return response.Result.Namespace == testNamespace;
        });
    }

    #endregion

    #region Archive Operations

    private void TestArchiveCreateViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Archive Create Test (WebSocket) ===");
        Console.WriteLine("Testing archive creation via typed proxy...");

        RunWebSocketTest("Documentation archive create test", async adminClient =>
        {
            // Create some documents first
            Console.WriteLine("   Creating documents for archive...");
            await CreateTestDocumentAsync(adminClient, "archive1", "Archive Test 1");
            await CreateTestDocumentAsync(adminClient, "archive2", "Archive Test 2");

            Console.WriteLine("   Creating archive...");
            var response = await adminClient.Documentation.CreateDocumentationArchiveAsync(new CreateArchiveRequest
            {
                Namespace = TEST_NAMESPACE,
                Owner = "ws-edge-tester",
                Description = $"Archive created via WebSocket test at {DateTime.Now.Ticks}"
            }, timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to create archive: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Created archive: {response.Result.ArchiveId}");
            Console.WriteLine($"   Documents archived: {response.Result.DocumentCount}");
            return response.Result.ArchiveId != Guid.Empty;
        });
    }

    private void TestArchiveListViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Archive List Test (WebSocket) ===");
        Console.WriteLine("Testing archive listing via typed proxy...");

        RunWebSocketTest("Documentation archive list test", async adminClient =>
        {
            Console.WriteLine("   Listing archives...");
            var response = await adminClient.Documentation.ListDocumentationArchivesAsync(new ListArchivesRequest
            {
                Namespace = TEST_NAMESPACE,
                Limit = 10
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to list archives: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine($"   Archives found: {response.Result.Total}");
            return response.Result.Archives != null && response.Result.Total >= 0;
        });
    }

    private void TestArchiveDeleteViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Documentation Archive Delete Test (WebSocket) ===");
        Console.WriteLine("Testing archive deletion via typed proxy...");

        RunWebSocketTest("Documentation archive delete test", async adminClient =>
        {
            // Create an archive first
            Console.WriteLine("   Creating archive for delete test...");
            await CreateTestDocumentAsync(adminClient, "archivedelete", "Archive Delete Test");

            var createResponse = await adminClient.Documentation.CreateDocumentationArchiveAsync(new CreateArchiveRequest
            {
                Namespace = TEST_NAMESPACE,
                Owner = "ws-edge-tester",
                Description = $"Archive to be deleted - {DateTime.Now.Ticks}"
            }, timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create archive: {FormatError(createResponse.Error)}");
                return false;
            }

            var archiveId = createResponse.Result.ArchiveId;

            Console.WriteLine($"   Deleting archive: {archiveId}...");
            var response = await adminClient.Documentation.DeleteDocumentationArchiveAsync(new DeleteArchiveRequest
            {
                ArchiveId = archiveId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to delete archive: {FormatError(response.Error)}");
                return false;
            }

            Console.WriteLine("   Archive deleted successfully");
            return response.Result.Deleted;
        });
    }

    #endregion
}
