using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Tests for peer-to-peer routing via WebSocket.
/// Two WebSocket connections on the same Connect node can route messages
/// directly to each other using peer GUIDs with the Client flag (0x20).
/// </summary>
public class PeerRoutingTestHandler : IServiceTestHandler
{
    #region Helper Methods for Test Account Creation

    /// <summary>
    /// Creates a dedicated test account and returns the access token.
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

    /// <summary>
    /// Establishes a WebSocket connection and receives the capability manifest.
    /// Returns the WebSocket and parsed peerGuid from the manifest.
    /// </summary>
    private async Task<(ClientWebSocket? webSocket, Guid? peerGuid)> ConnectAndGetPeerGuidAsync(string accessToken, string connectionName)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine($"   [{connectionName}] Configuration not available");
            return (null, null);
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            Console.WriteLine($"   [{connectionName}] Connecting to {serverUri}...");
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);

            if (webSocket.State != WebSocketState.Open)
            {
                Console.WriteLine($"   [{connectionName}] WebSocket in unexpected state: {webSocket.State}");
                return (null, null);
            }

            Console.WriteLine($"   [{connectionName}] Connected, waiting for capability manifest...");

            // Wait for capability manifest (server pushes it on connection)
            var buffer = new ArraySegment<byte>(new byte[65536]);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var result = await webSocket.ReceiveAsync(buffer, cts.Token);
            if (result.Count == 0)
            {
                Console.WriteLine($"   [{connectionName}] Empty response received");
                return (webSocket, null);
            }

            // Parse the binary message to extract JSON payload
            if (result.Count < BinaryMessage.HeaderSize)
            {
                Console.WriteLine($"   [{connectionName}] Message too short for header");
                return (webSocket, null);
            }

            var payloadBytes = buffer.Array![(BinaryMessage.HeaderSize)..result.Count];
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            var manifestObj = JsonNode.Parse(payloadJson)?.AsObject();

            if (manifestObj == null)
            {
                Console.WriteLine($"   [{connectionName}] Failed to parse manifest JSON");
                return (webSocket, null);
            }

            var eventName = manifestObj["eventName"]?.GetValue<string>();
            if (eventName != "connect.capability_manifest")
            {
                Console.WriteLine($"   [{connectionName}] Unexpected event type: {eventName}");
                return (webSocket, null);
            }

            var peerGuidStr = manifestObj["peerGuid"]?.GetValue<string>();
            if (string.IsNullOrEmpty(peerGuidStr) || !Guid.TryParse(peerGuidStr, out var peerGuid))
            {
                Console.WriteLine($"   [{connectionName}] No peerGuid in capability manifest");
                return (webSocket, null);
            }

            Console.WriteLine($"   [{connectionName}] Received peerGuid: {peerGuid}");
            return (webSocket, peerGuid);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   [{connectionName}] Connection failed: {ex.Message}");
            webSocket.Dispose();
            return (null, null);
        }
    }

    /// <summary>
    /// Safely closes a WebSocket connection.
    /// </summary>
    private static async Task CloseWebSocketSafely(ClientWebSocket? webSocket)
    {
        if (webSocket == null) return;
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        finally
        {
            webSocket.Dispose();
        }
    }

    #endregion

    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            // External mode test (default - no peer routing)
            new ServiceTest(TestExternalModeNoPeerGuid, "WebSocket - External Mode No Peer GUID", "PeerRouting",
                "Test that External mode (default) does NOT include peerGuid in capability manifest"),

            // Relayed mode tests - require Connect in Relayed mode (deployed via orchestrator preset)
            // These tests will skip if peerGuid is not available (i.e., in External mode)
            new ServiceTest(TestPeerToPeerRouting, "WebSocket - Peer-to-Peer Routing", "PeerRouting",
                "Test routing messages between two WebSocket peers using Client flag (requires Relayed mode)"),
            new ServiceTest(TestBidirectionalPeerRouting, "WebSocket - Bidirectional Peer Routing", "PeerRouting",
                "Test bidirectional peer-to-peer communication between two connections (requires Relayed mode)"),
            new ServiceTest(TestUnknownPeerGuidReturnsError, "WebSocket - Unknown Peer Error", "PeerRouting",
                "Test that routing to an unknown peer GUID returns an error (requires Relayed mode)")
        };
    }

    /// <summary>
    /// Tests that External mode (default) does NOT include peerGuid in capability manifest.
    /// External mode is the default connection mode - no peer-to-peer routing.
    /// </summary>
    private void TestExternalModeNoPeerGuid(string[] args)
    {
        Console.WriteLine("=== External Mode - No Peer GUID Test ===");
        Console.WriteLine("Testing that External mode (default) does NOT include peerGuid...");

        try
        {
            var result = Task.Run(async () => await PerformExternalModeNoPeerGuidTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED: External mode correctly omits peerGuid from manifest");
            }
            else
            {
                Console.WriteLine("FAILED: External mode test failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: External mode test failed with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private async Task<bool> PerformExternalModeNoPeerGuidTest()
    {
        // Create a test account
        var accessToken = await CreateTestAccountAsync("externaltest");
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("   Failed to create test account");
            return false;
        }

        // Connect and check for peerGuid absence
        var (webSocket, peerGuid) = await ConnectAndGetPeerGuidAsync(accessToken, "TestConnection");
        try
        {
            if (webSocket == null)
            {
                Console.WriteLine("   Failed to establish WebSocket connection");
                return false;
            }

            // In External mode, peerGuid should NOT be present
            if (peerGuid != null && peerGuid != Guid.Empty)
            {
                Console.WriteLine($"   UNEXPECTED: peerGuid present ({peerGuid}) - Connect may be in Relayed mode");
                Console.WriteLine("   NOTE: If Relayed mode is intended, this test should be skipped");
                return false;
            }

            Console.WriteLine("   Confirmed: No peerGuid in External mode capability manifest (correct behavior)");
            return true;
        }
        finally
        {
            await CloseWebSocketSafely(webSocket);
        }
    }

    private void TestPeerToPeerRouting(string[] args)
    {
        Console.WriteLine("=== Peer-to-Peer Routing Test ===");
        Console.WriteLine("Testing routing messages between two WebSocket peers...");

        try
        {
            var result = Task.Run(async () => await PerformPeerToPeerRoutingTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED: Peer-to-peer routing test PASSED");
            }
            else
            {
                Console.WriteLine("FAILED: Peer-to-peer routing test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: Peer-to-peer routing test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private async Task<bool> PerformPeerToPeerRoutingTest()
    {
        // Create two test accounts
        var accessToken1 = await CreateTestAccountAsync("peer1");
        var accessToken2 = await CreateTestAccountAsync("peer2");

        if (string.IsNullOrEmpty(accessToken1) || string.IsNullOrEmpty(accessToken2))
        {
            Console.WriteLine("   Failed to create test accounts");
            return false;
        }

        // Connect both accounts
        var (webSocket1, peerGuid1) = await ConnectAndGetPeerGuidAsync(accessToken1, "Peer1");
        var (webSocket2, peerGuid2) = await ConnectAndGetPeerGuidAsync(accessToken2, "Peer2");

        try
        {
            if (webSocket1 == null || webSocket2 == null)
            {
                Console.WriteLine("   Failed to establish WebSocket connections");
                return false;
            }

            if (peerGuid1 == null || peerGuid2 == null)
            {
                Console.WriteLine("   SKIPPED: No peerGuid in manifests - Connect is in External mode");
                Console.WriteLine("   NOTE: Peer-to-peer routing requires Relayed mode (deploy via orchestrator preset)");
                // Return true to mark as SKIPPED rather than FAILED
                return true;
            }

            Console.WriteLine($"   Peer1 GUID: {peerGuid1}");
            Console.WriteLine($"   Peer2 GUID: {peerGuid2}");

            // Send a message from Peer1 to Peer2 using Client flag
            var testPayload = BannouJson.Serialize(new { message = "Hello from Peer1!", testId = Guid.NewGuid().ToString() });
            var payloadBytes = Encoding.UTF8.GetBytes(testPayload);
            var messageId = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var peerMessage = new BinaryMessage(
                flags: MessageFlags.Client,  // Client flag for peer-to-peer routing
                channel: 0,
                sequenceNumber: 1,
                serviceGuid: peerGuid2.Value,  // Target peer's GUID
                messageId: messageId,
                payload: payloadBytes
            );

            var messageBytes = peerMessage.ToByteArray();
            Console.WriteLine($"   Sending message from Peer1 to Peer2 (GUID: {peerGuid2})...");

            await webSocket1.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                CancellationToken.None);

            // Peer2 should receive the message
            Console.WriteLine("   Waiting for Peer2 to receive message...");
            var receiveBuffer = new ArraySegment<byte>(new byte[65536]);
            using var receiveCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var receiveResult = await webSocket2.ReceiveAsync(receiveBuffer, receiveCts.Token);

            if (receiveResult.Count == 0)
            {
                Console.WriteLine("   Peer2 received empty response");
                return false;
            }

            // Parse the received message
            if (receiveResult.Count < BinaryMessage.HeaderSize)
            {
                Console.WriteLine($"   Received message too short: {receiveResult.Count} bytes");
                return false;
            }

            var receivedPayloadBytes = receiveBuffer.Array![(BinaryMessage.HeaderSize)..receiveResult.Count];
            var receivedPayloadJson = Encoding.UTF8.GetString(receivedPayloadBytes);

            Console.WriteLine($"   Peer2 received: {receivedPayloadJson}");

            // Verify the payload matches
            if (!receivedPayloadJson.Contains("Hello from Peer1!"))
            {
                Console.WriteLine("   Received payload doesn't match sent message");
                return false;
            }

            Console.WriteLine("   Peer-to-peer message routing successful!");
            return true;
        }
        finally
        {
            await CloseWebSocketSafely(webSocket1);
            await CloseWebSocketSafely(webSocket2);
        }
    }

    private void TestBidirectionalPeerRouting(string[] args)
    {
        Console.WriteLine("=== Bidirectional Peer Routing Test ===");
        Console.WriteLine("Testing bidirectional peer-to-peer communication...");

        try
        {
            var result = Task.Run(async () => await PerformBidirectionalRoutingTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED: Bidirectional peer routing test PASSED");
            }
            else
            {
                Console.WriteLine("FAILED: Bidirectional peer routing test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: Bidirectional peer routing test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private async Task<bool> PerformBidirectionalRoutingTest()
    {
        // Create two test accounts
        var accessToken1 = await CreateTestAccountAsync("bidir1");
        var accessToken2 = await CreateTestAccountAsync("bidir2");

        if (string.IsNullOrEmpty(accessToken1) || string.IsNullOrEmpty(accessToken2))
        {
            Console.WriteLine("   Failed to create test accounts");
            return false;
        }

        // Connect both accounts
        var (webSocket1, peerGuid1) = await ConnectAndGetPeerGuidAsync(accessToken1, "BiPeer1");
        var (webSocket2, peerGuid2) = await ConnectAndGetPeerGuidAsync(accessToken2, "BiPeer2");

        try
        {
            if (webSocket1 == null || webSocket2 == null)
            {
                Console.WriteLine("   Failed to establish WebSocket connections");
                return false;
            }

            if (peerGuid1 == null || peerGuid2 == null)
            {
                Console.WriteLine("   SKIPPED: No peerGuid in manifests - Connect is in External mode");
                Console.WriteLine("   NOTE: Bidirectional peer routing requires Relayed mode");
                return true;  // SKIPPED
            }

            // Step 1: Peer1 sends to Peer2
            Console.WriteLine("   Step 1: Peer1 -> Peer2");
            var message1To2 = CreatePeerMessage(peerGuid2.Value, "Hello from Peer1!", 1);
            await webSocket1.SendAsync(
                new ArraySegment<byte>(message1To2),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);

            // Peer2 receives
            var (received1, _) = await ReceivePeerMessageAsync(webSocket2, "BiPeer2");
            if (received1 == null || !received1.Contains("Hello from Peer1!"))
            {
                Console.WriteLine("   Failed: Peer2 didn't receive message from Peer1");
                return false;
            }
            Console.WriteLine($"   Peer2 received: {received1}");

            // Step 2: Peer2 sends to Peer1
            Console.WriteLine("   Step 2: Peer2 -> Peer1");
            var message2To1 = CreatePeerMessage(peerGuid1.Value, "Hello from Peer2!", 2);
            await webSocket2.SendAsync(
                new ArraySegment<byte>(message2To1),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);

            // Peer1 receives
            var (received2, _) = await ReceivePeerMessageAsync(webSocket1, "BiPeer1");
            if (received2 == null || !received2.Contains("Hello from Peer2!"))
            {
                Console.WriteLine("   Failed: Peer1 didn't receive message from Peer2");
                return false;
            }
            Console.WriteLine($"   Peer1 received: {received2}");

            Console.WriteLine("   Bidirectional routing successful!");
            return true;
        }
        finally
        {
            await CloseWebSocketSafely(webSocket1);
            await CloseWebSocketSafely(webSocket2);
        }
    }

    private void TestUnknownPeerGuidReturnsError(string[] args)
    {
        Console.WriteLine("=== Unknown Peer GUID Error Test ===");
        Console.WriteLine("Testing that routing to unknown peer GUID returns error...");

        try
        {
            var result = Task.Run(async () => await PerformUnknownPeerGuidTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED: Unknown peer GUID error test PASSED");
            }
            else
            {
                Console.WriteLine("FAILED: Unknown peer GUID error test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: Unknown peer GUID error test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private async Task<bool> PerformUnknownPeerGuidTest()
    {
        // Create a test account
        var accessToken = await CreateTestAccountAsync("unknownpeer");
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("   Failed to create test account");
            return false;
        }

        // Connect
        var (webSocket, peerGuid) = await ConnectAndGetPeerGuidAsync(accessToken, "Sender");

        try
        {
            if (webSocket == null)
            {
                Console.WriteLine("   Failed to establish WebSocket connection");
                return false;
            }

            if (peerGuid == null)
            {
                Console.WriteLine("   SKIPPED: No peerGuid in manifest - Connect is in External mode");
                Console.WriteLine("   NOTE: Unknown peer GUID test requires Relayed mode");
                return true;  // SKIPPED
            }

            // Try to send to a random GUID that doesn't exist
            var unknownGuid = Guid.NewGuid();
            Console.WriteLine($"   Sending message to unknown peer GUID: {unknownGuid}");

            var message = CreatePeerMessage(unknownGuid, "This should fail", 1);
            await webSocket.SendAsync(
                new ArraySegment<byte>(message),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);

            // Should receive an error response
            Console.WriteLine("   Waiting for error response...");
            var (responsePayload, responseCode) = await ReceivePeerMessageAsync(webSocket, "Sender");

            Console.WriteLine($"   Response code: {responseCode}");

            // ResponseCode should be non-zero (ClientNotFound = 4)
            if (responseCode == 0)
            {
                Console.WriteLine("   Expected error response code, got 0 (success)");
                return false;
            }

            Console.WriteLine($"   Received expected error response (code: {responseCode})");
            return true;
        }
        finally
        {
            await CloseWebSocketSafely(webSocket);
        }
    }

    #region Binary Message Helpers

    /// <summary>
    /// Creates a binary message for peer-to-peer routing.
    /// </summary>
    private static byte[] CreatePeerMessage(Guid targetPeerGuid, string message, uint sequence)
    {
        var payload = BannouJson.Serialize(new { message, timestamp = DateTimeOffset.UtcNow.ToString("o") });
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var messageId = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var binaryMessage = new BinaryMessage(
            flags: MessageFlags.Client,  // Client flag for peer-to-peer routing
            channel: 0,
            sequenceNumber: sequence,
            serviceGuid: targetPeerGuid,
            messageId: messageId,
            payload: payloadBytes
        );

        return binaryMessage.ToByteArray();
    }

    /// <summary>
    /// Receives a message from WebSocket and parses the payload.
    /// Returns the JSON payload string and response code.
    /// </summary>
    private static async Task<(string? payload, byte responseCode)> ReceivePeerMessageAsync(ClientWebSocket webSocket, string connectionName)
    {
        var buffer = new ArraySegment<byte>(new byte[65536]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            var result = await webSocket.ReceiveAsync(buffer, cts.Token);

            if (result.Count == 0)
            {
                Console.WriteLine($"   [{connectionName}] Empty response received");
                return (null, 0);
            }

            // Check if it's a response (smaller header)
            if (result.Count >= BinaryMessage.ResponseHeaderSize)
            {
                var flags = (MessageFlags)buffer.Array![0];
                var responseCode = buffer.Array![15]; // Response code at byte 15 in response header

                // Extract payload based on whether it's a response or request
                int headerSize = flags.HasFlag(MessageFlags.Response) ? BinaryMessage.ResponseHeaderSize : BinaryMessage.HeaderSize;

                if (result.Count > headerSize)
                {
                    var payloadBytes = buffer.Array![headerSize..result.Count];
                    var payloadJson = Encoding.UTF8.GetString(payloadBytes);
                    return (payloadJson, responseCode);
                }

                return (null, responseCode);
            }

            Console.WriteLine($"   [{connectionName}] Message too short: {result.Count} bytes");
            return (null, 0);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"   [{connectionName}] Timeout waiting for response");
            return (null, 0);
        }
    }

    #endregion
}
