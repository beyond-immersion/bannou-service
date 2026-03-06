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
    public List<PlatformMappingData>? PlatformMappings { get; set; }
    public List<string>? Prerequisites { get; set; }

    /// <summary>Score type code for matching analytics.score.updated events.</summary>
    public string? ScoreType { get; set; }

    /// <summary>Milestone type code for matching analytics.milestone.reached events.</summary>
    public string? MilestoneType { get; set; }

    /// <summary>Expected milestone value for matching analytics.milestone.reached events.</summary>
    public double? MilestoneValue { get; set; }

    /// <summary>Expected milestone name for matching analytics.milestone.reached events.</summary>
    public string? MilestoneName { get; set; }

    /// <summary>Leaderboard ID for matching leaderboard.rank.changed events.</summary>
    public string? LeaderboardId { get; set; }

    /// <summary>Rank threshold for leaderboard achievements (unlock when rank &lt;= threshold).</summary>
    public long? RankThreshold { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Whether this definition is deprecated and should not be used for new progress.</summary>
    public bool IsDeprecated { get; set; }

    /// <summary>When deprecation occurred, null if not deprecated.</summary>
    public DateTimeOffset? DeprecatedAt { get; set; }

    /// <summary>Audit reason for deprecation, null if not deprecated.</summary>
    public string? DeprecationReason { get; set; }

    public long EarnedCount { get; set; }
    public long TotalEligibleEntities { get; set; }
    public double? RarityPercent { get; set; }
    public DateTimeOffset? RarityCalculatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public object? Metadata { get; set; }
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
/// Internal model for platform-specific achievement ID mapping.
/// </summary>
internal class PlatformMappingData
{
    /// <summary>External platform.</summary>
    public Platform Platform { get; set; }

    /// <summary>Platform-specific achievement identifier.</summary>
    public string PlatformAchievementId { get; set; } = string.Empty;
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

/// <summary>
/// Internal storage model for per-entity per-platform sync tracking.
/// Key pattern: {gameServiceId}:{entityId}:{platform}
/// </summary>
internal class PlatformSyncTrackingData
{
    /// <summary>Number of achievements successfully synced to this platform.</summary>
    public int SyncedCount { get; set; }

    /// <summary>Number of achievements that failed to sync.</summary>
    public int FailedCount { get; set; }

    /// <summary>Timestamp of the last successful sync operation.</summary>
    public DateTimeOffset? LastSyncAt { get; set; }

    /// <summary>Most recent sync error message, null if last sync was successful.</summary>
    public string? LastError { get; set; }
}
