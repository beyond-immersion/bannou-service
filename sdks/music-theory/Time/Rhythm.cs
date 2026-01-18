using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.MusicTheory.Time;

/// <summary>
/// Represents a rhythmic pattern as a sequence of relative durations.
/// </summary>
public sealed class RhythmPattern
{
    /// <summary>
    /// Name of the pattern.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; }

    /// <summary>
    /// Relative durations (1.0 = one beat).
    /// </summary>
    [JsonPropertyName("pattern")]
    public IReadOnlyList<double> Pattern { get; }

    /// <summary>
    /// Total duration in beats.
    /// </summary>
    [JsonPropertyName("totalBeats")]
    public double TotalBeats { get; }

    /// <summary>
    /// Selection weight for random generation.
    /// </summary>
    [JsonPropertyName("weight")]
    public double Weight { get; }

    /// <summary>
    /// Creates a rhythm pattern.
    /// </summary>
    /// <param name="name">Pattern name.</param>
    /// <param name="pattern">Relative durations.</param>
    /// <param name="weight">Selection weight (default 1.0).</param>
    [JsonConstructor]
    public RhythmPattern(string name, IReadOnlyList<double> pattern, double weight = 1.0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Pattern name cannot be empty", nameof(name));
        }

        if (pattern.Count == 0)
        {
            throw new ArgumentException("Pattern must have at least one duration", nameof(pattern));
        }

        Name = name;
        Pattern = pattern;
        Weight = weight;
        TotalBeats = pattern.Sum();
    }

    /// <summary>
    /// Converts the pattern to ticks given ticks per beat.
    /// </summary>
    /// <param name="ticksPerBeat">Ticks per quarter note.</param>
    /// <returns>Duration values in ticks.</returns>
    public IEnumerable<int> ToTicks(int ticksPerBeat)
    {
        return Pattern.Select(d => (int)Math.Round(d * ticksPerBeat));
    }

    /// <summary>
    /// Scales the pattern to fit a target duration.
    /// </summary>
    /// <param name="targetBeats">Target total duration in beats.</param>
    /// <returns>Scaled pattern.</returns>
    public RhythmPattern ScaleTo(double targetBeats)
    {
        var scale = targetBeats / TotalBeats;
        var scaled = Pattern.Select(d => d * scale).ToList();
        return new RhythmPattern(Name, scaled, Weight);
    }

    /// <summary>
    /// Repeats the pattern a number of times.
    /// </summary>
    public RhythmPattern Repeat(int times)
    {
        var repeated = new List<double>();
        for (var i = 0; i < times; i++)
        {
            repeated.AddRange(Pattern);
        }

        return new RhythmPattern($"{Name}x{times}", repeated, Weight);
    }

    /// <summary>
    /// Common rhythm patterns.
    /// </summary>
    public static class Common
    {
        /// <summary>Even quarter notes</summary>
        public static RhythmPattern EvenQuarters => new("even-quarters", [1.0, 1.0, 1.0, 1.0]);

        /// <summary>Even eighth notes</summary>
        public static RhythmPattern EvenEighths => new("even-eighths", [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]);

        /// <summary>Dotted quarter + eighth (common in 6/8)</summary>
        public static RhythmPattern DottedQuarterEighth => new("dotted-quarter-eighth", [1.5, 0.5]);

        /// <summary>Eighth-quarter-eighth (syncopa)</summary>
        public static RhythmPattern Syncopa => new("syncopa", [0.5, 1.0, 0.5]);

        /// <summary>Long-short-short (anapest)</summary>
        public static RhythmPattern Anapest => new("anapest", [1.0, 0.5, 0.5]);

        /// <summary>Short-short-long (dactyl)</summary>
        public static RhythmPattern Dactyl => new("dactyl", [0.5, 0.5, 1.0]);

        /// <summary>Triplet eighth notes</summary>
        public static RhythmPattern Triplet => new("triplet", [1.0 / 3, 1.0 / 3, 1.0 / 3]);

        /// <summary>Swing eighth notes</summary>
        public static RhythmPattern SwingEighths => new("swing-eighths", [2.0 / 3, 1.0 / 3]);
    }

    /// <summary>
    /// Celtic-style rhythm patterns.
    /// </summary>
    public static class Celtic
    {
        /// <summary>Reel pattern (even eighths in groups of 4)</summary>
        public static RhythmPattern ReelBasic => new("reel-basic", [0.5, 0.5, 0.5, 0.5], 1.0);

        /// <summary>Reel with ornament (short-long-short-short)</summary>
        public static RhythmPattern ReelOrnament => new("reel-ornament", [0.25, 0.75, 0.5, 0.5], 0.5);

        /// <summary>Jig pattern (dotted eighth + sixteenth + eighth)</summary>
        public static RhythmPattern JigBasic => new("jig-basic", [0.75, 0.25, 0.5], 1.0);

        /// <summary>Slip jig pattern (9/8 feel)</summary>
        public static RhythmPattern SlipJig => new("slip-jig", [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5], 0.8);

        /// <summary>Polka pattern (2/4 feel)</summary>
        public static RhythmPattern Polka => new("polka", [0.5, 0.5, 0.5, 0.5], 1.0);

        /// <summary>Hornpipe pattern (dotted rhythm)</summary>
        public static RhythmPattern Hornpipe => new("hornpipe", [0.75, 0.25, 0.75, 0.25], 0.7);
    }

    public override string ToString() => $"{Name}: [{string.Join(", ", Pattern.Select(d => d.ToString("F2")))}]";
}

/// <summary>
/// Manages a collection of rhythm patterns for a style.
/// </summary>
public sealed class RhythmPatternSet
{
    private readonly List<RhythmPattern> _patterns;
    private readonly double _totalWeight;
    private readonly Random _random;

    /// <summary>
    /// All patterns in the set.
    /// </summary>
    public IReadOnlyList<RhythmPattern> Patterns => _patterns;

    /// <summary>
    /// Creates a rhythm pattern set.
    /// </summary>
    /// <param name="patterns">The patterns to include.</param>
    /// <param name="seed">Optional random seed for reproducibility.</param>
    public RhythmPatternSet(IEnumerable<RhythmPattern> patterns, int? seed = null)
    {
        _patterns = patterns.ToList();
        _totalWeight = _patterns.Sum(p => p.Weight);
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Selects a random pattern weighted by the pattern weights.
    /// </summary>
    public RhythmPattern SelectWeighted()
    {
        if (_patterns.Count == 0)
        {
            throw new InvalidOperationException("No patterns in set");
        }

        var target = _random.NextDouble() * _totalWeight;
        var cumulative = 0.0;

        foreach (var pattern in _patterns)
        {
            cumulative += pattern.Weight;
            if (cumulative >= target)
            {
                return pattern;
            }
        }

        return _patterns[^1];
    }

    /// <summary>
    /// Gets all patterns that fit within a target duration.
    /// </summary>
    /// <param name="targetBeats">Maximum duration in beats.</param>
    public IEnumerable<RhythmPattern> GetFittingPatterns(double targetBeats)
    {
        return _patterns.Where(p => p.TotalBeats <= targetBeats);
    }
}
