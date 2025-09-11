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
    protected readonly HttpClient _httpClient;
    protected readonly IServiceAppMappingResolver _appMappingResolver;
    protected readonly ILogger _logger;
    protected readonly string _serviceName;

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
    /// </summary>
    protected string BaseUrl
    {
        get
        {
            var appId = _appMappingResolver.GetAppIdForService(_serviceName);
            var baseUrl = $"http://localhost:3500/v1.0/invoke/{appId}/method";

            _logger.LogTrace("Service {ServiceName} routing to app-id {AppId}", _serviceName, appId);
            return baseUrl;
        }
    }

    /// <summary>
    /// Prepares the HTTP request with proper Dapr headers.
    /// </summary>
    protected virtual void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
    {
        var appId = _appMappingResolver.GetAppIdForService(_serviceName);
        request.Headers.Add("dapr-app-id", appId);
    }

    /// <summary>
    /// Prepares the HTTP request with URL builder for Dapr routing.
    /// </summary>
    protected virtual void PrepareRequest(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder)
    {
        var appId = _appMappingResolver.GetAppIdForService(_serviceName);
        request.Headers.Add("dapr-app-id", appId);

        // Replace the URL to use proper Dapr routing
        var originalUrl = urlBuilder.ToString();
        var daprUrl = $"http://localhost:3500/v1.0/invoke/{appId}/method{originalUrl}";
        urlBuilder.Clear().Append(daprUrl);
    }

    /// <summary>
    /// Processes the HTTP response from Dapr service invocation.
    /// </summary>
    protected virtual void ProcessResponse(HttpClient client, HttpResponseMessage response)
    {
        // Default implementation - can be overridden for custom response processing
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Service {ServiceName} returned {StatusCode}: {ReasonPhrase}",
                _serviceName, response.StatusCode, response.ReasonPhrase);
        }
    }
}
