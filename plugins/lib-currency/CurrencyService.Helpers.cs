using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Currency.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Currency.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Xml;
using static BeyondImmersion.BannouService.Currency.CurrencyKeys;

namespace BeyondImmersion.BannouService.Currency;

// =============================================================================
// CurrencyService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by CurrencyService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (CurrencyService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ICurrencyService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (CurrencyService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for CurrencyService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class CurrencyService
{
    // Move private/internal helper methods here from CurrencyService.cs
    private async Task<CurrencyDefinitionModel?> ResolveCurrencyDefinitionAsync(Guid? id, string? code, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.ResolveCurrencyDefinitionAsync");

        if (id.HasValue)
        {
            return await _definitionStore.GetAsync($"{DEF_PREFIX}{id}", ct);
        }

        if (!string.IsNullOrEmpty(code))
        {
            var resolvedId = await _definitionStringStore.GetAsync($"{DEF_CODE_INDEX}{code}", ct);
            if (!string.IsNullOrEmpty(resolvedId))
            {
                return await _definitionStore.GetAsync($"{DEF_PREFIX}{resolvedId}", ct);
            }
        }

        return null;
    }

    private async Task<CurrencyDefinitionModel?> GetDefinitionByIdAsync(Guid id, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.GetDefinitionByIdAsync");
        return await _definitionStore.GetAsync($"{DEF_PREFIX}{id}", ct);
    }

    private async Task<WalletModel?> GetWalletByIdAsync(Guid id, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.GetWalletByIdAsync");
        return await _walletStore.GetAsync($"{WALLET_PREFIX}{id}", ct);
    }

    private async Task<WalletModel?> ResolveWalletAsync(Guid? walletId, Guid? ownerId, EntityType? ownerType, Guid? realmId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.ResolveWalletAsync");
        if (walletId.HasValue)
        {
            return await GetWalletByIdAsync(walletId.Value, ct);
        }

        if (ownerId.HasValue && ownerType.HasValue)
        {
            var ownerKey = BuildOwnerKey(ownerId.Value, ownerType.Value, realmId);
            var resolvedId = await _walletStringStore.GetAsync($"{WALLET_OWNER_INDEX}{ownerKey}", ct);
            if (!string.IsNullOrEmpty(resolvedId) && Guid.TryParse(resolvedId, out var resolvedGuid))
            {
                return await GetWalletByIdAsync(resolvedGuid, ct);
            }
        }

        return null;
    }

    private async Task<(BalanceModel Balance, bool IsNew)> GetOrCreateBalanceAsync(Guid walletId, Guid currencyDefId, CancellationToken ct, string? earnCapResetTime = null)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.GetOrCreateBalanceAsync");
        var key = $"{BALANCE_PREFIX}{walletId}:{currencyDefId}";

        // Check Redis cache first (per IMPLEMENTATION TENETS - use defined cache stores)
        var cached = await TryGetBalanceFromCacheAsync(key, ct);
        if (cached != null)
            return (cached, false);

        // Cache miss - read from MySQL
        var balance = await _balanceStore.GetAsync(key, ct);

        if (balance != null)
        {
            // Populate cache for future reads
            await UpdateBalanceCacheAsync(key, balance, ct);
            return (balance, false);
        }

        // Create new balance (don't cache until saved)
        var resetTime = ParseResetTime(earnCapResetTime);
        return (new BalanceModel
        {
            WalletId = walletId,
            CurrencyDefinitionId = currencyDefId,
            Amount = 0,
            DailyEarned = 0,
            WeeklyEarned = 0,
            DailyResetAt = GetNextDailyReset(resetTime),
            WeeklyResetAt = GetNextWeeklyReset(resetTime),
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow
        }, true);
    }

    private async Task SaveBalanceAsync(BalanceModel balance, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.SaveBalanceAsync");
        var key = $"{BALANCE_PREFIX}{balance.WalletId}:{balance.CurrencyDefinitionId}";
        await _balanceStore.SaveAsync(key, balance, cancellationToken: ct);

        // Update Redis cache after MySQL write
        await UpdateBalanceCacheAsync(key, balance, ct);

        // Maintain wallet-balance index for efficient wallet queries
        await AddToListAsync(_balanceStringStore, $"{BALANCE_WALLET_INDEX}{balance.WalletId}", balance.CurrencyDefinitionId.ToString(), ct);

        // Maintain currency-balance reverse index for autogain task processing
        await AddToListAsync(_balanceStringStore, $"{BALANCE_CURRENCY_INDEX}{balance.CurrencyDefinitionId}", balance.WalletId.ToString(), ct);
    }

    /// <summary>
    /// Publishes a balance lifecycle event (created or updated) after a balance mutation.
    /// </summary>
    private async Task PublishBalanceLifecycleEventAsync(
        BalanceModel balance, WalletModel wallet, CurrencyDefinitionModel definition,
        bool isNew, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.PublishBalanceLifecycleEventAsync");

        if (isNew)
        {
            await _messageBus.PublishCurrencyBalanceCreatedAsync(new CurrencyBalanceCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                WalletId = balance.WalletId,
                CurrencyDefinitionId = balance.CurrencyDefinitionId,
                CurrencyCode = definition.Code,
                OwnerId = wallet.OwnerId,
                OwnerType = wallet.OwnerType,
                Amount = balance.Amount,
                CreatedAt = balance.CreatedAt,
                UpdatedAt = balance.LastModifiedAt
            }, ct);
        }
        else
        {
            await _messageBus.PublishCurrencyBalanceUpdatedAsync(new CurrencyBalanceUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                WalletId = balance.WalletId,
                CurrencyDefinitionId = balance.CurrencyDefinitionId,
                CurrencyCode = definition.Code,
                OwnerId = wallet.OwnerId,
                OwnerType = wallet.OwnerType,
                Amount = balance.Amount,
                CreatedAt = balance.CreatedAt,
                UpdatedAt = balance.LastModifiedAt,
                ChangedFields = new List<string> { "amount" }
            }, ct);
        }
    }

    private async Task<List<BalanceModel>> GetAllBalancesForWalletAsync(Guid walletId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.GetAllBalancesForWalletAsync");

        var indexJson = await _balanceStringStore.GetAsync($"{BALANCE_WALLET_INDEX}{walletId}", ct);
        if (string.IsNullOrEmpty(indexJson))
            return new List<BalanceModel>();

        var currencyDefIds = BannouJson.Deserialize<List<string>>(indexJson) ?? new List<string>();
        var balances = new List<BalanceModel>();

        foreach (var currencyDefId in currencyDefIds)
        {
            var balance = await _balanceStore.GetAsync($"{BALANCE_PREFIX}{walletId}:{currencyDefId}", ct);
            if (balance is not null)
                balances.Add(balance);
        }

        return balances;
    }

    private async Task<List<BalanceSummary>> GetWalletBalanceSummariesAsync(Guid walletId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.GetWalletBalanceSummariesAsync");
        var balances = await GetAllBalancesForWalletAsync(walletId, ct);
        var summaries = new List<BalanceSummary>();

        foreach (var balance in balances)
        {
            var definition = await _definitionStore.GetAsync($"{DEF_PREFIX}{balance.CurrencyDefinitionId}", ct);
            if (definition is null) continue;

            var lockedAmount = await GetTotalHeldAmountAsync(balance.WalletId, balance.CurrencyDefinitionId, ct);
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
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.ApplyAutogainIfNeededAsync");
        if (!definition.AutogainEnabled || string.IsNullOrEmpty(definition.AutogainInterval))
            return balance;

        TimeSpan interval;
        try { interval = XmlConvert.ToTimeSpan(definition.AutogainInterval); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid autogain interval '{Interval}' for currency {CurrencyId}",
                definition.AutogainInterval, definition.DefinitionId);
            return balance;
        }

        if (interval.TotalSeconds <= 0) return balance;

        // Quick pre-check before acquiring lock (avoids locking when no autogain is due)
        // For game-time mode, the real-time pre-check is still a valid heuristic:
        // if real-time hasn't passed one interval, game-time hasn't either (game-time
        // ratio >= 1 means game-time advances faster than real-time, so real-time
        // underestimates game-time elapsed — the pre-check is conservative and correct).
        var now = DateTimeOffset.UtcNow;
        if (balance.LastAutogainAt is not null)
        {
            var elapsed = now - balance.LastAutogainAt.Value;
            var periodsElapsed = (int)(elapsed.TotalSeconds / interval.TotalSeconds);
            if (periodsElapsed <= 0) return balance;
        }

        // Acquire the BALANCE lock (same lock used by credit/debit/transfer) to prevent
        // race conditions where autogain save overwrites concurrent balance modifications
        var lockKey = $"{balance.WalletId}:{balance.CurrencyDefinitionId}";
        await using var balanceLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.CurrencyLock, lockKey,
            Guid.NewGuid().ToString(), _configuration.BalanceLockTimeoutSeconds, ct);
        if (!balanceLock.Success)
            return balance; // Skip, will be applied on next access

        // Re-read balance under lock to get authoritative state
        (balance, _) = await GetOrCreateBalanceAsync(
            balance.WalletId, balance.CurrencyDefinitionId, ct);

        now = DateTimeOffset.UtcNow;

        // First-time: initialize without retroactive gain
        if (balance.LastAutogainAt is null)
        {
            balance.LastAutogainAt = now;
            await SaveBalanceAsync(balance, ct);
            return balance;
        }

        // Resolve elapsed time based on time source (per-definition override wins, then global config)
        var useGameTime = definition.AutogainUseGameTime ?? (_configuration.AutogainTimeSource == TimeSource.GameTime);
        double elapsedSeconds;
        if (useGameTime && wallet.RealmId.HasValue)
        {
            try
            {
                var elapsedResponse = await _worldstateClient.GetElapsedGameTimeAsync(
                    new GetElapsedGameTimeRequest
                    {
                        RealmId = wallet.RealmId.Value,
                        FromRealTime = balance.LastAutogainAt.Value,
                        ToRealTime = now
                    }, ct);
                elapsedSeconds = elapsedResponse.TotalGameSeconds;
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Worldstate unavailable for realm {RealmId} during lazy autogain, skipping",
                    wallet.RealmId.Value);
                return balance;
            }
        }
        else
        {
            elapsedSeconds = (now - balance.LastAutogainAt.Value).TotalSeconds;
        }

        var periods = (int)(elapsedSeconds / interval.TotalSeconds);

        if (periods <= 0) return balance;

        var previousBalance = balance.Amount;
        double gain;

        if (definition.AutogainMode == AutogainMode.Compound)
        {
            var rate = definition.AutogainAmount ?? 0;
            var multiplier = Math.Pow(1 + rate, periods);
            gain = balance.Amount * (multiplier - 1);
        }
        else
        {
            gain = periods * (definition.AutogainAmount ?? 0);
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
        balance.LastAutogainAt = balance.LastAutogainAt.Value + TimeSpan.FromSeconds(interval.TotalSeconds * periods);
        balance.LastModifiedAt = now;

        await SaveBalanceAsync(balance, ct);

        await _messageBus.PublishCurrencyAutogainCalculatedAsync(new CurrencyAutogainCalculatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            WalletId = balance.WalletId,
            OwnerId = wallet.OwnerId,
            OwnerType = wallet.OwnerType,
            CurrencyDefinitionId = balance.CurrencyDefinitionId,
            CurrencyCode = definition.Code,
            PreviousBalance = previousBalance,
            PeriodsApplied = periods,
            AmountGained = gain,
            NewBalance = balance.Amount,
            AutogainMode = definition.AutogainMode ?? AutogainMode.Simple,
            CalculatedAt = now,
            PeriodsFrom = periodsFrom,
            PeriodsTo = balance.LastAutogainAt.Value
        }, ct);

        await PublishClientEventToWalletOwnerAsync(wallet.OwnerId, new CurrencyBalanceChangedClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            WalletId = balance.WalletId,
            CurrencyDefinitionId = balance.CurrencyDefinitionId,
            CurrencyCode = definition.Code,
            Amount = gain,
            NewBalance = balance.Amount,
            TransactionType = TransactionType.Autogain
        }, ct);

        // Dispatch DI listener notification for co-located L2 consumers (e.g., Genesis growth pipeline)
        await CurrencyTransactionDispatcher.DispatchCreditedAsync(
            _transactionListeners,
            new CurrencyTransactionNotification(
                WalletId: balance.WalletId,
                OwnerId: wallet.OwnerId,
                OwnerType: wallet.OwnerType,
                CurrencyDefinitionId: balance.CurrencyDefinitionId,
                CurrencyCode: definition.Code,
                Amount: gain,
                NewBalance: balance.Amount,
                TransactionType: TransactionType.Autogain,
                OccurredAt: now),
            _telemetryProvider, _logger, ct);

        return balance;
    }

    private async Task<TransactionModel> RecordTransactionAsync(
        Guid? sourceWalletId, Guid? targetWalletId, Guid currencyDefId,
        double amount, TransactionType type, string? refType, Guid? refId,
        Guid? escrowId, string idempotencyKey,
        double? sourceBalBefore, double? sourceBalAfter,
        double? targetBalBefore, double? targetBalAfter,
        object? metadata, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.RecordTransactionAsync");
        var txId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var tx = new TransactionModel
        {
            TransactionId = txId,
            SourceWalletId = sourceWalletId,
            TargetWalletId = targetWalletId,
            CurrencyDefinitionId = currencyDefId,
            Amount = amount,
            TransactionType = type,
            ReferenceType = refType,
            ReferenceId = refId,
            EscrowId = escrowId,
            IdempotencyKey = idempotencyKey,
            Timestamp = now,
            SourceBalanceBefore = sourceBalBefore,
            SourceBalanceAfter = sourceBalAfter,
            TargetBalanceBefore = targetBalBefore,
            TargetBalanceAfter = targetBalAfter,
            Metadata = metadata
        };

        await _transactionStore.SaveAsync($"{TX_PREFIX}{txId}", tx, cancellationToken: ct);

        // Index by wallet
        if (sourceWalletId.HasValue)
            await AddToListAsync(_transactionStringStore, $"{TX_WALLET_INDEX}{sourceWalletId}", txId.ToString(), ct);
        if (targetWalletId.HasValue)
            await AddToListAsync(_transactionStringStore, $"{TX_WALLET_INDEX}{targetWalletId}", txId.ToString(), ct);

        // Index by reference
        if (refType is not null && refId.HasValue)
            await AddToListAsync(_transactionStringStore, $"{TX_REF_INDEX}{refType}:{refId}", txId.ToString(), ct);

        return tx;
    }

    private async Task<(bool isDuplicate, Guid? existingTxId)> CheckIdempotencyAsync(string key, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.CheckIdempotencyAsync");
        var existing = await _idempotencyStore.GetAsync(key, ct);
        if (!string.IsNullOrEmpty(existing) && Guid.TryParse(existing, out var txId))
            return (true, txId);
        return (false, null);
    }

    private async Task RecordIdempotencyAsync(string key, string transactionId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.RecordIdempotencyAsync");
        await _idempotencyStore.SaveAsync(key, transactionId, new StateOptions { Ttl = _configuration.IdempotencyTtlSeconds }, ct);
    }

    private async Task InternalCreditAsync(Guid walletId, Guid currencyDefId, double amount,
        TransactionType type, string? refType, Guid? refId, string idempotencyKey, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.InternalCreditAsync");
        await CreditCurrencyAsync(new CreditCurrencyRequest
        {
            WalletId = walletId,
            CurrencyDefinitionId = currencyDefId,
            Amount = amount,
            TransactionType = type,
            ReferenceType = refType,
            ReferenceId = refId,
            IdempotencyKey = idempotencyKey,
            BypassEarnCap = true
        }, ct);
    }

    private async Task<double> GetTotalHeldAmountAsync(Guid walletId, Guid currencyDefId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.GetTotalHeldAmountAsync");

        var indexJson = await _holdStringStore.GetAsync($"{HOLD_WALLET_INDEX}{walletId}:{currencyDefId}", ct);
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
                hold = await _holdStore.GetAsync(holdKey, ct);
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

    private async Task<string?> FindBaseCurrencyCodeAsync(CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.FindBaseCurrencyCodeAsync");

        // O(1) lookup via dedicated base-currency index instead of iterating all definitions.
        // Check each scope (Global most likely for exchange rate pivot purposes).
        CurrencyScope[] scopes = [CurrencyScope.Global, CurrencyScope.RealmSpecific, CurrencyScope.MultiRealm];
        foreach (var scope in scopes)
        {
            var indexValue = await _definitionStringStore.GetAsync($"{BASE_CURRENCY_INDEX}{scope}", ct);
            if (!string.IsNullOrEmpty(indexValue))
            {
                // Index value format: "{definitionId}:{code}"
                var colonIndex = indexValue.IndexOf(':');
                return colonIndex >= 0 ? indexValue[(colonIndex + 1)..] : indexValue;
            }
        }

        return null;
    }
    private async Task AddToListAsync(IStateStore<string> store, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.AddToListAsync");
        for (var attempt = 0; attempt < _configuration.IndexLockMaxRetries; attempt++)
        {
            await using var lockResponse = await _lockProvider.LockAsync(
                StateStoreDefinitions.CurrencyLock, key, Guid.NewGuid().ToString(), _configuration.IndexLockTimeoutSeconds, ct);
            if (lockResponse.Success)
            {
                var existing = await store.GetAsync(key, ct);
                var list = string.IsNullOrEmpty(existing) ? new List<string>() : BannouJson.Deserialize<List<string>>(existing) ?? new List<string>();
                if (!list.Contains(value))
                {
                    list.Add(value);
                    await store.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: ct);
                }
                return;
            }

            _logger.LogDebug("Index lock contention for key {Key}, attempt {Attempt}", key, attempt + 1);
        }

        _logger.LogError("Failed to acquire index lock for key {Key} after {MaxRetries} attempts; index may be inconsistent",
            key, _configuration.IndexLockMaxRetries);
    }

    private async Task RemoveFromListAsync(IStateStore<string> store, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.RemoveFromListAsync");
        for (var attempt = 0; attempt < _configuration.IndexLockMaxRetries; attempt++)
        {
            await using var lockResponse = await _lockProvider.LockAsync(
                StateStoreDefinitions.CurrencyLock, key, Guid.NewGuid().ToString(), _configuration.IndexLockTimeoutSeconds, ct);
            if (lockResponse.Success)
            {
                var existing = await store.GetAsync(key, ct);
                if (string.IsNullOrEmpty(existing)) return;

                var list = BannouJson.Deserialize<List<string>>(existing) ?? new List<string>();
                if (list.Remove(value))
                {
                    await store.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: ct);
                }
                return;
            }

            _logger.LogDebug("Index lock contention for removal on key {Key}, attempt {Attempt}", key, attempt + 1);
        }

        _logger.LogError("Failed to acquire index lock for removal on key {Key} after {MaxRetries} attempts; index may be inconsistent",
            key, _configuration.IndexLockMaxRetries);
    }
    #region Balance Cache Helpers

    /// <summary>
    /// Attempts to retrieve a balance from the Redis cache.
    /// Uses constructor-cached balance cache store per FOUNDATION TENETS.
    /// </summary>
    private async Task<BalanceModel?> TryGetBalanceFromCacheAsync(string key, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.TryGetBalanceFromCacheAsync");
        try
        {
            return await _balanceCacheStore.GetAsync(key, ct);
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
    /// Uses constructor-cached balance cache store per FOUNDATION TENETS.
    /// </summary>
    private async Task UpdateBalanceCacheAsync(string key, BalanceModel balance, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.UpdateBalanceCacheAsync");
        try
        {
            await _balanceCacheStore.SaveAsync(key, balance, new StateOptions { Ttl = _configuration.BalanceCacheTtlSeconds }, ct);
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
    /// Uses constructor-cached hold cache store per FOUNDATION TENETS.
    /// </summary>
    private async Task<HoldModel?> TryGetHoldFromCacheAsync(string key, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.TryGetHoldFromCacheAsync");
        try
        {
            return await _holdCacheStore.GetAsync(key, ct);
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
    /// Uses constructor-cached hold cache store per FOUNDATION TENETS.
    /// </summary>
    private async Task UpdateHoldCacheAsync(string key, HoldModel hold, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.UpdateHoldCacheAsync");
        try
        {
            await _holdCacheStore.SaveAsync(key, hold, new StateOptions { Ttl = _configuration.HoldCacheTtlSeconds }, ct);
        }
        catch (Exception ex)
        {
            // Cache write failure is non-fatal
            _logger.LogWarning(ex, "Failed to update hold cache for {Key}", key);
        }
    }

    #endregion
    #region Client Event Publishing

    /// <summary>
    /// Publishes a client event to all sessions registered for the given wallet owner.
    /// Uses the Entity Session Registry with entity type "currency" to resolve sessions.
    /// </summary>
    private async Task PublishClientEventToWalletOwnerAsync<TEvent>(
        Guid ownerId, TEvent clientEvent, CancellationToken ct = default)
        where TEvent : BaseClientEvent
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.currency", "CurrencyService.PublishClientEventToWalletOwner");
        try
        {
            var count = await _entitySessionRegistry.PublishToEntitySessionsAsync(
                "currency", ownerId, clientEvent, ct);
            _logger.LogDebug("Published {EventName} to {SessionCount} sessions for owner {OwnerId}",
                clientEvent.EventName, count, ownerId);
        }
        catch (Exception ex)
        {
            // Client event delivery failure is non-fatal; the API operation already succeeded
            _logger.LogWarning(ex, "Failed to publish {EventName} to owner {OwnerId} sessions",
                clientEvent.EventName, ownerId);
        }
    }

    #endregion
}
