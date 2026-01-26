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
    /// Performs an orchestrator API call via the shared admin WebSocket.
    /// Uses Program.AdminClient which is already connected - we must NOT create new WebSocket
    /// connections as that would "subsume" (disconnect) the shared admin client.
    /// </summary>
    private async Task<bool> PerformOrchestratorApiTest(
        string method,
        string path,
        object? body,
        Func<JsonObject?, bool> validateResponse)
    {
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
            Console.WriteLine("   Orchestrator APIs require admin role. Check AdminEmails/AdminEmailDomain configuration.");
            return false;
        }

        Console.WriteLine($"📤 Sending orchestrator API request via shared admin WebSocket:");
        Console.WriteLine($"   Method: {method}");
        Console.WriteLine($"   Path: {path}");

        try
        {
            // Use the shared admin WebSocket to invoke the API
            var requestBody = body ?? new { };
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                method,
                path,
                requestBody,
                timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

            // Convert JsonElement to JsonObject for validation
            var responseJson = response.GetRawText();
            Console.WriteLine($"📥 Received response: {responseJson.Substring(0, Math.Min(500, responseJson.Length))}...");

            var responseObj = JsonNode.Parse(responseJson)?.AsObject();
            return validateResponse(responseObj);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unknown endpoint"))
        {
            Console.WriteLine($"❌ Endpoint not available: {method} {path}");
            Console.WriteLine($"   Admin may not have access to orchestrator APIs");
            Console.WriteLine($"   Available APIs: {string.Join(", ", adminClient.AvailableApis.Keys.Take(10))}...");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Orchestrator API test failed: {ex.Message}");
            return false;
        }
    }
}
