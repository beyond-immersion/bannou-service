using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Narratives;

/// <summary>
/// Handles smooth transitions between narrative phases.
/// Provides interpolation and transition planning to avoid abrupt changes.
/// </summary>
public static class PhaseTransition
{
    /// <summary>
    /// Default number of bars for a gradual transition.
    /// </summary>
    public const int DefaultTransitionBars = 2;

    /// <summary>
    /// Calculates the interpolated emotional target during a phase transition.
    /// </summary>
    /// <param name="fromPhase">The phase we're leaving.</param>
    /// <param name="toPhase">The phase we're entering.</param>
    /// <param name="transitionProgress">Progress through transition (0 = start of from, 1 = fully in to).</param>
    /// <returns>Interpolated emotional state.</returns>
    public static EmotionalState InterpolateEmotionalTarget(
        NarrativePhase fromPhase,
        NarrativePhase toPhase,
        double transitionProgress)
    {
        transitionProgress = Math.Clamp(transitionProgress, 0, 1);

        // Use smoothstep for more natural transitions
        var t = SmoothStep(transitionProgress);

        return fromPhase.EmotionalTarget.InterpolateTo(toPhase.EmotionalTarget, t);
    }

    /// <summary>
    /// Determines the appropriate harmonic character during a transition.
    /// </summary>
    /// <param name="fromPhase">The phase we're leaving.</param>
    /// <param name="toPhase">The phase we're entering.</param>
    /// <param name="transitionProgress">Progress through transition (0-1).</param>
    /// <returns>The harmonic character to use.</returns>
    public static HarmonicCharacter GetTransitionHarmonicCharacter(
        NarrativePhase fromPhase,
        NarrativePhase toPhase,
        double transitionProgress)
    {
        // Use the source character for first half, target for second half
        // with special handling for certain transitions
        if (transitionProgress < 0.5)
        {
            return fromPhase.HarmonicCharacter;
        }

        // Special case: if moving to a climactic phase, start building earlier
        if (toPhase.HarmonicCharacter == HarmonicCharacter.Climactic &&
            transitionProgress > 0.3)
        {
            return HarmonicCharacter.Building;
        }

        // Special case: if resolving, use resolving character throughout second half
        if (toPhase.HarmonicCharacter == HarmonicCharacter.Resolving ||
            toPhase.HarmonicCharacter == HarmonicCharacter.Peaceful)
        {
            return HarmonicCharacter.Resolving;
        }

        return toPhase.HarmonicCharacter;
    }

    /// <summary>
    /// Calculates whether a cadence should occur during the transition.
    /// </summary>
    /// <param name="fromPhase">The phase we're leaving.</param>
    /// <param name="toPhase">The phase we're entering.</param>
    /// <returns>Recommended cadence, if any.</returns>
    public static CadencePreference? GetTransitionCadence(
        NarrativePhase fromPhase,
        NarrativePhase toPhase)
    {
        // If leaving phase has a specific ending cadence, use it
        if (fromPhase.EndingCadence.HasValue &&
            fromPhase.EndingCadence.Value != CadencePreference.None)
        {
            return fromPhase.EndingCadence;
        }

        // If entering a stable/peaceful phase, suggest authentic cadence
        if (toPhase.HarmonicCharacter == HarmonicCharacter.Stable ||
            toPhase.HarmonicCharacter == HarmonicCharacter.Peaceful)
        {
            return CadencePreference.Authentic;
        }

        // If entering a building/climactic phase, half cadence maintains momentum
        if (toPhase.HarmonicCharacter == HarmonicCharacter.Building ||
            toPhase.HarmonicCharacter == HarmonicCharacter.Climactic)
        {
            return CadencePreference.Half;
        }

        // If entering a resolving phase after climax, authentic cadence
        if (fromPhase.HarmonicCharacter == HarmonicCharacter.Climactic &&
            toPhase.HarmonicCharacter == HarmonicCharacter.Resolving)
        {
            return CadencePreference.Authentic;
        }

        return null;
    }

    /// <summary>
    /// Determines the transition intensity (how dramatic the change should be).
    /// </summary>
    /// <param name="fromPhase">The phase we're leaving.</param>
    /// <param name="toPhase">The phase we're entering.</param>
    /// <returns>Intensity from 0 (seamless) to 1 (dramatic contrast).</returns>
    public static double CalculateTransitionIntensity(
        NarrativePhase fromPhase,
        NarrativePhase toPhase)
    {
        var emotionalDistance = fromPhase.EmotionalTarget.DistanceTo(toPhase.EmotionalTarget);

        // Normalize to 0-1 range (max theoretical distance is sqrt(6) ≈ 2.45)
        var normalizedDistance = Math.Min(1.0, emotionalDistance / 2.0);

        // Factor in harmonic character changes
        var harmonicFactor = GetHarmonicContrastFactor(
            fromPhase.HarmonicCharacter,
            toPhase.HarmonicCharacter);

        return Math.Max(normalizedDistance, harmonicFactor);
    }

    /// <summary>
    /// Suggests the number of transition bars based on intensity.
    /// </summary>
    /// <param name="fromPhase">The phase we're leaving.</param>
    /// <param name="toPhase">The phase we're entering.</param>
    /// <param name="totalBars">Total bars available.</param>
    /// <returns>Recommended number of transition bars.</returns>
    public static int SuggestTransitionBars(
        NarrativePhase fromPhase,
        NarrativePhase toPhase,
        int totalBars)
    {
        var intensity = CalculateTransitionIntensity(fromPhase, toPhase);

        // More intense transitions need more bars
        var suggestedBars = intensity switch
        {
            < 0.2 => 1,  // Very smooth - quick transition
            < 0.4 => 2,  // Moderate - standard transition
            < 0.6 => 3,  // Significant - gradual transition
            < 0.8 => 4,  // Major - extended transition
            _ => Math.Max(4, totalBars / 8)  // Dramatic - substantial transition
        };

        // Don't use more than 10% of total bars for any single transition
        return Math.Min(suggestedBars, Math.Max(1, totalBars / 10));
    }

    /// <summary>
    /// Creates a transition plan between two phases.
    /// </summary>
    /// <param name="fromPhase">The phase we're leaving.</param>
    /// <param name="toPhase">The phase we're entering.</param>
    /// <param name="transitionBars">Number of bars for transition.</param>
    /// <returns>A transition plan with bar-by-bar targets.</returns>
    public static TransitionPlan CreateTransitionPlan(
        NarrativePhase fromPhase,
        NarrativePhase toPhase,
        int transitionBars)
    {
        var steps = new List<TransitionStep>();

        for (var i = 0; i < transitionBars; i++)
        {
            var progress = (double)(i + 1) / transitionBars;
            var emotional = InterpolateEmotionalTarget(fromPhase, toPhase, progress);
            var harmonic = GetTransitionHarmonicCharacter(fromPhase, toPhase, progress);

            // Cadence only on the last bar of transition
            CadencePreference? cadence = null;
            if (i == transitionBars - 1)
            {
                cadence = GetTransitionCadence(fromPhase, toPhase);
            }

            steps.Add(new TransitionStep
            {
                BarOffset = i,
                Progress = progress,
                EmotionalTarget = emotional,
                HarmonicCharacter = harmonic,
                SuggestedCadence = cadence
            });
        }

        return new TransitionPlan
        {
            FromPhase = fromPhase,
            ToPhase = toPhase,
            TotalBars = transitionBars,
            Steps = steps
        };
    }

    private static double SmoothStep(double t)
    {
        // Hermite interpolation for smooth acceleration/deceleration
        return t * t * (3 - 2 * t);
    }

    private static double GetHarmonicContrastFactor(
        HarmonicCharacter from,
        HarmonicCharacter to)
    {
        // Define contrast levels between harmonic characters
        if (from == to)
            return 0.0;

        // Maximum contrast: Stable ↔ Climactic, Peaceful ↔ Climactic
        if ((from == HarmonicCharacter.Stable && to == HarmonicCharacter.Climactic) ||
            (from == HarmonicCharacter.Climactic && to == HarmonicCharacter.Stable) ||
            (from == HarmonicCharacter.Peaceful && to == HarmonicCharacter.Climactic) ||
            (from == HarmonicCharacter.Climactic && to == HarmonicCharacter.Peaceful))
        {
            return 1.0;
        }

        // High contrast: Building → Peaceful (skipping release), etc.
        if ((from == HarmonicCharacter.Building && to == HarmonicCharacter.Peaceful) ||
            (from == HarmonicCharacter.Wandering && to == HarmonicCharacter.Stable))
        {
            return 0.8;
        }

        // Moderate contrast: Adjacent states
        return 0.4;
    }
}

/// <summary>
/// A complete plan for transitioning between phases.
/// </summary>
public sealed class TransitionPlan
{
    /// <summary>
    /// Gets the source phase.
    /// </summary>
    public required NarrativePhase FromPhase { get; init; }

    /// <summary>
    /// Gets the target phase.
    /// </summary>
    public required NarrativePhase ToPhase { get; init; }

    /// <summary>
    /// Gets the total bars in this transition.
    /// </summary>
    public required int TotalBars { get; init; }

    /// <summary>
    /// Gets the individual transition steps.
    /// </summary>
    public required IReadOnlyList<TransitionStep> Steps { get; init; }
}

/// <summary>
/// A single step within a transition plan.
/// </summary>
public sealed class TransitionStep
{
    /// <summary>
    /// Gets the bar offset from transition start.
    /// </summary>
    public required int BarOffset { get; init; }

    /// <summary>
    /// Gets the progress through the transition (0-1).
    /// </summary>
    public required double Progress { get; init; }

    /// <summary>
    /// Gets the emotional target for this step.
    /// </summary>
    public required EmotionalState EmotionalTarget { get; init; }

    /// <summary>
    /// Gets the harmonic character for this step.
    /// </summary>
    public required HarmonicCharacter HarmonicCharacter { get; init; }

    /// <summary>
    /// Gets the suggested cadence for this step, if any.
    /// </summary>
    public CadencePreference? SuggestedCadence { get; init; }
}
