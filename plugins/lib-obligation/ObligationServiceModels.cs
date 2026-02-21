namespace BeyondImmersion.BannouService.Obligation;

/// <summary>
/// Internal data models for ObligationService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class ObligationService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Cached obligation manifest per character, stored in Redis.
/// Contains pre-computed violation cost map keyed by violation type code.
/// Rebuilt from active contracts on contract lifecycle events.
/// </summary>
internal class ObligationManifestModel
{
    /// <summary>Character this manifest belongs to.</summary>
    public Guid CharacterId { get; set; }

    /// <summary>Active obligation entries derived from contract behavioral clauses.</summary>
    public List<ObligationEntryModel> Obligations { get; set; } = new();

    /// <summary>Pre-computed violation cost map keyed by violation type code (aggregated base penalties).</summary>
    public Dictionary<string, float> ViolationCostMap { get; set; } = new();

    /// <summary>Number of active contracts with behavioral clauses.</summary>
    public int TotalActiveContracts { get; set; }

    /// <summary>When the cache was last rebuilt.</summary>
    public DateTimeOffset LastRefreshedAt { get; set; }
}

/// <summary>
/// Single obligation entry derived from a contract behavioral clause.
/// </summary>
internal class ObligationEntryModel
{
    /// <summary>Contract this obligation originates from.</summary>
    public Guid ContractId { get; set; }

    /// <summary>Template code of the originating contract.</summary>
    public string TemplateCode { get; set; } = string.Empty;

    /// <summary>Behavioral clause code within the contract.</summary>
    public string ClauseCode { get; set; } = string.Empty;

    /// <summary>Violation type code (opaque string, e.g., "theft", "deception").</summary>
    public string ViolationType { get; set; } = string.Empty;

    /// <summary>Base penalty cost for violating this obligation.</summary>
    public float BasePenalty { get; set; }

    /// <summary>Human-readable description of the obligation.</summary>
    public string? Description { get; set; }

    /// <summary>Role of the character in the contract that created this obligation.</summary>
    public string ContractRole { get; set; } = string.Empty;

    /// <summary>When the originating contract expires (null = no expiration).</summary>
    public DateTimeOffset? EffectiveUntil { get; set; }
}

/// <summary>
/// Action mapping model stored in MySQL. Maps GOAP action tags to violation type codes.
/// </summary>
internal class ActionMappingModel
{
    /// <summary>GOAP action tag (e.g., "attack_surrendered_enemy").</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Violation type codes this action maps to (e.g., ["honor_combat", "show_mercy"]).</summary>
    public List<string> ViolationTypes { get; set; } = new();

    /// <summary>Human-readable description of why this mapping exists.</summary>
    public string? Description { get; set; }

    /// <summary>When this mapping was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this mapping was last updated (null if never updated after creation).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Violation record stored in MySQL. Represents a knowing obligation violation.
/// </summary>
internal class ViolationRecordModel
{
    /// <summary>Unique identifier for this violation.</summary>
    public Guid ViolationId { get; set; }

    /// <summary>Character who committed the violation.</summary>
    public Guid CharacterId { get; set; }

    /// <summary>Contract that was violated.</summary>
    public Guid ContractId { get; set; }

    /// <summary>Behavioral clause code that was violated.</summary>
    public string ClauseCode { get; set; } = string.Empty;

    /// <summary>Violation type code (e.g., "theft", "deception").</summary>
    public string ViolationType { get; set; } = string.Empty;

    /// <summary>GOAP action tag that triggered the violation.</summary>
    public string ActionTag { get; set; } = string.Empty;

    /// <summary>Goal urgency that overrode the obligation (0.0-1.0).</summary>
    public float MotivationScore { get; set; }

    /// <summary>Total violation cost that was accepted.</summary>
    public float ViolationCost { get; set; }

    /// <summary>Whether a breach was filed with the contract service.</summary>
    public bool BreachReported { get; set; }

    /// <summary>Breach record ID from contract service (null if not reported).</summary>
    public Guid? BreachId { get; set; }

    /// <summary>Target entity of the violating action.</summary>
    public Guid? TargetEntityId { get; set; }

    /// <summary>Entity type of the target.</summary>
    public EntityType? TargetEntityType { get; set; }

    /// <summary>When the violation occurred.</summary>
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// Idempotency entry stored in Redis with TTL for violation report deduplication.
/// </summary>
internal class IdempotencyEntry
{
    /// <summary>The violation ID that was created for this idempotency key.</summary>
    public Guid ViolationId { get; set; }
}

/// <summary>
/// Parsed behavioral clause from a contract's CustomTerms dictionary.
/// </summary>
internal record BehavioralClause(
    string ClauseCode,
    string ViolationType,
    float BasePenalty,
    string? Description);
