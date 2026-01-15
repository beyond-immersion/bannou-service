namespace BeyondImmersion.BannouService.Scene.Helpers;

/// <summary>
/// Validates scene structures and applies game-specific validation rules.
/// Extracted from SceneService for improved testability.
/// </summary>
public interface ISceneValidationService
{
    /// <summary>
    /// Validates the structural integrity of a scene.
    /// Checks sceneId, version format, root node, node count limits, refId uniqueness, and transforms.
    /// </summary>
    /// <param name="scene">The scene to validate.</param>
    /// <param name="maxNodeCount">Maximum allowed node count.</param>
    /// <returns>Validation result with errors and warnings.</returns>
    ValidationResult ValidateStructure(Scene scene, int maxNodeCount);

    /// <summary>
    /// Applies game-specific validation rules to a scene.
    /// </summary>
    /// <param name="scene">The scene to validate.</param>
    /// <param name="rules">The validation rules to apply.</param>
    /// <returns>Validation result with errors and warnings from rule application.</returns>
    ValidationResult ApplyGameValidationRules(Scene scene, IReadOnlyList<ValidationRule>? rules);

    /// <summary>
    /// Merges validation results from multiple validations into a target result.
    /// </summary>
    /// <param name="target">The target result to merge into (modified in place).</param>
    /// <param name="source">The source result to merge from.</param>
    void MergeResults(ValidationResult target, ValidationResult source);

    /// <summary>
    /// Collects all nodes in a scene into a flat list.
    /// </summary>
    /// <param name="root">The root node to start from.</param>
    /// <returns>List of all nodes in the scene hierarchy.</returns>
    List<SceneNode> CollectAllNodes(SceneNode root);
}
