using BeyondImmersion.BannouService.Achievement;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Achievement service HTTP API endpoints.
/// Tests achievement definitions, progress tracking, unlock mechanics, and platform sync.
/// These tests verify basic service operation; detailed validation is in unit tests.
/// </summary>
public class AchievementTestHandler : BaseHttpTestHandler
{
    private static readonly Guid TestGameServiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public override ServiceTest[] GetServiceTests() =>
    [
        // Definition CRUD Tests
        new ServiceTest(TestCreateAchievementDefinition, "CreateDefinition", "Achievement", "Test achievement definition creation"),
        new ServiceTest(TestGetAchievementDefinition, "GetDefinition", "Achievement", "Test achievement definition retrieval"),
        new ServiceTest(TestListAchievementDefinitions, "ListDefinitions", "Achievement", "Test achievement definitions listing"),
        new ServiceTest(TestDeleteAchievementDefinition, "DeleteDefinition", "Achievement", "Test achievement definition deletion"),

        // Progress Tests
        new ServiceTest(TestGetAchievementProgress, "GetProgress", "Achievement", "Test achievement progress retrieval"),
        new ServiceTest(TestUpdateAchievementProgress, "UpdateProgress", "Achievement", "Test achievement progress update"),

        // Unlock Tests
        new ServiceTest(TestUnlockAchievement, "Unlock", "Achievement", "Test achievement unlock"),
        new ServiceTest(TestListUnlockedAchievements, "ListUnlocked", "Achievement", "Test listing unlocked achievements"),

        // Error Handling Tests
        new ServiceTest(TestGetNonExistentAchievement, "GetNonExistent", "Achievement", "Test 404 for non-existent achievement"),
    ];

    /// <summary>
    /// Helper to create a test achievement definition.
    /// </summary>
    private static async Task<AchievementDefinitionResponse> CreateTestAchievementAsync(
        IAchievementClient client,
        string suffix,
        AchievementType achievementType = AchievementType.Standard,
        int? progressTarget = null)
    {
        var achievementId = $"test-ach-{DateTime.Now.Ticks}-{suffix}".ToLowerInvariant();
        return await client.CreateAchievementDefinitionAsync(new CreateAchievementDefinitionRequest
        {
            GameServiceId = TestGameServiceId,
            AchievementId = achievementId,
            DisplayName = $"Test Achievement {suffix}",
            Description = "Test achievement for HTTP tests",
            EntityTypes = new List<EntityType> { EntityType.Account, EntityType.Character },
            AchievementType = achievementType,
            ProgressTarget = progressTarget,
            Points = 10,
            Platforms = new List<Platform> { Platform.Internal }
        });
    }

    private static async Task<TestResult> TestCreateAchievementDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var achievementClient = GetServiceClient<IAchievementClient>();
            var achievementId = $"create-test-{DateTime.Now.Ticks}".ToLowerInvariant();

            var request = new CreateAchievementDefinitionRequest
            {
                GameServiceId = TestGameServiceId,
                AchievementId = achievementId,
                DisplayName = "Create Test Achievement",
                Description = "Test achievement for create test",
                EntityTypes = new List<EntityType> { EntityType.Account },
                AchievementType = AchievementType.Standard,
                Points = 25,
                Platforms = new List<Platform> { Platform.Internal }
            };

            var response = await achievementClient.CreateAchievementDefinitionAsync(request);

            if (response.AchievementId != achievementId)
                return TestResult.Failed($"Achievement ID mismatch: expected '{achievementId}', got '{response.AchievementId}'");

            return TestResult.Successful($"Achievement created: ID={response.AchievementId}");
        }, "Create achievement definition");

    private static async Task<TestResult> TestGetAchievementDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var achievementClient = GetServiceClient<IAchievementClient>();
            var created = await CreateTestAchievementAsync(achievementClient, "get");

            var request = new GetAchievementDefinitionRequest
            {
                GameServiceId = TestGameServiceId,
                AchievementId = created.AchievementId
            };

            var response = await achievementClient.GetAchievementDefinitionAsync(request);

            if (response.AchievementId != created.AchievementId)
                return TestResult.Failed($"Achievement ID mismatch");

            return TestResult.Successful($"Achievement retrieved: ID={response.AchievementId}");
        }, "Get achievement definition");

    private static async Task<TestResult> TestListAchievementDefinitions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var achievementClient = GetServiceClient<IAchievementClient>();

            // Create a test achievement first
            await CreateTestAchievementAsync(achievementClient, "list");

            var request = new ListAchievementDefinitionsRequest
            {
                GameServiceId = TestGameServiceId
            };

            var response = await achievementClient.ListAchievementDefinitionsAsync(request);

            return TestResult.Successful("Achievements listed successfully");
        }, "List achievement definitions");

    private static async Task<TestResult> TestDeleteAchievementDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var achievementClient = GetServiceClient<IAchievementClient>();
            var created = await CreateTestAchievementAsync(achievementClient, "delete");

            await achievementClient.DeleteAchievementDefinitionAsync(new DeleteAchievementDefinitionRequest
            {
                GameServiceId = TestGameServiceId,
                AchievementId = created.AchievementId
            });

            // Verify deletion by trying to get it
            try
            {
                await achievementClient.GetAchievementDefinitionAsync(new GetAchievementDefinitionRequest
                {
                    GameServiceId = TestGameServiceId,
                    AchievementId = created.AchievementId
                });
                return TestResult.Failed("Achievement still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful($"Achievement deleted successfully: ID={created.AchievementId}");
            }
        }, "Delete achievement definition");

    private static async Task<TestResult> TestGetAchievementProgress(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var achievementClient = GetServiceClient<IAchievementClient>();
            var entityId = Guid.NewGuid();

            var request = new GetAchievementProgressRequest
            {
                GameServiceId = TestGameServiceId,
                EntityId = entityId,
                EntityType = EntityType.Account
            };

            var response = await achievementClient.GetAchievementProgressAsync(request);

            // Should return empty or default progress for new entity
            return TestResult.Successful("Progress retrieved successfully");
        }, "Get achievement progress");

    private static async Task<TestResult> TestUpdateAchievementProgress(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var achievementClient = GetServiceClient<IAchievementClient>();
            var created = await CreateTestAchievementAsync(achievementClient, "progress", AchievementType.Progressive, 100);
            var entityId = Guid.NewGuid();

            var request = new UpdateAchievementProgressRequest
            {
                GameServiceId = TestGameServiceId,
                AchievementId = created.AchievementId,
                EntityId = entityId,
                EntityType = EntityType.Account,
                Increment = 25
            };

            var response = await achievementClient.UpdateAchievementProgressAsync(request);

            return TestResult.Successful("Progress updated successfully");
        }, "Update achievement progress");

    private static async Task<TestResult> TestUnlockAchievement(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var achievementClient = GetServiceClient<IAchievementClient>();
            var created = await CreateTestAchievementAsync(achievementClient, "unlock");
            var entityId = Guid.NewGuid();

            var request = new UnlockAchievementRequest
            {
                GameServiceId = TestGameServiceId,
                AchievementId = created.AchievementId,
                EntityId = entityId,
                EntityType = EntityType.Account
            };

            var response = await achievementClient.UnlockAchievementAsync(request);

            if (!response.Unlocked)
                return TestResult.Failed("Achievement was not unlocked");

            return TestResult.Successful($"Achievement unlocked: ID={created.AchievementId}");
        }, "Unlock achievement");

    private static async Task<TestResult> TestListUnlockedAchievements(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var achievementClient = GetServiceClient<IAchievementClient>();
            var created = await CreateTestAchievementAsync(achievementClient, "listunlock");
            var entityId = Guid.NewGuid();

            // First unlock an achievement
            await achievementClient.UnlockAchievementAsync(new UnlockAchievementRequest
            {
                GameServiceId = TestGameServiceId,
                AchievementId = created.AchievementId,
                EntityId = entityId,
                EntityType = EntityType.Account
            });

            // Then list unlocked
            var request = new ListUnlockedAchievementsRequest
            {
                GameServiceId = TestGameServiceId,
                EntityId = entityId,
                EntityType = EntityType.Account
            };

            var response = await achievementClient.ListUnlockedAchievementsAsync(request);

            return TestResult.Successful("Unlocked achievements listed successfully");
        }, "List unlocked achievements");

    private static async Task<TestResult> TestGetNonExistentAchievement(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(
            async () =>
            {
                var achievementClient = GetServiceClient<IAchievementClient>();
                await achievementClient.GetAchievementDefinitionAsync(new GetAchievementDefinitionRequest
                {
                    GameServiceId = TestGameServiceId,
                    AchievementId = "non-existent-achievement"
                });
            },
            404,
            "Get non-existent achievement");
}
