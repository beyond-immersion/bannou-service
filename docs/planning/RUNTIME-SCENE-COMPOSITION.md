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

**Location**: `/Bannou.Client.SDK/` and `/Bannou.SDK/`

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

**Enhance Existing**: `RemoteAssetCache<T>` in `/Bannou.Client.SDK/`

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

## 9. Open Questions

1. **Scene Format Versioning**
   - How do we handle schema changes without breaking existing scenes?
   - Proposal: YAML `$schema` header with migration scripts

2. **Collaborative Editing**
   - Current: Lock-based (one editor at a time)
   - Future: OT/CRDT for real-time collaboration?

3. **Asset Validation**
   - Should AssetSlot enforce that only matching assets can be bound?
   - At composition time vs at commit time?

4. **Gizmo Rendering**
   - Let each engine use its native gizmo system?
   - Or provide cross-engine gizmo rendering?

5. **UI Framework**
   - Each engine has its own UI system
   - Should SDK provide abstract UI model that engines implement?

---

## 10. References

### GDC Talks
- [Skyrim's Modular Level Design (GDC 2013)](http://blog.joelburgess.com/2013/04/skyrims-modular-level-design-gdc-2013.html)
- [Keeping Level Designers in the Zone (GDC 2016)](https://gdcvault.com/browse/gdc-16/play/1023235)
- [The Architecture of Dreams - Media Molecule (GDC 2020)](https://www.gamedeveloper.com/programming/see-how-media-molecule-architected-the-code-i-dreams-i-are-made-of-at-gdc-)
- [Creating a Tools Pipeline for Horizon: Zero Dawn](https://www.gdcvault.com/play/1024124/Creating-a-Tools-Pipeline-for)

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

## 11. Glossary

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
