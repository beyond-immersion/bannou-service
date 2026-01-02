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
            AutoSpawn = AutoSpawn?.ToConfig(),
            TickIntervalMs = TickIntervalMs,
            AutoSaveIntervalSeconds = AutoSaveIntervalSeconds,
            MaxInstancesPerNode = MaxInstancesPerNode,
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
    /// Converts to API model.
    /// </summary>
    public AutoSpawnConfig ToConfig()
    {
        return new AutoSpawnConfig
        {
            Enabled = Enabled,
            IdPattern = IdPattern,
            MaxInstances = MaxInstances
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
            MaxInstances = config.MaxInstances
        };
    }
}
