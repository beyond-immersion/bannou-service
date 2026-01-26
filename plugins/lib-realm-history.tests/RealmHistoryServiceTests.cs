using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.RealmHistory;
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
    private readonly Mock<IStateStore<RealmParticipationIndexData>> _mockIndexStore;
    private readonly Mock<IStateStore<RealmLoreData>> _mockLoreStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    private const string STATE_STORE = "realm-history-statestore";

    public RealmHistoryServiceTests()
    {
        _mockLogger = new Mock<ILogger<RealmHistoryService>>();
        _configuration = new RealmHistoryServiceConfiguration();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockParticipationStore = new Mock<IStateStore<RealmParticipationData>>();
        _mockIndexStore = new Mock<IStateStore<RealmParticipationIndexData>>();
        _mockLoreStore = new Mock<IStateStore<RealmLoreData>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup default factory returns
        _mockStateStoreFactory
            .Setup(f => f.GetStore<RealmParticipationData>(STATE_STORE))
            .Returns(_mockParticipationStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<RealmParticipationIndexData>(STATE_STORE))
            .Returns(_mockIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<RealmLoreData>(STATE_STORE))
            .Returns(_mockLoreStore.Object);
    }

    private RealmHistoryService CreateService()
    {
        return new RealmHistoryService(
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
            .ReturnsAsync((RealmParticipationIndexData?)null);

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
        var existingIndex = new RealmParticipationIndexData
        {
            RealmId = realmId,
            ParticipationIds = new List<Guid> { Guid.NewGuid() }
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
            .ReturnsAsync((RealmParticipationIndexData?)null);

        // Act
        await service.RecordRealmParticipationAsync(request, CancellationToken.None);

        // Assert - Index should now have 2 participation IDs
        _mockIndexStore.Verify(s => s.SaveAsync(
            $"realm-participation-index-{realmId}",
            It.Is<RealmParticipationIndexData>(i => i.ParticipationIds.Count == 2),
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

        _mockIndexStore.Setup(s => s.GetAsync($"realm-participation-index-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmParticipationIndexData?)null);

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
    public async Task GetRealmParticipationAsync_WithFilter_FiltersResults()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var warParticipationId = Guid.NewGuid();
        var treatyParticipationId = Guid.NewGuid();

        var index = new RealmParticipationIndexData
        {
            RealmId = realmId,
            ParticipationIds = new List<Guid> { warParticipationId, treatyParticipationId }
        };

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

        var treatyParticipation = new RealmParticipationData
        {
            ParticipationId = treatyParticipationId,
            RealmId = realmId,
            EventId = Guid.NewGuid(),
            EventName = "Treaty Event",
            EventCategory = RealmEventCategory.TREATY,
            Role = RealmEventRole.MEDIATOR,
            EventDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Impact = 0.5f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _mockIndexStore.Setup(s => s.GetAsync($"realm-participation-index-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);

        // Service uses GetBulkAsync for fetching participations
        var bulkResult = new Dictionary<string, RealmParticipationData>
        {
            { $"realm-participation-{warParticipationId}", warParticipation },
            { $"realm-participation-{treatyParticipationId}", treatyParticipation }
        };
        _mockParticipationStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResult);

        var request = new GetRealmParticipationRequest
        {
            RealmId = realmId,
            EventCategory = RealmEventCategory.WAR, // Only WAR events
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
    }

    #endregion

    #region Lore Tests

    [Fact]
    public async Task GetRealmLoreAsync_NoLore_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        var request = new GetRealmLoreRequest { RealmId = realmId };

        _mockLoreStore.Setup(s => s.GetAsync($"realm-lore-{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmLoreData?)null);

        // Act
        var (status, result) = await service.GetRealmLoreAsync(request, CancellationToken.None);

        // Assert - RealmHistory returns OK with empty list (differs from CharacterHistory which returns NotFound)
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Empty(result.Elements);
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
