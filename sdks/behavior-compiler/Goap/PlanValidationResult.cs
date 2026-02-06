// ═══════════════════════════════════════════════════════════════════════════
// GOAP Plan Validation Result
// Result of validating an existing plan against current world state.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.BehaviorCompiler.Goap;

/// <summary>
/// Reason why a plan might need to be regenerated.
/// </summary>
public enum ReplanReason
{
    /// <summary>No replan needed, plan is still valid.</summary>
    None,

    /// <summary>The current action's preconditions are no longer satisfied.</summary>
    PreconditionInvalidated,

    /// <summary>An action in the plan failed during execution.</summary>
    ActionFailed,

    /// <summary>A higher priority goal is now available.</summary>
    BetterGoalAvailable,

    /// <summary>The plan has completed successfully.</summary>
    PlanCompleted,

    /// <summary>The goal has been achieved externally.</summary>
    GoalAlreadySatisfied,

    /// <summary>The plan is no longer optimal given world state changes.</summary>
    SuboptimalPlan
}

/// <summary>
/// Suggested action after plan validation.
/// </summary>
public enum ValidationSuggestion
{
    /// <summary>Continue executing the current plan.</summary>
    Continue,

    /// <summary>Generate a new plan from the current state.</summary>
    Replan,

    /// <summary>Abort the current plan without replanning.</summary>
    Abort
}

/// <summary>
/// Result of validating an existing GOAP plan against current world state.
/// </summary>
public sealed class PlanValidationResult
{
    /// <summary>
    /// Whether the plan is still valid.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Reason for potential replan.
    /// </summary>
    public ReplanReason Reason { get; }

    /// <summary>
    /// Suggested action based on validation.
    /// </summary>
    public ValidationSuggestion Suggestion { get; }

    /// <summary>
    /// Index in the plan where validation failed (if applicable).
    /// -1 if not applicable.
    /// </summary>
    public int InvalidatedAtIndex { get; }

    /// <summary>
    /// Additional message describing the validation result.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// If BetterGoalAvailable, the goal that should be pursued instead.
    /// </summary>
    public GoapGoal? BetterGoal { get; }

    private PlanValidationResult(
        bool isValid,
        ReplanReason reason,
        ValidationSuggestion suggestion,
        int invalidatedAtIndex = -1,
        string? message = null,
        GoapGoal? betterGoal = null)
    {
        IsValid = isValid;
        Reason = reason;
        Suggestion = suggestion;
        InvalidatedAtIndex = invalidatedAtIndex;
        Message = message;
        BetterGoal = betterGoal;
    }

    /// <summary>
    /// Creates a valid result (plan can continue).
    /// </summary>
    /// <returns>Valid result.</returns>
    public static PlanValidationResult Valid()
    {
        return new PlanValidationResult(
            isValid: true,
            reason: ReplanReason.None,
            suggestion: ValidationSuggestion.Continue);
    }

    /// <summary>
    /// Creates a result indicating plan is complete.
    /// </summary>
    /// <returns>Completed result.</returns>
    public static PlanValidationResult Completed()
    {
        return new PlanValidationResult(
            isValid: true,
            reason: ReplanReason.PlanCompleted,
            suggestion: ValidationSuggestion.Abort,
            message: "Plan has completed successfully");
    }

    /// <summary>
    /// Creates a result indicating goal is already satisfied.
    /// </summary>
    /// <returns>Goal satisfied result.</returns>
    public static PlanValidationResult GoalSatisfied()
    {
        return new PlanValidationResult(
            isValid: true,
            reason: ReplanReason.GoalAlreadySatisfied,
            suggestion: ValidationSuggestion.Abort,
            message: "Goal is already satisfied");
    }

    /// <summary>
    /// Creates a result indicating precondition failure.
    /// </summary>
    /// <param name="actionIndex">Index of the action with invalid preconditions.</param>
    /// <param name="message">Description of what precondition failed.</param>
    /// <returns>Precondition invalidated result.</returns>
    public static PlanValidationResult PreconditionInvalidated(int actionIndex, string? message = null)
    {
        return new PlanValidationResult(
            isValid: false,
            reason: ReplanReason.PreconditionInvalidated,
            suggestion: ValidationSuggestion.Replan,
            invalidatedAtIndex: actionIndex,
            message: message ?? $"Precondition invalidated at action {actionIndex}");
    }

    /// <summary>
    /// Creates a result indicating action failure.
    /// </summary>
    /// <param name="actionIndex">Index of the failed action.</param>
    /// <param name="message">Description of the failure.</param>
    /// <returns>Action failed result.</returns>
    public static PlanValidationResult ActionFailed(int actionIndex, string? message = null)
    {
        return new PlanValidationResult(
            isValid: false,
            reason: ReplanReason.ActionFailed,
            suggestion: ValidationSuggestion.Replan,
            invalidatedAtIndex: actionIndex,
            message: message ?? $"Action failed at index {actionIndex}");
    }

    /// <summary>
    /// Creates a result indicating a better goal is available.
    /// </summary>
    /// <param name="betterGoal">The higher-priority goal.</param>
    /// <returns>Better goal available result.</returns>
    public static PlanValidationResult BetterGoalAvailable(GoapGoal betterGoal)
    {
        return new PlanValidationResult(
            isValid: false,
            reason: ReplanReason.BetterGoalAvailable,
            suggestion: ValidationSuggestion.Replan,
            message: $"Higher priority goal available: {betterGoal.Id}",
            betterGoal: betterGoal);
    }

    /// <summary>
    /// Creates a result indicating the current plan is suboptimal.
    /// </summary>
    /// <param name="message">Description of why the plan is suboptimal.</param>
    /// <returns>Suboptimal plan result.</returns>
    public static PlanValidationResult SuboptimalPlan(string? message = null)
    {
        return new PlanValidationResult(
            isValid: false,
            reason: ReplanReason.SuboptimalPlan,
            suggestion: ValidationSuggestion.Replan,
            message: message ?? "A better plan may be available");
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"PlanValidationResult(valid={IsValid}, reason={Reason}, suggestion={Suggestion})";
    }
}
