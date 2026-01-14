# Runtime Scene Composition System

> **Status**: Planning Document
> **Last Updated**: 2025-01-11
> **Scope**: Engine-agnostic content authoring pipeline

---

## Executive Summary

This document formalizes the design of Bannou's **Runtime Scene Composition System** - a content authoring approach where game engines serve as rendering clients rather than authoring tools. The goal is to create 100% of game content using prefabricated object hierarchies stored in unified `.scene` files, eliminating dependency on engine-specific editors (Stride, Unity, Unreal, Godot).

**Core Philosophy**: Game engines become pure renderers; all composition happens through Bannou services.

---

## 1. Terminology

The game development industry uses several terms for related concepts. For Bannou, we adopt:

### Primary Terms

| Term | Definition | Usage |
|------|------------|-------|
| **Scene Composer** | Runtime editing tool for arranging assets into hierarchies | Our primary tool name |
| **Runtime Editor** | Editor that runs within the game client at runtime | Industry term for in-game editing |
| **Scene Composition** | The act of arranging assets into hierarchical structures | Our primary action verb |
| **Prefab** | Reusable template of arranged objects | Industry-standard term (Unity origin) |
| **Kit** / **Modular Kit** | System of reusable pieces that snap together | Bethesda terminology (Skyrim/Fallout) |

### Supporting Terms

| Term | Definition | Source |
|------|------------|--------|
| **Scene Graph** | Hierarchical data structure for spatial relationships | Graphics programming |
| **Kit-Bashing** | Mixing elements from multiple kits for variation | Bethesda |
| **Attachment Point / Socket** | Predefined location for attaching child objects | Unreal Engine |
| **Affordance** | What an object can do / how it can be interacted with | UX/Robotics |
| **Instance Override** | Per-instance modifications to a prefab | Unity |
| **Reference Node** | Node that embeds another scene | Our terminology |

### Industry Comparisons

| Our System | Similar To | Key Difference |
|------------|-----------|----------------|
| Scene Composer | Halo Forge | Server-side persistence, cross-engine |
| lib-scene | Unity Prefabs | Engine-agnostic, YAML-based |
| lib-asset Bundles | Unreal .pak files | Custom format with metadata |
| Reference Nodes | Nested Prefabs | Cross-scene composition |

---

## 2. Architecture Vision

### Current State

```
┌─────────────────────────────────────────────────────────────┐
│                     STRIDE DEMO                             │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ SceneEditorScript (Stride-specific orchestrator)      │  │
│  │  - Input handling (Stride.Input)                      │  │
│  │  - Camera control                                     │  │
│  │  - Gizmo rendering                                    │  │
│  │  - UI panels (Stride.UI)                              │  │
│  └──────────────────────────────────────────────────────┘  │
│                          │                                  │
│                          ▼                                  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ SceneNodeEntity (Bridge Layer)                        │  │
│  │  - Bidirectional transform sync                       │  │
│  │  - Hierarchy management                               │  │
│  │  - Asset resolution callbacks                         │  │
│  └──────────────────────────────────────────────────────┘  │
│                          │                                  │
│                          ▼                                  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ BannouContentManager (Dual-mode loader)               │  │
│  │  - Reflection mode (development)                      │  │
│  │  - Native mode (production)                           │  │
│  │  - LRU caching                                        │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
              ┌───────────────────────┐
              │   Bannou Services     │
              │                       │
              │  lib-scene (YAML)     │
              │  lib-asset (bundles)  │
              └───────────────────────┘
```

### Target State

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         BANNOU SDK                                       │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ SceneComposer (Engine-Agnostic Core)                                │ │
│  │  - Scene hierarchy management                                       │ │
│  │  - Transform operations (local/world)                               │ │
│  │  - Selection state machine                                          │ │
│  │  - Undo/redo command stack                                          │ │
│  │  - Validation & constraint checking                                 │ │
│  │  - Event publishing (scene.modified, node.added, etc.)              │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│                                    │                                     │
│  ┌─────────────────────────────────┼─────────────────────────────────┐  │
│  │                                 ▼                                  │  │
│  │  ┌────────────────────────────────────────────────────────────┐   │  │
│  │  │ ISceneComposerBridge (Interface for Engine Extensions)      │   │  │
│  │  │  - CreateEntity(nodeId, transform, asset?)                  │   │  │
│  │  │  - DestroyEntity(nodeId)                                    │   │  │
│  │  │  - UpdateTransform(nodeId, localTransform)                  │   │  │
│  │  │  - SetAsset(nodeId, assetRef)                               │   │  │
│  │  │  - RenderGizmo(position, mode, selectedAxis)                │   │  │
│  │  │  - PickObject(ray) -> nodeId?                               │   │  │
│  │  └────────────────────────────────────────────────────────────┘   │  │
│  │                                                                    │  │
│  │  ┌────────────────────────────────────────────────────────────┐   │  │
│  │  │ IAssetLoader<T> (Type-specific loading)                     │   │  │
│  │  │  - LoadAsync(assetRef) -> T                                 │   │  │
│  │  │  - UnloadAsync(assetRef)                                    │   │  │
│  │  │  - GetThumbnail(assetRef) -> Image                          │   │  │
│  │  └────────────────────────────────────────────────────────────┘   │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ AssetBundleManager (Engine-Agnostic)                                │ │
│  │  - Bundle manifest parsing                                          │ │
│  │  - Asset ID → Entry mapping                                         │ │
│  │  - LRU cache with size-based eviction                               │ │
│  │  - Download coordination                                            │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
                              │
    ┌─────────────────────────┼─────────────────────────┐
    │                         │                         │
    ▼                         ▼                         ▼
┌─────────────┐       ┌─────────────┐           ┌─────────────┐
│ Stride      │       │ Unity       │           │ Godot       │
│ Extension   │       │ Extension   │           │ Extension   │
│             │       │             │           │             │
│ Implements: │       │ Implements: │           │ Implements: │
│ ISceneCompo │       │ ISceneCompo │           │ ISceneCompo │
│ serBridge   │       │ serBridge   │           │ serBridge   │
│             │       │             │           │             │
│ IAssetLoade │       │ IAssetLoade │           │ IAssetLoade │
│ r<Model>    │       │ r<Mesh>     │           │ r<Mesh3D>   │
│ r<Texture>  │       │ r<Texture>  │           │ r<Texture>  │
└─────────────┘       └─────────────┘           └─────────────┘
```

---

## 3. Components Already In Place

### 3.1 lib-scene Service (Backend)

**Location**: `/plugins/lib-scene/`
**Schema**: `/schemas/scene-api.yaml`

**What It Provides**:
- Hierarchical scene document storage (YAML format)
- Scene types: region, city, district, lot, building, room, dungeon, arena, vehicle, prefab, cutscene
- Node types: group, mesh, marker, volume, emitter, reference, custom
- Local transforms (position, rotation, scale)
- Asset references (bundleId + assetId + variantId)
- Checkout/locking for concurrent editing (60-min TTL, max 10 extensions)
- Version history with semantic versioning
- Reference resolution (nested scene composition)
- Search by tags, game ID, scene type
- Validation system (structural + game-specific rules)

**Key Endpoints**:
```yaml
POST /scene/create      # Create new scene
POST /scene/get         # Retrieve with optional reference resolution
POST /scene/update      # Update (respects checkout locks)
POST /scene/checkout    # Lock for editing
POST /scene/commit      # Save changes, release lock
POST /scene/instantiate # Notify placement in world (event-driven)
POST /scene/search      # Find scenes by criteria
```

### 3.2 lib-asset Service (Backend)

**Location**: `/plugins/lib-asset/`
**Schema**: `/schemas/asset-api.yaml`

**What It Provides**:
- Pre-signed URL uploads (bypasses WebSocket payload limits)
- Asset types: texture, model, audio, behavior, bundle, prefab, other
- Processing pool for CPU-intensive work (mipmaps, LOD, transcoding)
- .bannou bundle format (LZ4 compressed, manifest + index + assets)
- ZIP conversion caching (24-hour TTL)
- Content-based deduplication (type-{hash} IDs)
- Elastic processing via Orchestrator

**Key Endpoints**:
```yaml
POST /assets/upload/request   # Get pre-signed upload URL
POST /assets/upload/complete  # Finalize upload, trigger processing
POST /assets/get              # Get asset with download URL
POST /bundles/create          # Create .bannou bundle from assets
POST /bundles/get             # Get bundle with download URL
```

### 3.3 Stride Runtime Editor (Reference Implementation)

**Location**: `~/repos/stride-demo/project/MonsterArenaDemo.Game/Editor/`

**Components**:
| File | Purpose | Lines |
|------|---------|-------|
| `SceneEditorScript.cs` | Main orchestrator | 628 |
| `SceneNodeEntity.cs` | Bidirectional bridge (Bannou ↔ Stride) | 311 |
| `TransformGizmo.cs` | Interactive transform manipulation | 412 |
| `EditorUIScript.cs` | UI panel management | 218 |
| `UI/AssetBrowserPanel.cs` | Bundle/asset browser | 349 |
| `UI/SceneHierarchyPanel.cs` | Scene tree view | 251 |

**Asset Loading**:
| File | Purpose |
|------|---------|
| `BannouContentManager.cs` | Dual-mode loading (reflection/native) |
| `BundleAssetLoader.cs` | O(1) asset lookup wrapper |
| `BannouToStrideConverter.cs` | .bannou → Stride .bundle conversion |
| `BundleCacheManager.cs` | Local bundle cache |
| `AssetCache.cs` | LRU cache (256 MB default) |

**Type Loaders**:
| Loader | Handles |
|--------|---------|
| `ModelLoader` | Stride compiled models with dependency resolution |
| `TextureLoader` | Stride textures with GPU resource creation |

### 3.4 SDK Infrastructure

**Location**: `/sdks/client/` and `/sdks/server/`

**Engine-Agnostic Components**:
- `BannouClient` - WebSocket connection management
- `BannouConnectionManager` - Auto-reconnect with backoff
- `RemoteAssetCache<T>` - Memory caching with CRC32 verification
- Generated service clients (ISceneClient, IAssetClient, etc.)

**Behavior Integration Points**:
- `IControlGate` - Control mode management
- `IClientCutsceneHandler` - Cinematic execution
- `IClientInputHandler` - QTE/player choice

---

## 4. What Needs To Be Built

### 4.1 SDK Core: SceneComposer (Engine-Agnostic)

**New Package**: `BeyondImmersion.Bannou.SceneComposer`

**Responsibilities**:
1. **Scene State Management**
   - In-memory scene graph representation
   - Dirty tracking for unsaved changes
   - Undo/redo command stack (Command pattern)

2. **Transform Operations**
   - Local ↔ world transform calculations
   - Constraint systems (grid snap, axis lock, rotation snap)
   - Parent-child transform propagation

3. **Selection System**
   - Single and multi-selection
   - Selection groups
   - Focus/orbit target tracking

4. **Node Operations**
   - Create node (group, mesh, marker, volume, emitter, reference)
   - Delete node (with child handling)
   - Reparent node
   - Duplicate node (with new IDs)
   - Copy/paste

5. **Asset Binding**
   - Assign asset reference to mesh node
   - Clear asset reference
   - Asset variant switching

6. **Validation**
   - Structural validation (single root, unique refIds)
   - Game-specific rule application
   - Constraint violation reporting

7. **Persistence Coordination**
   - Checkout/heartbeat management
   - Commit with conflict detection
   - Discard changes

**Key Interfaces**:
```csharp
public interface ISceneComposer
{
    // Scene lifecycle
    Scene CurrentScene { get; }
    bool IsDirty { get; }
    event EventHandler<SceneModifiedEventArgs> SceneModified;

    // Node operations
    SceneNode CreateNode(NodeType type, string name, SceneNode? parent = null);
    void DeleteNode(SceneNode node, bool deleteChildren = true);
    void ReparentNode(SceneNode node, SceneNode newParent, int? insertIndex = null);
    SceneNode DuplicateNode(SceneNode node, bool deepClone = true);

    // Transform operations
    void SetLocalTransform(SceneNode node, Transform transform);
    Transform GetWorldTransform(SceneNode node);
    void TranslateNode(SceneNode node, Vector3 delta, Space space);
    void RotateNode(SceneNode node, Quaternion delta, Space space);
    void ScaleNode(SceneNode node, Vector3 delta);

    // Selection
    IReadOnlyList<SceneNode> SelectedNodes { get; }
    void Select(SceneNode node, SelectionMode mode = SelectionMode.Replace);
    void ClearSelection();

    // Asset binding
    void BindAsset(SceneNode node, AssetReference asset);
    void ClearAsset(SceneNode node);

    // Undo/Redo
    bool CanUndo { get; }
    bool CanRedo { get; }
    void Undo();
    void Redo();

    // Persistence
    Task<bool> CheckoutAsync(CancellationToken ct = default);
    Task<bool> CommitAsync(string? comment = null, CancellationToken ct = default);
    Task DiscardAsync(CancellationToken ct = default);
}
```

### 4.2 SDK Core: AssetBundleManager (Engine-Agnostic)

**Enhance Existing**: `RemoteAssetCache<T>` in `/sdks/client/`

**Additional Responsibilities**:
1. **Bundle Manifest Parsing**
   - Parse .bannou manifest JSON
   - Build asset ID → entry index

2. **Streaming Support**
   - Progressive asset loading
   - Priority queue for visible assets
   - Background pre-fetching

3. **Thumbnail Generation**
   - Request thumbnail from service (or generate locally)
   - Cache thumbnails separately

4. **Dependency Resolution**
   - Track asset dependencies
   - Pre-load dependencies before primary asset

**Key Interfaces**:
```csharp
public interface IAssetBundleManager
{
    // Bundle management
    Task<BundleHandle> LoadBundleAsync(string bundleId, CancellationToken ct = default);
    Task UnloadBundleAsync(string bundleId, CancellationToken ct = default);
    bool IsBundleLoaded(string bundleId);

    // Asset queries
    IEnumerable<AssetEntry> GetAssetEntries(string bundleId, AssetType? filterType = null);
    bool HasAsset(string bundleId, string assetId);

    // Raw data access (for engine loaders)
    Task<byte[]> GetAssetBytesAsync(string bundleId, string assetId, CancellationToken ct = default);

    // Cache management
    long CacheSize { get; }
    int CachedAssetCount { get; }
    void ClearCache();
    void SetMaxCacheSize(long bytes);
}
```

### 4.3 Engine Extension Interface

**Location**: Part of `BeyondImmersion.Bannou.SceneComposer`

**Purpose**: Define what engine-specific extensions must implement.

```csharp
/// <summary>
/// Bridge between engine-agnostic SceneComposer and engine-specific rendering/interaction.
/// </summary>
public interface ISceneComposerBridge
{
    // Entity lifecycle
    object CreateEntity(Guid nodeId, Transform worldTransform, AssetReference? asset);
    void DestroyEntity(Guid nodeId);
    void SetEntityParent(Guid nodeId, Guid? parentNodeId);

    // Transform
    void UpdateEntityTransform(Guid nodeId, Transform worldTransform);

    // Asset binding
    Task SetEntityAssetAsync(Guid nodeId, AssetReference asset, CancellationToken ct = default);
    void ClearEntityAsset(Guid nodeId);

    // Selection visualization
    void SetEntitySelected(Guid nodeId, bool selected);
    void SetEntityHovered(Guid nodeId, bool hovered);

    // Gizmo rendering (optional - some engines may use built-in gizmos)
    void RenderGizmo(Vector3 position, Quaternion rotation, GizmoMode mode, GizmoAxis activeAxis);
    void HideGizmo();

    // Picking
    Guid? PickEntity(Ray ray);

    // Camera (optional - SceneComposer can manage its own if needed)
    void FocusCamera(Vector3 target, float distance);
    Ray GetMouseRay(Vector2 screenPosition);
}

/// <summary>
/// Type-specific asset loader for a particular engine.
/// </summary>
public interface IAssetLoader<T>
{
    Task<T> LoadAsync(byte[] data, string assetId, CancellationToken ct = default);
    void Unload(T asset);
    long EstimateSize(T asset);
}
```

### 4.4 Stride Extension Package

**New Package**: `BeyondImmersion.Bannou.Stride.SceneComposer`

**Contents**:
- `StrideSceneComposerBridge : ISceneComposerBridge`
- `StrideModelLoader : IAssetLoader<Model>`
- `StrideTextureLoader : IAssetLoader<Texture>`
- `StrideAnimationLoader : IAssetLoader<Animation>`
- `StrideTransformGizmo` (port from stride-demo)
- `StrideEditorCamera` (port from stride-demo)

**Migration Path**:
1. Extract `SceneNodeEntity` logic → SDK core
2. Extract `TransformGizmo` logic → Stride extension
3. Extract `BannouContentManager` caching → SDK core
4. Keep `BannouToStrideConverter` in Stride extension
5. Keep type loaders (`ModelLoader`, `TextureLoader`) in Stride extension

### 4.5 Attachment Points & Affordances

**Extend Scene Schema** (`/schemas/scene-api.yaml`):

```yaml
# Add to SceneNode
annotations:
  type: object
  description: Consumer-specific namespaced annotations
  additionalProperties: true
  properties:
    attachments:
      type: array
      description: Predefined attachment points on this node
      items:
        $ref: '#/components/schemas/AttachmentPoint'
    affordances:
      type: array
      description: What this node can do / how it can be interacted with
      items:
        $ref: '#/components/schemas/Affordance'
    assetSlot:
      $ref: '#/components/schemas/AssetSlot'
      description: Defines acceptable asset types for procedural swapping

AttachmentPoint:
  type: object
  required: [name, localTransform]
  properties:
    name:
      type: string
      description: Unique name for this attachment (e.g., "wall_hook_left")
    localTransform:
      $ref: '#/components/schemas/Transform'
    acceptsTags:
      type: array
      items:
        type: string
      description: Tags of assets that can attach here (e.g., ["wall_decoration", "picture_frame"])
    defaultAsset:
      $ref: '#/components/schemas/AssetReference'
      description: Default asset to show if none specified

Affordance:
  type: object
  required: [type]
  properties:
    type:
      type: string
      enum: [walkable, climbable, sittable, interactive, collectible, destructible, container, door, teleport]
    parameters:
      type: object
      additionalProperties: true
      description: Type-specific parameters

AssetSlot:
  type: object
  required: [slotType]
  properties:
    slotType:
      type: string
      description: Category of assets acceptable (e.g., "chair", "table", "wall_art")
    acceptsTags:
      type: array
      items:
        type: string
    defaultAsset:
      $ref: '#/components/schemas/AssetReference'
    variations:
      type: array
      items:
        $ref: '#/components/schemas/AssetReference'
      description: Pre-approved variations for random/targeted swapping
```

---

## 5. Objectives & Success Criteria

### Primary Objectives

1. **Eliminate Engine Editor Dependency**
   - 100% of game content created through Scene Composer
   - Engine editors used only for asset export (models, textures, animations)
   - No scene composition in Stride Editor, Unity Editor, etc.

2. **Unified Scene Format**
   - Single .scene (YAML) format works across all engines
   - Same scene loads in Stride, Unity, Godot, Unreal
   - Engine differences abstracted by ISceneComposerBridge

3. **Reusable Compositions**
   - Furniture sets, room templates, building shells as nested prefabs
   - Reference nodes enable scene-within-scene composition
   - Variant system for per-instance customization

4. **Procedural Generation Foundation**
   - AssetSlot annotations define what can be swapped
   - Variations list enables random selection
   - Affordance tags enable AI-driven placement decisions

5. **Mapping API Integration**
   - Attachment points queryable via lib-mapping
   - Affordances exposed for AI navigation/interaction
   - Spatial indexing of scene instances

### Success Criteria

| Metric | Target |
|--------|--------|
| Scene load time (1000 nodes) | < 2 seconds |
| Asset cache hit rate | > 90% after warmup |
| Undo/redo stack depth | 100 operations minimum |
| Reference resolution depth | 10 levels |
| Concurrent editors | Lock-based, no conflicts |
| Cross-engine compatibility | Same scene, 3+ engines |

---

## 6. Implementation Phases

### Phase 1: SDK Core Extraction (2-3 weeks)

**Goal**: Extract engine-agnostic components from stride-demo.

**Tasks**:
1. Create `BeyondImmersion.Bannou.SceneComposer` project
2. Port scene graph management from `SceneNodeEntity`
3. Implement `ISceneComposer` interface
4. Implement undo/redo command stack
5. Port asset caching from `BannouContentManager` → `AssetBundleManager`
6. Define `ISceneComposerBridge` and `IAssetLoader<T>` interfaces
7. Unit tests for core operations

**Deliverables**:
- NuGet package: `BeyondImmersion.Bannou.SceneComposer`
- Core interfaces documented
- 80%+ unit test coverage

### Phase 2: Stride Extension (1-2 weeks)

**Goal**: Create Stride-specific implementation using SDK core.

**Tasks**:
1. Create `BeyondImmersion.Bannou.Stride.SceneComposer` project
2. Implement `StrideSceneComposerBridge`
3. Port `ModelLoader`, `TextureLoader` as `IAssetLoader<T>` implementations
4. Port `TransformGizmo` → `StrideTransformGizmo`
5. Port `BannouToStrideConverter` (native bundle support)
6. Port UI panels (AssetBrowser, SceneHierarchy)
7. Integration test with stride-demo

**Deliverables**:
- NuGet package: `BeyondImmersion.Bannou.Stride.SceneComposer`
- stride-demo refactored to use new SDK
- No functionality regression

### Phase 3: Attachment Points & Affordances (1-2 weeks)

**Goal**: Extend scene schema for procedural generation support.

**Tasks**:
1. Update `/schemas/scene-api.yaml` with AttachmentPoint, Affordance, AssetSlot
2. Regenerate lib-scene service
3. Implement attachment point visualization in Scene Composer
4. Implement affordance tag editor UI
5. Add AssetSlot configuration UI
6. Document annotation patterns

**Deliverables**:
- Updated scene schema
- UI for managing attachment points
- Example prefabs with annotations

### Phase 4: Mapping Integration (1 week)

**Goal**: Expose scene data for spatial queries.

**Tasks**:
1. Implement event handler in lib-mapping for `scene.instantiated`
2. Extract spatial objects (markers, volumes) from scene
3. Index attachment points for query
4. Expose affordances in mapping API
5. Test with character navigation

**Deliverables**:
- Attachment point queries via lib-mapping
- Affordance-based navigation hints
- Integration documentation

### Phase 5: Unity Extension (2-3 weeks)

**Goal**: Prove cross-engine compatibility with Unity implementation.

**Tasks**:
1. Create Unity package (UPM)
2. Implement `UnitySceneComposerBridge`
3. Implement `UnityMeshLoader`, `UnityTextureLoader`
4. Unity-native gizmo integration
5. Unity UI panels
6. Validate same scene loads in both engines

**Deliverables**:
- Unity package
- Cross-engine scene compatibility verified
- Documentation for Unity developers

### Phase 6: Godot Extension (Optional, 2 weeks)

**Goal**: Third engine validates architecture generality.

**Tasks**:
1. Create Godot addon (GDExtension or C# binding)
2. Implement `GodotSceneComposerBridge`
3. Implement asset loaders
4. Godot-native controls

**Deliverables**:
- Godot addon
- Three-engine compatibility verified

---

## 7. Decision: New Plugin vs SDK Helpers

### Analysis

| Approach | Pros | Cons |
|----------|------|------|
| **New lib-composer Plugin** | Server-side composition validation; Centralized constraint enforcement | Over-engineering; Composition is client-side activity |
| **SDK Package Only** | Composition happens where it's used; Simpler architecture | Validation must be duplicated in each engine |
| **Hybrid: SDK + Schema Extensions** | Best of both; Validation in lib-scene; Composition in SDK | Two packages to maintain |

### Recommendation: Hybrid Approach

**SDK Package** (`BeyondImmersion.Bannou.SceneComposer`):
- All composition logic (node operations, transforms, selection)
- Engine bridge interface
- Asset loading abstraction
- Undo/redo stack
- Local validation (for immediate feedback)

**lib-scene Enhancements**:
- Attachment point schema additions
- Affordance annotations
- AssetSlot definitions
- Server-side validation rules
- Game-specific constraint registration

**No New Plugin** - The existing lib-scene already handles:
- Scene storage
- Checkout/locking
- Version history
- Validation rules
- Search/discovery

Adding a lib-composer would duplicate concerns. The SDK handles client-side composition; lib-scene handles server-side persistence.

---

## 8. Migration Strategy for stride-demo

### Current Structure
```
stride-demo/project/MonsterArenaDemo.Game/
├── Editor/
│   ├── SceneEditorScript.cs      → Refactor to use SDK
│   ├── SceneNodeEntity.cs        → Extract to SDK core
│   ├── TransformGizmo.cs         → Move to Stride extension
│   ├── EditorUIScript.cs         → Keep (Stride-specific UI)
│   └── UI/                        → Keep (Stride-specific UI)
└── Assets/
    ├── BannouContentManager.cs   → Split: caching → SDK, loading → extension
    ├── BundleAssetLoader.cs      → Move to SDK
    ├── BannouToStrideConverter.cs → Move to Stride extension
    ├── BundleCacheManager.cs     → Move to SDK
    ├── AssetCache.cs             → Move to SDK
    └── TypeLoaders/               → Move to Stride extension
```

### Target Structure
```
Bannou.SceneComposer/              # NuGet: BeyondImmersion.Bannou.SceneComposer
├── SceneComposer.cs               # ISceneComposer implementation
├── SceneGraph/
│   ├── SceneNode.cs               # Enhanced from stride-demo
│   ├── TransformCalculator.cs
│   └── HierarchyManager.cs
├── Commands/
│   ├── ICommand.cs
│   ├── CommandStack.cs
│   └── NodeCommands.cs
├── Assets/
│   ├── AssetBundleManager.cs
│   ├── AssetCache.cs              # From stride-demo
│   └── BundleManifest.cs
└── Abstractions/
    ├── ISceneComposerBridge.cs
    └── IAssetLoader.cs

Bannou.Stride.SceneComposer/       # NuGet: BeyondImmersion.Bannou.Stride.SceneComposer
├── StrideSceneComposerBridge.cs
├── StrideTransformGizmo.cs        # From stride-demo
├── StrideEditorCamera.cs
├── Loaders/
│   ├── StrideModelLoader.cs       # From stride-demo
│   ├── StrideTextureLoader.cs
│   └── BannouToStrideConverter.cs # From stride-demo
└── UI/                             # Stride-specific panels
    ├── AssetBrowserPanel.cs
    └── SceneHierarchyPanel.cs

stride-demo/project/MonsterArenaDemo.Game/
├── Editor/
│   ├── MonsterArenaSceneEditor.cs # Uses SDK, thin wrapper
│   └── MonsterArenaEditorUI.cs    # Game-specific UI
└── (Assets/ folder removed - now in SDK packages)
```

---

## 9. Lessons From Unity's Nested Prefabs (13-Year Journey)

Unity's nested prefabs feature took **13 years** to deliver (requested 2005, shipped 2018.3). Understanding why helps us avoid the same mistakes.

### What Went Wrong at Unity

| Problem | Impact | Our Mitigation |
|---------|--------|----------------|
| **Serialization architecture** | "Prefab" only existed at editor-time; at runtime, data was "baked" | We serialize to YAML always; no baking step |
| **No import pipeline** | Prefabs read directly from disk, no processing | lib-scene processes on save, validates structure |
| **Reference management** | Nested prefab connections "lost" when placed | Explicit `reference` node type with sceneId annotation |
| **Mixed editing context** | Editing prefabs required awkward scene drag-drop | Scene Composer is THE editing context, always |
| **Override tracking** | Couldn't track changes at multiple nesting levels | Instance overrides stored per-node in annotations |
| **The "Apply" button** | Accidentally overwrote prefab assets | No global apply; explicit commit per scene |

### Critical Insight: Editor vs Runtime

> "The Prefab instance only exists while you edit your project in the Unity Editor. During the project build, the Unity Editor instantiates a GameObject from its two sets of serialization data: the Prefab source and the Prefab instance's modifications... There is no concept of prefabs at runtime."

**Our approach is fundamentally different**:
- Scenes exist identically at edit-time AND runtime
- No "baking" step that discards structure
- Reference nodes resolved on-demand, not flattened
- Same YAML format in editor, in lib-scene, in game client

### Unity's Solution: Prefab Mode

Unity introduced **Prefab Mode** - a completely separate editing context:
- Opens prefab in isolation, hiding the scene
- Changes affect the prefab asset directly
- Clear visual distinction between prefab and scene editing

**Our equivalent**: Scene Composer always edits scenes in isolation. There's no "scene view" where you might accidentally edit the wrong thing. Each scene is its own document.

### What We Must NOT Do

1. **Never conflate instance and template editing** - Each scene is a document. Reference nodes point to other documents. Editing a referenced scene requires opening THAT scene.

2. **Never "bake" structure away** - The YAML format is the runtime format. No compilation step that loses hierarchy information.

3. **Never rely on editor-only concepts** - Everything that exists in Scene Composer must work identically in a headless game server.

4. **Never auto-propagate changes to instances** - Reference nodes always load the latest version of referenced scenes, but the referencing scene itself doesn't change.

### Why We're Better Positioned

| Unity Challenge | Our Advantage |
|-----------------|---------------|
| 15+ years of legacy projects | Starting fresh, no backwards compatibility debt |
| Binary serialization format | Human-readable YAML, easy to migrate |
| Editor-only prefab concept | Scenes work identically everywhere |
| Tight coupling to Unity editor | Engine-agnostic SDK from day one |
| Complex override tracking | Simple: your changes are YOUR scene |

---

## 10. Open Questions - ANSWERED

### Q1: Scene Format Versioning

**Question**: How do we handle schema changes without breaking existing scenes?

**Answer**: Adopt **SchemaVer** pattern (MODEL-REVISION-ADDITION) with lazy migration.

**Implementation**:

```yaml
# Scene header
$schema: "bannou://scene/v2.1.0"
schema_version: "2.1.0"
# ... rest of scene
```

**Version Semantics**:
- **MODEL** (2.x.x): Breaking change to existing fields - requires migration
- **REVISION** (x.1.x): Change affecting existing data - may require migration
- **ADDITION** (x.x.1): New optional field - backward compatible

**Migration Strategy: Lazy Migration on Save**

```csharp
public class SceneMigrationPipeline
{
    public Scene Load(string yaml)
    {
        var version = ExtractSchemaVersion(yaml);
        var scene = Deserialize(yaml, version);

        // Migrate in memory, don't persist yet
        if (version < CurrentVersion)
        {
            scene = MigrateToLatest(scene, version);
            scene.IsMigrated = true;  // Flag for UI
        }
        return scene;
    }

    public string Save(Scene scene)
    {
        scene.SchemaVersion = CurrentVersion;  // Always save as latest
        return Serialize(scene);
    }
}
```

**Migration Chain Pattern**:
```csharp
// Each version bump has a dedicated migrator
IMigrator[] migrators = [
    new V1ToV2Migrator(),  // Adds annotations field
    new V2ToV3Migrator(),  // Renames nodeType values
    // ...
];
```

**Key Principles**:
- Never break loading of old scenes (backward compatible reads)
- Always save as current version (forward migration on save)
- Maintain test fixtures for each historical version
- Document breaking changes in CHANGELOG

---

### Q2: Collaborative Editing

**Question**: Should we implement OT/CRDT for real-time collaboration?

**Answer**: **Not now**. Enhance lock-based editing with presence, then evaluate future needs.

**Why Lock-Based is Sufficient for Now**:
- Team sizes of 2-20 work well with locks
- Our content is separable (different scenes, regions)
- Most edits are solo work on specific prefabs
- Lock contention is low with good tooling

**Recommended Enhancement Path**:

```
Level 1: Current (Scene-Level Locks)
    ↓
Level 2: Enhanced Locking + Presence  ← IMPLEMENT NOW
    - Entity-level locks (not whole scene)
    - Real-time "who's viewing what"
    - Lock notifications and timeouts
    - Conflict detection at commit time
    ↓
Level 3: Optimistic with Merge  ← FUTURE (if needed)
    - No locks for viewing
    - Locks for editing intent
    - 3-way merge for compatible changes
    ↓
Level 4: Hybrid Real-Time  ← FAR FUTURE (if needed)
    - CRDT for simple properties (position, name)
    - Locks for complex operations
```

**Level 2 Implementation Details**:

```csharp
// New events for lib-scene
public record EditorPresenceEvent
{
    public Guid SceneId { get; init; }
    public Guid AccountId { get; init; }
    public string DisplayName { get; init; }
    public Guid? ViewingNodeId { get; init; }  // What they're looking at
    public Guid? EditingNodeId { get; init; }  // What they have locked
}

// State store additions
// scene:{sceneId}:presence → Set<AccountId> with TTL
// scene:{sceneId}:node-locks:{nodeId} → AccountId with TTL
```

**Why Not CRDT Now**:
- Adds significant complexity (~6+ months of work)
- Our hierarchy (parent-child relationships) is hard for CRDTs
- MovableTree CRDTs (Loro) are new, less battle-tested
- Lock-based is simpler to reason about and debug

**When to Reconsider**:
- If we hit >20 concurrent editors on same scene
- If wait times exceed 5 minutes regularly
- If we need offline editing with sync

---

### Q3: Asset Validation

**Question**: Should AssetSlot enforce that only matching assets can be bound? At composition time vs commit time?

**Answer**: **Both**, with different strictness.

**Validation Tiers**:

| When | What | Behavior |
|------|------|----------|
| **Composition time** | Warnings only | Show "asset doesn't match slot" but allow |
| **Commit time** | Schema violations | Block if required fields missing |
| **Game-specific rules** | Per-game constraints | Configurable: warn or block |

**Implementation**:

```csharp
public enum ValidationSeverity
{
    Info,       // Log but don't interrupt
    Warning,    // Show UI warning, allow proceed
    Error,      // Block action, require fix
    Critical    // Block and rollback partial changes
}

public class AssetSlotValidator
{
    public ValidationResult Validate(SceneNode node, AssetReference asset)
    {
        var slot = node.Annotations?.AssetSlot;
        if (slot == null) return ValidationResult.Ok;

        var issues = new List<ValidationIssue>();

        // Check slot type match
        if (!string.IsNullOrEmpty(slot.SlotType))
        {
            if (!asset.Tags.Contains(slot.SlotType))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"Asset '{asset.Name}' is not tagged as '{slot.SlotType}'"));
            }
        }

        // Check accepted tags
        if (slot.AcceptsTags?.Any() == true)
        {
            if (!slot.AcceptsTags.Any(t => asset.Tags.Contains(t)))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"Asset '{asset.Name}' doesn't have any accepted tags"));
            }
        }

        return new ValidationResult(issues);
    }
}
```

**User Experience**:
- During composition: Yellow warning icon on node, tooltip explains issue
- At commit: Dialog lists all warnings, option to commit anyway or fix
- Game-specific rules can upgrade warnings to errors

---

### Q4: Gizmo Rendering

**Question**: Let each engine use native gizmos, or provide cross-engine gizmo rendering?

**Answer**: **Hybrid approach** - Abstract logic in SDK, engine-specific rendering.

**Rationale**:
- Gizmo LOGIC (picking, constraints, state machine) is 100% engine-agnostic
- Gizmo RENDERING must use engine-specific primitives
- Input handling varies per engine
- Each engine has different ways to draw debug lines/shapes

**Architecture**:

```
┌─────────────────────────────────────────────────────────────┐
│                 SDK Core (Engine-Agnostic)                  │
├─────────────────────────────────────────────────────────────┤
│  GizmoCore                                                  │
│  ├─ GizmoStateMachine (Idle → Hovering → Dragging)         │
│  ├─ AxisPicker (ray-line/plane intersection math)          │
│  ├─ TransformConstraints (grid snap, axis lock)            │
│  ├─ TransformDelta (computed position/rotation/scale)      │
│  └─ GizmoConfiguration (colors, sizes, sensitivities)      │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│                 IGizmoBridge (Interface)                    │
├─────────────────────────────────────────────────────────────┤
│  // Engine must implement:                                  │
│  void DrawLine(Vector3 start, Vector3 end, Color color);   │
│  void DrawCone(Vector3 tip, Vector3 direction, Color);     │
│  void DrawCircle(Vector3 center, Vector3 normal, Color);   │
│  Ray GetMouseRay();                                         │
│  Vector2 GetMousePosition();                                │
│  bool IsMouseButtonDown(MouseButton button);                │
└─────────────────────────────────────────────────────────────┘
                            ↓
    ┌───────────────────┬───────────────────┬─────────────────┐
    │  StrideGizmoBridge│  UnityGizmoBridge │ GodotGizmoBridge│
    │  (Stride.Debug)   │  (GL.Lines/Gizmos)│ (DebugDraw3D)   │
    └───────────────────┴───────────────────┴─────────────────┘
```

**Configuration**:
```csharp
public class GizmoConfiguration
{
    // Axis colors (RGB = XYZ convention)
    public Color XAxisColor { get; set; } = new(255, 80, 80);
    public Color YAxisColor { get; set; } = new(80, 255, 80);
    public Color ZAxisColor { get; set; } = new(80, 80, 255);
    public Color HighlightColor { get; set; } = new(255, 255, 100);

    // Sizing
    public float HandleSize { get; set; } = 1.0f;
    public float ScreenScaleFactor { get; set; } = 0.15f;
    public float PickRadius { get; set; } = 0.1f;

    // Behavior
    public float TranslationSensitivity { get; set; } = 0.01f;
    public float RotationSensitivity { get; set; } = 0.01f;
    public float ScaleSensitivity { get; set; } = 0.01f;

    // Snapping
    public float GridSnapSize { get; set; } = 0.25f;
    public float RotationSnapAngle { get; set; } = 15f;  // degrees
}
```

**Why Not Fully Engine-Native**:
- Inconsistent behavior between engines
- Can't share state (what's selected, drag state)
- Each engine would need full gizmo reimplementation
- Testing becomes N engines × M operations

**Why Not Fully Custom Rendering**:
- Each engine has optimized debug drawing
- Would need to implement line/mesh rendering per engine anyway
- Engine-native input handling is already abstracted by bridge

---

### Q5: UI Framework

**Question**: Should SDK provide abstract UI model that engines implement?

**Answer**: **No abstract UI model**. Let each engine use native UI.

**Rationale**:
- UI frameworks are the MOST different between engines
- Unity has Unity UI/UIToolkit, Stride has Stride.UI, Godot has Control nodes
- Abstracting UI provides little value for significant complexity
- Editor UIs are often the most customized part of game tooling

**What SDK DOES Provide**:

```csharp
// Data models for UI to consume
public interface ISceneComposerViewModel
{
    // Hierarchy data
    IObservable<SceneNode> RootNode { get; }
    IObservable<IReadOnlySet<SceneNode>> SelectedNodes { get; }

    // Asset browser data
    IObservable<IReadOnlyList<BundleInfo>> LoadedBundles { get; }
    IObservable<IReadOnlyList<AssetEntry>> AvailableAssets { get; }

    // Editor state
    IObservable<EditorMode> CurrentMode { get; }
    IObservable<bool> IsDirty { get; }
    IObservable<CheckoutState?> CheckoutState { get; }

    // Commands (bindable to UI buttons)
    ICommand CreateGroupCommand { get; }
    ICommand DeleteSelectedCommand { get; }
    ICommand UndoCommand { get; }
    ICommand RedoCommand { get; }
    ICommand SaveCommand { get; }
}
```

**Engine Extension Responsibility**:

```csharp
// Stride example - wraps ViewModel for Stride.UI
public class StrideSceneHierarchyPanel : Border
{
    private readonly ISceneComposerViewModel _viewModel;

    public StrideSceneHierarchyPanel(ISceneComposerViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.RootNode.Subscribe(RebuildTree);
        _viewModel.SelectedNodes.Subscribe(UpdateSelection);
    }

    private void RebuildTree(SceneNode root) { /* Stride UI code */ }
}
```

**This Approach**:
- SDK provides data + logic, engines provide presentation
- Each engine can use idiomatic UI patterns
- No lowest-common-denominator UI
- Engine teams have full creative control over UX

---

## 11. Stride-Demo Analysis: Critical Gaps to Address

The deep analysis of stride-demo revealed several issues that MUST be fixed in the SDK:

### Gap 1: Undo/Redo System (COMPLETELY ABSENT)

**Current state**: Zero undo/redo capability

**Required Command Pattern**:

```csharp
public interface IEditorCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public class CommandStack
{
    private readonly Stack<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();
    private const int MaxStackDepth = 100;

    public void Execute(IEditorCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();  // New action clears redo
        TrimStack();
    }

    public void Undo()
    {
        if (_undoStack.TryPop(out var command))
        {
            command.Undo();
            _redoStack.Push(command);
        }
    }

    public void Redo()
    {
        if (_redoStack.TryPop(out var command))
        {
            command.Execute();
            _undoStack.Push(command);
        }
    }
}
```

**Commands Needed**:

| Command | Captures |
|---------|----------|
| `CreateNodeCommand` | Node data, parent ID, insert index |
| `DeleteNodeCommand` | Full node subtree, parent ID, index |
| `MoveNodeCommand` | Node ID, old/new position, space |
| `RotateNodeCommand` | Node ID, old/new rotation |
| `ScaleNodeCommand` | Node ID, old/new scale |
| `ReparentNodeCommand` | Node ID, old/new parent, old/new index |
| `BindAssetCommand` | Node ID, old/new asset reference |
| `BatchCommand` | List of sub-commands (for grouped operations) |

**Special: Transform Drag Sessions**:
```csharp
public class TransformDragSession
{
    private readonly SceneNode _node;
    private readonly Transform _initialTransform;

    public void Begin(SceneNode node)
    {
        _node = node;
        _initialTransform = node.LocalTransform.Clone();
    }

    public void Update(Transform current)
    {
        // Apply to node, but don't create command yet
        _node.LocalTransform = current;
    }

    public IEditorCommand End()
    {
        // NOW create command with initial → final
        return new SetTransformCommand(_node, _initialTransform, _node.LocalTransform);
    }
}
```

### Gap 2: Multi-Selection (SINGLE ONLY)

**Current**: `private SceneNodeEntity? _selectedNode;`

**Required**:
```csharp
public class SelectionManager
{
    private readonly HashSet<SceneNode> _selectedNodes = new();
    public SceneNode? PrimarySelection => _selectedNodes.FirstOrDefault();
    public IReadOnlySet<SceneNode> SelectedNodes => _selectedNodes;

    public void Select(SceneNode node, SelectionMode mode)
    {
        switch (mode)
        {
            case SelectionMode.Replace:
                _selectedNodes.Clear();
                _selectedNodes.Add(node);
                break;
            case SelectionMode.Add:      // Ctrl+Click
                _selectedNodes.Add(node);
                break;
            case SelectionMode.Toggle:   // Ctrl+Click on selected
                if (!_selectedNodes.Remove(node))
                    _selectedNodes.Add(node);
                break;
            case SelectionMode.Range:    // Shift+Click
                SelectRange(_primarySelection, node);
                break;
        }
        OnSelectionChanged?.Invoke();
    }
}
```

### Gap 3: Reference Node Resolution (NOT IMPLEMENTED)

**Current**: Reference node type exists in schema but has no runtime support.

**Required**:
```csharp
public class ReferenceNodeResolver
{
    private readonly ISceneClient _sceneClient;
    private readonly HashSet<Guid> _visitedScenes = new();  // Cycle detection

    public async Task<ResolvedReference> ResolveAsync(
        SceneNode referenceNode,
        int currentDepth,
        int maxDepth,
        CancellationToken ct)
    {
        if (currentDepth > maxDepth)
            return ResolvedReference.DepthExceeded(referenceNode);

        var sceneId = GetReferencedSceneId(referenceNode);

        if (_visitedScenes.Contains(sceneId))
            return ResolvedReference.CircularReference(referenceNode, sceneId);

        _visitedScenes.Add(sceneId);

        try
        {
            var scene = await _sceneClient.GetAsync(sceneId, ct);
            if (scene == null)
                return ResolvedReference.NotFound(referenceNode, sceneId);

            // Recursively resolve nested references
            await ResolveChildReferencesAsync(scene.Root, currentDepth + 1, maxDepth, ct);

            return ResolvedReference.Success(referenceNode, scene);
        }
        finally
        {
            _visitedScenes.Remove(sceneId);
        }
    }
}
```

### Gap 4: Error Handling (SWALLOWS ERRORS)

**Current**:
```csharp
catch { return null; }  // Silent failure
```

**Required**:
```csharp
public interface IEditorErrorHandler
{
    void HandleError(EditorError error);
    void HandleWarning(EditorWarning warning);
}

public record EditorError(
    string Operation,
    string Message,
    Exception? Exception,
    ErrorRecoveryAction[] AvailableActions);

public enum ErrorRecoveryAction
{
    Retry,
    Skip,
    Abort,
    UseDefault
}

// Usage
try
{
    model = await LoadModelAsync(assetId);
}
catch (AssetNotFoundException ex)
{
    _errorHandler.HandleError(new EditorError(
        "LoadAsset",
        $"Asset '{assetId}' not found in bundle",
        ex,
        [ErrorRecoveryAction.Skip, ErrorRecoveryAction.UseDefault]
    ));
    model = _placeholderModel;  // Show something rather than invisible
}
```

### Gap 5: Sync-Over-Async (DEADLOCK RISK)

**Current**:
```csharp
return _contentManager.GetModelAsync(assetId).GetAwaiter().GetResult();
```

**Required**: Proper async callback or async all the way:
```csharp
// Option A: Async callback pattern
public void LoadSceneNode(
    SceneNode node,
    Action<SceneNode, Model?> onModelLoaded)
{
    _ = LoadSceneNodeAsync(node, onModelLoaded);
}

private async Task LoadSceneNodeAsync(
    SceneNode node,
    Action<SceneNode, Model?> onModelLoaded)
{
    var model = await _contentManager.GetModelAsync(node.Asset.AssetId);
    onModelLoaded(node, model);
}

// Option B: Full async with placeholder
public async Task<SceneNodeEntity> BuildFromSceneNodeAsync(SceneNode node)
{
    var entity = CreateEntityWithPlaceholder(node);

    if (node.Asset != null)
    {
        var model = await _contentManager.GetModelAsync(node.Asset.AssetId);
        entity.SetModel(model);
    }

    return entity;
}
```

---

## 12. Refined Implementation Strategy

Based on all research, here's the refined phase plan:

### Phase 1: SDK Core Foundation (3-4 weeks)

**Focus**: Build the engine-agnostic foundation RIGHT the first time.

| Week | Deliverables |
|------|--------------|
| 1 | Project structure, `ISceneComposer` interface, scene graph classes |
| 2 | Command pattern (undo/redo), selection manager, transform operations |
| 3 | `ISceneComposerBridge` interface, `IAssetBundleManager`, error handling |
| 4 | Unit tests (80%+ coverage), documentation, API review |

**Key Design Decisions**:
- Use `IObservable<T>` for reactive UI binding
- Use `ValueTask` where hot-path async is needed
- Define clear ownership: SDK owns data, Bridge owns rendering
- All public APIs documented with XML comments

### Phase 2: Stride Extension + Migration (2-3 weeks)

**Focus**: Prove the abstraction works by migrating stride-demo.

| Week | Deliverables |
|------|--------------|
| 1 | `StrideSceneComposerBridge`, asset loaders, basic integration |
| 2 | Gizmo bridge, camera controller, UI panels using ViewModel |
| 3 | Full stride-demo migration, regression testing, performance validation |

**Success Criteria**:
- stride-demo works identically to before
- All operations go through SDK
- No direct Stride types in core logic

### Phase 3: Reference Nodes + Validation (2 weeks)

**Focus**: Complete the scene composition model.

| Week | Deliverables |
|------|--------------|
| 1 | Reference node resolution, circular detection, missing scene handling |
| 2 | Schema validation, game-specific rules, AssetSlot enforcement |

### Phase 4: Schema Extensions (1-2 weeks)

**Focus**: Attachment points and affordances.

| Deliverables |
|--------------|
| Schema updates to `/schemas/scene-api.yaml` |
| Regenerate lib-scene |
| Visualization in Scene Composer |
| Example prefabs demonstrating patterns |

### Phase 5: Presence + Enhanced Locking (1-2 weeks)

**Focus**: Multi-user awareness (Level 2 collaborative editing).

| Deliverables |
|--------------|
| Entity-level locks in lib-scene |
| Presence events via WebSocket |
| UI showing who's editing what |
| Lock timeout and notification |

### Phase 6: Unity Extension (3 weeks)

**Focus**: Validate cross-engine architecture.

| Deliverables |
|--------------|
| Unity package (UPM) |
| `UnitySceneComposerBridge` |
| Asset loaders for Unity types |
| Same scene loads in Stride AND Unity |

### Total Estimated Effort

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| Phase 1 | 3-4 weeks | None |
| Phase 2 | 2-3 weeks | Phase 1 |
| Phase 3 | 2 weeks | Phase 2 |
| Phase 4 | 1-2 weeks | Phase 1 |
| Phase 5 | 1-2 weeks | Phase 1 |
| Phase 6 | 3 weeks | Phases 1-4 |
| **Total** | **12-16 weeks** | |

Phases 4 and 5 can run in parallel with Phase 3.

---

## 13. Future Considerations

### Real-Time Collaboration (If Needed Later)

If lock-based editing becomes a bottleneck, consider:

1. **Loro CRDT Library** - Has MovableTree for hierarchical data
2. **Figma-style Hybrid** - CRDT for simple properties, locks for complex ops
3. **OT via lib-messaging** - Operations transformed server-side

**Estimated additional effort**: 6-9 months

### Versioning Automation

Consider tooling for:
- Automatic migration script generation from schema diffs
- Test fixture generation for each version
- Breaking change detection in CI

### Offline Editing

If needed, CRDT enables:
- Edit scenes without network connection
- Sync when reconnected
- Conflict resolution at merge time

---

## 14. References

### GDC Talks
- [Skyrim's Modular Level Design (GDC 2013)](http://blog.joelburgess.com/2013/04/skyrims-modular-level-design-gdc-2013.html)
- [Keeping Level Designers in the Zone (GDC 2016)](https://gdcvault.com/browse/gdc-16/play/1023235)
- [The Architecture of Dreams - Media Molecule (GDC 2020)](https://www.gamedeveloper.com/programming/see-how-media-molecule-architected-the-code-i-dreams-i-are-made-of-at-gdc-)
- [Creating a Tools Pipeline for Horizon: Zero Dawn](https://www.gdcvault.com/play/1024124/Creating-a-Tools-Pipeline-for)

### Unity Nested Prefabs (Case Study)
- [Unity Blog: Introducing new Prefab workflows](https://unity.com/blog/technology/introducing-new-prefab-workflows)
- [PocketGamer: Unity on creating workable nested prefabs](https://www.pocketgamer.biz/interview/69298/unity-on-creating-workable-nested-prefabs/)
- [Unity Manual: Nested Prefabs](https://docs.unity3d.com/Manual/NestedPrefabs.html)
- [SlideShare: Technical Deep Dive into the New Prefab System](https://www.slideshare.net/unity3d/technical-deep-dive-into-the-new-prefab-system)
- [Game Dev Beginner: Prefabs in Unity](https://gamedevbeginner.com/how-to-use-prefabs-in-unity/)

### Collaborative Editing
- [Figma: How Multiplayer Technology Works](https://www.figma.com/blog/how-figmas-multiplayer-technology-works/)
- [TinyCloud: Real-Time Collaboration OT vs CRDT](https://www.tiny.cloud/blog/real-time-collaboration-ot-vs-crdt/)
- [Loro CRDT Library](https://loro.dev/) - MovableTree for hierarchical data
- [Yjs CRDT Library](https://github.com/yjs/yjs)
- [Unreal Multi-User Editing](https://dev.epicgames.com/documentation/en-us/unreal-engine/multi-user-editing-in-unreal-engine)

### Schema Versioning
- [Snowplow: SchemaVer for Semantic Versioning of Schemas](https://snowplow.io/blog/introducing-schemaver-for-semantic-versioning-of-schemas)
- [MongoDB: Schema Versioning Pattern](https://www.mongodb.com/company/blog/building-with-patterns-the-schema-versioning-pattern)
- [Martin Kleppmann: Schema Evolution in Avro/Protobuf/Thrift](https://martin.kleppmann.com/2012/12/05/schema-evolution-in-avro-protocol-buffers-thrift.html)
- [Unity: FormerlySerializedAs Attribute](https://docs.unity3d.com/ScriptReference/Serialization.FormerlySerializedAsAttribute.html)

### Gizmo Implementation
- [ImGuizmo](https://github.com/CedricGuillemet/ImGuizmo) - Immediate mode gizmo library
- [tinygizmo](https://github.com/ddiakopoulos/tinygizmo) - Minimal public domain gizmo
- [Stride TransformationGizmo.cs](https://github.com/stride3d/stride/blob/master/sources/editor/Stride.Assets.Presentation/AssetEditors/Gizmos/TransformationGizmo.cs)
- [Godot 3D Gizmo Plugins](https://docs.godotengine.org/en/stable/tutorials/plugins/editor/3d_gizmos.html)

### Notable Implementations
- **Halo Forge** - Console-first runtime level editor
- **Fortnite Creative / UEFN** - Player-created experiences at scale
- **Dreams (Media Molecule)** - CSG-based creation for non-technical users
- **Roblox Studio** - Hierarchical scene with Lua scripting
- **LittleBigPlanet** - Popit-style radial menu creation

### Technical Resources
- [Unity Prefabs Manual](https://docs.unity3d.com/Manual/Prefabs.html)
- [Unreal Engine Sockets](https://dev.epicgames.com/documentation/en-us/unreal-engine/skeletal-mesh-sockets-in-unreal-engine)
- [3D AffordanceNet (CVPR 2021)](https://openaccess.thecvf.com/content/CVPR2021/papers/Deng_3D_AffordanceNet_A_Benchmark_for_Visual_Object_Affordance_Understanding_CVPR_2021_paper.pdf)
- [Open 3D Engine Scene Graph](https://docs.o3de.org/docs/user-guide/assets/scene-pipeline/scene-graph/)

---

## 15. Glossary

| Term | Definition |
|------|------------|
| **Scene Composer** | Bannou's runtime tool for arranging assets into hierarchical scenes |
| **Scene** | A YAML document storing hierarchical node structure |
| **Node** | An element in the scene hierarchy (group, mesh, marker, volume, emitter, reference) |
| **Prefab** | A reusable scene template |
| **Reference Node** | A node that embeds another scene |
| **Attachment Point** | A predefined location for attaching child objects |
| **Affordance** | What an object can do / how it can be interacted with |
| **AssetSlot** | A placeholder defining acceptable asset types for swapping |
| **Kit** | A set of modular pieces designed to work together |
| **Kit-Bashing** | Combining elements from multiple kits |
| **ISceneComposerBridge** | Interface for engine-specific integration |

---

*This document will evolve as implementation progresses. All architectural decisions require review against the Tenets.*
