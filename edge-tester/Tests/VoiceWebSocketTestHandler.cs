using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Voice;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for voice service integration.
/// Tests voice room lifecycle through GameSession APIs and verifies event-only peer delivery.
///
/// Voice Architecture:
/// - Voice rooms are created automatically when joining GameSession with voice enabled
/// - Peer data is delivered ONLY via VoicePeerJoinedEvent (not in API responses)
/// - Clients use /voice/peer/answer to send SDP answers after receiving peer offers
/// - voice:ringing state is set before VoicePeerJoinedEvent is published
/// </summary>
public class VoiceWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestVoiceRoomCreationViaGameSession, "Voice - Room via GameSession", "WebSocket",
                "Test voice room creation when joining game session with voice enabled"),
            new ServiceTest(TestVoiceEventOnlyPattern, "Voice - Event-Only Pattern", "WebSocket",
                "Test that peers are delivered via events, not in join response"),
            new ServiceTest(TestTwoClientVoicePeerEvents, "Voice - Peer Events (2 Clients)", "WebSocket",
                "Test VoicePeerJoinedEvent delivery when second client joins"),
            new ServiceTest(TestAnswerPeerEndpoint, "Voice - Answer Peer", "WebSocket",
                "Test SDP answer flow via /voice/peer/answer endpoint"),
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

    /// <summary>
    /// Creates a game session with voice enabled.
    /// </summary>
    private async Task<(Guid sessionId, Guid? voiceRoomId)?> CreateVoiceEnabledSessionAsync(BannouClient client, string sessionName)
    {
        var createRequest = new CreateGameSessionRequest
        {
            SessionName = sessionName,
            GameType = CreateGameSessionRequestGameType.Arcadia,
            MaxPlayers = 4,
            IsPrivate = false
            // Note: Voice is enabled when joining with a voice endpoint
        };

        try
        {
            var response = (await client.InvokeAsync<CreateGameSessionRequest, JsonElement>(
                "POST",
                "/sessions/create",
                createRequest,
                timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

            var sessionIdStr = response.TryGetProperty("sessionId", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrEmpty(sessionIdStr))
            {
                Console.WriteLine("   No sessionId in create response");
                return null;
            }

            var sessionId = Guid.Parse(sessionIdStr);

            // Voice room ID might be in the response
            Guid? voiceRoomId = null;
            if (response.TryGetProperty("voiceRoomId", out var voiceIdProp) &&
                voiceIdProp.ValueKind == JsonValueKind.String)
            {
                voiceRoomId = Guid.Parse(voiceIdProp.GetString()!);
            }

            return (sessionId, voiceRoomId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create session: {ex.Message}");
            return null;
        }
    }

    #endregion

    private void TestVoiceRoomCreationViaGameSession(string[] args)
    {
        Console.WriteLine("=== Voice Room via GameSession Test ===");
        Console.WriteLine("Testing voice room creation when joining game session with voice enabled...");

        try
        {
            var result = Task.Run(async () =>
            {
                // Create dedicated test account and client
                var authResult = await CreateTestAccountAsync("voice_room");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                // Create a voice-enabled session
                var createResult = await CreateVoiceEnabledSessionAsync(client, $"VoiceTest_{DateTime.Now.Ticks}");
                if (createResult == null)
                {
                    return false;
                }

                var sessionId = createResult.Value.sessionId;
                Console.WriteLine($"   Created session {sessionId}");

                // Join the session
                Console.WriteLine("   Joining session...");
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
                    Console.WriteLine("   Failed to join session");
                    return false;
                }

                // Check for voice connection info in response
                if (joinResponse.TryGetProperty("voice", out var voiceProp) &&
                    voiceProp.ValueKind == JsonValueKind.Object)
                {
                    var voiceEnabled = voiceProp.TryGetProperty("voiceEnabled", out var enabledProp) && enabledProp.GetBoolean();
                    var hasRoomId = voiceProp.TryGetProperty("roomId", out var roomIdProp) &&
                                    roomIdProp.ValueKind == JsonValueKind.String;

                    Console.WriteLine($"   Voice enabled: {voiceEnabled}");
                    Console.WriteLine($"   Has room ID: {hasRoomId}");

                    if (voiceEnabled && hasRoomId)
                    {
                        Console.WriteLine($"   Voice room ID: {roomIdProp.GetString()}");
                        return true;
                    }
                }

                Console.WriteLine("   Voice info not found in join response");
                return false;
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Voice room via GameSession test");
            }
            else
            {
                Console.WriteLine("FAILED Voice room via GameSession test");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Voice room test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestVoiceEventOnlyPattern(string[] args)
    {
        Console.WriteLine("=== Voice Event-Only Pattern Test ===");
        Console.WriteLine("Testing that peers are NOT in join response (event-only architecture)...");

        try
        {
            var result = Task.Run(async () =>
            {
                // Create dedicated test account and client
                var authResult = await CreateTestAccountAsync("voice_event");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                // Create and join a voice-enabled session
                var createResult = await CreateVoiceEnabledSessionAsync(client, $"EventOnlyTest_{DateTime.Now.Ticks}");
                if (createResult == null)
                {
                    return false;
                }

                var sessionId = createResult.Value.sessionId;
                Console.WriteLine($"   Created session {sessionId}");

                // Join the session
                var joinRequest = new JoinGameSessionRequest
                {
                    SessionId = sessionId
                };

                var joinResponse = (await client.InvokeAsync<JoinGameSessionRequest, JsonElement>(
                    "POST",
                    "/sessions/join",
                    joinRequest,
                    timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                // Verify voice section structure - should NOT have peers array
                if (joinResponse.TryGetProperty("voice", out var voiceProp) &&
                    voiceProp.ValueKind == JsonValueKind.Object)
                {
                    // Check that peers is NOT present (event-only pattern)
                    var hasPeers = voiceProp.TryGetProperty("peers", out var peersProp) &&
                                    peersProp.ValueKind == JsonValueKind.Array;

                    if (hasPeers)
                    {
                        Console.WriteLine("   FAIL: 'peers' array found in response - should be event-only!");
                        return false;
                    }

                    // Check that minimal fields ARE present
                    var hasVoiceEnabled = voiceProp.TryGetProperty("voiceEnabled", out _);
                    var hasRoomId = voiceProp.TryGetProperty("roomId", out _);
                    var hasTier = voiceProp.TryGetProperty("tier", out _);
                    var hasCodec = voiceProp.TryGetProperty("codec", out _);
                    var hasStunServers = voiceProp.TryGetProperty("stunServers", out _);

                    Console.WriteLine($"   voiceEnabled: {hasVoiceEnabled}");
                    Console.WriteLine($"   roomId: {hasRoomId}");
                    Console.WriteLine($"   tier: {hasTier}");
                    Console.WriteLine($"   codec: {hasCodec}");
                    Console.WriteLine($"   stunServers: {hasStunServers}");
                    Console.WriteLine($"   peers: NOT present (correct - event-only pattern)");

                    return hasVoiceEnabled && hasRoomId;
                }

                Console.WriteLine("   Voice section not found in join response");
                return false;
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Voice event-only pattern test");
            }
            else
            {
                Console.WriteLine("FAILED Voice event-only pattern test");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Voice event-only pattern test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestTwoClientVoicePeerEvents(string[] args)
    {
        Console.WriteLine("=== Voice Peer Events (2 Clients) Test ===");
        Console.WriteLine("Testing VoicePeerJoinedEvent delivery when second client joins...");

        try
        {
            var result = Task.Run(async () =>
            {
                // Create two test accounts
                var auth1 = await CreateTestAccountAsync("voice_peer1");
                var auth2 = await CreateTestAccountAsync("voice_peer2");

                if (auth1 == null || auth2 == null)
                {
                    Console.WriteLine("   Failed to create test accounts");
                    return false;
                }

                await using var client1 = await CreateConnectedClientAsync(auth1.Value.accessToken, auth1.Value.connectUrl);
                await using var client2 = await CreateConnectedClientAsync(auth2.Value.accessToken, auth2.Value.connectUrl);

                if (client1 == null || client2 == null)
                {
                    Console.WriteLine("   Failed to create clients");
                    return false;
                }

                // Client 1 creates and joins a voice-enabled session
                var createResult = await CreateVoiceEnabledSessionAsync(client1, $"TwoClientTest_{DateTime.Now.Ticks}");
                if (createResult == null)
                {
                    Console.WriteLine("   Failed to create session");
                    return false;
                }

                var sessionId = createResult.Value.sessionId;
                Console.WriteLine($"   Client 1 created session {sessionId}");

                // Client 1 joins
                var joinRequest1 = new JoinGameSessionRequest { SessionId = sessionId };
                var joinResponse1 = (await client1.InvokeAsync<JoinGameSessionRequest, JsonElement>(
                    "POST",
                    "/sessions/join",
                    joinRequest1,
                    timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                var join1Success = joinResponse1.TryGetProperty("success", out var s1) && s1.GetBoolean();
                if (!join1Success)
                {
                    Console.WriteLine("   Client 1 failed to join");
                    return false;
                }
                Console.WriteLine($"   Client 1 joined, session: {client1.SessionId}");

                // Set up event listener on client 1 for peer joined event
                var peerJoinedReceived = new TaskCompletionSource<bool>();
                string? receivedPeerSessionId = null;

                client1.OnEvent("voice_peer_joined", (json) =>
                {
                    Console.WriteLine($"   Client 1 received voice_peer_joined event");
                    try
                    {
                        var eventData = JsonDocument.Parse(json).RootElement;
                        if (eventData.TryGetProperty("peer", out var peerProp) &&
                            peerProp.TryGetProperty("peer_session_id", out var peerIdProp))
                        {
                            receivedPeerSessionId = peerIdProp.GetString();
                            Console.WriteLine($"   Received VoicePeerJoinedEvent for peer: {receivedPeerSessionId}");
                            peerJoinedReceived.TrySetResult(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   Failed to parse event: {ex.Message}");
                    }
                });

                // Client 2 joins the same session
                Console.WriteLine("   Client 2 joining session...");
                var joinRequest2 = new JoinGameSessionRequest { SessionId = sessionId };
                var joinResponse2 = (await client2.InvokeAsync<JoinGameSessionRequest, JsonElement>(
                    "POST",
                    "/sessions/join",
                    joinRequest2,
                    timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                var join2Success = joinResponse2.TryGetProperty("success", out var s2) && s2.GetBoolean();
                if (!join2Success)
                {
                    Console.WriteLine("   Client 2 failed to join");
                    return false;
                }
                Console.WriteLine($"   Client 2 joined, session: {client2.SessionId}");

                // Wait for peer joined event on client 1
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                var completedTask = await Task.WhenAny(peerJoinedReceived.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Console.WriteLine("   Timeout waiting for VoicePeerJoinedEvent");
                    Console.WriteLine("   FAIL: VoicePeerJoinedEvent was not received within timeout");
                    return false;
                }

                // Verify the peer session ID matches client 2
                if (receivedPeerSessionId == client2.SessionId)
                {
                    Console.WriteLine($"   Verified: peer session ID matches client 2");
                    return true;
                }
                else
                {
                    Console.WriteLine($"   Peer session ID mismatch: expected {client2.SessionId}, got {receivedPeerSessionId}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Voice peer events test");
            }
            else
            {
                Console.WriteLine("FAILED Voice peer events test");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Voice peer events test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestAnswerPeerEndpoint(string[] args)
    {
        Console.WriteLine("=== Voice Answer Peer Test ===");
        Console.WriteLine("Testing /voice/peer/answer endpoint for SDP answer delivery...");

        try
        {
            var result = Task.Run(async () =>
            {
                // Create two test accounts
                var auth1 = await CreateTestAccountAsync("voice_ans1");
                var auth2 = await CreateTestAccountAsync("voice_ans2");

                if (auth1 == null || auth2 == null)
                {
                    Console.WriteLine("   Failed to create test accounts");
                    return false;
                }

                await using var client1 = await CreateConnectedClientAsync(auth1.Value.accessToken, auth1.Value.connectUrl);
                await using var client2 = await CreateConnectedClientAsync(auth2.Value.accessToken, auth2.Value.connectUrl);

                if (client1 == null || client2 == null)
                {
                    Console.WriteLine("   Failed to create clients");
                    return false;
                }

                // Create and join a voice-enabled session
                var createResult = await CreateVoiceEnabledSessionAsync(client1, $"AnswerPeerTest_{DateTime.Now.Ticks}");
                if (createResult == null)
                {
                    Console.WriteLine("   Failed to create session");
                    return false;
                }

                var sessionId = createResult.Value.sessionId;
                Console.WriteLine($"   Created session {sessionId}");

                // Both clients join
                var joinRequest = new JoinGameSessionRequest { SessionId = sessionId };

                var join1 = (await client1.InvokeAsync<JoinGameSessionRequest, JsonElement>(
                    "POST", "/sessions/join", joinRequest, timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                var join2 = (await client2.InvokeAsync<JoinGameSessionRequest, JsonElement>(
                    "POST", "/sessions/join", joinRequest, timeout: TimeSpan.FromSeconds(15))).GetResultOrThrow();

                var success1 = join1.TryGetProperty("success", out var s1) && s1.GetBoolean();
                var success2 = join2.TryGetProperty("success", out var s2) && s2.GetBoolean();

                if (!success1 || !success2)
                {
                    Console.WriteLine("   Failed to join session with both clients");
                    return false;
                }

                Console.WriteLine($"   Both clients joined session");
                Console.WriteLine($"   Client 1 session: {client1.SessionId}");
                Console.WriteLine($"   Client 2 session: {client2.SessionId}");

                // Get voice room ID from join response
                Guid? voiceRoomId = null;
                if (join1.TryGetProperty("voice", out var voiceProp) &&
                    voiceProp.TryGetProperty("roomId", out var roomIdProp) &&
                    roomIdProp.ValueKind == JsonValueKind.String)
                {
                    voiceRoomId = Guid.Parse(roomIdProp.GetString()!);
                    Console.WriteLine($"   Voice room ID: {voiceRoomId}");
                }

                if (voiceRoomId == null)
                {
                    Console.WriteLine("   FAIL: No voice room ID in response - voice is not enabled");
                    return false;
                }

                // Set up event listener on client 1 for peer updated event
                var peerUpdatedReceived = new TaskCompletionSource<bool>();
                string? receivedSdpAnswer = null;

                client1.OnEvent("voice_peer_updated", (json) =>
                {
                    Console.WriteLine($"   Client 1 received voice_peer_updated event");
                    try
                    {
                        var eventData = JsonDocument.Parse(json).RootElement;
                        if (eventData.TryGetProperty("peer", out var peerProp) &&
                            peerProp.TryGetProperty("sdp_offer", out var sdpProp))
                        {
                            receivedSdpAnswer = sdpProp.GetString();
                            Console.WriteLine($"   Received VoicePeerUpdatedEvent with SDP");
                            peerUpdatedReceived.TrySetResult(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   Failed to parse event: {ex.Message}");
                    }
                });

                // Client 2 sends SDP answer to Client 1
                Console.WriteLine("   Client 2 sending SDP answer to Client 1...");

                var answerRequest = new AnswerPeerRequest
                {
                    RoomId = voiceRoomId.Value,
                    SenderSessionId = client2.SessionId ?? "unknown",
                    TargetSessionId = client1.SessionId ?? "unknown",
                    SdpAnswer = "v=0\r\no=- 12345 67890 IN IP4 127.0.0.1\r\ns=Test SDP Answer\r\n",
                    IceCandidates = new List<string> { "candidate:1 1 UDP 2130706431 192.168.1.1 12345 typ host" }
                };

                try
                {
                    await client2.InvokeAsync<AnswerPeerRequest, JsonElement>(
                        "POST",
                        "/voice/peer/answer",
                        answerRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    Console.WriteLine("   SDP answer sent successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   FAIL: Answer peer call failed: {ex.Message}");
                    return false;
                }

                // Wait for peer updated event on client 1
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                var completedTask = await Task.WhenAny(peerUpdatedReceived.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Console.WriteLine("   FAIL: Timeout waiting for VoicePeerUpdatedEvent");
                    return false;
                }

                Console.WriteLine($"   Received SDP answer on Client 1: {receivedSdpAnswer?[..Math.Min(50, receivedSdpAnswer.Length)]}...");
                Console.WriteLine("   Voice answer peer flow completed successfully");
                return true;
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Voice answer peer test");
            }
            else
            {
                Console.WriteLine("FAILED Voice answer peer test");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Voice answer peer test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}
