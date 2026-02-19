using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.HttpTester.Tests;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace BeyondImmersion.BannouService.HttpTester;

/// <summary>
/// Message type for RabbitMQ connectivity check.
/// </summary>
public record ConnectivityCheckMessage(string Test, DateTime Timestamp);

/// <summary>
/// Mock test client for tests that don't actually use the ITestClient interface
/// </summary>
public class MockTestClient : ITestClient
{
    public bool IsAuthenticated => false;
    public string TransportType => "Mock";

    public async Task<TestResponse<T>> PostAsync<T>(string endpoint, object? requestBody = null) where T : class
    {
        await Task.CompletedTask;
        throw new NotImplementedException("MockTestClient should not be used for actual HTTP requests");
    }

    public async Task<TestResponse<T>> GetAsync<T>(string endpoint) where T : class
    {
        await Task.CompletedTask;
        throw new NotImplementedException("MockTestClient should not be used for actual HTTP requests");
    }

    public async Task<bool> RegisterAsync(string username, string password)
    {
        await Task.CompletedTask;
        throw new NotImplementedException("MockTestClient should not be used for actual authentication");
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        await Task.CompletedTask;
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
    /// Sets up dependency injection with service clients for testing.
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

            // Register AppConfiguration required by ServiceAppMappingResolver
            serviceCollection.AddSingleton<BeyondImmersion.BannouService.Configuration.AppConfiguration>();

            // Configure direct RabbitMQ for pub/sub communication (no MassTransit)
            var rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq";
            var rabbitPort = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672");
            var rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? "guest";
            var rabbitPass = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";

            Console.WriteLine($"🔗 Configuring direct RabbitMQ at {rabbitHost}:{rabbitPort}");

            // Register MessagingServiceConfiguration with RabbitMQ settings
            var messagingConfig = new MessagingServiceConfiguration
            {
                RabbitMQHost = rabbitHost,
                RabbitMQPort = rabbitPort,
                RabbitMQUsername = rabbitUser,
                RabbitMQPassword = rabbitPass,
                RabbitMQVirtualHost = "/",
                DefaultExchange = AppConstants.DEFAULT_APP_NAME
            };
            serviceCollection.AddSingleton(messagingConfig);

            // Register shared connection manager as both concrete type and interface
            serviceCollection.AddSingleton<RabbitMQConnectionManager>();
            serviceCollection.AddSingleton<IChannelManager>(sp => sp.GetRequiredService<RabbitMQConnectionManager>());

            // Register retry buffer for handling transient publish failures
            serviceCollection.AddSingleton<MessageRetryBuffer>();
            serviceCollection.AddSingleton<IRetryBuffer>(sp => sp.GetRequiredService<MessageRetryBuffer>());

            // Register IMessageBus using factory pattern to break circular dependency:
            // RabbitMQMessageBus → IRetryBuffer → MessageRetryBuffer → IMessageBus? (optional)
            // Factory registration makes the dependency chain opaque to DI cycle detection,
            // allowing MessageRetryBuffer to receive null for IMessageBus? during construction.
            serviceCollection.AddSingleton<IMessageBus>(sp =>
            {
                var channelManager = sp.GetRequiredService<IChannelManager>();
                var retryBuffer = sp.GetRequiredService<IRetryBuffer>();
                var appConfig = sp.GetRequiredService<BeyondImmersion.BannouService.Configuration.AppConfiguration>();
                var msgConfig = sp.GetRequiredService<MessagingServiceConfiguration>();
                var logger = sp.GetRequiredService<ILogger<RabbitMQMessageBus>>();
                var telemetryProvider = sp.GetRequiredService<ITelemetryProvider>();
                return new RabbitMQMessageBus(channelManager, retryBuffer, appConfig, msgConfig, logger, telemetryProvider);
            });

            // Register IMessageSubscriber using factory pattern (same as MessagingServicePlugin)
            serviceCollection.AddSingleton<IMessageSubscriber>(sp =>
            {
                var channelManager = sp.GetRequiredService<IChannelManager>();
                var logger = sp.GetRequiredService<ILogger<RabbitMQMessageSubscriber>>();
                var msgConfig = sp.GetRequiredService<MessagingServiceConfiguration>();
                var telemetryProvider = sp.GetRequiredService<ITelemetryProvider>();
                var messageBus = sp.GetRequiredService<IMessageBus>();
                return new RabbitMQMessageSubscriber(channelManager, logger, msgConfig, telemetryProvider, messageBus);
            });

            // Register IMessageTap for event forwarding/tapping
            serviceCollection.AddSingleton<IMessageTap, RabbitMQMessageTap>();

            // =====================================================================
            // STATE INFRASTRUCTURE - Redis connection via lib-state
            // =====================================================================
            var stateRedisConnectionString = Environment.GetEnvironmentVariable("STATE_REDIS_CONNECTION_STRING")
                ?? "bannou-redis:6379";

            Console.WriteLine($"🔗 Configuring State to use Redis at {stateRedisConnectionString}");

            // Register StateServiceConfiguration
            var stateConfig = new StateServiceConfiguration
            {
                UseInMemory = false,
                RedisConnectionString = stateRedisConnectionString,
                ConnectionTimeoutSeconds = 60
            };
            serviceCollection.AddSingleton(stateConfig);

            // Build StateStoreFactoryConfiguration from StateServiceConfiguration
            // (same pattern as StateServicePlugin.BuildFactoryConfiguration)
            var factoryConfig = new StateStoreFactoryConfiguration
            {
                UseInMemory = stateConfig.UseInMemory,
                RedisConnectionString = stateRedisConnectionString,
                ConnectionRetryCount = 10,
                ConnectionTimeoutSeconds = stateConfig.ConnectionTimeoutSeconds
            };

            // Load store configurations from generated definitions (schema-first approach)
            foreach (var (storeName, storeConfig) in StateStoreDefinitions.Configurations)
            {
                factoryConfig.Stores[storeName] = storeConfig;
            }
            serviceCollection.AddSingleton(factoryConfig);

            // Register IStateStoreFactory using factory pattern (same as StateServicePlugin)
            // Uses sp.GetService (not GetRequiredService) for IMessageBus to handle
            // scenarios where messaging may not be fully initialized yet
            serviceCollection.AddSingleton<IStateStoreFactory>(sp =>
            {
                var config = sp.GetRequiredService<StateStoreFactoryConfiguration>();
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var telemetryProvider = sp.GetRequiredService<ITelemetryProvider>();
                var messageBus = sp.GetService<IMessageBus>();
                return new StateStoreFactory(config, loggerFactory, telemetryProvider, messageBus);
            });

            // =====================================================================
            // MESH INFRASTRUCTURE - Service discovery via lib-state
            // =====================================================================
            Console.WriteLine($"🔗 Configuring Mesh to use lib-state for Redis access");

            var meshConfig = new MeshServiceConfiguration
            {
                UseLocalRouting = false  // MUST use real Redis via lib-state
            };
            serviceCollection.AddSingleton(meshConfig);

            // Register MeshStateManager (uses IStateStoreFactory for Redis)
            serviceCollection.AddSingleton<IMeshStateManager, MeshStateManager>();

            // Register NullTelemetryProvider (http-tester doesn't need telemetry)
            serviceCollection.AddSingleton<ITelemetryProvider, NullTelemetryProvider>();

            // Register MeshInvocationClient using factory pattern (same as MeshServicePlugin)
            serviceCollection.AddSingleton<IMeshInvocationClient>(sp =>
            {
                var stateManager = sp.GetRequiredService<IMeshStateManager>();
                var stateStoreFactory = sp.GetRequiredService<IStateStoreFactory>();
                var messageBus = sp.GetRequiredService<IMessageBus>();
                var messageSubscriber = sp.GetRequiredService<IMessageSubscriber>();
                var config = sp.GetRequiredService<MeshServiceConfiguration>();
                var logger = sp.GetRequiredService<ILogger<MeshInvocationClient>>();
                var telemetryProvider = sp.GetRequiredService<ITelemetryProvider>();
                return new MeshInvocationClient(stateManager, stateStoreFactory, messageBus, messageSubscriber, config, logger, telemetryProvider);
            });

            // Add Bannou service client infrastructure (IServiceAppMappingResolver, IEventConsumer)
            Console.WriteLine("🔗 Registering Bannou service client infrastructure...");
            serviceCollection.AddBannouServiceClients();

            // Auto-register all clients from bannou-service assembly
            // All generated clients are in bannou-service/Generated/Clients/
            // Note: TestingTestHandler uses direct HTTP calls, not a generated client
            Console.WriteLine("🔗 Auto-registering all service clients...");
            var bannouServiceAssembly = typeof(BeyondImmersion.BannouService.Auth.AuthClient).Assembly;
            serviceCollection.AddAllBannouServiceClients(new[] { bannouServiceAssembly });

            // Build the service provider
            Console.WriteLine("🔗 Building service provider...");
            ServiceProvider = serviceCollection.BuildServiceProvider();
            Console.WriteLine("🔗 Service provider built successfully.");

            // Initialize state store factory first (required by mesh)
            Console.WriteLine("🔗 Resolving IStateStoreFactory...");
            var stateStoreFactory = ServiceProvider.GetRequiredService<IStateStoreFactory>();
            Console.WriteLine("🔗 IStateStoreFactory resolved.");
            if (!await WaitForStateReadiness(stateStoreFactory))
            {
                Console.WriteLine("❌ State store factory initialization failed.");
                return false;
            }

            // Initialize mesh state manager (required for service discovery)
            var meshStateManager = ServiceProvider.GetRequiredService<IMeshStateManager>();
            if (!await WaitForMeshReadiness(meshStateManager))
            {
                Console.WriteLine("❌ Mesh state manager initialization failed.");
                return false;
            }

            // Wait for bannou service to be healthy before proceeding
            if (!await WaitForServiceHealthAsync())
            {
                Console.WriteLine("❌ Failed to wait for bannou service health");
                return false;
            }

            // Wait for MassTransit to connect to RabbitMQ
            var messageBus = ServiceProvider.GetRequiredService<IMessageBus>();
            if (!await WaitForMessagingReadiness(messageBus))
            {
                Console.WriteLine("❌ Messaging readiness check failed.");
                return false;
            }

            Console.WriteLine("✅ Service provider setup completed successfully");
            Console.WriteLine("✅ RabbitMQ connectivity verified");

            // Wait for services to register their permissions (signals service readiness)
            // This is more reliable than fixed delays since services only register after startup complete
            var permissionClient = ServiceProvider.GetRequiredService<BeyondImmersion.BannouService.Permission.IPermissionClient>();
            if (!await WaitForServiceReadiness(permissionClient))
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
    /// Waits for the bannou service to be healthy before proceeding with testing.
    /// Uses HTTP health endpoint instead of mesh.
    /// </summary>
    /// <returns>True if service is healthy, false if timeout or error occurs.</returns>
    private static async Task<bool> WaitForServiceHealthAsync()
    {
        var timeout = TimeSpan.FromSeconds(60);
        var checkInterval = TimeSpan.FromSeconds(1);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        // IMPLEMENTATION TENETS exception: http-tester doesn't have access to service configuration
        var serviceUrl = Environment.GetEnvironmentVariable(AppConstants.ENV_BANNOU_HTTP_ENDPOINT)
            ?? "http://localhost:5012";
        var healthUrl = $"{serviceUrl}/health";

        Console.WriteLine($"Waiting for bannou service to be healthy (timeout: {timeout.TotalSeconds}s)...");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var response = await httpClient.GetAsync(healthUrl);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✅ Bannou service is healthy at {serviceUrl}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                if (elapsed < 5 || (int)elapsed % 5 == 0)
                {
                    Console.WriteLine($"⏳ Service health check failed, retrying... ({elapsed:F1}s elapsed) - {ex.Message}");
                }
            }

            await Task.Delay(checkInterval);
        }

        Console.WriteLine($"❌ Service health check timed out after {timeout.TotalSeconds}s.");
        return false;
    }

    /// <summary>
    /// Waits for the state store factory to initialize.
    /// This is required for mesh state management to work.
    /// </summary>
    /// <param name="stateStoreFactory">The state store factory to initialize.</param>
    /// <returns>True if state is ready, false if timeout or error occurs.</returns>
    private static async Task<bool> WaitForStateReadiness(IStateStoreFactory stateStoreFactory)
    {
        Console.WriteLine("Waiting for State store factory initialization...");

        try
        {
            await stateStoreFactory.InitializeAsync();
            Console.WriteLine("✅ State store factory initialized");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ State store factory initialization failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Waits for the mesh state manager to initialize.
    /// This is required for service discovery to work.
    /// </summary>
    /// <param name="meshStateManager">The mesh state manager to initialize.</param>
    /// <returns>True if mesh is ready, false if timeout or error occurs.</returns>
    private static async Task<bool> WaitForMeshReadiness(IMeshStateManager meshStateManager)
    {
        Console.WriteLine("Waiting for Mesh state manager initialization...");

        try
        {
            var initialized = await meshStateManager.InitializeAsync();
            if (initialized)
            {
                Console.WriteLine("✅ Mesh state manager initialized");
                return true;
            }
            else
            {
                Console.WriteLine("❌ Mesh state manager initialization returned false");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Mesh state manager initialization failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Waits for RabbitMQ to be connected.
    /// This ensures event-driven tests will work properly.
    /// </summary>
    /// <param name="messageBus">The message bus to use for connectivity checks.</param>
    /// <returns>True if messaging is ready, false if timeout or error occurs.</returns>
    private static async Task<bool> WaitForMessagingReadiness(IMessageBus messageBus)
    {
        var timeout = TimeSpan.FromSeconds(60);
        var checkInterval = TimeSpan.FromSeconds(2);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Console.WriteLine($"Waiting for RabbitMQ to be ready (timeout: {timeout.TotalSeconds}s)...");

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                // Try to publish a test message to verify RabbitMQ connectivity
                await messageBus.TryPublishAsync("test-connectivity", new ConnectivityCheckMessage("connectivity-check", DateTime.UtcNow));
                Console.WriteLine($"✅ RabbitMQ is ready and connected");
                return true;
            }
            catch (Exception ex)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                if (elapsed < 5 || (int)elapsed % 5 == 0)
                {
                    Console.WriteLine($"⏳ Messaging not ready, retrying... ({elapsed:F1}s elapsed) - {ex.Message}");
                }
            }

            await Task.Delay(checkInterval);
        }

        Console.WriteLine($"❌ Messaging readiness check timed out after {timeout.TotalSeconds}s.");
        return false;
    }

    /// <summary>
    /// Waits for services to register their permissions with the Permission service.
    /// Services only register after fully initializing, so this is a reliable readiness signal.
    /// </summary>
    /// <param name="permissionClient">The Permission client to use for checking registered services.</param>
    /// <returns>True if expected services are ready, false if timeout occurs.</returns>
    private static async Task<bool> WaitForServiceReadiness(BeyondImmersion.BannouService.Permission.IPermissionClient permissionClient)
    {
        var timeout = TimeSpan.FromSeconds(90);
        var checkInterval = TimeSpan.FromSeconds(2);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Core services we expect to be registered before testing
        // These are the services that provide critical functionality
        var expectedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "auth",
            "account",
            "documentation",
            "permission",
            "subscription",  // Required for auth service login flow
            "game-service"    // Required by subscription service
        };

        Console.WriteLine($"Waiting for service registration (timeout: {timeout.TotalSeconds}s)...");
        Console.WriteLine($"Expected services: {string.Join(", ", expectedServices)}");

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var response = await permissionClient.GetRegisteredServicesAsync(new ListServicesRequest());
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
                    using var mockTestClient = new MockTestClient();
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
            new AchievementTestHandler(),
            new ActorTestHandler(),
            new AnalyticsTestHandler(),
            new AssetTestHandler(),
            new AuthTestHandler(),
            new CharacterTestHandler(),
            new ConnectTestHandler(),
            new ContractTestHandler(),
            new PeerRoutingTestHandler(),
            new DocumentationTestHandler(),
            new GameSessionTestHandler(),
            new LeaderboardTestHandler(),
            new LocationTestHandler(),
            new MeshTestHandler(),
            new MessagingTestHandler(),
            new OrchestratorTestHandler(),
            new PermissionTestHandler(),
            new RealmTestHandler(),
            new RelationshipTestHandler(),
            new RepositoryBindingTestHandler(),
            new ArchiveTestHandler(),
            new RelationshipTypeTestHandler(),
            new GameServiceTestHandler(),
            new SaveLoadTestHandler(),
            new SpeciesTestHandler(),
            new StateTestHandler(),
            new SubscriptionTestHandler(),
            new TestingTestHandler(),
            new MatchmakingTestHandler(),
            new InventoryTestHandler()
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
            {
                // Use Type.Name as key to avoid collisions between handlers
                // e.g., "Leaderboard.CreateDefinition" vs "Achievement.CreateDefinition"
                var registryKey = $"{serviceTest.Type}.{serviceTest.Name}";
                sTestRegistry.Add(registryKey, serviceTest);
            }
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
                using var mockTestClient = new MockTestClient();
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
            "bannou, err:",                // mesh invoke error (service unreachable)
            "dial tcp",                    // Network connection failure to service
            "ERR_DIRECT_INVOKE",          // Bannou service invocation failure
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
