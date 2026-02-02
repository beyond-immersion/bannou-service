using BeyondImmersion.BannouService.CharacterHistory;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.CharacterHistory.Tests;

/// <summary>
/// Unit tests for CharacterHistoryService.
/// Tests participation recording, backstory management, and event publishing.
/// Now uses shared History infrastructure helpers (DualIndexHelper, BackstoryStorageHelper).
/// </summary>
public class CharacterHistoryServiceTests
{
    private readonly Mock<ILogger<CharacterHistoryService>> _mockLogger;
    private readonly CharacterHistoryServiceConfiguration _configuration;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<ParticipationData>> _mockParticipationStore;
    private readonly Mock<IStateStore<HistoryIndexData>> _mockIndexStore;
    private readonly Mock<IStateStore<BackstoryData>> _mockBackstoryStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    private const string STATE_STORE = "character-history-statestore";

    public CharacterHistoryServiceTests()
    {
        _mockLogger = new Mock<ILogger<CharacterHistoryService>>();
        _configuration = new CharacterHistoryServiceConfiguration();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockParticipationStore = new Mock<IStateStore<ParticipationData>>();
        _mockIndexStore = new Mock<IStateStore<HistoryIndexData>>();
        _mockBackstoryStore = new Mock<IStateStore<BackstoryData>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup default factory returns
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ParticipationData>(STATE_STORE))
            .Returns(_mockParticipationStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<HistoryIndexData>(STATE_STORE))
            .Returns(_mockIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<BackstoryData>(STATE_STORE))
            .Returns(_mockBackstoryStore.Object);
    }

    private CharacterHistoryService CreateService()
    {
        return new CharacterHistoryService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object);
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void CharacterHistoryService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<CharacterHistoryService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void CharacterHistoryServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new CharacterHistoryServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    #endregion

    #region RecordParticipation Tests

    [Fact]
    public async Task RecordParticipationAsync_ValidRequest_ReturnsOKAndCreatesParticipation()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var request = new RecordParticipationRequest
        {
            CharacterId = characterId,
            EventId = eventId,
            EventName = "The Great Battle",
            EventCategory = EventCategory.WAR,
            Role = ParticipationRole.COMBATANT,
            EventDate = DateTimeOffset.UtcNow.AddDays(-30),
            Significance = 0.8f,
            Metadata = null
        };

        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        var (status, result) = await service.RecordParticipationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(characterId, result.CharacterId);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal("The Great Battle", result.EventName);
        Assert.Equal(EventCategory.WAR, result.EventCategory);
        Assert.Equal(ParticipationRole.COMBATANT, result.Role);
        Assert.NotEqual(Guid.Empty, result.ParticipationId);

        // Verify state was saved
        _mockParticipationStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("participation-")),
            It.IsAny<ParticipationData>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "character-history.participation.recorded",
            It.IsAny<CharacterParticipationRecordedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordParticipationAsync_UpdatesCharacterIndex()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var existingEventId = Guid.NewGuid();
        var existingRecordId = "existing-id";
        var existingIndex = new HistoryIndexData
        {
            EntityId = characterId.ToString(),
            RecordIds = new List<string> { existingRecordId }
        };

        // Create an existing participation record with a different EventId
        var existingParticipation = new ParticipationData
        {
            ParticipationId = Guid.NewGuid(),
            CharacterId = characterId,
            EventId = existingEventId, // Different from the new request
            EventName = "Previous Event",
            EventCategory = EventCategory.WAR,
            Role = ParticipationRole.COMBATANT,
            EventDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
            Significance = 0.7f
        };

        var request = new RecordParticipationRequest
        {
            CharacterId = characterId,
            EventId = Guid.NewGuid(), // New event, different from existingEventId
            EventName = "New Event",
            EventCategory = EventCategory.CULTURAL,
            Role = ParticipationRole.WITNESS,
            EventDate = DateTimeOffset.UtcNow,
            Significance = 0.5f
        };

        // Mock index retrieval for duplicate check
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);
        _mockIndexStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("participation-event-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Mock GetBulkAsync to return existing participation records for duplicate check
        _mockParticipationStore.Setup(s => s.GetBulkAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ParticipationData>
            {
                { $"participation-{existingRecordId}", existingParticipation }
            });

        // Act
        await service.RecordParticipationAsync(request, CancellationToken.None);

        // Assert - Index should now have 2 record IDs
        _mockIndexStore.Verify(s => s.SaveAsync(
            $"participation-index-{characterId}",
            It.Is<HistoryIndexData>(i => i.RecordIds.Count == 2),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetParticipation Tests

    [Fact]
    public async Task GetParticipationAsync_NoParticipations_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var request = new GetParticipationRequest
        {
            CharacterId = characterId,
            Page = 1,
            PageSize = 20
        };

        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        var (status, result) = await service.GetParticipationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Empty(result.Participations);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.False(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public async Task GetParticipationAsync_WithFilter_FiltersResults()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var warParticipationId = Guid.NewGuid();
        var culturalParticipationId = Guid.NewGuid();

        var index = new HistoryIndexData
        {
            EntityId = characterId.ToString(),
            RecordIds = new List<string> { warParticipationId.ToString(), culturalParticipationId.ToString() }
        };

        var warParticipation = new ParticipationData
        {
            ParticipationId = warParticipationId,
            CharacterId = characterId,
            EventId = Guid.NewGuid(),
            EventName = "War Event",
            EventCategory = EventCategory.WAR,
            Role = ParticipationRole.COMBATANT,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Significance = 0.8f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var culturalParticipation = new ParticipationData
        {
            ParticipationId = culturalParticipationId,
            CharacterId = characterId,
            EventId = Guid.NewGuid(),
            EventName = "Cultural Event",
            EventCategory = EventCategory.CULTURAL,
            Role = ParticipationRole.WITNESS,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Significance = 0.5f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);
        _mockParticipationStore.Setup(s => s.GetAsync($"participation-{warParticipationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(warParticipation);
        _mockParticipationStore.Setup(s => s.GetAsync($"participation-{culturalParticipationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(culturalParticipation);

        // DualIndexHelper uses GetBulkAsync, so we need to mock that too
        _mockParticipationStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> keys, CancellationToken ct) =>
            {
                var results = new Dictionary<string, ParticipationData>();
                foreach (var key in keys)
                {
                    if (key == $"participation-{warParticipationId}")
                        results[key] = warParticipation;
                    else if (key == $"participation-{culturalParticipationId}")
                        results[key] = culturalParticipation;
                }
                return results;
            });

        var request = new GetParticipationRequest
        {
            CharacterId = characterId,
            EventCategory = EventCategory.WAR, // Only WAR events
            Page = 1,
            PageSize = 20
        };

        // Act
        var (status, result) = await service.GetParticipationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Participations);
        Assert.Equal("War Event", result.Participations.First().EventName);
    }

    #endregion

    #region Backstory Tests

    [Fact]
    public async Task GetBackstoryAsync_NoBackstory_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var request = new GetBackstoryRequest { CharacterId = characterId };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);

        // Act
        var (status, result) = await service.GetBackstoryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetBackstoryAsync_NewBackstory_CreatesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement
                {
                    ElementType = BackstoryElementType.ORIGIN,
                    Key = "homeland",
                    Value = "Born in the northern mountains",
                    Strength = 0.9f
                }
            },
            ReplaceExisting = false
        };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);

        // Act
        var (status, result) = await service.SetBackstoryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Elements);
        Assert.Equal("homeland", result.Elements.First().Key);

        // Verify backstory.created event was published (new backstory)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "character-history.backstory.created",
            It.IsAny<CharacterBackstoryCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetBackstoryAsync_ExistingBackstory_UpdatesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var existingBackstory = new BackstoryData
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElementData>
            {
                new BackstoryElementData
                {
                    ElementType = BackstoryElementType.ORIGIN,
                    Key = "homeland",
                    Value = "Old value",
                    Strength = 0.5f
                }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBackstory);

        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement
                {
                    ElementType = BackstoryElementType.ORIGIN,
                    Key = "homeland",
                    Value = "Updated value",
                    Strength = 0.9f
                }
            },
            ReplaceExisting = false
        };

        // Act
        var (status, result) = await service.SetBackstoryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify backstory.updated event was published (existing backstory)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "character-history.backstory.updated",
            It.IsAny<CharacterBackstoryUpdatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteParticipationAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var participationId = Guid.NewGuid();

        var request = new DeleteParticipationRequest { ParticipationId = participationId };

        _mockParticipationStore.Setup(s => s.GetAsync($"participation-{participationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipationData?)null);

        // Act
        var status = await service.DeleteParticipationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeleteBackstoryAsync_Existing_DeletesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var existingBackstory = new BackstoryData
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElementData>(),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBackstory);

        var request = new DeleteBackstoryRequest { CharacterId = characterId };

        // Act
        var status = await service.DeleteBackstoryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        _mockBackstoryStore.Verify(s => s.DeleteAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()), Times.Once);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "character-history.backstory.deleted",
            It.IsAny<CharacterBackstoryDeletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteBackstoryAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);

        var request = new DeleteBackstoryRequest { CharacterId = characterId };

        // Act
        var status = await service.DeleteBackstoryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region Resource Reference Tracking Tests

    [Fact]
    public async Task RecordParticipationAsync_PublishesReferenceRegisteredEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        SetupDefaultMessageBus();
        SetupEmptyCharacterIndex(characterId);
        SetupEmptyEventIndex(eventId);
        SetupParticipationSave();

        ResourceReferenceRegisteredEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.Is<string>(t => t == "resource.reference.registered"),
                It.IsAny<ResourceReferenceRegisteredEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ResourceReferenceRegisteredEvent, CancellationToken>((_, evt, _) =>
            {
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new RecordParticipationRequest
        {
            CharacterId = characterId,
            EventId = eventId,
            EventName = "Test Event",
            EventCategory = EventCategory.WAR,
            Role = ParticipationRole.WITNESS,
            EventDate = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.RecordParticipationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(capturedEvent);
        Assert.Equal("character", capturedEvent.ResourceType);
        Assert.Equal("character-history", capturedEvent.SourceType);
        Assert.Equal(characterId, capturedEvent.ResourceId);
        Assert.Equal(response.ParticipationId.ToString(), capturedEvent.SourceId);
    }

    [Fact]
    public async Task SetBackstoryAsync_NewBackstory_PublishesReferenceRegisteredEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        SetupDefaultMessageBus();
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);
        _mockBackstoryStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BackstoryData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        ResourceReferenceRegisteredEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.Is<string>(t => t == "resource.reference.registered"),
                It.IsAny<ResourceReferenceRegisteredEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ResourceReferenceRegisteredEvent, CancellationToken>((_, evt, _) =>
            {
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement { ElementType = BackstoryElementType.ORIGIN, Key = "homeland", Value = "Test origin", Strength = 0.8f }
            }
        };

        // Act
        var (status, response) = await service.SetBackstoryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedEvent);
        Assert.Equal("character", capturedEvent.ResourceType);
        Assert.Equal("character-history", capturedEvent.SourceType);
        Assert.Equal(characterId, capturedEvent.ResourceId);
        Assert.Equal($"backstory-{characterId}", capturedEvent.SourceId);
    }

    [Fact]
    public async Task SetBackstoryAsync_ExistingBackstory_DoesNotPublishReferenceEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        SetupDefaultMessageBus();
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackstoryData
            {
                CharacterId = characterId,
                Elements = new List<BackstoryElementData>(),
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
                UpdatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
            });
        _mockBackstoryStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BackstoryData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var referenceEventPublished = false;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.Is<string>(t => t == "resource.reference.registered"),
                It.IsAny<ResourceReferenceRegisteredEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ResourceReferenceRegisteredEvent, CancellationToken>((_, _, _) =>
            {
                referenceEventPublished = true;
            })
            .ReturnsAsync(true);

        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement { ElementType = BackstoryElementType.ORIGIN, Key = "homeland", Value = "Updated origin", Strength = 0.8f }
            }
        };

        // Act
        var (status, _) = await service.SetBackstoryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.False(referenceEventPublished, "Should not publish reference event for existing backstory update");
    }

    [Fact]
    public async Task DeleteBackstoryAsync_PublishesReferenceUnregisteredEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        SetupDefaultMessageBus();
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackstoryData
            {
                CharacterId = characterId,
                Elements = new List<BackstoryElementData>(),
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        _mockBackstoryStore.Setup(s => s.DeleteAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ResourceReferenceUnregisteredEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.Is<string>(t => t == "resource.reference.unregistered"),
                It.IsAny<ResourceReferenceUnregisteredEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ResourceReferenceUnregisteredEvent, CancellationToken>((_, evt, _) =>
            {
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new DeleteBackstoryRequest { CharacterId = characterId };

        // Act
        var status = await service.DeleteBackstoryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedEvent);
        Assert.Equal("character", capturedEvent.ResourceType);
        Assert.Equal("character-history", capturedEvent.SourceType);
        Assert.Equal(characterId, capturedEvent.ResourceId);
        Assert.Equal($"backstory-{characterId}", capturedEvent.SourceId);
    }

    #endregion

    #region Helper Methods

    private void SetupDefaultMessageBus()
    {
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private void SetupEmptyCharacterIndex(Guid characterId)
    {
        _mockIndexStore.Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);
        _mockIndexStore.Setup(s => s.GetWithETagAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((HistoryIndexData?)null, (string?)null));
        _mockIndexStore.Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<HistoryIndexData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
    }

    private void SetupEmptyEventIndex(Guid eventId)
    {
        _mockIndexStore.Setup(s => s.GetAsync($"event-idx-{eventId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);
        _mockIndexStore.Setup(s => s.GetWithETagAsync($"event-idx-{eventId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((HistoryIndexData?)null, (string?)null));
    }

    private void SetupParticipationSave()
    {
        _mockParticipationStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ParticipationData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
    }

    #endregion
}
