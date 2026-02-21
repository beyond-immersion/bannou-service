using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.RealmHistory;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.RealmHistory.Tests;

/// <summary>
/// Unit tests for RealmHistoryService.
/// Tests participation recording, lore management, and event publishing.
/// </summary>
public class RealmHistoryServiceTests
{
    private readonly Mock<ILogger<RealmHistoryService>> _mockLogger;
    private readonly RealmHistoryServiceConfiguration _configuration;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<RealmParticipationData>> _mockParticipationStore;
    private readonly Mock<IStateStore<HistoryIndexData>> _mockIndexStore;
    private readonly Mock<IStateStore<RealmLoreData>> _mockLoreStore;
    private readonly Mock<IJsonQueryableStateStore<RealmParticipationData>> _mockJsonQueryableStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IResourceClient> _mockResourceClient;

    private const string STATE_STORE = "realm-history-statestore";

    public RealmHistoryServiceTests()
    {
        _mockLogger = new Mock<ILogger<RealmHistoryService>>();
        _configuration = new RealmHistoryServiceConfiguration();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockParticipationStore = new Mock<IStateStore<RealmParticipationData>>();
        _mockIndexStore = new Mock<IStateStore<HistoryIndexData>>();
        _mockLoreStore = new Mock<IStateStore<RealmLoreData>>();
        _mockJsonQueryableStore = new Mock<IJsonQueryableStateStore<RealmParticipationData>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockResourceClient = new Mock<IResourceClient>();

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
            .Setup(f => f.GetStore<RealmParticipationData>(STATE_STORE))
            .Returns(_mockParticipationStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<HistoryIndexData>(STATE_STORE))
            .Returns(_mockIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<RealmLoreData>(STATE_STORE))
            .Returns(_mockLoreStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<RealmParticipationData>(STATE_STORE))
            .Returns(_mockJsonQueryableStore.Object);
    }

    private RealmHistoryService CreateService()
    {
        return new RealmHistoryService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object,
            _mockLockProvider.Object,
            _mockResourceClient.Object);
    }

    private void SetupJsonQueryPagedAsync(
        List<RealmParticipationData> items, long totalCount, int offset = 0, int limit = 20)
    {
        var queryResults = items.Select(m =>
            new JsonQueryResult<RealmParticipationData>($"realm-participation-{m.ParticipationId}", m)).ToList();

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<RealmParticipationData>(queryResults, totalCount, offset, limit));
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void RealmHistoryService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<RealmHistoryService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void RealmHistoryServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new RealmHistoryServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void RealmHistoryServiceConfiguration_MaxLoreElements_DefaultIs100()
    {
        // Arrange & Act
        var config = new RealmHistoryServiceConfiguration();

        // Assert
        Assert.Equal(100, config.MaxLoreElements);
    }

    #endregion

    #region RecordRealmParticipation Tests

    [Fact]
    public async Task RecordRealmParticipationAsync_ValidRequest_ReturnsOKAndCreatesParticipation()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var request = new RecordRealmParticipationRequest
        {
            RealmId = realmId,
            EventId = eventId,
            EventName = "The Great War",
            EventCategory = RealmEventCategory.WAR,
            Role = RealmEventRole.DEFENDER,
            EventDate = DateTimeOffset.UtcNow.AddDays(-30),
            Impact = 0.8f,
            Metadata = null
        };

        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        var (status, result) = await service.RecordRealmParticipationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(realmId, result.RealmId);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal("The Great War", result.EventName);
        Assert.Equal(RealmEventCategory.WAR, result.EventCategory);
        Assert.Equal(RealmEventRole.DEFENDER, result.Role);
        Assert.NotEqual(Guid.Empty, result.ParticipationId);

        // Verify state was saved
        _mockParticipationStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("realm-participation-")),
            It.IsAny<RealmParticipationData>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm-history.participation.recorded",
            It.IsAny<RealmParticipationRecordedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordRealmParticipationAsync_UpdatesRealmIndex()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var existingIndex = new HistoryIndexData
        {
            EntityId = realmId.ToString(),
            RecordIds = new List<string> { Guid.NewGuid().ToString() }
        };

        var request = new RecordRealmParticipationRequest
        {
            RealmId = realmId,
            EventId = Guid.NewGuid(),
            EventName = "New Treaty",
            EventCategory = RealmEventCategory.TREATY,
            Role = RealmEventRole.MEDIATOR,
            EventDate = DateTimeOffset.UtcNow,
            Impact = 0.5f
        };

        _mockIndexStore.Setup(s => s.GetAsync($"realm-participation-index-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndex);
        _mockIndexStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("realm-participation-event-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryIndexData?)null);

        // Act
        await service.RecordRealmParticipationAsync(request, CancellationToken.None);

        // Assert - Index should now have 2 participation IDs
        _mockIndexStore.Verify(s => s.SaveAsync(
            $"realm-participation-index-{realmId}",
            It.Is<HistoryIndexData>(i => i.RecordIds.Count == 2),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetRealmParticipation Tests

    [Fact]
    public async Task GetRealmParticipationAsync_NoParticipations_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var request = new GetRealmParticipationRequest
        {
            RealmId = realmId,
            Page = 1,
            PageSize = 20
        };

        SetupJsonQueryPagedAsync(new List<RealmParticipationData>(), 0);

        // Act
        var (status, result) = await service.GetRealmParticipationAsync(request, CancellationToken.None);

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
    public async Task GetRealmParticipationAsync_WithFilter_ReturnsFilteredResults()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var warParticipationId = Guid.NewGuid();

        var warParticipation = new RealmParticipationData
        {
            ParticipationId = warParticipationId,
            RealmId = realmId,
            EventId = Guid.NewGuid(),
            EventName = "War Event",
            EventCategory = RealmEventCategory.WAR,
            Role = RealmEventRole.DEFENDER,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Impact = 0.8f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Server-side query returns only matching results (filtered at database level)
        SetupJsonQueryPagedAsync(new List<RealmParticipationData> { warParticipation }, 1);

        var request = new GetRealmParticipationRequest
        {
            RealmId = realmId,
            EventCategory = RealmEventCategory.WAR,
            Page = 1,
            PageSize = 20
        };

        // Act
        var (status, result) = await service.GetRealmParticipationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Participations);
        Assert.Equal("War Event", result.Participations.First().EventName);

        // Verify query conditions were passed (filters pushed to database)
        _mockJsonQueryableStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>?>(c =>
                c != null && c.Any(q => q.Path == "$.RealmId") &&
                c.Any(q => q.Path == "$.EventCategory")),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRealmParticipationAsync_PaginatesCorrectly()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var participation = new RealmParticipationData
        {
            ParticipationId = Guid.NewGuid(),
            RealmId = realmId,
            EventId = Guid.NewGuid(),
            EventName = "Page 2 Event",
            EventCategory = RealmEventCategory.WAR,
            Role = RealmEventRole.DEFENDER,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Impact = 0.8f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Page 2 of 3 total items, pageSize=1
        SetupJsonQueryPagedAsync(new List<RealmParticipationData> { participation }, totalCount: 3, offset: 1, limit: 1);

        var request = new GetRealmParticipationRequest
        {
            RealmId = realmId,
            Page = 2,
            PageSize = 1
        };

        // Act
        var (status, result) = await service.GetRealmParticipationAsync(request, CancellationToken.None);

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

    #region GetRealmEventParticipants Tests

    [Fact]
    public async Task GetRealmEventParticipantsAsync_NoParticipants_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var eventId = Guid.NewGuid();

        var request = new GetRealmEventParticipantsRequest
        {
            EventId = eventId,
            Page = 1,
            PageSize = 20
        };

        SetupJsonQueryPagedAsync(new List<RealmParticipationData>(), 0);

        // Act
        var (status, result) = await service.GetRealmEventParticipantsAsync(request, CancellationToken.None);

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
    public async Task GetRealmEventParticipantsAsync_WithRoleFilter_ReturnsFilteredResults()
    {
        // Arrange
        var service = CreateService();
        var eventId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var defenderParticipation = new RealmParticipationData
        {
            ParticipationId = Guid.NewGuid(),
            RealmId = realmId,
            EventId = eventId,
            EventName = "Great War",
            EventCategory = RealmEventCategory.WAR,
            Role = RealmEventRole.DEFENDER,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Impact = 0.9f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Server-side query returns only DEFENDER role results
        SetupJsonQueryPagedAsync(new List<RealmParticipationData> { defenderParticipation }, 1);

        var request = new GetRealmEventParticipantsRequest
        {
            EventId = eventId,
            Role = RealmEventRole.DEFENDER,
            Page = 1,
            PageSize = 20
        };

        // Act
        var (status, result) = await service.GetRealmEventParticipantsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Participations);
        Assert.Equal(RealmEventRole.DEFENDER, result.Participations.First().Role);

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

    #region Lore Tests

    [Fact]
    public async Task GetRealmLoreAsync_NoLore_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var request = new GetRealmLoreRequest { RealmId = realmId };

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmLoreData?)null);

        // Act
        var (status, result) = await service.GetRealmLoreAsync(request, CancellationToken.None);

        // Assert - Consistent with CharacterHistory's GetBackstory: NotFound when no document exists
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetRealmLoreAsync_NewLore_CreatesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var request = new SetRealmLoreRequest
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElement>
            {
                new RealmLoreElement
                {
                    ElementType = RealmLoreElementType.ORIGIN_MYTH,
                    Key = "creation",
                    Value = "Born from the void",
                    Strength = 0.9f
                }
            },
            ReplaceExisting = false
        };

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmLoreData?)null);

        // Act
        var (status, result) = await service.SetRealmLoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Elements);
        Assert.Equal("creation", result.Elements.First().Key);

        // Verify lore.created event was published (new lore)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm-history.lore.created",
            It.IsAny<RealmLoreCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetRealmLoreAsync_ExistingLore_UpdatesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var existingLore = new RealmLoreData
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElementData>
            {
                new RealmLoreElementData
                {
                    ElementType = RealmLoreElementType.ORIGIN_MYTH,
                    Key = "creation",
                    Value = "Old value",
                    Strength = 0.5f
                }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLore);

        var request = new SetRealmLoreRequest
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElement>
            {
                new RealmLoreElement
                {
                    ElementType = RealmLoreElementType.ORIGIN_MYTH,
                    Key = "creation",
                    Value = "Updated value",
                    Strength = 0.9f
                }
            },
            ReplaceExisting = false
        };

        // Act
        var (status, result) = await service.SetRealmLoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify lore.updated event was published (existing lore)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm-history.lore.updated",
            It.IsAny<RealmLoreUpdatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Lore Element Limit Tests

    [Fact]
    public async Task SetRealmLoreAsync_ReplaceMode_ExceedsLimit_ReturnsBadRequest()
    {
        // Arrange
        _configuration.MaxLoreElements = 2;
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var request = new SetRealmLoreRequest
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElement>
            {
                new RealmLoreElement { ElementType = RealmLoreElementType.ORIGIN_MYTH, Key = "k1", Value = "v1", Strength = 0.5f },
                new RealmLoreElement { ElementType = RealmLoreElementType.CULTURAL_PRACTICE, Key = "k2", Value = "v2", Strength = 0.5f },
                new RealmLoreElement { ElementType = RealmLoreElementType.POLITICAL_SYSTEM, Key = "k3", Value = "v3", Strength = 0.5f }
            },
            ReplaceExisting = true
        };

        // Act
        var (status, result) = await service.SetRealmLoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetRealmLoreAsync_MergeMode_PostMergeExceedsLimit_ReturnsBadRequest()
    {
        // Arrange
        _configuration.MaxLoreElements = 2;
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var existingLore = new RealmLoreData
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElementData>
            {
                new RealmLoreElementData { ElementType = RealmLoreElementType.ORIGIN_MYTH, Key = "existing1", Value = "v1", Strength = 0.5f },
                new RealmLoreElementData { ElementType = RealmLoreElementType.CULTURAL_PRACTICE, Key = "existing2", Value = "v2", Strength = 0.5f }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLore);

        var request = new SetRealmLoreRequest
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElement>
            {
                // This is a truly new element (different type+key), pushing count to 3 > limit of 2
                new RealmLoreElement { ElementType = RealmLoreElementType.POLITICAL_SYSTEM, Key = "new1", Value = "v3", Strength = 0.5f }
            },
            ReplaceExisting = false
        };

        // Act
        var (status, result) = await service.SetRealmLoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetRealmLoreAsync_MergeMode_UpdatesExisting_AllowedEvenAtLimit()
    {
        // Arrange
        _configuration.MaxLoreElements = 2;
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var existingLore = new RealmLoreData
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElementData>
            {
                new RealmLoreElementData { ElementType = RealmLoreElementType.ORIGIN_MYTH, Key = "creation", Value = "old", Strength = 0.5f },
                new RealmLoreElementData { ElementType = RealmLoreElementType.CULTURAL_PRACTICE, Key = "farming", Value = "old", Strength = 0.5f }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLore);

        var request = new SetRealmLoreRequest
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElement>
            {
                // Same type+key as existing = update, not new. Count stays at 2 (at limit, not over)
                new RealmLoreElement { ElementType = RealmLoreElementType.ORIGIN_MYTH, Key = "creation", Value = "updated", Strength = 0.9f }
            },
            ReplaceExisting = false
        };

        // Act
        var (status, result) = await service.SetRealmLoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddRealmLoreElementAsync_AtLimit_NewElement_ReturnsBadRequest()
    {
        // Arrange
        _configuration.MaxLoreElements = 2;
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var existingLore = new RealmLoreData
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElementData>
            {
                new RealmLoreElementData { ElementType = RealmLoreElementType.ORIGIN_MYTH, Key = "k1", Value = "v1", Strength = 0.5f },
                new RealmLoreElementData { ElementType = RealmLoreElementType.CULTURAL_PRACTICE, Key = "k2", Value = "v2", Strength = 0.5f }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLore);

        var request = new AddRealmLoreElementRequest
        {
            RealmId = realmId,
            Element = new RealmLoreElement
            {
                ElementType = RealmLoreElementType.POLITICAL_SYSTEM,
                Key = "new_element",
                Value = "v3",
                Strength = 0.5f
            }
        };

        // Act
        var (status, result) = await service.AddRealmLoreElementAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddRealmLoreElementAsync_AtLimit_UpdateExisting_Allowed()
    {
        // Arrange
        _configuration.MaxLoreElements = 2;
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var existingLore = new RealmLoreData
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElementData>
            {
                new RealmLoreElementData { ElementType = RealmLoreElementType.ORIGIN_MYTH, Key = "creation", Value = "old_value", Strength = 0.5f },
                new RealmLoreElementData { ElementType = RealmLoreElementType.CULTURAL_PRACTICE, Key = "farming", Value = "old_value", Strength = 0.5f }
            },
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLore);

        var request = new AddRealmLoreElementRequest
        {
            RealmId = realmId,
            Element = new RealmLoreElement
            {
                // Same type+key = update, allowed even at limit
                ElementType = RealmLoreElementType.ORIGIN_MYTH,
                Key = "creation",
                Value = "updated_value",
                Strength = 0.9f
            }
        };

        // Act
        var (status, result) = await service.AddRealmLoreElementAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SetRealmLoreAsync_MergeMode_NoExistingLore_ExceedsLimit_ReturnsBadRequest()
    {
        // Arrange
        _configuration.MaxLoreElements = 1;
        var service = CreateService();
        var realmId = Guid.NewGuid();

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmLoreData?)null);

        var request = new SetRealmLoreRequest
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElement>
            {
                new RealmLoreElement { ElementType = RealmLoreElementType.ORIGIN_MYTH, Key = "k1", Value = "v1", Strength = 0.5f },
                new RealmLoreElement { ElementType = RealmLoreElementType.CULTURAL_PRACTICE, Key = "k2", Value = "v2", Strength = 0.5f }
            },
            ReplaceExisting = false
        };

        // Act
        var (status, result) = await service.SetRealmLoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteRealmParticipationAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var participationId = Guid.NewGuid();

        var request = new DeleteRealmParticipationRequest { ParticipationId = participationId };

        _mockParticipationStore.Setup(s => s.GetAsync($"realm-participation-{participationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmParticipationData?)null);

        // Act
        var status = await service.DeleteRealmParticipationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeleteRealmLoreAsync_Existing_DeletesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var existingLoreForDelete = new RealmLoreData
        {
            RealmId = realmId,
            Elements = new List<RealmLoreElementData>(),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLoreForDelete);

        var request = new DeleteRealmLoreRequest { RealmId = realmId };

        // Act
        var status = await service.DeleteRealmLoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        _mockLoreStore.Verify(s => s.DeleteAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()), Times.Once);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm-history.lore.deleted",
            It.IsAny<RealmLoreDeletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRealmLoreAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmLoreData?)null);

        var request = new DeleteRealmLoreRequest { RealmId = realmId };

        // Act
        var status = await service.DeleteRealmLoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion
}
