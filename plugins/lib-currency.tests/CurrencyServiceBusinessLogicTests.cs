using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Currency.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Currency.Tests;

/// <summary>
/// Business logic unit tests for CurrencyService.
/// Tests currency definition CRUD, wallet lifecycle, balance operations,
/// hold operations, escrow operations, conversion, and transaction tracking.
/// Uses the Capture Pattern per TESTING-PATTERNS.md.
/// </summary>
public class CurrencyServiceBusinessLogicTests
{
    #region Test Infrastructure

    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<CurrencyService>> _mockLogger;
    private readonly CurrencyServiceConfiguration _configuration;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IEntitySessionRegistry> _mockEntitySessionRegistry;
    private readonly Mock<ICurrencyDataCache> _mockCurrencyCache;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    // Typed stores
    private readonly Mock<IStateStore<WalletModel>> _mockWalletStore;
    private readonly Mock<IStateStore<CurrencyDefinitionModel>> _mockDefinitionStore;
    private readonly Mock<IStateStore<BalanceModel>> _mockBalanceStore;
    private readonly Mock<IStateStore<BalanceModel>> _mockBalanceCacheStore;
    private readonly Mock<IStateStore<TransactionModel>> _mockTransactionStore;
    private readonly Mock<IStateStore<HoldModel>> _mockHoldStore;
    private readonly Mock<IStateStore<HoldModel>> _mockHoldCacheStore;

    // String stores
    private readonly Mock<IStateStore<string>> _mockDefStringStore;
    private readonly Mock<IStateStore<string>> _mockWalletStringStore;
    private readonly Mock<IStateStore<string>> _mockIdempotencyStringStore;
    private readonly Mock<IStateStore<string>> _mockBalanceStringStore;
    private readonly Mock<IStateStore<string>> _mockTransactionStringStore;
    private readonly Mock<IStateStore<string>> _mockHoldsStringStore;

    // Capture lists for events
    private readonly List<(string Topic, object Event)> _capturedEvents;
    private readonly List<(string Key, CurrencyDefinitionModel Model)> _capturedDefinitions;
    private readonly List<(string Key, WalletModel Model)> _capturedWallets;
    private readonly List<(string Key, BalanceModel Model)> _capturedBalances;
    private readonly List<(string Key, TransactionModel Model)> _capturedTransactions;
    private readonly List<(string Key, HoldModel Model)> _capturedHolds;

    // Reusable test data
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly Guid _definitionId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();

    public CurrencyServiceBusinessLogicTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<CurrencyService>>();
        _configuration = new CurrencyServiceConfiguration();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockEntitySessionRegistry = new Mock<IEntitySessionRegistry>();
        _mockCurrencyCache = new Mock<ICurrencyDataCache>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Initialize stores
        _mockWalletStore = new Mock<IStateStore<WalletModel>>();
        _mockDefinitionStore = new Mock<IStateStore<CurrencyDefinitionModel>>();
        _mockBalanceStore = new Mock<IStateStore<BalanceModel>>();
        _mockBalanceCacheStore = new Mock<IStateStore<BalanceModel>>();
        _mockTransactionStore = new Mock<IStateStore<TransactionModel>>();
        _mockHoldStore = new Mock<IStateStore<HoldModel>>();
        _mockHoldCacheStore = new Mock<IStateStore<HoldModel>>();
        _mockDefStringStore = new Mock<IStateStore<string>>();
        _mockWalletStringStore = new Mock<IStateStore<string>>();
        _mockIdempotencyStringStore = new Mock<IStateStore<string>>();
        _mockBalanceStringStore = new Mock<IStateStore<string>>();
        _mockTransactionStringStore = new Mock<IStateStore<string>>();
        _mockHoldsStringStore = new Mock<IStateStore<string>>();

        // Initialize capture lists
        _capturedEvents = new List<(string, object)>();
        _capturedDefinitions = new List<(string, CurrencyDefinitionModel)>();
        _capturedWallets = new List<(string, WalletModel)>();
        _capturedBalances = new List<(string, BalanceModel)>();
        _capturedTransactions = new List<(string, TransactionModel)>();
        _capturedHolds = new List<(string, HoldModel)>();

        // Wire up state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<WalletModel>(StateStoreDefinitions.CurrencyWallets))
            .Returns(_mockWalletStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CurrencyDefinitionModel>(StateStoreDefinitions.CurrencyDefinitions))
            .Returns(_mockDefinitionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalances))
            .Returns(_mockBalanceStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BalanceModel>(StateStoreDefinitions.CurrencyBalanceCache))
            .Returns(_mockBalanceCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<TransactionModel>(StateStoreDefinitions.CurrencyTransactions))
            .Returns(_mockTransactionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHolds))
            .Returns(_mockHoldStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<HoldModel>(StateStoreDefinitions.CurrencyHoldsCache))
            .Returns(_mockHoldCacheStore.Object);

        // String stores need to differentiate by store name
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyDefinitions))
            .Returns(_mockDefStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyWallets))
            .Returns(_mockWalletStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyIdempotency))
            .Returns(_mockIdempotencyStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyBalances))
            .Returns(_mockBalanceStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyTransactions))
            .Returns(_mockTransactionStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyHolds))
            .Returns(_mockHoldsStringStore.Object);

        // Setup per-event-type captures for TryPublishAsync.
        // The 3-param DIM overload delegates to the 5-param version, but Moq
        // matches generic type parameters exactly, so It.IsAny<object>() won't
        // match calls with concrete event types. Each event type needs its own setup.
        SetupEventCapture<CurrencyDefinitionCreatedEvent>();
        SetupEventCapture<CurrencyDefinitionUpdatedEvent>();
        SetupEventCapture<CurrencyWalletCreatedEvent>();
        SetupEventCapture<CurrencyWalletFrozenEvent>();
        SetupEventCapture<CurrencyWalletUnfrozenEvent>();
        SetupEventCapture<CurrencyWalletClosedEvent>();
        SetupEventCapture<CurrencyEarnCapReachedEvent>();
        SetupEventCapture<CurrencyWalletCapReachedEvent>();
        SetupEventCapture<CurrencyCreditedEvent>();
        SetupEventCapture<CurrencyDebitedEvent>();
        SetupEventCapture<CurrencyTransferredEvent>();
        SetupEventCapture<CurrencyExchangeRateUpdatedEvent>();
        SetupEventCapture<CurrencyHoldCreatedEvent>();
        SetupEventCapture<CurrencyHoldCapturedEvent>();
        SetupEventCapture<CurrencyHoldReleasedEvent>();
        SetupEventCapture<CurrencyAutogainCalculatedEvent>();

        // Setup captures for stores
        _mockDefinitionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CurrencyDefinitionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CurrencyDefinitionModel, StateOptions?, CancellationToken>((k, m, _, _) =>
                _capturedDefinitions.Add((k, m)))
            .ReturnsAsync("etag");

        _mockWalletStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<WalletModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, WalletModel, StateOptions?, CancellationToken>((k, m, _, _) =>
                _capturedWallets.Add((k, m)))
            .ReturnsAsync("etag");

        _mockBalanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BalanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BalanceModel, StateOptions?, CancellationToken>((k, m, _, _) =>
                _capturedBalances.Add((k, m)))
            .ReturnsAsync("etag");

        _mockTransactionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<TransactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransactionModel, StateOptions?, CancellationToken>((k, m, _, _) =>
                _capturedTransactions.Add((k, m)))
            .ReturnsAsync("etag");

        _mockHoldStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<HoldModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, HoldModel, StateOptions?, CancellationToken>((k, m, _, _) =>
                _capturedHolds.Add((k, m)))
            .ReturnsAsync("etag");

        // Default: idempotency checks pass (not duplicate)
        _mockIdempotencyStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Default: holds index returns empty
        _mockHoldsStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Default: balance index returns empty
        _mockBalanceStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Default: transaction index returns empty
        _mockTransactionStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Default: definition index returns empty
        _mockDefStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Default: wallet index returns empty
        _mockWalletStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Default: all string stores SaveAsync succeeds
        _mockDefStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockWalletStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockIdempotencyStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockBalanceStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockTransactionStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockHoldsStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Default: balance cache store SaveAsync succeeds
        _mockBalanceCacheStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BalanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Default: hold cache store SaveAsync succeeds
        _mockHoldCacheStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<HoldModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Default: lock provider succeeds
        SetupLockSuccess();

        // Default: balance cache returns null (cache miss)
        _mockBalanceCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BalanceModel?)null);

        // Default: hold cache returns null (cache miss)
        _mockHoldCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HoldModel?)null);
    }

    private CurrencyService CreateService()
    {
        return new CurrencyService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object,
            _mockEntitySessionRegistry.Object,
            _mockCurrencyCache.Object,
            _mockEventConsumer.Object);
    }

    /// <summary>
    /// Sets up a per-event-type mock for TryPublishAsync that captures the event.
    /// Uses the 3-param DIM overload which is what the service calls.
    /// </summary>
    private void SetupEventCapture<TEvent>() where TEvent : class
    {
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<TEvent>(
                It.IsAny<string>(), It.IsAny<TEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, TEvent, CancellationToken>((topic, evt, _) =>
                _capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);
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

    private WalletModel CreateActiveWallet(Guid? walletId = null, Guid? ownerId = null)
    {
        return new WalletModel
        {
            WalletId = walletId ?? _walletId,
            OwnerId = ownerId ?? _ownerId,
            OwnerType = EntityType.Account,
            Status = WalletStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
    }

    private CurrencyDefinitionModel CreateDefinition(
        Guid? defId = null,
        string code = "GOLD",
        bool isActive = true,
        bool transferable = true,
        double? perWalletCap = null,
        CapOverflowBehavior? capOverflowBehavior = null,
        double? dailyEarnCap = null,
        double? weeklyEarnCap = null,
        bool isBaseCurrency = false,
        double? exchangeRateToBase = 1.0,
        bool? allowNegative = null)
    {
        return new CurrencyDefinitionModel
        {
            DefinitionId = defId ?? _definitionId,
            Code = code,
            Name = $"{code} Currency",
            Scope = CurrencyScope.Global,
            Precision = CurrencyPrecision.Decimal2,
            IsActive = isActive,
            Transferable = transferable,
            PerWalletCap = perWalletCap,
            CapOverflowBehavior = capOverflowBehavior,
            DailyEarnCap = dailyEarnCap,
            WeeklyEarnCap = weeklyEarnCap,
            IsBaseCurrency = isBaseCurrency,
            ExchangeRateToBase = exchangeRateToBase,
            AllowNegative = allowNegative,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-90)
        };
    }

    private BalanceModel CreateBalance(Guid walletId, Guid currencyDefId, double amount)
    {
        return new BalanceModel
        {
            WalletId = walletId,
            CurrencyDefinitionId = currencyDefId,
            Amount = amount,
            DailyEarned = 0,
            WeeklyEarned = 0,
            DailyResetAt = DateTimeOffset.UtcNow.Date.AddDays(1),
            WeeklyResetAt = DateTimeOffset.UtcNow.Date.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            LastModifiedAt = DateTimeOffset.UtcNow
        };
    }

    private void SetupWalletExists(WalletModel wallet)
    {
        _mockWalletStore
            .Setup(s => s.GetAsync($"wallet:{wallet.WalletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
    }

    private void SetupDefinitionExists(CurrencyDefinitionModel def)
    {
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{def.DefinitionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(def);
    }

    private void SetupBalanceExists(BalanceModel balance)
    {
        _mockBalanceStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains(balance.WalletId.ToString()) && k.Contains(balance.CurrencyDefinitionId.ToString())),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);
    }

    #endregion

    #region Currency Definition - Create

    [Fact]
    public async Task CreateCurrencyDefinition_Success_SavesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        _mockDefStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new CreateCurrencyDefinitionRequest
        {
            Code = "gold",
            Name = "Gold Currency",
            Description = "Test currency",
            Scope = CurrencyScope.Global,
            Precision = CurrencyPrecision.Decimal2
        };

        // Act
        var (status, response) = await service.CreateCurrencyDefinitionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("gold", response.Code);
        Assert.Equal("Gold Currency", response.Name);
        Assert.Equal(CurrencyScope.Global, response.Scope);
        Assert.True(response.IsActive);

        // Verify saved model via capture
        Assert.Single(_capturedDefinitions);
        var (savedKey, savedModel) = _capturedDefinitions[0];
        Assert.StartsWith("def:", savedKey);
        Assert.Equal("gold", savedModel.Code);
        Assert.Equal(CurrencyScope.Global, savedModel.Scope);

        // Verify published event via capture
        var createdEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.definition.created");
        Assert.NotNull(createdEvent.Event);
        var typedEvent = Assert.IsType<CurrencyDefinitionCreatedEvent>(createdEvent.Event);
        Assert.Equal("gold", typedEvent.Code);
        Assert.Equal(CurrencyScope.Global, typedEvent.Scope);
    }

    [Fact]
    public async Task CreateCurrencyDefinition_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        _mockDefStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("def-code:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        var request = new CreateCurrencyDefinitionRequest
        {
            Code = "gold",
            Name = "Gold Currency",
            Scope = CurrencyScope.Global,
            Precision = CurrencyPrecision.Decimal2
        };

        // Act
        var (status, response) = await service.CreateCurrencyDefinitionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
        Assert.Empty(_capturedDefinitions);
    }

    [Fact]
    public async Task CreateCurrencyDefinition_DuplicateBaseCurrency_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        _mockDefStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("def-code:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockDefStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("base-currency:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing-base-id:EXISTING");

        var request = new CreateCurrencyDefinitionRequest
        {
            Code = "gold",
            Name = "Gold Currency",
            Scope = CurrencyScope.Global,
            Precision = CurrencyPrecision.Decimal2,
            IsBaseCurrency = true
        };

        // Act
        var (status, response) = await service.CreateCurrencyDefinitionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region Currency Definition - Get

    [Fact]
    public async Task GetCurrencyDefinition_ById_ReturnsDefinition()
    {
        // Arrange
        var service = CreateService();
        var def = CreateDefinition();
        SetupDefinitionExists(def);

        var request = new GetCurrencyDefinitionRequest { DefinitionId = _definitionId };

        // Act
        var (status, response) = await service.GetCurrencyDefinitionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_definitionId, response.DefinitionId);
        Assert.Equal("GOLD", response.Code);
    }

    [Fact]
    public async Task GetCurrencyDefinition_ByCode_ReturnsDefinition()
    {
        // Arrange
        var service = CreateService();
        var def = CreateDefinition();
        _mockDefStringStore
            .Setup(s => s.GetAsync("def-code:GOLD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_definitionId.ToString());
        SetupDefinitionExists(def);

        var request = new GetCurrencyDefinitionRequest { Code = "GOLD" };

        // Act
        var (status, response) = await service.GetCurrencyDefinitionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("GOLD", response.Code);
    }

    [Fact]
    public async Task GetCurrencyDefinition_NotFound_Returns404()
    {
        // Arrange
        var service = CreateService();
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyDefinitionModel?)null);

        var request = new GetCurrencyDefinitionRequest { DefinitionId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.GetCurrencyDefinitionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Currency Definition - List

    [Fact]
    public async Task ListCurrencyDefinitions_FiltersInactive()
    {
        // Arrange
        var service = CreateService();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();

        _mockDefStringStore
            .Setup(s => s.GetAsync("all-defs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { activeId.ToString(), inactiveId.ToString() }));

        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{activeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinition(defId: activeId, isActive: true));
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{inactiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinition(defId: inactiveId, isActive: false));

        var request = new ListCurrencyDefinitionsRequest { IncludeInactive = false };

        // Act
        var (status, response) = await service.ListCurrencyDefinitionsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Definitions);
        Assert.Equal(activeId, response.Definitions.First().DefinitionId);
    }

    #endregion

    #region Currency Definition - Update

    [Fact]
    public async Task UpdateCurrencyDefinition_Success_UpdatesFieldsAndPublishes()
    {
        // Arrange
        var service = CreateService();
        var def = CreateDefinition();
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{_definitionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(def);

        var request = new UpdateCurrencyDefinitionRequest
        {
            DefinitionId = _definitionId,
            Name = "Updated Gold",
            PerWalletCap = 10000
        };

        // Act
        var (status, response) = await service.UpdateCurrencyDefinitionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Gold", response.Name);
        Assert.Equal(10000, response.PerWalletCap);

        var updateEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.definition.updated");
        Assert.NotNull(updateEvent.Event);
        var typedEvent = Assert.IsType<CurrencyDefinitionUpdatedEvent>(updateEvent.Event);
        Assert.Equal("Updated Gold", typedEvent.Name);
    }

    [Fact]
    public async Task UpdateCurrencyDefinition_NotFound_Returns404()
    {
        // Arrange
        var service = CreateService();
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyDefinitionModel?)null);

        var request = new UpdateCurrencyDefinitionRequest
        {
            DefinitionId = Guid.NewGuid(),
            Name = "Updated"
        };

        // Act
        var (status, response) = await service.UpdateCurrencyDefinitionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Wallet - Create

    [Fact]
    public async Task CreateWallet_Success_SavesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        _mockWalletStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new CreateWalletRequest
        {
            OwnerId = _ownerId,
            OwnerType = EntityType.Account
        };

        // Act
        var (status, response) = await service.CreateWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_ownerId, response.OwnerId);
        Assert.Equal(EntityType.Account, response.OwnerType);
        Assert.Equal(WalletStatus.Active, response.Status);

        // Verify captured wallet
        Assert.Single(_capturedWallets);
        Assert.Equal(WalletStatus.Active, _capturedWallets[0].Model.Status);

        // Verify event
        var createdEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.wallet.created");
        Assert.NotNull(createdEvent.Event);
        var typedEvent = Assert.IsType<CurrencyWalletCreatedEvent>(createdEvent.Event);
        Assert.Equal(_ownerId, typedEvent.OwnerId);
    }

    [Fact]
    public async Task CreateWallet_DuplicateOwner_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        _mockWalletStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("wallet-owner:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        var request = new CreateWalletRequest
        {
            OwnerId = _ownerId,
            OwnerType = EntityType.Account
        };

        // Act
        var (status, response) = await service.CreateWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
        Assert.Empty(_capturedWallets);
    }

    #endregion

    #region Wallet - Get

    [Fact]
    public async Task GetWallet_ById_ReturnsWalletWithBalances()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        SetupWalletExists(wallet);

        var request = new GetWalletRequest { WalletId = _walletId };

        // Act
        var (status, response) = await service.GetWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_walletId, response.Wallet.WalletId);
        Assert.NotNull(response.Balances);
    }

    [Fact]
    public async Task GetWallet_NotFound_Returns404()
    {
        // Arrange
        var service = CreateService();
        _mockWalletStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletModel?)null);

        var request = new GetWalletRequest { WalletId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.GetWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Wallet - GetOrCreate

    [Fact]
    public async Task GetOrCreateWallet_ExistingWallet_ReturnsFalseCreated()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        _mockWalletStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("wallet-owner:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_walletId.ToString());
        _mockWalletStore
            .Setup(s => s.GetAsync($"wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var request = new GetOrCreateWalletRequest
        {
            OwnerId = _ownerId,
            OwnerType = EntityType.Account
        };

        // Act
        var (status, response) = await service.GetOrCreateWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Created);
        Assert.Equal(_walletId, response.Wallet.WalletId);
    }

    [Fact]
    public async Task GetOrCreateWallet_NewWallet_ReturnsTrueCreated()
    {
        // Arrange
        var service = CreateService();
        _mockWalletStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("wallet-owner:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new GetOrCreateWalletRequest
        {
            OwnerId = _ownerId,
            OwnerType = EntityType.Account
        };

        // Act
        var (status, response) = await service.GetOrCreateWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Created);
    }

    #endregion

    #region Wallet - Freeze/Unfreeze

    [Fact]
    public async Task FreezeWallet_ActiveWallet_SetsFrozenStatus()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        _mockWalletStore
            .Setup(s => s.GetWithETagAsync($"wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((wallet, "etag-1"));
        _mockWalletStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<WalletModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var request = new FreezeWalletRequest { WalletId = _walletId, Reason = "Suspected fraud" };

        // Act
        var (status, response) = await service.FreezeWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(WalletStatus.Frozen, response.Status);

        var frozenEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.wallet.frozen");
        Assert.NotNull(frozenEvent.Event);
        var typedEvent = Assert.IsType<CurrencyWalletFrozenEvent>(frozenEvent.Event);
        Assert.Equal("Suspected fraud", typedEvent.Reason);
    }

    [Fact]
    public async Task FreezeWallet_AlreadyFrozen_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        wallet.Status = WalletStatus.Frozen;
        _mockWalletStore
            .Setup(s => s.GetWithETagAsync($"wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((wallet, "etag-1"));

        var request = new FreezeWalletRequest { WalletId = _walletId, Reason = "test" };

        // Act
        var (status, _) = await service.FreezeWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task UnfreezeWallet_FrozenWallet_SetsActiveStatus()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        wallet.Status = WalletStatus.Frozen;
        wallet.FrozenReason = "test freeze";
        _mockWalletStore
            .Setup(s => s.GetWithETagAsync($"wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((wallet, "etag-1"));
        _mockWalletStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<WalletModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var request = new UnfreezeWalletRequest { WalletId = _walletId };

        // Act
        var (status, response) = await service.UnfreezeWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(WalletStatus.Active, response.Status);

        var unfreezeEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.wallet.unfrozen");
        Assert.NotNull(unfreezeEvent.Event);
    }

    [Fact]
    public async Task UnfreezeWallet_NotFrozen_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        _mockWalletStore
            .Setup(s => s.GetWithETagAsync($"wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((wallet, "etag-1"));

        var request = new UnfreezeWalletRequest { WalletId = _walletId };

        // Act
        var (status, _) = await service.UnfreezeWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Wallet - Close

    [Fact]
    public async Task CloseWallet_Success_TransfersBalancesAndCloses()
    {
        // Arrange
        var service = CreateService();
        var destWalletId = Guid.NewGuid();
        var wallet = CreateActiveWallet();
        var destWallet = CreateActiveWallet(walletId: destWalletId);

        _mockWalletStore
            .Setup(s => s.GetWithETagAsync($"wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((wallet, "etag-1"));
        _mockWalletStore
            .Setup(s => s.GetAsync($"wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _mockWalletStore
            .Setup(s => s.GetAsync($"wallet:{destWalletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(destWallet);
        _mockWalletStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<WalletModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var request = new CloseWalletRequest
        {
            WalletId = _walletId,
            TransferRemainingTo = destWalletId
        };

        // Act
        var (status, response) = await service.CloseWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(WalletStatus.Closed, response.Wallet.Status);

        var closedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.wallet.closed");
        Assert.NotNull(closedEvent.Event);
        var typedEvent = Assert.IsType<CurrencyWalletClosedEvent>(closedEvent.Event);
        Assert.Equal(destWalletId, typedEvent.BalancesTransferredTo);
    }

    [Fact]
    public async Task CloseWallet_AlreadyClosed_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        wallet.Status = WalletStatus.Closed;
        _mockWalletStore
            .Setup(s => s.GetWithETagAsync($"wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((wallet, "etag-1"));

        var request = new CloseWalletRequest
        {
            WalletId = _walletId,
            TransferRemainingTo = Guid.NewGuid()
        };

        // Act
        var (status, _) = await service.CloseWalletAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Balance - Credit

    [Fact]
    public async Task CreditCurrency_Success_UpdatesBalanceAndRecordsTransaction()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition();
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var request = new CreditCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.Mint,
            ReferenceType = "test",
            IdempotencyKey = "credit-1"
        };

        // Act
        var (status, response) = await service.CreditCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(100, response.NewBalance);
        Assert.NotNull(response.Transaction);
        Assert.False(response.EarnCapApplied);
        Assert.False(response.WalletCapApplied);

        // Verify balance saved via capture
        var savedBalance = _capturedBalances.LastOrDefault(b => b.Key.Contains(_definitionId.ToString()));
        Assert.NotNull(savedBalance.Model);
        Assert.Equal(100, savedBalance.Model.Amount);

        // Verify credit event
        var creditEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.credited");
        Assert.NotNull(creditEvent.Event);
        var typedEvent = Assert.IsType<CurrencyCreditedEvent>(creditEvent.Event);
        Assert.Equal(100, typedEvent.Amount);
        Assert.Equal(_walletId, typedEvent.WalletId);
    }

    [Fact]
    public async Task CreditCurrency_ZeroAmount_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new CreditCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 0,
            TransactionType = TransactionType.Mint,
            IdempotencyKey = "credit-zero"
        };

        // Act
        var (status, response) = await service.CreditCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreditCurrency_IdempotencyDuplicate_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        _mockIdempotencyStringStore
            .Setup(s => s.GetAsync("credit-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        var request = new CreditCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.Mint,
            IdempotencyKey = "credit-dup"
        };

        // Act
        var (status, response) = await service.CreditCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreditCurrency_FrozenWallet_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        wallet.Status = WalletStatus.Frozen;
        SetupWalletExists(wallet);

        var request = new CreditCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.Mint,
            IdempotencyKey = "credit-frozen"
        };

        // Act
        var (status, _) = await service.CreditCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task CreditCurrency_EarnCapApplied_LimitsAmount()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition(dailyEarnCap: 50);
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var request = new CreditCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.Mint,
            IdempotencyKey = "credit-earncap"
        };

        // Act
        var (status, response) = await service.CreditCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.EarnCapApplied);
        Assert.Equal(50, response.EarnCapAmountLimited);
        Assert.Equal(50, response.NewBalance);

        // Verify earn cap event
        var earnCapEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.earn-cap.reached");
        Assert.NotNull(earnCapEvent.Event);
    }

    [Fact]
    public async Task CreditCurrency_WalletCapReject_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition(perWalletCap: 100, capOverflowBehavior: CapOverflowBehavior.Reject);
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        // Existing balance of 80
        var balance = CreateBalance(_walletId, _definitionId, 80);
        SetupBalanceExists(balance);

        var request = new CreditCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 50, // Would make 130, cap is 100
            TransactionType = TransactionType.Mint,
            IdempotencyKey = "credit-cap-reject"
        };

        // Act
        var (status, response) = await service.CreditCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreditCurrency_WalletCapClamp_ClipsOverflow()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition(perWalletCap: 100, capOverflowBehavior: CapOverflowBehavior.CapAndLose);
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        // Existing balance of 80
        var balance = CreateBalance(_walletId, _definitionId, 80);
        SetupBalanceExists(balance);

        var request = new CreditCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 50, // Would make 130, cap is 100 => clamp to 20 credit
            TransactionType = TransactionType.Mint,
            IdempotencyKey = "credit-cap-clamp"
        };

        // Act
        var (status, response) = await service.CreditCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.WalletCapApplied);
        Assert.Equal(30, response.WalletCapAmountLost);
        Assert.Equal(100, response.NewBalance);

        var walletCapEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.wallet-cap.reached");
        Assert.NotNull(walletCapEvent.Event);
    }

    [Fact]
    public async Task CreditCurrency_LockFailed_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition();
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                StateStoreDefinitions.CurrencyLock,
                It.Is<string>(k => k.Contains(_definitionId.ToString())),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        var request = new CreditCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.Mint,
            IdempotencyKey = "credit-lock-fail"
        };

        // Act
        var (status, _) = await service.CreditCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region Balance - Debit

    [Fact]
    public async Task DebitCurrency_Success_UpdatesBalanceAndRecordsTransaction()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition();
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var balance = CreateBalance(_walletId, _definitionId, 500);
        SetupBalanceExists(balance);

        var request = new DebitCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            TransactionType = TransactionType.VendorPurchase,
            IdempotencyKey = "debit-1"
        };

        // Act
        var (status, response) = await service.DebitCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(300, response.NewBalance);
        Assert.NotNull(response.Transaction);

        var debitEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.debited");
        Assert.NotNull(debitEvent.Event);
        var typedEvent = Assert.IsType<CurrencyDebitedEvent>(debitEvent.Event);
        Assert.Equal(200, typedEvent.Amount);
    }

    [Fact]
    public async Task DebitCurrency_InsufficientFunds_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition();
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var balance = CreateBalance(_walletId, _definitionId, 50);
        SetupBalanceExists(balance);

        var request = new DebitCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.VendorPurchase,
            IdempotencyKey = "debit-insufficient"
        };

        // Act
        var (status, response) = await service.DebitCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DebitCurrency_AllowNegative_SucceedsEvenIfInsufficient()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition(allowNegative: true);
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var balance = CreateBalance(_walletId, _definitionId, 50);
        SetupBalanceExists(balance);

        var request = new DebitCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.VendorPurchase,
            IdempotencyKey = "debit-negative"
        };

        // Act
        var (status, response) = await service.DebitCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(-50, response.NewBalance);
    }

    [Fact]
    public async Task DebitCurrency_ZeroAmount_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new DebitCurrencyRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 0,
            TransactionType = TransactionType.VendorPurchase,
            IdempotencyKey = "debit-zero"
        };

        // Act
        var (status, _) = await service.DebitCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Balance - Transfer

    [Fact]
    public async Task TransferCurrency_Success_DebitsSourceCreditsTarget()
    {
        // Arrange
        var service = CreateService();
        var sourceWalletId = Guid.NewGuid();
        var targetWalletId = Guid.NewGuid();
        var sourceWallet = CreateActiveWallet(walletId: sourceWalletId);
        var targetWallet = CreateActiveWallet(walletId: targetWalletId, ownerId: Guid.NewGuid());
        var def = CreateDefinition(transferable: true);

        _mockWalletStore.Setup(s => s.GetAsync($"wallet:{sourceWalletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceWallet);
        _mockWalletStore.Setup(s => s.GetAsync($"wallet:{targetWalletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetWallet);
        SetupDefinitionExists(def);

        var sourceBalance = CreateBalance(sourceWalletId, _definitionId, 500);
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(sourceWalletId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBalance);

        var request = new TransferCurrencyRequest
        {
            SourceWalletId = sourceWalletId,
            TargetWalletId = targetWalletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            TransactionType = TransactionType.Transfer,
            IdempotencyKey = "transfer-1"
        };

        // Act
        var (status, response) = await service.TransferCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(300, response.SourceNewBalance);
        Assert.Equal(200, response.TargetNewBalance);
        Assert.False(response.TargetCapApplied);

        var transferEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.transferred");
        Assert.NotNull(transferEvent.Event);
        var typedEvent = Assert.IsType<CurrencyTransferredEvent>(transferEvent.Event);
        Assert.Equal(200, typedEvent.Amount);
    }

    [Fact]
    public async Task TransferCurrency_NotTransferable_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sourceWallet = CreateActiveWallet(walletId: Guid.NewGuid());
        var targetWallet = CreateActiveWallet(walletId: Guid.NewGuid());
        var def = CreateDefinition(transferable: false);

        _mockWalletStore.Setup(s => s.GetAsync($"wallet:{sourceWallet.WalletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceWallet);
        _mockWalletStore.Setup(s => s.GetAsync($"wallet:{targetWallet.WalletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetWallet);
        SetupDefinitionExists(def);

        var request = new TransferCurrencyRequest
        {
            SourceWalletId = sourceWallet.WalletId,
            TargetWalletId = targetWallet.WalletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.Transfer,
            IdempotencyKey = "transfer-notransfer"
        };

        // Act
        var (status, _) = await service.TransferCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task TransferCurrency_ZeroAmount_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new TransferCurrencyRequest
        {
            SourceWalletId = Guid.NewGuid(),
            TargetWalletId = Guid.NewGuid(),
            CurrencyDefinitionId = _definitionId,
            Amount = 0,
            TransactionType = TransactionType.Transfer,
            IdempotencyKey = "transfer-zero"
        };

        // Act
        var (status, _) = await service.TransferCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Balance - GetBalance

    [Fact]
    public async Task GetBalance_ExistingBalance_ReturnsCorrectAmounts()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition();
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var balance = CreateBalance(_walletId, _definitionId, 500);
        SetupBalanceExists(balance);

        var request = new GetBalanceRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId
        };

        // Act
        var (status, response) = await service.GetBalanceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(500, response.Amount);
        Assert.Equal(0, response.LockedAmount);
        Assert.Equal(500, response.EffectiveAmount);
    }

    [Fact]
    public async Task GetBalance_WalletNotFound_Returns404()
    {
        // Arrange
        var service = CreateService();
        _mockWalletStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletModel?)null);

        var request = new GetBalanceRequest
        {
            WalletId = Guid.NewGuid(),
            CurrencyDefinitionId = _definitionId
        };

        // Act
        var (status, _) = await service.GetBalanceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region Balance - BatchGetBalances

    [Fact]
    public async Task BatchGetBalances_ReturnsAllQueried()
    {
        // Arrange
        var service = CreateService();
        var defId1 = Guid.NewGuid();
        var defId2 = Guid.NewGuid();

        var balance1 = CreateBalance(_walletId, defId1, 100);
        var balance2 = CreateBalance(_walletId, defId2, 200);

        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(defId1.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance1);
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(defId2.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance2);

        var request = new BatchGetBalancesRequest
        {
            Queries = new List<BalanceQuery>
            {
                new() { WalletId = _walletId, CurrencyDefinitionId = defId1 },
                new() { WalletId = _walletId, CurrencyDefinitionId = defId2 }
            }
        };

        // Act
        var (status, response) = await service.BatchGetBalancesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Balances.Count);
    }

    #endregion

    #region Conversion - Calculate

    [Fact]
    public async Task CalculateConversion_ReturnsCorrectRate()
    {
        // Arrange
        var service = CreateService();
        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();
        var fromDef = CreateDefinition(defId: fromId, code: "GOLD", exchangeRateToBase: 2.0);
        var toDef = CreateDefinition(defId: toId, code: "GEMS", isBaseCurrency: true);

        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{fromId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fromDef);
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{toId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(toDef);

        var request = new CalculateConversionRequest
        {
            FromCurrencyId = fromId,
            ToCurrencyId = toId,
            FromAmount = 100
        };

        // Act
        var (status, response) = await service.CalculateConversionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2.0, response.EffectiveRate);
        Assert.Equal(200, response.ToAmount);
        Assert.NotNull(response.ConversionPath);
        Assert.Equal(2, response.ConversionPath.Count);
    }

    [Fact]
    public async Task CalculateConversion_CurrencyNotFound_Returns404()
    {
        // Arrange
        var service = CreateService();
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyDefinitionModel?)null);

        var request = new CalculateConversionRequest
        {
            FromCurrencyId = Guid.NewGuid(),
            ToCurrencyId = Guid.NewGuid(),
            FromAmount = 100
        };

        // Act
        var (status, _) = await service.CalculateConversionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region Conversion - GetExchangeRate

    [Fact]
    public async Task GetExchangeRate_ReturnsRateAndInverse()
    {
        // Arrange
        var service = CreateService();
        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();
        var fromDef = CreateDefinition(defId: fromId, exchangeRateToBase: 2.0);
        var toDef = CreateDefinition(defId: toId, isBaseCurrency: true);

        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{fromId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fromDef);
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{toId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(toDef);

        var request = new GetExchangeRateRequest
        {
            FromCurrencyId = fromId,
            ToCurrencyId = toId
        };

        // Act
        var (status, response) = await service.GetExchangeRateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2.0, response.Rate);
        Assert.Equal(0.5, response.InverseRate);
    }

    #endregion

    #region Conversion - UpdateExchangeRate

    [Fact]
    public async Task UpdateExchangeRate_Success_UpdatesRateAndPublishes()
    {
        // Arrange
        var service = CreateService();
        var def = CreateDefinition(exchangeRateToBase: 1.5);
        _mockDefinitionStore
            .Setup(s => s.GetWithETagAsync($"def:{_definitionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((def, "etag-1"));
        _mockDefinitionStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<CurrencyDefinitionModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");
        _mockDefStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("base-currency:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new UpdateExchangeRateRequest
        {
            CurrencyDefinitionId = _definitionId,
            ExchangeRateToBase = 3.0
        };

        // Act
        var (status, response) = await service.UpdateExchangeRateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1.5, response.PreviousRate);

        var rateEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.exchange-rate.updated");
        Assert.NotNull(rateEvent.Event);
        var typedEvent = Assert.IsType<CurrencyExchangeRateUpdatedEvent>(rateEvent.Event);
        Assert.Equal(3.0, typedEvent.NewRate);
        Assert.Equal(1.5, typedEvent.PreviousRate);
    }

    [Fact]
    public async Task UpdateExchangeRate_BaseCurrency_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var def = CreateDefinition(isBaseCurrency: true);
        _mockDefinitionStore
            .Setup(s => s.GetWithETagAsync($"def:{_definitionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((def, "etag-1"));

        var request = new UpdateExchangeRateRequest
        {
            CurrencyDefinitionId = _definitionId,
            ExchangeRateToBase = 2.0
        };

        // Act
        var (status, _) = await service.UpdateExchangeRateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task UpdateExchangeRate_ConcurrentConflict_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var def = CreateDefinition();
        _mockDefinitionStore
            .Setup(s => s.GetWithETagAsync($"def:{_definitionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((def, "etag-1"));
        _mockDefinitionStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<CurrencyDefinitionModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null); // Conflict on every attempt

        var request = new UpdateExchangeRateRequest
        {
            CurrencyDefinitionId = _definitionId,
            ExchangeRateToBase = 2.0
        };

        // Act
        var (status, _) = await service.UpdateExchangeRateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region Hold Operations - Create

    [Fact]
    public async Task CreateHold_Success_CreatesHoldAndPublishes()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition();
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var balance = CreateBalance(_walletId, _definitionId, 500);
        SetupBalanceExists(balance);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var request = new CreateHoldRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            ExpiresAt = expiresAt,
            ReferenceType = "escrow",
            IdempotencyKey = "hold-create-1"
        };

        // Act
        var (status, response) = await service.CreateHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(200, response.Hold.Amount);
        Assert.Equal(HoldStatus.Active, response.Hold.Status);

        // Verify hold saved via capture
        Assert.Single(_capturedHolds);
        Assert.Equal(200, _capturedHolds[0].Model.Amount);
        Assert.Equal(HoldStatus.Active, _capturedHolds[0].Model.Status);

        var holdEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.hold.created");
        Assert.NotNull(holdEvent.Event);
        var typedEvent = Assert.IsType<CurrencyHoldCreatedEvent>(holdEvent.Event);
        Assert.Equal(200, typedEvent.Amount);
    }

    [Fact]
    public async Task CreateHold_InsufficientEffectiveBalance_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition();
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var balance = CreateBalance(_walletId, _definitionId, 100);
        SetupBalanceExists(balance);

        var request = new CreateHoldRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200, // More than available balance
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            IdempotencyKey = "hold-insufficient"
        };

        // Act
        var (status, _) = await service.CreateHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task CreateHold_ZeroAmount_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new CreateHoldRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 0,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            IdempotencyKey = "hold-zero"
        };

        // Act
        var (status, _) = await service.CreateHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Hold Operations - Capture

    [Fact]
    public async Task CaptureHold_PartialCapture_DebitsAndReleasesRemainder()
    {
        // Arrange
        var service = CreateService();
        var holdId = Guid.NewGuid();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition();
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var hold = new HoldModel
        {
            HoldId = holdId,
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            Status = HoldStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        _mockHoldStore
            .Setup(s => s.GetWithETagAsync($"hold:{holdId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((hold, "etag-1"));
        _mockHoldStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<HoldModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Balance of 500
        var balance = CreateBalance(_walletId, _definitionId, 500);
        SetupBalanceExists(balance);

        var request = new CaptureHoldRequest
        {
            HoldId = holdId,
            CaptureAmount = 150,
            IdempotencyKey = "capture-partial"
        };

        // Act
        var (status, response) = await service.CaptureHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(50, response.AmountReleased); // 200 held - 150 captured = 50 released
        Assert.NotNull(response.Transaction);

        var captureEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.hold.captured");
        Assert.NotNull(captureEvent.Event);
        var typedEvent = Assert.IsType<CurrencyHoldCapturedEvent>(captureEvent.Event);
        Assert.Equal(150, typedEvent.CapturedAmount);
        Assert.Equal(50, typedEvent.AmountReleased);
    }

    [Fact]
    public async Task CaptureHold_NonActive_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var holdId = Guid.NewGuid();
        var hold = new HoldModel
        {
            HoldId = holdId,
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            Status = HoldStatus.Released
        };
        _mockHoldStore
            .Setup(s => s.GetWithETagAsync($"hold:{holdId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((hold, "etag-1"));

        var request = new CaptureHoldRequest
        {
            HoldId = holdId,
            CaptureAmount = 100,
            IdempotencyKey = "capture-released"
        };

        // Act
        var (status, _) = await service.CaptureHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task CaptureHold_ExceedsHoldAmount_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var holdId = Guid.NewGuid();
        var hold = new HoldModel
        {
            HoldId = holdId,
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            Status = HoldStatus.Active
        };
        _mockHoldStore
            .Setup(s => s.GetWithETagAsync($"hold:{holdId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((hold, "etag-1"));

        var request = new CaptureHoldRequest
        {
            HoldId = holdId,
            CaptureAmount = 200, // More than held
            IdempotencyKey = "capture-exceed"
        };

        // Act
        var (status, _) = await service.CaptureHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Hold Operations - Release

    [Fact]
    public async Task ReleaseHold_Active_ReleasesAndPublishes()
    {
        // Arrange
        var service = CreateService();
        var holdId = Guid.NewGuid();
        var wallet = CreateActiveWallet();
        var hold = new HoldModel
        {
            HoldId = holdId,
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            Status = HoldStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        _mockHoldStore
            .Setup(s => s.GetWithETagAsync($"hold:{holdId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((hold, "etag-1"));
        _mockHoldStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<HoldModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");
        SetupWalletExists(wallet);

        var request = new ReleaseHoldRequest { HoldId = holdId };

        // Act
        var (status, response) = await service.ReleaseHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(HoldStatus.Released, response.Hold.Status);

        var releaseEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.hold.released");
        Assert.NotNull(releaseEvent.Event);
        var typedEvent = Assert.IsType<CurrencyHoldReleasedEvent>(releaseEvent.Event);
        Assert.Equal(200, typedEvent.Amount);
    }

    [Fact]
    public async Task ReleaseHold_NotActive_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var holdId = Guid.NewGuid();
        var hold = new HoldModel
        {
            HoldId = holdId,
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            Status = HoldStatus.Captured
        };
        _mockHoldStore
            .Setup(s => s.GetWithETagAsync($"hold:{holdId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((hold, "etag-1"));

        var request = new ReleaseHoldRequest { HoldId = holdId };

        // Act
        var (status, _) = await service.ReleaseHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Hold Operations - Get

    [Fact]
    public async Task GetHold_Found_ReturnsHold()
    {
        // Arrange
        var service = CreateService();
        var holdId = Guid.NewGuid();
        var hold = new HoldModel
        {
            HoldId = holdId,
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            Status = HoldStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        _mockHoldStore
            .Setup(s => s.GetAsync($"hold:{holdId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold);

        var request = new GetHoldRequest { HoldId = holdId };

        // Act
        var (status, response) = await service.GetHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(holdId, response.Hold.HoldId);
        Assert.Equal(100, response.Hold.Amount);
    }

    [Fact]
    public async Task GetHold_NotFound_Returns404()
    {
        // Arrange
        var service = CreateService();
        _mockHoldStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HoldModel?)null);

        var request = new GetHoldRequest { HoldId = Guid.NewGuid() };

        // Act
        var (status, _) = await service.GetHoldAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region Escrow Operations

    [Fact]
    public async Task EscrowDeposit_DelegatesToDebit()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition();
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var balance = CreateBalance(_walletId, _definitionId, 500);
        SetupBalanceExists(balance);

        var escrowId = Guid.NewGuid();
        var request = new EscrowDepositRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            EscrowId = escrowId,
            IdempotencyKey = "escrow-deposit-1"
        };

        // Act
        var (status, response) = await service.EscrowDepositAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(300, response.NewBalance);

        // Verify transaction recorded with EscrowDeposit type
        var debitEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "currency.debited");
        Assert.NotNull(debitEvent.Event);
        var typedEvent = Assert.IsType<CurrencyDebitedEvent>(debitEvent.Event);
        Assert.Equal(TransactionType.EscrowDeposit, typedEvent.TransactionType);
    }

    [Fact]
    public async Task EscrowRelease_DelegatesToCreditWithBypassEarnCap()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition(dailyEarnCap: 10); // Low cap but escrow should bypass
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var escrowId = Guid.NewGuid();
        var request = new EscrowReleaseRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            EscrowId = escrowId,
            IdempotencyKey = "escrow-release-1"
        };

        // Act
        var (status, response) = await service.EscrowReleaseAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(200, response.NewBalance); // Full amount, not capped
    }

    [Fact]
    public async Task EscrowRefund_DelegatesToCreditWithBypassEarnCap()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        var def = CreateDefinition(dailyEarnCap: 10);
        SetupWalletExists(wallet);
        SetupDefinitionExists(def);

        var escrowId = Guid.NewGuid();
        var request = new EscrowRefundRequest
        {
            WalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            EscrowId = escrowId,
            IdempotencyKey = "escrow-refund-1"
        };

        // Act
        var (status, response) = await service.EscrowRefundAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(100, response.NewBalance);
    }

    #endregion

    #region Transaction Tracking

    [Fact]
    public async Task GetTransaction_Found_ReturnsTransaction()
    {
        // Arrange
        var service = CreateService();
        var txId = Guid.NewGuid();
        var tx = new TransactionModel
        {
            TransactionId = txId,
            SourceWalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.Mint,
            Timestamp = DateTimeOffset.UtcNow,
            IdempotencyKey = "tx-1"
        };
        _mockTransactionStore
            .Setup(s => s.GetAsync($"tx:{txId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);

        var request = new GetTransactionRequest { TransactionId = txId };

        // Act
        var (status, response) = await service.GetTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(txId, response.Transaction.TransactionId);
        Assert.Equal(100, response.Transaction.Amount);
    }

    [Fact]
    public async Task GetTransaction_NotFound_Returns404()
    {
        // Arrange
        var service = CreateService();
        _mockTransactionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);

        var request = new GetTransactionRequest { TransactionId = Guid.NewGuid() };

        // Act
        var (status, _) = await service.GetTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task GetTransactionHistory_ReturnsFilteredAndPaged()
    {
        // Arrange
        var service = CreateService();
        var wallet = CreateActiveWallet();
        SetupWalletExists(wallet);

        var txId1 = Guid.NewGuid();
        var txId2 = Guid.NewGuid();
        _mockTransactionStringStore
            .Setup(s => s.GetAsync($"tx-wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { txId1.ToString(), txId2.ToString() }));

        var now = DateTimeOffset.UtcNow;
        var tx1 = new TransactionModel
        {
            TransactionId = txId1,
            SourceWalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.Mint,
            Timestamp = now,
            IdempotencyKey = "h-1"
        };
        var tx2 = new TransactionModel
        {
            TransactionId = txId2,
            SourceWalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 200,
            TransactionType = TransactionType.VendorPurchase,
            Timestamp = now.AddMinutes(-5),
            IdempotencyKey = "h-2"
        };

        _mockTransactionStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, TransactionModel>
            {
                [$"tx:{txId1}"] = tx1,
                [$"tx:{txId2}"] = tx2
            } as IReadOnlyDictionary<string, TransactionModel>);

        var request = new GetTransactionHistoryRequest
        {
            WalletId = _walletId,
            Limit = 10,
            Offset = 0
        };

        // Act
        var (status, response) = await service.GetTransactionHistoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Transactions.Count);
    }

    [Fact]
    public async Task GetTransactionsByReference_ReturnsMatchingTransactions()
    {
        // Arrange
        var service = CreateService();
        var txId = Guid.NewGuid();
        var refId = Guid.NewGuid();

        _mockTransactionStringStore
            .Setup(s => s.GetAsync($"tx-ref:escrow:{refId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { txId.ToString() }));

        var tx = new TransactionModel
        {
            TransactionId = txId,
            SourceWalletId = _walletId,
            CurrencyDefinitionId = _definitionId,
            Amount = 100,
            TransactionType = TransactionType.EscrowDeposit,
            ReferenceType = "escrow",
            ReferenceId = refId,
            Timestamp = DateTimeOffset.UtcNow,
            IdempotencyKey = "ref-1"
        };

        _mockTransactionStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, TransactionModel>
            {
                [$"tx:{txId}"] = tx
            } as IReadOnlyDictionary<string, TransactionModel>);

        var request = new GetTransactionsByReferenceRequest
        {
            ReferenceType = "escrow",
            ReferenceId = refId
        };

        // Act
        var (status, response) = await service.GetTransactionsByReferenceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Transactions);
        Assert.Equal(txId, response.Transactions.First().TransactionId);
    }

    #endregion

    #region Batch Operations

    [Fact]
    public async Task BatchCredit_IdempotencyDuplicate_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        _mockIdempotencyStringStore
            .Setup(s => s.GetAsync("batch-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        var request = new BatchCreditRequest
        {
            IdempotencyKey = "batch-dup",
            Operations = new List<BatchCreditOperation>
            {
                new()
                {
                    WalletId = _walletId,
                    CurrencyDefinitionId = _definitionId,
                    Amount = 100,
                    TransactionType = TransactionType.Mint
                }
            }
        };

        // Act
        var (status, _) = await service.BatchCreditCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task BatchDebit_IdempotencyDuplicate_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        _mockIdempotencyStringStore
            .Setup(s => s.GetAsync("batch-debit-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        var request = new BatchDebitRequest
        {
            IdempotencyKey = "batch-debit-dup",
            Operations = new List<BatchDebitOperation>
            {
                new()
                {
                    WalletId = _walletId,
                    CurrencyDefinitionId = _definitionId,
                    Amount = 100,
                    TransactionType = TransactionType.VendorPurchase
                }
            }
        };

        // Act
        var (status, _) = await service.BatchDebitCurrencyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region Event Handlers (Cache Invalidation)

    [Fact]
    public async Task EventConsumer_RegistersHandlerForCreditedEvents()
    {
        // Arrange & Act - constructor calls RegisterEventConsumers
        var service = CreateService();

        // Verify that Register was called for credited events
        // RegisterHandler extension calls Register<TEvent>(topicName, handlerKey, handler)
        _mockEventConsumer.Verify(
            ec => ec.Register<CurrencyCreditedEvent>(
                "currency.credited",
                It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, CurrencyCreditedEvent, Task>>()),
            Times.Once);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task EventConsumer_RegistersHandlerForDebitedEvents()
    {
        // Arrange & Act
        var service = CreateService();

        // Verify debited handler registration
        _mockEventConsumer.Verify(
            ec => ec.Register<CurrencyDebitedEvent>(
                "currency.debited",
                It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, CurrencyDebitedEvent, Task>>()),
            Times.Once);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task EventConsumer_RegistersHandlerForTransferredEvents()
    {
        // Arrange & Act
        var service = CreateService();

        // Verify transferred handler registration
        _mockEventConsumer.Verify(
            ec => ec.Register<CurrencyTransferredEvent>(
                "currency.transferred",
                It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, CurrencyTransferredEvent, Task>>()),
            Times.Once);

        await Task.CompletedTask;
    }

    #endregion
}
