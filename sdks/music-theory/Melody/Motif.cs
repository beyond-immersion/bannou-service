using BeyondImmersion.Bannou.MusicTheory.Pitch;
using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.MusicTheory.Melody;

/// <summary>
/// Types of motif transformations that can be applied.
/// </summary>
public enum MotifTransformation
{
    /// <summary>Original motif, unmodified</summary>
    Original,

    /// <summary>Transposed to a different pitch level</summary>
    Transpose,

    /// <summary>Intervals inverted (flipped around axis)</summary>
    Invert,

    /// <summary>Played backwards</summary>
    Retrograde,

    /// <summary>Retrograde inversion (both reversed and inverted)</summary>
    RetrogradeInvert,

    /// <summary>Durations lengthened (augmentation)</summary>
    Augment,

    /// <summary>Durations shortened (diminution)</summary>
    Diminish,

    /// <summary>Repeated at different pitch levels (sequence)</summary>
    Sequence,

    /// <summary>Truncated/fragmented version</summary>
    Fragment
}

/// <summary>
/// Record of a transformation applied to a motif.
/// </summary>
public sealed class MotifTransformationRecord
{
    /// <summary>
    /// The type of transformation applied.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MotifTransformation Type { get; }

    /// <summary>
    /// Parameter for the transformation (e.g., semitones for transpose, factor for augment).
    /// </summary>
    [JsonPropertyName("parameter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Parameter { get; }

    /// <summary>
    /// Creates a transformation record.
    /// </summary>
    [JsonConstructor]
    public MotifTransformationRecord(MotifTransformation type, double? parameter = null)
    {
        Type = type;
        Parameter = parameter;
    }

    /// <summary>
    /// Original (no transformation).
    /// </summary>
    public static MotifTransformationRecord Original => new(MotifTransformation.Original);

    /// <summary>
    /// Transposition by semitones.
    /// </summary>
    public static MotifTransformationRecord Transposed(int semitones) =>
        new(MotifTransformation.Transpose, semitones);

    /// <summary>
    /// Inversion.
    /// </summary>
    public static MotifTransformationRecord Inverted => new(MotifTransformation.Invert);

    /// <summary>
    /// Retrograde.
    /// </summary>
    public static MotifTransformationRecord Reversed => new(MotifTransformation.Retrograde);

    /// <summary>
    /// Augmentation by factor.
    /// </summary>
    public static MotifTransformationRecord Augmented(double factor = 2.0) =>
        new(MotifTransformation.Augment, factor);

    /// <summary>
    /// Diminution by factor.
    /// </summary>
    public static MotifTransformationRecord Diminished(double factor = 2.0) =>
        new(MotifTransformation.Diminish, factor);

    /// <summary>
    /// Sequence with step interval.
    /// </summary>
    public static MotifTransformationRecord Sequenced(int step) =>
        new(MotifTransformation.Sequence, step);

    /// <inheritdoc />
    public override string ToString() => Parameter.HasValue
        ? $"{Type}({Parameter.Value})"
        : Type.ToString();
}

/// <summary>
/// Represents a melodic motif - a short thematic fragment that can be developed.
/// </summary>
public sealed class Motif
{
    /// <summary>
    /// Intervals from the first note (in semitones).
    /// </summary>
    public IReadOnlyList<int> Intervals { get; }

    /// <summary>
    /// Relative durations (1.0 = reference duration).
    /// </summary>
    public IReadOnlyList<double> Durations { get; }

    /// <summary>
    /// Creates a motif from intervals and durations.
    /// </summary>
    /// <param name="intervals">Intervals from first note (starts with 0).</param>
    /// <param name="durations">Relative durations.</param>
    public Motif(IReadOnlyList<int> intervals, IReadOnlyList<double> durations)
    {
        if (intervals.Count == 0 || durations.Count == 0)
        {
            throw new ArgumentException("Motif must have at least one note");
        }

        if (intervals.Count != durations.Count)
        {
            throw new ArgumentException("Intervals and durations must have same length");
        }

        Intervals = intervals;
        Durations = durations;
    }

    /// <summary>
    /// Number of notes in the motif.
    /// </summary>
    public int Length => Intervals.Count;

    /// <summary>
    /// Total relative duration.
    /// </summary>
    public double TotalDuration => Durations.Sum();

    /// <summary>
    /// Creates a motif from a sequence of notes.
    /// </summary>
    /// <param name="notes">The notes.</param>
    /// <returns>Extracted motif.</returns>
    public static Motif FromNotes(IReadOnlyList<MelodyNote> notes)
    {
        if (notes.Count == 0)
        {
            throw new ArgumentException("Cannot create motif from empty notes");
        }

        var firstMidi = notes[0].Pitch.MidiNumber;
        var firstDuration = notes[0].DurationTicks;

        var intervals = new List<int> { 0 };
        var durations = new List<double> { 1.0 };

        for (var i = 1; i < notes.Count; i++)
        {
            intervals.Add(notes[i].Pitch.MidiNumber - firstMidi);
            durations.Add((double)notes[i].DurationTicks / firstDuration);
        }

        return new Motif(intervals, durations);
    }

    /// <summary>
    /// Transposes the motif by a number of semitones.
    /// </summary>
    public Motif Transpose(int semitones)
    {
        var newIntervals = Intervals.Select(i => i + semitones).ToList();
        return new Motif(newIntervals, Durations.ToList());
    }

    /// <summary>
    /// Creates an inversion of the motif (flips intervals).
    /// </summary>
    public Motif Invert()
    {
        var newIntervals = Intervals.Select(i => -i).ToList();
        return new Motif(newIntervals, Durations.ToList());
    }

    /// <summary>
    /// Creates a retrograde (reversed) version of the motif.
    /// </summary>
    public Motif Retrograde()
    {
        var reversedIntervals = new List<int>();
        var reversedDurations = new List<double>();

        // Recalculate intervals from the new starting point
        var lastInterval = Intervals[^1];
        for (var i = Intervals.Count - 1; i >= 0; i--)
        {
            reversedIntervals.Add(lastInterval - Intervals[i]);
            reversedDurations.Add(Durations[i]);
        }

        return new Motif(reversedIntervals, reversedDurations);
    }

    /// <summary>
    /// Augments (stretches) the durations by a factor.
    /// </summary>
    public Motif Augment(double factor = 2.0)
    {
        var newDurations = Durations.Select(d => d * factor).ToList();
        return new Motif(Intervals.ToList(), newDurations);
    }

    /// <summary>
    /// Diminishes (compresses) the durations by a factor.
    /// </summary>
    public Motif Diminish(double factor = 2.0)
    {
        var newDurations = Durations.Select(d => d / factor).ToList();
        return new Motif(Intervals.ToList(), newDurations);
    }

    /// <summary>
    /// Sequences the motif (repeats at different pitch levels).
    /// </summary>
    /// <param name="step">Interval to transpose each repetition.</param>
    /// <param name="count">Number of repetitions.</param>
    public Motif Sequence(int step, int count)
    {
        var allIntervals = new List<int>();
        var allDurations = new List<double>();

        var baseOffset = 0;
        for (var rep = 0; rep < count; rep++)
        {
            foreach (var interval in Intervals)
            {
                allIntervals.Add(interval + baseOffset);
            }

            allDurations.AddRange(Durations);
            baseOffset += step;
        }

        return new Motif(allIntervals, allDurations);
    }

    /// <summary>
    /// Realizes the motif starting from a given pitch.
    /// </summary>
    /// <param name="startPitch">Starting pitch.</param>
    /// <param name="ticksPerBeat">Ticks per beat for duration calculation.</param>
    /// <param name="startTick">Starting tick position.</param>
    /// <param name="referenceDurationTicks">Duration for 1.0 relative duration.</param>
    /// <returns>Realized melody notes.</returns>
    public IReadOnlyList<MelodyNote> Realize(
        Pitch.Pitch startPitch,
        int ticksPerBeat,
        int startTick = 0,
        int? referenceDurationTicks = null)
    {
        var refDuration = referenceDurationTicks ?? ticksPerBeat;
        var notes = new List<MelodyNote>();
        var currentTick = startTick;

        for (var i = 0; i < Length; i++)
        {
            var pitch = startPitch.Transpose(Intervals[i]);
            var durationTicks = (int)(Durations[i] * refDuration);

            notes.Add(new MelodyNote(pitch, currentTick, durationTicks));
            currentTick += durationTicks;
        }

        return notes;
    }

    /// <summary>
    /// Common melodic motif patterns.
    /// </summary>
    public static class Patterns
    {
        /// <summary>Scale run up (do-re-mi-fa)</summary>
        public static Motif ScaleUp => new([0, 2, 4, 5], [1.0, 1.0, 1.0, 1.0]);

        /// <summary>Scale run down (sol-fa-mi-re)</summary>
        public static Motif ScaleDown => new([0, -2, -4, -5], [1.0, 1.0, 1.0, 1.0]);

        /// <summary>Arpeggio up (do-mi-sol)</summary>
        public static Motif ArpeggioUp => new([0, 4, 7], [1.0, 1.0, 2.0]);

        /// <summary>Arpeggio down (sol-mi-do)</summary>
        public static Motif ArpeggioDown => new([0, -3, -7], [1.0, 1.0, 2.0]);

        /// <summary>Neighbor tone (do-re-do)</summary>
        public static Motif UpperNeighbor => new([0, 2, 0], [1.0, 0.5, 1.5]);

        /// <summary>Lower neighbor (do-ti-do)</summary>
        public static Motif LowerNeighbor => new([0, -1, 0], [1.0, 0.5, 1.5]);

        /// <summary>Turn figure (do-re-do-ti-do)</summary>
        public static Motif Turn => new([0, 2, 0, -1, 0], [0.5, 0.5, 0.5, 0.5, 1.0]);

        /// <summary>Mordent (quick do-re-do)</summary>
        public static Motif Mordent => new([0, 2, 0], [0.25, 0.25, 0.5]);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var intervals = string.Join(",", Intervals);
        return $"Motif[{Length}]: {intervals}";
    }
}
