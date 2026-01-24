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

### Behavior Loop

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultTickIntervalMs` | `ACTOR_DEFAULT_TICK_INTERVAL_MS` | `100` | Behavior loop tick frequency |
| `DefaultAutoSaveIntervalSeconds` | `ACTOR_DEFAULT_AUTOSAVE_INTERVAL_SECONDS` | `60` | State persistence interval |
| `PerceptionQueueSize` | `ACTOR_PERCEPTION_QUEUE_SIZE` | `100` | Bounded perception channel capacity |

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

### Pool Health Monitoring

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `HeartbeatIntervalSeconds` | `ACTOR_HEARTBEAT_INTERVAL_SECONDS` | `10` | Pool node heartbeat frequency |
| `HeartbeatTimeoutSeconds` | `ACTOR_HEARTBEAT_TIMEOUT_SECONDS` | `30` | Mark unhealthy after silence |
| `PoolHealthMonitorStartupDelaySeconds` | `ACTOR_POOL_HEALTH_MONITOR_STARTUP_DELAY_SECONDS` | `5` | Delay before monitoring starts |
| `PoolHealthCheckIntervalSeconds` | `ACTOR_POOL_HEALTH_CHECK_INTERVAL_SECONDS` | `15` | Health check cycle interval |

### Caching

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `PersonalityCacheTtlMinutes` | `ACTOR_PERSONALITY_CACHE_TTL_MINUTES` | `5` | Personality data cache lifetime |
| `EncounterCacheTtlMinutes` | `ACTOR_ENCOUNTER_CACHE_TTL_MINUTES` | `5` | Encounter data cache lifetime |
| `MaxEncounterResultsPerQuery` | `ACTOR_MAX_ENCOUNTER_RESULTS_PER_QUERY` | `50` | Query result limit |

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

1. **[QUALITY] Tenet number references in source code (T0 violation)**: `IActorRegistry.cs` line 5 and `ActorRegistry.cs` line 7 both reference "T9" directly in XML documentation comments (`Uses ConcurrentDictionary internally per T9 (Multi-Instance Safety)`). Also `ActorServiceEvents.cs` line 113 references "(T5)" in a comment. Per T0, source code must use category names (IMPLEMENTATION TENETS, FOUNDATION TENETS) not specific tenet numbers.

2. **[IMPLEMENTATION] ScheduledEventManager uses in-memory authoritative state (T9 violation)**: `Handlers/ScheduleEventHandler.cs` lines 248-361. The `ScheduledEventManager` class stores pending scheduled events in a `ConcurrentDictionary<string, ScheduledEvent>` which is an in-memory-only store. In multi-instance deployments, scheduled events created on one instance would not be visible to other instances, and if the instance crashes, all pending events are lost. This should use distributed state (lib-state) for multi-instance safety.

3. **[IMPLEMENTATION] ActorRegistry is in-memory authoritative state for bannou mode (T9 concern)**: `Runtime/ActorRegistry.cs`. The entire `ActorRegistry` class stores actors in a `ConcurrentDictionary` which is instance-local. While the pool mode uses Redis-backed `ActorPoolManager` for distributed state, the local bannou mode treats this in-memory dictionary as authoritative. If multiple bannou instances run simultaneously (which the monoservice architecture allows), they would have divergent actor registries. The `ConcurrentDictionary` satisfies thread-safety but not multi-instance safety.

4. **[IMPLEMENTATION] Anonymous objects passed to TryPublishErrorAsync details parameter (T5 violation)**: Multiple locations use anonymous objects for the `details` parameter of `TryPublishErrorAsync`:
   - `ActorServiceEvents.cs` lines 142, 241, 275, 309, 343, 381
   - `PoolNode/ActorPoolNodeWorker.cs` lines 241, 308, 373, 453
   - `Runtime/ActorRunner.cs` lines 512, 608, 992, 1097
   - `Pool/PoolHealthMonitor.cs` lines 165-171
   Per FOUNDATION TENETS (T5), all events should use typed objects, not anonymous objects.

5. **[IMPLEMENTATION] Anonymous object used as memory value in ProcessPerceptionsAsync (T5 violation)**: `Runtime/ActorRunner.cs` line 563. An anonymous object `new { perception.SourceId, perception.SourceType, perception.Data, perception.Urgency }` is stored as a memory value. This creates an untyped anonymous object that will have serialization issues and violates the typed events requirement.

6. **[IMPLEMENTATION] Hardcoded magic numbers for tunables (T21 violation)**: Multiple hardcoded numeric values that should be configuration:
   - `Runtime/ActorRunner.cs` line 522: `Task.Delay(1000, ct)` - hardcoded 1-second error retry delay
   - `Runtime/ActorRunner.cs` line 979: `Task.Delay(50 * (attempt + 1), ct)` - hardcoded 50ms base retry delay for state persistence
   - `Runtime/ActorRunner.cs` line 249: `TimeSpan.FromSeconds(5)` - hardcoded 5-second stop timeout
   - `Handlers/ScheduleEventHandler.cs` line 335: `Urgency = 0.7f` - hardcoded urgency for scheduled events
   - `Handlers/EmitPerceptionHandler.cs` line 102: `0.8f` - hardcoded default urgency for Event Brain instructions
   - `Handlers/QueryOptionsHandler.cs` line 91: `maxAgeMs = 5000` - hardcoded default max age

7. **[IMPLEMENTATION] DeploymentMode compared as string instead of enum (T25 violation)**: Throughout the codebase, `_configuration.DeploymentMode` is a `string` type compared against magic string `"bannou"` in many locations:
   - `ActorService.cs` lines 527, 534, 548, 626, 642, 681, 686, 838, 872, 993, 1085
   - `ActorServiceEvents.cs` lines 215, 253, 287, 321, 355
   - `Pool/PoolHealthMonitor.cs` line 60
   This should be a proper enum type for internal model type safety per IMPLEMENTATION TENETS (T25). The DeploymentMode has well-known values (bannou, pool-per-type, shared-pool, auto-scale) that should be represented as an enum.

8. **[IMPLEMENTATION] ActorAssignment.TemplateId is string instead of Guid (T25 violation)**: `Pool/ActorAssignment.cs` line 35. The `TemplateId` property is declared as `string` but always stores a GUID value. Usage in `ActorService.cs` line 589 shows `TemplateId = body.TemplateId.ToString()` converting a Guid to string, and line 695 shows `Guid.TryParse(assignment.TemplateId, out var tid) ? tid : Guid.Empty` converting back. This should be a `Guid` type per T25.

9. **[IMPLEMENTATION] ScheduledEvent.Id is string instead of Guid (T25 violation)**: `Handlers/ScheduleEventHandler.cs` line 187. The `Id` property is declared as `string` but initialized with `Guid.NewGuid().ToString()` at line 140. This should be a `Guid` type per T25.

10. **[IMPLEMENTATION] ActorInstanceCreatedEvent.Status is string instead of enum (T25 violation)**: `ActorService.cs` line 626. The event's `Status` field is set with a string literal (`"running"` or `"pending"`) rather than using the `ActorStatus` enum type. Similarly, `ActorInstanceDeletedEvent` at line 894 uses `runner.Status.ToString().ToLowerInvariant()` to produce a string from an enum.

11. **[IMPLEMENTATION] Direct System.Text.Json usage instead of BannouJson (T20 violation)**: `ActorService.cs` lines 1330-1337 and `Runtime/ActorRunner.cs` line 12 (using directive). The `StartEncounterAsync` method directly uses `System.Text.Json.JsonElement` for deserialization/manipulation instead of using `BannouJson` as required by T20.

12. **[IMPLEMENTATION] ActorPoolManager uses hardcoded store names instead of StateStoreDefinitions (T1/T4 violation)**: `Pool/ActorPoolManager.cs` lines 31-32. The constants `POOL_NODES_STORE = "actor-pool-nodes"` and `ACTOR_ASSIGNMENTS_STORE = "actor-assignments"` are hardcoded string literals rather than referencing `StateStoreDefinitions` constants. All state store names should come from the schema-first generated `StateStoreDefinitions` class per FOUNDATION TENETS.

13. **[IMPLEMENTATION] Fire-and-forget async calls without await (T23 concern)**: `Runtime/ActorRunner.cs` lines 344, 370, 398. The encounter event publishing uses `_ = _messageBus.TryPublishAsync(...)` which discards the Task. While documented as "fire-and-forget for monitoring", this pattern means exceptions are silently swallowed and the async operation is not properly awaited. Per T23, Task-returning methods should be awaited.

14. **[QUALITY] Missing ApiException distinction in error handlers (T7 concern)**: Throughout `ActorService.cs`, all catch blocks catch only the generic `Exception` type. There is no distinction between `ApiException` (expected service errors) and unexpected `Exception` as required by T7. For example, the `SpawnActorAsync` method (line 647) catches all exceptions uniformly. Calls to remote services via mesh or pool manager could throw `ApiException` which should be handled distinctly.

15. **[IMPLEMENTATION] etag ?? string.Empty usage without proper justification (CLAUDE.md null-coalescing rule)**: `ActorService.cs` lines 163, 361, 453. The pattern `etag ?? string.Empty` is used when the ETag could legitimately be null (first-time saves). While this may be a valid compiler-satisfaction pattern, there is no comment explaining why the coalesce is acceptable, which is required per the CLAUDE.md null-coalescing rules.

16. **[QUALITY] PoolHealthMonitor error handling does not publish error events on general exceptions (T7 concern)**: `Pool/PoolHealthMonitor.cs` line 91. The catch block `catch (Exception ex)` only logs the error but does not publish an error event via `TryPublishErrorAsync`. Per T7, unexpected exceptions should be published as error events for monitoring.

17. **[IMPLEMENTATION] QueryOptionsHandler maxAgeMs uses int literal default instead of configuration (T21 violation)**: `Handlers/QueryOptionsHandler.cs` line 91. The default `maxAgeMs = 5000` is a hardcoded tunable that should be defined in the service configuration schema.

18. **[QUALITY] ActorRunner class is not sealed (potential T16 concern)**: `Runtime/ActorRunner.cs` line 22. The `ActorRunner` class is declared as `public class` but not `sealed`. Given that it implements `IActorRunner` and is not designed for inheritance, it should be sealed for proper encapsulation.

19. **[QUALITY] ActorRegistry class is not sealed (T16 concern)**: `Runtime/ActorRegistry.cs` line 9. The `ActorRegistry` class is declared as `public class` but not `sealed`. It is a concrete implementation not designed for inheritance.

20. **[IMPLEMENTATION] ScheduledEventManager.CheckEvents uses sync Timer callback with async fire-and-forget (T23 violation)**: `Handlers/ScheduleEventHandler.cs` line 303. The `CheckEvents` method is a sync `Timer` callback that calls `_ = FireEventAsync(evt)` discarding the Task. This means exceptions in `FireEventAsync` are silently lost and the async method is not properly awaited. Additionally, the Timer approach could lead to concurrent invocations if `FireEventAsync` takes longer than the check interval.

21. **[IMPLEMENTATION] ScheduledEventManager manual Dispose pattern instead of using statement (T24 consideration)**: `Handlers/ScheduleEventHandler.cs` lines 352-360. The `ScheduledEventManager` class implements `IDisposable` with a manual `_disposed` flag and `_timer.Dispose()`. While this is acceptable for class-owned resources (the Timer is owned by the class), the class is registered as a singleton service and relies on the DI container to dispose it. This is noted but may be acceptable.

22. **[QUALITY] Missing XML param documentation on some internal classes**: `Pool/ActorAssignment.cs` - The `ActorAssignment` class properties have XML summary tags but the class itself has no `<remarks>` explaining its lifecycle or ownership. Similarly `Pool/PoolNodeState.cs` and `Pool/PoolCapacitySummary` lack detailed documentation about when/how they're created.

23. **[IMPLEMENTATION] ActorRunner._lastSourceAppId is non-atomic state shared across threads (T9 concern)**: `Runtime/ActorRunner.cs` line 51. The `_lastSourceAppId` field is written by the perception subscription handler (line 1109) on a RabbitMQ consumer thread and read by `PublishStateUpdateIfNeededAsync` (line 912) on the behavior loop thread. This is a simple string reference assignment which is atomic in .NET, but there is no memory barrier or volatile declaration ensuring visibility across threads. A `volatile` modifier or `Interlocked` usage would be more correct for multi-threaded access patterns.

24. **[IMPLEMENTATION] ActorRunner._encounter field is accessed without synchronization (T9 concern)**: `Runtime/ActorRunner.cs` lines 54, 317-408. The `_encounter` field is read and written from both the behavior loop thread (via `CreateExecutionScopeAsync`, `GetStateSnapshot`) and potentially from external threads calling `StartEncounter`/`SetEncounterPhase`/`EndEncounter` on the runner directly. The `_statusLock` protects only the `_status` field but not `_encounter`. Race conditions could occur if `StartEncounterAsync` is called from the service thread while the behavior loop is executing.

25. **[IMPLEMENTATION] Regex.Match without caching or timeout (potential performance/safety issue)**: `ActorService.cs` line 794. The `FindAutoSpawnTemplateAsync` method calls `Regex.Match(actorId, template.AutoSpawn.IdPattern)` on every GetActor request for non-existent actors, for every template with auto-spawn enabled. This compiles the regex fresh each time with no caching and no timeout, which could be vulnerable to ReDoS attacks if a malicious pattern is stored in a template.

26. **[IMPLEMENTATION] ActorService.ListActorsAsync enumerates runners twice (performance)**: `ActorService.cs` lines 988-995. The code calls `runners.Count()` which enumerates the filtered LINQ query, then immediately calls `.Skip().Take().Select().ToList()` which enumerates it again. For large actor registries, this is inefficient. The `Count()` materializes the enumerable once, then the pagination re-enumerates it.

### Intentional Quirks (Documented Behavior)

1. **Deployment mode branching**: All spawn/stop/perception operations check `DeploymentMode` to determine local vs remote execution. Pool-related event handlers early-return if mode is "bannou" (not control plane).

2. **Auto-spawn with regex pattern**: Templates with `AutoSpawn.Enabled=true` define a regex pattern. When `GetActor` is called for a non-existent actor, all auto-spawn templates are checked. If the actor ID matches a pattern, the actor is automatically spawned. Capture groups extract CharacterId.

3. **Perception queue DropOldest**: The bounded channel uses `BoundedChannelFullMode.DropOldest`. When the queue is full, the oldest unprocessed perception is discarded. This prioritizes recent stimuli over stale ones.

4. **Behavior hot-reload via event**: When `behavior.updated` is published, the behavior cache is invalidated and a perception is injected into affected actors. The next tick reloads the fresh ABML document without stopping the actor.

5. **Pool node auto-registration on heartbeat**: If a heartbeat arrives from an unknown node (before its registration event), the node is automatically registered. This handles race conditions during node startup.

6. **Error recovery in behavior loop**: If the behavior tick throws, the actor enters `Error` status for 1 second, then resumes `Running`. Self-healing prevents permanent actor death from transient failures.

7. **Character state publishing**: If an actor has a `CharacterId`, it publishes `character.state_update` events with feelings/goals/memories. This bridges the actor system to the character system without tight coupling.

8. **Options query freshness**: `QueryOptions` with `Fresh` freshness injects a perception and waits one tick for the behavior to recompute options. This provides up-to-date choices at the cost of one tick latency.

### Design Considerations (Requires Planning)

1. **Single-threaded behavior loop**: Each ActorRunner runs its behavior loop on a single task. CPU-intensive behaviors (complex GOAP planning, large perception sets) can delay tick processing. The `GoapPlanTimeoutMs` (50ms) provides a budget.

2. **State persistence is periodic**: Actor state is saved every `AutoSaveIntervalSeconds` (default 60s). A crash loses up to 60 seconds of state changes. Critical state (encounter transitions) publishes events immediately.

3. **Pool node capacity is self-reported**: Nodes report their own capacity. No external validation prevents a node from claiming higher capacity than it can handle.

4. **No distributed locks for actor operations**: Unlike game-session or inventory, actor operations don't use distributed locks. The registry (bannou mode) or assignments (pool mode) provide ownership semantics. Concurrent operations on the same actor are serialized by the behavior loop's single-threaded design.

5. **Perception subscription per-character**: NPC Brain actors subscribe to character-addressed perception topics. With 100,000+ actors, this creates 100,000+ RabbitMQ subscriptions. Pool mode distributes these across nodes.

6. **Memory cleanup is per-tick**: Expired memories are cleaned each tick (every 100ms default). With many memories, this scan adds per-tick overhead. The working memory is separate and cleared between perceptions.

7. **Template index optimistic concurrency**: Template creation uses optimistic concurrency (GetWithETag + TrySave) for the index. Under high concurrent creation load, retries may be needed. No retry logic is implemented - conflicts return immediately.

8. **Encounter phase strings unvalidated**: Encounter phases are arbitrary strings. No state machine validates transitions. "initializing" → "victory" is as valid as "initializing" → "combat" → "victory". Application logic in ABML behaviors enforces meaningful transitions.

9. **Template index optimistic concurrency failures silently succeed**: Lines 163-167 and 453-457 - if `TrySaveAsync` fails for the template index during create/delete, the operation logs a warning but the template IS saved/deleted. The index may be temporarily inconsistent with actual templates.

10. **One category = one template**: Line 127-132 checks if `category:{name}` already exists before creating. A category can only have one template. To change template behavior for a category, you must update or delete the existing template first.

11. **ForceStopActors stops actors sequentially**: Lines 424-440 stop each actor in a foreach loop with individual try-catch. If stopping one actor fails, others continue. Partial stop failures are logged but don't prevent template deletion.

12. **Fresh options query waits one tick approximately**: Lines 1121-1128 inject a perception then `Task.Delay(DefaultTickIntervalMs)` - this waits roughly one tick duration but doesn't actually synchronize with the behavior loop. The actor may not have processed the perception yet.

13. **GetEncounter returns OK with null encounter**: Lines 1558-1565 return `StatusCodes.OK` with `HasActiveEncounter=false` when actor has no encounter, rather than returning NotFound. Callers must check the flag.

14. **Auto-spawn failure returns NotFound**: Lines 731-734 - if auto-spawn attempt fails (e.g., max instances exceeded, conflict), `GetActor` returns NotFound rather than the actual failure status. The true failure reason is only logged.

15. **ListActors nodeId filter not implemented**: Line 986-987 has a comment "nodeId filtering not applicable in bannou mode" but the filter is never applied even in pool mode. The filter parameter exists but is ignored.
