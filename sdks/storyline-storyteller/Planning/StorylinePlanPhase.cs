// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineStoryteller.Templates;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Planning;

/// <summary>
/// A planned phase in a storyline.
/// </summary>
public sealed class StorylinePlanPhase
{
    /// <summary>
    /// Phase number (1-based).
    /// </summary>
    public required int PhaseNumber { get; init; }

    /// <summary>
    /// Phase name.
    /// </summary>
    public required string PhaseName { get; init; }

    /// <summary>
    /// Actions planned for this phase.
    /// </summary>
    public required StorylinePlanAction[] Actions { get; init; }

    /// <summary>
    /// Target state for this phase.
    /// </summary>
    public required PhaseTargetState TargetState { get; init; }

    /// <summary>
    /// Position bounds for this phase.
    /// </summary>
    public required PhasePosition PositionBounds { get; init; }
}
