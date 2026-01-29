using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for client event delivery system validation.
/// Tests the session-specific RabbitMQ channel architecture for pushing events to clients.
/// </summary>
public class ClientEventTestHandler : IServiceTestHandler
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
            new ServiceTest(TestClientEventDelivery, "ClientEvent - Delivery", "WebSocket",
                "Test that service publishes event and client receives via WebSocket"),
            new ServiceTest(TestEventQueueOnReconnect, "ClientEvent - Queue on Reconnect", "WebSocket",
                "Test that events are queued during disconnect and delivered on reconnect")
        };
    }

    private void TestClientEventDelivery(string[] args)
    {
        Console.WriteLine("=== Client Event Delivery Test ===");
        Console.WriteLine("Testing that service can publish event and client receives via WebSocket...");

        try
        {
            var result = Task.Run(async () => await PerformClientEventDeliveryTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED Client event delivery test PASSED");
            }
            else
            {
                Console.WriteLine("FAILED Client event delivery test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Client event delivery test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    private void TestEventQueueOnReconnect(string[] args)
    {
        Console.WriteLine("=== Event Queue on Reconnect Test ===");
        Console.WriteLine("Testing that events are queued during disconnect and delivered on reconnect...");

        try
        {
            var result = Task.Run(async () => await PerformEventQueueOnReconnectTest()).Result;
            if (result)
            {
                Console.WriteLine("PASSED Event queue on reconnect test PASSED");
            }
            else
            {
                Console.WriteLine("FAILED Event queue on reconnect test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Event queue on reconnect test FAILED with exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Tests the client event delivery flow:
    /// 1. Connect via WebSocket and receive initial capability manifest
    /// 2. Extract session ID from capability manifest
    /// 3. Call testing/publish-test-event endpoint via HTTP
    /// 4. Verify WebSocket receives the system.notification event
    /// </summary>
    private async Task<bool> PerformClientEventDeliveryTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("FAILED Configuration not available");
            return false;
        }

        // Create dedicated test account
        Console.WriteLine("Creating dedicated test account for client event delivery test...");
        var accessToken = await CreateTestAccountAsync("client_evt");
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("FAILED Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("OK WebSocket connected");

            // Wait for initial capability manifest to get session ID
            Console.WriteLine("Waiting for capability manifest to get session ID...");
            var receiveBuffer = new byte[65536];
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts1.Token);
            if (result.Count == 0)
            {
                Console.WriteLine("FAILED Received empty initial message");
                return false;
            }

            var receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
            var responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
            var manifest = JsonNode.Parse(responseText)?.AsObject();

            var messageType = manifest?["eventName"]?.GetValue<string>();
            if (messageType != "connect.capability_manifest")
            {
                Console.WriteLine($"FAILED Expected connect.capability_manifest but received '{messageType}'");
                return false;
            }

            var sessionId = manifest?["sessionId"]?.GetValue<string>();
            if (string.IsNullOrEmpty(sessionId))
            {
                Console.WriteLine("FAILED No sessionId in capability manifest");
                return false;
            }

            Console.WriteLine($"OK Received capability manifest with sessionId: {sessionId}");

            // Now call the testing/publish-test-event endpoint via admin WebSocket
            Console.WriteLine("Calling testing/publish-test-event endpoint via admin WebSocket...");

            var adminClient = Program.AdminClient;
            if (adminClient == null || !adminClient.IsConnected)
            {
                Console.WriteLine("FAILED Admin client not connected - cannot publish test event");
                Console.WriteLine("   Testing APIs require admin role. Check AdminEmails/AdminEmailDomain configuration.");
                return false;
            }

            var publishRequest = new
            {
                sessionId = sessionId,
                message = "Test notification from edge-tester"
            };

            try
            {
                var response = (await adminClient.InvokeAsync<object, JsonElement>(
                    "/testing/publish-test-event",
                    publishRequest,
                    timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

                Console.WriteLine($"OK Test event published: {response.GetRawText().Substring(0, Math.Min(200, response.GetRawText().Length))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED Failed to publish test event: {ex.Message}");
                return false;
            }

            // Wait for the event to be delivered via WebSocket
            Console.WriteLine("Waiting for event to be delivered via WebSocket...");
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts2.Token);
                if (result.Count == 0)
                {
                    Console.WriteLine("FAILED Received empty event message");
                    return false;
                }

                receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
                responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                Console.WriteLine($"Received event: {responseText[..Math.Min(500, responseText.Length)]}");

                var eventObj = JsonNode.Parse(responseText)?.AsObject();
                var eventName = eventObj?["eventName"]?.GetValue<string>();

                // Accept both schema value and NSwag enum serialization
                if (IsSystemNotificationEvent(eventName))
                {
                    var eventMessage = eventObj?["message"]?.GetValue<string>();
                    Console.WriteLine($"OK Received system.notification event: {eventMessage}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Received event type: {eventName} (may be capability update or other event)");
                    // This could be a capability update if the Testing service just registered permissions
                    // Try to receive another message - we MUST receive system.notification
                    using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts3.Token);
                    if (result.Count > 0)
                    {
                        receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
                        responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                        Console.WriteLine($"Received second event: {responseText[..Math.Min(500, responseText.Length)]}");

                        eventObj = JsonNode.Parse(responseText)?.AsObject();
                        eventName = eventObj?["eventName"]?.GetValue<string>();

                        if (IsSystemNotificationEvent(eventName))
                        {
                            Console.WriteLine($"OK Received system.notification event on second message");
                            return true;
                        }
                    }
                    // QUALITY TENETS: Test must fail if expected behavior doesn't occur
                    Console.WriteLine($"FAILED Did not receive system.notification event - client event delivery is broken");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("FAILED Timeout waiting for event - client event delivery may not be working");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Test failed: {ex.Message}");
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
    /// Tests the event queue during reconnection flow:
    /// 1. Connect via WebSocket and receive initial capability manifest
    /// 2. Get reconnection token via graceful disconnect
    /// 3. Publish event to session while disconnected
    /// 4. Reconnect with token
    /// 5. Verify queued events are delivered
    ///
    /// NOTE: This test validates the queue infrastructure. The actual queueing
    /// behavior depends on the ClientEventQueueManager being properly integrated.
    /// </summary>
    private async Task<bool> PerformEventQueueOnReconnectTest()
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("FAILED Configuration not available");
            return false;
        }

        // Create dedicated test account
        Console.WriteLine("Creating dedicated test account for reconnect queue test...");
        var accessToken = await CreateTestAccountAsync("recon_evt");
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("FAILED Failed to create test account");
            return false;
        }

        var serverUri = new Uri($"ws://{Program.Configuration.ConnectEndpoint}");
        var receiveBuffer = new byte[65536];

        // First connection to get session ID
        Console.WriteLine("Step 1: Establishing initial connection...");
        using var webSocket1 = new ClientWebSocket();
        webSocket1.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        string? sessionId = null;

        try
        {
            await webSocket1.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("OK Initial WebSocket connected");

            // Get capability manifest with session ID
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await webSocket1.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts1.Token);

            if (result.Count > 0)
            {
                var receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
                var responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                var manifest = JsonNode.Parse(responseText)?.AsObject();

                sessionId = manifest?["sessionId"]?.GetValue<string>();
                Console.WriteLine($"OK Received session ID: {sessionId}");
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Console.WriteLine("FAILED Could not get session ID from capability manifest");
                return false;
            }

            // Step 2: Graceful disconnect to get reconnection token
            Console.WriteLine("Step 2: Requesting graceful disconnect to get reconnection token...");

            // Close the WebSocket gracefully - server should send disconnect_notification with token
            await webSocket1.CloseAsync(WebSocketCloseStatus.NormalClosure, "Requesting reconnection token", CancellationToken.None);

            // The disconnect notification should have been sent before close completed
            // But since we already closed, we might not receive it. Let's check the close message.
            Console.WriteLine($"WebSocket closed with status: {webSocket1.CloseStatus}, description: {webSocket1.CloseStatusDescription}");

            // For this test, we'll use the session ID to publish an event
            // The reconnection token mechanism requires receiving the disconnect_notification
            // which is sent before the connection closes

            // Alternative approach: Just test that the infrastructure is set up correctly
            // by verifying we can publish to a session and the queue manager is involved

        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Initial connection failed: {ex.Message}");
            return false;
        }

        // Step 3: Publish event to session while disconnected via admin WebSocket
        Console.WriteLine("Step 3: Publishing event to session while disconnected via admin WebSocket...");

        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("FAILED Admin client not connected - cannot publish test event");
            Console.WriteLine("   Testing APIs require admin role. Check AdminEmails/AdminEmailDomain configuration.");
            return false;
        }

        var publishRequest = new
        {
            sessionId = sessionId,
            message = "Queued notification from edge-tester"
        };

        try
        {
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "/testing/publish-test-event",
                publishRequest,
                timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

            Console.WriteLine($"OK Event published to session while disconnected: {response.GetRawText().Substring(0, Math.Min(200, response.GetRawText().Length))}");
        }
        catch (Exception ex)
        {
            // QUALITY TENETS: If we can't publish to the session, the test cannot verify queue behavior
            // This is a legitimate failure - the session expired before we could test queuing
            Console.WriteLine($"FAILED Could not publish to disconnected session: {ex.Message}");
            Console.WriteLine("   Session expired before event could be queued - test cannot verify queue behavior");
            return false;
        }

        // Step 4: Reconnect and check for queued events
        Console.WriteLine("Step 4: Reconnecting to check for queued events...");

        using var webSocket2 = new ClientWebSocket();
        webSocket2.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
        // Note: Without proper reconnection token, this creates a new session
        // The full flow would require capturing the disconnect_notification

        try
        {
            await webSocket2.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("OK Reconnection WebSocket connected");

            // Wait for capability manifest first, then check for queued events
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            bool receivedCapabilityManifest = false;
            bool receivedQueuedEvent = false;

            try
            {
                // First message should be capability manifest
                var result = await webSocket2.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts2.Token);
                if (result.Count > 0)
                {
                    var receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
                    var responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                    Console.WriteLine($"Received on reconnect: {responseText[..Math.Min(300, responseText.Length)]}");

                    var eventObj = JsonNode.Parse(responseText)?.AsObject();
                    var msgType = eventObj?["eventName"]?.GetValue<string>();
                    Console.WriteLine($"OK Received message eventName: {msgType}");

                    if (msgType == "connect.capability_manifest")
                    {
                        receivedCapabilityManifest = true;

                        // Now wait for the queued event
                        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        result = await webSocket2.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cts3.Token);
                        if (result.Count > 0)
                        {
                            receivedMessage = BinaryMessage.Parse(receiveBuffer, result.Count);
                            responseText = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                            eventObj = JsonNode.Parse(responseText)?.AsObject();
                            var eventName = eventObj?["eventName"]?.GetValue<string>();

                            if (eventName == "system.notification")
                            {
                                Console.WriteLine($"OK Received queued system.notification event on reconnect");
                                receivedQueuedEvent = true;
                            }
                            else
                            {
                                Console.WriteLine($"Received message type: {eventName} (not the queued event)");
                            }
                        }
                    }
                    else if (msgType == "system.notification")
                    {
                        // Queued event arrived before/instead of capability manifest
                        Console.WriteLine($"OK Received queued system.notification event");
                        receivedQueuedEvent = true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // QUALITY TENETS: Timeout is a failure, not "expected behavior"
                Console.WriteLine("FAILED Timeout waiting for messages on reconnection");
            }

            // QUALITY TENETS: Test must verify the specific behavior it claims to test
            if (!receivedCapabilityManifest && !receivedQueuedEvent)
            {
                Console.WriteLine("FAILED Did not receive capability manifest or queued event on reconnection");
                return false;
            }

            if (!receivedQueuedEvent)
            {
                Console.WriteLine("FAILED Event was published but not received on reconnection - queue delivery not working");
                return false;
            }

            Console.WriteLine("OK Event queue on reconnect test complete - queued event delivered successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Reconnection failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (webSocket2.State == WebSocketState.Open)
            {
                await webSocket2.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Check if eventName matches system notification event.
    /// Accepts both schema value ("system.notification") and NSwag enum serialization ("System_notification").
    /// </summary>
    private static bool IsSystemNotificationEvent(string? eventName)
    {
        if (string.IsNullOrEmpty(eventName))
            return false;

        return eventName.Equals("system.notification", StringComparison.OrdinalIgnoreCase) ||
                eventName.Equals("System_notification", StringComparison.OrdinalIgnoreCase);
    }
}
