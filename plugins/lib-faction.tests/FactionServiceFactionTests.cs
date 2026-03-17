using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Faction;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Faction.Tests;

public class FactionServiceFactionTests : ServiceTestBase<FactionServiceConfiguration>
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ILogger<FactionService>> _mockLogger;
    private readonly Mock<ISeedClient> _mockSeedClient;
    private readonly Mock<ILocationClient> _mockLocationClient;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    private readonly Mock<IStateStore<FactionModel>> _mockFactionStore;
    private readonly Mock<IJsonQueryableStateStore<FactionModel>> _mockFactionQueryStore;
    private readonly Mock<IStateStore<FactionMemberModel>> _mockMemberStore;
    private readonly Mock<IJsonQueryableStateStore<FactionMemberModel>> _mockMemberQueryStore;
    private readonly Mock<IStateStore<MembershipListModel>> _mockMemberListStore;
    private readonly Mock<IStateStore<TerritoryClaimModel>> _mockTerritoryStore;
    private readonly Mock<IJsonQueryableStateStore<TerritoryClaimModel>> _mockTerritoryQueryStore;
    private readonly Mock<IStateStore<TerritoryClaimListModel>> _mockTerritoryListStore;
    private readonly Mock<IStateStore<NormDefinitionModel>> _mockNormStore;
    private readonly Mock<IStateStore<NormListModel>> _mockNormListStore;
    private readonly Mock<IStateStore<ResolvedNormCacheModel>> _mockNormCacheStore;
    private readonly Mock<IStateStore<GovernanceEntryModel>> _mockGovernanceStore;
    private readonly Mock<IStateStore<GovernanceEntryListModel>> _mockGovernanceListStore;
    private readonly Mock<IStateStore<CachedGovernanceResolution>> _mockGovernanceCacheStore;

    public FactionServiceFactionTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockLogger = new Mock<ILogger<FactionService>>();
        _mockSeedClient = new Mock<ISeedClient>();
        _mockLocationClient = new Mock<ILocationClient>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockFactionStore = new Mock<IStateStore<FactionModel>>();
        _mockFactionQueryStore = new Mock<IJsonQueryableStateStore<FactionModel>>();
        _mockMemberStore = new Mock<IStateStore<FactionMemberModel>>();
        _mockMemberQueryStore = new Mock<IJsonQueryableStateStore<FactionMemberModel>>();
        _mockMemberListStore = new Mock<IStateStore<MembershipListModel>>();
        _mockTerritoryStore = new Mock<IStateStore<TerritoryClaimModel>>();
        _mockTerritoryQueryStore = new Mock<IJsonQueryableStateStore<TerritoryClaimModel>>();
        _mockTerritoryListStore = new Mock<IStateStore<TerritoryClaimListModel>>();
        _mockNormStore = new Mock<IStateStore<NormDefinitionModel>>();
        _mockNormListStore = new Mock<IStateStore<NormListModel>>();
        _mockNormCacheStore = new Mock<IStateStore<ResolvedNormCacheModel>>();
        _mockGovernanceStore = new Mock<IStateStore<GovernanceEntryModel>>();
        _mockGovernanceListStore = new Mock<IStateStore<GovernanceEntryListModel>>();
        _mockGovernanceCacheStore = new Mock<IStateStore<CachedGovernanceResolution>>();

        // Setup state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<FactionModel>(StateStoreDefinitions.Faction)).Returns(_mockFactionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<FactionModel>(StateStoreDefinitions.Faction)).Returns(_mockFactionQueryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<FactionMemberModel>(StateStoreDefinitions.FactionMembership)).Returns(_mockMemberStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<FactionMemberModel>(StateStoreDefinitions.FactionMembership)).Returns(_mockMemberQueryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MembershipListModel>(StateStoreDefinitions.FactionMembership)).Returns(_mockMemberListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<TerritoryClaimModel>(StateStoreDefinitions.FactionTerritory)).Returns(_mockTerritoryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<TerritoryClaimModel>(StateStoreDefinitions.FactionTerritory)).Returns(_mockTerritoryQueryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<TerritoryClaimListModel>(StateStoreDefinitions.FactionTerritory)).Returns(_mockTerritoryListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<NormDefinitionModel>(StateStoreDefinitions.FactionNorm)).Returns(_mockNormStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<NormListModel>(StateStoreDefinitions.FactionNorm)).Returns(_mockNormListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<ResolvedNormCacheModel>(StateStoreDefinitions.FactionCache)).Returns(_mockNormCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CachedGovernanceResolution>(StateStoreDefinitions.FactionCache)).Returns(_mockGovernanceCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GovernanceEntryModel>(StateStoreDefinitions.FactionGovernance)).Returns(_mockGovernanceStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GovernanceEntryListModel>(StateStoreDefinitions.FactionGovernance)).Returns(_mockGovernanceListStore.Object);

        // Default message bus setup — capture pattern configured per test
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup lock provider to always succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private FactionService CreateService()
    {
        return new FactionService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockResourceClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockSeedClient.Object,
            _mockLocationClient.Object,
            _mockRealmClient.Object,
            _mockGameServiceClient.Object,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);
    }

    private static FactionModel CreateTestFactionModel(
        Guid factionId,
        Guid? gameServiceId = null,
        string code = "TEST_FACTION",
        FactionStatus status = FactionStatus.Active,
        bool isDeprecated = false,
        bool isRealmBaseline = false)
    {
        return new FactionModel
        {
            FactionId = factionId,
            GameServiceId = gameServiceId ?? Guid.NewGuid(),
            Name = "Test Faction",
            Code = code,
            RealmId = Guid.NewGuid(),
            Status = status,
            AuthorityLevel = AuthorityLevel.Influence,
            IsRealmBaseline = isRealmBaseline,
            IsDeprecated = isDeprecated,
            DeprecatedAt = isDeprecated ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            DeprecationReason = isDeprecated ? "Test deprecation" : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    // ========================================================================
    // CreateFaction
    // ========================================================================

    [Fact]
    public async Task CreateFactionAsync_ValidRequest_SavesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var seedId = Guid.NewGuid();

        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceResponse { ServiceId = gameServiceId });
        _mockRealmClient
            .Setup(c => c.RealmExistsAsync(It.IsAny<RealmExistsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true });
        _mockFactionStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("fac:" + gameServiceId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);
        _mockSeedClient
            .Setup(c => c.CreateSeedAsync(It.IsAny<CreateSeedRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateSeedResponse { SeedId = seedId });

        // Capture state saves
        var savedModels = new List<(string Key, FactionModel Model)>();
        _mockFactionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, FactionModel, StateOptions?, CancellationToken>((key, model, _, _) =>
                savedModels.Add((key, model)))
            .Returns(Task.CompletedTask);

        // Capture published events
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new CreateFactionRequest
        {
            GameServiceId = gameServiceId,
            Name = "Warriors Guild",
            Code = "WARRIORS",
            RealmId = realmId,
        };

        // Act
        var (status, response) = await service.CreateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Warriors Guild", response.Name);
        Assert.Equal("WARRIORS", response.Code);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal(seedId, response.SeedId);
        Assert.Equal(FactionStatus.Active, response.Status);

        // Verify saves: one for ID key, one for code key
        Assert.Equal(2, savedModels.Count);
        Assert.Equal("Warriors Guild", savedModels[0].Model.Name);

        // Verify event published
        Assert.NotNull(capturedTopic);
        Assert.Contains("faction.created", capturedTopic);
        var createdEvent = Assert.IsType<FactionCreatedEvent>(capturedEvent);
        Assert.Equal("Warriors Guild", createdEvent.Name);
        Assert.Equal("WARRIORS", createdEvent.Code);
    }

    [Fact]
    public async Task CreateFactionAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceResponse { ServiceId = gameServiceId });
        _mockRealmClient
            .Setup(c => c.RealmExistsAsync(It.IsAny<RealmExistsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true });

        var existingFaction = CreateTestFactionModel(Guid.NewGuid(), gameServiceId, "WARRIORS");
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionCodeKey(gameServiceId, "WARRIORS"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFaction);

        var request = new CreateFactionRequest
        {
            GameServiceId = gameServiceId,
            Name = "Warriors Guild",
            Code = "WARRIORS",
            RealmId = realmId,
        };

        // Act
        var (status, response) = await service.CreateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateFactionAsync_ParentNotFound_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var parentFactionId = Guid.NewGuid();

        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceResponse { ServiceId = gameServiceId });
        _mockRealmClient
            .Setup(c => c.RealmExistsAsync(It.IsAny<RealmExistsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true });
        _mockFactionStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("fac:" + gameServiceId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(parentFactionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        var request = new CreateFactionRequest
        {
            GameServiceId = gameServiceId,
            Name = "Sub-Guild",
            Code = "SUB_GUILD",
            RealmId = Guid.NewGuid(),
            ParentFactionId = parentFactionId,
        };

        // Act
        var (status, response) = await service.CreateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    // ========================================================================
    // GetFaction
    // ========================================================================

    [Fact]
    public async Task GetFactionAsync_ExistingFaction_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetFactionAsync(
            new GetFactionRequest { FactionId = factionId }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(factionId, response.FactionId);
    }

    [Fact]
    public async Task GetFactionAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        // Act
        var (status, response) = await service.GetFactionAsync(
            new GetFactionRequest { FactionId = factionId }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    // ========================================================================
    // GetFactionByCode
    // ========================================================================

    [Fact]
    public async Task GetFactionByCodeAsync_ExistingCode_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var model = CreateTestFactionModel(Guid.NewGuid(), gameServiceId, "GUILD_A");

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionCodeKey(gameServiceId, "GUILD_A"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetFactionByCodeAsync(
            new GetFactionByCodeRequest { GameServiceId = gameServiceId, Code = "GUILD_A" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("GUILD_A", response.Code);
    }

    [Fact]
    public async Task GetFactionByCodeAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionCodeKey(gameServiceId, "UNKNOWN"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        // Act
        var (status, response) = await service.GetFactionByCodeAsync(
            new GetFactionByCodeRequest { GameServiceId = gameServiceId, Code = "UNKNOWN" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    // ========================================================================
    // UpdateFaction
    // ========================================================================

    [Fact]
    public async Task UpdateFactionAsync_NameChange_SavesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Capture state saves
        var savedModels = new List<(string Key, FactionModel Model)>();
        _mockFactionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, FactionModel, StateOptions?, CancellationToken>((key, m, _, _) =>
                savedModels.Add((key, m)))
            .Returns(Task.CompletedTask);

        // Capture published event
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((_, evt, _) => capturedEvent = evt)
            .ReturnsAsync(true);

        var request = new UpdateFactionRequest
        {
            FactionId = factionId,
            Name = "Renamed Guild",
        };

        // Act
        var (status, response) = await service.UpdateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Renamed Guild", response.Name);

        Assert.True(savedModels.Count >= 2);
        Assert.Equal("Renamed Guild", savedModels[0].Model.Name);

        var updatedEvent = Assert.IsType<FactionUpdatedEvent>(capturedEvent);
        Assert.Contains("name", updatedEvent.ChangedFields);
    }

    [Fact]
    public async Task UpdateFactionAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        // Act
        var (status, response) = await service.UpdateFactionAsync(
            new UpdateFactionRequest { FactionId = factionId, Name = "New" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateFactionAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId, gameServiceId, "ORIGINAL");

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var conflicting = CreateTestFactionModel(Guid.NewGuid(), gameServiceId, "TAKEN");
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionCodeKey(gameServiceId, "TAKEN"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(conflicting);

        var request = new UpdateFactionRequest
        {
            FactionId = factionId,
            Code = "TAKEN",
        };

        // Act
        var (status, response) = await service.UpdateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    // ========================================================================
    // DeleteFaction
    // ========================================================================

    [Fact]
    public async Task DeleteFactionAsync_DeprecatedFaction_DeletesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId, isDeprecated: true);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Empty cascades — no members, territories, norms, governance
        _mockMemberQueryStore
            .Setup(s => s.JsonQueryPagedAsync(It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedQueryResult<FactionMemberModel> { Items = new List<KeyValuePair<string, FactionMemberModel>>(), HasMore = false });
        _mockTerritoryListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TerritoryClaimListModel?)null);
        _mockNormListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NormListModel?)null);
        _mockGovernanceListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GovernanceEntryListModel?)null);

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        var status = await service.DeleteFactionAsync(
            new DeleteFactionRequest { FactionId = factionId }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        Assert.NotNull(capturedTopic);
        Assert.Contains("faction.deleted", capturedTopic);
        var deletedEvent = Assert.IsType<FactionDeletedEvent>(capturedEvent);
        Assert.Equal(factionId, deletedEvent.FactionId);
    }

    [Fact]
    public async Task DeleteFactionAsync_NotDeprecated_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId, isDeprecated: false);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var status = await service.DeleteFactionAsync(
            new DeleteFactionRequest { FactionId = factionId }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task DeleteFactionAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        // Act
        var status = await service.DeleteFactionAsync(
            new DeleteFactionRequest { FactionId = factionId }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    // ========================================================================
    // DesignateRealmBaseline
    // ========================================================================

    [Fact]
    public async Task DesignateRealmBaselineAsync_ValidFaction_SetsBaselineAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // No previous baseline
        _mockFactionQueryStore
            .Setup(s => s.JsonQueryPagedAsync(It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedQueryResult<FactionModel>
            {
                Items = new List<KeyValuePair<string, FactionModel>>(),
                HasMore = false,
            });

        // For norm cache invalidation — empty member query
        _mockMemberQueryStore
            .Setup(s => s.JsonQueryPagedAsync(It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedQueryResult<FactionMemberModel> { Items = new List<KeyValuePair<string, FactionMemberModel>>(), HasMore = false });

        // Capture state saves
        var savedModels = new List<FactionModel>();
        _mockFactionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, FactionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModels.Add(m))
            .Returns(Task.CompletedTask);

        // Capture published events
        var capturedEvents = new List<(string Topic, object Event)>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) => capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.DesignateRealmBaselineAsync(
            new DesignateRealmBaselineRequest { FactionId = factionId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsRealmBaseline);
        Assert.Equal(AuthorityLevel.Sovereign, response.AuthorityLevel);

        // Verify baseline designated event was published
        var baselineEvent = capturedEvents.FirstOrDefault(e => e.Event is FactionRealmBaselineDesignatedEvent);
        Assert.NotNull(baselineEvent.Event);
        var designatedEvent = Assert.IsType<FactionRealmBaselineDesignatedEvent>(baselineEvent.Event);
        Assert.Equal(factionId, designatedEvent.FactionId);
    }

    [Fact]
    public async Task DesignateRealmBaselineAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        // Act
        var (status, response) = await service.DesignateRealmBaselineAsync(
            new DesignateRealmBaselineRequest { FactionId = factionId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    // ========================================================================
    // GetRealmBaseline
    // ========================================================================

    [Fact]
    public async Task GetRealmBaselineAsync_BaselineExists_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var baselineModel = CreateTestFactionModel(Guid.NewGuid(), isRealmBaseline: true);
        baselineModel.RealmId = realmId;

        _mockFactionQueryStore
            .Setup(s => s.JsonQueryPagedAsync(It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedQueryResult<FactionModel>
            {
                Items = new List<KeyValuePair<string, FactionModel>>
                {
                    new("key", baselineModel),
                },
                HasMore = false,
            });

        // Act
        var (status, response) = await service.GetRealmBaselineAsync(
            new GetRealmBaselineRequest { RealmId = realmId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsRealmBaseline);
    }

    [Fact]
    public async Task GetRealmBaselineAsync_NoBaseline_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        _mockFactionQueryStore
            .Setup(s => s.JsonQueryPagedAsync(It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedQueryResult<FactionModel>
            {
                Items = new List<KeyValuePair<string, FactionModel>>(),
                HasMore = false,
            });

        // Act
        var (status, response) = await service.GetRealmBaselineAsync(
            new GetRealmBaselineRequest { RealmId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    // ========================================================================
    // SeedFactions
    // ========================================================================

    [Fact]
    public async Task SeedFactionsAsync_NewFactions_CreatesAndReportsCount()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var seedId = Guid.NewGuid();

        // No existing factions (code lookup returns null)
        _mockFactionStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("fac:" + gameServiceId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        _mockSeedClient
            .Setup(c => c.CreateSeedAsync(It.IsAny<CreateSeedRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateSeedResponse { SeedId = seedId });

        // Capture saved models
        var savedModels = new List<FactionModel>();
        _mockFactionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, FactionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModels.Add(m))
            .Returns(Task.CompletedTask);

        var request = new SeedFactionsRequest
        {
            GameServiceId = gameServiceId,
            Factions = new List<SeedFactionDefinition>
            {
                new SeedFactionDefinition { Code = "WARRIORS", Name = "Warriors", RealmId = realmId },
                new SeedFactionDefinition { Code = "MAGES", Name = "Mages", RealmId = realmId },
            },
        };

        // Act
        var (status, response) = await service.SeedFactionsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Created);
        Assert.Equal(0, response.Skipped);
        Assert.Equal(0, response.Failed);

        // Each faction writes twice (ID key + code key)
        Assert.Equal(4, savedModels.Count);
    }
}
