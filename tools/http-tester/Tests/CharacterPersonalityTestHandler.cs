using BeyondImmersion.BannouService.CharacterPersonality;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Character Personality service HTTP API endpoints.
/// Tests personality trait management, experience recording, and combat preferences.
/// These tests verify basic service operation; detailed validation is in unit tests.
/// </summary>
public class CharacterPersonalityTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Personality Tests
        new ServiceTest(TestSetPersonality, "SetPersonality", "CharacterPersonality", "Test setting character personality"),
        new ServiceTest(TestGetPersonality, "GetPersonality", "CharacterPersonality", "Test getting character personality"),
        new ServiceTest(TestRecordExperience, "RecordExperience", "CharacterPersonality", "Test recording experience that may evolve traits"),
        new ServiceTest(TestBatchGetPersonalities, "BatchGetPersonalities", "CharacterPersonality", "Test batch personality retrieval"),
        new ServiceTest(TestDeletePersonality, "DeletePersonality", "CharacterPersonality", "Test deleting character personality"),

        // Combat Preferences Tests
        new ServiceTest(TestSetCombatPreferences, "SetCombatPreferences", "CharacterPersonality", "Test setting combat preferences"),
        new ServiceTest(TestGetCombatPreferences, "GetCombatPreferences", "CharacterPersonality", "Test getting combat preferences"),
        new ServiceTest(TestEvolveCombatPreferences, "EvolveCombatPreferences", "CharacterPersonality", "Test evolving combat preferences via experience"),
        new ServiceTest(TestDeleteCombatPreferences, "DeleteCombatPreferences", "CharacterPersonality", "Test deleting combat preferences"),
    ];

    private static async Task<TestResult> TestSetPersonality(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var personalityClient = GetServiceClient<ICharacterPersonalityClient>();
            var characterId = Guid.NewGuid();

            var response = await personalityClient.SetPersonalityAsync(new SetPersonalityRequest
            {
                CharacterId = characterId,
                Traits = new List<TraitValue>
                {
                    new TraitValue { Axis = TraitAxis.Openness, Value = 0.5f },
                    new TraitValue { Axis = TraitAxis.Conscientiousness, Value = 0.3f },
                    new TraitValue { Axis = TraitAxis.Extraversion, Value = -0.2f },
                    new TraitValue { Axis = TraitAxis.Agreeableness, Value = 0.7f },
                    new TraitValue { Axis = TraitAxis.Neuroticism, Value = -0.4f },
                    new TraitValue { Axis = TraitAxis.Honesty, Value = 0.8f },
                    new TraitValue { Axis = TraitAxis.Aggression, Value = -0.5f },
                    new TraitValue { Axis = TraitAxis.Loyalty, Value = 0.6f }
                }
            });

            if (response.CharacterId != characterId)
                return TestResult.Failed("Character ID mismatch");

            return TestResult.Successful($"Personality set: characterId={characterId}");
        }, "Set personality");

    private static async Task<TestResult> TestGetPersonality(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var personalityClient = GetServiceClient<ICharacterPersonalityClient>();
            var characterId = Guid.NewGuid();

            // Set first
            await personalityClient.SetPersonalityAsync(new SetPersonalityRequest
            {
                CharacterId = characterId,
                Traits = new List<TraitValue>
                {
                    new TraitValue { Axis = TraitAxis.Openness, Value = 0.5f }
                }
            });

            // Then get
            var response = await personalityClient.GetPersonalityAsync(new GetPersonalityRequest
            {
                CharacterId = characterId
            });

            if (response.CharacterId != characterId)
                return TestResult.Failed("Character ID mismatch");

            return TestResult.Successful($"Personality retrieved: {response.Traits.Count} traits");
        }, "Get personality");

    private static async Task<TestResult> TestRecordExperience(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var personalityClient = GetServiceClient<ICharacterPersonalityClient>();
            var characterId = Guid.NewGuid();

            // Set personality first
            await personalityClient.SetPersonalityAsync(new SetPersonalityRequest
            {
                CharacterId = characterId,
                Traits = new List<TraitValue>
                {
                    new TraitValue { Axis = TraitAxis.Agreeableness, Value = 0.5f }
                }
            });

            // Record experience
            var response = await personalityClient.RecordExperienceAsync(new RecordExperienceRequest
            {
                CharacterId = characterId,
                ExperienceType = ExperienceType.Trauma,
                Intensity = 0.7f
            });

            return TestResult.Successful($"Experience recorded: evolved={response.PersonalityEvolved}");
        }, "Record experience");

    private static async Task<TestResult> TestBatchGetPersonalities(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var personalityClient = GetServiceClient<ICharacterPersonalityClient>();
            var char1 = Guid.NewGuid();
            var char2 = Guid.NewGuid();

            // Set personalities
            await personalityClient.SetPersonalityAsync(new SetPersonalityRequest
            {
                CharacterId = char1,
                Traits = new List<TraitValue> { new TraitValue { Axis = TraitAxis.Openness, Value = 0.5f } }
            });
            await personalityClient.SetPersonalityAsync(new SetPersonalityRequest
            {
                CharacterId = char2,
                Traits = new List<TraitValue> { new TraitValue { Axis = TraitAxis.Openness, Value = -0.5f } }
            });

            // Batch get
            var response = await personalityClient.BatchGetPersonalitiesAsync(new BatchGetPersonalitiesRequest
            {
                CharacterIds = new List<Guid> { char1, char2 }
            });

            if (response.Personalities.Count != 2)
                return TestResult.Failed($"Expected 2 personalities, got {response.Personalities.Count}");

            return TestResult.Successful($"Batch retrieved: {response.Personalities.Count} personalities");
        }, "Batch get personalities");

    private static async Task<TestResult> TestDeletePersonality(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var personalityClient = GetServiceClient<ICharacterPersonalityClient>();
            var characterId = Guid.NewGuid();

            // Set first
            await personalityClient.SetPersonalityAsync(new SetPersonalityRequest
            {
                CharacterId = characterId,
                Traits = new List<TraitValue> { new TraitValue { Axis = TraitAxis.Openness, Value = 0.5f } }
            });

            // Delete (void return)
            await personalityClient.DeletePersonalityAsync(new DeletePersonalityRequest
            {
                CharacterId = characterId
            });

            // Verify deletion by trying to get it
            try
            {
                await personalityClient.GetPersonalityAsync(new GetPersonalityRequest
                {
                    CharacterId = characterId
                });
                return TestResult.Failed("Personality still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful($"Personality deleted: characterId={characterId}");
            }
        }, "Delete personality");

    private static async Task<TestResult> TestSetCombatPreferences(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var personalityClient = GetServiceClient<ICharacterPersonalityClient>();
            var characterId = Guid.NewGuid();

            var response = await personalityClient.SetCombatPreferencesAsync(new SetCombatPreferencesRequest
            {
                CharacterId = characterId,
                Preferences = new CombatPreferences
                {
                    Style = CombatStyle.Tactical,
                    PreferredRange = PreferredRange.Medium,
                    GroupRole = GroupRole.Support
                }
            });

            if (response.CharacterId != characterId)
                return TestResult.Failed("Character ID mismatch");

            return TestResult.Successful($"Combat preferences set: style={response.Preferences.Style}");
        }, "Set combat preferences");

    private static async Task<TestResult> TestGetCombatPreferences(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var personalityClient = GetServiceClient<ICharacterPersonalityClient>();
            var characterId = Guid.NewGuid();

            // Set first
            await personalityClient.SetCombatPreferencesAsync(new SetCombatPreferencesRequest
            {
                CharacterId = characterId,
                Preferences = new CombatPreferences
                {
                    Style = CombatStyle.Aggressive,
                    PreferredRange = PreferredRange.Melee,
                    GroupRole = GroupRole.Frontline
                }
            });

            // Get
            var response = await personalityClient.GetCombatPreferencesAsync(new GetCombatPreferencesRequest
            {
                CharacterId = characterId
            });

            if (response.CharacterId != characterId)
                return TestResult.Failed("Character ID mismatch");

            return TestResult.Successful($"Combat preferences retrieved: style={response.Preferences.Style}");
        }, "Get combat preferences");

    private static async Task<TestResult> TestEvolveCombatPreferences(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var personalityClient = GetServiceClient<ICharacterPersonalityClient>();
            var characterId = Guid.NewGuid();

            // Set combat preferences first
            await personalityClient.SetCombatPreferencesAsync(new SetCombatPreferencesRequest
            {
                CharacterId = characterId,
                Preferences = new CombatPreferences
                {
                    Style = CombatStyle.Balanced,
                    PreferredRange = PreferredRange.Medium,
                    GroupRole = GroupRole.Support
                }
            });

            // Evolve via combat experience
            var response = await personalityClient.EvolveCombatPreferencesAsync(new EvolveCombatRequest
            {
                CharacterId = characterId,
                ExperienceType = CombatExperienceType.DecisiveVictory,
                Intensity = 0.8f
            });

            return TestResult.Successful($"Combat preferences evolved: evolved={response.PreferencesEvolved}");
        }, "Evolve combat preferences");

    private static async Task<TestResult> TestDeleteCombatPreferences(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var personalityClient = GetServiceClient<ICharacterPersonalityClient>();
            var characterId = Guid.NewGuid();

            // Set first
            await personalityClient.SetCombatPreferencesAsync(new SetCombatPreferencesRequest
            {
                CharacterId = characterId,
                Preferences = new CombatPreferences
                {
                    Style = CombatStyle.Defensive,
                    PreferredRange = PreferredRange.Ranged,
                    GroupRole = GroupRole.Support
                }
            });

            // Delete (void return)
            await personalityClient.DeleteCombatPreferencesAsync(new DeleteCombatPreferencesRequest
            {
                CharacterId = characterId
            });

            // Verify deletion by trying to get it
            try
            {
                await personalityClient.GetCombatPreferencesAsync(new GetCombatPreferencesRequest
                {
                    CharacterId = characterId
                });
                return TestResult.Failed("Combat preferences still exist after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful($"Combat preferences deleted: characterId={characterId}");
            }
        }, "Delete combat preferences");
}
