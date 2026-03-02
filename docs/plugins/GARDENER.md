# Gardener Plugin Deep Dive

> **Plugin**: lib-gardener
> **Schema**: schemas/gardener-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: gardener-garden-instances (Redis), gardener-pois (Redis), gardener-scenario-templates (MySQL), gardener-scenario-instances (Redis), gardener-scenario-history (MySQL), gardener-phase-config (MySQL), gardener-lock (Redis)

## Overview

Player experience orchestration service (L4 GameFeatures) and the player-side counterpart to Puppetmaster: where Puppetmaster orchestrates what NPCs experience, Gardener orchestrates what players experience. A "garden" is an abstract conceptual space (lobby, in-game, housing, void/discovery) that a player inhabits, with Gardener managing their gameplay context, entity associations, and event routing. Provides the APIs and infrastructure that divine actors (running via Puppetmaster on the L2 Actor runtime) use to manipulate player experiences -- behavior-agnostic, providing primitives not policy. Currently implements the void/discovery garden type only; the broader garden concept (multiple types, garden-to-garden transitions) is the architectural target. Internal-only, never internet-facing.

## The Garden Concept (Architectural Target)

> **Status**: Conceptual framework. The current implementation covers the void/discovery garden type only. This section describes the full architectural vision.

### Gardens as Abstract Spaces

A garden is not a physical location -- it is a **conceptual space that defines a player's current gameplay context**. Every player is always in some garden. The garden determines what entities the player interacts with, what events reach them, and how the gardener behavior orchestrates their experience.

| Garden Type | Conceptual Space | Example |
|-------------|-----------------|---------|
| **Discovery** | The void from PLAYER-VISION.md | Spirit drifting, encountering POIs, entering scenarios |
| **Lobby** | Pre-game gathering space | Waiting room before a match, character selection, loadout |
| **In-Game** | Active gameplay experience | Combat encounter, exploration, crafting session |
| **Post-Game** | Results and transition space | Score screen, growth awards, next-scenario selection |
| **Housing** | Player-owned persistent space | Home base, workshop, garden (literal), trophy room |
| **Cooperative** | Shared multi-player space | Co-op dungeon, guild hall, trade marketplace |

Each garden has:
- **A seed** representing its state, capabilities, and growth progression
- **Entity associations** -- collections, inventories, characters available in this context
- **A gardener behavior** (or manual API calls) controlling what the player experiences

### Housing Garden Pattern (No Plugin Required)

Player housing does not require a dedicated `lib-housing` plugin. It composes entirely from existing primitives -- the same pattern as the void/discovery experience, and structurally identical to the dungeon master's Pattern A full-garden experience. See [VOXEL-BUILDER-SDK.md](../planning/VOXEL-BUILDER-SDK.md) for the SDK that enables interactive voxel construction within housing gardens.

| Housing Concern | Solved By |
|----------------|-----------|
| "A conceptual space the player inhabits" | **Gardener** (garden type: `housing`) |
| "Capabilities emerge from growth" | **Seed** (housing seed type -- phases unlock rooms, decorations, NPC servants, visitor capacity) |
| "Physical layout stored persistently" | **Scene** (node tree of housing objects, including `voxel` node types for player-built structures) + **Save-Load** (versioned persistence with chunk-level delta saves for voxel data) |
| "Interactive voxel building" | **Voxel Builder SDK** (pure computation -- brush, fill, mirror, WFC generation; engine bridges render; runs on both client and server) |
| "Rendered to the client on demand" | **Scene Composer SDK** + **Voxel Builder SDK** (engine bridges render the scene graph and voxel nodes respectively) |
| "A god tends the space" | **Divine/Puppetmaster** (gardener god-actor manages the housing experience per [BEHAVIORAL-BOOTSTRAP.md](../guides/BEHAVIORAL-BOOTSTRAP.md)) |
| "Items placed in the space" | **Inventory** (housing container) + **Item** (furniture/decorations as item instances with `placed_scene_id`/`placed_node_id` custom stats) |
| "Visitors can enter" | **Game Session** (housing visit = game session) + **Permission** (owner/visitor/trusted_friend role matrix) |
| "Entity events route to player" | **Entity Session Registry** in Connect (L1), per [Issue #426](https://github.com/beyond-immersion/bannou-service/issues/426) |

The flow mirrors the void experience exactly:

1. Player selects housing seed from the void (garden-to-garden transition: discovery → housing)
2. Gardener creates `housing` garden instance, binds per-garden entity associations (housing seed, inventory, scene)
3. Gardener god-actor begins tending (spawns NPC servants, manages visitors, seasonal changes, event reactions)
4. Scene data loaded → SceneComposer renders the node tree, VoxelBuilder renders voxel nodes
5. Player interacts: placing furniture goes through Inventory/Item (ABML behavior: checkout scene → remove item from inventory → add node with item metadata → commit), voxel editing goes through the SDK's operation system
6. Seed grows as player engages → unlocks capabilities (bigger space, more decoration slots, crafting stations, garden plots)
7. Save-Load persists the scene + voxel delta saves (chunk-aligned .bvox format means only modified 16x16x16 chunks re-serialize); Asset stores the base voxel data in MinIO

**Item ↔ Scene node binding** is handled by convention: Item instances use `customStats` to track placement (`placed_scene_id`, `placed_node_id`), and Scene nodes carry `annotations.item.instanceId` referencing back. The gardener behavior enforces consistency, with Scene checkout as the implicit transaction boundary. If the scene commit fails after an inventory removal, the behavior compensates by returning the item to inventory.

**Why this works without a plugin**: The garden IS the housing instance. The seed IS the capability system. Scene IS the layout. Save-Load IS the persistence. The Voxel Builder SDK IS the construction tool. The gardener god-actor IS the experience manager. There is no remaining concern that needs a dedicated service -- only authored content (housing seed type definitions, gardener behavior documents, item templates for furniture, Scene validation rules for spatial constraints).

### Gardener Behavior Actor Pattern (Divine Actor Unification)

The gardener behavior actor is a **divine actor** -- the same entity type that Puppetmaster launches as regional watchers to orchestrate NPC experiences in physical realms. From a god's perspective, tending a player's conceptual garden and tending a physical realm region are the same operation with different tools. The Gardener service provides the tools (garden instances, POIs, scenarios, entity associations); the divine actor's ABML behavior document determines when and how to use them.

**Dynamic character binding** enriches gardener behavior actors. A divine actor tending a garden can start as an event brain (basic orchestration: spawn POIs, manage transitions using fixed rules) and later bind to its divine character via `/actor/bind-character`. After binding, the god's `${personality.*}` traits influence garden-tending decisions: a merciful god spawns gentler scenarios for struggling players, a mischievous god spawns surprising content at unexpected moments, a wrathful god escalates challenge intensity. The ABML behavior document supports both modes -- before binding, it uses default orchestration paths; after binding, it uses personality-driven paths. This means gardens can start being tended immediately (event brain mode) while divine character profiles are still being provisioned.

```
lib-divine (L4) ── deity identity, economy, blessings
    │
    ├── Realm-tending (via Puppetmaster)        Garden-tending (via Gardener)
    │   God's Actor monitors realm events       God's Actor monitors player drift/events
    │   Spawns encounters, adjusts NPCs         Spawns POIs, manages transitions
    │   Tools: watch, load_snapshot,            Tools: Gardener APIs (enter garden,
    │          spawn_watcher, emit_perception          spawn POI, enter scenario, etc.)
    │           │                                       │
    │           └──── Both run on Actor Runtime (L2) ───┘
    │                 Same ABML bytecode interpreter
    │                 Same divine actor identity
    │                 Dynamic character binding:
    │                   start event brain → bind divine character → personality-driven
    │
    └── Any conceptual space can become physical and vice versa
        (the god shifts focus between garden types)
```

**Multi-game variability**: The gardener behavior can vary significantly between games. In Arcadia, the void/discovery experience is tended by divine actors using drift-based POI scoring and seed growth. A different game might use different deities (or non-deity actors entirely) for garden orchestration, or drive Gardener APIs directly from the game engine without an actor. The Gardener service is behavior-agnostic -- it provides primitives, not policy.

### Always-Active Lifecycle

The player never leaves Gardener's domain. Transitioning from a lobby to an in-game experience is a **garden-to-garden transition**, not a handoff to another service. The gardener behavior manages the full lifecycle:

```
Player connects
    │
    ▼
Enter discovery garden ──► POIs, drift, scenario selection
    │
    ├── Enter lobby garden ──► Character selection, loadout, party
    │       │
    │       └── Enter in-game garden ──► Combat, exploration, quests
    │               │
    │               ├── Chain to post-game garden ──► Results, growth
    │               │       │
    │               │       └── Return to discovery garden (loop)
    │               │
    │               └── Switch character (L1/R1) ──► Entity bindings shift
    │
    └── Enter housing garden ──► Persistent personal space
```

The current implementation models this as "enter garden → enter scenario (destroys garden)." The target architecture replaces scenario entry/destruction with garden-to-garden transitions where the gardener behavior continuously manages the player's context.

### Per-Garden Entity Associations

Each garden can define its own set of associated entities that the player can interact with:

- **Characters**: Which characters are available to control in this garden (the player may switch between them dynamically -- e.g., L1/R1 on a joystick)
- **Collections**: Which collections are visible/unlockable in this context
- **Inventories**: Which inventories the player can access
- **Currency wallets**: Which wallets are relevant
- **Status effects**: Which buff/debuff contexts apply

The gardener behavior manages these associations in real-time, adding and removing them as the player navigates between gardens and switches contexts within a garden.

### Entity Session Registry Role

When entity-based services (Status, Currency, Inventory, Collection, Seed, etc.) modify an entity, they need to notify connected players who care about that entity via WebSocket. These services operate on `(entityType, entityId)` and don't know WebSocket session IDs. The solution is an **Entity Session Registry** -- a shared Redis-backed mapping from `(entityType, entityId) → Set<sessionId>`.

**Architecture**:

| Component | Role | Layer |
|-----------|------|-------|
| **Connect** | Hosts the Entity Session Registry implementation (Redis infrastructure, register/unregister/query API). Already manages `account → session` mappings via `account-sessions:{accountId}`; the entity registry generalizes this to arbitrary `(entityType, entityId) → Set<sessionId>`. | L1 |
| **Game Session** | Registers `game-service → session` mappings via the registry API | L2 |
| **Gardener** | Primary registrar for gameplay entities: `seed → session`, `character → session`, `collection → session`, `inventory → session`, `currency → session`, `status → session`, `location → session` | L4 |

**Why Connect (L1), not Game Session (L2)**: Entity→session mappings are pure session routing infrastructure -- mapping any entity to WebSocket session IDs. This has no game-service boundary, no realm scoping, and no concept of "game session." Connect already owns session lifecycle (creation, heartbeat, disconnect cleanup, reconnection) and the `account-sessions` index. The entity registry is a natural generalization of what Connect already does. Placing it at L1 means it's available in ALL deployments (app-only, game, full) without requiring a no-op default implementation, and all layers (L1, L2, L3, L4) can both register and query mappings.

**Flow**:

```
1. Gardener behavior assigns character C to player session S
       │
       ▼
2. Gardener calls Entity Session Registry:
   Register(character, C, sessionId=S)
       │
       ▼
3. Later: Status grants a buff to character C
       │
       ▼
4. Status queries Entity Session Registry:
   GetSessionsForEntity(character, C) → {S}
       │
       ▼
5. Status publishes client event via IClientEventPublisher
   to session S → Connect → WebSocket → client
```

**Why not DI Listeners**: The multi-node problem. Gardener and all 13+ entity-based services can't be guaranteed to run on the same node. DI Listeners only fire locally.

**Why not eventing TO Gardener**: Volume. 100K+ NPCs generating entity mutations would flood Gardener with events it doesn't care about (99%+ irrelevant). The registry inverts this: entity-based services do a cheap Redis lookup on their own mutations instead of broadcasting everything to a central router.

**Dynamic registration**: The gardener behavior constantly shifts registrations as the player navigates. When a player switches characters, the gardener behavior may unregister the previous character and register the new one -- or it may keep both registered (to receive events about the previous character for behavioral reasoning). This is a behavior decision, not an infrastructure decision.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Type | Usage |
|------------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 Hard | 7 state stores: garden instances, POIs, templates, scenarios, history, phase config, locks |
| lib-state (`IDistributedLockProvider`) | L0 Hard | Distributed locks for garden/scenario/template/phase mutations |
| lib-state (`ICacheableStateStore`) | L0 Hard | Redis set operations for tracking active garden and scenario account IDs |
| lib-messaging (`IMessageBus`) | L0 Hard | Publishing 15 event types across garden, POI, scenario, template, bond, and phase domains |
| lib-messaging (`IEventConsumer`) | L0 Hard | Registering handlers for `seed.bond.formed`, `seed.activated`, `game-session.deleted` |
| lib-seed (`ISeedClient`) | L2 Hard | Seed type registration, growth queries, growth recording, bond resolution |
| lib-game-session (`IGameSessionClient`) | L2 Hard | Creating/cleaning up game sessions backing scenarios |
| lib-puppetmaster (`IPuppetmasterClient`) | L4 Soft | Optional notification of scenario start with behavior document ID |

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none discovered)* | No other plugins currently reference `IGardenerClient` or subscribe to gardener events |
| lib-director *(planned)* | During directed events, Director calls Gardener APIs to amplify player targeting: boosting POI scoring weights for event-targeted players, spawning event-themed scenario templates with elevated priority, and adjusting garden orchestration to draw players toward event regions. Director also subscribes to `gardener.scenario.completed` for event participation metrics. See [DIRECTOR.md](DIRECTOR.md) Player Targeting Methods |

## State Storage

**Store**: `gardener-garden-instances` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `garden:{accountId}` | `GardenInstanceModel` | Active garden instance per player: position, velocity, drift metrics, active POI IDs, phase, cached growth phase, bond ID |

**Store**: `gardener-pois` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `poi:{gardenInstanceId}:{poiId}` | `PoiModel` | Individual POI state: position, type, scenario template link, trigger mode/radius, TTL, status |

**Store**: `gardener-scenario-templates` (Backend: MySQL, JSON queryable)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `template:{scenarioTemplateId}` | `ScenarioTemplateModel` | Scenario template definitions: code, category, domain weights, prerequisites, chaining, multiplayer, content refs |

**Store**: `gardener-scenario-instances` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `scenario:{accountId}` | `ScenarioInstanceModel` | Active scenario per player: template link, game session ID, participants, chain depth, status |

**Store**: `gardener-scenario-history` (Backend: MySQL, JSON queryable)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `history:{scenarioInstanceId}` | `ScenarioHistoryModel` | Completed/abandoned scenario records: template code, duration, growth awarded, status |

**Store**: `gardener-phase-config` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `phase:config` | `DeploymentPhaseConfigModel` | Singleton deployment phase config: current phase, max concurrent scenarios, feature flags |

**Store**: `gardener-lock` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `garden:{accountId}` | Lock | Distributed lock for garden enter/leave/POI operations |
| `scenario:{accountId}` | Lock | Distributed lock for scenario enter/complete/abandon/chain |
| `bond-scenario:{bondId}` | Lock | Distributed lock for bond scenario entry |
| `template:{templateId}` | Lock | Distributed lock for template update/deprecate |
| `phase` | Lock | Distributed lock for phase config update |

**Tracking Sets** (via `ICacheableStateStore`, stored in `gardener-garden-instances` store):

| Set Key | Element Type | Purpose |
|---------|-------------|---------|
| `gardener:active-gardens` | `Guid` (accountId) | Tracks accounts with active garden instances for background worker iteration |
| `gardener:active-scenarios` | `Guid` (accountId) | Tracks accounts with active scenarios for lifecycle worker iteration |

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `DeploymentPhase` | C (System State) | Service-specific enum | Finite set of deployment phases (Alpha, Beta, Release); system-owned, gates scenario availability |
| `ConnectivityMode` | C (System State) | Service-specific enum | Finite set of world connectivity modes (Isolated, WorldSlice, Persistent); system-owned, determines how scenario instances connect to the broader game world |
| `ScenarioCategory` | C (System State) | Service-specific enum | Finite set of primary gameplay categories (Combat, Crafting, Social, Trade, Exploration, Magic, Survival, Mixed, Narrative, Tutorial); system-owned scenario classification |
| `PoiType` | C (System State) | Service-specific enum | Finite set of sensory presentation types (Visual, Auditory, Environmental, Portal, Social); system-owned, determines POI rendering behavior |
| `TriggerMode` | C (System State) | Service-specific enum | Finite set of POI trigger mechanisms (Proximity, Interaction, Prompted, Forced); system-owned operational modes |
| `PoiStatus` | C (System State) | Service-specific enum | Finite set of POI lifecycle states (Active, Entered, Declined, Expired); system-owned state machine |
| `ScenarioStatus` | C (System State) | Service-specific enum | Finite set of scenario lifecycle states (Initializing, Active, Completing, Completed, Abandoned); system-owned state machine |
| `TemplateStatus` | C (System State) | Service-specific enum | Finite set of template lifecycle states (Draft, Active, Deprecated); system-owned state machine |
| `ScenarioParticipantRole` | C (System State) | Service-specific enum | Finite set of participant roles (Primary, Partner); system-owned |

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `scenario-template.created` | `ScenarioTemplateCreatedEvent` | New scenario template created via `CreateTemplateAsync` |
| `scenario-template.updated` | `ScenarioTemplateUpdatedEvent` | Template updated or deprecated via `UpdateTemplateAsync`/`DeprecateTemplateAsync` |
| `scenario-template.deleted` | `ScenarioTemplateDeletedEvent` | Template deleted (lifecycle event, declared in schema but no delete endpoint currently) |
| `gardener.garden.entered` | `GardenerGardenEnteredEvent` | Player enters the garden via `EnterGardenAsync` |
| `gardener.garden.left` | `GardenerGardenLeftEvent` | Player leaves the garden via `LeaveGardenAsync` |
| `gardener.poi.spawned` | `GardenerPoiSpawnedEvent` | POI spawned by `GardenerGardenOrchestratorWorker` |
| `gardener.poi.entered` | `GardenerPoiEnteredEvent` | Player enters a POI via `InteractWithPoiAsync` or proximity trigger in `UpdatePositionAsync` |
| `gardener.poi.declined` | `GardenerPoiDeclinedEvent` | Player declines a prompted POI via `DeclinePoiAsync` |
| `gardener.poi.expired` | `GardenerPoiExpiredEvent` | POI TTL exceeded, expired by `GardenerGardenOrchestratorWorker` |
| `gardener.scenario.started` | `GardenerScenarioStartedEvent` | Scenario instance created via `EnterScenarioAsync` |
| `gardener.scenario.completed` | `GardenerScenarioCompletedEvent` | Scenario completed via `CompleteScenarioAsync` or timed-out by lifecycle worker |
| `gardener.scenario.abandoned` | `GardenerScenarioAbandonedEvent` | Scenario abandoned via `AbandonScenarioAsync` or detected abandoned by lifecycle worker |
| `gardener.scenario.chained` | `GardenerScenarioChainedEvent` | Scenario chained to next template via `ChainScenarioAsync` |
| `gardener.bond.entered-together` | `GardenerBondEnteredTogetherEvent` | Bonded players enter a scenario together via `EnterScenarioTogetherAsync` |
| `gardener.phase.changed` | `GardenerPhaseChangedEvent` | Deployment phase updated via `UpdatePhaseConfigAsync` |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `seed.bond.formed` | `HandleSeedBondFormedAsync` | Updates `BondId` on active garden instances for bond participants; marks for re-evaluation |
| `seed.activated` | `HandleSeedActivatedAsync` | Updates `SeedId` on active garden instance when account's seed changes; marks for re-evaluation |
| `game-session.deleted` | `HandleGameSessionDeletedAsync` | Logs deletion for observability; actual cleanup handled by `GardenerScenarioLifecycleWorker` on next cycle |

### DI Listener (ISeedEvolutionListener)

| Notification | Handler | Action |
|-------------|---------|--------|
| `OnGrowthRecordedAsync` | `GardenerSeedEvolutionListener` | Marks garden instance `NeedsReEvaluation = true` for next orchestrator tick |
| `OnPhaseChangedAsync` | `GardenerSeedEvolutionListener` | Updates `CachedGrowthPhase` on garden instance; marks for re-evaluation |
| `OnCapabilitiesChangedAsync` | `GardenerSeedEvolutionListener` | No-op (not currently used) |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `GardenTickIntervalMs` | `GARDENER_GARDEN_TICK_INTERVAL_MS` | 5000 | Milliseconds between garden orchestrator evaluation cycles |
| `MaxActivePoisPerGarden` | `GARDENER_MAX_ACTIVE_POIS_PER_GARDEN` | 8 | Maximum concurrent POIs per player garden instance |
| `PoiDefaultTtlMinutes` | `GARDENER_POI_DEFAULT_TTL_MINUTES` | 10 | Default TTL in minutes for spawned POIs |
| `PoiSpawnRadiusMin` | `GARDENER_POI_SPAWN_RADIUS_MIN` | 50.0 | Minimum spawn distance from player |
| `PoiSpawnRadiusMax` | `GARDENER_POI_SPAWN_RADIUS_MAX` | 200.0 | Maximum spawn distance from player |
| `MinPoiSpacing` | `GARDENER_MIN_POI_SPACING` | 30.0 | Minimum distance between any two POIs |
| `PoiDefaultIntensityRamp` | `GARDENER_POI_DEFAULT_INTENSITY_RAMP` | 0.5 | Default initial intensity for spawned POIs |
| `PoiDefaultTriggerRadius` | `GARDENER_POI_DEFAULT_TRIGGER_RADIUS` | 15.0 | Default proximity trigger radius |
| `HesitationDetectionThreshold` | `GARDENER_HESITATION_DETECTION_THRESHOLD` | 0.1 | Minimum movement for hesitation detection |
| `DiversitySeenPenalty` | `GARDENER_DIVERSITY_SEEN_PENALTY` | 0.2 | Diversity score for recently seen templates (0=never reoffered, 1=no penalty) |
| `ExplorationDistanceThreshold` | `GARDENER_EXPLORATION_DISTANCE_THRESHOLD` | 500.0 | Total drift distance to classify player as exploring |
| `HesitantHesitationThreshold` | `GARDENER_HESITANT_HESITATION_THRESHOLD` | 0.6 | Hesitation ratio to classify player as hesitant |
| `ExplorationMaxHesitationRatio` | `GARDENER_EXPLORATION_MAX_HESITATION_RATIO` | 0.3 | Max hesitation ratio for exploring classification |
| `DirectedDistanceThreshold` | `GARDENER_DIRECTED_DISTANCE_THRESHOLD` | 200.0 | Total drift distance to classify player as directed |
| `DirectedMaxHesitationRatio` | `GARDENER_DIRECTED_MAX_HESITATION_RATIO` | 0.15 | Max hesitation ratio for directed classification |
| `ProximityTriggerMaxHesitationRatio` | `GARDENER_PROXIMITY_TRIGGER_MAX_HESITATION_RATIO` | 0.2 | Max hesitation ratio for proximity trigger mode |
| `HesitationRatioNormalizationFactor` | `GARDENER_HESITATION_RATIO_NORMALIZATION_FACTOR` | 10.0 | Divisor for normalizing hesitation count against distance |
| `NarrativeScoreHigh` | `GARDENER_NARRATIVE_SCORE_HIGH` | 0.9 | Strongest category-pattern match score |
| `NarrativeScoreMediumHigh` | `GARDENER_NARRATIVE_SCORE_MEDIUM_HIGH` | 0.7 | Good category-pattern match score |
| `NarrativeScoreMedium` | `GARDENER_NARRATIVE_SCORE_MEDIUM` | 0.6 | Moderate category-pattern match score |
| `NarrativeScoreLow` | `GARDENER_NARRATIVE_SCORE_LOW` | 0.4 | Weak category-pattern match score |
| `NarrativeScoreNeutral` | `GARDENER_NARRATIVE_SCORE_NEUTRAL` | 0.5 | Baseline score when no drift pattern detected |
| `AffinityWeight` | `GARDENER_AFFINITY_WEIGHT` | 0.4 | Weight for domain affinity in scoring algorithm |
| `DiversityWeight` | `GARDENER_DIVERSITY_WEIGHT` | 0.3 | Weight for category diversity in scoring algorithm |
| `NarrativeWeight` | `GARDENER_NARRATIVE_WEIGHT` | 0.2 | Weight for drift-pattern narrative response |
| `RandomWeight` | `GARDENER_RANDOM_WEIGHT` | 0.1 | Weight for randomness/discovery in scoring |
| `RecentScenarioCooldownMinutes` | `GARDENER_RECENT_SCENARIO_COOLDOWN_MINUTES` | 30 | Minutes before completed scenario can be re-offered |
| `MaxConcurrentScenariosGlobal` | `GARDENER_MAX_CONCURRENT_SCENARIOS_GLOBAL` | 1000 | Maximum total active scenario instances |
| `ScenarioTimeoutMinutes` | `GARDENER_SCENARIO_TIMEOUT_MINUTES` | 60 | Maximum scenario duration before forced completion |
| `AbandonDetectionMinutes` | `GARDENER_ABANDON_DETECTION_MINUTES` | 5 | Minutes without input before scenario marked abandoned |
| `GrowthAwardMultiplier` | `GARDENER_GROWTH_AWARD_MULTIPLIER` | 1.0 | Global multiplier for growth awards |
| `GrowthFullCompletionMaxRatio` | `GARDENER_GROWTH_FULL_COMPLETION_MAX_RATIO` | 1.5 | Max time ratio cap for full completion growth |
| `GrowthFullCompletionMinRatio` | `GARDENER_GROWTH_FULL_COMPLETION_MIN_RATIO` | 0.5 | Min time ratio floor for full completion growth |
| `GrowthPartialMaxRatio` | `GARDENER_GROWTH_PARTIAL_MAX_RATIO` | 0.5 | Max time ratio cap for partial growth |
| `DefaultEstimatedDurationMinutes` | `GARDENER_DEFAULT_ESTIMATED_DURATION_MINUTES` | 30 | Fallback estimated duration for templates without one |
| `BondSharedGardenEnabled` | `GARDENER_BOND_SHARED_GARDEN_ENABLED` | true | Whether bonded players share a garden instance |
| `BondScenarioPriority` | `GARDENER_BOND_SCENARIO_PRIORITY` | 1.5 | Scoring boost multiplier for bond-preferred scenarios |
| `DefaultPhase` | `GARDENER_DEFAULT_PHASE` | Alpha | Starting deployment phase for new installations |
| `SeedTypeCode` | `GARDENER_SEED_TYPE_CODE` | guardian | Seed type code this gardener manages |
| `ScenarioLifecycleWorkerIntervalSeconds` | `GARDENER_SCENARIO_LIFECYCLE_WORKER_INTERVAL_SECONDS` | 30 | Seconds between lifecycle worker cycles |
| `BackgroundServiceStartupDelaySeconds` | `GARDENER_BACKGROUND_SERVICE_STARTUP_DELAY_SECONDS` | 5 | Startup delay before background workers begin |
| `DistributedLockTimeoutSeconds` | `GARDENER_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | 30 | Timeout for distributed lock acquisition |
| `PoiPositionMaxRetries` | `GARDENER_POI_POSITION_MAX_RETRIES` | 10 | Max rejection sampling attempts for POI position |
| `PoiVerticalDampeningFactor` | `GARDENER_POI_VERTICAL_DAMPENING_FACTOR` | 0.3 | Y-axis scale factor for POI vertical distribution |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<GardenerService>` | Structured logging |
| `GardenerServiceConfiguration` | Typed configuration access (44 properties) |
| `IStateStoreFactory` | State store access for 7 stores |
| `IMessageBus` | Event publishing (15 event types) |
| `IDistributedLockProvider` | Distributed locks for all mutations |
| `IEventConsumer` | Event subscription registration |
| `ISeedClient` | Seed type registration, growth queries/recording, bond resolution |
| `IGameSessionClient` | Game session creation and cleanup |
| `IServiceProvider` | Soft dependency resolution (`IPuppetmasterClient`) |
| `GardenerGardenOrchestratorWorker` | Background service: POI expiration, scoring, spawning |
| `GardenerScenarioLifecycleWorker` | Background service: timeout/abandon detection |
| `GardenerSeedEvolutionListener` | Singleton `ISeedEvolutionListener` for growth/phase notifications |
| `GardenerGrowthCalculation` | Static helper: shared growth award calculation logic |

## API Endpoints (Implementation Notes)

### Garden Management (4 endpoints)

- **EnterGardenAsync**: Requires active guardian seed (queries via `ISeedClient.GetSeedsByOwnerAsync`). Creates garden instance in Redis, adds to `gardener:active-gardens` tracking set. Returns 409 if already in garden, 404 if no active seed.
- **GetGardenStateAsync**: Read-only lookup of garden instance and active POIs.
- **UpdatePositionAsync**: High-frequency idempotent position update. No distributed lock (intentional -- last-write-wins for acceptable latency). Accumulates drift metrics (total distance, directional bias, hesitation count). Checks proximity triggers on active POIs.
- **LeaveGardenAsync**: Locked cleanup of all POIs, garden instance, and tracking set entry.

### POI Interaction (3 endpoints)

- **ListPoisAsync**: Returns all active POIs for the player's garden instance.
- **InteractWithPoiAsync**: Determines interaction result based on POI trigger mode. Prompted mode returns `ScenarioPrompt` with hardcoded choices `["Enter", "Decline"]` and template display name. Proximity/Interaction/Forced modes return `ScenarioEnter`. Marks POI as Entered.
- **DeclinePoiAsync**: Marks POI as Declined, adds template to garden's `ScenarioHistory` for diversity scoring, triggers re-evaluation.

### Scenario Lifecycle (6 endpoints)

- **EnterScenarioAsync**: Validates template active + allowed in phase + global capacity. Creates backing game session via `IGameSessionClient`. Moves player from garden tracking to scenario tracking. Cleans up garden instance and all POIs. Optionally notifies Puppetmaster (soft dependency).
- **GetScenarioStateAsync**: Read-only scenario lookup.
- **CompleteScenarioAsync**: Calculates full growth via `GardenerGrowthCalculation`, awards to all participants via `ISeedClient.RecordGrowthBatchAsync`, writes history to MySQL, cleans up Redis + game session.
- **AbandonScenarioAsync**: Same as complete but with partial growth calculation and Abandoned status.
- **ChainScenarioAsync**: Validates chaining rules (template `LeadsTo` list and `MaxChainDepth`). Completes current scenario with growth, creates new chained scenario reusing the same game session. Increments chain depth.
- **EnterScenarioTogetherAsync** (Bond): Resolves bond participants via `ISeedClient.GetBondAsync` + `GetSeedAsync`. Validates both in garden, template supports multiplayer. Creates shared game session and scenario for all participants.

### Template Management (7 endpoints)

Standard CRUD on scenario templates with JSON-queryable MySQL storage. Code uniqueness enforced via JSON query on creation. `ListTemplatesAsync` supports filtering by category, connectivity mode, status, and deployment phase. Deployment phase filtering is done in-memory post-query because JSON array contains is not supported in queries. `DeprecateTemplateAsync` sets status to Deprecated and publishes update event. `DeleteTemplateAsync` requires Deprecated status, permanently removes template from store, and publishes `scenario-template.deleted` lifecycle event.

### Phase Management (3 endpoints)

- **GetPhaseConfigAsync**: Returns phase config singleton, auto-creates with defaults if missing.
- **UpdatePhaseConfigAsync**: Locked update of phase config. Publishes `gardener.phase.changed` event only if phase actually changed.
- **GetPhaseMetricsAsync**: Returns active garden/scenario counts from tracking sets and capacity utilization ratio.

### Bond Features (2 endpoints)

- **EnterScenarioTogetherAsync**: Documented under Scenario Lifecycle above.
- **GetSharedGardenStateAsync**: Returns merged view of all bond participants' garden states (positions and POIs).

## Scenario Selection Algorithm

The `GardenerGardenOrchestratorWorker` spawns POIs using a weighted scoring formula:

```
TotalScore = AffinityWeight * affinity + DiversityWeight * diversity
           + NarrativeWeight * narrative + RandomWeight * random
```

If the player has a bond and the template is bond-preferred, score is multiplied by `BondScenarioPriority`.

**Affinity Score**: Measures how well template domain weights match the player's seed growth profile. For each template domain weight, multiplies it by the proportion of player growth in that domain relative to total growth.

**Diversity Score**: 1.0 for unseen templates, `DiversitySeenPenalty` (default 0.2) for templates in the garden's `ScenarioHistory`.

**Narrative Score**: Classifies player drift pattern (exploring, hesitant, directed) using configurable thresholds, then maps to scenario categories with four score tiers (high, medium-high, medium, low).

**Random Score**: `Random.Shared.NextSingle()` for discovery.

## Visual Aid

### Target Architecture: Garden Lifecycle & Entity Session Registry

```
Player connects → Gardener triggers divine actor for garden-tending
    │
    ▼
┌────────────────────────────────────────────────────────────────────┐
│  DIVINE ACTOR as GARDENER (running via L2 Actor Runtime)           │
│                                                                    │
│  Manages garden-to-garden transitions for this player:             │
│                                                                    │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐           │
│  │  Discovery   │───►│   Lobby      │───►│  In-Game     │          │
│  │  Garden      │    │   Garden     │    │  Garden      │          │
│  │             │    │             │    │             │            │
│  │ POIs, drift, │    │ Char select,│    │ Combat,     │           │
│  │ scoring      │    │ loadout     │    │ exploration │           │
│  └─────────────┘    └─────────────┘    └──────┬──────┘           │
│                                                │                  │
│  Entity Session Registry updates per garden:   │ Player switches  │
│  ┌─────────────────────────────────────────┐   │ character (L1/R1)│
│  │ Register(seed, seedId, sessionId)       │   │      │          │
│  │ Register(character, charA, sessionId)   │◄──┘      │          │
│  │ Register(inventory, invId, sessionId)   │          ▼          │
│  │ Register(collection, colId, sessionId)  │  Unregister(charA)  │
│  │ Register(location, locId, sessionId)    │  Register(charB)    │
│  │ ...                                     │  Update(location)   │
│  └─────────────────────────────────────────┘                     │
└────────────────────────────────────────────────────────────────────┘
         │
         │  Entity-based services use the registry directly:
         ▼
┌────────────────────────────────────────────────────────────────────┐
│  Status grants buff to character B                                 │
│      │                                                             │
│      ├── StatusService: GetSessionsForEntity(character, B) → {S}  │
│      │   (Redis SMEMBERS, sub-millisecond)                         │
│      │                                                             │
│      └── StatusService: PublishToSessionsAsync({S}, event)         │
│          → IClientEventPublisher → RabbitMQ → Connect → WebSocket  │
└────────────────────────────────────────────────────────────────────┘
```

### Current Implementation: Void/Discovery Garden Only

```
                        ┌──────────────────────────────┐
                        │     EnterGardenAsync           │
                        │  (Creates GardenInstance)    │
                        └──────────┬───────────────────┘
                                   │
                                   ▼
    ┌───────────────────────────────────────────────────────┐
    │              GARDEN (Active Instance)                   │
    │                                                        │
    │  garden:{accountId} ──── ActivePoiIds ────┐            │
    │  [Redis]               [List<Guid>]     │              │
    │                                         ▼              │
    │  ┌───────────────────────────────────────────┐         │
    │  │ GardenerGardenOrchestratorWorker (tick)     │         │
    │  │  1. Expire stale POIs                     │         │
    │  │  2. Score eligible templates               │         │
    │  │  3. Spawn POIs at valid positions          │         │
    │  │     poi:{gardenId}:{poiId} [Redis]         │         │
    │  └───────────────────────────────────────────┘         │
    │                                                        │
    │  Player ──UpdatePosition──► drift metrics              │
    │  Player ──InteractWithPOI──► scenario prompt           │
    │  Player ──DeclinePOI──► diversity history               │
    └──────────────────┬────────────────────────────────────┘
                       │ EnterScenarioAsync
                       │ (deletes garden + POIs,
                       │  creates game session)
                       ▼
    ┌───────────────────────────────────────────────────────┐
    │              SCENARIO (Active Instance)                │
    │                                                        │
    │  scenario:{accountId} ──── GameSessionId               │
    │  [Redis]                   [via IGameSessionClient]    │
    │                                                        │
    │  ┌───────────────────────────────────────────┐         │
    │  │ GardenerScenarioLifecycleWorker (cycle)    │         │
    │  │  Detects timeout / abandonment             │         │
    │  │  Awards partial growth, writes history     │         │
    │  └───────────────────────────────────────────┘         │
    │                                                        │
    │  CompleteScenario ──► full growth ──► history [MySQL]   │
    │  AbandonScenario ──► partial growth ──► history [MySQL] │
    │  ChainScenario ──► complete + new scenario (reuse GS)  │
    └───────────────────────────────────────────────────────┘
```

## Stubs & Unimplemented Features

### Architectural Gaps (Garden Concept)

11. **Divine actor as gardener pattern**: Garden-tending does not yet run as an actor. The current implementation uses background workers (`GardenerGardenOrchestratorWorker`, `GardenerScenarioLifecycleWorker`) with fixed-interval ticks instead of a per-player divine actor executing an ABML gardener behavior document. The target architecture uses divine actors (the same actor type that Puppetmaster launches as regional watchers) to tend conceptual garden spaces, unifying the puppetmaster and gardener roles under a single divine actor identity. Dynamic character binding simplifies this transition: divine gardener actors can start immediately as event brains (tending with default rules while divine character profiles are provisioned), then bind to their divine characters at runtime for personality-driven orchestration. The ABML gardener behavior document supports both modes from the start. See [DIVINE.md](DIVINE.md) for the full architectural rationale and dynamic binding lifecycle.

12. **Garden-to-garden transitions**: The current implementation destroys the garden instance on scenario entry (`EnterScenarioAsync` deletes garden + POIs). The target architecture replaces this with garden-to-garden transitions where the gardener behavior continuously manages the player's context across garden types (discovery → lobby → in-game → post-game → discovery). The player is always in some garden; Gardener is always active.

13. **Multiple garden types**: Only the void/discovery garden type is implemented (POIs, drift metrics, weighted scoring). Lobby gardens, in-game gardens, housing gardens, post-game gardens, and cooperative gardens are not implemented. Each type requires its own behavioral patterns and entity association rules. The housing garden type is a key validation case for the multi-garden architecture -- it composes Scene (layout), Seed (progression), Save-Load (persistence), Voxel Builder SDK (interactive construction), and Item/Inventory (furnishings) entirely through gardener behaviors with no dedicated plugin. See [Housing Garden Pattern](#housing-garden-pattern-no-plugin-required) above and [VOXEL-BUILDER-SDK.md](../planning/VOXEL-BUILDER-SDK.md).

14. **Per-garden entity associations**: Gardens do not currently track associated entities (characters, collections, inventories, wallets). The target architecture gives each garden its own set of entities that the player can interact with, managed dynamically by the gardener behavior. For example, a lobby garden offers character selection from a roster; an in-game garden binds the selected character and its inventory/wallet.

15. **Entity Session Registry integration**: Gardener does not currently register entity→session mappings. The target architecture has Gardener as the primary registrar for gameplay entities (seed→session, character→session, collection→session, inventory→session, currency→session, status→session, location→session) in the Entity Session Registry hosted by Connect (L1). This enables entity-based services to route client events to relevant WebSocket sessions without a central event router. The `location → session` binding ([#499](https://github.com/beyond-immersion/bannou-service/issues/499)) is updated atomically when a character moves between locations (unregister old, register new). See [The Garden Concept > Entity Session Registry Role](#entity-session-registry-role) for the full architecture, and [#502](https://github.com/beyond-immersion/bannou-service/issues/502) for the full L1/L2 client event rollout plan.

16. **Dynamic character switching**: No mechanism exists for players to switch between characters within a garden (e.g., L1/R1 on a joystick). The target architecture has the gardener behavior managing character bindings in real-time, updating entity session registrations as the player switches context.

### Implementation Gaps (Current Void/Discovery Garden)

1. **MinGrowthPhase filtering**: The `GetEligibleTemplatesAsync` method in `GardenerGardenOrchestratorWorker` has a MinGrowthPhase check block (lines 457-465) with an empty body -- the comment says "For now, include all templates where the player has any growth phase." Growth phases are opaque strings without ordinal comparison, so phase ordering would need a lookup table.

2. **scenario-template.deleted event**: Declared in the events schema via `x-lifecycle` but no delete endpoint exists in the API. The lifecycle event is generated but never published.

3. **Puppetmaster notification**: `EnterScenarioAsync` resolves `IPuppetmasterClient` and logs intent to notify about behavior documents, but does not actually call any Puppetmaster API. The notification is a log-only stub.

4. **PersistentEntryEnabled / GardenMinigamesEnabled**: These phase config flags are stored and returned but never read by any service logic. They appear to be forward-looking feature flags.

5. **Matchmaking integration**: The implementation plan specified `IMatchmakingClient` (L4 soft dependency) for submitting matchmaking tickets when templates have `MinPlayers > 1`. This was never implemented -- group scenarios beyond bond pairs have no queueing mechanism.

6. **Analytics integration**: The implementation plan specified `IAnalyticsClient` (L4 soft dependency) for querying milestone data to enrich scenario scoring. This was never implemented -- the `NarrativeWeight` scoring component partially compensates but milestone-based scoring is absent.

7. **Client event schema (gardener-client-events.yaml)**: No client event schema exists for real-time POI push to WebSocket clients. POI spawns, expirations, and trigger events happen server-side only. Clients must poll `GetGardenStateAsync` to discover changes, which defeats the purpose of the garden as a responsive discovery space. This is a significant gap for the intended player experience.

8. **ConnectivityMode.Persistent has no special handling**: The enum value exists (Alpha, Beta, Release map to Isolated, WorldSlice, Persistent) but the code treats all connectivity modes identically during scenario creation and lifecycle. The vision describes Persistent as "a scenario that doesn't end" -- the release surprise. No code path distinguishes Persistent scenarios from Isolated ones.

9. **Prerequisite validation during scenario entry**: Templates store `Prerequisites` (required domains, required/excluded scenarios) in the model but they are never validated in `EnterScenarioAsync`. A player can enter any scenario regardless of prerequisites. The `GardenerGardenOrchestratorWorker.GetEligibleTemplatesAsync` also does not filter by prerequisites.

10. **Per-template MaxConcurrentInstances enforcement**: Templates store a `MaxConcurrentInstances` value but it is never checked during scenario entry. Only the global `MaxConcurrentScenariosGlobal` is enforced.

## Potential Extensions

1. **Scenario history cleanup**: History records in MySQL accumulate indefinitely. No retention policy or cleanup mechanism exists. A background worker or configurable retention window (similar to transaction history in lib-currency) would prevent unbounded growth.

2. **Multi-participant history**: `WriteScenarioHistoryAsync` only writes a history record for the primary participant. Secondary participants in bond scenarios do not get individual history records. This means cooldown checking and scenario diversity scoring are inaccurate for non-primary bond participants.

3. **Template delete endpoint**: A delete API endpoint could allow removing deprecated templates and publishing the already-declared `scenario-template.deleted` lifecycle event.

4. **Content flywheel connection**: Scenarios complete and award growth, but there's no mechanism to feed scenario outcomes back into future content generation. No events are consumed by Storyline, no archive data is generated from scenario history. Per VISION.md, this is a load-bearing connection: "more play produces more content, which produces more play." Gardener generates play but doesn't yet contribute to the content side of the flywheel.

5. **Dialog choice trigger mode**: PLAYER-VISION.md describes four acceptance modes: implicit (proximity), prompted (acknowledge), dialog choices (multiple branching options), and forced. The implementation has Proximity, Interaction, Prompted, and Forced -- but "dialog choices" (presenting multiple distinct branching options beyond Enter/Decline) is missing. Prompted mode only offers binary accept/decline.

6. **Garden position binary protocol optimization**: `UpdatePositionAsync` uses POST JSON for high-frequency position updates. PLAYER-VISION.md describes the garden as "negligible bandwidth" but JSON POST per tick adds overhead. Binary protocol through Connect would reduce this but was deferred per the plan ("optimize only if latency/throughput becomes a problem"). In the full garden concept, high-frequency position updates may not apply to all garden types (lobby gardens don't have spatial drift).

7. **Multi-seed-type support**: `SeedTypeCode` is a single config property (default "guardian"). PLAYER-VISION.md describes multiple seed types (guardian spirit, dungeon master, realm-specific). Each type would need its own Gardener instance or the service would need to manage multiple types. The current architecture supports this (deploy multiple Gardener instances with different `GARDENER_SEED_TYPE_CODE`), but this deployment pattern is undocumented.

8. **Analytics milestone integration for scoring**: The plan specified consuming `analytics.milestone.reached` events to enrich scenario selection. When Analytics publishes this event, a subscription could be added to boost templates that align with player milestone achievements.

9. **Gardener-to-Gardener communication for co-op gardens**: When multiple players share a cooperative garden, their gardener behaviors need to coordinate. This could use the existing pair bond mechanism for bonded players, but ad-hoc multiplayer gardens (matchmade groups, guild activities) need a coordination pattern. Possible approaches: shared garden state in Redis, inter-actor messaging via the Actor runtime, or a dedicated co-op coordination layer.

10. **Location → session registration** ([#499](https://github.com/beyond-immersion/bannou-service/issues/499)): Add `("location", locationId)` entity session registration when a player's character enters a location, enabling Location (L2) to push entity presence change client events to all players at a location. Registration updated atomically (unregister old location, register new) when the character moves between locations. Part of the broader L1/L2 client event rollout ([#502](https://github.com/beyond-immersion/bannou-service/issues/502)).

11. **Director-driven player targeting APIs**: Director needs to externally influence Gardener's scenario selection and POI spawning for directed events. Options: (A) Director calls Gardener APIs directly (spawn POI, create temporary template, boost scoring weights per-player) -- simpler but tighter coupling; (B) Director publishes targeting intents and Gardener has a consumer that implements them -- cleaner but requires Gardener to understand Director targeting concepts. Either approach requires new endpoints or event handlers beyond what Gardener currently exposes. The `GardenerAmplificationMultiplier` configuration in Director provides the scoring weight boost factor. See [DIRECTOR.md](DIRECTOR.md).
<!-- AUDIT:NEEDS_DESIGN:2026-02-26:https://github.com/beyond-immersion/bannou-service/issues/499 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Guid.Empty sentinel for WebSocketSessionId**~~: Resolved -- made `webSocketSessionId` nullable in game-session API schema; Gardener now passes `null`.

2. ~~**Guid.Empty for GameServiceId in seed type registration**~~: Resolved -- made `GameServiceId` nullable in seed API schema; Seed service handles null→storage conversion internally via `CrossGameServiceId` constant.

3. ~~**ListTemplatesAsync in-memory phase filtering breaks pagination**~~: Resolved -- `AllowedPhases` now always stores explicit phase values (null/empty at API boundary normalized to all phases). Eliminated empty-array sentinel. Phase filtering uses `JSON_CONTAINS` via `QueryOperator.In` at the database level with correct server-side pagination.

4. ~~**Missing try-catch around CreateGameSessionAsync**~~: Resolved -- both call sites wrapped with `ApiException` catch, error event publication, and `ServiceUnavailable` return. No compensation needed because game session creation precedes scenario state.

5. ~~**EnterGardenAsync has no distributed lock**~~: Resolved -- added distributed lock on `garden:{accountId}` key; returns `Conflict` if lock cannot be acquired.

6. ~~**Hardcoded query page sizes in GardenOrchestratorWorker**~~: Resolved -- extracted to `ScenarioTemplateQueryPageSize` (default: 500) and `ScenarioHistoryQueryPageSize` (default: 200) configuration properties.

7. ~~**Race condition in background worker POI expiry**~~: Resolved -- moved to Intentional Quirks #8 as accepted last-write-wins behavior.

### Intentional Quirks (Documented Behavior)

1. **UpdatePositionAsync has no distributed lock**: Intentionally omitted for latency. High-frequency position updates are idempotent (last-write-wins), and stale reads only affect drift metric precision. Documented in code via `<remarks>` block.

2. **Hardcoded prompt choices**: `InteractWithPoiAsync` returns `["Enter", "Decline"]` as hardcoded prompt choices for `TriggerMode.Prompted` POIs. These are presentation hints for the client, not authoritative -- the client renders them but the server doesn't validate responses against them.

3. **Hardcoded game type string**: `"gardener-scenario"` is used as the game type string for game session creation. This is a convention-based identifier used only by Gardener and is filtered in the `HandleGameSessionDeletedAsync` event handler.

4. **HandleGameSessionDeletedAsync is log-only**: The event handler for `game-session.deleted` only logs the event. Actual cleanup relies on the `GardenerScenarioLifecycleWorker` detecting the orphaned scenario via `LastActivityAt` timeout. This is intentional because there is no reverse-lookup from GameSessionId to account ID in the scenario store (scenarios are keyed by `scenario:{accountId}`).

5. **ISeedEvolutionListener is a separate singleton class**: `GardenerSeedEvolutionListener` is registered as a Singleton rather than being part of `GardenerService` (which is Scoped). This is required because `ISeedEvolutionListener` must be resolvable from BackgroundService (Singleton) context. Follows the same pattern as `SeedCollectionUnlockListener`.

6. **Tracking sets use GardenCacheStore from garden-instances**: The `ActiveGardensTrackingKey` and `ActiveScenariosTrackingKey` Redis sets use the `ICacheableStateStore<GardenInstanceModel>` backed by the `gardener-garden-instances` store. This works because Redis set operations are independent of the store's value type. The tracking sets are separate data structures within the same Redis namespace.

7. **Chained scenarios reuse the game session**: When `ChainScenarioAsync` transitions to a new scenario, it reuses the previous scenario's `GameSessionId` rather than creating a new game session. This keeps the player in the same session context across the chain.

8. **Background worker POI expiry uses last-write-wins**: `GardenerGardenOrchestratorWorker.ExpireStalePoiAsync` modifies POI state (setting `Status = Expired`) and removes POI IDs from the garden's `ActivePoiIds` list without acquiring a distributed lock. If a player interacts with a POI via `InteractWithPoiAsync` (which does acquire a lock) at the same moment the worker expires it, the worker's unlocked write could overwrite the interaction result. This is accepted because: (a) the window is extremely narrow (worker runs every 5s, POI interaction is sub-millisecond), (b) the consequence is benign (player enters a scenario that was about to expire, or an expired POI briefly appears interactable), and (c) adding per-garden locks to the worker would significantly reduce throughput when processing many gardens per tick. The same last-write-wins semantics apply to `UpdatePositionAsync` (Quirk #1) for the same throughput reasons.

### Design Considerations (Requires Planning)

#### Architectural (Garden Concept)

7. **Entity Session Registry design** ([Issue #426](https://github.com/beyond-immersion/bannou-service/issues/426)): The Entity Session Registry needs to be designed and implemented in Connect (L1), generalizing Connect's existing `account-sessions:{accountId}` pattern to arbitrary entity types. Key decisions: interface shape (`IEntitySessionRegistry` in `bannou-service/`), Redis key structure (`entity-sessions:{entityType}:{entityId}` → `Set<sessionId>`), cleanup on session disconnect (`UnregisterSessionAsync` sweeping all bindings via session-to-entities reverse index), and TTL/expiry for stale entries. Connect already manages session lifecycle, heartbeat liveness, and disconnect cleanup -- the entity registry plugs into the same infrastructure. Entity-based services (Status, Currency, Inventory, Collection, Seed, etc.) then query the registry and publish their own client events via `IClientEventPublisher`. This is a cross-cutting infrastructure addition that affects many services.

8. **Divine gardener behavior document design**: The divine actor tending a garden uses an ABML behavior document (or family of documents) that encodes the per-game orchestration logic -- the same ABML bytecode as any other divine actor behavior (realm-tending, encounter orchestration). Key questions: Is there a default/base gardener behavior? How does a game author customize it? How does the divine actor interact with Gardener's APIs from ABML (the behavior needs to call garden/POI/scenario endpoints -- likely via custom ABML action handlers analogous to Puppetmaster's `spawn_watcher:`, `watch:`, `load_snapshot:`)? Does a single divine actor handle both realm-tending and garden-tending flows, or are they separate actors for the same deity?

9. **Garden type abstraction**: The current code assumes one garden type (void/discovery). Multiple garden types need a type registry, per-type behavioral patterns, and per-type entity association rules. Key question: Are garden types defined in configuration (like seed types), in schemas, or purely in behavior documents?

10. **Garden-to-garden transition mechanics**: Replacing the current "destroy garden on scenario entry" with smooth transitions. What state persists across transitions? Does the divine gardener actor persist or restart? How does the game session relationship change (is each garden backed by a game session, or only in-game gardens)?

#### Implementation (Current Void/Discovery Garden)

1. **Growth phase ordering is unresolved**: Templates can specify `MinGrowthPhase` but there is no mechanism to compare phase labels ordinally. The growth phases ("nascent", "awakening", "attuned", "resonant", "transcendent") are registered with `MinTotalGrowth` thresholds in the Seed service, but Gardener only caches the phase label string. Implementing this requires either querying Seed for phase ordering or maintaining a local phase-to-ordinal mapping.

2. ~~**DeploymentPhase filter needs MySQL JSON array query**~~: Resolved -- eliminated the empty-array sentinel by normalizing null/empty `AllowedPhases` to all phases at the API boundary. Storage always contains explicit values, enabling `QueryOperator.In` (which generates `JSON_CONTAINS`) for server-side filtering.

3. **Persistent connectivity mode and the release transition**: PLAYER-VISION.md describes the release as "a scenario that doesn't end, a door that leads to permanent inhabitation." `ConnectivityMode.Persistent` exists as an enum value but has no differentiated code path. Designing this requires answering: What makes a Persistent scenario different from Isolated? Does the player stay in-world indefinitely? Does the garden become a between-sessions lobby? How does the "surprise" transition work without disrupting players mid-scenario? In the full garden concept, this is subsumed by garden-to-garden transitions -- the "Persistent" mode is simply a garden that doesn't transition back to discovery.

4. **Content flywheel integration point**: Gardener generates play (scenarios, growth, history) but doesn't contribute to the content side of the flywheel. The architectural question is where the connection point should be: Should scenario completion publish events that Storyline/Puppetmaster consume? Should scenario history records feed into compressed archives via lib-resource? This is a cross-cutting design question that affects multiple services.

5. **Background worker and API lock coordination**: The `GardenerGardenOrchestratorWorker` processes gardens without distributed locks (for throughput), while API endpoints acquire locks for the same garden instances. Resolution: last-write-wins is accepted for the worker (see Intentional Quirk #8). The worker's unlocked operations (POI expiry, spawning) have benign race consequences, and per-garden locks would throttle throughput at scale. API endpoints continue to use locks for user-initiated mutations where correctness matters (scenario entry, garden creation).

6. **Plan document superseded**: The original implementation plan (`docs/plans/GARDENER.md`) was superseded by this deep dive and has been deleted. The plan had several inaccuracies: stated 25 endpoints (actual: 23), specified event-based growth awards (actual: direct API via `ISeedClient.RecordGrowthBatchAsync`), listed 5 broadcast event subscriptions (actual: 3 events + DI listener), and named `game-session.ended` (actual: `game-session.deleted`). This deep dive is the authoritative reference.

## Work Tracking

### P0 -- Fix Immediately

- [x] **Bug #1**: Make `webSocketSessionId` nullable in game-session API schema, update Gardener to pass `null` instead of `Guid.Empty`
- [x] **Bug #2**: Make `GameServiceId` nullable in seed API schema for cross-game seed types, update Gardener plugin registration
- [x] **Bug #3**: Eliminated empty-array sentinel for `AllowedPhases`; null/empty normalized to all phases at API boundary; `ListTemplatesAsync` now uses `JSON_CONTAINS` query with correct server-side pagination
- [x] **Bug #4**: Add try-catch + compensation around `CreateGameSessionAsync` in `EnterScenarioAsync` and `EnterScenarioTogetherAsync`
- [x] **Bug #5**: Add distributed lock to `EnterGardenAsync` (low-frequency operation, lock is appropriate)
- [x] **Bug #6**: Extract hardcoded query page sizes (500, 200) in `GardenerGardenOrchestratorWorker` to configuration properties
- [x] **Bug #7**: POI expiry race condition documented as accepted last-write-wins behavior (Intentional Quirk #8)

### P1 -- Complete Before Beta

- [ ] **Stub #9**: Implement prerequisite validation in `EnterScenarioAsync` and `GetEligibleTemplatesAsync`
- [ ] **Stub #10**: Implement per-template `MaxConcurrentInstances` enforcement during scenario entry
- [ ] **Stub #1/Design #1**: Implement `MinGrowthPhase` ordinal comparison (requires Seed phase ordering query or local mapping)
- [ ] **Stub #7**: Create `gardener-client-events.yaml` schema for real-time POI spawn/expire/trigger push to WebSocket clients
- [ ] **Extension #2**: Write history records for all bond scenario participants, not just primary
- [ ] **Stub #3**: Implement actual Puppetmaster API call in `EnterScenarioAsync` (not just log)
- [ ] **Design #7/Stub #15**: Design and implement Entity Session Registry in Connect (L1) -- `IEntitySessionRegistry` interface in `bannou-service/`, Redis-backed implementation in Connect (generalizing existing `account-sessions` pattern), cleanup on disconnect via reverse index. Cross-cutting: affects all entity-based services that want client event routing. See [Issue #426](https://github.com/beyond-immersion/bannou-service/issues/426).

### P2 -- Feature Gaps

- [ ] **Stub #6**: Add Analytics milestone integration for scenario scoring (`IAnalyticsClient` soft dependency)
- [ ] **Stub #5**: Add Matchmaking integration for group scenarios (`IMatchmakingClient` soft dependency)
- [ ] **Extension #1**: Implement scenario history retention policy / cleanup worker
- [ ] **Stub #8**: Design and implement `ConnectivityMode.Persistent` differentiation for release transition
- [ ] **Extension #4**: Design content flywheel connection (scenario outcomes feeding Storyline/Puppetmaster)
- [ ] **Extension #5**: Add dialog choice trigger mode for multi-option branching POIs
- [ ] **Stub #4**: Wire up `PersistentEntryEnabled` and `GardenMinigamesEnabled` phase config flags to service logic
- [ ] **Stub #11**: Divine actor as gardener pattern -- launch per-player divine actor (via Puppetmaster) with gardener behavior document instead of background workers. Dynamic character binding enables progressive rollout: start event brain actors immediately, bind divine characters later for personality-driven orchestration.
- [ ] **Stub #12**: Garden-to-garden transitions replacing destroy-on-scenario-entry
- [ ] **Stub #13**: Multiple garden types (lobby, in-game, housing, post-game, cooperative). Housing garden is the first validation target -- see [Housing Garden Pattern](#housing-garden-pattern-no-plugin-required) and [VOXEL-BUILDER-SDK.md](../planning/VOXEL-BUILDER-SDK.md)
- [ ] **Stub #14**: Per-garden entity associations (characters, collections, inventories, wallets)
- [ ] **Stub #16**: Dynamic character switching within a garden (entity binding updates on switch)

### P3 -- Documentation & Cleanup

- [x] **Design #6**: Implementation plan (`docs/plans/GARDENER.md`) superseded by this deep dive and deleted
- [x] **Stub #2**: Added `/gardener/template/delete` endpoint (developer-only) that requires Deprecated status, publishes `scenario-template.deleted` lifecycle event
- [ ] **Extension #7**: Document multi-seed-type deployment pattern (multiple Gardener instances with different `GARDENER_SEED_TYPE_CODE`)
- [ ] **Design #8**: Design divine gardener behavior document structure (base/default behavior, per-game customization, ABML action handlers for Gardener APIs)
- [ ] **Design #9**: Design garden type abstraction (registry, behavioral patterns, entity association rules)
- [ ] **Design #10**: Design garden-to-garden transition mechanics (state persistence, actor lifecycle, game session relationship)
- [ ] **Design #11**: Design housing garden ABML behaviors -- item placement (checkout→move→commit), voxel editing permissions (gated by seed capabilities), visitor management (permission role matrix), and item↔scene node binding convention. See [Issue #432](https://github.com/beyond-immersion/bannou-service/issues/432) and [VOXEL-BUILDER-SDK.md](../planning/VOXEL-BUILDER-SDK.md)
