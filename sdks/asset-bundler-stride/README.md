# Bannou Asset Bundler - Stride Extension

Stride engine asset compilation extension for Bannou Asset Bundler SDK.

## Overview

This package provides Stride-specific asset processing for the Bannou Asset Bundler:

- **StrideBatchCompiler**: Compiles FBX, textures, and other assets through Stride's pipeline
- **StrideTypeInferencer**: Identifies asset types and texture usage (normal maps, emissive, etc.)
- **Stride Content Types**: MIME types for compiled Stride assets

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetBundler.Stride
```

## Quick Start

```csharp
using BeyondImmersion.Bannou.AssetBundler.Pipeline;
using BeyondImmersion.Bannou.AssetBundler.Sources;
using BeyondImmersion.Bannou.AssetBundler.State;
using BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;

// Create source
var source = new DirectoryAssetSource(
    new DirectoryInfo("/path/to/assets"),
    sourceId: "my-assets",
    name: "My Asset Pack");

// Setup Stride compiler
var compilerOptions = new StrideCompilerOptions
{
    StrideVersion = "4.2.0.2188",
    Configuration = "Release",
    MaxTextureSize = 4096,
    TextureCompression = StrideTextureCompression.BC7
};

var compiler = new StrideBatchCompiler(compilerOptions);

// Configure pipeline
var pipelineOptions = new BundlerOptions
{
    WorkingDirectory = "/tmp/bundler-work",
    OutputDirectory = "/output/bundles",
    CreatedBy = "my-tool"
};

// Execute
var state = new BundlerStateManager(new DirectoryInfo("/path/to/state"));
var pipeline = new BundlerPipeline();
var result = await pipeline.ExecuteAsync(source, compiler, state, uploader: null, pipelineOptions);

Console.WriteLine($"Created bundle with {result.AssetCount} compiled assets");
```

## Compiler Options

| Option | Default | Description |
|--------|---------|-------------|
| `StrideVersion` | Latest | Stride package version to use |
| `DotnetPath` | `dotnet` | Path to dotnet executable |
| `Configuration` | `Release` | Build configuration |
| `Platform` | `Windows` | Target platform |
| `GraphicsBackend` | `Direct3D11` | Graphics backend |
| `BuildTimeoutMs` | `300000` | Build timeout (5 minutes) |
| `VerboseOutput` | `false` | Enable detailed build logging |
| `MaxTextureSize` | `4096` | Maximum texture dimension |
| `GenerateMipmaps` | `true` | Generate texture mipmaps |
| `TextureCompression` | `BC7` | Texture compression format |

## Texture Compression Formats

| Format | Best For | Compression Ratio |
|--------|----------|-------------------|
| `None` | Highest quality | 1:1 |
| `BC1` | Opaque textures | 6:1 |
| `BC3` | Textures with alpha | 4:1 |
| `BC7` | High quality (recommended) | 3:1 |
| `ETC2` | Mobile (Android/iOS) | 4:1 |
| `ASTC` | Modern mobile | Variable |

## Type Inference

The `StrideTypeInferencer` automatically categorizes assets:

### Asset Types

| Extension | Type |
|-----------|------|
| `.fbx`, `.obj`, `.glb` | Model |
| `.png`, `.jpg`, `.tga` | Texture |
| `.wav`, `.ogg`, `.mp3` | Audio |
| `.fbx` (with "anim") | Animation |

### Texture Types

| Pattern | Type | Stride Usage |
|---------|------|--------------|
| `_normal`, `_nml` | NormalMap | Normal mapping |
| `_emissive`, `_emit` | Emissive | Glow/emission |
| `_mask`, `_orm` | Mask | PBR channels |
| `_height`, `_h.` | HeightMap | Displacement |
| `spr_`, `ui_` | UI | Sprite rendering |
| Default | Color | Diffuse/albedo |

## How It Works

1. **Project Generation**: Creates a temporary Stride project with asset references
2. **Build Execution**: Runs `dotnet build` to compile assets through Stride's pipeline
3. **Index Parsing**: Reads Stride's output index to locate compiled assets
4. **Data Collection**: Extracts compiled asset data and dependencies (buffers, etc.)

## Requirements

- .NET 8.0 or later
- Stride NuGet packages (downloaded automatically during build)
- `dotnet` CLI available in PATH

## Content Types

Compiled Stride assets use these MIME types:

| Type | Content Type |
|------|--------------|
| Model | `application/x-stride-model` |
| Texture | `application/x-stride-texture` |
| Animation | `application/x-stride-animation` |
| Material | `application/x-stride-material` |
| Binary | `application/x-stride-binary` |

## Related Packages

- `BeyondImmersion.Bannou.AssetBundler` - Base bundler SDK
- `BeyondImmersion.Bannou.Client` - WebSocket client for upload
