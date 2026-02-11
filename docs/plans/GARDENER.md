# Implementation Plan: Gardener Service (L4 Game Features)

> **Related**: `docs/plugins/SEED.md`, `docs/guides/SEED-SYSTEM.md`, `docs/reference/SERVICE-HIERARCHY.md`
> **Prerequisite**: lib-seed must be fully implemented and operational before starting Gardener

## Context

The Gardener service is a new L4 Game Features plugin that orchestrates the player experience -- void navigation, scenario routing, progressive discovery, and deployment phase management. It is the player-side counterpart to Puppetmaster: where Puppetmaster orchestrates what NPCs experience, Gardener orchestrates what players experience.

Gardener is the first and primary consumer of lib-seed. It interprets seed capabilities as UX modules, uses seed growth phases for scenario selection, and manages seed bonds as the pair system described in PLAYER-VISION.md. However, Gardener does NOT own seeds -- it creates and grows them via lib-seed's API, just as lib-escrow doesn't own currencies or items but orchestrates them via lib-currency and lib-item.

**Why L4**: Gardener needs to read from nearly every layer: Seed and Game Session (L2), Asset (L3), Analytics, Behavior, Puppetmaster, Matchmaking (L4). It orchestrates the full player experience by composing content from across the service hierarchy. Classic L4 -- optional, feature-rich, maximum connectivity. None of Gardener's responsibilities (void navigation, POI spawning, scenario routing, deployment phase management) generalize to other seed consumers.

**Design principle**: Puppetmaster orchestrates what NPCs experience. Gardener orchestrates what players experience. Seed is the foundational growth primitive that both (and future systems) reference. Each consumer provides its own domain-specific orchestration on top of the shared Seed foundation.

**Open question resolutions** *(decisions made during planning)*:
- **Void position protocol**: Start with POST JSON; optimize to binary through Connect only if latency/throughput becomes a problem.
- **Growth phase as convenience vs. gate**: Soft filter with configurable strictness. Phases are never hard gates, but Gardener uses them for scenario selection (`minGrowthPhase`) with a strong bias rather than an absolute requirement.
- **Seed type registration timing**: Both -- configuration for built-in types (e.g., `guardian`), API for dynamic types added later. The Gardener's `SeedTypeCode` config property determines which seed type it manages.
- **Gardener orchestration extraction trigger**: Not until at least three seed consumers demonstrate genuinely shared orchestration logic. The abstraction should be extracted from working code, not pre-designed.

---

## Implementation Steps

### Step 1: Create Schema Files

#### 1a. `schemas/gardener-api.yaml`

Write the schema from the endpoint specifications below. Key requirements:

- **Header**: `x-service-layer: GameFeatures`, `servers: [{ url: http://localhost:5012 }]`
- **Fix x-permissions format**: Translate planning doc shorthand to actual format:
  - `[authenticated]` -> `- role: user` + `states: {}`
  - `[developer]` -> `- role: developer` + `states: {}`
- **Ensure all properties have `description` fields** (CS1591 compliance)
- **Ensure NRT compliance**: optional ref types have `nullable: true`, required fields in `required` arrays

**25 POST endpoints** across five API groups:

| Group | Endpoints | Permissions |
|-------|-----------|-------------|
| Void Management (4) | enter, get, update-position, leave | `user` |
| POI Interaction (3) | list, interact, decline | `user` |
| Scenario Lifecycle (5) | enter, get, complete, abandon, chain | `user` |
| Scenario Template Management (6) | create, get, get-by-code, list, update, deprecate | `developer` |
| Deployment Phase (3) | get, update, get-metrics | `developer` |
| Bond Scenarios (2) | enter-together, get-shared-void | `user` |

**Enums** (8 total, defined in `components/schemas`):
- `DeploymentPhase`: Alpha, Beta, Release
- `ConnectivityMode`: Isolated, WorldSlice, Persistent
- `ScenarioCategory`: Combat, Crafting, Social, Trade, Exploration, Magic, Survival, Mixed, Narrative, Tutorial
- `PoiType`: Visual, Auditory, Environmental, Portal, Social
- `TriggerMode`: Proximity, Interaction, Prompted, Forced
- `PoiStatus`: Active, Entered, Declined, Expired
- `ScenarioStatus`: Initializing, Active, Completing, Completed, Abandoned
- `TemplateStatus`: Draft, Active, Deprecated

**Shared sub-types** (defined in `components/schemas`, used by multiple request/response models):
- `Vec3` (x, y, z floats) -- spatial coordinates in void space
- `PoiSummary` -- POI state for client display
- `DomainWeight` -- domain + weight pair for scenario templates
- `ScenarioPrerequisites` -- domain requirements, scenario completion requirements, exclusions
- `ScenarioChaining` -- leadsTo codes, chain probabilities, max chain depth
- `ScenarioMultiplayer` -- min/max players, matchmaking queue code, bond preferred flag
- `ScenarioContent` -- behavior document ID, scene document ID, realm ID, location code
- `BondedPlayerVoidState` -- per-player void state within a shared bond void

**Request models** (one per endpoint):
- `EnterVoidRequest`: accountId, sessionId (both required uuid)
- `GetVoidStateRequest`: accountId (required uuid)
- `UpdatePositionRequest`: accountId (required uuid), position (Vec3), velocity (Vec3)
- `LeaveVoidRequest`: accountId (required uuid)
- `ListPoisRequest`: accountId (required uuid)
- `InteractWithPoiRequest`: accountId (required uuid), poiId (required uuid)
- `DeclinePoiRequest`: accountId (required uuid), poiId (required uuid)
- `EnterScenarioRequest`: accountId (required uuid), scenarioTemplateId (required uuid), poiId (nullable uuid), promptChoice (nullable string)
- `GetScenarioStateRequest`: accountId (required uuid)
- `CompleteScenarioRequest`: accountId (required uuid), scenarioInstanceId (required uuid)
- `AbandonScenarioRequest`: accountId (required uuid), scenarioInstanceId (required uuid)
- `ChainScenarioRequest`: accountId (required uuid), currentScenarioInstanceId (required uuid), targetTemplateId (required uuid)
- `CreateTemplateRequest`: code (required string), displayName (required string), description (required string), category (required ScenarioCategory), subcategory (nullable string), domainWeights (required array DomainWeight), minGrowthPhase (nullable string), connectivityMode (ConnectivityMode, default Isolated), allowedPhases (required array DeploymentPhase), maxConcurrentInstances (int, default 100), estimatedDurationMinutes (nullable int), prerequisites (nullable ScenarioPrerequisites), chaining (nullable ScenarioChaining), multiplayer (nullable ScenarioMultiplayer), content (nullable ScenarioContent)
- `GetTemplateRequest`: scenarioTemplateId (required uuid)
- `GetTemplateByCodeRequest`: code (required string)
- `ListTemplatesRequest`: category (nullable ScenarioCategory), connectivityMode (nullable ConnectivityMode), deploymentPhase (nullable DeploymentPhase), status (nullable TemplateStatus), page (int, default 1), pageSize (int, default 50)
- `UpdateTemplateRequest`: scenarioTemplateId (required uuid), displayName (nullable string), description (nullable string), domainWeights (nullable array DomainWeight), maxConcurrentInstances (nullable int), prerequisites (nullable ScenarioPrerequisites), chaining (nullable ScenarioChaining), multiplayer (nullable ScenarioMultiplayer), content (nullable ScenarioContent)
- `DeprecateTemplateRequest`: scenarioTemplateId (required uuid)
- `GetPhaseConfigRequest`: empty object
- `UpdatePhaseConfigRequest`: currentPhase (nullable DeploymentPhase), maxConcurrentScenariosGlobal (nullable int), persistentEntryEnabled (nullable bool), voidMinigamesEnabled (nullable bool)
- `GetPhaseMetricsRequest`: empty object
- `EnterTogetherRequest`: bondId (required uuid), scenarioTemplateId (required uuid)
- `GetSharedVoidRequest`: bondId (required uuid)

**Response models**:
- `VoidStateResponse`: voidInstanceId, seedId, accountId (all required uuid), position (Vec3), activePois (array PoiSummary)
- `PositionUpdateResponse`: acknowledged (required bool), triggeredPois (nullable array PoiSummary)
- `LeaveVoidResponse`: accountId (required uuid), sessionDurationSeconds (required float)
- `ListPoisResponse`: voidInstanceId (required uuid), pois (required array PoiSummary)
- `PoiInteractionResponse`: poiId (required uuid), result (required string -- one of "scenario_prompt", "scenario_enter", "poi_update", "chain_offer"), scenarioTemplateId (nullable uuid), promptText (nullable string), promptChoices (nullable array string)
- `DeclinePoiResponse`: poiId (required uuid), acknowledged (required bool)
- `ScenarioStateResponse`: scenarioInstanceId, scenarioTemplateId, gameSessionId (all required uuid), connectivityMode (required ConnectivityMode), status (required ScenarioStatus), createdAt (required date-time), chainedFrom (nullable uuid), chainDepth (int, default 0)
- `ScenarioCompletionResponse`: scenarioInstanceId (required uuid), growthAwarded (required map<string, float>), returnToVoid (required bool)
- `AbandonScenarioResponse`: scenarioInstanceId (required uuid), partialGrowthAwarded (required map<string, float>)
- `ScenarioTemplateResponse`: Full template with all fields -- scenarioTemplateId, code, displayName, description, category, subcategory, domainWeights, minGrowthPhase, connectivityMode, allowedPhases, maxConcurrentInstances, estimatedDurationMinutes, prerequisites, chaining, multiplayer, content, status, createdAt, updatedAt
- `ListTemplatesResponse`: templates (array ScenarioTemplateResponse), totalCount (int), page (int), pageSize (int)
- `PhaseConfigResponse`: currentPhase (DeploymentPhase), maxConcurrentScenariosGlobal (int), persistentEntryEnabled (bool), voidMinigamesEnabled (bool)
- `PhaseMetricsResponse`: currentPhase (DeploymentPhase), activeVoidInstances (int), activeScenarioInstances (int), scenarioCapacityUtilization (float, 0.0-1.0)
- `SharedVoidStateResponse`: bondId (uuid), participants (array BondedPlayerVoidState), sharedPois (array PoiSummary)

#### 1b. `schemas/gardener-events.yaml`

**x-lifecycle** for `ScenarioTemplate` entity (generates created/updated/deleted events):
- Model fields: scenarioTemplateId (primary), code, displayName, description, category, connectivityMode, status, createdAt, updatedAt
- Sensitive: content (exclude from lifecycle events -- may contain large nested objects)

**x-event-subscriptions** (consumed events):
- `seed.phase.changed` -> `SeedPhaseChangedEvent` -> `HandleSeedPhaseChanged` -- adjust scenario offerings based on new phase
- `seed.growth.updated` -> `SeedGrowthUpdatedEvent` -> `HandleSeedGrowthUpdated` -- recalculate scenario scoring for void instances
- `seed.bond.formed` -> `SeedBondFormedEvent` -> `HandleSeedBondFormed` -- enable bond-specific scenarios and shared void
- `seed.activated` -> `SeedActivatedEvent` -> `HandleSeedActivated` -- track which seed is active for an owner
- `game-session.ended` -> `GameSessionEndedEvent` -> `HandleGameSessionEnded` -- scenario instance cleanup when backing session ends

**x-event-publications** (published events):
- Lifecycle events from x-lifecycle: `scenario-template.created`, `scenario-template.updated`, `scenario-template.deleted`
- Custom events:
  - `gardener.void.entered` -> `GardenerVoidEnteredEvent` -- player entered the void
  - `gardener.void.left` -> `GardenerVoidLeftEvent` -- player left the void
  - `gardener.poi.spawned` -> `GardenerPoiSpawnedEvent` -- POI spawned for a player
  - `gardener.poi.entered` -> `GardenerPoiEnteredEvent` -- player entered a POI / triggered a scenario
  - `gardener.poi.declined` -> `GardenerPoiDeclinedEvent` -- player declined a POI
  - `gardener.poi.expired` -> `GardenerPoiExpiredEvent` -- POI expired without interaction
  - `gardener.scenario.started` -> `GardenerScenarioStartedEvent` -- scenario instance created
  - `gardener.scenario.completed` -> `GardenerScenarioCompletedEvent` -- scenario completed (includes growthAwarded)
  - `gardener.scenario.abandoned` -> `GardenerScenarioAbandonedEvent` -- scenario abandoned
  - `gardener.scenario.chained` -> `GardenerScenarioChainedEvent` -- player chained from one scenario to another
  - `gardener.bond.entered-together` -> `GardenerBondEnteredTogetherEvent` -- bonded players entered a scenario together
  - `gardener.phase.changed` -> `GardenerPhaseChangedEvent` -- deployment phase changed

**Custom event schemas** (in `components/schemas`):

```yaml
GardenerVoidEnteredEvent:
  type: object
  required: [eventId, accountId, seedId, voidInstanceId]
  properties:
    eventId: { type: string, format: uuid }
    accountId: { type: string, format: uuid }
    seedId: { type: string, format: uuid }
    voidInstanceId: { type: string, format: uuid }

GardenerVoidLeftEvent:
  type: object
  required: [eventId, accountId, voidInstanceId, sessionDurationSeconds]
  properties:
    eventId: { type: string, format: uuid }
    accountId: { type: string, format: uuid }
    voidInstanceId: { type: string, format: uuid }
    sessionDurationSeconds: { type: number, format: float }

GardenerPoiSpawnedEvent:
  type: object
  required: [eventId, voidInstanceId, poiId, poiType, scenarioTemplateId]
  properties:
    eventId: { type: string, format: uuid }
    voidInstanceId: { type: string, format: uuid }
    poiId: { type: string, format: uuid }
    poiType: { $ref: 'gardener-api.yaml#/components/schemas/PoiType' }
    scenarioTemplateId: { type: string, format: uuid }

GardenerPoiEnteredEvent:
  type: object
  required: [eventId, accountId, poiId, scenarioTemplateId]
  properties:
    eventId: { type: string, format: uuid }
    accountId: { type: string, format: uuid }
    poiId: { type: string, format: uuid }
    scenarioTemplateId: { type: string, format: uuid }

GardenerPoiDeclinedEvent:
  type: object
  required: [eventId, accountId, poiId, scenarioTemplateId]
  properties:
    eventId: { type: string, format: uuid }
    accountId: { type: string, format: uuid }
    poiId: { type: string, format: uuid }
    scenarioTemplateId: { type: string, format: uuid }

GardenerPoiExpiredEvent:
  type: object
  required: [eventId, voidInstanceId, poiId]
  properties:
    eventId: { type: string, format: uuid }
    voidInstanceId: { type: string, format: uuid }
    poiId: { type: string, format: uuid }

GardenerScenarioStartedEvent:
  type: object
  required: [eventId, scenarioInstanceId, scenarioTemplateId, gameSessionId, accountId, seedId]
  properties:
    eventId: { type: string, format: uuid }
    scenarioInstanceId: { type: string, format: uuid }
    scenarioTemplateId: { type: string, format: uuid }
    gameSessionId: { type: string, format: uuid }
    accountId: { type: string, format: uuid }
    seedId: { type: string, format: uuid }

GardenerScenarioCompletedEvent:
  type: object
  required: [eventId, scenarioInstanceId, scenarioTemplateId, accountId, growthAwarded]
  properties:
    eventId: { type: string, format: uuid }
    scenarioInstanceId: { type: string, format: uuid }
    scenarioTemplateId: { type: string, format: uuid }
    accountId: { type: string, format: uuid }
    growthAwarded:
      type: object
      additionalProperties: { type: number, format: float }

GardenerScenarioAbandonedEvent:
  type: object
  required: [eventId, scenarioInstanceId, accountId]
  properties:
    eventId: { type: string, format: uuid }
    scenarioInstanceId: { type: string, format: uuid }
    accountId: { type: string, format: uuid }

GardenerScenarioChainedEvent:
  type: object
  required: [eventId, previousScenarioInstanceId, newScenarioInstanceId, accountId, chainDepth]
  properties:
    eventId: { type: string, format: uuid }
    previousScenarioInstanceId: { type: string, format: uuid }
    newScenarioInstanceId: { type: string, format: uuid }
    accountId: { type: string, format: uuid }
    chainDepth: { type: integer }

GardenerBondEnteredTogetherEvent:
  type: object
  required: [eventId, bondId, scenarioInstanceId, scenarioTemplateId, participants]
  properties:
    eventId: { type: string, format: uuid }
    bondId: { type: string, format: uuid }
    scenarioInstanceId: { type: string, format: uuid }
    scenarioTemplateId: { type: string, format: uuid }
    participants:
      type: array
      items: { type: string, format: uuid }
      description: Seed IDs of bond participants entering together.

GardenerPhaseChangedEvent:
  type: object
  required: [eventId, previousPhase, newPhase]
  properties:
    eventId: { type: string, format: uuid }
    previousPhase: { $ref: 'gardener-api.yaml#/components/schemas/DeploymentPhase' }
    newPhase: { $ref: 'gardener-api.yaml#/components/schemas/DeploymentPhase' }
```

#### 1c. `schemas/gardener-configuration.yaml`

All properties with `env: GARDENER_{PROPERTY}` format, single-line descriptions:

```yaml
x-service-configuration:
  properties:
    # Void orchestration
    VoidTickIntervalMs:
      type: integer
      env: GARDENER_VOID_TICK_INTERVAL_MS
      minimum: 1000
      maximum: 60000
      default: 5000
      description: Milliseconds between void orchestrator evaluation cycles

    MaxActivePoisPerVoid:
      type: integer
      env: GARDENER_MAX_ACTIVE_POIS_PER_VOID
      minimum: 1
      maximum: 50
      default: 8
      description: Maximum concurrent POIs per player void instance

    PoiDefaultTtlMinutes:
      type: integer
      env: GARDENER_POI_DEFAULT_TTL_MINUTES
      minimum: 1
      maximum: 120
      default: 10
      description: Default time-to-live in minutes for spawned POIs before expiration

    PoiSpawnRadiusMin:
      type: number
      format: float
      env: GARDENER_POI_SPAWN_RADIUS_MIN
      default: 50.0
      description: Minimum distance from player to spawn a POI in void space units

    PoiSpawnRadiusMax:
      type: number
      format: float
      env: GARDENER_POI_SPAWN_RADIUS_MAX
      default: 200.0
      description: Maximum distance from player to spawn a POI in void space units

    MinPoiSpacing:
      type: number
      format: float
      env: GARDENER_MIN_POI_SPACING
      default: 30.0
      description: Minimum distance between any two POIs in void space units

    # Scenario selection algorithm weights
    AffinityWeight:
      type: number
      format: float
      env: GARDENER_AFFINITY_WEIGHT
      minimum: 0.0
      maximum: 1.0
      default: 0.4
      description: Weight for domain affinity in scenario scoring algorithm

    DiversityWeight:
      type: number
      format: float
      env: GARDENER_DIVERSITY_WEIGHT
      minimum: 0.0
      maximum: 1.0
      default: 0.3
      description: Weight for category diversity in scenario scoring algorithm

    NarrativeWeight:
      type: number
      format: float
      env: GARDENER_NARRATIVE_WEIGHT
      minimum: 0.0
      maximum: 1.0
      default: 0.2
      description: Weight for drift-pattern narrative response in scenario scoring

    RandomWeight:
      type: number
      format: float
      env: GARDENER_RANDOM_WEIGHT
      minimum: 0.0
      maximum: 1.0
      default: 0.1
      description: Weight for randomness and discovery in scenario scoring

    RecentScenarioCooldownMinutes:
      type: integer
      env: GARDENER_RECENT_SCENARIO_COOLDOWN_MINUTES
      minimum: 0
      maximum: 1440
      default: 30
      description: Minutes before a completed scenario can be re-offered to the same player

    # Scenario instances
    MaxConcurrentScenariosGlobal:
      type: integer
      env: GARDENER_MAX_CONCURRENT_SCENARIOS_GLOBAL
      minimum: 1
      default: 1000
      description: Maximum total active scenario instances across all players

    ScenarioTimeoutMinutes:
      type: integer
      env: GARDENER_SCENARIO_TIMEOUT_MINUTES
      minimum: 5
      maximum: 480
      default: 60
      description: Maximum scenario duration before forced completion in minutes

    AbandonDetectionMinutes:
      type: integer
      env: GARDENER_ABANDON_DETECTION_MINUTES
      minimum: 1
      maximum: 60
      default: 5
      description: Minutes without player input before scenario is marked abandoned

    GrowthAwardMultiplier:
      type: number
      format: float
      env: GARDENER_GROWTH_AWARD_MULTIPLIER
      minimum: 0.0
      default: 1.0
      description: Global multiplier applied to all growth awards from scenario completion

    # Bond features
    BondSharedVoidEnabled:
      type: boolean
      env: GARDENER_BOND_SHARED_VOID_ENABLED
      default: true
      description: Whether bonded players share a void instance with merged POIs

    BondScenarioPriority:
      type: number
      format: float
      env: GARDENER_BOND_SCENARIO_PRIORITY
      minimum: 1.0
      default: 1.5
      description: Scoring boost multiplier for bond-friendly scenarios when player is bonded

    # Phase defaults
    DefaultPhase:
      $ref: 'gardener-api.yaml#/components/schemas/DeploymentPhase'
      env: GARDENER_DEFAULT_PHASE
      default: Alpha
      description: Starting deployment phase for new installations

    # Seed integration
    SeedTypeCode:
      type: string
      env: GARDENER_SEED_TYPE_CODE
      default: guardian
      description: Which seed type code this gardener manages for player spirits

    # Background workers
    ScenarioLifecycleWorkerIntervalSeconds:
      type: integer
      env: GARDENER_SCENARIO_LIFECYCLE_WORKER_INTERVAL_SECONDS
      minimum: 5
      maximum: 300
      default: 30
      description: Seconds between scenario lifecycle worker evaluation cycles
```

#### 1d. Update `schemas/state-stores.yaml`

Add under `x-state-stores:`:

```yaml
gardener-void-instances:
  backend: redis
  prefix: "gardener:void"
  service: Gardener
  purpose: Active void instance state (ephemeral, per-session)

gardener-pois:
  backend: redis
  prefix: "gardener:poi"
  service: Gardener
  purpose: Active POIs per void instance (ephemeral)

gardener-scenario-templates:
  backend: mysql
  service: Gardener
  purpose: Registered scenario template definitions (durable, queryable)

gardener-scenario-instances:
  backend: redis
  prefix: "gardener:scenario"
  service: Gardener
  purpose: Active scenario instance state (ephemeral)

gardener-scenario-history:
  backend: mysql
  service: Gardener
  purpose: Completed scenario records (durable, for analytics and cooldown tracking)

gardener-phase-config:
  backend: mysql
  service: Gardener
  purpose: Deployment phase configuration (durable, admin-managed)

gardener-lock:
  backend: redis
  prefix: "gardener:lock"
  service: Gardener
  purpose: Distributed locks for void and scenario mutations
```

### Step 2: Generate Service (creates project, code, and templates)

```bash
cd scripts && ./generate-service.sh gardener
```

This single command bootstraps the entire plugin. It auto-creates:

**Plugin project infrastructure** (via `generate-project.sh`):
- `plugins/lib-gardener/` directory
- `plugins/lib-gardener/lib-gardener.csproj` (with ServiceLib.targets import)
- `plugins/lib-gardener/AssemblyInfo.cs` (ApiController, InternalsVisibleTo)
- Adds `lib-gardener` to `bannou-service.sln` via `dotnet sln add`

**Generated code** (in `plugins/lib-gardener/Generated/`):
- `IGardenerService.cs` - interface
- `GardenerController.cs` - HTTP routing
- `GardenerController.Meta.cs` - runtime schema introspection
- `GardenerServiceConfiguration.cs` - typed config class
- `GardenerPermissionRegistration.cs` - permissions
- `GardenerEventsController.cs` - event subscription handlers (from x-event-subscriptions)

**Generated code** (in `bannou-service/Generated/`):
- `Models/GardenerModels.cs` - request/response models
- `Clients/GardenerClient.cs` - client for other services to call Gardener
- `Events/GardenerEventsModels.cs` - event models
- Updated `StateStoreDefinitions.cs` with Gardener store constants

**Template files** (created once if missing, never overwritten):
- `plugins/lib-gardener/GardenerService.cs` - business logic template with TODO stubs
- `plugins/lib-gardener/GardenerServiceModels.cs` - internal models template
- `plugins/lib-gardener/GardenerServicePlugin.cs` - plugin registration template

**Test project** (via `generate-tests.sh`):
- `plugins/lib-gardener.tests/` directory, `.csproj`, `AssemblyInfo.cs`, `GlobalUsings.cs`
- `GardenerServiceTests.cs` template with basic tests
- Adds `lib-gardener.tests` to `bannou-service.sln` via `dotnet sln add`

**Build check**: `dotnet build` to verify generation succeeded.

### Step 3: Fill In Plugin Registration

#### 3a. `plugins/lib-gardener/GardenerServicePlugin.cs` (generated template -> fill in)

The generator creates the skeleton. Fill in following the GameSessionServicePlugin pattern:

- Extends `BaseBannouPlugin`
- `PluginName => "gardener"`, `DisplayName => "Gardener Service"`
- Standard lifecycle: ConfigureServices, ConfigureApplication, OnStartAsync (creates scope), OnRunningAsync, OnShutdownAsync
- **ConfigureServices**: Register `GardenerVoidOrchestratorWorker` and `GardenerScenarioLifecycleWorker` as hosted services
- **OnRunningAsync**: Register the `guardian` seed type via `ISeedClient.RegisterSeedTypeAsync` if it does not already exist. Use the `SeedTypeCode` configuration property, not a hardcoded value. Load or create default deployment phase configuration from `gardener-phase-config` store.

### Step 4: Fill In Internal Models

#### 4a. `plugins/lib-gardener/GardenerServiceModels.cs` (generated template -> fill in)

Internal storage models (not API-facing):

- **`VoidInstanceModel`**: VoidInstanceId (Guid), SeedId (Guid), AccountId (Guid), SessionId (Guid), CreatedAt (DateTimeOffset), Position (Vec3Model), Velocity (Vec3Model), ActivePoiIds (List\<Guid\>), Phase (DeploymentPhase), ScenarioHistory (List\<Guid\> -- recently visited scenario template IDs), DriftMetrics (DriftMetricsModel)
- **`Vec3Model`**: X (float), Y (float), Z (float)
- **`DriftMetricsModel`**: TotalDistance (float), DirectionalBiasX (float), DirectionalBiasY (float), DirectionalBiasZ (float), HesitationCount (int), EngagementPattern (string?)
- **`PoiModel`**: PoiId (Guid), VoidInstanceId (Guid), Position (Vec3Model), PoiType (PoiType enum), ScenarioTemplateId (Guid), VisualHint (string), AudioHint (string?), IntensityRamp (float), TriggerMode (TriggerMode enum), TriggerRadius (float), SpawnedAt (DateTimeOffset), ExpiresAt (DateTimeOffset?), Status (PoiStatus enum)
- **`ScenarioTemplateModel`**: All fields from the ScenarioTemplate planning doc entity, using proper C# types. Prerequisites as ScenarioPrerequisitesModel, Chaining as ScenarioChainingModel, Multiplayer as ScenarioMultiplayerModel, Content as ScenarioContentModel
- **`ScenarioPrerequisitesModel`**: RequiredDomains (Dictionary\<string, float\>?), RequiredScenarios (List\<string\>?), ExcludedScenarios (List\<string\>?)
- **`ScenarioChainingModel`**: LeadsTo (List\<string\>?), ChainProbabilities (Dictionary\<string, float\>?), MaxChainDepth (int)
- **`ScenarioMultiplayerModel`**: MinPlayers (int), MaxPlayers (int), MatchmakingQueueCode (string?), BondPreferred (bool)
- **`ScenarioContentModel`**: BehaviorDocumentId (string?), SceneDocumentId (Guid?), RealmId (Guid?), LocationCode (string?)
- **`ScenarioInstanceModel`**: ScenarioInstanceId (Guid), ScenarioTemplateId (Guid), GameSessionId (Guid), Participants (List\<ScenarioParticipantModel\>), ConnectivityMode (ConnectivityMode enum), Status (ScenarioStatus enum), CreatedAt (DateTimeOffset), CompletedAt (DateTimeOffset?), GrowthAwarded (Dictionary\<string, float\>?), ChainedFrom (Guid?), ChainDepth (int), LastActivityAt (DateTimeOffset)
- **`ScenarioParticipantModel`**: SeedId (Guid), AccountId (Guid), SessionId (Guid), JoinedAt (DateTimeOffset), Role (string?)
- **`ScenarioHistoryModel`**: ScenarioInstanceId (Guid), ScenarioTemplateId (Guid), AccountId (Guid), SeedId (Guid), CompletedAt (DateTimeOffset), Status (ScenarioStatus enum), GrowthAwarded (Dictionary\<string, float\>?), DurationSeconds (float)
- **`DeploymentPhaseConfigModel`**: CurrentPhase (DeploymentPhase enum), MaxConcurrentScenariosGlobal (int), PersistentEntryEnabled (bool), VoidMinigamesEnabled (bool), UpdatedAt (DateTimeOffset)
- **`ScenarioScore`**: ScenarioTemplateId (Guid), TotalScore (float), AffinityScore (float), DiversityScore (float), NarrativeScore (float), RandomScore (float) -- used internally by the scoring algorithm

All models use proper types per T25 (enums, Guids, DateTimeOffset). Nullable for optional fields per T26.

### Step 5: Create Event Handlers

#### 5a. `plugins/lib-gardener/GardenerServiceEvents.cs` (manual - not auto-generated)

Partial class of GardenerService:

- `RegisterEventConsumers(IEventConsumer eventConsumer)` - registers handlers for all consumed events:
  - `seed.phase.changed`
  - `seed.growth.updated`
  - `seed.bond.formed`
  - `seed.activated`
  - `game-session.ended`

**Handler implementations**:

- `HandleSeedPhaseChangedAsync(SeedPhaseChangedEvent evt)`:
  1. Find void instance for the seed's owner (if one exists in Redis)
  2. Update the void instance's cached growth phase
  3. Re-evaluate scenario offerings on next void tick (mark instance as "needs re-evaluation")

- `HandleSeedGrowthUpdatedAsync(SeedGrowthUpdatedEvent evt)`:
  1. Find void instance for the seed's owner (if one exists)
  2. Mark void instance for re-evaluation on next tick (don't recompute immediately -- batching is important for rapid growth events)

- `HandleSeedBondFormedAsync(SeedBondFormedEvent evt)`:
  1. Find void instances for both bond participants (if any exist)
  2. If `BondSharedVoidEnabled` config is true, merge void instances into a shared void
  3. Adjust POI scoring to prioritize `BondPreferred` scenarios

- `HandleSeedActivatedAsync(SeedActivatedEvent evt)`:
  1. If a void instance exists for this seed's owner, update the active seed reference
  2. Mark for re-evaluation (new seed may have different growth profile)

- `HandleGameSessionEndedAsync(GameSessionEndedEvent evt)`:
  1. Find any scenario instance backed by this game session
  2. If the scenario is still Active, mark it as Abandoned (the game session was terminated externally)
  3. Award partial growth if applicable
  4. Clean up scenario instance from Redis
  5. Write scenario history to MySQL

### Step 6: Create Background Worker Services

#### 6a. `plugins/lib-gardener/Services/GardenerVoidOrchestratorWorker.cs` (manual)

A `BackgroundService` / `IHostedService` that runs the scenario selection algorithm for all active void instances at a configurable interval.

**Loop**:
1. Wait `VoidTickIntervalMs` between cycles
2. Load all active void instance keys from Redis (use `ICacheableStateStore` set operations if available, otherwise key pattern scan)
3. For each void instance:
   a. Load void state
   b. Expire any POIs past their TTL, publish `gardener.poi.expired` events
   c. If the instance needs re-evaluation OR has fewer than `MaxActivePoisPerVoid` active POIs:
      - Run scenario selection algorithm
      - Spawn new POIs
      - Publish `gardener.poi.spawned` events
      - Push POI updates to client via `IClientEventPublisher`
   d. Check proximity triggers: if player position is within `TriggerRadius` of any proximity-triggered POI, auto-trigger it

**Scenario selection algorithm** (private helper):
1. Query eligible templates from `gardener-scenario-templates` store:
   - Filter by deployment phase (only templates whose `allowedPhases` includes current phase)
   - Filter by seed growth phase (template's `minGrowthPhase` <= seed's current phase)
   - Exclude templates on cooldown (check `gardener-scenario-history` for recent completions by this account)
   - Exclude templates at max capacity (count active instances in `gardener-scenario-instances`)
   - Exclude prerequisite failures (check required scenarios against history, required domains against seed growth)
2. Score each eligible template using weighted formula:
   - `AffinityScore` = dot product of template's `domainWeights` with seed's growth domain profile (normalized)
   - `DiversityScore` = inverse of recent category frequency in void instance's `ScenarioHistory`
   - `NarrativeScore` = correlation between drift metrics pattern and template category
   - `RandomScore` = random value for discovery
   - `TotalScore = AffinityWeight * AffinityScore + DiversityWeight * DiversityScore + NarrativeWeight * NarrativeScore + RandomWeight * RandomScore`
   - If player is bonded and template has `BondPreferred = true`, multiply total by `BondScenarioPriority` config
3. Select top N templates (where N = `MaxActivePoisPerVoid` - current active POI count)
4. For each selected template:
   - Generate POI position (random within `PoiSpawnRadiusMin` to `PoiSpawnRadiusMax` from player, respecting `MinPoiSpacing`)
   - Choose `PoiType` based on template category
   - Choose `TriggerMode` based on player behavior patterns (players who ignore proximity triggers get more Prompted/Forced)
   - Set `ExpiresAt` = now + `PoiDefaultTtlMinutes`
   - Save POI to `gardener-pois` store

#### 6b. `plugins/lib-gardener/Services/GardenerScenarioLifecycleWorker.cs` (manual)

A `BackgroundService` that manages scenario instance lifecycle.

**Loop** (every `ScenarioLifecycleWorkerIntervalSeconds`):
1. Scan active scenario instances from Redis
2. For each active scenario:
   a. Check for timeout: if `(now - CreatedAt) > ScenarioTimeoutMinutes`, force complete
   b. Check for abandonment: if `(now - LastActivityAt) > AbandonDetectionMinutes`, mark abandoned
3. For abandoned scenarios:
   - Calculate partial growth based on time spent (proportional to template's `domainWeights` and `GrowthAwardMultiplier`)
   - Publish `seed.growth.contributed` events to lib-seed
   - Publish `gardener.scenario.abandoned` event
   - Clean up Game Session via `IGameSessionClient`
   - Move scenario record to `gardener-scenario-history` (MySQL)
   - Remove from `gardener-scenario-instances` (Redis)

### Step 7: Implement Service Business Logic

#### 7a. `plugins/lib-gardener/GardenerService.cs` (generated template -> fill in)

Partial class with `[BannouService("gardener", typeof(IGardenerService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]`:

**Constructor dependencies**:
- `IStateStoreFactory` - for all state stores
- `IMessageBus` - event publishing
- `IDistributedLockProvider` - concurrent modification safety
- `ILogger<GardenerService>` - structured logging
- `GardenerServiceConfiguration` - typed config
- `IEventConsumer` - event handler registration
- `ISeedClient` - seed CRUD, growth queries, capability manifests, bonds (L2 hard dependency)
- `IGameSessionClient` - create/close game sessions for scenarios (L2 hard dependency)
- `IServiceProvider` - for optional L4 soft dependencies

**Soft dependencies** (resolved at runtime via `IServiceProvider`, null-checked):
- `IMatchmakingClient` - submit matchmaking tickets for group scenarios (L4)
- `IPuppetmasterClient` - delegate NPC orchestration within scenarios (L4)
- `IAnalyticsClient` - query milestone data for scenario selection (L4)

**Store initialization** (in constructor):
- `_voidStore` = GetStore\<VoidInstanceModel\>(StateStoreDefinitions.GardenerVoidInstances)
- `_poiStore` = GetStore\<PoiModel\>(StateStoreDefinitions.GardenerPois)
- `_templateStore` = GetStore\<ScenarioTemplateModel\>(StateStoreDefinitions.GardenerScenarioTemplates)
- `_scenarioStore` = GetStore\<ScenarioInstanceModel\>(StateStoreDefinitions.GardenerScenarioInstances)
- `_historyStore` = GetStore\<ScenarioHistoryModel\>(StateStoreDefinitions.GardenerScenarioHistory)
- `_phaseStore` = GetStore\<DeploymentPhaseConfigModel\>(StateStoreDefinitions.GardenerPhaseConfig)
- `_lockProvider` for distributed locks

**Key method implementations** (all follow T7 error handling, T8 return pattern):

| Method | Key Logic |
|--------|-----------|
| `EnterVoidAsync` | Query `ISeedClient.GetSeedsByOwnerAsync` for active guardian seed. Create VoidInstanceModel in Redis. Initialize drift metrics. Publish `gardener.void.entered` event. Return void state with empty POIs (first tick spawns them). |
| `GetVoidStateAsync` | Load void instance from Redis by accountId key. Load active POIs. Return composite response. |
| `UpdatePositionAsync` | Lock void instance, update position/velocity, accumulate drift metrics (total distance, directional bias, hesitation detection). Check proximity triggers against active POIs. Return acknowledgment + any triggered POIs. |
| `LeaveVoidAsync` | Lock void instance, clean up all associated POIs from Redis, calculate session duration, delete void instance, publish `gardener.void.left` event. |
| `ListPoisAsync` | Load void instance, load all POIs for this void instance from Redis, return as list. |
| `InteractWithPoiAsync` | Load POI, validate status is Active, determine interaction result based on TriggerMode. For Prompted POIs: return `scenario_prompt` with template info. For Proximity/Interaction: return `scenario_enter`. Update POI status to Entered. Publish `gardener.poi.entered` event. |
| `DeclinePoiAsync` | Load POI, validate status, set status to Declined. Add template to void instance's ScenarioHistory (for diversity scoring). Publish `gardener.poi.declined` event. |
| `EnterScenarioAsync` | Validate player has a void instance (or is chaining). Load template. Validate prerequisites against seed growth. Create Game Session via `IGameSessionClient`. Create ScenarioInstanceModel in Redis. Destroy void instance (player leaves void). Publish `gardener.scenario.started` event. If Puppetmaster available, notify it of the scenario. |
| `GetScenarioStateAsync` | Load active scenario instance from Redis by accountId key. |
| `CompleteScenarioAsync` | Lock scenario instance. Calculate growth awards from template's `domainWeights` * `GrowthAwardMultiplier`. Publish `seed.growth.contributed` events to lib-seed for each domain. Mark scenario Completed. Close Game Session. Move to history store. Publish `gardener.scenario.completed` event. Return growth summary and `returnToVoid = true`. |
| `AbandonScenarioAsync` | Lock scenario instance. Calculate partial growth (time-proportional). Publish partial growth via `seed.growth.contributed`. Mark Abandoned. Close Game Session. Move to history. Publish `gardener.scenario.abandoned`. |
| `ChainScenarioAsync` | Validate current scenario is Active. Load target template. Validate chaining rules (current template's `leadsTo` includes target code, chain depth < maxChainDepth). Complete current scenario (with growth). Create new scenario instance with `ChainedFrom` reference and incremented `ChainDepth`. Publish `gardener.scenario.chained`. |
| `CreateTemplateAsync` | Validate code uniqueness. Save to MySQL store. Publish lifecycle created event. |
| `GetTemplateAsync` | Load from MySQL by ID. 404 if not found. |
| `GetTemplateByCodeAsync` | JSON query by code field. 404 if not found. |
| `ListTemplatesAsync` | Paged JSON query with optional filters (category, connectivity mode, deployment phase, status). |
| `UpdateTemplateAsync` | Lock, load, validate, update non-null fields. Publish lifecycle updated event. |
| `DeprecateTemplateAsync` | Load template, set status to Deprecated. Publish lifecycle updated event. |
| `GetPhaseConfigAsync` | Load from MySQL. If not found, create default from config and return. |
| `UpdatePhaseConfigAsync` | Lock, load, update non-null fields. If phase changed, publish `gardener.phase.changed` event. |
| `GetPhaseMetricsAsync` | Count active void instances (Redis key scan). Count active scenario instances (Redis key scan). Compute capacity utilization. |
| `EnterScenarioTogetherAsync` | Load bond via `ISeedClient.GetBondAsync`. Validate both participants have active void instances. Validate template supports multiplayer and `BondPreferred`. Create shared Game Session. Create ScenarioInstance with both participants. Destroy both void instances. Publish `gardener.bond.entered-together`. |
| `GetSharedVoidStateAsync` | Load bond via `ISeedClient.GetBondAsync`. Load void instances for both participants. Load shared POIs. Return merged state. |

**State key patterns**:
- Void instance: `void:{accountId}`
- POI: `poi:{voidInstanceId}:{poiId}`
- Scenario instance: `scenario:{accountId}`
- Scenario template: `template:{scenarioTemplateId}`
- Scenario template by code: `template-code:{code}`
- Scenario history: `history:{scenarioInstanceId}`
- Phase config: `phase:config` (singleton)
- Locks: `gardener:lock:void:{accountId}`, `gardener:lock:scenario:{accountId}`, `gardener:lock:template:{scenarioTemplateId}`, `gardener:lock:phase`

### Step 8: Build and Verify

```bash
dotnet build
```

Verify no compilation errors, all generated code resolves, no CS1591 warnings.

### Step 9: Unit Tests

The test project and template `GardenerServiceTests.cs` were auto-created in Step 2. Fill in with comprehensive tests:

#### 9a. `plugins/lib-gardener.tests/GardenerServiceTests.cs` (generated template -> fill in)

Following testing patterns from TESTING-PATTERNS.md:

**Constructor validation**:
- `GardenerService_ConstructorIsValid()` via `ServiceConstructorValidator`

**Void management tests** (capture pattern for state saves and event publishing):
- `EnterVoid_ValidAccount_CreatesVoidInstanceAndPublishesEvent`
- `EnterVoid_NoActiveSeed_ReturnsNotFound`
- `EnterVoid_AlreadyInVoid_ReturnsConflict`
- `GetVoidState_Exists_ReturnsStateWithPois`
- `GetVoidState_NotInVoid_ReturnsNotFound`
- `UpdatePosition_ValidUpdate_UpdatesDriftMetrics`
- `UpdatePosition_ProximityTrigger_ReturnsTriggeredPois`
- `LeaveVoid_InVoid_CleansUpAndPublishesEvent`
- `LeaveVoid_NotInVoid_ReturnsNotFound`

**POI interaction tests**:
- `ListPois_ReturnsActivePois`
- `InteractWithPoi_PromptedPoi_ReturnsScenarioPrompt`
- `InteractWithPoi_ProximityPoi_ReturnsScenarioEnter`
- `InteractWithPoi_ExpiredPoi_ReturnsBadRequest`
- `DeclinePoi_ActivePoi_SetsDeclinedStatus`

**Scenario lifecycle tests**:
- `EnterScenario_ValidTemplate_CreatesInstanceAndGameSession`
- `EnterScenario_PrerequisiteNotMet_ReturnsBadRequest`
- `EnterScenario_CapacityFull_ReturnsConflict`
- `GetScenarioState_Active_ReturnsState`
- `CompleteScenario_AwardsGrowthAndPublishesEvents`
- `CompleteScenario_CalculatesCorrectGrowthPerDomain`
- `AbandonScenario_AwardsPartialGrowthAndCleanup`
- `ChainScenario_ValidChain_CreatesNewInstanceWithChainRef`
- `ChainScenario_NotInLeadsTo_ReturnsBadRequest`
- `ChainScenario_ExceedsMaxChainDepth_ReturnsBadRequest`

**Template management tests**:
- `CreateTemplate_ValidRequest_SavesAndPublishesEvent`
- `CreateTemplate_DuplicateCode_ReturnsConflict`
- `GetTemplate_Exists_ReturnsFullTemplate`
- `GetTemplate_NotFound_ReturnsNotFound`
- `GetTemplateByCode_Exists_ReturnsTemplate`
- `ListTemplates_WithCategoryFilter_ReturnsFiltered`
- `ListTemplates_Paginated_ReturnsCorrectPage`
- `UpdateTemplate_PartialUpdate_OnlyUpdatesProvidedFields`
- `DeprecateTemplate_Active_SetsDeprecatedStatus`

**Deployment phase tests**:
- `GetPhaseConfig_NoConfig_CreatesDefaultFromConfig`
- `GetPhaseConfig_Exists_ReturnsStored`
- `UpdatePhaseConfig_ChangePhase_PublishesPhaseChangedEvent`
- `GetPhaseMetrics_ReturnsCurrentCounts`

**Bond scenario tests**:
- `EnterScenarioTogether_ValidBond_CreatesSingleSharedInstance`
- `EnterScenarioTogether_OneNotInVoid_ReturnsBadRequest`
- `EnterScenarioTogether_TemplateNotMultiplayer_ReturnsBadRequest`
- `GetSharedVoidState_BondedAndInVoid_ReturnsMergedState`

**Scenario selection algorithm tests** (test the scoring logic as a separate helper):
- `ScoreTemplate_HighAffinityMatch_ScoresHighOnAffinity`
- `ScoreTemplate_RecentCategory_ScoresLowOnDiversity`
- `ScoreTemplate_BondPreferred_AppliesBondBoost`
- `ScoreTemplate_OnCooldown_ExcludedFromEligible`
- `ScoreTemplate_PhaseRestriction_ExcludedFromEligible`

**Event handler tests**:
- `HandleSeedPhaseChanged_PlayerInVoid_MarksForReEvaluation`
- `HandleSeedBondFormed_BothInVoid_EnablesSharedVoid`
- `HandleSeedActivated_PlayerInVoid_UpdatesActiveReference`
- `HandleGameSessionEnded_ActiveScenario_MarksAbandoned`

All tests use the capture pattern (Callback on mock setups) to verify saved state and published events, not just Verify calls.

---

## Files Created/Modified Summary

| File | Action |
|------|--------|
| `schemas/gardener-api.yaml` | Create (from planning doc draft, fix x-permissions format) |
| `schemas/gardener-events.yaml` | Create (lifecycle + subscriptions + 12 custom events) |
| `schemas/gardener-configuration.yaml` | Create (21 configuration properties) |
| `schemas/state-stores.yaml` | Modify (add 7 gardener stores) |
| `plugins/lib-gardener/GardenerService.cs` | Fill in (auto-generated template) |
| `plugins/lib-gardener/GardenerServiceModels.cs` | Fill in (auto-generated template) |
| `plugins/lib-gardener/GardenerServicePlugin.cs` | Fill in (auto-generated template) |
| `plugins/lib-gardener/GardenerServiceEvents.cs` | Create (NOT auto-generated -- partial class with 5 event handlers) |
| `plugins/lib-gardener/Services/GardenerVoidOrchestratorWorker.cs` | Create (background service for void tick loop and scenario selection) |
| `plugins/lib-gardener/Services/GardenerScenarioLifecycleWorker.cs` | Create (background service for scenario timeout/abandonment detection) |
| `plugins/lib-gardener.tests/GardenerServiceTests.cs` | Fill in (auto-generated template) |
| `plugins/lib-gardener/lib-gardener.csproj` | Auto-generated by `generate-service.sh` |
| `plugins/lib-gardener/AssemblyInfo.cs` | Auto-generated by `generate-service.sh` |
| `plugins/lib-gardener/Generated/*` | Auto-generated (do not edit) |
| `bannou-service/Generated/*` | Auto-generated (updated) |
| `bannou-service.sln` | Auto-updated by `generate-service.sh` |
| `plugins/lib-gardener.tests/*` | Auto-generated test project |

---

## Dependency Summary

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Layer | Usage |
|------------|-------|-------|
| `IStateStoreFactory` | L0 | All state stores (Redis void/poi/scenario, MySQL templates/history/phase) |
| `IDistributedLockProvider` | L0 | Concurrent modification safety for void, scenario, template, and phase mutations |
| `IMessageBus` | L0 | Event publishing for all 12+ custom events |
| `IEventConsumer` | L0 | Event subscription registration for 5 consumed events |
| `ISeedClient` | L2 | Seed CRUD, growth queries, capability manifests, bond queries |
| `IGameSessionClient` | L2 | Create/close game sessions backing scenario instances |

### Soft Dependencies (runtime resolution -- graceful degradation)

| Dependency | Layer | Usage | Behavior When Missing |
|------------|-------|-------|-----------------------|
| `IMatchmakingClient` | L4 | Submit matchmaking tickets for group scenarios | Group scenarios disabled; solo-only |
| `IPuppetmasterClient` | L4 | Delegate NPC orchestration for scenarios | Scenarios run without NPC orchestration |
| `IAnalyticsClient` | L4 | Query milestone data for scenario selection | Milestone-based scoring component zeroed |

---

## Integration Points

### Gardener -> Seed (L2, hard dependency)

| Interaction | API Call / Event |
|-------------|-----------------|
| Find player's active seed | `ISeedClient.GetSeedsByOwnerAsync` with `ownerType = "account"`, `seedTypeCode = config.SeedTypeCode` |
| Create guardian seed on first void entry | `ISeedClient.CreateSeedAsync` with `ownerType = "account"`, `seedTypeCode = config.SeedTypeCode` |
| Query growth for scenario scoring | `ISeedClient.GetGrowthAsync` |
| Query capability manifest | `ISeedClient.GetCapabilityManifestAsync` |
| Query bond state | `ISeedClient.GetBondForSeedAsync`, `ISeedClient.GetBondAsync` |
| Award growth on scenario completion | Publish `seed.growth.contributed` event via `IMessageBus` (event-driven, follows Resource reference tracking pattern) |
| Register guardian seed type on startup | `ISeedClient.RegisterSeedTypeAsync` in `OnRunningAsync` |

### Gardener -> Game Session (L2, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Create scenario-backed session | `IGameSessionClient.CreateGameSessionAsync` (matchmade type with reservation token) |
| Close session on scenario end | `IGameSessionClient.EndGameSessionAsync` |

### Gardener -> Matchmaking (L4, soft dependency)

| Interaction | API Call |
|-------------|----------|
| Queue group scenario | `IMatchmakingClient.CreateTicketAsync` for templates with `MinPlayers > 1` |

### Gardener -> Puppetmaster (L4, soft dependency)

| Interaction | API Call |
|-------------|----------|
| Notify scenario start | Service-to-service call to notify Puppetmaster of the scenario template being instantiated |

### Seed -> Gardener (consumed events)

| Event | Action |
|-------|--------|
| `seed.phase.changed` | Re-evaluate scenario offerings for the player |
| `seed.growth.updated` | Mark void instance for scoring re-evaluation |
| `seed.bond.formed` | Enable shared void and bond-priority scenarios |
| `seed.activated` | Update active seed reference in void instance |

### Game Session -> Gardener (consumed events)

| Event | Action |
|-------|--------|
| `game-session.ended` | Clean up any scenario instance backed by this session |

---

## Open Design Questions

These are questions identified during plan extraction that require human decision before or during implementation:

1. **Void position protocol optimization**: The `UpdatePosition` endpoint will be called at high frequency (potentially every frame). Starting with POST JSON per the planning doc recommendation, but this may need to move to a binary protocol through Connect for performance. Monitor latency before optimizing.

2. **Analytics milestone integration**: The planning doc mentions consuming `analytics.milestone.reached` events for scenario selection. This is not included in the current x-event-subscriptions because Analytics is also L4 and the event schema may not exist yet. Add this subscription when Analytics publishes the event. For now, the `NarrativeWeight` scoring component handles this gap.

3. **Growth award calculation formula**: The planning doc says growth is awarded based on template `domainWeights` and `GrowthAwardMultiplier`, but the exact formula (linear, time-proportional, performance-based) is not specified. Initial implementation: `amount = domainWeight * GrowthAwardMultiplier * (scenarioDurationMinutes / estimatedDurationMinutes)` capped at 1.5x the base. This means faster completion gets less growth (time-proportional), and scenarios running longer than estimated get a 50% bonus cap.

4. **POI spawn position generation**: The algorithm for generating positions within the spawn radius while respecting `MinPoiSpacing` needs a concrete implementation. Initial approach: random angle + random distance within radius, reject if within `MinPoiSpacing` of existing POI, retry up to 10 times. If all retries fail, skip that POI spawn (the next tick will try again).

5. **Shared void instance merging**: When `BondSharedVoidEnabled` is true and both bonded players are in the void, how are POIs merged? Initial approach: the higher-growth-phase player's void instance becomes the "primary," the other player's POIs are migrated into it, and both players see all POIs. New POIs are scored using the combined growth profiles.

6. **Client event pushing for POI updates**: The void orchestrator needs to push POI spawn/despawn/trigger updates to the client in real-time. This requires a `gardener-client-events.yaml` schema (not included in this initial plan). Create this schema as a follow-up once the core service is working.

---

## Verification

1. `dotnet build` -- compiles without errors or warnings
2. `dotnet test plugins/lib-gardener.tests/` -- all unit tests pass
3. Verify no CS1591 warnings (all schema properties have descriptions)
4. Verify `StateStoreDefinitions.cs` contains all 7 Gardener constants after generation
5. Verify `GardenerClient.cs` generated in `bannou-service/Generated/Clients/` for other services to call Gardener
6. Verify event subscription handlers generated in `GardenerEventsController.cs` for all 5 consumed events
7. Verify the `IGardenerService` interface has methods for all 23 endpoints
8. Manual verification: confirm `ISeedClient` is available via constructor injection (L2 loads before L4 per plugin loader)
