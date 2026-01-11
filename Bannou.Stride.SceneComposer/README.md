# Bannou Stride SceneComposer

Stride engine extension for the Bannou SceneComposer SDK. Provides `ISceneComposerBridge` implementation, asset loaders, and gizmo rendering for Stride-based games.

## Overview

This package bridges the engine-agnostic SceneComposer SDK to the Stride game engine:
- **StrideSceneComposerBridge** - Full `ISceneComposerBridge` implementation
- **StrideGizmoRenderer** - Transform gizmo visualization
- **IAssetLoader** - Interface for loading Bannou bundle assets into Stride
- **StrideTypeConverter** - Conversion between SDK and Stride math types

## Requirements

- **.NET 10.0** - Stride 4.3 requires .NET 10
- **Windows** - Full functionality requires Windows (physics, rendering)
- **Stride 4.3.x** - Compatible with Stride Engine 4.3

## Quick Start

```csharp
using BeyondImmersion.Bannou.SceneComposer;
using BeyondImmersion.Bannou.Stride.SceneComposer;
using Stride.Engine;

public class SceneEditorScript : SyncScript
{
    private SceneComposer _composer;
    private StrideSceneComposerBridge _bridge;

    public override void Start()
    {
        // Create the bridge with Stride services
        _bridge = new StrideSceneComposerBridge(
            Services.GetService<SceneSystem>().SceneInstance,
            Services.GetService<GraphicsDevice>(),
            assetLoader: null); // Optional: provide IAssetLoader for bundle loading

        // Create the scene composer
        _composer = new SceneComposer(_bridge);

        // Set up gizmo rendering
        _composer.SetGizmoMode(GizmoMode.Translate);
    }

    public override void Update()
    {
        // Handle input and update gizmos
        HandleMouseInput();
        _bridge.RenderGizmos(_composer.GizmoState);
    }

    private void HandleMouseInput()
    {
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var ray = _bridge.GetMouseRay(Input.MousePosition);
            var hit = _bridge.PickNode(ray);
            if (hit != null)
            {
                _composer.Selection.Set(new[] { hit });
            }
        }
    }
}
```

## Type Conversions

SDK uses double precision; Stride uses single precision. Use the extension methods:

```csharp
using BeyondImmersion.Bannou.Stride.SceneComposer;

// SDK to Stride
SdkVector3 sdkPos = new(10.5, 20.0, 30.0);
StrideVector3 stridePos = sdkPos.ToStride();

SdkQuaternion sdkRot = SdkQuaternion.Identity;
StrideQuaternion strideRot = sdkRot.ToStride();

// Stride to SDK
StrideVector3 pos = entity.Transform.Position;
SdkVector3 sdkPosition = pos.ToSdk();
```

## Bridge Features

### Node Management

```csharp
// Create entity
string nodeId = bridge.CreateNode("Enemy", parentId: null);

// Get/set transform
var transform = bridge.GetTransform(nodeId);
bridge.SetTransform(nodeId, newTransform);

// Reparent
bridge.SetParent(nodeId, newParentId);

// Destroy
bridge.DestroyNode(nodeId);
```

### Selection Visualization

```csharp
// Set selection (shows bounding boxes)
bridge.SetSelection(new[] { nodeId1, nodeId2 });

// Set hover state (shows highlight)
bridge.SetHover(nodeId);
bridge.ClearHover();
```

### Physics Picking

```csharp
// Ray pick with physics
var ray = bridge.GetMouseRay(mousePosition);
string? hitNodeId = bridge.PickNode(ray);

// Rectangular selection
var hits = bridge.PickNodesInRect(screenRect);
```

### Asset Loading

Implement `IAssetLoader` to load Bannou bundle assets:

```csharp
public class BundleAssetLoader : IAssetLoader
{
    public async Task<Model?> LoadModelAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default)
    {
        // Load from Bannou asset bundle
        var bundle = await LoadBundleAsync(bundleId);
        var data = bundle.GetAsset(assetId, variantId);
        return ConvertToStrideModel(data);
    }

    // ... implement other methods
}

// Use with bridge
var loader = new BundleAssetLoader(assetService);
var bridge = new StrideSceneComposerBridge(scene, graphics, loader);

// Bind asset to node
await bridge.BindAssetAsync(nodeId, "models", "character_base", "warrior");
```

## Gizmo Rendering

The gizmo renderer provides visual handles for transform operations:

```csharp
var gizmoRenderer = new StrideGizmoRenderer(graphicsDevice);

// Render at node position
gizmoRenderer.Render(
    position: entity.Transform.Position,
    rotation: entity.Transform.Rotation,
    mode: GizmoMode.Translate,
    activeAxis: GizmoAxis.X,
    scale: 1.0f);

// Pick gizmo axis
var ray = camera.GetPickRay(mousePosition);
GizmoAxis axis = gizmoRenderer.PickAxis(
    ray.ToStride(),
    gizmoPosition,
    gizmoRotation,
    GizmoMode.Translate,
    gizmoScale);
```

## Camera Operations

```csharp
// Get current camera info
var cameraPos = bridge.GetCameraPosition();
var cameraForward = bridge.GetCameraForward();

// Focus on selection
bridge.FocusOnSelection();

// Get pick ray from screen position
var ray = bridge.GetMouseRay(mousePosition);
```

## Platform Notes

| Platform | Status |
|----------|--------|
| Windows | Full functionality |
| Linux | Builds but Stride runtime limited |
| macOS | Not supported by Stride |

The package builds on all platforms for CI purposes, but full runtime functionality (physics, rendering) requires Windows with Stride properly installed.

## Dependencies

- `Stride.Engine` 4.3.x
- `Stride.Rendering` 4.3.x
- `Stride.Physics` 4.3.x
- `Stride.UI` 4.3.x
- `BeyondImmersion.Bannou.SceneComposer`

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Stride.SceneComposer
```

This will also install the core SceneComposer SDK as a dependency.

## License

MIT License
