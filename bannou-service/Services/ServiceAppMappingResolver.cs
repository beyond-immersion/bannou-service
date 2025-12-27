using BeyondImmersion.BannouService.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Default implementation of service-to-app-id mapping with dynamic updates via RabbitMQ.
/// Defaults to "bannou" (omnipotent local node) for all services, but can be dynamically
/// updated based on service discovery events for distributed production deployments.
/// Supports atomic full-state updates from Orchestrator's FullServiceMappingsEvent.
/// </summary>
public class ServiceAppMappingResolver : IServiceAppMappingResolver
{
    // Static so mappings are shared across any accidentally duplicated DI containers (plugins vs host).
    private static readonly ConcurrentDictionary<string, string> _serviceMappings = new();
    private static long _currentVersion = 0;
    private static readonly object _versionLock = new();
    private readonly ILogger<ServiceAppMappingResolver> _logger;

    /// <summary>
    /// The local app-id for this node. Infrastructure services always route here.
    /// </summary>
    private readonly string _localAppId;

    /// <summary>
    /// Infrastructure services that must ALWAYS be handled locally, never routed to other nodes.
    /// These provide the communication and storage fabric that all other services depend on.
    /// </summary>
    private static readonly HashSet<string> InfrastructureServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "state",
        "messaging",
        "mesh"
    };

    /// <summary>
    /// Checks if a service is an infrastructure service that must always be local.
    /// </summary>
    private static bool IsInfrastructureService(string serviceName) =>
        InfrastructureServices.Contains(serviceName);

    /// <inheritdoc/>
    public event EventHandler<ServiceMappingChangedEventArgs>? MappingChanged;

    /// <inheritdoc/>
    public long CurrentVersion => _currentVersion;

    /// <inheritdoc/>
    public ServiceAppMappingResolver(ILogger<ServiceAppMappingResolver> logger, AppConfiguration configuration)
    {
        _logger = logger;
        _localAppId = configuration.EffectiveAppId;
        _logger.LogInformation("ServiceAppMappingResolver initialized: local={LocalAppId}, default={DefaultAppId}",
            _localAppId, AppConstants.DEFAULT_APP_NAME);
    }

    /// <summary>
    /// Gets the app-id for the specified service.
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

        // Infrastructure services (state, messaging, mesh) must ALWAYS be handled locally.
        // These services cannot be delegated to other nodes - they provide the communication
        // and storage fabric that everything else depends on.
        if (IsInfrastructureService(serviceName))
        {
            _logger.LogTrace("Infrastructure service {ServiceName} forced to local app-id {LocalAppId}", serviceName, _localAppId);
            return _localAppId;
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

    /// <summary>
    /// Atomically replaces all service mappings with a new full state from Orchestrator.
    /// Implements version checking to reject out-of-order events.
    /// </summary>
    public bool ReplaceAllMappings(IReadOnlyDictionary<string, string> mappings, string defaultAppId, long version)
    {
        if (mappings == null)
        {
            _logger.LogWarning("Received null mappings in ReplaceAllMappings");
            return false;
        }

        // Declare outside lock so we can notify after releasing lock
        List<ServiceMappingChangedEventArgs> changedServices;

        // Version check with lock to prevent race conditions
        lock (_versionLock)
        {
            if (version <= _currentVersion)
            {
                _logger.LogDebug(
                    "Rejecting stale full mappings event v{Version} (current: v{CurrentVersion})",
                    version, _currentVersion);
                return false;
            }

            // Collect changes for event notification
            var previousMappings = _serviceMappings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            changedServices = new List<ServiceMappingChangedEventArgs>();

            // Find services that were removed
            foreach (var oldMapping in previousMappings)
            {
                if (!mappings.ContainsKey(oldMapping.Key))
                {
                    changedServices.Add(new ServiceMappingChangedEventArgs
                    {
                        ServiceName = oldMapping.Key,
                        NewAppId = null, // Removed - reverts to default
                        PreviousAppId = oldMapping.Value
                    });
                }
            }

            // Clear and replace atomically
            _serviceMappings.Clear();

            foreach (var kvp in mappings)
            {
                // Skip internal/info keys
                if (kvp.Key.StartsWith("_"))
                    continue;

                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    _serviceMappings[kvp.Key] = kvp.Value;

                    // Check if this is a new or changed mapping
                    if (!previousMappings.TryGetValue(kvp.Key, out var previousAppId) || previousAppId != kvp.Value)
                    {
                        changedServices.Add(new ServiceMappingChangedEventArgs
                        {
                            ServiceName = kvp.Key,
                            NewAppId = kvp.Value,
                            PreviousAppId = previousAppId
                        });
                    }
                }
            }

            _currentVersion = version;

            _logger.LogInformation(
                "Applied full mappings v{Version}: {Count} services ({ChangedCount} changes)",
                version, mappings.Count, changedServices.Count);
        }

        // Notify listeners of changes OUTSIDE the lock to prevent deadlocks
        foreach (var change in changedServices)
        {
            MappingChanged?.Invoke(this, change);
        }

        return true;
    }
}
