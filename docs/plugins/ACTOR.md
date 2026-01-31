# Actor Plugin Deep Dive

> **Plugin**: lib-actor
> **Schema**: schemas/actor-api.yaml
> **Version**: 1.0.0
> **State Stores**: actor-templates (Redis/MySQL), actor-state (Redis), actor-pool-nodes (Redis), actor-assignments (Redis)

---

## Overview

Distributed actor management and execution for NPC brains, event coordinators, and long-running behavior loops. Actors output behavioral state (feelings, goals, memories) to characters - NOT directly visible to players. Features multiple deployment modes (local `bannou`, `pool-per-type`, `shared-pool`, `auto-scale`), ABML behavior document execution with hot-reload, GOAP planning integration, bounded perception queues with urgency filtering, encounter management for Event Brain actors, and pool-based distributed execution with heartbeat monitoring. The runtime (ActorRunner) executes configurable tick-based behavior loops with periodic state persistence and character state publishing.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for actor state, templates, pool nodes, assignments |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events, state updates, pool commands; error publishing |
| lib-messaging (`IEventConsumer`) | Behavior/personality cache invalidation, pool node management events |
| lib-behavior (`IBehaviorClient`) | Loading compiled ABML behavior documents |
| lib-character-personality (`ICharacterPersonalityClient`) | Loading personality traits for behavior context |
| lib-character-history (`ICharacterHistoryClient`) | Loading backstory for behavior context |
| lib-asset (`IAssetClient`) | Fetching behavior YAML documents via pre-signed URLs |
| lib-character-encounter (`ICharacterEncounterClient`) | Loading encounter history, sentiment, and has-met data |
| lib-mesh (`IMeshInvocationClient`) | Forwarding requests to remote pool nodes in distributed mode |

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
| `behavior.updated` | `BehaviorUpdatedEvent` | Invalidates behavior cache, notifies actors for hot-reload |
| `personality.evolved` | `PersonalityEvolvedEvent` | Invalidates personality cache for character |
| `combat-preferences.evolved` | `CombatPreferencesEvolvedEvent` | Invalidates personality cache |
| `actor.pool-node.registered` | `PoolNodeRegisteredEvent` | Register node, track capacity (control plane only) |
| `actor.pool-node.heartbeat` | `PoolNodeHeartbeatEvent` | Update load, mark healthy (control plane only) |
| `actor.pool-node.draining` | `PoolNodeDrainingEvent` | Mark draining, track remaining (control plane only) |
| `actor.instance.status-changed` | `ActorStatusChangedEvent` | Update assignment status (control plane only) |
| `actor.instance.completed` | `ActorCompletedEvent` | Remove assignment (control plane only) |

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
| `PersonalityCacheTtlMinutes` | `ACTOR_PERSONALITY_CACHE_TTL_MINUTES` | `5` | Personality data cache lifetime |
| `EncounterCacheTtlMinutes` | `ACTOR_ENCOUNTER_CACHE_TTL_MINUTES` | `5` | Encounter data cache lifetime |
| `MaxEncounterResultsPerQuery` | `ACTOR_MAX_ENCOUNTER_RESULTS_PER_QUERY` | `50` | Query result limit |
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
| `ActorRunnerFactory` | Scoped | Creates ActorRunner instances |
| `ActorRegistry` | Singleton | Local actor instance tracking |
| `BehaviorDocumentCache` | Singleton | Compiled ABML document caching |
| `PersonalityCache` | Singleton | Character personality caching |
| `ActorPoolManager` | Singleton | Pool node management (control plane) |
| `ActorPoolNodeWorker` | Hosted (BackgroundService) | Pool node command listener |
| `HeartbeatEmitter` | Hosted (BackgroundService) | Pool node heartbeat publisher |
| `PoolHealthMonitor` | Hosted (BackgroundService) | Pool node health checking |

Service lifetime is **Scoped** for ActorService. Multiple BackgroundServices for pool operations.

---

## API Endpoints (Implementation Notes)

### Template Management (5 endpoints)

- **CreateActorTemplate** (`/actor/template/create`): Creates template with category, behaviorRef, autoSpawn config, tick interval, auto-save interval. Updates template index (optimistic concurrency). Publishes `actor-template.created`.
- **GetActorTemplate** (`/actor/template/get`): Lookup by template ID or category. Category lookup enables convention-based actor spawning.
- **ListActorTemplates** (`/actor/template/list`): Loads all template IDs from index, fetches each. Optional category filter.
- **UpdateActorTemplate** (`/actor/template/update`): Partial update with `changedFields` tracking. Publishes `actor-template.updated`.
- **DeleteActorTemplate** (`/actor/template/delete`): Removes from index. Publishes `actor-template.deleted`.

### Actor Lifecycle (4 endpoints)

- **SpawnActor** (`/actor/spawn`): In bannou mode: creates ActorRunner locally, registers in ActorRegistry, starts behavior loop. In pool mode: acquires least-loaded node via ActorPoolManager, publishes SpawnActorCommand to node's command topic. Publishes `actor-instance.created`.
- **StopActor** (`/actor/stop`): In bannou mode: stops local runner, removes from registry. In pool mode: publishes StopActorCommand to assigned node. Publishes `actor-instance.deleted`.
- **GetActor** (`/actor/get`): Returns actor state snapshot. If actor not running but matches an auto-spawn template (regex pattern), automatically spawns it (instantiate-on-access pattern).
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
                │    │    ├── goals: {primary, secondary[], relevance{}}
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
  ├── Feelings: Dict<string, float> [0-1]
  │    ├── joy, sadness, anger, fear
  │    ├── surprise, trust, disgust
  │    └── anticipation
  │
  ├── Goals
  │    ├── PrimaryGoal: string
  │    ├── SecondaryGoals: List<string>
  │    └── GoalRelevance: Dict<string, float>
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
2. **GOAP integration partial**: GOAP configuration exists (replan threshold, max depth, timeout) but the full planning integration with the behavior loop is minimal. Planning triggers exist but action execution is delegated to ABML flows.
3. **Auto-scale deployment mode**: Declared as a valid `DeploymentMode` value but no auto-scaling logic is implemented. Pool nodes must be manually managed or pre-provisioned.

---

## Potential Extensions

1. **Session-bound actors**: Actors that stop when their controlling player session disconnects.
2. **Memory decay**: Gradual relevance decay for memories over time (not just TTL expiry).
3. **Cross-node encounters**: Encounter coordination across multiple pool nodes for large-scale events.
4. **Behavior versioning**: Deploy behavior updates with version tracking, enabling rollback without service restart.
5. **Actor migration**: Move running actors between pool nodes for load balancing without state loss.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks

1. **Auto-spawn with regex pattern**: Templates with `AutoSpawn.Enabled=true` define a regex pattern. When `GetActor` is called for a non-existent actor, all auto-spawn templates are checked. If the actor ID matches a pattern, the actor is automatically spawned.

2. **Perception queue DropOldest**: When the queue is full, the oldest unprocessed perception is discarded. This prioritizes recent stimuli over stale ones.

3. **Error recovery in behavior loop**: If the behavior tick throws, the actor enters `Error` status for 1 second, then resumes `Running`. Self-healing prevents permanent actor death from transient failures.

4. **Encounter event publishing uses discard pattern**: `StartEncounter`/`SetEncounterPhase`/`EndEncounter` use `_ = _messageBus.TryPublishAsync(...)` because the `IActorRunner` interface defines these methods as **synchronous** (`bool` return type). Changing to `Task<bool>` would be a breaking interface change. Note that `TryPublishAsync` still buffers and retries internally if RabbitMQ is unavailable - events WILL be delivered eventually (see MESSAGING.md Quirk #1). The discard means the caller can't await completion or check the return value, but the underlying retry mechanism ensures delivery.

### Design Considerations (Requires Planning)

1. **ScheduledEventManager uses in-memory state**: Pending scheduled events stored in `ConcurrentDictionary`. Multi-instance deployments would have divergent event queues. Acceptable for single-instance bannou mode; pool mode distributes actors across nodes.

2. **ActorRegistry is instance-local for bannou mode**: Pool mode uses Redis-backed `ActorPoolManager`, but bannou mode uses in-memory `ConcurrentDictionary`. Bannou mode is designed for single-instance operation.

3. **ScheduledEventManager Timer uses discard pattern**: `CheckEvents` callback discards Tasks via `_ = FireEventAsync()`. If `FireEventAsync` throws, the exception is unobserved (no crash, but no logging). This is a timer callback constraint - the callback must be synchronous. Note: if `FireEventAsync` uses `TryPublishAsync` internally, the retry buffer handles RabbitMQ failures; other exceptions would be lost.

4. **ActorRunner._encounter field lacks synchronization**: Field accessed from behavior loop thread and external callers without lock protection. `_statusLock` only protects `_status`.

5. **Single-threaded behavior loop**: Each ActorRunner runs on single task. CPU-intensive behaviors delay tick processing. `GoapPlanTimeoutMs` (50ms) provides budget.

6. **State persistence is periodic**: Saved every `AutoSaveIntervalSeconds` (60s default). Crash loses up to 60 seconds. Critical state publishes events immediately.

7. **Pool node capacity is self-reported**: No external validation of claimed capacity.

8. **Perception subscription per-character**: 100,000+ actors = 100,000+ RabbitMQ subscriptions. Pool mode distributes.

9. **Memory cleanup is per-tick**: Expired memories scanned each tick. Working memory cleared between perceptions.

10. **Template index optimistic concurrency**: No retry on conflict - returns immediately. Index may be temporarily inconsistent if TrySave fails.

11. **Encounter phase strings unvalidated**: No state machine. ABML behaviors enforce meaningful transitions.

12. **Fresh options query waits approximately one tick**: `Task.Delay(DefaultTickIntervalMs)` doesn't synchronize with actual behavior loop.

13. **Auto-spawn failure returns NotFound**: True failure reason only logged, not returned to caller.

14. **ListActors nodeId filter not implemented**: Parameter exists but ignored in all modes.

---

## Work Tracking

### AUDIT Markers

- **AUDIT[2026-01-31]: Schema-code mismatch for personality/combat-preferences event subscriptions**
  - **Status**: FIXED
  - **Issue**: `actor-events.yaml` x-event-subscriptions was missing `personality.evolved` and `combat-preferences.evolved` entries that were implemented in `ActorServiceEvents.cs`
  - **Fix**: Added missing event subscriptions to schema, regenerated service events
  - **Verification**: Build passes, schema now matches implementation

### Implementation Gaps

No implementation gaps identified requiring AUDIT markers.
