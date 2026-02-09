namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Internal data models for EscrowService.
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
public partial class EscrowService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal model for storing escrow agreements.
/// Uses proper C# types per IMPLEMENTATION TENETS (Type Safety).
/// </summary>
internal class EscrowAgreementModel
{
    public Guid EscrowId { get; set; }
    public EscrowType EscrowType { get; set; }
    public EscrowTrustMode TrustMode { get; set; }
    public Guid? TrustedPartyId { get; set; }
    public EntityType? TrustedPartyType { get; set; }
    public string? InitiatorServiceId { get; set; }
    public List<EscrowPartyModel>? Parties { get; set; }
    public List<ExpectedDepositModel>? ExpectedDeposits { get; set; }
    public List<EscrowDepositModel>? Deposits { get; set; }
    public List<ReleaseAllocationModel>? ReleaseAllocations { get; set; }
    public Guid? BoundContractId { get; set; }
    public List<EscrowConsentModel>? Consents { get; set; }
    public EscrowStatus Status { get; set; } = EscrowStatus.PendingDeposits;
    public int RequiredConsentsForRelease { get; set; } = -1;
    public DateTimeOffset? LastValidatedAt { get; set; }
    public List<ValidationFailureModel>? ValidationFailures { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public EntityType CreatedByType { get; set; }
    public DateTimeOffset? FundedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Description { get; set; }
    public object? Metadata { get; set; }
    public EscrowResolution? Resolution { get; set; }
    public string? ResolutionNotes { get; set; }
    public ReleaseMode ReleaseMode { get; set; } = ReleaseMode.ServiceOnly;
    public RefundMode RefundMode { get; set; } = RefundMode.Immediate;
    public List<ReleaseConfirmationModel>? ReleaseConfirmations { get; set; }
    public DateTimeOffset? ConfirmationDeadline { get; set; }
}

/// <summary>
/// Internal model for storing escrow party information.
/// </summary>
internal class EscrowPartyModel
{
    public Guid PartyId { get; set; }
    public EntityType PartyType { get; set; }
    public string? DisplayName { get; set; }
    public EscrowPartyRole Role { get; set; }
    public bool ConsentRequired { get; set; }
    public Guid? WalletId { get; set; }
    public Guid? ContainerId { get; set; }
    public Guid? EscrowWalletId { get; set; }
    public Guid? EscrowContainerId { get; set; }
    public string? DepositToken { get; set; }
    public bool DepositTokenUsed { get; set; }
    public DateTimeOffset? DepositTokenUsedAt { get; set; }
    public string? ReleaseToken { get; set; }
    public bool ReleaseTokenUsed { get; set; }
    public DateTimeOffset? ReleaseTokenUsedAt { get; set; }
}

/// <summary>
/// Internal model for storing expected deposit definitions.
/// </summary>
internal class ExpectedDepositModel
{
    public Guid PartyId { get; set; }
    public EntityType PartyType { get; set; }
    public List<EscrowAssetModel>? ExpectedAssets { get; set; }
    public bool Optional { get; set; }
    public DateTimeOffset? DepositDeadline { get; set; }
    public bool Fulfilled { get; set; }
}

/// <summary>
/// Internal model for storing actual deposit records.
/// </summary>
internal class EscrowDepositModel
{
    public Guid DepositId { get; set; }
    public Guid EscrowId { get; set; }
    public Guid PartyId { get; set; }
    public EntityType PartyType { get; set; }
    public EscrowAssetBundleModel Assets { get; set; } = new();
    public DateTimeOffset DepositedAt { get; set; }
    public string? DepositTokenUsed { get; set; }
    public required string IdempotencyKey { get; set; }
}

/// <summary>
/// Internal model for storing asset bundles.
/// </summary>
internal class EscrowAssetBundleModel
{
    public Guid BundleId { get; set; }
    public List<EscrowAssetModel>? Assets { get; set; }
    public string? Description { get; set; }
    public double? EstimatedValue { get; set; }
}

/// <summary>
/// Internal model for storing individual assets.
/// Uses proper C# types per IMPLEMENTATION TENETS (Type Safety).
/// </summary>
internal class EscrowAssetModel
{
    public AssetType AssetType { get; set; }
    public Guid? CurrencyDefinitionId { get; set; }
    public string? CurrencyCode { get; set; }
    public double? CurrencyAmount { get; set; }
    public Guid? ItemInstanceId { get; set; }
    public string? ItemName { get; set; }
    public Guid? ItemTemplateId { get; set; }
    public string? ItemTemplateName { get; set; }
    public int? ItemQuantity { get; set; }
    public Guid? ContractInstanceId { get; set; }
    public string? ContractTemplateCode { get; set; }
    public string? ContractDescription { get; set; }
    public string? ContractPartyRole { get; set; }
    public string? CustomAssetType { get; set; }
    public string? CustomAssetId { get; set; }
    public object? CustomAssetData { get; set; }
    public Guid SourceOwnerId { get; set; }
    public EntityType SourceOwnerType { get; set; }
    public Guid? SourceContainerId { get; set; }
}

/// <summary>
/// Internal model for storing release allocations.
/// </summary>
internal class ReleaseAllocationModel
{
    public Guid RecipientPartyId { get; set; }
    public EntityType RecipientPartyType { get; set; }
    public List<EscrowAssetModel>? Assets { get; set; }
    public Guid? DestinationWalletId { get; set; }
    public Guid? DestinationContainerId { get; set; }
}

/// <summary>
/// Internal model for storing consent records.
/// </summary>
internal class EscrowConsentModel
{
    public Guid PartyId { get; set; }
    public EntityType PartyType { get; set; }
    public EscrowConsentType ConsentType { get; set; }
    public DateTimeOffset ConsentedAt { get; set; }
    public string? ReleaseTokenUsed { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Internal model for storing validation failures.
/// Uses proper C# types per IMPLEMENTATION TENETS (Type Safety).
/// </summary>
internal class ValidationFailureModel
{
    public DateTimeOffset DetectedAt { get; set; }
    public AssetType AssetType { get; set; }
    public required string AssetDescription { get; set; }
    public ValidationFailureType FailureType { get; set; }
    public Guid AffectedPartyId { get; set; }
    public EntityType AffectedPartyType { get; set; }
    public object? Details { get; set; }
}

/// <summary>
/// Internal model for storing token hash lookups.
/// </summary>
internal class TokenHashModel
{
    public required string TokenHash { get; set; }
    public Guid EscrowId { get; set; }
    public Guid PartyId { get; set; }
    public TokenType TokenType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Used { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>
/// Internal model for idempotency tracking.
/// </summary>
internal class IdempotencyRecord
{
    public required string Key { get; set; }
    public Guid EscrowId { get; set; }
    public Guid PartyId { get; set; }
    public required string Operation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public object? Result { get; set; }
}

/// <summary>
/// Internal model for asset handler registration.
/// </summary>
internal class AssetHandlerModel
{
    public required string AssetType { get; set; }
    public required string PluginId { get; set; }
    public bool BuiltIn { get; set; }
    public required string DepositEndpoint { get; set; }
    public required string ReleaseEndpoint { get; set; }
    public required string RefundEndpoint { get; set; }
    public required string ValidateEndpoint { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>
/// Internal model for tracking pending escrow counts per party.
/// </summary>
internal class PartyPendingCount
{
    public Guid PartyId { get; set; }
    public EntityType PartyType { get; set; }
    public int PendingCount { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>
/// Internal model for status index entries.
/// </summary>
internal class StatusIndexEntry
{
    public Guid EscrowId { get; set; }
    public EscrowStatus Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>
/// Internal model for validation tracking.
/// </summary>
internal class ValidationTrackingEntry
{
    public Guid EscrowId { get; set; }
    public DateTimeOffset LastValidatedAt { get; set; }
    public DateTimeOffset NextValidationDue { get; set; }
    public int FailedValidationCount { get; set; }
}

/// <summary>
/// Internal model for tracking release/refund confirmations per party.
/// </summary>
internal class ReleaseConfirmationModel
{
    public Guid PartyId { get; set; }
    public EntityType PartyType { get; set; }
    public bool ServiceConfirmed { get; set; }
    public bool PartyConfirmed { get; set; }
    public DateTimeOffset? ServiceConfirmedAt { get; set; }
    public DateTimeOffset? PartyConfirmedAt { get; set; }
}
