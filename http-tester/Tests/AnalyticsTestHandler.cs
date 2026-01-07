using BeyondImmersion.BannouService.Analytics;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Analytics service HTTP API endpoints.
/// Tests event ingestion, entity summaries, skill ratings, and controller history.
/// These tests verify basic service operation; detailed validation is in unit tests.
/// </summary>
public class AnalyticsTestHandler : BaseHttpTestHandler
{
    private static readonly Guid TestGameServiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public override ServiceTest[] GetServiceTests() =>
    [
        // Event Ingestion Tests
        new ServiceTest(TestIngestEvent, "IngestEvent", "Analytics", "Test single event ingestion"),
        new ServiceTest(TestIngestEventBatch, "IngestEventBatch", "Analytics", "Test batch event ingestion"),

        // Entity Summary Tests
        new ServiceTest(TestGetEntitySummary, "GetEntitySummary", "Analytics", "Test entity summary retrieval"),
        new ServiceTest(TestQueryEntitySummaries, "QueryEntitySummaries", "Analytics", "Test entity summaries query"),

        // Skill Rating Tests
        new ServiceTest(TestGetSkillRating, "GetSkillRating", "Analytics", "Test skill rating retrieval"),

        // Controller History Tests
        new ServiceTest(TestRecordControllerEvent, "RecordControllerEvent", "Analytics", "Test controller event recording"),
        new ServiceTest(TestQueryControllerHistory, "QueryControllerHistory", "Analytics", "Test controller history query"),
    ];

    private static async Task<TestResult> TestIngestEvent(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var analyticsClient = GetServiceClient<IAnalyticsClient>();
            var entityId = Guid.NewGuid();

            var request = new IngestEventRequest
            {
                GameServiceId = TestGameServiceId,
                EventType = "test.event",
                EntityId = entityId,
                EntityType = EntityType.Account
            };

            var response = await analyticsClient.IngestEventAsync(request);

            if (!response.Accepted)
                return TestResult.Failed("Event not accepted");

            return TestResult.Successful($"Event ingested: EventId={response.EventId}");
        }, "Ingest event");

    private static async Task<TestResult> TestIngestEventBatch(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var analyticsClient = GetServiceClient<IAnalyticsClient>();

            var events = Enumerable.Range(1, 5).Select(i => new IngestEventRequest
            {
                GameServiceId = TestGameServiceId,
                EventType = $"batch.event.{i}",
                EntityId = Guid.NewGuid(),
                EntityType = EntityType.Account
            }).ToList();

            var request = new IngestEventBatchRequest
            {
                Events = events
            };

            var response = await analyticsClient.IngestEventBatchAsync(request);

            return TestResult.Successful($"Batch ingested: Accepted={response.Accepted}");
        }, "Ingest event batch");

    private static async Task<TestResult> TestGetEntitySummary(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var analyticsClient = GetServiceClient<IAnalyticsClient>();
            var entityId = Guid.NewGuid();

            // First ingest an event
            await analyticsClient.IngestEventAsync(new IngestEventRequest
            {
                GameServiceId = TestGameServiceId,
                EventType = "summary.test",
                EntityId = entityId,
                EntityType = EntityType.Account
            });

            // Then get the summary
            var request = new GetEntitySummaryRequest
            {
                GameServiceId = TestGameServiceId,
                EntityId = entityId,
                EntityType = EntityType.Account
            };

            var response = await analyticsClient.GetEntitySummaryAsync(request);

            return TestResult.Successful($"Summary retrieved for entity {entityId}");
        }, "Get entity summary");

    private static async Task<TestResult> TestQueryEntitySummaries(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var analyticsClient = GetServiceClient<IAnalyticsClient>();

            var request = new QueryEntitySummariesRequest
            {
                GameServiceId = TestGameServiceId,
                Limit = 10
            };

            var response = await analyticsClient.QueryEntitySummariesAsync(request);

            return TestResult.Successful("Summaries queried successfully");
        }, "Query entity summaries");

    private static async Task<TestResult> TestGetSkillRating(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var analyticsClient = GetServiceClient<IAnalyticsClient>();
            var entityId = Guid.NewGuid();

            var request = new GetSkillRatingRequest
            {
                GameServiceId = TestGameServiceId,
                EntityId = entityId,
                EntityType = EntityType.Account,
                RatingType = "pvp"
            };

            var response = await analyticsClient.GetSkillRatingAsync(request);

            return TestResult.Successful($"Skill rating retrieved: Rating={response.Rating}");
        }, "Get skill rating");

    private static async Task<TestResult> TestRecordControllerEvent(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var analyticsClient = GetServiceClient<IAnalyticsClient>();
            var accountId = Guid.NewGuid();
            var targetId = Guid.NewGuid();

            var request = new RecordControllerEventRequest
            {
                GameServiceId = TestGameServiceId,
                AccountId = accountId,
                TargetEntityId = targetId,
                TargetEntityType = EntityType.Character,
                Action = ControllerAction.Possess
            };

            // This returns void on success
            await analyticsClient.RecordControllerEventAsync(request);

            return TestResult.Successful($"Controller event recorded: Account={accountId}, Target={targetId}");
        }, "Record controller event");

    private static async Task<TestResult> TestQueryControllerHistory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var analyticsClient = GetServiceClient<IAnalyticsClient>();
            var targetId = Guid.NewGuid();

            var request = new QueryControllerHistoryRequest
            {
                GameServiceId = TestGameServiceId,
                TargetEntityId = targetId,
                Limit = 10
            };

            var response = await analyticsClient.QueryControllerHistoryAsync(request);

            return TestResult.Successful("Controller history queried successfully");
        }, "Query controller history");
}
