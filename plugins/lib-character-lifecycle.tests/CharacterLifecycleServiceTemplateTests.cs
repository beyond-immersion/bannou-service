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
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.CharacterLifecycle.Tests;

/// <summary>
/// Unit tests for CharacterLifecycleService template and bloodline management endpoints.
/// Tests seeding templates, establishing/deleting bloodlines, and their side effects.
/// </summary>
public class CharacterLifecycleServiceTemplateTests : ServiceTestBase<CharacterLifecycleServiceConfiguration>
{
    // ========================================================================
    // Test Infrastructure
    // ========================================================================

    /// <summary>
    /// Creates a CharacterLifecycleService with all 19 constructor parameters mocked,
    /// returning the service and all mocks needed for capture-based assertions.
    /// </summary>
    private (
        CharacterLifecycleService Service,
        Mock<IStateStore<LifecycleProfileModel>> MockProfileStore,
        Mock<IStateStore<object>> MockHeritageStore,
        Mock<IStateStore<object>> MockBloodlineStore,
        Mock<IStateStore<object>> MockCacheStore,
        Mock<IMessageBus> MockMessageBus,
        Mock<IResourceClient> MockResourceClient,
        Mock<IGameServiceClient> MockGameServiceClient
    ) CreateService()
    {
        var mockLogger = new Mock<ILogger<CharacterLifecycleService>>();
        var mockStateStoreFactory = new Mock<IStateStoreFactory>();
        var mockLockProvider = new Mock<IDistributedLockProvider>();
        var mockMessageBus = new Mock<IMessageBus>();
        var mockEventConsumer = new Mock<IEventConsumer>();
        var mockTelemetryProvider = new Mock<ITelemetryProvider>();
        var mockCharacterClient = new Mock<ICharacterClient>();
        var mockRelationshipClient = new Mock<IRelationshipClient>();
        var mockSpeciesClient = new Mock<ISpeciesClient>();
        var mockWorldstateClient = new Mock<IWorldstateClient>();
        var mockContractClient = new Mock<IContractClient>();
        var mockResourceClient = new Mock<IResourceClient>();
        var mockSeedClient = new Mock<ISeedClient>();
        var mockGameServiceClient = new Mock<IGameServiceClient>();
        var mockInventoryClient = new Mock<IInventoryClient>();
        var mockCurrencyClient = new Mock<ICurrencyClient>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        // Constructor-cached state stores
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileModel>>();
        var mockHeritageStore = new Mock<IStateStore<object>>();
        var mockBloodlineStore = new Mock<IStateStore<object>>();
        var mockCacheStore = new Mock<IStateStore<object>>();

        mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileModel>(StateStoreDefinitions.CharacterLifecycleProfiles))
            .Returns(mockProfileStore.Object);
        mockStateStoreFactory
            .Setup(f => f.GetStore<object>(StateStoreDefinitions.CharacterLifecycleHeritage))
            .Returns(mockHeritageStore.Object);
        mockStateStoreFactory
            .Setup(f => f.GetStore<object>(StateStoreDefinitions.CharacterLifecycleBloodlines))
            .Returns(mockBloodlineStore.Object);
        mockStateStoreFactory
            .Setup(f => f.GetStore<object>(StateStoreDefinitions.CharacterLifecycleCache))
            .Returns(mockCacheStore.Object);

        var service = new CharacterLifecycleService(
            mockLogger.Object,
            Configuration,
            mockStateStoreFactory.Object,
            mockLockProvider.Object,
            mockMessageBus.Object,
            mockEventConsumer.Object,
            mockTelemetryProvider.Object,
            mockCharacterClient.Object,
            mockRelationshipClient.Object,
            mockSpeciesClient.Object,
            mockWorldstateClient.Object,
            mockContractClient.Object,
            mockResourceClient.Object,
            mockSeedClient.Object,
            mockGameServiceClient.Object,
            mockInventoryClient.Object,
            mockCurrencyClient.Object,
            mockServiceProvider.Object);

        return (
            service,
            mockProfileStore,
            mockHeritageStore,
            mockBloodlineStore,
            mockCacheStore,
            mockMessageBus,
            mockResourceClient,
            mockGameServiceClient
        );
    }

    // ========================================================================
    // SeedLifecycleTemplate
    // ========================================================================

    [Fact]
    public async Task SeedLifecycleTemplateAsync_ValidRequest_Returns200AndSavesTemplateAndPublishesEvent()
    {
        // Arrange
        var (service, _, mockHeritageStore, _, _, mockMessageBus, _, _) = CreateService();
        var gameServiceId = Guid.NewGuid();
        var request = new SeedLifecycleTemplateRequest
        {
            GameServiceId = gameServiceId,
            SpeciesCode = "human",
            Stages = new[]
            {
                new LifecycleStageDefinition
                {
                    Code = "infant", MinAge = 0, MaxAge = 3,
                    HealthModifier = 0.5f, FertilityBase = 0f,
                    CanMarry = false, CanProcreate = false,
                    CanOwnOrg = false, CanBePossessed = true
                },
                new LifecycleStageDefinition
                {
                    Code = "adult", MinAge = 18, MaxAge = 60,
                    HealthModifier = 1.0f, FertilityBase = 1.0f,
                    CanMarry = true, CanProcreate = true,
                    CanOwnOrg = true, CanBePossessed = false
                }
            },
            NaturalDeathRange = new NaturalDeathRange
            {
                MinAge = 60,
                MaxAge = 100,
                Distribution = DeathDistribution.Normal
            },
            FertilityWindow = new FertilityWindow
            {
                PeakStartAge = 20,
                PeakEndAge = 35,
                DeclineRate = 0.05f
            }
        };

        // Heritage store returns null (template does not already exist)
        mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Capture saved key and model
        string? savedKey = null;
        object? savedModel = null;
        mockHeritageStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag");

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.SeedLifecycleTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Assert saved key matches expected key pattern
        var expectedKey = CharacterLifecycleService.BuildLifecycleTemplateKey("human", gameServiceId);
        Assert.Equal(expectedKey, savedKey);

        // Assert saved model is the correct type with correct fields
        Assert.NotNull(savedModel);
        var typedModel = Assert.IsType<LifecycleTemplateModel>(savedModel);
        Assert.Equal("human", typedModel.SpeciesCode);
        Assert.Equal(gameServiceId, typedModel.GameServiceId);
        Assert.Equal(2, typedModel.Stages.Count);

        // Assert event topic and content
        Assert.Equal("character-lifecycle.lifecycle-template.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    [Fact]
    public async Task SeedLifecycleTemplateAsync_TemplateAlreadyExists_Returns409()
    {
        // Arrange
        var (service, _, mockHeritageStore, _, _, _, _, _) = CreateService();
        var request = new SeedLifecycleTemplateRequest
        {
            GameServiceId = Guid.NewGuid(),
            SpeciesCode = "human",
            Stages = new[] { CreateMinimalStage() },
            NaturalDeathRange = new NaturalDeathRange
            {
                MinAge = 60,
                MaxAge = 100,
                Distribution = DeathDistribution.Normal
            },
            FertilityWindow = new FertilityWindow
            {
                PeakStartAge = 20,
                PeakEndAge = 35,
                DeclineRate = 0.05f
            }
        };

        // Heritage store returns existing template
        mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LifecycleTemplateModel { SpeciesCode = "human" });

        // Act
        var (status, response) = await service.SeedLifecycleTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SeedLifecycleTemplateAsync_InvalidGameServiceId_Returns400()
    {
        // Arrange
        var (service, _, _, _, _, _, _, mockGameServiceClient) = CreateService();
        var request = new SeedLifecycleTemplateRequest
        {
            GameServiceId = Guid.NewGuid(),
            SpeciesCode = "human",
            Stages = new[] { CreateMinimalStage() },
            NaturalDeathRange = new NaturalDeathRange
            {
                MinAge = 60,
                MaxAge = 100,
                Distribution = DeathDistribution.Normal
            },
            FertilityWindow = new FertilityWindow
            {
                PeakStartAge = 20,
                PeakEndAge = 35,
                DeclineRate = 0.05f
            }
        };

        // GameServiceClient.GetServiceAsync throws ApiException for invalid gameServiceId
        mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Game service not found", 400, null, null, null));

        // Act
        var (status, response) = await service.SeedLifecycleTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    // ========================================================================
    // SeedHeritableTraitTemplate
    // ========================================================================

    [Fact]
    public async Task SeedHeritableTraitTemplateAsync_ValidRequest_Returns200AndSavesAndPublishes()
    {
        // Arrange
        var (service, _, mockHeritageStore, _, _, mockMessageBus, _, _) = CreateService();
        var gameServiceId = Guid.NewGuid();
        var request = new SeedHeritableTraitTemplateRequest
        {
            GameServiceId = gameServiceId,
            SpeciesCode = "elf",
            Traits = new[]
            {
                new HeritableTraitDefinition
                {
                    TraitCode = "strength",
                    DisplayName = "Strength",
                    Category = "physical",
                    DominanceModel = DominanceModel.Blending,
                    MutationChance = 0.02f,
                    MutationRange = 0.1f
                }
            }
        };

        mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Capture saved key and model
        string? savedKey = null;
        object? savedModel = null;
        mockHeritageStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag");

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.SeedHeritableTraitTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Assert saved key matches expected key pattern
        var expectedKey = CharacterLifecycleService.BuildTraitTemplateKey("elf", gameServiceId);
        Assert.Equal(expectedKey, savedKey);

        // Assert saved model is the correct type with correct fields
        Assert.NotNull(savedModel);
        var typedModel = Assert.IsType<HeritableTraitTemplateModel>(savedModel);
        Assert.Equal("elf", typedModel.SpeciesCode);
        Assert.Equal(gameServiceId, typedModel.GameServiceId);
        Assert.Single(typedModel.Traits);
        Assert.Equal("strength", typedModel.Traits[0].TraitCode);

        // Assert event topic and content
        Assert.Equal("character-lifecycle.heritable-trait-template.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    [Fact]
    public async Task SeedHeritableTraitTemplateAsync_AlreadyExists_Returns409()
    {
        // Arrange
        var (service, _, mockHeritageStore, _, _, _, _, _) = CreateService();
        var request = new SeedHeritableTraitTemplateRequest
        {
            GameServiceId = Guid.NewGuid(),
            SpeciesCode = "elf",
            Traits = new[]
            {
                new HeritableTraitDefinition
                {
                    TraitCode = "agility",
                    DisplayName = "Agility",
                    Category = "physical",
                    DominanceModel = DominanceModel.DominantHigh,
                    MutationChance = 0.01f,
                    MutationRange = 0.05f
                }
            }
        };

        mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HeritableTraitTemplateModel { SpeciesCode = "elf" });

        // Act
        var (status, response) = await service.SeedHeritableTraitTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SeedHeritableTraitTemplateAsync_InvalidGameServiceId_Returns400()
    {
        // Arrange
        var (service, _, _, _, _, _, _, mockGameServiceClient) = CreateService();
        var request = new SeedHeritableTraitTemplateRequest
        {
            GameServiceId = Guid.NewGuid(),
            SpeciesCode = "elf",
            Traits = new[]
            {
                new HeritableTraitDefinition
                {
                    TraitCode = "wisdom",
                    DisplayName = "Wisdom",
                    Category = "mental",
                    DominanceModel = DominanceModel.Codominant,
                    MutationChance = 0.01f,
                    MutationRange = 0.05f
                }
            }
        };

        // GameServiceClient.GetServiceAsync throws ApiException for invalid gameServiceId
        mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Game service not found", 400, null, null, null));

        // Act
        var (status, response) = await service.SeedHeritableTraitTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    // ========================================================================
    // SeedHybridTemplate
    // ========================================================================

    [Fact]
    public async Task SeedHybridTemplateAsync_ValidRequest_Returns200AndSavesAndPublishes()
    {
        // Arrange
        var (service, _, mockHeritageStore, _, _, mockMessageBus, _, _) = CreateService();
        var gameServiceId = Guid.NewGuid();
        var request = new SeedHybridTemplateRequest
        {
            GameServiceId = gameServiceId,
            SpeciesA = "human",
            SpeciesB = "elf",
            TraitOverrides = new[]
            {
                new HybridTraitOverride
                {
                    TraitCode = "longevity",
                    DominanceOverride = DominanceModel.DominantHigh
                }
            },
            HybridFertilityModifier = 0.5f
        };

        mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Capture saved key and model
        string? savedKey = null;
        object? savedModel = null;
        mockHeritageStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag");

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.SeedHybridTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Assert saved key matches expected key pattern
        var expectedKey = CharacterLifecycleService.BuildHybridTemplateKey("human", "elf", gameServiceId);
        Assert.Equal(expectedKey, savedKey);

        // Assert saved model is the correct type with correct fields
        Assert.NotNull(savedModel);
        var typedModel = Assert.IsType<HybridTraitTemplateModel>(savedModel);
        Assert.Equal("human", typedModel.SpeciesA);
        Assert.Equal("elf", typedModel.SpeciesB);
        Assert.Equal(gameServiceId, typedModel.GameServiceId);
        Assert.Equal(0.5f, typedModel.HybridFertilityModifier);
        Assert.Single(typedModel.TraitOverrides);

        // Assert event topic and content
        Assert.Equal("character-lifecycle.hybrid-trait-template.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    [Fact]
    public async Task SeedHybridTemplateAsync_AlreadyExists_Returns409()
    {
        // Arrange
        var (service, _, mockHeritageStore, _, _, _, _, _) = CreateService();
        var request = new SeedHybridTemplateRequest
        {
            GameServiceId = Guid.NewGuid(),
            SpeciesA = "human",
            SpeciesB = "elf",
            TraitOverrides = new[]
            {
                new HybridTraitOverride
                {
                    TraitCode = "strength",
                    DominanceOverride = DominanceModel.Blending
                }
            },
            HybridFertilityModifier = 0.7f
        };

        mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HybridTraitTemplateModel { SpeciesA = "human", SpeciesB = "elf" });

        // Act
        var (status, response) = await service.SeedHybridTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SeedHybridTemplateAsync_InvalidGameServiceId_Returns400()
    {
        // Arrange
        var (service, _, _, _, _, _, _, mockGameServiceClient) = CreateService();
        var request = new SeedHybridTemplateRequest
        {
            GameServiceId = Guid.NewGuid(),
            SpeciesA = "human",
            SpeciesB = "dwarf",
            TraitOverrides = new[]
            {
                new HybridTraitOverride
                {
                    TraitCode = "constitution",
                    DominanceOverride = DominanceModel.DominantHigh
                }
            },
            HybridFertilityModifier = 0.6f
        };

        // GameServiceClient.GetServiceAsync throws ApiException for invalid gameServiceId
        mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Game service not found", 400, null, null, null));

        // Act
        var (status, response) = await service.SeedHybridTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    // ========================================================================
    // EstablishBloodline
    // ========================================================================

    [Fact]
    public async Task EstablishBloodlineAsync_ValidRequest_Returns200AndSavesAllRecordsAndPublishesEvents()
    {
        // Arrange
        var (service, _, _, mockBloodlineStore, mockCacheStore, mockMessageBus, _, _) = CreateService();
        var gameServiceId = Guid.NewGuid();
        var originCharacterId = Guid.NewGuid();
        var ancestorId = Guid.NewGuid();
        var request = new EstablishBloodlineRequest
        {
            BloodlineCode = "IRONBLOOD",
            GameServiceId = gameServiceId,
            OriginCharacterId = originCharacterId,
            TraitSignature = new[] { "strength", "endurance" },
            AncestorCharacterIds = new[] { ancestorId }
        };

        // No existing bloodline with this code
        mockBloodlineStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Capture all saves to bloodline store
        var savedKeys = new List<string>();
        var savedModels = new List<object>();
        mockBloodlineStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKeys.Add(k);
                savedModels.Add(m);
            })
            .ReturnsAsync("etag");

        // Capture cache deletes for manifest invalidation
        var deletedCacheKeys = new List<string>();
        mockCacheStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) =>
            {
                deletedCacheKeys.Add(k);
            })
            .ReturnsAsync(true);

        // Capture all published events
        var capturedTopics = new List<string>();
        var capturedEvents = new List<object>();
        mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopics.Add(t);
                capturedEvents.Add(e);
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.EstablishBloodlineAsync(request, CancellationToken.None);

        // Assert status and response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.BloodlineId);

        // Assert exact save keys: bloodline record, code lookup, member entries for origin + ancestor, members list
        // Per implementation map: bloodline:{id}, bloodline:code:{gameServiceId}:{code}, bloodline:member:{charId} x2, bloodline:members:{id}
        Assert.Equal(5, savedKeys.Count);

        // Bloodline record key
        var expectedBloodlineKey = CharacterLifecycleService.BuildBloodlineKey(response.BloodlineId);
        Assert.Contains(expectedBloodlineKey, savedKeys);

        // Code lookup key
        var expectedCodeKey = CharacterLifecycleService.BuildBloodlineCodeKey(gameServiceId, "IRONBLOOD");
        Assert.Contains(expectedCodeKey, savedKeys);

        // Member keys for origin character and ancestor
        var expectedOriginMemberKey = CharacterLifecycleService.BuildBloodlineMemberKey(originCharacterId);
        Assert.Contains(expectedOriginMemberKey, savedKeys);
        var expectedAncestorMemberKey = CharacterLifecycleService.BuildBloodlineMemberKey(ancestorId);
        Assert.Contains(expectedAncestorMemberKey, savedKeys);

        // Members list key
        var expectedMembersKey = CharacterLifecycleService.BuildBloodlineMembersKey(response.BloodlineId);
        Assert.Contains(expectedMembersKey, savedKeys);

        // Assert bloodline model was saved with correct fields
        var bloodlineModel = savedModels[savedKeys.IndexOf(expectedBloodlineKey)];
        var typedBloodline = Assert.IsType<BloodlineModel>(bloodlineModel);
        Assert.Equal("IRONBLOOD", typedBloodline.BloodlineCode);
        Assert.Equal(gameServiceId, typedBloodline.GameServiceId);
        Assert.Equal(originCharacterId, typedBloodline.OriginCharacterId);

        // Assert cache invalidation for each assigned character
        Assert.Equal(2, deletedCacheKeys.Count);
        var expectedOriginCacheKey = CharacterLifecycleService.BuildManifestKey(originCharacterId);
        Assert.Contains(expectedOriginCacheKey, deletedCacheKeys);
        var expectedAncestorCacheKey = CharacterLifecycleService.BuildManifestKey(ancestorId);
        Assert.Contains(expectedAncestorCacheKey, deletedCacheKeys);

        // Assert both formed and created events published
        Assert.Equal(2, capturedTopics.Count);
        Assert.Contains("character-lifecycle.bloodline.formed", capturedTopics);
        Assert.Contains("character-lifecycle.bloodline.created", capturedTopics);

        // Assert formed event content
        var formedIndex = capturedTopics.IndexOf("character-lifecycle.bloodline.formed");
        Assert.NotNull(capturedEvents[formedIndex]);
    }

    [Fact]
    public async Task EstablishBloodlineAsync_BloodlineCodeExists_Returns409()
    {
        // Arrange
        var (service, _, _, mockBloodlineStore, _, _, _, _) = CreateService();
        var gameServiceId = Guid.NewGuid();
        var request = new EstablishBloodlineRequest
        {
            BloodlineCode = "IRONBLOOD",
            GameServiceId = gameServiceId,
            OriginCharacterId = Guid.NewGuid(),
            TraitSignature = new[] { "strength" }
        };

        // Existing bloodline with this code — match the code-lookup key specifically
        var expectedCodeKey = CharacterLifecycleService.BuildBloodlineCodeKey(gameServiceId, "IRONBLOOD");
        mockBloodlineStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == expectedCodeKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());

        // Act
        var (status, response) = await service.EstablishBloodlineAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    // ========================================================================
    // DeleteBloodline
    // ========================================================================

    [Fact]
    public async Task DeleteBloodlineAsync_BloodlineExists_Returns200AndDeletesAllKeysAndCleansUpAndPublishes()
    {
        // Arrange
        var (service, _, _, mockBloodlineStore, _, mockMessageBus, mockResourceClient, _) = CreateService();
        var bloodlineId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();
        var request = new DeleteBloodlineRequest { BloodlineId = bloodlineId };

        // Return a typed BloodlineModel so the service can extract gameServiceId and bloodlineCode
        var existingBloodline = new BloodlineModel
        {
            BloodlineId = bloodlineId,
            BloodlineCode = "STORMBORN",
            GameServiceId = gameServiceId,
            OriginCharacterId = Guid.NewGuid(),
            MemberCount = 3,
            GenerationSpan = 2
        };

        mockBloodlineStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == CharacterLifecycleService.BuildBloodlineKey(bloodlineId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBloodline);

        // Capture all deleted keys from bloodline store
        var deletedKeys = new List<string>();
        mockBloodlineStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) =>
            {
                deletedKeys.Add(k);
            })
            .ReturnsAsync(true);

        // Capture resource cleanup call
        ExecuteCleanupRequest? capturedCleanupRequest = null;
        mockResourceClient
            .Setup(r => r.ExecuteCleanupAsync(
                It.IsAny<ExecuteCleanupRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ExecuteCleanupRequest, CancellationToken>((req, _) =>
            {
                capturedCleanupRequest = req;
            })
            .ReturnsAsync(new ExecuteCleanupResponse
            {
                ResourceType = "bloodline",
                ResourceId = bloodlineId,
                Success = true,
                CallbackResults = new List<CleanupCallbackResult>()
            });

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.DeleteBloodlineAsync(request, CancellationToken.None);

        // Assert status
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Assert both bloodline record and code-lookup key deleted
        var expectedBloodlineKey = CharacterLifecycleService.BuildBloodlineKey(bloodlineId);
        Assert.Contains(expectedBloodlineKey, deletedKeys);
        var expectedCodeKey = CharacterLifecycleService.BuildBloodlineCodeKey(gameServiceId, "STORMBORN");
        Assert.Contains(expectedCodeKey, deletedKeys);

        // Assert resource cleanup was called with correct parameters
        Assert.NotNull(capturedCleanupRequest);
        Assert.Equal(bloodlineId, capturedCleanupRequest.ResourceId);
        Assert.Equal("bloodline", capturedCleanupRequest.ResourceType);

        // Assert deleted event published
        Assert.Equal("character-lifecycle.bloodline.deleted", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    [Fact]
    public async Task DeleteBloodlineAsync_NotFound_Returns404()
    {
        // Arrange
        var (service, _, _, mockBloodlineStore, _, _, _, _) = CreateService();
        var request = new DeleteBloodlineRequest { BloodlineId = Guid.NewGuid() };

        mockBloodlineStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var (status, response) = await service.DeleteBloodlineAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    /// <summary>
    /// Creates a minimal lifecycle stage definition for tests that need valid but compact requests.
    /// </summary>
    private static LifecycleStageDefinition CreateMinimalStage()
    {
        return new LifecycleStageDefinition
        {
            Code = "adult",
            MinAge = 18,
            MaxAge = 80,
            HealthModifier = 1.0f,
            FertilityBase = 1.0f,
            CanMarry = true,
            CanProcreate = true,
            CanOwnOrg = true,
            CanBePossessed = false
        };
    }
}
