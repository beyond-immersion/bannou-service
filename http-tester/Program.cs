using BeyondImmersion.BannouService.Attributes;
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
            return 1;  // Return failure exit code
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
            serviceCollection.AddDaprClient();

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

            // Build the service provider
            ServiceProvider = serviceCollection.BuildServiceProvider();

            // Test Dapr connectivity
            var daprClient = ServiceProvider.GetRequiredService<DaprClient>();
            await daprClient.CheckHealthAsync();

            Console.WriteLine("✅ Service provider setup completed successfully");
            Console.WriteLine("✅ Dapr client connectivity verified");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to setup service provider: {ex.Message}");
            return false;
        }
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
            Console.WriteLine("Running in daemon mode - executing all tests sequentially with fail-fast on service crashes...");
            int failedTests = 0;
            int totalTests = 0;

            foreach (var kvp in sTestRegistry)
            {
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
                            {
                                Console.WriteLine($"🚨 Exception: {result.Exception.Message}");
                            }
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
            new DaprServiceMappingTestHandler()
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
