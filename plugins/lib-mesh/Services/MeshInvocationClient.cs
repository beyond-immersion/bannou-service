#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
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
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly HttpMessageInvoker _httpClient;

    // Cache for endpoint resolution to reduce state store calls
    private readonly EndpointCache _endpointCache;

    // Distributed circuit breaker state per app-id (shared across instances via Redis)
    private readonly DistributedCircuitBreaker _circuitBreaker;

    // Round-robin counter for load balancing across multiple endpoints
    private int _roundRobinCounter;

    /// <inheritdoc/>
    public Guid InstanceId { get; }

    /// <summary>
    /// Creates a new MeshInvocationClient.
    /// </summary>
    /// <param name="stateManager">State manager for endpoint resolution (avoids circular dependency with generated clients).</param>
    /// <param name="stateStoreFactory">Factory for obtaining Redis operations for distributed circuit breaker.</param>
    /// <param name="messageBus">Message bus for publishing circuit state change events.</param>
    /// <param name="messageSubscriber">Message subscriber for receiving circuit state change events from other instances.</param>
    /// <param name="configuration">Mesh service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for instrumentation (NullTelemetryProvider when telemetry disabled).</param>
    /// <param name="instanceIdentifier">Node identity provider for this mesh instance.</param>
    public MeshInvocationClient(
        IMeshStateManager stateManager,
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        MeshServiceConfiguration configuration,
        ILogger<MeshInvocationClient> logger,
        ITelemetryProvider telemetryProvider,
        IMeshInstanceIdentifier instanceIdentifier)
    {
        _stateManager = stateManager;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        InstanceId = instanceIdentifier.InstanceId;

        if (_telemetryProvider.TracingEnabled || _telemetryProvider.MetricsEnabled)
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

        _endpointCache = new EndpointCache(
            TimeSpan.FromSeconds(configuration.EndpointCacheTtlSeconds),
            configuration.EndpointCacheMaxSize);

        // Create distributed circuit breaker (shares state across instances via Redis + events)
        _circuitBreaker = new DistributedCircuitBreaker(
            stateStoreFactory,
            messageBus,
            logger,
            telemetryProvider,
            configuration.CircuitBreakerThreshold,
            TimeSpan.FromSeconds(configuration.CircuitBreakerResetSeconds));

        // Subscribe to circuit state change events from other instances
        if (configuration.CircuitBreakerEnabled)
        {
            _ = SubscribeToCircuitStateChangesAsync(messageSubscriber);
        }
    }

    /// <summary>
    /// Subscribes to circuit state change events to keep local cache synchronized.
    /// </summary>
    private async Task SubscribeToCircuitStateChangesAsync(IMessageSubscriber messageSubscriber)
    {
        try
        {
            await messageSubscriber.SubscribeAsync<MeshCircuitStateChangedEvent>(
                "mesh.circuit.changed",
                (evt, _) =>
                {
                    _circuitBreaker.HandleStateChangeEvent(evt);
                    return Task.CompletedTask;
                });

            _logger.LogDebug("Subscribed to mesh.circuit.changed events for distributed circuit breaker");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to circuit state change events - distributed sync disabled");
        }
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
        using var activity = _telemetryProvider.StartActivity(
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
                var state = await _circuitBreaker.GetStateAsync(appId, cancellationToken);
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

                    await RecordCircuitBreakerFailureAsync(appId, cancellationToken);
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
                    // Apply per-request timeout (excludes retries)
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.RequestTimeoutSeconds));

                    lastResponse = await _httpClient.SendAsync(request, timeoutCts.Token);
                    activity?.SetTag("http.response.status_code", (int)lastResponse.StatusCode);

                    if (!IsTransientError(lastResponse.StatusCode))
                    {
                        // Non-transient response (success or client error) - done
                        await RecordCircuitBreakerSuccessAsync(appId, cancellationToken);
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
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Request timeout (not external cancellation) - treat as transient error for retry
                    lastException = new TimeoutException(
                        $"Request to {appId}/{methodName} timed out after {_configuration.RequestTimeoutSeconds}s");
                    _endpointCache.Invalidate(appId);
                    activity?.SetTag("bannou.mesh.timeout", true);

                    _logger.LogDebug(
                        "Request timeout to {AppId}/{Method} after {TimeoutSeconds}s, {Remaining} retries remaining",
                        appId, methodName, _configuration.RequestTimeoutSeconds, maxAttempts - attempt - 1);
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
            await RecordCircuitBreakerFailureAsync(appId, cancellationToken);
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
    private async Task RecordCircuitBreakerFailureAsync(string appId, CancellationToken cancellationToken)
    {
        if (_configuration.CircuitBreakerEnabled)
        {
            await _circuitBreaker.RecordFailureAsync(appId, cancellationToken);
        }
    }

    /// <summary>
    /// Records a circuit breaker success if circuit breaking is enabled.
    /// </summary>
    private async Task RecordCircuitBreakerSuccessAsync(string appId, CancellationToken cancellationToken)
    {
        if (_configuration.CircuitBreakerEnabled)
        {
            await _circuitBreaker.RecordSuccessAsync(appId, cancellationToken);
        }
    }

    /// <summary>
    /// Records metrics for a mesh invocation operation.
    /// </summary>
    private void RecordInvocationMetrics(string appId, string method, bool success, int retryCount, double durationSeconds)
    {
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
        var tags = new[]
        {
            new KeyValuePair<string, object?>("app_id", appId),
            new KeyValuePair<string, object?>("state", state)
        };

        _telemetryProvider.RecordCounter(TelemetryComponents.Mesh, TelemetryMetrics.MeshCircuitBreakerStateChanges, 1, tags);
    }

    /// <summary>
    /// Determines if an HTTP status code represents a transient infrastructure error eligible for retry.
    /// Only gateway/proxy errors are retried (502, 503, 504) — these indicate the request likely
    /// never reached the target service or the service was temporarily unavailable.
    /// 500 (Internal Server Error) is NOT retried because the service received and processed the
    /// request — retrying a deterministic bug wastes time and risks duplicate side effects.
    /// 408/429 are NOT retried because they are application-level responses, not infrastructure failures.
    /// Connection failures and timeouts are handled separately via HttpRequestException and
    /// OperationCanceledException catch blocks.
    /// </summary>
    private static bool IsTransientError(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
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

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> InvokeRawAsync(
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

        // Start telemetry activity for this raw mesh invocation (distinct from normal invoke)
        using var activity = _telemetryProvider.StartActivity(
            TelemetryComponents.Mesh,
            "mesh.invoke.raw",
            ActivityKind.Client);

        var sw = Stopwatch.StartNew();
        var success = false;
        var retryCount = 0;

        // Set activity tags for tracing - include raw_api marker
        activity?.SetTag("rpc.system", "bannou-mesh");
        activity?.SetTag("rpc.service", appId);
        activity?.SetTag("rpc.method", methodName);
        activity?.SetTag("bannou.mesh.app_id", appId);
        activity?.SetTag("bannou.mesh.raw_api", true);

        try
        {
            // NOTE: No circuit breaker check - this is intentional for raw API execution
            // where target services may be optional/disabled

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
                        "Retrying raw API {Method} on {AppId} (attempt {Attempt}/{MaxAttempts}, delay {DelayMs}ms)",
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

                    // NOTE: No circuit breaker failure recording for raw API
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
                        "Invoking raw API {Method} on {AppId} at {TargetUri}",
                        methodName, appId, targetUri);
                }

                try
                {
                    // Apply per-request timeout (excludes retries)
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.RequestTimeoutSeconds));

                    lastResponse = await _httpClient.SendAsync(request, timeoutCts.Token);
                    activity?.SetTag("http.response.status_code", (int)lastResponse.StatusCode);

                    if (!IsTransientError(lastResponse.StatusCode))
                    {
                        // Non-transient response (success or client error) - done
                        // NOTE: No circuit breaker success recording for raw API
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
                        "Transient error {StatusCode} from raw API {AppId}, {Remaining} retries remaining",
                        (int)lastResponse.StatusCode, appId, maxAttempts - attempt - 1);

                    lastException = null;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Request timeout (not external cancellation) - treat as transient error for retry
                    lastException = new TimeoutException(
                        $"Raw API request to {appId}/{methodName} timed out after {_configuration.RequestTimeoutSeconds}s");
                    _endpointCache.Invalidate(appId);
                    activity?.SetTag("bannou.mesh.timeout", true);

                    _logger.LogDebug(
                        "Request timeout to raw API {AppId}/{Method} after {TimeoutSeconds}s, {Remaining} retries remaining",
                        appId, methodName, _configuration.RequestTimeoutSeconds, maxAttempts - attempt - 1);
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    _endpointCache.Invalidate(appId);

                    _logger.LogDebug(
                        ex, "Connection failure to raw API {AppId}, {Remaining} retries remaining",
                        appId, maxAttempts - attempt - 1);
                }
            }

            // All attempts exhausted
            // NOTE: No circuit breaker failure recording for raw API
            activity?.SetTag("bannou.mesh.retry_count", retryCount);

            if (lastException != null)
            {
                _logger.LogWarning(lastException, "Failed to invoke raw API {Method} on {AppId} after {Attempts} attempts",
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
            RecordRawInvocationMetrics(appId, methodName, success, retryCount, sw.Elapsed.TotalSeconds);
        }
    }

    /// <summary>
    /// Records metrics for a raw mesh invocation operation (no circuit breaker).
    /// </summary>
    private void RecordRawInvocationMetrics(string appId, string method, bool success, int retryCount, double durationSeconds)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("service", appId),
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("success", success),
            new KeyValuePair<string, object?>("raw_api", true)
        };

        _telemetryProvider.RecordCounter(TelemetryComponents.Mesh, TelemetryMetrics.MeshRawInvocations, 1, tags);
        _telemetryProvider.RecordHistogram(TelemetryComponents.Mesh, TelemetryMetrics.MeshDuration, durationSeconds, tags);
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
    /// Simple in-memory cache for endpoint resolution with optional size limit.
    /// Uses ConcurrentDictionary per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private sealed class EndpointCache
    {
        private readonly TimeSpan _ttl;
        private readonly int _maxSize;
        private readonly ConcurrentDictionary<string, (MeshEndpoint Endpoint, DateTimeOffset Expiry)> _cache = new();

        /// <summary>
        /// Creates a new endpoint cache.
        /// </summary>
        /// <param name="ttl">Time-to-live for cached entries.</param>
        /// <param name="maxSize">Maximum number of entries (0 for unlimited).</param>
        public EndpointCache(TimeSpan ttl, int maxSize = 0)
        {
            _ttl = ttl;
            _maxSize = maxSize;
        }

        public bool TryGet(string appId, out MeshEndpoint? endpoint)
        {
            if (_cache.TryGetValue(appId, out var entry))
            {
                if (entry.Expiry > DateTimeOffset.UtcNow)
                {
                    endpoint = entry.Endpoint;
                    return true;
                }

                // Expired - remove it
                _cache.TryRemove(appId, out _);
            }

            endpoint = null;
            return false;
        }

        public void Set(string appId, MeshEndpoint endpoint)
        {
            // Enforce size limit if configured (0 means unlimited)
            if (_maxSize > 0 && _cache.Count >= _maxSize && !_cache.ContainsKey(appId))
            {
                // Evict expired entries first
                var now = DateTimeOffset.UtcNow;
                foreach (var key in _cache.Keys.ToList())
                {
                    if (_cache.TryGetValue(key, out var entry) && entry.Expiry <= now)
                    {
                        _cache.TryRemove(key, out _);
                    }
                }

                // If still at limit, evict oldest entry
                if (_cache.Count >= _maxSize)
                {
                    var oldestKey = _cache
                        .OrderBy(kv => kv.Value.Expiry)
                        .Select(kv => kv.Key)
                        .FirstOrDefault();

                    if (oldestKey != null)
                    {
                        _cache.TryRemove(oldestKey, out _);
                    }
                }
            }

            _cache[appId] = (endpoint, DateTimeOffset.UtcNow.Add(_ttl));
        }

        public void Invalidate(string appId)
        {
            _cache.TryRemove(appId, out _);
        }
    }
}
