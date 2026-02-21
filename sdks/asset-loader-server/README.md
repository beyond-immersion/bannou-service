# Bannou Asset Loader Server

Mesh-based asset source for game servers and backend services.

## Overview

This package provides `BannouMeshAssetSource`, an `IAssetSource` implementation that uses the generated `IAssetClient` for URL resolution via the service mesh.

**Important**: Most server use cases don't need this package. If you only need:
- Asset metadata queries
- Download URLs to pass to clients/workers
- Bundle validation

Then use the generated `IAssetClient` directly (see "Direct API Access" below).

Use this package when your server needs to **actually load bundle contents** into memory.

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetLoader.Server
```

## When to Use This Package

| Use Case | This Package? | Alternative |
|----------|---------------|-------------|
| Query asset metadata | No | Direct `IAssetClient` |
| Get download URLs | No | Direct `IAssetClient` |
| Validate uploads | No | Direct `IAssetClient` |
| Server-side physics/collision | **Yes** | - |
| NPC AI reading asset data | **Yes** | - |
| Asset processing pipelines | Maybe | Direct MinIO access |

## Direct API Access (Most Common)

For metadata and URL queries, use the generated client directly:

```csharp
public class AssetQueryService
{
    private readonly IAssetClient _assetClient;

    public AssetQueryService(IAssetClient assetClient)
    {
        _assetClient = assetClient;
    }

    public async Task<AssetWithDownloadUrl> GetAssetInfoAsync(string assetId)
    {
        var request = new GetAssetRequest { AssetId = assetId, Version = "latest" };
        return await _assetClient.GetAssetAsync(request);
    }

    public async Task<string> GetBundleUrlAsync(string bundleId)
    {
        var request = new GetBundleRequest { BundleId = bundleId };
        var response = await _assetClient.GetBundleAsync(request);
        return response.DownloadUrl;  // Pre-signed URL
    }
}
```

## Full Asset Loading (When Needed)

When your server needs actual asset bytes:

### With Dependency Injection

```csharp
// Service registration
services.AddSingleton<IAssetSource>(sp =>
{
    var assetClient = sp.GetRequiredService<IAssetClient>();
    var logger = sp.GetService<ILogger<BannouMeshAssetSource>>();
    return new BannouMeshAssetSource(assetClient, Realm.Arcadia, logger);
});

// Optional: memory cache for containers (or FileAssetCache for VMs)
services.AddSingleton<IAssetCache>(sp =>
    new MemoryAssetCache(maxSizeBytes: 256 * 1024 * 1024));

services.AddSingleton<AssetLoader>();
```

### Direct Usage

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Server;
using BeyondImmersion.Bannou.AssetLoader.Cache;

// Create source from injected client
var source = new BannouMeshAssetSource(assetClient, Realm.Arcadia);

// Optional cache
var cache = new MemoryAssetCache(maxSizeBytes: 256 * 1024 * 1024);

// Create loader
var loader = new AssetLoader(source, cache);

// Load assets
var result = await loader.EnsureAssetsAvailableAsync(new[]
{
    "polygon-adventure/level-1",
    "polygon-adventure/enemies"
});

// Get raw bytes
var levelData = await loader.GetAssetBytesAsync("polygon-adventure/level-1");
```

## Caching Recommendations

| Environment | Cache Type | Reason |
|-------------|------------|--------|
| Kubernetes/Docker | `MemoryAssetCache` | Ephemeral filesystem |
| VMs with disk | `FileAssetCache` | Survives restarts |
| Serverless | None | Minimal lifetime |
| Processing workers | `MemoryAssetCache` | Process then discard |

## Features

| Feature | Description |
|---------|-------------|
| Mesh-based resolution | Uses service-to-service mesh for URL resolution |
| No auth overhead | Service identity handles authentication automatically |
| Metabundle support | Automatically prefers metabundles when available |
| Pre-signed URLs | Downloads use pre-signed URLs for direct storage access |

## Comparison with Client SDK

| Feature | Asset Loader Client | Asset Loader Server |
|---------|---------------------|---------------------|
| Transport | WebSocket | HTTP mesh |
| Authentication | User credentials | Service identity |
| Use case | Game clients, dev tools | Game servers, backend |
| Connection | Long-lived | Per-request |
| Facade | `AssetManager` | None (use `AssetLoader` directly) |

## Further Reading

- [Asset SDK Guide](../../docs/guides/ASSET-SDK.md) - Comprehensive documentation
- [Asset Loader Core](../asset-loader/README.md) - Engine-agnostic core SDK
- [Asset Loader Client](../asset-loader-client/README.md) - Game client SDK

## License

MIT
