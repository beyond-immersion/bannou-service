using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Math;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;

namespace BeyondImmersion.Bannou.SceneComposer.Commands;

/// <summary>
/// Command to create a new node.
/// </summary>
public class CreateNodeCommand : IEditorCommand
{
    private readonly ComposerScene _scene;
    private readonly ComposerSceneNode _node;
    private readonly ComposerSceneNode? _parent;
    private readonly int? _insertIndex;
    private readonly Action<ComposerSceneNode> _onCreated;
    private readonly Action<ComposerSceneNode> _onDeleted;

    public string Description => $"Create {_node.NodeType} '{_node.Name}'";

    public CreateNodeCommand(
        ComposerScene scene,
        ComposerSceneNode node,
        ComposerSceneNode? parent,
        int? insertIndex,
        Action<ComposerSceneNode> onCreated,
        Action<ComposerSceneNode> onDeleted)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _parent = parent;
        _insertIndex = insertIndex;
        _onCreated = onCreated ?? throw new ArgumentNullException(nameof(onCreated));
        _onDeleted = onDeleted ?? throw new ArgumentNullException(nameof(onDeleted));
    }

    public void Execute()
    {
        _scene.RegisterNode(_node);

        if (_parent != null)
        {
            _parent.AddChild(_node, _insertIndex);
        }
        else
        {
            _scene.AddRootNode(_node, _insertIndex);
        }

        _onCreated(_node);
    }

    public void Undo()
    {
        _onDeleted(_node);

        if (_parent != null)
        {
            _parent.RemoveChild(_node);
        }
        else
        {
            _scene.RemoveRootNode(_node);
        }

        _scene.UnregisterNode(_node);
    }

    public bool CanMergeWith(IEditorCommand other) => false;
    public bool TryMerge(IEditorCommand other) => false;
}

/// <summary>
/// Command to delete a node.
/// </summary>
public class DeleteNodeCommand : IEditorCommand
{
    private readonly ComposerScene _scene;
    private readonly ComposerSceneNode _node;
    private readonly ComposerSceneNode? _parent;
    private readonly int _siblingIndex;
    private readonly bool _deleteChildren;
    private readonly List<(ComposerSceneNode child, int index)> _orphanedChildren = new();
    private readonly Action<ComposerSceneNode> _onCreated;
    private readonly Action<ComposerSceneNode> _onDeleted;

    public string Description => $"Delete '{_node.Name}'";

    public DeleteNodeCommand(
        ComposerScene scene,
        ComposerSceneNode node,
        bool deleteChildren,
        Action<ComposerSceneNode> onCreated,
        Action<ComposerSceneNode> onDeleted)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _deleteChildren = deleteChildren;
        _parent = node.Parent;
        _siblingIndex = _parent?.GetChildIndex(node) ?? _scene.GetRootNodeIndex(node);
        _onCreated = onCreated ?? throw new ArgumentNullException(nameof(onCreated));
        _onDeleted = onDeleted ?? throw new ArgumentNullException(nameof(onDeleted));
    }

    public void Execute()
    {
        // If not deleting children, reparent them first
        if (!_deleteChildren)
        {
            _orphanedChildren.Clear();
            var children = _node.Children.ToList();
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                _orphanedChildren.Add((child, i));
                _node.RemoveChild(child);

                if (_parent != null)
                {
                    _parent.AddChild(child, _siblingIndex);
                }
                else
                {
                    _scene.AddRootNode(child, _siblingIndex);
                }
            }
        }
        else
        {
            // Notify deletion of all descendants
            foreach (var descendant in _scene.GetDescendants(_node).Reverse())
            {
                _onDeleted(descendant);
                _scene.UnregisterNode(descendant);
            }
        }

        // Remove the node
        _onDeleted(_node);

        if (_parent != null)
        {
            _parent.RemoveChild(_node);
        }
        else
        {
            _scene.RemoveRootNode(_node);
        }

        _scene.UnregisterNode(_node);
    }

    public void Undo()
    {
        // Re-add the node
        _scene.RegisterNode(_node);

        if (_parent != null)
        {
            _parent.AddChild(_node, _siblingIndex);
        }
        else
        {
            _scene.AddRootNode(_node, _siblingIndex);
        }

        _onCreated(_node);

        if (!_deleteChildren)
        {
            // Restore children to their original parent
            foreach (var (child, index) in _orphanedChildren)
            {
                if (_parent != null)
                {
                    _parent.RemoveChild(child);
                }
                else
                {
                    _scene.RemoveRootNode(child);
                }

                _node.AddChild(child, index);
            }
        }
        else
        {
            // Re-register and notify creation of all descendants
            foreach (var descendant in _scene.GetDescendants(_node))
            {
                _scene.RegisterNode(descendant);
                _onCreated(descendant);
            }
        }
    }

    public bool CanMergeWith(IEditorCommand other) => false;
    public bool TryMerge(IEditorCommand other) => false;
}

/// <summary>
/// Command to change a node's parent.
/// </summary>
public class ReparentNodeCommand : IEditorCommand
{
    private readonly ComposerScene _scene;
    private readonly ComposerSceneNode _node;
    private readonly ComposerSceneNode? _oldParent;
    private readonly ComposerSceneNode? _newParent;
    private readonly int _oldIndex;
    private readonly int? _newIndex;
    private readonly Action<ComposerSceneNode> _onReparented;

    public string Description => $"Move '{_node.Name}'";

    public ReparentNodeCommand(
        ComposerScene scene,
        ComposerSceneNode node,
        ComposerSceneNode? newParent,
        int? insertIndex,
        Action<ComposerSceneNode> onReparented)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _newParent = newParent;
        _newIndex = insertIndex;
        _oldParent = node.Parent;
        _oldIndex = _oldParent?.GetChildIndex(node) ?? _scene.GetRootNodeIndex(node);
        _onReparented = onReparented ?? throw new ArgumentNullException(nameof(onReparented));
    }

    public void Execute()
    {
        // Remove from old parent
        if (_oldParent != null)
        {
            _oldParent.RemoveChild(_node);
        }
        else
        {
            _scene.RemoveRootNode(_node);
        }

        // Add to new parent
        if (_newParent != null)
        {
            _newParent.AddChild(_node, _newIndex);
        }
        else
        {
            _scene.AddRootNode(_node, _newIndex);
        }

        _onReparented(_node);
    }

    public void Undo()
    {
        // Remove from new parent
        if (_newParent != null)
        {
            _newParent.RemoveChild(_node);
        }
        else
        {
            _scene.RemoveRootNode(_node);
        }

        // Add back to old parent
        if (_oldParent != null)
        {
            _oldParent.AddChild(_node, _oldIndex);
        }
        else
        {
            _scene.AddRootNode(_node, _oldIndex);
        }

        _onReparented(_node);
    }

    public bool CanMergeWith(IEditorCommand other) => false;
    public bool TryMerge(IEditorCommand other) => false;
}

/// <summary>
/// Command to change a node's transform.
/// </summary>
public class TransformNodeCommand : NodePropertyCommand<Transform>
{
    private readonly ComposerSceneNode _node;
    private readonly Action<ComposerSceneNode> _onTransformChanged;

    public override string Description => $"Transform '{_node.Name}'";

    public TransformNodeCommand(
        ComposerSceneNode node,
        Transform oldTransform,
        Transform newTransform,
        Action<ComposerSceneNode> onTransformChanged)
        : base(node.Id, oldTransform, newTransform)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _onTransformChanged = onTransformChanged ?? throw new ArgumentNullException(nameof(onTransformChanged));
    }

    public override void Execute()
    {
        _node.LocalTransform = NewValue;
        _onTransformChanged(_node);
    }

    public override void Undo()
    {
        _node.LocalTransform = OldValue;
        _onTransformChanged(_node);
    }
}

/// <summary>
/// Command to change a node's asset binding.
/// </summary>
public class BindAssetCommand : IEditorCommand
{
    private readonly ComposerSceneNode _node;
    private readonly AssetReference _oldAsset;
    private readonly AssetReference _newAsset;
    private readonly Action<ComposerSceneNode> _onAssetChanged;

    public string Description => _newAsset.IsValid
        ? $"Set asset on '{_node.Name}'"
        : $"Clear asset from '{_node.Name}'";

    public BindAssetCommand(
        ComposerSceneNode node,
        AssetReference newAsset,
        Action<ComposerSceneNode> onAssetChanged)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _oldAsset = node.Asset;
        _newAsset = newAsset;
        _onAssetChanged = onAssetChanged ?? throw new ArgumentNullException(nameof(onAssetChanged));
    }

    public void Execute()
    {
        _node.Asset = _newAsset;
        _onAssetChanged(_node);
    }

    public void Undo()
    {
        _node.Asset = _oldAsset;
        _onAssetChanged(_node);
    }

    public bool CanMergeWith(IEditorCommand other) => false;
    public bool TryMerge(IEditorCommand other) => false;
}

/// <summary>
/// Command to rename a node.
/// </summary>
public class RenameNodeCommand : NodePropertyCommand<string>
{
    private readonly ComposerSceneNode _node;
    private readonly Action<ComposerSceneNode>? _onRenamed;

    public override string Description => $"Rename to '{NewValue}'";

    public RenameNodeCommand(
        ComposerSceneNode node,
        string newName,
        Action<ComposerSceneNode>? onRenamed = null)
        : base(node.Id, node.Name, newName)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _onRenamed = onRenamed;
    }

    public override void Execute()
    {
        _node.Name = NewValue;
        _onRenamed?.Invoke(_node);
    }

    public override void Undo()
    {
        _node.Name = OldValue;
        _onRenamed?.Invoke(_node);
    }
}

/// <summary>
/// Command to change a node's visibility.
/// </summary>
public class SetVisibilityCommand : IEditorCommand
{
    private readonly ComposerSceneNode _node;
    private readonly bool _oldVisible;
    private readonly bool _newVisible;
    private readonly Action<ComposerSceneNode>? _onVisibilityChanged;

    public string Description => _newVisible ? $"Show '{_node.Name}'" : $"Hide '{_node.Name}'";

    public SetVisibilityCommand(
        ComposerSceneNode node,
        bool visible,
        Action<ComposerSceneNode>? onVisibilityChanged = null)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _oldVisible = node.IsVisible;
        _newVisible = visible;
        _onVisibilityChanged = onVisibilityChanged;
    }

    public void Execute()
    {
        _node.IsVisible = _newVisible;
        _onVisibilityChanged?.Invoke(_node);
    }

    public void Undo()
    {
        _node.IsVisible = _oldVisible;
        _onVisibilityChanged?.Invoke(_node);
    }

    public bool CanMergeWith(IEditorCommand other) =>
        other is SetVisibilityCommand vc && vc._node.Id == _node.Id;

    public bool TryMerge(IEditorCommand other) => false; // Visibility toggles shouldn't merge
}
