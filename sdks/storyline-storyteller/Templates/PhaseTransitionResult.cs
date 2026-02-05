// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Result of checking phase transition conditions.
/// </summary>
public enum PhaseTransitionResult
{
    /// <summary>
    /// Below position floor, cannot advance yet.
    /// </summary>
    NotReady,

    /// <summary>
    /// Meets all conditions, ready to advance.
    /// </summary>
    Ready,

    /// <summary>
    /// Hit position ceiling, forced to advance.
    /// </summary>
    ForcedAdvance
}
