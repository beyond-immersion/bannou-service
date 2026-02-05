// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Arcs;

/// <summary>
/// A complete emotional arc definition loaded from emotional-arcs.yaml.
/// Defines the shape of emotional trajectory using Reagan et al.'s six fundamental patterns.
/// </summary>
public sealed class EmotionalArc
{
    /// <summary>
    /// The arc type identifier.
    /// </summary>
    public required ArcType Type { get; init; }

    /// <summary>
    /// The arc code (e.g., "MAN_IN_HOLE").
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// The human-readable name (e.g., "Man in a Hole").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Alternative names for this arc (e.g., "Fall-Rise", "U-Shape").
    /// </summary>
    public required string[] Aliases { get; init; }

    /// <summary>
    /// The visual pattern (e.g., "↘↗" for Man in Hole).
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// A description of the arc's emotional trajectory.
    /// </summary>
    public required string Description { get; init; }

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
    /// Used for quick interpolation.
    /// </summary>
    public required double[] SampledTrajectory { get; init; }

    /// <summary>
    /// The number of inflection points (direction changes) in this arc.
    /// </summary>
    public required int InflectionPoints { get; init; }

    /// <summary>
    /// Genres commonly associated with this arc type.
    /// </summary>
    public required string[] GenreAssociations { get; init; }

    /// <summary>
    /// Media examples of this arc type.
    /// </summary>
    public required string[] Examples { get; init; }

    /// <summary>
    /// Evaluates the arc at a given position using linear interpolation
    /// of the sampled trajectory.
    /// </summary>
    /// <param name="position">The story position (0.0 to 1.0).</param>
    /// <returns>The emotional value at that position.</returns>
    public double EvaluateAt(double position)
    {
        if (position <= 0) return SampledTrajectory[0];
        if (position >= 1) return SampledTrajectory[^1];

        // SampledTrajectory has 11 points at 0%, 10%, 20%, ..., 100%
        var scaledPosition = position * (SampledTrajectory.Length - 1);
        var lowerIndex = (int)Math.Floor(scaledPosition);
        var upperIndex = Math.Min(lowerIndex + 1, SampledTrajectory.Length - 1);
        var fraction = scaledPosition - lowerIndex;

        return SampledTrajectory[lowerIndex] * (1 - fraction) +
               SampledTrajectory[upperIndex] * fraction;
    }

    /// <summary>
    /// Gets the target value for a phase based on the arc shape.
    /// </summary>
    /// <param name="phaseStartPosition">The starting position of the phase (0.0 to 1.0).</param>
    /// <param name="phaseEndPosition">The ending position of the phase (0.0 to 1.0).</param>
    /// <returns>The target value at the end of the phase.</returns>
    public double GetPhaseTargetValue(double phaseStartPosition, double phaseEndPosition)
    {
        return EvaluateAt(phaseEndPosition);
    }

    /// <summary>
    /// Gets the expected direction of change for a phase.
    /// </summary>
    /// <param name="phaseStartPosition">The starting position of the phase (0.0 to 1.0).</param>
    /// <param name="phaseEndPosition">The ending position of the phase (0.0 to 1.0).</param>
    /// <returns>Positive if the arc rises in this phase, negative if it falls, zero if roughly flat.</returns>
    public int GetPhaseDirection(double phaseStartPosition, double phaseEndPosition)
    {
        var startValue = EvaluateAt(phaseStartPosition);
        var endValue = EvaluateAt(phaseEndPosition);
        var delta = endValue - startValue;

        if (Math.Abs(delta) < 0.05) return 0; // Roughly flat
        return delta > 0 ? 1 : -1;
    }
}
