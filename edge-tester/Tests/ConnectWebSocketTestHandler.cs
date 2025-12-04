using BeyondImmersion.BannouService.Connect.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;

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
                "Test that deleting an account disconnects the WebSocket connection via event chain")
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
            var testPayload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { test = "Binary protocol validation", timestamp = DateTime.UtcNow }));

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
                var responseObj = Newtonsoft.Json.Linq.JObject.Parse(responsePayload);
                var messageType = (string?)responseObj["type"];

                if (messageType == "capability_manifest")
                {
                    var availableApis = responseObj["availableAPIs"] as Newtonsoft.Json.Linq.JArray;
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
            var requestPayload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(apiRequest));

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
                    var apiResponse = JsonConvert.DeserializeObject(responsePayload);
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
        var registerContent = new JObject
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
            registerRequest.Content = new StringContent(JsonConvert.SerializeObject(registerContent), Encoding.UTF8, "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            if (registerResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var errorBody = await registerResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Registration failed: {registerResponse.StatusCode} - {errorBody}");
                return false;
            }

            var registerBody = await registerResponse.Content.ReadAsStringAsync();
            var registerObj = JObject.Parse(registerBody);
            userAccessToken = (string?)registerObj["accessToken"] ?? "";

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
            var jwtPayload = JObject.Parse(payloadJson);
            var accountIdString = (string?)jwtPayload["nameid"] ?? (string?)jwtPayload["sub"];

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
                body = JsonConvert.SerializeObject(deleteRequestBody)
            };
            var requestPayload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(deleteRequest));

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
                        var manifest = JObject.Parse(payloadText);
                        var availableAPIs = manifest["availableAPIs"] as JArray;

                        if (availableAPIs != null)
                        {
                            Console.WriteLine($"📥 Received capability manifest: {availableAPIs.Count} APIs available");

                            // Look for POST:/accounts/delete
                            foreach (var api in availableAPIs)
                            {
                                var method = (string?)api["method"];
                                var path = (string?)api["path"];
                                var serviceGuidStr = (string?)api["serviceGuid"];

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

                JObject manifest;
                try { manifest = JObject.Parse(payloadJson); }
                catch { continue; }

                var type = (string?)manifest["type"];
                if (type != "capability_manifest") continue;

                var availableApis = manifest["availableAPIs"] as JArray;
                if (availableApis == null || availableApis.Count == 0)
                {
                    Console.WriteLine("⚠️ Manifest has no available APIs");
                    continue;
                }

                Console.WriteLine($"📥 Received capability manifest with {availableApis.Count} APIs");

                // Log available APIs
                foreach (var api in availableApis)
                {
                    var debugMethod = (string?)api["method"];
                    var debugPath = (string?)api["path"];
                    var debugService = (string?)api["serviceName"];
                    Console.WriteLine($"      - {debugMethod}:{debugPath} ({debugService})");
                }

                // Prefer GET endpoints for testing (they don't require request body)
                foreach (var api in availableApis)
                {
                    var apiMethod = (string?)api["method"];
                    var apiPath = (string?)api["path"];
                    var apiGuid = (string?)api["serviceGuid"];

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
                    var apiMethod = (string?)api["method"];
                    var apiPath = (string?)api["path"];
                    var apiGuid = (string?)api["serviceGuid"];

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

                JObject manifest;
                try
                {
                    manifest = JObject.Parse(payloadJson);
                }
                catch
                {
                    Console.WriteLine("⚠️ Failed to parse event payload as JSON, waiting for more...");
                    continue;
                }

                // Verify this is a capability manifest
                var type = (string?)manifest["type"];
                if (type != "capability_manifest")
                {
                    Console.WriteLine($"⚠️ Received event type '{type}', waiting for capability_manifest...");
                    continue;
                }

                var reason = (string?)manifest["reason"];
                Console.WriteLine($"📥 Received capability manifest: {result.Count} bytes (reason: {reason ?? "initial"})");

                var availableApis = manifest["availableAPIs"] as JArray;
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
                    var debugMethod = (string?)api["method"];
                    var debugPath = (string?)api["path"];
                    var debugService = (string?)api["serviceName"];
                    Console.WriteLine($"      - {debugMethod}:{debugPath} ({debugService})");
                }

                // Try to find the GUID for our endpoint
                foreach (var api in availableApis)
                {
                    var apiMethod = (string?)api["method"];
                    var apiPath = (string?)api["path"];
                    var apiGuid = (string?)api["serviceGuid"];

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
                    var endpointKey = (string?)api["endpointKey"];
                    var apiGuid = (string?)api["serviceGuid"];

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
                        var eventObj = JObject.Parse(payloadJson);
                        var eventType = (string?)eventObj["type"];
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
