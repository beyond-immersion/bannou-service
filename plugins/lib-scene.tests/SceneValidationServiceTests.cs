using BeyondImmersion.BannouService.Scene.Helpers;

namespace BeyondImmersion.BannouService.Scene.Tests;

/// <summary>
/// Unit tests for SceneValidationService helper methods.
/// </summary>
public class SceneValidationServiceTests
{
    private readonly SceneValidationService _sut;
    private const int DefaultMaxNodeCount = 10000;

    public SceneValidationServiceTests()
    {
        _sut = new SceneValidationService();
    }

    #region ValidateStructure Tests

    [Fact]
    public void ValidateStructure_EmptySceneId_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.SceneId = Guid.Empty;

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("sceneId must be a valid non-empty UUID"));
    }

    [Fact]
    public void ValidateStructure_InvalidVersionPattern_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Version = "invalid";

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("version must match MAJOR.MINOR.PATCH"));
    }

    [Fact]
    public void ValidateStructure_ValidVersionPattern_Passes()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Version = "1.0.0";

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.True(result.Valid);
    }

    [Fact]
    public void ValidateStructure_RootWithParent_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.ParentNodeId = Guid.NewGuid();

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Root node must have null parentNodeId"));
    }

    [Fact]
    public void ValidateStructure_ValidScene_ReturnsValid()
    {
        // Arrange
        var scene = CreateValidScene();

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.True(result.Valid);
    }

    [Fact]
    public void ValidateStructure_InvalidRefIdFormat_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.RefId = "Invalid-RefId"; // Uppercase not allowed

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("refId") && e.Message.Contains("must match pattern"));
    }

    [Fact]
    public void ValidateStructure_RefIdWithSpaces_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.RefId = "invalid ref id";

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("refId"));
    }

    [Fact]
    public void ValidateStructure_ValidRefIdWithUnderscores_ReturnsValid()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.RefId = "valid_ref_id_123";

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.True(result.Valid);
    }

    [Fact]
    public void ValidateStructure_DuplicateRefIds_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Children = new List<SceneNode>
        {
            new SceneNode
            {
                NodeId = Guid.NewGuid(),
                RefId = scene.Root.RefId, // Duplicate
                NodeType = NodeType.Group,
                LocalTransform = new Transform()
            }
        };

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Duplicate refId"));
    }

    [Fact]
    public void ValidateStructure_NestedNodes_ValidatesAllLevels()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Children = new List<SceneNode>
        {
            new SceneNode
            {
                NodeId = Guid.NewGuid(),
                RefId = "child_node",
                NodeType = NodeType.Group,
                LocalTransform = new Transform(),
                Children = new List<SceneNode>
                {
                    new SceneNode
                    {
                        NodeId = Guid.NewGuid(),
                        RefId = "Invalid-Grandchild", // Invalid at deep level
                        NodeType = NodeType.Group,
                        LocalTransform = new Transform()
                    }
                }
            }
        };

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("refId") && e.Message.Contains("must match pattern"));
    }

    [Fact]
    public void ValidateStructure_NodeCountExceeded_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Children = new List<SceneNode>();
        for (int i = 0; i < 10; i++)
        {
            scene.Root.Children.Add(new SceneNode
            {
                NodeId = Guid.NewGuid(),
                RefId = $"node_{i}",
                NodeType = NodeType.Group,
                LocalTransform = new Transform()
            });
        }

        // Act - Use maxNodeCount of 5 to trigger error
        var result = _sut.ValidateStructure(scene, 5);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("exceeds maximum node count"));
    }

    [Fact]
    public void ValidateStructure_EmptyRefId_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.RefId = "";

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Node must have a refId"));
    }

    [Fact]
    public void ValidateStructure_EmptyNodeId_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.NodeId = Guid.Empty;

        // Act
        var result = _sut.ValidateStructure(scene, DefaultMaxNodeCount);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("nodeId must be a valid non-empty UUID"));
    }

    #endregion

    #region ApplyGameValidationRules Tests

    [Fact]
    public void ApplyGameValidationRules_NullRules_ReturnsValidResult()
    {
        // Arrange
        var scene = CreateValidScene();

        // Act
        var result = _sut.ApplyGameValidationRules(scene, null);

        // Assert
        Assert.True(result.Valid);
    }

    [Fact]
    public void ApplyGameValidationRules_EmptyRules_ReturnsValidResult()
    {
        // Arrange
        var scene = CreateValidScene();
        var rules = new List<ValidationRule>();

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.True(result.Valid);
    }

    [Fact]
    public void ApplyGameValidationRules_RequireTag_PresentTag_Passes()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Tags = new List<string> { "required_tag" };
        scene.Root.NodeType = NodeType.Group;
        var rules = new List<ValidationRule>
        {
            new ValidationRule
            {
                RuleId = "test-require-tag",
                RuleType = ValidationRuleType.Require_tag,
                Severity = ValidationSeverity.Error,
                Config = new ValidationRuleConfig
                {
                    Tag = "required_tag",
                    NodeType = "Group"
                }
            }
        };

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.True(result.Valid);
    }

    [Fact]
    public void ApplyGameValidationRules_RequireTag_MissingTag_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Tags = new List<string> { "other_tag" };
        scene.Root.NodeType = NodeType.Group;
        var rules = new List<ValidationRule>
        {
            new ValidationRule
            {
                RuleId = "test-require-tag",
                RuleType = ValidationRuleType.Require_tag,
                Severity = ValidationSeverity.Error,
                Config = new ValidationRuleConfig
                {
                    Tag = "required_tag",
                    NodeType = "Group"
                }
            }
        };

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("required_tag"));
    }

    [Fact]
    public void ApplyGameValidationRules_ForbidTag_AbsentTag_Passes()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Tags = new List<string> { "allowed_tag" };
        var rules = new List<ValidationRule>
        {
            new ValidationRule
            {
                RuleId = "test-forbid-tag",
                RuleType = ValidationRuleType.Forbid_tag,
                Severity = ValidationSeverity.Error,
                Config = new ValidationRuleConfig
                {
                    Tag = "forbidden_tag"
                }
            }
        };

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.True(result.Valid);
    }

    [Fact]
    public void ApplyGameValidationRules_ForbidTag_PresentTag_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Tags = new List<string> { "forbidden_tag" };
        var rules = new List<ValidationRule>
        {
            new ValidationRule
            {
                RuleId = "test-forbid-tag",
                RuleType = ValidationRuleType.Forbid_tag,
                Severity = ValidationSeverity.Error,
                Config = new ValidationRuleConfig
                {
                    Tag = "forbidden_tag"
                }
            }
        };

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("forbidden_tag") && e.Message.Contains("forbidden"));
    }

    [Fact]
    public void ApplyGameValidationRules_RequireNodeType_Present_Passes()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.NodeType = NodeType.Marker;
        var rules = new List<ValidationRule>
        {
            new ValidationRule
            {
                RuleId = "test-require-node-type",
                RuleType = ValidationRuleType.Require_node_type,
                Severity = ValidationSeverity.Error,
                Config = new ValidationRuleConfig
                {
                    NodeType = "Marker"
                }
            }
        };

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.True(result.Valid);
    }

    [Fact]
    public void ApplyGameValidationRules_RequireNodeType_Missing_ReturnsError()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.NodeType = NodeType.Group;
        var rules = new List<ValidationRule>
        {
            new ValidationRule
            {
                RuleId = "test-require-node-type",
                RuleType = ValidationRuleType.Require_node_type,
                Severity = ValidationSeverity.Error,
                Config = new ValidationRuleConfig
                {
                    NodeType = "Marker"
                }
            }
        };

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Marker"));
    }

    [Fact]
    public void ApplyGameValidationRules_WarningSeverity_AddsToWarnings()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Tags = new List<string> { "forbidden_tag" };
        var rules = new List<ValidationRule>
        {
            new ValidationRule
            {
                RuleId = "test-warning",
                RuleType = ValidationRuleType.Forbid_tag,
                Severity = ValidationSeverity.Warning,
                Config = new ValidationRuleConfig
                {
                    Tag = "forbidden_tag"
                }
            }
        };

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.True(result.Valid); // Warnings don't make result invalid
        Assert.NotNull(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Message.Contains("forbidden_tag"));
    }

    [Fact]
    public void ApplyGameValidationRules_RequireTag_MinCount_Passes()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Tags = new List<string> { "spawn_point" };
        scene.Root.Children = new List<SceneNode>
        {
            new SceneNode
            {
                NodeId = Guid.NewGuid(),
                RefId = "child1",
                NodeType = NodeType.Group,
                LocalTransform = new Transform(),
                Tags = new List<string> { "spawn_point" }
            }
        };
        var rules = new List<ValidationRule>
        {
            new ValidationRule
            {
                RuleId = "test-min-count",
                RuleType = ValidationRuleType.Require_tag,
                Severity = ValidationSeverity.Error,
                Config = new ValidationRuleConfig
                {
                    Tag = "spawn_point",
                    MinCount = 2
                }
            }
        };

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.True(result.Valid);
    }

    [Fact]
    public void ApplyGameValidationRules_RequireTag_MaxCount_Exceeded()
    {
        // Arrange
        var scene = CreateValidScene();
        scene.Root.Tags = new List<string> { "limited_tag" };
        scene.Root.Children = new List<SceneNode>
        {
            new SceneNode
            {
                NodeId = Guid.NewGuid(),
                RefId = "child1",
                NodeType = NodeType.Group,
                LocalTransform = new Transform(),
                Tags = new List<string> { "limited_tag" }
            },
            new SceneNode
            {
                NodeId = Guid.NewGuid(),
                RefId = "child2",
                NodeType = NodeType.Group,
                LocalTransform = new Transform(),
                Tags = new List<string> { "limited_tag" }
            }
        };
        var rules = new List<ValidationRule>
        {
            new ValidationRule
            {
                RuleId = "test-max-count",
                RuleType = ValidationRuleType.Require_tag,
                Severity = ValidationSeverity.Error,
                Config = new ValidationRuleConfig
                {
                    Tag = "limited_tag",
                    MinCount = 1,
                    MaxCount = 1
                }
            }
        };

        // Act
        var result = _sut.ApplyGameValidationRules(scene, rules);

        // Assert
        Assert.False(result.Valid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("too many"));
    }

    #endregion

    #region MergeResults Tests

    [Fact]
    public void MergeResults_BothValid_TargetRemainsValid()
    {
        // Arrange
        var target = new ValidationResult { Valid = true };
        var source = new ValidationResult { Valid = true };

        // Act
        _sut.MergeResults(target, source);

        // Assert
        Assert.True(target.Valid);
    }

    [Fact]
    public void MergeResults_SourceHasErrors_TargetBecomesInvalid()
    {
        // Arrange
        var target = new ValidationResult { Valid = true };
        var source = new ValidationResult
        {
            Valid = false,
            Errors = new List<ValidationError>
            {
                new ValidationError { RuleId = "test", Message = "Error from source", Severity = ValidationSeverity.Error }
            }
        };

        // Act
        _sut.MergeResults(target, source);

        // Assert
        Assert.False(target.Valid);
        Assert.NotNull(target.Errors);
        Assert.Contains(target.Errors, e => e.Message == "Error from source");
    }

    [Fact]
    public void MergeResults_BothHaveErrors_CombinesAllErrors()
    {
        // Arrange
        var target = new ValidationResult
        {
            Valid = false,
            Errors = new List<ValidationError>
            {
                new ValidationError { RuleId = "a", Message = "Error A", Severity = ValidationSeverity.Error }
            }
        };
        var source = new ValidationResult
        {
            Valid = false,
            Errors = new List<ValidationError>
            {
                new ValidationError { RuleId = "b", Message = "Error B", Severity = ValidationSeverity.Error }
            }
        };

        // Act
        _sut.MergeResults(target, source);

        // Assert
        Assert.False(target.Valid);
        Assert.Equal(2, target.Errors?.Count);
    }

    [Fact]
    public void MergeResults_CombinesWarnings()
    {
        // Arrange
        var target = new ValidationResult
        {
            Valid = true,
            Warnings = new List<ValidationError>
            {
                new ValidationError { RuleId = "w1", Message = "Warning A", Severity = ValidationSeverity.Warning }
            }
        };
        var source = new ValidationResult
        {
            Valid = true,
            Warnings = new List<ValidationError>
            {
                new ValidationError { RuleId = "w2", Message = "Warning B", Severity = ValidationSeverity.Warning }
            }
        };

        // Act
        _sut.MergeResults(target, source);

        // Assert
        Assert.True(target.Valid);
        Assert.Equal(2, target.Warnings?.Count);
    }

    [Fact]
    public void MergeResults_SourceHasNullCollections_HandlesGracefully()
    {
        // Arrange
        var target = new ValidationResult { Valid = true };
        var source = new ValidationResult { Valid = true };

        // Act
        _sut.MergeResults(target, source);

        // Assert
        Assert.True(target.Valid);
        Assert.Null(target.Errors);
        Assert.Null(target.Warnings);
    }

    #endregion

    #region CollectAllNodes Tests

    [Fact]
    public void CollectAllNodes_SingleNode_ReturnsOneNode()
    {
        // Arrange
        var root = CreateValidNode("root");

        // Act
        var nodes = _sut.CollectAllNodes(root);

        // Assert
        Assert.Single(nodes);
        Assert.Equal("root", nodes[0].RefId);
    }

    [Fact]
    public void CollectAllNodes_NodeWithChildren_ReturnsAll()
    {
        // Arrange
        var root = CreateValidNode("root");
        root.Children = new List<SceneNode>
        {
            CreateValidNode("child1"),
            CreateValidNode("child2")
        };

        // Act
        var nodes = _sut.CollectAllNodes(root);

        // Assert
        Assert.Equal(3, nodes.Count);
    }

    [Fact]
    public void CollectAllNodes_NestedHierarchy_FlattensAll()
    {
        // Arrange
        var root = CreateValidNode("root");
        var child = CreateValidNode("child");
        var grandchild1 = CreateValidNode("grandchild1");
        var grandchild2 = CreateValidNode("grandchild2");

        child.Children = new List<SceneNode> { grandchild1, grandchild2 };
        root.Children = new List<SceneNode> { child };

        // Act
        var nodes = _sut.CollectAllNodes(root);

        // Assert
        Assert.Equal(4, nodes.Count);
        Assert.Contains(nodes, n => n.RefId == "root");
        Assert.Contains(nodes, n => n.RefId == "child");
        Assert.Contains(nodes, n => n.RefId == "grandchild1");
        Assert.Contains(nodes, n => n.RefId == "grandchild2");
    }

    [Fact]
    public void CollectAllNodes_EmptyChildren_HandlesGracefully()
    {
        // Arrange
        var root = CreateValidNode("root");
        root.Children = new List<SceneNode>();

        // Act
        var nodes = _sut.CollectAllNodes(root);

        // Assert
        Assert.Single(nodes);
    }

    [Fact]
    public void CollectAllNodes_NullChildren_HandlesGracefully()
    {
        // Arrange
        var root = CreateValidNode("root");
        // Don't set Children at all - it's already null by default from CreateValidNode

        // Act
        var nodes = _sut.CollectAllNodes(root);

        // Assert
        Assert.Single(nodes);
    }

    [Fact]
    public void CollectAllNodes_DeeplyNested_CollectsAllLevels()
    {
        // Arrange
        var root = CreateValidNode("level0");
        var current = root;
        for (int i = 1; i < 10; i++)
        {
            var child = CreateValidNode($"level{i}");
            current.Children = new List<SceneNode> { child };
            current = child;
        }

        // Act
        var nodes = _sut.CollectAllNodes(root);

        // Assert
        Assert.Equal(10, nodes.Count);
    }

    #endregion

    #region Helper Methods

    private static Scene CreateValidScene()
    {
        return new Scene
        {
            SceneId = Guid.NewGuid(),
            Version = "1.0.0",
            Root = new SceneNode
            {
                NodeId = Guid.NewGuid(),
                RefId = "root",
                NodeType = NodeType.Group,
                LocalTransform = new Transform()
            }
        };
    }

    private static SceneNode CreateValidNode(string refId)
    {
        return new SceneNode
        {
            NodeId = Guid.NewGuid(),
            RefId = refId,
            NodeType = NodeType.Group,
            LocalTransform = new Transform()
        };
    }

    #endregion
}
