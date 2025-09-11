using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// WebSocket test client that communicates via the Connect service using the Bannou binary protocol
/// </summary>
public class WebSocketTestClient : ITestClient, IDisposable
{
    private readonly TestConfiguration _configuration;
    private ClientWebSocket? _webSocket;
    private readonly Dictionary<string, string> _serviceRegistry = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TestResponse<JObject>>> _pendingRequests = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private string? _accessToken;
    private string? _refreshToken;
    private bool _isConnected;

    public WebSocketTestClient(TestConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken);
    public string TransportType => "WebSocket";

    /// <summary>
    /// Connect to the WebSocket service and perform service discovery
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _webSocket = new ClientWebSocket();
            var uri = new Uri(_configuration.WebSocket_Endpoint ?? "wss://localhost/connect");

            await _webSocket.ConnectAsync(uri, _cancellationTokenSource.Token);
            _isConnected = true;

            // Start message handling loop
            _ = Task.Run(HandleMessages, _cancellationTokenSource.Token);

            // Wait for service discovery
            var discoveryReceived = new TaskCompletionSource<bool>();
            var timeout = Task.Delay(TimeSpan.FromSeconds(10), _cancellationTokenSource.Token);

            // Set up temporary handler for service discovery
            _pendingRequests["service_discovery"] = new TaskCompletionSource<TestResponse<JObject>>();

            if (await Task.WhenAny(discoveryReceived.Task, timeout) == timeout)
            {
                return false;
            }

            return _serviceRegistry.Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string username, string password)
    {
        if (!_isConnected && !await ConnectAsync())
            return false;

        try
        {
            var request = new
            {
                username = username,
                password = password,
                email = $"{username}@example.com"
            };

            var response = await CallServiceAsync("auth.register", request);
            if (response.Success)
            {
                var data = response.Data;
                _accessToken = data?["access_token"]?.Value<string>();
                _refreshToken = data?["refresh_token"]?.Value<string>();
                return !string.IsNullOrWhiteSpace(_accessToken);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        if (!_isConnected && !await ConnectAsync())
            return false;

        try
        {
            var request = new
            {
                username = username,
                password = password
            };

            var response = await CallServiceAsync("auth.login", request);
            if (response.Success)
            {
                var data = response.Data;
                _accessToken = data?["access_token"]?.Value<string>();
                _refreshToken = data?["refresh_token"]?.Value<string>();
                return !string.IsNullOrWhiteSpace(_accessToken);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<TestResponse<T>> PostAsync<T>(string endpoint, object? requestBody = null) where T : class
    {
        if (!_isConnected && !await ConnectAsync())
            return TestResponse<T>.Failed(503, "WebSocket not connected");

        try
        {
            // Convert HTTP endpoint to service method
            var serviceMethod = ConvertEndpointToServiceMethod(endpoint);

            var response = await CallServiceAsync(serviceMethod, requestBody);

            if (!response.Success)
                return TestResponse<T>.Failed(response.StatusCode, response.ErrorMessage ?? "WebSocket request failed");

            // Convert JObject to requested type
            if (typeof(T) == typeof(JObject))
                return TestResponse<T>.Successful((T)(object)response.Data!, response.StatusCode);

            if (response.Data != null)
            {
                var jsonString = response.Data.ToString();
                var convertedData = JsonSerializer.Deserialize<T>(jsonString);
                if (convertedData != null)
                    return TestResponse<T>.Successful(convertedData, response.StatusCode);
            }

            return TestResponse<T>.Failed(500, "Failed to convert response data");
        }
        catch (Exception ex)
        {
            return TestResponse<T>.Failed(500, $"WebSocket request exception: {ex.Message}");
        }
    }

    public async Task<TestResponse<T>> GetAsync<T>(string endpoint) where T : class
    {
        // For WebSocket, GET requests are just POST requests without body
        return await PostAsync<T>(endpoint, null);
    }

    private async Task<TestResponse<JObject>> CallServiceAsync(string serviceMethod, object? payload)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return TestResponse<JObject>.Failed(503, "WebSocket not connected");

        if (!_serviceRegistry.TryGetValue(serviceMethod, out var serviceGuid))
            return TestResponse<JObject>.Failed(404, $"Unknown service method: {serviceMethod}");

        var correlationId = Guid.NewGuid().ToString();
        var request = new
        {
            type = "service_request",
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            correlation_id = correlationId,
            service_method = serviceMethod,
            payload = payload,
            expect_response = true
        };

        // Create pending request handler
        var tcs = new TaskCompletionSource<TestResponse<JObject>>();
        _pendingRequests[correlationId] = tcs;

        try
        {
            // Serialize message
            var messageJson = JsonSerializer.Serialize(request);
            var messageBytes = Encoding.UTF8.GetBytes(messageJson);

            // Pack with binary protocol (simplified - in real implementation would use service GUID)
            var binaryMessage = CreateBinaryMessage(serviceGuid, messageBytes);

            // Send message
            await _webSocket.SendAsync(binaryMessage, WebSocketMessageType.Binary, true, _cancellationTokenSource.Token);

            // Wait for response with timeout
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
            var completedTask = await Task.WhenAny(tcs.Task, timeout);

            if (completedTask == timeout)
            {
                _pendingRequests.TryRemove(correlationId, out _);
                return TestResponse<JObject>.Failed(408, "Request timeout");
            }

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(correlationId, out _);
            return TestResponse<JObject>.Failed(500, $"Request failed: {ex.Message}");
        }
    }

    private async Task HandleMessages()
    {
        var buffer = new byte[8192];

        while (_webSocket?.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var messageText = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    HandleTextMessage(messageText);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    HandleBinaryMessage(buffer, result.Count);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket message handling error: {ex.Message}");
                break;
            }
        }
    }

    private void HandleTextMessage(string messageText)
    {
        try
        {
            var message = JsonSerializer.Deserialize<JsonElement>(messageText);

            if (message.TryGetProperty("type", out var typeElement) &&
                typeElement.GetString() == "service_discovery")
            {
                HandleServiceDiscovery(message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling text message: {ex.Message}");
        }
    }

    private void HandleBinaryMessage(byte[] buffer, int length)
    {
        try
        {
            // Simplified binary message handling - extract payload and deserialize
            // In real implementation, would properly parse binary protocol with service GUID header
            var payloadStart = 24; // Skip 24-byte header (16 bytes service GUID + 8 bytes message ID)
            if (length <= payloadStart) return;

            var payloadBytes = buffer[payloadStart..length];
            var payloadText = Encoding.UTF8.GetString(payloadBytes);
            var response = JsonSerializer.Deserialize<JsonElement>(payloadText);

            if (response.TryGetProperty("correlation_id", out var correlationIdElement))
            {
                var correlationId = correlationIdElement.GetString();
                if (correlationId != null && _pendingRequests.TryRemove(correlationId, out var tcs))
                {
                    var success = response.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
                    var statusCode = success ? 200 : 400;

                    if (success && response.TryGetProperty("payload", out var payloadElement))
                    {
                        var payloadObj = JObject.Parse(payloadElement.GetRawText());
                        tcs.SetResult(TestResponse<JObject>.Successful(payloadObj, statusCode));
                    }
                    else if (!success && response.TryGetProperty("error", out var errorElement))
                    {
                        var errorMessage = errorElement.TryGetProperty("message", out var msgElement)
                            ? msgElement.GetString() ?? "Unknown error"
                            : "Request failed";
                        tcs.SetResult(TestResponse<JObject>.Failed(statusCode, errorMessage));
                    }
                    else
                    {
                        tcs.SetResult(TestResponse<JObject>.Failed(500, "Invalid response format"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling binary message: {ex.Message}");
        }
    }

    private void HandleServiceDiscovery(JsonElement message)
    {
        try
        {
            if (message.TryGetProperty("services", out var servicesElement))
            {
                foreach (var service in servicesElement.EnumerateObject())
                {
                    _serviceRegistry[service.Name] = service.Value.GetString() ?? "";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling service discovery: {ex.Message}");
        }
    }

    private string ConvertEndpointToServiceMethod(string endpoint)
    {
        // Convert HTTP endpoint to WebSocket service method
        // e.g., "api/accounts/create" -> "account.create"
        var parts = endpoint.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0] == "api")
        {
            var service = parts[1].TrimEnd('s'); // Remove trailing 's' from service name
            var method = parts.Length > 2 ? parts[2] : "get";
            return $"{service}.{method}";
        }

        return endpoint.Replace("/", ".");
    }

    private byte[] CreateBinaryMessage(string serviceGuid, byte[] payload)
    {
        // Simplified binary protocol implementation
        // Real implementation would use proper GUID parsing and binary encoding
        var guidBytes = Encoding.UTF8.GetBytes(serviceGuid.PadRight(16).Substring(0, 16));
        var messageIdBytes = Guid.NewGuid().ToByteArray()[..8];

        var message = new byte[24 + payload.Length];
        Array.Copy(guidBytes, 0, message, 0, 16);
        Array.Copy(messageIdBytes, 0, message, 16, 8);
        Array.Copy(payload, 0, message, 24, payload.Length);

        return message;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _webSocket?.Dispose();
        _cancellationTokenSource.Dispose();
    }
}
