using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.MusicTheory.Time;

/// <summary>
/// Represents a time signature (meter).
/// </summary>
public readonly struct Meter : IEquatable<Meter>
{
    /// <summary>
    /// Number of beats per measure.
    /// </summary>
    [JsonPropertyName("numerator")]
    public int Numerator { get; }

    /// <summary>
    /// Note value that gets one beat (4 = quarter, 8 = eighth, etc.).
    /// </summary>
    [JsonPropertyName("denominator")]
    public int Denominator { get; }

    /// <summary>
    /// Creates a time signature.
    /// </summary>
    /// <param name="numerator">Beats per measure.</param>
    /// <param name="denominator">Note value per beat (power of 2).</param>
    [JsonConstructor]
    public Meter(int numerator, int denominator)
    {
        if (numerator < 1 || numerator > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), "Numerator must be 1-32");
        }

        if (!IsPowerOfTwo(denominator) || denominator < 1 || denominator > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be a power of 2 (1-64)");
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>
    /// Gets the type of meter (simple, compound, or irregular).
    /// </summary>
    public MeterType Type
    {
        get
        {
            // Compound meters have numerators divisible by 3 (except 3 itself)
            if (Numerator > 3 && Numerator % 3 == 0)
            {
                return MeterType.Compound;
            }

            // Irregular meters are typically 5, 7, 11, etc.
            if (Numerator is 5 or 7 or 11 or 13)
            {
                return MeterType.Irregular;
            }

            return MeterType.Simple;
        }
    }

    /// <summary>
    /// Gets the number of beats in compound/irregular meters.
    /// For simple meters, returns the numerator.
    /// For compound meters, returns numerator / 3.
    /// </summary>
    public int BeatCount => Type == MeterType.Compound ? Numerator / 3 : Numerator;

    /// <summary>
    /// Gets the duration of one measure in quarter-note beats.
    /// </summary>
    public double MeasureBeats => (double)Numerator * 4 / Denominator;

    /// <summary>
    /// Gets the duration of one measure in ticks.
    /// </summary>
    /// <param name="ticksPerBeat">Ticks per quarter note.</param>
    public int MeasureTicks(int ticksPerBeat) => (int)(MeasureBeats * ticksPerBeat);

    /// <summary>
    /// Gets the strong beats (downbeat and secondary accents).
    /// </summary>
    public IEnumerable<int> StrongBeats
    {
        get
        {
            yield return 1; // Downbeat is always strong

            switch (Type)
            {
                case MeterType.Simple when Numerator == 4:
                    yield return 3; // Beat 3 in 4/4
                    break;
                case MeterType.Compound:
                    // Every third beat is strong in compound meters
                    for (var i = 4; i <= Numerator; i += 3)
                    {
                        yield return i;
                    }

                    break;
                case MeterType.Irregular:
                    // Common groupings for irregular meters
                    switch (Numerator)
                    {
                        case 5:
                            yield return 4; // 3+2 grouping
                            break;
                        case 7:
                            yield return 4; // 3+2+2 or 2+2+3
                            yield return 6;
                            break;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Common time signatures.
    /// </summary>
    public static class Common
    {
        /// <summary>4/4 time (common time)</summary>
        public static Meter CommonTime => new(4, 4);

        /// <summary>3/4 time (waltz)</summary>
        public static Meter ThreeFour => new(3, 4);

        /// <summary>2/4 time (march)</summary>
        public static Meter TwoFour => new(2, 4);

        /// <summary>6/8 time (compound duple)</summary>
        public static Meter SixEight => new(6, 8);

        /// <summary>9/8 time (compound triple)</summary>
        public static Meter NineEight => new(9, 8);

        /// <summary>12/8 time (compound quadruple)</summary>
        public static Meter TwelveEight => new(12, 8);

        /// <summary>2/2 time (cut time)</summary>
        public static Meter CutTime => new(2, 2);

        /// <summary>5/4 time (irregular)</summary>
        public static Meter FiveFour => new(5, 4);

        /// <summary>7/8 time (irregular)</summary>
        public static Meter SevenEight => new(7, 8);
    }

    /// <summary>
    /// Parses a time signature from notation like "4/4" or "6/8".
    /// </summary>
    public static Meter Parse(string notation)
    {
        if (string.IsNullOrWhiteSpace(notation))
        {
            throw new ArgumentException("Time signature cannot be empty", nameof(notation));
        }

        var parts = notation.Split('/');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid time signature format: {notation}", nameof(notation));
        }

        if (!int.TryParse(parts[0], out var numerator) || !int.TryParse(parts[1], out var denominator))
        {
            throw new ArgumentException($"Invalid time signature values: {notation}", nameof(notation));
        }

        return new Meter(numerator, denominator);
    }

    /// <inheritdoc />
    public bool Equals(Meter other) => Numerator == other.Numerator && Denominator == other.Denominator;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Meter other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    /// <summary>Tests equality between two meters.</summary>
    public static bool operator ==(Meter a, Meter b) => a.Equals(b);
    /// <summary>Tests inequality between two meters.</summary>
    public static bool operator !=(Meter a, Meter b) => !a.Equals(b);

    /// <inheritdoc />
    public override string ToString() => $"{Numerator}/{Denominator}";
}

/// <summary>
/// Types of meter.
/// </summary>
public enum MeterType
{
    /// <summary>Simple meter (2, 3, 4 beats)</summary>
    Simple,

    /// <summary>Compound meter (6, 9, 12 beats grouped in 3s)</summary>
    Compound,

    /// <summary>Irregular/asymmetric meter (5, 7, etc.)</summary>
    Irregular
}
