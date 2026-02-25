using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Currency.Tests;

/// <summary>
/// Unit tests for CurrencyService.
/// Tests constructor validation, configuration, and permission registration.
/// Full business logic testing is done via HTTP integration tests.
/// </summary>
public class CurrencyServiceTests
{
    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// Catches: multiple constructors, optional parameters, missing null checks,
    /// and wrong parameter names in ArgumentNullException.
    /// </summary>
    [Fact]
    public void CurrencyService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<CurrencyService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void CurrencyServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new CurrencyServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void CurrencyServiceConfiguration_HasExpectedDefaults()
    {
        // Arrange & Act
        var config = new CurrencyServiceConfiguration();

        // Assert - verify default values are sensible
        Assert.False(config.DefaultAllowNegative);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
        // Currency API has 31 endpoints defined
        Assert.True(endpoints.Count >= 25, $"Expected at least 25 endpoints, got {endpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldContainDefinitionEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert - verify core endpoints exist
        var createEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/currency/definition/create" &&
            e.Method == ServiceEndpointMethod.POST);

        Assert.NotNull(createEndpoint);
        Assert.NotNull(createEndpoint.Permissions);
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldHaveWalletEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        var walletEndpoints = endpoints.Where(e => e.Path.Contains("/wallet")).ToList();
        Assert.True(walletEndpoints.Count >= 5, $"Expected at least 5 wallet endpoints, got {walletEndpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldHaveHoldEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        var holdEndpoints = endpoints.Where(e => e.Path.Contains("/hold")).ToList();
        Assert.True(holdEndpoints.Count >= 4, $"Expected at least 4 hold endpoints, got {holdEndpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldHaveBalanceEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        var balanceEndpoints = endpoints.Where(e => e.Path.Contains("/balance")).ToList();
        Assert.True(balanceEndpoints.Count >= 2, $"Expected at least 2 balance endpoints, got {balanceEndpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldHaveTransactionEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        var transactionEndpoints = endpoints.Where(e => e.Path.Contains("/transaction")).ToList();
        Assert.True(transactionEndpoints.Count >= 2, $"Expected at least 2 transaction endpoints, got {transactionEndpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_AllEndpointsHavePermissions()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert - every endpoint must have permissions defined
        foreach (var endpoint in endpoints)
        {
            Assert.NotNull(endpoint.Permissions);
            Assert.NotEmpty(endpoint.Permissions);
        }
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_AllEndpointsArePOST()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert - POST-only API pattern
        foreach (var endpoint in endpoints)
        {
            Assert.Equal(ServiceEndpointMethod.POST, endpoint.Method);
        }
    }

    #endregion

    #region Model Validation Tests

    [Fact]
    public void CurrencyScope_HasExpectedValues()
    {
        // Assert - verify enum values exist
        Assert.True(Enum.IsDefined(typeof(CurrencyScope), CurrencyScope.Global));
        Assert.True(Enum.IsDefined(typeof(CurrencyScope), CurrencyScope.RealmSpecific));
        Assert.True(Enum.IsDefined(typeof(CurrencyScope), CurrencyScope.MultiRealm));
    }

    [Fact]
    public void WalletStatus_HasExpectedValues()
    {
        // Assert - verify enum values exist
        Assert.True(Enum.IsDefined(typeof(WalletStatus), WalletStatus.Active));
        Assert.True(Enum.IsDefined(typeof(WalletStatus), WalletStatus.Frozen));
        Assert.True(Enum.IsDefined(typeof(WalletStatus), WalletStatus.Closed));
    }

    [Fact]
    public void HoldStatus_HasExpectedValues()
    {
        // Assert - verify enum values exist
        Assert.True(Enum.IsDefined(typeof(HoldStatus), HoldStatus.Active));
        Assert.True(Enum.IsDefined(typeof(HoldStatus), HoldStatus.Captured));
        Assert.True(Enum.IsDefined(typeof(HoldStatus), HoldStatus.Released));
        Assert.True(Enum.IsDefined(typeof(HoldStatus), HoldStatus.Expired));
    }

    [Fact]
    public void CurrencyPrecision_HasExpectedValues()
    {
        // Assert - verify enum values exist
        Assert.True(Enum.IsDefined(typeof(CurrencyPrecision), CurrencyPrecision.Integer));
        Assert.True(Enum.IsDefined(typeof(CurrencyPrecision), CurrencyPrecision.Decimal2));
    }

    [Fact]
    public void EntityType_HasExpectedValues()
    {
        // Assert - verify enum values exist for wallet owner types
        Assert.True(Enum.IsDefined(typeof(EntityType), EntityType.Account));
        Assert.True(Enum.IsDefined(typeof(EntityType), EntityType.Character));
    }

    #endregion

    #region Request Model Tests

    [Fact]
    public void CreateCurrencyDefinitionRequest_CanBeInstantiated()
    {
        // Arrange & Act
        var request = new CreateCurrencyDefinitionRequest
        {
            Code = "gold",
            Name = "Gold Currency",
            Description = "Test currency",
            Scope = CurrencyScope.Global,
            Precision = CurrencyPrecision.Decimal2
        };

        // Assert
        Assert.Equal("gold", request.Code);
        Assert.Equal("Gold Currency", request.Name);
        Assert.Equal(CurrencyScope.Global, request.Scope);
    }

    [Fact]
    public void CreateWalletRequest_CanBeInstantiated()
    {
        // Arrange & Act
        var ownerId = Guid.NewGuid();
        var request = new CreateWalletRequest
        {
            OwnerId = ownerId,
            OwnerType = EntityType.Account
        };

        // Assert
        Assert.Equal(ownerId, request.OwnerId);
        Assert.Equal(EntityType.Account, request.OwnerType);
    }

    [Fact]
    public void CreditCurrencyRequest_CanBeInstantiated()
    {
        // Arrange & Act
        var walletId = Guid.NewGuid();
        var currencyId = Guid.NewGuid();
        var request = new CreditCurrencyRequest
        {
            WalletId = walletId,
            CurrencyDefinitionId = currencyId,
            Amount = 100.50,
            TransactionType = TransactionType.Mint,
            ReferenceType = "test",
            IdempotencyKey = "test-key"
        };

        // Assert
        Assert.Equal(walletId, request.WalletId);
        Assert.Equal(currencyId, request.CurrencyDefinitionId);
        Assert.Equal(100.50, request.Amount);
        Assert.Equal(TransactionType.Mint, request.TransactionType);
    }

    [Fact]
    public void CreateHoldRequest_CanBeInstantiated()
    {
        // Arrange & Act
        var walletId = Guid.NewGuid();
        var currencyId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var request = new CreateHoldRequest
        {
            WalletId = walletId,
            CurrencyDefinitionId = currencyId,
            Amount = 50.00,
            ExpiresAt = expiresAt,
            ReferenceType = "test",
            IdempotencyKey = "hold-key"
        };

        // Assert
        Assert.Equal(walletId, request.WalletId);
        Assert.Equal(currencyId, request.CurrencyDefinitionId);
        Assert.Equal(50.00, request.Amount);
        Assert.Equal(expiresAt, request.ExpiresAt);
    }

    #endregion
}

/// <summary>
/// Tests for currency conversion concurrency protections:
/// wallet cap pre-validation, compensating credit on failure, and earn cap bypass.
/// </summary>
public class CurrencyConversionConcurrencyTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<CurrencyService>> _mockLogger;
    private readonly CurrencyServiceConfiguration _configuration;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    // Typed stores
    private readonly Mock<IStateStore<WalletModel>> _mockWalletStore;
    private readonly Mock<IStateStore<CurrencyDefinitionModel>> _mockDefinitionStore;
    private readonly Mock<IStateStore<BalanceModel>> _mockBalanceStore;
    private readonly Mock<IStateStore<BalanceModel>> _mockBalanceCacheStore;
    private readonly Mock<IStateStore<TransactionModel>> _mockTransactionStore;
    private readonly Mock<IStateStore<HoldModel>> _mockHoldStore;

    // String stores (for indexes and idempotency)
    private readonly Mock<IStateStore<string>> _mockIdempotencyStringStore;
    private readonly Mock<IStateStore<string>> _mockBalanceStringStore;
    private readonly Mock<IStateStore<string>> _mockTransactionStringStore;
    private readonly Mock<IStateStore<string>> _mockHoldsStringStore;

    // Test data
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly Guid _fromCurrencyId = Guid.NewGuid();
    private readonly Guid _toCurrencyId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();

    public CurrencyConversionConcurrencyTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<CurrencyService>>();
        _configuration = new CurrencyServiceConfiguration();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        // Initialize stores
        _mockWalletStore = new Mock<IStateStore<WalletModel>>();
        _mockDefinitionStore = new Mock<IStateStore<CurrencyDefinitionModel>>();
        _mockBalanceStore = new Mock<IStateStore<BalanceModel>>();
        _mockBalanceCacheStore = new Mock<IStateStore<BalanceModel>>();
        _mockTransactionStore = new Mock<IStateStore<TransactionModel>>();
        _mockHoldStore = new Mock<IStateStore<HoldModel>>();
        _mockIdempotencyStringStore = new Mock<IStateStore<string>>();
        _mockBalanceStringStore = new Mock<IStateStore<string>>();
        _mockTransactionStringStore = new Mock<IStateStore<string>>();
        _mockHoldsStringStore = new Mock<IStateStore<string>>();

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
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyIdempotency))
            .Returns(_mockIdempotencyStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyBalances))
            .Returns(_mockBalanceStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyTransactions))
            .Returns(_mockTransactionStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CurrencyHolds))
            .Returns(_mockHoldsStringStore.Object);

        // Default: all idempotency checks pass (not duplicate)
        _mockIdempotencyStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Default: message bus succeeds
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default: lock provider succeeds
        var successLock = new Mock<ILockResponse>();
        successLock.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

        // Default: hold index returns empty (no holds)
        _mockHoldsStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Default: balance index operations succeed
        _mockBalanceStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockTransactionStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    private CurrencyService CreateService()
    {
        return new CurrencyService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);
    }

    private WalletModel CreateTestWallet()
    {
        return new WalletModel
        {
            WalletId = _walletId,
            OwnerId = _ownerId,
            OwnerType = EntityType.Account,
            Status = WalletStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
    }

    private CurrencyDefinitionModel CreateFromDefinition(double exchangeRate = 2.0)
    {
        return new CurrencyDefinitionModel
        {
            DefinitionId = _fromCurrencyId,
            Code = "GOLD",
            Name = "Gold",
            Scope = CurrencyScope.Global,
            Precision = CurrencyPrecision.Decimal2,
            IsBaseCurrency = false,
            ExchangeRateToBase = exchangeRate,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-90)
        };
    }

    private CurrencyDefinitionModel CreateToDefinition(
        double exchangeRate = 1.0,
        double? perWalletCap = null,
        CapOverflowBehavior? capOverflowBehavior = null,
        double? dailyEarnCap = null)
    {
        return new CurrencyDefinitionModel
        {
            DefinitionId = _toCurrencyId,
            Code = "GEMS",
            Name = "Gems",
            Scope = CurrencyScope.Global,
            Precision = CurrencyPrecision.Integer,
            IsBaseCurrency = true,
            ExchangeRateToBase = exchangeRate,
            PerWalletCap = perWalletCap,
            CapOverflowBehavior = capOverflowBehavior,
            DailyEarnCap = dailyEarnCap,
            IsActive = true,
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

    private void SetupWalletAndDefinitions(
        CurrencyDefinitionModel fromDef,
        CurrencyDefinitionModel toDef)
    {
        var wallet = CreateTestWallet();

        _mockWalletStore
            .Setup(s => s.GetAsync($"wallet:{_walletId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{_fromCurrencyId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fromDef);
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"def:{_toCurrencyId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(toDef);
    }

    #region Wallet Cap Pre-Validation Tests

    [Fact]
    public async Task ExecuteConversionAsync_WalletCapRejectPreValidation_Returns422BeforeDebit()
    {
        // Arrange
        var service = CreateService();

        var fromDef = CreateFromDefinition(exchangeRate: 2.0);
        var toDef = CreateToDefinition(
            exchangeRate: 1.0,
            perWalletCap: 100,
            capOverflowBehavior: CapOverflowBehavior.Reject);
        SetupWalletAndDefinitions(fromDef, toDef);

        // Target balance is at 80; conversion of 50 from (rate 2.0/1.0) = 100 to-amount
        // 80 + 100 = 180 > 100 cap → should reject
        var targetBalance = CreateBalance(_walletId, _toCurrencyId, 80);
        _mockBalanceCacheStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_toCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BalanceModel?)null);
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_toCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetBalance);

        var request = new ExecuteConversionRequest
        {
            WalletId = _walletId,
            FromCurrencyId = _fromCurrencyId,
            ToCurrencyId = _toCurrencyId,
            FromAmount = 50,
            IdempotencyKey = "test-conversion-cap"
        };

        // Act
        var (status, response) = await service.ExecuteConversionAsync(request);

        // Assert - should be rejected before any debit occurs
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);

        // Verify no balance lock was acquired (debit never started)
        _mockLockProvider.Verify(
            l => l.LockAsync("currency-balance", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify no transaction was recorded
        _mockTransactionStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<TransactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Compensation on Credit Failure Tests

    [Fact]
    public async Task ExecuteConversionAsync_CreditLockFails_ReversesDebitWithCompensatingCredit()
    {
        // Arrange
        var service = CreateService();

        var fromDef = CreateFromDefinition(exchangeRate: 2.0);
        var toDef = CreateToDefinition(exchangeRate: 1.0); // No cap, simple conversion
        SetupWalletAndDefinitions(fromDef, toDef);

        // Source balance has 1000 units
        var sourceBalance = CreateBalance(_walletId, _fromCurrencyId, 1000);
        _mockBalanceCacheStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_fromCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BalanceModel?)null);
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_fromCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBalance);

        // Target balance (for credit - will never be used since lock fails)
        _mockBalanceCacheStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_toCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BalanceModel?)null);
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_toCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BalanceModel?)null); // Will use default balance (amount = 0)

        // Lock setup: succeed for fromCurrency, FAIL for toCurrency
        var successLock = new Mock<ILockResponse>();
        successLock.Setup(l => l.Success).Returns(true);
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);

        _mockLockProvider
            .Setup(l => l.LockAsync(
                "currency-balance",
                It.Is<string>(k => k.Contains(_toCurrencyId.ToString())),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Keep default (succeed) for fromCurrency and currency-index locks

        var request = new ExecuteConversionRequest
        {
            WalletId = _walletId,
            FromCurrencyId = _fromCurrencyId,
            ToCurrencyId = _toCurrencyId,
            FromAmount = 100,
            IdempotencyKey = "test-conversion-compensate"
        };

        // Act
        var (status, response) = await service.ExecuteConversionAsync(request);

        // Assert - credit failed with Conflict, compensation should have fired
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify: lock was acquired for fromCurrency at least 2 times
        // (once for debit, once for compensating credit)
        _mockLockProvider.Verify(
            l => l.LockAsync(
                "currency-balance",
                It.Is<string>(k => k.Contains(_fromCurrencyId.ToString())),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));

        // Verify: balance was saved at least 2 times (debit save + compensating credit save)
        _mockBalanceStore.Verify(
            s => s.SaveAsync(
                It.Is<string>(k => k.Contains(_fromCurrencyId.ToString())),
                It.IsAny<BalanceModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    #endregion

    #region BypassEarnCap Tests

    [Fact]
    public async Task ExecuteConversionAsync_CreditBypassesEarnCap_FullAmountCredited()
    {
        // Arrange
        var service = CreateService();

        // From: exchange rate 1.0 (base currency), To: exchange rate 1.0, daily earn cap of 10
        var fromDef = CreateFromDefinition(exchangeRate: 1.0);
        fromDef.IsBaseCurrency = true;
        var toDef = CreateToDefinition(exchangeRate: 1.0, dailyEarnCap: 10);
        toDef.IsBaseCurrency = false;
        toDef.ExchangeRateToBase = 1.0;
        SetupWalletAndDefinitions(fromDef, toDef);

        // Source balance: 1000
        var sourceBalance = CreateBalance(_walletId, _fromCurrencyId, 1000);
        _mockBalanceCacheStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_fromCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BalanceModel?)null);
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_fromCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBalance);

        // Target balance: already earned 9 of 10 daily cap
        var targetBalance = CreateBalance(_walletId, _toCurrencyId, 50);
        targetBalance.DailyEarned = 9; // Almost at daily cap of 10
        _mockBalanceCacheStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_toCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BalanceModel?)null);
        _mockBalanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(_toCurrencyId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetBalance);

        var request = new ExecuteConversionRequest
        {
            WalletId = _walletId,
            FromCurrencyId = _fromCurrencyId,
            ToCurrencyId = _toCurrencyId,
            FromAmount = 50, // Rate 1:1, so credit should be 50
            IdempotencyKey = "test-conversion-earncap"
        };

        // Act
        var (status, response) = await service.ExecuteConversionAsync(request);

        // Assert - conversion should succeed (earn cap bypassed for conversions)
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(50, response.ToCredited); // Full amount, not capped to 1 (10 - 9 = 1)

        // Verify: target balance was saved with the full credited amount (50 + 50 = 100)
        _mockBalanceStore.Verify(
            s => s.SaveAsync(
                It.Is<string>(k => k.Contains(_toCurrencyId.ToString())),
                It.Is<BalanceModel>(b => b.Amount == 100), // 50 existing + 50 credited
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
