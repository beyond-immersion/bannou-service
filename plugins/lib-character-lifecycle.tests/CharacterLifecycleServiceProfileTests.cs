using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.CharacterLifecycle;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.CharacterLifecycle.Tests;

/// <summary>
/// Unit tests for CharacterLifecycleService profile and heritage endpoints.
/// Tests lifecycle profile CRUD, genetic profile management, offspring simulation, and family tree retrieval.
/// </summary>
public class CharacterLifecycleServiceProfileTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ILogger<CharacterLifecycleService>> _mockLogger;
    private readonly CharacterLifecycleServiceConfiguration _configuration;

    private const string PROFILES_STORE = "character-lifecycle-profiles";
    private const string HERITAGE_STORE = "character-lifecycle-heritage";
    private const string CACHE_STORE = "character-lifecycle-cache";

    public CharacterLifecycleServiceProfileTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockLogger = new Mock<ILogger<CharacterLifecycleService>>();
        _configuration = new CharacterLifecycleServiceConfiguration();
    }

    /// <summary>
    /// Creates the service under test with default mocked dependencies.
    /// </summary>
    private CharacterLifecycleService CreateService()
    {
        return new CharacterLifecycleService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockResourceClient.Object,
            _mockLogger.Object,
            _configuration);
    }

    #region GetLifecycleProfile

    [Fact]
    public async Task GetLifecycleProfile_ProfileExists_ReturnsOkWithProfile()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        var storedProfile = new LifecycleProfileSummary
        {
            CharacterId = characterId,
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            SpeciesCode = "human",
            BirthGameYear = 100,
            CurrentAge = 25,
            CurrentStage = "adult",
            CauseOfCreation = CreationCause.Seeded,
            ChildCount = 0,
            TotalChildCount = 0,
            FertilityModifier = 1.0f,
            HealthModifier = 1.0f,
            Status = LifecycleStatus.Alive
        };

        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedProfile);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileSummary?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        var mockCacheStore = new Mock<IStateStore<object>>();

        var existingProfile = new LifecycleProfileSummary
        {
            CharacterId = characterId,
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            SpeciesCode = "human",
            BirthGameYear = 100,
            CurrentAge = 25,
            CurrentStage = "adult",
            CauseOfCreation = CreationCause.Seeded,
            ChildCount = 0,
            TotalChildCount = 0,
            FertilityModifier = 1.0f,
            HealthModifier = 1.0f,
            Status = LifecycleStatus.Alive,
            NaturalDeathYear = 180
        };

        mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingProfile, "etag-1"));

        // Capture saved profile
        LifecycleProfileSummary? savedProfile = null;
        mockProfileStore.Setup(s => s.TrySaveAsync(
                $"profile:{characterId}", It.IsAny<LifecycleProfileSummary>(),
                "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LifecycleProfileSummary, string, StateOptions?, CancellationToken>((_, m, _, _, _) => savedProfile = m)
            .ReturnsAsync("etag-2");

        // Capture cache deletion
        string? deletedCacheKey = null;
        mockCacheStore.Setup(s => s.DeleteAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) => deletedCacheKey = k)
            .ReturnsAsync(true);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<object>(CACHE_STORE))
            .Returns(mockCacheStore.Object);

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
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((LifecycleProfileSummary?)null, (string?)null));
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        var existingProfile = new LifecycleProfileSummary
        {
            CharacterId = characterId,
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            SpeciesCode = "human",
            BirthGameYear = 100,
            CurrentAge = 25,
            CurrentStage = "adult",
            CauseOfCreation = CreationCause.Seeded,
            ChildCount = 0,
            TotalChildCount = 0,
            FertilityModifier = 1.0f,
            HealthModifier = 1.0f,
            Status = LifecycleStatus.Alive
        };

        mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingProfile, "etag-1"));
        mockProfileStore.Setup(s => s.TrySaveAsync(
                $"profile:{characterId}", It.IsAny<LifecycleProfileSummary>(),
                "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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

        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        // Neither profile exists
        mockProfileStore.Setup(s => s.GetAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileSummary?)null);

        // Capture saves
        var savedKeys = new List<string>();
        mockProfileStore.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<LifecycleProfileSummary>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LifecycleProfileSummary, StateOptions?, CancellationToken>((k, _, _, _) => savedKeys.Add(k))
            .ReturnsAsync("etag-new");

        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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

        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();

        // Existing character already has a profile
        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{existingCharId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LifecycleProfileSummary
            {
                CharacterId = existingCharId,
                GameServiceId = gameServiceId,
                RealmId = realmId,
                SpeciesCode = "human",
                BirthGameYear = 100,
                CurrentAge = 20,
                CurrentStage = "adult",
                CauseOfCreation = CreationCause.Seeded,
                ChildCount = 0,
                TotalChildCount = 0,
                FertilityModifier = 1.0f,
                HealthModifier = 1.0f,
                Status = LifecycleStatus.Alive
            });
        // New character does not
        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{newCharId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileSummary?)null);

        var savedKeys = new List<string>();
        mockProfileStore.Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<LifecycleProfileSummary>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LifecycleProfileSummary, StateOptions?, CancellationToken>((k, _, _, _) => savedKeys.Add(k))
            .ReturnsAsync("etag-new");

        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();

        var geneticProfile = new GetGeneticProfileResponse
        {
            CharacterId = characterId,
            SpeciesCode = "human",
            GenerationDepth = 0,
            Genotype = new[] { new GenotypeEntry { TraitCode = "height", AlleleA = 0.7f, AlleleB = 0.5f, Dominance = DominanceModel.Blending } },
            Phenotype = new[] { new PhenotypeEntry { TraitCode = "height", Value = 0.6f, ExpressionRule = DominanceModel.Blending } },
            Aptitudes = new[] { new AptitudeEntry { Domain = "strength", Value = 0.8f } },
            Bloodlines = Array.Empty<BloodlineEntry>(),
            Mutations = Array.Empty<MutationEntry>()
        };

        mockHeritageStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(geneticProfile);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);

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
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();
        mockHeritageStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetGeneticProfileResponse?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);

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
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();

        var geneticProfile = new GetGeneticProfileResponse
        {
            CharacterId = characterId,
            SpeciesCode = "elf",
            GenerationDepth = 1,
            Genotype = new[] { new GenotypeEntry { TraitCode = "height", AlleleA = 0.9f, AlleleB = 0.8f, Dominance = DominanceModel.DominantHigh } },
            Phenotype = new[] { new PhenotypeEntry { TraitCode = "height", Value = 0.9f, ExpressionRule = DominanceModel.DominantHigh } },
            Aptitudes = new[] { new AptitudeEntry { Domain = "magic", Value = 0.95f } },
            Bloodlines = Array.Empty<BloodlineEntry>(),
            Mutations = Array.Empty<MutationEntry>()
        };

        mockHeritageStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(geneticProfile);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);

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
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();
        mockHeritageStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetGeneticProfileResponse?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);

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
    public async Task SeedGeneticProfile_NoExistingProfile_CreatesProfile()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();
        var mockCacheStore = new Mock<IStateStore<object>>();

        // No existing genetic profile
        mockHeritageStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetGeneticProfileResponse?)null);

        // Trait template exists
        var mockTraitTemplateStore = new Mock<IStateStore<GetHeritableTraitTemplateResponse>>();
        mockTraitTemplateStore.Setup(s => s.GetAsync(
                $"trait-template:human:{gameServiceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetHeritableTraitTemplateResponse
            {
                SpeciesCode = "human",
                GameServiceId = gameServiceId,
                Traits = new[] { new HeritableTraitDefinition
                {
                    TraitCode = "height", DisplayName = "Height", Category = "physical",
                    DominanceModel = DominanceModel.Blending, MutationChance = 0.05f, MutationRange = 0.1f
                }}
            });

        // Capture save
        GetGeneticProfileResponse? savedProfile = null;
        mockHeritageStore.Setup(s => s.SaveAsync(
                $"genetic:{characterId}", It.IsAny<GetGeneticProfileResponse>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GetGeneticProfileResponse, StateOptions?, CancellationToken>((_, m, _, _) => savedProfile = m)
            .ReturnsAsync("etag-new");

        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetHeritableTraitTemplateResponse>(HERITAGE_STORE))
            .Returns(mockTraitTemplateStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<object>(CACHE_STORE))
            .Returns(mockCacheStore.Object);

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedGeneticProfileAsync(
            new SeedGeneticProfileRequest { CharacterId = characterId, SpeciesCode = "human", GameServiceId = gameServiceId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedProfile);
        Assert.Equal(characterId, savedProfile.CharacterId);
        Assert.Equal("human", savedProfile.SpeciesCode);
    }

    [Fact]
    public async Task SeedGeneticProfile_AlreadyExists_ReturnsConflict()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();
        mockHeritageStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetGeneticProfileResponse
            {
                CharacterId = characterId,
                SpeciesCode = "human",
                GenerationDepth = 0,
                Genotype = Array.Empty<GenotypeEntry>(),
                Phenotype = Array.Empty<PhenotypeEntry>(),
                Aptitudes = Array.Empty<AptitudeEntry>(),
                Bloodlines = Array.Empty<BloodlineEntry>(),
                Mutations = Array.Empty<MutationEntry>()
            });
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);

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
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();
        var mockTraitTemplateStore = new Mock<IStateStore<GetHeritableTraitTemplateResponse>>();

        mockHeritageStore.Setup(s => s.GetAsync(
                $"genetic:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetGeneticProfileResponse?)null);
        mockTraitTemplateStore.Setup(s => s.GetAsync(
                $"trait-template:human:{gameServiceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetHeritableTraitTemplateResponse?)null);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetHeritableTraitTemplateResponse>(HERITAGE_STORE))
            .Returns(mockTraitTemplateStore.Object);

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
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();

        var parentAProfile = new GetGeneticProfileResponse
        {
            CharacterId = parentAId,
            SpeciesCode = "human",
            GenerationDepth = 0,
            Genotype = new[] { new GenotypeEntry { TraitCode = "height", AlleleA = 0.8f, AlleleB = 0.6f, Dominance = DominanceModel.Blending } },
            Phenotype = new[] { new PhenotypeEntry { TraitCode = "height", Value = 0.7f, ExpressionRule = DominanceModel.Blending } },
            Aptitudes = new[] { new AptitudeEntry { Domain = "strength", Value = 0.9f } },
            Bloodlines = Array.Empty<BloodlineEntry>(),
            Mutations = Array.Empty<MutationEntry>()
        };
        var parentBProfile = new GetGeneticProfileResponse
        {
            CharacterId = parentBId,
            SpeciesCode = "human",
            GenerationDepth = 0,
            Genotype = new[] { new GenotypeEntry { TraitCode = "height", AlleleA = 0.5f, AlleleB = 0.4f, Dominance = DominanceModel.Blending } },
            Phenotype = new[] { new PhenotypeEntry { TraitCode = "height", Value = 0.45f, ExpressionRule = DominanceModel.Blending } },
            Aptitudes = new[] { new AptitudeEntry { Domain = "strength", Value = 0.3f } },
            Bloodlines = Array.Empty<BloodlineEntry>(),
            Mutations = Array.Empty<MutationEntry>()
        };

        mockHeritageStore.Setup(s => s.GetAsync($"genetic:{parentAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentAProfile);
        mockHeritageStore.Setup(s => s.GetAsync($"genetic:{parentBId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentBProfile);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);

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
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();

        mockHeritageStore.Setup(s => s.GetAsync($"genetic:{parentAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetGeneticProfileResponse?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);

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
        var mockHeritageStore = new Mock<IStateStore<GetGeneticProfileResponse>>();

        mockHeritageStore.Setup(s => s.GetAsync($"genetic:{parentAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetGeneticProfileResponse
            {
                CharacterId = parentAId,
                SpeciesCode = "human",
                GenerationDepth = 0,
                Genotype = Array.Empty<GenotypeEntry>(),
                Phenotype = Array.Empty<PhenotypeEntry>(),
                Aptitudes = Array.Empty<AptitudeEntry>(),
                Bloodlines = Array.Empty<BloodlineEntry>(),
                Mutations = Array.Empty<MutationEntry>()
            });
        mockHeritageStore.Setup(s => s.GetAsync($"genetic:{parentBId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetGeneticProfileResponse?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GetGeneticProfileResponse>(HERITAGE_STORE))
            .Returns(mockHeritageStore.Object);

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
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();

        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LifecycleProfileSummary
            {
                CharacterId = characterId,
                GameServiceId = Guid.NewGuid(),
                RealmId = Guid.NewGuid(),
                SpeciesCode = "human",
                BirthGameYear = 100,
                CurrentAge = 25,
                CurrentStage = "adult",
                CauseOfCreation = CreationCause.Seeded,
                ChildCount = 0,
                TotalChildCount = 0,
                FertilityModifier = 1.0f,
                HealthModifier = 1.0f,
                Status = LifecycleStatus.Alive
            });

        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileSummary?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
