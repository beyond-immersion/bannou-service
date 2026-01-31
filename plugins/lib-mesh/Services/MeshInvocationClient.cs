#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// HTTP-based implementation of mesh service invocation.
/// Uses IMeshStateManager directly for endpoint resolution to avoid circular dependencies.
/// This is infrastructure that all generated clients depend on, so it cannot use generated clients.
/// </summary>
public sealed class MeshInvocationClient : IMeshInvocationClient, IDisposable
{
    private readonly IMeshStateManager _stateManager;
    private readonly MeshServiceConfiguration _configuration;
    private readonly ILogger<MeshInvocationClient> _logger;
    private readonly ITelemetryProvider? _telemetryProvider;
    private readonly HttpMessageInvoker _httpClient;

    // Cache for endpoint resolution to reduce state store calls
    private readonly EndpointCache _endpointCache;

    // Circuit breaker state per app-id
    private readonly CircuitBreaker _circuitBreaker;

    // Round-robin counter for load balancing across multiple endpoints
    private int _roundRobinCounter;

    /// <summary>
    /// Creates a new MeshInvocationClient.
    /// </summary>
    /// <param name="stateManager">State manager for endpoint resolution (avoids circular dependency with generated clients).</param>
    /// <param name="configuration">Mesh service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Optional telemetry provider for instrumentation.</param>
    public MeshInvocationClient(
        IMeshStateManager stateManager,
        MeshServiceConfiguration configuration,
        ILogger<MeshInvocationClient> logger,
        ITelemetryProvider? telemetryProvider = null)
    {
        _stateManager = stateManager;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;

        if (_telemetryProvider != null)
        {
            _logger.LogDebug(
                "MeshInvocationClient created with telemetry instrumentation: tracing={TracingEnabled}, metrics={MetricsEnabled}",
                _telemetryProvider.TracingEnabled, _telemetryProvider.MetricsEnabled);
        }

        SocketsHttpHandler? handler = null;
        try
        {
            handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                EnableMultipleHttp2Connections = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(configuration.PooledConnectionLifetimeMinutes),
                ConnectTimeout = TimeSpan.FromSeconds(configuration.ConnectTimeoutSeconds)
            };
            _httpClient = new HttpMessageInvoker(handler);
            handler = null; // Ownership transferred to HttpMessageInvoker
        }
        finally
        {
            handler?.Dispose(); // Only executes if ownership transfer failed
        }

        _endpointCache = new EndpointCache(TimeSpan.FromSeconds(configuration.EndpointCacheTtlSeconds));
        _circuitBreaker = new CircuitBreaker(
            configuration.CircuitBreakerThreshold,
            TimeSpan.FromSeconds(configuration.CircuitBreakerResetSeconds));
    }

    /// <inheritdoc/>
    public async Task<TResponse> InvokeMethodAsync<TRequest, TResponse>(
        string appId,
        string methodName,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        using var httpRequest = CreateInvokeMethodRequest(HttpMethod.Post, appId, methodName, request);
        using var response = await InvokeMethodWithResponseAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw MeshInvocationException.HttpError(appId, methodName, (int)response.StatusCode, errorBody);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await BannouJson.DeserializeAsync<TResponse>(responseStream, cancellationToken) ?? throw new MeshInvocationException(appId, methodName, "Response deserialized to null");
        return result;
    }

    /// <inheritdoc/>
    public async Task InvokeMethodAsync<TRequest>(
        string appId,
        string methodName,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
    {
        using var httpRequest = CreateInvokeMethodRequest(HttpMethod.Post, appId, methodName, request);
        using var response = await InvokeMethodWithResponseAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw MeshInvocationException.HttpError(appId, methodName, (int)response.StatusCode, errorBody);
        }
    }

    /// <inheritdoc/>
    public async Task<TResponse> InvokeMethodAsync<TResponse>(
        string appId,
        string methodName,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        using var httpRequest = CreateInvokeMethodRequest(HttpMethod.Get, appId, methodName);
        using var response = await InvokeMethodWithResponseAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw MeshInvocationException.HttpError(appId, methodName, (int)response.StatusCode, errorBody);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await BannouJson.DeserializeAsync<TResponse>(responseStream, cancellationToken) ?? throw new MeshInvocationException(appId, methodName, "Response deserialized to null");
        return result;
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> InvokeMethodWithResponseAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        // Extract app-id and method from request headers/uri
        if (!request.Options.TryGetValue(new HttpRequestOptionsKey<string>("mesh-app-id"), out var appId) ||
            string.IsNullOrEmpty(appId))
        {
            throw new ArgumentException("Request must include mesh-app-id option. Use CreateInvokeMethodRequest to create requests.");
        }

        if (!request.Options.TryGetValue(new HttpRequestOptionsKey<string>("mesh-method"), out var methodName))
        {
            methodName = request.RequestUri?.PathAndQuery ?? "unknown";
        }

        // Start telemetry activity for this mesh invocation
        using var activity = _telemetryProvider?.StartActivity(
            TelemetryComponents.Mesh,
            "mesh.invoke",
            ActivityKind.Client);

        var sw = Stopwatch.StartNew();
        var success = false;
        var retryCount = 0;

        // Set activity tags for tracing
        activity?.SetTag("rpc.system", "bannou-mesh");
        activity?.SetTag("rpc.service", appId);
        activity?.SetTag("rpc.method", methodName);
        activity?.SetTag("bannou.mesh.app_id", appId);

        try
        {
            // Check circuit breaker before attempting invocation
            if (_configuration.CircuitBreakerEnabled)
            {
                var state = _circuitBreaker.GetState(appId);
                activity?.SetTag("bannou.mesh.circuit_breaker_state", state.ToString());

                if (state == CircuitState.Open)
                {
                    _logger.LogWarning(
                        "Circuit breaker open for {AppId}, rejecting call to {Method}",
                        appId, methodName);
                    RecordCircuitBreakerStateChange(appId, "open");
                    activity?.SetStatus(ActivityStatusCode.Error, "Circuit breaker open");
                    throw MeshInvocationException.CircuitBreakerOpen(appId, methodName);
                }
            }

            var maxAttempts = _configuration.MaxRetries + 1;
            var delayMs = _configuration.RetryDelayMilliseconds;
            HttpResponseMessage? lastResponse = null;
            Exception? lastException = null;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                // On retry, invalidate cache to potentially get a different endpoint
                if (attempt > 0)
                {
                    retryCount++;
                    _endpointCache.Invalidate(appId);

                    _logger.LogDebug(
                        "Retrying {Method} on {AppId} (attempt {Attempt}/{MaxAttempts}, delay {DelayMs}ms)",
                        methodName, appId, attempt + 1, maxAttempts, delayMs);

                    RecordRetryMetric(appId, methodName, "transient_error");

                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2; // Exponential backoff
                }

                // Resolve endpoint
                var endpoint = await ResolveEndpointAsync(appId, cancellationToken);
                if (endpoint == null)
                {
                    if (attempt < maxAttempts - 1)
                        continue; // Retry - endpoint might become available

                    RecordCircuitBreakerFailure(appId);
                    activity?.SetStatus(ActivityStatusCode.Error, "No endpoints available");
                    throw MeshInvocationException.NoEndpointsAvailable(appId, methodName);
                }

                // Build target URL
                var targetUri = BuildTargetUri(endpoint, methodName);
                request.RequestUri = new Uri(targetUri);
                activity?.SetTag("server.address", endpoint.Host);
                activity?.SetTag("server.port", endpoint.Port);

                if (attempt == 0)
                {
                    _logger.LogDebug(
                        "Invoking {Method} on {AppId} at {TargetUri}",
                        methodName, appId, targetUri);
                }

                try
                {
                    lastResponse = await _httpClient.SendAsync(request, cancellationToken);
                    activity?.SetTag("http.response.status_code", (int)lastResponse.StatusCode);

                    if (!IsTransientError(lastResponse.StatusCode))
                    {
                        // Non-transient response (success or client error) - done
                        RecordCircuitBreakerSuccess(appId);
                        success = lastResponse.IsSuccessStatusCode;
                        if (success)
                        {
                            activity?.SetStatus(ActivityStatusCode.Ok);
                        }
                        else
                        {
                            activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)lastResponse.StatusCode}");
                        }
                        return lastResponse;
                    }

                    // Transient server error - retry if attempts remain
                    _logger.LogDebug(
                        "Transient error {StatusCode} from {AppId}, {Remaining} retries remaining",
                        (int)lastResponse.StatusCode, appId, maxAttempts - attempt - 1);

                    lastException = null;
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    _endpointCache.Invalidate(appId);

                    _logger.LogDebug(
                        ex, "Connection failure to {AppId}, {Remaining} retries remaining",
                        appId, maxAttempts - attempt - 1);
                }
            }

            // All attempts exhausted
            RecordCircuitBreakerFailure(appId);
            activity?.SetTag("bannou.mesh.retry_count", retryCount);

            if (lastException != null)
            {
                _logger.LogWarning(lastException, "Failed to invoke {Method} on {AppId} after {Attempts} attempts",
                    methodName, appId, maxAttempts);
                activity?.SetStatus(ActivityStatusCode.Error, lastException.Message);
                throw new MeshInvocationException(appId, methodName, lastException.Message, lastException);
            }

            // Return the last transient error response
            if (lastResponse != null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)lastResponse.StatusCode}");
                return lastResponse;
            }

            activity?.SetStatus(ActivityStatusCode.Error, "No endpoints available");
            throw MeshInvocationException.NoEndpointsAvailable(appId, methodName);
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("bannou.mesh.retry_count", retryCount);
            RecordInvocationMetrics(appId, methodName, success, retryCount, sw.Elapsed.TotalSeconds);
        }
    }

    /// <summary>
    /// Records a circuit breaker failure if circuit breaking is enabled.
    /// Called once per invocation after all retries are exhausted.
    /// </summary>
    private void RecordCircuitBreakerFailure(string appId)
    {
        if (_configuration.CircuitBreakerEnabled)
        {
            _circuitBreaker.RecordFailure(appId);
        }
    }

    /// <summary>
    /// Records a circuit breaker success if circuit breaking is enabled.
    /// </summary>
    private void RecordCircuitBreakerSuccess(string appId)
    {
        if (_configuration.CircuitBreakerEnabled)
        {
            _circuitBreaker.RecordSuccess(appId);
        }
    }

    /// <summary>
    /// Records metrics for a mesh invocation operation.
    /// </summary>
    private void RecordInvocationMetrics(string appId, string method, bool success, int retryCount, double durationSeconds)
    {
        if (_telemetryProvider == null)
        {
            return;
        }

        var tags = new[]
        {
            new KeyValuePair<string, object?>("service", appId),
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("success", success)
        };

        _telemetryProvider.RecordCounter(TelemetryComponents.Mesh, TelemetryMetrics.MeshInvocations, 1, tags);
        _telemetryProvider.RecordHistogram(TelemetryComponents.Mesh, TelemetryMetrics.MeshDuration, durationSeconds, tags);
    }

    /// <summary>
    /// Records a retry attempt metric.
    /// </summary>
    private void RecordRetryMetric(string appId, string method, string reason)
    {
        if (_telemetryProvider == null)
        {
            return;
        }

        var tags = new[]
        {
            new KeyValuePair<string, object?>("service", appId),
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("reason", reason)
        };

        _telemetryProvider.RecordCounter(TelemetryComponents.Mesh, TelemetryMetrics.MeshRetries, 1, tags);
    }

    /// <summary>
    /// Records a circuit breaker state change metric.
    /// </summary>
    private void RecordCircuitBreakerStateChange(string appId, string state)
    {
        if (_telemetryProvider == null)
        {
            return;
        }

        var tags = new[]
        {
            new KeyValuePair<string, object?>("app_id", appId),
            new KeyValuePair<string, object?>("state", state)
        };

        _telemetryProvider.RecordCounter(TelemetryComponents.Mesh, TelemetryMetrics.MeshCircuitBreakerStateChanges, 1, tags);
    }

    /// <summary>
    /// Determines if an HTTP status code represents a transient error eligible for retry.
    /// Only server errors and specific timeout/throttle codes are retried.
    /// </summary>
    private static bool IsTransientError(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.RequestTimeout => true,          // 408
            HttpStatusCode.TooManyRequests => true,         // 429
            HttpStatusCode.InternalServerError => true,     // 500
            HttpStatusCode.BadGateway => true,              // 502
            HttpStatusCode.ServiceUnavailable => true,      // 503
            HttpStatusCode.GatewayTimeout => true,          // 504
            _ => false
        };
    }

    /// <inheritdoc/>
    public HttpRequestMessage CreateInvokeMethodRequest(
        HttpMethod httpMethod,
        string appId,
        string methodName)
    {

        var request = new HttpRequestMessage(httpMethod, methodName);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Store app-id and method for later resolution
        request.Options.Set(new HttpRequestOptionsKey<string>("mesh-app-id"), appId);
        request.Options.Set(new HttpRequestOptionsKey<string>("mesh-method"), methodName);

        return request;
    }

    /// <inheritdoc/>
    public HttpRequestMessage CreateInvokeMethodRequest<TRequest>(
        HttpMethod httpMethod,
        string appId,
        string methodName,
        TRequest request)
        where TRequest : class
    {
        var httpRequest = CreateInvokeMethodRequest(httpMethod, appId, methodName);

        var jsonBytes = BannouJson.SerializeToUtf8Bytes(request);
        var content = new ByteArrayContent(jsonBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        httpRequest.Content = content;

        return httpRequest;
    }

    /// <inheritdoc/>
    public async Task<bool> IsServiceAvailableAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {

        var endpoint = await ResolveEndpointAsync(appId, cancellationToken);
        return endpoint != null;
    }

    private async Task<MeshEndpoint?> ResolveEndpointAsync(
        string appId,
        CancellationToken cancellationToken)
    {
        // Check cache first
        if (_endpointCache.TryGet(appId, out var cachedEndpoint))
        {
            return cachedEndpoint;
        }

        try
        {
            // Query state manager directly for healthy endpoints (avoids circular dependency with generated MeshClient)
            var endpoints = await _stateManager.GetEndpointsForAppIdAsync(appId, includeUnhealthy: false);

            if (endpoints.Count == 0)
            {
                _logger.LogWarning("No healthy endpoints available for app {AppId}", appId);
                return null;
            }

            // Round-robin selection for load balancing
            var index = Interlocked.Increment(ref _roundRobinCounter) % endpoints.Count;
            var selectedEndpoint = endpoints[index];

            _endpointCache.Set(appId, selectedEndpoint);
            return selectedEndpoint;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve endpoint for app {AppId}", appId);
        }

        return null;
    }

    private static string BuildTargetUri(MeshEndpoint endpoint, string methodName)
    {
        // Ensure method doesn't start with /
        var path = methodName.TrimStart('/');

        // Build URL from endpoint
        var scheme = endpoint.Port == 443 ? "https" : "http";
        return $"{scheme}://{endpoint.Host}:{endpoint.Port}/{path}";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>
    /// Circuit breaker states for mesh endpoints.
    /// </summary>
    private enum CircuitState
    {
        /// <summary>Requests flow normally.</summary>
        Closed,
        /// <summary>Requests are blocked; waiting for reset period to elapse.</summary>
        Open,
        /// <summary>One probe request allowed to test recovery.</summary>
        HalfOpen
    }

    /// <summary>
    /// Per-appId circuit breaker tracking consecutive failures.
    /// Uses ConcurrentDictionary per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private sealed class CircuitBreaker
    {
        private readonly int _threshold;
        private readonly TimeSpan _resetTimeout;
        private readonly ConcurrentDictionary<string, CircuitEntry> _circuits = new();

        public CircuitBreaker(int threshold, TimeSpan resetTimeout)
        {
            _threshold = threshold;
            _resetTimeout = resetTimeout;
        }

        /// <summary>
        /// Gets the current circuit state for an appId.
        /// Transitions from Open to HalfOpen when the reset period has elapsed.
        /// </summary>
        public CircuitState GetState(string appId)
        {
            if (!_circuits.TryGetValue(appId, out var entry))
                return CircuitState.Closed;

            if (entry.State == CircuitState.Open &&
                DateTimeOffset.UtcNow >= entry.OpenedAt + _resetTimeout)
            {
                // Reset period elapsed - allow a probe
                entry.State = CircuitState.HalfOpen;
                return CircuitState.HalfOpen;
            }

            return entry.State;
        }

        /// <summary>
        /// Records a successful invocation. Resets the circuit to Closed.
        /// </summary>
        public void RecordSuccess(string appId)
        {
            if (_circuits.TryGetValue(appId, out var entry))
            {
                entry.ConsecutiveFailures = 0;
                entry.State = CircuitState.Closed;
            }
        }

        /// <summary>
        /// Records a failed invocation. Opens the circuit when threshold is reached.
        /// </summary>
        public void RecordFailure(string appId)
        {
            var entry = _circuits.GetOrAdd(appId, _ => new CircuitEntry());

            entry.ConsecutiveFailures++;

            if (entry.ConsecutiveFailures >= _threshold)
            {
                entry.State = CircuitState.Open;
                entry.OpenedAt = DateTimeOffset.UtcNow;
            }
        }

        private sealed class CircuitEntry
        {
            public int ConsecutiveFailures;
            public CircuitState State = CircuitState.Closed;
            public DateTimeOffset OpenedAt;
        }
    }

    /// <summary>
    /// Simple in-memory cache for endpoint resolution.
    /// </summary>
    private sealed class EndpointCache
    {
        private readonly TimeSpan _ttl;
        private readonly Dictionary<string, (MeshEndpoint Endpoint, DateTimeOffset Expiry)> _cache = new();
        private readonly object _lock = new();

        public EndpointCache(TimeSpan ttl)
        {
            _ttl = ttl;
        }

        public bool TryGet(string appId, out MeshEndpoint? endpoint)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(appId, out var entry))
                {
                    if (entry.Expiry > DateTimeOffset.UtcNow)
                    {
                        endpoint = entry.Endpoint;
                        return true;
                    }

                    _cache.Remove(appId);
                }
            }

            endpoint = null;
            return false;
        }

        public void Set(string appId, MeshEndpoint endpoint)
        {
            lock (_lock)
            {
                _cache[appId] = (endpoint, DateTimeOffset.UtcNow.Add(_ttl));
            }
        }

        public void Invalidate(string appId)
        {
            lock (_lock)
            {
                _cache.Remove(appId);
            }
        }
    }
}
