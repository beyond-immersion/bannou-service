using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Events;
using BeyondImmersion.Bannou.SceneComposer.Math;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;
using BeyondImmersion.Bannou.SceneComposer.Selection;
using Xunit;

namespace BeyondImmersion.Bannou.SceneComposer.Tests.Selection;

/// <summary>
/// Tests for the SelectionManager class.
/// </summary>
public class SelectionManagerTests
{
    // =========================================================================
    // INITIAL STATE
    // =========================================================================

    [Fact]
    public void InitialState_IsEmpty()
    {
        var manager = new SelectionManager();

        Assert.Equal(0, manager.Count);
        Assert.False(manager.HasSelection);
        Assert.Null(manager.PrimaryNode);
        Assert.Null(manager.HoveredNode);
        Assert.Empty(manager.SelectedNodes);
    }

    // =========================================================================
    // SELECT (SINGLE NODE)
    // =========================================================================

    [Fact]
    public void Select_Replace_SelectsNode()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");

        manager.Select(node, SelectionMode.Replace);

        Assert.Equal(1, manager.Count);
        Assert.True(manager.HasSelection);
        Assert.True(manager.IsSelected(node));
        Assert.Equal(node, manager.PrimaryNode);
    }

    [Fact]
    public void Select_Replace_ClearsPreviousSelection()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        manager.Select(node1);

        manager.Select(node2, SelectionMode.Replace);

        Assert.Equal(1, manager.Count);
        Assert.False(manager.IsSelected(node1));
        Assert.True(manager.IsSelected(node2));
    }

    [Fact]
    public void Select_Add_AddsToSelection()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        manager.Select(node1);

        manager.Select(node2, SelectionMode.Add);

        Assert.Equal(2, manager.Count);
        Assert.True(manager.IsSelected(node1));
        Assert.True(manager.IsSelected(node2));
        Assert.Equal(node2, manager.PrimaryNode);
    }

    [Fact]
    public void Select_Add_DoesNotDuplicateAlreadySelected()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        manager.Select(node);

        manager.Select(node, SelectionMode.Add);

        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void Select_Remove_RemovesFromSelection()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        manager.Select(new[] { node1, node2 });

        manager.Select(node1, SelectionMode.Remove);

        Assert.Equal(1, manager.Count);
        Assert.False(manager.IsSelected(node1));
        Assert.True(manager.IsSelected(node2));
    }

    [Fact]
    public void Select_Remove_UpdatesPrimaryNode()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        manager.Select(new[] { node1, node2 });
        Assert.Equal(node2, manager.PrimaryNode);

        manager.Select(node2, SelectionMode.Remove);

        Assert.Equal(node1, manager.PrimaryNode);
    }

    [Fact]
    public void Select_Toggle_SelectsUnselectedNode()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");

        manager.Select(node, SelectionMode.Toggle);

        Assert.True(manager.IsSelected(node));
    }

    [Fact]
    public void Select_Toggle_DeselectsSelectedNode()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        manager.Select(node);

        manager.Select(node, SelectionMode.Toggle);

        Assert.False(manager.IsSelected(node));
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void Select_ThrowsOnNullNode()
    {
        var manager = new SelectionManager();

        Assert.Throws<ArgumentNullException>(() => manager.Select((ComposerSceneNode)null!));
    }

    [Fact]
    public void Select_LockedNode_IsIgnored()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        node.IsLocked = true;

        manager.Select(node);

        Assert.Equal(0, manager.Count);
        Assert.False(manager.IsSelected(node));
    }

    // =========================================================================
    // SELECT (MULTIPLE NODES)
    // =========================================================================

    [Fact]
    public void Select_Multiple_Replace_SelectsAllNodes()
    {
        var manager = new SelectionManager();
        var nodes = new[] { CreateTestNode("Node1"), CreateTestNode("Node2"), CreateTestNode("Node3") };

        manager.Select(nodes, SelectionMode.Replace);

        Assert.Equal(3, manager.Count);
        Assert.All(nodes, n => Assert.True(manager.IsSelected(n)));
        Assert.Equal(nodes[2], manager.PrimaryNode);
    }

    [Fact]
    public void Select_Multiple_Add_AddsToExisting()
    {
        var manager = new SelectionManager();
        var existing = CreateTestNode("Existing");
        var newNodes = new[] { CreateTestNode("Node1"), CreateTestNode("Node2") };
        manager.Select(existing);

        manager.Select(newNodes, SelectionMode.Add);

        Assert.Equal(3, manager.Count);
        Assert.True(manager.IsSelected(existing));
    }

    [Fact]
    public void Select_Multiple_Remove_RemovesFromSelection()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        var node3 = CreateTestNode("Node3");
        manager.Select(new[] { node1, node2, node3 });

        manager.Select(new[] { node1, node2 }, SelectionMode.Remove);

        Assert.Equal(1, manager.Count);
        Assert.True(manager.IsSelected(node3));
    }

    [Fact]
    public void Select_Multiple_Toggle_TogglesEachNode()
    {
        var manager = new SelectionManager();
        var selected = CreateTestNode("Selected");
        var unselected = CreateTestNode("Unselected");
        manager.Select(selected);

        manager.Select(new[] { selected, unselected }, SelectionMode.Toggle);

        Assert.False(manager.IsSelected(selected));
        Assert.True(manager.IsSelected(unselected));
    }

    [Fact]
    public void Select_Multiple_IgnoresLockedNodes()
    {
        var manager = new SelectionManager();
        var unlocked = CreateTestNode("Unlocked");
        var locked = CreateTestNode("Locked");
        locked.IsLocked = true;

        manager.Select(new[] { unlocked, locked });

        Assert.Equal(1, manager.Count);
        Assert.True(manager.IsSelected(unlocked));
        Assert.False(manager.IsSelected(locked));
    }

    [Fact]
    public void Select_EmptyCollection_Replace_ClearsSelection()
    {
        var manager = new SelectionManager();
        manager.Select(CreateTestNode("Node1"));

        manager.Select(Array.Empty<ComposerSceneNode>(), SelectionMode.Replace);

        Assert.Equal(0, manager.Count);
    }

    // =========================================================================
    // CLEAR SELECTION
    // =========================================================================

    [Fact]
    public void ClearSelection_RemovesAllNodes()
    {
        var manager = new SelectionManager();
        manager.Select(new[] { CreateTestNode("Node1"), CreateTestNode("Node2") });

        manager.ClearSelection();

        Assert.Equal(0, manager.Count);
        Assert.False(manager.HasSelection);
        Assert.Null(manager.PrimaryNode);
    }

    [Fact]
    public void ClearSelection_EmptySelection_NoEvent()
    {
        var manager = new SelectionManager();
        var eventFired = false;
        manager.SelectionChanged += (_, _) => eventFired = true;

        manager.ClearSelection();

        Assert.False(eventFired);
    }

    // =========================================================================
    // IS SELECTED
    // =========================================================================

    [Fact]
    public void IsSelected_ByNode_ReturnsTrue()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        manager.Select(node);

        Assert.True(manager.IsSelected(node));
    }

    [Fact]
    public void IsSelected_ByNode_ReturnsFalse()
    {
        var manager = new SelectionManager();
        var selected = CreateTestNode("Selected");
        var unselected = CreateTestNode("Unselected");
        manager.Select(selected);

        Assert.False(manager.IsSelected(unselected));
    }

    [Fact]
    public void IsSelected_ById_ReturnsTrue()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        manager.Select(node);

        Assert.True(manager.IsSelected(node.Id));
    }

    [Fact]
    public void IsSelected_ById_ReturnsFalse()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        manager.Select(node);

        Assert.False(manager.IsSelected(Guid.NewGuid()));
    }

    // =========================================================================
    // SELECTION ORDER
    // =========================================================================

    [Fact]
    public void SelectedNodes_MaintainsInsertionOrder()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        var node3 = CreateTestNode("Node3");

        manager.Select(node1);
        manager.Select(node2, SelectionMode.Add);
        manager.Select(node3, SelectionMode.Add);

        Assert.Equal(node1, manager.SelectedNodes[0]);
        Assert.Equal(node2, manager.SelectedNodes[1]);
        Assert.Equal(node3, manager.SelectedNodes[2]);
    }

    // =========================================================================
    // HOVER STATE
    // =========================================================================

    [Fact]
    public void SetHovered_SetsHoveredNode()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");

        manager.SetHovered(node);

        Assert.Equal(node, manager.HoveredNode);
    }

    [Fact]
    public void SetHovered_Null_ClearsHoveredNode()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        manager.SetHovered(node);

        manager.SetHovered(null);

        Assert.Null(manager.HoveredNode);
    }

    [Fact]
    public void SetHovered_SameNode_NoEvent()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        manager.SetHovered(node);

        var eventFired = false;
        manager.HoverChanged += (_, _) => eventFired = true;

        manager.SetHovered(node);

        Assert.False(eventFired);
    }

    [Fact]
    public void SetHovered_RaisesEvent()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        manager.SetHovered(node1);

        HoverChangedEventArgs? args = null;
        manager.HoverChanged += (_, e) => args = e;

        manager.SetHovered(node2);

        Assert.NotNull(args);
        Assert.Equal(node1, args.Previous);
        Assert.Equal(node2, args.Current);
    }

    // =========================================================================
    // SELECTION CHANGED EVENT
    // =========================================================================

    [Fact]
    public void SelectionChanged_FiredOnSelect()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        SelectionChangedEventArgs? args = null;
        manager.SelectionChanged += (_, e) => args = e;

        manager.Select(node);

        Assert.NotNull(args);
        Assert.Single(args.SelectedNodes);
        Assert.Single(args.Added);
        Assert.Empty(args.Removed);
        Assert.Equal(node, args.PrimaryNode);
    }

    [Fact]
    public void SelectionChanged_ReportsAddedAndRemoved()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        manager.Select(node1);

        SelectionChangedEventArgs? args = null;
        manager.SelectionChanged += (_, e) => args = e;
        manager.Select(node2);

        Assert.NotNull(args);
        Assert.Contains(node2, args.Added);
        Assert.Contains(node1, args.Removed);
    }

    [Fact]
    public void SelectionChanged_NotFiredWhenNoChange()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        manager.Select(node);

        var eventFired = false;
        manager.SelectionChanged += (_, _) => eventFired = true;
        manager.Select(node, SelectionMode.Add); // Already selected

        Assert.False(eventFired);
    }

    // =========================================================================
    // NOTIFY NODE DELETED
    // =========================================================================

    [Fact]
    public void NotifyNodeDeleted_RemovesFromSelection()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        manager.Select(new[] { node1, node2 });

        manager.NotifyNodeDeleted(node1);

        Assert.Equal(1, manager.Count);
        Assert.False(manager.IsSelected(node1));
        Assert.True(manager.IsSelected(node2));
    }

    [Fact]
    public void NotifyNodeDeleted_UpdatesPrimaryNode()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        manager.Select(new[] { node1, node2 });
        Assert.Equal(node2, manager.PrimaryNode);

        manager.NotifyNodeDeleted(node2);

        Assert.Equal(node1, manager.PrimaryNode);
    }

    [Fact]
    public void NotifyNodeDeleted_ClearsHoveredIfMatch()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        manager.SetHovered(node);

        manager.NotifyNodeDeleted(node);

        Assert.Null(manager.HoveredNode);
    }

    // =========================================================================
    // RESTORE SELECTION
    // =========================================================================

    [Fact]
    public void RestoreSelection_RestoresNodesAndPrimary()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        var node3 = CreateTestNode("Node3");

        manager.RestoreSelection(new[] { node1, node2, node3 }, node2);

        Assert.Equal(3, manager.Count);
        Assert.True(manager.IsSelected(node1));
        Assert.True(manager.IsSelected(node2));
        Assert.True(manager.IsSelected(node3));
        Assert.Equal(node2, manager.PrimaryNode);
    }

    [Fact]
    public void RestoreSelection_RaisesEvent()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        SelectionChangedEventArgs? args = null;
        manager.SelectionChanged += (_, e) => args = e;

        manager.RestoreSelection(new[] { node }, node);

        Assert.NotNull(args);
    }

    // =========================================================================
    // GET SELECTION CENTER
    // =========================================================================

    [Fact]
    public void GetSelectionCenter_EmptySelection_ReturnsZero()
    {
        var manager = new SelectionManager();

        var center = manager.GetSelectionCenter();

        Assert.Equal(Vector3.Zero, center);
    }

    [Fact]
    public void GetSelectionCenter_SingleNode_ReturnsNodePosition()
    {
        var manager = new SelectionManager();
        var node = CreateTestNode("Node1");
        node.LocalTransform = new Transform(new Vector3(10, 20, 30));
        manager.Select(node);

        var center = manager.GetSelectionCenter();

        Assert.Equal(10, center.X, 1e-6);
        Assert.Equal(20, center.Y, 1e-6);
        Assert.Equal(30, center.Z, 1e-6);
    }

    [Fact]
    public void GetSelectionCenter_MultipleNodes_ReturnsAverage()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        node1.LocalTransform = new Transform(new Vector3(0, 0, 0));
        node2.LocalTransform = new Transform(new Vector3(10, 10, 10));
        manager.Select(new[] { node1, node2 });

        var center = manager.GetSelectionCenter();

        Assert.Equal(5, center.X, 1e-6);
        Assert.Equal(5, center.Y, 1e-6);
        Assert.Equal(5, center.Z, 1e-6);
    }

    // =========================================================================
    // GET SELECTION BOUNDS
    // =========================================================================

    [Fact]
    public void GetSelectionBounds_EmptySelection_ReturnsZero()
    {
        var manager = new SelectionManager();

        var (min, max) = manager.GetSelectionBounds();

        Assert.Equal(Vector3.Zero, min);
        Assert.Equal(Vector3.Zero, max);
    }

    [Fact]
    public void GetSelectionBounds_MultipleNodes_ReturnsBounds()
    {
        var manager = new SelectionManager();
        var node1 = CreateTestNode("Node1");
        var node2 = CreateTestNode("Node2");
        var node3 = CreateTestNode("Node3");
        node1.LocalTransform = new Transform(new Vector3(-5, 0, 10));
        node2.LocalTransform = new Transform(new Vector3(5, 10, -10));
        node3.LocalTransform = new Transform(new Vector3(0, -5, 0));
        manager.Select(new[] { node1, node2, node3 });

        var (min, max) = manager.GetSelectionBounds();

        Assert.Equal(-5, min.X, 1e-6);
        Assert.Equal(-5, min.Y, 1e-6);
        Assert.Equal(-10, min.Z, 1e-6);
        Assert.Equal(5, max.X, 1e-6);
        Assert.Equal(10, max.Y, 1e-6);
        Assert.Equal(10, max.Z, 1e-6);
    }

    // =========================================================================
    // TEST HELPERS
    // =========================================================================

    private static ComposerSceneNode CreateTestNode(string name, NodeType type = NodeType.Group)
    {
        return new ComposerSceneNode(type, name);
    }
}
