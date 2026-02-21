// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Arcs;

/// <summary>
/// A control point defining a key position in an emotional arc.
/// </summary>
public sealed class ArcControlPoint
{
    /// <summary>
    /// The position in the story (0.0 = start, 1.0 = end).
    /// </summary>
    public required double Position { get; init; }

    /// <summary>
    /// The emotional/spectrum value at this position.
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// A label for this control point (e.g., "start", "nadir", "apex", "triumph").
    /// </summary>
    public required string Label { get; init; }
}
