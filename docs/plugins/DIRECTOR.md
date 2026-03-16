# Director Plugin Deep Dive

> **Plugin**: lib-director (not yet created)
> **Schema**: `schemas/director-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: director-sessions (MySQL), director-events (MySQL), director-actor-taps (Redis), director-overrides (Redis), director-player-targets (Redis), director-lock (Redis) -- all planned
> **Layer**: GameFeatures
> **Status**: Aspirational -- no schema, no generated code, no service implementation exists.
> **Implementation Map**: [docs/maps/DIRECTOR.md](../maps/DIRECTOR.md)
> **Planning**: N/A
> **Short**: Human-in-the-loop event coordination (Observe/Steer/Drive tiers) for live content management

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
| **GOAP priority override** | Director stores priority modifiers in `director-overrides` Redis store; Actor's GOAP planner reads these as additional cost modifiers via `DirectorOverrideProviderFactory` (standard `IVariableProviderFactory`) | Action costs shift, causing different plan selection |
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
 name: string # Human-readable event name
 description: string # What this event is about
 gameServiceId: Guid # Scoped to a game service
 realmId: Guid? # Optional realm scope
 status: DirectedEventStatus # Planned, Active, Climax, WindDown, Completed
 createdBySessionId: Guid # WebSocket session ID of the creating developer
 createdAt: DateTime
 startedAt: DateTime? # When status moved to Active
 completedAt: DateTime?

 # Actor tracking
 actors: DirectedEventActor[] # Actors involved in this event
 # Each actor has: actorId, role (string), controlTier, assignedToSessionId (Guid?)

 # Player targeting
 playerTargets: DirectedEventTarget[] # Player populations to draw in
 # Each target has: targetType (region/location/session), targetId, priority, method

 # Broadcast coordination
 broadcastPriority: int # 0 = no broadcast, 1-10 = priority for Broadcast/Showtime
 broadcastSessionId: Guid? # Linked lib-broadcast platform session
 showtimeSessionId: Guid? # Linked lib-showtime in-game session
```

### Directed Event Lifecycle

```
Planned Developer creates the event, assigns actors and targets.
 │ Actors continue running autonomously. No player impact yet.
 │
 ▼ [developer activates]
Active Director begins steering: perception injection, GOAP overrides,
 │ Gardener amplification. Players start being drawn toward the region.
 │ Broadcast may begin recording/streaming.
 │
 ▼ [developer signals climax]
Climax Peak coordination. Director may drive key actors directly.
 │ Broadcast priority at maximum. Showtime hype mechanics active.
 │ Gardener aggressively spawns event-related POIs and scenarios.
 │
 ▼ [developer signals wind-down]
WindDown Director releases driven actors. GOAP overrides removed gradually.
 │ Broadcast continues but de-prioritized. Gardener returns to normal.
 │ Event consequences propagate through normal event system.
 │
 ▼ [developer completes]
Completed All taps closed. All overrides removed. Event archived to MySQL.
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

## Visual Aid

```
+----------------------------------------------------------------------+
| Director Service: Human-in-the-Loop |
+----------------------------------------------------------------------+
| |
| DEVELOPER (WebSocket via Connect, developer role) |
| | |
| +-- Director Session (one per WebSocket connection) |
| | |
| +-- OBSERVE (Tier 1) ----------------------------------------+
| | | |
| | +-- Tap Actor A: perceptions, GOAP plan, feelings |
| | +-- Tap Actor B: variable state, behavior position |
| | +-- Tap Actor C: encounter state, memories |
| | | |
| | Delivery: actor runtime --> RabbitMQ tap topic |
| | --> Director relay --> IClientEventPublisher |
| | --> Connect --> WebSocket --> developer client |
| | |
| +-- STEER (Tier 2) -----------------------------------------+
| | | |
| | +-- InjectPerception: urgency-weighted perception data |
| | | (via IActorClient.InjectPerceptionAsync) |
| | | |
| | +-- SetOverrides: GOAP cost modifiers |
| | | (via DirectorOverrideProviderFactory -> ${director.*}) |
| | | |
| | +-- SetActionGates: approval-required action types |
| | | (via RabbitMQ director.approval.{actorId}) |
| | | |
| | +-- Gardener Amplification: POI injection, scoring boost |
| | +-- Hearsay Injection: NPC rumor seeding |
| | +-- Quest Hooks: event-related quest creation |
| | |
| +-- DRIVE (Tier 3) -----------------------------------------+
| | |
| +-- Drive Actor: pause ABML, bind developer as cognition|
| +-- ExecuteAction: same IActionHandler pipeline as ABML |
| +-- EvaluateExpression: query variable providers |
| +-- Release: resume ABML from checkpoint |
| |
| DIRECTED EVENT (multi-actor coordination) |
| +--------------------------------------------------------------------+
| | |
| | Actors: [Moira (steer), 5 NPCs (observe), Dungeon (drive)] |
| | Targets: [region players (rumor), void players (POI)] |
| | Broadcast: priority 8, linked to platform session |
| | Showtime: audience primed, hype thresholds lowered |
| | |
| | Lifecycle: Planned --> Active --> Climax --> WindDown |
| | --> Completed |
| +--------------------------------------------------------------------+
| |
| DEPLOYMENT GRADIENT |
| +--------------------------------------------------------------------+
| | Early: Director REQUIRED -- developer drives god-actors |
| | Growing: Director ASSISTS -- developer steers, system generates |
| | Mature: Director OBSERVES -- developer monitors, rarely intervenes|
| | Full: Director UNUSED -- autonomous divine actor system |
| +--------------------------------------------------------------------+
| |
+------------------------------------------------------------------------+
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 1: Schema & Generation
- Create `director-api.yaml` schema with all endpoints (24 endpoints across 7 groups)
- Create `director-events.yaml` schema (9 published events: 6 x-lifecycle + 3 custom domain, 3 consumed events)
- Create `director-configuration.yaml` schema (17 configuration properties)
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
- Implement post-event metric publication to Analytics via events
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

7. **Variable Provider Factory expansion**: Expand `DirectorOverrideProviderFactory` (standard `IVariableProviderFactory`) to provide `${director.*}` namespace to all actors (not just overridden ones). Could expose: `${director.event_active}` (boolean -- is a directed event active in this region), `${director.broadcast_priority}` (current broadcast priority level), enabling NPC behaviors that react to the knowledge that "something important is happening" without knowing the specific event. NPC actors could heighten awareness or seek shelter during high-priority events.

8. **Metrics dashboard integration**: Real-time director dashboard showing event health: player density in region, NPC engagement levels, Showtime audience metrics, broadcast viewer count, hype train status. Composable from existing service APIs (Gardener garden state, Showtime audience snapshot, Broadcast viewer count, Actor list with regional filtering).

9. **A/B event comparison**: Run two directed events of similar type with different coordination strategies and compare outcomes (player engagement, broadcast reach, hype metrics, post-event content generation). Director captures standardized metrics that enable data-driven improvement of event coordination strategies.

10. **Voice channel coordination**: During directed events, Director could create and manage voice rooms via lib-voice for developer-to-developer communication (coordination channel) and developer-to-player communication (event narration, quest delivery via NPC voice). The developer speaks "as" a god-actor -- voice routed through the actor system so players hear divine speech.

---

## Type Field Classification

| Field | Category | Type | Rationale |
|---|---|---|---|
| `DirectedEventStatus` | C (System State) | Service-specific enum (`Planned`, `Active`, `Climax`, `WindDown`, `Completed`, `Cancelled`) | Directed event lifecycle state machine; system-owned transitions |
| `ControlTier` | C (System State) | Service-specific enum (`Observe`, `Steer`, `Drive`) | Developer's control relationship to an actor; system-owned |
| `TargetMethod` | C (System State) | Service-specific enum (`PoiInjection`, `ScenarioOffering`, `RumorSeeding`, `NpcBehavior`, `ShortcutPublishing`, `QuestHook`, `EconomicIncentive`) | How players are drawn to events; maps to specific service APIs |
| `TargetType` | C (System State) | Service-specific enum (`Region`, `Location`, `Session`) | Scope of player targeting; determines which identifiers are valid |
| `DirectorSessionStatus` | C (System State) | Service-specific enum (`Active`, `Suspended`, `Ended`) | Developer session lifecycle; system-owned |
| `actorRole` | B (Content Code) | Opaque string | Role an actor plays in a directed event (e.g., `"primary_deity"`, `"supporting_npc"`, `"environmental"`). Game-configurable, extensible without schema changes. |
| `eventCategory` | B (Content Code) | Opaque string | Category of directed event (e.g., `"world_event"`, `"regional_crisis"`, `"divine_intervention"`, `"economic_disruption"`). Game-configurable, extensible without schema changes. |

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
| Developer identity | Connect (L1) for session-to-developer resolution, Permission (L1) for role verification |

The Director follows the same structural pattern as lib-divine, lib-showtime, lib-escrow, lib-quest -- an L4 orchestration layer that composes existing Bannou primitives to deliver a specific domain capability. Where lib-divine orchestrates blessings and divinity economy, and lib-showtime orchestrates audience dynamics and streamer career, lib-director orchestrates developer-driven event coordination and actor supervision. They are parallel orchestration layers composing the same underlying primitives.

**The critical differentiator**: Director is the only service where the cognition source is a human, not ABML bytecode. Every other orchestration layer (Divine, Puppetmaster, Gardener) ultimately runs ABML behaviors on the Actor runtime. Director replaces or augments that cognition with human judgment, using the same downstream APIs. This makes it both a production tool and an integration testing mechanism for the entire actor system.

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

1. Developer observes actor tap data — "Moira is about to commission a legendary ghost quest from the archive of Seraphina the Dragonslayer"
2. Developer creates directed event — `POST /director/event/create` with name, realmId, broadcastPriority
3. Developer adds actors and targets — associates actors at various control tiers, adds player targeting records
4. Developer activates event — Director begins Gardener amplification and Hearsay injection; Broadcast notified
5. Event unfolds (minutes to hours) — actors run ABML behaviors (steering adjusts timing), players arrive organically, Showtime session created, Broadcast camera pointed at event region
6. Developer signals climax — Broadcast priority maximized, Showtime hype amplified, developer may drive key actors directly
7. Wind-down and completion — developer releases actors, removes overrides, event consequences propagate, Director publishes analytics events

### Showtime Hype Amplification

During directed events, Director can signal Showtime to prime its hype mechanics:

- **Pre-event**: Showtime increases audience pool churn rate, bringing in audience members with interest tags matching the event's content
- **During event**: Hype train trigger thresholds lowered (events become more likely to start trains). Content tags injected to match event themes.
- **Climax**: Hype train level advancement rates boosted. Peak hype events generate realm-history-level content flywheel contributions.
- **Post-event**: Normal thresholds restored. Director publishes analytics events for the event timeline.

This uses Showtime's existing APIs (`/showtime/audience/set-tags`, `/showtime/hype/inject`) -- Director provides timing and context, not new mechanics.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(No active bugs)*

### Intentional Quirks (Documented Behavior)

1. **Fail-open everywhere**: Every mechanism that involves developer input has a timeout that defaults to "proceed without the developer." Action gates auto-approve. Drive sessions auto-release. Directed events auto-complete. The autonomous system never waits for a human. This is deliberate -- Director enhances but never blocks the living world.

2. **One session per WebSocket connection**: Each WebSocket connection can have at most one active director session. This prevents confusion from multiple sessions competing for the same actors. Multiple developers can operate simultaneously by each having their own session on their own connection, but they must coordinate directed event ownership explicitly.

3. **Drive is exclusive**: Only one developer can drive an actor at a time. Attempting to drive an already-driven actor returns `Conflict`. This prevents conflicting commands from multiple developers. Observation (tap) is non-exclusive -- many developers can tap the same actor.

4. **Overrides are additive**: GOAP cost modifiers from Director add to (not replace) existing cost factors from Obligation, Faction, and personality. A developer can make an action more or less expensive, but cannot make an actor ignore its own personality or obligations entirely. The actor's autonomy is modulated, not overridden.

5. **No player notification**: Players never know a directed event is happening. There is no "GM event" indicator, no special UI, no announcement system from Director. Players discover events through organic mechanisms (NPC rumors, quest hooks, POIs, environmental changes). The directed nature is invisible to preserve immersion.

6. **Directed event actors continue autonomously**: Adding an actor to a directed event at Tier 1 (observe) does not affect the actor's behavior at all. Even at Tier 2 (steer), the actor's ABML behavior continues running -- overrides modulate decisions but don't replace them. Only Tier 3 (drive) pauses autonomous behavior.

7. **Post-event analytics are best-effort**: On event completion, Director publishes analytics events with event timeline data. If Analytics is unavailable, the event still completes successfully -- metrics capture is fire-and-forget via events.

8. **Developer role required for ALL endpoints**: Unlike most Bannou services where endpoints have mixed permission requirements, every Director endpoint requires the `developer` role. This is a security boundary -- Director provides powerful actor manipulation capabilities that should never be accessible to normal players.

### Design Considerations (Requires Planning)

1. **Tap data protocol design**: The RabbitMQ tap relay needs careful protocol design. Raw per-tick actor state is too much data for WebSocket delivery. Options: delta compression (only send changed fields), sampling (every Nth tick), priority-based filtering (only send perceptions above urgency threshold), or configurable data stream selection (perception-only, variable-only, etc.). The configuration properties provide knobs but the protocol format needs specification.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/609 -->

2. **Drive session checkpoint mechanism**: When a developer releases a driven actor, the ABML execution must resume from a meaningful point. The simplest approach: resume from the `on_tick` entry point with all state changes preserved (feelings, goals, memories the developer set during the drive session are available to ABML). This avoids needing to track execution position within the ABML document, which would be complex. The trade-off is that any in-progress ABML action chain is abandoned on drive, then restarted from tick entry on release.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/612 -->

3. **Action gate approval mechanism design**: The action gate description says "Actor's execution pauses before those actions and publishes an approval request to `director.approval.{actorId}`" with timeout-based auto-approve. The mechanism for how Actor (L2) receives the approval/denial response from Director (L4) is unspecified. If Actor subscribes to a Director response topic, that creates a lower-layer subscribing to higher-layer events (forbidden per Foundation Tenets Cross-Service Communication Discipline). If Actor polls a Redis key that Director writes, Actor's correctness depends on Director's Redis state. The implementation must use only generic hooks in Actor (e.g., a generic pre-action gate via DI Provider that any plugin can implement) to avoid L2→L4 conceptual coupling. Open questions: (1) synchronous blocking (pauses actor tick) vs asynchronous (skip and re-evaluate next tick) vs hybrid (queue with timeout) — each has gameplay consequences; (2) generic `IActionGateProvider` interface vs Director-specific; (3) impact on GOAP plan consistency if actions are deferred.
<!-- AUDIT:NEEDS_DESIGN:2026-03-16:https://github.com/beyond-immersion/bannou-service/issues/668 -->

---

## Work Tracking

### Completed

- **Dependency classification fix** (2026-03-08): Moved lib-quest (`IQuestClient`) from soft to hard dependency. L2 dependencies are always hard for L4 services per SERVICE-HIERARCHY.md. Updated dependency table and DI Services list.
- **Event type naming fix** (2026-03-08): Renamed lifecycle event types from `DirectedEvent*Event` to `DirectorEvent*Event`. With `topic_prefix: director`, the x-lifecycle entity must be `DirectorEvent` (kebab: `director-event`, starts with `director-`) to produce topic `director.event.*` per SCHEMA-RULES.md Pattern C naming rules. `DirectedEvent` (kebab: `directed-event`) would produce `director.directed-event.*` instead.
- **GOAP override provider discovery resolved** (2026-03-08): Design Consideration #3 resolved — the established Variable Provider Factory caching pattern (used by all 15 existing IVariableProviderFactory implementations) fully answers this concern. Per-tick Redis reads with service-owned cache interface is the standard approach.
- **Gardener integration depth resolved** (2026-03-08): Design Consideration #6 resolved — mandates direct API calls for same-layer (L4→L4) dependencies with graceful degradation. Option B (publish targeting intents) is the forbidden Inverted Subscription Anti-Pattern. Standard `GetService<IGardenerClient>()` + null check pattern applies.
- **Cross-node actor driving resolved** (2026-03-08): Design Considerations #4 and #7 resolved — Actor's encounter operations already implement transparent cross-node forwarding via `IMeshInvocationClient`. Director drive commands follow the same mesh forwarding pattern. Actor-side Tier 3 prerequisites (pause/resume, action handler access) tracked in Actor issue #599.
- **Directed event persistence scope resolved** (2026-03-08): Design Consideration #5 resolved — "restart interrupts active coordination" is the correct failure mode per universal Bannou Redis-ephemeral patterns (Connect sessions, Permission capabilities, Actor scheduled events). No service reconstructs Redis state from MySQL. Developer re-establishes coordination through normal APIs after Redis recovery.
- **Resource cleanup design corrected** (2026-03-16): Bugs #1 and #2 fixed — actor deletion cleanup split: Redis-ephemeral state (taps, overrides, drives) cleaned via `actor.instance.deleted` event (live session management), MySQL `DirectedEventModel` actor removal via lib-resource cleanup callback (`/director/cleanup-by-actor`, `onDelete: detach`). Added `x-references` for both `actor` and `realm` targets to Resource Cleanup table.
