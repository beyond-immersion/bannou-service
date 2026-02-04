using BeyondImmersion.Bannou.StorylineTheory.State;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Represents a discrete narrative action that modifies story state.
/// Actions are the building blocks of GOAP planning for stories.
/// </summary>
/// <param name="Code">Unique identifier for the action.</param>
/// <param name="Name">Human-readable name.</param>
/// <param name="Description">What this action represents narratively.</param>
/// <param name="Preconditions">Required state ranges for this action to be valid.</param>
/// <param name="Effects">State changes this action produces (delta values, 0.5 = no change).</param>
/// <param name="Cost">Base cost for GOAP planning (lower = preferred).</param>
/// <param name="Category">Functional category for filtering.</param>
public sealed record NarrativeAction(
    string Code,
    string Name,
    string Description,
    NarrativeStateRange Preconditions,
    NarrativeState Effects,
    double Cost,
    ActionCategory Category)
{
    /// <summary>
    /// Checks if this action can be taken from the given state.
    /// </summary>
    /// <param name="state">Current narrative state.</param>
    /// <returns>True if preconditions are satisfied.</returns>
    public bool CanApply(NarrativeState state) => Preconditions.Contains(state);

    /// <summary>
    /// Applies this action to a state, returning the resulting state.
    /// </summary>
    /// <param name="state">Current narrative state.</param>
    /// <returns>New state with effects applied.</returns>
    public NarrativeState Apply(NarrativeState state) => state.ApplyDelta(Effects);

    /// <summary>
    /// Gets the effective cost for GOAP planning, considering state context.
    /// </summary>
    /// <param name="currentState">Current state.</param>
    /// <param name="goalState">Target state.</param>
    /// <returns>Adjusted cost for this action.</returns>
    public double GetEffectiveCost(NarrativeState currentState, NarrativeState goalState)
    {
        // Base cost, modified by how much this action helps reach the goal
        var beforeDistance = currentState.DistanceTo(goalState);
        var afterDistance = Apply(currentState).DistanceTo(goalState);

        // If action moves us closer, reduce cost; if away, increase cost
        var improvement = beforeDistance - afterDistance;
        return Cost - improvement * 0.5;
    }
}

/// <summary>
/// Represents valid ranges for narrative state dimensions.
/// Used for action preconditions.
/// </summary>
public sealed class NarrativeStateRange
{
    /// <summary>
    /// Minimum tension required (null = no minimum).
    /// </summary>
    public double? MinTension { get; init; }

    /// <summary>
    /// Maximum tension allowed (null = no maximum).
    /// </summary>
    public double? MaxTension { get; init; }

    /// <summary>
    /// Minimum stakes required.
    /// </summary>
    public double? MinStakes { get; init; }

    /// <summary>
    /// Maximum stakes allowed.
    /// </summary>
    public double? MaxStakes { get; init; }

    /// <summary>
    /// Minimum mystery required.
    /// </summary>
    public double? MinMystery { get; init; }

    /// <summary>
    /// Maximum mystery allowed.
    /// </summary>
    public double? MaxMystery { get; init; }

    /// <summary>
    /// Minimum urgency required.
    /// </summary>
    public double? MinUrgency { get; init; }

    /// <summary>
    /// Maximum urgency allowed.
    /// </summary>
    public double? MaxUrgency { get; init; }

    /// <summary>
    /// Minimum intimacy required.
    /// </summary>
    public double? MinIntimacy { get; init; }

    /// <summary>
    /// Maximum intimacy allowed.
    /// </summary>
    public double? MaxIntimacy { get; init; }

    /// <summary>
    /// Minimum hope required.
    /// </summary>
    public double? MinHope { get; init; }

    /// <summary>
    /// Maximum hope allowed.
    /// </summary>
    public double? MaxHope { get; init; }

    /// <summary>
    /// Range that allows any state.
    /// </summary>
    public static NarrativeStateRange Any { get; } = new();

    /// <summary>
    /// Checks if a state falls within this range.
    /// </summary>
    public bool Contains(NarrativeState state)
    {
        if (MinTension.HasValue && state.Tension < MinTension.Value) return false;
        if (MaxTension.HasValue && state.Tension > MaxTension.Value) return false;
        if (MinStakes.HasValue && state.Stakes < MinStakes.Value) return false;
        if (MaxStakes.HasValue && state.Stakes > MaxStakes.Value) return false;
        if (MinMystery.HasValue && state.Mystery < MinMystery.Value) return false;
        if (MaxMystery.HasValue && state.Mystery > MaxMystery.Value) return false;
        if (MinUrgency.HasValue && state.Urgency < MinUrgency.Value) return false;
        if (MaxUrgency.HasValue && state.Urgency > MaxUrgency.Value) return false;
        if (MinIntimacy.HasValue && state.Intimacy < MinIntimacy.Value) return false;
        if (MaxIntimacy.HasValue && state.Intimacy > MaxIntimacy.Value) return false;
        if (MinHope.HasValue && state.Hope < MinHope.Value) return false;
        if (MaxHope.HasValue && state.Hope > MaxHope.Value) return false;
        return true;
    }
}

/// <summary>
/// Categories of narrative actions.
/// </summary>
public enum ActionCategory
{
    /// <summary>Actions that escalate conflict.</summary>
    Escalation,

    /// <summary>Actions that de-escalate or resolve.</summary>
    Resolution,

    /// <summary>Actions that reveal information.</summary>
    Revelation,

    /// <summary>Actions that deepen relationships.</summary>
    Bonding,

    /// <summary>Actions that create new problems.</summary>
    Complication,

    /// <summary>Actions that change emotional tone.</summary>
    ToneShift
}
