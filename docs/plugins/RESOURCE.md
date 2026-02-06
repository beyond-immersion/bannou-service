# Resource Plugin Deep Dive

> **Plugin**: lib-resource
> **Layer**: L1 (App Foundation)
> **Schema**: schemas/resource-api.yaml
> **Version**: 1.0.0
> **State Stores**: resource-refcounts (Redis), resource-cleanup (Redis), resource-grace (Redis), resource-compress (Redis), resource-archives (MySQL), resource-snapshots (Redis)

---

## Overview

Resource reference tracking, lifecycle management, and hierarchical compression for foundational resources. Provides three core capabilities:

1. **Reference Tracking**: Enables foundational services (L2) to safely delete resources by tracking references from higher-layer consumers (L3/L4) without violating the service hierarchy. Higher-layer services publish reference events when they create/delete references to foundational resources.

2. **Cleanup Coordination**: Maintains reference counts using Redis sets and coordinates cleanup callbacks when resources are deleted. Supports CASCADE, RESTRICT, and DETACH deletion policies.

3. **Hierarchical Compression**: Centralizes compression of resources and their dependents. Higher-layer services register compression callbacks that gather data for archival. The Resource service orchestrates callback execution, bundles data into unified archives stored in MySQL, and supports full decompression for data recovery.

**Key Design Principle**: lib-resource (L1) uses opaque string identifiers for `resourceType` and `sourceType`. It does NOT enumerate or validate these against any service registry - that would create implicit coupling to higher layers. The strings are just identifiers that consumers self-report.

**Why L1**: Any layer can depend on L1. Resources being tracked are at L2 or higher, and their consumers are at L3/L4. By placing this service at L1, all layers can use it without hierarchy violations.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Get Redis state stores for reference sets, cleanup callbacks, and grace periods |
| lib-state (`ICacheableStateStore<T>`) | Set operations (`AddToSetAsync`, `RemoveFromSetAsync`, `GetSetAsync`, `SetCountAsync`, `DeleteSetAsync`) for atomic reference tracking |
| lib-state (`IDistributedLockProvider`) | Distributed locks during cleanup execution to prevent concurrent cleanup |
| lib-messaging (`IMessageBus`) | Publishing `resource.grace-period.started` and `resource.cleanup.callback-failed` events |
| lib-mesh (`IServiceNavigator`) | Executing cleanup callbacks via `ExecutePreboundApiBatchAsync` |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Queries `/resource/check` for L4 references in `CheckCharacterReferencesAsync` |
| lib-actor | Publishes `resource.reference.registered/unregistered` in SpawnActorAsync/StopActorAsync; cleanup via `/actor/cleanup-by-character` |
| lib-character-encounter | Publishes reference events in RecordEncounterAsync/DeleteEncounterAsync; cleanup via `/character-encounter/delete-by-character` |
| lib-character-history | Publishes reference events for participations and backstory; cleanup via `/character-history/delete-all` |
| lib-character-personality | Publishes reference events for personality/combat prefs; cleanup via `/character-personality/cleanup-by-character` |
| *lib-scene (not yet integrated)* | *Planned: would publish reference events for scene-to-character references; would register cleanup callback* |

---

## State Storage

**Stores**: 6 state stores (5 Redis-backed, 1 MySQL-backed)

| Store | Backend | Schema Prefix | Purpose |
|-------|---------|---------------|---------|
| `resource-refcounts` | Redis | `resource:ref` | Reference tracking via sets |
| `resource-cleanup` | Redis | `resource:cleanup` | Cleanup callback definitions |
| `resource-grace` | Redis | `resource:grace` | Grace period timestamps |
| `resource-compress` | Redis | `resource:compress` | Compression callback definitions and indexes |
| `resource-archives` | MySQL | N/A | Compressed archive bundles (durable storage) |
| `resource-snapshots` | Redis | `resource:snapshot` | Ephemeral snapshots of living resources (TTL-based auto-expiry) |

**Note**: The "Schema Prefix" column shows the prefix defined in `state-stores.yaml`. The actual Redis key is `{prefix}:{key}` where key patterns are shown below.

**Reference & Cleanup Key Patterns** (resource-refcounts, resource-cleanup, resource-grace stores):

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{resourceType}:{resourceId}:sources` | Set of `ResourceReferenceEntry` | All entities referencing this resource |
| `{resourceType}:{resourceId}:grace` | `GracePeriodRecord` | When refcount became zero |
| `callback:{resourceType}:{sourceType}` | `CleanupCallbackDefinition` | Cleanup endpoint for a source type |
| `callback-index:{resourceType}` | Set of `string` | Source types with registered callbacks (for enumeration without KEYS/SCAN) |
| `callback-resource-types` | Set of `string` | Master index of all resource types with callbacks (for listing all without KEYS scan) |

**Compression Key Patterns** (resource-compress, resource-archives stores):

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `compress-callback:{resourceType}:{sourceType}` | `CompressCallbackDefinition` | Compression endpoint for a source type |
| `compress-callback-index:{resourceType}` | Set of `string` | Source types with registered compression callbacks |
| `compress-callback-resource-types` | Set of `string` | Master index of resource types with compression callbacks |
| `archive-version:{resourceType}:{resourceId}` | `int` (counter) | Current archive version (atomic increment) |
| `archive:{resourceType}:{resourceId}:{version}` | `ResourceArchiveModel` (MySQL) | Bundled compressed archive data |

**Snapshot Key Patterns** (resource-snapshots store):

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `snap:{snapshotId}` | `ResourceSnapshotModel` | Ephemeral snapshot with TTL (auto-expired by Redis) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `resource.grace-period.started` | `ResourceGracePeriodStartedEvent` | Resource refcount reaches zero via `UnregisterReferenceAsync` |
| `resource.cleanup.callback-failed` | `ResourceCleanupCallbackFailedEvent` | Cleanup callback returns non-2xx during `ExecuteCleanupAsync` |
| `resource.compressed` | `ResourceCompressedEvent` | Compression completes successfully via `ExecuteCompressAsync` |
| `resource.compress.callback-failed` | `ResourceCompressCallbackFailedEvent` | Compression callback fails during `ExecuteCompressAsync` |
| `resource.decompressed` | `ResourceDecompressedEvent` | Decompression completes successfully via `ExecuteDecompressAsync` |
| `resource.snapshot.created` | `ResourceSnapshotCreatedEvent` | Ephemeral snapshot created via `ExecuteSnapshotAsync` |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `resource.reference.registered` | `ResourceReferenceRegisteredEvent` | `HandleReferenceRegisteredAsync` - delegates to `RegisterReferenceAsync` |
| `resource.reference.unregistered` | `ResourceReferenceUnregisteredEvent` | `HandleReferenceUnregisteredAsync` - delegates to `UnregisterReferenceAsync` |

---

## Configuration

### Cleanup Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultGracePeriodSeconds` | `RESOURCE_DEFAULT_GRACE_PERIOD_SECONDS` | 604800 (7 days) | Grace period before cleanup eligible |
| `CleanupLockExpirySeconds` | `RESOURCE_CLEANUP_LOCK_EXPIRY_SECONDS` | 300 | Distributed lock timeout during cleanup |
| `DefaultCleanupPolicy` | `RESOURCE_DEFAULT_CLEANUP_POLICY` | BEST_EFFORT | Policy when not specified per-request |
| `CleanupCallbackTimeoutSeconds` | `RESOURCE_CLEANUP_CALLBACK_TIMEOUT_SECONDS` | 30 | Timeout for cleanup callback execution |

### Compression Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultCompressionPolicy` | `RESOURCE_DEFAULT_COMPRESSION_POLICY` | ALL_REQUIRED | Default compression policy when not specified per-request |
| `CompressionCallbackTimeoutSeconds` | `RESOURCE_COMPRESSION_CALLBACK_TIMEOUT_SECONDS` | 60 | Timeout for each compression callback execution |
| `CompressionLockExpirySeconds` | `RESOURCE_COMPRESSION_LOCK_EXPIRY_SECONDS` | 600 | Distributed lock timeout during compression |

### Snapshot Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `SnapshotDefaultTtlSeconds` | `RESOURCE_SNAPSHOT_DEFAULT_TTL_SECONDS` | 3600 (1 hour) | Default TTL when not specified in request |
| `SnapshotMinTtlSeconds` | `RESOURCE_SNAPSHOT_MIN_TTL_SECONDS` | 60 (1 minute) | Minimum allowed TTL (clamped) |
| `SnapshotMaxTtlSeconds` | `RESOURCE_SNAPSHOT_MAX_TTL_SECONDS` | 86400 (24 hours) | Maximum allowed TTL (clamped) |

All configuration properties are verified as used in `ResourceService.cs`.

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<ResourceService>` | Structured logging |
| `ResourceServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for all six stores |
| `IDistributedLockProvider` | Acquiring cleanup locks |
| `IMessageBus` | Publishing events |
| `IServiceNavigator` | Executing cleanup callbacks |
| `IEventConsumer` | Registering event subscription handlers |
| `IEnumerable<ISeededResourceProvider>` | Discovered seeded resource providers from DI |

**Shared Provider Interfaces** (defined in `bannou-service/Providers/`):
| Type | Role |
|------|------|
| `ISeededResourceProvider` | Interface for plugins to provide seeded resources |
| `SeededResource` | Record type for seeded resource with identifier, content, metadata |
| `EmbeddedResourceProvider` | Base class for loading from assembly embedded resources |

**Internal Types** (defined in ResourceService.cs):
| Type | Role |
|------|------|
| `ResourceReferenceEntry` | Set member for reference tracking; equality based on `SourceType` + `SourceId` |
| `GracePeriodRecord` | Records when refcount became zero |
| `CleanupCallbackDefinition` | Stores callback registration (service, endpoint, template, onDeleteAction) |
| `ResourceSnapshotModel` | Ephemeral snapshot with TTL (stored in Redis, mirrors archive structure) |

---

## API Endpoints (Implementation Notes)

### Reference Management

| Endpoint | Notes |
|----------|-------|
| `POST /resource/register` | Uses `AddToSetAsync` for atomic add; clears grace period if new reference added |
| `POST /resource/unregister` | Uses `RemoveFromSetAsync`; publishes `grace-period.started` event when refcount reaches zero |
| `POST /resource/check` | Derives refcount from `SetCountAsync`; computes `isCleanupEligible` from grace period timestamp |
| `POST /resource/list` | Returns all set members; supports `filterSourceType` and `limit` |

### Cleanup Management

| Endpoint | Notes |
|----------|-------|
| `POST /resource/cleanup/define` | Upserts callback definition with `onDeleteAction`; maintains `callback-index:{resourceType}` set and `callback-resource-types` master index; `serviceName` defaults to `sourceType` if not specified |
| `POST /resource/cleanup/execute` | Full cleanup flow: RESTRICT check → pre-check → lock → re-validate → execute CASCADE/DETACH callbacks → clear state. Supports `dryRun` flag to preview without executing. |
| `POST /resource/cleanup/list` | Lists registered cleanup callbacks; filter by `resourceType` and/or `sourceType`; uses master index to avoid KEYS scan |
| `POST /resource/cleanup/remove` | Removes a cleanup callback registration; idempotent (returns `wasRegistered: false` if not found); cleans up indexes |

**OnDeleteAction Behavior** (per-callback, configured via `/resource/cleanup/define`):

| Action | Behavior |
|--------|----------|
| `CASCADE` (default) | Execute cleanup callback to delete dependent entities |
| `RESTRICT` | Block resource deletion if references of this sourceType exist |
| `DETACH` | Execute cleanup callback (consumer implements null-out/detach logic) |

**Cleanup Execution Flow**:
1. Get all callbacks and identify RESTRICT vs CASCADE/DETACH callbacks
2. **DryRun check**: If `dryRun: true`, return preview of what callbacks would execute without acquiring lock or executing. Returns `Success = false` if any RESTRICT callbacks exist.
3. **RESTRICT check**: If any active references have RESTRICT callbacks, return failure immediately with `"Blocked by RESTRICT policy from: {sourceTypes}"`
4. Pre-check refcount and grace period (without lock)
5. If blocked by non-RESTRICT reasons (unhandled refs, grace period), return early with reason
6. Acquire distributed lock on `cleanup:{resourceType}:{resourceId}`
7. Re-validate refcount under lock (race protection)
8. Execute only CASCADE and DETACH callbacks via `IServiceNavigator.ExecutePreboundApiBatchAsync` in parallel with configured timeout (`CleanupCallbackTimeoutSeconds`)
9. Per cleanup policy: abort or continue on failures
10. Delete grace period record and reference set
11. Release lock

### Compression Management

| Endpoint | Notes |
|----------|-------|
| `POST /resource/compress/define` | Registers compression callback for a resource type; maintains indexes for enumeration |
| `POST /resource/compress/execute` | Orchestrates compression: gather data from callbacks → bundle into archive → store in MySQL → optionally delete source data |
| `POST /resource/decompress/execute` | Restores data from archive by invoking decompression callbacks |
| `POST /resource/compress/list` | Lists registered compression callbacks with filtering |
| `POST /resource/archive/get` | Retrieves compressed archive by resourceId or specific version |

**CompressionPolicy** (per-request or default from config):

| Policy | Behavior |
|--------|----------|
| `ALL_REQUIRED` (default) | Abort compression if any callback fails |
| `BEST_EFFORT` | Create archive even if some callbacks fail |

**Compression Execution Flow**:
1. Get all compression callbacks for resourceType, sorted by priority (lower = earlier)
2. If no callbacks registered, return `Success = false` with reason "No callbacks registered"
3. **DryRun check**: If `dryRun: true`, return preview of what would execute without acquiring lock
4. Acquire distributed lock on `compress:{resourceType}:{resourceId}`
5. For each callback (in priority order):
   a. Build PreboundApiDefinition from callback registration
   b. Execute via `IServiceNavigator.ExecutePreboundApiAsync` with configured timeout
   c. On success: GZip compress response, add to archive entries
   d. On failure: if `ALL_REQUIRED`, abort immediately; else continue and log
   e. Publish `resource.compress.callback-failed` event on failure
6. Create `ResourceArchiveModel` with all collected entries
7. Atomically increment archive version via `ICacheableStateStore.IncrementAsync`
8. Save archive to MySQL store with key `archive:{resourceType}:{resourceId}:{version}`
9. If `deleteSourceData: true`, execute cleanup callbacks via `ExecuteCleanupAsync`
10. Publish `resource.compressed` event
11. Release lock, return result

**Decompression Execution Flow**:
1. Retrieve archive from MySQL (by version or latest)
2. If archive not found, return `Success = false`
3. For each archive entry, invoke the registered decompression callback
4. Publish `resource.decompressed` event on success

### Snapshot Management (Living Entity Snapshots)

| Endpoint | Notes |
|----------|-------|
| `POST /resource/snapshot/execute` | Creates ephemeral snapshot using compression callbacks, stores in Redis with TTL |
| `POST /resource/snapshot/get` | Retrieves snapshot by ID; returns 404 if expired or not found |

**Key Differences from Compression**:
- Stores in Redis (ephemeral) with TTL, not MySQL (permanent)
- Never deletes source data (non-destructive)
- Publishes `resource.snapshot.created` event (not `resource.compressed`)
- Snapshot auto-expires after TTL (default 1 hour, max 24 hours)

**Use Cases**:
- Storyline Composer needs compressed data from living entities to seed emergent narratives
- Actor behaviors can capture character state via ABML `service_call`
- Analytics can capture living entity snapshots for point-in-time analysis

**Snapshot Execution Flow**:
1. Get all compression callbacks for resourceType (same as compression)
2. If `dryRun: true`, return preview without executing
3. Execute each callback to gather data (same as compression)
4. Bundle responses into snapshot model
5. Store in Redis with TTL via `SaveAsync` with `StateOptions { Ttl = ttlSeconds }`
6. Publish `resource.snapshot.created` event
7. Return snapshotId for later retrieval

### Seeded Resource Management

| Endpoint | Notes |
|----------|-------|
| `POST /resource/seeded/list` | Lists all available seeded resources; optionally filter by `resourceType` |
| `POST /resource/seeded/get` | Retrieves seeded resource content by type and identifier; returns base64-encoded content |

**Purpose**: Provides a consistent pattern for plugins to load embedded/static resources (ABML behaviors, templates, configuration data) without each plugin reinventing the wheel.

**Key Design**:
- Uses `ISeededResourceProvider` interface (defined in `bannou-service/Providers/`)
- Providers discovered via DI collection (`IEnumerable<ISeededResourceProvider>`)
- Content returned as base64-encoded bytes with MIME type metadata
- Seeded resources are **read-only factory defaults** - consumers may copy to state stores for runtime modification

**Integration Pattern for Higher-Layer Plugins**:

1. **Implement provider** (use `EmbeddedResourceProvider` base class for assembly-embedded resources):
   ```csharp
   public class BehaviorSeededProvider : EmbeddedResourceProvider
   {
       public override string ResourceType => "behavior";
       public override string ContentType => "application/yaml";
       protected override Assembly ResourceAssembly => typeof(BehaviorSeededProvider).Assembly;
       protected override string ResourcePrefix => "BeyondImmersion.LibActor.Behaviors.";
   }
   ```

2. **Register in plugin DI** (in `ConfigureServices`):
   ```csharp
   services.AddSingleton<ISeededResourceProvider, BehaviorSeededProvider>();
   ```

3. **Query via API** (from any service or ABML behavior):
   ```
   POST /resource/seeded/list
   { "resourceType": "behavior" }

   POST /resource/seeded/get
   { "resourceType": "behavior", "identifier": "idle" }
   ```

---

## Visual Aid

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Resource State Store Layout                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  resource-refcounts (Redis)                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: {resourceType}:{resourceId}:sources                            │   │
│  │ Type: Redis Set                                                     │   │
│  │ Members: [                                                          │   │
│  │   { SourceType: "actor", SourceId: "abc-123", RegisteredAt: "..." },│   │
│  │   { SourceType: "scene", SourceId: "def-456", RegisteredAt: "..." } │   │
│  │ ]                                                                   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  resource-grace (Redis)                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: {resourceType}:{resourceId}:grace                              │   │
│  │ Type: JSON object                                                   │   │
│  │ Value: { ResourceType, ResourceId, ZeroTimestamp }                  │   │
│  │ Created: When refcount becomes zero                                 │   │
│  │ Deleted: When cleanup executes OR new reference registered          │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  resource-cleanup (Redis)                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: callback:{resourceType}:{sourceType}                           │   │
│  │ Type: JSON object                                                   │   │
│  │ Value: { ServiceName, CallbackEndpoint, PayloadTemplate, ... }      │   │
│  ├─────────────────────────────────────────────────────────────────────┤   │
│  │ Key: callback-index:{resourceType}                                  │   │
│  │ Type: Redis Set (for enumeration without KEYS/SCAN)                 │   │
│  │ Members: [ "actor", "scene", "encounter" ]                          │   │
│  ├─────────────────────────────────────────────────────────────────────┤   │
│  │ Key: callback-resource-types                                        │   │
│  │ Type: Redis Set (master index of all resource types with callbacks) │   │
│  │ Members: [ "character", "realm", "location" ]                       │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  resource-compress (Redis)                                                  │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: compress-callback:{resourceType}:{sourceType}                  │   │
│  │ Type: JSON object                                                   │   │
│  │ Value: { ServiceName, CompressEndpoint, DecompressEndpoint, ... }   │   │
│  ├─────────────────────────────────────────────────────────────────────┤   │
│  │ Key: compress-callback-index:{resourceType}                         │   │
│  │ Type: Redis Set                                                     │   │
│  │ Members: [ "character-personality", "character-history", ... ]      │   │
│  ├─────────────────────────────────────────────────────────────────────┤   │
│  │ Key: archive-version:{resourceType}:{resourceId}                    │   │
│  │ Type: Counter (atomic increment for versioning)                     │   │
│  │ Value: 1, 2, 3... (increments on each re-compression)               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  resource-archives (MySQL)                                                  │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: archive:{resourceType}:{resourceId}:{version}                  │   │
│  │ Type: JSON object (ResourceArchiveModel)                            │   │
│  │ Value: {                                                            │   │
│  │   ArchiveId, ResourceType, ResourceId, Version,                     │   │
│  │   Entries: [                                                        │   │
│  │     { SourceType, ServiceName, Data (base64 gzip), ... }            │   │
│  │   ],                                                                │   │
│  │   CreatedAt, SourceDataDeleted                                      │   │
│  │ }                                                                   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  resource-snapshots (Redis with TTL)                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: snap:{snapshotId}                                              │   │
│  │ Type: JSON object (ResourceSnapshotModel) with Redis TTL            │   │
│  │ Value: {                                                            │   │
│  │   SnapshotId, ResourceType, ResourceId, SnapshotType,               │   │
│  │   Entries: [ ... ] (same format as archives),                       │   │
│  │   CreatedAt, ExpiresAt                                              │   │
│  │ }                                                                   │   │
│  │ Auto-deleted: When Redis TTL expires (default 1 hour)               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Compression Flow Diagram**:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Hierarchical Compression Flow                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. Caller invokes POST /resource/compress/execute                          │
│     { resourceType: "character", resourceId: "abc-123" }                    │
│                                                                             │
│  2. Resource Service retrieves registered compression callbacks             │
│     ┌──────────────────────────────────────────────────────────────────┐   │
│     │ compress-callback:character:character-base (priority: 0)         │   │
│     │ compress-callback:character:character-personality (priority: 10) │   │
│     │ compress-callback:character:character-history (priority: 20)     │   │
│     │ compress-callback:character:character-encounter (priority: 30)   │   │
│     └──────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  3. Execute callbacks in priority order (lower = earlier)                   │
│     ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐   │
│     │ Character (L2)  │ --> │ Personality(L4) │ --> │ History (L4)    │   │
│     │ /get-compress-  │     │ /get-compress-  │     │ /get-compress-  │   │
│     │   data          │     │   data          │     │   data          │   │
│     └─────────────────┘     └─────────────────┘     └─────────────────┘   │
│                                                                             │
│  4. Bundle responses into archive                                           │
│     ┌──────────────────────────────────────────────────────────────────┐   │
│     │ ResourceArchive {                                                │   │
│     │   archiveId: "new-guid",                                         │   │
│     │   resourceType: "character",                                     │   │
│     │   resourceId: "abc-123",                                         │   │
│     │   version: 1,                                                    │   │
│     │   entries: [                                                     │   │
│     │     { sourceType: "character-base", data: "H4sI..." },           │   │
│     │     { sourceType: "character-personality", data: "H4sI..." },    │   │
│     │     { sourceType: "character-history", data: "H4sI..." },        │   │
│     │     { sourceType: "character-encounter", data: "H4sI..." }       │   │
│     │   ]                                                              │   │
│     │ }                                                                │   │
│     └──────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  5. Store archive in MySQL (durable)                                        │
│                                                                             │
│  6. If deleteSourceData=true, invoke cleanup callbacks                      │
│                                                                             │
│  7. Publish resource.compressed event                                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

None currently.

---

## Potential Extensions

1. **Per-resource-type cleanup policies**: Currently `DefaultCleanupPolicy` applies globally; could add per-resource-type configuration via `DefineCleanupRequest`.
<!-- AUDIT:NEEDS_DESIGN:2026-02-03:https://github.com/beyond-immersion/bannou-service/issues/275 -->

2. **Automatic cleanup scheduler**: Background service that periodically scans for resources past grace period and triggers cleanup (opt-in per resource type).
<!-- AUDIT:NEEDS_DESIGN:2026-02-03:https://github.com/beyond-immersion/bannou-service/issues/276 -->

3. **Reference type metadata**: Allow consumers to attach metadata to references (e.g., reference strength, priority for cleanup ordering).
<!-- AUDIT:NEEDS_DESIGN:2026-02-03:https://github.com/beyond-immersion/bannou-service/issues/277 -->

4. **Cleanup callback ordering**: Currently all callbacks execute in parallel; could add priority/ordering for sequential cleanup dependencies.
<!-- AUDIT:NEEDS_DESIGN:2026-02-03:https://github.com/beyond-immersion/bannou-service/issues/278 -->

5. **Reference lifecycle hooks**: Pre-register/post-unregister hooks for validation or side effects.

---

## Integration Pattern

### For Higher-Layer Services (L3/L4)

1. **At startup (OnRunningAsync)**: Register cleanup callbacks via `/resource/cleanup/define`

   **CRITICAL**: Cleanup callback registration MUST happen in `OnRunningAsync`, NOT `OnStartAsync`.
   The `OnRunningAsync` lifecycle hook runs AFTER all plugins have completed their `StartAsync` phase,
   ensuring lib-resource is fully available. Registering during `OnStartAsync` is unsafe because
   plugin load order is not guaranteed beyond infrastructure plugins (lib-state, lib-messaging, lib-mesh).

   ```csharp
   // In YourServicePlugin.OnRunningAsync():
   var resourceClient = scope.ServiceProvider.GetService<IResourceClient>();
   if (resourceClient != null)
   {
       await YourService.RegisterResourceCleanupCallbacksAsync(resourceClient, ct);
   }
   ```

   The generated `RegisterResourceCleanupCallbacksAsync` method (from `x-references` schema extension) calls:
   ```csharp
   POST /resource/cleanup/define
   {
     "resourceType": "character",
     "sourceType": "actor",
     "serviceName": "actor",
     "callbackEndpoint": "/actor/cleanup-by-character",
     "payloadTemplate": "{\"characterId\": \"{{resourceId}}\"}",
     "onDeleteAction": "CASCADE"  // or "RESTRICT" / "DETACH"
   }
   ```

   **OnDeleteAction Options**:
   - `CASCADE` (default): Delete dependent entities when resource is deleted
   - `RESTRICT`: Block resource deletion while references of this type exist
   - `DETACH`: Nullify/detach references when resource is deleted (consumer implements)

2. **On entity creation with reference**: Publish event
   ```csharp
   await _messageBus.TryPublishAsync("resource.reference.registered",
       new ResourceReferenceRegisteredEvent
       {
           ResourceType = "character",
           ResourceId = characterId,
           SourceType = "actor",
           SourceId = actorId,
           Timestamp = DateTimeOffset.UtcNow
       }, ct);
   ```

3. **On entity deletion**: Publish event
   ```csharp
   await _messageBus.TryPublishAsync("resource.reference.unregistered",
       new ResourceReferenceUnregisteredEvent { ... }, ct);
   ```

4. **Implement cleanup endpoint**: Handle cascading deletion when called back

### For Foundational Services (L2)

1. **Before deletion**: Check references via `/resource/check`
2. **Execute cleanup**: Call `/resource/cleanup/execute`
3. **Proceed with deletion**: After cleanup succeeds

### Compression Callbacks (For Higher-Layer Services)

1. **At startup (OnRunningAsync)**: Register compression callbacks via `/resource/compress/define`

   ```csharp
   // In YourServicePlugin.OnRunningAsync():
   var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();
   await resourceClient.DefineCompressCallbackAsync(
       new DefineCompressCallbackRequest
       {
           ResourceType = "character",
           SourceType = "character-personality",
           ServiceName = "character-personality",
           CompressEndpoint = "/character-personality/get-compress-data",
           CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
           DecompressEndpoint = "/character-personality/restore-from-archive",
           DecompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\", \"data\": \"{{data}}\"}",
           Priority = 10,
           Description = "Personality traits and combat preferences"
       },
       ct);
   ```

   **Priority**: Lower values execute earlier. Use for dependency ordering:
   - Priority 0: Base entity data (e.g., character core fields)
   - Priority 10-30: Extension data (personality, history, encounters)

2. **Implement compression endpoint**: Return data for archival

   ```csharp
   public async Task<(StatusCodes, YourCompressData?)> GetCompressDataAsync(
       GetCompressDataRequest body,
       CancellationToken cancellationToken)
   {
       // Gather all data for this character that should be archived
       // Return structured response; Resource service will GZip and Base64 encode
       return (StatusCodes.OK, new YourCompressData { ... });
   }
   ```

3. **Implement decompression endpoint**: Restore data from archive

   ```csharp
   public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(
       RestoreFromArchiveRequest body,
       CancellationToken cancellationToken)
   {
       // body.Data contains Base64-encoded GZip JSON
       // Decompress, deserialize, restore to state stores
       return (StatusCodes.OK, new RestoreFromArchiveResponse { Success = true });
   }
   ```

### Compression (For Foundational Services)

1. **Invoke compression**: Call `/resource/compress/execute`

   ```csharp
   var result = await _resourceClient.ExecuteCompressAsync(
       new ExecuteCompressRequest
       {
           ResourceType = "character",
           ResourceId = characterId,
           DeleteSourceData = true,  // Clean up after archival
           CompressionPolicy = CompressionPolicy.ALL_REQUIRED
       }, ct);
   ```

2. **Retrieve archive**: Call `/resource/archive/get` to inspect or export

3. **Restore from archive**: Call `/resource/decompress/execute` to restore deleted data

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None.

### Intentional Quirks (Documented Behavior)

1. **Opaque string identifiers for resourceType/sourceType**: This is deliberate per SCHEMA-RULES.md - lib-resource (L1) must not enumerate L2+ services or entity types, so these are plain strings with no validation.

2. **Set-based reference counting**: Reference count is derived from set cardinality (`SetCountAsync`), not a separate counter. This avoids Lua scripts for atomic increment/decrement. The small race window between operations is acceptable because cleanup always re-validates under distributed lock.

3. **Event handlers delegate to API methods**: `HandleReferenceRegisteredAsync` and `HandleReferenceUnregisteredAsync` simply construct requests and call the API methods. This ensures consistent logic but means event processing pays the full API path cost.

4. **Cleanup lock uses refcount store name**: The distributed lock is acquired with `storeName: StateStoreDefinitions.ResourceRefcounts` even though it's a logical lock, not a data lock. This is intentional - the lock protects the refcount state.

5. **Cleanup callbacks registered in OnRunningAsync**: Consumer plugins MUST register their cleanup callbacks in `OnRunningAsync`, not `OnStartAsync`. This is because `OnRunningAsync` runs after ALL plugins have completed their `StartAsync` phase, guaranteeing lib-resource is available. Plugin load order is not guaranteed beyond infrastructure plugins (L0), so registering during `OnStartAsync` could fail if lib-resource hasn't started yet. See ActorServicePlugin for the reference implementation.

### Design Considerations (Requires Planning)

None currently. Previous design gaps (callback listing/removal, dry-run preview) have been addressed.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Active

*No active work items.*

### Completed (Historical)

- **2026-02-06**: Added seeded resource loading capability (Issue #289):
  - 2 new endpoints (`/resource/seeded/list`, `/resource/seeded/get`)
  - `ISeededResourceProvider` interface in `bannou-service/Providers/`
  - `SeededResource` record type for resource content and metadata
  - `EmbeddedResourceProvider` base class for loading from assembly embedded resources
  - Provider discovery via DI collection (`IEnumerable<ISeededResourceProvider>`)
  - 11 unit tests covering provider discovery, listing, retrieval, and record type
  - Enables plugins to provide factory default resources (ABML behaviors, templates, etc.)

- **2026-02-03**: Added ephemeral snapshot system for living entities:
  - 2 new endpoints (`/resource/snapshot/execute`, `/resource/snapshot/get`)
  - 1 new state store (`resource-snapshots` for Redis TTL-based storage)
  - 1 new event (`resource.snapshot.created`)
  - 3 configuration properties (`SnapshotDefaultTtlSeconds`, `SnapshotMinTtlSeconds`, `SnapshotMaxTtlSeconds`)
  - Uses compression callbacks (same as permanent archival) but stores ephemerally
  - Intended for Storyline Composer and Actor behaviors needing living entity data

- **2026-02-03**: Added centralized compression system:
  - 5 new compression endpoints (`/compress/define`, `/compress/execute`, `/decompress/execute`, `/compress/list`, `/archive/get`)
  - 2 new state stores (`resource-compress` for callbacks, `resource-archives` for MySQL durable storage)
  - 3 new events (`resource.compressed`, `resource.compress.callback-failed`, `resource.decompressed`)
  - 3 configuration properties (`DefaultCompressionPolicy`, `CompressionCallbackTimeoutSeconds`, `CompressionLockExpirySeconds`)
  - Unit tests for compression functionality (48 total tests in lib-resource.tests)
  - Compression callbacks registered by L4 services: character-personality, character-history, character-encounter, character (base data)
  - Supports hierarchical archival with priority ordering and decompression for data recovery
  - Archives stored in MySQL for durability with version tracking via atomic increment

- **2026-02-03**: Added cleanup management enhancements:
  - `dryRun` flag on `/resource/cleanup/execute` for previewing what would happen without executing
  - `/resource/cleanup/list` endpoint to view all registered callbacks with filtering
  - `/resource/cleanup/remove` endpoint to delete orphaned callbacks (idempotent)
  - Added `callback-resource-types` master index for efficient listing without KEYS scan

The lib-resource service is feature-complete for the current integration requirements:
- Schema files (api, events, configuration)
- State store definitions (resource-refcounts, resource-cleanup, resource-grace, resource-compress, resource-archives, resource-snapshots)
- Service implementation with ICacheableStateStore for atomic set operations
- Event handlers for reference tracking wired up via IEventConsumer
- Unit tests covering reference counting, grace periods, cleanup policies, compression, and snapshots
- OnDeleteAction enum (CASCADE/RESTRICT/DETACH) for per-callback deletion behavior
- CompressionPolicy enum (ALL_REQUIRED/BEST_EFFORT) for compression callback failure handling
- Ephemeral snapshot system for living entity data capture

**Integrated Consumers** (Reference Tracking & Cleanup):
- lib-actor: `x-references` schema, `/actor/cleanup-by-character` endpoint
- lib-character-encounter: `x-references` schema, `/character-encounter/delete-by-character` endpoint
- lib-character-history: `x-references` schema, `/character-history/delete-all` cleanup
- lib-character-personality: `x-references` schema, `/character-personality/cleanup-by-character` endpoint

**Integrated Consumers** (Compression):
- lib-character: `/character/get-compress-data` (priority 0)
- lib-character-personality: `/character-personality/get-compress-data`, `/character-personality/restore-from-archive` (priority 10)
- lib-character-history: `/character-history/get-compress-data`, `/character-history/restore-from-archive` (priority 20)
- lib-character-encounter: `/character-encounter/get-compress-data`, `/character-encounter/restore-from-archive` (priority 30)

**Foundation Consumer**:
- lib-character: Queries `/resource/check` in `CheckCharacterReferencesAsync`, invokes `/resource/compress/execute` for character archival

### Pending Integrations

1. **lib-scene**: Not yet integrated with lib-resource. When scene references to characters are added, will need `x-references` schema extension, cleanup endpoint, and optionally compression callbacks.

---

## Related Documents

- [SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md) - Layer placement rationale
- [TENETS.md](../reference/TENETS.md) - Compliance requirements
- [SCHEMA-RULES.md](../reference/SCHEMA-RULES.md) - `x-references` and `x-resource-lifecycle` schema extensions
- [Planning Document](~/.claude/plans/typed-crunching-muffin.md) - Full implementation plan with phases
