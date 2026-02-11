using BeyondImmersion.Bannou.Core;
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

    #region Decay Configuration Tests

    [Fact]
    public void ResolveDecayConfig_TypeOverrideEnabled_WinsOverGlobalDisabled()
    {
        // Arrange: global disabled, type enabled
        Configuration.GrowthDecayEnabled = false;
        Configuration.GrowthDecayRatePerDay = 0.01;
        var seedType = CreateTestSeedType();
        seedType.GrowthDecayEnabled = true;
        seedType.GrowthDecayRatePerDay = 0.05f;

        // Act
        var (enabled, rate) = SeedService.ResolveDecayConfig(seedType, Configuration);

        // Assert
        Assert.True(enabled);
        Assert.Equal(0.05f, rate);
    }

    [Fact]
    public void ResolveDecayConfig_TypeOverrideDisabled_WinsOverGlobalEnabled()
    {
        // Arrange: global enabled, type explicitly disabled
        Configuration.GrowthDecayEnabled = true;
        Configuration.GrowthDecayRatePerDay = 0.01;
        var seedType = CreateTestSeedType();
        seedType.GrowthDecayEnabled = false;
        seedType.GrowthDecayRatePerDay = null;

        // Act
        var (enabled, _) = SeedService.ResolveDecayConfig(seedType, Configuration);

        // Assert
        Assert.False(enabled);
    }

    [Fact]
    public void ResolveDecayConfig_NullTypeOverrides_FallsBackToGlobal()
    {
        // Arrange: type has no overrides, global config used
        Configuration.GrowthDecayEnabled = true;
        Configuration.GrowthDecayRatePerDay = 0.02;
        var seedType = CreateTestSeedType();
        seedType.GrowthDecayEnabled = null;
        seedType.GrowthDecayRatePerDay = null;

        // Act
        var (enabled, rate) = SeedService.ResolveDecayConfig(seedType, Configuration);

        // Assert
        Assert.True(enabled);
        Assert.Equal(0.02f, rate);
    }

    [Fact]
    public void ResolveDecayConfig_NullSeedType_FallsBackToGlobal()
    {
        // Arrange: null seed type (defensive path)
        Configuration.GrowthDecayEnabled = true;
        Configuration.GrowthDecayRatePerDay = 0.03;

        // Act
        var (enabled, rate) = SeedService.ResolveDecayConfig(null, Configuration);

        // Assert
        Assert.True(enabled);
        Assert.Equal(0.03f, rate);
    }

    [Fact]
    public void ResolveDecayConfig_TypeRateOverride_WinsOverGlobalRate()
    {
        // Arrange: type has only rate override, enabled falls back to global
        Configuration.GrowthDecayEnabled = true;
        Configuration.GrowthDecayRatePerDay = 0.01;
        var seedType = CreateTestSeedType();
        seedType.GrowthDecayEnabled = null;
        seedType.GrowthDecayRatePerDay = 0.1f;

        // Act
        var (enabled, rate) = SeedService.ResolveDecayConfig(seedType, Configuration);

        // Assert
        Assert.True(enabled);
        Assert.Equal(0.1f, rate);
    }

    #endregion

    #region PeakDepth Tracking Tests

    [Fact]
    public async Task RecordGrowth_NewDomain_PeakDepthEqualsDepth()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 5f, Source = "test" },
            CancellationToken.None);

        // Assert
        Assert.NotNull(savedGrowth);
        var entry = savedGrowth.Domains["combat.melee"];
        Assert.Equal(5f, entry.Depth);
        Assert.Equal(5f, entry.PeakDepth);
    }

    [Fact]
    public async Task RecordGrowth_ExistingDomain_PeakDepthRetainedWhenBelowHistorical()
    {
        // Arrange: depth was decayed from 10 to 7, PeakDepth preserved at 10
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();

        var existingGrowth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 7f, LastActivityAt = DateTimeOffset.UtcNow.AddDays(-1), PeakDepth = 10f } }
            }
        };

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(existingGrowth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act: add 2f → depth=9f, but PeakDepth should stay at 10f
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 2f, Source = "test" },
            CancellationToken.None);

        // Assert
        Assert.NotNull(savedGrowth);
        var entry = savedGrowth.Domains["combat.melee"];
        Assert.Equal(9f, entry.Depth);
        Assert.Equal(10f, entry.PeakDepth);
    }

    [Fact]
    public async Task RecordGrowth_ExistingDomain_PeakDepthIncreasesWhenExceeded()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();

        var existingGrowth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 8f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 8f } }
            }
        };

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(existingGrowth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act: add 5f → depth=13f exceeds PeakDepth=8f
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 5f, Source = "test" },
            CancellationToken.None);

        // Assert
        Assert.NotNull(savedGrowth);
        var entry = savedGrowth.Domains["combat.melee"];
        Assert.Equal(13f, entry.Depth);
        Assert.Equal(13f, entry.PeakDepth);
    }

    #endregion

    #region Bond Shared Activity Tests

    [Fact]
    public async Task RecordGrowth_PermanentBond_ResetsPartnerDecayTimerOnMatchingDomainsOnly()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var partnerId = Guid.NewGuid();
        var bondId = Guid.NewGuid();

        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        seed.BondId = bondId;
        var seedType = CreateTestSeedType();

        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.Active,
            Permanent = true,
            Participants = new List<BondParticipantEntry>
            {
                new() { SeedId = seedId, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true },
                new() { SeedId = partnerId, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            BondStrength = 1f,
            SharedGrowth = 10f
        };

        var oldTime = DateTimeOffset.UtcNow.AddDays(-5);
        var partnerGrowth = new SeedGrowthModel
        {
            SeedId = partnerId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 4f, LastActivityAt = oldTime, PeakDepth = 4f } },
                { "magic.fire", new DomainGrowthEntry { Depth = 2f, LastActivityAt = oldTime, PeakDepth = 2f } }
            }
        };

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedGrowthModel { SeedId = seedId, Domains = new() });
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{partnerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(partnerGrowth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockBondStore.Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>())).ReturnsAsync(bond);

        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockBondStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedBondModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Act: record growth only in "combat.melee"
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 3f, Source = "test" },
            CancellationToken.None);

        // Assert: partner's combat.melee timer was reset, magic.fire was NOT
        _mockGrowthStore.Verify(s => s.SaveAsync(
            $"growth:{partnerId}",
            It.Is<SeedGrowthModel>(g =>
                g.Domains["combat.melee"].LastActivityAt > oldTime &&
                g.Domains["magic.fire"].LastActivityAt == oldTime),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordGrowth_NonPermanentBond_DoesNotResetPartnerDecayTimer()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var partnerId = Guid.NewGuid();
        var bondId = Guid.NewGuid();

        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        seed.BondId = bondId;
        var seedType = CreateTestSeedType();

        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.Active,
            Permanent = false,
            Participants = new List<BondParticipantEntry>
            {
                new() { SeedId = seedId, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true },
                new() { SeedId = partnerId, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            BondStrength = 1f,
            SharedGrowth = 10f
        };

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedGrowthModel { SeedId = seedId, Domains = new() });
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockBondStore.Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>())).ReturnsAsync(bond);

        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockBondStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedBondModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 3f, Source = "test" },
            CancellationToken.None);

        // Assert: partner growth was never read (non-permanent bond skips shared activity)
        _mockGrowthStore.Verify(s => s.GetAsync($"growth:{partnerId}", It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Phase Direction Tests

    [Fact]
    public async Task RecordGrowth_PhaseProgression_PublishesProgressedDirection()
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
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 8f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 8f } }
            }
        };

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(existingGrowth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Act: cross the 10-threshold into "awakening"
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 3f, Source = "test" },
            CancellationToken.None);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.phase.changed",
            It.Is<SeedPhaseChangedEvent>(e =>
                e.Direction == PhaseChangeDirection.Progressed &&
                e.PreviousPhase == "nascent" &&
                e.NewPhase == "awakening"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    // Event handler tests for seed.growth.contributed removed.
    // Collection→Seed growth pipeline now uses ICollectionUnlockListener DI provider pattern.
    // See SeedCollectionUnlockListener for the implementation.

    #region Cross-Pollination Tests

    [Fact]
    public async Task RecordGrowth_MultiplierGreaterThanZero_AppliesGrowthToSiblings()
    {
        // Arrange
        var service = CreateService();
        var primarySeedId = Guid.NewGuid();
        var siblingSeedId = Guid.NewGuid();

        var primarySeed = CreateTestSeed(seedId: primarySeedId, status: SeedStatus.Active);
        var siblingSeed = CreateTestSeed(seedId: siblingSeedId, status: SeedStatus.Active);

        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0.5f;

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primarySeed);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{siblingSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(siblingSeed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{siblingSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        // Sibling query returns both seeds (primary will be skipped)
        SetupSeedQueryPagedAsync(new List<SeedModel> { primarySeed, siblingSeed }, 2);

        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        SeedGrowthModel? savedSiblingGrowth = null;
        _mockGrowthStore
            .Setup(s => s.SaveAsync($"growth:{siblingSeedId}", It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedSiblingGrowth = m)
            .ReturnsAsync("etag");
        _mockGrowthStore
            .Setup(s => s.SaveAsync($"growth:{primarySeedId}", It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act: record 10.0 growth on primary
        var (status, _) = await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = primarySeedId, Domain = "combat.melee", Amount = 10f, Source = "test" },
            CancellationToken.None);

        // Assert: sibling received 10.0 * 0.5 = 5.0 growth
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedSiblingGrowth);
        Assert.Equal(5.0f, savedSiblingGrowth.Domains["combat.melee"].Depth);
    }

    [Fact]
    public async Task RecordGrowth_MultiplierZero_DoesNotQuerySiblings()
    {
        // Arrange
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0f;

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 5f, Source = "test" },
            CancellationToken.None);

        // Assert: no sibling query was made
        _mockSeedQueryStore.Verify(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>?>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordGrowth_CrossPollination_ArchivedSiblingExcluded()
    {
        // Arrange
        var service = CreateService();
        var primarySeedId = Guid.NewGuid();
        var archivedSeedId = Guid.NewGuid();

        var primarySeed = CreateTestSeed(seedId: primarySeedId, status: SeedStatus.Active);
        var archivedSeed = CreateTestSeed(seedId: archivedSeedId, status: SeedStatus.Archived);

        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0.5f;

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primarySeed);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{archivedSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(archivedSeed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        // Sibling query returns the archived seed (it somehow got through the query filter)
        SetupSeedQueryPagedAsync(new List<SeedModel> { primarySeed, archivedSeed }, 2);

        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = primarySeedId, Domain = "combat.melee", Amount = 10f, Source = "test" },
            CancellationToken.None);

        // Assert: no growth saved for the archived sibling
        _mockGrowthStore.Verify(s => s.SaveAsync(
            $"growth:{archivedSeedId}",
            It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordGrowth_CrossPollination_DormantSiblingReceivesGrowth()
    {
        // Arrange
        var service = CreateService();
        var primarySeedId = Guid.NewGuid();
        var dormantSeedId = Guid.NewGuid();

        var primarySeed = CreateTestSeed(seedId: primarySeedId, status: SeedStatus.Active);
        var dormantSeed = CreateTestSeed(seedId: dormantSeedId, status: SeedStatus.Dormant);

        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0.25f;

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primarySeed);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{dormantSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dormantSeed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{dormantSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SetupSeedQueryPagedAsync(new List<SeedModel> { primarySeed, dormantSeed }, 2);

        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        SeedGrowthModel? savedDormantGrowth = null;
        _mockGrowthStore
            .Setup(s => s.SaveAsync($"growth:{dormantSeedId}", It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedDormantGrowth = m)
            .ReturnsAsync("etag");
        _mockGrowthStore
            .Setup(s => s.SaveAsync($"growth:{primarySeedId}", It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act: record 8.0 growth
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = primarySeedId, Domain = "magic.fire", Amount = 8f, Source = "test" },
            CancellationToken.None);

        // Assert: dormant sibling received 8.0 * 0.25 = 2.0
        Assert.NotNull(savedDormantGrowth);
        Assert.Equal(2.0f, savedDormantGrowth.Domains["magic.fire"].Depth);
    }

    [Fact]
    public async Task RecordGrowth_CrossPollination_UsesRawAmountsNotBondBoosted()
    {
        // Arrange
        var service = CreateService();
        var primarySeedId = Guid.NewGuid();
        var siblingSeedId = Guid.NewGuid();
        var bondId = Guid.NewGuid();

        var primarySeed = CreateTestSeed(seedId: primarySeedId, status: SeedStatus.Active);
        primarySeed.BondId = bondId;
        var siblingSeed = CreateTestSeed(seedId: siblingSeedId, status: SeedStatus.Active);

        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 1.0f; // Full mirror for easy math

        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.Active,
            Permanent = false,
            Participants = new List<BondParticipantEntry>
            {
                new() { SeedId = primarySeedId, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true },
                new() { SeedId = Guid.NewGuid(), JoinedAt = DateTimeOffset.UtcNow, Confirmed = true }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            BondStrength = 1f,
            SharedGrowth = 0f
        };

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primarySeed);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{siblingSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(siblingSeed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{siblingSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockBondStore
            .Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bond);

        SetupSeedQueryPagedAsync(new List<SeedModel> { primarySeed, siblingSeed }, 2);

        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockBondStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedBondModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore
            .Setup(s => s.SaveAsync($"growth:{primarySeedId}", It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        SeedGrowthModel? savedSiblingGrowth = null;
        _mockGrowthStore
            .Setup(s => s.SaveAsync($"growth:{siblingSeedId}", It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedSiblingGrowth = m)
            .ReturnsAsync("etag");

        // Act: record 4.0 growth (bond multiplier is 1.5x, so primary gets 6.0, but sibling should get 4.0 * 1.0 = 4.0 raw)
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = primarySeedId, Domain = "combat.melee", Amount = 4f, Source = "test" },
            CancellationToken.None);

        // Assert: sibling got raw 4.0 (not bond-boosted 6.0)
        Assert.NotNull(savedSiblingGrowth);
        Assert.Equal(4.0f, savedSiblingGrowth.Domains["combat.melee"].Depth);
    }

    [Fact]
    public async Task RecordGrowth_CrossPollination_EventsHaveCrossPollinatedTrue()
    {
        // Arrange
        var service = CreateService();
        var primarySeedId = Guid.NewGuid();
        var siblingSeedId = Guid.NewGuid();

        var primarySeed = CreateTestSeed(seedId: primarySeedId, status: SeedStatus.Active);
        var siblingSeed = CreateTestSeed(seedId: siblingSeedId, status: SeedStatus.Active);

        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0.5f;

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primarySeed);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{siblingSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(siblingSeed);
        _mockGrowthStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SetupSeedQueryPagedAsync(new List<SeedModel> { primarySeed, siblingSeed }, 2);

        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = primarySeedId, Domain = "combat.melee", Amount = 5f, Source = "test" },
            CancellationToken.None);

        // Assert: primary seed's event has CrossPollinated = false
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.growth.updated",
            It.Is<SeedGrowthUpdatedEvent>(e =>
                e.SeedId == primarySeedId &&
                e.CrossPollinated == false),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert: sibling seed's event has CrossPollinated = true
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.growth.updated",
            It.Is<SeedGrowthUpdatedEvent>(e =>
                e.SeedId == siblingSeedId &&
                e.CrossPollinated == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordGrowth_CrossPollination_LockFailure_SkipsSibling()
    {
        // Arrange
        var service = CreateService();
        var primarySeedId = Guid.NewGuid();
        var siblingSeedId = Guid.NewGuid();

        var primarySeed = CreateTestSeed(seedId: primarySeedId, status: SeedStatus.Active);
        var siblingSeed = CreateTestSeed(seedId: siblingSeedId, status: SeedStatus.Active);

        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0.5f;

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primarySeed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SetupSeedQueryPagedAsync(new List<SeedModel> { primarySeed, siblingSeed }, 2);

        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Set up lock to succeed for primary but fail for sibling's try-lock
        var primaryLock = new Mock<ILockResponse>();
        primaryLock.Setup(r => r.Success).Returns(true);
        var siblingLock = new Mock<ILockResponse>();
        siblingLock.Setup(r => r.Success).Returns(false);

        var lockCallCount = 0;
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                lockCallCount++;
                // First call is for the primary seed (10s timeout), second for sibling (3s timeout)
                return lockCallCount == 1 ? primaryLock.Object : siblingLock.Object;
            });

        // Act
        var (status, _) = await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = primarySeedId, Domain = "combat.melee", Amount = 10f, Source = "test" },
            CancellationToken.None);

        // Assert: primary succeeds, sibling growth not saved
        Assert.Equal(StatusCodes.OK, status);
        _mockGrowthStore.Verify(s => s.SaveAsync(
            $"growth:{siblingSeedId}",
            It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordGrowth_CrossPollination_SiblingPhaseTransition()
    {
        // Arrange
        var service = CreateService();
        var primarySeedId = Guid.NewGuid();
        var siblingSeedId = Guid.NewGuid();

        var primarySeed = CreateTestSeed(seedId: primarySeedId, status: SeedStatus.Active);
        var siblingSeed = CreateTestSeed(seedId: siblingSeedId, status: SeedStatus.Active);
        siblingSeed.GrowthPhase = "nascent";
        siblingSeed.TotalGrowth = 8f;

        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 1.0f; // Full mirror

        var existingSiblingGrowth = new SeedGrowthModel
        {
            SeedId = siblingSeedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 8f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 8f } }
            }
        };

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(primarySeed);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{siblingSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(siblingSeed);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{primarySeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{siblingSeedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSiblingGrowth);
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SetupSeedQueryPagedAsync(new List<SeedModel> { primarySeed, siblingSeed }, 2);

        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Act: record 3.0 growth — sibling has 8.0 + 3.0 = 11.0, crossing "awakening" threshold at 10
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = primarySeedId, Domain = "combat.melee", Amount = 3f, Source = "test" },
            CancellationToken.None);

        // Assert: sibling phase change event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.phase.changed",
            It.Is<SeedPhaseChangedEvent>(e =>
                e.SeedId == siblingSeedId &&
                e.PreviousPhase == "nascent" &&
                e.NewPhase == "awakening" &&
                e.Direction == PhaseChangeDirection.Progressed),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert: sibling capability cache invalidated
        _mockCapabilitiesStore.Verify(s => s.DeleteAsync(
            $"cap:{siblingSeedId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CreateSeed - Deprecated Type

    [Fact]
    public async Task CreateSeed_DeprecatedSeedType_ReturnsBadRequest()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        seedType.IsDeprecated = true;
        seedType.DeprecatedAt = DateTimeOffset.UtcNow;
        seedType.DeprecationReason = "Superseded";

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        var request = new CreateSeedRequest
        {
            OwnerId = _testOwnerId,
            OwnerType = "character",
            SeedTypeCode = "guardian",
            GameServiceId = _testGameServiceId
        };

        var (status, response) = await service.CreateSeedAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateSeed_GameServiceNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var request = new CreateSeedRequest
        {
            OwnerId = _testOwnerId,
            OwnerType = "character",
            SeedTypeCode = "guardian",
            GameServiceId = Guid.NewGuid()
        };

        var (status, response) = await service.CreateSeedAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateSeed_WithMetadata_WrapsInDataKey()
    {
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

        var metadata = new Dictionary<string, object> { ["element"] = "fire" };
        var request = new CreateSeedRequest
        {
            OwnerId = _testOwnerId,
            OwnerType = "character",
            SeedTypeCode = "guardian",
            GameServiceId = _testGameServiceId,
            Metadata = metadata
        };

        await service.CreateSeedAsync(request, CancellationToken.None);

        Assert.NotNull(savedSeed);
        Assert.NotNull(savedSeed.Metadata);
        Assert.True(savedSeed.Metadata.ContainsKey("data"));
    }

    [Fact]
    public async Task CreateSeed_UsesDefaultMaxPerOwner_WhenTypeMaxIsZero()
    {
        var service = CreateService();
        Configuration.DefaultMaxSeedsPerOwner = 2;

        var seedType = CreateTestSeedType(maxPerOwner: 0);
        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        // Return 2 existing seeds (should hit the default limit of 2)
        SetupSeedQueryPagedAsync(
            new List<SeedModel> { CreateTestSeed(), CreateTestSeed() }, 2);

        var request = new CreateSeedRequest
        {
            OwnerId = _testOwnerId,
            OwnerType = "character",
            SeedTypeCode = "guardian",
            GameServiceId = _testGameServiceId
        };

        var (status, _) = await service.CreateSeedAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region GetSeedsByOwner Tests

    [Fact]
    public async Task GetSeedsByOwner_ReturnsMatchingSeeds()
    {
        var service = CreateService();
        var seed1 = CreateTestSeed();
        var seed2 = CreateTestSeed();
        SetupSeedQueryPagedAsync(new List<SeedModel> { seed1, seed2 }, 2);

        var (status, response) = await service.GetSeedsByOwnerAsync(
            new GetSeedsByOwnerRequest
            {
                OwnerId = _testOwnerId,
                OwnerType = "character",
                IncludeArchived = false
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Seeds.Count);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task GetSeedsByOwner_WithTypeFilter_IncludesTypeCondition()
    {
        var service = CreateService();
        SetupSeedQueryPagedAsync(new List<SeedModel>(), 0);

        await service.GetSeedsByOwnerAsync(
            new GetSeedsByOwnerRequest
            {
                OwnerId = _testOwnerId,
                OwnerType = "character",
                SeedTypeCode = "guardian",
                IncludeArchived = false
            }, CancellationToken.None);

        _mockSeedQueryStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>>(c =>
                c.Any(q => q.Path == "$.SeedTypeCode" && (string)q.Value == "guardian")),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSeedsByOwner_IncludeArchived_OmitsArchiveFilter()
    {
        var service = CreateService();
        SetupSeedQueryPagedAsync(new List<SeedModel>(), 0);

        await service.GetSeedsByOwnerAsync(
            new GetSeedsByOwnerRequest
            {
                OwnerId = _testOwnerId,
                OwnerType = "character",
                IncludeArchived = true
            }, CancellationToken.None);

        _mockSeedQueryStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>>(c =>
                !c.Any(q => q.Path == "$.Status" && q.Operator == QueryOperator.NotEquals)),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ListSeeds Tests

    [Fact]
    public async Task ListSeeds_AppliesPagination()
    {
        var service = CreateService();
        SetupSeedQueryPagedAsync(new List<SeedModel>(), 0);

        await service.ListSeedsAsync(
            new ListSeedsRequest { Page = 3, PageSize = 25 }, CancellationToken.None);

        // Offset = (3-1) * 25 = 50
        _mockSeedQueryStore.Verify(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>?>(),
            50, 25,
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListSeeds_WithAllFilters_AppliesAllConditions()
    {
        var service = CreateService();
        SetupSeedQueryPagedAsync(new List<SeedModel>(), 0);

        await service.ListSeedsAsync(
            new ListSeedsRequest
            {
                SeedTypeCode = "guardian",
                OwnerType = "character",
                GameServiceId = _testGameServiceId,
                GrowthPhase = "nascent",
                Status = SeedStatus.Active,
                Page = 1,
                PageSize = 10
            }, CancellationToken.None);

        _mockSeedQueryStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>>(c =>
                c.Any(q => q.Path == "$.SeedTypeCode") &&
                c.Any(q => q.Path == "$.OwnerType") &&
                c.Any(q => q.Path == "$.GameServiceId") &&
                c.Any(q => q.Path == "$.GrowthPhase") &&
                c.Any(q => q.Path == "$.Status")),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateSeed Tests

    [Fact]
    public async Task UpdateSeed_ValidUpdate_UpdatesFieldsAndPublishesEvent()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        SeedModel? savedSeed = null;
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedModel, StateOptions?, CancellationToken>((_, m, _, _) => savedSeed = m)
            .ReturnsAsync("etag");

        var request = new UpdateSeedRequest
        {
            SeedId = seedId,
            DisplayName = "New Name",
            Metadata = new Dictionary<string, object> { ["custom"] = "value" }
        };

        var (status, response) = await service.UpdateSeedAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("New Name", response.DisplayName);

        Assert.NotNull(savedSeed);
        Assert.Equal("New Name", savedSeed.DisplayName);
        Assert.NotNull(savedSeed.Metadata);
        Assert.True(savedSeed.Metadata.ContainsKey("data"));

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.updated",
            It.Is<SeedUpdatedEvent>(e =>
                e.SeedId == seedId &&
                e.ChangedFields.Contains("displayName") &&
                e.ChangedFields.Contains("metadata")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateSeed_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockSeedStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        var (status, _) = await service.UpdateSeedAsync(
            new UpdateSeedRequest { SeedId = Guid.NewGuid(), DisplayName = "X" }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdateSeed_LockFailure_ReturnsConflict()
    {
        var service = CreateService();
        var failLock = new Mock<ILockResponse>();
        failLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failLock.Object);

        var (status, _) = await service.UpdateSeedAsync(
            new UpdateSeedRequest { SeedId = Guid.NewGuid(), DisplayName = "X" }, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region ActivateSeed Additional Tests

    [Fact]
    public async Task ActivateSeed_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockSeedStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        var (status, _) = await service.ActivateSeedAsync(
            new ActivateSeedRequest { SeedId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task ActivateSeed_AlreadyActive_ReturnsOkIdempotent()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var (status, response) = await service.ActivateSeedAsync(
            new ActivateSeedRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(SeedStatus.Active, response.Status);

        // No event published for idempotent activation
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.activated",
            It.IsAny<SeedActivatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActivateSeed_ArchivedSeed_ReturnsBadRequest()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Archived);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var (status, _) = await service.ActivateSeedAsync(
            new ActivateSeedRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task ActivateSeed_LockFailure_ReturnsConflict()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Dormant);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var failLock = new Mock<ILockResponse>();
        failLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failLock.Object);

        var (status, _) = await service.ActivateSeedAsync(
            new ActivateSeedRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region ArchiveSeed Additional Tests

    [Fact]
    public async Task ArchiveSeed_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockSeedStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        var (status, _) = await service.ArchiveSeedAsync(
            new ArchiveSeedRequest { SeedId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task ArchiveSeed_AlreadyArchived_ReturnsOkIdempotent()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Archived);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var (status, response) = await service.ArchiveSeedAsync(
            new ArchiveSeedRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ArchiveSeed_DormantSeed_ArchivesAndPublishesEvent()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Dormant);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        SeedModel? savedSeed = null;
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedModel, StateOptions?, CancellationToken>((_, m, _, _) => savedSeed = m)
            .ReturnsAsync("etag");

        var (status, response) = await service.ArchiveSeedAsync(
            new ArchiveSeedRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedSeed);
        Assert.Equal(SeedStatus.Archived, savedSeed.Status);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.archived",
            It.Is<SeedArchivedEvent>(e => e.SeedId == seedId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetGrowth Tests

    [Fact]
    public async Task GetGrowth_Exists_ReturnsGrowthData()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 5f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 5f } },
                { "magic.fire", new DomainGrowthEntry { Depth = 3f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 3f } }
            }
        };

        _mockGrowthStore
            .Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growth);

        var (status, response) = await service.GetGrowthAsync(
            new GetGrowthRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(seedId, response.SeedId);
        Assert.Equal(8f, response.TotalGrowth);
        Assert.Equal(2, response.Domains.Count);
        Assert.Equal(5f, response.Domains["combat.melee"]);
        Assert.Equal(3f, response.Domains["magic.fire"]);
    }

    [Fact]
    public async Task GetGrowth_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockGrowthStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);

        var (status, _) = await service.GetGrowthAsync(
            new GetGrowthRequest { SeedId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region GetGrowthPhase Tests

    [Fact]
    public async Task GetGrowthPhase_ValidSeed_ReturnsPhaseInfo()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        seed.TotalGrowth = 15f;
        seed.GrowthPhase = "awakening";

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var seedType = CreateTestSeedType();
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        var (status, response) = await service.GetGrowthPhaseAsync(
            new GetGrowthPhaseRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("awakening", response.PhaseCode);
        Assert.Equal("Awakening", response.DisplayName);
        Assert.Equal(15f, response.TotalGrowth);
        Assert.Equal("mature", response.NextPhaseCode);
        Assert.Equal(50f, response.NextPhaseThreshold);
    }

    [Fact]
    public async Task GetGrowthPhase_SeedNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockSeedStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        var (status, _) = await service.GetGrowthPhaseAsync(
            new GetGrowthPhaseRequest { SeedId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task GetGrowthPhase_AtMaxPhase_NextIsNull()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        seed.TotalGrowth = 100f;
        seed.GrowthPhase = "mature";

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var seedType = CreateTestSeedType();
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        var (status, response) = await service.GetGrowthPhaseAsync(
            new GetGrowthPhaseRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("mature", response.PhaseCode);
        Assert.Null(response.NextPhaseCode);
        Assert.Null(response.NextPhaseThreshold);
    }

    #endregion

    #region RecordGrowth Additional Tests

    [Fact]
    public async Task RecordGrowth_LockFailure_ReturnsConflict()
    {
        var service = CreateService();
        var failLock = new Mock<ILockResponse>();
        failLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failLock.Object);

        var (status, _) = await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = Guid.NewGuid(), Domain = "combat", Amount = 1f, Source = "test" },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task RecordGrowth_NonActiveSeed_ReturnsBadRequest()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Dormant);

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var (status, _) = await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat", Amount = 1f, Source = "test" },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task RecordGrowth_SeedNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockSeedStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        var (status, _) = await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = Guid.NewGuid(), Domain = "combat", Amount = 1f, Source = "test" },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task RecordGrowth_ActiveBond_AppliesMultiplier()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var bondId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        seed.BondId = bondId;
        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0f;

        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.Active,
            Permanent = false,
            Participants = new List<BondParticipantEntry>
            {
                new() { SeedId = seedId, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true },
                new() { SeedId = Guid.NewGuid(), JoinedAt = DateTimeOffset.UtcNow, Confirmed = true }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            BondStrength = 0f,
            SharedGrowth = 0f
        };

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockBondStore.Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>())).ReturnsAsync(bond);

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockBondStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedBondModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Act: record 4.0 growth with 1.5x bond multiplier = 6.0
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 4f, Source = "test" },
            CancellationToken.None);

        Assert.NotNull(savedGrowth);
        Assert.Equal(6.0f, savedGrowth.Domains["combat.melee"].Depth);
    }

    [Fact]
    public async Task RecordGrowth_PendingBond_DoesNotApplyMultiplier()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var bondId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        seed.BondId = bondId;
        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0f;

        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.PendingConfirmation,
            Permanent = false,
            Participants = new List<BondParticipantEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockBondStore.Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>())).ReturnsAsync(bond);

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Act: record 4.0 growth; pending bond should NOT apply 1.5x multiplier
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 4f, Source = "test" },
            CancellationToken.None);

        Assert.NotNull(savedGrowth);
        Assert.Equal(4.0f, savedGrowth.Domains["combat.melee"].Depth);
    }

    [Fact]
    public async Task RecordGrowth_ActiveBond_UpdatesBondStrengthAndSharedGrowth()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var bondId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        seed.BondId = bondId;
        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0f;

        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.Active,
            Permanent = false,
            Participants = new List<BondParticipantEntry>
            {
                new() { SeedId = seedId, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            BondStrength = 0.5f,
            SharedGrowth = 10f
        };

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockBondStore.Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>())).ReturnsAsync(bond);

        SeedBondModel? savedBond = null;
        _mockBondStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedBondModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedBondModel, StateOptions?, CancellationToken>((_, m, _, _) => savedBond = m)
            .ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat", Amount = 5f, Source = "test" },
            CancellationToken.None);

        Assert.NotNull(savedBond);
        Assert.Equal(15f, savedBond.SharedGrowth); // 10 + 5
        Assert.Equal(1.0f, savedBond.BondStrength); // 0.5 + 5 * 0.1
    }

    [Fact]
    public async Task RecordGrowth_InvalidatesCapabilityCache()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0f;

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat", Amount = 1f, Source = "test" },
            CancellationToken.None);

        _mockCapabilitiesStore.Verify(s => s.DeleteAsync(
            $"cap:{seedId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Capability Additional Tests

    [Fact]
    public async Task GetCapabilityManifest_StepFidelity_CorrectCalculation()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();
        seedType.CapabilityRules = new List<CapabilityRule>
        {
            new CapabilityRule { CapabilityCode = "combat.stance", Domain = "combat.melee", UnlockThreshold = 5f, FidelityFormula = "step" }
        };

        // depth=5 → normalized=1.0 → step: >=1.0 → 0.5
        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 5f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 5f } }
            }
        };

        _mockCapabilitiesStore.Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapabilityManifestModel?)null);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(growth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockCapabilitiesStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CapabilityManifestModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        var (status, response) = await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        var cap = Assert.Single(response.Capabilities);
        Assert.True(cap.Unlocked);
        Assert.Equal(0.5f, cap.Fidelity, precision: 3);
    }

    [Fact]
    public async Task GetCapabilityManifest_StepFidelity_AtDoubleThreshold_ReturnsFull()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();
        seedType.CapabilityRules = new List<CapabilityRule>
        {
            new CapabilityRule { CapabilityCode = "combat.stance", Domain = "combat.melee", UnlockThreshold = 5f, FidelityFormula = "step" }
        };

        // depth=10 → normalized=2.0 → step: >=2.0 → 1.0
        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 10f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 10f } }
            }
        };

        _mockCapabilitiesStore.Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapabilityManifestModel?)null);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(growth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockCapabilitiesStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CapabilityManifestModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        var (_, response) = await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        Assert.NotNull(response);
        var cap = Assert.Single(response.Capabilities);
        Assert.Equal(1.0f, cap.Fidelity, precision: 3);
    }

    [Fact]
    public async Task GetCapabilityManifest_BelowThreshold_NotUnlocked()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();

        // depth=3 < threshold=5 → not unlocked
        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 3f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 3f } }
            }
        };

        _mockCapabilitiesStore.Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapabilityManifestModel?)null);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(growth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockCapabilitiesStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CapabilityManifestModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        var (status, response) = await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        var cap = Assert.Single(response.Capabilities);
        Assert.False(cap.Unlocked);
        Assert.Equal(0f, cap.Fidelity);
    }

    [Fact]
    public async Task GetCapabilityManifest_SeedNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();

        _mockCapabilitiesStore.Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapabilityManifestModel?)null);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        var (status, _) = await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task GetCapabilityManifest_IncrementsVersionFromExisting()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();

        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 8f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 8f } }
            }
        };

        // Cache miss on first read (force recompute), but existing manifest with version 5 on second read
        var callCount = 0;
        _mockCapabilitiesStore.Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return null; // first call: cache miss
                return new CapabilityManifestModel { SeedId = seedId, Version = 5, ComputedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
            });

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(growth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);

        CapabilityManifestModel? savedManifest = null;
        _mockCapabilitiesStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CapabilityManifestModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CapabilityManifestModel, StateOptions?, CancellationToken>((_, m, _, _) => savedManifest = m)
            .ReturnsAsync("etag");

        await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        Assert.NotNull(savedManifest);
        Assert.Equal(6, savedManifest.Version);
    }

    [Fact]
    public async Task GetCapabilityManifest_NoCapabilityRules_ReturnsEmptyList()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();
        seedType.CapabilityRules = null;

        _mockCapabilitiesStore.Setup(s => s.GetAsync($"cap:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapabilityManifestModel?)null);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedGrowthModel { SeedId = seedId, Domains = new() });
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);
        _mockCapabilitiesStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CapabilityManifestModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        var (status, response) = await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Capabilities);
    }

    #endregion

    #region GetSeedType Tests

    [Fact]
    public async Task GetSeedType_Exists_ReturnsSeedType()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        var (status, response) = await service.GetSeedTypeAsync(
            new GetSeedTypeRequest { SeedTypeCode = "guardian", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("guardian", response.SeedTypeCode);
    }

    [Fact]
    public async Task GetSeedType_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedTypeDefinitionModel?)null);

        var (status, _) = await service.GetSeedTypeAsync(
            new GetSeedTypeRequest { SeedTypeCode = "missing", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ListSeedTypes Tests

    [Fact]
    public async Task ListSeedTypes_ExcludesDeprecatedByDefault()
    {
        var service = CreateService();
        var active = CreateTestSeedType(seedTypeCode: "active");
        var deprecated = CreateTestSeedType(seedTypeCode: "old");
        deprecated.IsDeprecated = true;

        SetupTypeQueryPagedAsync(new List<SeedTypeDefinitionModel> { active, deprecated }, 2);

        var (status, response) = await service.ListSeedTypesAsync(
            new ListSeedTypesRequest { GameServiceId = _testGameServiceId, IncludeDeprecated = false },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.SeedTypes);
        Assert.Equal("active", response.SeedTypes.First().SeedTypeCode);
    }

    [Fact]
    public async Task ListSeedTypes_IncludesDeprecatedWhenRequested()
    {
        var service = CreateService();
        var active = CreateTestSeedType(seedTypeCode: "active");
        var deprecated = CreateTestSeedType(seedTypeCode: "old");
        deprecated.IsDeprecated = true;

        SetupTypeQueryPagedAsync(new List<SeedTypeDefinitionModel> { active, deprecated }, 2);

        var (status, response) = await service.ListSeedTypesAsync(
            new ListSeedTypesRequest { GameServiceId = _testGameServiceId, IncludeDeprecated = true },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.SeedTypes.Count);
    }

    #endregion

    #region UpdateSeedType Tests

    [Fact]
    public async Task UpdateSeedType_ValidUpdate_SavesAndPublishesEvent()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockTypeStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedTypeDefinitionModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        var (status, response) = await service.UpdateSeedTypeAsync(
            new UpdateSeedTypeRequest
            {
                SeedTypeCode = "guardian",
                GameServiceId = _testGameServiceId,
                DisplayName = "Updated Guardian",
                MaxPerOwner = 5
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Guardian", response.DisplayName);
        Assert.Equal(5, response.MaxPerOwner);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed-type.updated",
            It.Is<SeedTypeUpdatedEvent>(e =>
                e.ChangedFields.Contains("displayName") && e.ChangedFields.Contains("maxPerOwner")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateSeedType_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedTypeDefinitionModel?)null);

        var (status, _) = await service.UpdateSeedTypeAsync(
            new UpdateSeedTypeRequest
            {
                SeedTypeCode = "missing",
                GameServiceId = _testGameServiceId,
                DisplayName = "X"
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdateSeedType_LockFailure_ReturnsConflict()
    {
        var service = CreateService();
        var failLock = new Mock<ILockResponse>();
        failLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failLock.Object);

        var (status, _) = await service.UpdateSeedTypeAsync(
            new UpdateSeedTypeRequest
            {
                SeedTypeCode = "guardian",
                GameServiceId = _testGameServiceId,
                DisplayName = "X"
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task UpdateSeedType_PhasesChanged_TriggersRecomputation()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockTypeStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedTypeDefinitionModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Set up a seed to be found during recomputation
        var seedId = Guid.NewGuid();
        var existingSeed = CreateTestSeed(seedId: seedId);
        existingSeed.TotalGrowth = 15f;
        existingSeed.GrowthPhase = "awakening";

        SetupSeedQueryPagedAsync(new List<SeedModel> { existingSeed }, 1);
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag");

        // Update phases: raise awakening threshold to 20 (seed at 15 will regress to nascent)
        var newPhases = new List<GrowthPhaseDefinition>
        {
            new GrowthPhaseDefinition { PhaseCode = "nascent", DisplayName = "Nascent", MinTotalGrowth = 0 },
            new GrowthPhaseDefinition { PhaseCode = "awakening", DisplayName = "Awakening", MinTotalGrowth = 20 },
            new GrowthPhaseDefinition { PhaseCode = "mature", DisplayName = "Mature", MinTotalGrowth = 100 }
        };

        await service.UpdateSeedTypeAsync(
            new UpdateSeedTypeRequest
            {
                SeedTypeCode = "guardian",
                GameServiceId = _testGameServiceId,
                GrowthPhases = newPhases
            }, CancellationToken.None);

        // Seed should have been recomputed and phase change published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.phase.changed",
            It.Is<SeedPhaseChangedEvent>(e =>
                e.SeedId == seedId &&
                e.PreviousPhase == "awakening" &&
                e.NewPhase == "nascent" &&
                e.Direction == PhaseChangeDirection.Regressed),
            It.IsAny<CancellationToken>()), Times.Once);

        // Capability cache invalidated
        _mockCapabilitiesStore.Verify(s => s.DeleteAsync(
            $"cap:{seedId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeprecateSeedType Tests

    [Fact]
    public async Task DeprecateSeedType_ValidType_DeprecatesAndPublishesEvent()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SeedTypeDefinitionModel? savedType = null;
        _mockTypeStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedTypeDefinitionModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedTypeDefinitionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedType = m)
            .ReturnsAsync("etag");

        var (status, response) = await service.DeprecateSeedTypeAsync(
            new DeprecateSeedTypeRequest
            {
                SeedTypeCode = "guardian",
                GameServiceId = _testGameServiceId,
                Reason = "Replaced by archon"
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);

        Assert.NotNull(savedType);
        Assert.True(savedType.IsDeprecated);
        Assert.NotNull(savedType.DeprecatedAt);
        Assert.Equal("Replaced by archon", savedType.DeprecationReason);
    }

    [Fact]
    public async Task DeprecateSeedType_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedTypeDefinitionModel?)null);

        var (status, _) = await service.DeprecateSeedTypeAsync(
            new DeprecateSeedTypeRequest { SeedTypeCode = "missing", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeprecateSeedType_AlreadyDeprecated_ReturnsConflict()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        seedType.IsDeprecated = true;

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        var (status, _) = await service.DeprecateSeedTypeAsync(
            new DeprecateSeedTypeRequest { SeedTypeCode = "guardian", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region UndeprecateSeedType Tests

    [Fact]
    public async Task UndeprecateSeedType_ValidDeprecated_RestoresAndPublishesEvent()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        seedType.IsDeprecated = true;
        seedType.DeprecatedAt = DateTimeOffset.UtcNow;
        seedType.DeprecationReason = "Some reason";

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SeedTypeDefinitionModel? savedType = null;
        _mockTypeStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedTypeDefinitionModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedTypeDefinitionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedType = m)
            .ReturnsAsync("etag");

        var (status, response) = await service.UndeprecateSeedTypeAsync(
            new UndeprecateSeedTypeRequest { SeedTypeCode = "guardian", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsDeprecated);

        Assert.NotNull(savedType);
        Assert.False(savedType.IsDeprecated);
        Assert.Null(savedType.DeprecatedAt);
        Assert.Null(savedType.DeprecationReason);
    }

    [Fact]
    public async Task UndeprecateSeedType_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedTypeDefinitionModel?)null);

        var (status, _) = await service.UndeprecateSeedTypeAsync(
            new UndeprecateSeedTypeRequest { SeedTypeCode = "missing", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UndeprecateSeedType_NotDeprecated_ReturnsConflict()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        seedType.IsDeprecated = false;

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        var (status, _) = await service.UndeprecateSeedTypeAsync(
            new UndeprecateSeedTypeRequest { SeedTypeCode = "guardian", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region DeleteSeedType Tests

    [Fact]
    public async Task DeleteSeedType_ValidDeprecatedNoSeeds_DeletesAndPublishesEvent()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        seedType.IsDeprecated = true;

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SetupSeedQueryPagedAsync(new List<SeedModel>(), 0);

        var status = await service.DeleteSeedTypeAsync(
            new DeleteSeedTypeRequest { SeedTypeCode = "guardian", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        _mockTypeStore.Verify(s => s.DeleteAsync(
            $"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()), Times.Once);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed-type.deleted",
            It.Is<SeedTypeDeletedEvent>(e => e.SeedTypeCode == "guardian"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSeedType_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedTypeDefinitionModel?)null);

        var status = await service.DeleteSeedTypeAsync(
            new DeleteSeedTypeRequest { SeedTypeCode = "missing", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeleteSeedType_NotDeprecated_ReturnsBadRequest()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        seedType.IsDeprecated = false;

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        var status = await service.DeleteSeedTypeAsync(
            new DeleteSeedTypeRequest { SeedTypeCode = "guardian", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task DeleteSeedType_HasNonArchivedSeeds_ReturnsConflict()
    {
        var service = CreateService();
        var seedType = CreateTestSeedType();
        seedType.IsDeprecated = true;

        _mockTypeStore
            .Setup(s => s.GetAsync($"type:{_testGameServiceId}:guardian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);

        SetupSeedQueryPagedAsync(new List<SeedModel> { CreateTestSeed() }, 1);

        var status = await service.DeleteSeedTypeAsync(
            new DeleteSeedTypeRequest { SeedTypeCode = "guardian", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task DeleteSeedType_LockFailure_ReturnsConflict()
    {
        var service = CreateService();
        var failLock = new Mock<ILockResponse>();
        failLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failLock.Object);

        var status = await service.DeleteSeedTypeAsync(
            new DeleteSeedTypeRequest { SeedTypeCode = "guardian", GameServiceId = _testGameServiceId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region GetBond Tests

    [Fact]
    public async Task GetBond_Exists_ReturnsBond()
    {
        var service = CreateService();
        var bondId = Guid.NewGuid();
        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.Active,
            Participants = new List<BondParticipantEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockBondStore
            .Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bond);

        var (status, response) = await service.GetBondAsync(
            new GetBondRequest { BondId = bondId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(bondId, response.BondId);
    }

    [Fact]
    public async Task GetBond_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockBondStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedBondModel?)null);

        var (status, _) = await service.GetBondAsync(
            new GetBondRequest { BondId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region GetBondForSeed Tests

    [Fact]
    public async Task GetBondForSeed_SeedNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockSeedStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        var (status, _) = await service.GetBondForSeedAsync(
            new GetBondForSeedRequest { SeedId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task GetBondForSeed_NoBond_ReturnsNotFound()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        seed.BondId = null;

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var (status, _) = await service.GetBondForSeedAsync(
            new GetBondForSeedRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task GetBondForSeed_HasBond_ReturnsBond()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var bondId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        seed.BondId = bondId;

        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.Active,
            Participants = new List<BondParticipantEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockBondStore
            .Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bond);

        var (status, response) = await service.GetBondForSeedAsync(
            new GetBondForSeedRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(bondId, response.BondId);
    }

    #endregion

    #region GetBondPartners Tests

    [Fact]
    public async Task GetBondPartners_ValidBond_ReturnsPartnerSummaries()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var partnerId = Guid.NewGuid();
        var bondId = Guid.NewGuid();

        var seed = CreateTestSeed(seedId: seedId);
        seed.BondId = bondId;

        var partner = CreateTestSeed(seedId: partnerId);
        partner.GrowthPhase = "awakening";

        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.Active,
            Participants = new List<BondParticipantEntry>
            {
                new() { SeedId = seedId, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true },
                new() { SeedId = partnerId, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{partnerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);
        _mockBondStore
            .Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bond);

        var (status, response) = await service.GetBondPartnersAsync(
            new GetBondPartnersRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(bondId, response.BondId);
        Assert.Single(response.Partners);
        Assert.Equal(partnerId, response.Partners.First().SeedId);
        Assert.Equal("awakening", response.Partners.First().GrowthPhase);
    }

    [Fact]
    public async Task GetBondPartners_SeedNotBonded_ReturnsNotFound()
    {
        var service = CreateService();
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        seed.BondId = null;

        _mockSeedStore
            .Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var (status, _) = await service.GetBondPartnersAsync(
            new GetBondPartnersRequest { SeedId = seedId }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ConfirmBond Additional Tests

    [Fact]
    public async Task ConfirmBond_BondNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockBondStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedBondModel?)null);

        var (status, _) = await service.ConfirmBondAsync(
            new ConfirmBondRequest { BondId = Guid.NewGuid(), ConfirmingSeedId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task ConfirmBond_AlreadyActive_ReturnsBadRequest()
    {
        var service = CreateService();
        var bondId = Guid.NewGuid();
        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.Active,
            Participants = new List<BondParticipantEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockBondStore
            .Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bond);

        var (status, _) = await service.ConfirmBondAsync(
            new ConfirmBondRequest { BondId = bondId, ConfirmingSeedId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task ConfirmBond_InvalidParticipant_ReturnsBadRequest()
    {
        var service = CreateService();
        var bondId = Guid.NewGuid();
        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.PendingConfirmation,
            Participants = new List<BondParticipantEntry>
            {
                new() { SeedId = Guid.NewGuid(), JoinedAt = DateTimeOffset.UtcNow, Confirmed = true }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockBondStore
            .Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bond);

        var (status, _) = await service.ConfirmBondAsync(
            new ConfirmBondRequest { BondId = bondId, ConfirmingSeedId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task ConfirmBond_PartialConfirmation_StaysPending()
    {
        var service = CreateService();
        var bondId = Guid.NewGuid();
        var seed1Id = Guid.NewGuid();
        var seed2Id = Guid.NewGuid();
        var seed3Id = Guid.NewGuid();

        var bond = new SeedBondModel
        {
            BondId = bondId,
            SeedTypeCode = "guardian",
            Status = BondStatus.PendingConfirmation,
            Participants = new List<BondParticipantEntry>
            {
                new() { SeedId = seed1Id, JoinedAt = DateTimeOffset.UtcNow, Confirmed = true },
                new() { SeedId = seed2Id, JoinedAt = DateTimeOffset.UtcNow, Confirmed = false },
                new() { SeedId = seed3Id, JoinedAt = DateTimeOffset.UtcNow, Confirmed = false }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockBondStore
            .Setup(s => s.GetAsync($"bond:{bondId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bond);

        SeedBondModel? savedBond = null;
        _mockBondStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedBondModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedBondModel, StateOptions?, CancellationToken>((_, m, _, _) => savedBond = m)
            .ReturnsAsync("etag");

        var (status, response) = await service.ConfirmBondAsync(
            new ConfirmBondRequest { BondId = bondId, ConfirmingSeedId = seed2Id },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(BondStatus.PendingConfirmation, response.Status);

        Assert.NotNull(savedBond);
        Assert.Equal(BondStatus.PendingConfirmation, savedBond.Status);

        // Bond formed event should NOT be published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.bond.formed",
            It.IsAny<SeedBondFormedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmBond_LockFailure_ReturnsConflict()
    {
        var service = CreateService();
        var failLock = new Mock<ILockResponse>();
        failLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failLock.Object);

        var (status, _) = await service.ConfirmBondAsync(
            new ConfirmBondRequest { BondId = Guid.NewGuid(), ConfirmingSeedId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region InitiateBond Additional Tests

    [Fact]
    public async Task InitiateBond_SeedNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockSeedStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedModel?)null);

        var (status, _) = await service.InitiateBondAsync(
            new InitiateBondRequest { InitiatorSeedId = Guid.NewGuid(), TargetSeedId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task InitiateBond_BondCardinalityZero_ReturnsBadRequest()
    {
        var service = CreateService();
        var initiatorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var initiator = CreateTestSeed(seedId: initiatorId, status: SeedStatus.Active);
        var target = CreateTestSeed(seedId: targetId, status: SeedStatus.Active);
        var seedType = CreateTestSeedType();
        seedType.BondCardinality = 0;

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{initiatorId}", It.IsAny<CancellationToken>())).ReturnsAsync(initiator);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{targetId}", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(seedType);

        var (status, _) = await service.InitiateBondAsync(
            new InitiateBondRequest { InitiatorSeedId = initiatorId, TargetSeedId = targetId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion
}
