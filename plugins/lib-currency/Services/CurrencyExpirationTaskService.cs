#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Xml;
using static BeyondImmersion.BannouService.Currency.CurrencyKeys;

namespace BeyondImmersion.BannouService.Currency.Services;

/// <summary>
/// Background service that scans balances with expiration policies and zeroes expired amounts.
/// Supports FixedDate (global cutoff) and DurationFromEarn (per-balance cutoff) policies.
/// EndOfSeason is deferred until Worldstate integration is implemented.
/// </summary>
public class CurrencyExpirationTaskService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CurrencyExpirationTaskService> _logger;
    private readonly CurrencyServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new CurrencyExpirationTaskService.
    /// </summary>
    public CurrencyExpirationTaskService(
        IServiceProvider serviceProvider,
        ILogger<CurrencyExpirationTaskService> logger,
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
            "Currency expiration task service starting, interval: {IntervalMs}ms, batch size: {BatchSize}",
            _configuration.CurrencyExpirationTaskIntervalMs, _configuration.CurrencyExpirationBatchSize);

        // Wait before first cycle to allow services to start
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.CurrencyExpirationTaskStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpirationCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during currency expiration processing cycle");
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    "currency", "CurrencyExpirationTask", ex, _logger, stoppingToken);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_configuration.CurrencyExpirationTaskIntervalMs), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Currency expiration task service stopped");
    }

    /// <summary>
    /// Processes a single expiration cycle: finds currencies with expiration policies
    /// and zeroes expired balances.
    /// </summary>
    private async Task ProcessExpirationCycleAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyExpirationTaskService.ProcessExpirationCycleAsync");
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();

        // Acquire all stores once per scope (FOUNDATION TENETS: BackgroundService store access)
        var defStore = stateStoreFactory.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions);
        var defStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions);
        var balanceStore = stateStoreFactory.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalances);
        var balanceStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyBalances);
        var balanceCacheStore = stateStoreFactory.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalanceCache);
        var txStore = stateStoreFactory.GetStore<TransactionModel>(StateStoreDefinitions.CurrencyTransactions);
        var txStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyTransactions);
        var walletStore = stateStoreFactory.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets);

        // Get all definition IDs
        var allDefsJson = await defStringStore.GetAsync(ALL_DEFS_KEY, cancellationToken);
        if (string.IsNullOrEmpty(allDefsJson))
        {
            _logger.LogDebug("No currency definitions found for expiration processing");
            return;
        }

        var allIds = BannouJson.Deserialize<List<string>>(allDefsJson) ?? new List<string>();
        var totalExpired = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var defId in allIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var definition = await defStore.GetAsync($"{DEF_PREFIX}{defId}", cancellationToken);
            if (definition is null || !definition.Expires || definition.ExpirationPolicy is null)
                continue;

            // FixedDate: check global cutoff before iterating wallets
            if (definition.ExpirationPolicy == ExpirationPolicy.FixedDate)
            {
                if (definition.ExpirationDate is null || now < definition.ExpirationDate.Value)
                    continue; // Not yet expired globally
            }
            else if (definition.ExpirationPolicy == ExpirationPolicy.EndOfSeason)
            {
                // Deferred: requires Worldstate integration for season-end resolution
                continue;
            }
            // DurationFromEarn: per-balance cutoff evaluated below

            var expired = await ProcessExpirationForCurrencyAsync(
                definition, now, balanceStore, balanceStringStore, balanceCacheStore,
                txStore, txStringStore, walletStore, messageBus, lockProvider, cancellationToken);
            totalExpired += expired;
        }

        if (totalExpired > 0)
        {
            _logger.LogInformation("Currency expiration cycle complete: expired {Count} balances", totalExpired);
        }
        else
        {
            _logger.LogDebug("Currency expiration cycle complete: no balances needed expiring");
        }
    }

    /// <summary>
    /// Processes expiration for all wallets holding a specific expiring currency.
    /// </summary>
    private async Task<int> ProcessExpirationForCurrencyAsync(
        CurrencyDefinitionModel definition,
        DateTimeOffset now,
        IStateStore<BalanceModel> balanceStore,
        IStateStore<string> balanceStringStore,
        IStateStore<BalanceModel> balanceCacheStore,
        IStateStore<TransactionModel> txStore,
        IStateStore<string> txStringStore,
        IStateStore<WalletModel> walletStore,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyExpirationTaskService.ProcessExpirationForCurrencyAsync");

        // Get all wallet IDs with this currency via reverse index
        var indexJson = await balanceStringStore.GetAsync(
            $"{BALANCE_CURRENCY_INDEX}{definition.DefinitionId}", cancellationToken);

        if (string.IsNullOrEmpty(indexJson))
            return 0;

        var walletIds = BannouJson.Deserialize<List<string>>(indexJson) ?? new List<string>();
        var expired = 0;

        // Parse duration once for DurationFromEarn policy
        TimeSpan? duration = null;
        if (definition.ExpirationPolicy == ExpirationPolicy.DurationFromEarn
            && !string.IsNullOrEmpty(definition.ExpirationDuration))
        {
            try
            {
                duration = XmlConvert.ToTimeSpan(definition.ExpirationDuration);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid expiration duration '{Duration}' for currency {CurrencyId}",
                    definition.ExpirationDuration, definition.DefinitionId);
                return 0;
            }
        }

        // Process in batches
        for (var i = 0; i < walletIds.Count; i += _configuration.CurrencyExpirationBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = walletIds.Skip(i).Take(_configuration.CurrencyExpirationBatchSize);
            foreach (var walletId in batch)
            {
                var result = await ProcessSingleBalanceExpirationAsync(
                    walletId, definition, now, duration, balanceStore, balanceCacheStore,
                    txStore, txStringStore, walletStore, messageBus, lockProvider, cancellationToken);
                if (result) expired++;
            }
        }

        return expired;
    }

    /// <summary>
    /// Processes expiration for a single balance. Uses double-read pattern:
    /// read before lock to detect candidates cheaply, then re-read under lock before writing.
    /// </summary>
    private async Task<bool> ProcessSingleBalanceExpirationAsync(
        string walletId,
        CurrencyDefinitionModel definition,
        DateTimeOffset now,
        TimeSpan? duration,
        IStateStore<BalanceModel> balanceStore,
        IStateStore<BalanceModel> balanceCacheStore,
        IStateStore<TransactionModel> txStore,
        IStateStore<string> txStringStore,
        IStateStore<WalletModel> walletStore,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyExpirationTaskService.ProcessSingleBalanceExpirationAsync");
        try
        {
            var balanceKey = $"{BALANCE_PREFIX}{walletId}:{definition.DefinitionId}";

            // Pre-lock read: cheap check for expiration candidates
            var balance = await balanceStore.GetAsync(balanceKey, cancellationToken);
            if (balance is null || balance.Amount <= 0)
                return false;

            // For DurationFromEarn, check per-balance cutoff
            if (definition.ExpirationPolicy == ExpirationPolicy.DurationFromEarn && duration is not null)
            {
                var cutoff = balance.CreatedAt + duration.Value;
                if (now < cutoff)
                    return false; // This individual balance has not yet expired
            }

            // Balance is expired — zero it out under distributed lock
            await using var lockResponse = await lockProvider.LockAsync(
                StateStoreDefinitions.CurrencyLock, $"{walletId}:{definition.DefinitionId}",
                Guid.NewGuid().ToString(), _configuration.BalanceLockTimeoutSeconds, cancellationToken);

            if (!lockResponse.Success)
                return false; // Skip this balance, will be processed next cycle

            // Re-read under lock: amount may have changed since pre-lock check
            balance = await balanceStore.GetAsync(balanceKey, cancellationToken);
            if (balance is null || balance.Amount <= 0)
                return false; // Already zeroed concurrently

            // Re-check per-balance cutoff under lock (CreatedAt doesn't change, but amount might)
            if (definition.ExpirationPolicy == ExpirationPolicy.DurationFromEarn && duration is not null)
            {
                var cutoff = balance.CreatedAt + duration.Value;
                if (now < cutoff)
                    return false;
            }

            var amountExpired = balance.Amount;
            var transactionId = Guid.NewGuid();

            // Zero out the balance
            balance.Amount = 0;
            balance.LastModifiedAt = now;
            await balanceStore.SaveAsync(balanceKey, balance, cancellationToken: cancellationToken);
            await balanceCacheStore.SaveAsync(balanceKey, balance, cancellationToken: cancellationToken);

            // Record expiration transaction (TransactionType = System) for history visibility
            var transaction = new TransactionModel
            {
                TransactionId = transactionId,
                TargetWalletId = Guid.Parse(walletId),
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = amountExpired,
                TransactionType = TransactionType.Expiration,
                Timestamp = now,
                TargetBalanceBefore = amountExpired,
                TargetBalanceAfter = 0
            };
            await txStore.SaveAsync($"{TX_PREFIX}{transactionId}", transaction, cancellationToken: cancellationToken);
            await AddToTransactionIndexAsync(txStringStore, walletId, transactionId.ToString(), lockProvider, cancellationToken);

            // Get wallet for owner info in event
            var wallet = await walletStore.GetAsync($"{WALLET_PREFIX}{walletId}", cancellationToken);
            if (wallet is null)
            {
                _logger.LogWarning("Wallet {WalletId} not found during expiration processing for currency {CurrencyId}",
                    walletId, definition.DefinitionId);
                return true; // Balance was zeroed successfully, just can't populate event owner fields
            }

            await messageBus.PublishCurrencyExpiredAsync(new CurrencyExpiredEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                TransactionId = transactionId,
                WalletId = wallet.WalletId,
                OwnerId = wallet.OwnerId,
                CurrencyDefinitionId = definition.DefinitionId,
                CurrencyCode = definition.Code,
                AmountExpired = amountExpired,
                ExpirationPolicy = definition.ExpirationPolicy ?? throw new InvalidOperationException("ExpirationPolicy was null after validation")
            }, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            // Per-item error isolation (IMPLEMENTATION TENETS): one corrupt balance must not block the rest
            _logger.LogWarning(ex, "Error processing currency expiration for wallet {WalletId}, currency {CurrencyId}",
                walletId, definition.DefinitionId);
            return false;
        }
    }

    /// <summary>
    /// Adds a transaction ID to the wallet's transaction index with distributed locking.
    /// Replicates the locked list append pattern from CurrencyService.AddToListAsync.
    /// </summary>
    private async Task AddToTransactionIndexAsync(
        IStateStore<string> txStringStore,
        string walletId,
        string transactionId,
        IDistributedLockProvider lockProvider,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyExpirationTaskService.AddToTransactionIndexAsync");
        var key = $"{TX_WALLET_INDEX}{walletId}";
        for (var attempt = 0; attempt < _configuration.IndexLockMaxRetries; attempt++)
        {
            await using var lockResponse = await lockProvider.LockAsync(
                StateStoreDefinitions.CurrencyLock, key, Guid.NewGuid().ToString(),
                _configuration.IndexLockTimeoutSeconds, cancellationToken);
            if (lockResponse.Success)
            {
                var existing = await txStringStore.GetAsync(key, cancellationToken);
                var list = string.IsNullOrEmpty(existing)
                    ? new List<string>()
                    : BannouJson.Deserialize<List<string>>(existing) ?? new List<string>();
                if (!list.Contains(transactionId))
                {
                    list.Add(transactionId);
                    await txStringStore.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: cancellationToken);
                }
                return;
            }

            _logger.LogDebug("Transaction index lock contention for wallet {WalletId}, attempt {Attempt}", walletId, attempt + 1);
        }

        _logger.LogError("Failed to acquire transaction index lock for wallet {WalletId} after {MaxRetries} attempts; index may be inconsistent",
            walletId, _configuration.IndexLockMaxRetries);
    }
}
