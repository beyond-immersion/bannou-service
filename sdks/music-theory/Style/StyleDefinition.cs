using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Harmony;
using BeyondImmersion.Bannou.MusicTheory.Melody;
using BeyondImmersion.Bannou.MusicTheory.Structure;
using BeyondImmersion.Bannou.MusicTheory.Time;

namespace BeyondImmersion.Bannou.MusicTheory.Style;

/// <summary>
/// Mode probability distribution for a style.
/// </summary>
public sealed class ModeDistribution
{
    private readonly Dictionary<ModeType, double> _weights = new();
    private double _totalWeight;

    /// <summary>
    /// Gets or sets the weight for a mode.
    /// </summary>
    public double this[ModeType mode]
    {
        get => _weights.GetValueOrDefault(mode, 0.0);
        set
        {
            _weights[mode] = value;
            _totalWeight = _weights.Values.Sum();
        }
    }

    /// <summary>
    /// Major mode weight.
    /// </summary>
    public double Major { get => this[ModeType.Major]; set => this[ModeType.Major] = value; }

    /// <summary>
    /// Minor mode weight.
    /// </summary>
    public double Minor { get => this[ModeType.Minor]; set => this[ModeType.Minor] = value; }

    /// <summary>
    /// Dorian mode weight.
    /// </summary>
    public double Dorian { get => this[ModeType.Dorian]; set => this[ModeType.Dorian] = value; }

    /// <summary>
    /// Mixolydian mode weight.
    /// </summary>
    public double Mixolydian { get => this[ModeType.Mixolydian]; set => this[ModeType.Mixolydian] = value; }

    /// <summary>
    /// Phrygian mode weight.
    /// </summary>
    public double Phrygian { get => this[ModeType.Phrygian]; set => this[ModeType.Phrygian] = value; }

    /// <summary>
    /// Lydian mode weight.
    /// </summary>
    public double Lydian { get => this[ModeType.Lydian]; set => this[ModeType.Lydian] = value; }

    /// <summary>
    /// Selects a mode based on the distribution.
    /// </summary>
    public ModeType Select(Random random)
    {
        if (_totalWeight <= 0)
        {
            return ModeType.Major;
        }

        var target = random.NextDouble() * _totalWeight;
        var cumulative = 0.0;

        foreach (var (mode, weight) in _weights)
        {
            cumulative += weight;
            if (cumulative >= target)
            {
                return mode;
            }
        }

        return ModeType.Major;
    }
}

/// <summary>
/// Tune type definition for a style.
/// </summary>
public sealed class TuneTypeDefinition
{
    /// <summary>
    /// Tune type name (e.g., "reel", "jig").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Time signature.
    /// </summary>
    public Meter Meter { get; set; } = Meter.Common.CommonTime;

    /// <summary>
    /// Typical tempo range (min-max BPM).
    /// </summary>
    public (int min, int max) TempoRange { get; set; } = (100, 140);

    /// <summary>
    /// Default form for this tune type.
    /// </summary>
    public string DefaultForm { get; set; } = "AABB";

    /// <summary>
    /// Rhythm pattern names to use.
    /// </summary>
    public List<string> RhythmPatterns { get; set; } = [];
}

/// <summary>
/// Harmony style preferences.
/// </summary>
public sealed class HarmonyStyleDefinition
{
    /// <summary>
    /// Primary cadence type.
    /// </summary>
    public CadenceType PrimaryCadence { get; set; } = CadenceType.AuthenticPerfect;

    /// <summary>
    /// Probability of pre-dominant before dominant.
    /// </summary>
    public double DominantPrepProbability { get; set; } = 0.6;

    /// <summary>
    /// Probability of secondary dominants.
    /// </summary>
    public double SecondaryDominantProbability { get; set; } = 0.3;

    /// <summary>
    /// Probability of modal interchange (borrowed chords).
    /// </summary>
    public double ModalInterchangeProbability { get; set; } = 0.1;

    /// <summary>
    /// Common chord progressions (roman numeral strings).
    /// </summary>
    public List<string> CommonProgressions { get; set; } = [];
}

/// <summary>
/// Complete style definition for music generation.
/// </summary>
public sealed class StyleDefinition
{
    /// <summary>
    /// Unique style identifier.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Human-readable name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Style category (e.g., "folk", "classical", "jazz").
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Description of the style.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Mode probability distribution.
    /// </summary>
    public ModeDistribution ModeDistribution { get; set; } = new();

    /// <summary>
    /// Interval preferences for melody.
    /// </summary>
    public IntervalPreferences IntervalPreferences { get; set; } = IntervalPreferences.Default;

    /// <summary>
    /// Available form templates.
    /// </summary>
    public List<Form> FormTemplates { get; set; } = [];

    /// <summary>
    /// Rhythm patterns.
    /// </summary>
    public List<RhythmPattern> RhythmPatterns { get; set; } = [];

    /// <summary>
    /// Tune types (style-specific forms like "reel", "jig").
    /// </summary>
    public List<TuneTypeDefinition> TuneTypes { get; set; } = [];

    /// <summary>
    /// Default tempo in BPM.
    /// </summary>
    public int DefaultTempo { get; set; } = 120;

    /// <summary>
    /// Harmony style preferences.
    /// </summary>
    public HarmonyStyleDefinition? HarmonyStyle { get; set; }

    /// <summary>
    /// Default time signature.
    /// </summary>
    public Meter DefaultMeter { get; set; } = Meter.Common.CommonTime;

    /// <summary>
    /// Gets a tune type by name.
    /// </summary>
    public TuneTypeDefinition? GetTuneType(string name)
    {
        return TuneTypes.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the default form.
    /// </summary>
    public Form GetDefaultForm()
    {
        return FormTemplates.FirstOrDefault() ?? Form.Common.AABB;
    }

    /// <summary>
    /// Selects a random mode based on the distribution.
    /// </summary>
    public ModeType SelectMode(Random random)
    {
        return ModeDistribution.Select(random);
    }
}

/// <summary>
/// Built-in style definitions.
/// </summary>
public static class BuiltInStyles
{
    /// <summary>
    /// Celtic/Irish traditional style.
    /// </summary>
    public static StyleDefinition Celtic => new()
    {
        Id = "celtic",
        Name = "Celtic",
        Category = "folk",
        Description = "Traditional Irish and Scottish instrumental music",
        ModeDistribution = new ModeDistribution
        {
            Major = 0.64,
            Minor = 0.16,
            Dorian = 0.14,
            Mixolydian = 0.06
        },
        IntervalPreferences = IntervalPreferences.Celtic,
        DefaultTempo = 120,
        DefaultMeter = Meter.Common.CommonTime,
        FormTemplates = [Form.Common.AABB],
        TuneTypes =
        [
            new TuneTypeDefinition
            {
                Name = "reel",
                Meter = new Meter(4, 4),
                TempoRange = (100, 140),
                DefaultForm = "AABB",
                RhythmPatterns = ["reel-basic", "reel-ornament"]
            },
            new TuneTypeDefinition
            {
                Name = "jig",
                Meter = new Meter(6, 8),
                TempoRange = (100, 130),
                DefaultForm = "AABB",
                RhythmPatterns = ["jig-basic"]
            },
            new TuneTypeDefinition
            {
                Name = "polka",
                Meter = new Meter(2, 4),
                TempoRange = (110, 150),
                DefaultForm = "AABB",
                RhythmPatterns = ["polka"]
            },
            new TuneTypeDefinition
            {
                Name = "hornpipe",
                Meter = new Meter(4, 4),
                TempoRange = (80, 100),
                DefaultForm = "AABB",
                RhythmPatterns = ["hornpipe"]
            }
        ],
        RhythmPatterns =
        [
            RhythmPattern.Celtic.ReelBasic,
            RhythmPattern.Celtic.ReelOrnament,
            RhythmPattern.Celtic.JigBasic,
            RhythmPattern.Celtic.Polka,
            RhythmPattern.Celtic.Hornpipe
        ],
        HarmonyStyle = new HarmonyStyleDefinition
        {
            PrimaryCadence = CadenceType.AuthenticPerfect,
            DominantPrepProbability = 0.4,
            SecondaryDominantProbability = 0.1,
            ModalInterchangeProbability = 0.05,
            CommonProgressions = ["I-IV-V-I", "I-V-vi-IV", "i-VII-VI-VII"]
        }
    };

    /// <summary>
    /// Baroque classical style.
    /// </summary>
    public static StyleDefinition Baroque => new()
    {
        Id = "baroque",
        Name = "Baroque",
        Category = "classical",
        Description = "European classical music from 1600-1750",
        ModeDistribution = new ModeDistribution
        {
            Major = 0.6,
            Minor = 0.4
        },
        IntervalPreferences = IntervalPreferences.Baroque,
        DefaultTempo = 80,
        DefaultMeter = Meter.Common.CommonTime,
        FormTemplates = [Form.Common.AABA],
        HarmonyStyle = new HarmonyStyleDefinition
        {
            PrimaryCadence = CadenceType.AuthenticPerfect,
            DominantPrepProbability = 0.7,
            SecondaryDominantProbability = 0.4,
            ModalInterchangeProbability = 0.1,
            CommonProgressions = ["I-IV-V-I", "I-ii-V-I", "I-vi-IV-V"]
        }
    };

    /// <summary>
    /// Jazz style.
    /// </summary>
    public static StyleDefinition Jazz => new()
    {
        Id = "jazz",
        Name = "Jazz",
        Category = "jazz",
        Description = "American jazz tradition",
        ModeDistribution = new ModeDistribution
        {
            Major = 0.4,
            Minor = 0.3,
            Dorian = 0.2,
            Mixolydian = 0.1
        },
        IntervalPreferences = IntervalPreferences.Jazz,
        DefaultTempo = 140,
        DefaultMeter = Meter.Common.CommonTime,
        FormTemplates = [Form.Common.AABA],
        HarmonyStyle = new HarmonyStyleDefinition
        {
            PrimaryCadence = CadenceType.AuthenticPerfect,
            DominantPrepProbability = 0.8,
            SecondaryDominantProbability = 0.6,
            ModalInterchangeProbability = 0.3,
            CommonProgressions = ["ii-V-I", "I-VI-ii-V", "iii-vi-ii-V"]
        }
    };

    /// <summary>
    /// Gets a built-in style by ID.
    /// </summary>
    public static StyleDefinition? GetById(string id)
    {
        return id.ToLowerInvariant() switch
        {
            "celtic" => Celtic,
            "baroque" => Baroque,
            "jazz" => Jazz,
            _ => null
        };
    }

    /// <summary>
    /// Gets all built-in styles.
    /// </summary>
    public static IEnumerable<StyleDefinition> All
    {
        get
        {
            yield return Celtic;
            yield return Baroque;
            yield return Jazz;
        }
    }
}
