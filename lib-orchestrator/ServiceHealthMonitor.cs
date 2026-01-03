using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

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
    private readonly IOrchestratorStateManager _stateManager;
    private readonly IOrchestratorEventManager _eventManager;

    // Cache of current service routings to detect changes
    private readonly ConcurrentDictionary<string, ServiceRouting> _currentRoutings = new();

    // Version tracking for full mappings events
    private long _mappingsVersion = 0;

    // Instance ID for this orchestrator (for source tracking in events)
    // Uses the shared Program.ServiceGUID for consistent identification
    private Guid _instanceId => Guid.Parse(Program.ServiceGUID);

    // Periodic publication timer
    private Timer? _fullMappingsTimer;
    private readonly TimeSpan _fullMappingsInterval = TimeSpan.FromSeconds(30);

    // Track if routing changed since last publication
    private bool _routingChanged = false;
    private readonly object _routingChangeLock = new();

    public ServiceHealthMonitor(
        ILogger<ServiceHealthMonitor> logger,
        OrchestratorServiceConfiguration configuration,
        IOrchestratorStateManager stateManager,
        IOrchestratorEventManager eventManager)
    {
        _logger = logger;
        _configuration = configuration;
        _stateManager = stateManager;
        _eventManager = eventManager;

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
    /// </summary>
    private void OnHeartbeatReceived(ServiceHeartbeatEvent heartbeat)
    {
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
                    existingRouting.Status = serviceStatus.Status.ToString().ToLowerInvariant();
                    existingRouting.LoadPercent = loadPercent;
                    existingRouting.LastUpdated = DateTimeOffset.UtcNow;

                    await _stateManager.WriteServiceRoutingAsync(serviceName, existingRouting);

                    _logger.LogDebug(
                        "Updated health for {ServiceName} on {AppId} (status: {Status}, load: {Load}%)",
                        serviceName, heartbeat.AppId, serviceStatus.Status, loadPercent);
                }
                else
                {
                    // No existing routing - heartbeat can initialize it
                    var newRouting = new ServiceRouting
                    {
                        AppId = heartbeat.AppId,
                        Host = heartbeat.AppId, // In Docker, container name = app_id
                        Port = 80,
                        Status = serviceStatus.Status.ToString().ToLowerInvariant(),
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

            // If routing changed, mark for publication and publish immediately
            if (routingChanged)
            {
                MarkRoutingChanged();
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
        var routing = new ServiceRouting
        {
            AppId = appId,
            Host = appId,
            Port = 80,
            Status = "healthy"
        };

        await _stateManager.WriteServiceRoutingAsync(serviceName, routing);
        _currentRoutings[serviceName] = routing;
        MarkRoutingChanged();

        _logger.LogInformation("Set routing for {ServiceName} -> {AppId}", serviceName, appId);
    }

    /// <summary>
    /// Remove service routing. Called by OrchestratorService during teardown.
    /// </summary>
    public async Task RemoveServiceRoutingAsync(string serviceName)
    {
        await _stateManager.RemoveServiceRoutingAsync(serviceName);
        _currentRoutings.TryRemove(serviceName, out _);
        MarkRoutingChanged();

        _logger.LogInformation("Removed routing for {ServiceName}", serviceName);
    }

    /// <summary>
    /// Reset all service mappings to default ("bannou").
    /// Clears all custom routing from Redis and in-memory cache, then publishes updated mappings.
    /// </summary>
    public async Task ResetAllMappingsToDefaultAsync()
    {
        try
        {
            // Clear all service-specific routings from Redis
            await _stateManager.ClearAllServiceRoutingsAsync();

            // Clear in-memory cache
            _currentRoutings.Clear();

            // Mark routing changed and publish immediately
            MarkRoutingChanged();
            await PublishFullMappingsAsync("reset to default topology");

            _logger.LogInformation("Reset all service mappings to default - all services now route to 'bannou'");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset all service mappings to default");
            throw;
        }
    }

    /// <summary>
    /// Mark that routing has changed, triggering immediate publication.
    /// </summary>
    private void MarkRoutingChanged()
    {
        lock (_routingChangeLock)
        {
            _routingChanged = true;
        }
    }

    /// <summary>
    /// Publish full mappings if routing changed or periodic timer fired.
    /// </summary>
    private async Task PublishFullMappingsIfNeededAsync()
    {
        bool shouldPublish;
        lock (_routingChangeLock)
        {
            shouldPublish = _routingChanged;
            _routingChanged = false;
        }

        // Always publish on timer (periodic heartbeat), but only increment version if changed
        await PublishFullMappingsAsync(shouldPublish ? "routing changed" : "periodic");
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

            // Increment version
            var version = Interlocked.Increment(ref _mappingsVersion);

            var fullMappingsEvent = new FullServiceMappingsEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Mappings = mappings,
                DefaultAppId = AppConstants.DEFAULT_APP_NAME,
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

        return (int)((double)heartbeat.Capacity.CurrentConnections /
                    heartbeat.Capacity.MaxConnections * 100);
    }

    /// <summary>
    /// Get comprehensive health report for all services.
    /// Reads heartbeat data from Redis and evaluates health status.
    /// </summary>
    public async Task<ServiceHealthReport> GetServiceHealthReportAsync()
    {
        var heartbeats = await _stateManager.GetServiceHeartbeatsAsync();
        var now = DateTimeOffset.UtcNow;
        var heartbeatTimeout = TimeSpan.FromSeconds(_configuration.HeartbeatTimeoutSeconds);

        var healthyServices = new List<ServiceHealthStatus>();
        var unhealthyServices = new List<ServiceHealthStatus>();

        foreach (var heartbeat in heartbeats)
        {
            var timeSinceLastHeartbeat = now - heartbeat.LastSeen;
            var isExpired = timeSinceLastHeartbeat > heartbeatTimeout;

            if (isExpired || heartbeat.Status == "unavailable" || heartbeat.Status == "shutting_down")
            {
                unhealthyServices.Add(heartbeat);
            }
            else
            {
                healthyServices.Add(heartbeat);
            }
        }

        var totalServices = healthyServices.Count + unhealthyServices.Count;
        var healthPercentage = totalServices > 0
            ? (float)healthyServices.Count / totalServices * 100
            : 0.0f;

        return new ServiceHealthReport
        {
            Timestamp = now,
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
                ServiceName = serviceName,
                CurrentStatus = "unavailable",
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

        if (worstStatus == "unavailable" || worstStatus == "shutting_down")
        {
            shouldRestart = true;
            reason = $"Service is {worstStatus}";
        }
        else if (worstStatus == "degraded" || worstStatus == "overloaded")
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
            ServiceName = serviceName,
            CurrentStatus = worstStatus,
            LastSeen = latestHeartbeat.LastSeen,
            DegradedDuration = degradedDuration ?? string.Empty,
            Reason = reason
        };
    }

    /// <summary>
    /// Determine the worst status among multiple service instances.
    /// Priority: unavailable > shutting_down > overloaded > degraded > healthy
    /// </summary>
    private string DetermineWorstStatus(List<ServiceHealthStatus> heartbeats)
    {
        var statusPriority = new Dictionary<string, int>
        {
            { "unavailable", 5 },
            { "shutting_down", 4 },
            { "overloaded", 3 },
            { "degraded", 2 },
            { "healthy", 1 }
        };

        var worstStatus = "healthy";
        var worstPriority = 0;

        foreach (var heartbeat in heartbeats)
        {
            var status = heartbeat.Status ?? "unavailable";
            if (statusPriority.TryGetValue(status, out var priority))
            {
                if (priority > worstPriority)
                {
                    worstPriority = priority;
                    worstStatus = status;
                }
            }
        }

        return worstStatus;
    }

    /// <summary>
    /// Get health status for a specific service instance.
    /// </summary>
    public async Task<ServiceHealthStatus?> GetServiceHealthStatusAsync(string serviceId, string appId)
    {
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
