using BeyondImmersion.BannouService.RelationshipType;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for relationship type API endpoints using generated clients.
/// Tests the relationship type service APIs directly via NSwag-generated RelationshipTypeClient.
///
/// Note: RelationshipType APIs test service-to-service communication via Dapr.
/// These tests validate hierarchical type management with real datastores.
/// </summary>
public class RelationshipTypeTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            // CRUD operations
            new ServiceTest(TestCreateRelationshipType, "CreateRelationshipType", "RelationshipType", "Test relationship type creation"),
            new ServiceTest(TestGetRelationshipType, "GetRelationshipType", "RelationshipType", "Test relationship type retrieval by ID"),
            new ServiceTest(TestGetRelationshipTypeByCode, "GetRelationshipTypeByCode", "RelationshipType", "Test relationship type retrieval by code"),
            new ServiceTest(TestUpdateRelationshipType, "UpdateRelationshipType", "RelationshipType", "Test relationship type update"),
            new ServiceTest(TestDeleteRelationshipType, "DeleteRelationshipType", "RelationshipType", "Test relationship type deletion"),
            new ServiceTest(TestListRelationshipTypes, "ListRelationshipTypes", "RelationshipType", "Test listing all relationship types"),

            // Hierarchy operations
            new ServiceTest(TestCreateChildType, "CreateChildType", "RelationshipType", "Test creating a child relationship type"),
            new ServiceTest(TestGetChildTypes, "GetChildTypes", "RelationshipType", "Test getting child types"),
            new ServiceTest(TestMatchesHierarchy, "MatchesHierarchy", "RelationshipType", "Test hierarchy matching"),
            new ServiceTest(TestGetAncestors, "GetAncestors", "RelationshipType", "Test getting ancestors"),

            // Error handling
            new ServiceTest(TestGetNonExistentType, "GetNonExistentType", "RelationshipType", "Test 404 for non-existent type"),
            new ServiceTest(TestDuplicateCodeConflict, "DuplicateCodeConflict", "RelationshipType", "Test 409 for duplicate code"),
            new ServiceTest(TestDeleteTypeWithChildren, "DeleteTypeWithChildren", "RelationshipType", "Test 409 when deleting type with children"),

            // Seed operation
            new ServiceTest(TestSeedRelationshipTypes, "SeedRelationshipTypes", "RelationshipType", "Test seeding relationship types"),

            // Complete lifecycle
            new ServiceTest(TestCompleteTypeLifecycle, "CompleteTypeLifecycle", "RelationshipType", "Test complete type lifecycle with hierarchy"),
        };
    }

    private static async Task<TestResult> TestCreateRelationshipType(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            var createRequest = new CreateRelationshipTypeRequest
            {
                Code = $"TEST_{DateTime.Now.Ticks}",
                Name = "Test Relationship Type",
                Description = "A test relationship type",
                Category = "TEST",
                IsBidirectional = false
            };

            var response = await typeClient.CreateRelationshipTypeAsync(createRequest);

            if (response.RelationshipTypeId == Guid.Empty)
                return TestResult.Failed("Create returned empty ID");

            if (response.Code != createRequest.Code.ToUpperInvariant())
                return TestResult.Failed($"Code mismatch: expected '{createRequest.Code.ToUpperInvariant()}', got '{response.Code}'");

            return TestResult.Successful($"Created relationship type: ID={response.RelationshipTypeId}, Code={response.Code}");
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

    private static async Task<TestResult> TestGetRelationshipType(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create a type first
            var createRequest = new CreateRelationshipTypeRequest
            {
                Code = $"GET_TEST_{DateTime.Now.Ticks}",
                Name = "Get Test Type"
            };
            var created = await typeClient.CreateRelationshipTypeAsync(createRequest);

            // Now retrieve it
            var getRequest = new GetRelationshipTypeRequest { RelationshipTypeId = created.RelationshipTypeId };
            var response = await typeClient.GetRelationshipTypeAsync(getRequest);

            if (response.RelationshipTypeId != created.RelationshipTypeId)
                return TestResult.Failed("ID mismatch");

            if (response.Name != createRequest.Name)
                return TestResult.Failed($"Name mismatch: expected '{createRequest.Name}', got '{response.Name}'");

            return TestResult.Successful($"Retrieved type: ID={response.RelationshipTypeId}, Code={response.Code}");
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

    private static async Task<TestResult> TestGetRelationshipTypeByCode(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create a type first
            var code = $"CODE_TEST_{DateTime.Now.Ticks}";
            var createRequest = new CreateRelationshipTypeRequest
            {
                Code = code,
                Name = "Code Lookup Test"
            };
            var created = await typeClient.CreateRelationshipTypeAsync(createRequest);

            // Retrieve by code
            var getRequest = new GetRelationshipTypeByCodeRequest { Code = code };
            var response = await typeClient.GetRelationshipTypeByCodeAsync(getRequest);

            if (response.RelationshipTypeId != created.RelationshipTypeId)
                return TestResult.Failed("ID mismatch when fetching by code");

            return TestResult.Successful($"Retrieved type by code: ID={response.RelationshipTypeId}");
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

    private static async Task<TestResult> TestUpdateRelationshipType(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create a type
            var createRequest = new CreateRelationshipTypeRequest
            {
                Code = $"UPDATE_TEST_{DateTime.Now.Ticks}",
                Name = "Original Name",
                Description = "Original description"
            };
            var created = await typeClient.CreateRelationshipTypeAsync(createRequest);

            // Update it
            var updateRequest = new UpdateRelationshipTypeRequest
            {
                RelationshipTypeId = created.RelationshipTypeId,
                Name = "Updated Name",
                Description = "Updated description"
            };
            var response = await typeClient.UpdateRelationshipTypeAsync(updateRequest);

            if (response.Name != "Updated Name")
                return TestResult.Failed($"Name not updated: expected 'Updated Name', got '{response.Name}'");

            if (response.Description != "Updated description")
                return TestResult.Failed($"Description not updated");

            return TestResult.Successful($"Updated type: ID={response.RelationshipTypeId}, Name={response.Name}");
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

    private static async Task<TestResult> TestDeleteRelationshipType(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create a type
            var createRequest = new CreateRelationshipTypeRequest
            {
                Code = $"DELETE_TEST_{DateTime.Now.Ticks}",
                Name = "Delete Test Type"
            };
            var created = await typeClient.CreateRelationshipTypeAsync(createRequest);

            // Delete it
            await typeClient.DeleteRelationshipTypeAsync(new DeleteRelationshipTypeRequest
            {
                RelationshipTypeId = created.RelationshipTypeId
            });

            // Verify deletion
            try
            {
                await typeClient.GetRelationshipTypeAsync(new GetRelationshipTypeRequest
                {
                    RelationshipTypeId = created.RelationshipTypeId
                });
                return TestResult.Failed("Type still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful($"Deleted type: ID={created.RelationshipTypeId}");
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

    private static async Task<TestResult> TestListRelationshipTypes(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create some types
            var category = $"LIST_CAT_{DateTime.Now.Ticks}";
            for (int i = 0; i < 3; i++)
            {
                await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
                {
                    Code = $"LIST_TEST_{DateTime.Now.Ticks}_{i}",
                    Name = $"List Test Type {i}",
                    Category = category
                });
            }

            // List with filter
            var response = await typeClient.ListRelationshipTypesAsync(new ListRelationshipTypesRequest
            {
                Category = category
            });

            if (response.Types == null || response.Types.Count < 3)
                return TestResult.Failed($"Expected at least 3 types, got {response.Types?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Types.Count} types in category '{category}'");
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

    private static async Task<TestResult> TestCreateChildType(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create parent type
            var parentRequest = new CreateRelationshipTypeRequest
            {
                Code = $"PARENT_{DateTime.Now.Ticks}",
                Name = "Parent Type",
                Category = "FAMILY"
            };
            var parent = await typeClient.CreateRelationshipTypeAsync(parentRequest);

            // Create child type
            var childRequest = new CreateRelationshipTypeRequest
            {
                Code = $"CHILD_{DateTime.Now.Ticks}",
                Name = "Child Type",
                Category = "FAMILY",
                ParentTypeId = parent.RelationshipTypeId
            };
            var child = await typeClient.CreateRelationshipTypeAsync(childRequest);

            if (child.ParentTypeId != parent.RelationshipTypeId)
                return TestResult.Failed($"Parent ID mismatch: expected '{parent.RelationshipTypeId}', got '{child.ParentTypeId}'");

            if (child.Depth != 1)
                return TestResult.Failed($"Depth should be 1 for child, got {child.Depth}");

            return TestResult.Successful($"Created child type: ID={child.RelationshipTypeId}, ParentID={child.ParentTypeId}, Depth={child.Depth}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Create child failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetChildTypes(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create parent
            var parent = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"GETCHILD_PARENT_{DateTime.Now.Ticks}",
                Name = "Get Children Parent"
            });

            // Create children
            for (int i = 0; i < 3; i++)
            {
                await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
                {
                    Code = $"GETCHILD_CHILD_{DateTime.Now.Ticks}_{i}",
                    Name = $"Child {i}",
                    ParentTypeId = parent.RelationshipTypeId
                });
            }

            // Get children
            var response = await typeClient.GetChildRelationshipTypesAsync(new GetChildRelationshipTypesRequest
            {
                ParentTypeId = parent.RelationshipTypeId
            });

            if (response.Types == null || response.Types.Count < 3)
                return TestResult.Failed($"Expected 3 children, got {response.Types?.Count ?? 0}");

            return TestResult.Successful($"Retrieved {response.Types.Count} child types");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Get children failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestMatchesHierarchy(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create hierarchy: grandparent -> parent -> child
            var grandparent = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"HIERARCHY_GRAND_{DateTime.Now.Ticks}",
                Name = "Grandparent"
            });

            var parent = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"HIERARCHY_PARENT_{DateTime.Now.Ticks}",
                Name = "Parent",
                ParentTypeId = grandparent.RelationshipTypeId
            });

            var child = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"HIERARCHY_CHILD_{DateTime.Now.Ticks}",
                Name = "Child",
                ParentTypeId = parent.RelationshipTypeId
            });

            // Test child matches grandparent
            var response = await typeClient.MatchesHierarchyAsync(new MatchesHierarchyRequest
            {
                TypeId = child.RelationshipTypeId,
                AncestorTypeId = grandparent.RelationshipTypeId
            });

            if (!response.Matches)
                return TestResult.Failed("Child should match grandparent in hierarchy");

            if (response.Depth != 2)
                return TestResult.Failed($"Depth should be 2, got {response.Depth}");

            // Test self-match
            var selfMatch = await typeClient.MatchesHierarchyAsync(new MatchesHierarchyRequest
            {
                TypeId = child.RelationshipTypeId,
                AncestorTypeId = child.RelationshipTypeId
            });

            if (!selfMatch.Matches || selfMatch.Depth != 0)
                return TestResult.Failed("Self-match should return true with depth 0");

            return TestResult.Successful($"Hierarchy matching works: child→grandparent depth={response.Depth}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Hierarchy match failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetAncestors(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create hierarchy
            var root = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"ANCESTOR_ROOT_{DateTime.Now.Ticks}",
                Name = "Root"
            });

            var mid = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"ANCESTOR_MID_{DateTime.Now.Ticks}",
                Name = "Mid",
                ParentTypeId = root.RelationshipTypeId
            });

            var leaf = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"ANCESTOR_LEAF_{DateTime.Now.Ticks}",
                Name = "Leaf",
                ParentTypeId = mid.RelationshipTypeId
            });

            // Get ancestors of leaf
            var response = await typeClient.GetAncestorsAsync(new GetAncestorsRequest
            {
                TypeId = leaf.RelationshipTypeId
            });

            if (response.Types == null || response.Types.Count != 2)
                return TestResult.Failed($"Expected 2 ancestors, got {response.Types?.Count ?? 0}");

            return TestResult.Successful($"Retrieved {response.Types.Count} ancestors for leaf type");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Get ancestors failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetNonExistentType(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            try
            {
                await typeClient.GetRelationshipTypeAsync(new GetRelationshipTypeRequest
                {
                    RelationshipTypeId = Guid.NewGuid()
                });
                return TestResult.Failed("Expected 404 for non-existent type");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Correctly returned 404 for non-existent type");
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
            var typeClient = new RelationshipTypeClient();

            var code = $"DUPLICATE_{DateTime.Now.Ticks}";

            // Create first type
            await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = code,
                Name = "First Type"
            });

            // Try to create second with same code
            try
            {
                await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
                {
                    Code = code,
                    Name = "Second Type"
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

    private static async Task<TestResult> TestDeleteTypeWithChildren(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            // Create parent with child
            var parent = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"DEL_PARENT_{DateTime.Now.Ticks}",
                Name = "Parent to Delete"
            });

            await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"DEL_CHILD_{DateTime.Now.Ticks}",
                Name = "Child",
                ParentTypeId = parent.RelationshipTypeId
            });

            // Try to delete parent
            try
            {
                await typeClient.DeleteRelationshipTypeAsync(new DeleteRelationshipTypeRequest
                {
                    RelationshipTypeId = parent.RelationshipTypeId
                });
                return TestResult.Failed("Expected 409 when deleting type with children");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                return TestResult.Successful("Correctly returned 409 when deleting type with children");
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

    private static async Task<TestResult> TestSeedRelationshipTypes(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();

            var seedRequest = new SeedRelationshipTypesRequest
            {
                Types = new List<SeedRelationshipType>
                {
                    new SeedRelationshipType
                    {
                        Code = $"SEED_ROOT_{DateTime.Now.Ticks}",
                        Name = "Seed Root",
                        Category = "SEED_TEST"
                    },
                    new SeedRelationshipType
                    {
                        Code = $"SEED_CHILD_{DateTime.Now.Ticks}",
                        Name = "Seed Child",
                        Category = "SEED_TEST",
                        ParentTypeCode = $"SEED_ROOT_{DateTime.Now.Ticks}"
                    }
                },
                UpdateExisting = false
            };

            var response = await typeClient.SeedRelationshipTypesAsync(seedRequest);

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

    private static async Task<TestResult> TestCompleteTypeLifecycle(ITestClient client, string[] args)
    {
        try
        {
            var typeClient = new RelationshipTypeClient();
            var testId = DateTime.Now.Ticks;

            // Step 1: Create root type
            Console.WriteLine("  Step 1: Creating root type...");
            var root = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"LIFECYCLE_ROOT_{testId}",
                Name = "Lifecycle Root",
                Category = "LIFECYCLE_TEST"
            });

            // Step 2: Create child type
            Console.WriteLine("  Step 2: Creating child type...");
            var child = await typeClient.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = $"LIFECYCLE_CHILD_{testId}",
                Name = "Lifecycle Child",
                ParentTypeId = root.RelationshipTypeId
            });

            // Step 3: Update child
            Console.WriteLine("  Step 3: Updating child type...");
            child = await typeClient.UpdateRelationshipTypeAsync(new UpdateRelationshipTypeRequest
            {
                RelationshipTypeId = child.RelationshipTypeId,
                Name = "Updated Lifecycle Child"
            });

            // Step 4: Verify hierarchy
            Console.WriteLine("  Step 4: Verifying hierarchy...");
            var matchResult = await typeClient.MatchesHierarchyAsync(new MatchesHierarchyRequest
            {
                TypeId = child.RelationshipTypeId,
                AncestorTypeId = root.RelationshipTypeId
            });
            if (!matchResult.Matches)
                return TestResult.Failed("Hierarchy verification failed");

            // Step 5: Get ancestors
            Console.WriteLine("  Step 5: Getting ancestors...");
            var ancestors = await typeClient.GetAncestorsAsync(new GetAncestorsRequest
            {
                TypeId = child.RelationshipTypeId
            });
            if (ancestors.Types?.Count != 1)
                return TestResult.Failed($"Expected 1 ancestor, got {ancestors.Types?.Count}");

            // Step 6: Delete child first (required order)
            Console.WriteLine("  Step 6: Deleting child type...");
            await typeClient.DeleteRelationshipTypeAsync(new DeleteRelationshipTypeRequest
            {
                RelationshipTypeId = child.RelationshipTypeId
            });

            // Step 7: Delete root
            Console.WriteLine("  Step 7: Deleting root type...");
            await typeClient.DeleteRelationshipTypeAsync(new DeleteRelationshipTypeRequest
            {
                RelationshipTypeId = root.RelationshipTypeId
            });

            // Step 8: Verify deletion
            Console.WriteLine("  Step 8: Verifying deletion...");
            try
            {
                await typeClient.GetRelationshipTypeAsync(new GetRelationshipTypeRequest
                {
                    RelationshipTypeId = root.RelationshipTypeId
                });
                return TestResult.Failed("Root still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful("Complete relationship type lifecycle test passed");
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
