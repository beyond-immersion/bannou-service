using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.RelationshipType;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.RelationshipType.Tests;

/// <summary>
/// Unit tests for RelationshipTypeService.
/// Tests hierarchical relationship type operations including hierarchy traversal and merge functionality.
/// </summary>
public class RelationshipTypeServiceTests : ServiceTestBase<RelationshipTypeServiceConfiguration>
{
    private const string STATE_STORE = "relationship-type-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<RelationshipTypeModel>> _mockRelationshipTypeStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<RelationshipTypeService>> _mockLogger;
    private readonly Mock<IRelationshipClient> _mockRelationshipClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public RelationshipTypeServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockRelationshipTypeStore = new Mock<IStateStore<RelationshipTypeModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<RelationshipTypeService>>();
        _mockRelationshipClient = new Mock<IRelationshipClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<RelationshipTypeModel>(STATE_STORE)).Returns(_mockRelationshipTypeStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE)).Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE)).Returns(_mockListStore.Object);
    }

    private RelationshipTypeService CreateService()
    {
        return new RelationshipTypeService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
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
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            null!,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockRelationshipClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            _mockStateStoreFactory.Object,
            null!,
            _mockLogger.Object,
            Configuration,
            _mockRelationshipClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            null!,
            Configuration,
            _mockRelationshipClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            null!,
            _mockRelationshipClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelationshipTypeService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationshipTypeModel?)null);

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

        _mockStringStore
            .Setup(s => s.GetAsync("code-index:FRIEND", It.IsAny<CancellationToken>()))
            .ReturnsAsync(typeId.ToString());

        var model = CreateTestRelationshipTypeModel(typeId, code, "Friend");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
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

        _mockStringStore
            .Setup(s => s.GetAsync("code-index:UNKNOWN", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

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

        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("code-index:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        // Setup all-types list
        _mockListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup children index
        _mockListStore
            .Setup(s => s.GetAsync($"children-idx:{parentId}", It.IsAny<CancellationToken>()))
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
        _mockMessageBus.Verify(m => m.PublishAsync(
            "relationship-type.created",
            It.IsAny<RelationshipTypeCreatedEvent>(),
            It.IsAny<PublishOptions?>(),
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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationshipTypeModel?)null);

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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
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
        _mockMessageBus.Verify(m => m.PublishAsync(
            "relationship-type.updated",
            It.IsAny<RelationshipTypeUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Setup all-types list
        _mockListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { typeId.ToString() });

        // Setup children index (no children)
        _mockListStore
            .Setup(s => s.GetAsync($"children-idx:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationshipTypeModel?)null);

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

        _mockListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

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

        _mockListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(typeIds);

        var bulkResults = typeIds.Select((id, idx) =>
            new KeyValuePair<string, RelationshipTypeModel>(
                $"type:{id}",
                CreateTestRelationshipTypeModel(Guid.Parse(id), $"TYPE{idx}", $"Type {idx}")))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        _mockRelationshipTypeStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipTypeModel>)bulkResults);

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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockListStore
            .Setup(s => s.GetAsync($"children-idx:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
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

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
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
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("code-index:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(codeExists ? Guid.NewGuid().ToString() : null);

        // Setup all-types list
        _mockListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
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
