using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for species service API endpoints.
/// Tests the species service APIs through the Connect service WebSocket binary protocol.
///
/// Note: Species create/update/deprecate/undeprecate APIs require admin role,
/// so these tests use Program.AdminClient which is already connected with admin permissions.
/// </summary>
public class SpeciesWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestListSpeciesViaWebSocket, "Species - List (WebSocket)", "WebSocket",
                "Test species listing via WebSocket binary protocol"),
            new ServiceTest(TestCreateAndGetSpeciesViaWebSocket, "Species - Create and Get (WebSocket)", "WebSocket",
                "Test species creation and retrieval via WebSocket binary protocol"),
            new ServiceTest(TestSpeciesLifecycleViaWebSocket, "Species - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete species lifecycle via WebSocket: create -> update -> deprecate -> undeprecate"),
        };
    }

    private void TestListSpeciesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species List Test (WebSocket) ===");
        Console.WriteLine("Testing /species/list via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () => await PerformSpeciesApiTest(
                "POST",
                "/species/list",
                new { },
                response =>
                {
                    var hasSpeciesArray = response?["species"] != null &&
                                        response["species"] is JsonArray;
                    var totalCount = response?["totalCount"]?.GetValue<int>() ?? 0;

                    Console.WriteLine($"   Species array present: {hasSpeciesArray}");
                    Console.WriteLine($"   Total Count: {totalCount}");

                    return hasSpeciesArray;
                })).Result;

            if (result)
            {
                Console.WriteLine("✅ Species list test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Species list test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Species list test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCreateAndGetSpeciesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /species/create and /species/get via shared admin WebSocket...");

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

                var uniqueCode = $"TEST{DateTime.Now.Ticks % 100000}";

                try
                {
                    // Create species
                    Console.WriteLine("   Invoking /species/create...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/species/create",
                        new
                        {
                            code = uniqueCode,
                            name = $"Test Species {uniqueCode}",
                            description = "Created via WebSocket edge test",
                            isPlayable = true,
                            baseLifespan = 100,
                            maturityAge = 18
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var speciesIdStr = createJson?["speciesId"]?.GetValue<string>();
                    var code = createJson?["code"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(speciesIdStr))
                    {
                        Console.WriteLine("   Failed to create species - no speciesId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created species: {speciesIdStr} ({code})");

                    // Now retrieve it
                    Console.WriteLine("   Invoking /species/get...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/species/get",
                        new { speciesId = speciesIdStr },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var retrievedId = getJson?["speciesId"]?.GetValue<string>();
                    var retrievedCode = getJson?["code"]?.GetValue<string>();

                    Console.WriteLine($"   Retrieved species: {retrievedId} ({retrievedCode})");

                    return retrievedId == speciesIdStr && retrievedCode == uniqueCode;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Species create and get test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Species create and get test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Species create and get test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestSpeciesLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete species lifecycle via shared admin WebSocket...");

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
                    // Step 1: Create species
                    Console.WriteLine("   Step 1: Creating species...");
                    var uniqueCode = $"LIFE{DateTime.Now.Ticks % 100000}";

                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/species/create",
                        new
                        {
                            code = uniqueCode,
                            name = $"Lifecycle Test {uniqueCode}",
                            description = "Lifecycle test species",
                            isPlayable = false
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var speciesIdStr = createJson?["speciesId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(speciesIdStr))
                    {
                        Console.WriteLine("   Failed to create species - no speciesId in response");
                        return false;
                    }
                    Console.WriteLine($"   Created species {speciesIdStr}");

                    // Step 2: Update species
                    Console.WriteLine("   Step 2: Updating species...");
                    var updateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/species/update",
                        new
                        {
                            speciesId = speciesIdStr,
                            name = $"Updated Lifecycle Test {uniqueCode}",
                            description = "Updated description"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var updateJson = JsonNode.Parse(updateResponse.GetRawText())?.AsObject();
                    var updatedName = updateJson?["name"]?.GetValue<string>();
                    if (!updatedName?.StartsWith("Updated") ?? true)
                    {
                        Console.WriteLine($"   Failed to update species - name: {updatedName}");
                        return false;
                    }
                    Console.WriteLine($"   Updated species name to: {updatedName}");

                    // Step 3: Deprecate species
                    Console.WriteLine("   Step 3: Deprecating species...");
                    var deprecateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/species/deprecate",
                        new
                        {
                            speciesId = speciesIdStr,
                            reason = "WebSocket lifecycle test"
                        },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var deprecateJson = JsonNode.Parse(deprecateResponse.GetRawText())?.AsObject();
                    var isDeprecated = deprecateJson?["isDeprecated"]?.GetValue<bool>() ?? false;
                    if (!isDeprecated)
                    {
                        Console.WriteLine("   Failed to deprecate species");
                        return false;
                    }
                    Console.WriteLine($"   Species deprecated successfully");

                    // Step 4: Undeprecate species
                    Console.WriteLine("   Step 4: Undeprecating species...");
                    var undeprecateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/species/undeprecate",
                        new { speciesId = speciesIdStr },
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var undeprecateJson = JsonNode.Parse(undeprecateResponse.GetRawText())?.AsObject();
                    var isUndeprecated = !(undeprecateJson?["isDeprecated"]?.GetValue<bool>() ?? true);
                    if (!isUndeprecated)
                    {
                        Console.WriteLine("   Failed to undeprecate species");
                        return false;
                    }
                    Console.WriteLine($"   Species restored successfully");

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
                Console.WriteLine("✅ Species complete lifecycle test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Species complete lifecycle test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Species lifecycle test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Performs a species API call via the shared admin WebSocket.
    /// Uses Program.AdminClient which is already connected with admin permissions.
    /// </summary>
    private async Task<bool> PerformSpeciesApiTest(
        string method,
        string path,
        object? body,
        Func<JsonObject?, bool> validateResponse)
    {
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
            Console.WriteLine("   Species APIs require admin role for create/update/delete operations.");
            return false;
        }

        Console.WriteLine($"📤 Sending species API request via shared admin WebSocket:");
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
            Console.WriteLine($"   Admin may not have access to species APIs");
            Console.WriteLine($"   Available APIs: {string.Join(", ", adminClient.AvailableApis.Keys.Take(10))}...");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Species API test failed: {ex.Message}");
            return false;
        }
    }
}
