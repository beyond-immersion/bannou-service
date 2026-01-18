using BeyondImmersion.Bannou.MusicStoryteller.Actions;

namespace BeyondImmersion.Bannou.MusicStoryteller.Planning;

/// <summary>
/// Goal-Oriented Action Planning (GOAP) implementation for music composition.
/// Uses A* search to find optimal action sequences toward goals.
/// Source: Plan Phase 6 - GOAP Planning
/// </summary>
public sealed class GOAPPlanner
{
    private readonly IReadOnlyList<GOAPAction> _actions;

    /// <summary>
    /// Maximum depth for plan search.
    /// </summary>
    public int MaxDepth { get; init; } = 10;

    /// <summary>
    /// Maximum nodes to explore before giving up.
    /// </summary>
    public int MaxNodesExplored { get; init; } = 1000;

    /// <summary>
    /// Creates a GOAP planner with actions from a library.
    /// </summary>
    /// <param name="library">The action library.</param>
    public GOAPPlanner(ActionLibrary library)
    {
        _actions = GOAPAction.FromLibrary(library).ToList();
    }

    /// <summary>
    /// Creates a GOAP planner with specific actions.
    /// </summary>
    /// <param name="actions">The available actions.</param>
    public GOAPPlanner(IEnumerable<GOAPAction> actions)
    {
        _actions = actions.ToList();
    }

    /// <summary>
    /// Creates a plan to reach the goal from the current state.
    /// Uses A* search with action costs as edge weights.
    /// </summary>
    /// <param name="current">Current world state.</param>
    /// <param name="goal">Goal to achieve.</param>
    /// <returns>A plan to reach the goal, or a failed plan if no path exists.</returns>
    public Plan CreatePlan(WorldState current, GOAPGoal goal)
    {
        // Check if goal is already satisfied
        if (goal.IsSatisfied(current))
        {
            return Plan.Empty(goal, current);
        }

        // A* search
        var openSet = new PriorityQueue<PlanNode, double>();
        var closedSet = new HashSet<string>();
        var nodesExplored = 0;

        var startNode = new PlanNode
        {
            State = current,
            Actions = [],
            GCost = 0,
            HCost = goal.GetDistance(current)
        };

        openSet.Enqueue(startNode, startNode.FCost);

        while (openSet.Count > 0 && nodesExplored < MaxNodesExplored)
        {
            var currentNode = openSet.Dequeue();
            nodesExplored++;

            // Check if we've reached the goal
            if (goal.IsSatisfied(currentNode.State))
            {
                return BuildPlan(goal, currentNode);
            }

            // Skip if we've explored this state
            var stateKey = GetStateKey(currentNode.State);
            if (closedSet.Contains(stateKey))
                continue;

            closedSet.Add(stateKey);

            // Don't go too deep
            if (currentNode.Actions.Count >= MaxDepth)
                continue;

            // Expand with available actions
            foreach (var action in _actions)
            {
                if (!action.IsSatisfied(currentNode.State))
                    continue;

                var newState = action.Apply(currentNode.State);
                var newStateKey = GetStateKey(newState);

                if (closedSet.Contains(newStateKey))
                    continue;

                var newActions = currentNode.Actions.Append(action).ToList();
                var gCost = currentNode.GCost + action.CalculateCost(currentNode.State);
                var hCost = goal.GetDistance(newState);

                var newNode = new PlanNode
                {
                    State = newState,
                    Actions = newActions,
                    GCost = gCost,
                    HCost = hCost
                };

                openSet.Enqueue(newNode, newNode.FCost);
            }
        }

        // No path found
        return Plan.Failed(goal);
    }

    /// <summary>
    /// Creates a plan with multiple goal priorities.
    /// Tries higher priority goals first.
    /// </summary>
    /// <param name="current">Current world state.</param>
    /// <param name="goals">Goals in priority order.</param>
    /// <returns>A plan for the highest achievable goal.</returns>
    public Plan CreatePlan(WorldState current, IEnumerable<GOAPGoal> goals)
    {
        var sortedGoals = goals.OrderByDescending(g => g.Priority).ToList();

        foreach (var goal in sortedGoals)
        {
            var plan = CreatePlan(current, goal);
            if (plan.IsValid || plan.IsEmpty)
            {
                return plan;
            }
        }

        // Return failed plan for highest priority goal
        return Plan.Failed(sortedGoals.First());
    }

    /// <summary>
    /// Creates a plan constrained to specific action categories.
    /// </summary>
    /// <param name="current">Current world state.</param>
    /// <param name="goal">Goal to achieve.</param>
    /// <param name="allowedCategories">Categories of actions allowed.</param>
    /// <returns>A constrained plan.</returns>
    public Plan CreateConstrainedPlan(
        WorldState current,
        GOAPGoal goal,
        IReadOnlyList<ActionCategory> allowedCategories)
    {
        var filteredActions = _actions
            .Where(a => allowedCategories.Contains(a.MusicalAction.Category))
            .ToList();

        var constrainedPlanner = new GOAPPlanner(filteredActions)
        {
            MaxDepth = MaxDepth,
            MaxNodesExplored = MaxNodesExplored
        };

        return constrainedPlanner.CreatePlan(current, goal);
    }

    /// <summary>
    /// Estimates the minimum number of actions needed to reach a goal.
    /// </summary>
    /// <param name="current">Current world state.</param>
    /// <param name="goal">Goal to achieve.</param>
    /// <returns>Estimated action count, or -1 if unreachable.</returns>
    public int EstimateActionsNeeded(WorldState current, GOAPGoal goal)
    {
        if (goal.IsSatisfied(current))
            return 0;

        // Simple BFS to find shortest path
        var queue = new Queue<(WorldState state, int depth)>();
        var visited = new HashSet<string>();

        queue.Enqueue((current, 0));

        while (queue.Count > 0)
        {
            var (state, depth) = queue.Dequeue();

            if (depth > MaxDepth)
                continue;

            var stateKey = GetStateKey(state);
            if (visited.Contains(stateKey))
                continue;

            visited.Add(stateKey);

            if (goal.IsSatisfied(state))
                return depth;

            foreach (var action in _actions)
            {
                if (action.IsSatisfied(state))
                {
                    var newState = action.Apply(state);
                    queue.Enqueue((newState, depth + 1));
                }
            }
        }

        return -1; // Unreachable
    }

    private Plan BuildPlan(GOAPGoal goal, PlanNode node)
    {
        var totalCost = node.Actions.Sum(a => a.BaseCost);

        return new Plan
        {
            Goal = goal,
            Actions = node.Actions,
            TotalCost = totalCost,
            ExpectedFinalState = node.State
        };
    }

    private static string GetStateKey(WorldState state)
    {
        // Create a hash of the state for duplicate detection
        var parts = state.AllKeys
            .OrderBy(k => k)
            .Select(k =>
            {
                var val = state.Get<object>(k);
                if (val is double d)
                    return $"{k}:{Math.Round(d, 2)}";
                return $"{k}:{val}";
            });

        return string.Join("|", parts);
    }

    private sealed class PlanNode
    {
        public required WorldState State { get; init; }
        public required IReadOnlyList<GOAPAction> Actions { get; init; }
        public double GCost { get; init; }
        public double HCost { get; init; }
        public double FCost => GCost + HCost;
    }
}
