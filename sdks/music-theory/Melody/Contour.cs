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
/// Melodic register describing pitch range usage.
/// </summary>
public enum MelodicRegister
{
    /// <summary>Predominantly in lower third of range</summary>
    Low,

    /// <summary>Predominantly in middle third of range</summary>
    Middle,

    /// <summary>Predominantly in upper third of range</summary>
    High,

    /// <summary>Full range with expanding motion outward</summary>
    Expanding,

    /// <summary>Range contracting toward center</summary>
    Contracting,

    /// <summary>Moving from low to high register</summary>
    Ascending,

    /// <summary>Moving from high to low register</summary>
    Descending,

    /// <summary>Full range usage throughout</summary>
    Full
}

/// <summary>
/// Utilities for analyzing melodic register.
/// </summary>
public static class RegisterAnalysis
{
    /// <summary>
    /// Analyzes the register usage of a melody.
    /// </summary>
    /// <param name="midiNotes">MIDI note numbers.</param>
    /// <param name="rangeMin">Minimum of available range.</param>
    /// <param name="rangeMax">Maximum of available range.</param>
    /// <returns>The detected register.</returns>
    public static MelodicRegister Analyze(IReadOnlyList<int> midiNotes, int rangeMin, int rangeMax)
    {
        if (midiNotes.Count == 0)
        {
            return MelodicRegister.Middle;
        }

        var rangeSize = rangeMax - rangeMin;
        if (rangeSize <= 0)
        {
            return MelodicRegister.Middle;
        }

        var lowThreshold = rangeMin + rangeSize / 3.0;
        var highThreshold = rangeMin + 2 * rangeSize / 3.0;

        var lowCount = 0;
        var midCount = 0;
        var highCount = 0;

        foreach (var note in midiNotes)
        {
            if (note < lowThreshold)
            {
                lowCount++;
            }
            else if (note > highThreshold)
            {
                highCount++;
            }
            else
            {
                midCount++;
            }
        }

        var total = midiNotes.Count;
        var lowPct = (double)lowCount / total;
        var midPct = (double)midCount / total;
        var highPct = (double)highCount / total;

        // Check for expanding/contracting by comparing first and last halves
        if (midiNotes.Count >= 4)
        {
            var firstHalf = midiNotes.Take(midiNotes.Count / 2).ToList();
            var secondHalf = midiNotes.Skip(midiNotes.Count / 2).ToList();

            var firstRange = firstHalf.Max() - firstHalf.Min();
            var secondRange = secondHalf.Max() - secondHalf.Min();

            if (secondRange > firstRange * 1.5)
            {
                return MelodicRegister.Expanding;
            }

            if (firstRange > secondRange * 1.5)
            {
                return MelodicRegister.Contracting;
            }

            // Check for ascending/descending register shift
            var firstAvg = firstHalf.Average();
            var secondAvg = secondHalf.Average();
            var avgDiff = secondAvg - firstAvg;

            if (avgDiff > rangeSize * 0.2)
            {
                return MelodicRegister.Ascending;
            }

            if (avgDiff < -rangeSize * 0.2)
            {
                return MelodicRegister.Descending;
            }
        }

        // Check for predominant register
        if (lowPct > 0.5)
        {
            return MelodicRegister.Low;
        }

        if (highPct > 0.5)
        {
            return MelodicRegister.High;
        }

        if (midPct > 0.5)
        {
            return MelodicRegister.Middle;
        }

        // Distributed across range
        return MelodicRegister.Full;
    }

    /// <summary>
    /// Gets a target pitch position (0-1) for a register.
    /// </summary>
    /// <param name="register">The target register.</param>
    /// <param name="position">Normalized position in phrase (0-1).</param>
    /// <returns>Target pitch position within range (0=low, 1=high).</returns>
    public static double GetTargetPosition(MelodicRegister register, double position)
    {
        return register switch
        {
            MelodicRegister.Low => 0.2 + position * 0.1,
            MelodicRegister.Middle => 0.4 + position * 0.2,
            MelodicRegister.High => 0.7 + position * 0.2,
            MelodicRegister.Expanding => 0.5 + (position - 0.5) * position,
            MelodicRegister.Contracting => 0.5 - (position - 0.5) * (1 - position) * 0.5,
            MelodicRegister.Ascending => 0.2 + position * 0.6,
            MelodicRegister.Descending => 0.8 - position * 0.6,
            MelodicRegister.Full => 0.5,
            _ => 0.5
        };
    }
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

/// <summary>
/// Result of melodic density analysis.
/// </summary>
public sealed class DensityAnalysis
{
    /// <summary>
    /// Notes per beat (average).
    /// </summary>
    public double NotesPerBeat { get; init; }

    /// <summary>
    /// Normalized density from 0 (very sparse) to 1 (very dense).
    /// </summary>
    public double NormalizedDensity { get; init; }

    /// <summary>
    /// Percentage of time with active notes (vs. rests).
    /// </summary>
    public double ActivityRatio { get; init; }

    /// <summary>
    /// Average note duration in ticks.
    /// </summary>
    public double AverageNoteDuration { get; init; }

    /// <summary>
    /// Descriptive category for the density.
    /// </summary>
    public DensityCategory Category { get; init; }
}

/// <summary>
/// Categories of rhythmic density.
/// </summary>
public enum DensityCategory
{
    /// <summary>Very sparse with long notes and rests (less than 0.5 notes/beat)</summary>
    VerySparse,

    /// <summary>Sparse melodic texture (0.5-1 notes/beat)</summary>
    Sparse,

    /// <summary>Moderate density (1-2 notes/beat)</summary>
    Moderate,

    /// <summary>Dense texture (2-4 notes/beat)</summary>
    Dense,

    /// <summary>Very dense with many short notes (more than 4 notes/beat)</summary>
    VeryDense
}

/// <summary>
/// Utilities for analyzing rhythmic density.
/// </summary>
public static class RhythmicDensityAnalyzer
{
    /// <summary>
    /// Analyzes the rhythmic density of a melody.
    /// </summary>
    /// <param name="notes">The melody notes.</param>
    /// <param name="ticksPerBeat">Ticks per beat (PPQN).</param>
    /// <returns>Density analysis results.</returns>
    public static DensityAnalysis Analyze(IReadOnlyList<MelodyNote> notes, int ticksPerBeat = 480)
    {
        if (notes.Count == 0)
        {
            return new DensityAnalysis
            {
                NotesPerBeat = 0,
                NormalizedDensity = 0,
                ActivityRatio = 0,
                AverageNoteDuration = 0,
                Category = DensityCategory.VerySparse
            };
        }

        // Calculate total duration span
        var firstNoteStart = notes.Min(n => n.StartTick);
        var lastNoteEnd = notes.Max(n => n.EndTick);
        var totalTicks = lastNoteEnd - firstNoteStart;

        if (totalTicks <= 0)
        {
            totalTicks = notes.Sum(n => n.DurationTicks);
        }

        var totalBeats = (double)totalTicks / ticksPerBeat;
        var notesPerBeat = totalBeats > 0 ? notes.Count / totalBeats : 0;

        // Calculate activity ratio (how much time has notes playing)
        var totalNoteDuration = notes.Sum(n => n.DurationTicks);
        var activityRatio = totalTicks > 0 ? Math.Min(1.0, (double)totalNoteDuration / totalTicks) : 0;

        // Average note duration
        var avgDuration = notes.Count > 0 ? (double)totalNoteDuration / notes.Count : 0;

        // Normalized density (0-1) based on typical ranges
        // Reference: 1 note per beat = 0.5 density (quarter notes)
        // 4 notes per beat = 1.0 density (sixteenth notes)
        var normalizedDensity = Math.Min(1.0, notesPerBeat / 4.0);

        // Determine category
        var category = notesPerBeat switch
        {
            < 0.5 => DensityCategory.VerySparse,
            < 1.0 => DensityCategory.Sparse,
            < 2.0 => DensityCategory.Moderate,
            < 4.0 => DensityCategory.Dense,
            _ => DensityCategory.VeryDense
        };

        return new DensityAnalysis
        {
            NotesPerBeat = notesPerBeat,
            NormalizedDensity = normalizedDensity,
            ActivityRatio = activityRatio,
            AverageNoteDuration = avgDuration,
            Category = category
        };
    }

    /// <summary>
    /// Calculates the density of a specific time window within a melody.
    /// </summary>
    /// <param name="notes">The melody notes.</param>
    /// <param name="startTick">Start of the window.</param>
    /// <param name="endTick">End of the window.</param>
    /// <param name="ticksPerBeat">Ticks per beat.</param>
    /// <returns>Notes per beat in the window.</returns>
    public static double CalculateWindowDensity(
        IReadOnlyList<MelodyNote> notes,
        int startTick,
        int endTick,
        int ticksPerBeat = 480)
    {
        var windowNotes = notes.Count(n => n.StartTick >= startTick && n.StartTick < endTick);
        var windowBeats = (double)(endTick - startTick) / ticksPerBeat;

        return windowBeats > 0 ? windowNotes / windowBeats : 0;
    }

    /// <summary>
    /// Calculates the density variation across a melody.
    /// </summary>
    /// <param name="notes">The melody notes.</param>
    /// <param name="windowBeats">Size of each analysis window in beats.</param>
    /// <param name="ticksPerBeat">Ticks per beat.</param>
    /// <returns>Density values for each window.</returns>
    public static IReadOnlyList<double> CalculateDensityProfile(
        IReadOnlyList<MelodyNote> notes,
        double windowBeats = 1.0,
        int ticksPerBeat = 480)
    {
        if (notes.Count == 0)
        {
            return [];
        }

        var windowTicks = (int)(windowBeats * ticksPerBeat);
        var firstTick = notes.Min(n => n.StartTick);
        var lastTick = notes.Max(n => n.EndTick);

        var densities = new List<double>();
        for (var tick = firstTick; tick < lastTick; tick += windowTicks)
        {
            var density = CalculateWindowDensity(notes, tick, tick + windowTicks, ticksPerBeat);
            densities.Add(density);
        }

        return densities;
    }

    /// <summary>
    /// Suggests a target density based on a normalized value (0-1).
    /// </summary>
    /// <param name="normalizedDensity">Target density (0 = sparse, 1 = dense).</param>
    /// <returns>Target notes per beat.</returns>
    public static double SuggestNotesPerBeat(double normalizedDensity)
    {
        // Maps 0-1 to approximately 0.5-4 notes per beat
        return 0.5 + normalizedDensity * 3.5;
    }
}
