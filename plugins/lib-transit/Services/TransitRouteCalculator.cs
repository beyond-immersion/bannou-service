using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Calculates optimal routes over the cached connection graph using Dijkstra's algorithm.
/// Stateless computation service -- reads graph data from the cache but does not write anything.
/// </summary>
/// <remarks>
/// <para>
/// Supports three cost functions:
/// <list type="bullet">
///   <item><c>fastest</c>: cost = estimated_hours (distance / effective speed with terrain modifiers)</item>
///   <item><c>safest</c>: cost = cumulative risk</item>
///   <item><c>shortest</c>: cost = distance in km</item>
/// </list>
/// </para>
/// <para>
/// <b>Discovery Filtering</b>: When <c>EntityId</c> is provided, discoverable connections
/// are filtered to only those the entity has discovered. When null, discoverable connections
/// are excluded entirely (conservative default).
/// </para>
/// <para>
/// <b>Cross-Realm Routing</b>: When origin and destination are in different realms,
/// the calculator merges graphs for all relevant realms.
/// </para>
/// <para>
/// Does NOT apply entity-specific DI cost modifiers -- those are applied by the
/// variable provider when GOAP evaluates results. Returns objective travel data.
/// </para>
/// </remarks>
internal sealed class TransitRouteCalculator : ITransitRouteCalculator
{
    private readonly ITransitConnectionGraphCache _graphCache;
    private readonly IQueryableStateStore<TransitConnectionModel> _connectionStore;
    private readonly IStateStore<HashSet<Guid>> _discoveryCacheStore;
    private readonly IQueryableStateStore<TransitModeModel> _modeStore;
    private readonly TransitServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<TransitRouteCalculator> _logger;

    /// <summary>
    /// Creates a new instance of the TransitRouteCalculator.
    /// </summary>
    /// <param name="graphCache">Connection graph cache for per-realm adjacency lists.</param>
    /// <param name="stateStoreFactory">Factory for state store access.</param>
    /// <param name="configuration">Transit service configuration for max legs, max options, cargo penalty, etc.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    /// <param name="logger">Structured logger.</param>
    public TransitRouteCalculator(
        ITransitConnectionGraphCache graphCache,
        IStateStoreFactory stateStoreFactory,
        TransitServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        ILogger<TransitRouteCalculator> logger)
    {
        _graphCache = graphCache;
        _connectionStore = stateStoreFactory.GetQueryableStore<TransitConnectionModel>(StateStoreDefinitions.TransitConnections);
        _discoveryCacheStore = stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.TransitDiscoveryCache);
        _modeStore = stateStoreFactory.GetQueryableStore<TransitModeModel>(StateStoreDefinitions.TransitModes);
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RouteCalculationResult>> CalculateAsync(
        RouteCalculationRequest request, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitRouteCalculator.CalculateAsync");

        _logger.LogDebug(
            "Calculating route from {OriginId} to {DestinationId}, sort: {SortBy}, multiModal: {MultiModal}",
            request.OriginLocationId, request.DestinationLocationId,
            request.SortBy, request.PreferMultiModal);

        // Build the combined graph (may merge multiple realms for cross-realm routing)
        var graph = await BuildFilteredGraphAsync(request, ct);

        if (graph.Count == 0)
        {
            _logger.LogDebug("No graph edges available for route calculation");
            return Array.Empty<RouteCalculationResult>();
        }

        // Load mode data for speed/terrain calculations
        var modes = await LoadModeDataAsync(ct);

        // Run k-shortest-paths to find multiple distinct routes
        var results = FindRoutes(graph, modes, request);

        _logger.LogDebug(
            "Route calculation complete: {RouteCount} options found from {OriginId} to {DestinationId}",
            results.Count, request.OriginLocationId, request.DestinationLocationId);

        return results;
    }

    #region Graph Building and Filtering

    /// <summary>
    /// Builds the filtered graph by discovering realm IDs from connections touching the
    /// origin/destination, loading per-realm adjacency lists from the cache,
    /// merging them for cross-realm routing, and filtering by discovery and seasonal status.
    /// </summary>
    /// <param name="request">Route calculation parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Filtered list of graph edges to use for route calculation.</returns>
    private async Task<List<ConnectionGraphEntry>> BuildFilteredGraphAsync(
        RouteCalculationRequest request, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitRouteCalculator.BuildFilteredGraphAsync");

        // Discover realm IDs from connections touching origin or destination
        var connectionsByLocation = await _connectionStore.QueryAsync(
            c => c.FromLocationId == request.OriginLocationId
                 || c.ToLocationId == request.OriginLocationId
                 || c.FromLocationId == request.DestinationLocationId
                 || c.ToLocationId == request.DestinationLocationId,
            ct);

        if (connectionsByLocation.Count == 0)
        {
            _logger.LogDebug(
                "No connections found touching origin {OriginId} or destination {DestinationId}",
                request.OriginLocationId, request.DestinationLocationId);
            return new List<ConnectionGraphEntry>();
        }

        // Extract unique realm IDs from these connections
        var realmIds = new HashSet<Guid>();
        foreach (var conn in connectionsByLocation)
        {
            realmIds.Add(conn.FromRealmId);
            realmIds.Add(conn.ToRealmId);
        }

        // Load per-entity discovery data for filtering discoverable connections
        HashSet<Guid>? discoveredConnectionIds = null;
        if (request.EntityId.HasValue)
        {
            var discoveryCacheKey = $"discovery:{request.EntityId.Value}";
            discoveredConnectionIds = await _discoveryCacheStore.GetAsync(discoveryCacheKey, ct);
        }

        // Load and merge graphs for all relevant realms, deduplicating cross-realm edges
        var allEdges = new List<ConnectionGraphEntry>();
        var seenEdgeDirections = new HashSet<string>();

        foreach (var realmId in realmIds)
        {
            var realmGraph = await _graphCache.GetGraphAsync(realmId, ct);
            foreach (var edge in realmGraph)
            {
                // Deduplicate: cross-realm connections appear in both realms' graphs
                var directionKey = $"{edge.ConnectionId}:{edge.FromLocationId}:{edge.ToLocationId}";
                if (seenEdgeDirections.Add(directionKey))
                {
                    allEdges.Add(edge);
                }
            }
        }

        // Filter edges based on request parameters
        var filtered = new List<ConnectionGraphEntry>();
        foreach (var edge in allEdges)
        {
            if (!IsEdgeTraversable(edge, request, discoveredConnectionIds))
            {
                continue;
            }

            filtered.Add(edge);
        }

        return filtered;
    }

    /// <summary>
    /// Determines whether an edge should be included in the graph for route calculation
    /// based on status, discovery, seasonal, and mode filters.
    /// </summary>
    /// <param name="edge">The graph edge to evaluate.</param>
    /// <param name="request">Route calculation parameters.</param>
    /// <param name="discoveredConnectionIds">Set of connection IDs discovered by the entity, or null.</param>
    /// <returns>True if the edge should be included in the route calculation graph.</returns>
    private static bool IsEdgeTraversable(
        ConnectionGraphEntry edge,
        RouteCalculationRequest request,
        HashSet<Guid>? discoveredConnectionIds)
    {
        // Exclude closed and blocked connections
        if (edge.Status == ConnectionStatus.Closed || edge.Status == ConnectionStatus.Blocked)
        {
            return false;
        }

        // Exclude seasonal_closed unless explicitly included
        if (!request.IncludeSeasonalClosed && edge.Status == ConnectionStatus.Seasonal_closed)
        {
            return false;
        }

        // Filter discoverable connections
        if (edge.Discoverable)
        {
            if (request.EntityId.HasValue)
            {
                // Entity provided: only include discovered connections
                if (discoveredConnectionIds == null || !discoveredConnectionIds.Contains(edge.ConnectionId))
                {
                    return false;
                }
            }
            else
            {
                // No entity: exclude all discoverable connections (conservative default)
                return false;
            }
        }

        // When a specific mode is requested (not multi-modal), filter by mode compatibility
        if (!string.IsNullOrEmpty(request.ModeCode) && !request.PreferMultiModal)
        {
            // Empty CompatibleModes = walking only; check if the requested mode is walking
            // or if the connection explicitly lists the mode
            if (edge.CompatibleModes.Count > 0 && !edge.CompatibleModes.Contains(request.ModeCode))
            {
                return false;
            }
        }

        return true;
    }

    #endregion

    #region Mode Data Loading

    /// <summary>
    /// Loads all non-deprecated transit mode data for speed and terrain calculations.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary of mode code to mode data.</returns>
    private async Task<Dictionary<string, TransitModeModel>> LoadModeDataAsync(CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitRouteCalculator.LoadModeDataAsync");

        var modes = await _modeStore.QueryAsync(m => !m.IsDeprecated, ct);
        return modes.ToDictionary(m => m.Code, m => m);
    }

    #endregion

    #region Route Finding (Yen's k-Shortest Paths)

    /// <summary>
    /// Finds routes using Yen's k-shortest loopless paths algorithm with the specified cost function.
    /// Returns up to MaxOptions distinct routes ranked by the chosen cost function.
    /// </summary>
    /// <param name="graph">Filtered graph edges.</param>
    /// <param name="modes">Mode data for speed calculations.</param>
    /// <param name="request">Route calculation parameters.</param>
    /// <returns>Ranked list of route calculation results.</returns>
    private List<RouteCalculationResult> FindRoutes(
        List<ConnectionGraphEntry> graph,
        Dictionary<string, TransitModeModel> modes,
        RouteCalculationRequest request)
    {
        var adjacency = BuildAdjacencyList(graph);

        if (!adjacency.ContainsKey(request.OriginLocationId))
        {
            _logger.LogDebug("Origin location {OriginId} not found in graph adjacency list",
                request.OriginLocationId);
            return new List<RouteCalculationResult>();
        }

        // Use Yen's algorithm to find multiple distinct shortest paths
        var foundPaths = YenKShortestPaths(adjacency, modes, request);

        var results = new List<RouteCalculationResult>();
        var rank = 1;
        foreach (var path in foundPaths)
        {
            var result = BuildRouteResult(path, modes, request, rank);
            if (result != null)
            {
                results.Add(result);
                rank++;
            }
        }

        return results;
    }

    /// <summary>
    /// Implements Yen's k-shortest loopless paths algorithm to find multiple distinct routes
    /// from origin to destination.
    /// </summary>
    /// <param name="adjacency">The graph adjacency list.</param>
    /// <param name="modes">Available transit modes.</param>
    /// <param name="request">Route calculation parameters.</param>
    /// <returns>List of paths, each path being a list of (edge, mode code) tuples.</returns>
    private List<List<(ConnectionGraphEntry Edge, string ModeCode)>> YenKShortestPaths(
        Dictionary<Guid, List<ConnectionGraphEntry>> adjacency,
        Dictionary<string, TransitModeModel> modes,
        RouteCalculationRequest request)
    {
        var origin = request.OriginLocationId;
        var destination = request.DestinationLocationId;
        var maxOptions = request.MaxOptions;
        var maxLegs = request.MaxLegs;

        // A: list of k-shortest paths found so far
        var shortestPaths = new List<List<(ConnectionGraphEntry Edge, string ModeCode)>>();

        // Find the first shortest path using Dijkstra
        var firstPath = DijkstraShortestPath(
            adjacency, modes, request, origin, destination, maxLegs, null, null);

        if (firstPath == null)
        {
            return shortestPaths;
        }

        shortestPaths.Add(firstPath);

        // B: candidate paths sorted by total cost (tie-break by insertion order)
        var candidates = new SortedSet<CandidatePath>(new CandidatePathComparer());
        var candidateId = 0;

        for (var k = 1; k < maxOptions; k++)
        {
            var previousPath = shortestPaths[k - 1];

            // For each spur node in the previous shortest path
            for (var i = 0; i < previousPath.Count; i++)
            {
                var spurNodeId = i == 0
                    ? origin
                    : previousPath[i - 1].Edge.ToLocationId;

                // Root path: edges from origin to the spur node
                var rootPath = previousPath.Take(i).ToList();

                // Build set of edges to exclude: for each existing shortest path that shares
                // the same root path, exclude the edge leaving the spur node
                var excludedEdges = new HashSet<string>();
                foreach (var existingPath in shortestPaths)
                {
                    if (existingPath.Count <= i)
                    {
                        continue;
                    }

                    if (SharesRootPath(existingPath, previousPath, i))
                    {
                        var edgeKey = FormatEdgeKey(existingPath[i].Edge);
                        excludedEdges.Add(edgeKey);
                    }
                }

                // Nodes to exclude: all nodes in the root path except the spur node (prevent loops)
                var excludedNodes = new HashSet<Guid> { origin };
                foreach (var leg in rootPath)
                {
                    excludedNodes.Add(leg.Edge.ToLocationId);
                }
                excludedNodes.Remove(spurNodeId);

                // Find spur path from spur node to destination
                var remainingLegs = maxLegs - rootPath.Count;
                if (remainingLegs <= 0)
                {
                    continue;
                }

                var spurPath = DijkstraShortestPath(
                    adjacency, modes, request, spurNodeId, destination,
                    remainingLegs, excludedEdges, excludedNodes);

                if (spurPath == null)
                {
                    continue;
                }

                // Total path = root path + spur path
                var totalPath = new List<(ConnectionGraphEntry Edge, string ModeCode)>(rootPath.Count + spurPath.Count);
                totalPath.AddRange(rootPath);
                totalPath.AddRange(spurPath);

                // Skip duplicate paths
                if (IsPathDuplicate(totalPath, shortestPaths))
                {
                    continue;
                }

                // Compute total cost for ordering
                var totalCost = ComputePathCost(totalPath, modes, request);
                candidates.Add(new CandidatePath(totalCost, candidateId++, totalPath));
            }

            if (candidates.Count == 0)
            {
                break;
            }

            // Pop the best candidate (lowest cost)
            var best = candidates.Min;
            if (best != null)
            {
                candidates.Remove(best);
                shortestPaths.Add(best.Path);
            }
        }

        return shortestPaths;
    }

    /// <summary>
    /// Runs Dijkstra's algorithm from source to target with optional edge and node exclusions.
    /// Used as the core shortest-path subroutine for Yen's k-shortest paths.
    /// </summary>
    /// <param name="adjacency">The graph adjacency list.</param>
    /// <param name="modes">Available transit modes.</param>
    /// <param name="request">Route calculation parameters.</param>
    /// <param name="source">Source location ID.</param>
    /// <param name="target">Target location ID.</param>
    /// <param name="maxLegs">Maximum number of legs (edges) to traverse.</param>
    /// <param name="excludedEdges">Edge keys to exclude (for Yen's algorithm). Null to skip exclusion.</param>
    /// <param name="excludedNodes">Node IDs to exclude (for Yen's algorithm). Null to skip exclusion.</param>
    /// <returns>The shortest path as a list of (edge, mode) tuples, or null if no path exists.</returns>
    private List<(ConnectionGraphEntry Edge, string ModeCode)>? DijkstraShortestPath(
        Dictionary<Guid, List<ConnectionGraphEntry>> adjacency,
        Dictionary<string, TransitModeModel> modes,
        RouteCalculationRequest request,
        Guid source,
        Guid target,
        int maxLegs,
        HashSet<string>? excludedEdges,
        HashSet<Guid>? excludedNodes)
    {
        var dist = new Dictionary<Guid, decimal>();
        var legs = new Dictionary<Guid, int>();
        var prev = new Dictionary<Guid, (Guid PrevNode, ConnectionGraphEntry Edge, string ModeCode)>();
        var visited = new HashSet<Guid>();

        // .NET PriorityQueue: (element, priority) -- dequeues lowest priority first
        var pq = new PriorityQueue<Guid, decimal>();

        dist[source] = 0;
        legs[source] = 0;
        pq.Enqueue(source, 0);

        while (pq.Count > 0)
        {
            var current = pq.Dequeue();

            if (current == target)
            {
                return ReconstructPath(prev, source, target);
            }

            if (visited.Contains(current))
            {
                continue;
            }
            visited.Add(current);

            if (!adjacency.TryGetValue(current, out var outEdges))
            {
                continue;
            }

            var currentDist = dist[current];
            var currentLegs = legs[current];

            if (currentLegs >= maxLegs)
            {
                continue;
            }

            foreach (var edge in outEdges)
            {
                var neighbor = edge.ToLocationId;

                if (excludedNodes != null && excludedNodes.Contains(neighbor))
                {
                    continue;
                }

                if (excludedEdges != null)
                {
                    var edgeKey = FormatEdgeKey(edge);
                    if (excludedEdges.Contains(edgeKey))
                    {
                        continue;
                    }
                }

                var (cost, modeCode, _, _, _) = ComputeEdgeCost(edge, modes, request);
                var newDist = currentDist + cost;

                if (!dist.TryGetValue(neighbor, out var existingDist) || newDist < existingDist)
                {
                    dist[neighbor] = newDist;
                    legs[neighbor] = currentLegs + 1;
                    prev[neighbor] = (current, edge, modeCode);
                    pq.Enqueue(neighbor, newDist);
                }
            }
        }

        return null;
    }

    #endregion

    #region Cost Computation

    /// <summary>
    /// Computes the cost of traversing a graph edge based on the selected cost function.
    /// </summary>
    /// <param name="edge">The graph edge to evaluate.</param>
    /// <param name="modes">Available transit modes.</param>
    /// <param name="request">Route calculation parameters for mode preference and cargo.</param>
    /// <returns>
    /// A tuple of (cost for Dijkstra sorting, best mode code for this edge,
    /// travel time in game-hours, distance in km, risk level).
    /// </returns>
    private (decimal Cost, string ModeCode, decimal GameHours, decimal DistanceKm, decimal Risk)
        ComputeEdgeCost(
            ConnectionGraphEntry edge,
            Dictionary<string, TransitModeModel> modes,
            RouteCalculationRequest request)
    {
        var bestMode = SelectBestMode(edge, modes, request);
        var gameHours = ComputeTravelTime(edge, bestMode, modes, request);
        var risk = edge.BaseRiskLevel;
        var distance = edge.DistanceKm;

        var cost = request.SortBy switch
        {
            RouteSortBy.Fastest => gameHours,
            RouteSortBy.Safest => risk,
            RouteSortBy.Shortest => distance,
            _ => gameHours
        };

        return (cost, bestMode, gameHours, distance, risk);
    }

    /// <summary>
    /// Computes the total cost of a complete path for ordering in Yen's candidate list.
    /// </summary>
    /// <param name="path">The complete path.</param>
    /// <param name="modes">Available transit modes.</param>
    /// <param name="request">Route calculation parameters.</param>
    /// <returns>Total cost of the path.</returns>
    private decimal ComputePathCost(
        List<(ConnectionGraphEntry Edge, string ModeCode)> path,
        Dictionary<string, TransitModeModel> modes,
        RouteCalculationRequest request)
    {
        var totalCost = 0m;
        foreach (var (edge, _) in path)
        {
            var (cost, _, _, _, _) = ComputeEdgeCost(edge, modes, request);
            totalCost += cost;
        }
        return totalCost;
    }

    /// <summary>
    /// Selects the best transit mode for a given edge based on request preferences.
    /// When preferMultiModal is true, selects the fastest compatible mode per leg.
    /// When a specific mode is requested, uses that mode if compatible.
    /// Falls back to default walking speed if no mode is compatible.
    /// </summary>
    /// <param name="edge">The graph edge to evaluate.</param>
    /// <param name="modes">Available transit modes.</param>
    /// <param name="request">Route calculation parameters.</param>
    /// <returns>The best mode code for this edge.</returns>
    private string SelectBestMode(
        ConnectionGraphEntry edge,
        Dictionary<string, TransitModeModel> modes,
        RouteCalculationRequest request)
    {
        // If a specific mode is requested and not multi-modal, use it if compatible
        if (!string.IsNullOrEmpty(request.ModeCode) && !request.PreferMultiModal)
        {
            if (edge.CompatibleModes.Count == 0 || edge.CompatibleModes.Contains(request.ModeCode))
            {
                return request.ModeCode;
            }
        }

        // Multi-modal or best-available: find the fastest compatible mode on this edge
        string? bestMode = null;
        var bestSpeed = 0m;

        // Candidate modes: the edge's compatible modes, or all available modes if edge has none
        var candidateCodes = edge.CompatibleModes.Count > 0
            ? (IEnumerable<string>)edge.CompatibleModes
            : modes.Keys;

        foreach (var modeCode in candidateCodes)
        {
            if (!modes.TryGetValue(modeCode, out var mode))
            {
                continue;
            }

            // Check terrain compatibility
            if (mode.CompatibleTerrainTypes.Count > 0
                && !mode.CompatibleTerrainTypes.Contains(edge.TerrainType))
            {
                continue;
            }

            var effectiveSpeed = ComputeEffectiveSpeed(mode, edge.TerrainType, request.CargoWeightKg);
            if (effectiveSpeed > bestSpeed)
            {
                bestSpeed = effectiveSpeed;
                bestMode = modeCode;
            }
        }

        // Fall back to "walking" if it exists, otherwise pick first available candidate
        if (bestMode == null)
        {
            if (modes.ContainsKey("walking"))
            {
                return "walking";
            }
            // Last resort: use the first compatible mode code
            return edge.CompatibleModes.Count > 0
                ? edge.CompatibleModes[0]
                : modes.Keys.FirstOrDefault() ?? "walking";
        }

        return bestMode;
    }

    /// <summary>
    /// Computes the effective speed for a mode on a given terrain, accounting for
    /// terrain speed modifiers and cargo speed penalty.
    /// </summary>
    /// <param name="mode">The transit mode.</param>
    /// <param name="terrainType">The terrain type of the connection.</param>
    /// <param name="cargoWeightKg">Current cargo weight for penalty calculation.</param>
    /// <returns>Effective speed in km/game-hour.</returns>
    private decimal ComputeEffectiveSpeed(
        TransitModeModel mode,
        string terrainType,
        decimal cargoWeightKg)
    {
        var baseSpeed = mode.BaseSpeedKmPerGameHour;

        // Apply terrain speed modifier if present
        var terrainMultiplier = 1.0m;
        if (mode.TerrainSpeedModifiers != null)
        {
            var modifier = mode.TerrainSpeedModifiers.FirstOrDefault(
                t => t.TerrainType == terrainType);
            if (modifier != null)
            {
                terrainMultiplier = modifier.Multiplier;
            }
        }

        var effectiveSpeed = baseSpeed * terrainMultiplier;

        // Apply cargo speed penalty if cargo exceeds threshold
        // Formula: speed_reduction = (cargo - threshold) / (capacity - threshold) * rate
        var threshold = (decimal)_configuration.CargoSpeedPenaltyThresholdKg;
        if (cargoWeightKg > threshold && mode.CargoCapacityKg > threshold)
        {
            var penaltyRate = mode.CargoSpeedPenaltyRate
                              ?? (decimal)_configuration.DefaultCargoSpeedPenaltyRate;
            var speedReduction = (cargoWeightKg - threshold) / (mode.CargoCapacityKg - threshold) * penaltyRate;
            // Cap reduction at the penalty rate (e.g., 0.3 = max 30% speed loss at full capacity)
            speedReduction = Math.Min(speedReduction, penaltyRate);
            effectiveSpeed *= (1.0m - speedReduction);
        }

        // Ensure speed never drops to zero
        return Math.Max(effectiveSpeed, 0.01m);
    }

    /// <summary>
    /// Computes travel time in game-hours for traversing an edge with a specific mode.
    /// </summary>
    /// <param name="edge">The graph edge.</param>
    /// <param name="modeCode">The selected mode code.</param>
    /// <param name="modes">Available transit modes.</param>
    /// <param name="request">Route calculation parameters.</param>
    /// <returns>Travel time in game-hours.</returns>
    private decimal ComputeTravelTime(
        ConnectionGraphEntry edge,
        string modeCode,
        Dictionary<string, TransitModeModel> modes,
        RouteCalculationRequest request)
    {
        decimal effectiveSpeed;

        if (modes.TryGetValue(modeCode, out var mode))
        {
            effectiveSpeed = ComputeEffectiveSpeed(mode, edge.TerrainType, request.CargoWeightKg);
        }
        else
        {
            // Mode not found in registry, use default walking speed from configuration
            effectiveSpeed = (decimal)_configuration.DefaultWalkingSpeedKmPerGameHour;
        }

        return edge.DistanceKm / effectiveSpeed;
    }

    #endregion

    #region Path Helpers

    /// <summary>
    /// Builds an adjacency list from graph edges, mapping each location to its outgoing edges.
    /// </summary>
    /// <param name="edges">The graph edges to organize.</param>
    /// <returns>Adjacency list mapping location IDs to their outgoing edges.</returns>
    private static Dictionary<Guid, List<ConnectionGraphEntry>> BuildAdjacencyList(
        List<ConnectionGraphEntry> edges)
    {
        var adjacency = new Dictionary<Guid, List<ConnectionGraphEntry>>();
        foreach (var edge in edges)
        {
            if (!adjacency.TryGetValue(edge.FromLocationId, out var edgeList))
            {
                edgeList = new List<ConnectionGraphEntry>();
                adjacency[edge.FromLocationId] = edgeList;
            }
            edgeList.Add(edge);
        }
        return adjacency;
    }

    /// <summary>
    /// Reconstructs the path from source to target using the predecessor map from Dijkstra.
    /// </summary>
    /// <param name="prev">Predecessor map.</param>
    /// <param name="source">Source location ID.</param>
    /// <param name="target">Target location ID.</param>
    /// <returns>Ordered list of (edge, mode) tuples forming the path.</returns>
    private static List<(ConnectionGraphEntry Edge, string ModeCode)> ReconstructPath(
        Dictionary<Guid, (Guid PrevNode, ConnectionGraphEntry Edge, string ModeCode)> prev,
        Guid source,
        Guid target)
    {
        var path = new List<(ConnectionGraphEntry Edge, string ModeCode)>();
        var current = target;

        while (current != source)
        {
            if (!prev.TryGetValue(current, out var predecessor))
            {
                return new List<(ConnectionGraphEntry Edge, string ModeCode)>();
            }
            path.Add((predecessor.Edge, predecessor.ModeCode));
            current = predecessor.PrevNode;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Checks whether two paths share the same root (first <paramref name="rootLength"/> edges).
    /// </summary>
    /// <param name="pathA">First path to compare.</param>
    /// <param name="pathB">Second path to compare.</param>
    /// <param name="rootLength">Number of edges in the shared root to check.</param>
    /// <returns>True if both paths share the same first rootLength edges.</returns>
    private static bool SharesRootPath(
        List<(ConnectionGraphEntry Edge, string ModeCode)> pathA,
        List<(ConnectionGraphEntry Edge, string ModeCode)> pathB,
        int rootLength)
    {
        for (var j = 0; j < rootLength; j++)
        {
            if (pathA[j].Edge.ConnectionId != pathB[j].Edge.ConnectionId
                || pathA[j].Edge.FromLocationId != pathB[j].Edge.FromLocationId)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Checks whether a path is a duplicate of any existing path in the list.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <param name="existingPaths">List of already-found paths.</param>
    /// <returns>True if the path is a duplicate.</returns>
    private static bool IsPathDuplicate(
        List<(ConnectionGraphEntry Edge, string ModeCode)> path,
        List<List<(ConnectionGraphEntry Edge, string ModeCode)>> existingPaths)
    {
        var pathKey = FormatPathKey(path);
        foreach (var existing in existingPaths)
        {
            if (FormatPathKey(existing) == pathKey)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Formats a unique string key for an edge direction.
    /// </summary>
    /// <param name="edge">The graph edge.</param>
    /// <returns>A string uniquely identifying this edge direction.</returns>
    private static string FormatEdgeKey(ConnectionGraphEntry edge) =>
        $"{edge.ConnectionId}:{edge.FromLocationId}:{edge.ToLocationId}";

    /// <summary>
    /// Formats a unique string key for an entire path for duplicate detection.
    /// </summary>
    /// <param name="path">The path to format.</param>
    /// <returns>A string uniquely identifying this path.</returns>
    private static string FormatPathKey(
        List<(ConnectionGraphEntry Edge, string ModeCode)> path) =>
        string.Join("|", path.Select(p => $"{p.Edge.ConnectionId}:{p.Edge.FromLocationId}"));

    #endregion

    #region Result Building

    /// <summary>
    /// Builds a <see cref="RouteCalculationResult"/> from a path found by the algorithm.
    /// </summary>
    /// <param name="path">The path as a list of (edge, mode code) tuples.</param>
    /// <param name="modes">Available transit modes.</param>
    /// <param name="request">Route calculation parameters.</param>
    /// <param name="rank">The rank (1 = best) of this route option.</param>
    /// <returns>A route calculation result, or null if the path is empty.</returns>
    private RouteCalculationResult? BuildRouteResult(
        List<(ConnectionGraphEntry Edge, string ModeCode)> path,
        Dictionary<string, TransitModeModel> modes,
        RouteCalculationRequest request,
        int rank)
    {
        if (path.Count == 0)
        {
            return null;
        }

        var waypoints = new List<Guid> { path[0].Edge.FromLocationId };
        var connections = new List<Guid>();
        var legModes = new List<string>();
        var totalDistanceKm = 0m;
        var totalGameHours = 0m;
        var totalRisk = 0m;
        var maxRisk = 0m;
        var allLegsOpen = true;
        var seasonalWarnings = new List<SeasonalWarningResult>();

        for (var legIndex = 0; legIndex < path.Count; legIndex++)
        {
            var (edge, modeCode) = path[legIndex];

            waypoints.Add(edge.ToLocationId);
            connections.Add(edge.ConnectionId);
            legModes.Add(modeCode);
            totalDistanceKm += edge.DistanceKm;

            var travelTime = ComputeTravelTime(edge, modeCode, modes, request);

            // Add waypoint transfer time if present
            if (edge.WaypointTransferTimeGameHours.HasValue)
            {
                travelTime += edge.WaypointTransferTimeGameHours.Value;
            }

            totalGameHours += travelTime;
            totalRisk += edge.BaseRiskLevel;
            maxRisk = Math.Max(maxRisk, edge.BaseRiskLevel);

            if (edge.Status != ConnectionStatus.Open)
            {
                allLegsOpen = false;
            }

            // Build seasonal warnings for connections with seasonal closures
            if (edge.SeasonalAvailability != null)
            {
                foreach (var seasonal in edge.SeasonalAvailability)
                {
                    if (!seasonal.Available)
                    {
                        seasonalWarnings.Add(new SeasonalWarningResult(
                            ConnectionId: edge.ConnectionId,
                            ConnectionName: edge.Name,
                            LegIndex: legIndex,
                            CurrentSeason: "current",
                            ClosingSeason: seasonal.Season,
                            ClosingSeasonIndex: 1));
                    }
                }
            }
        }

        var averageRisk = path.Count > 0 ? totalRisk / path.Count : 0m;

        // Convert game-hours to approximate real minutes using current time ratio
        // time_ratio = game-seconds per real-second, so:
        // game-hours * 3600 game-seconds / time_ratio = real-seconds
        // real-seconds / 60 = real-minutes
        var totalRealMinutes = request.CurrentTimeRatio > 0
            ? totalGameHours * 3600m / request.CurrentTimeRatio / 60m
            : 0m;

        // Determine primary mode code: the mode used for the most legs (plurality)
        var primaryMode = legModes
            .GroupBy(m => m)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;

        return new RouteCalculationResult(
            Waypoints: waypoints,
            Connections: connections,
            LegModes: legModes,
            TotalDistanceKm: Math.Round(totalDistanceKm, 2),
            TotalGameHours: Math.Round(totalGameHours, 2),
            TotalRealMinutes: Math.Round(totalRealMinutes, 2),
            AverageRisk: Math.Round(averageRisk, 4),
            MaxLegRisk: maxRisk,
            AllLegsOpen: allLegsOpen,
            SeasonalWarnings: seasonalWarnings.Count > 0 ? seasonalWarnings : null);
    }

    #endregion

    #region Internal Types

    /// <summary>
    /// Represents a candidate path in Yen's algorithm with its total cost and unique ID.
    /// </summary>
    /// <param name="TotalCost">The total cost of this candidate path.</param>
    /// <param name="Id">Unique identifier for deterministic tie-breaking in the sorted set.</param>
    /// <param name="Path">The complete path from origin to destination.</param>
    private record CandidatePath(
        decimal TotalCost,
        int Id,
        List<(ConnectionGraphEntry Edge, string ModeCode)> Path);

    /// <summary>
    /// Comparer for candidate paths: orders by total cost ascending, breaks ties by ID ascending.
    /// </summary>
    private sealed class CandidatePathComparer : IComparer<CandidatePath>
    {
        /// <inheritdoc/>
        public int Compare(CandidatePath? x, CandidatePath? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var costComparison = x.TotalCost.CompareTo(y.TotalCost);
            return costComparison != 0 ? costComparison : x.Id.CompareTo(y.Id);
        }
    }

    #endregion
}
