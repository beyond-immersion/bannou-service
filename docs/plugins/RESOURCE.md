# Resource Plugin Deep Dive

> **Plugin**: lib-resource
> **Schema**: schemas/resource-api.yaml
> **Version**: 1.0.0
> **Layer**: AppFoundation
> **State Store**: resource-refcounts (Redis), resource-cleanup (Redis), resource-grace (Redis), resource-compress (Redis), resource-archives (MySQL), resource-snapshots (Redis), resource-migrate (Redis)
> **Implementation Map**: [docs/maps/RESOURCE.md](../maps/RESOURCE.md)
> **Endpoints**: 20
> **Events Published**: 8 (resource.grace-period.started, resource.cleanup.callback-failed, resource.compressed, resource.compress.callback-failed, resource.decompressed, resource.snapshot.created, resource.migrated, resource.migrate.callback-failed)
> **Short**: Cross-layer reference tracking, cleanup coordination (CASCADE/RESTRICT/DETACH), and hierarchical compression

---

## Overview

Resource reference tracking, lifecycle management, and hierarchical compression service (L1 AppFoundation) for foundational resources. Enables safe deletion of L2 resources by tracking references from higher-layer consumers (L2/L3/L4) without hierarchy violations, coordinates cleanup callbacks with CASCADE/RESTRICT/DETACH policies, and centralizes compression of resources and their dependents into unified MySQL-backed archives. Placed at L1 so all layers can use it; uses opaque string identifiers for resource/source types to avoid coupling to higher layers. Widely integrated: 13 services use generated reference tracking, 11 services register compression callbacks, and 20 services total inject `IResourceClient`.

---

## Dependents (What Relies On This Plugin)

### Reference Tracking (`x-references` schema extension)

| Dependent | Layer | Cleanup Endpoint |
|-----------|-------|------------------|
| lib-actor | L2 | `/actor/cleanup-by-character` |
| lib-character-encounter | L4 | `/character-encounter/delete-by-character` |
| lib-character-history | L4 | `/character-history/delete-all` |
| lib-character-personality | L4 | `/character-personality/cleanup-by-character` |
| lib-collection | L2 | via generated `CollectionReferenceTracking` |
| lib-divine | L4 | via generated `DivineReferenceTracking` |
| lib-faction | L4 | via generated `FactionReferenceTracking` |
| lib-license | L4 | via generated `LicenseReferenceTracking` |
| lib-obligation | L4 | via generated `ObligationReferenceTracking` |
| lib-realm-history | L4 | via generated `RealmHistoryReferenceTracking` |
| lib-relationship | L2 | via generated `RelationshipReferenceTracking` |
| lib-transit | L2 | via generated `TransitReferenceTracking` |
| lib-worldstate | L2 | via generated `WorldstateReferenceTracking` |

### Compression Callbacks (`x-compression-callback` schema extension)

| Dependent | Layer | Resource Type | Priority |
|-----------|-------|---------------|----------|
| lib-character | L2 | character | 0 |
| lib-character-personality | L4 | character | 10 |
| lib-character-history | L4 | character | 20 |
| lib-character-encounter | L4 | character | 30 |
| lib-quest | L2 | character | 50 |
| lib-storyline | L4 | character | 60 |
| lib-realm-history | L4 | realm | 0 |
| lib-realm | L2 | realm | (via generated `RealmCompressionCallbacks`) |
| lib-location | L2 | location | (via generated `LocationCompressionCallbacks`) |
| lib-faction | L4 | faction | (via generated `FactionCompressionCallbacks`) |
| lib-obligation | L4 | obligation | (via generated `ObligationCompressionCallbacks`) |

### Seeded Resource Providers (`ISeededResourceProvider` DI interface)

Higher-layer plugins implement `ISeededResourceProvider` to expose embedded/static resources (ABML behaviors, scenario templates) that Resource aggregates through the `/resource/seeded/list` and `/resource/seeded/get` endpoints. Resource discovers providers via `IEnumerable<ISeededResourceProvider>` DI collection with per-provider error isolation.

### Other Consumers

| Dependent | Layer | Relationship |
|-----------|-------|-------------|
| lib-character | L2 | Queries `/resource/check` in `CheckCharacterReferencesAsync`; invokes `/resource/compress/execute` for character archival |
| lib-chat | L1 | Constructor-injected `IResourceClient` for room resource tracking |
| lib-game-service | L2 | Constructor-injected `IResourceClient` for game service resource tracking |
| lib-species | L2 | Constructor-injected `IResourceClient` for species resource tracking |
| lib-realm | L2 | Constructor-injected `IResourceClient` for realm resource tracking and compression |
| lib-location | L2 | Constructor-injected `IResourceClient` for location resource tracking and compression |
| lib-puppetmaster | L4 | Uses `ResourceSnapshotCache` for caching resource snapshots (indirect via `IResourceClient`) |
| *lib-scene (not yet integrated)* | *L4* | *Planned: scene-to-character references; cleanup callback* |

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
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  resource-archives (MySQL)                                                  │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: archive:{resourceType}:{resourceId}                            │   │
│  │ Type: JSON object (ResourceArchiveModel)                            │   │
│  │ Note: Single archive per resource; version tracked in model;        │   │
│  │       overwrites previous on re-compression                         │   │
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
│  resource-migrate (Redis)                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: callback:{resourceType}:{sourceType}                           │   │
│  │ Type: JSON object                                                   │   │
│  │ Value: { ServiceName, CallbackEndpoint, PayloadTemplate, ... }      │   │
│  ├─────────────────────────────────────────────────────────────────────┤   │
│  │ Key: callback-index:{resourceType}                                  │   │
│  │ Type: Redis Set (for enumeration without KEYS/SCAN)                 │   │
│  │ Members: [ "actor", "character-personality", "quest" ]              │   │
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

1. **Automatic cleanup scheduler**: Background service that periodically scans for resources past grace period and triggers cleanup (opt-in per resource type).
<!-- AUDIT:NEEDS_DESIGN:2026-02-03:https://github.com/beyond-immersion/bannou-service/issues/276 -->

2. **Cleanup callback ordering**: Currently all callbacks execute in parallel; could add priority/ordering for sequential cleanup dependencies (mirroring compression's existing priority system).
<!-- AUDIT:NEEDS_DESIGN:2026-02-03:https://github.com/beyond-immersion/bannou-service/issues/278 -->

3. **Batch reference unregistration**: When higher-layer services bulk-delete entities (e.g., character-history deleting all participations), each entity makes an individual `UnregisterReferenceAsync` API call. A batch unregister endpoint would reduce O(N) API calls to a single operation.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/351 -->

4. **Batch compression endpoint**: Add `/resource/compress/execute-batch` to compress multiple resources in a single request, improving efficiency for bulk archival operations (e.g., purging many dead characters). Design questions include partial success semantics, lock acquisition strategy, and configurable parallelism.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/253 -->

5. **lib-scene integration**: Not yet integrated with lib-resource (no `IResourceClient` usage, no generated reference tracking or compression files). When scene references to characters are added, will need `x-references` schema extension, cleanup endpoint, and optionally compression callbacks.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/598 -->

6. **lib-seed compression integration**: Seed archival currently retains growth data, capability cache, and bond data indefinitely ([#366](https://github.com/beyond-immersion/bannou-service/issues/366)). Phase 2 of the seed cleanup strategy calls for lib-resource compression integration to archive growth/bond data before deletion, requiring `x-compression-callback` schema extension and compress/decompress endpoints in lib-seed.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/366 -->

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

2. **On entity creation with reference**: Call Resource API directly
   ```csharp
   await _resourceClient.RegisterReferenceAsync(
       new RegisterReferenceRequest
       {
           ResourceType = "character",
           ResourceId = characterId,
           SourceType = "actor",
           SourceId = actorId
       }, ct);
   ```

3. **On entity deletion**: Call Resource API directly
   ```csharp
   await _resourceClient.UnregisterReferenceAsync(
       new UnregisterReferenceRequest { ... }, ct);
   ```

4. **Implement cleanup endpoint**: Handle cascading deletion when called back

### For Foundational Services (L2)

1. **Before deletion**: Check references via `/resource/check`
2. **Execute cleanup**: Call `/resource/cleanup/execute`
3. **Proceed with deletion**: After cleanup succeeds

### Compression Callbacks (For Higher-Layer Services)

**Schema-Driven Registration (Preferred)**: Use the `x-compression-callback` schema extension in your API schema.
The code generator produces a `{Service}CompressionCallbacks.RegisterAsync()` method that you call in your plugin.

See [SCHEMA-RULES.md](../reference/SCHEMA-RULES.md#x-compression-callback-compression-callback-registration) for full documentation.

```yaml
# In your-service-api.yaml
x-compression-callback:
  resourceType: character
  sourceType: character-personality
  priority: 10
  description: Personality traits and combat preferences
  compressEndpoint: /character-personality/get-compress-data
  compressPayloadTemplate: '{"characterId": "{{resourceId}}"}'
  decompressEndpoint: /character-personality/restore-from-archive
  decompressPayloadTemplate: '{"characterId": "{{resourceId}}", "data": "{{data}}"}'
```

```csharp
// In YourServicePlugin.OnRunningAsync():
if (await YourServiceCompressionCallbacks.RegisterAsync(resourceClient, ct))
{
    Logger?.LogInformation("Registered compression callback with lib-resource");
}
```

**Manual Registration (Legacy)**: For cases not yet migrated to schema-driven approach:

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
       return (StatusCodes.OK, new RestoreFromArchiveResponse());
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

3. **Cleanup lock uses refcount store name**: The distributed lock is acquired with `storeName: StateStoreDefinitions.ResourceRefcounts` even though it's a logical lock, not a data lock. This is intentional - the lock protects the refcount state.

4. **Cleanup callbacks registered in OnRunningAsync**: Consumer plugins MUST register their cleanup callbacks in `OnRunningAsync`, not `OnStartAsync`. This is because `OnRunningAsync` runs after ALL plugins have completed their `StartAsync` phase, guaranteeing lib-resource is available. Plugin load order is not guaranteed beyond infrastructure plugins (L0), so registering during `OnStartAsync` could fail if lib-resource hasn't started yet. See ActorServicePlugin for the reference implementation.

5. **Transaction compensation backoff uses transaction-level timestamp**: The `TransactionRecoveryWorker`'s exponential backoff for compensation retries uses `transaction.UpdatedAt` as the base time rather than a per-provision `lastAttemptedAt` timestamp. This means the backoff window is relative to when the transaction was last updated, not when a specific provision's compensation was last attempted. If the transaction's `UpdatedAt` advances because another provision succeeds in the same cycle, the backoff window effectively resets for all remaining failed provisions. In practice this produces a slightly looser backoff than strict per-provision timing — compensations may be retried slightly earlier than the pure exponential schedule would dictate. This is an acceptable tradeoff: per-provision timestamps would add a field to `ResourceProvisionModel` and tracking complexity for marginal scheduling precision, while the transaction-level approach is simpler and still prevents rapid-fire retry floods (the backoff grows with `CompensationAttempts` which IS per-provision).

6. **Transaction endpoints use proper HTTP status codes**: Unlike Resource's existing endpoints which return `200 OK` for all outcomes with structured `success`/`found`/`abortReason` flags, the transaction management endpoints (begin, register-provision, confirm-provision, commit, abort, status) use standard HTTP status codes (400, 404, 409) for business failures. This is a deliberate divergence documented in the implementation map — transaction callers need clear, immediate signal without parsing response bodies. See planning document R8.

7. **PreboundApi fields stored as serialized JSON strings**: Transaction and provision models store `PreboundApi` objects (completionValidation, compensation, verification) as `BannouJson.Serialize()`'d strings in MySQL rather than as typed objects. This preserves all fields at registration-time values — if `PreboundApi` gains new fields, stored transactions retain their original values instead of silently inheriting new defaults. Deserialized via `BannouJson.Deserialize<PreboundApi>()` at execution time.

8. **Commit retry cap is time-based, not counter-based**: The `TransactionRecoveryWorker` determines whether commit resume retries are exhausted by checking how long the transaction has been in `Committing` state (`UpdatedAt` age > `TransactionCommitMaxRetries * WorkerIntervalSeconds`) rather than maintaining an explicit retry counter. This avoids adding a separate counter field to the transaction model while providing equivalent protection — each worker cycle is one implicit retry attempt.

### Design Considerations (Requires Planning)

None currently. Previous design gaps (callback listing/removal, dry-run preview) have been addressed.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Pending Design Review
- **2026-02-01**: [#253](https://github.com/beyond-immersion/bannou-service/issues/253) - Batch compression endpoint (`/resource/compress/execute-batch`) for bulk archival operations
- **2026-02-03**: [#276](https://github.com/beyond-immersion/bannou-service/issues/276) - Automatic cleanup scheduler for periodic grace-period expiry processing (opt-in per resource type)
- **2026-02-03**: [#278](https://github.com/beyond-immersion/bannou-service/issues/278) - Priority ordering for cleanup callbacks (mirroring compression's existing priority system)
- **2026-02-08**: [#351](https://github.com/beyond-immersion/bannou-service/issues/351) - Batch reference unregistration for bulk entity deletion (affects character-history, character-encounter, character-personality, actor)
- **2026-03-08**: [#598](https://github.com/beyond-immersion/bannou-service/issues/598) - Evaluate lib-scene lib-resource integration scope (character references, game service cleanup, asset tracking, scene-to-scene references)
- **2026-03-08**: [#366](https://github.com/beyond-immersion/bannou-service/issues/366) - lib-seed compression integration for growth/bond data archival before deletion

### Active

- **Resource Transactions (Phase 1)**: Durable multi-service provisioning and cleanup coordination. 6 new endpoints (begin, register-provision, confirm-provision, commit, abort, status), TransactionRecoveryWorker background service (TTL validation, commit resume, compensation retry, metadata purge), 2 MySQL state stores, 8 transaction lifecycle events, 11 configuration properties. See [RESOURCE-TRANSACTIONS.md](../planning/RESOURCE-TRANSACTIONS.md) for the full design. Phase 1a (prerequisite compensation endpoints — Seed delete) and Phase 1b (caller-specified IDs) pending. Phase 2 (Genesis migration) depends on 1a/1b.

### Completed

- **2026-03-19**: PreboundApi moved to Core SDK (`sdks/core/PreboundApi.cs`) with `x-sdk-type` schema references. ResponseTransformer added to Core SDK for declarative response validation. Contract schema modernization tracked in [#701](https://github.com/beyond-immersion/bannou-service/issues/701).

