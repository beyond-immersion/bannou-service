using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Relationship;
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
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<RelationshipService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    private const string STATE_STORE = "relationship-statestore";

    public RelationshipServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockRelationshipStore = new Mock<IStateStore<RelationshipModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<Guid>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<RelationshipService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<RelationshipModel>(STATE_STORE)).Returns(_mockRelationshipStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE)).Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE)).Returns(_mockListStore.Object);
    }

    private RelationshipService CreateService()
    {
        return new RelationshipService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    /// </summary>
    [Fact]
    public void RelationshipService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<RelationshipService>();

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
    public void RelationshipPermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Act
        var instanceId = Guid.NewGuid();
        var registrationEvent = RelationshipPermissionRegistration.CreateRegistrationEvent(instanceId, "test-app");

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("relationship", registrationEvent.ServiceName);
        Assert.Equal(instanceId, registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.Endpoints);
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
        Assert.Equal(100, config.SeedPageSize);
        Assert.Equal(20, config.MaxHierarchyDepth);
        Assert.Equal(100, config.MaxMigrationErrorsToTrack);
    }
}
