# Bannou Asset Bundler SDK

Engine-agnostic SDK for creating `.bannou` asset bundles.

## Overview

This SDK provides a complete pipeline for:
- Discovering and extracting assets from various sources (directories, ZIPs, vendor packs)
- Processing assets through engine-specific compilation or pass-through
- Packaging assets into `.bannou` bundle format with LZ4 compression
- Uploading bundles to Bannou Asset Service
- Requesting server-side metabundle creation

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetBundler
```

## Quick Start

### Simple Directory Bundling

```csharp
using BeyondImmersion.Bannou.AssetBundler.Pipeline;
using BeyondImmersion.Bannou.AssetBundler.Sources;
using BeyondImmersion.Bannou.AssetBundler.State;

// Create source from directory
var source = new DirectoryAssetSource(
    new DirectoryInfo("/path/to/assets"),
    sourceId: "my-assets",
    name: "My Asset Pack");

// Setup state manager for incremental builds
var stateDir = new DirectoryInfo("/path/to/state");
var state = new BundlerStateManager(stateDir);

// Configure pipeline
var options = new BundlerOptions
{
    WorkingDirectory = "/tmp/bundler-work",
    OutputDirectory = "/output/bundles",
    CreatedBy = "my-tool"
};

// Execute pipeline
var pipeline = new BundlerPipeline();
var result = await pipeline.ExecuteAsync(source, processor: null, state, uploader: null, options);

Console.WriteLine($"Bundle created: {result.BundlePath} with {result.AssetCount} assets");
```

### ZIP Archive Source

```csharp
var source = new ZipArchiveAssetSource(
    new FileInfo("/path/to/assets.zip"),
    sourceId: "synty/polygon-adventure",
    name: "POLYGON Adventure Pack",
    version: "v4");

var result = await pipeline.ExecuteAsync(source, processor: null, state, uploader: null, options);
```

### With Upload to Bannou

```csharp
using BeyondImmersion.Bannou.AssetBundler.Upload;

var uploaderOptions = new UploaderOptions
{
    ServerUrl = "wss://bannou.example.com",
    ServiceToken = Environment.GetEnvironmentVariable("BANNOU_TOKEN"),
    Owner = "account-123"
};

var uploader = new BannouUploader(uploaderOptions);
await uploader.ConnectAsync();

var result = await pipeline.ExecuteAsync(source, processor: null, state, uploader, options);
Console.WriteLine($"Uploaded as: {result.UploadedBundleId}");
```

### Creating Metabundles

```csharp
using BeyondImmersion.Bannou.AssetBundler.Metabundles;

var request = new MetabundleRequestBuilder()
    .WithId("game-assets-v1")
    .AddSourceBundles(["synty/polygon-adventure", "synty/polygon-fantasy"])
    .WithVersion("1.0.0")
    .WithOwner("account-123")
    .Build();

var client = new MetabundleClient(bannouClient);
var result = await client.CreateAsync(request);
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Your Bundling Tool                            │
│  (SyntyBundler, custom tools, CI/CD pipelines)                  │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│           BeyondImmersion.Bannou.AssetBundler                   │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │  Abstractions: IAssetSource, IAssetProcessor, etc.          │ │
│ │  Sources: DirectoryAssetSource, ZipArchiveAssetSource       │ │
│ │  Processing: RawAssetProcessor (pass-through)               │ │
│ │  Pipeline: BundlerPipeline (orchestration)                  │ │
│ │  State: BundlerStateManager (incremental builds)            │ │
│ │  Upload: BannouUploader (service integration)               │ │
│ │  Metabundles: MetabundleClient                              │ │
│ └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                               │
           ┌───────────────────┴───────────────────┐
           ▼                                       ▼
┌─────────────────────────┐           ┌─────────────────────────┐
│  Engine-Specific SDKs   │           │   Bannou Client SDK     │
│  (Stride, Godot, etc.)  │           │  (Upload, WebSocket)    │
└─────────────────────────┘           └─────────────────────────┘
```

## Components

### Abstractions

| Interface | Purpose |
|-----------|---------|
| `IAssetSource` | Represents a source of raw assets (directory, ZIP, etc.) |
| `IAssetSourceProvider` | Discovers multiple sources from a root location |
| `IAssetProcessor` | Processes/compiles assets (engine-specific or pass-through) |
| `IAssetTypeInferencer` | Infers asset types from filenames |
| `IProcessedAsset` | Output from processing a single asset |

### Sources

| Class | Purpose |
|-------|---------|
| `DirectoryAssetSource` | Reads assets from a directory |
| `ZipArchiveAssetSource` | Reads assets from a ZIP archive |
| `DefaultTypeInferencer` | Basic file extension type inference |

### Processing

| Class | Purpose |
|-------|---------|
| `RawAssetProcessor` | Pass-through processor (no transformation) |
| `ProcessedAsset` | Default implementation of IProcessedAsset |

### Pipeline

| Class | Purpose |
|-------|---------|
| `BundlerPipeline` | Orchestrates Source → Extract → Process → Bundle → Upload |
| `BundlerOptions` | Pipeline configuration |
| `BundleResult` | Result of bundling a single source |

### State

| Class | Purpose |
|-------|---------|
| `BundlerStateManager` | Tracks processed sources for incremental builds |
| `SourceProcessingRecord` | Record of a processed source |

### Upload

| Class | Purpose |
|-------|---------|
| `BannouUploader` | Uploads bundles to Bannou Asset Service |
| `UploaderOptions` | Upload configuration |
| `UploadResult` | Result of an upload operation |

### Metabundles

| Class | Purpose |
|-------|---------|
| `MetabundleClient` | Requests server-side metabundle creation |
| `MetabundleRequestBuilder` | Fluent builder for metabundle requests |

## Custom Processors

Implement `IAssetProcessor` for engine-specific compilation:

```csharp
public class MyEngineProcessor : IAssetProcessor
{
    public string ProcessorId => "my-engine";
    public IReadOnlyList<string> OutputContentTypes => ["application/x-myengine-model"];
    public IAssetTypeInferencer? TypeInferencer => null;

    public async Task<IReadOnlyDictionary<string, IProcessedAsset>> ProcessAsync(
        IReadOnlyList<ExtractedAsset> assets,
        DirectoryInfo workingDir,
        ProcessorOptions? options,
        CancellationToken ct)
    {
        // Compile assets through your engine's pipeline
        // Return dictionary of processed assets
    }
}
```

## Custom Source Providers

Implement `IAssetSourceProvider` for vendor-specific discovery:

```csharp
public class SyntySourceProvider : IAssetSourceProvider
{
    public string ProviderId => "synty";
    public IAssetTypeInferencer TypeInferencer { get; } = new SyntyTypeInferencer();

    public async IAsyncEnumerable<IAssetSource> DiscoverSourcesAsync(
        DirectoryInfo root,
        DiscoveryOptions? options,
        CancellationToken ct)
    {
        // Scan for Synty pack directories/ZIPs
        // Yield ZipArchiveAssetSource for each discovered pack
    }
}
```

## Related Packages

- `BeyondImmersion.Bannou.AssetBundler.Stride` - Stride engine compilation
- `BeyondImmersion.Bannou.Client` - WebSocket client for Bannou services

## Dependencies

- `Microsoft.Extensions.Logging.Abstractions` - Logging
- `K4os.Compression.LZ4` - Bundle compression
- `BeyondImmersion.Bannou.Client` - Service communication
