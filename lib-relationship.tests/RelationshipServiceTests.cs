using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using Dapr.Client;
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
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<RelationshipService>> _mockLogger;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public RelationshipServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<RelationshipService>>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockEventConsumer = new Mock<IEventConsumer>();
    }

    private RelationshipService CreateService()
    {
        return new RelationshipService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipService(
            null!,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipService(
            _mockDaprClient.Object,
            null!,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            null!,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullErrorEventEmitter_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            null!,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            null!));
    }

    #endregion

    #region GetRelationship Tests

    [Fact]
    public async Task GetRelationshipAsync_ExistingRelationship_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();
        var model = CreateTestRelationshipModel(relationshipId);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipModel>(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipModel>(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<RelationshipModel?>(null));

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

        var request = new CreateRelationshipRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.CHARACTER,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.CHARACTER,
            RelationshipTypeId = relationshipTypeId,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.CreateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        Assert.NotNull(response);
        Assert.Equal(entity1Id, response.Entity1Id);
        Assert.Equal(entity2Id, response.Entity2Id);
        Assert.Equal(relationshipTypeId, response.RelationshipTypeId);
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
            Entity1Type = EntityType.CHARACTER,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.CHARACTER,
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

        var request = new CreateRelationshipRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.CHARACTER,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.NPC,
            RelationshipTypeId = relationshipTypeId,
            StartedAt = DateTimeOffset.UtcNow,
            Metadata = metadata
        };

        // Act
        var (status, response) = await service.CreateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Metadata);
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

        var request = new CreateRelationshipRequest
        {
            Entity1Id = entity1Id,
            Entity1Type = EntityType.CHARACTER,
            Entity2Id = entity2Id,
            Entity2Type = EntityType.CHARACTER,
            RelationshipTypeId = relationshipTypeId,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        var (status, _) = await service.CreateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "relationship.created",
            It.IsAny<RelationshipCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipModel>(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UpdateRelationshipRequest
        {
            RelationshipId = relationshipId,
            Metadata = new Dictionary<string, object> { { "updated", true } }
        };

        // Act
        var (status, response) = await service.UpdateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(relationshipId, response.RelationshipId);
    }

    [Fact]
    public async Task UpdateRelationshipAsync_NonExistentRelationship_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipModel>(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<RelationshipModel?>(null));

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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipModel>(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UpdateRelationshipRequest
        {
            RelationshipId = relationshipId,
            Metadata = new Dictionary<string, object> { { "updated", true } }
        };

        // Act
        var (status, _) = await service.UpdateRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "relationship.updated",
            It.IsAny<RelationshipUpdatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipModel>(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new EndRelationshipRequest
        {
            RelationshipId = relationshipId,
            Reason = "Friendship ended"
        };

        // Act
        var (status, response) = await service.EndRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task EndRelationshipAsync_NonExistentRelationship_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var relationshipId = Guid.NewGuid();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipModel>(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<RelationshipModel?>(null));

        var request = new EndRelationshipRequest { RelationshipId = relationshipId };

        // Act
        var (status, response) = await service.EndRelationshipAsync(request);

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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipModel>(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<RelationshipModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, RelationshipModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedModel = data);

        var request = new EndRelationshipRequest { RelationshipId = relationshipId };

        // Act
        var (status, _) = await service.EndRelationshipAsync(request);

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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipModel>(
                "relationship-statestore",
                $"rel:{relationshipId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new EndRelationshipRequest
        {
            RelationshipId = relationshipId,
            Reason = "Test reason"
        };

        // Act
        var (status, _) = await service.EndRelationshipAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "relationship.deleted",
            It.IsAny<RelationshipDeletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ListRelationshipsByEntity Tests

    [Fact]
    public async Task ListRelationshipsByEntityAsync_NoRelationships_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-statestore",
                $"entity-idx:CHARACTER:{entityId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<List<string>?>(null));

        var request = new ListRelationshipsByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.CHARACTER
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
        var relationshipIds = new List<string> { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-statestore",
                $"entity-idx:CHARACTER:{entityId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(relationshipIds);

        var bulkResults = relationshipIds.Select(id => new BulkStateItem(
            $"rel:{id}",
            BannouJson.Serialize(CreateTestRelationshipModel(Guid.Parse(id))),
            "etag")).ToList();

        _mockDaprClient
            .Setup(d => d.GetBulkStateAsync(
                "relationship-statestore",
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<int?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        var request = new ListRelationshipsByEntityRequest
        {
            EntityId = entityId,
            EntityType = EntityType.CHARACTER,
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
        var registrationEvent = RelationshipPermissionRegistration.CreateRegistrationEvent();

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("relationship", registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.EventId);
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
            RelationshipId = relationshipId.ToString(),
            Entity1Id = Guid.NewGuid().ToString(),
            Entity1Type = "CHARACTER",
            Entity2Id = Guid.NewGuid().ToString(),
            Entity2Type = "CHARACTER",
            RelationshipTypeId = Guid.NewGuid().ToString(),
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private void SetupCreateRelationshipMocks(Guid entity1Id, Guid entity2Id, Guid relationshipTypeId, bool existingCompositeKey)
    {
        // Setup composite key check using prefix pattern since key includes entity types
        // Key format: composite:TYPE:ID:TYPE:ID:relationshipTypeId (entities sorted)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                "relationship-statestore",
                It.Is<string>(k => k.StartsWith("composite:")),
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<string?>(existingCompositeKey ? Guid.NewGuid().ToString() : null));

        // Setup entity index gets (for adding to index)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-statestore",
                It.Is<string>(k => k.StartsWith("entity-idx:")),
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup type index get
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-statestore",
                It.Is<string>(k => k.StartsWith("type-idx:")),
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup all-relationships list
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-statestore",
                "all-relationships",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
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
    }
}
