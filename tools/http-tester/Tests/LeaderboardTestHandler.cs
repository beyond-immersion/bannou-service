using BeyondImmersion.BannouService.Leaderboard;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Leaderboard service HTTP API endpoints.
/// Tests leaderboard definitions, score submission, and ranking queries.
/// These tests verify basic service operation; detailed validation is in unit tests.
/// </summary>
public class LeaderboardTestHandler : BaseHttpTestHandler
{
    private static readonly Guid TestGameServiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public override ServiceTest[] GetServiceTests() =>
    [
        // Definition CRUD Tests
        new ServiceTest(TestCreateLeaderboardDefinition, "CreateDefinition", "Leaderboard", "Test leaderboard definition creation"),
        new ServiceTest(TestGetLeaderboardDefinition, "GetDefinition", "Leaderboard", "Test leaderboard definition retrieval"),
        new ServiceTest(TestListLeaderboardDefinitions, "ListDefinitions", "Leaderboard", "Test leaderboard definitions listing"),
        new ServiceTest(TestDeleteLeaderboardDefinition, "DeleteDefinition", "Leaderboard", "Test leaderboard definition deletion"),

        // Score Submission Tests
        new ServiceTest(TestSubmitScore, "SubmitScore", "Leaderboard", "Test single score submission"),

        // Ranking Query Tests
        new ServiceTest(TestGetEntityRank, "GetEntityRank", "Leaderboard", "Test entity rank retrieval"),
        new ServiceTest(TestGetTopRanks, "GetTopRanks", "Leaderboard", "Test top ranks retrieval"),

        // Error Handling Tests
        new ServiceTest(TestGetNonExistentLeaderboard, "GetNonExistent", "Leaderboard", "Test 404 for non-existent leaderboard"),

        // Additional Coverage Tests
        new ServiceTest(TestUpdateLeaderboardDefinition, "UpdateDefinition", "Leaderboard", "Test leaderboard definition update"),
        new ServiceTest(TestSubmitScoreBatch, "SubmitScoreBatch", "Leaderboard", "Test batch score submission"),
        new ServiceTest(TestGetRanksAround, "GetRanksAround", "Leaderboard", "Test getting ranks around an entity"),
        new ServiceTest(TestCreateSeason, "CreateSeason", "Leaderboard", "Test creating a leaderboard season"),
        new ServiceTest(TestGetSeason, "GetSeason", "Leaderboard", "Test getting season details"),
    ];

    /// <summary>
    /// Helper to create a test leaderboard definition.
    /// </summary>
    private static async Task<LeaderboardDefinitionResponse> CreateTestLeaderboardAsync(
        ILeaderboardClient client,
        string suffix)
    {
        var leaderboardId = $"test-lb-{DateTime.Now.Ticks}-{suffix}".ToLowerInvariant();
        return await client.CreateLeaderboardDefinitionAsync(new CreateLeaderboardDefinitionRequest
        {
            GameServiceId = TestGameServiceId,
            LeaderboardId = leaderboardId,
            DisplayName = $"Test Leaderboard {suffix}",
            Description = "Test leaderboard for HTTP tests",
            EntityTypes = new List<EntityType> { EntityType.Account, EntityType.Character },
            SortOrder = SortOrder.Descending,
            UpdateMode = UpdateMode.Replace
        });
    }

    private static async Task<TestResult> TestCreateLeaderboardDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var leaderboardId = $"create-test-{DateTime.Now.Ticks}".ToLowerInvariant();

            var request = new CreateLeaderboardDefinitionRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = leaderboardId,
                DisplayName = "Create Test Leaderboard",
                Description = "Test leaderboard for create test",
                EntityTypes = new List<EntityType> { EntityType.Account },
                SortOrder = SortOrder.Descending,
                UpdateMode = UpdateMode.Replace
            };

            var response = await leaderboardClient.CreateLeaderboardDefinitionAsync(request);

            if (response.LeaderboardId != leaderboardId)
                return TestResult.Failed($"Leaderboard ID mismatch: expected '{leaderboardId}', got '{response.LeaderboardId}'");

            return TestResult.Successful($"Leaderboard created: ID={response.LeaderboardId}");
        }, "Create leaderboard definition");

    private static async Task<TestResult> TestGetLeaderboardDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateTestLeaderboardAsync(leaderboardClient, "get");

            var request = new GetLeaderboardDefinitionRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId
            };

            var response = await leaderboardClient.GetLeaderboardDefinitionAsync(request);

            if (response.LeaderboardId != created.LeaderboardId)
                return TestResult.Failed($"Leaderboard ID mismatch");

            return TestResult.Successful($"Leaderboard retrieved: ID={response.LeaderboardId}");
        }, "Get leaderboard definition");

    private static async Task<TestResult> TestListLeaderboardDefinitions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();

            // Create a test leaderboard first
            await CreateTestLeaderboardAsync(leaderboardClient, "list");

            var request = new ListLeaderboardDefinitionsRequest
            {
                GameServiceId = TestGameServiceId
            };

            var response = await leaderboardClient.ListLeaderboardDefinitionsAsync(request);

            return TestResult.Successful("Leaderboards listed successfully");
        }, "List leaderboard definitions");

    private static async Task<TestResult> TestDeleteLeaderboardDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateTestLeaderboardAsync(leaderboardClient, "delete");

            await leaderboardClient.DeleteLeaderboardDefinitionAsync(new DeleteLeaderboardDefinitionRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId
            });

            // Verify deletion by trying to get it
            try
            {
                await leaderboardClient.GetLeaderboardDefinitionAsync(new GetLeaderboardDefinitionRequest
                {
                    GameServiceId = TestGameServiceId,
                    LeaderboardId = created.LeaderboardId
                });
                return TestResult.Failed("Leaderboard still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful($"Leaderboard deleted successfully: ID={created.LeaderboardId}");
            }
        }, "Delete leaderboard definition");

    private static async Task<TestResult> TestSubmitScore(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateTestLeaderboardAsync(leaderboardClient, "submitscore");
            var entityId = Guid.NewGuid();

            var request = new SubmitScoreRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                EntityId = entityId,
                EntityType = EntityType.Account,
                Score = 1000.0
            };

            var response = await leaderboardClient.SubmitScoreAsync(request);

            if (!response.Accepted)
                return TestResult.Failed("Score was not accepted");

            return TestResult.Successful($"Score submitted: CurrentScore={response.CurrentScore}");
        }, "Submit score");

    private static async Task<TestResult> TestGetEntityRank(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateTestLeaderboardAsync(leaderboardClient, "getrank");
            var entityId = Guid.NewGuid();

            // Submit a score first
            await leaderboardClient.SubmitScoreAsync(new SubmitScoreRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                EntityId = entityId,
                EntityType = EntityType.Account,
                Score = 500.0
            });

            // Get the rank
            var request = new GetEntityRankRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                EntityId = entityId,
                EntityType = EntityType.Account
            };

            var response = await leaderboardClient.GetEntityRankAsync(request);

            return TestResult.Successful($"Entity rank retrieved: Rank={response.Rank}");
        }, "Get entity rank");

    private static async Task<TestResult> TestGetTopRanks(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateTestLeaderboardAsync(leaderboardClient, "topranks");

            // Submit multiple scores
            for (int i = 1; i <= 5; i++)
            {
                await leaderboardClient.SubmitScoreAsync(new SubmitScoreRequest
                {
                    GameServiceId = TestGameServiceId,
                    LeaderboardId = created.LeaderboardId,
                    EntityId = Guid.NewGuid(),
                    EntityType = EntityType.Account,
                    Score = i * 100.0
                });
            }

            var request = new GetTopRanksRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                Count = 10
            };

            var response = await leaderboardClient.GetTopRanksAsync(request);

            return TestResult.Successful("Top ranks retrieved successfully");
        }, "Get top ranks");

    private static async Task<TestResult> TestGetNonExistentLeaderboard(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(
            async () =>
            {
                var leaderboardClient = GetServiceClient<ILeaderboardClient>();
                await leaderboardClient.GetLeaderboardDefinitionAsync(new GetLeaderboardDefinitionRequest
                {
                    GameServiceId = TestGameServiceId,
                    LeaderboardId = "non-existent-leaderboard"
                });
            },
            404,
            "Get non-existent leaderboard");

    // =========================================================================
    // Additional Coverage Tests
    // =========================================================================

    /// <summary>
    /// Helper to create a seasonal test leaderboard definition.
    /// </summary>
    private static async Task<LeaderboardDefinitionResponse> CreateSeasonalLeaderboardAsync(
        ILeaderboardClient client,
        string suffix)
    {
        var leaderboardId = $"test-seasonal-lb-{DateTime.Now.Ticks}-{suffix}".ToLowerInvariant();
        return await client.CreateLeaderboardDefinitionAsync(new CreateLeaderboardDefinitionRequest
        {
            GameServiceId = TestGameServiceId,
            LeaderboardId = leaderboardId,
            DisplayName = $"Seasonal Test Leaderboard {suffix}",
            Description = "Test seasonal leaderboard for HTTP tests",
            EntityTypes = new List<EntityType> { EntityType.Account },
            SortOrder = SortOrder.Descending,
            UpdateMode = UpdateMode.Replace,
            IsSeasonal = true
        });
    }

    private static async Task<TestResult> TestUpdateLeaderboardDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateTestLeaderboardAsync(leaderboardClient, "update");

            var updated = await leaderboardClient.UpdateLeaderboardDefinitionAsync(new UpdateLeaderboardDefinitionRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                DisplayName = "Updated Leaderboard Name",
                Description = "Updated description for test"
            });

            if (updated.DisplayName != "Updated Leaderboard Name")
                return TestResult.Failed($"Display name not updated: {updated.DisplayName}");

            return TestResult.Successful($"Leaderboard updated: ID={updated.LeaderboardId}");
        }, "Update leaderboard definition");

    private static async Task<TestResult> TestSubmitScoreBatch(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateTestLeaderboardAsync(leaderboardClient, "batch");

            var scores = new List<BatchScoreEntry>();
            for (int i = 0; i < 5; i++)
            {
                scores.Add(new BatchScoreEntry
                {
                    EntityId = Guid.NewGuid(),
                    EntityType = EntityType.Account,
                    Score = (i + 1) * 100.0
                });
            }

            var response = await leaderboardClient.SubmitScoreBatchAsync(new SubmitScoreBatchRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                Scores = scores
            });

            if (response.Accepted != 5)
                return TestResult.Failed($"Expected 5 accepted, got: {response.Accepted}");

            return TestResult.Successful($"Batch submitted: accepted={response.Accepted}, rejected={response.Rejected}");
        }, "Submit score batch");

    private static async Task<TestResult> TestGetRanksAround(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateTestLeaderboardAsync(leaderboardClient, "ranksaround");
            var centerEntityId = Guid.NewGuid();

            // Submit scores for multiple entities
            for (int i = 1; i <= 10; i++)
            {
                var entityId = i == 5 ? centerEntityId : Guid.NewGuid();
                await leaderboardClient.SubmitScoreAsync(new SubmitScoreRequest
                {
                    GameServiceId = TestGameServiceId,
                    LeaderboardId = created.LeaderboardId,
                    EntityId = entityId,
                    EntityType = EntityType.Account,
                    Score = i * 100.0
                });
            }

            var response = await leaderboardClient.GetRanksAroundAsync(new GetRanksAroundRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                EntityId = centerEntityId,
                EntityType = EntityType.Account,
                CountBefore = 3,
                CountAfter = 3
            });

            return TestResult.Successful($"Ranks around retrieved: entries={response.Entries.Count}");
        }, "Get ranks around");

    private static async Task<TestResult> TestCreateSeason(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateSeasonalLeaderboardAsync(leaderboardClient, "createseason");

            var response = await leaderboardClient.CreateSeasonAsync(new CreateSeasonRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                SeasonName = "Test Season 1"
            });

            if (response.SeasonNumber < 1)
                return TestResult.Failed($"Invalid season number: {response.SeasonNumber}");

            return TestResult.Successful($"Season created: number={response.SeasonNumber}");
        }, "Create season");

    private static async Task<TestResult> TestGetSeason(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var leaderboardClient = GetServiceClient<ILeaderboardClient>();
            var created = await CreateSeasonalLeaderboardAsync(leaderboardClient, "getseason");

            // Create a season first
            var seasonResponse = await leaderboardClient.CreateSeasonAsync(new CreateSeasonRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                SeasonName = "Get Season Test"
            });

            // Get the season
            var response = await leaderboardClient.GetSeasonAsync(new GetSeasonRequest
            {
                GameServiceId = TestGameServiceId,
                LeaderboardId = created.LeaderboardId,
                SeasonNumber = seasonResponse.SeasonNumber
            });

            if (response.SeasonNumber != seasonResponse.SeasonNumber)
                return TestResult.Failed($"Season number mismatch: expected {seasonResponse.SeasonNumber}, got {response.SeasonNumber}");

            return TestResult.Successful($"Season retrieved: number={response.SeasonNumber}, name={response.SeasonName}");
        }, "Get season");
}
