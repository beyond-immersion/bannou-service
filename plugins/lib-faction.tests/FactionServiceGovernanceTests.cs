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

public class FactionServiceGovernanceTests : ServiceTestBase<FactionServiceConfiguration>
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

    public FactionServiceGovernanceTests()
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

    private static FactionModel CreateTestFactionModel(
        Guid factionId,
        AuthorityLevel authority = AuthorityLevel.Sovereign,
        Guid? seedId = null,
        Guid? parentFactionId = null)
    {
        return new FactionModel
        {
            FactionId = factionId,
            GameServiceId = Guid.NewGuid(),
            Name = "Test Faction",
            Code = "TEST_FACTION",
            RealmId = Guid.NewGuid(),
            Status = FactionStatus.Active,
            AuthorityLevel = authority,
            SeedId = seedId ?? Guid.NewGuid(),
            ParentFactionId = parentFactionId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private void SetupSeedCapability(Guid seedId, string capability, bool hasCapability)
    {
        var capabilities = hasCapability
            ? new List<Capability> { new() { CapabilityCode = capability, Domain = "governance", Fidelity = 1.0f } }
            : new List<Capability>();

        _mockSeedClient
            .Setup(s => s.GetCapabilityManifestAsync(
                It.Is<GetCapabilityManifestRequest>(r => r.SeedId == seedId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityManifestResponse
            {
                SeedId = seedId,
                SeedTypeCode = "faction",
                ComputedAt = DateTimeOffset.UtcNow,
                Version = 1,
                Capabilities = capabilities,
            });
    }

    #region SetGovernanceEntryAsync

    [Fact]
    public async Task SetGovernanceEntryAsync_NewEntry_CreatesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId, AuthorityLevel.Sovereign, seedId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        SetupSeedCapability(seedId, "governance.arbitrate", true);

        _mockGovernanceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("gov:dom:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GovernanceEntryModel?)null);

        _mockGovernanceListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionGovernanceKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GovernanceEntryListModel?)null);

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

        GovernanceEntryModel? savedEntry = null;
        _mockGovernanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GovernanceEntryModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GovernanceEntryModel, StateOptions?, CancellationToken>((_, entry, _, _) => savedEntry = entry)
            .ReturnsAsync("ok");

        var request = new SetGovernanceEntryRequest
        {
            FactionId = factionId,
            Domain = "trade_dispute",
            TemplateCode = "DISPUTE_TEMPLATE_V1",
        };

        // Act
        var (status, response) = await service.SetGovernanceEntryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(factionId, response.FactionId);
        Assert.Equal("trade_dispute", response.Domain);
        Assert.Equal("DISPUTE_TEMPLATE_V1", response.TemplateCode);

        Assert.NotNull(savedEntry);
        Assert.Equal("trade_dispute", savedEntry.Domain);

        Assert.Equal("faction.governance.defined", capturedTopic);
        var definedEvent = Assert.IsType<FactionGovernanceDefinedEvent>(capturedEvent);
        Assert.Equal(factionId, definedEvent.FactionId);
        Assert.Equal("trade_dispute", definedEvent.Domain);
        Assert.Null(definedEvent.ChangedFields);
    }

    [Fact]
    public async Task SetGovernanceEntryAsync_UpdateExisting_PublishesEventWithChangedFields()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var governanceId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId, AuthorityLevel.Delegated, seedId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        SetupSeedCapability(seedId, "governance.arbitrate", true);

        var existingEntry = new GovernanceEntryModel
        {
            GovernanceId = governanceId,
            FactionId = factionId,
            Domain = "dissolution",
            TemplateCode = "OLD_TEMPLATE",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _mockGovernanceStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionGovernanceDomainKey(factionId, "dissolution"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

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

        var request = new SetGovernanceEntryRequest
        {
            FactionId = factionId,
            Domain = "dissolution",
            TemplateCode = "NEW_TEMPLATE",
        };

        // Act
        var (status, response) = await service.SetGovernanceEntryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("NEW_TEMPLATE", response.TemplateCode);

        Assert.Equal("faction.governance.defined", capturedTopic);
        var definedEvent = Assert.IsType<FactionGovernanceDefinedEvent>(capturedEvent);
        Assert.NotNull(definedEvent.ChangedFields);
        Assert.Contains("templateCode", definedEvent.ChangedFields);
        Assert.Contains("governanceParameters", definedEvent.ChangedFields);
    }

    [Fact]
    public async Task SetGovernanceEntryAsync_FactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        var request = new SetGovernanceEntryRequest { FactionId = factionId, Domain = "test", TemplateCode = "T" };

        // Act
        var (status, response) = await service.SetGovernanceEntryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SetGovernanceEntryAsync_InsufficientAuthority_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId, AuthorityLevel.Influence);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        var request = new SetGovernanceEntryRequest { FactionId = factionId, Domain = "test", TemplateCode = "T" };

        // Act
        var (status, response) = await service.SetGovernanceEntryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SetGovernanceEntryAsync_LacksSeedCapability_ReturnsForbidden()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId, AuthorityLevel.Sovereign, seedId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        SetupSeedCapability(seedId, "governance.arbitrate", false);

        var request = new SetGovernanceEntryRequest { FactionId = factionId, Domain = "test", TemplateCode = "T" };

        // Act
        var (status, response) = await service.SetGovernanceEntryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    #endregion

    #region RemoveGovernanceEntryAsync

    [Fact]
    public async Task RemoveGovernanceEntryAsync_ExistingEntry_DeletesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var governanceId = Guid.NewGuid();

        var entry = new GovernanceEntryModel
        {
            GovernanceId = governanceId,
            FactionId = factionId,
            Domain = "trade_dispute",
            TemplateCode = "DISPUTE_TEMPLATE",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _mockGovernanceStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionGovernanceDomainKey(factionId, "trade_dispute"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var govList = new GovernanceEntryListModel
        {
            FactionId = factionId,
            GovernanceIds = new List<Guid> { governanceId },
        };
        _mockGovernanceListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionGovernanceKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(govList);

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

        var request = new RemoveGovernanceEntryRequest { FactionId = factionId, Domain = "trade_dispute" };

        // Act
        var status = await service.RemoveGovernanceEntryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        _mockGovernanceStore.Verify(
            s => s.DeleteAsync(FactionService.BuildGovernanceKey(governanceId), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Equal("faction.governance.deleted", capturedTopic);
        var deletedEvent = Assert.IsType<FactionGovernanceDeletedEvent>(capturedEvent);
        Assert.Equal(factionId, deletedEvent.FactionId);
        Assert.Equal(governanceId, deletedEvent.GovernanceId);
        Assert.Equal("trade_dispute", deletedEvent.Domain);
    }

    [Fact]
    public async Task RemoveGovernanceEntryAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockGovernanceStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionGovernanceDomainKey(factionId, "missing"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GovernanceEntryModel?)null);

        var request = new RemoveGovernanceEntryRequest { FactionId = factionId, Domain = "missing" };

        // Act
        var status = await service.RemoveGovernanceEntryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ListGovernanceEntriesAsync

    [Fact]
    public async Task ListGovernanceEntriesAsync_WithEntries_ReturnsAll()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();
        var govId1 = Guid.NewGuid();
        var govId2 = Guid.NewGuid();
        var faction = CreateTestFactionModel(factionId);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        var govList = new GovernanceEntryListModel
        {
            FactionId = factionId,
            GovernanceIds = new List<Guid> { govId1, govId2 },
        };
        _mockGovernanceListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionGovernanceKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(govList);

        _mockGovernanceStore
            .Setup(s => s.GetAsync(FactionService.BuildGovernanceKey(govId1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GovernanceEntryModel
            {
                GovernanceId = govId1,
                FactionId = factionId,
                Domain = "trade",
                TemplateCode = "T1",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        _mockGovernanceStore
            .Setup(s => s.GetAsync(FactionService.BuildGovernanceKey(govId2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GovernanceEntryModel
            {
                GovernanceId = govId2,
                FactionId = factionId,
                Domain = "law",
                TemplateCode = "T2",
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var request = new ListGovernanceEntriesRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.ListGovernanceEntriesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Entries.Count);
    }

    [Fact]
    public async Task ListGovernanceEntriesAsync_FactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var factionId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(factionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        var request = new ListGovernanceEntriesRequest { FactionId = factionId };

        // Act
        var (status, response) = await service.ListGovernanceEntriesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region DelegateAuthorityAsync

    [Fact]
    public async Task DelegateAuthorityAsync_ValidHierarchy_DelegatesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var sovereignId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sovereign = CreateTestFactionModel(sovereignId, AuthorityLevel.Sovereign);
        var target = CreateTestFactionModel(targetId, AuthorityLevel.Influence, parentFactionId: sovereignId);
        target.GameServiceId = sovereign.GameServiceId;

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(sovereignId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sovereign);
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        FactionModel? savedTarget = null;
        _mockFactionStore
            .Setup(s => s.SaveAsync(FactionService.BuildFactionKey(targetId), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, FactionModel, StateOptions?, CancellationToken>((_, model, _, _) => savedTarget = model)
            .ReturnsAsync("ok");
        _mockFactionStore
            .Setup(s => s.SaveAsync(It.Is<string>(k => k.StartsWith("fac:") && k != FactionService.BuildFactionKey(targetId)), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var capturedTopics = new List<string>();
        var capturedEvents = new List<object>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopics.Add(topic);
                capturedEvents.Add(evt);
            })
            .ReturnsAsync(true);

        var request = new DelegateAuthorityRequest
        {
            SovereignFactionId = sovereignId,
            TargetFactionId = targetId,
            Domains = new List<string> { "trade", "law" },
        };

        // Act
        var (status, response) = await service.DelegateAuthorityAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(AuthorityLevel.Delegated, response.AuthorityLevel);

        Assert.NotNull(savedTarget);
        Assert.Equal(AuthorityLevel.Delegated, savedTarget.AuthorityLevel);

        Assert.Contains("faction.authority.delegated", capturedTopics);
        var delegatedEvent = capturedEvents.OfType<FactionAuthorityDelegatedEvent>().Single();
        Assert.Equal(sovereignId, delegatedEvent.SovereignFactionId);
        Assert.Equal(targetId, delegatedEvent.TargetFactionId);
        Assert.Equal(2, delegatedEvent.Domains.Count);
    }

    [Fact]
    public async Task DelegateAuthorityAsync_SovereignNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var sovereignId = Guid.NewGuid();

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(sovereignId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FactionModel?)null);

        var request = new DelegateAuthorityRequest
        {
            SovereignFactionId = sovereignId,
            TargetFactionId = Guid.NewGuid(),
            Domains = new List<string> { "trade" },
        };

        // Act
        var (status, response) = await service.DelegateAuthorityAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DelegateAuthorityAsync_NotSovereign_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sovereignId = Guid.NewGuid();
        var faction = CreateTestFactionModel(sovereignId, AuthorityLevel.Influence);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(sovereignId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(faction);

        var request = new DelegateAuthorityRequest
        {
            SovereignFactionId = sovereignId,
            TargetFactionId = Guid.NewGuid(),
            Domains = new List<string> { "trade" },
        };

        // Act
        var (status, response) = await service.DelegateAuthorityAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DelegateAuthorityAsync_TargetNotDescendant_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sovereignId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sovereign = CreateTestFactionModel(sovereignId, AuthorityLevel.Sovereign);
        // Target has no parent — not a descendant of sovereign
        var target = CreateTestFactionModel(targetId, AuthorityLevel.Influence);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(sovereignId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sovereign);
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        var request = new DelegateAuthorityRequest
        {
            SovereignFactionId = sovereignId,
            TargetFactionId = targetId,
            Domains = new List<string> { "trade" },
        };

        // Act
        var (status, response) = await service.DelegateAuthorityAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region RevokeAuthorityAsync

    [Fact]
    public async Task RevokeAuthorityAsync_BlanketRevocation_RevertsToInfluenceAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var sovereignId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sovereign = CreateTestFactionModel(sovereignId, AuthorityLevel.Sovereign);
        var target = CreateTestFactionModel(targetId, AuthorityLevel.Delegated);
        target.GameServiceId = sovereign.GameServiceId;

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(sovereignId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sovereign);
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        _mockGovernanceListStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionGovernanceKey(targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GovernanceEntryListModel
            {
                FactionId = targetId,
                GovernanceIds = new List<Guid>(),
            });

        FactionModel? savedTarget = null;
        _mockFactionStore
            .Setup(s => s.SaveAsync(FactionService.BuildFactionKey(targetId), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, FactionModel, StateOptions?, CancellationToken>((_, model, _, _) => savedTarget = model)
            .ReturnsAsync("ok");
        _mockFactionStore
            .Setup(s => s.SaveAsync(It.Is<string>(k => k.StartsWith("fac:") && k != FactionService.BuildFactionKey(targetId)), It.IsAny<FactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var capturedTopics = new List<string>();
        var capturedEvents = new List<object>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopics.Add(topic);
                capturedEvents.Add(evt);
            })
            .ReturnsAsync(true);

        var request = new RevokeAuthorityRequest
        {
            SovereignFactionId = sovereignId,
            TargetFactionId = targetId,
            Domains = null, // blanket revocation
        };

        // Act
        var (status, response) = await service.RevokeAuthorityAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(AuthorityLevel.Influence, response.AuthorityLevel);

        Assert.NotNull(savedTarget);
        Assert.Equal(AuthorityLevel.Influence, savedTarget.AuthorityLevel);

        Assert.Contains("faction.authority.revoked", capturedTopics);
        var revokedEvent = capturedEvents.OfType<FactionAuthorityRevokedEvent>().Single();
        Assert.Equal(sovereignId, revokedEvent.SovereignFactionId);
        Assert.Equal(targetId, revokedEvent.TargetFactionId);
        Assert.Equal(AuthorityLevel.Influence, revokedEvent.ResultingAuthorityLevel);
    }

    [Fact]
    public async Task RevokeAuthorityAsync_TargetNotDelegated_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sovereignId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sovereign = CreateTestFactionModel(sovereignId, AuthorityLevel.Sovereign);
        var target = CreateTestFactionModel(targetId, AuthorityLevel.Influence);

        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(sovereignId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sovereign);
        _mockFactionStore
            .Setup(s => s.GetAsync(FactionService.BuildFactionKey(targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        var request = new RevokeAuthorityRequest
        {
            SovereignFactionId = sovereignId,
            TargetFactionId = targetId,
        };

        // Act
        var (status, response) = await service.RevokeAuthorityAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion
}
