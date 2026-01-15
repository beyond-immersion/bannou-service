# Bannou Asset Loader SDK

Engine-agnostic asset loading SDK for downloading, caching, and deserializing Bannou asset bundles.

## Overview

The Asset Loader SDK provides a layered architecture for loading assets from Bannou bundles:

- **Core Layer** - Bundle reading, HTTP downloading, disk/memory caching (no service dependencies)
- **Network Adapters** - URL resolution via WebSocket (clients) or mesh (servers)
- **Engine Extensions** - Type-specific deserialization for Stride, Godot, Unity

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetLoader
```

For service integration:
```bash
# For game clients (WebSocket)
dotnet add package BeyondImmersion.Bannou.AssetLoader.Client

# For game servers (mesh)
dotnet add package BeyondImmersion.Bannou.AssetLoader.Server
```

For engine support:
```bash
dotnet add package BeyondImmersion.Bannou.AssetLoader.Stride
dotnet add package BeyondImmersion.Bannou.AssetLoader.Godot
```

## Usage

### Basic Usage (Direct URLs)

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Cache;
using BeyondImmersion.Bannou.AssetLoader.Sources;

// Create a source with known URLs
var source = new HttpAssetSource();
source.RegisterBundle(
    "my-bundle",
    new Uri("https://cdn.example.com/bundles/my-bundle.bannou"),
    1024 * 1024,
    new[] { "asset-1", "asset-2" });

// Create cache and loader
var cache = new FileAssetCache("./cache");
var loader = new AssetLoader(source, cache);

// Load assets
var result = await loader.EnsureAssetsAvailableAsync(new[] { "asset-1", "asset-2" });

// Get raw bytes
var bytes = await loader.GetAssetBytesAsync("asset-1");
```

### With WebSocket Client (Game Clients)

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Client;
using BeyondImmersion.Bannou.AssetLoader.Cache;

// Connect to Bannou
var source = await BannouWebSocketAssetSource.ConnectAsync(
    "wss://bannou.example.com",
    "user@example.com",
    "password");

var cache = new FileAssetCache("./asset-cache");
var loader = new AssetLoader(source, cache);

// Assets are resolved via server-side bundle optimization
var result = await loader.EnsureAssetsAvailableAsync(new[]
{
    "polygon-adventure/hero-model",
    "polygon-adventure/hero-texture"
});

// Get typed assets (with engine extension)
loader.UseStride(services);
var model = await loader.LoadAssetAsync<Model>("polygon-adventure/hero-model");
```

### Local Files (Offline Mode)

```csharp
var source = new FileSystemAssetSource();
await source.ScanDirectoryAsync("./bundles");

var loader = new AssetLoader(source); // No cache needed for local files

var result = await loader.EnsureAssetsAvailableAsync(assetIds);
```

## Architecture

```
AssetLoader (orchestrator)
    │
    ├── IAssetSource (URL resolution)
    │   ├── HttpAssetSource (direct URLs)
    │   ├── FileSystemAssetSource (local files)
    │   ├── BannouWebSocketAssetSource (game clients)
    │   └── BannouMeshAssetSource (game servers)
    │
    ├── IAssetCache (persistence)
    │   ├── FileAssetCache (disk LRU)
    │   └── MemoryAssetCache (in-memory)
    │
    ├── BundleRegistry (loaded bundle tracking)
    │
    └── IAssetTypeLoader<T> (engine-specific)
        ├── StrideModelLoader
        ├── StrideTextureLoader
        ├── GodotMeshLoader
        └── ...
```

## Key Features

- **Zero service dependencies** - Core SDK works offline
- **Pluggable sources** - WebSocket, mesh, HTTP, or local files
- **LRU caching** - Disk and memory caches with eviction
- **Progress reporting** - Download progress callbacks
- **Type-safe loading** - Generic `LoadAssetAsync<T>()` with registered loaders
- **Thread-safe** - Concurrent bundle loading with semaphore

## License

MIT
