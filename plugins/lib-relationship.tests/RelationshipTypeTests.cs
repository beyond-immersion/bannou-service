using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Relationship.Tests;

/// <summary>
/// Unit tests for relationship type operations on the consolidated RelationshipService.
/// Tests hierarchical relationship type operations including hierarchy traversal, merge, and seed.
/// </summary>
public class RelationshipTypeTests : ServiceTestBase<RelationshipServiceConfiguration>
{
    // Relationship type state store (type definitions, code index, parent index)
    private const string RT_STATE_STORE = "relationship-type-statestore";
    // Relationship instance state store (for merge/delete tests that call internal methods)
    private const string REL_STATE_STORE = "relationship-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;

    // Relationship type stores
    private readonly Mock<IStateStore<RelationshipTypeModel>> _mockRtModelStore;
    private readonly Mock<IStateStore<string>> _mockRtStringStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockRtGuidListStore;

    // Relationship instance stores (needed for merge/delete tests)
    private readonly Mock<IStateStore<RelationshipModel>> _mockRelModelStore;
    private readonly Mock<IStateStore<string>> _mockRelStringStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockRelGuidListStore;

    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<RelationshipService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IResourceClient> _mockResourceClient;

    public RelationshipTypeTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockRtModelStore = new Mock<IStateStore<RelationshipTypeModel>>();
        _mockRtStringStore = new Mock<IStateStore<string>>();
        _mockRtGuidListStore = new Mock<IStateStore<List<Guid>>>();
        _mockRelModelStore = new Mock<IStateStore<RelationshipModel>>();
        _mockRelStringStore = new Mock<IStateStore<string>>();
        _mockRelGuidListStore = new Mock<IStateStore<List<Guid>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<RelationshipService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockResourceClient = new Mock<IResourceClient>();

        // Setup factory for relationship-type-statestore
        _mockStateStoreFactory.Setup(f => f.GetStore<RelationshipTypeModel>(RT_STATE_STORE)).Returns(_mockRtModelStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(RT_STATE_STORE)).Returns(_mockRtStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(RT_STATE_STORE)).Returns(_mockRtGuidListStore.Object);

        // Setup factory for relationship-statestore (used by merge/delete internal calls)
        _mockStateStoreFactory.Setup(f => f.GetStore<RelationshipModel>(REL_STATE_STORE)).Returns(_mockRelModelStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(REL_STATE_STORE)).Returns(_mockRelStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(REL_STATE_STORE)).Returns(_mockRelGuidListStore.Object);

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
            _mockResourceClient.Object);
    }

    #region GetRelationshipType Tests

    [Fact]
    public async Task GetRelationshipTypeAsync_ExistingType_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "FRIEND", "Friend");

        _mockRtModelStore
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

        _mockRtModelStore
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

        _mockRtStringStore
            .Setup(s => s.GetAsync("code-index:FRIEND", It.IsAny<CancellationToken>()))
            .ReturnsAsync(typeId.ToString());

        var model = CreateTestRelationshipTypeModel(typeId, code, "Friend");

        _mockRtModelStore
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

        _mockRtStringStore
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

        _mockRtStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("code-index:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        // Setup all-types list
        _mockRtGuidListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Setup parent index
        _mockRtGuidListStore
            .Setup(s => s.GetAsync($"parent-index:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

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

        _mockRtModelStore
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

        _mockRtModelStore
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

        _mockRtModelStore
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
        model.IsDeprecated = true;

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Setup all-types list
        _mockRtGuidListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { typeId });

        // Setup parent index (no children)
        _mockRtGuidListStore
            .Setup(s => s.GetAsync($"parent-index:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Internal ListRelationshipsByType reads from relationship-statestore type-idx
        _mockRelGuidListStore
            .Setup(s => s.GetAsync($"type-idx:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

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

        _mockRtModelStore
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

        _mockRtGuidListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

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
        var typeIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        _mockRtGuidListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(typeIds);

        var bulkResults = typeIds.Select((id, idx) =>
            new KeyValuePair<string, RelationshipTypeModel>(
                $"type:{id}",
                CreateTestRelationshipTypeModel(id, $"TYPE{idx}", $"Type {idx}")))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        _mockRtModelStore
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

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockRtGuidListStore
            .Setup(s => s.GetAsync($"parent-index:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

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
        model.ParentTypeId = null;

        _mockRtModelStore
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

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new MatchesHierarchyRequest
        {
            TypeId = typeId,
            AncestorTypeId = typeId
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

        _mockRtModelStore
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

        _mockRtModelStore
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

    [Fact]
    public async Task UndeprecateRelationshipTypeAsync_NotDeprecated_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var typeId = Guid.NewGuid();
        var model = CreateTestRelationshipTypeModel(typeId, "ACTIVE", "Active Type");
        // model.IsDeprecated defaults to false

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UndeprecateRelationshipTypeRequest { RelationshipTypeId = typeId };

        // Act
        var (status, response) = await service.UndeprecateRelationshipTypeAsync(request);

        // Assert — idempotent per IMPLEMENTATION TENETS: caller's intent is already satisfied
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
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

        _mockRtModelStore
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
        sourceModel.IsDeprecated = false;

        _mockRtModelStore
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

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        _mockRtModelStore
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
    public async Task MergeRelationshipTypeAsync_TargetDeprecated_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var sourceModel = CreateTestRelationshipTypeModel(sourceId, "SOURCE", "Source Type");
        sourceModel.IsDeprecated = true;

        var targetModel = CreateTestRelationshipTypeModel(targetId, "TARGET", "Target Type");
        targetModel.IsDeprecated = true;

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        var request = new MergeRelationshipTypeRequest
        {
            SourceTypeId = sourceId,
            TargetTypeId = targetId
        };

        // Act
        var (status, response) = await service.MergeRelationshipTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
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

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        // Internal ListRelationshipsByType reads type-idx from relationship-statestore
        _mockRelGuidListStore
            .Setup(s => s.GetAsync($"type-idx:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

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

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        // Set up 3 relationships in the source type index
        var relIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _mockRelGuidListStore
            .Setup(s => s.GetAsync($"type-idx:{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(relIds);

        // Bulk load returns all 3 relationship models
        var bulkResults = relIds.ToDictionary(
            id => $"rel:{id}",
            id => new RelationshipModel
            {
                RelationshipId = id,
                Entity1Id = Guid.NewGuid(),
                Entity1Type = EntityType.Character,
                Entity2Id = Guid.NewGuid(),
                Entity2Type = EntityType.Character,
                RelationshipTypeId = sourceId,
                StartedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });

        _mockRelModelStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, RelationshipModel>)bulkResults);

        // Each UpdateRelationshipAsync call reads the model by key
        foreach (var relId in relIds)
        {
            _mockRelModelStore
                .Setup(s => s.GetAsync($"rel:{relId}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(bulkResults[$"rel:{relId}"]);
        }

        // Track which relationships were updated
        var updatedRelationships = new List<Guid>();
        _mockRelModelStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("rel:")),
                It.IsAny<RelationshipModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipModel, StateOptions?, CancellationToken>((_, m, _, _) =>
                updatedRelationships.Add(m.RelationshipId))
            .ReturnsAsync("etag");

        // Setup type index reads for add/remove
        _mockRelGuidListStore
            .Setup(s => s.GetAsync($"type-idx:{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

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

        _mockRtStringStore
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

        _mockRtStringStore
            .Setup(s => s.GetAsync("code-index:FRIEND", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId.ToString());

        _mockRtModelStore
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
        SetupDynamicStoreMocks(out var creationOrder);

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
        SetupDynamicStoreMocks(out var creationOrder);

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
        Assert.Equal(new[] { "PARENT", "CHILD", "GRANDCHILD" }, creationOrder);
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

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockRtGuidListStore
            .Setup(s => s.GetAsync($"parent-index:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { child1Id, child2Id });

        _mockRtModelStore
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

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockRtGuidListStore
            .Setup(s => s.GetAsync($"parent-index:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { childId });

        _mockRtGuidListStore
            .Setup(s => s.GetAsync($"parent-index:{childId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { grandchildId });

        _mockRtGuidListStore
            .Setup(s => s.GetAsync($"parent-index:{grandchildId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        _mockRtModelStore
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
        Assert.Equal(2, response.Types.Count);
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

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{childId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(childModel);

        _mockRtModelStore
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

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{grandchildId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandchildModel);

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockRtModelStore
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
        typeModel.ParentTypeId = null;

        var unrelatedModel = CreateTestRelationshipTypeModel(unrelatedId, "UNRELATED", "Unrelated");

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{typeId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(typeModel);

        _mockRtModelStore
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
        grandparentModel.ParentTypeId = null;

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{grandchildId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandchildModel);

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockRtModelStore
            .Setup(s => s.GetAsync($"type:{grandparentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandparentModel);

        var request = new GetAncestorsRequest { TypeId = grandchildId };

        // Act
        var (status, response) = await service.GetAncestorsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Types.Count);

        var typesList = response.Types.ToList();
        Assert.Equal("PARENT", typesList[0].Code);
        Assert.Equal("GRANDPARENT", typesList[1].Code);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void Configuration_HasMergedProperties_WithCorrectDefaults()
    {
        // Assert that configuration includes relationship-type properties
        var config = new RelationshipServiceConfiguration();
        Assert.Equal(20, config.MaxHierarchyDepth);
        Assert.Equal(100, config.MaxMigrationErrorsToTrack);
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
        _mockRtStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("code-index:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(codeExists ? Guid.NewGuid().ToString() : null);

        _mockRtGuidListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
    }

    private void SetupSeedMocks()
    {
        _mockRtStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("code-index:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockRtGuidListStore
            .Setup(s => s.GetAsync("all-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        _mockRtGuidListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("parent-index:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
    }

    /// <summary>
    /// Sets up dynamic store mocks that simulate real store behavior for seed tests.
    /// </summary>
    private void SetupDynamicStoreMocks(out List<string> creationOrder)
    {
        var storedTypes = new Dictionary<string, RelationshipTypeModel>();
        var storedCodeIndex = new Dictionary<string, string>();
        var order = new List<string>();

        _mockRtStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedCodeIndex.TryGetValue(key, out var val) ? val : null);

        _mockRtStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((key, value, _, _) =>
                storedCodeIndex[key] = value)
            .ReturnsAsync("etag");

        _mockRtModelStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedTypes.TryGetValue(key, out var val) ? val : null);

        _mockRtModelStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<RelationshipTypeModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RelationshipTypeModel, StateOptions?, CancellationToken>((key, model, _, _) =>
            {
                storedTypes[key] = model;
                order.Add(model.Code);
            })
            .ReturnsAsync("etag");

        _mockRtGuidListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        creationOrder = order;
    }

    #endregion
}
