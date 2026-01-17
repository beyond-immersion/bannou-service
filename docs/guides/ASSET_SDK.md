# Bannou Asset SDK Guide

This guide explains how to use the Bannou Asset SDKs for both **consuming** assets in game clients and **producing** bundles with content tools.

## Overview

The Bannou asset system has two sides:

| Side | SDK | Used By | Purpose |
|------|-----|---------|---------|
| **Consumer** | `asset-loader-client` | Game clients | Download and load bundles |
| **Producer** | `asset-bundler` | SyntyBundler, content tools | Create and upload bundles |

## Quick Start (Consumer)

The `AssetManager` is the primary entry point for game clients:

```csharp
using BeyondImmersion.Bannou.AssetLoader.Client;

// Connect to server
await using var manager = await AssetManager.ConnectAsync(
    "wss://bannou.example.com/connect",
    email,
    password,
    new AssetManagerOptions
    {
        CacheDirectory = "./asset-cache",
        Realm = Realm.Arcadia
    });

// Load assets by ID
var assetIds = new[] { "synty/character/knight_male", "synty/props/barrel_01" };
var result = await manager.LoadAssetsAsync(assetIds);

// Check availability
if (result.AllAvailable)
{
    Console.WriteLine($"Loaded {result.DownloadedBundleIds.Count} bundles");
}

// Get raw bytes for a loaded asset
byte[]? data = await manager.GetAssetBytesAsync("synty/character/knight_male");
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         AssetManager (Facade)                       │
│  - ConnectAsync() / ConnectWithTokenAsync() / FromClient()          │
│  - LoadAssetsAsync() / LoadAssetAsync()                             │
│  - GetAssetAsync<T>() / GetAssetBytesAsync()                        │
│  - RegisterTypeLoader<T>()                                          │
└─────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────┐
│                          AssetLoader (Core)                         │
│  - EnsureAssetsAvailableAsync() - resolution + download             │
│  - LoadBundleAsync() - single bundle loading                        │
│  - LoadAssetAsync<T>() - typed deserialization                      │
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
         │           ┌────────────────┐
         │           │ FileAssetCache │  ← Disk-based LRU cache
         │           └────────────────┘
         ▼
┌─────────────────────────┐
│ BannouWebSocketAssetSource │  ← Calls /bundles/resolve, /bundles/get
└─────────────────────────┘
```

## Consumer Guide: Game Client Integration

### Initialization Options

```csharp
var options = new AssetManagerOptions
{
    // Where to store cached bundles (persists across restarts)
    CacheDirectory = "./asset-cache",

    // Default realm for bundle resolution
    Realm = Realm.Arcadia,

    // Parallel download limit
    MaxConcurrentDownloads = 4,

    // Validate bundle integrity with SHA256
    ValidateBundles = true,

    // Prefer cached bundles over fresh downloads
    PreferCache = true,

    // Maximum cache size (1GB default)
    MaxCacheSizeBytes = 1024L * 1024 * 1024,

    // Enable/disable disk caching
    EnableCache = true
};
```

### Connection Methods

```csharp
// Email/password authentication
var manager = await AssetManager.ConnectAsync(
    serverUrl, email, password, options, loggerFactory);

// Service token authentication
var manager = await AssetManager.ConnectWithTokenAsync(
    serverUrl, serviceToken, options, loggerFactory);

// Use existing BannouClient connection
var manager = AssetManager.FromClient(existingClient, options, loggerFactory);
```

### Loading Assets

```csharp
// Load multiple assets (downloads bundles as needed)
var result = await manager.LoadAssetsAsync(assetIds, progress: progressReporter);

// Check results
Console.WriteLine($"Available: {result.AvailableCount}/{result.RequestedAssetIds.Count}");
Console.WriteLine($"Bundles downloaded: {result.DownloadedBundleIds.Count}");
Console.WriteLine($"Unresolved: {string.Join(", ", result.UnresolvedAssetIds)}");

// Load single asset
bool loaded = await manager.LoadAssetAsync("synty/character/knight_male");
```

### Progress Reporting

```csharp
var progress = new Progress<AssetLoadProgress>(p =>
{
    switch (p.Phase)
    {
        case AssetLoadPhase.Resolving:
            Console.WriteLine("Resolving bundles...");
            break;

        case AssetLoadPhase.Downloading:
            Console.WriteLine($"Downloading: {p.CompletedBundles}/{p.TotalBundles} " +
                $"({p.Progress:P0}) - {p.CurrentBundleId}");
            break;

        case AssetLoadPhase.Complete:
            Console.WriteLine("Loading complete!");
            break;
    }
});

await manager.LoadAssetsAsync(assetIds, progress);
```

### Type Loaders (Engine Integration)

Register type loaders to deserialize assets into engine-native types:

```csharp
// Stride example
public class StrideModelLoader : AssetTypeLoaderBase<Model>
{
    public override IReadOnlyList<string> SupportedContentTypes =>
        new[] { "application/x-stride-model", "model/gltf-binary" };

    public override async Task<Model> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct)
    {
        // Deserialize using Stride's asset system
        return await StrideAssetLoader.LoadModel(data.ToArray());
    }

    public override void Unload(Model asset)
    {
        asset.Dispose();
    }
}

// Register the loader
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
Console.WriteLine($"Cache: {stats.BundleCount} bundles, " +
    $"{stats.TotalBytes / 1024 / 1024}MB / {stats.MaxBytes / 1024 / 1024}MB");
```

### Querying Loaded State

```csharp
// Check if asset is loaded
if (manager.HasAsset("synty/character/knight_male"))
{
    var bytes = await manager.GetAssetBytesAsync("synty/character/knight_male");
}

// Check bundle state
if (manager.HasBundle("synty-characters-v1"))
{
    // Bundle is loaded
}

// Get all loaded bundle IDs
foreach (var bundleId in manager.GetLoadedBundleIds())
{
    Console.WriteLine($"Loaded: {bundleId}");
}

// Get counts
Console.WriteLine($"Bundles: {manager.LoadedBundleCount}");
Console.WriteLine($"Assets: {manager.LoadedAssetCount}");
```

## Producer Guide: Creating Bundles

See SyntyBundler (`~/repos/stride-kb/tools/SyntyBundler`) for a reference implementation.

### Bundle Creation Workflow

```csharp
using BeyondImmersion.Bannou.AssetBundler;

// Create bundle writer
using var writer = new BannouBundleWriter();

// Add assets
writer.AddAsset(
    assetId: "synty/character/knight_male",
    contentType: "application/x-stride-model",
    data: modelBytes);

writer.AddAsset(
    assetId: "synty/texture/knight_diffuse",
    contentType: "image/png",
    data: textureBytes);

// Write bundle to stream
using var stream = File.Create("characters.bannou");
await writer.WriteAsync(stream);
```

### Upload with Progress

```csharp
using BeyondImmersion.Bannou.AssetBundler.Upload;

var uploader = new BannouUploader(client);

// Upload bundle (handles multipart for large files)
var result = await uploader.UploadBundleAsync(
    bundlePath: "characters.bannou",
    bundleId: "synty-characters-v1",
    realm: Realm.Arcadia,
    progress: uploadProgress);

Console.WriteLine($"Uploaded: {result.BundleId}");
```

### Creating Metabundles

Metabundles combine multiple bundles for efficient downloads:

```csharp
// Via API
var response = await client.Asset.CreateMetabundleAsync(new CreateMetabundleRequest
{
    Name = "arcadia-characters-v1",
    Description = "All character assets for Arcadia",
    SourceBundleIds = { "synty-characters-v1", "synty-animations-v1" },
    Realm = Realm.Arcadia
});
```

## Engine Integration Patterns

### Stride

```csharp
public class StrideAssetManager
{
    private readonly AssetManager _manager;
    private readonly ContentManager _content;

    public StrideAssetManager(AssetManager manager, ContentManager content)
    {
        _manager = manager;
        _content = content;

        // Register Stride type loaders
        _manager.RegisterTypeLoader(new StrideModelLoader(_content));
        _manager.RegisterTypeLoader(new StrideTextureLoader(_content));
    }

    public async Task<Model> LoadModelAsync(string assetId)
    {
        // Ensure asset is available
        await _manager.LoadAssetAsync(assetId);

        // Load as typed asset
        var result = await _manager.GetAssetAsync<Model>(assetId);
        if (!result.Success)
            throw new AssetLoadException(result.ErrorMessage);

        return result.Asset;
    }
}
```

### Godot

```csharp
public class GodotAssetBridge
{
    private readonly AssetManager _manager;

    public async Task<PackedScene> LoadSceneAsync(string assetId)
    {
        var bytes = await _manager.GetAssetBytesAsync(assetId);
        if (bytes == null)
            throw new FileNotFoundException(assetId);

        // Save to temp file and load via Godot
        var tempPath = $"user://temp/{assetId.Replace('/', '_')}.tscn";
        var file = FileAccess.Open(tempPath, FileAccess.ModeFlags.Write);
        file.StoreBuffer(bytes);
        file.Close();

        return ResourceLoader.Load<PackedScene>(tempPath);
    }
}
```

### Unity

```csharp
public class UnityAssetLoader : MonoBehaviour
{
    private AssetManager _manager;

    async void Start()
    {
        _manager = await AssetManager.ConnectAsync(serverUrl, email, password);
        _manager.RegisterTypeLoader(new UnityMeshLoader());
    }

    public async Task<Mesh> LoadMeshAsync(string assetId)
    {
        var result = await _manager.GetAssetAsync<Mesh>(assetId);
        return result.Success ? result.Asset : null;
    }

    async void OnDestroy()
    {
        if (_manager != null)
            await _manager.DisposeAsync();
    }
}
```

## Error Handling

```csharp
try
{
    var result = await manager.LoadAssetsAsync(assetIds);

    if (result.UnresolvedAssetIds.Any())
    {
        // Some assets don't exist in the system
        foreach (var missing in result.UnresolvedAssetIds)
        {
            Debug.LogWarning($"Asset not found: {missing}");
        }
    }
}
catch (InvalidOperationException ex) when (ex.Message.Contains("not connected"))
{
    // Connection lost
    await ReconnectAsync();
}
catch (AssetSourceException ex)
{
    // API error (server-side)
    Debug.LogError($"Server error: {ex.Message}");
}
```

## Best Practices

1. **Batch asset loading**: Load multiple assets at once to minimize API calls
2. **Enable caching**: Disk cache dramatically improves startup time
3. **Preload assets**: Load assets during loading screens, not during gameplay
4. **Monitor cache size**: Set appropriate `MaxCacheSizeBytes` for your platform
5. **Handle disconnections**: Check `IsConnected` and implement reconnection logic
6. **Unload unused bundles**: Free memory when assets are no longer needed
7. **Use typed loaders**: Register engine-specific loaders for proper deserialization

## Troubleshooting

### Common Issues

**"Not connected to Bannou server"**
- Check network connectivity
- Verify server URL is correct
- Ensure credentials are valid

**"Asset not found in any loaded bundle"**
- Call `LoadAssetsAsync()` before `GetAssetBytesAsync()`
- Check `result.UnresolvedAssetIds` for missing assets

**"No type loader registered for content type"**
- Register a type loader with `RegisterTypeLoader<T>()`
- Verify the content type matches the asset's metadata

**Bundle integrity validation failed**
- Bundle may be corrupted in cache; clear cache and retry
- Network issue during download; retry with fresh download

### Logging

Enable logging for diagnostics:

```csharp
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
});

var manager = await AssetManager.ConnectAsync(
    serverUrl, email, password, options, loggerFactory);
```

## API Reference

### AssetManager

| Method | Description |
|--------|-------------|
| `ConnectAsync()` | Connect with email/password |
| `ConnectWithTokenAsync()` | Connect with service token |
| `FromClient()` | Wrap existing BannouClient |
| `LoadAssetsAsync()` | Ensure assets are available |
| `LoadAssetAsync()` | Load single asset |
| `GetAssetBytesAsync()` | Get raw asset bytes |
| `GetAssetAsync<T>()` | Get typed asset |
| `GetAssetEntry()` | Get asset metadata |
| `HasAsset()` | Check if asset is loaded |
| `HasBundle()` | Check if bundle is loaded |
| `RegisterTypeLoader<T>()` | Register type deserializer |
| `UnloadBundle()` | Unload specific bundle |
| `UnloadAllBundles()` | Unload all bundles |
| `ClearCacheAsync()` | Clear disk cache |

### AssetManagerOptions

| Property | Default | Description |
|----------|---------|-------------|
| `CacheDirectory` | `"./asset-cache"` | Disk cache location |
| `Realm` | `Realm.Shared` | Default realm for resolution |
| `MaxConcurrentDownloads` | `4` | Parallel download limit |
| `ValidateBundles` | `true` | SHA256 integrity checks |
| `PreferCache` | `true` | Use cache over fresh downloads |
| `MaxCacheSizeBytes` | 1GB | Maximum cache size |
| `EnableCache` | `true` | Enable disk caching |

### AssetLoadProgress

| Property | Description |
|----------|-------------|
| `Phase` | Current phase (Resolving, Downloading, Complete, Failed) |
| `TotalBundles` | Total bundles to download |
| `CompletedBundles` | Bundles completed |
| `TotalBytes` | Total bytes to download |
| `DownloadedBytes` | Bytes downloaded |
| `CurrentBundleId` | Currently downloading bundle |
| `Progress` | Overall progress (0.0-1.0) |
| `EstimatedTimeRemaining` | ETA based on download speed |
