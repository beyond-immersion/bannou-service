using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Mesh;

/// <summary>
/// Event handler implementations for MeshService.
/// Handles ServiceHeartbeatEvent and FullServiceMappingsEvent subscriptions.
/// </summary>
public partial class MeshService
{
    /// <summary>
    /// Cache for degradation event deduplication.
    /// Key = "{instanceId}:{reason}", Value = last publish time.
    /// Static because MeshService is scoped but we want dedup across requests.
    /// Follows lib-state deduplication pattern per IMPLEMENTATION TENETS.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTimeOffset> _degradationEventDeduplicationCache = new();
    /// <summary>
    /// Register event consumers for mesh service.
    /// Called from constructor after all dependencies are initialized.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IMeshService, ServiceHeartbeatEvent>(
            "bannou.service-heartbeat",
            async (svc, evt) => await ((MeshService)svc).HandleServiceHeartbeatAsync(evt));

        if (_configuration.EnableServiceMappingSync)
        {
            eventConsumer.RegisterHandler<IMeshService, FullServiceMappingsEvent>(
                "bannou.full-service-mappings",
                async (svc, evt) => await ((MeshService)svc).HandleServiceMappingsAsync(evt));
        }
    }

    /// <summary>
    /// Handle service heartbeat events.
    /// Updates endpoint status and metrics from heartbeat data.
    /// </summary>
    /// <param name="evt">The service heartbeat event.</param>
    public async Task HandleServiceHeartbeatAsync(ServiceHeartbeatEvent evt)
    {
        _logger.LogDebug(
            "Processing service heartbeat from {AppId} with {ServiceCount} services",
            evt.AppId,
            evt.Services?.Count ?? 0);

        try
        {
            // Check if this instance is already registered
            var existingEndpoint = await _stateManager.GetEndpointByInstanceIdAsync(evt.ServiceId);

            if (existingEndpoint != null)
            {
                // Update heartbeat for existing endpoint
                // Preserve existing issues - event-based heartbeats don't report issues
                var newStatus = MapHeartbeatStatus(evt.Status);
                var previousStatus = existingEndpoint.Status;

                await _stateManager.UpdateHeartbeatAsync(
                    existingEndpoint.InstanceId,
                    evt.AppId,
                    newStatus,
                    evt.Capacity?.CpuUsage ?? 0,
                    evt.Capacity?.CurrentConnections ?? 0,
                    existingEndpoint.Issues,
                    _configuration.EndpointTtlSeconds);

                // Detect and publish degradation transition
                if (newStatus == EndpointStatus.Degraded && previousStatus != EndpointStatus.Degraded)
                {
                    var reason = DetermineDegradationReason(evt, existingEndpoint);
                    await TryPublishDegradationEventAsync(
                        existingEndpoint,
                        previousStatus,
                        newStatus,
                        reason,
                        loadPercent: evt.Capacity?.CpuUsage,
                        lastHeartbeatAt: existingEndpoint.LastSeen);
                }

                _logger.LogDebug(
                    "Updated heartbeat for existing endpoint {InstanceId}",
                    existingEndpoint.InstanceId);
            }
            else
            {
                // Auto-register new endpoint from heartbeat
                // This enables automatic discovery without explicit registration
                var instanceId = evt.ServiceId;
                var endpoint = new MeshEndpoint
                {
                    InstanceId = instanceId,
                    AppId = evt.AppId,
                    Host = evt.AppId, // Use app-id as host for mesh-style routing
                    Port = _configuration.EndpointPort,
                    Status = MapHeartbeatStatus(evt.Status),
                    Services = evt.Services?.Select(s => s.ServiceName).ToList() ?? new List<string>(),
                    MaxConnections = evt.Capacity?.MaxConnections ?? _configuration.DefaultMaxConnections,
                    CurrentConnections = evt.Capacity?.CurrentConnections ?? 0,
                    LoadPercent = evt.Capacity?.CpuUsage ?? 0,
                    RegisteredAt = DateTimeOffset.UtcNow,
                    LastSeen = DateTimeOffset.UtcNow
                };

                await _stateManager.RegisterEndpointAsync(endpoint, _configuration.EndpointTtlSeconds);

                _logger.LogInformation(
                    "Auto-registered endpoint {InstanceId} for app {AppId} from heartbeat",
                    instanceId, evt.AppId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing service heartbeat from {AppId}", evt.AppId);
            await _messageBus.TryPublishErrorAsync(
                "mesh", "HandleServiceHeartbeat", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "event:bannou.service-heartbeat",
                details: new { AppId = evt.AppId }, stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handle full service mappings events.
    /// Updates the shared IServiceAppMappingResolver atomically so all generated clients
    /// (AuthClient, AccountClient, etc.) route to the correct app-ids.
    /// IMPORTANT: Empty mappings events are valid and mean "reset to default routing" -
    /// all services should route to the default app-id ("bannou").
    /// </summary>
    /// <param name="evt">The full service mappings event.</param>
    public async Task HandleServiceMappingsAsync(FullServiceMappingsEvent evt)
    {
        _logger.LogDebug(
            "Processing service mappings update v{Version} with {Count} mappings",
            evt.Version,
            evt.Mappings?.Count ?? 0);

        try
        {
            await Task.CompletedTask; // Async method requires await in success path

            // Convert to dictionary - empty is valid (means reset to default)
            var mappingsDict = evt.Mappings?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ?? new Dictionary<string, string>();

            // Update the shared ServiceAppMappingResolver used by all generated clients.
            // Empty mappings = reset to default routing (all services -> "bannou").
            var updated = _mappingResolver.ReplaceAllMappings(
                mappingsDict,
                evt.DefaultAppId ?? AppConstants.DEFAULT_APP_NAME,
                evt.Version);

            if (updated)
            {
                if (mappingsDict.Count == 0)
                {
                    _logger.LogInformation(
                        "Reset ServiceAppMappingResolver to default routing v{Version} (all services -> {DefaultAppId})",
                        evt.Version,
                        evt.DefaultAppId ?? AppConstants.DEFAULT_APP_NAME);
                }
                else
                {
                    _logger.LogInformation(
                        "Updated ServiceAppMappingResolver to v{Version} with {Count} mappings",
                        evt.Version,
                        mappingsDict.Count);

                    // Log the mappings for debugging
                    var displayLimit = _configuration.MaxServiceMappingsDisplayed;
                    foreach (var mapping in mappingsDict.Take(displayLimit))
                    {
                        _logger.LogDebug("  {Service} -> {AppId}", mapping.Key, mapping.Value);
                    }
                    if (mappingsDict.Count > displayLimit)
                    {
                        _logger.LogDebug("  ... and {Count} more", mappingsDict.Count - displayLimit);
                    }
                }
            }
            else
            {
                _logger.LogDebug(
                    "Skipped stale mappings update v{Version} (current: v{Current})",
                    evt.Version,
                    _mappingResolver.CurrentVersion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing service mappings event");
            await _messageBus.TryPublishErrorAsync(
                "mesh", "HandleServiceMappings", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "event:bannou.full-service-mappings",
                details: new { Version = evt.Version }, stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Map heartbeat status enum to endpoint status enum.
    /// </summary>
    private static EndpointStatus MapHeartbeatStatus(ServiceHeartbeatEventStatus? status)
    {
        return status switch
        {
            ServiceHeartbeatEventStatus.Healthy => EndpointStatus.Healthy,
            ServiceHeartbeatEventStatus.Degraded => EndpointStatus.Degraded,
            ServiceHeartbeatEventStatus.Overloaded => EndpointStatus.Degraded,
            ServiceHeartbeatEventStatus.Unavailable => EndpointStatus.Unavailable,
            ServiceHeartbeatEventStatus.Shutting_down => EndpointStatus.ShuttingDown,
            _ => EndpointStatus.Healthy
        };
    }

    /// <summary>
    /// Determines the reason for degradation based on heartbeat data.
    /// </summary>
    private DegradedReason DetermineDegradationReason(
        ServiceHeartbeatEvent evt,
        MeshEndpoint existingEndpoint)
    {
        // Check for high load first (most specific)
        if (evt.Capacity?.CpuUsage >= _configuration.LoadThresholdPercent)
        {
            return DegradedReason.HighLoad;
        }

        // Check for high connection count
        if (evt.Capacity?.CurrentConnections >= existingEndpoint.MaxConnections)
        {
            return DegradedReason.HighConnectionCount;
        }

        // Default to missed heartbeat (status came from heartbeat status field)
        return DegradedReason.MissedHeartbeat;
    }

    /// <summary>
    /// Publishes a degradation event if transitioning TO Degraded from non-Degraded status.
    /// Follows lib-state deduplication pattern per IMPLEMENTATION TENETS.
    /// </summary>
    private async Task TryPublishDegradationEventAsync(
        MeshEndpoint endpoint,
        EndpointStatus previousStatus,
        EndpointStatus newStatus,
        DegradedReason reason,
        float? loadPercent,
        DateTimeOffset? lastHeartbeatAt)
    {
        // Only publish on transition TO Degraded from non-Degraded
        if (newStatus != EndpointStatus.Degraded || previousStatus == EndpointStatus.Degraded)
        {
            return;
        }

        var dedupKey = $"{endpoint.InstanceId}:{reason}";
        var windowSeconds = _configuration.DegradationEventDeduplicationWindowSeconds;
        var now = DateTimeOffset.UtcNow;

        // Check dedup cache - skip if we published this event recently
        if (_degradationEventDeduplicationCache.TryGetValue(dedupKey, out var lastPublished))
        {
            if (now - lastPublished < TimeSpan.FromSeconds(windowSeconds))
            {
                _logger.LogDebug(
                    "Skipping duplicate degradation event for endpoint {InstanceId} reason {Reason} (last published {Seconds:F1}s ago)",
                    endpoint.InstanceId, reason, (now - lastPublished).TotalSeconds);
                return;
            }
        }

        // Update cache before publishing to prevent races
        _degradationEventDeduplicationCache[dedupKey] = now;

        try
        {
            var evt = new MeshEndpointDegradedEvent
            {
                EventName = "mesh.endpoint_degraded",
                EventId = Guid.NewGuid(),
                Timestamp = now,
                InstanceId = endpoint.InstanceId,
                AppId = endpoint.AppId,
                Reason = reason,
                LoadPercent = loadPercent,
                LastHeartbeatAt = lastHeartbeatAt
            };

            await _messageBus.TryPublishAsync(
                "mesh.endpoint.degraded",
                evt,
                cancellationToken: CancellationToken.None);

            _logger.LogInformation(
                "Published degradation event for endpoint {InstanceId} reason {Reason}",
                endpoint.InstanceId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish degradation event for endpoint {InstanceId}", endpoint.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "mesh", "PublishDegradationEvent", ex.GetType().Name, ex.Message,
                severity: ServiceErrorEventSeverity.Warning,
                cancellationToken: CancellationToken.None);
        }
    }
}
