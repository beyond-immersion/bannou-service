namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Internal data models for GardenerService.
/// </summary>
/// <remarks>
/// <para>
/// Contains storage models for state stores, cache entries, and internal DTOs.
/// These are NOT exposed via the API and are NOT generated from schemas.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations.
/// </para>
/// </remarks>
public partial class GardenerService
{
    // Partial class anchor for internal models below.
}

// ============================================================================
// VOID INSTANCE MODELS
// ============================================================================

/// <summary>
/// Storage model for an active void instance (Redis, keyed by accountId).
/// </summary>
internal class VoidInstanceModel
{
    /// <summary>Unique void instance identifier.</summary>
    public Guid VoidInstanceId { get; set; }

    /// <summary>Account that owns this void.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Active seed driving this void session.</summary>
    public Guid SeedId { get; set; }

    /// <summary>When the player entered the void.</summary>
    public DateTimeOffset EnteredAt { get; set; }

    /// <summary>Player's current position in void space.</summary>
    public Position3D PlayerPosition { get; set; } = new();

    /// <summary>Accumulated drift vector for narrative analysis.</summary>
    public Position3D DriftVector { get; set; } = new();

    /// <summary>IDs of active POIs in this void instance.</summary>
    public List<Guid> ActivePoiIds { get; set; } = new();

    /// <summary>Bond ID if player is bonded and sharing void, null otherwise.</summary>
    public Guid? BondId { get; set; }

    /// <summary>Bonded partner's seed ID if sharing void, null otherwise.</summary>
    public Guid? BondedSeedId { get; set; }
}

// ============================================================================
// POI MODELS
// ============================================================================

/// <summary>
/// Storage model for a POI in a void instance (Redis, keyed by poiId).
/// </summary>
internal class PoiModel
{
    /// <summary>Unique POI identifier.</summary>
    public Guid PoiId { get; set; }

    /// <summary>Void instance this POI belongs to.</summary>
    public Guid VoidInstanceId { get; set; }

    /// <summary>Account that owns the parent void instance.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Sensory type of this POI.</summary>
    public PoiType PoiType { get; set; }

    /// <summary>Scenario template this POI leads to.</summary>
    public Guid ScenarioTemplateId { get; set; }

    /// <summary>Position in void space.</summary>
    public Position3D Position { get; set; } = new();

    /// <summary>When this POI was spawned.</summary>
    public DateTimeOffset SpawnedAt { get; set; }

    /// <summary>When this POI expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Whether the player has been notified of this POI.</summary>
    public bool Discovered { get; set; }

    /// <summary>Score from the scenario selection algorithm.</summary>
    public double SelectionScore { get; set; }
}

// ============================================================================
// SCENARIO TEMPLATE STORAGE MODEL
// ============================================================================

/// <summary>
/// Storage model for scenario templates (MySQL, durable).
/// </summary>
internal class ScenarioTemplateModel
{
    /// <summary>Unique template identifier.</summary>
    public Guid ScenarioTemplateId { get; set; }

    /// <summary>Unique code for reference lookups.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Template description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Primary gameplay category.</summary>
    public ScenarioCategory Category { get; set; }

    /// <summary>World connectivity mode.</summary>
    public ConnectivityMode ConnectivityMode { get; set; }

    /// <summary>Minimum deployment phase required.</summary>
    public DeploymentPhase MinimumPhase { get; set; }

    /// <summary>Current lifecycle status.</summary>
    public TemplateStatus Status { get; set; }

    /// <summary>Estimated duration in minutes.</summary>
    public int EstimatedDurationMinutes { get; set; }

    /// <summary>Whether bonded players can enter together.</summary>
    public bool BondCompatible { get; set; }

    /// <summary>Maximum simultaneous instances of this template.</summary>
    public int MaxConcurrentInstances { get; set; }

    /// <summary>Growth domains and amounts awarded on completion.</summary>
    public Dictionary<string, double> GrowthAwards { get; set; } = new();

    /// <summary>Domain affinities for scoring (domain name to weight).</summary>
    public Dictionary<string, double> DomainAffinities { get; set; } = new();

    /// <summary>Optional template IDs that can chain from this scenario.</summary>
    public List<Guid> ChainTargets { get; set; } = new();

    /// <summary>Optional content tags for metadata.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>When the template was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the template was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

// ============================================================================
// SCENARIO INSTANCE MODELS
// ============================================================================

/// <summary>
/// Storage model for an active scenario instance (Redis, keyed by scenarioInstanceId).
/// </summary>
internal class ScenarioInstanceModel
{
    /// <summary>Unique instance identifier.</summary>
    public Guid ScenarioInstanceId { get; set; }

    /// <summary>Template this instance was created from.</summary>
    public Guid ScenarioTemplateId { get; set; }

    /// <summary>Backing game session ID.</summary>
    public Guid GameSessionId { get; set; }

    /// <summary>Account playing this scenario.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Active seed for the entering player.</summary>
    public Guid SeedId { get; set; }

    /// <summary>Current scenario status.</summary>
    public ScenarioInstanceStatus Status { get; set; }

    /// <summary>When the scenario started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the last player input was received.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>When the scenario completed (null if still active).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Chain depth (0 for initial, increments on each chain).</summary>
    public int ChainDepth { get; set; }

    /// <summary>Previous scenario instance if this was chained, null otherwise.</summary>
    public Guid? PreviousScenarioInstanceId { get; set; }

    /// <summary>Bond ID if this is a bond scenario, null otherwise.</summary>
    public Guid? BondId { get; set; }

    /// <summary>Participants if bond scenario (seed IDs).</summary>
    public List<Guid>? BondParticipants { get; set; }
}

// ============================================================================
// SCENARIO HISTORY MODEL
// ============================================================================

/// <summary>
/// Storage model for completed scenario history (MySQL, durable, queryable for cooldown).
/// </summary>
internal class ScenarioHistoryModel
{
    /// <summary>Unique history entry identifier.</summary>
    public Guid HistoryId { get; set; }

    /// <summary>Original scenario instance ID.</summary>
    public Guid ScenarioInstanceId { get; set; }

    /// <summary>Template that was used.</summary>
    public Guid ScenarioTemplateId { get; set; }

    /// <summary>Account that played the scenario.</summary>
    public Guid AccountId { get; set; }

    /// <summary>How the scenario ended.</summary>
    public ScenarioOutcome Outcome { get; set; }

    /// <summary>Duration of the scenario in seconds.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>Growth awarded per domain.</summary>
    public Dictionary<string, double> GrowthAwarded { get; set; } = new();

    /// <summary>Chain depth at completion.</summary>
    public int ChainDepth { get; set; }

    /// <summary>When the scenario was completed.</summary>
    public DateTimeOffset CompletedAt { get; set; }
}

// ============================================================================
// PHASE CONFIG MODEL
// ============================================================================

/// <summary>
/// Storage model for deployment phase configuration (MySQL, durable).
/// </summary>
internal class PhaseConfigModel
{
    /// <summary>Configuration entry identifier.</summary>
    public Guid PhaseConfigId { get; set; }

    /// <summary>Current deployment phase.</summary>
    public DeploymentPhase CurrentPhase { get; set; }

    /// <summary>When the phase was last changed.</summary>
    public DateTimeOffset LastChangedAt { get; set; }

    /// <summary>Who or what triggered the phase change.</summary>
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>Total active scenario count at last check.</summary>
    public int ActiveScenarioCount { get; set; }

    /// <summary>Total unique players who have entered void.</summary>
    public int TotalVoidEntries { get; set; }

    /// <summary>Total completed scenarios across all players.</summary>
    public int TotalCompletedScenarios { get; set; }
}

// ============================================================================
// INTERNAL ENUMS (not in API schema)
// ============================================================================

/// <summary>
/// Internal status for scenario instances.
/// </summary>
internal enum ScenarioInstanceStatus
{
    /// <summary>Scenario is active and in progress.</summary>
    Active,

    /// <summary>Scenario completed successfully.</summary>
    Completed,

    /// <summary>Scenario was abandoned by the player.</summary>
    Abandoned,

    /// <summary>Scenario timed out.</summary>
    TimedOut
}

/// <summary>
/// Outcome of a completed scenario for history tracking.
/// </summary>
internal enum ScenarioOutcome
{
    /// <summary>Player completed the scenario successfully.</summary>
    Completed,

    /// <summary>Player abandoned the scenario.</summary>
    Abandoned,

    /// <summary>Scenario timed out without completion.</summary>
    TimedOut
}

// ============================================================================
// HELPER TYPES
// ============================================================================

/// <summary>
/// Internal 3D position record for void space coordinates.
/// </summary>
internal class Position3D
{
    /// <summary>X coordinate in void space.</summary>
    public double X { get; set; }

    /// <summary>Y coordinate in void space.</summary>
    public double Y { get; set; }

    /// <summary>Z coordinate in void space.</summary>
    public double Z { get; set; }

    /// <summary>Calculates distance to another position.</summary>
    public double DistanceTo(Position3D other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>
/// Scored scenario template candidate from the selection algorithm.
/// </summary>
internal record ScoredTemplate(
    ScenarioTemplateModel Template,
    double Score,
    double AffinityScore,
    double DiversityScore,
    double NarrativeScore,
    double RandomScore);
