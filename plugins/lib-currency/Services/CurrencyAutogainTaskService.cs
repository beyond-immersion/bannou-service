#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Xml;

namespace BeyondImmersion.BannouService.Currency.Services;

/// <summary>
/// Background service that proactively applies autogain to all eligible wallets.
/// When AutogainProcessingMode is "task", this service periodically iterates wallets
/// with autogain-enabled currencies and applies gains without waiting for balance queries.
/// This ensures accurate global supply statistics, leaderboards, and timely cap events.
/// </summary>
public class CurrencyAutogainTaskService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CurrencyAutogainTaskService> _logger;
    private readonly CurrencyServiceConfiguration _configuration;

    // Key prefixes matching CurrencyService constants
    private const string DEF_PREFIX = "def:";
    private const string ALL_DEFS_KEY = "all-defs";
    private const string BALANCE_PREFIX = "bal:";
    private const string BALANCE_CURRENCY_INDEX = "bal-currency:";
    private const string WALLET_PREFIX = "wallet:";

    /// <summary>
    /// Creates a new CurrencyAutogainTaskService.
    /// </summary>
    public CurrencyAutogainTaskService(
        IServiceProvider serviceProvider,
        ILogger<CurrencyAutogainTaskService> logger,
        CurrencyServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration.AutogainProcessingMode != "task")
        {
            _logger.LogInformation("Autogain processing mode is '{Mode}', background task disabled",
                _configuration.AutogainProcessingMode);
            return;
        }

        _logger.LogInformation(
            "Autogain task service starting, interval: {IntervalMs}ms, batch size: {BatchSize}",
            _configuration.AutogainTaskIntervalMs, _configuration.AutogainBatchSize);

        // Wait before first cycle to allow services to start
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAutogainCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during autogain processing cycle");
                await TryPublishErrorAsync(ex, stoppingToken);
            }

            try
            {
                await Task.Delay(_configuration.AutogainTaskIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Autogain task service stopped");
    }

    /// <summary>
    /// Processes a single autogain cycle: finds autogain-enabled currencies
    /// and applies gains to all wallets with balances in those currencies.
    /// </summary>
    private async Task ProcessAutogainCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();

        var defStore = stateStoreFactory.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions);
        var stringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions);

        // Get all definition IDs
        var allDefsJson = await stringStore.GetAsync(ALL_DEFS_KEY, cancellationToken);
        if (string.IsNullOrEmpty(allDefsJson))
        {
            _logger.LogDebug("No currency definitions found for autogain processing");
            return;
        }

        var allIds = BannouJson.Deserialize<List<string>>(allDefsJson) ?? new List<string>();
        var totalProcessed = 0;

        foreach (var defId in allIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var definition = await defStore.GetAsync($"{DEF_PREFIX}{defId}", cancellationToken);
            if (definition is null || !definition.AutogainEnabled || string.IsNullOrEmpty(definition.AutogainInterval))
                continue;

            var processed = await ProcessAutogainForCurrencyAsync(
                definition, stateStoreFactory, messageBus, lockProvider, cancellationToken);
            totalProcessed += processed;
        }

        if (totalProcessed > 0)
        {
            _logger.LogInformation("Autogain cycle complete: processed {Count} balances", totalProcessed);
        }
        else
        {
            _logger.LogDebug("Autogain cycle complete: no balances needed processing");
        }
    }

    /// <summary>
    /// Processes autogain for all wallets that hold a specific autogain-enabled currency.
    /// Iterates wallets in batches of the configured batch size.
    /// </summary>
    private async Task<int> ProcessAutogainForCurrencyAsync(
        CurrencyDefinitionModel definition,
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        CancellationToken cancellationToken)
    {
        var balanceStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CurrencyBalances);
        var balanceStore = stateStoreFactory.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalances);
        var walletStore = stateStoreFactory.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets);

        // Get all wallet IDs with this currency via reverse index
        var indexJson = await balanceStringStore.GetAsync(
            $"{BALANCE_CURRENCY_INDEX}{definition.DefinitionId}", cancellationToken);

        if (string.IsNullOrEmpty(indexJson))
            return 0;

        var walletIds = BannouJson.Deserialize<List<string>>(indexJson) ?? new List<string>();
        var processed = 0;

        // Process in batches
        for (var i = 0; i < walletIds.Count; i += _configuration.AutogainBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = walletIds.Skip(i).Take(_configuration.AutogainBatchSize);
            var tasks = batch.Select(walletId =>
                ProcessSingleBalanceAutogainAsync(
                    walletId, definition, balanceStore, walletStore, messageBus, lockProvider, cancellationToken));

            var results = await Task.WhenAll(tasks);
            processed += results.Count(r => r);
        }

        return processed;
    }

    /// <summary>
    /// Applies autogain to a single wallet's balance for a specific currency.
    /// Uses distributed locking to prevent conflicts with concurrent lazy autogain.
    /// </summary>
    private async Task<bool> ProcessSingleBalanceAutogainAsync(
        string walletId,
        CurrencyDefinitionModel definition,
        IStateStore<BalanceModel> balanceStore,
        IStateStore<WalletModel> walletStore,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var balanceKey = $"{BALANCE_PREFIX}{walletId}:{definition.DefinitionId}";

            // Acquire lock to prevent concurrent modification with lazy autogain path
            await using var lockResponse = await lockProvider.LockAsync(
                "currency-autogain", $"{walletId}:{definition.DefinitionId}",
                Guid.NewGuid().ToString(), 10, cancellationToken);

            if (!lockResponse.Success)
                return false; // Skip this balance, will be processed next cycle

            var balance = await balanceStore.GetAsync(balanceKey, cancellationToken);
            if (balance is null)
                return false;

            var now = DateTimeOffset.UtcNow;

            // First-time: initialize without retroactive gain
            if (balance.LastAutogainAt is null)
            {
                balance.LastAutogainAt = now;
                await balanceStore.SaveAsync(balanceKey, balance, cancellationToken: cancellationToken);
                return false;
            }

            TimeSpan interval;
            try { interval = XmlConvert.ToTimeSpan(definition.AutogainInterval); }
            catch { return false; }

            if (interval.TotalSeconds <= 0) return false;

            var elapsed = now - balance.LastAutogainAt.Value;
            var periodsElapsed = (int)(elapsed.TotalSeconds / interval.TotalSeconds);

            if (periodsElapsed <= 0) return false;

            var previousBalance = balance.Amount;
            double gain;

            if (definition.AutogainMode == AutogainMode.Compound.ToString())
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

            if (gain <= 0) return false;

            var periodsFrom = balance.LastAutogainAt.Value;
            balance.Amount += gain;
            balance.LastAutogainAt = balance.LastAutogainAt.Value + TimeSpan.FromSeconds(interval.TotalSeconds * periodsElapsed);
            balance.LastModifiedAt = now;

            await balanceStore.SaveAsync(balanceKey, balance, cancellationToken: cancellationToken);

            // Get wallet for owner info in event
            var wallet = await walletStore.GetAsync($"{WALLET_PREFIX}{walletId}", cancellationToken);

            await messageBus.TryPublishAsync("currency.autogain.calculated", new CurrencyAutogainCalculatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                WalletId = Guid.Parse(balance.WalletId),
                OwnerId = wallet is not null ? Guid.Parse(wallet.OwnerId) : Guid.Empty,
                OwnerType = wallet?.OwnerType ?? "unknown",
                CurrencyDefinitionId = Guid.Parse(balance.CurrencyDefinitionId),
                CurrencyCode = definition.Code,
                PreviousBalance = previousBalance,
                PeriodsApplied = periodsElapsed,
                AmountGained = gain,
                NewBalance = balance.Amount,
                AutogainMode = definition.AutogainMode ?? AutogainMode.Simple.ToString(),
                CalculatedAt = now,
                PeriodsFrom = periodsFrom,
                PeriodsTo = balance.LastAutogainAt.Value
            }, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing autogain for wallet {WalletId}, currency {CurrencyId}",
                walletId, definition.DefinitionId);
            return false;
        }
    }

    /// <summary>
    /// Tries to publish an error event for autogain task failures.
    /// </summary>
    private async Task TryPublishErrorAsync(Exception ex, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await messageBus.TryPublishErrorAsync(
                "currency",
                "AutogainTask",
                ex.GetType().Name,
                ex.Message,
                severity: ServiceErrorEventSeverity.Error,
                cancellationToken: cancellationToken);
        }
        catch
        {
            // Don't let error publishing failures affect the loop
        }
    }

    /// <summary>
    /// Internal model for currency definitions (matches CurrencyService.CurrencyDefinitionModel).
    /// Duplicated here as the service model is internal to the scoped service.
    /// All fields included to prevent data loss during JSON round-trip.
    /// </summary>
    internal class CurrencyDefinitionModel
    {
        public string DefinitionId { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string Scope { get; set; } = "";
        public List<string>? RealmsAvailable { get; set; }
        public string Precision { get; set; } = "";
        public bool Transferable { get; set; } = true;
        public bool Tradeable { get; set; } = true;
        public bool? AllowNegative { get; set; }
        public double? PerWalletCap { get; set; }
        public string? CapOverflowBehavior { get; set; }
        public double? GlobalSupplyCap { get; set; }
        public double? DailyEarnCap { get; set; }
        public double? WeeklyEarnCap { get; set; }
        public string? EarnCapResetTime { get; set; }
        public bool AutogainEnabled { get; set; }
        public string? AutogainMode { get; set; }
        public double? AutogainAmount { get; set; }
        public string? AutogainInterval { get; set; }
        public double? AutogainCap { get; set; }
        public bool Expires { get; set; }
        public string? ExpirationPolicy { get; set; }
        public DateTimeOffset? ExpirationDate { get; set; }
        public string? ExpirationDuration { get; set; }
        public string? SeasonId { get; set; }
        public bool LinkedToItem { get; set; }
        public string? LinkedItemTemplateId { get; set; }
        public string? LinkageMode { get; set; }
        public bool IsBaseCurrency { get; set; }
        public double? ExchangeRateToBase { get; set; }
        public DateTimeOffset? ExchangeRateUpdatedAt { get; set; }
        public string? IconAssetId { get; set; }
        public string? DisplayFormat { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ModifiedAt { get; set; }
    }

    /// <summary>
    /// Internal model for balance records (matches CurrencyService.BalanceModel).
    /// All fields included to prevent data loss during JSON round-trip.
    /// </summary>
    internal class BalanceModel
    {
        public string WalletId { get; set; } = "";
        public string CurrencyDefinitionId { get; set; } = "";
        public double Amount { get; set; }
        public DateTimeOffset? LastAutogainAt { get; set; }
        public double DailyEarned { get; set; }
        public double WeeklyEarned { get; set; }
        public DateTimeOffset DailyResetAt { get; set; }
        public DateTimeOffset WeeklyResetAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastModifiedAt { get; set; }
    }

    /// <summary>
    /// Internal model for wallet ownership info (matches CurrencyService.WalletModel).
    /// Only read (never saved), so subset of fields is safe.
    /// </summary>
    internal class WalletModel
    {
        public string WalletId { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string OwnerType { get; set; } = "";
    }
}
