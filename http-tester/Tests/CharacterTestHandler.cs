using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for character API endpoints using generated clients.
/// Tests the character service APIs directly via NSwag-generated CharacterClient.
///
/// Note: Character APIs test service-to-service communication via mesh.
/// These tests validate full CRUD operations with real datastores.
/// Characters require valid Realm and Species - these are created as dependencies.
/// </summary>
public class CharacterTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Character CRUD operations
        new ServiceTest(TestCreateCharacter, "CreateCharacter", "Character", "Test character creation endpoint"),
        new ServiceTest(TestGetCharacter, "GetCharacter", "Character", "Test character retrieval endpoint"),
        new ServiceTest(TestUpdateCharacter, "UpdateCharacter", "Character", "Test character update endpoint"),
        new ServiceTest(TestDeleteCharacter, "DeleteCharacter", "Character", "Test character deletion endpoint"),

        // Character listing operations
        new ServiceTest(TestListCharacters, "ListCharacters", "Character", "Test character listing with filters"),
        new ServiceTest(TestGetCharactersByRealm, "GetCharactersByRealm", "Character", "Test realm-based character query"),

        // Error handling tests
        new ServiceTest(TestGetNonExistentCharacter, "GetNonExistentCharacter", "Character", "Test 404 for non-existent character"),

        // Complete lifecycle test
        new ServiceTest(TestCompleteCharacterLifecycle, "CompleteCharacterLifecycle", "Character", "Test complete character lifecycle: create → update → delete"),
    ];

    /// <summary>
    /// Helper to create a test realm for character tests.
    /// </summary>
    private static async Task<RealmResponse> CreateTestRealmAsync(string suffix)
    {
        var realmClient = GetServiceClient<IRealmClient>();
        return await realmClient.CreateRealmAsync(new CreateRealmRequest
        {
            Code = $"CHAR_TEST_{DateTime.Now.Ticks}_{suffix}",
            Name = $"Character Test Realm {suffix}",
            Category = "TEST"
        });
    }

    /// <summary>
    /// Helper to create a test species and add it to a realm for character tests.
    /// </summary>
    private static async Task<SpeciesResponse> CreateTestSpeciesAsync(Guid realmId, string suffix)
    {
        var speciesClient = GetServiceClient<ISpeciesClient>();

        // Create the species
        var species = await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
        {
            Code = $"CHAR_SPECIES_{DateTime.Now.Ticks}_{suffix}",
            Name = $"Character Test Species {suffix}",
            Description = "Test species for character tests"
        });

        // Add species to the realm
        await speciesClient.AddSpeciesToRealmAsync(new AddSpeciesToRealmRequest
        {
            SpeciesId = species.SpeciesId,
            RealmId = realmId
        });

        return species;
    }

    private static async Task<TestResult> TestCreateCharacter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("CREATE");
            var species = await CreateTestSpeciesAsync(realm.RealmId, "CREATE");
            var characterClient = GetServiceClient<ICharacterClient>();

            var createRequest = new CreateCharacterRequest
            {
                Name = $"TestCharacter_{DateTime.Now.Ticks}",
                RealmId = realm.RealmId,
                SpeciesId = species.SpeciesId,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-25),
                Status = CharacterStatus.Alive
            };

            var response = await characterClient.CreateCharacterAsync(createRequest);

            if (response.CharacterId == Guid.Empty)
                return TestResult.Failed("Character creation returned empty character ID");

            if (response.Name != createRequest.Name)
                return TestResult.Failed($"Character name mismatch: expected '{createRequest.Name}', got '{response.Name}'");

            if (response.RealmId != createRequest.RealmId)
                return TestResult.Failed($"Realm ID mismatch: expected '{createRequest.RealmId}', got '{response.RealmId}'");

            return TestResult.Successful($"Character created successfully: ID={response.CharacterId}, Name={response.Name}");
        }, "Create character");

    private static async Task<TestResult> TestGetCharacter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("GET");
            var species = await CreateTestSpeciesAsync(realm.RealmId, "GET");
            var characterClient = GetServiceClient<ICharacterClient>();

            var createRequest = new CreateCharacterRequest
            {
                Name = $"GetTest_{DateTime.Now.Ticks}",
                RealmId = realm.RealmId,
                SpeciesId = species.SpeciesId,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-30),
                Status = CharacterStatus.Alive
            };

            var createResponse = await characterClient.CreateCharacterAsync(createRequest);
            if (createResponse.CharacterId == Guid.Empty)
                return TestResult.Failed("Failed to create test character for retrieval test");

            var response = await characterClient.GetCharacterAsync(new GetCharacterRequest { CharacterId = createResponse.CharacterId });

            if (response.CharacterId != createResponse.CharacterId)
                return TestResult.Failed($"Character ID mismatch: expected '{createResponse.CharacterId}', got '{response.CharacterId}'");

            if (response.Name != createRequest.Name)
                return TestResult.Failed($"Character name mismatch: expected '{createRequest.Name}', got '{response.Name}'");

            return TestResult.Successful($"Character retrieved successfully: ID={response.CharacterId}, Name={response.Name}");
        }, "Get character");

    private static async Task<TestResult> TestUpdateCharacter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("UPDATE");
            var species = await CreateTestSpeciesAsync(realm.RealmId, "UPDATE");
            var characterClient = GetServiceClient<ICharacterClient>();

            var createRequest = new CreateCharacterRequest
            {
                Name = $"UpdateTest_{DateTime.Now.Ticks}",
                RealmId = realm.RealmId,
                SpeciesId = species.SpeciesId,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-20),
                Status = CharacterStatus.Alive
            };

            var createResponse = await characterClient.CreateCharacterAsync(createRequest);

            var newName = $"UpdatedName_{DateTime.Now.Ticks}";
            var updateRequest = new UpdateCharacterRequest
            {
                CharacterId = createResponse.CharacterId,
                Name = newName,
                Status = CharacterStatus.Dormant
            };

            var response = await characterClient.UpdateCharacterAsync(updateRequest);

            if (response.Name != newName)
                return TestResult.Failed($"Updated name mismatch: expected '{newName}', got '{response.Name}'");

            if (response.Status != CharacterStatus.Dormant)
                return TestResult.Failed($"Updated status mismatch: expected 'Dormant', got '{response.Status}'");

            return TestResult.Successful($"Character updated successfully: ID={response.CharacterId}, Name={response.Name}, Status={response.Status}");
        }, "Update character");

    private static async Task<TestResult> TestDeleteCharacter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("DELETE");
            var species = await CreateTestSpeciesAsync(realm.RealmId, "DELETE");
            var characterClient = GetServiceClient<ICharacterClient>();

            var createRequest = new CreateCharacterRequest
            {
                Name = $"DeleteTest_{DateTime.Now.Ticks}",
                RealmId = realm.RealmId,
                SpeciesId = species.SpeciesId,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-18),
                Status = CharacterStatus.Alive
            };

            var createResponse = await characterClient.CreateCharacterAsync(createRequest);

            await characterClient.DeleteCharacterAsync(new DeleteCharacterRequest { CharacterId = createResponse.CharacterId });

            // Verify the character is deleted
            try
            {
                await characterClient.GetCharacterAsync(new GetCharacterRequest { CharacterId = createResponse.CharacterId });
                return TestResult.Failed("Character still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful($"Character deleted successfully: ID={createResponse.CharacterId}");
        }, "Delete character");

    private static async Task<TestResult> TestListCharacters(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("LIST");
            var species = await CreateTestSpeciesAsync(realm.RealmId, "LIST");
            var characterClient = GetServiceClient<ICharacterClient>();

            // Create a few test characters
            for (var i = 0; i < 3; i++)
            {
                var createRequest = new CreateCharacterRequest
                {
                    Name = $"ListTest_{DateTime.Now.Ticks}_{i}",
                    RealmId = realm.RealmId,
                    SpeciesId = species.SpeciesId,
                    BirthDate = DateTimeOffset.UtcNow.AddYears(-25 - i),
                    Status = CharacterStatus.Alive
                };
                await characterClient.CreateCharacterAsync(createRequest);
            }

            var response = await characterClient.ListCharactersAsync(new ListCharactersRequest
            {
                RealmId = realm.RealmId,
                Status = CharacterStatus.Alive,
                Page = 1,
                PageSize = 10
            });

            if (response.Characters == null)
                return TestResult.Failed("List response returned null characters array");

            if (response.Characters.Count < 3)
                return TestResult.Failed($"Expected at least 3 characters, got {response.Characters.Count}");

            return TestResult.Successful($"Listed {response.Characters.Count} characters (Total: {response.TotalCount}, Page: {response.Page}, HasNext: {response.HasNextPage})");
        }, "List characters");

    private static async Task<TestResult> TestGetCharactersByRealm(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("REALM");
            var species = await CreateTestSpeciesAsync(realm.RealmId, "REALM");
            var characterClient = GetServiceClient<ICharacterClient>();

            // Create test characters
            for (var i = 0; i < 3; i++)
            {
                var createRequest = new CreateCharacterRequest
                {
                    Name = $"RealmTest_{DateTime.Now.Ticks}_{i}",
                    RealmId = realm.RealmId,
                    SpeciesId = species.SpeciesId,
                    BirthDate = DateTimeOffset.UtcNow.AddYears(-20 - i),
                    Status = CharacterStatus.Alive
                };
                await characterClient.CreateCharacterAsync(createRequest);
            }

            var response = await characterClient.GetCharactersByRealmAsync(new GetCharactersByRealmRequest
            {
                RealmId = realm.RealmId,
                Page = 1,
                PageSize = 10
            });

            if (response.Characters == null)
                return TestResult.Failed("Realm query returned null characters array");

            if (response.Characters.Count < 3)
                return TestResult.Failed($"Expected at least 3 characters in realm, got {response.Characters.Count}");

            // Verify all returned characters are in the correct realm
            foreach (var character in response.Characters)
            {
                if (character.RealmId != realm.RealmId)
                    return TestResult.Failed($"Character {character.CharacterId} has wrong realm: expected '{realm.RealmId}', got '{character.RealmId}'");
            }

            return TestResult.Successful($"Retrieved {response.Characters.Count} characters from realm {realm.RealmId}");
        }, "Get characters by realm");

    private static async Task<TestResult> TestGetNonExistentCharacter(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var characterClient = GetServiceClient<ICharacterClient>();
                await characterClient.GetCharacterAsync(new GetCharacterRequest { CharacterId = Guid.NewGuid() });
            },
            404,
            "Get non-existent character");

    private static async Task<TestResult> TestCompleteCharacterLifecycle(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("LIFECYCLE");
            var species = await CreateTestSpeciesAsync(realm.RealmId, "LIFECYCLE");
            var characterClient = GetServiceClient<ICharacterClient>();

            // Step 1: Create character
            var createRequest = new CreateCharacterRequest
            {
                Name = $"LifecycleTest_{DateTime.Now.Ticks}",
                RealmId = realm.RealmId,
                SpeciesId = species.SpeciesId,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-25),
                Status = CharacterStatus.Alive
            };

            var createResponse = await characterClient.CreateCharacterAsync(createRequest);
            Console.WriteLine($"  Step 1: Created character {createResponse.CharacterId}");

            // Step 2: Verify retrieval
            var getResponse = await characterClient.GetCharacterAsync(new GetCharacterRequest { CharacterId = createResponse.CharacterId });
            Console.WriteLine($"  Step 2: Retrieved character {getResponse.Name}");

            // Step 3: Update character
            var updateResponse = await characterClient.UpdateCharacterAsync(new UpdateCharacterRequest
            {
                CharacterId = createResponse.CharacterId,
                Name = $"Updated_{createResponse.Name}",
                Status = CharacterStatus.Dormant
            });
            Console.WriteLine($"  Step 3: Updated character to {updateResponse.Name} (Status: {updateResponse.Status})");

            // Step 4: Delete character
            await characterClient.DeleteCharacterAsync(new DeleteCharacterRequest { CharacterId = createResponse.CharacterId });
            Console.WriteLine("  Step 4: Deleted character");

            // Step 5: Verify deletion
            try
            {
                await characterClient.GetCharacterAsync(new GetCharacterRequest { CharacterId = createResponse.CharacterId });
                return TestResult.Failed("Character still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                Console.WriteLine("  Step 5: Verified character deletion (404)");
            }

            return TestResult.Successful($"Complete character lifecycle test passed for character {createResponse.CharacterId}");
        }, "Complete character lifecycle");
}
