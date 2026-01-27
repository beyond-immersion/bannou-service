using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Realm;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for realm service API endpoints.
/// Tests the realm service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
/// </summary>
public class RealmWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "REALM";
    private const string Description = "Realm";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListRealmsViaWebSocket, "Realm - List (WebSocket)", "WebSocket",
            "Test realm listing via typed proxy"),
        new ServiceTest(TestCreateAndGetRealmViaWebSocket, "Realm - Create and Get (WebSocket)", "WebSocket",
            "Test realm creation and retrieval via typed proxy"),
        new ServiceTest(TestRealmLifecycleViaWebSocket, "Realm - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete realm lifecycle via typed proxy: create -> update -> deprecate -> undeprecate"),
    ];

    private void TestListRealmsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Realm List Test (WebSocket) ===");
        Console.WriteLine("Testing realm list via typed proxy...");

        RunWebSocketTest("Realm list test", async adminClient =>
        {
            var response = await adminClient.Realm.ListRealmsAsync(new ListRealmsRequest());

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to list realms: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Realms returned: {result.Realms?.Count ?? 0}");
            Console.WriteLine($"   Total Count: {result.TotalCount}");

            return result.Realms != null;
        });
    }

    private void TestCreateAndGetRealmViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Realm Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing realm creation and retrieval via typed proxy...");

        RunWebSocketTest("Realm create and get test", async adminClient =>
        {
            var uniqueCode = $"REALM{GenerateUniqueCode()}";

            // Get or create a game service first (required for realm creation)
            var gameServiceId = await GetOrCreateTestGameServiceAsync(adminClient);
            if (gameServiceId == null)
            {
                Console.WriteLine("   Failed to get/create game service");
                return false;
            }

            // Create realm using typed proxy
            Console.WriteLine("   Creating realm via typed proxy...");
            var createResponse = await adminClient.Realm.CreateRealmAsync(new CreateRealmRequest
            {
                Code = uniqueCode,
                Name = $"Test Realm {uniqueCode}",
                Description = "Created via WebSocket edge test",
                Category = "test",
                GameServiceId = gameServiceId.Value
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create realm: {FormatError(createResponse.Error)}");
                return false;
            }

            var realm = createResponse.Result;
            Console.WriteLine($"   Created realm: {realm.RealmId} ({realm.Code})");

            // Retrieve it using typed proxy
            Console.WriteLine("   Retrieving realm via typed proxy...");
            var getResponse = await adminClient.Realm.GetRealmAsync(new GetRealmRequest
            {
                RealmId = realm.RealmId
            });

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get realm: {FormatError(getResponse.Error)}");
                return false;
            }

            var retrieved = getResponse.Result;
            Console.WriteLine($"   Retrieved realm: {retrieved.RealmId} ({retrieved.Code})");

            return retrieved.RealmId == realm.RealmId && retrieved.Code == uniqueCode;
        });
    }

    private void TestRealmLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Realm Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete realm lifecycle via typed proxy...");

        RunWebSocketTest("Realm complete lifecycle test", async adminClient =>
        {
            // Get or create a game service first (required for realm creation)
            var gameServiceId = await GetOrCreateTestGameServiceAsync(adminClient);
            if (gameServiceId == null)
            {
                Console.WriteLine("   Failed to get/create game service");
                return false;
            }

            // Step 1: Create realm
            Console.WriteLine("   Step 1: Creating realm...");
            var uniqueCode = $"LIFE{GenerateUniqueCode()}";

            var createResponse = await adminClient.Realm.CreateRealmAsync(new CreateRealmRequest
            {
                Code = uniqueCode,
                Name = $"Lifecycle Test {uniqueCode}",
                Description = "Lifecycle test realm",
                Category = "test",
                GameServiceId = gameServiceId.Value
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create realm: {FormatError(createResponse.Error)}");
                return false;
            }

            var realm = createResponse.Result;
            Console.WriteLine($"   Created realm {realm.RealmId}");

            // Step 2: Update realm
            Console.WriteLine("   Step 2: Updating realm...");
            var updateResponse = await adminClient.Realm.UpdateRealmAsync(new UpdateRealmRequest
            {
                RealmId = realm.RealmId,
                Name = $"Updated Lifecycle Test {uniqueCode}",
                Description = "Updated description"
            });

            if (!updateResponse.IsSuccess || updateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to update realm: {FormatError(updateResponse.Error)}");
                return false;
            }

            var updated = updateResponse.Result;
            if (!updated.Name.StartsWith("Updated"))
            {
                Console.WriteLine($"   Update didn't apply - name: {updated.Name}");
                return false;
            }
            Console.WriteLine($"   Updated realm name to: {updated.Name}");

            // Step 3: Deprecate realm
            Console.WriteLine("   Step 3: Deprecating realm...");
            var deprecateResponse = await adminClient.Realm.DeprecateRealmAsync(new DeprecateRealmRequest
            {
                RealmId = realm.RealmId,
                Reason = "WebSocket lifecycle test"
            });

            if (!deprecateResponse.IsSuccess || deprecateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to deprecate realm: {FormatError(deprecateResponse.Error)}");
                return false;
            }

            if (!deprecateResponse.Result.IsDeprecated)
            {
                Console.WriteLine("   Realm not marked as deprecated");
                return false;
            }
            Console.WriteLine("   Realm deprecated successfully");

            // Step 4: Undeprecate realm
            Console.WriteLine("   Step 4: Undeprecating realm...");
            var undeprecateResponse = await adminClient.Realm.UndeprecateRealmAsync(new UndeprecateRealmRequest
            {
                RealmId = realm.RealmId
            });

            if (!undeprecateResponse.IsSuccess || undeprecateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to undeprecate realm: {FormatError(undeprecateResponse.Error)}");
                return false;
            }

            if (undeprecateResponse.Result.IsDeprecated)
            {
                Console.WriteLine("   Realm still marked as deprecated");
                return false;
            }
            Console.WriteLine("   Realm restored successfully");

            return true;
        });
    }
}
