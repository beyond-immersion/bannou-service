using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Base class for all Dapr-aware service clients.
/// Handles dynamic app-id resolution and Dapr routing.
/// </summary>
public abstract class DaprServiceClientBase
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
    /// Only available when using the full constructor.
    /// </summary>
    protected string BaseUrl
    {
        get
        {
            if (_appMappingResolver == null || _serviceName == null)
                throw new InvalidOperationException("BaseUrl is only available when using the full constructor with all dependencies");

            var appId = _appMappingResolver.GetAppIdForService(_serviceName);
            var baseUrl = $"http://localhost:3500/v1.0/invoke/{appId}/method";

            _logger?.LogTrace("Service {ServiceName} routing to app-id {AppId}", _serviceName, appId);
            return baseUrl;
        }
    }

    /// <summary>
    /// Prepares the HTTP request with proper Dapr headers.
    /// Only functional when using the full constructor.
    /// </summary>
    protected virtual void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
    {
        if (_appMappingResolver != null && _serviceName != null)
        {
            var appId = _appMappingResolver.GetAppIdForService(_serviceName);
            request.Headers.Add("dapr-app-id", appId);
        }
    }

    /// <summary>
    /// Prepares the HTTP request with URL builder for Dapr routing.
    /// Only functional when using the full constructor.
    /// </summary>
    protected virtual void PrepareRequest(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder)
    {
        if (_appMappingResolver != null && _serviceName != null)
        {
            var appId = _appMappingResolver.GetAppIdForService(_serviceName);
            request.Headers.Add("dapr-app-id", appId);

            // Replace the URL to use proper Dapr routing
            var originalUrl = urlBuilder.ToString();
            var daprUrl = $"http://localhost:3500/v1.0/invoke/{appId}/method{originalUrl}";
            urlBuilder.Clear().Append(daprUrl);
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
}
