# Bannou Asset Loader Server

Mesh-based asset source for the Bannou Asset Loader SDK.

## Overview

This package provides `BannouMeshAssetSource`, an `IAssetSource` implementation that uses the generated `IAssetClient` for URL resolution via the service mesh. Use this for:

- Game servers
- Backend services
- Any application that communicates via the Bannou mesh

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetLoader.Server
```

## Usage

### With Dependency Injection

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Server;
using BeyondImmersion.Bannou.AssetLoader.Cache;

// In your service registration
services.AddSingleton<IAssetSource>(sp =>
{
    var assetClient = sp.GetRequiredService<IAssetClient>();
    var logger = sp.GetService<ILogger<BannouMeshAssetSource>>();
    return new BannouMeshAssetSource(assetClient, Realm.Arcadia, logger);
});

services.AddSingleton<IAssetCache>(sp =>
    new FileAssetCache("/var/cache/assets"));

services.AddSingleton<AssetLoader>();
```

### Direct Usage

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Server;
using BeyondImmersion.Bannou.AssetLoader.Cache;

// AssetClient is typically injected or obtained from DI
var source = new BannouMeshAssetSource(assetClient, Realm.Arcadia);
var cache = new FileAssetCache("/var/cache/assets");
var loader = new AssetLoader(source, cache);

// Load assets for game state
var result = await loader.EnsureAssetsAvailableAsync(new[]
{
    "polygon-adventure/level-1",
    "polygon-adventure/enemies"
});

// Get raw bytes for processing
var levelData = await loader.GetAssetBytesAsync("polygon-adventure/level-1");
```

## Features

- **Mesh-based resolution** - Uses service-to-service mesh for URL resolution
- **No authentication overhead** - Service identity handles auth automatically
- **Metabundle support** - Automatically prefers metabundles when available
- **Pre-signed URLs** - Downloads use pre-signed URLs for direct storage access

## Comparison with Client SDK

| Feature | Asset Loader Client | Asset Loader Server |
|---------|---------------------|---------------------|
| Transport | WebSocket | HTTP mesh |
| Authentication | User credentials | Service identity |
| Use case | Game clients, dev tools | Game servers, backend |
| Connection | Long-lived | Per-request |

## Known Limitations

**Current Architecture**: This package currently references `bannou-service` directly to access the generated `IAssetClient` interface and related types. This means:

- External consumers cannot use this package without the full Bannou server codebase
- The package cannot be published to NuGet in its current form

**Planned Fix**: Extract asset service types into a standalone `BeyondImmersion.Bannou.AssetService.Client` package that can be consumed independently.

## License

MIT
