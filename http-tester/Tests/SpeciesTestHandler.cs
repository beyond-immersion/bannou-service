using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for species API endpoints using generated clients.
/// Tests the species service APIs directly via NSwag-generated SpeciesClient.
///
/// Note: Species APIs test service-to-service communication via mesh.
/// These tests validate realm-associated species management with real datastores.
/// Species-realm associations require real Realms to exist first.
/// </summary>
public class SpeciesTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests()
    {
        return
        [
            // CRUD operations
            new ServiceTest(TestCreateSpecies, "CreateSpecies", "Species", "Test species creation"),
            new ServiceTest(TestGetSpecies, "GetSpecies", "Species", "Test species retrieval by ID"),
            new ServiceTest(TestGetSpeciesByCode, "GetSpeciesByCode", "Species", "Test species retrieval by code"),
            new ServiceTest(TestUpdateSpecies, "UpdateSpecies", "Species", "Test species update"),
            new ServiceTest(TestDeleteSpecies, "DeleteSpecies", "Species", "Test species deletion"),
            new ServiceTest(TestListSpecies, "ListSpecies", "Species", "Test listing all species"),

            // Realm associations
            new ServiceTest(TestAddSpeciesToRealm, "AddSpeciesToRealm", "Species", "Test adding species to realm"),
            new ServiceTest(TestRemoveSpeciesFromRealm, "RemoveSpeciesFromRealm", "Species", "Test removing species from realm"),
            new ServiceTest(TestListSpeciesByRealm, "ListSpeciesByRealm", "Species", "Test listing species by realm"),

            // Error handling
            new ServiceTest(TestGetNonExistentSpecies, "GetNonExistentSpecies", "Species", "Test 404 for non-existent species"),
            new ServiceTest(TestDuplicateCodeConflict, "Species_DuplicateCodeConflict", "Species", "Test 409 for duplicate code"),

            // Seed operation
            new ServiceTest(TestSeedSpecies, "SeedSpecies", "Species", "Test seeding species"),

            // Complete lifecycle
            new ServiceTest(TestCompleteSpeciesLifecycle, "CompleteSpeciesLifecycle", "Species", "Test complete species lifecycle with realm associations"),
        ];
    }

    /// <summary>
    /// Helper to create a test realm for species tests.
    /// </summary>
    private static async Task<RealmResponse> CreateTestRealmAsync(string suffix)
    {
        var realmClient = GetServiceClient<IRealmClient>();
        return await realmClient.CreateRealmAsync(new CreateRealmRequest
        {
            Code = $"SPECIES_TEST_{DateTime.Now.Ticks}_{suffix}",
            Name = $"Species Test Realm {suffix}",
            Category = "TEST"
        });
    }

    private static Task<TestResult> TestCreateSpecies(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var speciesClient = GetServiceClient<ISpeciesClient>();

            var createRequest = new CreateSpeciesRequest
            {
                Code = GenerateTestId("TEST_SPECIES"),
                Name = "Test Species",
                Description = "A test species for HTTP testing"
            };

            var response = await speciesClient.CreateSpeciesAsync(createRequest);

            if (response.SpeciesId == Guid.Empty)
                return TestResult.Failed("Create returned empty ID");

            if (response.Code != createRequest.Code.ToUpperInvariant())
                return TestResult.Failed($"Code mismatch: expected '{createRequest.Code.ToUpperInvariant()}', got '{response.Code}'");

            return TestResult.Successful($"Created species: ID={response.SpeciesId}, Code={response.Code}");
        }, "Create species");

    private static Task<TestResult> TestGetSpecies(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var speciesClient = GetServiceClient<ISpeciesClient>();

            // Create a species first
            var createRequest = new CreateSpeciesRequest
            {
                Code = GenerateTestId("GET_SPECIES"),
                Name = "Get Test Species"
            };
            var created = await speciesClient.CreateSpeciesAsync(createRequest);

            // Now retrieve it
            var response = await speciesClient.GetSpeciesAsync(new GetSpeciesRequest { SpeciesId = created.SpeciesId });

            if (response.SpeciesId != created.SpeciesId)
                return TestResult.Failed("ID mismatch");

            if (response.Name != createRequest.Name)
                return TestResult.Failed($"Name mismatch: expected '{createRequest.Name}', got '{response.Name}'");

            return TestResult.Successful($"Retrieved species: ID={response.SpeciesId}, Code={response.Code}");
        }, "Get species");

    private static Task<TestResult> TestGetSpeciesByCode(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var speciesClient = GetServiceClient<ISpeciesClient>();

            var code = GenerateTestId("CODE_SPECIES");
            var createRequest = new CreateSpeciesRequest
            {
                Code = code,
                Name = "Code Lookup Species"
            };
            var created = await speciesClient.CreateSpeciesAsync(createRequest);

            var response = await speciesClient.GetSpeciesByCodeAsync(new GetSpeciesByCodeRequest { Code = code });

            if (response.SpeciesId != created.SpeciesId)
                return TestResult.Failed("ID mismatch when fetching by code");

            return TestResult.Successful($"Retrieved species by code: ID={response.SpeciesId}");
        }, "Get species by code");

    private static Task<TestResult> TestUpdateSpecies(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var speciesClient = GetServiceClient<ISpeciesClient>();

            var createRequest = new CreateSpeciesRequest
            {
                Code = GenerateTestId("UPDATE_SPECIES"),
                Name = "Original Species Name",
                Description = "Original description"
            };
            var created = await speciesClient.CreateSpeciesAsync(createRequest);

            var updateRequest = new UpdateSpeciesRequest
            {
                SpeciesId = created.SpeciesId,
                Name = "Updated Species Name",
                Description = "Updated description"
            };
            var response = await speciesClient.UpdateSpeciesAsync(updateRequest);

            if (response.Name != "Updated Species Name")
                return TestResult.Failed($"Name not updated: expected 'Updated Species Name', got '{response.Name}'");

            if (response.Description != "Updated description")
                return TestResult.Failed("Description not updated");

            return TestResult.Successful($"Updated species: ID={response.SpeciesId}, Name={response.Name}");
        }, "Update species");

    private static Task<TestResult> TestDeleteSpecies(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var speciesClient = GetServiceClient<ISpeciesClient>();

            var createRequest = new CreateSpeciesRequest
            {
                Code = GenerateTestId("DELETE_SPECIES"),
                Name = "Delete Test Species"
            };
            var created = await speciesClient.CreateSpeciesAsync(createRequest);

            await speciesClient.DeleteSpeciesAsync(new DeleteSpeciesRequest { SpeciesId = created.SpeciesId });

            // Verify deletion - expect 404
            try
            {
                await speciesClient.GetSpeciesAsync(new GetSpeciesRequest { SpeciesId = created.SpeciesId });
                return TestResult.Failed("Species still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful($"Deleted species: ID={created.SpeciesId}");
        }, "Delete species");

    private static Task<TestResult> TestListSpecies(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var speciesClient = GetServiceClient<ISpeciesClient>();

            // Create some species
            for (int i = 0; i < 3; i++)
            {
                await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
                {
                    Code = $"{GenerateTestId("LIST_SPECIES")}_{i}",
                    Name = $"List Test Species {i}"
                });
            }

            var response = await speciesClient.ListSpeciesAsync(new ListSpeciesRequest());

            if (response.Species == null || response.Species.Count < 3)
                return TestResult.Failed($"Expected at least 3 species, got {response.Species?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Species.Count} species");
        }, "List species");

    private static Task<TestResult> TestAddSpeciesToRealm(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("ADD");
            var speciesClient = GetServiceClient<ISpeciesClient>();

            var species = await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = GenerateTestId("ADD_REALM_SPECIES"),
                Name = "Add Realm Species"
            });

            var response = await speciesClient.AddSpeciesToRealmAsync(new AddSpeciesToRealmRequest
            {
                SpeciesId = species.SpeciesId,
                RealmId = realm.RealmId
            });

            if (response.RealmIds == null || !response.RealmIds.Contains(realm.RealmId))
                return TestResult.Failed("Realm ID not added to species");

            return TestResult.Successful($"Added species to realm: SpeciesID={species.SpeciesId}, RealmID={realm.RealmId}");
        }, "Add species to realm");

    private static Task<TestResult> TestRemoveSpeciesFromRealm(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realm1 = await CreateTestRealmAsync("REMOVE1");
            var realm2 = await CreateTestRealmAsync("REMOVE2");
            var speciesClient = GetServiceClient<ISpeciesClient>();

            var species = await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = GenerateTestId("REMOVE_REALM_SPECIES"),
                Name = "Remove Realm Species",
                RealmIds = [realm1.RealmId, realm2.RealmId]
            });

            var response = await speciesClient.RemoveSpeciesFromRealmAsync(new RemoveSpeciesFromRealmRequest
            {
                SpeciesId = species.SpeciesId,
                RealmId = realm1.RealmId
            });

            if (response.RealmIds != null && response.RealmIds.Contains(realm1.RealmId))
                return TestResult.Failed("Realm ID still present after removal");

            if (response.RealmIds == null || !response.RealmIds.Contains(realm2.RealmId))
                return TestResult.Failed("Other realm ID should still be present");

            return TestResult.Successful($"Removed species from realm: SpeciesID={species.SpeciesId}");
        }, "Remove species from realm");

    private static Task<TestResult> TestListSpeciesByRealm(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("LIST");
            var speciesClient = GetServiceClient<ISpeciesClient>();

            for (int i = 0; i < 3; i++)
            {
                await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
                {
                    Code = $"{GenerateTestId("REALM_LIST_SPECIES")}_{i}",
                    Name = $"Realm List Species {i}",
                    RealmIds = [realm.RealmId]
                });
            }

            var response = await speciesClient.ListSpeciesByRealmAsync(new ListSpeciesByRealmRequest
            {
                RealmId = realm.RealmId
            });

            if (response.Species == null || response.Species.Count < 3)
                return TestResult.Failed($"Expected at least 3 species in realm, got {response.Species?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Species.Count} species in realm {realm.RealmId}");
        }, "List species by realm");

    private static Task<TestResult> TestGetNonExistentSpecies(ITestClient client, string[] args) =>
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var speciesClient = GetServiceClient<ISpeciesClient>();
                await speciesClient.GetSpeciesAsync(new GetSpeciesRequest { SpeciesId = Guid.NewGuid() });
            },
            404,
            "Get non-existent species");

    private static Task<TestResult> TestDuplicateCodeConflict(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var speciesClient = GetServiceClient<ISpeciesClient>();
            var code = GenerateTestId("DUPLICATE_SPECIES");

            // Create first species
            await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = code,
                Name = "First Species"
            });

            // Try to create second with same code - expect 409
            return await ExecuteExpectingStatusAsync(
                async () =>
                {
                    await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
                    {
                        Code = code,
                        Name = "Second Species"
                    });
                },
                409,
                "Duplicate code");
        }, "Duplicate code conflict");

    private static Task<TestResult> TestSeedSpecies(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var speciesClient = GetServiceClient<ISpeciesClient>();

            var seedRequest = new SeedSpeciesRequest
            {
                Species =
                [
                    new SeedSpecies
                    {
                        Code = GenerateTestId("SEED_HUMAN"),
                        Name = "Human",
                        Description = "Standard humanoid species"
                    },
                    new SeedSpecies
                    {
                        Code = GenerateTestId("SEED_ELF"),
                        Name = "Elf",
                        Description = "Long-lived magical species"
                    }
                ],
                UpdateExisting = false
            };

            var response = await speciesClient.SeedSpeciesAsync(seedRequest);

            if (response.Created < 2)
                return TestResult.Failed($"Expected 2 created, got {response.Created}");

            return TestResult.Successful($"Seed completed: Created={response.Created}, Updated={response.Updated}, Skipped={response.Skipped}");
        }, "Seed species");

    private static Task<TestResult> TestCompleteSpeciesLifecycle(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realm = await CreateTestRealmAsync("LIFECYCLE");
            var speciesClient = GetServiceClient<ISpeciesClient>();
            var testId = GenerateTestId("LIFECYCLE_SPECIES");

            // Step 1: Create species
            Console.WriteLine("  Step 1: Creating species...");
            var species = await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = testId,
                Name = "Lifecycle Species",
                Description = "Species for lifecycle testing"
            });

            // Step 2: Update species
            Console.WriteLine("  Step 2: Updating species...");
            species = await speciesClient.UpdateSpeciesAsync(new UpdateSpeciesRequest
            {
                SpeciesId = species.SpeciesId,
                Name = "Updated Lifecycle Species"
            });

            if (species.Name != "Updated Lifecycle Species")
                return TestResult.Failed("Update did not apply");

            // Step 3: Add to realm
            Console.WriteLine("  Step 3: Adding to realm...");
            species = await speciesClient.AddSpeciesToRealmAsync(new AddSpeciesToRealmRequest
            {
                SpeciesId = species.SpeciesId,
                RealmId = realm.RealmId
            });

            if (species.RealmIds == null || !species.RealmIds.Contains(realm.RealmId))
                return TestResult.Failed("Realm not added");

            // Step 4: Verify by realm listing
            Console.WriteLine("  Step 4: Verifying realm listing...");
            var realmSpecies = await speciesClient.ListSpeciesByRealmAsync(new ListSpeciesByRealmRequest
            {
                RealmId = realm.RealmId
            });
            if (realmSpecies.Species == null || !realmSpecies.Species.Any(s => s.SpeciesId == species.SpeciesId))
                return TestResult.Failed("Species not found in realm listing");

            // Step 5: Get by code
            Console.WriteLine("  Step 5: Verifying code lookup...");
            var byCode = await speciesClient.GetSpeciesByCodeAsync(new GetSpeciesByCodeRequest
            {
                Code = species.Code
            });
            if (byCode.SpeciesId != species.SpeciesId)
                return TestResult.Failed("Code lookup returned wrong species");

            // Step 6: Remove from realm
            Console.WriteLine("  Step 6: Removing from realm...");
            species = await speciesClient.RemoveSpeciesFromRealmAsync(new RemoveSpeciesFromRealmRequest
            {
                SpeciesId = species.SpeciesId,
                RealmId = realm.RealmId
            });

            if (species.RealmIds != null && species.RealmIds.Contains(realm.RealmId))
                return TestResult.Failed("Realm still present after removal");

            // Step 7: Delete species
            Console.WriteLine("  Step 7: Deleting species...");
            await speciesClient.DeleteSpeciesAsync(new DeleteSpeciesRequest
            {
                SpeciesId = species.SpeciesId
            });

            // Step 8: Verify deletion
            Console.WriteLine("  Step 8: Verifying deletion...");
            try
            {
                await speciesClient.GetSpeciesAsync(new GetSpeciesRequest { SpeciesId = species.SpeciesId });
                return TestResult.Failed("Species still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful("Complete species lifecycle test passed");
        }, "Complete species lifecycle");
}
