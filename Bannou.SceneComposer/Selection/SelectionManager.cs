using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Events;
using BeyondImmersion.Bannou.SceneComposer.Math;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;

namespace BeyondImmersion.Bannou.SceneComposer.Selection;

/// <summary>
/// Manages selection state for the Scene Composer.
/// Supports multi-selection, hover state, and selection pivot.
/// </summary>
public class SelectionManager
{
    private readonly HashSet<ComposerSceneNode> _selectedNodes = new();
    private readonly List<ComposerSceneNode> _selectionOrder = new(); // Maintains insertion order
    private ComposerSceneNode? _hoveredNode;
    private ComposerSceneNode? _primaryNode; // Last selected node (for operations)

    /// <summary>
    /// Raised when selection changes.
    /// </summary>
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// Raised when hover state changes.
    /// </summary>
    public event EventHandler<HoverChangedEventArgs>? HoverChanged;

    /// <summary>
    /// Currently selected nodes (in selection order).
    /// </summary>
    public IReadOnlyList<ComposerSceneNode> SelectedNodes => _selectionOrder;

    /// <summary>
    /// Number of selected nodes.
    /// </summary>
    public int Count => _selectedNodes.Count;

    /// <summary>
    /// Whether any nodes are selected.
    /// </summary>
    public bool HasSelection => _selectedNodes.Count > 0;

    /// <summary>
    /// The primary (last selected) node, or null if no selection.
    /// </summary>
    public ComposerSceneNode? PrimaryNode => _primaryNode;

    /// <summary>
    /// Currently hovered node, or null.
    /// </summary>
    public ComposerSceneNode? HoveredNode => _hoveredNode;

    /// <summary>
    /// Check if a node is selected.
    /// </summary>
    public bool IsSelected(ComposerSceneNode node)
    {
        return _selectedNodes.Contains(node);
    }

    /// <summary>
    /// Check if a node is selected by ID.
    /// </summary>
    public bool IsSelected(Guid nodeId)
    {
        return _selectedNodes.Any(n => n.Id == nodeId);
    }

    /// <summary>
    /// Select a node.
    /// </summary>
    /// <param name="node">Node to select.</param>
    /// <param name="mode">How to combine with existing selection.</param>
    public void Select(ComposerSceneNode node, SelectionMode mode = SelectionMode.Replace)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        if (node.IsLocked) return;

        var previousSelection = _selectionOrder.ToList();

        switch (mode)
        {
            case SelectionMode.Replace:
                _selectedNodes.Clear();
                _selectionOrder.Clear();
                _selectedNodes.Add(node);
                _selectionOrder.Add(node);
                _primaryNode = node;
                break;

            case SelectionMode.Add:
                if (_selectedNodes.Add(node))
                {
                    _selectionOrder.Add(node);
                }
                _primaryNode = node;
                break;

            case SelectionMode.Remove:
                if (_selectedNodes.Remove(node))
                {
                    _selectionOrder.Remove(node);
                    if (_primaryNode == node)
                    {
                        _primaryNode = _selectionOrder.Count > 0 ? _selectionOrder[^1] : null;
                    }
                }
                break;

            case SelectionMode.Toggle:
                if (_selectedNodes.Contains(node))
                {
                    _selectedNodes.Remove(node);
                    _selectionOrder.Remove(node);
                    if (_primaryNode == node)
                    {
                        _primaryNode = _selectionOrder.Count > 0 ? _selectionOrder[^1] : null;
                    }
                }
                else
                {
                    _selectedNodes.Add(node);
                    _selectionOrder.Add(node);
                    _primaryNode = node;
                }
                break;
        }

        RaiseSelectionChanged(previousSelection);
    }

    /// <summary>
    /// Select multiple nodes.
    /// </summary>
    /// <param name="nodes">Nodes to select.</param>
    /// <param name="mode">How to combine with existing selection.</param>
    public void Select(IEnumerable<ComposerSceneNode> nodes, SelectionMode mode = SelectionMode.Replace)
    {
        if (nodes == null) throw new ArgumentNullException(nameof(nodes));

        var nodeList = nodes.Where(n => !n.IsLocked).ToList();
        if (nodeList.Count == 0 && mode == SelectionMode.Replace)
        {
            ClearSelection();
            return;
        }

        var previousSelection = _selectionOrder.ToList();

        switch (mode)
        {
            case SelectionMode.Replace:
                _selectedNodes.Clear();
                _selectionOrder.Clear();
                foreach (var node in nodeList)
                {
                    if (_selectedNodes.Add(node))
                    {
                        _selectionOrder.Add(node);
                    }
                }
                _primaryNode = nodeList.Count > 0 ? nodeList[^1] : null;
                break;

            case SelectionMode.Add:
                foreach (var node in nodeList)
                {
                    if (_selectedNodes.Add(node))
                    {
                        _selectionOrder.Add(node);
                    }
                }
                if (nodeList.Count > 0)
                {
                    _primaryNode = nodeList[^1];
                }
                break;

            case SelectionMode.Remove:
                foreach (var node in nodeList)
                {
                    if (_selectedNodes.Remove(node))
                    {
                        _selectionOrder.Remove(node);
                    }
                }
                if (_primaryNode != null && !_selectedNodes.Contains(_primaryNode))
                {
                    _primaryNode = _selectionOrder.Count > 0 ? _selectionOrder[^1] : null;
                }
                break;

            case SelectionMode.Toggle:
                foreach (var node in nodeList)
                {
                    if (_selectedNodes.Contains(node))
                    {
                        _selectedNodes.Remove(node);
                        _selectionOrder.Remove(node);
                    }
                    else
                    {
                        _selectedNodes.Add(node);
                        _selectionOrder.Add(node);
                    }
                }
                if (_primaryNode != null && !_selectedNodes.Contains(_primaryNode))
                {
                    _primaryNode = _selectionOrder.Count > 0 ? _selectionOrder[^1] : null;
                }
                else if (nodeList.Count > 0 && _selectedNodes.Contains(nodeList[^1]))
                {
                    _primaryNode = nodeList[^1];
                }
                break;
        }

        RaiseSelectionChanged(previousSelection);
    }

    /// <summary>
    /// Clear all selection.
    /// </summary>
    public void ClearSelection()
    {
        if (_selectedNodes.Count == 0) return;

        var previousSelection = _selectionOrder.ToList();

        _selectedNodes.Clear();
        _selectionOrder.Clear();
        _primaryNode = null;

        RaiseSelectionChanged(previousSelection);
    }

    /// <summary>
    /// Select all nodes in a scene.
    /// </summary>
    public void SelectAll(ComposerScene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        Select(scene.GetAllNodes().Where(n => !n.IsLocked));
    }

    /// <summary>
    /// Set the hovered node.
    /// </summary>
    public void SetHovered(ComposerSceneNode? node)
    {
        if (_hoveredNode == node) return;

        var previous = _hoveredNode;
        _hoveredNode = node;

        HoverChanged?.Invoke(this, new HoverChangedEventArgs(previous, node));
    }

    /// <summary>
    /// Get the center of the current selection (average position).
    /// </summary>
    public Vector3 GetSelectionCenter()
    {
        if (_selectedNodes.Count == 0)
            return Vector3.Zero;

        var sum = Vector3.Zero;
        foreach (var node in _selectedNodes)
        {
            sum = sum + node.GetWorldTransform().Position;
        }
        return sum / _selectedNodes.Count;
    }

    /// <summary>
    /// Get the bounds of the current selection.
    /// </summary>
    public (Vector3 min, Vector3 max) GetSelectionBounds()
    {
        if (_selectedNodes.Count == 0)
            return (Vector3.Zero, Vector3.Zero);

        var min = new Vector3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vector3(double.MinValue, double.MinValue, double.MinValue);

        foreach (var node in _selectedNodes)
        {
            var pos = node.GetWorldTransform().Position;
            min = Vector3.Min(min, pos);
            max = Vector3.Max(max, pos);
        }

        return (min, max);
    }

    /// <summary>
    /// Notify that a node was deleted (remove from selection if present).
    /// </summary>
    internal void NotifyNodeDeleted(ComposerSceneNode node)
    {
        if (_selectedNodes.Remove(node))
        {
            _selectionOrder.Remove(node);
            if (_primaryNode == node)
            {
                _primaryNode = _selectionOrder.Count > 0 ? _selectionOrder[^1] : null;
            }
            // Don't raise event - deletion events handle UI updates
        }

        if (_hoveredNode == node)
        {
            _hoveredNode = null;
        }
    }

    /// <summary>
    /// Restore selection state (for undo/redo).
    /// </summary>
    internal void RestoreSelection(IEnumerable<ComposerSceneNode> nodes, ComposerSceneNode? primary)
    {
        var previousSelection = _selectionOrder.ToList();

        _selectedNodes.Clear();
        _selectionOrder.Clear();

        foreach (var node in nodes)
        {
            if (_selectedNodes.Add(node))
            {
                _selectionOrder.Add(node);
            }
        }

        _primaryNode = primary;

        RaiseSelectionChanged(previousSelection);
    }

    private void RaiseSelectionChanged(IReadOnlyList<ComposerSceneNode> previousSelection)
    {
        var added = _selectionOrder.Except(previousSelection).ToList();
        var removed = previousSelection.Except(_selectionOrder).ToList();

        if (added.Count > 0 || removed.Count > 0)
        {
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(
                _selectionOrder.ToList(),
                added,
                removed,
                _primaryNode));
        }
    }
}

/// <summary>
/// Event args for hover state changes.
/// </summary>
public class HoverChangedEventArgs : EventArgs
{
    /// <summary>
    /// Previously hovered node.
    /// </summary>
    public ComposerSceneNode? Previous { get; }

    /// <summary>
    /// Currently hovered node.
    /// </summary>
    public ComposerSceneNode? Current { get; }

    public HoverChangedEventArgs(ComposerSceneNode? previous, ComposerSceneNode? current)
    {
        Previous = previous;
        Current = current;
    }
}
