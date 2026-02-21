namespace BeyondImmersion.BannouService.Leaderboard;

/// <summary>
/// Internal data models for LeaderboardService.
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
public partial class LeaderboardService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal storage model for leaderboard definition.
/// </summary>
internal class LeaderboardDefinitionData
{
    public Guid GameServiceId { get; set; }
    public string LeaderboardId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<EntityType>? EntityTypes { get; set; }
    public SortOrder SortOrder { get; set; }
    public UpdateMode UpdateMode { get; set; }
    public bool IsSeasonal { get; set; }
    public bool IsPublic { get; set; }
    public int? CurrentSeason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public object? Metadata { get; set; }
}
