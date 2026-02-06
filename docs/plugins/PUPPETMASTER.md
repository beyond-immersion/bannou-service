# Puppetmaster Plugin Deep Dive

> **Plugin**: lib-puppetmaster
> **Schema**: schemas/puppetmaster-api.yaml
> **Version**: 1.0.0
> **State Store**: None (in-memory only)

---

## Overview

The Puppetmaster service orchestrates dynamic behaviors, regional watchers, and encounter coordination for the Arcadia game system. It "pulls the strings" while actors perform on stage. As an L4 (Game Features) service, it provides the missing link between the behavior execution runtime (lib-actor at L2) and the asset service (lib-asset at L3) - enabling dynamic ABML behavior loading that would otherwise violate the service hierarchy. The service implements `IBehaviorDocumentProvider` (priority 100) to supply runtime-loaded behaviors to actors via the provider chain pattern.

**Key Responsibilities**:
- Dynamic behavior document caching and loading from Asset service
- Regional watcher lifecycle management (create/stop/list)
- Resource snapshot caching for Event Brain actors
- Automatic watcher startup on realm creation events

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-messaging (`IMessageBus`) | Publishing watcher and behavior invalidation events |
| lib-asset (`IAssetClient`) | Downloading ABML YAML documents via pre-signed URLs |
| lib-resource (`IResourceClient`) | Loading resource snapshots for Event Brain actors |
| `IEventConsumer` | Registering handlers for realm lifecycle events |
| `IServiceScopeFactory` | Creating scopes to resolve scoped service clients |
| `IHttpClientFactory` | Downloading YAML content from asset download URLs |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor | Consumes `IBehaviorDocumentProvider` (priority 100) for dynamic behavior loading |
| *(future)* | Other services may subscribe to `puppetmaster.watcher.*` events |

---

## State Storage

**Store**: None - all state is in-memory

The Puppetmaster service is a `Singleton` and maintains all state in memory via `ConcurrentDictionary`:

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `_cache[behaviorRef]` | `AbmlDocument` | Cached parsed ABML behavior documents |
| `_activeWatchers[watcherId]` | `WatcherInfo` | Active regional watcher instances |
| `_watchersByRealmAndType[(realmId, watcherType)]` | `Guid` | Secondary index for fast realm-filtered lookups |
| `_cache[resourceType:resourceId]` (ResourceSnapshotCache) | `CacheEntry` | Cached resource snapshots with TTL |

**Important**: Watcher state is **not persisted**. If the service restarts, all watchers are lost. This is by design for the current phase - persistent watcher state will require distributed state in a future phase.

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `puppetmaster.behavior.invalidated` | `BehaviorInvalidatedEvent` | When `InvalidateBehaviorsAsync` is called |
| `puppetmaster.watcher.started` | `WatcherStartedEvent` | When a watcher is successfully started |
| `puppetmaster.watcher.stopped` | `WatcherStoppedEvent` | When a watcher is stopped |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `realm.created` | `HandleRealmCreatedAsync` | Auto-starts regional watchers for newly created active realms |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `BehaviorCacheMaxSize` | `PUPPETMASTER_BEHAVIOR_CACHE_MAX_SIZE` | 1000 | Maximum number of behavior documents to cache in memory |
| `BehaviorCacheTtlSeconds` | `PUPPETMASTER_BEHAVIOR_CACHE_TTL_SECONDS` | 3600 | Time-to-live for cached behavior documents in seconds (1 hour default) |
| `AssetDownloadTimeoutSeconds` | `PUPPETMASTER_ASSET_DOWNLOAD_TIMEOUT_SECONDS` | 30 | Timeout for downloading behavior YAML from asset service |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<PuppetmasterService>` | Structured logging |
| `PuppetmasterServiceConfiguration` | Typed configuration access |
| `IMessageBus` | Event publishing |
| `IBehaviorDocumentCache` | Behavior document caching (in-memory) |
| `IEventConsumer` | Event handler registration for pub/sub fan-out |
| `BehaviorDocumentCache` | Implementation of `IBehaviorDocumentCache` |
| `DynamicBehaviorProvider` | `IBehaviorDocumentProvider` implementation (priority 100) |
| `ResourceSnapshotCache` | `IResourceSnapshotCache` implementation for Event Brain actors |
| `ResourceArchiveProvider` | `IVariableProvider` implementation (not DI-registered - created per-snapshot for ABML expressions) |
| `LoadSnapshotHandler` | `IActionHandler` for `load_snapshot:` ABML action - enables Event Brain actors to load resource snapshots |
| `PrefetchSnapshotsHandler` | `IActionHandler` for `prefetch_snapshots:` ABML action - batch cache warmup for multiple snapshots |
| `SpawnWatcherHandler` | `IActionHandler` for `spawn_watcher:` ABML action - spawns regional watchers |
| `StopWatcherHandler` | `IActionHandler` for `stop_watcher:` ABML action - stops regional watchers |
| `ListWatchersHandler` | `IActionHandler` for `list_watchers:` ABML action - queries active watchers |

---

## ABML Action Handlers

The Puppetmaster plugin provides ABML action handlers that extend the behavior execution runtime. These handlers are discovered by `DocumentExecutorFactory` via `GetServices<IActionHandler>()`.

### load_snapshot

Loads a resource snapshot and registers it as a variable provider for expression evaluation.

**YAML Syntax**:
```yaml
- load_snapshot:
    name: candidate          # Provider name for expressions (required)
    resource_type: character # Resource type to load (required)
    resource_id: ${target_id} # Expression evaluating to GUID (required)
    filter:                  # Optional: limit to specific source types
      - character-personality
      - character-history
```

**After loading, access via expressions**:
```yaml
- cond:
    - when: ${candidate.personality.aggression > 0.7}
      then:
        - log: "High aggression detected"
```

**Implementation Notes**:
- Provider is registered in **root scope** (document-wide access)
- Uses `ResourceSnapshotCache` for TTL-based caching
- If snapshot cannot be loaded, registers an empty provider (graceful degradation - returns null for all paths)
- `resource_id` expression is evaluated at runtime, enabling dynamic resource loading

### spawn_watcher

Spawns a regional watcher for the specified realm via the Puppetmaster service.

**YAML Syntax**:
```yaml
- spawn_watcher:
    watcher_type: regional           # Required - watcher type string
    realm_id: ${event.realmId}       # Required - realm GUID expression
    behavior_id: watcher-regional    # Optional - behavior document to use
    into: spawned_watcher_id         # Optional - variable to store watcher ID
```

**Implementation Notes**:
- Calls `IPuppetmasterClient.StartWatcherAsync` via mesh
- Returns existing watcher if one already exists for realm/type combination
- If `into` is specified, stores the watcher ID in that variable
- `realm_id` is required - returns error if not provided

### stop_watcher

Stops a running regional watcher.

**YAML Syntax**:
```yaml
- stop_watcher:
    watcher_id: ${watcher_to_stop}   # Required - watcher GUID expression
```

**Implementation Notes**:
- Calls `IPuppetmasterClient.StopWatcherAsync` via mesh
- Returns success even if watcher doesn't exist (idempotent behavior)

### list_watchers

Queries active watchers with optional filtering.

**YAML Syntax**:
```yaml
- list_watchers:
    into: active_watchers            # Required - variable to store results
    realm_id: ${realm_id}            # Optional - filter by realm
    watcher_type: regional           # Optional - filter by type
```

**Implementation Notes**:
- Calls `IPuppetmasterClient.ListWatchersAsync` via mesh
- Client-side filters by `watcher_type` since API only supports realm filter
- Result is a list of `WatcherInfo` objects with `watcherId`, `realmId`, `watcherType`, `startedAt`, `behaviorRef`, and `actorId` properties

---

## API Endpoints (Implementation Notes)

**Tag: Puppetmaster**
- `POST /puppetmaster/status` - Returns service health, cached behavior count, and active watcher count. Always returns `isHealthy: true`.

**Tag: Behaviors**
- `POST /puppetmaster/behaviors/invalidate` - Invalidates specific or all cached behaviors. Publishes `BehaviorInvalidatedEvent` regardless of whether any behaviors were actually invalidated.

**Tag: Watchers**
- `POST /puppetmaster/watchers/list` - Lists active watchers with optional realm filter. Returns from in-memory dictionary.
- `POST /puppetmaster/watchers/start` - Starts a watcher for a realm/type combination. Idempotent - returns existing watcher if already running for that realm/type. Requires `developer` role.
- `POST /puppetmaster/watchers/stop` - Stops a watcher by ID. Returns `stopped: false` if watcher not found (does not error).
- `POST /puppetmaster/watchers/start-for-realm` - Starts all default watcher types for a realm. Currently only starts "regional" type. Requires `developer` role.

**Permission Notes**:
- Status and list endpoints have empty `x-permissions` (no auth required for internal calls)
- Start/stop endpoints require `developer` role

---

## Visual Aid

```
                                 ┌──────────────────────────────────────┐
                                 │         PuppetmasterService          │
                                 │            (Singleton, L4)           │
                                 ├──────────────────────────────────────┤
                                 │ _activeWatchers: ConcurrentDict      │
                                 │ _watchersByRealmAndType: ConcurrentDict│
                                 └──────────────┬───────────────────────┘
                                                │
            ┌───────────────────────────────────┼───────────────────────────────────┐
            │                                   │                                   │
            ▼                                   ▼                                   ▼
┌───────────────────────┐      ┌───────────────────────────┐      ┌───────────────────────┐
│  BehaviorDocumentCache │      │    ResourceSnapshotCache   │      │  DynamicBehaviorProvider│
│     (Singleton)        │      │       (Singleton)          │      │     (Singleton)        │
├───────────────────────┤      ├───────────────────────────┤      ├───────────────────────┤
│ _cache: ConcurrentDict │      │ _cache: ConcurrentDict    │      │ Priority: 100         │
│ _parser: DocumentParser│      │ _defaultTtl: 5 min        │      │ CanProvide: GUID only │
└──────────┬────────────┘      └──────────┬────────────────┘      └───────────────────────┘
           │                              │
           │ GetOrLoadAsync               │ GetOrLoadAsync
           ▼                              ▼
    ┌────────────┐                 ┌────────────┐
    │ IAssetClient│                │IResourceClient│
    │    (L3)    │                 │    (L1)    │
    └────────────┘                 └────────────┘
```

---

## Stubs & Unimplemented Features

1. **Watcher-Actor Integration**: The `ActorId` field on `WatcherInfo` is always `null`. The TODO comment at line 213 indicates actor spawning is not yet implemented. Watchers don't actually execute any behavior - they're just registered in memory.

2. **Configurable Default Watcher Types**: `StartWatchersForRealmAsync` hardcodes `defaultWatcherTypes = ["regional"]`. The code comment notes this "could be configurable per realm or game service".

3. **ResourceSnapshotCache TTL Configuration**: The TTL is hardcoded to 5 minutes (`TimeSpan.FromMinutes(5)`). There's no configuration property to adjust this.

---

## Potential Extensions

1. **Distributed Watcher State**: Move watcher registry to Redis for multi-instance consistency and persistence across restarts.

2. **Watcher Type Configuration**: Allow per-realm or per-game-service configuration of which watcher types should auto-start.

3. **Behavior Variant Selection**: Support the ABML variant system with fallback chains (e.g., `character-personality:aggressive` → `character-personality:default` → `character-base`).

4. **Watcher Health Monitoring**: Track watcher execution health, restart failed watchers, expose metrics.

5. **Cache Warm-up on Startup**: Pre-load commonly-used behaviors on service startup to reduce first-request latency.

6. **Realm Deactivation Handling**: Subscribe to realm deactivation/deletion events to automatically stop watchers.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*None currently identified.*

### Intentional Quirks (Documented Behavior)

1. **StopWatcher returns success for non-existent watchers**: `StopWatcherAsync` returns `(StatusCodes.OK, { Stopped = false })` instead of an error when the watcher doesn't exist. This is intentional idempotency - callers don't need to check if a watcher exists before stopping it.

2. **Watcher uniqueness is per (realm, type)**: Only one watcher per realm/type combination is allowed. `StartWatcherAsync` returns the existing watcher with `AlreadyExisted = true` rather than creating a duplicate.

3. **DynamicBehaviorProvider only handles GUIDs**: `CanProvide` returns `false` for non-GUID behavior references (like `"humanoid_base"`). This is intentional - named behaviors are served by the SeededBehaviorProvider (priority 50) in lib-actor.

4. **Lazy TTL eviction for behavior cache**: Expired cache entries are removed on access, not proactively via background cleanup. This matches the `ResourceSnapshotCache` pattern and avoids background task overhead for a cache that's typically small.

5. **ResourceSnapshotCache uses `GetService<T>()` not constructor injection**: Because `IResourceClient` is an optional L1 dependency and the cache is a singleton, it resolves the client via service scope at runtime. This is the correct pattern per SERVICE-HIERARCHY.md.

6. **Event handler doesn't stop watchers on realm deletion**: The service only subscribes to `realm.created`, not `realm.deleted`. Watchers for deleted realms continue running until manually stopped or service restart.

### Design Considerations (Requires Planning)

1. **In-memory watcher state is lost on restart**: All active watchers are lost when the service restarts. This is acceptable for the current phase but will need distributed state for production. Requires design decisions about:
   - State store backend (Redis vs MySQL)
   - Recovery behavior on startup (auto-restart previously active watchers?)
   - Heartbeat/liveness tracking for watchers

2. **No actor spawning integration**: Watchers are just data structures without actual behavior execution. The full implementation requires:
   - Spawning actors via `IActorClient`
   - Passing behavior references to actors
   - Tracking actor lifecycle (restart on crash)
   - Correlating watcher IDs with actor IDs

---

## Work Tracking

This section tracks active development work. Managed by `/audit-plugin` workflow.

### Completed

- **2026-02-06**: Issue #298 - Added `spawn_watcher`, `stop_watcher`, and `list_watchers` ABML actions for Puppetmaster self-orchestration. Replaced forbidden generic `api_call` with purpose-built handlers.
