// =============================================================================
// Actor Pool Manager Implementation
// Redis-backed pool node and actor assignment management.
// =============================================================================

using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.Pool;

/// <summary>
/// Redis-backed implementation of IActorPoolManager.
/// Uses lib-state for all storage operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS:</b> Uses lib-state infrastructure, no direct Redis connections.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS:</b> All state is in Redis, safe for multi-instance control planes.
/// </para>
/// </remarks>
public sealed class ActorPoolManager : IActorPoolManager
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<ActorPoolManager> _logger;
    private readonly ActorServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    private static readonly string POOL_NODES_STORE = StateStoreDefinitions.ActorPoolNodes;
    private static readonly string ACTOR_ASSIGNMENTS_STORE = StateStoreDefinitions.ActorAssignments;
    private const string NODE_INDEX_KEY = "_node_index";
    private const string ACTOR_INDEX_KEY = "_actor_index";

    /// <summary>
    /// Creates a new ActorPoolManager.
    /// </summary>
    public ActorPoolManager(
        IStateStoreFactory stateStoreFactory,
        ILogger<ActorPoolManager> logger,
        ActorServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    #region Pool Node Management

    /// <inheritdoc/>
    public async Task<bool> RegisterNodeAsync(PoolNodeRegisteredEvent registration, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.RegisterNode");

        var nodeId = registration.NodeId;
        var store = _stateStoreFactory.GetStore<PoolNodeState>(POOL_NODES_STORE);

        // Check if node already exists
        var existing = await store.GetAsync(nodeId, ct);
        if (existing != null)
        {
            _logger.LogWarning("Pool node {NodeId} already registered, updating state", nodeId);
        }

        var state = new PoolNodeState
        {
            NodeId = nodeId,
            AppId = registration.AppId,
            PoolType = registration.PoolType,
            Capacity = registration.Capacity,
            CurrentLoad = 0,
            Status = PoolNodeStatus.Healthy,
            RegisteredAt = DateTimeOffset.UtcNow,
            LastHeartbeat = DateTimeOffset.UtcNow
        };

        await store.SaveAsync(nodeId, state, cancellationToken: ct);

        // Update index
        await UpdateNodeIndexAsync(nodeId, add: true, ct);

        _logger.LogInformation(
            "Registered pool node {NodeId} (type: {PoolType}, capacity: {Capacity})",
            nodeId, registration.PoolType, registration.Capacity);

        return existing == null;
    }

    /// <inheritdoc/>
    public async Task UpdateNodeHeartbeatAsync(PoolNodeHeartbeatEvent heartbeat, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.UpdateNodeHeartbeat");

        var store = _stateStoreFactory.GetStore<PoolNodeState>(POOL_NODES_STORE);
        var state = await store.GetAsync(heartbeat.NodeId, ct);

        if (state == null)
        {
            _logger.LogWarning(
                "Heartbeat from unknown node {NodeId}, treating as registration",
                heartbeat.NodeId);

            // Auto-register if we receive heartbeat before registration
            state = new PoolNodeState
            {
                NodeId = heartbeat.NodeId,
                AppId = heartbeat.AppId ?? heartbeat.NodeId,
                PoolType = "shared", // Default to shared if not specified
                Capacity = heartbeat.Capacity,
                CurrentLoad = heartbeat.CurrentLoad,
                Status = PoolNodeStatus.Healthy,
                RegisteredAt = DateTimeOffset.UtcNow,
                LastHeartbeat = DateTimeOffset.UtcNow
            };

            await UpdateNodeIndexAsync(heartbeat.NodeId, add: true, ct);
        }
        else
        {
            // Update existing state
            state.CurrentLoad = heartbeat.CurrentLoad;
            state.Capacity = heartbeat.Capacity;
            state.LastHeartbeat = DateTimeOffset.UtcNow;

            // If node was unhealthy and is now sending heartbeats, mark healthy
            if (state.Status == PoolNodeStatus.Unhealthy)
            {
                state.Status = PoolNodeStatus.Healthy;
                _logger.LogInformation("Pool node {NodeId} recovered to healthy", heartbeat.NodeId);
            }
        }

        await store.SaveAsync(heartbeat.NodeId, state, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task<int> DrainNodeAsync(string nodeId, int remainingActors, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.DrainNode");
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var store = _stateStoreFactory.GetStore<PoolNodeState>(POOL_NODES_STORE);
        var state = await store.GetAsync(nodeId, ct);

        if (state == null)
        {
            _logger.LogWarning("Attempted to drain unknown node {NodeId}", nodeId);
            return 0;
        }

        state.Status = PoolNodeStatus.Draining;
        state.DrainingActorsRemaining = remainingActors;

        await store.SaveAsync(nodeId, state, cancellationToken: ct);

        _logger.LogInformation(
            "Pool node {NodeId} draining with {RemainingActors} actors",
            nodeId, remainingActors);

        return remainingActors;
    }

    /// <inheritdoc/>
    public async Task<int> RemoveNodeAsync(string nodeId, string reason, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.RemoveNode");
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var nodeStore = _stateStoreFactory.GetStore<PoolNodeState>(POOL_NODES_STORE);

        // Get assignments for this node first
        var assignments = await ListActorsByNodeAsync(nodeId, ct);
        var actorCount = assignments.Count;

        // Remove all assignments for this node
        var assignmentStore = _stateStoreFactory.GetStore<ActorAssignment>(ACTOR_ASSIGNMENTS_STORE);
        foreach (var assignment in assignments)
        {
            await assignmentStore.DeleteAsync(assignment.ActorId, ct);
        }

        // Remove from index
        await UpdateNodeIndexAsync(nodeId, add: false, ct);

        // Remove node state
        await nodeStore.DeleteAsync(nodeId, ct);

        _logger.LogInformation(
            "Removed pool node {NodeId}: {Reason}. {ActorCount} actors were assigned.",
            nodeId, reason, actorCount);

        return actorCount;
    }

    /// <inheritdoc/>
    public async Task<PoolNodeState?> GetNodeAsync(string nodeId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.GetNode");
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var store = _stateStoreFactory.GetStore<PoolNodeState>(POOL_NODES_STORE);
        return await store.GetAsync(nodeId, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PoolNodeState>> ListNodesAsync(CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.ListNodes");
        var index = await GetNodeIndexAsync(ct);
        if (index.NodeIds.Count == 0)
        {
            return Array.Empty<PoolNodeState>();
        }

        var store = _stateStoreFactory.GetStore<PoolNodeState>(POOL_NODES_STORE);
        var nodes = new List<PoolNodeState>();

        foreach (var nodeId in index.NodeIds)
        {
            var state = await store.GetAsync(nodeId, ct);
            if (state != null)
            {
                nodes.Add(state);
            }
        }

        return nodes;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PoolNodeState>> ListNodesByTypeAsync(string poolType, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.ListNodesByType");
        var allNodes = await ListNodesAsync(ct);
        return allNodes.Where(n => n.PoolType.Equals(poolType, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    #endregion

    #region Actor Assignment

    /// <inheritdoc/>
    public async Task<PoolNodeState?> AcquireNodeForActorAsync(string category, int estimatedLoad = 1, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.AcquireNodeForActor");
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        // Map category to pool type
        // In shared-pool mode, all categories go to "shared" pool
        // In pool-per-type mode, category maps directly to pool type
        var poolType = _configuration.DeploymentMode == ActorDeploymentMode.SharedPool ? "shared" : category;

        var nodes = await ListNodesByTypeAsync(poolType, ct);
        var healthyNodes = nodes.Where(n => n.HasCapacity).ToList();

        if (healthyNodes.Count == 0)
        {
            // Fall back to shared pool if no dedicated pool available
            if (poolType != "shared")
            {
                nodes = await ListNodesByTypeAsync("shared", ct);
                healthyNodes = nodes.Where(n => n.HasCapacity).ToList();
            }

            if (healthyNodes.Count == 0)
            {
                _logger.LogWarning(
                    "No pool nodes with capacity available for category {Category} (image: {PoolNodeImage}, max nodes: {MaxPoolNodes})",
                    category, _configuration.PoolNodeImage, _configuration.MaxPoolNodes);
                return null;
            }
        }

        // Select least-loaded node
        var selectedNode = healthyNodes.OrderBy(n => n.CurrentLoad / (float)n.Capacity).First();

        _logger.LogDebug(
            "Acquired node {NodeId} for actor (category: {Category}, load: {Load}/{Capacity})",
            selectedNode.NodeId, category, selectedNode.CurrentLoad, selectedNode.Capacity);

        return selectedNode;
    }

    /// <inheritdoc/>
    public async Task RecordActorAssignmentAsync(ActorAssignment assignment, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.RecordActorAssignment");

        var store = _stateStoreFactory.GetStore<ActorAssignment>(ACTOR_ASSIGNMENTS_STORE);
        assignment.AssignedAt = DateTimeOffset.UtcNow;

        await store.SaveAsync(assignment.ActorId, assignment, cancellationToken: ct);

        // Update actor index
        await UpdateActorIndexAsync(assignment.ActorId, assignment.NodeId, add: true, ct);

        // Increment node load with optimistic concurrency per IMPLEMENTATION TENETS
        await UpdateNodeLoadAsync(assignment.NodeId, increment: 1, ct);

        _logger.LogDebug(
            "Recorded actor {ActorId} assignment to node {NodeId}",
            assignment.ActorId, assignment.NodeId);
    }

    /// <inheritdoc/>
    public async Task<ActorAssignment?> GetActorAssignmentAsync(string actorId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.GetActorAssignment");
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var store = _stateStoreFactory.GetStore<ActorAssignment>(ACTOR_ASSIGNMENTS_STORE);
        return await store.GetAsync(actorId, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveActorAssignmentAsync(string actorId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.RemoveActorAssignment");
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var store = _stateStoreFactory.GetStore<ActorAssignment>(ACTOR_ASSIGNMENTS_STORE);
        var assignment = await store.GetAsync(actorId, ct);

        if (assignment == null)
        {
            return false;
        }

        // Decrement node load with optimistic concurrency per IMPLEMENTATION TENETS
        await UpdateNodeLoadAsync(assignment.NodeId, increment: -1, ct);

        await store.DeleteAsync(actorId, ct);

        // Update actor index
        await UpdateActorIndexAsync(actorId, assignment.NodeId, add: false, ct);

        _logger.LogDebug("Removed actor {ActorId} assignment from node {NodeId}",
            actorId, assignment.NodeId);

        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ActorAssignment>> ListActorsByNodeAsync(string nodeId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.ListActorsByNode");
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        // Use index to get actor IDs for this node
        var index = await GetActorIndexAsync(ct);
        if (!index.ActorsByNode.TryGetValue(nodeId, out var actorIds) || actorIds.Count == 0)
        {
            return Array.Empty<ActorAssignment>();
        }

        var store = _stateStoreFactory.GetStore<ActorAssignment>(ACTOR_ASSIGNMENTS_STORE);
        var assignments = new List<ActorAssignment>();

        foreach (var actorId in actorIds)
        {
            var assignment = await store.GetAsync(actorId, ct);
            if (assignment != null)
            {
                assignments.Add(assignment);
            }
        }

        return assignments;
    }

    /// <inheritdoc/>
    public async Task UpdateActorStatusAsync(string actorId, ActorStatus newStatus, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.UpdateActorStatus");
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var store = _stateStoreFactory.GetStore<ActorAssignment>(ACTOR_ASSIGNMENTS_STORE);

        // Use optimistic concurrency to prevent lost updates from concurrent assignment
        // modifications (e.g., UpdateActorCharacterAsync racing with a status transition
        // on the same assignment record — the loser would overwrite the winner's field).
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var (assignment, etag) = await store.GetWithETagAsync(actorId, ct);

            if (assignment == null)
            {
                return;
            }

            assignment.Status = newStatus;

            // Set StartedAt when status transitions to running
            if (newStatus == ActorStatus.Running && assignment.StartedAt == null)
            {
                assignment.StartedAt = DateTimeOffset.UtcNow;
            }

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var result = await store.TrySaveAsync(actorId, assignment, etag ?? string.Empty, ct);
            if (result != null)
            {
                return; // Successfully saved
            }

            if (attempt < maxRetries)
            {
                _logger.LogDebug(
                    "Actor assignment conflict for {ActorId} during status update to {Status}, retrying (attempt {Attempt}/{Max})",
                    actorId, newStatus, attempt, maxRetries);
            }
            else
            {
                _logger.LogWarning(
                    "Actor assignment update failed after {MaxRetries} attempts for {ActorId} status update to {Status}",
                    maxRetries, actorId, newStatus);
            }
        }
    }

    /// <inheritdoc/>
    public async Task UpdateActorCharacterAsync(string actorId, Guid characterId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.UpdateActorCharacter");
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var store = _stateStoreFactory.GetStore<ActorAssignment>(ACTOR_ASSIGNMENTS_STORE);

        // Use optimistic concurrency to prevent lost updates from concurrent assignment
        // modifications (e.g., UpdateActorStatusAsync racing with UpdateActorCharacterAsync).
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var (assignment, etag) = await store.GetWithETagAsync(actorId, ct);

            if (assignment == null)
            {
                _logger.LogWarning("Actor assignment {ActorId} not found for character binding", actorId);
                return;
            }

            assignment.CharacterId = characterId;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var result = await store.TrySaveAsync(actorId, assignment, etag ?? string.Empty, ct);
            if (result != null)
            {
                return; // Successfully saved
            }

            if (attempt < maxRetries)
            {
                _logger.LogDebug(
                    "Actor assignment conflict for {ActorId} during character bind, retrying (attempt {Attempt}/{Max})",
                    actorId, attempt, maxRetries);
            }
            else
            {
                _logger.LogWarning(
                    "Actor assignment update failed after {MaxRetries} attempts for {ActorId} character bind",
                    maxRetries, actorId);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ActorAssignment>> GetAssignmentsByTemplateAsync(string templateId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.GetAssignmentsByTemplate");
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        if (!Guid.TryParse(templateId, out var parsedTemplateId))
        {
            return Array.Empty<ActorAssignment>();
        }

        // Get all actor IDs from the index
        var index = await GetActorIndexAsync(ct);
        var allActorIds = index.ActorsByNode.Values.SelectMany(ids => ids).ToList();

        if (allActorIds.Count == 0)
        {
            return Array.Empty<ActorAssignment>();
        }

        var store = _stateStoreFactory.GetStore<ActorAssignment>(ACTOR_ASSIGNMENTS_STORE);
        var assignments = new List<ActorAssignment>();

        foreach (var actorId in allActorIds)
        {
            var assignment = await store.GetAsync(actorId, ct);
            if (assignment != null && assignment.TemplateId == parsedTemplateId)
            {
                assignments.Add(assignment);
            }
        }

        return assignments;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ActorAssignment>> GetActorAssignmentsByCharacterAsync(Guid characterId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.GetActorAssignmentsByCharacter");

        // Get all actor IDs from the index
        var index = await GetActorIndexAsync(ct);
        var allActorIds = index.ActorsByNode.Values.SelectMany(ids => ids).ToList();

        if (allActorIds.Count == 0)
        {
            return Array.Empty<ActorAssignment>();
        }

        var store = _stateStoreFactory.GetStore<ActorAssignment>(ACTOR_ASSIGNMENTS_STORE);
        var assignments = new List<ActorAssignment>();

        foreach (var actorId in allActorIds)
        {
            var assignment = await store.GetAsync(actorId, ct);
            if (assignment != null && assignment.CharacterId == characterId)
            {
                assignments.Add(assignment);
            }
        }

        return assignments;
    }

    #endregion

    #region Capacity & Monitoring

    /// <inheritdoc/>
    public async Task<PoolCapacitySummary> GetCapacitySummaryAsync(CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.GetCapacitySummary");
        var nodes = await ListNodesAsync(ct);

        var summary = new PoolCapacitySummary
        {
            TotalNodes = nodes.Count,
            HealthyNodes = nodes.Count(n => n.Status == PoolNodeStatus.Healthy),
            DrainingNodes = nodes.Count(n => n.Status == PoolNodeStatus.Draining),
            UnhealthyNodes = nodes.Count(n => n.Status == PoolNodeStatus.Unhealthy)
        };

        var healthyNodes = nodes.Where(n => n.Status == PoolNodeStatus.Healthy).ToList();
        summary.TotalCapacity = healthyNodes.Sum(n => n.Capacity);
        summary.TotalLoad = healthyNodes.Sum(n => n.CurrentLoad);

        // Group by pool type
        foreach (var group in nodes.GroupBy(n => n.PoolType))
        {
            var healthyInGroup = group.Where(n => n.Status == PoolNodeStatus.Healthy).ToList();
            summary.ByPoolType[group.Key] = new PoolTypeCapacity
            {
                PoolType = group.Key,
                NodeCount = group.Count(),
                TotalCapacity = healthyInGroup.Sum(n => n.Capacity),
                CurrentLoad = healthyInGroup.Sum(n => n.CurrentLoad)
            };
        }

        return summary;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PoolNodeState>> GetUnhealthyNodesAsync(TimeSpan heartbeatTimeout, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.GetUnhealthyNodes");
        var nodes = await ListNodesAsync(ct);
        var cutoff = DateTimeOffset.UtcNow - heartbeatTimeout;

        return nodes
            .Where(n => n.Status != PoolNodeStatus.Unhealthy && n.LastHeartbeat < cutoff)
            .ToList();
    }

    #endregion

    #region Node Load Management

    /// <summary>
    /// Atomically updates a node's current load using optimistic concurrency.
    /// </summary>
    private async Task UpdateNodeLoadAsync(string nodeId, int increment, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.UpdateNodeLoad");
        var nodeStore = _stateStoreFactory.GetStore<PoolNodeState>(POOL_NODES_STORE);

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var (node, etag) = await nodeStore.GetWithETagAsync(nodeId, ct);
            if (node == null)
            {
                return; // Node not found
            }

            node.CurrentLoad = Math.Max(0, node.CurrentLoad + increment);

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var result = await nodeStore.TrySaveAsync(nodeId, node, etag ?? string.Empty, ct);
            if (result != null)
            {
                return; // Successfully saved
            }

            if (attempt < maxRetries)
            {
                _logger.LogDebug("Node load conflict for {NodeId}, retrying (attempt {Attempt}/{Max})", nodeId, attempt, maxRetries);
            }
            else
            {
                _logger.LogWarning("Node load update failed after {MaxRetries} attempts for node {NodeId}", maxRetries, nodeId);
            }
        }
    }

    #endregion

    #region Index Management

    /// <summary>
    /// Index for tracking known node IDs.
    /// Avoids KEYS/SCAN operations per IMPLEMENTATION TENETS.
    /// </summary>
    private class PoolNodeIndex
    {
        public HashSet<string> NodeIds { get; set; } = new();
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }

    private async Task<PoolNodeIndex> GetNodeIndexAsync(CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.GetNodeIndex");
        var store = _stateStoreFactory.GetStore<PoolNodeIndex>(POOL_NODES_STORE);
        var index = await store.GetAsync(NODE_INDEX_KEY, ct);
        return index ?? new PoolNodeIndex();
    }

    private async Task UpdateNodeIndexAsync(string nodeId, bool add, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.UpdateNodeIndex");
        var store = _stateStoreFactory.GetStore<PoolNodeIndex>(POOL_NODES_STORE);

        // Optimistic concurrency: retry on conflict per IMPLEMENTATION TENETS
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var (index, etag) = await store.GetWithETagAsync(NODE_INDEX_KEY, ct);
            index ??= new PoolNodeIndex();

            if (add)
            {
                index.NodeIds.Add(nodeId);
            }
            else
            {
                index.NodeIds.Remove(nodeId);
            }

            index.LastUpdated = DateTimeOffset.UtcNow;

            var result = await store.TrySaveAsync(NODE_INDEX_KEY, index, etag ?? string.Empty, ct);
            if (result != null)
            {
                return; // Successfully saved
            }

            if (attempt < maxRetries)
            {
                _logger.LogDebug("Node index conflict, retrying (attempt {Attempt}/{Max})", attempt, maxRetries);
            }
            else
            {
                _logger.LogWarning("Node index update failed after {MaxRetries} attempts for node {NodeId}", maxRetries, nodeId);
            }
        }
    }

    /// <summary>
    /// Index for tracking actor assignments by node.
    /// Enables efficient lookup of actors on a specific node.
    /// </summary>
    private class ActorIndex
    {
        public Dictionary<string, HashSet<string>> ActorsByNode { get; set; } = new();
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }

    private async Task<ActorIndex> GetActorIndexAsync(CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.GetActorIndex");
        var store = _stateStoreFactory.GetStore<ActorIndex>(ACTOR_ASSIGNMENTS_STORE);
        var index = await store.GetAsync(ACTOR_INDEX_KEY, ct);
        return index ?? new ActorIndex();
    }

    private async Task UpdateActorIndexAsync(string actorId, string nodeId, bool add, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorPoolManager.UpdateActorIndex");
        var store = _stateStoreFactory.GetStore<ActorIndex>(ACTOR_ASSIGNMENTS_STORE);

        // Optimistic concurrency: retry on conflict per IMPLEMENTATION TENETS
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var (index, etag) = await store.GetWithETagAsync(ACTOR_INDEX_KEY, ct);
            index ??= new ActorIndex();

            if (!index.ActorsByNode.TryGetValue(nodeId, out var actorIds))
            {
                actorIds = new HashSet<string>();
                index.ActorsByNode[nodeId] = actorIds;
            }

            if (add)
            {
                actorIds.Add(actorId);
            }
            else
            {
                actorIds.Remove(actorId);
                if (actorIds.Count == 0)
                {
                    index.ActorsByNode.Remove(nodeId);
                }
            }

            index.LastUpdated = DateTimeOffset.UtcNow;

            var result = await store.TrySaveAsync(ACTOR_INDEX_KEY, index, etag ?? string.Empty, ct);
            if (result != null)
            {
                return; // Successfully saved
            }

            if (attempt < maxRetries)
            {
                _logger.LogDebug("Actor index conflict, retrying (attempt {Attempt}/{Max})", attempt, maxRetries);
            }
            else
            {
                _logger.LogWarning("Actor index update failed after {MaxRetries} attempts for actor {ActorId}", maxRetries, actorId);
            }
        }
    }

    #endregion
}
