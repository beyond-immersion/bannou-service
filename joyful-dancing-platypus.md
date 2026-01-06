# Phase 5: Asset Pipeline

## Overview

Build the asset pipeline for Monster Arena Demo:
1. **AssetTool** (CLI) - Process raw assets (FBX, textures, ABML) → bundle into `.bannou` files
2. **Runtime Loader** (Stride client) - Download and load assets from bundles at runtime

**Key Decisions:**
- Build patterns in MonsterArenaDemo first, extract to Bannou.Stride.SDK later
- Use Stride.Core.Assets.CompilerApp for programmatic asset compilation
- Synty assets available at `\\AssetsNAS\Public\SyntyStore`

---

## Architecture

```
BUILD TIME                                    RUNTIME
─────────────────────────────────────────    ─────────────────────────────────────
Synty FBX/PNG     AssetTool                  BundleDownloader    BannouContentManager
     │                │                           │                    │
     ▼                ▼                           ▼                    ▼
┌─────────┐    ┌─────────────┐              ┌──────────┐        ┌────────────┐
│ process │───►│ .sdmodel    │              │ Download │───────►│ Load from  │
│ command │    │ .sdtex      │              │ bundle   │        │ bundle     │
└─────────┘    │ .sdclip     │              └──────────┘        └────────────┘
               │ .bmodel     │                    │                    │
               └──────┬──────┘                    ▼                    ▼
                      │                      ┌──────────┐        ┌────────────┐
                      ▼                      │ .bannou  │        │ Stride     │
               ┌─────────────┐               │ file     │        │ entities   │
               │ bundle      │──────────────►│ (cached) │        │ + behaviors│
               │ command     │               └──────────┘        └────────────┘
               └─────────────┘
```

---

## Part 1: AssetTool (`MonsterArenaDemo.AssetTool/`)

### Files to Create

| File | Purpose |
|------|---------|
| `Commands/ProcessCommand.cs` | `process` - Convert raw assets to Stride formats |
| `Commands/BundleCommand.cs` | `bundle` - Create .bannou bundles |
| `Commands/UploadCommand.cs` | `upload` - Upload to Asset Service |
| `Commands/ListCommand.cs` | `list` - Inspect bundle contents |
| `Processing/IAssetProcessor.cs` | Processor interface |
| `Processing/AssetProcessorContext.cs` | Processing config |
| `Processing/StrideBuildOrchestrator.cs` | Orchestrate Stride asset compilation |
| `Processing/ModelProcessor.cs` | FBX → .sdmodel |
| `Processing/TextureProcessor.cs` | PNG/TGA → .sdtex (BC7) |
| `Processing/AnimationProcessor.cs` | FBX animations → .sdclip |
| `Processing/AbmlProcessor.cs` | YAML → .bmodel bytecode |
| `Bundling/GameBundleType.cs` | Core, Character, Environment, UI |
| `Bundling/GameBundleManifest.cs` | Extended manifest with game metadata |
| `Bundling/CharacterBundleBuilder.cs` | Build character bundles |
| `Bundling/EnvironmentBundleBuilder.cs` | Build environment bundles |
| `Upload/AssetServiceUploader.cs` | Upload via BannouClient |

### Key Types

```csharp
public interface IAssetProcessor
{
    string Name { get; }
    IReadOnlyList<string> SupportedExtensions { get; }
    Task<ProcessedAsset> ProcessAsync(FileInfo source, AssetProcessorContext ctx, CancellationToken ct);
}

public enum GameBundleType { Core, Character, Environment, UI }

public sealed class GameBundleManifest
{
    public required GameBundleType BundleType { get; init; }
    public IReadOnlyList<BundleDependency>? Dependencies { get; init; }
    public CharacterBundleInfo? CharacterInfo { get; init; }  // For character bundles
}

public sealed class CharacterBundleInfo
{
    public required string CharacterId { get; init; }
    public required string ModelAssetId { get; init; }
    public required string BehaviorAssetId { get; init; }
    public IReadOnlyList<string> AnimationAssetIds { get; init; }
}
```

### StrideBuildOrchestrator Strategy

```csharp
// Create minimal Stride project in temp directory:
// 1. Create .sdpkg pointing to source assets
// 2. Create minimal GameSettings.sdgamesettings
// 3. Run `dotnet build` which triggers Stride.Core.Assets.CompilerApp
// 4. Collect compiled .sd* files from output
```

### AbmlProcessor (uses existing Bannou infrastructure)

```csharp
var compiler = new BehaviorCompiler();
var result = compiler.CompileYaml(yaml, new CompilationOptions { ModelId = Guid.NewGuid() });
// Returns bytecode as .bmodel
```

---

## Part 2: Runtime Loader (`MonsterArenaDemo.Game/Assets/`)

### Files to Create

| File | Purpose |
|------|---------|
| `BannouContentManager.cs` | Main content loading interface |
| `BundleDownloader.cs` | Download bundles from Asset Service or local |
| `BundleRegistry.cs` | Track loaded bundles |
| `AssetCache.cs` | LRU cache with reference counting |
| `Loaders/IAssetLoader.cs` | Loader interface |
| `Loaders/ModelLoader.cs` | .sdmodel → Stride Model |
| `Loaders/TextureLoader.cs` | .sdtex → Stride Texture |
| `Loaders/AnimationLoader.cs` | .sdclip → AnimationClip |
| `Loaders/BehaviorLoader.cs` | .bmodel → BehaviorModel |

### BannouContentManager API

```csharp
public sealed class BannouContentManager : IDisposable
{
    public BannouContentManager(IServiceRegistry services, GraphicsDevice device,
        BannouClient? client = null, string? localBundlePath = null);

    // Load bundle (downloads if needed)
    public Task<LoadedBundle> LoadBundleAsync(string bundleId, string version, ...);

    // Load assets from bundle
    public Task<Model> LoadModelAsync(string bundleId, string assetId, ...);
    public Task<Texture> LoadTextureAsync(string bundleId, string assetId, ...);
    public Task<BehaviorModel> LoadBehaviorAsync(string bundleId, string assetId, ...);
}
```

### BundleDownloader

```csharp
public async Task<string> EnsureBundleAsync(string bundleId, string version, ...)
{
    // 1. Check local dev path (_config.LocalBundlePath)
    // 2. Check cache (~/.local/share/MonsterArenaDemo/BundleCache/)
    // 3. Download from Asset Service via pre-signed URL
}
```

---

## Implementation Order

### Phase 5.1: AssetTool Foundation
1. Add NuGet references to AssetTool.csproj:
   - `BeyondImmersion.Bannou.SDK` (for BannouBundleWriter)
   - Reference lib-behavior for BehaviorCompiler
2. Implement command infrastructure (ProcessCommand, BundleCommand, ListCommand)
3. Implement AbmlProcessor (simplest, validates infrastructure)
4. Test with sample ABML behavior file

### Phase 5.2: Stride Asset Processing
1. **Test first**: Can Stride.Core.Assets.CompilerApp run on WSL2?
   - If yes: proceed with StrideBuildOrchestrator
   - If no: design Windows-only processing, WSL2 bundling
2. Implement StrideBuildOrchestrator
3. Implement ModelProcessor, TextureProcessor, AnimationProcessor

### Phase 5.3: Bundle Creation
1. Implement GameBundleManifest, CharacterBundleInfo
2. Implement CharacterBundleBuilder, EnvironmentBundleBuilder
3. Process sample Synty assets:
   - Troll from Fantasy Rivals → `character-troll-1.0.0.bannou`
   - Elemental Golem → `character-golem-1.0.0.bannou`
   - Basic arena → `arena-basic-1.0.0.bannou`

### Phase 5.4: Runtime Loader
1. Implement BannouContentManager, BundleDownloader, BundleRegistry, AssetCache
2. Implement BehaviorLoader (simplest - uses BehaviorModel.Deserialize)
3. Implement TextureLoader, ModelLoader, AnimationLoader
4. Integration test: Load character bundle → create entity

### Phase 5.5: Upload and Polish
1. Implement AssetServiceUploader
2. End-to-end test: raw → process → bundle → upload → download → load

---

## Critical Concerns

### 1. Stride Compilation on WSL2
**Risk:** `Stride.Core.Assets.CompilerApp` may require Windows SDK.

**Test early.** If it fails:
- Option A: Hybrid - process on Windows, bundle on WSL2
- Option B: Windows-only AssetTool

### 2. Stride Model Deserialization
**Risk:** Compiled `.sdmodel` uses internal serialization.

**Options:**
- Create custom `DatabaseFileProvider` wrapping `BannouBundleReader`
- Write to temp file, use standard `Content.Load` (proof of concept)
- Store FBX, use Assimp at runtime (slower fallback)

**Investigate:** Stride's `DatabaseFileProvider` and `ObjectDatabase` for proper integration.

### 3. Bundle Paths
**Mitigation:** Normalize all paths to forward slashes in manifests.

---

## Existing Bannou SDK Integration

| Component | Path | Usage |
|-----------|------|-------|
| BannouBundleWriter | `lib-asset/Bundles/BannouBundleWriter.cs` | Create .bannou |
| BannouBundleReader | `lib-asset/Bundles/BannouBundleReader.cs` | Read .bannou |
| BundleManifest | `lib-asset/Bundles/BundleManifest.cs` | Base manifest |
| BehaviorCompiler | `lib-behavior/Compiler/BehaviorCompiler.cs` | ABML → bytecode |
| BehaviorModel | `Bannou.Client.SDK/.../BehaviorModel.cs` | Runtime behavior |
| BannouClient | `Bannou.Client.SDK/BannouClient.cs` | Asset Service API |

---

## Test Assets (from Synty)

**Characters:**
- `\\AssetsNAS\Public\SyntyStore\POLYGON_Fantasy_Rivals\` → Troll, Elemental Golem
- Include: Model FBX, textures, animations

**Environment:**
- `\\AssetsNAS\Public\SyntyStore\POLYGON_Farm_Pack\` or similar → Arena props

**Behaviors:**
- Create `behaviors/monster-combat-basic.yml` (ABML)

---

## Dependencies to Add

**MonsterArenaDemo.AssetTool.csproj:**
```xml
<PackageReference Include="BeyondImmersion.Bannou.SDK" Version="..." />
<PackageReference Include="K4os.Compression.LZ4" Version="1.3.6" />
```

**MonsterArenaDemo.Game.csproj:**
```xml
<!-- Already has Stride references -->
<!-- BannouBundleReader comes via Client SDK -->
```
