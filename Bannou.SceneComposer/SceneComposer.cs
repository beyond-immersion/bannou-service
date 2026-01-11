using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Commands;
using BeyondImmersion.Bannou.SceneComposer.Events;
using BeyondImmersion.Bannou.SceneComposer.Math;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;
using BeyondImmersion.Bannou.SceneComposer.Selection;
using BeyondImmersion.Bannou.SceneComposer.Validation;

namespace BeyondImmersion.Bannou.SceneComposer;

/// <summary>
/// Main orchestrator for scene composition operations.
/// Coordinates scene graph, selection, commands, and engine bridge.
/// </summary>
public class SceneComposer : ISceneComposer
{
    private readonly ISceneComposerBridge _bridge;
    private readonly ISceneServiceClient? _serviceClient;
    private readonly CommandStack _commandStack;
    private readonly SelectionManager _selectionManager;
    private readonly SceneValidator _validator;

    private ComposerScene? _currentScene;
    private bool _isDirty;
    private string? _checkoutSessionId;
    private DateTime? _checkoutExpiresAt;
    private CompoundCommandBuilder? _activeCompound;
    private int _compoundNestingLevel;

    /// <inheritdoc />
    public ComposerScene? CurrentScene => _currentScene;

    /// <inheritdoc />
    public bool IsDirty => _isDirty;

    /// <inheritdoc />
    public event EventHandler<SceneModifiedEventArgs>? SceneModified;

    /// <inheritdoc />
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// Raised when dirty state changes.
    /// </summary>
    public event EventHandler<DirtyStateChangedEventArgs>? DirtyStateChanged;

    /// <summary>
    /// Raised when undo/redo availability changes.
    /// </summary>
    public event EventHandler<UndoRedoStateChangedEventArgs>? UndoRedoStateChanged;

    /// <summary>
    /// Raised when checkout state changes.
    /// </summary>
    public event EventHandler<CheckoutStateChangedEventArgs>? CheckoutStateChanged;

    /// <summary>
    /// Create a new SceneComposer.
    /// </summary>
    /// <param name="bridge">Engine-specific bridge for rendering and interaction.</param>
    /// <param name="serviceClient">Optional service client for persistence. If null, offline mode.</param>
    /// <param name="validator">Optional pre-configured validator. If null, default validator is used.</param>
    public SceneComposer(
        ISceneComposerBridge bridge,
        ISceneServiceClient? serviceClient = null,
        SceneValidator? validator = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _serviceClient = serviceClient;
        _commandStack = new CommandStack();
        _selectionManager = new SelectionManager();
        _validator = validator ?? new SceneValidator();

        // Wire up selection events
        _selectionManager.SelectionChanged += OnSelectionChanged;

        // Wire up command stack events
        _commandStack.CommandExecuted += OnCommandExecuted;
        _commandStack.CommandUndone += OnCommandUndone;
        _commandStack.CommandRedone += OnCommandRedone;
    }

    #region Scene Lifecycle

    /// <inheritdoc />
    public ComposerScene NewScene(SceneType sceneType, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Scene name cannot be empty", nameof(name));

        CloseScene();

        _currentScene = new ComposerScene(Guid.NewGuid().ToString(), name, sceneType);
        _isDirty = true;

        RaiseSceneModified(SceneModificationType.SceneLoaded, Array.Empty<ComposerSceneNode>(), $"New scene: {name}");
        RaiseDirtyStateChanged();

        return _currentScene;
    }

    /// <inheritdoc />
    public async Task<ComposerScene> LoadSceneAsync(string sceneId, bool resolveReferences = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new ArgumentException("Scene ID cannot be empty", nameof(sceneId));

        if (_serviceClient == null)
            throw new InvalidOperationException("Cannot load scene: no service client configured");

        CloseScene();

        var startTime = DateTime.UtcNow;
        var response = await _serviceClient.GetSceneAsync(sceneId, resolveReferences, ct: ct);

        if (response?.Data == null)
            throw new InvalidOperationException($"Scene not found: {sceneId}");

        _currentScene = ConvertFromServiceResponse(response);
        _isDirty = false;

        // Create engine entities for all nodes
        await CreateEngineEntitiesAsync(_currentScene.RootNodes, ct);

        var loadTime = DateTime.UtcNow - startTime;
        RaiseSceneModified(SceneModificationType.SceneLoaded, Array.Empty<ComposerSceneNode>(), $"Loaded: {_currentScene.Name}");

        return _currentScene;
    }

    /// <inheritdoc />
    public void CloseScene()
    {
        if (_currentScene == null)
            return;

        // Clear selection
        _selectionManager.ClearSelection();

        // Destroy all engine entities
        foreach (var node in _currentScene.GetAllNodes())
        {
            _bridge.DestroyEntity(node.Id);
        }

        // Clear command history
        _commandStack.Clear();

        // Release checkout if active
        if (_checkoutSessionId != null && _serviceClient != null)
        {
            _ = _serviceClient.DiscardAsync(_currentScene.SceneId, _checkoutSessionId);
        }

        _currentScene = null;
        _isDirty = false;
        _checkoutSessionId = null;
        _checkoutExpiresAt = null;

        RaiseDirtyStateChanged();
        RaiseUndoRedoStateChanged();
    }

    #endregion

    #region Node Operations

    /// <inheritdoc />
    public ComposerSceneNode CreateNode(NodeType type, string name, ComposerSceneNode? parent = null)
    {
        var scene = EnsureSceneLoaded();

        var node = new ComposerSceneNode(type, name);

        var command = new CreateNodeCommand(
            scene,
            node,
            parent,
            insertIndex: null,
            onCreated: OnNodeCreated,
            onDeleted: OnNodeDeleted);

        ExecuteCommand(command);

        return node;
    }

    /// <inheritdoc />
    public void DeleteNode(ComposerSceneNode node, bool deleteChildren = true)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        var scene = EnsureSceneLoaded();

        var command = new DeleteNodeCommand(
            scene,
            node,
            deleteChildren,
            onCreated: OnNodeCreated,
            onDeleted: OnNodeDeleted);

        ExecuteCommand(command);
    }

    /// <inheritdoc />
    public void DeleteNodes(IEnumerable<ComposerSceneNode> nodes, bool deleteChildren = true)
    {
        var nodeList = nodes.ToList();
        if (nodeList.Count == 0) return;

        if (nodeList.Count == 1)
        {
            DeleteNode(nodeList[0], deleteChildren);
            return;
        }

        EnsureSceneLoaded();

        using var compound = BeginCompoundOperation($"Delete {nodeList.Count} nodes");

        // Delete in reverse hierarchy order to avoid parent issues
        var sorted = nodeList.OrderByDescending(n => n.GetDepth()).ToList();
        foreach (var node in sorted)
        {
            DeleteNode(node, deleteChildren);
        }
    }

    /// <inheritdoc />
    public void ReparentNode(ComposerSceneNode node, ComposerSceneNode? newParent, int? insertIndex = null)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        var scene = EnsureSceneLoaded();

        // Prevent parenting to self or descendant
        if (newParent != null && (newParent == node || node.IsAncestorOf(newParent)))
            throw new InvalidOperationException("Cannot parent node to itself or a descendant");

        var command = new ReparentNodeCommand(
            scene,
            node,
            newParent,
            insertIndex,
            onReparented: OnNodeReparented);

        ExecuteCommand(command);
    }

    /// <inheritdoc />
    public ComposerSceneNode DuplicateNode(ComposerSceneNode node, bool deepClone = true)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        var scene = EnsureSceneLoaded();

        var clone = node.Clone(deepClone);

        // Append " Copy" to name
        clone.Name = $"{node.Name} Copy";

        var command = new CreateNodeCommand(
            scene,
            clone,
            node.Parent,
            insertIndex: null,
            onCreated: n => OnNodeCreatedRecursive(n, deepClone),
            onDeleted: n => OnNodeDeletedRecursive(n, deepClone));

        ExecuteCommand(command);

        return clone;
    }

    /// <inheritdoc />
    public IReadOnlyList<ComposerSceneNode> DuplicateNodes(IEnumerable<ComposerSceneNode> nodes, bool deepClone = true)
    {
        var nodeList = nodes.ToList();
        if (nodeList.Count == 0) return Array.Empty<ComposerSceneNode>();

        if (nodeList.Count == 1)
            return new[] { DuplicateNode(nodeList[0], deepClone) };

        EnsureSceneLoaded();

        var clones = new List<ComposerSceneNode>();

        using var compound = BeginCompoundOperation($"Duplicate {nodeList.Count} nodes");

        foreach (var node in nodeList)
        {
            clones.Add(DuplicateNode(node, deepClone));
        }

        return clones;
    }

    #endregion

    #region Transform Operations

    /// <inheritdoc />
    public void SetLocalTransform(ComposerSceneNode node, Transform transform)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        EnsureSceneLoaded();

        var command = new TransformNodeCommand(
            node,
            transform,
            onTransformChanged: OnTransformChanged);

        ExecuteCommand(command);
    }

    /// <inheritdoc />
    public Transform GetWorldTransform(ComposerSceneNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        return node.GetWorldTransform();
    }

    /// <inheritdoc />
    public void TranslateNode(ComposerSceneNode node, Vector3 delta, CoordinateSpace space)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        EnsureSceneLoaded();

        var localDelta = delta;
        if (space == CoordinateSpace.World && node.Parent != null)
        {
            // Convert world delta to local space
            var parentWorldRot = node.Parent.GetWorldTransform().Rotation;
            localDelta = parentWorldRot.Inverse.Rotate(delta);
        }

        var newTransform = node.LocalTransform.WithPosition(node.LocalTransform.Position + localDelta);
        SetLocalTransform(node, newTransform);
    }

    /// <inheritdoc />
    public void TranslateNodes(IEnumerable<ComposerSceneNode> nodes, Vector3 delta, CoordinateSpace space)
    {
        var nodeList = nodes.ToList();
        if (nodeList.Count == 0) return;

        if (nodeList.Count == 1)
        {
            TranslateNode(nodeList[0], delta, space);
            return;
        }

        using var compound = BeginCompoundOperation($"Move {nodeList.Count} nodes");

        foreach (var node in nodeList)
        {
            TranslateNode(node, delta, space);
        }
    }

    /// <inheritdoc />
    public void RotateNode(ComposerSceneNode node, Quaternion delta, CoordinateSpace space)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        EnsureSceneLoaded();

        Quaternion newRotation;
        if (space == CoordinateSpace.World)
        {
            // Apply world rotation: delta * current
            var worldTransform = node.GetWorldTransform();
            var newWorldRot = delta * worldTransform.Rotation;

            // Convert back to local
            if (node.Parent != null)
            {
                var parentWorldRot = node.Parent.GetWorldTransform().Rotation;
                newRotation = parentWorldRot.Inverse * newWorldRot;
            }
            else
            {
                newRotation = newWorldRot;
            }
        }
        else
        {
            // Local: current * delta
            newRotation = node.LocalTransform.Rotation * delta;
        }

        var newTransform = node.LocalTransform.WithRotation(newRotation);
        SetLocalTransform(node, newTransform);
    }

    /// <inheritdoc />
    public void RotateNodes(IEnumerable<ComposerSceneNode> nodes, Quaternion delta, CoordinateSpace space, Vector3? pivot = null)
    {
        var nodeList = nodes.ToList();
        if (nodeList.Count == 0) return;

        if (nodeList.Count == 1 && pivot == null)
        {
            RotateNode(nodeList[0], delta, space);
            return;
        }

        using var compound = BeginCompoundOperation($"Rotate {nodeList.Count} nodes");

        var actualPivot = pivot ?? _selectionManager.GetSelectionCenter();

        foreach (var node in nodeList)
        {
            // Rotate position around pivot
            var worldPos = node.GetWorldTransform().Position;
            var offset = worldPos - actualPivot;
            var rotatedOffset = delta.Rotate(offset);
            var newWorldPos = actualPivot + rotatedOffset;

            // Convert to local position
            Vector3 newLocalPos;
            if (node.Parent != null)
            {
                newLocalPos = node.Parent.GetWorldTransform().InverseTransformPoint(newWorldPos);
            }
            else
            {
                newLocalPos = newWorldPos;
            }

            // Apply rotation to node
            var newTransform = node.LocalTransform
                .WithPosition(newLocalPos)
                .WithRotation(delta * node.LocalTransform.Rotation);

            SetLocalTransform(node, newTransform);
        }
    }

    /// <inheritdoc />
    public void ScaleNode(ComposerSceneNode node, Vector3 delta)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        EnsureSceneLoaded();

        var newScale = new Vector3(
            node.LocalTransform.Scale.X * delta.X,
            node.LocalTransform.Scale.Y * delta.Y,
            node.LocalTransform.Scale.Z * delta.Z);

        var newTransform = node.LocalTransform.WithScale(newScale);
        SetLocalTransform(node, newTransform);
    }

    /// <inheritdoc />
    public void ScaleNodes(IEnumerable<ComposerSceneNode> nodes, Vector3 delta, Vector3? pivot = null)
    {
        var nodeList = nodes.ToList();
        if (nodeList.Count == 0) return;

        if (nodeList.Count == 1 && pivot == null)
        {
            ScaleNode(nodeList[0], delta);
            return;
        }

        using var compound = BeginCompoundOperation($"Scale {nodeList.Count} nodes");

        var actualPivot = pivot ?? _selectionManager.GetSelectionCenter();

        foreach (var node in nodeList)
        {
            // Scale position relative to pivot
            var worldPos = node.GetWorldTransform().Position;
            var offset = worldPos - actualPivot;
            var scaledOffset = new Vector3(offset.X * delta.X, offset.Y * delta.Y, offset.Z * delta.Z);
            var newWorldPos = actualPivot + scaledOffset;

            // Convert to local position
            Vector3 newLocalPos;
            if (node.Parent != null)
            {
                newLocalPos = node.Parent.GetWorldTransform().InverseTransformPoint(newWorldPos);
            }
            else
            {
                newLocalPos = newWorldPos;
            }

            // Apply scale to node
            var newScale = new Vector3(
                node.LocalTransform.Scale.X * delta.X,
                node.LocalTransform.Scale.Y * delta.Y,
                node.LocalTransform.Scale.Z * delta.Z);

            var newTransform = node.LocalTransform
                .WithPosition(newLocalPos)
                .WithScale(newScale);

            SetLocalTransform(node, newTransform);
        }
    }

    #endregion

    #region Selection

    /// <inheritdoc />
    public IReadOnlyList<ComposerSceneNode> SelectedNodes => _selectionManager.SelectedNodes;

    /// <inheritdoc />
    public bool HasSelection => _selectionManager.HasSelection;

    /// <inheritdoc />
    public void Select(ComposerSceneNode node, SelectionMode mode = SelectionMode.Replace)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        _selectionManager.Select(node, mode);
    }

    /// <inheritdoc />
    public void Select(IEnumerable<ComposerSceneNode> nodes, SelectionMode mode = SelectionMode.Replace)
    {
        _selectionManager.Select(nodes, mode);
    }

    /// <inheritdoc />
    public void ClearSelection()
    {
        _selectionManager.ClearSelection();
    }

    /// <inheritdoc />
    public void SelectAll()
    {
        EnsureSceneLoaded();
        _selectionManager.Select(_currentScene!.GetAllNodes(), SelectionMode.Replace);
    }

    #endregion

    #region Asset Binding

    /// <inheritdoc />
    public void BindAsset(ComposerSceneNode node, AssetReference asset)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        EnsureSceneLoaded();

        var command = new BindAssetCommand(
            node,
            asset,
            onAssetChanged: OnAssetChanged);

        ExecuteCommand(command);
    }

    /// <inheritdoc />
    public void ClearAsset(ComposerSceneNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        BindAsset(node, AssetReference.None);
    }

    #endregion

    #region Undo/Redo

    /// <inheritdoc />
    public bool CanUndo => _commandStack.CanUndo;

    /// <inheritdoc />
    public bool CanRedo => _commandStack.CanRedo;

    /// <inheritdoc />
    public string? UndoDescription => _commandStack.CanUndo ? _commandStack.PeekUndo()?.Description : null;

    /// <inheritdoc />
    public string? RedoDescription => _commandStack.CanRedo ? _commandStack.PeekRedo()?.Description : null;

    /// <inheritdoc />
    public void Undo()
    {
        if (!CanUndo) return;

        _commandStack.Undo();
        RaiseSceneModified(SceneModificationType.Undo, Array.Empty<ComposerSceneNode>(), UndoDescription ?? "Undo");
    }

    /// <inheritdoc />
    public void Redo()
    {
        if (!CanRedo) return;

        _commandStack.Redo();
        RaiseSceneModified(SceneModificationType.Redo, Array.Empty<ComposerSceneNode>(), RedoDescription ?? "Redo");
    }

    /// <inheritdoc />
    public void ClearHistory()
    {
        _commandStack.Clear();
        RaiseUndoRedoStateChanged();
    }

    /// <inheritdoc />
    public IDisposable BeginCompoundOperation(string description)
    {
        _compoundNestingLevel++;

        if (_activeCompound == null)
        {
            _activeCompound = new CompoundCommandBuilder(description);
        }

        return new CompoundOperationScope(this);
    }

    private void EndCompoundOperation()
    {
        _compoundNestingLevel--;

        if (_compoundNestingLevel == 0 && _activeCompound != null)
        {
            var compound = _activeCompound.Build();
            _activeCompound = null;

            if (compound.Commands.Count > 0)
            {
                // Execute the compound as a single undoable operation
                // Commands were already executed individually, so just add to stack
                _commandStack.AddExecutedCommand(compound);
                MarkDirty();
            }
        }
    }

    #endregion

    #region Persistence

    /// <inheritdoc />
    public bool IsCheckedOut => _checkoutSessionId != null;

    /// <inheritdoc />
    public async Task<bool> CheckoutAsync(CancellationToken ct = default)
    {
        EnsureSceneLoaded();

        if (_serviceClient == null)
            throw new InvalidOperationException("Cannot checkout: no service client configured");

        if (IsCheckedOut)
            return true;

        var result = await _serviceClient.CheckoutAsync(_currentScene!.SceneId, ct);

        if (result.Success)
        {
            _checkoutSessionId = result.SessionId;
            _checkoutExpiresAt = result.ExpiresAt;
            CheckoutStateChanged?.Invoke(this, new CheckoutStateChangedEventArgs(true, result.ExpiresAt));
        }

        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> ExtendCheckoutAsync(CancellationToken ct = default)
    {
        EnsureSceneLoaded();

        if (_serviceClient == null || _checkoutSessionId == null)
            return false;

        var success = await _serviceClient.HeartbeatAsync(_currentScene!.SceneId, _checkoutSessionId, ct);

        if (success)
        {
            _checkoutExpiresAt = DateTime.UtcNow.AddMinutes(60); // Assume 60-min extension
        }

        return success;
    }

    /// <inheritdoc />
    public async Task<bool> CommitAsync(string? comment = null, CancellationToken ct = default)
    {
        EnsureSceneLoaded();

        if (_serviceClient == null)
            throw new InvalidOperationException("Cannot commit: no service client configured");

        if (!IsCheckedOut)
        {
            // Try to checkout first
            if (!await CheckoutAsync(ct))
                return false;
        }

        var sceneData = ConvertToSceneData(_currentScene!);
        var result = await _serviceClient.CommitAsync(
            _currentScene!.SceneId,
            _checkoutSessionId!,
            sceneData,
            comment,
            ct);

        if (result.Success)
        {
            _currentScene.Version = result.NewVersion ?? _currentScene.Version;
            _isDirty = false;
            _checkoutSessionId = null;
            _checkoutExpiresAt = null;

            RaiseSceneModified(SceneModificationType.SceneSaved, Array.Empty<ComposerSceneNode>(), $"Saved v{result.NewVersion}");
            RaiseDirtyStateChanged();
            CheckoutStateChanged?.Invoke(this, new CheckoutStateChangedEventArgs(false));
        }

        return result.Success;
    }

    /// <inheritdoc />
    public async Task DiscardAsync(CancellationToken ct = default)
    {
        EnsureSceneLoaded();

        if (_serviceClient != null && _checkoutSessionId != null)
        {
            await _serviceClient.DiscardAsync(_currentScene!.SceneId, _checkoutSessionId, ct);
        }

        _checkoutSessionId = null;
        _checkoutExpiresAt = null;

        // Reload scene to discard changes
        var sceneId = _currentScene!.SceneId;
        CloseScene();
        await LoadSceneAsync(sceneId, ct: ct);

        RaiseSceneModified(SceneModificationType.ChangesDiscarded, Array.Empty<ComposerSceneNode>(), "Changes discarded");
        CheckoutStateChanged?.Invoke(this, new CheckoutStateChangedEventArgs(false));
    }

    #endregion

    #region Validation

    /// <inheritdoc />
    public ValidationResult ValidateScene()
    {
        EnsureSceneLoaded();
        return _validator.Validate(_currentScene!);
    }

    /// <inheritdoc />
    public ValidationResult ValidateNode(ComposerSceneNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        EnsureSceneLoaded();
        return _validator.ValidateNode(node, _currentScene!);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Ensures a scene is loaded and returns it.
    /// </summary>
    /// <returns>The currently loaded scene.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no scene is loaded.</exception>
    private ComposerScene EnsureSceneLoaded()
    {
        return _currentScene ?? throw new InvalidOperationException("No scene is loaded");
    }

    private void ExecuteCommand(IEditorCommand command)
    {
        if (_activeCompound != null)
        {
            // In compound mode: execute and add to compound
            command.Execute();
            _activeCompound.Add(command);
            MarkDirty();
        }
        else
        {
            // Normal mode: execute via command stack
            _commandStack.Execute(command);
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (!_isDirty)
        {
            _isDirty = true;
            RaiseDirtyStateChanged();
        }
    }

    private async Task CreateEngineEntitiesAsync(IEnumerable<ComposerSceneNode> nodes, CancellationToken ct)
    {
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();

            var worldTransform = node.GetWorldTransform();
            var asset = node.Asset.IsValid ? (AssetReference?)node.Asset : null;

            _bridge.CreateEntity(node.Id, node.NodeType, worldTransform, asset);

            if (node.Parent != null)
            {
                _bridge.SetEntityParent(node.Id, node.Parent.Id);
            }

            if (node.Asset.IsValid)
            {
                await _bridge.SetEntityAssetAsync(node.Id, node.Asset, ct);
            }

            _bridge.SetEntityVisible(node.Id, node.IsVisible);

            // Recurse to children
            await CreateEngineEntitiesAsync(node.Children, ct);
        }
    }

    private void OnNodeCreated(ComposerSceneNode node)
    {
        var worldTransform = node.GetWorldTransform();
        var asset = node.Asset.IsValid ? (AssetReference?)node.Asset : null;

        _bridge.CreateEntity(node.Id, node.NodeType, worldTransform, asset);

        if (node.Parent != null)
        {
            _bridge.SetEntityParent(node.Id, node.Parent.Id);
        }

        if (node.Asset.IsValid)
        {
            _ = _bridge.SetEntityAssetAsync(node.Id, node.Asset);
        }

        RaiseSceneModified(SceneModificationType.NodeCreated, new[] { node }, $"Created {node.Name}");
    }

    private void OnNodeCreatedRecursive(ComposerSceneNode node, bool includeChildren)
    {
        OnNodeCreated(node);
        if (includeChildren)
        {
            foreach (var child in node.Children)
            {
                OnNodeCreatedRecursive(child, true);
            }
        }
    }

    private void OnNodeDeleted(ComposerSceneNode node)
    {
        _selectionManager.NotifyNodeDeleted(node);
        _bridge.DestroyEntity(node.Id);
        RaiseSceneModified(SceneModificationType.NodeDeleted, new[] { node }, $"Deleted {node.Name}");
    }

    private void OnNodeDeletedRecursive(ComposerSceneNode node, bool includeChildren)
    {
        if (includeChildren)
        {
            foreach (var child in node.Children)
            {
                OnNodeDeletedRecursive(child, true);
            }
        }
        OnNodeDeleted(node);
    }

    private void OnNodeReparented(ComposerSceneNode node, ComposerSceneNode? oldParent, ComposerSceneNode? newParent)
    {
        _bridge.SetEntityParent(node.Id, newParent?.Id);
        _bridge.UpdateEntityTransform(node.Id, node.GetWorldTransform());
        RaiseSceneModified(SceneModificationType.NodeReparented, new[] { node }, $"Reparented {node.Name}");
    }

    private void OnTransformChanged(ComposerSceneNode node, Transform oldTransform, Transform newTransform)
    {
        _bridge.UpdateEntityTransform(node.Id, node.GetWorldTransform());
        RaiseSceneModified(SceneModificationType.TransformChanged, new[] { node }, $"Transformed {node.Name}");
    }

    private void OnAssetChanged(ComposerSceneNode node, AssetReference oldAsset, AssetReference newAsset)
    {
        if (newAsset.IsValid)
        {
            _ = _bridge.SetEntityAssetAsync(node.Id, newAsset);
        }
        else
        {
            _bridge.ClearEntityAsset(node.Id);
        }

        RaiseSceneModified(SceneModificationType.AssetChanged, new[] { node }, $"Asset changed on {node.Name}");
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Update engine visualization
        foreach (var node in e.Removed)
        {
            _bridge.SetEntitySelected(node.Id, false);
        }

        foreach (var node in e.Added)
        {
            _bridge.SetEntitySelected(node.Id, true);
        }

        SelectionChanged?.Invoke(this, e);
    }

    private void OnCommandExecuted(object? sender, CommandExecutedEventArgs e)
    {
        RaiseUndoRedoStateChanged();
    }

    private void OnCommandUndone(object? sender, CommandExecutedEventArgs e)
    {
        RaiseUndoRedoStateChanged();
    }

    private void OnCommandRedone(object? sender, CommandExecutedEventArgs e)
    {
        RaiseUndoRedoStateChanged();
    }

    private void RaiseSceneModified(SceneModificationType type, IEnumerable<ComposerSceneNode> nodes, string description)
    {
        SceneModified?.Invoke(this, new SceneModifiedEventArgs(type, nodes, description));
    }

    private void RaiseDirtyStateChanged()
    {
        DirtyStateChanged?.Invoke(this, new DirtyStateChangedEventArgs(_isDirty));
    }

    private void RaiseUndoRedoStateChanged()
    {
        UndoRedoStateChanged?.Invoke(this, new UndoRedoStateChangedEventArgs(
            CanUndo,
            CanRedo,
            UndoDescription,
            RedoDescription));
    }

    #endregion

    #region Conversion Helpers

    private ComposerScene ConvertFromServiceResponse(SceneServiceResponse response)
    {
        var sceneType = Enum.TryParse<SceneType>(response.SceneType, true, out var st) ? st : SceneType.Region;
        var scene = new ComposerScene(response.SceneId, response.Name, sceneType)
        {
            Version = response.Version
        };

        if (response.Data != null)
        {
            foreach (var nodeData in response.Data.RootNodes)
            {
                var node = ConvertNodeFromData(nodeData);
                scene.AddRootNode(node);
                scene.RegisterNode(node);
            }

            foreach (var tag in response.Data.Tags)
            {
                scene.Tags.Add(tag);
            }
        }

        return scene;
    }

    private ComposerSceneNode ConvertNodeFromData(SceneNodeData data)
    {
        var nodeType = Enum.TryParse<NodeType>(data.NodeType, true, out var nt) ? nt : NodeType.Group;
        var node = new ComposerSceneNode(nodeType, data.Name, data.Id)
        {
            IsVisible = data.IsVisible,
            IsLocked = data.IsLocked
        };

        node.LocalTransform = new Transform(
            new Vector3(data.Transform.PositionX, data.Transform.PositionY, data.Transform.PositionZ),
            new Quaternion(data.Transform.RotationX, data.Transform.RotationY, data.Transform.RotationZ, data.Transform.RotationW),
            new Vector3(data.Transform.ScaleX, data.Transform.ScaleY, data.Transform.ScaleZ));

        if (data.Asset != null)
        {
            node.Asset = new AssetReference(data.Asset.BundleId, data.Asset.AssetId, data.Asset.VariantId);
        }

        foreach (var childData in data.Children)
        {
            var child = ConvertNodeFromData(childData);
            node.AddChild(child);
        }

        return node;
    }

    private SceneData ConvertToSceneData(ComposerScene scene)
    {
        return new SceneData
        {
            RootNodes = scene.RootNodes.Select(ConvertNodeToData).ToList(),
            Tags = new List<string>(scene.Tags)
        };
    }

    private SceneNodeData ConvertNodeToData(ComposerSceneNode node)
    {
        return new SceneNodeData
        {
            Id = node.Id,
            Name = node.Name,
            NodeType = node.NodeType.ToString().ToLowerInvariant(),
            Transform = new TransformData
            {
                PositionX = node.LocalTransform.Position.X,
                PositionY = node.LocalTransform.Position.Y,
                PositionZ = node.LocalTransform.Position.Z,
                RotationX = node.LocalTransform.Rotation.X,
                RotationY = node.LocalTransform.Rotation.Y,
                RotationZ = node.LocalTransform.Rotation.Z,
                RotationW = node.LocalTransform.Rotation.W,
                ScaleX = node.LocalTransform.Scale.X,
                ScaleY = node.LocalTransform.Scale.Y,
                ScaleZ = node.LocalTransform.Scale.Z
            },
            Asset = node.Asset.IsValid
                ? new AssetReferenceData
                {
                    BundleId = node.Asset.BundleId,
                    AssetId = node.Asset.AssetId,
                    VariantId = node.Asset.VariantId
                }
                : null,
            IsVisible = node.IsVisible,
            IsLocked = node.IsLocked,
            Children = node.Children.Select(ConvertNodeToData).ToList()
        };
    }

    #endregion

    #region Helper Classes

    private sealed class CompoundOperationScope : IDisposable
    {
        private readonly SceneComposer _composer;
        private bool _disposed;

        public CompoundOperationScope(SceneComposer composer)
        {
            _composer = composer;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _composer.EndCompoundOperation();
            }
        }
    }

    private sealed class CompoundCommandBuilder
    {
        private readonly string _description;
        private readonly List<IEditorCommand> _commands = new();

        public CompoundCommandBuilder(string description)
        {
            _description = description;
        }

        public void Add(IEditorCommand command)
        {
            _commands.Add(command);
        }

        public CompoundCommand Build()
        {
            var compound = new CompoundCommand(_description);
            foreach (var cmd in _commands)
            {
                compound.AddExecuted(cmd);
            }
            return compound;
        }
    }

    #endregion
}
