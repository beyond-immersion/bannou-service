namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Internal data models for LocationService.
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
public partial class LocationService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal storage model for entity presence in Redis.
/// Stored at key entity-location:{entityType}:{entityId} with configurable TTL.
/// </summary>
internal class EntityPresenceModel
{
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid LocationId { get; set; }
    public Guid RealmId { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public string? ReportedBy { get; set; }
}

/// <summary>
/// Cached location context data for ABML variable provider resolution.
/// Contains pre-resolved physical facts about the location a character occupies.
/// </summary>
public record LocationContextData(
    string Zone,
    string Name,
    string? Region,
    LocationType Type,
    int Depth,
    string Realm,
    List<string> NearbyPois,
    int EntityCount);
