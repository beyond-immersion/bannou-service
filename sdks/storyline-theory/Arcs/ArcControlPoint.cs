// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Arcs;

/// <summary>
/// A control point defining a key position in an emotional arc.
/// Control points are used for interpolation to generate the full arc trajectory.
/// </summary>
public sealed class ArcControlPoint
{
    /// <summary>
    /// The position in the story (0.0 = start, 1.0 = end).
    /// </summary>
    public required double Position { get; init; }

    /// <summary>
    /// The emotional/spectrum value at this position (typically 0.0 to 1.0).
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// A label for this control point (e.g., "start", "nadir", "apex", "triumph").
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// A description of what happens at this point in the narrative.
    /// </summary>
    public string? Description { get; init; }
}
