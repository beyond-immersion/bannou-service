using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for relationship type service API endpoints.
/// Tests the relationship type service APIs through the Connect service WebSocket binary protocol.
///
/// Note: RelationshipType create/update/deprecate/undeprecate APIs require admin role,
/// so these tests use Program.AdminClient which is already connected with admin permissions.
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

    private void TestListRelationshipTypesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType List Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship-type/list via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () => await PerformRelationshipTypeApiTest(
                "POST",
                "/relationship-type/list",
                new { },
                response =>
                {
                    var hasTypesArray = response?["types"] != null &&
                                        response["types"] is JsonArray;
                    var totalCount = response?["totalCount"]?.GetValue<int>() ?? 0;

                    Console.WriteLine($"   Types array present: {hasTypesArray}");
                    Console.WriteLine($"   Total Count: {totalCount}");

                    return hasTypesArray;
                })).Result;

            if (result)
            {
                Console.WriteLine("✅ RelationshipType list test PASSED");
            }
            else
            {
                Console.WriteLine("❌ RelationshipType list test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ RelationshipType list test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCreateAndGetRelationshipTypeViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /relationship-type/create and /relationship-type/get via shared admin WebSocket...");

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

                var uniqueCode = $"TYPE{DateTime.Now.Ticks % 100000}";

                try
                {
                    // Create relationship type
                    Console.WriteLine("   Invoking /relationship-type/create...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/create",
                        new
                        {
                            code = uniqueCode,
                            name = $"Test Type {uniqueCode}",
                            description = "Created via WebSocket edge test",
                            category = "TEST",
                            isBidirectional = true
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var typeIdStr = createJson?["relationshipTypeId"]?.GetValue<string>();
                    var code = createJson?["code"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(typeIdStr))
                    {
                        Console.WriteLine("   Failed to create relationship type - no relationshipTypeId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created relationship type: {typeIdStr} ({code})");

                    // Now retrieve it
                    Console.WriteLine("   Invoking /relationship-type/get...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/get",
                        new { relationshipTypeId = typeIdStr },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var retrievedId = getJson?["relationshipTypeId"]?.GetValue<string>();
                    var retrievedCode = getJson?["code"]?.GetValue<string>();

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
                Console.WriteLine("✅ RelationshipType create and get test PASSED");
            }
            else
            {
                Console.WriteLine("❌ RelationshipType create and get test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ RelationshipType create and get test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRelationshipTypeHierarchyViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Hierarchy Test (WebSocket) ===");
        Console.WriteLine("Testing hierarchy operations via shared admin WebSocket...");

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
                    // Step 1: Create parent type
                    Console.WriteLine("   Step 1: Creating parent type...");
                    var parentCode = $"PARENT{DateTime.Now.Ticks % 100000}";

                    var parentResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/create",
                        new
                        {
                            code = parentCode,
                            name = $"Parent Type {parentCode}",
                            category = "FAMILY",
                            isBidirectional = false
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var parentJson = JsonNode.Parse(parentResponse.GetRawText())?.AsObject();
                    var parentIdStr = parentJson?["relationshipTypeId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(parentIdStr))
                    {
                        Console.WriteLine("   Failed to create parent type");
                        return false;
                    }
                    Console.WriteLine($"   Created parent type {parentIdStr}");

                    // Step 2: Create child type with parent
                    Console.WriteLine("   Step 2: Creating child type...");
                    var childCode = $"CHILD{DateTime.Now.Ticks % 100000}";

                    var childResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/create",
                        new
                        {
                            code = childCode,
                            name = $"Child Type {childCode}",
                            category = "FAMILY",
                            parentTypeId = parentIdStr,
                            isBidirectional = false
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var childJson = JsonNode.Parse(childResponse.GetRawText())?.AsObject();
                    var childIdStr = childJson?["relationshipTypeId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(childIdStr))
                    {
                        Console.WriteLine("   Failed to create child type");
                        return false;
                    }
                    Console.WriteLine($"   Created child type {childIdStr}");

                    // Step 3: Test GetChildRelationshipTypes
                    Console.WriteLine("   Step 3: Getting child types...");
                    var childrenResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/get-children",
                        new { parentTypeId = parentIdStr },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var childrenJson = JsonNode.Parse(childrenResponse.GetRawText())?.AsObject();
                    var typesArray = childrenJson?["types"]?.AsArray();
                    var hasChildren = typesArray != null && typesArray.Count > 0;

                    if (!hasChildren)
                    {
                        Console.WriteLine("   No children found for parent type");
                        return false;
                    }
                    Console.WriteLine($"   Found {typesArray?.Count} child type(s)");

                    // Step 4: Test MatchesHierarchy
                    Console.WriteLine("   Step 4: Testing hierarchy match...");
                    var matchResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/matches-hierarchy",
                        new
                        {
                            typeId = childIdStr,
                            ancestorTypeId = parentIdStr
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var matchJson = JsonNode.Parse(matchResponse.GetRawText())?.AsObject();
                    var matches = matchJson?["matches"]?.GetValue<bool>() ?? false;

                    if (!matches)
                    {
                        Console.WriteLine("   Child type does not match parent hierarchy");
                        return false;
                    }
                    Console.WriteLine($"   Child type matches parent hierarchy");

                    // Step 5: Test GetAncestors
                    Console.WriteLine("   Step 5: Getting ancestors...");
                    var ancestorsResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/get-ancestors",
                        new { typeId = childIdStr },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var ancestorsJson = JsonNode.Parse(ancestorsResponse.GetRawText())?.AsObject();
                    var ancestorsArray = ancestorsJson?["types"]?.AsArray();
                    var hasAncestors = ancestorsArray != null && ancestorsArray.Count > 0;

                    if (!hasAncestors)
                    {
                        Console.WriteLine("   No ancestors found for child type");
                        return false;
                    }
                    Console.WriteLine($"   Found {ancestorsArray?.Count} ancestor(s)");

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
                Console.WriteLine("✅ RelationshipType hierarchy test PASSED");
            }
            else
            {
                Console.WriteLine("❌ RelationshipType hierarchy test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ RelationshipType hierarchy test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestRelationshipTypeLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== RelationshipType Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete relationship type lifecycle via shared admin WebSocket...");

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
                    // Step 1: Create type
                    Console.WriteLine("   Step 1: Creating relationship type...");
                    var uniqueCode = $"LIFE{DateTime.Now.Ticks % 100000}";

                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/create",
                        new
                        {
                            code = uniqueCode,
                            name = $"Lifecycle Test {uniqueCode}",
                            description = "Lifecycle test type",
                            category = "TEST",
                            isBidirectional = true
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var typeIdStr = createJson?["relationshipTypeId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(typeIdStr))
                    {
                        Console.WriteLine("   Failed to create relationship type - no relationshipTypeId in response");
                        return false;
                    }
                    Console.WriteLine($"   Created relationship type {typeIdStr}");

                    // Step 2: Update type
                    Console.WriteLine("   Step 2: Updating relationship type...");
                    var updateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/update",
                        new
                        {
                            relationshipTypeId = typeIdStr,
                            name = $"Updated Lifecycle Test {uniqueCode}",
                            description = "Updated description"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var updateJson = JsonNode.Parse(updateResponse.GetRawText())?.AsObject();
                    var updatedName = updateJson?["name"]?.GetValue<string>();
                    if (!updatedName?.StartsWith("Updated") ?? true)
                    {
                        Console.WriteLine($"   Failed to update relationship type - name: {updatedName}");
                        return false;
                    }
                    Console.WriteLine($"   Updated relationship type name to: {updatedName}");

                    // Step 3: Deprecate type
                    Console.WriteLine("   Step 3: Deprecating relationship type...");
                    var deprecateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/deprecate",
                        new
                        {
                            relationshipTypeId = typeIdStr,
                            reason = "WebSocket lifecycle test"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var deprecateJson = JsonNode.Parse(deprecateResponse.GetRawText())?.AsObject();
                    var isDeprecated = deprecateJson?["isDeprecated"]?.GetValue<bool>() ?? false;
                    if (!isDeprecated)
                    {
                        Console.WriteLine("   Failed to deprecate relationship type");
                        return false;
                    }
                    Console.WriteLine($"   Relationship type deprecated successfully");

                    // Step 4: Undeprecate type
                    Console.WriteLine("   Step 4: Undeprecating relationship type...");
                    var undeprecateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/relationship-type/undeprecate",
                        new { relationshipTypeId = typeIdStr },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var undeprecateJson = JsonNode.Parse(undeprecateResponse.GetRawText())?.AsObject();
                    var isUndeprecated = !(undeprecateJson?["isDeprecated"]?.GetValue<bool>() ?? true);
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
                Console.WriteLine("✅ RelationshipType complete lifecycle test PASSED");
            }
            else
            {
                Console.WriteLine("❌ RelationshipType complete lifecycle test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ RelationshipType lifecycle test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Performs a relationship type API call via the shared admin WebSocket.
    /// Uses Program.AdminClient which is already connected with admin permissions.
    /// </summary>
    private async Task<bool> PerformRelationshipTypeApiTest(
        string method,
        string path,
        object? body,
        Func<JsonObject?, bool> validateResponse)
    {
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
            Console.WriteLine("   RelationshipType APIs require admin role for create/update/delete operations.");
            return false;
        }

        Console.WriteLine($"📤 Sending relationship type API request via shared admin WebSocket:");
        Console.WriteLine($"   Method: {method}");
        Console.WriteLine($"   Path: {path}");

        try
        {
            var requestBody = body ?? new { };
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                method,
                path,
                requestBody,
                timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

            var responseJson = response.GetRawText();
            Console.WriteLine($"📥 Received response: {responseJson.Substring(0, Math.Min(500, responseJson.Length))}...");

            var responseObj = JsonNode.Parse(responseJson)?.AsObject();
            return validateResponse(responseObj);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine($"❌ Endpoint not available: {method} {path}");
            Console.WriteLine($"   Admin may not have access to relationship type APIs");
            Console.WriteLine($"   Available APIs: {string.Join(", ", adminClient.AvailableApis.Keys.Take(10))}...");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ RelationshipType API test failed: {ex.Message}");
            return false;
        }
    }
}
