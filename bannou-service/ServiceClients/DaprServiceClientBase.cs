using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Base class for all Dapr-aware service clients.
/// Handles dynamic app-id resolution and Dapr routing.
/// </summary>
public abstract class DaprServiceClientBase : IDaprClient
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
    /// Should match the service name in the corresponding DaprServiceAttribute.
    /// </summary>
    public virtual string ServiceName => _serviceName ?? GetType().Name.Replace("Client", "").ToLowerInvariant();

    /// <summary>
    /// Parameterless constructor for NSwag generated clients that handle their own dependency injection.
    /// </summary>
    protected DaprServiceClientBase()
    {
    }

    /// <summary>
    /// Full constructor for manual instantiation with all dependencies.
    /// </summary>
    /// <param name="httpClient">HTTP client for making requests.</param>
    /// <param name="appMappingResolver">Resolver for dynamic app-id mapping.</param>
    /// <param name="logger">Logger for tracing service calls.</param>
    /// <param name="serviceName">Name of the target service.</param>
    protected DaprServiceClientBase(
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
    /// Gets the base URL for Dapr service invocation with dynamic app-id resolution.
    /// For parameterless constructor, falls back to "bannou" app-id.
    /// </summary>
    protected string BaseUrl
    {
        get
        {
            // If full dependencies available, use dynamic resolution
            if (_appMappingResolver != null && _serviceName != null)
            {
                var appId = _appMappingResolver.GetAppIdForService(_serviceName);
                var baseUrl = $"http://localhost:3500/v1.0/invoke/{appId}/method/";
                _logger?.LogTrace("Service {ServiceName} routing to app-id {AppId}", _serviceName, appId);
                return baseUrl;
            }

            // Fallback for parameterless constructor - use "bannou" default
            var fallbackUrl = "http://localhost:3500/v1.0/invoke/bannou/method/";
            _logger?.LogTrace("Service {ServiceName} using fallback app-id 'bannou' (parameterless constructor)", ServiceName);
            return fallbackUrl;
        }
    }

    /// <summary>
    /// Prepares the HTTP request with proper Dapr headers.
    /// Works with both full constructor and parameterless constructor.
    /// </summary>
    protected virtual void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
    {
        // Ensure dapr-app-id header is set if not already present
        if (!request.Headers.Contains("dapr-app-id"))
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

            request.Headers.Add("dapr-app-id", appId);
            _logger?.LogTrace("Added dapr-app-id header: {AppId} for URL: {Url}", appId, url);
        }
    }

    /// <summary>
    /// Prepares the HTTP request with URL builder for Dapr routing.
    /// Works with both full constructor and parameterless constructor.
    /// </summary>
    protected virtual void PrepareRequest(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder)
    {
        // Ensure dapr-app-id header is set if not already present
        if (!request.Headers.Contains("dapr-app-id"))
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

            request.Headers.Add("dapr-app-id", appId);
            _logger?.LogTrace("Added dapr-app-id header: {AppId} for URL builder: {Url}", appId, urlBuilder.ToString());
        }
    }

    /// <summary>
    /// Processes the HTTP response from Dapr service invocation.
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

    /// <summary>
    /// Extracts the Dapr app-id from a Dapr invoke URL.
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
