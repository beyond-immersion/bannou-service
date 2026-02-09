namespace BeyondImmersion.BannouService.Analytics;

/// <summary>
/// Internal data models for AnalyticsService.
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
public partial class AnalyticsService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Buffered analytics event stored prior to summary aggregation.
/// </summary>
internal sealed class BufferedAnalyticsEvent
{
    public Guid EventId { get; set; }
    public Guid GameServiceId { get; set; }
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public double Value { get; set; }
    public Guid? SessionId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Cached mapping for game service IDs by stub name.
/// </summary>
internal sealed class GameServiceCacheEntry
{
    public Guid ServiceId { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}

/// <summary>
/// Cached mapping between game sessions and game service IDs.
/// </summary>
internal sealed class GameSessionMappingData
{
    public Guid SessionId { get; set; }
    public string GameType { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for entity summary data.
/// </summary>
internal class EntitySummaryData
{
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public Guid GameServiceId { get; set; }
    public long TotalEvents { get; set; }
    public DateTimeOffset FirstEventAt { get; set; }
    public DateTimeOffset LastEventAt { get; set; }
    public Dictionary<string, long> EventCounts { get; set; } = new();
    public Dictionary<string, double> Aggregates { get; set; } = new();
}

/// <summary>
/// Internal storage model for Glicko-2 skill rating data.
/// </summary>
internal class SkillRatingData
{
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public string RatingType { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public double Rating { get; set; }
    public double RatingDeviation { get; set; }
    public double Volatility { get; set; }
    public int MatchesPlayed { get; set; }
    public DateTimeOffset? LastMatchAt { get; set; }
}

/// <summary>
/// Internal storage model for controller history events.
/// </summary>
internal class ControllerHistoryData
{
    public Guid EventId { get; set; }
    public Guid GameServiceId { get; set; }
    public Guid AccountId { get; set; }
    public Guid TargetEntityId { get; set; }
    public EntityType TargetEntityType { get; set; }
    public ControllerAction Action { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid? SessionId { get; set; }
}

/// <summary>
/// Cached mapping between realm IDs and game service IDs.
/// </summary>
internal sealed class RealmGameServiceCacheEntry
{
    public Guid GameServiceId { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}

/// <summary>
/// Cached mapping between character IDs and realm IDs.
/// </summary>
internal sealed class CharacterRealmCacheEntry
{
    public Guid RealmId { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
