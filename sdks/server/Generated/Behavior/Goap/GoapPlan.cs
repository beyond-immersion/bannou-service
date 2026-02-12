// ═══════════════════════════════════════════════════════════════════════════
// GOAP Plan
// Represents a plan result from the A* planner.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.Server.Behavior.Goap;

/// <summary>
/// Represents a planned action in a GOAP plan.
/// </summary>
public sealed class PlannedAction
{
    /// <summary>
    /// The action to execute.
    /// </summary>
    public GoapAction Action { get; }

    /// <summary>
    /// Index of this action in the plan sequence.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Creates a new planned action.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="index">Index in plan.</param>
    public PlannedAction(GoapAction action, int index)
    {
        Action = action;
        Index = index;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"[{Index}] {Action.Id} (cost={Action.Cost})";
    }
}

/// <summary>
/// Represents a GOAP plan - a sequence of actions to achieve a goal.
/// </summary>
public sealed class GoapPlan
{
    /// <summary>
    /// The goal this plan achieves.
    /// </summary>
    public GoapGoal Goal { get; }

    /// <summary>
    /// Ordered sequence of actions to execute.
    /// </summary>
    public IReadOnlyList<PlannedAction> Actions { get; }

    /// <summary>
    /// Total cost of all actions in the plan.
    /// </summary>
    public float TotalCost { get; }

    /// <summary>
    /// Number of nodes expanded during planning.
    /// </summary>
    public int NodesExpanded { get; }

    /// <summary>
    /// Time spent planning in milliseconds.
    /// </summary>
    public long PlanningTimeMs { get; }

    /// <summary>
    /// The initial world state the plan was generated from.
    /// </summary>
    public WorldState InitialState { get; }

    /// <summary>
    /// The expected world state after plan execution.
    /// </summary>
    public WorldState ExpectedFinalState { get; }

    /// <summary>
    /// Creates a new GOAP plan.
    /// </summary>
    /// <param name="goal">Goal being achieved.</param>
    /// <param name="actions">Ordered actions.</param>
    /// <param name="totalCost">Total plan cost.</param>
    /// <param name="nodesExpanded">Nodes expanded during search.</param>
    /// <param name="planningTimeMs">Planning time in ms.</param>
    /// <param name="initialState">Initial world state.</param>
    /// <param name="expectedFinalState">Expected final state.</param>
    public GoapPlan(
        GoapGoal goal,
        IReadOnlyList<PlannedAction> actions,
        float totalCost,
        int nodesExpanded,
        long planningTimeMs,
        WorldState initialState,
        WorldState expectedFinalState)
    {
        Goal = goal;
        Actions = actions;
        TotalCost = totalCost;
        NodesExpanded = nodesExpanded;
        PlanningTimeMs = planningTimeMs;
        InitialState = initialState;
        ExpectedFinalState = expectedFinalState;
    }

    /// <summary>
    /// Creates an empty plan (goal already satisfied).
    /// </summary>
    /// <param name="goal">Goal that's already satisfied.</param>
    /// <param name="currentState">Current world state.</param>
    /// <param name="planningTimeMs">Planning time in ms.</param>
    /// <returns>Empty plan.</returns>
    public static GoapPlan Empty(GoapGoal goal, WorldState currentState, long planningTimeMs = 0)
    {
        return new GoapPlan(
            goal,
            Array.Empty<PlannedAction>(),
            0f,
            0,
            planningTimeMs,
            currentState,
            currentState);
    }

    /// <summary>
    /// Gets the number of actions in the plan.
    /// </summary>
    public int ActionCount => Actions.Count;

    /// <summary>
    /// Checks if the plan is empty (goal was already satisfied).
    /// </summary>
    public bool IsEmpty => Actions.Count == 0;

    /// <summary>
    /// Gets the action at a specific index.
    /// </summary>
    /// <param name="index">Action index.</param>
    /// <returns>The planned action.</returns>
    public PlannedAction GetAction(int index)
    {
        if (index < 0 || index >= Actions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return Actions[index];
    }

    /// <summary>
    /// Gets the action IDs as a list.
    /// </summary>
    /// <returns>List of action IDs in order.</returns>
    public IReadOnlyList<string> GetActionIds()
    {
        return Actions.Select(a => a.Action.Id).ToList();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var actionStr = string.Join(" -> ", Actions.Select(a => a.Action.Id));
        return $"GoapPlan(goal={Goal.Id}, actions=[{actionStr}], cost={TotalCost}, nodes={NodesExpanded}, time={PlanningTimeMs}ms)";
    }
}
