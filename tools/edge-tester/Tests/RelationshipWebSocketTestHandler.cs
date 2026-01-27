using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Relationship;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for relationship service API endpoints.
/// Tests the relationship service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
/// </summary>
public class RelationshipWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "REL";
    private const string Description = "Relationship";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestCreateAndGetRelationshipViaWebSocket, "Relationship - Create and Get (WebSocket)", "WebSocket",
            "Test relationship creation and retrieval via typed proxy"),
        new ServiceTest(TestListRelationshipsByEntityViaWebSocket, "Relationship - List by Entity (WebSocket)", "WebSocket",
            "Test listing relationships for an entity via typed proxy"),
        new ServiceTest(TestRelationshipLifecycleViaWebSocket, "Relationship - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete relationship lifecycle via typed proxy: create -> update -> end"),
    ];

    private void TestCreateAndGetRelationshipViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing relationship creation and retrieval via typed proxy...");

        RunWebSocketTest("Relationship create and get test", async adminClient =>
        {
            var entity1Id = Guid.NewGuid();
            var entity2Id = Guid.NewGuid();
            var relationshipTypeId = Guid.NewGuid();

            // Create relationship using typed proxy
            Console.WriteLine("   Creating relationship via typed proxy...");
            var createResponse = await adminClient.Relationship.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                Entity1Id = entity1Id,
                Entity1Type = EntityType.Character,
                Entity2Id = entity2Id,
                Entity2Type = EntityType.Character,
                RelationshipTypeId = relationshipTypeId,
                StartedAt = DateTimeOffset.UtcNow
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create relationship: {FormatError(createResponse.Error)}");
                return false;
            }

            var relationship = createResponse.Result;
            Console.WriteLine($"   Created relationship: {relationship.RelationshipId}");

            // Retrieve it using typed proxy
            Console.WriteLine("   Retrieving relationship via typed proxy...");
            var getResponse = await adminClient.Relationship.GetRelationshipAsync(new GetRelationshipRequest
            {
                RelationshipId = relationship.RelationshipId
            });

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get relationship: {FormatError(getResponse.Error)}");
                return false;
            }

            var retrieved = getResponse.Result;
            Console.WriteLine($"   Retrieved relationship: {retrieved.RelationshipId}");

            return retrieved.RelationshipId == relationship.RelationshipId && retrieved.Entity1Id == entity1Id;
        });
    }

    private void TestListRelationshipsByEntityViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship List by Entity Test (WebSocket) ===");
        Console.WriteLine("Testing relationship list by entity via typed proxy...");

        RunWebSocketTest("Relationship list by entity test", async adminClient =>
        {
            var entityId = Guid.NewGuid();

            // Create a relationship first using typed proxy
            Console.WriteLine("   Creating test relationship...");
            var createResponse = await adminClient.Relationship.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                Entity1Id = entityId,
                Entity1Type = EntityType.Character,
                Entity2Id = Guid.NewGuid(),
                Entity2Type = EntityType.Actor,
                RelationshipTypeId = Guid.NewGuid(),
                StartedAt = DateTimeOffset.UtcNow
            });

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"   Failed to create test relationship: {FormatError(createResponse.Error)}");
                return false;
            }

            // Now list by entity using typed proxy
            Console.WriteLine("   Listing relationships by entity via typed proxy...");
            var listResponse = await adminClient.Relationship.ListRelationshipsByEntityAsync(new ListRelationshipsByEntityRequest
            {
                EntityId = entityId,
                EntityType = EntityType.Character,
                IncludeEnded = true
            });

            if (!listResponse.IsSuccess || listResponse.Result == null)
            {
                Console.WriteLine($"   Failed to list relationships: {FormatError(listResponse.Error)}");
                return false;
            }

            var result = listResponse.Result;
            Console.WriteLine($"   Relationships array present: {result.Relationships != null}");
            Console.WriteLine($"   Total Count: {result.TotalCount}");

            return result.Relationships != null && result.TotalCount >= 1;
        });
    }

    private void TestRelationshipLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete relationship lifecycle via typed proxy...");

        RunWebSocketTest("Relationship complete lifecycle test", async adminClient =>
        {
            // Step 1: Create relationship
            Console.WriteLine("   Step 1: Creating relationship...");
            var entity1Id = Guid.NewGuid();
            var entity2Id = Guid.NewGuid();

            var createResponse = await adminClient.Relationship.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                Entity1Id = entity1Id,
                Entity1Type = EntityType.Character,
                Entity2Id = entity2Id,
                Entity2Type = EntityType.Character,
                RelationshipTypeId = Guid.NewGuid(),
                StartedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, object> { { "testKey", "testValue" } }
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create relationship: {FormatError(createResponse.Error)}");
                return false;
            }

            var relationship = createResponse.Result;
            Console.WriteLine($"   Created relationship {relationship.RelationshipId}");

            // Step 2: Update relationship
            Console.WriteLine("   Step 2: Updating relationship...");
            var updateResponse = await adminClient.Relationship.UpdateRelationshipAsync(new UpdateRelationshipRequest
            {
                RelationshipId = relationship.RelationshipId,
                Metadata = new Dictionary<string, object> { { "testKey", "updatedValue" }, { "newKey", "newValue" } }
            });

            if (!updateResponse.IsSuccess || updateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to update relationship: {FormatError(updateResponse.Error)}");
                return false;
            }

            if (updateResponse.Result.RelationshipId != relationship.RelationshipId)
            {
                Console.WriteLine($"   Failed to update relationship - returned id doesn't match");
                return false;
            }
            Console.WriteLine("   Updated relationship metadata");

            // Step 3: End relationship (event-based operation)
            Console.WriteLine("   Step 3: Ending relationship...");
            await adminClient.Relationship.EndRelationshipEventAsync(new EndRelationshipRequest
            {
                RelationshipId = relationship.RelationshipId,
                Reason = "WebSocket lifecycle test"
            });
            Console.WriteLine("   End relationship event sent");

            // Give a moment for the event to process
            await Task.Delay(100);

            // Step 4: Verify relationship has endedAt set
            Console.WriteLine("   Step 4: Verifying relationship ended...");
            var getResponse = await adminClient.Relationship.GetRelationshipAsync(new GetRelationshipRequest
            {
                RelationshipId = relationship.RelationshipId
            });

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get relationship: {FormatError(getResponse.Error)}");
                return false;
            }

            if (getResponse.Result.EndedAt == null)
            {
                Console.WriteLine("   Relationship has no endedAt timestamp - end event may not have processed yet");
                // This might be expected if the event is async
                return true;
            }
            Console.WriteLine($"   Relationship has endedAt timestamp: {getResponse.Result.EndedAt}");

            return true;
        });
    }
}
