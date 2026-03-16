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
/// Unit tests for CharacterLifecycleService orchestration endpoints (InitiateMarriage, InitiateProcreation, RecordDeath).
/// Tests guard clauses and lock acquisition only. Full multi-service orchestration is tested via HTTP integration tests.
/// </summary>
public class CharacterLifecycleServiceOrchestrationTests
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
    private readonly Mock<IStateStore<object>> _mockHeritageStore;
    private readonly Mock<IStateStore<object>> _mockBloodlineStore;
    private readonly Mock<IStateStore<object>> _mockCacheStore;

    private const string PROFILES_STORE = "character-lifecycle-profiles";
    private const string HERITAGE_STORE = "character-lifecycle-heritage";
    private const string BLOODLINES_STORE = "character-lifecycle-bloodlines";
    private const string CACHE_STORE = "character-lifecycle-cache";

    public CharacterLifecycleServiceOrchestrationTests()
    {
        _mockLogger = new Mock<ILogger<CharacterLifecycleService>>();
        _configuration = new CharacterLifecycleServiceConfiguration();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
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

        // Constructor-cached store mocks
        _mockProfileStore = new Mock<IStateStore<LifecycleProfileModel>>();
        _mockHeritageStore = new Mock<IStateStore<object>>();
        _mockBloodlineStore = new Mock<IStateStore<object>>();
        _mockCacheStore = new Mock<IStateStore<object>>();

        // Wire state store factory to return typed stores (constructor-cached)
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileModel>(PROFILES_STORE))
            .Returns(_mockProfileStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<object>(HERITAGE_STORE))
            .Returns(_mockHeritageStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<object>(BLOODLINES_STORE))
            .Returns(_mockBloodlineStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<object>(CACHE_STORE))
            .Returns(_mockCacheStore.Object);

        // Default lock provider behavior - always succeed with proper disposable
        SetupLockSucceeds();

        // Default message bus behavior
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Configures the lock provider to succeed on all lock requests.
    /// </summary>
    private void SetupLockSucceeds()
    {
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        mockLockResponse.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    /// <summary>
    /// Configures the lock provider to fail on all lock requests.
    /// </summary>
    private void SetupLockFails()
    {
        var failedLockResponse = new Mock<ILockResponse>();
        failedLockResponse.Setup(r => r.Success).Returns(false);
        failedLockResponse.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse.Object);
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
            BirthGameYear = 100,
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

    #region InitiateMarriage Guard Clauses

    [Fact]
    public async Task InitiateMarriage_LockFails_ReturnsConflict()
    {
        // Arrange
        SetupLockFails();
        var service = CreateService();

        // Act
        var (status, _) = await service.InitiateMarriageAsync(
            new InitiateMarriageRequest { CharacterAId = Guid.NewGuid(), CharacterBId = Guid.NewGuid(), GameServiceId = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task InitiateMarriage_ProfileANotFound_ReturnsNotFound()
    {
        // Arrange
        var charAId = Guid.NewGuid();
        var charBId = Guid.NewGuid();

        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{charAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, _) = await service.InitiateMarriageAsync(
            new InitiateMarriageRequest { CharacterAId = charAId, CharacterBId = charBId, GameServiceId = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task InitiateMarriage_ProfileBNotFound_ReturnsNotFound()
    {
        // Arrange
        var charAId = Guid.NewGuid();
        var charBId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();

        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{charAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProfile(charAId, gameServiceId));
        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{charBId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, _) = await service.InitiateMarriageAsync(
            new InitiateMarriageRequest { CharacterAId = charAId, CharacterBId = charBId, GameServiceId = gameServiceId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region InitiateProcreation Guard Clauses

    [Fact]
    public async Task InitiateProcreation_LockFails_ReturnsConflict()
    {
        // Arrange
        SetupLockFails();
        var service = CreateService();

        // Act
        var (status, _) = await service.InitiateProcreationAsync(
            new InitiateProcreationRequest { ParentAId = Guid.NewGuid(), ParentBId = Guid.NewGuid(), GameServiceId = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task InitiateProcreation_ParentANotFound_ReturnsNotFound()
    {
        // Arrange
        var parentAId = Guid.NewGuid();
        var parentBId = Guid.NewGuid();

        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{parentAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, _) = await service.InitiateProcreationAsync(
            new InitiateProcreationRequest { ParentAId = parentAId, ParentBId = parentBId, GameServiceId = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task InitiateProcreation_ParentBNotFound_ReturnsNotFound()
    {
        // Arrange
        var parentAId = Guid.NewGuid();
        var parentBId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();

        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{parentAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProfile(parentAId, gameServiceId));
        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{parentBId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, _) = await service.InitiateProcreationAsync(
            new InitiateProcreationRequest { ParentAId = parentAId, ParentBId = parentBId, GameServiceId = gameServiceId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region RecordDeath Guard Clauses

    [Fact]
    public async Task RecordDeath_LockFails_ReturnsConflict()
    {
        // Arrange
        SetupLockFails();
        var service = CreateService();

        // Act
        var (status, _) = await service.RecordDeathAsync(
            new RecordDeathRequest { CharacterId = Guid.NewGuid(), DeathCause = "old age" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task RecordDeath_ProfileNotFound_ReturnsNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        _mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((LifecycleProfileModel?)null, (string?)null));
        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileModel?)null);

        var service = CreateService();

        // Act
        var (status, _) = await service.RecordDeathAsync(
            new RecordDeathRequest { CharacterId = characterId, DeathCause = "old age" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task RecordDeath_AlreadyDead_ReturnsOkIdempotent()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        var deadProfile = CreateTestProfile(characterId, currentAge: 80, currentStage: "elder", status: LifecycleStatus.Dead);
        deadProfile.DeathGameYear = 180;
        deadProfile.DeathCause = "old age";
        deadProfile.FulfillmentScore = 0.75f;
        deadProfile.AfterlifePath = "ancestral-halls";
        deadProfile.ChildCount = 2;
        deadProfile.TotalChildCount = 2;
        deadProfile.FertilityModifier = 0.0f;
        deadProfile.HealthModifier = 0.3f;

        _mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((deadProfile, "etag-1"));
        _mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(deadProfile);

        var service = CreateService();

        // Act
        var (status, response) = await service.RecordDeathAsync(
            new RecordDeathRequest { CharacterId = characterId, DeathCause = "combat" },
            CancellationToken.None);

        // Assert — idempotent return with existing death data
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0.75f, response.FulfillmentScore);
        Assert.Equal("ancestral-halls", response.AfterlifePath);
    }

    #endregion
}
