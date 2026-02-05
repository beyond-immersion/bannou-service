// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Spectrums;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Single phase in a story template.
/// </summary>
public sealed class StoryPhase
{
    /// <summary>
    /// Phase number (1-based).
    /// </summary>
    public required int PhaseNumber { get; init; }

    /// <summary>
    /// Phase name (e.g., "nadir", "setup", "climax").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Position constraints for this phase.
    /// </summary>
    public required PhasePosition Position { get; init; }

    /// <summary>
    /// Target state for phase completion.
    /// </summary>
    public required PhaseTargetState TargetState { get; init; }

    /// <summary>
    /// Transition trigger conditions.
    /// </summary>
    public required PhaseTransition Transition { get; init; }

    /// <summary>
    /// Save the Cat beats covered in this phase.
    /// </summary>
    public required string[] StcBeatsCovered { get; init; }

    /// <summary>
    /// Whether this is the terminal (final) phase.
    /// </summary>
    public bool IsTerminal { get; init; }

    /// <summary>
    /// Check if transition conditions are met.
    /// </summary>
    public PhaseTransitionResult CheckTransition(double currentPosition, NarrativeState state, SpectrumType primarySpectrum)
    {
        // Forced advancement at ceiling (deadlock prevention)
        if (currentPosition >= Transition.PositionCeiling)
        {
            return PhaseTransitionResult.ForcedAdvance;
        }

        // Not ready if below floor (speed-running prevention)
        if (currentPosition < Transition.PositionFloor)
        {
            return PhaseTransitionResult.NotReady;
        }

        // Check state requirements
        var primaryValue = state[primarySpectrum];
        if (primaryValue.HasValue)
        {
            if (Transition.PrimarySpectrumMin.HasValue && primaryValue < Transition.PrimarySpectrumMin)
            {
                return PhaseTransitionResult.NotReady;
            }

            if (Transition.PrimarySpectrumMax.HasValue && primaryValue > Transition.PrimarySpectrumMax)
            {
                return PhaseTransitionResult.NotReady;
            }
        }

        return PhaseTransitionResult.Ready;
    }
}
