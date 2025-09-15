using BeyondImmersion.BannouService.Connect.Protocol;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;

namespace BeyondImmersion.ServiceTester.Tests;

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
            new ServiceTest(TestServiceDiscovery, "WebSocket - Service Discovery", "WebSocket",
                "Test API discovery through WebSocket binary protocol"),
            new ServiceTest(TestInternalAPIProxy, "WebSocket - Internal API Proxy", "WebSocket",
                "Test proxying internal API calls through WebSocket binary protocol")
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

    private void TestServiceDiscovery(string[] args)
    {
        Console.WriteLine("=== Service Discovery Test ===");
        Console.WriteLine("Testing API discovery through WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () => await PerformServiceDiscoveryTest()).Result;
            if (result)
            {
                Console.WriteLine("✅ Service discovery test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Service discovery test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Service discovery test FAILED with exception: {ex.Message}");
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

        // Get access token from Program (should already be set from login)
        var accessTokenField = typeof(Program).GetField("sAccessToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var accessToken = (string?)accessTokenField?.GetValue(null);

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Access token not available - ensure login completed successfully");
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

        var accessTokenField = typeof(Program).GetField("sAccessToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var accessToken = (string?)accessTokenField?.GetValue(null);

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Access token not available");
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

    private async Task<bool> PerformServiceDiscoveryTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        var accessTokenField = typeof(Program).GetField("sAccessToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var accessToken = (string?)accessTokenField?.GetValue(null);

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Access token not available");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for service discovery test");

            // Create a service discovery request using binary protocol
            var discoveryRequest = new { action = "discover_apis", sessionId = "test-session-" + Guid.NewGuid().ToString("N")[..8] };
            var requestPayload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(discoveryRequest));

            var binaryMessage = new BinaryMessage(
                flags: MessageFlags.None,
                channel: 0, // Discovery channel
                sequenceNumber: 1,
                serviceGuid: Guid.NewGuid(),
                messageId: GuidGenerator.GenerateMessageId(),
                payload: requestPayload
            );

            Console.WriteLine($"📤 Sending service discovery request:");
            Console.WriteLine($"   Request: {JsonConvert.SerializeObject(discoveryRequest)}");

            // Send the discovery request
            var messageBytes = binaryMessage.ToByteArray();
            var buffer = new ArraySegment<byte>(messageBytes);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Receive response
            var receiveBuffer = new ArraySegment<byte>(new byte[8192]); // Larger buffer for API list
            var result = await webSocket.ReceiveAsync(receiveBuffer, CancellationToken.None);

            Console.WriteLine($"📥 Received service discovery response: {result.Count} bytes");

            if (receiveBuffer.Array == null)
            {
                Console.WriteLine("❌ Received buffer is null");
                return false;
            }

            try
            {
                var receivedMessage = BinaryMessage.Parse(receiveBuffer.Array, result.Count);

                if (receivedMessage.Payload.Length > 0)
                {
                    var responsePayload = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                    Console.WriteLine($"✅ Service discovery response: {responsePayload}");

                    // Try to parse as JSON to verify structure
                    var discoveryResponse = JsonConvert.DeserializeObject(responsePayload);
                    if (discoveryResponse != null)
                    {
                        Console.WriteLine("✅ Service discovery response is valid JSON");
                        return true;
                    }
                }

                Console.WriteLine("⚠️ Service discovery response has no payload");
                return false;
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"❌ Failed to parse service discovery response: {parseEx.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Service discovery test failed: {ex.Message}");
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

        var accessTokenField = typeof(Program).GetField("sAccessToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var accessToken = (string?)accessTokenField?.GetValue(null);

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Access token not available");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for internal API proxy test");

            // Create an internal API request using binary protocol
            // Test a simple API call like getting service mappings
            var apiRequest = new
            {
                method = "GET",
                path = "/connect/service-mappings",
                headers = new Dictionary<string, string>(),
                body = (string?)null
            };
            var requestPayload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(apiRequest));

            var binaryMessage = new BinaryMessage(
                flags: MessageFlags.None,
                channel: 2, // API proxy channel
                sequenceNumber: 1,
                serviceGuid: Guid.NewGuid(),
                messageId: GuidGenerator.GenerateMessageId(),
                payload: requestPayload
            );

            Console.WriteLine($"📤 Sending internal API proxy request:");
            Console.WriteLine($"   Method: {apiRequest.method}");
            Console.WriteLine($"   Path: {apiRequest.path}");

            // Send the API proxy request
            var messageBytes = binaryMessage.ToByteArray();
            var buffer = new ArraySegment<byte>(messageBytes);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Receive response
            var receiveBuffer = new ArraySegment<byte>(new byte[8192]);
            var result = await webSocket.ReceiveAsync(receiveBuffer, CancellationToken.None);

            Console.WriteLine($"📥 Received API proxy response: {result.Count} bytes");

            if (receiveBuffer.Array == null)
            {
                Console.WriteLine("❌ Received buffer is null");
                return false;
            }

            try
            {
                var receivedMessage = BinaryMessage.Parse(receiveBuffer.Array, result.Count);

                if (receivedMessage.Payload.Length > 0)
                {
                    var responsePayload = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                    Console.WriteLine($"✅ API proxy response: {responsePayload}");

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
}
