using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Achievement;
using BeyondImmersion.BannouService.Achievement.Sync;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Achievement.Tests;

/// <summary>
/// Unit tests for AchievementService.
/// Tests focus on validation logic and constructor validation.
/// Full CRUD, platform sync, and progress tracking are covered by HTTP tests.
/// </summary>
/// <remarks>
/// The AchievementService has complex dependencies on platform sync providers
/// and state stores. These tests verify input validation and service construction;
/// HTTP tests verify business logic with actual infrastructure.
/// </remarks>
public class AchievementServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<AchievementService>> _mockLogger;
    private readonly AchievementServiceConfiguration _configuration;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IStateStore<AchievementDefinitionData>> _mockDefinitionStore;
    private readonly Mock<IStateStore<EntityProgressData>> _mockProgressStore;
    private readonly List<IPlatformAchievementSync> _platformSyncs;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;

    public AchievementServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<AchievementService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockDefinitionStore = new Mock<IStateStore<AchievementDefinitionData>>();
        _mockProgressStore = new Mock<IStateStore<EntityProgressData>>();
        _platformSyncs = new List<IPlatformAchievementSync>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        _configuration = new AchievementServiceConfiguration
        {
            SteamApiKey = "test-steam-key",
            SteamAppId = "test-app-id",
            XboxClientId = "test-xbox-id",
            XboxClientSecret = "test-xbox-secret",
            PlayStationClientId = "test-ps-id",
            PlayStationClientSecret = "test-ps-secret"
        };

        SetupMinimalMocks();
    }

    private void SetupMinimalMocks()
    {
        // Setup state store factory to return appropriate mocks
        _mockStateStoreFactory
            .Setup(f => f.GetStore<AchievementDefinitionData>(It.IsAny<string>()))
            .Returns(_mockDefinitionStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntityProgressData>(It.IsAny<string>()))
            .Returns(_mockProgressStore.Object);

        // Default TryPublishErrorAsync to prevent exceptions during error handling
        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private AchievementService CreateService()
    {
        return new AchievementService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object,
            _platformSyncs,
            _mockLockProvider.Object);
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void ConstructorIsValid()
        => ServiceConstructorValidator.ValidateServiceConstructor<AchievementService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void Configuration_DefaultValues_AreValid()
    {
        var config = new AchievementServiceConfiguration();

        Assert.True(config.AutoSyncOnUnlock);
        Assert.Equal(3, config.SyncRetryAttempts);
        Assert.Equal(60, config.SyncRetryDelaySeconds);
        Assert.Equal(0, config.ProgressTtlSeconds);
        Assert.Equal(60, config.RarityCalculationIntervalMinutes);
        Assert.Equal(5.0, config.RareThresholdPercent);
    }

    [Fact]
    public void Configuration_CanSetCustomValues()
    {
        var config = new AchievementServiceConfiguration
        {
            AutoSyncOnUnlock = false,
            SyncRetryAttempts = 5,
            SyncRetryDelaySeconds = 120,
            ProgressTtlSeconds = 600,
            RarityCalculationIntervalMinutes = 30,
            RareThresholdPercent = 10.0
        };

        Assert.False(config.AutoSyncOnUnlock);
        Assert.Equal(5, config.SyncRetryAttempts);
        Assert.Equal(120, config.SyncRetryDelaySeconds);
        Assert.Equal(600, config.ProgressTtlSeconds);
        Assert.Equal(30, config.RarityCalculationIntervalMinutes);
        Assert.Equal(10.0, config.RareThresholdPercent);
    }

    #endregion

    #region UpdateAchievementProgress Validation Tests

    [Fact]
    public async Task UpdateAchievementProgressAsync_EntityTypeNotAllowed_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var achievementId = "test-achievement";

        // Setup definition that only allows Account type
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AchievementDefinitionData
            {
                GameServiceId = gameServiceId,
                AchievementId = achievementId,
                EntityTypes = new List<EntityType> { EntityType.Account },
                AchievementType = AchievementType.Progressive,
                ProgressTarget = 100
            });

        var request = new UpdateAchievementProgressRequest
        {
            GameServiceId = gameServiceId,
            AchievementId = achievementId,
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Character, // Not allowed
            Increment = 10
        };

        // Act
        var (status, _) = await service.UpdateAchievementProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task UpdateAchievementProgressAsync_NonProgressiveAchievement_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var achievementId = "test-achievement";

        // Setup definition as standard (not progressive)
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AchievementDefinitionData
            {
                GameServiceId = gameServiceId,
                AchievementId = achievementId,
                EntityTypes = new List<EntityType> { EntityType.Account },
                AchievementType = AchievementType.Standard, // Not progressive
                ProgressTarget = null
            });

        var request = new UpdateAchievementProgressRequest
        {
            GameServiceId = gameServiceId,
            AchievementId = achievementId,
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Account,
            Increment = 10
        };

        // Act
        var (status, _) = await service.UpdateAchievementProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task UpdateAchievementProgressAsync_MissingProgressTarget_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var achievementId = "test-achievement";

        // Setup definition as progressive but without target
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AchievementDefinitionData
            {
                GameServiceId = gameServiceId,
                AchievementId = achievementId,
                EntityTypes = new List<EntityType> { EntityType.Account },
                AchievementType = AchievementType.Progressive,
                ProgressTarget = null // Missing target
            });

        var request = new UpdateAchievementProgressRequest
        {
            GameServiceId = gameServiceId,
            AchievementId = achievementId,
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Account,
            Increment = 10
        };

        // Act
        var (status, _) = await service.UpdateAchievementProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region UnlockAchievement Validation Tests

    [Fact]
    public async Task UnlockAchievementAsync_EntityTypeNotAllowed_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var achievementId = "test-achievement";

        // Setup definition that only allows Account type
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AchievementDefinitionData
            {
                GameServiceId = gameServiceId,
                AchievementId = achievementId,
                EntityTypes = new List<EntityType> { EntityType.Account },
                AchievementType = AchievementType.Standard
            });

        var request = new UnlockAchievementRequest
        {
            GameServiceId = gameServiceId,
            AchievementId = achievementId,
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Guild // Not allowed
        };

        // Act
        var (status, _) = await service.UnlockAchievementAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task UnlockAchievementAsync_PrerequisiteNotMet_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var achievementId = "advanced-achievement";
        var entityId = Guid.NewGuid();

        // Setup definition with prerequisite
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AchievementDefinitionData
            {
                GameServiceId = gameServiceId,
                AchievementId = achievementId,
                EntityTypes = new List<EntityType> { EntityType.Account },
                AchievementType = AchievementType.Standard,
                Prerequisites = new List<string> { "beginner-achievement" }
            });

        // Setup progress data without the prerequisite unlocked
        _mockProgressStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityProgressData
            {
                EntityId = entityId,
                EntityType = EntityType.Account,
                Achievements = new Dictionary<string, AchievementProgressData>
                {
                    // No beginner-achievement entry
                }
            });

        var request = new UnlockAchievementRequest
        {
            GameServiceId = gameServiceId,
            AchievementId = achievementId,
            EntityId = entityId,
            EntityType = EntityType.Account
        };

        // Act
        var (status, _) = await service.UnlockAchievementAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task UnlockAchievementAsync_PrerequisiteNotUnlocked_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var achievementId = "advanced-achievement";
        var entityId = Guid.NewGuid();

        // Setup definition with prerequisite
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AchievementDefinitionData
            {
                GameServiceId = gameServiceId,
                AchievementId = achievementId,
                EntityTypes = new List<EntityType> { EntityType.Account },
                AchievementType = AchievementType.Standard,
                Prerequisites = new List<string> { "beginner-achievement" }
            });

        // Setup progress data with prerequisite in progress but not unlocked
        _mockProgressStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityProgressData
            {
                EntityId = entityId,
                EntityType = EntityType.Account,
                Achievements = new Dictionary<string, AchievementProgressData>
                {
                    ["beginner-achievement"] = new AchievementProgressData
                    {
                        CurrentProgress = 50,
                        TargetProgress = 100,
                        IsUnlocked = false // Not yet unlocked
                    }
                }
            });

        var request = new UnlockAchievementRequest
        {
            GameServiceId = gameServiceId,
            AchievementId = achievementId,
            EntityId = entityId,
            EntityType = EntityType.Account
        };

        // Act
        var (status, _) = await service.UnlockAchievementAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region SyncPlatformAchievements Validation Tests

    [Fact]
    public async Task SyncPlatformAchievementsAsync_NonAccountEntityType_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new SyncPlatformAchievementsRequest
        {
            GameServiceId = Guid.NewGuid(),
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Character, // Must be Account for platform sync
            Platform = Platform.Steam
        };

        // Act
        var (status, _) = await service.SyncPlatformAchievementsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SyncPlatformAchievementsAsync_NoPlatformProvider_ReturnsBadRequest()
    {
        // Arrange - service created with empty platform syncs
        var service = CreateService();

        var request = new SyncPlatformAchievementsRequest
        {
            GameServiceId = Guid.NewGuid(),
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Account,
            Platform = Platform.Steam // No provider registered
        };

        // Act
        var (status, response) = await service.SyncPlatformAchievementsAsync(request, CancellationToken.None);

        // Assert - when no provider is registered, service returns BadRequest
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region GetPlatformSyncStatus Validation Tests

    [Fact]
    public async Task GetPlatformSyncStatusAsync_NonAccountEntityType_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new GetPlatformSyncStatusRequest
        {
            GameServiceId = Guid.NewGuid(),
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Guild // Must be Account
        };

        // Act
        var (status, _) = await service.GetPlatformSyncStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Definition NotFound Tests

    [Fact]
    public async Task GetAchievementDefinitionAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AchievementDefinitionData?)null);

        var request = new GetAchievementDefinitionRequest
        {
            GameServiceId = Guid.NewGuid(),
            AchievementId = "non-existent"
        };

        // Act
        var (status, _) = await service.GetAchievementDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdateAchievementProgressAsync_AchievementNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AchievementDefinitionData?)null);

        var request = new UpdateAchievementProgressRequest
        {
            GameServiceId = Guid.NewGuid(),
            AchievementId = "non-existent",
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Account,
            Increment = 10
        };

        // Act
        var (status, _) = await service.UpdateAchievementProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UnlockAchievementAsync_AchievementNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AchievementDefinitionData?)null);

        var request = new UnlockAchievementRequest
        {
            GameServiceId = Guid.NewGuid(),
            AchievementId = "non-existent",
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Account
        };

        // Act
        var (status, _) = await service.UnlockAchievementAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion
}
