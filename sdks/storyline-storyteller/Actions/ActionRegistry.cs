// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory;
using BeyondImmersion.Bannou.StorylineTheory.Planning;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Loads and provides access to story actions from story-actions.yaml.
/// </summary>
public static class ActionRegistry
{
    private static readonly Lazy<Dictionary<string, StoryAction>> Actions = new(BuildActions);

    /// <summary>
    /// Gets an action by ID.
    /// </summary>
    public static StoryAction Get(string id) => Actions.Value[id];

    /// <summary>
    /// Gets all actions.
    /// </summary>
    public static IReadOnlyCollection<StoryAction> All => Actions.Value.Values;

    /// <summary>
    /// Gets core events for a genre.
    /// </summary>
    public static IEnumerable<StoryAction> GetCoreEvents(string genre)
    {
        return All.Where(a => a.IsCoreEvent && a.ApplicableGenres.Contains(genre, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets actions applicable to the current world state and genre.
    /// </summary>
    public static IEnumerable<StoryAction> GetApplicable(WorldState state, string genre)
    {
        return All
            .Where(a => a.ApplicableGenres.Contains(genre, StringComparer.OrdinalIgnoreCase))
            .Where(a => a.CanExecute(state));
    }

    private static Dictionary<string, StoryAction> BuildActions()
    {
        var data = YamlLoader.Load<StoryActionsData>("story-actions.yaml");
        var actions = new Dictionary<string, StoryAction>(StringComparer.OrdinalIgnoreCase);

        if (data.Actions == null)
        {
            return actions;
        }

        foreach (var kvp in data.Actions)
        {
            var id = kvp.Key;
            var yaml = kvp.Value;

            var preconditions = new List<ActionPrecondition>();
            if (yaml.Preconditions != null)
            {
                foreach (var pre in yaml.Preconditions)
                {
                    preconditions.Add(new ActionPrecondition
                    {
                        Key = pre.Key,
                        Value = pre.Value,
                        Operator = ActionPreconditionOperator.Equals
                    });
                }
            }

            var effects = new List<ActionEffect>();
            if (yaml.Effects != null)
            {
                foreach (var eff in yaml.Effects)
                {
                    effects.Add(new ActionEffect
                    {
                        Key = eff.Key,
                        Value = eff.Value,
                        Cardinality = EffectCardinality.Exclusive
                    });
                }
            }

            var narrativeEffect = new NarrativeEffect
            {
                PrimarySpectrumDelta = yaml.NarrativeEffect?.PrimarySpectrum,
                SecondarySpectrumDelta = null,
                PositionAdvance = null
            };

            // Derive applicable genres from genre_labels or satisfies_obligatory
            var applicableGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (yaml.GenreLabels != null)
            {
                foreach (var genre in yaml.GenreLabels.Keys)
                {
                    applicableGenres.Add(genre);
                }
            }
            if (yaml.SatisfiesObligatory != null)
            {
                foreach (var genre in yaml.SatisfiesObligatory.Keys)
                {
                    applicableGenres.Add(genre);
                }
            }
            // If no genres specified, treat as universal
            if (applicableGenres.Count == 0)
            {
                applicableGenres.Add("universal");
            }

            var variants = new List<StoryActionVariant>();
            if (yaml.Variants != null)
            {
                foreach (var v in yaml.Variants)
                {
                    variants.Add(new StoryActionVariant
                    {
                        Genres = Array.Empty<string>(),
                        DescriptionOverride = v.Description,
                        NarrativeEffectOverride = null
                    });
                }
            }

            actions[id] = new StoryAction
            {
                Id = id,
                Category = ParseCategory(yaml.Category),
                Cost = yaml.Cost,
                IsCoreEvent = yaml.IsCoreEvent,
                ApplicableGenres = applicableGenres.ToArray(),
                Preconditions = preconditions.ToArray(),
                Effects = effects.ToArray(),
                NarrativeEffect = narrativeEffect,
                ChainedAction = yaml.ChainedAction,
                Description = yaml.Description,
                Variants = variants.Count > 0 ? variants.ToArray() : null
            };
        }

        return actions;
    }

    private static ActionCategory ParseCategory(string? category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return ActionCategory.Transformation;
        }

        return category.ToLowerInvariant() switch
        {
            "conflict" => ActionCategory.Conflict,
            "relationship" => ActionCategory.Relationship,
            "mystery" => ActionCategory.Mystery,
            "resolution" => ActionCategory.Resolution,
            "transformation" => ActionCategory.Transformation,
            _ => ActionCategory.Transformation
        };
    }

    #region YAML Data Classes

    internal sealed class StoryActionsData
    {
        public string? Version { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, ActionYaml>? Actions { get; set; }
    }

    internal sealed class ActionYaml
    {
        public string? Category { get; set; }
        public string? UniversalName { get; set; }
        public string? Description { get; set; }
        public double Cost { get; set; }
        public bool IsCoreEvent { get; set; }
        public Dictionary<string, object>? Preconditions { get; set; }
        public Dictionary<string, object>? Effects { get; set; }
        public NarrativeEffectYaml? NarrativeEffect { get; set; }
        public Dictionary<string, string>? GenreLabels { get; set; }
        public Dictionary<string, List<string>>? SatisfiesObligatory { get; set; }
        public List<string>? ProppInspiration { get; set; }
        public string? ChainedAction { get; set; }
        public List<VariantYaml>? Variants { get; set; }
    }

    internal sealed class NarrativeEffectYaml
    {
        public double? PrimarySpectrum { get; set; }
        public double? LifeDeath { get; set; }
    }

    internal sealed class VariantYaml
    {
        public string? Id { get; set; }
        public string? Description { get; set; }
        public double ProbabilityWeight { get; set; }
    }

    #endregion
}
