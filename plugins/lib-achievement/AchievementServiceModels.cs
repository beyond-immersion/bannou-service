namespace BeyondImmersion.BannouService.Achievement;

/// <summary>
/// Internal data models for AchievementService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class AchievementService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal storage model for achievement definition.
/// </summary>
internal class AchievementDefinitionData
{
    public Guid GameServiceId { get; set; }
    public string AchievementId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? HiddenDescription { get; set; }
    public AchievementType AchievementType { get; set; }
    public List<EntityType>? EntityTypes { get; set; }
    public int? ProgressTarget { get; set; }
    public int Points { get; set; }
    public string? IconUrl { get; set; }
    public List<Platform>? Platforms { get; set; }
    public Dictionary<Platform, string>? PlatformIds { get; set; }
    public List<string>? Prerequisites { get; set; }
    public bool IsActive { get; set; }
    public long EarnedCount { get; set; }
    public long TotalEligibleEntities { get; set; }
    public double? RarityPercent { get; set; }
    public DateTimeOffset? RarityCalculatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Internal storage model for entity progress across all achievements.
/// </summary>
internal class EntityProgressData
{
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public Dictionary<string, AchievementProgressData> Achievements { get; set; } = new();
    public int TotalPoints { get; set; }
}

/// <summary>
/// Internal storage model for progress on a single achievement.
/// </summary>
internal class AchievementProgressData
{
    public string DisplayName { get; set; } = string.Empty;
    public int CurrentProgress { get; set; }
    public int TargetProgress { get; set; }
    public bool IsUnlocked { get; set; }
    public DateTimeOffset? UnlockedAt { get; set; }
}
