using BeyondImmersion.Bannou.SceneComposer.Events;
using BeyondImmersion.Bannou.SceneComposer.Math;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;

namespace BeyondImmersion.Bannou.SceneComposer.Abstractions;

/// <summary>
/// Main interface for the Scene Composer - orchestrates all scene editing operations.
/// Engine-agnostic; requires an ISceneComposerBridge for engine-specific rendering.
/// </summary>
public interface ISceneComposer
{
    /// <summary>
    /// The currently loaded scene, or null if no scene is open.
    /// </summary>
    ComposerScene? CurrentScene { get; }

    /// <summary>
    /// Whether the scene has unsaved modifications.
    /// </summary>
    bool IsDirty { get; }

    /// <summary>
    /// Raised when any modification is made to the scene.
    /// </summary>
    event EventHandler<SceneModifiedEventArgs>? SceneModified;

    /// <summary>
    /// Raised when selection changes.
    /// </summary>
    event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    #region Scene Lifecycle

    /// <summary>
    /// Create a new empty scene.
    /// </summary>
    /// <param name="sceneType">Type of scene to create.</param>
    /// <param name="name">Name for the scene.</param>
    /// <returns>The created scene.</returns>
    ComposerScene NewScene(SceneType sceneType, string name);

    /// <summary>
    /// Load a scene from the server.
    /// </summary>
    /// <param name="sceneId">Scene identifier.</param>
    /// <param name="resolveReferences">Whether to resolve reference nodes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded scene.</returns>
    Task<ComposerScene> LoadSceneAsync(string sceneId, bool resolveReferences = true, CancellationToken ct = default);

    /// <summary>
    /// Close the current scene, discarding unsaved changes.
    /// </summary>
    void CloseScene();

    #endregion

    #region Node Operations

    /// <summary>
    /// Create a new node in the scene.
    /// </summary>
    /// <param name="type">Type of node to create.</param>
    /// <param name="name">Name for the node.</param>
    /// <param name="parent">Parent node, or null for root-level.</param>
    /// <returns>The created node.</returns>
    ComposerSceneNode CreateNode(NodeType type, string name, ComposerSceneNode? parent = null);

    /// <summary>
    /// Delete a node from the scene.
    /// </summary>
    /// <param name="node">Node to delete.</param>
    /// <param name="deleteChildren">If false, children are reparented to the deleted node's parent.</param>
    void DeleteNode(ComposerSceneNode node, bool deleteChildren = true);

    /// <summary>
    /// Delete multiple nodes from the scene.
    /// </summary>
    /// <param name="nodes">Nodes to delete.</param>
    /// <param name="deleteChildren">If false, children are reparented.</param>
    void DeleteNodes(IEnumerable<ComposerSceneNode> nodes, bool deleteChildren = true);

    /// <summary>
    /// Move a node to a new parent.
    /// </summary>
    /// <param name="node">Node to move.</param>
    /// <param name="newParent">New parent node, or null for root-level.</param>
    /// <param name="insertIndex">Position among siblings, or null for end.</param>
    void ReparentNode(ComposerSceneNode node, ComposerSceneNode? newParent, int? insertIndex = null);

    /// <summary>
    /// Duplicate a node and optionally its children.
    /// </summary>
    /// <param name="node">Node to duplicate.</param>
    /// <param name="deepClone">Whether to duplicate children.</param>
    /// <returns>The duplicated node.</returns>
    ComposerSceneNode DuplicateNode(ComposerSceneNode node, bool deepClone = true);

    /// <summary>
    /// Duplicate multiple nodes.
    /// </summary>
    /// <param name="nodes">Nodes to duplicate.</param>
    /// <param name="deepClone">Whether to duplicate children.</param>
    /// <returns>The duplicated nodes.</returns>
    IReadOnlyList<ComposerSceneNode> DuplicateNodes(IEnumerable<ComposerSceneNode> nodes, bool deepClone = true);

    #endregion

    #region Transform Operations

    /// <summary>
    /// Set a node's local transform.
    /// </summary>
    /// <param name="node">Target node.</param>
    /// <param name="transform">New local transform.</param>
    void SetLocalTransform(ComposerSceneNode node, Transform transform);

    /// <summary>
    /// Get a node's world transform (computed from hierarchy).
    /// </summary>
    /// <param name="node">Target node.</param>
    /// <returns>World-space transform.</returns>
    Transform GetWorldTransform(ComposerSceneNode node);

    /// <summary>
    /// Translate a node.
    /// </summary>
    /// <param name="node">Target node.</param>
    /// <param name="delta">Translation amount.</param>
    /// <param name="space">Coordinate space.</param>
    void TranslateNode(ComposerSceneNode node, Vector3 delta, CoordinateSpace space);

    /// <summary>
    /// Translate multiple nodes together.
    /// </summary>
    /// <param name="nodes">Target nodes.</param>
    /// <param name="delta">Translation amount.</param>
    /// <param name="space">Coordinate space.</param>
    void TranslateNodes(IEnumerable<ComposerSceneNode> nodes, Vector3 delta, CoordinateSpace space);

    /// <summary>
    /// Rotate a node.
    /// </summary>
    /// <param name="node">Target node.</param>
    /// <param name="delta">Rotation to apply.</param>
    /// <param name="space">Coordinate space.</param>
    void RotateNode(ComposerSceneNode node, Quaternion delta, CoordinateSpace space);

    /// <summary>
    /// Rotate multiple nodes together.
    /// </summary>
    /// <param name="nodes">Target nodes.</param>
    /// <param name="delta">Rotation to apply.</param>
    /// <param name="space">Coordinate space.</param>
    /// <param name="pivot">Rotation pivot point in world space.</param>
    void RotateNodes(IEnumerable<ComposerSceneNode> nodes, Quaternion delta, CoordinateSpace space, Vector3? pivot = null);

    /// <summary>
    /// Scale a node.
    /// </summary>
    /// <param name="node">Target node.</param>
    /// <param name="delta">Scale factors to multiply.</param>
    void ScaleNode(ComposerSceneNode node, Vector3 delta);

    /// <summary>
    /// Scale multiple nodes together.
    /// </summary>
    /// <param name="nodes">Target nodes.</param>
    /// <param name="delta">Scale factors to multiply.</param>
    /// <param name="pivot">Scale pivot point in world space.</param>
    void ScaleNodes(IEnumerable<ComposerSceneNode> nodes, Vector3 delta, Vector3? pivot = null);

    #endregion

    #region Selection

    /// <summary>
    /// Currently selected nodes.
    /// </summary>
    IReadOnlyList<ComposerSceneNode> SelectedNodes { get; }

    /// <summary>
    /// Whether any nodes are selected.
    /// </summary>
    bool HasSelection { get; }

    /// <summary>
    /// Select a node.
    /// </summary>
    /// <param name="node">Node to select.</param>
    /// <param name="mode">How to combine with existing selection.</param>
    void Select(ComposerSceneNode node, SelectionMode mode = SelectionMode.Replace);

    /// <summary>
    /// Select multiple nodes.
    /// </summary>
    /// <param name="nodes">Nodes to select.</param>
    /// <param name="mode">How to combine with existing selection.</param>
    void Select(IEnumerable<ComposerSceneNode> nodes, SelectionMode mode = SelectionMode.Replace);

    /// <summary>
    /// Clear all selection.
    /// </summary>
    void ClearSelection();

    /// <summary>
    /// Select all nodes in the scene.
    /// </summary>
    void SelectAll();

    #endregion

    #region Asset Binding

    /// <summary>
    /// Bind an asset to a node.
    /// </summary>
    /// <param name="node">Target node.</param>
    /// <param name="asset">Asset reference to bind.</param>
    void BindAsset(ComposerSceneNode node, AssetReference asset);

    /// <summary>
    /// Clear the asset binding from a node.
    /// </summary>
    /// <param name="node">Target node.</param>
    void ClearAsset(ComposerSceneNode node);

    #endregion

    #region Undo/Redo

    /// <summary>
    /// Whether an undo operation is available.
    /// </summary>
    bool CanUndo { get; }

    /// <summary>
    /// Whether a redo operation is available.
    /// </summary>
    bool CanRedo { get; }

    /// <summary>
    /// Description of the next undo operation.
    /// </summary>
    string? UndoDescription { get; }

    /// <summary>
    /// Description of the next redo operation.
    /// </summary>
    string? RedoDescription { get; }

    /// <summary>
    /// Undo the last operation.
    /// </summary>
    void Undo();

    /// <summary>
    /// Redo the last undone operation.
    /// </summary>
    void Redo();

    /// <summary>
    /// Clear undo/redo history.
    /// </summary>
    void ClearHistory();

    /// <summary>
    /// Begin a compound operation (multiple actions as one undo step).
    /// </summary>
    /// <param name="description">Description of the compound operation.</param>
    /// <returns>Disposable that ends the compound when disposed.</returns>
    IDisposable BeginCompoundOperation(string description);

    #endregion

    #region Persistence

    /// <summary>
    /// Whether the current scene is checked out for editing.
    /// </summary>
    bool IsCheckedOut { get; }

    /// <summary>
    /// Check out the scene for exclusive editing.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if checkout succeeded.</returns>
    Task<bool> CheckoutAsync(CancellationToken ct = default);

    /// <summary>
    /// Extend the checkout lock.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if extension succeeded.</returns>
    Task<bool> ExtendCheckoutAsync(CancellationToken ct = default);

    /// <summary>
    /// Commit changes and release the lock.
    /// </summary>
    /// <param name="comment">Optional commit comment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if commit succeeded.</returns>
    Task<bool> CommitAsync(string? comment = null, CancellationToken ct = default);

    /// <summary>
    /// Discard changes and release the lock.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task DiscardAsync(CancellationToken ct = default);

    #endregion

    #region Validation

    /// <summary>
    /// Validate the current scene.
    /// </summary>
    /// <returns>Validation results.</returns>
    ValidationResult ValidateScene();

    /// <summary>
    /// Validate a specific node.
    /// </summary>
    /// <param name="node">Node to validate.</param>
    /// <returns>Validation results.</returns>
    ValidationResult ValidateNode(ComposerSceneNode node);

    #endregion
}

/// <summary>
/// Type of scene document.
/// </summary>
public enum SceneType
{
    /// <summary>Large outdoor area.</summary>
    Region,
    /// <summary>City or town.</summary>
    City,
    /// <summary>City district or neighborhood.</summary>
    District,
    /// <summary>Property lot.</summary>
    Lot,
    /// <summary>Building structure.</summary>
    Building,
    /// <summary>Interior room.</summary>
    Room,
    /// <summary>Dungeon or instanced area.</summary>
    Dungeon,
    /// <summary>PvP or competitive arena.</summary>
    Arena,
    /// <summary>Vehicle interior.</summary>
    Vehicle,
    /// <summary>Reusable prefab template.</summary>
    Prefab,
    /// <summary>Cutscene or cinematic.</summary>
    Cutscene
}

/// <summary>
/// Type of scene node.
/// </summary>
public enum NodeType
{
    /// <summary>Empty transform node for organization.</summary>
    Group,
    /// <summary>Mesh/model rendering node.</summary>
    Mesh,
    /// <summary>Positional marker (spawn point, waypoint, etc.).</summary>
    Marker,
    /// <summary>Volume trigger (collision, zone, etc.).</summary>
    Volume,
    /// <summary>Particle or audio emitter.</summary>
    Emitter,
    /// <summary>Reference to another scene (nested prefab).</summary>
    Reference,
    /// <summary>Custom/extension node type.</summary>
    Custom
}

/// <summary>
/// How to combine new selection with existing.
/// </summary>
public enum SelectionMode
{
    /// <summary>Clear existing selection and select new nodes.</summary>
    Replace,
    /// <summary>Add new nodes to existing selection.</summary>
    Add,
    /// <summary>Remove nodes from existing selection.</summary>
    Remove,
    /// <summary>Toggle selection state of nodes.</summary>
    Toggle
}

/// <summary>
/// Coordinate space for transform operations.
/// </summary>
public enum CoordinateSpace
{
    /// <summary>Local space (relative to parent).</summary>
    Local,
    /// <summary>World space (global coordinates).</summary>
    World
}

/// <summary>
/// Reference to an asset in a bundle.
/// </summary>
public readonly struct AssetReference : IEquatable<AssetReference>
{
    /// <summary>Empty/null asset reference.</summary>
    public static readonly AssetReference None = default;

    /// <summary>Bundle containing the asset.</summary>
    public string BundleId { get; }

    /// <summary>Asset identifier within the bundle.</summary>
    public string AssetId { get; }

    /// <summary>Optional variant identifier.</summary>
    public string? VariantId { get; }

    /// <summary>
    /// Create an asset reference.
    /// </summary>
    public AssetReference(string bundleId, string assetId, string? variantId = null)
    {
        BundleId = bundleId ?? throw new ArgumentNullException(nameof(bundleId));
        AssetId = assetId ?? throw new ArgumentNullException(nameof(assetId));
        VariantId = variantId;
    }

    /// <summary>
    /// Whether this is a valid (non-empty) reference.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(BundleId) && !string.IsNullOrEmpty(AssetId);

    public bool Equals(AssetReference other) =>
        BundleId == other.BundleId && AssetId == other.AssetId && VariantId == other.VariantId;

    public override bool Equals(object? obj) =>
        obj is AssetReference other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(BundleId, AssetId, VariantId);

    public static bool operator ==(AssetReference left, AssetReference right) =>
        left.Equals(right);

    public static bool operator !=(AssetReference left, AssetReference right) =>
        !left.Equals(right);

    public override string ToString() =>
        VariantId != null
            ? $"{BundleId}/{AssetId}@{VariantId}"
            : $"{BundleId}/{AssetId}";
}

/// <summary>
/// Result of scene or node validation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Validation issues found.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>
    /// Whether validation passed with no errors.
    /// </summary>
    public bool IsValid => !Issues.Any(i => i.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Whether validation passed with no errors or warnings.
    /// </summary>
    public bool IsClean => Issues.Count == 0;

    /// <summary>
    /// Create a validation result.
    /// </summary>
    public ValidationResult(IEnumerable<ValidationIssue> issues)
    {
        Issues = issues.ToList();
    }

    /// <summary>
    /// Empty valid result.
    /// </summary>
    public static ValidationResult Valid { get; } = new(Array.Empty<ValidationIssue>());
}

/// <summary>
/// A single validation issue.
/// </summary>
public class ValidationIssue
{
    /// <summary>
    /// Severity of the issue.
    /// </summary>
    public ValidationSeverity Severity { get; }

    /// <summary>
    /// Issue code for programmatic handling.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Node the issue relates to, if applicable.
    /// </summary>
    public Guid? NodeId { get; }

    /// <summary>
    /// Create a validation issue.
    /// </summary>
    public ValidationIssue(ValidationSeverity severity, string code, string message, Guid? nodeId = null)
    {
        Severity = severity;
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        NodeId = nodeId;
    }
}

/// <summary>
/// Severity of a validation issue.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Informational hint.</summary>
    Info,
    /// <summary>Warning that may cause issues.</summary>
    Warning,
    /// <summary>Error that must be fixed before commit.</summary>
    Error
}
