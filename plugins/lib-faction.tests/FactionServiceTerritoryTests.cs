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

public class FactionServiceTerritoryTests : ServiceTestBase<FactionServiceConfiguration>
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

    public FactionServiceTerritoryTests()
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
    // ClaimTerritoryAsync
    // ========================================================================

    [Fact]
    public async Task ClaimTerritoryAsync_ValidRequest_SavesClaimAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
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
                    new Capability { CapabilityCode = "territory.claim", Domain = "governance", Fidelity = 1.0f }
                }
            });

        _mockTerritoryStore
            .Setup(s => s.GetAsync(FactionService.BuildLocationClaimKey(locationId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TerritoryClaimModel?)null);

        _mockTerritoryListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionClaimsKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TerritoryClaimListModel?)null);

        // Capture state saves
        TerritoryClaimModel? capturedClaim = null;
        _mockTerritoryStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("tcl:") && !k.StartsWith("tcl:loc:") && !k.StartsWith("tcl:fac:")),
                It.IsAny<TerritoryClaimModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, TerritoryClaimModel, StateOptions?, CancellationToken>((_, claim, _, _) =>
            {
                capturedClaim = claim;
            });

        // Capture event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                if (topic.Contains("territory.claimed"))
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                }
            })
            .ReturnsAsync(true);

        var request = new ClaimTerritoryRequest { FactionId = factionId, LocationId = locationId };

        // Act
        var (status, response) = await service.ClaimTerritoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(factionId, response.FactionId);
        Assert.Equal(locationId, response.LocationId);
        Assert.Equal(TerritoryClaimStatus.Active, response.Status);

        Assert.NotNull(capturedClaim);
        Assert.Equal(factionId, capturedClaim.FactionId);
        Assert.Equal(locationId, capturedClaim.LocationId);
        Assert.Equal(TerritoryClaimStatus.Active, capturedClaim.Status);

        Assert.NotNull(capturedTopic);
        var claimedEvent = Assert.IsType<FactionTerritoryClaimedEvent>(capturedEvent);
        Assert.Equal(factionId, claimedEvent.FactionId);
        Assert.Equal(locationId, claimedEvent.LocationId);
    }

    [Fact]
    public async Task ClaimTerritoryAsync_FactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        var request = new ClaimTerritoryRequest { FactionId = factionId, LocationId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.ClaimTerritoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ClaimTerritoryAsync_DeprecatedFaction_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId);
        faction.IsDeprecated = true;

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        var request = new ClaimTerritoryRequest { FactionId = factionId, LocationId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.ClaimTerritoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ClaimTerritoryAsync_LocationAlreadyClaimed_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
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
                    new Capability { CapabilityCode = "territory.claim", Domain = "governance", Fidelity = 1.0f }
                }
            });

        var existingClaim = new TerritoryClaimModel
        {
            ClaimId = Guid.NewGuid(),
            FactionId = Guid.NewGuid(),
            LocationId = locationId,
            Status = TerritoryClaimStatus.Active,
            ClaimedAt = DateTimeOffset.UtcNow,
        };
        _mockTerritoryStore
            .Setup(s => s.GetAsync(FactionService.BuildLocationClaimKey(locationId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingClaim);

        var request = new ClaimTerritoryRequest { FactionId = factionId, LocationId = locationId };

        // Act
        var (status, response) = await service.ClaimTerritoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    // ========================================================================
    // ReleaseTerritoryAsync
    // ========================================================================

    [Fact]
    public async Task ReleaseTerritoryAsync_ValidClaim_PublishesReleasedEvent()
    {
        // Arrange
        var service = CreateService();
        var claimId = Guid.NewGuid();
        var factionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var claim = new TerritoryClaimModel
        {
            ClaimId = claimId,
            FactionId = factionId,
            LocationId = locationId,
            Status = TerritoryClaimStatus.Active,
            ClaimedAt = DateTimeOffset.UtcNow,
        };

        _mockTerritoryStore
            .Setup(s => s.GetAsync(FactionService.BuildClaimKey(claimId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _mockTerritoryListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionClaimsKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerritoryClaimListModel { FactionId = factionId, ClaimIds = new List<Guid> { claimId } });

        // Capture event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                if (topic.Contains("territory.released"))
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                }
            })
            .ReturnsAsync(true);

        var request = new ReleaseTerritoryRequest { ClaimId = claimId };

        // Act
        var status = await service.ReleaseTerritoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        Assert.NotNull(capturedTopic);
        var releasedEvent = Assert.IsType<FactionTerritoryReleasedEvent>(capturedEvent);
        Assert.Equal(factionId, releasedEvent.FactionId);
        Assert.Equal(locationId, releasedEvent.LocationId);
        Assert.Equal(claimId, releasedEvent.ClaimId);
    }

    [Fact]
    public async Task ReleaseTerritoryAsync_ClaimNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var claimId = Guid.NewGuid();

        _mockTerritoryStore
            .Setup(s => s.GetAsync(FactionService.BuildClaimKey(claimId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TerritoryClaimModel?)null);

        var request = new ReleaseTerritoryRequest { ClaimId = claimId };

        // Act
        var status = await service.ReleaseTerritoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    // ========================================================================
    // GetControllingFactionAsync
    // ========================================================================

    [Fact]
    public async Task GetControllingFactionAsync_ActiveClaim_ReturnsFactionAndClaim()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var factionId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var claimedAt = DateTimeOffset.UtcNow.AddHours(-1);

        var claim = new TerritoryClaimModel
        {
            ClaimId = claimId,
            FactionId = factionId,
            LocationId = locationId,
            Status = TerritoryClaimStatus.Active,
            ClaimedAt = claimedAt,
        };

        _mockTerritoryStore
            .Setup(s => s.GetAsync(FactionService.BuildLocationClaimKey(locationId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var faction = CreateTestFactionModel(factionId);
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        var request = new GetControllingFactionRequest { LocationId = locationId };

        // Act
        var (status, response) = await service.GetControllingFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(locationId, response.LocationId);
        Assert.Equal(claimId, response.ClaimId);
        Assert.Equal(claimedAt, response.ClaimedAt);
        Assert.Equal(factionId, response.Faction.FactionId);
    }

    [Fact]
    public async Task GetControllingFactionAsync_NoClaim_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();

        _mockTerritoryStore
            .Setup(s => s.GetAsync(FactionService.BuildLocationClaimKey(locationId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TerritoryClaimModel?)null);

        var request = new GetControllingFactionRequest { LocationId = locationId };

        // Act
        var (status, response) = await service.GetControllingFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetControllingFactionAsync_FactionDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var factionId = Guid.NewGuid();

        var claim = new TerritoryClaimModel
        {
            ClaimId = Guid.NewGuid(),
            FactionId = factionId,
            LocationId = locationId,
            Status = TerritoryClaimStatus.Active,
            ClaimedAt = DateTimeOffset.UtcNow,
        };

        _mockTerritoryStore
            .Setup(s => s.GetAsync(FactionService.BuildLocationClaimKey(locationId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        var request = new GetControllingFactionRequest { LocationId = locationId };

        // Act
        var (status, response) = await service.GetControllingFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }
}
