using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for location API endpoints using generated clients.
/// Tests the location service APIs directly via NSwag-generated LocationClient.
///
/// Note: Location APIs test service-to-service communication via mesh.
/// These tests validate hierarchical location management with real datastores.
/// </summary>
public class LocationTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // CRUD operations
        new ServiceTest(TestCreateLocation, "CreateLocation", "Location", "Test location creation"),
        new ServiceTest(TestGetLocation, "GetLocation", "Location", "Test location retrieval by ID"),
        new ServiceTest(TestGetLocationByCode, "GetLocationByCode", "Location", "Test location retrieval by code and realm"),
        new ServiceTest(TestUpdateLocation, "UpdateLocation", "Location", "Test location update"),
        new ServiceTest(TestDeleteLocation, "DeleteLocation", "Location", "Test location deletion"),

        // Listing operations
        new ServiceTest(TestListLocations, "ListLocations", "Location", "Test listing all locations"),
        new ServiceTest(TestListLocationsByRealm, "ListLocationsByRealm", "Location", "Test listing locations by realm"),
        new ServiceTest(TestListRootLocations, "ListRootLocations", "Location", "Test listing root locations in realm"),

        // Hierarchy operations
        new ServiceTest(TestSetLocationParent, "SetLocationParent", "Location", "Test setting location parent"),
        new ServiceTest(TestRemoveLocationParent, "RemoveLocationParent", "Location", "Test removing location parent"),
        new ServiceTest(TestListLocationsByParent, "ListLocationsByParent", "Location", "Test listing child locations"),
        new ServiceTest(TestGetLocationAncestors, "GetLocationAncestors", "Location", "Test getting location ancestors"),
        new ServiceTest(TestGetLocationDescendants, "GetLocationDescendants", "Location", "Test getting location descendants"),

        // Deprecation operations
        new ServiceTest(TestDeprecateLocation, "DeprecateLocation", "Location", "Test deprecating a location"),
        new ServiceTest(TestUndeprecateLocation, "UndeprecateLocation", "Location", "Test restoring a deprecated location"),

        // Validation endpoints
        new ServiceTest(TestLocationExists, "LocationExists", "Location", "Test location existence check"),
        new ServiceTest(TestValidateTerritoryExclusivePass, "ValidateTerritory_ExclusivePass", "Location", "Test territory validation - exclusive mode passes when outside territory"),
        new ServiceTest(TestValidateTerritoryExclusiveFail, "ValidateTerritory_ExclusiveFail", "Location", "Test territory validation - exclusive mode fails when inside territory"),
        new ServiceTest(TestValidateTerritoryInclusivePass, "ValidateTerritory_InclusivePass", "Location", "Test territory validation - inclusive mode passes when inside territory"),
        new ServiceTest(TestValidateTerritoryInclusiveFail, "ValidateTerritory_InclusiveFail", "Location", "Test territory validation - inclusive mode fails when outside territory"),
        new ServiceTest(TestValidateTerritoryNotFound, "ValidateTerritory_NotFound", "Location", "Test territory validation - 404 for non-existent location"),

        // Error handling
        new ServiceTest(TestGetNonExistentLocation, "GetNonExistentLocation", "Location", "Test 404 for non-existent location"),
        new ServiceTest(TestDuplicateCodeConflict, "Location_DuplicateCodeConflict", "Location", "Test 409 for duplicate code in realm"),

        // Seed operation
        new ServiceTest(TestSeedLocations, "SeedLocations", "Location", "Test seeding locations"),

        // Complete lifecycle
        new ServiceTest(TestCompleteLocationLifecycle, "CompleteLocationLifecycle", "Location", "Test complete location lifecycle with hierarchy"),
    ];

    private static async Task<TestResult> TestCreateLocation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            // Create a realm for the location
            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "CREATE");

            var createRequest = new CreateLocationRequest
            {
                Code = $"TEST_LOC_{DateTime.Now.Ticks}",
                Name = "Test Location",
                Description = "A test location for HTTP testing",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY
            };

            var response = await locationClient.CreateLocationAsync(createRequest);

            if (response.LocationId == Guid.Empty)
                return TestResult.Failed("Create returned empty ID");

            if (response.Code != createRequest.Code.ToUpperInvariant())
                return TestResult.Failed($"Code mismatch: expected '{createRequest.Code.ToUpperInvariant()}', got '{response.Code}'");

            if (response.RealmId != realm.RealmId)
                return TestResult.Failed("Realm ID mismatch");

            return TestResult.Successful($"Created location: ID={response.LocationId}, Code={response.Code}");
        }, "Create location");

    private static async Task<TestResult> TestGetLocation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "GET");

            // Create a location first
            var createRequest = new CreateLocationRequest
            {
                Code = $"GET_LOC_{DateTime.Now.Ticks}",
                Name = "Get Test Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            };
            var created = await locationClient.CreateLocationAsync(createRequest);

            // Now retrieve it
            var getRequest = new GetLocationRequest { LocationId = created.LocationId };
            var response = await locationClient.GetLocationAsync(getRequest);

            if (response.LocationId != created.LocationId)
                return TestResult.Failed("ID mismatch");

            if (response.Name != createRequest.Name)
                return TestResult.Failed($"Name mismatch: expected '{createRequest.Name}', got '{response.Name}'");

            return TestResult.Successful($"Retrieved location: ID={response.LocationId}, Code={response.Code}");
        }, "Get location");

    private static async Task<TestResult> TestGetLocationByCode(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "BYCODE");

            // Create a location first
            var code = $"CODE_LOC_{DateTime.Now.Ticks}";
            var createRequest = new CreateLocationRequest
            {
                Code = code,
                Name = "Code Lookup Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.BUILDING
            };
            var created = await locationClient.CreateLocationAsync(createRequest);

            // Retrieve by code and realm
            var getRequest = new GetLocationByCodeRequest
            {
                Code = code,
                RealmId = realm.RealmId
            };
            var response = await locationClient.GetLocationByCodeAsync(getRequest);

            if (response.LocationId != created.LocationId)
                return TestResult.Failed("ID mismatch when fetching by code");

            return TestResult.Successful($"Retrieved location by code: ID={response.LocationId}");
        }, "Get location by code");

    private static async Task<TestResult> TestUpdateLocation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "UPDATE");

            // Create a location
            var createRequest = new CreateLocationRequest
            {
                Code = $"UPDATE_LOC_{DateTime.Now.Ticks}",
                Name = "Original Location Name",
                Description = "Original description",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY
            };
            var created = await locationClient.CreateLocationAsync(createRequest);

            // Update it
            var updateRequest = new UpdateLocationRequest
            {
                LocationId = created.LocationId,
                Name = "Updated Location Name",
                Description = "Updated description"
            };
            var response = await locationClient.UpdateLocationAsync(updateRequest);

            if (response.Name != "Updated Location Name")
                return TestResult.Failed($"Name not updated: expected 'Updated Location Name', got '{response.Name}'");

            if (response.Description != "Updated description")
                return TestResult.Failed("Description not updated");

            return TestResult.Successful($"Updated location: ID={response.LocationId}, Name={response.Name}");
        }, "Update location");

    private static async Task<TestResult> TestDeleteLocation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "DELETE");

            // Create a location
            var createRequest = new CreateLocationRequest
            {
                Code = $"DELETE_LOC_{DateTime.Now.Ticks}",
                Name = "Delete Test Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.BUILDING
            };
            var created = await locationClient.CreateLocationAsync(createRequest);

            // Deprecate first (required before delete)
            await locationClient.DeprecateLocationAsync(new DeprecateLocationRequest
            {
                LocationId = created.LocationId,
                Reason = "Testing deletion"
            });

            // Delete it
            await locationClient.DeleteLocationAsync(new DeleteLocationRequest
            {
                LocationId = created.LocationId
            });

            // Verify deletion
            try
            {
                await locationClient.GetLocationAsync(new GetLocationRequest
                {
                    LocationId = created.LocationId
                });
                return TestResult.Failed("Location still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful($"Deleted location: ID={created.LocationId}");
        }, "Delete location");

    private static async Task<TestResult> TestListLocations(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "LIST");

            // Create some locations
            for (var i = 0; i < 3; i++)
            {
                await locationClient.CreateLocationAsync(new CreateLocationRequest
                {
                    Code = $"LIST_LOC_{DateTime.Now.Ticks}_{i}",
                    Name = $"List Test Location {i}",
                    RealmId = realm.RealmId,
                    LocationType = LocationType.CITY
                });
            }

            // List all locations in the realm (RealmId is required)
            var response = await locationClient.ListLocationsAsync(new ListLocationsRequest
            {
                RealmId = realm.RealmId
            });

            if (response.Locations == null || response.Locations.Count < 3)
                return TestResult.Failed($"Expected at least 3 locations, got {response.Locations?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Locations.Count} locations in realm");
        }, "List locations");

    private static async Task<TestResult> TestListLocationsByRealm(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "BYREALM");

            // Create locations in the realm
            for (var i = 0; i < 3; i++)
            {
                await locationClient.CreateLocationAsync(new CreateLocationRequest
                {
                    Code = $"REALM_LIST_LOC_{DateTime.Now.Ticks}_{i}",
                    Name = $"Realm List Location {i}",
                    RealmId = realm.RealmId,
                    LocationType = LocationType.REGION
                });
            }

            // List by realm
            var response = await locationClient.ListLocationsByRealmAsync(new ListLocationsByRealmRequest
            {
                RealmId = realm.RealmId
            });

            if (response.Locations == null || response.Locations.Count < 3)
                return TestResult.Failed($"Expected at least 3 locations in realm, got {response.Locations?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Locations.Count} locations in realm");
        }, "List locations by realm");

    private static async Task<TestResult> TestListRootLocations(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "ROOT");

            // Create root locations (no parent)
            for (var i = 0; i < 3; i++)
            {
                await locationClient.CreateLocationAsync(new CreateLocationRequest
                {
                    Code = $"ROOT_LOC_{DateTime.Now.Ticks}_{i}",
                    Name = $"Root Location {i}",
                    RealmId = realm.RealmId,
                    LocationType = LocationType.CONTINENT
                });
            }

            // List root locations
            var response = await locationClient.ListRootLocationsAsync(new ListRootLocationsRequest
            {
                RealmId = realm.RealmId
            });

            if (response.Locations == null || response.Locations.Count < 3)
                return TestResult.Failed($"Expected at least 3 root locations, got {response.Locations?.Count ?? 0}");

            // Verify all are root (depth = 0)
            if (response.Locations.Any(l => l.Depth != 0))
                return TestResult.Failed("Some locations have non-zero depth");

            return TestResult.Successful($"Listed {response.Locations.Count} root locations");
        }, "List root locations");

    private static async Task<TestResult> TestSetLocationParent(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "SETPARENT");

            // Create parent location
            var parent = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"PARENT_LOC_{DateTime.Now.Ticks}",
                Name = "Parent Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            // Create child location (initially no parent)
            var child = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"CHILD_LOC_{DateTime.Now.Ticks}",
                Name = "Child Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY
            });

            // Set parent
            var response = await locationClient.SetLocationParentAsync(new SetLocationParentRequest
            {
                LocationId = child.LocationId,
                ParentLocationId = parent.LocationId
            });

            if (response.ParentLocationId != parent.LocationId)
                return TestResult.Failed("Parent not set correctly");

            if (response.Depth != 1)
                return TestResult.Failed($"Expected depth 1, got {response.Depth}");

            return TestResult.Successful($"Set parent: child={child.LocationId}, parent={parent.LocationId}");
        }, "Set location parent");

    private static async Task<TestResult> TestRemoveLocationParent(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "REMOVEPARENT");

            // Create parent location
            var parent = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"RP_PARENT_{DateTime.Now.Ticks}",
                Name = "Parent Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            // Create child with parent
            var child = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"RP_CHILD_{DateTime.Now.Ticks}",
                Name = "Child Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY,
                ParentLocationId = parent.LocationId
            });

            if (child.Depth != 1)
                return TestResult.Failed($"Initial depth should be 1, got {child.Depth}");

            // Remove parent
            var response = await locationClient.RemoveLocationParentAsync(new RemoveLocationParentRequest
            {
                LocationId = child.LocationId
            });

            if (response.ParentLocationId != null)
                return TestResult.Failed("Parent should be null after removal");

            if (response.Depth != 0)
                return TestResult.Failed($"Depth should be 0 after removing parent, got {response.Depth}");

            return TestResult.Successful($"Removed parent from location {child.LocationId}");
        }, "Remove location parent");

    private static async Task<TestResult> TestListLocationsByParent(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "BYPARENT");

            // Create parent location
            var parent = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"LBP_PARENT_{DateTime.Now.Ticks}",
                Name = "Parent Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            // Create child locations
            for (var i = 0; i < 3; i++)
            {
                await locationClient.CreateLocationAsync(new CreateLocationRequest
                {
                    Code = $"LBP_CHILD_{DateTime.Now.Ticks}_{i}",
                    Name = $"Child Location {i}",
                    RealmId = realm.RealmId,
                    LocationType = LocationType.CITY,
                    ParentLocationId = parent.LocationId
                });
            }

            // List by parent
            var response = await locationClient.ListLocationsByParentAsync(new ListLocationsByParentRequest
            {
                ParentLocationId = parent.LocationId
            });

            if (response.Locations == null || response.Locations.Count < 3)
                return TestResult.Failed($"Expected at least 3 children, got {response.Locations?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Locations.Count} child locations");
        }, "List locations by parent");

    private static async Task<TestResult> TestGetLocationAncestors(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "ANCESTORS");

            // Create hierarchy: Region -> City -> District
            var region = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"ANC_REGION_{DateTime.Now.Ticks}",
                Name = "Region",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            var city = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"ANC_CITY_{DateTime.Now.Ticks}",
                Name = "City",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY,
                ParentLocationId = region.LocationId
            });

            var district = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"ANC_DISTRICT_{DateTime.Now.Ticks}",
                Name = "District",
                RealmId = realm.RealmId,
                LocationType = LocationType.DISTRICT,
                ParentLocationId = city.LocationId
            });

            // Get ancestors of district
            var response = await locationClient.GetLocationAncestorsAsync(new GetLocationAncestorsRequest
            {
                LocationId = district.LocationId
            });

            if (response.Locations == null || response.Locations.Count != 2)
                return TestResult.Failed($"Expected 2 ancestors (city, region), got {response.Locations?.Count ?? 0}");

            // Verify correct order (immediate parent first)
            var ancestors = response.Locations.ToList();
            if (ancestors[0].LocationId != city.LocationId)
                return TestResult.Failed("First ancestor should be city");
            if (ancestors[1].LocationId != region.LocationId)
                return TestResult.Failed("Second ancestor should be region");

            return TestResult.Successful($"Got {response.Locations.Count} ancestors");
        }, "Get location ancestors");

    private static async Task<TestResult> TestGetLocationDescendants(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "DESCENDANTS");

            // Create hierarchy: Region -> 2 Cities -> 2 Districts each
            var region = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"DESC_REGION_{DateTime.Now.Ticks}",
                Name = "Region",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            for (var i = 0; i < 2; i++)
            {
                var city = await locationClient.CreateLocationAsync(new CreateLocationRequest
                {
                    Code = $"DESC_CITY_{DateTime.Now.Ticks}_{i}",
                    Name = $"City {i}",
                    RealmId = realm.RealmId,
                    LocationType = LocationType.CITY,
                    ParentLocationId = region.LocationId
                });

                for (var j = 0; j < 2; j++)
                {
                    await locationClient.CreateLocationAsync(new CreateLocationRequest
                    {
                        Code = $"DESC_DISTRICT_{DateTime.Now.Ticks}_{i}_{j}",
                        Name = $"District {i}-{j}",
                        RealmId = realm.RealmId,
                        LocationType = LocationType.DISTRICT,
                        ParentLocationId = city.LocationId
                    });
                }
            }

            // Get all descendants of region
            var response = await locationClient.GetLocationDescendantsAsync(new GetLocationDescendantsRequest
            {
                LocationId = region.LocationId
            });

            // Should have 2 cities + 4 districts = 6 descendants
            if (response.Locations == null || response.Locations.Count < 6)
                return TestResult.Failed($"Expected 6 descendants, got {response.Locations?.Count ?? 0}");

            return TestResult.Successful($"Got {response.Locations.Count} descendants");
        }, "Get location descendants");

    private static async Task<TestResult> TestDeprecateLocation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "DEPRECATE");

            // Create a location
            var location = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"DEP_LOC_{DateTime.Now.Ticks}",
                Name = "Deprecate Test Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.BUILDING
            });

            // Deprecate it
            var response = await locationClient.DeprecateLocationAsync(new DeprecateLocationRequest
            {
                LocationId = location.LocationId,
                Reason = "Testing deprecation"
            });

            if (!response.IsDeprecated)
                return TestResult.Failed("Location should be deprecated");

            if (response.DeprecationReason != "Testing deprecation")
                return TestResult.Failed("Deprecation reason not set");

            return TestResult.Successful($"Deprecated location: ID={location.LocationId}");
        }, "Deprecate location");

    private static async Task<TestResult> TestUndeprecateLocation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "UNDEPRECATE");

            // Create and deprecate a location
            var location = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"UNDEP_LOC_{DateTime.Now.Ticks}",
                Name = "Undeprecate Test Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.ROOM
            });

            await locationClient.DeprecateLocationAsync(new DeprecateLocationRequest
            {
                LocationId = location.LocationId
            });

            // Undeprecate it
            var response = await locationClient.UndeprecateLocationAsync(new UndeprecateLocationRequest
            {
                LocationId = location.LocationId
            });

            if (response.IsDeprecated)
                return TestResult.Failed("Location should not be deprecated after undeprecation");

            return TestResult.Successful($"Undeprecated location: ID={location.LocationId}");
        }, "Undeprecate location");

    private static async Task<TestResult> TestLocationExists(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "EXISTS");

            // Create a location
            var location = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"EXISTS_LOC_{DateTime.Now.Ticks}",
                Name = "Exists Test Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.LANDMARK
            });

            // Check existence
            var existsResponse = await locationClient.LocationExistsAsync(new LocationExistsRequest
            {
                LocationId = location.LocationId
            });

            if (!existsResponse.Exists)
                return TestResult.Failed("Location should exist");

            if (!existsResponse.IsActive)
                return TestResult.Failed("Location should be active");

            // Check non-existent location
            var notExistsResponse = await locationClient.LocationExistsAsync(new LocationExistsRequest
            {
                LocationId = Guid.NewGuid()
            });

            if (notExistsResponse.Exists)
                return TestResult.Failed("Non-existent location should not exist");

            return TestResult.Successful("Location existence check passed");
        }, "Check location existence");

    private static async Task<TestResult> TestValidateTerritoryExclusivePass(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "TERR_EX_PASS");

            // Create two unrelated locations
            var territoryLocation = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"TERR_ZONE_{DateTime.Now.Ticks}",
                Name = "Territory Zone",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            var outsideLocation = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"OUTSIDE_LOC_{DateTime.Now.Ticks}",
                Name = "Outside Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY
                // No parent - not related to territory
            });

            // Validate: exclusive mode should PASS because location is NOT inside territory
            var response = await locationClient.ValidateTerritoryAsync(new ValidateTerritoryRequest
            {
                LocationId = outsideLocation.LocationId,
                TerritoryLocationIds = new List<Guid> { territoryLocation.LocationId },
                TerritoryMode = TerritoryMode.Exclusive
            });

            if (!response.IsValid)
                return TestResult.Failed($"Expected valid (outside exclusive territory), got invalid: {response.ViolationReason}");

            return TestResult.Successful("Exclusive territory validation passed (location outside territory)");
        }, "Validate territory - exclusive pass");

    private static async Task<TestResult> TestValidateTerritoryExclusiveFail(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "TERR_EX_FAIL");

            // Create parent territory
            var territoryLocation = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"TERR_PARENT_{DateTime.Now.Ticks}",
                Name = "Territory Parent",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            // Create child location inside the territory
            var insideLocation = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"INSIDE_LOC_{DateTime.Now.Ticks}",
                Name = "Inside Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY,
                ParentLocationId = territoryLocation.LocationId
            });

            // Validate: exclusive mode should FAIL because location IS inside territory
            var response = await locationClient.ValidateTerritoryAsync(new ValidateTerritoryRequest
            {
                LocationId = insideLocation.LocationId,
                TerritoryLocationIds = new List<Guid> { territoryLocation.LocationId },
                TerritoryMode = TerritoryMode.Exclusive
            });

            if (response.IsValid)
                return TestResult.Failed("Expected invalid (inside exclusive territory), got valid");

            if (response.MatchedTerritoryId != territoryLocation.LocationId)
                return TestResult.Failed($"Expected matched territory {territoryLocation.LocationId}, got {response.MatchedTerritoryId}");

            return TestResult.Successful($"Exclusive territory validation failed correctly: {response.ViolationReason}");
        }, "Validate territory - exclusive fail");

    private static async Task<TestResult> TestValidateTerritoryInclusivePass(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "TERR_IN_PASS");

            // Create parent territory
            var territoryLocation = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"TERR_INCL_{DateTime.Now.Ticks}",
                Name = "Inclusive Territory",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            // Create child location inside the territory
            var insideLocation = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"INCL_LOC_{DateTime.Now.Ticks}",
                Name = "Inside Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY,
                ParentLocationId = territoryLocation.LocationId
            });

            // Validate: inclusive mode should PASS because location IS inside territory
            var response = await locationClient.ValidateTerritoryAsync(new ValidateTerritoryRequest
            {
                LocationId = insideLocation.LocationId,
                TerritoryLocationIds = new List<Guid> { territoryLocation.LocationId },
                TerritoryMode = TerritoryMode.Inclusive
            });

            if (!response.IsValid)
                return TestResult.Failed($"Expected valid (inside inclusive territory), got invalid: {response.ViolationReason}");

            if (response.MatchedTerritoryId != territoryLocation.LocationId)
                return TestResult.Failed($"Expected matched territory {territoryLocation.LocationId}, got {response.MatchedTerritoryId}");

            return TestResult.Successful("Inclusive territory validation passed (location inside territory)");
        }, "Validate territory - inclusive pass");

    private static async Task<TestResult> TestValidateTerritoryInclusiveFail(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "TERR_IN_FAIL");

            // Create two unrelated locations
            var territoryLocation = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"TERR_OTHER_{DateTime.Now.Ticks}",
                Name = "Territory Zone",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            var outsideLocation = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"OUTSIDE_INCL_{DateTime.Now.Ticks}",
                Name = "Outside Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY
                // No parent - not related to territory
            });

            // Validate: inclusive mode should FAIL because location is NOT inside territory
            var response = await locationClient.ValidateTerritoryAsync(new ValidateTerritoryRequest
            {
                LocationId = outsideLocation.LocationId,
                TerritoryLocationIds = new List<Guid> { territoryLocation.LocationId },
                TerritoryMode = TerritoryMode.Inclusive
            });

            if (response.IsValid)
                return TestResult.Failed("Expected invalid (outside inclusive territory), got valid");

            return TestResult.Successful($"Inclusive territory validation failed correctly: {response.ViolationReason}");
        }, "Validate territory - inclusive fail");

    private static async Task<TestResult> TestValidateTerritoryNotFound(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(
            async () =>
            {
                var locationClient = GetServiceClient<ILocationClient>();
                await locationClient.ValidateTerritoryAsync(new ValidateTerritoryRequest
                {
                    LocationId = Guid.NewGuid(), // Non-existent location
                    TerritoryLocationIds = new List<Guid> { Guid.NewGuid() }
                });
            },
            404,
            "Validate territory - not found");

    private static async Task<TestResult> TestGetNonExistentLocation(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var locationClient = GetServiceClient<ILocationClient>();
                await locationClient.GetLocationAsync(new GetLocationRequest
                {
                    LocationId = Guid.NewGuid()
                });
            },
            404,
            "Get non-existent location");

    private static async Task<TestResult> TestDuplicateCodeConflict(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var locationClient = GetServiceClient<ILocationClient>();

            var realm = await CreateTestRealmAsync("LOC_TEST", "Location", "CONFLICT");

            var code = $"DUPLICATE_LOC_{DateTime.Now.Ticks}";

            // Create first location
            await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = code,
                Name = "First Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY
            });

            // Try to create second with same code in same realm - expect 409
            return await ExecuteExpectingStatusAsync(
                async () =>
                {
                    await locationClient.CreateLocationAsync(new CreateLocationRequest
                    {
                        Code = code,
                        Name = "Second Location",
                        RealmId = realm.RealmId,
                        LocationType = LocationType.CITY
                    });
                },
                409,
                "Duplicate code");
        }, "Duplicate code conflict");

    private static async Task<TestResult> TestSeedLocations(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();
            var locationClient = GetServiceClient<ILocationClient>();

            // Create a realm for seeding (using a unique code for the seed request)
            var realmCode = $"SEED_REALM_{DateTime.Now.Ticks}";
            await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = realmCode,
                Name = "Seed Test Realm"
            });

            var seedRequest = new SeedLocationsRequest
            {
                Locations =
                [
                    new SeedLocation
                    {
                        Code = $"SEED_REGION_{DateTime.Now.Ticks}",
                        Name = "Test Region",
                        Description = "A seeded region",
                        RealmCode = realmCode,
                        LocationType = LocationType.REGION
                    },
                    new SeedLocation
                    {
                        Code = $"SEED_CITY_{DateTime.Now.Ticks}",
                        Name = "Test City",
                        Description = "A seeded city",
                        RealmCode = realmCode,
                        LocationType = LocationType.CITY
                    }
                ],
                UpdateExisting = false
            };

            var response = await locationClient.SeedLocationsAsync(seedRequest);

            if (response.Created < 2)
                return TestResult.Failed($"Expected 2 created, got {response.Created}");

            return TestResult.Successful($"Seed completed: Created={response.Created}, Updated={response.Updated}, Skipped={response.Skipped}");
        }, "Seed locations");

    private static async Task<TestResult> TestCompleteLocationLifecycle(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realmClient = GetServiceClient<IRealmClient>();
            var locationClient = GetServiceClient<ILocationClient>();
            var testId = DateTime.Now.Ticks;

            // Step 1: Create realm
            Console.WriteLine("  Step 1: Creating realm...");
            var realm = await realmClient.CreateRealmAsync(new CreateRealmRequest
            {
                Code = $"LIFECYCLE_REALM_{testId}",
                Name = "Lifecycle Test Realm"
            });

            // Step 2: Create parent location
            Console.WriteLine("  Step 2: Creating parent location...");
            var parent = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"LIFECYCLE_PARENT_{testId}",
                Name = "Parent Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.REGION
            });

            // Step 3: Create child location
            Console.WriteLine("  Step 3: Creating child location...");
            var child = await locationClient.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"LIFECYCLE_CHILD_{testId}",
                Name = "Child Location",
                RealmId = realm.RealmId,
                LocationType = LocationType.CITY,
                ParentLocationId = parent.LocationId
            });

            if (child.Depth != 1)
                return TestResult.Failed($"Child depth should be 1, got {child.Depth}");

            // Step 4: Update child
            Console.WriteLine("  Step 4: Updating child location...");
            child = await locationClient.UpdateLocationAsync(new UpdateLocationRequest
            {
                LocationId = child.LocationId,
                Name = "Updated Child Location"
            });

            if (child.Name != "Updated Child Location")
                return TestResult.Failed("Update did not apply");

            // Step 5: Verify hierarchy
            Console.WriteLine("  Step 5: Verifying hierarchy...");
            var children = await locationClient.ListLocationsByParentAsync(new ListLocationsByParentRequest
            {
                ParentLocationId = parent.LocationId
            });
            if (children.Locations == null || !children.Locations.Any(l => l.LocationId == child.LocationId))
                return TestResult.Failed("Child not found in parent's children");

            // Step 6: Get ancestors
            Console.WriteLine("  Step 6: Getting ancestors...");
            var ancestors = await locationClient.GetLocationAncestorsAsync(new GetLocationAncestorsRequest
            {
                LocationId = child.LocationId
            });
            if (ancestors.Locations == null || ancestors.Locations.Count != 1)
                return TestResult.Failed($"Expected 1 ancestor, got {ancestors.Locations?.Count ?? 0}");

            // Step 7: Deprecate child
            Console.WriteLine("  Step 7: Deprecating child...");
            child = await locationClient.DeprecateLocationAsync(new DeprecateLocationRequest
            {
                LocationId = child.LocationId,
                Reason = "Lifecycle test"
            });

            if (!child.IsDeprecated)
                return TestResult.Failed("Child should be deprecated");

            // Step 8: Verify inactive
            Console.WriteLine("  Step 8: Verifying inactive status...");
            var exists = await locationClient.LocationExistsAsync(new LocationExistsRequest
            {
                LocationId = child.LocationId
            });
            if (!exists.Exists || exists.IsActive)
                return TestResult.Failed("Location should exist but be inactive");

            // Step 9: Delete child
            Console.WriteLine("  Step 9: Deleting child...");
            await locationClient.DeleteLocationAsync(new DeleteLocationRequest
            {
                LocationId = child.LocationId
            });

            // Step 10: Deprecate and delete parent
            Console.WriteLine("  Step 10: Cleaning up parent...");
            await locationClient.DeprecateLocationAsync(new DeprecateLocationRequest
            {
                LocationId = parent.LocationId
            });
            await locationClient.DeleteLocationAsync(new DeleteLocationRequest
            {
                LocationId = parent.LocationId
            });

            // Step 11: Verify deletion
            Console.WriteLine("  Step 11: Verifying deletion...");
            try
            {
                await locationClient.GetLocationAsync(new GetLocationRequest
                {
                    LocationId = child.LocationId
                });
                return TestResult.Failed("Child still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful("Complete location lifecycle test passed");
        }, "Complete location lifecycle");
}
