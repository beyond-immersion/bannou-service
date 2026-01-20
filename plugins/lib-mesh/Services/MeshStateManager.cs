#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Manages mesh endpoint state via lib-state infrastructure.
/// Uses IStateStoreFactory for Redis operations, replacing direct StackExchange.Redis dependency.
/// Per IMPLEMENTATION TENETS - uses distributed state via lib-state.
/// </summary>
public class MeshStateManager : IMeshStateManager
{
    private readonly ILogger<MeshStateManager> _logger;
    private readonly IStateStoreFactory _stateStoreFactory;

    // Index key for tracking all known instance IDs (avoids KEYS/SCAN)
    private const string GLOBAL_INDEX_KEY = "_index";

    // Cached stores (lazy initialization after InitializeAsync)
    private IStateStore<MeshEndpoint>? _endpointStore;
    private IStateStore<MeshEndpoint>? _appIdIndexStore;
    private IStateStore<MeshEndpoint>? _globalIndexStore;

    private bool _initialized;

    /// <summary>
    /// Creates MeshStateManager with state store factory from lib-state.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for Redis access.</param>
    /// <param name="logger">Logger instance.</param>
    public MeshStateManager(
        IStateStoreFactory stateStoreFactory,
        ILogger<MeshStateManager> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_initialized)
            {
                _logger.LogDebug("Mesh state stores already initialized, skipping...");
                return true;
            }

            _logger.LogInformation("Initializing mesh state stores via lib-state...");

            // Get stores for mesh endpoint data
            // lib-state is already initialized by StateServicePlugin
            _endpointStore = await _stateStoreFactory.GetStoreAsync<MeshEndpoint>(
                StateStoreDefinitions.MeshEndpoints, cancellationToken);
            _appIdIndexStore = await _stateStoreFactory.GetStoreAsync<MeshEndpoint>(
                StateStoreDefinitions.MeshAppidIndex, cancellationToken);
            _globalIndexStore = await _stateStoreFactory.GetStoreAsync<MeshEndpoint>(
                StateStoreDefinitions.MeshGlobalIndex, cancellationToken);

            // Verify connectivity with a simple operation
            var testResult = await CheckHealthAsync();

            if (testResult.IsHealthy)
            {
                _initialized = true;
                _logger.LogInformation(
                    "Mesh state stores initialized successfully (operation time: {OperationTime}ms)",
                    testResult.OperationTime?.TotalMilliseconds ?? 0);
                return true;
            }

            _logger.LogWarning("Mesh state store health check failed: {Message}", testResult.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize mesh state stores");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<(bool IsHealthy, string? Message, TimeSpan? OperationTime)> CheckHealthAsync()
    {
        if (_globalIndexStore == null)
        {
            return (false, "State stores not initialized", null);
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Simple operation to verify connectivity
            await _globalIndexStore.ExistsAsync(GLOBAL_INDEX_KEY);

            stopwatch.Stop();
            return (true, $"Mesh state stores healthy (operation: {stopwatch.ElapsedMilliseconds}ms)", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mesh state store health check failed");
            return (false, $"Health check failed: {ex.Message}", null);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RegisterEndpointAsync(MeshEndpoint endpoint, int ttlSeconds)
    {
        if (_endpointStore == null || _appIdIndexStore == null || _globalIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot register endpoint.");
            return false;
        }

        try
        {
            var instanceId = endpoint.InstanceId.ToString();
            var options = new StateOptions { Ttl = ttlSeconds };

            // Store the endpoint data with TTL
            await _endpointStore.SaveAsync(instanceId, endpoint, options);

            // Add instance ID to the app-id set for quick lookup
            await _appIdIndexStore.AddToSetAsync(endpoint.AppId, instanceId, options);

            // Add instance ID to global index
            await _globalIndexStore.AddToSetAsync(GLOBAL_INDEX_KEY, instanceId);

            _logger.LogDebug(
                "Registered endpoint {InstanceId} for app {AppId} at {Host}:{Port}",
                instanceId, endpoint.AppId, endpoint.Host, endpoint.Port);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register endpoint {InstanceId}", endpoint.InstanceId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeregisterEndpointAsync(Guid instanceId, string appId)
    {
        if (_endpointStore == null || _appIdIndexStore == null || _globalIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot deregister endpoint.");
            return false;
        }

        try
        {
            var instanceIdStr = instanceId.ToString();

            // Remove from endpoint storage
            await _endpointStore.DeleteAsync(instanceIdStr);

            // Remove from app-id set
            await _appIdIndexStore.RemoveFromSetAsync(appId, instanceIdStr);

            // Remove from global index
            await _globalIndexStore.RemoveFromSetAsync(GLOBAL_INDEX_KEY, instanceIdStr);

            _logger.LogInformation(
                "Deregistered endpoint {InstanceId} from app {AppId}",
                instanceId, appId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deregister endpoint {InstanceId}", instanceId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateHeartbeatAsync(
        Guid instanceId,
        string appId,
        EndpointStatus status,
        float loadPercent,
        int currentConnections,
        int ttlSeconds)
    {
        if (_endpointStore == null || _appIdIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot update heartbeat.");
            return false;
        }

        try
        {
            var instanceIdStr = instanceId.ToString();

            // Get existing endpoint
            var endpoint = await _endpointStore.GetAsync(instanceIdStr);
            if (endpoint == null)
            {
                _logger.LogWarning(
                    "Heartbeat for unknown endpoint {InstanceId}. Endpoint must be registered first.",
                    instanceId);
                return false;
            }

            // Update metrics
            endpoint.Status = status;
            endpoint.LoadPercent = loadPercent;
            endpoint.CurrentConnections = currentConnections;
            endpoint.LastSeen = DateTimeOffset.UtcNow;

            // Save updated endpoint with TTL
            var options = new StateOptions { Ttl = ttlSeconds };
            await _endpointStore.SaveAsync(instanceIdStr, endpoint, options);

            // Refresh app-id set TTL
            await _appIdIndexStore.RefreshSetTtlAsync(appId, ttlSeconds);

            _logger.LogDebug(
                "Updated heartbeat for {InstanceId}: {Status}, load {LoadPercent}%",
                instanceId, status, loadPercent);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update heartbeat for {InstanceId}", instanceId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<List<MeshEndpoint>> GetEndpointsForAppIdAsync(string appId, bool includeUnhealthy = false)
    {
        var endpoints = new List<MeshEndpoint>();

        if (_endpointStore == null || _appIdIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot get endpoints.");
            return endpoints;
        }

        try
        {
            // Get instance IDs from app-id set
            var instanceIds = await _appIdIndexStore.GetSetAsync<string>(appId);

            foreach (var instanceId in instanceIds)
            {
                var endpoint = await _endpointStore.GetAsync(instanceId);

                if (endpoint == null)
                {
                    // Instance ID in set but endpoint expired - clean up
                    await _appIdIndexStore.RemoveFromSetAsync(appId, instanceId);
                    continue;
                }

                // Filter by health status if requested
                if (!includeUnhealthy && endpoint.Status != EndpointStatus.Healthy)
                {
                    continue;
                }

                endpoints.Add(endpoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get endpoints for app {AppId}", appId);
            throw; // Don't mask state store failures - caller needs to know if discovery failed
        }

        return endpoints;
    }

    /// <inheritdoc/>
    public async Task<List<MeshEndpoint>> GetAllEndpointsAsync(string? appIdPrefix = null)
    {
        var endpoints = new List<MeshEndpoint>();

        if (_endpointStore == null || _globalIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot get all endpoints.");
            return endpoints;
        }

        try
        {
            // Get all instance IDs from global index
            var instanceIds = await _globalIndexStore.GetSetAsync<string>(GLOBAL_INDEX_KEY);

            foreach (var instanceId in instanceIds)
            {
                var endpoint = await _endpointStore.GetAsync(instanceId);

                if (endpoint == null)
                {
                    // Clean up stale index entry
                    await _globalIndexStore.RemoveFromSetAsync(GLOBAL_INDEX_KEY, instanceId);
                    continue;
                }

                // Apply prefix filter if specified
                if (!string.IsNullOrEmpty(appIdPrefix) &&
                    !endpoint.AppId.StartsWith(appIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                endpoints.Add(endpoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all endpoints");
            throw; // Don't mask state store failures - caller needs to know if discovery failed
        }

        return endpoints;
    }

    /// <inheritdoc/>
    public async Task<MeshEndpoint?> GetEndpointByInstanceIdAsync(Guid instanceId)
    {
        if (_endpointStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot get endpoint.");
            return null;
        }

        try
        {
            return await _endpointStore.GetAsync(instanceId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get endpoint {InstanceId}", instanceId);
            throw; // Don't mask state store failures - null should mean "not found", not "error"
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        // State store factory handles cleanup
        _logger.LogDebug("MeshStateManager disposed");
        return ValueTask.CompletedTask;
    }
}
