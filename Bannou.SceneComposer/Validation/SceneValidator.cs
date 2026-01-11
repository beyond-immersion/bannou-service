using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;

namespace BeyondImmersion.Bannou.SceneComposer.Validation;

/// <summary>
/// Validates scene composition for structural and logical correctness.
/// </summary>
public class SceneValidator
{
    private readonly List<IValidationRule> _rules = new();

    /// <summary>
    /// Maximum allowed hierarchy depth.
    /// </summary>
    public int MaxHierarchyDepth { get; set; } = 50;

    /// <summary>
    /// Maximum allowed children per node.
    /// </summary>
    public int MaxChildrenPerNode { get; set; } = 1000;

    /// <summary>
    /// Maximum allowed reference resolution depth.
    /// </summary>
    public int MaxReferenceDepth { get; set; } = 10;

    /// <summary>
    /// Whether to validate asset references exist.
    /// </summary>
    public bool ValidateAssetReferences { get; set; } = true;

    /// <summary>
    /// Create a scene validator with default rules.
    /// </summary>
    public SceneValidator()
    {
        // Add built-in rules
        AddRule(new HierarchyDepthRule());
        AddRule(new ChildCountRule());
        AddRule(new CircularReferenceRule());
        AddRule(new EmptyNameRule());
        AddRule(new DuplicateNodeIdRule());
        AddRule(new InvalidTransformRule());
    }

    /// <summary>
    /// Add a custom validation rule.
    /// </summary>
    public void AddRule(IValidationRule rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        _rules.Add(rule);
    }

    /// <summary>
    /// Remove a validation rule by type.
    /// </summary>
    public bool RemoveRule<T>() where T : IValidationRule
    {
        var rule = _rules.OfType<T>().FirstOrDefault();
        if (rule == null) return false;
        _rules.Remove(rule);
        return true;
    }

    /// <summary>
    /// Validate an entire scene.
    /// </summary>
    public ValidationResult Validate(ComposerScene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        var issues = new List<ValidationIssue>();
        var context = new ValidationContext(scene, this);

        // Run scene-level rules
        foreach (var rule in _rules)
        {
            issues.AddRange(rule.ValidateScene(scene, context));
        }

        // Run node-level rules for each node
        foreach (var node in scene.GetAllNodes())
        {
            foreach (var rule in _rules)
            {
                issues.AddRange(rule.ValidateNode(node, context));
            }
        }

        return new ValidationResult(issues);
    }

    /// <summary>
    /// Validate a single node.
    /// </summary>
    public ValidationResult ValidateNode(ComposerSceneNode node, ComposerScene scene)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        var issues = new List<ValidationIssue>();
        var context = new ValidationContext(scene, this);

        foreach (var rule in _rules)
        {
            issues.AddRange(rule.ValidateNode(node, context));
        }

        return new ValidationResult(issues);
    }

    /// <summary>
    /// Quick check if scene is valid (no errors).
    /// </summary>
    public bool IsValid(ComposerScene scene)
    {
        return Validate(scene).IsValid;
    }
}

/// <summary>
/// Interface for validation rules.
/// </summary>
public interface IValidationRule
{
    /// <summary>
    /// Validate the entire scene.
    /// </summary>
    IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context);

    /// <summary>
    /// Validate a single node.
    /// </summary>
    IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context);
}

/// <summary>
/// Context passed to validation rules.
/// </summary>
public class ValidationContext
{
    /// <summary>
    /// The scene being validated.
    /// </summary>
    public ComposerScene Scene { get; }

    /// <summary>
    /// The validator running the validation.
    /// </summary>
    public SceneValidator Validator { get; }

    /// <summary>
    /// Set of seen node IDs (for duplicate detection).
    /// </summary>
    public HashSet<Guid> SeenNodeIds { get; } = new();

    /// <summary>
    /// Set of seen reference scene IDs (for circular reference detection).
    /// </summary>
    public HashSet<string> SeenReferenceSceneIds { get; } = new();

    public ValidationContext(ComposerScene scene, SceneValidator validator)
    {
        Scene = scene;
        Validator = validator;
    }
}

#region Built-in Rules

/// <summary>
/// Validates hierarchy depth is within limits.
/// </summary>
public class HierarchyDepthRule : IValidationRule
{
    public IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context)
    {
        yield break; // Checked per-node
    }

    public IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context)
    {
        var depth = node.GetDepth();
        if (depth > context.Validator.MaxHierarchyDepth)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                "HIERARCHY_TOO_DEEP",
                $"Node '{node.Name}' is at depth {depth}, exceeding maximum of {context.Validator.MaxHierarchyDepth}",
                node.Id);
        }
        else if (depth > context.Validator.MaxHierarchyDepth * 0.8)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Warning,
                "HIERARCHY_DEEP",
                $"Node '{node.Name}' is at depth {depth}, approaching maximum of {context.Validator.MaxHierarchyDepth}",
                node.Id);
        }
    }
}

/// <summary>
/// Validates child count is within limits.
/// </summary>
public class ChildCountRule : IValidationRule
{
    public IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context)
    {
        yield break;
    }

    public IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context)
    {
        if (node.Children.Count > context.Validator.MaxChildrenPerNode)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                "TOO_MANY_CHILDREN",
                $"Node '{node.Name}' has {node.Children.Count} children, exceeding maximum of {context.Validator.MaxChildrenPerNode}",
                node.Id);
        }
    }
}

/// <summary>
/// Detects circular reference chains.
/// </summary>
public class CircularReferenceRule : IValidationRule
{
    public IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context)
    {
        yield break;
    }

    public IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context)
    {
        if (node.NodeType != NodeType.Reference || string.IsNullOrEmpty(node.ReferenceSceneId))
            yield break;

        // Check if this reference creates a circular chain
        if (context.SeenReferenceSceneIds.Contains(node.ReferenceSceneId))
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                "CIRCULAR_REFERENCE",
                $"Node '{node.Name}' creates a circular reference to scene '{node.ReferenceSceneId}'",
                node.Id);
        }

        // Check reference depth
        if (context.SeenReferenceSceneIds.Count >= context.Validator.MaxReferenceDepth)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                "REFERENCE_TOO_DEEP",
                $"Reference chain depth {context.SeenReferenceSceneIds.Count} exceeds maximum of {context.Validator.MaxReferenceDepth}",
                node.Id);
        }
    }
}

/// <summary>
/// Validates nodes have non-empty names.
/// </summary>
public class EmptyNameRule : IValidationRule
{
    public IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(scene.Name))
        {
            yield return new ValidationIssue(
                ValidationSeverity.Warning,
                "EMPTY_SCENE_NAME",
                "Scene has an empty name");
        }
    }

    public IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(node.Name))
        {
            yield return new ValidationIssue(
                ValidationSeverity.Warning,
                "EMPTY_NODE_NAME",
                "Node has an empty name",
                node.Id);
        }
    }
}

/// <summary>
/// Detects duplicate node IDs.
/// </summary>
public class DuplicateNodeIdRule : IValidationRule
{
    public IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context)
    {
        yield break;
    }

    public IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context)
    {
        if (!context.SeenNodeIds.Add(node.Id))
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                "DUPLICATE_NODE_ID",
                $"Duplicate node ID: {node.Id}",
                node.Id);
        }
    }
}

/// <summary>
/// Validates transforms don't have invalid values.
/// </summary>
public class InvalidTransformRule : IValidationRule
{
    public IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context)
    {
        yield break;
    }

    public IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context)
    {
        var transform = node.LocalTransform;

        // Check for NaN/Infinity in position
        if (ContainsInvalid(transform.Position))
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                "INVALID_POSITION",
                $"Node '{node.Name}' has invalid position values (NaN or Infinity)",
                node.Id);
        }

        // Check for zero scale (invisible node)
        if (transform.Scale.X == 0 || transform.Scale.Y == 0 || transform.Scale.Z == 0)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Warning,
                "ZERO_SCALE",
                $"Node '{node.Name}' has zero scale on at least one axis",
                node.Id);
        }

        // Check for negative scale
        if (transform.Scale.X < 0 || transform.Scale.Y < 0 || transform.Scale.Z < 0)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Info,
                "NEGATIVE_SCALE",
                $"Node '{node.Name}' has negative scale (may cause inverted normals)",
                node.Id);
        }

        // Check for very large scale
        if (System.Math.Abs(transform.Scale.X) > 1000 ||
            System.Math.Abs(transform.Scale.Y) > 1000 ||
            System.Math.Abs(transform.Scale.Z) > 1000)
        {
            yield return new ValidationIssue(
                ValidationSeverity.Warning,
                "LARGE_SCALE",
                $"Node '{node.Name}' has very large scale values",
                node.Id);
        }
    }

    private static bool ContainsInvalid(Math.Vector3 v)
    {
        return double.IsNaN(v.X) || double.IsNaN(v.Y) || double.IsNaN(v.Z) ||
               double.IsInfinity(v.X) || double.IsInfinity(v.Y) || double.IsInfinity(v.Z);
    }
}

#endregion

#region Optional Rules

/// <summary>
/// Validates asset references point to existing assets.
/// </summary>
public class AssetReferenceRule : IValidationRule
{
    private readonly Func<AssetReference, bool> _assetExists;

    /// <summary>
    /// Create an asset reference rule.
    /// </summary>
    /// <param name="assetExists">Function to check if an asset exists.</param>
    public AssetReferenceRule(Func<AssetReference, bool> assetExists)
    {
        _assetExists = assetExists ?? throw new ArgumentNullException(nameof(assetExists));
    }

    public IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context)
    {
        yield break;
    }

    public IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context)
    {
        if (!context.Validator.ValidateAssetReferences)
            yield break;

        if (node.Asset.IsValid && !_assetExists(node.Asset))
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                "MISSING_ASSET",
                $"Node '{node.Name}' references missing asset: {node.Asset}",
                node.Id);
        }
    }
}

/// <summary>
/// Validates reference nodes point to existing scenes.
/// </summary>
public class ReferenceSceneRule : IValidationRule
{
    private readonly Func<string, bool> _sceneExists;

    /// <summary>
    /// Create a reference scene rule.
    /// </summary>
    /// <param name="sceneExists">Function to check if a scene exists.</param>
    public ReferenceSceneRule(Func<string, bool> sceneExists)
    {
        _sceneExists = sceneExists ?? throw new ArgumentNullException(nameof(sceneExists));
    }

    public IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context)
    {
        yield break;
    }

    public IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context)
    {
        if (node.NodeType != NodeType.Reference)
            yield break;

        if (string.IsNullOrEmpty(node.ReferenceSceneId))
        {
            yield return new ValidationIssue(
                ValidationSeverity.Warning,
                "EMPTY_REFERENCE",
                $"Reference node '{node.Name}' has no scene ID specified",
                node.Id);
        }
        else if (!_sceneExists(node.ReferenceSceneId))
        {
            yield return new ValidationIssue(
                ValidationSeverity.Error,
                "MISSING_REFERENCE_SCENE",
                $"Reference node '{node.Name}' points to missing scene: {node.ReferenceSceneId}",
                node.Id);
        }
    }
}

/// <summary>
/// Validates attachment points have valid configurations.
/// </summary>
public class AttachmentPointRule : IValidationRule
{
    public IEnumerable<ValidationIssue> ValidateScene(ComposerScene scene, ValidationContext context)
    {
        yield break;
    }

    public IEnumerable<ValidationIssue> ValidateNode(ComposerSceneNode node, ValidationContext context)
    {
        var names = new HashSet<string>();

        foreach (var ap in node.AttachmentPoints)
        {
            if (string.IsNullOrWhiteSpace(ap.Name))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Warning,
                    "EMPTY_ATTACHMENT_NAME",
                    $"Node '{node.Name}' has an attachment point with no name",
                    node.Id);
            }
            else if (!names.Add(ap.Name))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error,
                    "DUPLICATE_ATTACHMENT_NAME",
                    $"Node '{node.Name}' has duplicate attachment point name: '{ap.Name}'",
                    node.Id);
            }
        }
    }
}

#endregion
