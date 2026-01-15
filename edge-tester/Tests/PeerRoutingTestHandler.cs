using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Tests for External mode WebSocket behavior - no peer-to-peer routing.
/// These tests run BEFORE any orchestrator deployment and verify:
/// 1. External mode (default) does NOT include peerGuid in capability manifest
/// 2. Using a fake peerGuid with Client flag is rejected
///
/// Relayed mode tests (actual peer-to-peer routing) are in SplitServiceRoutingTestHandler,
/// which deploys the relayed-connect preset before testing peer routing.
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
    /// Returns the WebSocket and parsed peerGuid from the manifest (null if not present).
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
                Console.WriteLine($"   [{connectionName}] No peerGuid in capability manifest (External mode)");
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

    /// <summary>
    /// Returns External mode tests only.
    /// Relayed mode tests are in SplitServiceRoutingTestHandler (after relayed-connect deployment).
    /// </summary>
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestExternalModeNoPeerGuid, "WebSocket - External Mode No Peer GUID", "PeerRouting",
                "Test that External mode (default) does NOT include peerGuid in capability manifest"),

            new ServiceTest(TestFakePeerGuidRejected, "WebSocket - Fake Peer GUID Rejected", "PeerRouting",
                "Test that using a fake peerGuid with Client flag is rejected in External mode"),
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
                Console.WriteLine("   This test expects External mode (default)");
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

    /// <summary>
    /// Tests that using a fake peerGuid with Client flag is rejected in External mode.
    /// Even though peer routing isn't enabled, we should get a proper rejection.
    /// </summary>
    private void TestFakePeerGuidRejected(string[] args)
    {
        Console.WriteLine("=== Fake Peer GUID Rejected Test ===");
        Console.WriteLine("Testing that fake peerGuid with Client flag is rejected in External mode...");

        try
        {
            var result = Task.Run(async () => await PerformFakePeerGuidRejectedTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED: Fake peerGuid correctly rejected");
            }
            else
            {
                Console.WriteLine("FAILED: Fake peerGuid rejection test failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: Fake peerGuid rejection test failed with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private async Task<bool> PerformFakePeerGuidRejectedTest()
    {
        // Create a test account
        var accessToken = await CreateTestAccountAsync("fakepeertest");
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("   Failed to create test account");
            return false;
        }

        // Connect
        var (webSocket, peerGuid) = await ConnectAndGetPeerGuidAsync(accessToken, "FakePeerSender");

        try
        {
            if (webSocket == null)
            {
                Console.WriteLine("   Failed to establish WebSocket connection");
                return false;
            }

            // Confirm we're in External mode (no peerGuid)
            if (peerGuid != null && peerGuid != Guid.Empty)
            {
                Console.WriteLine($"   WARNING: peerGuid present ({peerGuid}) - this test assumes External mode");
                Console.WriteLine("   Test will still run to verify behavior");
            }
            else
            {
                Console.WriteLine("   Confirmed: No peerGuid (External mode)");
            }

            // Try to send a message with Client flag to a fake peerGuid
            var fakePeerGuid = Guid.NewGuid();
            Console.WriteLine($"   Attempting to route to fake peerGuid: {fakePeerGuid}");

            var message = CreatePeerMessage(fakePeerGuid, "This should be rejected", 1);
            await webSocket.SendAsync(
                new ArraySegment<byte>(message),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);

            // Should receive an error response
            Console.WriteLine("   Waiting for error response...");
            var (responsePayload, responseCode) = await ReceivePeerMessageAsync(webSocket, "FakePeerSender");

            Console.WriteLine($"   Response code: {responseCode}");
            if (responsePayload != null)
            {
                Console.WriteLine($"   Response payload: {responsePayload}");
            }

            // Response code should be non-zero (error)
            // ClientNotFound = 4, or similar error code
            if (responseCode == 0)
            {
                Console.WriteLine("   FAILED: Expected error response code, got 0 (success)");
                Console.WriteLine("   Fake peerGuid should have been rejected");
                return false;
            }

            Console.WriteLine($"   Fake peerGuid correctly rejected with error code {responseCode}");
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
    internal static byte[] CreatePeerMessage(Guid targetPeerGuid, string message, uint sequence)
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
    internal static async Task<(string? payload, byte responseCode)> ReceivePeerMessageAsync(ClientWebSocket webSocket, string connectionName)
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
