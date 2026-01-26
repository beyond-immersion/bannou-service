using System.Text.Json;
using BeyondImmersion.Bannou.Client;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for character service API endpoints.
/// Tests the character service APIs through the Connect service WebSocket binary protocol.
/// </summary>
public class CharacterWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "CHAR";
    private const string Description = "Character";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListCharactersViaWebSocket, "Character - List (WebSocket)", "WebSocket",
            "Test character listing via WebSocket binary protocol"),
        new ServiceTest(TestCreateAndGetCharacterViaWebSocket, "Character - Create and Get (WebSocket)", "WebSocket",
            "Test character creation and retrieval via WebSocket binary protocol"),
        new ServiceTest(TestCharacterLifecycleViaWebSocket, "Character - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete character lifecycle via WebSocket: create -> update -> delete"),
    ];

    private void TestListCharactersViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Character List Test (WebSocket) ===");
        Console.WriteLine("Testing /character/list via shared admin WebSocket...");

        RunWebSocketTest("Character list test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create a test realm (realmId is required for listing)
            var realmId = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realmId == null)
                return false;

            // List characters for this realm
            var response = await InvokeApiAsync(adminClient, "/character/list", new { realmId });

            var hasCharactersArray = HasArrayProperty(response, "characters");
            var totalCount = GetIntProperty(response, "totalCount");

            Console.WriteLine($"   Characters array present: {hasCharactersArray}");
            Console.WriteLine($"   Total Count: {totalCount}");

            return hasCharactersArray;
        });
    }

    private void TestCreateAndGetCharacterViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Character Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /character/create and /character/get via shared admin WebSocket...");

        RunWebSocketTest("Character create and get test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create test realm and species (required for character creation)
            var realmId = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realmId == null)
                return false;

            var speciesId = await CreateTestSpeciesAsync(adminClient, CodePrefix, Description, uniqueCode, realmId);
            if (speciesId == null)
                return false;

            var uniqueName = $"TestChar{uniqueCode}";

            // Create character
            Console.WriteLine("   Invoking /character/create...");
            var createResponse = await InvokeApiAsync(adminClient, "/character/create", new
            {
                name = uniqueName,
                realmId,
                speciesId,
                birthDate = DateTimeOffset.UtcNow,
                status = "alive"
            });

            var characterId = GetStringProperty(createResponse, "characterId");
            if (string.IsNullOrEmpty(characterId))
            {
                Console.WriteLine("   Failed to create character - no characterId in response");
                return false;
            }

            Console.WriteLine($"   Created character: {characterId} ({GetStringProperty(createResponse, "name")})");

            // Retrieve it
            Console.WriteLine("   Invoking /character/get...");
            var getResponse = await InvokeApiAsync(adminClient, "/character/get", new { characterId });

            var retrievedId = GetStringProperty(getResponse, "characterId");
            var retrievedName = GetStringProperty(getResponse, "name");

            Console.WriteLine($"   Retrieved character: {retrievedId} ({retrievedName})");

            return retrievedId == characterId && retrievedName == uniqueName;
        });
    }

    private void TestCharacterLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Character Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete character lifecycle via shared admin WebSocket...");

        RunWebSocketTest("Character complete lifecycle test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create test realm and species
            var realmId = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realmId == null)
                return false;

            var speciesId = await CreateTestSpeciesAsync(adminClient, CodePrefix, Description, uniqueCode, realmId);
            if (speciesId == null)
                return false;

            // Step 1: Create character
            Console.WriteLine("   Step 1: Creating character...");
            var uniqueName = $"LifecycleChar{uniqueCode}";

            var createResponse = await InvokeApiAsync(adminClient, "/character/create", new
            {
                name = uniqueName,
                realmId,
                speciesId,
                birthDate = DateTimeOffset.UtcNow,
                status = "alive"
            });

            var characterId = GetStringProperty(createResponse, "characterId");
            if (string.IsNullOrEmpty(characterId))
            {
                Console.WriteLine("   Failed to create character - no characterId in response");
                return false;
            }
            Console.WriteLine($"   Created character {characterId}");

            // Step 2: Update character name
            Console.WriteLine("   Step 2: Updating character name...");
            var updateResponse = await InvokeApiAsync(adminClient, "/character/update", new
            {
                characterId,
                name = $"Updated {uniqueName}"
            });

            var updatedName = GetStringProperty(updateResponse, "name");
            if (!updatedName?.StartsWith("Updated") ?? true)
            {
                Console.WriteLine($"   Failed to update character - name: {updatedName}");
                return false;
            }
            Console.WriteLine($"   Updated character name to: {updatedName}");

            // Step 3: Update character status to dead
            Console.WriteLine("   Step 3: Setting character status to dead...");
            var deathResponse = await InvokeApiAsync(adminClient, "/character/update", new
            {
                characterId,
                status = "dead",
                deathDate = DateTimeOffset.UtcNow
            });

            var statusStr = GetStringProperty(deathResponse, "status");
            if (!string.Equals(statusStr, "dead", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"   Failed to set character status to dead - status: {statusStr}");
                return false;
            }
            Console.WriteLine($"   Character status set to: {statusStr}");

            // Step 4: Delete character
            Console.WriteLine("   Step 4: Deleting character...");
            await InvokeApiAsync(adminClient, "/character/delete", new { characterId });
            Console.WriteLine("   Character deleted successfully");

            return true;
        });
    }
}
