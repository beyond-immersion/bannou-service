using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Connect.Models;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// WebSocket-first edge gateway service providing zero-copy message routing.
/// Handles client connections, service discovery, and message routing between clients and services.
/// </summary>
[DaprService("connect", typeof(IConnectService), lifetime: ServiceLifetime.Singleton)]
public class ConnectService : DaprService<ConnectServiceConfiguration>, IConnectService
{
    private readonly IAuthService _authService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ConnectService> _logger;

    // Thread-safe collections for connection management
    private readonly ConcurrentDictionary<string, ClientConnection> _connections;
    private readonly ConcurrentDictionary<Guid, string> _serviceGuidToClient;
    private readonly Channel<BinaryMessage> _messageQueue;
    private readonly ChannelWriter<BinaryMessage> _messageWriter;
    private readonly ChannelReader<BinaryMessage> _messageReader;

    // Metrics
    private long _totalConnections;
    private long _currentConnections;
    private long _totalMessagesRouted;
    private readonly DateTime _serviceStartTime;

    public ConnectService(
        IAuthService authService,
        IMemoryCache cache,
        ConnectServiceConfiguration configuration,
        ILogger<ConnectService> logger)
        : base(configuration, logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _connections = new ConcurrentDictionary<string, ClientConnection>();
        _serviceGuidToClient = new ConcurrentDictionary<Guid, string>();

        // Create message processing queue
        var channelOptions = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };

        _messageQueue = Channel.CreateBounded<BinaryMessage>(channelOptions);
        _messageWriter = _messageQueue.Writer;
        _messageReader = _messageQueue.Reader;

        _serviceStartTime = DateTime.UtcNow;

        // Start background message processing
        _ = Task.Run(ProcessMessageQueueAsync);
    }

    /// <summary>
    /// Internal API proxy for stateless requests
    /// </summary>
    public async Task<(StatusCodes, InternalProxyResponse?)> ProxyInternalRequestAsync(
        InternalProxyRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing internal proxy request to {TargetService}/{Method}",
                body.TargetService, body.Method);

            // TODO: Implement actual proxy logic
            // This is a placeholder implementation
            await Task.Delay(1, cancellationToken);

            var response = new InternalProxyResponse
            {
                Success = true,
                StatusCode = 200,
                Response = "{\"message\": \"Proxy request processed successfully\"}",
                Headers = new Dictionary<string, ICollection<string>>()
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing internal proxy request");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Get available APIs for current session
    /// </summary>
    public async Task<(StatusCodes, ApiDiscoveryResponse?)> DiscoverAPIsAsync(
        ApiDiscoveryRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing API discovery request for session {SessionId}",
                body.SessionId);

            // TODO: Implement actual API discovery logic
            // This is a placeholder implementation
            await Task.Delay(1, cancellationToken);

            var response = new ApiDiscoveryResponse
            {
                SessionId = body.SessionId ?? Guid.NewGuid().ToString(),
                AvailableAPIs = new List<ApiEndpointInfo>(),
                ServiceCapabilities = new Dictionary<string, ICollection<string>>(),
                GeneratedAt = DateTimeOffset.UtcNow
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing API discovery request");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task HandleWebSocketAsync(
        WebSocket webSocket,
        string? authorization = null,
        CancellationToken cancellationToken = default)
    {
        var clientId = await RegisterClientAsync(webSocket, authorization, cancellationToken);
        var connection = _connections[clientId];

        _logger.LogInformation("WebSocket connection established for client {ClientId}", clientId);

        try
        {
            // Send initial service discovery response
            await SendServiceDiscoveryResponse(connection, cancellationToken);

            // Message handling loop
            var buffer = new byte[Configuration.BufferSize];
            var messageBuffer = new List<byte>();

            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await connection.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (result == null)
                    break;

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client requested close", cancellationToken);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Accumulate message data
                    messageBuffer.AddRange(buffer.AsSpan(0, result.Count).ToArray());

                    if (result.EndOfMessage)
                    {
                        // Process complete message
                        var messageData = messageBuffer.ToArray();
                        messageBuffer.Clear();

                        await ProcessClientMessage(clientId, messageData, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket for client {ClientId}", clientId);
        }
        finally
        {
            await UnregisterClientAsync(clientId, cancellationToken);
            _logger.LogInformation("WebSocket connection closed for client {ClientId}", clientId);
        }
    }

    /// <inheritdoc />
    public async Task<string> RegisterClientAsync(
        WebSocket webSocket,
        string? authorization = null,
        CancellationToken cancellationToken = default)
    {
        var clientId = Guid.NewGuid().ToString();

        // Validate authorization and create principal
        ClaimsPrincipal? principal = null;
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            principal = await ValidateAuthorizationAsync(authorization, cancellationToken);
        }

        // Create connection
        var connection = new ClientConnection(clientId, webSocket, principal);

        // Update service mappings based on authentication
        var availableServices = GetAvailableServices(principal);
        connection.UpdateServiceMappings(availableServices);

        // Register service GUID mappings for reverse lookup
        foreach (var mapping in connection.ServiceMappings)
        {
            _serviceGuidToClient[mapping.Value] = clientId;
        }

        // Add to connections
        _connections[clientId] = connection;

        Interlocked.Increment(ref _totalConnections);
        Interlocked.Increment(ref _currentConnections);

        _logger.LogDebug("Registered client {ClientId} with {ServiceCount} available services",
            clientId, connection.ServiceMappings.Count);

        return clientId;
    }

    /// <inheritdoc />
    public async Task UnregisterClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (_connections.TryRemove(clientId, out var connection))
        {
            // Remove service GUID mappings
            foreach (var mapping in connection.ServiceMappings)
            {
                _serviceGuidToClient.TryRemove(mapping.Value, out _);
            }

            // Close connection if still open
            await connection.CloseAsync(cancellationToken: cancellationToken);
            connection.Dispose();

            Interlocked.Decrement(ref _currentConnections);

            _logger.LogDebug("Unregistered client {ClientId}", clientId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RouteMessageAsync(
        ReadOnlyMemory<byte> message,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (message.Length < 24) // Minimum header size
            {
                _logger.LogWarning("Received malformed message from client {ClientId} (size: {Size})", clientId, message.Length);
                return false;
            }

            var binaryMessage = new BinaryMessage(message);

            // Check if this is a client-to-client message
            if (Configuration.EnableClientToClientRouting &&
                _serviceGuidToClient.TryGetValue(binaryMessage.ServiceGuid, out var targetClientId) &&
                targetClientId != clientId)
            {
                return await SendToClientAsync(targetClientId, message, cancellationToken);
            }

            // Otherwise, route to service via Dapr
            return await RouteToServiceAsync(binaryMessage, clientId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error routing message from client {ClientId}", clientId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendToClientAsync(
        string clientId,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(clientId, out var connection))
        {
            return await connection.SendAsync(message, cancellationToken);
        }

        _logger.LogWarning("Attempted to send message to non-existent client {ClientId}", clientId);
        return false;
    }

    /// <inheritdoc />
    public async Task BroadcastAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        var connections = _connections.Values.ToArray();
        var tasks = connections.Select(conn => conn.SendAsync(message, cancellationToken));

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r);

        _logger.LogDebug("Broadcast message to {Total} clients, {Success} successful",
            connections.Length, successCount);
    }

    #region Private Helper Methods

    private async Task<ClaimsPrincipal?> ValidateAuthorizationAsync(string authorization, CancellationToken cancellationToken)
    {
        try
        {
            var token = authorization.StartsWith("Bearer ") ? authorization[7..] : authorization;
            var validateResult = await _authService.ValidateTokenAsync(token, cancellationToken);

            if (validateResult.Result is OkObjectResult okResult &&
                okResult.Value is ValidateTokenResponse tokenResponse &&
                tokenResponse.Valid)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, tokenResponse.AccountId.ToString()),
                    new(ClaimTypes.Email, tokenResponse.Email)
                };

                claims.AddRange(tokenResponse.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
                return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate authorization token");
        }

        return null;
    }

    private List<string> GetAvailableServices(ClaimsPrincipal? principal)
    {
        var services = new List<string>(Configuration.DefaultServices);

        if (principal?.Identity?.IsAuthenticated == true)
        {
            services.AddRange(Configuration.AuthenticatedServices);
        }

        return services;
    }

    private Dictionary<string, string> GenerateServiceMappings(string clientId, IEnumerable<string> services)
    {
        var mappings = new Dictionary<string, string>();

        foreach (var service in services)
        {
            // Generate unique GUID for this client/service combination
            var salt = $"{clientId}:{service}:{DateTime.UtcNow:O}";
            var guidBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(salt));
            var serviceGuid = new Guid(guidBytes);
            mappings[service] = serviceGuid.ToString();
        }

        return mappings;
    }

    private async Task SendServiceDiscoveryResponse(ClientConnection connection, CancellationToken cancellationToken)
    {
        var response = new
        {
            type = "service_discovery",
            client_id = connection.ClientId,
            services = connection.ServiceMappings.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToString())
        };

        var jsonResponse = JsonSerializer.Serialize(response);
        var responseMessage = BinaryMessage.Create(Guid.Empty, 0, jsonResponse);

        await connection.SendAsync(responseMessage.Data, cancellationToken);
    }

    private async Task ProcessClientMessage(string clientId, byte[] messageData, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(clientId, out var connection))
            return;

        // Check rate limiting
        if (connection.IsRateLimited(Configuration.MaxMessagesPerMinute, Configuration.RateLimitWindowMinutes))
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId}", clientId);
            return;
        }

        connection.IncrementMessageCount();

        // Queue message for processing
        var binaryMessage = new BinaryMessage(messageData);

        if (!await _messageWriter.WaitToWriteAsync(cancellationToken))
            return;

        await _messageWriter.WriteAsync(binaryMessage, cancellationToken);
    }

    private async Task ProcessMessageQueueAsync()
    {
        await foreach (var message in _messageReader.ReadAllAsync())
        {
            try
            {
                // Process message routing logic here
                Interlocked.Increment(ref _totalMessagesRouted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from queue");
            }
        }
    }

    private async Task<bool> RouteToServiceAsync(BinaryMessage message, string clientId, CancellationToken cancellationToken)
    {
        // Get the service name from the GUID
        if (!_connections.TryGetValue(clientId, out var connection))
            return false;

        var serviceName = connection.GetServiceForGuid(message.ServiceGuid);
        if (string.IsNullOrEmpty(serviceName))
        {
            _logger.LogWarning("Unknown service GUID {ServiceGuid} from client {ClientId}",
                message.ServiceGuid, clientId);
            return false;
        }

        // TODO: Route to actual Dapr service
        // This is where we would call the appropriate service via Dapr
        _logger.LogDebug("Routing message to service {ServiceName} for client {ClientId}",
            serviceName, clientId);

        await Task.CompletedTask;
        return true;
    }

    private double CalculateAverageLatency()
    {
        // Placeholder for latency calculation
        return 0.0;
    }

    private ActionResult<T> StatusCode<T>(int statusCode, string message)
    {
        return new ObjectResult(new { error = message }) { StatusCode = statusCode };
    }

    #endregion

}
