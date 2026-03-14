using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.CharacterHistory;
using BeyondImmersion.BannouService.CharacterHistory.Caching;
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
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<ParticipationData>> _mockParticipationStore;
    private readonly Mock<IStateStore<HistoryIndexData>> _mockIndexStore;
    private readonly Mock<IStateStore<BackstoryData>> _mockBackstoryStore;
    private readonly Mock<IJsonQueryableStateStore<ParticipationData>> _mockJsonQueryableStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IBackstoryCache> _mockBackstoryCache;

    private const string STATE_STORE = "character-history-statestore";

    public CharacterHistoryServiceTests()
    {
        _mockLogger = new Mock<ILogger<CharacterHistoryService>>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockParticipationStore = new Mock<IStateStore<ParticipationData>>();
        _mockIndexStore = new Mock<IStateStore<HistoryIndexData>>();
        _mockBackstoryStore = new Mock<IStateStore<BackstoryData>>();
        _mockJsonQueryableStore = new Mock<IJsonQueryableStateStore<ParticipationData>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockBackstoryCache = new Mock<IBackstoryCache>();

        // Default: lock provider succeeds
        var successLock = new Mock<ILockResponse>();
        successLock.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

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
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<ParticipationData>(STATE_STORE))
            .Returns(_mockJsonQueryableStore.Object);
    }

    private CharacterHistoryService CreateService(CharacterHistoryServiceConfiguration? configuration = null)
    {
        return new CharacterHistoryService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _mockEventConsumer.Object,
            configuration ?? new CharacterHistoryServiceConfiguration(),
            _mockLockProvider.Object,
            _mockResourceClient.Object,
            _mockTelemetryProvider.Object,
            _mockBackstoryCache.Object);
    }

    #region Constructor Validation

    #endregion

    #region Configuration Tests

    [Fact]
    public void CharacterHistoryServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new CharacterHistoryServiceConfiguration();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(10, config.BackstoryCacheTtlMinutes);
        Assert.Equal(100, config.MaxBackstoryElements);
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
            EventCategory = EventCategory.War,
            Role = ParticipationRole.Combatant,
            EventDate = DateTimeOffset.UtcNow.AddDays(-30),
            Significance = 0.8f,
            Metadata = null
        };

        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        var (status, result) = await service.RecordParticipationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(characterId, result.CharacterId);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal("The Great Battle", result.EventName);
        Assert.Equal(EventCategory.War, result.EventCategory);
        Assert.Equal(ParticipationRole.Combatant, result.Role);
        Assert.NotEqual(Guid.Empty, result.ParticipationId);

        // Verify state was saved
        _mockParticipationStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("participation-")),
            It.IsAny<ParticipationData>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert captured event
        Assert.Equal("character-history.participation.recorded", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<CharacterParticipationRecordedEvent>(capturedEvent);
        Assert.Equal(characterId, typedEvent.CharacterId);
        Assert.Equal(eventId, typedEvent.HistoricalEventId);
        Assert.Equal(result.ParticipationId, typedEvent.ParticipationId);
        Assert.Equal(ParticipationRole.Combatant, typedEvent.Role);
        Assert.Equal("The Great Battle", typedEvent.HistoricalEventName);
        Assert.Equal(EventCategory.War, typedEvent.EventCategory);
        Assert.Equal(0.8f, typedEvent.Significance);
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
            EventCategory = EventCategory.War,
            Role = ParticipationRole.Combatant,
            EventDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
            Significance = 0.7f
        };

        var request = new RecordParticipationRequest
        {
            CharacterId = characterId,
            EventId = Guid.NewGuid(), // New event, different from existingEventId
            EventName = "New Event",
            EventCategory = EventCategory.Cultural,
            Role = ParticipationRole.Witness,
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
        await service.RecordParticipationAsync(request, TestContext.Current.CancellationToken);

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

        SetupJsonQueryPagedAsync(new List<ParticipationData>(), 0);

        // Act
        var (status, result) = await service.GetParticipationAsync(request, TestContext.Current.CancellationToken);

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
    public async Task GetParticipationAsync_WithFilter_ReturnsFilteredResults()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var warParticipationId = Guid.NewGuid();

        var warParticipation = new ParticipationData
        {
            ParticipationId = warParticipationId,
            CharacterId = characterId,
            EventId = Guid.NewGuid(),
            EventName = "War Event",
            EventCategory = EventCategory.War,
            Role = ParticipationRole.Combatant,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Significance = 0.8f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Server-side query returns only matching results (filtered at database level)
        SetupJsonQueryPagedAsync(new List<ParticipationData> { warParticipation }, 1);

        var request = new GetParticipationRequest
        {
            CharacterId = characterId,
            EventCategory = EventCategory.War,
            Page = 1,
            PageSize = 20
        };

        // Act
        var (status, result) = await service.GetParticipationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Participations);
        Assert.Equal("War Event", result.Participations.First().EventName);

        // Verify query conditions were passed (filters pushed to database)
        _mockJsonQueryableStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>?>(c =>
                c != null && c.Any(q => q.Path == "$.CharacterId") &&
                c.Any(q => q.Path == "$.EventCategory")),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetParticipationAsync_PaginatesCorrectly()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var participation = new ParticipationData
        {
            ParticipationId = Guid.NewGuid(),
            CharacterId = characterId,
            EventId = Guid.NewGuid(),
            EventName = "Page 2 Event",
            EventCategory = EventCategory.War,
            Role = ParticipationRole.Combatant,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Significance = 0.8f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Page 2 of 3 total items, pageSize=1
        SetupJsonQueryPagedAsync(new List<ParticipationData> { participation }, totalCount: 3, offset: 1, limit: 1);

        var request = new GetParticipationRequest
        {
            CharacterId = characterId,
            Page = 2,
            PageSize = 1
        };

        // Act
        var (status, result) = await service.GetParticipationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Participations);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.True(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    #endregion

    #region GetEventParticipants Tests

    [Fact]
    public async Task GetEventParticipantsAsync_NoParticipants_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var eventId = Guid.NewGuid();

        var request = new GetEventParticipantsRequest
        {
            EventId = eventId,
            Page = 1,
            PageSize = 20
        };

        SetupJsonQueryPagedAsync(new List<ParticipationData>(), 0);

        // Act
        var (status, result) = await service.GetEventParticipantsAsync(request, TestContext.Current.CancellationToken);

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
    public async Task GetEventParticipantsAsync_WithRoleFilter_ReturnsFilteredResults()
    {
        // Arrange
        var service = CreateService();
        var eventId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        var leaderParticipation = new ParticipationData
        {
            ParticipationId = Guid.NewGuid(),
            CharacterId = characterId,
            EventId = eventId,
            EventName = "Battle",
            EventCategory = EventCategory.War,
            Role = ParticipationRole.Leader,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Significance = 0.9f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Server-side query returns only LEADER role results
        SetupJsonQueryPagedAsync(new List<ParticipationData> { leaderParticipation }, 1);

        var request = new GetEventParticipantsRequest
        {
            EventId = eventId,
            Role = ParticipationRole.Leader,
            Page = 1,
            PageSize = 20
        };

        // Act
        var (status, result) = await service.GetEventParticipantsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Participations);
        Assert.Equal(ParticipationRole.Leader, result.Participations.First().Role);

        // Verify query conditions include EventId and Role filters
        _mockJsonQueryableStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>?>(c =>
                c != null && c.Any(q => q.Path == "$.EventId") &&
                c.Any(q => q.Path == "$.Role")),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
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
        var (status, result) = await service.GetBackstoryAsync(request, TestContext.Current.CancellationToken);

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
                    ElementType = BackstoryElementType.Origin,
                    Key = "homeland",
                    Value = "Born in the northern mountains",
                    Strength = 0.9f
                }
            },
            ReplaceExisting = false
        };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        var (status, result) = await service.SetBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Elements);
        Assert.Equal("homeland", result.Elements.First().Key);

        // Assert captured event
        Assert.Equal("character-history.backstory.created", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<CharacterBackstoryCreatedEvent>(capturedEvent);
        Assert.Equal(characterId, typedEvent.CharacterId);
        Assert.Equal(1, typedEvent.ElementCount);
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
                    ElementType = BackstoryElementType.Origin,
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

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement
                {
                    ElementType = BackstoryElementType.Origin,
                    Key = "homeland",
                    Value = "Updated value",
                    Strength = 0.9f
                }
            },
            ReplaceExisting = false
        };

        // Act
        var (status, result) = await service.SetBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Assert captured event
        Assert.Equal("character-history.backstory.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<CharacterBackstoryUpdatedEvent>(capturedEvent);
        Assert.Equal(characterId, typedEvent.CharacterId);
        Assert.False(typedEvent.ReplaceExisting);
    }

    #endregion

    #region Backstory Element Limit Tests

    [Fact]
    public async Task SetBackstoryAsync_ReplaceExceedsLimit_ReturnsBadRequest()
    {
        // Arrange
        var config = new CharacterHistoryServiceConfiguration { MaxBackstoryElements = 3 };
        var service = CreateService(config);
        var characterId = Guid.NewGuid();

        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement { ElementType = BackstoryElementType.Origin, Key = "a", Value = "v", Strength = 0.5f },
                new BackstoryElement { ElementType = BackstoryElementType.Trauma, Key = "b", Value = "v", Strength = 0.5f },
                new BackstoryElement { ElementType = BackstoryElementType.Goal, Key = "c", Value = "v", Strength = 0.5f },
                new BackstoryElement { ElementType = BackstoryElementType.Fear, Key = "d", Value = "v", Strength = 0.5f }
            },
            ReplaceExisting = true
        };

        // Act
        var (status, result) = await service.SetBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);

        // Verify no state was saved
        _mockBackstoryStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<BackstoryData>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetBackstoryAsync_MergeExceedsLimit_ReturnsBadRequest()
    {
        // Arrange
        var config = new CharacterHistoryServiceConfiguration { MaxBackstoryElements = 2 };
        var service = CreateService(config);
        var characterId = Guid.NewGuid();

        var existingBackstory = new BackstoryData
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElementData>
            {
                new BackstoryElementData { ElementType = BackstoryElementType.Origin, Key = "homeland", Value = "north", Strength = 0.9f },
                new BackstoryElementData { ElementType = BackstoryElementType.Trauma, Key = "war", Value = "siege", Strength = 0.7f }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBackstory);

        // Request adds a new element (GOAL doesn't exist yet), which would push count to 3
        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement { ElementType = BackstoryElementType.Goal, Key = "revenge", Value = "v", Strength = 0.8f }
            },
            ReplaceExisting = false
        };

        // Act
        var (status, result) = await service.SetBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetBackstoryAsync_MergeWithUpdatesOnly_AllowsAtLimit()
    {
        // Arrange: 2 existing elements at limit of 2, merging updates to same type+key pairs
        var config = new CharacterHistoryServiceConfiguration { MaxBackstoryElements = 2 };
        var service = CreateService(config);
        var characterId = Guid.NewGuid();

        var existingBackstory = new BackstoryData
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElementData>
            {
                new BackstoryElementData { ElementType = BackstoryElementType.Origin, Key = "homeland", Value = "old", Strength = 0.5f },
                new BackstoryElementData { ElementType = BackstoryElementType.Trauma, Key = "war", Value = "old", Strength = 0.3f }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBackstory);

        // Request updates existing elements (same type+key), no new additions
        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement { ElementType = BackstoryElementType.Origin, Key = "homeland", Value = "updated", Strength = 0.9f },
                new BackstoryElement { ElementType = BackstoryElementType.Trauma, Key = "war", Value = "updated", Strength = 0.8f }
            },
            ReplaceExisting = false
        };

        // Act
        var (status, result) = await service.SetBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert - should succeed because no new elements are added, just updates
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddBackstoryElementAsync_ExceedsLimit_ReturnsBadRequest()
    {
        // Arrange
        var config = new CharacterHistoryServiceConfiguration { MaxBackstoryElements = 2 };
        var service = CreateService(config);
        var characterId = Guid.NewGuid();

        var existingBackstory = new BackstoryData
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElementData>
            {
                new BackstoryElementData { ElementType = BackstoryElementType.Origin, Key = "homeland", Value = "north", Strength = 0.9f },
                new BackstoryElementData { ElementType = BackstoryElementType.Trauma, Key = "war", Value = "siege", Strength = 0.7f }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBackstory);

        // Adding a new element type that doesn't exist yet
        var request = new AddBackstoryElementRequest
        {
            CharacterId = characterId,
            Element = new BackstoryElement { ElementType = BackstoryElementType.Goal, Key = "revenge", Value = "avenge", Strength = 0.8f }
        };

        // Act
        var (status, result) = await service.AddBackstoryElementAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddBackstoryElementAsync_UpdateExistingAtLimit_Succeeds()
    {
        // Arrange: at limit of 2, but updating existing element (same type+key)
        var config = new CharacterHistoryServiceConfiguration { MaxBackstoryElements = 2 };
        var service = CreateService(config);
        var characterId = Guid.NewGuid();

        var existingBackstory = new BackstoryData
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElementData>
            {
                new BackstoryElementData { ElementType = BackstoryElementType.Origin, Key = "homeland", Value = "north", Strength = 0.5f },
                new BackstoryElementData { ElementType = BackstoryElementType.Trauma, Key = "war", Value = "siege", Strength = 0.7f }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBackstory);

        // Updating existing ORIGIN/homeland element (upsert - same type+key)
        var request = new AddBackstoryElementRequest
        {
            CharacterId = characterId,
            Element = new BackstoryElement { ElementType = BackstoryElementType.Origin, Key = "homeland", Value = "updated value", Strength = 0.95f }
        };

        // Act
        var (status, result) = await service.AddBackstoryElementAsync(request, TestContext.Current.CancellationToken);

        // Assert - should succeed because this is an update, not a new element
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
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
        var status = await service.DeleteParticipationAsync(request, TestContext.Current.CancellationToken);

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

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new DeleteBackstoryRequest { CharacterId = characterId };

        // Act
        var status = await service.DeleteBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        _mockBackstoryStore.Verify(s => s.DeleteAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()), Times.Once);

        // Assert captured event
        Assert.Equal("character-history.backstory.deleted", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<CharacterBackstoryDeletedEvent>(capturedEvent);
        Assert.Equal(characterId, typedEvent.CharacterId);
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
        var status = await service.DeleteBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region Resource Reference Registration Tests

    [Fact]
    public async Task RecordParticipationAsync_RegistersCharacterReference()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        SetupDefaultMessageBus();
        SetupEmptyCharacterIndex(characterId);
        SetupEmptyEventIndex(eventId);
        SetupParticipationSave();

        RegisterReferenceRequest? capturedRequest = null;
        _mockResourceClient
            .Setup(m => m.RegisterReferenceAsync(
                It.IsAny<RegisterReferenceRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<RegisterReferenceRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new RegisterReferenceResponse());

        var request = new RecordParticipationRequest
        {
            CharacterId = characterId,
            EventId = eventId,
            EventName = "Test Event",
            EventCategory = EventCategory.War,
            Role = ParticipationRole.Witness,
            EventDate = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.RecordParticipationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(capturedRequest);
        Assert.Equal("character", capturedRequest.ResourceType);
        Assert.Equal("character-history", capturedRequest.SourceType);
        Assert.Equal(characterId, capturedRequest.ResourceId);
        Assert.Equal(response.ParticipationId.ToString(), capturedRequest.SourceId);
    }

    [Fact]
    public async Task SetBackstoryAsync_NewBackstory_RegistersCharacterReference()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        SetupDefaultMessageBus();
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);
        _mockBackstoryStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BackstoryData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        RegisterReferenceRequest? capturedRequest = null;
        _mockResourceClient
            .Setup(m => m.RegisterReferenceAsync(
                It.IsAny<RegisterReferenceRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<RegisterReferenceRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new RegisterReferenceResponse());

        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement { ElementType = BackstoryElementType.Origin, Key = "homeland", Value = "Test origin", Strength = 0.8f }
            }
        };

        // Act
        var (status, response) = await service.SetBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedRequest);
        Assert.Equal("character", capturedRequest.ResourceType);
        Assert.Equal("character-history", capturedRequest.SourceType);
        Assert.Equal(characterId, capturedRequest.ResourceId);
        Assert.Equal($"backstory-{characterId}", capturedRequest.SourceId);
    }

    [Fact]
    public async Task SetBackstoryAsync_ExistingBackstory_DoesNotRegisterReference()
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

        var request = new SetBackstoryRequest
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElement>
            {
                new BackstoryElement { ElementType = BackstoryElementType.Origin, Key = "homeland", Value = "Updated origin", Strength = 0.8f }
            }
        };

        // Act
        var (status, _) = await service.SetBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockResourceClient.Verify(
            m => m.RegisterReferenceAsync(It.IsAny<RegisterReferenceRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Should not call RegisterReferenceAsync for existing backstory update");
    }

    [Fact]
    public async Task DeleteBackstoryAsync_UnregistersCharacterReference()
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

        UnregisterReferenceRequest? capturedRequest = null;
        _mockResourceClient
            .Setup(m => m.UnregisterReferenceAsync(
                It.IsAny<UnregisterReferenceRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<UnregisterReferenceRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new UnregisterReferenceResponse());

        var request = new DeleteBackstoryRequest { CharacterId = characterId };

        // Act
        var status = await service.DeleteBackstoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedRequest);
        Assert.Equal("character", capturedRequest.ResourceType);
        Assert.Equal("character-history", capturedRequest.SourceType);
        Assert.Equal(characterId, capturedRequest.ResourceId);
        Assert.Equal($"backstory-{characterId}", capturedRequest.SourceId);
    }

    #endregion

    #region Helper Methods

    private void SetupJsonQueryPagedAsync(
        List<ParticipationData> items,
        long totalCount,
        int offset = 0,
        int limit = 20)
    {
        var queryResults = items.Select(m =>
            new JsonQueryResult<ParticipationData>(
                $"participation-{m.ParticipationId}",
                m))
            .ToList();

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ParticipationData>(
                queryResults,
                totalCount,
                offset,
                limit));
    }

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
        _mockIndexStore.Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<HistoryIndexData>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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

    #region SummarizeHistory Tests

    [Fact]
    public async Task SummarizeHistoryAsync_WithData_ReturnsKeyPointsAndLifeEvents()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var backstoryData = new BackstoryData
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElementData>
            {
                new BackstoryElementData
                {
                    ElementType = BackstoryElementType.Origin,
                    Key = "homeland",
                    Value = "Northlands",
                    Strength = 0.9f
                },
                new BackstoryElementData
                {
                    ElementType = BackstoryElementType.Training,
                    Key = "trained_by",
                    Value = "Knights Guild",
                    Strength = 0.7f
                }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(backstoryData);

        // DualIndexHelper uses "participation-index-{key}" for primary index lookups
        var participationId = Guid.NewGuid();
        var indexData = new HistoryIndexData
        {
            EntityId = characterId.ToString(),
            RecordIds = new List<string> { participationId.ToString() }
        };
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexData);
        _mockParticipationStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ParticipationData>
            {
                { $"participation-{participationId}", new ParticipationData
                    {
                        ParticipationId = participationId,
                        CharacterId = characterId,
                        EventId = Guid.NewGuid(),
                        EventName = "Battle of Stormgate",
                        EventCategory = EventCategory.War,
                        Role = ParticipationRole.Hero,
                        EventDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
                        Significance = 0.95f,
                        CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }
                }
            });

        var request = new SummarizeHistoryRequest
        {
            CharacterId = characterId,
            MaxBackstoryPoints = 5,
            MaxLifeEvents = 10
        };

        // Act
        var (status, response) = await service.SummarizeHistoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEmpty(response.KeyBackstoryPoints);
        Assert.NotEmpty(response.MajorLifeEvents);
        Assert.Equal(2, response.KeyBackstoryPoints.Count);
        Assert.Single(response.MajorLifeEvents);
    }

    [Fact]
    public async Task SummarizeHistoryAsync_WithNoData_ReturnsEmptyLists()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);

        // DualIndexHelper uses "participation-index-{key}" for primary index lookups
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        var request = new SummarizeHistoryRequest
        {
            CharacterId = characterId,
            MaxBackstoryPoints = 5,
            MaxLifeEvents = 10
        };

        // Act
        var (status, response) = await service.SummarizeHistoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.KeyBackstoryPoints);
        Assert.Empty(response.MajorLifeEvents);
    }

    #endregion

    #region DeleteAllHistory Tests

    [Fact]
    public async Task DeleteAllHistoryAsync_WithData_DeletesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var participationId = Guid.NewGuid();
        var indexData = new HistoryIndexData
        {
            EntityId = characterId.ToString(),
            RecordIds = new List<string> { participationId.ToString() }
        };

        // DualIndexHelper uses "participation-index-{key}" for primary index lookups
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexData);
        _mockIndexStore.Setup(s => s.GetWithETagAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((indexData, "etag-1"));
        _mockParticipationStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ParticipationData>
            {
                { $"participation-{participationId}", new ParticipationData
                    {
                        ParticipationId = participationId,
                        CharacterId = characterId,
                        EventId = Guid.NewGuid(),
                        EventName = "Battle",
                        EventCategory = EventCategory.War,
                        Role = ParticipationRole.Hero,
                        EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        Significance = 0.9f,
                        CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }
                }
            });
        // Secondary index bulk lookup for RemoveAllByPrimaryKeyAsync
        _mockIndexStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, HistoryIndexData>());
        // Bulk delete for records and indices
        _mockParticipationStore.Setup(s => s.DeleteBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackstoryData
            {
                CharacterId = characterId,
                Elements = new List<BackstoryElementData>(),
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new DeleteAllHistoryRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.DeleteAllHistoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ParticipationsDeleted);
        Assert.True(response.BackstoryDeleted);

        // Assert captured event
        Assert.Equal("character-history.deleted", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<CharacterHistoryDeletedEvent>(capturedEvent);
        Assert.Equal(characterId, typedEvent.CharacterId);
        Assert.Equal(1, typedEvent.ParticipationsDeleted);
        Assert.True(typedEvent.BackstoryDeleted);
    }

    [Fact]
    public async Task DeleteAllHistoryAsync_WithNoData_ReturnsZeroCounts()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // DualIndexHelper uses "participation-index-{key}" for primary index lookups
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);
        _mockIndexStore.Setup(s => s.GetWithETagAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((HistoryIndexData?)null, (string?)null));
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);

        SetupDefaultMessageBus();

        var request = new DeleteAllHistoryRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.DeleteAllHistoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.ParticipationsDeleted);
        Assert.False(response.BackstoryDeleted);
    }

    #endregion

    #region GetCompressData Tests

    [Fact]
    public async Task GetCompressDataAsync_WithBothParticipationsAndBackstory_ReturnsComplete()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var participationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var participation = new ParticipationData
        {
            ParticipationId = participationId,
            CharacterId = characterId,
            EventId = eventId,
            EventName = "The Great Battle",
            EventCategory = EventCategory.War,
            Role = ParticipationRole.Combatant,
            EventDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
            Significance = 0.8f,
            CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()
        };

        var backstory = new BackstoryData
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElementData>
            {
                new BackstoryElementData
                {
                    ElementType = BackstoryElementType.Origin,
                    Key = "homeland",
                    Value = "Northern mountains",
                    Strength = 0.9f
                }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var indexData = new HistoryIndexData
        {
            EntityId = characterId.ToString(),
            RecordIds = new List<string> { participationId.ToString() }
        };

        // DualIndexHelper uses "participation-index-{key}" for primary index lookups
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexData);
        _mockParticipationStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ParticipationData>
            {
                { $"participation-{participationId}", participation }
            });
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(backstory);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.True(response.HasParticipations);
        Assert.NotNull(response.Participations);
        Assert.Single(response.Participations);
        Assert.True(response.HasBackstory);
        Assert.NotNull(response.Backstory);
    }

    [Fact]
    public async Task GetCompressDataAsync_WithParticipationsOnly_ReturnsPartial()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var participationId = Guid.NewGuid();

        var participation = new ParticipationData
        {
            ParticipationId = participationId,
            CharacterId = characterId,
            EventId = Guid.NewGuid(),
            EventName = "Event",
            EventCategory = EventCategory.Cultural,
            Role = ParticipationRole.Witness,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Significance = 0.5f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var indexData = new HistoryIndexData
        {
            EntityId = characterId.ToString(),
            RecordIds = new List<string> { participationId.ToString() }
        };

        // DualIndexHelper uses "participation-index-{key}" for primary index lookups
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexData);
        _mockParticipationStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ParticipationData>
            {
                { $"participation-{participationId}", participation }
            });
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.HasParticipations);
        Assert.NotNull(response.Participations);
        Assert.Single(response.Participations);
        Assert.False(response.HasBackstory);
        Assert.Null(response.Backstory);
    }

    [Fact]
    public async Task GetCompressDataAsync_WithBackstoryOnly_ReturnsPartial()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var backstory = new BackstoryData
        {
            CharacterId = characterId,
            Elements = new List<BackstoryElementData>
            {
                new BackstoryElementData
                {
                    ElementType = BackstoryElementType.Trauma,
                    Key = "childhood",
                    Value = "Lost family early",
                    Strength = 0.7f
                }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // DualIndexHelper uses "participation-index-{key}" for primary index lookups
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(backstory);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.HasParticipations);
        Assert.NotNull(response.Participations);
        Assert.Empty(response.Participations);
        Assert.True(response.HasBackstory);
        Assert.NotNull(response.Backstory);
    }

    [Fact]
    public async Task GetCompressDataAsync_WithNeither_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // DualIndexHelper uses "participation-index-{key}" for primary index lookups
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCompressDataAsync_WhenStateStoreThrows_ReturnsInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // DualIndexHelper uses "participation-index-{key}" for primary index lookups
        _mockIndexStore.Setup(s => s.GetAsync($"participation-index-{characterId}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<Exception>(() => service.GetCompressDataAsync(request, TestContext.Current.CancellationToken));
    }

    #endregion

    #region RestoreFromArchive Tests

    [Fact]
    public async Task RestoreFromArchiveAsync_WithValidData_RestoresBoth()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var participationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var archiveData = new CharacterHistoryArchive
        {
            // ResourceArchiveBase fields
            ResourceId = characterId,
            ResourceType = "character-history",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            // Service-specific fields
            CharacterId = characterId,
            HasParticipations = true,
            Participations = new List<HistoricalParticipation>
            {
                new HistoricalParticipation
                {
                    ParticipationId = participationId,
                    EventId = eventId,
                    EventName = "Battle of Test",
                    EventCategory = EventCategory.War,
                    Role = ParticipationRole.Combatant,
                    EventDate = DateTimeOffset.UtcNow.AddDays(-30),
                    Significance = 0.8f,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
                }
            },
            HasBackstory = true,
            Backstory = new BackstoryResponse
            {
                Elements = new List<BackstoryElement>
                {
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.Origin,
                        Key = "homeland",
                        Value = "Test Land",
                        Strength = 0.9f
                    }
                },
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
            }
        };

        var compressedData = CompressArchiveData(archiveData);

        SetupDefaultMessageBus();
        SetupEmptyCharacterIndex(characterId);
        SetupEmptyEventIndex(eventId);
        SetupParticipationSave();
        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);
        _mockBackstoryStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BackstoryData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = compressedData
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ParticipationsRestored);
        Assert.True(response.BackstoryRestored);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_WithParticipationsOnly_RestoresOnlyParticipations()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var participationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var archiveData = new CharacterHistoryArchive
        {
            // ResourceArchiveBase fields
            ResourceId = characterId,
            ResourceType = "character-history",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            // Service-specific fields
            CharacterId = characterId,
            HasParticipations = true,
            Participations = new List<HistoricalParticipation>
            {
                new HistoricalParticipation
                {
                    ParticipationId = participationId,
                    EventId = eventId,
                    EventName = "Test Event",
                    EventCategory = EventCategory.Cultural,
                    Role = ParticipationRole.Witness,
                    EventDate = DateTimeOffset.UtcNow,
                    Significance = 0.5f,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            },
            HasBackstory = false,
            Backstory = null
        };

        var compressedData = CompressArchiveData(archiveData);

        SetupDefaultMessageBus();
        SetupEmptyCharacterIndex(characterId);
        SetupEmptyEventIndex(eventId);
        SetupParticipationSave();

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = compressedData
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ParticipationsRestored);
        Assert.False(response.BackstoryRestored);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_WithInvalidBase64_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = "not-valid-base64!!!"
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_WithInvalidGzip_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        // Valid base64 but not valid gzip
        var invalidData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not gzip data"));

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = invalidData
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_WhenSaveFails_ReturnsInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var archiveData = new CharacterHistoryArchive
        {
            // ResourceArchiveBase fields
            ResourceId = characterId,
            ResourceType = "character-history",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            // Service-specific fields
            CharacterId = characterId,
            HasParticipations = false,
            Participations = new List<HistoricalParticipation>(),
            HasBackstory = true,
            Backstory = new BackstoryResponse
            {
                Elements = new List<BackstoryElement>
                {
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.Origin,
                        Key = "homeland",
                        Value = "Test",
                        Strength = 0.5f
                    }
                },
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            }
        };

        var compressedData = CompressArchiveData(archiveData);

        _mockBackstoryStore.Setup(s => s.GetAsync($"backstory-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BackstoryData?)null);
        _mockBackstoryStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BackstoryData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Save failed"));

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = compressedData
        };

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<Exception>(() => service.RestoreFromArchiveAsync(request, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Helper method to compress archive data for testing RestoreFromArchive.
    /// </summary>
    private static string CompressArchiveData(CharacterHistoryArchive data)
    {
        var json = BannouJson.Serialize(data);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var output = new System.IO.MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(
            output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    #endregion
}
