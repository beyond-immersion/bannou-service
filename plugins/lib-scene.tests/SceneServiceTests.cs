using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Scene;
using BeyondImmersion.BannouService.Scene.Helpers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Scene.Tests;

/// <summary>
/// Unit tests for SceneService.
/// Tests constructor validation, configuration, permission registration, and scene validation logic.
/// Integration tests for full CRUD operations are in lib-testing (http-tester, edge-tester).
/// </summary>
public class SceneServiceTests
{
    #region Constructor Validation
    #endregion

    #region Key Builder Tests

    [Fact]
    public void BuildSceneIndexKey_ProducesCorrectFormat()
    {
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var key = SceneService.BuildSceneIndexKey(id);
        Assert.Equal("scene:index:01234567-89ab-cdef-0123-456789abcdef", key);
    }

    [Fact]
    public void BuildSceneContentKey_ProducesCorrectFormat()
    {
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var key = SceneService.BuildSceneContentKey(id);
        Assert.Equal("scene:content:01234567-89ab-cdef-0123-456789abcdef", key);
    }

    [Fact]
    public void BuildSceneByGameKey_ProducesCorrectFormat()
    {
        var key = SceneService.BuildSceneByGameKey("my-game");
        Assert.Equal("scene:by-game:my-game", key);
    }

    [Fact]
    public void BuildSceneByTypeKey_ProducesCorrectFormat()
    {
        var key = SceneService.BuildSceneByTypeKey("my-game", "Region");
        Assert.Equal("scene:by-type:my-game:Region", key);
    }

    [Fact]
    public void BuildSceneReferencesKey_ProducesCorrectFormat()
    {
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var key = SceneService.BuildSceneReferencesKey(id);
        Assert.Equal("scene:references:01234567-89ab-cdef-0123-456789abcdef", key);
    }

    [Fact]
    public void BuildSceneAssetsKey_ProducesCorrectFormat()
    {
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var key = SceneService.BuildSceneAssetsKey(id);
        Assert.Equal("scene:assets:01234567-89ab-cdef-0123-456789abcdef", key);
    }

    [Fact]
    public void BuildSceneCheckoutKey_ProducesCorrectFormat()
    {
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var key = SceneService.BuildSceneCheckoutKey(id);
        Assert.Equal("scene:checkout:01234567-89ab-cdef-0123-456789abcdef", key);
    }

    [Fact]
    public void BuildValidationRulesKey_ProducesCorrectFormat()
    {
        var key = SceneService.BuildValidationRulesKey("my-game", "Region");
        Assert.Equal("scene:validation:my-game:Region", key);
    }

    [Fact]
    public void BuildSceneVersionHistoryKey_ProducesCorrectFormat()
    {
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var key = SceneService.BuildSceneVersionHistoryKey(id);
        Assert.Equal("scene:version-history:01234567-89ab-cdef-0123-456789abcdef", key);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void SceneServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new SceneServiceConfiguration();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(60, config.DefaultCheckoutTtlMinutes);
        Assert.Equal(10000, config.MaxNodeCount);
    }

    [Fact]
    public void SceneServiceConfiguration_HasExpectedDefaults()
    {
        // Arrange & Act
        var config = new SceneServiceConfiguration();

        // Assert - verify all configuration defaults
        Assert.Equal(10, config.MaxCheckoutExtensions);
        Assert.Equal(3, config.DefaultMaxReferenceDepth);
        Assert.Equal(10, config.MaxReferenceDepthLimit);
        Assert.Equal(100, config.MaxVersionRetentionCount);
        Assert.Equal(50, config.MaxTagsPerScene);
        Assert.Equal(20, config.MaxTagsPerNode);
        Assert.Equal(200, config.MaxListResults);
        Assert.Equal(100, config.MaxSearchResults);
    }

    [Fact]
    public void SceneServiceConfiguration_CheckoutTtlBuffer_DefaultsTo5()
    {
        var config = new SceneServiceConfiguration();
        Assert.Equal(5, config.CheckoutTtlBufferMinutes);
    }

    [Fact]
    public void SceneServiceConfiguration_CanSetForceServiceId()
    {
        var config = new SceneServiceConfiguration();
        var testId = Guid.NewGuid();
        config.ForceServiceId = testId;
        Assert.Equal(testId, config.ForceServiceId);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void ScenePermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = ScenePermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
    }

    [Fact]
    public void ScenePermissionRegistration_GetEndpoints_ShouldContainCreateSceneEndpoint()
    {
        // Act
        var endpoints = ScenePermissionRegistration.GetEndpoints();

        // Assert
        var createEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/scene/create" &&
            e.Method == ServiceEndpointMethod.Post);

        Assert.NotNull(createEndpoint);
    }

    [Fact]
    public void ScenePermissionRegistration_GetEndpoints_ShouldContainGetSceneEndpoint()
    {
        // Act
        var endpoints = ScenePermissionRegistration.GetEndpoints();

        // Assert
        var getEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/scene/get" &&
            e.Method == ServiceEndpointMethod.Post);

        Assert.NotNull(getEndpoint);
    }

    [Fact]
    public void ScenePermissionRegistration_GetEndpoints_ShouldContainListEndpoint()
    {
        // Act
        var endpoints = ScenePermissionRegistration.GetEndpoints();

        // Assert
        var listEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/scene/list" &&
            e.Method == ServiceEndpointMethod.Post);

        Assert.NotNull(listEndpoint);
    }

    [Fact]
    public void ScenePermissionRegistration_GetEndpoints_ShouldContainUpdateEndpoint()
    {
        // Act
        var endpoints = ScenePermissionRegistration.GetEndpoints();

        // Assert
        var updateEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/scene/update" &&
            e.Method == ServiceEndpointMethod.Post);

        Assert.NotNull(updateEndpoint);
    }

    [Fact]
    public void ScenePermissionRegistration_GetEndpoints_ShouldContainDeleteEndpoint()
    {
        // Act
        var endpoints = ScenePermissionRegistration.GetEndpoints();

        // Assert
        var deleteEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/scene/delete" &&
            e.Method == ServiceEndpointMethod.Post);

        Assert.NotNull(deleteEndpoint);
    }

    [Fact]
    public void ScenePermissionRegistration_GetEndpoints_ShouldContainCheckoutEndpoint()
    {
        // Act
        var endpoints = ScenePermissionRegistration.GetEndpoints();

        // Assert
        var checkoutEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/scene/checkout" &&
            e.Method == ServiceEndpointMethod.Post);

        Assert.NotNull(checkoutEndpoint);
    }

    [Fact]
    public void ScenePermissionRegistration_GetEndpoints_ShouldContainCommitEndpoint()
    {
        // Act
        var endpoints = ScenePermissionRegistration.GetEndpoints();

        // Assert
        var commitEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/scene/commit" &&
            e.Method == ServiceEndpointMethod.Post);

        Assert.NotNull(commitEndpoint);
    }

    [Fact]
    public void ScenePermissionRegistration_ServiceId_ShouldBeScene()
    {
        // Assert
        Assert.Equal("scene", ScenePermissionRegistration.ServiceId);
    }

    [Fact]
    public void ScenePermissionRegistration_BuildPermissionMatrix_ShouldBeValid()
    {
        PermissionMatrixValidator.ValidatePermissionMatrix(
            ScenePermissionRegistration.ServiceId,
            ScenePermissionRegistration.ServiceVersion,
            ScenePermissionRegistration.BuildPermissionMatrix());
    }

    #endregion

    #region Scene Model Validation Tests

    [Fact]
    public void Scene_CanBeInstantiated()
    {
        var scene = new Scene();
        Assert.NotNull(scene);
    }

    [Fact]
    public void Scene_DefaultSchema_IsCorrect()
    {
        var scene = new Scene();
        Assert.Equal("bannou://schemas/scene/v1", scene.Schema);
    }

    [Fact]
    public void SceneNode_CanBeInstantiated()
    {
        var node = new SceneNode();
        Assert.NotNull(node);
    }

    [Fact]
    public void SceneNode_DefaultEnabled_IsTrue()
    {
        var node = new SceneNode();
        Assert.True(node.Enabled);
    }

    [Fact]
    public void SceneNode_DefaultSortOrder_IsZero()
    {
        var node = new SceneNode();
        Assert.Equal(0, node.SortOrder);
    }

    [Fact]
    public void Transform_CanBeInstantiated()
    {
        var transform = new Transform();
        Assert.NotNull(transform);
        Assert.NotNull(transform.Position);
        Assert.NotNull(transform.Rotation);
        Assert.NotNull(transform.Scale);
    }

    [Fact]
    public void Vector3_CanBeInstantiated()
    {
        var vec = new Vector3 { X = 1, Y = 2, Z = 3 };
        Assert.Equal(1, vec.X);
        Assert.Equal(2, vec.Y);
        Assert.Equal(3, vec.Z);
    }

    [Fact]
    public void Quaternion_CanBeInstantiated()
    {
        var quat = new Quaternion { X = 0, Y = 0, Z = 0, W = 1 };
        Assert.Equal(0, quat.X);
        Assert.Equal(0, quat.Y);
        Assert.Equal(0, quat.Z);
        Assert.Equal(1, quat.W);
    }

    [Fact]
    public void ValidationResult_CanBeInstantiated()
    {
        var result = new ValidationResult { Valid = true };
        Assert.True(result.Valid);
    }

    [Fact]
    public void ValidationError_CanBeInstantiated()
    {
        var error = new ValidationError
        {
            RuleId = "test-rule",
            Message = "Test error message",
            Severity = ValidationSeverity.Error
        };
        Assert.Equal("test-rule", error.RuleId);
        Assert.Equal("Test error message", error.Message);
        Assert.Equal(ValidationSeverity.Error, error.Severity);
    }

    #endregion

    #region NodeType Enum Tests

    [Fact]
    public void NodeType_ContainsExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(NodeType), NodeType.Group));
        Assert.True(Enum.IsDefined(typeof(NodeType), NodeType.Reference));
        Assert.True(Enum.IsDefined(typeof(NodeType), NodeType.Mesh));
        Assert.True(Enum.IsDefined(typeof(NodeType), NodeType.Marker));
    }

    #endregion

    #region Request/Response Model Tests

    [Fact]
    public void CreateSceneRequest_CanBeInstantiated()
    {
        var request = new CreateSceneRequest { Scene = new Scene() };
        Assert.NotNull(request.Scene);
    }

    [Fact]
    public void GetSceneRequest_CanBeInstantiated()
    {
        var sceneId = Guid.NewGuid();
        var request = new GetSceneRequest { SceneId = sceneId };
        Assert.Equal(sceneId, request.SceneId);
    }

    [Fact]
    public void UpdateSceneRequest_CanBeInstantiated()
    {
        var request = new UpdateSceneRequest
        {
            Scene = new Scene(),
            CheckoutToken = "test-token"
        };
        Assert.NotNull(request.Scene);
        Assert.Equal("test-token", request.CheckoutToken);
    }

    [Fact]
    public void DeleteSceneRequest_CanBeInstantiated()
    {
        var sceneId = Guid.NewGuid();
        var request = new DeleteSceneRequest
        {
            SceneId = sceneId,
            Reason = "Test deletion"
        };
        Assert.Equal(sceneId, request.SceneId);
        Assert.Equal("Test deletion", request.Reason);
    }

    [Fact]
    public void ValidateSceneRequest_CanBeInstantiated()
    {
        var request = new ValidateSceneRequest
        {
            Scene = new Scene(),
            ApplyGameRules = true
        };
        Assert.NotNull(request.Scene);
        Assert.True(request.ApplyGameRules);
    }

    [Fact]
    public void CheckoutRequest_CanBeInstantiated()
    {
        var sceneId = Guid.NewGuid();
        var request = new CheckoutRequest
        {
            SceneId = sceneId,
            EditorId = "user-123"
        };
        Assert.Equal(sceneId, request.SceneId);
        Assert.Equal("user-123", request.EditorId);
    }

    [Fact]
    public void ListScenesRequest_CanBeInstantiated()
    {
        var request = new ListScenesRequest
        {
            GameId = "game-1",
            Limit = 50,
            Offset = 10
        };
        Assert.Equal("game-1", request.GameId);
        Assert.Equal(50, request.Limit);
        Assert.Equal(10, request.Offset);
    }

    [Fact]
    public void SceneResponse_CanBeInstantiated()
    {
        var scene = new Scene { Name = "Test Scene" };
        var response = new SceneResponse { Scene = scene };
        Assert.NotNull(response.Scene);
        Assert.Equal("Test Scene", response.Scene.Name);
    }

    [Fact]
    public void DeleteSceneResponse_CanBeInstantiated()
    {
        var response = new DeleteSceneResponse();
        Assert.NotNull(response);
    }

    #endregion

    #region Scene Composer Extensions (AttachmentPoint, Affordance, AssetSlot)

    [Fact]
    public void AttachmentPoint_CanBeInstantiated()
    {
        var attachmentPoint = new AttachmentPoint();
        Assert.NotNull(attachmentPoint);
    }

    [Fact]
    public void AttachmentPoint_LocalTransform_DefaultsToNewTransform()
    {
        var attachmentPoint = new AttachmentPoint();
        Assert.NotNull(attachmentPoint.LocalTransform);
    }

    [Fact]
    public void AttachmentPoint_CanSetProperties()
    {
        var attachmentPoint = new AttachmentPoint
        {
            Name = "wall_hook_left",
            AcceptsTags = new List<string> { "wall_decoration", "picture_frame" }
        };

        Assert.Equal("wall_hook_left", attachmentPoint.Name);
        Assert.Contains("wall_decoration", attachmentPoint.AcceptsTags);
        Assert.Contains("picture_frame", attachmentPoint.AcceptsTags);
    }

    [Fact]
    public void Affordance_CanBeInstantiated()
    {
        var affordance = new Affordance();
        Assert.NotNull(affordance);
    }

    [Fact]
    public void Affordance_CanSetType()
    {
        var affordance = new Affordance { Type = "Sittable" };
        Assert.Equal("Sittable", affordance.Type);
    }

    [Fact]
    public void Affordance_CanSetParameters()
    {
        var affordance = new Affordance
        {
            Type = "Door",
            Parameters = new { openAngle = 90, locked = false }
        };

        Assert.Equal("Door", affordance.Type);
        Assert.NotNull(affordance.Parameters);
    }

    [Fact]
    public void AssetSlot_CanBeInstantiated()
    {
        var assetSlot = new AssetSlot();
        Assert.NotNull(assetSlot);
    }

    [Fact]
    public void AssetSlot_CanSetProperties()
    {
        var assetSlot = new AssetSlot
        {
            SlotType = "chair",
            AcceptsTags = new List<string> { "furniture", "seating" }
        };

        Assert.Equal("chair", assetSlot.SlotType);
        Assert.Contains("furniture", assetSlot.AcceptsTags);
    }

    [Fact]
    public void AssetSlot_CanSetVariations()
    {
        var bundleId = Guid.NewGuid();
        var assetSlot = new AssetSlot
        {
            SlotType = "table",
            Variations = new List<AssetReference>
            {
                new AssetReference { BundleId = bundleId, AssetId = Guid.NewGuid() },
                new AssetReference { BundleId = bundleId, AssetId = Guid.NewGuid() }
            }
        };

        Assert.Equal(2, assetSlot.Variations.Count);
    }

    #endregion

    #region SceneNode Extensions Tests

    [Fact]
    public void SceneNode_CanHaveAttachmentPoints()
    {
        var node = new SceneNode
        {
            AttachmentPoints = new List<AttachmentPoint>
            {
                new AttachmentPoint { Name = "hook1" },
                new AttachmentPoint { Name = "hook2" }
            }
        };

        Assert.Equal(2, node.AttachmentPoints.Count);
    }

    [Fact]
    public void SceneNode_CanHaveAffordances()
    {
        var node = new SceneNode
        {
            Affordances = new List<Affordance>
            {
                new Affordance { Type = "Walkable" },
                new Affordance { Type = "Sittable" }
            }
        };

        Assert.Equal(2, node.Affordances.Count);
    }

    [Fact]
    public void SceneNode_CanHaveAssetSlot()
    {
        var node = new SceneNode
        {
            AssetSlot = new AssetSlot { SlotType = "furniture" }
        };

        Assert.NotNull(node.AssetSlot);
        Assert.Equal("furniture", node.AssetSlot.SlotType);
    }

    [Fact]
    public void SceneNode_CanHaveMarkerType()
    {
        var node = new SceneNode
        {
            NodeType = NodeType.Marker,
            MarkerType = "SpawnPoint"
        };

        Assert.Equal("SpawnPoint", node.MarkerType);
    }

    #endregion

    #region Event Model Tests

    [Fact]
    public void SceneCreatedEvent_CanBeInstantiated()
    {
        var evt = new SceneCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = Guid.NewGuid(),
            GameId = "test-game",
            Name = "Test Scene"
        };
        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.NotEqual(Guid.Empty, evt.SceneId);
        Assert.Equal("test-game", evt.GameId);
    }

    [Fact]
    public void SceneUpdatedEvent_CanBeInstantiated()
    {
        var evt = new SceneUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = Guid.NewGuid(),
            GameId = "test-game",
            SceneType = "Region",
            Name = "Test Scene"
        };
        Assert.Equal("test-game", evt.GameId);
        Assert.Equal("Region", evt.SceneType);
    }

    [Fact]
    public void SceneDeletedEvent_CanBeInstantiated()
    {
        var evt = new SceneDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = Guid.NewGuid(),
            Name = "Deleted Scene"
        };
        Assert.Equal("Deleted Scene", evt.Name);
    }

    [Fact]
    public void SceneCheckedOutEvent_CanBeInstantiated()
    {
        var evt = new SceneCheckedOutEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = Guid.NewGuid(),
            CheckedOutByType = SceneEditorType.Session,
            CheckedOutById = "user-123",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        Assert.Equal("user-123", evt.CheckedOutById);
        Assert.True(evt.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void SceneCommittedEvent_CanBeInstantiated()
    {
        var evt = new SceneCommittedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = Guid.NewGuid(),
            CommittedByType = SceneEditorType.Session,
            CommittedById = "user-123",
            Version = "1.0.2"
        };
        Assert.Equal("1.0.2", evt.Version);
    }

    #endregion
}

/// <summary>
/// Business logic unit tests for SceneService with mocked state stores.
/// Tests service method logic including state interactions, validation flows, and error paths.
/// </summary>
#pragma warning disable CS8620 // Argument of type cannot be used for parameter of type due to differences in the nullability
public class SceneServiceBusinessLogicTests : ServiceTestBase<SceneServiceConfiguration>
{
    private const string STATE_STORE = "scene-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<SceneIndexEntry>> _mockIndexStore;
    private readonly Mock<IStateStore<HashSet<Guid>>> _mockGuidSetStore;
    private readonly Mock<IStateStore<CheckoutState>> _mockCheckoutStore;
    private readonly Mock<IStateStore<SceneContentEntry>> _mockContentStore;
    private readonly Mock<IStateStore<List<ValidationRule>>> _mockRulesStore;
    private readonly Mock<IStateStore<List<VersionHistoryEntry>>> _mockHistoryStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<SceneService>> _mockLogger;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ISceneValidationService> _mockValidationService;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IMeshInstanceIdentifier> _mockMeshInstanceIdentifier;

    public SceneServiceBusinessLogicTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockIndexStore = new Mock<IStateStore<SceneIndexEntry>>();
        _mockGuidSetStore = new Mock<IStateStore<HashSet<Guid>>>();
        _mockCheckoutStore = new Mock<IStateStore<CheckoutState>>();
        _mockContentStore = new Mock<IStateStore<SceneContentEntry>>();
        _mockRulesStore = new Mock<IStateStore<List<ValidationRule>>>();
        _mockHistoryStore = new Mock<IStateStore<List<VersionHistoryEntry>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<SceneService>>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockValidationService = new Mock<ISceneValidationService>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockMeshInstanceIdentifier = new Mock<IMeshInstanceIdentifier>();

        _mockMeshInstanceIdentifier.Setup(m => m.InstanceId).Returns(Guid.NewGuid());

        // Setup factory to return typed stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SceneIndexEntry>(STATE_STORE))
            .Returns(_mockIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<HashSet<Guid>>(STATE_STORE))
            .Returns(_mockGuidSetStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CheckoutState>(STATE_STORE))
            .Returns(_mockCheckoutStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SceneContentEntry>(STATE_STORE))
            .Returns(_mockContentStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<ValidationRule>>(STATE_STORE))
            .Returns(_mockRulesStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<VersionHistoryEntry>>(STATE_STORE))
            .Returns(_mockHistoryStore.Object);

        // Default bulk operation behavior
        _mockIndexStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SceneIndexEntry>());
    }

    private SceneService CreateService() => new SceneService(
        _mockMessageBus.Object,
        _mockStateStoreFactory.Object,
        _mockLogger.Object,
        Configuration,
        _mockLockProvider.Object,
        _mockEventConsumer.Object,
        _mockValidationService.Object,
        _mockTelemetryProvider.Object,
        _mockMeshInstanceIdentifier.Object);

    #region GetSceneAsync Tests

    [Fact]
    public async Task GetSceneAsync_SceneNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();
        _mockIndexStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SceneIndexEntry?)null);

        // Act
        var (status, response) = await service.GetSceneAsync(new GetSceneRequest { SceneId = sceneId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetSceneAsync_AssetNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        _mockIndexStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SceneIndexEntry
            {
                SceneId = sceneId,
                AssetId = assetId,
                SceneType = "Region",
                Name = "Test Scene",
                Version = "1.0.0"
            });

        // Content store returns null — asset missing
        _mockContentStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneContentKey(assetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SceneContentEntry?)null);

        // Act
        var (status, response) = await service.GetSceneAsync(new GetSceneRequest { SceneId = sceneId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetSceneAsync_SceneExists_ReturnsScene()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        _mockIndexStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SceneIndexEntry
            {
                SceneId = sceneId,
                AssetId = assetId,
                SceneType = "Region",
                Name = "Test Scene",
                Version = "1.0.0"
            });

        // Content store returns valid YAML
        _mockContentStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneContentKey(assetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SceneContentEntry
            {
                SceneId = sceneId,
                Version = "1.0.0",
                Content = $"sceneId: {sceneId}\nname: Test Scene\nsceneType: Region\nversion: 1.0.0\nschema: bannou://schemas/scene/v1\n",
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.GetSceneAsync(new GetSceneRequest { SceneId = sceneId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Scene);
        Assert.Equal("Test Scene", response.Scene.Name);
    }

    #endregion

    #region DeleteSceneAsync Tests

    [Fact]
    public async Task DeleteSceneAsync_SceneNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();
        _mockIndexStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SceneIndexEntry?)null);

        // Act
        var (status, response) = await service.DeleteSceneAsync(new DeleteSceneRequest { SceneId = sceneId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteSceneAsync_SceneHasReferences_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();
        var referencingSceneId = Guid.NewGuid();

        _mockIndexStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SceneIndexEntry
            {
                SceneId = sceneId,
                AssetId = Guid.NewGuid(),
                SceneType = "Region",
                Name = "Test Scene",
                Version = "1.0.0"
            });

        // Reference index shows another scene references this one
        _mockGuidSetStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneReferencesKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { referencingSceneId });

        // Act
        var (status, response) = await service.DeleteSceneAsync(new DeleteSceneRequest { SceneId = sceneId });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
        // Verify no delete occurred
        _mockIndexStore.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSceneAsync_NoReferences_DeletesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        _mockIndexStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SceneIndexEntry
            {
                SceneId = sceneId,
                AssetId = assetId,
                SceneType = "Region",
                Name = "Test Scene",
                Version = "1.0.0"
            });

        // No references
        _mockGuidSetStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneReferencesKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<Guid>?)null);

        // Content store for loading scene for event publishing
        _mockContentStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneContentKey(assetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SceneContentEntry?)null);

        // ETag operations for index modification
        _mockGuidSetStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((HashSet<Guid>?)null, (string?)"etag-1"));
        _mockGuidSetStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<HashSet<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (status, response) = await service.DeleteSceneAsync(new DeleteSceneRequest { SceneId = sceneId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // Verify index was deleted
        _mockIndexStore.Verify(s => s.DeleteAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()), Times.Once);
        // Verify content was deleted
        _mockContentStore.Verify(s => s.DeleteAsync(SceneService.BuildSceneContentKey(sceneId), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetValidationRulesAsync Tests

    [Fact]
    public async Task GetValidationRulesAsync_NoRulesStored_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var gameId = "arcadia";
        var sceneType = "Region";

        _mockRulesStore
            .Setup(s => s.GetAsync(SceneService.BuildValidationRulesKey(gameId, sceneType), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ValidationRule>?)null);

        // Act
        var (status, response) = await service.GetValidationRulesAsync(new GetValidationRulesRequest
        {
            GameId = gameId,
            SceneType = sceneType
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Rules);
        Assert.Empty(response.Rules);
    }

    [Fact]
    public async Task GetValidationRulesAsync_RulesExist_ReturnsStoredRules()
    {
        // Arrange
        var service = CreateService();
        var gameId = "arcadia";
        var sceneType = "Region";

        var storedRules = new List<ValidationRule>
        {
            new ValidationRule { RuleId = "rule-1", Description = "Must have root node", Severity = ValidationSeverity.Error, RuleType = ValidationRuleType.RequireTag },
            new ValidationRule { RuleId = "rule-2", Description = "Recommended description", Severity = ValidationSeverity.Warning, RuleType = ValidationRuleType.RequireTag }
        };

        _mockRulesStore
            .Setup(s => s.GetAsync(SceneService.BuildValidationRulesKey(gameId, sceneType), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedRules);

        // Act
        var (status, response) = await service.GetValidationRulesAsync(new GetValidationRulesRequest
        {
            GameId = gameId,
            SceneType = sceneType
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Rules);
        Assert.Equal(2, response.Rules.Count);
        var rulesList = response.Rules.ToList();
        Assert.Equal("rule-1", rulesList[0].RuleId);
        Assert.Equal("rule-2", rulesList[1].RuleId);
    }

    #endregion

    #region RegisterValidationRulesAsync Tests

    [Fact]
    public async Task RegisterValidationRulesAsync_StoresRulesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var gameId = "arcadia";
        var sceneType = "Region";

        var rules = new List<ValidationRule>
        {
            new ValidationRule { RuleId = "rule-1", Description = "Test rule", Severity = ValidationSeverity.Error, RuleType = ValidationRuleType.RequireTag }
        };

        // Setup message bus to accept publish calls
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var status = await service.RegisterValidationRulesAsync(new RegisterValidationRulesRequest
        {
            GameId = gameId,
            SceneType = sceneType,
            Rules = rules
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify rules were stored at the correct key
        _mockRulesStore.Verify(s => s.SaveAsync(
            SceneService.BuildValidationRulesKey(gameId, sceneType),
            It.Is<List<ValidationRule>>(r => r.Count == 1 && r[0].RuleId == "rule-1"),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CheckoutSceneAsync Tests

    [Fact]
    public async Task CheckoutSceneAsync_SceneNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();

        _mockIndexStore
            .Setup(s => s.GetWithETagAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SceneIndexEntry?)null, (string?)null));

        // Act
        var (status, response) = await service.CheckoutSceneAsync(new CheckoutRequest
        {
            SceneId = sceneId,
            EditorType = SceneEditorType.Session
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CheckoutSceneAsync_AlreadyCheckedOut_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();

        _mockIndexStore
            .Setup(s => s.GetWithETagAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new SceneIndexEntry
            {
                SceneId = sceneId,
                AssetId = Guid.NewGuid(),
                SceneType = "Region",
                Name = "Test Scene",
                Version = "1.0.0",
                IsCheckedOut = true,
                CheckedOutByType = SceneEditorType.Session,
                CheckedOutById = "other-user"
            }, (string?)"etag-1"));

        // Existing checkout is still valid (not expired)
        _mockCheckoutStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneCheckoutKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutState
            {
                SceneId = sceneId,
                Token = "existing-token",
                EditorType = SceneEditorType.Session,
                EditorId = "other-user",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            });

        // Act
        var (status, response) = await service.CheckoutSceneAsync(new CheckoutRequest
        {
            SceneId = sceneId,
            EditorType = SceneEditorType.Session
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CheckoutSceneAsync_NotCheckedOut_CreatesCheckout()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        _mockIndexStore
            .Setup(s => s.GetWithETagAsync(SceneService.BuildSceneIndexKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new SceneIndexEntry
            {
                SceneId = sceneId,
                AssetId = assetId,
                SceneType = "Region",
                Name = "Test Scene",
                Version = "1.0.0",
                IsCheckedOut = false
            }, (string?)"etag-1"));

        // LoadSceneAssetAsync needs content store to return valid content
        _mockContentStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneContentKey(assetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SceneContentEntry
            {
                SceneId = sceneId,
                Version = "1.0.0",
                Content = $"sceneId: {sceneId}\nname: Test Scene\nsceneType: Region\nversion: 1.0.0\nschema: bannou://schemas/scene/v1\n",
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockIndexStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<SceneIndexEntry>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.CheckoutSceneAsync(new CheckoutRequest
        {
            SceneId = sceneId,
            EditorType = SceneEditorType.Session,
            EditorId = "my-session-id"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.CheckoutToken);
        Assert.True(response.ExpiresAt > DateTimeOffset.UtcNow);

        // Verify checkout state was saved
        _mockCheckoutStore.Verify(s => s.SaveAsync(
            SceneService.BuildSceneCheckoutKey(sceneId),
            It.Is<CheckoutState>(c => c.SceneId == sceneId && c.EditorId == "my-session-id"),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DiscardCheckoutAsync Tests

    [Fact]
    public async Task DiscardCheckoutAsync_InvalidToken_ReturnsForbidden()
    {
        // Arrange
        var service = CreateService();
        var sceneId = Guid.NewGuid();

        _mockCheckoutStore
            .Setup(s => s.GetAsync(SceneService.BuildSceneCheckoutKey(sceneId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutState
            {
                SceneId = sceneId,
                Token = "real-token",
                EditorType = SceneEditorType.Session,
                EditorId = "user-1",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            });

        // Act
        var status = await service.DiscardCheckoutAsync(new DiscardRequest
        {
            SceneId = sceneId,
            CheckoutToken = "wrong-token"
        });

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
    }

    #endregion
}
#pragma warning restore CS8620
