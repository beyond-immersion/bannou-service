using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Mesh;

/// <summary>
/// Event handler implementations for MeshService.
/// Handles ServiceHeartbeatEvent and FullServiceMappingsEvent subscriptions.
/// </summary>
public partial class MeshService
{
    /// <summary>
    /// Register event consumers for mesh service.
    /// Called from constructor after all dependencies are initialized.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IMeshService, ServiceHeartbeatEvent>(
            "bannou-service-heartbeats",
            async (svc, evt) => await ((MeshService)svc).HandleServiceHeartbeatAsync(evt));

        eventConsumer.RegisterHandler<IMeshService, FullServiceMappingsEvent>(
            "bannou-full-service-mappings",
            async (svc, evt) => await ((MeshService)svc).HandleServiceMappingsAsync(evt));
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
            var existingEndpoint = await _redisManager.GetEndpointByInstanceIdAsync(evt.ServiceId);

            if (existingEndpoint != null)
            {
                // Update heartbeat for existing endpoint
                var status = MapHeartbeatStatus(evt.Status);
                await _redisManager.UpdateHeartbeatAsync(
                    existingEndpoint.InstanceId,
                    evt.AppId,
                    status,
                    evt.Capacity?.CpuUsage ?? 0,
                    evt.Capacity?.CurrentConnections ?? 0,
                    90); // Default TTL

                _logger.LogDebug(
                    "Updated heartbeat for existing endpoint {InstanceId}",
                    existingEndpoint.InstanceId);
            }
            else
            {
                // Auto-register new endpoint from heartbeat
                // This enables automatic discovery without explicit registration
                var instanceId = evt.ServiceId != Guid.Empty ? evt.ServiceId : Guid.NewGuid();
                var endpoint = new MeshEndpoint
                {
                    InstanceId = instanceId,
                    AppId = evt.AppId,
                    Host = evt.AppId, // Use app-id as host for mesh-style routing
                    Port = 80,
                    Status = MapHeartbeatStatus(evt.Status),
                    Services = evt.Services?.Select(s => s.ServiceName).ToList() ?? new List<string>(),
                    MaxConnections = evt.Capacity?.MaxConnections ?? 1000,
                    CurrentConnections = evt.Capacity?.CurrentConnections ?? 0,
                    LoadPercent = evt.Capacity?.CpuUsage ?? 0,
                    RegisteredAt = DateTimeOffset.UtcNow,
                    LastSeen = DateTimeOffset.UtcNow
                };

                await _redisManager.RegisterEndpointAsync(endpoint, 90);

                _logger.LogInformation(
                    "Auto-registered endpoint {InstanceId} for app {AppId} from heartbeat",
                    instanceId, evt.AppId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing service heartbeat from {AppId}", evt.AppId);
        }
    }

    /// <summary>
    /// Handle full service mappings events.
    /// Updates the local service-to-app-id cache atomically.
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
            if (evt.Mappings == null || evt.Mappings.Count == 0)
            {
                _logger.LogWarning("Received empty service mappings event");
                await Task.CompletedTask;
                return;
            }

            var updated = UpdateMappingsCache(
                evt.Mappings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                evt.Version);

            if (updated)
            {
                _logger.LogInformation(
                    "Updated service mappings cache to v{Version} with {Count} mappings",
                    evt.Version,
                    evt.Mappings.Count);

                // Note: Not persisting to Redis here because:
                // - All active instances receive this event and update their local cache
                // - Newly deployed containers fetch mappings via HTTP from source app-id
                // - The orchestrator persists routing data via OrchestratorStateManager
            }
            else
            {
                _logger.LogDebug(
                    "Skipped stale mappings update v{Version} (current: {Current})",
                    evt.Version,
                    _mappingsCacheVersion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing service mappings event");
        }

        await Task.CompletedTask;
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
}
