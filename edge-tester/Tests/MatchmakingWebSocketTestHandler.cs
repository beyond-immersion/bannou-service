using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Matchmaking;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for matchmaking service API endpoints.
/// Tests the matchmaking service APIs through the Connect service WebSocket binary protocol.
///
/// IMPORTANT: These tests create dedicated test accounts with their own BannouClient instances.
/// This avoids interfering with Program.Client or Program.AdminClient, and properly tests
/// the user experience from account creation through API usage.
/// </summary>
public class MatchmakingWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestListQueuesViaWebSocket, "Matchmaking - List Queues (WebSocket)", "WebSocket",
                "Test listing matchmaking queues via WebSocket protocol"),
            new ServiceTest(TestCreateQueueViaWebSocket, "Matchmaking - Create Queue (WebSocket)", "WebSocket",
                "Test creating a matchmaking queue via WebSocket protocol"),
            new ServiceTest(TestQueueLifecycleViaWebSocket, "Matchmaking - Queue Lifecycle (WebSocket)", "WebSocket",
                "Test complete queue lifecycle: create -> get -> update -> delete via WebSocket"),
            new ServiceTest(TestJoinMatchmakingViaWebSocket, "Matchmaking - Join Queue (WebSocket)", "WebSocket",
                "Test joining a matchmaking queue via WebSocket protocol"),
        };
    }

    #region Helper Methods

    /// <summary>
    /// Creates a dedicated test account and returns the access token, connect URL, email, and account ID.
    /// </summary>
    private async Task<(string accessToken, string connectUrl, string email, Guid accountId)?> CreateTestAccountAsync(string testPrefix)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return null;
        }

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;
        var uniqueId = Guid.NewGuid().ToString("N")[..12];
        var testEmail = $"{testPrefix}_{uniqueId}@test.local";
        var testPassword = $"{testPrefix}Test123!";

        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new { username = $"{testPrefix}_{uniqueId}", email = testEmail, password = testPassword };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(
                BannouJson.Serialize(registerContent),
                Encoding.UTF8,
                "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            if (!registerResponse.IsSuccessStatusCode)
            {
                var errorBody = await registerResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"   Failed to create test account: {registerResponse.StatusCode} - {errorBody}");
                return null;
            }

            var responseBody = await registerResponse.Content.ReadAsStringAsync();
            var responseObj = JsonDocument.Parse(responseBody);
            var accessToken = responseObj.RootElement.GetProperty("accessToken").GetString();
            var connectUrl = responseObj.RootElement.GetProperty("connectUrl").GetString();
            var accountIdString = responseObj.RootElement.GetProperty("accountId").GetString();

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(connectUrl))
            {
                Console.WriteLine("   Missing accessToken or connectUrl in registration response");
                return null;
            }

            if (string.IsNullOrEmpty(accountIdString) || !Guid.TryParse(accountIdString, out var accountId))
            {
                Console.WriteLine("   Missing or invalid accountId in registration response");
                return null;
            }

            Console.WriteLine($"   Created test account: {testEmail} (ID: {accountId})");
            return (accessToken, connectUrl, testEmail, accountId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a BannouClient connected with the given access token and connect URL.
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

    private void TestListQueuesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Matchmaking List Queues Test (WebSocket) ===");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                try
                {
                    Console.WriteLine("   Listing matchmaking queues via WebSocket...");
                    var response = await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/list",
                        new { gameId = "test-game" },
                        timeout: TimeSpan.FromSeconds(5));

                    if (!response.IsSuccess)
                    {
                        Console.WriteLine($"   List queues failed: {response.Error?.Message}");
                        return false;
                    }

                    var json = System.Text.Json.Nodes.JsonNode.Parse(response.Result.GetRawText())?.AsObject();
                    var queues = json?["queues"]?.AsArray();
                    Console.WriteLine($"   Listed {queues?.Count ?? 0} queues");

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Matchmaking list queues test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Matchmaking list queues test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Matchmaking list queues test with exception: {ex.Message}");
        }
    }

    private void TestCreateQueueViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Matchmaking Create Queue Test (WebSocket) ===");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                var queueId = $"ws-test-queue-{DateTime.Now.Ticks}";

                try
                {
                    Console.WriteLine($"   Creating queue {queueId} via WebSocket...");
                    var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/create",
                        new
                        {
                            queueId = queueId,
                            gameId = "test-game",
                            displayName = "WebSocket Test Queue",
                            description = "Queue created via WebSocket edge test",
                            minCount = 2,
                            maxCount = 4,
                            useSkillRating = true,
                            matchAcceptTimeoutSeconds = 30
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    if (!createResponse.IsSuccess)
                    {
                        Console.WriteLine($"   Create queue failed: {createResponse.Error?.Message}");
                        return false;
                    }

                    var json = System.Text.Json.Nodes.JsonNode.Parse(createResponse.Result.GetRawText())?.AsObject();
                    var createdQueueId = json?["queueId"]?.GetValue<string>();

                    if (createdQueueId != queueId)
                    {
                        Console.WriteLine($"   Queue ID mismatch: expected {queueId}, got {createdQueueId}");
                        return false;
                    }

                    Console.WriteLine($"   Queue created: {createdQueueId}");

                    // Cleanup: delete the queue
                    Console.WriteLine("   Cleaning up - deleting queue...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/delete",
                        new { queueId = queueId },
                        timeout: TimeSpan.FromSeconds(5));

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Matchmaking create queue test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Matchmaking create queue test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Matchmaking create queue test with exception: {ex.Message}");
        }
    }

    private void TestQueueLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Matchmaking Queue Lifecycle Test (WebSocket) ===");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                var queueId = $"ws-lifecycle-queue-{DateTime.Now.Ticks}";

                try
                {
                    // Step 1: Create queue
                    Console.WriteLine($"   Step 1: Creating queue {queueId}...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/create",
                        new
                        {
                            queueId = queueId,
                            gameId = "test-game",
                            displayName = "Lifecycle Test Queue",
                            minCount = 2,
                            maxCount = 4
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    Console.WriteLine("   Queue created");

                    // Step 2: Get queue
                    Console.WriteLine("   Step 2: Getting queue...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/get",
                        new { queueId = queueId },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var json = System.Text.Json.Nodes.JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var displayName = json?["displayName"]?.GetValue<string>();
                    Console.WriteLine($"   Got queue: {displayName}");

                    // Step 3: Update queue
                    Console.WriteLine("   Step 3: Updating queue...");
                    var updateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/update",
                        new
                        {
                            queueId = queueId,
                            displayName = "Updated Lifecycle Queue",
                            maxCount = 8
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var updatedJson = System.Text.Json.Nodes.JsonNode.Parse(updateResponse.GetRawText())?.AsObject();
                    var updatedDisplayName = updatedJson?["displayName"]?.GetValue<string>();
                    var updatedMaxCount = updatedJson?["maxCount"]?.GetValue<int>() ?? 0;

                    if (updatedDisplayName != "Updated Lifecycle Queue" || updatedMaxCount != 8)
                    {
                        Console.WriteLine($"   Update failed: displayName={updatedDisplayName}, maxCount={updatedMaxCount}");
                        return false;
                    }
                    Console.WriteLine("   Queue updated");

                    // Step 4: Delete queue
                    Console.WriteLine("   Step 4: Deleting queue...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/delete",
                        new { queueId = queueId },
                        timeout: TimeSpan.FromSeconds(5));

                    Console.WriteLine("   Queue deleted");

                    // Step 5: Verify deletion (should return 404)
                    Console.WriteLine("   Step 5: Verifying deletion...");
                    var verifyResponse = await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/get",
                        new { queueId = queueId },
                        timeout: TimeSpan.FromSeconds(5));

                    if (verifyResponse.IsSuccess)
                    {
                        Console.WriteLine("   Queue still exists after deletion!");
                        return false;
                    }

                    if (verifyResponse.Error?.ResponseCode != 404)
                    {
                        Console.WriteLine($"   Expected 404, got {verifyResponse.Error?.ResponseCode}");
                        return false;
                    }

                    Console.WriteLine("   Deletion verified (404)");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    // Try to cleanup
                    try
                    {
                        await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/matchmaking/queue/delete",
                            new { queueId = queueId },
                            timeout: TimeSpan.FromSeconds(2));
                    }
                    catch { }
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Matchmaking queue lifecycle test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Matchmaking queue lifecycle test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Matchmaking queue lifecycle test with exception: {ex.Message}");
        }
    }

    private void TestJoinMatchmakingViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Matchmaking Join Queue Test (WebSocket) ===");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                var queueId = $"ws-join-test-queue-{DateTime.Now.Ticks}";

                try
                {
                    // Step 1: Create a test user and connect
                    Console.WriteLine("   Step 1: Creating test user...");
                    var authResult = await CreateTestAccountAsync($"matchmaking_{DateTime.Now.Ticks % 100000}");
                    if (authResult == null)
                    {
                        Console.WriteLine("   Failed to create test user");
                        return false;
                    }

                    await using var userClient = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                    if (userClient == null)
                    {
                        Console.WriteLine("   Failed to connect user client");
                        return false;
                    }

                    // Step 2: Create queue (admin)
                    Console.WriteLine($"   Step 2: Creating queue {queueId}...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/create",
                        new
                        {
                            queueId = queueId,
                            gameId = "test-game",
                            displayName = "Join Test Queue",
                            minCount = 2,
                            maxCount = 4
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    Console.WriteLine("   Queue created");

                    // Step 3: Join matchmaking (user)
                    Console.WriteLine("   Step 3: Joining matchmaking...");
                    var joinResponse = await userClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/join",
                        new
                        {
                            queueId = queueId,
                            accountId = authResult.Value.accountId,
                            webSocketSessionId = userClient.SessionId
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    if (!joinResponse.IsSuccess)
                    {
                        Console.WriteLine($"   Join failed: {joinResponse.Error?.Message}");
                        // Cleanup
                        await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/matchmaking/queue/delete",
                            new { queueId = queueId },
                            timeout: TimeSpan.FromSeconds(2));
                        return false;
                    }

                    var json = System.Text.Json.Nodes.JsonNode.Parse(joinResponse.Result.GetRawText())?.AsObject();
                    var ticketId = json?["ticketId"]?.GetValue<string>();
                    Console.WriteLine($"   Joined with ticket: {ticketId}");

                    // Step 4: Leave matchmaking
                    Console.WriteLine("   Step 4: Leaving matchmaking...");
                    await userClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/leave",
                        new { ticketId = ticketId },
                        timeout: TimeSpan.FromSeconds(5));

                    Console.WriteLine("   Left matchmaking");

                    // Cleanup
                    Console.WriteLine("   Cleaning up...");
                    await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/matchmaking/queue/delete",
                        new { queueId = queueId },
                        timeout: TimeSpan.FromSeconds(5));

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    // Try to cleanup
                    try
                    {
                        await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/matchmaking/queue/delete",
                            new { queueId = queueId },
                            timeout: TimeSpan.FromSeconds(2));
                    }
                    catch { }
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Matchmaking join queue test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Matchmaking join queue test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Matchmaking join queue test with exception: {ex.Message}");
        }
    }
}
