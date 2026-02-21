using BeyondImmersion.BannouService.CharacterEncounter;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Character Encounter service HTTP API endpoints.
/// Tests encounter type management, encounter recording, and memory/sentiment queries.
/// These tests verify basic service operation; detailed validation is in unit tests.
/// </summary>
public class CharacterEncounterTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Encounter Type CRUD Tests
        new ServiceTest(TestCreateEncounterType, "CreateEncounterType", "CharacterEncounter", "Test encounter type creation"),
        new ServiceTest(TestGetEncounterType, "GetEncounterType", "CharacterEncounter", "Test encounter type retrieval"),
        new ServiceTest(TestListEncounterTypes, "ListEncounterTypes", "CharacterEncounter", "Test listing encounter types"),
        new ServiceTest(TestUpdateEncounterType, "UpdateEncounterType", "CharacterEncounter", "Test encounter type update"),
        new ServiceTest(TestDeleteEncounterType, "DeleteEncounterType", "CharacterEncounter", "Test encounter type deletion"),
        new ServiceTest(TestSeedEncounterTypes, "SeedEncounterTypes", "CharacterEncounter", "Test seeding encounter types"),

        // Encounter Recording Tests
        new ServiceTest(TestRecordEncounter, "RecordEncounter", "CharacterEncounter", "Test recording an encounter"),

        // Query Tests
        new ServiceTest(TestQueryByCharacter, "QueryByCharacter", "CharacterEncounter", "Test querying encounters by character"),
        new ServiceTest(TestQueryBetween, "QueryBetween", "CharacterEncounter", "Test querying encounters between characters"),
        new ServiceTest(TestQueryByLocation, "QueryByLocation", "CharacterEncounter", "Test querying encounters by location"),
        new ServiceTest(TestHasMet, "HasMet", "CharacterEncounter", "Test checking if characters have met"),

        // Sentiment Tests
        new ServiceTest(TestGetSentiment, "GetSentiment", "CharacterEncounter", "Test getting sentiment between characters"),
        new ServiceTest(TestBatchGetSentiment, "BatchGetSentiment", "CharacterEncounter", "Test batch sentiment retrieval"),

        // Perspective Tests
        new ServiceTest(TestGetPerspective, "GetPerspective", "CharacterEncounter", "Test getting encounter perspective"),
        new ServiceTest(TestUpdatePerspective, "UpdatePerspective", "CharacterEncounter", "Test updating encounter perspective"),
        new ServiceTest(TestRefreshMemory, "RefreshMemory", "CharacterEncounter", "Test refreshing memory strength"),

        // Cleanup Tests
        new ServiceTest(TestDeleteEncounter, "DeleteEncounter", "CharacterEncounter", "Test deleting an encounter"),
        new ServiceTest(TestDeleteByCharacter, "DeleteByCharacter", "CharacterEncounter", "Test deleting all character encounters"),
        new ServiceTest(TestDecayMemories, "DecayMemories", "CharacterEncounter", "Test memory decay processing"),
    ];

    private static async Task<TestResult> TestCreateEncounterType(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var typeCode = $"TEST_TYPE_{DateTime.Now.Ticks}";

            var response = await encounterClient.CreateEncounterTypeAsync(new CreateEncounterTypeRequest
            {
                Code = typeCode,
                Name = "Test Encounter Type",
                Description = "A test encounter type for HTTP tests"
            });

            if (response.Code != typeCode)
                return TestResult.Failed($"Code mismatch: expected {typeCode}, got {response.Code}");

            return TestResult.Successful($"Encounter type created: code={response.Code}");
        }, "Create encounter type");

    private static async Task<TestResult> TestGetEncounterType(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();

            // Get a built-in type
            var response = await encounterClient.GetEncounterTypeAsync(new GetEncounterTypeRequest
            {
                Code = "COMBAT"
            });

            if (response.Code != "COMBAT")
                return TestResult.Failed("Failed to retrieve COMBAT type");

            return TestResult.Successful($"Encounter type retrieved: code={response.Code}");
        }, "Get encounter type");

    private static async Task<TestResult> TestListEncounterTypes(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();

            var response = await encounterClient.ListEncounterTypesAsync(new ListEncounterTypesRequest());

            if (response.Types == null || response.Types.Count == 0)
                return TestResult.Failed("No encounter types found");

            return TestResult.Successful($"Listed {response.Types.Count} encounter types");
        }, "List encounter types");

    private static async Task<TestResult> TestUpdateEncounterType(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var typeCode = $"TEST_UPDATE_{DateTime.Now.Ticks}";

            // Create first
            await encounterClient.CreateEncounterTypeAsync(new CreateEncounterTypeRequest
            {
                Code = typeCode,
                Name = "Original Name"
            });

            // Update
            var response = await encounterClient.UpdateEncounterTypeAsync(new UpdateEncounterTypeRequest
            {
                Code = typeCode,
                Name = "Updated Name",
                Description = "Updated description"
            });

            if (response.Name != "Updated Name")
                return TestResult.Failed($"Name not updated: {response.Name}");

            return TestResult.Successful($"Encounter type updated: code={response.Code}");
        }, "Update encounter type");

    private static async Task<TestResult> TestDeleteEncounterType(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var typeCode = $"TEST_DELETE_{DateTime.Now.Ticks}";

            // Create first
            await encounterClient.CreateEncounterTypeAsync(new CreateEncounterTypeRequest
            {
                Code = typeCode,
                Name = "To Be Deleted"
            });

            // Delete
            await encounterClient.DeleteEncounterTypeAsync(new DeleteEncounterTypeRequest
            {
                Code = typeCode
            });

            // Verify deletion
            try
            {
                await encounterClient.GetEncounterTypeAsync(new GetEncounterTypeRequest
                {
                    Code = typeCode
                });
                return TestResult.Failed("Encounter type still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful($"Encounter type deleted: code={typeCode}");
            }
        }, "Delete encounter type");

    private static async Task<TestResult> TestSeedEncounterTypes(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();

            // SeedEncounterTypesRequest only has ForceReset - seeds built-in types
            var response = await encounterClient.SeedEncounterTypesAsync(new SeedEncounterTypesRequest
            {
                ForceReset = false
            });

            return TestResult.Successful($"Seeded encounter types: created={response.Created}, updated={response.Updated}");
        }, "Seed encounter types");

    private static async Task<TestResult> TestRecordEncounter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterA = Guid.NewGuid();
            var characterB = Guid.NewGuid();
            var realmId = Guid.NewGuid();

            var response = await encounterClient.RecordEncounterAsync(new RecordEncounterRequest
            {
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = realmId,
                EncounterTypeCode = "FRIENDLY",
                Outcome = EncounterOutcome.POSITIVE,
                ParticipantIds = new List<Guid> { characterA, characterB }
            });

            if (response.Encounter.EncounterId == Guid.Empty)
                return TestResult.Failed("Encounter ID is empty");

            return TestResult.Successful($"Encounter recorded: id={response.Encounter.EncounterId}");
        }, "Record encounter");

    private static async Task<TestResult> TestQueryByCharacter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterId = Guid.NewGuid();

            var response = await encounterClient.QueryByCharacterAsync(new QueryByCharacterRequest
            {
                CharacterId = characterId
            });

            // New character should have no encounters
            return TestResult.Successful($"Query returned {response.Encounters.Count} encounters");
        }, "Query by character");

    private static async Task<TestResult> TestQueryBetween(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterA = Guid.NewGuid();
            var characterB = Guid.NewGuid();

            var response = await encounterClient.QueryBetweenAsync(new QueryBetweenRequest
            {
                CharacterIdA = characterA,
                CharacterIdB = characterB
            });

            return TestResult.Successful($"Query returned {response.Encounters.Count} encounters between characters");
        }, "Query between characters");

    private static async Task<TestResult> TestQueryByLocation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var locationId = Guid.NewGuid();

            var response = await encounterClient.QueryByLocationAsync(new QueryByLocationRequest
            {
                LocationId = locationId
            });

            return TestResult.Successful($"Query returned {response.Encounters.Count} encounters at location");
        }, "Query by location");

    private static async Task<TestResult> TestHasMet(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterA = Guid.NewGuid();
            var characterB = Guid.NewGuid();

            var response = await encounterClient.HasMetAsync(new HasMetRequest
            {
                CharacterIdA = characterA,
                CharacterIdB = characterB
            });

            // New characters should not have met
            if (response.HasMet)
                return TestResult.Failed("New characters should not have met");

            return TestResult.Successful($"HasMet check: {response.HasMet}");
        }, "Has met check");

    private static async Task<TestResult> TestGetSentiment(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterA = Guid.NewGuid();
            var characterB = Guid.NewGuid();

            var response = await encounterClient.GetSentimentAsync(new GetSentimentRequest
            {
                CharacterId = characterA,
                TargetCharacterId = characterB
            });

            return TestResult.Successful($"Sentiment: {response.Sentiment}, encounters={response.EncounterCount}");
        }, "Get sentiment");

    private static async Task<TestResult> TestBatchGetSentiment(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterA = Guid.NewGuid();
            var targetB = Guid.NewGuid();
            var targetC = Guid.NewGuid();

            var response = await encounterClient.BatchGetSentimentAsync(new BatchGetSentimentRequest
            {
                CharacterId = characterA,
                TargetCharacterIds = new List<Guid> { targetB, targetC }
            });

            return TestResult.Successful($"Batch sentiment: {response.Sentiments.Count} results");
        }, "Batch get sentiment");

    private static async Task<TestResult> TestGetPerspective(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterA = Guid.NewGuid();
            var characterB = Guid.NewGuid();
            var realmId = Guid.NewGuid();

            // First record an encounter
            var encounter = await encounterClient.RecordEncounterAsync(new RecordEncounterRequest
            {
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = realmId,
                EncounterTypeCode = "FRIENDLY",
                Outcome = EncounterOutcome.POSITIVE,
                ParticipantIds = new List<Guid> { characterA, characterB }
            });

            // Then get perspective
            var response = await encounterClient.GetPerspectiveAsync(new GetPerspectiveRequest
            {
                EncounterId = encounter.Encounter.EncounterId,
                CharacterId = characterA
            });

            return TestResult.Successful($"Perspective retrieved: memoryStrength={response.Perspective.MemoryStrength}");
        }, "Get perspective");

    private static async Task<TestResult> TestUpdatePerspective(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterA = Guid.NewGuid();
            var characterB = Guid.NewGuid();
            var realmId = Guid.NewGuid();

            // First record an encounter
            var encounter = await encounterClient.RecordEncounterAsync(new RecordEncounterRequest
            {
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = realmId,
                EncounterTypeCode = "FRIENDLY",
                Outcome = EncounterOutcome.POSITIVE,
                ParticipantIds = new List<Guid> { characterA, characterB }
            });

            // Update perspective
            var response = await encounterClient.UpdatePerspectiveAsync(new UpdatePerspectiveRequest
            {
                EncounterId = encounter.Encounter.EncounterId,
                CharacterId = characterA,
                RememberedAs = "Updated memory from test",
                SentimentShift = 0.5f
            });

            return TestResult.Successful("Perspective updated");
        }, "Update perspective");

    private static async Task<TestResult> TestRefreshMemory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterA = Guid.NewGuid();
            var characterB = Guid.NewGuid();
            var realmId = Guid.NewGuid();

            // First record an encounter
            var encounter = await encounterClient.RecordEncounterAsync(new RecordEncounterRequest
            {
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = realmId,
                EncounterTypeCode = "FRIENDLY",
                Outcome = EncounterOutcome.POSITIVE,
                ParticipantIds = new List<Guid> { characterA, characterB }
            });

            // Refresh memory
            var response = await encounterClient.RefreshMemoryAsync(new RefreshMemoryRequest
            {
                EncounterId = encounter.Encounter.EncounterId,
                CharacterId = characterA,
                StrengthBoost = 0.1f
            });

            return TestResult.Successful($"Memory refreshed: newStrength={response.Perspective.MemoryStrength}");
        }, "Refresh memory");

    private static async Task<TestResult> TestDeleteEncounter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterA = Guid.NewGuid();
            var characterB = Guid.NewGuid();
            var realmId = Guid.NewGuid();

            // First record an encounter
            var encounter = await encounterClient.RecordEncounterAsync(new RecordEncounterRequest
            {
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = realmId,
                EncounterTypeCode = "FRIENDLY",
                Outcome = EncounterOutcome.POSITIVE,
                ParticipantIds = new List<Guid> { characterA, characterB }
            });

            // Delete it
            var response = await encounterClient.DeleteEncounterAsync(new DeleteEncounterRequest
            {
                EncounterId = encounter.Encounter.EncounterId
            });

            if (response.EncounterId != encounter.Encounter.EncounterId)
                return TestResult.Failed("Encounter ID mismatch in delete response");

            return TestResult.Successful($"Encounter deleted: perspectivesDeleted={response.PerspectivesDeleted}");
        }, "Delete encounter");

    private static async Task<TestResult> TestDeleteByCharacter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterId = Guid.NewGuid();

            var response = await encounterClient.DeleteByCharacterAsync(new DeleteByCharacterRequest
            {
                CharacterId = characterId
            });

            return TestResult.Successful($"Deleted by character: encounters={response.EncountersDeleted}, perspectives={response.PerspectivesDeleted}");
        }, "Delete by character");

    private static async Task<TestResult> TestDecayMemories(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var encounterClient = GetServiceClient<ICharacterEncounterClient>();
            var characterId = Guid.NewGuid();

            var response = await encounterClient.DecayMemoriesAsync(new DecayMemoriesRequest
            {
                CharacterId = characterId,
                DryRun = true
            });

            return TestResult.Successful($"Memory decay (dry run): processed={response.PerspectivesProcessed}, faded={response.MemoriesFaded}");
        }, "Decay memories");
}
