using BeyondImmersion.Bannou.Core;
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
    /// Creates a CharacterLifecycleService with all constructor parameters mocked,
    /// returning the service and properly typed store mocks for capture-based assertions.
    /// </summary>
    private (
        CharacterLifecycleService Service,
        Mock<IStateStore<LifecycleTemplateModel>> MockLifecycleTemplateStore,
        Mock<IStateStore<HeritableTraitTemplateModel>> MockTraitTemplateStore,
        Mock<IStateStore<HybridTraitTemplateModel>> MockHybridTemplateStore,
        Mock<IStateStore<BloodlineModel>> MockBloodlineStore,
        Mock<IStateStore<string>> MockCodeStore,
        Mock<IStateStore<BloodlineMembershipModel>> MockMembershipStore,
        Mock<IStateStore<BloodlineMemberListModel>> MockMemberListStore,
        Mock<IStateStore<LifecycleManifestModel>> MockCacheStore,
        Mock<IMessageBus> MockMessageBus,
        Mock<IResourceClient> MockResourceClient,
        Mock<IGameServiceClient> MockGameServiceClient
    ) CreateService()
    {
        var mockLogger = new Mock<ILogger<CharacterLifecycleService>>();
        var mockStateStoreFactory = new Mock<IStateStoreFactory> { DefaultValue = DefaultValue.Mock };
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

        // Properly typed store mocks matching service constructor types
        var mockLifecycleTemplateStore = new Mock<IStateStore<LifecycleTemplateModel>>();
        var mockTraitTemplateStore = new Mock<IStateStore<HeritableTraitTemplateModel>>();
        var mockHybridTemplateStore = new Mock<IStateStore<HybridTraitTemplateModel>>();
        var mockBloodlineStore = new Mock<IStateStore<BloodlineModel>>();
        var mockCodeStore = new Mock<IStateStore<string>>();
        var mockMembershipStore = new Mock<IStateStore<BloodlineMembershipModel>>();
        var mockMemberListStore = new Mock<IStateStore<BloodlineMemberListModel>>();
        var mockCacheStore = new Mock<IStateStore<LifecycleManifestModel>>();

        // Wire typed stores to factory (heritage store has multiple types)
        mockStateStoreFactory.Setup(f => f.GetStore<LifecycleTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage)).Returns(mockLifecycleTemplateStore.Object);
        mockStateStoreFactory.Setup(f => f.GetStore<HeritableTraitTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage)).Returns(mockTraitTemplateStore.Object);
        mockStateStoreFactory.Setup(f => f.GetStore<HybridTraitTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage)).Returns(mockHybridTemplateStore.Object);
        mockStateStoreFactory.Setup(f => f.GetStore<BloodlineModel>(StateStoreDefinitions.CharacterLifecycleBloodlines)).Returns(mockBloodlineStore.Object);
        mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.CharacterLifecycleBloodlines)).Returns(mockCodeStore.Object);
        mockStateStoreFactory.Setup(f => f.GetStore<BloodlineMembershipModel>(StateStoreDefinitions.CharacterLifecycleBloodlines)).Returns(mockMembershipStore.Object);
        mockStateStoreFactory.Setup(f => f.GetStore<BloodlineMemberListModel>(StateStoreDefinitions.CharacterLifecycleBloodlines)).Returns(mockMemberListStore.Object);
        mockStateStoreFactory.Setup(f => f.GetStore<LifecycleManifestModel>(StateStoreDefinitions.CharacterLifecycleCache)).Returns(mockCacheStore.Object);

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

        return (service, mockLifecycleTemplateStore, mockTraitTemplateStore, mockHybridTemplateStore,
            mockBloodlineStore, mockCodeStore, mockMembershipStore, mockMemberListStore,
            mockCacheStore, mockMessageBus, mockResourceClient, mockGameServiceClient);
    }

    // ========================================================================
    // SeedLifecycleTemplate
    // ========================================================================

    [Fact]
    public async Task SeedLifecycleTemplateAsync_ValidRequest_Returns200AndSavesTemplateAndPublishesEvent()
    {
        // Arrange
        var (service, mockLifecycleTemplateStore, mockTraitTemplateStore, mockHybridTemplateStore, _, _, _, _, _, mockMessageBus, _, _) = CreateService();
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
                    Code = "child", MinAge = 4, MaxAge = 17,
                    HealthModifier = 0.8f, FertilityBase = 0f,
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

        // Lifecycle template store returns null (template does not already exist)
        mockLifecycleTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleTemplateModel?)null);

        // Capture saved key and model
        string? savedKey = null;
        LifecycleTemplateModel? savedModel = null;
        mockLifecycleTemplateStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<LifecycleTemplateModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LifecycleTemplateModel, StateOptions?, CancellationToken>((k, m, _, _) =>
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
        Assert.Equal(3, typedModel.Stages.Count);

        // Assert event topic and content
        Assert.Equal("character-lifecycle.lifecycle-template.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    [Fact]
    public async Task SeedLifecycleTemplateAsync_TemplateAlreadyExists_Returns409()
    {
        // Arrange
        var (service, mockLifecycleTemplateStore, mockTraitTemplateStore, mockHybridTemplateStore, _, _, _, _, _, _, _, _) = CreateService();
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

        // Lifecycle template store returns existing template
        mockLifecycleTemplateStore
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
        var (service, _, _, _, _, _, _, _, _, _, _, mockGameServiceClient) = CreateService();
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
        var (service, mockLifecycleTemplateStore, mockTraitTemplateStore, mockHybridTemplateStore, _, _, _, _, _, mockMessageBus, _, _) = CreateService();
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

        mockTraitTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HeritableTraitTemplateModel?)null);

        // Capture saved key and model
        string? savedKey = null;
        HeritableTraitTemplateModel? savedModel = null;
        mockTraitTemplateStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<HeritableTraitTemplateModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, HeritableTraitTemplateModel, StateOptions?, CancellationToken>((k, m, _, _) =>
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
        var (service, mockLifecycleTemplateStore, mockTraitTemplateStore, mockHybridTemplateStore, _, _, _, _, _, _, _, _) = CreateService();
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

        mockTraitTemplateStore
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
        var (service, _, _, _, _, _, _, _, _, _, _, mockGameServiceClient) = CreateService();
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
        var (service, mockLifecycleTemplateStore, mockTraitTemplateStore, mockHybridTemplateStore, _, _, _, _, _, mockMessageBus, _, _) = CreateService();
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

        mockHybridTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HybridTraitTemplateModel?)null);

        // Capture saved key and model
        string? savedKey = null;
        HybridTraitTemplateModel? savedModel = null;
        mockHybridTemplateStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<HybridTraitTemplateModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, HybridTraitTemplateModel, StateOptions?, CancellationToken>((k, m, _, _) =>
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
        var typedModel = savedModel;
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
        var (service, mockLifecycleTemplateStore, mockTraitTemplateStore, mockHybridTemplateStore, _, _, _, _, _, _, _, _) = CreateService();
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

        mockHybridTemplateStore
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
        var (service, _, _, _, _, _, _, _, _, _, _, mockGameServiceClient) = CreateService();
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
        var (service, _, _, _, mockBloodlineStore, mockCodeStore, mockMembershipStore, mockMemberListStore, mockCacheStore, mockMessageBus, _, _) = CreateService();
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
        mockCodeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Capture saves to each properly typed store
        var bloodlineSavedKeys = new List<string>();
        var bloodlineSavedModels = new List<BloodlineModel>();
        mockBloodlineStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<BloodlineModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BloodlineModel, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                bloodlineSavedKeys.Add(k);
                bloodlineSavedModels.Add(m);
            })
            .ReturnsAsync("etag");

        var codeSavedKeys = new List<string>();
        mockCodeStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((k, _, _, _) => codeSavedKeys.Add(k))
            .ReturnsAsync("etag");

        var membershipSavedKeys = new List<string>();
        mockMembershipStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BloodlineMembershipModel?)null);
        mockMembershipStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<BloodlineMembershipModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BloodlineMembershipModel, StateOptions?, CancellationToken>((k, _, _, _) => membershipSavedKeys.Add(k))
            .ReturnsAsync("etag");

        var memberListSavedKeys = new List<string>();
        mockMemberListStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<BloodlineMemberListModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BloodlineMemberListModel, StateOptions?, CancellationToken>((k, _, _, _) => memberListSavedKeys.Add(k))
            .ReturnsAsync("etag");

        // Capture cache deletes for manifest invalidation
        var deletedCacheKeys = new List<string>();
        mockCacheStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) => deletedCacheKeys.Add(k))
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

        // Assert saves across all typed stores
        // Bloodline store: initial save + update with member count = 2 saves
        Assert.True(bloodlineSavedKeys.Count >= 1);
        var expectedBloodlineKey = CharacterLifecycleService.BuildBloodlineKey(response.BloodlineId);
        Assert.Contains(expectedBloodlineKey, bloodlineSavedKeys);

        // Code lookup store: 1 save
        var expectedCodeKey = CharacterLifecycleService.BuildBloodlineCodeKey(gameServiceId, "IRONBLOOD");
        Assert.Single(codeSavedKeys);
        Assert.Contains(expectedCodeKey, codeSavedKeys);

        // Membership store: 2 saves (origin + ancestor)
        Assert.Equal(2, membershipSavedKeys.Count);
        Assert.Contains(CharacterLifecycleService.BuildBloodlineMemberKey(originCharacterId), membershipSavedKeys);
        Assert.Contains(CharacterLifecycleService.BuildBloodlineMemberKey(ancestorId), membershipSavedKeys);

        // Member list store: 1 save
        Assert.Single(memberListSavedKeys);
        Assert.Contains(CharacterLifecycleService.BuildBloodlineMembersKey(response.BloodlineId), memberListSavedKeys);

        // Assert bloodline model was saved with correct fields
        var typedBloodline = bloodlineSavedModels.First(m => m.BloodlineCode == "IRONBLOOD");
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
        var (service, _, _, _, mockBloodlineStore, mockCodeStore, _, _, _, _, _, _) = CreateService();
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
        mockCodeStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == expectedCodeKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

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
        var (service, _, _, _, mockBloodlineStore, mockCodeStore, _, _, _, mockMessageBus, mockResourceClient, _) = CreateService();
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

        // Capture deleted keys from bloodline store and code store
        var deletedBloodlineKeys = new List<string>();
        mockBloodlineStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) => deletedBloodlineKeys.Add(k))
            .ReturnsAsync(true);
        var deletedCodeKeys = new List<string>();
        mockCodeStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) => deletedCodeKeys.Add(k))
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

        // Assert both bloodline record and code-lookup key deleted (separate typed stores)
        var expectedBloodlineKey = CharacterLifecycleService.BuildBloodlineKey(bloodlineId);
        Assert.Contains(expectedBloodlineKey, deletedBloodlineKeys);
        var expectedCodeKey = CharacterLifecycleService.BuildBloodlineCodeKey(gameServiceId, "STORMBORN");
        Assert.Contains(expectedCodeKey, deletedCodeKeys);

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
        var (service, _, _, _, mockBloodlineStore, mockCodeStore, _, _, _, _, _, _) = CreateService();
        var request = new DeleteBloodlineRequest { BloodlineId = Guid.NewGuid() };

        mockBloodlineStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BloodlineModel?)null);

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
