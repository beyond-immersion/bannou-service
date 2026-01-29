using System.Text.Json;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.RelationshipType;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for relationship API endpoints using generated clients.
/// Tests the relationship service APIs directly via NSwag-generated RelationshipClient.
///
/// Note: Relationship APIs test service-to-service communication via mesh.
/// These tests validate entity-to-entity relationship management with composite uniqueness,
/// bidirectional support, and soft-delete capability.
/// </summary>
public class RelationshipTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // CRUD operations
        new ServiceTest(TestCreateRelationship, "CreateRelationship", "Relationship", "Test relationship creation"),
        new ServiceTest(TestGetRelationship, "GetRelationship", "Relationship", "Test relationship retrieval by ID"),
        new ServiceTest(TestUpdateRelationship, "UpdateRelationship", "Relationship", "Test relationship metadata update"),
        new ServiceTest(TestEndRelationship, "EndRelationship", "Relationship", "Test ending a relationship (soft delete)"),

        // List operations
        new ServiceTest(TestListRelationshipsByEntity, "ListRelationshipsByEntity", "Relationship", "Test listing relationships for an entity"),
        new ServiceTest(TestGetRelationshipsBetween, "GetRelationshipsBetween", "Relationship", "Test getting relationships between two entities"),
        new ServiceTest(TestListRelationshipsByType, "ListRelationshipsByType", "Relationship", "Test listing relationships by type"),

        // Error handling
        new ServiceTest(TestGetNonExistentRelationship, "GetNonExistentRelationship", "Relationship", "Test 404 for non-existent relationship"),
        new ServiceTest(TestDuplicateCompositeKeyConflict, "DuplicateCompositeKeyConflict", "Relationship", "Test 409 for duplicate composite key"),

        // Complete lifecycle
        new ServiceTest(TestCompleteRelationshipLifecycle, "CompleteRelationshipLifecycle", "Relationship", "Test complete relationship lifecycle"),
    ];

    private static async Task<TestResult> TestCreateRelationship(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var relationshipClient = GetServiceClient<IRelationshipClient>();

            // Create a relationship type first (we need a valid type ID)
            var relationshipTypeClient = GetServiceClient<IRelationshipTypeClient>();
            var typeResponse = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"REL_TEST_{DateTime.Now.Ticks}",
                    Name = "Test Relationship Type",
                    Category = "TESTING",
                    IsBidirectional = false
                });

            var createRequest = new CreateRelationshipRequest
            {
                Entity1Id = Guid.NewGuid(),
                Entity1Type = EntityType.Character,
                Entity2Id = Guid.NewGuid(),
                Entity2Type = EntityType.Actor,
                RelationshipTypeId = typeResponse.RelationshipTypeId,
                StartedAt = DateTimeOffset.UtcNow
            };

            var response = await relationshipClient.CreateRelationshipAsync(createRequest);

            if (response.RelationshipId == Guid.Empty)
                return TestResult.Failed("Create returned empty ID");

            if (response.Entity1Id != createRequest.Entity1Id)
                return TestResult.Failed($"Entity1Id mismatch: expected '{createRequest.Entity1Id}', got '{response.Entity1Id}'");

            if (response.Entity2Id != createRequest.Entity2Id)
                return TestResult.Failed($"Entity2Id mismatch: expected '{createRequest.Entity2Id}', got '{response.Entity2Id}'");

            if (response.RelationshipTypeId != createRequest.RelationshipTypeId)
                return TestResult.Failed("RelationshipTypeId mismatch");

            return TestResult.Successful($"Created relationship: ID={response.RelationshipId}, Entity1={response.Entity1Type}, Entity2={response.Entity2Type}");
        }, "Create relationship");

    private static async Task<TestResult> TestGetRelationship(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var relationshipClient = GetServiceClient<IRelationshipClient>();

            // Create a relationship type first
            var relationshipTypeClient = GetServiceClient<IRelationshipTypeClient>();
            var typeResponse = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"GET_REL_TYPE_{DateTime.Now.Ticks}",
                    Name = "Get Test Type"
                });

            // Create a relationship
            var createRequest = new CreateRelationshipRequest
            {
                Entity1Id = Guid.NewGuid(),
                Entity1Type = EntityType.Character,
                Entity2Id = Guid.NewGuid(),
                Entity2Type = EntityType.Actor,
                RelationshipTypeId = typeResponse.RelationshipTypeId,
                StartedAt = DateTimeOffset.UtcNow
            };
            var created = await relationshipClient.CreateRelationshipAsync(createRequest);

            // Retrieve it
            var getRequest = new GetRelationshipRequest { RelationshipId = created.RelationshipId };
            var response = await relationshipClient.GetRelationshipAsync(getRequest);

            if (response.RelationshipId != created.RelationshipId)
                return TestResult.Failed("ID mismatch");

            if (response.Entity1Id != createRequest.Entity1Id)
                return TestResult.Failed("Entity1Id mismatch");

            return TestResult.Successful($"Retrieved relationship: ID={response.RelationshipId}");
        }, "Get relationship");

    private static async Task<TestResult> TestUpdateRelationship(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var relationshipClient = GetServiceClient<IRelationshipClient>();

            // Create a relationship type first
            var relationshipTypeClient = GetServiceClient<IRelationshipTypeClient>();
            var typeResponse = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"UPDATE_REL_TYPE_{DateTime.Now.Ticks}",
                    Name = "Update Test Type"
                });

            // Create a relationship
            var created = await relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                Entity1Id = Guid.NewGuid(),
                Entity1Type = EntityType.Character,
                Entity2Id = Guid.NewGuid(),
                Entity2Type = EntityType.Actor,
                RelationshipTypeId = typeResponse.RelationshipTypeId,
                StartedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, object> { { "initial", "value" } }
            });

            // Update metadata
            var updateRequest = new UpdateRelationshipRequest
            {
                RelationshipId = created.RelationshipId,
                Metadata = new Dictionary<string, object>
                {
                    { "updated", "new_value" },
                    { "trust_level", 75 }
                }
            };
            var response = await relationshipClient.UpdateRelationshipAsync(updateRequest);

            if (response.RelationshipId != created.RelationshipId)
                return TestResult.Failed("ID mismatch after update");

            // Metadata comes back as JsonElement from System.Text.Json
            if (response.Metadata == null)
                return TestResult.Failed("Metadata is null after update");

            if (response.Metadata is JsonElement metadataElement)
            {
                if (!metadataElement.TryGetProperty("updated", out _))
                    return TestResult.Failed("Metadata 'updated' key not found");

                return TestResult.Successful($"Updated relationship: ID={response.RelationshipId}, Metadata updated successfully");
            }

            return TestResult.Successful($"Updated relationship: ID={response.RelationshipId}, Metadata type={response.Metadata.GetType().Name}");
        }, "Update relationship");

    private static async Task<TestResult> TestEndRelationship(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var relationshipClient = GetServiceClient<IRelationshipClient>();

            // Create a relationship type first
            var relationshipTypeClient = GetServiceClient<IRelationshipTypeClient>();
            var typeResponse = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"END_REL_TYPE_{DateTime.Now.Ticks}",
                    Name = "End Test Type"
                });

            // Create a relationship
            var created = await relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                Entity1Id = Guid.NewGuid(),
                Entity1Type = EntityType.Character,
                Entity2Id = Guid.NewGuid(),
                Entity2Type = EntityType.Actor,
                RelationshipTypeId = typeResponse.RelationshipTypeId,
                StartedAt = DateTimeOffset.UtcNow
            });

            // End the relationship
            await relationshipClient.EndRelationshipAsync(new EndRelationshipRequest
            {
                RelationshipId = created.RelationshipId,
                Reason = "Test ending relationship"
            });

            // Verify it shows as ended (should still be retrievable but with endedAt set)
            var getResponse = await relationshipClient.GetRelationshipAsync(new GetRelationshipRequest
            {
                RelationshipId = created.RelationshipId
            });

            if (getResponse.EndedAt == null)
                return TestResult.Failed("EndedAt should be set after ending relationship");

            return TestResult.Successful($"Ended relationship: ID={created.RelationshipId}, EndedAt={getResponse.EndedAt}");
        }, "End relationship");

    private static async Task<TestResult> TestListRelationshipsByEntity(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var relationshipClient = GetServiceClient<IRelationshipClient>();

            // Create a relationship type
            var relationshipTypeClient = GetServiceClient<IRelationshipTypeClient>();
            var typeResponse = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"LIST_ENTITY_TYPE_{DateTime.Now.Ticks}",
                    Name = "List By Entity Type"
                });

            // Create an entity and multiple relationships for it
            var entityId = Guid.NewGuid();
            for (int i = 0; i < 3; i++)
            {
                await relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
                {
                    Entity1Id = entityId,
                    Entity1Type = EntityType.Character,
                    Entity2Id = Guid.NewGuid(),
                    Entity2Type = EntityType.Actor,
                    RelationshipTypeId = typeResponse.RelationshipTypeId,
                    StartedAt = DateTimeOffset.UtcNow
                });
            }

            // List relationships for the entity
            var response = await relationshipClient.ListRelationshipsByEntityAsync(new ListRelationshipsByEntityRequest
            {
                EntityId = entityId,
                EntityType = EntityType.Character
            });

            if (response.Relationships == null || response.Relationships.Count < 3)
                return TestResult.Failed($"Expected at least 3 relationships, got {response.Relationships?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Relationships.Count} relationships for entity {entityId}");
        }, "List relationships by entity");

    private static async Task<TestResult> TestGetRelationshipsBetween(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var relationshipClient = GetServiceClient<IRelationshipClient>();

            // Create relationship types
            var relationshipTypeClient = GetServiceClient<IRelationshipTypeClient>();
            var type1 = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"BETWEEN_TYPE1_{DateTime.Now.Ticks}",
                    Name = "Between Type 1"
                });
            var type2 = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"BETWEEN_TYPE2_{DateTime.Now.Ticks}",
                    Name = "Between Type 2"
                });

            // Create two entities with multiple relationships between them
            var entity1Id = Guid.NewGuid();
            var entity2Id = Guid.NewGuid();

            await relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                Entity1Id = entity1Id,
                Entity1Type = EntityType.Character,
                Entity2Id = entity2Id,
                Entity2Type = EntityType.Character,
                RelationshipTypeId = type1.RelationshipTypeId,
                StartedAt = DateTimeOffset.UtcNow
            });

            await relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                Entity1Id = entity1Id,
                Entity1Type = EntityType.Character,
                Entity2Id = entity2Id,
                Entity2Type = EntityType.Character,
                RelationshipTypeId = type2.RelationshipTypeId,
                StartedAt = DateTimeOffset.UtcNow
            });

            // Get relationships between the two entities
            var response = await relationshipClient.GetRelationshipsBetweenAsync(new GetRelationshipsBetweenRequest
            {
                Entity1Id = entity1Id,
                Entity1Type = EntityType.Character,
                Entity2Id = entity2Id,
                Entity2Type = EntityType.Character
            });

            if (response.Relationships == null || response.Relationships.Count < 2)
                return TestResult.Failed($"Expected at least 2 relationships, got {response.Relationships?.Count ?? 0}");

            return TestResult.Successful($"Found {response.Relationships.Count} relationships between entities");
        }, "Get relationships between");

    private static async Task<TestResult> TestListRelationshipsByType(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var relationshipClient = GetServiceClient<IRelationshipClient>();

            // Create a relationship type
            var relationshipTypeClient = GetServiceClient<IRelationshipTypeClient>();
            var typeResponse = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"LIST_TYPE_{DateTime.Now.Ticks}",
                    Name = "List By Type"
                });

            // Create multiple relationships of this type
            for (int i = 0; i < 3; i++)
            {
                await relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
                {
                    Entity1Id = Guid.NewGuid(),
                    Entity1Type = EntityType.Character,
                    Entity2Id = Guid.NewGuid(),
                    Entity2Type = EntityType.Actor,
                    RelationshipTypeId = typeResponse.RelationshipTypeId,
                    StartedAt = DateTimeOffset.UtcNow
                });
            }

            // List relationships by type
            var response = await relationshipClient.ListRelationshipsByTypeAsync(new ListRelationshipsByTypeRequest
            {
                RelationshipTypeId = typeResponse.RelationshipTypeId
            });

            if (response.Relationships == null || response.Relationships.Count < 3)
                return TestResult.Failed($"Expected at least 3 relationships, got {response.Relationships?.Count ?? 0}");

            return TestResult.Successful($"Listed {response.Relationships.Count} relationships of type {typeResponse.RelationshipTypeId}");
        }, "List relationships by type");

    private static async Task<TestResult> TestGetNonExistentRelationship(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var relationshipClient = GetServiceClient<IRelationshipClient>();
                await relationshipClient.GetRelationshipAsync(new GetRelationshipRequest
                {
                    RelationshipId = Guid.NewGuid()
                });
            },
            404,
            "Get non-existent relationship");

    private static async Task<TestResult> TestDuplicateCompositeKeyConflict(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var relationshipClient = GetServiceClient<IRelationshipClient>();

            // Create a relationship type
            var relationshipTypeClient = GetServiceClient<IRelationshipTypeClient>();
            var typeResponse = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"DUP_COMPOSITE_TYPE_{DateTime.Now.Ticks}",
                    Name = "Duplicate Composite Type"
                });

            // Create same entity IDs and type - should conflict on composite key
            var entity1Id = Guid.NewGuid();
            var entity2Id = Guid.NewGuid();

            // First relationship
            await relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                Entity1Id = entity1Id,
                Entity1Type = EntityType.Character,
                Entity2Id = entity2Id,
                Entity2Type = EntityType.Actor,
                RelationshipTypeId = typeResponse.RelationshipTypeId,
                StartedAt = DateTimeOffset.UtcNow
            });

            // Try to create duplicate
            try
            {
                await relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
                {
                    Entity1Id = entity1Id,
                    Entity1Type = EntityType.Character,
                    Entity2Id = entity2Id,
                    Entity2Type = EntityType.Actor,
                    RelationshipTypeId = typeResponse.RelationshipTypeId,
                    StartedAt = DateTimeOffset.UtcNow
                });
                return TestResult.Failed("Expected 409 for duplicate composite key");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                return TestResult.Successful("Correctly returned 409 for duplicate composite key");
            }
        }, "Duplicate composite key conflict");

    private static async Task<TestResult> TestCompleteRelationshipLifecycle(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var relationshipClient = GetServiceClient<IRelationshipClient>();
            var testId = DateTime.Now.Ticks;

            // Step 1: Create a relationship type
            Console.WriteLine("  Step 1: Creating relationship type...");
            var relationshipTypeClient = GetServiceClient<IRelationshipTypeClient>();
            var typeResponse = await relationshipTypeClient.CreateRelationshipTypeAsync(
                new RelationshipType.CreateRelationshipTypeRequest
                {
                    Code = $"LIFECYCLE_TYPE_{testId}",
                    Name = "Lifecycle Test Type",
                    IsBidirectional = true
                });

            // Step 2: Create a relationship
            Console.WriteLine("  Step 2: Creating relationship...");
            var entity1Id = Guid.NewGuid();
            var entity2Id = Guid.NewGuid();
            var relationship = await relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                Entity1Id = entity1Id,
                Entity1Type = EntityType.Character,
                Entity2Id = entity2Id,
                Entity2Type = EntityType.Character,
                RelationshipTypeId = typeResponse.RelationshipTypeId,
                StartedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, object> { { "trust_level", 50 } }
            });

            if (relationship.RelationshipId == Guid.Empty)
                return TestResult.Failed("Create returned empty ID");

            // Step 3: Retrieve the relationship
            Console.WriteLine("  Step 3: Retrieving relationship...");
            var retrieved = await relationshipClient.GetRelationshipAsync(new GetRelationshipRequest
            {
                RelationshipId = relationship.RelationshipId
            });

            if (retrieved.RelationshipId != relationship.RelationshipId)
                return TestResult.Failed("ID mismatch on retrieve");

            // Step 4: Update metadata
            Console.WriteLine("  Step 4: Updating metadata...");
            var updated = await relationshipClient.UpdateRelationshipAsync(new UpdateRelationshipRequest
            {
                RelationshipId = relationship.RelationshipId,
                Metadata = new Dictionary<string, object> { { "trust_level", 80 }, { "notes", "Improved relationship" } }
            });

            // Metadata comes back as JsonElement from System.Text.Json
            if (updated.Metadata == null)
                return TestResult.Failed("Metadata is null after update");

            if (updated.Metadata is JsonElement metadataJson)
            {
                if (!metadataJson.TryGetProperty("notes", out _))
                    return TestResult.Failed("Metadata 'notes' key not found");
            }

            // Step 5: List by entity
            Console.WriteLine("  Step 5: Listing by entity...");
            var entityList = await relationshipClient.ListRelationshipsByEntityAsync(new ListRelationshipsByEntityRequest
            {
                EntityId = entity1Id,
                EntityType = EntityType.Character
            });

            if (entityList.Relationships == null || !entityList.Relationships.Any(r => r.RelationshipId == relationship.RelationshipId))
                return TestResult.Failed("Relationship not found in entity list");

            // Step 6: Get between entities
            Console.WriteLine("  Step 6: Getting relationships between entities...");
            var betweenList = await relationshipClient.GetRelationshipsBetweenAsync(new GetRelationshipsBetweenRequest
            {
                Entity1Id = entity1Id,
                Entity1Type = EntityType.Character,
                Entity2Id = entity2Id,
                Entity2Type = EntityType.Character
            });

            if (betweenList.Relationships == null || !betweenList.Relationships.Any(r => r.RelationshipId == relationship.RelationshipId))
                return TestResult.Failed("Relationship not found between entities");

            // Step 7: List by type
            Console.WriteLine("  Step 7: Listing by type...");
            var typeList = await relationshipClient.ListRelationshipsByTypeAsync(new ListRelationshipsByTypeRequest
            {
                RelationshipTypeId = typeResponse.RelationshipTypeId
            });

            if (typeList.Relationships == null || !typeList.Relationships.Any(r => r.RelationshipId == relationship.RelationshipId))
                return TestResult.Failed("Relationship not found in type list");

            // Step 8: End relationship
            Console.WriteLine("  Step 8: Ending relationship...");
            await relationshipClient.EndRelationshipAsync(new EndRelationshipRequest
            {
                RelationshipId = relationship.RelationshipId,
                Reason = "Lifecycle test complete"
            });

            // Step 9: Verify ended
            Console.WriteLine("  Step 9: Verifying ended state...");
            var ended = await relationshipClient.GetRelationshipAsync(new GetRelationshipRequest
            {
                RelationshipId = relationship.RelationshipId
            });

            if (ended.EndedAt == null)
                return TestResult.Failed("EndedAt should be set after ending");

            return TestResult.Successful("Complete relationship lifecycle test passed");
        }, "Complete relationship lifecycle");
}
