namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Internal data models for GardenerService.
/// </summary>
/// <remarks>
/// <para>
/// Storage models, cache entries, and internal DTOs used exclusively by this service.
/// NOT exposed via the API and NOT generated from schemas.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class GardenerService
{
    // Models defined at namespace level below.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal storage model for an active garden instance.
/// Stored in Redis with key pattern: garden:{accountId}
/// </summary>
internal class GardenInstanceModel
{
    /// <summary>
    /// Unique identifier for this garden instance.
    /// </summary>
    public Guid GardenInstanceId { get; set; }

    /// <summary>
    /// The active seed driving this garden session.
    /// </summary>
    public Guid SeedId { get; set; }

    /// <summary>
    /// Account that owns this garden instance.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Connect session ID for this garden session.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// When this garden instance was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Current player position in garden space.
    /// </summary>
    public Vec3Model Position { get; set; } = new();

    /// <summary>
    /// Current player velocity in garden space.
    /// </summary>
    public Vec3Model Velocity { get; set; } = new();

    /// <summary>
    /// IDs of currently active POIs in this garden.
    /// </summary>
    public List<Guid> ActivePoiIds { get; set; } = new();

    /// <summary>
    /// Current deployment phase for scenario gating.
    /// </summary>
    public DeploymentPhase Phase { get; set; }

    /// <summary>
    /// Recently visited scenario template IDs for diversity scoring.
    /// </summary>
    public List<Guid> ScenarioHistory { get; set; } = new();

    /// <summary>
    /// Drift metrics accumulated during this garden session.
    /// </summary>
    public DriftMetricsModel DriftMetrics { get; set; } = new();

    /// <summary>
    /// Whether this instance needs re-evaluation on the next orchestrator tick.
    /// </summary>
    public bool NeedsReEvaluation { get; set; }

    /// <summary>
    /// Current seed growth phase label (cached from Seed service).
    /// </summary>
    public string? CachedGrowthPhase { get; set; }

    /// <summary>
    /// Bond ID if this player has an active bond, for shared garden logic.
    /// </summary>
    public Guid? BondId { get; set; }
}

/// <summary>
/// Three-dimensional spatial coordinates for internal use.
/// </summary>
internal class Vec3Model
{
    /// <summary>
    /// X coordinate in garden space units.
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// Y coordinate in garden space units.
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// Z coordinate in garden space units.
    /// </summary>
    public float Z { get; set; }

    /// <summary>
    /// Calculates the Euclidean distance to another point.
    /// </summary>
    public float DistanceTo(Vec3Model other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>
/// Drift metrics tracking player movement patterns for narrative scoring.
/// </summary>
internal class DriftMetricsModel
{
    /// <summary>
    /// Total distance traveled during this garden session.
    /// </summary>
    public float TotalDistance { get; set; }

    /// <summary>
    /// Accumulated directional bias on X axis.
    /// </summary>
    public float DirectionalBiasX { get; set; }

    /// <summary>
    /// Accumulated directional bias on Y axis.
    /// </summary>
    public float DirectionalBiasY { get; set; }

    /// <summary>
    /// Accumulated directional bias on Z axis.
    /// </summary>
    public float DirectionalBiasZ { get; set; }

    /// <summary>
    /// Number of times the player stopped moving or reversed direction.
    /// </summary>
    public int HesitationCount { get; set; }

    /// <summary>
    /// Detected engagement pattern (e.g., "exploring", "hesitant", "directed").
    /// </summary>
    public string? EngagementPattern { get; set; }
}

/// <summary>
/// Internal storage model for a point of interest in a garden.
/// Stored in Redis with key pattern: poi:{gardenInstanceId}:{poiId}
/// </summary>
internal class PoiModel
{
    /// <summary>
    /// Unique identifier for this POI.
    /// </summary>
    public Guid PoiId { get; set; }

    /// <summary>
    /// Garden instance this POI belongs to.
    /// </summary>
    public Guid GardenInstanceId { get; set; }

    /// <summary>
    /// Position in garden space.
    /// </summary>
    public Vec3Model Position { get; set; } = new();

    /// <summary>
    /// Sensory presentation type.
    /// </summary>
    public PoiType PoiType { get; set; }

    /// <summary>
    /// Scenario template this POI leads to.
    /// </summary>
    public Guid ScenarioTemplateId { get; set; }

    /// <summary>
    /// Scenario category hint for client rendering.
    /// </summary>
    public ScenarioCategory? VisualHint { get; set; }

    /// <summary>
    /// Audio hint identifier for client rendering.
    /// </summary>
    public string? AudioHint { get; set; }

    /// <summary>
    /// Current intensity ramp (0.0-1.0).
    /// </summary>
    public float IntensityRamp { get; set; }

    /// <summary>
    /// How this POI is triggered by the player.
    /// </summary>
    public TriggerMode TriggerMode { get; set; }

    /// <summary>
    /// Trigger radius in garden space units.
    /// </summary>
    public float TriggerRadius { get; set; }

    /// <summary>
    /// When this POI was spawned.
    /// </summary>
    public DateTimeOffset SpawnedAt { get; set; }

    /// <summary>
    /// When this POI expires. Null means no expiration.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Current lifecycle status.
    /// </summary>
    public PoiStatus Status { get; set; }
}

/// <summary>
/// Internal storage model for a scenario template definition.
/// Stored in MySQL with key pattern: template:{scenarioTemplateId}
/// </summary>
internal class ScenarioTemplateModel
{
    /// <summary>
    /// Unique identifier for this template.
    /// </summary>
    public Guid ScenarioTemplateId { get; set; }

    /// <summary>
    /// Human-readable unique code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Display name for this template.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of this scenario.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Primary gameplay category.
    /// </summary>
    public ScenarioCategory Category { get; set; }

    /// <summary>
    /// Optional subcategory refinement.
    /// </summary>
    public string? Subcategory { get; set; }

    /// <summary>
    /// How this scenario connects to the game world.
    /// </summary>
    public ConnectivityMode ConnectivityMode { get; set; }

    /// <summary>
    /// Domain weights for growth awards on completion.
    /// </summary>
    public List<DomainWeightModel> DomainWeights { get; set; } = new();

    /// <summary>
    /// Minimum seed growth phase required to access this scenario.
    /// </summary>
    public string? MinGrowthPhase { get; set; }

    /// <summary>
    /// Estimated duration in minutes for this scenario.
    /// </summary>
    public int? EstimatedDurationMinutes { get; set; }

    /// <summary>
    /// Prerequisite requirements for entering this scenario.
    /// </summary>
    public ScenarioPrerequisitesModel? Prerequisites { get; set; }

    /// <summary>
    /// Chaining configuration for linking scenarios.
    /// </summary>
    public ScenarioChainingModel? Chaining { get; set; }

    /// <summary>
    /// Multiplayer configuration for group scenarios.
    /// </summary>
    public ScenarioMultiplayerModel? Multiplayer { get; set; }

    /// <summary>
    /// Content references linking to game assets.
    /// </summary>
    public ScenarioContentModel? Content { get; set; }

    /// <summary>
    /// Deployment phases during which this template is available.
    /// </summary>
    public List<DeploymentPhase> AllowedPhases { get; set; } = new();

    /// <summary>
    /// Maximum concurrent instances of this template allowed globally.
    /// </summary>
    public int MaxConcurrentInstances { get; set; }

    /// <summary>
    /// Current lifecycle status of this template.
    /// </summary>
    public TemplateStatus Status { get; set; }

    /// <summary>
    /// When this template was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this template was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Domain and weight pair for internal storage.
/// </summary>
internal class DomainWeightModel
{
    /// <summary>
    /// Growth domain path (e.g. "combat.melee").
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Weight applied to this domain on scenario completion.
    /// </summary>
    public float Weight { get; set; }
}

/// <summary>
/// Prerequisite requirements for internal storage.
/// </summary>
internal class ScenarioPrerequisitesModel
{
    /// <summary>
    /// Minimum growth depth per domain.
    /// </summary>
    public Dictionary<string, float>? RequiredDomains { get; set; }

    /// <summary>
    /// Scenario template codes that must be completed first.
    /// </summary>
    public List<string>? RequiredScenarios { get; set; }

    /// <summary>
    /// Scenario template codes that disqualify the player.
    /// </summary>
    public List<string>? ExcludedScenarios { get; set; }
}

/// <summary>
/// Chaining configuration for internal storage.
/// </summary>
internal class ScenarioChainingModel
{
    /// <summary>
    /// Template codes this scenario can chain into.
    /// </summary>
    public List<string>? LeadsTo { get; set; }

    /// <summary>
    /// Per-code probability weights for chain selection.
    /// </summary>
    public Dictionary<string, float>? ChainProbabilities { get; set; }

    /// <summary>
    /// Maximum chain depth from the initial scenario.
    /// </summary>
    public int MaxChainDepth { get; set; } = 3;
}

/// <summary>
/// Multiplayer configuration for internal storage.
/// </summary>
internal class ScenarioMultiplayerModel
{
    /// <summary>
    /// Minimum number of players required.
    /// </summary>
    public int MinPlayers { get; set; }

    /// <summary>
    /// Maximum number of players allowed.
    /// </summary>
    public int MaxPlayers { get; set; }

    /// <summary>
    /// Matchmaking queue code for automatic grouping.
    /// </summary>
    public string? MatchmakingQueueCode { get; set; }

    /// <summary>
    /// Whether bonded players receive a scoring boost.
    /// </summary>
    public bool BondPreferred { get; set; }
}

/// <summary>
/// Content references for internal storage.
/// </summary>
internal class ScenarioContentModel
{
    /// <summary>
    /// ABML behavior document ID for NPC orchestration.
    /// </summary>
    public Guid? BehaviorDocumentId { get; set; }

    /// <summary>
    /// Scene document ID for environment composition.
    /// </summary>
    public Guid? SceneDocumentId { get; set; }

    /// <summary>
    /// Realm ID where this scenario takes place.
    /// </summary>
    public Guid? RealmId { get; set; }

    /// <summary>
    /// Location code within the realm.
    /// </summary>
    public string? LocationCode { get; set; }
}

/// <summary>
/// Internal storage model for an active scenario instance.
/// Stored in Redis with key pattern: scenario:{accountId}
/// </summary>
internal class ScenarioInstanceModel
{
    /// <summary>
    /// Unique identifier for this scenario instance.
    /// </summary>
    public Guid ScenarioInstanceId { get; set; }

    /// <summary>
    /// Template this instance was created from.
    /// </summary>
    public Guid ScenarioTemplateId { get; set; }

    /// <summary>
    /// Backing game session ID.
    /// </summary>
    public Guid GameSessionId { get; set; }

    /// <summary>
    /// Participants in this scenario.
    /// </summary>
    public List<ScenarioParticipantModel> Participants { get; set; } = new();

    /// <summary>
    /// Connectivity mode for this instance.
    /// </summary>
    public ConnectivityMode ConnectivityMode { get; set; }

    /// <summary>
    /// Current lifecycle status.
    /// </summary>
    public ScenarioStatus Status { get; set; }

    /// <summary>
    /// When this scenario was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this scenario was completed or abandoned.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Growth awarded per domain on completion.
    /// </summary>
    public Dictionary<string, float>? GrowthAwarded { get; set; }

    /// <summary>
    /// ID of the scenario this was chained from, if any.
    /// </summary>
    public Guid? ChainedFrom { get; set; }

    /// <summary>
    /// Current chain depth (0 = root scenario).
    /// </summary>
    public int ChainDepth { get; set; }

    /// <summary>
    /// Last time a participant performed an action in this scenario.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; set; }
}

/// <summary>
/// A participant in a scenario instance.
/// </summary>
internal class ScenarioParticipantModel
{
    /// <summary>
    /// Seed ID of the participant.
    /// </summary>
    public Guid SeedId { get; set; }

    /// <summary>
    /// Account ID of the participant.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Connect session ID of the participant.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// When this participant joined the scenario.
    /// </summary>
    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>
    /// Role of this participant in the scenario.
    /// </summary>
    public ScenarioParticipantRole? Role { get; set; }
}

/// <summary>
/// Internal storage model for completed scenario history records.
/// Stored in MySQL with key pattern: history:{scenarioInstanceId}
/// </summary>
internal class ScenarioHistoryModel
{
    /// <summary>
    /// Scenario instance ID.
    /// </summary>
    public Guid ScenarioInstanceId { get; set; }

    /// <summary>
    /// Template this scenario was created from.
    /// </summary>
    public Guid ScenarioTemplateId { get; set; }

    /// <summary>
    /// Account that participated.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Seed that participated.
    /// </summary>
    public Guid SeedId { get; set; }

    /// <summary>
    /// When this scenario completed or was abandoned.
    /// </summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>
    /// Final status (Completed or Abandoned).
    /// </summary>
    public ScenarioStatus Status { get; set; }

    /// <summary>
    /// Growth awarded per domain.
    /// </summary>
    public Dictionary<string, float>? GrowthAwarded { get; set; }

    /// <summary>
    /// Total duration of the scenario in seconds.
    /// </summary>
    public float DurationSeconds { get; set; }

    /// <summary>
    /// Template code for cooldown tracking via JSON queries.
    /// Nullable when template was deleted before scenario completed.
    /// </summary>
    public string? TemplateCode { get; set; }
}

/// <summary>
/// Internal storage model for deployment phase configuration.
/// Stored in MySQL with key: phase:config (singleton).
/// </summary>
internal class DeploymentPhaseConfigModel
{
    /// <summary>
    /// Current deployment phase.
    /// </summary>
    public DeploymentPhase CurrentPhase { get; set; }

    /// <summary>
    /// Maximum concurrent scenarios globally.
    /// </summary>
    public int MaxConcurrentScenariosGlobal { get; set; }

    /// <summary>
    /// Whether persistent garden entry is enabled.
    /// </summary>
    public bool PersistentEntryEnabled { get; set; }

    /// <summary>
    /// Whether garden minigames are enabled.
    /// </summary>
    public bool GardenMinigamesEnabled { get; set; }

    /// <summary>
    /// When this configuration was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Intermediate scoring result for the scenario selection algorithm.
/// </summary>
internal class ScenarioScore
{
    /// <summary>
    /// Template being scored.
    /// </summary>
    public Guid ScenarioTemplateId { get; set; }

    /// <summary>
    /// Combined total score.
    /// </summary>
    public float TotalScore { get; set; }

    /// <summary>
    /// Score from domain affinity matching.
    /// </summary>
    public float AffinityScore { get; set; }

    /// <summary>
    /// Score from category diversity.
    /// </summary>
    public float DiversityScore { get; set; }

    /// <summary>
    /// Score from drift-pattern narrative response.
    /// </summary>
    public float NarrativeScore { get; set; }

    /// <summary>
    /// Score from randomness for discovery.
    /// </summary>
    public float RandomScore { get; set; }
}

/// <summary>
/// Shared growth calculation logic used by both GardenerService and
/// GardenerScenarioLifecycleWorker to ensure consistent growth awards.
/// </summary>
internal static class GardenerGrowthCalculation
{
    /// <summary>
    /// Calculates growth amounts per domain for a scenario based on time spent.
    /// </summary>
    /// <param name="scenario">The scenario instance to calculate growth for.</param>
    /// <param name="template">The scenario template with domain weights.</param>
    /// <param name="growthMultiplier">Global growth award multiplier from configuration.</param>
    /// <param name="fullCompletion">True for completed scenarios, false for abandoned/timed-out.</param>
    /// <param name="fullCompletionMaxRatio">Maximum time ratio cap for full completion (from configuration).</param>
    /// <param name="fullCompletionMinRatio">Minimum time ratio floor for full completion (from configuration).</param>
    /// <param name="partialMaxRatio">Maximum time ratio cap for partial completion (from configuration).</param>
    /// <param name="defaultEstimatedDurationMinutes">Fallback estimated duration when template has none (from configuration).</param>
    /// <returns>Dictionary of domain to growth amount.</returns>
    public static Dictionary<string, float> CalculateGrowth(
        ScenarioInstanceModel scenario,
        ScenarioTemplateModel? template,
        double growthMultiplier,
        bool fullCompletion,
        float fullCompletionMaxRatio,
        float fullCompletionMinRatio,
        float partialMaxRatio,
        int defaultEstimatedDurationMinutes)
    {
        var growth = new Dictionary<string, float>();

        if (template?.DomainWeights == null || template.DomainWeights.Count == 0)
            return growth;

        var durationMinutes = (float)(DateTimeOffset.UtcNow - scenario.CreatedAt).TotalMinutes;
        var estimatedMinutes = template.EstimatedDurationMinutes ?? defaultEstimatedDurationMinutes;

        float timeRatio;
        if (fullCompletion)
        {
            // Full completion: time-proportional with configured caps
            timeRatio = MathF.Min(durationMinutes / estimatedMinutes, fullCompletionMaxRatio);
            timeRatio = MathF.Max(timeRatio, fullCompletionMinRatio);
        }
        else
        {
            // Partial (abandoned/timeout): proportional to time spent with configured cap
            timeRatio = MathF.Min(durationMinutes / estimatedMinutes, partialMaxRatio);
        }

        foreach (var dw in template.DomainWeights)
        {
            var amount = (float)(dw.Weight * growthMultiplier * timeRatio);
            growth[dw.Domain] = amount;
        }

        return growth;
    }
}
