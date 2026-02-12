# Gardener Plugin Deep Dive

> **Plugin**: lib-gardener
> **Schema**: schemas/gardener-api.yaml
> **Version**: 1.0.0
> **State Stores**: gardener-void-instances (Redis), gardener-pois (Redis), gardener-scenario-templates (MySQL), gardener-scenario-instances (Redis), gardener-scenario-history (MySQL), gardener-phase-config (MySQL), gardener-lock (Redis)

## Overview

Player experience orchestration service (L4 GameFeatures) for void navigation, scenario routing, progressive discovery, and deployment phase management. Gardener is the player-side counterpart to Puppetmaster: where Puppetmaster orchestrates what NPCs experience, Gardener orchestrates what players experience. Players enter a procedural "Void" discovery space, encounter POIs (Points of Interest) driven by a weighted scoring algorithm, and enter scenarios backed by Game Sessions that award Seed growth on completion. Internal-only, never internet-facing.

## Dependencies (What This Plugin Relies On)

| Dependency | Type | Usage |
|------------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 Hard | 7 state stores: void instances, POIs, templates, scenarios, history, phase config, locks |
| lib-state (`IDistributedLockProvider`) | L0 Hard | Distributed locks for void/scenario/template/phase mutations |
| lib-state (`ICacheableStateStore`) | L0 Hard | Redis set operations for tracking active void and scenario account IDs |
| lib-messaging (`IMessageBus`) | L0 Hard | Publishing 15 event types across void, POI, scenario, template, bond, and phase domains |
| lib-messaging (`IEventConsumer`) | L0 Hard | Registering handlers for `seed.bond.formed`, `seed.activated`, `game-session.deleted` |
| lib-seed (`ISeedClient`) | L2 Hard | Seed type registration, growth queries, growth recording, bond resolution |
| lib-game-session (`IGameSessionClient`) | L2 Hard | Creating/cleaning up game sessions backing scenarios |
| lib-puppetmaster (`IPuppetmasterClient`) | L4 Soft | Optional notification of scenario start with behavior document ID |

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none discovered)* | No other plugins currently reference `IGardenerClient` or subscribe to gardener events |

## State Storage

**Store**: `gardener-void-instances` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `void:{accountId}` | `GardenInstanceModel` | Active void instance per player: position, velocity, drift metrics, active POI IDs, phase, cached growth phase, bond ID |

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
| `void:{accountId}` | Lock | Distributed lock for void enter/leave/POI operations |
| `scenario:{accountId}` | Lock | Distributed lock for scenario enter/complete/abandon/chain |
| `bond-scenario:{bondId}` | Lock | Distributed lock for bond scenario entry |
| `template:{templateId}` | Lock | Distributed lock for template update/deprecate |
| `phase` | Lock | Distributed lock for phase config update |

**Tracking Sets** (via `ICacheableStateStore`, stored in `gardener-void-instances` store):

| Set Key | Element Type | Purpose |
|---------|-------------|---------|
| `gardener:active-voids` | `Guid` (accountId) | Tracks accounts with active void instances for background worker iteration |
| `gardener:active-scenarios` | `Guid` (accountId) | Tracks accounts with active scenarios for lifecycle worker iteration |

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `scenario-template.created` | `ScenarioTemplateCreatedEvent` | New scenario template created via `CreateTemplateAsync` |
| `scenario-template.updated` | `ScenarioTemplateUpdatedEvent` | Template updated or deprecated via `UpdateTemplateAsync`/`DeprecateTemplateAsync` |
| `scenario-template.deleted` | `ScenarioTemplateDeletedEvent` | Template deleted (lifecycle event, declared in schema but no delete endpoint currently) |
| `gardener.void.entered` | `GardenerVoidEnteredEvent` | Player enters the void via `EnterVoidAsync` |
| `gardener.void.left` | `GardenerVoidLeftEvent` | Player leaves the void via `LeaveVoidAsync` |
| `gardener.poi.spawned` | `GardenerPoiSpawnedEvent` | POI spawned by `GardenerVoidOrchestratorWorker` |
| `gardener.poi.entered` | `GardenerPoiEnteredEvent` | Player enters a POI via `InteractWithPoiAsync` or proximity trigger in `UpdatePositionAsync` |
| `gardener.poi.declined` | `GardenerPoiDeclinedEvent` | Player declines a prompted POI via `DeclinePoiAsync` |
| `gardener.poi.expired` | `GardenerPoiExpiredEvent` | POI TTL exceeded, expired by `GardenerVoidOrchestratorWorker` |
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
| `VoidTickIntervalMs` | `GARDENER_VOID_TICK_INTERVAL_MS` | 5000 | Milliseconds between void orchestrator evaluation cycles |
| `MaxActivePoisPerVoid` | `GARDENER_MAX_ACTIVE_POIS_PER_VOID` | 8 | Maximum concurrent POIs per player void instance |
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
| `BondSharedVoidEnabled` | `GARDENER_BOND_SHARED_VOID_ENABLED` | true | Whether bonded players share a void instance |
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
| `GardenerVoidOrchestratorWorker` | Background service: POI expiration, scoring, spawning |
| `GardenerScenarioLifecycleWorker` | Background service: timeout/abandon detection |
| `GardenerSeedEvolutionListener` | Singleton `ISeedEvolutionListener` for growth/phase notifications |
| `GardenerGrowthCalculation` | Static helper: shared growth award calculation logic |

## API Endpoints (Implementation Notes)

### Void Management (4 endpoints)

- **EnterVoidAsync**: Requires active guardian seed (queries via `ISeedClient.GetSeedsByOwnerAsync`). Creates garden instance in Redis, adds to `gardener:active-voids` tracking set. Returns 409 if already in void, 404 if no active seed.
- **GetVoidStateAsync**: Read-only lookup of garden instance and active POIs.
- **UpdatePositionAsync**: High-frequency idempotent position update. No distributed lock (intentional -- last-write-wins for acceptable latency). Accumulates drift metrics (total distance, directional bias, hesitation count). Checks proximity triggers on active POIs.
- **LeaveVoidAsync**: Locked cleanup of all POIs, garden instance, and tracking set entry.

### POI Interaction (3 endpoints)

- **ListPoisAsync**: Returns all active POIs for the player's void instance.
- **InteractWithPoiAsync**: Determines interaction result based on POI trigger mode. Prompted mode returns `ScenarioPrompt` with hardcoded choices `["Enter", "Decline"]` and template display name. Proximity/Interaction/Forced modes return `ScenarioEnter`. Marks POI as Entered.
- **DeclinePoiAsync**: Marks POI as Declined, adds template to garden's `ScenarioHistory` for diversity scoring, triggers re-evaluation.

### Scenario Lifecycle (6 endpoints)

- **EnterScenarioAsync**: Validates template active + allowed in phase + global capacity. Creates backing game session via `IGameSessionClient`. Moves player from void tracking to scenario tracking. Cleans up garden instance and all POIs. Optionally notifies Puppetmaster (soft dependency).
- **GetScenarioStateAsync**: Read-only scenario lookup.
- **CompleteScenarioAsync**: Calculates full growth via `GardenerGrowthCalculation`, awards to all participants via `ISeedClient.RecordGrowthBatchAsync`, writes history to MySQL, cleans up Redis + game session.
- **AbandonScenarioAsync**: Same as complete but with partial growth calculation and Abandoned status.
- **ChainScenarioAsync**: Validates chaining rules (template `LeadsTo` list and `MaxChainDepth`). Completes current scenario with growth, creates new chained scenario reusing the same game session. Increments chain depth.
- **EnterScenarioTogetherAsync** (Bond): Resolves bond participants via `ISeedClient.GetBondAsync` + `GetSeedAsync`. Validates both in void, template supports multiplayer. Creates shared game session and scenario for all participants.

### Template Management (6 endpoints)

Standard CRUD on scenario templates with JSON-queryable MySQL storage. Code uniqueness enforced via JSON query on creation. `ListTemplatesAsync` supports filtering by category, connectivity mode, status, and deployment phase. Deployment phase filtering is done in-memory post-query because JSON array contains is not supported in queries. `DeprecateTemplateAsync` sets status to Deprecated and publishes update event.

### Phase Management (3 endpoints)

- **GetPhaseConfigAsync**: Returns phase config singleton, auto-creates with defaults if missing.
- **UpdatePhaseConfigAsync**: Locked update of phase config. Publishes `gardener.phase.changed` event only if phase actually changed.
- **GetPhaseMetricsAsync**: Returns active void/scenario counts from tracking sets and capacity utilization ratio.

### Bond Features (2 endpoints)

- **EnterScenarioTogetherAsync**: Documented under Scenario Lifecycle above.
- **GetSharedVoidStateAsync**: Returns merged view of all bond participants' void states (positions and POIs).

## Scenario Selection Algorithm

The `GardenerVoidOrchestratorWorker` spawns POIs using a weighted scoring formula:

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

```
                        ┌──────────────────────────────┐
                        │     EnterVoidAsync           │
                        │  (Creates GardenInstance)    │
                        └──────────┬───────────────────┘
                                   │
                                   ▼
    ┌───────────────────────────────────────────────────────┐
    │              VOID (Active Garden)                      │
    │                                                        │
    │  void:{accountId} ──── ActivePoiIds ────┐              │
    │  [Redis]               [List<Guid>]     │              │
    │                                         ▼              │
    │  ┌───────────────────────────────────────────┐         │
    │  │ GardenerVoidOrchestratorWorker (tick)      │         │
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

1. **MinGrowthPhase filtering**: The `GetEligibleTemplatesAsync` method in `GardenerVoidOrchestratorWorker` has a MinGrowthPhase check block (lines 457-465) with an empty body -- the comment says "For now, include all templates where the player has any growth phase." Growth phases are opaque strings without ordinal comparison, so phase ordering would need a lookup table.

2. **scenario-template.deleted event**: Declared in the events schema via `x-lifecycle` but no delete endpoint exists in the API. The lifecycle event is generated but never published.

3. **Puppetmaster notification**: `EnterScenarioAsync` resolves `IPuppetmasterClient` and logs intent to notify about behavior documents, but does not actually call any Puppetmaster API. The notification is a log-only stub.

4. **PersistentEntryEnabled / GardenMinigamesEnabled**: These phase config flags are stored and returned but never read by any service logic. They appear to be forward-looking feature flags.

5. **Matchmaking integration**: The implementation plan specified `IMatchmakingClient` (L4 soft dependency) for submitting matchmaking tickets when templates have `MinPlayers > 1`. This was never implemented -- group scenarios beyond bond pairs have no queueing mechanism.

6. **Analytics integration**: The implementation plan specified `IAnalyticsClient` (L4 soft dependency) for querying milestone data to enrich scenario scoring. This was never implemented -- the `NarrativeWeight` scoring component partially compensates but milestone-based scoring is absent.

7. **Client event schema (gardener-client-events.yaml)**: No client event schema exists for real-time POI push to WebSocket clients. POI spawns, expirations, and trigger events happen server-side only. Clients must poll `GetVoidStateAsync` to discover changes, which defeats the purpose of the void as a responsive discovery space. This is a significant gap for the intended player experience.

8. **ConnectivityMode.Persistent has no special handling**: The enum value exists (Alpha, Beta, Release map to Isolated, WorldSlice, Persistent) but the code treats all connectivity modes identically during scenario creation and lifecycle. The vision describes Persistent as "a scenario that doesn't end" -- the release surprise. No code path distinguishes Persistent scenarios from Isolated ones.

9. **Prerequisite validation during scenario entry**: Templates store `Prerequisites` (required domains, required/excluded scenarios) in the model but they are never validated in `EnterScenarioAsync`. A player can enter any scenario regardless of prerequisites. The `VoidOrchestratorWorker.GetEligibleTemplatesAsync` also does not filter by prerequisites.

10. **Per-template MaxConcurrentInstances enforcement**: Templates store a `MaxConcurrentInstances` value but it is never checked during scenario entry. Only the global `MaxConcurrentScenariosGlobal` is enforced.

## Potential Extensions

1. **Scenario history cleanup**: History records in MySQL accumulate indefinitely. No retention policy or cleanup mechanism exists. A background worker or configurable retention window (similar to transaction history in lib-currency) would prevent unbounded growth.

2. **Multi-participant history**: `WriteScenarioHistoryAsync` only writes a history record for the primary participant. Secondary participants in bond scenarios do not get individual history records. This means cooldown checking and scenario diversity scoring are inaccurate for non-primary bond participants.

3. **Template delete endpoint**: A delete API endpoint could allow removing deprecated templates and publishing the already-declared `scenario-template.deleted` lifecycle event.

4. **Content flywheel connection**: Scenarios complete and award growth, but there's no mechanism to feed scenario outcomes back into future content generation. No events are consumed by Storyline, no archive data is generated from scenario history. Per VISION.md, this is a load-bearing connection: "more play produces more content, which produces more play." Gardener generates play but doesn't yet contribute to the content side of the flywheel.

5. **Dialog choice trigger mode**: PLAYER-VISION.md describes four acceptance modes: implicit (proximity), prompted (acknowledge), dialog choices (multiple branching options), and forced. The implementation has Proximity, Interaction, Prompted, and Forced -- but "dialog choices" (presenting multiple distinct branching options beyond Enter/Decline) is missing. Prompted mode only offers binary accept/decline.

6. **Void position binary protocol optimization**: `UpdatePositionAsync` uses POST JSON for high-frequency position updates. PLAYER-VISION.md describes the void as "negligible bandwidth" but JSON POST per tick adds overhead. Binary protocol through Connect would reduce this but was deferred per the plan ("optimize only if latency/throughput becomes a problem").

7. **Multi-seed-type support**: `SeedTypeCode` is a single config property (default "guardian"). PLAYER-VISION.md describes multiple seed types (guardian spirit, dungeon master, realm-specific). Each type would need its own Gardener instance or the service would need to manage multiple types. The current architecture supports this (deploy multiple Gardener instances with different `GARDENER_SEED_TYPE_CODE`), but this deployment pattern is undocumented.

8. **Analytics milestone integration for scoring**: The plan specified consuming `analytics.milestone.reached` events to enrich scenario selection. When Analytics publishes this event, a subscription could be added to boost templates that align with player milestone achievements.

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Guid.Empty sentinel for WebSocketSessionId**: Both `TryCleanupGameSessionAsync` (GardenerService.cs:1674) and `ForceCompleteScenarioAsync` (GardenerScenarioLifecycleWorker.cs:393) pass `Guid.Empty` as `WebSocketSessionId` when calling `LeaveGameSessionByIdAsync`. This is a no-sentinel tenet violation. The game-session schema requires a non-nullable `webSocketSessionId` field but server-side leave operations have no real WebSocket session. Fix requires making `webSocketSessionId` nullable in the game-session API schema.

2. **Guid.Empty for GameServiceId in seed type registration**: `GardenerServicePlugin.OnRunningAsync` (line 69) passes `Guid.Empty` for `GameServiceId` when registering the guardian seed type. Comment says "cross-game; resolved at runtime" but this is a no-sentinel tenet violation. If the seed type is truly cross-game, `GameServiceId` should be nullable in the seed API schema.

3. **ListTemplatesAsync in-memory phase filtering breaks pagination**: When `DeploymentPhase` filter is provided, templates are filtered in-memory after the database query. The response uses `templateList.Count` instead of `result.TotalCount`, making TotalCount inaccurate relative to page/pageSize. If 100 templates exist but only 10 match the phase filter on page 1, the client sees TotalCount=10 regardless of what's on other pages.

4. **Missing try-catch around CreateGameSessionAsync**: In `EnterScenarioAsync` (line 607) and `EnterScenarioTogetherAsync` (line 1383), `_gameSessionClient.CreateGameSessionAsync` is called without try-catch. If game session creation fails, the exception propagates unhandled. Per error handling tenets, external service calls should be wrapped with `ApiException` distinction. The void tracking set and garden instance may be in an inconsistent state if this call fails mid-operation (garden already deleted, but scenario never created -- player stuck with no void and no scenario). Fix requires try-catch with compensation (restore garden instance on failure) or transactional ordering (create game session before deleting garden).

5. **EnterVoidAsync has no distributed lock**: Unlike `LeaveVoidAsync`, `InteractWithPoiAsync`, `DeclinePoiAsync`, and all scenario mutation methods, `EnterVoidAsync` does not acquire a distributed lock before checking for an existing garden and creating a new one. Two concurrent calls for the same account could both pass the existence check (line 179-184), both create a garden instance, and both publish `gardener.void.entered` events. The second `SaveAsync` overwrites the first, and the tracking set `AddToSetAsync` is idempotent, but the first call's event would reference a garden that was immediately replaced. Unlike `UpdatePositionAsync` (which intentionally omits the lock for latency on high-frequency calls), `EnterVoidAsync` is a low-frequency operation where a lock would be appropriate.

6. **Hardcoded query page sizes in VoidOrchestratorWorker (T21 violation)**: `GetEligibleTemplatesAsync` (GardenerVoidOrchestratorWorker.cs:427) queries templates with hardcoded `page: 0, pageSize: 500` and history with `page: 0, pageSize: 200` (line 438). If a deployment has more than 500 active templates or a player has more than 200 history records, excess entries are silently ignored. These limits should be configuration properties per the Configuration-First tenet.

7. **Race condition in background worker POI expiry**: `GardenerVoidOrchestratorWorker.ExpireStalePoiAsync` modifies POI state (setting `Status = Expired`) and removes POI IDs from the garden's `ActivePoiIds` list without acquiring a distributed lock for the garden instance. If a player interacts with a POI via `InteractWithPoiAsync` (which does acquire a lock) at the same moment the worker expires it, the worker's unlocked write could overwrite the interaction result. The API and worker operate on the same Redis keys without coordination. Fix requires either acquiring a garden-level lock in the worker's per-garden processing or accepting and documenting last-write-wins semantics for POI state.

### Intentional Quirks (Documented Behavior)

1. **UpdatePositionAsync has no distributed lock**: Intentionally omitted for latency. High-frequency position updates are idempotent (last-write-wins), and stale reads only affect drift metric precision. Documented in code via `<remarks>` block.

2. **Hardcoded prompt choices**: `InteractWithPoiAsync` returns `["Enter", "Decline"]` as hardcoded prompt choices for `TriggerMode.Prompted` POIs. These are presentation hints for the client, not authoritative -- the client renders them but the server doesn't validate responses against them.

3. **Hardcoded game type string**: `"gardener-scenario"` is used as the game type string for game session creation. This is a convention-based identifier used only by Gardener and is filtered in the `HandleGameSessionDeletedAsync` event handler.

4. **HandleGameSessionDeletedAsync is log-only**: The event handler for `game-session.deleted` only logs the event. Actual cleanup relies on the `GardenerScenarioLifecycleWorker` detecting the orphaned scenario via `LastActivityAt` timeout. This is intentional because there is no reverse-lookup from GameSessionId to account ID in the scenario store (scenarios are keyed by `scenario:{accountId}`).

5. **ISeedEvolutionListener is a separate singleton class**: `GardenerSeedEvolutionListener` is registered as a Singleton rather than being part of `GardenerService` (which is Scoped). This is required because `ISeedEvolutionListener` must be resolvable from BackgroundService (Singleton) context. Follows the same pattern as `SeedCollectionUnlockListener`.

6. **Tracking sets use GardenCacheStore from void-instances**: The `ActiveVoidsTrackingKey` and `ActiveScenariosTrackingKey` Redis sets use the `ICacheableStateStore<GardenInstanceModel>` backed by the `gardener-void-instances` store. This works because Redis set operations are independent of the store's value type. The tracking sets are separate data structures within the same Redis namespace.

7. **Chained scenarios reuse the game session**: When `ChainScenarioAsync` transitions to a new scenario, it reuses the previous scenario's `GameSessionId` rather than creating a new game session. This keeps the player in the same session context across the chain.

### Design Considerations (Requires Planning)

1. **Growth phase ordering is unresolved**: Templates can specify `MinGrowthPhase` but there is no mechanism to compare phase labels ordinally. The growth phases ("nascent", "awakening", "attuned", "resonant", "transcendent") are registered with `MinTotalGrowth` thresholds in the Seed service, but Gardener only caches the phase label string. Implementing this requires either querying Seed for phase ordering or maintaining a local phase-to-ordinal mapping.

2. **DeploymentPhase filter needs MySQL JSON array query**: The `ListTemplatesAsync` in-memory filtering for `AllowedPhases` is a workaround because MySQL JSON path queries don't support "array contains value" natively in the lib-state abstraction. A proper fix requires either extending `IJsonQueryableStateStore` with array-contains semantics or using a different storage pattern (e.g., separate phase-template mapping).

## Work Tracking

*(No active work tracking items)*
