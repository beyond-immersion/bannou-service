using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for realm service API endpoints.
/// Tests the realm service APIs through the Connect service WebSocket binary protocol.
///
/// Note: Realm create/update/deprecate/undeprecate APIs require admin role,
/// so these tests use Program.AdminClient which is already connected with admin permissions.
/// </summary>
public class RealmWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestListRealmsViaWebSocket, "Realm - List (WebSocket)", "WebSocket",
                "Test realm listing via WebSocket binary protocol"),
            new ServiceTest(TestCreateAndGetRealmViaWebSocket, "Realm - Create and Get (WebSocket)", "WebSocket",
                "Test realm creation and retrieval via WebSocket binary protocol"),
            new ServiceTest(TestRealmLifecycleViaWebSocket, "Realm - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete realm lifecycle via WebSocket: create -> update -> deprecate -> undeprecate"),
        };
    }

    private void TestListRealmsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Realm List Test (WebSocket) ===");
        Console.WriteLine("Testing /realm/list via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () => await PerformRealmApiTest(
                "POST",
                "/realm/list",
                new { },
                response =>
                {
                    var hasRealmsArray = response?["realms"] != null &&
                                        response["realms"] is JsonArray;
                    var totalCount = response?["totalCount"]?.GetValue<int>() ?? 0;

                    Console.WriteLine($"   Realms array present: {hasRealmsArray}");
                    Console.WriteLine($"   Total Count: {totalCount}");

                    return hasRealmsArray;
                })).Result;

            if (result)
            {
                Console.WriteLine("✅ Realm list test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Realm list test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Realm list test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCreateAndGetRealmViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Realm Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /realm/create and /realm/get via shared admin WebSocket...");

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

                var uniqueCode = $"REALM{DateTime.Now.Ticks % 100000}";

                try
                {
                    // Create realm
                    Console.WriteLine("   Invoking /realm/create...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/realm/create",
                        new
                        {
                            code = uniqueCode,
                            name = $"Test Realm {uniqueCode}",
                            description = "Created via WebSocket edge test",
                            category = "test"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var realmIdStr = createJson?["realmId"]?.GetValue<string>();
                    var code = createJson?["code"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(realmIdStr))
                    {
                        Console.WriteLine("   Failed to create realm - no realmId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created realm: {realmIdStr} ({code})");

                    // Now retrieve it
                    Console.WriteLine("   Invoking /realm/get...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/realm/get",
                        new { realmId = realmIdStr },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var retrievedId = getJson?["realmId"]?.GetValue<string>();
                    var retrievedCode = getJson?["code"]?.GetValue<string>();

                    Console.WriteLine($"   Retrieved realm: {retrievedId} ({retrievedCode})");

                    return retrievedId == realmIdStr && retrievedCode == uniqueCode;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Realm create and get test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Realm create and get test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Realm create and get test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRealmLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Realm Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete realm lifecycle via shared admin WebSocket...");

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
                    // Step 1: Create realm
                    Console.WriteLine("   Step 1: Creating realm...");
                    var uniqueCode = $"LIFE{DateTime.Now.Ticks % 100000}";

                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/realm/create",
                        new
                        {
                            code = uniqueCode,
                            name = $"Lifecycle Test {uniqueCode}",
                            description = "Lifecycle test realm",
                            category = "test"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var realmIdStr = createJson?["realmId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(realmIdStr))
                    {
                        Console.WriteLine("   Failed to create realm - no realmId in response");
                        return false;
                    }
                    Console.WriteLine($"   Created realm {realmIdStr}");

                    // Step 2: Update realm
                    Console.WriteLine("   Step 2: Updating realm...");
                    var updateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/realm/update",
                        new
                        {
                            realmId = realmIdStr,
                            name = $"Updated Lifecycle Test {uniqueCode}",
                            description = "Updated description"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var updateJson = JsonNode.Parse(updateResponse.GetRawText())?.AsObject();
                    var updatedName = updateJson?["name"]?.GetValue<string>();
                    if (!updatedName?.StartsWith("Updated") ?? true)
                    {
                        Console.WriteLine($"   Failed to update realm - name: {updatedName}");
                        return false;
                    }
                    Console.WriteLine($"   Updated realm name to: {updatedName}");

                    // Step 3: Deprecate realm
                    Console.WriteLine("   Step 3: Deprecating realm...");
                    var deprecateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/realm/deprecate",
                        new
                        {
                            realmId = realmIdStr,
                            reason = "WebSocket lifecycle test"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var deprecateJson = JsonNode.Parse(deprecateResponse.GetRawText())?.AsObject();
                    var isDeprecated = deprecateJson?["isDeprecated"]?.GetValue<bool>() ?? false;
                    if (!isDeprecated)
                    {
                        Console.WriteLine("   Failed to deprecate realm");
                        return false;
                    }
                    Console.WriteLine($"   Realm deprecated successfully");

                    // Step 4: Undeprecate realm
                    Console.WriteLine("   Step 4: Undeprecating realm...");
                    var undeprecateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/realm/undeprecate",
                        new { realmId = realmIdStr },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var undeprecateJson = JsonNode.Parse(undeprecateResponse.GetRawText())?.AsObject();
                    var isUndeprecated = !(undeprecateJson?["isDeprecated"]?.GetValue<bool>() ?? true);
                    if (!isUndeprecated)
                    {
                        Console.WriteLine("   Failed to undeprecate realm");
                        return false;
                    }
                    Console.WriteLine($"   Realm restored successfully");

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
                Console.WriteLine("✅ Realm complete lifecycle test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Realm complete lifecycle test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Realm lifecycle test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Performs a realm API call via the shared admin WebSocket.
    /// Uses Program.AdminClient which is already connected with admin permissions.
    /// </summary>
    private async Task<bool> PerformRealmApiTest(
        string method,
        string path,
        object? body,
        Func<JsonObject?, bool> validateResponse)
    {
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
            Console.WriteLine("   Realm APIs require admin role for create/update/delete operations.");
            return false;
        }

        Console.WriteLine($"📤 Sending realm API request via shared admin WebSocket:");
        Console.WriteLine($"   Method: {method}");
        Console.WriteLine($"   Path: {path}");

        try
        {
            var requestBody = body ?? new { };
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                method,
                path,
                requestBody,
                timeout: TimeSpan.FromSeconds(30))).GetResultOrThrow();

            var responseJson = response.GetRawText();
            Console.WriteLine($"📥 Received response: {responseJson.Substring(0, Math.Min(500, responseJson.Length))}...");

            var responseObj = JsonNode.Parse(responseJson)?.AsObject();
            return validateResponse(responseObj);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine($"❌ Endpoint not available: {method} {path}");
            Console.WriteLine($"   Admin may not have access to realm APIs");
            Console.WriteLine($"   Available APIs: {string.Join(", ", adminClient.AvailableApis.Keys.Take(10))}...");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Realm API test failed: {ex.Message}");
            return false;
        }
    }
}
