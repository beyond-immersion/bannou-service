# Bannou Asset Loader Client

WebSocket-based asset loading SDK for game clients connecting to Bannou.

## Overview

This package provides the consumer-side SDK for loading assets from Bannou:

- **`AssetManager`** - High-level facade for game client integration
- **`BannouWebSocketAssetSource`** - Low-level `IAssetSource` for custom configurations

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetLoader.Client
```

## Quick Start

The `AssetManager` is the recommended entry point for game clients:

```csharp
using BeyondImmersion.Bannou.AssetLoader.Client;

// Connect and configure
await using var manager = await AssetManager.ConnectAsync(
    "wss://bannou.example.com/connect",
    "player@example.com",
    "password",
    new AssetManagerOptions
    {
        CacheDirectory = "./asset-cache",
        Realm = Realm.Arcadia
    });

// Load assets (downloads bundles as needed)
var result = await manager.LoadAssetsAsync(new[]
{
    "synty/character/knight_male",
    "synty/props/barrel_01"
});

// Check availability
Console.WriteLine($"Loaded: {result.AvailableCount}/{result.RequestedAssetIds.Count}");

// Get raw asset bytes
byte[]? data = await manager.GetAssetBytesAsync("synty/character/knight_male");
```

## AssetManager API

### Connection Methods

```csharp
// Email/password authentication
var manager = await AssetManager.ConnectAsync(serverUrl, email, password, options);

// Service token authentication
var manager = await AssetManager.ConnectWithTokenAsync(serverUrl, token, options);

// Use existing BannouClient connection
var manager = AssetManager.FromClient(existingClient, options);
```

### Configuration Options

```csharp
var options = new AssetManagerOptions
{
    CacheDirectory = "./asset-cache",     // Disk cache location
    Realm = Realm.Arcadia,                // Default realm for resolution
    MaxConcurrentDownloads = 4,           // Parallel download limit
    ValidateBundles = true,               // SHA256 integrity checks
    PreferCache = true,                   // Offline-first behavior
    MaxCacheSizeBytes = 1024L * 1024 * 1024, // 1GB max cache
    EnableCache = true                    // Enable disk caching
};
```

### Loading Assets

```csharp
// Load multiple assets with progress reporting
var progress = new Progress<AssetLoadProgress>(p =>
{
    Console.WriteLine($"Phase: {p.Phase}, Progress: {p.Progress:P0}");
});

var result = await manager.LoadAssetsAsync(assetIds, progress);

// Check results
if (result.AllAvailable)
{
    Console.WriteLine($"Loaded {result.DownloadedBundleIds.Count} bundles");
}
else
{
    Console.WriteLine($"Missing: {string.Join(", ", result.UnresolvedAssetIds)}");
}
```

### Type Loaders (Engine Integration)

Register type loaders for engine-specific deserialization:

```csharp
// Register a Stride model loader
manager.RegisterTypeLoader(new StrideModelLoader());

// Load typed asset
var result = await manager.GetAssetAsync<Model>("synty/character/knight_male");
if (result.Success)
{
    var model = result.Asset;
    // Use the model
}
```

### Memory Management

```csharp
// Unload specific bundle
manager.UnloadBundle("synty-characters-v1");

// Unload all bundles
manager.UnloadAllBundles();

// Clear disk cache
await manager.ClearCacheAsync();

// Get cache statistics
var stats = manager.CacheStats;
Console.WriteLine($"Cache: {stats.BundleCount} bundles, {stats.TotalBytes / 1024 / 1024}MB");
```

### Querying State

```csharp
// Check if asset/bundle is loaded
bool hasAsset = manager.HasAsset("synty/character/knight_male");
bool hasBundle = manager.HasBundle("synty-characters-v1");

// Get counts
Console.WriteLine($"Bundles: {manager.LoadedBundleCount}, Assets: {manager.LoadedAssetCount}");

// Check connection
if (!manager.IsConnected)
{
    // Handle disconnection
}
```

## Low-Level Usage

For custom configurations, use `BannouWebSocketAssetSource` directly:

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Client;
using BeyondImmersion.Bannou.AssetLoader.Cache;

// Connect and create asset source
var source = await BannouWebSocketAssetSource.ConnectAsync(
    "wss://bannou.example.com/connect",
    "user@example.com",
    "password",
    defaultRealm: Realm.Arcadia);

// Create loader with custom options
var cache = new FileAssetCache("./cache", maxSizeBytes: 512 * 1024 * 1024);
var loader = new AssetLoader(source, cache, new AssetLoaderOptions
{
    MaxConcurrentDownloads = 2,
    ValidateBundles = false  // Skip validation for trusted sources
});

// Load assets
var result = await loader.EnsureAssetsAvailableAsync(assetIds);
```

### Using Existing BannouClient

```csharp
// If you already have a BannouClient for other purposes
var client = new BannouClient();
await client.ConnectAsync(serverUrl, email, password);

// Create source from existing client (doesn't take ownership)
var source = new BannouWebSocketAssetSource(client);
var loader = new AssetLoader(source);
```

## Features

| Feature | Description |
|---------|-------------|
| Server-side resolution | Optimal bundle selection via `/bundles/resolve` |
| Metabundle support | Automatically prefers metabundles when available |
| Pre-signed URLs | Downloads bypass WebSocket for large files |
| LRU disk cache | Persists across application restarts |
| Progress reporting | Real-time download progress callbacks |
| Type-safe loading | Generic `GetAssetAsync<T>()` with registered loaders |
| Thread-safe | Concurrent bundle loading with semaphore |

## When to Use This vs Server SDK

| Scenario | SDK |
|----------|-----|
| Game clients | **Asset Loader Client** (this package) |
| Developer tools | **Asset Loader Client** (this package) |
| Game servers | Asset Loader Server |
| Backend services | Asset Loader Server or direct API |

## Further Reading

- [Asset SDK Guide](../../docs/guides/ASSET-SDK.md) - Comprehensive documentation
- [Asset Loader Core](../asset-loader/README.md) - Engine-agnostic core SDK
- [Bannou Client SDK](../client/README.md) - WebSocket client documentation

## License

MIT
