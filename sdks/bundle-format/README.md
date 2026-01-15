# Bundle Format

Shared code for reading and writing `.bannou` bundle files. This is NOT a NuGet package - it's included via `<Compile Include>` in consuming projects.

## Usage

Include in your project file:

```xml
<ItemGroup>
  <Compile Include="../bundle-format/*.cs" LinkBase="Bundle" />
</ItemGroup>
```

## Bundle Structure

```
┌─────────────────────────────────────────────────────────┐
│ Manifest Length (4 bytes, big-endian)                   │
├─────────────────────────────────────────────────────────┤
│ Manifest JSON (UTF-8, variable length)                  │
├─────────────────────────────────────────────────────────┤
│ Index Header: "BNIX" (4 bytes)                          │
├─────────────────────────────────────────────────────────┤
│ Entry Count (4 bytes, big-endian)                       │
├─────────────────────────────────────────────────────────┤
│ Index Entries (48 bytes each)                           │
│   - Offset (8 bytes)                                    │
│   - Compressed Size (8 bytes)                           │
│   - Uncompressed Size (8 bytes)                         │
│   - Content Hash Prefix (24 bytes, SHA256 truncated)    │
├─────────────────────────────────────────────────────────┤
│ Asset Data (LZ4-compressed chunks, concatenated)        │
└─────────────────────────────────────────────────────────┘
```

## Components

| File | Purpose |
|------|---------|
| `BannouBundleWriter.cs` | Write assets to bundle format |
| `BannouBundleReader.cs` | Read and decompress assets |
| `BundleManifest.cs` | Manifest and asset entry types |
| `BundleIndex.cs` | Binary index for random access |
| `BundleValidator.cs` | Validation and integrity checks |
| `BundleJson.cs` | Internal JSON serialization |

## Writing a Bundle

```csharp
using BeyondImmersion.Bannou.Bundle.Format;

await using var output = File.Create("my-bundle.bannou");
using var writer = new BannouBundleWriter(output);

// Add assets
writer.AddAsset(
    assetId: "texture-001",
    filename: "diffuse.png",
    contentType: "image/png",
    data: File.ReadAllBytes("diffuse.png"));

// Finalize bundle
writer.Finalize(
    bundleId: "my-bundle-v1",
    name: "My Asset Bundle",
    version: "1.0.0",
    createdBy: "user-123");
```

## Reading a Bundle

```csharp
using BeyondImmersion.Bannou.Bundle.Format;

await using var input = File.OpenRead("my-bundle.bannou");
using var reader = new BannouBundleReader(input);

// Read header (manifest + index)
reader.ReadHeader();

// Access manifest
Console.WriteLine($"Bundle: {reader.Manifest.Name}");
Console.WriteLine($"Assets: {reader.Manifest.AssetCount}");

// Read specific asset
var data = reader.ReadAsset("texture-001");

// Or iterate all assets
foreach (var (entry, data) in reader.ReadAllAssets())
{
    Console.WriteLine($"  {entry.AssetId}: {entry.ContentType}");
}
```

## Validation

```csharp
using BeyondImmersion.Bannou.Bundle.Format;

await using var input = File.OpenRead("my-bundle.bannou");
var result = await BundleValidator.ValidateAsync(input);

if (!result.IsValid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"[{error.Stage}] {error.Message}");
    }
}
```

## Namespace

All types use namespace: `BeyondImmersion.Bannou.Bundle.Format`

## Dependencies

- `K4os.Compression.LZ4` - LZ4 compression
- `System.Text.Json` - JSON serialization
