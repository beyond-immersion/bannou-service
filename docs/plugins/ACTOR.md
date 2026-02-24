# Actor Plugin Deep Dive

> **Plugin**: lib-actor
> **Schema**: schemas/actor-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Stores**: actor-templates (Redis/MySQL), actor-state (Redis), actor-pool-nodes (Redis), actor-assignments (Redis)

---

## Overview

Distributed actor management and execution (L2 GameFoundation) for NPC brains, event coordinators, and long-running behavior loops. Actors output behavioral state (feelings, goals, memories) to characters -- not directly visible to players. Supports multiple deployment modes (local, pool-per-type, shared-pool, auto-scale), ABML behavior document execution with hot-reload, GOAP planning integration, bounded perception queues with urgency filtering, and dynamic character binding (actors can start without a character and bind to one at runtime, transitioning from event brain to character brain mode without relaunch). Receives data from L4 services (personality, encounters, history) via the Variable Provider Factory pattern without depending on them.

---

## The NPC Intelligence Stack (Architectural Target)

> **Status**: The core actor runtime (ActorRunner, behavior loop, pool deployment, perception/memory) is implemented. The cognition pipeline is integrated with a forward-compatible evaluate_consequences slot. The broader vision of what Actor enables -- described below -- is the architectural north star that these systems serve.

### Living Worlds Require 100,000+ Concurrent Autonomous NPCs

The world must be alive whether or not players are watching. NPCs pursue their own goals, run businesses, form relationships, participate in politics, and generate emergent stories. This requires a scale target of 100,000+ concurrent AI-driven characters, each making decisions every 100-500ms. Every architecture choice in Actor -- zero-allocation bytecode VM, bounded perception queues, pool deployment modes, per-character RabbitMQ subscriptions -- exists specifically to hit this scale target. This is not a nice-to-have; it is the minimum viable living world.

### Actor Is a Universal Autonomous Agent Runtime

The same ActorRunner + ABML bytecode interpreter executes fundamentally different kinds of autonomous entities. NPC character brains, dungeon core intelligences, divine actors serving as both regional watcher gods (Moira/Fate, Thanatos/Death, Ares/War, Hermes/Commerce) and player garden orchestrators, and event brains for cinematic combat encounters all run on Actor. A divine actor tending a physical realm region and the same divine actor tending a player's conceptual garden space are the same operation from Actor's perspective -- different ABML behavior documents, same runtime. The category system (`npc-brain`, `event-combat`, `event-regional`, `world-admin`, `scheduled-task`) reflects this generality. If the Actor system can run all of these, it proves that the platform supports "any autonomous entity" -- and that improvements to the runtime benefit every system simultaneously.

**Dynamic character binding** further unifies these actor types. An actor can start as an event brain (no character, orchestrating multiple entities) and later bind to a character at runtime via `BindCharacterAsync`, transitioning to character brain mode with full variable provider activation -- without relaunching. The ABML behavior document can reference `${personality.*}`, `${encounters.*}`, `${backstory.*}` etc. from the start; those providers simply have no data until a characterId is bound. This enables progressive entity awakening: a divine actor creates its character profile in a system realm then binds to it, a dungeon core grows until it develops enough personality to warrant a character record, or any entity that starts simple and develops character-level cognition over time.

### Actors Are Both Producers and Consumers in the Content Flywheel

The fundamental thesis: more play produces more content, which produces more play. Actor behavioral state (feelings, goals, memories, encounter outcomes) feeds into character history. Character history feeds into resource archives when characters die. Archives feed the Storyline composer which generates narrative seeds. Regional watcher actors (running on Actor) consume those seeds and orchestrate new scenarios. Those scenarios involve NPC character brains (running on Actor) which generate new behavioral state. The loop is: Actor output → History → Archives → Storyline → Watcher Actor input → new Actor output.

### Player Characters Are Always Autonomous

Characters are independent entities with NPC brains running at all times. The guardian spirit (the player) influences but does not directly control. Actor cognition runs for player-bound characters; the player is an input source, not a replacement for the brain. If the player goes idle, the character continues acting autonomously based on personality. If the player pushes the character against its nature, the character resists through the same morality and personality systems that govern NPCs. This dual-agency model means Actor is relevant for ALL characters, not just NPCs.

### The Combat Dream: Cinematic Combat as an Actor Deployment Pattern

The vision for combat is that encounters feel like choreographed cinematics generated in real-time from actual environment, character capabilities, and player input. This is not a separate system -- it is an Actor deployment pattern. An Event Brain actor (category `event-combat`) queries the Mapping service for spatial affordances ("what objects within 5m can be grabbed?", "are there elevation changes for dramatic leaps?"), queries character agents for capabilities and personality, composes streaming cinematics via continuation points, and coordinates the three-version temporal desync (canonical past, participant present, spectator projection). The Actor runtime's existing event brain architecture, encounter management, and ABML execution are the foundation.

### Morality Integration: The Conscience That Emerges from the Pipeline

The `evaluate_consequences` cognition stage (opt-in via `conscience: true` ABML metadata) enables NPCs to have "second thoughts" before taking morally costly actions. The stage reads obligation cost data (`${obligations.violation_cost.<type>}`) and faction norm data (`${faction.*}`) from the variable provider factory, flags actions where cost exceeds a threshold as `knowing_violation`, and writes cost modifiers into the GOAP planner's execution context. The planner then naturally selects alternatives when the moral cost is high enough. An honest merchant buys food; a desperate rogue steals it; the same rogue in a temple district buys it because territorial norms make theft expensive there. This is the endpoint of the Faction → Obligation → Actor cognition pipeline described in the [Morality System guide](../guides/MORALITY-SYSTEM.md).

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for actor state, templates, pool nodes, assignments |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events, state updates, pool commands; error publishing |
| lib-messaging (`IEventConsumer`) | Behavior/personality cache invalidation, pool node management events |
| lib-mesh (`IMeshInvocationClient`) | Forwarding requests to remote pool nodes in distributed mode |
| lib-resource (`IResourceClient`) | Character reference cleanup via x-references pattern |
| lib-character (`ICharacterClient`) | Realm lookup on actor spawn (L2 same-layer dependency) |
| `IEnumerable<IVariableProviderFactory>` | DI-discovered providers from L3/L4 services (see below) |
| `IEnumerable<IBehaviorDocumentProvider>` | DI-discovered behavior document providers (see below) |
| `ICognitionBuilder` | Builds cognition pipelines from templates with override composition |

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

**Registered Provider Factories** (discovered via `IEnumerable<IVariableProviderFactory>` DI injection):

| Factory | Provider Namespace | Owning Plugin | Layer |
|---------|-------------------|---------------|-------|
| `PersonalityProviderFactory` | `personality` | lib-character-personality | L4 |
| `CombatPreferencesProviderFactory` | `combat` | lib-character-personality | L4 |
| `BackstoryProviderFactory` | `backstory` | lib-character-history | L4 |
| `EncountersProviderFactory` | `encounters` | lib-character-encounter | L4 |
| `ObligationProviderFactory` | `obligations` | lib-obligation | L4 |
| `FactionProviderFactory` | `faction` | lib-faction | L4 |
| `QuestProviderFactory` | `quest` | lib-quest | L2 |
| `SeedProviderFactory` | `seed` | lib-seed | L2 |
| `LocationContextProviderFactory` | `location` | lib-location | L2 |

**Character Brain vs Event Brain Data Access**

The Variable Provider Factory pattern above applies to **Character Brain actors** - actors bound to a single character that access data about *themselves* via live providers.

**Event Brain actors** (regional watchers, encounter coordinators) use a different pattern because they orchestrate *multiple* characters dynamically. Instead of live variable providers, Event Brain actors use:

- **ResourceArchiveProvider** (from lib-puppetmaster) - provides ABML expression access to resource snapshots
- **ResourceSnapshotCache** (from lib-puppetmaster) - caches loaded snapshots with TTL-based expiration
- **`load_snapshot:` ABML action** - explicitly loads character/resource data into the execution scope

This separation exists because:
1. Character Brains have a stable binding to one character ID (providers can be created once)
2. Event Brains shift focus constantly (load data on-demand for whichever entity they're currently evaluating)

**Dynamic Binding: Event Brain → Character Brain Transition**

Actors can transition from event brain to character brain mode at runtime via the `BindCharacterAsync` API without relaunching. When an actor starts without a `characterId`, it operates as an event brain -- ABML expressions referencing `${personality.*}`, `${encounters.*}`, etc. resolve to null/empty (the providers are not loaded because there is no character to load from). When `BindCharacterAsync` is called:

1. The `CharacterId` is set on the running ActorRunner
2. A per-character RabbitMQ perception subscription is established (`character.{characterId}.perceptions`)
3. On the next behavior tick, variable provider factories detect the new `characterId` and activate -- the actor gains access to personality traits, encounter history, backstory, quest data, and all other character-based providers
4. An `ActorCharacterBoundEvent` is published on topic `actor.instance.character-bound`

This enables **progressive entity awakening**: entities that start simple (event brain, no character) and develop character-level cognition as they grow. Divine actors create their character profile in a system realm then bind to it. Dungeon cores grow until their seed reaches a phase that warrants a character record. The ABML behavior document supports both modes from the start -- the same document, richer data as providers activate.

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

Current providers: personality, combat, backstory, encounters, obligations, faction, quest, seed, location (see Registered Provider Factories table above). The `world` provider namespace is defined in `variable-providers.yaml` (for `${world.*}` expressions) but lib-worldstate does not yet implement `IVariableProviderFactory`. Planned future providers ([#147](https://github.com/beyond-immersion/bannou-service/issues/147)): currency (30s TTL), inventory (1m TTL), relationships (5m TTL).

**Anti-patterns**: Never access another plugin's state store directly. Never poll APIs in tight loops (use Variable Providers with cache). Never cache mutation-critical data beyond short TTLs.

**Cognition Pipeline Integration**

ActorRunner executes a two-phase tick model: template-driven cognition first, then ABML-driven behavior. The cognition pipeline is built from `ICognitionBuilder` during actor initialization and cached for the actor's lifetime.

**Template Resolution Order** (first non-null wins):
1. `ActorTemplateData.CognitionTemplateId` — explicit per-template config (primary)
2. `AbmlDocument.Metadata.CognitionTemplate` — behavior-specified override
3. `CognitionDefaults` category mapping — convention fallback (`npc-brain` → `humanoid-cognition-base`, `event-combat`/`event-regional` → `creature-cognition-base`, `world-admin` → `object-cognition-base`)
4. No template resolved → no pipeline, actor runs ABML-only (legitimate for `scheduled-task` and similar)

**Override Composition** (applied in order, later layers override earlier):
- Layer 1: `ActorTemplateData.CognitionOverrides` — static per-type defaults (game designer's knob)
- Layer 2: `ActorStateSnapshot.CognitionOverrides` — per-NPC overrides accumulated through gameplay

**Failure Mode**: If a template ID is resolved but `ICognitionBuilder.Build()` returns null (template not found in registry), the actor fails to start with `ActorStatus.Error`. A misconfigured template is a bug, not a degradation case.

**TOCTOU Safety**: The `_cognitionPipeline` field can be nulled by `InvalidateCachedBehavior()` on a RabbitMQ event handler thread. The main loop captures the pipeline reference to a local variable before use, preventing races between the null-check and pipeline execution.

**Perception Loss Prevention**: If the cognition pipeline throws during processing, perceptions that were already drained from the bounded channel are stored directly into working memory as a fallback. This prevents data loss even on pipeline failures.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-puppetmaster | Calls `IActorClient.InjectPerceptionAsync` to inject perceptions into running actors; calls `IActorClient.ListActorsAsync` to enumerate actors for behavior cache invalidation after asset updates |

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
| `actor.pool-node.unhealthy` | `PoolNodeUnhealthyEvent` | PoolHealthMonitor detected heartbeat timeout; node removed from pool |
| `actor.instance.character-bound` | `ActorCharacterBoundEvent` | Actor bound to a character (via BindActorCharacter API or on startup when spawned with a characterId) |
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
| `IMeshInvocationClient` | Scoped | Forwarding requests to remote pool nodes in distributed mode |
| `IResourceClient` | Scoped | Character reference cleanup via x-references pattern |
| `ICharacterClient` | Scoped | Realm lookup on actor spawn (L2 same-layer) |
| `ITelemetryProvider` | Singleton | Span instrumentation for async methods |
| `ActorRunnerFactory` | Singleton | Creates ActorRunner instances |
| `ActorRegistry` | Singleton | Local actor instance tracking |
| `ICognitionBuilder` | Singleton | Builds cognition pipelines from templates + overrides |
| `IBehaviorDocumentLoader` | Singleton | Loads behavior documents via provider chain |
| `SeededBehaviorProvider` | Singleton | Loads static behaviors from filesystem (Priority 50) |
| `FallbackBehaviorProvider` | Singleton | Returns null for missing behaviors after logging warning (Priority 0) |
| `IDocumentExecutorFactory` | Singleton | Creates document executors for ABML behavior execution |
| `IScheduledEventManager` | Singleton | Per-node delayed event timer management |
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

### Actor Lifecycle (6 endpoints)

- **SpawnActor** (`/actor/spawn`): In bannou mode: creates ActorRunner locally, registers in ActorRegistry, starts behavior loop. In pool mode: acquires least-loaded node via ActorPoolManager, publishes SpawnActorCommand to node's command topic. Publishes `actor-instance.created`.
- **StopActor** (`/actor/stop`): In bannou mode: stops local runner, removes from registry. In pool mode: publishes StopActorCommand to assigned node. Publishes `actor-instance.deleted`.
- **GetActor** (`/actor/get`): Returns actor state snapshot. If actor not running but matches an auto-spawn template (regex pattern), automatically spawns it (instantiate-on-access pattern).
- **BindActorCharacter** (`/actor/bind-character`): Binds a running actor to a character, enabling character brain variable providers without relaunching. In bannou mode: calls `BindCharacterAsync` on the local ActorRunner directly. In pool mode: updates the actor assignment record with the new characterId, then publishes `BindActorCharacterCommand` to the assigned node's command topic. The ActorRunner sets the `CharacterId`, establishes a per-character RabbitMQ perception subscription, and publishes `ActorCharacterBoundEvent`. Returns `Conflict` if the actor is already bound to a character. Returns `NotFound` if the actor is not running. Also emits the bound event on `StartAsync` when spawned with an initial characterId.
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


ActorRunner Behavior Loop (Two-Phase Execution)
==================================================

  StartAsync()
       │
       ├── InitializeBehaviorAsync()
       │    ├── Load ABML document (cached, hot-reloadable)
       │    └── BuildCognitionPipeline()
       │         ├── Resolve template: config → ABML metadata → category default
       │         ├── Merge overrides: template L1 + instance L2
       │         └── _cognitionPipeline = builder.Build(templateId, overrides)
       │              └── null template ID → no pipeline (ABML-only)
       │              └── null result with ID → Error (misconfigured)
       │
       ├── If CharacterId set at spawn:
       │    ├── SetupPerceptionSubscriptionAsync()
       │    └── Publish ActorCharacterBoundEvent
       │
       ├── [Later, at runtime] BindCharacterAsync(characterId):
       │    ├── Guard: already bound → InvalidOperationException
       │    ├── Set CharacterId = characterId
       │    ├── SetupPerceptionSubscriptionAsync()
       │    └── Publish ActorCharacterBoundEvent
       │
       └── RunBehaviorLoopAsync() [while !cancelled]
                │
                ├── Phase 1: COGNITION (if pipeline exists)
                │    ├── Capture pipeline to local (TOCTOU safety)
                │    ├── Drain perception queue → perceptions list
                │    │    ├── urgency < FilterThreshold → drop
                │    │    └── urgency ≥ MemoryThreshold → store as memory
                │    ├── Build CognitionContext (entityId, handler registry)
                │    ├── pipeline.ProcessBatchAsync(perceptions, context)
                │    └── Apply result → working memory, replan flags
                │         └── On failure: store perceptions directly (no loss)
                │
                ├── Phase 2: BEHAVIOR (always)
                │    ├── Build execution scope:
                │    │    ├── agent: {id, behavior_id, character_id, category}
                │    │    ├── feelings: {joy: 0.5, anger: 0.2, ...}
                │    │    ├── goals: {primary, secondary[], parameters{}}
                │    │    ├── memories: {key → value (with TTL)}
                │    │    ├── working_memory: {perception:type:source → data}
                │    │    ├── personality: {traits, combat_style, risk}
                │    │    └── backstory: {elements[]}
                │    │
                │    └── Execute flow: on_tick (preferred) or main
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


Dynamic Character Binding
===========================

  BindActorCharacter(actorId, characterId)
       │
       ├── BANNOU MODE:
       │    ├── Find runner in ActorRegistry
       │    ├── Call runner.BindCharacterAsync(characterId)
       │    │    ├── Guard: already bound? → Conflict
       │    │    ├── Set CharacterId = characterId
       │    │    ├── SetupPerceptionSubscriptionAsync()
       │    │    │    └── SubscribeDynamicAsync("character.{charId}.perceptions")
       │    │    └── Publish ActorCharacterBoundEvent
       │    └── Next tick: variable providers detect characterId
       │         ├── ${personality.*} → activates
       │         ├── ${encounters.*} → activates
       │         ├── ${backstory.*}  → activates
       │         └── ${quest.*}      → activates
       │
       └── POOL MODE:
            ├── Update actor assignment (characterId) in Redis
            ├── Publish BindActorCharacterCommand to node topic
            │    └── actor.node.{appId}.bind-character
            └── Pool node worker receives command
                 └── Forwards to local runner.BindCharacterAsync()


  Progressive Entity Awakening (Example: Divine Actor)
  =====================================================

  1. Deity created → Actor spawned (event brain, no character)
     │  Actor runs ABML behavior with ${personality.*} = null
     │  Uses load_snapshot: for ad-hoc entity data
     │
  2. Deity creates Character in divine system realm
     │  (via /divine/deity/create or runtime behavior)
     │
  3. BindActorCharacter(actorId, divineCharacterId)
     │  Actor now has ${personality.*}, ${encounters.*}, etc.
     │  Same behavior document, richer data
     │  Still uses load_snapshot: for mortal data
     │
     └── The actor is now a character brain + event brain hybrid


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
6. **Additional variable providers (currency, inventory, relationships)**: Extend the Variable Provider Factory pattern with providers for currency balance (30s TTL), inventory contents (1m TTL), and relationship data (5m TTL).
<!-- AUDIT:NEEDS_DESIGN:2026-02-23:https://github.com/beyond-immersion/bannou-service/issues/147 -->
7. **Worldstate variable provider**: `world` provider namespace is defined in `variable-providers.yaml` but lib-worldstate does not yet implement `IVariableProviderFactory` to provide `${world.*}` expressions (game time, calendar, season data).
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/477 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **T29 violation: `cognitionOverrides` uses `additionalProperties: true` but is deserialized to typed `CognitionOverrides`**: The `cognitionOverrides` field on `CreateActorTemplateRequest`, `UpdateActorTemplateRequest`, and `ActorTemplateResponse` is defined as an opaque metadata bag but `ActorTemplateData.DeserializeCognitionOverrides()` explicitly calls `BannouJson.Deserialize<CognitionOverrides>()` to convert it to a fully typed record with 5 discriminated `ICognitionOverride` subtypes. Should be a typed schema with `oneOf`/discriminator pattern.
   <!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/462 -->

2. **T29 violation: `initialState` uses `additionalProperties: true` but is deserialized to `ActorStateSnapshot`**: `SpawnActorRequest.initialState` is marked opaque but `ActorRunner.InitializeFromState()` casts it to `ActorStateSnapshot` and reads `.Feelings`, `.Goals`, `.Memories`, `.WorkingMemory`, `.CognitionOverrides`. Schema description contradicts itself. Should define `ActorStateSnapshot` (or API-appropriate subset) as a typed schema.
   <!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/463 -->

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

10. **Per-actor RabbitMQ subscription for perceptions**: Each actor with a `CharacterId` creates a dynamic RabbitMQ subscription via `SubscribeDynamicAsync` on topic `character.{characterId}.perceptions`. This subscription is established either at `StartAsync` (when spawned with a characterId) or at `BindCharacterAsync` (when binding at runtime). Game servers and other actors publish perceptions to these character-specific topics via the ABML `emit_perception:` action. At scale (100,000+ actors), this means 100,000+ RabbitMQ subscriptions. This is intentional: targeted per-character topics enable efficient perception routing without the actor needing to filter irrelevant perceptions from a shared topic. Pool mode distributes subscriptions across nodes (e.g., 10 nodes × 10,000 subscriptions each). The `InjectPerception` API provides a direct injection alternative for testing. Subscriptions are disposed on actor stop.

11. **Per-tick memory cleanup via linear scan**: `ActorState.CleanupExpiredMemories()` runs every behavior loop tick (~100ms) and performs `List.RemoveAll()` on the `_memories` list under a lock, removing entries where `ExpiresAt <= now`. Permanent memories (`ExpiresAt = null`) are never removed. This is a simple O(n) scan per tick, acceptable for typical actor populations (hundreds of memories per actor). At extreme scale (10,000+ memories per actor), this could become a bottleneck and would benefit from a sorted expiration index. Working memory (`SetWorkingMemory`) persists across ticks — new perceptions overwrite by key but no explicit clearing occurs between ticks.

12. **Encounter phase strings are intentionally unvalidated**: `SetEncounterPhase(string phase)` accepts any string without validation or state machine enforcement. `StartEncounter` sets the initial phase to `"initializing"`. Phase names and valid transitions are defined by ABML behavior scripts per encounter type — the Actor service (L2) is intentionally agnostic to encounter-type-specific phase semantics. Adding server-side phase validation would couple Actor to specific encounter type definitions, violating the extensibility model where new encounter types (with their own phases) can be authored in ABML without schema or code changes. Callers (Puppetmaster, game servers) are responsible for passing meaningful phase strings. Invalid phase strings are accepted silently because "invalid" is relative to the encounter type's ABML behavior, not to Actor.

13. **Fresh options query uses approximate tick wait**: `QueryOptionsAsync` with `Freshness=Fresh` injects a perception into the actor's queue and then calls `Task.Delay(DefaultTickIntervalMs)` (~100ms) before re-reading state. This does NOT synchronize with the actual behavior loop — the delay may finish before or after the next tick processes the perception. This is intentional: options queries are best-effort decision-support, not precision-critical operations. The three freshness levels (`Cached`/`Fresh`/`Stale`) already give callers control over timing expectations. Adding a synchronization primitive (e.g., `TaskCompletionSource` signaled on tick completion) would add contention to the behavior loop hot path (~100ms per tick per actor × 100,000+ actors) for a marginal improvement in query timing. If the perception wasn't processed in one tick, the caller can query again with `Fresh` or fall back to `Stale`.

14. **Auto-spawn failure returns NotFound to caller**: When `GetActor` triggers auto-spawn and `SpawnActorAsync` fails (Conflict, ServiceUnavailable, etc.), `GetActorAsync` returns `NotFound` — not the real failure status. This is intentional: auto-spawn is an implementation detail invisible to the caller. From the caller's perspective, they asked "get actor X" and the answer is "it doesn't exist." Returning `Conflict` or `ServiceUnavailable` would leak the auto-spawn mechanism. The real failure status and reason are logged at Warning level server-side for debugging. **Edge case**: If `SpawnActorAsync` returns `Conflict` because a concurrent `GetActor` call just auto-spawned the same actor, the caller gets `NotFound` even though the actor now exists. A retry by the caller would succeed. If this becomes a problem at scale (high-contention auto-spawn for the same actor ID), a retry-on-conflict loop could be added inside `GetActorAsync`.

### Design Considerations (Requires Planning)

1. **Pool node capacity is self-reported**: No external validation of claimed capacity.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/394 -->

---

## Work Tracking

### Completed

- **2026-02-13**: Wired CognitionBuilder into ActorRunner ([#422](https://github.com/beyond-immersion/bannou-service/issues/422)). Two-phase tick execution (cognition pipeline then ABML behavior). Template resolution: actor config → ABML metadata → category defaults. Three-layer override composition (template + instance). TOCTOU-safe pipeline capture. Perception loss prevention on pipeline failure.

- **2026-02-16**: Dynamic character binding. Added `POST /actor/bind-character` endpoint with `BindActorCharacterRequest`/`BindActorCharacterResponse`, `ActorCharacterBoundEvent`, and `BindActorCharacterCommand` pool command. Enables progressive entity awakening.

- **2026-02-23**: L3-hardening audit (19 items). Schema fixes: removed T8 filler booleans from responses, consolidated duplicate `Position3D`/`ChoreographyPosition` types, fixed NRT compliance across API and events schemas, consolidated inline enums to shared types (`ActorExitReason`, `ActorDeploymentMode`, `ActorPoolNodeType`), added `x-event-publications` declarations, added validation keywords to all configuration properties, added `minLength: 1` to required request string fields, added `StartEncounterResponse` body, fixed `cognitionOverrides` metadata bag descriptions. Code fixes: fixed dangling scoped service reference in `ActorServicePlugin.OnStartAsync`, replaced `Guid.Empty` sentinels with nullable `Guid?` in event fields, added ETag retry loops for non-atomic index operations in `ActorPoolManager`, fixed T23 `Task.FromResult`/`ValueTask.FromResult` patterns to use async/await, replaced hardcoded `"bannou"` fallbacks with configuration property references (T21), changed generic `Exception` catch to `ApiException` in `ResolveRealmIdAsync` (T7), made `LoopIterations` nullable for remote actors (T8), fixed `IAsyncDisposable` and timer disposal patterns (T24), added missing `CancellationToken` to draining event publish. Telemetry: added T30 `ITelemetryProvider.StartActivity` spans to ~80 async methods across 22 files (ActorRunner, ActorPoolManager, ActorPoolNodeWorker, HeartbeatEmitter, PoolHealthMonitor, ActorService, ActorServiceEvents, BehaviorDocumentLoader, SeededBehaviorProvider, FallbackBehaviorProvider, ActorRunnerFactory pass-through, ActorServicePlugin lifecycle, and all 9 ABML action handlers + ScheduledEventManager).

- **2026-02-23**: Variable provider documentation audit. Updated Registered Provider Factories table: added 4 missing providers (seed, obligations, location, faction), fixed `combat_preferences` → `combat` provider name, added layer column. Updated planned future providers paragraph: removed spatial context (now implemented as `LocationContextProviderFactory`), noted `world` provider defined but not implemented. Added Potential Extensions #6 (currency/inventory/relationships, #147) and #7 (worldstate provider).

### Implementation Gaps

No implementation gaps identified requiring AUDIT markers.
