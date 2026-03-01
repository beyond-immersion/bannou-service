using BeyondImmersion.BannouService.Documentation;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for documentation API endpoints using generated clients.
/// Tests the documentation service APIs directly via NSwag-generated DocumentationClient.
/// </summary>
public class DocumentationTestHandler : BaseHttpTestHandler
{
    private const string TEST_NAMESPACE = "test-docs";

    public override ServiceTest[] GetServiceTests() =>
    [
        // CRUD operations
        new ServiceTest(TestCreateDocument, "CreateDocument", "Documentation", "Test document creation"),
        new ServiceTest(TestGetDocument, "GetDocument", "Documentation", "Test document retrieval by ID"),
        new ServiceTest(TestGetDocumentBySlug, "GetDocumentBySlug", "Documentation", "Test document retrieval by slug"),
        new ServiceTest(TestUpdateDocument, "UpdateDocument", "Documentation", "Test document update"),
        new ServiceTest(TestDeleteDocument, "DeleteDocument", "Documentation", "Test document soft-delete to trashcan"),
        new ServiceTest(TestListDocuments, "ListDocuments", "Documentation", "Test listing documents"),

        // Query and search operations
        new ServiceTest(TestQueryDocumentation, "QueryDocumentation", "Documentation", "Test natural language query"),
        new ServiceTest(TestSearchDocumentation, "SearchDocumentation", "Documentation", "Test keyword search"),
        new ServiceTest(TestSuggestRelatedTopics, "SuggestRelatedTopics", "Documentation", "Test related topic suggestions"),

        // Trashcan operations
        new ServiceTest(TestRecoverDocument, "RecoverDocument", "Documentation", "Test document recovery from trashcan"),
        new ServiceTest(TestListTrashcan, "ListTrashcan", "Documentation", "Test listing trashcan contents"),
        new ServiceTest(TestPurgeTrashcan, "PurgeTrashcan", "Documentation", "Test permanent trashcan purge"),

        // Bulk operations
        new ServiceTest(TestBulkUpdateDocuments, "BulkUpdateDocuments", "Documentation", "Test bulk metadata update"),
        new ServiceTest(TestBulkDeleteDocuments, "BulkDeleteDocuments", "Documentation", "Test bulk soft-delete"),
        new ServiceTest(TestImportDocumentation, "ImportDocumentation", "Documentation", "Test document import"),

        // Statistics
        new ServiceTest(TestGetNamespaceStats, "GetNamespaceStats", "Documentation", "Test namespace statistics"),

        // Error handling
        new ServiceTest(TestGetNonExistentDocument, "GetNonExistentDocument", "Documentation", "Test 404 for non-existent document"),
        new ServiceTest(TestDuplicateSlugConflict, "DuplicateSlugConflict", "Documentation", "Test 409 for duplicate slug"),

        // Complete lifecycle
        new ServiceTest(TestCompleteDocumentLifecycle, "CompleteDocumentLifecycle", "Documentation", "Test complete document lifecycle with trashcan"),

        // Sorting tests
        new ServiceTest(TestListDocumentsWithSorting, "ListDocumentsWithSorting", "Documentation", "Test listing documents with sorting options"),
        new ServiceTest(TestSearchDocumentationWithSorting, "SearchWithSorting", "Documentation", "Test search with sorting options"),
        new ServiceTest(TestListDocumentsWithTagsFilter, "ListDocumentsWithTags", "Documentation", "Test listing documents with tags filter"),
    ];

    private static async Task<TestResult> TestCreateDocument(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var createRequest = new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Title = $"Test Document {DateTime.Now.Ticks}",
                Slug = $"test-doc-{DateTime.Now.Ticks}",
                Content = "This is test content for HTTP integration testing.",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "test", "http-tester" }
            };

            var response = await docClient.CreateDocumentAsync(createRequest);

            if (response.DocumentId == Guid.Empty)
                return TestResult.Failed("Create returned empty ID");

            if (response.Slug != createRequest.Slug)
                return TestResult.Failed($"Slug mismatch: expected '{createRequest.Slug}', got '{response.Slug}'");

            return TestResult.Successful($"Created document: ID={response.DocumentId}, Slug={response.Slug}");
        }, "Create document");

    private static async Task<TestResult> TestGetDocument(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            // Create a document first
            var createRequest = new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Title = "Get Test Document",
                Slug = $"get-test-{DateTime.Now.Ticks}",
                Content = "Content for get test."
            };
            var created = await docClient.CreateDocumentAsync(createRequest);

            // Now retrieve it by ID
            var getRequest = new GetDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = created.DocumentId
            };
            var response = await docClient.GetDocumentAsync(getRequest);

            if (response.Document.DocumentId != created.DocumentId)
                return TestResult.Failed("ID mismatch");

            if (response.Document.Title != createRequest.Title)
                return TestResult.Failed($"Title mismatch: expected '{createRequest.Title}', got '{response.Document.Title}'");

            return TestResult.Successful($"Retrieved document: ID={response.Document.DocumentId}, Title={response.Document.Title}");
        }, "Get document");

    private static async Task<TestResult> TestGetDocumentBySlug(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            // Create a document first
            var slug = $"slug-test-{DateTime.Now.Ticks}";
            var createRequest = new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Title = "Slug Lookup Document",
                Slug = slug,
                Content = "Content for slug test."
            };
            var created = await docClient.CreateDocumentAsync(createRequest);

            // Retrieve by slug
            var getRequest = new GetDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Slug = slug
            };
            var response = await docClient.GetDocumentAsync(getRequest);

            if (response.Document.DocumentId != created.DocumentId)
                return TestResult.Failed("ID mismatch when fetching by slug");

            return TestResult.Successful($"Retrieved document by slug: ID={response.Document.DocumentId}");
        }, "Get document by slug");

    private static async Task<TestResult> TestUpdateDocument(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            // Create a document
            var createRequest = new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Title = "Original Title",
                Slug = $"update-test-{DateTime.Now.Ticks}",
                Content = "Original content"
            };
            var created = await docClient.CreateDocumentAsync(createRequest);

            // Update it
            var updateRequest = new UpdateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = created.DocumentId,
                Title = "Updated Title",
                Content = "Updated content"
            };
            var updateResponse = await docClient.UpdateDocumentAsync(updateRequest);

            if (updateResponse.DocumentId != created.DocumentId)
                return TestResult.Failed("Update response document ID mismatch");

            // Verify by fetching
            var getResponse = await docClient.GetDocumentAsync(new GetDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = created.DocumentId
            });

            if (getResponse.Document.Title != "Updated Title")
                return TestResult.Failed($"Title not updated: expected 'Updated Title', got '{getResponse.Document.Title}'");

            return TestResult.Successful($"Updated document: ID={updateResponse.DocumentId}");
        }, "Update document");

    private static async Task<TestResult> TestDeleteDocument(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            // Create a document
            var createRequest = new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Title = "Delete Test Document",
                Slug = $"delete-test-{DateTime.Now.Ticks}",
                Content = "Delete test content"
            };
            var created = await docClient.CreateDocumentAsync(createRequest);

            // Delete it (soft-delete to trashcan)
            var response = await docClient.DeleteDocumentAsync(new DeleteDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = created.DocumentId
            });

            if (response.DocumentId != created.DocumentId)
                return TestResult.Failed("Delete response document ID mismatch");

            // Verify deletion - should return 404
            try
            {
                await docClient.GetDocumentAsync(new GetDocumentRequest
                {
                    Namespace = TEST_NAMESPACE,
                    DocumentId = created.DocumentId
                });
                return TestResult.Failed("Document still accessible after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful($"Deleted document to trashcan: ID={created.DocumentId}");
        }, "Delete document");

    private static async Task<TestResult> TestListDocuments(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"list-test-{DateTime.Now.Ticks}";

            // Create some documents with category (required for filtering)
            for (int i = 0; i < 3; i++)
            {
                await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = testNamespace,
                    Title = $"List Test Document {i}",
                    Slug = $"list-test-{DateTime.Now.Ticks}-{i}",
                    Content = $"Content {i}",
                    Category = DocumentCategory.GettingStarted
                });
            }

            // List all
            var response = await docClient.ListDocumentsAsync(new ListDocumentsRequest
            {
                Namespace = testNamespace,
                Page = 1,
                PageSize = 10
            });

            if (response.Documents == null || response.Documents.Count < 3)
                return TestResult.Failed($"Expected at least 3 documents, got {response.Documents?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Documents.Count} documents (TotalCount: {response.TotalCount})");
        }, "List documents");

    private static async Task<TestResult> TestQueryDocumentation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"query-test-{DateTime.Now.Ticks}";

            // Create a document with specific content
            await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = testNamespace,
                Title = "Authentication Guide",
                Slug = "auth-guide",
                Content = "This guide explains how to authenticate users using OAuth2 providers."
            });

            // Query for it
            var response = await docClient.QueryDocumentationAsync(new QueryDocumentationRequest
            {
                Namespace = testNamespace,
                Query = "authentication OAuth"
            });

            if (response.Results == null)
                return TestResult.Failed("Query returned null results");

            return TestResult.Successful($"Query returned {response.TotalResults} results");
        }, "Query documentation");

    private static async Task<TestResult> TestSearchDocumentation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"search-test-{DateTime.Now.Ticks}";

            // Create documents with searchable content
            await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = testNamespace,
                Title = "API Reference",
                Slug = "api-reference",
                Content = "Complete API reference for all endpoints."
            });

            // Search
            var response = await docClient.SearchDocumentationAsync(new SearchDocumentationRequest
            {
                Namespace = testNamespace,
                SearchTerm = "API endpoints"
            });

            if (response.Results == null)
                return TestResult.Failed("Search returned null results");

            return TestResult.Successful($"Search found {response.TotalResults} results");
        }, "Search documentation");

    private static async Task<TestResult> TestSuggestRelatedTopics(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"suggest-test-{DateTime.Now.Ticks}";

            // Create related documents
            await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = testNamespace,
                Title = "Getting Started",
                Slug = "getting-started",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "intro", "basics" },
                Content = "Getting started content"
            });

            await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = testNamespace,
                Title = "Quick Start Guide",
                Slug = "quick-start",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "intro", "quick" },
                Content = "Quick start content"
            });

            // Get suggestions
            var response = await docClient.SuggestRelatedTopicsAsync(new SuggestRelatedRequest
            {
                Namespace = testNamespace,
                SuggestionSource = SuggestionSource.Topic,
                SourceValue = "getting started"
            });

            if (response.Suggestions == null)
                return TestResult.Failed("Suggestions returned null");

            return TestResult.Successful($"Got {response.Suggestions.Count} suggestions");
        }, "Suggest related topics");

    private static async Task<TestResult> TestRecoverDocument(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            // Create and delete a document
            var created = await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Title = "Recover Test Document",
                Slug = $"recover-test-{DateTime.Now.Ticks}",
                Content = "Recovery test content"
            });

            await docClient.DeleteDocumentAsync(new DeleteDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = created.DocumentId
            });

            // Recover it
            var response = await docClient.RecoverDocumentAsync(new RecoverDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = created.DocumentId
            });

            if (response.DocumentId != created.DocumentId)
                return TestResult.Failed("Recovered document ID mismatch");

            // Verify it's accessible again
            var retrieved = await docClient.GetDocumentAsync(new GetDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                DocumentId = created.DocumentId
            });

            if (retrieved.Document.DocumentId != created.DocumentId)
                return TestResult.Failed("Retrieved document ID mismatch after recovery");

            return TestResult.Successful($"Recovered document: ID={response.DocumentId}");
        }, "Recover document");

    private static async Task<TestResult> TestListTrashcan(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"trashcan-list-{DateTime.Now.Ticks}";

            // Create and delete some documents
            for (int i = 0; i < 3; i++)
            {
                var created = await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = testNamespace,
                    Title = $"Trashcan Test {i}",
                    Slug = $"trashcan-test-{DateTime.Now.Ticks}-{i}",
                    Content = $"Trashcan content {i}",
                    Category = DocumentCategory.GettingStarted
                });

                await docClient.DeleteDocumentAsync(new DeleteDocumentRequest
                {
                    Namespace = testNamespace,
                    DocumentId = created.DocumentId
                });
            }

            // List trashcan
            var response = await docClient.ListTrashcanAsync(new ListTrashcanRequest
            {
                Namespace = testNamespace,
                Page = 1,
                PageSize = 10
            });

            if (response.Items == null || response.Items.Count < 3)
                return TestResult.Failed($"Expected at least 3 trashcan items, got {response.Items?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Items.Count} trashcan items");
        }, "List trashcan");

    private static async Task<TestResult> TestPurgeTrashcan(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"purge-test-{DateTime.Now.Ticks}";

            // Create and delete a document
            var created = await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = testNamespace,
                Title = "Purge Test Document",
                Slug = $"purge-test-{DateTime.Now.Ticks}",
                Content = "Purge test content",
                Category = DocumentCategory.GettingStarted
            });

            await docClient.DeleteDocumentAsync(new DeleteDocumentRequest
            {
                Namespace = testNamespace,
                DocumentId = created.DocumentId
            });

            // Purge it permanently
            var response = await docClient.PurgeTrashcanAsync(new PurgeTrashcanRequest
            {
                Namespace = testNamespace,
                DocumentIds = new List<Guid> { created.DocumentId }
            });

            if (response.PurgedCount != 1)
                return TestResult.Failed($"Expected 1 purged, got {response.PurgedCount}");

            // Verify it cannot be recovered
            try
            {
                await docClient.RecoverDocumentAsync(new RecoverDocumentRequest
                {
                    Namespace = testNamespace,
                    DocumentId = created.DocumentId
                });
                return TestResult.Failed("Document should not be recoverable after purge");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful($"Purged {response.PurgedCount} documents permanently");
        }, "Purge trashcan");

    private static async Task<TestResult> TestBulkUpdateDocuments(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"bulk-update-{DateTime.Now.Ticks}";

            // Create some documents
            var docIds = new List<Guid>();
            for (int i = 0; i < 3; i++)
            {
                var created = await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = testNamespace,
                    Title = $"Bulk Update Test {i}",
                    Slug = $"bulk-update-{DateTime.Now.Ticks}-{i}",
                    Category = DocumentCategory.GettingStarted,
                    Content = $"Bulk update content {i}"
                });
                docIds.Add(created.DocumentId);
            }

            // Bulk update category
            var response = await docClient.BulkUpdateDocumentsAsync(new BulkUpdateRequest
            {
                Namespace = testNamespace,
                DocumentIds = docIds,
                Category = DocumentCategory.ApiReference,
                AddTags = new List<string> { "bulk-updated" }
            });

            if (response.Succeeded.Count != 3)
                return TestResult.Failed($"Expected 3 succeeded, got {response.Succeeded.Count}");

            return TestResult.Successful($"Bulk updated {response.Succeeded.Count} documents");
        }, "Bulk update documents");

    private static async Task<TestResult> TestBulkDeleteDocuments(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"bulk-delete-{DateTime.Now.Ticks}";

            // Create some documents
            var docIds = new List<Guid>();
            for (int i = 0; i < 3; i++)
            {
                var created = await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = testNamespace,
                    Title = $"Bulk Delete Test {i}",
                    Slug = $"bulk-delete-{DateTime.Now.Ticks}-{i}",
                    Content = $"Bulk delete content {i}"
                });
                docIds.Add(created.DocumentId);
            }

            // Bulk delete
            var response = await docClient.BulkDeleteDocumentsAsync(new BulkDeleteRequest
            {
                Namespace = testNamespace,
                DocumentIds = docIds
            });

            if (response.Succeeded.Count != 3)
                return TestResult.Failed($"Expected 3 succeeded, got {response.Succeeded.Count}");

            return TestResult.Successful($"Bulk deleted {response.Succeeded.Count} documents");
        }, "Bulk delete documents");

    private static async Task<TestResult> TestImportDocumentation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"import-test-{DateTime.Now.Ticks}";

            // Import documents
            var response = await docClient.ImportDocumentationAsync(new ImportDocumentationRequest
            {
                Namespace = testNamespace,
                Documents = new List<ImportDocument>
                {
                    new ImportDocument
                    {
                        Title = "Imported Doc 1",
                        Slug = $"imported-1-{DateTime.Now.Ticks}",
                        Content = "Imported content 1",
                        Category = DocumentCategory.GettingStarted
                    },
                    new ImportDocument
                    {
                        Title = "Imported Doc 2",
                        Slug = $"imported-2-{DateTime.Now.Ticks}",
                        Content = "Imported content 2",
                        Category = DocumentCategory.Tutorials
                    }
                },
                OnConflict = ConflictResolution.Skip
            });

            var importedCount = response.Created + response.Updated;
            if (importedCount < 2)
                return TestResult.Failed($"Expected 2 imported, got {importedCount}");

            return TestResult.Successful($"Imported {response.Created} created, {response.Updated} updated");
        }, "Import documentation");

    private static async Task<TestResult> TestGetNamespaceStats(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"stats-test-{DateTime.Now.Ticks}";

            // Create some documents
            for (int i = 0; i < 3; i++)
            {
                await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = testNamespace,
                    Title = $"Stats Test {i}",
                    Slug = $"stats-test-{DateTime.Now.Ticks}-{i}",
                    Category = DocumentCategory.GettingStarted,
                    Tags = new List<string> { "test", $"tag-{i}" },
                    Content = $"Stats content {i}"
                });
            }

            // Get stats
            var response = await docClient.GetNamespaceStatsAsync(new GetNamespaceStatsRequest
            {
                Namespace = testNamespace
            });

            if (response.DocumentCount < 3)
                return TestResult.Failed($"Expected at least 3 documents, got {response.DocumentCount}");

            return TestResult.Successful($"Stats: {response.DocumentCount} documents, {response.TrashcanCount} in trashcan");
        }, "Get namespace stats");

    private static async Task<TestResult> TestGetNonExistentDocument(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var docClient = GetServiceClient<IDocumentationClient>();
                await docClient.GetDocumentAsync(new GetDocumentRequest
                {
                    Namespace = TEST_NAMESPACE,
                    DocumentId = Guid.NewGuid()
                });
            },
            404,
            "Get non-existent document");

    private static async Task<TestResult> TestDuplicateSlugConflict(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var slug = $"duplicate-test-{DateTime.Now.Ticks}";

            // Create first document
            await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = TEST_NAMESPACE,
                Title = "First Document",
                Slug = slug,
                Content = "First content"
            });

            // Try to create second with same slug - expect 409
            try
            {
                await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = TEST_NAMESPACE,
                    Title = "Second Document",
                    Slug = slug,
                    Content = "Second content"
                });
                return TestResult.Failed("Expected 409 for duplicate slug");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                return TestResult.Successful("Correctly returned 409 for duplicate slug");
            }
        }, "Duplicate slug conflict");

    private static async Task<TestResult> TestCompleteDocumentLifecycle(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testId = DateTime.Now.Ticks;
            var testNamespace = $"lifecycle-{testId}";

            // Step 1: Create document
            Console.WriteLine("  Step 1: Creating document...");
            var createResponse = await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = testNamespace,
                Title = "Lifecycle Test Document",
                Slug = $"lifecycle-{testId}",
                Content = "Initial content for lifecycle testing.",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "lifecycle", "test" }
            });

            // Step 2: Update document
            Console.WriteLine("  Step 2: Updating document...");
            await docClient.UpdateDocumentAsync(new UpdateDocumentRequest
            {
                Namespace = testNamespace,
                DocumentId = createResponse.DocumentId,
                Title = "Updated Lifecycle Document",
                Content = "Updated content."
            });

            // Verify update
            var getResponse = await docClient.GetDocumentAsync(new GetDocumentRequest
            {
                Namespace = testNamespace,
                DocumentId = createResponse.DocumentId
            });

            if (getResponse.Document.Title != "Updated Lifecycle Document")
                return TestResult.Failed("Update did not apply");

            // Step 3: Search for document
            Console.WriteLine("  Step 3: Searching for document...");
            var searchResults = await docClient.SearchDocumentationAsync(new SearchDocumentationRequest
            {
                Namespace = testNamespace,
                SearchTerm = "lifecycle"
            });

            if (searchResults.TotalResults == 0)
                return TestResult.Failed("Search did not find the document");

            // Step 4: Get document by slug
            Console.WriteLine("  Step 4: Getting document by slug...");
            var bySlug = await docClient.GetDocumentAsync(new GetDocumentRequest
            {
                Namespace = testNamespace,
                Slug = $"lifecycle-{testId}"
            });

            if (bySlug.Document.DocumentId != createResponse.DocumentId)
                return TestResult.Failed("Slug lookup returned wrong document");

            // Step 5: Delete document (soft-delete)
            Console.WriteLine("  Step 5: Soft-deleting document...");
            await docClient.DeleteDocumentAsync(new DeleteDocumentRequest
            {
                Namespace = testNamespace,
                DocumentId = createResponse.DocumentId
            });

            // Step 6: Verify in trashcan
            Console.WriteLine("  Step 6: Verifying in trashcan...");
            var trashcan = await docClient.ListTrashcanAsync(new ListTrashcanRequest
            {
                Namespace = testNamespace
            });

            if (trashcan.Items == null || !trashcan.Items.Any(t => t.DocumentId == createResponse.DocumentId))
                return TestResult.Failed("Document not found in trashcan");

            // Step 7: Recover document
            Console.WriteLine("  Step 7: Recovering document...");
            await docClient.RecoverDocumentAsync(new RecoverDocumentRequest
            {
                Namespace = testNamespace,
                DocumentId = createResponse.DocumentId
            });

            // Step 8: Verify recovery
            Console.WriteLine("  Step 8: Verifying recovery...");
            var recovered = await docClient.GetDocumentAsync(new GetDocumentRequest
            {
                Namespace = testNamespace,
                DocumentId = createResponse.DocumentId
            });

            if (recovered.Document.DocumentId != createResponse.DocumentId)
                return TestResult.Failed("Recovery verification failed");

            // Step 9: Delete and purge
            Console.WriteLine("  Step 9: Deleting and purging...");
            await docClient.DeleteDocumentAsync(new DeleteDocumentRequest
            {
                Namespace = testNamespace,
                DocumentId = createResponse.DocumentId
            });

            var purged = await docClient.PurgeTrashcanAsync(new PurgeTrashcanRequest
            {
                Namespace = testNamespace,
                DocumentIds = new List<Guid> { createResponse.DocumentId }
            });

            if (purged.PurgedCount != 1)
                return TestResult.Failed("Purge did not work");

            // Step 10: Verify permanent deletion
            Console.WriteLine("  Step 10: Verifying permanent deletion...");
            try
            {
                await docClient.GetDocumentAsync(new GetDocumentRequest
                {
                    Namespace = testNamespace,
                    DocumentId = createResponse.DocumentId
                });
                return TestResult.Failed("Document still accessible after purge");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful("Complete document lifecycle test passed");
        }, "Complete document lifecycle");

    private static async Task<TestResult> TestListDocumentsWithSorting(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"sort-test-{DateTime.Now.Ticks}";

            // Create documents with different titles
            var titles = new[] { "Zebra Guide", "Apple Tutorial", "Mango Reference" };
            foreach (var title in titles)
            {
                await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = testNamespace,
                    Title = title,
                    Slug = $"sort-{title.ToLower().Replace(" ", "-")}-{DateTime.Now.Ticks}",
                    Content = $"Content for {title}",
                    Category = DocumentCategory.GettingStarted
                });
            }

            // Test sorting by title ascending
            var ascResponse = await docClient.ListDocumentsAsync(new ListDocumentsRequest
            {
                Namespace = testNamespace,
                SortBy = ListSortField.Title,
                SortOrder = SortOrder.Asc
            });

            if (ascResponse.Documents == null || ascResponse.Documents.Count < 3)
                return TestResult.Failed($"Expected 3 documents, got {ascResponse.Documents?.Count ?? 0}");

            // Verify ascending order - first should be Apple
            var ascFirst = ascResponse.Documents.First();
            if (!ascFirst.Title.StartsWith("Apple"))
                return TestResult.Failed($"Expected first document to be 'Apple...', got '{ascFirst.Title}'");

            // Test sorting by title descending
            var descResponse = await docClient.ListDocumentsAsync(new ListDocumentsRequest
            {
                Namespace = testNamespace,
                SortBy = ListSortField.Title,
                SortOrder = SortOrder.Desc
            });

            // Verify descending order - first should be Zebra
            var descFirst = descResponse.Documents.First();
            if (!descFirst.Title.StartsWith("Zebra"))
                return TestResult.Failed($"Expected first document to be 'Zebra...', got '{descFirst.Title}'");

            // Test sorting by created_at
            var createdResponse = await docClient.ListDocumentsAsync(new ListDocumentsRequest
            {
                Namespace = testNamespace,
                SortBy = ListSortField.CreatedAt,
                SortOrder = SortOrder.Desc
            });

            if (createdResponse.Documents == null || createdResponse.Documents.Count < 3)
                return TestResult.Failed("Sort by created_at failed to return documents");

            return TestResult.Successful($"Sorting tests passed: title asc first='{ascFirst.Title}', title desc first='{descFirst.Title}'");
        }, "List documents with sorting");

    private static async Task<TestResult> TestSearchDocumentationWithSorting(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"search-sort-{DateTime.Now.Ticks}";

            // Create documents with searchable content
            var docs = new[]
            {
                ("Zebra API Guide", "The zebra API provides authentication features"),
                ("Apple SDK Reference", "The apple SDK includes authentication methods"),
                ("Mango Integration", "Mango authentication integration guide")
            };

            foreach (var (title, content) in docs)
            {
                await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = testNamespace,
                    Title = title,
                    Slug = $"search-sort-{title.ToLower().Replace(" ", "-")}-{DateTime.Now.Ticks}",
                    Content = content,
                    Category = DocumentCategory.ApiReference
                });
            }

            // Test search with alphabetical sorting
            var alphaResponse = await docClient.SearchDocumentationAsync(new SearchDocumentationRequest
            {
                Namespace = testNamespace,
                SearchTerm = "authentication",
                SortBy = SearchSortBy.Alphabetical
            });

            if (alphaResponse.Results == null)
                return TestResult.Failed("Search returned null results");

            // Test search with relevance sorting (default)
            var relevanceResponse = await docClient.SearchDocumentationAsync(new SearchDocumentationRequest
            {
                Namespace = testNamespace,
                SearchTerm = "authentication",
                SortBy = SearchSortBy.Relevance
            });

            // Test search with recency sorting
            var recencyResponse = await docClient.SearchDocumentationAsync(new SearchDocumentationRequest
            {
                Namespace = testNamespace,
                SearchTerm = "authentication",
                SortBy = SearchSortBy.Recency
            });

            return TestResult.Successful($"Search sorting tests passed: alpha={alphaResponse.TotalResults}, relevance={relevanceResponse.TotalResults}, recency={recencyResponse.TotalResults}");
        }, "Search with sorting");

    private static async Task<TestResult> TestListDocumentsWithTagsFilter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();
            var testNamespace = $"tags-test-{DateTime.Now.Ticks}";

            // Create documents with different tag combinations
            await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = testNamespace,
                Title = "Doc with tag1 and tag2",
                Slug = $"tags-both-{DateTime.Now.Ticks}",
                Content = "Content with both tags",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "tag1", "tag2" }
            });

            await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = testNamespace,
                Title = "Doc with only tag1",
                Slug = $"tags-one-{DateTime.Now.Ticks}",
                Content = "Content with one tag",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "tag1" }
            });

            await docClient.CreateDocumentAsync(new CreateDocumentRequest
            {
                Namespace = testNamespace,
                Title = "Doc with tag3",
                Slug = $"tags-three-{DateTime.Now.Ticks}",
                Content = "Content with different tag",
                Category = DocumentCategory.GettingStarted,
                Tags = new List<string> { "tag3" }
            });

            // Test tagsMatch=all - should only return doc with both tags
            var allResponse = await docClient.ListDocumentsAsync(new ListDocumentsRequest
            {
                Namespace = testNamespace,
                Tags = new List<string> { "tag1", "tag2" },
                TagsMatch = TagMatchMode.All
            });

            if (allResponse.Documents == null || allResponse.Documents.Count != 1)
                return TestResult.Failed($"TagsMatch=All: expected 1 document, got {allResponse.Documents?.Count ?? 0}");

            // Test tagsMatch=any - should return docs with tag1 OR tag2
            var anyResponse = await docClient.ListDocumentsAsync(new ListDocumentsRequest
            {
                Namespace = testNamespace,
                Tags = new List<string> { "tag1", "tag2" },
                TagsMatch = TagMatchMode.Any
            });

            if (anyResponse.Documents == null || anyResponse.Documents.Count != 2)
                return TestResult.Failed($"TagsMatch=Any: expected 2 documents, got {anyResponse.Documents?.Count ?? 0}");

            return TestResult.Successful($"Tags filter tests passed: all={allResponse.Documents.Count}, any={anyResponse.Documents.Count}");
        }, "List documents with tags filter");
}
