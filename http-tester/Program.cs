using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Configuration;

namespace BeyondImmersion.BannouService.HttpTester;

public class Program
{
    private static TestConfiguration _configuration = null!;
    /// <summary>
    /// Client configuration.
    /// Pull from .env files, Config.json, ENVs, and command line args using bannou-service configuration system.
    /// </summary>
    public static TestConfiguration Configuration
    {
        get
        {
            if (_configuration != null)
                return _configuration;

            var configRoot = IServiceConfiguration.BuildConfigurationRoot(Environment.GetCommandLineArgs());
            _configuration = configRoot.Get<TestConfiguration>() ?? new TestConfiguration();

            return _configuration;
        }

        internal set => _configuration = value;
    }

    /// <summary>
    /// Token source for initiating a clean shutdown.
    /// </summary>
    public static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();

    /// <summary>
    /// Lookup for all service tests.
    /// </summary>
    private static readonly Dictionary<string, ServiceTest> sTestRegistry = new();

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

    internal static async Task Main(string[] args)
    {
        try
        {
            // configuration is auto-created on first get, so this call creates the config too
            if (Configuration == null || !Configuration.HasHttpRequired())
                throw new InvalidOperationException("Required HTTP testing configuration missing.");

            Console.WriteLine("HTTP Service Tester - Direct endpoint testing");
            Console.WriteLine($"Base URL: {Configuration.Http_Base_Url}");
            Console.WriteLine();

            Console.WriteLine("Testing service-to-service communication via NSwag-generated clients...");

            // Start the interactive test console - no client abstraction needed
            await RunInteractiveTestConsole(args);
        }
        catch (Exception exc)
        {
            ShutdownCancellationTokenSource.Cancel();
            Console.WriteLine($"An exception has occurred: '{exc.Message}'");
            Console.WriteLine($"Stack trace: {exc.StackTrace}");

            WaitForUserInput("Press any key to exit...", args);
        }
    }

    /// <summary>
    /// Runs the test console that allows users to select and execute tests, or runs all tests in daemon mode
    /// </summary>
    private static async Task RunInteractiveTestConsole(string[] args)
    {
        LoadServiceTests();

        if (sTestRegistry.Count == 0)
        {
            WaitForUserInput("No tests to run - press any key to exit.", args);
            return;
        }

        // In daemon mode, run all tests automatically
        if (IsDaemonMode(args))
        {
            Console.WriteLine("Running in daemon mode - executing all tests automatically...");
            foreach (var kvp in sTestRegistry)
            {
                Console.WriteLine($"Running test: {kvp.Value.Name}");
                try
                {
                    // Tests no longer need a client parameter - they create their own generated clients
                    var result = await kvp.Value.TestAction(null!, Array.Empty<string>());
                    if (result.Success)
                    {
                        Console.WriteLine($"✅ Test {kvp.Value.Name} completed successfully: {result.Message}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ Test {kvp.Value.Name} failed: {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Test {kvp.Value.Name} failed with exception: {ex.Message}");
                }
            }
            Console.WriteLine("All tests completed in daemon mode.");
            return;
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
                        var result = await testTarget.TestAction(null!, commandArgs.ToArray());

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

        // Load HTTP-specific test handlers
        var testHandlers = new List<IServiceTestHandler>
        {
            new AccountTestHandler(),
            new AuthTestHandler(),
            new OpenRestyAuthTestHandler(),  // OpenResty-specific auth and queue system tests
            new DaprServiceMappingTestHandler()
            // Add more test handlers as needed
        };

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
                var result = await kvp.Value.TestAction(null!, args);
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
    /// Will initiate a client shutdown.
    /// </summary>
    public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();
}