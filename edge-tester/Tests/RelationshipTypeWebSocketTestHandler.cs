using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.RelationshipType;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for relationship type service API endpoints.
/// Tests the relationship type service APIs through the Connect service WebSocket binary protocol.
///
/// IMPORTANT: These tests create dedicated test accounts with their own BannouClient instances.
/// This avoids interfering with Program.Client or Program.AdminClient, and properly tests
/// the user experience from account creation through API usage.
/// </summary>
public class RelationshipTypeWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestListRelationshipTypesViaWebSocket, "RelationshipType - List (WebSocket)", "WebSocket",
                "Test relationship type listing via WebSocket binary protocol"),
            new ServiceTest(TestCreateAndGetRelationshipTypeViaWebSocket, "RelationshipType - Create and Get (WebSocket)", "WebSocket",
                "Test relationship type creation and retrieval via WebSocket binary protocol"),
            new ServiceTest(TestRelationshipTypeHierarchyViaWebSocket, "RelationshipType - Hierarchy (WebSocket)", "WebSocket",
                "Test relationship type hierarchy operations via WebSocket binary protocol"),
            new ServiceTest(TestRelationshipTypeLifecycleViaWebSocket, "RelationshipType - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete relationship type lifecycle via WebSocket: create -> update -> deprecate -> undeprecate"),
        };
    }

    #region Helper Methods for Test Account Creation

    /// <summary>
    /// Creates a dedicated admin test account and returns the access token and connect URL.
    /// Admin accounts have elevated permissions needed for relationship type management.
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

    private void TestListRelationshipTypesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType List Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship-type/list via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("reltype_list");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                var listRequest = new ListRelationshipTypesRequest();

                try
                {
                    Console.WriteLine("   Invoking /relationship-type/list...");
                    var response = await client.InvokeAsync<ListRelationshipTypesRequest, JsonElement>(
                        "POST",
                        "/relationship-type/list",
                        listRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var hasTypesArray = response.TryGetProperty("types", out var typesProp) &&
                                        typesProp.ValueKind == JsonValueKind.Array;
                    var totalCount = response.TryGetProperty("totalCount", out var countProp) ? countProp.GetInt32() : 0;

                    Console.WriteLine($"   Types array present: {hasTypesArray}");
                    Console.WriteLine($"   Total Count: {totalCount}");

                    return hasTypesArray;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED RelationshipType list test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED RelationshipType list test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED RelationshipType list test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCreateAndGetRelationshipTypeViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship-type/create and /relationship-type/get via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("reltype_crud");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                var uniqueCode = $"TYPE{DateTime.Now.Ticks % 100000}";
                var createRequest = new CreateRelationshipTypeRequest
                {
                    Code = uniqueCode,
                    Name = $"Test Type {uniqueCode}",
                    Description = "Created via WebSocket edge test",
                    Category = "TEST",
                    IsBidirectional = true
                };

                try
                {
                    Console.WriteLine("   Invoking /relationship-type/create...");
                    var createResponse = await client.InvokeAsync<CreateRelationshipTypeRequest, JsonElement>(
                        "POST",
                        "/relationship-type/create",
                        createRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var typeIdStr = createResponse.TryGetProperty("relationshipTypeId", out var idProp) ? idProp.GetString() : null;
                    var code = createResponse.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;

                    if (string.IsNullOrEmpty(typeIdStr))
                    {
                        Console.WriteLine("   Failed to create relationship type - no relationshipTypeId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created relationship type: {typeIdStr} ({code})");

                    // Now retrieve it
                    var getRequest = new GetRelationshipTypeRequest
                    {
                        RelationshipTypeId = Guid.Parse(typeIdStr)
                    };

                    Console.WriteLine("   Invoking /relationship-type/get...");
                    var getResponse = await client.InvokeAsync<GetRelationshipTypeRequest, JsonElement>(
                        "POST",
                        "/relationship-type/get",
                        getRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var retrievedId = getResponse.TryGetProperty("relationshipTypeId", out var retrievedIdProp) ? retrievedIdProp.GetString() : null;
                    var retrievedCode = getResponse.TryGetProperty("code", out var retrievedCodeProp) ? retrievedCodeProp.GetString() : null;

                    Console.WriteLine($"   Retrieved relationship type: {retrievedId} ({retrievedCode})");

                    return retrievedId == typeIdStr && retrievedCode == uniqueCode;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED RelationshipType create and get test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED RelationshipType create and get test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED RelationshipType create and get test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRelationshipTypeHierarchyViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Hierarchy Test (WebSocket) ===");
        Console.WriteLine("Testing hierarchy operations via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("reltype_hier");
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
                    // Step 1: Create parent type
                    Console.WriteLine("   Step 1: Creating parent type...");
                    var parentCode = $"PARENT{DateTime.Now.Ticks % 100000}";
                    var parentRequest = new CreateRelationshipTypeRequest
                    {
                        Code = parentCode,
                        Name = $"Parent Type {parentCode}",
                        Category = "FAMILY",
                        IsBidirectional = false
                    };

                    var parentResponse = await client.InvokeAsync<CreateRelationshipTypeRequest, JsonElement>(
                        "POST",
                        "/relationship-type/create",
                        parentRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var parentIdStr = parentResponse.TryGetProperty("relationshipTypeId", out var parentIdProp) ? parentIdProp.GetString() : null;
                    if (string.IsNullOrEmpty(parentIdStr))
                    {
                        Console.WriteLine("   Failed to create parent type");
                        return false;
                    }
                    var parentId = Guid.Parse(parentIdStr);
                    Console.WriteLine($"   Created parent type {parentId}");

                    // Step 2: Create child type with parent
                    Console.WriteLine("   Step 2: Creating child type...");
                    var childCode = $"CHILD{DateTime.Now.Ticks % 100000}";
                    var childRequest = new CreateRelationshipTypeRequest
                    {
                        Code = childCode,
                        Name = $"Child Type {childCode}",
                        Category = "FAMILY",
                        ParentTypeId = parentId,
                        IsBidirectional = false
                    };

                    var childResponse = await client.InvokeAsync<CreateRelationshipTypeRequest, JsonElement>(
                        "POST",
                        "/relationship-type/create",
                        childRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var childIdStr = childResponse.TryGetProperty("relationshipTypeId", out var childIdProp) ? childIdProp.GetString() : null;
                    if (string.IsNullOrEmpty(childIdStr))
                    {
                        Console.WriteLine("   Failed to create child type");
                        return false;
                    }
                    var childId = Guid.Parse(childIdStr);
                    Console.WriteLine($"   Created child type {childId}");

                    // Step 3: Test GetChildRelationshipTypes
                    Console.WriteLine("   Step 3: Getting child types...");
                    var getChildrenRequest = new GetChildRelationshipTypesRequest
                    {
                        ParentTypeId = parentId
                    };

                    var childrenResponse = await client.InvokeAsync<GetChildRelationshipTypesRequest, JsonElement>(
                        "POST",
                        "/relationship-type/get-children",
                        getChildrenRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var hasChildren = childrenResponse.TryGetProperty("types", out var typesProp) &&
                                    typesProp.ValueKind == JsonValueKind.Array &&
                                    typesProp.GetArrayLength() > 0;

                    if (!hasChildren)
                    {
                        Console.WriteLine("   No children found for parent type");
                        return false;
                    }
                    Console.WriteLine($"   Found {typesProp.GetArrayLength()} child type(s)");

                    // Step 4: Test MatchesHierarchy
                    Console.WriteLine("   Step 4: Testing hierarchy match...");
                    var matchRequest = new MatchesHierarchyRequest
                    {
                        TypeId = childId,
                        AncestorTypeId = parentId
                    };

                    var matchResponse = await client.InvokeAsync<MatchesHierarchyRequest, JsonElement>(
                        "POST",
                        "/relationship-type/matches-hierarchy",
                        matchRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var matches = matchResponse.TryGetProperty("matches", out var matchesProp) && matchesProp.GetBoolean();

                    if (!matches)
                    {
                        Console.WriteLine("   Child type does not match parent hierarchy");
                        return false;
                    }
                    Console.WriteLine($"   Child type matches parent hierarchy");

                    // Step 5: Test GetAncestors
                    Console.WriteLine("   Step 5: Getting ancestors...");
                    var ancestorsRequest = new GetAncestorsRequest
                    {
                        TypeId = childId
                    };

                    var ancestorsResponse = await client.InvokeAsync<GetAncestorsRequest, JsonElement>(
                        "POST",
                        "/relationship-type/get-ancestors",
                        ancestorsRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var hasAncestors = ancestorsResponse.TryGetProperty("types", out var ancestorsProp) &&
                                        ancestorsProp.ValueKind == JsonValueKind.Array &&
                                        ancestorsProp.GetArrayLength() > 0;

                    if (!hasAncestors)
                    {
                        Console.WriteLine("   No ancestors found for child type");
                        return false;
                    }
                    Console.WriteLine($"   Found {ancestorsProp.GetArrayLength()} ancestor(s)");

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Hierarchy test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED RelationshipType hierarchy test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED RelationshipType hierarchy test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED RelationshipType hierarchy test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRelationshipTypeLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete relationship type lifecycle via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("reltype_lifecycle");
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
                    // Step 1: Create type
                    Console.WriteLine("   Step 1: Creating relationship type...");
                    var uniqueCode = $"LIFE{DateTime.Now.Ticks % 100000}";
                    var createRequest = new CreateRelationshipTypeRequest
                    {
                        Code = uniqueCode,
                        Name = $"Lifecycle Test {uniqueCode}",
                        Description = "Lifecycle test type",
                        Category = "TEST",
                        IsBidirectional = true
                    };

                    var createResponse = await client.InvokeAsync<CreateRelationshipTypeRequest, JsonElement>(
                        "POST",
                        "/relationship-type/create",
                        createRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var typeIdStr = createResponse.TryGetProperty("relationshipTypeId", out var idProp) ? idProp.GetString() : null;
                    if (string.IsNullOrEmpty(typeIdStr))
                    {
                        Console.WriteLine("   Failed to create relationship type - no relationshipTypeId in response");
                        return false;
                    }
                    var typeId = Guid.Parse(typeIdStr);
                    Console.WriteLine($"   Created relationship type {typeId}");

                    // Step 2: Update type
                    Console.WriteLine("   Step 2: Updating relationship type...");
                    var updateRequest = new UpdateRelationshipTypeRequest
                    {
                        RelationshipTypeId = typeId,
                        Name = $"Updated Lifecycle Test {uniqueCode}",
                        Description = "Updated description"
                    };

                    var updateResponse = await client.InvokeAsync<UpdateRelationshipTypeRequest, JsonElement>(
                        "POST",
                        "/relationship-type/update",
                        updateRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var updatedName = updateResponse.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (!updatedName?.StartsWith("Updated") ?? true)
                    {
                        Console.WriteLine($"   Failed to update relationship type - name: {updatedName}");
                        return false;
                    }
                    Console.WriteLine($"   Updated relationship type name to: {updatedName}");

                    // Step 3: Deprecate type
                    Console.WriteLine("   Step 3: Deprecating relationship type...");
                    var deprecateRequest = new DeprecateRelationshipTypeRequest
                    {
                        RelationshipTypeId = typeId,
                        Reason = "WebSocket lifecycle test"
                    };

                    var deprecateResponse = await client.InvokeAsync<DeprecateRelationshipTypeRequest, JsonElement>(
                        "POST",
                        "/relationship-type/deprecate",
                        deprecateRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var isDeprecated = deprecateResponse.TryGetProperty("isDeprecated", out var deprecatedProp) && deprecatedProp.GetBoolean();
                    if (!isDeprecated)
                    {
                        Console.WriteLine("   Failed to deprecate relationship type");
                        return false;
                    }
                    Console.WriteLine($"   Relationship type deprecated successfully");

                    // Step 4: Undeprecate type
                    Console.WriteLine("   Step 4: Undeprecating relationship type...");
                    var undeprecateRequest = new UndeprecateRelationshipTypeRequest
                    {
                        RelationshipTypeId = typeId
                    };

                    var undeprecateResponse = await client.InvokeAsync<UndeprecateRelationshipTypeRequest, JsonElement>(
                        "POST",
                        "/relationship-type/undeprecate",
                        undeprecateRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var isUndeprecated = undeprecateResponse.TryGetProperty("isDeprecated", out var undeprecatedProp) && !undeprecatedProp.GetBoolean();
                    if (!isUndeprecated)
                    {
                        Console.WriteLine("   Failed to undeprecate relationship type");
                        return false;
                    }
                    Console.WriteLine($"   Relationship type restored successfully");

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
                Console.WriteLine("PASSED RelationshipType complete lifecycle test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED RelationshipType complete lifecycle test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED RelationshipType lifecycle test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}
