namespace BeyondImmersion.Bannou.MusicTheory.Time;

/// <summary>
/// Standard note duration values relative to a whole note.
/// </summary>
public enum DurationValue
{
    /// <summary>Whole note (semibreve) - 4 beats in 4/4</summary>
    Whole = 1,

    /// <summary>Half note (minim) - 2 beats in 4/4</summary>
    Half = 2,

    /// <summary>Quarter note (crotchet) - 1 beat in 4/4</summary>
    Quarter = 4,

    /// <summary>Eighth note (quaver) - 1/2 beat in 4/4</summary>
    Eighth = 8,

    /// <summary>Sixteenth note (semiquaver) - 1/4 beat in 4/4</summary>
    Sixteenth = 16,

    /// <summary>Thirty-second note (demisemiquaver)</summary>
    ThirtySecond = 32,

    /// <summary>Sixty-fourth note (hemidemisemiquaver)</summary>
    SixtyFourth = 64
}

/// <summary>
/// Represents a musical duration with optional dotting and tuplet modifications.
/// </summary>
public readonly struct Duration : IEquatable<Duration>, IComparable<Duration>
{
    /// <summary>
    /// The base note value.
    /// </summary>
    public DurationValue Value { get; }

    /// <summary>
    /// Number of dots (0-2). Each dot adds half the previous duration.
    /// </summary>
    public int Dots { get; }

    /// <summary>
    /// Tuplet ratio numerator (e.g., 3 for triplets). 1 for normal.
    /// </summary>
    public int TupletNumerator { get; }

    /// <summary>
    /// Tuplet ratio denominator (e.g., 2 for triplets). 1 for normal.
    /// </summary>
    public int TupletDenominator { get; }

    /// <summary>
    /// Creates a simple duration without modifications.
    /// </summary>
    public Duration(DurationValue value) : this(value, 0, 1, 1)
    {
    }

    /// <summary>
    /// Creates a dotted duration.
    /// </summary>
    public Duration(DurationValue value, int dots) : this(value, dots, 1, 1)
    {
    }

    /// <summary>
    /// Creates a duration with all modifiers.
    /// </summary>
    /// <param name="value">Base note value.</param>
    /// <param name="dots">Number of dots (0-2).</param>
    /// <param name="tupletNumerator">Tuplet ratio numerator.</param>
    /// <param name="tupletDenominator">Tuplet ratio denominator.</param>
    public Duration(DurationValue value, int dots, int tupletNumerator, int tupletDenominator)
    {
        if (dots < 0 || dots > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(dots), "Dots must be 0-2");
        }

        if (tupletNumerator < 1 || tupletDenominator < 1)
        {
            throw new ArgumentOutOfRangeException("Tuplet values must be positive");
        }

        Value = value;
        Dots = dots;
        TupletNumerator = tupletNumerator;
        TupletDenominator = tupletDenominator;
    }

    /// <summary>
    /// Gets the duration in beats (quarter note = 1 beat).
    /// </summary>
    public double Beats
    {
        get
        {
            // Base duration in beats (quarter = 1)
            var baseDuration = 4.0 / (int)Value;

            // Apply dots
            var dotMultiplier = 1.0;
            var dotValue = 0.5;
            for (var i = 0; i < Dots; i++)
            {
                dotMultiplier += dotValue;
                dotValue /= 2;
            }

            // Apply tuplet
            var tupletMultiplier = (double)TupletDenominator / TupletNumerator;

            return baseDuration * dotMultiplier * tupletMultiplier;
        }
    }

    /// <summary>
    /// Gets the duration in ticks given a ticks-per-beat value (PPQN).
    /// </summary>
    /// <param name="ticksPerBeat">Ticks per quarter note.</param>
    /// <returns>Duration in ticks.</returns>
    public int ToTicks(int ticksPerBeat)
    {
        return (int)Math.Round(Beats * ticksPerBeat);
    }

    /// <summary>
    /// Creates a triplet version of this duration.
    /// </summary>
    public Duration AsTriplet() => new(Value, Dots, 3, 2);

    /// <summary>
    /// Creates a dotted version of this duration.
    /// </summary>
    public Duration Dotted() => new(Value, Math.Min(Dots + 1, 2), TupletNumerator, TupletDenominator);

    /// <summary>
    /// Common duration presets.
    /// </summary>
    public static class Common
    {
        /// <summary>Whole note</summary>
        public static Duration Whole => new(DurationValue.Whole);

        /// <summary>Dotted half note</summary>
        public static Duration DottedHalf => new(DurationValue.Half, 1);

        /// <summary>Half note</summary>
        public static Duration Half => new(DurationValue.Half);

        /// <summary>Dotted quarter note</summary>
        public static Duration DottedQuarter => new(DurationValue.Quarter, 1);

        /// <summary>Quarter note</summary>
        public static Duration Quarter => new(DurationValue.Quarter);

        /// <summary>Dotted eighth note</summary>
        public static Duration DottedEighth => new(DurationValue.Eighth, 1);

        /// <summary>Eighth note</summary>
        public static Duration Eighth => new(DurationValue.Eighth);

        /// <summary>Sixteenth note</summary>
        public static Duration Sixteenth => new(DurationValue.Sixteenth);

        /// <summary>Eighth note triplet</summary>
        public static Duration EighthTriplet => new(DurationValue.Eighth, 0, 3, 2);

        /// <summary>Quarter note triplet</summary>
        public static Duration QuarterTriplet => new(DurationValue.Quarter, 0, 3, 2);
    }

    /// <summary>
    /// Creates a duration from a number of beats.
    /// </summary>
    /// <param name="beats">Number of beats.</param>
    /// <returns>Closest matching duration.</returns>
    public static Duration FromBeats(double beats)
    {
        // Find the closest standard duration
        return beats switch
        {
            >= 3.5 => Common.Whole,
            >= 2.5 => Common.DottedHalf,
            >= 1.75 => Common.Half,
            >= 1.25 => Common.DottedQuarter,
            >= 0.875 => Common.Quarter,
            >= 0.625 => Common.DottedEighth,
            >= 0.375 => Common.Eighth,
            >= 0.1875 => Common.Sixteenth,
            _ => new Duration(DurationValue.ThirtySecond)
        };
    }

    /// <inheritdoc />
    public bool Equals(Duration other)
    {
        return Math.Abs(Beats - other.Beats) < 0.001;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Duration other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => Beats.GetHashCode();
    /// <inheritdoc />
    public int CompareTo(Duration other) => Beats.CompareTo(other.Beats);

    /// <summary>Tests equality between two durations.</summary>
    public static bool operator ==(Duration a, Duration b) => a.Equals(b);
    /// <summary>Tests inequality between two durations.</summary>
    public static bool operator !=(Duration a, Duration b) => !a.Equals(b);
    /// <summary>Tests if one duration is shorter than another.</summary>
    public static bool operator <(Duration a, Duration b) => a.Beats < b.Beats;
    /// <summary>Tests if one duration is longer than another.</summary>
    public static bool operator >(Duration a, Duration b) => a.Beats > b.Beats;
    /// <summary>Tests if one duration is shorter than or equal to another.</summary>
    public static bool operator <=(Duration a, Duration b) => a.Beats <= b.Beats;
    /// <summary>Tests if one duration is longer than or equal to another.</summary>
    public static bool operator >=(Duration a, Duration b) => a.Beats >= b.Beats;

    /// <inheritdoc />
    public override string ToString()
    {
        var name = Value switch
        {
            DurationValue.Whole => "w",
            DurationValue.Half => "h",
            DurationValue.Quarter => "q",
            DurationValue.Eighth => "e",
            DurationValue.Sixteenth => "s",
            DurationValue.ThirtySecond => "t",
            DurationValue.SixtyFourth => "x",
            _ => "?"
        };

        var dots = new string('.', Dots);
        var tuplet = TupletNumerator != 1 ? $"({TupletNumerator}:{TupletDenominator})" : "";

        return $"{name}{dots}{tuplet}";
    }
}
