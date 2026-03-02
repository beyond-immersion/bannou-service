# Director Plugin Deep Dive

> **Plugin**: lib-director (not yet created)
> **Schema**: `schemas/director-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: director-sessions (MySQL), director-events (MySQL), director-actor-taps (Redis), director-overrides (Redis), director-player-targets (Redis), director-lock (Redis) -- all planned
> **Layer**: GameFeatures
> **Status**: Aspirational -- no schema, no generated code, no service implementation exists.
> **Planning**: N/A

## Overview

Human-in-the-loop orchestration service (L4 GameFeatures) for developer-driven event coordination, actor observation, and player audience management. The Director is to the development team what Puppetmaster is to NPC behavior and Gardener is to player experience: an orchestration layer that coordinates major world events through existing service primitives, ensuring the right players witness the right moments. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver live event management mechanics.

Three control tiers define the developer's relationship to the actor system: **Observe** (tap into any actor's perception stream and cognitive state), **Steer** (inject perceptions and adjust GOAP priorities while actors run autonomously), and **Drive** (replace an actor's ABML cognition with human decision-making, issuing API calls through the same action handlers actors use). The developer never bypasses game rules -- every action goes through the same pipelines the autonomous system uses, simultaneously testing actor mechanisms while orchestrating live content.

Game-agnostic: event categories, steering strategies, and broadcast coordination rules are configured through director configuration and event templates at deployment time. Internal-only, never internet-facing. All endpoints require the `developer` role.

---

## The Film Director Metaphor

In film production, the director doesn't write the script (Storyline), doesn't act (Actor), doesn't manage the set (Puppetmaster), doesn't manage the audience experience (Gardener), and doesn't operate the camera (Broadcast). The director coordinates all of these for the best possible production -- deciding when to roll, which actors to focus on, where to position the audience, and when the moment is broadcast-worthy. That is exactly what this service does.

The key architectural insight: **the developer doesn't replace emergence with authoring.** The content flywheel produces emergent events (Moira composes a ghost quest from a legendary character's death archive, a dungeon core awakens after centuries of dormancy, an economic crisis triggers divine intervention). The Director ensures the right audience witnesses these events and amplifies their reach through Broadcast/Showtime. The events themselves remain emergent -- the developer coordinates production, not content.

---

## The Three Control Tiers

### Tier 1: OBSERVE (Tap)

The developer sees what any active actor sees. No intervention, no side effects. Pure monitoring.

| Data Stream | Source | Update Frequency |
|---|---|---|
| Perception queue | Actor's bounded channel (via subscription relay) | Real-time (per perception) |
| Variable provider state | Actor's registered `IVariableProviderFactory` outputs | Per-tick snapshot |
| Current GOAP plan | Actor's planner output (goal chain + action sequence) | Per-replan |
| Behavior tree position | ABML document executor's current flow/node | Per-tick |
| Feelings/goals/memories | `ActorStateSnapshot` from actor-state store | Per auto-save interval |
| Encounter state | Active encounter phase, participants, data | Per phase change |
| Cognition pipeline output | Perception processing results, working memory | Per-tick |

**Delivery mechanism**: Director subscribes to a per-actor RabbitMQ topic (`director.tap.{actorId}`) that the actor's runtime publishes to when a tap is active. This avoids modifying Actor's core loop -- the tap subscription is registered externally by Director, and Actor publishes tap data only when the `director.tap.{actorId}` queue has consumers (lazy publishing via RabbitMQ's basic.get or consumer count check). The developer's WebSocket session receives tap data via `IClientEventPublisher` on the Connect service.

**Implementation**: Director calls `POST /actor/inject-perception` with a special `director_tap_start` perception type that Actor recognizes as a tap registration. Actor starts publishing tick snapshots to the tap topic. `director_tap_stop` deregisters. Actor treats these as no-op perceptions for its own cognition -- they only affect the tap publishing side channel.

**Scaling**: A single developer can tap multiple actors simultaneously (monitoring a region). Each tap is a lightweight RabbitMQ subscription. At scale, a tap dashboard might observe 20-50 actors in a region, with variable provider snapshots sampled at reduced frequency (every 5th tick) to manage bandwidth.

### Tier 2: STEER (Assist)

The developer redirects autonomous actors without replacing their cognition. Actors continue running ABML behaviors; the developer modulates inputs and priorities.

| Mechanism | How It Works | Actor Impact |
|---|---|---|
| **Perception injection** | `POST /actor/inject-perception` with developer-authored perception data | Actor processes as normal perception; urgency determines impact |
| **GOAP priority override** | Director stores priority modifiers in `director-overrides` Redis store; Actor's GOAP planner reads these as additional cost modifiers via a `IDirectorOverrideProvider` | Action costs shift, causing different plan selection |
| **Action gate** | Director marks specific action types as requiring approval; Actor's execution pauses before those actions and publishes an approval request to `director.approval.{actorId}` | Developer approves/denies via API; timeout defaults to approve (fail-open) |
| **Gardener amplification** | Director calls Gardener APIs to increase POI density, spawn specific scenario templates, or adjust scoring weights for players near the event | Players are drawn toward the event through normal Gardener mechanisms |
| **Hearsay injection** | Director calls Hearsay APIs to inject rumors about the event, spreading awareness through NPC social networks | NPCs organically discuss and react to the approaching event |

**The IDirectorOverrideProvider Pattern**: A new `IVariableProviderFactory` implementation (namespace: `director`) registered by lib-director. Provides `${director.priority.<action_type>}` cost modifiers that GOAP planners read during action evaluation. When no director session is active for an actor, the provider returns null for all paths (zero overhead). This follows the established Variable Provider Factory pattern -- Actor discovers it via `IEnumerable<IVariableProviderFactory>` with no compile-time dependency on lib-director.

**Fail-open by default**: If the developer disconnects or doesn't respond to an approval gate within the configured timeout (`ActionGateTimeoutSeconds`), the action proceeds. The autonomous system never stalls waiting for human input. Director enhances but never blocks.

### Tier 3: DRIVE (Impersonate)

The developer replaces an actor's ABML cognition for the duration of a drive session. The actor's behavior loop pauses; the developer issues commands through the same action handler pipeline.

| Aspect | How It Works |
|---|---|
| **Binding** | `POST /director/actor/drive` pauses the actor's ABML execution loop and registers the developer's session as the cognition source |
| **Commands** | Developer issues ABML-equivalent actions via `POST /director/actor/execute-action`. These route through the same `IActionHandler` pipeline (load_snapshot, watch, spawn_watcher, emit_perception, call, etc.) |
| **Variable access** | Developer can query any `${namespace.path}` expression via `POST /director/actor/evaluate-expression`, reading the same data actors see |
| **State visibility** | Actor's perception queue continues accumulating; developer sees incoming perceptions and decides how to respond |
| **Unbinding** | `POST /director/actor/release` resumes ABML execution. The actor's behavior loop restarts from its last checkpoint. Any state changes the developer made (feelings, goals, memories) persist. |
| **Timeout** | If the developer disconnects without releasing, auto-release after `DriveSessionTimeoutMinutes`. Actor resumes autonomously. |

**Why this matters for testing**: Every action the developer issues goes through the identical pipeline that ABML bytecode uses. If a developer can't accomplish something, it means the ABML action handler doesn't support it -- which is a genuine gap to fix. If something works for the developer but not for an ABML behavior, it's a compiler or bytecode issue. Drive sessions are simultaneously production orchestration AND integration testing.

**Indistinguishable from autonomous behavior**: Players and NPCs cannot tell whether an actor is being driven by ABML or a developer. The same API calls produce the same events. The same perceptions arrive through the same channels. This is essential for live event integrity -- a "developer-driven" god should feel identical to an autonomous one.

---

## Directed Events: Multi-Actor Coordination

A directed event is a named coordination context that tracks multiple actors, player populations, and broadcast state as a single orchestrated production.

### Directed Event Anatomy

```
DirectedEvent:
  eventId: Guid
  name: string                          # Human-readable event name
  description: string                   # What this event is about
  gameServiceId: Guid                   # Scoped to a game service
  realmId: Guid?                        # Optional realm scope
  status: DirectedEventStatus           # Planned, Active, Climax, WindDown, Completed
  createdBy: Guid                       # Account ID of the creating developer
  createdAt: DateTime
  startedAt: DateTime?                  # When status moved to Active
  completedAt: DateTime?

  # Actor tracking
  actors: DirectedEventActor[]          # Actors involved in this event
  # Each actor has: actorId, role (string), controlTier, assignedTo (accountId?)

  # Player targeting
  playerTargets: DirectedEventTarget[]  # Player populations to draw in
  # Each target has: targetType (region/location/session/account), targetId, priority, method

  # Broadcast coordination
  broadcastPriority: int                # 0 = no broadcast, 1-10 = priority for Broadcast/Showtime
  broadcastSessionId: Guid?             # Linked lib-broadcast platform session
  showtimeSessionId: Guid?              # Linked lib-showtime in-game session
```

### Directed Event Lifecycle

```
PLANNED                    Developer creates the event, assigns actors and targets.
   │                       Actors continue running autonomously. No player impact yet.
   │
   ▼  [developer activates]
ACTIVE                     Director begins steering: perception injection, GOAP overrides,
   │                       Gardener amplification. Players start being drawn toward the region.
   │                       Broadcast may begin recording/streaming.
   │
   ▼  [developer signals climax]
CLIMAX                     Peak coordination. Director may drive key actors directly.
   │                       Broadcast priority at maximum. Showtime hype mechanics active.
   │                       Gardener aggressively spawns event-related POIs and scenarios.
   │
   ▼  [developer signals wind-down]
WIND_DOWN                  Director releases driven actors. GOAP overrides removed gradually.
   │                       Broadcast continues but de-prioritized. Gardener returns to normal.
   │                       Event consequences propagate through normal event system.
   │
   ▼  [developer completes]
COMPLETED                  All taps closed. All overrides removed. Event archived to MySQL.
                           Post-event metrics captured (player count, broadcast reach, etc.).
```

### Player Targeting Methods

Director doesn't teleport players or force participation. It uses existing mechanisms to create organic flow:

| Method | Service Used | How It Works |
|---|---|---|
| **POI injection** | Gardener | Spawn event-related POIs in target players' gardens with high priority and event-flavored trigger modes |
| **Scenario offering** | Gardener | Create temporary scenario templates linked to the event; boost scoring weight for target players |
| **Rumor seeding** | Hearsay | Inject beliefs about the event into NPC social networks near target players; NPCs organically mention it |
| **NPC behavior** | Actor (via perception injection) | NPCs near target players receive perceptions about the event, causing them to talk about it, travel toward it, or react to it |
| **Shortcut publishing** | Connect | Publish WebSocket shortcuts offering direct entry to event-related game sessions |
| **Quest hooks** | Quest | Create event-related quests via Divine actor or directly; quest objectives lead players toward the event region |
| **Economic incentive** | Currency / Trade | Divine economic intervention drops opportunity near the event (merchant caravans, treasure, rare resources) |

All of these methods are existing service capabilities. Director orchestrates their timing and targeting but invents no new player-facing mechanisms.

---

## The Deployment Gradient

Director supports varying levels of human involvement, from "required for all major events" to "entirely unused":

| Maturity Level | Director Usage | Autonomous System | Hardware Implication |
|---|---|---|---|
| **Early / Minimal hardware** | Director required for all major events. Divine actors handle routine only. Developer drives god-actors during climax moments. | Gods run basic patrol behaviors. Content flywheel turns slowly. | Minimal actor pool. Gods may not have enough compute for complex GOAP evaluation. |
| **Growing / Moderate hardware** | Director assists. Developer steers god-actors and amplifies reach. Autonomous system generates events; developer ensures audience. | Gods compose narratives, spawn quests. Gardener routes players. Some events fire without developer involvement. | Moderate actor pool. Gods run full cognition with personality-driven decisions. |
| **Mature / Full hardware** | Director observes. Developer monitors dashboards and intervenes only for exceptional moments. Most events are fully automated. | Full content flywheel. Gods orchestrate complex multi-service event chains. Gardener provides responsive player experiences. | Full actor pool (100K+ NPCs). Gods run rich cognitive behaviors. Automated broadcast decisions. |
| **Maximum automation** | Director unused or pure observer. All orchestration through divine actors. Developer only reviews post-hoc analytics. | Complete autonomous world management. Gods compete, cooperate, and generate events that naturally attract players. | Hardware scales to demand. Orchestrator manages pool dynamically. |

**The transition is smooth** because Director and divine actors use the same APIs. A developer steering a god-actor through Tier 2 (STEER) and a god-actor's ABML behavior document producing the same API calls are indistinguishable. As ABML behaviors mature, the developer's role shifts from "driving the event" to "monitoring the event" to "reviewing the event afterwards."

---

## Broadcast & Showtime Integration

Director is the coordination point between "something interesting is happening" (detected by observation) and "the world should see this" (delivered by Broadcast and Showtime).

### The Broadcast Coordination Flow

```
1. Developer observes actor tap data
   "Moira is about to commission a legendary ghost quest from the archive
    of Seraphina the Dragonslayer"
        │
        ▼
2. Developer creates directed event
   POST /director/event/create
   { name: "Ghost of Seraphina", realmId: ..., broadcastPriority: 8 }
        │
        ▼
3. Developer adds actors and targets
   POST /director/event/add-actor    (Moira's actor, tier: steer)
   POST /director/event/add-actor    (nearby NPC actors, tier: observe)
   POST /director/event/add-target   (players in the region, method: rumor_seeding)
   POST /director/event/add-target   (players in void, method: poi_injection)
        │
        ▼
4. Developer activates event
   POST /director/event/activate
   Director begins Gardener amplification and Hearsay injection.
   Broadcast service notified: "high-priority event starting in region X"
        │
        ▼
5. Event unfolds (minutes to hours)
   Moira's actor runs its ABML behavior (steering adjusts timing)
   Players arrive organically (Gardener POIs, NPC rumors, quest hooks)
   Showtime session created (audience pools, hype train primed)
   Broadcast camera pointed at event region
        │
        ▼
6. Developer signals climax
   POST /director/event/set-phase { phase: "Climax" }
   Broadcast priority maximized. Showtime hype amplified.
   Developer may drive Moira directly for the critical moment.
        │
        ▼
7. Wind-down and completion
   Developer releases actors, removes overrides
   Event consequences propagate normally through event system
   Showtime captures metrics. Broadcast archives footage.
   POST /director/event/complete
```

### Showtime Hype Amplification

During directed events, Director can signal Showtime to prime its hype mechanics:

- **Pre-event**: Showtime increases audience pool churn rate, bringing in audience members with interest tags matching the event's content
- **During event**: Hype train trigger thresholds lowered (events become more likely to start trains). Content tags injected to match event themes.
- **Climax**: Hype train level advancement rates boosted. Peak hype events generate realm-history-level content flywheel contributions.
- **Post-event**: Normal thresholds restored. Event metrics captured in streaming session data.

This uses Showtime's existing APIs (`/showtime/audience/set-tags`, `/showtime/hype/inject`) -- Director provides timing and context, not new mechanics.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|---|---|
| lib-state (`IStateStoreFactory`) | Director sessions (MySQL), directed events (MySQL), actor taps (Redis), overrides (Redis), player targets (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for session mutations, event lifecycle transitions, actor control tier changes |
| lib-messaging (`IMessageBus`) | Publishing director lifecycle events (event created/activated/completed), tap relay setup, override broadcasts |
| lib-messaging (`IMessageSubscriber`) | Dynamic subscriptions to `director.tap.{actorId}` topics for actor observation relay |
| lib-actor (`IActorClient`) | Perception injection, actor state queries, actor listing, encounter observation (L2) |
| lib-character (`ICharacterClient`) | Character existence validation for event scoping (L2) |
| lib-realm (`IRealmClient`) | Realm existence validation for event scoping (L2) |
| lib-game-service (`IGameServiceClient`) | Game service existence validation for event scoping (L2) |
| lib-connect (`IConnectClient`) | Session awareness for player targeting, shortcut publishing for event entry (L1) |
| lib-account (`IAccountClient`) | Developer account validation (L1) |
| lib-permission (`IPermissionClient`) | Developer role verification for all endpoints (L1) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|---|---|---|
| lib-puppetmaster (`IPuppetmasterClient`) | Watcher lifecycle for event-related regional watchers, behavior cache queries | Cannot manage watchers directly; actor-level operations still work |
| lib-gardener (`IGardenerClient`) | POI injection, scenario template boosting, player garden state queries for targeting | Player steering unavailable; actors still controllable, event still trackable |
| lib-broadcast (`IBroadcastClient`) | Platform session coordination, camera management, broadcast priority signaling | No external streaming; event coordination and actor control still work |
| lib-showtime (`IShowtimeClient`) | Audience pool priming, hype train amplification, content tag injection | No in-game audience metagame effects; event coordination still works |
| lib-divine (`IDivineClient`) | Deity state queries for divine actor observation, blessing coordination during events | Cannot query divine-specific data; actor-level observation still works through Actor APIs |
| lib-hearsay (`IHearsayClient`) | Rumor injection for organic player awareness of events | No NPC rumor-based player drawing; other targeting methods still available |
| lib-quest (`IQuestClient`) | Event-related quest creation for drawing players to event regions | No quest-based player targeting; other methods still available |
| lib-analytics (`IAnalyticsClient`) | Post-event metrics (player participation, engagement, broadcast reach) | No post-event analytics; event completion still works |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|---|---|
| *(none)* | Director is a terminal L4 service with no planned consumers. It orchestrates other services; no service should depend on it. |

---

## Type Field Classification

| Field | Category | Type | Rationale |
|---|---|---|---|
| `DirectedEventStatus` | C (System State) | Service-specific enum (`planned`, `active`, `climax`, `wind_down`, `completed`, `cancelled`) | Directed event lifecycle state machine; system-owned transitions |
| `ControlTier` | C (System State) | Service-specific enum (`observe`, `steer`, `drive`) | Developer's control relationship to an actor; system-owned |
| `TargetMethod` | C (System State) | Service-specific enum (`poi_injection`, `scenario_offering`, `rumor_seeding`, `npc_behavior`, `shortcut_publishing`, `quest_hook`, `economic_incentive`) | How players are drawn to events; maps to specific service APIs |
| `TargetType` | C (System State) | Service-specific enum (`region`, `location`, `session`, `account`) | Scope of player targeting; determines which identifiers are valid |
| `DirectorSessionStatus` | C (System State) | Service-specific enum (`active`, `suspended`, `ended`) | Developer session lifecycle; system-owned |
| `actorRole` | B (Content Code) | Opaque string | Role an actor plays in a directed event (e.g., `"primary_deity"`, `"supporting_npc"`, `"environmental"`). Game-configurable, extensible without schema changes. |
| `eventCategory` | B (Content Code) | Opaque string | Category of directed event (e.g., `"world_event"`, `"regional_crisis"`, `"divine_intervention"`, `"economic_disruption"`). Game-configurable, extensible without schema changes. |

---

## State Storage

### Director Session Store
**Store**: `director-sessions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|---|---|---|
| `dsess:{sessionId}` | `DirectorSessionModel` | Director session record: developer accountId, start time, status, active taps count, active drives count, associated event IDs |
| `dsess-account:{accountId}` | `DirectorSessionModel` | Active session lookup by developer account (at most one active session per developer) |

### Directed Event Store
**Store**: `director-events` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|---|---|---|
| `devt:{eventId}` | `DirectedEventModel` | Directed event record: name, description, game service scope, realm scope, status, phase timestamps, actor list, target list, broadcast config, post-event metrics |
| `devt-active:{gameServiceId}` | `DirectedEventModel` | Active events by game service (for dashboard listing) |

### Actor Tap Store
**Store**: `director-actor-taps` (Backend: Redis, prefix: `director:tap`)

| Key Pattern | Data Type | Purpose |
|---|---|---|
| `tap:{sessionId}:{actorId}` | `ActorTapModel` | Active tap subscription: session ID, actor ID, sampling rate (every Nth tick), subscribed data streams (perceptions, variables, goap, behavior), created timestamp |
| `tap-actor:{actorId}` | `Set<Guid>` | Reverse index: which sessions are tapping this actor (for cleanup on actor deletion) |

### Override Store
**Store**: `director-overrides` (Backend: Redis, prefix: `director:ovr`)

| Key Pattern | Data Type | Purpose |
|---|---|---|
| `ovr:{actorId}:priority` | `Dictionary<string, float>` | GOAP action cost modifiers per action type for this actor |
| `ovr:{actorId}:gates` | `HashSet<string>` | Action types requiring developer approval before execution |
| `ovr:{actorId}:drive` | `DriveSessionModel` | Active drive session: developer sessionId, bound timestamp, last command timestamp |

### Player Target Store
**Store**: `director-player-targets` (Backend: Redis, prefix: `director:tgt`)

| Key Pattern | Data Type | Purpose |
|---|---|---|
| `tgt:{eventId}:{targetId}` | `PlayerTargetModel` | Player targeting record: target type, target identifier, method, priority, status (pending/active/completed), metrics (players reached, players participating) |

### Distributed Locks
**Store**: `director-lock` (Backend: Redis, prefix: `director:lock`)

| Key Pattern | Purpose |
|---|---|
| `director:lock:session:{accountId}` | Director session create/end lock (one session per developer) |
| `director:lock:event:{eventId}` | Directed event lifecycle transition lock |
| `director:lock:drive:{actorId}` | Actor drive binding lock (one driver per actor) |
| `director:lock:override:{actorId}` | Override modification lock |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|---|---|---|
| `director.session.started` | `DirectorSessionStartedEvent` | Developer starts a director session |
| `director.session.ended` | `DirectorSessionEndedEvent` | Developer ends session (or timeout) |
| `director.event.created` | `DirectorEventCreatedEvent` | Directed event created (lifecycle) |
| `director.event.updated` | `DirectorEventUpdatedEvent` | Directed event modified (lifecycle -- actors added, targets changed, etc.) |
| `director.event.activated` | `DirectorEventActivatedEvent` | Directed event moved to Active phase |
| `director.event.climax` | `DirectorEventClimaxEvent` | Directed event entered Climax phase |
| `director.event.completed` | `DirectorEventCompletedEvent` | Directed event completed (with post-event metrics) |
| `director.event.cancelled` | `DirectorEventCancelledEvent` | Directed event cancelled before completion |
| `director.actor.tapped` | `DirectorActorTappedEvent` | Developer started observing an actor |
| `director.actor.driven` | `DirectorActorDrivenEvent` | Developer took direct control of an actor |
| `director.actor.released` | `DirectorActorReleasedEvent` | Developer released control of an actor |

### Consumed Events

| Topic | Handler | Action |
|---|---|---|
| `actor.instance.deleted` | `HandleActorDeletedAsync` | Clean up all taps, overrides, and drive sessions for the deleted actor. Remove actor from any active directed events. |
| `actor.instance.status-changed` | `HandleActorStatusChangedAsync` | Update directed event actor status. If a driven actor errors, auto-release and alert the developer. |
| `session.disconnected` | `HandleSessionDisconnectedAsync` | If the disconnected session belongs to a developer with an active director session, release all driven actors (fail-open) and suspend taps. Session can be resumed on reconnection. |
| `gardener.scenario.completed` | `HandleScenarioCompletedAsync` | If the scenario was spawned by a directed event targeting method, update target metrics (players participating count). Soft -- no-op if Gardener absent. |
| `showtime.hype.completed` | `HandleHypeCompletedAsync` | If a hype train completed during a directed event, capture peak level in event metrics. Soft -- no-op if Showtime absent. |

### Resource Cleanup (T28)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|---|---|---|---|
| game-service | director | CASCADE | `/director/cleanup-by-game-service` |
| account | director | CASCADE | `/director/cleanup-by-account` |

| Trigger | Cleanup Endpoint | Action |
|---|---|---|
| Game service deleted | `/director/cleanup-by-game-service` | Cancel all active directed events for the game service, end active director sessions, clean up all taps/overrides/targets |
| Account deleted | `/director/cleanup-by-account` | End active director session for the account, release all driven actors, cancel any events created by this account |

### DI Provider Pattern

| Pattern | Interface | Registration |
|---|---|---|
| Variable Provider | `IVariableProviderFactory` | Registers `DirectorOverrideProviderFactory` providing the `director` namespace. Returns cost modifiers from `director-overrides` store for the actor's `actorId`. Returns null for all paths when no overrides are active (zero overhead for non-directed actors). |

---

## Configuration

| Property | Env Var | Default | Purpose |
|---|---|---|---|
| `DirectorEnabled` | `DIRECTOR_ENABLED` | `false` | Master feature flag. When false, all endpoints return `ServiceUnavailable`. |
| `MaxConcurrentDirectorSessions` | `DIRECTOR_MAX_CONCURRENT_SESSIONS` | `10` | Maximum simultaneously active director sessions across all developers |
| `MaxTapsPerSession` | `DIRECTOR_MAX_TAPS_PER_SESSION` | `50` | Maximum actors a single developer can observe simultaneously |
| `MaxDrivesPerSession` | `DIRECTOR_MAX_DRIVES_PER_SESSION` | `3` | Maximum actors a single developer can drive simultaneously |
| `TapDefaultSamplingRate` | `DIRECTOR_TAP_DEFAULT_SAMPLING_RATE` | `5` | Default: relay every Nth tick of actor data (1 = every tick, 5 = every 5th tick) |
| `TapVariableSnapshotRate` | `DIRECTOR_TAP_VARIABLE_SNAPSHOT_RATE` | `10` | Variable provider snapshots relayed every Nth tick (more expensive than perceptions) |
| `ActionGateTimeoutSeconds` | `DIRECTOR_ACTION_GATE_TIMEOUT_SECONDS` | `10` | Seconds before an unapproved gated action auto-approves (fail-open) |
| `DriveSessionTimeoutMinutes` | `DIRECTOR_DRIVE_SESSION_TIMEOUT_MINUTES` | `30` | Auto-release driven actors after this duration without developer commands |
| `DriveIdleTimeoutMinutes` | `DIRECTOR_DRIVE_IDLE_TIMEOUT_MINUTES` | `5` | Auto-release if developer sends no commands within this window |
| `MaxConcurrentDirectedEvents` | `DIRECTOR_MAX_CONCURRENT_EVENTS` | `5` | Maximum simultaneously active directed events per game service |
| `EventDefaultTimeoutHours` | `DIRECTOR_EVENT_DEFAULT_TIMEOUT_HOURS` | `4` | Auto-complete directed events after this duration (prevent orphaned events) |
| `GardenerAmplificationMultiplier` | `DIRECTOR_GARDENER_AMPLIFICATION_MULTIPLIER` | `2.0` | POI scoring weight boost for event-targeted players (multiplied into Gardener's normal scoring) |
| `RumorInjectionCooldownSeconds` | `DIRECTOR_RUMOR_INJECTION_COOLDOWN_SECONDS` | `60` | Minimum interval between Hearsay rumor injections for the same event (prevent flooding) |
| `BroadcastCoordinationEnabled` | `DIRECTOR_BROADCAST_COORDINATION_ENABLED` | `true` | Whether directed events automatically coordinate with Broadcast/Showtime |
| `MetricsRetentionDays` | `DIRECTOR_METRICS_RETENTION_DAYS` | `90` | How long completed event metrics are retained in MySQL |
| `DistributedLockTimeoutSeconds` | `DIRECTOR_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |

---

## DI Services & Helpers

| Service | Role |
|---|---|
| `ILogger<DirectorService>` | Structured logging |
| `DirectorServiceConfiguration` | Typed configuration access (16 properties) |
| `IStateStoreFactory` | State store access (creates 6 stores) |
| `IMessageBus` | Event publishing |
| `IMessageSubscriber` | Dynamic tap topic subscriptions |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IActorClient` | Actor perception injection, state queries, listing (L2 hard) |
| `ICharacterClient` | Character validation for event scoping (L2 hard) |
| `IRealmClient` | Realm validation for event scoping (L2 hard) |
| `IGameServiceClient` | Game service validation for event scoping (L2 hard) |
| `IConnectClient` | Session awareness, shortcut publishing (L1 hard) |
| `IAccountClient` | Developer account validation (L1 hard) |
| `IPermissionClient` | Developer role verification (L1 hard) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies (Puppetmaster, Gardener, Broadcast, Showtime, Divine, Hearsay, Quest, Analytics) |
| `IClientEventPublisher` | Push tap data and approval requests to developer's WebSocket session |
| `DirectorOverrideProviderFactory` | `IVariableProviderFactory` implementation for `${director.*}` namespace |
| `ITapRelayManager` | Manages RabbitMQ subscriptions for actor tap data relay (internal) |
| `IDriveSessionManager` | Manages actor cognition binding for drive sessions (internal) |
| `ITargetingOrchestrator` | Coordinates multi-method player targeting across services (internal) |

### Background Workers

| Worker | Purpose | Interval Config | Lock Key |
|---|---|---|---|
| `DirectorSessionTimeoutWorker` | Detects idle/timed-out director sessions and drive bindings. Releases driven actors, closes stale taps, ends abandoned sessions. | 30s (hardcoded) | `director:lock:session-timeout-worker` |
| `DirectorEventTimeoutWorker` | Auto-completes directed events that exceed `EventDefaultTimeoutHours`. Captures final metrics, releases all actors, removes all overrides. | 60s (hardcoded) | `director:lock:event-timeout-worker` |

Both workers acquire distributed locks before processing to ensure multi-instance safety.

---

## API Endpoints (Implementation Notes)

**Current status**: Pre-implementation. All endpoints described below are architectural targets.

### Director Session Management (3 endpoints)

All endpoints require `developer` role.

- **Start** (`/director/session/start`): Validates developer account and role. Checks `MaxConcurrentDirectorSessions`. Creates `DirectorSessionModel` in MySQL. Publishes `director.session.started`. Returns session ID for all subsequent calls.
- **Get** (`/director/session/get`): Returns session state including active tap count, drive count, associated events.
- **End** (`/director/session/end`): Releases all driven actors, closes all taps, removes all overrides, marks session ended. Publishes `director.session.ended`. Idempotent -- returns OK if already ended.

### Actor Observation (4 endpoints)

All endpoints require `developer` role and active director session.

- **Tap** (`/director/actor/tap`): Creates tap subscription for the specified actor. Registers RabbitMQ relay from `director.tap.{actorId}` to the developer's WebSocket session. Actor receives `director_tap_start` perception. Returns current actor state snapshot as initial payload.
- **Untap** (`/director/actor/untap`): Removes tap subscription. Actor receives `director_tap_stop` perception. Cleans up RabbitMQ relay. Idempotent.
- **GetActorState** (`/director/actor/get-state`): One-shot query of an actor's current state (feelings, goals, memories, encounter, behavior position) without establishing a persistent tap. Proxies to `IActorClient.GetActorAsync`.
- **ListActors** (`/director/actor/list`): Lists actors with optional filters (realm, category, status, within directed event). Proxies to `IActorClient.ListActorsAsync` with additional director-specific metadata (which actors are tapped, steered, driven by any director session).

### Actor Control -- Steer (4 endpoints)

All endpoints require `developer` role and active director session.

- **InjectPerception** (`/director/actor/inject-perception`): Wraps `IActorClient.InjectPerceptionAsync` with director session tracking. Records the injection in the directed event log if the actor is part of an event.
- **SetOverrides** (`/director/actor/set-overrides`): Sets GOAP priority cost modifiers for specific action types on the actor. Stored in `director-overrides` Redis store. The `DirectorOverrideProviderFactory` reads these during GOAP evaluation.
- **ClearOverrides** (`/director/actor/clear-overrides`): Removes all priority overrides for the actor. Idempotent.
- **SetActionGates** (`/director/actor/set-action-gates`): Marks specific action types as requiring developer approval before execution. Stored in `director-overrides`. The approval flow uses a RabbitMQ topic (`director.approval.{actorId}`) with timeout-based auto-approve.

### Actor Control -- Drive (3 endpoints)

All endpoints require `developer` role and active director session.

- **Drive** (`/director/actor/drive`): Acquires drive lock. Pauses the actor's ABML execution loop. Registers the developer's session as cognition source. Returns the actor's current state and available action handlers. Publishes `director.actor.driven`.
- **ExecuteAction** (`/director/actor/execute-action`): Executes an ABML-equivalent action through the actor's `IActionHandler` pipeline. Accepts the same action YAML syntax that ABML documents use (call, load_snapshot, watch, emit_perception, etc.). Returns the action result.
- **Release** (`/director/actor/release`): Resumes ABML execution from last checkpoint. Removes drive binding. Any state changes persist. Publishes `director.actor.released`. Idempotent.

### Expression Evaluation (1 endpoint)

All endpoints require `developer` role and active director session.

- **EvaluateExpression** (`/director/actor/evaluate-expression`): Evaluates an ABML expression string (e.g., `${personality.aggression > 0.7 AND encounters.last_hostile_days < 3}`) against the actor's current variable provider state. Returns the resolved value. Useful for debugging ABML behavior conditions without driving the actor.

### Directed Event Management (8 endpoints)

All endpoints require `developer` role and active director session.

- **Create** (`/director/event/create`): Validates game service and optional realm. Checks `MaxConcurrentDirectedEvents`. Creates `DirectedEventModel` in MySQL with status `Planned`. Publishes `director.event.created`.
- **Get** (`/director/event/get`): Returns full event state including actors, targets, metrics, broadcast links.
- **List** (`/director/event/list`): Active and recent events by game service, paginated. Optional status filter.
- **AddActor** (`/director/event/add-actor`): Associates an actor with the directed event. Specifies role (opaque string) and initial control tier. Actor must exist.
- **AddTarget** (`/director/event/add-target`): Adds a player targeting record. Specifies target type, identifier, method, and priority. Does not begin targeting until event is activated.
- **Activate** (`/director/event/activate`): Transitions to Active. Begins all targeting methods. Notifies Broadcast if `broadcastPriority > 0`. Publishes `director.event.activated`.
- **SetPhase** (`/director/event/set-phase`): Transitions between Active/Climax/WindDown. Adjusts Broadcast priority and Showtime amplification accordingly. Publishes phase-specific events.
- **Complete** (`/director/event/complete`): Ends all targeting, releases all driven actors, removes all overrides, captures metrics. Archives to MySQL. Publishes `director.event.completed`.

### Cleanup Endpoints (2 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByGameService** (`/director/cleanup-by-game-service`): Cancel all active events for the game service. End all sessions. Clean up all Redis state.
- **CleanupByAccount** (`/director/cleanup-by-account`): End active session for the account. Release all driven actors. Cancel events created by this account (or transfer ownership if co-managed).

---

## Visual Aid

```
+----------------------------------------------------------------------+
|                   Director Service: Human-in-the-Loop                  |
+----------------------------------------------------------------------+
|                                                                        |
|  DEVELOPER (WebSocket via Connect, developer role)                     |
|      |                                                                 |
|      +-- Director Session (one per developer)                          |
|          |                                                             |
|          +-- OBSERVE (Tier 1) ----------------------------------------+
|          |   |                                                        |
|          |   +-- Tap Actor A: perceptions, GOAP plan, feelings        |
|          |   +-- Tap Actor B: variable state, behavior position       |
|          |   +-- Tap Actor C: encounter state, memories               |
|          |   |                                                        |
|          |   Delivery: actor runtime --> RabbitMQ tap topic            |
|          |             --> Director relay --> IClientEventPublisher    |
|          |             --> Connect --> WebSocket --> developer client  |
|          |                                                            |
|          +-- STEER (Tier 2) -----------------------------------------+
|          |   |                                                        |
|          |   +-- InjectPerception: urgency-weighted perception data   |
|          |   |   (via IActorClient.InjectPerceptionAsync)             |
|          |   |                                                        |
|          |   +-- SetOverrides: GOAP cost modifiers                    |
|          |   |   (via IDirectorOverrideProviderFactory -> ${director.*})|
|          |   |                                                        |
|          |   +-- SetActionGates: approval-required action types       |
|          |   |   (via RabbitMQ director.approval.{actorId})           |
|          |   |                                                        |
|          |   +-- Gardener Amplification: POI injection, scoring boost |
|          |   +-- Hearsay Injection: NPC rumor seeding                 |
|          |   +-- Quest Hooks: event-related quest creation            |
|          |                                                            |
|          +-- DRIVE (Tier 3) -----------------------------------------+
|              |                                                        |
|              +-- Drive Actor: pause ABML, bind developer as cognition|
|              +-- ExecuteAction: same IActionHandler pipeline as ABML  |
|              +-- EvaluateExpression: query variable providers          |
|              +-- Release: resume ABML from checkpoint                 |
|                                                                        |
|  DIRECTED EVENT (multi-actor coordination)                             |
|  +--------------------------------------------------------------------+
|  |                                                                    |
|  |  Actors:       [Moira (steer), 5 NPCs (observe), Dungeon (drive)] |
|  |  Targets:      [region players (rumor), void players (POI)]        |
|  |  Broadcast:    priority 8, linked to platform session              |
|  |  Showtime:     audience primed, hype thresholds lowered            |
|  |                                                                    |
|  |  Lifecycle:    PLANNED --> ACTIVE --> CLIMAX --> WIND_DOWN         |
|  |                                      --> COMPLETED                 |
|  +--------------------------------------------------------------------+
|                                                                        |
|  DEPLOYMENT GRADIENT                                                   |
|  +--------------------------------------------------------------------+
|  |  Early:   Director REQUIRED -- developer drives god-actors          |
|  |  Growing: Director ASSISTS  -- developer steers, system generates   |
|  |  Mature:  Director OBSERVES -- developer monitors, rarely intervenes|
|  |  Full:    Director UNUSED   -- autonomous divine actor system       |
|  +--------------------------------------------------------------------+
|                                                                        |
+------------------------------------------------------------------------+
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 1: Schema & Generation
- Create `director-api.yaml` schema with all endpoints (25 endpoints across 7 groups)
- Create `director-events.yaml` schema (11 published events, 5 consumed events)
- Create `director-configuration.yaml` schema (16 configuration properties)
- Create `director-client-events.yaml` (tap data relay, approval requests, event status updates)
- Generate service code
- Verify build succeeds

### Phase 2: Director Session & Actor Observation
- Implement director session lifecycle (start, get, end)
- Implement actor tap/untap with RabbitMQ relay
- Implement tap data delivery via `IClientEventPublisher`
- Implement `GetActorState` and `ListActors` proxies with director metadata
- Implement session timeout background worker

### Phase 3: Actor Steering
- Implement perception injection wrapper with session tracking
- Implement `DirectorOverrideProviderFactory` as `IVariableProviderFactory`
- Implement GOAP override store and provider integration
- Implement action gate mechanism with approval flow
- Test: verify GOAP plans change with overrides active

### Phase 4: Actor Driving
- Implement drive binding (pause ABML, bind developer session)
- Implement `ExecuteAction` routing through `IActionHandler` pipeline
- Implement `EvaluateExpression` for variable provider queries
- Implement release with ABML resume from checkpoint
- Implement drive timeout worker
- Test: verify developer commands produce identical results to ABML-driven commands

### Phase 5: Directed Event Orchestration
- Implement directed event CRUD and lifecycle state machine
- Implement actor association and target management
- Implement player targeting orchestration across services
- Implement event timeout background worker
- Test: verify targeting methods produce organic player flow

### Phase 6: Broadcast & Showtime Integration
- Implement Broadcast priority signaling on event activation
- Implement Showtime hype amplification on phase transitions
- Implement content tag injection for event themes
- Implement post-event metric capture from Broadcast/Showtime data
- Test: verify broadcast coordination produces streaming content

### Phase 7: Client Dashboard
- Define `director-client-events.yaml` for real-time dashboard updates
- Implement tap data streaming protocol (sampling, batching, delta compression)
- Implement approval request/response protocol
- Implement event status push notifications
- Design client SDK integration for director dashboard UI

---

## Potential Extensions

1. **Replay system**: Record all tap data during a directed event for post-hoc replay. Store perception streams, variable snapshots, and developer commands as a time-indexed archive. Enable replaying events from any actor's perspective. Uses Save-Load for persistence and Asset for binary storage.

2. **Multi-developer collaboration**: Multiple developers coordinate on the same directed event. One drives Moira while another steers nearby NPCs. Requires role-based access within directed events (event lead, actor driver, observer) and conflict resolution for overlapping overrides.

3. **Automated directed event triggers**: Director monitors actor tap data for patterns that indicate broadcast-worthy events forming (high GOAP plan complexity, multiple gods converging on a region, rare archive being processed by Storyline). Notifies developers with a "potential event detected" alert. Semi-automated: detection is algorithmic, decision to activate is human.

4. **Event templates**: Reusable directed event configurations (actor roles, targeting methods, broadcast setup) that developers can instantiate for recurring event types. "Regional crisis" template, "divine intervention" template, "economic disruption" template. Configuration, not code.

5. **Post-event content generation**: When a directed event completes, Director packages the event timeline (actor decisions, player participation, broadcast metrics, outcome) as structured data that Storyline can consume for narrative composition. Directed events become explicit content flywheel inputs -- the developer's coordination itself generates future content.

6. **Training mode**: New developers practice directing in a sandboxed environment. Director spawns a temporary realm with pre-configured actors and scenarios. Actions are real (same APIs, same actor system) but isolated from production world state. Uses Orchestrator processing pools for isolated instances.

7. **Variable Provider Factory**: `IDirectorVariableProviderFactory` providing `${director.*}` namespace to all actors (not just overridden ones). Could expose: `${director.event_active}` (boolean -- is a directed event active in this region), `${director.broadcast_priority}` (current broadcast priority level), enabling NPC behaviors that react to the knowledge that "something important is happening" without knowing the specific event. NPC actors could heighten awareness or seek shelter during high-priority events.

8. **Metrics dashboard integration**: Real-time director dashboard showing event health: player density in region, NPC engagement levels, Showtime audience metrics, broadcast viewer count, hype train status. Composable from existing service APIs (Gardener garden state, Showtime audience snapshot, Broadcast viewer count, Actor list with regional filtering).

9. **A/B event comparison**: Run two directed events of similar type with different coordination strategies and compare outcomes (player engagement, broadcast reach, hype metrics, post-event content generation). Director captures standardized metrics that enable data-driven improvement of event coordination strategies.

10. **Voice channel coordination**: During directed events, Director could create and manage voice rooms via lib-voice for developer-to-developer communication (coordination channel) and developer-to-player communication (event narration, quest delivery via NPC voice). The developer speaks "as" a god-actor -- voice routed through the actor system so players hear divine speech.

---

## Composability Map

Director session and event management are owned here. Actor observation and control routing is owned here. All other capabilities are composed from existing services:

| Director Concern | Composed From |
|---|---|
| Actor observation | Actor state queries (L2), RabbitMQ tap topics, IClientEventPublisher |
| Actor steering | Actor perception injection (L2), `IVariableProviderFactory` for GOAP overrides |
| Actor driving | Actor's `IActionHandler` pipeline (L2), ABML execution pause/resume |
| Player targeting | Gardener POIs/scenarios (L4), Hearsay rumors (L4), Quest hooks (L2), Connect shortcuts (L1), Currency economic incentives (L2) |
| Broadcast coordination | Broadcast platform sessions (L3), Showtime audience/hype (L4) |
| Event scoping | Game Service, Realm, Character (L2) for entity validation |
| Developer identity | Account (L1), Permission (L1) for role verification |

The Director follows the same structural pattern as lib-divine, lib-showtime, lib-escrow, lib-quest -- an L4 orchestration layer that composes existing Bannou primitives to deliver a specific domain capability. Where lib-divine orchestrates blessings and divinity economy, and lib-showtime orchestrates audience dynamics and streamer career, lib-director orchestrates developer-driven event coordination and actor supervision. They are parallel orchestration layers composing the same underlying primitives.

**The critical differentiator**: Director is the only service where the cognition source is a human, not ABML bytecode. Every other orchestration layer (Divine, Puppetmaster, Gardener) ultimately runs ABML behaviors on the Actor runtime. Director replaces or augments that cognition with human judgment, using the same downstream APIs. This makes it both a production tool and an integration testing mechanism for the entire actor system.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Fail-open everywhere**: Every mechanism that involves developer input has a timeout that defaults to "proceed without the developer." Action gates auto-approve. Drive sessions auto-release. Directed events auto-complete. The autonomous system never waits for a human. This is deliberate -- Director enhances but never blocks the living world.

2. **One session per developer**: Each developer account can have at most one active director session. This prevents confusion from multiple sessions competing for the same actors. Multiple developers can operate simultaneously by each having their own session, but they must coordinate directed event ownership explicitly.

3. **Drive is exclusive**: Only one developer can drive an actor at a time. Attempting to drive an already-driven actor returns `Conflict`. This prevents conflicting commands from multiple developers. Observation (tap) is non-exclusive -- many developers can tap the same actor.

4. **Overrides are additive**: GOAP cost modifiers from Director add to (not replace) existing cost factors from Obligation, Faction, and personality. A developer can make an action more or less expensive, but cannot make an actor ignore its own personality or obligations entirely. The actor's autonomy is modulated, not overridden.

5. **No player notification**: Players never know a directed event is happening. There is no "GM event" indicator, no special UI, no announcement system from Director. Players discover events through organic mechanisms (NPC rumors, quest hooks, POIs, environmental changes). The directed nature is invisible to preserve immersion.

6. **Directed event actors continue autonomously**: Adding an actor to a directed event at Tier 1 (observe) does not affect the actor's behavior at all. Even at Tier 2 (steer), the actor's ABML behavior continues running -- overrides modulate decisions but don't replace them. Only Tier 3 (drive) pauses autonomous behavior.

7. **Metrics are best-effort**: Post-event metrics (player count, broadcast reach, hype peaks) are captured from soft dependencies. If Showtime is unavailable, those metrics are zero -- not an error. The event completion flow never fails due to missing metrics.

8. **Developer role required for ALL endpoints**: Unlike most Bannou services where endpoints have mixed permission requirements, every Director endpoint requires the `developer` role. This is a security boundary -- Director provides powerful actor manipulation capabilities that should never be accessible to normal players.

### Design Considerations (Requires Planning)

1. **Tap data protocol design**: The RabbitMQ tap relay needs careful protocol design. Raw per-tick actor state is too much data for WebSocket delivery. Options: delta compression (only send changed fields), sampling (every Nth tick), priority-based filtering (only send perceptions above urgency threshold), or configurable data stream selection (perception-only, variable-only, etc.). The configuration properties provide knobs but the protocol format needs specification.

2. **Drive session checkpoint mechanism**: When a developer releases a driven actor, the ABML execution must resume from a meaningful point. The simplest approach: resume from the `on_tick` entry point with all state changes preserved (feelings, goals, memories the developer set during the drive session are available to ABML). This avoids needing to track execution position within the ABML document, which would be complex. The trade-off is that any in-progress ABML action chain is abandoned on drive, then restarted from tick entry on release.

3. **GOAP override provider discovery**: The `DirectorOverrideProviderFactory` needs to check Redis on every GOAP evaluation for the actor's overrides. For non-directed actors (the vast majority), this is a Redis miss that should be fast. For directed actors, it's a Redis hit with small payload. Caching is dangerous because overrides can change mid-plan. A `ConcurrentDictionary` local cache with RabbitMQ invalidation might be needed for 100K+ actor scale.

4. **Cross-node actor driving**: In pool deployment mode, a driven actor runs on a pool node while the developer's session is on the main Bannou node. Drive commands must route through the mesh to the correct pool node. The existing `IMeshInvocationClient` forwarding pattern (used by Actor for remote pool operations) applies, but the interactive latency requirements for drive sessions may need optimization.

5. **Directed event persistence scope**: Directed events are MySQL-persisted for post-hoc analysis, but the active coordination state (taps, overrides, drive sessions) is Redis-ephemeral. If Redis restarts during an active event, overrides and taps are lost but the event record survives. Should overrides be reconstructable from the event record, or is "restart interrupts active coordination" an acceptable failure mode?

6. **Gardener integration depth**: How deeply should Director integrate with Gardener for player targeting? Option A: Director calls Gardener APIs directly (spawn POI, create template, boost scores). Option B: Director publishes targeting intents and Gardener has a consumer that implements them. Option A is simpler but tightly couples. Option B is cleaner but requires Gardener to understand Director targeting concepts.

7. **Action handler pipeline access for drive sessions**: The `ExecuteAction` endpoint needs to route through the actor's `IActionHandler` pipeline. In bannou mode, this is direct access to the ActorRunner's document executor. In pool mode, this requires forwarding the action to the pool node, executing it in the actor's context, and returning the result. The existing pool command pattern (RabbitMQ commands to `actor.node.{appId}.*`) may need a request-reply variant for synchronous drive commands.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase.*
