# Bannou Stride SceneComposer

Stride engine integration for the Bannou SceneComposer SDK. Provides `ISceneComposerBridge` implementation, asset bundle loading, caching, and gizmo rendering for Stride-based games.

## Overview

This package bridges the engine-agnostic SceneComposer SDK to the Stride game engine:
- **StrideSceneComposerBridge** - Full `ISceneComposerBridge` implementation
- **Asset Loading Pipeline** - Multi-tier system for loading `.bannou` bundle assets
- **LRU Asset Cache** - Size-based eviction cache for loaded assets
- **Type Converters** - Bidirectional conversion between SDK and Stride math types
- **Gizmo Renderer** - Transform gizmo visualization

## Requirements

- **.NET 10.0** with Windows TFM (`net10.0-windows`)
- **Stride 4.3.x** - Compatible with Stride Engine 4.3
- **Windows** - Full runtime functionality requires Windows (physics, rendering)

## Project Structure

```
sdks/scene-composer-stride/
├── Bridge/                     # Engine bridge implementation
│   └── StrideSceneComposerBridge.cs
├── Content/                    # High-level content management
│   ├── StrideContentManager.cs     # Orchestrates loading + caching
│   ├── BundleAssetLoader.cs        # Reads .bannou bundle files (LZ4)
│   └── StrideBannouAssetLoader.cs  # Converts bundle data to Stride types
├── Loaders/                    # Individual asset type loaders
│   ├── IAssetLoader.cs             # Generic loader interface
│   ├── IStrideAssetLoader.cs       # Stride-specific loader interface
│   ├── ModelLoader.cs              # Loads Model assets
│   ├── TextureLoader.cs            # Loads Texture assets
│   └── AnimationClipLoader.cs      # Loads AnimationClip assets
├── Caching/                    # Asset caching
│   └── AssetCache.cs               # LRU cache with size-based eviction
├── Gizmo/                      # Transform gizmo rendering
│   └── StrideGizmoRenderer.cs
├── Examples/                   # Usage examples
│   └── StrideSceneEditorScript.cs
└── StrideTypeConverter.cs      # SDK <-> Stride type conversion
```

## Architecture

The asset loading system is organized in tiers:

```
┌─────────────────────────────────────────────────────────────┐
│                   StrideContentManager                       │
│              (Orchestration + Caching Layer)                 │
│                                                              │
│  - Manages AssetCache for loaded assets                      │
│  - Coordinates bundle loading and type conversion            │
│  - Provides async loading with cancellation support          │
└───────────────────────────┬─────────────────────────────────┘
                            │
            ┌───────────────┼───────────────┐
            │               │               │
            v               v               v
┌───────────────┐ ┌───────────────┐ ┌───────────────┐
│  ModelLoader  │ │ TextureLoader │ │AnimationLoader│
│               │ │               │ │               │
│ Creates Model │ │Creates Texture│ │Creates Clip   │
│ from bundle   │ │ from bundle   │ │ from bundle   │
│ vertex/index  │ │ pixel data    │ │ curve data    │
│ buffers       │ │               │ │               │
└───────┬───────┘ └───────┬───────┘ └───────┬───────┘
        │                 │                 │
        └─────────────────┼─────────────────┘
                          │
                          v
            ┌─────────────────────────────┐
            │     BundleAssetLoader       │
            │    (Bundle Reading Layer)   │
            │                             │
            │  - Reads .bannou file format│
            │  - LZ4 decompression        │
            │  - Asset manifest parsing   │
            │  - Raw byte extraction      │
            └─────────────────────────────┘
```

### Loading Tiers

1. **Tier 1: Bundle Reading** (`BundleAssetLoader`)
   - Reads `.bannou` bundle files from disk or streams
   - Handles LZ4 decompression of asset data
   - Parses asset manifests and metadata
   - Returns raw bytes for individual assets

2. **Tier 2: Type-Specific Loaders** (`ModelLoader`, `TextureLoader`, `AnimationClipLoader`)
   - Convert raw bundle data to Stride runtime types
   - Handle GPU resource creation (buffers, textures)
   - Manage format-specific deserialization

3. **Tier 3: Content Management** (`StrideContentManager`)
   - Orchestrates the loading pipeline
   - Manages `AssetCache` for loaded assets
   - Provides unified API for loading any asset type
   - Handles cache hits/misses transparently

## Quick Start

```csharp
using BeyondImmersion.Bannou.SceneComposer;
using BeyondImmersion.Bannou.Stride.SceneComposer;
using BeyondImmersion.Bannou.Stride.SceneComposer.Content;
using Stride.Engine;

public class SceneEditorScript : SyncScript
{
    private SceneComposer _composer;
    private StrideSceneComposerBridge _bridge;
    private StrideContentManager _contentManager;

    public override void Start()
    {
        // Set up content manager for asset loading
        _contentManager = new StrideContentManager(
            GraphicsDevice,
            maxCacheSize: 512 * 1024 * 1024); // 512 MB cache

        // Create the bridge with content manager
        _bridge = new StrideSceneComposerBridge(
            Entity.Scene,
            GraphicsDevice,
            _contentManager);

        // Create the scene composer
        _composer = new SceneComposer(_bridge);
    }

    public override void Update()
    {
        HandleInput();
    }

    private void HandleInput()
    {
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var ray = _bridge.GetMouseRay(Input.MousePosition);
            var hit = _bridge.PickNode(ray);
            if (hit != null)
            {
                _composer.Select(_composer.CurrentScene.FindNode(hit));
            }
        }
    }
}
```

## Asset Loading

### Loading from Bundles

```csharp
// Create content manager
var contentManager = new StrideContentManager(graphicsDevice);

// Register bundle location
contentManager.RegisterBundle("characters", "/path/to/characters.bannou");

// Load assets
var model = await contentManager.LoadModelAsync("characters", "warrior");
var texture = await contentManager.LoadTextureAsync("characters", "warrior_diffuse");
var animation = await contentManager.LoadAnimationAsync("characters", "warrior_idle");
```

### Direct Bundle Access

For lower-level control, use `BundleAssetLoader` directly:

```csharp
using var loader = new BundleAssetLoader("/path/to/bundle.bannou");

// Check if asset exists
if (loader.ContainsAsset("my_model"))
{
    // Get asset metadata
    var metadata = loader.GetAssetMetadata("my_model");
    Console.WriteLine($"Asset type: {metadata.Type}, Size: {metadata.Size}");

    // Load raw asset data
    var data = loader.LoadAssetData("my_model");
}

// List all assets
foreach (var assetId in loader.GetAssetIds())
{
    Console.WriteLine(assetId);
}
```

### Cache Management

The `AssetCache` provides LRU eviction with size tracking:

```csharp
// Create cache with 256 MB limit
var cache = new AssetCache(maxSizeBytes: 256 * 1024 * 1024);

// Add asset with size tracking
cache.Add("asset-id", loadedModel, sizeBytes: modelSize);

// Retrieve cached asset
if (cache.TryGet<Model>("asset-id", out var model))
{
    // Use cached model
}

// Check cache stats
Console.WriteLine($"Cache: {cache.Count} items, {cache.CurrentSize / 1024 / 1024} MB");

// Manual eviction
cache.Remove("asset-id");
cache.Clear();
```

## Type Conversions

SDK uses double precision; Stride uses single precision. Use the `StrideTypeConverter`:

```csharp
using BeyondImmersion.Bannou.Stride.SceneComposer;

// SDK to Stride
var sdkPos = new SdkVector3(10.5, 20.0, 30.0);
var stridePos = StrideTypeConverter.ToStride(sdkPos);

var sdkRot = SdkQuaternion.Identity;
var strideRot = StrideTypeConverter.ToStride(sdkRot);

var sdkTransform = new SdkTransform(position, rotation, scale);
var strideMatrix = StrideTypeConverter.ToStrideMatrix(sdkTransform);

// Stride to SDK
var strideVec = entity.Transform.Position;
var sdkVec = StrideTypeConverter.ToSdk(strideVec);
```

## Bridge Features

### Node Management

```csharp
// Create entity (called by SceneComposer)
bridge.CreateEntity(nodeId, NodeType.Model, transform, asset);

// Update transform
bridge.UpdateEntityTransform(nodeId, worldTransform);

// Set parent
bridge.SetEntityParent(nodeId, parentId);

// Destroy entity
bridge.DestroyEntity(nodeId);
```

### Asset Binding

```csharp
// Bind asset to entity
await bridge.SetEntityAssetAsync(nodeId, new AssetReference(
    bundleId: "props",
    assetId: "crate",
    variantId: "damaged"));

// Clear asset
bridge.ClearEntityAsset(nodeId);
```

### Selection Visualization

```csharp
// Set selected state (shows selection outline)
bridge.SetEntitySelected(nodeId, selected: true);

// Set visibility
bridge.SetEntityVisible(nodeId, visible: false);
```

### Physics Picking

```csharp
// Ray pick
var ray = bridge.GetMouseRay(mousePosition);
string? hitNodeId = bridge.PickNode(ray);

// Rectangular selection
var hits = bridge.PickNodesInRect(screenRect);
```

## Gizmo Rendering

The `StrideGizmoRenderer` provides visual handles for transform operations:

```csharp
var gizmoRenderer = new StrideGizmoRenderer(graphicsDevice);

// Render gizmo at position
gizmoRenderer.Render(
    commandList,
    position: entity.Transform.WorldMatrix.TranslationVector,
    rotation: entity.Transform.Rotation,
    mode: GizmoMode.Translate,
    activeAxis: GizmoAxis.X,
    cameraPosition: camera.Entity.Transform.Position);

// Pick gizmo axis from ray
GizmoAxis axis = gizmoRenderer.PickAxis(
    ray,
    gizmoPosition,
    gizmoRotation,
    GizmoMode.Translate);
```

## Bundle File Format

The `.bannou` bundle format is a proprietary container for Stride-compiled assets:

```
┌──────────────────────────────────┐
│ Header (16 bytes)                │
│  - Magic: "BANN" (4 bytes)       │
│  - Version: uint32               │
│  - Asset Count: uint32           │
│  - Manifest Offset: uint32       │
├──────────────────────────────────┤
│ Asset Data (LZ4 compressed)      │
│  - Asset 1 data                  │
│  - Asset 2 data                  │
│  - ...                           │
├──────────────────────────────────┤
│ Manifest (JSON)                  │
│  - Asset IDs                     │
│  - Types                         │
│  - Offsets and sizes             │
│  - Dependencies                  │
└──────────────────────────────────┘
```

## Platform Notes

| Platform | Status |
|----------|--------|
| Windows | Full functionality |
| Linux | Builds, but Stride runtime limited |
| macOS | Not supported by Stride |

The package builds on all platforms for CI purposes, but full runtime functionality (GPU resources, physics, rendering) requires Windows with Stride properly installed.

## Dependencies

- `Stride.Engine` 4.3.x
- `Stride.Rendering` 4.3.x
- `Stride.Graphics` 4.3.x
- `Stride.Core.Mathematics` 4.3.x
- `K4os.Compression.LZ4` - For bundle decompression
- `BeyondImmersion.Bannou.SceneComposer` - Core SDK

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Stride.SceneComposer
```

This will also install the core SceneComposer SDK as a dependency.

## Testing

The test project (`Bannou.Stride.SceneComposer.Tests`) includes:
- Unit tests for `AssetCache` (LRU eviction, size tracking, thread safety)
- Unit tests for `StrideTypeConverter` (precision, edge cases)
- Integration tests for `BundleAssetLoader` with real `.bannou` bundles
- Tests run on all platforms (mock Stride types where needed)

Run tests:
```bash
dotnet test Bannou.Stride.SceneComposer.Tests
```

## License

MIT License
