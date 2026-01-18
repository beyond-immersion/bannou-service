namespace BeyondImmersion.Bannou.MusicStoryteller.Narratives;

/// <summary>
/// A complete narrative template defining an emotional arc through music.
/// Templates provide the high-level structure that GOAP planning uses
/// to select appropriate musical actions.
/// Source: Plan Phase 5 - Narrative Templates
/// </summary>
public sealed class NarrativeTemplate
{
    /// <summary>
    /// Gets the unique identifier for this template.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name of this template.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a description of the emotional journey this template creates.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets evocative imagery descriptions for each phase.
    /// Used to inspire interpretation and help match templates to requests.
    /// </summary>
    public IReadOnlyList<string> Imagery { get; init; } = [];

    /// <summary>
    /// Gets the ordered phases that make up this narrative arc.
    /// Phase durations should sum to 1.0.
    /// </summary>
    public required IReadOnlyList<NarrativePhase> Phases { get; init; }

    /// <summary>
    /// Gets tags for template matching (e.g., "celtic", "dramatic", "peaceful").
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets the minimum recommended duration in bars.
    /// </summary>
    public int MinimumBars { get; init; } = 16;

    /// <summary>
    /// Gets the ideal duration in bars for full expression.
    /// </summary>
    public int IdealBars { get; init; } = 32;

    /// <summary>
    /// Gets whether this template works well with modulation.
    /// </summary>
    public bool SupportsModulation { get; init; } = true;

    /// <summary>
    /// Validates that the template is well-formed.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public bool IsValid()
    {
        if (Phases.Count == 0)
            return false;

        var totalDuration = 0.0;
        foreach (var phase in Phases)
        {
            if (phase.RelativeDuration <= 0 || phase.RelativeDuration > 1)
                return false;
            totalDuration += phase.RelativeDuration;
        }

        // Allow small floating-point tolerance
        return Math.Abs(totalDuration - 1.0) < 0.01;
    }

    /// <summary>
    /// Gets the phase that should be active at a given progress point.
    /// </summary>
    /// <param name="progress">Progress through composition (0-1).</param>
    /// <returns>The active phase and its index.</returns>
    public (NarrativePhase phase, int index) GetPhaseAtProgress(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);

        var accumulated = 0.0;
        for (var i = 0; i < Phases.Count; i++)
        {
            accumulated += Phases[i].RelativeDuration;
            if (progress <= accumulated)
            {
                return (Phases[i], i);
            }
        }

        return (Phases[^1], Phases.Count - 1);
    }

    /// <summary>
    /// Gets the progress within the current phase (0-1).
    /// </summary>
    /// <param name="overallProgress">Overall composition progress (0-1).</param>
    /// <returns>Progress within the current phase.</returns>
    public double GetPhaseProgress(double overallProgress)
    {
        overallProgress = Math.Clamp(overallProgress, 0, 1);

        var accumulated = 0.0;
        foreach (var phase in Phases)
        {
            var phaseEnd = accumulated + phase.RelativeDuration;
            if (overallProgress <= phaseEnd)
            {
                var progressInPhase = overallProgress - accumulated;
                return progressInPhase / phase.RelativeDuration;
            }
            accumulated = phaseEnd;
        }

        return 1.0;
    }

    /// <summary>
    /// Calculates the bar range for a specific phase given total bars.
    /// </summary>
    /// <param name="phaseIndex">Index of the phase.</param>
    /// <param name="totalBars">Total bars in composition.</param>
    /// <returns>Start and end bar (inclusive).</returns>
    public (int startBar, int endBar) GetPhaseBarRange(int phaseIndex, int totalBars)
    {
        if (phaseIndex < 0 || phaseIndex >= Phases.Count)
            throw new ArgumentOutOfRangeException(nameof(phaseIndex));

        var startProgress = 0.0;
        for (var i = 0; i < phaseIndex; i++)
        {
            startProgress += Phases[i].RelativeDuration;
        }

        var endProgress = startProgress + Phases[phaseIndex].RelativeDuration;

        var startBar = (int)(startProgress * totalBars);
        var endBar = (int)(endProgress * totalBars) - 1;

        return (startBar, Math.Max(startBar, endBar));
    }
}
