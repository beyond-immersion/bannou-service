using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Orchestrator;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for orchestrator service API endpoints.
/// Tests the orchestrator service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
///
/// Note: Orchestrator APIs require admin role, so these tests will only pass
/// with admin credentials or when permissions are configured for the test user.
/// </summary>
public class OrchestratorWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "ORCH";
    private const string Description = "Orchestrator";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestInfrastructureHealthViaWebSocket, "Orchestrator - Infrastructure Health (WebSocket)", "WebSocket",
            "Test infrastructure health check via typed proxy"),
        new ServiceTest(TestServicesHealthViaWebSocket, "Orchestrator - Services Health (WebSocket)", "WebSocket",
            "Test services health report via typed proxy"),
        new ServiceTest(TestGetBackendsViaWebSocket, "Orchestrator - Get Backends (WebSocket)", "WebSocket",
            "Test backend detection via typed proxy"),
        new ServiceTest(TestGetStatusViaWebSocket, "Orchestrator - Get Status (WebSocket)", "WebSocket",
            "Test environment status via typed proxy")
    ];

    private void TestInfrastructureHealthViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Orchestrator Infrastructure Health Test (WebSocket) ===");
        Console.WriteLine("Testing infrastructure health check via typed proxy...");

        RunWebSocketTest("Orchestrator infrastructure health test", async adminClient =>
        {
            Console.WriteLine("   Checking infrastructure health via typed proxy...");
            var response = await adminClient.Orchestrator.GetInfrastructureHealthAsync(
                new InfrastructureHealthRequest(),
                timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to get infrastructure health: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Health status: {(result.Healthy ? "Healthy" : "Unhealthy")}");
            Console.WriteLine($"   Components: {result.Components?.Count ?? 0}");

            return result.Healthy;
        });
    }

    private void TestServicesHealthViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Orchestrator Services Health Test (WebSocket) ===");
        Console.WriteLine("Testing services health report via typed proxy...");

        RunWebSocketTest("Orchestrator services health test", async adminClient =>
        {
            Console.WriteLine("   Checking services health via typed proxy...");
            var response = await adminClient.Orchestrator.GetServicesHealthAsync(
                new ServiceHealthRequest(),
                timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to get services health: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Total services: {result.TotalServices}");
            Console.WriteLine($"   Health percentage: {result.HealthPercentage:F1}%");

            return result.TotalServices > 0;
        });
    }

    private void TestGetBackendsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Orchestrator Get Backends Test (WebSocket) ===");
        Console.WriteLine("Testing backend detection via typed proxy...");

        RunWebSocketTest("Orchestrator get backends test", async adminClient =>
        {
            Console.WriteLine("   Listing backends via typed proxy...");
            var response = await adminClient.Orchestrator.GetBackendsAsync(
                new ListBackendsRequest(),
                timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to list backends: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Backends: {result.Backends?.Count ?? 0}");
            Console.WriteLine($"   Recommended: {result.Recommended}");

            return result.Backends != null && result.Backends.Count > 0;
        });
    }

    private void TestGetStatusViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Orchestrator Get Status Test (WebSocket) ===");
        Console.WriteLine("Testing environment status via typed proxy...");

        RunWebSocketTest("Orchestrator get status test", async adminClient =>
        {
            Console.WriteLine("   Getting status via typed proxy...");
            var response = await adminClient.Orchestrator.GetStatusAsync(
                new GetStatusRequest(),
                timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to get status: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Deployed: {result.Deployed}");
            Console.WriteLine($"   Backend: {result.Backend}");
            Console.WriteLine($"   Services: {result.Services?.Count ?? 0}");

            return result.Services != null;
        });
    }
}
