using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Default implementation of service-to-app-id mapping with dynamic updates via RabbitMQ.
/// Defaults to "bannou" (omnipotent local node) for all services, but can be dynamically
/// updated based on service discovery events for distributed production deployments.
/// </summary>
public class ServiceAppMappingResolver : IServiceAppMappingResolver
{
    // Static so mappings are shared across any accidentally duplicated DI containers (plugins vs host).
    private static readonly ConcurrentDictionary<string, string> _serviceMappings = new();
    private readonly ILogger<ServiceAppMappingResolver> _logger;

    /// <inheritdoc/>
    public event EventHandler<ServiceMappingChangedEventArgs>? MappingChanged;

    /// <inheritdoc/>
    public ServiceAppMappingResolver(ILogger<ServiceAppMappingResolver> logger)
    {
        _logger = logger;
        _logger.LogInformation("ServiceAppMappingResolver initialized with default app-id: {DefaultAppId}", AppConstants.DEFAULT_APP_NAME);
    }

    /// <summary>
    /// Gets the Dapr app-id for the specified service.
    /// Defaults to "bannou" but can be overridden by RabbitMQ service mapping events.
    /// </summary>
    public string GetAppIdForService(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            _logger.LogWarning("Empty service name provided, defaulting to {DefaultAppId}", AppConstants.DEFAULT_APP_NAME);
            return AppConstants.DEFAULT_APP_NAME;
        }

        // Orchestrator is control-plane only and must always run on the default app-id.
        if (string.Equals(serviceName, AppConstants.ORCHESTRATOR_SERVICE_NAME, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogTrace("Service {ServiceName} forced to control-plane app-id {DefaultAppId}", serviceName, AppConstants.DEFAULT_APP_NAME);
            return AppConstants.DEFAULT_APP_NAME;
        }

        // Check for dynamic mapping first
        if (_serviceMappings.TryGetValue(serviceName, out var mappedAppId))
        {
            _logger.LogTrace("Service {ServiceName} mapped to app-id {AppId}", serviceName, mappedAppId);
            return mappedAppId;
        }

        // Default to "bannou" (omnipotent local node)
        _logger.LogTrace("Service {ServiceName} using default app-id {DefaultAppId}", serviceName, AppConstants.DEFAULT_APP_NAME);
        return AppConstants.DEFAULT_APP_NAME;
    }

    /// <summary>
    /// Clears all mappings. Intended for test isolation.
    /// </summary>
    internal static void ClearAllMappingsForTests()
    {
        _serviceMappings.Clear();
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

        _serviceMappings.TryGetValue(serviceName, out var previousAppId);

        _serviceMappings.AddOrUpdate(serviceName, appId, (key, oldValue) => appId);

        if (previousAppId != appId)
        {
            _logger.LogInformation("Updated service mapping: {ServiceName} -> {AppId} (was: {PreviousAppId})",
                serviceName, appId, previousAppId ?? "default");

            // Raise event to notify listeners (e.g., ServiceHeartbeatManager)
            MappingChanged?.Invoke(this, new ServiceMappingChangedEventArgs
            {
                ServiceName = serviceName,
                NewAppId = appId,
                PreviousAppId = previousAppId
            });
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

            // Raise event to notify listeners - service reverts to default routing
            MappingChanged?.Invoke(this, new ServiceMappingChangedEventArgs
            {
                ServiceName = serviceName,
                NewAppId = null, // null means reverted to default
                PreviousAppId = removedAppId
            });
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

    /// <summary>
    /// Imports multiple service mappings at once, typically from a source container.
    /// Used during startup of containers deployed by orchestrator.
    /// </summary>
    /// <param name="mappings">The mappings to import</param>
    public void ImportMappings(IReadOnlyDictionary<string, string> mappings)
    {
        if (mappings == null || mappings.Count == 0)
        {
            _logger.LogDebug("No mappings to import");
            return;
        }

        var importedCount = 0;
        foreach (var kvp in mappings)
        {
            // Skip internal/info keys
            if (kvp.Key.StartsWith("_"))
                continue;

            if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            {
                _serviceMappings[kvp.Key] = kvp.Value;
                importedCount++;
                _logger.LogDebug("Imported mapping: {ServiceName} -> {AppId}", kvp.Key, kvp.Value);
            }
        }

        _logger.LogInformation("Imported {Count} service mappings from source", importedCount);
    }
}
