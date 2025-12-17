using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.Relationship;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for relationship service API endpoints.
/// Tests the relationship service APIs through the Connect service WebSocket binary protocol.
///
/// IMPORTANT: These tests create dedicated test accounts with their own BannouClient instances.
/// This avoids interfering with Program.Client or Program.AdminClient, and properly tests
/// the user experience from account creation through API usage.
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

    #region Helper Methods for Test Account Creation

    /// <summary>
    /// Creates a dedicated admin test account and returns the access token and connect URL.
    /// Admin accounts have elevated permissions needed for relationship management.
    /// </summary>
    private async Task<(string accessToken, string connectUrl)?> CreateAdminTestAccountAsync(string testPrefix)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return null;
        }

        var openrestyHost = Program.Configuration.OpenResty_Host ?? "openresty";
        var openrestyPort = Program.Configuration.OpenResty_Port ?? 80;
        var uniqueId = Guid.NewGuid().ToString("N")[..12];
        var testEmail = $"{testPrefix}_{uniqueId}@test.local";
        var testPassword = $"{testPrefix}Test123!";

        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new { username = $"admin_{testPrefix}_{uniqueId}", email = testEmail, password = testPassword, role = "admin" };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(
                JsonSerializer.Serialize(registerContent),
                Encoding.UTF8,
                "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            if (!registerResponse.IsSuccessStatusCode)
            {
                var errorBody = await registerResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"   Failed to create admin test account: {registerResponse.StatusCode} - {errorBody}");
                return null;
            }

            var responseBody = await registerResponse.Content.ReadAsStringAsync();
            var responseObj = JsonDocument.Parse(responseBody);
            var accessToken = responseObj.RootElement.GetProperty("accessToken").GetString();
            var connectUrl = responseObj.RootElement.GetProperty("connectUrl").GetString();

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("   No accessToken in registration response");
                return null;
            }

            if (string.IsNullOrEmpty(connectUrl))
            {
                Console.WriteLine("   No connectUrl in registration response");
                return null;
            }

            Console.WriteLine($"   Created admin test account: {testEmail}");
            return (accessToken, connectUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create admin test account: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a BannouClient connected with the given access token and connect URL.
    /// Returns null if connection fails.
    /// </summary>
    private async Task<BannouClient?> CreateConnectedClientAsync(string accessToken, string connectUrl)
    {
        var client = new BannouClient();

        try
        {
            var connected = await client.ConnectWithTokenAsync(connectUrl, accessToken);
            if (!connected || !client.IsConnected)
            {
                Console.WriteLine("   BannouClient failed to connect");
                await client.DisposeAsync();
                return null;
            }

            Console.WriteLine($"   BannouClient connected, session: {client.SessionId}");
            return client;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   BannouClient connection failed: {ex.Message}");
            await client.DisposeAsync();
            return null;
        }
    }

    #endregion

    private void TestCreateAndGetRelationshipViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship/create and /relationship/get via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("rel_crud");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                var entity1Id = Guid.NewGuid();
                var entity2Id = Guid.NewGuid();
                var relationshipTypeId = Guid.NewGuid();

                var createRequest = new CreateRelationshipRequest
                {
                    Entity1Id = entity1Id,
                    Entity1Type = EntityType.CHARACTER,
                    Entity2Id = entity2Id,
                    Entity2Type = EntityType.CHARACTER,
                    RelationshipTypeId = relationshipTypeId,
                    StartedAt = DateTimeOffset.UtcNow
                };

                try
                {
                    Console.WriteLine("   Invoking /relationship/create...");
                    var createResponse = await client.InvokeAsync<CreateRelationshipRequest, JsonElement>(
                        "POST",
                        "/relationship/create",
                        createRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var relationshipIdStr = createResponse.TryGetProperty("relationshipId", out var idProp) ? idProp.GetString() : null;

                    if (string.IsNullOrEmpty(relationshipIdStr))
                    {
                        Console.WriteLine("   Failed to create relationship - no relationshipId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created relationship: {relationshipIdStr}");

                    // Now retrieve it
                    var getRequest = new GetRelationshipRequest
                    {
                        RelationshipId = Guid.Parse(relationshipIdStr)
                    };

                    Console.WriteLine("   Invoking /relationship/get...");
                    var getResponse = await client.InvokeAsync<GetRelationshipRequest, JsonElement>(
                        "POST",
                        "/relationship/get",
                        getRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var retrievedId = getResponse.TryGetProperty("relationshipId", out var retrievedIdProp) ? retrievedIdProp.GetString() : null;
                    var retrievedEntity1 = getResponse.TryGetProperty("entity1Id", out var e1Prop) ? e1Prop.GetString() : null;

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
                Console.WriteLine("PASSED Relationship create and get test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Relationship create and get test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Relationship create and get test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestListRelationshipsByEntityViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship List by Entity Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship/list-by-entity via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("rel_list");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                // Create a relationship first
                var entityId = Guid.NewGuid();
                var createRequest = new CreateRelationshipRequest
                {
                    Entity1Id = entityId,
                    Entity1Type = EntityType.CHARACTER,
                    Entity2Id = Guid.NewGuid(),
                    Entity2Type = EntityType.NPC,
                    RelationshipTypeId = Guid.NewGuid(),
                    StartedAt = DateTimeOffset.UtcNow
                };

                try
                {
                    Console.WriteLine("   Creating test relationship...");
                    var createResponse = await client.InvokeAsync<CreateRelationshipRequest, JsonElement>(
                        "POST",
                        "/relationship/create",
                        createRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    // Now list by entity
                    var listRequest = new ListRelationshipsByEntityRequest
                    {
                        EntityId = entityId,
                        EntityType = EntityType.CHARACTER,
                        IncludeEnded = true
                    };

                    Console.WriteLine("   Invoking /relationship/list-by-entity...");
                    var listResponse = await client.InvokeAsync<ListRelationshipsByEntityRequest, JsonElement>(
                        "POST",
                        "/relationship/list-by-entity",
                        listRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var hasRelationshipsArray = listResponse.TryGetProperty("relationships", out var relsProp) &&
                                                relsProp.ValueKind == JsonValueKind.Array;
                    var totalCount = listResponse.TryGetProperty("totalCount", out var countProp) ? countProp.GetInt32() : 0;

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
                Console.WriteLine("PASSED Relationship list by entity test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Relationship list by entity test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Relationship list by entity test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRelationshipLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Relationship Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete relationship lifecycle via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("rel_lifecycle");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                try
                {
                    // Step 1: Create relationship
                    Console.WriteLine("   Step 1: Creating relationship...");
                    var entity1Id = Guid.NewGuid();
                    var entity2Id = Guid.NewGuid();
                    var createRequest = new CreateRelationshipRequest
                    {
                        Entity1Id = entity1Id,
                        Entity1Type = EntityType.CHARACTER,
                        Entity2Id = entity2Id,
                        Entity2Type = EntityType.CHARACTER,
                        RelationshipTypeId = Guid.NewGuid(),
                        StartedAt = DateTimeOffset.UtcNow,
                        Metadata = new Dictionary<string, object> { { "testKey", "testValue" } }
                    };

                    var createResponse = await client.InvokeAsync<CreateRelationshipRequest, JsonElement>(
                        "POST",
                        "/relationship/create",
                        createRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var relationshipIdStr = createResponse.TryGetProperty("relationshipId", out var idProp) ? idProp.GetString() : null;
                    if (string.IsNullOrEmpty(relationshipIdStr))
                    {
                        Console.WriteLine("   Failed to create relationship - no relationshipId in response");
                        return false;
                    }
                    var relationshipId = Guid.Parse(relationshipIdStr);
                    Console.WriteLine($"   Created relationship {relationshipId}");

                    // Step 2: Update relationship
                    Console.WriteLine("   Step 2: Updating relationship...");
                    var updateRequest = new UpdateRelationshipRequest
                    {
                        RelationshipId = relationshipId,
                        Metadata = new Dictionary<string, object> { { "testKey", "updatedValue" }, { "newKey", "newValue" } }
                    };

                    var updateResponse = await client.InvokeAsync<UpdateRelationshipRequest, JsonElement>(
                        "POST",
                        "/relationship/update",
                        updateRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var updatedId = updateResponse.TryGetProperty("relationshipId", out var updatedIdProp) ? updatedIdProp.GetString() : null;
                    if (updatedId != relationshipIdStr)
                    {
                        Console.WriteLine($"   Failed to update relationship - returned id: {updatedId}");
                        return false;
                    }
                    Console.WriteLine($"   Updated relationship metadata");

                    // Step 3: End relationship (soft delete)
                    Console.WriteLine("   Step 3: Ending relationship...");
                    var endRequest = new EndRelationshipRequest
                    {
                        RelationshipId = relationshipId,
                        Reason = "WebSocket lifecycle test"
                    };

                    await client.InvokeAsync<EndRelationshipRequest, JsonElement>(
                        "POST",
                        "/relationship/end",
                        endRequest,
                        timeout: TimeSpan.FromSeconds(15));
                    Console.WriteLine($"   Ended relationship successfully");

                    // Step 4: Verify relationship has endedAt set
                    Console.WriteLine("   Step 4: Verifying relationship ended...");
                    var getRequest = new GetRelationshipRequest
                    {
                        RelationshipId = relationshipId
                    };

                    var getResponse = await client.InvokeAsync<GetRelationshipRequest, JsonElement>(
                        "POST",
                        "/relationship/get",
                        getRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var hasEndedAt = getResponse.TryGetProperty("endedAt", out var endedAtProp) &&
                                    endedAtProp.ValueKind != JsonValueKind.Null;

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
                Console.WriteLine("PASSED Relationship complete lifecycle test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Relationship complete lifecycle test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Relationship lifecycle test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}
