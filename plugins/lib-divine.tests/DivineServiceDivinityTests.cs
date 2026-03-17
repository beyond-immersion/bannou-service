using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Collection;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Divine;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Divine.Tests;

/// <summary>
/// Unit tests for DivineService divinity economy operations.
/// Tests balance queries, credit, debit, and transaction history.
/// </summary>
public class DivineServiceDivinityTests : ServiceTestBase<DivineServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IJsonQueryableStateStore<DeityModel>> _mockDeityStore;
    private readonly Mock<IJsonQueryableStateStore<BlessingModel>> _mockBlessingStore;
    private readonly Mock<ICacheableStateStore<AttentionSlotModel>> _mockAttentionStore;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<DivineService>> _mockLogger;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ICurrencyClient> _mockCurrencyClient;
    private readonly Mock<IRelationshipClient> _mockRelationshipClient;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;
    private readonly Mock<ISeedClient> _mockSeedClient;
    private readonly Mock<ICollectionClient> _mockCollectionClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public DivineServiceDivinityTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockDeityStore = new Mock<IJsonQueryableStateStore<DeityModel>>();
        _mockBlessingStore = new Mock<IJsonQueryableStateStore<BlessingModel>>();
        _mockAttentionStore = new Mock<ICacheableStateStore<AttentionSlotModel>>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<DivineService>>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockCurrencyClient = new Mock<ICurrencyClient>();
        _mockRelationshipClient = new Mock<IRelationshipClient>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();
        _mockSeedClient = new Mock<ISeedClient>();
        _mockCollectionClient = new Mock<ICollectionClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<DeityModel>(StateStoreDefinitions.DivineDeities))
            .Returns(_mockDeityStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<BlessingModel>(StateStoreDefinitions.DivineBlessings))
            .Returns(_mockBlessingStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<AttentionSlotModel>(StateStoreDefinitions.DivineAttention))
            .Returns(_mockAttentionStore.Object);

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private DivineService CreateService()
    {
        var serviceCollection = new ServiceCollection();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        return new DivineService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLockProvider.Object,
            new NullTelemetryProvider(),
            _mockResourceClient.Object,
            _mockCurrencyClient.Object,
            _mockRelationshipClient.Object,
            _mockCharacterClient.Object,
            _mockGameServiceClient.Object,
            _mockSeedClient.Object,
            _mockCollectionClient.Object,
            serviceProvider,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object);
    }

    private static DeityModel CreateTestDeityModel(Guid deityId, Guid? walletId = null)
    {
        return new DeityModel
        {
            DeityId = deityId,
            GameServiceId = Guid.NewGuid(),
            Code = "ZEUS",
            DisplayName = "Zeus",
            Description = "Test deity",
            Domains = new List<DomainInfluenceData>(),
            Status = DeityStatus.Active,
            CurrencyWalletId = walletId ?? Guid.NewGuid(),
            FollowerCount = 0,
            MaxAttentionSlots = 10,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    #region GetDivinityBalance Tests

    [Fact]
    public async Task GetDivinityBalanceAsync_ExistingDeity_ReturnsBalance()
    {
        // Map: READ deity -> 404, CALL currencyClient.GetBalanceAsync, RETURN balance
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId, walletId));

        _mockCurrencyClient
            .Setup(c => c.GetBalanceAsync(
                It.Is<GetBalanceRequest>(r => r.WalletId == walletId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBalanceResponse { Amount = 150.0 });

        var (status, response) = await service.GetDivinityBalanceAsync(
            new GetDivinityBalanceRequest { DeityId = deityId }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(150.0, response.Balance);
    }

    [Fact]
    public async Task GetDivinityBalanceAsync_DeityNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, _) = await service.GetDivinityBalanceAsync(
            new GetDivinityBalanceRequest { DeityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region CreditDivinity Tests

    [Fact]
    public async Task CreditDivinityAsync_ValidCredit_ReturnsOkAndPublishesCredited()
    {
        // Map: READ deity -> 404, CALL currencyClient.CreditCurrencyAsync, PUBLISH credited
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId, walletId));

        _mockCurrencyClient
            .Setup(c => c.CreditCurrencyAsync(
                It.IsAny<CreditCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreditCurrencyResponse { NewBalance = 250.0 });

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var (status, response) = await service.CreditDivinityAsync(
            new CreditDivinityRequest { DeityId = deityId, Amount = 100.0, Source = "prayer" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("divine.divinity.credited", capturedTopic);
        var typedEvent = Assert.IsType<DivineDivinityCreditedEvent>(capturedEvent);
        Assert.Equal(deityId, typedEvent.DeityId);
        Assert.Equal(100.0, typedEvent.Amount);
        Assert.Equal("prayer", typedEvent.Source);
    }

    [Fact]
    public async Task CreditDivinityAsync_DeityNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, _) = await service.CreditDivinityAsync(
            new CreditDivinityRequest { DeityId = Guid.NewGuid(), Amount = 100.0, Source = "prayer" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region DebitDivinity Tests

    [Fact]
    public async Task DebitDivinityAsync_SufficientBalance_ReturnsOkAndPublishesDebited()
    {
        // Map: READ deity -> 404, CALL currencyClient.DebitCurrencyAsync -> 400, PUBLISH debited
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId, walletId));

        _mockCurrencyClient
            .Setup(c => c.DebitCurrencyAsync(
                It.IsAny<DebitCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DebitCurrencyResponse { NewBalance = 50.0 });

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var (status, response) = await service.DebitDivinityAsync(
            new DebitDivinityRequest { DeityId = deityId, Amount = 50.0, Purpose = "miracle" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("divine.divinity.debited", capturedTopic);
        var typedEvent = Assert.IsType<DivineDivinityDebitedEvent>(capturedEvent);
        Assert.Equal(deityId, typedEvent.DeityId);
        Assert.Equal(50.0, typedEvent.Amount);
        Assert.Equal("miracle", typedEvent.Purpose);
    }

    [Fact]
    public async Task DebitDivinityAsync_InsufficientBalance_ReturnsBadRequest()
    {
        // Map: CALL currencyClient.DebitCurrencyAsync -> 400 if insufficient (throws ApiException)
        var service = CreateService();
        var deityId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId));

        _mockCurrencyClient
            .Setup(c => c.DebitCurrencyAsync(
                It.IsAny<DebitCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Insufficient balance", 400, "", null, null));

        var (status, _) = await service.DebitDivinityAsync(
            new DebitDivinityRequest { DeityId = deityId, Amount = 9999.0, Purpose = "miracle" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task DebitDivinityAsync_DeityNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, _) = await service.DebitDivinityAsync(
            new DebitDivinityRequest { DeityId = Guid.NewGuid(), Amount = 50.0, Purpose = "miracle" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region GetDivinityHistory Tests

    [Fact]
    public async Task GetDivinityHistoryAsync_DeityNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, _) = await service.GetDivinityHistoryAsync(
            new GetDivinityHistoryRequest { DeityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion
}
