using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for realm API endpoints using generated clients.
/// Tests the realm service APIs directly via NSwag-generated RealmClient.
///
/// Note: Realm APIs test service-to-service communication via Dapr.
/// These tests validate realm management with real datastores.
/// </summary>
public class RealmTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            // CRUD operations
            new ServiceTest(TestCreateRealm, "CreateRealm", "Realm", "Test realm creation"),
            new ServiceTest(TestGetRealm, "GetRealm", "Realm", "Test realm retrieval by ID"),
            new ServiceTest(TestGetRealmByCode, "GetRealmByCode", "Realm", "Test realm retrieval by code"),
            new ServiceTest(TestUpdateRealm, "UpdateRealm", "Realm", "Test realm update"),
            new ServiceTest(TestDeleteRealm, "DeleteRealm", "Realm", "Test realm deletion"),
            new ServiceTest(TestListRealms, "ListRealms", "Realm", "Test listing all realms"),

            // Deprecation operations
            new ServiceTest(TestDeprecateRealm, "DeprecateRealm", "Realm", "Test deprecating a realm"),
            new ServiceTest(TestUndeprecateRealm, "UndeprecateRealm", "Realm", "Test restoring a deprecated realm"),

            // Validation endpoint
            new ServiceTest(TestRealmExists, "RealmExists", "Realm", "Test realm existence check"),

            // Error handling
            new ServiceTest(TestGetNonExistentRealm, "GetNonExistentRealm", "Realm", "Test 404 for non-existent realm"),
            new ServiceTest(TestDuplicateCodeConflict, "Realm_DuplicateCodeConflict", "Realm", "Test 409 for duplicate code"),

            // Seed operation
            new ServiceTest(TestSeedRealms, "SeedRealms", "Realm", "Test seeding realms"),

            // Complete lifecycle
            new ServiceTest(TestCompleteRealmLifecycle, "CompleteRealmLifecycle", "Realm", "Test complete realm lifecycle with deprecation"),
        };
    }

    private static async Task<TestResult> TestCreateRealm(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            var createRequest = new CreateRealmRequest
            {
                Code = $"TEST_REALM_{DateTime.Now.Ticks}",
                Name = "Test Realm",
                Description = "A test realm for HTTP testing",
                Category = "TEST",
                IsActive = true
            };

            var response = await realmClient.CreateRealmAsync(createRequest);

            if (response.RealmId == Guid.Empty)
                return TestResult.Failed("Create returned empty ID");

            if (response.Code != createRequest.Code.ToUpperInvariant())
                return TestResult.Failed($"Code mismatch: expected '{createRequest.Code.ToUpperInvariant()}', got '{response.Code}'");

            return TestResult.Successful($"Created realm: ID={response.RealmId}, Code={response.Code}");
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

    private static async Task<TestResult> TestGetRealm(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            // Create a realm first
            var createRequest = new CreateRealmRequest
            {
                Code = $"GET_REALM_{DateTime.Now.Ticks}",
                Name = "Get Test Realm"
            };
            var created = await realmClient.CreateRealmAsync(createRequest);

            // Now retrieve it
            var getRequest = new GetRealmRequest { RealmId = created.RealmId };
            var response = await realmClient.GetRealmAsync(getRequest);

            if (response.RealmId != created.RealmId)
                return TestResult.Failed("ID mismatch");

            if (response.Name != createRequest.Name)
                return TestResult.Failed($"Name mismatch: expected '{createRequest.Name}', got '{response.Name}'");

            return TestResult.Successful($"Retrieved realm: ID={response.RealmId}, Code={response.Code}");
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

    private static async Task<TestResult> TestGetRealmByCode(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            // Create a realm first
            var code = $"CODE_REALM_{DateTime.Now.Ticks}";
            var createRequest = new CreateRealmRequest
            {
                Code = code,
                Name = "Code Lookup Realm"
            };
            var created = await realmClient.CreateRealmAsync(createRequest);

            // Retrieve by code
            var getRequest = new GetRealmByCodeRequest { Code = code };
            var response = await realmClient.GetRealmByCodeAsync(getRequest);

            if (response.RealmId != created.RealmId)
                return TestResult.Failed("ID mismatch when fetching by code");

            return TestResult.Successful($"Retrieved realm by code: ID={response.RealmId}");
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

    private static async Task<TestResult> TestUpdateRealm(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            // Create a realm
            var createRequest = new CreateRealmRequest
            {
                Code = $"UPDATE_REALM_{DateTime.Now.Ticks}",
                Name = "Original Realm Name",
                Description = "Original description"
            };
            var created = await realmClient.CreateRealmAsync(createRequest);

            // Update it
            var updateRequest = new UpdateRealmRequest
            {
                RealmId = created.RealmId,
                Name = "Updated Realm Name",
                Description = "Updated description"
            };
            var response = await realmClient.UpdateRealmAsync(updateRequest);

            if (response.Name != "Updated Realm Name")
                return TestResult.Failed($"Name not updated: expected 'Updated Realm Name', got '{response.Name}'");

            if (response.Description != "Updated description")
                return TestResult.Failed($"Description not updated");

            return TestResult.Successful($"Updated realm: ID={response.RealmId}, Name={response.Name}");
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

    private static async Task<TestResult> TestDeleteRealm(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            // Create a realm
            var createRequest = new CreateRealmRequest
            {
                Code = $"DELETE_REALM_{DateTime.Now.Ticks}",
                Name = "Delete Test Realm"
            };
            var created = await realmClient.CreateRealmAsync(createRequest);

            // Deprecate first (required before delete)
            await realmClient.DeprecateRealmAsync(new DeprecateRealmRequest
            {
                RealmId = created.RealmId,
                Reason = "Testing deletion"
            });

            // Delete it
            await realmClient.DeleteRealmAsync(new DeleteRealmRequest
            {
                RealmId = created.RealmId
            });

            // Verify deletion
            try
            {
                await realmClient.GetRealmAsync(new GetRealmRequest
                {
                    RealmId = created.RealmId
                });
                return TestResult.Failed("Realm still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful($"Deleted realm: ID={created.RealmId}");
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

    private static async Task<TestResult> TestListRealms(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            // Create some realms
            for (int i = 0; i < 3; i++)
            {
                await realmClient.CreateRealmAsync(new CreateRealmRequest
                {
                    Code = $"LIST_REALM_{DateTime.Now.Ticks}_{i}",
                    Name = $"List Test Realm {i}"
                });
            }

            // List all
            var response = await realmClient.ListRealmsAsync(new ListRealmsRequest());

            if (response.Realms == null || response.Realms.Count < 3)
                return TestResult.Failed($"Expected at least 3 realms, got {response.Realms?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Realms.Count} realms");
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

    private static async Task<TestResult> TestDeprecateRealm(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            // Create a realm
            var realm = await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = $"DEPRECATE_REALM_{DateTime.Now.Ticks}",
                Name = "Deprecate Test Realm"
            });

            // Deprecate it
            var response = await realmClient.DeprecateRealmAsync(new DeprecateRealmRequest
            {
                RealmId = realm.RealmId,
                Reason = "Testing deprecation"
            });

            if (!response.IsDeprecated)
                return TestResult.Failed("Realm should be deprecated");

            if (response.DeprecationReason != "Testing deprecation")
                return TestResult.Failed("Deprecation reason not set");

            return TestResult.Successful($"Deprecated realm: ID={realm.RealmId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Deprecate failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestUndeprecateRealm(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            // Create and deprecate a realm
            var realm = await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = $"UNDEP_REALM_{DateTime.Now.Ticks}",
                Name = "Undeprecate Test Realm"
            });

            await realmClient.DeprecateRealmAsync(new DeprecateRealmRequest
            {
                RealmId = realm.RealmId
            });

            // Undeprecate it
            var response = await realmClient.UndeprecateRealmAsync(new UndeprecateRealmRequest
            {
                RealmId = realm.RealmId
            });

            if (response.IsDeprecated)
                return TestResult.Failed("Realm should not be deprecated after undeprecation");

            return TestResult.Successful($"Undeprecated realm: ID={realm.RealmId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Undeprecate failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestRealmExists(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            // Create a realm
            var realm = await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = $"EXISTS_REALM_{DateTime.Now.Ticks}",
                Name = "Exists Test Realm"
            });

            // Check existence
            var existsResponse = await realmClient.RealmExistsAsync(new RealmExistsRequest
            {
                RealmId = realm.RealmId
            });

            if (!existsResponse.Exists)
                return TestResult.Failed("Realm should exist");

            if (!existsResponse.IsActive)
                return TestResult.Failed("Realm should be active");

            // Check non-existent realm
            var notExistsResponse = await realmClient.RealmExistsAsync(new RealmExistsRequest
            {
                RealmId = Guid.NewGuid()
            });

            if (notExistsResponse.Exists)
                return TestResult.Failed("Non-existent realm should not exist");

            return TestResult.Successful($"Realm existence check passed");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Exists check failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetNonExistentRealm(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            try
            {
                await realmClient.GetRealmAsync(new GetRealmRequest
                {
                    RealmId = Guid.NewGuid()
                });
                return TestResult.Failed("Expected 404 for non-existent realm");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Correctly returned 404 for non-existent realm");
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
            var realmClient = new RealmClient();

            var code = $"DUPLICATE_REALM_{DateTime.Now.Ticks}";

            // Create first realm
            await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = code,
                Name = "First Realm"
            });

            // Try to create second with same code
            try
            {
                await realmClient.CreateRealmAsync(new CreateRealmRequest
                {
                    Code = code,
                    Name = "Second Realm"
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

    private static async Task<TestResult> TestSeedRealms(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();

            var seedRequest = new SeedRealmsRequest
            {
                Realms = new List<SeedRealm>
                {
                    new SeedRealm
                    {
                        Code = $"SEED_OMEGA_{DateTime.Now.Ticks}",
                        Name = "Omega",
                        Description = "Cyberpunk world",
                        Category = "MAIN"
                    },
                    new SeedRealm
                    {
                        Code = $"SEED_ARCADIA_{DateTime.Now.Ticks}",
                        Name = "Arcadia",
                        Description = "Fantasy world",
                        Category = "MAIN"
                    }
                },
                UpdateExisting = false
            };

            var response = await realmClient.SeedRealmsAsync(seedRequest);

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

    private static async Task<TestResult> TestCompleteRealmLifecycle(ITestClient client, string[] args)
    {
        try
        {
            var realmClient = new RealmClient();
            var testId = DateTime.Now.Ticks;

            // Step 1: Create realm
            Console.WriteLine("  Step 1: Creating realm...");
            var realm = await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = $"LIFECYCLE_REALM_{testId}",
                Name = "Lifecycle Realm",
                Description = "Realm for lifecycle testing",
                Category = "TEST"
            });

            // Step 2: Update realm
            Console.WriteLine("  Step 2: Updating realm...");
            realm = await realmClient.UpdateRealmAsync(new UpdateRealmRequest
            {
                RealmId = realm.RealmId,
                Name = "Updated Lifecycle Realm"
            });

            if (realm.Name != "Updated Lifecycle Realm")
                return TestResult.Failed("Update did not apply");

            // Step 3: Verify existence
            Console.WriteLine("  Step 3: Verifying existence...");
            var exists = await realmClient.RealmExistsAsync(new RealmExistsRequest
            {
                RealmId = realm.RealmId
            });
            if (!exists.Exists || !exists.IsActive)
                return TestResult.Failed("Realm should exist and be active");

            // Step 4: Get by code
            Console.WriteLine("  Step 4: Verifying code lookup...");
            var byCode = await realmClient.GetRealmByCodeAsync(new GetRealmByCodeRequest
            {
                Code = realm.Code
            });
            if (byCode.RealmId != realm.RealmId)
                return TestResult.Failed("Code lookup returned wrong realm");

            // Step 5: Deprecate realm
            Console.WriteLine("  Step 5: Deprecating realm...");
            realm = await realmClient.DeprecateRealmAsync(new DeprecateRealmRequest
            {
                RealmId = realm.RealmId,
                Reason = "Lifecycle test"
            });

            if (!realm.IsDeprecated)
                return TestResult.Failed("Realm should be deprecated");

            // Step 6: Verify inactive after deprecation
            Console.WriteLine("  Step 6: Verifying inactive status...");
            exists = await realmClient.RealmExistsAsync(new RealmExistsRequest
            {
                RealmId = realm.RealmId
            });
            if (!exists.Exists || exists.IsActive)
                return TestResult.Failed("Realm should exist but be inactive after deprecation");

            // Step 7: Undeprecate
            Console.WriteLine("  Step 7: Undeprecating realm...");
            realm = await realmClient.UndeprecateRealmAsync(new UndeprecateRealmRequest
            {
                RealmId = realm.RealmId
            });

            if (realm.IsDeprecated)
                return TestResult.Failed("Realm should not be deprecated after undeprecation");

            // Step 8: Deprecate again for deletion
            Console.WriteLine("  Step 8: Deprecating again for deletion...");
            await realmClient.DeprecateRealmAsync(new DeprecateRealmRequest
            {
                RealmId = realm.RealmId
            });

            // Step 9: Delete realm
            Console.WriteLine("  Step 9: Deleting realm...");
            await realmClient.DeleteRealmAsync(new DeleteRealmRequest
            {
                RealmId = realm.RealmId
            });

            // Step 10: Verify deletion
            Console.WriteLine("  Step 10: Verifying deletion...");
            try
            {
                await realmClient.GetRealmAsync(new GetRealmRequest
                {
                    RealmId = realm.RealmId
                });
                return TestResult.Failed("Realm still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful("Complete realm lifecycle test passed");
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
