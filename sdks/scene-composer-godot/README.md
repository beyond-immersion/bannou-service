# Bannou Godot SceneComposer

Godot 4.x engine integration for the Bannou SceneComposer SDK. Provides `ISceneComposerBridge` implementation, asset loading via `res://` paths, and gizmo rendering for Godot-based games.

## Overview

This package bridges the engine-agnostic SceneComposer SDK to the Godot 4.x engine:
- **GodotSceneComposerBridge** - Full `ISceneComposerBridge` implementation
- **Asset Loading** - Native Godot `ResourceLoader` integration with `res://` paths
- **Type Converters** - Bidirectional conversion between SDK (double-precision) and Godot (float) types
- **Gizmo Renderer** - Transform gizmo visualization with procedural ArrayMesh
- **Physics Picking** - Ray-based entity selection with AABB fallback

## Requirements

- **Godot 4.3+** with .NET support enabled
- **.NET 8.0**
- **C# scripting** (not GDScript)

## Project Structure

```
Bannou.Godot.SceneComposer/
├── Bridge/                         # Engine bridge implementation
│   └── GodotSceneComposerBridge.cs     # ISceneComposerBridge for Godot
├── Content/                        # Asset loading
│   └── GodotAssetLoader.cs             # res:// path ResourceLoader wrapper
├── Gizmo/                          # Transform gizmo rendering
│   ├── GodotGizmoRenderer.cs           # ArrayMesh-based gizmo renderer
│   └── GizmoGeometry.cs                # Procedural mesh generation
├── Math/                           # Math utilities
│   └── RayMath.cs                      # Ray-AABB intersection
└── GodotTypeConverter.cs           # SDK <-> Godot type conversion
```

## Architecture

The asset loading system uses Godot's native resource system:

```
┌─────────────────────────────────────────────────────────────┐
│                  GodotSceneComposerBridge                    │
│                    (Bridge Layer)                            │
│                                                              │
│  - Manages entity lifecycle (Node3D instances)               │
│  - Coordinates asset loading and type conversion             │
│  - Handles picking, gizmo rendering, camera operations       │
└───────────────────────────┬─────────────────────────────────┘
                            │
            ┌───────────────┼───────────────┐
            │               │               │
            v               v               v
┌───────────────┐ ┌───────────────┐ ┌───────────────┐
│ GodotAsset    │ │ GodotGizmo    │ │ GodotType     │
│ Loader        │ │ Renderer      │ │ Converter     │
│               │ │               │ │               │
│ ResourceLoader│ │ ArrayMesh     │ │ SDK <-> Godot │
│ res:// paths  │ │ procedural    │ │ math types    │
└───────────────┘ └───────────────┘ └───────────────┘
```

## Quick Start

```csharp
using BeyondImmersion.Bannou.Godot.SceneComposer;
using BeyondImmersion.Bannou.SceneComposer;

public partial class SceneEditorScript : Node3D
{
    private GodotSceneComposerBridge _bridge;
    private SceneComposer _composer;

    public override void _Ready()
    {
        // Get references
        var sceneRoot = GetNode<Node3D>("SceneRoot");
        var camera = GetNode<Camera3D>("Camera3D");
        var viewport = GetViewport();

        // Create the bridge
        _bridge = new GodotSceneComposerBridge(sceneRoot, camera, viewport);

        // Create the scene composer
        _composer = new SceneComposer(_bridge);

        // Create a new scene
        _composer.NewScene(SceneType.Arena, "My Scene");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                // Pick entity under cursor
                var ray = _bridge.GetMouseRay(mouseButton.Position);
                var hit = _bridge.PickNode(ray);
                if (hit != null)
                {
                    _composer.Select(_composer.CurrentScene.FindNode(hit));
                }
            }
        }
    }
}
```

## Asset Loading

### Loading from res:// Paths

```csharp
// Assets are referenced using Godot's res:// path system
// The AssetReference format: (bundleId, assetId, variantId)
// For Godot, use assetId as the res:// path

var assetRef = new AssetReference(
    bundleId: "",  // Not used for Godot
    assetId: "res://models/character.glb",
    variantId: ""  // Not used for Godot
);

// Bind asset to entity
await bridge.SetEntityAssetAsync(nodeId, assetRef);
```

### Async Loading

```csharp
// The bridge uses ResourceLoader.LoadThreadedRequest for async loading
// This avoids blocking the main thread for large assets

var loader = new GodotAssetLoader();
var mesh = await loader.LoadMeshAsync("res://models/large_model.glb");
```

## Type Conversions

SDK uses double precision; Godot uses single precision. The `GodotTypeConverter` handles all conversions:

```csharp
using BeyondImmersion.Bannou.Godot.SceneComposer;

// SDK to Godot (extension methods)
var sdkPos = new SdkVector3(10.5, 20.0, 30.0);
Godot.Vector3 godotPos = sdkPos.ToGodot();

var sdkRot = SdkQuaternion.Identity;
Godot.Quaternion godotRot = sdkRot.ToGodot();

var sdkTransform = new SdkTransform(position, rotation, scale);
Godot.Transform3D godotTransform = sdkTransform.ToGodot();

// Godot to SDK (extension methods)
var godotVec = new Godot.Vector3(1, 2, 3);
SdkVector3 sdkVec = godotVec.ToSdk();
```

## NodeType Mapping

| SDK NodeType | Godot Node | Creation Pattern |
|--------------|------------|------------------|
| Group | Node3D | Empty transform node |
| Mesh | Node3D + MeshInstance3D child | Parent for transform, child for visual |
| Marker | Marker3D | Godot's built-in marker |
| Volume | Area3D + CollisionShape3D | Physics-enabled volume |
| Emitter | GPUParticles3D | Particle emitter |
| Reference | Node3D (with metadata) | Scene reference |

## Gizmo Rendering

The `GodotGizmoRenderer` provides visual handles for transform operations using procedural `ArrayMesh`:

```csharp
// Gizmo is managed automatically by the bridge
// But you can access it directly if needed

var gizmoRenderer = new GodotGizmoRenderer(sceneRoot);

// Render gizmo at position
gizmoRenderer.Render(
    position,
    rotation,
    GizmoMode.Translate,
    GizmoAxis.X,
    scale: 1.0);

// Pick gizmo axis from ray
GizmoAxis axis = gizmoRenderer.PickAxis(
    rayOrigin,
    rayDirection,
    gizmoPosition,
    gizmoRotation,
    scale: 1.0);

// Hide gizmo
gizmoRenderer.Hide();
```

### Gizmo Modes

- **Translate** - Arrow meshes for position manipulation
- **Rotate** - Ring meshes (torus segments) for rotation
- **Scale** - Box handles for scaling

### Gizmo Appearance

- X axis: Red
- Y axis: Green
- Z axis: Blue
- Highlighted axis: Yellow
- Always rendered on top (no depth test)

## Physics Picking

Two-tier picking system for entity selection:

```csharp
// Primary: Physics-based picking (requires CollisionShape3D)
var ray = bridge.GetMouseRay(mousePosition);
string? hitNodeId = bridge.PickNode(ray);

// The bridge automatically falls back to AABB intersection
// when entities don't have collision shapes

// Rectangular selection for multi-select
var hits = bridge.PickNodesInRect(screenRect);
```

## Coordinate System

Godot uses **-Z as forward** (camera looks toward -Z by default). This differs from some engines that use +Z forward.

The bridge handles coordinate conversion internally, but be aware when:
- Setting up cameras
- Orienting gizmos
- Working with imported models

## Platform Notes

| Platform | Status |
|----------|--------|
| Windows | Full functionality |
| Linux | Full functionality |
| macOS | Full functionality |
| Web (HTML5) | Requires .NET WASM support |
| Mobile | Requires Godot mobile export |

## Dependencies

- `Godot.NET.Sdk` 4.3.0+
- `BeyondImmersion.Bannou.SceneComposer` - Core SDK

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Godot.SceneComposer
```

This will also install the core SceneComposer SDK as a dependency.

Or add a project reference if building from source:

```xml
<ProjectReference Include="path/to/Bannou.Godot.SceneComposer.csproj" />
```

## Testing

The test project (`Bannou.Godot.SceneComposer.Tests`) includes:
- Unit tests for `GodotTypeConverter` (Vector3, Quaternion, Color, Transform conversions)
- Unit tests for `RayMath` (ray-AABB intersection)
- Unit tests for `GizmoGeometry` (procedural mesh generation)
- Tests run on all platforms via `dotnet test`

Run tests:
```bash
dotnet test Bannou.Godot.SceneComposer.Tests
```

## Demo Project

A demo project is available at `Bannou.Godot.SceneComposer.Demo/`:
- Complete scene editor example
- Camera orbit/pan controls
- Entity selection and gizmo manipulation
- Keyboard shortcuts (G=translate, R=rotate, S=scale)

To run the demo:
1. Open the project in Godot 4.3+
2. Build the C# solution
3. Run the `demo_main.tscn` scene

## License

MIT License
