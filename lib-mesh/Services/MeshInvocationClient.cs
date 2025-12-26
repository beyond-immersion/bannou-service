#nullable enable

using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// HTTP-based implementation of mesh service invocation.
/// Uses IMeshRedisManager directly for endpoint resolution to avoid circular dependencies.
/// This is infrastructure that all generated clients depend on, so it cannot use generated clients.
/// </summary>
public sealed class MeshInvocationClient : IMeshInvocationClient, IDisposable
{
    private readonly IMeshRedisManager _redisManager;
    private readonly ILogger<MeshInvocationClient> _logger;
    private readonly HttpMessageInvoker _httpClient;

    // Cache for endpoint resolution to reduce Redis calls
    private readonly EndpointCache _endpointCache;

    // Round-robin counter for load balancing across multiple endpoints
    private int _roundRobinCounter;

    /// <summary>
    /// Creates a new MeshInvocationClient.
    /// </summary>
    /// <param name="redisManager">Redis manager for direct endpoint resolution (avoids circular dependency with generated clients).</param>
    /// <param name="logger">Logger instance.</param>
    public MeshInvocationClient(
        IMeshRedisManager redisManager,
        ILogger<MeshInvocationClient> logger)
    {
        _redisManager = redisManager ?? throw new ArgumentNullException(nameof(redisManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create HTTP client for outbound requests
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        _httpClient = new HttpMessageInvoker(handler);

        _endpointCache = new EndpointCache(TimeSpan.FromSeconds(5));
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
        ArgumentNullException.ThrowIfNull(appId, nameof(appId));
        ArgumentNullException.ThrowIfNull(methodName, nameof(methodName));
        ArgumentNullException.ThrowIfNull(request, nameof(request));

        var httpRequest = CreateInvokeMethodRequest(HttpMethod.Post, appId, methodName, request);

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
        ArgumentNullException.ThrowIfNull(appId, nameof(appId));
        ArgumentNullException.ThrowIfNull(methodName, nameof(methodName));
        ArgumentNullException.ThrowIfNull(request, nameof(request));

        var httpRequest = CreateInvokeMethodRequest(HttpMethod.Post, appId, methodName, request);

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
        ArgumentNullException.ThrowIfNull(appId, nameof(appId));
        ArgumentNullException.ThrowIfNull(methodName, nameof(methodName));

        var httpRequest = CreateInvokeMethodRequest(HttpMethod.Get, appId, methodName);

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
        ArgumentNullException.ThrowIfNull(request, nameof(request));

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

        // Resolve endpoint
        var endpoint = await ResolveEndpointAsync(appId, cancellationToken) ?? throw MeshInvocationException.NoEndpointsAvailable(appId, methodName);

        // Build target URL
        var targetUri = BuildTargetUri(endpoint, methodName);
        request.RequestUri = new Uri(targetUri);

        _logger.LogDebug(
            "Invoking {Method} on {AppId} at {TargetUri}",
            methodName, appId, targetUri);

        try
        {
            // Send request directly using the HTTP client
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to invoke {Method} on {AppId}", methodName, appId);

            // Invalidate cached endpoint on connection failure
            _endpointCache.Invalidate(appId);

            throw new MeshInvocationException(appId, methodName, ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public HttpRequestMessage CreateInvokeMethodRequest(
        HttpMethod httpMethod,
        string appId,
        string methodName)
    {
        ArgumentNullException.ThrowIfNull(httpMethod, nameof(httpMethod));
        ArgumentNullException.ThrowIfNull(appId, nameof(appId));
        ArgumentNullException.ThrowIfNull(methodName, nameof(methodName));

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
        ArgumentNullException.ThrowIfNull(appId, nameof(appId));

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
            // Query Redis directly for healthy endpoints (avoids circular dependency with generated MeshClient)
            var endpoints = await _redisManager.GetEndpointsForAppIdAsync(appId, includeUnhealthy: false);

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
