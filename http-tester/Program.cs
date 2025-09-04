using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Configuration;

namespace BeyondImmersion.BannouService.HttpTester;

public class Program
{
    private static TestConfiguration _configuration = null!;
    /// <summary>
    /// Client configuration.
    /// Pull from Config.json, ENVs, and command line args.
    /// </summary>
    public static TestConfiguration Configuration
    {
        get
        {
            if (_configuration != null)
                return _configuration;

            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile("Config.json", optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(Environment.GetCommandLineArgs());
            var configRoot = configBuilder.Build();
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

    internal static async Task Main()
    {
        try
        {
            // configuration is auto-created on first get, so this call creates the config too
            if (Configuration == null || !Configuration.HasHttpRequired())
                throw new InvalidOperationException("Required HTTP testing configuration missing.");

            Console.WriteLine("HTTP Service Tester - Direct endpoint testing");
            Console.WriteLine($"Base URL: {Configuration.Http_Base_Url}");
            Console.WriteLine();

            // Create HTTP client for testing
            using var httpClient = new HttpTestClient(Configuration);

            Console.WriteLine("Attempting to authenticate...");

            // Try login first, then registration if that fails
            bool authenticated = await httpClient.LoginAsync(Configuration.Client_Username!, Configuration.Client_Password!);
            if (!authenticated)
            {
                Console.WriteLine("Login failed, attempting registration...");
                authenticated = await httpClient.RegisterAsync(Configuration.Client_Username!, Configuration.Client_Password!);

                if (!authenticated)
                    throw new InvalidOperationException("Failed to authenticate or register user account.");

                Console.WriteLine("Registration and login successful.");
            }
            else
            {
                Console.WriteLine("Login successful.");
            }

            // Start the interactive test console
            await RunInteractiveTestConsole(httpClient);
        }
        catch (Exception exc)
        {
            ShutdownCancellationTokenSource.Cancel();
            Console.WriteLine($"An exception has occurred: '{exc.Message}'");
            Console.WriteLine($"Stack trace: {exc.StackTrace}");
            Console.WriteLine("Press any key to exit...");
            _ = Console.ReadKey();
        }
    }

    /// <summary>
    /// Runs the interactive test console that allows users to select and execute tests
    /// </summary>
    /// <param name="testClient">The test client to use for executing tests</param>
    private static async Task RunInteractiveTestConsole(ITestClient testClient)
    {
        LoadServiceTests();

        string? line;
        if (sTestRegistry.Count == 0)
        {
            Console.WriteLine("No tests to run - press any key to exit.");
            _ = Console.ReadKey();
            return;
        }

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
                    Console.WriteLine($"Running test: {testTarget.Name} ({testClient.TransportType})");
                    Console.WriteLine(new string('-', 50));

                    try
                    {
                        var result = await testTarget.TestAction(testClient, commandArgs.ToArray());

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
                Console.WriteLine($"Press any key to continue...");
                _ = Console.ReadKey();
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
            new AuthTestHandler()
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
    /// <param name="testClient">The test client to use for executing tests</param>
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
                var result = await kvp.Value.TestAction(testClient, args);
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
