using System.Text.Json;
using BeyondImmersion.Bannou.Client;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for species service API endpoints.
/// Tests the species service APIs through the Connect service WebSocket binary protocol.
/// </summary>
public class SpeciesWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "SPEC";
    private const string Description = "Species";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListSpeciesViaWebSocket, "Species - List (WebSocket)", "WebSocket",
            "Test species listing via WebSocket binary protocol"),
        new ServiceTest(TestCreateAndGetSpeciesViaWebSocket, "Species - Create and Get (WebSocket)", "WebSocket",
            "Test species creation and retrieval via WebSocket binary protocol"),
        new ServiceTest(TestSpeciesLifecycleViaWebSocket, "Species - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete species lifecycle via WebSocket: create -> update -> deprecate -> undeprecate"),
    ];

    private void TestListSpeciesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species List Test (WebSocket) ===");
        Console.WriteLine("Testing /species/list via shared admin WebSocket...");

        RunWebSocketTest("Species list test", async adminClient =>
        {
            var response = await InvokeApiAsync(adminClient, "/species/list", new { });

            var hasSpeciesArray = HasArrayProperty(response, "species");
            var totalCount = GetIntProperty(response, "totalCount");

            Console.WriteLine($"   Species array present: {hasSpeciesArray}");
            Console.WriteLine($"   Total Count: {totalCount}");

            return hasSpeciesArray;
        });
    }

    private void TestCreateAndGetSpeciesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /species/create and /species/get via shared admin WebSocket...");

        RunWebSocketTest("Species create and get test", async adminClient =>
        {
            var uniqueCode = $"TEST{GenerateUniqueCode()}";

            // Create species
            Console.WriteLine("   Invoking /species/create...");
            var createResponse = await InvokeApiAsync(adminClient, "/species/create", new
            {
                code = uniqueCode,
                name = $"Test Species {uniqueCode}",
                description = "Created via WebSocket edge test",
                isPlayable = true,
                baseLifespan = 100,
                maturityAge = 18
            });

            var speciesId = GetStringProperty(createResponse, "speciesId");
            if (string.IsNullOrEmpty(speciesId))
            {
                Console.WriteLine("   Failed to create species - no speciesId in response");
                return false;
            }

            Console.WriteLine($"   Created species: {speciesId} ({GetStringProperty(createResponse, "code")})");

            // Retrieve it
            Console.WriteLine("   Invoking /species/get...");
            var getResponse = await InvokeApiAsync(adminClient, "/species/get", new { speciesId });

            var retrievedId = GetStringProperty(getResponse, "speciesId");
            var retrievedCode = GetStringProperty(getResponse, "code");

            Console.WriteLine($"   Retrieved species: {retrievedId} ({retrievedCode})");

            return retrievedId == speciesId && retrievedCode == uniqueCode;
        });
    }

    private void TestSpeciesLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete species lifecycle via shared admin WebSocket...");

        RunWebSocketTest("Species complete lifecycle test", async adminClient =>
        {
            // Step 1: Create species
            Console.WriteLine("   Step 1: Creating species...");
            var uniqueCode = $"LIFE{GenerateUniqueCode()}";

            var createResponse = await InvokeApiAsync(adminClient, "/species/create", new
            {
                code = uniqueCode,
                name = $"Lifecycle Test {uniqueCode}",
                description = "Lifecycle test species",
                isPlayable = false
            });

            var speciesId = GetStringProperty(createResponse, "speciesId");
            if (string.IsNullOrEmpty(speciesId))
            {
                Console.WriteLine("   Failed to create species - no speciesId in response");
                return false;
            }
            Console.WriteLine($"   Created species {speciesId}");

            // Step 2: Update species
            Console.WriteLine("   Step 2: Updating species...");
            var updateResponse = await InvokeApiAsync(adminClient, "/species/update", new
            {
                speciesId,
                name = $"Updated Lifecycle Test {uniqueCode}",
                description = "Updated description"
            });

            var updatedName = GetStringProperty(updateResponse, "name");
            if (!updatedName?.StartsWith("Updated") ?? true)
            {
                Console.WriteLine($"   Failed to update species - name: {updatedName}");
                return false;
            }
            Console.WriteLine($"   Updated species name to: {updatedName}");

            // Step 3: Deprecate species
            Console.WriteLine("   Step 3: Deprecating species...");
            var deprecateResponse = await InvokeApiAsync(adminClient, "/species/deprecate", new
            {
                speciesId,
                reason = "WebSocket lifecycle test"
            });

            var isDeprecated = deprecateResponse?["isDeprecated"]?.GetValue<bool>() ?? false;
            if (!isDeprecated)
            {
                Console.WriteLine("   Failed to deprecate species");
                return false;
            }
            Console.WriteLine("   Species deprecated successfully");

            // Step 4: Undeprecate species
            Console.WriteLine("   Step 4: Undeprecating species...");
            var undeprecateResponse = await InvokeApiAsync(adminClient, "/species/undeprecate", new { speciesId });

            var isUndeprecated = !(undeprecateResponse?["isDeprecated"]?.GetValue<bool>() ?? true);
            if (!isUndeprecated)
            {
                Console.WriteLine("   Failed to undeprecate species");
                return false;
            }
            Console.WriteLine("   Species restored successfully");

            return true;
        });
    }
}
