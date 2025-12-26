using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for species API endpoints using generated clients.
/// Tests the species service APIs directly via NSwag-generated SpeciesClient.
///
/// Note: Species APIs test service-to-service communication via Dapr.
/// These tests validate realm-associated species management with real datastores.
/// Species-realm associations require real Realms to exist first.
/// </summary>
public class SpeciesTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
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
        };
    }

    /// <summary>
    /// Helper to create a test realm for species tests.
    /// </summary>
    private static async Task<RealmResponse> CreateTestRealmAsync(string suffix)
    {
        var realmClient = Program.ServiceProvider!.GetRequiredService<IRealmClient>();
        return await realmClient.CreateRealmAsync(new CreateRealmRequest
        {
            Code = $"SPECIES_TEST_{DateTime.Now.Ticks}_{suffix}",
            Name = $"Species Test Realm {suffix}",
            Category = "TEST"
        });
    }

    private static async Task<TestResult> TestCreateSpecies(ITestClient client, string[] args)
    {
        try
        {
            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            // Create without realm association for basic creation test
            var createRequest = new CreateSpeciesRequest
            {
                Code = $"TEST_SPECIES_{DateTime.Now.Ticks}",
                Name = "Test Species",
                Description = "A test species for HTTP testing"
            };

            var response = await speciesClient.CreateSpeciesAsync(createRequest);

            if (response.SpeciesId == Guid.Empty)
                return TestResult.Failed("Create returned empty ID");

            if (response.Code != createRequest.Code.ToUpperInvariant())
                return TestResult.Failed($"Code mismatch: expected '{createRequest.Code.ToUpperInvariant()}', got '{response.Code}'");

            return TestResult.Successful($"Created species: ID={response.SpeciesId}, Code={response.Code}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Create failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetSpecies(ITestClient client, string[] args)
    {
        try
        {
            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            // Create a species first
            var createRequest = new CreateSpeciesRequest
            {
                Code = $"GET_SPECIES_{DateTime.Now.Ticks}",
                Name = "Get Test Species"
            };
            var created = await speciesClient.CreateSpeciesAsync(createRequest);

            // Now retrieve it
            var getRequest = new GetSpeciesRequest { SpeciesId = created.SpeciesId };
            var response = await speciesClient.GetSpeciesAsync(getRequest);

            if (response.SpeciesId != created.SpeciesId)
                return TestResult.Failed("ID mismatch");

            if (response.Name != createRequest.Name)
                return TestResult.Failed($"Name mismatch: expected '{createRequest.Name}', got '{response.Name}'");

            return TestResult.Successful($"Retrieved species: ID={response.SpeciesId}, Code={response.Code}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Get failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetSpeciesByCode(ITestClient client, string[] args)
    {
        try
        {
            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            // Create a species first
            var code = $"CODE_SPECIES_{DateTime.Now.Ticks}";
            var createRequest = new CreateSpeciesRequest
            {
                Code = code,
                Name = "Code Lookup Species"
            };
            var created = await speciesClient.CreateSpeciesAsync(createRequest);

            // Retrieve by code
            var getRequest = new GetSpeciesByCodeRequest { Code = code };
            var response = await speciesClient.GetSpeciesByCodeAsync(getRequest);

            if (response.SpeciesId != created.SpeciesId)
                return TestResult.Failed("ID mismatch when fetching by code");

            return TestResult.Successful($"Retrieved species by code: ID={response.SpeciesId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Get by code failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestUpdateSpecies(ITestClient client, string[] args)
    {
        try
        {
            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            // Create a species
            var createRequest = new CreateSpeciesRequest
            {
                Code = $"UPDATE_SPECIES_{DateTime.Now.Ticks}",
                Name = "Original Species Name",
                Description = "Original description"
            };
            var created = await speciesClient.CreateSpeciesAsync(createRequest);

            // Update it
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
                return TestResult.Failed($"Description not updated");

            return TestResult.Successful($"Updated species: ID={response.SpeciesId}, Name={response.Name}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Update failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestDeleteSpecies(ITestClient client, string[] args)
    {
        try
        {
            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            // Create a species
            var createRequest = new CreateSpeciesRequest
            {
                Code = $"DELETE_SPECIES_{DateTime.Now.Ticks}",
                Name = "Delete Test Species"
            };
            var created = await speciesClient.CreateSpeciesAsync(createRequest);

            // Delete it
            await speciesClient.DeleteSpeciesAsync(new DeleteSpeciesRequest
            {
                SpeciesId = created.SpeciesId
            });

            // Verify deletion
            try
            {
                await speciesClient.GetSpeciesAsync(new GetSpeciesRequest
                {
                    SpeciesId = created.SpeciesId
                });
                return TestResult.Failed("Species still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful($"Deleted species: ID={created.SpeciesId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Delete failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestListSpecies(ITestClient client, string[] args)
    {
        try
        {
            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            // Create some species
            for (int i = 0; i < 3; i++)
            {
                await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
                {
                    Code = $"LIST_SPECIES_{DateTime.Now.Ticks}_{i}",
                    Name = $"List Test Species {i}"
                });
            }

            // List all
            var response = await speciesClient.ListSpeciesAsync(new ListSpeciesRequest());

            if (response.Species == null || response.Species.Count < 3)
                return TestResult.Failed($"Expected at least 3 species, got {response.Species?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Species.Count} species");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"List failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestAddSpeciesToRealm(ITestClient client, string[] args)
    {
        try
        {
            // Create a real realm first
            var realm = await CreateTestRealmAsync("ADD");

            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            // Create a species
            var species = await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = $"ADD_REALM_SPECIES_{DateTime.Now.Ticks}",
                Name = "Add Realm Species"
            });

            // Add to the real realm
            var response = await speciesClient.AddSpeciesToRealmAsync(new AddSpeciesToRealmRequest
            {
                SpeciesId = species.SpeciesId,
                RealmId = realm.RealmId
            });

            if (response.RealmIds == null || !response.RealmIds.Contains(realm.RealmId))
                return TestResult.Failed("Realm ID not added to species");

            return TestResult.Successful($"Added species to realm: SpeciesID={species.SpeciesId}, RealmID={realm.RealmId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Add to realm failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestRemoveSpeciesFromRealm(ITestClient client, string[] args)
    {
        try
        {
            // Create real realms first
            var realm1 = await CreateTestRealmAsync("REMOVE1");
            var realm2 = await CreateTestRealmAsync("REMOVE2");

            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            // Create a species with realm associations
            var species = await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = $"REMOVE_REALM_SPECIES_{DateTime.Now.Ticks}",
                Name = "Remove Realm Species",
                RealmIds = new List<Guid> { realm1.RealmId, realm2.RealmId }
            });

            // Remove from one realm
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
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Remove from realm failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestListSpeciesByRealm(ITestClient client, string[] args)
    {
        try
        {
            // Create a real realm first
            var realm = await CreateTestRealmAsync("LIST");

            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            // Create species with the specific realm
            for (int i = 0; i < 3; i++)
            {
                await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
                {
                    Code = $"REALM_LIST_SPECIES_{DateTime.Now.Ticks}_{i}",
                    Name = $"Realm List Species {i}",
                    RealmIds = new List<Guid> { realm.RealmId }
                });
            }

            // List by realm
            var response = await speciesClient.ListSpeciesByRealmAsync(new ListSpeciesByRealmRequest
            {
                RealmId = realm.RealmId
            });

            if (response.Species == null || response.Species.Count < 3)
                return TestResult.Failed($"Expected at least 3 species in realm, got {response.Species?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Species.Count} species in realm {realm.RealmId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"List by realm failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetNonExistentSpecies(ITestClient client, string[] args)
    {
        try
        {
            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            try
            {
                await speciesClient.GetSpeciesAsync(new GetSpeciesRequest
                {
                    SpeciesId = Guid.NewGuid()
                });
                return TestResult.Failed("Expected 404 for non-existent species");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Correctly returned 404 for non-existent species");
            }
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Unexpected error: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestDuplicateCodeConflict(ITestClient client, string[] args)
    {
        try
        {
            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            var code = $"DUPLICATE_SPECIES_{DateTime.Now.Ticks}";

            // Create first species
            await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = code,
                Name = "First Species"
            });

            // Try to create second with same code
            try
            {
                await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
                {
                    Code = code,
                    Name = "Second Species"
                });
                return TestResult.Failed("Expected 409 for duplicate code");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                return TestResult.Successful("Correctly returned 409 for duplicate code");
            }
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Unexpected error: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestSeedSpecies(ITestClient client, string[] args)
    {
        try
        {
            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();

            var seedRequest = new SeedSpeciesRequest
            {
                Species = new List<SeedSpecies>
                {
                    new SeedSpecies
                    {
                        Code = $"SEED_HUMAN_{DateTime.Now.Ticks}",
                        Name = "Human",
                        Description = "Standard humanoid species"
                    },
                    new SeedSpecies
                    {
                        Code = $"SEED_ELF_{DateTime.Now.Ticks}",
                        Name = "Elf",
                        Description = "Long-lived magical species"
                    }
                },
                UpdateExisting = false
            };

            var response = await speciesClient.SeedSpeciesAsync(seedRequest);

            if (response.Created < 2)
                return TestResult.Failed($"Expected 2 created, got {response.Created}");

            return TestResult.Successful($"Seed completed: Created={response.Created}, Updated={response.Updated}, Skipped={response.Skipped}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Seed failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestCompleteSpeciesLifecycle(ITestClient client, string[] args)
    {
        try
        {
            // Create a real realm first for lifecycle test
            var realm = await CreateTestRealmAsync("LIFECYCLE");

            var speciesClient = Program.ServiceProvider!.GetRequiredService<ISpeciesClient>();
            var testId = DateTime.Now.Ticks;

            // Step 1: Create species
            Console.WriteLine("  Step 1: Creating species...");
            var species = await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = $"LIFECYCLE_SPECIES_{testId}",
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
                await speciesClient.GetSpeciesAsync(new GetSpeciesRequest
                {
                    SpeciesId = species.SpeciesId
                });
                return TestResult.Failed("Species still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful("Complete species lifecycle test passed");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Lifecycle test failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}
