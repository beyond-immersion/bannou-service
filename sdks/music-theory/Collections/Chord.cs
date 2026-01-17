using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicTheory.Collections;

/// <summary>
/// Chord quality types.
/// </summary>
public enum ChordQuality
{
    /// <summary>Major triad (1-3-5)</summary>
    Major,

    /// <summary>Minor triad (1-b3-5)</summary>
    Minor,

    /// <summary>Diminished triad (1-b3-b5)</summary>
    Diminished,

    /// <summary>Augmented triad (1-3-#5)</summary>
    Augmented,

    /// <summary>Dominant seventh (1-3-5-b7)</summary>
    Dominant7,

    /// <summary>Major seventh (1-3-5-7)</summary>
    Major7,

    /// <summary>Minor seventh (1-b3-5-b7)</summary>
    Minor7,

    /// <summary>Diminished seventh (1-b3-b5-bb7)</summary>
    Diminished7,

    /// <summary>Half-diminished seventh (1-b3-b5-b7)</summary>
    HalfDiminished7,

    /// <summary>Augmented seventh (1-3-#5-b7)</summary>
    Augmented7,

    /// <summary>Minor-major seventh (1-b3-5-7)</summary>
    MinorMajor7,

    /// <summary>Suspended second (1-2-5)</summary>
    Sus2,

    /// <summary>Suspended fourth (1-4-5)</summary>
    Sus4,

    /// <summary>Power chord (1-5)</summary>
    Power
}

/// <summary>
/// Extension methods for ChordQuality.
/// </summary>
public static class ChordQualityExtensions
{
    /// <summary>
    /// Gets the interval pattern (semitones from root) for a chord quality.
    /// </summary>
    public static IReadOnlyList<int> GetIntervals(this ChordQuality quality)
    {
        return quality switch
        {
            ChordQuality.Major => [0, 4, 7],
            ChordQuality.Minor => [0, 3, 7],
            ChordQuality.Diminished => [0, 3, 6],
            ChordQuality.Augmented => [0, 4, 8],
            ChordQuality.Dominant7 => [0, 4, 7, 10],
            ChordQuality.Major7 => [0, 4, 7, 11],
            ChordQuality.Minor7 => [0, 3, 7, 10],
            ChordQuality.Diminished7 => [0, 3, 6, 9],
            ChordQuality.HalfDiminished7 => [0, 3, 6, 10],
            ChordQuality.Augmented7 => [0, 4, 8, 10],
            ChordQuality.MinorMajor7 => [0, 3, 7, 11],
            ChordQuality.Sus2 => [0, 2, 7],
            ChordQuality.Sus4 => [0, 5, 7],
            ChordQuality.Power => [0, 7],
            _ => throw new ArgumentOutOfRangeException(nameof(quality))
        };
    }

    /// <summary>
    /// Gets the chord symbol suffix for display.
    /// </summary>
    public static string GetSymbol(this ChordQuality quality)
    {
        return quality switch
        {
            ChordQuality.Major => "",
            ChordQuality.Minor => "m",
            ChordQuality.Diminished => "dim",
            ChordQuality.Augmented => "aug",
            ChordQuality.Dominant7 => "7",
            ChordQuality.Major7 => "maj7",
            ChordQuality.Minor7 => "m7",
            ChordQuality.Diminished7 => "dim7",
            ChordQuality.HalfDiminished7 => "m7b5",
            ChordQuality.Augmented7 => "aug7",
            ChordQuality.MinorMajor7 => "mMaj7",
            ChordQuality.Sus2 => "sus2",
            ChordQuality.Sus4 => "sus4",
            ChordQuality.Power => "5",
            _ => throw new ArgumentOutOfRangeException(nameof(quality))
        };
    }

    /// <summary>
    /// Parses a chord quality from a symbol string.
    /// </summary>
    public static ChordQuality Parse(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return ChordQuality.Major;
        }

        return symbol.Trim().ToLowerInvariant() switch
        {
            "" or "maj" or "major" => ChordQuality.Major,
            "m" or "min" or "minor" => ChordQuality.Minor,
            "dim" or "o" or "diminished" => ChordQuality.Diminished,
            "aug" or "+" or "augmented" => ChordQuality.Augmented,
            "7" or "dom7" or "dominant7" => ChordQuality.Dominant7,
            "maj7" or "major7" or "m7" when symbol.Contains("aj") => ChordQuality.Major7,
            "m7" or "min7" or "minor7" => ChordQuality.Minor7,
            "dim7" or "o7" or "diminished7" => ChordQuality.Diminished7,
            "m7b5" or "ø" or "ø7" or "halfdim" or "halfdiminished7" => ChordQuality.HalfDiminished7,
            "aug7" or "+7" or "augmented7" => ChordQuality.Augmented7,
            "mmaj7" or "minmaj7" or "minormajor7" => ChordQuality.MinorMajor7,
            "sus2" or "suspended2" => ChordQuality.Sus2,
            "sus4" or "sus" or "suspended4" => ChordQuality.Sus4,
            "5" or "power" => ChordQuality.Power,
            _ => throw new ArgumentException($"Unknown chord quality: {symbol}", nameof(symbol))
        };
    }
}

/// <summary>
/// Represents a chord as a set of pitch classes with a root and quality.
/// </summary>
public sealed class Chord
{
    /// <summary>
    /// The root pitch class.
    /// </summary>
    public PitchClass Root { get; }

    /// <summary>
    /// The chord quality.
    /// </summary>
    public ChordQuality Quality { get; }

    /// <summary>
    /// The bass note (for inversions/slash chords). Null means root is bass.
    /// </summary>
    public PitchClass? Bass { get; }

    /// <summary>
    /// The pitch classes in this chord.
    /// </summary>
    public IReadOnlyList<PitchClass> PitchClasses { get; }

    /// <summary>
    /// Creates a chord with the given root and quality.
    /// </summary>
    /// <param name="root">The root pitch class.</param>
    /// <param name="quality">The chord quality.</param>
    /// <param name="bass">Optional bass note for inversions.</param>
    public Chord(PitchClass root, ChordQuality quality, PitchClass? bass = null)
    {
        Root = root;
        Quality = quality;
        Bass = bass;

        var intervals = quality.GetIntervals();
        var pitchClasses = new PitchClass[intervals.Count];

        for (var i = 0; i < intervals.Count; i++)
        {
            pitchClasses[i] = root.Transpose(intervals[i]);
        }

        PitchClasses = pitchClasses;
    }

    /// <summary>
    /// Gets the chord symbol (e.g., "C", "Am", "G7").
    /// </summary>
    public string Symbol
    {
        get
        {
            var symbol = Root.ToSharpName() + Quality.GetSymbol();
            if (Bass.HasValue && Bass.Value != Root)
            {
                symbol += "/" + Bass.Value.ToSharpName();
            }

            return symbol;
        }
    }

    /// <summary>
    /// Checks if a pitch class is part of this chord.
    /// </summary>
    public bool Contains(PitchClass pitchClass)
    {
        return PitchClasses.Contains(pitchClass);
    }

    /// <summary>
    /// Gets the inversion of this chord (rotates the bass note).
    /// </summary>
    /// <param name="inversion">Inversion number (0 = root, 1 = first, etc.)</param>
    /// <returns>The inverted chord.</returns>
    public Chord GetInversion(int inversion)
    {
        if (inversion < 0 || inversion >= PitchClasses.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(inversion));
        }

        return new Chord(Root, Quality, PitchClasses[inversion]);
    }

    /// <summary>
    /// Transposes the chord by the given interval.
    /// </summary>
    public Chord Transpose(int semitones)
    {
        var newRoot = Root.Transpose(semitones);
        var newBass = Bass?.Transpose(semitones);
        return new Chord(newRoot, Quality, newBass);
    }

    /// <summary>
    /// Parses a chord from a symbol string (e.g., "Am", "G7", "Dm/F").
    /// </summary>
    public static Chord Parse(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Chord symbol cannot be empty", nameof(symbol));
        }

        symbol = symbol.Trim();

        // Check for slash chord
        PitchClass? bass = null;
        var slashIndex = symbol.IndexOf('/');
        if (slashIndex > 0)
        {
            var bassPart = symbol[(slashIndex + 1)..];
            bass = PitchClassExtensions.Parse(bassPart);
            symbol = symbol[..slashIndex];
        }

        // Find where the root ends (1-2 characters)
        var rootEnd = 1;
        if (symbol.Length > 1 && (symbol[1] == '#' || symbol[1] == 'b'))
        {
            rootEnd = 2;
        }

        var rootPart = symbol[..rootEnd];
        var qualityPart = symbol[rootEnd..];

        var root = PitchClassExtensions.Parse(rootPart);
        var quality = ChordQualityExtensions.Parse(qualityPart);

        return new Chord(root, quality, bass);
    }

    /// <summary>
    /// Gets the diatonic chord at a given scale degree.
    /// </summary>
    /// <param name="scale">The scale.</param>
    /// <param name="degree">Scale degree (1-7).</param>
    /// <param name="seventh">Whether to include the seventh.</param>
    /// <returns>The diatonic chord.</returns>
    public static Chord FromScaleDegree(Scale scale, int degree, bool seventh = false)
    {
        if (degree < 1 || degree > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(degree), "Degree must be 1-7");
        }

        var root = scale.GetPitchClassAtDegree(degree);
        var third = scale.GetPitchClassAtDegree(degree + 2);
        var fifth = scale.GetPitchClassAtDegree(degree + 4);

        // Determine quality based on intervals
        var thirdInterval = root.SemitonesTo(third);
        var fifthInterval = root.SemitonesTo(fifth);

        ChordQuality quality;

        if (seventh)
        {
            var seventhNote = scale.GetPitchClassAtDegree(degree + 6);
            var seventhInterval = root.SemitonesTo(seventhNote);

            quality = (thirdInterval, fifthInterval, seventhInterval) switch
            {
                (4, 7, 11) => ChordQuality.Major7,
                (4, 7, 10) => ChordQuality.Dominant7,
                (3, 7, 10) => ChordQuality.Minor7,
                (3, 6, 10) => ChordQuality.HalfDiminished7,
                (3, 6, 9) => ChordQuality.Diminished7,
                _ => ChordQuality.Dominant7 // Default fallback
            };
        }
        else
        {
            quality = (thirdInterval, fifthInterval) switch
            {
                (4, 7) => ChordQuality.Major,
                (3, 7) => ChordQuality.Minor,
                (3, 6) => ChordQuality.Diminished,
                (4, 8) => ChordQuality.Augmented,
                _ => ChordQuality.Major // Default fallback
            };
        }

        return new Chord(root, quality);
    }

    public override string ToString() => Symbol;
}
