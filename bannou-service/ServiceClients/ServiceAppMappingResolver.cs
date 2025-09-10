using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Default implementation of service-to-app-id mapping with dynamic updates via RabbitMQ.
/// Defaults to "bannou" (omnipotent local node) for all services, but can be dynamically
/// updated based on service discovery events for distributed production deployments.
/// </summary>
public class ServiceAppMappingResolver : IServiceAppMappingResolver
{
    private readonly ConcurrentDictionary<string, string> _serviceMappings = new();
    private readonly ILogger<ServiceAppMappingResolver> _logger;
    private const string DEFAULT_APP_ID = "bannou"; // "almighty" - handles everything locally

    public ServiceAppMappingResolver(ILogger<ServiceAppMappingResolver> logger)
    {
        _logger = logger;
        _logger.LogInformation("ServiceAppMappingResolver initialized with default app-id: {DefaultAppId}", DEFAULT_APP_ID);
    }

    /// <summary>
    /// Gets the Dapr app-id for the specified service.
    /// Defaults to "bannou" but can be overridden by RabbitMQ service mapping events.
    /// </summary>
    public string GetAppIdForService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            _logger.LogWarning("Empty service name provided, defaulting to {DefaultAppId}", DEFAULT_APP_ID);
            return DEFAULT_APP_ID;
        }

        // Check for dynamic mapping first
        if (_serviceMappings.TryGetValue(serviceName, out var mappedAppId))
        {
            _logger.LogTrace("Service {ServiceName} mapped to app-id {AppId}", serviceName, mappedAppId);
            return mappedAppId;
        }

        // Default to "bannou" (omnipotent local node)
        _logger.LogTrace("Service {ServiceName} using default app-id {DefaultAppId}", serviceName, DEFAULT_APP_ID);
        return DEFAULT_APP_ID;
    }

    /// <summary>
    /// Updates service mapping from RabbitMQ service discovery events.
    /// </summary>
    public void UpdateServiceMapping(string serviceName, string appId)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            _logger.LogWarning("Cannot update mapping for empty service name");
            return;
        }

        if (string.IsNullOrWhiteSpace(appId))
        {
            _logger.LogWarning("Cannot update mapping for service {ServiceName} with empty app-id", serviceName);
            return;
        }

        var previousAppId = _serviceMappings.AddOrUpdate(serviceName, appId, (key, oldValue) => appId);

        if (previousAppId != appId)
        {
            _logger.LogInformation("Updated service mapping: {ServiceName} -> {AppId} (was: {PreviousAppId})",
                serviceName, appId, previousAppId);
        }
        else
        {
            _logger.LogDebug("Service mapping confirmed: {ServiceName} -> {AppId}", serviceName, appId);
        }
    }

    /// <summary>
    /// Removes service mapping when service goes offline.
    /// </summary>
    public void RemoveServiceMapping(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            _logger.LogWarning("Cannot remove mapping for empty service name");
            return;
        }

        if (_serviceMappings.TryRemove(serviceName, out var removedAppId))
        {
            _logger.LogInformation("Removed service mapping: {ServiceName} -> {AppId} (reverting to default)",
                serviceName, removedAppId);
        }
        else
        {
            _logger.LogDebug("No mapping found to remove for service {ServiceName}", serviceName);
        }
    }

    /// <summary>
    /// Gets all current service mappings for debugging/monitoring.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllMappings()
    {
        return _serviceMappings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
