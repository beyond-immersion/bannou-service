using BeyondImmersion.Bannou.StorylineTheory.Scoring;
using BeyondImmersion.Bannou.StorylineTheory.State;
using BeyondImmersion.Bannou.StorylineStoryteller.Actions;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Planning;

/// <summary>
/// A* planner for finding narrative action sequences that reach goal states.
/// Uses narrative state distance as the heuristic.
/// </summary>
public sealed class StoryPlanner
{
    /// <summary>
    /// Default threshold for considering goal reached.
    /// </summary>
    public const double DefaultGoalThreshold = 0.1;

    /// <summary>
    /// Plan configuration for urgency-based search parameters.
    /// </summary>
    public static class UrgencyTiers
    {
        /// <summary>Low urgency - thorough search, lower cost tolerance.</summary>
        public static PlannerConfig Low { get; } = new(
            MaxIterations: 5000,
            MaxDepth: 30,
            CostTolerance: 1.2);

        /// <summary>Medium urgency - balanced search.</summary>
        public static PlannerConfig Medium { get; } = new(
            MaxIterations: 2000,
            MaxDepth: 20,
            CostTolerance: 1.5);

        /// <summary>High urgency - fast search, higher cost tolerance.</summary>
        public static PlannerConfig High { get; } = new(
            MaxIterations: 500,
            MaxDepth: 10,
            CostTolerance: 2.0);
    }

    /// <summary>
    /// Plan a sequence of actions from current state to goal state.
    /// </summary>
    /// <param name="currentState">Starting narrative state.</param>
    /// <param name="goalState">Target narrative state.</param>
    /// <param name="availableActions">Actions to consider (defaults to all).</param>
    /// <param name="config">Planner configuration (defaults to Medium urgency).</param>
    /// <param name="goalThreshold">Distance threshold for goal satisfaction.</param>
    /// <returns>Plan result with action sequence if successful.</returns>
    public StoryPlan Plan(
        NarrativeState currentState,
        NarrativeState goalState,
        IReadOnlyList<NarrativeAction>? availableActions = null,
        PlannerConfig? config = null,
        double goalThreshold = DefaultGoalThreshold)
    {
        availableActions ??= NarrativeActions.All;
        config ??= UrgencyTiers.Medium;

        // Check if already at goal
        if (currentState.NormalizedDistanceTo(goalState) <= goalThreshold)
        {
            return new StoryPlan(
                Success: true,
                Actions: Array.Empty<NarrativeAction>(),
                FinalState: currentState,
                TotalCost: 0,
                IterationsUsed: 0,
                DistanceToGoal: currentState.NormalizedDistanceTo(goalState));
        }

        // A* search
        var openSet = new PriorityQueue<PlanNode, double>();
        var closedSet = new HashSet<string>();

        var startNode = new PlanNode(
            State: currentState,
            Actions: new List<NarrativeAction>(),
            GCost: 0,
            HCost: NarrativePotentialScorer.GoapHeuristic(currentState, goalState));

        openSet.Enqueue(startNode, startNode.FCost);
        var iterations = 0;

        while (openSet.Count > 0 && iterations < config.MaxIterations)
        {
            iterations++;
            var current = openSet.Dequeue();

            // Check if goal reached
            if (current.State.NormalizedDistanceTo(goalState) <= goalThreshold)
            {
                return new StoryPlan(
                    Success: true,
                    Actions: current.Actions,
                    FinalState: current.State,
                    TotalCost: current.GCost,
                    IterationsUsed: iterations,
                    DistanceToGoal: current.State.NormalizedDistanceTo(goalState));
            }

            // Check max depth
            if (current.Actions.Count >= config.MaxDepth)
                continue;

            // Generate state hash for closed set
            var stateHash = GetStateHash(current.State);
            if (!closedSet.Add(stateHash))
                continue;

            // Expand neighbors
            foreach (var action in availableActions)
            {
                if (!action.CanApply(current.State))
                    continue;

                var newState = action.Apply(current.State);
                var newStateHash = GetStateHash(newState);

                if (closedSet.Contains(newStateHash))
                    continue;

                var newActions = new List<NarrativeAction>(current.Actions) { action };
                var gCost = current.GCost + action.Cost;
                var hCost = NarrativePotentialScorer.GoapHeuristic(newState, goalState);

                // Apply cost tolerance - prune paths that are too expensive
                var bestPossibleCost = gCost + hCost;
                if (bestPossibleCost > config.CostTolerance * config.MaxDepth)
                    continue;

                var newNode = new PlanNode(
                    State: newState,
                    Actions: newActions,
                    GCost: gCost,
                    HCost: hCost);

                openSet.Enqueue(newNode, newNode.FCost);
            }
        }

        // Failed to find path
        return new StoryPlan(
            Success: false,
            Actions: Array.Empty<NarrativeAction>(),
            FinalState: currentState,
            TotalCost: 0,
            IterationsUsed: iterations,
            DistanceToGoal: currentState.NormalizedDistanceTo(goalState));
    }

    /// <summary>
    /// Plan incrementally, returning the best next action.
    /// Useful for real-time decision making.
    /// </summary>
    /// <param name="currentState">Current narrative state.</param>
    /// <param name="goalState">Target narrative state.</param>
    /// <param name="availableActions">Actions to consider.</param>
    /// <returns>Best next action, or null if no valid action improves state.</returns>
    public NarrativeAction? PlanNextAction(
        NarrativeState currentState,
        NarrativeState goalState,
        IReadOnlyList<NarrativeAction>? availableActions = null)
    {
        availableActions ??= NarrativeActions.All;

        var validActions = availableActions
            .Where(a => a.CanApply(currentState))
            .ToList();

        if (validActions.Count == 0)
            return null;

        // Score each action by resulting distance to goal
        var bestAction = validActions
            .Select(a => (Action: a, Distance: a.Apply(currentState).NormalizedDistanceTo(goalState)))
            .OrderBy(x => x.Distance)
            .First();

        // Only return if it improves things
        var currentDistance = currentState.NormalizedDistanceTo(goalState);
        return bestAction.Distance < currentDistance ? bestAction.Action : null;
    }

    /// <summary>
    /// Generate a state hash for closed set comparison.
    /// Quantizes state to avoid floating point comparison issues.
    /// </summary>
    private static string GetStateHash(NarrativeState state)
    {
        // Quantize to 0.05 increments (20 buckets per dimension)
        var t = (int)(state.Tension * 20);
        var s = (int)(state.Stakes * 20);
        var m = (int)(state.Mystery * 20);
        var u = (int)(state.Urgency * 20);
        var i = (int)(state.Intimacy * 20);
        var h = (int)(state.Hope * 20);

        return $"{t},{s},{m},{u},{i},{h}";
    }

    /// <summary>
    /// Internal node for A* search.
    /// </summary>
    private sealed record PlanNode(
        NarrativeState State,
        IReadOnlyList<NarrativeAction> Actions,
        double GCost,
        double HCost)
    {
        public double FCost => GCost + HCost;
    }
}

/// <summary>
/// Configuration for the story planner.
/// </summary>
/// <param name="MaxIterations">Maximum A* iterations before giving up.</param>
/// <param name="MaxDepth">Maximum action sequence length.</param>
/// <param name="CostTolerance">Multiplier for path cost pruning.</param>
public sealed record PlannerConfig(
    int MaxIterations,
    int MaxDepth,
    double CostTolerance);

/// <summary>
/// Result of story planning.
/// </summary>
/// <param name="Success">Whether a valid plan was found.</param>
/// <param name="Actions">Sequence of actions to take.</param>
/// <param name="FinalState">Resulting state after all actions.</param>
/// <param name="TotalCost">Sum of action costs.</param>
/// <param name="IterationsUsed">A* iterations consumed.</param>
/// <param name="DistanceToGoal">Final distance from goal state.</param>
public sealed record StoryPlan(
    bool Success,
    IReadOnlyList<NarrativeAction> Actions,
    NarrativeState FinalState,
    double TotalCost,
    int IterationsUsed,
    double DistanceToGoal)
{
    /// <summary>
    /// Gets a human-readable summary of the plan.
    /// </summary>
    public string GetSummary()
    {
        if (!Success)
            return $"Planning failed after {IterationsUsed} iterations. Distance to goal: {DistanceToGoal:F2}";

        if (Actions.Count == 0)
            return "Already at goal state.";

        var actionNames = string.Join(" → ", Actions.Select(a => a.Name));
        return $"Plan ({Actions.Count} actions, cost {TotalCost:F1}): {actionNames}";
    }
}
