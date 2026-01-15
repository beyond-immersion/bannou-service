# Bannou Asset Loader Client

WebSocket-based asset source for the Bannou Asset Loader SDK.

## Overview

This package provides `BannouWebSocketAssetSource`, an `IAssetSource` implementation that uses the Bannou Client SDK (WebSocket) for URL resolution. Use this for:

- Game clients
- Developer tools (level editors, asset browsers)
- Any application that connects to Bannou via WebSocket

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetLoader.Client
```

## Usage

### With Email/Password

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Client;
using BeyondImmersion.Bannou.AssetLoader.Cache;

// Connect and create asset source
var source = await BannouWebSocketAssetSource.ConnectAsync(
    "wss://bannou.example.com",
    "user@example.com",
    "password");

// Create loader with the source
var cache = new FileAssetCache("./cache");
var loader = new AssetLoader(source, cache);

// Load assets
var result = await loader.EnsureAssetsAvailableAsync(new[]
{
    "polygon-adventure/hero-model",
    "polygon-adventure/hero-texture"
});
```

### With Existing Client

```csharp
// If you already have a BannouClient
var client = new BannouClient();
await client.ConnectAsync(serverUrl, email, password);

// Create source from existing client
var source = new BannouWebSocketAssetSource(client);
var loader = new AssetLoader(source);
```

### With Service Token

```csharp
// For service-to-service authentication
var source = await BannouWebSocketAssetSource.ConnectWithTokenAsync(
    "wss://bannou.example.com",
    serviceToken);

var loader = new AssetLoader(source);
```

## Features

- **Server-side bundle resolution** - Optimal bundle selection via `/bundles/resolve`
- **Metabundle support** - Automatically prefers metabundles when available
- **Pre-signed URLs** - Downloads bypass WebSocket for large files
- **Connection management** - Automatic disposal when owned

## License

MIT
