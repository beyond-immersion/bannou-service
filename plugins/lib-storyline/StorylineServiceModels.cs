using BeyondImmersion.Bannou.StorylineStoryteller.Planning;
using BeyondImmersion.Bannou.StorylineTheory.Arcs;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;

namespace BeyondImmersion.BannouService.Storyline;

/// <summary>
/// Internal data models for StorylineService.
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
public partial class StorylineService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal model for cached storyline plans.
/// </summary>
internal sealed class CachedPlan
{
    public required Guid PlanId { get; init; }
    public required StorylineGoal Goal { get; init; }
    public required ArcType ArcType { get; init; }
    public required SpectrumType PrimarySpectrum { get; init; }
    public string? Genre { get; init; }
    public double Confidence { get; init; }
    public List<StorylinePlanPhase>? Phases { get; init; }
    public List<EntityRequirement>? EntitiesToSpawn { get; init; }
    public List<StorylineLink>? Links { get; init; }
    public List<StorylineRisk>? Risks { get; init; }
    public List<string>? Themes { get; init; }
    public Guid? RealmId { get; init; }
    public List<Guid>? ArchiveIds { get; init; }
    public List<Guid>? SnapshotIds { get; init; }
    public int? Seed { get; init; }
    public int GenerationTimeMs { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Internal model for plan index entries.
/// </summary>
internal sealed class PlanIndexEntry
{
    public required Guid PlanId { get; init; }
    public required Guid RealmId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Internal storage model for scenario definitions (MySQL-backed).
/// </summary>
internal sealed class ScenarioDefinitionModel
{
    public required Guid ScenarioId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string TriggerConditionsJson { get; set; }
    public required string PhasesJson { get; set; }
    public string? MutationsJson { get; set; }
    public string? QuestHooksJson { get; set; }
    public int? CooldownSeconds { get; set; }
    public string? ExclusivityTagsJson { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public Guid? RealmId { get; set; }
    public Guid? GameServiceId { get; set; }
    public string? TagsJson { get; set; }
    public bool Deprecated { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public required string Etag { get; set; }
}

/// <summary>
/// Internal storage model for scenario executions (MySQL-backed history).
/// </summary>
internal sealed class ScenarioExecutionModel
{
    public required Guid ExecutionId { get; init; }
    public required Guid ScenarioId { get; init; }
    public required string ScenarioCode { get; init; }
    public required string ScenarioName { get; init; }
    public required Guid PrimaryCharacterId { get; init; }
    public string? AdditionalParticipantsJson { get; set; }
    public Guid? OrchestratorId { get; set; }
    public Guid? RealmId { get; set; }
    public Guid? GameServiceId { get; set; }
    public required ScenarioStatus Status { get; set; }
    public int CurrentPhase { get; set; }
    public int TotalPhases { get; set; }
    public double? FitScore { get; set; }
    public string? MutationsAppliedJson { get; set; }
    public string? QuestsSpawnedJson { get; set; }
    public string? FailureReason { get; set; }
    public required DateTimeOffset TriggeredAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Internal marker for cooldown tracking with TTL.
/// </summary>
internal sealed class CooldownMarker
{
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Internal marker for idempotency tracking with TTL.
/// </summary>
internal sealed class IdempotencyMarker
{
    public required Guid ExecutionId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Internal model for tracking active scenarios per character.
/// </summary>
internal sealed class ActiveScenarioEntry
{
    public required Guid ExecutionId { get; init; }
    public required Guid ScenarioId { get; init; }
    public required string ScenarioCode { get; init; }
}
