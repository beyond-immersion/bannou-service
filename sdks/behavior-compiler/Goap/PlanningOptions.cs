// ═══════════════════════════════════════════════════════════════════════════
// GOAP Planning Options
// Configuration for the A* planner.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.BehaviorCompiler.Goap;

/// <summary>
/// Configuration options for GOAP planning.
/// </summary>
public sealed class PlanningOptions
{
    /// <summary>
    /// Maximum depth of the search tree (number of actions in a plan).
    /// Default: 10
    /// </summary>
    public int MaxDepth { get; init; } = 10;

    /// <summary>
    /// Maximum number of nodes to expand during search.
    /// Prevents runaway planning in complex action spaces.
    /// Default: 1000
    /// </summary>
    public int MaxNodesExpanded { get; init; } = 1000;

    /// <summary>
    /// Planning timeout in milliseconds.
    /// Default: 100ms
    /// </summary>
    public int TimeoutMs { get; init; } = 100;

    /// <summary>
    /// Whether to allow duplicate actions in a plan.
    /// Default: true (same action can appear multiple times)
    /// </summary>
    public bool AllowDuplicateActions { get; init; } = true;

    /// <summary>
    /// Heuristic weight for A* search.
    /// Higher values make search faster but potentially suboptimal.
    /// Default: 1.0 (standard A*)
    /// </summary>
    public float HeuristicWeight { get; init; } = 1.0f;

    /// <summary>
    /// Maximum total plan cost. Nodes whose accumulated gCost exceeds this
    /// bound are pruned during A* expansion, avoiding exploration of
    /// prohibitively expensive plans. Null means no cost limit (default).
    /// </summary>
    public float? MaxCostBound { get; init; }

    /// <summary>
    /// Default planning options.
    /// </summary>
    public static PlanningOptions Default { get; } = new();

    /// <summary>
    /// Fast planning options for real-time use.
    /// Shorter timeout, fewer nodes.
    /// </summary>
    public static PlanningOptions Fast { get; } = new()
    {
        MaxDepth = 5,
        MaxNodesExpanded = 500,
        TimeoutMs = 50,
        HeuristicWeight = 1.2f
    };

    /// <summary>
    /// Thorough planning options for complex scenarios.
    /// Longer timeout, more nodes, deeper search.
    /// </summary>
    public static PlanningOptions Thorough { get; } = new()
    {
        MaxDepth = 20,
        MaxNodesExpanded = 5000,
        TimeoutMs = 500,
        HeuristicWeight = 1.0f
    };

    /// <summary>
    /// Creates options with a specific timeout.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    /// <returns>New options with the specified timeout.</returns>
    public PlanningOptions WithTimeout(int timeoutMs)
    {
        return new PlanningOptions
        {
            MaxDepth = MaxDepth,
            MaxNodesExpanded = MaxNodesExpanded,
            TimeoutMs = timeoutMs,
            AllowDuplicateActions = AllowDuplicateActions,
            HeuristicWeight = HeuristicWeight,
            MaxCostBound = MaxCostBound
        };
    }

    /// <summary>
    /// Creates options with a specific max depth.
    /// </summary>
    /// <param name="maxDepth">Maximum plan depth.</param>
    /// <returns>New options with the specified max depth.</returns>
    public PlanningOptions WithMaxDepth(int maxDepth)
    {
        return new PlanningOptions
        {
            MaxDepth = maxDepth,
            MaxNodesExpanded = MaxNodesExpanded,
            TimeoutMs = TimeoutMs,
            AllowDuplicateActions = AllowDuplicateActions,
            HeuristicWeight = HeuristicWeight,
            MaxCostBound = MaxCostBound
        };
    }

    /// <summary>
    /// Creates options with a specific max cost bound.
    /// </summary>
    /// <param name="maxCostBound">Maximum total plan cost, or null for no limit.</param>
    /// <returns>New options with the specified cost bound.</returns>
    public PlanningOptions WithMaxCostBound(float? maxCostBound)
    {
        return new PlanningOptions
        {
            MaxDepth = MaxDepth,
            MaxNodesExpanded = MaxNodesExpanded,
            TimeoutMs = TimeoutMs,
            AllowDuplicateActions = AllowDuplicateActions,
            HeuristicWeight = HeuristicWeight,
            MaxCostBound = maxCostBound
        };
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var costBound = MaxCostBound.HasValue ? $", maxCost={MaxCostBound.Value}" : "";
        return $"PlanningOptions(depth={MaxDepth}, nodes={MaxNodesExpanded}, timeout={TimeoutMs}ms{costBound})";
    }
}
