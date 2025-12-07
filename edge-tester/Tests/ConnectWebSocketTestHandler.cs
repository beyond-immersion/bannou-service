using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

public class ConnectWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestWebSocketUpgrade, "WebSocket - Upgrade", "WebSocket",
                "Test WebSocket connection upgrade with JWT authentication"),
            new ServiceTest(TestBinaryProtocolEcho, "WebSocket - Binary Protocol", "WebSocket",
                "Test binary protocol message sending and receiving"),
            new ServiceTest(TestCapabilityManifestReceived, "WebSocket - Capability Manifest", "WebSocket",
                "Test that capability manifest is pushed on WebSocket connection"),
            new ServiceTest(TestInternalAPIProxy, "WebSocket - Internal API Proxy", "WebSocket",
                "Test proxying internal API calls through WebSocket binary protocol"),
            new ServiceTest(TestAccountDeletionDisconnectsWebSocket, "WebSocket - Account Deletion Disconnect", "WebSocket",
                "Test that deleting an account disconnects the WebSocket connection via event chain"),
            new ServiceTest(TestReconnectionTokenFlow, "WebSocket - Reconnection Token", "WebSocket",
                "Test that graceful disconnect provides reconnection token and reconnection works"),
            new ServiceTest(TestSessionSubsumeBehavior, "WebSocket - Session Subsume", "WebSocket",
                "Test that second WebSocket with same JWT subsumes (takes over) the first connection")
        };
    }

    private void TestWebSocketUpgrade(string[] args)
    {
        Console.WriteLine("=== WebSocket Upgrade Test ===");
        Console.WriteLine("Testing WebSocket connection upgrade with JWT authentication...");

        try
        {
            var result = Task.Run(async () => await PerformWebSocketUpgradeTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ WebSocket upgrade test PASSED");
            }
            else
            {
                Console.WriteLine("❌ WebSocket upgrade test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ WebSocket upgrade test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private void TestBinaryProtocolEcho(string[] args)
    {
        Console.WriteLine("=== Binary Protocol Echo Test ===");
        Console.WriteLine("Testing binary protocol message sending and receiving...");

        try
        {
            var result = Task.Run(async () => await PerformBinaryProtocolTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Binary protocol echo test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Binary protocol echo test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Binary protocol echo test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private void TestCapabilityManifestReceived(string[] args)
    {
        Console.WriteLine("=== Capability Manifest Test ===");
        Console.WriteLine("Testing that capability manifest is pushed on WebSocket connection...");

        try
        {
            var result = Task.Run(async () => await PerformCapabilityManifestTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Capability manifest test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Capability manifest test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Capability manifest test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private void TestInternalAPIProxy(string[] args)
    {
        Console.WriteLine("=== Internal API Proxy Test ===");
        Console.WriteLine("Testing internal API proxying through WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () => await PerformInternalAPIProxyTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Internal API proxy test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Internal API proxy test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Internal API proxy test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private async Task<bool> PerformWebSocketUpgradeTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Get access token from BannouClient
        var accessToken = Program.Client?.AccessToken;

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Access token not available - ensure BannouClient login completed successfully");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        Console.WriteLine($"📡 Connecting to WebSocket: {serverUri}");

        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connection established successfully");

            // Verify connection state
            if (webSocket.State == WebSocketState.Open)
            {
                Console.WriteLine("✅ WebSocket is in Open state");
                return true;
            }
            else
            {
                Console.WriteLine($"❌ WebSocket in unexpected state: {webSocket.State}");
                return false;
            }
        }
        catch (WebSocketException wse)
        {
            Console.WriteLine($"❌ WebSocket connection failed: {wse.Message}");
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

    private async Task<bool> PerformBinaryProtocolTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var accessToken = Program.Client?.AccessToken;

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Access token not available - ensure BannouClient login completed successfully");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for binary protocol test");

            // Create a test message using binary protocol
            var testPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { test = "Binary protocol validation", timestamp = DateTime.UtcNow }));

            var binaryMessage = new BinaryMessage(
                flags: MessageFlags.None,
                channel: 1,
                sequenceNumber: 1,
                serviceGuid: Guid.NewGuid(), // Client-salted GUID
                messageId: GuidGenerator.GenerateMessageId(),
                payload: testPayload
            );

            Console.WriteLine($"📤 Sending binary message:");
            Console.WriteLine($"   Flags: {binaryMessage.Flags}");
            Console.WriteLine($"   Channel: {binaryMessage.Channel}");
            Console.WriteLine($"   Sequence: {binaryMessage.SequenceNumber}");
            Console.WriteLine($"   Service GUID: {binaryMessage.ServiceGuid}");
            Console.WriteLine($"   Message ID: {binaryMessage.MessageId}");
            Console.WriteLine($"   Payload size: {testPayload.Length} bytes");

            // Send the binary message
            var messageBytes = binaryMessage.ToByteArray();
            var buffer = new ArraySegment<byte>(messageBytes);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);
            Console.WriteLine("✅ Binary message sent successfully");

            // Receive response
            var receiveBuffer = new ArraySegment<byte>(new byte[4096]);
            var result = await webSocket.ReceiveAsync(receiveBuffer, CancellationToken.None);

            Console.WriteLine($"📥 Received {result.Count} bytes");

            if (receiveBuffer.Array == null)
            {
                Console.WriteLine("❌ Received buffer is null");
                return false;
            }

            // Try to parse the response
            try
            {
                var receivedMessage = BinaryMessage.Parse(receiveBuffer.Array, result.Count);
                Console.WriteLine("✅ Successfully parsed binary protocol response:");
                Console.WriteLine($"   Flags: {receivedMessage.Flags}");
                Console.WriteLine($"   Channel: {receivedMessage.Channel}");
                Console.WriteLine($"   Sequence: {receivedMessage.SequenceNumber}");
                Console.WriteLine($"   Service GUID: {receivedMessage.ServiceGuid}");
                Console.WriteLine($"   Message ID: {receivedMessage.MessageId}");

                if (receivedMessage.Payload.Length > 0)
                {
                    var responsePayload = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                    Console.WriteLine($"   Payload: {responsePayload}");
                }

                return true;
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"⚠️ Could not parse as binary protocol: {parseEx.Message}");
                Console.WriteLine($"   Raw received data: {Convert.ToHexString(receiveBuffer.Array, 0, result.Count)}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Binary protocol test failed: {ex.Message}");
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
    /// Tests that the server pushes a capability manifest when a WebSocket connection is established.
    /// Capabilities are push-based - the server sends them immediately after connection, not in response to a request.
    /// </summary>
    private async Task<bool> PerformCapabilityManifestTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var accessToken = Program.Client?.AccessToken;

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Access token not available - ensure BannouClient login completed successfully");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for capability manifest test");

            // Wait for capability manifest to be PUSHED by server (not requested)
            Console.WriteLine("📥 Waiting for capability manifest to be pushed by server...");

            var receiveBuffer = new ArraySegment<byte>(new byte[65536]);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            try
            {
                var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                Console.WriteLine($"📥 Received {result.Count} bytes");

                if (receiveBuffer.Array == null || result.Count == 0)
                {
                    Console.WriteLine("❌ No data received from server");
                    return false;
                }

                var receivedMessage = BinaryMessage.Parse(receiveBuffer.Array, result.Count);
                var responsePayload = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                Console.WriteLine($"   Payload preview: {responsePayload[..Math.Min(500, responsePayload.Length)]}");

                // Parse and validate the capability manifest
                var responseObj = JsonNode.Parse(responsePayload)?.AsObject();
                var messageType = responseObj?["type"]?.GetValue<string>();

                if (messageType == "capability_manifest")
                {
                    var availableApis = responseObj?["availableAPIs"]?.AsArray();
                    var apiCount = availableApis?.Count ?? 0;
                    Console.WriteLine($"✅ Received capability manifest with {apiCount} available APIs");

                    // Verify this is flagged as an Event (push message)
                    if (receivedMessage.Flags.HasFlag(MessageFlags.Event))
                    {
                        Console.WriteLine("✅ Message correctly flagged as Event (push-based)");
                    }

                    // Must have APIs to be meaningful
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
            catch (OperationCanceledException)
            {
                Console.WriteLine("❌ Timeout waiting for capability manifest - server did not push capabilities");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Capability manifest test failed: {ex.Message}");
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

    private async Task<bool> PerformInternalAPIProxyTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var accessToken = Program.Client?.AccessToken;

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Access token not available - ensure BannouClient login completed successfully");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for internal API proxy test");

            // First, receive the capability manifest and find ANY valid service GUID
            // The actual endpoint doesn't matter - we just want to test the binary protocol routing
            Console.WriteLine("📥 Waiting for capability manifest...");
            var (serviceGuid, apiMethod, apiPath) = await ReceiveCapabilityManifestAndFindAnyServiceGuid(webSocket);

            if (serviceGuid == Guid.Empty)
            {
                Console.WriteLine("❌ No APIs available in capability manifest");
                return false;
            }

            Console.WriteLine($"✅ Found service GUID for {apiMethod}:{apiPath}: {serviceGuid}");

            // Create an internal API request using binary protocol
            var apiRequest = new
            {
                method = apiMethod,
                path = apiPath,
                headers = new Dictionary<string, string>(),
                body = (string?)null
            };
            var requestPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(apiRequest));

            var binaryMessage = new BinaryMessage(
                flags: MessageFlags.None,
                channel: 2, // API proxy channel
                sequenceNumber: 1,
                serviceGuid: serviceGuid, // Use the GUID from capability manifest
                messageId: GuidGenerator.GenerateMessageId(),
                payload: requestPayload
            );

            Console.WriteLine($"📤 Sending internal API proxy request:");
            Console.WriteLine($"   Method: {apiMethod}");
            Console.WriteLine($"   Path: {apiPath}");
            Console.WriteLine($"   ServiceGuid: {serviceGuid}");

            // Send the API proxy request
            var messageBytes = binaryMessage.ToByteArray();
            var buffer = new ArraySegment<byte>(messageBytes);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Wait for Response message, skipping any Event messages
            var responseResult = await WaitForResponseMessage(webSocket, TimeSpan.FromSeconds(15));

            if (responseResult == null)
            {
                Console.WriteLine("❌ Timeout waiting for API response");
                return false;
            }

            var response = responseResult.Value;
            Console.WriteLine($"📥 Received API proxy response: {response.Payload.Length} bytes");

            try
            {
                if (response.Payload.Length > 0)
                {
                    var responsePayload = Encoding.UTF8.GetString(response.Payload.Span);
                    Console.WriteLine($"✅ API proxy response: {responsePayload.Substring(0, Math.Min(500, responsePayload.Length))}");

                    // Try to parse as JSON to verify structure
                    var apiResponse = JsonNode.Parse(responsePayload);
                    if (apiResponse != null)
                    {
                        Console.WriteLine("✅ API proxy response is valid JSON");
                        return true;
                    }
                }

                Console.WriteLine("⚠️ API proxy response has no payload");
                return false;
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"❌ Failed to parse API proxy response: {parseEx.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Internal API proxy test failed: {ex.Message}");
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

    private void TestAccountDeletionDisconnectsWebSocket(string[] args)
    {
        Console.WriteLine("=== Account Deletion → WebSocket Disconnect Test ===");
        Console.WriteLine("Testing that account deletion triggers session invalidation event which disconnects WebSocket...");

        try
        {
            var result = Task.Run(async () => await PerformAccountDeletionDisconnectTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Account deletion disconnect test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Account deletion disconnect test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Account deletion disconnect test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Tests the complete event chain:
    /// 1. Account deleted → AccountsService publishes account.deleted
    /// 2. AuthService receives account.deleted → invalidates all sessions → publishes session.invalidated
    /// 3. ConnectService receives session.invalidated → disconnects WebSocket connection
    /// </summary>
    private async Task<bool> PerformAccountDeletionDisconnectTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Verify admin client is available (needed for account deletion via WebSocket)
        if (Program.AdminAccessToken == null)
        {
            Console.WriteLine("❌ Admin access token not available - required for account deletion");
            return false;
        }

        // Step 1: Create a new account via /auth/register (HTTP - only exposed auth endpoint)
        Console.WriteLine("📋 Step 1: Creating test account via registration...");
        var uniqueId = Guid.NewGuid().ToString("N")[..16];
        var testUsername = $"wsdeltest_{uniqueId}";
        var testEmail = $"{testUsername}@test.local";
        var testPassword = "WebSocketDeleteTest123!";

        var registerUrl = $"http://{Program.Configuration.OpenResty_Host}:{Program.Configuration.OpenResty_Port}/auth/register";
        var registerContent = new JsonObject
        {
            ["username"] = testUsername,
            ["email"] = testEmail,
            ["password"] = testPassword
        };

        string userAccessToken;
        Guid accountId;

        try
        {
            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(JsonSerializer.Serialize(registerContent), Encoding.UTF8, "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            if (registerResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var errorBody = await registerResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Registration failed: {registerResponse.StatusCode} - {errorBody}");
                return false;
            }

            var registerBody = await registerResponse.Content.ReadAsStringAsync();
            var registerObj = JsonNode.Parse(registerBody)?.AsObject();
            userAccessToken = registerObj?["accessToken"]?.GetValue<string>() ?? "";

            if (string.IsNullOrEmpty(userAccessToken))
            {
                Console.WriteLine("❌ Registration response missing accessToken");
                return false;
            }

            // Extract account ID from JWT claims (nameid or sub)
            var jwtParts = userAccessToken.Split('.');
            if (jwtParts.Length < 2)
            {
                Console.WriteLine("❌ Invalid JWT format");
                return false;
            }

            var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(jwtParts[1])));
            var jwtPayload = JsonNode.Parse(payloadJson)?.AsObject();
            var accountIdString = jwtPayload?["nameid"]?.GetValue<string>() ?? jwtPayload?["sub"]?.GetValue<string>();

            if (string.IsNullOrEmpty(accountIdString) || !Guid.TryParse(accountIdString, out accountId))
            {
                Console.WriteLine($"❌ Could not extract account ID from JWT: {payloadJson}");
                return false;
            }

            Console.WriteLine($"✅ Account created via registration: ID={accountId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to create test account: {ex.Message}");
            return false;
        }

        // Step 2: Establish user's WebSocket connection (this is the one we expect to be disconnected)
        Console.WriteLine("📋 Step 2: Establishing user's WebSocket connection...");
        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var userWebSocket = new ClientWebSocket();
        userWebSocket.Options.SetRequestHeader("Authorization", "Bearer " + userAccessToken);

        try
        {
            await userWebSocket.ConnectAsync(serverUri, CancellationToken.None);
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"❌ User WebSocket connection failed: {ex.Message}");
            return false;
        }

        if (userWebSocket.State != WebSocketState.Open)
        {
            Console.WriteLine($"❌ User WebSocket in unexpected state: {userWebSocket.State}");
            return false;
        }

        Console.WriteLine("✅ User WebSocket connection established");

        // Wait for capability manifest (server pushes it on connection)
        Console.WriteLine("📋 Step 2b: Waiting for user capability manifest...");
        var capabilityBuffer = new ArraySegment<byte>(new byte[65536]);
        using var capabilityCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            var capResult = await userWebSocket.ReceiveAsync(capabilityBuffer, capabilityCts.Token);
            if (capResult.Count > 0)
            {
                Console.WriteLine($"✅ Received user capability manifest ({capResult.Count} bytes)");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("⚠️ Timeout waiting for capability manifest (continuing anyway)");
        }

        // Step 3: Connect admin WebSocket and delete account via binary protocol
        // (accounts endpoints are only accessible via WebSocket, not HTTP)
        Console.WriteLine("📋 Step 3: Connecting admin WebSocket for account deletion...");
        using var adminWebSocket = new ClientWebSocket();
        adminWebSocket.Options.SetRequestHeader("Authorization", "Bearer " + Program.AdminAccessToken);

        try
        {
            await adminWebSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ Admin WebSocket connected");

            // Wait for admin capability manifest and find POST:/accounts/delete GUID
            Console.WriteLine("📋 Step 3b: Waiting for admin capability manifest...");
            var adminServiceGuid = await ReceiveCapabilityManifestAndFindAccountDeleteGuid(adminWebSocket);

            if (adminServiceGuid == Guid.Empty)
            {
                Console.WriteLine("❌ Could not find POST:/accounts/delete in admin capabilities");
                Console.WriteLine("   Admin may not have permission to delete accounts");
                await CloseWebSocketSafely(userWebSocket);
                await CloseWebSocketSafely(adminWebSocket);
                return false;
            }

            Console.WriteLine($"✅ Found POST:/accounts/delete service GUID: {adminServiceGuid}");

            // Step 4: Delete the account via WebSocket binary protocol
            Console.WriteLine($"📋 Step 4: Deleting account {accountId} via WebSocket...");

            var deleteRequestBody = new { accountId = accountId.ToString() };
            var deleteRequest = new
            {
                method = "POST",
                path = "/accounts/delete",
                headers = new Dictionary<string, string>(),
                body = JsonSerializer.Serialize(deleteRequestBody)
            };
            var requestPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(deleteRequest));

            var binaryMessage = new BinaryMessage(
                flags: MessageFlags.None,
                channel: 2, // API proxy channel
                sequenceNumber: 1,
                serviceGuid: adminServiceGuid,
                messageId: GuidGenerator.GenerateMessageId(),
                payload: requestPayload
            );

            var messageBytes = binaryMessage.ToByteArray();
            await adminWebSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Binary, true, CancellationToken.None);
            Console.WriteLine("✅ Account deletion request sent via WebSocket");

            // Wait briefly for deletion to process (we don't need to parse the response)
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to delete account via WebSocket: {ex.Message}");
            await CloseWebSocketSafely(userWebSocket);
            return false;
        }
        finally
        {
            await CloseWebSocketSafely(adminWebSocket);
        }

        // Step 5: Wait for user's WebSocket to be closed by the server
        Console.WriteLine("📋 Step 5: Waiting for server to close user's WebSocket connection...");
        var receiveBuffer = new ArraySegment<byte>(new byte[4096]);
        using var receiveCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            while (userWebSocket.State == WebSocketState.Open)
            {
                var result = await userWebSocket.ReceiveAsync(receiveBuffer, receiveCts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"✅ Server initiated close: Status={result.CloseStatus}, Description=\"{result.CloseStatusDescription}\"");

                    // Complete the close handshake
                    if (userWebSocket.State == WebSocketState.CloseReceived)
                    {
                        await userWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", CancellationToken.None);
                    }

                    // Verify the close reason indicates session invalidation
                    if (result.CloseStatusDescription?.Contains("invalidated") == true ||
                        result.CloseStatusDescription?.Contains("Session") == true ||
                        result.CloseStatus == WebSocketCloseStatus.NormalClosure)
                    {
                        Console.WriteLine("✅ WebSocket closed due to session invalidation (event chain working!)");
                        return true;
                    }

                    Console.WriteLine($"✅ WebSocket closed by server (close reason: {result.CloseStatusDescription})");
                    return true;
                }

                // Log any other messages received
                Console.WriteLine($"📥 Received {result.Count} bytes while waiting for close");
            }

            // If we exited the loop, check final state
            Console.WriteLine($"🔍 WebSocket final state: {userWebSocket.State}");
            if (userWebSocket.State == WebSocketState.Closed || userWebSocket.State == WebSocketState.Aborted)
            {
                Console.WriteLine("✅ WebSocket connection was closed by server");
                return true;
            }

            Console.WriteLine("❌ WebSocket remained open after account deletion");
            return false;
        }
        catch (OperationCanceledException)
        {
            // Check if the WebSocket was actually closed/aborted - that's a success!
            if (userWebSocket.State == WebSocketState.Aborted || userWebSocket.State == WebSocketState.Closed)
            {
                Console.WriteLine($"✅ WebSocket connection was terminated during wait (state: {userWebSocket.State})");
                return true;
            }

            Console.WriteLine($"❌ Timeout waiting for WebSocket close (state: {userWebSocket.State})");

            // Try to close the WebSocket cleanly
            await CloseWebSocketSafely(userWebSocket);

            return false;
        }
        catch (WebSocketException ex)
        {
            // Connection aborted/closed is actually what we expect!
            if (userWebSocket.State == WebSocketState.Aborted || userWebSocket.State == WebSocketState.Closed)
            {
                Console.WriteLine($"✅ WebSocket connection was terminated (expected): {ex.Message}");
                return true;
            }

            Console.WriteLine($"❌ WebSocket error: {ex.Message}");
            return false;
        }
    }

    private void TestReconnectionTokenFlow(string[] args)
    {
        Console.WriteLine("=== Reconnection Token Flow Test ===");
        Console.WriteLine("Testing graceful disconnect with reconnection token and session restoration...");

        try
        {
            var result = Task.Run(async () => await PerformReconnectionTokenTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Reconnection token flow test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Reconnection token flow test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Reconnection token flow test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Tests the complete reconnection flow:
    /// 1. Connect with JWT authentication
    /// 2. Receive capability manifest
    /// 3. Initiate graceful close (client-side)
    /// 4. Receive disconnect_notification with reconnection token
    /// 5. Reconnect using the reconnection token
    /// 6. Verify session is restored (receive capability manifest again)
    /// </summary>
    private async Task<bool> PerformReconnectionTokenTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var accessToken = Program.Client?.AccessToken;

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Access token not available - ensure BannouClient login completed successfully");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        string? reconnectionToken = null;

        // Step 1: Initial connection
        Console.WriteLine("📋 Step 1: Establishing initial WebSocket connection...");
        using (var webSocket = new ClientWebSocket())
        {
            webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

            try
            {
                await webSocket.ConnectAsync(serverUri, CancellationToken.None);
                Console.WriteLine("✅ Initial WebSocket connection established");
            }
            catch (WebSocketException wse)
            {
                Console.WriteLine($"❌ Initial WebSocket connection failed: {wse.Message}");
                return false;
            }

            // Step 2: Receive capability manifest to confirm connection is working
            Console.WriteLine("📋 Step 2: Waiting for capability manifest...");
            var (serviceGuid, method, path) = await ReceiveCapabilityManifestAndFindAnyServiceGuid(webSocket);
            if (serviceGuid == Guid.Empty)
            {
                Console.WriteLine("❌ Failed to receive capability manifest on initial connection");
                return false;
            }
            Console.WriteLine($"✅ Received capability manifest with at least {method}:{path}");

            // Step 3: Initiate graceful close - server should send disconnect_notification first
            Console.WriteLine("📋 Step 3: Initiating graceful close...");
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Testing reconnection", CancellationToken.None);

            // The server should have sent us a disconnect_notification before closing
            // We need to receive any pending messages before the close completed
            // Note: In practice, the server sends the notification before accepting our close
            Console.WriteLine("   WebSocket gracefully closed");

            // Actually, we need to receive messages BEFORE closing
            // Let me restructure this to properly capture the disconnect notification
        }

        // Alternative approach: Don't close from client side, let server handle the close
        // and capture the disconnect_notification
        Console.WriteLine("📋 Step 3 (revised): Connecting again and receiving disconnect notification...");
        using (var webSocket = new ClientWebSocket())
        {
            webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);

            // Receive capability manifest first
            var receiveBuffer = new ArraySegment<byte>(new byte[65536]);
            var manifestReceived = false;
            var timeout = TimeSpan.FromSeconds(10);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout && !manifestReceived)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
                {
                    var payloadText = result.MessageType == WebSocketMessageType.Text
                        ? Encoding.UTF8.GetString(receiveBuffer.Array!, 0, result.Count)
                        : TryParseAsJsonFromBinary(receiveBuffer.Array!, result.Count);

                    if (payloadText?.Contains("capability_manifest") == true)
                    {
                        manifestReceived = true;
                        Console.WriteLine("✅ Received capability manifest");
                    }
                }
            }

            if (!manifestReceived)
            {
                Console.WriteLine("❌ Did not receive capability manifest");
                return false;
            }

            // Now initiate close and try to capture disconnect_notification
            // The server should send it before completing the close
            Console.WriteLine("📋 Step 4: Closing connection to trigger disconnect notification...");

            // Send close frame
            if (webSocket.State == WebSocketState.Open)
            {
                // Start receiving to capture any messages before close completes
                var disconnectNotificationReceived = false;
                var closeReceived = false;

                try
                {
                    // Request close
                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Testing reconnection", CancellationToken.None);

                    // Now receive any final messages (should include disconnect_notification)
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    while (!closeReceived && webSocket.State != WebSocketState.Closed)
                    {
                        var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            closeReceived = true;
                            Console.WriteLine($"   Received close frame: {result.CloseStatusDescription}");
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var text = Encoding.UTF8.GetString(receiveBuffer.Array!, 0, result.Count);
                            Console.WriteLine($"   Received text message: {text[..Math.Min(100, text.Length)]}...");

                            if (text.Contains("disconnect_notification"))
                            {
                                disconnectNotificationReceived = true;
                                var notification = JsonNode.Parse(text)?.AsObject();
                                reconnectionToken = notification?["reconnectionToken"]?.GetValue<string>();
                                var expiresAt = notification?["expiresAt"]?.GetValue<string>();
                                Console.WriteLine($"✅ Received disconnect_notification!");
                                Console.WriteLine($"   Reconnection token: {reconnectionToken?[..Math.Min(20, reconnectionToken?.Length ?? 0)]}...");
                                Console.WriteLine($"   Expires at: {expiresAt}");
                            }
                        }
                    }
                }
                catch (WebSocketException wse)
                {
                    Console.WriteLine($"   WebSocket closed: {wse.Message}");
                }

                if (!disconnectNotificationReceived)
                {
                    Console.WriteLine("⚠️ Did not receive disconnect_notification before WebSocket closed");
                    Console.WriteLine("   Check that DaprSessionManager is properly registered and state store is available");
                }
            }
        }

        // Step 5: Attempt reconnection with the token
        Console.WriteLine("📋 Step 5: Attempting reconnection with token...");

        if (string.IsNullOrEmpty(reconnectionToken))
        {
            Console.WriteLine("❌ FAIL: No reconnection token received - disconnect_notification was not sent");
            Console.WriteLine("   This indicates session management is not working (check DaprSessionManager registration)");
            return false;
        }

        // Test reconnection with valid token
        using (var webSocket = new ClientWebSocket())
        {
            webSocket.Options.SetRequestHeader("Authorization", $"Reconnect {reconnectionToken}");

            try
            {
                await webSocket.ConnectAsync(serverUri, CancellationToken.None);
                Console.WriteLine("✅ Reconnection successful!");
            }
            catch (WebSocketException wse)
            {
                Console.WriteLine($"❌ Reconnection failed: {wse.Message}");
                return false;
            }

            // Step 6: Verify session restoration by receiving capability manifest
            Console.WriteLine("📋 Step 6: Verifying session restoration...");
            var (restoredGuid, restoredMethod, restoredPath) = await ReceiveCapabilityManifestAndFindAnyServiceGuid(webSocket);

            if (restoredGuid == Guid.Empty)
            {
                Console.WriteLine("❌ Failed to receive capability manifest after reconnection");
                return false;
            }

            Console.WriteLine($"✅ Session restored! Capability manifest received with {restoredMethod}:{restoredPath}");

            await CloseWebSocketSafely(webSocket);
        }

        Console.WriteLine("✅ Full reconnection flow completed successfully!");
        return true;
    }

    private void TestSessionSubsumeBehavior(string[] args)
    {
        Console.WriteLine("=== Session Subsume Behavior Test ===");
        Console.WriteLine("Testing that second WebSocket with same JWT subsumes the first connection...");

        try
        {
            var result = Task.Run(async () => await PerformSessionSubsumeTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Session subsume behavior test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Session subsume behavior test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Session subsume behavior test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Tests the WebSocket session subsume behavior:
    /// The server only allows one WebSocket per session. When a second WebSocket connects
    /// with the same JWT (same session_key), the server closes the first connection with
    /// the message "New connection established" and the second connection takes over.
    ///
    /// This is the expected behavior for session management and is critical for:
    /// - Reconnection scenarios where a client reconnects with the same JWT
    /// - Preventing stale connections from consuming server resources
    /// - Ensuring single-WebSocket-per-session invariant
    ///
    /// IMPORTANT: This test creates its own account to avoid interfering with other tests.
    /// </summary>
    private async Task<bool> PerformSessionSubsumeTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var openrestyHost = Program.Configuration.OpenResty_Host ?? "openresty";
        var openrestyPort = Program.Configuration.OpenResty_Port ?? 80;

        // Step 1: Create a dedicated test account to avoid interfering with other tests
        Console.WriteLine("📋 Step 1: Creating dedicated test account for subsume test...");
        var uniqueId = Guid.NewGuid().ToString("N")[..12];
        var testEmail = $"subsume_{uniqueId}@test.local";
        var testPassword = "SubsumeTest123!";

        string accessToken;
        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new { username = $"subsume_{uniqueId}", email = testEmail, password = testPassword };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(
                JsonSerializer.Serialize(registerContent),
                Encoding.UTF8,
                "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            if (!registerResponse.IsSuccessStatusCode)
            {
                var errorBody = await registerResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Failed to create test account: {registerResponse.StatusCode} - {errorBody}");
                return false;
            }

            var responseBody = await registerResponse.Content.ReadAsStringAsync();
            var responseObj = System.Text.Json.JsonDocument.Parse(responseBody);
            accessToken = responseObj.RootElement.GetProperty("accessToken").GetString()
                ?? throw new InvalidOperationException("No accessToken in response");
            Console.WriteLine($"✅ Test account created: {testEmail}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to create test account: {ex.Message}");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");

        // Step 2: Establish first WebSocket connection
        Console.WriteLine("📋 Step 2: Establishing first WebSocket connection...");
        using var webSocket1 = new ClientWebSocket();
        webSocket1.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket1.ConnectAsync(serverUri, CancellationToken.None);
            if (webSocket1.State != WebSocketState.Open)
            {
                Console.WriteLine($"❌ First WebSocket failed to connect: {webSocket1.State}");
                return false;
            }
            Console.WriteLine("✅ First WebSocket connected");
        }
        catch (WebSocketException wse)
        {
            Console.WriteLine($"❌ First WebSocket connection failed: {wse.Message}");
            return false;
        }

        // Wait for capability manifest on first connection
        Console.WriteLine("📋 Step 2b: Waiting for capability manifest on first connection...");
        var receiveBuffer = new ArraySegment<byte>(new byte[65536]);
        using var capabilityCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            var capResult = await webSocket1.ReceiveAsync(receiveBuffer, capabilityCts.Token);
            if (capResult.Count > 0)
            {
                Console.WriteLine($"✅ First connection received capability manifest ({capResult.Count} bytes)");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("⚠️ Timeout waiting for capability manifest on first connection");
        }

        // Step 3: Establish second WebSocket connection with the SAME JWT
        Console.WriteLine("📋 Step 3: Establishing second WebSocket with SAME JWT (should subsume first)...");
        using var webSocket2 = new ClientWebSocket();
        webSocket2.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket2.ConnectAsync(serverUri, CancellationToken.None);
            if (webSocket2.State != WebSocketState.Open)
            {
                Console.WriteLine($"❌ Second WebSocket failed to connect: {webSocket2.State}");
                return false;
            }
            Console.WriteLine("✅ Second WebSocket connected");
        }
        catch (WebSocketException wse)
        {
            Console.WriteLine($"❌ Second WebSocket connection failed: {wse.Message}");
            return false;
        }

        // Step 4: Verify the first WebSocket gets closed by the server
        Console.WriteLine("📋 Step 4: Verifying first WebSocket is closed by server (subsume behavior)...");

        // The server closes the first connection asynchronously when the second connects
        // We need to wait for the close message or detect the connection is no longer open
        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            // Try to receive on the first WebSocket - should get a close message
            var closeResult = await webSocket1.ReceiveAsync(receiveBuffer, closeCts.Token);

            if (closeResult.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine($"✅ First WebSocket received close from server:");
                Console.WriteLine($"   Close Status: {closeResult.CloseStatus}");
                Console.WriteLine($"   Close Description: \"{closeResult.CloseStatusDescription}\"");

                // Verify the close description indicates subsume
                if (closeResult.CloseStatusDescription?.Contains("New connection") == true)
                {
                    Console.WriteLine("✅ Server correctly indicated 'New connection established' as close reason");
                }

                // Complete the close handshake
                if (webSocket1.State == WebSocketState.CloseReceived)
                {
                    await webSocket1.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", CancellationToken.None);
                }
            }
            else
            {
                Console.WriteLine($"📥 First WebSocket received data instead of close ({closeResult.Count} bytes)");
                Console.WriteLine("   Checking WebSocket state...");
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout - check the WebSocket state directly
            Console.WriteLine($"⏳ Timeout waiting for close, checking WebSocket1 state: {webSocket1.State}");
        }
        catch (WebSocketException wse)
        {
            // Connection error is expected if the server already closed
            Console.WriteLine($"✅ First WebSocket connection terminated: {wse.Message}");
        }

        // Verify final states
        var ws1Closed = webSocket1.State == WebSocketState.Closed ||
                        webSocket1.State == WebSocketState.Aborted ||
                        webSocket1.State == WebSocketState.CloseReceived;
        var ws2Open = webSocket2.State == WebSocketState.Open;

        Console.WriteLine($"📋 Final states: WebSocket1={webSocket1.State}, WebSocket2={webSocket2.State}");

        // Step 5: Verify second WebSocket is still functional
        Console.WriteLine("📋 Step 5: Verifying second WebSocket is still functional...");

        try
        {
            // Receive capability manifest on second connection
            using var ws2CapCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ws2Result = await webSocket2.ReceiveAsync(receiveBuffer, ws2CapCts.Token);

            if (ws2Result.Count > 0)
            {
                Console.WriteLine($"✅ Second WebSocket received data ({ws2Result.Count} bytes) - still functional");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("⚠️ Timeout on second WebSocket - may not have received capability manifest yet");
            // This isn't necessarily a failure - the second connection is still open
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error on second WebSocket: {ex.Message}");
            ws2Open = false;
        }

        // Clean up
        await CloseWebSocketSafely(webSocket1);
        await CloseWebSocketSafely(webSocket2);

        // Test passes if:
        // 1. First WebSocket was closed (subsumed)
        // 2. Second WebSocket remained open (took over the session)
        if (ws1Closed && ws2Open)
        {
            Console.WriteLine("✅ Subsume behavior verified: Second WebSocket took over session, first was closed");
            return true;
        }
        else
        {
            Console.WriteLine($"❌ Unexpected behavior: ws1Closed={ws1Closed}, ws2Open={ws2Open}");
            return false;
        }
    }

    /// <summary>
    /// Tries to extract JSON text from a binary message payload.
    /// </summary>
    private static string? TryParseAsJsonFromBinary(byte[] buffer, int count)
    {
        try
        {
            var message = BinaryMessage.Parse(buffer, count);
            return Encoding.UTF8.GetString(message.Payload.Span);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Safely closes a WebSocket connection.
    /// </summary>
    private static async Task CloseWebSocketSafely(ClientWebSocket webSocket)
    {
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test cleanup", CancellationToken.None);
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }

    /// <summary>
    /// Receives capability manifest and finds the POST:/accounts/delete service GUID.
    /// Waits for capability updates if the endpoint isn't immediately available.
    /// </summary>
    private static async Task<Guid> ReceiveCapabilityManifestAndFindAccountDeleteGuid(ClientWebSocket webSocket)
    {
        var overallTimeout = TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;
        var receiveBuffer = new ArraySegment<byte>(new byte[65536]);

        while (DateTime.UtcNow - startTime < overallTimeout)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                if (result.Count > 0 && receiveBuffer.Array != null)
                {
                    var receivedMessage = BinaryMessage.Parse(receiveBuffer.Array, result.Count);
                    var payloadText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);

                    // Check if this is a capability manifest
                    if (payloadText.Contains("capability_manifest"))
                    {
                        var manifest = JsonNode.Parse(payloadText)?.AsObject();
                        var availableAPIs = manifest?["availableAPIs"]?.AsArray();

                        if (availableAPIs != null)
                        {
                            Console.WriteLine($"📥 Received capability manifest: {availableAPIs.Count} APIs available");

                            // Look for POST:/accounts/delete
                            foreach (var api in availableAPIs)
                            {
                                var method = api?["method"]?.GetValue<string>();
                                var path = api?["path"]?.GetValue<string>();
                                var serviceGuidStr = api?["serviceGuid"]?.GetValue<string>();

                                // Match POST /accounts/delete
                                if (method == "POST" && path == "/accounts/delete")
                                {
                                    if (Guid.TryParse(serviceGuidStr, out var guid))
                                    {
                                        Console.WriteLine($"   Found: {method}:{path} -> {guid}");
                                        return guid;
                                    }
                                }
                            }

                            Console.WriteLine("   POST:/accounts/delete not found in manifest, waiting for updates...");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⏳ Waiting for capability update...");
            }
        }

        return Guid.Empty;
    }

    /// <summary>
    /// Pads a base64 string to the correct length for decoding.
    /// JWT base64url encoding omits padding characters.
    /// </summary>
    private static string PadBase64(string base64)
    {
        // Replace URL-safe characters with standard base64
        var result = base64.Replace('-', '+').Replace('_', '/');

        // Add padding if needed
        switch (result.Length % 4)
        {
            case 2: result += "=="; break;
            case 3: result += "="; break;
        }

        return result;
    }

    /// <summary>
    /// Receives the capability manifest from the server and extracts ANY available service GUID.
    /// Returns the first GET endpoint found (preferred for testing) or any other endpoint.
    /// </summary>
    private static async Task<(Guid serviceGuid, string method, string path)> ReceiveCapabilityManifestAndFindAnyServiceGuid(
        ClientWebSocket webSocket)
    {
        var overallTimeout = TimeSpan.FromSeconds(15);
        var startTime = DateTime.UtcNow;
        var receiveBuffer = new ArraySegment<byte>(new byte[65536]);

        while (DateTime.UtcNow - startTime < overallTimeout)
        {
            try
            {
                var remainingTime = overallTimeout - (DateTime.UtcNow - startTime);
                if (remainingTime <= TimeSpan.Zero) break;

                using var cts = new CancellationTokenSource(remainingTime);
                var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                if (receiveBuffer.Array == null || result.Count == 0) continue;

                var receivedMessage = BinaryMessage.Parse(receiveBuffer.Array, result.Count);
                if (!receivedMessage.Flags.HasFlag(MessageFlags.Event)) continue;
                if (receivedMessage.Payload.Length == 0) continue;

                var payloadJson = Encoding.UTF8.GetString(receivedMessage.Payload.Span);

                JsonObject? manifest;
                try { manifest = JsonNode.Parse(payloadJson)?.AsObject(); }
                catch { continue; }

                var type = manifest?["type"]?.GetValue<string>();
                if (type != "capability_manifest") continue;

                var availableApis = manifest?["availableAPIs"]?.AsArray();
                if (availableApis == null || availableApis.Count == 0)
                {
                    Console.WriteLine("⚠️ Manifest has no available APIs");
                    continue;
                }

                Console.WriteLine($"📥 Received capability manifest with {availableApis.Count} APIs");

                // Log available APIs
                foreach (var api in availableApis)
                {
                    var debugMethod = api?["method"]?.GetValue<string>();
                    var debugPath = api?["path"]?.GetValue<string>();
                    var debugService = api?["serviceName"]?.GetValue<string>();
                    Console.WriteLine($"      - {debugMethod}:{debugPath} ({debugService})");
                }

                // Prefer GET endpoints for testing (they don't require request body)
                foreach (var api in availableApis)
                {
                    var apiMethod = api?["method"]?.GetValue<string>();
                    var apiPath = api?["path"]?.GetValue<string>();
                    var apiGuid = api?["serviceGuid"]?.GetValue<string>();

                    if (apiMethod == "GET" && !string.IsNullOrEmpty(apiPath) && !string.IsNullOrEmpty(apiGuid))
                    {
                        if (Guid.TryParse(apiGuid, out var guid))
                        {
                            return (guid, apiMethod, apiPath);
                        }
                    }
                }

                // Fallback: return any endpoint
                foreach (var api in availableApis)
                {
                    var apiMethod = api?["method"]?.GetValue<string>();
                    var apiPath = api?["path"]?.GetValue<string>();
                    var apiGuid = api?["serviceGuid"]?.GetValue<string>();

                    if (!string.IsNullOrEmpty(apiMethod) && !string.IsNullOrEmpty(apiPath) && !string.IsNullOrEmpty(apiGuid))
                    {
                        if (Guid.TryParse(apiGuid, out var guid))
                        {
                            return (guid, apiMethod, apiPath);
                        }
                    }
                }

                Console.WriteLine("⚠️ No usable API found in manifest");
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return (Guid.Empty, "", "");
    }

    /// <summary>
    /// Receives the capability manifest from the server and extracts the service GUID for the requested endpoint.
    /// The server pushes the capability manifest immediately after WebSocket connection is established.
    /// </summary>
    private static async Task<Guid> ReceiveCapabilityManifestAndFindServiceGuid(
        ClientWebSocket webSocket,
        string method,
        string path)
    {
        var overallTimeout = TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;
        var receiveBuffer = new ArraySegment<byte>(new byte[65536]);

        while (DateTime.UtcNow - startTime < overallTimeout)
        {
            try
            {
                var remainingTime = overallTimeout - (DateTime.UtcNow - startTime);
                if (remainingTime <= TimeSpan.Zero) break;

                using var cts = new CancellationTokenSource(remainingTime);
                var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                if (receiveBuffer.Array == null || result.Count == 0)
                {
                    Console.WriteLine("⚠️ Received empty message, waiting for more...");
                    continue;
                }

                // Parse the binary message
                var receivedMessage = BinaryMessage.Parse(receiveBuffer.Array, result.Count);

                // Check if this is an event message (capability manifest)
                if (!receivedMessage.Flags.HasFlag(MessageFlags.Event))
                {
                    Console.WriteLine($"⚠️ Received non-Event message (flags: {receivedMessage.Flags}), waiting for capability manifest...");
                    continue;
                }

                if (receivedMessage.Payload.Length == 0)
                {
                    Console.WriteLine("⚠️ Event message has no payload, waiting for more...");
                    continue;
                }

                var payloadJson = Encoding.UTF8.GetString(receivedMessage.Payload.Span);

                JsonObject? manifest;
                try
                {
                    manifest = JsonNode.Parse(payloadJson)?.AsObject();
                }
                catch
                {
                    Console.WriteLine("⚠️ Failed to parse event payload as JSON, waiting for more...");
                    continue;
                }

                // Verify this is a capability manifest
                var type = manifest?["type"]?.GetValue<string>();
                if (type != "capability_manifest")
                {
                    Console.WriteLine($"⚠️ Received event type '{type}', waiting for capability_manifest...");
                    continue;
                }

                var reason = manifest?["reason"]?.GetValue<string>();
                Console.WriteLine($"📥 Received capability manifest: {result.Count} bytes (reason: {reason ?? "initial"})");

                var availableApis = manifest?["availableAPIs"]?.AsArray();
                if (availableApis == null)
                {
                    Console.WriteLine("⚠️ No availableAPIs in manifest, waiting for update...");
                    continue;
                }

                Console.WriteLine($"   Available APIs: {availableApis.Count}");

                // Log all available APIs for debugging
                Console.WriteLine($"   Currently available endpoints:");
                foreach (var api in availableApis)
                {
                    var debugMethod = api?["method"]?.GetValue<string>();
                    var debugPath = api?["path"]?.GetValue<string>();
                    var debugService = api?["serviceName"]?.GetValue<string>();
                    Console.WriteLine($"      - {debugMethod}:{debugPath} ({debugService})");
                }

                // Try to find the GUID for our endpoint
                foreach (var api in availableApis)
                {
                    var apiMethod = api?["method"]?.GetValue<string>();
                    var apiPath = api?["path"]?.GetValue<string>();
                    var apiGuid = api?["serviceGuid"]?.GetValue<string>();

                    if (apiMethod == method && apiPath == path && !string.IsNullOrEmpty(apiGuid))
                    {
                        if (Guid.TryParse(apiGuid, out var guid))
                        {
                            Console.WriteLine($"   ✅ Found API by exact match: {method}:{path}");
                            return guid;
                        }
                    }
                }

                // Try by endpoint key format
                foreach (var api in availableApis)
                {
                    var endpointKey = api?["endpointKey"]?.GetValue<string>();
                    var apiGuid = api?["serviceGuid"]?.GetValue<string>();

                    // The endpoint key format is "serviceName:METHOD:/path"
                    if (!string.IsNullOrEmpty(endpointKey) && endpointKey.Contains($":{method}:{path}"))
                    {
                        if (Guid.TryParse(apiGuid, out var guid))
                        {
                            Console.WriteLine($"   ✅ Found API by endpointKey: {endpointKey}");
                            return guid;
                        }
                    }
                }

                // API not found yet - if this is the initial manifest, wait for updates
                Console.WriteLine($"⚠️ API {method}:{path} not found yet, waiting for capability updates...");
            }
            catch (OperationCanceledException)
            {
                // Timeout - check if we should continue waiting
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error receiving message: {ex.Message}, retrying...");
            }
        }

        Console.WriteLine($"❌ Timeout waiting for API {method}:{path} to become available (waited {overallTimeout.TotalSeconds}s)");
        return Guid.Empty;
    }

    /// <summary>
    /// Waits for a Response message from the WebSocket, skipping any Event messages.
    /// Event messages (like capability_manifest updates) can arrive asynchronously,
    /// so we need to filter for actual API responses.
    /// </summary>
    private static async Task<BinaryMessage?> WaitForResponseMessage(ClientWebSocket webSocket, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        var receiveBuffer = new ArraySegment<byte>(new byte[65536]);

        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var remainingTime = timeout - (DateTime.UtcNow - startTime);
                if (remainingTime <= TimeSpan.Zero) break;

                using var cts = new CancellationTokenSource(remainingTime);
                var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("⚠️ WebSocket closed while waiting for response");
                    return null;
                }

                if (receiveBuffer.Array == null || result.Count == 0)
                {
                    Console.WriteLine("⚠️ Received empty message, continuing to wait...");
                    continue;
                }

                // Parse the binary message
                var message = BinaryMessage.Parse(receiveBuffer.Array, result.Count);

                Console.WriteLine($"   Received message: flags={message.Flags}, channel={message.Channel}, msgId={message.MessageId}");

                // Check if this is a Response message (not an Event)
                if (message.Flags.HasFlag(MessageFlags.Response))
                {
                    Console.WriteLine($"   ✅ Found Response message");
                    return message;
                }

                // If it's an Event message, skip it and continue waiting
                if (message.Flags.HasFlag(MessageFlags.Event))
                {
                    // Log what type of event we're skipping
                    try
                    {
                        var payloadJson = Encoding.UTF8.GetString(message.Payload.Span);
                        var eventObj = JsonNode.Parse(payloadJson)?.AsObject();
                        var eventType = eventObj?["type"]?.GetValue<string>();
                        Console.WriteLine($"   ⏭️ Skipping Event message (type: {eventType ?? "unknown"})");
                    }
                    catch
                    {
                        Console.WriteLine($"   ⏭️ Skipping Event message (could not parse type)");
                    }
                    continue;
                }

                // Message is neither Response nor Event - could be an error or unexpected format
                Console.WriteLine($"   ⚠️ Received non-Response, non-Event message (flags: {message.Flags})");

                // For backwards compatibility, return messages that look like they might be responses
                // (no Event flag and has payload)
                if (message.Payload.Length > 0)
                {
                    Console.WriteLine($"   Treating as response due to payload presence");
                    return message;
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout - check if we should continue
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Error receiving message: {ex.Message}, retrying...");
            }
        }

        Console.WriteLine($"❌ Timeout waiting for Response message (waited {timeout.TotalSeconds}s)");
        return null;
    }
}
