# Bannou Asset Loader Stride

Stride engine extension for the Bannou Asset Loader SDK.

## Overview

This package provides `IAssetTypeLoader<T>` implementations for Stride asset types:

- **Model** - 3D models (.sdmodel)
- **Texture** - Textures (.sdtex, DDS, PNG, JPEG)
- **AnimationClip** - Animations (.sdanim)

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetLoader.Stride
```

**Note**: Requires .NET 10 and Stride 4.3. Full functionality requires Windows.

## Usage

### Register All Stride Loaders

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Stride;
using BeyondImmersion.Bannou.AssetLoader.Cache;

// In your Stride game initialization
var source = new BannouWebSocketAssetSource(client);
var cache = new FileAssetCache("./asset-cache");
var loader = new AssetLoader(source, cache);

// Register all Stride type loaders
loader.UseStride(Services, GraphicsDevice);

// Load assets
await loader.EnsureAssetsAvailableAsync(new[]
{
    "polygon-adventure/hero-model",
    "polygon-adventure/hero-texture"
});

// Get typed Stride assets
var modelResult = await loader.LoadAssetAsync<Model>("polygon-adventure/hero-model");
var textureResult = await loader.LoadAssetAsync<Texture>("polygon-adventure/hero-texture");

if (modelResult.Success)
{
    // Use the model in your scene
    var entity = new Entity { new ModelComponent(modelResult.Value) };
    rootScene.Entities.Add(entity);
}
```

### Selective Registration

If you only need specific asset types:

```csharp
// Models only (no GraphicsDevice required)
loader.UseStrideModels(Services);

// Textures only
loader.UseStrideTextures(Services, GraphicsDevice);

// Animations only
loader.UseStrideAnimations(Services);
```

### With Dependency Injection

```csharp
services.AddSingleton<AssetLoader>(sp =>
{
    var source = sp.GetRequiredService<IAssetSource>();
    var cache = sp.GetRequiredService<IAssetCache>();
    var strideServices = sp.GetRequiredService<IServiceRegistry>();
    var graphics = sp.GetRequiredService<GraphicsDevice>();

    var loader = new AssetLoader(source, cache);
    loader.UseStride(strideServices, graphics);
    return loader;
});
```

## Supported Content Types

| Asset Type | Content Types |
|------------|---------------|
| Model | `application/x-stride-model`, `application/x-sdmodel` |
| Texture | `application/x-stride-texture`, `application/x-sdtex`, `image/dds`, `image/png`, `image/jpeg` |
| AnimationClip | `application/x-stride-animation`, `application/x-sdanim` |

## Architecture

This package wraps the existing `scene-composer-stride` loaders to implement the `IAssetTypeLoader<T>` interface:

```
┌─────────────────────────────────────────┐
│          asset-loader-stride            │
│  ┌───────────────────────────────────┐  │
│  │ StrideModelTypeLoader             │  │
│  │ StrideTextureTypeLoader           │  │
│  │ StrideAnimationTypeLoader         │  │
│  └───────────────────────────────────┘  │
│                   │                     │
│                   ▼                     │
│  ┌───────────────────────────────────┐  │
│  │ scene-composer-stride Loaders     │  │
│  │ (ModelLoader, TextureLoader, etc) │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
```

## Notes

- Models and textures use reflection to access internal Stride serialization APIs
- The animation loader is theoretical and may require adjustments for actual .sdanim files
- GPU resources (textures) are created during loading - ensure GraphicsDevice is ready
- Models don't require explicit disposal; textures should be disposed when no longer needed

## License

MIT
