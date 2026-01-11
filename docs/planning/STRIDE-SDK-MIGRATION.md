# Stride SceneComposer SDK Migration Plan

**Status:** Complete
**Created:** 2026-01-11
**Completed:** 2026-01-11
**Source:** stride-demo/project/MonsterArenaDemo.Game

## Overview

This document tracks the migration of reusable components from the stride-demo project into the `Bannou.Stride.SceneComposer` SDK. The goal is to make the SDK fully functional out-of-the-box, so Stride developers can integrate scene editing with minimal code.

## Migration Tiers

### Tier 1: Core Editor Integration (MUST MIGRATE)

| # | File | Lines | Target Location | Status |
|---|------|-------|-----------------|--------|
| 1.1 | `Editor/SceneEditorScript.cs` | 567 | `Examples/StrideSceneEditorScript.cs` | Complete (moved to Examples/) |
| 1.2 | `Editor/BannouAssetLoader.cs` | 127 | `Content/StrideBannouAssetLoader.cs` | Complete |
| 1.3 | `Assets/TypeLoaders/IStrideAssetLoader.cs` | 50 | `Loaders/IStrideAssetLoader.cs` | Complete |

### Tier 2: Asset Loading Infrastructure (SHOULD MIGRATE)

| # | File | Lines | Target Location | Status |
|---|------|-------|-----------------|--------|
| 2.1 | `Assets/BannouContentManager.cs` | 728 | `Content/StrideContentManager.cs` | Complete |
| 2.2 | `Assets/TypeLoaders/ModelLoader.cs` | 409 | `Loaders/ModelLoader.cs` | Complete |
| 2.3 | `Assets/TypeLoaders/TextureLoader.cs` | 281 | `Loaders/TextureLoader.cs` | Complete |
| 2.4 | `Assets/BundleAssetLoader.cs` | 193 | `Content/BundleAssetLoader.cs` | Complete |
| 2.5 | `Assets/AssetCache.cs` | 194 | `Caching/AssetCache.cs` | Complete |

### Tier 3: Editor UI (OPTIONAL - Future)

| # | File | Lines | Target Location | Status |
|---|------|-------|-----------------|--------|
| 3.1 | `Editor/EditorUIScript.cs` | 209 | `Editor/UI/EditorUIScript.cs` | Future |
| 3.2 | `Editor/UI/SceneHierarchyPanel.cs` | 257 | `Editor/UI/SceneHierarchyPanel.cs` | Future |
| 3.3 | `Editor/UI/AssetBrowserPanel.cs` | 349 | `Editor/UI/AssetBrowserPanel.cs` | Future |

## Target SDK Structure

```
Bannou.Stride.SceneComposer/
├── Bridge/
│   └── StrideSceneComposerBridge.cs       [EXISTS]
├── Gizmo/
│   └── StrideGizmoRenderer.cs             [EXISTS]
├── Loaders/
│   ├── IAssetLoader.cs                    [EXISTS]
│   ├── IStrideAssetLoader.cs              [MIGRATED]
│   ├── ModelLoader.cs                     [MIGRATED]
│   └── TextureLoader.cs                   [MIGRATED]
├── Content/
│   ├── StrideContentManager.cs            [MIGRATED]
│   ├── StrideBannouAssetLoader.cs         [MIGRATED]
│   └── BundleAssetLoader.cs               [MIGRATED]
├── Caching/
│   └── AssetCache.cs                      [MIGRATED]
├── Examples/
│   └── StrideSceneEditorScript.cs         [MIGRATED - Reference implementation]
├── StrideTypeConverter.cs                 [EXISTS]
└── Bannou.Stride.SceneComposer.csproj     [EXISTS]

Bannou.Stride.SceneComposer.Tests/
├── AssetCacheTests.cs                     [NEW - 21 tests]
└── Bannou.Stride.SceneComposer.Tests.csproj
```

## Migration Steps

### Phase 1: Foundation (Tier 2 Infrastructure)

These must be migrated first as they are dependencies for Tier 1.

1. **[2.5] AssetCache.cs** - No dependencies, pure utility
   - Copy to `Caching/AssetCache.cs`
   - Update namespace to `BeyondImmersion.Bannou.Stride.SceneComposer.Caching`
   - No code changes needed (generic, no game-specific logic)

2. **[1.3] IStrideAssetLoader.cs** - Interface definition
   - Copy to `Loaders/IStrideAssetLoader.cs`
   - Update namespace to `BeyondImmersion.Bannou.Stride.SceneComposer.Loaders`
   - No code changes needed

3. **[2.2] ModelLoader.cs** - Model deserialization
   - Copy to `Loaders/ModelLoader.cs`
   - Update namespace
   - Remove `BundleTestSystem.Log` calls (replace with optional ILogger)
   - Add XML doc warnings about reflection fragility

4. **[2.3] TextureLoader.cs** - Texture loading
   - Copy to `Loaders/TextureLoader.cs`
   - Update namespace
   - No significant changes needed

5. **[2.4] BundleAssetLoader.cs** - Bundle wrapper
   - Copy to `Content/BundleAssetLoader.cs`
   - Update namespace
   - Uses `BeyondImmersion.BannouService.Asset.Bundles` (already a dependency)

6. **[2.1] StrideContentManager.cs** - Central content hub
   - Copy to `Content/StrideContentManager.cs`
   - Update namespace
   - Rename class from `BannouContentManager` to `StrideContentManager`
   - Remove `BannouToStrideConverter` references (native mode incomplete)
   - Update imports for relocated types

### Phase 2: Integration (Tier 1)

7. **[1.2] StrideBannouAssetLoader.cs** - IAssetLoader implementation
   - Copy to `Content/StrideBannouAssetLoader.cs`
   - Update namespace
   - Rename class from `BannouAssetLoader` to `StrideBannouAssetLoader`
   - Update to use `StrideContentManager`

8. **[1.1] StrideSceneEditorScript.cs** - Editor controller
   - Copy to `Editor/StrideSceneEditorScript.cs`
   - Update namespace
   - Rename class from `SceneEditorScript` to `StrideSceneEditorScript`
   - Update imports for relocated types
   - Make configurable (virtual methods for customization)

### Phase 3: Cleanup & Testing

9. Update `Bannou.Stride.SceneComposer.csproj`
   - Ensure all new files are included
   - Add any missing package references

10. Build and verify
    - `dotnet build Bannou.Stride.SceneComposer`
    - Fix any compilation errors

11. Update README.md with usage examples

## Dependencies

The SDK requires these NuGet packages (already in csproj):
- Stride.Engine 4.3.x
- Stride.Rendering 4.3.x
- Stride.Physics 4.3.x
- Stride.UI 4.3.x

The SDK requires these project references (already in csproj):
- Bannou.SceneComposer (engine-agnostic SDK)

The following types from bannou-service are used:
- `BeyondImmersion.BannouService.Asset.Bundles.BannouBundleReader`
- `BeyondImmersion.BannouService.Asset.Bundles.BundleManifest`
- `BeyondImmersion.BannouService.Asset.Bundles.BundleAssetEntry`

**Note:** Need to verify if `BannouService.Asset.Bundles` namespace is available as a NuGet package or if we need to add a project reference.

## Known Issues / Fragility

### Reflection-Based Loading

`ModelLoader.cs` and `TextureLoader.cs` use reflection to access Stride internal APIs:

```csharp
// These are internal to Stride and may change between versions:
- ContentSerializerContext constructor (internal)
- ContentSerializerContext.SerializeReferences (internal)
- ContentSerializerContext.SerializeContent (internal)
- ContentSerializer.GetSerializer (internal)
- ContentManager.RegisterDeserializedObject (internal)
```

**Mitigation:**
1. Document the fragility clearly
2. Add Stride version checks at runtime
3. Wrap in try-catch with fallback to native loading
4. Consider contributing upstream to Stride for public APIs

### Native Loading Mode

The stride-demo has incomplete native loading support:
- `BannouToStrideConverter` is referenced but stub implementation
- `BundleCacheManager` exists but native path needs work

**Decision:** Disable native loading mode for initial migration. Focus on reflection mode which works.

## Usage After Migration

```csharp
using BeyondImmersion.Bannou.SceneComposer;
using BeyondImmersion.Bannou.Stride.SceneComposer;
using BeyondImmersion.Bannou.Stride.SceneComposer.Content;
using BeyondImmersion.Bannou.Stride.SceneComposer.Editor;

// In your game's Start():
var contentManager = new StrideContentManager(Services, GraphicsDevice);
var assetLoader = new StrideBannouAssetLoader(contentManager);
var gizmoRenderer = new StrideGizmoRenderer(GraphicsDevice);
var bridge = new StrideSceneComposerBridge(
    Entity.Scene,
    EditorCamera,
    GraphicsDevice,
    assetLoader,
    gizmoRenderer);
var composer = new SceneComposer(bridge);

// Or use the all-in-one editor script:
var editorEntity = new Entity("SceneEditor");
editorEntity.Add(new StrideSceneEditorScript
{
    EditorCamera = camera,
    InitialBundlePath = "path/to/bundle.bannou"
});
Scene.Entities.Add(editorEntity);
```

## Completion Checklist

- [x] Phase 1: Foundation
  - [x] 2.5 AssetCache.cs migrated
  - [x] 1.3 IStrideAssetLoader.cs migrated
  - [x] 2.2 ModelLoader.cs migrated
  - [x] 2.3 TextureLoader.cs migrated
  - [x] 2.4 BundleAssetLoader.cs migrated
  - [x] 2.1 StrideContentManager.cs migrated
- [x] Phase 2: Integration
  - [x] 1.2 StrideBannouAssetLoader.cs migrated
  - [x] 1.1 StrideSceneEditorScript.cs migrated (moved to Examples/)
- [x] Phase 3: Cleanup
  - [x] csproj updated (SDK-style auto-includes files)
  - [x] Build succeeds
  - [x] T23 async/await compliance fixed
  - [x] Unit test project created (21 tests for AssetCache)
  - [ ] README updated (optional - future enhancement)

## Notes

- All migrated code retains MIT license
- Original code is from stride-demo which is internal/proprietary
- Migration maintains backwards compatibility with existing SDK users
