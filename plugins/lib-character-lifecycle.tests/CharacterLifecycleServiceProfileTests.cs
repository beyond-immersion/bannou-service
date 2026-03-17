using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.CharacterLifecycle;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Worldstate;

namespace BeyondImmersion.BannouService.CharacterLifecycle.Tests;

/// <summary>
/// Unit tests for CharacterLifecycleService profile and heritage endpoints.
/// Tests lifecycle profile CRUD, genetic profile management, offspring simulation, and family tree retrieval.
/// </summary>
public class CharacterLifecycleServiceProfileTests
{
    private readonly Mock<ILogger<CharacterLifecycleService>> _mockLogger;
    private readonly CharacterLifecycleServiceConfiguration _configuration;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IRelationshipClient> _mockRelationshipClient;
    private readonly Mock<ISpeciesClient> _mockSpeciesClient;
    private readonly Mock<IWorldstateClient> _mockWorldstateClient;
    private readonly Mock<IContractClient> _mockContractClient;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ISeedClient> _mockSeedClient;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;
    private readonly Mock<IInventoryClient> _mockInventoryClient;
    private readonly Mock<ICurrencyClient> _mockCurrencyClient;
    private readonly Mock<IServiceProvider> _mockServiceProvider;

    // Constructor-cached store mocks matching service constructor types
    private readonly Mock<IStateStore<LifecycleProfileModel>> _mockProfileStore;
    private readonly Mock<IStateStore<GeneticProfileModel>> _mockGeneticStore;
    private readonly Mock<IStateStore<HeritableTraitTemplateModel>> _mockTraitTemplateStore;
    private readonly Mock<IStateStore<LifecycleManifestModel>> _mockCacheStore;

    public CharacterLifecycleServiceProfileTests()
    {
        _mockLogger = new Mock<ILogger<CharacterLifecycleService>>();
        _configuration = new CharacterLifecycleServiceConfiguration();
        _mockStateStoreFactory = new Mock<IStateStoreFactory> { DefaultValue = DefaultValue.Mock };
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockRelationshipClient = new Mock<IRelationshipClient>();
        _mockSpeciesClient = new Mock<ISpeciesClient>();
        _mockWorldstateClient = new Mock<IWorldstateClient>();
        _mockContractClient = new Mock<IContractClient>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockSeedClient = new Mock<ISeedClient>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();
        _mockInventoryClient = new Mock<IInventoryClient>();
        _mockCurrencyClient = new Mock<ICurrencyClient>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        // Constructor-cached store mocks (properly typed)
        _mockProfileStore = new Mock<IStateStore<LifecycleProfileModel>>();
        _mockGeneticStore = new Mock<IStateStore<GeneticProfileModel>>();
        _mockTraitTemplateStore = new Mock<IStateStore<HeritableTraitTemplateModel>>();
        _mockCacheStore = new Mock<IStateStore<LifecycleManifestModel>>();

        // Wire typed stores to factory
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileModel>(StateStoreDefinitions.CharacterLifecycleProfiles))
            .Returns(_mockProfileStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GeneticProfileModel>(StateStoreDefinitions.CharacterLifecycleHeritage))
            .Returns(_mockGeneticStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<HeritableTraitTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage))
            .Returns(_mockTraitTemplateStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleManifestModel>(StateStoreDefinitions.CharacterLifecycleCache))
            .Returns(_mockCacheStore.Object);

        // Default lock provider behavior - always succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        mockLockResponse.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Default message bus behavior
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Creates the service under test with the full 19-parameter constructor.
    /// </summary>
    private CharacterLifecycleService CreateService()
    {
        return new CharacterLifecycleService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockLockProvider.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object,
            _mockTelemetryProvider.Object,
            _mockCharacterClient.Object,
            _mockRelationshipClient.Object,
            _mockSpeciesClient.Object,
            _mockWorldstateClient.Object,
            _mockContractClient.Object,
            _mockResourceClient.Object,
            _mockSeedClient.Object,
            _mockGameServiceClient.Object,
            _mockInventoryClient.Object,
            _mockCurrencyClient.Object,
            _mockServiceProvider.Object);
    }

    /// <summary>
    /// Creates a test lifecycle profile model with sensible defaults.
    /// </summary>
    private static LifecycleProfileModel CreateTestProfile(
        Guid characterId,
        Guid? gameServiceId = null,
        Guid? realmId = null,
        string speciesCode = "human",
        int birthGameYear = 100,
        int currentAge = 25,
        string currentStage = "adult",
        LifecycleStatus status = LifecycleStatus.Alive)
    {
        return new LifecycleProfileModel
        {
            CharacterId = characterId,
            GameServiceId = gameServiceId ?? Guid.NewGuid(),
            RealmId = realmId ?? Guid.NewGuid(),
            SpeciesCode = speciesCode,
            BirthGameYear = birthGameYear,
            CurrentAge = currentAge,
            CurrentStage = currentStage,
            CauseOfCreation = CreationCause.Seeded,
            ChildCount = 0,
            TotalChildCount = 0,
            FertilityModifier = 1.0f,
            HealthModifier = 1.0f,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #region GetLifecycleProfile

    [Fact]
    public async Task GetLifecycleProfile_ProfileExists_ReturnsOkWithProfile()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var storedProfile = CreateTestProfile(characterId);

        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedProfile);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetLifecycleProfileAsync(
            new GetLifecycleProfileRequest { CharacterId = characterId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.Profile.CharacterId);
        Assert.Equal("human", response.Profile.SpeciesCode);
        Assert.Equal(LifecycleStatus.Alive, response.Profile.Status);
    }

    [Fact]
    public async Task GetLifecycleProfile_ProfileNotFound_ReturnsNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetLifecycleProfileAsync(
            new GetLifecycleProfileRequest { CharacterId = characterId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region SetNaturalDeathYear

    [Fact]
    public async Task SetNaturalDeathYear_ProfileExists_UpdatesAndDeletesCache()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingProfile = CreateTestProfile(characterId);
        existingProfile.NaturalDeathYear = 180;

        _mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingProfile, "etag-1"));

        // Capture saved profile
        LifecycleProfileModel? savedProfile = null;
        _mockProfileStore.Setup(s => s.TrySaveAsync(
                $"profile:{characterId}", It.IsAny<LifecycleProfileModel>(),
                "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LifecycleProfileModel, string, StateOptions?, CancellationToken>((_, m, _, _, _) => savedProfile = m)
            .ReturnsAsync("etag-2");

        // Capture cache deletion
        string? deletedCacheKey = null;
        _mockCacheStore.Setup(s => s.DeleteAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) => deletedCacheKey = k)
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var (status, response) = await service.SetNaturalDeathYearAsync(
            new SetNaturalDeathYearRequest { CharacterId = characterId, NaturalDeathYear = 200 },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedProfile);
        Assert.Equal(200, savedProfile.NaturalDeathYear);
        Assert.Equal($"manifest:{characterId}", deletedCacheKey);
    }

    [Fact]
    public async Task SetNaturalDeathYear_ProfileNotFound_ReturnsNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        _mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((LifecycleProfileModel?)null, (string?)null));

        var service = CreateService();

        // Act
        var (status, response) = await service.SetNaturalDeathYearAsync(
            new SetNaturalDeathYearRequest { CharacterId = characterId, NaturalDeathYear = 200 },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SetNaturalDeathYear_ETagConflict_ReturnsConflict()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingProfile = CreateTestProfile(characterId);

        _mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingProfile, "etag-1"));
        _mockProfileStore.Setup(s => s.TrySaveAsync(
                $"profile:{characterId}", It.IsAny<LifecycleProfileModel>(),
                "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = CreateService();

        // Act
        var (status, response) = await service.SetNaturalDeathYearAsync(
            new SetNaturalDeathYearRequest { CharacterId = characterId, NaturalDeathYear = 200 },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region SeedLifecycleProfile

    [Fact]
    public async Task SeedLifecycleProfile_MultipleCharacters_ReturnsCreatedCount()
    {
        // Arrange
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        // Neither profile exists
        _mockProfileStore.Setup(s => s.GetAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileModel?)null);

        // Capture saves
        var savedKeys = new List<string>();
        _mockProfileStore.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<LifecycleProfileModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LifecycleProfileModel, StateOptions?, CancellationToken>((k, _, _, _) => savedKeys.Add(k))
            .ReturnsAsync("etag-new");

        var service = CreateService();
        var request = new SeedLifecycleProfileRequest
        {
            Characters = new[]
            {
                new SeedCharacterEntry
                {
                    CharacterId = charA, GameServiceId = gameServiceId, RealmId = realmId,
                    SpeciesCode = "human", BirthGameYear = 100, CurrentAge = 20, CurrentStage = "adult"
                },
                new SeedCharacterEntry
                {
                    CharacterId = charB, GameServiceId = gameServiceId, RealmId = realmId,
                    SpeciesCode = "elf", BirthGameYear = 80, CurrentAge = 40, CurrentStage = "adult"
                }
            }
        };

        // Act
        var (status, response) = await service.SeedLifecycleProfileAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.CreatedCount);
        Assert.Contains($"profile:{charA}", savedKeys);
        Assert.Contains($"profile:{charB}", savedKeys);
    }

    [Fact]
    public async Task SeedLifecycleProfile_ExistingProfileSkipped_DoesNotOverwrite()
    {
        // Arrange
        var existingCharId = Guid.NewGuid();
        var newCharId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        // Existing character already has a profile
        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{existingCharId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProfile(existingCharId, gameServiceId, realmId));
        // New character does not
        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{newCharId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileModel?)null);

        var savedKeys = new List<string>();
        _mockProfileStore.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<LifecycleProfileModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LifecycleProfileModel, StateOptions?, CancellationToken>((k, _, _, _) => savedKeys.Add(k))
            .ReturnsAsync("etag-new");

        var service = CreateService();
        var request = new SeedLifecycleProfileRequest
        {
            Characters = new[]
            {
                new SeedCharacterEntry
                {
                    CharacterId = existingCharId, GameServiceId = gameServiceId, RealmId = realmId,
                    SpeciesCode = "human", BirthGameYear = 100, CurrentAge = 20, CurrentStage = "adult"
                },
                new SeedCharacterEntry
                {
                    CharacterId = newCharId, GameServiceId = gameServiceId, RealmId = realmId,
                    SpeciesCode = "human", BirthGameYear = 110, CurrentAge = 10, CurrentStage = "child"
                }
            }
        };

        // Act
        var (status, response) = await service.SeedLifecycleProfileAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.CreatedCount);
        Assert.DoesNotContain($"profile:{existingCharId}", savedKeys);
        Assert.Contains($"profile:{newCharId}", savedKeys);
    }

    #endregion

    #region GetGeneticProfile

    [Fact]
    public async Task GetGeneticProfile_ProfileExists_ReturnsOkWithProfile()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var geneticProfile = new GeneticProfileModel
        {
            CharacterId = characterId,
            SpeciesCode = "human",
            GenerationDepth = 0,
            Genotype = new List<GenotypeEntry> { new() { TraitCode = "height", AlleleA = 0.7f, AlleleB = 0.5f, Dominance = DominanceModel.Blending } },
            Phenotype = new List<PhenotypeEntry> { new() { TraitCode = "height", Value = 0.6f, ExpressionRule = DominanceModel.Blending } },
            Aptitudes = new List<AptitudeEntry> { new() { Domain = "strength", Value = 0.8f } },
            Bloodlines = new List<BloodlineEntry>(),
            Mutations = new List<MutationEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockGeneticStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(geneticProfile);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetGeneticProfileAsync(
            new GetGeneticProfileRequest { CharacterId = characterId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal("human", response.SpeciesCode);
    }

    [Fact]
    public async Task GetGeneticProfile_NotFound_ReturnsNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        _mockGeneticStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeneticProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetGeneticProfileAsync(
            new GetGeneticProfileRequest { CharacterId = characterId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetPhenotype

    [Fact]
    public async Task GetPhenotype_ProfileExists_ReturnsPhenotypeSubset()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var geneticProfile = new GeneticProfileModel
        {
            CharacterId = characterId,
            SpeciesCode = "elf",
            GenerationDepth = 1,
            Genotype = new List<GenotypeEntry> { new() { TraitCode = "height", AlleleA = 0.9f, AlleleB = 0.8f, Dominance = DominanceModel.DominantHigh } },
            Phenotype = new List<PhenotypeEntry> { new() { TraitCode = "height", Value = 0.9f, ExpressionRule = DominanceModel.DominantHigh } },
            Aptitudes = new List<AptitudeEntry> { new() { Domain = "magic", Value = 0.95f } },
            Bloodlines = new List<BloodlineEntry>(),
            Mutations = new List<MutationEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockGeneticStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(geneticProfile);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetPhenotypeAsync(
            new GetPhenotypeRequest { CharacterId = characterId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.NotEmpty(response.Phenotype);
        Assert.NotEmpty(response.Aptitudes);
    }

    [Fact]
    public async Task GetPhenotype_NotFound_ReturnsNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        _mockGeneticStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeneticProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetPhenotypeAsync(
            new GetPhenotypeRequest { CharacterId = characterId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region SeedGeneticProfile

    [Fact]
    public async Task SeedGeneticProfile_NoExistingProfile_CreatesProfileAndDeletesCache()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();

        // No existing genetic profile
        _mockGeneticStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeneticProfileModel?)null);

        // Trait template exists
        var traitTemplate = new HeritableTraitTemplateModel
        {
            SpeciesCode = "human",
            GameServiceId = gameServiceId,
            Traits = new List<HeritableTraitDefinition>
            {
                new()
                {
                    TraitCode = "height", DisplayName = "Height", Category = "physical",
                    DominanceModel = DominanceModel.Blending, MutationChance = 0.05f, MutationRange = 0.1f
                }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };
        _mockTraitTemplateStore.Setup(s => s.GetAsync(
                $"trait-template:human:{gameServiceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(traitTemplate);

        // Capture save
        GeneticProfileModel? savedProfile = null;
        string? savedKey = null;
        _mockGeneticStore.Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("genetic:")), It.IsAny<GeneticProfileModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GeneticProfileModel, StateOptions?, CancellationToken>((k, m, _, _) => { savedKey = k; savedProfile = m; })
            .ReturnsAsync("etag-new");

        // Capture cache deletion
        string? deletedCacheKey = null;
        _mockCacheStore.Setup(s => s.DeleteAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) => deletedCacheKey = k)
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedGeneticProfileAsync(
            new SeedGeneticProfileRequest { CharacterId = characterId, SpeciesCode = "human", GameServiceId = gameServiceId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedProfile);
        Assert.Equal($"genetic:{characterId}", savedKey);
        Assert.Equal($"manifest:{characterId}", deletedCacheKey);
    }

    [Fact]
    public async Task SeedGeneticProfile_AlreadyExists_ReturnsConflict()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingGenetic = new GeneticProfileModel
        {
            CharacterId = characterId,
            SpeciesCode = "human",
            GenerationDepth = 0,
            Genotype = new List<GenotypeEntry>(),
            Phenotype = new List<PhenotypeEntry>(),
            Aptitudes = new List<AptitudeEntry>(),
            Bloodlines = new List<BloodlineEntry>(),
            Mutations = new List<MutationEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _mockGeneticStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingGenetic);

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedGeneticProfileAsync(
            new SeedGeneticProfileRequest { CharacterId = characterId, SpeciesCode = "human", GameServiceId = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SeedGeneticProfile_NoTraitTemplate_ReturnsNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();

        _mockGeneticStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeneticProfileModel?)null);
        _mockTraitTemplateStore.Setup(s => s.GetAsync(
                $"trait-template:human:{gameServiceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeritableTraitTemplateModel?)null);

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedGeneticProfileAsync(
            new SeedGeneticProfileRequest { CharacterId = characterId, SpeciesCode = "human", GameServiceId = gameServiceId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region SimulateOffspring

    [Fact]
    public async Task SimulateOffspring_BothParentsFound_ReturnsRanges()
    {
        // Arrange
        var parentAId = Guid.NewGuid();
        var parentBId = Guid.NewGuid();

        var parentAProfile = new GeneticProfileModel
        {
            CharacterId = parentAId,
            SpeciesCode = "human",
            GenerationDepth = 0,
            Genotype = new List<GenotypeEntry> { new() { TraitCode = "height", AlleleA = 0.8f, AlleleB = 0.6f, Dominance = DominanceModel.Blending } },
            Phenotype = new List<PhenotypeEntry> { new() { TraitCode = "height", Value = 0.7f, ExpressionRule = DominanceModel.Blending } },
            Aptitudes = new List<AptitudeEntry> { new() { Domain = "strength", Value = 0.9f } },
            Bloodlines = new List<BloodlineEntry>(),
            Mutations = new List<MutationEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var parentBProfile = new GeneticProfileModel
        {
            CharacterId = parentBId,
            SpeciesCode = "human",
            GenerationDepth = 0,
            Genotype = new List<GenotypeEntry> { new() { TraitCode = "height", AlleleA = 0.5f, AlleleB = 0.4f, Dominance = DominanceModel.Blending } },
            Phenotype = new List<PhenotypeEntry> { new() { TraitCode = "height", Value = 0.45f, ExpressionRule = DominanceModel.Blending } },
            Aptitudes = new List<AptitudeEntry> { new() { Domain = "strength", Value = 0.3f } },
            Bloodlines = new List<BloodlineEntry>(),
            Mutations = new List<MutationEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockGeneticStore.Setup(s => s.GetAsync($"genetic:{parentAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentAProfile);
        _mockGeneticStore.Setup(s => s.GetAsync($"genetic:{parentBId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentBProfile);

        var service = CreateService();

        // Act
        var (status, response) = await service.SimulateOffspringAsync(
            new SimulateOffspringRequest { ParentAId = parentAId, ParentBId = parentBId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEmpty(response.TraitRanges);
        Assert.NotEmpty(response.AptitudeRanges);
    }

    [Fact]
    public async Task SimulateOffspring_ParentANotFound_ReturnsNotFound()
    {
        // Arrange
        var parentAId = Guid.NewGuid();
        var parentBId = Guid.NewGuid();

        _mockGeneticStore.Setup(s => s.GetAsync($"genetic:{parentAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeneticProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, response) = await service.SimulateOffspringAsync(
            new SimulateOffspringRequest { ParentAId = parentAId, ParentBId = parentBId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SimulateOffspring_ParentBNotFound_ReturnsNotFound()
    {
        // Arrange
        var parentAId = Guid.NewGuid();
        var parentBId = Guid.NewGuid();

        var parentAProfile = new GeneticProfileModel
        {
            CharacterId = parentAId,
            SpeciesCode = "human",
            GenerationDepth = 0,
            Genotype = new List<GenotypeEntry>(),
            Phenotype = new List<PhenotypeEntry>(),
            Aptitudes = new List<AptitudeEntry>(),
            Bloodlines = new List<BloodlineEntry>(),
            Mutations = new List<MutationEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockGeneticStore.Setup(s => s.GetAsync($"genetic:{parentAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentAProfile);
        _mockGeneticStore.Setup(s => s.GetAsync($"genetic:{parentBId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeneticProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, response) = await service.SimulateOffspringAsync(
            new SimulateOffspringRequest { ParentAId = parentAId, ParentBId = parentBId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetFamilyTree

    [Fact]
    public async Task GetFamilyTree_ProfileExists_ReturnsTreeNodes()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProfile(characterId));

        var service = CreateService();

        // Act
        var (status, response) = await service.GetFamilyTreeAsync(
            new GetFamilyTreeRequest { CharacterId = characterId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.RootCharacterId);
        Assert.NotEmpty(response.Nodes);
    }

    [Fact]
    public async Task GetFamilyTree_ProfileNotFound_ReturnsNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetFamilyTreeAsync(
            new GetFamilyTreeRequest { CharacterId = characterId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion
}
