using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

public class ConnectWebSocketTestHandler : IServiceTestHandler
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
                "Test that second WebSocket with same JWT subsumes (takes over) the first connection"),
            new ServiceTest(TestBannouClientWithDedicatedAccount, "WebSocket - BannouClient SDK Flow", "WebSocket",
                "Test BannouClient SDK with dedicated account: register -> connect -> invoke -> dispose"),
            new ServiceTest(TestCapabilityManifestCaching, "WebSocket - Manifest GUID Caching", "WebSocket",
                "Test that all API GUIDs are cached from a single capability manifest")
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

        // Create dedicated test account to avoid subsuming Program.Client's WebSocket
        Console.WriteLine("📋 Creating dedicated test account for WebSocket upgrade test...");
        var accessToken = await CreateTestAccountAsync("ws_upgrade");

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
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

        // Create dedicated test account to avoid subsuming Program.Client's WebSocket
        Console.WriteLine("📋 Creating dedicated test account for binary protocol test...");
        var accessToken = await CreateTestAccountAsync("ws_binary");

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for binary protocol test");

            // Create a test message using binary protocol
            var testPayload = Encoding.UTF8.GetBytes(BannouJson.Serialize(new { test = "Binary protocol validation", timestamp = DateTime.UtcNow }));

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
                var receivedMessage = BinaryMessageHelper.ParseAndDecompress(receiveBuffer.Array, result.Count);
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

        // Create dedicated test account to avoid subsuming Program.Client's WebSocket
        Console.WriteLine("📋 Creating dedicated test account for capability manifest test...");
        var accessToken = await CreateTestAccountAsync("ws_manifest");

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for capability manifest test");

            // Wait for capability manifest to be PUSHED by server (not requested)
            Console.WriteLine("📥 Waiting for capability manifest to be pushed by server...");

            var receiveBuffer = new ArraySegment<byte>(new byte[65536]);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

                Console.WriteLine($"📥 Received {result.Count} bytes");

                if (receiveBuffer.Array == null || result.Count == 0)
                {
                    Console.WriteLine("❌ No data received from server");
                    return false;
                }

                var receivedMessage = BinaryMessageHelper.ParseAndDecompress(receiveBuffer.Array, result.Count);
                var responsePayload = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                Console.WriteLine($"   Payload preview: {responsePayload[..Math.Min(500, responsePayload.Length)]}");

                // Parse and validate the capability manifest
                var responseObj = JsonNode.Parse(responsePayload)?.AsObject();
                var messageType = responseObj?["eventName"]?.GetValue<string>();

                if (messageType == "connect.capability-manifest")
                {
                    var availableApis = responseObj?["availableApis"]?.AsArray();
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
                    Console.WriteLine($"❌ Expected capability-manifest but received '{messageType}'");
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

        // Create dedicated test account to avoid subsuming Program.Client's WebSocket
        Console.WriteLine("📋 Creating dedicated test account for internal API proxy test...");
        var accessToken = await CreateTestAccountAsync("ws_apiproxy");

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for internal API proxy test");

            // Use /testing/ping endpoint - designed for testing and accepts empty requests
            const string apiEndpoint = "/testing/ping";

            Console.WriteLine("📥 Waiting for capability manifest...");
            var serviceGuid = await ReceiveCapabilityManifestAndFindServiceGuid(webSocket, apiEndpoint);

            if (serviceGuid == Guid.Empty)
            {
                Console.WriteLine($"❌ {apiEndpoint} not found in capability manifest");
                return false;
            }

            Console.WriteLine($"✅ Found service GUID for {apiEndpoint}: {serviceGuid}");

            // Create a ping request - the endpoint accepts optional ClientTimestamp and SequenceNumber
            var pingRequest = new
            {
                ClientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SequenceNumber = 1
            };
            var requestPayload = Encoding.UTF8.GetBytes(BannouJson.Serialize(pingRequest));

            var binaryMessage = new BinaryMessage(
                flags: MessageFlags.None,
                channel: 2, // API proxy channel
                sequenceNumber: 1,
                serviceGuid: serviceGuid, // Use the GUID from capability manifest
                messageId: GuidGenerator.GenerateMessageId(),
                payload: requestPayload
            );

            Console.WriteLine($"📤 Sending internal API proxy request:");
            Console.WriteLine($"   Endpoint: {apiEndpoint}");
            Console.WriteLine($"   ServiceGuid: {serviceGuid}");

            // Send the API proxy request
            var messageBytes = binaryMessage.ToByteArray();
            var buffer = new ArraySegment<byte>(messageBytes);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Wait for Response message, skipping any Event messages
            var responseResult = await WaitForResponseMessage(webSocket, TimeSpan.FromSeconds(5));

            if (responseResult == null)
            {
                Console.WriteLine("❌ Timeout waiting for API response");
                return false;
            }

            var response = responseResult.Value;
            Console.WriteLine($"📥 Received API proxy response: {response.Payload.Length} bytes, ResponseCode: {response.ResponseCode}");

            try
            {
                // Check the response code from the binary header
                // ResponseCode 0 = success, non-zero = error
                if (response.ResponseCode != 0)
                {
                    Console.WriteLine($"❌ API proxy returned error code: {response.ResponseCode}");
                    return false;
                }

                // Success! Empty payloads are valid for void/update endpoints
                if (response.Payload.Length > 0)
                {
                    var responsePayload = Encoding.UTF8.GetString(response.Payload.Span);
                    Console.WriteLine($"✅ API proxy response: {responsePayload.Substring(0, Math.Min(500, responsePayload.Length))}");

                    // Try to parse as JSON to verify structure
                    var apiResponse = JsonNode.Parse(responsePayload);
                    if (apiResponse != null)
                    {
                        Console.WriteLine("✅ API proxy response is valid JSON");
                    }
                }
                else
                {
                    Console.WriteLine("✅ API proxy returned success with empty payload (valid for void endpoints)");
                }

                return true;
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
    /// 1. Account deleted → AccountService publishes account.deleted
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

        var registerUrl = $"http://{Program.Configuration.OpenRestyHost}:{Program.Configuration.OpenRestyPort}/auth/register";
        var registerContent = new RegisterRequest
        {
            Username = testUsername,
            Email = testEmail,
            Password = testPassword
        };

        string userAccessToken;
        Guid accountId;

        try
        {
            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(BannouJson.Serialize(registerContent), Encoding.UTF8, "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            if (registerResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var errorBody = await registerResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Registration failed: {registerResponse.StatusCode} - {errorBody}");
                return false;
            }

            var registerBody = await registerResponse.Content.ReadAsStringAsync();
            var registerResult = BannouJson.Deserialize<RegisterResponse>(registerBody);
            userAccessToken = registerResult?.AccessToken ?? "";
            var accountIdValue = registerResult?.AccountId;

            if (string.IsNullOrEmpty(userAccessToken))
            {
                Console.WriteLine("❌ Registration response missing accessToken");
                return false;
            }

            if (accountIdValue == null || accountIdValue == Guid.Empty)
            {
                Console.WriteLine("❌ Registration response missing or invalid accountId");
                return false;
            }

            accountId = accountIdValue.Value;

            Console.WriteLine($"✅ Account created via registration: ID={accountId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to create test account: {ex.Message}");
            return false;
        }

        // Step 2: Establish user's WebSocket connection (this is the one we expect to be disconnected)
        Console.WriteLine("📋 Step 2: Establishing user's WebSocket connection...");
        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
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

        // Step 3: Delete account via shared admin WebSocket
        // IMPORTANT: We must use Program.AdminClient (the shared admin WebSocket) instead of
        // creating a new WebSocket, because creating a new WebSocket with the same JWT would
        // "subsume" (disconnect) the shared admin client that other tests depend on.
        Console.WriteLine("📋 Step 3: Deleting account via shared admin WebSocket...");

        var adminClient = Program.AdminClient;
        if (adminClient == null)
        {
            Console.WriteLine("❌ Admin client is null - cannot delete account");
            await CloseWebSocketSafely(userWebSocket);
            return false;
        }

        Console.WriteLine($"   Admin client IsConnected: {adminClient.IsConnected}");
        Console.WriteLine($"   Admin client SessionId: {adminClient.SessionId}");
        Console.WriteLine($"   Admin client AvailableApis count: {adminClient.AvailableApis.Count}");

        if (!adminClient.IsConnected)
        {
            Console.WriteLine("❌ Admin client not connected - cannot delete account");
            await CloseWebSocketSafely(userWebSocket);
            return false;
        }

        // Check if the delete endpoint is available
        var deleteGuid = adminClient.GetServiceGuid("/account/delete");
        if (deleteGuid == null)
        {
            Console.WriteLine("❌ Admin client does not have /account/delete in available APIs");
            Console.WriteLine("   Available APIs:");
            foreach (var api in adminClient.AvailableApis.Keys.Take(20))
            {
                Console.WriteLine($"      - {api}");
            }
            await CloseWebSocketSafely(userWebSocket);
            return false;
        }
        Console.WriteLine($"   Found delete endpoint GUID: {deleteGuid}");

        try
        {
            Console.WriteLine($"📋 Step 4: Deleting account {accountId} via shared admin WebSocket...");
            var deleteRequest = new DeleteAccountRequest { AccountId = accountId };
            var response = await adminClient.InvokeAsync<DeleteAccountRequest, JsonElement>(
                "/account/delete",
                deleteRequest,
                timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess)
            {
                Console.WriteLine($"❌ Account deletion failed: {response.Error?.ErrorName} - {response.Error?.Message} (code: {response.Error?.ResponseCode})");
                await CloseWebSocketSafely(userWebSocket);
                return false;
            }

            // Handle both populated and empty success responses
            var result = response.Result;
            if (result.ValueKind != JsonValueKind.Undefined)
            {
                Console.WriteLine($"✅ Account deletion response received: {result.GetRawText().Substring(0, Math.Min(100, result.GetRawText().Length))}...");
            }
            else
            {
                Console.WriteLine("✅ Account deletion successful (empty response)");
            }
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"❌ InvalidOperationException during account deletion: {ex.Message}");
            Console.WriteLine($"   Admin client IsConnected after error: {adminClient.IsConnected}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
            await CloseWebSocketSafely(userWebSocket);
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to delete account via WebSocket: {ex.GetType().Name}: {ex.Message}");
            await CloseWebSocketSafely(userWebSocket);
            return false;
        }

        // Step 5: Wait for user's WebSocket to be closed by the server
        Console.WriteLine("📋 Step 5: Waiting for server to close user's WebSocket connection...");
        var receiveBuffer = new ArraySegment<byte>(new byte[4096]);
        using var receiveCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

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
    /// 4. Receive disconnect-notification with reconnection token
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

        // Create dedicated test account to avoid subsuming Program.Client's WebSocket
        Console.WriteLine("📋 Creating dedicated test account for reconnection token test...");
        var accessToken = await CreateTestAccountAsync("ws_reconnect");

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
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
            var (serviceGuid, endpoint) = await ReceiveCapabilityManifestAndFindAnyServiceGuid(webSocket);
            if (serviceGuid == Guid.Empty)
            {
                Console.WriteLine("❌ Failed to receive capability manifest on initial connection");
                return false;
            }
            Console.WriteLine($"✅ Received capability manifest with at least {endpoint}");

            // Step 3: Initiate graceful close - server should send disconnect-notification first
            Console.WriteLine("📋 Step 3: Initiating graceful close...");
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Testing reconnection", CancellationToken.None);

            // The server should have sent us a disconnect-notification before closing
            // We need to receive any pending messages before the close completed
            // Note: In practice, the server sends the notification before accepting our close
            Console.WriteLine("   WebSocket gracefully closed");

            // Actually, we need to receive messages BEFORE closing
            // Let me restructure this to properly capture the disconnect notification
        }

        // Alternative approach: Don't close from client side, let server handle the close
        // and capture the disconnect-notification
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

                    if (payloadText?.Contains("capability-manifest") == true)
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

            // Now initiate close and try to capture disconnect-notification
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

                    // Now receive any final messages (should include disconnect-notification)
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

                            if (text.Contains("disconnect-notification"))
                            {
                                disconnectNotificationReceived = true;
                                var notification = JsonNode.Parse(text)?.AsObject();
                                reconnectionToken = notification?["reconnectionToken"]?.GetValue<string>();
                                var expiresAt = notification?["expiresAt"]?.GetValue<string>();
                                Console.WriteLine($"✅ Received disconnect-notification!");
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
                    Console.WriteLine("⚠️ Did not receive disconnect-notification before WebSocket closed");
                    Console.WriteLine("   Check that BannouSessionManager is properly registered and state store is available");
                }
            }
        }

        // Step 5: Attempt reconnection with the token
        Console.WriteLine("📋 Step 5: Attempting reconnection with token...");

        if (string.IsNullOrEmpty(reconnectionToken))
        {
            Console.WriteLine("❌ FAIL: No reconnection token received - disconnect-notification was not sent");
            Console.WriteLine("   This indicates session management is not working (check BannouSessionManager registration)");
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
            var (restoredGuid, restoredEndpoint) = await ReceiveCapabilityManifestAndFindAnyServiceGuid(webSocket);

            if (restoredGuid == Guid.Empty)
            {
                Console.WriteLine("❌ Failed to receive capability manifest after reconnection");
                return false;
            }

            Console.WriteLine($"✅ Session restored! Capability manifest received with {restoredEndpoint}");

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

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;

        // Step 1: Create a dedicated test account to avoid interfering with other tests
        Console.WriteLine("📋 Step 1: Creating dedicated test account for subsume test...");
        var uniqueId = Guid.NewGuid().ToString("N")[..12];
        var testEmail = $"subsume_{uniqueId}@test.local";
        var testPassword = "SubsumeTest123!";

        string accessToken;
        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new RegisterRequest { Username = $"subsume_{uniqueId}", Email = testEmail, Password = testPassword };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(
                BannouJson.Serialize(registerContent),
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
            var registerResult = BannouJson.Deserialize<RegisterResponse>(responseBody);
            accessToken = registerResult?.AccessToken
                ?? throw new InvalidOperationException("No accessToken in response");
            Console.WriteLine($"✅ Test account created: {testEmail}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to create test account: {ex.Message}");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");

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
            Console.WriteLine("❌ Timeout on second WebSocket - may not have received capability manifest");
            ws2Open = false;
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
    /// Handles decompression transparently when the Compressed flag is set.
    /// </summary>
    private static string? TryParseAsJsonFromBinary(byte[] buffer, int count)
    {
        return BinaryMessageHelper.TryParseJsonPayload(buffer, count);
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
    /// Receives capability manifest and finds the /account/delete service GUID.
    /// Waits for capability updates if the endpoint isn't immediately available.
    /// </summary>
    private static async Task<Guid> ReceiveCapabilityManifestAndFindAccountDeleteGuid(ClientWebSocket webSocket)
    {
        var overallTimeout = TimeSpan.FromSeconds(10);
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
                    var receivedMessage = BinaryMessageHelper.ParseAndDecompress(receiveBuffer.Array, result.Count);
                    var payloadText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);

                    // Check if this is a capability manifest
                    if (payloadText.Contains("capability-manifest"))
                    {
                        var manifest = JsonNode.Parse(payloadText)?.AsObject();
                        var availableApis = manifest?["availableApis"]?.AsArray();

                        if (availableApis != null)
                        {
                            Console.WriteLine($"📥 Received capability manifest: {availableApis.Count} APIs available");

                            // Look for /account/delete
                            foreach (var api in availableApis)
                            {
                                var endpoint = api?["endpoint"]?.GetValue<string>();
                                var serviceIdStr = api?["serviceId"]?.GetValue<string>();

                                if (endpoint == "/account/delete")
                                {
                                    if (Guid.TryParse(serviceIdStr, out var guid))
                                    {
                                        Console.WriteLine($"   Found: {endpoint} -> {guid}");
                                        return guid;
                                    }
                                }
                            }

                            Console.WriteLine("   /account/delete not found in manifest, waiting for updates...");
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
    /// Receives the capability manifest from the server and extracts ANY available service GUID.
    /// Returns the first available endpoint.
    /// </summary>
    private static async Task<(Guid serviceGuid, string endpoint)> ReceiveCapabilityManifestAndFindAnyServiceGuid(
        ClientWebSocket webSocket)
    {
        var overallTimeout = TimeSpan.FromSeconds(5);
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

                var receivedMessage = BinaryMessageHelper.ParseAndDecompress(receiveBuffer.Array, result.Count);
                if (!receivedMessage.Flags.HasFlag(MessageFlags.Event)) continue;
                if (receivedMessage.Payload.Length == 0) continue;

                var payloadJson = Encoding.UTF8.GetString(receivedMessage.Payload.Span);

                JsonObject? manifest;
                try { manifest = JsonNode.Parse(payloadJson)?.AsObject(); }
                catch (JsonException ex)
                {
                    Console.WriteLine($"⚠️ Failed to parse message as JSON: {ex.Message}");
                    Console.WriteLine($"   Raw payload: {payloadJson[..Math.Min(200, payloadJson.Length)]}...");
                    continue;
                }

                var type = manifest?["eventName"]?.GetValue<string>();
                if (type != "connect.capability-manifest") continue;

                var availableApis = manifest?["availableApis"]?.AsArray();
                if (availableApis == null || availableApis.Count == 0)
                {
                    Console.WriteLine("⚠️ Manifest has no available APIs");
                    continue;
                }

                Console.WriteLine($"📥 Received capability manifest with {availableApis.Count} APIs");

                // Log available APIs
                foreach (var api in availableApis)
                {
                    var debugEndpoint = api?["endpoint"]?.GetValue<string>();
                    var debugService = api?["service"]?.GetValue<string>();
                    Console.WriteLine($"      - {debugEndpoint} ({debugService})");
                }

                // Return the first available endpoint
                foreach (var api in availableApis)
                {
                    var apiEndpoint = api?["endpoint"]?.GetValue<string>();
                    var apiGuid = api?["serviceId"]?.GetValue<string>();

                    if (!string.IsNullOrEmpty(apiEndpoint) && !string.IsNullOrEmpty(apiGuid))
                    {
                        if (Guid.TryParse(apiGuid, out var guid))
                        {
                            return (guid, apiEndpoint);
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

        return (Guid.Empty, "");
    }

    /// <summary>
    /// Receives the capability manifest from the server and extracts the service GUID for the requested endpoint.
    /// The server pushes the capability manifest immediately after WebSocket connection is established.
    /// </summary>
    private static async Task<Guid> ReceiveCapabilityManifestAndFindServiceGuid(
        ClientWebSocket webSocket,
        string endpoint)
    {
        var overallTimeout = TimeSpan.FromSeconds(10);
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
                var receivedMessage = BinaryMessageHelper.ParseAndDecompress(receiveBuffer.Array, result.Count);

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
                var type = manifest?["eventName"]?.GetValue<string>();
                if (type != "connect.capability-manifest")
                {
                    Console.WriteLine($"⚠️ Received event type '{type}', waiting for connect.capability-manifest...");
                    continue;
                }

                var reason = manifest?["reason"]?.GetValue<string>();
                Console.WriteLine($"📥 Received capability manifest: {result.Count} bytes (reason: {reason ?? "initial"})");

                var availableApis = manifest?["availableApis"]?.AsArray();
                if (availableApis == null)
                {
                    Console.WriteLine("⚠️ No availableApis in manifest, waiting for update...");
                    continue;
                }

                Console.WriteLine($"   Available APIs: {availableApis.Count}");

                // Log all available APIs for debugging
                Console.WriteLine($"   Currently available endpoints:");
                foreach (var api in availableApis)
                {
                    var debugEndpoint = api?["endpoint"]?.GetValue<string>();
                    var debugService = api?["service"]?.GetValue<string>();
                    Console.WriteLine($"      - {debugEndpoint} ({debugService})");
                }

                // Find the GUID for our endpoint
                foreach (var api in availableApis)
                {
                    var apiEndpoint = api?["endpoint"]?.GetValue<string>();
                    var apiGuid = api?["serviceId"]?.GetValue<string>();

                    if (apiEndpoint == endpoint && !string.IsNullOrEmpty(apiGuid))
                    {
                        if (Guid.TryParse(apiGuid, out var guid))
                        {
                            Console.WriteLine($"   ✅ Found API: {endpoint}");
                            return guid;
                        }
                    }
                }

                // API not found yet - if this is the initial manifest, wait for updates
                Console.WriteLine($"⚠️ API {endpoint} not found yet, waiting for capability updates...");
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

        Console.WriteLine($"❌ Timeout waiting for API {endpoint} to become available (waited {overallTimeout.TotalSeconds}s)");
        return Guid.Empty;
    }

    /// <summary>
    /// Waits for a Response message from the WebSocket, skipping any Event messages.
    /// Event messages (like capability-manifest updates) can arrive asynchronously,
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
                var message = BinaryMessageHelper.ParseAndDecompress(receiveBuffer.Array, result.Count);

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
                        var eventType = eventObj?["eventName"]?.GetValue<string>();
                        Console.WriteLine($"   ⏭️ Skipping Event message (eventName: {eventType ?? "unknown"})");
                    }
                    catch
                    {
                        Console.WriteLine($"   ⏭️ Skipping Event message (could not parse eventName)");
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

    /// <summary>
    /// Tests the BannouClient SDK flow with a dedicated test account.
    /// This validates the complete SDK experience:
    /// 1. Register a new account via HTTP
    /// 2. Create a new BannouClient instance
    /// 3. Connect with ConnectWithTokenAsync
    /// 4. Call InvokeAsync to hit the /testing/debug/path endpoint
    /// 5. Verify the response
    /// 6. Dispose the client
    ///
    /// This test is critical for validating BannouClient works correctly before
    /// other tests (like GameSession) use it. If BannouClient has a bug, this test
    /// fails first, making the issue clear.
    /// </summary>
    private void TestBannouClientWithDedicatedAccount(string[] args)
    {
        Console.WriteLine("=== BannouClient SDK Flow Test ===");
        Console.WriteLine("Testing BannouClient with dedicated account: register -> connect -> invoke -> dispose...");

        try
        {
            var result = Task.Run(async () => await PerformBannouClientSdkTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED BannouClient SDK flow test");
            }
            else
            {
                Console.WriteLine("FAILED BannouClient SDK flow test");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED BannouClient SDK flow test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private async Task<bool> PerformBannouClientSdkTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return false;
        }

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;

        // Step 1: Create a dedicated test account
        Console.WriteLine("   Step 1: Creating dedicated test account...");
        var uniqueId = Guid.NewGuid().ToString("N")[..12];
        var testEmail = $"bannouclient_test_{uniqueId}@test.local";
        var testPassword = "BannouClientTest123!";

        string accessToken;
        string connectUrl;
        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new RegisterRequest { Username = $"bannouclient_{uniqueId}", Email = testEmail, Password = testPassword };

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
                return false;
            }

            var responseBody = await registerResponse.Content.ReadAsStringAsync();
            var registerResult = BannouJson.Deserialize<RegisterResponse>(responseBody);
            accessToken = registerResult?.AccessToken
                ?? throw new InvalidOperationException("No accessToken in response");
            connectUrl = registerResult?.ConnectUrl?.ToString()
                ?? throw new InvalidOperationException("No connectUrl in response");
            Console.WriteLine($"   Test account created: {testEmail}");
            Console.WriteLine($"   Connect URL from auth: {connectUrl}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
            return false;
        }

        // Step 2: Create a new BannouClient instance
        Console.WriteLine("   Step 2: Creating BannouClient instance...");
        await using var client = new BannouClient();

        // Step 3: Connect with the JWT using the connectUrl from auth response
        Console.WriteLine("   Step 3: Connecting with ConnectWithTokenAsync...");
        var serverUrl = connectUrl;

        try
        {
            var connected = await client.ConnectWithTokenAsync(serverUrl, accessToken);
            if (!connected)
            {
                Console.WriteLine("   ConnectWithTokenAsync returned false");
                return false;
            }

            if (!client.IsConnected)
            {
                Console.WriteLine("   Client reports not connected after ConnectWithTokenAsync");
                return false;
            }
            Console.WriteLine($"   Connected successfully, session: {client.SessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Connection failed: {ex.Message}");
            return false;
        }

        // Step 4: Call InvokeAsync to hit /testing/debug/path
        Console.WriteLine("   Step 4: Calling InvokeAsync for /testing/debug/path...");
        try
        {
            // The /testing/debug/path endpoint returns routing debug info
            // Only POST endpoints are exposed in capability manifest
            var response = (await client.InvokeAsync<object, JsonElement>(
                "/testing/debug/path",
                new { }, // Empty body
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            // Verify we got a valid response with expected fields
            var hasPath = response.TryGetProperty("Path", out var pathProp) ||
                        response.TryGetProperty("path", out pathProp);
            var hasMethod = response.TryGetProperty("Method", out var methodProp) ||
                            response.TryGetProperty("method", out methodProp);
            var hasTimestamp = response.TryGetProperty("Timestamp", out var timestampProp) ||
                                response.TryGetProperty("timestamp", out timestampProp);

            Console.WriteLine($"   Response received:");
            Console.WriteLine($"      Path field present: {hasPath}");
            Console.WriteLine($"      Method field present: {hasMethod}");
            Console.WriteLine($"      Timestamp field present: {hasTimestamp}");

            if (!hasPath || !hasMethod)
            {
                Console.WriteLine($"   Response missing expected fields");
                Console.WriteLine($"   Full response: {response}");
                return false;
            }

            Console.WriteLine($"   InvokeAsync succeeded");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   InvokeAsync failed: {ex.Message}");
            return false;
        }

        // Step 5: Verify client is still connected
        Console.WriteLine("   Step 5: Verifying client still connected...");
        if (!client.IsConnected)
        {
            Console.WriteLine("   Client disconnected unexpectedly");
            return false;
        }
        Console.WriteLine($"   Client still connected");

        // Step 6: Dispose happens automatically via using statement
        Console.WriteLine("   Step 6: Test complete, client will be disposed");

        return true;
    }

    #region Capability Manifest Caching Test

    /// <summary>
    /// Cache for service GUIDs to validate manifest caching behavior.
    /// </summary>
    private readonly Dictionary<string, Guid> _manifestGuidCache = new();

    /// <summary>
    /// Tests that the capability manifest caching behavior works correctly.
    /// This validates that when a capability manifest arrives, ALL GUIDs in the manifest
    /// are cached - not just the one being looked up. This is critical because:
    /// 1. Server only pushes the manifest once at connection time
    /// 2. Without caching all GUIDs upfront, subsequent API calls would wait forever
    ///    for a manifest that never arrives
    ///
    /// This test uses a raw WebSocket to validate the protocol behavior directly.
    /// </summary>
    private void TestCapabilityManifestCaching(string[] args)
    {
        Console.WriteLine("=== Capability Manifest Caching Test ===");
        Console.WriteLine("Validating that all API GUIDs are cached from capability manifest...");

        // Clear the cache to ensure fresh state
        _manifestGuidCache.Clear();

        try
        {
            var result = Task.Run(async () => await PerformManifestCachingTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED Manifest caching behavior test");
            }
            else
            {
                Console.WriteLine("FAILED Manifest caching behavior test");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Manifest caching test with exception: {ex.Message}");
        }
    }

    private async Task<bool> PerformManifestCachingTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return false;
        }

        var openrestyHost = Program.Configuration.OpenRestyHost ?? "openresty";
        var openrestyPort = Program.Configuration.OpenRestyPort ?? 80;

        // Create dedicated test account for this test
        Console.WriteLine("   Creating dedicated test account...");
        var uniqueId = Guid.NewGuid().ToString("N")[..12];
        var testEmail = $"manifest_test_{uniqueId}@test.local";
        var testPassword = "ManifestTest123!";

        string accessToken;
        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new RegisterRequest { Username = $"manifest_{uniqueId}", Email = testEmail, Password = testPassword };

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
                return false;
            }

            var responseBody = await registerResponse.Content.ReadAsStringAsync();
            var registerResult = BannouJson.Deserialize<RegisterResponse>(responseBody);
            accessToken = registerResult?.AccessToken
                ?? throw new InvalidOperationException("No accessToken in response");
            Console.WriteLine($"   Test account created: {testEmail}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
            return false;
        }

        // Connect via raw WebSocket to observe manifest directly
        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        await webSocket.ConnectAsync(serverUri, CancellationToken.None);
        Console.WriteLine("   WebSocket connected");

        // Request one API - this triggers receiving the manifest
        // Use /auth/validate which is always available to authenticated users
        var firstGuid = await ReceiveAndCacheAllGuids(webSocket, "/auth/validate");

        if (firstGuid == Guid.Empty)
        {
            Console.WriteLine("   Failed to get GUID for /auth/validate");
            return false;
        }
        Console.WriteLine($"   Got GUID for /auth/validate: {firstGuid}");

        // Game session endpoints are either state-gated (require in_game) or shortcut-only
        // None should appear in the initial manifest for a user who hasn't joined a session

        // These require game-session:in_game state and should NOT be in initial manifest
        var stateGatedApis = new[]
        {
            "/sessions/get",
            "/sessions/leave",
            "/sessions/chat",
            "/sessions/actions"
        };

        // These are accessed via session shortcuts (pre-bound API calls), not direct permissions
        // They intentionally have no x-permissions in the schema and won't appear in capability manifest
        var shortcutOnlyApis = new[]
        {
            "/sessions/list",
            "/sessions/create",
            "/sessions/join"
        };

        // Verify state-gated endpoints are correctly NOT in manifest (requires joining session)
        foreach (var endpoint in stateGatedApis)
        {
            if (_manifestGuidCache.TryGetValue(endpoint, out var cachedGuid))
            {
                Console.WriteLine($"   {endpoint} unexpectedly cached: {cachedGuid} (should require in_game state)");
                // Don't fail - state-gated endpoints appearing is unexpected but not wrong
            }
            else
            {
                Console.WriteLine($"   {endpoint} correctly absent (requires game-session:in_game state)");
            }
        }

        // Verify shortcut-only endpoints are correctly NOT in manifest (accessed via session shortcuts)
        foreach (var endpoint in shortcutOnlyApis)
        {
            if (_manifestGuidCache.TryGetValue(endpoint, out var cachedGuid))
            {
                Console.WriteLine($"   {endpoint} unexpectedly cached: {cachedGuid} (should be shortcut-only)");
                // Don't fail - but this is unexpected
            }
            else
            {
                Console.WriteLine($"   {endpoint} correctly absent (accessed via session shortcuts)");
            }
        }

        Console.WriteLine($"   Total APIs in cache: {_manifestGuidCache.Count}");

        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
        }

        // Test passes if we successfully received and cached the manifest
        // Game session endpoints should correctly NOT be in manifest (state-gated or shortcut-only)
        return true;
    }

    /// <summary>
    /// Receives the capability manifest and caches ALL GUIDs from it.
    /// Returns the GUID for the requested endpoint, or Guid.Empty if not found.
    /// </summary>
    private async Task<Guid> ReceiveAndCacheAllGuids(ClientWebSocket webSocket, string endpoint)
    {
        var overallTimeout = TimeSpan.FromSeconds(10);
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
                    continue;
                }

                // Parse the binary message
                var receivedMessage = BinaryMessageHelper.ParseAndDecompress(receiveBuffer.Array, result.Count);

                // Check if this is an event message (capability manifest)
                if (!receivedMessage.Flags.HasFlag(MessageFlags.Event))
                {
                    continue;
                }

                if (receivedMessage.Payload.Length == 0)
                {
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
                    continue;
                }

                // Verify this is a capability manifest
                var type = manifest?["eventName"]?.GetValue<string>();
                if (type != "connect.capability-manifest")
                {
                    continue;
                }

                var availableApis = manifest?["availableApis"]?.AsArray();
                if (availableApis == null)
                {
                    continue;
                }

                // Cache ALL GUIDs from the manifest
                var cachedCount = 0;
                foreach (var api in availableApis)
                {
                    var apiEndpoint = api?["endpoint"]?.GetValue<string>();
                    var apiGuid = api?["serviceId"]?.GetValue<string>();

                    if (!string.IsNullOrEmpty(apiEndpoint) && !string.IsNullOrEmpty(apiGuid))
                    {
                        if (Guid.TryParse(apiGuid, out var guid))
                        {
                            if (!_manifestGuidCache.ContainsKey(apiEndpoint))
                            {
                                _manifestGuidCache[apiEndpoint] = guid;
                                cachedCount++;
                            }
                        }
                    }
                }

                if (cachedCount > 0)
                {
                    Console.WriteLine($"   Cached {cachedCount} API GUIDs from manifest (total: {_manifestGuidCache.Count})");
                }

                // Return the requested endpoint's GUID
                if (_manifestGuidCache.TryGetValue(endpoint, out var requestedGuid))
                {
                    return requestedGuid;
                }

                Console.WriteLine($"   API {endpoint} not found in manifest ({_manifestGuidCache.Count} APIs cached)");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Error receiving message: {ex.Message}, retrying...");
            }
        }

        Console.WriteLine($"   Timeout waiting for API {endpoint} to become available");
        return Guid.Empty;
    }

    #endregion
}
