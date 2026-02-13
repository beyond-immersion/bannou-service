using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Behavior;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Internal data model for actor templates stored in lib-state.
/// </summary>
public class ActorTemplateData
{
    /// <summary>
    /// Gets or sets the unique template identifier.
    /// </summary>
    public Guid TemplateId { get; set; }

    /// <summary>
    /// Gets or sets the category identifier (e.g., "npc-brain", "world-admin").
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the behavior reference in lib-assets.
    /// </summary>
    public string BehaviorRef { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default configuration for behavior execution.
    /// </summary>
    public object? Configuration { get; set; }

    /// <summary>
    /// Gets or sets the auto-spawn configuration.
    /// </summary>
    public AutoSpawnConfigData? AutoSpawn { get; set; }

    /// <summary>
    /// Gets or sets the tick interval in milliseconds.
    /// </summary>
    public int TickIntervalMs { get; set; } = 100;

    /// <summary>
    /// Gets or sets the auto-save interval in seconds.
    /// </summary>
    public int AutoSaveIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the maximum instances per node.
    /// </summary>
    public int MaxInstancesPerNode { get; set; } = 100;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the cognition template ID for this actor type.
    /// Primary source for cognition pipeline resolution (per IMPLEMENTATION TENETS).
    /// When null, falls back to ABML metadata, then category default mapping.
    /// </summary>
    public string? CognitionTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the static template-level cognition overrides.
    /// Applied as the first layer in the three-layer override composition.
    /// </summary>
    public CognitionOverrides? CognitionOverrides { get; set; }

    /// <summary>
    /// Deserializes cognition overrides from API object.
    /// The generated API model uses object? (arrives as JsonElement), but internal model is typed.
    /// </summary>
    public static CognitionOverrides? DeserializeCognitionOverrides(object? apiOverrides)
    {
        if (apiOverrides is not JsonElement element)
            return null;

        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
            return null;

        return BannouJson.Deserialize<CognitionOverrides>(element.GetRawText());
    }

    /// <summary>
    /// Serializes cognition overrides to a JsonElement for the API response model.
    /// </summary>
    private static object? SerializeCognitionOverrides(CognitionOverrides? overrides)
    {
        if (overrides == null)
            return null;

        var json = BannouJson.Serialize(overrides);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// Converts to API response model.
    /// </summary>
    public ActorTemplateResponse ToResponse()
    {
        return new ActorTemplateResponse
        {
            TemplateId = TemplateId,
            Category = Category,
            BehaviorRef = BehaviorRef,
            Configuration = Configuration,
            AutoSpawn = AutoSpawn?.ToConfig() ?? new AutoSpawnConfig { Enabled = false },
            TickIntervalMs = TickIntervalMs,
            AutoSaveIntervalSeconds = AutoSaveIntervalSeconds,
            MaxInstancesPerNode = MaxInstancesPerNode,
            CognitionTemplateId = CognitionTemplateId,
            CognitionOverrides = SerializeCognitionOverrides(CognitionOverrides),
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}

/// <summary>
/// Internal data model for auto-spawn configuration.
/// </summary>
public class AutoSpawnConfigData
{
    /// <summary>
    /// Gets or sets whether auto-spawn is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the ID pattern for auto-spawn.
    /// </summary>
    public string? IdPattern { get; set; }

    /// <summary>
    /// Gets or sets the maximum auto-spawned instances.
    /// </summary>
    public int? MaxInstances { get; set; }

    /// <summary>
    /// Gets or sets the 1-based regex capture group index for extracting CharacterId from actor ID.
    /// </summary>
    public int? CharacterIdCaptureGroup { get; set; }

    /// <summary>
    /// Converts to API model.
    /// </summary>
    public AutoSpawnConfig ToConfig()
    {
        return new AutoSpawnConfig
        {
            Enabled = Enabled,
            IdPattern = IdPattern,
            MaxInstances = MaxInstances,
            CharacterIdCaptureGroup = CharacterIdCaptureGroup
        };
    }

    /// <summary>
    /// Creates from API model.
    /// </summary>
    public static AutoSpawnConfigData? FromConfig(AutoSpawnConfig? config)
    {
        if (config == null)
            return null;

        return new AutoSpawnConfigData
        {
            Enabled = config.Enabled,
            IdPattern = config.IdPattern,
            MaxInstances = config.MaxInstances,
            CharacterIdCaptureGroup = config.CharacterIdCaptureGroup
        };
    }
}
