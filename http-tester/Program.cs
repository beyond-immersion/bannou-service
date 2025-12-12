using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.HttpTester.Tests;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;
using Dapr.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.HttpTester;

/// <summary>
/// Mock test client for tests that don't actually use the ITestClient interface
/// </summary>
public class MockTestClient : ITestClient
{
    public bool IsAuthenticated => false;
    public string TransportType => "Mock";

    public Task<TestResponse<T>> PostAsync<T>(string endpoint, object? requestBody = null) where T : class
    {
        throw new NotImplementedException("MockTestClient should not be used for actual HTTP requests");
    }

    public Task<TestResponse<T>> GetAsync<T>(string endpoint) where T : class
    {
        throw new NotImplementedException("MockTestClient should not be used for actual HTTP requests");
    }

    public Task<bool> RegisterAsync(string username, string password)
    {
        throw new NotImplementedException("MockTestClient should not be used for actual authentication");
    }

    public Task<bool> LoginAsync(string username, string password)
    {
        throw new NotImplementedException("MockTestClient should not be used for actual authentication");
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}

public class Program
{
    /// <summary>
    /// Token source for initiating a clean shutdown.
    /// </summary>
    public static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();

    /// <summary>
    /// Lookup for all service tests.
    /// </summary>
    private static readonly Dictionary<string, ServiceTest> sTestRegistry = new();

    /// <summary>
    /// Service provider for dependency injection in tests.
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; private set; }

    /// <summary>
    /// Checks if the application is running in daemon mode (non-interactive).
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>True if in daemon mode, false otherwise.</returns>
    private static bool IsDaemonMode(string[] args)
    {
        return Environment.GetEnvironmentVariable("DAEMON_MODE") == "true" ||
                args.Contains("--daemon") ||
                args.Contains("-d");
    }

    /// <summary>
    /// Waits for user input if not in daemon mode.
    /// </summary>
    /// <param name="message">Message to display to the user.</param>
    /// <param name="args">Command line arguments.</param>
    private static void WaitForUserInput(string message, string[] args)
    {
        if (!IsDaemonMode(args))
        {
            Console.WriteLine(message);
            _ = Console.ReadKey();
        }
        else
        {
            Console.WriteLine("Running in daemon mode - continuing without user input.");
        }
    }

    internal static async Task<int> Main(string[] args)
    {
        try
        {
            Console.WriteLine("Testing service-to-service communication via NSwag-generated clients...");

            // Set up dependency injection for service-to-service testing
            if (!await SetupServiceProvider())
            {
                Console.WriteLine("Failed to set up service provider for testing");
                return 1;
            }

            // Start the interactive test console with DI-enabled testing
            var exitCode = await RunInteractiveTestConsole(args);
            return exitCode;
        }
        catch (Exception exc)
        {
            ShutdownCancellationTokenSource.Cancel();
            Console.WriteLine($"An exception has occurred: '{exc.Message}'");
            Console.WriteLine($"Stack trace: {exc.StackTrace}");

            WaitForUserInput("Press any key to exit...", args);
            return 1;
        }
    }

    /// <summary>
    /// Sets up dependency injection with DaprClient and service clients for testing.
    /// </summary>
    private static async Task<bool> SetupServiceProvider()
    {
        try
        {
            var serviceCollection = new ServiceCollection();

            // Add logging
            serviceCollection.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Add Dapr client for service-to-service communication
            // In CI environment, connect to sidecar container instead of localhost
            var daprHttpEndpoint = Environment.GetEnvironmentVariable("DAPR_HTTP_ENDPOINT") ?? "http://localhost:3500";
            Console.WriteLine($"🔗 Configuring DaprClient to use endpoint: {daprHttpEndpoint}");

            // Test connectivity to the endpoint before configuring DaprClient
            try
            {
                using var httpClient = new HttpClient();
                var healthUrl = daprHttpEndpoint + "/v1.0/healthz";
                Console.WriteLine($"🔍 Testing connectivity to: {healthUrl}");
                var response = await httpClient.GetAsync(healthUrl);
                Console.WriteLine($"✅ HTTP healthz test successful: {response.StatusCode}");

                // Also test the metadata endpoint that DaprClient uses
                var metadataUrl = daprHttpEndpoint + "/v1.0/metadata";
                Console.WriteLine($"🔍 Testing metadata endpoint: {metadataUrl}");
                var metadataResponse = await httpClient.GetAsync(metadataUrl);
                Console.WriteLine($"✅ HTTP metadata test successful: {metadataResponse.StatusCode}");
                var metadataContent = await metadataResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"📋 Metadata response: {metadataContent.Substring(0, Math.Min(200, metadataContent.Length))}...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ HTTP connectivity test failed: {ex.Message}");
                Console.WriteLine($"   Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
                }
            }

            serviceCollection.AddDaprClient(daprClientBuilder =>
            {
                // Configure both HTTP and gRPC endpoints for bannou-http-tester-dapr
                var daprGrpcEndpoint = Environment.GetEnvironmentVariable("DAPR_GRPC_ENDPOINT") ?? "http://localhost:50001";
                Console.WriteLine($"🔧 DaprClient HTTP endpoint: {daprHttpEndpoint}");
                Console.WriteLine($"🔧 DaprClient gRPC endpoint: {daprGrpcEndpoint}");

                daprClientBuilder.UseHttpEndpoint(daprHttpEndpoint);
                daprClientBuilder.UseGrpcEndpoint(daprGrpcEndpoint);

                daprClientBuilder.UseJsonSerializationOptions(new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
            });

            // Add Bannou service client infrastructure
            serviceCollection.AddBannouServiceClients();

            // Register generated service clients using simple scoped registration (NSwag parameterless constructor architecture)
            serviceCollection.AddScoped<BeyondImmersion.BannouService.Auth.IAuthClient, BeyondImmersion.BannouService.Auth.AuthClient>();
            serviceCollection.AddScoped<BeyondImmersion.BannouService.Accounts.IAccountsClient, BeyondImmersion.BannouService.Accounts.AccountsClient>();
            serviceCollection.AddScoped<BeyondImmersion.BannouService.Connect.IConnectClient, BeyondImmersion.BannouService.Connect.ConnectClient>();
            serviceCollection.AddScoped<BeyondImmersion.BannouService.Behavior.IBehaviorClient, BeyondImmersion.BannouService.Behavior.BehaviorClient>();
            serviceCollection.AddScoped<BeyondImmersion.BannouService.Permissions.IPermissionsClient, BeyondImmersion.BannouService.Permissions.PermissionsClient>();
            serviceCollection.AddScoped<BeyondImmersion.BannouService.GameSession.IGameSessionClient, BeyondImmersion.BannouService.GameSession.GameSessionClient>();
            serviceCollection.AddScoped<BeyondImmersion.BannouService.Website.IWebsiteClient, BeyondImmersion.BannouService.Website.WebsiteClient>();
            serviceCollection.AddScoped<BeyondImmersion.BannouService.Orchestrator.IOrchestratorClient, BeyondImmersion.BannouService.Orchestrator.OrchestratorClient>();
            serviceCollection.AddScoped<BeyondImmersion.BannouService.Servicedata.IServicedataClient, BeyondImmersion.BannouService.Servicedata.ServicedataClient>();
            serviceCollection.AddScoped<BeyondImmersion.BannouService.Subscriptions.ISubscriptionsClient, BeyondImmersion.BannouService.Subscriptions.SubscriptionsClient>();

            // Build the service provider
            ServiceProvider = serviceCollection.BuildServiceProvider();

            // Get DaprClient for readiness check
            var daprClient = ServiceProvider.GetRequiredService<DaprClient>();

            // Wait for Dapr to be ready before proceeding (same pattern as main Bannou service)
            if (!await WaitForDaprReadiness(daprClient))
            {
                Console.WriteLine("❌ Failed to wait for Dapr readiness");
                return false;
            }

            // Wait for pubsub component to be ready (RabbitMQ connectivity)
            // This ensures event-driven tests will work properly
            if (!await WaitForPubSubReadiness(daprClient))
            {
                Console.WriteLine("❌ Pubsub readiness check failed.");
                return false;
            }

            // Wait for statestore component to be ready (Redis connectivity)
            // This ensures Accounts service tests will work properly
            if (!await WaitForStateStoreReadiness(daprClient))
            {
                Console.WriteLine("❌ Statestore readiness check failed.");
                return false;
            }

            Console.WriteLine("✅ Service provider setup completed successfully");
            Console.WriteLine("✅ Dapr client connectivity verified");

            // Wait for services to register their permissions (signals service readiness)
            // This is more reliable than fixed delays since services only register after startup complete
            var permissionsClient = ServiceProvider.GetRequiredService<BeyondImmersion.BannouService.Permissions.IPermissionsClient>();
            if (!await WaitForServiceReadiness(permissionsClient))
            {
                Console.WriteLine("❌ Service readiness check timed out.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to setup service provider: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Waits for Dapr to be ready before proceeding with testing.
    /// Uses the same pattern as the main Bannou service for consistency.
    /// </summary>
    /// <param name="daprClient">The Dapr client to use for readiness checks.</param>
    /// <returns>True if Dapr is ready, false if timeout or error occurs.</returns>
    private static async Task<bool> WaitForDaprReadiness(DaprClient daprClient)
    {
        // Use a 60-second timeout for testing scenarios
        var timeout = TimeSpan.FromSeconds(60);
        var checkInterval = TimeSpan.FromSeconds(1);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Console.WriteLine($"Waiting for Dapr to be ready (timeout: {timeout.TotalSeconds}s)...");

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                // Try to get Dapr metadata as a basic connectivity check
                var metadata = await daprClient.GetMetadataAsync();
                if (metadata != null)
                {
                    Console.WriteLine($"✅ Dapr is ready (sidecar ID: {metadata.Id})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⏳ Dapr readiness check failed, retrying... ({stopwatch.Elapsed.TotalSeconds:F1}s elapsed) - {ex.Message}");
            }

            await Task.Delay(checkInterval);
        }

        Console.WriteLine($"❌ Dapr readiness check timed out after {timeout.TotalSeconds}s. Ensure Dapr sidecar is running.");
        return false;
    }

    /// <summary>
    /// Waits for the Dapr pubsub component to be fully connected to RabbitMQ.
    /// This ensures event-driven tests will work properly.
    /// </summary>
    /// <param name="daprClient">The Dapr client to use for pubsub checks.</param>
    /// <returns>True if pubsub is ready, false if timeout or error occurs.</returns>
    private static async Task<bool> WaitForPubSubReadiness(DaprClient daprClient)
    {
        var timeout = TimeSpan.FromSeconds(60);
        var checkInterval = TimeSpan.FromSeconds(2);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var pubsubName = "bannou-pubsub";
        var testTopic = "test-connectivity";

        Console.WriteLine($"Waiting for pubsub component '{pubsubName}' to be ready (timeout: {timeout.TotalSeconds}s)...");

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                // Try to publish a test message to verify RabbitMQ connectivity
                // This will fail if RabbitMQ isn't connected yet
                await daprClient.PublishEventAsync(pubsubName, testTopic, new { test = "connectivity-check", timestamp = DateTime.UtcNow });
                Console.WriteLine($"✅ Pubsub component '{pubsubName}' is ready and connected to RabbitMQ");
                return true;
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                // Only log every few seconds to reduce noise
                if (elapsed < 5 || (int)elapsed % 5 == 0)
                {
                    Console.WriteLine($"⏳ Pubsub not ready, retrying... ({elapsed:F1}s elapsed) - {ex.Message}");
                }
            }

            await Task.Delay(checkInterval);
        }

        Console.WriteLine($"❌ Pubsub readiness check timed out after {timeout.TotalSeconds}s.");
        return false;
    }

    /// <summary>
    /// Waits for the Dapr statestore component to be fully connected to Redis.
    /// This ensures Accounts service tests will work properly.
    /// </summary>
    /// <param name="daprClient">The Dapr client to use for statestore checks.</param>
    /// <returns>True if statestore is ready, false if timeout or error occurs.</returns>
    private static async Task<bool> WaitForStateStoreReadiness(DaprClient daprClient)
    {
        var timeout = TimeSpan.FromSeconds(60);
        var checkInterval = TimeSpan.FromSeconds(2);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var storeName = "statestore";
        var testKey = "test-connectivity-check";

        Console.WriteLine($"Waiting for statestore component '{storeName}' to be ready (timeout: {timeout.TotalSeconds}s)...");

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                // Try to save and retrieve a test value to verify Redis connectivity
                await daprClient.SaveStateAsync(storeName, testKey, new { test = "connectivity-check", timestamp = DateTime.UtcNow });
                var result = await daprClient.GetStateAsync<object>(storeName, testKey);
                if (result != null)
                {
                    // Clean up test key
                    await daprClient.DeleteStateAsync(storeName, testKey);
                    Console.WriteLine($"✅ Statestore component '{storeName}' is ready and connected to Redis");
                    return true;
                }
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                // Only log every few seconds to reduce noise
                if (elapsed < 5 || (int)elapsed % 5 == 0)
                {
                    Console.WriteLine($"⏳ Statestore not ready, retrying... ({elapsed:F1}s elapsed) - {ex.Message}");
                }
            }

            await Task.Delay(checkInterval);
        }

        Console.WriteLine($"❌ Statestore readiness check timed out after {timeout.TotalSeconds}s.");
        return false;
    }

    /// <summary>
    /// Waits for services to register their permissions with the Permissions service.
    /// Services only register after fully initializing, so this is a reliable readiness signal.
    /// </summary>
    /// <param name="permissionsClient">The permissions client to use for checking registered services.</param>
    /// <returns>True if expected services are ready, false if timeout occurs.</returns>
    private static async Task<bool> WaitForServiceReadiness(BeyondImmersion.BannouService.Permissions.IPermissionsClient permissionsClient)
    {
        var timeout = TimeSpan.FromSeconds(90);
        var checkInterval = TimeSpan.FromSeconds(2);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Core services we expect to be registered before testing
        // These are the services that provide critical functionality
        var expectedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "auth",
            "accounts",
            "permissions",
            "subscriptions",  // Required for auth service login flow
            "servicedata"     // Required by subscriptions service
        };

        Console.WriteLine($"Waiting for service registration (timeout: {timeout.TotalSeconds}s)...");
        Console.WriteLine($"Expected services: {string.Join(", ", expectedServices)}");

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var response = await permissionsClient.GetRegisteredServicesAsync(new ListServicesRequest());
                if (response != null && response.Services != null)
                {
                    var registeredServiceIds = response.Services.Select(s => s.ServiceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var missingServices = expectedServices.Except(registeredServiceIds).ToList();

                    if (missingServices.Count == 0)
                    {
                        Console.WriteLine($"✅ All expected services registered:");
                        foreach (var service in response.Services)
                        {
                            Console.WriteLine($"   - {service.ServiceId}: {service.EndpointCount} endpoints (v{service.Version})");
                        }
                        return true;
                    }
                    else
                    {
                        var elapsed = stopwatch.Elapsed.TotalSeconds;
                        // Log progress periodically
                        if (elapsed < 10 || (int)elapsed % 10 == 0)
                        {
                            Console.WriteLine($"⏳ Waiting for services: {string.Join(", ", missingServices)} ({elapsed:F1}s elapsed)");
                            Console.WriteLine($"   Currently registered: {string.Join(", ", registeredServiceIds)}");
                        }
                    }
                }
                else
                {
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    if (elapsed < 5 || (int)elapsed % 5 == 0)
                    {
                        Console.WriteLine($"⏳ No services registered yet ({elapsed:F1}s elapsed)");
                    }
                }
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                if (elapsed < 5 || (int)elapsed % 5 == 0)
                {
                    Console.WriteLine($"⏳ Service readiness check failed, retrying... ({elapsed:F1}s elapsed) - {ex.Message}");
                }
            }

            await Task.Delay(checkInterval);
        }

        Console.WriteLine($"❌ Service readiness check timed out after {timeout.TotalSeconds}s.");
        return false;
    }

    /// <summary>
    /// Runs the test console that allows users to select and execute tests, or runs all tests in daemon mode
    /// </summary>
    private static async Task<int> RunInteractiveTestConsole(string[] args)
    {
        LoadServiceTests();

        if (sTestRegistry.Count == 0)
        {
            WaitForUserInput("No tests to run - press any key to exit.", args);
            return 1;  // No tests means something is wrong
        }

        // In daemon mode, run all tests automatically
        if (IsDaemonMode(args))
        {
            Console.WriteLine("Running in daemon mode - executing tests sequentially with fail-fast on service crashes...");
            int failedTests = 0;
            int totalTests = 0;

            foreach (var kvp in sTestRegistry)
            {
                // Skip the "All" meta-test in daemon mode since we're running individual tests
                if (kvp.Key == "All")
                    continue;

                Console.WriteLine($"Running test: {kvp.Value.Name}");
                totalTests++;
                try
                {
                    // Tests no longer need a client parameter - they create their own generated clients
                    // Pass a mock test client to satisfy the interface signature
                    var mockTestClient = new MockTestClient();
                    var result = await kvp.Value.TestAction(mockTestClient, Array.Empty<string>());
                    if (result.Success)
                    {
                        Console.WriteLine($"✅ Test {kvp.Value.Name} completed successfully: {result.Message}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ Test {kvp.Value.Name} failed: {result.Message}");
                        failedTests++;

                        // Check for service crash indicators
                        if (IsServiceCrashError(result.Message) || IsServiceCrashError(result.Exception?.Message))
                        {
                            Console.WriteLine($"🚨 FATAL: Service crash detected in test '{kvp.Value.Name}'. Exiting immediately to prevent cascade failures.");
                            Console.WriteLine($"🚨 Error details: {result.Message}");

                            if (result.Exception != null)
                                Console.WriteLine($"🚨 Exception: {result.Exception.Message}");

                            return 139;  // Return segfault exit code to indicate service crash
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Test {kvp.Value.Name} failed with exception: {ex.Message}");
                    failedTests++;

                    // Check for service crash indicators in exceptions
                    if (IsServiceCrashError(ex.Message))
                    {
                        Console.WriteLine($"🚨 FATAL: Service crash detected in test '{kvp.Value.Name}'. Exiting immediately to prevent cascade failures.");
                        Console.WriteLine($"🚨 Exception: {ex.Message}");
                        return 139;  // Return segfault exit code to indicate service crash
                    }
                }
            }

            Console.WriteLine($"All tests completed in daemon mode. Passed: {totalTests - failedTests}, Failed: {failedTests}");

            if (failedTests > 0)
            {
                Console.WriteLine($"❌ {failedTests} test(s) failed - returning exit code 1");
                return 1;  // Return failure exit code if any tests failed
            }
            else
            {
                Console.WriteLine("✅ All tests passed - returning exit code 0");
                return 0;  // All tests passed
            }
        }

        // Interactive mode
        string? line;
        string command;
        List<string> commandArgs;
        do
        {
            Console.WriteLine("Select a test to run from the following list (press CTRL+C to exit):");
            var i = 0;
            foreach (KeyValuePair<string, ServiceTest> kvp in sTestRegistry)
                Console.WriteLine($"{++i}. {kvp.Key} ({kvp.Value.Type}) - {kvp.Value.Description}");

            Console.WriteLine();
            Console.Write("> ");
            line = Console.ReadLine();
            if (line != null && !string.IsNullOrWhiteSpace(line))
            {
                commandArgs = line.Split(' ').ToList();
                command = commandArgs[0];
                commandArgs.RemoveAt(0);

                if (int.TryParse(command, out var commandIndex) && commandIndex >= 1 && commandIndex <= sTestRegistry.Count)
                    command = sTestRegistry.Keys.ElementAt(commandIndex - 1);

                if (sTestRegistry.TryGetValue(command, out ServiceTest? testTarget))
                {
                    Console.Clear();
                    Console.WriteLine($"Running test: {testTarget.Name}");
                    Console.WriteLine(new string('-', 50));

                    try
                    {
                        // Tests no longer need a client parameter - they create their own generated clients
                        // Pass a mock test client to satisfy the interface signature
                        var mockTestClient = new MockTestClient();
                        var result = await testTarget.TestAction(mockTestClient, commandArgs.ToArray());

                        if (result.Success)
                        {
                            Console.WriteLine($"✓ Test PASSED: {result.Message}");
                        }
                        else
                        {
                            Console.WriteLine($"✗ Test FAILED: {result.Message}");
                            if (result.Exception != null)
                            {
                                Console.WriteLine($"Exception: {result.Exception.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"✗ Test FAILED with exception: {ex.Message}");
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }
                else
                {
                    Console.WriteLine($"Command '{command}' not found.");
                }

                Console.WriteLine();
                WaitForUserInput("Press any key to continue...", args);
                Console.Clear();
            }

            Thread.Sleep(1);

        } while (true);
    }

    /// <summary>
    /// Loads all available service test handlers and registers their tests
    /// </summary>
    private static void LoadServiceTests()
    {
        // Add "All Tests" meta-test
        sTestRegistry.Add("All", new ServiceTest(RunAllTests, "All", "Meta", "Run entire test suite"));

        // Check for plugin filtering
        var pluginFilter = Environment.GetEnvironmentVariable("PLUGIN");

        // Load HTTP-specific test handlers
        var testHandlers = new List<IServiceTestHandler>
        {
            new AccountTestHandler(),
            new AuthTestHandler(),
            new ConnectTestHandler(),
            new GameSessionTestHandler(),
            new OrchestratorTestHandler(),
            new PermissionsTestHandler(),
            new ServicedataTestHandler(),
            new SubscriptionsTestHandler()
            // Add more test handlers as needed
        };

        // Filter test handlers by plugin if specified
        if (!string.IsNullOrWhiteSpace(pluginFilter))
        {
            Console.WriteLine($"🔍 Filtering tests for plugin: {pluginFilter}");
            testHandlers = testHandlers.Where(handler =>
            {
                var handlerName = handler.GetType().Name.Replace("TestHandler", "").ToLowerInvariant();
                return handlerName.Contains(pluginFilter.ToLowerInvariant()) ||
                        pluginFilter.ToLowerInvariant().Contains(handlerName);
            }).ToList();

            if (testHandlers.Count == 0)
            {
                Console.WriteLine($"⚠️ No test handlers found matching plugin filter: {pluginFilter}");
            }
            else
            {
                Console.WriteLine($"✅ Found {testHandlers.Count} test handler(s) for plugin: {pluginFilter}");
                foreach (var handler in testHandlers)
                {
                    Console.WriteLine($"   - {handler.GetType().Name}");
                }
            }
        }

        foreach (var handler in testHandlers)
        {
            foreach (ServiceTest serviceTest in handler.GetServiceTests())
                sTestRegistry.Add(serviceTest.Name, serviceTest);
        }
    }

    /// <summary>
    /// Executes all registered tests in sequence and reports overall results
    /// </summary>
    /// <param name="testClient">Not used - tests create their own clients</param>
    /// <param name="args">Command line arguments passed to each test</param>
    /// <returns>A TestResult indicating whether all tests passed</returns>
    private static async Task<TestResult> RunAllTests(ITestClient testClient, string[] args)
    {
        int passed = 0;
        int failed = 0;

        foreach (KeyValuePair<string, ServiceTest> kvp in sTestRegistry)
        {
            if (kvp.Key == "All")
                continue;

            Console.WriteLine($"Running: {kvp.Value.Name}");
            try
            {
                // Tests no longer need a client parameter - they create their own generated clients
                // Pass a mock test client to satisfy the interface signature
                var mockTestClient = new MockTestClient();
                var result = await kvp.Value.TestAction(mockTestClient, args);
                if (result.Success)
                {
                    Console.WriteLine($"  ✓ PASSED: {result.Message}");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"  ✗ FAILED: {result.Message}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ FAILED: {ex.Message}");
                failed++;
            }
            Console.WriteLine();
        }

        return new TestResult(failed == 0, $"Test suite completed. Passed: {passed}, Failed: {failed}");
    }

    /// <summary>
    /// Detects error messages that indicate a service has crashed (segfault, container exit, connection refused)
    /// </summary>
    /// <param name="errorMessage">Error message to check</param>
    /// <returns>True if the error indicates a service crash</returns>
    private static bool IsServiceCrashError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return false;

        var crashIndicators = new[]
        {
            "connection refused",           // Service not responding (likely crashed)
            "bannou, err:",                // Dapr invoke error (service unreachable)
            "dial tcp",                    // Network connection failure to service
            "ERR_DIRECT_INVOKE",          // Dapr service invocation failure
            "No connection could be made", // Windows equivalent of connection refused
            "timeout",                     // Service not responding (potential crash)
            "Connection reset by peer"     // Service crashed during request
        };

        return crashIndicators.Any(indicator =>
            errorMessage.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Will initiate a client shutdown.
    /// </summary>
    public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();
}
