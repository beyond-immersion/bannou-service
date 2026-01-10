using BeyondImmersion.BannouService.CharacterHistory;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for CharacterHistory service HTTP API endpoints.
/// Tests participation recording, backstory management, deletion operations, and summarization.
/// These tests verify basic service operation; detailed validation is in unit tests.
/// </summary>
public class CharacterHistoryTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Participation Tests
        new ServiceTest(TestRecordParticipation, "RecordParticipation", "CharacterHistory", "Test recording character participation in historical event"),
        new ServiceTest(TestGetParticipation, "GetParticipation", "CharacterHistory", "Test retrieving character participation records"),
        new ServiceTest(TestGetEventParticipants, "GetEventParticipants", "CharacterHistory", "Test retrieving all characters that participated in an event"),

        // Backstory Tests
        new ServiceTest(TestSetBackstory, "SetBackstory", "CharacterHistory", "Test setting backstory elements for a character"),
        new ServiceTest(TestGetBackstory, "GetBackstory", "CharacterHistory", "Test retrieving backstory elements for a character"),
        new ServiceTest(TestAddBackstoryElement, "AddBackstoryElement", "CharacterHistory", "Test adding a single backstory element"),

        // Delete Tests
        new ServiceTest(TestDeleteParticipation, "DeleteParticipation", "CharacterHistory", "Test deleting a participation record"),
        new ServiceTest(TestDeleteBackstory, "DeleteBackstory", "CharacterHistory", "Test deleting all backstory for a character"),
        new ServiceTest(TestDeleteAllHistory, "DeleteAllHistory", "CharacterHistory", "Test deleting all history data for a character"),

        // Summary Test
        new ServiceTest(TestSummarizeHistory, "SummarizeHistory", "CharacterHistory", "Test generating character history summaries"),

        // Negative Tests
        new ServiceTest(TestGetBackstory_NotFound, "GetBackstory_NotFound", "CharacterHistory", "Test getting backstory for character with no backstory returns 404"),
        new ServiceTest(TestDeleteParticipation_NotFound, "DeleteParticipation_NotFound", "CharacterHistory", "Test deleting non-existent participation returns 404"),
    ];

    private static async Task<TestResult> TestRecordParticipation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var characterId = Guid.NewGuid();
            var eventId = Guid.NewGuid();

            var request = new RecordParticipationRequest
            {
                CharacterId = characterId,
                EventId = eventId,
                EventName = "The Great Battle of Dawn",
                EventCategory = EventCategory.WAR,
                Role = ParticipationRole.COMBATANT,
                EventDate = DateTimeOffset.UtcNow.AddDays(-100),
                Significance = 0.8f
            };

            var response = await characterHistoryClient.RecordParticipationAsync(request);

            if (response.CharacterId != characterId)
                return TestResult.Failed($"Expected CharacterId {characterId}, got {response.CharacterId}");

            if (response.EventId != eventId)
                return TestResult.Failed($"Expected EventId {eventId}, got {response.EventId}");

            if (response.ParticipationId == Guid.Empty)
                return TestResult.Failed("ParticipationId should not be empty");

            return TestResult.Successful($"Participation recorded: ParticipationId={response.ParticipationId}");
        }, "Record participation");

    private static async Task<TestResult> TestGetParticipation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var characterId = Guid.NewGuid();

            // First record a participation
            var recordRequest = new RecordParticipationRequest
            {
                CharacterId = characterId,
                EventId = Guid.NewGuid(),
                EventName = "Birth of a Hero",
                EventCategory = EventCategory.PERSONAL,
                Role = ParticipationRole.HERO,
                EventDate = DateTimeOffset.UtcNow.AddDays(-365),
                Significance = 0.9f
            };

            await characterHistoryClient.RecordParticipationAsync(recordRequest);

            // Then retrieve it
            var getRequest = new GetParticipationRequest
            {
                CharacterId = characterId,
                Page = 1,
                PageSize = 20
            };

            var response = await characterHistoryClient.GetParticipationAsync(getRequest);

            if (response.Participations.Count == 0)
                return TestResult.Failed("Expected at least one participation record");

            return TestResult.Successful($"Retrieved {response.Participations.Count} participation records, TotalCount={response.TotalCount}");
        }, "Get participation");

    private static async Task<TestResult> TestGetEventParticipants(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var eventId = Guid.NewGuid();

            // Record participations for multiple characters in the same event
            var character1 = Guid.NewGuid();
            var character2 = Guid.NewGuid();

            await characterHistoryClient.RecordParticipationAsync(new RecordParticipationRequest
            {
                CharacterId = character1,
                EventId = eventId,
                EventName = "The Coronation Ceremony",
                EventCategory = EventCategory.POLITICAL,
                Role = ParticipationRole.LEADER,
                EventDate = DateTimeOffset.UtcNow.AddDays(-50),
                Significance = 1.0f
            });

            await characterHistoryClient.RecordParticipationAsync(new RecordParticipationRequest
            {
                CharacterId = character2,
                EventId = eventId,
                EventName = "The Coronation Ceremony",
                EventCategory = EventCategory.POLITICAL,
                Role = ParticipationRole.WITNESS,
                EventDate = DateTimeOffset.UtcNow.AddDays(-50),
                Significance = 0.5f
            });

            // Get all participants for this event
            var request = new GetEventParticipantsRequest
            {
                EventId = eventId,
                Page = 1,
                PageSize = 20
            };

            var response = await characterHistoryClient.GetEventParticipantsAsync(request);

            if (response.Participations.Count < 2)
                return TestResult.Failed($"Expected at least 2 participants, got {response.Participations.Count}");

            return TestResult.Successful($"Retrieved {response.Participations.Count} participants for event");
        }, "Get event participants");

    private static async Task<TestResult> TestSetBackstory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var characterId = Guid.NewGuid();

            var request = new SetBackstoryRequest
            {
                CharacterId = characterId,
                Elements = new List<BackstoryElement>
                {
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.ORIGIN,
                        Key = "birthplace",
                        Value = "Born in the northern mountains",
                        Strength = 0.9f
                    },
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.OCCUPATION,
                        Key = "profession",
                        Value = "Blacksmith apprentice",
                        Strength = 0.7f
                    }
                },
                ReplaceExisting = false
            };

            var response = await characterHistoryClient.SetBackstoryAsync(request);

            if (response.Elements.Count != 2)
                return TestResult.Failed($"Expected 2 elements, got {response.Elements.Count}");

            return TestResult.Successful($"Backstory set with {response.Elements.Count} elements for character {characterId}");
        }, "Set backstory");

    private static async Task<TestResult> TestGetBackstory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var characterId = Guid.NewGuid();

            // First set some backstory
            await characterHistoryClient.SetBackstoryAsync(new SetBackstoryRequest
            {
                CharacterId = characterId,
                Elements = new List<BackstoryElement>
                {
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.TRAINING,
                        Key = "skill",
                        Value = "Sword fighting",
                        Strength = 0.85f
                    }
                },
                ReplaceExisting = false
            });

            // Then retrieve it
            var request = new GetBackstoryRequest
            {
                CharacterId = characterId
            };

            var response = await characterHistoryClient.GetBackstoryAsync(request);

            if (response.Elements.Count == 0)
                return TestResult.Failed("Expected at least one backstory element");

            return TestResult.Successful($"Retrieved {response.Elements.Count} backstory elements for character {characterId}");
        }, "Get backstory");

    private static async Task<TestResult> TestAddBackstoryElement(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var characterId = Guid.NewGuid();

            // First set initial backstory
            await characterHistoryClient.SetBackstoryAsync(new SetBackstoryRequest
            {
                CharacterId = characterId,
                Elements = new List<BackstoryElement>
                {
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.TRAUMA,
                        Key = "loss",
                        Value = "Lost family in fire",
                        Strength = 0.9f
                    }
                },
                ReplaceExisting = false
            });

            // Then add another element
            var request = new AddBackstoryElementRequest
            {
                CharacterId = characterId,
                Element = new BackstoryElement
                {
                    ElementType = BackstoryElementType.GOAL,
                    Key = "ambition",
                    Value = "Seek revenge",
                    Strength = 0.95f
                }
            };

            var response = await characterHistoryClient.AddBackstoryElementAsync(request);

            if (response.Elements.Count < 2)
                return TestResult.Failed($"Expected at least 2 elements after adding, got {response.Elements.Count}");

            return TestResult.Successful($"Added element, now have {response.Elements.Count} elements");
        }, "Add backstory element");

    private static async Task<TestResult> TestDeleteParticipation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var characterId = Guid.NewGuid();
            var eventId = Guid.NewGuid();

            // First record a participation
            var recordResponse = await characterHistoryClient.RecordParticipationAsync(new RecordParticipationRequest
            {
                CharacterId = characterId,
                EventId = eventId,
                EventName = "Test Event to Delete",
                EventCategory = EventCategory.CULTURAL,
                Role = ParticipationRole.WITNESS,
                EventDate = DateTimeOffset.UtcNow.AddDays(-10),
                Significance = 0.5f
            });

            // Then delete it
            var deleteRequest = new DeleteParticipationRequest
            {
                ParticipationId = recordResponse.ParticipationId
            };

            await characterHistoryClient.DeleteParticipationAsync(deleteRequest);

            return TestResult.Successful($"Deleted participation {recordResponse.ParticipationId}");
        }, "Delete participation");

    private static async Task<TestResult> TestDeleteBackstory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var characterId = Guid.NewGuid();

            // First set some backstory
            await characterHistoryClient.SetBackstoryAsync(new SetBackstoryRequest
            {
                CharacterId = characterId,
                Elements = new List<BackstoryElement>
                {
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.SECRET,
                        Key = "hidden_identity",
                        Value = "Royal heir in hiding",
                        Strength = 1.0f
                    }
                },
                ReplaceExisting = false
            });

            // Then delete it
            var deleteRequest = new DeleteBackstoryRequest
            {
                CharacterId = characterId
            };

            await characterHistoryClient.DeleteBackstoryAsync(deleteRequest);

            return TestResult.Successful($"Deleted backstory for character {characterId}");
        }, "Delete backstory");

    private static async Task<TestResult> TestDeleteAllHistory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var characterId = Guid.NewGuid();

            // First create some history data
            await characterHistoryClient.RecordParticipationAsync(new RecordParticipationRequest
            {
                CharacterId = characterId,
                EventId = Guid.NewGuid(),
                EventName = "Test Event for Deletion",
                EventCategory = EventCategory.ECONOMIC,
                Role = ParticipationRole.BENEFICIARY,
                EventDate = DateTimeOffset.UtcNow.AddDays(-30),
                Significance = 0.6f
            });

            await characterHistoryClient.SetBackstoryAsync(new SetBackstoryRequest
            {
                CharacterId = characterId,
                Elements = new List<BackstoryElement>
                {
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.ACHIEVEMENT,
                        Key = "medal",
                        Value = "Decorated war hero",
                        Strength = 0.85f
                    }
                },
                ReplaceExisting = false
            });

            // Then delete all
            var deleteRequest = new DeleteAllHistoryRequest
            {
                CharacterId = characterId
            };

            var response = await characterHistoryClient.DeleteAllHistoryAsync(deleteRequest);

            return TestResult.Successful($"Deleted all history: Participations={response.ParticipationsDeleted}, BackstoryDeleted={response.BackstoryDeleted}");
        }, "Delete all history");

    private static async Task<TestResult> TestSummarizeHistory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();
            var characterId = Guid.NewGuid();

            // Create some history data first
            await characterHistoryClient.RecordParticipationAsync(new RecordParticipationRequest
            {
                CharacterId = characterId,
                EventId = Guid.NewGuid(),
                EventName = "The First Victory",
                EventCategory = EventCategory.WAR,
                Role = ParticipationRole.HERO,
                EventDate = DateTimeOffset.UtcNow.AddDays(-1000),
                Significance = 1.0f
            });

            await characterHistoryClient.SetBackstoryAsync(new SetBackstoryRequest
            {
                CharacterId = characterId,
                Elements = new List<BackstoryElement>
                {
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.BELIEF,
                        Key = "creed",
                        Value = "Honor above all",
                        Strength = 0.9f
                    }
                },
                ReplaceExisting = false
            });

            // Generate summaries
            var request = new SummarizeHistoryRequest
            {
                CharacterId = characterId
            };

            var response = await characterHistoryClient.SummarizeHistoryAsync(request);

            return TestResult.Successful($"Summary generated: KeyBackstoryPoints count={response.KeyBackstoryPoints.Count}, MajorLifeEvents count={response.MajorLifeEvents.Count}");
        }, "Summarize history");

    private static async Task<TestResult> TestGetBackstory_NotFound(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();

            // Try to get backstory for a character that has none
            var request = new GetBackstoryRequest
            {
                CharacterId = Guid.NewGuid()
            };

            await characterHistoryClient.GetBackstoryAsync(request);
        }, 404, "Get backstory for non-existent character");

    private static async Task<TestResult> TestDeleteParticipation_NotFound(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(async () =>
        {
            var characterHistoryClient = GetServiceClient<ICharacterHistoryClient>();

            // Try to delete non-existent participation
            var request = new DeleteParticipationRequest
            {
                ParticipationId = Guid.NewGuid()
            };

            await characterHistoryClient.DeleteParticipationAsync(request);
        }, 404, "Delete non-existent participation");
}
