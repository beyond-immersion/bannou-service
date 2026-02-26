namespace BeyondImmersion.BannouService.Quest;

/// <summary>
/// Internal model for quest definition storage.
/// Uses proper C# types per IMPLEMENTATION TENETS T25/T26.
/// </summary>
internal class QuestDefinitionModel
{
    /// <summary>Unique definition ID.</summary>
    public Guid DefinitionId { get; set; }

    /// <summary>ID of the underlying contract template.</summary>
    public Guid ContractTemplateId { get; set; }

    /// <summary>Unique quest code (uppercase, normalized).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Quest description for players.</summary>
    public string? Description { get; set; }

    /// <summary>Quest category.</summary>
    public QuestCategory Category { get; set; }

    /// <summary>Difficulty rating.</summary>
    public QuestDifficulty Difficulty { get; set; }

    /// <summary>Minimum character level required.</summary>
    public int? LevelRequirement { get; set; }

    /// <summary>Whether quest can be repeated.</summary>
    public bool Repeatable { get; set; }

    /// <summary>Cooldown in seconds for repeatable quests.</summary>
    public int? CooldownSeconds { get; set; }

    /// <summary>Time limit in seconds.</summary>
    public int? DeadlineSeconds { get; set; }

    /// <summary>Maximum party members.</summary>
    public int MaxQuestors { get; set; } = 1;

    /// <summary>Quest objectives.</summary>
    public List<ObjectiveDefinitionModel>? Objectives { get; set; }

    /// <summary>Quest prerequisites.</summary>
    public List<PrerequisiteDefinitionModel>? Prerequisites { get; set; }

    /// <summary>Quest rewards.</summary>
    public List<RewardDefinitionModel>? Rewards { get; set; }

    /// <summary>Tags for filtering.</summary>
    public ICollection<string>? Tags { get; set; }

    /// <summary>Character who offers this quest.</summary>
    public Guid? QuestGiverCharacterId { get; set; }

    /// <summary>Game service this quest belongs to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Whether this quest definition is deprecated.</summary>
    public bool IsDeprecated { get; set; }

    /// <summary>When the quest definition was deprecated.</summary>
    public DateTimeOffset? DeprecatedAt { get; set; }

    /// <summary>Reason for deprecation.</summary>
    public string? DeprecationReason { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Internal model for objective definitions within a quest.
/// </summary>
internal class ObjectiveDefinitionModel
{
    /// <summary>Unique objective code within quest.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Objective description.</summary>
    public string? Description { get; set; }

    /// <summary>Type of objective.</summary>
    public ObjectiveType ObjectiveType { get; set; }

    /// <summary>Count needed to complete.</summary>
    public int RequiredCount { get; set; }

    /// <summary>Entity type for kill/collect objectives.</summary>
    public string? TargetEntityType { get; set; }

    /// <summary>Subtype filter.</summary>
    public string? TargetEntitySubtype { get; set; }

    /// <summary>Location for travel/deliver objectives.</summary>
    public Guid? TargetLocationId { get; set; }

    /// <summary>Whether objective is hidden initially.</summary>
    public bool Hidden { get; set; }

    /// <summary>When to reveal hidden objective.</summary>
    public ObjectiveRevealBehavior RevealBehavior { get; set; }

    /// <summary>Whether objective is optional.</summary>
    public bool Optional { get; set; }
}

/// <summary>
/// Internal model for prerequisite definitions.
/// </summary>
internal class PrerequisiteDefinitionModel
{
    /// <summary>Type of prerequisite check.</summary>
    public PrerequisiteType Type { get; set; }

    /// <summary>Quest code for QUEST_COMPLETED type.</summary>
    public string? QuestCode { get; set; }

    /// <summary>Minimum level for CHARACTER_LEVEL type.</summary>
    public int? MinLevel { get; set; }

    /// <summary>Faction code for REPUTATION type.</summary>
    public string? FactionCode { get; set; }

    /// <summary>Minimum reputation for REPUTATION type.</summary>
    public int? MinReputation { get; set; }

    /// <summary>Item code for ITEM_OWNED type.</summary>
    public string? ItemCode { get; set; }

    /// <summary>Currency code for CURRENCY_AMOUNT type.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Minimum amount for CURRENCY_AMOUNT type.</summary>
    public int? MinAmount { get; set; }
}

/// <summary>
/// Internal model for reward definitions.
/// </summary>
internal class RewardDefinitionModel
{
    /// <summary>Type of reward.</summary>
    public RewardDefinitionType Type { get; set; }

    /// <summary>Currency code for CURRENCY rewards.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Amount for CURRENCY/EXPERIENCE rewards.</summary>
    public int? Amount { get; set; }

    /// <summary>Item code for ITEM rewards.</summary>
    public string? ItemCode { get; set; }

    /// <summary>Quantity for ITEM rewards.</summary>
    public int? Quantity { get; set; }

    /// <summary>Faction code for REPUTATION rewards.</summary>
    public string? FactionCode { get; set; }
}

/// <summary>
/// Internal model for quest instance storage.
/// </summary>
internal class QuestInstanceModel
{
    /// <summary>Unique instance ID.</summary>
    public Guid QuestInstanceId { get; set; }

    /// <summary>Quest definition ID.</summary>
    public Guid DefinitionId { get; set; }

    /// <summary>Underlying contract instance ID.</summary>
    public Guid ContractInstanceId { get; set; }

    /// <summary>Quest code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Quest name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Current status.</summary>
    public QuestStatus Status { get; set; }

    /// <summary>Characters on the quest.</summary>
    public List<Guid> QuestorCharacterIds { get; set; } = new();

    /// <summary>Quest giver NPC.</summary>
    public Guid? QuestGiverCharacterId { get; set; }

    /// <summary>When quest was accepted.</summary>
    public DateTimeOffset AcceptedAt { get; set; }

    /// <summary>Quest deadline.</summary>
    public DateTimeOffset? Deadline { get; set; }

    /// <summary>When completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Game service ID.</summary>
    public Guid GameServiceId { get; set; }
}

/// <summary>
/// Internal model for objective progress tracking.
/// </summary>
internal class ObjectiveProgressModel
{
    /// <summary>Quest instance ID.</summary>
    public Guid QuestInstanceId { get; set; }

    /// <summary>Objective code.</summary>
    public string ObjectiveCode { get; set; } = string.Empty;

    /// <summary>Objective name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Objective description.</summary>
    public string? Description { get; set; }

    /// <summary>Type of objective.</summary>
    public ObjectiveType ObjectiveType { get; set; }

    /// <summary>Current progress count.</summary>
    public int CurrentCount { get; set; }

    /// <summary>Required count.</summary>
    public int RequiredCount { get; set; }

    /// <summary>Whether complete.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Whether hidden.</summary>
    public bool Hidden { get; set; }

    /// <summary>When to reveal.</summary>
    public ObjectiveRevealBehavior RevealBehavior { get; set; }

    /// <summary>Whether optional.</summary>
    public bool Optional { get; set; }

    /// <summary>
    /// Tracked entity IDs for deduplication (e.g., killed enemies, collected items).
    /// Prevents counting the same entity multiple times for progress.
    /// </summary>
    public HashSet<Guid> TrackedEntityIds { get; set; } = new();
}

/// <summary>
/// Character quest index for efficient lookup.
/// </summary>
internal class CharacterQuestIndex
{
    /// <summary>Character ID.</summary>
    public Guid CharacterId { get; set; }

    /// <summary>Active quest instance IDs.</summary>
    public List<Guid>? ActiveQuestIds { get; set; }

    /// <summary>Completed quest codes.</summary>
    public List<string>? CompletedQuestCodes { get; set; }
}

/// <summary>
/// Cooldown tracking entry.
/// </summary>
internal class CooldownEntry
{
    /// <summary>Character ID.</summary>
    public Guid CharacterId { get; set; }

    /// <summary>Quest code.</summary>
    public string QuestCode { get; set; } = string.Empty;

    /// <summary>When cooldown expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Idempotency tracking record.
/// </summary>
internal class IdempotencyRecord
{
    /// <summary>Idempotency key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Created timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Cached response ID if applicable.</summary>
    public Guid? ResponseId { get; set; }
}
