using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for relationship service API endpoints.
/// Tests the relationship service APIs through the Connect service WebSocket binary protocol.
///
/// Note: Relationship create/update/terminate APIs require admin role,
/// so these tests use Program.AdminClient which is already connected with admin permissions.
/// </summary>
public class RelationshipWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestCreateAndGetRelationshipViaWebSocket, "Relationship - Create and Get (WebSocket)", "WebSocket",
                "Test relationship creation and retrieval via WebSocket binary protocol"),
            new ServiceTest(TestListRelationshipsByEntityViaWebSocket, "Relationship - List by Entity (WebSocket)", "WebSocket",
                "Test listing relationships for an entity via WebSocket binary protocol"),
            new ServiceTest(TestRelationshipLifecycleViaWebSocket, "Relationship - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete relationship lifecycle via WebSocket: create -> update -> end"),
        };
    }

    private void TestCreateAndGetRelationshipViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship/create and /relationship/get via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
                    return false;
                }

                var entity1Id = Guid.NewGuid();
                var entity2Id = Guid.NewGuid();
                var relationshipTypeId = Guid.NewGuid();

                try
                {
                    // Create relationship
                    Console.WriteLine("   Invoking /relationship/create...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship/create",
                        new
                        {
                            entity1Id = entity1Id.ToString(),
                            entity1Type = "CHARACTER",
                            entity2Id = entity2Id.ToString(),
                            entity2Type = "CHARACTER",
                            relationshipTypeId = relationshipTypeId.ToString(),
                            startedAt = DateTimeOffset.UtcNow
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var relationshipIdStr = createJson?["relationshipId"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(relationshipIdStr))
                    {
                        Console.WriteLine("   Failed to create relationship - no relationshipId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created relationship: {relationshipIdStr}");

                    // Now retrieve it
                    Console.WriteLine("   Invoking /relationship/get...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship/get",
                        new { relationshipId = relationshipIdStr },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var retrievedId = getJson?["relationshipId"]?.GetValue<string>();
                    var retrievedEntity1 = getJson?["entity1Id"]?.GetValue<string>();

                    Console.WriteLine($"   Retrieved relationship: {retrievedId}");

                    return retrievedId == relationshipIdStr && retrievedEntity1 == entity1Id.ToString();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Relationship create and get test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Relationship create and get test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Relationship create and get test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestListRelationshipsByEntityViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship List by Entity Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship/list-by-entity via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
                    return false;
                }

                // Create a relationship first
                var entityId = Guid.NewGuid();

                try
                {
                    Console.WriteLine("   Creating test relationship...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship/create",
                        new
                        {
                            entity1Id = entityId.ToString(),
                            entity1Type = "CHARACTER",
                            entity2Id = Guid.NewGuid().ToString(),
                            entity2Type = "NPC",
                            relationshipTypeId = Guid.NewGuid().ToString(),
                            startedAt = DateTimeOffset.UtcNow
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    // Now list by entity
                    Console.WriteLine("   Invoking /relationship/list-by-entity...");
                    var listResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship/list-by-entity",
                        new
                        {
                            entityId = entityId.ToString(),
                            entityType = "CHARACTER",
                            includeEnded = true
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var listJson = JsonNode.Parse(listResponse.GetRawText())?.AsObject();
                    var hasRelationshipsArray = listJson?["relationships"] != null &&
                                                listJson["relationships"] is JsonArray;
                    var totalCount = listJson?["totalCount"]?.GetValue<int>() ?? 0;

                    Console.WriteLine($"   Relationships array present: {hasRelationshipsArray}");
                    Console.WriteLine($"   Total Count: {totalCount}");

                    return hasRelationshipsArray && totalCount >= 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Relationship list by entity test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Relationship list by entity test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Relationship list by entity test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRelationshipLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete relationship lifecycle via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
                    return false;
                }

                try
                {
                    // Step 1: Create relationship
                    Console.WriteLine("   Step 1: Creating relationship...");
                    var entity1Id = Guid.NewGuid();
                    var entity2Id = Guid.NewGuid();

                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship/create",
                        new
                        {
                            entity1Id = entity1Id.ToString(),
                            entity1Type = "CHARACTER",
                            entity2Id = entity2Id.ToString(),
                            entity2Type = "CHARACTER",
                            relationshipTypeId = Guid.NewGuid().ToString(),
                            startedAt = DateTimeOffset.UtcNow,
                            metadata = new Dictionary<string, object> { { "testKey", "testValue" } }
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var relationshipIdStr = createJson?["relationshipId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(relationshipIdStr))
                    {
                        Console.WriteLine("   Failed to create relationship - no relationshipId in response");
                        return false;
                    }
                    Console.WriteLine($"   Created relationship {relationshipIdStr}");

                    // Step 2: Update relationship
                    Console.WriteLine("   Step 2: Updating relationship...");
                    var updateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship/update",
                        new
                        {
                            relationshipId = relationshipIdStr,
                            metadata = new Dictionary<string, object> { { "testKey", "updatedValue" }, { "newKey", "newValue" } }
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var updateJson = JsonNode.Parse(updateResponse.GetRawText())?.AsObject();
                    var updatedId = updateJson?["relationshipId"]?.GetValue<string>();
                    if (updatedId != relationshipIdStr)
                    {
                        Console.WriteLine($"   Failed to update relationship - returned id: {updatedId}");
                        return false;
                    }
                    Console.WriteLine($"   Updated relationship metadata");

                    // Step 3: End relationship (soft delete)
                    Console.WriteLine("   Step 3: Ending relationship...");
                    (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship/end",
                        new
                        {
                            relationshipId = relationshipIdStr,
                            reason = "WebSocket lifecycle test"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();
                    Console.WriteLine($"   Ended relationship successfully");

                    // Step 4: Verify relationship has endedAt set
                    Console.WriteLine("   Step 4: Verifying relationship ended...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship/get",
                        new { relationshipId = relationshipIdStr },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var hasEndedAt = getJson?["endedAt"] != null &&
                                    getJson["endedAt"]?.GetValueKind() != JsonValueKind.Null;

                    if (!hasEndedAt)
                    {
                        Console.WriteLine("   Failed to verify relationship ended - no endedAt");
                        return false;
                    }
                    Console.WriteLine($"   Relationship has endedAt timestamp");

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Lifecycle test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("✅ Relationship complete lifecycle test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Relationship complete lifecycle test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Relationship lifecycle test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}
