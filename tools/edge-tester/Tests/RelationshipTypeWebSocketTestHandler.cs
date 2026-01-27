using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.RelationshipType;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for relationship type service API endpoints.
/// Tests the relationship type service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
/// </summary>
public class RelationshipTypeWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "RELTYPE";
    private const string Description = "RelationshipType";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListRelationshipTypesViaWebSocket, "RelationshipType - List (WebSocket)", "WebSocket",
            "Test relationship type listing via typed proxy"),
        new ServiceTest(TestCreateAndGetRelationshipTypeViaWebSocket, "RelationshipType - Create and Get (WebSocket)", "WebSocket",
            "Test relationship type creation and retrieval via typed proxy"),
        new ServiceTest(TestRelationshipTypeHierarchyViaWebSocket, "RelationshipType - Hierarchy (WebSocket)", "WebSocket",
            "Test relationship type hierarchy operations via typed proxy"),
        new ServiceTest(TestRelationshipTypeLifecycleViaWebSocket, "RelationshipType - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete relationship type lifecycle via typed proxy: create -> update -> deprecate -> undeprecate"),
    ];

    private void TestListRelationshipTypesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType List Test (WebSocket) ===");
        Console.WriteLine("Testing relationship type list via typed proxy...");

        RunWebSocketTest("RelationshipType list test", async adminClient =>
        {
            var response = await adminClient.RelationshipType.ListRelationshipTypesAsync(new ListRelationshipTypesRequest());

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to list relationship types: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Types array present: {result.Types != null}");
            Console.WriteLine($"   Total Count: {result.TotalCount}");

            return result.Types != null;
        });
    }

    private void TestCreateAndGetRelationshipTypeViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing relationship type creation and retrieval via typed proxy...");

        RunWebSocketTest("RelationshipType create and get test", async adminClient =>
        {
            var uniqueCode = $"TYPE{GenerateUniqueCode()}";

            // Create relationship type using typed proxy
            Console.WriteLine("   Creating relationship type via typed proxy...");
            var createResponse = await adminClient.RelationshipType.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = uniqueCode,
                Name = $"Test Type {uniqueCode}",
                Description = "Created via WebSocket edge test",
                Category = "TEST",
                IsBidirectional = true
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create relationship type: {FormatError(createResponse.Error)}");
                return false;
            }

            var relType = createResponse.Result;
            Console.WriteLine($"   Created relationship type: {relType.RelationshipTypeId} ({relType.Code})");

            // Retrieve it using typed proxy
            Console.WriteLine("   Retrieving relationship type via typed proxy...");
            var getResponse = await adminClient.RelationshipType.GetRelationshipTypeAsync(new GetRelationshipTypeRequest
            {
                RelationshipTypeId = relType.RelationshipTypeId
            });

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get relationship type: {FormatError(getResponse.Error)}");
                return false;
            }

            var retrieved = getResponse.Result;
            Console.WriteLine($"   Retrieved relationship type: {retrieved.RelationshipTypeId} ({retrieved.Code})");

            return retrieved.RelationshipTypeId == relType.RelationshipTypeId && retrieved.Code == uniqueCode;
        });
    }

    private void TestRelationshipTypeHierarchyViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Hierarchy Test (WebSocket) ===");
        Console.WriteLine("Testing hierarchy operations via typed proxy...");

        RunWebSocketTest("RelationshipType hierarchy test", async adminClient =>
        {
            // Step 1: Create parent type
            Console.WriteLine("   Step 1: Creating parent type...");
            var parentCode = $"PARENT{GenerateUniqueCode()}";

            var parentResponse = await adminClient.RelationshipType.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = parentCode,
                Name = $"Parent Type {parentCode}",
                Category = "FAMILY",
                IsBidirectional = false
            });

            if (!parentResponse.IsSuccess || parentResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create parent type: {FormatError(parentResponse.Error)}");
                return false;
            }

            var parent = parentResponse.Result;
            Console.WriteLine($"   Created parent type {parent.RelationshipTypeId}");

            // Step 2: Create child type with parent
            Console.WriteLine("   Step 2: Creating child type...");
            var childCode = $"CHILD{GenerateUniqueCode()}";

            var childResponse = await adminClient.RelationshipType.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = childCode,
                Name = $"Child Type {childCode}",
                Category = "FAMILY",
                ParentTypeId = parent.RelationshipTypeId,
                IsBidirectional = false
            });

            if (!childResponse.IsSuccess || childResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create child type: {FormatError(childResponse.Error)}");
                return false;
            }

            var child = childResponse.Result;
            Console.WriteLine($"   Created child type {child.RelationshipTypeId}");

            // Step 3: Test GetChildRelationshipTypes
            Console.WriteLine("   Step 3: Getting child types...");
            var childrenResponse = await adminClient.RelationshipType.GetChildRelationshipTypesAsync(new GetChildRelationshipTypesRequest
            {
                ParentTypeId = parent.RelationshipTypeId
            });

            if (!childrenResponse.IsSuccess || childrenResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get children: {FormatError(childrenResponse.Error)}");
                return false;
            }

            var childCount = childrenResponse.Result.Types?.Count ?? 0;
            if (childCount == 0)
            {
                Console.WriteLine("   No children found for parent type");
                return false;
            }
            Console.WriteLine($"   Found {childCount} child type(s)");

            // Step 4: Test MatchesHierarchy
            Console.WriteLine("   Step 4: Testing hierarchy match...");
            var matchResponse = await adminClient.RelationshipType.MatchesHierarchyAsync(new MatchesHierarchyRequest
            {
                TypeId = child.RelationshipTypeId,
                AncestorTypeId = parent.RelationshipTypeId
            });

            if (!matchResponse.IsSuccess || matchResponse.Result == null)
            {
                Console.WriteLine($"   Failed to check hierarchy: {FormatError(matchResponse.Error)}");
                return false;
            }

            if (!matchResponse.Result.Matches)
            {
                Console.WriteLine("   Child type does not match parent hierarchy");
                return false;
            }
            Console.WriteLine("   Child type matches parent hierarchy");

            // Step 5: Test GetAncestors
            Console.WriteLine("   Step 5: Getting ancestors...");
            var ancestorsResponse = await adminClient.RelationshipType.GetAncestorsAsync(new GetAncestorsRequest
            {
                TypeId = child.RelationshipTypeId
            });

            if (!ancestorsResponse.IsSuccess || ancestorsResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get ancestors: {FormatError(ancestorsResponse.Error)}");
                return false;
            }

            var ancestorCount = ancestorsResponse.Result.Types?.Count ?? 0;
            if (ancestorCount == 0)
            {
                Console.WriteLine("   No ancestors found for child type");
                return false;
            }
            Console.WriteLine($"   Found {ancestorCount} ancestor(s)");

            return true;
        });
    }

    private void TestRelationshipTypeLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete relationship type lifecycle via typed proxy...");

        RunWebSocketTest("RelationshipType complete lifecycle test", async adminClient =>
        {
            // Step 1: Create type
            Console.WriteLine("   Step 1: Creating relationship type...");
            var uniqueCode = $"LIFE{GenerateUniqueCode()}";

            var createResponse = await adminClient.RelationshipType.CreateRelationshipTypeAsync(new CreateRelationshipTypeRequest
            {
                Code = uniqueCode,
                Name = $"Lifecycle Test {uniqueCode}",
                Description = "Lifecycle test type",
                Category = "TEST",
                IsBidirectional = true
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create relationship type: {FormatError(createResponse.Error)}");
                return false;
            }

            var relType = createResponse.Result;
            Console.WriteLine($"   Created relationship type {relType.RelationshipTypeId}");

            // Step 2: Update type
            Console.WriteLine("   Step 2: Updating relationship type...");
            var updateResponse = await adminClient.RelationshipType.UpdateRelationshipTypeAsync(new UpdateRelationshipTypeRequest
            {
                RelationshipTypeId = relType.RelationshipTypeId,
                Name = $"Updated Lifecycle Test {uniqueCode}",
                Description = "Updated description"
            });

            if (!updateResponse.IsSuccess || updateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to update relationship type: {FormatError(updateResponse.Error)}");
                return false;
            }

            var updated = updateResponse.Result;
            if (!updated.Name.StartsWith("Updated"))
            {
                Console.WriteLine($"   Update didn't apply - name: {updated.Name}");
                return false;
            }
            Console.WriteLine($"   Updated relationship type name to: {updated.Name}");

            // Step 3: Deprecate type
            Console.WriteLine("   Step 3: Deprecating relationship type...");
            var deprecateResponse = await adminClient.RelationshipType.DeprecateRelationshipTypeAsync(new DeprecateRelationshipTypeRequest
            {
                RelationshipTypeId = relType.RelationshipTypeId,
                Reason = "WebSocket lifecycle test"
            });

            if (!deprecateResponse.IsSuccess || deprecateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to deprecate relationship type: {FormatError(deprecateResponse.Error)}");
                return false;
            }

            if (!deprecateResponse.Result.IsDeprecated)
            {
                Console.WriteLine("   Relationship type not marked as deprecated");
                return false;
            }
            Console.WriteLine("   Relationship type deprecated successfully");

            // Step 4: Undeprecate type
            Console.WriteLine("   Step 4: Undeprecating relationship type...");
            var undeprecateResponse = await adminClient.RelationshipType.UndeprecateRelationshipTypeAsync(new UndeprecateRelationshipTypeRequest
            {
                RelationshipTypeId = relType.RelationshipTypeId
            });

            if (!undeprecateResponse.IsSuccess || undeprecateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to undeprecate relationship type: {FormatError(undeprecateResponse.Error)}");
                return false;
            }

            if (undeprecateResponse.Result.IsDeprecated)
            {
                Console.WriteLine("   Relationship type still marked as deprecated");
                return false;
            }
            Console.WriteLine("   Relationship type restored successfully");

            return true;
        });
    }
}
