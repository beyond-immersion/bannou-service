using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.EdgeTester.Application;
using System.Net.WebSockets;
using System.Text;

namespace BeyondImmersion.EdgeTester;

/// <summary>
/// Edge tester main program. Uses BannouClient SDK for authentication and WebSocket connection,
/// then runs comprehensive protocol tests.
/// </summary>
public class Program
{
    private static ClientConfiguration? _configuration;
    /// <summary>
    /// Client configuration.
    /// Pull from .env files, ENVs, and command line args.
    /// </summary>
    public static ClientConfiguration Configuration
    {
        get
        {
            if (_configuration != null)
                return _configuration;

            _configuration = ConfigurationHelper.BuildClientConfiguration(Environment.GetCommandLineArgs());
            return _configuration;
        }

        internal set => _configuration = value;
    }

    private static HttpClient _httpClient;
    /// <summary>
    /// HTTP Client for making performing setup for tests.
    /// </summary>
    public static HttpClient HttpClient
    {
        get
        {
            if (_httpClient != null)
                return _httpClient;

            // allow "insecure" (curl -k) SSL validation, for testing
            _httpClient = new(new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

            return _httpClient;
        }
    }

    /// <summary>
    /// Token source for initiating a clean shutdown.
    /// </summary>
    public static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();

    /// <summary>
    /// Lookup for all service tests.
    /// </summary>
    private static readonly Dictionary<string, Action<string[]>> sTestRegistry = new();

    // BannouClient instances for SDK-based connections
    private static BannouClient? _client;
    private static BannouClient? _adminClient;

    /// <summary>
    /// Gets the BannouClient for regular user operations.
    /// </summary>
    public static BannouClient? Client => _client;

    /// <summary>
    /// Gets the BannouClient for admin operations.
    /// </summary>
    public static BannouClient? AdminClient => _adminClient;

    // Legacy accessor for EstablishWebsocketAndSendMessage - tests should use Client.AccessToken
    private static string? sAccessToken => _client?.AccessToken;

    /// <summary>
    /// Gets the admin access token (for orchestrator API tests).
    /// Returns null if admin login hasn't been performed.
    /// </summary>
    public static string? AdminAccessToken => _adminClient?.AccessToken;

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

    /// <summary>
    /// Waits for OpenResty gateway and backend services to become ready.
    /// Polls the health endpoint with exponential backoff, then verifies
    /// that Dapr-dependent services are also ready by making a warmup call.
    /// </summary>
    /// <returns>True if services are ready, false if timeout exceeded.</returns>
    private static async Task<bool> WaitForServiceReadiness()
    {
        const int maxWaitSeconds = 120;
        const int initialDelayMs = 500;
        const int maxDelayMs = 5000;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var delayMs = initialDelayMs;

        // Construct health check URL from OpenResty configuration
        var openrestyHost = Configuration.OpenResty_Host ?? "openresty";
        var openrestyPort = Configuration.OpenResty_Port ?? 80;
        var healthUrl = $"http://{openrestyHost}:{openrestyPort}/health";
        var authHealthUrl = $"http://{openrestyHost}:{openrestyPort}/auth/health";

        Console.WriteLine($"⏳ Waiting for services to become ready...");
        Console.WriteLine($"   OpenResty health URL: {healthUrl}");
        Console.WriteLine($"   Auth service health URL: {authHealthUrl}");

        // Phase 1: Wait for OpenResty and basic bannou service
        while (stopwatch.Elapsed.TotalSeconds < maxWaitSeconds)
        {
            try
            {
                // First check OpenResty itself
                var openrestyResponse = await HttpClient.GetAsync(healthUrl);
                if (!openrestyResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"⏳ OpenResty not ready ({openrestyResponse.StatusCode}), retrying in {delayMs}ms...");
                    await Task.Delay(delayMs);
                    delayMs = Math.Min(delayMs * 2, maxDelayMs);
                    continue;
                }

                // Then check that auth service is reachable through OpenResty
                var authResponse = await HttpClient.GetAsync(authHealthUrl);
                if (authResponse.IsSuccessStatusCode || authResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // NotFound is OK - it means OpenResty reached bannou but the health endpoint doesn't exist
                    // That's fine - the service is available
                    Console.WriteLine($"✅ Basic services ready after {stopwatch.Elapsed.TotalSeconds:F1}s");
                    break;
                }

                if (authResponse.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                    authResponse.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    Console.WriteLine($"⏳ Backend service not ready ({authResponse.StatusCode}), retrying in {delayMs}ms...");
                }
                else
                {
                    Console.WriteLine($"⏳ Unexpected response ({authResponse.StatusCode}), retrying in {delayMs}ms...");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"⏳ Connection failed ({ex.Message}), retrying in {delayMs}ms...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⏳ Error ({ex.GetType().Name}: {ex.Message}), retrying in {delayMs}ms...");
            }

            await Task.Delay(delayMs);
            delayMs = Math.Min(delayMs * 2, maxDelayMs);
        }

        if (stopwatch.Elapsed.TotalSeconds >= maxWaitSeconds)
        {
            Console.WriteLine($"❌ Services failed to become ready within {maxWaitSeconds}s");
            return false;
        }

        // Phase 2: Wait for Dapr sidecar to be ready by making a warmup registration attempt
        // The registration endpoint calls AccountsClient through Dapr, so this actually exercises
        // the full Dapr dependency chain. We expect 200 (created) or 409 (conflict) when working,
        // but 500/502/503 when Dapr isn't ready yet. Login with empty credentials doesn't work
        // because validation fails before Dapr is touched.
        Console.WriteLine($"⏳ Verifying Dapr sidecar readiness...");
        var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
        delayMs = initialDelayMs;

        while (stopwatch.Elapsed.TotalSeconds < maxWaitSeconds)
        {
            try
            {
                // Make a registration request with a unique warmup username - this will exercise Dapr
                var warmupUsername = $"warmup_{Guid.NewGuid():N}@test.local";
                var warmupContent = new StringContent(
                    $"{{\"username\":\"{warmupUsername}\",\"password\":\"warmup-password-123\"}}",
                    Encoding.UTF8,
                    "application/json");
                var warmupResponse = await HttpClient.PostAsync(registerUrl, warmupContent);

                // 500/502/503 typically indicates Dapr sidecar isn't ready
                if (warmupResponse.StatusCode == System.Net.HttpStatusCode.InternalServerError ||
                    warmupResponse.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                    warmupResponse.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    Console.WriteLine($"⏳ Dapr sidecar not ready ({warmupResponse.StatusCode}), retrying in {delayMs}ms...");
                    await Task.Delay(delayMs);
                    delayMs = Math.Min(delayMs * 2, maxDelayMs);
                    continue;
                }

                // 200 OK or 409 Conflict means the service chain is fully working
                Console.WriteLine($"✅ Dapr sidecar ready after {stopwatch.Elapsed.TotalSeconds:F1}s (warmup returned {warmupResponse.StatusCode})");

                // Wait additional time for services to register their permissions with Permissions service
                // Permission registration happens AFTER Dapr readiness check in bannou startup
                Console.WriteLine("⏳ Waiting for service permission registration to complete...");
                await Task.Delay(5000); // 5 seconds for permission events to be published and processed
                Console.WriteLine("✅ Permission registration delay complete");

                return true;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"⏳ Warmup connection failed ({ex.Message}), retrying in {delayMs}ms...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⏳ Warmup error ({ex.GetType().Name}: {ex.Message}), retrying in {delayMs}ms...");
            }

            await Task.Delay(delayMs);
            delayMs = Math.Min(delayMs * 2, maxDelayMs);
        }

        Console.WriteLine($"❌ Dapr sidecar failed to become ready within {maxWaitSeconds}s");
        return false;
    }

    internal static async Task Main(string[] args)
    {
        try
        {
            // configuration is auto-created on first get, so this call creates the config too
            if (Configuration == null || !Configuration.HasRequired())
                throw new InvalidOperationException("Required client configuration missing.");

            // Wait for OpenResty gateway and backend services to be ready
            if (!await WaitForServiceReadiness())
            {
                throw new InvalidOperationException("Services failed to become ready within timeout.");
            }

            Console.WriteLine("Attempting to authenticate using BannouClient SDK...");

            // Build server URL from configuration
            var openrestyHost = Configuration.OpenResty_Host ?? "openresty";
            var openrestyPort = Configuration.OpenResty_Port ?? 80;
            var serverUrl = $"http://{openrestyHost}:{openrestyPort}";

            // Use BannouClient for authentication and WebSocket connection
            _client = new BannouClient(HttpClient);

            // Try login first, then registration
            var email = Configuration.Client_Username ?? throw new InvalidOperationException("Client_Username is required");
            var password = Configuration.Client_Password ?? throw new InvalidOperationException("Client_Password is required");
            var username = email.Contains('@') ? email.Split('@')[0] : email;

            var connected = await _client.ConnectAsync(serverUrl, email, password);
            if (!connected)
            {
                Console.WriteLine($"Login failed: {_client.LastError}");
                Console.WriteLine("Attempting registration...");
                connected = await _client.RegisterAndConnectAsync(serverUrl, username, email, password);
                if (!connected)
                {
                    Console.WriteLine($"Registration failed: {_client.LastError}");
                    throw new InvalidOperationException($"Failed to register and connect using BannouClient: {_client.LastError}");
                }

                Console.WriteLine("✅ Registration and connection successful via BannouClient.");
            }
            else
            {
                Console.WriteLine("✅ Login and connection successful via BannouClient.");
            }

            Console.WriteLine($"   Session ID: {_client.SessionId}");
            Console.WriteLine($"   Available APIs: {_client.AvailableApis.Count}");

            // Also authenticate with admin credentials for orchestrator API tests
            Console.WriteLine("\n=== Admin Authentication ===");
            bool adminAuthenticated = await EnsureAdminAuthenticated();
            if (!adminAuthenticated)
            {
                Console.WriteLine("⚠️ Admin authentication failed - orchestrator tests may fail.");
            }
            Console.WriteLine();

            Console.WriteLine("🧪 Enhanced WebSocket Protocol Testing - Binary Protocol Validation");
            Console.WriteLine($"Base URL: ws://{Configuration.Connect_Endpoint}");
            Console.WriteLine();

            // Run initial connectivity test
            bool connectivityPassed = await EstablishWebsocketAndSendMessage();

            if (!connectivityPassed)
            {
                if (IsDaemonMode(args))
                {
                    Console.WriteLine("❌ Basic WebSocket connectivity test FAILED");
                    Environment.Exit(1); // CI failure exit code
                }
                throw new Exception("WebSocket protocol test failed.");
            }

            Console.WriteLine("✅ Basic WebSocket connectivity test PASSED");
            Console.WriteLine();

            // Load all test handlers
            LoadClientTests();

            if (IsDaemonMode(args))
            {
                // In daemon mode, run all tests automatically and report results
                Console.WriteLine("🔄 Running full edge test suite in daemon mode...");
                Console.WriteLine($"   Total tests registered: {sTestRegistry.Count - 1}"); // -1 for "All"
                Console.WriteLine();

                var failedTests = new List<string>();
                var passedTests = new List<string>();

                foreach (var kvp in sTestRegistry)
                {
                    if (kvp.Key == "All")
                        continue;

                    Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    Console.WriteLine($"📋 Running: {kvp.Key}");
                    Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                    try
                    {
                        // Capture console output to detect pass/fail
                        var originalOut = Console.Out;
                        using var stringWriter = new StringWriter();
                        Console.SetOut(new DualWriter(originalOut, stringWriter));

                        kvp.Value?.Invoke(Array.Empty<string>());

                        Console.SetOut(originalOut);
                        var output = stringWriter.ToString();

                        // Check for failure indicators in output
                        if (output.Contains("❌") || output.Contains("FAILED"))
                        {
                            failedTests.Add(kvp.Key);
                            Console.WriteLine($"❌ Test {kvp.Key} FAILED");
                        }
                        else if (output.Contains("✅") || output.Contains("PASSED"))
                        {
                            passedTests.Add(kvp.Key);
                            Console.WriteLine($"✅ Test {kvp.Key} PASSED");
                        }
                        else
                        {
                            // Ambiguous result - treat as warning but not failure
                            passedTests.Add(kvp.Key);
                            Console.WriteLine($"⚠️ Test {kvp.Key} completed (result unclear)");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedTests.Add(kvp.Key);
                        Console.WriteLine($"❌ Test {kvp.Key} FAILED with exception: {ex.Message}");
                    }

                    Console.WriteLine();
                }

                // Print summary
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("📊 TEST SUMMARY");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"   Total:  {passedTests.Count + failedTests.Count}");
                Console.WriteLine($"   Passed: {passedTests.Count}");
                Console.WriteLine($"   Failed: {failedTests.Count}");

                if (failedTests.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("❌ Failed tests:");
                    foreach (var test in failedTests)
                    {
                        Console.WriteLine($"   - {test}");
                    }
                    Environment.Exit(1);
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("✅ All edge tests passed!");
                }
            }
            else
            {
                Console.WriteLine("\n🎮 Entering interactive test console...");
                GiveControlToTestConsole();
            }
        }
        catch (Exception exc)
        {
            ShutdownCancellationTokenSource.Cancel();
            Console.WriteLine($"❌ An exception has occurred: '{exc.Message}'");
            Console.WriteLine($"Stack trace: {exc.StackTrace}");

            if (IsDaemonMode(args))
            {
                Environment.Exit(1); // CI failure exit code
            }

            WaitForUserInput("Press any key to exit...", args);
        }
    }

    /// <summary>
    /// Ensures admin user exists and is logged in. Stores admin tokens for orchestrator API tests.
    /// Uses BannouClient SDK for authentication.
    /// </summary>
    private static async Task<bool> EnsureAdminAuthenticated()
    {
        Console.WriteLine("Attempting to authenticate admin using BannouClient SDK...");

        var openrestyHost = Configuration.OpenResty_Host ?? "openresty";
        var openrestyPort = Configuration.OpenResty_Port ?? 80;
        var serverUrl = $"http://{openrestyHost}:{openrestyPort}";

        _adminClient = new BannouClient(HttpClient);

        var adminEmail = Configuration.GetAdminUsername();
        var adminPassword = Configuration.GetAdminPassword();

        if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
        {
            Console.WriteLine("⚠️ Admin credentials not configured - orchestrator tests will be skipped.");
            return false;
        }

        var adminUsername = adminEmail.Contains('@') ? adminEmail.Split('@')[0] : adminEmail;

        var connected = await _adminClient.ConnectAsync(serverUrl, adminEmail, adminPassword);
        if (!connected)
        {
            Console.WriteLine("Admin login failed, attempting registration...");
            connected = await _adminClient.RegisterAndConnectAsync(serverUrl, adminUsername, adminEmail, adminPassword);
            if (!connected)
            {
                Console.WriteLine("⚠️ Failed to create admin account - orchestrator tests will be skipped.");
                _adminClient = null;
                return false;
            }

            Console.WriteLine("✅ Admin registration and connection successful via BannouClient.");
        }
        else
        {
            Console.WriteLine("✅ Admin login and connection successful via BannouClient.");
        }

        Console.WriteLine($"   Admin Session ID: {_adminClient.SessionId}");
        Console.WriteLine($"   Admin Available APIs: {_adminClient.AvailableApis.Count}");
        Console.WriteLine($"✅ Admin authenticated as: {adminEmail}");
        return true;
    }

    private static async Task<bool> EstablishWebsocketAndSendMessage()
    {
        var serverUri = new Uri($"ws://{Configuration.Connect_Endpoint}");

        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + sAccessToken);

        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Console.WriteLine("✅ Connected to the server");

            // Create a test message using our enhanced binary protocol
            var testPayload = Encoding.UTF8.GetBytes("{ \"test\": \"Enhanced WebSocket protocol validation\" }");

            var binaryMessage = new BinaryMessage(
                flags: MessageFlags.None,
                channel: 1,
                sequenceNumber: 1,
                serviceGuid: Guid.NewGuid(), // This would normally be a client-salted GUID
                messageId: GuidGenerator.GenerateMessageId(),
                payload: testPayload
            );

            // Serialize the binary message using our protocol
            var messageBytes = binaryMessage.ToByteArray();
            var buffer = new ArraySegment<byte>(messageBytes);

            Console.WriteLine($"📤 Sending binary message (31-byte header + {testPayload.Length} bytes payload)");
            Console.WriteLine($"   Flags: {binaryMessage.Flags}");
            Console.WriteLine($"   Channel: {binaryMessage.Channel}");
            Console.WriteLine($"   SequenceNumber: {binaryMessage.SequenceNumber}");
            Console.WriteLine($"   ServiceGuid: {binaryMessage.ServiceGuid}");
            Console.WriteLine($"   MessageId: {binaryMessage.MessageId}");

            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);
            Console.WriteLine("✅ Binary protocol message sent successfully");

            // Receive response using binary protocol
            var receivedBuffer = new ArraySegment<byte>(new byte[4096]); // Larger buffer for binary protocol
            var result = await webSocket.ReceiveAsync(receivedBuffer, CancellationToken.None);

            if (receivedBuffer.Array == null)
                throw new NullReferenceException();

            Console.WriteLine($"📥 Received {result.Count} bytes");

            // Try to parse as binary protocol message
            try
            {
                var bufferArray = receivedBuffer.Array ?? throw new InvalidOperationException("Received buffer array is null");
                var receivedMessage = BinaryMessage.Parse(bufferArray, result.Count);
                Console.WriteLine("✅ Successfully parsed binary protocol response:");
                Console.WriteLine($"   Flags: {receivedMessage.Flags}");
                Console.WriteLine($"   Channel: {receivedMessage.Channel}");
                Console.WriteLine($"   SequenceNumber: {receivedMessage.SequenceNumber}");
                Console.WriteLine($"   ServiceGuid: {receivedMessage.ServiceGuid}");
                Console.WriteLine($"   MessageId: {receivedMessage.MessageId}");

                if (receivedMessage.Payload.Length > 0)
                {
                    var responsePayload = Encoding.UTF8.GetString(receivedMessage.Payload.Span);
                    Console.WriteLine($"   Payload: {responsePayload}");
                }
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"⚠️ Could not parse as binary protocol: {parseEx.Message}");
                Console.WriteLine($"   Raw received data: {Convert.ToHexString(receivedBuffer.Array, 0, result.Count)}");
            }

            return true;
        }
        catch (WebSocketException wse)
        {
            Console.WriteLine($"❌ WebSocket exception: {wse.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }

        return false;
    }

    private static void GiveControlToTestConsole()
    {
        LoadClientTests();

        string? line;
        if (sTestRegistry.Count == 0)
        {
            Console.WriteLine("No tests to run- press any key to exit.");
            _ = Console.ReadKey();
            return;
        }

        string command;
        List<string> commandArgs;
        do
        {
            Console.WriteLine("Select a test to run from the following list (press CTRL+C to exit):");
            var i = 0;
            foreach (KeyValuePair<string, Action<string[]>> kvp in sTestRegistry)
                Console.WriteLine($"{++i}. {kvp.Key}");

            Console.WriteLine();
            line = Console.ReadLine();
            if (line != null && !string.IsNullOrWhiteSpace(line))
            {
                commandArgs = line.Split(' ').ToList();
                command = commandArgs[0];
                commandArgs.RemoveAt(0);

                if (int.TryParse(command, out var commandIndex) && commandIndex >= 1 && commandIndex <= sTestRegistry.Count)
                    command = sTestRegistry.Keys.ElementAt(commandIndex - 1);

                if (sTestRegistry.TryGetValue(command, out Action<string[]>? testTarget))
                {
                    Console.Clear();
                    testTarget?.Invoke(commandArgs.ToArray());
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

    private static void LoadClientTests()
    {
        sTestRegistry.Add("All", RunEntireTestSuite);

        // load auth/login tests
        var loginTestHandler = new LoginTestHandler();
        foreach (ServiceTest serviceTest in loginTestHandler.GetServiceTests())
            sTestRegistry.Add(serviceTest.Name, serviceTest.Target);

        // load connect websocket tests
        var connectTestHandler = new ConnectWebSocketTestHandler();
        foreach (ServiceTest serviceTest in connectTestHandler.GetServiceTests())
            sTestRegistry.Add(serviceTest.Name, serviceTest.Target);

        // load capability flow tests (permission discovery and GUID routing)
        var capabilityTestHandler = new Tests.CapabilityFlowTestHandler();
        foreach (ServiceTest serviceTest in capabilityTestHandler.GetServiceTests())
            sTestRegistry.Add(serviceTest.Name, serviceTest.Target);

        // load orchestrator websocket tests
        var orchestratorTestHandler = new OrchestratorWebSocketTestHandler();
        foreach (ServiceTest serviceTest in orchestratorTestHandler.GetServiceTests())
            sTestRegistry.Add(serviceTest.Name, serviceTest.Target);
    }

    private static void RunEntireTestSuite(string[] args)
    {
        foreach (KeyValuePair<string, Action<string[]>> kvp in sTestRegistry)
        {
            if (kvp.Key == "All")
                continue;

            kvp.Value?.Invoke(args);
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Will initiate a client shutdown.
    /// </summary>
    public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();

    /// <summary>
    /// TextWriter that writes to both console and a StringWriter for test result detection.
    /// </summary>
    private class DualWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly TextWriter _capture;

        public DualWriter(TextWriter console, TextWriter capture)
        {
            _console = console;
            _capture = capture;
        }

        public override Encoding Encoding => _console.Encoding;

        public override void Write(char value)
        {
            _console.Write(value);
            _capture.Write(value);
        }

        public override void Write(string? value)
        {
            _console.Write(value);
            _capture.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _console.WriteLine(value);
            _capture.WriteLine(value);
        }

        public override void WriteLine()
        {
            _console.WriteLine();
            _capture.WriteLine();
        }
    }
}
