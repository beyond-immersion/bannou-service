# Bannou.Godot.SceneComposer

Godot 4.x engine extension for the Bannou SceneComposer SDK.

## Overview

This library provides the `ISceneComposerBridge` implementation for Godot 4.x, enabling:

- Scene composition with hierarchical node management
- Transform manipulation with translate/rotate/scale gizmos
- Asset loading via Godot's `res://` path system
- Physics-based entity picking with AABB fallback
- Full undo/redo support through the SceneComposer SDK

## Requirements

- Godot 4.3+ with .NET support
- .NET 8.0

## Installation

Add the NuGet package to your Godot C# project:

```bash
dotnet add package BeyondImmersion.Bannou.Godot.SceneComposer
```

Or add a project reference if building from source:

```xml
<ProjectReference Include="path/to/Bannou.Godot.SceneComposer.csproj" />
```

## Quick Start

```csharp
using BeyondImmersion.Bannou.Godot.SceneComposer;
using BeyondImmersion.Bannou.SceneComposer;

public partial class MySceneEditor : Node3D
{
    private GodotSceneComposerBridge _bridge;
    private SceneComposer _composer;

    public override void _Ready()
    {
        // Get references to scene root and camera
        var sceneRoot = GetNode<Node3D>("SceneRoot");
        var camera = GetNode<Camera3D>("Camera3D");
        var viewport = GetViewport();

        // Create the bridge
        _bridge = new GodotSceneComposerBridge(sceneRoot, camera, viewport);

        // Create the composer
        _composer = new SceneComposer(_bridge);

        // Create a new scene
        _composer.NewScene(SceneType.Arena, "My Scene");
    }

    public override void _Process(double delta)
    {
        // Handle input, update gizmos, etc.
    }
}
```

## Architecture

This library implements the Bridge pattern from the core SceneComposer SDK:

```
Bannou.SceneComposer (engine-agnostic)
    │
    ├── ISceneComposerBridge interface
    │
    └── Bannou.Godot.SceneComposer (this library)
            │
            ├── GodotSceneComposerBridge
            ├── GodotTypeConverter
            ├── GodotAssetLoader
            └── GodotGizmoRenderer
```

## NodeType Mapping

| SDK NodeType | Godot Node |
|--------------|------------|
| Group | Node3D |
| Mesh | Node3D + MeshInstance3D child |
| Marker | Marker3D |
| Volume | Area3D + CollisionShape3D |
| Emitter | GPUParticles3D |
| Reference | Node3D (with metadata) |

## Coordinate System

Godot uses -Z as the forward direction (camera looks toward -Z by default). This library handles the coordinate conversion internally.

## License

MIT License - See LICENSE file for details.
