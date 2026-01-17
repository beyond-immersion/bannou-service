# Bannou Asset Loader SDK

Engine-agnostic core SDK for downloading, caching, and deserializing Bannou asset bundles.

## Overview

The Asset Loader SDK provides a layered architecture for loading assets from Bannou bundles:

- **Core Layer** - Bundle reading, HTTP downloading, disk/memory caching
- **Network Adapters** - URL resolution via WebSocket (clients) or mesh (servers)
- **Engine Extensions** - Type-specific deserialization for Stride, Godot, Unity

This is the **core package** - it has no network dependencies. For game clients, use `asset-loader-client` which provides the `AssetManager` facade. For servers, use `asset-loader-server` or direct API access.

## Installation

```bash
# Core SDK (required by all)
dotnet add package BeyondImmersion.Bannou.AssetLoader

# For game clients (recommended)
dotnet add package BeyondImmersion.Bannou.AssetLoader.Client

# For game servers (when loading bundle contents)
dotnet add package BeyondImmersion.Bannou.AssetLoader.Server

# For engine support
dotnet add package BeyondImmersion.Bannou.AssetLoader.Stride
```

## Quick Start (Game Clients)

For game clients, use the `AssetManager` facade from `asset-loader-client`:

```csharp
using BeyondImmersion.Bannou.AssetLoader.Client;

// Connect and load assets
await using var manager = await AssetManager.ConnectAsync(
    "wss://bannou.example.com/connect",
    email, password,
    new AssetManagerOptions { CacheDirectory = "./cache" });

await manager.LoadAssetsAsync(assetIds);
var bytes = await manager.GetAssetBytesAsync(assetId);
```

See the [Asset Loader Client README](../asset-loader-client/README.md) for full documentation.

## Core Usage (Advanced)

For custom configurations or when building your own facade:

### Direct URL Loading

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Cache;
using BeyondImmersion.Bannou.AssetLoader.Sources;

// Create source with pre-known URLs
var source = new HttpAssetSource();
source.RegisterBundle(
    "my-bundle",
    new Uri("https://cdn.example.com/bundles/my-bundle.bannou"),
    sizeBytes: 1024 * 1024,
    assetIds: new[] { "asset-1", "asset-2" });

// Create cache and loader
var cache = new FileAssetCache("./cache");
var loader = new AssetLoader(source, cache);

// Load assets
var result = await loader.EnsureAssetsAvailableAsync(new[] { "asset-1", "asset-2" });
var bytes = await loader.GetAssetBytesAsync("asset-1");
```

### Local File Loading

```csharp
// For development or offline testing
var source = new HttpAssetSource();
source.RegisterBundle(
    "local-bundle",
    new Uri("file:///path/to/bundle.bannou"),
    sizeBytes: 0,
    assetIds: new[] { "asset-1" });

var loader = new AssetLoader(source);  // No cache needed for local files
```

### Custom Options

```csharp
var options = new AssetLoaderOptions
{
    MaxConcurrentDownloads = 4,      // Parallel download limit
    ValidateBundles = true,          // SHA256 integrity checks
    PreferCache = true,              // Use cache over fresh downloads
    AutoRegisterBundles = true       // Auto-register after download
};

var loader = new AssetLoader(source, cache, options);
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         AssetManager (Facade)                       │
│              (in asset-loader-client, for game clients)             │
└─────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                          AssetLoader (Core)                         │
│  - EnsureAssetsAvailableAsync() - resolution + download             │
│  - LoadBundleAsync() - single bundle loading                        │
│  - LoadAssetAsync<T>() - typed deserialization                      │
│  - GetAssetBytesAsync() - raw bytes                                 │
└─────────────────────────────────────────────────────────────────────┘
         │
    ┌────┴────────────────────┬──────────────────────┐
    ▼                         ▼                      ▼
┌────────────────┐   ┌────────────────┐   ┌────────────────────┐
│  IAssetSource  │   │  IAssetCache   │   │  BundleRegistry    │
│  (URL resolve) │   │  (persistence) │   │  (O(1) lookup)     │
└────────────────┘   └────────────────┘   └────────────────────┘
         │                    │
         │                    ▼
         │           ┌────────────────┐   ┌────────────────┐
         │           │ FileAssetCache │   │MemoryAssetCache│
         │           │  (disk LRU)    │   │  (in-memory)   │
         │           └────────────────┘   └────────────────┘
         ▼
┌─────────────────────────┐   ┌─────────────────────────┐
│ BannouWebSocketAssetSource │   │ BannouMeshAssetSource    │
│   (asset-loader-client) │   │   (asset-loader-server) │
└─────────────────────────┘   └─────────────────────────┘
```

## Components

### IAssetSource Implementations

| Source | Package | Use Case |
|--------|---------|----------|
| `BannouWebSocketAssetSource` | `asset-loader-client` | Game clients |
| `BannouMeshAssetSource` | `asset-loader-server` | Game servers |
| `HttpAssetSource` | `asset-loader` | Direct URLs, offline |

### IAssetCache Implementations

| Cache | Best For | Persistence |
|-------|----------|-------------|
| `FileAssetCache` | Game clients, VMs | Disk-based, survives restarts |
| `MemoryAssetCache` | Containers, testing | In-memory only |

### BundleRegistry

Thread-safe registry providing O(1) asset-to-bundle lookup:

```csharp
// Typically accessed via loader
var registry = loader.Registry;

bool hasAsset = registry.HasAsset("asset-id");
bool hasBundle = registry.HasBundle("bundle-id");
string? bundleId = registry.FindBundleForAsset("asset-id");
```

## Type Loaders

Register engine-specific loaders for typed asset loading:

```csharp
public class MyModelLoader : AssetTypeLoaderBase<MyModel>
{
    public override IReadOnlyList<string> SupportedContentTypes =>
        new[] { "application/x-my-model" };

    public override async Task<MyModel> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct)
    {
        return MyModelSerializer.Deserialize(data.ToArray());
    }

    public override void Unload(MyModel asset)
    {
        asset.Dispose();
    }
}

// Register
loader.RegisterTypeLoader(new MyModelLoader());

// Load typed
var result = await loader.LoadAssetAsync<MyModel>("asset-id");
if (result.Success)
{
    var model = result.Asset;
}
```

## Key Features

| Feature | Description |
|---------|-------------|
| Zero service dependencies | Core SDK works offline with `HttpAssetSource` |
| Pluggable sources | WebSocket, mesh, HTTP, or custom |
| LRU caching | Disk and memory caches with eviction |
| Progress reporting | `IProgress<BundleDownloadProgress>` callbacks |
| Type-safe loading | Generic `LoadAssetAsync<T>()` |
| Thread-safe | Concurrent loading with semaphore |
| Integrity validation | Optional SHA256 hash verification |

## Package Hierarchy

```
BeyondImmersion.Bannou.AssetLoader           ← Core (this package)
    │
    ├── BeyondImmersion.Bannou.AssetLoader.Client   ← Game clients
    │       └── AssetManager facade
    │       └── BannouWebSocketAssetSource
    │
    ├── BeyondImmersion.Bannou.AssetLoader.Server   ← Game servers
    │       └── BannouMeshAssetSource
    │
    └── BeyondImmersion.Bannou.AssetLoader.Stride   ← Engine support
            └── StrideModelTypeLoader
            └── StrideTextureTypeLoader
```

## Further Reading

- [Asset SDK Guide](../../docs/guides/ASSET_SDK.md) - Comprehensive documentation
- [Asset Loader Client](../asset-loader-client/README.md) - Game client SDK with `AssetManager`
- [Asset Loader Server](../asset-loader-server/README.md) - Server-side usage
- [Bundle Format](../bundle-format/README.md) - `.bannou` file format

## License

MIT
