// =============================================================================
// Asset Processor Pool Manager Implementation
// Redis-backed processor node state management.
// =============================================================================

using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Pool;

/// <summary>
/// Redis-backed implementation of IAssetProcessorPoolManager.
/// Uses lib-state for all storage operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS:</b> Uses lib-state infrastructure, no direct Redis connections.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS:</b> All state is in Redis, safe for multi-instance deployments.
/// Uses per-pool-type indexes to avoid expensive KEYS/SCAN operations.
/// </para>
/// </remarks>
public sealed class AssetProcessorPoolManager : IAssetProcessorPoolManager
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<AssetProcessorPoolManager> _logger;
    private readonly AssetServiceConfiguration _configuration;

    // Store name now comes from configuration (ProcessorPoolStoreName)
    private const string INDEX_SUFFIX = ":index";

    /// <summary>
    /// Creates a new AssetProcessorPoolManager.
    /// </summary>
    public AssetProcessorPoolManager(
        IStateStoreFactory stateStoreFactory,
        ILogger<AssetProcessorPoolManager> logger,
        AssetServiceConfiguration configuration)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    #region Node Registration

    /// <inheritdoc/>
    public async Task<bool> RegisterNodeAsync(
        string nodeId,
        string appId,
        string poolType,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(poolType);

        var store = _stateStoreFactory.GetStore<ProcessorNodeState>(_configuration.ProcessorPoolStoreName);
        var nodeKey = GetNodeKey(poolType, nodeId);

        // Check if node already exists
        var existing = await store.GetAsync(nodeKey, cancellationToken);
        if (existing != null)
        {
            _logger.LogWarning(
                "Processor node {NodeId} already registered in pool {PoolType}, updating state",
                nodeId, poolType);
        }

        var state = new ProcessorNodeState
        {
            NodeId = nodeId,
            AppId = appId,
            PoolType = poolType,
            Capacity = capacity,
            CurrentLoad = 0,
            Status = ProcessorNodeStatus.Healthy,
            RegisteredAt = DateTimeOffset.UtcNow,
            LastHeartbeat = DateTimeOffset.UtcNow,
            IdleHeartbeatCount = 0
        };

        // Save with TTL for automatic cleanup if node crashes
        var ttlSeconds = _configuration.ProcessorHeartbeatTimeoutSeconds * 2;
        await store.SaveAsync(nodeKey, state, new StateOptions { Ttl = ttlSeconds }, cancellationToken);

        // Update pool index
        await UpdatePoolIndexAsync(poolType, nodeId, add: true, cancellationToken);

        _logger.LogInformation(
            "Registered processor node {NodeId} in pool {PoolType} (app-id: {AppId}, capacity: {Capacity})",
            nodeId, poolType, appId, capacity);

        return existing == null;
    }

    #endregion

    #region Heartbeat

    /// <inheritdoc/>
    public async Task<ProcessorNodeState?> UpdateHeartbeatAsync(
        string nodeId,
        string poolType,
        int currentLoad,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(poolType);

        var store = _stateStoreFactory.GetStore<ProcessorNodeState>(_configuration.ProcessorPoolStoreName);
        var nodeKey = GetNodeKey(poolType, nodeId);
        var state = await store.GetAsync(nodeKey, cancellationToken);

        if (state == null)
        {
            _logger.LogWarning(
                "Heartbeat from unknown processor node {NodeId} in pool {PoolType}",
                nodeId, poolType);
            return null;
        }

        // Update load and heartbeat timestamp
        var previousLoad = state.CurrentLoad;
        state.CurrentLoad = currentLoad;
        state.LastHeartbeat = DateTimeOffset.UtcNow;

        // Track idle heartbeats for auto-shutdown
        if (currentLoad == 0)
        {
            state.IdleHeartbeatCount++;
        }
        else
        {
            state.IdleHeartbeatCount = 0;
        }

        // Save with refreshed TTL
        var ttlSeconds = _configuration.ProcessorHeartbeatTimeoutSeconds * 2;
        await store.SaveAsync(nodeKey, state, new StateOptions { Ttl = ttlSeconds }, cancellationToken);

        _logger.LogDebug(
            "Heartbeat from processor {NodeId}: load {PreviousLoad} -> {CurrentLoad}, idle count: {IdleCount}",
            nodeId, previousLoad, currentLoad, state.IdleHeartbeatCount);

        return state;
    }

    #endregion

    #region Node Removal

    /// <inheritdoc/>
    public async Task<bool> RemoveNodeAsync(
        string nodeId,
        string poolType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(poolType);

        var store = _stateStoreFactory.GetStore<ProcessorNodeState>(_configuration.ProcessorPoolStoreName);
        var nodeKey = GetNodeKey(poolType, nodeId);

        // Remove from index first
        await UpdatePoolIndexAsync(poolType, nodeId, add: false, cancellationToken);

        // Delete node state
        await store.DeleteAsync(nodeKey, cancellationToken);

        _logger.LogInformation(
            "Removed processor node {NodeId} from pool {PoolType}",
            nodeId, poolType);

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> SetDrainingAsync(
        string nodeId,
        string poolType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(poolType);

        var store = _stateStoreFactory.GetStore<ProcessorNodeState>(_configuration.ProcessorPoolStoreName);
        var nodeKey = GetNodeKey(poolType, nodeId);
        var state = await store.GetAsync(nodeKey, cancellationToken);

        if (state == null)
        {
            _logger.LogWarning(
                "Attempted to drain unknown processor node {NodeId} in pool {PoolType}",
                nodeId, poolType);
            return false;
        }

        state.Status = ProcessorNodeStatus.Draining;

        // Save with TTL (keep alive while draining)
        var ttlSeconds = _configuration.ProcessorHeartbeatTimeoutSeconds * 2;
        await store.SaveAsync(nodeKey, state, new StateOptions { Ttl = ttlSeconds }, cancellationToken);

        _logger.LogInformation(
            "Processor node {NodeId} in pool {PoolType} marked as draining (current load: {Load})",
            nodeId, poolType, state.CurrentLoad);

        return true;
    }

    #endregion

    #region Node Queries

    /// <inheritdoc/>
    public async Task<ProcessorNodeState?> GetNodeAsync(
        string nodeId,
        string poolType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(poolType);

        var store = _stateStoreFactory.GetStore<ProcessorNodeState>(_configuration.ProcessorPoolStoreName);
        var nodeKey = GetNodeKey(poolType, nodeId);
        return await store.GetAsync(nodeKey, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetAvailableCountAsync(
        string poolType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poolType);

        var nodes = await ListNodesInPoolAsync(poolType, cancellationToken);
        return nodes.Count(n => n.HasCapacity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProcessorNodeState>> GetAvailableNodesAsync(
        string poolType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poolType);

        var nodes = await ListNodesInPoolAsync(poolType, cancellationToken);
        return nodes.Where(n => n.HasCapacity).ToList();
    }

    /// <inheritdoc/>
    public async Task<int> GetTotalNodeCountAsync(
        string poolType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poolType);

        var index = await GetPoolIndexAsync(poolType, cancellationToken);
        return index.NodeIds.Count;
    }

    /// <summary>
    /// Lists all nodes in a specific pool.
    /// </summary>
    private async Task<IReadOnlyList<ProcessorNodeState>> ListNodesInPoolAsync(
        string poolType,
        CancellationToken cancellationToken)
    {
        var index = await GetPoolIndexAsync(poolType, cancellationToken);
        if (index.NodeIds.Count == 0)
        {
            return Array.Empty<ProcessorNodeState>();
        }

        var store = _stateStoreFactory.GetStore<ProcessorNodeState>(_configuration.ProcessorPoolStoreName);
        var nodes = new List<ProcessorNodeState>();
        var staleNodeIds = new List<string>();

        foreach (var nodeId in index.NodeIds)
        {
            var nodeKey = GetNodeKey(poolType, nodeId);
            var state = await store.GetAsync(nodeKey, cancellationToken);

            if (state != null)
            {
                nodes.Add(state);
            }
            else
            {
                // Node state expired (TTL), mark for cleanup
                staleNodeIds.Add(nodeId);
            }
        }

        // Clean up stale entries from index
        if (staleNodeIds.Count > 0)
        {
            foreach (var staleId in staleNodeIds)
            {
                await UpdatePoolIndexAsync(poolType, staleId, add: false, cancellationToken);
            }

            _logger.LogInformation(
                "Cleaned up {Count} stale node entries from pool {PoolType} index",
                staleNodeIds.Count, poolType);
        }

        return nodes;
    }

    #endregion

    #region Index Management

    /// <summary>
    /// Gets the index key for a pool type.
    /// </summary>
    private static string GetIndexKey(string poolType) => $"{poolType}{INDEX_SUFFIX}";

    /// <summary>
    /// Gets the node key for a specific node in a pool.
    /// </summary>
    private static string GetNodeKey(string poolType, string nodeId) => $"{poolType}:{nodeId}";

    /// <summary>
    /// Gets the pool index for a specific pool type.
    /// </summary>
    private async Task<ProcessorPoolIndex> GetPoolIndexAsync(string poolType, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<ProcessorPoolIndex>(_configuration.ProcessorPoolStoreName);
        var indexKey = GetIndexKey(poolType);
        var index = await store.GetAsync(indexKey, ct);
        return index ?? new ProcessorPoolIndex();
    }

    /// <summary>
    /// Updates the pool index to add or remove a node ID.
    /// Per IMPLEMENTATION TENETS: Maintains index to avoid KEYS/SCAN operations.
    /// Uses optimistic concurrency with retry to prevent lost updates.
    /// </summary>
    private async Task UpdatePoolIndexAsync(string poolType, string nodeId, bool add, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<ProcessorPoolIndex>(_configuration.ProcessorPoolStoreName);
        var indexKey = GetIndexKey(poolType);
        const int maxRetries = 5;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var (index, etag) = await store.GetWithETagAsync(indexKey, ct);
            index ??= new ProcessorPoolIndex();

            if (add)
            {
                index.NodeIds.Add(nodeId);
            }
            else
            {
                index.NodeIds.Remove(nodeId);
            }

            index.LastUpdated = DateTimeOffset.UtcNow;

            // Use optimistic concurrency if we have an etag
            if (etag != null)
            {
                var saved = await store.TrySaveAsync(indexKey, index, etag, ct);
                if (saved)
                {
                    return;
                }

                // Conflict - retry with fresh data
                _logger.LogDebug(
                    "Pool index update conflict for {PoolType}, retrying (attempt {Attempt}/{MaxRetries})",
                    poolType, attempt, maxRetries);
            }
            else
            {
                // No existing entry, just save
                await store.SaveAsync(indexKey, index, cancellationToken: ct);
                return;
            }
        }

        // All retries exhausted - log error but don't throw
        // Index will self-correct on next node lookup (stale entries cleaned up)
        _logger.LogWarning(
            "Failed to update pool index for {PoolType} after {MaxRetries} attempts (node: {NodeId}, add: {Add})",
            poolType, maxRetries, nodeId, add);
    }

    #endregion
}
