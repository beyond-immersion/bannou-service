using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.CharacterLifecycle;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
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
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ILogger<CharacterLifecycleService>> _mockLogger;

    // Heritage store mocks (templates live here)
    private readonly Mock<IStateStore<object>> _mockHeritageStore;

    // Bloodline store mocks
    private readonly Mock<IStateStore<object>> _mockBloodlineStore;

    // Cache store mock
    private readonly Mock<IStateStore<object>> _mockCacheStore;

    private const string HERITAGE_STORE = "character-lifecycle-heritage";
    private const string BLOODLINE_STORE = "character-lifecycle-bloodlines";
    private const string CACHE_STORE = "character-lifecycle-cache";

    public CharacterLifecycleServiceTemplateTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockLogger = new Mock<ILogger<CharacterLifecycleService>>();
        _mockHeritageStore = new Mock<IStateStore<object>>();
        _mockBloodlineStore = new Mock<IStateStore<object>>();
        _mockCacheStore = new Mock<IStateStore<object>>();

        // Wire up state store factory to return typed mocks
        _mockStateStoreFactory
            .Setup(f => f.GetStore<object>(HERITAGE_STORE))
            .Returns(_mockHeritageStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<object>(BLOODLINE_STORE))
            .Returns(_mockBloodlineStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<object>(CACHE_STORE))
            .Returns(_mockCacheStore.Object);
    }

    /// <summary>
    /// Creates a CharacterLifecycleService with the current mock configuration.
    /// </summary>
    private CharacterLifecycleService CreateService()
    {
        return new CharacterLifecycleService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockResourceClient.Object,
            _mockLogger.Object,
            Configuration);
    }

    // ========================================================================
    // SeedLifecycleTemplate
    // ========================================================================

    [Fact]
    public async Task SeedLifecycleTemplateAsync_ValidRequest_Returns200AndSavesTemplateAndPublishesEvent()
    {
        // Arrange
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
        _mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Capture saves and events
        string? savedKey = null;
        object? savedModel = null;
        _mockHeritageStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedLifecycleTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedKey);
        Assert.Contains("human", savedKey);
        Assert.Contains(gameServiceId.ToString(), savedKey);
        Assert.NotNull(savedModel);
        Assert.Equal("character-lifecycle.lifecycle-template.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    [Fact]
    public async Task SeedLifecycleTemplateAsync_TemplateAlreadyExists_Returns409()
    {
        // Arrange
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
        _mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedLifecycleTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    // ========================================================================
    // SeedHeritableTraitTemplate
    // ========================================================================

    [Fact]
    public async Task SeedHeritableTraitTemplateAsync_ValidRequest_Returns200AndSavesAndPublishes()
    {
        // Arrange
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

        _mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        string? savedKey = null;
        object? savedModel = null;
        _mockHeritageStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedHeritableTraitTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedKey);
        Assert.Contains("elf", savedKey);
        Assert.Contains(gameServiceId.ToString(), savedKey);
        Assert.NotNull(savedModel);
        Assert.Equal("character-lifecycle.heritable-trait-template.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    [Fact]
    public async Task SeedHeritableTraitTemplateAsync_AlreadyExists_Returns409()
    {
        // Arrange
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

        _mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedHeritableTraitTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    // ========================================================================
    // SeedHybridTemplate
    // ========================================================================

    [Fact]
    public async Task SeedHybridTemplateAsync_ValidRequest_Returns200AndSavesAndPublishes()
    {
        // Arrange
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

        _mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        string? savedKey = null;
        object? savedModel = null;
        _mockHeritageStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedHybridTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedKey);
        Assert.Contains("human", savedKey);
        Assert.Contains("elf", savedKey);
        Assert.Contains(gameServiceId.ToString(), savedKey);
        Assert.NotNull(savedModel);
        Assert.Equal("character-lifecycle.hybrid-trait-template.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    [Fact]
    public async Task SeedHybridTemplateAsync_AlreadyExists_Returns409()
    {
        // Arrange
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

        _mockHeritageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());

        var service = CreateService();

        // Act
        var (status, response) = await service.SeedHybridTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    // ========================================================================
    // EstablishBloodline
    // ========================================================================

    [Fact]
    public async Task EstablishBloodlineAsync_ValidRequest_Returns200AndSavesRecordsAndPublishesEvents()
    {
        // Arrange
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
        _mockBloodlineStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Capture all saves to bloodline store
        var savedKeys = new List<string>();
        var savedModels = new List<object>();
        _mockBloodlineStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKeys.Add(k);
                savedModels.Add(m);
            })
            .ReturnsAsync("etag");

        // Allow cache deletes
        _mockCacheStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Capture all published events
        var capturedTopics = new List<string>();
        var capturedEvents = new List<object>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopics.Add(t);
                capturedEvents.Add(e);
            })
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var (status, response) = await service.EstablishBloodlineAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.BloodlineId);

        // Verify multiple records saved: bloodline, code lookup, members
        Assert.True(savedKeys.Count >= 2, "Expected at least bloodline and code lookup saves");

        // Verify code lookup key contains gameServiceId and bloodlineCode
        Assert.Contains(savedKeys, k => k.Contains("IRONBLOOD") && k.Contains(gameServiceId.ToString()));

        // Verify both formed and created events published
        Assert.Contains(capturedTopics, t => t == "character-lifecycle.bloodline.formed");
        Assert.Contains(capturedTopics, t => t == "character-lifecycle.bloodline.created");
        Assert.True(capturedEvents.Count >= 2, "Expected at least formed and created events");
    }

    [Fact]
    public async Task EstablishBloodlineAsync_BloodlineCodeExists_Returns409()
    {
        // Arrange
        var request = new EstablishBloodlineRequest
        {
            BloodlineCode = "IRONBLOOD",
            GameServiceId = Guid.NewGuid(),
            OriginCharacterId = Guid.NewGuid(),
            TraitSignature = new[] { "strength" }
        };

        // Existing bloodline with this code
        _mockBloodlineStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("IRONBLOOD")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());

        var service = CreateService();

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
    public async Task DeleteBloodlineAsync_BloodlineExists_Returns200AndDeletesAndCleansUpAndPublishes()
    {
        // Arrange
        var bloodlineId = Guid.NewGuid();
        var request = new DeleteBloodlineRequest { BloodlineId = bloodlineId };

        // Bloodline exists in store
        _mockBloodlineStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains(bloodlineId.ToString())),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());

        _mockBloodlineStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Capture resource cleanup call
        ExecuteCleanupRequest? capturedCleanupRequest = null;
        _mockResourceClient
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
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var (status, response) = await service.DeleteBloodlineAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify bloodline record deleted
        _mockBloodlineStore.Verify(
            s => s.DeleteAsync(
                It.Is<string>(k => k.Contains(bloodlineId.ToString())),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // Verify resource cleanup was called with correct parameters
        Assert.NotNull(capturedCleanupRequest);
        Assert.Equal(bloodlineId, capturedCleanupRequest.ResourceId);
        Assert.Equal("bloodline", capturedCleanupRequest.ResourceType);

        // Verify deleted event published
        Assert.Equal("character-lifecycle.bloodline.deleted", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    [Fact]
    public async Task DeleteBloodlineAsync_NotFound_Returns404()
    {
        // Arrange
        var request = new DeleteBloodlineRequest { BloodlineId = Guid.NewGuid() };

        _mockBloodlineStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var service = CreateService();

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
