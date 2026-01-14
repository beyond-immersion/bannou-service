using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.Client;

/// <summary>
/// Manages the WebSocket connection to Bannou services with auto-reconnect,
/// health checks, and event routing.
/// </summary>
/// <remarks>
/// This class wraps BannouClient and provides:
/// - Automatic reconnection with exponential backoff
/// - Health check loop for connection monitoring
/// - Event subscription/dispatch pattern
/// - Connection status tracking with events
/// </remarks>
public sealed class BannouConnectionManager : IAsyncDisposable
{
    private readonly ILogger<BannouConnectionManager>? _logger;
    private readonly BannouConnectionConfig _config;
    private BannouClient? _client;
    private CancellationTokenSource? _reconnectCts;
    private Task? _healthCheckTask;
    private bool _disposed;

    // Connection state
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private int _reconnectAttempts;
    private DateTime _lastConnectAttempt;
    private DateTime _lastSuccessfulConnect;

    /// <summary>
    /// Event handlers for server-pushed events from Bannou.
    /// </summary>
    private readonly Dictionary<string, List<Action<string>>> _eventHandlers = new();

    /// <summary>
    /// Current connection status.
    /// </summary>
    public ConnectionStatus Status => _status;

    /// <summary>
    /// Whether currently connected to Bannou services.
    /// </summary>
    public bool IsConnected => _client?.IsConnected ?? false;

    /// <summary>
    /// Session ID from Bannou (null if not connected).
    /// </summary>
    public string? SessionId => _client?.SessionId;

    /// <summary>
    /// The underlying BannouClient for direct API calls.
    /// </summary>
    public BannouClient? Client => _client;

    /// <summary>
    /// Time of the last successful connection.
    /// </summary>
    public DateTime LastSuccessfulConnect => _lastSuccessfulConnect;

    /// <summary>
    /// Number of reconnect attempts since last successful connection.
    /// </summary>
    public int ReconnectAttempts => _reconnectAttempts;

    /// <summary>
    /// Event raised when connection status changes.
    /// </summary>
    public event Action<ConnectionStatus>? OnStatusChanged;

    /// <summary>
    /// Creates a new BannouConnectionManager.
    /// </summary>
    /// <param name="config">Connection configuration.</param>
    /// <param name="logger">Optional logger instance.</param>
    public BannouConnectionManager(BannouConnectionConfig config, ILogger<BannouConnectionManager>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    // ==========================================================================
    // CONNECTION LIFECYCLE
    // ==========================================================================

    /// <summary>
    /// Connect to Bannou services.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection succeeded, false otherwise.</returns>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BannouConnectionManager));

        if (IsConnected)
        {
            _logger?.LogWarning("Already connected to Bannou");
            return true;
        }

        SetStatus(ConnectionStatus.Connecting);
        _lastConnectAttempt = DateTime.UtcNow;

        try
        {
            _client = new BannouClient();

            // Register base event handlers
            RegisterCoreEventHandlers();

            _logger?.LogInformation("Connecting to Bannou at {Endpoint}...", _config.Endpoint);

            bool connected;
            if (_config.UseRegistration)
            {
                // Try login first, then register if needed
                connected = await _client.ConnectAsync(
                    _config.Endpoint,
                    _config.Email,
                    _config.Password,
                    cancellationToken);

                if (!connected)
                {
                    _logger?.LogInformation("Login failed, attempting registration...");
                    connected = await _client.RegisterAndConnectAsync(
                        _config.Endpoint,
                        _config.Username,
                        _config.Email,
                        _config.Password,
                        cancellationToken);
                }
            }
            else
            {
                connected = await _client.ConnectAsync(
                    _config.Endpoint,
                    _config.Email,
                    _config.Password,
                    cancellationToken);
            }

            if (connected)
            {
                _reconnectAttempts = 0;
                _lastSuccessfulConnect = DateTime.UtcNow;
                SetStatus(ConnectionStatus.Connected);

                _logger?.LogInformation("Connected to Bannou! Session: {SessionId}", _client.SessionId);
                LogAvailableApis();

                // Start health check loop if auto-reconnect is enabled
                if (_config.AutoReconnect)
                {
                    StartHealthCheck();
                }

                return true;
            }
            else
            {
                _logger?.LogError("Failed to connect to Bannou: {Error}", _client.LastError);
                SetStatus(ConnectionStatus.Failed);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception connecting to Bannou");
            SetStatus(ConnectionStatus.Failed);
            return false;
        }
    }

    /// <summary>
    /// Disconnect from Bannou services.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_disposed) return;

        _logger?.LogInformation("Disconnecting from Bannou...");

        StopHealthCheck();

        if (_client != null && _client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during disconnect");
            }
        }

        SetStatus(ConnectionStatus.Disconnected);
        _logger?.LogInformation("Disconnected from Bannou");
    }

    /// <summary>
    /// Attempt to reconnect to Bannou services.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if reconnection succeeded, false otherwise.</returns>
    public async Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Attempting to reconnect to Bannou (attempt {Attempt})...",
            _reconnectAttempts + 1);

        SetStatus(ConnectionStatus.Reconnecting);
        _reconnectAttempts++;

        // Dispose old client
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        // Apply exponential backoff
        var delay = CalculateBackoffDelay();
        _logger?.LogDebug("Waiting {Delay}ms before reconnect attempt", delay);
        await Task.Delay(delay, cancellationToken);

        return await ConnectAsync(cancellationToken);
    }

    // ==========================================================================
    // API INVOCATION
    // ==========================================================================

    /// <summary>
    /// Invoke a Bannou service API.
    /// </summary>
    /// <typeparam name="TRequest">Request type.</typeparam>
    /// <typeparam name="TResponse">Response type.</typeparam>
    /// <param name="method">HTTP method (POST, GET, etc.).</param>
    /// <param name="path">API path.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="channel">Optional channel for routing.</param>
    /// <param name="timeout">Optional timeout override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>API response with success/failure status.</returns>
    public async Task<ApiResponse<TResponse>> InvokeAsync<TRequest, TResponse>(
        string method,
        string path,
        TRequest request,
        ushort channel = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _client == null)
        {
            return ApiResponse<TResponse>.Failure(new ErrorResponse
            {
                Message = "Not connected to Bannou services"
            });
        }

        try
        {
            return await _client.InvokeAsync<TRequest, TResponse>(
                method, path, request, channel, timeout, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error invoking {Method} {Path}", method, path);
            return ApiResponse<TResponse>.Failure(new ErrorResponse
            {
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Send a fire-and-forget event to Bannou.
    /// </summary>
    /// <typeparam name="TRequest">Request type.</typeparam>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">API path.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="channel">Optional channel for routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SendEventAsync<TRequest>(
        string method,
        string path,
        TRequest request,
        ushort channel = 0,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _client == null)
        {
            throw new InvalidOperationException("Not connected to Bannou services");
        }

        await _client.SendEventAsync(method, path, request, channel, cancellationToken);
    }

    // ==========================================================================
    // EVENT HANDLING
    // ==========================================================================

    /// <summary>
    /// Subscribe to a Bannou event type.
    /// </summary>
    /// <param name="eventType">Event type to subscribe to.</param>
    /// <param name="handler">Handler to invoke when event is received.</param>
    public void Subscribe(string eventType, Action<string> handler)
    {
        if (!_eventHandlers.TryGetValue(eventType, out var handlers))
        {
            handlers = new List<Action<string>>();
            _eventHandlers[eventType] = handlers;

            // Register with client if connected
            _client?.OnEvent(eventType, json => DispatchEvent(eventType, json));
        }

        handlers.Add(handler);
    }

    /// <summary>
    /// Unsubscribe from a Bannou event type.
    /// </summary>
    /// <param name="eventType">Event type to unsubscribe from.</param>
    /// <param name="handler">Handler to remove.</param>
    public void Unsubscribe(string eventType, Action<string> handler)
    {
        if (_eventHandlers.TryGetValue(eventType, out var handlers))
        {
            handlers.Remove(handler);

            if (handlers.Count == 0)
            {
                _eventHandlers.Remove(eventType);
                _client?.RemoveEventHandler(eventType);
            }
        }
    }

    private void DispatchEvent(string eventType, string json)
    {
        if (_eventHandlers.TryGetValue(eventType, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    handler(json);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in event handler for {EventType}", eventType);
                }
            }
        }
    }

    private void RegisterCoreEventHandlers()
    {
        if (_client == null) return;

        _client.OnEvent("connect.capability_manifest", OnCapabilityManifestReceived);
        _client.OnEvent("connect.session_invalidated", OnSessionInvalidated);
    }

    private void OnCapabilityManifestReceived(string json)
    {
        _logger?.LogDebug("Received capability manifest update");
        DispatchEvent("connect.capability_manifest", json);
    }

    private void OnSessionInvalidated(string json)
    {
        _logger?.LogWarning("Session invalidated by server");
        SetStatus(ConnectionStatus.Disconnected);
        DispatchEvent("connect.session_invalidated", json);
    }

    // ==========================================================================
    // HEALTH CHECK & RECONNECT
    // ==========================================================================

    private void StartHealthCheck()
    {
        _reconnectCts = new CancellationTokenSource();
        _healthCheckTask = HealthCheckLoopAsync(_reconnectCts.Token);
    }

    private void StopHealthCheck()
    {
        _reconnectCts?.Cancel();
        _reconnectCts = null;
        _healthCheckTask = null;
    }

    private async Task HealthCheckLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_config.HealthCheckIntervalMs, cancellationToken);

                if (!IsConnected && _config.AutoReconnect)
                {
                    if (_reconnectAttempts < _config.MaxReconnectAttempts)
                    {
                        await ReconnectAsync(cancellationToken);
                    }
                    else
                    {
                        _logger?.LogError("Max reconnect attempts ({Max}) reached, giving up",
                            _config.MaxReconnectAttempts);
                        SetStatus(ConnectionStatus.Failed);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in health check loop");
            }
        }
    }

    private int CalculateBackoffDelay()
    {
        // Exponential backoff with jitter: min(baseDelay * 2^attempts, maxDelay) + random
        var exponential = (int)Math.Min(
            _config.ReconnectBaseDelayMs * Math.Pow(2, _reconnectAttempts - 1),
            _config.ReconnectMaxDelayMs);

        var jitter = Random.Shared.Next(0, exponential / 4);
        return exponential + jitter;
    }

    // ==========================================================================
    // UTILITY
    // ==========================================================================

    private void SetStatus(ConnectionStatus status)
    {
        if (_status != status)
        {
            _status = status;
            OnStatusChanged?.Invoke(status);
        }
    }

    private void LogAvailableApis()
    {
        if (_client?.AvailableApis == null) return;

        var apis = _client.AvailableApis;
        _logger?.LogDebug("Available APIs ({Count}):", apis.Count);

        // Log first few APIs
        foreach (var api in apis.Take(10))
        {
            _logger?.LogDebug("  {Key}", api.Key);
        }

        if (apis.Count > 10)
        {
            _logger?.LogDebug("  ... and {More} more", apis.Count - 10);
        }
    }

    /// <summary>
    /// Dispose of the connection manager and release resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        StopHealthCheck();

        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }
}

/// <summary>
/// Configuration for Bannou connection.
/// </summary>
public sealed class BannouConnectionConfig
{
    /// <summary>
    /// Bannou server endpoint (e.g., "http://localhost:5050").
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:5050";

    /// <summary>
    /// Email for authentication.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Password for authentication.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Username for registration (if enabled).
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Whether to attempt registration if login fails.
    /// </summary>
    public bool UseRegistration { get; init; } = false;

    /// <summary>
    /// Whether to automatically reconnect on connection loss.
    /// </summary>
    public bool AutoReconnect { get; init; } = true;

    /// <summary>
    /// Health check interval in milliseconds.
    /// </summary>
    public int HealthCheckIntervalMs { get; init; } = 5000;

    /// <summary>
    /// Maximum number of reconnect attempts before giving up.
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 10;

    /// <summary>
    /// Base delay for reconnect backoff in milliseconds.
    /// </summary>
    public int ReconnectBaseDelayMs { get; init; } = 1000;

    /// <summary>
    /// Maximum delay for reconnect backoff in milliseconds.
    /// </summary>
    public int ReconnectMaxDelayMs { get; init; } = 30000;
}

/// <summary>
/// Connection status for Bannou services.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>Not connected to Bannou services.</summary>
    Disconnected,
    /// <summary>Currently connecting to Bannou services.</summary>
    Connecting,
    /// <summary>Connected to Bannou services.</summary>
    Connected,
    /// <summary>Attempting to reconnect after connection loss.</summary>
    Reconnecting,
    /// <summary>Connection failed and reconnect attempts exhausted.</summary>
    Failed
}
