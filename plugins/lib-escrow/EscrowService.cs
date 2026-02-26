using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
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
    /// <summary>Escrow releasing event topic (transitioning to Releasing state).</summary>
    public const string EscrowReleasing = "escrow.releasing";
    /// <summary>Escrow refunding event topic (transitioning to Refunding state).</summary>
    public const string EscrowRefunding = "escrow.refunding";
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
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<EscrowService> _logger;
    private readonly EscrowServiceConfiguration _configuration;
    private readonly IEventConsumer _eventConsumer;

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
        [EscrowStatus.PendingDeposits] = new HashSet<EscrowStatus>
        {
            EscrowStatus.PartiallyFunded,
            EscrowStatus.Funded,
            EscrowStatus.Cancelled,
            EscrowStatus.Expired
        },
        [EscrowStatus.PartiallyFunded] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Funded,
            EscrowStatus.Refunding,
            EscrowStatus.Cancelled,
            EscrowStatus.Expired
        },
        [EscrowStatus.Funded] = new HashSet<EscrowStatus>
        {
            EscrowStatus.PendingConsent,
            EscrowStatus.PendingCondition,
            EscrowStatus.Disputed,
            EscrowStatus.Finalizing
        },
        [EscrowStatus.PendingConsent] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Finalizing,
            EscrowStatus.Refunding,
            EscrowStatus.Disputed,
            EscrowStatus.Expired
        },
        [EscrowStatus.PendingCondition] = new HashSet<EscrowStatus>
        {
            EscrowStatus.Finalizing,
            EscrowStatus.Refunding,
            EscrowStatus.ValidationFailed,
            EscrowStatus.Disputed,
            EscrowStatus.Expired
        },
        [EscrowStatus.ValidationFailed] = new HashSet<EscrowStatus>
        {
            EscrowStatus.PendingCondition,
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
    /// <param name="stateStoreFactory">State store factory for persistence.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="eventConsumer">Event consumer for subscription registration.</param>
    public EscrowService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<EscrowService> logger,
        EscrowServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _eventConsumer = eventConsumer;

        RegisterEventConsumers(eventConsumer);
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
        // Use cryptographically random bytes for token generation (configurable length)
        var randomBytes = new byte[_configuration.TokenLength];
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
    internal static string GetPartyPendingKey(Guid partyId, EntityType partyType)
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
    /// Atomically increments the pending escrow count for a party using optimistic concurrency.
    /// </summary>
    /// <param name="partyId">The party ID.</param>
    /// <param name="partyType">The party type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task IncrementPartyPendingCountAsync(Guid partyId, EntityType partyType, CancellationToken cancellationToken = default)
    {
        var partyKey = GetPartyPendingKey(partyId, partyType);
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (existing, etag) = await PartyPendingStore.GetWithETagAsync(partyKey, cancellationToken);
            var newCount = new PartyPendingCount
            {
                PartyId = partyId,
                PartyType = partyType,
                PendingCount = (existing?.PendingCount ?? 0) + 1,
                LastUpdated = now
            };

            // etag is null when key doesn't exist yet; empty string signals
            // "create new" to TrySaveAsync (will never conflict on new entries)
            var saveResult = await PartyPendingStore.TrySaveAsync(partyKey, newCount, etag ?? string.Empty, cancellationToken);
            if (saveResult != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on party pending count {PartyKey}, retrying increment (attempt {Attempt})",
                partyKey, attempt + 1);
        }

        _logger.LogWarning("Failed to increment party pending count for {PartyType}:{PartyId} after {MaxRetries} attempts",
            partyType, partyId, _configuration.MaxConcurrencyRetries);
    }

    /// <summary>
    /// Atomically decrements the pending escrow count for a party using optimistic concurrency.
    /// Only decrements if current count is greater than zero.
    /// </summary>
    /// <param name="partyId">The party ID.</param>
    /// <param name="partyType">The party type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task DecrementPartyPendingCountAsync(Guid partyId, EntityType partyType, CancellationToken cancellationToken = default)
    {
        var partyKey = GetPartyPendingKey(partyId, partyType);
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (existing, etag) = await PartyPendingStore.GetWithETagAsync(partyKey, cancellationToken);
            if (existing == null || existing.PendingCount <= 0)
            {
                return;
            }

            existing.PendingCount--;
            existing.LastUpdated = now;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await PartyPendingStore.TrySaveAsync(partyKey, existing, etag ?? string.Empty, cancellationToken);
            if (saveResult != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on party pending count {PartyKey}, retrying decrement (attempt {Attempt})",
                partyKey, attempt + 1);
        }

        _logger.LogWarning("Failed to decrement party pending count for {PartyType}:{PartyId} after {MaxRetries} attempts",
            partyType, partyId, _configuration.MaxConcurrencyRetries);
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
            ResolutionNotes = model.ResolutionNotes,
            ReleaseMode = model.ReleaseMode,
            RefundMode = model.RefundMode,
            ConfirmationDeadline = model.ConfirmationDeadline
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
            AssetType = model.AssetType,
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
                AssetType.ItemStack => $"{asset.ItemQuantity}x {asset.ItemTemplateName ?? "items"}",
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
