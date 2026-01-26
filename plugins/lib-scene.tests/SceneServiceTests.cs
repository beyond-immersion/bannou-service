using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Scene;
using BeyondImmersion.BannouService.TestUtilities;
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

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void SceneService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<SceneService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void SceneServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new SceneServiceConfiguration();

        // Assert
        Assert.NotNull(config);
        Assert.Equal("scenes", config.AssetBucket);
        Assert.Equal(60, config.DefaultCheckoutTtlMinutes);
        Assert.Equal(10000, config.MaxNodeCount);
    }

    [Fact]
    public void SceneServiceConfiguration_HasExpectedDefaults()
    {
        // Arrange & Act
        var config = new SceneServiceConfiguration();

        // Assert - verify all configuration defaults
        Assert.Equal("application/x-bannou-scene+yaml", config.AssetContentType);
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
    public void SceneServiceConfiguration_AssetBucket_DefaultsToScenes()
    {
        var config = new SceneServiceConfiguration();
        Assert.Equal("scenes", config.AssetBucket);
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
            e.Method == ServiceEndpointMethod.POST);

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
            e.Method == ServiceEndpointMethod.POST);

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
            e.Method == ServiceEndpointMethod.POST);

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
            e.Method == ServiceEndpointMethod.POST);

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
            e.Method == ServiceEndpointMethod.POST);

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
            e.Method == ServiceEndpointMethod.POST);

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
            e.Method == ServiceEndpointMethod.POST);

        Assert.NotNull(commitEndpoint);
    }

    [Fact]
    public void ScenePermissionRegistration_ServiceId_ShouldBeScene()
    {
        // Assert
        Assert.Equal("scene", ScenePermissionRegistration.ServiceId);
    }

    [Fact]
    public void ScenePermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        // Act
        var registrationEvent = ScenePermissionRegistration.CreateRegistrationEvent(instanceId, "test-app");

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("scene", registrationEvent.ServiceName);
        Assert.Equal(instanceId, registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.Endpoints);
        Assert.NotEmpty(registrationEvent.Endpoints);
    }

    [Fact]
    public void ScenePermissionRegistration_CreateRegistrationEvent_EndpointsMatchGetEndpoints()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        // Act
        var registrationEvent = ScenePermissionRegistration.CreateRegistrationEvent(instanceId, "test-app");
        var directEndpoints = ScenePermissionRegistration.GetEndpoints();

        // Assert
        Assert.Equal(directEndpoints.Count, registrationEvent.Endpoints.Count);
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

    #region SceneType Enum Tests

    [Fact]
    public void SceneType_ContainsExpectedValues()
    {
        // Verify core scene types exist
        Assert.True(Enum.IsDefined(typeof(SceneType), SceneType.Unknown));
        Assert.True(Enum.IsDefined(typeof(SceneType), SceneType.Region));
        Assert.True(Enum.IsDefined(typeof(SceneType), SceneType.City));
        Assert.True(Enum.IsDefined(typeof(SceneType), SceneType.District));
        Assert.True(Enum.IsDefined(typeof(SceneType), SceneType.Lot));
        Assert.True(Enum.IsDefined(typeof(SceneType), SceneType.Building));
        Assert.True(Enum.IsDefined(typeof(SceneType), SceneType.Room));
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
        var sceneId = Guid.NewGuid();
        var response = new DeleteSceneResponse
        {
            Deleted = true,
            SceneId = sceneId
        };
        Assert.True(response.Deleted);
        Assert.Equal(sceneId, response.SceneId);
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
        var affordance = new Affordance { Type = AffordanceType.Sittable };
        Assert.Equal(AffordanceType.Sittable, affordance.Type);
    }

    [Fact]
    public void Affordance_CanSetParameters()
    {
        var affordance = new Affordance
        {
            Type = AffordanceType.Door,
            Parameters = new { openAngle = 90, locked = false }
        };

        Assert.Equal(AffordanceType.Door, affordance.Type);
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

    #region AffordanceType Enum Tests

    [Fact]
    public void AffordanceType_ContainsExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(AffordanceType), AffordanceType.Walkable));
        Assert.True(Enum.IsDefined(typeof(AffordanceType), AffordanceType.Climbable));
        Assert.True(Enum.IsDefined(typeof(AffordanceType), AffordanceType.Sittable));
        Assert.True(Enum.IsDefined(typeof(AffordanceType), AffordanceType.Interactive));
        Assert.True(Enum.IsDefined(typeof(AffordanceType), AffordanceType.Collectible));
        Assert.True(Enum.IsDefined(typeof(AffordanceType), AffordanceType.Destructible));
        Assert.True(Enum.IsDefined(typeof(AffordanceType), AffordanceType.Container));
        Assert.True(Enum.IsDefined(typeof(AffordanceType), AffordanceType.Door));
        Assert.True(Enum.IsDefined(typeof(AffordanceType), AffordanceType.Teleport));
    }

    #endregion

    #region MarkerType Enum Tests

    [Fact]
    public void MarkerType_ContainsExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(MarkerType), MarkerType.Generic));
        Assert.True(Enum.IsDefined(typeof(MarkerType), MarkerType.Spawn_point));
        Assert.True(Enum.IsDefined(typeof(MarkerType), MarkerType.Npc_spawn));
        Assert.True(Enum.IsDefined(typeof(MarkerType), MarkerType.Waypoint));
        Assert.True(Enum.IsDefined(typeof(MarkerType), MarkerType.Camera_point));
        Assert.True(Enum.IsDefined(typeof(MarkerType), MarkerType.Light_point));
        Assert.True(Enum.IsDefined(typeof(MarkerType), MarkerType.Audio_point));
        Assert.True(Enum.IsDefined(typeof(MarkerType), MarkerType.Trigger_point));
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
                new Affordance { Type = AffordanceType.Walkable },
                new Affordance { Type = AffordanceType.Sittable }
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
            MarkerType = MarkerType.Spawn_point
        };

        Assert.Equal(MarkerType.Spawn_point, node.MarkerType);
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
            SceneType = SceneType.Region,
            Name = "Test Scene"
        };
        Assert.Equal("test-game", evt.GameId);
        Assert.Equal(SceneType.Region, evt.SceneType);
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
            CheckedOutBy = "user-123",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        Assert.Equal("user-123", evt.CheckedOutBy);
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
            CommittedBy = "user-123",
            Version = "1.0.2"
        };
        Assert.Equal("1.0.2", evt.Version);
    }

    #endregion
}
