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
    /// <param name="type">The spectrum type.</param>
    /// <returns>The spectrum definition.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the spectrum type is not found.</exception>
    public static SpectrumDefinition GetSpectrum(SpectrumType type)
    {
        return SpectrumsByType.Value[type];
    }

    /// <summary>
    /// Gets the primary spectrum for a genre.
    /// </summary>
    /// <param name="genre">The genre code (e.g., "action", "crime").</param>
    /// <returns>The primary spectrum type for the genre.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the genre is not found.</exception>
    public static SpectrumType GetPrimarySpectrum(string genre)
    {
        return GenreMappingsByGenre.Value[genre].PrimarySpectrum;
    }

    /// <summary>
    /// Gets the genre mapping for a genre.
    /// </summary>
    /// <param name="genre">The genre code (e.g., "action", "crime").</param>
    /// <returns>The genre mapping, or null if not found.</returns>
    public static GenreSpectrumMapping? GetGenreMapping(string genre)
    {
        return GenreMappingsByGenre.Value.TryGetValue(genre, out var mapping) ? mapping : null;
    }

    /// <summary>
    /// Creates an initial NarrativeState for a genre with default values.
    /// </summary>
    /// <param name="genre">The genre code.</param>
    /// <param name="initialPrimaryValue">The initial value for the primary spectrum (default 0.5).</param>
    /// <returns>A new NarrativeState configured for the genre.</returns>
    public static NarrativeState CreateForGenre(string genre, double initialPrimaryValue = 0.5)
    {
        var primarySpectrum = GetPrimarySpectrum(genre);
        var state = new NarrativeState { PrimarySpectrum = primarySpectrum };
        state[primarySpectrum] = initialPrimaryValue;
        return state;
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
                        Description = yaml.Poles.Positive.Description ?? "",
                        Examples = yaml.Poles.Positive.Examples
                    });
                }
                if (yaml.Poles.Contrary != null)
                {
                    stages.Add(new SpectrumPole
                    {
                        Label = yaml.Poles.Contrary.Label ?? "Contrary",
                        Value = yaml.Poles.Contrary.Value,
                        Description = yaml.Poles.Contrary.Description ?? "",
                        Examples = yaml.Poles.Contrary.Examples
                    });
                }
                if (yaml.Poles.Negative != null)
                {
                    stages.Add(new SpectrumPole
                    {
                        Label = yaml.Poles.Negative.Label ?? "Negative",
                        Value = yaml.Poles.Negative.Value,
                        Description = yaml.Poles.Negative.Description ?? "",
                        Examples = yaml.Poles.Negative.Examples
                    });
                }
                if (yaml.Poles.NegationOfNegation != null)
                {
                    stages.Add(new SpectrumPole
                    {
                        Label = yaml.Poles.NegationOfNegation.Label ?? "Negation",
                        Value = yaml.Poles.NegationOfNegation.Value,
                        Description = yaml.Poles.NegationOfNegation.Description ?? "",
                        Examples = yaml.Poles.NegationOfNegation.Examples
                    });
                }
            }

            spectrums.Add(new SpectrumDefinition
            {
                Type = type,
                Code = yaml.Code,
                Name = yaml.Name,
                Domain = yaml.Domain ?? "unknown",
                MaslowLevel = yaml.MaslowLevel,
                PositiveLabel = yaml.Poles?.Positive?.Label ?? yaml.Name.Split(" vs ")[0],
                NegativeLabel = yaml.Poles?.Negative?.Label ?? yaml.Name.Split(" vs ").LastOrDefault() ?? "",
                Stages = stages.ToArray(),
                CoreNeed = yaml.CoreNeed ?? "",
                CoreEmotion = yaml.CoreEmotion ?? "",
                PrimaryGenres = yaml.Genres?.Primary ?? Array.Empty<string>(),
                DramaticQuestion = yaml.Question ?? ""
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

            // Look up override if present
            string? emotionOverride = null;
            if (data.GenreOverrides?.TryGetValue(genre, out var overrideData) == true)
            {
                emotionOverride = overrideData.CoreEmotion;
            }

            mappings.Add(new GenreSpectrumMapping
            {
                Genre = genre,
                PrimarySpectrum = ParseSpectrumType(yaml.Primary.ToUpperInvariant().Replace("_", "")),
                CoreEvent = yaml.CoreEvent ?? "",
                CoreEmotionOverride = emotionOverride
            });
        }

        return mappings;
    }

    private static SpectrumType ParseSpectrumType(string code)
    {
        // Handle both formats: "LIFE_DEATH" and "LIFEDEATH"
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

    // These classes match the YAML structure for deserialization
    internal sealed class NarrativeStateData
    {
        public string? Version { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, SpectrumYaml> Spectrums { get; set; } = new();
        public Dictionary<string, GenreMappingYaml>? GenreSpectrumMapping { get; set; }
        public Dictionary<string, GenreOverrideYaml>? GenreOverrides { get; set; }
    }

    internal sealed class SpectrumYaml
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Domain { get; set; }
        public int MaslowLevel { get; set; }
        public PolesYaml? Poles { get; set; }
        public string? CoreNeed { get; set; }
        public string? CoreEmotion { get; set; }
        public GenresYaml? Genres { get; set; }
        public string? Question { get; set; }
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
        public string[]? Examples { get; set; }
    }

    internal sealed class GenresYaml
    {
        public string[]? Primary { get; set; }
    }

    internal sealed class GenreMappingYaml
    {
        public string Primary { get; set; } = "";
        public string? CoreEvent { get; set; }
    }

    internal sealed class GenreOverrideYaml
    {
        public string? CoreEmotion { get; set; }
    }

    #endregion
}
