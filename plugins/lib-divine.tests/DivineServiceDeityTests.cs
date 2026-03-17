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
/// Unit tests for DivineService deity management operations.
/// Tests deity CRUD, lifecycle (deprecation, merge, delete), and activation.
/// </summary>
public class DivineServiceDeityTests : ServiceTestBase<DivineServiceConfiguration>
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

    public DivineServiceDeityTests()
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
        SetupDefaultMessageBus();
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

    private void SetupDefaultMessageBus()
    {
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private static DeityModel CreateTestDeityModel(
        Guid deityId,
        Guid gameServiceId,
        string code = "ZEUS",
        string displayName = "Zeus",
        DeityStatus status = DeityStatus.Dormant)
    {
        return new DeityModel
        {
            DeityId = deityId,
            GameServiceId = gameServiceId,
            Code = code,
            DisplayName = displayName,
            Description = "Test deity",
            Domains = new List<DomainInfluenceData>
            {
                new() { Domain = "lightning", Influence = 0.8 }
            },
            Status = status,
            FollowerCount = 0,
            MaxAttentionSlots = 10,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    #region CreateDeity Tests

    [Fact]
    public async Task CreateDeityAsync_ValidRequest_ReturnsOkAndSavesDeity()
    {
        // Map: CALL gameServiceClient -> 404, READ deity-code -> 409 if exists,
        //       CALL seedClient -> seedId, WRITE deity + deity-code, PUBLISH created
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var request = new CreateDeityRequest
        {
            GameServiceId = gameServiceId,
            Code = "ZEUS",
            DisplayName = "Zeus",
            Description = "God of thunder",
            Domains = new[] { new DomainInfluence { Domain = "lightning", Weight = 0.8 } },
            DivineAffectations = new DivineAffectations
            {
                Temperament = "wrathful",
                AttentionBias = "heroes",
                Generosity = 0.6,
                Jealousy = 0.9,
            },
        };

        // Generated clients return response directly, throw ApiException on error
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = gameServiceId });

        _mockDeityStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("deity-code:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        _mockSeedClient
            .Setup(c => c.CreateSeedAsync(
                It.IsAny<CreateSeedRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedResponse { SeedId = Guid.NewGuid() });

        // Capture saved deity model
        string? savedDeityKey = null;
        DeityModel? savedDeityModel = null;
        _mockDeityStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("deity:") && !k.StartsWith("deity-code:")),
                It.IsAny<DeityModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DeityModel, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedDeityKey = k;
                savedDeityModel = m;
            })
            .ReturnsAsync("etag-1");

        // Capture published event
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

        var (status, response) = await service.CreateDeityAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("ZEUS", response.Code);
        Assert.Equal("Zeus", response.DisplayName);
        Assert.Equal(gameServiceId, response.GameServiceId);
        Assert.Equal(DeityStatus.Dormant, response.Status);

        Assert.NotNull(savedDeityModel);
        Assert.Equal("ZEUS", savedDeityModel.Code);
        Assert.Equal(gameServiceId, savedDeityModel.GameServiceId);

        Assert.Equal("divine.deity.created", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<DeityCreatedEvent>(capturedEvent);
        Assert.Equal("ZEUS", typedEvent.Code);
    }

    [Fact]
    public async Task CreateDeityAsync_GameServiceNotFound_ReturnsNotFound()
    {
        // Map: CALL gameServiceClient -> 404 if not found
        var service = CreateService();

        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, "", null, null));

        var (status, response) = await service.CreateDeityAsync(
            new CreateDeityRequest
            {
                GameServiceId = Guid.NewGuid(),
                Code = "ZEUS",
                DisplayName = "Zeus",
                Description = "Test",
                Domains = new[] { new DomainInfluence { Domain = "lightning", Weight = 0.8 } },
                DivineAffectations = new DivineAffectations
                { Temperament = "wrathful", AttentionBias = "heroes", Generosity = 0.6, Jealousy = 0.9 },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateDeityAsync_DuplicateCode_ReturnsConflict()
    {
        // Map: READ deity-code:{gameServiceId}:{code} -> 409 if exists
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();

        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = gameServiceId });

        _mockDeityStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("deity-code:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(Guid.NewGuid(), gameServiceId, "ZEUS"));

        var (status, response) = await service.CreateDeityAsync(
            new CreateDeityRequest
            {
                GameServiceId = gameServiceId,
                Code = "ZEUS",
                DisplayName = "Zeus",
                Description = "Test",
                Domains = new[] { new DomainInfluence { Domain = "lightning", Weight = 0.8 } },
                DivineAffectations = new DivineAffectations
                { Temperament = "wrathful", AttentionBias = "heroes", Generosity = 0.6, Jealousy = 0.9 },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region GetDeity Tests

    [Fact]
    public async Task GetDeityAsync_ExistingDeity_ReturnsOk()
    {
        // Map: READ deity:{deityId} -> 404 if null
        var service = CreateService();
        var deityId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId, Guid.NewGuid()));

        var (status, response) = await service.GetDeityAsync(
            new GetDeityRequest { DeityId = deityId }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(deityId, response.DeityId);
    }

    [Fact]
    public async Task GetDeityAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, response) = await service.GetDeityAsync(
            new GetDeityRequest { DeityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetDeityByCode Tests

    [Fact]
    public async Task GetDeityByCodeAsync_ExistingCode_ReturnsOk()
    {
        // Map: QUERY WHERE gameServiceId AND code -> 404 if empty
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var deityId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.JsonQueryAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JsonQueryResult<DeityModel>>
            {
                new(
                    $"deity:{deityId}",
                    CreateTestDeityModel(deityId, gameServiceId, "ZEUS")
                ),
            });

        var (status, response) = await service.GetDeityByCodeAsync(
            new GetDeityByCodeRequest { GameServiceId = gameServiceId, Code = "ZEUS" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("ZEUS", response.Code);
    }

    [Fact]
    public async Task GetDeityByCodeAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.JsonQueryAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JsonQueryResult<DeityModel>>());

        var (status, _) = await service.GetDeityByCodeAsync(
            new GetDeityByCodeRequest { GameServiceId = Guid.NewGuid(), Code = "NONEXISTENT" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region UpdateDeity Tests

    [Fact]
    public async Task UpdateDeityAsync_ValidUpdate_ReturnsOkAndPublishesEvent()
    {
        // Map: LOCK, READ with ETag -> 404, apply non-null, ETAG-WRITE, PUBLISH updated
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var model = CreateTestDeityModel(deityId, Guid.NewGuid());

        _mockDeityStore
            .Setup(s => s.GetWithETagAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

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

        var (status, response) = await service.UpdateDeityAsync(
            new UpdateDeityRequest { DeityId = deityId, DisplayName = "Updated Zeus" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("divine.deity.updated", capturedTopic);
        var typedEvent = Assert.IsType<DeityUpdatedEvent>(capturedEvent);
        Assert.Equal(deityId, typedEvent.DeityId);
        Assert.Contains("displayName", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task UpdateDeityAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((DeityModel?)null, (string?)null));

        var (status, _) = await service.UpdateDeityAsync(
            new UpdateDeityRequest { DeityId = Guid.NewGuid(), DisplayName = "Updated" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ActivateDeity Tests

    [Fact]
    public async Task ActivateDeityAsync_DormantDeity_ReturnsOkAndPublishesActivated()
    {
        // Map: LOCK, READ -> 404, set Active, WRITE, PUBLISH activated
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId, gameServiceId, status: DeityStatus.Dormant));

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

        var (status, response) = await service.ActivateDeityAsync(
            new ActivateDeityRequest { DeityId = deityId }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(DeityStatus.Active, response.Status);
        Assert.Equal("divine.deity.activated", capturedTopic);
        var typedEvent = Assert.IsType<DivineDeityActivatedEvent>(capturedEvent);
        Assert.Equal(deityId, typedEvent.DeityId);
    }

    [Fact]
    public async Task ActivateDeityAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, _) = await service.ActivateDeityAsync(
            new ActivateDeityRequest { DeityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region DeactivateDeity Tests

    [Fact]
    public async Task DeactivateDeityAsync_ActiveDeity_ReturnsOkAndPublishesDormant()
    {
        // Map: LOCK, READ -> 404, stop Puppetmaster, set Dormant, clear attention, WRITE, PUBLISH
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();

        _mockDeityStore
            .Setup(s => s.GetAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDeityModel(deityId, gameServiceId, status: DeityStatus.Active));

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

        var (status, response) = await service.DeactivateDeityAsync(
            new DeactivateDeityRequest { DeityId = deityId }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(DeityStatus.Dormant, response.Status);
        Assert.Equal("divine.deity.dormant", capturedTopic);
    }

    [Fact]
    public async Task DeactivateDeityAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, _) = await service.DeactivateDeityAsync(
            new DeactivateDeityRequest { DeityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region DeprecateDeity Tests

    [Fact]
    public async Task DeprecateDeityAsync_NotDeprecated_ReturnsOkAndPublishesUpdated()
    {
        // Map: LOCK, READ+ETag -> 404, if deprecated -> 200 idempotent,
        //       set deprecation fields, ETAG-WRITE, PUBLISH updated
        var service = CreateService();
        var deityId = Guid.NewGuid();
        var model = CreateTestDeityModel(deityId, Guid.NewGuid());

        _mockDeityStore
            .Setup(s => s.GetWithETagAsync($"deity:{deityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

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

        var (status, response) = await service.DeprecateDeityAsync(
            new DeprecateDeityRequest { DeityId = deityId, Reason = "Replaced by Athena" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
        Assert.Equal("divine.deity.updated", capturedTopic);
        var typedEvent = Assert.IsType<DeityUpdatedEvent>(capturedEvent);
        Assert.Contains("isDeprecated", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task DeprecateDeityAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((DeityModel?)null, (string?)null));

        var (status, _) = await service.DeprecateDeityAsync(
            new DeprecateDeityRequest { DeityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region UndeprecateDeity Tests

    [Fact]
    public async Task UndeprecateDeityAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((DeityModel?)null, (string?)null));

        var (status, _) = await service.UndeprecateDeityAsync(
            new UndeprecateDeityRequest { DeityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region DeleteDeity Tests

    [Fact]
    public async Task DeleteDeityAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var status = await service.DeleteDeityAsync(
            new DeleteDeityRequest { DeityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region MergeDeity Tests

    [Fact]
    public async Task MergeDeityAsync_SourceNotFound_ReturnsNotFound()
    {
        // Map: READ source -> 404 if null
        var service = CreateService();

        _mockDeityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeityModel?)null);

        var (status, _) = await service.MergeDeityAsync(
            new MergeDeprecatedRequest { SourceEntityId = Guid.NewGuid(), TargetEntityId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion
}
