using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("lib-escrow.tests")]

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Topic constants for escrow events.
/// </summary>
public static class EscrowTopics
{
    /// <summary>Escrow created event topic.</summary>
    public const string EscrowCreated = "escrow.created";
    /// <summary>Deposit received event topic.</summary>
    public const string EscrowDepositReceived = "escrow.deposit.received";
    /// <summary>Escrow fully funded event topic.</summary>
    public const string EscrowFunded = "escrow.funded";
    /// <summary>Consent received event topic.</summary>
    public const string EscrowConsentReceived = "escrow.consent.received";
    /// <summary>Escrow finalizing event topic.</summary>
    public const string EscrowFinalizing = "escrow.finalizing";
    /// <summary>Escrow released event topic.</summary>
    public const string EscrowReleased = "escrow.released";
    /// <summary>Escrow refunded event topic.</summary>
    public const string EscrowRefunded = "escrow.refunded";
    /// <summary>Escrow disputed event topic.</summary>
    public const string EscrowDisputed = "escrow.disputed";
    /// <summary>Escrow resolved event topic.</summary>
    public const string EscrowResolved = "escrow.resolved";
    /// <summary>Escrow expired event topic.</summary>
    public const string EscrowExpired = "escrow.expired";
    /// <summary>Escrow cancelled event topic.</summary>
    public const string EscrowCancelled = "escrow.cancelled";
    /// <summary>Validation failed event topic.</summary>
    public const string EscrowValidationFailed = "escrow.validation.failed";
    /// <summary>Validation reaffirmed event topic.</summary>
    public const string EscrowValidationReaffirmed = "escrow.validation.reaffirmed";
}

/// <summary>
/// Implementation of the Escrow service.
/// This class contains the business logic for all Escrow operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS CHECKLIST:</b>
/// <list type="bullet">
///   <item><b>Type Safety:</b> Internal POCOs MUST use proper C# types (enums, Guids, DateTimeOffset) - never string representations. No Enum.Parse in business logic.</item>
///   <item><b>Configuration:</b> ALL config properties in EscrowServiceConfiguration MUST be wired up. No hardcoded magic numbers for tunables.</item>
///   <item><b>Events:</b> ALL meaningful state changes MUST publish typed events, even without current consumers.</item>
///   <item><b>Cache Stores:</b> If state-stores.yaml defines cache stores for this service, implement read-through/write-through caching.</item>
///   <item><b>Concurrency:</b> Use GetWithETagAsync + TrySaveAsync for list/index operations. No non-atomic read-modify-write.</item>
/// </list>
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Request/Response models: bannou-service/Generated/Models/EscrowModels.cs</item>
///   <item>Event models: bannou-service/Generated/Events/EscrowEventsModels.cs</item>
///   <item>Lifecycle events: bannou-service/Generated/Events/EscrowLifecycleEvents.cs</item>
///   <item>Configuration: Generated/EscrowServiceConfiguration.cs</item>
///   <item>State stores: bannou-service/Generated/StateStoreDefinitions.cs</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("escrow", typeof(IEscrowService), lifetime: ServiceLifetime.Scoped)]
public partial class EscrowService : IEscrowService
{
    private readonly IMessageBus _messageBus;
    private readonly IServiceNavigator _navigator;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<EscrowService> _logger;
    private readonly EscrowServiceConfiguration _configuration;

    #region State Store Keys

    // Key prefixes for state store operations
    private const string AGREEMENT_PREFIX = "agreement:";
    private const string TOKEN_PREFIX = "token:";
    private const string IDEMPOTENCY_PREFIX = "idemp:";
    private const string HANDLER_PREFIX = "handler:";
    private const string PARTY_PENDING_PREFIX = "party:";
    private const string STATUS_INDEX_PREFIX = "status:";
    private const string VALIDATION_PREFIX = "validate:";

    #endregion

    #region State Machine

    /// <summary>
    /// Valid state transitions for escrow status.
    /// </summary>
    private static readonly Dictionary<EscrowStatus, HashSet<EscrowStatus>> ValidTransitions = new()
    {
        [EscrowStatus.Pending_deposits] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Partially_funded,
            EscrowStatus.Funded,
            EscrowStatus.Cancelled,
            EscrowStatus.Expired
        },
        [EscrowStatus.Partially_funded] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Funded,
            EscrowStatus.Refunding,
            EscrowStatus.Cancelled,
            EscrowStatus.Expired
        },
        [EscrowStatus.Funded] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Pending_consent,
            EscrowStatus.Pending_condition,
            EscrowStatus.Disputed,
            EscrowStatus.Finalizing
        },
        [EscrowStatus.Pending_consent] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Finalizing,
            EscrowStatus.Refunding,
            EscrowStatus.Disputed,
            EscrowStatus.Expired
        },
        [EscrowStatus.Pending_condition] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Finalizing,
            EscrowStatus.Refunding,
            EscrowStatus.Validation_failed,
            EscrowStatus.Disputed,
            EscrowStatus.Expired
        },
        [EscrowStatus.Validation_failed] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Pending_condition,
            EscrowStatus.Refunding
        },
        [EscrowStatus.Finalizing] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Releasing,
            EscrowStatus.Refunding
        },
        [EscrowStatus.Releasing] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Released
        },
        [EscrowStatus.Refunding] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Refunded
        },
        [EscrowStatus.Disputed] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Releasing,
            EscrowStatus.Refunding
        },
        // Terminal states have no valid transitions
        [EscrowStatus.Released] = new HashSet<EscrowStatus>(),
        [EscrowStatus.Refunded] = new HashSet<EscrowStatus>(),
        [EscrowStatus.Expired] = new HashSet<EscrowStatus>(),
        [EscrowStatus.Cancelled] = new HashSet<EscrowStatus>()
    };

    /// <summary>
    /// Terminal states that indicate the escrow has completed.
    /// </summary>
    private static readonly HashSet<EscrowStatus> TerminalStates = new()
    {
        EscrowStatus.Released,
        EscrowStatus.Refunded,
        EscrowStatus.Expired,
        EscrowStatus.Cancelled
    };

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="EscrowService"/> class.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="navigator">Service navigator for cross-service calls.</param>
    /// <param name="stateStoreFactory">State store factory for persistence.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    public EscrowService(
        IMessageBus messageBus,
        IServiceNavigator navigator,
        IStateStoreFactory stateStoreFactory,
        ILogger<EscrowService> logger,
        EscrowServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _navigator = navigator;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
    }

    #region State Store Accessors

    private IQueryableStateStore<EscrowAgreementModel>? _agreementStore;
    private IQueryableStateStore<EscrowAgreementModel> AgreementStore =>
        _agreementStore ??= _stateStoreFactory.GetQueryableStore<EscrowAgreementModel>(StateStoreDefinitions.EscrowAgreements);

    private IStateStore<TokenHashModel>? _tokenStore;
    private IStateStore<TokenHashModel> TokenStore =>
        _tokenStore ??= _stateStoreFactory.GetStore<TokenHashModel>(StateStoreDefinitions.EscrowTokens);

    private IStateStore<IdempotencyRecord>? _idempotencyStore;
    private IStateStore<IdempotencyRecord> IdempotencyStore =>
        _idempotencyStore ??= _stateStoreFactory.GetStore<IdempotencyRecord>(StateStoreDefinitions.EscrowIdempotency);

    private IQueryableStateStore<AssetHandlerModel>? _handlerStore;
    private IQueryableStateStore<AssetHandlerModel> HandlerStore =>
        _handlerStore ??= _stateStoreFactory.GetQueryableStore<AssetHandlerModel>(StateStoreDefinitions.EscrowHandlerRegistry);

    private IStateStore<PartyPendingCount>? _partyPendingStore;
    private IStateStore<PartyPendingCount> PartyPendingStore =>
        _partyPendingStore ??= _stateStoreFactory.GetStore<PartyPendingCount>(StateStoreDefinitions.EscrowPartyPending);

    private IStateStore<StatusIndexEntry>? _statusIndexStore;
    private IStateStore<StatusIndexEntry> StatusIndexStore =>
        _statusIndexStore ??= _stateStoreFactory.GetStore<StatusIndexEntry>(StateStoreDefinitions.EscrowStatusIndex);

    private IStateStore<ValidationTrackingEntry>? _validationStore;
    private IStateStore<ValidationTrackingEntry> ValidationStore =>
        _validationStore ??= _stateStoreFactory.GetStore<ValidationTrackingEntry>(StateStoreDefinitions.EscrowActiveValidation);

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates that a status transition is allowed.
    /// </summary>
    /// <param name="currentStatus">Current escrow status.</param>
    /// <param name="newStatus">Target status.</param>
    /// <returns>True if transition is valid.</returns>
    internal static bool IsValidTransition(EscrowStatus currentStatus, EscrowStatus newStatus)
    {
        return ValidTransitions.TryGetValue(currentStatus, out var validTargets) &&
                validTargets.Contains(newStatus);
    }

    /// <summary>
    /// Checks if an escrow is in a terminal state.
    /// </summary>
    /// <param name="status">Status to check.</param>
    /// <returns>True if terminal.</returns>
    internal static bool IsTerminalState(EscrowStatus status)
    {
        return TerminalStates.Contains(status);
    }

    /// <summary>
    /// Generates a cryptographic token for escrow operations.
    /// </summary>
    /// <param name="escrowId">Escrow ID.</param>
    /// <param name="partyId">Party ID.</param>
    /// <param name="tokenType">Type of token.</param>
    /// <returns>Generated token string.</returns>
    internal string GenerateToken(Guid escrowId, Guid partyId, TokenType tokenType)
    {
        // Use cryptographically random bytes for token generation
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        // Combine with escrow context for uniqueness
        var contextBytes = Encoding.UTF8.GetBytes($"{escrowId}:{partyId}:{tokenType}");
        var combinedBytes = new byte[randomBytes.Length + contextBytes.Length];
        Buffer.BlockCopy(randomBytes, 0, combinedBytes, 0, randomBytes.Length);
        Buffer.BlockCopy(contextBytes, 0, combinedBytes, randomBytes.Length, contextBytes.Length);

        return Convert.ToBase64String(SHA256.HashData(combinedBytes));
    }

    /// <summary>
    /// Hashes a token for secure storage.
    /// </summary>
    /// <param name="token">Plain token.</param>
    /// <returns>Hashed token.</returns>
    internal static string HashToken(string token)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Gets the agreement key for state store.
    /// </summary>
    /// <param name="escrowId">Escrow ID.</param>
    /// <returns>State store key.</returns>
    internal static string GetAgreementKey(Guid escrowId)
    {
        return $"{AGREEMENT_PREFIX}{escrowId}";
    }

    /// <summary>
    /// Gets the token key for state store.
    /// </summary>
    /// <param name="tokenHash">Hashed token.</param>
    /// <returns>State store key.</returns>
    internal static string GetTokenKey(string tokenHash)
    {
        return $"{TOKEN_PREFIX}{tokenHash}";
    }

    /// <summary>
    /// Gets the idempotency key for state store.
    /// </summary>
    /// <param name="key">Idempotency key.</param>
    /// <returns>State store key.</returns>
    internal static string GetIdempotencyKey(string key)
    {
        return $"{IDEMPOTENCY_PREFIX}{key}";
    }

    /// <summary>
    /// Gets the handler key for state store.
    /// </summary>
    /// <param name="assetType">Custom asset type.</param>
    /// <returns>State store key.</returns>
    internal static string GetHandlerKey(string assetType)
    {
        return $"{HANDLER_PREFIX}{assetType}";
    }

    /// <summary>
    /// Gets the party pending count key.
    /// </summary>
    /// <param name="partyId">Party ID.</param>
    /// <param name="partyType">Party type.</param>
    /// <returns>State store key.</returns>
    internal static string GetPartyPendingKey(Guid partyId, string partyType)
    {
        return $"{PARTY_PENDING_PREFIX}{partyType}:{partyId}";
    }

    /// <summary>
    /// Gets the status index key.
    /// </summary>
    /// <param name="status">Escrow status.</param>
    /// <returns>State store key.</returns>
    internal static string GetStatusIndexKey(EscrowStatus status)
    {
        return $"{STATUS_INDEX_PREFIX}{status}";
    }

    /// <summary>
    /// Gets the validation tracking key.
    /// </summary>
    /// <param name="escrowId">Escrow ID.</param>
    /// <returns>State store key.</returns>
    internal static string GetValidationKey(Guid escrowId)
    {
        return $"{VALIDATION_PREFIX}{escrowId}";
    }

    /// <summary>
    /// Maps internal model to API model.
    /// </summary>
    /// <param name="model">Internal model.</param>
    /// <returns>API model.</returns>
    internal static EscrowAgreement MapToApiModel(EscrowAgreementModel model)
    {
        return new EscrowAgreement
        {
            Id = model.EscrowId,
            EscrowType = model.EscrowType,
            TrustMode = model.TrustMode,
            TrustedPartyId = model.TrustedPartyId,
            TrustedPartyType = model.TrustedPartyType,
            InitiatorServiceId = model.InitiatorServiceId,
            Parties = model.Parties?.Select(MapPartyToApiModel).ToList()
                ?? new List<EscrowParty>(),
            ExpectedDeposits = model.ExpectedDeposits?.Select(MapExpectedDepositToApiModel).ToList()
                ?? new List<ExpectedDeposit>(),
            Deposits = model.Deposits?.Select(MapDepositToApiModel).ToList()
                ?? new List<EscrowDeposit>(),
            ReleaseAllocations = model.ReleaseAllocations?.Select(MapAllocationToApiModel).ToList(),
            BoundContractId = model.BoundContractId,
            Consents = model.Consents?.Select(MapConsentToApiModel).ToList()
                ?? new List<EscrowConsent>(),
            Status = model.Status,
            RequiredConsentsForRelease = model.RequiredConsentsForRelease,
            LastValidatedAt = model.LastValidatedAt,
            ValidationFailures = model.ValidationFailures?.Select(MapValidationFailureToApiModel).ToList(),
            CreatedAt = model.CreatedAt,
            CreatedBy = model.CreatedBy,
            CreatedByType = model.CreatedByType,
            FundedAt = model.FundedAt,
            ExpiresAt = model.ExpiresAt,
            CompletedAt = model.CompletedAt,
            ReferenceType = model.ReferenceType,
            ReferenceId = model.ReferenceId,
            Description = model.Description,
            Metadata = model.Metadata,
            Resolution = model.Resolution,
            ResolutionNotes = model.ResolutionNotes
        };
    }

    /// <summary>
    /// Maps internal party model to API model.
    /// </summary>
    internal static EscrowParty MapPartyToApiModel(EscrowPartyModel model)
    {
        return new EscrowParty
        {
            PartyId = model.PartyId,
            PartyType = model.PartyType,
            DisplayName = model.DisplayName,
            Role = model.Role,
            ConsentRequired = model.ConsentRequired,
            WalletId = model.WalletId,
            ContainerId = model.ContainerId,
            EscrowWalletId = model.EscrowWalletId,
            EscrowContainerId = model.EscrowContainerId,
            DepositToken = model.DepositToken,
            DepositTokenUsed = model.DepositTokenUsed,
            DepositTokenUsedAt = model.DepositTokenUsedAt,
            ReleaseToken = model.ReleaseToken,
            ReleaseTokenUsed = model.ReleaseTokenUsed,
            ReleaseTokenUsedAt = model.ReleaseTokenUsedAt
        };
    }

    /// <summary>
    /// Maps internal expected deposit model to API model.
    /// </summary>
    internal static ExpectedDeposit MapExpectedDepositToApiModel(ExpectedDepositModel model)
    {
        return new ExpectedDeposit
        {
            PartyId = model.PartyId,
            PartyType = model.PartyType,
            ExpectedAssets = model.ExpectedAssets?.Select(MapAssetToApiModel).ToList()
                ?? new List<EscrowAsset>(),
            Optional = model.Optional,
            DepositDeadline = model.DepositDeadline,
            Fulfilled = model.Fulfilled
        };
    }

    /// <summary>
    /// Maps internal deposit model to API model.
    /// </summary>
    internal static EscrowDeposit MapDepositToApiModel(EscrowDepositModel model)
    {
        return new EscrowDeposit
        {
            Id = model.DepositId,
            EscrowId = model.EscrowId,
            PartyId = model.PartyId,
            PartyType = model.PartyType,
            Assets = MapAssetBundleToApiModel(model.Assets),
            DepositedAt = model.DepositedAt,
            DepositTokenUsed = model.DepositTokenUsed,
            IdempotencyKey = model.IdempotencyKey
        };
    }

    /// <summary>
    /// Maps internal asset bundle model to API model.
    /// </summary>
    internal static EscrowAssetBundle MapAssetBundleToApiModel(EscrowAssetBundleModel model)
    {
        return new EscrowAssetBundle
        {
            BundleId = model.BundleId,
            Assets = model.Assets?.Select(MapAssetToApiModel).ToList()
                ?? new List<EscrowAsset>(),
            Description = model.Description,
            EstimatedValue = model.EstimatedValue
        };
    }

    /// <summary>
    /// Maps internal asset model to API model.
    /// </summary>
    internal static EscrowAsset MapAssetToApiModel(EscrowAssetModel model)
    {
        return new EscrowAsset
        {
            AssetType = model.AssetType,
            CurrencyDefinitionId = model.CurrencyDefinitionId,
            CurrencyCode = model.CurrencyCode,
            CurrencyAmount = model.CurrencyAmount,
            ItemInstanceId = model.ItemInstanceId,
            ItemName = model.ItemName,
            ItemTemplateId = model.ItemTemplateId,
            ItemTemplateName = model.ItemTemplateName,
            ItemQuantity = model.ItemQuantity,
            ContractInstanceId = model.ContractInstanceId,
            ContractTemplateCode = model.ContractTemplateCode,
            ContractDescription = model.ContractDescription,
            ContractPartyRole = model.ContractPartyRole,
            CustomAssetType = model.CustomAssetType,
            CustomAssetId = model.CustomAssetId,
            CustomAssetData = model.CustomAssetData,
            SourceOwnerId = model.SourceOwnerId,
            SourceOwnerType = model.SourceOwnerType,
            SourceContainerId = model.SourceContainerId
        };
    }

    /// <summary>
    /// Maps internal allocation model to API model.
    /// </summary>
    internal static ReleaseAllocation MapAllocationToApiModel(ReleaseAllocationModel model)
    {
        return new ReleaseAllocation
        {
            RecipientPartyId = model.RecipientPartyId,
            RecipientPartyType = model.RecipientPartyType,
            Assets = model.Assets?.Select(MapAssetToApiModel).ToList()
                ?? new List<EscrowAsset>(),
            DestinationWalletId = model.DestinationWalletId,
            DestinationContainerId = model.DestinationContainerId
        };
    }

    /// <summary>
    /// Maps internal consent model to API model.
    /// </summary>
    internal static EscrowConsent MapConsentToApiModel(EscrowConsentModel model)
    {
        return new EscrowConsent
        {
            PartyId = model.PartyId,
            PartyType = model.PartyType,
            ConsentType = model.ConsentType,
            ConsentedAt = model.ConsentedAt,
            ReleaseTokenUsed = model.ReleaseTokenUsed,
            Notes = model.Notes
        };
    }

    /// <summary>
    /// Maps internal validation failure model to API model.
    /// </summary>
    internal static ValidationFailure MapValidationFailureToApiModel(ValidationFailureModel model)
    {
        return new ValidationFailure
        {
            DetectedAt = model.DetectedAt,
            AssetType = model.AssetType.ToString(),
            AssetDescription = model.AssetDescription,
            FailureType = model.FailureType,
            AffectedPartyId = model.AffectedPartyId,
            AffectedPartyType = model.AffectedPartyType,
            Details = model.Details
        };
    }

    /// <summary>
    /// Emits an error event for unexpected failures.
    /// </summary>
    /// <param name="operation">Operation name.</param>
    /// <param name="error">Error message.</param>
    /// <param name="context">Additional context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task EmitErrorAsync(string operation, string error, object? context = null, CancellationToken cancellationToken = default)
    {
        _logger.LogError("Escrow operation {Operation} failed: {Error}", operation, error);
        await _messageBus.TryPublishErrorAsync(
            "escrow",
            operation,
            error,
            error,
            details: context,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Generates an asset summary string for events.
    /// </summary>
    /// <param name="assets">List of assets.</param>
    /// <returns>Human-readable summary.</returns>
    internal static string GenerateAssetSummary(IEnumerable<EscrowAssetModel>? assets)
    {
        if (assets == null || !assets.Any())
        {
            return "No assets";
        }

        var summaryParts = new List<string>();
        foreach (var asset in assets)
        {
            var part = asset.AssetType switch
            {
                AssetType.Currency => $"{asset.CurrencyAmount} {asset.CurrencyCode ?? "currency"}",
                AssetType.Item => asset.ItemName ?? "item",
                AssetType.Item_stack => $"{asset.ItemQuantity}x {asset.ItemTemplateName ?? "items"}",
                AssetType.Contract => asset.ContractTemplateCode ?? "contract",
                AssetType.Custom => asset.CustomAssetType ?? "custom asset",
                _ => "unknown asset"
            };
            summaryParts.Add(part);
        }

        return string.Join(", ", summaryParts);
    }

    #endregion
}

#region Internal Models - IMPLEMENTATION TENETS (Type Safety)

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
    public string? TrustedPartyType { get; set; }
    public string? InitiatorServiceId { get; set; }
    public List<EscrowPartyModel>? Parties { get; set; }
    public List<ExpectedDepositModel>? ExpectedDeposits { get; set; }
    public List<EscrowDepositModel>? Deposits { get; set; }
    public List<ReleaseAllocationModel>? ReleaseAllocations { get; set; }
    public Guid? BoundContractId { get; set; }
    public List<EscrowConsentModel>? Consents { get; set; }
    public EscrowStatus Status { get; set; } = EscrowStatus.Pending_deposits;
    public int RequiredConsentsForRelease { get; set; } = -1;
    public DateTimeOffset? LastValidatedAt { get; set; }
    public List<ValidationFailureModel>? ValidationFailures { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public string CreatedByType { get; set; } = string.Empty;
    public DateTimeOffset? FundedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Description { get; set; }
    public object? Metadata { get; set; }
    public EscrowResolution? Resolution { get; set; }
    public string? ResolutionNotes { get; set; }
}

/// <summary>
/// Internal model for storing escrow party information.
/// </summary>
internal class EscrowPartyModel
{
    public Guid PartyId { get; set; }
    public string PartyType { get; set; } = string.Empty;
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
    public string PartyType { get; set; } = string.Empty;
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
    public string PartyType { get; set; } = string.Empty;
    public EscrowAssetBundleModel Assets { get; set; } = new();
    public DateTimeOffset DepositedAt { get; set; }
    public string? DepositTokenUsed { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
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
    public string SourceOwnerType { get; set; } = string.Empty;
    public Guid? SourceContainerId { get; set; }
}

/// <summary>
/// Internal model for storing release allocations.
/// </summary>
internal class ReleaseAllocationModel
{
    public Guid RecipientPartyId { get; set; }
    public string RecipientPartyType { get; set; } = string.Empty;
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
    public string PartyType { get; set; } = string.Empty;
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
    public string AssetDescription { get; set; } = string.Empty;
    public ValidationFailureType FailureType { get; set; }
    public Guid AffectedPartyId { get; set; }
    public string AffectedPartyType { get; set; } = string.Empty;
    public object? Details { get; set; }
}

/// <summary>
/// Internal model for storing token hash lookups.
/// </summary>
internal class TokenHashModel
{
    public string TokenHash { get; set; } = string.Empty;
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
    public string Key { get; set; } = string.Empty;
    public Guid EscrowId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public object? Result { get; set; }
}

/// <summary>
/// Internal model for asset handler registration.
/// </summary>
internal class AssetHandlerModel
{
    public string AssetType { get; set; } = string.Empty;
    public string PluginId { get; set; } = string.Empty;
    public bool BuiltIn { get; set; }
    public string DepositEndpoint { get; set; } = string.Empty;
    public string ReleaseEndpoint { get; set; } = string.Empty;
    public string RefundEndpoint { get; set; } = string.Empty;
    public string ValidateEndpoint { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>
/// Internal model for tracking pending escrow counts per party.
/// </summary>
internal class PartyPendingCount
{
    public Guid PartyId { get; set; }
    public string PartyType { get; set; } = string.Empty;
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

#endregion
