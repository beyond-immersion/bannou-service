using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Base class for all Bannou-aware service clients.
/// Handles dynamic app-id resolution and Bannou routing.
/// </summary>
public abstract class BannouServiceClientBase : IBannouClient
{
    /// <summary>
    /// HTTP client for making service requests.
    /// </summary>
    protected readonly HttpClient? _httpClient;

    /// <summary>
    /// Resolver for dynamic service-to-app-id mapping.
    /// </summary>
    protected readonly IServiceAppMappingResolver? _appMappingResolver;

    /// <summary>
    /// Logger for tracing service calls.
    /// </summary>
    protected readonly ILogger? _logger;

    /// <summary>
    /// Name of the target service.
    /// </summary>
    protected readonly string? _serviceName;

    /// <summary>
    /// The name of the service this client communicates with.
    /// Should match the service name in the corresponding BannouServiceAttribute.
    /// </summary>
    public virtual string ServiceName => _serviceName ?? GetType().Name.Replace("Client", "").ToLowerInvariant();

    /// <summary>
    /// Parameterless constructor for NSwag generated clients that handle their own dependency injection.
    /// </summary>
    protected BannouServiceClientBase()
    {
    }

    /// <summary>
    /// Full constructor for manual instantiation with all dependencies.
    /// </summary>
    /// <param name="httpClient">HTTP client for making requests.</param>
    /// <param name="appMappingResolver">Resolver for dynamic app-id mapping.</param>
    /// <param name="logger">Logger for tracing service calls.</param>
    /// <param name="serviceName">Name of the target service.</param>
    protected BannouServiceClientBase(
        HttpClient httpClient,
        IServiceAppMappingResolver appMappingResolver,
        ILogger logger,
        string serviceName)
    {
        _httpClient = httpClient;
        _appMappingResolver = appMappingResolver;
        _logger = logger;
        _serviceName = serviceName;
    }

    /// <summary>
    /// Gets the base URL for Bannou service invocation with dynamic app-id resolution.
    /// For parameterless constructor, falls back to "bannou" app-id.
    /// Uses environment variable BANNOU_HTTP_ENDPOINT if available for containerized environments.
    /// </summary>
    /// <remarks>
    /// CRITICAL ARCHITECTURAL CONSTRAINT:
    /// The app-id used here ("bannou" by default) becomes part of the URL path that mesh
    /// forwards to the target service. mesh does NOT strip the /v1.0/invoke/{app-id}/method/
    /// prefix - it preserves the full path.
    ///
    /// This means all OpenAPI schemas MUST use the same app-id in their `servers` URL:
    ///   servers:
    ///     - url: http://localhost:3500/v1.0/invoke/bannou/method
    ///
    /// NSwag generates controller route prefixes from this URL. If the schema uses a different
    /// app-id (e.g., "game-session"), the generated controller route won't match the path
    /// that clients send, resulting in 404 errors.
    ///
    /// The ServiceAppMappingResolver can dynamically route to different meshs, but
    /// the path structure (including app-id) must match what controllers expect.
    ///
    /// See docs/BANNOU_DESIGN.md "servers URL Constraint" section for full documentation.
    /// </remarks>
    protected string BaseUrl
    {
        get
        {
            // Get mesh HTTP endpoint from environment (for container environments)
            // Match ServiceHeartbeatManager pattern: check BANNOU_HTTP_ENDPOINT first, then BANNOU_HTTP_PORT
            var bannouHttpEndpoint = Environment.GetEnvironmentVariable("BANNOU_HTTP_ENDPOINT");
            if (string.IsNullOrEmpty(bannouHttpEndpoint))
            {
                var portStr = Environment.GetEnvironmentVariable("BANNOU_HTTP_PORT");
                var port = int.TryParse(portStr, out var p) && p > 0 ? p : 3500;
                bannouHttpEndpoint = $"http://localhost:{port}";
            }

            // If full dependencies available, use dynamic resolution
            if (_appMappingResolver != null && _serviceName != null)
            {
                var appId = _appMappingResolver.GetAppIdForService(_serviceName);
                var baseUrl = $"{bannouHttpEndpoint}/v1.0/invoke/{appId}/method/";
                _logger?.LogTrace("Service {ServiceName} routing to app-id {AppId} via {BannouEndpoint}", _serviceName, appId, bannouHttpEndpoint);
                return baseUrl;
            }

            // Fallback for parameterless constructor - use "bannou" default
            var fallbackUrl = $"{bannouHttpEndpoint}/v1.0/invoke/bannou/method/";
            _logger?.LogTrace("Service {ServiceName} using fallback app-id 'bannou' via {BannouEndpoint} (parameterless constructor)", ServiceName, bannouHttpEndpoint);
            return fallbackUrl;
        }
    }

    /// <summary>
    /// Prepares the HTTP request with proper routing headers and custom headers.
    /// Works with both full constructor and parameterless constructor.
    /// </summary>
    protected virtual void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
    {
        PrepareRequestCore(request, url);
    }

    /// <summary>
    /// Prepares the HTTP request with URL builder for Bannou routing and custom headers.
    /// Works with both full constructor and parameterless constructor.
    /// </summary>
    protected virtual void PrepareRequest(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder)
    {
        PrepareRequestCore(request, urlBuilder.ToString());
    }

    /// <summary>
    /// Core implementation of request preparation. Applies routing headers, authorization, and custom headers.
    /// </summary>
    /// <param name="request">The HTTP request message to prepare.</param>
    /// <param name="url">The target URL (for logging purposes).</param>
    private void PrepareRequestCore(HttpRequestMessage request, string url)
    {
        // Ensure bannou-app-id header is set if not already present
        if (!request.Headers.Contains("bannou-app-id"))
        {
            string appId;

            // If full dependencies available, use dynamic resolution
            if (_appMappingResolver != null && _serviceName != null)
            {
                appId = _appMappingResolver.GetAppIdForService(_serviceName);
                _logger?.LogTrace("Service {ServiceName} routing to app-id {AppId} (full constructor)", _serviceName, appId);
            }
            else
            {
                // Fallback for parameterless constructor - always use "bannou" (matches BaseUrl)
                appId = "bannou";
                _logger?.LogTrace("Service {ServiceName} using fallback app-id {AppId} (parameterless constructor)", ServiceName, appId);
            }

            request.Headers.Add("bannou-app-id", appId);
            _logger?.LogTrace("Added bannou-app-id header: {AppId} for URL: {Url}", appId, url);
        }

        // Apply authorization header if set
        if (!string.IsNullOrEmpty(_authorizationHeader))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                _authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? _authorizationHeader.Substring(7)
                    : _authorizationHeader);
            _logger?.LogTrace("Added Authorization header for service {ServiceName}", ServiceName);
        }

        // Apply custom headers
        foreach (var header in _customHeaders)
        {
            if (!request.Headers.Contains(header.Key))
            {
                request.Headers.Add(header.Key, header.Value);
                _logger?.LogTrace("Added custom header {HeaderName}: {HeaderValue} for service {ServiceName}",
                    header.Key, header.Value, ServiceName);
            }
        }

        // Clear headers after applying them (one-time use)
        ClearHeaders();
    }

    /// <summary>
    /// Processes the HTTP response from Bannou service invocation.
    /// </summary>
    protected virtual void ProcessResponse(HttpClient client, HttpResponseMessage response)
    {
        // Default implementation - can be overridden for custom response processing
        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning("Service {ServiceName} returned {StatusCode}: {ReasonPhrase}",
                _serviceName, response.StatusCode, response.ReasonPhrase);
        }
    }

    #region Header Support Methods

    /// <summary>
    /// Storage for custom headers to be applied to the next request.
    /// </summary>
    private readonly Dictionary<string, string> _customHeaders = new();

    /// <summary>
    /// Authorization header value for the next request.
    /// </summary>
    private string? _authorizationHeader;

    /// <summary>
    /// Sets a custom header for the next service request.
    /// Headers are applied once and then cleared.
    /// Used by generated clients for type-safe fluent API.
    /// </summary>
    /// <param name="name">Header name (e.g., "X-Custom-Header")</param>
    /// <param name="value">Header value</param>
    public void SetHeader(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Header name cannot be null or empty", nameof(name));

        _customHeaders[name] = value ?? string.Empty;
    }

    /// <summary>
    /// Clears the Authorization header.
    /// Used by generated clients for type-safe fluent API.
    /// </summary>
    public void ClearAuthorization()
    {
        _authorizationHeader = null;
    }

    /// <summary>
    /// Clears all custom headers and authorization for a fresh request.
    /// Called automatically after each request.
    /// </summary>
    protected void ClearHeaders()
    {
        _customHeaders.Clear();
        _authorizationHeader = null;
    }

    #endregion

    /// <summary>
    /// Extracts the app-id from a mesh invoke URL.
    /// URL pattern: http://localhost:3500/v1.0/invoke/{app-id}/method/...
    /// </summary>
    private static string? ExtractAppIdFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var invokePrefix = "/v1.0/invoke/";
        var invokeIndex = url.IndexOf(invokePrefix);
        if (invokeIndex >= 0)
        {
            var startIndex = invokeIndex + invokePrefix.Length;
            var endIndex = url.IndexOf("/method", startIndex);
            if (endIndex > startIndex)
            {
                return url.Substring(startIndex, endIndex - startIndex);
            }
        }
        return null;
    }
}
