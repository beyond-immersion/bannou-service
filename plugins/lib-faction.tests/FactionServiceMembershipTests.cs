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

public class FactionServiceMembershipTests : ServiceTestBase<FactionServiceConfiguration>
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

    public FactionServiceMembershipTests()
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
        FactionStatus status = FactionStatus.Active,
        bool isDeprecated = false)
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
            IsDeprecated = isDeprecated,
            DeprecatedAt = isDeprecated ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            DeprecationReason = isDeprecated ? "Test deprecation" : null,
            MemberCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static FactionMemberModel CreateTestMemberModel(Guid factionId, Guid characterId, FactionMemberRole role = FactionMemberRole.Member)
    {
        return new FactionMemberModel
        {
            FactionId = factionId,
            CharacterId = characterId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow,
            FactionName = "Test Faction",
            FactionCode = "TEST_FACTION",
        };
    }

    // ========================================================================
    // AddMember
    // ========================================================================

    [Fact]
    public async Task AddMemberAsync_ValidRequest_SavesMemberAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);
        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionMemberModel?)null);
        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MembershipListModel?)null);

        // Capture member state saves
        var savedMembers = new List<(string Key, FactionMemberModel Model)>();
        _mockMemberStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionMemberModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, FactionMemberModel, StateOptions?, CancellationToken>((key, m, _, _) =>
                savedMembers.Add((key, m)))
            .Returns(Task.CompletedTask);

        // Capture published events
        var capturedEvents = new List<(string Topic, object Event)>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) => capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        var request = new AddMemberRequest
        {
            FactionId = factionId,
            CharacterId = characterId,
            Role = FactionMemberRole.Officer,
        };

        // Act
        var (status, response) = await service.AddMemberAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(factionId, response.FactionId);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(FactionMemberRole.Officer, response.Role);

        // Verify member was saved
        Assert.Single(savedMembers);
        Assert.Equal(FactionMemberRole.Officer, savedMembers[0].Model.Role);
        Assert.Equal(characterId, savedMembers[0].Model.CharacterId);

        // Verify member added event was published
        var addedEvent = capturedEvents.FirstOrDefault(e => e.Event is FactionMemberAddedEvent);
        Assert.NotNull(addedEvent.Event);
        var evt = Assert.IsType<FactionMemberAddedEvent>(addedEvent.Event);
        Assert.Equal(factionId, evt.FactionId);
        Assert.Equal(characterId, evt.CharacterId);
        Assert.Equal(FactionMemberRole.Officer, evt.Role);
    }

    [Fact]
    public async Task AddMemberAsync_FactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        // Act
        var (status, response) = await service.AddMemberAsync(
            new AddMemberRequest { FactionId = factionId, CharacterId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddMemberAsync_AlreadyMember_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        var existingMember = CreateTestMemberModel(factionId, characterId);
        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMember);

        // Act
        var (status, response) = await service.AddMemberAsync(
            new AddMemberRequest { FactionId = factionId, CharacterId = characterId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddMemberAsync_DeprecatedFaction_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId, isDeprecated: true);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        // Act
        var (status, response) = await service.AddMemberAsync(
            new AddMemberRequest { FactionId = factionId, CharacterId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    // ========================================================================
    // RemoveMember
    // ========================================================================

    [Fact]
    public async Task RemoveMemberAsync_ExistingMember_RemovesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId);
        faction.MemberCount = 1;
        var member = CreateTestMemberModel(factionId, characterId);

        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MembershipListModel
            {
                CharacterId = characterId,
                Memberships = new List<MembershipEntry>
                {
                    new MembershipEntry { FactionId = factionId, Role = FactionMemberRole.Member, JoinedAt = DateTimeOffset.UtcNow },
                },
            });
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        // Capture published events
        var capturedEvents = new List<(string Topic, object Event)>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) => capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        // Act
        var status = await service.RemoveMemberAsync(
            new RemoveMemberRequest { FactionId = factionId, CharacterId = characterId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify member removed event was published
        var removedEvent = capturedEvents.FirstOrDefault(e => e.Event is FactionMemberRemovedEvent);
        Assert.NotNull(removedEvent.Event);
        var evt = Assert.IsType<FactionMemberRemovedEvent>(removedEvent.Event);
        Assert.Equal(factionId, evt.FactionId);
        Assert.Equal(characterId, evt.CharacterId);

        // Verify member record was deleted
        _mockMemberStore.Verify(
            s => s.DeleteAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveMemberAsync_NotMember_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionMemberModel?)null);

        // Act
        var status = await service.RemoveMemberAsync(
            new RemoveMemberRequest { FactionId = factionId, CharacterId = characterId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    // ========================================================================
    // ListMembershipsByCharacter
    // ========================================================================

    [Fact]
    public async Task ListMembershipsByCharacterAsync_HasMemberships_ReturnsEntries()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var factionId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();

        var faction = CreateTestFactionModel(factionId);
        faction.GameServiceId = gameServiceId;

        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MembershipListModel
            {
                CharacterId = characterId,
                Memberships = new List<MembershipEntry>
                {
                    new MembershipEntry { FactionId = factionId, Role = FactionMemberRole.Officer, JoinedAt = DateTimeOffset.UtcNow },
                },
            });

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        // Act
        var (status, response) = await service.ListMembershipsByCharacterAsync(
            new ListMembershipsByCharacterRequest { CharacterId = characterId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Memberships);
        Assert.Equal(factionId, response.Memberships[0].FactionId);
        Assert.Equal(FactionMemberRole.Officer, response.Memberships[0].Role);
    }

    // ========================================================================
    // UpdateMemberRole
    // ========================================================================

    [Fact]
    public async Task UpdateMemberRoleAsync_RoleChanged_SavesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var member = CreateTestMemberModel(factionId, characterId, FactionMemberRole.Member);

        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
        _mockMemberListStore
            .Setup(s => s.GetAsync(FactionService.BuildCharacterMembershipsKey(characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MembershipListModel
            {
                CharacterId = characterId,
                Memberships = new List<MembershipEntry>
                {
                    new MembershipEntry { FactionId = factionId, Role = FactionMemberRole.Member, JoinedAt = DateTimeOffset.UtcNow },
                },
            });

        // Capture member state saves
        var savedMembers = new List<FactionMemberModel>();
        _mockMemberStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionMemberModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, FactionMemberModel, StateOptions?, CancellationToken>((_, m, _, _) => savedMembers.Add(m))
            .Returns(Task.CompletedTask);

        // Capture published events
        var capturedEvents = new List<(string Topic, object Event)>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) => capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        var request = new UpdateMemberRoleRequest
        {
            FactionId = factionId,
            CharacterId = characterId,
            Role = FactionMemberRole.Officer,
        };

        // Act
        var (status, response) = await service.UpdateMemberRoleAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(FactionMemberRole.Officer, response.Role);

        // Verify member was saved with new role
        Assert.Single(savedMembers);
        Assert.Equal(FactionMemberRole.Officer, savedMembers[0].Role);

        // Verify role changed event with old and new roles
        var roleEvent = capturedEvents.FirstOrDefault(e => e.Event is FactionMemberRoleChangedEvent);
        Assert.NotNull(roleEvent.Event);
        var evt = Assert.IsType<FactionMemberRoleChangedEvent>(roleEvent.Event);
        Assert.Equal(FactionMemberRole.Member, evt.PreviousRole);
        Assert.Equal(FactionMemberRole.Officer, evt.NewRole);
        Assert.Equal(factionId, evt.FactionId);
        Assert.Equal(characterId, evt.CharacterId);
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_MemberNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionMemberModel?)null);

        // Act
        var (status, response) = await service.UpdateMemberRoleAsync(
            new UpdateMemberRoleRequest { FactionId = factionId, CharacterId = characterId, Role = FactionMemberRole.Leader },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_SameRole_ReturnsOKWithoutWrite()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var member = CreateTestMemberModel(factionId, characterId, FactionMemberRole.Officer);

        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);

        var request = new UpdateMemberRoleRequest
        {
            FactionId = factionId,
            CharacterId = characterId,
            Role = FactionMemberRole.Officer,
        };

        // Act
        var (status, response) = await service.UpdateMemberRoleAsync(request, TestContext.Current.CancellationToken);

        // Assert — idempotent: same role returns OK without any writes
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(FactionMemberRole.Officer, response.Role);

        _mockMemberStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionMemberModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ========================================================================
    // CheckMembership
    // ========================================================================

    [Fact]
    public async Task CheckMembershipAsync_IsMember_ReturnsTrueWithRole()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var member = CreateTestMemberModel(factionId, characterId, FactionMemberRole.Leader);

        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);

        // Act
        var (status, response) = await service.CheckMembershipAsync(
            new CheckMembershipRequest { FactionId = factionId, CharacterId = characterId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsMember);
        Assert.Equal(FactionMemberRole.Leader, response.Role);
    }

    [Fact]
    public async Task CheckMembershipAsync_NotMember_ReturnsFalseWithNullRole()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        _mockMemberStore
            .Setup(s => s.GetAsync(FactionService.BuildMemberKey(factionId, characterId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionMemberModel?)null);

        // Act
        var (status, response) = await service.CheckMembershipAsync(
            new CheckMembershipRequest { FactionId = factionId, CharacterId = characterId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsMember);
        Assert.Null(response.Role);
    }
}
