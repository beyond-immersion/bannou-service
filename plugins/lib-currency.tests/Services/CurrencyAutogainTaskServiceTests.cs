#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Currency.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Worldstate;

namespace BeyondImmersion.BannouService.Currency.Tests.Services;

/// <summary>
/// Unit tests for CurrencyAutogainTaskService game-time integration.
/// Tests the background worker's branching on AutogainTimeSource config
/// and per-definition AutogainUseGameTime override.
/// Uses the Capture Pattern per TESTING-PATTERNS.md.
/// </summary>
public class CurrencyAutogainTaskServiceTests
{
    #region Test Infrastructure

    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceProvider> _mockScopedProvider;
    private readonly Mock<ILogger<CurrencyAutogainTaskService>> _mockLogger;
    private readonly CurrencyServiceConfiguration _configuration;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    // Stores resolved from scope
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IWorldstateClient> _mockWorldstateClient;

    // Typed stores
    private readonly Mock<IStateStore<CurrencyDefinitionModel>> _mockDefStore;
    private readonly Mock<IStateStore<BalanceModel>> _mockBalanceStore;
    private readonly Mock<IStateStore<WalletModel>> _mockWalletStore;

    // String stores
    private readonly Mock<IStateStore<string>> _mockDefStringStore;
    private readonly Mock<IStateStore<string>> _mockBalanceStringStore;

    // Capture lists
    private readonly List<(string Key, BalanceModel Model)> _capturedBalances;
    private readonly List<(string Topic, object Event)> _capturedEvents;

    // Reusable test data
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly Guid _definitionId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _realmId = Guid.NewGuid();

    public CurrencyAutogainTaskServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScopedProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<CurrencyAutogainTaskService>>();
        _configuration = new CurrencyServiceConfiguration
        {
            AutogainProcessingMode = AutogainProcessingMode.Task,
            AutogainTaskStartupDelaySeconds = 0,
            AutogainTaskIntervalSeconds = 60,
            AutogainBatchSize = 100,
            BalanceLockTimeoutSeconds = 30,
            AutogainTimeSource = TimeSource.GameTime
        };
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockWorldstateClient = new Mock<IWorldstateClient>();

        _mockDefStore = new Mock<IStateStore<CurrencyDefinitionModel>>();
        _mockBalanceStore = new Mock<IStateStore<BalanceModel>>();
        _mockWalletStore = new Mock<IStateStore<WalletModel>>();
        _mockDefStringStore = new Mock<IStateStore<string>>();
        _mockBalanceStringStore = new Mock<IStateStore<string>>();

        _capturedBalances = new List<(string, BalanceModel)>();
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
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IWorldstateClient)))
            .Returns(_mockWorldstateClient.Object);

        // Wire up state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions))
            .Returns(_mockDefStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions))
            .Returns(_mockDefStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalances))
            .Returns(_mockBalanceStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyBalances))
            .Returns(_mockBalanceStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets))
            .Returns(_mockWalletStore.Object);

        // Setup captures
        _mockBalanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BalanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BalanceModel, StateOptions?, CancellationToken>((k, m, _, _) =>
                _capturedBalances.Add((k, m)))
            .ReturnsAsync("etag");

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
                _capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        // Default: lock succeeds
        var successLock = new Mock<ILockResponse>();
        successLock.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);
    }

    private CurrencyAutogainTaskService CreateService()
    {
        return new CurrencyAutogainTaskService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockTelemetryProvider.Object);
    }

    private async Task RunOneCycleAsync()
    {
        using var service = CreateService();
        using var cts = new CancellationTokenSource();
        var task = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        try { await task; }
        catch (OperationCanceledException) { }
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    private CurrencyDefinitionModel CreateAutogainDefinition(bool? autogainUseGameTime = null)
    {
        return new CurrencyDefinitionModel
        {
            DefinitionId = _definitionId,
            Code = "GOLD",
            Name = "Gold",
            Scope = CurrencyScope.Global,
            Precision = CurrencyPrecision.Integer,
            AutogainEnabled = true,
            AutogainMode = AutogainMode.Simple,
            AutogainAmount = 10.0,
            AutogainInterval = "PT1H", // 1 hour interval
            AutogainUseGameTime = autogainUseGameTime,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-90)
        };
    }

    private void SetupDefinitionAndBalanceIndex(CurrencyDefinitionModel definition)
    {
        var defId = definition.DefinitionId.ToString();
        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { defId }));
        _mockDefStore
            .Setup(s => s.GetAsync($"def:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        _mockBalanceStringStore
            .Setup(s => s.GetAsync($"bal-currency:{defId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { _walletId.ToString() }));
    }

    private BalanceModel CreateBalance(DateTimeOffset lastAutogainAt)
    {
        return new BalanceModel
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100.0,
            LastAutogainAt = lastAutogainAt,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            LastModifiedAt = DateTimeOffset.UtcNow
        };
    }

    private WalletModel CreateWallet(Guid? realmId = null)
    {
        return new WalletModel
        {
            WalletId = _walletId,
            OwnerId = _ownerId,
            OwnerType = EntityType.Character,
            RealmId = realmId,
            Status = WalletStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
    }

    #endregion

    #region GameTime Autogain Tests

    [Fact]
    public async Task ProcessCycle_GameTimeSource_CallsWorldstateForElapsedTime()
    {
        // Arrange
        _configuration.AutogainTimeSource = TimeSource.GameTime;
        var definition = CreateAutogainDefinition();
        SetupDefinitionAndBalanceIndex(definition);

        var balance = CreateBalance(DateTimeOffset.UtcNow.AddHours(-2));
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("bal:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        var wallet = CreateWallet(realmId: _realmId);
        _mockWalletStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("wallet:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        // Worldstate returns 48 game hours elapsed (24:1 ratio over 2 real hours)
        _mockWorldstateClient
            .Setup(c => c.GetElapsedGameTimeAsync(
                It.Is<GetElapsedGameTimeRequest>(r => r.RealmId == _realmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetElapsedGameTimeResponse
            {
                TotalGameSeconds = 48 * 3600, // 48 game hours
                GameDays = 2,
                GameHours = 0,
                GameMinutes = 0
            });

        // Act
        await RunOneCycleAsync();

        // Assert: Worldstate was called
        _mockWorldstateClient.Verify(c => c.GetElapsedGameTimeAsync(
            It.Is<GetElapsedGameTimeRequest>(r => r.RealmId == _realmId),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert: autogain was applied using game-time periods (48 hours / 1 hour interval = 48 periods)
        Assert.Single(_capturedBalances);
        var savedBalance = _capturedBalances[0].Model;
        // 48 periods * 10.0 amount = 480.0 gain, starting from 100.0 = 580.0
        Assert.Equal(580.0, savedBalance.Amount, 0.1);
    }

    [Fact]
    public async Task ProcessCycle_RealTimeSource_DoesNotCallWorldstate()
    {
        // Arrange
        _configuration.AutogainTimeSource = TimeSource.RealTime;
        var definition = CreateAutogainDefinition();
        SetupDefinitionAndBalanceIndex(definition);

        var balance = CreateBalance(DateTimeOffset.UtcNow.AddHours(-2));
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("bal:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        var wallet = CreateWallet(realmId: _realmId);
        _mockWalletStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("wallet:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        // Act
        await RunOneCycleAsync();

        // Assert: Worldstate was NOT called
        _mockWorldstateClient.Verify(c => c.GetElapsedGameTimeAsync(
            It.IsAny<GetElapsedGameTimeRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Assert: autogain was applied using real-time (2 hours / 1 hour = 2 periods)
        Assert.Single(_capturedBalances);
        var savedBalance = _capturedBalances[0].Model;
        // 2 periods * 10.0 amount = 20.0 gain, starting from 100.0 = 120.0
        Assert.Equal(120.0, savedBalance.Amount, 0.1);
    }

    #endregion

    #region Per-Definition Override Tests

    [Fact]
    public async Task ProcessCycle_PerDefinitionOverrideFalse_UsesRealTimeEvenWhenGlobalIsGameTime()
    {
        // Arrange
        _configuration.AutogainTimeSource = TimeSource.GameTime;
        var definition = CreateAutogainDefinition(autogainUseGameTime: false);
        SetupDefinitionAndBalanceIndex(definition);

        var balance = CreateBalance(DateTimeOffset.UtcNow.AddHours(-2));
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("bal:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        var wallet = CreateWallet(realmId: _realmId);
        _mockWalletStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("wallet:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        // Act
        await RunOneCycleAsync();

        // Assert: Worldstate was NOT called (per-definition override to real-time)
        _mockWorldstateClient.Verify(c => c.GetElapsedGameTimeAsync(
            It.IsAny<GetElapsedGameTimeRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Assert: real-time autogain applied (2 periods)
        Assert.Single(_capturedBalances);
        Assert.Equal(120.0, _capturedBalances[0].Model.Amount, 0.1);
    }

    #endregion

    #region No RealmId Fallback Tests

    [Fact]
    public async Task ProcessCycle_WalletWithoutRealmId_FallsBackToRealTime()
    {
        // Arrange
        _configuration.AutogainTimeSource = TimeSource.GameTime;
        var definition = CreateAutogainDefinition();
        SetupDefinitionAndBalanceIndex(definition);

        var balance = CreateBalance(DateTimeOffset.UtcNow.AddHours(-2));
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("bal:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        // Wallet WITHOUT realmId (global/account wallet)
        var wallet = CreateWallet(realmId: null);
        _mockWalletStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("wallet:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        // Act
        await RunOneCycleAsync();

        // Assert: Worldstate NOT called (no realmId to query)
        _mockWorldstateClient.Verify(c => c.GetElapsedGameTimeAsync(
            It.IsAny<GetElapsedGameTimeRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Assert: real-time fallback (2 periods)
        Assert.Single(_capturedBalances);
        Assert.Equal(120.0, _capturedBalances[0].Model.Amount, 0.1);
    }

    #endregion

    #region Worldstate Unavailable Tests

    [Fact]
    public async Task ProcessCycle_WorldstateUnavailable_SkipsWallet()
    {
        // Arrange
        _configuration.AutogainTimeSource = TimeSource.GameTime;
        var definition = CreateAutogainDefinition();
        SetupDefinitionAndBalanceIndex(definition);

        var balance = CreateBalance(DateTimeOffset.UtcNow.AddHours(-2));
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("bal:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        var wallet = CreateWallet(realmId: _realmId);
        _mockWalletStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("wallet:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        // Worldstate throws ApiException (service unavailable)
        _mockWorldstateClient
            .Setup(c => c.GetElapsedGameTimeAsync(It.IsAny<GetElapsedGameTimeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Worldstate unavailable", 503, null, null, null));

        // Act
        await RunOneCycleAsync();

        // Assert: balance was NOT modified (wallet skipped)
        Assert.Empty(_capturedBalances);

        // Assert: no events published
        Assert.Empty(_capturedEvents);
    }

    #endregion
}
