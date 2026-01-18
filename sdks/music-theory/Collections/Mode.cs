using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicTheory.Collections;

/// <summary>
/// Represents musical modes (scales with specific interval patterns).
/// </summary>
public enum ModeType
{
    /// <summary>Major mode (Ionian)</summary>
    Major,

    /// <summary>Natural minor mode (Aeolian)</summary>
    Minor,

    /// <summary>Dorian mode (minor with raised 6th)</summary>
    Dorian,

    /// <summary>Phrygian mode (minor with lowered 2nd)</summary>
    Phrygian,

    /// <summary>Lydian mode (major with raised 4th)</summary>
    Lydian,

    /// <summary>Mixolydian mode (major with lowered 7th)</summary>
    Mixolydian,

    /// <summary>Aeolian mode (natural minor)</summary>
    Aeolian,

    /// <summary>Locrian mode (diminished)</summary>
    Locrian,

    /// <summary>Harmonic minor</summary>
    HarmonicMinor,

    /// <summary>Melodic minor (ascending)</summary>
    MelodicMinor,

    /// <summary>Major pentatonic</summary>
    MajorPentatonic,

    /// <summary>Minor pentatonic</summary>
    MinorPentatonic,

    /// <summary>Blues scale</summary>
    Blues,

    /// <summary>Whole tone scale</summary>
    WholeTone,

    /// <summary>Chromatic scale</summary>
    Chromatic
}

/// <summary>
/// Defines the interval pattern for a mode.
/// </summary>
public static class ModePatterns
{
    /// <summary>
    /// Gets the interval pattern (in semitones from root) for a mode.
    /// </summary>
    public static IReadOnlyList<int> GetPattern(ModeType mode)
    {
        return mode switch
        {
            ModeType.Major => [0, 2, 4, 5, 7, 9, 11],
            ModeType.Minor or ModeType.Aeolian => [0, 2, 3, 5, 7, 8, 10],
            ModeType.Dorian => [0, 2, 3, 5, 7, 9, 10],
            ModeType.Phrygian => [0, 1, 3, 5, 7, 8, 10],
            ModeType.Lydian => [0, 2, 4, 6, 7, 9, 11],
            ModeType.Mixolydian => [0, 2, 4, 5, 7, 9, 10],
            ModeType.Locrian => [0, 1, 3, 5, 6, 8, 10],
            ModeType.HarmonicMinor => [0, 2, 3, 5, 7, 8, 11],
            ModeType.MelodicMinor => [0, 2, 3, 5, 7, 9, 11],
            ModeType.MajorPentatonic => [0, 2, 4, 7, 9],
            ModeType.MinorPentatonic => [0, 3, 5, 7, 10],
            ModeType.Blues => [0, 3, 5, 6, 7, 10],
            ModeType.WholeTone => [0, 2, 4, 6, 8, 10],
            ModeType.Chromatic => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>
    /// Gets whether a mode is considered "major" (has a major third).
    /// </summary>
    public static bool IsMajor(ModeType mode)
    {
        return mode switch
        {
            ModeType.Major or ModeType.Lydian or ModeType.Mixolydian or ModeType.MajorPentatonic => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets the relative major/minor mode.
    /// </summary>
    public static ModeType GetRelative(ModeType mode)
    {
        return mode switch
        {
            ModeType.Major => ModeType.Minor,
            ModeType.Minor => ModeType.Major,
            ModeType.Aeolian => ModeType.Major,
            ModeType.Dorian => ModeType.Major,
            ModeType.Phrygian => ModeType.Major,
            ModeType.Lydian => ModeType.Major,
            ModeType.Mixolydian => ModeType.Major,
            ModeType.Locrian => ModeType.Major,
            _ => mode
        };
    }

    /// <summary>
    /// Parses a mode type from a string.
    /// </summary>
    public static ModeType Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Mode name cannot be empty", nameof(name));
        }

        return name.Trim().ToLowerInvariant() switch
        {
            "major" or "ionian" => ModeType.Major,
            "minor" or "natural minor" or "aeolian" => ModeType.Minor,
            "dorian" => ModeType.Dorian,
            "phrygian" => ModeType.Phrygian,
            "lydian" => ModeType.Lydian,
            "mixolydian" => ModeType.Mixolydian,
            "locrian" => ModeType.Locrian,
            "harmonic minor" or "harmonicminor" => ModeType.HarmonicMinor,
            "melodic minor" or "melodicminor" => ModeType.MelodicMinor,
            "major pentatonic" or "majorpentatonic" => ModeType.MajorPentatonic,
            "minor pentatonic" or "minorpentatonic" => ModeType.MinorPentatonic,
            "blues" => ModeType.Blues,
            "whole tone" or "wholetone" => ModeType.WholeTone,
            "chromatic" => ModeType.Chromatic,
            _ => throw new ArgumentException($"Unknown mode: {name}", nameof(name))
        };
    }
}
