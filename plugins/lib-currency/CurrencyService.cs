using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Xml;

[assembly: InternalsVisibleTo("lib-currency.tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace BeyondImmersion.BannouService.Currency;

/// <summary>
/// Implementation of the Currency service.
/// Provides multi-currency management with wallets, balances, autogain,
/// earn/wallet caps, conversion, escrow integration, and authorization holds.
/// </summary>
[BannouService("currency", typeof(ICurrencyService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class CurrencyService : ICurrencyService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<CurrencyService> _logger;
    private readonly CurrencyServiceConfiguration _configuration;
    private readonly IDistributedLockProvider _lockProvider;

    // State store key prefixes
    private const string DEF_PREFIX = "def:";
    private const string DEF_CODE_INDEX = "def-code:";
    private const string ALL_DEFS_KEY = "all-defs";
    private const string WALLET_PREFIX = "wallet:";
    private const string WALLET_OWNER_INDEX = "wallet-owner:";
    private const string BALANCE_PREFIX = "bal:";
    private const string BALANCE_WALLET_INDEX = "bal-wallet:";
    private const string BALANCE_CURRENCY_INDEX = "bal-currency:";
    private const string TX_PREFIX = "tx:";
    private const string TX_WALLET_INDEX = "tx-wallet:";
    private const string TX_REF_INDEX = "tx-ref:";
    private const string HOLD_PREFIX = "hold:";
    private const string HOLD_WALLET_INDEX = "hold-wallet:";

    /// <summary>
    /// Initializes a new instance of the CurrencyService.
    /// </summary>
    public CurrencyService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<CurrencyService> logger,
        CurrencyServiceConfiguration configuration,
        IDistributedLockProvider lockProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _lockProvider = lockProvider;
    }

    #region Currency Definition Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, CurrencyDefinitionResponse?)> CreateCurrencyDefinitionAsync(
        CreateCurrencyDefinitionRequest body,
        CancellationToken cancellationToken = default)
    {
            _logger.LogInformation("Creating currency definition with code: {Code}", body.Code);

            var store = _stateStoreFactory.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions);
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions);

            // Check code uniqueness
            var existingId = await stringStore.GetAsync($"{DEF_CODE_INDEX}{body.Code}", cancellationToken);
            if (!string.IsNullOrEmpty(existingId))
            {
                _logger.LogWarning("Currency code already exists: {Code}", body.Code);
                return (StatusCodes.Conflict, null);
            }

            // Check base currency uniqueness if marking as base
            if (body.IsBaseCurrency)
            {
                var allDefsJson = await stringStore.GetAsync(ALL_DEFS_KEY, cancellationToken);
                if (!string.IsNullOrEmpty(allDefsJson))
                {
                    var allIds = BannouJson.Deserialize<List<string>>(allDefsJson) ?? new List<string>();
                    foreach (var id in allIds)
                    {
                        var existing = await store.GetAsync($"{DEF_PREFIX}{id}", cancellationToken);
                        if (existing is not null && existing.IsBaseCurrency && existing.Scope == body.Scope)
                        {
                            _logger.LogWarning("Base currency already exists for scope: {Scope}", body.Scope);
                            return (StatusCodes.Conflict, null);
                        }
                    }
                }
            }

            var definitionId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new CurrencyDefinitionModel
            {
                DefinitionId = definitionId,
                Code = body.Code,
                Name = body.Name,
                Description = body.Description,
                Scope = body.Scope,
                RealmsAvailable = body.RealmsAvailable?.ToList(),
                Precision = body.Precision,
                Transferable = body.Transferable,
                Tradeable = body.Tradeable,
                AllowNegative = body.AllowNegative,
                PerWalletCap = body.PerWalletCap,
                CapOverflowBehavior = body.CapOverflowBehavior,
                GlobalSupplyCap = body.GlobalSupplyCap,
                DailyEarnCap = body.DailyEarnCap,
                WeeklyEarnCap = body.WeeklyEarnCap,
                EarnCapResetTime = body.EarnCapResetTime,
                AutogainEnabled = body.AutogainEnabled,
                AutogainMode = body.AutogainMode,
                AutogainAmount = body.AutogainAmount,
                AutogainInterval = body.AutogainInterval,
                AutogainCap = body.AutogainCap,
                Expires = body.Expires,
                ExpirationPolicy = body.ExpirationPolicy,
                ExpirationDate = body.ExpirationDate,
                ExpirationDuration = body.ExpirationDuration,
                SeasonId = body.SeasonId,
                LinkedToItem = body.LinkedToItem,
                LinkedItemTemplateId = body.LinkedItemTemplateId,
                LinkageMode = body.LinkageMode,
                IsBaseCurrency = body.IsBaseCurrency,
                ExchangeRateToBase = body.ExchangeRateToBase,
                IconAssetId = body.IconAssetId,
                DisplayFormat = body.DisplayFormat,
                IsActive = true,
                CreatedAt = now,
                ModifiedAt = now
            };

            await store.SaveAsync($"{DEF_PREFIX}{definitionId}", model, cancellationToken: cancellationToken);
            await stringStore.SaveAsync($"{DEF_CODE_INDEX}{body.Code}", definitionId.ToString(), cancellationToken: cancellationToken);
            await AddToListAsync(StateStoreDefinitions.CurrencyDefinitions, ALL_DEFS_KEY, definitionId.ToString(), cancellationToken);

            await _messageBus.TryPublishAsync("currency-definition.created", new CurrencyDefinitionCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                DefinitionId = definitionId,
                Code = model.Code,
                Name = model.Name,
                Scope = model.Scope,
                Precision = model.Precision,
                IsActive = model.IsActive,
                CreatedAt = now
            }, cancellationToken);

            _logger.LogInformation("Created currency definition: {DefinitionId} code={Code}", definitionId, body.Code);
            return (StatusCodes.OK, MapDefinitionToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CurrencyDefinitionResponse?)> GetCurrencyDefinitionAsync(
        GetCurrencyDefinitionRequest body,
        CancellationToken cancellationToken = default)
    {
            var model = await ResolveCurrencyDefinitionAsync(body.DefinitionId?.ToString(), body.Code, cancellationToken);
            if (model is null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapDefinitionToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListCurrencyDefinitionsResponse?)> ListCurrencyDefinitionsAsync(
        ListCurrencyDefinitionsRequest body,
        CancellationToken cancellationToken = default)
    {
            var store = _stateStoreFactory.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions);
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions);

            var allDefsJson = await stringStore.GetAsync(ALL_DEFS_KEY, cancellationToken);
            var allIds = string.IsNullOrEmpty(allDefsJson)
                ? new List<string>()
                : BannouJson.Deserialize<List<string>>(allDefsJson) ?? new List<string>();

            var definitions = new List<CurrencyDefinitionResponse>();
            foreach (var id in allIds)
            {
                var model = await store.GetAsync($"{DEF_PREFIX}{id}", cancellationToken);
                if (model is null) continue;
                if (!body.IncludeInactive && !model.IsActive) continue;
                if (body.Scope is not null && model.Scope != body.Scope) continue;
                if (body.IsBaseCurrency is not null && model.IsBaseCurrency != body.IsBaseCurrency) continue;
                if (body.RealmId is not null)
                {
                    if (model.Scope == CurrencyScope.Global) { /* global is always available */ }
                    else if (model.RealmsAvailable is null || !model.RealmsAvailable.Contains(body.RealmId.Value)) continue;
                }

                definitions.Add(MapDefinitionToResponse(model));
            }

            return (StatusCodes.OK, new ListCurrencyDefinitionsResponse { Definitions = definitions });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CurrencyDefinitionResponse?)> UpdateCurrencyDefinitionAsync(
        UpdateCurrencyDefinitionRequest body,
        CancellationToken cancellationToken = default)
    {
            var store = _stateStoreFactory.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions);
            var key = $"{DEF_PREFIX}{body.DefinitionId}";
            var model = await store.GetAsync(key, cancellationToken);

            if (model is null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Apply mutable field updates
            if (body.Name is not null) model.Name = body.Name;
            if (body.Description is not null) model.Description = body.Description;
            if (body.Transferable is not null) model.Transferable = body.Transferable.Value;
            if (body.Tradeable is not null) model.Tradeable = body.Tradeable.Value;
            if (body.AllowNegative is not null) model.AllowNegative = body.AllowNegative;
            if (body.PerWalletCap is not null) model.PerWalletCap = body.PerWalletCap;
            if (body.CapOverflowBehavior is not null) model.CapOverflowBehavior = body.CapOverflowBehavior;
            if (body.DailyEarnCap is not null) model.DailyEarnCap = body.DailyEarnCap;
            if (body.WeeklyEarnCap is not null) model.WeeklyEarnCap = body.WeeklyEarnCap;
            if (body.AutogainEnabled is not null) model.AutogainEnabled = body.AutogainEnabled.Value;
            if (body.AutogainMode is not null) model.AutogainMode = body.AutogainMode;
            if (body.AutogainAmount is not null) model.AutogainAmount = body.AutogainAmount;
            if (body.AutogainInterval is not null) model.AutogainInterval = body.AutogainInterval;
            if (body.AutogainCap is not null) model.AutogainCap = body.AutogainCap;
            if (body.ExchangeRateToBase is not null)
            {
                model.ExchangeRateToBase = body.ExchangeRateToBase;
                model.ExchangeRateUpdatedAt = DateTimeOffset.UtcNow;
            }
            if (body.IconAssetId is not null) model.IconAssetId = body.IconAssetId;
            if (body.DisplayFormat is not null) model.DisplayFormat = body.DisplayFormat;
            if (body.IsActive is not null) model.IsActive = body.IsActive.Value;
            model.ModifiedAt = DateTimeOffset.UtcNow;

            await store.SaveAsync(key, model, cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync("currency-definition.updated", new CurrencyDefinitionUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                DefinitionId = model.DefinitionId,
                Code = model.Code,
                Name = model.Name,
                Scope = model.Scope,
                Precision = model.Precision,
                IsActive = model.IsActive,
                CreatedAt = model.CreatedAt,
                ModifiedAt = model.ModifiedAt
            }, cancellationToken);

            return (StatusCodes.OK, MapDefinitionToResponse(model));
    }

    #endregion

    #region Wallet Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, WalletResponse?)> CreateWalletAsync(
        CreateWalletRequest body,
        CancellationToken cancellationToken = default)
    {
            var store = _stateStoreFactory.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets);
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyWallets);

            var ownerKey = BuildOwnerKey(body.OwnerId.ToString(), body.OwnerType.ToString(), body.RealmId?.ToString());
            var existingId = await stringStore.GetAsync($"{WALLET_OWNER_INDEX}{ownerKey}", cancellationToken);
            if (!string.IsNullOrEmpty(existingId))
            {
                return (StatusCodes.Conflict, null);
            }

            var walletId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new WalletModel
            {
                WalletId = walletId,
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType,
                RealmId = body.RealmId,
                Status = WalletStatus.Active,
                CreatedAt = now,
                LastActivityAt = now
            };

            await store.SaveAsync($"{WALLET_PREFIX}{walletId}", model, cancellationToken: cancellationToken);
            await stringStore.SaveAsync($"{WALLET_OWNER_INDEX}{ownerKey}", walletId.ToString(), cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync("currency-wallet.created", new CurrencyWalletCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                WalletId = walletId,
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType,
                RealmId = body.RealmId,
                Status = WalletStatus.Active,
                CreatedAt = now
            }, cancellationToken);

            return (StatusCodes.OK, MapWalletToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, WalletWithBalancesResponse?)> GetWalletAsync(
        GetWalletRequest body,
        CancellationToken cancellationToken = default)
    {
            var wallet = await ResolveWalletAsync(body.WalletId?.ToString(), body.OwnerId?.ToString(),
                body.OwnerType?.ToString(), body.RealmId?.ToString(), cancellationToken);

            if (wallet is null)
            {
                return (StatusCodes.NotFound, null);
            }

            var balances = await GetWalletBalanceSummariesAsync(wallet.WalletId.ToString(), cancellationToken);

            return (StatusCodes.OK, new WalletWithBalancesResponse
            {
                Wallet = MapWalletToResponse(wallet),
                Balances = balances
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GetOrCreateWalletResponse?)> GetOrCreateWalletAsync(
        GetOrCreateWalletRequest body,
        CancellationToken cancellationToken = default)
    {
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyWallets);
            var ownerKey = BuildOwnerKey(body.OwnerId.ToString(), body.OwnerType.ToString(), body.RealmId?.ToString());
            var existingId = await stringStore.GetAsync($"{WALLET_OWNER_INDEX}{ownerKey}", cancellationToken);

            if (!string.IsNullOrEmpty(existingId))
            {
                var store = _stateStoreFactory.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets);
                var existing = await store.GetAsync($"{WALLET_PREFIX}{existingId}", cancellationToken);
                if (existing is not null)
                {
                    var balances = await GetWalletBalanceSummariesAsync(existing.WalletId.ToString(), cancellationToken);
                    return (StatusCodes.OK, new GetOrCreateWalletResponse
                    {
                        Wallet = MapWalletToResponse(existing),
                        Balances = balances,
                        Created = false
                    });
                }
            }

            // Create new wallet
            var (status, walletResp) = await CreateWalletAsync(new CreateWalletRequest
            {
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType,
                RealmId = body.RealmId
            }, cancellationToken);

            if (status != StatusCodes.OK || walletResp is null)
            {
                return (status, null);
            }

            return (StatusCodes.OK, new GetOrCreateWalletResponse
            {
                Wallet = walletResp,
                Balances = new List<BalanceSummary>(),
                Created = true
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, WalletResponse?)> FreezeWalletAsync(
        FreezeWalletRequest body,
        CancellationToken cancellationToken = default)
    {
            var store = _stateStoreFactory.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets);
            var key = $"{WALLET_PREFIX}{body.WalletId}";
            var (wallet, etag) = await store.GetWithETagAsync(key, cancellationToken);

            if (wallet is null) return (StatusCodes.NotFound, null);
            if (wallet.Status == WalletStatus.Frozen) return (StatusCodes.Conflict, null);

            wallet.Status = WalletStatus.Frozen;
            wallet.FrozenReason = body.Reason;
            wallet.FrozenAt = DateTimeOffset.UtcNow;

            // GetWithETagAsync returns non-null etag when entity exists (checked above);
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var newEtag = await store.TrySaveAsync(key, wallet, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected freezing wallet {WalletId}", body.WalletId);
                return (StatusCodes.Conflict, null);
            }

            await _messageBus.TryPublishAsync("currency-wallet.frozen", new CurrencyWalletFrozenEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                WalletId = body.WalletId,
                OwnerId = wallet.OwnerId,
                Reason = body.Reason
            }, cancellationToken);

            return (StatusCodes.OK, MapWalletToResponse(wallet));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, WalletResponse?)> UnfreezeWalletAsync(
        UnfreezeWalletRequest body,
        CancellationToken cancellationToken = default)
    {
            var store = _stateStoreFactory.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets);
            var key = $"{WALLET_PREFIX}{body.WalletId}";
            var (wallet, etag) = await store.GetWithETagAsync(key, cancellationToken);

            if (wallet is null) return (StatusCodes.NotFound, null);
            if (wallet.Status != WalletStatus.Frozen) return (StatusCodes.BadRequest, null);

            wallet.Status = WalletStatus.Active;
            wallet.FrozenReason = null;
            wallet.FrozenAt = null;

            // GetWithETagAsync returns non-null etag when entity exists (checked above);
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var newEtag = await store.TrySaveAsync(key, wallet, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected unfreezing wallet {WalletId}", body.WalletId);
                return (StatusCodes.Conflict, null);
            }

            await _messageBus.TryPublishAsync("currency-wallet.unfrozen", new CurrencyWalletUnfrozenEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                WalletId = body.WalletId,
                OwnerId = wallet.OwnerId
            }, cancellationToken);

            return (StatusCodes.OK, MapWalletToResponse(wallet));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CloseWalletResponse?)> CloseWalletAsync(
        CloseWalletRequest body,
        CancellationToken cancellationToken = default)
    {
            var store = _stateStoreFactory.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets);
            var walletKey = $"{WALLET_PREFIX}{body.WalletId}";
            var (wallet, walletEtag) = await store.GetWithETagAsync(walletKey, cancellationToken);

            if (wallet is null) return (StatusCodes.NotFound, null);
            if (wallet.Status == WalletStatus.Closed) return (StatusCodes.BadRequest, null);

            var destKey = $"{WALLET_PREFIX}{body.TransferRemainingTo}";
            var destWallet = await store.GetAsync(destKey, cancellationToken);
            if (destWallet is null) return (StatusCodes.NotFound, null);

            // Acquire wallet lock to prevent concurrent close/transfer operations
            await using var walletLock = await _lockProvider.LockAsync(
                "currency-wallet", body.WalletId.ToString(), Guid.NewGuid().ToString(), _configuration.WalletLockTimeoutSeconds, cancellationToken);
            if (!walletLock.Success)
            {
                _logger.LogWarning("Could not acquire wallet lock for close on wallet {WalletId}", body.WalletId);
                return (StatusCodes.Conflict, null);
            }

            // Verify no active holds exist before closing
            var balances = await GetAllBalancesForWalletAsync(wallet.WalletId.ToString(), cancellationToken);
            foreach (var balance in balances)
            {
                var heldAmount = await GetTotalHeldAmountAsync(wallet.WalletId.ToString(), balance.CurrencyDefinitionId.ToString(), cancellationToken);
                if (heldAmount > 0)
                {
                    _logger.LogWarning("Cannot close wallet {WalletId}: active holds exist for currency {CurrencyId} (held: {HeldAmount})",
                        body.WalletId, balance.CurrencyDefinitionId, heldAmount);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // Transfer remaining balances
            var transferredBalances = new List<TransferredBalance>();
            foreach (var balance in balances)
            {
                if (balance.Amount > 0)
                {
                    await InternalCreditAsync(body.TransferRemainingTo.ToString(), balance.CurrencyDefinitionId.ToString(),
                        balance.Amount, TransactionType.Transfer, "wallet_close", body.WalletId,
                        $"close-{body.WalletId}-{balance.CurrencyDefinitionId}", cancellationToken);

                    transferredBalances.Add(new TransferredBalance
                    {
                        CurrencyDefinitionId = balance.CurrencyDefinitionId,
                        Amount = balance.Amount
                    });
                }
            }

            wallet.Status = WalletStatus.Closed;
            // GetWithETagAsync returns non-null etag when entity exists (checked above);
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var newWalletEtag = await store.TrySaveAsync(walletKey, wallet, walletEtag ?? string.Empty, cancellationToken);
            if (newWalletEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected closing wallet {WalletId}", body.WalletId);
                return (StatusCodes.Conflict, null);
            }

            await _messageBus.TryPublishAsync("currency-wallet.closed", new CurrencyWalletClosedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                WalletId = body.WalletId,
                OwnerId = wallet.OwnerId,
                BalancesTransferredTo = body.TransferRemainingTo
            }, cancellationToken);

            return (StatusCodes.OK, new CloseWalletResponse
            {
                Wallet = MapWalletToResponse(wallet),
                TransferredBalances = transferredBalances
            });
    }

    #endregion

    #region Balance Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, GetBalanceResponse?)> GetBalanceAsync(
        GetBalanceRequest body,
        CancellationToken cancellationToken = default)
    {
            var wallet = await GetWalletByIdAsync(body.WalletId.ToString(), cancellationToken);
            if (wallet is null) return (StatusCodes.NotFound, null);

            var definition = await GetDefinitionByIdAsync(body.CurrencyDefinitionId.ToString(), cancellationToken);
            if (definition is null) return (StatusCodes.NotFound, null);

            var balance = await GetOrCreateBalanceAsync(body.WalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);

            // Apply lazy autogain if enabled
            if (definition.AutogainEnabled)
            {
                balance = await ApplyAutogainIfNeededAsync(balance, definition, wallet, cancellationToken);
            }

            // Reset earn caps if needed
            ResetEarnCapsIfNeeded(balance);

            var lockedAmount = await GetTotalHeldAmountAsync(body.WalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);

            return (StatusCodes.OK, new GetBalanceResponse
            {
                WalletId = body.WalletId,
                CurrencyDefinitionId = body.CurrencyDefinitionId,
                CurrencyCode = definition.Code,
                Amount = balance.Amount,
                LockedAmount = lockedAmount,
                EffectiveAmount = balance.Amount - lockedAmount,
                EarnCapInfo = BuildEarnCapInfo(balance, definition),
                AutogainInfo = BuildAutogainInfo(balance, definition)
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BatchGetBalancesResponse?)> BatchGetBalancesAsync(
        BatchGetBalancesRequest body,
        CancellationToken cancellationToken = default)
    {
            var results = new List<BatchBalanceResult>();
            foreach (var query in body.Queries)
            {
                var balance = await GetOrCreateBalanceAsync(query.WalletId.ToString(), query.CurrencyDefinitionId.ToString(), cancellationToken);
                var definition = await GetDefinitionByIdAsync(query.CurrencyDefinitionId.ToString(), cancellationToken);

                if (definition is not null && definition.AutogainEnabled)
                {
                    var wallet = await GetWalletByIdAsync(query.WalletId.ToString(), cancellationToken);
                    if (wallet is not null)
                    {
                        balance = await ApplyAutogainIfNeededAsync(balance, definition, wallet, cancellationToken);
                    }
                }

                var lockedAmount = await GetTotalHeldAmountAsync(query.WalletId.ToString(), query.CurrencyDefinitionId.ToString(), cancellationToken);

                results.Add(new BatchBalanceResult
                {
                    WalletId = query.WalletId,
                    CurrencyDefinitionId = query.CurrencyDefinitionId,
                    Amount = balance.Amount,
                    LockedAmount = lockedAmount,
                    EffectiveAmount = balance.Amount - lockedAmount
                });
            }

            return (StatusCodes.OK, new BatchGetBalancesResponse { Balances = results });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CreditCurrencyResponse?)> CreditCurrencyAsync(
        CreditCurrencyRequest body,
        CancellationToken cancellationToken = default)
    {
            if (body.Amount <= 0) return (StatusCodes.BadRequest, null);

            // Idempotency check
            var (isDuplicate, existingTxId) = await CheckIdempotencyAsync(body.IdempotencyKey, cancellationToken);
            if (isDuplicate) return (StatusCodes.Conflict, null);

            var wallet = await GetWalletByIdAsync(body.WalletId.ToString(), cancellationToken);
            if (wallet is null) return (StatusCodes.NotFound, null);
            if (wallet.Status == WalletStatus.Frozen) return ((StatusCodes)422, null);

            var definition = await GetDefinitionByIdAsync(body.CurrencyDefinitionId.ToString(), cancellationToken);
            if (definition is null) return (StatusCodes.NotFound, null);

            // Acquire distributed lock for atomic balance modification
            var balanceLockKey = $"{body.WalletId}:{body.CurrencyDefinitionId}";
            await using var lockResponse = await _lockProvider.LockAsync(
                "currency-balance", balanceLockKey, Guid.NewGuid().ToString(), _configuration.BalanceLockTimeoutSeconds, cancellationToken);
            if (!lockResponse.Success)
            {
                _logger.LogWarning("Could not acquire balance lock for wallet {WalletId}, currency {CurrencyId}", body.WalletId, body.CurrencyDefinitionId);
                return (StatusCodes.Conflict, null);
            }

            var balance = await GetOrCreateBalanceAsync(body.WalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);
            ResetEarnCapsIfNeeded(balance);

            var creditAmount = body.Amount;
            var earnCapApplied = false;
            double earnCapAmountLimited = 0;
            var walletCapApplied = false;
            double walletCapAmountLost = 0;

            // Earn cap enforcement
            if (!body.BypassEarnCap && (definition.DailyEarnCap is not null || definition.WeeklyEarnCap is not null))
            {
                var cappedAmount = ApplyEarnCap(creditAmount, balance, definition);
                if (cappedAmount < creditAmount)
                {
                    earnCapApplied = true;
                    earnCapAmountLimited = creditAmount - cappedAmount;
                    creditAmount = cappedAmount;

                    await _messageBus.TryPublishAsync("currency.earn_cap.reached", new CurrencyEarnCapReachedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        WalletId = body.WalletId,
                        OwnerId = wallet.OwnerId,
                        CurrencyDefinitionId = body.CurrencyDefinitionId,
                        CurrencyCode = definition.Code,
                        CapType = definition.DailyEarnCap is not null ? EarnCapType.Daily : EarnCapType.Weekly,
                        CapAmount = definition.DailyEarnCap ?? definition.WeeklyEarnCap ?? 0,
                        AttemptedAmount = body.Amount,
                        LimitedAmount = creditAmount
                    }, cancellationToken);
                }
            }

            if (creditAmount <= 0)
            {
                return ((StatusCodes)422, null);
            }

            // Wallet cap enforcement
            if (definition.PerWalletCap is not null)
            {
                var newBalance = balance.Amount + creditAmount;
                if (newBalance > definition.PerWalletCap.Value)
                {
                    var behavior = definition.CapOverflowBehavior ?? CapOverflowBehavior.Reject;
                    if (behavior == CapOverflowBehavior.Reject)
                    {
                        return ((StatusCodes)422, null);
                    }

                    var overflow = newBalance - definition.PerWalletCap.Value;
                    walletCapApplied = true;
                    walletCapAmountLost = overflow;
                    creditAmount -= overflow;

                    await _messageBus.TryPublishAsync("currency.wallet_cap.reached", new CurrencyWalletCapReachedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        WalletId = body.WalletId,
                        OwnerId = wallet.OwnerId,
                        CurrencyDefinitionId = body.CurrencyDefinitionId,
                        CurrencyCode = definition.Code,
                        CapAmount = definition.PerWalletCap.Value,
                        OverflowBehavior = behavior,
                        AmountLost = overflow
                    }, cancellationToken);
                }
            }

            var balanceBefore = balance.Amount;
            balance.Amount += creditAmount;
            balance.DailyEarned += creditAmount;
            balance.WeeklyEarned += creditAmount;
            balance.LastModifiedAt = DateTimeOffset.UtcNow;

            await SaveBalanceAsync(balance, cancellationToken);

            var transaction = await RecordTransactionAsync(null, body.WalletId.ToString(),
                body.CurrencyDefinitionId.ToString(), creditAmount, body.TransactionType,
                body.ReferenceType, body.ReferenceId?.ToString(), null, body.IdempotencyKey,
                null, balanceBefore, null, balance.Amount, body.Metadata, cancellationToken);

            await RecordIdempotencyAsync(body.IdempotencyKey, transaction.TransactionId.ToString(), cancellationToken);

            await _messageBus.TryPublishAsync("currency.credited", new CurrencyCreditedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TransactionId = transaction.TransactionId,
                WalletId = body.WalletId,
                OwnerId = wallet.OwnerId,
                OwnerType = wallet.OwnerType,
                CurrencyDefinitionId = body.CurrencyDefinitionId,
                CurrencyCode = definition.Code,
                Amount = creditAmount,
                TransactionType = body.TransactionType,
                NewBalance = balance.Amount,
                ReferenceType = body.ReferenceType,
                ReferenceId = body.ReferenceId,
                EarnCapApplied = earnCapApplied,
                WalletCapApplied = walletCapApplied
            }, cancellationToken);

            return (StatusCodes.OK, new CreditCurrencyResponse
            {
                Transaction = MapTransactionToRecord(transaction),
                NewBalance = balance.Amount,
                EarnCapApplied = earnCapApplied,
                EarnCapAmountLimited = earnCapApplied ? earnCapAmountLimited : null,
                WalletCapApplied = walletCapApplied,
                WalletCapAmountLost = walletCapApplied ? walletCapAmountLost : null
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, DebitCurrencyResponse?)> DebitCurrencyAsync(
        DebitCurrencyRequest body,
        CancellationToken cancellationToken = default)
    {
            if (body.Amount <= 0) return (StatusCodes.BadRequest, null);

            var (isDuplicate, _) = await CheckIdempotencyAsync(body.IdempotencyKey, cancellationToken);
            if (isDuplicate) return (StatusCodes.Conflict, null);

            var wallet = await GetWalletByIdAsync(body.WalletId.ToString(), cancellationToken);
            if (wallet is null) return (StatusCodes.NotFound, null);
            if (wallet.Status == WalletStatus.Frozen) return ((StatusCodes)422, null);

            var definition = await GetDefinitionByIdAsync(body.CurrencyDefinitionId.ToString(), cancellationToken);
            if (definition is null) return (StatusCodes.NotFound, null);

            // Acquire distributed lock for atomic balance modification
            var balanceLockKey = $"{body.WalletId}:{body.CurrencyDefinitionId}";
            await using var lockResponse = await _lockProvider.LockAsync(
                "currency-balance", balanceLockKey, Guid.NewGuid().ToString(), _configuration.BalanceLockTimeoutSeconds, cancellationToken);
            if (!lockResponse.Success)
            {
                _logger.LogWarning("Could not acquire balance lock for wallet {WalletId}, currency {CurrencyId}", body.WalletId, body.CurrencyDefinitionId);
                return (StatusCodes.Conflict, null);
            }

            var balance = await GetOrCreateBalanceAsync(body.WalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);

            // Check sufficient funds (accounting for active holds)
            var allowNegative = IsNegativeAllowed(definition, body.AllowNegative);
            if (!allowNegative)
            {
                var heldAmount = await GetTotalHeldAmountAsync(body.WalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);
                if (balance.Amount - heldAmount < body.Amount)
                {
                    return ((StatusCodes)422, null);
                }
            }

            var balanceBefore = balance.Amount;
            balance.Amount -= body.Amount;
            balance.LastModifiedAt = DateTimeOffset.UtcNow;

            await SaveBalanceAsync(balance, cancellationToken);

            var transaction = await RecordTransactionAsync(body.WalletId.ToString(), null,
                body.CurrencyDefinitionId.ToString(), body.Amount, body.TransactionType,
                body.ReferenceType, body.ReferenceId?.ToString(), null, body.IdempotencyKey,
                balanceBefore, balance.Amount, null, null, body.Metadata, cancellationToken);

            await RecordIdempotencyAsync(body.IdempotencyKey, transaction.TransactionId.ToString(), cancellationToken);

            await _messageBus.TryPublishAsync("currency.debited", new CurrencyDebitedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TransactionId = transaction.TransactionId,
                WalletId = body.WalletId,
                OwnerId = wallet.OwnerId,
                OwnerType = wallet.OwnerType,
                CurrencyDefinitionId = body.CurrencyDefinitionId,
                CurrencyCode = definition.Code,
                Amount = body.Amount,
                TransactionType = body.TransactionType,
                NewBalance = balance.Amount,
                ReferenceType = body.ReferenceType,
                ReferenceId = body.ReferenceId
            }, cancellationToken);

            return (StatusCodes.OK, new DebitCurrencyResponse
            {
                Transaction = MapTransactionToRecord(transaction),
                NewBalance = balance.Amount
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, TransferCurrencyResponse?)> TransferCurrencyAsync(
        TransferCurrencyRequest body,
        CancellationToken cancellationToken = default)
    {
            if (body.Amount <= 0) return (StatusCodes.BadRequest, null);

            var (isDuplicate, _) = await CheckIdempotencyAsync(body.IdempotencyKey, cancellationToken);
            if (isDuplicate) return (StatusCodes.Conflict, null);

            var sourceWallet = await GetWalletByIdAsync(body.SourceWalletId.ToString(), cancellationToken);
            if (sourceWallet is null) return (StatusCodes.NotFound, null);
            if (sourceWallet.Status == WalletStatus.Frozen) return ((StatusCodes)422, null);

            var targetWallet = await GetWalletByIdAsync(body.TargetWalletId.ToString(), cancellationToken);
            if (targetWallet is null) return (StatusCodes.NotFound, null);
            if (targetWallet.Status == WalletStatus.Frozen) return ((StatusCodes)422, null);

            var definition = await GetDefinitionByIdAsync(body.CurrencyDefinitionId.ToString(), cancellationToken);
            if (definition is null) return (StatusCodes.NotFound, null);
            if (!definition.Transferable) return (StatusCodes.BadRequest, null);

            // Acquire distributed locks for both wallets in deterministic order to prevent deadlock
            var sourceLockKey = $"{body.SourceWalletId}:{body.CurrencyDefinitionId}";
            var targetLockKey = $"{body.TargetWalletId}:{body.CurrencyDefinitionId}";
            var firstLockKey = string.CompareOrdinal(sourceLockKey, targetLockKey) <= 0 ? sourceLockKey : targetLockKey;
            var secondLockKey = string.CompareOrdinal(sourceLockKey, targetLockKey) <= 0 ? targetLockKey : sourceLockKey;

            await using var firstLock = await _lockProvider.LockAsync(
                "currency-balance", firstLockKey, Guid.NewGuid().ToString(), _configuration.BalanceLockTimeoutSeconds, cancellationToken);
            if (!firstLock.Success)
            {
                _logger.LogWarning("Could not acquire first balance lock for transfer {FirstKey}", firstLockKey);
                return (StatusCodes.Conflict, null);
            }

            await using var secondLock = await _lockProvider.LockAsync(
                "currency-balance", secondLockKey, Guid.NewGuid().ToString(), _configuration.BalanceLockTimeoutSeconds, cancellationToken);
            if (!secondLock.Success)
            {
                _logger.LogWarning("Could not acquire second balance lock for transfer {SecondKey}", secondLockKey);
                return (StatusCodes.Conflict, null);
            }

            var sourceBalance = await GetOrCreateBalanceAsync(body.SourceWalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);
            var sourceHeldAmount = await GetTotalHeldAmountAsync(body.SourceWalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);
            if (sourceBalance.Amount - sourceHeldAmount < body.Amount)
            {
                return ((StatusCodes)422, null);
            }

            var targetBalance = await GetOrCreateBalanceAsync(body.TargetWalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);

            var sourceBalanceBefore = sourceBalance.Amount;
            var targetBalanceBefore = targetBalance.Amount;

            sourceBalance.Amount -= body.Amount;
            sourceBalance.LastModifiedAt = DateTimeOffset.UtcNow;

            var creditAmount = body.Amount;
            var targetCapApplied = false;
            double targetCapAmountLost = 0;

            // Apply wallet cap on target
            if (definition.PerWalletCap is not null)
            {
                var newTargetBalance = targetBalance.Amount + creditAmount;
                if (newTargetBalance > definition.PerWalletCap.Value)
                {
                    var behavior = definition.CapOverflowBehavior ?? CapOverflowBehavior.Reject;
                    if (behavior == CapOverflowBehavior.Reject)
                    {
                        return ((StatusCodes)422, null);
                    }
                    var overflow = newTargetBalance - definition.PerWalletCap.Value;
                    targetCapApplied = true;
                    targetCapAmountLost = overflow;
                    creditAmount -= overflow;
                }
            }

            targetBalance.Amount += creditAmount;
            targetBalance.LastModifiedAt = DateTimeOffset.UtcNow;

            await SaveBalanceAsync(sourceBalance, cancellationToken);
            await SaveBalanceAsync(targetBalance, cancellationToken);

            var transaction = await RecordTransactionAsync(body.SourceWalletId.ToString(), body.TargetWalletId.ToString(),
                body.CurrencyDefinitionId.ToString(), body.Amount, body.TransactionType,
                body.ReferenceType, body.ReferenceId?.ToString(), null, body.IdempotencyKey,
                sourceBalanceBefore, sourceBalance.Amount, targetBalanceBefore, targetBalance.Amount,
                body.Metadata, cancellationToken);

            await RecordIdempotencyAsync(body.IdempotencyKey, transaction.TransactionId.ToString(), cancellationToken);

            await _messageBus.TryPublishAsync("currency.transferred", new CurrencyTransferredEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TransactionId = transaction.TransactionId,
                SourceWalletId = body.SourceWalletId,
                SourceOwnerId = sourceWallet.OwnerId,
                SourceOwnerType = sourceWallet.OwnerType,
                TargetWalletId = body.TargetWalletId,
                TargetOwnerId = targetWallet.OwnerId,
                TargetOwnerType = targetWallet.OwnerType,
                CurrencyDefinitionId = body.CurrencyDefinitionId,
                CurrencyCode = definition.Code,
                Amount = body.Amount,
                TransactionType = body.TransactionType
            }, cancellationToken);

            return (StatusCodes.OK, new TransferCurrencyResponse
            {
                Transaction = MapTransactionToRecord(transaction),
                SourceNewBalance = sourceBalance.Amount,
                TargetNewBalance = targetBalance.Amount,
                TargetCapApplied = targetCapApplied,
                TargetCapAmountLost = targetCapApplied ? targetCapAmountLost : null
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BatchCreditResponse?)> BatchCreditCurrencyAsync(
        BatchCreditRequest body,
        CancellationToken cancellationToken = default)
    {
            var (isDuplicate, _) = await CheckIdempotencyAsync(body.IdempotencyKey, cancellationToken);
            if (isDuplicate) return (StatusCodes.Conflict, null);

            var results = new List<BatchCreditResult>();
            var operations = body.Operations.ToList();
            for (var i = 0; i < operations.Count; i++)
            {
                var op = operations[i];
                var opKey = $"{body.IdempotencyKey}:{i}";
                var (status, response) = await CreditCurrencyAsync(new CreditCurrencyRequest
                {
                    WalletId = op.WalletId,
                    CurrencyDefinitionId = op.CurrencyDefinitionId,
                    Amount = op.Amount,
                    TransactionType = op.TransactionType,
                    ReferenceType = op.ReferenceType,
                    ReferenceId = op.ReferenceId,
                    IdempotencyKey = opKey
                }, cancellationToken);

                results.Add(new BatchCreditResult
                {
                    Index = i,
                    Success = status == StatusCodes.OK,
                    Transaction = response?.Transaction,
                    Error = status != StatusCodes.OK ? status.ToString() : null
                });
            }

            await RecordIdempotencyAsync(body.IdempotencyKey, Guid.NewGuid().ToString(), cancellationToken);

            return (StatusCodes.OK, new BatchCreditResponse { Results = results });
    }

    #endregion

    #region Conversion Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, CalculateConversionResponse?)> CalculateConversionAsync(
        CalculateConversionRequest body,
        CancellationToken cancellationToken = default)
    {
            var fromDef = await GetDefinitionByIdAsync(body.FromCurrencyId.ToString(), cancellationToken);
            if (fromDef is null) return (StatusCodes.NotFound, null);

            var toDef = await GetDefinitionByIdAsync(body.ToCurrencyId.ToString(), cancellationToken);
            if (toDef is null) return (StatusCodes.NotFound, null);

            var (rate, baseCurrency, error) = CalculateEffectiveRate(fromDef, toDef);
            if (error is not null) return ((StatusCodes)422, null);

            var toAmount = body.FromAmount * rate;

            return (StatusCodes.OK, new CalculateConversionResponse
            {
                ToAmount = Math.Round(toAmount, _configuration.ConversionRoundingPrecision, MidpointRounding.ToEven),
                EffectiveRate = rate,
                BaseCurrency = baseCurrency,
                ConversionPath = new List<ConversionStep>
                {
                    new() { From = fromDef.Code, To = baseCurrency, Rate = fromDef.ExchangeRateToBase ?? 1.0 },
                    new() { From = baseCurrency, To = toDef.Code, Rate = 1.0 / (toDef.ExchangeRateToBase ?? 1.0) }
                }
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ExecuteConversionResponse?)> ExecuteConversionAsync(
        ExecuteConversionRequest body,
        CancellationToken cancellationToken = default)
    {
            var (isDuplicate, _) = await CheckIdempotencyAsync(body.IdempotencyKey, cancellationToken);
            if (isDuplicate) return (StatusCodes.Conflict, null);

            var wallet = await GetWalletByIdAsync(body.WalletId.ToString(), cancellationToken);
            if (wallet is null) return (StatusCodes.NotFound, null);
            if (wallet.Status == WalletStatus.Frozen) return ((StatusCodes)422, null);

            var fromDef = await GetDefinitionByIdAsync(body.FromCurrencyId.ToString(), cancellationToken);
            if (fromDef is null) return (StatusCodes.NotFound, null);

            var toDef = await GetDefinitionByIdAsync(body.ToCurrencyId.ToString(), cancellationToken);
            if (toDef is null) return (StatusCodes.NotFound, null);

            var (rate, baseCurrency, error) = CalculateEffectiveRate(fromDef, toDef);
            if (error is not null) return ((StatusCodes)422, null);

            var toAmount = Math.Round(body.FromAmount * rate, _configuration.ConversionRoundingPrecision, MidpointRounding.ToEven);

            // Pre-validate wallet cap on target currency before debiting source
            if (toDef.PerWalletCap is not null &&
                (toDef.CapOverflowBehavior ?? CapOverflowBehavior.Reject) == CapOverflowBehavior.Reject)
            {
                var targetBalance = await GetOrCreateBalanceAsync(
                    body.WalletId.ToString(), body.ToCurrencyId.ToString(), cancellationToken);
                if (targetBalance.Amount + toAmount > toDef.PerWalletCap.Value)
                {
                    _logger.LogWarning(
                        "Conversion would exceed wallet cap on target currency {CurrencyId}: current={Current}, credit={Credit}, cap={Cap}",
                        body.ToCurrencyId, targetBalance.Amount, toAmount, toDef.PerWalletCap.Value);
                    return ((StatusCodes)422, null);
                }
            }

            // Debit source currency
            var debitKey = $"{body.IdempotencyKey}:debit";
            var (debitStatus, debitResp) = await DebitCurrencyAsync(new DebitCurrencyRequest
            {
                WalletId = body.WalletId,
                CurrencyDefinitionId = body.FromCurrencyId,
                Amount = body.FromAmount,
                TransactionType = TransactionType.ConversionDebit,
                IdempotencyKey = debitKey,
                ReferenceType = "conversion"
            }, cancellationToken);

            if (debitStatus != StatusCodes.OK || debitResp is null) return (debitStatus, null);

            // Credit target currency (bypass earn cap - conversions are exchanges, not earnings)
            var creditKey = $"{body.IdempotencyKey}:credit";
            var (creditStatus, creditResp) = await CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = body.WalletId,
                CurrencyDefinitionId = body.ToCurrencyId,
                Amount = toAmount,
                TransactionType = TransactionType.ConversionCredit,
                IdempotencyKey = creditKey,
                ReferenceType = "conversion",
                BypassEarnCap = true
            }, cancellationToken);

            if (creditStatus != StatusCodes.OK || creditResp is null)
            {
                // Compensate: reverse the debit since credit failed
                var compensateKey = $"{body.IdempotencyKey}:compensate";
                await CreditCurrencyAsync(new CreditCurrencyRequest
                {
                    WalletId = body.WalletId,
                    CurrencyDefinitionId = body.FromCurrencyId,
                    Amount = body.FromAmount,
                    TransactionType = TransactionType.ConversionCredit,
                    IdempotencyKey = compensateKey,
                    ReferenceType = "conversion_reversal",
                    BypassEarnCap = true
                }, cancellationToken);

                _logger.LogWarning("Conversion credit failed for wallet {WalletId}, reversed debit", body.WalletId);
                return (creditStatus, null);
            }

            await RecordIdempotencyAsync(body.IdempotencyKey, debitResp.Transaction.TransactionId.ToString(), cancellationToken);

            return (StatusCodes.OK, new ExecuteConversionResponse
            {
                DebitTransaction = debitResp.Transaction,
                CreditTransaction = creditResp.Transaction,
                FromDebited = body.FromAmount,
                ToCredited = toAmount,
                EffectiveRate = rate
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GetExchangeRateResponse?)> GetExchangeRateAsync(
        GetExchangeRateRequest body,
        CancellationToken cancellationToken = default)
    {
            var fromDef = await GetDefinitionByIdAsync(body.FromCurrencyId.ToString(), cancellationToken);
            if (fromDef is null) return (StatusCodes.NotFound, null);

            var toDef = await GetDefinitionByIdAsync(body.ToCurrencyId.ToString(), cancellationToken);
            if (toDef is null) return (StatusCodes.NotFound, null);

            var (rate, baseCurrency, error) = CalculateEffectiveRate(fromDef, toDef);
            if (error is not null) return ((StatusCodes)422, null);

            return (StatusCodes.OK, new GetExchangeRateResponse
            {
                Rate = rate,
                InverseRate = 1.0 / rate,
                BaseCurrency = baseCurrency,
                FromCurrencyRateToBase = fromDef.ExchangeRateToBase ?? 1.0,
                ToCurrencyRateToBase = toDef.ExchangeRateToBase ?? 1.0
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, UpdateExchangeRateResponse?)> UpdateExchangeRateAsync(
        UpdateExchangeRateRequest body,
        CancellationToken cancellationToken = default)
    {
            var store = _stateStoreFactory.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions);
            var key = $"{DEF_PREFIX}{body.CurrencyDefinitionId}";

            for (var attempt = 0; attempt < _configuration.ExchangeRateUpdateMaxRetries; attempt++)
            {
                var (definition, etag) = await store.GetWithETagAsync(key, cancellationToken);

                if (definition is null) return (StatusCodes.NotFound, null);
                if (definition.IsBaseCurrency) return (StatusCodes.BadRequest, null);

                var previousRate = definition.ExchangeRateToBase ?? 0;
                definition.ExchangeRateToBase = body.ExchangeRateToBase;
                definition.ExchangeRateUpdatedAt = DateTimeOffset.UtcNow;
                definition.ModifiedAt = DateTimeOffset.UtcNow;

                // GetWithETagAsync returns non-null etag when entity exists (checked above);
                // coalesce satisfies compiler's nullable analysis (will never execute)
                var saveResult = await store.TrySaveAsync(key, definition, etag ?? string.Empty, cancellationToken);
                if (saveResult != null)
                {
                    await _messageBus.TryPublishAsync("currency.exchange_rate.updated", new CurrencyExchangeRateUpdatedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        CurrencyDefinitionId = body.CurrencyDefinitionId,
                        CurrencyCode = definition.Code,
                        PreviousRate = previousRate,
                        NewRate = body.ExchangeRateToBase,
                        BaseCurrencyCode = "base",
                        UpdatedAt = DateTimeOffset.UtcNow
                    }, cancellationToken);

                    return (StatusCodes.OK, new UpdateExchangeRateResponse
                    {
                        Definition = MapDefinitionToResponse(definition),
                        PreviousRate = previousRate
                    });
                }

                _logger.LogDebug(
                    "Concurrent modification during exchange rate update for {CurrencyDefinitionId}, retrying (attempt {Attempt})",
                    body.CurrencyDefinitionId, attempt + 1);
            }

            _logger.LogWarning("Exchange rate update failed after retries for {CurrencyDefinitionId} due to concurrent modifications",
                body.CurrencyDefinitionId);
            return (StatusCodes.Conflict, null);
    }

    #endregion

    #region Transaction History Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, TransactionResponse?)> GetTransactionAsync(
        GetTransactionRequest body,
        CancellationToken cancellationToken = default)
    {
            var store = _stateStoreFactory.GetStore<TransactionModel>(StateStoreDefinitions.CurrencyTransactions);
            var tx = await store.GetAsync($"{TX_PREFIX}{body.TransactionId}", cancellationToken);
            if (tx is null) return (StatusCodes.NotFound, null);

            return (StatusCodes.OK, new TransactionResponse { Transaction = MapTransactionToRecord(tx) });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GetTransactionHistoryResponse?)> GetTransactionHistoryAsync(
        GetTransactionHistoryRequest body,
        CancellationToken cancellationToken = default)
    {
            var wallet = await GetWalletByIdAsync(body.WalletId.ToString(), cancellationToken);
            if (wallet is null) return (StatusCodes.NotFound, null);

            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyTransactions);
            var txStore = _stateStoreFactory.GetStore<TransactionModel>(StateStoreDefinitions.CurrencyTransactions);

            var indexJson = await stringStore.GetAsync($"{TX_WALLET_INDEX}{body.WalletId}", cancellationToken);
            var txIds = string.IsNullOrEmpty(indexJson) ? new List<string>() : BannouJson.Deserialize<List<string>>(indexJson) ?? new List<string>();

            var retentionFloor = DateTimeOffset.UtcNow - TimeSpan.FromDays(_configuration.TransactionRetentionDays);
            var effectiveFromDate = body.FromDate is not null && body.FromDate > retentionFloor
                ? body.FromDate
                : retentionFloor;

            var transactions = new List<CurrencyTransactionRecord>();
            foreach (var txId in txIds.AsEnumerable().Reverse())
            {
                var tx = await txStore.GetAsync($"{TX_PREFIX}{txId}", cancellationToken);
                if (tx is null) continue;

                // Apply filters
                if (body.CurrencyDefinitionId is not null && tx.CurrencyDefinitionId != body.CurrencyDefinitionId.Value) continue;
                if (body.TransactionTypes is not null && body.TransactionTypes.Count > 0 &&
                    !body.TransactionTypes.Contains(tx.TransactionType)) continue;
                if (tx.Timestamp < effectiveFromDate) continue;
                if (body.ToDate is not null && tx.Timestamp > body.ToDate) continue;

                transactions.Add(MapTransactionToRecord(tx));
            }

            var totalCount = transactions.Count;
            var offset = body.Offset;
            var limit = body.Limit;
            var paged = transactions.Skip(offset).Take(limit).ToList();

            return (StatusCodes.OK, new GetTransactionHistoryResponse
            {
                Transactions = paged,
                TotalCount = totalCount
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GetTransactionsByReferenceResponse?)> GetTransactionsByReferenceAsync(
        GetTransactionsByReferenceRequest body,
        CancellationToken cancellationToken = default)
    {
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyTransactions);
            var txStore = _stateStoreFactory.GetStore<TransactionModel>(StateStoreDefinitions.CurrencyTransactions);

            var refKey = $"{TX_REF_INDEX}{body.ReferenceType}:{body.ReferenceId}";
            var indexJson = await stringStore.GetAsync(refKey, cancellationToken);
            var txIds = string.IsNullOrEmpty(indexJson) ? new List<string>() : BannouJson.Deserialize<List<string>>(indexJson) ?? new List<string>();

            var transactions = new List<CurrencyTransactionRecord>();
            foreach (var txId in txIds)
            {
                var tx = await txStore.GetAsync($"{TX_PREFIX}{txId}", cancellationToken);
                if (tx is not null)
                {
                    transactions.Add(MapTransactionToRecord(tx));
                }
            }

            return (StatusCodes.OK, new GetTransactionsByReferenceResponse { Transactions = transactions });
    }

    #endregion

    #region Analytics Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, GetGlobalSupplyResponse?)> GetGlobalSupplyAsync(
        GetGlobalSupplyRequest body,
        CancellationToken cancellationToken = default)
    {
            var definition = await GetDefinitionByIdAsync(body.CurrencyDefinitionId.ToString(), cancellationToken);
            if (definition is null) return (StatusCodes.NotFound, null);

            // Aggregate from all balances - simplified implementation
            // In production, this would use pre-computed aggregates
            return (StatusCodes.OK, new GetGlobalSupplyResponse
            {
                TotalSupply = 0,
                InCirculation = 0,
                InEscrow = 0,
                TotalMinted = 0,
                TotalBurned = 0,
                SupplyCap = definition.GlobalSupplyCap,
                SupplyCapRemaining = definition.GlobalSupplyCap
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GetWalletDistributionResponse?)> GetWalletDistributionAsync(
        GetWalletDistributionRequest body,
        CancellationToken cancellationToken = default)
    {
            var definition = await GetDefinitionByIdAsync(body.CurrencyDefinitionId.ToString(), cancellationToken);
            if (definition is null) return (StatusCodes.NotFound, null);

            // Simplified implementation - returns zeros
            // In production, this would compute from indexed balance data
            return (StatusCodes.OK, new GetWalletDistributionResponse
            {
                TotalWallets = 0,
                WalletsWithBalance = 0,
                AverageBalance = 0,
                MedianBalance = 0,
                Percentiles = new DistributionPercentiles
                {
                    P10 = 0,
                    P25 = 0,
                    P50 = 0,
                    P75 = 0,
                    P90 = 0,
                    P99 = 0
                },
                GiniCoefficient = 0
            });
    }

    #endregion

    #region Escrow Integration

    /// <inheritdoc/>
    public async Task<(StatusCodes, EscrowDepositResponse?)> EscrowDepositAsync(
        EscrowDepositRequest body,
        CancellationToken cancellationToken = default)
    {
            var (status, debitResp) = await DebitCurrencyAsync(new DebitCurrencyRequest
            {
                WalletId = body.WalletId,
                CurrencyDefinitionId = body.CurrencyDefinitionId,
                Amount = body.Amount,
                TransactionType = TransactionType.EscrowDeposit,
                ReferenceType = "escrow",
                ReferenceId = body.EscrowId,
                IdempotencyKey = body.IdempotencyKey
            }, cancellationToken);

            if (status != StatusCodes.OK || debitResp is null) return (status, null);

            return (StatusCodes.OK, new EscrowDepositResponse
            {
                Transaction = debitResp.Transaction,
                NewBalance = debitResp.NewBalance
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, EscrowReleaseResponse?)> EscrowReleaseAsync(
        EscrowReleaseRequest body,
        CancellationToken cancellationToken = default)
    {
            var (status, creditResp) = await CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = body.WalletId,
                CurrencyDefinitionId = body.CurrencyDefinitionId,
                Amount = body.Amount,
                TransactionType = TransactionType.EscrowRelease,
                ReferenceType = "escrow",
                ReferenceId = body.EscrowId,
                IdempotencyKey = body.IdempotencyKey,
                BypassEarnCap = true
            }, cancellationToken);

            if (status != StatusCodes.OK || creditResp is null) return (status, null);

            return (StatusCodes.OK, new EscrowReleaseResponse
            {
                Transaction = creditResp.Transaction,
                NewBalance = creditResp.NewBalance
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, EscrowRefundResponse?)> EscrowRefundAsync(
        EscrowRefundRequest body,
        CancellationToken cancellationToken = default)
    {
            var (status, creditResp) = await CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = body.WalletId,
                CurrencyDefinitionId = body.CurrencyDefinitionId,
                Amount = body.Amount,
                TransactionType = TransactionType.EscrowRefund,
                ReferenceType = "escrow",
                ReferenceId = body.EscrowId,
                IdempotencyKey = body.IdempotencyKey,
                BypassEarnCap = true
            }, cancellationToken);

            if (status != StatusCodes.OK || creditResp is null) return (status, null);

            return (StatusCodes.OK, new EscrowRefundResponse
            {
                Transaction = creditResp.Transaction,
                NewBalance = creditResp.NewBalance
            });
    }

    #endregion

    #region Authorization Hold Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, HoldResponse?)> CreateHoldAsync(
        CreateHoldRequest body,
        CancellationToken cancellationToken = default)
    {
            if (body.Amount <= 0) return (StatusCodes.BadRequest, null);

            var (isDuplicate, _) = await CheckIdempotencyAsync(body.IdempotencyKey, cancellationToken);
            if (isDuplicate) return (StatusCodes.Conflict, null);

            var wallet = await GetWalletByIdAsync(body.WalletId.ToString(), cancellationToken);
            if (wallet is null) return (StatusCodes.NotFound, null);

            var definition = await GetDefinitionByIdAsync(body.CurrencyDefinitionId.ToString(), cancellationToken);
            if (definition is null) return (StatusCodes.NotFound, null);

            // Acquire balance lock to serialize holds against balance changes
            var balanceLockKey = $"{body.WalletId}:{body.CurrencyDefinitionId}";
            await using var lockResponse = await _lockProvider.LockAsync(
                "currency-balance", balanceLockKey, Guid.NewGuid().ToString(), _configuration.BalanceLockTimeoutSeconds, cancellationToken);
            if (!lockResponse.Success)
            {
                _logger.LogWarning("Could not acquire balance lock for hold creation on wallet {WalletId}, currency {CurrencyId}",
                    body.WalletId, body.CurrencyDefinitionId);
                return (StatusCodes.Conflict, null);
            }

            var balance = await GetOrCreateBalanceAsync(body.WalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);
            var currentHeld = await GetTotalHeldAmountAsync(body.WalletId.ToString(), body.CurrencyDefinitionId.ToString(), cancellationToken);
            var effectiveBalance = balance.Amount - currentHeld;

            if (effectiveBalance < body.Amount)
            {
                return ((StatusCodes)422, null);
            }

            var holdId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var maxExpiry = now + TimeSpan.FromDays(_configuration.HoldMaxDurationDays);
            if (body.ExpiresAt > maxExpiry)
            {
                _logger.LogWarning("Hold expiry {ExpiresAt} exceeds max duration of {MaxDays} days, clamping",
                    body.ExpiresAt, _configuration.HoldMaxDurationDays);
            }

            var clampedExpiry = body.ExpiresAt > maxExpiry ? maxExpiry : body.ExpiresAt;

            var hold = new HoldModel
            {
                HoldId = holdId,
                WalletId = body.WalletId,
                CurrencyDefinitionId = body.CurrencyDefinitionId,
                Amount = body.Amount,
                Status = HoldStatus.Active,
                CreatedAt = now,
                ExpiresAt = clampedExpiry,
                ReferenceType = body.ReferenceType,
                ReferenceId = body.ReferenceId
            };

            var holdStore = _stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHolds);
            var holdKey = $"{HOLD_PREFIX}{holdId}";
            await holdStore.SaveAsync(holdKey, hold, cancellationToken: cancellationToken);

            // Update Redis cache after MySQL write
            await UpdateHoldCacheAsync(holdKey, hold, cancellationToken);

            await AddToListAsync(StateStoreDefinitions.CurrencyHolds,
                $"{HOLD_WALLET_INDEX}{body.WalletId}:{body.CurrencyDefinitionId}", holdId.ToString(), cancellationToken);

            await RecordIdempotencyAsync(body.IdempotencyKey, holdId.ToString(), cancellationToken);

            await _messageBus.TryPublishAsync("currency.hold.created", new CurrencyHoldCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                HoldId = holdId,
                WalletId = body.WalletId,
                OwnerId = wallet.OwnerId,
                CurrencyDefinitionId = body.CurrencyDefinitionId,
                Amount = body.Amount,
                ExpiresAt = body.ExpiresAt,
                ReferenceType = body.ReferenceType,
                ReferenceId = body.ReferenceId
            }, cancellationToken);

            return (StatusCodes.OK, new HoldResponse { Hold = MapHoldToRecord(hold) });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CaptureHoldResponse?)> CaptureHoldAsync(
        CaptureHoldRequest body,
        CancellationToken cancellationToken = default)
    {
            var (isDuplicate, _) = await CheckIdempotencyAsync(body.IdempotencyKey, cancellationToken);
            if (isDuplicate) return (StatusCodes.Conflict, null);

            var holdKey = $"{HOLD_PREFIX}{body.HoldId}";

            // Read from MySQL with ETag for optimistic concurrency
            var holdStore = _stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHolds);
            var (hold, holdEtag) = await holdStore.GetWithETagAsync(holdKey, cancellationToken);

            if (hold is null) return (StatusCodes.NotFound, null);
            if (hold.Status != HoldStatus.Active) return (StatusCodes.BadRequest, null);
            if (body.CaptureAmount > hold.Amount) return (StatusCodes.BadRequest, null);

            // Acquire hold lock to prevent concurrent captures on the same hold
            await using var holdLock = await _lockProvider.LockAsync(
                "currency-hold", body.HoldId.ToString(), Guid.NewGuid().ToString(), _configuration.HoldLockTimeoutSeconds, cancellationToken);
            if (!holdLock.Success)
            {
                _logger.LogWarning("Could not acquire hold lock for capture on hold {HoldId}", body.HoldId);
                return (StatusCodes.Conflict, null);
            }

            // Debit the captured amount
            var debitKey = $"{body.IdempotencyKey}:hold-capture";
            var (debitStatus, debitResp) = await DebitCurrencyAsync(new DebitCurrencyRequest
            {
                WalletId = hold.WalletId,
                CurrencyDefinitionId = hold.CurrencyDefinitionId,
                Amount = body.CaptureAmount,
                TransactionType = TransactionType.Fee,
                ReferenceType = hold.ReferenceType ?? "hold",
                ReferenceId = hold.ReferenceId,
                IdempotencyKey = debitKey
            }, cancellationToken);

            if (debitStatus != StatusCodes.OK || debitResp is null) return (debitStatus, null);

            var amountReleased = hold.Amount - body.CaptureAmount;
            hold.Status = HoldStatus.Captured;
            hold.CapturedAmount = body.CaptureAmount;
            hold.CompletedAt = DateTimeOffset.UtcNow;

            // GetWithETagAsync returns non-null etag when entity exists (checked above);
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var newHoldEtag = await holdStore.TrySaveAsync(holdKey, hold, holdEtag ?? string.Empty, cancellationToken);
            if (newHoldEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for hold {HoldId}", body.HoldId);
                return (StatusCodes.Conflict, null);
            }

            // Update Redis cache after MySQL write
            await UpdateHoldCacheAsync(holdKey, hold, cancellationToken);

            // Remove completed hold from wallet index to prevent unbounded index growth
            await RemoveFromListAsync(StateStoreDefinitions.CurrencyHolds,
                $"{HOLD_WALLET_INDEX}{hold.WalletId}:{hold.CurrencyDefinitionId}", hold.HoldId.ToString(), cancellationToken);

            await RecordIdempotencyAsync(body.IdempotencyKey, hold.HoldId.ToString(), cancellationToken);

            // Fetch wallet for event - data integrity issue if missing
            var captureWallet = await GetWalletByIdAsync(hold.WalletId.ToString(), cancellationToken);
            if (captureWallet is null)
            {
                _logger.LogError("Data integrity issue: Wallet {WalletId} not found or missing OwnerId for hold {HoldId}",
                    hold.WalletId, body.HoldId);
                await _messageBus.TryPublishErrorAsync(
                    "currency",
                    "CaptureHold",
                    "data_integrity_error",
                    $"Wallet {hold.WalletId} not found or missing OwnerId",
                    dependency: null,
                    endpoint: "post:/currency/hold/capture",
                    details: null,
                    stack: null);
                return (StatusCodes.InternalServerError, null);
            }

            await _messageBus.TryPublishAsync("currency.hold.captured", new CurrencyHoldCapturedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                HoldId = body.HoldId,
                WalletId = hold.WalletId,
                OwnerId = captureWallet.OwnerId,
                CurrencyDefinitionId = hold.CurrencyDefinitionId,
                HoldAmount = hold.Amount,
                CapturedAmount = body.CaptureAmount,
                AmountReleased = amountReleased,
                TransactionId = debitResp.Transaction.TransactionId
            }, cancellationToken);

            return (StatusCodes.OK, new CaptureHoldResponse
            {
                Hold = MapHoldToRecord(hold),
                Transaction = debitResp.Transaction,
                NewBalance = debitResp.NewBalance,
                AmountReleased = amountReleased
            });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, HoldResponse?)> ReleaseHoldAsync(
        ReleaseHoldRequest body,
        CancellationToken cancellationToken = default)
    {
            var holdKey = $"{HOLD_PREFIX}{body.HoldId}";

            // Read from MySQL with ETag for optimistic concurrency
            var holdStore = _stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHolds);
            var (hold, holdEtag) = await holdStore.GetWithETagAsync(holdKey, cancellationToken);

            if (hold is null) return (StatusCodes.NotFound, null);
            if (hold.Status != HoldStatus.Active) return (StatusCodes.BadRequest, null);

            hold.Status = HoldStatus.Released;
            hold.CompletedAt = DateTimeOffset.UtcNow;

            // GetWithETagAsync returns non-null etag when entity exists (checked above);
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var newHoldEtag = await holdStore.TrySaveAsync(holdKey, hold, holdEtag ?? string.Empty, cancellationToken);
            if (newHoldEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for hold {HoldId}", body.HoldId);
                return (StatusCodes.Conflict, null);
            }

            // Update Redis cache after MySQL write
            await UpdateHoldCacheAsync(holdKey, hold, cancellationToken);

            // Remove completed hold from wallet index to prevent unbounded index growth
            await RemoveFromListAsync(StateStoreDefinitions.CurrencyHolds,
                $"{HOLD_WALLET_INDEX}{hold.WalletId}:{hold.CurrencyDefinitionId}", hold.HoldId.ToString(), cancellationToken);

            // Fetch wallet for event - data integrity issue if missing
            var wallet = await GetWalletByIdAsync(hold.WalletId.ToString(), cancellationToken);
            if (wallet is null)
            {
                _logger.LogError("Data integrity issue: Wallet {WalletId} not found or missing OwnerId for hold {HoldId}",
                    hold.WalletId, body.HoldId);
                await _messageBus.TryPublishErrorAsync(
                    "currency",
                    "ReleaseHold",
                    "data_integrity_error",
                    $"Wallet {hold.WalletId} not found or missing OwnerId",
                    dependency: null,
                    endpoint: "post:/currency/hold/release",
                    details: null,
                    stack: null);
                return (StatusCodes.InternalServerError, null);
            }

            await _messageBus.TryPublishAsync("currency.hold.released", new CurrencyHoldReleasedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                HoldId = body.HoldId,
                WalletId = hold.WalletId,
                OwnerId = wallet.OwnerId,
                CurrencyDefinitionId = hold.CurrencyDefinitionId,
                Amount = hold.Amount
            }, cancellationToken);

            return (StatusCodes.OK, new HoldResponse { Hold = MapHoldToRecord(hold) });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, HoldResponse?)> GetHoldAsync(
        GetHoldRequest body,
        CancellationToken cancellationToken = default)
    {
            var holdKey = $"{HOLD_PREFIX}{body.HoldId}";

            // Check Redis cache first (per IMPLEMENTATION TENETS - use defined cache stores)
            var hold = await TryGetHoldFromCacheAsync(holdKey, cancellationToken);
            if (hold != null)
                return (StatusCodes.OK, new HoldResponse { Hold = MapHoldToRecord(hold) });

            // Cache miss - read from MySQL
            var holdStore = _stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHolds);
            hold = await holdStore.GetAsync(holdKey, cancellationToken);

            if (hold is null) return (StatusCodes.NotFound, null);

            // Populate cache for future reads
            await UpdateHoldCacheAsync(holdKey, hold, cancellationToken);

            return (StatusCodes.OK, new HoldResponse { Hold = MapHoldToRecord(hold) });
    }

    #endregion

    #region Private Helper Methods

    private async Task<CurrencyDefinitionModel?> ResolveCurrencyDefinitionAsync(string? id, string? code, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions);
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions);

        if (!string.IsNullOrEmpty(id))
        {
            return await store.GetAsync($"{DEF_PREFIX}{id}", ct);
        }

        if (!string.IsNullOrEmpty(code))
        {
            var resolvedId = await stringStore.GetAsync($"{DEF_CODE_INDEX}{code}", ct);
            if (!string.IsNullOrEmpty(resolvedId))
            {
                return await store.GetAsync($"{DEF_PREFIX}{resolvedId}", ct);
            }
        }

        return null;
    }

    private async Task<CurrencyDefinitionModel?> GetDefinitionByIdAsync(string id, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions);
        return await store.GetAsync($"{DEF_PREFIX}{id}", ct);
    }

    private async Task<WalletModel?> GetWalletByIdAsync(string id, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets);
        return await store.GetAsync($"{WALLET_PREFIX}{id}", ct);
    }

    private async Task<WalletModel?> ResolveWalletAsync(string? walletId, string? ownerId, string? ownerType, string? realmId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(walletId))
        {
            return await GetWalletByIdAsync(walletId, ct);
        }

        if (!string.IsNullOrEmpty(ownerId) && !string.IsNullOrEmpty(ownerType))
        {
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyWallets);
            var ownerKey = BuildOwnerKey(ownerId, ownerType, realmId);
            var resolvedId = await stringStore.GetAsync($"{WALLET_OWNER_INDEX}{ownerKey}", ct);
            if (!string.IsNullOrEmpty(resolvedId))
            {
                return await GetWalletByIdAsync(resolvedId, ct);
            }
        }

        return null;
    }

    private async Task<BalanceModel> GetOrCreateBalanceAsync(string walletId, string currencyDefId, CancellationToken ct)
    {
        var key = $"{BALANCE_PREFIX}{walletId}:{currencyDefId}";

        // Check Redis cache first (per IMPLEMENTATION TENETS - use defined cache stores)
        var cached = await TryGetBalanceFromCacheAsync(key, ct);
        if (cached != null)
            return cached;

        // Cache miss - read from MySQL
        var store = _stateStoreFactory.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalances);
        var balance = await store.GetAsync(key, ct);

        if (balance != null)
        {
            // Populate cache for future reads
            await UpdateBalanceCacheAsync(key, balance, ct);
            return balance;
        }

        // Create new balance (don't cache until saved)
        return new BalanceModel
        {
            WalletId = Guid.Parse(walletId),
            CurrencyDefinitionId = Guid.Parse(currencyDefId),
            Amount = 0,
            DailyEarned = 0,
            WeeklyEarned = 0,
            DailyResetAt = DateTimeOffset.UtcNow.Date.AddDays(1),
            WeeklyResetAt = GetNextWeeklyReset(),
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task SaveBalanceAsync(BalanceModel balance, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalances);
        var key = $"{BALANCE_PREFIX}{balance.WalletId}:{balance.CurrencyDefinitionId}";
        await store.SaveAsync(key, balance, cancellationToken: ct);

        // Update Redis cache after MySQL write
        await UpdateBalanceCacheAsync(key, balance, ct);

        // Maintain wallet-balance index for efficient wallet queries
        await AddToListAsync(StateStoreDefinitions.CurrencyBalances, $"{BALANCE_WALLET_INDEX}{balance.WalletId}", balance.CurrencyDefinitionId.ToString(), ct);

        // Maintain currency-balance reverse index for autogain task processing
        await AddToListAsync(StateStoreDefinitions.CurrencyBalances, $"{BALANCE_CURRENCY_INDEX}{balance.CurrencyDefinitionId}", balance.WalletId.ToString(), ct);
    }

    private async Task<List<BalanceModel>> GetAllBalancesForWalletAsync(string walletId, CancellationToken ct)
    {
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyBalances);
        var balanceStore = _stateStoreFactory.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalances);

        var indexJson = await stringStore.GetAsync($"{BALANCE_WALLET_INDEX}{walletId}", ct);
        if (string.IsNullOrEmpty(indexJson))
            return new List<BalanceModel>();

        var currencyDefIds = BannouJson.Deserialize<List<string>>(indexJson) ?? new List<string>();
        var balances = new List<BalanceModel>();

        foreach (var currencyDefId in currencyDefIds)
        {
            var balance = await balanceStore.GetAsync($"{BALANCE_PREFIX}{walletId}:{currencyDefId}", ct);
            if (balance is not null)
                balances.Add(balance);
        }

        return balances;
    }

    private async Task<List<BalanceSummary>> GetWalletBalanceSummariesAsync(string walletId, CancellationToken ct)
    {
        var balances = await GetAllBalancesForWalletAsync(walletId, ct);
        var defStore = _stateStoreFactory.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions);
        var summaries = new List<BalanceSummary>();

        foreach (var balance in balances)
        {
            var definition = await defStore.GetAsync($"{DEF_PREFIX}{balance.CurrencyDefinitionId}", ct);
            if (definition is null) continue;

            var lockedAmount = await GetTotalHeldAmountAsync(balance.WalletId.ToString(), balance.CurrencyDefinitionId.ToString(), ct);
            summaries.Add(new BalanceSummary
            {
                CurrencyDefinitionId = balance.CurrencyDefinitionId,
                CurrencyCode = definition.Code,
                Amount = balance.Amount,
                LockedAmount = lockedAmount,
                EffectiveAmount = balance.Amount - lockedAmount
            });
        }

        return summaries;
    }

    private async Task<BalanceModel> ApplyAutogainIfNeededAsync(BalanceModel balance, CurrencyDefinitionModel definition, WalletModel wallet, CancellationToken ct)
    {
        if (!definition.AutogainEnabled || string.IsNullOrEmpty(definition.AutogainInterval))
            return balance;

        var now = DateTimeOffset.UtcNow;

        // First-time: initialize without retroactive gain
        if (balance.LastAutogainAt is null)
        {
            balance.LastAutogainAt = now;
            await SaveBalanceAsync(balance, ct);
            return balance;
        }

        TimeSpan interval;
        try { interval = XmlConvert.ToTimeSpan(definition.AutogainInterval); }
        catch { return balance; }

        if (interval.TotalSeconds <= 0) return balance;

        var elapsed = now - balance.LastAutogainAt.Value;
        var periodsElapsed = (int)(elapsed.TotalSeconds / interval.TotalSeconds);

        if (periodsElapsed <= 0) return balance;

        // Acquire autogain lock to prevent concurrent autogain application
        await using var autogainLock = await _lockProvider.LockAsync(
            "currency-autogain", $"{balance.WalletId}:{balance.CurrencyDefinitionId}",
            Guid.NewGuid().ToString(), _configuration.AutogainLockTimeoutSeconds, ct);
        if (!autogainLock.Success)
            return balance; // Skip, will be applied on next access

        var previousBalance = balance.Amount;
        double gain;

        if (definition.AutogainMode == AutogainMode.Compound)
        {
            var rate = definition.AutogainAmount ?? 0;
            var multiplier = Math.Pow(1 + rate, periodsElapsed);
            gain = balance.Amount * (multiplier - 1);
        }
        else
        {
            gain = periodsElapsed * (definition.AutogainAmount ?? 0);
        }

        // Apply autogain cap
        if (definition.AutogainCap is not null && balance.Amount >= definition.AutogainCap.Value)
        {
            gain = 0;
        }
        else if (definition.AutogainCap is not null)
        {
            gain = Math.Min(gain, definition.AutogainCap.Value - balance.Amount);
        }

        if (gain <= 0) return balance;

        var periodsFrom = balance.LastAutogainAt.Value;
        balance.Amount += gain;
        balance.LastAutogainAt = balance.LastAutogainAt.Value + TimeSpan.FromSeconds(interval.TotalSeconds * periodsElapsed);
        balance.LastModifiedAt = now;

        await SaveBalanceAsync(balance, ct);

        await _messageBus.TryPublishAsync("currency.autogain.calculated", new CurrencyAutogainCalculatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            WalletId = balance.WalletId,
            OwnerId = wallet.OwnerId,
            OwnerType = wallet.OwnerType,
            CurrencyDefinitionId = balance.CurrencyDefinitionId,
            CurrencyCode = definition.Code,
            PreviousBalance = previousBalance,
            PeriodsApplied = periodsElapsed,
            AmountGained = gain,
            NewBalance = balance.Amount,
            AutogainMode = definition.AutogainMode ?? AutogainMode.Simple,
            CalculatedAt = now,
            PeriodsFrom = periodsFrom,
            PeriodsTo = balance.LastAutogainAt.Value
        }, ct);

        return balance;
    }

    private async Task<TransactionModel> RecordTransactionAsync(
        string? sourceWalletId, string? targetWalletId, string currencyDefId,
        double amount, TransactionType type, string? refType, string? refId,
        string? escrowId, string idempotencyKey,
        double? sourceBalBefore, double? sourceBalAfter,
        double? targetBalBefore, double? targetBalAfter,
        object? metadata, CancellationToken ct)
    {
        var txId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var tx = new TransactionModel
        {
            TransactionId = txId,
            SourceWalletId = sourceWalletId is not null ? Guid.Parse(sourceWalletId) : null,
            TargetWalletId = targetWalletId is not null ? Guid.Parse(targetWalletId) : null,
            CurrencyDefinitionId = Guid.Parse(currencyDefId),
            Amount = amount,
            TransactionType = type,
            ReferenceType = refType,
            ReferenceId = refId is not null ? Guid.Parse(refId) : null,
            EscrowId = escrowId is not null ? Guid.Parse(escrowId) : null,
            IdempotencyKey = idempotencyKey,
            Timestamp = now,
            SourceBalanceBefore = sourceBalBefore,
            SourceBalanceAfter = sourceBalAfter,
            TargetBalanceBefore = targetBalBefore,
            TargetBalanceAfter = targetBalAfter
        };

        var store = _stateStoreFactory.GetStore<TransactionModel>(StateStoreDefinitions.CurrencyTransactions);
        await store.SaveAsync($"{TX_PREFIX}{txId}", tx, cancellationToken: ct);

        // Index by wallet
        if (sourceWalletId is not null)
            await AddToListAsync(StateStoreDefinitions.CurrencyTransactions, $"{TX_WALLET_INDEX}{sourceWalletId}", txId.ToString(), ct);
        if (targetWalletId is not null)
            await AddToListAsync(StateStoreDefinitions.CurrencyTransactions, $"{TX_WALLET_INDEX}{targetWalletId}", txId.ToString(), ct);

        // Index by reference
        if (refType is not null && refId is not null)
            await AddToListAsync(StateStoreDefinitions.CurrencyTransactions, $"{TX_REF_INDEX}{refType}:{refId}", txId.ToString(), ct);

        return tx;
    }

    private async Task<(bool isDuplicate, Guid? existingTxId)> CheckIdempotencyAsync(string key, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyIdempotency);
        var existing = await store.GetAsync(key, ct);
        if (!string.IsNullOrEmpty(existing) && Guid.TryParse(existing, out var txId))
            return (true, txId);
        return (false, null);
    }

    private async Task RecordIdempotencyAsync(string key, string transactionId, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyIdempotency);
        await store.SaveAsync(key, transactionId, new StateOptions { Ttl = _configuration.IdempotencyTtlSeconds }, ct);
    }

    private async Task InternalCreditAsync(string walletId, string currencyDefId, double amount,
        TransactionType type, string? refType, Guid? refId, string idempotencyKey, CancellationToken ct)
    {
        await CreditCurrencyAsync(new CreditCurrencyRequest
        {
            WalletId = Guid.Parse(walletId),
            CurrencyDefinitionId = Guid.Parse(currencyDefId),
            Amount = amount,
            TransactionType = type,
            ReferenceType = refType,
            ReferenceId = refId,
            IdempotencyKey = idempotencyKey,
            BypassEarnCap = true
        }, ct);
    }

    private async Task<double> GetTotalHeldAmountAsync(string walletId, string currencyDefId, CancellationToken ct)
    {
        var holdStore = _stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHolds);
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyHolds);

        var indexJson = await stringStore.GetAsync($"{HOLD_WALLET_INDEX}{walletId}:{currencyDefId}", ct);
        if (string.IsNullOrEmpty(indexJson)) return 0;

        var holdIds = BannouJson.Deserialize<List<string>>(indexJson) ?? new List<string>();
        double total = 0;
        foreach (var holdId in holdIds)
        {
            var holdKey = $"{HOLD_PREFIX}{holdId}";

            // Check Redis cache first (per IMPLEMENTATION TENETS - use defined cache stores)
            var hold = await TryGetHoldFromCacheAsync(holdKey, ct);
            if (hold is null)
            {
                // Cache miss - read from MySQL
                hold = await holdStore.GetAsync(holdKey, ct);
                if (hold != null)
                {
                    // Populate cache for future reads
                    await UpdateHoldCacheAsync(holdKey, hold, ct);
                }
            }

            if (hold is not null && hold.Status == HoldStatus.Active)
            {
                total += hold.Amount;
            }
        }
        return total;
    }

    private static (double rate, string baseCurrency, string? error) CalculateEffectiveRate(
        CurrencyDefinitionModel fromDef, CurrencyDefinitionModel toDef)
    {
        var fromRate = fromDef.IsBaseCurrency ? 1.0 : fromDef.ExchangeRateToBase;
        var toRate = toDef.IsBaseCurrency ? 1.0 : toDef.ExchangeRateToBase;

        if (fromRate is null || toRate is null || toRate == 0)
            return (0, "", "Missing exchange rate");

        var baseCurrencyCode = fromDef.IsBaseCurrency ? fromDef.Code : (toDef.IsBaseCurrency ? toDef.Code : "base");

        var effectiveRate = fromRate.Value / toRate.Value;
        return (effectiveRate, baseCurrencyCode, null);
    }

    private static void ResetEarnCapsIfNeeded(BalanceModel balance)
    {
        var now = DateTimeOffset.UtcNow;
        if (balance.DailyResetAt <= now)
        {
            balance.DailyEarned = 0;
            balance.DailyResetAt = now.Date.AddDays(1);
        }
        if (balance.WeeklyResetAt <= now)
        {
            balance.WeeklyEarned = 0;
            balance.WeeklyResetAt = GetNextWeeklyReset();
        }
    }

    private static double ApplyEarnCap(double amount, BalanceModel balance, CurrencyDefinitionModel definition)
    {
        var result = amount;

        if (definition.DailyEarnCap is not null)
        {
            var remaining = definition.DailyEarnCap.Value - balance.DailyEarned;
            if (remaining <= 0) return 0;
            result = Math.Min(result, remaining);
        }

        if (definition.WeeklyEarnCap is not null)
        {
            var remaining = definition.WeeklyEarnCap.Value - balance.WeeklyEarned;
            if (remaining <= 0) return 0;
            result = Math.Min(result, remaining);
        }

        return result;
    }

    private bool IsNegativeAllowed(CurrencyDefinitionModel definition, bool? transactionOverride)
    {
        return transactionOverride ?? definition.AllowNegative ?? _configuration.DefaultAllowNegative;
    }

    private static EarnCapInfo? BuildEarnCapInfo(BalanceModel balance, CurrencyDefinitionModel definition)
    {
        if (definition.DailyEarnCap is null && definition.WeeklyEarnCap is null)
            return null;

        return new EarnCapInfo
        {
            DailyEarned = balance.DailyEarned,
            DailyRemaining = Math.Max(0, (definition.DailyEarnCap ?? double.MaxValue) - balance.DailyEarned),
            DailyResetsAt = balance.DailyResetAt,
            WeeklyEarned = balance.WeeklyEarned,
            WeeklyRemaining = Math.Max(0, (definition.WeeklyEarnCap ?? double.MaxValue) - balance.WeeklyEarned),
            WeeklyResetsAt = balance.WeeklyResetAt
        };
    }

    private static AutogainInfo? BuildAutogainInfo(BalanceModel balance, CurrencyDefinitionModel definition)
    {
        if (!definition.AutogainEnabled || string.IsNullOrEmpty(definition.AutogainInterval))
            return null;

        TimeSpan interval;
        try { interval = XmlConvert.ToTimeSpan(definition.AutogainInterval); }
        catch { return null; }

        var lastCalc = balance.LastAutogainAt ?? DateTimeOffset.UtcNow;
        var nextGainAt = lastCalc + interval;

        double nextGainAmount;
        if (definition.AutogainMode == AutogainMode.Compound)
        {
            nextGainAmount = balance.Amount * (definition.AutogainAmount ?? 0);
        }
        else
        {
            nextGainAmount = definition.AutogainAmount ?? 0;
        }

        return new AutogainInfo
        {
            LastCalculatedAt = lastCalc,
            NextGainAt = nextGainAt,
            NextGainAmount = nextGainAmount,
            Mode = definition.AutogainMode ?? AutogainMode.Simple
        };
    }

    private static string BuildOwnerKey(string ownerId, string ownerType, string? realmId)
    {
        return realmId is not null ? $"{ownerId}:{ownerType}:{realmId}" : $"{ownerId}:{ownerType}";
    }

    private static DateTimeOffset GetNextWeeklyReset()
    {
        var now = DateTimeOffset.UtcNow;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return now.Date.AddDays(daysUntilMonday);
    }

    private async Task AddToListAsync(string storeName, string key, string value, CancellationToken ct)
    {
        await using var lockResponse = await _lockProvider.LockAsync(
            "currency-index", key, Guid.NewGuid().ToString(), _configuration.IndexLockTimeoutSeconds, ct);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire index lock for key {Key} in store {Store}", key, storeName);
            return;
        }

        var store = _stateStoreFactory.GetStore<string>(storeName);
        var existing = await store.GetAsync(key, ct);
        var list = string.IsNullOrEmpty(existing) ? new List<string>() : BannouJson.Deserialize<List<string>>(existing) ?? new List<string>();
        if (!list.Contains(value))
        {
            list.Add(value);
            await store.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: ct);
        }
    }

    private async Task RemoveFromListAsync(string storeName, string key, string value, CancellationToken ct)
    {
        await using var lockResponse = await _lockProvider.LockAsync(
            "currency-index", key, Guid.NewGuid().ToString(), _configuration.IndexLockTimeoutSeconds, ct);
        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire index lock for removal on key {Key} in store {Store}", key, storeName);
            return;
        }

        var store = _stateStoreFactory.GetStore<string>(storeName);
        var existing = await store.GetAsync(key, ct);
        if (string.IsNullOrEmpty(existing)) return;

        var list = BannouJson.Deserialize<List<string>>(existing) ?? new List<string>();
        if (list.Remove(value))
        {
            await store.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: ct);
        }
    }

    #endregion

    #region Mapping Helpers

    private static CurrencyDefinitionResponse MapDefinitionToResponse(CurrencyDefinitionModel m)
    {
        return new CurrencyDefinitionResponse
        {
            DefinitionId = m.DefinitionId,
            Code = m.Code,
            Name = m.Name,
            Description = m.Description,
            Scope = m.Scope,
            RealmsAvailable = m.RealmsAvailable,
            Precision = m.Precision,
            Transferable = m.Transferable,
            Tradeable = m.Tradeable,
            AllowNegative = m.AllowNegative,
            PerWalletCap = m.PerWalletCap,
            CapOverflowBehavior = m.CapOverflowBehavior,
            GlobalSupplyCap = m.GlobalSupplyCap,
            DailyEarnCap = m.DailyEarnCap,
            WeeklyEarnCap = m.WeeklyEarnCap,
            EarnCapResetTime = m.EarnCapResetTime,
            AutogainEnabled = m.AutogainEnabled,
            AutogainMode = m.AutogainMode,
            AutogainAmount = m.AutogainAmount,
            AutogainInterval = m.AutogainInterval,
            AutogainCap = m.AutogainCap,
            Expires = m.Expires,
            ExpirationPolicy = m.ExpirationPolicy,
            ExpirationDate = m.ExpirationDate,
            ExpirationDuration = m.ExpirationDuration,
            SeasonId = m.SeasonId,
            LinkedToItem = m.LinkedToItem,
            LinkedItemTemplateId = m.LinkedItemTemplateId,
            LinkageMode = m.LinkageMode,
            IsBaseCurrency = m.IsBaseCurrency,
            ExchangeRateToBase = m.ExchangeRateToBase,
            ExchangeRateUpdatedAt = m.ExchangeRateUpdatedAt,
            IconAssetId = m.IconAssetId,
            DisplayFormat = m.DisplayFormat,
            IsActive = m.IsActive,
            CreatedAt = m.CreatedAt,
            ModifiedAt = m.ModifiedAt
        };
    }

    private static WalletResponse MapWalletToResponse(WalletModel m)
    {
        return new WalletResponse
        {
            WalletId = m.WalletId,
            OwnerId = m.OwnerId,
            OwnerType = m.OwnerType,
            RealmId = m.RealmId,
            Status = m.Status,
            FrozenReason = m.FrozenReason,
            FrozenAt = m.FrozenAt,
            CreatedAt = m.CreatedAt,
            LastActivityAt = m.LastActivityAt
        };
    }

    private static CurrencyTransactionRecord MapTransactionToRecord(TransactionModel m)
    {
        return new CurrencyTransactionRecord
        {
            TransactionId = m.TransactionId,
            SourceWalletId = m.SourceWalletId,
            TargetWalletId = m.TargetWalletId,
            CurrencyDefinitionId = m.CurrencyDefinitionId,
            Amount = m.Amount,
            TransactionType = m.TransactionType,
            ReferenceType = m.ReferenceType,
            ReferenceId = m.ReferenceId,
            EscrowId = m.EscrowId,
            IdempotencyKey = m.IdempotencyKey,
            Timestamp = m.Timestamp,
            SourceBalanceBefore = m.SourceBalanceBefore,
            SourceBalanceAfter = m.SourceBalanceAfter,
            TargetBalanceBefore = m.TargetBalanceBefore,
            TargetBalanceAfter = m.TargetBalanceAfter
        };
    }

    private static HoldRecord MapHoldToRecord(HoldModel m)
    {
        return new HoldRecord
        {
            HoldId = m.HoldId,
            WalletId = m.WalletId,
            CurrencyDefinitionId = m.CurrencyDefinitionId,
            Amount = m.Amount,
            Status = m.Status,
            CreatedAt = m.CreatedAt,
            ExpiresAt = m.ExpiresAt,
            ReferenceType = m.ReferenceType,
            ReferenceId = m.ReferenceId,
            CapturedAmount = m.CapturedAmount,
            CompletedAt = m.CompletedAt
        };
    }

    #endregion

    #region Balance Cache Helpers

    /// <summary>
    /// Attempts to retrieve a balance from the Redis cache.
    /// Uses StateStoreDefinitions.CurrencyBalanceCache directly per IMPLEMENTATION TENETS.
    /// </summary>
    private async Task<BalanceModel?> TryGetBalanceFromCacheAsync(string key, CancellationToken ct)
    {
            var cache = _stateStoreFactory.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalanceCache);
            return await cache.GetAsync(key, ct);
        }
        catch (Exception ex)
        {
            // Cache error is non-fatal - proceed to MySQL
            _logger.LogDebug(ex, "Balance cache lookup failed for {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// Updates the balance cache after a read or write.
    /// Uses StateStoreDefinitions.CurrencyBalanceCache directly per IMPLEMENTATION TENETS.
    /// </summary>
    private async Task UpdateBalanceCacheAsync(string key, BalanceModel balance, CancellationToken ct)
    {
            var cache = _stateStoreFactory.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalanceCache);
            await cache.SaveAsync(key, balance, new StateOptions { Ttl = _configuration.BalanceCacheTtlSeconds }, ct);
        }
        catch (Exception ex)
        {
            // Cache write failure is non-fatal
            _logger.LogWarning(ex, "Failed to update balance cache for {Key}", key);
        }
    }

    #endregion

    #region Hold Cache Helpers

    /// <summary>
    /// Attempts to retrieve a hold from the Redis cache.
    /// Uses StateStoreDefinitions.CurrencyHoldsCache directly per IMPLEMENTATION TENETS.
    /// </summary>
    private async Task<HoldModel?> TryGetHoldFromCacheAsync(string key, CancellationToken ct)
    {
            var cache = _stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHoldsCache);
            return await cache.GetAsync(key, ct);
        }
        catch (Exception ex)
        {
            // Cache error is non-fatal - proceed to MySQL
            _logger.LogDebug(ex, "Hold cache lookup failed for {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// Updates the hold cache after a read or write.
    /// Uses StateStoreDefinitions.CurrencyHoldsCache directly per IMPLEMENTATION TENETS.
    /// </summary>
    private async Task UpdateHoldCacheAsync(string key, HoldModel hold, CancellationToken ct)
    {
            var cache = _stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHoldsCache);
            await cache.SaveAsync(key, hold, new StateOptions { Ttl = _configuration.HoldCacheTtlSeconds }, ct);
        }
        catch (Exception ex)
        {
            // Cache write failure is non-fatal
            _logger.LogWarning(ex, "Failed to update hold cache for {Key}", key);
        }
    }

    /// <summary>
    /// Invalidates a hold from the cache (for terminal states).
    /// </summary>
    private async Task InvalidateHoldCacheAsync(string key, CancellationToken ct)
    {
            var cache = _stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHoldsCache);
            await cache.DeleteAsync(key, ct);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal
            _logger.LogDebug(ex, "Failed to invalidate hold cache for {Key}", key);
        }
    }

    #endregion
}
