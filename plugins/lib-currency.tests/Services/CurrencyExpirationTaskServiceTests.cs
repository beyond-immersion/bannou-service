#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Currency.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Currency.Tests.Services;

/// <summary>
/// Unit tests for CurrencyExpirationTaskService.
/// Tests the background worker that scans balances with expiration policies
/// and zeroes expired amounts, publishing currency.expired events.
/// Uses the Capture Pattern per TESTING-PATTERNS.md.
/// </summary>
public class CurrencyExpirationTaskServiceTests
{
    #region Test Infrastructure

    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceProvider> _mockScopedProvider;
    private readonly Mock<ILogger<CurrencyExpirationTaskService>> _mockLogger;
    private readonly CurrencyServiceConfiguration _configuration;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    // Stores resolved from scope
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;

    // Typed stores
    private readonly Mock<IStateStore<CurrencyDefinitionModel>> _mockDefStore;
    private readonly Mock<IStateStore<BalanceModel>> _mockBalanceStore;
    private readonly Mock<IStateStore<BalanceModel>> _mockBalanceCacheStore;
    private readonly Mock<IStateStore<TransactionModel>> _mockTxStore;
    private readonly Mock<IStateStore<WalletModel>> _mockWalletStore;

    // String stores
    private readonly Mock<IStateStore<string>> _mockDefStringStore;
    private readonly Mock<IStateStore<string>> _mockBalanceStringStore;
    private readonly Mock<IStateStore<string>> _mockTxStringStore;

    // Capture lists
    private readonly List<(string Key, BalanceModel Model)> _capturedBalances;
    private readonly List<(string Key, TransactionModel Model)> _capturedTransactions;
    private readonly List<(string Topic, object Event)> _capturedEvents;

    // Reusable test data
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly Guid _definitionId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();

    public CurrencyExpirationTaskServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScopedProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<CurrencyExpirationTaskService>>();
        _configuration = new CurrencyServiceConfiguration
        {
            CurrencyExpirationTaskStartupDelaySeconds = 0,
            CurrencyExpirationTaskIntervalMs = 100,
            CurrencyExpirationBatchSize = 100,
            BalanceLockTimeoutSeconds = 30,
            IndexLockTimeoutSeconds = 15,
            IndexLockMaxRetries = 3
        };
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();

        _mockDefStore = new Mock<IStateStore<CurrencyDefinitionModel>>();
        _mockBalanceStore = new Mock<IStateStore<BalanceModel>>();
        _mockBalanceCacheStore = new Mock<IStateStore<BalanceModel>>();
        _mockTxStore = new Mock<IStateStore<TransactionModel>>();
        _mockWalletStore = new Mock<IStateStore<WalletModel>>();
        _mockDefStringStore = new Mock<IStateStore<string>>();
        _mockBalanceStringStore = new Mock<IStateStore<string>>();
        _mockTxStringStore = new Mock<IStateStore<string>>();

        _capturedBalances = new List<(string, BalanceModel)>();
        _capturedTransactions = new List<(string, TransactionModel)>();
        _capturedEvents = new List<(string, object)>();

        // Wire up scope creation
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockScopeFactory.Object);
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.ServiceProvider).Returns(_mockScopedProvider.Object);

        // Wire up scoped service resolution
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Returns(_mockStateStoreFactory.Object);
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IMessageBus)))
            .Returns(_mockMessageBus.Object);
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IDistributedLockProvider)))
            .Returns(_mockLockProvider.Object);

        // Wire up state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions))
            .Returns(_mockDefStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions))
            .Returns(_mockDefStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalances))
            .Returns(_mockBalanceStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyBalances))
            .Returns(_mockBalanceStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalanceCache))
            .Returns(_mockBalanceCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<TransactionModel>(StateStoreDefinitions.CurrencyTransactions))
            .Returns(_mockTxStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyTransactions))
            .Returns(_mockTxStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets))
            .Returns(_mockWalletStore.Object);

        // Setup captures for balance store saves
        _mockBalanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BalanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BalanceModel, StateOptions?, CancellationToken>((k, m, _, _) =>
                _capturedBalances.Add((k, m)))
            .ReturnsAsync("etag");

        // Setup captures for balance cache store saves
        _mockBalanceCacheStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BalanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup captures for transaction store saves
        _mockTxStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<TransactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransactionModel, StateOptions?, CancellationToken>((k, m, _, _) =>
                _capturedTransactions.Add((k, m)))
            .ReturnsAsync("etag");

        // Setup event capture for CurrencyExpiredEvent
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<CurrencyExpiredEvent>(
                It.IsAny<string>(), It.IsAny<CurrencyExpiredEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, CurrencyExpiredEvent, CancellationToken>((topic, evt, _) =>
                _capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        // Default: string store saves succeed
        _mockDefStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockBalanceStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockTxStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Default: lock provider succeeds
        SetupLockSuccess();
    }

    private CurrencyExpirationTaskService CreateService()
    {
        return new CurrencyExpirationTaskService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockTelemetryProvider.Object);
    }

    /// <summary>
    /// Runs the worker for exactly one cycle by cancelling after a short delay.
    /// Disposes the service after completion.
    /// </summary>
    private async Task RunOneCycleAsync()
    {
        using var service = CreateService();
        using var cts = new CancellationTokenSource();
        var task = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        await service.StopAsync(CancellationToken.None);
    }

    private void SetupLockSuccess()
    {
        var successLock = new Mock<ILockResponse>();
        successLock.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);
    }

    private void SetupLockFailure()
    {
        var failLock = new Mock<ILockResponse>();
        failLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failLock.Object);
    }

    #endregion

    #region No Work Scenarios

    [Fact]
    public async Task ProcessCycle_NoCurrencyDefinitions_DoesNothing()
    {
        // Arrange - no definitions exist
        _mockDefStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await RunOneCycleAsync();

        // Assert - no balance operations performed
        Assert.Empty(_capturedBalances);
        Assert.Empty(_capturedTransactions);
        Assert.Empty(_capturedEvents);
    }

    [Fact]
    public async Task ProcessCycle_NonExpiringCurrency_SkipsCurrency()
    {
        // Arrange - definition exists but doesn't expire
        var defId = _definitionId.ToString();
        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionModel
            {
                DefinitionId = _definitionId,
                Code = "GOLD",
                Name = "Gold",
                Expires = false,
                ExpirationPolicy = null
            });

        // Act
        await RunOneCycleAsync();

        // Assert - no expiration processing
        Assert.Empty(_capturedBalances);
        Assert.Empty(_capturedEvents);
    }

    [Fact]
    public async Task ProcessCycle_EndOfSeasonPolicy_SkipsCurrency()
    {
        // Arrange - definition with EndOfSeason policy (deferred)
        var defId = _definitionId.ToString();
        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionModel
            {
                DefinitionId = _definitionId,
                Code = "SEASON",
                Name = "Season Points",
                Expires = true,
                ExpirationPolicy = ExpirationPolicy.EndOfSeason
            });

        // Act
        await RunOneCycleAsync();

        // Assert - EndOfSeason is deferred, no processing
        Assert.Empty(_capturedBalances);
        Assert.Empty(_capturedEvents);
    }

    #endregion

    #region FixedDate Expiration

    [Fact]
    public async Task ProcessCycle_FixedDate_NotYetExpired_SkipsBalance()
    {
        // Arrange - FixedDate in the future
        var defId = _definitionId.ToString();
        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionModel
            {
                DefinitionId = _definitionId,
                Code = "EVENT",
                Name = "Event Token",
                Expires = true,
                ExpirationPolicy = ExpirationPolicy.FixedDate,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(30) // Future date
            });

        // Act
        await RunOneCycleAsync();

        // Assert - not yet expired globally, no processing
        Assert.Empty(_capturedBalances);
        Assert.Empty(_capturedEvents);
    }

    [Fact]
    public async Task ProcessCycle_FixedDate_Expired_ZeroesBalanceAndPublishesEvent()
    {
        // Arrange
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionModel
            {
                DefinitionId = _definitionId,
                Code = "EVENT",
                Name = "Event Token",
                Expires = true,
                ExpirationPolicy = ExpirationPolicy.FixedDate,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(-1) // Past date
            });

        // Wallet index for this currency
        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        // Balance exists with amount
        var balance = new BalanceModel
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 500,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            LastModifiedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };
        _mockBalanceStore
            .Setup(s => s.GetAsync($"bal:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        // Wallet exists for event population
        _mockWalletStore
            .Setup(s => s.GetAsync($"wallet:{walletIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletModel
            {
                WalletId = _walletId,
                OwnerId = _ownerId,
                OwnerType = EntityType.Account,
                Status = WalletStatus.Active
            });

        // Transaction index returns empty
        _mockTxStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await RunOneCycleAsync();

        // Assert - balance zeroed
        Assert.Single(_capturedBalances);
        var (balKey, savedBalance) = _capturedBalances[0];
        Assert.Contains(walletIdStr, balKey);
        Assert.Equal(0, savedBalance.Amount);

        // Assert - expiration transaction recorded
        Assert.Single(_capturedTransactions);
        var (_, savedTx) = _capturedTransactions[0];
        Assert.Equal(TransactionType.Expiration, savedTx.TransactionType);
        Assert.Equal(500, savedTx.Amount);
        Assert.Equal(500, savedTx.TargetBalanceBefore);
        Assert.Equal(0, savedTx.TargetBalanceAfter);

        // Assert - event published
        Assert.Single(_capturedEvents);
        var evt = _capturedEvents[0].Event as CurrencyExpiredEvent;
        Assert.NotNull(evt);
        Assert.Equal(_walletId, evt.WalletId);
        Assert.Equal(_ownerId, evt.OwnerId);
        Assert.Equal(_definitionId, evt.CurrencyDefinitionId);
        Assert.Equal("EVENT", evt.CurrencyCode);
        Assert.Equal(500, evt.AmountExpired);
        Assert.Equal(ExpirationPolicy.FixedDate, evt.ExpirationPolicy);
    }

    [Fact]
    public async Task ProcessCycle_FixedDate_ZeroBalance_SkipsBalance()
    {
        // Arrange
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionModel
            {
                DefinitionId = _definitionId,
                Code = "EVENT",
                Name = "Event Token",
                Expires = true,
                ExpirationPolicy = ExpirationPolicy.FixedDate,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(-1)
            });

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        // Balance already at zero
        _mockBalanceStore
            .Setup(s => s.GetAsync($"bal:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BalanceModel
            {
                WalletId = _walletId,
                CurrencyDefinitionId = _definitionId,
                Amount = 0,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
            });

        // Act
        await RunOneCycleAsync();

        // Assert - nothing written or published
        Assert.Empty(_capturedBalances);
        Assert.Empty(_capturedTransactions);
        Assert.Empty(_capturedEvents);
    }

    #endregion

    #region DurationFromEarn Expiration

    [Fact]
    public async Task ProcessCycle_DurationFromEarn_NotYetExpired_SkipsBalance()
    {
        // Arrange - balance earned recently, within duration
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionModel
            {
                DefinitionId = _definitionId,
                Code = "BONUS",
                Name = "Bonus Points",
                Expires = true,
                ExpirationPolicy = ExpirationPolicy.DurationFromEarn,
                ExpirationDuration = "P30D" // 30-day ISO 8601 duration
            });

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        // Balance earned 10 days ago (within 30-day window)
        _mockBalanceStore
            .Setup(s => s.GetAsync($"bal:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BalanceModel
            {
                WalletId = _walletId,
                CurrencyDefinitionId = _definitionId,
                Amount = 100,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
            });

        // Act
        await RunOneCycleAsync();

        // Assert - not yet expired, no processing
        Assert.Empty(_capturedBalances);
        Assert.Empty(_capturedEvents);
    }

    [Fact]
    public async Task ProcessCycle_DurationFromEarn_Expired_ZeroesBalance()
    {
        // Arrange - balance earned 60 days ago, duration is 30 days
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionModel
            {
                DefinitionId = _definitionId,
                Code = "BONUS",
                Name = "Bonus Points",
                Expires = true,
                ExpirationPolicy = ExpirationPolicy.DurationFromEarn,
                ExpirationDuration = "P30D" // 30 days
            });

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        // Balance earned 60 days ago (past 30-day window)
        _mockBalanceStore
            .Setup(s => s.GetAsync($"bal:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BalanceModel
            {
                WalletId = _walletId,
                CurrencyDefinitionId = _definitionId,
                Amount = 250,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
                LastModifiedAt = DateTimeOffset.UtcNow.AddDays(-5)
            });

        // Wallet for event
        _mockWalletStore
            .Setup(s => s.GetAsync($"wallet:{walletIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletModel
            {
                WalletId = _walletId,
                OwnerId = _ownerId,
                OwnerType = EntityType.Account,
                Status = WalletStatus.Active
            });

        _mockTxStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await RunOneCycleAsync();

        // Assert - balance zeroed
        Assert.Single(_capturedBalances);
        Assert.Equal(0, _capturedBalances[0].Model.Amount);

        // Assert - transaction recorded
        Assert.Single(_capturedTransactions);
        Assert.Equal(TransactionType.Expiration, _capturedTransactions[0].Model.TransactionType);
        Assert.Equal(250, _capturedTransactions[0].Model.Amount);

        // Assert - event published with DurationFromEarn policy
        Assert.Single(_capturedEvents);
        var evt = _capturedEvents[0].Event as CurrencyExpiredEvent;
        Assert.NotNull(evt);
        Assert.Equal(ExpirationPolicy.DurationFromEarn, evt.ExpirationPolicy);
        Assert.Equal(250, evt.AmountExpired);
    }

    #endregion

    #region Lock Failure

    [Fact]
    public async Task ProcessCycle_LockFailed_SkipsBalance()
    {
        // Arrange
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionModel
            {
                DefinitionId = _definitionId,
                Code = "EVENT",
                Name = "Event Token",
                Expires = true,
                ExpirationPolicy = ExpirationPolicy.FixedDate,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(-1)
            });

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        _mockBalanceStore
            .Setup(s => s.GetAsync($"bal:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BalanceModel
            {
                WalletId = _walletId,
                CurrencyDefinitionId = _definitionId,
                Amount = 100,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
            });

        // Lock fails
        SetupLockFailure();

        // Act
        await RunOneCycleAsync();

        // Assert - balance not modified, will be processed next cycle
        Assert.Empty(_capturedBalances);
        Assert.Empty(_capturedEvents);
    }

    #endregion

    #region Per-Item Error Isolation

    [Fact]
    public async Task ProcessCycle_PerItemError_ContinuesProcessingOtherBalances()
    {
        // Arrange - two wallets with same expiring currency, first throws error
        var defId = _definitionId.ToString();
        var wallet1 = Guid.NewGuid();
        var wallet2 = Guid.NewGuid();
        var wallet1Str = wallet1.ToString();
        var wallet2Str = wallet2.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionModel
            {
                DefinitionId = _definitionId,
                Code = "EVENT",
                Name = "Event Token",
                Expires = true,
                ExpirationPolicy = ExpirationPolicy.FixedDate,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(-1)
            });

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { wallet1Str, wallet2Str }));

        // First wallet throws on balance read
        _mockBalanceStore
            .Setup(s => s.GetAsync($"bal:{wallet1Str}:{defId}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated store error"));

        // Second wallet has a valid balance
        _mockBalanceStore
            .Setup(s => s.GetAsync($"bal:{wallet2Str}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BalanceModel
            {
                WalletId = wallet2,
                CurrencyDefinitionId = _definitionId,
                Amount = 300,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
            });

        _mockWalletStore
            .Setup(s => s.GetAsync($"wallet:{wallet2Str}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletModel
            {
                WalletId = wallet2,
                OwnerId = _ownerId,
                OwnerType = EntityType.Account,
                Status = WalletStatus.Active
            });

        _mockTxStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await RunOneCycleAsync();

        // Assert - second wallet's balance was still processed despite first wallet error
        Assert.Single(_capturedBalances);
        Assert.Equal(0, _capturedBalances[0].Model.Amount);
        Assert.Single(_capturedEvents);
    }

    #endregion
}
