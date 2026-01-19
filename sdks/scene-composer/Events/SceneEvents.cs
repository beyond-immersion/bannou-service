using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Math;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;

namespace BeyondImmersion.Bannou.SceneComposer.Events;

/// <summary>
/// Base class for scene composer events.
/// </summary>
public abstract class SceneComposerEvent : EventArgs
{
    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

/// <summary>
/// Event args for scene modifications.
/// </summary>
public class SceneModifiedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// Type of modification.
    /// </summary>
    public SceneModificationType ModificationType { get; }

    /// <summary>
    /// Affected nodes (if applicable).
    /// </summary>
    public IReadOnlyList<ComposerSceneNode> AffectedNodes { get; }

    /// <summary>
    /// Description of the modification.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Whether this modification can be undone.
    /// </summary>
    public bool IsUndoable { get; }

    /// <summary>Creates scene modified event args.</summary>
    public SceneModifiedEventArgs(
        SceneModificationType modificationType,
        IEnumerable<ComposerSceneNode> affectedNodes,
        string description,
        bool isUndoable = true)
    {
        ModificationType = modificationType;
        AffectedNodes = affectedNodes.ToList();
        Description = description;
        IsUndoable = isUndoable;
    }

    /// <summary>
    /// Create for a single node.
    /// </summary>
    public static SceneModifiedEventArgs ForNode(
        SceneModificationType type,
        ComposerSceneNode node,
        string description)
    {
        return new SceneModifiedEventArgs(type, new[] { node }, description);
    }
}

/// <summary>
/// Type of scene modification.
/// </summary>
public enum SceneModificationType
{
    /// <summary>One or more nodes were created.</summary>
    NodeCreated,

    /// <summary>One or more nodes were deleted.</summary>
    NodeDeleted,

    /// <summary>A node was reparented.</summary>
    NodeReparented,

    /// <summary>A node's transform changed.</summary>
    TransformChanged,

    /// <summary>A node's asset binding changed.</summary>
    AssetChanged,

    /// <summary>A node was renamed.</summary>
    NodeRenamed,

    /// <summary>A node's properties changed.</summary>
    PropertyChanged,

    /// <summary>Scene metadata changed.</summary>
    MetadataChanged,

    /// <summary>Multiple changes (compound operation).</summary>
    Compound,

    /// <summary>Scene was loaded.</summary>
    SceneLoaded,

    /// <summary>Scene was saved/committed.</summary>
    SceneSaved,

    /// <summary>Changes were discarded.</summary>
    ChangesDiscarded,

    /// <summary>Undo operation performed.</summary>
    Undo,

    /// <summary>Redo operation performed.</summary>
    Redo
}

/// <summary>
/// Event args for selection changes.
/// </summary>
public class SelectionChangedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// Currently selected nodes.
    /// </summary>
    public IReadOnlyList<ComposerSceneNode> SelectedNodes { get; }

    /// <summary>
    /// Nodes added to selection.
    /// </summary>
    public IReadOnlyList<ComposerSceneNode> Added { get; }

    /// <summary>
    /// Nodes removed from selection.
    /// </summary>
    public IReadOnlyList<ComposerSceneNode> Removed { get; }

    /// <summary>
    /// The primary (last selected) node.
    /// </summary>
    public ComposerSceneNode? PrimaryNode { get; }

    /// <summary>Creates selection changed event args.</summary>
    public SelectionChangedEventArgs(
        IReadOnlyList<ComposerSceneNode> selectedNodes,
        IReadOnlyList<ComposerSceneNode> added,
        IReadOnlyList<ComposerSceneNode> removed,
        ComposerSceneNode? primaryNode)
    {
        SelectedNodes = selectedNodes;
        Added = added;
        Removed = removed;
        PrimaryNode = primaryNode;
    }
}

/// <summary>
/// Event args for node created.
/// </summary>
public class NodeCreatedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// The created node.
    /// </summary>
    public ComposerSceneNode Node { get; }

    /// <summary>
    /// Parent of the created node.
    /// </summary>
    public ComposerSceneNode? Parent { get; }

    /// <summary>Creates node created event args.</summary>
    public NodeCreatedEventArgs(ComposerSceneNode node, ComposerSceneNode? parent)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Parent = parent;
    }
}

/// <summary>
/// Event args for node deleted.
/// </summary>
public class NodeDeletedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// The deleted node.
    /// </summary>
    public ComposerSceneNode Node { get; }

    /// <summary>
    /// Former parent of the deleted node.
    /// </summary>
    public ComposerSceneNode? FormerParent { get; }

    /// <summary>Creates node deleted event args.</summary>
    public NodeDeletedEventArgs(ComposerSceneNode node, ComposerSceneNode? formerParent)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        FormerParent = formerParent;
    }
}

/// <summary>
/// Event args for node reparented.
/// </summary>
public class NodeReparentedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// The reparented node.
    /// </summary>
    public ComposerSceneNode Node { get; }

    /// <summary>
    /// Previous parent.
    /// </summary>
    public ComposerSceneNode? OldParent { get; }

    /// <summary>
    /// New parent.
    /// </summary>
    public ComposerSceneNode? NewParent { get; }

    /// <summary>Creates node reparented event args.</summary>
    public NodeReparentedEventArgs(ComposerSceneNode node, ComposerSceneNode? oldParent, ComposerSceneNode? newParent)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        OldParent = oldParent;
        NewParent = newParent;
    }
}

/// <summary>
/// Event args for transform changed.
/// </summary>
public class TransformChangedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// The node whose transform changed.
    /// </summary>
    public ComposerSceneNode Node { get; }

    /// <summary>
    /// Previous transform.
    /// </summary>
    public Transform OldTransform { get; }

    /// <summary>
    /// New transform.
    /// </summary>
    public Transform NewTransform { get; }

    /// <summary>Creates transform changed event args.</summary>
    public TransformChangedEventArgs(ComposerSceneNode node, Transform oldTransform, Transform newTransform)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        OldTransform = oldTransform;
        NewTransform = newTransform;
    }
}

/// <summary>
/// Event args for asset binding changed.
/// </summary>
public class AssetChangedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// The node whose asset changed.
    /// </summary>
    public ComposerSceneNode Node { get; }

    /// <summary>
    /// Previous asset reference.
    /// </summary>
    public AssetReference OldAsset { get; }

    /// <summary>
    /// New asset reference.
    /// </summary>
    public AssetReference NewAsset { get; }

    /// <summary>Creates asset changed event args.</summary>
    public AssetChangedEventArgs(ComposerSceneNode node, AssetReference oldAsset, AssetReference newAsset)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        OldAsset = oldAsset;
        NewAsset = newAsset;
    }
}

/// <summary>
/// Event args for scene loaded.
/// </summary>
public class SceneLoadedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// The loaded scene.
    /// </summary>
    public ComposerScene Scene { get; }

    /// <summary>
    /// Time taken to load.
    /// </summary>
    public TimeSpan LoadTime { get; }

    /// <summary>
    /// Whether references were resolved.
    /// </summary>
    public bool ReferencesResolved { get; }

    /// <summary>Creates scene loaded event args.</summary>
    public SceneLoadedEventArgs(ComposerScene scene, TimeSpan loadTime, bool referencesResolved)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        LoadTime = loadTime;
        ReferencesResolved = referencesResolved;
    }
}

/// <summary>
/// Event args for scene saved.
/// </summary>
public class SceneSavedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// The saved scene.
    /// </summary>
    public ComposerScene Scene { get; }

    /// <summary>
    /// New version after save.
    /// </summary>
    public string NewVersion { get; }

    /// <summary>
    /// Commit comment.
    /// </summary>
    public string? Comment { get; }

    /// <summary>Creates scene saved event args.</summary>
    public SceneSavedEventArgs(ComposerScene scene, string newVersion, string? comment)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        NewVersion = newVersion ?? throw new ArgumentNullException(nameof(newVersion));
        Comment = comment;
    }
}

/// <summary>
/// Event args for checkout state changes.
/// </summary>
public class CheckoutStateChangedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// Whether the scene is now checked out.
    /// </summary>
    public bool IsCheckedOut { get; }

    /// <summary>
    /// Checkout expiration time (if checked out).
    /// </summary>
    public DateTime? ExpiresAt { get; }

    /// <summary>
    /// User who has the scene checked out.
    /// </summary>
    public string? CheckedOutBy { get; }

    /// <summary>Creates checkout state changed event args.</summary>
    public CheckoutStateChangedEventArgs(bool isCheckedOut, DateTime? expiresAt = null, string? checkedOutBy = null)
    {
        IsCheckedOut = isCheckedOut;
        ExpiresAt = expiresAt;
        CheckedOutBy = checkedOutBy;
    }
}

/// <summary>
/// Event args for validation completed.
/// </summary>
public class ValidationCompletedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// Validation result.
    /// </summary>
    public ValidationResult Result { get; }

    /// <summary>Creates validation completed event args.</summary>
    public ValidationCompletedEventArgs(ValidationResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }
}

/// <summary>
/// Event args for undo/redo state changed.
/// </summary>
public class UndoRedoStateChangedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// Whether undo is available.
    /// </summary>
    public bool CanUndo { get; }

    /// <summary>
    /// Whether redo is available.
    /// </summary>
    public bool CanRedo { get; }

    /// <summary>
    /// Description of next undo operation.
    /// </summary>
    public string? UndoDescription { get; }

    /// <summary>
    /// Description of next redo operation.
    /// </summary>
    public string? RedoDescription { get; }

    /// <summary>Creates undo/redo state changed event args.</summary>
    public UndoRedoStateChangedEventArgs(bool canUndo, bool canRedo, string? undoDescription, string? redoDescription)
    {
        CanUndo = canUndo;
        CanRedo = canRedo;
        UndoDescription = undoDescription;
        RedoDescription = redoDescription;
    }
}

/// <summary>
/// Event args for dirty state changed.
/// </summary>
public class DirtyStateChangedEventArgs : SceneComposerEvent
{
    /// <summary>
    /// Whether the scene has unsaved changes.
    /// </summary>
    public bool IsDirty { get; }

    /// <summary>Creates dirty state changed event args.</summary>
    public DirtyStateChangedEventArgs(bool isDirty)
    {
        IsDirty = isDirty;
    }
}
