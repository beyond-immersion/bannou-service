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
/// Unit tests for HoldExpirationTaskService.
/// Tests the background worker that scans authorization holds past their
/// ExpiresAt timestamp and auto-releases them, publishing currency.hold.expired events.
/// Uses the Capture Pattern per TESTING-PATTERNS.md.
/// </summary>
public class HoldExpirationTaskServiceTests
{
    #region Test Infrastructure

    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceProvider> _mockScopedProvider;
    private readonly Mock<ILogger<HoldExpirationTaskService>> _mockLogger;
    private readonly CurrencyServiceConfiguration _configuration;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    // Stores resolved from scope
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;

    // Typed stores
    private readonly Mock<IStateStore<HoldModel>> _mockHoldStore;
    private readonly Mock<IStateStore<HoldModel>> _mockHoldCacheStore;
    private readonly Mock<IStateStore<WalletModel>> _mockWalletStore;

    // String stores
    private readonly Mock<IStateStore<string>> _mockDefStringStore;
    private readonly Mock<IStateStore<string>> _mockBalanceStringStore;
    private readonly Mock<IStateStore<string>> _mockHoldStringStore;

    // Capture lists
    private readonly List<(string Key, HoldModel Model)> _capturedHolds;
    private readonly List<(string Topic, object Event)> _capturedEvents;

    // Reusable test data
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly Guid _definitionId = Guid.NewGuid();
    private readonly Guid _holdId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();

    public HoldExpirationTaskServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScopedProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<HoldExpirationTaskService>>();
        _configuration = new CurrencyServiceConfiguration
        {
            HoldExpirationTaskStartupDelaySeconds = 0,
            HoldExpirationTaskIntervalMs = 100,
            HoldExpirationBatchSize = 100,
            HoldLockTimeoutSeconds = 30,
            IndexLockTimeoutSeconds = 15,
            IndexLockMaxRetries = 3
        };
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();

        _mockHoldStore = new Mock<IStateStore<HoldModel>>();
        _mockHoldCacheStore = new Mock<IStateStore<HoldModel>>();
        _mockWalletStore = new Mock<IStateStore<WalletModel>>();
        _mockDefStringStore = new Mock<IStateStore<string>>();
        _mockBalanceStringStore = new Mock<IStateStore<string>>();
        _mockHoldStringStore = new Mock<IStateStore<string>>();

        _capturedHolds = new List<(string, HoldModel)>();
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
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions))
            .Returns(_mockDefStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyBalances))
            .Returns(_mockBalanceStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHolds))
            .Returns(_mockHoldStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyHolds))
            .Returns(_mockHoldStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHoldsCache))
            .Returns(_mockHoldCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets))
            .Returns(_mockWalletStore.Object);

        // Setup captures for hold store saves via TrySaveAsync (etag-based)
        _mockHoldStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<HoldModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, HoldModel, string, StateOptions?, CancellationToken>((k, m, _, _, _) =>
                _capturedHolds.Add((k, m)))
            .ReturnsAsync("new-etag");

        // Setup captures for hold cache store saves
        _mockHoldCacheStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<HoldModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup event capture for CurrencyHoldExpiredEvent
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<CurrencyHoldExpiredEvent>(
                It.IsAny<string>(), It.IsAny<CurrencyHoldExpiredEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, CurrencyHoldExpiredEvent, CancellationToken>((topic, evt, _) =>
                _capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        // Default: string store saves succeed
        _mockHoldStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Default: hold cache returns null (cache miss)
        _mockHoldCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HoldModel?)null);

        // Default: lock provider succeeds
        SetupLockSuccess();
    }

    private HoldExpirationTaskService CreateService()
    {
        return new HoldExpirationTaskService(
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
        await service.StopAsync(TestContext.Current.CancellationToken);
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
        // Arrange
        _mockDefStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await RunOneCycleAsync();

        // Assert
        Assert.Empty(_capturedHolds);
        Assert.Empty(_capturedEvents);
    }

    [Fact]
    public async Task ProcessCycle_NoWalletsWithCurrency_DoesNothing()
    {
        // Arrange
        var defId = _definitionId.ToString();
        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await RunOneCycleAsync();

        // Assert
        Assert.Empty(_capturedHolds);
        Assert.Empty(_capturedEvents);
    }

    #endregion

    #region Hold Expiration - Happy Path

    [Fact]
    public async Task ProcessCycle_ExpiredHold_ReleasesHoldAndPublishesEvent()
    {
        // Arrange
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();
        var holdIdStr = _holdId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        // Hold index for this wallet+currency
        _mockHoldStringStore
            .Setup(s => s.GetAsync($"hold-wallet:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { holdIdStr }));

        // Pre-lock cache check: expired hold
        var expiredHold = new HoldModel
        {
            HoldId = _holdId,
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            Status = HoldStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30), // Expired
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        _mockHoldCacheStore
            .Setup(s => s.GetAsync($"hold:{holdIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredHold);

        // Re-read under lock with ETag
        _mockHoldStore
            .Setup(s => s.GetWithETagAsync($"hold:{holdIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((expiredHold, "etag-123"));

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

        // Act
        await RunOneCycleAsync();

        // Assert - hold released (status set to Released)
        Assert.Single(_capturedHolds);
        var (holdKey, savedHold) = _capturedHolds[0];
        Assert.Contains(holdIdStr, holdKey);
        Assert.Equal(HoldStatus.Released, savedHold.Status);
        Assert.NotNull(savedHold.CompletedAt);

        // Assert - event published
        Assert.Single(_capturedEvents);
        var evt = _capturedEvents[0].Event as CurrencyHoldExpiredEvent;
        Assert.NotNull(evt);
        Assert.Equal(_holdId, evt.HoldId);
        Assert.Equal(_walletId, evt.WalletId);
        Assert.Equal(_ownerId, evt.OwnerId);
        Assert.Equal(_definitionId, evt.CurrencyDefinitionId);
        Assert.Equal(200, evt.Amount);
    }

    #endregion

    #region Hold Not Yet Expired

    [Fact]
    public async Task ProcessCycle_HoldNotYetExpired_SkipsHold()
    {
        // Arrange
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();
        var holdIdStr = _holdId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        _mockHoldStringStore
            .Setup(s => s.GetAsync($"hold-wallet:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { holdIdStr }));

        // Hold not yet expired
        _mockHoldCacheStore
            .Setup(s => s.GetAsync($"hold:{holdIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HoldModel
            {
                HoldId = _holdId,
                Status = HoldStatus.Active,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), // Future
                Amount = 100
            });

        // Act
        await RunOneCycleAsync();

        // Assert
        Assert.Empty(_capturedHolds);
        Assert.Empty(_capturedEvents);
    }

    #endregion

    #region Already Released/Captured

    [Fact]
    public async Task ProcessCycle_HoldAlreadyReleased_SkipsHold()
    {
        // Arrange
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();
        var holdIdStr = _holdId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        _mockHoldStringStore
            .Setup(s => s.GetAsync($"hold-wallet:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { holdIdStr }));

        // Hold already released
        _mockHoldCacheStore
            .Setup(s => s.GetAsync($"hold:{holdIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HoldModel
            {
                HoldId = _holdId,
                Status = HoldStatus.Released, // Already released
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                Amount = 100
            });

        // Act
        await RunOneCycleAsync();

        // Assert - no modifications
        Assert.Empty(_capturedHolds);
        Assert.Empty(_capturedEvents);
    }

    #endregion

    #region Lock and ETag Failure

    [Fact]
    public async Task ProcessCycle_LockFailed_SkipsHold()
    {
        // Arrange
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();
        var holdIdStr = _holdId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        _mockHoldStringStore
            .Setup(s => s.GetAsync($"hold-wallet:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { holdIdStr }));

        _mockHoldCacheStore
            .Setup(s => s.GetAsync($"hold:{holdIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HoldModel
            {
                HoldId = _holdId,
                Status = HoldStatus.Active,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                Amount = 100
            });

        // Lock fails
        SetupLockFailure();

        // Act
        await RunOneCycleAsync();

        // Assert - no modifications
        Assert.Empty(_capturedHolds);
        Assert.Empty(_capturedEvents);
    }

    [Fact]
    public async Task ProcessCycle_ETagConflict_SkipsHold()
    {
        // Arrange
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();
        var holdIdStr = _holdId.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        _mockHoldStringStore
            .Setup(s => s.GetAsync($"hold-wallet:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { holdIdStr }));

        var expiredHold = new HoldModel
        {
            HoldId = _holdId,
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Status = HoldStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            Amount = 100
        };

        _mockHoldCacheStore
            .Setup(s => s.GetAsync($"hold:{holdIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredHold);

        _mockHoldStore
            .Setup(s => s.GetWithETagAsync($"hold:{holdIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((expiredHold, "etag-123"));

        // ETag conflict - TrySaveAsync returns null
        _mockHoldStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<HoldModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await RunOneCycleAsync();

        // Assert - no event published (conflict means another operation handled the hold)
        Assert.Empty(_capturedEvents);
    }

    #endregion

    #region Per-Item Error Isolation

    [Fact]
    public async Task ProcessCycle_PerItemError_ContinuesProcessingOtherHolds()
    {
        // Arrange - two holds, first throws
        var defId = _definitionId.ToString();
        var walletIdStr = _walletId.ToString();
        var hold1 = Guid.NewGuid();
        var hold2 = Guid.NewGuid();
        var hold1Str = hold1.ToString();
        var hold2Str = hold2.ToString();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));

        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { walletIdStr }));

        _mockHoldStringStore
            .Setup(s => s.GetAsync($"hold-wallet:{walletIdStr}:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { hold1Str, hold2Str }));

        // First hold throws on cache read
        _mockHoldCacheStore
            .Setup(s => s.GetAsync($"hold:{hold1Str}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated store error"));

        // Second hold is valid and expired
        var expiredHold2 = new HoldModel
        {
            HoldId = hold2,
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 150,
            Status = HoldStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };

        _mockHoldCacheStore
            .Setup(s => s.GetAsync($"hold:{hold2Str}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredHold2);

        _mockHoldStore
            .Setup(s => s.GetWithETagAsync($"hold:{hold2Str}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((expiredHold2, "etag-456"));

        _mockWalletStore
            .Setup(s => s.GetAsync($"wallet:{walletIdStr}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletModel
            {
                WalletId = _walletId,
                OwnerId = _ownerId,
                OwnerType = EntityType.Account,
                Status = WalletStatus.Active
            });

        // Act
        await RunOneCycleAsync();

        // Assert - second hold was still processed
        Assert.Single(_capturedHolds);
        Assert.Equal(HoldStatus.Released, _capturedHolds[0].Model.Status);
        Assert.Single(_capturedEvents);
    }

    #endregion
}
