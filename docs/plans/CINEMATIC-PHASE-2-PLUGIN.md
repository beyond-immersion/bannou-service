# Phase 2: lib-cinematic Plugin

> **Status**: Draft
> **Parent Plan**: [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md)
> **Prerequisites**: Phase 1 complete (cinematic-composer SDK with proven ABML bridge)
> **Estimated Scope**: Schema-first plugin following established pattern

---

## Goal

Build lib-cinematic as a thin L4 GameFeatures plugin following the established Bannou plugin pattern. The plugin is a **passive scenario registry** with mechanical condition matching, execution tracking, and distributed safety. Intelligence (which scenario to trigger, for whom) belongs to the actor system, not the plugin.

---

## Plugin Responsibilities

| Responsibility | How |
|----------------|-----|
| **Scenario storage** | MySQL for persistent templates, Redis for cache + ephemeral instances |
| **Condition matching** | Delegates to cinematic-composer SDK's condition evaluation |
| **Execution tracking** | Active instance tracking per realm/location in Redis |
| **Compilation pipeline** | Scenario → ABML (SDK) → bytecode (behavior-compiler) → Redis cache |
| **Behavior document serving** | `IBehaviorDocumentProvider` implementation (priority 75) |
| **Variable provider** | `IVariableProviderFactory` for `${cinematic.*}` namespace |
| **Distributed safety** | Locks, cooldowns, idempotency (same patterns as lib-storyline) |

---

## API Design (~8 POST Endpoints)

### Scenario Registry (4 endpoints)

| Endpoint | Operation | Notes |
|----------|-----------|-------|
| `/cinematic/scenario/create` | Create scenario template | Accepts `CinematicScenario` serialized as JSON, persists to MySQL |
| `/cinematic/scenario/get` | Get by ID or code | Read-through Redis cache |
| `/cinematic/scenario/list` | List with filtering | By realm, game service, tags, enabled status. Pagination. |
| `/cinematic/scenario/deprecate` | Soft-delete | Marks deprecated, prevents new triggers, running instances continue |

### Scenario Discovery (2 endpoints)

| Endpoint | Operation | Notes |
|----------|-----------|-------|
| `/cinematic/scenario/find-available` | Mechanical filter | Caller provides encounter context, plugin returns all scenarios whose trigger conditions are met. **No ranking.** Returns condition match details per scenario. |
| `/cinematic/scenario/test` | Dry-run evaluation | Test trigger conditions against context without executing. Returns per-condition diagnostics. |

### Execution (2 endpoints)

| Endpoint | Operation | Notes |
|----------|-----------|-------|
| `/cinematic/trigger` | Trigger scenario | Bind entities to slots, compile, cache, register with IBehaviorDocumentProvider. Distributed lock + cooldown + idempotency. Returns cinematic reference. |
| `/cinematic/get-active` | Query active instances | By realm, location, participant. Returns running cinematic IDs with participant info. |

---

## Schema Design

### cinematic-api.yaml

```yaml
openapi: 3.0.0
info:
  title: Cinematic Service API
  version: 1.0.0
  description: |
    Cinematic scenario registry and execution service (L4 GameFeatures).
    Stores hand-authored and procedurally generated cinematic scenarios,
    provides mechanical condition matching for passive discovery, and
    manages the compilation and execution pipeline.

    Plugins are passive registries. God-actors provide judgment about
    which scenarios to trigger. See CINEMATIC-SYSTEM.md for architecture.
x-service-layer: GameFeatures

servers:
  - url: http://localhost:5012
```

Key schema models:

| Model | Purpose |
|-------|---------|
| `CreateScenarioRequest` | Code, name, scenario JSON (CinematicScenario serialized), realm/game scope |
| `CreateScenarioResponse` | Scenario ID, ETag |
| `GetScenarioRequest` | By ID or code |
| `GetScenarioResponse` | Full scenario definition + metadata |
| `ListScenariosRequest` | Filters: realm, game, tags, enabled, pagination |
| `ListScenariosResponse` | Paginated scenario summaries |
| `DeprecateScenarioRequest` | Scenario ID, reason |
| `FindAvailableRequest` | Encounter context: participant capabilities, location, spatial state |
| `FindAvailableResponse` | List of matching scenarios with per-condition match details |
| `TestScenarioRequest` | Scenario ID + encounter context |
| `TestScenarioResponse` | Per-condition diagnostics (met/not-met, actual/expected) |
| `TriggerCinematicRequest` | Scenario ID, participant bindings (slot → entity ID), location, seed |
| `TriggerCinematicResponse` | Cinematic reference ID, participant list, estimated duration |
| `GetActiveCinematicsRequest` | Filters: realm, location, participant |
| `GetActiveCinematicsResponse` | List of active cinematic instances |

### cinematic-events.yaml

| Event | Published When |
|-------|---------------|
| `cinematic.scenario.created` | New scenario template registered |
| `cinematic.scenario.deprecated` | Scenario deprecated |
| `cinematic.triggered` | Cinematic instance started |
| `cinematic.completed` | Cinematic instance finished (normal completion) |
| `cinematic.aborted` | Cinematic instance aborted |

### cinematic-configuration.yaml

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `CompilationCacheTtlMinutes` | int | 60 | How long compiled BehaviorModels live in Redis cache |
| `CooldownDefaultSeconds` | int | 60 | Default cooldown between triggers of same scenario |
| `MaxActiveCinematicsPerRealm` | int | 50 | Safety limit on concurrent cinematics per realm |
| `MaxActiveCinematicsPerLocation` | int | 5 | Safety limit per location |
| `FindAvailableMaxResults` | int | 20 | Cap on scenarios returned by find-available |

### state-stores.yaml additions

| Store | Backend | Purpose |
|-------|---------|---------|
| `CinematicScenarioDefinitions` | MySQL (queryable) | Persistent scenario templates |
| `CinematicScenarioCache` | Redis (cacheable) | Read-through cache for definitions |
| `CinematicActiveInstances` | Redis | Active cinematic tracking per realm/location |
| `CinematicCooldowns` | Redis (TTL) | Per-scenario cooldown markers |
| `CinematicIdempotency` | Redis (TTL) | Idempotency keys for trigger requests |
| `CinematicCompilationCache` | Redis (TTL) | Compiled BehaviorModel cache (deterministic seed → bytecode) |

---

## Service Implementation

### CinematicService.cs (Partial Class Structure)

```csharp
[BannouService("cinematic", typeof(ICinematicService),
    lifetime: ServiceLifetime.Scoped,
    layer: ServiceLayer.GameFeatures)]
public partial class CinematicService : ICinematicService
{
    // Hard dependencies (L0/L1/L2 -- constructor injection)
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly IDistributedLockProvider _lockProvider;

    // State stores
    private readonly IQueryableStateStore<ScenarioDefinitionModel> _scenarioStore;
    private readonly ICacheableStateStore<ScenarioDefinitionModel> _scenarioCache;
    private readonly IStateStore<ActiveCinematicModel> _activeStore;
    private readonly IStateStore<CooldownMarker> _cooldownStore;
    private readonly IStateStore<IdempotencyMarker> _idempotencyStore;
    private readonly IStateStore<byte[]> _compilationCache;

    // Soft dependencies (L3/L4 -- runtime resolution)
    private readonly IServiceProvider _serviceProvider;
}
```

### IBehaviorDocumentProvider Implementation

Priority 75 (between dynamic/100 and seeded/50):

```csharp
public class CinematicBehaviorDocumentProvider : IBehaviorDocumentProvider
{
    public int Priority => 75;

    public bool CanProvide(string behaviorRef)
        => behaviorRef.StartsWith("cinematic:", StringComparison.Ordinal);

    public async Task<AbmlDocument?> GetDocumentAsync(string behaviorRef, CancellationToken ct)
    {
        // Strip "cinematic:" prefix, look up compiled bytecode from Redis cache
        // If cache miss, load scenario, export, compile, cache, return
    }
}
```

### IVariableProviderFactory Implementation

Provides `${cinematic.*}` variables to the Actor runtime:

| Variable | Type | Description |
|----------|------|-------------|
| `${cinematic.is_active}` | bool | Is this entity currently in a cinematic? |
| `${cinematic.role}` | string | Participant slot name (e.g., "thrower", "dodger") |
| `${cinematic.scenario_code}` | string | Code of the active scenario |
| `${cinematic.phase}` | string | Current phase/beat identifier |

### Trigger Flow (Critical Path)

```
TriggerCinematicAsync(request):
  1. Acquire distributed lock on scenario + location
  2. Check idempotency key (return existing if duplicate)
  3. Check cooldown (reject if cooling down)
  4. Check active limits (per-realm, per-location)
  5. Load scenario definition (MySQL → cache)
  6. Validate participant bindings against slot requirements
  7. Check compilation cache (deterministic seed → cached bytecode)
  8. If cache miss:
     a. Export to ABML via cinematic-composer AbmlExporter
     b. Compile via behavior-compiler
     c. Store compiled bytecode in Redis with TTL
  9. Register with IBehaviorDocumentProvider (cinematic:{reference_id})
  10. Create active instance record (Redis)
  11. Set cooldown marker (Redis TTL)
  12. Set idempotency marker (Redis TTL)
  13. Publish cinematic.triggered event
  14. Return cinematic reference
```

---

## Dependencies

### Hard (Constructor Injection)

| Dependency | Layer | What For |
|------------|-------|----------|
| `IStateStoreFactory` | L0 | All state stores |
| `IMessageBus` | L0 | Event publishing |
| `IDistributedLockProvider` | L0 | Trigger locking |
| `ILocationClient` | L2 | Validate location exists for trigger |

### Soft (Runtime Resolution, Graceful Degradation)

| Dependency | Layer | What For | If Missing |
|------------|-------|----------|------------|
| `IAssetClient` | L3 | Scenario template bundling | Skip bundling, scenarios still work via MySQL |
| `ICharacterPersonalityClient` | L4 | Enriched context for find-available | Return fewer match details, still functional |
| `IEthologyClient` | L4 | Species archetype matching | Skip species-specific conditions |
| `IAgencyClient` | L4 | QTE density gating (Phase 3) | All continuation points active (no progressive agency gating) |

---

## Implementation Steps

### Step 1: Create Schemas

1. Create `schemas/cinematic-api.yaml` with all endpoints
2. Create `schemas/cinematic-events.yaml` with event definitions
3. Create `schemas/cinematic-configuration.yaml` with configuration properties
4. Add state stores to `schemas/state-stores.yaml`
5. Verify schema rules compliance (read SCHEMA-RULES.md first)

### Step 2: Generate

1. `cd scripts && ./generate-service.sh cinematic`
2. Verify generated files in `plugins/lib-cinematic/Generated/` and `bannou-service/Generated/`
3. `dotnet build` succeeds (generated stubs)

### Step 3: Implement Service

1. Create `CinematicService.cs` (partial class) with constructor, state store initialization
2. Implement scenario CRUD (create, get, list, deprecate)
3. Implement find-available (assemble encounter context, delegate condition evaluation to SDK)
4. Implement test (dry-run evaluation with diagnostics)
5. Implement trigger (the critical path described above)
6. Implement get-active (query Redis)

### Step 4: Implement Provider Interfaces

1. Create `CinematicBehaviorDocumentProvider` implementing `IBehaviorDocumentProvider`
2. Create `CinematicVariableProviderFactory` implementing `IVariableProviderFactory`
3. Register both in plugin DI

### Step 5: Implement Events

1. Create `CinematicServiceEvents.cs` (partial class)
2. Publish events for scenario lifecycle and cinematic execution

### Step 6: Write Unit Tests

1. Create `plugins/lib-cinematic.tests/`
2. Test scenario CRUD operations
3. Test find-available with various encounter contexts
4. Test trigger flow (mocked dependencies)
5. Test compilation caching (cache hit vs miss)
6. Test distributed safety (cooldown, idempotency, active limits)

### Step 7: Build Verification

1. `dotnet build plugins/lib-cinematic/lib-cinematic.csproj --no-restore` succeeds
2. `dotnet test plugins/lib-cinematic.tests/lib-cinematic.tests.csproj --no-restore` passes
3. Full `dotnet build` succeeds

---

## Acceptance Criteria

1. Plugin follows schema-first development (all endpoints generated from schema)
2. All 8 endpoints implemented and compilable
3. `IBehaviorDocumentProvider` serves compiled cinematics at priority 75
4. `IVariableProviderFactory` provides `${cinematic.*}` variables
5. Trigger flow includes full distributed safety (lock, cooldown, idempotency, limits)
6. Compilation caching works (deterministic seed produces cache hits)
7. L3/L4 dependencies degrade gracefully when services are disabled
8. Unit tests cover all critical paths
9. `dotnet build` succeeds for the full solution

---

## Storage Model

### ScenarioDefinitionModel (MySQL)

```csharp
internal sealed class CinematicScenarioDefinitionModel
{
    public required Guid ScenarioId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string ScenarioJson { get; set; }  // Full CinematicScenario serialized
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Ephemeral { get; set; }  // Persistent vs ephemeral flag
    public Guid? RealmId { get; set; }
    public Guid? GameServiceId { get; set; }
    public string? TagsJson { get; set; }
    public int? CooldownSeconds { get; set; }
    public string? ExclusivityTagsJson { get; set; }
    public bool Deprecated { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public required string Etag { get; set; }
}
```

Note: Unlike lib-storyline's multiple JSON blob fields, lib-cinematic stores the entire `CinematicScenario` as a single `ScenarioJson` field. The SDK types handle the internal structure. This avoids the JSON-blob-per-field pattern that Phase 0 is remediating in lib-storyline.

### ActiveCinematicModel (Redis)

```csharp
internal sealed class ActiveCinematicModel
{
    public required Guid CinematicId { get; init; }
    public required Guid ScenarioId { get; init; }
    public required string ScenarioCode { get; init; }
    public required string BehaviorRef { get; init; }  // "cinematic:{id}" for IBehaviorDocumentProvider
    public required IReadOnlyDictionary<string, Guid> ParticipantBindings { get; init; }  // slot -> entity ID
    public required Guid LocationId { get; init; }
    public Guid? RealmId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public float? EstimatedDurationSeconds { get; init; }
}
```

---

*This is the Phase 2 implementation plan. For architectural context, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). For the SDK this plugin wraps, see [Phase 1](CINEMATIC-PHASE-1-COMPOSER-SDK.md).*
