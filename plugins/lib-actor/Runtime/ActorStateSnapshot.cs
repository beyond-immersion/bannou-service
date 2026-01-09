namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Immutable snapshot of an actor's current state.
/// Used for reporting actor state without exposing mutable internals.
/// </summary>
public class ActorStateSnapshot
{
    /// <summary>
    /// Gets the actor's unique identifier.
    /// </summary>
    public string ActorId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the template ID this actor was spawned from.
    /// </summary>
    public Guid TemplateId { get; init; }

    /// <summary>
    /// Gets the actor's category.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional character ID for NPC brain actors.
    /// </summary>
    public Guid? CharacterId { get; init; }

    /// <summary>
    /// Gets the current status.
    /// </summary>
    public ActorStatus Status { get; init; }

    /// <summary>
    /// Gets the time the actor was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Gets the time of the last heartbeat.
    /// </summary>
    public DateTimeOffset? LastHeartbeat { get; init; }

    /// <summary>
    /// Gets the total behavior loop iterations.
    /// </summary>
    public long LoopIterations { get; init; }

    /// <summary>
    /// Gets the current perception queue depth.
    /// </summary>
    public int PerceptionQueueDepth { get; init; }

    /// <summary>
    /// Gets the current feeling state (emotional intensities).
    /// </summary>
    public IReadOnlyDictionary<string, double> Feelings { get; init; } = new Dictionary<string, double>();

    /// <summary>
    /// Gets the current goal state.
    /// </summary>
    public GoalStateData? Goals { get; init; }

    /// <summary>
    /// Gets the current memory entries.
    /// </summary>
    public IReadOnlyList<MemoryEntry> Memories { get; init; } = Array.Empty<MemoryEntry>();

    /// <summary>
    /// Gets the working memory (perception-derived data).
    /// </summary>
    public IReadOnlyDictionary<string, object> WorkingMemory { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Gets the current encounter state for Event Brain actors.
    /// Null if this actor is not managing an encounter.
    /// </summary>
    public EncounterStateData? Encounter { get; init; }

    /// <summary>
    /// Converts to API response model.
    /// </summary>
    public ActorInstanceResponse ToResponse(string? nodeId = null, string? nodeAppId = null)
    {
        return new ActorInstanceResponse
        {
            ActorId = ActorId,
            TemplateId = TemplateId,
            Category = Category,
            CharacterId = CharacterId,
            Status = Status,
            StartedAt = StartedAt,
            LastHeartbeat = LastHeartbeat,
            LoopIterations = LoopIterations,
            NodeId = nodeId,
            NodeAppId = nodeAppId
        };
    }
}

/// <summary>
/// Internal data model for goal state.
/// </summary>
public class GoalStateData
{
    /// <summary>
    /// Gets or sets the primary goal name.
    /// </summary>
    public string? PrimaryGoal { get; set; }

    /// <summary>
    /// Gets or sets the goal parameters.
    /// </summary>
    public Dictionary<string, object> GoalParameters { get; set; } = new();

    /// <summary>
    /// Gets or sets the secondary/background goals.
    /// </summary>
    public List<string> SecondaryGoals { get; set; } = new();
}

/// <summary>
/// Internal data model for a memory entry.
/// </summary>
public class MemoryEntry
{
    /// <summary>
    /// Gets or sets the memory key identifier.
    /// </summary>
    public string MemoryKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the memory value.
    /// </summary>
    public object? MemoryValue { get; set; }

    /// <summary>
    /// Gets or sets when this memory expires (null = permanent).
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets when this memory was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Internal data model for encounter state (Event Brain actors).
/// Tracks the encounter an Event Brain is coordinating.
/// </summary>
public class EncounterStateData
{
    /// <summary>
    /// Gets or sets the unique encounter identifier.
    /// </summary>
    public string EncounterId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the encounter type (e.g., "combat", "conversation", "choreography").
    /// </summary>
    public string EncounterType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the character IDs participating in this encounter.
    /// </summary>
    public List<Guid> Participants { get; set; } = new();

    /// <summary>
    /// Gets or sets the current phase of the encounter.
    /// </summary>
    public string Phase { get; set; } = "initializing";

    /// <summary>
    /// Gets or sets when the encounter started.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Gets or sets custom encounter-specific data.
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();
}
