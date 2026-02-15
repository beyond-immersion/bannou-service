# Scene Plugin Deep Dive

> **Plugin**: lib-scene
> **Schema**: schemas/scene-api.yaml
> **Version**: 1.0.0
> **State Stores**: scene-statestore (MySQL)

---

## Overview

Hierarchical composition storage (L4 GameFeatures) for game worlds. Stores scene documents as node trees with support for multiple node types (group, mesh, marker, volume, emitter, reference, custom), scene-to-scene references with recursive resolution, an exclusive checkout/commit/discard workflow, game-specific validation rules, full-text search, and version history. Does not compute world transforms or interpret node behavior at runtime -- consumers decide what nodes mean.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for scene indexes, content, checkout state, version history, validation rules, and secondary indexes |
| lib-state (`IDistributedLockProvider`) | Injected but not actively used in current implementation (optimistic concurrency via ETags instead) |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events, checkout/versioning events, instantiation events, reference integrity events, and error events |
| lib-messaging (`IEventConsumer`) | Registered in constructor for future event consumption (partial class extension point) |
| YamlDotNet | Serializing/deserializing scene documents to/from YAML format for storage |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-mapping | Consumes `scene.instantiated` and `scene.destroyed` events to manage spatial data |
| lib-actor | Consumes `scene.instantiated` events to spawn NPC actors at scene marker locations |

---

## State Storage

**Stores**: 1 state store (used with multiple key prefixes and typed store instances)

| Store | Backend | Purpose |
|-------|---------|---------|
| `scene-statestore` | MySQL | All scene data: indexes, content, checkout state, version history, validation rules, reference/asset tracking |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `scene:index:{sceneId}` | `SceneIndexEntry` | Scene metadata index (name, version, tags, nodeCount, checkout status) |
| `scene:content:{sceneId}` | `SceneContentEntry` | YAML-serialized scene document content |
| `scene:global-index` | `HashSet<Guid>` | Set of all scene IDs in the system |
| `scene:by-game:{gameId}` | `HashSet<Guid>` | Scene IDs partitioned by game |
| `scene:by-type:{gameId}:{sceneType}` | `HashSet<Guid>` | Scene IDs by game and scene type |
| `scene:references:{sceneId}` | `HashSet<Guid>` | Scene IDs that reference the given scene (reverse index) |
| `scene:assets:{assetId}` | `HashSet<Guid>` | Scene IDs that use the given asset (reverse index) |
| `scene:checkout:{sceneId}` | `CheckoutState` | Active checkout lock (token, editor, expiry, extension count) |
| `scene:validation:{gameId}:{sceneType}` | `List<ValidationRule>` | Registered validation rules per game+type |
| `scene:version-history:{sceneId}` | `List<VersionHistoryEntry>` | Version history entries ordered by creation time |
| `scene:checkout-ext:{sceneId}` | (unused) | Defined constant `SCENE_CHECKOUT_EXT_PREFIX` but never referenced in code |
| `scene:version-retention:{sceneId}` | (unused) | Defined constant `VERSION_RETENTION_PREFIX` but never referenced in code |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `scene.created` | `SceneCreatedEvent` | New scene document created |
| `scene.updated` | `SceneUpdatedEvent` | Scene document modified (version incremented) |
| `scene.deleted` | `SceneDeletedEvent` | Scene soft-deleted |
| `scene.instantiated` | `SceneInstantiatedEvent` | Scene declared instantiated in game world |
| `scene.destroyed` | `SceneDestroyedEvent` | Scene instance declared removed from game world |
| `scene.checked_out` | `SceneCheckedOutEvent` | Scene locked for editing |
| `scene.committed` | `SceneCommittedEvent` | Checkout changes committed, version bumped |
| `scene.checkout.discarded` | `SceneCheckoutDiscardedEvent` | Checkout released without saving |
| `scene.checkout.expired` | `SceneCheckoutExpiredEvent` | Checkout lock expired due to TTL (defined but not currently triggered by background process) |
| `scene.validation_rules.updated` | `SceneValidationRulesUpdatedEvent` | Validation rules registered/updated for gameId+sceneType |
| `scene.reference.broken` | `SceneReferenceBrokenEvent` | Referenced scene became unavailable (defined but not currently triggered) |

### Consumed Events

This plugin does not currently consume external events. The `IEventConsumer` is registered and `RegisterEventConsumers` is called from the constructor, but the partial method body is empty (extension point for future use).

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultCheckoutTtlMinutes` | `SCENE_DEFAULT_CHECKOUT_TTL_MINUTES` | `60` | Default checkout lock TTL in minutes |
| `CheckoutTtlBufferMinutes` | `SCENE_CHECKOUT_TTL_BUFFER_MINUTES` | `5` | Buffer time added to TTL for state store expiry |
| `MaxCheckoutExtensions` | `SCENE_MAX_CHECKOUT_EXTENSIONS` | `10` | Maximum heartbeat extensions per checkout |
| `DefaultMaxReferenceDepth` | `SCENE_DEFAULT_MAX_REFERENCE_DEPTH` | `3` | Default depth for reference resolution |
| `MaxReferenceDepthLimit` | `SCENE_MAX_REFERENCE_DEPTH_LIMIT` | `10` | Hard ceiling on reference resolution depth |
| `MaxVersionRetentionCount` | `SCENE_MAX_VERSION_RETENTION_COUNT` | `100` | Maximum version history entries retained per scene |
| `MaxNodeCount` | `SCENE_MAX_NODE_COUNT` | `10000` | Maximum nodes allowed in a single scene |
| `MaxTagsPerScene` | `SCENE_MAX_TAGS_PER_SCENE` | `50` | Maximum tags on a scene document |
| `MaxTagsPerNode` | `SCENE_MAX_TAGS_PER_NODE` | `20` | Maximum tags per node |
| `MaxListResults` | `SCENE_MAX_LIST_RESULTS` | `200` | List endpoint result cap |
| `MaxSearchResults` | `SCENE_MAX_SEARCH_RESULTS` | `100` | Search endpoint result cap |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<SceneService>` | Scoped | Structured logging |
| `SceneServiceConfiguration` | Singleton | All 12 config properties |
| `IStateStoreFactory` | Singleton | MySQL state store access (indexes, content, checkout, history, rules) |
| `IDistributedLockProvider` | Singleton | Distributed locks (injected but not actively used; ETags provide concurrency) |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event consumer registration (partial class extension point) |
| `ISceneValidationService` | Scoped | Scene structure validation and game rule application (extracted helper) |

Service lifetime is **Scoped** (per-request). No background services.

### Helper: `SceneValidationService`

Extracted from `SceneService` for testability. Handles:
- **Structural validation** (`ValidateStructure`): Checks sceneId non-empty, version pattern (MAJOR.MINOR.PATCH), root node existence, root has no parent, node count limit, refId uniqueness, refId pattern (`^[a-z][a-z0-9_]*$`), nodeId non-empty, and localTransform presence.
- **Game-specific rules** (`ApplyGameValidationRules`): Applies registered `ValidationRule` entries by type: `require_tag` (min/max count of nodes with tag), `forbid_tag` (no nodes may have tag), `require_node_type` (min count of nodes of type).
- **Result merging** (`MergeResults`): Combines errors/warnings from multiple validation passes.
- **Node collection** (`CollectAllNodes`): Flattens the hierarchy into a list for iteration.

### Internal Models

| Model | Purpose |
|-------|---------|
| `SceneIndexEntry` | Lightweight index: sceneId, assetId, gameId, sceneType, name, description, version, tags, nodeCount, timestamps, checkout status |
| `CheckoutState` | Lock state: sceneId, token, editorId, expiresAt, extensionCount |
| `SceneContentEntry` | YAML content wrapper: sceneId, version, content string, updatedAt unix timestamp |
| `VersionHistoryEntry` | Version record: version string, createdAt, createdBy |

---

## API Endpoints (Implementation Notes)

### Scene CRUD Operations (6 endpoints)

- **CreateScene** (`/scene/create`): Validates structure via `ISceneValidationService.ValidateStructure()`. Validates tag counts (scene-level and per-node) against configuration limits. Checks for existing scene with same ID (returns 409 Conflict). Sets timestamps and default version "1.0.0". Serializes scene to YAML via YamlDotNet and stores in `scene:content:{id}`. Creates `SceneIndexEntry`. Adds to global index. Updates game/type secondary indexes. Extracts and indexes scene references and asset references. Records initial version history entry. Publishes `scene.created`.

- **GetScene** (`/scene/get`): Loads index entry to find asset ID. Loads YAML content from state store and deserializes. If `resolveReferences=true`, recursively resolves reference nodes using `GetReferenceSceneId()` which checks the typed `ReferenceSceneId` field first, then falls back to `annotations.reference.sceneAssetId` for legacy data. Respects `maxReferenceDepth` capped by `MaxReferenceDepthLimit`. Detects circular references via visited set. Returns resolved references, unresolved references (with reason: not_found, circular_reference, depth_exceeded), and error messages.

- **ListScenes** (`/scene/list`): Filters candidates using secondary indexes (game index, type index, multi-type union). Falls back to global index scan when no filters provided. Applies additional in-memory filters: nameContains (case-insensitive), tags (ALL must match). Creates `SceneSummary` objects (excludes full node tree). Sorts by updatedAt descending. Applies pagination with offset/limit capped by `MaxListResults`.

- **UpdateScene** (`/scene/update`): Checks scene exists (404 if not). If scene is checked out and no token provided, returns 409. If token provided, validates against stored checkout state (403 on mismatch). Re-validates structure and tag counts. Preserves createdAt, sets updatedAt. Increments PATCH version via `IncrementPatchVersion()`. Stores updated YAML. Updates index entry and secondary indexes (handles reference/asset diff between old and new). Records version history entry with editor ID from checkout. Publishes `scene.updated`.

- **DeleteScene** (`/scene/delete`): Checks existence (404 if not). Checks reverse reference index -- blocks deletion if other scenes reference this one (returns 409 Conflict with referencing scene IDs). Loads scene for event data. Removes index entry, secondary indexes, and global index entry. Deletes version history. Publishes `scene.deleted`. Note: asset content in `scene:content:{id}` is NOT explicitly deleted (remains until TTL or manual cleanup).

- **DuplicateScene** (`/scene/duplicate`): Loads source scene (404 if not found). Creates new scene with fresh sceneId and all node IDs regenerated via `DuplicateNodeWithNewIds()`. Preserves refIds, names, transforms, assets, tags, annotations. Optionally overrides gameId and sceneType. Resets version to "1.0.0". Delegates to `CreateSceneAsync()` for storage, indexing, and event publishing.

### Instance Operations (2 endpoints)

- **InstantiateScene** (`/scene/instantiate`): NOTIFICATION endpoint (caller has already instantiated). Validates scene exists via index lookup (404 if not found). Builds `SceneInstantiatedEvent` with instance ID, scene asset ID, version, name, gameId, sceneType, regionId, and world transform (position/rotation/scale converted to event types). Publishes event. Returns instanceId, sceneVersion, and whether event was published. Does NOT track instance state -- purely event-driven.

- **DestroyInstance** (`/scene/destroy-instance`): Publishes `SceneDestroyedEvent` with instanceId, sceneAssetId (Guid.Empty if not provided), regionId (Guid.Empty if not provided), and caller metadata. Does NOT validate instance exists. Always returns destroyed=true. Purely event-driven notification for consumers (Mapping, Actor) to react.

### Versioning Operations (5 endpoints)

- **CheckoutScene** (`/scene/checkout`): Gets index with ETag for optimistic concurrency. If already checked out, checks if existing checkout is expired (allows takeover if expired). Loads scene from content store. Creates `CheckoutState` with random token (Guid "N" format), editor ID, expiry (now + ttlMinutes), extension count 0. Stores with TTL = ttlMinutes + 5 minutes (buffer). Updates index `IsCheckedOut=true` with ETag concurrency check. Publishes `scene.checked_out`. Returns token, scene, and expiresAt.

- **CommitScene** (`/scene/commit`): Validates checkout token (403 Forbidden on mismatch). Checks expiry (409 Conflict if expired). Gets index with ETag. Delegates scene update to `UpdateSceneAsync()` (which handles validation, version increment, and storage). Deletes checkout state. Clears `IsCheckedOut` on index with ETag concurrency. Publishes `scene.committed` with new version, previous version, committer, changes summary, and node count.

- **DiscardCheckout** (`/scene/discard`): Validates checkout token (403 Forbidden on mismatch). Does NOT check expiry (allows discard even after expiry). Deletes checkout state. Clears `IsCheckedOut` on index with ETag concurrency. Publishes `scene.checkout.discarded`. Scene remains at pre-checkout version.

- **HeartbeatCheckout** (`/scene/heartbeat`): Validates checkout token (403 Forbidden). Checks expiry (409 if expired). Checks extension limit (`MaxCheckoutExtensions`). If limit reached, returns extended=false with extensionsRemaining=0. Otherwise, sets new expiry to now + `DefaultCheckoutTtlMinutes`, increments extension count, re-saves with new TTL. Returns extended=true, new expiry, and remaining extensions.

- **GetSceneHistory** (`/scene/history`): Validates scene exists (404). Loads version history from `scene:version-history:{sceneId}`. Orders by createdAt descending. Limits to requested count. Returns sceneId, currentVersion (from index), and version list with version string, createdAt, createdBy.

### Validation Operations (2 endpoints)

- **RegisterValidationRules** (`/scene/register-validation-rules`): Stores rule list at `scene:validation:{gameId}:{sceneType}`. Replaces any existing rules for the combination. Publishes `scene.validation_rules.updated` with game ID, scene type, and rule count. Returns registered=true and rule count. Supported rule types: require_tag, forbid_tag, require_node_type, require_annotation (not yet implemented), custom_expression (not yet implemented).

- **GetValidationRules** (`/scene/get-validation-rules`): Simple lookup from state store. Returns empty list if no rules registered for the gameId+sceneType combination.

### Query Operations (3 endpoints)

- **SearchScenes** (`/scene/search`): Loads all scene IDs from global index. Iterates each, loading index entries. Applies game/type filters. Searches name, description, and tags for case-insensitive substring match (priority: name > description > tag). Returns `SearchResult` with match type and context. Pagination via offset/limit capped by `MaxSearchResults`. Note: this is a brute-force scan -- no full-text index.

- **FindReferences** (`/scene/find-references`): Loads reverse reference index (`scene:references:{sceneId}`). For each referencing scene, loads the full scene document and walks the node tree to find reference nodes pointing to the target. Returns `ReferenceInfo` with scene ID, scene name, node ID, node refId, and node name. Allows answering "which scenes embed this one?".

- **FindAssetUsage** (`/scene/find-asset-usage`): Loads asset usage index (`scene:assets:{assetId}`). For each using scene, loads the document and walks nodes to find those referencing the target asset. Applies optional gameId filter. Returns `AssetUsageInfo` with scene ID, scene name, node ID, node refId, node name, and node type. Allows answering "which scenes use this mesh/sound/particle asset?".

---

## Visual Aid

```
Scene Document Hierarchy
===========================

  Scene
  ├── sceneId: uuid
  ├── gameId: "arcadia"
  ├── sceneType: building | region | dungeon | ...
  ├── name: "Tavern Interior"
  ├── version: "1.2.5"
  ├── tags: [indoor, tavern, social]
  └── root: SceneNode (group)
       ├── nodeId: uuid
       ├── refId: "tavern_root"
       ├── nodeType: group
       ├── localTransform: {pos, rot, scale}
       └── children:
            ├── SceneNode (mesh)
            │    ├── refId: "floor_mesh"
            │    ├── nodeType: mesh
            │    ├── asset: {assetId: uuid}
            │    └── affordances: [{type: walkable}]
            │
            ├── SceneNode (marker)
            │    ├── refId: "npc_spawn_1"
            │    ├── nodeType: marker
            │    ├── markerType: npc_spawn
            │    └── tags: [barkeeper]
            │
            ├── SceneNode (volume)
            │    ├── refId: "ambient_zone"
            │    ├── nodeType: volume
            │    ├── volumeShape: box
            │    └── volumeSize: {x:10, y:3, z:8}
            │
            ├── SceneNode (reference)
            │    ├── refId: "back_room"
            │    ├── nodeType: reference
            │    ├── referenceSceneId: uuid (points to another Scene)
            │    └── annotations: {reference: {sceneAssetId: "..."}}
            │
            └── SceneNode (mesh + attachmentPoints)
                 ├── refId: "wall_section_1"
                 ├── nodeType: mesh
                 ├── asset: {assetId: uuid}
                 └── attachmentPoints:
                      ├── {name: "wall_hook_left", acceptsTags: [painting]}
                      └── {name: "shelf_1", acceptsTags: [decoration]}


Checkout/Commit Workflow
==========================

  Developer                    Scene Service                    State Store
     │                              │                              │
     ├── POST /scene/checkout ──────►                              │
     │                              ├── GetWithETag(index) ────────►
     │                              ◄── (indexEntry, etag) ────────┤
     │                              ├── Check IsCheckedOut         │
     │                              ├── LoadSceneAsset ────────────►
     │                              ◄── (scene YAML) ─────────────┤
     │                              ├── Generate token (Guid.N)    │
     │                              ├── Save CheckoutState ────────►
     │                              │   (TTL = ttlMinutes + 5)     │
     │                              ├── TrySave(index, etag) ──────►
     │                              ◄── (newEtag) ────────────────┤
     ◄── {token, scene, expiresAt} ─┤                              │
     │                              │                              │
     │  ... editing locally ...     │                              │
     │                              │                              │
     ├── POST /scene/heartbeat ─────►                              │
     │                              ├── Validate token + expiry    │
     │                              ├── Check extensionCount < max │
     │                              ├── Extend expiry, count++     │
     │                              ├── Re-save CheckoutState ─────►
     ◄── {extended, newExpiresAt} ──┤                              │
     │                              │                              │
     ├── POST /scene/commit ────────►                              │
     │                              ├── Validate token + expiry    │
     │                              ├── UpdateSceneAsync(scene)    │
     │                              │   ├── Validate structure     │
     │                              │   ├── Increment PATCH ver    │
     │                              │   ├── Store YAML ────────────►
     │                              │   └── Update indexes ────────►
     │                              ├── Delete CheckoutState ──────►
     │                              ├── Clear IsCheckedOut ─────────►
     │                              ├── Publish scene.committed    │
     ◄── {committed, newVersion} ───┤                              │


Reference Resolution
=======================

  GetScene(sceneId, resolveReferences=true, maxDepth=3)
       │
       ├── Load scene A
       ├── visited = {A}
       │
       ├── Walk nodes of A:
       │    └── Node(type=reference, annotations.reference.sceneAssetId = B)
       │         ├── depth=1, maxDepth=3 → proceed
       │         ├── B not in visited → load scene B
       │         ├── visited = {A, B}
       │         ├── resolved += {nodeId, refId, sceneId=B, scene=B, depth=1}
       │         │
       │         └── Walk nodes of B:
       │              └── Node(type=reference, sceneAssetId = C)
       │                   ├── depth=2, maxDepth=3 → proceed
       │                   ├── C not in visited → load scene C
       │                   ├── resolved += {sceneId=C, depth=2}
       │                   └── Walk nodes of C:
       │                        └── Node(type=reference, sceneAssetId = A)
       │                             ├── depth=3, maxDepth=3 → proceed
       │                             ├── A in visited → CIRCULAR!
       │                             └── unresolved += {reason=circular_reference, cyclePath=[A,B,C]}
       │
       └── Return: resolved=[B,C], unresolved=[A@depth3], errors=[...]


Secondary Index Architecture
===============================

  ┌─────────────────────────────────────────────────────────────────────┐
  │                          State Store                                 │
  │                                                                     │
  │  scene:global-index ──── {id1, id2, id3, ...}                       │
  │                                                                     │
  │  scene:by-game:arcadia ─── {id1, id2}                               │
  │  scene:by-game:fantasia ── {id3}                                    │
  │                                                                     │
  │  scene:by-type:arcadia:building ── {id1}                            │
  │  scene:by-type:arcadia:dungeon ─── {id2}                            │
  │                                                                     │
  │  scene:references:id1 ──── {id2, id3}  (scenes referencing id1)     │
  │  scene:assets:assetUuid ── {id1, id3}  (scenes using this asset)    │
  │                                                                     │
  │  scene:index:id1 ──── SceneIndexEntry (metadata)                    │
  │  scene:content:id1 ── SceneContentEntry (YAML)                      │
  │  scene:checkout:id1 ─ CheckoutState (lock)                          │
  │  scene:version-history:id1 ── [VersionHistoryEntry, ...]            │
  │  scene:validation:arcadia:building ── [ValidationRule, ...]         │
  └─────────────────────────────────────────────────────────────────────┘


Version History Retention
============================

  AddVersionHistoryEntry(sceneId, "1.0.5", editorId)
       │
       ├── Load existing history list
       ├── Append new entry {version, createdAt, createdBy}
       │
       ├── if historyEntries.Count > MaxVersionRetentionCount:
       │    └── Keep only newest MaxVersionRetentionCount entries
       │         (OrderByDescending(createdAt).Take(max))
       │
       └── Save trimmed history list


Optimistic Concurrency Pattern (Checkout)
============================================

  ┌──────────────────────────────────────────────────────────┐
  │  CheckoutScene:                                          │
  │                                                          │
  │  1. GetWithETag(indexKey) → (entry, etag)                │
  │  2. Validate not checked out / expired                   │
  │  3. Save CheckoutState (separate key, with TTL)          │
  │  4. Modify entry: IsCheckedOut = true                    │
  │  5. TrySave(indexKey, entry, etag) → newEtag             │
  │       │                                                  │
  │       ├── Success: concurrency safe                      │
  │       └── null: another request modified the index       │
  │            └── Rollback: delete CheckoutState            │
  │                 return 409 Conflict                      │
  └──────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

1. ~~**lib-asset integration**~~: **RESOLVED** (2026-01-31) - Removed dead config properties `AssetBucket` and `AssetContentType` per IMPLEMENTATION TENETS (T21 Configuration-First: no dead config). Scene content is stored directly in the state store via `scene:content:{id}` key. If lib-asset integration is needed in the future (e.g., for version content snapshots), the config properties can be re-added at that time.

2. **Version-specific retrieval**: `LoadSceneAssetAsync` accepts a `version` parameter but ignores it. Only the latest version's content is stored. Historical version content is not preserved -- only version metadata (version string, timestamp, editor) is retained. [Issue #187](https://github.com/beyond-immersion/bannou-service/issues/187)

3. **SceneCheckoutExpiredEvent**: The topic constant and event type exist, but no background process monitors and expires stale checkouts. Expiry is checked lazily when another user attempts checkout (can take over expired locks), but the event is never published.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/254 -->

4. **SceneReferenceBrokenEvent**: The topic and event type are defined in the events schema, but no code path currently publishes this event. It would need to be triggered when a scene is force-deleted despite references (currently blocked by the 409 Conflict check).
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/257 -->

5. ~~**require_annotation and custom_expression validation rules silently ignored**~~: **FIXED** (2026-02-08) - `ApplyValidationRule` now returns a `Warning`-severity `ValidationError` for unimplemented rule types (`RequireAnnotation`, `CustomExpression`) and unknown types (default case). Rules are no longer silently skipped. See [#310](https://github.com/beyond-immersion/bannou-service/issues/310).

6. ~~**referenceSceneId field on SceneNode**~~: **Implemented**. The `GetReferenceSceneId()` helper now uses the typed `ReferenceSceneId` field as the primary source, with `annotations.reference.sceneAssetId` as a fallback for backward compatibility with legacy scene data.

7. **node_name search match type**: The `SearchMatchType` enum includes `node_name`, but the search implementation only checks index-level fields (name, description, tags). Searching node names would require loading full scene content, which is not done for performance.

8. **Soft-delete recovery**: The schema describes soft-deletion with ~30 day recovery via lib-asset, but since content is stored in the state store rather than lib-asset, there is no recovery mechanism. Deletion removes the index entry and version history; content at `scene:content:{id}` is not explicitly deleted but becomes orphaned.

---

## Potential Extensions

1. **Background checkout expiry**: A periodic background service that scans CheckoutState entries, publishes `scene.checkout.expired` for stale locks, and clears the IsCheckedOut flag on indexes. Would prevent perpetually locked scenes when editors disconnect.

2. **Full-text search with Redis Search**: Replace the brute-force global index scan in `SearchScenesAsync` with Redis Search indexing on name, description, and tags fields for sub-millisecond query performance.

3. **Version content snapshots**: Store full YAML content per version (perhaps in lib-asset) to enable true version rollback and diff between versions, rather than only tracking version metadata.

4. **Scene composition graph**: Build a materialized graph of scene-to-scene references for efficient multi-hop queries (e.g., "find all scenes transitively reachable from this region").

5. **Custom expression validation**: Implement the `custom_expression` rule type using a safe expression evaluator (e.g., JSON path predicates over node annotations) for complex game-specific validation without code changes.

6. **Attachment point validation**: Validate that attached nodes match the `acceptsTags` constraints on attachment points during scene creation/update.

7. **Node-level search**: Index node names and refIds in a secondary search structure to support the `node_name` match type without loading full scene documents.

8. **Voxel node type**: A `voxel` node type referencing `.bvox` assets from the Voxel Builder SDK ([VOXEL-BUILDER-SDK.md](../planning/VOXEL-BUILDER-SDK.md)). Voxel nodes carry annotations for grid scale (`voxel.gridScale`), meshing strategy (`voxel.mesher`: greedy/culled/marching-cubes), and collision mesher. The SceneComposer SDK's engine bridge delegates voxel node rendering to the VoxelBuilder engine bridge. Key consumers: player housing (interactive voxel construction within housing gardens), dungeon cores (chamber reshaping), NPC builders (structure construction). Voxel assets stored in MinIO via the Asset service, with chunk-level delta saves through Save-Load for modified housing/dungeon grids.

9. **Item-scene node binding via annotations**: Convention for tracking item instance placement within scenes. Scene nodes carry `annotations.item.instanceId` referencing the placed Item instance; the Item instance carries `customStats` with `placed_scene_id` and `placed_node_id` back-references. Consistency enforced by ABML gardener behaviors using Scene's checkout/commit as the transaction boundary. Enables the housing garden pattern where furniture items move between Inventory containers and Scene node trees. See [Gardener: Housing Garden Pattern](GARDENER.md#housing-garden-pattern-no-plugin-required).

10. **Housing-specific Scene validation rules**: Authored validation rules (via existing `/scene/register-validation-rules`) for housing scenes: maximum furniture per room (require_tag with max count), forbidden node types per zone (forbid_tag for forge-in-bedroom), spatial overlap prevention (requires custom_expression implementation). These are data, not code -- game designers author rules that the Scene service validates on commit.

---

## Known Quirks & Caveats

### Bugs

No bugs identified.

### Intentional Quirks

1. **Expired checkout takeover**: If a checkout lock has expired, the next `CheckoutScene` call silently takes over the lock without publishing a `scene.checkout.expired` event. The previous editor loses their token.

2. **HeartbeatCheckout returns OK even at extension limit**: When `MaxCheckoutExtensions` is reached, the endpoint returns 200 OK with `extended=false` rather than an error status. Callers must check the `extended` field.

3. **DuplicateScene preserves refIds**: When duplicating, node IDs are regenerated but refIds are preserved. This means the duplicate has the same scripting references as the original.

4. **InstantiateScene is notification-only**: The endpoint does not persist instance state or track active instances. It validates the scene exists and publishes an event. Instance lifecycle is entirely consumer-managed.

5. **DeleteScene blocks on any reference**: Even if the referencing scene is itself deleted (orphaned reference index entry), deletion is still blocked. Index cleanup for references is not transactional.

6. **Patch version only**: UpdateScene and CommitScene always increment the PATCH version. There is no mechanism to increment MAJOR or MINOR versions. Callers who need semantic versioning must set the version manually before the update.

### Design Considerations

1. **Global index unbounded growth**: The `scene:global-index` key holds ALL scene IDs in a single `HashSet<Guid>`. At scale (millions of scenes), this set becomes a memory and serialization bottleneck. Consider partitioned indexes or cursor-based iteration.

2. ~~**N+1 query pattern in ListScenes**~~: **FIXED** (2026-01-31) - ListScenes now uses `GetBulkAsync` for single database round-trip when loading index entries. Filtering still happens in-memory after bulk load.

3. ~~**N+1 in SearchScenes**~~: **FIXED** (2026-01-31) - SearchScenes now uses `GetBulkAsync` for single database round-trip. The global index scan remains, but individual index entry loading is now bulk.

4. **Secondary index race conditions**: Index updates (game, type, reference, asset) are not atomic with the primary index write. A crash between primary save and secondary index update leaves indexes inconsistent. There is no reconciliation mechanism.

5. **YAML serialization performance**: Every scene read deserializes YAML, and every write serializes to YAML. For large scenes (10,000 nodes), YamlDotNet serialization may be a latency bottleneck. JSON would be significantly faster.

6. **No content versioning**: Only one version of scene content is stored. The version history tracks metadata (version string, timestamp, editor) but not the actual content at each version. Version-specific retrieval in GetScene is a no-op.

7. **Optimistic concurrency only on checkout**: The ETag-based concurrency check is used for checkout/commit operations on the index entry, but not for other updates. Two concurrent UpdateScene calls (without checkout) can overwrite each other's changes.

8. **Asset/reference index staleness**: If a scene is updated and the YAML deserialization produces different reference/asset sets, the index diff logic handles adds and removes. However, if deserialization fails or produces different results than what was stored, indexes become stale with no self-healing mechanism.

9. **No pagination in FindReferences/FindAssetUsage**: These endpoints return all results without pagination. A heavily-referenced scene or widely-used asset could produce unbounded response sizes. Note: Index entry loading was optimized to use `GetBulkAsync` (2026-01-31), but scene content loading for node traversal remains sequential.

10. **Unused key prefix constants**: The service defines `SCENE_CHECKOUT_EXT_PREFIX` ("scene:checkout-ext:") and `VERSION_RETENTION_PREFIX` ("scene:version-retention:") but never references them. These appear to be dead code from planned features that were never implemented or were refactored away.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Completed

- **2026-02-08**: Fixed unimplemented validation rule types silently passing ([#310](https://github.com/beyond-immersion/bannou-service/issues/310)). `RequireAnnotation`, `CustomExpression`, and any future enum values now return `Warning`-severity `ValidationError` instead of being silently skipped. Callers can see which rules were not applied.

- **2026-01-31**: Removed dead config properties `AssetBucket` and `AssetContentType` from scene-configuration.yaml per IMPLEMENTATION TENETS (T21 Configuration-First). These were never used in SceneService.cs - scene content is stored directly in state store. Updated tests accordingly.

- **2026-01-31**: N+1 bulk loading optimization - Replaced N+1 `GetAsync` calls with `GetBulkAsync` in `ListScenesAsync`, `SearchScenesAsync`, `FindReferencesAsync`, and `FindAssetUsageAsync`. Index entry loading now uses single database round-trips. See Issue #168 for `IStateStore` bulk operations.

