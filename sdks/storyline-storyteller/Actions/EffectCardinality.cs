// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Cardinality for effect application.
/// </summary>
public enum EffectCardinality
{
    /// <summary>
    /// Replaces any existing value at key (e.g., location).
    /// </summary>
    Exclusive,

    /// <summary>
    /// Adds to collection at key (e.g., allies).
    /// </summary>
    Additive
}
