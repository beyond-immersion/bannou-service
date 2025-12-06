using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for orchestrator service API endpoints.
/// Tests the orchestrator service APIs through the Connect service WebSocket binary protocol.
///
/// Note: Orchestrator APIs require admin role, so these tests will only pass
/// with admin credentials or when permissions are configured for the test user.
/// </summary>
public class OrchestratorWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestInfrastructureHealthViaWebSocket, "Orchestrator - Infrastructure Health (WebSocket)", "WebSocket",
                "Test infrastructure health check via WebSocket binary protocol"),
            new ServiceTest(TestServicesHealthViaWebSocket, "Orchestrator - Services Health (WebSocket)", "WebSocket",
                "Test services health report via WebSocket binary protocol"),
            new ServiceTest(TestGetBackendsViaWebSocket, "Orchestrator - Get Backends (WebSocket)", "WebSocket",
                "Test backend detection via WebSocket binary protocol"),
            new ServiceTest(TestGetStatusViaWebSocket, "Orchestrator - Get Status (WebSocket)", "WebSocket",
                "Test environment status via WebSocket binary protocol")
        };
    }

    private void TestInfrastructureHealthViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Orchestrator Infrastructure Health Test (WebSocket) ===");
        Console.WriteLine("Testing /orchestrator/health/infrastructure via WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () => await PerformOrchestratorApiTest(
                "POST",
                "/orchestrator/health/infrastructure",
                new { }, // Empty request body for POST-only pattern
                response =>
                {
                    if (response?["healthy"] != null)
                    {
                        var healthy = response?["healthy"]?.GetValue<bool>();
                        var components = response?["components"]?.AsArray();
                        var componentCount = components?.Count ?? 0;
                        Console.WriteLine($"   Health status: {(healthy == true ? "Healthy" : "Unhealthy")}");
                        Console.WriteLine($"   Components: {componentCount}");
                        return healthy == true;
                    }
                    return false;
                })).Result;

            if (result)
            {
                Console.WriteLine("✅ Orchestrator infrastructure health test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Orchestrator infrastructure health test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Orchestrator infrastructure health test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestServicesHealthViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Orchestrator Services Health Test (WebSocket) ===");
        Console.WriteLine("Testing /orchestrator/health/services via WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () => await PerformOrchestratorApiTest(
                "POST",
                "/orchestrator/health/services",
                new { }, // Empty request body for POST-only pattern
                response =>
                {
                    if (response?["totalServices"] != null)
                    {
                        var totalServices = response?["totalServices"]?.GetValue<int>() ?? 0;
                        var healthPercentage = response?["healthPercentage"]?.GetValue<double>() ?? 0;
                        Console.WriteLine($"   Total services: {totalServices}");
                        Console.WriteLine($"   Health percentage: {healthPercentage:F1}%");
                        return true; // Test passes if we got valid response structure
                    }
                    return false;
                })).Result;

            if (result)
            {
                Console.WriteLine("✅ Orchestrator services health test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Orchestrator services health test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Orchestrator services health test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestGetBackendsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Orchestrator Get Backends Test (WebSocket) ===");
        Console.WriteLine("Testing /orchestrator/backends/list via WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () => await PerformOrchestratorApiTest(
                "POST",
                "/orchestrator/backends/list",
                new { }, // Empty request body for POST-only pattern
                response =>
                {
                    if (response?["backends"] != null)
                    {
                        var backends = response?["backends"]?.AsArray();
                        var recommended = response?["recommended"]?.GetValue<string>();
                        Console.WriteLine($"   Backends: {backends?.Count ?? 0}");
                        Console.WriteLine($"   Recommended: {recommended}");
                        return true; // Test passes if we got valid response structure
                    }
                    return false;
                })).Result;

            if (result)
            {
                Console.WriteLine("✅ Orchestrator get backends test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Orchestrator get backends test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Orchestrator get backends test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestGetStatusViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Orchestrator Get Status Test (WebSocket) ===");
        Console.WriteLine("Testing /orchestrator/status via WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () => await PerformOrchestratorApiTest(
                "POST",
                "/orchestrator/status",
                new { }, // Empty request body for POST-only pattern
                response =>
                {
                    if (response?["deployed"] != null)
                    {
                        var deployed = response?["deployed"]?.GetValue<bool>();
                        var backend = response?["backend"]?.GetValue<string>();
                        var services = response?["services"]?.AsArray();
                        Console.WriteLine($"   Deployed: {deployed}");
                        Console.WriteLine($"   Backend: {backend}");
                        Console.WriteLine($"   Services: {services?.Count ?? 0}");
                        return true; // Test passes if we got valid response structure
                    }
                    return false;
                })).Result;

            if (result)
            {
                Console.WriteLine("✅ Orchestrator get status test PASSED");
            }
            else
            {
                Console.WriteLine("❌ Orchestrator get status test FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Orchestrator get status test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Performs an orchestrator API call through the WebSocket binary protocol.
    /// </summary>
    private async Task<bool> PerformOrchestratorApiTest(
        string method,
        string path,
        object? body,
        Func<JsonObject?, bool> validateResponse)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("❌ Configuration not available");
            return false;
        }

        // Use admin access token for orchestrator APIs (requires admin role)
        var accessToken = Program.AdminAccessToken;

        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("❌ Admin access token not available - ensure admin login completed successfully");
            Console.WriteLine("   Orchestrator APIs require admin role. Check AdminEmails/AdminEmailDomain configuration.");
            return false;
        }

        // Debug: Log token info to verify correct token is being used
        Console.WriteLine($"🔑 Using admin access token: {accessToken.Substring(0, Math.Min(20, accessToken.Length))}...");
        try
        {
            // Decode JWT to show which account it's for (without validation)
            var tokenParts = accessToken.Split('.');
            if (tokenParts.Length >= 2)
            {
                var payload = tokenParts[1];
                // Add padding if needed
                payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                var payloadJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/')));
                Console.WriteLine($"   JWT payload: {payloadJson}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Could not decode JWT: {ex.Message}");
        }

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for orchestrator API test");

            // First, receive the capability manifest pushed by the server
            Console.WriteLine("📥 Waiting for capability manifest...");
            var serviceGuid = await ReceiveCapabilityManifestAndGetGuid(webSocket, method, path);

            if (serviceGuid == Guid.Empty)
            {
                Console.WriteLine("❌ Failed to receive capability manifest or find GUID for endpoint");
                return false;
            }

            Console.WriteLine($"✅ Found service GUID for {method}:{path}: {serviceGuid}");

            // Create an API proxy request using binary protocol
            var apiRequest = new
            {
                method = method,
                path = path,
                headers = new Dictionary<string, string>(),
                body = body != null ? JsonSerializer.Serialize(body) : null
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

            Console.WriteLine($"📤 Sending orchestrator API request:");
            Console.WriteLine($"   Method: {method}");
            Console.WriteLine($"   Path: {path}");
            Console.WriteLine($"   ServiceGuid: {serviceGuid}");

            // Send the API proxy request
            var messageBytes = binaryMessage.ToByteArray();
            var buffer = new ArraySegment<byte>(messageBytes);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Receive response with timeout - wait for a Response message, skip any Event messages
            var responseResult = await WaitForResponseMessage(webSocket, TimeSpan.FromSeconds(15));

            if (responseResult == null)
            {
                Console.WriteLine("❌ Timeout waiting for API response message");
                return false;
            }

            var response = responseResult.Value;
            Console.WriteLine($"📥 Received API response: {response.Payload.Length} bytes payload");

            try
            {
                if (response.Payload.Length > 0)
                {
                    var responsePayload = Encoding.UTF8.GetString(response.Payload.Span);
                    Console.WriteLine($"   Response payload: {responsePayload.Substring(0, Math.Min(500, responsePayload.Length))}...");

                    // Parse as JSON and validate
                    var responseObj = JsonNode.Parse(responsePayload)?.AsObject();

                    // Check for error response
                    if (responseObj?["error"] != null)
                    {
                        var errorMessage = responseObj?["error"]?.GetValue<string>();
                        var statusCode = responseObj?["statusCode"]?.GetValue<int>();
                        Console.WriteLine($"❌ API returned error: {statusCode} - {errorMessage}");

                        // 403 Forbidden is expected if user doesn't have admin role
                        if (statusCode == 403)
                        {
                            Console.WriteLine("⚠️ Access denied - orchestrator APIs require admin role");
                            Console.WriteLine("   This is expected behavior if test user doesn't have admin permissions");
                        }

                        return false;
                    }

                    // Validate the response
                    return validateResponse(responseObj);
                }

                Console.WriteLine("⚠️ API response has no payload");
                return false;
            }
            catch (JsonException)
            {
                // Not JSON - may be a text error message
                var responseText = Encoding.UTF8.GetString(response.Payload.Span);
                Console.WriteLine($"⚠️ Non-JSON response: {responseText}");
                return false;
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"❌ Failed to parse API response: {parseEx.Message}");
                Console.WriteLine($"   Raw data (first 100 bytes): {Convert.ToHexString(response.Payload.ToArray(), 0, Math.Min(100, response.Payload.Length))}");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("❌ Request timed out waiting for response");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Orchestrator API test failed: {ex.Message}");
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
    /// Receives the capability manifest from the server and extracts the service GUID for the requested endpoint.
    /// The server pushes the capability manifest immediately after WebSocket connection is established.
    /// If the API isn't available initially, waits for capability updates (up to 30 seconds).
    /// </summary>
    private async Task<Guid> ReceiveCapabilityManifestAndGetGuid(
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
                Console.WriteLine($"   Manifest preview: {payloadJson.Substring(0, Math.Min(300, payloadJson.Length))}...");

                var availableApis = manifest?["availableAPIs"]?.AsArray();
                if (availableApis == null)
                {
                    Console.WriteLine("⚠️ No availableAPIs in manifest, waiting for update...");
                    continue;
                }

                Console.WriteLine($"   Available APIs: {availableApis.Count}");

                // Try to find the GUID for our endpoint
                var guid = FindGuidInManifest(availableApis, method, path);
                if (guid != Guid.Empty)
                {
                    return guid;
                }

                // API not found yet - if this is the initial manifest, wait for updates
                Console.WriteLine($"⚠️ API {method}:{path} not found yet, waiting for capability updates...");
                Console.WriteLine("   Currently available endpoints:");
                foreach (var api in availableApis)
                {
                    Console.WriteLine($"     - {api?["method"]}:{api?["path"]} ({api?["serviceName"]})");
                }
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
    /// Finds the GUID for a specific endpoint in the capability manifest.
    /// </summary>
    private Guid FindGuidInManifest(JsonArray availableApis, string method, string path)
    {
        // Try exact match first
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

        return Guid.Empty;
    }

    /// <summary>
    /// Waits for a Response message from the WebSocket, skipping any Event messages.
    /// Event messages (like capability_manifest updates) can arrive asynchronously,
    /// so we need to filter for actual API responses.
    /// </summary>
    private async Task<BinaryMessage?> WaitForResponseMessage(ClientWebSocket webSocket, TimeSpan timeout)
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
