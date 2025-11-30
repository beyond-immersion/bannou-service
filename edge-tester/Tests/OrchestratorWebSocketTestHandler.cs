using BeyondImmersion.BannouService.Connect.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;

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
                "GET",
                "/orchestrator/health/infrastructure",
                null,
                response =>
                {
                    if (response["healthy"] != null)
                    {
                        var healthy = (bool?)response["healthy"];
                        var components = response["components"] as JArray;
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
                "GET",
                "/orchestrator/health/services",
                null,
                response =>
                {
                    if (response["totalServices"] != null)
                    {
                        var totalServices = (int?)response["totalServices"] ?? 0;
                        var healthPercentage = (double?)response["healthPercentage"] ?? 0;
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
        Console.WriteLine("Testing /orchestrator/backends via WebSocket binary protocol...");

        try
        {
            var result = Task.Run(async () => await PerformOrchestratorApiTest(
                "GET",
                "/orchestrator/backends",
                null,
                response =>
                {
                    if (response["backends"] != null)
                    {
                        var backends = response["backends"] as JArray;
                        var recommended = (string?)response["recommended"];
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
                "GET",
                "/orchestrator/status",
                null,
                response =>
                {
                    if (response["deployed"] != null)
                    {
                        var deployed = (bool?)response["deployed"];
                        var backend = (string?)response["backend"];
                        var services = response["services"] as JArray;
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
        Func<JObject, bool> validateResponse)
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

        var serverUri = new Uri($"ws://{Program.Configuration.Connect_Endpoint}");
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ WebSocket connected for orchestrator API test");

            // Create an API proxy request using binary protocol
            var apiRequest = new
            {
                method = method,
                path = path,
                headers = new Dictionary<string, string>(),
                body = body != null ? JsonConvert.SerializeObject(body) : null
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

            Console.WriteLine($"📤 Sending orchestrator API request:");
            Console.WriteLine($"   Method: {method}");
            Console.WriteLine($"   Path: {path}");

            // Send the API proxy request
            var messageBytes = binaryMessage.ToByteArray();
            var buffer = new ArraySegment<byte>(messageBytes);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Receive response with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var receiveBuffer = new ArraySegment<byte>(new byte[16384]); // Larger buffer for full responses
            var result = await webSocket.ReceiveAsync(receiveBuffer, cts.Token);

            Console.WriteLine($"📥 Received API response: {result.Count} bytes");

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
                    Console.WriteLine($"   Response payload: {responsePayload.Substring(0, Math.Min(500, responsePayload.Length))}...");

                    // Parse as JSON and validate
                    var responseObj = JObject.Parse(responsePayload);

                    // Check for error response
                    if (responseObj["error"] != null)
                    {
                        var errorMessage = (string?)responseObj["error"];
                        var statusCode = (int?)responseObj["statusCode"];
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
                var responseText = Encoding.UTF8.GetString(receiveBuffer.Array, 0, result.Count);
                Console.WriteLine($"⚠️ Non-JSON response: {responseText}");
                return false;
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"❌ Failed to parse API response: {parseEx.Message}");
                Console.WriteLine($"   Raw data (first 100 bytes): {Convert.ToHexString(receiveBuffer.Array, 0, Math.Min(100, result.Count))}");
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
}
