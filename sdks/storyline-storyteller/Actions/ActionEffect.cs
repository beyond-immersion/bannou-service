// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Effect produced by action execution.
/// </summary>
public sealed class ActionEffect
{
    /// <summary>
    /// The world state key to modify (e.g., "hero.at_mercy").
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The new value to set.
    /// </summary>
    public required object Value { get; init; }

    /// <summary>
    /// How the effect is applied. Defaults to Exclusive (replaces existing value).
    /// </summary>
    public EffectCardinality Cardinality { get; init; } = EffectCardinality.Exclusive;
}
