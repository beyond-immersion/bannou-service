// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Spectrums;

/// <summary>
/// Loads spectrum definitions and genre mappings from narrative-state.yaml.
/// </summary>
public static class NarrativeStateLoader
{
    private static readonly Lazy<NarrativeStateData> Data = new(() =>
        YamlLoader.Load<NarrativeStateData>("narrative-state.yaml"));

    private static readonly Lazy<IReadOnlyList<SpectrumDefinition>> SpectrumsList = new(BuildSpectrums);
    private static readonly Lazy<IReadOnlyDictionary<SpectrumType, SpectrumDefinition>> SpectrumsByType = new(() =>
        SpectrumsList.Value.ToDictionary(s => s.Type));
    private static readonly Lazy<IReadOnlyList<GenreSpectrumMapping>> GenreMappingsList = new(BuildGenreMappings);
    private static readonly Lazy<IReadOnlyDictionary<string, GenreSpectrumMapping>> GenreMappingsByGenre = new(() =>
        GenreMappingsList.Value.ToDictionary(g => g.Genre, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Gets all spectrum definitions.
    /// </summary>
    public static IReadOnlyList<SpectrumDefinition> Spectrums => SpectrumsList.Value;

    /// <summary>
    /// Gets all genre-to-spectrum mappings.
    /// </summary>
    public static IReadOnlyList<GenreSpectrumMapping> GenreMappings => GenreMappingsList.Value;

    /// <summary>
    /// Gets a spectrum definition by type.
    /// </summary>
    public static SpectrumDefinition GetSpectrum(SpectrumType type)
    {
        return SpectrumsByType.Value[type];
    }

    /// <summary>
    /// Gets the primary spectrum for a genre.
    /// </summary>
    public static SpectrumType GetPrimarySpectrum(string genre)
    {
        return GenreMappingsByGenre.Value[genre].PrimarySpectrum;
    }

    private static IReadOnlyList<SpectrumDefinition> BuildSpectrums()
    {
        var data = Data.Value;
        var spectrums = new List<SpectrumDefinition>();

        foreach (var kvp in data.Spectrums)
        {
            var yaml = kvp.Value;
            var type = ParseSpectrumType(yaml.Code);

            var stages = new List<SpectrumPole>();
            if (yaml.Poles != null)
            {
                if (yaml.Poles.Positive != null)
                {
                    stages.Add(new SpectrumPole
                    {
                        Label = yaml.Poles.Positive.Label ?? "Positive",
                        Value = yaml.Poles.Positive.Value,
                        Description = yaml.Poles.Positive.Description ?? ""
                    });
                }
                if (yaml.Poles.Contrary != null)
                {
                    stages.Add(new SpectrumPole
                    {
                        Label = yaml.Poles.Contrary.Label ?? "Contrary",
                        Value = yaml.Poles.Contrary.Value,
                        Description = yaml.Poles.Contrary.Description ?? ""
                    });
                }
                if (yaml.Poles.Negative != null)
                {
                    stages.Add(new SpectrumPole
                    {
                        Label = yaml.Poles.Negative.Label ?? "Negative",
                        Value = yaml.Poles.Negative.Value,
                        Description = yaml.Poles.Negative.Description ?? ""
                    });
                }
                if (yaml.Poles.NegationOfNegation != null)
                {
                    stages.Add(new SpectrumPole
                    {
                        Label = yaml.Poles.NegationOfNegation.Label ?? "Negation",
                        Value = yaml.Poles.NegationOfNegation.Value,
                        Description = yaml.Poles.NegationOfNegation.Description ?? ""
                    });
                }
            }

            spectrums.Add(new SpectrumDefinition
            {
                Type = type,
                PositiveLabel = yaml.Poles?.Positive?.Label ?? yaml.Name.Split(" vs ")[0],
                NegativeLabel = yaml.Poles?.Negative?.Label ?? yaml.Name.Split(" vs ").LastOrDefault() ?? "",
                Stages = stages.ToArray(),
                MediaExamples = Array.Empty<string>()
            });
        }

        return spectrums;
    }

    private static IReadOnlyList<GenreSpectrumMapping> BuildGenreMappings()
    {
        var data = Data.Value;
        var mappings = new List<GenreSpectrumMapping>();

        if (data.GenreSpectrumMapping == null)
        {
            return mappings;
        }

        foreach (var kvp in data.GenreSpectrumMapping)
        {
            var genre = kvp.Key;
            var yaml = kvp.Value;

            mappings.Add(new GenreSpectrumMapping
            {
                Genre = genre,
                Subgenre = null,
                PrimarySpectrum = ParseSpectrumType(yaml.Primary.ToUpperInvariant().Replace("_", "")),
                SecondarySpectrum = null
            });
        }

        return mappings;
    }

    private static SpectrumType ParseSpectrumType(string code)
    {
        var normalized = code.ToUpperInvariant().Replace("_", "");
        return normalized switch
        {
            "LIFEDEATH" => SpectrumType.LifeDeath,
            "HONORDISHONOR" => SpectrumType.HonorDishonor,
            "JUSTICEINJUSTICE" => SpectrumType.JusticeInjustice,
            "FREEDOMSUBJUGATION" => SpectrumType.FreedomSubjugation,
            "LOVEHATE" => SpectrumType.LoveHate,
            "RESPECTSHAME" => SpectrumType.RespectShame,
            "POWERIMPOTENCE" => SpectrumType.PowerImpotence,
            "SUCCESSFAILURE" => SpectrumType.SuccessFailure,
            "ALTRUISMSELFISHNESS" => SpectrumType.AltruismSelfishness,
            "WISDOMIGNORANCE" => SpectrumType.WisdomIgnorance,
            _ => throw new ArgumentException($"Unknown spectrum code: {code}")
        };
    }

    #region YAML Data Classes

    internal sealed class NarrativeStateData
    {
        public string? Version { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, SpectrumYaml> Spectrums { get; set; } = new();
        public Dictionary<string, GenreMappingYaml>? GenreSpectrumMapping { get; set; }
    }

    internal sealed class SpectrumYaml
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public PolesYaml? Poles { get; set; }
    }

    internal sealed class PolesYaml
    {
        public PoleYaml? Positive { get; set; }
        public PoleYaml? Contrary { get; set; }
        public PoleYaml? Negative { get; set; }
        public PoleYaml? NegationOfNegation { get; set; }
    }

    internal sealed class PoleYaml
    {
        public double Value { get; set; }
        public string? Label { get; set; }
        public string? Description { get; set; }
    }

    internal sealed class GenreMappingYaml
    {
        public string Primary { get; set; } = "";
        public string? CoreEvent { get; set; }
    }

    #endregion
}
