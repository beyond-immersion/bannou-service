// =============================================================================
// Cognition Template Registry
// Manages cognition pipeline templates from embedded resources and YAML files.
// =============================================================================

using System.Collections.Concurrent;
using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.Bannou.Behavior.Cognition;

/// <summary>
/// Registry for cognition templates.
/// </summary>
/// <remarks>
/// <para>
/// The registry loads templates from two sources:
/// </para>
/// <list type="bullet">
/// <item>Embedded defaults - Standard templates compiled into the assembly</item>
/// <item>YAML files - Runtime configuration from the file system</item>
/// </list>
/// <para>
/// File-loaded templates override embedded ones with the same ID.
/// </para>
/// </remarks>
public sealed class CognitionTemplateRegistry : ICognitionTemplateRegistry
{
    private readonly ConcurrentDictionary<string, CognitionTemplate> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CognitionTemplateRegistry>? _logger;
    private readonly IDeserializer _yamlDeserializer;

    /// <summary>
    /// Creates a new template registry with embedded defaults.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    /// <param name="loadEmbeddedDefaults">Whether to load embedded default templates.</param>
    public CognitionTemplateRegistry(
        ILogger<CognitionTemplateRegistry>? logger = null,
        bool loadEmbeddedDefaults = true)
    {
        _logger = logger;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        if (loadEmbeddedDefaults)
        {
            RegisterEmbeddedDefaults();
        }
    }

    /// <inheritdoc/>
    public CognitionTemplate? GetTemplate(string templateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        return _templates.TryGetValue(templateId, out var template) ? template : null;
    }

    /// <inheritdoc/>
    public bool HasTemplate(string templateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        return _templates.ContainsKey(templateId);
    }

    /// <inheritdoc/>
    public void RegisterTemplate(CognitionTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(template.Id);

        _templates[template.Id] = template;
        _logger?.LogDebug("Registered cognition template: {TemplateId}", template.Id);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetTemplateIds()
        => _templates.Keys.ToList().AsReadOnly();

    /// <inheritdoc/>
    public int LoadFromDirectory(string directory, bool recursive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            _logger?.LogWarning("Cognition template directory not found: {Directory}", directory);
            return 0;
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directory, "*.yaml", searchOption)
            .Concat(Directory.GetFiles(directory, "*.yml", searchOption));

        var loadedCount = 0;
        foreach (var file in files)
        {
            try
            {
                var template = LoadFromFile(file);
                if (template != null)
                {
                    RegisterTemplate(template);
                    loadedCount++;
                    _logger?.LogDebug("Loaded cognition template from {File}: {TemplateId}", file, template.Id);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load cognition template from {File}", file);
            }
        }

        _logger?.LogInformation(
            "Loaded {Count} cognition templates from {Directory}",
            loadedCount, directory);

        return loadedCount;
    }

    /// <summary>
    /// Loads a template from a YAML file.
    /// </summary>
    /// <param name="filePath">Path to the YAML file.</param>
    /// <returns>The loaded template, or null if parsing failed.</returns>
    public CognitionTemplate? LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            return null;
        }

        var yaml = File.ReadAllText(filePath);
        return ParseYaml(yaml);
    }

    /// <summary>
    /// Parses a template from YAML content.
    /// </summary>
    /// <param name="yaml">The YAML content.</param>
    /// <returns>The parsed template, or null if parsing failed.</returns>
    public CognitionTemplate? ParseYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        try
        {
            var dto = _yamlDeserializer.Deserialize<CognitionTemplateDto>(yaml);
            return dto?.ToTemplate();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse cognition template YAML");
            return null;
        }
    }

    private void RegisterEmbeddedDefaults()
    {
        // Register humanoid base template
        RegisterTemplate(CreateHumanoidBaseTemplate());

        // Register creature base template
        RegisterTemplate(CreateCreatureBaseTemplate());

        // Register object base template (minimal cognition)
        RegisterTemplate(CreateObjectBaseTemplate());

        _logger?.LogDebug("Registered {Count} embedded cognition templates", 3);
    }

    private static CognitionTemplate CreateHumanoidBaseTemplate()
    {
        return new CognitionTemplate
        {
            Id = CognitionTemplates.HumanoidBase,
            Description = "Standard cognition pipeline for humanoid characters",
            Version = "1.0",
            Stages =
            [
                new CognitionStageDefinition
                {
                    Name = CognitionStages.Filter,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "attention_filter",
                            HandlerName = "filter_attention",
                            Description = "Filters perceptions based on attention budget and priority",
                            Parameters = new Dictionary<string, object>
                            {
                                ["attention_budget"] = 100f,
                                ["max_perceptions"] = 10,
                                ["priority_weights"] = new Dictionary<string, object>
                                {
                                    ["threat"] = 10.0f,
                                    ["novelty"] = 5.0f,
                                    ["social"] = 3.0f,
                                    ["routine"] = 1.0f
                                },
                                ["threat_fast_track"] = true,
                                ["threat_threshold"] = 0.8f,
                                ["result_variable"] = "filtered_perceptions",
                                ["fast_track_variable"] = "fast_track_perceptions"
                            }
                        }
                    ]
                },
                new CognitionStageDefinition
                {
                    Name = CognitionStages.MemoryQuery,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "memory_query",
                            HandlerName = "query_memory",
                            Description = "Queries relevant memories for context",
                            Parameters = new Dictionary<string, object>
                            {
                                ["input"] = "${filtered_perceptions}",
                                ["max_results"] = 20,
                                ["min_relevance"] = 0.1f,
                                ["result_variable"] = "relevant_memories"
                            }
                        }
                    ]
                },
                new CognitionStageDefinition
                {
                    Name = CognitionStages.Significance,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "assess_significance",
                            HandlerName = "assess_significance",
                            Description = "Assesses perception significance for memory storage",
                            Parameters = new Dictionary<string, object>
                            {
                                ["perception"] = "${perception}",
                                ["memories"] = "${relevant_memories}",
                                ["relationships"] = "${agent.relationships}",
                                ["personality"] = "${agent.personality}",
                                ["current_goals"] = "${agent.goals}",
                                ["weights"] = new Dictionary<string, object>
                                {
                                    ["emotional"] = 0.4f,
                                    ["goal_relevance"] = 0.4f,
                                    ["relationship"] = 0.2f
                                },
                                ["threshold"] = 0.7f,
                                ["result_variable"] = "significance_score"
                            }
                        }
                    ]
                },
                new CognitionStageDefinition
                {
                    Name = CognitionStages.Storage,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "store_memory",
                            HandlerName = "store_memory",
                            Description = "Stores significant perceptions as memories",
                            Parameters = new Dictionary<string, object>
                            {
                                ["perception"] = "${perception}",
                                ["significance"] = "${significance_score}",
                                ["agent_id"] = "${agent.id}"
                            }
                        }
                    ]
                },
                new CognitionStageDefinition
                {
                    Name = CognitionStages.Intention,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "goal_impact",
                            HandlerName = "evaluate_goal_impact",
                            Description = "Evaluates how perceptions affect goals",
                            Parameters = new Dictionary<string, object>
                            {
                                ["perceptions"] = "${filtered_perceptions}",
                                ["current_goals"] = "${agent.goals}",
                                ["impact_threshold"] = 0.3f,
                                ["result_variable"] = "goal_impact"
                            }
                        },
                        new CognitionHandlerDefinition
                        {
                            Id = "goap_replan",
                            HandlerName = "trigger_goap_replan",
                            Description = "Triggers GOAP replanning if needed",
                            Parameters = new Dictionary<string, object>
                            {
                                ["goal_impact"] = "${goal_impact}",
                                ["fast_track_perceptions"] = "${fast_track_perceptions}",
                                ["urgency_threshold"] = 0.5f
                            }
                        }
                    ]
                }
            ]
        };
    }

    private static CognitionTemplate CreateCreatureBaseTemplate()
    {
        return new CognitionTemplate
        {
            Id = CognitionTemplates.CreatureBase,
            Description = "Cognition pipeline for creature characters (simpler than humanoid)",
            Version = "1.0",
            Stages =
            [
                new CognitionStageDefinition
                {
                    Name = CognitionStages.Filter,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "attention_filter",
                            HandlerName = "filter_attention",
                            Description = "Creature attention filtering (instinct-focused)",
                            Parameters = new Dictionary<string, object>
                            {
                                ["attention_budget"] = 50f,  // Lower than humanoid
                                ["max_perceptions"] = 5,     // Fewer simultaneous perceptions
                                ["priority_weights"] = new Dictionary<string, object>
                                {
                                    ["threat"] = 15.0f,      // Higher threat sensitivity
                                    ["novelty"] = 3.0f,      // Lower novelty interest
                                    ["social"] = 8.0f,       // Pack awareness
                                    ["routine"] = 1.0f
                                },
                                ["threat_fast_track"] = true,
                                ["threat_threshold"] = 0.6f,  // Lower threshold = faster reactions
                                ["result_variable"] = "filtered_perceptions",
                                ["fast_track_variable"] = "fast_track_perceptions"
                            }
                        }
                    ]
                },
                new CognitionStageDefinition
                {
                    Name = CognitionStages.MemoryQuery,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "memory_query",
                            HandlerName = "query_memory",
                            Description = "Limited memory query for creatures",
                            Parameters = new Dictionary<string, object>
                            {
                                ["input"] = "${filtered_perceptions}",
                                ["max_results"] = 5,        // Fewer memories
                                ["min_relevance"] = 0.3f,   // Higher relevance threshold
                                ["result_variable"] = "relevant_memories"
                            }
                        }
                    ]
                },
                // Creatures skip significance assessment - they react instinctively
                new CognitionStageDefinition
                {
                    Name = CognitionStages.Intention,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "goal_impact",
                            HandlerName = "evaluate_goal_impact",
                            Description = "Fast goal evaluation for creatures",
                            Parameters = new Dictionary<string, object>
                            {
                                ["perceptions"] = "${filtered_perceptions}",
                                ["current_goals"] = "${agent.goals}",
                                ["impact_threshold"] = 0.2f,  // Lower threshold = more reactive
                                ["result_variable"] = "goal_impact"
                            }
                        },
                        new CognitionHandlerDefinition
                        {
                            Id = "goap_replan",
                            HandlerName = "trigger_goap_replan",
                            Description = "Creature GOAP replanning",
                            Parameters = new Dictionary<string, object>
                            {
                                ["goal_impact"] = "${goal_impact}",
                                ["fast_track_perceptions"] = "${fast_track_perceptions}",
                                ["urgency_threshold"] = 0.3f  // Lower = more replanning
                            }
                        }
                    ]
                }
            ]
        };
    }

    private static CognitionTemplate CreateObjectBaseTemplate()
    {
        return new CognitionTemplate
        {
            Id = CognitionTemplates.ObjectBase,
            Description = "Minimal cognition for interactive objects (traps, doors, puzzles)",
            Version = "1.0",
            Stages =
            [
                new CognitionStageDefinition
                {
                    Name = CognitionStages.Filter,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "attention_filter",
                            HandlerName = "filter_attention",
                            Description = "Object attention filtering (trigger-focused)",
                            Parameters = new Dictionary<string, object>
                            {
                                ["attention_budget"] = 10f,   // Very limited
                                ["max_perceptions"] = 2,      // Only respond to direct triggers
                                ["priority_weights"] = new Dictionary<string, object>
                                {
                                    ["threat"] = 1.0f,
                                    ["novelty"] = 1.0f,
                                    ["social"] = 10.0f,       // High sensitivity to interactions
                                    ["routine"] = 0.5f
                                },
                                ["threat_fast_track"] = false,  // No fast-track for objects
                                ["result_variable"] = "filtered_perceptions",
                                ["fast_track_variable"] = "fast_track_perceptions"
                            }
                        }
                    ]
                },
                // Objects skip memory, significance - they're stateless responders
                new CognitionStageDefinition
                {
                    Name = CognitionStages.Intention,
                    Handlers =
                    [
                        new CognitionHandlerDefinition
                        {
                            Id = "goal_impact",
                            HandlerName = "evaluate_goal_impact",
                            Description = "Object state change evaluation",
                            Parameters = new Dictionary<string, object>
                            {
                                ["perceptions"] = "${filtered_perceptions}",
                                ["current_goals"] = "${object.state_goals}",
                                ["impact_threshold"] = 0.1f,
                                ["result_variable"] = "goal_impact"
                            }
                        }
                    ]
                }
            ]
        };
    }
}

#region YAML DTOs

/// <summary>
/// DTO for YAML template parsing.
/// </summary>
internal sealed class CognitionTemplateDto
{
    public string? Id { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public MetadataDto? Metadata { get; set; }
    public Dictionary<string, StageDto>? Stages { get; set; }

    public CognitionTemplate? ToTemplate()
    {
        // Support both flat and nested metadata formats
        var id = Id ?? Metadata?.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var stages = new List<CognitionStageDefinition>();
        if (Stages != null)
        {
            foreach (var (stageName, stageDto) in Stages)
            {
                stages.Add(stageDto.ToDefinition(stageName));
            }
        }

        return new CognitionTemplate
        {
            Id = id,
            Description = Description ?? Metadata?.Description,
            Version = Version ?? "1.0",
            Stages = stages
        };
    }
}

internal sealed class MetadataDto
{
    public string? Id { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
}

internal sealed class StageDto
{
    public List<HandlerDto>? Handlers { get; set; }

    public CognitionStageDefinition ToDefinition(string stageName)
    {
        var handlers = Handlers?.Select(h => h.ToDefinition()).ToList()
            ?? new List<CognitionHandlerDefinition>();

        return new CognitionStageDefinition
        {
            Name = stageName,
            Handlers = handlers
        };
    }
}

internal sealed class HandlerDto
{
    public string? Id { get; set; }
    public string? Handler { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<string, object>? Parameters { get; set; }

    public CognitionHandlerDefinition ToDefinition()
    {
        return new CognitionHandlerDefinition
        {
            Id = Id ?? Handler ?? "unknown",
            HandlerName = Handler ?? "unknown",
            Description = Description,
            Enabled = Enabled,
            Parameters = Parameters ?? new Dictionary<string, object>()
        };
    }
}

#endregion
