using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;

namespace BeyondImmersion.EdgeTester;

public class Program
{
    // Protocol definitions now imported from BeyondImmersion.BannouService.Connect.Protocol
    // MessageFlags, ServiceRequestItem, ServiceResponseItem, BinaryMessage are available

    // ResponseCodes enum available from Connect service protocol

    // Enhanced ServiceRequestItem and ServiceResponseItem available from Connect service protocol
    // These include additional fields like Channel and Sequence for improved functionality

    private static ClientConfiguration _configuration;
    /// <summary>
    /// Client configuration.
    /// Pull from .env files, Config.json, ENVs, and command line args using bannou-service configuration system.
    /// </summary>
    public static ClientConfiguration Configuration
    {
        get
        {
            if (_configuration != null)
                return _configuration;

            var configRoot = IServiceConfiguration.BuildConfigurationRoot(Environment.GetCommandLineArgs());
            _configuration = configRoot.Get<ClientConfiguration>() ?? new ClientConfiguration();

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

    private static List<string> ServiceNames { get; } = new List<string> { "accounts", "authorization", "connect", "leaderboards" };
    private static Dictionary<string, Guid>? ServiceLookup { get; set; }
    private static Dictionary<Guid, string>? ServiceReverseLookup { get; set; }

    /// <summary>
    /// Token source for initiating a clean shutdown.
    /// </summary>
    public static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();

    /// <summary>
    /// Lookup for all service tests.
    /// </summary>
    private static readonly Dictionary<string, Action<string[]>> sTestRegistry = new();

    // tokens generated from login
    private static string? sAccessToken = null;
    private static string? sRefreshToken = null;

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
            if (Configuration == null || !Configuration.HasRequired())
                throw new InvalidOperationException("Required client configuration missing.");

            Console.WriteLine("Attempting to log in.");

            JObject? accessTokens = await LoginWithCredentials();
            if (accessTokens == null)
            {
                accessTokens = await RegisterWithCredentials();
                if (accessTokens == null)
                    throw new InvalidOperationException("Failed to register user account.");

                Console.WriteLine("Registration successful.");
            }
            else
                Console.WriteLine("Login successful.");

            // Auth API returns camelCase property names per OpenAPI schema
            sAccessToken = (string?)accessTokens["accessToken"];
            sRefreshToken = (string?)accessTokens["refreshToken"];
            if (sAccessToken == null)
                throw new InvalidOperationException("Failed to parse JWT from login result.");

            Console.WriteLine("Parsing access token and refresh token from login result successful.");

            Console.WriteLine("🧪 Enhanced WebSocket Protocol Testing - Binary Protocol Validation");
            Console.WriteLine($"Base URL: ws://{Configuration.Connect_Endpoint}");
            Console.WriteLine();

            bool testPassed = await EstablishWebsocketAndSendMessage();

            if (testPassed)
            {
                Console.WriteLine("✅ WebSocket protocol test completed successfully!");

                if (!IsDaemonMode(args))
                {
                    Console.WriteLine("\n🎮 Entering interactive test console...");
                    GiveControlToTestConsole();
                }
            }
            else
            {
                if (IsDaemonMode(args))
                {
                    Environment.Exit(1); // CI failure exit code
                }
                throw new Exception("WebSocket protocol test failed.");
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

    private static async Task<JObject?> RegisterWithCredentials()
    {
        var serverUri = new Uri($"http://{Configuration.Register_Endpoint}");
        Console.WriteLine($"Registration Uri: {serverUri}");

        var contentObj = new JObject()
        {
            ["username"] = Configuration.Client_Username,
            ["password"] = Configuration.Client_Password
        };
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, serverUri);
        var jsonContentStr = JsonConvert.SerializeObject(contentObj);
        var strContent = new StringContent(jsonContentStr, Encoding.UTF8, "application/json");
        httpRequest.Content = strContent;

        using var response = await HttpClient.SendAsync(httpRequest, ShutdownCancellationTokenSource.Token);
        if (response == null)
        {
            Console.WriteLine($"No server response received");
            return null;
        }

        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"Server responded with: {response.StatusCode}, Reason: {response.ReasonPhrase}");
            return null;
        }

        var responseStr = await response.Content.ReadAsStringAsync();
        var responseObj = JObject.Parse(responseStr);

        return responseObj;
    }

    private static async Task<JObject?> LoginWithCredentials()
    {
        var serverUri = new Uri($"http://{Configuration.Login_Credentials_Endpoint}");
        Console.WriteLine($"Credential login Uri: {serverUri}");

        // Auth API expects JSON body with email/password per auth-api.yaml
        var contentObj = new JObject()
        {
            ["email"] = Configuration.Client_Username, // Using username as email for testing
            ["password"] = Configuration.Client_Password
        };
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, serverUri);
        var jsonContentStr = JsonConvert.SerializeObject(contentObj);
        var strContent = new StringContent(jsonContentStr, Encoding.UTF8, "application/json");
        httpRequest.Content = strContent;

        using var response = await HttpClient.SendAsync(httpRequest, ShutdownCancellationTokenSource.Token);
        if (response == null)
        {
            Console.WriteLine($"No server response received");
            return null;
        }

        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"Server responded with: {response.StatusCode}, Reason: {response.ReasonPhrase}");
            return null;
        }

        var responseStr = await response.Content.ReadAsStringAsync();
        var responseObj = JObject.Parse(responseStr);

        return responseObj;
    }

    private static async Task<JObject?> LoginWithRefreshToken()
    {
        var serverUri = new Uri($"http://{Configuration.Login_Token_Endpoint}");
        Console.WriteLine($"Token login Uri: {serverUri}");

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, serverUri);
        httpRequest.Headers.Add("token", sRefreshToken);
        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await HttpClient.SendAsync(httpRequest, ShutdownCancellationTokenSource.Token);
        if (response == null)
        {
            Console.WriteLine($"No server response received");
            return null;
        }

        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.WriteLine($"Server responded with: {response.StatusCode}, Reason: {response.ReasonPhrase}");
            return null;
        }

        var responseStr = await response.Content.ReadAsStringAsync();
        var responseObj = JObject.Parse(responseStr);

        return responseObj;
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

        // load login tests
        var loginTestHandler = new LoginTestHandler();
        foreach (ServiceTest serviceTest in loginTestHandler.GetServiceTests())
            sTestRegistry.Add(serviceTest.Name, serviceTest.Target);

        // load template tests
        var templateTestHandler = new TemplateTestHandler();
        foreach (ServiceTest serviceTest in templateTestHandler.GetServiceTests())
            sTestRegistry.Add(serviceTest.Name, serviceTest.Target);

        // load connect websocket tests
        var connectTestHandler = new ConnectWebSocketTestHandler();
        foreach (ServiceTest serviceTest in connectTestHandler.GetServiceTests())
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
}
