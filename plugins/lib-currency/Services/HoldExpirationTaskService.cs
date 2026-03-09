#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static BeyondImmersion.BannouService.Currency.CurrencyKeys;

namespace BeyondImmersion.BannouService.Currency.Services;

/// <summary>
/// Background service that scans authorization holds past their ExpiresAt timestamp
/// and auto-releases them, returning reserved funds to available balance.
/// Hold expiration is semantically identical to ReleaseHold — no balance record write
/// is needed because holds only reduce effective balance (Amount - sum(active holds)).
/// </summary>
public class HoldExpirationTaskService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HoldExpirationTaskService> _logger;
    private readonly CurrencyServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new HoldExpirationTaskService.
    /// </summary>
    public HoldExpirationTaskService(
        IServiceProvider serviceProvider,
        ILogger<HoldExpirationTaskService> logger,
        CurrencyServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Hold expiration task service starting, interval: {IntervalMs}ms, batch size: {BatchSize}",
            _configuration.HoldExpirationTaskIntervalMs, _configuration.HoldExpirationBatchSize);

        // Wait before first cycle to allow services to start
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.HoldExpirationTaskStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessHoldExpirationCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during hold expiration processing cycle");
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    "currency", "HoldExpirationTask", ex, _logger, stoppingToken);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_configuration.HoldExpirationTaskIntervalMs), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Hold expiration task service stopped");
    }

    /// <summary>
    /// Processes a single hold expiration cycle: iterates all currencies, finds wallets with
    /// active holds, and auto-releases any holds past their ExpiresAt timestamp.
    /// </summary>
    private async Task ProcessHoldExpirationCycleAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "HoldExpirationTaskService.ProcessHoldExpirationCycleAsync");
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();

        // Acquire all stores once per scope (FOUNDATION TENETS: BackgroundService store access)
        var defStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions);
        var balanceStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyBalances);
        var holdStore = stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHolds);
        var holdStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyHolds);
        var holdCacheStore = stateStoreFactory.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHoldsCache);
        var walletStore = stateStoreFactory.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets);

        // Get all definition IDs
        var allDefsJson = await defStringStore.GetAsync(ALL_DEFS_KEY, cancellationToken);
        if (string.IsNullOrEmpty(allDefsJson))
        {
            _logger.LogDebug("No currency definitions found for hold expiration processing");
            return;
        }

        var allIds = BannouJson.Deserialize<List<string>>(allDefsJson) ?? new List<string>();
        var totalExpired = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var defId in allIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Use the bal-currency reverse index to find wallets holding this currency,
            // then check each wallet's per-currency hold index for expired holds.
            // No global holds-by-currency index exists.
            var walletIndexJson = await balanceStringStore.GetAsync(
                $"{BALANCE_CURRENCY_INDEX}{defId}", cancellationToken);
            if (string.IsNullOrEmpty(walletIndexJson))
                continue;

            var walletIds = BannouJson.Deserialize<List<string>>(walletIndexJson) ?? new List<string>();

            // Process in batches
            for (var i = 0; i < walletIds.Count; i += _configuration.HoldExpirationBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = walletIds.Skip(i).Take(_configuration.HoldExpirationBatchSize);
                foreach (var walletId in batch)
                {
                    var expired = await ProcessHoldsForWalletCurrencyAsync(
                        walletId, defId, now, holdStore, holdStringStore, holdCacheStore,
                        walletStore, messageBus, lockProvider, cancellationToken);
                    totalExpired += expired;
                }
            }
        }

        if (totalExpired > 0)
        {
            _logger.LogInformation("Hold expiration cycle complete: expired {Count} holds", totalExpired);
        }
        else
        {
            _logger.LogDebug("Hold expiration cycle complete: no holds needed expiring");
        }
    }

    /// <summary>
    /// Processes expired holds for a specific wallet and currency combination.
    /// </summary>
    private async Task<int> ProcessHoldsForWalletCurrencyAsync(
        string walletId,
        string definitionId,
        DateTimeOffset now,
        IStateStore<HoldModel> holdStore,
        IStateStore<string> holdStringStore,
        IStateStore<HoldModel> holdCacheStore,
        IStateStore<WalletModel> walletStore,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "HoldExpirationTaskService.ProcessHoldsForWalletCurrencyAsync");

        var holdIndexKey = $"{HOLD_WALLET_INDEX}{walletId}:{definitionId}";
        var holdIndexJson = await holdStringStore.GetAsync(holdIndexKey, cancellationToken);
        if (string.IsNullOrEmpty(holdIndexJson))
            return 0;

        var holdIds = BannouJson.Deserialize<List<string>>(holdIndexJson) ?? new List<string>();
        var expired = 0;

        foreach (var holdId in holdIds)
        {
            var result = await ProcessSingleHoldExpirationAsync(
                holdId, walletId, definitionId, now, holdStore, holdStringStore,
                holdCacheStore, walletStore, messageBus, lockProvider, cancellationToken);
            if (result) expired++;
        }

        return expired;
    }

    /// <summary>
    /// Processes expiration for a single hold. Uses ETag-based optimistic concurrency
    /// under hold-level lock, matching CaptureHold/ReleaseHold patterns.
    /// </summary>
    private async Task<bool> ProcessSingleHoldExpirationAsync(
        string holdId,
        string walletId,
        string definitionId,
        DateTimeOffset now,
        IStateStore<HoldModel> holdStore,
        IStateStore<string> holdStringStore,
        IStateStore<HoldModel> holdCacheStore,
        IStateStore<WalletModel> walletStore,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "HoldExpirationTaskService.ProcessSingleHoldExpirationAsync");
        try
        {
            var holdKey = $"{HOLD_PREFIX}{holdId}";

            // Pre-lock check: read from cache first, fall back to MySQL
            var hold = await holdCacheStore.GetAsync(holdKey, cancellationToken)
                        ?? await holdStore.GetAsync(holdKey, cancellationToken);

            if (hold is null || hold.Status != HoldStatus.Active)
                return false; // Already captured, released, or gone

            if (hold.ExpiresAt > now)
                return false; // Not yet expired

            // Hold is expired — auto-release under hold-level distributed lock
            await using var lockResponse = await lockProvider.LockAsync(
                StateStoreDefinitions.CurrencyLock, holdId,
                Guid.NewGuid().ToString(), _configuration.HoldLockTimeoutSeconds, cancellationToken);

            if (!lockResponse.Success)
                return false; // Skip, will be processed next cycle

            // Re-read under lock with ETag for optimistic concurrency
            var (holdUnderLock, etag) = await holdStore.GetWithETagAsync(holdKey, cancellationToken);
            if (holdUnderLock is null || holdUnderLock.Status != HoldStatus.Active)
                return false; // Status changed while waiting for lock

            if (etag is null)
                return false; // Cannot perform optimistic concurrency without ETag

            // Set status = Released, completedAt = now (no balance modification needed)
            holdUnderLock.Status = HoldStatus.Released;
            holdUnderLock.CompletedAt = now;

            var savedEtag = await holdStore.TrySaveAsync(holdKey, holdUnderLock, etag, cancellationToken: cancellationToken);
            if (savedEtag is null)
            {
                // ETag conflict: a concurrent CaptureHold or ReleaseHold already handled this hold
                _logger.LogDebug("Hold {HoldId} was modified concurrently during expiration, skipping", holdId);
                return false;
            }

            // Update cache
            await holdCacheStore.SaveAsync(holdKey, holdUnderLock, cancellationToken: cancellationToken);

            // Remove from active hold index
            await RemoveFromHoldIndexAsync(holdStringStore, walletId, definitionId, holdId, lockProvider, cancellationToken);

            // Get wallet for owner info in event
            var wallet = await walletStore.GetAsync($"{WALLET_PREFIX}{walletId}", cancellationToken);
            if (wallet is null)
            {
                _logger.LogWarning("Wallet {WalletId} not found during hold expiration for hold {HoldId}",
                    walletId, holdId);
                return true; // Hold was released successfully, just can't populate event owner fields
            }

            await messageBus.PublishCurrencyHoldExpiredAsync(new CurrencyHoldExpiredEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                HoldId = holdUnderLock.HoldId,
                WalletId = wallet.WalletId,
                OwnerId = wallet.OwnerId,
                CurrencyDefinitionId = Guid.Parse(definitionId),
                Amount = holdUnderLock.Amount,
                OriginalExpiresAt = holdUnderLock.ExpiresAt
            }, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            // Per-item error isolation (IMPLEMENTATION TENETS): one corrupt hold must not block the rest
            _logger.LogWarning(ex, "Error processing hold expiration for hold {HoldId}, wallet {WalletId}",
                holdId, walletId);
            return false;
        }
    }

    /// <summary>
    /// Removes a hold ID from the wallet+currency hold index with distributed locking.
    /// Replicates the locked list removal pattern from CurrencyService.RemoveFromListAsync.
    /// </summary>
    private async Task RemoveFromHoldIndexAsync(
        IStateStore<string> holdStringStore,
        string walletId,
        string definitionId,
        string holdId,
        IDistributedLockProvider lockProvider,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "HoldExpirationTaskService.RemoveFromHoldIndexAsync");
        var key = $"{HOLD_WALLET_INDEX}{walletId}:{definitionId}";
        for (var attempt = 0; attempt < _configuration.IndexLockMaxRetries; attempt++)
        {
            await using var lockResponse = await lockProvider.LockAsync(
                StateStoreDefinitions.CurrencyLock, key, Guid.NewGuid().ToString(),
                _configuration.IndexLockTimeoutSeconds, cancellationToken);
            if (lockResponse.Success)
            {
                var existing = await holdStringStore.GetAsync(key, cancellationToken);
                if (string.IsNullOrEmpty(existing)) return;

                var list = BannouJson.Deserialize<List<string>>(existing) ?? new List<string>();
                if (list.Remove(holdId))
                {
                    await holdStringStore.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: cancellationToken);
                }
                return;
            }

            _logger.LogDebug("Hold index lock contention for wallet {WalletId}, currency {CurrencyId}, attempt {Attempt}",
                walletId, definitionId, attempt + 1);
        }

        _logger.LogError("Failed to acquire hold index lock for wallet {WalletId}, currency {CurrencyId} after {MaxRetries} attempts; index may be inconsistent",
            walletId, definitionId, _configuration.IndexLockMaxRetries);
    }
}
