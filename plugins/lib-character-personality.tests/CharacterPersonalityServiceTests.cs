using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.CharacterPersonality;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.CharacterPersonality.Tests;

/// <summary>
/// Comprehensive unit tests for CharacterPersonalityService.
/// Tests all 9 service endpoints with success, failure, and edge cases.
/// </summary>
public class CharacterPersonalityServiceTests
{
    private readonly Mock<ILogger<CharacterPersonalityService>> _mockLogger;
    private readonly CharacterPersonalityServiceConfiguration _configuration;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<PersonalityData>> _mockPersonalityStore;
    private readonly Mock<IStateStore<CombatPreferencesData>> _mockCombatStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    // Capture lists for verifying published events
    private readonly List<(string Topic, object Event)> _publishedEvents = new();

    public CharacterPersonalityServiceTests()
    {
        _mockLogger = new Mock<ILogger<CharacterPersonalityService>>();
        _configuration = new CharacterPersonalityServiceConfiguration();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockPersonalityStore = new Mock<IStateStore<PersonalityData>>();
        _mockCombatStore = new Mock<IStateStore<CombatPreferencesData>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<PersonalityData>(It.IsAny<string>()))
            .Returns(_mockPersonalityStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CombatPreferencesData>(It.IsAny<string>()))
            .Returns(_mockCombatStore.Object);

        // Setup default behavior for state stores
        _mockPersonalityStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PersonalityData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockPersonalityStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCombatStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CombatPreferencesData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockCombatStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup message bus to capture published events using the 3-argument overload
        // The service calls TryPublishAsync(topic, event, cancellationToken: ct)
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<PersonalityCreatedEvent>(It.IsAny<string>(), It.IsAny<PersonalityCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, PersonalityCreatedEvent, CancellationToken>((topic, evt, _) => _publishedEvents.Add((topic, evt)))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<PersonalityUpdatedEvent>(It.IsAny<string>(), It.IsAny<PersonalityUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, PersonalityUpdatedEvent, CancellationToken>((topic, evt, _) => _publishedEvents.Add((topic, evt)))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<PersonalityEvolvedEvent>(It.IsAny<string>(), It.IsAny<PersonalityEvolvedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, PersonalityEvolvedEvent, CancellationToken>((topic, evt, _) => _publishedEvents.Add((topic, evt)))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<PersonalityDeletedEvent>(It.IsAny<string>(), It.IsAny<PersonalityDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, PersonalityDeletedEvent, CancellationToken>((topic, evt, _) => _publishedEvents.Add((topic, evt)))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<CombatPreferencesCreatedEvent>(It.IsAny<string>(), It.IsAny<CombatPreferencesCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, CombatPreferencesCreatedEvent, CancellationToken>((topic, evt, _) => _publishedEvents.Add((topic, evt)))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<CombatPreferencesUpdatedEvent>(It.IsAny<string>(), It.IsAny<CombatPreferencesUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, CombatPreferencesUpdatedEvent, CancellationToken>((topic, evt, _) => _publishedEvents.Add((topic, evt)))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<CombatPreferencesEvolvedEvent>(It.IsAny<string>(), It.IsAny<CombatPreferencesEvolvedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, CombatPreferencesEvolvedEvent, CancellationToken>((topic, evt, _) => _publishedEvents.Add((topic, evt)))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishAsync<CombatPreferencesDeletedEvent>(It.IsAny<string>(), It.IsAny<CombatPreferencesDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, CombatPreferencesDeletedEvent, CancellationToken>((topic, evt, _) => _publishedEvents.Add((topic, evt)))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Helper method to create a CharacterPersonalityService with all required dependencies.
    /// </summary>
    private CharacterPersonalityService CreateService()
    {
        return new CharacterPersonalityService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);
    }

    /// <summary>
    /// Creates a sample PersonalityData for testing.
    /// </summary>
    private static PersonalityData CreateTestPersonalityData(Guid characterId, int version = 1)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new PersonalityData
        {
            CharacterId = characterId,
            Traits = new Dictionary<TraitAxis, float>
            {
                { TraitAxis.OPENNESS, 0.5f },
                { TraitAxis.CONSCIENTIOUSNESS, 0.3f },
                { TraitAxis.EXTRAVERSION, -0.2f },
                { TraitAxis.AGREEABLENESS, 0.7f },
                { TraitAxis.NEUROTICISM, -0.4f },
                { TraitAxis.HONESTY, 0.6f },
                { TraitAxis.AGGRESSION, -0.5f },
                { TraitAxis.LOYALTY, 0.8f }
            },
            Version = version,
            CreatedAtUnix = now - 3600, // 1 hour ago
            UpdatedAtUnix = now
        };
    }

    /// <summary>
    /// Creates a sample CombatPreferencesData for testing.
    /// </summary>
    private static CombatPreferencesData CreateTestCombatData(Guid characterId, int version = 1)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new CombatPreferencesData
        {
            CharacterId = characterId,
            Style = CombatStyle.BALANCED,
            PreferredRange = PreferredRange.MEDIUM,
            GroupRole = GroupRole.FRONTLINE,
            RiskTolerance = 0.5f,
            RetreatThreshold = 0.3f,
            ProtectAllies = true,
            Version = version,
            CreatedAtUnix = now - 3600,
            UpdatedAtUnix = now
        };
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void CharacterPersonalityService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<CharacterPersonalityService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void CharacterPersonalityServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new CharacterPersonalityServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    #endregion

    #region GetPersonality Tests

    [Fact]
    public async Task GetPersonalityAsync_WithExistingPersonality_ShouldReturnOkWithPersonality()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var testData = CreateTestPersonalityData(characterId);

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        var service = CreateService();
        var request = new GetPersonalityRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetPersonalityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(testData.Version, response.Version);
        Assert.Equal(8, response.Traits.Count);
    }

    [Fact]
    public async Task GetPersonalityAsync_WithNonExistentPersonality_ShouldReturnNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersonalityData?)null);

        var service = CreateService();
        var request = new GetPersonalityRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetPersonalityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetPersonalityAsync_WhenStateStoreThrows_ShouldReturnInternalServerError()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store connection failed"));

        var service = CreateService();
        var request = new GetPersonalityRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetPersonalityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);

        // Verify error event was published
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "character-personality",
            "GetPersonality",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region SetPersonality Tests

    [Fact]
    public async Task SetPersonalityAsync_CreatingNew_ShouldReturnOkAndPublishCreatedEvent()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersonalityData?)null);

        var service = CreateService();
        var request = new SetPersonalityRequest
        {
            CharacterId = characterId,
            Traits = new List<TraitValue>
            {
                new() { Axis = TraitAxis.OPENNESS, Value = 0.5f },
                new() { Axis = TraitAxis.CONSCIENTIOUSNESS, Value = 0.3f }
            }
        };

        // Act
        var (status, response) = await service.SetPersonalityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(1, response.Version);

        // Verify save was called
        _mockPersonalityStore.Verify(s => s.SaveAsync(
            $"personality-{characterId}",
            It.Is<PersonalityData>(d => d.Version == 1),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify created event was published
        var createdEvent = _publishedEvents.FirstOrDefault(e => e.Topic == "personality.created");
        Assert.NotNull(createdEvent.Event);
        Assert.IsType<PersonalityCreatedEvent>(createdEvent.Event);
        var evt = (PersonalityCreatedEvent)createdEvent.Event;
        Assert.Equal(characterId, evt.CharacterId);
        Assert.Equal(1, evt.Version);
    }

    [Fact]
    public async Task SetPersonalityAsync_UpdatingExisting_ShouldIncrementVersionAndPublishUpdatedEvent()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestPersonalityData(characterId, version: 3);

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingData);

        var service = CreateService();
        var request = new SetPersonalityRequest
        {
            CharacterId = characterId,
            Traits = new List<TraitValue>
            {
                new() { Axis = TraitAxis.OPENNESS, Value = 0.9f }
            }
        };

        // Act
        var (status, response) = await service.SetPersonalityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(4, response.Version); // Incremented from 3

        // Verify updated event was published
        var updatedEvent = _publishedEvents.FirstOrDefault(e => e.Topic == "personality.updated");
        Assert.NotNull(updatedEvent.Event);
        Assert.IsType<PersonalityUpdatedEvent>(updatedEvent.Event);
    }

    [Fact]
    public async Task SetPersonalityAsync_WhenSaveFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersonalityData?)null);
        _mockPersonalityStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PersonalityData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Save failed"));

        var service = CreateService();
        var request = new SetPersonalityRequest
        {
            CharacterId = characterId,
            Traits = new List<TraitValue> { new() { Axis = TraitAxis.OPENNESS, Value = 0.5f } }
        };

        // Act
        var (status, response) = await service.SetPersonalityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
    }

    #endregion

    #region DeletePersonality Tests

    [Fact]
    public async Task DeletePersonalityAsync_WithExistingPersonality_ShouldReturnOkAndPublishDeletedEvent()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestPersonalityData(characterId);

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingData);

        var service = CreateService();
        var request = new DeletePersonalityRequest { CharacterId = characterId };

        // Act
        var status = await service.DeletePersonalityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify delete was called
        _mockPersonalityStore.Verify(s => s.DeleteAsync($"personality-{characterId}", It.IsAny<CancellationToken>()), Times.Once);

        // Verify deleted event was published
        var deletedEvent = _publishedEvents.FirstOrDefault(e => e.Topic == "personality.deleted");
        Assert.NotNull(deletedEvent.Event);
        Assert.IsType<PersonalityDeletedEvent>(deletedEvent.Event);
    }

    [Fact]
    public async Task DeletePersonalityAsync_WithNonExistentPersonality_ShouldReturnNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersonalityData?)null);

        var service = CreateService();
        var request = new DeletePersonalityRequest { CharacterId = characterId };

        // Act
        var status = await service.DeletePersonalityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);

        // Verify delete was NOT called
        _mockPersonalityStore.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region BatchGetPersonalities Tests

    [Fact]
    public async Task BatchGetPersonalitiesAsync_WithMixedResults_ShouldReturnFoundAndNotFound()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var existingData = CreateTestPersonalityData(existingId);

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{existingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingData);
        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{missingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersonalityData?)null);

        var service = CreateService();
        var request = new BatchGetPersonalitiesRequest
        {
            CharacterIds = new List<Guid> { existingId, missingId }
        };

        // Act
        var (status, response) = await service.BatchGetPersonalitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Personalities);
        Assert.Single(response.NotFound);
        Assert.Equal(existingId, response.Personalities.First().CharacterId);
        Assert.Contains(missingId, response.NotFound);
    }

    [Fact]
    public async Task BatchGetPersonalitiesAsync_ExceedingMaxLimit_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new BatchGetPersonalitiesRequest
        {
            CharacterIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList()
        };

        // Act
        var (status, response) = await service.BatchGetPersonalitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BatchGetPersonalitiesAsync_AtMaxLimit_ShouldSucceed()
    {
        // Arrange
        var service = CreateService();
        var request = new BatchGetPersonalitiesRequest
        {
            CharacterIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList()
        };

        // All return null (not found)
        _mockPersonalityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersonalityData?)null);

        // Act
        var (status, response) = await service.BatchGetPersonalitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Personalities);
        Assert.Equal(100, response.NotFound.Count);
    }

    [Fact]
    public async Task BatchGetPersonalitiesAsync_AllFound_ShouldReturnAllPersonalities()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{id1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestPersonalityData(id1));
        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{id2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestPersonalityData(id2));

        var service = CreateService();
        var request = new BatchGetPersonalitiesRequest
        {
            CharacterIds = new List<Guid> { id1, id2 }
        };

        // Act
        var (status, response) = await service.BatchGetPersonalitiesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Personalities.Count);
        Assert.Empty(response.NotFound);
    }

    #endregion

    #region RecordExperience Tests

    [Fact]
    public async Task RecordExperienceAsync_WithNonExistentPersonality_ShouldReturnNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersonalityData?)null);

        var service = CreateService();
        var request = new RecordExperienceRequest
        {
            CharacterId = characterId,
            ExperienceType = ExperienceType.TRAUMA,
            Intensity = 1.0f
        };

        // Act
        var (status, response) = await service.RecordExperienceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RecordExperienceAsync_WithExistingPersonality_ShouldReturnResult()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestPersonalityData(characterId);

        _mockPersonalityStore
            .Setup(s => s.GetWithETagAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingData, "etag-1"));
        _mockPersonalityStore
            .Setup(s => s.TrySaveAsync($"personality-{characterId}", It.IsAny<PersonalityData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var service = CreateService();
        var request = new RecordExperienceRequest
        {
            CharacterId = characterId,
            ExperienceType = ExperienceType.VICTORY,
            Intensity = 0.5f
        };

        // Act
        var (status, response) = await service.RecordExperienceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.True(response.ExperienceRecorded);
        // Evolution is probabilistic, so we just verify the result contains valid data
    }

    [Fact]
    public async Task RecordExperienceAsync_WhenEvolutionOccurs_ShouldUpdateStateAndPublishEvent()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestPersonalityData(characterId, version: 1);

        _mockPersonalityStore
            .Setup(s => s.GetWithETagAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingData, "etag-1"));
        _mockPersonalityStore
            .Setup(s => s.TrySaveAsync($"personality-{characterId}", It.IsAny<PersonalityData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var service = CreateService();

        // Run multiple times to get at least one evolution (15% * 1.0 = 15% chance each)
        var evolutionOccurred = false;
        for (int i = 0; i < 100 && !evolutionOccurred; i++)
        {
            _publishedEvents.Clear();
            var request = new RecordExperienceRequest
            {
                CharacterId = characterId,
                ExperienceType = ExperienceType.TRAUMA,
                Intensity = 1.0f // Max intensity for highest evolution chance
            };

            var (status, response) = await service.RecordExperienceAsync(request, CancellationToken.None);

            if (response?.PersonalityEvolved == true)
            {
                evolutionOccurred = true;

                // Assert evolution happened correctly
                Assert.NotNull(response.ChangedTraits);
                Assert.True(response.ChangedTraits.Count > 0);
                Assert.NotNull(response.NewVersion);

                // Verify evolved event was published
                var evolvedEvent = _publishedEvents.FirstOrDefault(e => e.Topic == "personality.evolved");
                Assert.NotNull(evolvedEvent.Event);
                Assert.IsType<PersonalityEvolvedEvent>(evolvedEvent.Event);
            }
        }

        // With 100 iterations at 15% chance, we should have gotten at least one evolution
        Assert.True(evolutionOccurred, "Expected at least one evolution to occur in 100 iterations");
    }

    [Theory]
    [InlineData(ExperienceType.TRAUMA)]
    [InlineData(ExperienceType.BETRAYAL)]
    [InlineData(ExperienceType.LOSS)]
    [InlineData(ExperienceType.VICTORY)]
    [InlineData(ExperienceType.FRIENDSHIP)]
    [InlineData(ExperienceType.REDEMPTION)]
    [InlineData(ExperienceType.CORRUPTION)]
    [InlineData(ExperienceType.ENLIGHTENMENT)]
    [InlineData(ExperienceType.SACRIFICE)]
    public async Task RecordExperienceAsync_AllExperienceTypes_ShouldBeHandled(ExperienceType experienceType)
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestPersonalityData(characterId);

        _mockPersonalityStore
            .Setup(s => s.GetWithETagAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingData, "etag-1"));
        _mockPersonalityStore
            .Setup(s => s.TrySaveAsync($"personality-{characterId}", It.IsAny<PersonalityData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var service = CreateService();
        var request = new RecordExperienceRequest
        {
            CharacterId = characterId,
            ExperienceType = experienceType,
            Intensity = 0.5f
        };

        // Act
        var (status, response) = await service.RecordExperienceAsync(request, CancellationToken.None);

        // Assert - all experience types should complete without error
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.ExperienceRecorded);
    }

    #endregion

    #region GetCombatPreferences Tests

    [Fact]
    public async Task GetCombatPreferencesAsync_WithExistingPreferences_ShouldReturnOk()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var testData = CreateTestCombatData(characterId);

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        var service = CreateService();
        var request = new GetCombatPreferencesRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCombatPreferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(CombatStyle.BALANCED, response.Preferences.Style);
        Assert.Equal(PreferredRange.MEDIUM, response.Preferences.PreferredRange);
        Assert.Equal(GroupRole.FRONTLINE, response.Preferences.GroupRole);
    }

    [Fact]
    public async Task GetCombatPreferencesAsync_WithNonExistent_ShouldReturnNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CombatPreferencesData?)null);

        var service = CreateService();
        var request = new GetCombatPreferencesRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCombatPreferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region SetCombatPreferences Tests

    [Fact]
    public async Task SetCombatPreferencesAsync_CreatingNew_ShouldReturnOkAndPublishCreatedEvent()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CombatPreferencesData?)null);

        var service = CreateService();
        var request = new SetCombatPreferencesRequest
        {
            CharacterId = characterId,
            Preferences = new CombatPreferences
            {
                Style = CombatStyle.AGGRESSIVE,
                PreferredRange = PreferredRange.MELEE,
                GroupRole = GroupRole.FLANKER,
                RiskTolerance = 0.8f,
                RetreatThreshold = 0.2f,
                ProtectAllies = false
            }
        };

        // Act
        var (status, response) = await service.SetCombatPreferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.Version);
        Assert.Equal(CombatStyle.AGGRESSIVE, response.Preferences.Style);

        // Verify created event
        var createdEvent = _publishedEvents.FirstOrDefault(e => e.Topic == "combat-preferences.created");
        Assert.NotNull(createdEvent.Event);
        Assert.IsType<CombatPreferencesCreatedEvent>(createdEvent.Event);
    }

    [Fact]
    public async Task SetCombatPreferencesAsync_UpdatingExisting_ShouldIncrementVersionAndPublishUpdatedEvent()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestCombatData(characterId, version: 5);

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingData);

        var service = CreateService();
        var request = new SetCombatPreferencesRequest
        {
            CharacterId = characterId,
            Preferences = new CombatPreferences
            {
                Style = CombatStyle.DEFENSIVE,
                PreferredRange = PreferredRange.RANGED,
                GroupRole = GroupRole.SUPPORT,
                RiskTolerance = 0.1f,
                RetreatThreshold = 0.7f,
                ProtectAllies = true
            }
        };

        // Act
        var (status, response) = await service.SetCombatPreferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(6, response.Version); // Incremented

        // Verify updated event
        var updatedEvent = _publishedEvents.FirstOrDefault(e => e.Topic == "combat-preferences.updated");
        Assert.NotNull(updatedEvent.Event);
        Assert.IsType<CombatPreferencesUpdatedEvent>(updatedEvent.Event);
    }

    #endregion

    #region DeleteCombatPreferences Tests

    [Fact]
    public async Task DeleteCombatPreferencesAsync_WithExisting_ShouldReturnOkAndPublishEvent()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestCombatData(characterId);

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingData);

        var service = CreateService();
        var request = new DeleteCombatPreferencesRequest { CharacterId = characterId };

        // Act
        var status = await service.DeleteCombatPreferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify delete was called
        _mockCombatStore.Verify(s => s.DeleteAsync($"combat-{characterId}", It.IsAny<CancellationToken>()), Times.Once);

        // Verify deleted event
        var deletedEvent = _publishedEvents.FirstOrDefault(e => e.Topic == "combat-preferences.deleted");
        Assert.NotNull(deletedEvent.Event);
        Assert.IsType<CombatPreferencesDeletedEvent>(deletedEvent.Event);
    }

    [Fact]
    public async Task DeleteCombatPreferencesAsync_WithNonExistent_ShouldReturnNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CombatPreferencesData?)null);

        var service = CreateService();
        var request = new DeleteCombatPreferencesRequest { CharacterId = characterId };

        // Act
        var status = await service.DeleteCombatPreferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        _mockCombatStore.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region EvolveCombatPreferences Tests

    [Fact]
    public async Task EvolveCombatPreferencesAsync_WithNonExistent_ShouldReturnNotFound()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CombatPreferencesData?)null);

        var service = CreateService();
        var request = new EvolveCombatRequest
        {
            CharacterId = characterId,
            ExperienceType = CombatExperienceType.DECISIVE_VICTORY,
            Intensity = 1.0f
        };

        // Act
        var (status, response) = await service.EvolveCombatPreferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task EvolveCombatPreferencesAsync_WithExisting_ShouldReturnResult()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestCombatData(characterId);

        _mockCombatStore
            .Setup(s => s.GetWithETagAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingData, "etag-1"));
        _mockCombatStore
            .Setup(s => s.TrySaveAsync($"combat-{characterId}", It.IsAny<CombatPreferencesData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var service = CreateService();
        var request = new EvolveCombatRequest
        {
            CharacterId = characterId,
            ExperienceType = CombatExperienceType.DEFEAT,
            Intensity = 0.5f
        };

        // Act
        var (status, response) = await service.EvolveCombatPreferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.True(response.ExperienceRecorded);
    }

    [Theory]
    [InlineData(CombatExperienceType.DECISIVE_VICTORY)]
    [InlineData(CombatExperienceType.NARROW_VICTORY)]
    [InlineData(CombatExperienceType.DEFEAT)]
    [InlineData(CombatExperienceType.NEAR_DEATH)]
    [InlineData(CombatExperienceType.ALLY_SAVED)]
    [InlineData(CombatExperienceType.ALLY_LOST)]
    [InlineData(CombatExperienceType.SUCCESSFUL_RETREAT)]
    [InlineData(CombatExperienceType.FAILED_RETREAT)]
    [InlineData(CombatExperienceType.AMBUSH_SUCCESS)]
    [InlineData(CombatExperienceType.AMBUSH_SURVIVED)]
    public async Task EvolveCombatPreferencesAsync_AllExperienceTypes_ShouldBeHandled(CombatExperienceType experienceType)
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestCombatData(characterId);

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingData);

        var service = CreateService();
        var request = new EvolveCombatRequest
        {
            CharacterId = characterId,
            ExperienceType = experienceType,
            Intensity = 0.5f
        };

        // Act
        var (status, response) = await service.EvolveCombatPreferencesAsync(request, CancellationToken.None);

        // Assert - all combat experience types should complete without error
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.ExperienceRecorded);
    }

    [Fact]
    public async Task EvolveCombatPreferencesAsync_WhenEvolutionOccurs_ShouldReturnPreviousAndNewPreferences()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestCombatData(characterId, version: 1);

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingData);

        var service = CreateService();

        // Run multiple times to get evolution
        var evolutionOccurred = false;
        for (int i = 0; i < 100 && !evolutionOccurred; i++)
        {
            _publishedEvents.Clear();
            var request = new EvolveCombatRequest
            {
                CharacterId = characterId,
                ExperienceType = CombatExperienceType.NEAR_DEATH,
                Intensity = 1.0f
            };

            var (status, response) = await service.EvolveCombatPreferencesAsync(request, CancellationToken.None);

            if (response?.PreferencesEvolved == true)
            {
                evolutionOccurred = true;

                // Assert evolution data is populated
                Assert.NotNull(response.PreviousPreferences);
                Assert.NotNull(response.NewPreferences);
                Assert.NotNull(response.NewVersion);

                // Verify evolved event was published
                var evolvedEvent = _publishedEvents.FirstOrDefault(e => e.Topic == "combat-preferences.evolved");
                Assert.NotNull(evolvedEvent.Event);
                Assert.IsType<CombatPreferencesEvolvedEvent>(evolvedEvent.Event);
            }
        }

        Assert.True(evolutionOccurred, "Expected at least one evolution in 100 iterations");
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void CharacterPersonalityPermissionRegistration_GetEndpoints_ShouldReturnAllEndpoints()
    {
        // Act
        var endpoints = CharacterPersonalityPermissionRegistration.GetEndpoints();

        // Assert - Only endpoints with x-permissions are registered (6 out of 9)
        // The evolve and batch-get endpoints have no x-permissions defined
        Assert.NotNull(endpoints);
        Assert.Equal(6, endpoints.Count);
    }

    [Fact]
    public void CharacterPersonalityPermissionRegistration_GetEndpoints_ShouldContainCorrectPaths()
    {
        // Act
        var endpoints = CharacterPersonalityPermissionRegistration.GetEndpoints();

        // Assert expected endpoints exist (only those with x-permissions in schema)
        Assert.Contains(endpoints, e => e.Path == "/character-personality/get");
        Assert.Contains(endpoints, e => e.Path == "/character-personality/set");
        Assert.Contains(endpoints, e => e.Path == "/character-personality/delete");
        Assert.Contains(endpoints, e => e.Path == "/character-personality/get-combat");
        Assert.Contains(endpoints, e => e.Path == "/character-personality/set-combat");
        Assert.Contains(endpoints, e => e.Path == "/character-personality/delete-combat");
        // Note: batch-get and evolve endpoints have no x-permissions, so not registered
    }

    [Fact]
    public void CharacterPersonalityPermissionRegistration_ServiceId_ShouldBeCorrect()
    {
        // Assert
        Assert.Equal("character-personality", CharacterPersonalityPermissionRegistration.ServiceId);
    }

    [Fact]
    public void CharacterPersonalityPermissionRegistration_ServiceVersion_ShouldNotBeEmpty()
    {
        // Assert
        Assert.NotNull(CharacterPersonalityPermissionRegistration.ServiceVersion);
        Assert.NotEmpty(CharacterPersonalityPermissionRegistration.ServiceVersion);
    }

    [Fact]
    public void CharacterPersonalityPermissionRegistration_CreateRegistrationEvent_ShouldCreateValidEvent()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        // Act
        var evt = CharacterPersonalityPermissionRegistration.CreateRegistrationEvent(instanceId, "test-app");

        // Assert
        Assert.NotNull(evt);
        Assert.Equal("character-personality", evt.ServiceName);
        Assert.Equal(instanceId, evt.ServiceId);
        Assert.Equal(6, evt.Endpoints.Count); // Only endpoints with x-permissions
        Assert.NotEmpty(evt.Version);
    }

    #endregion

    #region Response Mapping Tests

    [Fact]
    public async Task GetPersonalityAsync_ResponseMapping_ShouldMapAllFieldsCorrectly()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();
        var updatedAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();

        var testData = new PersonalityData
        {
            CharacterId = characterId,
            Traits = new Dictionary<TraitAxis, float>
            {
                { TraitAxis.OPENNESS, 0.75f },
                { TraitAxis.NEUROTICISM, -0.5f }
            },
            Version = 3,
            CreatedAtUnix = createdAt,
            UpdatedAtUnix = updatedAt
        };

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetPersonalityAsync(new GetPersonalityRequest { CharacterId = characterId }, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(3, response.Version);
        Assert.Equal(2, response.Traits.Count);

        var openness = response.Traits.First(t => t.Axis == TraitAxis.OPENNESS);
        Assert.Equal(0.75f, openness.Value);

        var neuroticism = response.Traits.First(t => t.Axis == TraitAxis.NEUROTICISM);
        Assert.Equal(-0.5f, neuroticism.Value);

        // CreatedAt and UpdatedAt should be different, so UpdatedAt should be populated
        Assert.NotNull(response.UpdatedAt);
    }

    [Fact]
    public async Task GetCombatPreferencesAsync_ResponseMapping_ShouldMapAllFieldsCorrectly()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var testData = new CombatPreferencesData
        {
            CharacterId = characterId,
            Style = CombatStyle.TACTICAL,
            PreferredRange = PreferredRange.RANGED,
            GroupRole = GroupRole.LEADER,
            RiskTolerance = 0.3f,
            RetreatThreshold = 0.6f,
            ProtectAllies = true,
            Version = 7,
            CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetCombatPreferencesAsync(new GetCombatPreferencesRequest { CharacterId = characterId }, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(7, response.Version);
        Assert.Equal(CombatStyle.TACTICAL, response.Preferences.Style);
        Assert.Equal(PreferredRange.RANGED, response.Preferences.PreferredRange);
        Assert.Equal(GroupRole.LEADER, response.Preferences.GroupRole);
        Assert.Equal(0.3f, response.Preferences.RiskTolerance);
        Assert.Equal(0.6f, response.Preferences.RetreatThreshold);
        Assert.True(response.Preferences.ProtectAllies);
        Assert.NotNull(response.UpdatedAt);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task SetPersonalityAsync_WithSingleTrait_ShouldSucceed()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersonalityData?)null);

        var service = CreateService();
        var request = new SetPersonalityRequest
        {
            CharacterId = characterId,
            Traits = new List<TraitValue>
            {
                new() { Axis = TraitAxis.LOYALTY, Value = 1.0f }
            }
        };

        // Act
        var (status, response) = await service.SetPersonalityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task RecordExperienceAsync_WithZeroIntensity_ShouldStillProcess()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var existingData = CreateTestPersonalityData(characterId);

        _mockPersonalityStore
            .Setup(s => s.GetAsync($"personality-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingData);

        var service = CreateService();
        var request = new RecordExperienceRequest
        {
            CharacterId = characterId,
            ExperienceType = ExperienceType.VICTORY,
            Intensity = 0.0f // Zero intensity should have 0% evolution chance
        };

        // Act
        var (status, response) = await service.RecordExperienceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.ExperienceRecorded);
        // With 0 intensity, evolution probability is 0, so no evolution
        Assert.False(response.PersonalityEvolved);
    }

    [Fact]
    public async Task SetCombatPreferencesAsync_WithBoundaryValues_ShouldAcceptValidRange()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        _mockCombatStore
            .Setup(s => s.GetAsync($"combat-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CombatPreferencesData?)null);

        var service = CreateService();
        var request = new SetCombatPreferencesRequest
        {
            CharacterId = characterId,
            Preferences = new CombatPreferences
            {
                Style = CombatStyle.BERSERKER,
                PreferredRange = PreferredRange.MELEE,
                GroupRole = GroupRole.SOLO,
                RiskTolerance = 1.0f, // Max
                RetreatThreshold = 0.0f, // Min - fight to death
                ProtectAllies = false
            }
        };

        // Act
        var (status, response) = await service.SetCombatPreferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1.0f, response.Preferences.RiskTolerance);
        Assert.Equal(0.0f, response.Preferences.RetreatThreshold);
    }

    #endregion
}

// Note: These are internal classes from the service that we can test due to InternalsVisibleTo
