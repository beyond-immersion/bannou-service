using System.Text.Json;
using BeyondImmersion.Bannou.Client;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for realm service API endpoints.
/// Tests the realm service APIs through the Connect service WebSocket binary protocol.
/// </summary>
public class RealmWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "REALM";
    private const string Description = "Realm";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListRealmsViaWebSocket, "Realm - List (WebSocket)", "WebSocket",
            "Test realm listing via WebSocket binary protocol"),
        new ServiceTest(TestCreateAndGetRealmViaWebSocket, "Realm - Create and Get (WebSocket)", "WebSocket",
            "Test realm creation and retrieval via WebSocket binary protocol"),
        new ServiceTest(TestRealmLifecycleViaWebSocket, "Realm - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete realm lifecycle via WebSocket: create -> update -> deprecate -> undeprecate"),
    ];

    private void TestListRealmsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Realm List Test (WebSocket) ===");
        Console.WriteLine("Testing /realm/list via shared admin WebSocket...");

        RunWebSocketTest("Realm list test", async adminClient =>
        {
            var response = await InvokeApiAsync(adminClient, "/realm/list", new { });

            var hasRealmsArray = HasArrayProperty(response, "realms");
            var totalCount = GetIntProperty(response, "totalCount");

            Console.WriteLine($"   Realms array present: {hasRealmsArray}");
            Console.WriteLine($"   Total Count: {totalCount}");

            return hasRealmsArray;
        });
    }

    private void TestCreateAndGetRealmViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Realm Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /realm/create and /realm/get via shared admin WebSocket...");

        RunWebSocketTest("Realm create and get test", async adminClient =>
        {
            var uniqueCode = $"REALM{GenerateUniqueCode()}";

            // Create realm
            Console.WriteLine("   Invoking /realm/create...");
            var createResponse = await InvokeApiAsync(adminClient, "/realm/create", new
            {
                code = uniqueCode,
                name = $"Test Realm {uniqueCode}",
                description = "Created via WebSocket edge test",
                category = "test"
            });

            var realmId = GetStringProperty(createResponse, "realmId");
            if (string.IsNullOrEmpty(realmId))
            {
                Console.WriteLine("   Failed to create realm - no realmId in response");
                return false;
            }

            Console.WriteLine($"   Created realm: {realmId} ({GetStringProperty(createResponse, "code")})");

            // Retrieve it
            Console.WriteLine("   Invoking /realm/get...");
            var getResponse = await InvokeApiAsync(adminClient, "/realm/get", new { realmId });

            var retrievedId = GetStringProperty(getResponse, "realmId");
            var retrievedCode = GetStringProperty(getResponse, "code");

            Console.WriteLine($"   Retrieved realm: {retrievedId} ({retrievedCode})");

            return retrievedId == realmId && retrievedCode == uniqueCode;
        });
    }

    private void TestRealmLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Realm Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete realm lifecycle via shared admin WebSocket...");

        RunWebSocketTest("Realm complete lifecycle test", async adminClient =>
        {
            // Step 1: Create realm
            Console.WriteLine("   Step 1: Creating realm...");
            var uniqueCode = $"LIFE{GenerateUniqueCode()}";

            var createResponse = await InvokeApiAsync(adminClient, "/realm/create", new
            {
                code = uniqueCode,
                name = $"Lifecycle Test {uniqueCode}",
                description = "Lifecycle test realm",
                category = "test"
            });

            var realmId = GetStringProperty(createResponse, "realmId");
            if (string.IsNullOrEmpty(realmId))
            {
                Console.WriteLine("   Failed to create realm - no realmId in response");
                return false;
            }
            Console.WriteLine($"   Created realm {realmId}");

            // Step 2: Update realm
            Console.WriteLine("   Step 2: Updating realm...");
            var updateResponse = await InvokeApiAsync(adminClient, "/realm/update", new
            {
                realmId,
                name = $"Updated Lifecycle Test {uniqueCode}",
                description = "Updated description"
            });

            var updatedName = GetStringProperty(updateResponse, "name");
            if (!updatedName?.StartsWith("Updated") ?? true)
            {
                Console.WriteLine($"   Failed to update realm - name: {updatedName}");
                return false;
            }
            Console.WriteLine($"   Updated realm name to: {updatedName}");

            // Step 3: Deprecate realm
            Console.WriteLine("   Step 3: Deprecating realm...");
            var deprecateResponse = await InvokeApiAsync(adminClient, "/realm/deprecate", new
            {
                realmId,
                reason = "WebSocket lifecycle test"
            });

            var isDeprecated = deprecateResponse?["isDeprecated"]?.GetValue<bool>() ?? false;
            if (!isDeprecated)
            {
                Console.WriteLine("   Failed to deprecate realm");
                return false;
            }
            Console.WriteLine("   Realm deprecated successfully");

            // Step 4: Undeprecate realm
            Console.WriteLine("   Step 4: Undeprecating realm...");
            var undeprecateResponse = await InvokeApiAsync(adminClient, "/realm/undeprecate", new { realmId });

            var isUndeprecated = !(undeprecateResponse?["isDeprecated"]?.GetValue<bool>() ?? true);
            if (!isUndeprecated)
            {
                Console.WriteLine("   Failed to undeprecate realm");
                return false;
            }
            Console.WriteLine("   Realm restored successfully");

            return true;
        });
    }
}
