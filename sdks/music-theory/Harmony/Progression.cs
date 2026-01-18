using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicTheory.Harmony;

/// <summary>
/// Represents a chord in a progression with timing information.
/// </summary>
public sealed class ProgressionChord
{
    /// <summary>
    /// The chord.
    /// </summary>
    public Chord Chord { get; }

    /// <summary>
    /// Roman numeral analysis.
    /// </summary>
    public string RomanNumeral { get; }

    /// <summary>
    /// Scale degree (1-7).
    /// </summary>
    public int Degree { get; }

    /// <summary>
    /// Duration in beats.
    /// </summary>
    public double DurationBeats { get; }

    /// <summary>
    /// Creates a progression chord.
    /// </summary>
    public ProgressionChord(Chord chord, string romanNumeral, int degree, double durationBeats)
    {
        Chord = chord;
        RomanNumeral = romanNumeral;
        Degree = degree;
        DurationBeats = durationBeats;
    }

    public override string ToString() => $"{RomanNumeral} ({DurationBeats}b)";
}

/// <summary>
/// Options for progression generation.
/// </summary>
public sealed class ProgressionOptions
{
    /// <summary>
    /// Whether to use seventh chords.
    /// </summary>
    public bool UseSevenths { get; set; }

    /// <summary>
    /// Whether to allow secondary dominants.
    /// </summary>
    public bool AllowSecondaryDominants { get; set; } = true;

    /// <summary>
    /// Whether to allow modal interchange (borrowed chords).
    /// </summary>
    public bool AllowModalInterchange { get; set; }

    /// <summary>
    /// Preferred cadence type at the end.
    /// </summary>
    public CadenceType? EndingCadence { get; set; } = CadenceType.AuthenticPerfect;

    /// <summary>
    /// Starting degree (null = random).
    /// </summary>
    public int? StartDegree { get; set; } = 1;

    /// <summary>
    /// Duration per chord in beats.
    /// </summary>
    public double BeatsPerChord { get; set; } = 4.0;

    /// <summary>
    /// Random seed for reproducibility.
    /// </summary>
    public int? Seed { get; set; }
}

/// <summary>
/// Generates chord progressions using harmonic function theory.
/// </summary>
public sealed class ProgressionGenerator
{
    private readonly Random _random;

    // Transition probabilities for major keys (from degree -> to degree)
    private static readonly Dictionary<int, (int degree, double weight)[]> MajorTransitions = new()
    {
        [1] = [(4, 0.3), (5, 0.25), (6, 0.2), (2, 0.15), (3, 0.1)],
        [2] = [(5, 0.5), (7, 0.2), (4, 0.15), (1, 0.1), (3, 0.05)],
        [3] = [(6, 0.35), (4, 0.3), (2, 0.2), (1, 0.15)],
        [4] = [(5, 0.4), (1, 0.25), (2, 0.2), (7, 0.1), (6, 0.05)],
        [5] = [(1, 0.5), (6, 0.25), (4, 0.15), (3, 0.1)],
        [6] = [(4, 0.3), (2, 0.3), (5, 0.2), (3, 0.1), (1, 0.1)],
        [7] = [(1, 0.5), (3, 0.25), (6, 0.15), (5, 0.1)]
    };

    /// <summary>
    /// Creates a progression generator.
    /// </summary>
    /// <param name="seed">Optional random seed.</param>
    public ProgressionGenerator(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Generates a chord progression.
    /// </summary>
    /// <param name="scale">The scale/key.</param>
    /// <param name="length">Number of chords.</param>
    /// <param name="options">Generation options.</param>
    /// <returns>The generated progression.</returns>
    public IReadOnlyList<ProgressionChord> Generate(Scale scale, int length, ProgressionOptions? options = null)
    {
        options ??= new ProgressionOptions();

        var progression = new List<ProgressionChord>();
        var currentDegree = options.StartDegree ?? 1;

        // Reserve last 2-3 chords for cadence
        var cadenceLength = options.EndingCadence.HasValue ? 2 : 0;
        var bodyLength = length - cadenceLength;

        // Generate body of progression
        for (var i = 0; i < bodyLength; i++)
        {
            var chord = CreateChord(scale, currentDegree, options);
            progression.Add(chord);
            currentDegree = SelectNextDegree(currentDegree, scale);
        }

        // Add cadence
        if (options.EndingCadence.HasValue)
        {
            AddCadence(progression, scale, options.EndingCadence.Value, options);
        }

        return progression;
    }

    /// <summary>
    /// Generates a progression following a specific pattern.
    /// </summary>
    /// <param name="scale">The scale/key.</param>
    /// <param name="pattern">Roman numeral pattern (e.g., "I-IV-V-I").</param>
    /// <param name="options">Generation options.</param>
    public IReadOnlyList<ProgressionChord> GenerateFromPattern(Scale scale, string pattern, ProgressionOptions? options = null)
    {
        options ??= new ProgressionOptions();
        var degrees = ParsePattern(pattern);

        return degrees.Select(d => CreateChord(scale, d, options)).ToList();
    }

    private ProgressionChord CreateChord(Scale scale, int degree, ProgressionOptions options)
    {
        var chord = Chord.FromScaleDegree(scale, degree, options.UseSevenths);
        var romanNumeral = GetRomanNumeral(degree, chord.Quality);

        return new ProgressionChord(chord, romanNumeral, degree, options.BeatsPerChord);
    }

    private int SelectNextDegree(int currentDegree, Scale scale)
    {
        if (!MajorTransitions.TryGetValue(currentDegree, out var transitions))
        {
            return 1; // Default to tonic
        }

        var target = _random.NextDouble();
        var cumulative = 0.0;

        foreach (var (degree, weight) in transitions)
        {
            cumulative += weight;
            if (cumulative >= target)
            {
                return degree;
            }
        }

        return transitions[^1].degree;
    }

    private void AddCadence(List<ProgressionChord> progression, Scale scale, CadenceType type, ProgressionOptions options)
    {
        var cadenceDegrees = type switch
        {
            CadenceType.AuthenticPerfect or CadenceType.AuthenticImperfect => new[] { 5, 1 },
            CadenceType.Half => new[] { 5 },
            CadenceType.Plagal => new[] { 4, 1 },
            CadenceType.Deceptive => new[] { 5, 6 },
            CadenceType.LeadingTone => new[] { 7, 1 },
            _ => new[] { 5, 1 }
        };

        foreach (var degree in cadenceDegrees)
        {
            progression.Add(CreateChord(scale, degree, options));
        }
    }

    private static IReadOnlyList<int> ParsePattern(string pattern)
    {
        var parts = pattern.Split('-', ',', ' ').Where(s => !string.IsNullOrWhiteSpace(s));
        var degrees = new List<int>();

        foreach (var part in parts)
        {
            var degree = ParseRomanNumeral(part.Trim());
            degrees.Add(degree);
        }

        return degrees;
    }

    private static int ParseRomanNumeral(string numeral)
    {
        var upper = numeral.ToUpperInvariant().Replace("°", "").Replace("O", "");

        return upper switch
        {
            "I" => 1,
            "II" => 2,
            "III" => 3,
            "IV" => 4,
            "V" => 5,
            "VI" => 6,
            "VII" => 7,
            _ => throw new ArgumentException($"Unknown roman numeral: {numeral}")
        };
    }

    private static string GetRomanNumeral(int degree, ChordQuality quality)
    {
        var numeral = degree switch
        {
            1 => "I",
            2 => "ii",
            3 => "iii",
            4 => "IV",
            5 => "V",
            6 => "vi",
            7 => "vii",
            _ => "?"
        };

        // Adjust case based on quality
        if (quality is ChordQuality.Major or ChordQuality.Dominant7 or ChordQuality.Major7)
        {
            numeral = numeral.ToUpperInvariant();
        }
        else
        {
            numeral = numeral.ToLowerInvariant();
        }

        // Add quality suffix
        numeral += quality switch
        {
            ChordQuality.Diminished => "°",
            ChordQuality.Augmented => "+",
            ChordQuality.Dominant7 => "7",
            ChordQuality.Major7 => "maj7",
            ChordQuality.Minor7 => "7",
            ChordQuality.HalfDiminished7 => "ø7",
            ChordQuality.Diminished7 => "°7",
            _ => ""
        };

        return numeral;
    }

    /// <summary>
    /// Common progression patterns.
    /// </summary>
    public static class CommonPatterns
    {
        /// <summary>I-IV-V-I (basic)</summary>
        public const string OneFourFiveOne = "I-IV-V-I";

        /// <summary>I-V-vi-IV (pop progression)</summary>
        public const string PopProgression = "I-V-vi-IV";

        /// <summary>ii-V-I (jazz turnaround)</summary>
        public const string TwoFiveOne = "ii-V-I";

        /// <summary>I-vi-IV-V (50s progression)</summary>
        public const string FiftiesProgression = "I-vi-IV-V";

        /// <summary>vi-IV-I-V (sensitive)</summary>
        public const string SensitiveProgression = "vi-IV-I-V";

        /// <summary>I-IV-vi-V</summary>
        public const string Axis = "I-IV-vi-V";

        /// <summary>i-VII-VI-VII (Andalusian)</summary>
        public const string Andalusian = "i-VII-VI-VII";

        /// <summary>I-V-vi-iii-IV-I-IV-V (Canon)</summary>
        public const string Canon = "I-V-vi-iii-IV-I-IV-V";
    }
}
