using System.Text.Json;
using BeyondImmersion.Bannou.Client;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for location service API endpoints.
/// Tests the location service APIs through the Connect service WebSocket binary protocol.
/// </summary>
public class LocationWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "LOC";
    private const string Description = "Location";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListLocationsByRealmViaWebSocket, "Location - List by Realm (WebSocket)", "WebSocket",
            "Test location listing by realm via WebSocket binary protocol"),
        new ServiceTest(TestCreateAndGetLocationViaWebSocket, "Location - Create and Get (WebSocket)", "WebSocket",
            "Test location creation and retrieval via WebSocket binary protocol"),
        new ServiceTest(TestLocationLifecycleViaWebSocket, "Location - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete location lifecycle via WebSocket: create -> update -> deprecate -> undeprecate"),
        new ServiceTest(TestLocationHierarchyViaWebSocket, "Location - Hierarchy (WebSocket)", "WebSocket",
            "Test location hierarchy (parent/child relationships) via WebSocket binary protocol"),
    ];

    private void TestListLocationsByRealmViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location List by Realm Test (WebSocket) ===");
        Console.WriteLine("Testing /location/list-by-realm via shared admin WebSocket...");

        RunWebSocketTest("Location list by realm test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            var realmId = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realmId == null)
                return false;

            Console.WriteLine("   Invoking /location/list-by-realm...");
            var response = await InvokeApiAsync(adminClient, "/location/list-by-realm", new { realmId });

            var hasLocationsArray = HasArrayProperty(response, "locations");
            var totalCount = GetIntProperty(response, "totalCount");

            Console.WriteLine($"   Locations array present: {hasLocationsArray}");
            Console.WriteLine($"   Total Count: {totalCount}");

            return hasLocationsArray;
        });
    }

    private void TestCreateAndGetLocationViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /location/create and /location/get via shared admin WebSocket...");

        RunWebSocketTest("Location create and get test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            var realmId = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realmId == null)
                return false;

            var locationCode = $"LOC{uniqueCode}";

            // Create location
            Console.WriteLine("   Invoking /location/create...");
            var createResponse = await InvokeApiAsync(adminClient, "/location/create", new
            {
                realmId,
                code = locationCode,
                name = $"Test Location {locationCode}",
                description = "Created via WebSocket edge test",
                locationType = "city"
            });

            var locationId = GetStringProperty(createResponse, "locationId");
            if (string.IsNullOrEmpty(locationId))
            {
                Console.WriteLine("   Failed to create location - no locationId in response");
                return false;
            }

            Console.WriteLine($"   Created location: {locationId} ({GetStringProperty(createResponse, "code")})");

            // Retrieve it
            Console.WriteLine("   Invoking /location/get...");
            var getResponse = await InvokeApiAsync(adminClient, "/location/get", new { locationId });

            var retrievedId = GetStringProperty(getResponse, "locationId");
            var retrievedCode = GetStringProperty(getResponse, "code");

            Console.WriteLine($"   Retrieved location: {retrievedId} ({retrievedCode})");

            return retrievedId == locationId && retrievedCode == locationCode;
        });
    }

    private void TestLocationLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete location lifecycle via shared admin WebSocket...");

        RunWebSocketTest("Location complete lifecycle test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            var realmId = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realmId == null)
                return false;

            // Step 1: Create location
            Console.WriteLine("   Step 1: Creating location...");
            var locationCode = $"LIFE{uniqueCode}";

            var createResponse = await InvokeApiAsync(adminClient, "/location/create", new
            {
                realmId,
                code = locationCode,
                name = $"Lifecycle Test {locationCode}",
                description = "Lifecycle test location",
                locationType = "region"
            });

            var locationId = GetStringProperty(createResponse, "locationId");
            if (string.IsNullOrEmpty(locationId))
            {
                Console.WriteLine("   Failed to create location - no locationId in response");
                return false;
            }
            Console.WriteLine($"   Created location {locationId}");

            // Step 2: Update location
            Console.WriteLine("   Step 2: Updating location...");
            var updateResponse = await InvokeApiAsync(adminClient, "/location/update", new
            {
                locationId,
                name = $"Updated Lifecycle Test {locationCode}",
                description = "Updated description"
            });

            var updatedName = GetStringProperty(updateResponse, "name");
            if (!updatedName?.StartsWith("Updated") ?? true)
            {
                Console.WriteLine($"   Failed to update location - name: {updatedName}");
                return false;
            }
            Console.WriteLine($"   Updated location name to: {updatedName}");

            // Step 3: Deprecate location
            Console.WriteLine("   Step 3: Deprecating location...");
            var deprecateResponse = await InvokeApiAsync(adminClient, "/location/deprecate", new
            {
                locationId,
                reason = "WebSocket lifecycle test"
            });

            var isDeprecated = deprecateResponse?["isDeprecated"]?.GetValue<bool>() ?? false;
            if (!isDeprecated)
            {
                Console.WriteLine("   Failed to deprecate location");
                return false;
            }
            Console.WriteLine("   Location deprecated successfully");

            // Step 4: Undeprecate location
            Console.WriteLine("   Step 4: Undeprecating location...");
            var undeprecateResponse = await InvokeApiAsync(adminClient, "/location/undeprecate", new { locationId });

            var isUndeprecated = !(undeprecateResponse?["isDeprecated"]?.GetValue<bool>() ?? true);
            if (!isUndeprecated)
            {
                Console.WriteLine("   Failed to undeprecate location");
                return false;
            }
            Console.WriteLine("   Location restored successfully");

            return true;
        });
    }

    private void TestLocationHierarchyViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location Hierarchy Test (WebSocket) ===");
        Console.WriteLine("Testing location parent/child relationships via shared admin WebSocket...");

        RunWebSocketTest("Location hierarchy test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            var realmId = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realmId == null)
                return false;

            // Step 1: Create parent location (region)
            Console.WriteLine("   Step 1: Creating parent location (region)...");
            var parentResponse = await InvokeApiAsync(adminClient, "/location/create", new
            {
                realmId,
                code = $"REGION{uniqueCode}",
                name = $"Test Region {uniqueCode}",
                description = "Parent location for hierarchy test",
                locationType = "region"
            });

            var parentId = GetStringProperty(parentResponse, "locationId");
            if (string.IsNullOrEmpty(parentId))
            {
                Console.WriteLine("   Failed to create parent location");
                return false;
            }
            Console.WriteLine($"   Created parent location: {parentId}");

            // Step 2: Create child location (city under region)
            Console.WriteLine("   Step 2: Creating child location (city)...");
            var childResponse = await InvokeApiAsync(adminClient, "/location/create", new
            {
                realmId,
                code = $"CITY{uniqueCode}",
                name = $"Test City {uniqueCode}",
                description = "Child location for hierarchy test",
                locationType = "city",
                parentLocationId = parentId
            });

            var childId = GetStringProperty(childResponse, "locationId");
            if (string.IsNullOrEmpty(childId))
            {
                Console.WriteLine("   Failed to create child location");
                return false;
            }
            var childDepth = GetIntProperty(childResponse, "depth", -1);
            Console.WriteLine($"   Created child location: {childId} (depth: {childDepth})");

            // Step 3: Verify child has correct depth
            if (childDepth != 1)
            {
                Console.WriteLine($"   FAILED: Expected depth 1 for child, got {childDepth}");
                return false;
            }

            // Step 4: List children of parent
            Console.WriteLine("   Step 4: Listing children of parent...");
            var listResponse = await InvokeApiAsync(adminClient, "/location/list-by-parent", new { parentLocationId = parentId });

            var locationsArray = listResponse?["locations"]?.AsArray();
            var childCount = locationsArray?.Count ?? 0;

            Console.WriteLine($"   Parent has {childCount} children");

            if (childCount != 1)
            {
                Console.WriteLine($"   FAILED: Expected 1 child, got {childCount}");
                return false;
            }

            // Step 5: List root locations in realm
            Console.WriteLine("   Step 5: Listing root locations...");
            var rootsResponse = await InvokeApiAsync(adminClient, "/location/list-root", new { realmId });

            var rootsArray = rootsResponse?["locations"]?.AsArray();
            var rootCount = rootsArray?.Count ?? 0;

            Console.WriteLine($"   Realm has {rootCount} root locations");

            if (rootCount < 1)
            {
                Console.WriteLine("   FAILED: Expected at least 1 root location");
                return false;
            }

            Console.WriteLine("   Hierarchy test completed successfully");
            return true;
        });
    }
}
