# Bannou Asset Bundler Godot

Godot engine asset processing extension for the Bannou Asset Bundler SDK.

## Overview

Unlike Stride (which requires offline compilation to proprietary formats), Godot can load many standard formats at runtime via its buffer-based APIs. This SDK provides:

- **GodotTypeInferencer** - Asset type inference with Godot-specific filtering
- **GodotAssetProcessor** - Pass-through processor with optional format conversion
- **GodotContentTypes** - Standard MIME types Godot supports at runtime

## Supported Formats

| Asset Type | Formats | Godot API |
|------------|---------|-----------|
| Texture | PNG, JPG, WebP | `Image.LoadPngFromBuffer()` |
| Mesh | glTF, glb | `GltfDocument.AppendFromBuffer()` |
| Audio | WAV, OGG, MP3 | `AudioStreamWav/Ogg/MP3` |

## Format Conversion

Some source formats need conversion for Godot runtime loading:

| Source Format | Target Format | Notes |
|---------------|---------------|-------|
| FBX | glTF/glb | Requires external converter |
| TGA, DDS | PNG | Lossless conversion |
| FLAC | OGG/WAV | Audio conversion |

## Usage

```csharp
using BeyondImmersion.Bannou.AssetBundler;
using BeyondImmersion.Bannou.AssetBundler.Godot;

// Create Godot asset processor
var processor = new GodotAssetProcessor(new GodotProcessorOptions
{
    ConvertFbxToGltf = true,  // Enable FBX conversion
    MaxTextureSize = 4096
});

// Use in pipeline
var pipeline = new BundlerPipeline(
    source: new DirectoryAssetSource(assetDir),
    processor: processor,
    typeInferencer: new GodotTypeInferencer()
);

await pipeline.ProcessAsync(outputDir);
```

## Comparison with Stride

| Feature | Stride | Godot |
|---------|--------|-------|
| Compilation | Required (dotnet build) | Not required |
| Format | Proprietary (.sd*) | Standard (PNG, glTF, etc.) |
| Dependencies | Stride NuGet packages | None |
| Processing | Complex pipeline | Mostly pass-through |
| Format conversion | Built-in | Optional external tools |

## Architecture Notes

Godot's scene tree architecture differs from Stride's ECS. The asset bundler focuses on formats that work well with Godot's runtime loading:

- **Textures**: Godot's Image class can load PNG/JPG from buffers
- **Meshes**: GltfDocument handles glTF/glb parsing
- **Audio**: Native support for WAV, OGG, and MP3 streams

For scene composition, use `scene-composer-godot` with `asset-loader-godot` to load bundled assets at runtime.

## License

MIT
