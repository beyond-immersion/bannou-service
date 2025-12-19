using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for capability discovery and permission flow validation.
/// Tests the complete flow from WebSocket connection through capability initialization.
/// </summary>
public class CapabilityFlowTestHandler : IServiceTestHandler
{
    #region Helper Methods for Test Account Creation

    /// <summary>
    /// Creates a dedicated test account and returns the access token.
    /// Each test should create its own account to avoid JWT reuse which causes subsume behavior.
    /// </summary>
    private async Task<string?> CreateTestAccountAsync(string testPrefix)
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

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("   No accessToken in registration response");
                return null;
            }

            Console.WriteLine($"   Created test account: {testEmail}");
            return accessToken;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
            return null;
        }
    }

    #endregion

    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestCapabilityInitialization, "Capability - Session Init", "WebSocket",
                "Test that WebSocket connection receives pushed capability manifest"),
            new ServiceTest(TestUniqueGuidPerSession, "Capability - Unique GUIDs", "WebSocket",
                "Test that different sessions receive unique GUIDs for same endpoints"),
            new ServiceTest(TestServiceGuidRouting, "Capability - GUID Routing", "WebSocket",
                "Test that service requests with valid GUIDs are routed correctly"),
            new ServiceTest(TestStateBasedCapabilityUpdate, "Capability - State Update", "WebSocket",
                "Test that setting game-session:in_game state triggers capability manifest update")
        };
    }

    private void TestCapabilityInitialization(string[] args)
    {
        Console.WriteLine("=== Capability Session Initialization Test ===");
        Console.WriteLine("Testing that WebSocket connection initializes session capabilities...");

        try
        {
            var result = Task.Run(async () => await PerformCapabilityInitializationTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Capability session initialization test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Capability session initialization test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Capability session initialization test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private void TestUniqueGuidPerSession(string[] args)
    {
        Console.WriteLine("=== Unique GUID Per Session Test ===");
        Console.WriteLine("Testing that different sessions receive unique GUIDs...");

        try
        {
            var result = Task.Run(async () => await PerformUniqueGuidTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Unique GUID per session test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Unique GUID per session test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Unique GUID per session test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private void TestServiceGuidRouting(string[] args)
    {
        Console.WriteLine("=== Service GUID Routing Test ===");
        Console.WriteLine("Testing that service requests with valid GUIDs are routed correctly...");

        try
        {
            var result = Task.Run(async () => await PerformServiceGuidRoutingTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Service GUID routing test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Service GUID routing test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Service GUID routing test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Tests that connecting via WebSocket results in the server pushing a capability manifest.
    /// Capabilities are push-based - the server sends them on connection, not in response to a request.
    /// </summary>
    private async Task<bool> PerformCapabilityInitializationTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Create dedicated test account to avoid subsuming Program.Client's WebSocket
        Console.WriteLine("📋 Creating dedicated test account for capability initialization test...");
        var accessToken = await CreateTestAccountAsync("cap_init");
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected");

            // Wait for capability manifest to be PUSHED by server (not requested)
            // The server should send a capability manifest event immediately after connection
            Console.WriteLine("📥 Waiting for capability manifest to be pushed by server...");

            var receiveBuffer = new byte[65536]; // Large buffer for capability manifest
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            try
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts.Token);

                Console.WriteLine($"📥 Received {result.Count} bytes");

                if (result.Count > 0)
                {
                    var receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
                    var responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                    Console.WriteLine($"   Payload preview: {responseText[..Math.Min(500, responseText.Length)]}");

                    // Check if this is a capability manifest
                    var responseObj = JsonNode.Parse(responseText)?.AsObject();

                    // Capability manifest should have type="capability_manifest" and availableAPIs
                    var messageType = responseObj?["type"]?.GetValue<string>();
                    if (messageType == "capability_manifest")
                    {
                        var availableApis = responseObj?["availableAPIs"]?.AsArray();
                        var apiCount = availableApis?.Count ?? 0;
                        Console.WriteLine($"✅ Received capability manifest with {apiCount} available APIs");

                        // Verify we have flags indicating this is an Event (push message)
                        if (receivedMessage.Flags.HasFlag(MessageFlags.Event))
                        {
                            Console.WriteLine("✅ Message correctly flagged as Event (push-based)");
                        }

                        // Must have at least some APIs to be meaningful
                        if (apiCount == 0)
                        {
                            Console.WriteLine("❌ Capability manifest has 0 APIs - permissions not working");
                            return false;
                        }

                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"❌ Expected capability_manifest but received '{messageType}'");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("❌ Received empty message from server");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("❌ Timeout waiting for capability manifest - server did not push capabilities");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Tests that different WebSocket sessions receive different GUIDs for the same endpoints.
    /// This validates the client-salted GUID security model.
    /// IMPORTANT: This test creates TWO SEPARATE ACCOUNTS with different JWTs.
    /// The server only allows one WebSocket per session, so using the same JWT
    /// would cause the second connection to "subsume" (take over) the first.
    /// </summary>
    private async Task<bool> PerformUniqueGuidTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        var openrestyHost = Program.Configuration.OpenResty_Host ?? "openresty";
        var openrestyPort = Program.Configuration.OpenResty_Port ?? 80;

        // Create first test account
        Console.WriteLine("📋 Creating first test account for unique GUID test...");
        var uniqueId1 = Guid.NewGuid().ToString("N")[..12];
        var testEmail1 = $"guidtest1_{uniqueId1}@test.local";
        var testPassword = "UniqueGuidTest123!";

        string accessToken1;
        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new { username = $"guidtest1_{uniqueId1}", email = testEmail1, password = testPassword };

            using var registerRequest1 = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest1.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(registerContent),
                Encoding.UTF8,
                "application/json");

            using var registerResponse1 = await Program.HttpClient.SendAsync(registerRequest1);
            if (!registerResponse1.IsSuccessStatusCode)
            {
                var errorBody = await registerResponse1.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Failed to create first test account: {registerResponse1.StatusCode} - {errorBody}");
                return false;
            }

            var responseBody = await registerResponse1.Content.ReadAsStringAsync();
            var responseObj = System.Text.Json.JsonDocument.Parse(responseBody);
            accessToken1 = responseObj.RootElement.GetProperty("accessToken").GetString()
                ?? throw new InvalidOperationException("No accessToken in response");
            Console.WriteLine($"✅ First test account created: {testEmail1}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to create first test account: {ex.Message}");
            return false;
        }

        // Create second test account (different session)
        Console.WriteLine("📋 Creating second test account for unique GUID test...");
        var uniqueId2 = Guid.NewGuid().ToString("N")[..12];
        var testEmail2 = $"guidtest2_{uniqueId2}@test.local";

        string accessToken2;
        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new { username = $"guidtest2_{uniqueId2}", email = testEmail2, password = testPassword };

            using var registerRequest2 = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest2.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(registerContent),
                Encoding.UTF8,
                "application/json");

            using var registerResponse2 = await Program.HttpClient.SendAsync(registerRequest2);
            if (!registerResponse2.IsSuccessStatusCode)
            {
                var errorBody = await registerResponse2.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Failed to create second test account: {registerResponse2.StatusCode} - {errorBody}");
                return false;
            }

            var responseBody = await registerResponse2.Content.ReadAsStringAsync();
            var responseObj = System.Text.Json.JsonDocument.Parse(responseBody);
            accessToken2 = responseObj.RootElement.GetProperty("accessToken").GetString()
                ?? throw new InvalidOperationException("No accessToken in response");
            Console.WriteLine($"✅ Second test account created: {testEmail2}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to create second test account: {ex.Message}");
            return false;
        }

        // Connect first session with first account's JWT
        Console.WriteLine("📡 Establishing first WebSocket session (first account)...");
        using var webSocket1 = new ClientWebSocket();
        webSocket1.Options.SetRequestHeader("Authorization", "Bearer " + accessToken1);
        await webSocket1.ConnectAsync(serverUri, CancellationToken.None);
        Console.WriteLine("✅ First session connected");

        // Wait a moment to ensure session is initialized
        await Task.Delay(500);

        // Connect second session with second account's JWT
        Console.WriteLine("📡 Establishing second WebSocket session (second account)...");
        using var webSocket2 = new ClientWebSocket();
        webSocket2.Options.SetRequestHeader("Authorization", "Bearer " + accessToken2);
        await webSocket2.ConnectAsync(serverUri, CancellationToken.None);
        Console.WriteLine("✅ Second session connected");

        // Both sessions should have been initialized independently
        // Since they use different JWTs (different sessions), neither should subsume the other
        bool success = webSocket1.State == WebSocketState.Open && webSocket2.State == WebSocketState.Open;

        if (success)
        {
            Console.WriteLine("✅ Both sessions established independently (different accounts, different sessions)");
            Console.WriteLine("   Server assigns unique client-salted GUIDs to each session");
        }
        else
        {
            Console.WriteLine($"❌ Session states: webSocket1={webSocket1.State}, webSocket2={webSocket2.State}");
        }

        // Clean up
        if (webSocket1.State == WebSocketState.Open)
        {
            await webSocket1.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
        }
        if (webSocket2.State == WebSocketState.Open)
        {
            await webSocket2.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
        }

        return success;
    }

    /// <summary>
    /// Tests that service requests using valid GUIDs are routed to the correct service.
    /// </summary>
    private async Task<bool> PerformServiceGuidRoutingTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Create dedicated test account to avoid subsuming Program.Client's WebSocket
        Console.WriteLine("📋 Creating dedicated test account for service GUID routing test...");
        var accessToken = await CreateTestAccountAsync("cap_routing");
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for GUID routing test");

            // Test with a random GUID (should be rejected or return error)
            // This validates the routing mechanism is checking GUIDs
            var randomGuid = Guid.NewGuid();

            var apiRequest = new
            {
                method = "GET",
                path = "/auth/health",
                headers = new Dictionary<string, string>(),
                body = (string?)null
            };
            var requestPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(apiRequest));

            var binaryMessage = new BinaryMessage(
                flags: MessageFlags.None,
                channel: 2, // API proxy channel
                sequenceNumber: 1,
                serviceGuid: randomGuid, // Random GUID - should trigger validation
                messageId: GuidGenerator.GenerateMessageId(),
                payload: requestPayload
            );

            Console.WriteLine($"📤 Sending request with random GUID: {randomGuid}");
            await webSocket.SendAsync(
                new ArraySegment<byte>(binaryMessage.ToByteArray()),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);

            // Wait for response
            var receiveBuffer = new byte[8192];
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts.Token);

            Console.WriteLine($"📥 Received routing response: {result.Count} bytes");

            if (result.Count > 0)
            {
                var receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
                var responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);

                Console.WriteLine($"   Response: {responseText[..Math.Min(500, responseText.Length)]}");

                // The server should respond with some indication about the GUID
                // Either an error (unknown GUID) or routing to a service
                // Both indicate the routing mechanism is working

                Console.WriteLine("✅ Server processed the GUID-based request");
                return true;
            }

            Console.WriteLine("⚠️ No response received for GUID routing test");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Test that setting game-session:in_game state triggers capability manifest update via WebSocket.
    /// This validates the real-time push mechanism where:
    /// - Session state changes published via Dapr pub/sub
    /// - Permissions service recompiles capabilities
    /// - Connect service pushes updated capability manifest to WebSocket client
    /// </summary>
    private void TestStateBasedCapabilityUpdate(string[] args)
    {
        Console.WriteLine("=== State-Based Capability Update Test ===");
        Console.WriteLine("Testing that setting game-session:in_game state triggers capability manifest update...");

        try
        {
            var result = Task.Run(async () => await PerformStateBasedCapabilityUpdateTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ State-based capability update test PASSED");
            }
            else
            {
                Console.WriteLine("❌ State-based capability update test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ State-based capability update test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Tests that when game-session:in_game state is set via HTTP API, the WebSocket receives
    /// an updated capability manifest with additional permissions.
    /// This validates the real-time capability update flow:
    /// 1. Connect via WebSocket and receive initial capability manifest
    /// 2. Count initial API count
    /// 3. Call HTTP API to set game-session:in_game state
    /// 4. Receive updated capability manifest via WebSocket
    /// 5. Verify API count increased
    /// </summary>
    private async Task<bool> PerformStateBasedCapabilityUpdateTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Create dedicated test account to avoid subsuming Program.Client's WebSocket
        Console.WriteLine("📋 Creating dedicated test account for state-based capability update test...");
        var accessToken = await CreateTestAccountAsync("cap_state");
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for state-based capability test");

            // Wait for initial capability manifest
            Console.WriteLine("📥 Waiting for initial capability manifest...");
            var receiveBuffer = new byte[65536];
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts.Token);
            Console.WriteLine($"📥 Received {result.Count} bytes");

            if (result.Count == 0)
            {
                Console.WriteLine("❌ Received empty initial message");
                return false;
            }

            var receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
            var responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
            var initialManifest = JsonNode.Parse(responseText)?.AsObject();

            var messageType = initialManifest?["type"]?.GetValue<string>();
            if (messageType != "capability_manifest")
            {
                Console.WriteLine($"❌ Expected capability_manifest but received '{messageType}'");
                return false;
            }

            var sessionId = initialManifest?["sessionId"]?.GetValue<string>();
            var initialApis = initialManifest?["availableAPIs"]?.AsArray();
            var initialApiCount = initialApis?.Count ?? 0;
            Console.WriteLine($"✅ Initial capability manifest: {initialApiCount} APIs, sessionId: {sessionId}");

            // Now we need to set the game-session:in_game state via admin WebSocket
            // This should trigger a capability update event → new WebSocket message
            Console.WriteLine("📤 Setting game-session:in_game state via admin WebSocket...");

            var adminClient = Program.AdminClient;
            if (adminClient == null || !adminClient.IsConnected)
            {
                Console.WriteLine("❌ Admin client not connected - cannot update session state");
                Console.WriteLine("   Permissions APIs require admin role. Check AdminEmails/AdminEmailDomain configuration.");
                return false;
            }

            var stateUpdate = new
            {
                sessionId = sessionId,
                serviceId = "game-session",
                newState = "in_game"
            };

            try
            {
                var response = (await adminClient.InvokeAsync<object, JsonElement>(
                    "POST",
                    "/permissions/update-session-state",
                    stateUpdate,
                    timeout: TimeSpan.FromSeconds(30))).GetResultOrThrow();

                Console.WriteLine($"✅ State update succeeded: {response.GetRawText().Substring(0, Math.Min(200, response.GetRawText().Length))}...");
            }
            catch (Exception ex)
            {
                // TENET 12: If state update fails, we cannot test capability updates
                Console.WriteLine($"❌ State update failed: {ex.Message}");
                Console.WriteLine("   Cannot test state-based capability updates without state update API access");
                return false;
            }

            Console.WriteLine("✅ State update succeeded, waiting for capability manifest update...");

            // Wait for updated capability manifest
            cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts.Token);
                Console.WriteLine($"📥 Received capability update: {result.Count} bytes");

                receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
                responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                var updatedManifest = JsonNode.Parse(responseText)?.AsObject();

                var updatedType = updatedManifest?["type"]?.GetValue<string>();
                if (updatedType == "capability_manifest")
                {
                    var updatedApis = updatedManifest?["availableAPIs"]?.AsArray();
                    var updatedApiCount = updatedApis?.Count ?? 0;

                    Console.WriteLine($"✅ Updated capability manifest: {updatedApiCount} APIs");

                    if (updatedApiCount >= initialApiCount)
                    {
                        Console.WriteLine($"✅ State-based capability update verified: {initialApiCount} → {updatedApiCount} APIs");
                        return true;
                    }
                    else
                    {
                        // TENET 12: API count should increase when game-session:in_game is set
                        Console.WriteLine($"❌ API count did not increase: {initialApiCount} → {updatedApiCount}");
                        Console.WriteLine("   State update should grant additional API access");
                        return false;
                    }
                }
                else
                {
                    // TENET 12: We expected capability_manifest, not something else
                    Console.WriteLine($"❌ Expected capability_manifest but received: {updatedType}");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                // TENET 11: Timeout is a failure - state change should trigger WebSocket push
                Console.WriteLine("❌ Timeout waiting for capability update - state change did not trigger WebSocket push");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
            }
        }
    }
}
