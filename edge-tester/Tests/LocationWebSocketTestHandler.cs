using System.Text.Json;
using System.Text.Json.Nodes;
using BeyondImmersion.Bannou.Client.SDK;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for location service API endpoints.
/// Tests the location service APIs through the Connect service WebSocket binary protocol.
///
/// Note: Location create/update/deprecate/undeprecate APIs require admin role,
/// so these tests use Program.AdminClient which is already connected with admin permissions.
/// </summary>
public class LocationWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestListLocationsByRealmViaWebSocket, "Location - List by Realm (WebSocket)", "WebSocket",
                "Test location listing by realm via WebSocket binary protocol"),
            new ServiceTest(TestCreateAndGetLocationViaWebSocket, "Location - Create and Get (WebSocket)", "WebSocket",
                "Test location creation and retrieval via WebSocket binary protocol"),
            new ServiceTest(TestLocationLifecycleViaWebSocket, "Location - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete location lifecycle via WebSocket: create -> update -> deprecate -> undeprecate"),
            new ServiceTest(TestLocationHierarchyViaWebSocket, "Location - Hierarchy (WebSocket)", "WebSocket",
                "Test location hierarchy (parent/child relationships) via WebSocket binary protocol"),
        };
    }

    #region Helper Methods

    /// <summary>
    /// Creates a test realm for location tests using the shared admin client.
    /// </summary>
    private async Task<string?> CreateTestRealmAsync(BannouClient adminClient, string uniqueCode)
    {
        try
        {
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/realm/create",
                new
                {
                    code = $"LOC_REALM_{uniqueCode}",
                    name = $"Location Test Realm {uniqueCode}",
                    description = "Test realm for location tests",
                    category = "test"
                },
                timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

            var responseJson = JsonNode.Parse(response.GetRawText())?.AsObject();
            var realmIdStr = responseJson?["realmId"]?.GetValue<string>();
            if (string.IsNullOrEmpty(realmIdStr))
            {
                Console.WriteLine("   Failed to create test realm");
                return null;
            }

            Console.WriteLine($"   Created test realm: {realmIdStr}");
            return realmIdStr;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test realm: {ex.Message}");
            return null;
        }
    }

    #endregion

    private void TestListLocationsByRealmViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location List by Realm Test (WebSocket) ===");
        Console.WriteLine("Testing /location/list-by-realm via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
                    return false;
                }

                // First create a test realm
                var uniqueCode = $"{DateTime.Now.Ticks % 100000}";
                var realmIdStr = await CreateTestRealmAsync(adminClient, uniqueCode);
                if (realmIdStr == null)
                {
                    return false;
                }

                try
                {
                    Console.WriteLine("   Invoking /location/list-by-realm...");
                    var response = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/list-by-realm",
                        new { realmId = realmIdStr },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var responseJson = JsonNode.Parse(response.GetRawText())?.AsObject();
                    var hasLocationsArray = responseJson?["locations"] != null &&
                                            responseJson["locations"] is JsonArray;
                    var totalCount = responseJson?["totalCount"]?.GetValue<int>() ?? 0;

                    Console.WriteLine($"   Locations array present: {hasLocationsArray}");
                    Console.WriteLine($"   Total Count: {totalCount}");

                    return hasLocationsArray;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Location list by realm test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Location list by realm test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Location list by realm test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCreateAndGetLocationViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /location/create and /location/get via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
                    return false;
                }

                // First create a test realm
                var uniqueCode = $"{DateTime.Now.Ticks % 100000}";
                var realmIdStr = await CreateTestRealmAsync(adminClient, uniqueCode);
                if (realmIdStr == null)
                {
                    return false;
                }

                var locationCode = $"LOC{uniqueCode}";

                try
                {
                    // Create location
                    Console.WriteLine("   Invoking /location/create...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/create",
                        new
                        {
                            realmId = realmIdStr,
                            code = locationCode,
                            name = $"Test Location {locationCode}",
                            description = "Created via WebSocket edge test",
                            locationType = "city"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var locationIdStr = createJson?["locationId"]?.GetValue<string>();
                    var code = createJson?["code"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(locationIdStr))
                    {
                        Console.WriteLine("   Failed to create location - no locationId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created location: {locationIdStr} ({code})");

                    // Now retrieve it
                    Console.WriteLine("   Invoking /location/get...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/get",
                        new { locationId = locationIdStr },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var retrievedId = getJson?["locationId"]?.GetValue<string>();
                    var retrievedCode = getJson?["code"]?.GetValue<string>();

                    Console.WriteLine($"   Retrieved location: {retrievedId} ({retrievedCode})");

                    return retrievedId == locationIdStr && retrievedCode == locationCode;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Location create and get test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Location create and get test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Location create and get test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestLocationLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete location lifecycle via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
                    return false;
                }

                try
                {
                    // Create a test realm first
                    var uniqueCode = $"{DateTime.Now.Ticks % 100000}";
                    var realmIdStr = await CreateTestRealmAsync(adminClient, uniqueCode);
                    if (realmIdStr == null)
                    {
                        return false;
                    }

                    // Step 1: Create location
                    Console.WriteLine("   Step 1: Creating location...");
                    var locationCode = $"LIFE{uniqueCode}";

                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/create",
                        new
                        {
                            realmId = realmIdStr,
                            code = locationCode,
                            name = $"Lifecycle Test {locationCode}",
                            description = "Lifecycle test location",
                            locationType = "region"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var locationIdStr = createJson?["locationId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(locationIdStr))
                    {
                        Console.WriteLine("   Failed to create location - no locationId in response");
                        return false;
                    }
                    Console.WriteLine($"   Created location {locationIdStr}");

                    // Step 2: Update location
                    Console.WriteLine("   Step 2: Updating location...");
                    var updateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/update",
                        new
                        {
                            locationId = locationIdStr,
                            name = $"Updated Lifecycle Test {locationCode}",
                            description = "Updated description"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var updateJson = JsonNode.Parse(updateResponse.GetRawText())?.AsObject();
                    var updatedName = updateJson?["name"]?.GetValue<string>();
                    if (!updatedName?.StartsWith("Updated") ?? true)
                    {
                        Console.WriteLine($"   Failed to update location - name: {updatedName}");
                        return false;
                    }
                    Console.WriteLine($"   Updated location name to: {updatedName}");

                    // Step 3: Deprecate location
                    Console.WriteLine("   Step 3: Deprecating location...");
                    var deprecateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/deprecate",
                        new
                        {
                            locationId = locationIdStr,
                            reason = "WebSocket lifecycle test"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var deprecateJson = JsonNode.Parse(deprecateResponse.GetRawText())?.AsObject();
                    var isDeprecated = deprecateJson?["isDeprecated"]?.GetValue<bool>() ?? false;
                    if (!isDeprecated)
                    {
                        Console.WriteLine("   Failed to deprecate location");
                        return false;
                    }
                    Console.WriteLine($"   Location deprecated successfully");

                    // Step 4: Undeprecate location
                    Console.WriteLine("   Step 4: Undeprecating location...");
                    var undeprecateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/undeprecate",
                        new { locationId = locationIdStr },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var undeprecateJson = JsonNode.Parse(undeprecateResponse.GetRawText())?.AsObject();
                    var isUndeprecated = !(undeprecateJson?["isDeprecated"]?.GetValue<bool>() ?? true);
                    if (!isUndeprecated)
                    {
                        Console.WriteLine("   Failed to undeprecate location");
                        return false;
                    }
                    Console.WriteLine($"   Location restored successfully");

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Lifecycle test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Location complete lifecycle test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Location complete lifecycle test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Location lifecycle test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestLocationHierarchyViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Location Hierarchy Test (WebSocket) ===");
        Console.WriteLine("Testing location parent/child relationships via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
                    return false;
                }

                try
                {
                    // Create a test realm first
                    var uniqueCode = $"{DateTime.Now.Ticks % 100000}";
                    var realmIdStr = await CreateTestRealmAsync(adminClient, uniqueCode);
                    if (realmIdStr == null)
                    {
                        return false;
                    }

                    // Step 1: Create parent location (region)
                    Console.WriteLine("   Step 1: Creating parent location (region)...");
                    var parentResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/create",
                        new
                        {
                            realmId = realmIdStr,
                            code = $"REGION{uniqueCode}",
                            name = $"Test Region {uniqueCode}",
                            description = "Parent location for hierarchy test",
                            locationType = "region"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var parentJson = JsonNode.Parse(parentResponse.GetRawText())?.AsObject();
                    var parentIdStr = parentJson?["locationId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(parentIdStr))
                    {
                        Console.WriteLine("   Failed to create parent location");
                        return false;
                    }
                    Console.WriteLine($"   Created parent location: {parentIdStr}");

                    // Step 2: Create child location (city under region)
                    Console.WriteLine("   Step 2: Creating child location (city)...");
                    var childResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/create",
                        new
                        {
                            realmId = realmIdStr,
                            code = $"CITY{uniqueCode}",
                            name = $"Test City {uniqueCode}",
                            description = "Child location for hierarchy test",
                            locationType = "city",
                            parentLocationId = parentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var childJson = JsonNode.Parse(childResponse.GetRawText())?.AsObject();
                    var childIdStr = childJson?["locationId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(childIdStr))
                    {
                        Console.WriteLine("   Failed to create child location");
                        return false;
                    }
                    var childDepth = childJson?["depth"]?.GetValue<int>() ?? -1;
                    Console.WriteLine($"   Created child location: {childIdStr} (depth: {childDepth})");

                    // Step 3: Verify child has correct depth and parent
                    if (childDepth != 1)
                    {
                        Console.WriteLine($"   FAILED: Expected depth 1 for child, got {childDepth}");
                        return false;
                    }

                    // Step 4: List children of parent
                    Console.WriteLine("   Step 4: Listing children of parent...");
                    var listResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/list-by-parent",
                        new { parentLocationId = parentIdStr },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var listJson = JsonNode.Parse(listResponse.GetRawText())?.AsObject();
                    var locationsArray = listJson?["locations"]?.AsArray();
                    var childCount = locationsArray?.Count ?? 0;

                    Console.WriteLine($"   Parent has {childCount} children");

                    if (childCount != 1)
                    {
                        Console.WriteLine($"   FAILED: Expected 1 child, got {childCount}");
                        return false;
                    }

                    // Step 5: List root locations in realm
                    Console.WriteLine("   Step 5: Listing root locations...");
                    var rootsResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/location/list-root",
                        new { realmId = realmIdStr },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var rootsJson = JsonNode.Parse(rootsResponse.GetRawText())?.AsObject();
                    var rootsArray = rootsJson?["locations"]?.AsArray();
                    var rootCount = rootsArray?.Count ?? 0;

                    Console.WriteLine($"   Realm has {rootCount} root locations");

                    // Parent should be in root locations (depth 0)
                    if (rootCount < 1)
                    {
                        Console.WriteLine($"   FAILED: Expected at least 1 root location");
                        return false;
                    }

                    Console.WriteLine("   Hierarchy test completed successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Hierarchy test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Location hierarchy test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Location hierarchy test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Location hierarchy test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}
