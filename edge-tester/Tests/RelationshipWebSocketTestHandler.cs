using System.Text.Json;
using BeyondImmersion.Bannou.Client;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for relationship service API endpoints.
/// Tests the relationship service APIs through the Connect service WebSocket binary protocol.
/// </summary>
public class RelationshipWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "REL";
    private const string Description = "Relationship";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestCreateAndGetRelationshipViaWebSocket, "Relationship - Create and Get (WebSocket)", "WebSocket",
            "Test relationship creation and retrieval via WebSocket binary protocol"),
        new ServiceTest(TestListRelationshipsByEntityViaWebSocket, "Relationship - List by Entity (WebSocket)", "WebSocket",
            "Test listing relationships for an entity via WebSocket binary protocol"),
        new ServiceTest(TestRelationshipLifecycleViaWebSocket, "Relationship - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete relationship lifecycle via WebSocket: create -> update -> end"),
    ];

    private void TestCreateAndGetRelationshipViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship/create and /relationship/get via shared admin WebSocket...");

        RunWebSocketTest("Relationship create and get test", async adminClient =>
        {
            var entity1Id = Guid.NewGuid();
            var entity2Id = Guid.NewGuid();
            var relationshipTypeId = Guid.NewGuid();

            // Create relationship
            Console.WriteLine("   Invoking /relationship/create...");
            var createResponse = await InvokeApiAsync(adminClient, "/relationship/create", new
            {
                entity1Id = entity1Id.ToString(),
                entity1Type = "CHARACTER",
                entity2Id = entity2Id.ToString(),
                entity2Type = "CHARACTER",
                relationshipTypeId = relationshipTypeId.ToString(),
                startedAt = DateTimeOffset.UtcNow
            });

            var relationshipId = GetStringProperty(createResponse, "relationshipId");
            if (string.IsNullOrEmpty(relationshipId))
            {
                Console.WriteLine("   Failed to create relationship - no relationshipId in response");
                return false;
            }

            Console.WriteLine($"   Created relationship: {relationshipId}");

            // Retrieve it
            Console.WriteLine("   Invoking /relationship/get...");
            var getResponse = await InvokeApiAsync(adminClient, "/relationship/get", new { relationshipId });

            var retrievedId = GetStringProperty(getResponse, "relationshipId");
            var retrievedEntity1 = GetStringProperty(getResponse, "entity1Id");

            Console.WriteLine($"   Retrieved relationship: {retrievedId}");

            return retrievedId == relationshipId && retrievedEntity1 == entity1Id.ToString();
        });
    }

    private void TestListRelationshipsByEntityViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship List by Entity Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship/list-by-entity via shared admin WebSocket...");

        RunWebSocketTest("Relationship list by entity test", async adminClient =>
        {
            var entityId = Guid.NewGuid();

            // Create a relationship first
            Console.WriteLine("   Creating test relationship...");
            await InvokeApiAsync(adminClient, "/relationship/create", new
            {
                entity1Id = entityId.ToString(),
                entity1Type = "CHARACTER",
                entity2Id = Guid.NewGuid().ToString(),
                entity2Type = "NPC",
                relationshipTypeId = Guid.NewGuid().ToString(),
                startedAt = DateTimeOffset.UtcNow
            });

            // Now list by entity
            Console.WriteLine("   Invoking /relationship/list-by-entity...");
            var listResponse = await InvokeApiAsync(adminClient, "/relationship/list-by-entity", new
            {
                entityId = entityId.ToString(),
                entityType = "CHARACTER",
                includeEnded = true
            });

            var hasRelationshipsArray = HasArrayProperty(listResponse, "relationships");
            var totalCount = GetIntProperty(listResponse, "totalCount");

            Console.WriteLine($"   Relationships array present: {hasRelationshipsArray}");
            Console.WriteLine($"   Total Count: {totalCount}");

            return hasRelationshipsArray && totalCount >= 1;
        });
    }

    private void TestRelationshipLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete relationship lifecycle via shared admin WebSocket...");

        RunWebSocketTest("Relationship complete lifecycle test", async adminClient =>
        {
            // Step 1: Create relationship
            Console.WriteLine("   Step 1: Creating relationship...");
            var entity1Id = Guid.NewGuid();
            var entity2Id = Guid.NewGuid();

            var createResponse = await InvokeApiAsync(adminClient, "/relationship/create", new
            {
                entity1Id = entity1Id.ToString(),
                entity1Type = "CHARACTER",
                entity2Id = entity2Id.ToString(),
                entity2Type = "CHARACTER",
                relationshipTypeId = Guid.NewGuid().ToString(),
                startedAt = DateTimeOffset.UtcNow,
                metadata = new Dictionary<string, object> { { "testKey", "testValue" } }
            });

            var relationshipId = GetStringProperty(createResponse, "relationshipId");
            if (string.IsNullOrEmpty(relationshipId))
            {
                Console.WriteLine("   Failed to create relationship - no relationshipId in response");
                return false;
            }
            Console.WriteLine($"   Created relationship {relationshipId}");

            // Step 2: Update relationship
            Console.WriteLine("   Step 2: Updating relationship...");
            var updateResponse = await InvokeApiAsync(adminClient, "/relationship/update", new
            {
                relationshipId,
                metadata = new Dictionary<string, object> { { "testKey", "updatedValue" }, { "newKey", "newValue" } }
            });

            var updatedId = GetStringProperty(updateResponse, "relationshipId");
            if (updatedId != relationshipId)
            {
                Console.WriteLine($"   Failed to update relationship - returned id: {updatedId}");
                return false;
            }
            Console.WriteLine("   Updated relationship metadata");

            // Step 3: End relationship
            Console.WriteLine("   Step 3: Ending relationship...");
            await InvokeApiAsync(adminClient, "/relationship/end", new
            {
                relationshipId,
                reason = "WebSocket lifecycle test"
            });
            Console.WriteLine("   Ended relationship successfully");

            // Step 4: Verify relationship has endedAt set
            Console.WriteLine("   Step 4: Verifying relationship ended...");
            var getResponse = await InvokeApiAsync(adminClient, "/relationship/get", new { relationshipId });

            var hasEndedAt = getResponse?["endedAt"] != null &&
                            getResponse["endedAt"]?.GetValueKind() != JsonValueKind.Null;

            if (!hasEndedAt)
            {
                Console.WriteLine("   Failed to verify relationship ended - no endedAt");
                return false;
            }
            Console.WriteLine("   Relationship has endedAt timestamp");

            return true;
        });
    }
}
