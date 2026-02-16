# Workshop Plugin Deep Dive

> **Plugin**: lib-workshop (not yet created)
> **Schema**: `schemas/workshop-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: workshop-blueprint (MySQL), workshop-task (MySQL), workshop-worker (MySQL), workshop-rate-segment (MySQL), workshop-cache (Redis), workshop-lock (Redis) — all planned
> **Layer**: GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.

---

## Overview

Time-based automated production service (L4 GameFeatures) for continuous background item generation: assign workers to blueprints, consume materials from source inventories, place outputs in destination inventories over game time. Uses lazy evaluation with piecewise rate segments for accurate production tracking across worker count changes, and a background materialization worker with fair per-entity scheduling. Game-agnostic: blueprint structures, production categories, worker types, and proficiency domains are all opaque configuration defined per game at deployment time through blueprint seeding. Internal-only, never internet-facing.

---

## Core Concepts

Bannou has interactive crafting (lib-craft's Contract-backed step-by-step sessions), but no mechanism for continuous automated production. An NPC blacksmith who forges swords all day shouldn't need a GOAP action for every single sword — they should have a running production line that produces swords at their skill level, consuming iron and leather from a supply chest and filling a shop inventory. A player who sets up a mining operation shouldn't need to click each ore extraction — they assign workers to the mine and collect output periodically. An idle game's "auto-factory" shouldn't need per-item interaction. Workshop provides the "set it, check it later" production paradigm.

**Why not actors**: Actors have cognitive overhead: perception queues, ABML bytecode execution, GOAP planning, variable provider resolution, behavior documents. An automation task is deterministic: "produce X items at rate Y consuming materials Z." There's no perception, no decision-making, no emergent behavior. The 100ms actor tick is orders of magnitude too frequent for a task that produces one item every few game-minutes. Using actors for automation would dilute cognitive processing resources meant for NPC brains. Workshop uses a background worker with fair per-entity scheduling — simpler, cheaper, equally isolated.

**Why not extend lib-craft**: lib-craft is an interactive crafting engine with step-by-step progression, quality skill checks, Contract-backed sessions, and proficiency tracking. Workshop is a passive production engine with continuous output, lazy evaluation, and worker-based rate scaling. They compose well (Workshop can reference Craft recipes for input/output definitions) but serve fundamentally different interaction patterns. Craft answers "the player/NPC is actively crafting this item right now." Workshop answers "this production line has been running for 3 game-days, how much was produced?"

### Production Blueprint

Blueprints support two production paradigms:

| Paradigm | Input Source | Output Source | Example |
|----------|-------------|---------------|---------|
| **Recipe-referenced** | Derived from lib-craft recipe `inputs` | Derived from lib-craft recipe `outputs` | "Run the 'forge_iron_sword' recipe continuously" |
| **Custom** | Defined directly on the blueprint | Defined directly on the blueprint | "Mine: consumes nothing (time only), produces iron_ore" |

A blueprint defines a repeatable production transformation. It specifies what goes in, what comes out, and how long each unit takes.

```
ProductionBlueprint:
  blueprintId: Guid
  gameServiceId: Guid
  code: string                         # Unique within game service (e.g., "forge_iron_sword", "mine_iron", "grow_wheat")

  # Classification
  category: string                     # "crafting", "mining", "farming", "manufacturing", "training", etc.
  tags: [string]                       # For filtering (e.g., ["blacksmithing", "weapons", "iron"])

  # Input specification (consumed per unit of production)
  inputs:
    - itemTemplateCode: string         # Required material
      quantityPerUnit: decimal         # Amount consumed per unit produced
  # Note: empty inputs = time-only production (mining, training, passive generation)

  # Output specification (produced per unit)
  outputs:
    - itemTemplateCode: string         # Produced item
      quantityPerUnit: decimal         # Amount produced per unit
      qualitySource: string            # "fixed", "worker_average", "blueprint_default"
      fixedQuality: decimal?           # Quality value when qualitySource is "fixed"

  # Recipe reference (alternative to explicit inputs/outputs)
  recipeCode: string?                  # If set, inputs/outputs derived from lib-craft recipe
  recipeGameServiceId: Guid?           # Game service owning the recipe (for cross-game-service references)

  # Time
  baseGameSecondsPerUnit: int          # Game-time seconds per unit at base rate (1 worker, no bonuses)

  # Worker constraints
  minWorkers: int                      # Minimum workers to operate (default: 1, 0 = autonomous)
  maxWorkers: int                      # Maximum workers (default: 0 = unlimited)
  workerTypes: [string]?               # Valid worker entity types (null = any)

  # Optional constraints
  requiresStationType: string?         # Must be near a station of this type (via lib-craft station registry)
  requiresLocationId: Guid?            # Must be at a specific location

  # Metadata
  isActive: bool
  isDeprecated: bool
```

**Key design decisions**:

1. **Recipe-referenced OR custom inputs/outputs**: When `recipeCode` is set, Workshop queries lib-craft for the recipe's inputs and outputs at task creation time and snapshots them onto the task. This avoids runtime coupling to lib-craft during production. When `recipeCode` is null, explicit `inputs` and `outputs` on the blueprint define the transformation directly.

2. **Base rate is per-unit**: `baseGameSecondsPerUnit` defines how many game-seconds one unit takes with exactly one worker and no bonuses. Multiple workers divide this time: 2 workers = half the time per unit. This is the rate the piecewise function operates on.

3. **Quality is simplified**: Interactive crafting (lib-craft) has a rich quality formula with material quality, proficiency, tool quality, and step bonuses. Automated production uses a simplified quality model: fixed quality per blueprint, or average worker proficiency as quality. The quality system is a configuration choice, not a formula.

4. **MinWorkers of 0 means autonomous**: A mine or farm that produces output with no assigned workers (e.g., passive resource regeneration, autogain-style item production). The task runs at base rate with 0 workers when `minWorkers` is 0.

5. **Blueprint categories and tags are opaque strings**: "crafting", "mining", "farming" are conventions. A game might add "enchanting_automation", "ritual_channeling", "essence_distillation" -- all equally valid.

### Production Task

A task is a running instance of a blueprint, bound to specific inventories and workers.

```
ProductionTask:
  taskId: Guid
  blueprintId: Guid
  realmId: Guid                        # For game-time lookup from lib-worldstate

  # Ownership
  ownerType: string                    # "character", "npc", "faction", "location", "account"
  ownerId: Guid

  # Inventories
  sourceInventoryId: Guid              # Consume materials from here
  destinationInventoryId: Guid         # Place outputs here

  # Production target
  targetQuantity: int?                 # null = indefinite (run until paused or out of materials)

  # Current state (lazy-evaluated)
  lastProcessedGameTime: long          # Game-seconds-since-epoch at last materialization
  fractionalProgress: decimal          # Partial unit progress (0.0-1.0), carries across materializations
  totalProduced: long                  # Lifetime count of units materialized
  totalConsumed: object                # Lifetime count per input item consumed

  # Rate (derived from workers)
  currentEffectiveRate: decimal        # Units per game-second (recomputed on worker changes)

  # Status
  status: string                       # See status table below
  statusReason: string?                # Human-readable reason for current status
  pausedAtGameTime: long?              # Game-time when task was paused (null if running)
  createdAtGameTime: long              # Game-time when task was created
  completedAtGameTime: long?           # Game-time when task completed (reached target)

  # Snapshot (from blueprint or recipe at creation time)
  snapshotInputs: [...]                # Inputs locked at task creation
  snapshotOutputs: [...]               # Outputs locked at task creation
  snapshotBaseGameSecondsPerUnit: int  # Base time locked at task creation
```

**Task statuses**:

| Status | Meaning | Transition From | Transition To |
|--------|---------|----------------|---------------|
| `running` | Actively producing | `created`, `paused:manual`, `paused:no_materials`, `paused:no_space` | `paused:*`, `completed`, `cancelled` |
| `paused:manual` | Manually paused by owner | `running` | `running`, `cancelled` |
| `paused:no_materials` | Source inventory lacks required materials | `running` | `running` (auto-resume when materials available on next check) |
| `paused:no_space` | Destination inventory is full | `running` | `running` (auto-resume when space available on next check) |
| `paused:no_workers` | Below minimum worker count | `running` | `running` (auto-resume when workers assigned) |
| `completed` | Reached target quantity | `running` | (terminal) |
| `cancelled` | Cancelled by owner or cleanup | Any non-terminal | (terminal) |

### Workers

Workers are entity references assigned to a task. Each worker contributes to the production rate.

```
WorkerAssignment:
  taskId: Guid
  workerId: Guid
  workerType: string                   # "character", "npc", "actor"
  assignedAtGameTime: long             # Game-time when worker was assigned
  rateContribution: decimal            # This worker's rate contribution (default: 1.0)
  proficiencyMultiplier: decimal       # Multiplier from worker's skill level (default: 1.0)
```

**Rate computation**:

```
effectiveRate = sum(worker.rateContribution * worker.proficiencyMultiplier)
              / blueprint.baseGameSecondsPerUnit

Example:
  baseGameSecondsPerUnit = 3600 (1 game-hour per unit)
  Worker A: rateContribution=1.0, proficiencyMultiplier=1.0 → contributes 1.0
  Worker B: rateContribution=1.0, proficiencyMultiplier=1.5 → contributes 1.5
  Total contribution = 2.5
  effectiveRate = 2.5 / 3600 = 0.000694 units per game-second
  At 24:1 ratio: ~60 units per real-hour (each unit takes ~1 real-minute)
```

**Proficiency multiplier**: When a worker is assigned, Workshop optionally queries lib-seed (if the blueprint has a proficiency domain configured) for the worker's proficiency seed growth depth, and computes a multiplier. A master blacksmith produces faster than an apprentice. If lib-seed or proficiency data is unavailable, the multiplier defaults to 1.0.

### Rate Segments

Every rate change (worker added, worker removed, manual rate adjustment) creates a new rate segment. These segments enable accurate lazy evaluation of production across rate changes.

```
RateSegment:
  taskId: Guid
  segmentIndex: int                    # Monotonically increasing per task
  startGameTime: long                  # Game-seconds-since-epoch when this rate became effective
  effectiveRate: decimal               # Units per game-second during this segment
  workerCount: int                     # Worker count at start of segment (for display)
```

Rate segments are append-only. When a worker joins or leaves:
1. Materialize pending production up to current game time (flush lazy state)
2. Record new rate segment with updated rate
3. Update task's `currentEffectiveRate`

### Lazy Evaluation (The Core Algorithm)

Currency's autogain worker is the direct architectural precedent. It runs on a timer, computes elapsed periods, and applies passive generation. Workshop applies the same lazy-evaluation-with-materialization pattern to item production instead of currency generation. The key differences: Workshop supports variable rates (workers join/leave), operates in game-time (via lib-worldstate instead of real-time), and handles material consumption/inventory capacity constraints.

Production is not simulated in real-time. When a task's status is queried or the background worker runs, pending production is computed and materialized.

```
MaterializeProduction(task, currentGameTime):
  gameSecondsElapsed = currentGameTime - task.lastProcessedGameTime
  if gameSecondsElapsed <= 0: return

  # Integrate production over rate segments
  pendingUnits = task.fractionalProgress
  for each segment overlapping [task.lastProcessedGameTime, currentGameTime]:
    segmentGameSeconds = min(segment.end, currentGameTime)
                       - max(segment.start, task.lastProcessedGameTime)
    pendingUnits += segmentGameSeconds * segment.effectiveRate

  wholeUnits = floor(pendingUnits)
  remainingFraction = pendingUnits - wholeUnits

  if wholeUnits == 0:
    task.fractionalProgress = remainingFraction
    task.lastProcessedGameTime = currentGameTime
    return

  # Check material availability (cap by what source inventory has)
  availableMaterials = checkSourceInventory(task.sourceInventoryId, task.snapshotInputs)
  maxFromMaterials = minUnitsProducibleFrom(availableMaterials, task.snapshotInputs)
  # If inputs are empty (time-only production), maxFromMaterials = unlimited

  # Check destination capacity
  destinationCapacity = checkDestinationCapacity(task.destinationInventoryId, task.snapshotOutputs)
  maxFromCapacity = maxUnitsPlaceable(destinationCapacity, task.snapshotOutputs)

  actualUnits = min(wholeUnits, maxFromMaterials, maxFromCapacity)

  if actualUnits == 0:
    # Can't produce anything -- pause with reason
    if maxFromMaterials == 0:
      task.status = "paused:no_materials"
    elif maxFromCapacity == 0:
      task.status = "paused:no_space"
    task.fractionalProgress = remainingFraction
    task.lastProcessedGameTime = currentGameTime
    return

  # Materialize: consume inputs, create outputs
  consumeMaterials(task.sourceInventoryId, task.snapshotInputs, actualUnits)
  createOutputs(task.destinationInventoryId, task.snapshotOutputs, actualUnits, quality)

  task.totalProduced += actualUnits
  task.fractionalProgress = remainingFraction + (wholeUnits - actualUnits)
    # ^ If we couldn't produce all pending units, the excess stays as fractional
    # Wait -- actually if we're paused (no materials/space), we shouldn't
    # accumulate more than we can produce. Cap fractional at 1.0 to prevent
    # unlimited backlog.
  task.fractionalProgress = min(remainingFraction, 1.0)
  task.lastProcessedGameTime = currentGameTime

  # Check target
  if task.targetQuantity != null && task.totalProduced >= task.targetQuantity:
    task.status = "completed"

  # Publish event
  publish("workshop.production.materialized", { taskId, units: actualUnits })
```

**Key behavior**: If a task runs for 10 game-days but the source inventory only had materials for 3 units, only 3 units are produced. The task pauses with `no_materials`. It does NOT accumulate 10 days of "debt" that suddenly materializes when materials are restocked. Fractional progress is capped at 1.0 to prevent backlog accumulation during paused periods.

### Background Materialization Worker

A `BackgroundService` that periodically processes pending production tasks.

```
WorkshopMaterializationWorkerService:
  Runs every MaterializationIntervalSeconds (default: 30 real seconds)

  Algorithm:
  1. Load all tasks with status "running" or "paused:no_materials" or "paused:no_space"
  2. Group by ownerId (fair scheduling)
  3. Round-robin through owners:
     a. For each owner, process up to MaxTasksPerOwnerPerTick tasks
     b. For each task: call MaterializeProduction(task, currentGameTime)
     c. If task was paused:no_materials, check if materials are now available → resume
     d. If task was paused:no_space, check if space is now available → resume
  4. Track processing time per owner for monitoring

  Fair scheduling guarantee:
  - Each owner gets at most MaxTasksPerOwnerPerTick tasks processed per cycle
  - An owner with 100 tasks doesn't block an owner with 1 task
  - Processing order within an owner: oldest task first (FIFO)
```

**Two materialization paths** (same pattern as Currency autogain):

| Path | Trigger | When |
|------|---------|------|
| **Lazy** (on-demand) | `GetTask` or `GetProductionStatus` query | When someone asks "how's this task doing?" |
| **Worker** (background) | `WorkshopMaterializationWorkerService` timer | Every `MaterializationIntervalSeconds` |

Both paths call `MaterializeProduction` and use the same distributed lock to prevent concurrent materialization of the same task.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Blueprints (MySQL), tasks (MySQL), workers (MySQL), rate segments (MySQL), production cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for task materialization, worker assignment, and blueprint mutations |
| lib-messaging (`IMessageBus`) | Publishing production lifecycle events, error event publication |
| lib-worldstate (`IWorldstateClient`) | Querying current game time for a realm, computing elapsed game-time for lazy evaluation. The foundational dependency -- Workshop cannot function without a game clock. (L2) |
| lib-item (`IItemClient`) | Creating output items during materialization. Reading item template metadata for output quality defaults. (L2) |
| lib-inventory (`IInventoryClient`) | Consuming materials from source containers, placing outputs in destination containers, checking container capacity. (L2) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence for blueprint scoping. (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-craft (`ICraftClient`) | Resolving recipe inputs/outputs when blueprint references a `recipeCode`. Looking up recipe proficiency domain for worker proficiency. (L4) | Recipe-referenced blueprints cannot be created. Custom blueprints with explicit inputs/outputs work normally. |
| lib-seed (`ISeedClient`) | Reading worker proficiency seed growth for proficiency multiplier calculation. (L2) | Worker proficiency multiplier defaults to 1.0 when no proficiency domain is configured. |
| lib-location (`ILocationClient`) | Validating location constraint on blueprints that require `requiresLocationId`. (L2) | Location constraints are skipped when `requiresLocationId` is null on the blueprint. |

**Hierarchy note**: lib-seed and lib-location are L2 services. Per SERVICE-HIERARCHY.md, L4 services should use constructor injection for L2 dependencies (they are guaranteed available). The original specification listed these as soft because the features they support (proficiency, location constraints) are optional. However, service availability and feature optionality are different concerns. When implementing, use constructor injection for `ISeedClient` and `ILocationClient`, and gate optional features at the data level instead. See Design Consideration #8.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2, via Variable Provider) | Actor discovers `WorkshopProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates providers for ABML behavior expressions (`${workshop.*}` variables). NPCs query their owned production tasks for GOAP decisions ("my forge is running low on iron, I should buy more"). |
| lib-craft (L4, reverse) | Craft's "Crafting queues" potential extension (design consideration #1) is superseded by Workshop. Interactive crafting and automated production are complementary, not overlapping. |
| NPC GOAP decisions (via Actor) | NPCs with production tasks make economic decisions based on workshop state: restocking source inventories, collecting outputs, adjusting worker assignments. |

---

## State Storage

### Blueprint Store
**Store**: `workshop-blueprint` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `blueprint:{blueprintId}` | `ProductionBlueprintModel` | Primary lookup by blueprint ID |
| `blueprint-code:{gameServiceId}:{code}` | `ProductionBlueprintModel` | Code-uniqueness lookup within game service |

Paginated queries by gameServiceId + optional filters (category, tags, recipeCode, minWorkers range) use `IJsonQueryableStateStore<ProductionBlueprintModel>.JsonQueryPagedAsync()`.

### Task Store
**Store**: `workshop-task` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `task:{taskId}` | `ProductionTaskModel` | Primary lookup by task ID. Contains full task state including snapshot inputs/outputs, progress, status. |
| `task-owner:{ownerType}:{ownerId}` | `ProductionTaskListModel` | Active task IDs for an entity. Used by background worker for per-owner grouping and by `ListTasks` endpoint. |
| `task-blueprint:{blueprintId}` | `List<string>` | Task IDs using a blueprint. For blueprint deprecation impact analysis. |

### Worker Store
**Store**: `workshop-worker` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `worker:{taskId}` | `WorkerAssignmentListModel` | All worker assignments for a task. Stored as a list model (tasks rarely have more than 10-20 workers). |
| `worker-entity:{workerType}:{workerId}` | `WorkerEntityIndexModel` | Reverse index: which tasks is this entity assigned to? For cleanup when an entity is deleted. |

### Rate Segment Store
**Store**: `workshop-rate-segment` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `segments:{taskId}` | `RateSegmentListModel` | Ordered list of rate segments for a task. Append-only during task lifetime. Deleted on task cancellation/completion. |

### Production Cache
**Store**: `workshop-cache` (Backend: Redis, prefix: `workshop:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `status:{taskId}` | `ProductionStatusCacheModel` | Cached computed production status (units pending, estimated completion). Short TTL. Invalidated on materialization. |

### Distributed Locks
**Store**: `workshop-lock` (Backend: Redis, prefix: `workshop:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `task:{taskId}` | Task materialization lock (prevents concurrent lazy + worker materialization) |
| `worker:{taskId}` | Worker assignment/removal lock (serializes rate changes) |
| `blueprint:{blueprintId}` | Blueprint mutation lock |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `workshop-blueprint.created` | `WorkshopBlueprintCreatedEvent` | Blueprint created (lifecycle) |
| `workshop-blueprint.updated` | `WorkshopBlueprintUpdatedEvent` | Blueprint updated (lifecycle) |
| `workshop-blueprint.deprecated` | `WorkshopBlueprintDeprecatedEvent` | Blueprint deprecated (lifecycle) |
| `workshop.task.created` | `WorkshopTaskCreatedEvent` | Production task created. Includes blueprintId, ownerId, inventories. |
| `workshop.task.started` | `WorkshopTaskStartedEvent` | Task transitioned to running (first worker assigned, or autonomous task started). |
| `workshop.task.paused` | `WorkshopTaskPausedEvent` | Task paused. Includes status reason (manual, no_materials, no_space, no_workers). |
| `workshop.task.resumed` | `WorkshopTaskResumedEvent` | Task resumed from paused state. |
| `workshop.task.completed` | `WorkshopTaskCompletedEvent` | Task reached target quantity. Includes totalProduced, total game-time elapsed. |
| `workshop.task.cancelled` | `WorkshopTaskCancelledEvent` | Task cancelled. Includes totalProduced before cancellation. |
| `workshop.production.materialized` | `WorkshopProductionMaterializedEvent` | Production materialized (items created). Includes taskId, unitsMaterialized, totalProduced. Published both by lazy path and background worker. |
| `workshop.worker.assigned` | `WorkshopWorkerAssignedEvent` | Worker added to task. Includes workerId, new effective rate. |
| `workshop.worker.removed` | `WorkshopWorkerRemovedEvent` | Worker removed from task. Includes workerId, new effective rate. |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `worldstate.day-changed` | `HandleDayChangedAsync` | Triggers the background materialization worker to process tasks in the changed realm. Optional optimization: the worker already runs on its own timer, but day-changed events provide a natural game-time-aligned trigger for realms that just crossed a day boundary. |

### Resource Cleanup (FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| game-service | workshop | CASCADE | `/workshop/cleanup-by-game-service` |
| character | workshop | CASCADE | `/workshop/cleanup-by-entity` |
| inventory | workshop | CASCADE | `/workshop/cleanup-by-inventory` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaterializationIntervalSeconds` | `WORKSHOP_MATERIALIZATION_INTERVAL_SECONDS` | `30` | Real-time seconds between background materialization cycles (range: 5-300). |
| `MaterializationStartupDelaySeconds` | `WORKSHOP_MATERIALIZATION_STARTUP_DELAY_SECONDS` | `15` | Seconds to wait after startup before first materialization cycle (range: 0-120). |
| `MaxTasksPerOwnerPerTick` | `WORKSHOP_MAX_TASKS_PER_OWNER_PER_TICK` | `10` | Maximum tasks processed per owner per materialization cycle (range: 1-100). Fair scheduling cap. |
| `MaxActiveTasksPerOwner` | `WORKSHOP_MAX_ACTIVE_TASKS_PER_OWNER` | `20` | Maximum concurrent non-terminal tasks per owner (range: 1-200). |
| `MaxWorkersPerTask` | `WORKSHOP_MAX_WORKERS_PER_TASK` | `50` | Global cap on workers per task, applied when blueprint's maxWorkers is 0 (range: 1-500). |
| `MaxBlueprintsPerGameService` | `WORKSHOP_MAX_BLUEPRINTS_PER_GAME_SERVICE` | `5000` | Safety limit on blueprint count per game service (range: 100-50000). |
| `DefaultQuality` | `WORKSHOP_DEFAULT_QUALITY` | `0.5` | Default output quality when blueprint specifies `qualitySource: "blueprint_default"` (range: 0.0-1.0). |
| `FractionalProgressCap` | `WORKSHOP_FRACTIONAL_PROGRESS_CAP` | `1.0` | Maximum fractional progress that can accumulate. Prevents unbounded backlog when paused (range: 0.0-10.0). |
| `MaterialReservationBatchSize` | `WORKSHOP_MATERIAL_RESERVATION_BATCH_SIZE` | `10` | Number of units' worth of materials to validate per materialization check (range: 1-100). |
| `ProductionStatusCacheTtlSeconds` | `WORKSHOP_PRODUCTION_STATUS_CACHE_TTL_SECONDS` | `30` | TTL for cached production status in Redis (range: 5-300). |
| `RateSegmentRetentionCount` | `WORKSHOP_RATE_SEGMENT_RETENTION_COUNT` | `100` | Maximum rate segments retained per task. Older segments are compacted (range: 10-1000). |
| `ProficiencyDomain` | `WORKSHOP_PROFICIENCY_DOMAIN` | `""` | Default proficiency seed domain for worker multiplier calculation. Empty = no proficiency check (all workers contribute 1.0). |
| `DistributedLockTimeoutSeconds` | `WORKSHOP_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `15` | Timeout for distributed lock acquisition (range: 5-60). |
| `QueryPageSize` | `WORKSHOP_QUERY_PAGE_SIZE` | `20` | Default page size for paged queries (range: 1-100). |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<WorkshopService>` | Structured logging |
| `WorkshopServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 6 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IWorldstateClient` | Game time queries: `GetRealmTime` for current time, `GetElapsedGameTime` for lazy evaluation (L2 hard) |
| `IItemClient` | Creating output items during materialization (L2 hard) |
| `IInventoryClient` | Material consumption and output placement (L2 hard) |
| `IGameServiceClient` | Game service existence validation (L2 hard) |
| `IServiceProvider` | Runtime resolution of soft dependencies (lib-craft, lib-seed, lib-location) |
| `WorkshopMaterializationWorkerService` | Background `HostedService` that periodically processes pending production tasks with fair per-owner scheduling |
| `WorkshopProviderFactory` | Implements `IVariableProviderFactory` to provide `${workshop.*}` variables to Actor's behavior system |
| `IProductionCalculator` / `ProductionCalculator` | Internal helper for rate segment integration, material availability checks, and materialization logic |

### Variable Provider Factory

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `WorkshopProviderFactory` | `${workshop.*}` | Reads owner's active tasks, production status, worker counts | `IVariableProviderFactory` (DI singleton) |

---

## API Endpoints (Implementation Notes)

### Blueprint Management (6 endpoints)

All endpoints require `developer` role.

- **CreateBlueprint** (`/workshop/blueprint/create`): Validates game service existence. Validates code uniqueness within game service. If `recipeCode` is set: resolves recipe via lib-craft, snapshots inputs/outputs from recipe, validates recipe exists. If explicit inputs/outputs: validates item template codes exist via lib-item. Validates structural consistency (at least one output, base time > 0, minWorkers <= maxWorkers unless maxWorkers is 0). Enforces `MaxBlueprintsPerGameService`. Publishes `workshop-blueprint.created`.

- **GetBlueprint** (`/workshop/blueprint/get`): Supports lookup by blueprintId or by gameServiceId + code.

- **ListBlueprints** (`/workshop/blueprint/list`): Paged JSON query with required gameServiceId filter. Optional filters: category, tags (any match), recipeCode, minWorkers range, isActive, isDeprecated.

- **UpdateBlueprint** (`/workshop/blueprint/update`): Acquires distributed lock. Partial update. **Cannot change**: code, gameServiceId, recipeCode (identity-level). Active tasks using this blueprint continue with their creation-time snapshot; new tasks use updated values. Publishes `workshop-blueprint.updated`.

- **DeprecateBlueprint** (`/workshop/blueprint/deprecate`): Marks inactive. Active tasks continue. New tasks cannot use deprecated blueprints. Publishes `workshop-blueprint.deprecated`.

- **SeedBlueprints** (`/workshop/blueprint/seed`): Bulk creation, skipping existing codes (idempotent). Returns created/skipped counts.

### Task Management (7 endpoints)

- **CreateTask** (`/workshop/task/create`): Core task creation. Validates: blueprint exists and is active, owner exists, source and destination inventories exist and owner has access, realm exists (for game-time). If blueprint has location constraint: validates owner is at location (if lib-location available). Snapshots blueprint inputs/outputs onto the task (decouples from future blueprint changes). Creates initial rate segment (rate = 0 if minWorkers > 0, or base rate if minWorkers = 0). Sets status to `paused:no_workers` if minWorkers > 0, or `running` if minWorkers = 0 (autonomous). Enforces `MaxActiveTasksPerOwner`. Returns taskId.

- **GetTask** (`/workshop/task/get`): **Triggers lazy materialization** before returning. Acquires task lock, materializes pending production, returns current state including computed pending units, estimated completion time, and worker list.

- **ListTasks** (`/workshop/task/list`): Lists tasks for an owner. Does NOT trigger lazy materialization (too expensive for list queries). Returns last-known state. Filters: status, blueprintId, category.

- **PauseTask** (`/workshop/task/pause`): Manually pauses a running task. Materializes pending production first (flush). Records paused game-time. Sets status to `paused:manual`. Does NOT remove workers -- they remain assigned but idle.

- **ResumeTask** (`/workshop/task/resume`): Resumes from manual pause. Validates: still has minimum workers, materials available. Updates `lastProcessedGameTime` to current game-time (no backlog production during manual pause). Creates new rate segment. Sets status to `running`.

- **CancelTask** (`/workshop/task/cancel`): Cancels a non-terminal task. Materializes pending production first (flush any pending output). Removes all worker assignments. Deletes rate segments. Sets status to `cancelled`. Publishes `workshop.task.cancelled`.

- **AdjustTarget** (`/workshop/task/adjust-target`): Changes the target quantity on a running task. Can change from indefinite to finite, or adjust the finite target. If new target <= totalProduced, task completes immediately.

### Worker Management (3 endpoints)

- **AssignWorker** (`/workshop/worker/assign`): Adds a worker to a task. Acquires worker lock. Validates: worker not already assigned to this task, worker type is in blueprint's `workerTypes` (if specified), task not at `maxWorkers`. Materializes pending production (flush before rate change). Computes proficiency multiplier from lib-seed (if configured). Records worker assignment. Creates new rate segment with updated rate. If task was `paused:no_workers` and now meets `minWorkers`: resumes. Publishes `workshop.worker.assigned`.

- **RemoveWorker** (`/workshop/worker/remove`): Removes a worker from a task. Acquires worker lock. Materializes pending production (flush before rate change). Removes worker assignment. Creates new rate segment with updated rate. If worker count drops below `minWorkers`: pauses with `paused:no_workers`. Updates reverse index. Publishes `workshop.worker.removed`.

- **ListWorkers** (`/workshop/worker/list`): Returns all worker assignments for a task, including each worker's rate contribution and proficiency multiplier.

### Query (3 endpoints)

- **GetProductionStatus** (`/workshop/query/production-status`): Computes current production status without materializing. Returns: pending units (not yet materialized), estimated units if materialized now (capped by materials and space), estimated time to next unit, estimated time to target completion, current effective rate, current worker count. Cached in Redis with short TTL.

- **EstimateCompletion** (`/workshop/query/estimate-completion`): Given a task (or blueprint + inventories hypothetically), estimates game-time and real-time to reach a target quantity. Accounts for current rate and material availability. Returns both optimistic (materials always available) and pessimistic (only current materials) estimates.

- **GetBlueprintViability** (`/workshop/query/blueprint-viability`): Given a blueprint and source inventory, returns: how many units can be produced with current materials, which inputs are bottlenecks, what the effective rate would be with N workers. Used by NPC GOAP decisions and UI previews.

### Cleanup (3 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByGameService** (`/workshop/cleanup-by-game-service`): Cancels all tasks, deletes blueprints, workers, rate segments for a game service.
- **CleanupByEntity** (`/workshop/cleanup-by-entity`): Cancels tasks owned by the entity, removes entity from worker assignments on other tasks, cleans up reverse indexes.
- **CleanupByInventory** (`/workshop/cleanup-by-inventory`): Cancels tasks using this inventory as source or destination. Prevents orphaned tasks after inventory deletion.

---

## Visual Aid

Blueprints are owned here. Item creation and destruction are lib-item (L2). Container operations are lib-inventory (L2). Game time is lib-worldstate (L2). Recipe definitions, when referenced, are lib-craft (L4, soft). Worker proficiency, when relevant, is lib-seed (L2) or lib-craft (L4, soft). Workshop orchestrates these into a continuous production loop.

```
┌────────────────────────────────────────────────────────────────────────────┐
│                        Lazy Evaluation with Rate Segments                    │
│                                                                            │
│  Timeline (game-time):                                                     │
│  ─────────────────────────────────────────────────────────────────────→    │
│  T0          T1          T2          T3          T4 (now)                  │
│  │           │           │           │           │                         │
│  │ Segment 0 │ Segment 1 │ Segment 2 │ Segment 3 │                        │
│  │ rate=0.001│ rate=0.002│ rate=0.003│ rate=0.002│                        │
│  │ 1 worker  │ 2 workers │ 3 workers │ 2 workers │                        │
│  │           │           │           │(1 left)   │                        │
│  │           │           │           │           │                         │
│  lastProcessed                                   ▲                         │
│  = T1                                            │ MaterializeProduction() │
│                                                  │ called now              │
│  Computation:                                                              │
│  ┌──────────────────────────────────────────────────────┐                 │
│  │ Segment 1: (T2 - T1) * 0.002 = 7200s * 0.002 = 14.4 │                 │
│  │ Segment 2: (T3 - T2) * 0.003 = 3600s * 0.003 = 10.8 │                 │
│  │ Segment 3: (T4 - T3) * 0.002 = 1800s * 0.002 =  3.6 │                 │
│  │                                                        │                 │
│  │ pendingUnits = fractionalProgress + 14.4 + 10.8 + 3.6 │                 │
│  │              = 0.2 + 28.8                              │                 │
│  │              = 29.0                                    │                 │
│  │                                                        │                 │
│  │ wholeUnits = 29                                        │                 │
│  │ remainingFraction = 0.0                                │                 │
│  │                                                        │                 │
│  │ Materials available for: 25 units (bottleneck: iron)   │                 │
│  │ Destination capacity for: 40 units (plenty of space)   │                 │
│  │                                                        │                 │
│  │ actualUnits = min(29, 25, 40) = 25                     │                 │
│  │                                                        │                 │
│  │ → Consume 25 units' worth of materials from source     │                 │
│  │ → Create 25 output items in destination                │                 │
│  │ → task.totalProduced += 25                             │                 │
│  │ → task.fractionalProgress = min(0.0, 1.0) = 0.0       │                 │
│  │ → task.status = "paused:no_materials" (ran out)        │                 │
│  │ → task.lastProcessedGameTime = T4                      │                 │
│  └──────────────────────────────────────────────────────┘                 │
│                                                                            │
│  Worker Rate Change Flow:                                                  │
│  ┌──────────────────────────────────────────────────────┐                 │
│  │ AssignWorker(taskId, workerId):                        │                 │
│  │   1. Acquire lock: workshop:lock:worker:{taskId}       │                 │
│  │   2. MaterializeProduction(task, now)  ← flush first   │                 │
│  │   3. Add worker, compute new rate                      │                 │
│  │   4. Append RateSegment(startTime=now, rate=newRate)   │                 │
│  │   5. Update task.currentEffectiveRate                  │                 │
│  │   6. Release lock                                      │                 │
│  │                                                        │                 │
│  │ This ensures all production before the rate change     │                 │
│  │ is computed at the old rate, and production after is    │                 │
│  │ computed at the new rate. No lost or double-counted     │                 │
│  │ production.                                            │                 │
│  └──────────────────────────────────────────────────────┘                 │
└────────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 1: Blueprint Infrastructure
- Create `workshop-api.yaml` schema with all endpoints
- Create `workshop-events.yaml` schema
- Create `workshop-configuration.yaml` schema
- Generate service code
- Implement blueprint CRUD (create, get, list, update, deprecate, seed)
- Implement blueprint validation (input/output references, structural consistency)
- Implement recipe reference resolution from lib-craft (soft dependency path)

### Phase 2: Task Lifecycle
- Implement task creation with blueprint snapshotting
- Implement task status management (pause, resume, cancel, adjust-target)
- Implement source/destination inventory validation
- Implement rate segment storage (initial segment creation)
- Implement task listing and owner-scoped queries

### Phase 3: Worker Management
- Implement worker assignment with pre-materialization flush
- Implement worker removal with pre-materialization flush
- Implement rate recomputation on worker changes
- Implement proficiency multiplier from lib-seed (soft dependency path)
- Implement worker reverse index for entity cleanup
- Rate segment creation on worker changes

### Phase 4: Lazy Evaluation & Materialization
- Implement `ProductionCalculator` with rate segment integration
- Implement material availability checking against source inventory
- Implement destination capacity checking
- Implement materialization: consume inputs, create outputs via lib-item/lib-inventory
- Implement fractional progress tracking
- Implement auto-pause on material exhaustion and capacity limits
- Implement auto-resume checks on materialization worker

### Phase 5: Background Worker
- Implement `WorkshopMaterializationWorkerService` with fair per-owner scheduling
- Implement lazy materialization trigger on `GetTask`
- Implement distributed locking for concurrent materialization prevention
- Implement `worldstate.day-changed` event handler for realm-aligned triggers

### Phase 6: Query & Variable Provider
- Implement `GetProductionStatus` with caching
- Implement `EstimateCompletion` with rate-based projection
- Implement `GetBlueprintViability` for NPC decision support
- Implement `WorkshopProviderFactory` and `WorkshopProvider` (`${workshop.*}` namespace)
- Integration testing with Actor runtime

### Phase 7: Cleanup & Events
- Implement resource cleanup endpoints (by game-service, by entity, by inventory)
- Implement all event publishing
- Integration testing with lib-worldstate, lib-inventory, lib-item

---

## Potential Extensions

1. **Blueprint chains**: Blueprints that define multi-stage production pipelines where one task's output is another task's input. "Smelt ore → Forge ingots → Assemble weapon" as a single managed chain. Workshop could auto-create linked tasks where the destination of stage N is the source of stage N+1.

2. **Seasonal production modifiers**: Blueprints with seasonal rate multipliers. A farm blueprint produces at 1.5x in summer and 0.5x in winter. Workshop queries `${world.season}` from lib-worldstate and applies the seasonal modifier to the effective rate. Rate segments would need seasonal metadata.

3. **Quality degradation over time**: Output quality that degrades if items sit in the destination inventory too long (perishable goods). Workshop could track output creation timestamps and apply quality decay on collection. Relevant for food, potions, and other time-sensitive outputs.

4. **Collaborative tasks across owners**: Tasks where multiple owners contribute workers from their own entities. A town's communal bakery where multiple NPCs contribute labor. Requires multi-owner authorization and shared output distribution.

5. **Research/training tasks**: Tasks that produce no items but grant experience (to workers via lib-seed) or unlock recipes (via lib-craft's discovery system). The "output" is growth or knowledge rather than items. `outputs` would be empty; a new `grants` field would specify seed growth or recipe codes.

6. **Task templates**: Pre-configured task setups (blueprint + source + destination + target) that can be instantiated with one call. An NPC's "standard forge setup" that they re-create each morning.

7. **Market integration**: Workshop reads market prices for inputs and outputs to compute task profitability. The `${workshop.task.<code>.profitability}` variable would enable GOAP decisions like "switch to producing shields because they sell for more than swords right now."

8. **Visual production progress**: `workshop-client-events.yaml` for pushing real-time production ticks to connected WebSocket clients. Players watching a forge see a progress bar that advances based on the current rate. Low-bandwidth: one event per produced unit, not per game-second.

9. **Overflow handling**: When destination is full and source has materials, optionally create overflow storage (a temporary container) rather than pausing. The owner can collect overflow manually. Prevents lost production when destination fills unexpectedly.

10. **Worker fatigue**: Workers assigned for long game-time periods accumulate fatigue, reducing their proficiency multiplier. Rotating workers becomes a gameplay mechanic. Fatigue resets when the worker is removed from the task for a rest period.

---

## Variable Provider: `${workshop.*}` Namespace

Implements `IVariableProviderFactory` (via `WorkshopProviderFactory`) providing production task state to Actor (L2) for GOAP-driven economic decisions. Reads from cached task data per character/entity.

### Task Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${workshop.active_task_count}` | int | Number of active (non-terminal) production tasks owned by this entity |
| `${workshop.total_producing}` | int | Number of tasks currently in `running` status |
| `${workshop.total_paused}` | int | Number of tasks currently in any `paused:*` status |
| `${workshop.task.<code>.status}` | string | Status of task running a specific blueprint code (first match) |
| `${workshop.task.<code>.rate}` | float | Current effective rate of the task (units per game-second) |
| `${workshop.task.<code>.total_produced}` | long | Lifetime production count for the task |
| `${workshop.task.<code>.has_materials}` | bool | Whether source inventory has materials for at least 1 more unit |
| `${workshop.task.<code>.has_space}` | bool | Whether destination inventory has space for at least 1 more unit |
| `${workshop.task.<code>.worker_count}` | int | Current worker count on the task |
| `${workshop.any_paused_no_materials}` | bool | Whether any task is paused due to lack of materials |
| `${workshop.any_paused_no_space}` | bool | Whether any task is paused due to lack of space |

### ABML Usage Examples

```yaml
flows:
  manage_workshop:
    # NPC blacksmith manages their forge production line
    - cond:
        # My forge is paused because I ran out of iron
        - when: "${workshop.task.forge_iron_sword.status == 'paused:no_materials'}"
          then:
            - call: go_to_market
            - call: buy_iron_ingots
            - set: { shopping_reason: "restock_forge" }

        # My output chest is full -- go sell some swords
        - when: "${workshop.task.forge_iron_sword.status == 'paused:no_space'}"
          then:
            - call: collect_from_output_chest
            - call: go_to_market
            - call: sell_swords

        # Everything is running smoothly
        - when: "${workshop.task.forge_iron_sword.status == 'running'}"
          then:
            - cond:
                # But I could work faster with an apprentice
                - when: "${workshop.task.forge_iron_sword.worker_count < 2
                          && disposition.drive.has_drive.master_craft}"
                  then:
                    - call: look_for_apprentice
                - otherwise:
                    - call: do_other_work

  economic_decisions:
    # NPC evaluates whether to start new production
    - cond:
        # I have capacity for more tasks
        - when: "${workshop.active_task_count < 3
                  && craft.proficiency.blacksmithing > 5}"
          then:
            # Check what sells well at market
            - call: evaluate_market_demand
            - call: start_production_if_profitable

        # All my tasks are running and I'm not needed
        - when: "${workshop.total_producing > 0
                  && !workshop.any_paused_no_materials}"
          then:
            - call: pursue_personal_goals  # Follow drives
```

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified. Plugin is pre-implementation.*

### Intentional Quirks (Documented Behavior)

1. **Blueprint snapshots at task creation**: When a task is created, the blueprint's inputs, outputs, and base time are copied onto the task. Subsequent blueprint updates do NOT affect running tasks. This prevents mid-production changes from creating inconsistent state. New tasks pick up the updated blueprint.

2. **Fractional progress is capped**: When a task is paused (no materials, no space), fractional progress is capped at `FractionalProgressCap` (default: 1.0 = 1 unit). This prevents unbounded backlog accumulation during long pause periods. An entity that's been paused for 30 game-days doesn't suddenly produce 30 days' worth of items when materials are restocked -- they produce at most 1 pending unit immediately, then resume normal rate production.

3. **Manual pause does NOT accumulate production**: When a task is manually paused and then resumed, `lastProcessedGameTime` is set to the current game-time at resume. No backlog from the pause period is materialized. This is intentional -- manual pause means "stop producing," not "queue up production for later."

4. **Auto-pause DOES track elapsed time for status reporting**: When paused for `no_materials` or `no_space`, the task tracks how long it's been paused (for monitoring and `${workshop.*}` variables), but this time does NOT produce items. The task simply resumes at the current rate when the constraint is resolved.

5. **Materialization is idempotent within lock**: Both the lazy path (on query) and the worker path acquire the same distributed lock and read `lastProcessedGameTime`. If the lazy path just ran, the worker path finds nothing to do. Double-materialization is impossible.

6. **GetTask triggers materialization, ListTasks does not**: `GetTask` is the "give me the current accurate state" call and flushes pending production. `ListTasks` returns last-known state for efficiency -- listing 20 tasks shouldn't trigger 20 materializations. Callers wanting accurate counts should call `GetTask` per task.

7. **Worker proficiency is snapshotted at assignment**: A worker's proficiency multiplier is computed once when they're assigned. If their proficiency improves (via lib-seed growth) while assigned, the rate doesn't auto-update. The owner can remove and re-assign the worker to refresh the multiplier. This avoids continuous proficiency polling for every assigned worker.

8. **Rate of 0 is valid for paused-no-workers**: When `minWorkers > 0` and no workers are assigned, the rate is 0.0. A rate segment with rate 0.0 is recorded. `GetElapsedGameTime` integration over this segment correctly produces 0 units.

9. **Blueprint categories and tags are opaque strings**: "crafting", "mining", "farming" are conventions, not constraints. Games define their own production vocabulary.

10. **Autonomous tasks (minWorkers = 0) produce at base rate with no workers**: A blueprint with `minWorkers: 0` and `baseGameSecondsPerUnit: 3600` produces 1 unit per game-hour with no workers assigned. Assigning workers accelerates it. This covers passive production (a mine that slowly yields ore, a farm that grows crops without tending but faster with farmers).

### Design Considerations (Requires Planning)

1. **Material reservation strategy**: The current design checks material availability at materialization time. An alternative: reserve N units' worth of materials when the task starts or when materials are restocked. Reservation prevents other operations from consuming materials out from under a running task, but locks up inventory. The right approach may depend on game design -- a competitive multiplayer game needs reservations, a single-player-with-NPCs game may not.

2. **Concurrency between Workshop and interactive Craft**: If an NPC has both a Workshop task consuming iron from their supply chest AND is interactively crafting a special sword that also consumes iron, who gets the iron? First-come-first-served at the inventory level (lib-inventory doesn't know about reservations). Workshop should probably check material availability AFTER interactive crafting reservations, treating Workshop as lower priority.

3. **Worker entity lifecycle**: What happens when a worker character dies, is archived, or changes realm? Workshop needs either resource cleanup callbacks (character.deleted → remove from all workshop assignments) or periodic liveness checks. The resource cleanup path (FOUNDATION TENETS) is the correct pattern. Workshop registers as a reference source on character.

4. **Scale at 100,000+ NPCs**: If every NPC artisan has 1-3 production tasks, that's 100,000-300,000 active tasks. The background worker processing all tasks every 30 seconds with per-owner fair scheduling must be efficient. May need realm-scoped partitioning (process one realm per tick cycle) or priority-based scheduling (tasks that are running get processed before paused tasks checking for resume).

5. **Game-time jumps after downtime**: If worldstate catches up 2 game-months after server downtime, Workshop's lazy evaluation correctly computes "this task should have produced 1440 units." But materializing 1440 units at once (consuming 4320 iron ingots and creating 1440 swords) is expensive. May need a materialization cap per task per evaluation cycle, with the remainder processed over subsequent cycles.

6. **Worker proficiency refresh**: Quirk #7 notes that proficiency is snapshotted at assignment. This means a blacksmith NPC who improves over time doesn't benefit from their improved skill in automation until re-assigned. A periodic refresh (e.g., on `worldstate.season-changed`) could update all worker proficiency multipliers, but adds complexity. The trade-off between accuracy and simplicity needs a decision.

7. **Rate segment compaction**: Over long-running tasks with frequent worker changes, the rate segment list grows. `RateSegmentRetentionCount` enables compaction, but the compaction algorithm (merging adjacent segments with weighted averaging) must be designed to preserve accuracy for `MaterializeProduction` calculations that span compacted ranges.

8. **L2 soft dependencies should be hard per SERVICE-HIERARCHY.md**: The Dependencies section lists lib-seed (L2) and lib-location (L2) as soft dependencies with runtime resolution via `IServiceProvider`. Per the service hierarchy, L4 services MUST use constructor injection for L2 dependencies (they are guaranteed available). The rationale that "proficiency is optional" and "location constraint is optional" conflates feature optionality with service availability. The services are always running; the optional features should be handled at the data level (check if a proficiency domain is configured, check if `requiresLocationId` is set) rather than the service availability level. When implementing, use constructor injection for `ISeedClient` and `ILocationClient`, with the optional behavior gated by configuration/data checks, not null-service checks.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Blueprint infrastructure (Phase 1) is self-contained. Task lifecycle (Phase 2) depends on Phase 1 and lib-worldstate (Phase 2+). Lazy evaluation (Phase 4) is the core algorithm and should be extensively unit-tested. Background worker (Phase 5) depends on Phase 4.*
