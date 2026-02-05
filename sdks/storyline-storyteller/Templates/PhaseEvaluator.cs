// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Planning;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Evaluates phase transitions and manages phase progression.
/// </summary>
public sealed class PhaseEvaluator
{
    /// <summary>
    /// Check if the given world state meets the transition requirements for the phase.
    /// </summary>
    public PhaseTransitionResult CheckTransition(
        WorldState currentState,
        StoryPhase phase,
        SpectrumType primarySpectrum)
    {
        return phase.CheckTransition(currentState.Position, currentState.NarrativeState, primarySpectrum);
    }

    /// <summary>
    /// Get the current phase for a template based on position.
    /// </summary>
    public StoryPhase GetCurrentPhase(StoryTemplate template, double position)
    {
        return template.GetPhaseAt(position);
    }

    /// <summary>
    /// Check if the story has reached a terminal state.
    /// </summary>
    public bool IsComplete(StoryTemplate template, WorldState currentState)
    {
        var currentPhase = template.GetPhaseAt(currentState.Position);
        return currentPhase.IsTerminal && currentState.Position >= currentPhase.Transition.PositionFloor;
    }
}
