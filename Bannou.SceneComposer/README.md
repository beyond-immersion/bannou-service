# Bannou SceneComposer SDK

Engine-agnostic scene composition SDK for Bannou. Provides hierarchical scene editing, undo/redo, multi-selection, and transform gizmo logic without engine dependencies.

## Overview

The SceneComposer SDK provides a complete solution for building scene editors:
- **Hierarchical scene graph** with node creation, deletion, and reparenting
- **Command-based undo/redo** with compound operations for grouping
- **Multi-selection** with add/remove/toggle modes
- **Transform operations** in local or world coordinate space
- **Validation system** for scene and node integrity
- **Engine bridge pattern** for integration with any game engine
- **Service client pattern** for persistence and checkout/commit workflow

## Architecture

```
                    ISceneComposerBridge
                           |
                           v
+-----------------+   +-----------+   +----------------+
| SceneComposer   |-->| Commands  |-->| Engine Bridge  |
|  - Selection    |   | - Create  |   | (Stride/Unity) |
|  - CommandStack |   | - Delete  |   +----------------+
|  - Validation   |   | - Move    |
+-----------------+   | - etc.    |
        |             +-----------+
        v
+------------------+
| ISceneService    |
| - Checkout/Commit|
| - Persistence    |
+------------------+
```

The SDK is designed in layers:
1. **Engine-agnostic core** (this package) - All editing logic
2. **Engine-specific bridge** (separate package) - Renders to your engine
3. **Service integration** (optional) - Backend persistence

## Project Structure

```
Bannou.SceneComposer/
├── Abstractions/           # Core interfaces
│   ├── ISceneComposer.cs       # Main orchestrator interface
│   ├── ISceneComposerBridge.cs # Engine integration interface
│   └── ISceneServiceClient.cs  # Backend persistence interface
├── Commands/               # Undo/redo command system
│   ├── IEditorCommand.cs       # Command interface
│   ├── NodeCommands.cs         # Create, delete, reparent, etc.
│   └── CommandStack.cs         # Undo/redo stack management
├── SceneGraph/             # Hierarchical scene structure
│   ├── ComposerScene.cs        # Scene container
│   └── ComposerSceneNode.cs    # Node with transform, asset, children
├── Selection/              # Multi-selection system
│   └── SelectionManager.cs     # Selection state and events
├── Math/                   # Engine-agnostic math types
│   ├── Vector3.cs              # 3D vector (double precision)
│   ├── Quaternion.cs           # Rotation quaternion
│   ├── Transform.cs            # Position + Rotation + Scale
│   ├── Ray.cs                  # Origin + Direction for picking
│   └── Color.cs                # RGBA color
├── Gizmo/                  # Transform gizmo logic
│   ├── GizmoController.cs      # Drag handling logic
│   └── GizmoTypes.cs           # Mode/axis enumerations
├── Validation/             # Scene validation
│   └── SceneValidator.cs       # Validates scene and node integrity
├── Events/                 # Event argument types
│   └── SceneEvents.cs          # Scene/selection/dirty state events
├── Assets/                 # Asset management interfaces
│   └── IAssetBundleManager.cs  # Bundle loading abstraction
└── SceneComposer.cs        # Main orchestrator implementation
```

## Quick Start

```csharp
using BeyondImmersion.Bannou.SceneComposer;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;

// Create a scene composer with your engine bridge
ISceneComposerBridge bridge = new YourEngineBridge();
var composer = new SceneComposer(bridge);

// Create a new scene
var scene = composer.NewScene(SceneType.Region, "My Level");

// Create nodes
var player = composer.CreateNode(NodeType.Actor, "Player");
var weapon = composer.CreateNode(NodeType.Model, "Sword", parent: player);

// Transform a node
composer.TranslateNode(player, new Vector3(10, 0, 5), CoordinateSpace.World);

// Undo/Redo
composer.Undo();
composer.Redo();
```

## Features

### Scene Graph

Hierarchical node management with transform inheritance:

```csharp
// Create nodes with parent relationships
var parent = composer.CreateNode(NodeType.Group, "Parent");
var child = composer.CreateNode(NodeType.Model, "Child", parent: parent);

// Reparent nodes
composer.ReparentNode(child, newParent: otherParent, insertIndex: 0);

// Duplicate with hierarchy
var clone = composer.DuplicateNode(parent, deepClone: true);

// Delete with or without children
composer.DeleteNode(parent, deleteChildren: true);
```

### Transform Operations

Transform in local or world coordinates:

```csharp
// Set local transform
composer.SetLocalTransform(node, new Transform(
    position: new Vector3(1, 2, 3),
    rotation: Quaternion.FromEuler(0, 45, 0),
    scale: Vector3.One));

// Translate in world space
composer.TranslateNode(node, delta: new Vector3(5, 0, 0), CoordinateSpace.World);

// Rotate multiple nodes around a pivot
composer.RotateNodes(selectedNodes, rotation, CoordinateSpace.World, pivot: center);

// Scale multiple nodes from a pivot
composer.ScaleNodes(selectedNodes, new Vector3(2, 2, 2), pivot: center);
```

### Command System

Full undo/redo with compound operations:

```csharp
// Individual commands are automatically tracked
composer.CreateNode(NodeType.Model, "Object1");
composer.CreateNode(NodeType.Model, "Object2");
composer.Undo(); // Removes Object2
composer.Undo(); // Removes Object1

// Group multiple operations as one undo step
using (composer.BeginCompoundOperation("Create Grid"))
{
    for (int x = 0; x < 10; x++)
    for (int y = 0; y < 10; y++)
    {
        composer.CreateNode(NodeType.Model, $"Cell_{x}_{y}");
    }
}
composer.Undo(); // Removes all 100 cells at once
```

### Multi-Selection

Select and operate on multiple nodes:

```csharp
// Selection modes
composer.Select(node1, SelectionMode.Replace);  // Clear and select one
composer.Select(node2, SelectionMode.Add);      // Add to selection
composer.Select(node2, SelectionMode.Toggle);   // Toggle in/out

// Select multiple
composer.Select(nodeList, SelectionMode.Replace);

// Select all in scene
composer.SelectAll();

// Clear selection
composer.ClearSelection();

// Check selection
if (composer.HasSelection)
{
    var selected = composer.SelectedNodes;
}
```

### Asset Binding

Bind visual assets to nodes:

```csharp
// Bind asset from a bundle
composer.BindAsset(node, new AssetReference(
    bundleId: "characters",
    assetId: "warrior_model",
    variantId: "high_poly"));

// Clear asset
composer.ClearAsset(node);
```

### Persistence (with Service Client)

Checkout/commit workflow for collaborative editing:

```csharp
// Load scene from service
var scene = await composer.LoadSceneAsync("scene-123");

// Checkout for exclusive editing
if (await composer.CheckoutAsync())
{
    // Make changes...
    composer.CreateNode(NodeType.Model, "NewObject");

    // Commit changes
    await composer.CommitAsync("Added new object");
}

// Or discard changes
await composer.DiscardAsync();
```

### Validation

Validate scene integrity:

```csharp
// Validate entire scene
var result = composer.ValidateScene();
if (!result.IsValid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"{error.Severity}: {error.Message}");
    }
}

// Validate specific node
var nodeResult = composer.ValidateNode(node);
```

## Implementing a Bridge

Create your engine bridge by implementing `ISceneComposerBridge`:

```csharp
public class MyEngineBridge : ISceneComposerBridge
{
    public void CreateEntity(string nodeId, NodeType nodeType, Transform transform, AssetReference? asset)
    {
        // Create entity in your engine
        var entity = new GameObject();
        entity.name = nodeId;
        ApplyTransform(entity, transform);
        _entities[nodeId] = entity;
    }

    public void DestroyEntity(string nodeId)
    {
        if (_entities.TryGetValue(nodeId, out var entity))
        {
            Destroy(entity);
            _entities.Remove(nodeId);
        }
    }

    public void UpdateEntityTransform(string nodeId, Transform worldTransform)
    {
        var entity = _entities[nodeId];
        entity.transform.position = worldTransform.Position.ToEngine();
        entity.transform.rotation = worldTransform.Rotation.ToEngine();
        entity.transform.localScale = worldTransform.Scale.ToEngine();
    }

    public void SetEntityParent(string nodeId, string? parentId)
    {
        var entity = _entities[nodeId];
        var parent = parentId != null ? _entities[parentId] : null;
        entity.transform.SetParent(parent?.transform);
    }

    public Task SetEntityAssetAsync(string nodeId, AssetReference asset, CancellationToken ct = default)
    {
        // Load and assign asset
        return Task.CompletedTask;
    }

    // ... implement other methods
}
```

## Events

Subscribe to scene changes:

```csharp
// Scene modifications (node created, deleted, transformed, etc.)
composer.SceneModified += (sender, e) =>
{
    Console.WriteLine($"{e.ModificationType}: {e.Description}");
};

// Selection changes
composer.SelectionChanged += (sender, e) =>
{
    Console.WriteLine($"Selected: {e.CurrentSelection.Count} nodes");
};

// Dirty state (unsaved changes)
composer.DirtyStateChanged += (sender, e) =>
{
    windowTitle = e.IsDirty ? "Scene Editor *" : "Scene Editor";
};

// Undo/redo availability
composer.UndoRedoStateChanged += (sender, e) =>
{
    undoButton.Enabled = e.CanUndo;
    redoButton.Enabled = e.CanRedo;
};
```

## Engine Bridges

| Package | Engine | Notes |
|---------|--------|-------|
| `BeyondImmersion.Bannou.Stride.SceneComposer` | Stride Engine 4.3 | Windows, bundle loading, LRU cache |
| `BeyondImmersion.Bannou.Godot.SceneComposer` | Godot 4.3+ | Cross-platform, res:// paths |

## Dependencies

- .NET 8.0 or later
- No external dependencies (pure .NET)

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.SceneComposer
```

For Stride engine integration:
```bash
dotnet add package BeyondImmersion.Bannou.Stride.SceneComposer
```

For Godot 4.x engine integration:
```bash
dotnet add package BeyondImmersion.Bannou.Godot.SceneComposer
```

## License

MIT License
