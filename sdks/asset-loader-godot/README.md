# Bannou Asset Loader Godot

Godot 4.x engine extension for the Bannou Asset Loader SDK.

## Overview

This package provides `IAssetTypeLoader<T>` implementations for Godot asset types:

- **Texture2D** - PNG, JPEG images
- **Mesh** - glTF/glb 3D models
- **AudioStream** - WAV, OGG, MP3 audio

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetLoader.Godot
```

**Note**: Requires .NET 8.0 and Godot 4.3+.

## Usage

### Register All Godot Loaders

```csharp
using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Godot;

// In your Godot game initialization
var source = new BannouWebSocketAssetSource(client);
var cache = new FileAssetCache("user://asset-cache");
var loader = new AssetLoader(source, cache);

// Register all Godot type loaders
loader.UseGodot();

// Load assets
await loader.EnsureAssetsAvailableAsync(new[]
{
    "my-bundle/hero-texture",
    "my-bundle/hero-model"
});

// Get typed Godot assets
var textureResult = await loader.LoadAssetAsync<Texture2D>("my-bundle/hero-texture");
var meshResult = await loader.LoadAssetAsync<Mesh>("my-bundle/hero-model");

if (meshResult.Success)
{
    var meshInstance = new MeshInstance3D { Mesh = meshResult.Value };
    AddChild(meshInstance);
}
```

### Selective Registration

```csharp
// Textures only
loader.UseGodotTextures();

// Meshes only
loader.UseGodotMeshes();

// Audio only
loader.UseGodotAudio();
```

## Supported Content Types

| Asset Type | Content Types |
|------------|---------------|
| Texture2D | `image/png`, `image/jpeg`, `image/jpg` |
| Mesh | `model/gltf-binary`, `model/gltf+json` |
| AudioStream | `audio/wav`, `audio/ogg`, `audio/mpeg`, `audio/mp3` |

## Architecture

The Godot loaders implement loading directly using Godot's native APIs:

- `Image.LoadPngFromBuffer()` / `Image.LoadJpgFromBuffer()` for textures
- `GltfDocument.AppendFromBuffer()` for meshes
- `AudioStreamWav.Data`, `AudioStreamOggVorbis.Data`, `AudioStreamMp3.Data` for audio

## Notes

- Meshes are loaded via GLTFDocument which generates a scene tree; we extract the first mesh
- WAV files require header parsing to configure AudioStreamWav correctly
- OGG and MP3 streams accept raw data directly
- Resources should be freed when no longer needed (Godot uses reference counting)

## License

MIT
