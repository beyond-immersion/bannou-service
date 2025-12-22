using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.RelationshipType;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace BeyondImmersion.BannouService.RelationshipType.Tests;

/// <summary>
/// Unit tests for RelationshipTypeService.
/// Tests hierarchical relationship type operations including hierarchy traversal and merge functionality.
/// </summary>
public class RelationshipTypeServiceTests : ServiceTestBase<RelationshipTypeServiceConfiguration>
{
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<RelationshipTypeService>> _mockLogger;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private readonly Mock<IRelationshipClient> _mockRelationshipClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public RelationshipTypeServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<RelationshipTypeService>>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockRelationshipClient = new Mock<IRelationshipClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();
    }

    private RelationshipTypeService CreateService()
    {
        return new RelationshipTypeService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockRelationshipClient.Object,
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
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            null!,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockRelationshipClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            _mockDaprClient.Object,
            null!,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockRelationshipClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            null!,
            _mockErrorEventEmitter.Object,
            _mockRelationshipClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullErrorEventEmitter_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            null!,
            _mockRelationshipClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockRelationshipClient.Object,
            null!));
    }

    #endregion

    #region GetRelationshipType Tests

    [Fact]
    public async Task GetRelationshipTypeAsync_ExistingType_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "FRIEND", "Friend");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetRelationshipTypeAsync(
            new GetRelationshipTypeRequest { RelationshipTypeId = typeId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(typeId, response.RelationshipTypeId);
        Assert.Equal("FRIEND", response.Code);
    }

    [Fact]
    public async Task GetRelationshipTypeAsync_NonExistentType_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<RelationshipTypeModel?>(null));

        // Act
        var (status, response) = await service.GetRelationshipTypeAsync(
            new GetRelationshipTypeRequest { RelationshipTypeId = typeId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetRelationshipTypeByCode Tests

    [Fact]
    public async Task GetRelationshipTypeByCodeAsync_ExistingCode_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var code = "FRIEND";

        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                "relationship-type-statestore",
                "code-index:FRIEND",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(typeId.ToString());

        var model = CreateTestRelationshipTypeModel(typeId, code, "Friend");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetRelationshipTypeByCodeAsync(
            new GetRelationshipTypeByCodeRequest { Code = code });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(code, response.Code);
    }

    [Fact]
    public async Task GetRelationshipTypeByCodeAsync_NonExistentCode_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                "relationship-type-statestore",
                "code-index:UNKNOWN",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<string?>(null));

        // Act
        var (status, response) = await service.GetRelationshipTypeByCodeAsync(
            new GetRelationshipTypeByCodeRequest { Code = "UNKNOWN" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region CreateRelationshipType Tests

    [Fact]
    public async Task CreateRelationshipTypeAsync_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var service = CreateService();
        SetupCreateRelationshipTypeMocks(codeExists: false);

        var request = new CreateRelationshipTypeRequest
        {
            Code = "ENEMY",
            Name = "Enemy",
            Description = "An adversarial relationship",
            IsBidirectional = true
        };

        // Act
        var (status, response) = await service.CreateRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        Assert.NotNull(response);
        Assert.Equal("ENEMY", response.Code);
        Assert.Equal("Enemy", response.Name);
    }

    [Fact]
    public async Task CreateRelationshipTypeAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        SetupCreateRelationshipTypeMocks(codeExists: true);

        var request = new CreateRelationshipTypeRequest
        {
            Code = "FRIEND",
            Name = "Friend"
        };

        // Act
        var (status, response) = await service.CreateRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateRelationshipTypeAsync_WithParent_InheritsBidirectionality()
    {
        // Arrange
        var service = CreateService();
        var parentId = Guid.NewGuid();
        var parentModel = CreateTestRelationshipTypeModel(parentId, "SOCIAL", "Social");
        parentModel.IsBidirectional = true;

        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                "relationship-type-statestore",
                It.Is<string>(k => k.StartsWith("code-index:")),
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<string?>(null));

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{parentId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        // Setup all-types list
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-type-statestore",
                "all-types",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup children index
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-type-statestore",
                $"children-idx:{parentId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var request = new CreateRelationshipTypeRequest
        {
            Code = "FRIEND",
            Name = "Friend",
            ParentTypeId = parentId
        };

        // Act
        var (status, response) = await service.CreateRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        Assert.NotNull(response);
        Assert.Equal(parentId, response.ParentTypeId);
    }

    [Fact]
    public async Task CreateRelationshipTypeAsync_PublishesCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        SetupCreateRelationshipTypeMocks(codeExists: false);

        var request = new CreateRelationshipTypeRequest
        {
            Code = "ALLY",
            Name = "Ally"
        };

        // Act
        var (status, _) = await service.CreateRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "relationship-type.created",
            It.IsAny<RelationshipTypeCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateRelationshipType Tests

    [Fact]
    public async Task UpdateRelationshipTypeAsync_ExistingType_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "FRIEND", "Friend");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UpdateRelationshipTypeRequest
        {
            RelationshipTypeId = typeId,
            Name = "Best Friend",
            Description = "A close friendship"
        };

        // Act
        var (status, response) = await service.UpdateRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Best Friend", response.Name);
    }

    [Fact]
    public async Task UpdateRelationshipTypeAsync_NonExistentType_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<RelationshipTypeModel?>(null));

        var request = new UpdateRelationshipTypeRequest
        {
            RelationshipTypeId = typeId,
            Name = "Updated Name"
        };

        // Act
        var (status, response) = await service.UpdateRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateRelationshipTypeAsync_PublishesUpdatedEvent()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "FRIEND", "Friend");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UpdateRelationshipTypeRequest
        {
            RelationshipTypeId = typeId,
            Description = "Updated description"
        };

        // Act
        var (status, _) = await service.UpdateRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "relationship-type.updated",
            It.IsAny<RelationshipTypeUpdatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteRelationshipType Tests

    [Fact]
    public async Task DeleteRelationshipTypeAsync_ExistingType_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "TEST", "Test Type");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Setup all-types list
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-type-statestore",
                "all-types",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { typeId.ToString() });

        // Setup children index (no children)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-type-statestore",
                $"children-idx:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<List<string>?>(null));

        // Act
        var (status, response) = await service.DeleteRelationshipTypeAsync(
            new DeleteRelationshipTypeRequest { RelationshipTypeId = typeId });

        // Assert
        Assert.Equal(StatusCodes.NoContent, status);
    }

    [Fact]
    public async Task DeleteRelationshipTypeAsync_NonExistentType_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<RelationshipTypeModel?>(null));

        // Act
        var (status, response) = await service.DeleteRelationshipTypeAsync(
            new DeleteRelationshipTypeRequest { RelationshipTypeId = typeId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ListRelationshipTypes Tests

    [Fact]
    public async Task ListRelationshipTypesAsync_NoTypes_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-type-statestore",
                "all-types",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<List<string>?>(null));

        // Act
        var (status, response) = await service.ListRelationshipTypesAsync(new ListRelationshipTypesRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Types);
    }

    [Fact]
    public async Task ListRelationshipTypesAsync_WithTypes_ReturnsList()
    {
        // Arrange
        var service = CreateService();
        var typeIds = new List<string> { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-type-statestore",
                "all-types",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(typeIds);

        var bulkResults = typeIds.Select((id, idx) => new BulkStateItem(
            $"type:{id}",
            JsonSerializer.Serialize(CreateTestRelationshipTypeModel(Guid.Parse(id), $"TYPE{idx}", $"Type {idx}")),
            "etag")).ToList();

        _mockDaprClient
            .Setup(d => d.GetBulkStateAsync(
                "relationship-type-statestore",
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<int?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        // Act
        var (status, response) = await service.ListRelationshipTypesAsync(
            new ListRelationshipTypesRequest { IncludeDeprecated = true });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Types.Count);
    }

    #endregion

    #region Hierarchy Tests

    [Fact]
    public async Task GetChildRelationshipTypesAsync_NoChildren_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var parentId = Guid.NewGuid();
        var parentModel = CreateTestRelationshipTypeModel(parentId, "SOCIAL", "Social");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{parentId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-type-statestore",
                $"children-idx:{parentId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<List<string>?>(null));

        // Act
        var (status, response) = await service.GetChildRelationshipTypesAsync(
            new GetChildRelationshipTypesRequest { ParentTypeId = parentId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Types);
    }

    [Fact]
    public async Task GetAncestorsAsync_RootType_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "ROOT", "Root Type");
        model.ParentTypeId = null; // No parent = root

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetAncestorsAsync(
            new GetAncestorsRequest { TypeId = typeId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Types);
    }

    [Fact]
    public async Task MatchesHierarchyAsync_SameType_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "TYPE", "Type");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new MatchesHierarchyRequest
        {
            TypeId = typeId,
            AncestorTypeId = typeId  // Same type = matches
        };

        // Act
        var (status, response) = await service.MatchesHierarchyAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Matches);
    }

    #endregion

    #region Deprecation Tests

    [Fact]
    public async Task DeprecateRelationshipTypeAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "OLD", "Old Type");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new DeprecateRelationshipTypeRequest
        {
            RelationshipTypeId = typeId,
            Reason = "No longer used"
        };

        // Act
        var (status, response) = await service.DeprecateRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
    }

    [Fact]
    public async Task UndeprecateRelationshipTypeAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "OLD", "Old Type");
        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow;

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RelationshipTypeModel>(
                "relationship-type-statestore",
                $"type:{typeId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UndeprecateRelationshipTypeRequest { RelationshipTypeId = typeId };

        // Act
        var (status, response) = await service.UndeprecateRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsDeprecated);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void RelationshipTypePermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = RelationshipTypePermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
    }

    [Fact]
    public void RelationshipTypePermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Act
        var registrationEvent = RelationshipTypePermissionRegistration.CreateRegistrationEvent();

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("relationship-type", registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.EventId);
        Assert.NotNull(registrationEvent.Endpoints);
    }

    [Fact]
    public void RelationshipTypePermissionRegistration_ServiceId_ShouldBeRelationshipType()
    {
        // Assert
        Assert.Equal("relationship-type", RelationshipTypePermissionRegistration.ServiceId);
    }

    #endregion

    #region Helper Methods

    private static RelationshipTypeModel CreateTestRelationshipTypeModel(Guid typeId, string code, string name)
    {
        return new RelationshipTypeModel
        {
            RelationshipTypeId = typeId.ToString(),
            Code = code,
            Name = name,
            Description = "Test relationship type description",
            IsBidirectional = true,
            Depth = 0,
            IsDeprecated = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private void SetupCreateRelationshipTypeMocks(bool codeExists)
    {
        // Setup code existence check
        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                "relationship-type-statestore",
                It.Is<string>(k => k.StartsWith("code-index:")),
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<string?>(codeExists ? Guid.NewGuid().ToString() : null));

        // Setup all-types list
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "relationship-type-statestore",
                "all-types",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
    }

    #endregion
}

public class RelationshipTypeConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new RelationshipTypeServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }
}
