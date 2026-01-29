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
            // Request health for all sources (control plane + deployed)
            // With source=All, we should see control plane services immediately
            // without waiting for deployed service heartbeats
            Console.WriteLine("   Checking services health via typed proxy (source: all)...");
            var response = await adminClient.Orchestrator.GetServicesHealthAsync(
                new ServiceHealthRequest { Source = ServiceHealthSource.All },
                timeout: TimeSpan.FromSeconds(10));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to get services health: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Source: {result.Source}");
            Console.WriteLine($"   Control plane app-id: {result.ControlPlaneAppId}");
            Console.WriteLine($"   Total services: {result.TotalServices}");
            Console.WriteLine($"   Healthy: {result.HealthyServices?.Count ?? 0}");
            Console.WriteLine($"   Unhealthy: {result.UnhealthyServices?.Count ?? 0}");
            Console.WriteLine($"   Health percentage: {result.HealthPercentage:F1}%");

            if (result.TotalServices > 0)
            {
                // Log a few service names for visibility
                var sampleServices = result.HealthyServices?.Take(5).Select(s => $"{s.ServiceId}@{s.AppId}");
                if (sampleServices != null && sampleServices.Any())
                {
                    Console.WriteLine($"   Sample services: {string.Join(", ", sampleServices)}");
                }
                return true;
            }

            Console.WriteLine("   No services reported (this should not happen with source=all)");
            return false;
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
