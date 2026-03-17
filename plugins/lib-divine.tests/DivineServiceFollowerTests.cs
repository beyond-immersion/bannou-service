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
/// Unit tests for DivineService follower and cleanup operations.
/// </summary>
public class DivineServiceFollowerTests : ServiceTestBase<DivineServiceConfiguration>
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

    public DivineServiceFollowerTests()
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

    private static DeityModel CreateTestDeityModel(
        Guid deityId, int followerCount = 3, int maxAttentionSlots = 10)
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
            FollowerCount = followerCount,
            MaxAttentionSlots = maxAttentionSlots,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    #region RegisterFollower Tests

    [Fact]
    public async Task RegisterFollowerAsync_ValidRequest_ReturnsOkAndPublishesRegistered()
    {
        // Map: READ deity -> 404, CALL characterClient -> 404,
        //       CALL relationshipClient.CreateRelationshipAsync, allocate attention,
        //       WRITE deity (FollowerCount+1), PUBLISH follower.registered
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestDeityModel(deityId, followerCount: 2, maxAttentionSlots: 10);

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(
                It.Is<GetCharacterRequest>(r => r.CharacterId == characterId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = characterId });

        _mockRelationshipClient
            .Setup(c => c.CreateRelationshipAsync(
                It.IsAny<CreateRelationshipRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipResponse { RelationshipId = relationshipId });

        AttentionSlotModel? savedAttention = null;
        _mockAttentionStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("attention:")),
                It.IsAny<AttentionSlotModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, AttentionSlotModel, StateOptions?, CancellationToken>((_, m, _, _) => savedAttention = m)
            .ReturnsAsync("etag-1");

        DeityModel? savedDeity = null;
        _mockDeityStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("deity:")),
                It.IsAny<DeityModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DeityModel, StateOptions?, CancellationToken>((_, m, _, _) => savedDeity = m)
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

        var (status, response) = await service.RegisterFollowerAsync(
            new RegisterFollowerRequest { DeityId = deityId, CharacterId = characterId },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(deityId, response.DeityId);
        Assert.Equal(relationshipId, response.RelationshipId);
        Assert.True(response.AttentionSlotAllocated);

        Assert.NotNull(savedDeity);
        Assert.Equal(3, savedDeity.FollowerCount);

        Assert.NotNull(savedAttention);
        Assert.Equal(deityId, savedAttention.DeityId);
        Assert.Equal(characterId, savedAttention.CharacterId);

        Assert.Equal("divine.follower.registered", capturedTopic);
        var typedEvent = Assert.IsType<DivineFollowerRegisteredEvent>(capturedEvent);
        Assert.Equal(deityId, typedEvent.DeityId);
        Assert.Equal(characterId, typedEvent.CharacterId);
    }

    [Fact]
    public async Task RegisterFollowerAsync_DeityNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, _) = await service.RegisterFollowerAsync(
            new RegisterFollowerRequest { DeityId = Guid.NewGuid(), CharacterId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task RegisterFollowerAsync_CharacterNotFound_ReturnsNotFound()
    {
        // Map: CALL characterClient -> 404 (throws ApiException)
        var service = CreateService();
        var deityId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId));

        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(
                It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, "", null, null));

        var (status, _) = await service.RegisterFollowerAsync(
            new RegisterFollowerRequest { DeityId = deityId, CharacterId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region UnregisterFollower Tests

    [Fact]
    public async Task UnregisterFollowerAsync_ValidRequest_ReturnsOkAndPublishesRemoved()
    {
        // Map: READ deity -> 404, CALL EndRelationshipAsync, DELETE attention,
        //       WRITE deity (FollowerCount-1), PUBLISH follower.removed
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var model = CreateTestDeityModel(deityId, followerCount: 5);

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockRelationshipClient
            .Setup(c => c.EndRelationshipAsync(
                It.IsAny<EndRelationshipRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockAttentionStore
            .Setup(s => s.DeleteAsync(
                It.Is<string>(k => k == $"attention:{deityId}:{characterId}"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        DeityModel? savedDeity = null;
        _mockDeityStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<DeityModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DeityModel, StateOptions?, CancellationToken>((_, m, _, _) => savedDeity = m)
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

        var status = await service.UnregisterFollowerAsync(
            new UnregisterFollowerRequest { DeityId = deityId, CharacterId = characterId },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);

        Assert.NotNull(savedDeity);
        Assert.Equal(4, savedDeity.FollowerCount);

        Assert.Equal("divine.follower.removed", capturedTopic);
        var typedEvent = Assert.IsType<DivineFollowerRemovedEvent>(capturedEvent);
        Assert.Equal(deityId, typedEvent.DeityId);
        Assert.Equal(characterId, typedEvent.CharacterId);
    }

    [Fact]
    public async Task UnregisterFollowerAsync_DeityNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var status = await service.UnregisterFollowerAsync(
            new UnregisterFollowerRequest { DeityId = Guid.NewGuid(), CharacterId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region GetFollowers Tests

    [Fact]
    public async Task GetFollowersAsync_ValidRequest_ReturnsFollowerList()
    {
        // Map: CALL relationshipClient.ListRelationshipsByEntityAsync
        var service = CreateService();

        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByEntityAsync(
                It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>(),
                TotalCount = 0,
            });

        var (status, response) = await service.GetFollowersAsync(
            new GetFollowersRequest { DeityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    #endregion

    #region CleanupByCharacter Tests

    [Fact]
    public async Task CleanupByCharacterAsync_ReturnsOk()
    {
        // Map: QUERY blessings, FOREACH revoke, remove followers, RETURN 200
        var service = CreateService();
        var characterId = Guid.NewGuid();

        _mockBlessingStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<BlessingModel>(
                new List<JsonQueryResult<BlessingModel>>(), 0, 0, 100));

        var status = await service.CleanupByCharacterAsync(
            new CleanupByCharacterRequest { CharacterId = characterId },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
    }

    #endregion

    #region CleanupByGameService Tests

    [Fact]
    public async Task CleanupByGameServiceAsync_ReturnsOk()
    {
        // Map: QUERY deities by gameServiceId, FOREACH delete
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();

        var mockLock = new Mock<ILockResponse>();
        mockLock.Setup(l => l.Success).Returns(true);
        mockLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLock.Object);

        _mockDeityStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<DeityModel>(
                new List<JsonQueryResult<DeityModel>>(), 0, 0, 100));

        var status = await service.CleanupByGameServiceAsync(
            new CleanupByGameServiceRequest { GameServiceId = gameServiceId },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
    }

    #endregion
}
