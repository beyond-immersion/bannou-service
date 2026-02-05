// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Categories of story actions for GOAP planning.
/// </summary>
public enum ActionCategory
{
    /// <summary>
    /// Actions that introduce and escalate opposition.
    /// </summary>
    Conflict,

    /// <summary>
    /// Actions that build and modify character bonds.
    /// </summary>
    Relationship,

    /// <summary>
    /// Actions for information revelation and concealment.
    /// </summary>
    Mystery,

    /// <summary>
    /// Actions that move toward story conclusion.
    /// </summary>
    Resolution,

    /// <summary>
    /// Actions representing character growth and change.
    /// </summary>
    Transformation
}
