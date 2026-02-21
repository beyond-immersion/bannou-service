// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory;
using BeyondImmersion.Bannou.StorylineTheory.Arcs;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Loads and provides access to story templates from story-templates.yaml.
/// </summary>
public static class TemplateRegistry
{
    private static readonly Lazy<Dictionary<ArcType, StoryTemplate>> Templates = new(BuildTemplates);

    /// <summary>
    /// Gets a template by arc type.
    /// </summary>
    public static StoryTemplate Get(ArcType type) => Templates.Value[type];

    /// <summary>
    /// Gets all templates.
    /// </summary>
    public static IReadOnlyCollection<StoryTemplate> All => Templates.Value.Values;

    /// <summary>
    /// Gets templates compatible with a genre and optional subgenre.
    /// </summary>
    public static IEnumerable<StoryTemplate> GetCompatible(string genre, string? subgenre)
    {
        return All.Where(t => t.IsCompatibleWith(genre, subgenre));
    }

    private static Dictionary<ArcType, StoryTemplate> BuildTemplates()
    {
        var data = YamlLoader.Load<StoryTemplatesData>("story-templates.yaml");
        var templates = new Dictionary<ArcType, StoryTemplate>();

        if (data.Templates == null)
        {
            return templates;
        }

        foreach (var kvp in data.Templates)
        {
            var yaml = kvp.Value;
            var arcType = ParseArcType(yaml.Code);

            var phases = new List<StoryPhase>();
            if (yaml.Phases != null)
            {
                var phaseNumber = 1;
                foreach (var phaseKvp in yaml.Phases)
                {
                    var phaseYaml = phaseKvp.Value;
                    var isLast = phaseNumber == yaml.Phases.Count;

                    phases.Add(new StoryPhase
                    {
                        PhaseNumber = phaseNumber,
                        Name = phaseYaml.Name ?? phaseKvp.Key,
                        Position = new PhasePosition
                        {
                            StcCenter = phaseYaml.Position?.StcCenter ?? 0,
                            Floor = phaseYaml.Position?.Floor ?? 0,
                            Ceiling = phaseYaml.Position?.Ceiling ?? 1,
                            ValidationBand = phaseYaml.Position?.ValidationBand ?? 0.05
                        },
                        TargetState = new PhaseTargetState
                        {
                            MinPrimarySpectrum = phaseYaml.TargetState?.PrimarySpectrum?.Length > 0
                                ? phaseYaml.TargetState.PrimarySpectrum[0]
                                : 0,
                            MaxPrimarySpectrum = phaseYaml.TargetState?.PrimarySpectrum?.Length > 1
                                ? phaseYaml.TargetState.PrimarySpectrum[1]
                                : 1,
                            RangeDescription = phaseYaml.TargetState?.RangeDescription
                        },
                        Transition = new PhaseTransition
                        {
                            PositionFloor = phaseYaml.Transition?.PositionFloor ?? 0,
                            PositionCeiling = phaseYaml.Transition?.PositionCeiling ?? 1,
                            PrimarySpectrumMin = phaseYaml.Transition?.StateRequirements?.PrimarySpectrumMin,
                            PrimarySpectrumMax = phaseYaml.Transition?.StateRequirements?.PrimarySpectrumMax
                        },
                        StcBeatsCovered = phaseYaml.StcBeatsCovered?.ToArray() ?? Array.Empty<string>(),
                        IsTerminal = isLast
                    });

                    phaseNumber++;
                }
            }

            var compatibleGenres = new List<TemplateGenreCompatibility>();
            if (yaml.CompatibleGenres != null)
            {
                foreach (var genreKvp in yaml.CompatibleGenres)
                {
                    var genre = genreKvp.Key;
                    var subgenres = genreKvp.Value switch
                    {
                        bool b when b => null, // true means all subgenres
                        List<object> list => list.Select(o => o.ToString() ?? "").ToArray(),
                        _ => null
                    };

                    compatibleGenres.Add(new TemplateGenreCompatibility
                    {
                        Genre = genre,
                        Subgenres = subgenres
                    });
                }
            }

            templates[arcType] = new StoryTemplate
            {
                ArcType = arcType,
                Code = yaml.Code ?? kvp.Key.ToUpperInvariant().Replace(" ", "_"),
                Direction = ParseDirection(yaml.ArcDirection),
                MathematicalForm = yaml.MathematicalForm ?? "",
                Phases = phases.ToArray(),
                CompatibleGenres = compatibleGenres.ToArray(),
                DefaultActionCount = yaml.DefaultActionCount ?? 50,
                ActionCountRange = (yaml.ActionCountRange?.Min ?? 30, yaml.ActionCountRange?.Max ?? 100)
            };
        }

        return templates;
    }

    private static ArcType ParseArcType(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return ArcType.ManInHole;
        }

        var normalized = code.ToUpperInvariant().Replace("_", "").Replace(" ", "");
        return normalized switch
        {
            "RAGSTORICHIES" or "RAGSTORICHES" => ArcType.RagsToRiches,
            "TRAGEDY" => ArcType.Tragedy,
            "MANINHOLE" or "MANINAHOLE" => ArcType.ManInHole,
            "ICARUS" => ArcType.Icarus,
            "CINDERELLA" => ArcType.Cinderella,
            "OEDIPUS" => ArcType.Oedipus,
            _ => ArcType.ManInHole
        };
    }

    private static ArcDirection ParseDirection(string? direction)
    {
        if (string.IsNullOrEmpty(direction))
        {
            return ArcDirection.Positive;
        }

        return direction.ToLowerInvariant() switch
        {
            "positive" => ArcDirection.Positive,
            "negative" => ArcDirection.Negative,
            _ => ArcDirection.Positive
        };
    }

    #region YAML Data Classes

    internal sealed class StoryTemplatesData
    {
        public string? Version { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, TemplateYaml>? Templates { get; set; }
    }

    internal sealed class TemplateYaml
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? ArcPattern { get; set; }
        public string? ArcDirection { get; set; }
        public string? Description { get; set; }
        public string? MathematicalForm { get; set; }
        public Dictionary<string, object>? CompatibleGenres { get; set; }
        public Dictionary<string, PhaseYaml>? Phases { get; set; }
        public int? DefaultActionCount { get; set; }
        public ActionCountRangeYaml? ActionCountRange { get; set; }
    }

    internal sealed class PhaseYaml
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public PositionYaml? Position { get; set; }
        public TargetStateYaml? TargetState { get; set; }
        public List<string>? StcBeatsCovered { get; set; }
        public int? SceneCapacity { get; set; }
        public TransitionYaml? Transition { get; set; }
    }

    internal sealed class PositionYaml
    {
        public double StcCenter { get; set; }
        public double Floor { get; set; }
        public double Ceiling { get; set; }
        public double ValidationBand { get; set; }
    }

    internal sealed class TargetStateYaml
    {
        public double[]? PrimarySpectrum { get; set; }
        public string? RangeDescription { get; set; }
    }

    internal sealed class TransitionYaml
    {
        public double PositionFloor { get; set; }
        public double PositionCeiling { get; set; }
        public StateRequirementsYaml? StateRequirements { get; set; }
    }

    internal sealed class StateRequirementsYaml
    {
        public double? PrimarySpectrumMin { get; set; }
        public double? PrimarySpectrumMax { get; set; }
        public bool? CoreEventCompleted { get; set; }
    }

    internal sealed class ActionCountRangeYaml
    {
        public int Min { get; set; }
        public int Max { get; set; }
    }

    #endregion
}
