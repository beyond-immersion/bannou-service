namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Internal data models for TransitService.
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
public partial class TransitService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal storage model for transit mode definitions.
/// Stored in transit-modes (MySQL).
/// Key pattern: mode:{code}
/// </summary>
/// <remarks>
/// Transit modes use Category A deprecation per IMPLEMENTATION TENETS:
/// connections store <c>compatibleModes</c> and journeys store <c>primaryModeCode</c>/<c>legModes</c>
/// referencing mode codes, so persistent data in OTHER entities references this entity's code.
/// Deprecation triple-field model: <see cref="IsDeprecated"/>, <see cref="DeprecatedAt"/>,
/// <see cref="DeprecationReason"/>.
/// </remarks>
internal class TransitModeModel
{
    /// <summary>Unique mode code (e.g., "walking", "horseback", "river_boat"). Primary key.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description of this transit mode.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Base travel speed in kilometers per game-hour.</summary>
    public decimal BaseSpeedKmPerGameHour { get; set; }

    /// <summary>Per-terrain speed multipliers. Null = base speed applies uniformly across all compatible terrain.</summary>
    public List<TerrainSpeedModifierEntry>? TerrainSpeedModifiers { get; set; }

    /// <summary>Maximum number of passengers this mode can carry.</summary>
    public int PassengerCapacity { get; set; }

    /// <summary>Maximum cargo weight in kilograms this mode can carry.</summary>
    public decimal CargoCapacityKg { get; set; }

    /// <summary>Per-mode cargo speed penalty rate overriding plugin-level default. Null = use config default.</summary>
    public decimal? CargoSpeedPenaltyRate { get; set; }

    /// <summary>Terrain types this mode can traverse. Empty array = all terrain.</summary>
    public List<string> CompatibleTerrainTypes { get; set; } = new();

    /// <summary>Entity types that can use this mode. Null = no restriction.</summary>
    public List<string>? ValidEntityTypes { get; set; }

    /// <summary>Item, species, and physical requirements for using this mode.</summary>
    public TransitModeRequirementsModel Requirements { get; set; } = new();

    /// <summary>Fatigue accumulation rate per game-hour of travel.</summary>
    public decimal FatigueRatePerGameHour { get; set; }

    /// <summary>Noise level on a 0-1 normalized scale.</summary>
    public decimal NoiseLevelNormalized { get; set; }

    /// <summary>Realm restrictions. Null = available in all realms.</summary>
    public List<Guid>? RealmRestrictions { get; set; }

    /// <summary>Whether this mode is deprecated (Category A deprecation).</summary>
    public bool IsDeprecated { get; set; }

    /// <summary>When the mode was deprecated. Null if not deprecated.</summary>
    public DateTimeOffset? DeprecatedAt { get; set; }

    /// <summary>Reason for deprecation. Null if not deprecated.</summary>
    public string? DeprecationReason { get; set; }

    /// <summary>Freeform classification tags.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>When this mode was first registered.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this mode was last modified.</summary>
    public DateTimeOffset ModifiedAt { get; set; }
}

/// <summary>
/// Internal storage sub-model for terrain speed modifiers within a transit mode.
/// </summary>
internal class TerrainSpeedModifierEntry
{
    /// <summary>Terrain type code this modifier applies to.</summary>
    public string TerrainType { get; set; } = string.Empty;

    /// <summary>Speed multiplier for this terrain (1.0 = no change, 0.5 = half speed).</summary>
    public decimal Multiplier { get; set; }
}

/// <summary>
/// Internal storage sub-model for transit mode requirements.
/// </summary>
internal class TransitModeRequirementsModel
{
    /// <summary>Item tag required to use this mode. Null = no item needed.</summary>
    public string? RequiredItemTag { get; set; }

    /// <summary>Species codes allowed to use this mode. Null = any species.</summary>
    public List<string>? AllowedSpeciesCodes { get; set; }

    /// <summary>Species codes excluded from using this mode. Null = no exclusions.</summary>
    public List<string>? ExcludedSpeciesCodes { get; set; }

    /// <summary>Minimum party size required to use this mode (e.g., ocean vessel needs crew).</summary>
    public int MinimumPartySize { get; set; } = 1;

    /// <summary>Maximum entity size category that can use this mode. Null = no size restriction.</summary>
    public string? MaximumEntitySizeCategory { get; set; }
}

/// <summary>
/// Internal storage model for transit connections (graph edges between locations).
/// Stored in transit-connections (MySQL).
/// Key pattern: connection:{id}
/// </summary>
/// <remarks>
/// Realm fields (<see cref="FromRealmId"/>, <see cref="ToRealmId"/>, <see cref="CrossRealm"/>)
/// are derived from endpoint locations via Location service and never specified by callers.
/// </remarks>
internal class TransitConnectionModel
{
    /// <summary>Unique connection identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Source location for this connection.</summary>
    public Guid FromLocationId { get; set; }

    /// <summary>Destination location for this connection.</summary>
    public Guid ToLocationId { get; set; }

    /// <summary>Whether this connection can be traversed in both directions.</summary>
    public bool Bidirectional { get; set; }

    /// <summary>Distance in kilometers between the connected locations.</summary>
    public decimal DistanceKm { get; set; }

    /// <summary>Terrain type code for this connection (Category B content code).</summary>
    public string TerrainType { get; set; } = string.Empty;

    /// <summary>Mode codes compatible with this connection. Empty = walking only.</summary>
    public List<string> CompatibleModes { get; set; } = new();

    /// <summary>Seasonal availability restrictions. Null = always available.</summary>
    public List<SeasonalAvailabilityModel>? SeasonalAvailability { get; set; }

    /// <summary>Base risk level for this connection on a 0-1 scale.</summary>
    public decimal BaseRiskLevel { get; set; }

    /// <summary>Human-readable risk description. Null if no special risk.</summary>
    public string? RiskDescription { get; set; }

    /// <summary>Current operational status of this connection.</summary>
    public ConnectionStatus Status { get; set; }

    /// <summary>Reason for the current status. Null if status is open.</summary>
    public string? StatusReason { get; set; }

    /// <summary>When the status was last changed.</summary>
    public DateTimeOffset StatusChangedAt { get; set; }

    /// <summary>Whether this connection must be discovered before appearing in routes.</summary>
    public bool Discoverable { get; set; }

    /// <summary>Human-readable name for this connection. Null if unnamed.</summary>
    public string? Name { get; set; }

    /// <summary>Unique code identifier for this connection. Null if no code assigned.</summary>
    public string? Code { get; set; }

    /// <summary>Freeform classification tags.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Realm ID of the source location (derived from Location service).</summary>
    public Guid FromRealmId { get; set; }

    /// <summary>Realm ID of the destination location (derived from Location service).</summary>
    public Guid ToRealmId { get; set; }

    /// <summary>Whether this connection crosses realm boundaries (derived).</summary>
    public bool CrossRealm { get; set; }

    /// <summary>When this connection was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this connection was last modified.</summary>
    public DateTimeOffset ModifiedAt { get; set; }
}

/// <summary>
/// Storage sub-model for seasonal availability entries within a connection.
/// Public because it is referenced by <see cref="ConnectionGraphEntry"/> which is exposed
/// via the public <see cref="ITransitConnectionGraphCache"/> interface.
/// </summary>
public class SeasonalAvailabilityModel
{
    /// <summary>Season code matching the realm's Worldstate calendar template.</summary>
    public string Season { get; set; } = string.Empty;

    /// <summary>Whether the connection is available during this season.</summary>
    public bool Available { get; set; }
}

/// <summary>
/// Internal storage model for active transit journeys.
/// Stored in transit-journeys (Redis) while active, then archived to transit-journeys-archive (MySQL).
/// Key pattern: journey:{id}
/// </summary>
internal class TransitJourneyModel
{
    /// <summary>Unique journey identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Entity undertaking this journey (e.g., character ID, caravan ID).</summary>
    public Guid EntityId { get; set; }

    /// <summary>Type of the traveling entity (Category B content code: "character", "caravan", etc.).</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Ordered list of route legs for this journey.</summary>
    public List<TransitJourneyLegModel> Legs { get; set; } = new();

    /// <summary>Index of the current leg (0-based).</summary>
    public int CurrentLegIndex { get; set; }

    /// <summary>Primary transit mode code for this journey.</summary>
    public string PrimaryModeCode { get; set; } = string.Empty;

    /// <summary>Effective travel speed in km/game-hour accounting for cargo and modifiers.</summary>
    public decimal EffectiveSpeedKmPerGameHour { get; set; }

    /// <summary>Game-time when departure was planned.</summary>
    public decimal PlannedDepartureGameTime { get; set; }

    /// <summary>Game-time when the entity actually departed. Null if not yet departed.</summary>
    public decimal? ActualDepartureGameTime { get; set; }

    /// <summary>Estimated game-time of arrival at destination.</summary>
    public decimal EstimatedArrivalGameTime { get; set; }

    /// <summary>Actual game-time of arrival. Null if not yet arrived.</summary>
    public decimal? ActualArrivalGameTime { get; set; }

    /// <summary>Starting location of the journey.</summary>
    public Guid OriginLocationId { get; set; }

    /// <summary>Final destination of the journey.</summary>
    public Guid DestinationLocationId { get; set; }

    /// <summary>Current location of the traveling entity.</summary>
    public Guid CurrentLocationId { get; set; }

    /// <summary>Current journey status.</summary>
    public JourneyStatus Status { get; set; }

    /// <summary>Reason for the current status. Null for normal status transitions.</summary>
    public string? StatusReason { get; set; }

    /// <summary>Interruption records for this journey.</summary>
    public List<TransitInterruptionModel> Interruptions { get; set; } = new();

    /// <summary>Number of entities in the traveling party.</summary>
    public int PartySize { get; set; }

    /// <summary>Total cargo weight in kilograms.</summary>
    public decimal CargoWeightKg { get; set; }

    /// <summary>Realm ID for the journey's origin location (denormalized from Location service for efficient archival worker game-time lookups).</summary>
    public Guid RealmId { get; set; }

    /// <summary>When this journey was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this journey was last modified.</summary>
    public DateTimeOffset ModifiedAt { get; set; }
}

/// <summary>
/// Internal storage model for a single leg of a transit journey.
/// </summary>
internal class TransitJourneyLegModel
{
    /// <summary>Connection traversed in this leg.</summary>
    public Guid ConnectionId { get; set; }

    /// <summary>Starting location for this leg.</summary>
    public Guid FromLocationId { get; set; }

    /// <summary>Ending location for this leg.</summary>
    public Guid ToLocationId { get; set; }

    /// <summary>Mode code for this leg (may differ from journey's primary mode for multi-modal routes).</summary>
    public string ModeCode { get; set; } = string.Empty;

    /// <summary>Distance in kilometers for this leg (copied from connection for historical record).</summary>
    public decimal DistanceKm { get; set; }

    /// <summary>Terrain type for this leg (copied from connection for historical record).</summary>
    public string TerrainType { get; set; } = string.Empty;

    /// <summary>Estimated travel duration in game-hours for this leg.</summary>
    public decimal EstimatedDurationGameHours { get; set; }

    /// <summary>Transfer time at waypoint in game-hours (disembarking, dock wait, etc.). Null = no transfer time.</summary>
    public decimal? WaypointTransferTimeGameHours { get; set; }

    /// <summary>Current status of this leg.</summary>
    public JourneyLegStatus Status { get; set; }

    /// <summary>Game-time when this leg was completed. Null if not yet completed.</summary>
    public decimal? CompletedAtGameTime { get; set; }
}

/// <summary>
/// Internal storage model for a journey interruption record.
/// </summary>
/// <remarks>
/// The <see cref="Resolved"/> flag is stored entity state (NOT filler per IMPLEMENTATION TENETS).
/// An interruption can be resolved with 0 <see cref="DurationGameHours"/> (immediately repelled),
/// so this flag distinguishes active interruptions from resolved ones.
/// </remarks>
internal class TransitInterruptionModel
{
    /// <summary>Index of the leg where the interruption occurred.</summary>
    public int LegIndex { get; set; }

    /// <summary>Game-time when the interruption occurred.</summary>
    public decimal GameTime { get; set; }

    /// <summary>Reason for the interruption (e.g., "bandit_attack", "storm", "breakdown").</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Duration of the interruption in game-hours (0 = resolved immediately).</summary>
    public decimal DurationGameHours { get; set; }

    /// <summary>Whether this interruption has been resolved.</summary>
    public bool Resolved { get; set; }
}

/// <summary>
/// Internal storage model for per-entity connection discovery records.
/// Stored in transit-discovery (MySQL).
/// Key pattern: discovery:{entityId}:{connectionId}
/// </summary>
internal class TransitDiscoveryModel
{
    /// <summary>Entity that discovered this connection.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Connection that was discovered.</summary>
    public Guid ConnectionId { get; set; }

    /// <summary>How the connection was discovered (Category B content code: "travel", "guide", "hearsay", etc.).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>When the connection was first discovered.</summary>
    public DateTimeOffset DiscoveredAt { get; set; }

    /// <summary>
    /// Whether this was a new discovery at the time of recording.
    /// Stored entity state distinguishing first discovery from re-revelation,
    /// needed by Collection (only new discoveries trigger unlock events).
    /// </summary>
    public bool IsNew { get; set; }
}

/// <summary>
/// Storage model for cached connection graph adjacency list entries.
/// Stored in transit-connection-graph (Redis).
/// Key pattern: graph:{realmId}
/// </summary>
/// <remarks>
/// <para>
/// Each entry represents a single directional edge in the cached adjacency list.
/// For bidirectional connections, two entries are stored (one per direction).
/// The cache is rebuilt from MySQL on cache miss and invalidated on connection mutations.
/// </para>
/// <para>
/// Public because it is exposed via the <see cref="ITransitConnectionGraphCache"/> and
/// <see cref="ITransitRouteCalculator"/> interfaces.
/// </para>
/// </remarks>
public class ConnectionGraphEntry
{
    /// <summary>Source location ID for this graph edge.</summary>
    public Guid FromLocationId { get; set; }

    /// <summary>Destination location ID for this graph edge.</summary>
    public Guid ToLocationId { get; set; }

    /// <summary>Connection ID for this edge.</summary>
    public Guid ConnectionId { get; set; }

    /// <summary>Distance in kilometers.</summary>
    public decimal DistanceKm { get; set; }

    /// <summary>Terrain type of this connection.</summary>
    public string TerrainType { get; set; } = string.Empty;

    /// <summary>Compatible mode codes for this edge.</summary>
    public List<string> CompatibleModes { get; set; } = new();

    /// <summary>Base risk level (0-1).</summary>
    public decimal BaseRiskLevel { get; set; }

    /// <summary>Current connection status.</summary>
    public ConnectionStatus Status { get; set; }

    /// <summary>Whether this connection requires discovery to use in routes.</summary>
    public bool Discoverable { get; set; }

    /// <summary>Connection code for route display. Null if unnamed.</summary>
    public string? Code { get; set; }

    /// <summary>Connection name for route display. Null if unnamed.</summary>
    public string? Name { get; set; }

    /// <summary>Seasonal availability restrictions. Null = always available.</summary>
    public List<SeasonalAvailabilityModel>? SeasonalAvailability { get; set; }

    /// <summary>Waypoint transfer time in game-hours at the destination of this edge. Null = no transfer time.</summary>
    public decimal? WaypointTransferTimeGameHours { get; set; }
}

/// <summary>
/// Internal storage model for archived journeys.
/// Stored in transit-journeys-archive (MySQL).
/// Key pattern: archive:{id}
/// </summary>
/// <remarks>
/// Mirrors <see cref="TransitJourneyModel"/> for completed/abandoned journeys that have
/// been archived from Redis to MySQL by the Journey Archival Worker. Retains all journey
/// data for Trade velocity calculations, Analytics aggregation, and Character History
/// travel biography. Archived journeys may have longer retention than active journeys
/// (governed by <c>JourneyArchiveRetentionDays</c> configuration).
/// </remarks>
internal class JourneyArchiveModel
{
    /// <summary>Unique journey identifier (same as the original journey ID).</summary>
    public Guid Id { get; set; }

    /// <summary>Entity that undertook this journey.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Type of the traveling entity.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Ordered list of route legs.</summary>
    public List<TransitJourneyLegModel> Legs { get; set; } = new();

    /// <summary>Final leg index when journey completed/was abandoned.</summary>
    public int CurrentLegIndex { get; set; }

    /// <summary>Primary transit mode code used.</summary>
    public string PrimaryModeCode { get; set; } = string.Empty;

    /// <summary>Effective speed at time of journey.</summary>
    public decimal EffectiveSpeedKmPerGameHour { get; set; }

    /// <summary>Planned departure game-time.</summary>
    public decimal PlannedDepartureGameTime { get; set; }

    /// <summary>Actual departure game-time. Null if journey was abandoned before departure.</summary>
    public decimal? ActualDepartureGameTime { get; set; }

    /// <summary>Estimated arrival game-time at creation.</summary>
    public decimal EstimatedArrivalGameTime { get; set; }

    /// <summary>Actual arrival game-time. Null if journey was abandoned.</summary>
    public decimal? ActualArrivalGameTime { get; set; }

    /// <summary>Origin location of the journey.</summary>
    public Guid OriginLocationId { get; set; }

    /// <summary>Intended destination of the journey.</summary>
    public Guid DestinationLocationId { get; set; }

    /// <summary>Location where the entity ended up (destination if arrived, or last known if abandoned).</summary>
    public Guid CurrentLocationId { get; set; }

    /// <summary>Final journey status (arrived or abandoned).</summary>
    public JourneyStatus Status { get; set; }

    /// <summary>Final status reason.</summary>
    public string? StatusReason { get; set; }

    /// <summary>All interruption records from the journey.</summary>
    public List<TransitInterruptionModel> Interruptions { get; set; } = new();

    /// <summary>Party size during the journey.</summary>
    public int PartySize { get; set; }

    /// <summary>Cargo weight during the journey.</summary>
    public decimal CargoWeightKg { get; set; }

    /// <summary>Realm ID for the journey's origin location (denormalized from the active journey model).</summary>
    public Guid RealmId { get; set; }

    /// <summary>When this journey was originally created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this journey was last modified (before archival).</summary>
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>When this journey was archived from Redis to MySQL.</summary>
    public DateTimeOffset ArchivedAt { get; set; }
}
