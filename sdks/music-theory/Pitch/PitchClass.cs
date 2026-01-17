namespace BeyondImmersion.Bannou.MusicTheory.Pitch;

/// <summary>
/// Represents one of the 12 pitch classes in 12-TET (twelve-tone equal temperament).
/// Uses sharps as the canonical accidental representation.
/// </summary>
public enum PitchClass
{
    /// <summary>C natural</summary>
    C = 0,

    /// <summary>C sharp / D flat</summary>
    Cs = 1,

    /// <summary>D natural</summary>
    D = 2,

    /// <summary>D sharp / E flat</summary>
    Ds = 3,

    /// <summary>E natural</summary>
    E = 4,

    /// <summary>F natural</summary>
    F = 5,

    /// <summary>F sharp / G flat</summary>
    Fs = 6,

    /// <summary>G natural</summary>
    G = 7,

    /// <summary>G sharp / A flat</summary>
    Gs = 8,

    /// <summary>A natural</summary>
    A = 9,

    /// <summary>A sharp / B flat</summary>
    As = 10,

    /// <summary>B natural</summary>
    B = 11
}

/// <summary>
/// Extension methods for PitchClass operations.
/// </summary>
public static class PitchClassExtensions
{
    private static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] FlatNames = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];

    /// <summary>
    /// Transposes a pitch class by the given number of semitones.
    /// </summary>
    /// <param name="pitchClass">The pitch class to transpose.</param>
    /// <param name="semitones">Number of semitones to transpose (can be negative).</param>
    /// <returns>The transposed pitch class.</returns>
    public static PitchClass Transpose(this PitchClass pitchClass, int semitones)
    {
        var newValue = ((int)pitchClass + semitones % 12 + 12) % 12;
        return (PitchClass)newValue;
    }

    /// <summary>
    /// Gets the interval in semitones from this pitch class to another.
    /// Always returns a positive value (ascending interval).
    /// </summary>
    /// <param name="from">Starting pitch class.</param>
    /// <param name="to">Target pitch class.</param>
    /// <returns>Interval in semitones (0-11).</returns>
    public static int SemitonesTo(this PitchClass from, PitchClass to)
    {
        return ((int)to - (int)from + 12) % 12;
    }

    /// <summary>
    /// Gets the display name using sharps.
    /// </summary>
    public static string ToSharpName(this PitchClass pitchClass)
    {
        return SharpNames[(int)pitchClass];
    }

    /// <summary>
    /// Gets the display name using flats.
    /// </summary>
    public static string ToFlatName(this PitchClass pitchClass)
    {
        return FlatNames[(int)pitchClass];
    }

    /// <summary>
    /// Parses a pitch class from a string representation.
    /// Accepts both sharps (#) and flats (b).
    /// </summary>
    /// <param name="name">The pitch class name (e.g., "C", "C#", "Db").</param>
    /// <returns>The parsed pitch class.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is invalid.</exception>
    public static PitchClass Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Pitch class name cannot be empty", nameof(name));
        }

        var normalized = name.Trim().ToUpperInvariant();

        // Handle enharmonic equivalents
        return normalized switch
        {
            "C" => PitchClass.C,
            "C#" or "CS" or "DB" => PitchClass.Cs,
            "D" => PitchClass.D,
            "D#" or "DS" or "EB" => PitchClass.Ds,
            "E" or "FB" => PitchClass.E,
            "F" or "E#" or "ES" => PitchClass.F,
            "F#" or "FS" or "GB" => PitchClass.Fs,
            "G" => PitchClass.G,
            "G#" or "GS" or "AB" => PitchClass.Gs,
            "A" => PitchClass.A,
            "A#" or "AS" or "BB" => PitchClass.As,
            "B" or "CB" => PitchClass.B,
            _ => throw new ArgumentException($"Invalid pitch class name: {name}", nameof(name))
        };
    }

    /// <summary>
    /// Attempts to parse a pitch class from a string.
    /// </summary>
    /// <param name="name">The pitch class name.</param>
    /// <param name="result">The parsed pitch class if successful.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(string name, out PitchClass result)
    {
        try
        {
            result = Parse(name);
            return true;
        }
        catch (ArgumentException)
        {
            result = default;
            return false;
        }
    }
}
