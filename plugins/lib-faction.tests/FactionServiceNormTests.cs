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

public class FactionServiceNormTests : ServiceTestBase<FactionServiceConfiguration>
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

    public FactionServiceNormTests()
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
        _mockStateStoreFactory.Setup(f => f.GetStore<GovernanceEntryModel>(StateStoreDefinitions.FactionGovernance)).Returns(_mockGovernanceStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GovernanceEntryListModel>(StateStoreDefinitions.FactionGovernance)).Returns(_mockGovernanceListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CachedGovernanceResolution>(StateStoreDefinitions.FactionCache)).Returns(_mockGovernanceCacheStore.Object);

        // Default message bus setup
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

    private static FactionModel CreateTestFactionModel(Guid factionId, FactionStatus status = FactionStatus.Active)
    {
        return new FactionModel
        {
            FactionId = factionId,
            GameServiceId = Guid.NewGuid(),
            Name = "Test Faction",
            Code = "TEST_FACTION",
            RealmId = Guid.NewGuid(),
            SeedId = Guid.NewGuid(),
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    // ========================================================================
    // DefineNormAsync
    // ========================================================================

    [Fact]
    public async Task DefineNormAsync_ValidRequest_SavesNormAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        _mockSeedClient
            .Setup(s => s.GetCapabilityManifestAsync(It.IsAny<GetCapabilityManifestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityManifestResponse
            {
                SeedId = faction.SeedId!.Value,
                SeedTypeCode = "faction",
                ComputedAt = DateTimeOffset.UtcNow,
                Capabilities = new List<Capability>
                {
                    new Capability { CapabilityCode = "norm.define", Domain = "governance", Fidelity = 1.0f }
                }
            });

        _mockNormListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionNormsKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NormListModel?)null);

        // Capture saved norm
        NormDefinitionModel? capturedNorm = null;
        _mockNormStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<NormDefinitionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, NormDefinitionModel, StateOptions?, CancellationToken>((_, norm, _, _) =>
            {
                capturedNorm = norm;
            });

        // Capture event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                if (topic.Contains("norm.defined"))
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                }
            })
            .ReturnsAsync(true);

        var request = new DefineNormRequest
        {
            FactionId = factionId,
            ViolationType = "theft",
            BasePenalty = 10.0f,
            Severity = NormSeverity.Standard,
            Scope = NormScope.Internal,
            Description = "Do not steal"
        };

        // Act
        var (status, response) = await service.DefineNormAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(factionId, response.FactionId);
        Assert.Equal("theft", response.ViolationType);
        Assert.Equal(10.0f, response.BasePenalty);
        Assert.Equal(NormSeverity.Standard, response.Severity);
        Assert.Equal(NormScope.Internal, response.Scope);
        Assert.Equal("Do not steal", response.Description);

        Assert.NotNull(capturedNorm);
        Assert.Equal(factionId, capturedNorm.FactionId);
        Assert.Equal("theft", capturedNorm.ViolationType);
        Assert.Equal(10.0f, capturedNorm.BasePenalty);

        Assert.NotNull(capturedTopic);
        var definedEvent = Assert.IsType<FactionNormDefinedEvent>(capturedEvent);
        Assert.Equal(factionId, definedEvent.FactionId);
        Assert.Equal("theft", definedEvent.ViolationType);
        Assert.Equal(10.0f, definedEvent.BasePenalty);
        Assert.Equal(NormSeverity.Standard, definedEvent.Severity);
        Assert.Equal(NormScope.Internal, definedEvent.Scope);
    }

    [Fact]
    public async Task DefineNormAsync_FactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        var request = new DefineNormRequest
        {
            FactionId = factionId,
            ViolationType = "theft",
            BasePenalty = 5.0f,
            Severity = NormSeverity.Advisory,
            Scope = NormScope.Internal,
        };

        // Act
        var (status, response) = await service.DefineNormAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DefineNormAsync_DeprecatedFaction_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId);
        faction.IsDeprecated = true;

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        var request = new DefineNormRequest
        {
            FactionId = factionId,
            ViolationType = "deception",
            BasePenalty = 8.0f,
            Severity = NormSeverity.Strict,
            Scope = NormScope.External,
        };

        // Act
        var (status, response) = await service.DefineNormAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    // ========================================================================
    // UpdateNormAsync
    // ========================================================================

    [Fact]
    public async Task UpdateNormAsync_ValidUpdate_SavesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var normId = Guid.NewGuid();
        var factionId = Guid.NewGuid();

        var norm = new NormDefinitionModel
        {
            NormId = normId,
            FactionId = factionId,
            ViolationType = "theft",
            BasePenalty = 5.0f,
            Severity = NormSeverity.Advisory,
            Scope = NormScope.Internal,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        _mockNormStore
            .Setup(s => s.GetAsync(FactionService.BuildNormKey(normId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(norm);

        // Capture saved norm
        NormDefinitionModel? capturedNorm = null;
        _mockNormStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<NormDefinitionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, NormDefinitionModel, StateOptions?, CancellationToken>((_, saved, _, _) =>
            {
                capturedNorm = saved;
            });

        // Capture event
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                if (topic.Contains("norm.updated"))
                    capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new UpdateNormRequest
        {
            NormId = normId,
            BasePenalty = 15.0f,
            Severity = NormSeverity.Strict,
        };

        // Act
        var (status, response) = await service.UpdateNormAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(15.0f, response.BasePenalty);
        Assert.Equal(NormSeverity.Strict, response.Severity);

        Assert.NotNull(capturedNorm);
        Assert.Equal(15.0f, capturedNorm.BasePenalty);
        Assert.Equal(NormSeverity.Strict, capturedNorm.Severity);
        Assert.NotNull(capturedNorm.UpdatedAt);

        var updatedEvent = Assert.IsType<FactionNormUpdatedEvent>(capturedEvent);
        Assert.Equal(factionId, updatedEvent.FactionId);
        Assert.Equal(normId, updatedEvent.NormId);
        Assert.Equal(15.0f, updatedEvent.BasePenalty);
        Assert.Equal(NormSeverity.Strict, updatedEvent.Severity);
    }

    [Fact]
    public async Task UpdateNormAsync_NormNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var normId = Guid.NewGuid();

        _mockNormStore
            .Setup(s => s.GetAsync(FactionService.BuildNormKey(normId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NormDefinitionModel?)null);

        var request = new UpdateNormRequest { NormId = normId, BasePenalty = 20.0f };

        // Act
        var (status, response) = await service.UpdateNormAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    // ========================================================================
    // DeleteNormAsync
    // ========================================================================

    [Fact]
    public async Task DeleteNormAsync_ExistingNorm_DeletesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var normId = Guid.NewGuid();
        var factionId = Guid.NewGuid();

        var norm = new NormDefinitionModel
        {
            NormId = normId,
            FactionId = factionId,
            ViolationType = "smuggling",
            BasePenalty = 12.0f,
            Severity = NormSeverity.Strict,
            Scope = NormScope.External,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _mockNormStore
            .Setup(s => s.GetAsync(FactionService.BuildNormKey(normId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(norm);

        _mockNormListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionNormsKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormListModel { FactionId = factionId, NormIds = new List<Guid> { normId } });

        // Capture event
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                if (topic.Contains("norm.deleted"))
                    capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new DeleteNormRequest { NormId = normId };

        // Act
        var status = await service.DeleteNormAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        var deletedEvent = Assert.IsType<FactionNormDeletedEvent>(capturedEvent);
        Assert.Equal(factionId, deletedEvent.FactionId);
        Assert.Equal(normId, deletedEvent.NormId);
        Assert.Equal("smuggling", deletedEvent.ViolationType);

        _mockNormStore.Verify(
            s => s.DeleteAsync(FactionService.BuildNormKey(normId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteNormAsync_NormNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var normId = Guid.NewGuid();

        _mockNormStore
            .Setup(s => s.GetAsync(FactionService.BuildNormKey(normId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NormDefinitionModel?)null);

        var request = new DeleteNormRequest { NormId = normId };

        // Act
        var status = await service.DeleteNormAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    // ========================================================================
    // ListNormsAsync
    // ========================================================================

    [Fact]
    public async Task ListNormsAsync_HasNorms_ReturnsMatchingNorms()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var normId1 = Guid.NewGuid();
        var normId2 = Guid.NewGuid();

        _mockNormListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionNormsKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormListModel { FactionId = factionId, NormIds = new List<Guid> { normId1, normId2 } });

        _mockNormStore
            .Setup(s => s.GetAsync(FactionService.BuildNormKey(normId1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormDefinitionModel
            {
                NormId = normId1,
                FactionId = factionId,
                ViolationType = "theft",
                BasePenalty = 5.0f,
                Severity = NormSeverity.Standard,
                Scope = NormScope.Internal,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        _mockNormStore
            .Setup(s => s.GetAsync(FactionService.BuildNormKey(normId2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormDefinitionModel
            {
                NormId = normId2,
                FactionId = factionId,
                ViolationType = "assault",
                BasePenalty = 20.0f,
                Severity = NormSeverity.Strict,
                Scope = NormScope.External,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var request = new ListNormsRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.ListNormsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(factionId, response.FactionId);
        Assert.Equal(2, response.Norms.Count);
    }

    [Fact]
    public async Task ListNormsAsync_NoNorms_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockNormListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionNormsKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NormListModel?)null);

        var request = new ListNormsRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.ListNormsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(factionId, response.FactionId);
        Assert.Empty(response.Norms);
    }

    // ========================================================================
    // QueryApplicableNormsAsync
    // ========================================================================

    [Fact]
    public async Task QueryApplicableNormsAsync_CacheHit_ReturnsCachedData()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var normFactionId = Guid.NewGuid();
        var normId = Guid.NewGuid();

        var cachedModel = new ResolvedNormCacheModel
        {
            CharacterId = characterId,
            LocationId = locationId,
            ApplicableNorms = new List<CachedApplicableNorm>
            {
                new CachedApplicableNorm
                {
                    NormId = normId,
                    FactionId = normFactionId,
                    FactionName = "Cached Faction",
                    ViolationType = "theft",
                    BasePenalty = 5.0f,
                    Severity = NormSeverity.Standard,
                    Scope = NormScope.Internal,
                    Source = NormSource.Membership,
                    AuthorityLevel = AuthorityLevel.Influence,
                }
            },
            MergedNormMap = new Dictionary<string, CachedMergedNorm>
            {
                ["theft"] = new CachedMergedNorm
                {
                    ViolationType = "theft",
                    BasePenalty = 5.0f,
                    Source = NormSource.Membership,
                    FactionId = normFactionId,
                    Severity = NormSeverity.Standard,
                    AuthorityLevel = AuthorityLevel.Influence,
                }
            },
            MembershipFactionCount = 1,
            TerritoryFactionResolved = false,
            RealmBaselineResolved = false,
            CachedAt = DateTimeOffset.UtcNow,
        };

        _mockNormCacheStore
            .Setup(s => s.GetAsync(FactionService.BuildNormCacheKey(characterId, locationId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedModel);

        var request = new QueryApplicableNormsRequest
        {
            CharacterId = characterId,
            RealmId = realmId,
            LocationId = locationId,
        };

        // Act
        var (status, response) = await service.QueryApplicableNormsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        var applicableNorm = Assert.Single(response.ApplicableNorms);
        Assert.Equal("theft", applicableNorm.ViolationType);
        Assert.Equal(normFactionId, applicableNorm.FactionId);
        Assert.Equal(1, response.MembershipFactionCount);
        Assert.False(response.TerritoryFactionResolved);
        Assert.False(response.RealmBaselineResolved);
        Assert.True(response.MergedNormMap.ContainsKey("theft"));
        Assert.Equal(5.0f, response.MergedNormMap["theft"].BasePenalty);
    }

    [Fact]
    public async Task QueryApplicableNormsAsync_SingleMembershipNorm_ResolvesAndCaches()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var factionId = Guid.NewGuid();
        var normId = Guid.NewGuid();

        // No cache hit
        _mockNormCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedNormCacheModel?)null);

        // Character has one membership
        var membershipList = new MembershipListModel
        {
            CharacterId = characterId,
            Memberships = new List<MembershipEntry>
            {
                new MembershipEntry { FactionId = factionId, Role = FactionMemberRole.Member, JoinedAt = DateTimeOffset.UtcNow }
            }
        };
        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membershipList);

        // Faction is active
        var faction = CreateTestFactionModel(factionId);
        faction.RealmId = realmId;
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        // Faction has one norm
        _mockNormListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionNormsKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormListModel { FactionId = factionId, NormIds = new List<Guid> { normId } });

        _mockNormStore
            .Setup(s => s.GetAsync(FactionService.BuildNormKey(normId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormDefinitionModel
            {
                NormId = normId,
                FactionId = factionId,
                ViolationType = "deception",
                BasePenalty = 8.0f,
                Severity = NormSeverity.Strict,
                Scope = NormScope.Internal,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // No realm baseline
        _mockFactionQueryStore
            .Setup(s => s.JsonQueryAsync(It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<JsonQueryResult<FactionModel>>() as IReadOnlyList<JsonQueryResult<FactionModel>>);

        // Capture cache save
        ResolvedNormCacheModel? capturedCache = null;
        _mockNormCacheStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ResolvedNormCacheModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResolvedNormCacheModel, StateOptions?, CancellationToken>((_, cache, _, _) =>
            {
                capturedCache = cache;
            });

        var request = new QueryApplicableNormsRequest
        {
            CharacterId = characterId,
            RealmId = realmId,
        };

        // Act
        var (status, response) = await service.QueryApplicableNormsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        var resolvedNorm = Assert.Single(response.ApplicableNorms);
        Assert.Equal("deception", resolvedNorm.ViolationType);
        Assert.Equal(NormSource.Membership, resolvedNorm.Source);
        Assert.Equal(factionId, resolvedNorm.FactionId);
        Assert.Equal(1, response.MembershipFactionCount);

        Assert.True(response.MergedNormMap.ContainsKey("deception"));
        Assert.Equal(8.0f, response.MergedNormMap["deception"].BasePenalty);
        Assert.Equal(NormSource.Membership, response.MergedNormMap["deception"].Source);

        Assert.NotNull(capturedCache);
        Assert.Single(capturedCache.ApplicableNorms);
        Assert.Equal(1, capturedCache.MembershipFactionCount);
    }
}
