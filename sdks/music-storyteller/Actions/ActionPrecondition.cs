using BeyondImmersion.Bannou.MusicStoryteller.State;
using BeyondImmersion.Bannou.MusicTheory.Harmony;

namespace BeyondImmersion.Bannou.MusicStoryteller.Actions;

/// <summary>
/// Represents a precondition that must be satisfied for an action to execute.
/// </summary>
public sealed class ActionPrecondition
{
    /// <summary>
    /// Description of this precondition.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Function to check if the precondition is satisfied.
    /// </summary>
    public Func<CompositionState, bool> Predicate { get; }

    /// <summary>
    /// Creates a precondition.
    /// </summary>
    /// <param name="description">Human-readable description.</param>
    /// <param name="predicate">Evaluation function.</param>
    public ActionPrecondition(string description, Func<CompositionState, bool> predicate)
    {
        Description = description;
        Predicate = predicate;
    }

    /// <summary>
    /// Checks if this precondition is satisfied.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>True if satisfied.</returns>
    public bool IsSatisfied(CompositionState state)
    {
        return Predicate(state);
    }

    /// <summary>
    /// Requires tension above a threshold.
    /// </summary>
    public static ActionPrecondition TensionAbove(double threshold)
        => new($"Tension > {threshold:F2}", s => s.Emotional.Tension > threshold);

    /// <summary>
    /// Requires tension below a threshold.
    /// </summary>
    public static ActionPrecondition TensionBelow(double threshold)
        => new($"Tension < {threshold:F2}", s => s.Emotional.Tension < threshold);

    /// <summary>
    /// Requires stability above a threshold.
    /// </summary>
    public static ActionPrecondition StabilityAbove(double threshold)
        => new($"Stability > {threshold:F2}", s => s.Emotional.Stability > threshold);

    /// <summary>
    /// Requires being on a specific harmonic function.
    /// </summary>
    public static ActionPrecondition OnFunction(HarmonicFunctionType function)
        => new($"On {function}", s => s.Harmonic.CurrentFunction == function);

    /// <summary>
    /// Requires NOT being on a specific harmonic function.
    /// </summary>
    public static ActionPrecondition NotOnFunction(HarmonicFunctionType function)
        => new($"Not on {function}", s => s.Harmonic.CurrentFunction != function);

    /// <summary>
    /// Requires being on tonic.
    /// </summary>
    public static ActionPrecondition OnTonic
        => OnFunction(HarmonicFunctionType.Tonic);

    /// <summary>
    /// Requires NOT being on tonic.
    /// </summary>
    public static ActionPrecondition NotOnTonic
        => NotOnFunction(HarmonicFunctionType.Tonic);

    /// <summary>
    /// Requires being on dominant.
    /// </summary>
    public static ActionPrecondition OnDominant
        => OnFunction(HarmonicFunctionType.Dominant);

    /// <summary>
    /// Requires cadence to be expected.
    /// </summary>
    public static ActionPrecondition ExpectingCadence
        => new("Cadence expected", s => s.Harmonic.ExpectingCadence);

    /// <summary>
    /// Requires main motif to be introduced.
    /// </summary>
    public static ActionPrecondition MainMotifIntroduced
        => new("Main motif introduced", s => s.Thematic.MainMotifIntroduced);

    /// <summary>
    /// Requires main motif to NOT be introduced.
    /// </summary>
    public static ActionPrecondition MainMotifNotIntroduced
        => new("Main motif not yet introduced", s => !s.Thematic.MainMotifIntroduced);

    /// <summary>
    /// Requires a minimum number of bars since main motif.
    /// </summary>
    public static ActionPrecondition BarsSinceMainMotifAtLeast(int bars)
        => new($"At least {bars} bars since main motif",
            s => s.Thematic.BarsSinceMainMotif >= bars);

    /// <summary>
    /// Requires being at a phrase boundary.
    /// </summary>
    public static ActionPrecondition AtPhraseBoundary
        => new("At phrase boundary", s => s.Position.IsPhraseBoundary);

    /// <summary>
    /// Requires being at a section boundary.
    /// </summary>
    public static ActionPrecondition AtSectionBoundary
        => new("At section boundary", s => s.Position.IsSectionBoundary);

    /// <summary>
    /// Requires overall progress to be above a threshold.
    /// </summary>
    public static ActionPrecondition ProgressAbove(double threshold)
        => new($"Progress > {threshold:P0}", s => s.Position.OverallProgress > threshold);

    /// <summary>
    /// Requires overall progress to be below a threshold.
    /// </summary>
    public static ActionPrecondition ProgressBelow(double threshold)
        => new($"Progress < {threshold:P0}", s => s.Position.OverallProgress < threshold);

    /// <summary>
    /// Requires listener attention above a threshold.
    /// </summary>
    public static ActionPrecondition AttentionAbove(double threshold)
        => new($"Attention > {threshold:F2}", s => s.Listener.Attention > threshold);

    /// <summary>
    /// Requires listener attention below a threshold.
    /// </summary>
    public static ActionPrecondition AttentionBelow(double threshold)
        => new($"Attention < {threshold:F2}", s => s.Listener.Attention < threshold);

    /// <summary>
    /// Combines multiple preconditions with AND logic.
    /// </summary>
    public static ActionPrecondition All(params ActionPrecondition[] preconditions)
    {
        var descriptions = string.Join(" AND ", preconditions.Select(p => p.Description));
        return new ActionPrecondition(descriptions, s => preconditions.All(p => p.IsSatisfied(s)));
    }

    /// <summary>
    /// Combines multiple preconditions with OR logic.
    /// </summary>
    public static ActionPrecondition Any(params ActionPrecondition[] preconditions)
    {
        var descriptions = string.Join(" OR ", preconditions.Select(p => p.Description));
        return new ActionPrecondition(descriptions, s => preconditions.Any(p => p.IsSatisfied(s)));
    }

    /// <inheritdoc />
    public override string ToString() => Description;
}
