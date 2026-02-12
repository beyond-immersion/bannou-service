# Puppetmaster Plugin Deep Dive

> **Plugin**: lib-puppetmaster
> **Schema**: schemas/puppetmaster-api.yaml
> **Version**: 1.0.0
> **State Store**: None (in-memory only)

---

## Overview

The Puppetmaster service (L4 GameFeatures) orchestrates dynamic behaviors, regional watchers, and encounter coordination for the Arcadia game system. Provides the bridge between the behavior execution runtime (lib-actor at L2) and the asset service (lib-asset at L3), enabling dynamic ABML behavior loading that would otherwise violate the service hierarchy. Implements `IBehaviorDocumentProvider` to supply runtime-loaded behaviors to actors via the provider chain pattern. Also manages regional watcher lifecycle and resource snapshot caching for Event Brain actors.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-messaging (`IMessageBus`) | Publishing watcher and behavior invalidation events |
| lib-messaging (`IMessageSubscriber`) | Dynamic subscriptions to lifecycle event topics for watch system |
| lib-actor (`IActorClient`) | Injecting perceptions into actors (watch notifications, behavior hot-reload) - resolved via `IServiceScopeFactory` |
| lib-asset (`IAssetClient`) | Downloading ABML YAML documents via pre-signed URLs |
| lib-resource (`IResourceClient`) | Loading resource snapshots for Event Brain actors |
| `IEventConsumer` | Registering handlers for realm lifecycle, behavior, and actor deletion events |
| `IServiceScopeFactory` | Creating scopes to resolve scoped service clients (`IActorClient`, `IResourceClient`) |
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
| `_actorWatches[actorId][resourceKey]` (WatchRegistry) | `WatchEntry` | Per-actor watch subscriptions (actor→resources index) |
| `_resourceWatchers[resourceKey][actorId]` (WatchRegistry) | `byte` | Per-resource watching actors (resource→actors index) |

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
| `behavior.updated` | `HandleBehaviorUpdatedAsync` | Invalidates cached behavior document by AssetId, then paginates through all running actors via `IActorClient` to inject behavior_updated perceptions for hot-reload |
| `actor.instance.deleted` | `HandleActorDeletedAsync` | Cleans up all watches in `WatchRegistry` for the deleted actor |
| *(dynamic lifecycle topics)* | `HandleLifecycleEventAsync` | Subscribed via `IMessageSubscriber.SubscribeDynamicRawAsync` to all topics from `ResourceEventMapping`. Dispatches `WatchPerception` events to actors watching the affected resource. |

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
| `IMessageSubscriber` | Dynamic subscriptions to lifecycle event topics |
| `IBehaviorDocumentCache` | Behavior document caching (in-memory) |
| `IEventConsumer` | Event handler registration for pub/sub fan-out |
| `WatchRegistry` | Dual-indexed in-memory watch subscription registry |
| `ResourceEventMapping` | Maps resource types to lifecycle event topics (loaded from generated `ResourceEventMappings.All`) |
| `BehaviorDocumentCache` | Implementation of `IBehaviorDocumentCache` |
| `DynamicBehaviorProvider` | `IBehaviorDocumentProvider` implementation (priority 100) |
| `ResourceSnapshotCache` | `IResourceSnapshotCache` implementation for Event Brain actors |
| `ResourceArchiveProvider` | `IVariableProvider` implementation (not DI-registered - created per-snapshot for ABML expressions) |
| `LoadSnapshotHandler` | `IActionHandler` for `load_snapshot:` ABML action - enables Event Brain actors to load resource snapshots |
| `PrefetchSnapshotsHandler` | `IActionHandler` for `prefetch_snapshots:` ABML action - batch cache warmup for multiple snapshots |
| `SpawnWatcherHandler` | `IActionHandler` for `spawn_watcher:` ABML action - spawns regional watchers |
| `StopWatcherHandler` | `IActionHandler` for `stop_watcher:` ABML action - stops regional watchers |
| `ListWatchersHandler` | `IActionHandler` for `list_watchers:` ABML action - queries active watchers |
| `WatchHandler` | `IActionHandler` for `watch:` ABML action - registers resource change watches |
| `UnwatchHandler` | `IActionHandler` for `unwatch:` ABML action - removes resource change watches |

---

## Event Brain Architecture

The Puppetmaster plugin provides the infrastructure for **Event Brain actors** - actors that orchestrate multiple characters dynamically (regional watchers, encounter coordinators) as opposed to **Character Brain actors** which are bound to a single character.

### Character Brain vs Event Brain Data Access

| Aspect | Character Brain | Event Brain |
|--------|-----------------|-------------|
| **Binding** | One actor → one character | One actor → many characters (dynamic) |
| **Data source** | Live variable providers via DI | Resource snapshots via `load_snapshot:` |
| **Provider registration** | Once at actor creation | On-demand during execution |
| **Cache location** | Per-actor in ActorRunner | Shared `ResourceSnapshotCache` |
| **TTL behavior** | Provider-specific caching | 5-minute default TTL |

### How Event Brain Data Access Works

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Event Brain Execution Flow                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ABML Behavior Document                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ - load_snapshot:                                                     │   │
│  │     name: attacker                                                   │   │
│  │     resource_type: character                                         │   │
│  │     resource_id: ${attacker_id}                                      │   │
│  │     filter: [character-personality, character-history]               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                      │                                                      │
│                      ▼                                                      │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    LoadSnapshotHandler                               │   │
│  │  1. Evaluate ${attacker_id} → Guid                                  │   │
│  │  2. Check ResourceSnapshotCache for cached snapshot                 │   │
│  │  3. If miss: call IResourceClient.GetSnapshotAsync(filter)          │   │
│  │  4. Create ResourceArchiveProvider from snapshot entries            │   │
│  │  5. Register provider as "attacker" in root execution scope         │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                      │                                                      │
│                      ▼                                                      │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │           ResourceArchiveProvider (IVariableProvider)                │   │
│  │                                                                      │   │
│  │  Expression: ${attacker.personality.aggression}                      │   │
│  │       ├── namespace: "attacker"                                      │   │
│  │       ├── source: "character-personality"                            │   │
│  │       └── path: "aggression"                                         │   │
│  │                                                                      │   │
│  │  Resolves to: 0.75 (from snapshot data)                              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Performance Pattern: Prefetch Before Iteration

When an Event Brain needs to evaluate multiple characters (e.g., all raid participants), use `prefetch_snapshots:` to batch-load data before iterating:

```yaml
# Without prefetch: N sequential API calls during foreach
# With prefetch: 1 batch call + N cache hits
- prefetch_snapshots:
    resource_type: character
    resource_ids: ${participants | map('character_id')}
    filter: [character-personality]

- foreach:
    variable: p
    collection: ${participants}
    do:
      - load_snapshot:              # Cache hit - instant
          name: char
          resource_type: character
          resource_id: ${p.character_id}
```

See [ACTOR.md](ACTOR.md) for the Variable Provider Factory pattern used by Character Brains.

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

### watch

Registers a resource change watch in the `WatchRegistry`. When the watched resource is modified (via lifecycle events matching the optional sources filter), the Puppetmaster service injects a `WatchPerception` into the actor's bounded perception channel.

**YAML Syntax**:
```yaml
- watch:
    resource_type: character          # Required - resource type to watch
    resource_id: ${target_id}         # Required - expression evaluating to GUID
    sources:                          # Optional - filter to specific source types
      - character-personality
      - character-history
    on_change: handle_update          # Optional - flow to invoke on change (default: queued perception)
```

**Implementation Notes**:
- Requires actor context (`ActorId` must be present in execution context)
- `resource_id` is evaluated at runtime via `ValueEvaluator` - must resolve to a valid GUID
- `sources` filter limits which lifecycle event source types trigger notifications (null = all sources match)
- Duplicate watches on the same resource overwrite the previous entry (idempotent)
- Watches are stored in-memory in `WatchRegistry` and lost on service restart

### unwatch

Removes a resource change watch from the `WatchRegistry`.

**YAML Syntax**:
```yaml
- unwatch:
    resource_type: character          # Required - resource type to stop watching
    resource_id: ${target_id}         # Required - expression evaluating to GUID
```

**Implementation Notes**:
- Requires actor context (`ActorId` must be present in execution context)
- Returns success even if the watch doesn't exist (idempotent no-op)
- Deletion events auto-unwatch affected resources, so explicit unwatch is not needed for deleted resources

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

## Watch System

The Puppetmaster plugin includes a watch system for subscribing to resource change events. This enables dynamic behavior triggering when characters, realms, or other resources are modified.

### Components

| Component | Role |
|-----------|------|
| `ResourceEventMapping` | Maps resource types to their lifecycle event topics (loaded from generated `ResourceEventMappings.All`) |
| `WatchRegistry` | Dual-indexed in-memory registry: `actor→{resourceKey→WatchEntry}` and `resource→{actorId}` for bidirectional lookup |
| `WatchPerception` | Perception payload injected into actor channels: `watch:resource_changed` (urgency 0.7) and `watch:resource_deleted` (urgency 0.9) |
| `WatchHandler` | ABML `watch:` action handler - registers watches in `WatchRegistry` |
| `UnwatchHandler` | ABML `unwatch:` action handler - removes watches from `WatchRegistry` |

### Resource Event Mapping

The `ResourceEventMapping` class maps resource types (e.g., "character", "realm") to their lifecycle events. **Mappings are auto-generated** from `x-resource-mapping` schema extensions and loaded at startup from `ResourceEventMappings.All`.

**Key Methods**:
- `GetSourcesForResource(resourceType)` - Get all source types for a resource (e.g., character → ["character", "character-personality", "character-history", "character-encounter"])
- `GetMapping(sourceType)` - Get mapping details for a source type
- `GetAllTopics()` - Get all unique event topics to subscribe to
- `GetSourceTypesForTopic(topic)` - Get source types that publish to a topic

**Schema-Driven Discovery**:
Mappings are declared in event schemas using `x-resource-mapping`:

```yaml
# In lifecycle events (character-events.yaml)
x-lifecycle:
  Character:
    resource_mapping:
      resource_type: character      # Which resource this affects
      # resource_id_field defaults to primary key (characterId)
      # source_type defaults to topic base (character)

# In non-lifecycle events (character-personality-events.yaml)
PersonalityUpdatedEvent:
  type: object
  x-resource-mapping:
    resource_type: character
    resource_id_field: characterId
    source_type: character-personality
```

The `generate-resource-mappings.py` script scans all event schemas and generates `bannou-service/Generated/ResourceEventMappings.cs` with the complete mapping registry.

### Watch Dispatch Flow

When a lifecycle event arrives (e.g., `personality.updated`):

1. `HandleLifecycleEventAsync` receives raw JSON via `IMessageSubscriber`
2. Looks up all source types for the topic via `ResourceEventMapping`
3. Extracts the resource ID from the event data using the configured field name
4. Queries `WatchRegistry.GetWatchers()` for actors watching that resource
5. For change events: checks `HasMatchingWatch()` against the actor's sources filter
6. For deletion events: notifies all watchers regardless of source filter, then auto-unwatches
7. Injects a `WatchPerception` into the actor's bounded channel via `IActorClient.InjectPerceptionAsync`

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

1. **`actor.instance.deleted` subscription not declared in events schema**: The subscription is registered manually in `RegisterEventConsumers` via `IEventConsumer` but is NOT declared in `x-event-subscriptions` in `puppetmaster-events.yaml`. This is a schema-code mismatch violating schema-first development. Fix: add `actor.instance.deleted` to the schema's `x-event-subscriptions` list.

### Intentional Quirks (Documented Behavior)

1. **StopWatcher returns success for non-existent watchers**: `StopWatcherAsync` returns `(StatusCodes.OK, { Stopped = false })` instead of an error when the watcher doesn't exist. This is intentional idempotency - callers don't need to check if a watcher exists before stopping it.

2. **Watcher uniqueness is per (realm, type)**: Only one watcher per realm/type combination is allowed. `StartWatcherAsync` returns the existing watcher with `AlreadyExisted = true` rather than creating a duplicate.

3. **DynamicBehaviorProvider only handles GUIDs**: `CanProvide` returns `false` for non-GUID behavior references (like `"humanoid_base"`). This is intentional - named behaviors are served by the SeededBehaviorProvider (priority 50) in lib-actor.

4. **Lazy TTL eviction for behavior cache**: Expired cache entries are removed on access, not proactively via background cleanup. This matches the `ResourceSnapshotCache` pattern and avoids background task overhead for a cache that's typically small.

5. **ResourceSnapshotCache uses `GetRequiredService<IResourceClient>()` via scope**: Because the cache is a Singleton and `IResourceClient` is Scoped, it cannot use constructor injection. Instead it creates a scope and calls `GetRequiredService<IResourceClient>()` - which correctly fails fast if the Resource service (L1) is unavailable, per the hard dependency pattern for L0/L1/L2 dependencies.

6. **Event handler doesn't stop watchers on realm deletion**: The service only subscribes to `realm.created`, not `realm.deleted`. Watchers for deleted realms continue running until manually stopped or service restart.

7. **Dynamic lifecycle subscriptions bypass `IEventConsumer`**: The `SubscribeToLifecycleEvents` method uses `IMessageSubscriber.SubscribeDynamicRawAsync()` directly because the subscribed topics are determined at runtime from `ResourceEventMapping`, not statically known at schema time. This is intentional - `IEventConsumer` requires compile-time event type registration.

8. **`IActorClient` uses soft dependency pattern despite being L2 (hard dependency from L4)**: Both `InjectWatchPerceptionAsync` and `HandleBehaviorUpdatedAsync` resolve `IActorClient` via `GetService<T>()` with null check instead of `GetRequiredService<T>()`. Actor is L2 (GameFoundation), which per the hierarchy is a hard dependency from L4. The soft pattern is used because these are event handlers that may fire during startup before Actor is fully available, but this diverges from the documented hard dependency pattern.

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
