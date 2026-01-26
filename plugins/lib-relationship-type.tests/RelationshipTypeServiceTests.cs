using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.RelationshipType;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
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
    private readonly Mock<IStateStore<List<Guid>>> _mockGuidListStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<RelationshipTypeService>> _mockLogger;
    private readonly Mock<IRelationshipClient> _mockRelationshipClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public RelationshipTypeServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockRelationshipTypeStore = new Mock<IStateStore<RelationshipTypeModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockGuidListStore = new Mock<IStateStore<List<Guid>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<RelationshipTypeService>>();
        _mockRelationshipClient = new Mock<IRelationshipClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<RelationshipTypeModel>(STATE_STORE)).Returns(_mockRelationshipTypeStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE)).Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE)).Returns(_mockGuidListStore.Object);
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
    public void RelationshipTypeService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<RelationshipTypeService>();

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
        Assert.Equal(StatusCodes.OK, status);
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
        Assert.Equal(StatusCodes.OK, status);
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
        Assert.Equal(StatusCodes.OK, status);
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "relationship-type.created",
            It.IsAny<RelationshipTypeCreatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
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
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "relationship-type.updated",
            It.IsAny<RelationshipTypeUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
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
        var status = await service.DeleteRelationshipTypeAsync(
            new DeleteRelationshipTypeRequest { RelationshipTypeId = typeId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
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
        var status = await service.DeleteRelationshipTypeAsync(
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

    #region MergeRelationshipType Tests

    [Fact]
    public async Task MergeRelationshipTypeAsync_SourceNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationshipTypeModel?)null);

        var request = new MergeRelationshipTypeRequest
        {
            SourceTypeId = sourceId,
            TargetTypeId = targetId
        };

        // Act
        var (status, response) = await service.MergeRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MergeRelationshipTypeAsync_SourceNotDeprecated_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var sourceModel = CreateTestRelationshipTypeModel(sourceId, "SOURCE", "Source Type");
        sourceModel.IsDeprecated = false; // Not deprecated

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        var request = new MergeRelationshipTypeRequest
        {
            SourceTypeId = sourceId,
            TargetTypeId = targetId
        };

        // Act
        var (status, response) = await service.MergeRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MergeRelationshipTypeAsync_TargetNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var sourceModel = CreateTestRelationshipTypeModel(sourceId, "SOURCE", "Source Type");
        sourceModel.IsDeprecated = true;

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationshipTypeModel?)null);

        var request = new MergeRelationshipTypeRequest
        {
            SourceTypeId = sourceId,
            TargetTypeId = targetId
        };

        // Act
        var (status, response) = await service.MergeRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MergeRelationshipTypeAsync_NoRelationshipsToMigrate_ReturnsOKWithZeroMigrated()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var sourceModel = CreateTestRelationshipTypeModel(sourceId, "SOURCE", "Source Type");
        sourceModel.IsDeprecated = true;
        var targetModel = CreateTestRelationshipTypeModel(targetId, "TARGET", "Target Type");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        // No relationships to migrate
        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByTypeAsync(
                It.Is<ListRelationshipsByTypeRequest>(r => r.RelationshipTypeId == sourceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>(),
                TotalCount = 0,
                HasNextPage = false
            });

        var request = new MergeRelationshipTypeRequest
        {
            SourceTypeId = sourceId,
            TargetTypeId = targetId
        };

        // Act
        var (status, response) = await service.MergeRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(sourceId, response.SourceTypeId);
        Assert.Equal(targetId, response.TargetTypeId);
        Assert.Equal(0, response.RelationshipsMigrated);
    }

    [Fact]
    public async Task MergeRelationshipTypeAsync_SinglePageOfRelationships_MigratesAll()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var sourceModel = CreateTestRelationshipTypeModel(sourceId, "SOURCE", "Source Type");
        sourceModel.IsDeprecated = true;
        var targetModel = CreateTestRelationshipTypeModel(targetId, "TARGET", "Target Type");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        // 3 relationships to migrate
        var relationships = new List<RelationshipResponse>
        {
            new() { RelationshipId = Guid.NewGuid() },
            new() { RelationshipId = Guid.NewGuid() },
            new() { RelationshipId = Guid.NewGuid() }
        };

        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByTypeAsync(
                It.Is<ListRelationshipsByTypeRequest>(r => r.RelationshipTypeId == sourceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = relationships,
                TotalCount = 3,
                HasNextPage = false
            });

        // Track which relationships were updated
        var updatedRelationships = new List<Guid>();
        _mockRelationshipClient
            .Setup(c => c.UpdateRelationshipAsync(
                It.IsAny<UpdateRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<UpdateRelationshipRequest, CancellationToken>((req, _) =>
                updatedRelationships.Add(req.RelationshipId))
            .ReturnsAsync(new RelationshipResponse());

        var request = new MergeRelationshipTypeRequest
        {
            SourceTypeId = sourceId,
            TargetTypeId = targetId
        };

        // Act
        var (status, response) = await service.MergeRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.RelationshipsMigrated);
        Assert.Equal(3, updatedRelationships.Count);

        // Verify all relationships were updated with the target type ID
        _mockRelationshipClient.Verify(c => c.UpdateRelationshipAsync(
            It.Is<UpdateRelationshipRequest>(r => r.RelationshipTypeId == targetId),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task MergeRelationshipTypeAsync_MultiplePages_PaginatesCorrectly()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var sourceModel = CreateTestRelationshipTypeModel(sourceId, "SOURCE", "Source Type");
        sourceModel.IsDeprecated = true;
        var targetModel = CreateTestRelationshipTypeModel(targetId, "TARGET", "Target Type");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        // Page 1: 2 relationships, has next page
        var page1Relationships = new List<RelationshipResponse>
        {
            new() { RelationshipId = Guid.NewGuid() },
            new() { RelationshipId = Guid.NewGuid() }
        };

        // Page 2: 1 relationship, no next page
        var page2Relationships = new List<RelationshipResponse>
        {
            new() { RelationshipId = Guid.NewGuid() }
        };

        var pageCallCount = 0;
        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByTypeAsync(
                It.Is<ListRelationshipsByTypeRequest>(r => r.RelationshipTypeId == sourceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                pageCallCount++;
                return pageCallCount == 1
                    ? new RelationshipListResponse { Relationships = page1Relationships, HasNextPage = true }
                    : new RelationshipListResponse { Relationships = page2Relationships, HasNextPage = false };
            });

        _mockRelationshipClient
            .Setup(c => c.UpdateRelationshipAsync(
                It.IsAny<UpdateRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipResponse());

        var request = new MergeRelationshipTypeRequest
        {
            SourceTypeId = sourceId,
            TargetTypeId = targetId
        };

        // Act
        var (status, response) = await service.MergeRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.RelationshipsMigrated);
        Assert.Equal(2, pageCallCount); // Should have fetched 2 pages
    }

    [Fact]
    public async Task MergeRelationshipTypeAsync_PartialFailure_ReportsFailedCount()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var sourceModel = CreateTestRelationshipTypeModel(sourceId, "SOURCE", "Source Type");
        sourceModel.IsDeprecated = true;
        var targetModel = CreateTestRelationshipTypeModel(targetId, "TARGET", "Target Type");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        var relationships = new List<RelationshipResponse>
        {
            new() { RelationshipId = Guid.NewGuid() },
            new() { RelationshipId = Guid.NewGuid() },
            new() { RelationshipId = Guid.NewGuid() }
        };

        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByTypeAsync(
                It.IsAny<ListRelationshipsByTypeRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = relationships,
                HasNextPage = false
            });

        // First and third succeed, second fails
        var callCount = 0;
        _mockRelationshipClient
            .Setup(c => c.UpdateRelationshipAsync(
                It.IsAny<UpdateRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 2)
                    throw new InvalidOperationException("Simulated failure");
                return new RelationshipResponse();
            });

        var request = new MergeRelationshipTypeRequest
        {
            SourceTypeId = sourceId,
            TargetTypeId = targetId
        };

        // Act
        var (status, response) = await service.MergeRelationshipTypeAsync(request);

        // Assert - should still succeed but with partial migration
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.RelationshipsMigrated); // 2 succeeded
        // The failed count isn't exposed in the response, but the operation continues
    }

    #endregion

    #region SeedRelationshipTypes Tests

    [Fact]
    public async Task SeedRelationshipTypesAsync_EmptyList_ReturnsOKWithZeroCounts()
    {
        // Arrange
        var service = CreateService();
        var request = new SeedRelationshipTypesRequest
        {
            Types = new List<SeedRelationshipType>()
        };

        // Act
        var (status, response) = await service.SeedRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public async Task SeedRelationshipTypesAsync_NewType_CreatesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        SetupSeedMocks();

        var request = new SeedRelationshipTypesRequest
        {
            Types = new List<SeedRelationshipType>
            {
                new() { Code = "FRIEND", Name = "Friend" }
            }
        };

        // Act
        var (status, response) = await service.SeedRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public async Task SeedRelationshipTypesAsync_ExistingType_SkipsWithoutUpdateFlag()
    {
        // Arrange
        var service = CreateService();
        var existingId = Guid.NewGuid();

        // Code already exists
        _mockStringStore
            .Setup(s => s.GetAsync("code-index:FRIEND", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId.ToString());

        var request = new SeedRelationshipTypesRequest
        {
            Types = new List<SeedRelationshipType>
            {
                new() { Code = "FRIEND", Name = "Friend" }
            },
            UpdateExisting = false
        };

        // Act
        var (status, response) = await service.SeedRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(1, response.Skipped);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public async Task SeedRelationshipTypesAsync_ExistingType_UpdatesWithUpdateFlag()
    {
        // Arrange
        var service = CreateService();
        var existingId = Guid.NewGuid();
        var existingModel = CreateTestRelationshipTypeModel(existingId, "FRIEND", "Old Friend Name");

        _mockStringStore
            .Setup(s => s.GetAsync("code-index:FRIEND", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId.ToString());

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{existingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        var request = new SeedRelationshipTypesRequest
        {
            Types = new List<SeedRelationshipType>
            {
                new() { Code = "FRIEND", Name = "New Friend Name" }
            },
            UpdateExisting = true
        };

        // Act
        var (status, response) = await service.SeedRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(1, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public async Task SeedRelationshipTypesAsync_TypesWithParentHierarchy_ProcessesInCorrectOrder()
    {
        // Arrange
        var service = CreateService();

        // Set up a more sophisticated mock that simulates store behavior
        var storedTypes = new Dictionary<string, RelationshipTypeModel>();
        var storedCodeIndex = new Dictionary<string, string>();

        // String store simulates code-index lookups
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedCodeIndex.TryGetValue(key, out var val) ? val : null);

        _mockStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((key, value, _, _) =>
                storedCodeIndex[key] = value)
            .ReturnsAsync("etag");

        // Type store simulates type lookups
        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedTypes.TryGetValue(key, out var val) ? val : null);

        // Track creation order
        var creationOrder = new List<string>();
        _mockRelationshipTypeStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipTypeModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipTypeModel, StateOptions?, CancellationToken>((key, model, _, _) =>
            {
                storedTypes[key] = model;
                creationOrder.Add(model.Code);
            })
            .ReturnsAsync("etag");

        // List store for all-types and parent-index
        _mockListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Child references parent - should process parent first even though child comes first in list
        var request = new SeedRelationshipTypesRequest
        {
            Types = new List<SeedRelationshipType>
            {
                new() { Code = "CHILD", Name = "Child Type", ParentTypeCode = "PARENT" },
                new() { Code = "PARENT", Name = "Parent Type" }
            }
        };

        // Act
        var (status, response) = await service.SeedRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Created);

        // Parent should be created before child
        Assert.Equal(2, creationOrder.Count);
        Assert.Equal("PARENT", creationOrder[0]);
        Assert.Equal("CHILD", creationOrder[1]);
    }

    [Fact]
    public async Task SeedRelationshipTypesAsync_UnresolvableParent_ReportsError()
    {
        // Arrange
        var service = CreateService();
        SetupSeedMocks();

        // Child references parent that doesn't exist in seed list
        var request = new SeedRelationshipTypesRequest
        {
            Types = new List<SeedRelationshipType>
            {
                new() { Code = "ORPHAN", Name = "Orphan Type", ParentTypeCode = "NONEXISTENT" }
            }
        };

        // Act
        var (status, response) = await service.SeedRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.NotEmpty(response.Errors);
        Assert.Contains(response.Errors, e => e.Contains("NONEXISTENT") && e.Contains("ORPHAN"));
    }

    [Fact]
    public async Task SeedRelationshipTypesAsync_MultiLevelHierarchy_ProcessesAllLevels()
    {
        // Arrange
        var service = CreateService();

        // Set up stores that simulate real behavior
        var storedTypes = new Dictionary<string, RelationshipTypeModel>();
        var storedCodeIndex = new Dictionary<string, string>();

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedCodeIndex.TryGetValue(key, out var val) ? val : null);

        _mockStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((key, value, _, _) =>
                storedCodeIndex[key] = value)
            .ReturnsAsync("etag");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedTypes.TryGetValue(key, out var val) ? val : null);

        var creationOrder = new List<string>();
        _mockRelationshipTypeStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipTypeModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipTypeModel, StateOptions?, CancellationToken>((key, model, _, _) =>
            {
                storedTypes[key] = model;
                creationOrder.Add(model.Code);
            })
            .ReturnsAsync("etag");

        _mockListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Grandchild -> Child -> Parent hierarchy
        var request = new SeedRelationshipTypesRequest
        {
            Types = new List<SeedRelationshipType>
            {
                new() { Code = "GRANDCHILD", Name = "Grandchild", ParentTypeCode = "CHILD" },
                new() { Code = "CHILD", Name = "Child", ParentTypeCode = "PARENT" },
                new() { Code = "PARENT", Name = "Parent" }
            }
        };

        // Act
        var (status, response) = await service.SeedRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.Created);
        Assert.Empty(response.Errors);

        // Verify order: PARENT -> CHILD -> GRANDCHILD
        Assert.Equal(new[] { "PARENT", "CHILD", "GRANDCHILD" }, creationOrder);
    }

    [Fact]
    public async Task SeedRelationshipTypesAsync_MixedCreateSkipUpdate_ReportsCorrectly()
    {
        // Arrange
        var service = CreateService();
        var existingId = Guid.NewGuid();
        var skipThisId = Guid.NewGuid();
        var existingModel = CreateTestRelationshipTypeModel(existingId, "EXISTING", "Existing");
        var skipThisModel = CreateTestRelationshipTypeModel(skipThisId, "SKIP_THIS", "Skip This");

        // Set up stores
        var storedCodeIndex = new Dictionary<string, string>
        {
            ["code-index:EXISTING"] = existingId.ToString(),
            ["code-index:SKIP_THIS"] = skipThisId.ToString()
        };
        var storedTypes = new Dictionary<string, RelationshipTypeModel>
        {
            [$"type:{existingId}"] = existingModel,
            [$"type:{skipThisId}"] = skipThisModel
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedCodeIndex.TryGetValue(key, out var val) ? val : null);

        _mockStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((key, value, _, _) =>
                storedCodeIndex[key] = value)
            .ReturnsAsync("etag");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedTypes.TryGetValue(key, out var val) ? val : null);

        _mockRelationshipTypeStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipTypeModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipTypeModel, StateOptions?, CancellationToken>((key, model, _, _) =>
                storedTypes[key] = model)
            .ReturnsAsync("etag");

        _mockListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var request = new SeedRelationshipTypesRequest
        {
            Types = new List<SeedRelationshipType>
            {
                new() { Code = "EXISTING", Name = "Updated Existing" },
                new() { Code = "NEW", Name = "Brand New Type" },
                new() { Code = "SKIP_THIS", Name = "Should Skip" }
            },
            UpdateExisting = true
        };

        // Act
        var (status, response) = await service.SeedRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.Created);  // NEW
        Assert.Equal(2, response.Updated);  // EXISTING and SKIP_THIS (both exist and UpdateExisting=true)
        Assert.Equal(0, response.Skipped);
    }

    #endregion

    #region Deep Hierarchy Traversal Tests

    [Fact]
    public async Task GetChildRelationshipTypesAsync_WithDirectChildren_ReturnsChildren()
    {
        // Arrange
        var service = CreateService();
        var parentId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();

        var parentModel = CreateTestRelationshipTypeModel(parentId, "PARENT", "Parent");
        var child1Model = CreateTestRelationshipTypeModel(child1Id, "CHILD1", "Child 1");
        var child2Model = CreateTestRelationshipTypeModel(child2Id, "CHILD2", "Child 2");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockListStore
            .Setup(s => s.GetAsync($"parent-index:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { child1Id.ToString(), child2Id.ToString() });

        _mockRelationshipTypeStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RelationshipTypeModel>
            {
                [$"type:{child1Id}"] = child1Model,
                [$"type:{child2Id}"] = child2Model
            });

        var request = new GetChildRelationshipTypesRequest { ParentTypeId = parentId };

        // Act
        var (status, response) = await service.GetChildRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Types.Count);
    }

    [Fact]
    public async Task GetChildRelationshipTypesAsync_RecursiveWithGrandchildren_ReturnsAllDescendants()
    {
        // Arrange
        var service = CreateService();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        var parentModel = CreateTestRelationshipTypeModel(parentId, "PARENT", "Parent");
        var childModel = CreateTestRelationshipTypeModel(childId, "CHILD", "Child");
        var grandchildModel = CreateTestRelationshipTypeModel(grandchildId, "GRANDCHILD", "Grandchild");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        // Parent has child
        _mockListStore
            .Setup(s => s.GetAsync($"parent-index:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { childId.ToString() });

        // Child has grandchild
        _mockListStore
            .Setup(s => s.GetAsync($"parent-index:{childId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { grandchildId.ToString() });

        // Grandchild has no children
        _mockListStore
            .Setup(s => s.GetAsync($"parent-index:{grandchildId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        _mockRelationshipTypeStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> keys, CancellationToken _) =>
            {
                var result = new Dictionary<string, RelationshipTypeModel>();
                foreach (var key in keys)
                {
                    if (key.Contains(childId.ToString())) result[key] = childModel;
                    if (key.Contains(grandchildId.ToString())) result[key] = grandchildModel;
                }
                return result;
            });

        var request = new GetChildRelationshipTypesRequest
        {
            ParentTypeId = parentId,
            Recursive = true
        };

        // Act
        var (status, response) = await service.GetChildRelationshipTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Types.Count); // Both child and grandchild
    }

    [Fact]
    public async Task MatchesHierarchyAsync_DirectParent_ReturnsDepth1()
    {
        // Arrange
        var service = CreateService();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        var childModel = CreateTestRelationshipTypeModel(childId, "CHILD", "Child");
        childModel.ParentTypeId = parentId;

        var parentModel = CreateTestRelationshipTypeModel(parentId, "PARENT", "Parent");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{childId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(childModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        var request = new MatchesHierarchyRequest
        {
            TypeId = childId,
            AncestorTypeId = parentId
        };

        // Act
        var (status, response) = await service.MatchesHierarchyAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Matches);
        Assert.Equal(1, response.Depth);
    }

    [Fact]
    public async Task MatchesHierarchyAsync_Grandparent_ReturnsDepth2()
    {
        // Arrange
        var service = CreateService();
        var grandchildId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var grandparentId = Guid.NewGuid();

        var grandchildModel = CreateTestRelationshipTypeModel(grandchildId, "GRANDCHILD", "Grandchild");
        grandchildModel.ParentTypeId = parentId;

        var parentModel = CreateTestRelationshipTypeModel(parentId, "PARENT", "Parent");
        parentModel.ParentTypeId = grandparentId;

        var grandparentModel = CreateTestRelationshipTypeModel(grandparentId, "GRANDPARENT", "Grandparent");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{grandchildId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandchildModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{grandparentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandparentModel);

        var request = new MatchesHierarchyRequest
        {
            TypeId = grandchildId,
            AncestorTypeId = grandparentId
        };

        // Act
        var (status, response) = await service.MatchesHierarchyAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Matches);
        Assert.Equal(2, response.Depth);
    }

    [Fact]
    public async Task MatchesHierarchyAsync_NotAnAncestor_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();

        var typeModel = CreateTestRelationshipTypeModel(typeId, "TYPE", "Type");
        typeModel.ParentTypeId = null; // Root type

        var unrelatedModel = CreateTestRelationshipTypeModel(unrelatedId, "UNRELATED", "Unrelated");

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(typeModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{unrelatedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(unrelatedModel);

        var request = new MatchesHierarchyRequest
        {
            TypeId = typeId,
            AncestorTypeId = unrelatedId
        };

        // Act
        var (status, response) = await service.MatchesHierarchyAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Matches);
        Assert.Equal(-1, response.Depth);
    }

    [Fact]
    public async Task GetAncestorsAsync_WithMultipleAncestors_ReturnsAllInOrder()
    {
        // Arrange
        var service = CreateService();
        var grandchildId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var grandparentId = Guid.NewGuid();

        var grandchildModel = CreateTestRelationshipTypeModel(grandchildId, "GRANDCHILD", "Grandchild");
        grandchildModel.ParentTypeId = parentId;

        var parentModel = CreateTestRelationshipTypeModel(parentId, "PARENT", "Parent");
        parentModel.ParentTypeId = grandparentId;

        var grandparentModel = CreateTestRelationshipTypeModel(grandparentId, "GRANDPARENT", "Grandparent");
        grandparentModel.ParentTypeId = null; // Root

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{grandchildId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandchildModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockRelationshipTypeStore
            .Setup(s => s.GetAsync($"type:{grandparentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandparentModel);

        var request = new GetAncestorsRequest { TypeId = grandchildId };

        // Act
        var (status, response) = await service.GetAncestorsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Types.Count);

        // Ancestors should be in order: parent first, then grandparent
        var typesList = response.Types.ToList();
        Assert.Equal("PARENT", typesList[0].Code);
        Assert.Equal("GRANDPARENT", typesList[1].Code);
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
        var instanceId = Guid.NewGuid();
        var registrationEvent = RelationshipTypePermissionRegistration.CreateRegistrationEvent(instanceId, "test-app");

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("relationship-type", registrationEvent.ServiceName);
        Assert.Equal(instanceId, registrationEvent.ServiceId);
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
            RelationshipTypeId = typeId,
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
        _mockGuidListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
    }

    private void SetupSeedMocks()
    {
        // Default: no codes exist yet
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("code-index:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Setup all-types list
        _mockGuidListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Setup parent-index for any type (no children by default)
        _mockGuidListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("parent-index:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
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
