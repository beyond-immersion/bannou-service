# Bannou SceneComposer SDK

Engine-agnostic scene composition SDK for Bannou. Provides hierarchical scene editing, undo/redo, multi-selection, and gizmo logic without engine dependencies.

## Overview

The SceneComposer SDK provides a complete solution for building scene editors:
- **Hierarchical scene management** with node creation, destruction, and reparenting
- **Undo/redo system** with command merging for smooth editing
- **Multi-selection** with rectangular selection support
- **Transform gizmos** for translate, rotate, and scale operations
- **Engine bridge pattern** for integration with any game engine

## Quick Start

```csharp
using BeyondImmersion.Bannou.SceneComposer;
using BeyondImmersion.Bannou.SceneComposer.Commands;
using BeyondImmersion.Bannou.SceneComposer.Gizmo;

// Create a scene composer with your engine bridge
ISceneComposerBridge bridge = new YourEngineBridge();
var composer = new SceneComposer(bridge);

// Create a node
var createCmd = new CreateNodeCommand(composer, "Player", parentId: null);
composer.ExecuteCommand(createCmd);
var playerId = createCmd.CreatedNodeId;

// Transform it
var moveCmd = new TransformCommand(
    composer,
    playerId,
    new Transform(new Vector3(10, 0, 5), Quaternion.Identity, Vector3.One));
composer.ExecuteCommand(moveCmd);

// Undo!
composer.Undo();

// Redo!
composer.Redo();
```

## Architecture

```
                    ISceneComposerBridge
                           |
                           v
+-----------------+   +-----------+   +----------------+
| SceneComposer   |-->| Commands  |-->| Engine Bridge  |
|  - Selection    |   | - Create  |   | (Stride/Unity) |
|  - CommandStack |   | - Delete  |   +----------------+
|  - Gizmo Logic  |   | - Move    |
+-----------------+   | - etc.    |
                      +-----------+
```

The SDK is split into two parts:
1. **Engine-agnostic core** (this package) - All editing logic
2. **Engine-specific bridge** (separate package) - Renders to your engine

## Features

### Command Stack

Full undo/redo with command merging:

```csharp
// Commands are automatically merged when appropriate
// (e.g., continuous dragging becomes one undoable operation)
var cmd1 = new TransformCommand(composer, nodeId, transform1);
composer.ExecuteCommand(cmd1);

var cmd2 = new TransformCommand(composer, nodeId, transform2);
// If within merge window, cmd2 merges into cmd1
composer.ExecuteCommand(cmd2);

// One undo reverts both transforms
composer.Undo();
```

### Multi-Selection

Select multiple nodes for batch operations:

```csharp
// Add to selection
composer.Selection.Add(nodeId1);
composer.Selection.Add(nodeId2);

// Or set multiple at once
composer.Selection.Set(new[] { nodeId1, nodeId2, nodeId3 });

// Rectangular selection via bridge
var hits = bridge.PickNodesInRect(screenRect);
composer.Selection.Set(hits);

// Transform applies to all selected
var batchMove = new TransformCommand(composer, transform, affectsSelection: true);
composer.ExecuteCommand(batchMove);
```

### Gizmo System

Transform gizmo with axis picking:

```csharp
// Set gizmo mode
composer.SetGizmoMode(GizmoMode.Translate);

// Pick gizmo axis from mouse ray
var ray = composer.GetMouseRay(mousePosition);
var axis = composer.PickGizmoAxis(ray);

// Start drag operation
if (axis != GizmoAxis.None)
{
    composer.BeginGizmoDrag(axis, ray);
}

// During drag
composer.UpdateGizmoDrag(currentRay);

// End drag (creates undo command)
composer.EndGizmoDrag();
```

### Scene Hierarchy

Node management with proper hierarchy:

```csharp
// Create child node
var childCmd = new CreateNodeCommand(composer, "Weapon", parentId: playerId);
composer.ExecuteCommand(childCmd);

// Reparent node
var reparentCmd = new ReparentCommand(composer, weaponId, newParentId: handBoneId);
composer.ExecuteCommand(reparentCmd);

// Delete with children
var deleteCmd = new DeleteCommand(composer, playerId, deleteChildren: true);
composer.ExecuteCommand(deleteCmd);
```

## Implementing a Bridge

Create your engine bridge by implementing `ISceneComposerBridge`:

```csharp
public class MyEngineBridge : ISceneComposerBridge
{
    public string CreateNode(string name, string? parentId)
    {
        // Create entity in your engine
        var entity = new GameObject(name);
        if (parentId != null)
            entity.transform.SetParent(FindNode(parentId).transform);
        return entity.GetInstanceID().ToString();
    }

    public void DestroyNode(string nodeId)
    {
        var obj = FindNode(nodeId);
        GameObject.Destroy(obj);
    }

    public void SetTransform(string nodeId, Transform transform)
    {
        var obj = FindNode(nodeId);
        obj.transform.position = transform.Position.ToEngine();
        obj.transform.rotation = transform.Rotation.ToEngine();
        obj.transform.localScale = transform.Scale.ToEngine();
    }

    // ... implement other methods
}
```

## Available Commands

| Command | Description |
|---------|-------------|
| `CreateNodeCommand` | Create a new node |
| `DeleteCommand` | Delete node(s) |
| `TransformCommand` | Move/rotate/scale |
| `ReparentCommand` | Change node parent |
| `DuplicateCommand` | Clone node(s) |
| `RenameCommand` | Change node name |
| `BatchCommand` | Group multiple commands |

## Engine Bridges

| Package | Engine |
|---------|--------|
| `BeyondImmersion.Bannou.Stride.SceneComposer` | Stride Engine |

## Dependencies

- `System.Reactive` - Observable patterns for change notification
- `System.Text.Json` - Scene serialization
- `YamlDotNet` - YAML scene file support
- `BeyondImmersion.Bannou.Client.SDK` - Shared types

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.SceneComposer
```

For Stride engine integration:
```bash
dotnet add package BeyondImmersion.Bannou.Stride.SceneComposer
```

## License

MIT License
