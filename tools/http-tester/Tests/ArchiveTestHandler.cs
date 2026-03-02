using BeyondImmersion.BannouService.Documentation;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// HTTP integration tests for documentation archive operations.
/// Tests archive creation, listing, and deletion without Asset Service integration.
/// </summary>
public class ArchiveTestHandler : BaseHttpTestHandler
{
    private const string TEST_NAMESPACE = "test-archive";
    private static Guid _createdArchiveId = Guid.Empty;

    public override ServiceTest[] GetServiceTests()
    {
        return
        [
            // Setup: Create documents to archive
            new(TestCreateDocumentForArchive, "CreateDocumentForArchive", "Documentation",
                "Create a test document for archive testing"),

            // Archive operations
            new(TestCreateArchive, "CreateArchive", "Documentation",
                "Create an archive of namespace documents"),
            new(TestListArchives, "ListArchives", "Documentation",
                "List archives for a namespace"),
            new(TestCreateArchive_EmptyNamespace_400, "CreateArchive_EmptyNamespace_400", "Documentation",
                "Verify 400 for empty namespace"),
            new(TestDeleteArchive, "DeleteArchive", "Documentation",
                "Delete an archive"),
            new(TestDeleteArchive_NotFound_404, "DeleteArchive_NotFound_404", "Documentation",
                "Verify 404 for non-existent archive"),

            // Cleanup: Remove test documents
            new(TestCleanupTestDocuments, "CleanupTestDocuments", "Documentation",
                "Cleanup test documents"),
        ];
    }

    #region Setup and Cleanup

    private static async Task<TestResult> TestCreateDocumentForArchive(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            // Create a few test documents
            for (int i = 1; i <= 3; i++)
            {
                var response = await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = TEST_NAMESPACE,
                    Slug = $"archive-test-doc-{i}",
                    Title = $"Archive Test Document {i}",
                    Category = DocumentCategory.Other,
                    Content = $"This is test document {i} for archive testing."
                });

                if (response.DocumentId == Guid.Empty)
                    return TestResult.Failed($"Failed to create test document {i}");
            }

            return TestResult.Successful("Created 3 test documents for archive testing");
        }, "Create test documents");

    private static async Task<TestResult> TestCleanupTestDocuments(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            // Delete the test documents by slug
            for (int i = 1; i <= 3; i++)
            {
                try
                {
                    await docClient.DeleteDocumentAsync(new DeleteDocumentRequest
                    {
                        Namespace = TEST_NAMESPACE,
                        Slug = $"archive-test-doc-{i}"
                    });
                }
                catch (ApiException)
                {
                    // Document may already be deleted
                }
            }

            return TestResult.Successful("Cleaned up test documents");
        }, "Cleanup test documents");

    #endregion

    #region Archive Tests

    private static async Task<TestResult> TestCreateArchive(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.CreateDocumentationArchiveAsync(new CreateArchiveRequest
            {
                Owner = "http-tester",
                Namespace = TEST_NAMESPACE,
                Description = "Test archive created by HTTP tester"
            });

            if (response.ArchiveId == Guid.Empty)
                return TestResult.Failed("CreateArchive returned empty ID");

            if (response.Namespace != TEST_NAMESPACE)
                return TestResult.Failed($"Namespace mismatch: expected '{TEST_NAMESPACE}', got '{response.Namespace}'");

            if (response.DocumentCount < 3)
                return TestResult.Failed($"Expected at least 3 documents, got {response.DocumentCount}");

            _createdArchiveId = response.ArchiveId;

            return TestResult.Successful($"Created archive: Id={response.ArchiveId}, Docs={response.DocumentCount}, Size={response.SizeBytes} bytes");
        }, "Create archive");

    private static async Task<TestResult> TestListArchives(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.ListDocumentationArchivesAsync(new ListArchivesRequest
            {
                Namespace = TEST_NAMESPACE,
                Limit = 10,
                Offset = 0
            });

            if (response.Archives == null)
                return TestResult.Failed("ListArchives returned null archives list");

            if (response.Total < 1)
                return TestResult.Failed($"Expected at least 1 archive, got {response.Total}");

            var testArchive = response.Archives.FirstOrDefault(a => a.ArchiveId == _createdArchiveId);
            if (testArchive == null)
                return TestResult.Failed($"Created archive {_createdArchiveId} not found in list");

            return TestResult.Successful($"Found {response.Total} archive(s) in namespace");
        }, "List archives");

    private static async Task<TestResult> TestCreateArchive_EmptyNamespace_400(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var docClient = GetServiceClient<IDocumentationClient>();

                await docClient.CreateDocumentationArchiveAsync(new CreateArchiveRequest
                {
                    Namespace = "",
                    Description = "Should fail"
                });
            },
            400,
            "Empty namespace returns 400");

    private static async Task<TestResult> TestDeleteArchive(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            if (_createdArchiveId == Guid.Empty)
                return TestResult.Failed("No archive ID from previous test");

            var response = await docClient.DeleteDocumentationArchiveAsync(new DeleteArchiveRequest
            {
                ArchiveId = _createdArchiveId
            });

            return TestResult.Successful($"Deleted archive {_createdArchiveId}");
        }, "Delete archive");

    private static async Task<TestResult> TestDeleteArchive_NotFound_404(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var docClient = GetServiceClient<IDocumentationClient>();

                await docClient.DeleteDocumentationArchiveAsync(new DeleteArchiveRequest
                {
                    ArchiveId = Guid.NewGuid() // Non-existent
                });
            },
            404,
            "Non-existent archive returns 404");

    #endregion
}
