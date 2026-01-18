using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Harmony;
using BeyondImmersion.Bannou.MusicTheory.Structure;
using BeyondImmersion.Bannou.MusicTheory.Time;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.Bannou.MusicTheory.Style;

/// <summary>
/// YAML representation of a style for loading.
/// </summary>
internal sealed class YamlStyleDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Description { get; set; }
    public YamlModeDistribution? ModeDistribution { get; set; }
    public YamlIntervalPreferences? IntervalPreferences { get; set; }
    public List<string>? FormTemplates { get; set; }
    public List<YamlTuneType>? TuneTypes { get; set; }
    public int? DefaultTempo { get; set; }
    public string? DefaultMeter { get; set; }
    public YamlHarmonyStyle? HarmonyStyle { get; set; }
}

internal sealed class YamlModeDistribution
{
    public double Major { get; set; }
    public double Minor { get; set; }
    public double Dorian { get; set; }
    public double Mixolydian { get; set; }
    public double Phrygian { get; set; }
    public double Lydian { get; set; }
}

internal sealed class YamlIntervalPreferences
{
    public double StepWeight { get; set; } = 0.5;
    public double ThirdWeight { get; set; } = 0.25;
    public double LeapWeight { get; set; } = 0.15;
    public double LargeLeapWeight { get; set; } = 0.1;
}

internal sealed class YamlTuneType
{
    public string Name { get; set; } = "";
    public string Meter { get; set; } = "4/4";
    public int? MinTempo { get; set; }
    public int? MaxTempo { get; set; }
    public string? DefaultForm { get; set; }
    public List<string>? RhythmPatterns { get; set; }
}

internal sealed class YamlHarmonyStyle
{
    public string? PrimaryCadence { get; set; }
    public double? DominantPrepProbability { get; set; }
    public double? SecondaryDominantProbability { get; set; }
    public double? ModalInterchangeProbability { get; set; }
    public List<string>? CommonProgressions { get; set; }
}

/// <summary>
/// Loads style definitions from YAML files.
/// </summary>
public sealed class StyleLoader
{
    private readonly IDeserializer _deserializer;

    /// <summary>
    /// Creates a style loader.
    /// </summary>
    public StyleLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Loads a style from a YAML string.
    /// </summary>
    /// <param name="yaml">The YAML content.</param>
    /// <returns>The loaded style definition.</returns>
    public StyleDefinition LoadFromYaml(string yaml)
    {
        var yamlDef = _deserializer.Deserialize<YamlStyleDefinition>(yaml);
        return ConvertToStyleDefinition(yamlDef);
    }

    /// <summary>
    /// Loads a style from a file.
    /// </summary>
    /// <param name="path">Path to the YAML file.</param>
    /// <returns>The loaded style definition.</returns>
    public StyleDefinition LoadFromFile(string path)
    {
        var yaml = File.ReadAllText(path);
        return LoadFromYaml(yaml);
    }

    /// <summary>
    /// Loads all styles from a directory.
    /// </summary>
    /// <param name="directory">Directory containing YAML files.</param>
    /// <param name="pattern">File pattern (default: *.yaml).</param>
    /// <returns>Loaded style definitions.</returns>
    public IEnumerable<StyleDefinition> LoadFromDirectory(string directory, string pattern = "*.yaml")
    {
        var files = Directory.GetFiles(directory, pattern);

        foreach (var file in files)
        {
            StyleDefinition? style = null;
            try
            {
                style = LoadFromFile(file);
            }
            catch
            {
                // Skip invalid files
            }

            if (style != null)
            {
                yield return style;
            }
        }
    }

    private StyleDefinition ConvertToStyleDefinition(YamlStyleDefinition yaml)
    {
        var style = new StyleDefinition
        {
            Id = yaml.Id,
            Name = yaml.Name,
            Category = yaml.Category,
            Description = yaml.Description,
            DefaultTempo = yaml.DefaultTempo ?? 120
        };

        // Mode distribution
        if (yaml.ModeDistribution != null)
        {
            style.ModeDistribution = new ModeDistribution
            {
                Major = yaml.ModeDistribution.Major,
                Minor = yaml.ModeDistribution.Minor,
                Dorian = yaml.ModeDistribution.Dorian,
                Mixolydian = yaml.ModeDistribution.Mixolydian,
                Phrygian = yaml.ModeDistribution.Phrygian,
                Lydian = yaml.ModeDistribution.Lydian
            };
        }

        // Interval preferences
        if (yaml.IntervalPreferences != null)
        {
            style.IntervalPreferences = new Melody.IntervalPreferences
            {
                StepWeight = yaml.IntervalPreferences.StepWeight,
                ThirdWeight = yaml.IntervalPreferences.ThirdWeight,
                LeapWeight = yaml.IntervalPreferences.LeapWeight,
                LargeLeapWeight = yaml.IntervalPreferences.LargeLeapWeight
            };
        }

        // Form templates
        if (yaml.FormTemplates != null)
        {
            foreach (var formPattern in yaml.FormTemplates)
            {
                style.FormTemplates.Add(Form.Parse(formPattern));
            }
        }

        // Tune types
        if (yaml.TuneTypes != null)
        {
            foreach (var tt in yaml.TuneTypes)
            {
                style.TuneTypes.Add(new TuneTypeDefinition
                {
                    Name = tt.Name,
                    Meter = Meter.Parse(tt.Meter),
                    TempoRange = (tt.MinTempo ?? 100, tt.MaxTempo ?? 140),
                    DefaultForm = tt.DefaultForm ?? "AABB",
                    RhythmPatterns = tt.RhythmPatterns ?? []
                });
            }
        }

        // Default meter
        if (!string.IsNullOrEmpty(yaml.DefaultMeter))
        {
            style.DefaultMeter = Meter.Parse(yaml.DefaultMeter);
        }

        // Harmony style
        if (yaml.HarmonyStyle != null)
        {
            style.HarmonyStyle = new HarmonyStyleDefinition
            {
                PrimaryCadence = ParseCadenceType(yaml.HarmonyStyle.PrimaryCadence),
                DominantPrepProbability = yaml.HarmonyStyle.DominantPrepProbability ?? 0.6,
                SecondaryDominantProbability = yaml.HarmonyStyle.SecondaryDominantProbability ?? 0.3,
                ModalInterchangeProbability = yaml.HarmonyStyle.ModalInterchangeProbability ?? 0.1,
                CommonProgressions = yaml.HarmonyStyle.CommonProgressions ?? []
            };
        }

        return style;
    }

    private static CadenceType ParseCadenceType(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return CadenceType.AuthenticPerfect;
        }

        return value.ToLowerInvariant() switch
        {
            "authentic" or "authenticperfect" or "pac" => CadenceType.AuthenticPerfect,
            "imperfect" or "authenticimperfect" or "iac" => CadenceType.AuthenticImperfect,
            "half" or "hc" => CadenceType.Half,
            "plagal" or "pc" => CadenceType.Plagal,
            "deceptive" or "dc" => CadenceType.Deceptive,
            "leadingtone" => CadenceType.LeadingTone,
            _ => CadenceType.AuthenticPerfect
        };
    }
}

/// <summary>
/// Serializes style definitions to YAML.
/// </summary>
public sealed class StyleSerializer
{
    private readonly ISerializer _serializer;

    /// <summary>
    /// Creates a style serializer.
    /// </summary>
    public StyleSerializer()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    /// <summary>
    /// Serializes a style to YAML.
    /// </summary>
    public string ToYaml(StyleDefinition style)
    {
        var yaml = new YamlStyleDefinition
        {
            Id = style.Id,
            Name = style.Name,
            Category = style.Category,
            Description = style.Description,
            DefaultTempo = style.DefaultTempo,
            DefaultMeter = style.DefaultMeter.ToString(),
            ModeDistribution = new YamlModeDistribution
            {
                Major = style.ModeDistribution.Major,
                Minor = style.ModeDistribution.Minor,
                Dorian = style.ModeDistribution.Dorian,
                Mixolydian = style.ModeDistribution.Mixolydian,
                Phrygian = style.ModeDistribution.Phrygian,
                Lydian = style.ModeDistribution.Lydian
            },
            IntervalPreferences = new YamlIntervalPreferences
            {
                StepWeight = style.IntervalPreferences.StepWeight,
                ThirdWeight = style.IntervalPreferences.ThirdWeight,
                LeapWeight = style.IntervalPreferences.LeapWeight,
                LargeLeapWeight = style.IntervalPreferences.LargeLeapWeight
            },
            FormTemplates = style.FormTemplates.Select(f => f.Name).ToList(),
            TuneTypes = style.TuneTypes.Select(t => new YamlTuneType
            {
                Name = t.Name,
                Meter = t.Meter.ToString(),
                MinTempo = t.TempoRange.min,
                MaxTempo = t.TempoRange.max,
                DefaultForm = t.DefaultForm,
                RhythmPatterns = t.RhythmPatterns
            }).ToList()
        };

        if (style.HarmonyStyle != null)
        {
            yaml.HarmonyStyle = new YamlHarmonyStyle
            {
                PrimaryCadence = style.HarmonyStyle.PrimaryCadence.ToString(),
                DominantPrepProbability = style.HarmonyStyle.DominantPrepProbability,
                SecondaryDominantProbability = style.HarmonyStyle.SecondaryDominantProbability,
                ModalInterchangeProbability = style.HarmonyStyle.ModalInterchangeProbability,
                CommonProgressions = style.HarmonyStyle.CommonProgressions
            };
        }

        return _serializer.Serialize(yaml);
    }
}
