using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Seed.Tests;

/// <summary>
/// Unit tests for the Seed service.
/// </summary>
public class SeedServiceTests : ServiceTestBase<SeedServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<SeedModel>> _mockSeedStore;
    private readonly Mock<IJsonQueryableStateStore<SeedModel>> _mockSeedQueryStore;
    private readonly Mock<IStateStore<SeedGrowthModel>> _mockGrowthStore;
    private readonly Mock<IStateStore<CapabilityManifestModel>> _mockCapabilitiesStore;
    private readonly Mock<IStateStore<SeedBondModel>> _mockBondStore;
    private readonly Mock<IStateStore<SeedTypeDefinitionModel>> _mockTypeStore;
    private readonly Mock<IJsonQueryableStateStore<SeedTypeDefinitionModel>> _mockTypeQueryStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<SeedService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;

    private readonly Guid _testGameServiceId = Guid.NewGuid();
    private readonly Guid _testOwnerId = Guid.NewGuid();

    public SeedServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockSeedStore = new Mock<IStateStore<SeedModel>>();
        _mockSeedQueryStore = new Mock<IJsonQueryableStateStore<SeedModel>>();
        _mockGrowthStore = new Mock<IStateStore<SeedGrowthModel>>();
        _mockCapabilitiesStore = new Mock<IStateStore<CapabilityManifestModel>>();
        _mockBondStore = new Mock<IStateStore<SeedBondModel>>();
        _mockTypeStore = new Mock<IStateStore<SeedTypeDefinitionModel>>();
        _mockTypeQueryStore = new Mock<IJsonQueryableStateStore<SeedTypeDefinitionModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<SeedService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();

        // Wire up state store factory
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedModel>(StateStoreDefinitions.Seed))
            .Returns(_mockSeedStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<SeedModel>(StateStoreDefinitions.Seed))
            .Returns(_mockSeedQueryStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowth))
            .Returns(_mockGrowthStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CapabilityManifestModel>(StateStoreDefinitions.SeedCapabilitiesCache))
            .Returns(_mockCapabilitiesStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedBondModel>(StateStoreDefinitions.SeedBonds))
            .Returns(_mockBondStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions))
            .Returns(_mockTypeStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions))
            .Returns(_mockTypeQueryStore.Object);

        // Default lock to succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Message bus: loose mock returns false for TryPublishAsync by default,
        // service uses TryPublish (fire-and-forget) so this is fine.

        // Default configuration
        Configuration.DefaultMaxSeedsPerOwner = 3;
        Configuration.MaxSeedTypesPerGameService = 50;
        Configuration.BondSharedGrowthMultiplier = 1.5;
        Configuration.CapabilityRecomputeDebounceMs = 5000;
        Configuration.GrowthDecayEnabled = false;
        Configuration.GrowthDecayRatePerDay = 0.01;
        Configuration.BondStrengthGrowthRate = 0.1;
        Configuration.DefaultQueryPageSize = 100;
    }

    private SeedService CreateService() => new SeedService(
        _mockMessageBus.Object,
        _mockStateStoreFactory.Object,
        _mockLockProvider.Object,
        _mockLogger.Object,
        Configuration,
        _mockEventConsumer.Object,
        _mockGameServiceClient.Object);

    private SeedTypeDefinitionModel CreateTestSeedType(
        string seedTypeCode = "guardian",
        Guid? gameServiceId = null,
        int maxPerOwner = 3) => new SeedTypeDefinitionModel
        {
            SeedTypeCode = seedTypeCode,
            GameServiceId = gameServiceId ?? _testGameServiceId,
            DisplayName = "Guardian",
            Description = "A guardian seed type",
            MaxPerOwner = maxPerOwner,
            AllowedOwnerTypes = new List<string> { "account", "character" },
            GrowthPhases = new List<GrowthPhaseDefinition>
        {
            new GrowthPhaseDefinition { PhaseCode = "nascent", DisplayName = "Nascent", MinTotalGrowth = 0 },
            new GrowthPhaseDefinition { PhaseCode = "awakening", DisplayName = "Awakening", MinTotalGrowth = 10 },
            new GrowthPhaseDefinition { PhaseCode = "mature", DisplayName = "Mature", MinTotalGrowth = 50 }
        },
            BondCardinality = 1,
            BondPermanent = false,
            CapabilityRules = new List<CapabilityRule>
        {
            new CapabilityRule { CapabilityCode = "combat.stance", Domain = "combat.melee", UnlockThreshold = 5f, FidelityFormula = "linear" }
        }
        };

    private SeedModel CreateTestSeed(
        Guid? seedId = null,
        Guid? ownerId = null,
        string ownerType = "character",
        string seedTypeCode = "guardian",
        Guid? gameServiceId = null,
        SeedStatus status = SeedStatus.Dormant) => new SeedModel
        {
            SeedId = seedId ?? Guid.NewGuid(),
            OwnerId = ownerId ?? _testOwnerId,
            OwnerType = ownerType,
            SeedTypeCode = seedTypeCode,
            GameServiceId = gameServiceId ?? _testGameServiceId,
            CreatedAt = DateTimeOffset.UtcNow,
            GrowthPhase = "nascent",
            TotalGrowth = 0f,
            DisplayName = "Test Seed",
            Status = status
        };

    private void SetupSeedQueryPagedAsync(
        List<SeedModel> items, long totalCount, int offset = 0, int limit = 100)
    {
        var queryResults = items.Select(m =>
            new JsonQueryResult<SeedModel>($"seed:{m.SeedId}", m)).ToList();

        _mockSeedQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<SeedModel>(queryResults, totalCount, offset, limit));
    }

    private void SetupTypeQueryPagedAsync(
        List<SeedTypeDefinitionModel> items, long totalCount, int offset = 0, int limit = 100)
    {
        var queryResults = items.Select(m =>
            new JsonQueryResult<SeedTypeDefinitionModel>($"type:{m.GameServiceId}:{m.SeedTypeCode}", m)).ToList();

        _mockTypeQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<SeedTypeDefinitionModel>(queryResults, totalCount, offset, limit));
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void SeedService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<SeedService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void SeedServiceConfiguration_CanBeInstantiated()
    {
        var config = new SeedServiceConfiguration();
        Assert.NotNull(config);
    }

    #endregion

    #region CreateSeed Tests

    [Fact]
    public async Task CreateSeed_ValidRequest_SavesSeedAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var seedType = CreateTestSeedType();

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SetupSeedQueryPagedAsync(new List<SeedModel>(), 0);

        SeedModel? savedSeed = null;
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedModel, StateOptions?, CancellationToken>((_, m, _, _) => savedSeed = m)
            .ReturnsAsync("etag");

        var request = new CreateSeedRequest
        {
            OwnerId = _testOwnerId,
            OwnerType = "character",
            SeedTypeCode = "guardian",
            GameServiceId = _testGameServiceId,
            DisplayName = "My Guardian"
        };

        // Act
        var (status, response) = await service.CreateSeedAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("guardian", response.SeedTypeCode);
        Assert.Equal("My Guardian", response.DisplayName);
        Assert.Equal(SeedStatus.Active, response.Status);
        Assert.Equal("nascent", response.GrowthPhase);

        Assert.NotNull(savedSeed);
        Assert.Equal(_testOwnerId, savedSeed.OwnerId);
        Assert.Equal("character", savedSeed.OwnerType);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.created",
            It.Is<SeedCreatedEvent>(e => e.SeedTypeCode == "guardian"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSeed_InvalidSeedType_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedTypeDefinitionModel?)null);

        var request = new CreateSeedRequest
        {
            OwnerId = _testOwnerId,
            OwnerType = "character",
            SeedTypeCode = "nonexistent",
            GameServiceId = _testGameServiceId
        };

        // Act
        var (status, response) = await service.CreateSeedAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateSeed_InvalidOwnerType_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var seedType = CreateTestSeedType();

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        var request = new CreateSeedRequest
        {
            OwnerId = _testOwnerId,
            OwnerType = "invalid_type",
            SeedTypeCode = "guardian",
            GameServiceId = _testGameServiceId
        };

        // Act
        var (status, response) = await service.CreateSeedAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateSeed_ExceedsMaxPerOwner_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var seedType = CreateTestSeedType(maxPerOwner: 1);

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SetupSeedQueryPagedAsync(new List<SeedModel> { CreateTestSeed() }, 1);

        var request = new CreateSeedRequest
        {
            OwnerId = _testOwnerId,
            OwnerType = "character",
            SeedTypeCode = "guardian",
            GameServiceId = _testGameServiceId
        };

        // Act
        var (status, response) = await service.CreateSeedAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region GetSeed Tests

    [Fact]
    public async Task GetSeed_Exists_ReturnsSeed()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        // Act
        var (status, response) = await service.GetSeedAsync(
            new GetSeedRequest { SeedId = seedId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(seedId, response.SeedId);
    }

    [Fact]
    public async Task GetSeed_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockSeedStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        // Act
        var (status, response) = await service.GetSeedAsync(
            new GetSeedRequest { SeedId = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ActivateSeed Tests

    [Fact]
    public async Task ActivateSeed_DeactivatesPreviousAndActivatesTarget()
    {
        // Arrange
        var service = CreateService();
        var targetSeedId = Guid.NewGuid();
        var previousSeedId = Guid.NewGuid();
        var targetSeed = CreateTestSeed(seedId: targetSeedId, status: SeedStatus.Dormant);
        var previousSeed = CreateTestSeed(seedId: previousSeedId, status: SeedStatus.Active);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{targetSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetSeed);

        SetupSeedQueryPagedAsync(new List<SeedModel> { previousSeed }, 1);

        SeedModel? savedTarget = null;
        SeedModel? savedPrevious = null;
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedModel, StateOptions?, CancellationToken>((key, m, _, _) =>
            {
                if (key == $"seed:{targetSeedId}") savedTarget = m;
                if (key == $"seed:{previousSeedId}") savedPrevious = m;
            })
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.ActivateSeedAsync(
            new ActivateSeedRequest { SeedId = targetSeedId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(SeedStatus.Active, response.Status);

        Assert.NotNull(savedTarget);
        Assert.Equal(SeedStatus.Active, savedTarget.Status);

        Assert.NotNull(savedPrevious);
        Assert.Equal(SeedStatus.Dormant, savedPrevious.Status);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.activated",
            It.Is<SeedActivatedEvent>(e => e.SeedId == targetSeedId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveSeed_ActiveSeed_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        // Act
        var (status, response) = await service.ArchiveSeedAsync(
            new ArchiveSeedRequest { SeedId = seedId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region Growth Tests

    [Fact]
    public async Task RecordGrowth_NewDomain_CreatesDomainEntry()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");

        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new RecordGrowthRequest
        {
            SeedId = seedId,
            Domain = "combat.melee.sword",
            Amount = 3.5f,
            Source = "character-encounter"
        };

        // Act
        var (status, response) = await service.RecordGrowthAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3.5f, response.TotalGrowth);
        Assert.True(response.Domains.ContainsKey("combat.melee.sword"));
        Assert.Equal(3.5f, response.Domains["combat.melee.sword"]);

        Assert.NotNull(savedGrowth);
        Assert.Equal(3.5f, savedGrowth.Domains["combat.melee.sword"].Depth);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.growth.updated",
            It.Is<SeedGrowthUpdatedEvent>(e => e.SeedId == seedId && e.Domain == "combat.melee.sword"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordGrowth_ExistingDomain_IncrementsDepth()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();
        var existingGrowth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry> { { "combat.melee", new DomainGrowthEntry { Depth = 5.0f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 5.0f } } }
        };

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingGrowth);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 2.0f, Source = "test" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedGrowth);
        Assert.Equal(7.0f, savedGrowth.Domains["combat.melee"].Depth);
    }

    [Fact]
    public async Task RecordGrowth_CrossesPhaseThreshold_PublishesPhaseChangedEvent()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        seed.GrowthPhase = "nascent";
        seed.TotalGrowth = 8f;
        var seedType = CreateTestSeedType();

        var existingGrowth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry> { { "combat.melee", new DomainGrowthEntry { Depth = 8f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 8f } } }
        };

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingGrowth);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockGrowthStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act - add 3 growth to cross the 10-threshold for "awakening"
        var (status, _) = await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 3f, Source = "test" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.phase.changed",
            It.Is<SeedPhaseChangedEvent>(e =>
                e.SeedId == seedId &&
                e.PreviousPhase == "nascent" &&
                e.NewPhase == "awakening"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordGrowthBatch_MultipleDomainsAtomically()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new RecordGrowthBatchRequest
        {
            SeedId = seedId,
            Entries = new List<GrowthEntry>
            {
                new GrowthEntry { Domain = "combat.melee", Amount = 2f },
                new GrowthEntry { Domain = "magic.fire", Amount = 3f }
            },
            Source = "test"
        };

        // Act
        var (status, response) = await service.RecordGrowthBatchAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(5f, response.TotalGrowth);
        Assert.NotNull(savedGrowth);
        Assert.Equal(2f, savedGrowth.Domains["combat.melee"].Depth);
        Assert.Equal(3f, savedGrowth.Domains["magic.fire"].Depth);
    }

    #endregion

    #region Capability Tests

    [Fact]
    public async Task GetCapabilityManifest_CacheHit_ReturnsCachedVersion()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var cached = new CapabilityManifestModel
        {
            SeedId = seedId,
            SeedTypeCode = "guardian",
            ComputedAt = DateTimeOffset.UtcNow,
            Version = 3,
            Capabilities = new List<CapabilityEntry>
            {
                new CapabilityEntry { CapabilityCode = "combat.stance", Domain = "combat.melee", Fidelity = 0.5f, Unlocked = true }
            }
        };

        _mockCapabilitiesStore
            .Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        // Act
        var (status, response) = await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.Version);
        Assert.Single(response.Capabilities);
    }

    [Fact]
    public async Task GetCapabilityManifest_ComputesFromGrowthAndRules()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();
        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry> { { "combat.melee", new DomainGrowthEntry { Depth = 8f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 8f } } }
        };

        _mockCapabilitiesStore
            .Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapabilityManifestModel?)null);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growth);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        CapabilityManifestModel? savedManifest = null;
        _mockCapabilitiesStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CapabilityManifestModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CapabilityManifestModel, StateOptions?, CancellationToken>((_, m, _, _) => savedManifest = m)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Capabilities);
        Assert.True(response.Capabilities.First().Unlocked);
        Assert.True(response.Capabilities.First().Fidelity > 0f);

        Assert.NotNull(savedManifest);
        Assert.Equal(1, savedManifest.Version);
    }

    [Fact]
    public async Task GetCapabilityManifest_LinearFidelity_CorrectCalculation()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        // Linear formula: threshold=5, depth=7.5 → normalized=1.5 → fidelity=(1.5-1.0)/1.0=0.5
        var seedType = CreateTestSeedType();
        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry> { { "combat.melee", new DomainGrowthEntry { Depth = 7.5f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 7.5f } } }
        };

        _mockCapabilitiesStore
            .Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapabilityManifestModel?)null);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growth);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockCapabilitiesStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CapabilityManifestModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        var cap = Assert.Single(response.Capabilities);
        Assert.True(cap.Unlocked);
        Assert.Equal(0.5f, cap.Fidelity, precision: 3);
    }

    [Fact]
    public async Task GetCapabilityManifest_LogarithmicFidelity_CorrectCalculation()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        // Logarithmic formula: threshold=5, depth=5 → normalized=1.0 → log(2)/log(2)=1.0
        var seedType = CreateTestSeedType();
        seedType.CapabilityRules = new List<CapabilityRule>
        {
            new CapabilityRule { CapabilityCode = "combat.stance", Domain = "combat.melee", UnlockThreshold = 5f, FidelityFormula = "logarithmic" }
        };
        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry> { { "combat.melee", new DomainGrowthEntry { Depth = 5f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 5f } } }
        };

        _mockCapabilitiesStore
            .Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapabilityManifestModel?)null);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growth);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockCapabilitiesStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CapabilityManifestModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        // Assert - log(1 + 1.0) / log(2) = log(2)/log(2) = 1.0
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        var cap = Assert.Single(response.Capabilities);
        Assert.True(cap.Unlocked);
        Assert.Equal(1.0f, cap.Fidelity, precision: 3);
    }

    #endregion

    #region Type Definition Tests

    [Fact]
    public async Task RegisterSeedType_ValidRequest_SavesAndReturns()
    {
        // Arrange
        var service = CreateService();
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedTypeDefinitionModel?)null);

        SetupTypeQueryPagedAsync(new List<SeedTypeDefinitionModel>(), 0);

        SeedTypeDefinitionModel? savedType = null;
        _mockTypeStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedTypeDefinitionModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedTypeDefinitionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedType = m)
            .ReturnsAsync("etag");

        var request = new RegisterSeedTypeRequest
        {
            SeedTypeCode = "guardian",
            GameServiceId = _testGameServiceId,
            DisplayName = "Guardian",
            Description = "A guardian seed type",
            MaxPerOwner = 3,
            AllowedOwnerTypes = new List<string> { "character" },
            GrowthPhases = new List<GrowthPhaseDefinition>
            {
                new GrowthPhaseDefinition { PhaseCode = "nascent", DisplayName = "Nascent", MinTotalGrowth = 0 }
            },
            BondCardinality = 1,
            BondPermanent = false
        };

        // Act
        var (status, response) = await service.RegisterSeedTypeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("guardian", response.SeedTypeCode);

        Assert.NotNull(savedType);
        Assert.Equal("guardian", savedType.SeedTypeCode);
        Assert.Equal(3, savedType.MaxPerOwner);
    }

    [Fact]
    public async Task RegisterSeedType_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var existing = CreateTestSeedType();

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var request = new RegisterSeedTypeRequest
        {
            SeedTypeCode = "guardian",
            GameServiceId = _testGameServiceId,
            DisplayName = "Guardian",
            Description = "A guardian seed type",
            MaxPerOwner = 3,
            AllowedOwnerTypes = new List<string> { "character" },
            GrowthPhases = new List<GrowthPhaseDefinition>
            {
                new GrowthPhaseDefinition { PhaseCode = "nascent", DisplayName = "Nascent", MinTotalGrowth = 0 }
            }
        };

        // Act
        var (status, response) = await service.RegisterSeedTypeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterSeedType_ExceedsMaxTypesPerGameService_ReturnsConflict()
    {
        // Arrange
        Configuration.MaxSeedTypesPerGameService = 1;
        var service = CreateService();

        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedTypeDefinitionModel?)null);

        SetupTypeQueryPagedAsync(new List<SeedTypeDefinitionModel> { CreateTestSeedType() }, 1);

        var request = new RegisterSeedTypeRequest
        {
            SeedTypeCode = "new_type",
            GameServiceId = _testGameServiceId,
            DisplayName = "New Type",
            Description = "Another type",
            MaxPerOwner = 3,
            AllowedOwnerTypes = new List<string> { "character" },
            GrowthPhases = new List<GrowthPhaseDefinition>
            {
                new GrowthPhaseDefinition { PhaseCode = "nascent", DisplayName = "Nascent", MinTotalGrowth = 0 }
            }
        };

        // Act
        var (status, response) = await service.RegisterSeedTypeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region Bond Tests

    [Fact]
    public async Task InitiateBond_ValidSeeds_CreatesPendingBond()
    {
        // Arrange
        var service = CreateService();
        var initiatorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var initiator = CreateTestSeed(seedId: initiatorId, status: SeedStatus.Active);
        var target = CreateTestSeed(seedId: targetId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{initiatorId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(initiator);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SeedBondModel? savedBond = null;
        _mockBondStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedBondModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedBondModel, StateOptions?, CancellationToken>((_, m, _, _) => savedBond = m)
            .ReturnsAsync("etag");

        var request = new InitiateBondRequest
        {
            InitiatorSeedId = initiatorId,
            TargetSeedId = targetId
        };

        // Act
        var (status, response) = await service.InitiateBondAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(BondStatus.PendingConfirmation, response.Status);

        Assert.NotNull(savedBond);
        Assert.Equal(2, savedBond.Participants.Count);
        Assert.Equal(BondStatus.PendingConfirmation, savedBond.Status);
    }

    [Fact]
    public async Task InitiateBond_DifferentTypes_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var initiatorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var initiator = CreateTestSeed(seedId: initiatorId, seedTypeCode: "guardian");
        var target = CreateTestSeed(seedId: targetId, seedTypeCode: "dungeon_core");

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{initiatorId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(initiator);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        var request = new InitiateBondRequest
        {
            InitiatorSeedId = initiatorId,
            TargetSeedId = targetId
        };

        // Act
        var (status, response) = await service.InitiateBondAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task InitiateBond_AlreadyBonded_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var initiatorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var initiator = CreateTestSeed(seedId: initiatorId, status: SeedStatus.Active);
        initiator.BondId = Guid.NewGuid();

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{initiatorId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(initiator);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestSeed(seedId: targetId, status: SeedStatus.Active));
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestSeedType());

        var request = new InitiateBondRequest
        {
            InitiatorSeedId = initiatorId,
            TargetSeedId = targetId
        };

        // Act
        var (status, response) = await service.InitiateBondAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ConfirmBond_AllConfirmed_ActivatesBondAndUpdateSeeds()
    {
        // Arrange
        var service = CreateService();
        var bondId = Guid.NewGuid();
        var seed1Id = Guid.NewGuid();
        var seed2Id = Guid.NewGuid();
        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.PendingConfirmation,
            Participants = new List<BondParticipantEntry>
            {
                new BondParticipantEntry { SeedId = seed1Id, JoinedAt = DateTimeOffset.UtcNow, Role = "initiator", Confirmed = true },
                new BondParticipantEntry { SeedId = seed2Id, JoinedAt = DateTimeOffset.UtcNow, Confirmed = false }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            Permanent = false
        };

        var seed1 = CreateTestSeed(seedId: seed1Id);
        var seed2 = CreateTestSeed(seedId: seed2Id);

        _mockBondStore
            .Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bond);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seed1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed1);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seed2Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed2);

        SeedBondModel? savedBond = null;
        _mockBondStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedBondModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedBondModel, StateOptions?, CancellationToken>((_, m, _, _) => savedBond = m)
            .ReturnsAsync("etag");
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new ConfirmBondRequest
        {
            BondId = bondId,
            ConfirmingSeedId = seed2Id
        };

        // Act
        var (status, response) = await service.ConfirmBondAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(BondStatus.Active, response.Status);

        Assert.NotNull(savedBond);
        Assert.Equal(BondStatus.Active, savedBond.Status);
        Assert.True(savedBond.Participants.All(p => p.Confirmed));

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.bond.formed",
            It.Is<SeedBondFormedEvent>(e => e.BondId == bondId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Event Handler Tests

    [Fact]
    public async Task HandleGrowthContributed_ValidEvent_RecordsGrowth()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockGrowthStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var evt = new SeedGrowthContributedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SeedId = seedId,
            Domain = "combat.melee",
            Amount = 5f,
            Source = "character-encounter"
        };

        // Act
        await service.HandleGrowthContributedAsync(evt);

        // Assert - growth was saved
        _mockGrowthStore.Verify(s => s.SaveAsync(
            $"growth:{seedId}",
            It.Is<SeedGrowthModel>(g => g.Domains.ContainsKey("combat.melee")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleGrowthContributed_SeedNotFound_LogsWarning()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        var evt = new SeedGrowthContributedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SeedId = seedId,
            Domain = "combat.melee",
            Amount = 5f,
            Source = "character-encounter"
        };

        // Act
        await service.HandleGrowthContributedAsync(evt);

        // Assert - no growth saved (seed not found)
        _mockGrowthStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
