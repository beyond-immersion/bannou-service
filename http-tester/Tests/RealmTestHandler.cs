using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for realm API endpoints using generated clients.
/// Tests the realm service APIs directly via NSwag-generated RealmClient.
///
/// Note: Realm APIs test service-to-service communication via mesh.
/// These tests validate realm management with real datastores.
/// </summary>
public class RealmTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
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
    ];

    private static Task<TestResult> TestCreateRealm(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            var createRequest = new CreateRealmRequest
            {
                Code = GenerateTestId("TEST_REALM"),
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
        }, "Create realm");

    private static Task<TestResult> TestGetRealm(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            var createRequest = new CreateRealmRequest
            {
                Code = GenerateTestId("GET_REALM"),
                Name = "Get Test Realm"
            };
            var created = await realmClient.CreateRealmAsync(createRequest);

            var response = await realmClient.GetRealmAsync(new GetRealmRequest { RealmId = created.RealmId });

            if (response.RealmId != created.RealmId)
                return TestResult.Failed("ID mismatch");

            if (response.Name != createRequest.Name)
                return TestResult.Failed($"Name mismatch: expected '{createRequest.Name}', got '{response.Name}'");

            return TestResult.Successful($"Retrieved realm: ID={response.RealmId}, Code={response.Code}");
        }, "Get realm");

    private static Task<TestResult> TestGetRealmByCode(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            var code = GenerateTestId("CODE_REALM");
            var createRequest = new CreateRealmRequest
            {
                Code = code,
                Name = "Code Lookup Realm"
            };
            var created = await realmClient.CreateRealmAsync(createRequest);

            var response = await realmClient.GetRealmByCodeAsync(new GetRealmByCodeRequest { Code = code });

            if (response.RealmId != created.RealmId)
                return TestResult.Failed("ID mismatch when fetching by code");

            return TestResult.Successful($"Retrieved realm by code: ID={response.RealmId}");
        }, "Get realm by code");

    private static Task<TestResult> TestUpdateRealm(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            var createRequest = new CreateRealmRequest
            {
                Code = GenerateTestId("UPDATE_REALM"),
                Name = "Original Realm Name",
                Description = "Original description"
            };
            var created = await realmClient.CreateRealmAsync(createRequest);

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
                return TestResult.Failed("Description not updated");

            return TestResult.Successful($"Updated realm: ID={response.RealmId}, Name={response.Name}");
        }, "Update realm");

    private static Task<TestResult> TestDeleteRealm(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            var createRequest = new CreateRealmRequest
            {
                Code = GenerateTestId("DELETE_REALM"),
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
            await realmClient.DeleteRealmAsync(new DeleteRealmRequest { RealmId = created.RealmId });

            // Verify deletion - expect 404
            try
            {
                await realmClient.GetRealmAsync(new GetRealmRequest { RealmId = created.RealmId });
                return TestResult.Failed("Realm still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful($"Deleted realm: ID={created.RealmId}");
        }, "Delete realm");

    private static Task<TestResult> TestListRealms(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            // Create some realms
            for (var i = 0; i < 3; i++)
            {
                await realmClient.CreateRealmAsync(new CreateRealmRequest
                {
                    Code = $"{GenerateTestId("LIST_REALM")}_{i}",
                    Name = $"List Test Realm {i}"
                });
            }

            var response = await realmClient.ListRealmsAsync(new ListRealmsRequest());

            if (response.Realms == null || response.Realms.Count < 3)
                return TestResult.Failed($"Expected at least 3 realms, got {response.Realms?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Realms.Count} realms");
        }, "List realms");

    private static Task<TestResult> TestDeprecateRealm(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            var realm = await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = GenerateTestId("DEPRECATE_REALM"),
                Name = "Deprecate Test Realm"
            });

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
        }, "Deprecate realm");

    private static Task<TestResult> TestUndeprecateRealm(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            var realm = await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = GenerateTestId("UNDEP_REALM"),
                Name = "Undeprecate Test Realm"
            });

            await realmClient.DeprecateRealmAsync(new DeprecateRealmRequest { RealmId = realm.RealmId });

            var response = await realmClient.UndeprecateRealmAsync(new UndeprecateRealmRequest { RealmId = realm.RealmId });

            if (response.IsDeprecated)
                return TestResult.Failed("Realm should not be deprecated after undeprecation");

            return TestResult.Successful($"Undeprecated realm: ID={realm.RealmId}");
        }, "Undeprecate realm");

    private static Task<TestResult> TestRealmExists(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            var realm = await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = GenerateTestId("EXISTS_REALM"),
                Name = "Exists Test Realm"
            });

            var existsResponse = await realmClient.RealmExistsAsync(new RealmExistsRequest { RealmId = realm.RealmId });

            if (!existsResponse.Exists)
                return TestResult.Failed("Realm should exist");

            if (!existsResponse.IsActive)
                return TestResult.Failed("Realm should be active");

            var notExistsResponse = await realmClient.RealmExistsAsync(new RealmExistsRequest { RealmId = Guid.NewGuid() });

            if (notExistsResponse.Exists)
                return TestResult.Failed("Non-existent realm should not exist");

            return TestResult.Successful("Realm existence check passed");
        }, "Realm exists");

    private static Task<TestResult> TestGetNonExistentRealm(ITestClient client, string[] args) =>
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var realmClient = GetServiceClient<IRealmClient>();
                await realmClient.GetRealmAsync(new GetRealmRequest { RealmId = Guid.NewGuid() });
            },
            404,
            "Get non-existent realm");

    private static Task<TestResult> TestDuplicateCodeConflict(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();
            var code = GenerateTestId("DUPLICATE_REALM");

            // Create first realm
            await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = code,
                Name = "First Realm"
            });

            // Try to create second with same code - expect 409
            return await ExecuteExpectingStatusAsync(
                async () =>
                {
                    await realmClient.CreateRealmAsync(new CreateRealmRequest
                    {
                        Code = code,
                        Name = "Second Realm"
                    });
                },
                409,
                "Duplicate code");
        }, "Duplicate code conflict");

    private static Task<TestResult> TestSeedRealms(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();

            var seedRequest = new SeedRealmsRequest
            {
                Realms =
                [
                    new SeedRealm
                    {
                        Code = GenerateTestId("SEED_OMEGA"),
                        Name = "Omega",
                        Description = "Cyberpunk world",
                        Category = "MAIN"
                    },
                    new SeedRealm
                    {
                        Code = GenerateTestId("SEED_ARCADIA"),
                        Name = "Arcadia",
                        Description = "Fantasy world",
                        Category = "MAIN"
                    }
                ],
                UpdateExisting = false
            };

            var response = await realmClient.SeedRealmsAsync(seedRequest);

            if (response.Created < 2)
                return TestResult.Failed($"Expected 2 created, got {response.Created}");

            return TestResult.Successful($"Seed completed: Created={response.Created}, Updated={response.Updated}, Skipped={response.Skipped}");
        }, "Seed realms");

    private static Task<TestResult> TestCompleteRealmLifecycle(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();
            var testId = GenerateTestId("LIFECYCLE_REALM");

            // Step 1: Create realm
            Console.WriteLine("  Step 1: Creating realm...");
            var realm = await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = testId,
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
            var exists = await realmClient.RealmExistsAsync(new RealmExistsRequest { RealmId = realm.RealmId });
            if (!exists.Exists || !exists.IsActive)
                return TestResult.Failed("Realm should exist and be active");

            // Step 4: Get by code
            Console.WriteLine("  Step 4: Verifying code lookup...");
            var byCode = await realmClient.GetRealmByCodeAsync(new GetRealmByCodeRequest { Code = realm.Code });
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
            exists = await realmClient.RealmExistsAsync(new RealmExistsRequest { RealmId = realm.RealmId });
            if (!exists.Exists || exists.IsActive)
                return TestResult.Failed("Realm should exist but be inactive after deprecation");

            // Step 7: Undeprecate
            Console.WriteLine("  Step 7: Undeprecating realm...");
            realm = await realmClient.UndeprecateRealmAsync(new UndeprecateRealmRequest { RealmId = realm.RealmId });

            if (realm.IsDeprecated)
                return TestResult.Failed("Realm should not be deprecated after undeprecation");

            // Step 8: Deprecate again for deletion
            Console.WriteLine("  Step 8: Deprecating again for deletion...");
            await realmClient.DeprecateRealmAsync(new DeprecateRealmRequest { RealmId = realm.RealmId });

            // Step 9: Delete realm
            Console.WriteLine("  Step 9: Deleting realm...");
            await realmClient.DeleteRealmAsync(new DeleteRealmRequest { RealmId = realm.RealmId });

            // Step 10: Verify deletion
            Console.WriteLine("  Step 10: Verifying deletion...");
            try
            {
                await realmClient.GetRealmAsync(new GetRealmRequest { RealmId = realm.RealmId });
                return TestResult.Failed("Realm still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful("Complete realm lifecycle test passed");
        }, "Complete realm lifecycle");
}
