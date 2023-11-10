using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;

namespace BeyondImmersion.ServiceTester;

public class Program
{
    private static IConfigurationRoot _configurationRoot;
    /// <summary>
    /// Shared service configuration root.
    /// Includes command line args.
    /// </summary>
    public static IConfigurationRoot ConfigurationRoot
    {
        get => _configurationRoot ??= IServiceConfiguration.BuildConfigurationRoot(Environment.GetCommandLineArgs());
        internal set => _configurationRoot = value;
    }

    private static ClientConfiguration _configuration;
    /// <summary>
    /// Client configuration.
    /// Pull from Config.json, ENVs, and command line args.
    /// </summary>
    public static ClientConfiguration Configuration
    {
        get => _configuration ??= ConfigurationRoot.Get<ClientConfiguration>() ?? new ClientConfiguration();
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

    // tokens generated from login
    private static string? sAccessToken = null;
    private static string? sRefreshToken = null;

    internal static async Task Main()
    {
        try
        {
            // configuration is auto-created on first get, so this call creates the config too
            if (Configuration == null || !(Configuration as IServiceConfiguration).HasRequired())
                throw new InvalidOperationException("Client configuration missing.");

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
