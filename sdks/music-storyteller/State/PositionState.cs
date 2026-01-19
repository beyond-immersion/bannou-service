namespace BeyondImmersion.Bannou.MusicStoryteller.State;

/// <summary>
/// Tracks the current position within the composition.
/// Includes bar, beat, and narrative phase information.
/// </summary>
public sealed class PositionState
{
    /// <summary>
    /// Current bar number (0-based).
    /// </summary>
    public int Bar { get; set; }

    /// <summary>
    /// Current beat within the bar (0-based).
    /// </summary>
    public double Beat { get; set; }

    /// <summary>
    /// Total bars in the composition (if known).
    /// </summary>
    public int TotalBars { get; set; }

    /// <summary>
    /// Beats per bar (time signature numerator).
    /// </summary>
    public int BeatsPerBar { get; set; } = 4;

    /// <summary>
    /// Current narrative phase index.
    /// </summary>
    public int PhaseIndex { get; set; }

    /// <summary>
    /// Total number of narrative phases.
    /// </summary>
    public int TotalPhases { get; set; }

    /// <summary>
    /// Progress through the current phase (0-1).
    /// </summary>
    public double PhaseProgress { get; set; }

    /// <summary>
    /// Overall progress through the composition (0-1).
    /// </summary>
    public double OverallProgress => TotalBars > 0 ? (double)Bar / TotalBars : 0;

    /// <summary>
    /// Alias for OverallProgress (convenience).
    /// </summary>
    public double Progress => OverallProgress;

    /// <summary>
    /// Whether we're at the start of a bar.
    /// </summary>
    public bool IsBarStart => Beat < 0.001;

    /// <summary>
    /// Whether we're on a strong beat (beat 0 or 2 in 4/4).
    /// </summary>
    public bool IsStrongBeat => BeatsPerBar == 4
        ? (int)Beat is 0 or 2
        : (int)Beat == 0;

    /// <summary>
    /// Whether we're at a phrase boundary (every 4 or 8 bars typically).
    /// </summary>
    public bool IsPhraseBoundary => Bar % 4 == 0 && IsBarStart;

    /// <summary>
    /// Whether we're at a section boundary (every 8 or 16 bars typically).
    /// </summary>
    public bool IsSectionBoundary => Bar % 8 == 0 && IsBarStart;

    /// <summary>
    /// Advances the position by the specified number of beats.
    /// </summary>
    /// <param name="beats">Number of beats to advance.</param>
    public void Advance(double beats)
    {
        Beat += beats;
        while (Beat >= BeatsPerBar)
        {
            Beat -= BeatsPerBar;
            Bar++;
        }
    }

    /// <summary>
    /// Advances to the next bar.
    /// </summary>
    public void NextBar()
    {
        Bar++;
        Beat = 0;
    }

    /// <summary>
    /// Updates phase progress based on current bar and phase boundaries.
    /// </summary>
    /// <param name="phaseStartBar">The bar where the current phase started.</param>
    /// <param name="phaseDurationBars">The duration of the current phase in bars.</param>
    public void UpdatePhaseProgress(int phaseStartBar, int phaseDurationBars)
    {
        if (phaseDurationBars > 0)
        {
            PhaseProgress = (double)(Bar - phaseStartBar) / phaseDurationBars;
            PhaseProgress = Math.Clamp(PhaseProgress, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Creates a deep copy of this position state.
    /// </summary>
    public PositionState Clone()
    {
        return new PositionState
        {
            Bar = Bar,
            Beat = Beat,
            TotalBars = TotalBars,
            BeatsPerBar = BeatsPerBar,
            PhaseIndex = PhaseIndex,
            TotalPhases = TotalPhases,
            PhaseProgress = PhaseProgress
        };
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Position[Bar {Bar}/{TotalBars}, Beat {Beat:F1}, Phase {PhaseIndex + 1}/{TotalPhases} ({PhaseProgress:P0})]";
    }
}
