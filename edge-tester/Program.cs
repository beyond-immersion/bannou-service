using BeyondImmersion.BannouService.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;

namespace BeyondImmersion.ServiceTester;

public class Program
{
    /// <summary>
    /// The very first byte of any request or response message.
    /// Indicates how the message content should be treated, and
    /// how the message itself should be routed through the system.
    /// </summary>
    [Flags]
    public enum MessageFlags : byte
    {
        /// <summary>
        /// The "default message type" is a text/JSON request, being
        /// made to a service endpoint (rather than to another client),
        /// unencrypted, uncompressed, standard priority, and expecting
        /// a response from the service.
        ///
        /// With these flags, the next "ServiceID" section of the header
        /// would be referring to the Dapr service app to pass the message
        /// along to, after converting it to an HTTP request.
        ///
        /// The "MessageID" section will also be enterring the system for
        /// the first time- the same ID will need to be handed back to the
        /// client for the response, so they can associate it back to the
        /// request themselves if needed.
        /// </summary>
        None = 0,
        /// <summary>
        /// Message payload is binary data.
        /// Default/off indicates Text/JSON.
        /// </summary>
        Binary = 1 << 0,
        /// <summary>
        /// Message payload is encrypted.
        /// </summary>
        Encrypted = 1 << 1,
        /// <summary>
        /// Message payload is compressed.
        /// </summary>
        Compressed = 1 << 2,
        /// <summary>
        /// Delivery at high priority- skip to the front of queues.
        /// </summary>
        HighPriority = 1 << 3,
        /// <summary>
        /// Message is an event- fire-and-forget.
        /// Default/off indicates an RPC- expects a response.
        /// </summary>
        Event = 1 << 4,
        /// <summary>
        /// Message should be handed off to another WebSocket connection.
        /// Default/off will direct the message to a Dapr service (HTTP).
        /// </summary>
        Client = 1 << 5,
        /// <summary>
        /// Message is the response to an RPC.
        /// Default/off indicates a request (or event).
        /// </summary>
        Response = 1 << 6
    };

    /// <summary>
    ///
    /// </summary>
    public enum ResponseCodes : byte
    {
        OK = 0,

        RequestError = 10,
        RequestTooLarge,
        TooManyRequests,
        InvalidRequestChannel,

        Unauthorized = 20,

        ServiceNotFound = 30,
        ClientNotFound,
        MessageNotFound,

        Service_BadRequest = 50,
        Service_NotFound,
        Service_Unauthorized,
        Service_InternalServerError = 60
    };

    /// <summary>
    /// A request- either outgoing from the client to a service, or
    /// incoming from a service to the client (as an RPC- cool!).
    /// </summary>
    public struct ServiceRequestItem
    {
        public MessageFlags Flags { get; set; }
        public Guid MessageID { get; set; }
        public ushort MessageChannel { get; set; }
        public Guid ServiceID { get; set; }

        public byte[] Content { get; set; }
    }

    /// <summary>
    /// A response to an RPC, whether made by the client to a service,
    /// or from a service to the client, depending on where the request
    /// originated.
    /// </summary>
    public struct ServiceResponseItem
    {
        public MessageFlags Flags { get; set; }
        public Guid MessageID { get; set; }
        public ResponseCodes ResponseCode { get; set; }

        public byte[]? Content { get; set; }
    }

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

    internal static async Task Main()
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

            sAccessToken = (string?)accessTokens["access_token"];
            sRefreshToken = (string?)accessTokens["refresh_token"];
            if (sAccessToken == null)
                throw new InvalidOperationException("Failed to parse JWT from login result.");

            Console.WriteLine("Parsing access token and refresh token from login result successful.");

            if (!await EstablishWebsocketAndSendMessage())
                throw new Exception("Couldn't establish websocket connection.");
        }
        catch (Exception exc)
        {
            ShutdownCancellationTokenSource.Cancel();
            Console.WriteLine($"An exception has occurred: '{exc}'\nExiting application...");
            _ = Console.ReadKey();

            return;
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

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, serverUri);
        httpRequest.Headers.Add("username", Configuration.Client_Username);
        httpRequest.Headers.Add("password", Configuration.Client_Password);
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
            Console.WriteLine("Connected to the server");

            var message = "Test message";
            var encoded = Encoding.UTF8.GetBytes(message);
            var buffer = new ArraySegment<byte>(encoded, 0, encoded.Length);

            await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine("Sent message: " + message);

            var receivedBuffer = new ArraySegment<byte>(new byte[1024]);
            var result = await webSocket.ReceiveAsync(receivedBuffer, CancellationToken.None);

            if (receivedBuffer.Array == null)
                throw new NullReferenceException();

            var receivedMessage = Encoding.UTF8.GetString(receivedBuffer.Array, 0, result.Count);
            Console.WriteLine("Received message: " + receivedMessage);

            return true;
        }
        catch (WebSocketException wse)
        {
            Console.WriteLine("WebSocket exception: " + wse.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception: " + ex.Message);
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
