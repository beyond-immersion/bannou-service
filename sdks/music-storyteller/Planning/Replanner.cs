using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Planning;

/// <summary>
/// Handles replanning when actual state diverges from expected state.
/// Monitors execution and triggers replanning as needed.
/// </summary>
public sealed class Replanner
{
    private readonly GOAPPlanner _planner;

    /// <summary>
    /// Threshold for state divergence that triggers replanning.
    /// </summary>
    public double DivergenceThreshold { get; init; } = 0.3;

    /// <summary>
    /// Minimum steps remaining before replanning is considered.
    /// If plan is almost complete, finish it.
    /// </summary>
    public int MinStepsForReplan { get; init; } = 2;

    /// <summary>
    /// Creates a replanner.
    /// </summary>
    /// <param name="planner">The GOAP planner to use.</param>
    public Replanner(GOAPPlanner planner)
    {
        _planner = planner;
    }

    /// <summary>
    /// Checks if replanning is needed given current state vs expected.
    /// </summary>
    /// <param name="expected">Expected world state from plan.</param>
    /// <param name="actual">Actual world state.</param>
    /// <returns>True if replanning is recommended.</returns>
    public bool NeedsReplan(WorldState expected, WorldState actual)
    {
        var distance = actual.DistanceTo(expected);
        return distance > DivergenceThreshold;
    }

    /// <summary>
    /// Evaluates whether to replan or continue with current plan.
    /// </summary>
    /// <param name="execution">Current plan execution.</param>
    /// <param name="actualState">Current actual world state.</param>
    /// <returns>Replan decision with details.</returns>
    public ReplanDecision Evaluate(PlanExecution execution, WorldState actualState)
    {
        // If plan is complete, no need to replan
        if (execution.IsComplete)
        {
            return new ReplanDecision
            {
                ShouldReplan = false,
                Reason = "Plan is complete"
            };
        }

        // Get expected state at current step
        var expectedState = execution.GetExpectedStateAtStep(execution.CurrentStep);
        var divergence = actualState.DistanceTo(expectedState);

        // Check divergence
        if (divergence <= DivergenceThreshold)
        {
            return new ReplanDecision
            {
                ShouldReplan = false,
                Reason = "State within tolerance",
                Divergence = divergence
            };
        }

        // Check if plan is almost complete
        var remainingSteps = execution.Plan.Length - execution.CurrentStep;
        if (remainingSteps < MinStepsForReplan)
        {
            return new ReplanDecision
            {
                ShouldReplan = false,
                Reason = $"Only {remainingSteps} steps remaining, completing plan",
                Divergence = divergence
            };
        }

        // Check if goal is still achievable with remaining actions
        var remainingPlan = execution.Plan.Skip(execution.CurrentStep);
        var projectedFinal = ProjectFinalState(actualState, remainingPlan);
        var goalDistance = projectedFinal.DistanceTo(execution.Plan.ExpectedFinalState);

        if (goalDistance <= DivergenceThreshold * 2)
        {
            return new ReplanDecision
            {
                ShouldReplan = false,
                Reason = "Goal still achievable with current plan",
                Divergence = divergence,
                ProjectedGoalDistance = goalDistance
            };
        }

        // Replanning recommended
        return new ReplanDecision
        {
            ShouldReplan = true,
            Reason = $"State diverged by {divergence:F2}, goal distance {goalDistance:F2}",
            Divergence = divergence,
            ProjectedGoalDistance = goalDistance
        };
    }

    /// <summary>
    /// Creates a new plan from the current state.
    /// </summary>
    /// <param name="currentState">Current actual world state.</param>
    /// <param name="goal">The goal to achieve.</param>
    /// <returns>A new plan.</returns>
    public Plan Replan(WorldState currentState, GOAPGoal goal)
    {
        return _planner.CreatePlan(currentState, goal);
    }

    /// <summary>
    /// Creates a new plan from a composition state.
    /// </summary>
    /// <param name="state">Current composition state.</param>
    /// <param name="goal">The goal to achieve.</param>
    /// <returns>A new plan.</returns>
    public Plan Replan(CompositionState state, GOAPGoal goal)
    {
        var worldState = WorldState.FromCompositionState(state);
        return _planner.CreatePlan(worldState, goal);
    }

    /// <summary>
    /// Attempts to repair the current plan by adjusting remaining actions.
    /// Less disruptive than full replan.
    /// </summary>
    /// <param name="execution">Current execution.</param>
    /// <param name="actualState">Actual world state.</param>
    /// <returns>Repaired plan, or null if repair not possible.</returns>
    public Plan? TryRepair(PlanExecution execution, WorldState actualState)
    {
        // Try a shortened search from current state
        var remainingGoal = new GOAPGoal
        {
            Id = execution.Plan.Goal.Id + "_repair",
            Name = "Repair: " + execution.Plan.Goal.Name,
            TargetState = execution.Plan.Goal.TargetState,
            Priority = execution.Plan.Goal.Priority,
            Tolerance = execution.Plan.Goal.Tolerance * 1.5 // More lenient
        };

        var repairPlanner = new GOAPPlanner([])
        {
            MaxDepth = Math.Min(5, execution.Plan.Length - execution.CurrentStep + 2),
            MaxNodesExplored = 200
        };

        var repairPlan = _planner.CreatePlan(actualState, remainingGoal);

        if (repairPlan.IsValid && repairPlan.Length <= execution.Plan.Length - execution.CurrentStep + 2)
        {
            return repairPlan;
        }

        return null;
    }

    private WorldState ProjectFinalState(WorldState start, Plan plan)
    {
        var state = start.Clone();
        foreach (var action in plan.Actions)
        {
            state = action.Apply(state);
        }
        return state;
    }
}

/// <summary>
/// Result of replan evaluation.
/// </summary>
public sealed class ReplanDecision
{
    /// <summary>
    /// Gets whether replanning is recommended.
    /// </summary>
    public required bool ShouldReplan { get; init; }

    /// <summary>
    /// Gets the reason for the decision.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets the measured divergence from expected state.
    /// </summary>
    public double Divergence { get; init; }

    /// <summary>
    /// Gets the projected distance to goal with current plan.
    /// </summary>
    public double ProjectedGoalDistance { get; init; }
}
