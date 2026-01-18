using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.MusicTheory.Melody;

/// <summary>
/// Melodic contour shapes describing overall melodic direction.
/// </summary>
public enum ContourShape
{
    /// <summary>Rises then falls (arch shape)</summary>
    Arch,

    /// <summary>Falls then rises (inverted arch)</summary>
    InvertedArch,

    /// <summary>Generally ascending</summary>
    Ascending,

    /// <summary>Generally descending</summary>
    Descending,

    /// <summary>Wave-like with multiple peaks and valleys</summary>
    Wave,

    /// <summary>Relatively flat/static</summary>
    Static,

    /// <summary>Random/unpredictable</summary>
    Free
}

/// <summary>
/// Defines melodic contour rules and analysis.
/// </summary>
public sealed class Contour
{
    /// <summary>
    /// The overall shape.
    /// </summary>
    public ContourShape Shape { get; }

    /// <summary>
    /// Creates a contour with a specific shape.
    /// </summary>
    public Contour(ContourShape shape)
    {
        Shape = shape;
    }

    /// <summary>
    /// Gets the target direction at a given normalized position (0-1).
    /// Returns -1 (descending), 0 (static), or 1 (ascending).
    /// </summary>
    /// <param name="position">Normalized position in the phrase (0-1).</param>
    /// <returns>Target direction.</returns>
    public int GetDirectionAt(double position)
    {
        if (position < 0 || position > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position must be 0-1");
        }

        return Shape switch
        {
            ContourShape.Arch => position < 0.5 ? 1 : -1,
            ContourShape.InvertedArch => position < 0.5 ? -1 : 1,
            ContourShape.Ascending => 1,
            ContourShape.Descending => -1,
            ContourShape.Wave => Math.Sin(position * Math.PI * 2) > 0 ? 1 : -1,
            ContourShape.Static => 0,
            ContourShape.Free => 0,
            _ => 0
        };
    }

    /// <summary>
    /// Gets the relative target pitch at a given normalized position (0-1).
    /// Returns a value from 0 (lowest) to 1 (highest) within the phrase range.
    /// </summary>
    /// <param name="position">Normalized position in the phrase (0-1).</param>
    /// <returns>Relative target pitch (0-1).</returns>
    public double GetTargetPitchAt(double position)
    {
        if (position < 0 || position > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position must be 0-1");
        }

        return Shape switch
        {
            ContourShape.Arch => Math.Sin(position * Math.PI),
            ContourShape.InvertedArch => 1 - Math.Sin(position * Math.PI),
            ContourShape.Ascending => position,
            ContourShape.Descending => 1 - position,
            ContourShape.Wave => (Math.Sin(position * Math.PI * 2) + 1) / 2,
            ContourShape.Static => 0.5,
            ContourShape.Free => 0.5,
            _ => 0.5
        };
    }

    /// <summary>
    /// Analyzes a sequence of pitch values to determine its contour.
    /// </summary>
    /// <param name="pitches">Sequence of MIDI note numbers or relative pitches.</param>
    /// <returns>The detected contour shape.</returns>
    public static ContourShape Analyze(IReadOnlyList<int> pitches)
    {
        if (pitches.Count < 3)
        {
            return ContourShape.Static;
        }

        var first = pitches[0];
        var last = pitches[^1];
        var max = pitches.Max();
        var min = pitches.Min();
        var maxIndex = pitches.ToList().IndexOf(max);
        var minIndex = pitches.ToList().IndexOf(min);
        var range = max - min;

        // Calculate overall direction
        var totalMotion = last - first;
        var normalizedPosition = pitches.Count > 1 ? (double)maxIndex / (pitches.Count - 1) : 0.5;

        // Static if very little movement
        if (range <= 2)
        {
            return ContourShape.Static;
        }

        // Arch: peak in the middle
        if (maxIndex > 0 && maxIndex < pitches.Count - 1 &&
            normalizedPosition is > 0.25 and < 0.75)
        {
            return ContourShape.Arch;
        }

        // Inverted arch: valley in the middle
        if (minIndex > 0 && minIndex < pitches.Count - 1)
        {
            var minNormalizedPosition = (double)minIndex / (pitches.Count - 1);
            if (minNormalizedPosition is > 0.25 and < 0.75)
            {
                return ContourShape.InvertedArch;
            }
        }

        // Ascending
        if (totalMotion > range * 0.5)
        {
            return ContourShape.Ascending;
        }

        // Descending
        if (totalMotion < -range * 0.5)
        {
            return ContourShape.Descending;
        }

        // Count direction changes for wave detection
        var directionChanges = 0;
        var lastDirection = 0;

        for (var i = 1; i < pitches.Count; i++)
        {
            var direction = Math.Sign(pitches[i] - pitches[i - 1]);
            if (direction != 0 && direction != lastDirection && lastDirection != 0)
            {
                directionChanges++;
            }

            if (direction != 0)
            {
                lastDirection = direction;
            }
        }

        if (directionChanges >= 2)
        {
            return ContourShape.Wave;
        }

        return ContourShape.Free;
    }
}

/// <summary>
/// Interval direction preferences for melodic movement.
/// </summary>
public sealed class IntervalPreferences
{
    /// <summary>
    /// Weight for stepwise motion (m2, M2).
    /// </summary>
    [JsonPropertyName("stepWeight")]
    public double StepWeight { get; set; } = 0.5;

    /// <summary>
    /// Weight for thirds (m3, M3).
    /// </summary>
    [JsonPropertyName("thirdWeight")]
    public double ThirdWeight { get; set; } = 0.25;

    /// <summary>
    /// Weight for larger leaps (P4, P5).
    /// </summary>
    [JsonPropertyName("leapWeight")]
    public double LeapWeight { get; set; } = 0.15;

    /// <summary>
    /// Weight for very large leaps (> P5).
    /// </summary>
    [JsonPropertyName("largeLeapWeight")]
    public double LargeLeapWeight { get; set; } = 0.1;

    /// <summary>
    /// Creates default preferences.
    /// </summary>
    public static IntervalPreferences Default => new();

    /// <summary>
    /// Celtic-style preferences (more stepwise).
    /// </summary>
    public static IntervalPreferences Celtic => new()
    {
        StepWeight = 0.48,
        ThirdWeight = 0.25,
        LeapWeight = 0.18,
        LargeLeapWeight = 0.09
    };

    /// <summary>
    /// Jazz-style preferences (more leaps).
    /// </summary>
    public static IntervalPreferences Jazz => new()
    {
        StepWeight = 0.35,
        ThirdWeight = 0.30,
        LeapWeight = 0.20,
        LargeLeapWeight = 0.15
    };

    /// <summary>
    /// Baroque-style preferences (balanced).
    /// </summary>
    public static IntervalPreferences Baroque => new()
    {
        StepWeight = 0.45,
        ThirdWeight = 0.30,
        LeapWeight = 0.15,
        LargeLeapWeight = 0.10
    };

    /// <summary>
    /// Selects an interval size based on preferences.
    /// </summary>
    /// <param name="random">Random number generator.</param>
    /// <returns>Selected interval in semitones (absolute value).</returns>
    public int SelectInterval(Random random)
    {
        var roll = random.NextDouble();

        if (roll < StepWeight)
        {
            return random.Next(1, 3); // m2 or M2
        }

        roll -= StepWeight;
        if (roll < ThirdWeight)
        {
            return random.Next(3, 5); // m3 or M3
        }

        roll -= ThirdWeight;
        if (roll < LeapWeight)
        {
            return random.Next(5, 8); // P4 to P5
        }

        return random.Next(8, 13); // m6 to P8
    }
}
