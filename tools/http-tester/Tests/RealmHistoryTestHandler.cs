using BeyondImmersion.BannouService.RealmHistory;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for RealmHistory service HTTP API endpoints.
/// Tests participation recording, lore management, deletion operations, and summarization.
/// These tests verify basic service operation; detailed validation is in unit tests.
/// </summary>
public class RealmHistoryTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Participation Tests
        new ServiceTest(TestRecordRealmParticipation, "RecordRealmParticipation", "RealmHistory", "Test recording realm participation in historical event"),
        new ServiceTest(TestGetRealmParticipation, "GetRealmParticipation", "RealmHistory", "Test retrieving realm participation records"),
        new ServiceTest(TestGetRealmEventParticipants, "GetRealmEventParticipants", "RealmHistory", "Test retrieving all realms that participated in an event"),

        // Lore Tests
        new ServiceTest(TestSetRealmLore, "SetRealmLore", "RealmHistory", "Test setting lore elements for a realm"),
        new ServiceTest(TestGetRealmLore, "GetRealmLore", "RealmHistory", "Test retrieving lore elements for a realm"),
        new ServiceTest(TestAddRealmLoreElement, "AddRealmLoreElement", "RealmHistory", "Test adding a single lore element"),

        // Delete Tests
        new ServiceTest(TestDeleteRealmParticipation, "DeleteRealmParticipation", "RealmHistory", "Test deleting a participation record"),
        new ServiceTest(TestDeleteRealmLore, "DeleteRealmLore", "RealmHistory", "Test deleting all lore for a realm"),
        new ServiceTest(TestDeleteAllRealmHistory, "DeleteAllRealmHistory", "RealmHistory", "Test deleting all history data for a realm"),

        // Summary Test
        new ServiceTest(TestSummarizeRealmHistory, "SummarizeRealmHistory", "RealmHistory", "Test generating realm history summaries"),

        // Negative Tests
        new ServiceTest(TestGetRealmLore_NotFound, "GetRealmLore_NotFound", "RealmHistory", "Test getting lore for realm with no lore returns 404"),
        new ServiceTest(TestDeleteRealmParticipation_NotFound, "DeleteRealmParticipation_NotFound", "RealmHistory", "Test deleting non-existent participation returns 404"),
    ];

    private static async Task<TestResult> TestRecordRealmParticipation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var realmId = Guid.NewGuid();
            var eventId = Guid.NewGuid();

            var request = new RecordRealmParticipationRequest
            {
                RealmId = realmId,
                EventId = eventId,
                EventName = "The Great War of the North",
                EventCategory = RealmEventCategory.War,
                Role = RealmEventRole.Defender,
                EventDate = DateTimeOffset.UtcNow.AddDays(-100),
                Impact = 0.8f
            };

            var response = await realmHistoryClient.RecordRealmParticipationAsync(request);

            if (response.RealmId != realmId)
                return TestResult.Failed($"Expected RealmId {realmId}, got {response.RealmId}");

            if (response.EventId != eventId)
                return TestResult.Failed($"Expected EventId {eventId}, got {response.EventId}");

            if (response.ParticipationId == Guid.Empty)
                return TestResult.Failed("ParticipationId should not be empty");

            return TestResult.Successful($"Participation recorded: ParticipationId={response.ParticipationId}");
        }, "Record realm participation");

    private static async Task<TestResult> TestGetRealmParticipation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var realmId = Guid.NewGuid();

            // First record a participation
            var recordRequest = new RecordRealmParticipationRequest
            {
                RealmId = realmId,
                EventId = Guid.NewGuid(),
                EventName = "Founding of the Eastern Kingdom",
                EventCategory = RealmEventCategory.Founding,
                Role = RealmEventRole.Origin,
                EventDate = DateTimeOffset.UtcNow.AddDays(-365),
                Impact = 0.9f
            };

            await realmHistoryClient.RecordRealmParticipationAsync(recordRequest);

            // Then retrieve it
            var getRequest = new GetRealmParticipationRequest
            {
                RealmId = realmId,
                Page = 1,
                PageSize = 20
            };

            var response = await realmHistoryClient.GetRealmParticipationAsync(getRequest);

            if (response.Participations.Count == 0)
                return TestResult.Failed("Expected at least one participation record");

            return TestResult.Successful($"Retrieved {response.Participations.Count} participation records, TotalCount={response.TotalCount}");
        }, "Get realm participation");

    private static async Task<TestResult> TestGetRealmEventParticipants(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var eventId = Guid.NewGuid();

            // Record participations for multiple realms in the same event
            var realm1 = Guid.NewGuid();
            var realm2 = Guid.NewGuid();

            await realmHistoryClient.RecordRealmParticipationAsync(new RecordRealmParticipationRequest
            {
                RealmId = realm1,
                EventId = eventId,
                EventName = "The Treaty of Nations",
                EventCategory = RealmEventCategory.Treaty,
                Role = RealmEventRole.Mediator,
                EventDate = DateTimeOffset.UtcNow.AddDays(-50),
                Impact = 0.7f
            });

            await realmHistoryClient.RecordRealmParticipationAsync(new RecordRealmParticipationRequest
            {
                RealmId = realm2,
                EventId = eventId,
                EventName = "The Treaty of Nations",
                EventCategory = RealmEventCategory.Treaty,
                Role = RealmEventRole.Beneficiary,
                EventDate = DateTimeOffset.UtcNow.AddDays(-50),
                Impact = 0.6f
            });

            // Get all participants for this event
            var request = new GetRealmEventParticipantsRequest
            {
                EventId = eventId,
                Page = 1,
                PageSize = 20
            };

            var response = await realmHistoryClient.GetRealmEventParticipantsAsync(request);

            if (response.Participations.Count < 2)
                return TestResult.Failed($"Expected at least 2 participants, got {response.Participations.Count}");

            return TestResult.Successful($"Retrieved {response.Participations.Count} participants for event");
        }, "Get realm event participants");

    private static async Task<TestResult> TestSetRealmLore(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var realmId = Guid.NewGuid();

            var request = new SetRealmLoreRequest
            {
                RealmId = realmId,
                Elements = new List<RealmLoreElement>
                {
                    new RealmLoreElement
                    {
                        ElementType = RealmLoreElementType.OriginMyth,
                        Key = "creation_story",
                        Value = "Born from the ashes of the old world",
                        Strength = 0.9f
                    },
                    new RealmLoreElement
                    {
                        ElementType = RealmLoreElementType.PoliticalSystem,
                        Key = "government",
                        Value = "Feudal monarchy with elected council",
                        Strength = 0.8f
                    }
                },
                ReplaceExisting = false
            };

            var response = await realmHistoryClient.SetRealmLoreAsync(request);

            if (response.Elements.Count != 2)
                return TestResult.Failed($"Expected 2 elements, got {response.Elements.Count}");

            return TestResult.Successful($"Lore set with {response.Elements.Count} elements for realm {realmId}");
        }, "Set realm lore");

    private static async Task<TestResult> TestGetRealmLore(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var realmId = Guid.NewGuid();

            // First set some lore
            await realmHistoryClient.SetRealmLoreAsync(new SetRealmLoreRequest
            {
                RealmId = realmId,
                Elements = new List<RealmLoreElement>
                {
                    new RealmLoreElement
                    {
                        ElementType = RealmLoreElementType.EconomicBase,
                        Key = "primary_trade",
                        Value = "Mining and metalwork",
                        Strength = 0.85f
                    }
                },
                ReplaceExisting = false
            });

            // Then retrieve it
            var request = new GetRealmLoreRequest
            {
                RealmId = realmId
            };

            var response = await realmHistoryClient.GetRealmLoreAsync(request);

            if (response.Elements.Count == 0)
                return TestResult.Failed("Expected at least one lore element");

            return TestResult.Successful($"Retrieved {response.Elements.Count} lore elements for realm {realmId}");
        }, "Get realm lore");

    private static async Task<TestResult> TestAddRealmLoreElement(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var realmId = Guid.NewGuid();

            // First set initial lore
            await realmHistoryClient.SetRealmLoreAsync(new SetRealmLoreRequest
            {
                RealmId = realmId,
                Elements = new List<RealmLoreElement>
                {
                    new RealmLoreElement
                    {
                        ElementType = RealmLoreElementType.CulturalPractice,
                        Key = "festival",
                        Value = "Annual harvest celebration",
                        Strength = 0.7f
                    }
                },
                ReplaceExisting = false
            });

            // Then add another element
            var request = new AddRealmLoreElementRequest
            {
                RealmId = realmId,
                Element = new RealmLoreElement
                {
                    ElementType = RealmLoreElementType.ReligiousTradition,
                    Key = "deity",
                    Value = "Sun worship",
                    Strength = 0.8f
                }
            };

            var response = await realmHistoryClient.AddRealmLoreElementAsync(request);

            if (response.Elements.Count < 2)
                return TestResult.Failed($"Expected at least 2 elements after adding, got {response.Elements.Count}");

            return TestResult.Successful($"Added element, now have {response.Elements.Count} elements");
        }, "Add realm lore element");

    private static async Task<TestResult> TestDeleteRealmParticipation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var realmId = Guid.NewGuid();
            var eventId = Guid.NewGuid();

            // First record a participation
            var recordResponse = await realmHistoryClient.RecordRealmParticipationAsync(new RecordRealmParticipationRequest
            {
                RealmId = realmId,
                EventId = eventId,
                EventName = "Test Event to Delete",
                EventCategory = RealmEventCategory.Discovery,
                Role = RealmEventRole.Instigator,
                EventDate = DateTimeOffset.UtcNow.AddDays(-10),
                Impact = 0.5f
            });

            // Then delete it
            var deleteRequest = new DeleteRealmParticipationRequest
            {
                ParticipationId = recordResponse.ParticipationId
            };

            await realmHistoryClient.DeleteRealmParticipationAsync(deleteRequest);

            return TestResult.Successful($"Deleted participation {recordResponse.ParticipationId}");
        }, "Delete realm participation");

    private static async Task<TestResult> TestDeleteRealmLore(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var realmId = Guid.NewGuid();

            // First set some lore
            await realmHistoryClient.SetRealmLoreAsync(new SetRealmLoreRequest
            {
                RealmId = realmId,
                Elements = new List<RealmLoreElement>
                {
                    new RealmLoreElement
                    {
                        ElementType = RealmLoreElementType.GeographicFeature,
                        Key = "terrain",
                        Value = "Mountain range",
                        Strength = 0.9f
                    }
                },
                ReplaceExisting = false
            });

            // Then delete it
            var deleteRequest = new DeleteRealmLoreRequest
            {
                RealmId = realmId
            };

            await realmHistoryClient.DeleteRealmLoreAsync(deleteRequest);

            return TestResult.Successful($"Deleted lore for realm {realmId}");
        }, "Delete realm lore");

    private static async Task<TestResult> TestDeleteAllRealmHistory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var realmId = Guid.NewGuid();

            // First create some history data
            await realmHistoryClient.RecordRealmParticipationAsync(new RecordRealmParticipationRequest
            {
                RealmId = realmId,
                EventId = Guid.NewGuid(),
                EventName = "Test Event for Deletion",
                EventCategory = RealmEventCategory.Migration,
                Role = RealmEventRole.Affected,
                EventDate = DateTimeOffset.UtcNow.AddDays(-30),
                Impact = 0.6f
            });

            await realmHistoryClient.SetRealmLoreAsync(new SetRealmLoreRequest
            {
                RealmId = realmId,
                Elements = new List<RealmLoreElement>
                {
                    new RealmLoreElement
                    {
                        ElementType = RealmLoreElementType.FamousFigure,
                        Key = "hero",
                        Value = "Legendary founder",
                        Strength = 0.85f
                    }
                },
                ReplaceExisting = false
            });

            // Then delete all
            var deleteRequest = new DeleteAllRealmHistoryRequest
            {
                RealmId = realmId
            };

            var response = await realmHistoryClient.DeleteAllRealmHistoryAsync(deleteRequest);

            return TestResult.Successful($"Deleted all history: Participations={response.ParticipationsDeleted}, LoreDeleted={response.LoreDeleted}");
        }, "Delete all realm history");

    private static async Task<TestResult> TestSummarizeRealmHistory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();
            var realmId = Guid.NewGuid();

            // Create some history data first
            await realmHistoryClient.RecordRealmParticipationAsync(new RecordRealmParticipationRequest
            {
                RealmId = realmId,
                EventId = Guid.NewGuid(),
                EventName = "The Foundation",
                EventCategory = RealmEventCategory.Founding,
                Role = RealmEventRole.Origin,
                EventDate = DateTimeOffset.UtcNow.AddDays(-1000),
                Impact = 1.0f
            });

            await realmHistoryClient.SetRealmLoreAsync(new SetRealmLoreRequest
            {
                RealmId = realmId,
                Elements = new List<RealmLoreElement>
                {
                    new RealmLoreElement
                    {
                        ElementType = RealmLoreElementType.TechnologicalLevel,
                        Key = "tech_era",
                        Value = "Iron Age",
                        Strength = 0.8f
                    }
                },
                ReplaceExisting = false
            });

            // Generate summaries
            var request = new SummarizeRealmHistoryRequest
            {
                RealmId = realmId
            };

            var response = await realmHistoryClient.SummarizeRealmHistoryAsync(request);

            return TestResult.Successful($"Summary generated: KeyLorePoints count={response.KeyLorePoints.Count}, MajorHistoricalEvents count={response.MajorHistoricalEvents.Count}");
        }, "Summarize realm history");

    private static async Task<TestResult> TestGetRealmLore_NotFound(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();

            // Try to get lore for a realm that has none
            var request = new GetRealmLoreRequest
            {
                RealmId = Guid.NewGuid()
            };

            await realmHistoryClient.GetRealmLoreAsync(request);
        }, 404, "Get realm lore for non-existent realm");

    private static async Task<TestResult> TestDeleteRealmParticipation_NotFound(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(async () =>
        {
            var realmHistoryClient = GetServiceClient<IRealmHistoryClient>();

            // Try to delete non-existent participation
            var request = new DeleteRealmParticipationRequest
            {
                ParticipationId = Guid.NewGuid()
            };

            await realmHistoryClient.DeleteRealmParticipationAsync(request);
        }, 404, "Delete non-existent participation");
}
