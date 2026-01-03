using BeyondImmersion.BannouService.Documentation;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// HTTP integration tests for repository binding operations.
/// Includes pseudo-git fixture tests (always run) and live git tests (conditional).
/// Fixture repo is mounted at /fixtures/test-docs-repo inside the bannou container.
/// </summary>
public class RepositoryBindingTestHandler : BaseHttpTestHandler
{
    private const string TEST_NAMESPACE = "test-repo-binding";

    // Path to fixture repo inside the bannou container (mounted via docker-compose.test.http.yml)
    private const string FIXTURE_REPO_URL = "file:///fixtures/test-docs-repo";

    // Environment variables for live git testing
    private const string LIVE_GIT_URL_ENV = "DOCUMENTATION_GIT_TEST_URL";
    private const string LIVE_GIT_BRANCH_ENV = "DOCUMENTATION_GIT_TEST_BRANCH";
    private const string LIVE_TEST_NAMESPACE = "test-live-git";

    public override ServiceTest[] GetServiceTests()
    {
        var tests = new List<ServiceTest>
        {
            // Pseudo-git fixture tests (always run)
            new(TestBindRepository_Fixture, "BindRepository_Fixture", "Documentation",
                "Bind pseudo-git fixture repository to namespace"),
            new(TestSyncRepository_Fixture, "SyncRepository_Fixture", "Documentation",
                "Sync fixture repository and verify documents created"),
            new(TestGetRepositoryStatus_Fixture, "GetRepositoryStatus_Fixture", "Documentation",
                "Get status of bound fixture repository"),
            new(TestListRepositoryBindings, "ListRepositoryBindings", "Documentation",
                "List all repository bindings"),
            new(TestUpdateRepositoryBinding, "UpdateRepositoryBinding", "Documentation",
                "Update repository binding configuration"),
            new(TestDocumentProtection_403, "DocumentProtection_403", "Documentation",
                "Verify bound namespace returns 403 for direct document creation"),
            new(TestUnbindRepository_Fixture, "UnbindRepository_Fixture", "Documentation",
                "Unbind fixture repository from namespace"),
        };

        // Conditionally add live git tests
        var liveGitUrl = Environment.GetEnvironmentVariable(LIVE_GIT_URL_ENV);
        if (!string.IsNullOrEmpty(liveGitUrl))
        {
            tests.AddRange([
                new(TestBindRepository_Live, "BindRepository_Live", "Documentation",
                    "Bind live git repository (conditional)"),
                new(TestSyncRepository_Live, "SyncRepository_Live", "Documentation",
                    "Sync live repository with real git operations"),
                new(TestUnbindRepository_Live, "UnbindRepository_Live", "Documentation",
                    "Unbind live repository"),
            ]);
        }

        return tests.ToArray();
    }

    #region Fixture Repository Tests

    private static async Task<TestResult> TestBindRepository_Fixture(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.BindRepositoryAsync(new BindRepositoryRequest
            {
                Namespace = TEST_NAMESPACE,
                RepositoryUrl = FIXTURE_REPO_URL,
                Branch = "main",
                FilePatterns = new List<string> { "**/*.md" },
                ExcludePatterns = new List<string> { "drafts/**" },
                CategoryMapping = new Dictionary<string, string>
                {
                    { "guides/", "Guide" },
                    { "api/", "Reference" },
                    { "tutorials/", "Tutorial" }
                },
                DefaultCategory = DocumentCategory.Other
            });

            if (response.BindingId == Guid.Empty)
                return TestResult.Failed("BindRepository returned empty ID");

            if (response.Namespace != TEST_NAMESPACE)
                return TestResult.Failed($"Namespace mismatch: expected '{TEST_NAMESPACE}', got '{response.Namespace}'");

            return TestResult.Successful($"Bound fixture repo: BindingId={response.BindingId}, Status={response.Status}");
        }, "Bind fixture repository");

    private static async Task<TestResult> TestSyncRepository_Fixture(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.SyncRepositoryAsync(new SyncRepositoryRequest
            {
                Namespace = TEST_NAMESPACE,
                Force = true
            });

            if (response.Status == SyncStatus.Failed)
                return TestResult.Failed($"Sync failed: {response.ErrorMessage}");

            // Verify expected document count (README, guides/getting-started, api/reference, tutorials/first-tutorial)
            // Drafts should be excluded
            if (response.DocumentsCreated < 4)
                return TestResult.Failed($"Expected at least 4 docs (excluding drafts), got {response.DocumentsCreated}");

            return TestResult.Successful(
                $"Synced fixture repo: Status={response.Status}, Created={response.DocumentsCreated}, Updated={response.DocumentsUpdated}, CommitHash={response.CommitHash?[..7]}");
        }, "Sync fixture repository");

    private static async Task<TestResult> TestGetRepositoryStatus_Fixture(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.GetRepositoryStatusAsync(new RepositoryStatusRequest
            {
                Namespace = TEST_NAMESPACE
            });

            if (response.Binding == null)
                return TestResult.Failed("GetRepositoryStatus returned null binding");

            if (response.Binding.Namespace != TEST_NAMESPACE)
                return TestResult.Failed($"Namespace mismatch: expected '{TEST_NAMESPACE}', got '{response.Binding.Namespace}'");

            if (response.Binding.Status != BindingStatus.Synced)
                return TestResult.Failed($"Expected status Synced, got {response.Binding.Status}");

            return TestResult.Successful(
                $"Status: {response.Binding.Status}, DocumentCount={response.Binding.DocumentCount}, LastSync={response.LastSync?.CompletedAt}");
        }, "Get repository status");

    private static async Task<TestResult> TestListRepositoryBindings(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.ListRepositoryBindingsAsync(new ListRepositoryBindingsRequest
            {
                Limit = 10,
                Offset = 0
            });

            if (response.Bindings == null || response.Bindings.Count == 0)
                return TestResult.Failed("ListRepositoryBindings returned no bindings");

            var testBinding = response.Bindings.FirstOrDefault(b => b.Namespace == TEST_NAMESPACE);
            if (testBinding == null)
                return TestResult.Failed($"Test namespace '{TEST_NAMESPACE}' not found in bindings list");

            return TestResult.Successful(
                $"Found {response.Bindings.Count} binding(s), Total={response.Total}");
        }, "List repository bindings");

    private static async Task<TestResult> TestUpdateRepositoryBinding(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.UpdateRepositoryBindingAsync(new UpdateRepositoryBindingRequest
            {
                Namespace = TEST_NAMESPACE,
                SyncIntervalMinutes = 120,
                SyncEnabled = true
            });

            if (response.Binding == null)
                return TestResult.Failed("UpdateRepositoryBinding returned null binding");

            if (response.Binding.SyncIntervalMinutes != 120)
                return TestResult.Failed($"SyncIntervalMinutes not updated: expected 120, got {response.Binding.SyncIntervalMinutes}");

            return TestResult.Successful($"Updated binding: SyncIntervalMinutes={response.Binding.SyncIntervalMinutes}");
        }, "Update repository binding");

    private static async Task<TestResult> TestDocumentProtection_403(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var docClient = GetServiceClient<IDocumentationClient>();

                // Attempt to create document in bound namespace - should fail with 403
                await docClient.CreateDocumentAsync(new CreateDocumentRequest
                {
                    Namespace = TEST_NAMESPACE,
                    Slug = "manual-doc",
                    Title = "Manual Document",
                    Category = DocumentCategory.Other,
                    Content = "This should fail - namespace is bound to git repo"
                });
            },
            403,
            "Document creation blocked in bound namespace");

    private static async Task<TestResult> TestUnbindRepository_Fixture(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.UnbindRepositoryAsync(new UnbindRepositoryRequest
            {
                Namespace = TEST_NAMESPACE,
                DeleteDocuments = true
            });

            if (response.Namespace != TEST_NAMESPACE)
                return TestResult.Failed($"UnbindRepository returned wrong namespace: {response.Namespace}");

            // Verify binding is gone - service returns 404 when no binding exists
            try
            {
                await docClient.GetRepositoryStatusAsync(new RepositoryStatusRequest
                {
                    Namespace = TEST_NAMESPACE
                });
                return TestResult.Failed("Expected 404 for non-existent binding, but got success");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected - binding no longer exists
            }

            return TestResult.Successful($"Unbound fixture repo, deleted {response.DocumentsDeleted} documents");
        }, "Unbind fixture repository");

    #endregion

    #region Live Git Repository Tests (Conditional)

    private static async Task<TestResult> TestBindRepository_Live(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var gitUrl = Environment.GetEnvironmentVariable(LIVE_GIT_URL_ENV)
                ?? throw new InvalidOperationException($"{LIVE_GIT_URL_ENV} not set");
            var branch = Environment.GetEnvironmentVariable(LIVE_GIT_BRANCH_ENV) ?? "main";

            var response = await docClient.BindRepositoryAsync(new BindRepositoryRequest
            {
                Namespace = LIVE_TEST_NAMESPACE,
                RepositoryUrl = gitUrl,
                Branch = branch,
                FilePatterns = new List<string> { "**/*.md" },
                ExcludePatterns = new List<string> { ".git/**", "node_modules/**", ".obsidian/**" }
            });

            if (response.BindingId == Guid.Empty)
                return TestResult.Failed("Live BindRepository returned empty ID");

            return TestResult.Successful($"Bound live repo: BindingId={response.BindingId}, URL={gitUrl}");
        }, "Bind live git repository");

    private static async Task<TestResult> TestSyncRepository_Live(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.SyncRepositoryAsync(new SyncRepositoryRequest
            {
                Namespace = LIVE_TEST_NAMESPACE,
                Force = false
            });

            if (response.Status == SyncStatus.Failed)
                return TestResult.Failed($"Live sync failed: {response.ErrorMessage}");

            return TestResult.Successful(
                $"Live sync: Status={response.Status}, Created={response.DocumentsCreated}, Updated={response.DocumentsUpdated}");
        }, "Sync live git repository");

    private static async Task<TestResult> TestUnbindRepository_Live(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var docClient = GetServiceClient<IDocumentationClient>();

            var response = await docClient.UnbindRepositoryAsync(new UnbindRepositoryRequest
            {
                Namespace = LIVE_TEST_NAMESPACE,
                DeleteDocuments = true
            });

            if (response.Namespace != LIVE_TEST_NAMESPACE)
                return TestResult.Failed($"Live unbind returned wrong namespace: {response.Namespace}");

            return TestResult.Successful($"Unbound live repo, deleted {response.DocumentsDeleted} documents");
        }, "Unbind live git repository");

    #endregion
}
