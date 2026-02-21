// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Arcs;

/// <summary>
/// A complete emotional arc definition loaded from emotional-arcs.yaml.
/// </summary>
public sealed class EmotionalArc
{
    /// <summary>
    /// The arc type identifier.
    /// </summary>
    public required ArcType Type { get; init; }

    /// <summary>
    /// The shape pattern (e.g., "fall_rise", "rise_fall_rise").
    /// </summary>
    public required string ShapePattern { get; init; }

    /// <summary>
    /// The overall direction (positive or negative ending).
    /// </summary>
    public required ArcDirection Direction { get; init; }

    /// <summary>
    /// The mathematical form description (for documentation).
    /// </summary>
    public required string MathematicalForm { get; init; }

    /// <summary>
    /// The control points defining key positions in the arc.
    /// </summary>
    public required ArcControlPoint[] ControlPoints { get; init; }

    /// <summary>
    /// Pre-computed trajectory at 11 sample points (0%, 10%, 20%, ..., 100%).
    /// </summary>
    public required double[] SampledTrajectory { get; init; }

    /// <summary>
    /// Evaluates the arc at a given position using linear interpolation.
    /// </summary>
    /// <param name="position">The story position (0.0 to 1.0).</param>
    /// <returns>The emotional value at that position.</returns>
    public double EvaluateAt(double position)
    {
        if (position <= 0) return SampledTrajectory[0];
        if (position >= 1) return SampledTrajectory[^1];

        var scaledPosition = position * (SampledTrajectory.Length - 1);
        var lowerIndex = (int)Math.Floor(scaledPosition);
        var upperIndex = Math.Min(lowerIndex + 1, SampledTrajectory.Length - 1);
        var fraction = scaledPosition - lowerIndex;

        return (SampledTrajectory[lowerIndex] * (1 - fraction)) +
            (SampledTrajectory[upperIndex] * fraction);
    }
}
