// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Transition trigger conditions for phase advancement.
/// </summary>
public sealed class PhaseTransition
{
    /// <summary>
    /// Minimum position to allow advancement.
    /// </summary>
    public required double PositionFloor { get; init; }

    /// <summary>
    /// Position at which advancement is forced.
    /// </summary>
    public required double PositionCeiling { get; init; }

    /// <summary>
    /// Minimum primary spectrum value required (state requirement).
    /// </summary>
    public double? PrimarySpectrumMin { get; init; }

    /// <summary>
    /// Maximum primary spectrum value required (state requirement).
    /// </summary>
    public double? PrimarySpectrumMax { get; init; }
}
