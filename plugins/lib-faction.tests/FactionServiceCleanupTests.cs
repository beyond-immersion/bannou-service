using BeyondImmersion.Bannou.Core;
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

public class FactionServiceCleanupTests : ServiceTestBase<FactionServiceConfiguration>
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
    private readonly Mock<IStateStore<CachedGovernanceResolution>> _mockGovernanceCacheStore;
    private readonly Mock<IStateStore<GovernanceEntryModel>> _mockGovernanceStore;
    private readonly Mock<IStateStore<GovernanceEntryListModel>> _mockGovernanceListStore;

    public FactionServiceCleanupTests()
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
        _mockGovernanceCacheStore = new Mock<IStateStore<CachedGovernanceResolution>>();
        _mockGovernanceStore = new Mock<IStateStore<GovernanceEntryModel>>();
        _mockGovernanceListStore = new Mock<IStateStore<GovernanceEntryListModel>>();

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
            Status = status,
            AuthorityLevel = AuthorityLevel.Influence,
            MemberCount = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    #region CleanupByCharacterAsync

    [Fact]
    public async Task CleanupByCharacterAsync_WithMemberships_RemovesAllAndReturnsCount()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var factionId1 = Guid.NewGuid();
        var factionId2 = Guid.NewGuid();

        var charList = new MembershipListModel
        {
            CharacterId = characterId,
            Memberships = new List<MembershipEntry>
            {
                new() { FactionId = factionId1, Role = FactionMemberRole.Member, JoinedAt = DateTimeOffset.UtcNow },
                new() { FactionId = factionId2, Role = FactionMemberRole.Recruit, JoinedAt = DateTimeOffset.UtcNow },
            },
        };
        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(charList);

        // Setup member records for RemoveMemberInternalAsync
        var member1 = new FactionMemberModel
        {
            FactionId = factionId1, CharacterId = characterId, Role = FactionMemberRole.Member,
            JoinedAt = DateTimeOffset.UtcNow, FactionName = "F1", FactionCode = "F1",
        };
        var member2 = new FactionMemberModel
        {
            FactionId = factionId2, CharacterId = characterId, Role = FactionMemberRole.Recruit,
            JoinedAt = DateTimeOffset.UtcNow, FactionName = "F2", FactionCode = "F2",
        };
        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId1, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(member1);
        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId2, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(member2);

        // Setup faction records for member count decrement
        var faction1 = CreateTestFactionModel(factionId1);
        var faction2 = CreateTestFactionModel(factionId2);
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction1);
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction2);

        var request = new CleanupByCharacterRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.CleanupByCharacterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.MembershipsRemoved);
    }

    [Fact]
    public async Task CleanupByCharacterAsync_NoMemberships_ReturnsZero()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MembershipListModel?)null);

        var request = new CleanupByCharacterRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.CleanupByCharacterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.MembershipsRemoved);
    }

    #endregion

    #region CleanupByLocationAsync

    [Fact]
    public async Task CleanupByLocationAsync_WithActiveClaim_ReleasesAndReturnsCount()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var factionId = Guid.NewGuid();
        var claimId = Guid.NewGuid();

        var claim = new TerritoryClaimModel
        {
            ClaimId = claimId,
            FactionId = factionId,
            LocationId = locationId,
            Status = TerritoryClaimStatus.Active,
            ClaimedAt = DateTimeOffset.UtcNow,
        };
        _mockTerritoryStore
            .Setup(s => s.GetAsync(FactionService.BuildLocationClaimKey(locationId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        // Setup faction for release
        var faction = CreateTestFactionModel(factionId);
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        // Setup territory claim list for release
        _mockTerritoryListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionClaimsKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerritoryClaimListModel
            {
                FactionId = factionId,
                ClaimIds = new List<Guid> { claimId },
            });

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

        var request = new CleanupByLocationRequest { LocationId = locationId };

        // Act
        var (status, response) = await service.CleanupByLocationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ClaimsRemoved);

        Assert.Equal("faction.territory.released", capturedTopic);
        var releasedEvent = Assert.IsType<FactionTerritoryReleasedEvent>(capturedEvent);
        Assert.Equal(factionId, releasedEvent.FactionId);
        Assert.Equal(locationId, releasedEvent.LocationId);
    }

    [Fact]
    public async Task CleanupByLocationAsync_NoClaim_ReturnsZero()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();

        _mockTerritoryStore
            .Setup(s => s.GetAsync(FactionService.BuildLocationClaimKey(locationId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TerritoryClaimModel?)null);

        var request = new CleanupByLocationRequest { LocationId = locationId };

        // Act
        var (status, response) = await service.CleanupByLocationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.ClaimsRemoved);
    }

    #endregion

    #region GetCompressDataAsync

    [Fact]
    public async Task GetCompressDataAsync_WithMemberships_ReturnsArchive()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var factionId = Guid.NewGuid();

        var charList = new MembershipListModel
        {
            CharacterId = characterId,
            Memberships = new List<MembershipEntry>
            {
                new() { FactionId = factionId, Role = FactionMemberRole.Member, JoinedAt = DateTimeOffset.UtcNow },
            },
        };
        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(charList);

        var member = new FactionMemberModel
        {
            FactionId = factionId, CharacterId = characterId, Role = FactionMemberRole.Member,
            JoinedAt = DateTimeOffset.UtcNow, FactionName = "Test", FactionCode = "TST",
        };
        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.True(response.HasMemberships);
        Assert.Equal(1, response.MembershipCount);
        Assert.NotNull(response.Memberships);
        Assert.Single(response.Memberships);
        Assert.Equal(factionId, response.Memberships.First().FactionId);
    }

    [Fact]
    public async Task GetCompressDataAsync_NoMemberships_ReturnsEmptyArchive()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MembershipListModel?)null);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.False(response.HasMemberships);
        Assert.Equal(0, response.MembershipCount);
        Assert.Null(response.Memberships);
    }

    #endregion

    #region RestoreFromArchiveAsync

    [Fact]
    public async Task RestoreFromArchiveAsync_ValidArchive_RestoresMemberships()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var factionId = Guid.NewGuid();
        var gsId = Guid.NewGuid();

        var archive = new FactionArchive
        {
            CharacterId = characterId,
            HasMemberships = true,
            MembershipCount = 1,
            Memberships = new List<FactionMemberResponse>
            {
                new()
                {
                    FactionId = factionId,
                    CharacterId = characterId,
                    Role = FactionMemberRole.Officer,
                    JoinedAt = DateTimeOffset.UtcNow.AddDays(-30),
                },
            },
        };
        var archiveData = BannouJson.Serialize(archive);

        var faction = new FactionModel
        {
            FactionId = factionId, GameServiceId = gsId, Name = "Test", Code = "TST",
            RealmId = Guid.NewGuid(), Status = FactionStatus.Active, IsDeprecated = false,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        // No existing member — fresh restore
        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionMemberModel?)null);

        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MembershipListModel?)null);

        FactionMemberModel? savedMember = null;
        _mockMemberStore
            .Setup(s => s.SaveAsync(
                FactionService.BuildMemberKey(factionId, characterId),
                It.IsAny<FactionMemberModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, FactionMemberModel, StateOptions?, CancellationToken>((_, m, _, _) => savedMember = m)
            .ReturnsAsync("ok");

        MembershipListModel? savedCharList = null;
        _mockMemberListStore
            .Setup(s => s.SaveAsync(
                FactionService.BuildCharacterMembershipsKey(characterId),
                It.IsAny<MembershipListModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, MembershipListModel, StateOptions?, CancellationToken>((_, list, _, _) => savedCharList = list)
            .ReturnsAsync("ok");

        // Faction store saves for member count increment
        _mockFactionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var request = new RestoreFromArchiveRequest { CharacterId = characterId, Data = archiveData };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.MembershipsRestoredCount);

        Assert.NotNull(savedMember);
        Assert.Equal(factionId, savedMember.FactionId);
        Assert.Equal(characterId, savedMember.CharacterId);
        Assert.Equal(FactionMemberRole.Officer, savedMember.Role);
        Assert.Equal("Test", savedMember.FactionName);

        Assert.NotNull(savedCharList);
        Assert.Single(savedCharList.Memberships);
        Assert.Equal(factionId, savedCharList.Memberships[0].FactionId);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_InvalidData_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new RestoreFromArchiveRequest
        {
            CharacterId = Guid.NewGuid(),
            Data = "not valid json {{{",
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion
}
