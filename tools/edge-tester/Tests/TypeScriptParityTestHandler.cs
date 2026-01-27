using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Test handler for verifying TypeScript SDK parity with the C# SDK.
/// Tests that both SDKs produce identical results for the same API calls.
/// </summary>
public class TypeScriptParityTestHandler : BaseWebSocketTestHandler
{
    public override ServiceTest[] GetServiceTests()
    {
        return
        [
            // Connection tests
            new ServiceTest(TestTsConnectionAndPing, "TS Parity - Connection", "Parity",
                "Test TypeScript SDK can connect to server"),

            // Realm parity tests
            new ServiceTest(TestTsRealmListParity, "TS Parity - Realm List", "Parity",
                "Verify C# and TypeScript SDKs return identical realm list"),
            new ServiceTest(TestTsRealmCreateAndReadParity, "TS Parity - Realm Create & Read", "Parity",
                "Verify create and read operations produce identical results"),
            new ServiceTest(TestTsRealmLifecycleParity, "TS Parity - Realm Lifecycle", "Parity",
                "Verify realm update/deprecate/undeprecate work identically"),

            // Species parity tests
            new ServiceTest(TestTsSpeciesListParity, "TS Parity - Species List", "Parity",
                "Verify C# and TypeScript SDKs return identical species list"),
            new ServiceTest(TestTsSpeciesCreateAndReadParity, "TS Parity - Species Create & Read", "Parity",
                "Verify species create and read produce identical results"),

            // Location parity tests
            new ServiceTest(TestTsLocationHierarchyParity, "TS Parity - Location Hierarchy", "Parity",
                "Verify location hierarchy queries work identically"),

            // Error handling parity tests
            new ServiceTest(TestTsNotFoundErrorParity, "TS Parity - 404 Not Found", "Parity",
                "Verify both SDKs handle 404 errors identically"),
            new ServiceTest(TestTsConflictErrorParity, "TS Parity - 409 Conflict", "Parity",
                "Verify both SDKs handle 409 conflict errors identically"),

            // RelationshipType parity tests
            new ServiceTest(TestTsRelationshipTypeListParity, "TS Parity - RelationshipType List", "Parity",
                "Verify relationship type list parity"),

            // Character parity tests
            new ServiceTest(TestTsCharacterCreateAndReadParity, "TS Parity - Character Create & Read", "Parity",
                "Verify character create and read produce identical results"),

            // Relationship parity tests
            new ServiceTest(TestTsRelationshipCreateAndReadParity, "TS Parity - Relationship Create & Read", "Parity",
                "Verify relationship create and read with polymorphic IDs"),

            // Typed API proxy test
            new ServiceTest(TestTsTypedInvokeParity, "TS Parity - Typed Invoke", "Parity",
                "Verify typed API invocation returns same structure"),

            // RelationshipType CRUD parity
            new ServiceTest(TestTsRelationshipTypeCreateAndReadParity, "TS Parity - RelationshipType Create & Read", "Parity",
                "Verify relationship type create and read produce identical results"),

            // Note: Auth validate test removed - /auth/validate requires JWT in HTTP header,
            // but WebSocket messages don't have HTTP headers. WebSocket clients are already
            // authenticated by virtue of being connected, so this endpoint doesn't apply.

            // Game Session parity tests
            new ServiceTest(TestTsGameSessionListParity, "TS Parity - Game Session List", "Parity",
                "Verify game session list parity"),

            // Location CRUD parity
            new ServiceTest(TestTsLocationCreateAndReadParity, "TS Parity - Location Create & Read", "Parity",
                "Verify location create and read produce identical results"),

            // Note: Permission capabilities test removed - /permission/capabilities has x-permissions: []
            // which means it's intentionally not exposed to WebSocket clients (internal only).
        ];
    }

    #region Helper Methods

    private static async Task<TypeScriptParityHelper?> ConnectTsHelper()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return null;
        }

        var tsHelper = await TypeScriptParityHelper.CreateAsync();
        var httpUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}".Replace("/auth/login", "");

        // Use admin credentials to match the C# adminClient's permissions
        var connected = await tsHelper.ConnectAsync(
            httpUrl,
            Program.Configuration.GetAdminUsername(),
            Program.Configuration.GetAdminPassword());

        if (!connected)
        {
            Console.WriteLine("❌ TypeScript SDK failed to connect");
            await tsHelper.DisposeAsync();
            return null;
        }

        return tsHelper;
    }

    private static bool CompareJsonArrayCounts(JsonObject? csharpObj, JsonObject? tsObj, string arrayName, string entityName)
    {
        var csharpCount = csharpObj?[arrayName]?.AsArray()?.Count ?? 0;
        var tsCount = tsObj?[arrayName]?.AsArray()?.Count ?? 0;

        Console.WriteLine($"   [C# SDK] Returned {csharpCount} {entityName}");
        Console.WriteLine($"   [TS SDK] Returned {tsCount} {entityName}");

        if (csharpCount != tsCount)
        {
            Console.WriteLine($"❌ Parity failure: C# returned {csharpCount}, TS returned {tsCount}");
            return false;
        }

        Console.WriteLine($"✅ Parity verified: Both SDKs returned {csharpCount} {entityName}");
        return true;
    }

    #endregion

    #region Connection Tests

    private void TestTsConnectionAndPing(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Connection Test ===");
        Console.WriteLine("Testing TypeScript SDK can connect to server...");

        RunWebSocketTest("TypeScript Connection", async _ =>
        {
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("✅ TypeScript SDK connected successfully");
            return true;
        });
    }

    #endregion

    #region Realm Parity Tests

    private void TestTsRealmListParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Realm List Parity Test ===");

        RunWebSocketTest("Realm List Parity", async adminClient =>
        {
            // C# SDK call
            Console.WriteLine("   [C# SDK] Calling /realm/list...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/list", new { }, timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpResponse.Error?.Message}");
                return false;
            }

            // TypeScript SDK call
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Calling /realm/list...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/realm/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
                return false;
            }

            var csharpRealms = ParseResponse(csharpResponse.Result);
            var tsRealms = tsResult.Result.HasValue
                ? JsonNode.Parse(tsResult.Result.Value.GetRawText())?.AsObject()
                : null;

            return CompareJsonArrayCounts(csharpRealms, tsRealms, "realms", "realms");
        });
    }

    private void TestTsRealmCreateAndReadParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Realm Create & Read Parity Test ===");

        RunWebSocketTest("Realm Create & Read Parity", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();
            var realmCode = $"PARITY_CR_{uniqueCode}";

            // Get or create game service (required for realm creation)
            var gameServiceId = await GetOrCreateTestGameServiceAsync(adminClient);
            if (string.IsNullOrEmpty(gameServiceId))
            {
                Console.WriteLine("❌ Failed to get/create game service");
                return false;
            }

            // Create via C# SDK
            Console.WriteLine($"   [C# SDK] Creating realm {realmCode}...");
            var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/create",
                new { code = realmCode, name = $"Parity Test {uniqueCode}", description = "Parity test", category = "test", gameServiceId },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK create failed: {createResponse.Error?.Message}");
                return false;
            }

            var realmId = GetStringProperty(createResponse.Result, "realmId");
            if (string.IsNullOrEmpty(realmId))
            {
                Console.WriteLine("❌ No realmId returned");
                return false;
            }
            Console.WriteLine($"   [C# SDK] Created realm: {realmId}");

            // Read via C# SDK
            var csharpReadResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/get", new { realmId }, timeout: TimeSpan.FromSeconds(10));

            if (!csharpReadResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK read failed: {csharpReadResponse.Error?.Message}");
                return false;
            }

            var csharpRealm = ParseResponse(csharpReadResponse.Result);

            // Read via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same realm...");
            var tsReadResult = await tsHelper.InvokeRawAsync("POST", "/realm/get", new { realmId });

            if (!tsReadResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK read failed: {tsReadResult.ErrorMessage}");
                return false;
            }

            var tsRealm = tsReadResult.Result.HasValue
                ? JsonNode.Parse(tsReadResult.Result.Value.GetRawText())?.AsObject()
                : null;

            // Compare key fields
            var csharpCode = GetStringProperty(csharpRealm, "code");
            var tsCode = GetStringProperty(tsRealm, "code");
            var csharpName = GetStringProperty(csharpRealm, "name");
            var tsName = GetStringProperty(tsRealm, "name");

            if (csharpCode != tsCode || csharpName != tsName)
            {
                Console.WriteLine($"❌ Parity failure: code ({csharpCode} vs {tsCode}), name ({csharpName} vs {tsName})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned identical realm data");
            return true;
        });
    }

    private void TestTsRealmLifecycleParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Realm Lifecycle Parity Test ===");

        RunWebSocketTest("Realm Lifecycle Parity", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();
            var realmCode = $"PARITY_LC_{uniqueCode}";

            // Get or create game service (required for realm creation)
            var gameServiceId = await GetOrCreateTestGameServiceAsync(adminClient);
            if (string.IsNullOrEmpty(gameServiceId))
            {
                Console.WriteLine("❌ Failed to get/create game service");
                return false;
            }

            // Create realm
            var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/create",
                new { code = realmCode, name = $"Lifecycle Test {uniqueCode}", description = "Lifecycle parity test", category = "test", gameServiceId },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"❌ Create failed: {createResponse.Error?.Message}");
                return false;
            }

            var realmId = GetStringProperty(createResponse.Result, "realmId");
            if (string.IsNullOrEmpty(realmId))
            {
                Console.WriteLine("❌ No realmId returned");
                return false;
            }
            Console.WriteLine($"   Created realm: {realmId}");

            // Update via C# SDK
            Console.WriteLine("   [C# SDK] Updating realm...");
            var updateResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/update",
                new { realmId, name = $"Updated Lifecycle {uniqueCode}" },
                timeout: TimeSpan.FromSeconds(10));

            if (!updateResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK update failed: {updateResponse.Error?.Message}");
                return false;
            }

            // Update via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Updating realm...");
            var tsUpdateResult = await tsHelper.InvokeRawAsync("POST", "/realm/update",
                new { realmId, name = $"TS Updated Lifecycle {uniqueCode}" });

            if (!tsUpdateResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK update failed: {tsUpdateResult.ErrorMessage}");
                return false;
            }

            // Read and verify both see the TS update
            var csharpReadResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/get", new { realmId }, timeout: TimeSpan.FromSeconds(10));
            var tsReadResult = await tsHelper.InvokeRawAsync("POST", "/realm/get", new { realmId });

            var csharpName = GetStringProperty(ParseResponse(csharpReadResponse.Result), "name");
            var tsRealm = tsReadResult.Result.HasValue
                ? JsonNode.Parse(tsReadResult.Result.Value.GetRawText())?.AsObject()
                : null;
            var tsName = GetStringProperty(tsRealm, "name");

            if (csharpName != tsName)
            {
                Console.WriteLine($"❌ After update: names differ (C#: {csharpName}, TS: {tsName})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Lifecycle operations work identically");
            return true;
        });
    }

    #endregion

    #region Species Parity Tests

    private void TestTsSpeciesListParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Species List Parity Test ===");

        RunWebSocketTest("Species List Parity", async adminClient =>
        {
            Console.WriteLine("   [C# SDK] Calling /species/list...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/species/list", new { }, timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpResponse.Error?.Message}");
                return false;
            }

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Calling /species/list...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/species/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
                return false;
            }

            var csharpSpecies = ParseResponse(csharpResponse.Result);
            var tsSpecies = tsResult.Result.HasValue
                ? JsonNode.Parse(tsResult.Result.Value.GetRawText())?.AsObject()
                : null;

            return CompareJsonArrayCounts(csharpSpecies, tsSpecies, "species", "species");
        });
    }

    private void TestTsSpeciesCreateAndReadParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Species Create & Read Parity Test ===");

        RunWebSocketTest("Species Create & Read Parity", async adminClient =>
        {
            // First create a realm for the species
            var uniqueCode = GenerateUniqueCode();
            var realmId = await CreateTestRealmAsync(adminClient, "SPECIES_PARITY", "Species Parity", uniqueCode);
            if (string.IsNullOrEmpty(realmId)) return false;

            // Create species via C# SDK
            var speciesCode = $"SP_PARITY_{uniqueCode}";
            Console.WriteLine($"   [C# SDK] Creating species {speciesCode}...");
            var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/species/create",
                new { code = speciesCode, name = $"Parity Species {uniqueCode}", description = "Species parity test", category = "test", realmIds = new[] { realmId } },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK create failed: {createResponse.Error?.Message}");
                return false;
            }

            var speciesId = GetStringProperty(createResponse.Result, "speciesId");
            if (string.IsNullOrEmpty(speciesId))
            {
                Console.WriteLine("❌ No speciesId returned");
                return false;
            }
            Console.WriteLine($"   [C# SDK] Created species: {speciesId}");

            // Read via both SDKs
            var csharpReadResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/species/get", new { speciesId }, timeout: TimeSpan.FromSeconds(10));

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same species...");
            var tsReadResult = await tsHelper.InvokeRawAsync("POST", "/species/get", new { speciesId });

            if (!csharpReadResponse.IsSuccess || !tsReadResult.IsSuccess)
            {
                Console.WriteLine("❌ Read failed");
                return false;
            }

            var csharpSpecies = ParseResponse(csharpReadResponse.Result);
            var tsSpecies = tsReadResult.Result.HasValue
                ? JsonNode.Parse(tsReadResult.Result.Value.GetRawText())?.AsObject()
                : null;

            var csharpCode = GetStringProperty(csharpSpecies, "code");
            var tsCode = GetStringProperty(tsSpecies, "code");

            if (csharpCode != tsCode)
            {
                Console.WriteLine($"❌ Parity failure: codes differ ({csharpCode} vs {tsCode})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned identical species data");
            return true;
        });
    }

    #endregion

    #region Location Parity Tests

    private void TestTsLocationHierarchyParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Location Hierarchy Parity Test ===");

        RunWebSocketTest("Location Hierarchy Parity", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create realm and parent location
            var realmId = await CreateTestRealmAsync(adminClient, "LOC_PARITY", "Location Parity", uniqueCode);
            if (string.IsNullOrEmpty(realmId)) return false;

            var parentLocationId = await CreateTestLocationAsync(adminClient, "PARENT", uniqueCode, realmId, "REGION");
            if (string.IsNullOrEmpty(parentLocationId)) return false;

            var childLocationId = await CreateTestLocationAsync(adminClient, "CHILD", uniqueCode, realmId, "CITY", parentLocationId);
            if (string.IsNullOrEmpty(childLocationId)) return false;

            // Query children via C# SDK
            Console.WriteLine("   [C# SDK] Querying location children...");
            var csharpChildrenResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/location/list-by-parent",
                new { parentLocationId },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpChildrenResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpChildrenResponse.Error?.Message}");
                return false;
            }

            // Query children via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Querying location children...");
            var tsChildrenResult = await tsHelper.InvokeRawAsync("POST", "/location/list-by-parent",
                new { parentLocationId });

            if (!tsChildrenResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsChildrenResult.ErrorMessage}");
                return false;
            }

            var csharpLocations = ParseResponse(csharpChildrenResponse.Result);
            var tsLocations = tsChildrenResult.Result.HasValue
                ? JsonNode.Parse(tsChildrenResult.Result.Value.GetRawText())?.AsObject()
                : null;

            return CompareJsonArrayCounts(csharpLocations, tsLocations, "locations", "child locations");
        });
    }

    #endregion

    #region Error Handling Parity Tests

    private void TestTsNotFoundErrorParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK 404 Not Found Parity Test ===");

        RunWebSocketTest("404 Not Found Parity", async adminClient =>
        {
            var fakeRealmId = Guid.NewGuid().ToString();

            // C# SDK - expect failure
            Console.WriteLine($"   [C# SDK] Requesting non-existent realm {fakeRealmId}...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/get", new { realmId = fakeRealmId }, timeout: TimeSpan.FromSeconds(10));

            if (csharpResponse.IsSuccess)
            {
                Console.WriteLine("❌ C# SDK unexpectedly succeeded");
                return false;
            }
            Console.WriteLine($"   [C# SDK] Got error: {csharpResponse.Error?.Message}");

            // TypeScript SDK - expect same failure
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine($"   [TS SDK] Requesting same non-existent realm...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/realm/get", new { realmId = fakeRealmId });

            if (tsResult.IsSuccess)
            {
                Console.WriteLine("❌ TypeScript SDK unexpectedly succeeded");
                return false;
            }
            Console.WriteLine($"   [TS SDK] Got error: {tsResult.ErrorMessage}");

            // Both should have failed with similar error codes
            var csharpCode = csharpResponse.Error?.ResponseCode ?? 0;
            var tsCode = tsResult.ErrorCode ?? 0;

            Console.WriteLine($"   Error codes: C# = {csharpCode}, TS = {tsCode}");
            Console.WriteLine($"✅ Parity verified: Both SDKs returned errors for non-existent entity");
            return true;
        });
    }

    private void TestTsConflictErrorParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK 409 Conflict Parity Test ===");

        RunWebSocketTest("409 Conflict Parity", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();
            var realmCode = $"CONFLICT_{uniqueCode}";

            // Get or create game service (required for realm creation)
            var gameServiceId = await GetOrCreateTestGameServiceAsync(adminClient);
            if (string.IsNullOrEmpty(gameServiceId))
            {
                Console.WriteLine("❌ Failed to get/create game service");
                return false;
            }

            // Create realm first time - should succeed
            Console.WriteLine($"   Creating realm {realmCode}...");
            var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/create",
                new { code = realmCode, name = $"Conflict Test {uniqueCode}", description = "Conflict test", category = "test", gameServiceId },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"❌ First create failed: {createResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   First create succeeded");

            // Try to create again via C# SDK - should fail with conflict
            Console.WriteLine("   [C# SDK] Attempting duplicate create...");
            var csharpDupeResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/create",
                new { code = realmCode, name = $"Duplicate {uniqueCode}", description = "Should fail", category = "test", gameServiceId },
                timeout: TimeSpan.FromSeconds(10));

            if (csharpDupeResponse.IsSuccess)
            {
                Console.WriteLine("❌ C# SDK duplicate create unexpectedly succeeded");
                return false;
            }
            Console.WriteLine($"   [C# SDK] Got error: {csharpDupeResponse.Error?.Message}");

            // Try to create again via TypeScript SDK - should also fail
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Attempting same duplicate create...");
            var tsDupeResult = await tsHelper.InvokeRawAsync("POST", "/realm/create",
                new { code = realmCode, name = $"TS Duplicate {uniqueCode}", description = "Should also fail", category = "test", gameServiceId });

            if (tsDupeResult.IsSuccess)
            {
                Console.WriteLine("❌ TypeScript SDK duplicate create unexpectedly succeeded");
                return false;
            }
            Console.WriteLine($"   [TS SDK] Got error: {tsDupeResult.ErrorMessage}");

            Console.WriteLine($"✅ Parity verified: Both SDKs returned conflict errors for duplicate");
            return true;
        });
    }

    #endregion

    #region RelationshipType Parity Tests

    private void TestTsRelationshipTypeListParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK RelationshipType List Parity Test ===");

        RunWebSocketTest("RelationshipType List Parity", async adminClient =>
        {
            Console.WriteLine("   [C# SDK] Calling /relationship-type/list...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/relationship-type/list", new { }, timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpResponse.Error?.Message}");
                return false;
            }

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Calling /relationship-type/list...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/relationship-type/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
                return false;
            }

            var csharpTypes = ParseResponse(csharpResponse.Result);
            var tsTypes = tsResult.Result.HasValue
                ? JsonNode.Parse(tsResult.Result.Value.GetRawText())?.AsObject()
                : null;

            return CompareJsonArrayCounts(csharpTypes, tsTypes, "relationshipTypes", "relationship types");
        });
    }

    #endregion

    #region Character Parity Tests

    private void TestTsCharacterCreateAndReadParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Character Create & Read Parity Test ===");

        RunWebSocketTest("Character Create & Read Parity", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create realm and species first (character dependencies)
            var realmId = await CreateTestRealmAsync(adminClient, "CHAR_PARITY", "Character Parity", uniqueCode);
            if (string.IsNullOrEmpty(realmId)) return false;

            var speciesId = await CreateTestSpeciesAsync(adminClient, "CHAR_PARITY", "Character Parity", uniqueCode, realmId);
            if (string.IsNullOrEmpty(speciesId)) return false;

            // Create character via C# SDK
            Console.WriteLine($"   [C# SDK] Creating character...");
            var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/character/create",
                new
                {
                    name = $"Parity Character {uniqueCode}",
                    realmId,
                    speciesId,
                    birthDate = DateTime.UtcNow.AddYears(-25).ToString("O"),
                    gender = "male"
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK create failed: {createResponse.Error?.Message}");
                return false;
            }

            var characterId = GetStringProperty(createResponse.Result, "characterId");
            if (string.IsNullOrEmpty(characterId))
            {
                Console.WriteLine("❌ No characterId returned");
                return false;
            }
            Console.WriteLine($"   [C# SDK] Created character: {characterId}");

            // Read via both SDKs
            var csharpReadResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/character/get", new { characterId }, timeout: TimeSpan.FromSeconds(10));

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same character...");
            var tsReadResult = await tsHelper.InvokeRawAsync("POST", "/character/get", new { characterId });

            if (!csharpReadResponse.IsSuccess || !tsReadResult.IsSuccess)
            {
                Console.WriteLine("❌ Read failed");
                return false;
            }

            var csharpCharacter = ParseResponse(csharpReadResponse.Result);
            var tsCharacter = tsReadResult.Result.HasValue
                ? JsonNode.Parse(tsReadResult.Result.Value.GetRawText())?.AsObject()
                : null;

            // Compare key fields
            var csharpName = GetStringProperty(csharpCharacter, "name");
            var tsName = GetStringProperty(tsCharacter, "name");
            var csharpAge = GetIntProperty(csharpCharacter, "age");
            var tsAge = tsCharacter?["age"]?.GetValue<int>() ?? 0;

            if (csharpName != tsName || csharpAge != tsAge)
            {
                Console.WriteLine($"❌ Parity failure: name ({csharpName} vs {tsName}), age ({csharpAge} vs {tsAge})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned identical character data");
            return true;
        });
    }

    #endregion

    #region Relationship Parity Tests

    private void TestTsRelationshipCreateAndReadParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Relationship Create & Read Parity Test ===");

        RunWebSocketTest("Relationship Create & Read Parity", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create a relationship type first
            Console.WriteLine("   Creating relationship type...");
            var typeResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/relationship-type/create",
                new
                {
                    code = $"PARITY_REL_{uniqueCode}",
                    name = $"Parity Relationship Type {uniqueCode}",
                    description = "For parity testing",
                    category = "test"
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!typeResponse.IsSuccess)
            {
                Console.WriteLine($"❌ Failed to create relationship type: {typeResponse.Error?.Message}");
                return false;
            }

            var relationshipTypeId = GetStringProperty(typeResponse.Result, "relationshipTypeId");
            if (string.IsNullOrEmpty(relationshipTypeId))
            {
                Console.WriteLine("❌ No relationshipTypeId returned");
                return false;
            }

            // Create relationship using GUIDs as entity IDs (polymorphic)
            var entity1Id = Guid.NewGuid().ToString();
            var entity2Id = Guid.NewGuid().ToString();

            Console.WriteLine($"   [C# SDK] Creating relationship...");
            var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/relationship/create",
                new
                {
                    relationshipTypeId,
                    entity1Id,
                    entity1Type = "character",
                    entity2Id,
                    entity2Type = "character",
                    startedAt = DateTime.UtcNow.ToString("O"),
                    metadata = new { notes = "parity test relationship" }
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK create failed: {createResponse.Error?.Message}");
                return false;
            }

            var relationshipId = GetStringProperty(createResponse.Result, "relationshipId");
            if (string.IsNullOrEmpty(relationshipId))
            {
                Console.WriteLine("❌ No relationshipId returned");
                return false;
            }
            Console.WriteLine($"   [C# SDK] Created relationship: {relationshipId}");

            // Read via both SDKs
            var csharpReadResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/relationship/get", new { relationshipId }, timeout: TimeSpan.FromSeconds(10));

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same relationship...");
            var tsReadResult = await tsHelper.InvokeRawAsync("POST", "/relationship/get", new { relationshipId });

            if (!csharpReadResponse.IsSuccess || !tsReadResult.IsSuccess)
            {
                Console.WriteLine("❌ Read failed");
                return false;
            }

            var csharpRelationship = ParseResponse(csharpReadResponse.Result);
            var tsRelationship = tsReadResult.Result.HasValue
                ? JsonNode.Parse(tsReadResult.Result.Value.GetRawText())?.AsObject()
                : null;

            // Compare entity IDs (polymorphic fields)
            var csharpEntity1 = GetStringProperty(csharpRelationship, "entity1Id");
            var tsEntity1 = GetStringProperty(tsRelationship, "entity1Id");
            var csharpEntity1Type = GetStringProperty(csharpRelationship, "entity1Type");
            var tsEntity1Type = GetStringProperty(tsRelationship, "entity1Type");

            if (csharpEntity1 != tsEntity1 || csharpEntity1Type != tsEntity1Type)
            {
                Console.WriteLine($"❌ Parity failure: entity1 ({csharpEntity1}/{csharpEntity1Type} vs {tsEntity1}/{tsEntity1Type})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned identical relationship data");
            return true;
        });
    }

    #endregion

    #region Typed API Parity Tests

    private void TestTsTypedInvokeParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Typed Invoke Parity Test ===");

        RunWebSocketTest("Typed Invoke Parity", async adminClient =>
        {
            // Use /realm/list as a simple typed endpoint
            // Both SDKs should return a response with "realms" array and "totalCount" int

            Console.WriteLine("   [C# SDK] Calling /realm/list (typed)...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/list", new { }, timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpResponse.Error?.Message}");
                return false;
            }

            var csharpResult = ParseResponse(csharpResponse.Result);

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Calling /realm/list (typed)...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/realm/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
                return false;
            }

            var tsResponse = tsResult.Result.HasValue
                ? JsonNode.Parse(tsResult.Result.Value.GetRawText())?.AsObject()
                : null;

            // Verify both have the expected structure
            var csharpHasRealms = HasArrayProperty(csharpResult, "realms");
            var tsHasRealms = tsResponse?["realms"] is JsonArray;

            var csharpTotalCount = GetIntProperty(csharpResult, "totalCount", -1);
            var tsTotalCount = tsResponse?["totalCount"]?.GetValue<int>() ?? -1;

            Console.WriteLine($"   [C# SDK] Has realms array: {csharpHasRealms}, totalCount: {csharpTotalCount}");
            Console.WriteLine($"   [TS SDK] Has realms array: {tsHasRealms}, totalCount: {tsTotalCount}");

            if (!csharpHasRealms || !tsHasRealms)
            {
                Console.WriteLine("❌ Parity failure: Missing realms array");
                return false;
            }

            if (csharpTotalCount != tsTotalCount)
            {
                Console.WriteLine($"❌ Parity failure: totalCount differs ({csharpTotalCount} vs {tsTotalCount})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned identical typed response structure");
            return true;
        });
    }

    #endregion

    #region RelationshipType CRUD Parity Tests

    private void TestTsRelationshipTypeCreateAndReadParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK RelationshipType Create & Read Parity Test ===");

        RunWebSocketTest("RelationshipType Create & Read Parity", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();
            var typeCode = $"RTYPE_PARITY_{uniqueCode}";

            // Create via C# SDK
            Console.WriteLine($"   [C# SDK] Creating relationship type {typeCode}...");
            var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/relationship-type/create",
                new
                {
                    code = typeCode,
                    name = $"Parity Type {uniqueCode}",
                    description = "Relationship type parity test",
                    category = "test"
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK create failed: {createResponse.Error?.Message}");
                return false;
            }

            var relationshipTypeId = GetStringProperty(createResponse.Result, "relationshipTypeId");
            if (string.IsNullOrEmpty(relationshipTypeId))
            {
                Console.WriteLine("❌ No relationshipTypeId returned");
                return false;
            }
            Console.WriteLine($"   [C# SDK] Created relationship type: {relationshipTypeId}");

            // Read via both SDKs
            var csharpReadResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/relationship-type/get", new { relationshipTypeId }, timeout: TimeSpan.FromSeconds(10));

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same relationship type...");
            var tsReadResult = await tsHelper.InvokeRawAsync("POST", "/relationship-type/get", new { relationshipTypeId });

            if (!csharpReadResponse.IsSuccess || !tsReadResult.IsSuccess)
            {
                Console.WriteLine("❌ Read failed");
                return false;
            }

            var csharpType = ParseResponse(csharpReadResponse.Result);
            var tsType = tsReadResult.Result.HasValue
                ? JsonNode.Parse(tsReadResult.Result.Value.GetRawText())?.AsObject()
                : null;

            // Compare key fields
            var csharpCode = GetStringProperty(csharpType, "code");
            var tsCode = GetStringProperty(tsType, "code");
            var csharpName = GetStringProperty(csharpType, "name");
            var tsName = GetStringProperty(tsType, "name");

            if (csharpCode != tsCode || csharpName != tsName)
            {
                Console.WriteLine($"❌ Parity failure: code ({csharpCode} vs {tsCode}), name ({csharpName} vs {tsName})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned identical relationship type data");
            return true;
        });
    }

    #endregion

    #region Auth Parity Tests

    private void TestTsAuthValidateParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Auth Validate Parity Test ===");

        RunWebSocketTest("Auth Validate Parity", async adminClient =>
        {
            // Both clients are already connected with valid tokens
            // Call /auth/validate to verify both can validate

            Console.WriteLine("   [C# SDK] Calling /auth/validate...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/auth/validate", new { }, timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpResponse.Error?.Message}");
                return false;
            }

            var csharpResult = ParseResponse(csharpResponse.Result);
            var csharpValid = csharpResult?["valid"]?.GetValue<bool>() ?? false;
            Console.WriteLine($"   [C# SDK] Token valid: {csharpValid}");

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Calling /auth/validate...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/auth/validate", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
                return false;
            }

            var tsResponse = tsResult.Result.HasValue
                ? JsonNode.Parse(tsResult.Result.Value.GetRawText())?.AsObject()
                : null;
            var tsValid = tsResponse?["valid"]?.GetValue<bool>() ?? false;
            Console.WriteLine($"   [TS SDK] Token valid: {tsValid}");

            if (csharpValid != tsValid)
            {
                Console.WriteLine($"❌ Parity failure: valid differs ({csharpValid} vs {tsValid})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs validate tokens identically");
            return true;
        });
    }

    #endregion

    #region Game Session Parity Tests

    private void TestTsGameSessionListParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Game Session List Parity Test ===");

        RunWebSocketTest("Game Session List Parity", async adminClient =>
        {
            Console.WriteLine("   [C# SDK] Calling /sessions/list...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/sessions/list", new { }, timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpResponse.Error?.Message}");
                return false;
            }

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Calling /sessions/list...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/sessions/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
                return false;
            }

            var csharpSessions = ParseResponse(csharpResponse.Result);
            var tsSessions = tsResult.Result.HasValue
                ? JsonNode.Parse(tsResult.Result.Value.GetRawText())?.AsObject()
                : null;

            return CompareJsonArrayCounts(csharpSessions, tsSessions, "sessions", "game sessions");
        });
    }

    #endregion

    #region Location CRUD Parity Tests

    private void TestTsLocationCreateAndReadParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Location Create & Read Parity Test ===");

        RunWebSocketTest("Location Create & Read Parity", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create realm first
            var realmId = await CreateTestRealmAsync(adminClient, "LOC_CR_PARITY", "Location CR Parity", uniqueCode);
            if (string.IsNullOrEmpty(realmId)) return false;

            // Create location via C# SDK
            var locationCode = $"LOC_PARITY_{uniqueCode}";
            Console.WriteLine($"   [C# SDK] Creating location {locationCode}...");
            var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/location/create",
                new
                {
                    code = locationCode,
                    name = $"Parity Location {uniqueCode}",
                    description = "Location parity test",
                    realmId,
                    locationType = "REGION"
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK create failed: {createResponse.Error?.Message}");
                return false;
            }

            var locationId = GetStringProperty(createResponse.Result, "locationId");
            if (string.IsNullOrEmpty(locationId))
            {
                Console.WriteLine("❌ No locationId returned");
                return false;
            }
            Console.WriteLine($"   [C# SDK] Created location: {locationId}");

            // Read via both SDKs
            var csharpReadResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/location/get", new { locationId }, timeout: TimeSpan.FromSeconds(10));

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same location...");
            var tsReadResult = await tsHelper.InvokeRawAsync("POST", "/location/get", new { locationId });

            if (!csharpReadResponse.IsSuccess || !tsReadResult.IsSuccess)
            {
                Console.WriteLine("❌ Read failed");
                return false;
            }

            var csharpLocation = ParseResponse(csharpReadResponse.Result);
            var tsLocation = tsReadResult.Result.HasValue
                ? JsonNode.Parse(tsReadResult.Result.Value.GetRawText())?.AsObject()
                : null;

            // Compare key fields
            var csharpCode = GetStringProperty(csharpLocation, "code");
            var tsCode = GetStringProperty(tsLocation, "code");
            var csharpName = GetStringProperty(csharpLocation, "name");
            var tsName = GetStringProperty(tsLocation, "name");

            if (csharpCode != tsCode || csharpName != tsName)
            {
                Console.WriteLine($"❌ Parity failure: code ({csharpCode} vs {tsCode}), name ({csharpName} vs {tsName})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned identical location data");
            return true;
        });
    }

    #endregion

    #region Permission Parity Tests

    private void TestTsPermissionCapabilitiesParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Permission Capabilities Parity Test ===");

        RunWebSocketTest("Permission Capabilities Parity", async adminClient =>
        {
            Console.WriteLine("   [C# SDK] Calling /permission/capabilities...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/permission/capabilities", new { }, timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpResponse.Error?.Message}");
                return false;
            }

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Calling /permission/capabilities...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/permission/capabilities", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
                return false;
            }

            var csharpCapabilities = ParseResponse(csharpResponse.Result);
            var tsCapabilities = tsResult.Result.HasValue
                ? JsonNode.Parse(tsResult.Result.Value.GetRawText())?.AsObject()
                : null;

            // Both should have capabilities array - compare counts
            return CompareJsonArrayCounts(csharpCapabilities, tsCapabilities, "capabilities", "capabilities");
        });
    }

    #endregion
}
