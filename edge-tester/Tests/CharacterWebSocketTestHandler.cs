using System.Text.Json;
using System.Text.Json.Nodes;
using BeyondImmersion.Bannou.Client;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for character service API endpoints.
/// Tests the character service APIs through the Connect service WebSocket binary protocol.
///
/// Note: Character create/update/delete APIs require admin role,
/// so these tests use Program.AdminClient which is already connected with admin permissions.
/// </summary>
public class CharacterWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestListCharactersViaWebSocket, "Character - List (WebSocket)", "WebSocket",
                "Test character listing via WebSocket binary protocol"),
            new ServiceTest(TestCreateAndGetCharacterViaWebSocket, "Character - Create and Get (WebSocket)", "WebSocket",
                "Test character creation and retrieval via WebSocket binary protocol"),
            new ServiceTest(TestCharacterLifecycleViaWebSocket, "Character - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete character lifecycle via WebSocket: create -> update -> delete"),
        };
    }

    #region Helper Methods

    /// <summary>
    /// Creates a test realm for character tests using the shared admin client.
    /// Characters must belong to a valid realm that exists in the database.
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
                    code = $"CHAR_REALM_{uniqueCode}",
                    name = $"Character Test Realm {uniqueCode}",
                    description = "Test realm for character tests",
                    category = "test"
                },
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            var responseJson = JsonNode.Parse(response.GetRawText())?.AsObject();
            var realmIdStr = responseJson?["realmId"]?.GetValue<string>();
            if (string.IsNullOrEmpty(realmIdStr))
            {
                Console.WriteLine("   Failed to create test realm - no realmId in response");
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

    /// <summary>
    /// Creates a test species for character tests using the shared admin client.
    /// Characters must belong to a valid species that exists in the database AND
    /// the species must be associated with the target realm.
    /// </summary>
    private async Task<string?> CreateTestSpeciesAsync(BannouClient adminClient, string uniqueCode, string realmId)
    {
        try
        {
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/species/create",
                new
                {
                    code = $"CHAR_SPECIES_{uniqueCode}",
                    name = $"Character Test Species {uniqueCode}",
                    description = "Test species for character tests",
                    category = "test",
                    realmIds = new[] { realmId }  // Associate species with the realm
                },
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            var responseJson = JsonNode.Parse(response.GetRawText())?.AsObject();
            var speciesIdStr = responseJson?["speciesId"]?.GetValue<string>();
            if (string.IsNullOrEmpty(speciesIdStr))
            {
                Console.WriteLine("   Failed to create test species - no speciesId in response");
                return null;
            }

            Console.WriteLine($"   Created test species: {speciesIdStr}");
            return speciesIdStr;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test species: {ex.Message}");
            return null;
        }
    }

    #endregion

    private void TestListCharactersViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Character List Test (WebSocket) ===");
        Console.WriteLine("Testing /character/list via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected");
                    return false;
                }

                var uniqueCode = $"{DateTime.Now.Ticks % 100000}";

                // Create a test realm (realmId is required for listing)
                var realmIdStr = await CreateTestRealmAsync(adminClient, uniqueCode);
                if (realmIdStr == null)
                {
                    Console.WriteLine("❌ Failed to create test realm");
                    return false;
                }

                Console.WriteLine($"   Created test realm: {realmIdStr}");

                // Now list characters for this realm
                var response = (await adminClient.InvokeAsync<object, JsonElement>(
                    "POST",
                    "/character/list",
                    new { realmId = realmIdStr },
                    timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                var json = JsonNode.Parse(response.GetRawText())?.AsObject();
                var hasCharactersArray = json?["characters"] != null &&
                                        json["characters"] is JsonArray;
                var totalCount = json?["totalCount"]?.GetValue<int>() ?? 0;

                Console.WriteLine($"   Characters array present: {hasCharactersArray}");
                Console.WriteLine($"   Total Count: {totalCount}");

                return hasCharactersArray;
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Character list test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Character list test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Character list test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCreateAndGetCharacterViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Character Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /character/create and /character/get via shared admin WebSocket...");

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

                var uniqueCode = $"{DateTime.Now.Ticks % 100000}";

                // First create a test realm (required for character creation)
                var realmIdStr = await CreateTestRealmAsync(adminClient, uniqueCode);
                if (realmIdStr == null)
                {
                    return false;
                }

                // Create a test species associated with the realm (required for character creation)
                var speciesIdStr = await CreateTestSpeciesAsync(adminClient, uniqueCode, realmIdStr);
                if (speciesIdStr == null)
                {
                    return false;
                }

                var uniqueName = $"TestChar{uniqueCode}";

                try
                {
                    // Create character with valid realm and species
                    Console.WriteLine("   Invoking /character/create...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/character/create",
                        new
                        {
                            name = uniqueName,
                            realmId = realmIdStr,
                            speciesId = speciesIdStr,
                            birthDate = DateTimeOffset.UtcNow,
                            status = "alive"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var characterIdStr = createJson?["characterId"]?.GetValue<string>();
                    var name = createJson?["name"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(characterIdStr))
                    {
                        Console.WriteLine("   Failed to create character - no characterId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created character: {characterIdStr} ({name})");

                    // Now retrieve it
                    Console.WriteLine("   Invoking /character/get...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/character/get",
                        new { characterId = characterIdStr },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var retrievedId = getJson?["characterId"]?.GetValue<string>();
                    var retrievedName = getJson?["name"]?.GetValue<string>();

                    Console.WriteLine($"   Retrieved character: {retrievedId} ({retrievedName})");

                    return retrievedId == characterIdStr && retrievedName == uniqueName;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Character create and get test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Character create and get test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Character create and get test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCharacterLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Character Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete character lifecycle via shared admin WebSocket...");

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
                    var uniqueCode = $"{DateTime.Now.Ticks % 100000}";

                    // First create a test realm (required for character creation)
                    var realmIdStr = await CreateTestRealmAsync(adminClient, uniqueCode);
                    if (realmIdStr == null)
                    {
                        return false;
                    }

                    // Create a test species associated with the realm (required for character creation)
                    var speciesIdStr = await CreateTestSpeciesAsync(adminClient, uniqueCode, realmIdStr);
                    if (speciesIdStr == null)
                    {
                        return false;
                    }

                    // Step 1: Create character with valid realm and species
                    Console.WriteLine("   Step 1: Creating character...");
                    var uniqueName = $"LifecycleChar{uniqueCode}";

                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/character/create",
                        new
                        {
                            name = uniqueName,
                            realmId = realmIdStr,
                            speciesId = speciesIdStr,
                            birthDate = DateTimeOffset.UtcNow,
                            status = "alive"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var characterIdStr = createJson?["characterId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(characterIdStr))
                    {
                        Console.WriteLine("   Failed to create character - no characterId in response");
                        return false;
                    }
                    Console.WriteLine($"   Created character {characterIdStr}");

                    // Step 2: Update character name
                    Console.WriteLine("   Step 2: Updating character name...");
                    var updateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/character/update",
                        new
                        {
                            characterId = characterIdStr,
                            name = $"Updated {uniqueName}"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var updateJson = JsonNode.Parse(updateResponse.GetRawText())?.AsObject();
                    var updatedName = updateJson?["name"]?.GetValue<string>();
                    if (!updatedName?.StartsWith("Updated") ?? true)
                    {
                        Console.WriteLine($"   Failed to update character - name: {updatedName}");
                        return false;
                    }
                    Console.WriteLine($"   Updated character name to: {updatedName}");

                    // Step 3: Update character status to dead
                    Console.WriteLine("   Step 3: Setting character status to dead...");
                    var deathResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/character/update",
                        new
                        {
                            characterId = characterIdStr,
                            status = "dead",
                            deathDate = DateTimeOffset.UtcNow
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var deathJson = JsonNode.Parse(deathResponse.GetRawText())?.AsObject();
                    var statusStr = deathJson?["status"]?.GetValue<string>();
                    if (!string.Equals(statusStr, "dead", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"   Failed to set character status to dead - status: {statusStr}");
                        return false;
                    }
                    Console.WriteLine($"   Character status set to: {statusStr}");

                    // Step 4: Delete character
                    Console.WriteLine("   Step 4: Deleting character...");
                    (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/character/delete",
                        new { characterId = characterIdStr },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    Console.WriteLine($"   Character deleted successfully");

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
                Console.WriteLine("✅ Character complete lifecycle test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Character complete lifecycle test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Character lifecycle test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Performs a character API call via the shared admin WebSocket.
    /// Uses Program.AdminClient which is already connected with admin permissions.
    /// </summary>
    private async Task<bool> PerformCharacterApiTest(
        string method,
        string path,
        object? body,
        Func<JsonObject?, bool> validateResponse)
    {
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
            Console.WriteLine("   Character APIs require admin role for create/update/delete operations.");
            return false;
        }

        Console.WriteLine($"   Sending character API request via shared admin WebSocket:");
        Console.WriteLine($"   Method: {method}");
        Console.WriteLine($"   Path: {path}");

        try
        {
            var requestBody = body ?? new { };
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                method,
                path,
                requestBody,
                timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

            var responseJson = response.GetRawText();
            Console.WriteLine($"   Received response: {responseJson.Substring(0, Math.Min(500, responseJson.Length))}...");

            var responseObj = JsonNode.Parse(responseJson)?.AsObject();
            return validateResponse(responseObj);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine($"❌ Endpoint not available: {method} {path}");
            Console.WriteLine($"   Admin may not have access to character APIs");
            Console.WriteLine($"   Available APIs: {string.Join(", ", adminClient.AvailableApis.Keys.Take(10))}...");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Character API test failed: {ex.Message}");
            return false;
        }
    }
}
