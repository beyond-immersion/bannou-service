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

public class FactionServiceTests : ServiceTestBase<FactionServiceConfiguration>
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

    public FactionServiceTests()
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
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    #region Constructor Validation

    #endregion

    #region Configuration Tests

    [Fact]
    public void FactionServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new FactionServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    #endregion

    #region Deprecation Tests

    [Fact]
    public async Task DeprecateFactionAsync_ValidFaction_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId, FactionStatus.Active);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.FactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new DeprecateFactionRequest
        {
            FactionId = factionId,
            DeprecationReason = "No longer relevant"
        };

        // Act
        var (status, response) = await service.DeprecateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(FactionStatus.Active, response.Status);
        Assert.True(response.IsDeprecated);
        Assert.NotNull(response.DeprecatedAt);
        Assert.Equal("No longer relevant", response.DeprecationReason);
    }

    [Fact]
    public async Task DeprecateFactionAsync_AlreadyDeprecated_ReturnsOKIdempotent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId, FactionStatus.Active);
        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        model.DeprecationReason = "Previously deprecated";

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.FactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new DeprecateFactionRequest
        {
            FactionId = factionId,
            DeprecationReason = "Deprecating again"
        };

        // Act
        var (status, response) = await service.DeprecateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert — idempotent per IMPLEMENTATION TENETS: caller's intent is already satisfied
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);

        // Verify no save was attempted (already in desired state)
        _mockFactionStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeprecateFactionAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.FactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        var request = new DeprecateFactionRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.DeprecateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeprecateFactionAsync_DissolvedFaction_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId, FactionStatus.Dissolved);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.FactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new DeprecateFactionRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.DeprecateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert — dissolved is a terminal state
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UndeprecateFactionAsync_DeprecatedFaction_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId, FactionStatus.Active);
        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        model.DeprecationReason = "Was deprecated";

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.FactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UndeprecateFactionRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.UndeprecateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(FactionStatus.Active, response.Status);
        Assert.Null(response.DeprecatedAt);
        Assert.Null(response.DeprecationReason);
    }

    [Fact]
    public async Task UndeprecateFactionAsync_ActiveFaction_ReturnsOKIdempotent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId, FactionStatus.Active);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.FactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UndeprecateFactionRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.UndeprecateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert — idempotent per IMPLEMENTATION TENETS: caller's intent is already satisfied
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(FactionStatus.Active, response.Status);

        // Verify no save was attempted (already in desired state)
        _mockFactionStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UndeprecateFactionAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.FactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        var request = new UndeprecateFactionRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.UndeprecateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UndeprecateFactionAsync_DissolvedFaction_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var model = CreateTestFactionModel(factionId, FactionStatus.Dissolved);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.FactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UndeprecateFactionRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.UndeprecateFactionAsync(request, TestContext.Current.CancellationToken);

        // Assert — dissolved is a terminal state
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion
}
