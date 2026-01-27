using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Location;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for location service API endpoints.
/// Tests the location service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
/// </summary>
public class LocationWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "LOC";
    private const string Description = "Location";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListLocationsByRealmViaWebSocket, "Location - List by Realm (WebSocket)", "WebSocket",
            "Test location listing by realm via typed proxy"),
        new ServiceTest(TestCreateAndGetLocationViaWebSocket, "Location - Create and Get (WebSocket)", "WebSocket",
            "Test location creation and retrieval via typed proxy"),
        new ServiceTest(TestLocationLifecycleViaWebSocket, "Location - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete location lifecycle via typed proxy: create -> update -> deprecate -> undeprecate"),
        new ServiceTest(TestLocationHierarchyViaWebSocket, "Location - Hierarchy (WebSocket)", "WebSocket",
            "Test location hierarchy (parent/child relationships) via typed proxy"),
    ];

    private void TestListLocationsByRealmViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location List by Realm Test (WebSocket) ===");
        Console.WriteLine("Testing location list by realm via typed proxy...");

        RunWebSocketTest("Location list by realm test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            var realm = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realm == null)
                return false;

            Console.WriteLine("   Listing locations by realm via typed proxy...");
            var response = await adminClient.Location.ListLocationsByRealmAsync(new ListLocationsByRealmRequest
            {
                RealmId = realm.RealmId
            });

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to list locations: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Locations array present: {result.Locations != null}");
            Console.WriteLine($"   Total Count: {result.TotalCount}");

            return result.Locations != null;
        });
    }

    private void TestCreateAndGetLocationViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing location creation and retrieval via typed proxy...");

        RunWebSocketTest("Location create and get test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            var realm = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realm == null)
                return false;

            var locationCode = $"LOC{uniqueCode}";

            // Create location using typed proxy
            Console.WriteLine("   Creating location via typed proxy...");
            var createResponse = await adminClient.Location.CreateLocationAsync(new CreateLocationRequest
            {
                RealmId = realm.RealmId,
                Code = locationCode,
                Name = $"Test Location {locationCode}",
                Description = "Created via WebSocket edge test",
                LocationType = LocationType.CITY
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create location: {FormatError(createResponse.Error)}");
                return false;
            }

            var location = createResponse.Result;
            Console.WriteLine($"   Created location: {location.LocationId} ({location.Code})");

            // Retrieve it using typed proxy
            Console.WriteLine("   Retrieving location via typed proxy...");
            var getResponse = await adminClient.Location.GetLocationAsync(new GetLocationRequest
            {
                LocationId = location.LocationId
            });

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get location: {FormatError(getResponse.Error)}");
                return false;
            }

            var retrieved = getResponse.Result;
            Console.WriteLine($"   Retrieved location: {retrieved.LocationId} ({retrieved.Code})");

            return retrieved.LocationId == location.LocationId && retrieved.Code == locationCode;
        });
    }

    private void TestLocationLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete location lifecycle via typed proxy...");

        RunWebSocketTest("Location complete lifecycle test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            var realm = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realm == null)
                return false;

            // Step 1: Create location
            Console.WriteLine("   Step 1: Creating location...");
            var locationCode = $"LIFE{uniqueCode}";

            var createResponse = await adminClient.Location.CreateLocationAsync(new CreateLocationRequest
            {
                RealmId = realm.RealmId,
                Code = locationCode,
                Name = $"Lifecycle Test {locationCode}",
                Description = "Lifecycle test location",
                LocationType = LocationType.REGION
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create location: {FormatError(createResponse.Error)}");
                return false;
            }

            var location = createResponse.Result;
            Console.WriteLine($"   Created location {location.LocationId}");

            // Step 2: Update location
            Console.WriteLine("   Step 2: Updating location...");
            var updateResponse = await adminClient.Location.UpdateLocationAsync(new UpdateLocationRequest
            {
                LocationId = location.LocationId,
                Name = $"Updated Lifecycle Test {locationCode}",
                Description = "Updated description"
            });

            if (!updateResponse.IsSuccess || updateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to update location: {FormatError(updateResponse.Error)}");
                return false;
            }

            var updated = updateResponse.Result;
            if (!updated.Name.StartsWith("Updated"))
            {
                Console.WriteLine($"   Update didn't apply - name: {updated.Name}");
                return false;
            }
            Console.WriteLine($"   Updated location name to: {updated.Name}");

            // Step 3: Deprecate location
            Console.WriteLine("   Step 3: Deprecating location...");
            var deprecateResponse = await adminClient.Location.DeprecateLocationAsync(new DeprecateLocationRequest
            {
                LocationId = location.LocationId,
                Reason = "WebSocket lifecycle test"
            });

            if (!deprecateResponse.IsSuccess || deprecateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to deprecate location: {FormatError(deprecateResponse.Error)}");
                return false;
            }

            if (!deprecateResponse.Result.IsDeprecated)
            {
                Console.WriteLine("   Location not marked as deprecated");
                return false;
            }
            Console.WriteLine("   Location deprecated successfully");

            // Step 4: Undeprecate location
            Console.WriteLine("   Step 4: Undeprecating location...");
            var undeprecateResponse = await adminClient.Location.UndeprecateLocationAsync(new UndeprecateLocationRequest
            {
                LocationId = location.LocationId
            });

            if (!undeprecateResponse.IsSuccess || undeprecateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to undeprecate location: {FormatError(undeprecateResponse.Error)}");
                return false;
            }

            if (undeprecateResponse.Result.IsDeprecated)
            {
                Console.WriteLine("   Location still marked as deprecated");
                return false;
            }
            Console.WriteLine("   Location restored successfully");

            return true;
        });
    }

    private void TestLocationHierarchyViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location Hierarchy Test (WebSocket) ===");
        Console.WriteLine("Testing location parent/child relationships via typed proxy...");

        RunWebSocketTest("Location hierarchy test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            var realm = await CreateTestRealmAsync(adminClient, CodePrefix, Description, uniqueCode);
            if (realm == null)
                return false;

            // Step 1: Create parent location (region)
            Console.WriteLine("   Step 1: Creating parent location (region)...");
            var parentResponse = await adminClient.Location.CreateLocationAsync(new CreateLocationRequest
            {
                RealmId = realm.RealmId,
                Code = $"REGION{uniqueCode}",
                Name = $"Test Region {uniqueCode}",
                Description = "Parent location for hierarchy test",
                LocationType = LocationType.REGION
            });

            if (!parentResponse.IsSuccess || parentResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create parent location: {FormatError(parentResponse.Error)}");
                return false;
            }

            var parent = parentResponse.Result;
            Console.WriteLine($"   Created parent location: {parent.LocationId}");

            // Step 2: Create child location (city under region)
            Console.WriteLine("   Step 2: Creating child location (city)...");
            var childResponse = await adminClient.Location.CreateLocationAsync(new CreateLocationRequest
            {
                RealmId = realm.RealmId,
                Code = $"CITY{uniqueCode}",
                Name = $"Test City {uniqueCode}",
                Description = "Child location for hierarchy test",
                LocationType = LocationType.CITY,
                ParentLocationId = parent.LocationId
            });

            if (!childResponse.IsSuccess || childResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create child location: {FormatError(childResponse.Error)}");
                return false;
            }

            var child = childResponse.Result;
            Console.WriteLine($"   Created child location: {child.LocationId} (depth: {child.Depth})");

            // Step 3: Verify child has correct depth
            if (child.Depth != 1)
            {
                Console.WriteLine($"   FAILED: Expected depth 1 for child, got {child.Depth}");
                return false;
            }

            // Step 4: List children of parent
            Console.WriteLine("   Step 4: Listing children of parent...");
            var listResponse = await adminClient.Location.ListLocationsByParentAsync(new ListLocationsByParentRequest
            {
                ParentLocationId = parent.LocationId
            });

            if (!listResponse.IsSuccess || listResponse.Result == null)
            {
                Console.WriteLine($"   Failed to list children: {FormatError(listResponse.Error)}");
                return false;
            }

            var childCount = listResponse.Result.Locations?.Count ?? 0;
            Console.WriteLine($"   Parent has {childCount} children");

            if (childCount != 1)
            {
                Console.WriteLine($"   FAILED: Expected 1 child, got {childCount}");
                return false;
            }

            // Step 5: List root locations in realm
            Console.WriteLine("   Step 5: Listing root locations...");
            var rootsResponse = await adminClient.Location.ListRootLocationsAsync(new ListRootLocationsRequest
            {
                RealmId = realm.RealmId
            });

            if (!rootsResponse.IsSuccess || rootsResponse.Result == null)
            {
                Console.WriteLine($"   Failed to list roots: {FormatError(rootsResponse.Error)}");
                return false;
            }

            var rootCount = rootsResponse.Result.Locations?.Count ?? 0;
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
