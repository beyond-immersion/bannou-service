using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Species;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Test handler for verifying TypeScript SDK parity with the C# SDK.
/// Tests that both SDKs succeed or fail as expected for the same API calls.
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
                "Verify C# and TypeScript SDKs both succeed for realm list"),
            new ServiceTest(TestTsRealmCreateAndReadParity, "TS Parity - Realm Create & Read", "Parity",
                "Verify create and read operations succeed for both SDKs"),
            new ServiceTest(TestTsRealmLifecycleParity, "TS Parity - Realm Lifecycle", "Parity",
                "Verify realm update/deprecate/undeprecate work for both SDKs"),

            // Species parity tests
            new ServiceTest(TestTsSpeciesListParity, "TS Parity - Species List", "Parity",
                "Verify C# and TypeScript SDKs both succeed for species list"),
            new ServiceTest(TestTsSpeciesCreateAndReadParity, "TS Parity - Species Create & Read", "Parity",
                "Verify species create and read succeed for both SDKs"),

            // Location parity tests
            new ServiceTest(TestTsLocationHierarchyParity, "TS Parity - Location Hierarchy", "Parity",
                "Verify location hierarchy queries work for both SDKs"),

            // Error handling parity tests
            new ServiceTest(TestTsNotFoundErrorParity, "TS Parity - 404 Not Found", "Parity",
                "Verify both SDKs return errors for non-existent entities"),
            new ServiceTest(TestTsConflictErrorParity, "TS Parity - 409 Conflict", "Parity",
                "Verify both SDKs return errors for conflict situations"),

            // RelationshipType parity tests
            new ServiceTest(TestTsRelationshipTypeListParity, "TS Parity - RelationshipType List", "Parity",
                "Verify relationship type list works for both SDKs"),

            // Character parity tests
            new ServiceTest(TestTsCharacterCreateAndReadParity, "TS Parity - Character Create & Read", "Parity",
                "Verify character create and read succeed for both SDKs"),

            // Relationship parity tests
            new ServiceTest(TestTsRelationshipCreateAndReadParity, "TS Parity - Relationship Create & Read", "Parity",
                "Verify relationship create and read with polymorphic IDs"),

            // Typed API proxy test
            new ServiceTest(TestTsTypedInvokeParity, "TS Parity - Typed Invoke", "Parity",
                "Verify typed API invocation succeeds for both SDKs"),

            // RelationshipType CRUD parity
            new ServiceTest(TestTsRelationshipTypeCreateAndReadParity, "TS Parity - RelationshipType Create & Read", "Parity",
                "Verify relationship type create and read succeed for both SDKs"),

            // Note: Auth validate test removed - /auth/validate requires JWT in HTTP header,
            // but WebSocket messages don't have HTTP headers. WebSocket clients are already
            // authenticated by virtue of being connected, so this endpoint doesn't apply.

            // Game Session parity tests
            new ServiceTest(TestTsGameSessionListParity, "TS Parity - Game Session List", "Parity",
                "Verify game session list works for both SDKs"),

            // Location CRUD parity
            new ServiceTest(TestTsLocationCreateAndReadParity, "TS Parity - Location Create & Read", "Parity",
                "Verify location create and read succeed for both SDKs"),

            // Note: Permission capabilities test removed - /permission/capabilities has x-permissions: []
            // which means it's intentionally not exposed to WebSocket clients (internal only).
        ];
    }

    #region Helper Methods

    private static async Task<TypeScriptParityHelper?> ConnectTsHelper()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
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
            Console.WriteLine("   TypeScript SDK failed to connect");
            await tsHelper.DisposeAsync();
            return null;
        }

        return tsHelper;
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

            Console.WriteLine("   [TS SDK] Connected successfully");
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
            // C# SDK call using typed proxy
            var csharpResponse = await adminClient.Realm.ListRealmsAsync(
                new ListRealmsRequest(), timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess || csharpResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Failed: {csharpResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Call succeeded");

            // TypeScript SDK call
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            var tsResult = await tsHelper.InvokeRawAsync("/realm/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Failed: {tsResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Call succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
            return true;
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
            if (!gameServiceId.HasValue)
            {
                Console.WriteLine("   Failed to get/create game service");
                return false;
            }

            // Create via C# SDK using typed proxy
            Console.WriteLine($"   [C# SDK] Creating realm {realmCode}...");
            var createResponse = await adminClient.Realm.CreateRealmAsync(
                new CreateRealmRequest
                {
                    Code = realmCode,
                    Name = $"Parity Test {uniqueCode}",
                    Description = "Parity test",
                    Category = "test",
                    GameServiceId = gameServiceId.Value
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Create failed: {createResponse.Error?.Message}");
                return false;
            }

            var realmId = createResponse.Result.RealmId;
            Console.WriteLine($"   [C# SDK] Created realm: {realmId}");

            // Read via C# SDK using typed proxy
            var csharpReadResponse = await adminClient.Realm.GetRealmAsync(
                new GetRealmRequest { RealmId = realmId },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpReadResponse.IsSuccess || csharpReadResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Read failed: {csharpReadResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Read succeeded");

            // Read via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same realm...");
            var tsReadResult = await tsHelper.InvokeRawAsync("/realm/get", new { realmId });

            if (!tsReadResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Read failed: {tsReadResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Read succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
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
            if (!gameServiceId.HasValue)
            {
                Console.WriteLine("   Failed to get/create game service");
                return false;
            }

            // Create realm using typed proxy
            var createResponse = await adminClient.Realm.CreateRealmAsync(
                new CreateRealmRequest
                {
                    Code = realmCode,
                    Name = $"Lifecycle Test {uniqueCode}",
                    Description = "Lifecycle parity test",
                    Category = "test",
                    GameServiceId = gameServiceId.Value
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result is null)
            {
                Console.WriteLine($"   Create failed: {createResponse.Error?.Message}");
                return false;
            }

            var realmId = createResponse.Result.RealmId;
            Console.WriteLine($"   Created realm: {realmId}");

            // Update via C# SDK using typed proxy
            Console.WriteLine("   [C# SDK] Updating realm...");
            var updateResponse = await adminClient.Realm.UpdateRealmAsync(
                new UpdateRealmRequest
                {
                    RealmId = realmId,
                    Name = $"Updated Lifecycle {uniqueCode}"
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!updateResponse.IsSuccess || updateResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Update failed: {updateResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Update succeeded");

            // Update via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Updating realm...");
            var tsUpdateResult = await tsHelper.InvokeRawAsync("/realm/update",
                new { realmId, name = $"TS Updated Lifecycle {uniqueCode}" });

            if (!tsUpdateResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Update failed: {tsUpdateResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Update succeeded");

            // Verify both can read the updated realm using typed proxy
            var csharpReadResponse = await adminClient.Realm.GetRealmAsync(
                new GetRealmRequest { RealmId = realmId },
                timeout: TimeSpan.FromSeconds(10));
            var tsReadResult = await tsHelper.InvokeRawAsync("/realm/get", new { realmId });

            if (!csharpReadResponse.IsSuccess || csharpReadResponse.Result is null || !tsReadResult.IsSuccess)
            {
                Console.WriteLine("   Read after update failed");
                return false;
            }

            Console.WriteLine("   Parity verified: Lifecycle operations work for both SDKs");
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
            // C# SDK call using typed proxy
            var csharpResponse = await adminClient.Species.ListSpeciesAsync(
                new ListSpeciesRequest(), timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess || csharpResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Failed: {csharpResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Call succeeded");

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            var tsResult = await tsHelper.InvokeRawAsync("/species/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Failed: {tsResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Call succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
            return true;
        });
    }

    private void TestTsSpeciesCreateAndReadParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Species Create & Read Parity Test ===");

        RunWebSocketTest("Species Create & Read Parity", async adminClient =>
        {
            // First create a realm for the species
            var uniqueCode = GenerateUniqueCode();
            var realm = await CreateTestRealmAsync(adminClient, "SPECIES_PARITY", "Species Parity", uniqueCode);
            if (realm == null) return false;

            // Create species via C# SDK using typed proxy
            var speciesCode = $"SP_PARITY_{uniqueCode}";
            Console.WriteLine($"   [C# SDK] Creating species {speciesCode}...");
            var createResponse = await adminClient.Species.CreateSpeciesAsync(
                new CreateSpeciesRequest
                {
                    Code = speciesCode,
                    Name = $"Parity Species {uniqueCode}",
                    Description = "Species parity test",
                    Category = "test",
                    RealmIds = new List<Guid> { realm.RealmId }
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Create failed: {createResponse.Error?.Message}");
                return false;
            }

            var speciesId = createResponse.Result.SpeciesId;
            Console.WriteLine($"   [C# SDK] Created species: {speciesId}");

            // Read via C# SDK using typed proxy
            var csharpReadResponse = await adminClient.Species.GetSpeciesAsync(
                new GetSpeciesRequest { SpeciesId = speciesId },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpReadResponse.IsSuccess || csharpReadResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Read failed: {csharpReadResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Read succeeded");

            // Read via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same species...");
            var tsReadResult = await tsHelper.InvokeRawAsync("/species/get", new { speciesId });

            if (!tsReadResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Read failed: {tsReadResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Read succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
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
            var realm = await CreateTestRealmAsync(adminClient, "LOC_PARITY", "Location Parity", uniqueCode);
            if (realm == null) return false;

            var parentLocation = await CreateTestLocationAsync(adminClient, "PARENT", uniqueCode, realm.RealmId, LocationType.REGION);
            if (parentLocation == null) return false;

            var childLocation = await CreateTestLocationAsync(adminClient, "CHILD", uniqueCode, realm.RealmId, LocationType.CITY, parentLocation.LocationId);
            if (childLocation == null) return false;

            // Query children via C# SDK using typed proxy
            Console.WriteLine("   [C# SDK] Querying location children...");
            var csharpChildrenResponse = await adminClient.Location.ListLocationsByParentAsync(
                new ListLocationsByParentRequest { ParentLocationId = parentLocation.LocationId },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpChildrenResponse.IsSuccess || csharpChildrenResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Failed: {csharpChildrenResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Call succeeded");

            // Query children via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Querying location children...");
            var tsChildrenResult = await tsHelper.InvokeRawAsync("/location/list-by-parent",
                new { parentLocationId = parentLocation.LocationId });

            if (!tsChildrenResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Failed: {tsChildrenResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Call succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
            return true;
        });
    }

    #endregion

    #region Error Handling Parity Tests

    private void TestTsNotFoundErrorParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK 404 Not Found Parity Test ===");

        RunWebSocketTest("404 Not Found Parity", async adminClient =>
        {
            var fakeRealmId = Guid.NewGuid();

            // C# SDK - expect failure using typed proxy
            Console.WriteLine($"   [C# SDK] Requesting non-existent realm...");
            var csharpResponse = await adminClient.Realm.GetRealmAsync(
                new GetRealmRequest { RealmId = fakeRealmId },
                timeout: TimeSpan.FromSeconds(10));

            if (csharpResponse.IsSuccess)
            {
                Console.WriteLine("   [C# SDK] Unexpectedly succeeded");
                return false;
            }
            Console.WriteLine("   [C# SDK] Returned error as expected");

            // TypeScript SDK - expect same failure
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Requesting same non-existent realm...");
            var tsResult = await tsHelper.InvokeRawAsync("/realm/get", new { realmId = fakeRealmId });

            if (tsResult.IsSuccess)
            {
                Console.WriteLine("   [TS SDK] Unexpectedly succeeded");
                return false;
            }
            Console.WriteLine("   [TS SDK] Returned error as expected");

            Console.WriteLine("   Parity verified: Both SDKs returned errors for non-existent entity");
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
            if (!gameServiceId.HasValue)
            {
                Console.WriteLine("   Failed to get/create game service");
                return false;
            }

            // Create realm first time using typed proxy - should succeed
            Console.WriteLine($"   Creating realm {realmCode}...");
            var createResponse = await adminClient.Realm.CreateRealmAsync(
                new CreateRealmRequest
                {
                    Code = realmCode,
                    Name = $"Conflict Test {uniqueCode}",
                    Description = "Conflict test",
                    Category = "test",
                    GameServiceId = gameServiceId.Value
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result is null)
            {
                Console.WriteLine($"   First create failed: {createResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   First create succeeded");

            // Try to create again via C# SDK using typed proxy - should fail with conflict
            Console.WriteLine("   [C# SDK] Attempting duplicate create...");
            var csharpDupeResponse = await adminClient.Realm.CreateRealmAsync(
                new CreateRealmRequest
                {
                    Code = realmCode,
                    Name = $"Duplicate {uniqueCode}",
                    Description = "Should fail",
                    Category = "test",
                    GameServiceId = gameServiceId.Value
                },
                timeout: TimeSpan.FromSeconds(10));

            if (csharpDupeResponse.IsSuccess)
            {
                Console.WriteLine("   [C# SDK] Duplicate create unexpectedly succeeded");
                return false;
            }
            Console.WriteLine("   [C# SDK] Returned error as expected");

            // Try to create again via TypeScript SDK - should also fail
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Attempting same duplicate create...");
            var tsDupeResult = await tsHelper.InvokeRawAsync("/realm/create",
                new { code = realmCode, name = $"TS Duplicate {uniqueCode}", description = "Should also fail", category = "test", gameServiceId = gameServiceId.Value });

            if (tsDupeResult.IsSuccess)
            {
                Console.WriteLine("   [TS SDK] Duplicate create unexpectedly succeeded");
                return false;
            }
            Console.WriteLine("   [TS SDK] Returned error as expected");

            Console.WriteLine("   Parity verified: Both SDKs returned conflict errors for duplicate");
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
            // C# SDK call using typed proxy
            var csharpResponse = await adminClient.Relationship.ListRelationshipTypesAsync(
                new ListRelationshipTypesRequest(), timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess || csharpResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Failed: {csharpResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Call succeeded");

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            var tsResult = await tsHelper.InvokeRawAsync("/relationship-type/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Failed: {tsResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Call succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
            return true;
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
            var realm = await CreateTestRealmAsync(adminClient, "CHAR_PARITY", "Character Parity", uniqueCode);
            if (realm == null) return false;

            var species = await CreateTestSpeciesAsync(adminClient, "CHAR_PARITY", "Character Parity", uniqueCode, realm.RealmId);
            if (species == null) return false;

            // Create character via C# SDK using typed proxy
            Console.WriteLine("   [C# SDK] Creating character...");
            var createResponse = await adminClient.Character.CreateCharacterAsync(
                new CreateCharacterRequest
                {
                    Name = $"Parity Character {uniqueCode}",
                    RealmId = realm.RealmId,
                    SpeciesId = species.SpeciesId,
                    BirthDate = DateTimeOffset.UtcNow.AddYears(-25)
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Create failed: {createResponse.Error?.Message}");
                return false;
            }

            var characterId = createResponse.Result.CharacterId;
            Console.WriteLine($"   [C# SDK] Created character: {characterId}");

            // Read via C# SDK using typed proxy
            var csharpReadResponse = await adminClient.Character.GetCharacterAsync(
                new GetCharacterRequest { CharacterId = characterId },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpReadResponse.IsSuccess || csharpReadResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Read failed: {csharpReadResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Read succeeded");

            // Read via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same character...");
            var tsReadResult = await tsHelper.InvokeRawAsync("/character/get", new { characterId });

            if (!tsReadResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Read failed: {tsReadResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Read succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
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

            // Create a relationship type first using typed proxy
            Console.WriteLine("   Creating relationship type...");
            var typeResponse = await adminClient.Relationship.CreateRelationshipTypeAsync(
                new CreateRelationshipTypeRequest
                {
                    Code = $"PARITY_REL_{uniqueCode}",
                    Name = $"Parity Relationship Type {uniqueCode}",
                    Description = "For parity testing",
                    Category = "test"
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!typeResponse.IsSuccess || typeResponse.Result is null)
            {
                Console.WriteLine($"   Failed to create relationship type: {typeResponse.Error?.Message}");
                return false;
            }

            var relationshipTypeId = typeResponse.Result.RelationshipTypeId;

            // Create relationship using GUIDs as entity IDs (polymorphic)
            var entity1Id = Guid.NewGuid();
            var entity2Id = Guid.NewGuid();

            Console.WriteLine("   [C# SDK] Creating relationship...");
            var createResponse = await adminClient.Relationship.CreateRelationshipAsync(
                new CreateRelationshipRequest
                {
                    RelationshipTypeId = relationshipTypeId,
                    Entity1Id = entity1Id,
                    Entity1Type = BeyondImmersion.BannouService.EntityType.Character,
                    Entity2Id = entity2Id,
                    Entity2Type = BeyondImmersion.BannouService.EntityType.Character,
                    StartedAt = DateTimeOffset.UtcNow,
                    Metadata = new { notes = "parity test relationship" }
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Create failed: {createResponse.Error?.Message}");
                return false;
            }

            var relationshipId = createResponse.Result.RelationshipId;
            Console.WriteLine($"   [C# SDK] Created relationship: {relationshipId}");

            // Read via C# SDK using typed proxy
            var csharpReadResponse = await adminClient.Relationship.GetRelationshipAsync(
                new GetRelationshipRequest { RelationshipId = relationshipId },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpReadResponse.IsSuccess || csharpReadResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Read failed: {csharpReadResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Read succeeded");

            // Read via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same relationship...");
            var tsReadResult = await tsHelper.InvokeRawAsync("/relationship/get", new { relationshipId });

            if (!tsReadResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Read failed: {tsReadResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Read succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
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
            // Use /realm/list as a simple typed endpoint with typed proxy
            var csharpResponse = await adminClient.Realm.ListRealmsAsync(
                new ListRealmsRequest(), timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess || csharpResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Failed: {csharpResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Call succeeded");

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            var tsResult = await tsHelper.InvokeRawAsync("/realm/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Failed: {tsResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Call succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
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

            // Create via C# SDK using typed proxy
            Console.WriteLine($"   [C# SDK] Creating relationship type {typeCode}...");
            var createResponse = await adminClient.Relationship.CreateRelationshipTypeAsync(
                new CreateRelationshipTypeRequest
                {
                    Code = typeCode,
                    Name = $"Parity Type {uniqueCode}",
                    Description = "Relationship type parity test",
                    Category = "test"
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Create failed: {createResponse.Error?.Message}");
                return false;
            }

            var relationshipTypeId = createResponse.Result.RelationshipTypeId;
            Console.WriteLine($"   [C# SDK] Created relationship type: {relationshipTypeId}");

            // Read via C# SDK using typed proxy
            var csharpReadResponse = await adminClient.Relationship.GetRelationshipTypeAsync(
                new GetRelationshipTypeRequest { RelationshipTypeId = relationshipTypeId },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpReadResponse.IsSuccess || csharpReadResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Read failed: {csharpReadResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Read succeeded");

            // Read via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same relationship type...");
            var tsReadResult = await tsHelper.InvokeRawAsync("/relationship-type/get", new { relationshipTypeId });

            if (!tsReadResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Read failed: {tsReadResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Read succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
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
            // C# SDK call using typed proxy
            var csharpResponse = await adminClient.GameSession.ListGameSessionsAsync(
                new ListGameSessionsRequest(), timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess || csharpResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Failed: {csharpResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Call succeeded");

            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            var tsResult = await tsHelper.InvokeRawAsync("/sessions/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Failed: {tsResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Call succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
            return true;
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
            var realm = await CreateTestRealmAsync(adminClient, "LOC_CR_PARITY", "Location CR Parity", uniqueCode);
            if (realm == null) return false;

            // Create location via C# SDK using typed proxy
            var locationCode = $"LOC_PARITY_{uniqueCode}";
            Console.WriteLine($"   [C# SDK] Creating location {locationCode}...");
            var createResponse = await adminClient.Location.CreateLocationAsync(
                new CreateLocationRequest
                {
                    Code = locationCode,
                    Name = $"Parity Location {uniqueCode}",
                    Description = "Location parity test",
                    RealmId = realm.RealmId,
                    LocationType = LocationType.REGION
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Create failed: {createResponse.Error?.Message}");
                return false;
            }

            var locationId = createResponse.Result.LocationId;
            Console.WriteLine($"   [C# SDK] Created location: {locationId}");

            // Read via C# SDK using typed proxy
            var csharpReadResponse = await adminClient.Location.GetLocationAsync(
                new GetLocationRequest { LocationId = locationId },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpReadResponse.IsSuccess || csharpReadResponse.Result is null)
            {
                Console.WriteLine($"   [C# SDK] Read failed: {csharpReadResponse.Error?.Message}");
                return false;
            }
            Console.WriteLine("   [C# SDK] Read succeeded");

            // Read via TypeScript SDK
            await using var tsHelper = await ConnectTsHelper();
            if (tsHelper == null) return false;

            Console.WriteLine("   [TS SDK] Reading same location...");
            var tsReadResult = await tsHelper.InvokeRawAsync("/location/get", new { locationId });

            if (!tsReadResult.IsSuccess)
            {
                Console.WriteLine($"   [TS SDK] Read failed: {tsReadResult.ErrorMessage}");
                return false;
            }
            Console.WriteLine("   [TS SDK] Read succeeded");

            Console.WriteLine("   Parity verified: Both SDKs succeeded");
            return true;
        });
    }

    #endregion
}
