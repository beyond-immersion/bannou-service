# Actor Plugin Deep Dive

> **Plugin**: lib-actor
> **Schema**: schemas/actor-api.yaml
> **Version**: 1.0.0
> **State Stores**: actor-templates (Redis/MySQL), actor-state (Redis), actor-pool-nodes (Redis), actor-assignments (Redis)

---

## Overview

Distributed actor management and execution (L2 GameFoundation) for NPC brains, event coordinators, and long-running behavior loops. Actors output behavioral state (feelings, goals, memories) to characters -- not directly visible to players. Supports multiple deployment modes (local, pool-per-type, shared-pool, auto-scale), ABML behavior document execution with hot-reload, GOAP planning integration, and bounded perception queues with urgency filtering. Receives data from L4 services (personality, encounters, history) via the Variable Provider Factory pattern without depending on them.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for actor state, templates, pool nodes, assignments |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events, state updates, pool commands; error publishing |
| lib-messaging (`IEventConsumer`) | Behavior/personality cache invalidation, pool node management events |
| lib-mesh (`IMeshInvocationClient`) | Forwarding requests to remote pool nodes in distributed mode |
| lib-resource (`IResourceClient`) | Character reference cleanup via x-references pattern |
| `IEnumerable<IVariableProviderFactory>` | DI-discovered providers from L3/L4 services (see below) |
| `IEnumerable<IBehaviorDocumentProvider>` | DI-discovered behavior document providers (see below) |

**Behavior Document Loading (Provider Chain)**

Actor loads ABML documents via `IBehaviorDocumentLoader` which discovers providers via DI. The provider chain supports multiple sources with priority-based fallback:

| Provider | Priority | Source Plugin | Purpose |
|----------|----------|---------------|---------|
| `DynamicBehaviorProvider` | 100 | lib-puppetmaster | Scene-specific dynamic behaviors |
| `SeededBehaviorProvider` | 50 | lib-actor | Static behaviors from filesystem/embedded resources |
| `FallbackBehaviorProvider` | 0 | lib-actor | Logs warning and returns null for missing behaviors (graceful degradation) |

Higher priority providers are checked first. If a provider returns `null`, the next lower priority provider is tried. The fallback provider always returns `null` after logging a warning (it does not generate stub behaviors).

**Note**: Actor uses shared ABML compiler types from `BeyondImmersion.Bannou.BehaviorCompiler` (in bannou-service), NOT a service client dependency on lib-behavior.

**Variable Provider Factory Pattern (L2 → L3/L4 Data Access)**

Actor is L2 (GameFoundation) but needs data from L3/L4 services (personality, encounters, quests) at runtime. Per SERVICE-HIERARCHY.md, this uses **dependency inversion**:

1. `IVariableProviderFactory` interface defined in `bannou-service/Providers/`
2. L3/L4 services implement and register factories via DI
3. ActorRunner discovers all factories via `IEnumerable<IVariableProviderFactory>` injection
4. Each factory creates providers on-demand with graceful degradation

**Registered Provider Factories** (from L3/L4 plugins):
| Factory | Provider | Owning Plugin |
|---------|----------|---------------|
| `PersonalityProviderFactory` | `personality` | lib-character-personality |
| `CombatPreferencesProviderFactory` | `combat_preferences` | lib-character-personality |
| `BackstoryProviderFactory` | `backstory` | lib-character-history |
| `EncountersProviderFactory` | `encounters` | lib-character-encounter |
| `QuestProviderFactory` | `quest` | lib-quest |

**Character Brain vs Event Brain Data Access**

The Variable Provider Factory pattern above applies to **Character Brain actors** - actors bound to a single character that access data about *themselves* via live providers.

**Event Brain actors** (regional watchers, encounter coordinators) use a different pattern because they orchestrate *multiple* characters dynamically. Instead of live variable providers, Event Brain actors use:

- **ResourceArchiveProvider** (from lib-puppetmaster) - provides ABML expression access to resource snapshots
- **ResourceSnapshotCache** (from lib-puppetmaster) - caches loaded snapshots with TTL-based expiration
- **`load_snapshot:` ABML action** - explicitly loads character/resource data into the execution scope

This separation exists because:
1. Character Brains have a stable binding to one character ID (providers can be created once)
2. Event Brains shift focus constantly (load data on-demand for whichever entity they're currently evaluating)

See [PUPPETMASTER.md](PUPPETMASTER.md) for Event Brain architecture details and [BEHAVIOR.md](BEHAVIOR.md) Domain Actions Reference for the `load_snapshot:` and `prefetch_snapshots:` actions.

**Data Access Pattern Selection**

When an actor needs data from another service, the correct pattern depends on the data's characteristics:

```
Is this the actor's own cognitive state (memories, perceptions)?
    YES → Shared store (agent-memories, owned by lib-behavior cognition pipeline)
    NO  ↓

Is this character attribute data for ABML expressions?
    YES → Variable Provider Factory (cached API calls, DI-discovered)
          Owning L4 plugin provides the factory + cache.
    NO  ↓

Is consistency critical (currency balance, item ownership)?
    YES → Direct API call via lib-mesh (authoritative source)
    NO  → Variable Provider Factory with appropriate cache TTL
```

Current providers cover personality, combat preferences, backstory, encounters, and quests. Planned future providers ([#147](https://github.com/beyond-immersion/bannou-service/issues/147)): currency (30s TTL), inventory (1m TTL), relationships (5m TTL). Spatial context ([#145](https://github.com/beyond-immersion/bannou-service/issues/145)): coarse-grained zone/region awareness (10s TTL) for GOAP planning -- actors need "am I in hostile territory?" not frame-by-frame coordinates.

**Anti-patterns**: Never access another plugin's state store directly. Never poll APIs in tight loops (use Variable Providers with cache). Never cache mutation-critical data beyond short TTLs.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| (none in production) | Actor service is a terminal consumer; other services publish perceptions to it |

---

## State Storage

**Stores**: 4 Redis state stores

| Store | Purpose |
|-------|---------|
| `actor-templates` | Actor template definitions and category index |
| `actor-state` | Runtime actor state snapshots (feelings, goals, memories) |
| `actor-pool-nodes` | Pool node registration and health status |
| `actor-assignments` | Actor-to-node assignment tracking |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{templateId}` | `ActorTemplateData` | Template definition |
| `category:{categoryName}` | `ActorTemplateData` | Category-based template lookup |
| `_all_template_ids` | `List<string>` | Global template index |
| `{actorId}` | `ActorStateSnapshot` | Runtime state (feelings, goals, memories, encounter) |
| `{nodeId}` | `PoolNodeState` | Pool node health and capacity |
| `_node_index` | `List<string>` | All registered pool node IDs |
| `{actorId}` | `ActorAssignment` | Actor-to-node mapping |
| `_actor_index` | `ActorIndex` | Node-to-actor-set mapping for pool queries |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `actor-template.created` | `ActorTemplateCreatedEvent` | Template created |
| `actor-template.updated` | `ActorTemplateUpdatedEvent` | Template modified |
| `actor-template.deleted` | `ActorTemplateDeletedEvent` | Template deleted |
| `actor-instance.created` | `ActorInstanceCreatedEvent` | Actor spawned |
| `actor-instance.started` | `ActorInstanceStartedEvent` | ActorRunner started behavior loop |
| `actor-instance.deleted` | `ActorInstanceDeletedEvent` | Actor stopped |
| `actor-instance.state-persisted` | `ActorStatePersistedEvent` | Periodic state save |
| `actor.encounter.started` | `ActorEncounterStartedEvent` | Encounter begun |
| `actor.encounter.phase-changed` | `ActorEncounterPhaseChangedEvent` | Encounter phase transition |
| `actor.encounter.ended` | `ActorEncounterEndedEvent` | Encounter finished |
| `character.state_update` | `CharacterStateUpdateEvent` | Actor publishes feelings/goals to character |
| `actor.pool-node.registered` | `PoolNodeRegisteredEvent` | Pool node came online |
| `actor.pool-node.heartbeat` | `PoolNodeHeartbeatEvent` | Pool node health update |
| `actor.pool-node.draining` | `PoolNodeDrainingEvent` | Pool node shutting down |
| `actor.instance.status-changed` | `ActorStatusChangedEvent` | Actor status transition |
| `actor.instance.completed` | `ActorCompletedEvent` | Actor finished execution |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `session.disconnected` | `SessionDisconnectedEvent` | Stubbed handler for future session-bound actors |
| `actor.pool-node.registered` | `PoolNodeRegisteredEvent` | Register node, track capacity (control plane only) |
| `actor.pool-node.heartbeat` | `PoolNodeHeartbeatEvent` | Update load, mark healthy (control plane only) |
| `actor.pool-node.draining` | `PoolNodeDrainingEvent` | Mark draining, track remaining (control plane only) |
| `actor.instance.status-changed` | `ActorStatusChangedEvent` | Update assignment status (control plane only) |
| `actor.instance.completed` | `ActorCompletedEvent` | Remove assignment (control plane only) |
| `actor-template.updated` | `ActorTemplateUpdatedEvent` | Invalidate behavior cache and signal running actors when BehaviorRef changes |

**Note**: `behavior.updated` handling was moved to lib-puppetmaster (L4) per issue #380. Puppetmaster owns the dynamic behavior cache and notifies running actors via `IActorClient`. Actor (L2) no longer subscribes to any L4 events.

---

## Configuration

### Deployment Mode

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DeploymentMode` | `ACTOR_DEPLOYMENT_MODE` | `bannou` | Runtime mode: bannou/pool-per-type/shared-pool/auto-scale |

### Pool Node Identity

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `PoolNodeId` | `ACTOR_POOL_NODE_ID` | `null` | This node's unique ID (null = not a pool node) |
| `PoolNodeAppId` | `ACTOR_POOL_NODE_APP_ID` | `null` | Mesh routing app ID for this node |
| `PoolNodeType` | `ACTOR_POOL_NODE_TYPE` | `shared` | Pool specialization (shared/npc-brain/custom) |
| `PoolNodeCapacity` | `ACTOR_POOL_NODE_CAPACITY` | `100` | Max actors this node can run |

### Pool Management (Control Plane)

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `PoolNodeImage` | `ACTOR_POOL_NODE_IMAGE` | `bannou-actor-pool:latest` | Container image for pool nodes |
| `MinPoolNodes` | `ACTOR_MIN_POOL_NODES` | `1` | Minimum pool size |
| `MaxPoolNodes` | `ACTOR_MAX_POOL_NODES` | `10` | Maximum pool size |
| `DefaultActorsPerNode` | `ACTOR_DEFAULT_ACTORS_PER_NODE` | `100` | Default capacity per node |

### Local Mode

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `LocalModeNodeId` | `ACTOR_LOCAL_MODE_NODE_ID` | `bannou-local` | Node ID for local/bannou deployment mode |
| `LocalModeAppId` | `ACTOR_LOCAL_MODE_APP_ID` | `bannou` | App ID for local/bannou deployment mode |

### Behavior Loop

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultTickIntervalMs` | `ACTOR_DEFAULT_TICK_INTERVAL_MS` | `100` | Behavior loop tick frequency |
| `DefaultAutoSaveIntervalSeconds` | `ACTOR_DEFAULT_AUTOSAVE_INTERVAL_SECONDS` | `60` | State persistence interval |
| `PerceptionQueueSize` | `ACTOR_PERCEPTION_QUEUE_SIZE` | `100` | Bounded perception channel capacity |
| `ErrorRetryDelayMs` | `ACTOR_ERROR_RETRY_DELAY_MS` | `1000` | Delay before retry after behavior loop error |

### GOAP Planning

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `GoapReplanThreshold` | `ACTOR_GOAP_REPLAN_THRESHOLD` | `0.3` | World state delta triggering replan |
| `GoapMaxPlanDepth` | `ACTOR_GOAP_MAX_PLAN_DEPTH` | `10` | Maximum action chain length |
| `GoapPlanTimeoutMs` | `ACTOR_GOAP_PLAN_TIMEOUT_MS` | `50` | Planning time budget |

### Perception & Memory

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `PerceptionFilterThreshold` | `ACTOR_PERCEPTION_FILTER_THRESHOLD` | `0.1` | Minimum urgency to process |
| `PerceptionMemoryThreshold` | `ACTOR_PERCEPTION_MEMORY_THRESHOLD` | `0.7` | Minimum urgency to store as memory |
| `ShortTermMemoryMinutes` | `ACTOR_SHORT_TERM_MEMORY_MINUTES` | `5` | High-urgency memory TTL |
| `DefaultMemoryExpirationMinutes` | `ACTOR_DEFAULT_MEMORY_EXPIRATION_MINUTES` | `60` | General memory TTL |
| `MemoryStoreMaxRetries` | `ACTOR_MEMORY_STORE_MAX_RETRIES` | `3` | Max retries for memory store operations |

### Pool Health Monitoring

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `HeartbeatIntervalSeconds` | `ACTOR_HEARTBEAT_INTERVAL_SECONDS` | `10` | Pool node heartbeat frequency |
| `HeartbeatTimeoutSeconds` | `ACTOR_HEARTBEAT_TIMEOUT_SECONDS` | `30` | Mark unhealthy after silence |
| `PoolHealthMonitorStartupDelaySeconds` | `ACTOR_POOL_HEALTH_MONITOR_STARTUP_DELAY_SECONDS` | `5` | Delay before monitoring starts |
| `PoolHealthCheckIntervalSeconds` | `ACTOR_POOL_HEALTH_CHECK_INTERVAL_SECONDS` | `15` | Health check cycle interval |

### Timeouts & Retries

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ActorOperationTimeoutSeconds` | `ACTOR_OPERATION_TIMEOUT_SECONDS` | `5` | Timeout for individual actor operations |
| `ActorStopTimeoutSeconds` | `ACTOR_STOP_TIMEOUT_SECONDS` | `5` | Timeout for graceful actor stop |
| `StatePersistenceRetryDelayMs` | `ACTOR_STATE_PERSISTENCE_RETRY_DELAY_MS` | `50` | Base delay between state persistence retries (multiplied by attempt) |

### Scheduled Events

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ScheduledEventCheckIntervalMilliseconds` | `ACTOR_SCHEDULED_EVENT_CHECK_INTERVAL_MS` | `100` | Interval for checking scheduled events |
| `ScheduledEventDefaultUrgency` | `ACTOR_SCHEDULED_EVENT_DEFAULT_URGENCY` | `0.7` | Default urgency for scheduled event perceptions |
| `EventBrainDefaultUrgency` | `ACTOR_EVENT_BRAIN_DEFAULT_URGENCY` | `0.8` | Default urgency for Event Brain instruction perceptions |

### Caching

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `QueryOptionsDefaultMaxAgeMs` | `ACTOR_QUERY_OPTIONS_DEFAULT_MAX_AGE_MS` | `5000` | Max age for cached query options |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<ActorService>` | Scoped | Structured logging |
| `ActorServiceConfiguration` | Singleton | 30+ config properties |
| `IStateStoreFactory` | Singleton | 4 state store access |
| `IMessageBus` | Scoped | Event publishing, pool commands |
| `IEventConsumer` | Scoped | Cache invalidation, pool events |
| `ActorRunnerFactory` | Singleton | Creates ActorRunner instances |
| `ActorRegistry` | Singleton | Local actor instance tracking |
| `IBehaviorDocumentLoader` | Singleton | Loads behavior documents via provider chain |
| `SeededBehaviorProvider` | Singleton | Loads static behaviors from filesystem (Priority 50) |
| `FallbackBehaviorProvider` | Singleton | Returns null for missing behaviors after logging warning (Priority 0) |
| `ActorPoolManager` | Singleton | Pool node management (control plane) |
| `ActorPoolNodeWorker` | Hosted (BackgroundService) | Pool node command listener |
| `HeartbeatEmitter` | Singleton | Pool node heartbeat publisher (started/stopped by ActorPoolNodeWorker) |
| `PoolHealthMonitor` | Hosted (BackgroundService) | Pool node health checking |

Service lifetime is **Scoped** for ActorService. Multiple BackgroundServices for pool operations.

**Behavior Document Provider Chain**: `IBehaviorDocumentLoader` aggregates all registered `IBehaviorDocumentProvider` implementations, sorted by priority (descending). When loading a behavior ref, it queries providers in order until one returns a document. This enables lib-puppetmaster to inject dynamic scene-specific behaviors (Priority 100) without Actor having a compile-time dependency.

---

## API Endpoints (Implementation Notes)

### Template Management (5 endpoints)

- **CreateActorTemplate** (`/actor/template/create`): Creates template with category, behaviorRef, autoSpawn config, tick interval, auto-save interval. Updates template index (optimistic concurrency). Publishes `actor-template.created`.
- **GetActorTemplate** (`/actor/template/get`): Lookup by template ID or category. Category lookup enables convention-based actor spawning.
- **ListActorTemplates** (`/actor/template/list`): Loads all template IDs from index, fetches each. Optional category filter.
- **UpdateActorTemplate** (`/actor/template/update`): Partial update with `changedFields` tracking. Publishes `actor-template.updated`.
- **DeleteActorTemplate** (`/actor/template/delete`): Removes from index. Publishes `actor-template.deleted`.

### Actor Lifecycle (5 endpoints)

- **SpawnActor** (`/actor/spawn`): In bannou mode: creates ActorRunner locally, registers in ActorRegistry, starts behavior loop. In pool mode: acquires least-loaded node via ActorPoolManager, publishes SpawnActorCommand to node's command topic. Publishes `actor-instance.created`.
- **StopActor** (`/actor/stop`): In bannou mode: stops local runner, removes from registry. In pool mode: publishes StopActorCommand to assigned node. Publishes `actor-instance.deleted`.
- **GetActor** (`/actor/get`): Returns actor state snapshot. If actor not running but matches an auto-spawn template (regex pattern), automatically spawns it (instantiate-on-access pattern).
- **CleanupByCharacter** (`/actor/cleanup-by-character`): Called by lib-resource cleanup coordination when a character is deleted. Stops and removes all actors referencing the specified characterId. Returns count of actors cleaned up. Part of x-references cascade pattern.
- **InjectPerception** (`/actor/inject-perception`): Enqueues perception data into actor's bounded channel. Returns current queue depth. Developer-only for testing.

### Encounter Management (4 endpoints)

- **StartEncounter** (`/actor/encounter/start`): Starts encounter on actor with participants, phase, and initial data. Supports remote forwarding via mesh for distributed actors. Publishes `actor.encounter.started`.
- **UpdateEncounterPhase** (`/actor/encounter/update-phase`): Transitions encounter phase (string-based, no validation). Publishes `actor.encounter.phase-changed`.
- **EndEncounter** (`/actor/encounter/end`): Cleans up encounter state. Publishes `actor.encounter.ended`.
- **GetEncounter** (`/actor/encounter/get`): Returns current encounter state for an actor.

### Query Operations (2 endpoints)

- **ListActors** (`/actor/list`): Lists actors from registry (bannou mode) or assignments (pool mode). Optional category/status filters.
- **QueryOptions** (`/actor/query-options`): Retrieves evaluated ABML options from actor state. Three freshness levels: `Cached` (immediate from memory), `Fresh` (inject perception + wait one tick), `Stale` (any value regardless of age). Returns combat/dialogue/custom options with preference scores.

---

## Visual Aid

```
Deployment Modes
==================

  BANNOU MODE (local, development):
  ┌─────────────────────────────────────────────────────────┐
  │  ActorService (main process)                            │
  │       │                                                  │
  │       ├── ActorRegistry (ConcurrentDictionary)          │
  │       │    ├── actor-1 → ActorRunner                    │
  │       │    ├── actor-2 → ActorRunner                    │
  │       │    └── actor-3 → ActorRunner                    │
  │       │                                                  │
  │       └── Direct spawn/stop (no network)                │
  └─────────────────────────────────────────────────────────┘

  POOL MODE (distributed, production):
  ┌─────────────────────────────────────────────────────────┐
  │  Control Plane (ActorService + ActorPoolManager)        │
  │       │                                                  │
  │       ├── AcquireNode → least-loaded pool node          │
  │       ├── Publish: actor.node.{appId}.spawn             │
  │       └── Track: actor-assignments store                │
  └──────────────────────┬──────────────────────────────────┘
                         │ RabbitMQ command topics
          ┌──────────────┼──────────────┐
          ▼              ▼              ▼
  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
  │ Pool Node A │ │ Pool Node B │ │ Pool Node C │
  │ (cap: 100)  │ │ (cap: 100)  │ │ (cap: 100)  │
  │             │ │             │ │             │
  │ Actors:     │ │ Actors:     │ │ Actors:     │
  │  runner-1   │ │  runner-4   │ │  runner-7   │
  │  runner-2   │ │  runner-5   │ │  runner-8   │
  │  runner-3   │ │  runner-6   │ │             │
  │             │ │             │ │             │
  │ Heartbeat ──┼─┼─► control   │ │ Heartbeat ──┤
  └─────────────┘ └─────────────┘ └─────────────┘


ActorRunner Behavior Loop
============================

  StartAsync()
       │
       └── RunBehaviorLoopAsync() [while !cancelled]
                │
                ├── 1. ProcessPerceptionsAsync()
                │    └── Drain perception queue → working memory
                │         ├── urgency < FilterThreshold → drop
                │         └── urgency ≥ MemoryThreshold → store as memory
                │
                ├── 2. ExecuteBehaviorTickAsync()
                │    ├── Load ABML document (cached, hot-reloadable)
                │    ├── Build execution scope:
                │    │    ├── agent: {id, behavior_id, character_id, category}
                │    │    ├── feelings: {joy: 0.5, anger: 0.2, ...}
                │    │    ├── goals: {primary, secondary[], parameters{}}
                │    │    ├── memories: {key → value (with TTL)}
                │    │    ├── working_memory: {perception:type:source → data}
                │    │    ├── personality: {traits, combat_style, risk}
                │    │    └── backstory: {elements[]}
                │    │
                │    └── Execute flow: process_tick (preferred) or main
                │
                ├── 3. PublishStateUpdateIfNeededAsync()
                │    └── If CharacterId set: publish character.state_update
                │
                ├── 4. PeriodicPersistence()
                │    └── If AutoSaveInterval exceeded: save state snapshot
                │
                ├── 5. CleanupExpiredMemories()
                │    └── Remove memories past ExpiresAt
                │
                └── 6. Sleep(TickInterval - elapsed)


Auto-Spawn Pattern
====================

  GetActor(actorId="character-abc123-npc-blacksmith")
       │
       ├── Check registry/assignments → not found
       │
       ├── FindAutoSpawnTemplateAsync(actorId)
       │    ├── For each template with AutoSpawn.Enabled:
       │    │    ├── Match regex: "character-(?<charid>[0-9a-f-]+)-.*"
       │    │    └── Extract CharacterId from capture group
       │    └── Return: (template, characterId=abc123)
       │
       └── SpawnActor(template, actorId, characterId)
            └── Actor starts running immediately


Perception Processing
=======================

  InjectPerception(actorId, type="player_nearby", urgency=0.8)
       │
       ├── Find actor (local registry or remote node)
       │
       ├── Enqueue to bounded channel:
       │    Channel<PerceptionData>(size=100, DropOldest)
       │         │
       │         ├── urgency < 0.1 → dropped (below filter threshold)
       │         ├── urgency ≥ 0.7 → stored as short-term memory (5 min TTL)
       │         └── 0.1 ≤ urgency < 0.7 → working memory only (ephemeral)
       │
       └── Next tick: perception influences behavior execution


Encounter Lifecycle (Event Brain)
====================================

  StartEncounter(actorId, participants, phase="initializing")
       │
       ├── Set actor.encounter = { id, participants, phase, data }
       ├── Publish: actor.encounter.started
       │
       ▼ (Event Brain behavior loop manages phases)
  UpdatePhase(actorId, phase="combat")
       │
       ├── actor.encounter.phase = "combat"
       ├── Publish: actor.encounter.phase-changed
       │
       ▼ (conditions met)
  EndEncounter(actorId, outcome="victory")
       │
       ├── Clear actor.encounter
       └── Publish: actor.encounter.ended


Actor State Model
===================

  ActorState
  ├── Feelings: Dict<string, double> [0-1]
  │    ├── joy, sadness, anger, fear
  │    ├── surprise, trust, disgust
  │    └── anticipation
  │
  ├── Goals
  │    ├── PrimaryGoal: string
  │    ├── SecondaryGoals: List<string>
  │    └── GoalParameters: Dict<string, object>
  │
  ├── Memories: Dict<string, MemoryEntry>
  │    ├── Key → { Value, ExpiresAt }
  │    └── TTL-based cleanup each tick
  │
  ├── WorkingMemory: Dict<string, object>
  │    └── Ephemeral per-tick data
  │
  └── Encounter (optional)
       ├── EncounterId, EncounterType
       ├── Participants, Phase
       ├── StartedAt
       └── Data: Dict<string, object?>
```

---

## Stubs & Unimplemented Features

1. **Session disconnection handling**: `HandleSessionDisconnectedAsync` is stubbed. Actors are not tied to player sessions - NPC brains continue running when players disconnect. Future: session-bound actors that stop on disconnect.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/191 -->
2. **Auto-scale deployment mode**: Declared as a valid `DeploymentMode` value but no auto-scaling logic is implemented. Pool nodes must be manually managed or pre-provisioned.
<!-- AUDIT:NEEDS_DESIGN:2026-02-07:https://github.com/beyond-immersion/bannou-service/issues/318 -->

---

## Potential Extensions

1. **Session-bound actors**: Actors that stop when their controlling player session disconnects. (See Stubs #1 and issue #191)
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/191 -->
2. **Memory decay**: Gradual relevance decay for memories over time (not just TTL expiry).
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/387 -->
3. **Cross-node encounters**: Encounter coordination across multiple pool nodes for large-scale events.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/390 -->
4. **Behavior versioning**: Deploy behavior updates with version tracking, enabling rollback without service restart.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/391 -->
5. **Actor migration**: Move running actors between pool nodes for load balancing without state loss.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/393 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks

1. **Auto-spawn with regex pattern**: Templates with `AutoSpawn.Enabled=true` define a regex pattern. When `GetActor` is called for a non-existent actor, all auto-spawn templates are checked. If the actor ID matches a pattern, the actor is automatically spawned.

2. **Perception queue DropOldest**: When the queue is full, the oldest unprocessed perception is discarded. This prioritizes recent stimuli over stale ones.

3. **Error recovery in behavior loop**: If the behavior tick throws, the actor enters `Error` status for 1 second, then resumes `Running`. Self-healing prevents permanent actor death from transient failures.

4. **Encounter event publishing uses discard pattern**: `StartEncounter`/`SetEncounterPhase`/`EndEncounter` use `_ = _messageBus.TryPublishAsync(...)` because the `IActorRunner` interface defines these methods as **synchronous** (`bool` return type). Changing to `Task<bool>` would be a breaking interface change. Note that `TryPublishAsync` still buffers and retries internally if RabbitMQ is unavailable - events WILL be delivered eventually (see MESSAGING.md Quirk #1). The discard means the caller can't await completion or check the return value, but the underlying retry mechanism ensures delivery.

5. **ScheduledEventManager uses in-memory state**: Pending scheduled events stored in `ConcurrentDictionary`, not backed by Redis. Each pool node has its own `ScheduledEventManager` Singleton. This is intentional: scheduled events are ephemeral timers (milliseconds-to-seconds delays) created as side effects of ABML `schedule_event:` action execution. Each actor runs on exactly one node, so its events fire locally. The fired events publish to RabbitMQ for distributed delivery. If a node crashes, the actor itself must be restarted and the behavior loop re-evaluates and re-schedules events as needed.

6. **ActorRegistry is instance-local for bannou mode**: Pool mode uses Redis-backed `ActorPoolManager`, but bannou mode uses in-memory `ConcurrentDictionary`. Bannou mode is designed for single-instance operation — running multiple bannou-mode instances would cause inconsistent actor tracking. Use pool mode for multi-instance deployments.

7. **ScheduledEventManager Timer uses fire-and-forget**: Timer callback `CheckEvents` is synchronous (Timer constraint), so it calls `_ = FireEventAsync(evt)` for due events. `FireEventAsync` wraps all logic in `try-catch(Exception)` with `_logger.LogError`, so exceptions ARE observed and logged. `TryPublishAsync` provides retry buffering for RabbitMQ delivery. The discard means the timer callback can't await completion, but the async method is self-contained with proper error handling.

8. **Single-threaded behavior loop by design**: Each ActorRunner runs its behavior loop on a single task (sequential: perceptions → behavior tick → state publish → persistence → sleep). CPU-intensive behaviors delay tick processing but do not block other actors. `GoapPlanTimeoutMs` (50ms default) is passed to the GOAP planner as a budget hint via the behavior scope — it is NOT enforced as a hard timeout by ActorRunner. If planning exceeds the budget, the tick runs long and the sleep phase is skipped to compensate. This design simplifies state management (no concurrent access to actor state) and matches the one-brain-per-NPC model.

9. **State persistence is a periodic snapshot, not the real-time path**: Actor state is persisted to Redis every `AutoSaveIntervalSeconds` (60s default) to balance write load against recovery granularity. During normal operation, all state changes publish `character.state_update` events immediately (every tick, ~100ms), so game servers see real-time updates. On graceful shutdown, state is persisted immediately. In crash scenarios, up to 60 seconds of unpersisted snapshot data may be lost, but gameplay-visible state was already broadcast via events. Feelings/goals re-stabilize quickly after actor respawn, and critical game state (inventory, quests, combat outcomes) is managed by other services.

10. **Per-actor RabbitMQ subscription for perceptions**: Each actor with a `CharacterId` creates a dynamic RabbitMQ subscription via `SubscribeDynamicAsync` on topic `character.{characterId}.perceptions`. Game servers and other actors publish perceptions to these character-specific topics via the ABML `emit_perception:` action. At scale (100,000+ actors), this means 100,000+ RabbitMQ subscriptions. This is intentional: targeted per-character topics enable efficient perception routing without the actor needing to filter irrelevant perceptions from a shared topic. Pool mode distributes subscriptions across nodes (e.g., 10 nodes × 10,000 subscriptions each). The `InjectPerception` API provides a direct injection alternative for testing. Subscriptions are disposed on actor stop.

11. **Per-tick memory cleanup via linear scan**: `ActorState.CleanupExpiredMemories()` runs every behavior loop tick (~100ms) and performs `List.RemoveAll()` on the `_memories` list under a lock, removing entries where `ExpiresAt <= now`. Permanent memories (`ExpiresAt = null`) are never removed. This is a simple O(n) scan per tick, acceptable for typical actor populations (hundreds of memories per actor). At extreme scale (10,000+ memories per actor), this could become a bottleneck and would benefit from a sorted expiration index. Working memory (`SetWorkingMemory`) persists across ticks — new perceptions overwrite by key but no explicit clearing occurs between ticks.

12. **Encounter phase strings are intentionally unvalidated**: `SetEncounterPhase(string phase)` accepts any string without validation or state machine enforcement. `StartEncounter` sets the initial phase to `"initializing"`. Phase names and valid transitions are defined by ABML behavior scripts per encounter type — the Actor service (L2) is intentionally agnostic to encounter-type-specific phase semantics. Adding server-side phase validation would couple Actor to specific encounter type definitions, violating the extensibility model where new encounter types (with their own phases) can be authored in ABML without schema or code changes. Callers (Puppetmaster, game servers) are responsible for passing meaningful phase strings. Invalid phase strings are accepted silently because "invalid" is relative to the encounter type's ABML behavior, not to Actor.

13. **Fresh options query uses approximate tick wait**: `QueryOptionsAsync` with `Freshness=Fresh` injects a perception into the actor's queue and then calls `Task.Delay(DefaultTickIntervalMs)` (~100ms) before re-reading state. This does NOT synchronize with the actual behavior loop — the delay may finish before or after the next tick processes the perception. This is intentional: options queries are best-effort decision-support, not precision-critical operations. The three freshness levels (`Cached`/`Fresh`/`Stale`) already give callers control over timing expectations. Adding a synchronization primitive (e.g., `TaskCompletionSource` signaled on tick completion) would add contention to the behavior loop hot path (~100ms per tick per actor × 100,000+ actors) for a marginal improvement in query timing. If the perception wasn't processed in one tick, the caller can query again with `Fresh` or fall back to `Stale`.

14. **Auto-spawn failure returns NotFound to caller**: When `GetActor` triggers auto-spawn and `SpawnActorAsync` fails (Conflict, ServiceUnavailable, etc.), `GetActorAsync` returns `NotFound` — not the real failure status. This is intentional: auto-spawn is an implementation detail invisible to the caller. From the caller's perspective, they asked "get actor X" and the answer is "it doesn't exist." Returning `Conflict` or `ServiceUnavailable` would leak the auto-spawn mechanism. The real failure status and reason are logged at Warning level server-side for debugging. **Edge case**: If `SpawnActorAsync` returns `Conflict` because a concurrent `GetActor` call just auto-spawned the same actor, the caller gets `NotFound` even though the actor now exists. A retry by the caller would succeed. If this becomes a problem at scale (high-contention auto-spawn for the same actor ID), a retry-on-conflict loop could be added inside `GetActorAsync`.

### Design Considerations (Requires Planning)

1. ~~**ActorRunner._encounter field lacks synchronization**~~: **FIXED** (2026-02-11) - All `_encounter` access points now use local variable capture to prevent TOCTOU NullReferenceException. Reference reads are atomic per ECMA-334; capturing to a local before accessing properties prevents the race between EndEncounter (nulling `_encounter`) and the behavior loop (reading encounter properties). No lock needed — the pattern avoids contention on the behavior loop hot path.

2. **Pool node capacity is self-reported**: No external validation of claimed capacity.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/394 -->

3. ~~**Perception subscription per-character**~~: **MOVED TO QUIRK #10** (2026-02-11) - Moved to Intentional Quirks with expanded documentation covering per-actor `SubscribeDynamicAsync` pattern, topic format, pool mode distribution, and testing alternative.

4. ~~**Memory cleanup is per-tick**~~: **MOVED TO QUIRK #11** (2026-02-11) - Moved to Intentional Quirks with corrected documentation. Original claimed "Working memory cleared between perceptions" which is inaccurate — working memory persists across ticks, new perceptions overwrite by key.

5. ~~**Template index optimistic concurrency**~~: **FIXED** (2026-02-11) - Template index updates (create/delete) now retry up to 3 times on optimistic concurrency conflict. Each retry re-reads the index to resolve the conflict. Without this, concurrent template creates/deletes could permanently orphan templates from the index, breaking list and auto-spawn operations.

6. ~~**Encounter phase strings unvalidated**~~: **MOVED TO QUIRK #12** (2026-02-11) - Moved to Intentional Quirks with expanded documentation explaining why phases are intentionally unvalidated (ABML behavior scripts define encounter-type-specific phase semantics).

7. ~~**Fresh options query waits approximately one tick**~~: **MOVED TO QUIRK #13** (2026-02-11) - Moved to Intentional Quirks with expanded documentation explaining why the approximate wait is acceptable and what the alternatives would be.

8. ~~**Auto-spawn failure returns NotFound**~~: **MOVED TO QUIRK #14** (2026-02-11) - Moved to Intentional Quirks with documentation explaining why NotFound is the correct response from the caller's perspective.

9. ~~**ListActors nodeId filter not implemented**~~: **FIXED** (2026-02-11) - Bannou mode now compares nodeId against LocalModeNodeId (returns empty on mismatch). Pool mode queries `IActorPoolManager.ListActorsByNodeAsync` with category/status/characterId filtering. Also fixed T23 violation (method is now async).

---

## Work Tracking

### Completed

- (2026-02-11) Fixed template index optimistic concurrency: added retry loop (3 attempts) to `UpdateTemplateIndexAsync` helper for both create and delete paths. Without retry, concurrent template mutations could permanently orphan templates from the index.
- (2026-02-11) Moved "Memory cleanup is per-tick" from Design Considerations to Intentional Quirks (#11) with corrected documentation. Fixed inaccuracy: working memory is NOT cleared between perceptions (persists across ticks, overwrites by key).
- (2026-02-11) Moved "Perception subscription per-character" from Design Considerations to Intentional Quirks (#10) with expanded documentation covering per-actor SubscribeDynamicAsync pattern, topic format, pool mode distribution, and testing alternative.
- (2026-02-11) Moved "Single-threaded behavior loop" from Design Considerations to Intentional Quirks (#8) with improved documentation clarifying GoapPlanTimeoutMs is a budget hint, not an enforced timeout.
- (2026-02-11) Moved "State persistence is periodic" from Design Considerations to Intentional Quirks (#9) with expanded documentation covering event-driven real-time path, graceful shutdown persistence, and crash recovery implications.
- (2026-02-11) Moved "Encounter phase strings unvalidated" from Design Considerations to Intentional Quirks (#12) with expanded documentation explaining ABML-driven phase semantics and why server-side validation would violate the extensibility model.
- (2026-02-11) Moved "Fresh options query waits approximately one tick" from Design Considerations to Intentional Quirks (#13) with expanded documentation covering why the approximate wait is acceptable, alternatives considered (TaskCompletionSource synchronization), and the caller's ability to use freshness levels or re-query.
- (2026-02-11) Moved "Auto-spawn failure returns NotFound" from Design Considerations to Intentional Quirks (#14) with documentation explaining why NotFound is the correct caller-facing response (auto-spawn is an implementation detail) and noting the concurrent auto-spawn edge case.
- (2026-02-11) Fixed missing behavior cache invalidation on template update (issue #391 side finding). Added `actor-template.updated` event subscription. Handler checks `ChangedFields` for `behaviorRef`, invalidates `BehaviorDocumentLoader` provider caches, and signals running actors on this node via `IActorRunner.InvalidateCachedBehavior()` to reload on next tick. This fixes hot-reload across all nodes (each node receives the event via RabbitMQ and invalidates locally).

### Implementation Gaps

No implementation gaps identified requiring AUDIT markers.
