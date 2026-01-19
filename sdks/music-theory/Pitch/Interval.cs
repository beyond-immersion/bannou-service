namespace BeyondImmersion.Bannou.MusicTheory.Pitch;

/// <summary>
/// Represents a musical interval as a number of semitones.
/// Provides named constants for common intervals and operations.
/// </summary>
public readonly struct Interval : IEquatable<Interval>, IComparable<Interval>
{
    /// <summary>
    /// The interval size in semitones.
    /// </summary>
    public int Semitones { get; }

    /// <summary>
    /// Creates an interval with the specified number of semitones.
    /// </summary>
    /// <param name="semitones">Number of semitones (can be negative for descending).</param>
    public Interval(int semitones)
    {
        Semitones = semitones;
    }

    // Common interval constants (ascending)
    /// <summary>Perfect unison (0 semitones)</summary>
    public static Interval PerfectUnison => new(0);

    /// <summary>Minor second (1 semitone)</summary>
    public static Interval MinorSecond => new(1);

    /// <summary>Major second (2 semitones)</summary>
    public static Interval MajorSecond => new(2);

    /// <summary>Minor third (3 semitones)</summary>
    public static Interval MinorThird => new(3);

    /// <summary>Major third (4 semitones)</summary>
    public static Interval MajorThird => new(4);

    /// <summary>Perfect fourth (5 semitones)</summary>
    public static Interval PerfectFourth => new(5);

    /// <summary>Tritone / Augmented fourth / Diminished fifth (6 semitones)</summary>
    public static Interval Tritone => new(6);

    /// <summary>Perfect fifth (7 semitones)</summary>
    public static Interval PerfectFifth => new(7);

    /// <summary>Minor sixth (8 semitones)</summary>
    public static Interval MinorSixth => new(8);

    /// <summary>Major sixth (9 semitones)</summary>
    public static Interval MajorSixth => new(9);

    /// <summary>Minor seventh (10 semitones)</summary>
    public static Interval MinorSeventh => new(10);

    /// <summary>Major seventh (11 semitones)</summary>
    public static Interval MajorSeventh => new(11);

    /// <summary>Perfect octave (12 semitones)</summary>
    public static Interval PerfectOctave => new(12);

    /// <summary>Minor ninth (13 semitones)</summary>
    public static Interval MinorNinth => new(13);

    /// <summary>Major ninth (14 semitones)</summary>
    public static Interval MajorNinth => new(14);

    /// <summary>
    /// Gets the simple interval (reduced to within one octave).
    /// </summary>
    public Interval Simple => new(((Semitones % 12) + 12) % 12);

    /// <summary>
    /// Gets the interval class (0-6, representing the shortest distance).
    /// Used for set theory analysis.
    /// </summary>
    public int IntervalClass
    {
        get
        {
            var simple = ((Semitones % 12) + 12) % 12;
            return simple > 6 ? 12 - simple : simple;
        }
    }

    /// <summary>
    /// Gets whether this is a consonant interval.
    /// </summary>
    public bool IsConsonant => Semitones switch
    {
        0 or 3 or 4 or 5 or 7 or 8 or 9 or 12 => true,
        _ => false
    };

    /// <summary>
    /// Gets whether this is a perfect interval (unison, fourth, fifth, octave).
    /// </summary>
    public bool IsPerfect => Simple.Semitones is 0 or 5 or 7;

    /// <summary>
    /// Gets whether this is a step (major or minor second).
    /// </summary>
    public bool IsStep => Math.Abs(Semitones) is 1 or 2;

    /// <summary>
    /// Gets whether this is a third.
    /// </summary>
    public bool IsThird => Math.Abs(Semitones) is 3 or 4;

    /// <summary>
    /// Gets whether this is a leap (larger than a third).
    /// </summary>
    public bool IsLeap => Math.Abs(Semitones) > 4;

    /// <summary>
    /// Gets the inversion of this interval.
    /// </summary>
    public Interval Inversion => new(12 - ((Semitones % 12 + 12) % 12));

    /// <summary>
    /// Gets a human-readable name for this interval.
    /// </summary>
    public string Name => Semitones switch
    {
        0 => "P1",
        1 => "m2",
        2 => "M2",
        3 => "m3",
        4 => "M3",
        5 => "P4",
        6 => "TT",
        7 => "P5",
        8 => "m6",
        9 => "M6",
        10 => "m7",
        11 => "M7",
        12 => "P8",
        13 => "m9",
        14 => "M9",
        _ => $"{Semitones}st"
    };

    /// <summary>Adds two intervals together.</summary>
    public static Interval operator +(Interval a, Interval b) => new(a.Semitones + b.Semitones);
    /// <summary>Subtracts one interval from another.</summary>
    public static Interval operator -(Interval a, Interval b) => new(a.Semitones - b.Semitones);
    /// <summary>Negates an interval.</summary>
    public static Interval operator -(Interval a) => new(-a.Semitones);
    /// <summary>Tests equality between two intervals.</summary>
    public static bool operator ==(Interval a, Interval b) => a.Semitones == b.Semitones;
    /// <summary>Tests inequality between two intervals.</summary>
    public static bool operator !=(Interval a, Interval b) => a.Semitones != b.Semitones;
    /// <summary>Tests if one interval is smaller than another.</summary>
    public static bool operator <(Interval a, Interval b) => a.Semitones < b.Semitones;
    /// <summary>Tests if one interval is greater than another.</summary>
    public static bool operator >(Interval a, Interval b) => a.Semitones > b.Semitones;
    /// <summary>Tests if one interval is smaller than or equal to another.</summary>
    public static bool operator <=(Interval a, Interval b) => a.Semitones <= b.Semitones;
    /// <summary>Tests if one interval is greater than or equal to another.</summary>
    public static bool operator >=(Interval a, Interval b) => a.Semitones >= b.Semitones;

    /// <summary>Implicitly converts an interval to its semitone count.</summary>
    public static implicit operator int(Interval interval) => interval.Semitones;
    /// <summary>Explicitly converts a semitone count to an interval.</summary>
    public static explicit operator Interval(int semitones) => new(semitones);

    /// <inheritdoc />
    public bool Equals(Interval other) => Semitones == other.Semitones;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Interval other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => Semitones.GetHashCode();
    /// <inheritdoc />
    public int CompareTo(Interval other) => Semitones.CompareTo(other.Semitones);
    /// <inheritdoc />
    public override string ToString() => Name;
}
