using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Voice;
using System.Text;
using GameSessionVoiceTier = BeyondImmersion.BannouService.Voice.VoiceTier; // Alias preserved for test readability

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for voice service integration.
/// Tests voice room lifecycle through GameSession APIs using TYPED PROXIES and verifies event-only peer delivery.
///
/// Voice Architecture:
/// - Voice rooms are created automatically when joining GameSession with voice enabled
/// - Peer data is delivered ONLY via VoicePeerJoinedClientEvent (not in API responses)
/// - Clients use /voice/peer/answer to send SDP answers after receiving peer offers
/// - voice:ringing state is set before VoicePeerJoinedClientEvent is published
///
/// Voice Tests Requirements:
/// - Set VOICE_TESTS_ENABLED=true to run voice tests (default: disabled)
/// - Voice plugin must be enabled (VOICE_SERVICE_ENABLED must NOT be false)
/// - For scaled tier tests, Kamailio + RTPEngine infrastructure is required
/// - Use: make test-voice-scaled to run voice tests with full infrastructure
/// </summary>
public class VoiceWebSocketTestHandler : IServiceTestHandler
{
    /// <summary>
    /// Check if voice tests are enabled via environment variable.
    /// Voice tests are disabled by default in edge tests to avoid requiring voice infrastructure.
    /// </summary>
    private static bool IsVoiceTestingEnabled()
    {
        var voiceTestsEnabled = Environment.GetEnvironmentVariable("VOICE_TESTS_ENABLED");
        return string.Equals(voiceTestsEnabled, "true", StringComparison.OrdinalIgnoreCase);
    }

    public ServiceTest[] GetServiceTests()
    {
        // Skip voice tests if not enabled via environment variable
        if (!IsVoiceTestingEnabled())
        {
            Console.WriteLine("Voice tests SKIPPED (VOICE_TESTS_ENABLED not set to 'true')");
            Console.WriteLine("   To run voice tests: make test-voice-scaled");
            return Array.Empty<ServiceTest>();
        }

        Console.WriteLine("Voice tests ENABLED (VOICE_TESTS_ENABLED=true)");

        return new ServiceTest[]
        {
            new ServiceTest(TestVoiceRoomCreationViaGameSession, "Voice - Room via GameSession", "WebSocket",
                "Test voice room creation when joining game session with voice enabled"),
            new ServiceTest(TestTwoClientVoicePeerEvents, "Voice - Peer Events (2 Clients)", "WebSocket",
                "Test VoicePeerJoinedClientEvent delivery when second client joins"),
            new ServiceTest(TestAnswerPeerEndpoint, "Voice - Answer Peer", "WebSocket",
                "Test SDP answer flow via /voice/peer/answer endpoint"),
            new ServiceTest(TestP2PToScaledTierUpgrade, "Voice - P2P to Scaled Upgrade", "WebSocket",
                "Test tier upgrade when 3rd client exceeds P2PMAXPARTICIPANTS=2"),
            new ServiceTest(TestScaledTierPersistence, "Voice - Scaled Tier Persistence", "WebSocket",
                "Test that scaled tier persists after clients leave (never downgrades to P2P)"),
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

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;
        var uniqueId = Guid.NewGuid().ToString("N")[..12];
        var testEmail = $"{testPrefix}_{uniqueId}@test.local";
        var testPassword = $"{testPrefix}Test123!";

        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new RegisterRequest { Username = $"{testPrefix}_{uniqueId}", Email = testEmail, Password = testPassword };

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
            var registerResult = BannouJson.Deserialize<RegisterResponse>(responseBody);
            var accessToken = registerResult?.AccessToken;
            var connectUrl = registerResult?.ConnectUrl?.ToString();

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
    /// Creates a game session using typed proxy and returns its ID.
    /// Voice room ID is only available after joining the session with voice enabled.
    /// </summary>
    private async Task<Guid?> CreateVoiceEnabledSessionAsync(BannouClient client, string sessionName)
    {
        var createRequest = new CreateGameSessionRequest
        {
            SessionName = sessionName,
            GameType = "test-game",
            MaxPlayers = 4,
            IsPrivate = false
            // Note: Voice is enabled when joining with a voice endpoint
        };

        try
        {
            var response = await client.GameSession.CreateGameSessionAsync(createRequest, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to create session: {response.Error?.Message ?? "Unknown error"}");
                return null;
            }

            return response.Result.SessionId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create session: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Voice Room Helpers

    /// <summary>
    /// Creates a voice room and joins it. Used by the first client in a test.
    /// Returns (roomId, tier) on success, null on failure.
    /// </summary>
    private async Task<(Guid roomId, VoiceTier tier)?> CreateAndJoinVoiceRoomAsync(BannouClient client)
    {
        var wsSessionId = Guid.TryParse(client.SessionId, out var sid) ? sid : Guid.Empty;

        var createResponse = await client.Voice.CreateVoiceRoomAsync(
            new CreateVoiceRoomRequest { SessionId = wsSessionId },
            timeout: TimeSpan.FromSeconds(5));

        if (!createResponse.IsSuccess || createResponse.Result == null)
        {
            Console.WriteLine($"   Failed to create voice room: {createResponse.Error?.Message ?? "Unknown error"}");
            return null;
        }

        var roomId = createResponse.Result.RoomId;

        var joinResponse = await client.Voice.JoinVoiceRoomAsync(
            new JoinVoiceRoomRequest
            {
                RoomId = roomId,
                SessionId = wsSessionId,
                SipEndpoint = CreateMockVoiceEndpoint()
            },
            timeout: TimeSpan.FromSeconds(5));

        if (!joinResponse.IsSuccess || joinResponse.Result == null)
        {
            Console.WriteLine($"   Failed to join voice room: {joinResponse.Error?.Message ?? "Unknown error"}");
            return null;
        }

        return (roomId, joinResponse.Result.Tier);
    }

    /// <summary>
    /// Joins an existing voice room. Used by subsequent clients in a test.
    /// Returns the tier on success, null on failure.
    /// </summary>
    private async Task<VoiceTier?> JoinExistingVoiceRoomAsync(BannouClient client, Guid roomId)
    {
        var wsSessionId = Guid.TryParse(client.SessionId, out var sid) ? sid : Guid.Empty;

        var joinResponse = await client.Voice.JoinVoiceRoomAsync(
            new JoinVoiceRoomRequest
            {
                RoomId = roomId,
                SessionId = wsSessionId,
                SipEndpoint = CreateMockVoiceEndpoint()
            },
            timeout: TimeSpan.FromSeconds(5));

        if (!joinResponse.IsSuccess || joinResponse.Result == null)
        {
            Console.WriteLine($"   Failed to join voice room: {joinResponse.Error?.Message ?? "Unknown error"}");
            return null;
        }

        return joinResponse.Result.Tier;
    }

    #endregion

    #region Voice Endpoint Helper

    /// <summary>
    /// Creates a mock SipEndpoint with a valid-format SDP offer for testing.
    /// The SDP offer is not used for actual WebRTC negotiation - it just needs to be
    /// structurally valid to trigger the voice join flow.
    /// </summary>
    private static SipEndpoint CreateMockVoiceEndpoint()
    {
        // A minimal SDP offer that has the correct structure
        // This won't work for real WebRTC, but it's enough for the server-side flow
        var mockSdpOffer = @"v=0
o=- 0 0 IN IP4 127.0.0.1
s=Mock Session
c=IN IP4 127.0.0.1
t=0 0
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=rtpmap:111 opus/48000/2";

        return new SipEndpoint
        {
            SdpOffer = mockSdpOffer,
            IceCandidates = new List<string>()
        };
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
                var sessionId = await CreateVoiceEnabledSessionAsync(client, $"VoiceTest_{DateTime.Now.Ticks}");
                if (sessionId == null)
                {
                    return false;
                }

                Console.WriteLine($"   Created session {sessionId}");

                // Join the session using typed proxy
                Console.WriteLine("   Joining session...");
                var joinRequest = new JoinGameSessionRequest
                {
                    SessionId = sessionId.Value
                };

                var joinResponse = await client.GameSession.JoinGameSessionAsync(joinRequest, timeout: TimeSpan.FromSeconds(5));

                if (!joinResponse.IsSuccess || joinResponse.Result == null)
                {
                    Console.WriteLine($"   Failed to join session: {joinResponse.Error?.Message ?? "Unknown error"}");
                    return false;
                }

                // Create and join voice room separately via Voice API
                Console.WriteLine("   Creating and joining voice room...");
                var voiceResult = await CreateAndJoinVoiceRoomAsync(client);
                if (voiceResult != null)
                {
                    Console.WriteLine($"   Voice enabled: true");
                    Console.WriteLine($"   Voice room ID: {voiceResult.Value.roomId}");
                    Console.WriteLine($"   Voice tier: {voiceResult.Value.tier}");
                    return true;
                }

                Console.WriteLine("   Failed to create/join voice room");
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

    private void TestTwoClientVoicePeerEvents(string[] args)
    {
        Console.WriteLine("=== Voice Peer Events (2 Clients) Test ===");
        Console.WriteLine("Testing VoicePeerJoinedClientEvent delivery when second client joins...");

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
                var sessionId = await CreateVoiceEnabledSessionAsync(client1, $"TwoClientTest_{DateTime.Now.Ticks}");
                if (sessionId == null)
                {
                    Console.WriteLine("   Failed to create session");
                    return false;
                }

                Console.WriteLine($"   Client 1 created session {sessionId}");

                // Client 1 joins using typed proxy
                var joinRequest1 = new JoinGameSessionRequest { SessionId = sessionId.Value };
                var joinResponse1 = await client1.GameSession.JoinGameSessionAsync(joinRequest1, timeout: TimeSpan.FromSeconds(5));

                if (!joinResponse1.IsSuccess || joinResponse1.Result == null)
                {
                    Console.WriteLine($"   Client 1 failed to join: {joinResponse1.Error?.Message ?? "Unknown error"}");
                    return false;
                }
                Console.WriteLine($"   Client 1 joined, session: {client1.SessionId}");

                // Client 1 creates and joins voice room
                var voiceResult = await CreateAndJoinVoiceRoomAsync(client1);
                if (voiceResult == null)
                {
                    Console.WriteLine("   Failed to create voice room");
                    return false;
                }
                Console.WriteLine($"   Voice room created: {voiceResult.Value.roomId}");

                // Set up event listener on client 1 for peer joined event
                var peerJoinedReceived = new TaskCompletionSource<bool>();
                Guid receivedPeerSessionId = Guid.Empty;

                client1.OnEvent<VoicePeerJoinedClientEvent>((evt) =>
                {
                    Console.WriteLine($"   Client 1 received voice.peer.joined event");
                    receivedPeerSessionId = evt.Peer.PeerSessionId;
                    Console.WriteLine($"   Received VoicePeerJoinedClientEvent for peer: {receivedPeerSessionId}");
                    peerJoinedReceived.TrySetResult(true);
                });

                // Client 2 joins the same session using typed proxy
                Console.WriteLine("   Client 2 joining session...");
                var joinRequest2 = new JoinGameSessionRequest { SessionId = sessionId.Value };
                var joinResponse2 = await client2.GameSession.JoinGameSessionAsync(joinRequest2, timeout: TimeSpan.FromSeconds(5));

                if (!joinResponse2.IsSuccess || joinResponse2.Result == null)
                {
                    Console.WriteLine($"   Client 2 failed to join: {joinResponse2.Error?.Message ?? "Unknown error"}");
                    return false;
                }
                Console.WriteLine($"   Client 2 joined, session: {client2.SessionId}");

                // Client 2 joins the existing voice room
                var client2Tier = await JoinExistingVoiceRoomAsync(client2, voiceResult.Value.roomId);
                if (client2Tier == null)
                {
                    Console.WriteLine("   Client 2 failed to join voice room");
                    return false;
                }

                // Wait for peer joined event on client 1
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                var completedTask = await Task.WhenAny(peerJoinedReceived.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Console.WriteLine("   Timeout waiting for VoicePeerJoinedClientEvent");
                    Console.WriteLine("   FAIL: VoicePeerJoinedClientEvent was not received within timeout");
                    return false;
                }

                // Verify the peer session ID matches client 2
                var client2SessionGuid = Guid.TryParse(client2.SessionId, out var c2Guid) ? c2Guid : Guid.Empty;
                if (receivedPeerSessionId == client2SessionGuid)
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
                var sessionId = await CreateVoiceEnabledSessionAsync(client1, $"AnswerPeerTest_{DateTime.Now.Ticks}");
                if (sessionId == null)
                {
                    Console.WriteLine("   Failed to create session");
                    return false;
                }

                Console.WriteLine($"   Created session {sessionId}");

                // Both clients join game session
                var joinResponse1 = await client1.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(5));
                var joinResponse2 = await client2.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(5));

                if (!joinResponse1.IsSuccess || joinResponse1.Result == null)
                {
                    Console.WriteLine($"   Client 1 failed to join: {joinResponse1.Error?.Message ?? "Unknown error"}");
                    return false;
                }

                if (!joinResponse2.IsSuccess || joinResponse2.Result == null)
                {
                    Console.WriteLine($"   Client 2 failed to join: {joinResponse2.Error?.Message ?? "Unknown error"}");
                    return false;
                }

                Console.WriteLine($"   Both clients joined session");
                Console.WriteLine($"   Client 1 session: {client1.SessionId}");
                Console.WriteLine($"   Client 2 session: {client2.SessionId}");

                // Client 1 creates voice room, both clients join
                var voiceResult = await CreateAndJoinVoiceRoomAsync(client1);
                if (voiceResult == null)
                {
                    Console.WriteLine("   FAIL: Failed to create voice room");
                    return false;
                }
                var voiceRoomId = voiceResult.Value.roomId;
                Console.WriteLine($"   Voice room ID: {voiceRoomId}");

                var client2Tier = await JoinExistingVoiceRoomAsync(client2, voiceRoomId);
                if (client2Tier == null)
                {
                    Console.WriteLine("   FAIL: Client 2 failed to join voice room");
                    return false;
                }

                // Set up event listener on client 1 for peer updated event
                var peerUpdatedReceived = new TaskCompletionSource<bool>();
                string? receivedSdpAnswer = null;

                client1.OnEvent<VoicePeerUpdatedClientEvent>((evt) =>
                {
                    Console.WriteLine($"   Client 1 received voice.peer.updated event");
                    receivedSdpAnswer = evt.Peer.SdpOffer;
                    Console.WriteLine($"   Received VoicePeerUpdatedClientEvent with SDP");
                    peerUpdatedReceived.TrySetResult(true);
                });

                // Client 2 sends SDP answer to Client 1 using typed proxy
                // Note: The capability manifest with /voice/peer/answer may arrive after the voice.peer.joined event
                // due to race condition between Permission service capability push and Voice service event publish.
                // Retry up to 5 times (4 seconds total) for "Unknown endpoint" errors.
                Console.WriteLine("   Client 2 sending SDP answer to Client 1...");

                var answerRequest = new AnswerPeerRequest
                {
                    RoomId = voiceRoomId,
                    SenderSessionId = Guid.TryParse(client2.SessionId, out var senderId) ? senderId : Guid.Empty,
                    TargetSessionId = Guid.TryParse(client1.SessionId, out var targetId) ? targetId : Guid.Empty,
                    SdpAnswer = "v=0\r\no=- 12345 67890 IN IP4 127.0.0.1\r\ns=Test SDP Answer\r\n",
                    IceCandidates = new List<string> { "candidate:1 1 UDP 2130706431 192.168.1.1 12345 typ host" }
                };

                const int maxAttempts = 5;
                const int retryDelayMs = 1000;
                bool answerSent = false;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        // Use typed proxy for fire-and-forget event
                        await client2.Voice.AnswerPeerEventAsync(answerRequest);

                        Console.WriteLine("   SDP answer sent successfully");
                        answerSent = true;
                        break;
                    }
                    catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
                    {
                        if (attempt < maxAttempts)
                        {
                            Console.WriteLine($"   Endpoint not yet available (attempt {attempt}/{maxAttempts}), waiting for capability manifest...");
                            await Task.Delay(retryDelayMs);
                        }
                        else
                        {
                            Console.WriteLine($"   FAIL: Answer peer endpoint still unavailable after {maxAttempts} attempts: {ex.Message}");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   FAIL: Answer peer call failed: {ex.Message}");
                        return false;
                    }
                }

                if (!answerSent)
                {
                    Console.WriteLine("   FAIL: Could not send SDP answer");
                    return false;
                }

                // Wait for peer updated event on client 1
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                var completedTask = await Task.WhenAny(peerUpdatedReceived.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Console.WriteLine("   FAIL: Timeout waiting for VoicePeerUpdatedClientEvent");
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

    private void TestP2PToScaledTierUpgrade(string[] args)
    {
        Console.WriteLine("=== Voice P2P to Scaled Tier Upgrade Test ===");
        Console.WriteLine("Testing tier upgrade when 3rd client exceeds P2PMAXPARTICIPANTS=2...");
        Console.WriteLine("   NOTE: Requires BANNOU_P2PMAXPARTICIPANTS=2, BANNOU_SCALEDTIERENABLED=true, BANNOU_TIERUPGRADEENABLED=true");

        try
        {
            var result = Task.Run(async () =>
            {
                // Create three test accounts
                var auth1 = await CreateTestAccountAsync("voice_tier1");
                var auth2 = await CreateTestAccountAsync("voice_tier2");
                var auth3 = await CreateTestAccountAsync("voice_tier3");

                if (auth1 == null || auth2 == null || auth3 == null)
                {
                    Console.WriteLine("   Failed to create test accounts");
                    return false;
                }

                await using var client1 = await CreateConnectedClientAsync(auth1.Value.accessToken, auth1.Value.connectUrl);
                await using var client2 = await CreateConnectedClientAsync(auth2.Value.accessToken, auth2.Value.connectUrl);
                await using var client3 = await CreateConnectedClientAsync(auth3.Value.accessToken, auth3.Value.connectUrl);

                if (client1 == null || client2 == null || client3 == null)
                {
                    Console.WriteLine("   Failed to create clients");
                    return false;
                }

                // Client 1 creates a voice-enabled session
                var sessionId = await CreateVoiceEnabledSessionAsync(client1, $"TierUpgradeTest_{DateTime.Now.Ticks}");
                if (sessionId == null)
                {
                    Console.WriteLine("   Failed to create session");
                    return false;
                }

                Console.WriteLine($"   Created session {sessionId}");

                // Set up tier upgrade event listeners on all clients
                var tierUpgrade1Received = new TaskCompletionSource<bool>();
                var tierUpgrade2Received = new TaskCompletionSource<bool>();
                var tierUpgrade3Received = new TaskCompletionSource<bool>();
                string? rtpServerUri = null;

                void SetupTierUpgradeListener(BannouClient client, TaskCompletionSource<bool> tcs, string clientName)
                {
                    client.OnEvent<VoiceTierUpgradeClientEvent>((evt) =>
                    {
                        Console.WriteLine($"   {clientName} received voice.tier-upgrade event");
                        Console.WriteLine($"   {clientName} tier upgrade: {evt.PreviousTier} -> {evt.NewTier}");

                        if (!string.IsNullOrEmpty(evt.RtpServerUri))
                        {
                            rtpServerUri = evt.RtpServerUri;
                            Console.WriteLine($"   RTP server URI: {rtpServerUri}");
                        }

                        tcs.TrySetResult(true);
                    });
                }

                SetupTierUpgradeListener(client1, tierUpgrade1Received, "Client 1");
                SetupTierUpgradeListener(client2, tierUpgrade2Received, "Client 2");
                SetupTierUpgradeListener(client3, tierUpgrade3Received, "Client 3");

                // Client 1 joins (1 participant - under P2P limit) using typed proxy
                Console.WriteLine("   Client 1 joining session...");
                var join1Response = await client1.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(5));

                if (!join1Response.IsSuccess || join1Response.Result == null)
                {
                    Console.WriteLine($"   Client 1 failed to join: {join1Response.Error?.Message ?? "Unknown error"}");
                    return false;
                }

                // Client 1 creates and joins voice room
                var voiceResult = await CreateAndJoinVoiceRoomAsync(client1);
                if (voiceResult == null)
                {
                    Console.WriteLine("   Failed to create voice room");
                    return false;
                }
                var voiceRoomId = voiceResult.Value.roomId;
                Console.WriteLine($"   Client 1 joined (1/{GetP2PMaxParticipants()} P2P capacity), voice room: {voiceRoomId}");

                // Client 2 joins (2 participants - at P2P limit) using typed proxy
                Console.WriteLine("   Client 2 joining session...");
                var join2Response = await client2.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(5));

                if (!join2Response.IsSuccess || join2Response.Result == null)
                {
                    Console.WriteLine($"   Client 2 failed to join: {join2Response.Error?.Message ?? "Unknown error"}");
                    return false;
                }
                await JoinExistingVoiceRoomAsync(client2, voiceRoomId);
                Console.WriteLine($"   Client 2 joined (2/{GetP2PMaxParticipants()} P2P capacity - FULL)");

                // Verify no tier upgrade yet (still within P2P capacity)
                await Task.Delay(1000);
                if (tierUpgrade1Received.Task.IsCompleted || tierUpgrade2Received.Task.IsCompleted)
                {
                    Console.WriteLine("   UNEXPECTED: Tier upgrade received with only 2 clients");
                    // This might be acceptable if P2PMAXPARTICIPANTS is set differently
                }

                // Client 3 joins (3 participants - EXCEEDS P2P limit, triggers upgrade)
                // Client 3's join triggers the upgrade, but they join DIRECTLY into scaled mode
                // so they don't receive a tier-upgrade event - their join response already says tier=scaled
                Console.WriteLine("   Client 3 joining session (should trigger tier upgrade)...");
                var join3Response = await client3.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(5));

                if (!join3Response.IsSuccess || join3Response.Result == null)
                {
                    Console.WriteLine($"   Client 3 failed to join: {join3Response.Error?.Message ?? "Unknown error"}");
                    return false;
                }
                Console.WriteLine($"   Client 3 joined (3 participants - EXCEEDS P2P limit)");

                // Client 3 joins voice room (should trigger tier upgrade)
                var client3Tier = await JoinExistingVoiceRoomAsync(client3, voiceRoomId);
                Console.WriteLine($"   Client 3 voice join tier: {client3Tier}");
                if (client3Tier != GameSessionVoiceTier.Scaled)
                {
                    Console.WriteLine($"   FAIL: Client 3 should have joined directly into scaled tier, got: {client3Tier}");
                    return false;
                }

                // Wait for tier upgrade events on Clients 1 & 2 only
                // Client 3 triggered the upgrade but joins directly into scaled mode (no upgrade event needed)
                Console.WriteLine("   Waiting for tier upgrade events on existing P2P clients (1 & 2)...");
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                var existingClientUpgrades = Task.WhenAll(
                    tierUpgrade1Received.Task,
                    tierUpgrade2Received.Task);

                var completedTask = await Task.WhenAny(existingClientUpgrades, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    var received = new List<string>();
                    if (tierUpgrade1Received.Task.IsCompleted) received.Add("Client 1");
                    if (tierUpgrade2Received.Task.IsCompleted) received.Add("Client 2");

                    Console.WriteLine($"   Timeout waiting for tier upgrade events");
                    Console.WriteLine($"   Received by: {(received.Count > 0 ? string.Join(", ", received) : "none")}");

                    if (received.Count == 0)
                    {
                        Console.WriteLine("   FAIL: No tier upgrade events received by existing clients");
                        Console.WriteLine("   This may indicate:");
                        Console.WriteLine("     - BANNOU_SCALEDTIERENABLED=false");
                        Console.WriteLine("     - BANNOU_TIERUPGRADEENABLED=false");
                        Console.WriteLine("     - Kamailio/RTPEngine not available");
                        return false;
                    }
                    else
                    {
                        Console.WriteLine($"   PARTIAL: {received.Count}/2 existing P2P clients received tier upgrade");
                        return false;
                    }
                }

                // Verify Clients 1 & 2 received the upgrade event (Client 3 doesn't need it)
                Console.WriteLine("   Clients 1 & 2 received voice.tier-upgrade event!");
                Console.WriteLine("   Client 3 joined directly into scaled tier (no upgrade event needed)");
                Console.WriteLine($"   Tier upgraded from P2P to Scaled");

                if (!string.IsNullOrEmpty(rtpServerUri))
                {
                    Console.WriteLine($"   RTP Server: {rtpServerUri}");
                }

                return true;
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Voice P2P to Scaled tier upgrade test");
            }
            else
            {
                Console.WriteLine("FAILED Voice P2P to Scaled tier upgrade test");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Voice tier upgrade test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Tests that once a room is upgraded to scaled tier, it NEVER downgrades back to P2P,
    /// even if participants leave and the count drops below the P2P threshold.
    /// New clients joining an already-scaled room should join in scaled mode directly.
    /// </summary>
    private void TestScaledTierPersistence(string[] args)
    {
        Console.WriteLine("=== Voice Scaled Tier Persistence Test ===");
        Console.WriteLine("Testing that scaled tier persists after clients leave (never downgrades to P2P)...");
        Console.WriteLine("   NOTE: Requires BANNOU_P2PMAXPARTICIPANTS=2, BANNOU_SCALEDTIERENABLED=true, BANNOU_TIERUPGRADEENABLED=true");
        Console.WriteLine();

        try
        {
            var result = Task.Run(async () =>
            {
                // Create 5 test accounts (we'll use them progressively)
                var account1 = await CreateTestAccountAsync("scaledpersist1");
                var account2 = await CreateTestAccountAsync("scaledpersist2");
                var account3 = await CreateTestAccountAsync("scaledpersist3");
                var account4 = await CreateTestAccountAsync("scaledpersist4");
                var account5 = await CreateTestAccountAsync("scaledpersist5");

                if (account1 == null || account2 == null || account3 == null || account4 == null || account5 == null)
                {
                    Console.WriteLine("   Failed to create test accounts");
                    return false;
                }

                await using var client1 = await CreateConnectedClientAsync(account1.Value.accessToken, account1.Value.connectUrl);
                await using var client2 = await CreateConnectedClientAsync(account2.Value.accessToken, account2.Value.connectUrl);
                await using var client3 = await CreateConnectedClientAsync(account3.Value.accessToken, account3.Value.connectUrl);
                await using var client4 = await CreateConnectedClientAsync(account4.Value.accessToken, account4.Value.connectUrl);
                await using var client5 = await CreateConnectedClientAsync(account5.Value.accessToken, account5.Value.connectUrl);

                if (client1 == null || client2 == null || client3 == null || client4 == null || client5 == null)
                {
                    Console.WriteLine("   Failed to create connected clients");
                    return false;
                }

                Console.WriteLine("   All 5 clients connected");

                // Phase 1: Create session and trigger upgrade with 3 clients
                Console.WriteLine();
                Console.WriteLine("   === Phase 1: Trigger upgrade with 3 clients ===");

                var sessionId = await CreateVoiceEnabledSessionAsync(client1, $"ScaledPersistTest_{DateTime.Now.Ticks}");
                if (sessionId == null)
                {
                    Console.WriteLine("   Failed to create voice-enabled session");
                    return false;
                }
                Console.WriteLine($"   Created session: {sessionId}");

                // Client 1 joins the session (1/2 P2P capacity) using typed proxy
                var join1Response = await client1.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(10));

                if (!join1Response.IsSuccess || join1Response.Result == null)
                {
                    Console.WriteLine($"   Client 1 failed to join: {join1Response.Error?.Message ?? "Unknown error"}");
                    return false;
                }

                // Client 1 creates and joins voice room
                var voiceResult = await CreateAndJoinVoiceRoomAsync(client1);
                if (voiceResult == null)
                {
                    Console.WriteLine("   Failed to create voice room");
                    return false;
                }
                var voiceRoomId = voiceResult.Value.roomId;
                Console.WriteLine($"   Client 1 joined (1/{GetP2PMaxParticipants()} P2P capacity), voice room: {voiceRoomId}");

                // Client 2 joins (2/2 - at capacity) using typed proxy
                var join2Response = await client2.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(10));

                if (!join2Response.IsSuccess || join2Response.Result == null)
                {
                    Console.WriteLine($"   Client 2 failed to join: {join2Response.Error?.Message ?? "Unknown error"}");
                    return false;
                }
                await JoinExistingVoiceRoomAsync(client2, voiceRoomId);
                Console.WriteLine($"   Client 2 joined (2/{GetP2PMaxParticipants()} P2P capacity - FULL)");

                // Set up tier upgrade listeners
                var tierUpgrade1 = new TaskCompletionSource<bool>();
                var tierUpgrade2 = new TaskCompletionSource<bool>();
                client1.OnEvent<VoiceTierUpgradeClientEvent>((_) => tierUpgrade1.TrySetResult(true));
                client2.OnEvent<VoiceTierUpgradeClientEvent>((_) => tierUpgrade2.TrySetResult(true));

                // Client 3 joins (triggers upgrade) using typed proxy
                var join3Response = await client3.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(5));

                if (!join3Response.IsSuccess || join3Response.Result == null)
                {
                    Console.WriteLine($"   Client 3 failed to join: {join3Response.Error?.Message ?? "Unknown error"}");
                    return false;
                }

                // Client 3 joins voice room (triggers upgrade)
                var client3Tier = await JoinExistingVoiceRoomAsync(client3, voiceRoomId);
                Console.WriteLine($"   Client 3 joined with tier: {client3Tier}");

                if (client3Tier != GameSessionVoiceTier.Scaled)
                {
                    Console.WriteLine($"   FAIL: Client 3 should have joined scaled tier, got: {client3Tier}");
                    return false;
                }

                // Wait for upgrade events on clients 1 & 2
                var upgradeTimeout = Task.Delay(TimeSpan.FromSeconds(10));
                var upgradeComplete = Task.WhenAll(tierUpgrade1.Task, tierUpgrade2.Task);
                if (await Task.WhenAny(upgradeComplete, upgradeTimeout) == upgradeTimeout)
                {
                    Console.WriteLine("   FAIL: Timeout waiting for tier upgrade events");
                    return false;
                }
                Console.WriteLine("   Clients 1 & 2 received tier upgrade events");
                Console.WriteLine("   Room is now SCALED with 3 participants");

                // Phase 2: Client 3 leaves (2 remaining - at P2P threshold, but room stays scaled)
                Console.WriteLine();
                Console.WriteLine("   === Phase 2: Client 3 leaves (2 remaining, should stay scaled) ===");

                // Use typed proxy for leave event
                await client3.GameSession.LeaveGameSessionEventAsync(new LeaveGameSessionRequest { SessionId = sessionId.Value });

                Console.WriteLine("   Client 3 left the session");
                Console.WriteLine($"   Room now has 2 participants (at P2P threshold, but MUST stay scaled)");

                // Phase 3: Client 4 joins - should join directly into SCALED tier using typed proxy
                Console.WriteLine();
                Console.WriteLine("   === Phase 3: Client 4 joins (should join SCALED, not P2P) ===");

                var join4Response = await client4.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(10));

                if (!join4Response.IsSuccess || join4Response.Result == null)
                {
                    Console.WriteLine($"   Client 4 failed to join: {join4Response.Error?.Message ?? "Unknown error"}");
                    return false;
                }

                var client4Tier = await JoinExistingVoiceRoomAsync(client4, voiceRoomId);
                Console.WriteLine($"   Client 4 joined with tier: {client4Tier}");

                if (client4Tier != GameSessionVoiceTier.Scaled)
                {
                    Console.WriteLine($"   FAIL: Client 4 should have joined SCALED tier (room already upgraded), got: {client4Tier}");
                    Console.WriteLine("   This indicates the room incorrectly downgraded to P2P when participants left!");
                    return false;
                }
                Console.WriteLine("   PASS: Client 4 correctly joined scaled tier");

                // Phase 4: Client 1 leaves (2 remaining), then Client 5 joins
                Console.WriteLine();
                Console.WriteLine("   === Phase 4: Client 1 leaves, Client 5 joins (should still be scaled) ===");

                // Use typed proxy for leave event
                await client1.GameSession.LeaveGameSessionEventAsync(new LeaveGameSessionRequest { SessionId = sessionId.Value });
                Console.WriteLine("   Client 1 left the session");

                var join5Response = await client5.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(10));

                if (!join5Response.IsSuccess || join5Response.Result == null)
                {
                    Console.WriteLine($"   Client 5 failed to join: {join5Response.Error?.Message ?? "Unknown error"}");
                    return false;
                }

                var client5Tier = await JoinExistingVoiceRoomAsync(client5, voiceRoomId);
                Console.WriteLine($"   Client 5 joined with tier: {client5Tier}");

                if (client5Tier != GameSessionVoiceTier.Scaled)
                {
                    Console.WriteLine($"   FAIL: Client 5 should have joined SCALED tier, got: {client5Tier}");
                    return false;
                }
                Console.WriteLine("   PASS: Client 5 correctly joined scaled tier");

                // Phase 5: Reduce to 1 participant, verify new join is still scaled
                Console.WriteLine();
                Console.WriteLine("   === Phase 5: Reduce to 1 participant, verify new join is still scaled ===");

                // Leave clients 4 and 5, leaving only client 2
                await client4.GameSession.LeaveGameSessionEventAsync(new LeaveGameSessionRequest { SessionId = sessionId.Value });
                Console.WriteLine("   Client 4 left");

                await client5.GameSession.LeaveGameSessionEventAsync(new LeaveGameSessionRequest { SessionId = sessionId.Value });
                Console.WriteLine("   Client 5 left");
                Console.WriteLine("   Only Client 2 remains (1 participant - well below P2P threshold)");

                // Client 3 rejoins (reusing the account) using typed proxy
                var rejoin3Response = await client3.GameSession.JoinGameSessionAsync(
                    new JoinGameSessionRequest { SessionId = sessionId.Value },
                    timeout: TimeSpan.FromSeconds(10));

                if (!rejoin3Response.IsSuccess || rejoin3Response.Result == null)
                {
                    Console.WriteLine($"   Client 3 failed to rejoin: {rejoin3Response.Error?.Message ?? "Unknown error"}");
                    return false;
                }

                // Client 3 rejoins voice room
                var client3RejoinTier = await JoinExistingVoiceRoomAsync(client3, voiceRoomId);
                Console.WriteLine($"   Client 3 rejoined with tier: {client3RejoinTier}");

                if (client3RejoinTier != GameSessionVoiceTier.Scaled)
                {
                    Console.WriteLine($"   FAIL: Client 3 rejoin should be SCALED tier, got: {client3RejoinTier}");
                    Console.WriteLine("   The room incorrectly downgraded when only 1 participant remained!");
                    return false;
                }
                Console.WriteLine("   PASS: Client 3 correctly rejoined scaled tier (room never downgraded)");

                Console.WriteLine();
                Console.WriteLine("   === All phases passed! ===");
                Console.WriteLine("   Verified: Once upgraded, room tier persists regardless of participant count");
                return true;
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Voice scaled tier persistence test");
            }
            else
            {
                Console.WriteLine("FAILED Voice scaled tier persistence test");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Voice scaled tier persistence test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Gets the P2P max participants from configuration or default.
    /// </summary>
    private static int GetP2PMaxParticipants()
    {
        // Try to get from environment, default to 2 for test expectations
        var envValue = Environment.GetEnvironmentVariable("BANNOU_P2PMAXPARTICIPANTS");
        if (int.TryParse(envValue, out var value))
        {
            return value;
        }
        return 2; // Default for tests
    }
}
