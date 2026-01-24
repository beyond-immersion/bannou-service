# Scene Plugin Deep Dive

> **Plugin**: lib-scene
> **Schema**: schemas/scene-api.yaml
> **Version**: 1.0.0
> **State Stores**: scene-statestore (MySQL)

---

## Overview

Hierarchical composition storage for game worlds. Stores and retrieves scene documents as YAML-serialized node trees with support for multiple node types (group, mesh, marker, volume, emitter, reference, custom), scene-to-scene references with recursive resolution, an exclusive checkout/commit/discard workflow with heartbeat-extended TTL locks, game-specific validation rules registered per gameId+sceneType, full-text search across names/descriptions/tags, reverse reference and asset usage tracking via secondary indexes, scene duplication with regenerated node IDs, and version history with configurable retention. Does NOT compute world transforms, determine affordances, push data to other services, or interpret node behavior at runtime -- consumers decide what nodes mean. Scene content is serialized to YAML using YamlDotNet and stored in a single MySQL-backed state store under multiple key prefixes. The Scene Composer SDK extensions (attachment points, affordances, asset slots, marker types, volume shapes) are stored as node properties but not interpreted by the service.

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
| `scene:global-index` | `HashSet<string>` | Set of all scene IDs in the system |
| `scene:by-game:{gameId}` | `HashSet<string>` | Scene IDs partitioned by game |
| `scene:by-type:{gameId}:{sceneType}` | `HashSet<string>` | Scene IDs by game and scene type |
| `scene:references:{sceneId}` | `HashSet<string>` | Scene IDs that reference the given scene (reverse index) |
| `scene:assets:{assetId}` | `HashSet<string>` | Scene IDs that use the given asset (reverse index) |
| `scene:checkout:{sceneId}` | `CheckoutState` | Active checkout lock (token, editor, expiry, extension count) |
| `scene:validation:{gameId}:{sceneType}` | `List<ValidationRule>` | Registered validation rules per game+type |
| `scene:version-history:{sceneId}` | `List<VersionHistoryEntry>` | Version history entries ordered by creation time |

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
| `AssetBucket` | `SCENE_ASSET_BUCKET` | `"scenes"` | lib-asset bucket name (not currently used; content stored in state store) |
| `AssetContentType` | `SCENE_ASSET_CONTENT_TYPE` | `"application/x-bannou-scene+yaml"` | Content type identifier (not currently used; content stored in state store) |
| `DefaultCheckoutTtlMinutes` | `SCENE_DEFAULT_CHECKOUT_TTL_MINUTES` | `60` | Default checkout lock TTL in minutes |
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

- **GetScene** (`/scene/get`): Loads index entry to find asset ID. Loads YAML content from state store and deserializes. If `resolveReferences=true`, recursively resolves reference nodes (nodeType=Reference with annotations containing `reference.sceneAssetId`). Respects `maxReferenceDepth` capped by `MaxReferenceDepthLimit`. Detects circular references via visited set. Returns resolved references, unresolved references (with reason: not_found, circular_reference, depth_exceeded), and error messages.

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

1. **lib-asset integration**: The configuration has `AssetBucket` and `AssetContentType` properties suggesting scene content should be stored in lib-asset. The actual implementation stores YAML content directly in the state store (`scene:content:{id}` key). The asset integration is stubbed out.

2. **Version-specific retrieval**: `LoadSceneAssetAsync` accepts a `version` parameter but ignores it. Only the latest version's content is stored. Historical version content is not preserved -- only version metadata (version string, timestamp, editor) is retained.

3. **SceneCheckoutExpiredEvent**: The topic constant and event type exist, but no background process monitors and expires stale checkouts. Expiry is checked lazily when another user attempts checkout (can take over expired locks), but the event is never published.

4. **SceneReferenceBrokenEvent**: The topic and event type are defined in the events schema, but no code path currently publishes this event. It would need to be triggered when a scene is force-deleted despite references (currently blocked by the 409 Conflict check).

5. **require_annotation and custom_expression validation rules**: The `ValidationRuleType` enum includes these values, but `ApplyValidationRule` in `SceneValidationService` only handles `require_tag`, `forbid_tag`, and `require_node_type`. The other types silently pass.

6. **referenceSceneId field on SceneNode**: The schema defines a `referenceSceneId` field on nodes for reference type, but the implementation reads reference scene IDs from `annotations.reference.sceneAssetId` instead. The generated model field appears unused in business logic.

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

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Expired checkout takeover**: If a checkout lock has expired, the next `CheckoutScene` call silently takes over the lock without publishing a `scene.checkout.expired` event. The previous editor loses their token.

2. **UpdateScene allows checkout bypass**: If a scene is checked out but the caller provides the correct checkout token, UpdateScene succeeds. This is the mechanism `CommitScene` uses internally.

3. **HeartbeatCheckout returns OK even at extension limit**: When `MaxCheckoutExtensions` is reached, the endpoint returns 200 OK with `extended=false` rather than an error status. Callers must check the `extended` field.

4. **DiscardCheckout does not check expiry**: Unlike CommitScene, DiscardCheckout allows discarding even after the lock has expired. This is intentional -- releasing a stale lock is always safe since no changes are persisted.

5. **DuplicateScene preserves refIds**: When duplicating, node IDs are regenerated but refIds are preserved. This means the duplicate has the same scripting references as the original, which is correct for scene templates.

6. **InstantiateScene is notification-only**: The endpoint does not persist instance state or track active instances. It validates the scene exists and publishes an event. Instance lifecycle is entirely consumer-managed.

7. **DeleteScene blocks on any reference**: Even if the referencing scene is itself deleted (orphaned reference index entry), deletion is still blocked. Index cleanup for references is not transactional.

8. **Patch version only**: UpdateScene and CommitScene always increment the PATCH version. There is no mechanism to increment MAJOR or MINOR versions. Callers who need semantic versioning must set the version manually before the update.

### Design Considerations (Requires Planning)

1. **Global index unbounded growth**: The `scene:global-index` key holds ALL scene IDs in a single `HashSet<string>`. At scale (millions of scenes), this set becomes a memory and serialization bottleneck. Consider partitioned indexes or cursor-based iteration.

2. **N+1 query pattern in ListScenes**: After resolving candidate IDs via secondary indexes, each scene's index entry is loaded individually. A page of 200 results generates 200 state store reads.

3. **N+1 in SearchScenes**: Even worse than ListScenes -- loads ALL scene IDs globally and iterates one-by-one. A system with 100,000 scenes requires 100,000 index reads per search.

4. **Secondary index race conditions**: Index updates (game, type, reference, asset) are not atomic with the primary index write. A crash between primary save and secondary index update leaves indexes inconsistent. There is no reconciliation mechanism.

5. **YAML serialization performance**: Every scene read deserializes YAML, and every write serializes to YAML. For large scenes (10,000 nodes), YamlDotNet serialization may be a latency bottleneck. JSON would be significantly faster.

6. **No content versioning**: Only one version of scene content is stored. The version history tracks metadata (version string, timestamp, editor) but not the actual content at each version. Version-specific retrieval in GetScene is a no-op.

7. **Optimistic concurrency only on checkout**: The ETag-based concurrency check is used for checkout/commit operations on the index entry, but not for other updates. Two concurrent UpdateScene calls (without checkout) can overwrite each other's changes.

8. **Asset/reference index staleness**: If a scene is updated and the YAML deserialization produces different reference/asset sets, the index diff logic handles adds and removes. However, if deserialization fails or produces different results than what was stored, indexes become stale with no self-healing mechanism.

9. **No pagination in FindReferences/FindAssetUsage**: These endpoints return all results without pagination. A heavily-referenced scene or widely-used asset could produce unbounded response sizes.
