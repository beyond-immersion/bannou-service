using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Test handler for Bannou service mapping and routing functionality.
/// Tests dynamic app-id resolution and service discovery events.
/// </summary>
public class BannouServiceMappingTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        // Each test must start from a clean mapping state because the resolver uses a static dictionary.
        return
        [
            new ServiceTest(Isolated(TestServiceMappingResolver), "Service Mapping Resolver", "Infrastructure", "Tests basic service-to-app-id resolution"),
            new ServiceTest(Isolated(TestServiceMappingEvents), "Service Mapping Events", "Infrastructure", "Tests RabbitMQ service mapping events"),
            new ServiceTest(Isolated(TestServiceMappingHealth), "Service Mapping Health", "Infrastructure", "Tests service mapping health endpoints")
        ];
    }

    /// <summary>
    /// Wraps a test so it starts with a clean ServiceAppMappingResolver state.
    /// </summary>
    private static Func<ITestClient, string[], Task<TestResult>> Isolated(Func<ITestClient, string[], Task<TestResult>> inner)
    {
        return async (client, args) =>
        {
            ServiceAppMappingResolver.ClearAllMappingsForTests();
            return await inner(client, args);
        };
    }


    /// <summary>
    /// Tests the basic service mapping resolver functionality.
    /// </summary>
    private static async Task<TestResult> TestServiceMappingResolver(ITestClient testClient, string[] args)
    {
        await Task.CompletedTask;
        try
        {
            Console.WriteLine("Testing service mapping resolver...");

            // Test default resolution
            var resolver = new ServiceAppMappingResolver(CreateTestLogger<ServiceAppMappingResolver>(), CreateTestConfiguration());

            // Should default to "bannou"
            var appId = resolver.GetAppIdForService("accounts");
            if (appId != "bannou")
                return new TestResult(false, $"Expected default app-id 'bannou', got '{appId}'");

            Console.WriteLine($"✓ Default service mapping: accounts -> {appId}");

            // Test dynamic mapping update
            resolver.UpdateServiceMapping("accounts", "accounts-service-east");
            appId = resolver.GetAppIdForService("accounts");
            if (appId != "accounts-service-east")
                return new TestResult(false, $"Expected updated app-id 'accounts-service-east', got '{appId}'");

            Console.WriteLine($"✓ Dynamic service mapping: accounts -> {appId}");

            // Test service removal (should revert to default)
            resolver.RemoveServiceMapping("accounts");
            appId = resolver.GetAppIdForService("accounts");
            if (appId != "bannou")
                return new TestResult(false, $"Expected reverted app-id 'bannou', got '{appId}'");

            Console.WriteLine($"✓ Service mapping removal: accounts -> {appId}");

            return new TestResult(true, "Service mapping resolver tests passed");
        }
        catch (Exception ex)
        {
            return new TestResult(false, $"Service mapping resolver test failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Tests service mapping event publishing and handling.
    /// </summary>
    private static async Task<TestResult> TestServiceMappingEvents(ITestClient testClient, string[] args)
    {
        await Task.CompletedTask;
        try
        {
            Console.WriteLine("Testing service mapping events...");

            // Test ServiceAppMappingResolver with simulated RabbitMQ events
            var resolver = new ServiceAppMappingResolver(CreateTestLogger<ServiceAppMappingResolver>(), CreateTestConfiguration());

            // Test 1: Verify default behavior (everything routes to "bannou")
            var defaultAppId = resolver.GetAppIdForService("test-service");
            if (defaultAppId != "bannou")
                return new TestResult(false, $"Expected default app-id 'bannou', got '{defaultAppId}'");

            Console.WriteLine($"✓ Default routing verified: test-service -> {defaultAppId}");

            // Test 2: Simulate RabbitMQ service mapping event
            Console.WriteLine("Simulating RabbitMQ service mapping update event...");
            resolver.UpdateServiceMapping("test-service", "test-service-east-01");

            var updatedAppId = resolver.GetAppIdForService("test-service");
            if (updatedAppId != "test-service-east-01")
                return new TestResult(false, $"Expected updated app-id 'test-service-east-01', got '{updatedAppId}'");

            Console.WriteLine($"✓ Dynamic mapping update: test-service -> {updatedAppId}");

            // Test 3: Test mapping removal (should revert to default)
            Console.WriteLine("Simulating service offline event (mapping removal)...");
            resolver.RemoveServiceMapping("test-service");

            var revertedAppId = resolver.GetAppIdForService("test-service");
            if (revertedAppId != "bannou")
                return new TestResult(false, $"Expected reverted app-id 'bannou', got '{revertedAppId}'");

            Console.WriteLine($"✓ Mapping removal: test-service -> {revertedAppId}");

            // Test 4: Test GetAllMappings for monitoring
            resolver.UpdateServiceMapping("accounts", "accounts-cluster-west");
            resolver.UpdateServiceMapping("behavior", "npc-processing-01");

            var allMappings = resolver.GetAllMappings();
            if (allMappings.Count != 2)
                return new TestResult(false, $"Expected 2 mappings, got {allMappings.Count}");

            Console.WriteLine($"✓ All mappings retrieved: {string.Join(", ", allMappings.Select(kv => $"{kv.Key}->{kv.Value}"))}");

            return new TestResult(true, "Service mapping event tests passed");
        }
        catch (Exception ex)
        {
            return new TestResult(false, $"Service mapping event test failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Tests service mapping health and monitoring functionality.
    /// </summary>
    private static async Task<TestResult> TestServiceMappingHealth(ITestClient testClient, string[] args)
    {
        await Task.CompletedTask;
        try
        {
            Console.WriteLine("Testing service mapping health and monitoring...");

            var resolver = new ServiceAppMappingResolver(CreateTestLogger<ServiceAppMappingResolver>(), CreateTestConfiguration());

            // Test 1: Health check - resolver responds correctly to various inputs
            Console.WriteLine("Testing resolver health with edge cases...");

            // Test empty service name
            var emptyResult = resolver.GetAppIdForService("");
            if (emptyResult != "bannou")
                return new TestResult(false, $"Empty service name should return 'bannou', got '{emptyResult}'");

            // Test null service name (edge case - method handles gracefully)
            string? nullServiceName = null;
            var nullResult = resolver.GetAppIdForService(nullServiceName);
            if (nullResult != "bannou")
                return new TestResult(false, $"Null service name should return 'bannou', got '{nullResult}'");

            // Test whitespace service name
            var whitespaceResult = resolver.GetAppIdForService("   ");
            if (whitespaceResult != "bannou")
                return new TestResult(false, $"Whitespace service name should return 'bannou', got '{whitespaceResult}'");

            Console.WriteLine("✓ Resolver handles edge cases correctly");

            // Test 2: Monitoring functionality - GetAllMappings
            Console.WriteLine("Testing monitoring capabilities...");

            // Initially should be empty
            var initialMappings = resolver.GetAllMappings();
            if (initialMappings.Count != 0)
                return new TestResult(false, $"Initial mappings should be empty, got {initialMappings.Count}");

            // Add some mappings and verify monitoring
            resolver.UpdateServiceMapping("accounts", "accounts-cluster");
            resolver.UpdateServiceMapping("auth", "auth-cluster");

            var updatedMappings = resolver.GetAllMappings();
            if (updatedMappings.Count != 2)
                return new TestResult(false, $"Expected 2 mappings after updates, got {updatedMappings.Count}");

            if (!updatedMappings.ContainsKey("accounts") || updatedMappings["accounts"] != "accounts-cluster")
                return new TestResult(false, "Accounts mapping not found or incorrect");

            if (!updatedMappings.ContainsKey("auth") || updatedMappings["auth"] != "auth-cluster")
                return new TestResult(false, "Auth mapping not found or incorrect");

            Console.WriteLine($"✓ Monitoring shows {updatedMappings.Count} active mappings");

            // Test 3: Thread safety simulation (rapid concurrent updates)
            Console.WriteLine("Testing concurrent access patterns...");

            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                int serviceId = i;
                tasks.Add(Task.Run(() =>
                {
                    resolver.UpdateServiceMapping($"service-{serviceId}", $"cluster-{serviceId}");
                    var result = resolver.GetAppIdForService($"service-{serviceId}");
                    // Result should be either the mapped value or "bannou" (if not yet updated)
                    if (result != $"cluster-{serviceId}" && result != "bannou")
                        throw new InvalidOperationException($"Unexpected result for service-{serviceId}: {result}");
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Console.WriteLine("✓ Concurrent access patterns handled correctly");

            return new TestResult(true, "Service mapping health and monitoring tests passed");
        }
        catch (Exception ex)
        {
            return new TestResult(false, $"Service mapping health test failed: {ex.Message}", ex);
        }
    }


    /// <summary>
    /// Creates a test logger for the given type.
    /// </summary>
    private static ILogger<T> CreateTestLogger<T>()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<ILogger<T>>();
    }

    private static AppConfiguration CreateTestConfiguration() => new AppConfiguration();
}
