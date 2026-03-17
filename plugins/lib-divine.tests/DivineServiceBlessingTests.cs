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
/// Unit tests for DivineService blessing operations.
/// Tests grant, revoke, list, and get blessings.
/// </summary>
public class DivineServiceBlessingTests : ServiceTestBase<DivineServiceConfiguration>
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

    public DivineServiceBlessingTests()
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

        SetupDefaultLockSuccess();

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

    private void SetupDefaultLockSuccess()
    {
        var mockLock = new Mock<ILockResponse>();
        mockLock.Setup(l => l.Success).Returns(true);
        mockLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLock.Object);
    }

    private static DeityModel CreateTestDeityModel(Guid deityId, DeityStatus status = DeityStatus.Active)
    {
        return new DeityModel
        {
            DeityId = deityId,
            GameServiceId = Guid.NewGuid(),
            Code = "ZEUS",
            DisplayName = "Zeus",
            Description = "Test deity",
            Domains = new List<DomainInfluenceData>(),
            Status = status,
            CurrencyWalletId = Guid.NewGuid(),
            FollowerCount = 5,
            MaxAttentionSlots = 10,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    private static BlessingModel CreateTestBlessingModel(
        Guid blessingId, Guid deityId,
        BlessingTier tier = BlessingTier.Minor,
        BlessingStatus status = BlessingStatus.Active)
    {
        return new BlessingModel
        {
            BlessingId = blessingId,
            DeityId = deityId,
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Character,
            Tier = tier,
            Status = status,
            ItemInstanceId = Guid.NewGuid(),
            ItemTemplateCode = "blessing_of_strength",
            Reason = "Granted for valor",
            GrantedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
    }

    #region GrantBlessing Tests

    [Fact]
    public async Task GrantBlessingAsync_GreaterTier_UsesCollectionAndPublishesGranted()
    {
        // Map: READ deity -> 404, check Active -> 400, count blessings -> 409,
        //       CALL DebitCurrencyAsync -> 400, CALL collectionClient.UnlockAsync,
        //       WRITE blessing, PUBLISH divine.blessing.granted
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId));

        _mockBlessingStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _mockCurrencyClient
            .Setup(c => c.DebitCurrencyAsync(
                It.IsAny<DebitCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DebitCurrencyResponse { NewBalance = 800.0 });

        _mockCollectionClient
            .Setup(c => c.GrantEntryAsync(
                It.IsAny<GrantEntryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrantEntryResponse { EntryTemplateId = Guid.NewGuid() });

        BlessingModel? savedBlessing = null;
        _mockBlessingStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("blessing:")),
                It.IsAny<BlessingModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BlessingModel, StateOptions?, CancellationToken>((_, m, _, _) => savedBlessing = m)
            .ReturnsAsync("etag-1");

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

        var (status, response) = await service.GrantBlessingAsync(
            new GrantBlessingRequest
            {
                DeityId = deityId,
                EntityId = entityId,
                EntityType = EntityType.Character,
                Tier = BlessingTier.Greater,
                ItemTemplateCode = "blessing_of_strength",
                Reason = "Valor in battle",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(BlessingTier.Greater, response.Tier);

        Assert.NotNull(savedBlessing);
        Assert.Equal(BlessingTier.Greater, savedBlessing.Tier);
        Assert.Equal(entityId, savedBlessing.EntityId);

        Assert.Equal("divine.blessing.granted", capturedTopic);
        var typedEvent = Assert.IsType<DivineBlessingGrantedEvent>(capturedEvent);
        Assert.Equal(deityId, typedEvent.DeityId);
        Assert.Equal(BlessingTier.Greater, typedEvent.Tier);
    }

    [Fact]
    public async Task GrantBlessingAsync_DeityNotActive_ReturnsBadRequest()
    {
        var service = CreateService();
        var deityId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId, DeityStatus.Dormant));

        var (status, _) = await service.GrantBlessingAsync(
            new GrantBlessingRequest
            {
                DeityId = deityId,
                EntityId = Guid.NewGuid(),
                EntityType = EntityType.Character,
                Tier = BlessingTier.Minor,
                ItemTemplateCode = "minor_blessing",
                Reason = "Test",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task GrantBlessingAsync_MaxBlessingsReached_ReturnsConflict()
    {
        // Map: COUNT active blessings -> 409 if >= MaxBlessingsPerEntity
        var service = CreateService();
        var deityId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId));

        _mockBlessingStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Configuration.MaxBlessingsPerEntity);

        var (status, _) = await service.GrantBlessingAsync(
            new GrantBlessingRequest
            {
                DeityId = deityId,
                EntityId = Guid.NewGuid(),
                EntityType = EntityType.Character,
                Tier = BlessingTier.Minor,
                ItemTemplateCode = "minor_blessing",
                Reason = "Test",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task GrantBlessingAsync_InsufficientDivinity_ReturnsBadRequest()
    {
        var service = CreateService();
        var deityId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId));

        _mockBlessingStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _mockCurrencyClient
            .Setup(c => c.DebitCurrencyAsync(
                It.IsAny<DebitCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Insufficient balance", 400, "", null, null));

        var (status, _) = await service.GrantBlessingAsync(
            new GrantBlessingRequest
            {
                DeityId = deityId,
                EntityId = Guid.NewGuid(),
                EntityType = EntityType.Character,
                Tier = BlessingTier.Supreme,
                ItemTemplateCode = "supreme_blessing",
                Reason = "Test",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task GrantBlessingAsync_DeityNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, _) = await service.GrantBlessingAsync(
            new GrantBlessingRequest
            {
                DeityId = Guid.NewGuid(),
                EntityId = Guid.NewGuid(),
                EntityType = EntityType.Character,
                Tier = BlessingTier.Minor,
                ItemTemplateCode = "blessing",
                Reason = "Test",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region RevokeBlessing Tests

    [Fact]
    public async Task RevokeBlessingAsync_ActiveBlessing_ReturnsOkAndPublishesRevoked()
    {
        // Map: LOCK, READ -> 404, if Revoked -> 409, revoke, WRITE, PUBLISH revoked
        var service = CreateService();
        var blessingId = Guid.NewGuid();
        var deityId = Guid.NewGuid();

        _mockBlessingStore
            .Setup(s => s.GetAsync($"blessing:{blessingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestBlessingModel(blessingId, deityId, BlessingTier.Minor));

        BlessingModel? savedBlessing = null;
        _mockBlessingStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("blessing:")),
                It.IsAny<BlessingModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BlessingModel, StateOptions?, CancellationToken>((_, m, _, _) => savedBlessing = m)
            .ReturnsAsync("etag-2");

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

        var (status, response) = await service.RevokeBlessingAsync(
            new RevokeBlessingRequest { BlessingId = blessingId, Reason = "Divine displeasure" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(BlessingStatus.Revoked, response.Status);

        Assert.NotNull(savedBlessing);
        Assert.Equal(BlessingStatus.Revoked, savedBlessing.Status);
        Assert.NotNull(savedBlessing.RevokedAt);

        Assert.Equal("divine.blessing.revoked", capturedTopic);
        var typedEvent = Assert.IsType<DivineBlessingRevokedEvent>(capturedEvent);
        Assert.Equal(blessingId, typedEvent.BlessingId);
        Assert.Equal(deityId, typedEvent.DeityId);
    }

    [Fact]
    public async Task RevokeBlessingAsync_AlreadyRevoked_ReturnsConflict()
    {
        var service = CreateService();
        var blessingId = Guid.NewGuid();

        _mockBlessingStore
            .Setup(s => s.GetAsync($"blessing:{blessingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestBlessingModel(blessingId, Guid.NewGuid(), status: BlessingStatus.Revoked));

        var (status, _) = await service.RevokeBlessingAsync(
            new RevokeBlessingRequest { BlessingId = blessingId, Reason = "Test" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task RevokeBlessingAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockBlessingStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlessingModel?)null);

        var (status, _) = await service.RevokeBlessingAsync(
            new RevokeBlessingRequest { BlessingId = Guid.NewGuid(), Reason = "Test" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region GetBlessing Tests

    [Fact]
    public async Task GetBlessingAsync_ExistingBlessing_ReturnsOk()
    {
        var service = CreateService();
        var blessingId = Guid.NewGuid();

        _mockBlessingStore
            .Setup(s => s.GetAsync($"blessing:{blessingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestBlessingModel(blessingId, Guid.NewGuid()));

        var (status, response) = await service.GetBlessingAsync(
            new GetBlessingRequest { BlessingId = blessingId }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(blessingId, response.BlessingId);
    }

    [Fact]
    public async Task GetBlessingAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockBlessingStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlessingModel?)null);

        var (status, _) = await service.GetBlessingAsync(
            new GetBlessingRequest { BlessingId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion
}
