using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.GameSession;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for game session service API endpoints.
/// Tests the game session service APIs through the Connect service WebSocket binary protocol.
///
/// IMPORTANT: These tests create dedicated test accounts with their own BannouClient instances.
/// This avoids interfering with Program.Client or Program.AdminClient, and properly tests
/// the user experience from account creation through API usage.
/// </summary>
public class GameSessionWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestCreateGameSessionViaWebSocket, "GameSession - Create (WebSocket)", "WebSocket",
                "Test game session creation via WebSocket binary protocol"),
            new ServiceTest(TestListGameSessionsViaWebSocket, "GameSession - List (WebSocket)", "WebSocket",
                "Test game session listing via WebSocket binary protocol"),
            new ServiceTest(TestCompleteSessionLifecycleViaWebSocket, "GameSession - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete session lifecycle via WebSocket: create -> join -> action -> leave"),
        };
    }

    #region Helper Methods for Test Account Creation

    /// <summary>
    /// Creates a dedicated test account and returns the access token and connect URL.
    /// Each test should create its own account to ensure isolation.
    /// </summary>
    private async Task<(string accessToken, string connectUrl)?> CreateTestAccountAsync(string testPrefix)
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
            var registerContent = new { username = $"{testPrefix}_{uniqueId}", email = testEmail, password = testPassword };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(
                JsonSerializer.Serialize(registerContent),
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

            Console.WriteLine($"   Created test account: {testEmail}");
            return (accessToken, connectUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
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

    private void TestCreateGameSessionViaWebSocket(string[] args)
    {
        Console.WriteLine("=== GameSession Create Test (WebSocket) ===");
        Console.WriteLine("Testing /sessions/create via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                // Create dedicated test account and client
                var authResult = await CreateTestAccountAsync("gs_create");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                // Use generated request type to ensure proper JSON serialization
                var createRequest = new CreateGameSessionRequest
                {
                    SessionName = $"WebSocketTest_{DateTime.Now.Ticks}",
                    GameType = CreateGameSessionRequestGameType.Arcadia,
                    MaxPlayers = 4,
                    IsPrivate = false
                };

                try
                {
                    Console.WriteLine("   Invoking /sessions/create...");
                    var response = (await client.InvokeAsync<CreateGameSessionRequest, JsonElement>(
                        "POST",
                        "/sessions/create",
                        createRequest,
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var sessionId = response.TryGetProperty("sessionId", out var idProp) ? idProp.GetString() : null;
                    var sessionName = response.TryGetProperty("sessionName", out var nameProp) ? nameProp.GetString() : null;
                    var maxPlayers = response.TryGetProperty("maxPlayers", out var maxProp) ? maxProp.GetInt32() : 0;

                    Console.WriteLine($"   Session ID: {sessionId}");
                    Console.WriteLine($"   Session Name: {sessionName}");
                    Console.WriteLine($"   Max Players: {maxPlayers}");

                    return !string.IsNullOrEmpty(sessionId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED GameSession create test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED GameSession create test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED GameSession create test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestListGameSessionsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== GameSession List Test (WebSocket) ===");
        Console.WriteLine("Testing /sessions/list via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                // Create dedicated test account and client
                var authResult = await CreateTestAccountAsync("gs_list");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                // Use generated request type
                var listRequest = new ListGameSessionsRequest();

                try
                {
                    Console.WriteLine("   Invoking /sessions/list...");
                    var response = (await client.InvokeAsync<ListGameSessionsRequest, JsonElement>(
                        "POST",
                        "/sessions/list",
                        listRequest,
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var hasSessionsArray = response.TryGetProperty("sessions", out var sessionsProp) &&
                                            sessionsProp.ValueKind == JsonValueKind.Array;
                    var totalCount = response.TryGetProperty("totalCount", out var countProp) ? countProp.GetInt32() : 0;

                    Console.WriteLine($"   Sessions array present: {hasSessionsArray}");
                    Console.WriteLine($"   Total Count: {totalCount}");

                    return hasSessionsArray;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED GameSession list test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED GameSession list test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED GameSession list test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCompleteSessionLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== GameSession Complete Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete session lifecycle via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                // Create dedicated test account and client
                var authResult = await CreateTestAccountAsync("gs_lifecycle");
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
                    // Step 1: Create session
                    Console.WriteLine("   Step 1: Creating session...");
                    var createRequest = new CreateGameSessionRequest
                    {
                        SessionName = $"LifecycleTest_{DateTime.Now.Ticks}",
                        GameType = CreateGameSessionRequestGameType.Arcadia,
                        MaxPlayers = 4,
                        IsPrivate = false
                    };

                    var createResponse = (await client.InvokeAsync<CreateGameSessionRequest, JsonElement>(
                        "POST",
                        "/sessions/create",
                        createRequest,
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var sessionIdStr = createResponse.TryGetProperty("sessionId", out var idProp) ? idProp.GetString() : null;
                    if (string.IsNullOrEmpty(sessionIdStr))
                    {
                        Console.WriteLine("   Failed to create session - no sessionId in response");
                        return false;
                    }
                    var sessionId = Guid.Parse(sessionIdStr);
                    Console.WriteLine($"   Created session {sessionId}");

                    // Step 2: Join session as a player
                    Console.WriteLine("   Step 2: Joining session...");
                    var joinRequest = new JoinGameSessionRequest
                    {
                        SessionId = sessionId
                    };

                    var joinResponse = (await client.InvokeAsync<JoinGameSessionRequest, JsonElement>(
                        "POST",
                        "/sessions/join",
                        joinRequest,
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var joinSuccess = joinResponse.TryGetProperty("success", out var successProp) && successProp.GetBoolean();
                    if (!joinSuccess)
                    {
                        // Check for error message
                        var error = joinResponse.TryGetProperty("error", out var errProp) ? errProp.GetString() : "unknown";
                        Console.WriteLine($"   Failed to join session: {error}");
                        return false;
                    }
                    Console.WriteLine($"   Joined session successfully");

                    // Step 3: Perform game action
                    Console.WriteLine("   Step 3: Performing game action...");
                    var actionRequest = new GameActionRequest
                    {
                        SessionId = sessionId,
                        ActionType = GameActionRequestActionType.Move,
                        ActionData = new { testData = "lifecycle_test" }
                    };

                    var actionResponse = (await client.InvokeAsync<GameActionRequest, JsonElement>(
                        "POST",
                        "/sessions/actions",
                        actionRequest,
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var actionId = actionResponse.TryGetProperty("actionId", out var actionIdProp) ? actionIdProp.GetString() : null;
                    if (string.IsNullOrEmpty(actionId))
                    {
                        Console.WriteLine("   Failed to perform game action - no actionId in response");
                        return false;
                    }
                    Console.WriteLine($"   Performed action {actionId}");

                    // Step 4: Send chat message
                    Console.WriteLine("   Step 4: Sending chat message...");
                    var chatRequest = new ChatMessageRequest
                    {
                        SessionId = sessionId,
                        Message = "WebSocket lifecycle test message",
                        MessageType = ChatMessageRequestMessageType.Public
                    };

                    try
                    {
                        (await client.InvokeAsync<ChatMessageRequest, JsonElement>(
                            "POST",
                            "/sessions/chat",
                            chatRequest,
                            timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();
                        Console.WriteLine($"   Sent chat message");
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("Failed to deserialize"))
                    {
                        // Chat may return empty response - that's OK
                        Console.WriteLine($"   Sent chat message (empty response OK)");
                    }

                    // Step 5: Leave session
                    Console.WriteLine("   Step 5: Leaving session...");
                    var leaveRequest = new LeaveGameSessionRequest
                    {
                        SessionId = sessionId
                    };

                    try
                    {
                        (await client.InvokeAsync<LeaveGameSessionRequest, JsonElement>(
                            "POST",
                            "/sessions/leave",
                            leaveRequest,
                            timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();
                        Console.WriteLine($"   Left session");
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("Failed to deserialize"))
                    {
                        // Leave may return empty response - that's OK
                        Console.WriteLine($"   Left session (empty response OK)");
                    }

                    // Step 6: Verify session still exists
                    Console.WriteLine("   Step 6: Verifying session exists...");
                    var getRequest = new GetGameSessionRequest
                    {
                        SessionId = sessionId
                    };

                    var getResponse = (await client.InvokeAsync<GetGameSessionRequest, JsonElement>(
                        "POST",
                        "/sessions/get",
                        getRequest,
                        timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                    var returnedSessionId = getResponse.TryGetProperty("sessionId", out var returnedIdProp) ? returnedIdProp.GetString() : null;
                    if (returnedSessionId != sessionIdStr)
                    {
                        Console.WriteLine($"   Failed to verify session - expected {sessionIdStr}, got {returnedSessionId}");
                        return false;
                    }
                    Console.WriteLine($"   Session verified");

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
                Console.WriteLine("PASSED GameSession complete lifecycle test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED GameSession complete lifecycle test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED GameSession lifecycle test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

}
