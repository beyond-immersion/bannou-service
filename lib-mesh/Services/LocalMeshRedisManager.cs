#nullable enable

using BeyondImmersion.BannouService.Mesh;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Local-only mesh manager for testing and minimal infrastructure scenarios.
/// Does NOT use Redis - all routing is local to the current process.
/// All service calls route to "bannou" (the omnipotent default).
/// </summary>
public sealed class LocalMeshRedisManager : IMeshRedisManager
{
    private readonly ILogger<LocalMeshRedisManager> _logger;
    private readonly MeshEndpoint _localEndpoint;

    public LocalMeshRedisManager(
        MeshServiceConfiguration config,
        ILogger<LocalMeshRedisManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var defaultAppId = config?.DefaultAppId ?? "bannou";

        // Create a local endpoint representing this instance
        // Use the shared Program.ServiceGUID for consistent identification
        _localEndpoint = new MeshEndpoint
        {
            InstanceId = Guid.Parse(Program.ServiceGUID),
            AppId = defaultAppId,
            Host = "localhost",
            Port = 80,
            Status = EndpointStatus.Healthy,
            LoadPercent = 0f,
            CurrentConnections = 0,
            LastSeen = DateTimeOffset.UtcNow
        };

        _logger.LogWarning(
            "LocalMeshRedisManager initialized - all service calls will route locally to '{AppId}'",
            defaultAppId);
    }

    /// <inheritdoc/>
    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Local mesh manager ready (no Redis connection required)");
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<(bool IsHealthy, string? Message, TimeSpan? PingTime)> CheckHealthAsync()
    {
        return Task.FromResult<(bool, string?, TimeSpan?)>((true, "Local routing mode (no Redis)", TimeSpan.Zero));
    }

    /// <inheritdoc/>
    public Task<bool> RegisterEndpointAsync(MeshEndpoint endpoint, int ttlSeconds)
    {
        _logger.LogDebug("Endpoint registration ignored in local mode: {AppId}:{InstanceId}", endpoint.AppId, endpoint.InstanceId);
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<bool> DeregisterEndpointAsync(Guid instanceId, string appId)
    {
        _logger.LogDebug("Endpoint deregistration ignored in local mode: {AppId}:{InstanceId}", appId, instanceId);
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<bool> UpdateHeartbeatAsync(
        Guid instanceId,
        string appId,
        EndpointStatus status,
        float loadPercent,
        int currentConnections,
        int ttlSeconds)
    {
        _logger.LogDebug("Heartbeat ignored in local mode: {AppId}:{InstanceId}", appId, instanceId);
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<List<MeshEndpoint>> GetEndpointsForAppIdAsync(string appId, bool includeUnhealthy = false)
    {
        // Always return the local endpoint - all routing goes local
        _logger.LogDebug("Returning local endpoint for app-id '{AppId}' (local mode)", appId);
        return Task.FromResult(new List<MeshEndpoint> { _localEndpoint });
    }

    /// <inheritdoc/>
    public Task<List<MeshEndpoint>> GetAllEndpointsAsync(string? appIdPrefix = null)
    {
        return Task.FromResult(new List<MeshEndpoint> { _localEndpoint });
    }

    /// <inheritdoc/>
    public Task<MeshEndpoint?> GetEndpointByInstanceIdAsync(Guid instanceId)
    {
        // If looking for our local instance, return it
        if (instanceId == _localEndpoint.InstanceId)
        {
            return Task.FromResult<MeshEndpoint?>(_localEndpoint);
        }

        // Otherwise, still return the local endpoint (all routing goes local)
        _logger.LogDebug("Instance {InstanceId} not found in local mode, returning local endpoint", instanceId);
        return Task.FromResult<MeshEndpoint?>(_localEndpoint);
    }

    public void Dispose()
    {
        _logger.LogDebug("Local mesh manager disposed");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
