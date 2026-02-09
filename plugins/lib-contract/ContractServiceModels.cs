using System.Text.Json;

namespace BeyondImmersion.BannouService.Contract;

/// <summary>
/// Internal data models for ContractService.
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
public partial class ContractService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal model for storing contract templates.
/// </summary>
internal class ContractTemplateModel
{
    public Guid TemplateId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? RealmId { get; set; }
    public int MinParties { get; set; }
    public int MaxParties { get; set; }
    public List<PartyRoleModel>? PartyRoles { get; set; }
    public ContractTermsModel? DefaultTerms { get; set; }
    public List<MilestoneDefinitionModel>? Milestones { get; set; }
    public EnforcementMode DefaultEnforcementMode { get; set; } = EnforcementMode.EventOnly;
    public bool Transferable { get; set; }
    public object? GameMetadata { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal model for storing party role definitions.
/// </summary>
internal class PartyRoleModel
{
    public string Role { get; set; } = string.Empty;
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
    public List<EntityType>? AllowedEntityTypes { get; set; }
}

/// <summary>
/// Internal model for storing contract terms.
/// </summary>
internal class ContractTermsModel
{
    public string? Duration { get; set; }
    public PaymentSchedule? PaymentSchedule { get; set; }
    public string? PaymentFrequency { get; set; }
    public TerminationPolicy? TerminationPolicy { get; set; }
    public string? TerminationNoticePeriod { get; set; }
    public int? BreachThreshold { get; set; }
    public string? GracePeriodForCure { get; set; }
    public Dictionary<string, object>? CustomTerms { get; set; }
}

/// <summary>
/// Internal model for storing milestone definitions.
/// </summary>
internal class MilestoneDefinitionModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Sequence { get; set; }
    public bool Required { get; set; }
    public string? Deadline { get; set; }
    public MilestoneDeadlineBehavior? DeadlineBehavior { get; set; }
    public List<PreboundApiModel>? OnComplete { get; set; }
    public List<PreboundApiModel>? OnExpire { get; set; }
}

/// <summary>
/// Internal model for storing prebound API configurations.
/// </summary>
internal class PreboundApiModel
{
    public string ServiceName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string PayloadTemplate { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PreboundApiExecutionMode ExecutionMode { get; set; } = PreboundApiExecutionMode.Sync;
    public ResponseValidation? ResponseValidation { get; set; }
}

/// <summary>
/// Internal model for storing contract instances.
/// </summary>
internal class ContractInstanceModel
{
    public Guid ContractId { get; set; }
    public Guid TemplateId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    public List<ContractPartyModel>? Parties { get; set; }
    public ContractTermsModel? Terms { get; set; }
    public List<MilestoneInstanceModel>? Milestones { get; set; }
    public int CurrentMilestoneIndex { get; set; }
    public List<Guid>? EscrowIds { get; set; }
    public List<Guid>? BreachIds { get; set; }
    public DateTimeOffset? ProposedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveUntil { get; set; }
    public DateTimeOffset? TerminatedAt { get; set; }
    public GameMetadataModel? GameMetadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Guardian state (for escrow integration)
    public Guid? GuardianId { get; set; }
    public string? GuardianType { get; set; }
    public DateTimeOffset? LockedAt { get; set; }

    // Template values for clause execution (escrow integration)
    public Dictionary<string, string>? TemplateValues { get; set; }

    // Execution tracking (idempotency for escrow integration)
    public DateTimeOffset? ExecutedAt { get; set; }
    public string? ExecutionIdempotencyKey { get; set; }
    public List<DistributionRecordModel>? ExecutionDistributions { get; set; }
}

/// <summary>
/// Internal model for storing contract party information.
/// </summary>
internal class ContractPartyModel
{
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public string Role { get; set; } = string.Empty;
    public ConsentStatus ConsentStatus { get; set; } = ConsentStatus.Pending;
    public DateTimeOffset? ConsentedAt { get; set; }
}

/// <summary>
/// Internal model for storing milestone instance state.
/// </summary>
internal class MilestoneInstanceModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Sequence { get; set; }
    public bool Required { get; set; }
    public MilestoneStatus Status { get; set; } = MilestoneStatus.Pending;
    public string? Deadline { get; set; }
    public MilestoneDeadlineBehavior? DeadlineBehavior { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public List<PreboundApiModel>? OnComplete { get; set; }
    public List<PreboundApiModel>? OnExpire { get; set; }
}

/// <summary>
/// Internal model for storing game metadata.
/// </summary>
internal class GameMetadataModel
{
    public object? InstanceData { get; set; }
    public object? RuntimeState { get; set; }
}

/// <summary>
/// Internal model for storing breach records.
/// </summary>
internal class BreachModel
{
    public Guid BreachId { get; set; }
    public Guid ContractId { get; set; }
    public Guid BreachingEntityId { get; set; }
    public EntityType BreachingEntityType { get; set; }
    public BreachType BreachType { get; set; }
    public string? BreachedTermOrMilestone { get; set; }
    public string? Description { get; set; }
    public BreachStatus Status { get; set; } = BreachStatus.Detected;
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? CureDeadline { get; set; }
    public DateTimeOffset? CuredAt { get; set; }
    public DateTimeOffset? ConsequencesAppliedAt { get; set; }
}

/// <summary>
/// Internal model for storing clause type definitions.
/// </summary>
internal class ClauseTypeModel
{
    public string TypeCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ClauseCategory Category { get; set; }
    public bool IsBuiltIn { get; set; }
    public ClauseHandlerModel? ValidationHandler { get; set; }
    public ClauseHandlerModel? ExecutionHandler { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Internal model for clause handler configuration.
/// </summary>
internal class ClauseHandlerModel
{
    public string Service { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public object? RequestMapping { get; set; }
    public object? ResponseMapping { get; set; }
}

/// <summary>
/// Internal model for storing distribution records.
/// Contract is a foundational service - no wallet/container IDs are stored.
/// </summary>
internal class DistributionRecordModel
{
    public string ClauseId { get; set; } = string.Empty;
    public string ClauseType { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public double Amount { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>
/// Parsed clause definition from template's custom terms.
/// Wraps a JsonElement to provide typed access to clause properties.
/// </summary>
internal class ClauseDefinition
{
    /// <summary>Unique clause identifier.</summary>
    public string Id { get; }

    /// <summary>Clause type code (e.g., asset_requirement, fee, distribution).</summary>
    public string Type { get; }

    private readonly JsonElement _element;

    /// <summary>
    /// Creates a new ClauseDefinition from a parsed JSON element.
    /// </summary>
    public ClauseDefinition(string id, string type, JsonElement element)
    {
        Id = id;
        Type = type;
        _element = element;
    }

    /// <summary>
    /// Gets a string property from the clause definition.
    /// </summary>
    public string? GetProperty(string name)
    {
        if (_element.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetRawText();
            }
        }
        return null;
    }

    /// <summary>
    /// Gets an array of JsonElements from the clause definition.
    /// </summary>
    public List<JsonElement> GetArray(string name)
    {
        var items = new List<JsonElement>();
        if (_element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prop.EnumerateArray())
            {
                items.Add(item.Clone());
            }
        }
        return items;
    }
}
