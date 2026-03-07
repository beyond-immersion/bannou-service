using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Relationship.Caching;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Relationship.Tests;

/// <summary>
/// Unit tests for RelationshipService.
/// Tests relationship management operations including CRUD, composite uniqueness, and soft-delete.
/// </summary>
public class RelationshipServiceTests : ServiceTestBase<RelationshipServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<RelationshipModel>> _mockRelationshipStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockListStore;
    private readonly Mock<IStateStore<RelationshipTypeModel>> _mockRtModelStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<RelationshipService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<IRelationshipDataCache> _mockRelationshipCache;

    private const string STATE_STORE = "relationship-statestore";

    public RelationshipServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockRelationshipStore = new Mock<IStateStore<RelationshipModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<Guid>>>();
        _mockRtModelStore = new Mock<IStateStore<RelationshipTypeModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<RelationshipService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockRelationshipCache = new Mock<IRelationshipDataCache>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<RelationshipModel>(STATE_STORE)).Returns(_mockRelationshipStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE)).Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE)).Returns(_mockListStore.Object);

        // Setup relationship-type-statestore (needed by constructor caching and deprecation checks)
        _mockStateStoreFactory.Setup(f => f.GetStore<RelationshipTypeModel>("relationship-type-statestore")).Returns(_mockRtModelStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>("relationship-type-statestore")).Returns(new Mock<IStateStore<string>>().Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>("relationship-type-statestore")).Returns(new Mock<IStateStore<List<Guid>>>().Object);

        // Setup lock provider to always succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private RelationshipService CreateService()
    {
        return new RelationshipService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockLockProvider.Object,
            _mockEventConsumer.Object,
            _mockTelemetryProvider.Object,
            _mockResourceClient.Object,
            _mockRelationshipCache.Object);
    }

    #region Constructor Tests

    #endregion

    #region GetRelationship Tests

    [Fact]
    public async Task GetRelationshipAsync_ExistingRelationship_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestRelationshipModel(relationshipId);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetRelationshipAsync(
            new GetRelationshipRequest { RelationshipId = relationshipId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(relationshipId, response.RelationshipId);
    }

    [Fact]
    public async Task GetRelationshipAsync_NonExistentRelationship_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationshipModel?)null);

        // Act
        var (status, response) = await service.GetRelationshipAsync(
            new GetRelationshipRequest { RelationshipId = relationshipId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region CreateRelationship Tests

    [Fact]
    public async Task CreateRelationshipAsync_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();
        var relationshipTypeId = Guid.NewGuid();

        SetupCreateRelationshipMocks(entity1Id, entity2Id, relationshipTypeId, existingCompositeKey: false);

        RelationshipModel? savedModel = null;
        string? savedKey = null;
        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipModel, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag");

        var startedAt = DateTimeOffset.UtcNow;
        var request = new CreateRelationshipRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character,
            RelationshipTypeId = relationshipTypeId,
            StartedAt = startedAt
        };

        // Act
        var (status, response) = await service.CreateRelationshipAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(entity1Id, response.Entity1Id);
        Assert.Equal(entity2Id, response.Entity2Id);
        Assert.Equal(relationshipTypeId, response.RelationshipTypeId);

        // Assert - State was saved correctly
        Assert.NotNull(savedModel);
        Assert.NotNull(savedKey);
        Assert.StartsWith("rel:", savedKey);
        Assert.Equal(entity1Id, savedModel.Entity1Id);
        Assert.Equal(entity2Id, savedModel.Entity2Id);
        Assert.Equal(EntityType.Character, savedModel.Entity1Type);
        Assert.Equal(EntityType.Character, savedModel.Entity2Type);
        Assert.Equal(relationshipTypeId, savedModel.RelationshipTypeId);
        Assert.Equal(startedAt, savedModel.StartedAt);
    }

    [Fact]
    public async Task CreateRelationshipAsync_DuplicateCompositeKey_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();
        var relationshipTypeId = Guid.NewGuid();

        // Setup: composite key already exists
        SetupCreateRelationshipMocks(entity1Id, entity2Id, relationshipTypeId, existingCompositeKey: true);

        var request = new CreateRelationshipRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character,
            RelationshipTypeId = relationshipTypeId,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.CreateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateRelationshipAsync_WithMetadata_StoresMetadataCorrectly()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();
        var relationshipTypeId = Guid.NewGuid();
        var metadata = new Dictionary<string, object> { { "strength", 100 }, { "notes", "Close friends" } };

        SetupCreateRelationshipMocks(entity1Id, entity2Id, relationshipTypeId, existingCompositeKey: false);

        RelationshipModel? savedModel = null;
        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new CreateRelationshipRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Actor,
            RelationshipTypeId = relationshipTypeId,
            StartedAt = DateTimeOffset.UtcNow,
            Metadata = metadata
        };

        // Act
        var (status, response) = await service.CreateRelationshipAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Metadata);

        // Assert - State was saved with metadata
        Assert.NotNull(savedModel);
        Assert.NotNull(savedModel.Metadata);
        Assert.Equal(EntityType.Character, savedModel.Entity1Type);
        Assert.Equal(EntityType.Actor, savedModel.Entity2Type);
    }

    [Fact]
    public async Task CreateRelationshipAsync_PublishesCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();
        var relationshipTypeId = Guid.NewGuid();

        SetupCreateRelationshipMocks(entity1Id, entity2Id, relationshipTypeId, existingCompositeKey: false);

        RelationshipCreatedEvent? capturedEvent = null;
        string? capturedTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<RelationshipCreatedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipCreatedEvent, PublishOptions?, Guid?, CancellationToken>((t, e, _, _, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var request = new CreateRelationshipRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character,
            RelationshipTypeId = relationshipTypeId,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.CreateRelationshipAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Assert - Event was published with correct content
        Assert.NotNull(capturedEvent);
        Assert.Equal("relationship.created", capturedTopic);
        Assert.Equal(response.RelationshipId, capturedEvent.RelationshipId);
        Assert.Equal(entity1Id, capturedEvent.Entity1Id);
        Assert.Equal(entity2Id, capturedEvent.Entity2Id);
        Assert.Equal(relationshipTypeId, capturedEvent.RelationshipTypeId);
    }

    #endregion

    #region UpdateRelationship Tests

    [Fact]
    public async Task UpdateRelationshipAsync_ExistingRelationship_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestRelationshipModel(relationshipId);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        RelationshipModel? savedModel = null;
        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new UpdateRelationshipRequest
        {
            RelationshipId = relationshipId,
            Metadata = new Dictionary<string, object> { { "updated", true } }
        };

        // Act
        var (status, response) = await service.UpdateRelationshipAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(relationshipId, response.RelationshipId);

        // Assert - State was saved with updated metadata
        Assert.NotNull(savedModel);
        Assert.NotNull(savedModel.Metadata);
    }

    [Fact]
    public async Task UpdateRelationshipAsync_NonExistentRelationship_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationshipModel?)null);

        var request = new UpdateRelationshipRequest
        {
            RelationshipId = relationshipId,
            Metadata = new Dictionary<string, object> { { "updated", true } }
        };

        // Act
        var (status, response) = await service.UpdateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateRelationshipAsync_PublishesUpdatedEvent()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestRelationshipModel(relationshipId);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        RelationshipUpdatedEvent? capturedEvent = null;
        string? capturedTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<RelationshipUpdatedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipUpdatedEvent, PublishOptions?, Guid?, CancellationToken>((t, e, _, _, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var request = new UpdateRelationshipRequest
        {
            RelationshipId = relationshipId,
            Metadata = new Dictionary<string, object> { { "updated", true } }
        };

        // Act
        var (status, _) = await service.UpdateRelationshipAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);

        // Assert - Event was published with correct content
        Assert.NotNull(capturedEvent);
        Assert.Equal("relationship.updated", capturedTopic);
        Assert.Equal(relationshipId, capturedEvent.RelationshipId);
        Assert.Contains("metadata", capturedEvent.ChangedFields);
    }

    #endregion

    #region EndRelationship Tests

    [Fact]
    public async Task EndRelationshipAsync_ExistingRelationship_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestRelationshipModel(relationshipId);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new EndRelationshipRequest
        {
            RelationshipId = relationshipId,
            Reason = "Friendship ended"
        };

        // Act
        var status = await service.EndRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task EndRelationshipAsync_NonExistentRelationship_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationshipModel?)null);

        var request = new EndRelationshipRequest { RelationshipId = relationshipId };

        // Act
        var status = await service.EndRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task EndRelationshipAsync_SetsEndedAtTimestamp()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestRelationshipModel(relationshipId);
        RelationshipModel? savedModel = null;

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockRelationshipStore
            .Setup(s => s.SaveAsync($"rel:{relationshipId}", It.IsAny<RelationshipModel>(), null, It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipModel, object?, CancellationToken>((key, data, metadata, ct) => savedModel = data)
            .ReturnsAsync("etag");

        var request = new EndRelationshipRequest { RelationshipId = relationshipId };

        // Act
        var status = await service.EndRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedModel);
        Assert.NotNull(savedModel.EndedAt);
    }

    [Fact]
    public async Task EndRelationshipAsync_PublishesDeletedEvent()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestRelationshipModel(relationshipId);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        RelationshipDeletedEvent? capturedEvent = null;
        string? capturedTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<RelationshipDeletedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipDeletedEvent, PublishOptions?, Guid?, CancellationToken>((t, e, _, _, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var request = new EndRelationshipRequest
        {
            RelationshipId = relationshipId,
            Reason = "Test reason"
        };

        // Act
        var status = await service.EndRelationshipAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);

        // Assert - Event was published with correct content
        Assert.NotNull(capturedEvent);
        Assert.Equal("relationship.deleted", capturedTopic);
        Assert.Equal(relationshipId, capturedEvent.RelationshipId);
        Assert.Equal("Test reason", capturedEvent.DeletedReason);
    }

    #endregion

    #region ListRelationshipsByEntity Tests

    [Fact]
    public async Task ListRelationshipsByEntityAsync_NoRelationships_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();

        // Entity index key uses EntityType.ToString() which returns PascalCase (e.g., "Character")
        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        var request = new ListRelationshipsByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.ListRelationshipsByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Relationships);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task ListRelationshipsByEntityAsync_WithRelationships_ReturnsList()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var relationshipIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Entity index key uses EntityType.ToString() which returns PascalCase (e.g., "Character")
        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(relationshipIds);

        // Setup bulk retrieval
        var bulkResults = relationshipIds.ToDictionary(
            id => $"rel:{id}",
            id => CreateTestRelationshipModel(id));

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new ListRelationshipsByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.ListRelationshipsByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Relationships.Count);
    }

    #endregion

    #region GetRelationshipsBetween Tests

    [Fact]
    public async Task GetRelationshipsBetweenAsync_NoRelationshipsForEntity1_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entity1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        var request = new GetRelationshipsBetweenRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character
        };

        // Act
        var (status, response) = await service.GetRelationshipsBetweenAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Relationships);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetRelationshipsBetweenAsync_WithMatchingRelationships_ReturnsFiltered()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();
        var unrelatedEntityId = Guid.NewGuid();

        var matchingRelId = Guid.NewGuid();
        var unrelatedRelId = Guid.NewGuid();

        var matchingModel = CreateTestRelationshipModel(matchingRelId);
        matchingModel.Entity1Id = entity1Id;
        matchingModel.Entity2Id = entity2Id;

        var unrelatedModel = CreateTestRelationshipModel(unrelatedRelId);
        unrelatedModel.Entity1Id = entity1Id;
        unrelatedModel.Entity2Id = unrelatedEntityId;

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entity1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { matchingRelId, unrelatedRelId });

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{matchingRelId}"] = matchingModel,
            [$"rel:{unrelatedRelId}"] = unrelatedModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new GetRelationshipsBetweenRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.GetRelationshipsBetweenAsync(request);

        // Assert - Only the matching relationship between entity1 and entity2 is returned
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(matchingRelId, response.Relationships.First().RelationshipId);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task GetRelationshipsBetweenAsync_FiltersEndedByDefault()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();

        var activeRelId = Guid.NewGuid();
        var endedRelId = Guid.NewGuid();

        var activeModel = CreateTestRelationshipModel(activeRelId);
        activeModel.Entity1Id = entity1Id;
        activeModel.Entity2Id = entity2Id;
        activeModel.EndedAt = null;

        var endedModel = CreateTestRelationshipModel(endedRelId);
        endedModel.Entity1Id = entity1Id;
        endedModel.Entity2Id = entity2Id;
        endedModel.EndedAt = DateTimeOffset.UtcNow.AddDays(-1);

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entity1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { activeRelId, endedRelId });

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{activeRelId}"] = activeModel,
            [$"rel:{endedRelId}"] = endedModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new GetRelationshipsBetweenRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character,
            IncludeEnded = false
        };

        // Act
        var (status, response) = await service.GetRelationshipsBetweenAsync(request);

        // Assert - Only the active relationship is returned (ended filtered out)
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(activeRelId, response.Relationships.First().RelationshipId);
    }

    [Fact]
    public async Task GetRelationshipsBetweenAsync_WithTypeFilter_FiltersCorrectly()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();
        var friendTypeId = Guid.NewGuid();
        var enemyTypeId = Guid.NewGuid();

        var friendRelId = Guid.NewGuid();
        var enemyRelId = Guid.NewGuid();

        var friendModel = CreateTestRelationshipModel(friendRelId);
        friendModel.Entity1Id = entity1Id;
        friendModel.Entity2Id = entity2Id;
        friendModel.RelationshipTypeId = friendTypeId;

        var enemyModel = CreateTestRelationshipModel(enemyRelId);
        enemyModel.Entity1Id = entity1Id;
        enemyModel.Entity2Id = entity2Id;
        enemyModel.RelationshipTypeId = enemyTypeId;

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entity1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { friendRelId, enemyRelId });

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{friendRelId}"] = friendModel,
            [$"rel:{enemyRelId}"] = enemyModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new GetRelationshipsBetweenRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character,
            RelationshipTypeId = friendTypeId,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.GetRelationshipsBetweenAsync(request);

        // Assert - Only the friend relationship is returned
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(friendRelId, response.Relationships.First().RelationshipId);
        Assert.Equal(friendTypeId, response.Relationships.First().RelationshipTypeId);
    }

    [Fact]
    public async Task GetRelationshipsBetweenAsync_DataInconsistency_SkipsNullAndEmitsError()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();
        var orphanedRelId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entity1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { orphanedRelId });

        // Return null for the relationship (simulates data inconsistency)
        var bulkResults = new Dictionary<string, RelationshipModel>();
        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new GetRelationshipsBetweenRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.GetRelationshipsBetweenAsync(request);

        // Assert - Returns empty (orphaned entry was skipped), no crash
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Relationships);
    }

    [Fact]
    public async Task GetRelationshipsBetweenAsync_Pagination_ReturnsCorrectPage()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();

        // Create 3 relationships between entity1 and entity2 at different times
        var relIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var bulkResults = new Dictionary<string, RelationshipModel>();

        for (int i = 0; i < relIds.Count; i++)
        {
            var model = CreateTestRelationshipModel(relIds[i]);
            model.Entity1Id = entity1Id;
            model.Entity2Id = entity2Id;
            model.CreatedAt = DateTimeOffset.UtcNow.AddHours(-i);
            bulkResults[$"rel:{relIds[i]}"] = model;
        }

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entity1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(relIds);

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new GetRelationshipsBetweenRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character,
            IncludeEnded = true,
            Page = 1,
            PageSize = 2
        };

        // Act
        var (status, response) = await service.GetRelationshipsBetweenAsync(request);

        // Assert - Page 1 with size 2 returns 2 items, total count 3, has next page
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Relationships.Count);
        Assert.Equal(3, response.TotalCount);
        Assert.True(response.HasNextPage);
        Assert.False(response.HasPreviousPage);
    }

    #endregion

    #region ListRelationshipsByType Tests

    [Fact]
    public async Task ListRelationshipsByTypeAsync_NoRelationships_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"type-idx:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        var request = new ListRelationshipsByTypeRequest
        {
            RelationshipTypeId = typeId
        };

        // Act
        var (status, response) = await service.ListRelationshipsByTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Relationships);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task ListRelationshipsByTypeAsync_WithRelationships_ReturnsList()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var relId1 = Guid.NewGuid();
        var relId2 = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"type-idx:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { relId1, relId2 });

        var model1 = CreateTestRelationshipModel(relId1);
        model1.RelationshipTypeId = typeId;
        var model2 = CreateTestRelationshipModel(relId2);
        model2.RelationshipTypeId = typeId;

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{relId1}"] = model1,
            [$"rel:{relId2}"] = model2
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new ListRelationshipsByTypeRequest
        {
            RelationshipTypeId = typeId,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.ListRelationshipsByTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Relationships.Count);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task ListRelationshipsByTypeAsync_FiltersEndedByDefault()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var activeRelId = Guid.NewGuid();
        var endedRelId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"type-idx:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { activeRelId, endedRelId });

        var activeModel = CreateTestRelationshipModel(activeRelId);
        activeModel.RelationshipTypeId = typeId;
        activeModel.EndedAt = null;

        var endedModel = CreateTestRelationshipModel(endedRelId);
        endedModel.RelationshipTypeId = typeId;
        endedModel.EndedAt = DateTimeOffset.UtcNow.AddDays(-1);

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{activeRelId}"] = activeModel,
            [$"rel:{endedRelId}"] = endedModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new ListRelationshipsByTypeRequest
        {
            RelationshipTypeId = typeId,
            IncludeEnded = false
        };

        // Act
        var (status, response) = await service.ListRelationshipsByTypeAsync(request);

        // Assert - Only active relationship returned
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(activeRelId, response.Relationships.First().RelationshipId);
    }

    [Fact]
    public async Task ListRelationshipsByTypeAsync_FiltersByEntity1Type()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var charRelId = Guid.NewGuid();
        var actorRelId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"type-idx:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { charRelId, actorRelId });

        var charModel = CreateTestRelationshipModel(charRelId);
        charModel.RelationshipTypeId = typeId;
        charModel.Entity1Type = EntityType.Character;

        var actorModel = CreateTestRelationshipModel(actorRelId);
        actorModel.RelationshipTypeId = typeId;
        actorModel.Entity1Type = EntityType.Actor;

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{charRelId}"] = charModel,
            [$"rel:{actorRelId}"] = actorModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new ListRelationshipsByTypeRequest
        {
            RelationshipTypeId = typeId,
            Entity1Type = EntityType.Character,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.ListRelationshipsByTypeAsync(request);

        // Assert - Only character-entity1 relationship returned
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(charRelId, response.Relationships.First().RelationshipId);
    }

    [Fact]
    public async Task ListRelationshipsByTypeAsync_FiltersByEntity2Type()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var realmRelId = Guid.NewGuid();
        var charRelId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"type-idx:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { realmRelId, charRelId });

        var realmModel = CreateTestRelationshipModel(realmRelId);
        realmModel.RelationshipTypeId = typeId;
        realmModel.Entity2Type = EntityType.Realm;

        var charModel = CreateTestRelationshipModel(charRelId);
        charModel.RelationshipTypeId = typeId;
        charModel.Entity2Type = EntityType.Character;

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{realmRelId}"] = realmModel,
            [$"rel:{charRelId}"] = charModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new ListRelationshipsByTypeRequest
        {
            RelationshipTypeId = typeId,
            Entity2Type = EntityType.Realm,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.ListRelationshipsByTypeAsync(request);

        // Assert - Only realm-entity2 relationship returned
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(realmRelId, response.Relationships.First().RelationshipId);
    }

    [Fact]
    public async Task ListRelationshipsByTypeAsync_DataInconsistency_SkipsNullAndContinues()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var validRelId = Guid.NewGuid();
        var orphanedRelId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"type-idx:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { validRelId, orphanedRelId });

        var validModel = CreateTestRelationshipModel(validRelId);
        validModel.RelationshipTypeId = typeId;

        // Only return the valid relationship (orphaned one is missing from store)
        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{validRelId}"] = validModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new ListRelationshipsByTypeRequest
        {
            RelationshipTypeId = typeId,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.ListRelationshipsByTypeAsync(request);

        // Assert - Only the valid relationship returned (orphaned skipped)
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(validRelId, response.Relationships.First().RelationshipId);
    }

    #endregion

    #region CleanupByEntity Tests

    [Fact]
    public async Task CleanupByEntityAsync_NoRelationships_ReturnsZeroCounts()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        var request = new CleanupByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.CleanupByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.RelationshipsEnded);
        Assert.Equal(0, response.AlreadyEnded);
    }

    [Fact]
    public async Task CleanupByEntityAsync_ActiveRelationships_EndsThemAll()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var relId1 = Guid.NewGuid();
        var relId2 = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { relId1, relId2 });

        var model1 = CreateTestRelationshipModel(relId1);
        model1.Entity1Id = entityId;
        model1.Entity1Type = EntityType.Character;
        model1.EndedAt = null;

        var model2 = CreateTestRelationshipModel(relId2);
        model2.Entity1Id = entityId;
        model2.Entity1Type = EntityType.Character;
        model2.EndedAt = null;

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{relId1}"] = model1,
            [$"rel:{relId2}"] = model2
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        // Setup per-relationship re-fetch after lock (cleanup does a re-read under lock)
        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model1);
        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model2);

        // Capture saved models to verify EndedAt was set
        var savedModels = new Dictionary<string, RelationshipModel>();
        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipModel, StateOptions?, CancellationToken>((k, m, _, _) => savedModels[k] = m)
            .ReturnsAsync("etag");

        var request = new CleanupByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.CleanupByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.RelationshipsEnded);
        Assert.Equal(0, response.AlreadyEnded);

        // Verify EndedAt was set on saved models
        Assert.True(savedModels.ContainsKey($"rel:{relId1}"));
        Assert.NotNull(savedModels[$"rel:{relId1}"].EndedAt);
        Assert.True(savedModels.ContainsKey($"rel:{relId2}"));
        Assert.NotNull(savedModels[$"rel:{relId2}"].EndedAt);
    }

    [Fact]
    public async Task CleanupByEntityAsync_AlreadyEndedRelationships_CountsCorrectly()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var activeRelId = Guid.NewGuid();
        var endedRelId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { activeRelId, endedRelId });

        var activeModel = CreateTestRelationshipModel(activeRelId);
        activeModel.Entity1Id = entityId;
        activeModel.Entity1Type = EntityType.Character;
        activeModel.EndedAt = null;

        var endedModel = CreateTestRelationshipModel(endedRelId);
        endedModel.Entity1Id = entityId;
        endedModel.Entity1Type = EntityType.Character;
        endedModel.EndedAt = DateTimeOffset.UtcNow.AddDays(-1);

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{activeRelId}"] = activeModel,
            [$"rel:{endedRelId}"] = endedModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        // Re-fetch for active relationship under lock
        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{activeRelId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeModel);

        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new CleanupByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.CleanupByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.RelationshipsEnded);
        Assert.Equal(1, response.AlreadyEnded);
    }

    [Fact]
    public async Task CleanupByEntityAsync_PublishesDeletedEventPerRelationship()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var relId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { relId });

        var model = CreateTestRelationshipModel(relId);
        model.Entity1Id = entityId;
        model.Entity1Type = EntityType.Character;
        model.EndedAt = null;

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{relId}"] = model
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Capture the published event
        RelationshipDeletedEvent? capturedEvent = null;
        string? capturedTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<RelationshipDeletedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipDeletedEvent, PublishOptions?, Guid?, CancellationToken>((t, e, _, _, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        var request = new CleanupByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.CleanupByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.RelationshipsEnded);

        // Assert event was published with correct content
        Assert.NotNull(capturedEvent);
        Assert.Equal("relationship.deleted", capturedTopic);
        Assert.Equal(relId, capturedEvent.RelationshipId);
        Assert.Equal("Entity deleted (cascade cleanup)", capturedEvent.DeletedReason);
    }

    [Fact]
    public async Task CleanupByEntityAsync_ClearsCompositeKeyForEndedRelationships()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var relId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { relId });

        var model = CreateTestRelationshipModel(relId);
        model.Entity1Id = entityId;
        model.Entity1Type = EntityType.Character;
        model.EndedAt = null;

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{relId}"] = model
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Track composite key deletion
        string? deletedKey = null;
        _mockStringStore
            .Setup(s => s.DeleteAsync(It.Is<string>(k => k.StartsWith("composite:")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) => deletedKey = k)
            .ReturnsAsync(true);

        var request = new CleanupByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.CleanupByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.RelationshipsEnded);

        // Verify composite key was deleted to allow future recreation
        Assert.NotNull(deletedKey);
        Assert.StartsWith("composite:", deletedKey);
    }

    #endregion

    #region CreateRelationship Edge Cases

    [Fact]
    public async Task CreateRelationshipAsync_SelfReferencing_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();

        var request = new CreateRelationshipRequest
        {
            Entity1Id = entityId,
            Entity1Type = EntityType.Character,
            Entity2Id = entityId,
            Entity2Type = EntityType.Character,
            RelationshipTypeId = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.CreateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateRelationshipAsync_DeprecatedType_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();
        var typeId = Guid.NewGuid();

        // Setup a deprecated relationship type
        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipTypeModel
            {
                RelationshipTypeId = typeId,
                Code = "DEPRECATED_TYPE",
                Name = "Deprecated",
                IsBidirectional = true,
                IsDeprecated = true,
                DeprecatedAt = DateTimeOffset.UtcNow.AddDays(-7),
                DeprecationReason = "Replaced by newer type",
                Depth = 0,
                CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-7)
            });

        var request = new CreateRelationshipRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.Character,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.Character,
            RelationshipTypeId = typeId,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.CreateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateRelationshipAsync_NonExistentType_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationshipTypeModel?)null);

        var request = new CreateRelationshipRequest
        {
            Entity1Id = Guid.NewGuid(),
            Entity1Type = EntityType.Character,
            Entity2Id = Guid.NewGuid(),
            Entity2Type = EntityType.Character,
            RelationshipTypeId = typeId,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.CreateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region ListRelationshipsByEntity Edge Cases

    [Fact]
    public async Task ListRelationshipsByEntityAsync_WithTypeFilter_FiltersCorrectly()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var friendTypeId = Guid.NewGuid();
        var enemyTypeId = Guid.NewGuid();
        var friendRelId = Guid.NewGuid();
        var enemyRelId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { friendRelId, enemyRelId });

        var friendModel = CreateTestRelationshipModel(friendRelId);
        friendModel.RelationshipTypeId = friendTypeId;
        var enemyModel = CreateTestRelationshipModel(enemyRelId);
        enemyModel.RelationshipTypeId = enemyTypeId;

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{friendRelId}"] = friendModel,
            [$"rel:{enemyRelId}"] = enemyModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new ListRelationshipsByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character,
            RelationshipTypeId = friendTypeId,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.ListRelationshipsByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(friendRelId, response.Relationships.First().RelationshipId);
    }

    [Fact]
    public async Task ListRelationshipsByEntityAsync_FiltersEndedByDefault()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var activeRelId = Guid.NewGuid();
        var endedRelId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { activeRelId, endedRelId });

        var activeModel = CreateTestRelationshipModel(activeRelId);
        activeModel.EndedAt = null;
        var endedModel = CreateTestRelationshipModel(endedRelId);
        endedModel.EndedAt = DateTimeOffset.UtcNow.AddDays(-1);

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{activeRelId}"] = activeModel,
            [$"rel:{endedRelId}"] = endedModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new ListRelationshipsByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character,
            IncludeEnded = false
        };

        // Act
        var (status, response) = await service.ListRelationshipsByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(activeRelId, response.Relationships.First().RelationshipId);
    }

    [Fact]
    public async Task ListRelationshipsByEntityAsync_WithOtherEntityTypeFilter_FiltersCorrectly()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var charPartnerId = Guid.NewGuid();
        var realmPartnerId = Guid.NewGuid();
        var charRelId = Guid.NewGuid();
        var realmRelId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Character}:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { charRelId, realmRelId });

        var charModel = CreateTestRelationshipModel(charRelId);
        charModel.Entity1Id = entityId;
        charModel.Entity1Type = EntityType.Character;
        charModel.Entity2Id = charPartnerId;
        charModel.Entity2Type = EntityType.Character;

        var realmModel = CreateTestRelationshipModel(realmRelId);
        realmModel.Entity1Id = entityId;
        realmModel.Entity1Type = EntityType.Character;
        realmModel.Entity2Id = realmPartnerId;
        realmModel.Entity2Type = EntityType.Realm;

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{charRelId}"] = charModel,
            [$"rel:{realmRelId}"] = realmModel
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        var request = new ListRelationshipsByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character,
            OtherEntityType = EntityType.Realm,
            IncludeEnded = true
        };

        // Act
        var (status, response) = await service.ListRelationshipsByEntityAsync(request);

        // Assert - Only the realm relationship is returned
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Relationships);
        Assert.Equal(realmRelId, response.Relationships.First().RelationshipId);
    }

    #endregion

    #region UpdateRelationship Edge Cases

    [Fact]
    public async Task UpdateRelationshipAsync_EndedRelationship_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestRelationshipModel(relationshipId);
        model.EndedAt = DateTimeOffset.UtcNow.AddDays(-1);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UpdateRelationshipRequest
        {
            RelationshipId = relationshipId,
            Metadata = new Dictionary<string, object> { { "test", true } }
        };

        // Act
        var (status, response) = await service.UpdateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region EndRelationship Edge Cases

    [Fact]
    public async Task EndRelationshipAsync_AlreadyEnded_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestRelationshipModel(relationshipId);
        model.EndedAt = DateTimeOffset.UtcNow.AddDays(-1);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new EndRelationshipRequest { RelationshipId = relationshipId };

        // Act
        var status = await service.EndRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void RelationshipPermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = RelationshipPermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
    }

    [Fact]
    public void RelationshipPermissionRegistration_BuildPermissionMatrix_ShouldBeValid()
    {
        PermissionMatrixValidator.ValidatePermissionMatrix(
            RelationshipPermissionRegistration.ServiceId,
            RelationshipPermissionRegistration.ServiceVersion,
            RelationshipPermissionRegistration.BuildPermissionMatrix());
    }

    [Fact]
    public void RelationshipPermissionRegistration_ServiceId_ShouldBeRelationship()
    {
        // Assert
        Assert.Equal("relationship", RelationshipPermissionRegistration.ServiceId);
    }

    #endregion

    #region Helper Methods

    private static RelationshipModel CreateTestRelationshipModel(Guid relationshipId)
    {
        return new RelationshipModel
        {
            RelationshipId = relationshipId,
            Entity1Id = Guid.NewGuid(),
            Entity1Type = EntityType.Character,
            Entity2Id = Guid.NewGuid(),
            Entity2Type = EntityType.Character,
            RelationshipTypeId = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private void SetupCreateRelationshipMocks(Guid entity1Id, Guid entity2Id, Guid relationshipTypeId, bool existingCompositeKey)
    {
        // Setup relationship type lookup (deprecation check per IMPLEMENTATION TENETS)
        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{relationshipTypeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipTypeModel
            {
                RelationshipTypeId = relationshipTypeId,
                Code = "TEST_TYPE",
                Name = "Test Type",
                IsBidirectional = true,
                IsDeprecated = false,
                Depth = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Setup composite key check using prefix pattern since key includes entity types
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("composite:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCompositeKey ? Guid.NewGuid().ToString() : null);

        // Setup entity index gets (for adding to index)
        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("entity-idx:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Setup type index get
        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("type-idx:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Setup all-relationships list
        _mockListStore
            .Setup(s => s.GetAsync("all-relationships", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
    }

    #endregion
}

/// <summary>
/// Tests for Location entity reference tracking in RelationshipService.
/// Validates that lib-resource references are registered/unregistered for Location entities.
/// </summary>
public class RelationshipLocationReferenceTests : ServiceTestBase<RelationshipServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<RelationshipModel>> _mockRelationshipStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockListStore;
    private readonly Mock<IStateStore<RelationshipTypeModel>> _mockRtModelStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<RelationshipService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<IRelationshipDataCache> _mockRelationshipCache;

    private const string STATE_STORE = "relationship-statestore";

    public RelationshipLocationReferenceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockRelationshipStore = new Mock<IStateStore<RelationshipModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<Guid>>>();
        _mockRtModelStore = new Mock<IStateStore<RelationshipTypeModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<RelationshipService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockRelationshipCache = new Mock<IRelationshipDataCache>();

        _mockStateStoreFactory.Setup(f => f.GetStore<RelationshipModel>(STATE_STORE)).Returns(_mockRelationshipStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE)).Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE)).Returns(_mockListStore.Object);

        _mockStateStoreFactory.Setup(f => f.GetStore<RelationshipTypeModel>("relationship-type-statestore")).Returns(_mockRtModelStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>("relationship-type-statestore")).Returns(new Mock<IStateStore<string>>().Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>("relationship-type-statestore")).Returns(new Mock<IStateStore<List<Guid>>>().Object);

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private RelationshipService CreateService()
    {
        return new RelationshipService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockLockProvider.Object,
            _mockEventConsumer.Object,
            _mockTelemetryProvider.Object,
            _mockResourceClient.Object,
            _mockRelationshipCache.Object);
    }

    private void SetupCreateRelationshipMocks(Guid entity1Id, Guid entity2Id, Guid relationshipTypeId)
    {
        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{relationshipTypeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipTypeModel
            {
                RelationshipTypeId = relationshipTypeId,
                Code = "LOCATION_BOND",
                Name = "Location Bond",
                IsBidirectional = true,
                IsDeprecated = false,
                Depth = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("composite:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("entity-idx:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("type-idx:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        _mockListStore
            .Setup(s => s.GetAsync("all-relationships", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
    }

    [Fact]
    public async Task CreateRelationshipAsync_RegistersLocationReference_WhenEntityIsLocation()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var typeId = Guid.NewGuid();

        SetupCreateRelationshipMocks(locationId, characterId, typeId);

        var request = new CreateRelationshipRequest
        {
            Entity1Id = locationId,
            Entity1Type = EntityType.Location,
            Entity2Id = characterId,
            Entity2Type = EntityType.Character,
            RelationshipTypeId = typeId,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        var (status, _) = await service.CreateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify location reference was registered via lib-resource
        _mockResourceClient.Verify(r => r.RegisterReferenceAsync(
            It.Is<RegisterReferenceRequest>(req =>
                req.ResourceType == "location" &&
                req.ResourceId == locationId &&
                req.SourceType == "relationship"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify character reference was also registered
        _mockResourceClient.Verify(r => r.RegisterReferenceAsync(
            It.Is<RegisterReferenceRequest>(req =>
                req.ResourceType == "character" &&
                req.ResourceId == characterId &&
                req.SourceType == "relationship"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EndRelationshipAsync_UnregistersLocationReference()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        var model = new RelationshipModel
        {
            RelationshipId = relationshipId,
            Entity1Id = locationId,
            Entity1Type = EntityType.Location,
            Entity2Id = characterId,
            Entity2Type = EntityType.Character,
            RelationshipTypeId = Guid.NewGuid(),
            EndedAt = null,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new EndRelationshipRequest
        {
            RelationshipId = relationshipId,
            Reason = "Location demolished"
        };

        // Act
        var status = await service.EndRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify location reference was unregistered
        _mockResourceClient.Verify(r => r.UnregisterReferenceAsync(
            It.Is<UnregisterReferenceRequest>(req =>
                req.ResourceType == "location" &&
                req.ResourceId == locationId &&
                req.SourceType == "relationship"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify character reference was also unregistered
        _mockResourceClient.Verify(r => r.UnregisterReferenceAsync(
            It.Is<UnregisterReferenceRequest>(req =>
                req.ResourceType == "character" &&
                req.ResourceId == characterId &&
                req.SourceType == "relationship"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CleanupByEntityAsync_EndsLocationRelationships()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();
        var otherEntityId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync($"entity-idx:{EntityType.Location}:{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { relationshipId });

        var model = new RelationshipModel
        {
            RelationshipId = relationshipId,
            Entity1Id = locationId,
            Entity1Type = EntityType.Location,
            Entity2Id = otherEntityId,
            Entity2Type = EntityType.Character,
            RelationshipTypeId = Guid.NewGuid(),
            EndedAt = null,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var bulkResults = new Dictionary<string, RelationshipModel>
        {
            [$"rel:{relationshipId}"] = model
        };

        _mockRelationshipStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        _mockRelationshipStore
            .Setup(s => s.GetAsync($"rel:{relationshipId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockRelationshipStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new CleanupByEntityRequest
        {
            EntityId = locationId,
            EntityType = EntityType.Location
        };

        // Act
        var (status, response) = await service.CleanupByEntityAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.RelationshipsEnded);

        // Verify location reference was unregistered
        _mockResourceClient.Verify(r => r.UnregisterReferenceAsync(
            It.Is<UnregisterReferenceRequest>(req =>
                req.ResourceType == "location" &&
                req.ResourceId == locationId &&
                req.SourceType == "relationship"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify character reference for the other entity was also unregistered
        _mockResourceClient.Verify(r => r.UnregisterReferenceAsync(
            It.Is<UnregisterReferenceRequest>(req =>
                req.ResourceType == "character" &&
                req.ResourceId == otherEntityId &&
                req.SourceType == "relationship"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class RelationshipConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new RelationshipServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);

        // Verify merged relationship-type config properties have correct defaults
        Assert.Equal(20, config.MaxHierarchyDepth);
        Assert.Equal(100, config.MaxMigrationErrorsToTrack);
    }
}
