// ═══════════════════════════════════════════════════════════════════════════
// GOAP A* Planner
// A* search implementation for GOAP planning.
// ═══════════════════════════════════════════════════════════════════════════

using System.Diagnostics;

namespace BeyondImmersion.Bannou.Behavior.Goap;

/// <summary>
/// A* search implementation for GOAP planning.
/// Thread-safe: all state is method-local.
/// </summary>
public sealed class GoapPlanner : IGoapPlanner
{
    /// <summary>
    /// Node in the A* search graph.
    /// </summary>
    private sealed class PlanNode
    {
        public WorldState State { get; }
        public GoapAction? Action { get; }
        public PlanNode? Parent { get; }
        public float GCost { get; }
        public float HCost { get; }
        public float FCost => GCost + HCost;
        public int Depth { get; }

        public PlanNode(
            WorldState state,
            GoapAction? action,
            PlanNode? parent,
            float gCost,
            float hCost)
        {
            State = state;
            Action = action;
            Parent = parent;
            GCost = gCost;
            HCost = hCost;
            Depth = parent != null ? parent.Depth + 1 : 0;
        }
    }

    /// <inheritdoc/>
    public ValueTask<GoapPlan?> PlanAsync(
        WorldState currentState,
        GoapGoal goal,
        IReadOnlyList<GoapAction> availableActions,
        PlanningOptions? options = null,
        CancellationToken ct = default)
    {

        options ??= PlanningOptions.Default;

        var stopwatch = Stopwatch.StartNew();

        // Check if goal is already satisfied
        if (currentState.SatisfiesGoal(goal))
        {
            return ValueTask.FromResult<GoapPlan?>(
                GoapPlan.Empty(goal, currentState, stopwatch.ElapsedMilliseconds));
        }

        // No actions available - can't plan
        if (availableActions.Count == 0)
        {
            return ValueTask.FromResult<GoapPlan?>(null);
        }

        // A* search
        var openSet = new PriorityQueue<PlanNode, float>();
        var closedSet = new HashSet<int>();
        var nodesExpanded = 0;

        // Start node
        var startHCost = currentState.DistanceToGoal(goal) * options.HeuristicWeight;
        var startNode = new PlanNode(currentState, null, null, 0, startHCost);
        openSet.Enqueue(startNode, startNode.FCost);

        PlanNode? goalNode = null;

        while (openSet.Count > 0)
        {
            // Check cancellation and timeout
            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (stopwatch.ElapsedMilliseconds > options.TimeoutMs)
            {
                break;
            }

            // Check node limit
            if (nodesExpanded >= options.MaxNodesExpanded)
            {
                break;
            }

            var current = openSet.Dequeue();
            nodesExpanded++;

            // Goal check
            if (current.State.SatisfiesGoal(goal))
            {
                goalNode = current;
                break;
            }

            // Add to closed set
            var stateHash = current.State.GetHashCode();
            if (!closedSet.Add(stateHash))
            {
                // Already visited this state
                continue;
            }

            // Depth limit check
            if (current.Depth >= options.MaxDepth)
            {
                continue;
            }

            // Expand neighbors (applicable actions)
            foreach (var action in availableActions)
            {
                // Check if action is applicable
                if (!action.IsApplicable(current.State))
                {
                    continue;
                }

                // Apply action to get new state
                var newState = action.Apply(current.State);
                var newStateHash = newState.GetHashCode();

                // Skip if already in closed set
                if (closedSet.Contains(newStateHash))
                {
                    continue;
                }

                // Calculate costs
                var gCost = current.GCost + action.Cost;
                var hCost = newState.DistanceToGoal(goal) * options.HeuristicWeight;
                var newNode = new PlanNode(newState, action, current, gCost, hCost);

                openSet.Enqueue(newNode, newNode.FCost);
            }
        }

        stopwatch.Stop();

        // No plan found
        if (goalNode == null)
        {
            return ValueTask.FromResult<GoapPlan?>(null);
        }

        // Reconstruct plan from goal node
        var plan = ReconstructPlan(goalNode, goal, nodesExpanded, stopwatch.ElapsedMilliseconds, currentState);
        return ValueTask.FromResult<GoapPlan?>(plan);
    }

    /// <inheritdoc/>
    public ValueTask<PlanValidationResult> ValidatePlanAsync(
        GoapPlan plan,
        int currentActionIndex,
        WorldState currentState,
        IReadOnlyList<GoapGoal>? activeGoals = null,
        CancellationToken ct = default)
    {

        // Plan completed?
        if (currentActionIndex >= plan.Actions.Count)
        {
            return ValueTask.FromResult(PlanValidationResult.Completed());
        }

        // Goal already satisfied?
        if (currentState.SatisfiesGoal(plan.Goal))
        {
            return ValueTask.FromResult(PlanValidationResult.GoalSatisfied());
        }

        // Check if current action's preconditions are still valid
        var currentAction = plan.Actions[currentActionIndex].Action;
        if (!currentAction.IsApplicable(currentState))
        {
            return ValueTask.FromResult(
                PlanValidationResult.PreconditionInvalidated(
                    currentActionIndex,
                    $"Action '{currentAction.Id}' preconditions no longer satisfied"));
        }

        // Check for higher priority goals
        if (activeGoals != null && activeGoals.Count > 0)
        {
            foreach (var goal in activeGoals)
            {
                // Skip if same goal or lower priority
                if (goal.Id == plan.Goal.Id || goal.Priority <= plan.Goal.Priority)
                {
                    continue;
                }

                // Check if this higher-priority goal is now actionable
                // (not already satisfied and we might want to pursue it)
                if (!currentState.SatisfiesGoal(goal))
                {
                    return ValueTask.FromResult(
                        PlanValidationResult.BetterGoalAvailable(goal));
                }
            }
        }

        // Plan is still valid
        return ValueTask.FromResult(PlanValidationResult.Valid());
    }

    private static GoapPlan ReconstructPlan(
        PlanNode goalNode,
        GoapGoal goal,
        int nodesExpanded,
        long planningTimeMs,
        WorldState initialState)
    {
        // Build action list by walking back from goal
        var actions = new List<PlannedAction>();
        var current = goalNode;

        while (current.Parent != null && current.Action != null)
        {
            actions.Add(new PlannedAction(current.Action, 0));
            current = current.Parent;
        }

        // Reverse to get correct order
        actions.Reverse();

        // Set correct indices
        for (var i = 0; i < actions.Count; i++)
        {
            actions[i] = new PlannedAction(actions[i].Action, i);
        }

        return new GoapPlan(
            goal: goal,
            actions: actions,
            totalCost: goalNode.GCost,
            nodesExpanded: nodesExpanded,
            planningTimeMs: planningTimeMs,
            initialState: initialState,
            expectedFinalState: goalNode.State);
    }
}
