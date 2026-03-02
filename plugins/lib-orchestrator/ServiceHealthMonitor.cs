using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
namespace LibOrchestrator;

/// <summary>
/// Monitors service health based on ServiceHeartbeatEvent data from state stores.
/// Uses existing heartbeat schema from common-events.yaml.
/// Writes heartbeat data and service routing to state stores for dynamic routing.
/// Publishes FullServiceMappingsEvent periodically and on routing changes.
/// </summary>
public class ServiceHealthMonitor : IServiceHealthMonitor, IAsyncDisposable
{
    private readonly ILogger<ServiceHealthMonitor> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly IOrchestratorStateManager _stateManager;
    private readonly IOrchestratorEventManager _eventManager;
    private readonly IControlPlaneServiceProvider _controlPlaneProvider;
    private readonly ITelemetryProvider _telemetryProvider;

    // Cache of current service routings to detect changes
    private readonly ConcurrentDictionary<string, ServiceRouting> _currentRoutings = new();

    // Mappings version tracked in Redis via _stateManager.IncrementMappingsVersionAsync()

    // Instance ID for this orchestrator (for source tracking in events)
    private readonly Guid _instanceId;

    // Periodic publication timer
    private Timer? _fullMappingsTimer;
    private readonly TimeSpan _fullMappingsInterval;

    // Periodic timer always publishes — no in-memory routing-change flag needed (IMPLEMENTATION TENETS)

    public ServiceHealthMonitor(
        ILogger<ServiceHealthMonitor> logger,
        OrchestratorServiceConfiguration configuration,
        AppConfiguration appConfiguration,
        IOrchestratorStateManager stateManager,
        IOrchestratorEventManager eventManager,
        IControlPlaneServiceProvider controlPlaneProvider,
        IMeshInstanceIdentifier instanceIdentifier,
        ITelemetryProvider telemetryProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _fullMappingsInterval = TimeSpan.FromSeconds(configuration.FullMappingsIntervalSeconds);
        _appConfiguration = appConfiguration;
        _stateManager = stateManager;
        _eventManager = eventManager;
        _controlPlaneProvider = controlPlaneProvider;
        _instanceId = instanceIdentifier.InstanceId;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;

        // Subscribe to real-time heartbeat events from RabbitMQ
        _eventManager.HeartbeatReceived += OnHeartbeatReceived;

        // Start periodic full mappings publication
        _fullMappingsTimer = new Timer(
            callback: _ => _ = PublishFullMappingsIfNeededAsync(),
            state: null,
            dueTime: _fullMappingsInterval,
            period: _fullMappingsInterval);

        _logger.LogInformation(
            "ServiceHealthMonitor started with {Interval}s full mappings publication interval",
            _fullMappingsInterval.TotalSeconds);
    }

    /// <summary>
    /// Handle real-time heartbeat events from RabbitMQ.
    /// Writes heartbeat to Redis and updates service routing for each service in the heartbeat.
    /// CRITICAL: Filters heartbeats from the control plane (app-id "bannou") to prevent the
    /// orchestrator's own heartbeat from claiming services before deployed nodes can.
    /// </summary>
    private void OnHeartbeatReceived(ServiceHeartbeatEvent heartbeat)
    {
        // CRITICAL: Ignore heartbeats from the control plane itself.
        // The orchestrator runs on "bannou" and publishes heartbeats, but we don't want
        // those heartbeats to initialize service routing. Only explicitly deployed nodes
        // (with different app-ids) should claim services via heartbeats.
        if (string.Equals(heartbeat.AppId, AppConstants.DEFAULT_APP_NAME, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Ignoring heartbeat from control plane (app-id: {AppId})",
                heartbeat.AppId);
            return;
        }

        var serviceNames = heartbeat.Services?.Select(s => s.ServiceName) ?? Enumerable.Empty<string>();
        _logger.LogDebug(
            "Aggregated heartbeat from {AppId}: InstanceId={InstanceId}, Status={Status}, Services=[{Services}]",
            heartbeat.AppId,
            heartbeat.ServiceId,
            heartbeat.Status,
            string.Join(", ", serviceNames));

        // Write heartbeat to Redis (fire and forget, but log errors)
        _ = WriteHeartbeatAndUpdateRoutingAsync(heartbeat);
    }

    /// <summary>
    /// Write heartbeat to Redis and update routing for each service in the heartbeat.
    /// Heartbeats can only initialize routing for new services or update status/load for services
    /// already routed to this app-id. They cannot change the app-id for a service.
    /// </summary>
    private async Task WriteHeartbeatAndUpdateRoutingAsync(ServiceHeartbeatEvent heartbeat)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.WriteHeartbeatAndUpdateRoutingAsync");
        try
        {
            // Write instance heartbeat to state store
            await _stateManager.WriteServiceHeartbeatAsync(heartbeat);

            // Update routing for each service in the aggregated heartbeat
            if (heartbeat.Services == null || heartbeat.Services.Count == 0)
            {
                _logger.LogWarning("Heartbeat from {AppId} contains no services", heartbeat.AppId);
                return;
            }

            var loadPercent = CalculateLoadPercent(heartbeat);
            var routingChanged = false;

            foreach (var serviceStatus in heartbeat.Services)
            {
                var serviceName = serviceStatus.ServiceName;

                // CRITICAL: Never process routing for infrastructure services
                // These must always be handled locally, never delegated to another node
                if (InfrastructureServices.Contains(serviceName))
                {
                    continue;
                }

                // Check if routing already exists for this service
                if (_currentRoutings.TryGetValue(serviceName, out var existingRouting))
                {
                    // Routing exists - only update if heartbeat is from the SAME app-id
                    // This prevents heartbeats from overwriting explicit routing set by orchestrator
                    if (!string.Equals(existingRouting.AppId, heartbeat.AppId, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug(
                            "Ignoring heartbeat for {ServiceName} from {HeartbeatAppId} - service is routed to {RoutedAppId}",
                            serviceName, heartbeat.AppId, existingRouting.AppId);
                        continue;
                    }

                    // Same app-id - update status and load only
                    existingRouting.Status = serviceStatus.Status;
                    existingRouting.LoadPercent = loadPercent;
                    existingRouting.LastUpdated = DateTimeOffset.UtcNow;

                    await _stateManager.WriteServiceRoutingAsync(serviceName, existingRouting);

                    _logger.LogDebug(
                        "Updated health for {ServiceName} on {AppId} (status: {Status}, load: {Load}%)",
                        serviceName, heartbeat.AppId, serviceStatus.Status, loadPercent);
                }
                else
                {
                    // No local cache entry - check Redis first to avoid overwriting orchestrator-managed routing
                    var redisRouting = await _stateManager.GetServiceRoutingAsync(serviceName);
                    if (redisRouting != null)
                    {
                        // Routing exists in Redis (set by another node or orchestrator)
                        // Cache it locally and only update if from same app-id
                        _currentRoutings[serviceName] = redisRouting;

                        if (!string.Equals(redisRouting.AppId, heartbeat.AppId, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug(
                                "Found existing Redis routing for {ServiceName} -> {RoutedAppId}, ignoring heartbeat from {HeartbeatAppId}",
                                serviceName, redisRouting.AppId, heartbeat.AppId);
                            continue;
                        }

                        // Same app-id - update status
                        redisRouting.Status = serviceStatus.Status;
                        redisRouting.LoadPercent = loadPercent;
                        redisRouting.LastUpdated = DateTimeOffset.UtcNow;
                        await _stateManager.WriteServiceRoutingAsync(serviceName, redisRouting);

                        _logger.LogDebug(
                            "Synced routing from Redis for {ServiceName} -> {AppId} (status: {Status})",
                            serviceName, heartbeat.AppId, serviceStatus.Status);
                    }
                    else
                    {
                        // No routing anywhere - heartbeat can initialize it
                        var newRouting = new ServiceRouting
                        {
                            AppId = heartbeat.AppId,
                            Host = heartbeat.AppId, // In Docker, container name = app_id
                            Port = _configuration.DefaultServicePort,
                            Status = serviceStatus.Status,
                            LoadPercent = loadPercent
                        };

                        await _stateManager.WriteServiceRoutingAsync(serviceName, newRouting);
                        _currentRoutings[serviceName] = newRouting;
                        routingChanged = true;

                        _logger.LogInformation(
                            "Initialized routing for {ServiceName} -> {AppId} (status: {Status})",
                            serviceName, heartbeat.AppId, serviceStatus.Status);
                    }
                }
            }

            // If routing changed, publish immediately
            if (routingChanged)
            {
                await PublishFullMappingsAsync("new service routing initialized");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write heartbeat/routing for instance {InstanceId} ({AppId})",
                heartbeat.ServiceId, heartbeat.AppId);
        }
    }

    /// <summary>
    /// Update service routing for a specific service. Called by OrchestratorService during deployment.
    /// This is the authoritative way to set routing - heartbeats cannot override these.
    /// </summary>
    public async Task SetServiceRoutingAsync(string serviceName, string appId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.SetServiceRoutingAsync");
        var routing = new ServiceRouting
        {
            AppId = appId,
            Host = appId,
            Port = _configuration.DefaultServicePort,
            Status = ServiceHealthStatus.Healthy
        };

        await _stateManager.WriteServiceRoutingAsync(serviceName, routing);
        _currentRoutings[serviceName] = routing;
        // Routing change will be picked up by periodic timer publication

        _logger.LogInformation("Set routing for {ServiceName} -> {AppId}", serviceName, appId);
    }

    /// <summary>
    /// Restore service routing to default. Called by OrchestratorService during teardown.
    /// Instead of deleting the routing entry (which causes proxies to fall back to hardcoded defaults),
    /// this sets the routing to the orchestrator's EffectiveAppId.
    /// </summary>
    public async Task RestoreServiceRoutingToDefaultAsync(string serviceName)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.RestoreServiceRoutingToDefaultAsync");
        var defaultAppId = _appConfiguration.EffectiveAppId;

        // Set the routing to the default app-id instead of removing it
        // This ensures routing proxies (like OpenResty) have an explicit route
        // rather than falling back to hardcoded defaults.
        var defaultRouting = new ServiceRouting
        {
            AppId = defaultAppId,
            Host = defaultAppId,
            Port = _configuration.DefaultServicePort,
            Status = ServiceHealthStatus.Healthy,
            LastUpdated = DateTimeOffset.UtcNow
        };

        await _stateManager.WriteServiceRoutingAsync(serviceName, defaultRouting);
        _currentRoutings[serviceName] = defaultRouting;
        // Routing change will be picked up by periodic timer publication

        _logger.LogInformation(
            "Restored routing for {ServiceName} to default app-id '{DefaultAppId}'",
            serviceName, defaultAppId);
    }

    /// <summary>
    /// Reset all service mappings to the orchestrator's effective app-id.
    /// Sets all known service routings to the default app-id (does NOT delete them).
    /// This ensures routing proxies like OpenResty have explicit routes rather than
    /// falling back to hardcoded defaults.
    /// </summary>
    public async Task ResetAllMappingsToDefaultAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.ResetAllMappingsToDefaultAsync");
        try
        {
            // Use the orchestrator's effective app-id (from configuration, not hardcoded constant)
            var defaultAppId = _appConfiguration.EffectiveAppId;

            // Set all service routings to the default app-id (NOT delete them)
            // This ensures OpenResty has explicit routes rather than
            // falling back to hardcoded defaults when routes are missing.
            var updatedServices = await _stateManager.SetAllServiceRoutingsToDefaultAsync(defaultAppId);

            // Update in-memory cache to match - set each service to default routing
            foreach (var serviceName in updatedServices)
            {
                _currentRoutings[serviceName] = new ServiceRouting
                {
                    AppId = defaultAppId,
                    Host = defaultAppId,
                    Port = _configuration.DefaultServicePort,
                    Status = ServiceHealthStatus.Healthy,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            }

            // Mark routing changed and publish immediately
            // Routing change will be picked up by periodic timer publication
            await PublishFullMappingsAsync("reset to default topology");

            _logger.LogInformation(
                "Reset {Count} service mappings to default app-id '{DefaultAppId}'",
                updatedServices.Count, defaultAppId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset all service mappings to default");
            throw;
        }
    }

    /// <summary>
    /// Publish full mappings on periodic timer.
    /// Every routing change already publishes immediately; this is the periodic heartbeat.
    /// </summary>
    private async Task PublishFullMappingsIfNeededAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.PublishFullMappingsIfNeededAsync");
        await PublishFullMappingsAsync("periodic");
    }

    /// <summary>
    /// Infrastructure services that should NEVER be included in routing mappings.
    /// These services must always be handled by the local node, never delegated.
    /// </summary>
    private static readonly HashSet<string> InfrastructureServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "state",
        "messaging",
        "mesh"
    };

    /// <summary>
    /// Publish the complete service mappings to all bannou instances.
    /// </summary>
    public async Task PublishFullMappingsAsync(string reason)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.PublishFullMappingsAsync");
        try
        {
            // Get all current routings from Redis (source of truth)
            var routings = await _stateManager.GetServiceRoutingsAsync();

            // Build mappings dictionary, excluding infrastructure services
            // Infrastructure libs (state, messaging, mesh) must always be handled locally
            var mappings = new Dictionary<string, string>();
            foreach (var routing in routings)
            {
                if (InfrastructureServices.Contains(routing.Key))
                {
                    continue; // Never include infrastructure services in routing
                }
                mappings[routing.Key] = routing.Value.AppId;
            }

            // Increment version via Redis for multi-instance safety (IMPLEMENTATION TENETS)
            var version = await _stateManager.IncrementMappingsVersionAsync();

            var fullMappingsEvent = new FullServiceMappingsEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Mappings = mappings,
                DefaultAppId = _appConfiguration.EffectiveAppId,
                Version = version,
                SourceInstanceId = _instanceId,
                TotalServices = mappings.Count
            };

            await _eventManager.PublishFullMappingsAsync(fullMappingsEvent);

            _logger.LogDebug(
                "Published full mappings v{Version} ({Reason}): {Count} services",
                version, reason, mappings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish full service mappings");
        }
    }

    /// <summary>
    /// Calculate load percentage from heartbeat capacity data.
    /// </summary>
    private static int CalculateLoadPercent(ServiceHeartbeatEvent heartbeat)
    {
        if (heartbeat.Capacity == null || heartbeat.Capacity.MaxConnections == 0)
        {
            return 0;
        }

        // Capacity values are nullable in event schema; use GetValueOrDefault for calculation
        return (int)((double)heartbeat.Capacity.CurrentConnections.GetValueOrDefault() /
                    heartbeat.Capacity.MaxConnections.GetValueOrDefault(1) * 100);
    }

    /// <summary>
    /// Get comprehensive health report for all services (default: all sources).
    /// Reads heartbeat data from Redis and control plane info, evaluates health status.
    /// </summary>
    public async Task<ServiceHealthReport> GetServiceHealthReportAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.GetServiceHealthReportAsync");
        return await GetServiceHealthReportAsync(ServiceHealthSource.All, null);
    }

    /// <summary>
    /// Get comprehensive health report for services based on the specified source filter.
    /// </summary>
    /// <param name="source">Which services to include: all, control_plane_only, or deployed_only</param>
    /// <param name="serviceFilter">Optional filter by service name (applied after source filter)</param>
    /// <returns>Health report with services filtered by the specified source</returns>
    public async Task<ServiceHealthReport> GetServiceHealthReportAsync(ServiceHealthSource source, string? serviceFilter = null)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.GetServiceHealthReportAsync");
        var now = DateTimeOffset.UtcNow;
        var heartbeatTimeout = TimeSpan.FromSeconds(_configuration.HeartbeatTimeoutSeconds);
        var controlPlaneAppId = _controlPlaneProvider.ControlPlaneAppId;

        var healthyServices = new List<ServiceHealthEntry>();
        var unhealthyServices = new List<ServiceHealthEntry>();

        // Get deployed services from Redis heartbeats (if requested)
        if (source == ServiceHealthSource.All || source == ServiceHealthSource.DeployedOnly)
        {
            var deployedHeartbeats = await _stateManager.GetServiceHeartbeatsAsync();

            foreach (var heartbeat in deployedHeartbeats)
            {
                // Apply service name filter if specified
                if (!string.IsNullOrEmpty(serviceFilter) &&
                    !heartbeat.ServiceId.Contains(serviceFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var timeSinceLastHeartbeat = now - heartbeat.LastSeen;
                var isExpired = timeSinceLastHeartbeat > heartbeatTimeout;

                if (isExpired || heartbeat.Status == InstanceHealthStatus.Unavailable)
                {
                    unhealthyServices.Add(heartbeat);
                }
                else
                {
                    healthyServices.Add(heartbeat);
                }
            }

            _logger.LogDebug(
                "Retrieved {Count} deployed service health entries",
                deployedHeartbeats.Count);
        }

        // Get control plane services (if requested)
        if (source == ServiceHealthSource.All || source == ServiceHealthSource.ControlPlaneOnly)
        {
            var controlPlaneServices = _controlPlaneProvider.GetControlPlaneServiceHealth();

            foreach (var service in controlPlaneServices)
            {
                // Apply service name filter if specified
                if (!string.IsNullOrEmpty(serviceFilter) &&
                    !service.ServiceId.Contains(serviceFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // When source=all, check if this service is overridden by a deployed service
                // If a deployed service with a different app-id exists for this service name,
                // the control plane version is still shown (they're separate instances)
                // The consumer can distinguish by comparing appId to controlPlaneAppId

                // Control plane services are always healthy (if enabled, they're running)
                healthyServices.Add(service);
            }

            _logger.LogDebug(
                "Retrieved {Count} control plane service health entries for app-id {AppId}",
                controlPlaneServices.Count, controlPlaneAppId);
        }

        var totalServices = healthyServices.Count + unhealthyServices.Count;
        var healthPercentage = totalServices > 0
            ? (float)healthyServices.Count / totalServices * 100
            : 0.0f;

        _logger.LogDebug(
            "Generated service health report: source={Source}, total={Total}, healthy={Healthy}, unhealthy={Unhealthy}",
            source, totalServices, healthyServices.Count, unhealthyServices.Count);

        return new ServiceHealthReport
        {
            Timestamp = now,
            Source = source,
            ControlPlaneAppId = controlPlaneAppId,
            TotalServices = totalServices,
            HealthPercentage = healthPercentage,
            HealthyServices = healthyServices,
            UnhealthyServices = unhealthyServices
        };
    }

    /// <summary>
    /// Check if a specific service needs restart based on health metrics.
    /// Returns recommendation aligned with smart restart logic (5-minute degradation threshold).
    /// </summary>
    public async Task<RestartRecommendation> ShouldRestartServiceAsync(string serviceName)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.ShouldRestartServiceAsync");
        // Get all heartbeats for this service (may be multiple app-ids)
        var allHeartbeats = await _stateManager.GetServiceHeartbeatsAsync();
        var serviceHeartbeats = allHeartbeats
            .Where(h => h.ServiceId == serviceName)
            .ToList();

        if (!serviceHeartbeats.Any())
        {
            return new RestartRecommendation
            {
                ShouldRestart = true,
                CurrentStatus = InstanceHealthStatus.Unavailable,
                Reason = "No heartbeat data found - service appears to be down"
            };
        }

        // Check the worst status among all instances
        var worstStatus = DetermineWorstStatus(serviceHeartbeats);
        var latestHeartbeat = serviceHeartbeats
            .OrderByDescending(h => h.LastSeen)
            .First();

        var timeSinceLastHeartbeat = DateTimeOffset.UtcNow - latestHeartbeat.LastSeen;
        var degradationThreshold = TimeSpan.FromMinutes(_configuration.DegradationThresholdMinutes);

        // Smart restart logic:
        // - Healthy: No restart
        // - Degraded < 5 min: No restart (transient issue)
        // - Degraded > 5 min: Restart recommended
        // - Unavailable: Restart needed
        bool shouldRestart = false;
        string reason;
        string? degradedDuration = null;

        if (worstStatus == InstanceHealthStatus.Unavailable)
        {
            shouldRestart = true;
            reason = $"Service is {worstStatus}";
        }
        else if (worstStatus == InstanceHealthStatus.Degraded)
        {
            if (timeSinceLastHeartbeat > degradationThreshold)
            {
                shouldRestart = true;
                degradedDuration = timeSinceLastHeartbeat.ToString(@"hh\:mm\:ss");
                reason = $"Service has been {worstStatus} for {degradedDuration} (threshold: {degradationThreshold.TotalMinutes} minutes)";
            }
            else
            {
                shouldRestart = false;
                degradedDuration = timeSinceLastHeartbeat.ToString(@"hh\:mm\:ss");
                reason = $"Service is {worstStatus} but within threshold ({degradedDuration} < {degradationThreshold.TotalMinutes} minutes)";
            }
        }
        else
        {
            shouldRestart = false;
            reason = $"Service is {worstStatus} - no restart needed";
        }

        return new RestartRecommendation
        {
            ShouldRestart = shouldRestart,
            CurrentStatus = worstStatus,
            LastSeen = latestHeartbeat.LastSeen,
            DegradedDuration = degradedDuration, // Nullable per schema - null when not degraded
            Reason = reason
        };
    }

    /// <summary>
    /// Determine the worst status among multiple service instances.
    /// Priority: unavailable > unknown > degraded > healthy
    /// </summary>
    private static InstanceHealthStatus DetermineWorstStatus(List<ServiceHealthEntry> heartbeats)
    {
        var worstStatus = InstanceHealthStatus.Healthy;

        foreach (var heartbeat in heartbeats)
        {
            if (heartbeat.Status > worstStatus)
            {
                worstStatus = heartbeat.Status;
            }
        }

        return worstStatus;
    }

    /// <summary>
    /// Get health status for a specific service instance.
    /// </summary>
    public async Task<ServiceHealthEntry?> GetServiceHealthEntryAsync(string serviceId, string appId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "ServiceHealthMonitor.GetServiceHealthEntryAsync");
        return await _stateManager.GetServiceHeartbeatAsync(serviceId, appId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_fullMappingsTimer != null)
        {
            await _fullMappingsTimer.DisposeAsync();
            _fullMappingsTimer = null;
        }

        _eventManager.HeartbeatReceived -= OnHeartbeatReceived;
    }
}
