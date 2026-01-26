using System.Text.Json;
using BeyondImmersion.Bannou.Client;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for relationship type service API endpoints.
/// Tests the relationship type service APIs through the Connect service WebSocket binary protocol.
/// </summary>
public class RelationshipTypeWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "RELTYPE";
    private const string Description = "RelationshipType";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListRelationshipTypesViaWebSocket, "RelationshipType - List (WebSocket)", "WebSocket",
            "Test relationship type listing via WebSocket binary protocol"),
        new ServiceTest(TestCreateAndGetRelationshipTypeViaWebSocket, "RelationshipType - Create and Get (WebSocket)", "WebSocket",
            "Test relationship type creation and retrieval via WebSocket binary protocol"),
        new ServiceTest(TestRelationshipTypeHierarchyViaWebSocket, "RelationshipType - Hierarchy (WebSocket)", "WebSocket",
            "Test relationship type hierarchy operations via WebSocket binary protocol"),
        new ServiceTest(TestRelationshipTypeLifecycleViaWebSocket, "RelationshipType - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete relationship type lifecycle via WebSocket: create -> update -> deprecate -> undeprecate"),
    ];

    private void TestListRelationshipTypesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType List Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship-type/list via shared admin WebSocket...");

        RunWebSocketTest("RelationshipType list test", async adminClient =>
        {
            var response = await InvokeApiAsync(adminClient, "/relationship-type/list", new { });

            var hasTypesArray = HasArrayProperty(response, "types");
            var totalCount = GetIntProperty(response, "totalCount");

            Console.WriteLine($"   Types array present: {hasTypesArray}");
            Console.WriteLine($"   Total Count: {totalCount}");

            return hasTypesArray;
        });
    }

    private void TestCreateAndGetRelationshipTypeViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship-type/create and /relationship-type/get via shared admin WebSocket...");

        RunWebSocketTest("RelationshipType create and get test", async adminClient =>
        {
            var uniqueCode = $"TYPE{GenerateUniqueCode()}";

            // Create relationship type
            Console.WriteLine("   Invoking /relationship-type/create...");
            var createResponse = await InvokeApiAsync(adminClient, "/relationship-type/create", new
            {
                code = uniqueCode,
                name = $"Test Type {uniqueCode}",
                description = "Created via WebSocket edge test",
                category = "TEST",
                isBidirectional = true
            });

            var typeId = GetStringProperty(createResponse, "relationshipTypeId");
            if (string.IsNullOrEmpty(typeId))
            {
                Console.WriteLine("   Failed to create relationship type - no relationshipTypeId in response");
                return false;
            }

            Console.WriteLine($"   Created relationship type: {typeId} ({GetStringProperty(createResponse, "code")})");

            // Retrieve it
            Console.WriteLine("   Invoking /relationship-type/get...");
            var getResponse = await InvokeApiAsync(adminClient, "/relationship-type/get", new { relationshipTypeId = typeId });

            var retrievedId = GetStringProperty(getResponse, "relationshipTypeId");
            var retrievedCode = GetStringProperty(getResponse, "code");

            Console.WriteLine($"   Retrieved relationship type: {retrievedId} ({retrievedCode})");

            return retrievedId == typeId && retrievedCode == uniqueCode;
        });
    }

    private void TestRelationshipTypeHierarchyViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Hierarchy Test (WebSocket) ===");
        Console.WriteLine("Testing hierarchy operations via shared admin WebSocket...");

        RunWebSocketTest("RelationshipType hierarchy test", async adminClient =>
        {
            // Step 1: Create parent type
            Console.WriteLine("   Step 1: Creating parent type...");
            var parentCode = $"PARENT{GenerateUniqueCode()}";

            var parentResponse = await InvokeApiAsync(adminClient, "/relationship-type/create", new
            {
                code = parentCode,
                name = $"Parent Type {parentCode}",
                category = "FAMILY",
                isBidirectional = false
            });

            var parentId = GetStringProperty(parentResponse, "relationshipTypeId");
            if (string.IsNullOrEmpty(parentId))
            {
                Console.WriteLine("   Failed to create parent type");
                return false;
            }
            Console.WriteLine($"   Created parent type {parentId}");

            // Step 2: Create child type with parent
            Console.WriteLine("   Step 2: Creating child type...");
            var childCode = $"CHILD{GenerateUniqueCode()}";

            var childResponse = await InvokeApiAsync(adminClient, "/relationship-type/create", new
            {
                code = childCode,
                name = $"Child Type {childCode}",
                category = "FAMILY",
                parentTypeId = parentId,
                isBidirectional = false
            });

            var childId = GetStringProperty(childResponse, "relationshipTypeId");
            if (string.IsNullOrEmpty(childId))
            {
                Console.WriteLine("   Failed to create child type");
                return false;
            }
            Console.WriteLine($"   Created child type {childId}");

            // Step 3: Test GetChildRelationshipTypes
            Console.WriteLine("   Step 3: Getting child types...");
            var childrenResponse = await InvokeApiAsync(adminClient, "/relationship-type/get-children", new { parentTypeId = parentId });

            var typesArray = childrenResponse?["types"]?.AsArray();
            var hasChildren = typesArray != null && typesArray.Count > 0;

            if (!hasChildren)
            {
                Console.WriteLine("   No children found for parent type");
                return false;
            }
            Console.WriteLine($"   Found {typesArray?.Count} child type(s)");

            // Step 4: Test MatchesHierarchy
            Console.WriteLine("   Step 4: Testing hierarchy match...");
            var matchResponse = await InvokeApiAsync(adminClient, "/relationship-type/matches-hierarchy", new
            {
                typeId = childId,
                ancestorTypeId = parentId
            });

            var matches = matchResponse?["matches"]?.GetValue<bool>() ?? false;
            if (!matches)
            {
                Console.WriteLine("   Child type does not match parent hierarchy");
                return false;
            }
            Console.WriteLine("   Child type matches parent hierarchy");

            // Step 5: Test GetAncestors
            Console.WriteLine("   Step 5: Getting ancestors...");
            var ancestorsResponse = await InvokeApiAsync(adminClient, "/relationship-type/get-ancestors", new { typeId = childId });

            var ancestorsArray = ancestorsResponse?["types"]?.AsArray();
            var hasAncestors = ancestorsArray != null && ancestorsArray.Count > 0;

            if (!hasAncestors)
            {
                Console.WriteLine("   No ancestors found for child type");
                return false;
            }
            Console.WriteLine($"   Found {ancestorsArray?.Count} ancestor(s)");

            return true;
        });
    }

    private void TestRelationshipTypeLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete relationship type lifecycle via shared admin WebSocket...");

        RunWebSocketTest("RelationshipType complete lifecycle test", async adminClient =>
        {
            // Step 1: Create type
            Console.WriteLine("   Step 1: Creating relationship type...");
            var uniqueCode = $"LIFE{GenerateUniqueCode()}";

            var createResponse = await InvokeApiAsync(adminClient, "/relationship-type/create", new
            {
                code = uniqueCode,
                name = $"Lifecycle Test {uniqueCode}",
                description = "Lifecycle test type",
                category = "TEST",
                isBidirectional = true
            });

            var typeId = GetStringProperty(createResponse, "relationshipTypeId");
            if (string.IsNullOrEmpty(typeId))
            {
                Console.WriteLine("   Failed to create relationship type - no relationshipTypeId in response");
                return false;
            }
            Console.WriteLine($"   Created relationship type {typeId}");

            // Step 2: Update type
            Console.WriteLine("   Step 2: Updating relationship type...");
            var updateResponse = await InvokeApiAsync(adminClient, "/relationship-type/update", new
            {
                relationshipTypeId = typeId,
                name = $"Updated Lifecycle Test {uniqueCode}",
                description = "Updated description"
            });

            var updatedName = GetStringProperty(updateResponse, "name");
            if (!updatedName?.StartsWith("Updated") ?? true)
            {
                Console.WriteLine($"   Failed to update relationship type - name: {updatedName}");
                return false;
            }
            Console.WriteLine($"   Updated relationship type name to: {updatedName}");

            // Step 3: Deprecate type
            Console.WriteLine("   Step 3: Deprecating relationship type...");
            var deprecateResponse = await InvokeApiAsync(adminClient, "/relationship-type/deprecate", new
            {
                relationshipTypeId = typeId,
                reason = "WebSocket lifecycle test"
            });

            var isDeprecated = deprecateResponse?["isDeprecated"]?.GetValue<bool>() ?? false;
            if (!isDeprecated)
            {
                Console.WriteLine("   Failed to deprecate relationship type");
                return false;
            }
            Console.WriteLine("   Relationship type deprecated successfully");

            // Step 4: Undeprecate type
            Console.WriteLine("   Step 4: Undeprecating relationship type...");
            var undeprecateResponse = await InvokeApiAsync(adminClient, "/relationship-type/undeprecate", new { relationshipTypeId = typeId });

            var isUndeprecated = !(undeprecateResponse?["isDeprecated"]?.GetValue<bool>() ?? true);
            if (!isUndeprecated)
            {
                Console.WriteLine("   Failed to undeprecate relationship type");
                return false;
            }
            Console.WriteLine("   Relationship type restored successfully");

            return true;
        });
    }
}
