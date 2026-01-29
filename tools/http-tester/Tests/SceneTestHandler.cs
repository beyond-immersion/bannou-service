using BeyondImmersion.BannouService.Scene;
using BeyondImmersion.BannouService.Testing;

using SceneModel = BeyondImmersion.BannouService.Scene.Scene;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Scene service HTTP API endpoints.
/// Tests scene CRUD, checkout workflow, validation, and reference tracking.
/// These tests verify basic service operation; detailed validation is in unit tests.
/// </summary>
public class SceneTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Scene CRUD Tests
        new ServiceTest(TestCreateScene, "CreateScene", "Scene", "Test scene creation"),
        new ServiceTest(TestGetScene, "GetScene", "Scene", "Test scene retrieval"),
        new ServiceTest(TestListScenes, "ListScenes", "Scene", "Test listing scenes"),
        new ServiceTest(TestUpdateScene, "UpdateScene", "Scene", "Test scene update"),
        new ServiceTest(TestDeleteScene, "DeleteScene", "Scene", "Test scene deletion"),

        // Validation Tests
        new ServiceTest(TestValidateScene, "ValidateScene", "Scene", "Test scene validation"),
        new ServiceTest(TestRegisterValidationRules, "RegisterValidationRules", "Scene", "Test registering validation rules"),
        new ServiceTest(TestGetValidationRules, "GetValidationRules", "Scene", "Test getting validation rules"),

        // Checkout Workflow Tests
        new ServiceTest(TestCheckoutScene, "CheckoutScene", "Scene", "Test scene checkout"),
        new ServiceTest(TestCommitScene, "CommitScene", "Scene", "Test scene commit"),
        new ServiceTest(TestDiscardCheckout, "DiscardCheckout", "Scene", "Test discarding checkout"),
        new ServiceTest(TestHeartbeatCheckout, "HeartbeatCheckout", "Scene", "Test checkout heartbeat"),

        // Instance Tests
        new ServiceTest(TestInstantiateScene, "InstantiateScene", "Scene", "Test scene instantiation"),
        new ServiceTest(TestDestroyInstance, "DestroyInstance", "Scene", "Test instance destruction"),

        // History and Search Tests
        new ServiceTest(TestGetSceneHistory, "GetSceneHistory", "Scene", "Test scene history retrieval"),
        new ServiceTest(TestSearchScenes, "SearchScenes", "Scene", "Test scene search"),

        // Reference Tracking Tests
        new ServiceTest(TestFindReferences, "FindReferences", "Scene", "Test finding scene references"),
        new ServiceTest(TestFindAssetUsage, "FindAssetUsage", "Scene", "Test finding asset usage"),
        new ServiceTest(TestDuplicateScene, "DuplicateScene", "Scene", "Test scene duplication"),
    ];

    /// <summary>
    /// Helper to create a minimal test scene.
    /// </summary>
    private static SceneModel CreateMinimalScene(string name, SceneType sceneType = SceneType.Room)
    {
        return new SceneModel
        {
            SceneId = Guid.NewGuid(),
            GameId = "test-game",
            SceneType = sceneType,
            Name = name,
            Description = "Test scene for HTTP tests",
            Version = "1.0.0",
            Root = new SceneNode
            {
                NodeId = Guid.NewGuid(),
                RefId = "root_group",
                NodeType = NodeType.Group,
                Name = "RootGroup"
            }
        };
    }

    private static async Task<TestResult> TestCreateScene(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"create-test-{DateTime.Now.Ticks}");

            var response = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            if (response.Scene.SceneId == Guid.Empty)
                return TestResult.Failed("Scene ID is empty");

            return TestResult.Successful($"Scene created: id={response.Scene.SceneId}");
        }, "Create scene");

    private static async Task<TestResult> TestGetScene(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"get-test-{DateTime.Now.Ticks}");

            // Create first
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            // Then get
            var response = await sceneClient.GetSceneAsync(new GetSceneRequest
            {
                SceneId = created.Scene.SceneId
            });

            if (response.Scene.SceneId != created.Scene.SceneId)
                return TestResult.Failed("Scene ID mismatch");

            return TestResult.Successful($"Scene retrieved: id={response.Scene.SceneId}");
        }, "Get scene");

    private static async Task<TestResult> TestListScenes(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();

            var response = await sceneClient.ListScenesAsync(new ListScenesRequest
            {
                GameId = "test-game"
            });

            return TestResult.Successful($"Listed {response.Scenes.Count} scenes");
        }, "List scenes");

    private static async Task<TestResult> TestUpdateScene(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"update-test-{DateTime.Now.Ticks}");

            // Create first
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            // Update
            var toUpdate = created.Scene;
            toUpdate.Description = "Updated description";
            var response = await sceneClient.UpdateSceneAsync(new UpdateSceneRequest
            {
                Scene = toUpdate
            });

            if (response.Scene.Description != "Updated description")
                return TestResult.Failed($"Description not updated: {response.Scene.Description}");

            return TestResult.Successful($"Scene updated: id={response.Scene.SceneId}");
        }, "Update scene");

    private static async Task<TestResult> TestDeleteScene(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"delete-test-{DateTime.Now.Ticks}");

            // Create first
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            // Delete
            var response = await sceneClient.DeleteSceneAsync(new DeleteSceneRequest
            {
                SceneId = created.Scene.SceneId
            });

            if (!response.Deleted)
                return TestResult.Failed("Scene was not deleted");

            return TestResult.Successful($"Scene deleted: id={created.Scene.SceneId}");
        }, "Delete scene");

    private static async Task<TestResult> TestValidateScene(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"validate-test-{DateTime.Now.Ticks}");

            var response = await sceneClient.ValidateSceneAsync(new ValidateSceneRequest
            {
                Scene = scene,
                ApplyGameRules = false
            });

            if (!response.Valid)
                return TestResult.Failed($"Scene validation failed: {response.Errors?.Count ?? 0} errors");

            return TestResult.Successful("Scene validated successfully");
        }, "Validate scene");

    private static async Task<TestResult> TestRegisterValidationRules(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();

            var response = await sceneClient.RegisterValidationRulesAsync(new RegisterValidationRulesRequest
            {
                GameId = "test-game",
                SceneType = SceneType.Room,
                Rules = new List<ValidationRule>
                {
                    new ValidationRule
                    {
                        RuleId = $"test-rule-{DateTime.Now.Ticks}",
                        Description = "Test validation rule",
                        Severity = ValidationSeverity.Warning,
                        RuleType = ValidationRuleType.RequireTag,
                        Config = new ValidationRuleConfig
                        {
                            Tag = "test-tag"
                        }
                    }
                }
            });

            return TestResult.Successful($"Registered {response.RuleCount} validation rules");
        }, "Register validation rules");

    private static async Task<TestResult> TestGetValidationRules(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();

            var response = await sceneClient.GetValidationRulesAsync(new GetValidationRulesRequest
            {
                GameId = "test-game",
                SceneType = SceneType.Room
            });

            return TestResult.Successful($"Retrieved {response.Rules.Count} validation rules");
        }, "Get validation rules");

    private static async Task<TestResult> TestCheckoutScene(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"checkout-test-{DateTime.Now.Ticks}");

            // Create first
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            // Checkout
            var response = await sceneClient.CheckoutSceneAsync(new CheckoutRequest
            {
                SceneId = created.Scene.SceneId
            });

            if (string.IsNullOrEmpty(response.CheckoutToken))
                return TestResult.Failed("Checkout token is empty");

            // Clean up - discard the checkout
            await sceneClient.DiscardCheckoutAsync(new DiscardRequest
            {
                SceneId = created.Scene.SceneId,
                CheckoutToken = response.CheckoutToken
            });

            return TestResult.Successful($"Scene checked out: token={response.CheckoutToken.Substring(0, 8)}...");
        }, "Checkout scene");

    private static async Task<TestResult> TestCommitScene(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"commit-test-{DateTime.Now.Ticks}");

            // Create first
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            // Checkout
            var checkout = await sceneClient.CheckoutSceneAsync(new CheckoutRequest
            {
                SceneId = created.Scene.SceneId
            });

            // Make a change
            checkout.Scene.Description = "Committed change";

            // Commit
            var response = await sceneClient.CommitSceneAsync(new CommitRequest
            {
                SceneId = created.Scene.SceneId,
                CheckoutToken = checkout.CheckoutToken,
                Scene = checkout.Scene
            });

            if (!response.Committed)
                return TestResult.Failed("Commit failed");

            return TestResult.Successful($"Scene committed: newVersion={response.NewVersion}");
        }, "Commit scene");

    private static async Task<TestResult> TestDiscardCheckout(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"discard-test-{DateTime.Now.Ticks}");

            // Create and checkout
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            var checkout = await sceneClient.CheckoutSceneAsync(new CheckoutRequest
            {
                SceneId = created.Scene.SceneId
            });

            // Discard
            var response = await sceneClient.DiscardCheckoutAsync(new DiscardRequest
            {
                SceneId = created.Scene.SceneId,
                CheckoutToken = checkout.CheckoutToken
            });

            if (!response.Discarded)
                return TestResult.Failed("Discard failed");

            return TestResult.Successful("Checkout discarded");
        }, "Discard checkout");

    private static async Task<TestResult> TestHeartbeatCheckout(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"heartbeat-test-{DateTime.Now.Ticks}");

            // Create and checkout
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            var checkout = await sceneClient.CheckoutSceneAsync(new CheckoutRequest
            {
                SceneId = created.Scene.SceneId
            });

            // Heartbeat
            var response = await sceneClient.HeartbeatCheckoutAsync(new HeartbeatRequest
            {
                SceneId = created.Scene.SceneId,
                CheckoutToken = checkout.CheckoutToken
            });

            if (!response.Extended)
                return TestResult.Failed("Heartbeat extension failed");

            // Clean up
            await sceneClient.DiscardCheckoutAsync(new DiscardRequest
            {
                SceneId = created.Scene.SceneId,
                CheckoutToken = checkout.CheckoutToken
            });

            return TestResult.Successful($"Checkout extended: newExpires={response.NewExpiresAt}");
        }, "Heartbeat checkout");

    private static async Task<TestResult> TestInstantiateScene(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"instantiate-test-{DateTime.Now.Ticks}");

            // Create first
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            // Instantiate
            var instanceId = Guid.NewGuid();
            var response = await sceneClient.InstantiateSceneAsync(new InstantiateSceneRequest
            {
                SceneAssetId = created.Scene.SceneId,
                InstanceId = instanceId
            });

            if (response.InstanceId != instanceId)
                return TestResult.Failed("Instance ID mismatch");

            return TestResult.Successful($"Scene instantiated: instanceId={response.InstanceId}");
        }, "Instantiate scene");

    private static async Task<TestResult> TestDestroyInstance(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"destroy-test-{DateTime.Now.Ticks}");

            // Create and instantiate
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            var instanceId = Guid.NewGuid();
            await sceneClient.InstantiateSceneAsync(new InstantiateSceneRequest
            {
                SceneAssetId = created.Scene.SceneId,
                InstanceId = instanceId
            });

            // Destroy
            var response = await sceneClient.DestroyInstanceAsync(new DestroyInstanceRequest
            {
                InstanceId = instanceId,
                SceneAssetId = created.Scene.SceneId
            });

            if (!response.Destroyed)
                return TestResult.Failed("Instance was not destroyed");

            return TestResult.Successful($"Instance destroyed: instanceId={instanceId}");
        }, "Destroy instance");

    private static async Task<TestResult> TestGetSceneHistory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"history-test-{DateTime.Now.Ticks}");

            // Create first
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            // Get history
            var response = await sceneClient.GetSceneHistoryAsync(new HistoryRequest
            {
                SceneId = created.Scene.SceneId
            });

            if (response.SceneId != created.Scene.SceneId)
                return TestResult.Failed("Scene ID mismatch in history");

            return TestResult.Successful($"History retrieved: {response.Versions.Count} versions");
        }, "Get scene history");

    private static async Task<TestResult> TestSearchScenes(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();

            var response = await sceneClient.SearchScenesAsync(new SearchScenesRequest
            {
                Query = "test",
                GameId = "test-game"
            });

            return TestResult.Successful($"Search returned {response.Results.Count} results");
        }, "Search scenes");

    private static async Task<TestResult> TestFindReferences(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"references-test-{DateTime.Now.Ticks}");

            // Create first
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            // Find references
            var response = await sceneClient.FindReferencesAsync(new FindReferencesRequest
            {
                SceneId = created.Scene.SceneId
            });

            return TestResult.Successful($"Found {response.ReferencingScenes.Count} references to scene");
        }, "Find references");

    private static async Task<TestResult> TestFindAssetUsage(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var assetId = Guid.NewGuid();

            var response = await sceneClient.FindAssetUsageAsync(new FindAssetUsageRequest
            {
                AssetId = assetId,
                GameId = "test-game"
            });

            return TestResult.Successful($"Found asset usage in {response.Usages.Count} locations");
        }, "Find asset usage");

    private static async Task<TestResult> TestDuplicateScene(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var sceneClient = GetServiceClient<ISceneClient>();
            var scene = CreateMinimalScene($"duplicate-source-{DateTime.Now.Ticks}");

            // Create source
            var created = await sceneClient.CreateSceneAsync(new CreateSceneRequest
            {
                Scene = scene
            });

            // Duplicate
            var response = await sceneClient.DuplicateSceneAsync(new DuplicateSceneRequest
            {
                SourceSceneId = created.Scene.SceneId,
                NewName = $"duplicate-copy-{DateTime.Now.Ticks}"
            });

            if (response.Scene.SceneId == created.Scene.SceneId)
                return TestResult.Failed("Duplicate has same ID as source");

            return TestResult.Successful($"Scene duplicated: newId={response.Scene.SceneId}");
        }, "Duplicate scene");
}
