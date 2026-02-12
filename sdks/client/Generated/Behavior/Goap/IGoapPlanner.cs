// ═══════════════════════════════════════════════════════════════════════════
// GOAP Planner Interface
// Interface for GOAP planning implementations.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.Client.Behavior.Goap;

/// <summary>
/// Interface for GOAP planners.
/// </summary>
public interface IGoapPlanner
{
    /// <summary>
    /// Generates a plan to achieve a goal from the current world state.
    /// </summary>
    /// <param name="currentState">Current world state.</param>
    /// <param name="goal">Goal to achieve.</param>
    /// <param name="availableActions">Actions available for planning.</param>
    /// <param name="options">Planning options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A plan if one is found, null otherwise.</returns>
    ValueTask<GoapPlan?> PlanAsync(
        WorldState currentState,
        GoapGoal goal,
        IReadOnlyList<GoapAction> availableActions,
        PlanningOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validates an existing plan against the current world state.
    /// </summary>
    /// <param name="plan">Plan to validate.</param>
    /// <param name="currentActionIndex">Index of the current action being executed.</param>
    /// <param name="currentState">Current world state.</param>
    /// <param name="activeGoals">All active goals (for priority checking).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with suggestions.</returns>
    ValueTask<PlanValidationResult> ValidatePlanAsync(
        GoapPlan plan,
        int currentActionIndex,
        WorldState currentState,
        IReadOnlyList<GoapGoal>? activeGoals = null,
        CancellationToken ct = default);
}
