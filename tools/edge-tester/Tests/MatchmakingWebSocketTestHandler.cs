using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Matchmaking;
using System.Text;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for matchmaking service API endpoints.
/// Tests the matchmaking service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
///
/// IMPORTANT: These tests create dedicated test accounts with their own BannouClient instances.
/// This avoids interfering with Program.Client or Program.AdminClient, and properly tests
/// the user experience from account creation through API usage.
/// </summary>
public class MatchmakingWebSocketTestHandler : BaseWebSocketTestHandler
{
    public override ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestListQueuesViaWebSocket, "Matchmaking - List Queues (WebSocket)", "WebSocket",
                "Test listing matchmaking queues via typed proxy"),
            new ServiceTest(TestCreateQueueViaWebSocket, "Matchmaking - Create Queue (WebSocket)", "WebSocket",
                "Test creating a matchmaking queue via typed proxy"),
            new ServiceTest(TestQueueLifecycleViaWebSocket, "Matchmaking - Queue Lifecycle (WebSocket)", "WebSocket",
                "Test complete queue lifecycle: create -> get -> update -> delete via typed proxy"),
            new ServiceTest(TestJoinMatchmakingViaWebSocket, "Matchmaking - Join Queue (WebSocket)", "WebSocket",
                "Test joining a matchmaking queue via typed proxy"),
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

    /// <summary>
    /// Deletes a queue safely using typed proxy, ignoring errors.
    /// </summary>
    private async Task TryDeleteQueueAsync(BannouClient adminClient, string queueId)
    {
        try
        {
            await adminClient.Matchmaking.DeleteQueueEventAsync(new DeleteQueueRequest
            {
                QueueId = queueId
            });
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #endregion

    private void TestListQueuesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Matchmaking List Queues Test (WebSocket) ===");
        Console.WriteLine("Testing listing matchmaking queues via typed proxy...");

        RunWebSocketTest("Matchmaking list queues test", async adminClient =>
        {
            Console.WriteLine("   Listing matchmaking queues via typed proxy...");
            var response = await adminClient.Matchmaking.ListQueuesAsync(new ListQueuesRequest
            {
                GameId = "test-game"
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   List queues failed: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Listed {result.Queues?.Count ?? 0} queues");

            return result.Queues != null;
        });
    }

    private void TestCreateQueueViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Matchmaking Create Queue Test (WebSocket) ===");
        Console.WriteLine("Testing creating matchmaking queue via typed proxy...");

        RunWebSocketTest("Matchmaking create queue test", async adminClient =>
        {
            var queueId = $"ws-test-queue-{DateTime.Now.Ticks}";

            try
            {
                Console.WriteLine($"   Creating queue {queueId} via typed proxy...");
                var createResponse = await adminClient.Matchmaking.CreateQueueAsync(new CreateQueueRequest
                {
                    QueueId = queueId,
                    GameId = "test-game",
                    DisplayName = "WebSocket Test Queue",
                    Description = "Queue created via WebSocket edge test",
                    MinCount = 2,
                    MaxCount = 4,
                    UseSkillRating = true,
                    MatchAcceptTimeoutSeconds = 30
                }, timeout: TimeSpan.FromSeconds(5));

                if (!createResponse.IsSuccess || createResponse.Result == null)
                {
                    Console.WriteLine($"   Create queue failed: {FormatError(createResponse.Error)}");
                    return false;
                }

                var result = createResponse.Result;
                if (result.QueueId != queueId)
                {
                    Console.WriteLine($"   Queue ID mismatch: expected {queueId}, got {result.QueueId}");
                    return false;
                }

                Console.WriteLine($"   Queue created: {result.QueueId}");

                // Cleanup: delete the queue
                Console.WriteLine("   Cleaning up - deleting queue...");
                await TryDeleteQueueAsync(adminClient, queueId);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Test failed: {ex.Message}");
                await TryDeleteQueueAsync(adminClient, queueId);
                return false;
            }
        });
    }

    private void TestQueueLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Matchmaking Queue Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete queue lifecycle via typed proxy...");

        RunWebSocketTest("Matchmaking queue lifecycle test", async adminClient =>
        {
            var queueId = $"ws-lifecycle-queue-{DateTime.Now.Ticks}";

            try
            {
                // Step 1: Create queue
                Console.WriteLine($"   Step 1: Creating queue {queueId}...");
                var createResponse = await adminClient.Matchmaking.CreateQueueAsync(new CreateQueueRequest
                {
                    QueueId = queueId,
                    GameId = "test-game",
                    DisplayName = "Lifecycle Test Queue",
                    MinCount = 2,
                    MaxCount = 4
                }, timeout: TimeSpan.FromSeconds(5));

                if (!createResponse.IsSuccess || createResponse.Result == null)
                {
                    Console.WriteLine($"   Create queue failed: {FormatError(createResponse.Error)}");
                    return false;
                }
                Console.WriteLine("   Queue created");

                // Step 2: Get queue
                Console.WriteLine("   Step 2: Getting queue...");
                var getResponse = await adminClient.Matchmaking.GetQueueAsync(new GetQueueRequest
                {
                    QueueId = queueId
                }, timeout: TimeSpan.FromSeconds(5));

                if (!getResponse.IsSuccess || getResponse.Result == null)
                {
                    Console.WriteLine($"   Get queue failed: {FormatError(getResponse.Error)}");
                    return false;
                }

                Console.WriteLine($"   Got queue: {getResponse.Result.DisplayName}");

                // Step 3: Update queue
                Console.WriteLine("   Step 3: Updating queue...");
                var updateResponse = await adminClient.Matchmaking.UpdateQueueAsync(new UpdateQueueRequest
                {
                    QueueId = queueId,
                    DisplayName = "Updated Lifecycle Queue",
                    MaxCount = 8
                }, timeout: TimeSpan.FromSeconds(5));

                if (!updateResponse.IsSuccess || updateResponse.Result == null)
                {
                    Console.WriteLine($"   Update queue failed: {FormatError(updateResponse.Error)}");
                    return false;
                }

                var updated = updateResponse.Result;
                if (updated.DisplayName != "Updated Lifecycle Queue" || updated.MaxCount != 8)
                {
                    Console.WriteLine($"   Update failed: displayName={updated.DisplayName}, maxCount={updated.MaxCount}");
                    return false;
                }
                Console.WriteLine("   Queue updated");

                // Step 4: Delete queue
                Console.WriteLine("   Step 4: Deleting queue...");
                await adminClient.Matchmaking.DeleteQueueEventAsync(new DeleteQueueRequest
                {
                    QueueId = queueId
                });
                Console.WriteLine("   Queue deleted");

                // Give a moment for the delete event to process
                await Task.Delay(100);

                // Step 5: Verify deletion (should return 404)
                Console.WriteLine("   Step 5: Verifying deletion...");
                var verifyResponse = await adminClient.Matchmaking.GetQueueAsync(new GetQueueRequest
                {
                    QueueId = queueId
                }, timeout: TimeSpan.FromSeconds(5));

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
                await TryDeleteQueueAsync(adminClient, queueId);
                return false;
            }
        });
    }

    private void TestJoinMatchmakingViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Matchmaking Join Queue Test (WebSocket) ===");
        Console.WriteLine("Testing joining matchmaking queue via typed proxy...");

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

                    // Step 2: Create queue (admin) using typed proxy
                    Console.WriteLine($"   Step 2: Creating queue {queueId}...");
                    var createResponse = await adminClient.Matchmaking.CreateQueueAsync(new CreateQueueRequest
                    {
                        QueueId = queueId,
                        GameId = "test-game",
                        DisplayName = "Join Test Queue",
                        MinCount = 2,
                        MaxCount = 4
                    }, timeout: TimeSpan.FromSeconds(5));

                    if (!createResponse.IsSuccess)
                    {
                        Console.WriteLine($"   Create queue failed: {FormatError(createResponse.Error)}");
                        return false;
                    }
                    Console.WriteLine("   Queue created");

                    // Step 3: Join matchmaking (user) using typed proxy
                    Console.WriteLine("   Step 3: Joining matchmaking...");
                    if (!Guid.TryParse(userClient.SessionId, out var sessionGuid))
                    {
                        Console.WriteLine("   Invalid session ID format");
                        await TryDeleteQueueAsync(adminClient, queueId);
                        return false;
                    }

                    var joinResponse = await userClient.Matchmaking.JoinMatchmakingAsync(new JoinMatchmakingRequest
                    {
                        QueueId = queueId,
                        AccountId = authResult.Value.accountId,
                        WebSocketSessionId = sessionGuid
                    }, timeout: TimeSpan.FromSeconds(5));

                    if (!joinResponse.IsSuccess || joinResponse.Result == null)
                    {
                        Console.WriteLine($"   Join failed: {FormatError(joinResponse.Error)}");
                        await TryDeleteQueueAsync(adminClient, queueId);
                        return false;
                    }

                    var ticketId = joinResponse.Result.TicketId;
                    Console.WriteLine($"   Joined with ticket: {ticketId}");

                    // Step 4: Leave matchmaking using typed proxy
                    Console.WriteLine("   Step 4: Leaving matchmaking...");
                    await userClient.Matchmaking.LeaveMatchmakingEventAsync(new LeaveMatchmakingRequest
                    {
                        TicketId = ticketId
                    });
                    Console.WriteLine("   Left matchmaking");

                    // Cleanup
                    Console.WriteLine("   Cleaning up...");
                    await TryDeleteQueueAsync(adminClient, queueId);

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    await TryDeleteQueueAsync(adminClient, queueId);
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
