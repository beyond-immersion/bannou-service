// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Arcs;

/// <summary>
/// The overall direction of an emotional arc - whether it ends higher or lower than it started.
/// </summary>
public enum ArcDirection
{
    /// <summary>
    /// The arc ends higher than it starts (positive resolution).
    /// Arcs: RagsToRiches, ManInHole, Cinderella.
    /// </summary>
    Positive,

    /// <summary>
    /// The arc ends lower than it starts (negative resolution).
    /// Arcs: Tragedy, Icarus, Oedipus.
    /// </summary>
    Negative
}
