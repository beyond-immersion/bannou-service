using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Character;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for character service API endpoints.
/// Tests the character service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
/// </summary>
public class CharacterWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "CHAR";
    private const string Description = "Character";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListCharactersViaWebSocket, "Character - List (WebSocket)", "WebSocket",
            "Test character listing via typed proxy"),
        new ServiceTest(TestCreateAndGetCharacterViaWebSocket, "Character - Create and Get (WebSocket)", "WebSocket",
            "Test character creation and retrieval via typed proxy"),
        new ServiceTest(TestCharacterLifecycleViaWebSocket, "Character - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete character lifecycle via typed proxy: create -> update -> delete"),
    ];

    private void TestListCharactersViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Character List Test (WebSocket) ===");
        Console.WriteLine("Testing character list via typed proxy...");

        RunWebSocketTest("Character list test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create a test realm (realmId is required for listing)
            var realm = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realm == null)
                return false;

            // List characters for this realm using typed proxy
            var response = await adminClient.Character.ListCharactersAsync(new ListCharactersRequest
            {
                RealmId = realm.RealmId
            });

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to list characters: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Characters array present: {result.Characters != null}");
            Console.WriteLine($"   Total Count: {result.TotalCount}");

            return result.Characters != null;
        });
    }

    private void TestCreateAndGetCharacterViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Character Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing character creation and retrieval via typed proxy...");

        RunWebSocketTest("Character create and get test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create test realm and species (required for character creation)
            var realm = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realm == null)
                return false;

            var species = await CreateTestSpeciesAsync(adminClient, CodePrefix, Description, uniqueCode, realm.RealmId);
            if (species == null)
                return false;

            var uniqueName = $"TestChar{uniqueCode}";

            // Create character using typed proxy
            Console.WriteLine("   Creating character via typed proxy...");
            var createResponse = await adminClient.Character.CreateCharacterAsync(new CreateCharacterRequest
            {
                Name = uniqueName,
                RealmId = realm.RealmId,
                SpeciesId = species.SpeciesId,
                BirthDate = DateTimeOffset.UtcNow,
                Status = CharacterStatus.Alive
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create character: {FormatError(createResponse.Error)}");
                return false;
            }

            var character = createResponse.Result;
            Console.WriteLine($"   Created character: {character.CharacterId} ({character.Name})");

            // Retrieve it using typed proxy
            Console.WriteLine("   Retrieving character via typed proxy...");
            var getResponse = await adminClient.Character.GetCharacterAsync(new GetCharacterRequest
            {
                CharacterId = character.CharacterId
            });

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get character: {FormatError(getResponse.Error)}");
                return false;
            }

            var retrieved = getResponse.Result;
            Console.WriteLine($"   Retrieved character: {retrieved.CharacterId} ({retrieved.Name})");

            return retrieved.CharacterId == character.CharacterId && retrieved.Name == uniqueName;
        });
    }

    private void TestCharacterLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Character Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete character lifecycle via typed proxy...");

        RunWebSocketTest("Character complete lifecycle test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create test realm and species
            var realm = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realm == null)
                return false;

            var species = await CreateTestSpeciesAsync(adminClient, CodePrefix, Description, uniqueCode, realm.RealmId);
            if (species == null)
                return false;

            // Step 1: Create character
            Console.WriteLine("   Step 1: Creating character...");
            var uniqueName = $"LifecycleChar{uniqueCode}";

            var createResponse = await adminClient.Character.CreateCharacterAsync(new CreateCharacterRequest
            {
                Name = uniqueName,
                RealmId = realm.RealmId,
                SpeciesId = species.SpeciesId,
                BirthDate = DateTimeOffset.UtcNow,
                Status = CharacterStatus.Alive
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create character: {FormatError(createResponse.Error)}");
                return false;
            }

            var character = createResponse.Result;
            Console.WriteLine($"   Created character {character.CharacterId}");

            // Step 2: Update character name
            Console.WriteLine("   Step 2: Updating character name...");
            var updateResponse = await adminClient.Character.UpdateCharacterAsync(new UpdateCharacterRequest
            {
                CharacterId = character.CharacterId,
                Name = $"Updated {uniqueName}"
            });

            if (!updateResponse.IsSuccess || updateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to update character: {FormatError(updateResponse.Error)}");
                return false;
            }

            var updated = updateResponse.Result;
            if (!updated.Name.StartsWith("Updated"))
            {
                Console.WriteLine($"   Update didn't apply - name: {updated.Name}");
                return false;
            }
            Console.WriteLine($"   Updated character name to: {updated.Name}");

            // Step 3: Update character status to dead
            Console.WriteLine("   Step 3: Setting character status to dead...");
            var deathResponse = await adminClient.Character.UpdateCharacterAsync(new UpdateCharacterRequest
            {
                CharacterId = character.CharacterId,
                Status = CharacterStatus.Dead,
                DeathDate = DateTimeOffset.UtcNow
            });

            if (!deathResponse.IsSuccess || deathResponse.Result == null)
            {
                Console.WriteLine($"   Failed to update character status: {FormatError(deathResponse.Error)}");
                return false;
            }

            if (deathResponse.Result.Status != CharacterStatus.Dead)
            {
                Console.WriteLine($"   Failed to set character status to dead - status: {deathResponse.Result.Status}");
                return false;
            }
            Console.WriteLine($"   Character status set to: {deathResponse.Result.Status}");

            // Step 4: Delete character (event-style operation, no response)
            Console.WriteLine("   Step 4: Deleting character...");
            await adminClient.Character.DeleteCharacterEventAsync(new DeleteCharacterRequest
            {
                CharacterId = character.CharacterId
            });
            Console.WriteLine("   Character deletion event sent successfully");

            return true;
        });
    }
}
