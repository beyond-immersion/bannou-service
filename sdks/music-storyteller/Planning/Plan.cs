using BeyondImmersion.Bannou.MusicStoryteller.Actions;

namespace BeyondImmersion.Bannou.MusicStoryteller.Planning;

/// <summary>
/// A sequence of planned actions to achieve a goal.
/// Result of GOAP planning.
/// </summary>
public sealed class Plan
{
    /// <summary>
    /// Gets the goal this plan aims to achieve.
    /// </summary>
    public required GOAPGoal Goal { get; init; }

    /// <summary>
    /// Gets the ordered list of actions in this plan.
    /// </summary>
    public required IReadOnlyList<GOAPAction> Actions { get; init; }

    /// <summary>
    /// Gets the total cost of executing this plan.
    /// </summary>
    public double TotalCost { get; init; }

    /// <summary>
    /// Gets the expected world state after executing this plan.
    /// </summary>
    public required WorldState ExpectedFinalState { get; init; }

    /// <summary>
    /// Gets whether this plan is valid (has actions and achieves goal).
    /// </summary>
    public bool IsValid => Actions.Count > 0;

    /// <summary>
    /// Gets whether this plan is empty (no actions needed).
    /// </summary>
    public bool IsEmpty => Actions.Count == 0;

    /// <summary>
    /// Gets the number of actions in this plan.
    /// </summary>
    public int Length => Actions.Count;

    /// <summary>
    /// Gets the action at a specific step.
    /// </summary>
    /// <param name="step">The step index (0-based).</param>
    /// <returns>The action at that step.</returns>
    public GOAPAction this[int step] => Actions[step];

    /// <summary>
    /// Gets the underlying musical actions.
    /// </summary>
    public IEnumerable<IMusicalAction> MusicalActions => Actions.Select(a => a.MusicalAction);

    /// <summary>
    /// Creates an empty plan (goal already satisfied).
    /// </summary>
    /// <param name="goal">The goal.</param>
    /// <param name="currentState">Current world state.</param>
    /// <returns>An empty plan.</returns>
    public static Plan Empty(GOAPGoal goal, WorldState currentState) => new()
    {
        Goal = goal,
        Actions = [],
        TotalCost = 0,
        ExpectedFinalState = currentState
    };

    /// <summary>
    /// Creates a failed plan (no path to goal found).
    /// </summary>
    /// <param name="goal">The goal.</param>
    /// <returns>A failed plan.</returns>
    public static Plan Failed(GOAPGoal goal) => new()
    {
        Goal = goal,
        Actions = [],
        TotalCost = double.MaxValue,
        ExpectedFinalState = new WorldState()
    };

    /// <summary>
    /// Gets a sub-plan starting from a specific step.
    /// </summary>
    /// <param name="startStep">The step to start from.</param>
    /// <returns>A new plan with remaining actions.</returns>
    public Plan Skip(int startStep)
    {
        if (startStep >= Actions.Count)
        {
            return Empty(Goal, ExpectedFinalState);
        }

        var remainingActions = Actions.Skip(startStep).ToList();
        var remainingCost = remainingActions.Sum(a => a.BaseCost);

        return new Plan
        {
            Goal = Goal,
            Actions = remainingActions,
            TotalCost = remainingCost,
            ExpectedFinalState = ExpectedFinalState
        };
    }

    /// <summary>
    /// Checks if a step is the last action in the plan.
    /// </summary>
    /// <param name="step">The step index.</param>
    /// <returns>True if this is the final step.</returns>
    public bool IsFinalStep(int step) => step >= Actions.Count - 1;
}

/// <summary>
/// Result of plan execution tracking.
/// </summary>
public sealed class PlanExecution
{
    /// <summary>
    /// Gets the plan being executed.
    /// </summary>
    public required Plan Plan { get; init; }

    /// <summary>
    /// Gets the current step in the plan.
    /// </summary>
    public int CurrentStep { get; set; }

    /// <summary>
    /// Gets whether the plan is complete.
    /// </summary>
    public bool IsComplete => CurrentStep >= Plan.Actions.Count;

    /// <summary>
    /// Gets the current action to execute.
    /// </summary>
    public GOAPAction? CurrentAction =>
        CurrentStep < Plan.Actions.Count ? Plan.Actions[CurrentStep] : null;

    /// <summary>
    /// Gets the progress through the plan (0-1).
    /// </summary>
    public double Progress => Plan.Actions.Count > 0
        ? (double)CurrentStep / Plan.Actions.Count
        : 1.0;

    /// <summary>
    /// Advances to the next step.
    /// </summary>
    /// <returns>True if there are more steps.</returns>
    public bool Advance()
    {
        if (IsComplete) return false;
        CurrentStep++;
        return !IsComplete;
    }

    /// <summary>
    /// Checks if the expected state matches the actual state.
    /// </summary>
    /// <param name="actualState">The actual world state.</param>
    /// <param name="tolerance">Tolerance for comparison.</param>
    /// <returns>True if states match within tolerance.</returns>
    public bool MatchesExpectedState(WorldState actualState, double tolerance = 0.2)
    {
        // Calculate expected state up to current step
        var expectedState = GetExpectedStateAtStep(CurrentStep);
        return actualState.DistanceTo(expectedState) <= tolerance * 3;
    }

    /// <summary>
    /// Gets the expected state after executing up to a specific step.
    /// </summary>
    /// <param name="step">The step to calculate state for.</param>
    /// <returns>Expected world state.</returns>
    public WorldState GetExpectedStateAtStep(int step)
    {
        if (step <= 0) return new WorldState();
        if (step >= Plan.Actions.Count) return Plan.ExpectedFinalState;

        // Apply actions sequentially up to the step
        var state = new WorldState();
        for (var i = 0; i < step; i++)
        {
            state = Plan.Actions[i].Apply(state);
        }
        return state;
    }

    /// <summary>
    /// Creates a new execution for a plan.
    /// </summary>
    /// <param name="plan">The plan to execute.</param>
    /// <returns>A new execution tracker.</returns>
    public static PlanExecution Start(Plan plan) => new()
    {
        Plan = plan,
        CurrentStep = 0
    };
}
