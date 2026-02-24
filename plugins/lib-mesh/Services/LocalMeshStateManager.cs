#nullable enable

using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Local-only mesh state manager for testing and minimal infrastructure scenarios.
/// Does NOT use Redis or lib-state - all routing is local to the current process.
/// All service calls route to "bannou" (the omnipotent default).
/// </summary>
public sealed class LocalMeshStateManager : IMeshStateManager
{
    private readonly ILogger<LocalMeshStateManager> _logger;
    private readonly MeshEndpoint _localEndpoint;

    /// <summary>
    /// Creates a LocalMeshStateManager for local-only routing.
    /// </summary>
    /// <param name="config">Mesh service configuration (unused but required for DI consistency).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="instanceIdentifier">Node identity provider for this mesh instance.</param>
    public LocalMeshStateManager(
        MeshServiceConfiguration config,
        ILogger<LocalMeshStateManager> logger,
        IMeshInstanceIdentifier instanceIdentifier)
    {
        _logger = logger;

        // Create a local endpoint representing this instance
        _localEndpoint = new MeshEndpoint
        {
            InstanceId = instanceIdentifier.InstanceId,
            AppId = AppConstants.DEFAULT_APP_NAME,
            Host = "localhost",
            Port = 80,
            Status = EndpointStatus.Healthy,
            LoadPercent = 0f,
            CurrentConnections = 0,
            LastSeen = DateTimeOffset.UtcNow
        };

        _logger.LogWarning(
            "LocalMeshStateManager initialized - all service calls will route locally to '{AppId}'",
            AppConstants.DEFAULT_APP_NAME);
    }

    /// <inheritdoc/>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        _logger.LogInformation("Local mesh state manager ready (no state store connection required)");
        return true;
    }

    /// <inheritdoc/>
    public async Task<(bool IsHealthy, string? Message, TimeSpan? OperationTime)> CheckHealthAsync()
    {
        await Task.CompletedTask;
        return (true, "Local routing mode (no state store)", TimeSpan.Zero);
    }

    /// <inheritdoc/>
    public async Task<bool> RegisterEndpointAsync(MeshEndpoint endpoint, int ttlSeconds)
    {
        await Task.CompletedTask;
        _logger.LogDebug("Endpoint registration ignored in local mode: {AppId}:{InstanceId}",
            endpoint.AppId, endpoint.InstanceId);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> DeregisterEndpointAsync(Guid instanceId, string appId)
    {
        await Task.CompletedTask;
        _logger.LogDebug("Endpoint deregistration ignored in local mode: {AppId}:{InstanceId}",
            appId, instanceId);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateHeartbeatAsync(
        Guid instanceId,
        string appId,
        EndpointStatus status,
        float loadPercent,
        int currentConnections,
        ICollection<string>? issues,
        int ttlSeconds)
    {
        await Task.CompletedTask;
        _logger.LogDebug("Heartbeat ignored in local mode: {AppId}:{InstanceId}", appId, instanceId);
        return true;
    }

    /// <inheritdoc/>
    public async Task<List<MeshEndpoint>> GetEndpointsForAppIdAsync(string appId, bool includeUnhealthy = false)
    {
        await Task.CompletedTask;
        // Always return the local endpoint - all routing goes local
        _logger.LogDebug("Returning local endpoint for app-id '{AppId}' (local mode)", appId);
        return new List<MeshEndpoint> { _localEndpoint };
    }

    /// <inheritdoc/>
    public async Task<List<MeshEndpoint>> GetAllEndpointsAsync(string? appIdPrefix = null)
    {
        await Task.CompletedTask;
        return new List<MeshEndpoint> { _localEndpoint };
    }

    /// <inheritdoc/>
    public async Task<MeshEndpoint?> GetEndpointByInstanceIdAsync(Guid instanceId)
    {
        await Task.CompletedTask;

        // If looking for our local instance, return it
        if (instanceId == _localEndpoint.InstanceId)
        {
            return _localEndpoint;
        }

        // Otherwise, still return the local endpoint (all routing goes local)
        _logger.LogDebug("Instance {InstanceId} not found in local mode, returning local endpoint", instanceId);
        return _localEndpoint;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _logger.LogDebug("Local mesh state manager disposed");
        return ValueTask.CompletedTask;
    }
}
