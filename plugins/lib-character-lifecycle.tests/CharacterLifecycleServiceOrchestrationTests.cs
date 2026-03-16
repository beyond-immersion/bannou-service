using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.CharacterLifecycle;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.CharacterLifecycle.Tests;

/// <summary>
/// Unit tests for CharacterLifecycleService orchestration endpoints (InitiateMarriage, InitiateProcreation, RecordDeath).
/// Tests guard clauses and lock acquisition only. Full multi-service orchestration is tested via HTTP integration tests.
/// </summary>
public class CharacterLifecycleServiceOrchestrationTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ILogger<CharacterLifecycleService>> _mockLogger;
    private readonly CharacterLifecycleServiceConfiguration _configuration;

    private const string PROFILES_STORE = "character-lifecycle-profiles";

    public CharacterLifecycleServiceOrchestrationTests()
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

    #region InitiateMarriage Guard Clauses

    [Fact]
    public async Task InitiateMarriage_ProfileANotFound_ReturnsNotFound()
    {
        // Arrange
        var charAId = Guid.NewGuid();
        var charBId = Guid.NewGuid();
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{charAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileSummary?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();

        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{charAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LifecycleProfileSummary
            {
                CharacterId = charAId,
                GameServiceId = gameServiceId,
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
        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{charBId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileSummary?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
    public async Task InitiateProcreation_ParentANotFound_ReturnsNotFound()
    {
        // Arrange
        var parentAId = Guid.NewGuid();
        var parentBId = Guid.NewGuid();
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{parentAId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileSummary?)null);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

        var service = CreateService();

        // Act
        var (status, _) = await service.InitiateProcreationAsync(
            new InitiateProcreationRequest { ParentAId = parentAId, ParentBId = parentBId, GameServiceId = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region RecordDeath Guard Clauses

    [Fact]
    public async Task RecordDeath_ProfileNotFound_ReturnsNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();
        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LifecycleProfileSummary?)null);
        mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((LifecycleProfileSummary?)null, (string?)null));
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
        var mockProfileStore = new Mock<IStateStore<LifecycleProfileSummary>>();

        var deadProfile = new LifecycleProfileSummary
        {
            CharacterId = characterId,
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            SpeciesCode = "human",
            BirthGameYear = 100,
            CurrentAge = 80,
            CurrentStage = "elder",
            CauseOfCreation = CreationCause.Seeded,
            ChildCount = 2,
            TotalChildCount = 2,
            FertilityModifier = 0.0f,
            HealthModifier = 0.3f,
            Status = LifecycleStatus.Dead,
            DeathGameYear = 180,
            DeathCause = "old age",
            FulfillmentScore = 0.75f,
            AfterlifePath = "ancestral-halls"
        };

        mockProfileStore.Setup(s => s.GetWithETagAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((deadProfile, "etag-1"));
        mockProfileStore.Setup(s => s.GetAsync(
                $"profile:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(deadProfile);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LifecycleProfileSummary>(PROFILES_STORE))
            .Returns(mockProfileStore.Object);

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
