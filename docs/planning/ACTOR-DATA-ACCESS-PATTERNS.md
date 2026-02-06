# Actor Data Access Patterns

> **Status**: Planning/Architecture Decision Record
> **Created**: 2026-01-30
> **Scope**: Cross-plugin data access patterns for lib-actor and lib-behavior

This document establishes consistent architectural patterns for how actors and behaviors access data from other plugins in the Bannou ecosystem.

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Background: The agent-memories Investigation](#background)
3. [Current Architecture Analysis](#current-architecture)
4. [Cross-Plugin Data Access Options](#data-access-options)
5. [Recommended Patterns](#recommended-patterns)
6. [Implementation Guidelines](#implementation-guidelines)
7. [Migration Path](#migration-path)

---

## 1. Executive Summary {#executive-summary}

### The Question

How should actors/behaviors access data from other plugins (currency, inventory, character, character-history, etc.)? Three options exist:

1. **API-based access**: Call plugin services via lib-mesh
2. **Shared datastores**: Directly access other plugins' state stores
3. **Actor-local caching**: Cache external data in actor-owned stores

### The Recommendation

**Hybrid approach with clear boundaries**:

| Data Type | Access Pattern | Rationale |
|-----------|----------------|-----------|
| **Cognition data** (memories, perceptions) | Shared datastore (`agent-memories`) | High-frequency, owned by cognition subsystem |
| **Character traits** (personality, backstory) | Cached via Variable Providers | Read-heavy, changes infrequently |
| **Game state** (currency, inventory, items) | API calls via lib-mesh | Authoritative source, consistency critical |
| **Spatial context** (zone, region, safety) | Cached Variable Provider | Coarse-grained, refreshed periodically (~10s) |

### Key Decision: agent-memories Ownership

**Affirmed**: The `agent-memories` store should remain attributed to `service: Actor` in state-stores.yaml, but with updated documentation clarifying it's part of the **Actor/Behavior cognition subsystem** - a shared concern between the two plugins.

The current implementation (lib-behavior's `ActorLocalMemoryStore` using `StateStoreDefinitions.AgentMemories`) is **intentionally correct** and should not change.

---

## 2. Background: The agent-memories Investigation {#background}

### The Anomaly Report

An investigation flagged that `lib-behavior` uses the `agent-memories` state store, but the store is declared with `service: Actor` in state-stores.yaml:

```yaml
# schemas/state-stores.yaml (lines 106-110)
agent-memories:
  backend: redis
  prefix: "agent:mem"
  service: Actor
  purpose: Agent memory and cognitive state
```

### Investigation Findings

1. **lib-actor does NOT use agent-memories directly**
   - Uses: `actor-templates`, `actor-state`, `actor-pool-nodes`, `actor-assignments`
   - The `ActorRunner` manages tick loops and behavior document execution
   - No direct memory store access in lib-actor code

2. **lib-behavior DOES use agent-memories**
   - `ActorLocalMemoryStore` (Cognition subsystem) stores/retrieves memories
   - All configuration in `BehaviorServiceConfiguration`
   - Interface `IMemoryStore` designed for swappable implementations

3. **The naming is entity-agnostic**
   - Store uses `entityId`, not `actorId`
   - Supports any entity having memories (characters, actors, NPCs)
   - Design allows non-actor entities to have cognitive memory

### Why This Design is Correct

The Actor/Behavior separation follows an intentional architectural pattern:

| Plugin | Responsibility | Data Scope |
|--------|----------------|------------|
| **lib-actor** | Execution infrastructure | Actor instances, pool management, templates |
| **lib-behavior** | Cognition and decision-making | Memories, perceptions, GOAP, ABML |

The `agent-memories` store is **cognition data**, which is lib-behavior's domain. The `service: Actor` attribution reflects that memories exist in the broader "actor system" context, not that lib-actor owns the implementation.

**Analogy**: In a human brain, the prefrontal cortex (behavior/planning) uses the hippocampus (memory), but both are part of the same "person" (actor).

---

## 3. Current Architecture Analysis {#current-architecture}

### Plugin Dependency Graph

```
lib-actor
    ├── owns: actor-templates, actor-state, actor-pool-nodes, actor-assignments
    ├── consumes: lib-behavior (for ABML document execution)
    └── manages: ActorRunner tick loop

lib-behavior
    ├── owns (used): agent-memories (via ActorLocalMemoryStore)
    ├── owns: behavior-statestore (ABML definitions)
    ├── provides: Cognition pipeline, GOAP planner, ABML compiler
    └── consumes: Character data via Variable Providers
```

### Data Flow: Actor Making Decisions

```
┌─────────────────────────────────────────────────────────────────────┐
│                        External Plugins                              │
├─────────────┬─────────────┬──────────────┬─────────────────────────┤
│ lib-currency│ lib-inventory│ lib-character│ lib-character-history  │
└──────┬──────┴──────┬──────┴──────┬───────┴──────────┬──────────────┘
       │             │             │                  │
       ▼             ▼             ▼                  ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Data Access Layer                                 │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────────────┐│
│  │ Mesh Clients   │  │ Variable       │  │ Event Subscriptions    ││
│  │ (API calls)    │  │ Providers      │  │ (real-time updates)    ││
│  └────────┬───────┘  └───────┬────────┘  └───────────┬────────────┘│
└───────────┼──────────────────┼───────────────────────┼──────────────┘
            │                  │                       │
            ▼                  ▼                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      lib-behavior (Cognition)                        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐ │
│  │ Attention   │─▶│ Significance│─▶│ Memory      │─▶│ Goal Impact│ │
│  │ Filter      │  │ Assessment  │  │ Formation   │  │ Evaluation │ │
│  └─────────────┘  └─────────────┘  └──────┬──────┘  └─────┬──────┘ │
│                                           │                │        │
│                                           ▼                ▼        │
│                                    ┌─────────────┐  ┌────────────┐ │
│                                    │agent-memories│  │ GOAP       │ │
│                                    │(shared store)│  │ Planner    │ │
│                                    └─────────────┘  └────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      lib-actor (Execution)                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────────┐ │
│  │ ActorRunner │  │ Pool        │  │ Intent Channels             │ │
│  │ (tick loop) │  │ Management  │  │ (output to game systems)    │ │
│  └─────────────┘  └─────────────┘  └─────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### Current Variable Providers (lib-actor)

ABML documents access character data through registered Variable Providers (implemented in `lib-actor/Runtime/`):

| Provider | Data Source | Access Pattern |
|----------|-------------|----------------|
| `PersonalityProvider` | lib-character-personality | API call (cached) |
| `CombatPreferencesProvider` | lib-character-personality | API call (cached) |
| `BackstoryProvider` | lib-character-history | API call (cached) |

Example ABML usage:
```yaml
condition: "${personality.aggression} > 0.7"
action: "attack_nearest_enemy"
```

---

## 4. Cross-Plugin Data Access Options {#data-access-options}

### Option A: API-Only (via lib-mesh)

**How it works**: All external data access goes through service clients.

```csharp
// Every time GOAP needs currency data:
var (status, balance) = await _currencyClient.GetBalanceAsync(new GetBalanceRequest
{
    WalletId = entityId,
    CurrencyCode = "gold"
}, ct);
```

**Pros**:
- Single source of truth (authoritative)
- No stale data
- Clean plugin boundaries
- Works across distributed deployments

**Cons**:
- Latency on every access (network round-trip)
- High load on target services during actor ticks
- GOAP planning may require dozens of queries per decision

**Best for**: Mutating operations, infrequent reads, consistency-critical data

### Option B: Shared Datastores

**How it works**: Actors directly read other plugins' state stores.

```csharp
// Direct state store access:
var balanceStore = _stateStoreFactory.GetStore<CurrencyBalance>(
    StateStoreDefinitions.CurrencyBalances);
var balance = await balanceStore.GetAsync($"wallet:{walletId}:gold", ct);
```

**Pros**:
- Fast (no network hop within same process)
- Reduced load on service layers
- Bulk queries possible

**Cons**:
- Tight coupling to store schema
- Breaks plugin encapsulation
- Schema changes break actors silently
- Only works in monolithic deployment

**Best for**: High-frequency read-only access where schema is stable

### Option C: Actor-Local Caching

**How it works**: Actors maintain local caches of external data, populated via APIs or events.

```csharp
// Actor maintains its own cache:
public class ActorDataCache
{
    private readonly ConcurrentDictionary<string, CurrencySnapshot> _currencyCache;
    private readonly ConcurrentDictionary<string, InventorySnapshot> _inventoryCache;

    // Populated on actor spawn and via event subscriptions
    public async Task RefreshCurrencyAsync(string entityId, CancellationToken ct)
    {
        var (_, balance) = await _currencyClient.GetBalanceAsync(...);
        _currencyCache[entityId] = new CurrencySnapshot(balance, DateTimeOffset.UtcNow);
    }
}
```

**Pros**:
- Very fast reads (in-memory)
- Decoupled from source availability
- Batch refresh possible
- Works in distributed deployments

**Cons**:
- Stale data (cache invalidation is hard)
- Memory pressure with many actors
- Complexity of cache management
- Need event subscriptions for freshness

**Best for**: Frequently accessed, tolerance for eventual consistency

### Option D: Hybrid (Recommended)

**How it works**: Different patterns for different data characteristics.

| Characteristic | Pattern | Example |
|----------------|---------|---------|
| High-frequency, owned | Shared store | agent-memories |
| Read-heavy, stable | Variable Provider (cached) | personality traits |
| Mutation or consistency-critical | API call | currency transactions |
| Spatial context, coarse-grained | Cached Variable Provider | zone, region, safety |

---

## 5. Recommended Patterns {#recommended-patterns}

### Pattern 1: Cognition Data (Shared Store)

**Use case**: Data that IS the actor's cognition (memories, perceptions, learned behaviors)

**Implementation**: Shared state store with lib-behavior ownership

```yaml
# state-stores.yaml
agent-memories:
  backend: redis
  prefix: "agent:mem"
  service: Actor  # Part of Actor/Behavior subsystem
  purpose: Agent memory and cognitive state (used by lib-behavior cognition)
```

**Rationale**: This data doesn't "belong" to another plugin - it's the actor's own cognitive state. The store is shared infrastructure for the cognition subsystem.

### Pattern 2: Character Attributes (Variable Providers)

**Use case**: Character data needed for ABML conditions and GOAP planning

**Implementation**: Registered Variable Providers with TTL-based caching

```csharp
public class CurrencyVariableProvider : IVariableProvider
{
    private readonly ICurrencyClient _currencyClient;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);

    public string Prefix => "currency";

    public async Task<object?> GetValueAsync(string path, string entityId, CancellationToken ct)
    {
        // path examples: "gold", "silver", "reputation"
        var cacheKey = $"currency:{entityId}:{path}";

        if (!_cache.TryGetValue(cacheKey, out decimal balance))
        {
            var (status, response) = await _currencyClient.GetBalanceAsync(
                new GetBalanceRequest { WalletId = entityId, CurrencyCode = path }, ct);

            if (status == StatusCodes.OK && response != null)
            {
                balance = response.Balance;
                _cache.Set(cacheKey, balance, _ttl);
            }
        }

        return balance;
    }
}
```

**ABML usage**:
```yaml
condition: "${currency.gold} >= 100"
action: "consider_purchase"
```

**Rationale**: Clean API boundary, caching handles performance, ABML syntax stays clean.

### Pattern 3: Game State Queries (API with Batching)

**Use case**: GOAP planning needs to query multiple external states

**Implementation**: Batched API calls with parallel execution

> **📋 PROPOSED**: This class does not yet exist. See [GitHub Issue #148](https://github.com/beyond-immersion/bannou-service/issues/148) for implementation tracking.

```csharp
public class GoapWorldStateProvider
{
    public async Task<WorldState> BuildWorldStateAsync(string entityId, CancellationToken ct)
    {
        // Parallel fetch of external state
        var tasks = new[]
        {
            FetchCurrencyStateAsync(entityId, ct),
            FetchInventoryStateAsync(entityId, ct),
            FetchRelationshipStateAsync(entityId, ct),
            FetchLocationStateAsync(entityId, ct)
        };

        await Task.WhenAll(tasks);

        return new WorldState
        {
            Currency = tasks[0].Result,
            Inventory = tasks[1].Result,
            Relationships = tasks[2].Result,
            Location = tasks[3].Result
        };
    }
}
```

**Rationale**: APIs maintain consistency, parallel execution minimizes latency, clear data ownership.

### Pattern 4: Spatial Context (Cached Location Data)

**Use case**: Actors need occasional spatial context for decision-making (zone awareness, proximity, safety)

**Important**: Actors are "souls updating mind state" - they need coarse-grained spatial context for GOAP planning, **not** frame-by-frame position telemetry. The mapping service already handles detailed spatial queries and affordances.

> **📋 PROPOSED**: See [GitHub Issue #145](https://github.com/beyond-immersion/bannou-service/issues/145) for implementation tracking.

```csharp
public class LocationContextProvider : IVariableProvider
{
    private readonly ILocationClient _locationClient;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(10);  // Not real-time!

    public string Name => "location";

    public async Task<object?> GetValueAsync(string path, string entityId, CancellationToken ct)
    {
        var context = await GetOrLoadContextAsync(entityId, ct);

        return path switch
        {
            "zone" => context.ZoneName,           // "tavern_district"
            "region" => context.RegionName,       // "capital_city"
            "is_safe" => context.IsSafeZone,      // true (in town, sanctuary)
            "is_hostile" => context.IsHostile,    // false
            "nearby_pois" => context.NearbyPOIs,  // ["blacksmith", "inn"]
            _ => null
        };
    }
}
```

**What actors need:**
| Need | Example | Frequency |
|------|---------|-----------|
| Zone awareness | "I'm in the tavern district" | Every ~10 seconds |
| Safety context | "Am I in hostile territory?" | On demand (cached) |
| Significant events | "Enemy entered perception range" | Via perception pipeline |

**What actors do NOT need:**
- Frame-by-frame X/Y/Z coordinates
- Real-time velocity vectors
- Continuous position streams

**Rationale**: Actors make high-level decisions ("should I flee?", "should I trade?"), not physics calculations. Coarse spatial context with caching is sufficient.

---

## 6. Implementation Guidelines {#implementation-guidelines}

### When to Use Each Pattern

```
┌─────────────────────────────────────────────────────────────────────┐
│                    Data Access Decision Tree                         │
└─────────────────────────────────────────────────────────────────────┘

Is this data the actor's own cognitive state?
    YES → Pattern 1: Shared Store (agent-memories)
    NO  ↓

Is this spatial/location context for decision-making?
    YES → Pattern 4: Spatial Context (cached, coarse-grained)
    NO  ↓

Is consistency critical (currency, ownership)?
    YES → Pattern 3: API with Batching
    NO  ↓

Is this character attribute data for ABML?
    YES → Pattern 2: Variable Provider
    NO  → Pattern 3: API with Batching
```

### Variable Provider Registration

All Variable Providers should be registered in lib-actor's DI setup:

```csharp
// lib-actor service registration (in ActorRunner)
services.AddSingleton<IVariableProvider, PersonalityVariableProvider>();
services.AddSingleton<IVariableProvider, CombatPreferencesVariableProvider>();
services.AddSingleton<IVariableProvider, BackstoryVariableProvider>();
services.AddSingleton<IVariableProvider, EncountersVariableProvider>();
// Phase 2 providers - see GitHub Issue #147
services.AddSingleton<IVariableProvider, CurrencyVariableProvider>();      // PLANNED - NOT YET IMPLEMENTED
services.AddSingleton<IVariableProvider, InventoryVariableProvider>();     // PLANNED - NOT YET IMPLEMENTED
services.AddSingleton<IVariableProvider, RelationshipVariableProvider>();  // PLANNED - NOT YET IMPLEMENTED
```

### Cache TTL Guidelines

| Data Type | Recommended TTL | Rationale | Status |
|-----------|-----------------|-----------|--------|
| Personality traits | 5 minutes | Changes very slowly | ✅ Implemented |
| Backstory elements | 10 minutes | Nearly immutable | ✅ Implemented |
| Encounter data | 5 minutes | Changes via explicit actions | ✅ Implemented |
| Currency balances | 30 seconds | May change from player actions | 📋 Planned |
| Inventory contents | 1 minute | Changes less frequently than currency | 📋 Planned |
| Relationships | 5 minutes | Changes via explicit actions only | 📋 Planned |
| Positions (event-driven) | N/A (real-time) | Overwritten on each event | 📋 Planned ([#145](https://github.com/beyond-immersion/bannou-service/issues/145)) |

### State Store Documentation Update

Update the comment in state-stores.yaml to clarify the Actor/Behavior subsystem:

```yaml
# ============================================================================
# Actor/Behavior Stores - Agent Cognition Subsystem
# ============================================================================
# These stores support the Actor/Behavior cognition subsystem.
# lib-actor manages execution (instances, pools, templates).
# lib-behavior manages cognition (memories, perceptions, planning).
# The agent-memories store is used by lib-behavior's cognition pipeline
# but attributed to Actor as the encompassing subsystem.

agent-memories:
  backend: redis
  prefix: "agent:mem"
  service: Actor
  purpose: Agent memory and cognitive state (cognition pipeline in lib-behavior)
```

---

## 7. Migration Path {#migration-path}

### Phase 1: Documentation (Immediate)

1. Update state-stores.yaml comments as shown above
2. Update ACTOR.md and BEHAVIOR.md deep dives to clarify relationship
3. Document Variable Provider pattern in ABML guide

### Phase 2: Variable Providers (Short-term) — [Issue #147](https://github.com/beyond-immersion/bannou-service/issues/147)

1. Implement `CurrencyVariableProvider` for ABML access to wallet balances
2. Implement `InventoryVariableProvider` for item/container queries
3. Implement `RelationshipVariableProvider` for social state
4. Add cache TTL configuration to `ActorServiceConfiguration`

### Phase 3: GOAP Integration (Medium-term) — [Issue #148](https://github.com/beyond-immersion/bannou-service/issues/148)

1. Create `GoapWorldStateProvider` with batched external queries
2. Integrate with existing GOAP planner in lib-behavior
3. Add configuration for which data sources to include in world state

### Phase 4: Event-Driven Cache (Future) — [Issue #145](https://github.com/beyond-immersion/bannou-service/issues/145)

1. Implement `ActorPerceptionCache` for real-time spatial data
2. Subscribe to position, combat, and effect events
3. Integrate with attention filter in cognition pipeline

---

## Summary

### Key Decisions

1. **agent-memories ownership is correct**: Keep `service: Actor`, clarify in comments that it's part of the Actor/Behavior cognition subsystem used by lib-behavior.

2. **Hybrid data access pattern**: Different patterns for different data characteristics, not a one-size-fits-all approach.

3. **Variable Providers for ABML**: Character attributes accessed via cached Variable Providers, keeping ABML syntax clean (`${currency.gold}`).

4. **APIs for mutations**: All state-changing operations go through service APIs for consistency.

5. **Events for real-time**: High-velocity data uses event subscriptions with local caching.

### Anti-Patterns to Avoid

- **Don't** have actors directly write to other plugins' state stores
- **Don't** poll APIs in tight loops (batch or use events)
- **Don't** cache mutation-critical data like ownership or balances beyond short TTLs
- **Don't** create coupling to internal store schemas of other plugins

---

## Appendix A: Research Findings from Source Files

This appendix contains raw findings from actual source file reads, accumulated during research.

### Appendix Organization

The findings are categorized by source type:

| Category | Sections | Description |
|----------|----------|-------------|
| **Active Documentation** | A.1 - A.5 | Current guides and schemas (describes what exists) |
| **Implementation Code** | A.6 - A.7, A.11 - A.14, A.16 - A.17 | Actual source code findings (verified implementations) |
| **Planning Documents** | A.8 - A.10, A.15 | Future architecture proposals (describes what's planned) |

> **Note**: Planning document findings (A.8-A.10, A.15) describe **future features that do not yet exist**.
> Code samples from these sections are aspirational, not implementations.

---

### Active Documentation Findings

### A.1 Findings from docs/guides/ABML.md

**File**: `docs/guides/ABML.md` (2196 lines)
**Version**: 2.1, Implemented (414 tests passing)
**Location**: `bannou-service/Abml/`

#### Variable Providers (Section 5.4, lines 587-676)

ABML accesses character data through **Variable Providers** registered with the ActorRunner:

1. **PersonalityProvider** (`${personality.*}`)
   - Exposes 8 personality trait axes (normalized 0.0-1.0)
   - Examples: `${personality.openness}`, `${personality.aggression}`, `${personality.loyalty}`
   - Includes version counter for change detection: `${personality.version}`

2. **CombatPreferencesProvider** (`${combat.*}`)
   - Style enum: aggressive/defensive/tactical/opportunistic
   - Range preference: close/mid/long
   - Group role: leader/support/striker/tank
   - Numeric preferences: `${combat.riskTolerance}`, `${combat.retreatThreshold}`
   - Boolean preferences: `${combat.protectAllies}`

3. **BackstoryProvider** (`${backstory.*}`)
   - Exposes 9 backstory element types
   - Direct access: `${backstory.origin}`, `${backstory.fear}`, `${backstory.trauma}`
   - Property access: `${backstory.fear.key}`, `${backstory.fear.value}`, `${backstory.fear.strength}`
   - Collection access: `${backstory.elements}`, `${backstory.elements.TRAUMA}`

**Quote (lines 587-589)**: "When executing behaviors for characters, the ActorRunner automatically registers variable providers that expose character data"

#### Options Block and Memory Storage (Section 2.3, lines 123-216)

The `options` block stores evaluated options in `state.memories.{type}_options`:
- Options are re-evaluated each tick
- Cached options are queryable via `/actor/query-options`
- Supports freshness levels and max_age_ms

#### Service Calls in ABML (lines 1026-1036)

ABML supports service calls via `service_call` action:
```yaml
- service_call:
    service: economy_service
    method: purchase_item
    parameters:
      item_id: "${selected_item.id}"
      quantity: 1
    result_variable: purchase_result
    on_error:
      - handle_purchase_error
```

**Implementation requirement** (Appendix A.1, lines 1821-1826): All ABML service_call handlers MUST use `IMeshInvocationClient` or generated clients via lib-mesh. Direct HTTP client usage is forbidden.

#### Cognition Pipeline Reference (Section 14.6, lines 1502-1512)

The Behavior service implements a 5-stage cognition pipeline:
1. Attention Filter → Budget-limited selection with priority weighting
2. Significance Assessment → Threat detection with fast-track bypass (urgency > 0.8)
3. Memory Formation → Stores memories with significance >= threshold (default 0.7)
4. Goal Impact Evaluation → Determines which goals are affected
5. Intention Formation → Triggers GOAP replan with urgency-based parameters

**Key file reference**: Full details in `docs/plugins/BEHAVIOR.md`

### A.2 Findings from docs/guides/ACTOR_SYSTEM.md

**File**: `docs/guides/ACTOR_SYSTEM.md` (1944 lines)
**Version**: 1.1, Implemented (Phases 0-5), Character Data Layer Complete
**Location**: `plugins/lib-actor/`, `plugins/lib-behavior/`, `plugins/lib-character-personality/`, `plugins/lib-character-history/`

#### Critical Architectural Point (Section 2.1-2.3)

**"Without ANY actor, a character is fully functional."** (line 97)

Actors are OPTIONAL - they provide growth, spontaneity, personality, realism but are NOT foundation. The dependency graph (lines 109-126) explicitly shows:
- Game Client → Game Server → Bannou Services
- Bannou Services split into REQUIRED (Character, Behavior, Asset) and OPTIONAL (Actor Service)
- **"Nothing depends on Actor Service."** (line 126)

#### Actor Definition (Section 1.1, lines 36-47)

An **Actor** is a long-running task that executes a behavior (ABML document) in a loop. Actors are:
- **NOT request-response** entities
- Autonomous processes that run continuously on pool nodes
- Execute behaviors defined in ABML
- Subscribe to events and react over time
- Emit state updates that influence character behaviors

#### Two Actor Paradigms (Section 1.2, lines 49-54)

| Actor Type | Scope | Primary Function | Data Flow |
|------------|-------|------------------|-----------|
| NPC Brain Actor | Single character | Growth, personality, feelings | Consumes perceptions → Emits state updates |
| Event Actor | Region/situation | Orchestration, drama | Queries spatial data → Emits cinematics/events |

#### Perception Flow - Direct Event Subscription (Section 4.2, lines 202-238)

**Critical**: Perceptions flow **DIRECTLY** from Game Server to Actor via event subscription - the control plane does NOT route perceptions.

```
Game Server → publish → character.{characterId}.perception → lib-messaging (RabbitMQ) → Actor Pool Node
```

This scales horizontally with pool nodes - no bottleneck in event routing.

#### Character Data Layer (Section 5.1-5.6, lines 368-523)

Two plugins provide character data:

1. **lib-character-personality**:
   - 8 Personality Traits: OPENNESS, CONSCIENTIOUSNESS, EXTRAVERSION, AGREEABLENESS, NEUROTICISM, HONESTY, AGGRESSION, LOYALTY
   - Combat Preferences: style, preferredRange, groupRole, riskTolerance, retreatThreshold, protectAllies
   - Experience Evolution: probabilistic trait changes from experiences (TRAUMA, BETRAYAL, VICTORY, etc.)

2. **lib-character-history**:
   - 9 Backstory Element Types: ORIGIN, OCCUPATION, TRAINING, TRAUMA, ACHIEVEMENT, SECRET, GOAL, FEAR, BELIEF
   - Event Participation: roles (LEADER, COMBATANT, VICTIM, WITNESS, BENEFICIARY, etc.)

#### ActorRunner Character Data Loading (Section 5.6, lines 501-523)

When an actor starts for a character, the ActorRunner automatically loads personality, combat preferences, and backstory:

```csharp
if (CharacterId.HasValue)
{
    // Load and cache personality traits
    var personality = await _personalityCache.GetOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new PersonalityProvider(personality));

    // Load and cache combat preferences
    var combatPrefs = await _personalityCache.GetCombatPreferencesOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new CombatPreferencesProvider(combatPrefs));

    // Load and cache backstory
    var backstory = await _personalityCache.GetBackstoryOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new BackstoryProvider(backstory));
}
```

**Data is cached with a 5-minute TTL and stale-if-error fallback for resilience.**

#### Query Options API (Section 5.7, lines 525-556)

Event Actors can query character agents for available options using `/actor/query-options`:

**Freshness levels:**
- `fresh` - Force re-evaluation of options (for critical decisions)
- `cached` - Accept recently cached options (configurable max age)
- `stale_ok` - Accept any cached value (for low-priority queries)

#### Event Brain ABML Actions (Section 6.8, lines 713-775)

**Coordination Actions (lib-actor)**:
| Action | Parameters | Description |
|--------|------------|-------------|
| `query_options` | `actor_id`, `query_type`, `freshness?`, `max_age_ms?`, `context?`, `result_variable?` | Query another actor's available options via RPC |
| `query_actor_state` | `actor_id`, `paths?`, `result_variable?` | Query another actor's state from local registry |
| `emit_perception` | `target_character`, `perception_type`, `urgency?`, `source_id?`, `data?` | Send choreography instruction to a character |
| `schedule_event` | `delay_ms`, `event_type`, `target_character?`, `data?` | Schedule a delayed event |
| `state_update` | `path`, `operation`, `value` | Update working memory (set/append/increment/decrement) |

**Cognition Actions (lib-behavior)**:
| Action | Parameters | Description |
|--------|------------|-------------|
| `filter_attention` | `input`, `attention_budget?`, `priority_weights?` | Filter perceptions by attention budget |
| `query_memory` | `perceptions`, `entity_id`, `limit?` | Query memory store for relevant memories |
| `assess_significance` | `perception`, `memories?`, `personality?`, `weights?` | Score perception significance |
| `store_memory` | `entity_id`, `perception`, `significance?` | Store significant perception as memory |
| `evaluate_goal_impact` | `perceptions`, `current_goals`, `current_plan?` | Evaluate perception impact on goals |
| `trigger_goap_replan` | `goals`, `urgency?`, `world_state?` | Trigger GOAP replanning |

#### Memory System MVP (Section 7.3, lines 892-933)

Current implementation uses keyword-based relevance matching:

| Factor | Weight | Description |
|--------|--------|-------------|
| Category match | 0.3 | Memory category matches perception category |
| Content overlap | 0.4 | Shared keywords between perception and memory |
| Metadata overlap | 0.2 | Shared keys in metadata |
| Recency bonus | 0.1 | Memories < 1 hour old get boost |
| Significance bonus | 0.1 | Higher significance memories score higher |

**When Keyword Matching is Sufficient:**
- Game-defined perception categories ("threat", "social", "routine")
- Entity-based relationships (entity IDs in metadata)
- Structured events (combat encounters, dialogue exchanges)
- NPCs writing their own memories (consistent terminology)
- No player-generated content requiring fuzzy matching

**Migration Path (lines 918-922):** See [GitHub Issue #146](https://github.com/beyond-immersion/bannou-service/issues/146) for implementation tracking.
1. `IMemoryStore` interface is already designed for swappable implementations
2. Create `EmbeddingMemoryStore` implementing the same interface
3. Configure via `BehaviorServiceConfiguration` which implementation to use
4. No changes needed to cognition pipeline or handlers

#### Cognition Templates (Section 7.5, lines 945-966)

Three embedded templates define cognition pipeline stages:

| Template | Use Case | Stages |
|----------|----------|--------|
| `humanoid_base` | Humanoid NPCs | All 5 stages (filter → memory_query → significance → storage → intention) |
| `creature_base` | Animals/creatures | Simpler (skips significance, lower attention budget, faster reactions) |
| `object_base` | Interactive objects | Minimal (just filter + intention for traps, doors, etc.) |

#### Actor State Structure (Section 10.1, lines 1597-1619)

```csharp
public class ActorState
{
    public string ActorId { get; set; }
    public string TemplateId { get; set; }
    public string Category { get; set; }

    // Behavior execution state
    public Dictionary<string, object?> Variables { get; set; }
    public string? CurrentFlowName { get; set; }
    public int CurrentActionIndex { get; set; }

    // GOAP state
    public object? CurrentGoal { get; set; }
    public object? CurrentPlan { get; set; }
    public int CurrentPlanStep { get; set; }

    // Metrics
    public long LoopIterations { get; set; }
    public DateTimeOffset LastSaveTime { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}
```

#### Key Architectural Insight - Actor State → Behavior Input (Section 4.4, lines 307-352)

The actor never emits IntentChannels directly. It emits STATE, which the behavior stack reads, which then emits IntentChannels:
- **Actor**: "Why" (feelings, goals, memories)
- **Behavior Stack**: "What" (which actions to take)
- **IntentChannels**: "How" (animation, movement execution)

### A.3 Findings from docs/plugins/ACTOR.md

**File**: `docs/plugins/ACTOR.md` (490 lines)
**Plugin**: lib-actor
**Version**: 1.0.0

#### CRITICAL: lib-actor State Stores (line 6)

**lib-actor uses 4 state stores - NONE of which is agent-memories:**
| Store | Purpose |
|-------|---------|
| `actor-templates` (Redis/MySQL) | Actor template definitions and category index |
| `actor-state` (Redis) | Runtime actor state snapshots (feelings, goals, memories) |
| `actor-pool-nodes` (Redis) | Pool node registration and health status |
| `actor-assignments` (Redis) | Actor-to-node assignment tracking |

**This confirms the investigation report: lib-actor does NOT use `agent-memories`. That store is ONLY used by lib-behavior.**

#### Dependencies (lines 16-29)

lib-actor depends on these plugins for character data:

| Dependency | Usage |
|------------|-------|
| `lib-behavior` (`IBehaviorClient`) | Loading compiled ABML behavior documents |
| `lib-character-personality` (`ICharacterPersonalityClient`) | Loading personality traits for behavior context |
| `lib-character-history` (`ICharacterHistoryClient`) | Loading backstory for behavior context |
| `lib-character-encounter` (`ICharacterEncounterClient`) | Loading encounter history, sentiment, and has-met data |
| `lib-asset` (`IAssetClient`) | Fetching behavior YAML documents via pre-signed URLs |

**Note (lines 36-37)**: "Actor service is a terminal consumer; other services publish perceptions to it"

#### Perception & Memory Configuration (lines 153-161)

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `PerceptionFilterThreshold` | `ACTOR_PERCEPTION_FILTER_THRESHOLD` | `0.1` | Minimum urgency to process |
| `PerceptionMemoryThreshold` | `ACTOR_PERCEPTION_MEMORY_THRESHOLD` | `0.7` | Minimum urgency to store as memory |
| `ShortTermMemoryMinutes` | `ACTOR_SHORT_TERM_MEMORY_MINUTES` | `5` | High-urgency memory TTL |
| `DefaultMemoryExpirationMinutes` | `ACTOR_DEFAULT_MEMORY_EXPIRATION_MINUTES` | `60` | General memory TTL |
| `MemoryStoreMaxRetries` | `ACTOR_MEMORY_STORE_MAX_RETRIES` | `3` | Max retries for memory store operations |

#### Caching Services (lines 189-216)

**DI Services for caching character data:**
| Service | Lifetime | Role |
|---------|----------|------|
| `BehaviorDocumentCache` | Singleton | Compiled ABML document caching |
| `PersonalityCache` | Singleton | Character personality caching |

**Caching configuration:**
| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `PersonalityCacheTtlMinutes` | `ACTOR_PERSONALITY_CACHE_TTL_MINUTES` | `5` | Personality data cache lifetime |
| `EncounterCacheTtlMinutes` | `ACTOR_ENCOUNTER_CACHE_TTL_MINUTES` | `5` | Encounter data cache lifetime |
| `MaxEncounterResultsPerQuery` | `ACTOR_MAX_ENCOUNTER_RESULTS_PER_QUERY` | `50` | Query result limit |
| `QueryOptionsDefaultMaxAgeMs` | `ACTOR_QUERY_OPTIONS_DEFAULT_MAX_AGE_MS` | `5000` | Max age for cached query options |

#### ActorRunner Behavior Loop (lines 293-327)

The behavior loop builds execution scope with character data:
```
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
```

#### Perception Processing (lines 347-361)

```
urgency < 0.1 → dropped (below filter threshold)
urgency ≥ 0.7 → stored as short-term memory (5 min TTL)
0.1 ≤ urgency < 0.7 → working memory only (ephemeral)
```

#### Actor State Model (lines 385-410)

```
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
     └── Data: Dict<string, object?>
```

**Note**: The `Memories` in ActorState are stored IN the actor-state Redis store, NOT in agent-memories. These are actor-local working memories with TTL.

#### Design Considerations (lines 449-477)

Key architectural notes relevant to data access:

1. **ScheduledEventManager uses in-memory state**: Pending scheduled events stored in `ConcurrentDictionary`. Acceptable for single-instance bannou mode; pool mode distributes actors across nodes.

2. **ActorRegistry is instance-local for bannou mode**: Pool mode uses Redis-backed `ActorPoolManager`, but bannou mode uses in-memory `ConcurrentDictionary`.

3. **State persistence is periodic**: Saved every `AutoSaveIntervalSeconds` (60s default). Crash loses up to 60 seconds. Critical state publishes events immediately.

4. **Perception subscription per-character**: 100,000+ actors = 100,000+ RabbitMQ subscriptions. Pool mode distributes.

5. **Memory cleanup is per-tick**: Expired memories scanned each tick. Working memory cleared between perceptions.

### A.4 Findings from docs/plugins/BEHAVIOR.md

**File**: `docs/plugins/BEHAVIOR.md` (491 lines)
**Plugin**: lib-behavior
**Version**: 3.0.0

#### CRITICAL: State Store Ownership Documentation (lines 39-55)

The BEHAVIOR.md document explicitly acknowledges the cross-plugin store usage:

| Store | Backend | Purpose | TTL | Owner |
|-------|---------|---------|-----|-------|
| `behavior-statestore` | Redis | Behavior metadata, bundle membership, GOAP metadata | N/A | **lib-behavior** |
| `agent-memories` | Redis | Memory entries for cognition pipeline | N/A | **lib-actor (used by lib-behavior's ActorLocalMemoryStore)** |

**Line 46 is explicit**: "agent-memories | Redis | Memory entries for cognition pipeline | N/A | lib-actor (used by lib-behavior's ActorLocalMemoryStore)"

**Line 138**: "IMemoryStore (via ActorLocalMemoryStore) | Scoped | Keyword-based memory storage and retrieval (uses `agent-memories` store owned by Actor)"

#### Key Pattern Data (lines 48-54)

| Key Pattern | Store | Data Type | Purpose |
|-------------|-------|-----------|---------|
| `memory:{entityId}:{memoryId}` | agent-memories | Memory JSON | Individual memory entries per agent |
| `memory-index:{entityId}` | agent-memories | List of memory IDs | Memory index for per-entity retrieval |

**Note**: Key patterns use `entityId`, not `actorId` - confirming entity-agnostic design.

#### Dependencies (lines 16-27)

lib-behavior's dependencies for data access:
| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for behavior metadata, bundle membership, GOAP metadata |
| lib-messaging (`IMessageBus`) | Publishing behavior lifecycle events, compilation failure events, GOAP plan events |
| lib-asset (`IAssetClient`) | Storing and retrieving compiled bytecode via pre-signed URLs |

**lib-behavior does NOT depend on lib-actor** - the relationship is inverted (lib-actor depends on lib-behavior).

#### Memory Configuration (lines 103-118)

All memory configuration is in `BehaviorServiceConfiguration`:

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultMemoryLimit` | `BEHAVIOR_DEFAULT_MEMORY_LIMIT` | `100` | Maximum memory entries per actor |
| `MemoryStoreMaxRetries` | `BEHAVIOR_MEMORY_STORE_MAX_RETRIES` | `3` | Max retries for memory store operations |
| `MemoryMinimumRelevanceThreshold` | `BEHAVIOR_MEMORY_MINIMUM_RELEVANCE_THRESHOLD` | `0.1` | Minimum relevance score for memory retrieval |
| `DefaultStorageThreshold` | `BEHAVIOR_DEFAULT_STORAGE_THRESHOLD` | `0.7` | Significance score threshold for storing memories |
| `MemoryCategoryMatchWeight` | `BEHAVIOR_MEMORY_CATEGORY_MATCH_WEIGHT` | `0.3` | Memory relevance: category match weight |
| `MemoryContentOverlapWeight` | `BEHAVIOR_MEMORY_CONTENT_OVERLAP_WEIGHT` | `0.4` | Memory relevance: content keyword overlap weight |
| `MemoryMetadataOverlapWeight` | `BEHAVIOR_MEMORY_METADATA_OVERLAP_WEIGHT` | `0.2` | Memory relevance: metadata key overlap weight |
| `MemoryRecencyBonusWeight` | `BEHAVIOR_MEMORY_RECENCY_BONUS_WEIGHT` | `0.1` | Memory relevance: recency bonus (< 1 hour) |
| `MemorySignificanceBonusWeight` | `BEHAVIOR_MEMORY_SIGNIFICANCE_BONUS_WEIGHT` | `0.1` | Memory relevance: significance bonus weight |
| `MemoryKeyPrefix` | `BEHAVIOR_MEMORY_KEY_PREFIX` | `memory:` | Key prefix for memory entries |
| `MemoryIndexKeyPrefix` | `BEHAVIOR_MEMORY_INDEX_KEY_PREFIX` | `memory-index:` | Key prefix for memory index entries |

#### Cognition Pipeline Visual Aid (lines 294-322)

```
Cognition Pipeline (5 Stages)
================================

  Perceptions (from environment/sensors)
       |
       v
  [Stage 1: Attention Filter]
       |  Priority = urgency * categoryWeight
       |  Budget-limited selection
       |  Threat fast-track (urgency > 0.8 -> skip to Stage 5)
       v
  [Stage 2: Significance Assessment]
       |  Score = emotional*0.4 + goalRelevance*0.4 + relationship*0.2
       |  Above StorageThreshold (0.7)? -> Stage 3
       v
  [Stage 3: Memory Formation]
       |  StoreExperienceAsync() via IMemoryStore
       |  Keyword-based retrieval for related memories
       |  Per-entity memory limit (100 default)
       v
  [Stage 4: Goal Impact Evaluation]
       |  Check if perceptions affect current goals
       |  Determine replanning urgency
       v
  [Stage 5: Intention Formation]
       |  GOAP replanning if goals affected
       |  Fast-tracked threats arrive here directly
       |  Output: action intents on channels
```

#### Memory Relevance Scoring Visual Aid (lines 370-394)

```
Memory Relevance Scoring (Keyword-Based)
==========================================

  FindRelevantAsync(entityId, perceptions, limit)
       |
       v
  For each memory x each perception:
       |
       +--> CategoryMatch:   0.3 * (category == perception.category ? 1 : 0)
       |
       +--> ContentOverlap:  0.4 * (shared words / max word count)
       |
       +--> MetadataOverlap: 0.2 * (shared keys / max key count)
       |
       +--> RecencyBonus:    0.1 * max(0, 1 - hours_since_creation)
       |                     (only for memories < 1 hour old)
       |
       +--> SignificanceBonus: 0.1 * memory.Significance
       |
       v
  Total = sum of above components
  Filter: Total >= MinimumRelevanceThreshold (0.1)
  Sort: descending by relevance
  Take: limit
```

#### Future Work: Embedding-Based Memory Store (line 404)

"**Embedding-based memory store**: `IMemoryStore` interface is designed for swappable implementations. Only `ActorLocalMemoryStore` (keyword-based) exists. The embedding-based implementation for semantic similarity matching is documented as a future migration path in ACTOR_SYSTEM.md section 7.3."

#### Design Considerations Relevant to Data Access (lines 444-471)

1. **Memory store loads up to DefaultMemoryLimit memories** (line 444): `FindRelevantAsync` calls `GetAllAsync(entityId, _configuration.DefaultMemoryLimit, ct)` which fetches the last N entries. With default limit of 100 and 10 perceptions, this is 1000 relevance calculations per query. Older memories beyond the limit are never scored.

2. **Single Redis store for all behavior data** (line 446): Behavior metadata, bundle membership, GOAP metadata, and actor memories all share `behavior-statestore`. **Note**: This appears to be a documentation error - agent-memories is a separate store.

3. **No TTL on memory entries** (line 448): Memory entries have no expiration. Eviction is handled by the per-entity memory limit (default 100): when new memory is stored and index exceeds capacity, oldest entries are trimmed.

4. **Memory index update forces save after retry exhaustion** (line 470): If ETag-based optimistic concurrency fails 3 times, the update falls back to unconditional save, potentially losing concurrent updates.

### A.5 Findings from schemas/state-stores.yaml

**File**: `schemas/state-stores.yaml` (568 lines)
**Version**: 1.0.0
**Purpose**: Single source of truth for state store configuration

#### Actor/Behavior Section Header (lines 102-104)

```yaml
# ============================================================================
# Actor/Behavior Stores - Agent Cognition Data
# ============================================================================
```

**Note**: The section header explicitly groups these as "Actor/Behavior Stores", suggesting shared conceptual ownership.

#### agent-memories Definition (lines 106-110)

```yaml
agent-memories:
  backend: redis
  prefix: "agent:mem"
  service: Actor
  purpose: Agent memory and cognitive state
```

#### All Actor-Service Stores (lines 106-140)

| Store | Backend | Prefix | Purpose |
|-------|---------|--------|---------|
| `agent-memories` | redis | `agent:mem` | Agent memory and cognitive state |
| `actor-state` | redis | `actor:state` | Runtime actor state |
| `actor-templates` | redis | `actor:tpl` | Actor template definitions |
| `actor-instances` | redis | `actor:inst` | Active actor instance registry |
| `actor-pool-nodes` | redis | `actor:pool` | Actor pool node assignments |
| `actor-assignments` | redis | `actor:assign` | Actor-to-node assignments |

**Total**: 6 stores with `service: Actor`

#### Behavior-Service Stores (lines 142-146)

| Store | Backend | Prefix | Purpose |
|-------|---------|--------|---------|
| `behavior-statestore` | redis | `behavior` | Behavior metadata and compiled definitions |

**Total**: 1 store with `service: Behavior`

#### Key Observation

The `service:` field in state-stores.yaml is documented as (line 13):
> "service: Primary service that owns this store"

This is a **documentation/attribution** field, not a runtime access control mechanism. The StateStoreDefinitions.cs generated code makes all stores available to all services - there is no enforcement of ownership at runtime.

---

### Implementation Code Findings (lib-behavior)

### A.6 Findings from plugins/lib-behavior/Cognition/ActorLocalMemoryStore.cs

**File**: `plugins/lib-behavior/Cognition/ActorLocalMemoryStore.cs` (501 lines)
**Namespace**: `BeyondImmersion.Bannou.Behavior.Cognition`
**Plugin**: lib-behavior

#### CRITICAL: State Store Usage (line 141)

```csharp
var memoryStore = _stateStoreFactory.GetStore<Memory>(StateStoreDefinitions.AgentMemories);
```

**This is the actual line of code that accesses the AgentMemories store from lib-behavior.**

Also used at lines: 166, 179, 209, 228, 233, 263, 325, 349.

#### Configuration Source (lines 37-38, 51)

```csharp
private readonly BeyondImmersion.BannouService.Behavior.BehaviorServiceConfiguration _configuration;
```

All memory configuration comes from `BehaviorServiceConfiguration`, NOT `ActorServiceConfiguration`.

#### Entity-Agnostic Key Patterns (lines 250-254)

```csharp
private string BuildMemoryKey(string entityId, string memoryId)
    => $"{_configuration.MemoryKeyPrefix}{entityId}:{memoryId}";

private string BuildMemoryIndexKey(string entityId)
    => $"{_configuration.MemoryIndexKeyPrefix}{entityId}";
```

**Key insight**: Uses `entityId` parameter, not `actorId`. The memory store is designed to work with ANY entity that needs memories, not just actors.

#### IMemoryStore Interface Design (lines 12-34, XML doc)

```csharp
/// <para>
/// <b>Migration path</b>: The <see cref="IMemoryStore"/> interface is designed for swappable
/// implementations. An embedding-based store can be created without changes to the cognition
/// pipeline.
/// </para>
```

The implementation explicitly acknowledges:
1. MVP status with keyword matching
2. Designed for swappability
3. Migration to embeddings won't require cognition pipeline changes

#### Configuration Properties Used

From `BehaviorServiceConfiguration`:
- Line 77: `_configuration.DefaultMemoryLimit` (memory count cap per entity)
- Line 266: `_configuration.MemoryStoreMaxRetries` (optimistic concurrency retries)
- Line 251: `_configuration.MemoryKeyPrefix` (Redis key prefix)
- Line 254: `_configuration.MemoryIndexKeyPrefix` (Redis index key prefix)

From `CognitionConstants` (static, initialized from config):
- Line 95: `CognitionConstants.MemoryMinimumRelevanceThreshold`
- Line 459: `CognitionConstants.MemoryCategoryMatchWeight`
- Line 473: `CognitionConstants.MemoryContentOverlapWeight`
- Line 482: `CognitionConstants.MemoryMetadataOverlapWeight`
- Line 490: `CognitionConstants.MemoryRecencyBonusWeight`
- Line 494: `CognitionConstants.MemorySignificanceBonusWeight`

#### Memory Eviction (lines 274-281)

```csharp
// Evict oldest entries if over capacity
evictedIds = [];
if (index.Count > _configuration.DefaultMemoryLimit)
{
    var excessCount = index.Count - _configuration.DefaultMemoryLimit;
    evictedIds = index.GetRange(0, excessCount);
    index.RemoveRange(0, excessCount);
}
```

Memories are bounded per-entity (default 100). Oldest entries are evicted when limit exceeded.

### A.7 Findings from plugins/lib-behavior/Cognition/IMemoryStore.cs

**File**: `plugins/lib-behavior/Cognition/IMemoryStore.cs` (218 lines)
**Namespace**: `BeyondImmersion.Bannou.Behavior.Cognition`
**Plugin**: lib-behavior

#### Interface Definition (lines 28-88)

```csharp
public interface IMemoryStore
{
    Task<IReadOnlyList<Memory>> FindRelevantAsync(
        string entityId,
        IReadOnlyList<Perception> perceptions,
        int limit,
        CancellationToken ct);

    Task StoreExperienceAsync(
        string entityId,
        Perception perception,
        float significance,
        IReadOnlyList<Memory> context,
        CancellationToken ct);

    Task<IReadOnlyList<Memory>> GetAllAsync(
        string entityId,
        int limit,
        CancellationToken ct);

    Task RemoveAsync(
        string entityId,
        string memoryId,
        CancellationToken ct);

    Task ClearAsync(string entityId, CancellationToken ct);
}
```

**All methods use `entityId` parameter** - designed for any entity with memories, not actor-specific.

#### XML Documentation on Swappability (lines 9-27)

```csharp
/// <para>
/// This interface abstracts memory storage and retrieval, enabling different implementations:
/// <list type="bullet">
/// <item><see cref="ActorLocalMemoryStore"/>: MVP using keyword-based relevance (current)</item>
/// <item>EmbeddingMemoryStore: Future option using semantic similarity via LLM embeddings</item>
/// </list>
/// </para>
/// <para>
/// <b>Implementation Selection Criteria</b> (see ACTOR_SYSTEM.md section 7.3):
/// <list type="bullet">
/// <item>Keyword: Fast, free, transparent - good for structured game data</item>
/// <item>Embedding: Semantic similarity - needed for player content or thematic matching</item>
/// </list>
/// </para>
```

#### Memory Class (lines 93-140)

```csharp
public sealed class Memory
{
    public string Id { get; init; }
    public string EntityId { get; init; }  // NOT "ActorId"
    public string Content { get; init; }
    public string Category { get; init; }
    public float Significance { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; }
    public IReadOnlyList<string> RelatedMemoryIds { get; init; }
    public float QueryRelevance { get; set; }  // Transient, not stored
}
```

#### Perception Class (lines 145-217)

```csharp
public sealed class Perception
{
    public string Id { get; init; }
    public string Category { get; init; }  // "threat, novelty, social, routine"
    public string Content { get; init; }
    public float Urgency { get; init; }    // 0-1
    public string Source { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public IReadOnlyDictionary<string, object> Data { get; init; }
    public float Priority { get; set; }    // Calculated during attention filtering
}
```

#### Perception Categories (line 155)

Built-in categories mentioned in XML doc: `threat`, `novelty`, `social`, `routine`

#### ABML Integration (lines 188-216)

`Perception.FromDictionary()` method for creating perceptions from ABML data structures.

---

### Planning Document Findings (Future Features)

> **⚠️ Important**: The findings in A.8-A.10 describe **planned future features** that do not yet exist.
> Code samples and architecture diagrams are aspirational, not verified implementations.

### A.8 Findings from docs/planning/ABML_GOAP_EXPANSION_OPPORTUNITIES.md

**File**: `docs/planning/ABML_GOAP_EXPANSION_OPPORTUNITIES.md` (2140 lines)
**Created**: 2026-01-19, Last Updated: 2026-01-23
**Purpose**: Strategic analysis of future ABML/GOAP applications

This document provides extensive planning for how GOAP should access data from other plugins.

#### Foundational Services for Quests (Part 7, lines 945-1450)

The document identifies what data services GOAP needs access to:

**Currently Exists (Can Support Quests):**
| Service | Quest Role | Status |
|---------|-----------|--------|
| Character | Quest participants | ✅ Complete |
| Character-Personality | Capability proxy, goal weighting | ✅ Complete |
| Character-History | Backstory for quest hooks | ✅ Complete |
| Relationship | NPC relationships, quest givers | ✅ Complete |
| Analytics (Glicko-2) | Skill ratings | ✅ Complete |
| Save-Load | Quest state persistence | ✅ Complete |
| Actor/Behavior | NPC quest behaviors | ✅ Complete |

**Critical Data Services (Implemented):**
| Service | Quest Role | Status |
|---------|-----------|--------|
| lib-inventory | Quest rewards, quest items | ✅ Implemented |
| lib-currency | Currency rewards | ✅ Implemented |
| lib-character-encounter | Memorable interactions | ✅ Implemented |
| lib-contract | Agreement/milestone tracking | ✅ Implemented |
| lib-item | Item templates and instances | ✅ Implemented |

#### ABML Data Access Patterns (Part 8, lines 1651-1767)

The document shows planned ABML integration patterns for accessing these services:

**Inventory-Aware NPC Behavior (lines 1713-1731):**
```yaml
# Merchant NPC adjusts behavior based on inventory state
- cond:
    - when: "${inventory.count_by_category('weapon') < 3}"
      then:
        # Low stock - prioritize restocking
        - set:
            variable: restock_urgency
            value: 0.9
        - call: seek_supplier
```

**Currency-Driven Decision Making (lines 1739-1751):**
```yaml
# NPC evaluates whether to accept quest based on wallet
- cond:
    - when: "${wallet.gold < 50}"
      then:
        # Desperate - accept any paying work
        - set:
            variable: quest_acceptance_threshold
            value: 0.1
```

**Encounter-Based Dialogue (lines 1195-1208):**
```yaml
# Check for prior encounter
- cond:
    - when: "${encounters.has_met(npc_id)}"
      then:
        - speak:
            text: "Ah, we meet again! I remember you from ${encounters.last_context(npc_id)}."
```

**Contract-Aware GOAP (lines 1656-1690):**
```yaml
# NPC merchant brain - contract-aware goals
goals:
  fulfill_trade_contract:
    priority: 85
    conditions:
      active_contract_milestones_remaining: "== 0"

flows:
  deliver_contracted_goods:
    goap:
      preconditions:
        has_contracted_items: "== true"
        active_contract_exists: "== true"
      effects:
        active_contract_milestones_remaining: "-1"
      cost: 3
```

#### The Service Dependency Graph (Appendix C, lines 2066-2136)

Shows the layered architecture for data access:

```
BEHAVIORAL INTELLIGENCE LAYER (Actor | Behavior | ABML | GOAP)
        │ queries / drives
        ▼
APPLICATION / THIN ORCHESTRATION (lib-quest | lib-trade | lib-market)
        │ creates / manages
        ▼
CUSTODY LAYER (lib-escrow)
        │ delegates logic to
        ▼
LOGIC LAYER (lib-contract)
        │ operates on
        ▼
ASSET LAYER (lib-currency | lib-item | lib-inventory)
        │ + memory layer
        ▼
MEMORY LAYER (lib-character-encounter)
        │ references / scoped by
        ▼
ENTITY FOUNDATION LAYER (Character | Personality | History | Relationship)
        │ persisted / routed by
        ▼
INFRASTRUCTURE LAYER (State | Messaging | Mesh | Connect)
```

#### Key Architectural Pattern (lines 768-784)

The document describes a layered SDK pattern:
```
High-Level SDK (Domain Orchestrator)
  e.g., TutorialEngine, QuestGenerator
        ↓
Mid-Level SDK (Domain Types)
  e.g., TutorialTypes, QuestTemplates
        ↓
Core SDK (ABML Runtime + GOAP Engine)
  BeyondImmersion.Bannou.Behavior
```

#### Quest Service as Thin Orchestration (lines 1753-1780)

**Key finding**: Most quest features are handled by the ecosystem:

| Quest Feature | Handled By |
|---------------|------------|
| "Collect 10 wolf pelts" | lib-inventory `hasItems` query |
| "Earn 500 gold reward" | lib-currency `credit` |
| "Receive Sword of Darkness" | lib-item `createInstance` → lib-inventory `addItemToContainer` |
| "NPC remembers you helped" | lib-character-encounter `recordEncounter` |
| "Ongoing employment quest" | lib-contract (milestone-based progression) |

**The quest service primarily needs to:**
1. Define quest templates (objectives, rewards, prerequisites)
2. Track active quest instances per character
3. Validate prerequisites (query other services)
4. Distribute rewards on completion (call other services)
5. Publish events for NPC reactivity

### A.9 Findings from docs/planning/ECONOMY_CURRENCY_ARCHITECTURE.md

**File**: `docs/planning/ECONOMY_CURRENCY_ARCHITECTURE.md` (2012 lines)
**Created**: 2026-01-19, Last Updated: 2026-01-24
**Purpose**: Architecture for market, trade, NPC economic systems
**Dependencies listed**: lib-currency, lib-item, lib-inventory, lib-contract, lib-escrow, **lib-actor**, lib-analytics, **lib-behavior**

This document is critical for understanding how actors should access economic data.

#### GOAP Integration for Economic NPCs (Section 2.4, lines 445-536)

The document defines explicit world state keys and GOAP goals/actions for economic behavior:

**Economic World State Schema** (lines 450-470):
```yaml
economic_worldstate_schema:
  # Resources
  gold_reserves: decimal
  has_raw_materials: boolean
  has_finished_goods: boolean
  inventory_space_remaining: integer

  # Market knowledge
  market_price_iron: decimal
  market_supply_iron: enum    # scarce | normal | abundant
  market_demand_swords: enum

  # NPC state
  shop_is_open: boolean
  fatigue: decimal
  hunger: decimal

  # Relationships
  supplier_trust: decimal
  customer_loyalty: decimal
```

**Economic GOAP Goals** (lines 472-494):
| Goal | Priority | Key Condition |
|------|----------|---------------|
| `survive` | 100 | `hunger < 0.9`, `gold_reserves > 10` |
| `maintain_wealth` | 70 | `gold_reserves >= ${personality.greed * 500}` |
| `grow_business` | 50 | `gold_reserves >= ${previous_gold * 1.1}` |
| `restock_shop` | 60 | `has_finished_goods == true` |

**Economic GOAP Actions** (lines 496-536):
| Flow | Preconditions | Effects | Cost |
|------|---------------|---------|------|
| `buy_raw_materials` | `gold_reserves > 100`, `has_raw_materials == false`, `market_supply_iron != scarce` | `gold_reserves -= market_price`, `has_raw_materials = true` | 2 |
| `craft_goods` | `has_raw_materials == true`, `fatigue < 0.8` | `has_raw_materials = false`, `has_finished_goods = true`, `fatigue += 0.15` | 3 |
| `sell_at_market` | `has_finished_goods == true`, `market_demand_swords != low` | `has_finished_goods = false`, `gold_reserves += calculate_sale_price()` | 1 |
| `adaptive_pricing` | `has_finished_goods == true` | `listed_price = adjust_price_for_market()` | 1 |

**Key insight**: GOAP preconditions reference `${personality.*}` variables (e.g., `${personality.greed}`), showing Variable Provider integration for economic behaviors.

#### ABML Economic Action Handlers (Part 5, lines 757-828)

New handlers for economic behaviors in lib-behavior:

| Handler | Parameters | Service Integration |
|---------|------------|---------------------|
| `economy_credit` | target, targetType, currency, amount, reason | lib-currency `/currency/credit` |
| `economy_debit` | target, targetType, currency, amount, reason | lib-currency `/currency/debit` |
| `inventory_add` | target, targetType, item, quantity, origin | lib-item + lib-inventory |
| `inventory_has` | target, item, quantity | lib-inventory query (returns boolean) |
| `market_query` | realm, items[] | lib-market price queries |

**Example ABML usage** (lines 816-820):
```yaml
- cond:
    - when: "${inventory_has(character_id, 'wolf_pelt', 10)}"
      then:
        - call: complete_objective
```

#### Economic Deity (God Actor) Pattern (Section 6.4, lines 926-963)

**Critical architectural pattern**: Economic balance maintained by **specialized divine actors** - long-running Actor instances (via lib-actor) that observe analytics and spawn corrective events.

**Deity Assignment Schema** (lines 957-963):
```yaml
EconomicDeityAssignment:
  deityActorId: uuid          # The god's Actor instance (lib-actor)
  deityType: string           # "mercurius", "binbougami", etc.
  realmsManaged: [uuid]       # Which realms this god watches

  personality:
    interventionFrequency: decimal
    subtlety: decimal
    favoredTargets: enum
    chaosAffinity: decimal
```

**God Actor Data Access** (lines 1002-1027):
- Reads velocity metrics from lib-analytics
- Queries wealth distribution from lib-analytics
- Spawns events via lib-actor
- Affects NPC GOAP brains through events

**Integration Points Diagram** (lines 1219-1240):
```
Economic Deity (God Actor)
   │ Reads                          │ Spawns
   ▼                                ▼
lib-analytics          →          lib-actor
/economy/velocity                  Event dispatch
/economy/distrib
   ▲                                │
   │ Records                        │ Affects
lib-currency           ←          NPC GOAP Brains
Transactions                       React to events
```

#### Three-Tier Usage Pattern (Section 7.1, lines 1246-1276)

The economic system serves three different actor types:

| Tier | User | Data Access Pattern |
|------|------|---------------------|
| **Tier 1: Divine/System** | God actors, system processes | Full analytics, automatic interventions |
| **Tier 2: NPC Governance** | Economist NPCs, customs officials | Query what they can "see", bounded-rational decisions |
| **Tier 3: External Management** | Game server | Direct API queries for metrics |

**Quote (line 1275)**: "The plugin provides primitives and data. It doesn't enforce game-specific rules. Games decide how to use the APIs."

#### NPC Economic Profile (Section 2.3, lines 418-443)

Schema for NPC economic participation:

```yaml
NpcEconomicProfile:
  characterId: uuid         # Links to Character service
  economicRole: enum        # merchant | craftsman | farmer | consumer | none

  produces: [{ templateId, rate, skillLevel }]
  consumes: [{ templateId, rate, priority }]

  tradingPersonality:
    riskTolerance: decimal  # 0-1, affects speculation
    priceAwareness: decimal # How closely they track market
    loyaltyFactor: decimal  # Preference for repeat partners
```

**Data access implication**: Actor GOAP needs to access both `tradingPersonality` (like `${personality.*}`) and external market data.

#### Scale Considerations for 100K+ NPCs (Section 4.1, lines 680-712)

1. **Lazy Wallet Creation**: NPCs don't get wallets until they transact - lib-currency `/wallet/get-or-create`
2. **Template-Based Defaults**: NPCs derive baseline wealth from templates
3. **Tick-Based Processing**: Economic decisions run periodically, not real-time (every 5 minutes)
4. **Regional Aggregation**: NPCs share market intelligence by location (cached)

**Key finding for data access**: Economic data should be cached regionally to avoid per-NPC API calls.

### A.10 Findings from docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md

**File**: `docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md` (393 lines)
**Status**: Design / Partial Implementation
**Purpose**: Gods as Regional Watchers - domain-specific Event Actors

This document describes how Event Brain actors (gods) access data from the game world.

#### Core Concept: Gods as Long-Running Event Actors (lines 19-30)

Gods are **long-running network tasks** that:
- Start lazily when a realm they influence becomes active
- Listen to event streams for domain-relevant events
- Execute APIs (game server, other services)
- Spawn Event Agents for specific situations
- Trigger encounters for Character Agents

**Each god has:**
- A domain of responsibility (death, forest, monsters, war, commerce)
- Event stream subscriptions matching their domain
- Capabilities specific to their role
- Realms of influence where they operate

#### God Data Access: Event Subscriptions (lines 45-53)

Gods subscribe to domain-specific event topics:
```yaml
subscriptions:
  - "character.*.died"           # Any character death
  - "combat.*.fatality"          # Combat fatalities
  - "ritual.necromancy.*"        # Necromantic rituals
  - "location.graveyard.*"       # Graveyard events
  - "undead.*.spawned"           # Undead spawning
```

**Implementation** (lines 299-305):
```csharp
await _messageSubscriber.SubscribeAsync<CharacterDiedEvent>(
    "character.*.died",
    async (evt, ct) => await OnCharacterDiedAsync(evt, ct),
    filterPredicate: evt => IsInMyDomain(evt));
```

#### God Data Access: API Calls (lines 308-319)

Gods execute capabilities via existing infrastructure:
```csharp
// Spawn monsters (God of Monsters)
await _gameServerClient.SpawnMonstersAsync(region, count, tier);

// Trigger encounter (any god)
await _actorService.StartActorAsync(eventAgentTemplate, context);

// Call game server API
await _meshClient.InvokeMethodAsync<Req, Resp>("game-server", "endpoint", request);
```

#### Relationship to Existing Infrastructure (lines 364-374)

| Existing Component | Role in God Pattern |
|-------------------|---------------------|
| `ActorRunner` | Runs god actors (long-running Event Brains) |
| `IMessageBus` | Gods subscribe to domain event streams |
| Event Brain handlers | Gods use `emit_perception`, `schedule_event`, etc. |
| `ActorService.StartActorAsync` | Gods spawn Event Agents |
| **lib-mesh** | **Gods call game server APIs** |
| Encounter system | Gods trigger encounters via existing endpoints |

**Key quote (line 373)**: "No new infrastructure needed - gods are Event Brain actors using existing capabilities."

#### Two Actor Patterns (lines 258-268)

| Scenario | Pattern | Data Access |
|----------|---------|-------------|
| Monster spawning | God Pattern | Event subscription + API calls |
| Arena fight | Direct Coordinator | Known participants at spawn |
| Death events | God Pattern | Domain event subscription |
| Cutscene | Direct Coordinator | Triggered by game, known context |
| Duel between players | Direct Coordinator | Explicit trigger |

#### Domain-Specific Filtering (lines 163-198)

The document explicitly rejects generic "interestingness scoring" in favor of domain-specific filters:

**Wrong approach:**
```yaml
interestingness:
  power_proximity: 0.3
  antagonism: 0.4
  spawn_threshold: 0.7
```

**Right approach:**
```yaml
god_of_death:
  cares_about:
    - deaths (especially dramatic or witnessed)
    - necromancy
    - graveyards

god_of_commerce:
  cares_about:
    - major transactions
    - market disruptions
    - trade route events
```

**Data access implication**: Each god's evaluation logic determines what data it needs, not a generic system.

#### God Lifecycle (lines 135-161)

```
Realm Activation
       │
       ▼
God Actor Started (if has influence in realm)
  1. Subscribe to domain event streams     ← IMessageBus
  2. Query initial realm state             ← API calls via lib-mesh
  3. Enter main processing loop
       │
       ▼
Event Processing Loop (long-running)
  - Receive domain events from subscriptions
  - Evaluate against god's interests
  - Execute capabilities (spawn, trigger, call APIs)
  - Spawn Event Agents for specific situations
       │
       ▼
Realm Deactivation → God Actor Stopped
```

---

### Implementation Code Findings (lib-actor)

### A.11 Findings from plugins/lib-actor/Caching/PersonalityCache.cs

**File**: `plugins/lib-actor/Caching/PersonalityCache.cs` (220 lines)
**Namespace**: `BeyondImmersion.BannouService.Actor.Caching`
**Plugin**: lib-actor

This file shows the **actual implementation** of how actors access character data from other plugins.

#### Data Access Pattern: Generated Client + In-Memory Cache (lines 62-68, 106-111, 149-154)

```csharp
using var scope = _scopeFactory.CreateScope();
var client = scope.ServiceProvider.GetRequiredService<ICharacterPersonalityClient>();

var response = await client.GetPersonalityAsync(
    new GetPersonalityRequest { CharacterId = characterId },
    ct);
```

**Key pattern**: Uses generated service clients (`ICharacterPersonalityClient`, `ICharacterHistoryClient`) via lib-mesh, NOT direct state store access.

#### Three Data Types Cached (lines 25-27)

```csharp
private readonly ConcurrentDictionary<Guid, CachedPersonality> _personalityCache = new();
private readonly ConcurrentDictionary<Guid, CachedCombatPreferences> _combatCache = new();
private readonly ConcurrentDictionary<Guid, CachedBackstory> _backstoryCache = new();
```

| Data Type | Source Service | Method |
|-----------|----------------|--------|
| Personality traits | `ICharacterPersonalityClient` | `GetPersonalityAsync` |
| Combat preferences | `ICharacterPersonalityClient` | `GetCombatPreferencesAsync` |
| Backstory elements | `ICharacterHistoryClient` | `GetBackstoryAsync` |

#### TTL-Based Cache (lines 32-33)

```csharp
private TimeSpan CacheTtl => TimeSpan.FromMinutes(_configuration.PersonalityCacheTtlMinutes);
```

Configuration: `ACTOR_PERSONALITY_CACHE_TTL_MINUTES` (default: 5 minutes)

#### Stale-If-Error Fallback (lines 86-88, 129-131, 174-175)

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to load personality for character {CharacterId}", characterId);
    return cached?.Personality; // Return stale data if available
}
```

**Key finding**: If service call fails, returns stale cached data for resilience.

#### Cache Invalidation (lines 178-194)

```csharp
public void Invalidate(Guid characterId)
{
    _personalityCache.TryRemove(characterId, out _);
    _combatCache.TryRemove(characterId, out _);
    _backstoryCache.TryRemove(characterId, out _);
}
```

Supports per-character invalidation (for event-driven cache refresh) and full cache clear.

#### Implementation Pattern Summary

1. **Check in-memory cache** (ConcurrentDictionary with TTL)
2. **On miss: Call generated client** (via IServiceScopeFactory for scoped dependency)
3. **Cache response with TTL**
4. **On error: Return stale data if available**

This is Pattern 2 (Variable Provider with cached API calls) from the main document, implemented in practice.

### A.12 Findings from plugins/lib-actor/Runtime/PersonalityProvider.cs

**File**: `plugins/lib-actor/Runtime/PersonalityProvider.cs` (95 lines)
**Namespace**: `BeyondImmersion.BannouService.Actor.Runtime`
**Plugin**: lib-actor

This file shows how the cache bridges to ABML variable access.

#### IVariableProvider Interface Implementation (lines 15, 21)

```csharp
public sealed class PersonalityProvider : IVariableProvider
{
    public string Name => "personality";
```

This enables `${personality.*}` access in ABML expressions.

#### Initialization from Cached Response (lines 27-41)

```csharp
public PersonalityProvider(PersonalityResponse? personality)
{
    _traits = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    _version = personality?.Version ?? 0;

    if (personality?.Traits != null)
    {
        foreach (var trait in personality.Traits)
        {
            // Store both as lowercase and original enum name for flexible access
            _traits[trait.Axis.ToString()] = trait.Value;
            _traits[trait.Axis.ToString().ToLowerInvariant()] = trait.Value;
        }
    }
}
```

**Key insight**: The provider is constructed from a `PersonalityResponse` (from cache/API), not directly from state store.

#### Path Resolution (lines 44-72)

Supports multiple access patterns:
| ABML Expression | Returns |
|-----------------|---------|
| `${personality.version}` | Version counter (int) |
| `${personality.traits}` | All traits (Dictionary) |
| `${personality.traits.AGGRESSION}` | Specific trait value (float) |
| `${personality.openness}` | Direct trait access (float) |
| `${personality.OPENNESS}` | Case-insensitive direct access (float) |

#### Data Flow Summary

```
PersonalityCache (API + TTL cache)
    │
    │ GetOrLoadAsync(characterId)
    ▼
PersonalityResponse
    │
    │ new PersonalityProvider(response)
    ▼
PersonalityProvider (IVariableProvider)
    │
    │ GetValue(["openness"])
    ▼
ABML Expression: ${personality.openness}
```

This shows the complete data access chain from external service to ABML variable.

### A.13 Findings from plugins/lib-actor/Caching/EncounterCache.cs

**File**: `plugins/lib-actor/Caching/EncounterCache.cs` (323 lines)
**Namespace**: `BeyondImmersion.BannouService.Actor.Caching`
**Plugin**: lib-actor

This file shows how actors access character encounter data (relationships, memories of past interactions).

#### Four Types of Cached Encounter Data (lines 23-26)

```csharp
private readonly ConcurrentDictionary<Guid, CachedEncounterList> _encounterListCache = new();
private readonly ConcurrentDictionary<string, CachedSentiment> _sentimentCache = new();
private readonly ConcurrentDictionary<string, CachedHasMet> _hasMetCache = new();
private readonly ConcurrentDictionary<string, CachedEncounterList> _pairEncounterCache = new();
```

| Cache | Key Type | API Method | Purpose |
|-------|----------|------------|---------|
| `_encounterListCache` | `Guid` (characterId) | `QueryByCharacterAsync` | All encounters for a character |
| `_sentimentCache` | `string` (pair key) | `GetSentimentAsync` | Character A's sentiment toward B |
| `_hasMetCache` | `string` (pair key) | `HasMetAsync` | Have characters A and B ever met? |
| `_pairEncounterCache` | `string` (pair key) | `QueryBetweenAsync` | All encounters between A and B |

#### Consistent Pair Key Generation (lines 293-297)

```csharp
private static string GetPairKey(Guid charA, Guid charB)
{
    // Always put the smaller GUID first for consistent keying
    return charA < charB ? $"{charA}:{charB}" : $"{charB}:{charA}";
}
```

**Important**: Pair keys are order-independent (A→B same key as B→A), matching the bidirectional nature of "has met" checks.

#### Service Client Usage (lines 61-70, 112-121, 165-174, 218-228)

All methods use `ICharacterEncounterClient`:
```csharp
using var scope = _scopeFactory.CreateScope();
var client = scope.ServiceProvider.GetRequiredService<ICharacterEncounterClient>();

var response = await client.GetSentimentAsync(
    new GetSentimentRequest
    {
        CharacterId = characterId,
        TargetCharacterId = targetCharacterId
    },
    ct);
```

#### Configuration (lines 31, 68, 226)

- `ACTOR_ENCOUNTER_CACHE_TTL_MINUTES` (default: 5 minutes)
- `ACTOR_MAX_ENCOUNTER_RESULTS_PER_QUERY` (default: 50)

#### Same Pattern as PersonalityCache

1. Check ConcurrentDictionary cache with TTL
2. On miss: Call generated client
3. Cache response
4. On error: Return stale data

**This confirms the consistent data access pattern**: Actors access cross-plugin data via generated clients with in-memory TTL caching, not direct state store access.

### A.14 Findings from plugins/lib-actor/Runtime/ActorRunner.cs

**File**: `plugins/lib-actor/Runtime/ActorRunner.cs` (1132 lines)
**Namespace**: `BeyondImmersion.BannouService.Actor.Runtime`
**Plugin**: lib-actor

This file is the core actor runtime showing how all data access patterns come together.

#### Cache Dependencies (lines 31-36)

```csharp
private readonly IStateStore<ActorStateSnapshot> _stateStore;
private readonly IBehaviorDocumentCache _behaviorCache;
private readonly IPersonalityCache _personalityCache;
private readonly IEncounterCache _encounterCache;
private readonly IDocumentExecutor _executor;
private readonly IExpressionEvaluator _expressionEvaluator;
```

**Key finding**: ActorRunner depends on TWO cache interfaces:
- `IPersonalityCache` (personality, combat prefs, backstory)
- `IEncounterCache` (encounter list, sentiment, has-met)

#### CreateExecutionScopeAsync - The Complete Data Flow (lines 678-769)

```csharp
private async Task<VariableScope> CreateExecutionScopeAsync(CancellationToken ct)
{
    var scope = new VariableScope();

    // Agent identity (from ActorRunner internal state)
    scope.SetValue("agent", new Dictionary<string, object?>
    {
        ["id"] = ActorId,
        ["behavior_id"] = _template.BehaviorRef,
        ["character_id"] = CharacterId?.ToString(),
        ["category"] = Category,
        ["template_id"] = TemplateId.ToString()
    });

    // ... more scope setup ...

    // Load personality, combat preferences, backstory, and encounters for character-based actors
    if (CharacterId.HasValue)
    {
        var personality = await _personalityCache.GetOrLoadAsync(CharacterId.Value, ct);
        scope.RegisterProvider(new PersonalityProvider(personality));

        var combatPrefs = await _personalityCache.GetCombatPreferencesOrLoadAsync(CharacterId.Value, ct);
        scope.RegisterProvider(new CombatPreferencesProvider(combatPrefs));

        var backstory = await _personalityCache.GetBackstoryOrLoadAsync(CharacterId.Value, ct);
        scope.RegisterProvider(new BackstoryProvider(backstory));

        // Load encounter data for the character
        var encounters = await _encounterCache.GetEncountersOrLoadAsync(CharacterId.Value, ct);
        scope.RegisterProvider(new EncountersProvider(encounters));
    }
    else
    {
        // Register empty providers for non-character actors to avoid null reference issues
        scope.RegisterProvider(new PersonalityProvider(null));
        scope.RegisterProvider(new CombatPreferencesProvider(null));
        scope.RegisterProvider(new BackstoryProvider(null));
        scope.RegisterProvider(new EncountersProvider(null));
    }

    return scope;
}
```

#### Complete Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     ActorRunner.CreateExecutionScopeAsync()             │
│                                                                         │
│  1. IPersonalityCache.GetOrLoadAsync(characterId)                       │
│     └─> ICharacterPersonalityClient.GetPersonalityAsync()               │
│         └─> PersonalityResponse                                         │
│             └─> new PersonalityProvider(response)                       │
│                 └─> scope.RegisterProvider()                            │
│                     └─> ${personality.openness} in ABML                 │
│                                                                         │
│  2. IPersonalityCache.GetCombatPreferencesOrLoadAsync(characterId)      │
│     └─> ICharacterPersonalityClient.GetCombatPreferencesAsync()         │
│         └─> CombatPreferencesResponse                                   │
│             └─> new CombatPreferencesProvider(response)                 │
│                 └─> scope.RegisterProvider()                            │
│                     └─> ${combat.preferred_weapon_type} in ABML         │
│                                                                         │
│  3. IPersonalityCache.GetBackstoryOrLoadAsync(characterId)              │
│     └─> ICharacterHistoryClient.GetBackstoryAsync()                     │
│         └─> BackstoryResponse                                           │
│             └─> new BackstoryProvider(response)                         │
│                 └─> scope.RegisterProvider()                            │
│                     └─> ${backstory.origin} in ABML                     │
│                                                                         │
│  4. IEncounterCache.GetEncountersOrLoadAsync(characterId)               │
│     └─> ICharacterEncounterClient.QueryByCharacterAsync()               │
│         └─> EncounterListResponse                                       │
│             └─> new EncountersProvider(response)                        │
│                 └─> scope.RegisterProvider()                            │
│                     └─> ${encounters.count} in ABML                     │
└─────────────────────────────────────────────────────────────────────────┘
```

#### State Update Publishing (lines 894-944)

After behavior execution, state updates are published:
```csharp
await _meshClient.InvokeMethodAsync(
    targetAppId,
    "character/state-update",
    evt,
    ct);
```

State updates are routed DIRECTLY to the game server via `IMeshInvocationClient`, not via pub/sub (if source app-id is known).

#### Internal State vs External Data (lines 683-736)

The scope contains TWO types of data:
1. **Internal state** (from ActorState class): `feelings`, `goals`, `memories`, `working_memory`
2. **External data** (from caches): `personality`, `combat`, `backstory`, `encounters`

```csharp
// Internal state (ActorState)
scope.SetValue("feelings", _state.GetAllFeelings());
scope.SetValue("goals", new Dictionary<string, object?> { ... });
scope.SetValue("memories", _state.GetAllMemories().ToDictionary(...));
scope.SetValue("working_memory", _state.GetAllWorkingMemory());

// External data (via IVariableProvider)
scope.RegisterProvider(new PersonalityProvider(personality));
scope.RegisterProvider(new CombatPreferencesProvider(combatPrefs));
scope.RegisterProvider(new BackstoryProvider(backstory));
scope.RegisterProvider(new EncountersProvider(encounters));
```

---

### Additional Planning Document (Future)

### A.15 Findings from docs/planning/ITEM_SYSTEM_REFERENCE.md

**File**: `docs/planning/ITEM_SYSTEM_REFERENCE.md` (745 lines)
**Created**: 2026-01-22
**Purpose**: Path of Exile item system as complexity benchmark

This document is a reference for future item system complexity but contains relevant architectural insights.

#### Key Insight for GOAP Data Access (lines 11-21)

**Quote** (lines 16-21):
> "PoE's complexity lives **above** basic item/inventory management:
> - **lib-item** handles: templates, instances, ownership, basic state
> - **lib-inventory** handles: containers, placement, movement
> - **Future plugins** handle: affixes, crafting, sockets, influences
>
> Our foundation plugins should be **unopinionated** about what makes items complex - that's for higher-level plugins."

**Implication for actor data access**: GOAP/actors access item data through lib-item and lib-inventory APIs, not lower-level state stores. Complex item logic (affixes, sockets) lives in higher-layer plugins.

#### Future Plugin Hierarchy (lines 649-690)

Shows layered plugin architecture:
```
lib-item (foundation)
└── lib-inventory (foundation)
    └── lib-affix (builds on lib-item)
        └── lib-socket (builds on lib-item)
            └── lib-crafting (builds on lib-affix, lib-socket)
```

**Implication**: Actors needing item data should call lib-item API, which provides unified access regardless of whether items have affixes, sockets, etc.

#### Caching Strategy (lines 630-644)

```
Redis Cache:
├── affix_pool:{item_class}:{influence}:{ilvl} → Valid affixes (TTL: 1h)
├── base_types:{class} → Base type list (TTL: 24h)
├── unique_items:all → All uniques (TTL: 24h)
└── item:{id}:computed_stats → Computed item stats (invalidate on change)
```

**Note**: This is proposed for future lib-affix, not currently implemented. Shows caching pattern consistent with PersonalityCache/EncounterCache.

---

### Implementation Code Findings (Variable Providers)

### A.16 Findings from plugins/lib-actor/Runtime/EncountersProvider.cs

**File**: `plugins/lib-actor/Runtime/EncountersProvider.cs` (279 lines)
**Namespace**: `BeyondImmersion.BannouService.Actor.Runtime`
**Plugin**: lib-actor

This file shows the complete EncountersProvider implementation for ABML variable access.

#### IVariableProvider Implementation (lines 29, 37)

```csharp
public sealed class EncountersProvider : IVariableProvider
{
    public string Name => "encounters";
```

This enables `${encounters.*}` access in ABML expressions.

#### Supported ABML Paths (lines 15-28)

From XML documentation:
| ABML Expression | Returns |
|-----------------|---------|
| `${encounters.recent}` | List of recent encounters |
| `${encounters.count}` | Total encounter count |
| `${encounters.grudges}` | Characters with sentiment < -0.5 |
| `${encounters.allies}` | Characters with sentiment > 0.5 |
| `${encounters.has_met.{characterId}}` | Boolean - whether met character |
| `${encounters.sentiment.{characterId}}` | Float - sentiment toward character |
| `${encounters.last_context.{characterId}}` | String - last encounter context |
| `${encounters.last_emotion.{characterId}}` | String - last emotional impact |
| `${encounters.encounter_count.{characterId}}` | Int - count with character |
| `${encounters.dominant_emotion.{characterId}}` | String - dominant emotion |

#### Constructor Parameters (lines 46-56)

```csharp
public EncountersProvider(
    EncounterListResponse? encounters,
    Dictionary<Guid, SentimentResponse>? sentiments = null,
    Dictionary<Guid, HasMetResponse>? hasMet = null,
    Dictionary<Guid, EncounterListResponse>? pairEncounters = null)
```

**Note**: Can be pre-loaded with sentiment, has-met, and pair encounter data for characters the NPC knows about. This allows lazy loading of specific character data only when needed.

#### Path Resolution Example (lines 98-135)

```csharp
// Handle ${encounters.has_met.{characterId}}
if (firstSegment.Equals("has_met", StringComparison.OrdinalIgnoreCase))
{
    return _hasMet.TryGetValue(characterId, out var hasMet) && hasMet.HasMet;
}

// Handle ${encounters.sentiment.{characterId}}
if (firstSegment.Equals("sentiment", StringComparison.OrdinalIgnoreCase))
{
    return _sentiments.TryGetValue(characterId, out var sentiment) ? sentiment.Sentiment : 0.0f;
}
```

#### Computed Properties: Grudges and Allies (lines 211-240)

```csharp
// Grudges: sentiment < -0.5
private List<Dictionary<string, object?>> GetGrudges()
{
    return _sentiments
        .Where(kv => kv.Value.Sentiment < -0.5f)
        .Select(kv => new Dictionary<string, object?>
        {
            ["character_id"] = kv.Key.ToString(),
            ["sentiment"] = kv.Value.Sentiment,
            ["encounter_count"] = kv.Value.EncounterCount,
            ["dominant_emotion"] = kv.Value.DominantEmotion?.ToString()
        })
        .ToList();
}

// Allies: sentiment > 0.5
private List<Dictionary<string, object?>> GetAllies()
{
    return _sentiments
        .Where(kv => kv.Value.Sentiment > 0.5f)
        // ... same projection
}
```

**Key insight**: The provider computes derived properties (grudges, allies) from cached sentiment data. GOAP behaviors can use `${encounters.grudges}` without additional API calls.

#### Complete Variable Provider Pattern Summary

All four providers follow the same pattern:

| Provider | Name | Source Cache | Source Service |
|----------|------|--------------|----------------|
| `PersonalityProvider` | `personality` | `IPersonalityCache` | `ICharacterPersonalityClient` |
| `CombatPreferencesProvider` | `combat` | `IPersonalityCache` | `ICharacterPersonalityClient` |
| `BackstoryProvider` | `backstory` | `IPersonalityCache` | `ICharacterHistoryClient` |
| `EncountersProvider` | `encounters` | `IEncounterCache` | `ICharacterEncounterClient` |

### A.17 Findings from plugins/lib-actor/Runtime/BackstoryProvider.cs

**File**: `plugins/lib-actor/Runtime/BackstoryProvider.cs` (150 lines)
**Namespace**: `BeyondImmersion.BannouService.Actor.Runtime`
**Plugin**: lib-actor

This file shows the BackstoryProvider implementation for ABML variable access.

#### IVariableProvider Implementation (lines 15, 22)

```csharp
public sealed class BackstoryProvider : IVariableProvider
{
    public string Name => "backstory";
```

This enables `${backstory.*}` access in ABML expressions.

#### Supported ABML Paths (lines 13, 66-101)

| ABML Expression | Returns |
|-----------------|---------|
| `${backstory.elements}` | All elements |
| `${backstory.elements.TRAUMA}` | All elements of type TRAUMA |
| `${backstory.origin}` | Value of first ORIGIN element |
| `${backstory.fear}` | Value of first FEAR element |
| `${backstory.goal}` | Value of first GOAL element |
| `${backstory.origin.key}` | Key of ORIGIN element |
| `${backstory.origin.value}` | Value of ORIGIN element |
| `${backstory.origin.strength}` | Strength of ORIGIN element |
| `${backstory.origin.relatedentityid}` | Related entity ID |

#### Element Type Support (from character-history schema)

Nine backstory element types:
- `ORIGIN` - Where character came from
- `OCCUPATION` - Current or past profession
- `TRAINING` - Skills/abilities learned
- `TRAUMA` - Negative experiences
- `ACHIEVEMENT` - Positive accomplishments
- `SECRET` - Hidden knowledge
- `GOAL` - Character motivations
- `FEAR` - What character avoids
- `BELIEF` - Core values

#### Data Structure (lines 17-19)

```csharp
private readonly Dictionary<string, BackstoryElement> _elementsByType;      // First of each type
private readonly Dictionary<string, List<BackstoryElement>> _elementGroupsByType; // All of each type
private readonly List<BackstoryElement> _allElements;                       // Complete list
```

**Key insight**: Provider supports both "first element of type" (for simple access like `${backstory.origin}`) and "all elements of type" (for iteration like `${backstory.elements.TRAUMA}`).

---

## Appendix B: Summary of Confirmed Patterns

### B.1 Cross-Plugin Data Access Pattern (CONFIRMED)

```
External Plugin Service
    │ GetXxxAsync() via lib-mesh
    ▼
Generated Client (IXxxClient)
    │ Call via IServiceScopeFactory
    ▼
Cache Layer (ConcurrentDictionary + TTL)
    │ PersonalityCache, EncounterCache
    ▼
Variable Provider (IVariableProvider)
    │ PersonalityProvider, BackstoryProvider, etc.
    ▼
ABML Expression Resolution
    │ ${personality.openness}, ${encounters.grudges}
    ▼
Behavior Execution
```

### B.2 Confirmed Rules

1. **Actors do NOT access other plugins' state stores directly**
2. **Actors use generated service clients via lib-mesh**
3. **Caching is done in-memory with TTL (5 minutes default)**
4. **Stale-if-error fallback for resilience**
5. **Variable Providers bridge cached responses to ABML expressions**
6. **The `agent-memories` store is correctly used by lib-behavior, not lib-actor**
7. **The `service:` field in state-stores.yaml is documentation only, not access control**

### B.3 Files That Implement This Pattern

| File | Role |
|------|------|
| `lib-actor/Caching/PersonalityCache.cs` | Cache for personality/combat/backstory |
| `lib-actor/Caching/EncounterCache.cs` | Cache for encounters/sentiment/has-met |
| `lib-actor/Runtime/PersonalityProvider.cs` | `${personality.*}` provider |
| `lib-actor/Runtime/CombatPreferencesProvider.cs` | `${combat.*}` provider |
| `lib-actor/Runtime/BackstoryProvider.cs` | `${backstory.*}` provider |
| `lib-actor/Runtime/EncountersProvider.cs` | `${encounters.*}` provider |
| `lib-actor/Runtime/ActorRunner.cs` | Orchestrates loading and scope creation |
| `lib-behavior/Cognition/ActorLocalMemoryStore.cs` | Memory storage (uses agent-memories store) |

---

## 8. Architectural Corrections Required {#corrections}

> **Added**: 2026-02-05
> **Status**: Analysis complete, implementation pending
> **Severity**: Foundational architecture violation

### 8.1 Design Intent: Actor as a Game Foundation Service

The original design intent for the Actor plugin was to be a **Layer 2 Foundational Service** - a generic "cloud brain infrastructure" that can execute any behavior without first-hand knowledge of game-specific features. The Actor service should be:

1. **A generic network task executor** - capable of running long-term objectives in the service network
2. **A cloud-brain for entities** - providing autonomous behavior execution for characters, NPCs, and event orchestrators
3. **Game-agnostic** - the Actor plugin should know how to execute ABML behaviors, not what game features exist

The key principle is **dependency inversion**: Actor provides interfaces (`IVariableProvider`), and higher-layer plugins register implementations. The ABML behaviors themselves are game-specific (they reference `${personality.*}`, `${quest.*}`, etc.), but Actor executes them without understanding what those namespaces mean.

### 8.2 Current Architecture Violations

**The Actor plugin currently violates the service hierarchy by having hardcoded knowledge of game features.**

#### Violation 1: Hardcoded Provider Implementations

The following files in `lib-actor/Runtime/` directly import and use L3/L4 service types:

| File | Imports | Layer Violation |
|------|---------|-----------------|
| `PersonalityProvider.cs` | `BeyondImmersion.BannouService.CharacterPersonality` | L4 → L3 |
| `CombatPreferencesProvider.cs` | `BeyondImmersion.BannouService.CharacterPersonality` | L4 → L3 |
| `BackstoryProvider.cs` | `BeyondImmersion.BannouService.CharacterHistory` | L4 → L3 |
| `EncountersProvider.cs` | `BeyondImmersion.BannouService.CharacterEncounter` | L4 → L3 |
| `QuestProvider.cs` | `BeyondImmersion.BannouService.Quest` | L4 → L4 (circular) |

#### Violation 2: Hardcoded Provider Loading in ActorRunner

`lib-actor/Runtime/ActorRunner.cs` lines 749-777 contain hardcoded logic to load and register all five providers:

```csharp
// CURRENT (WRONG): Actor has hardcoded knowledge of game features
if (CharacterId.HasValue)
{
    var personality = await _personalityCache.GetOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new PersonalityProvider(personality));

    var combatPrefs = await _personalityCache.GetCombatPreferencesOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new CombatPreferencesProvider(combatPrefs));

    var backstory = await _personalityCache.GetBackstoryOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new BackstoryProvider(backstory));

    var encounters = await _encounterCache.GetEncountersOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new EncountersProvider(encounters));

    var quests = await _questCache.GetActiveQuestsOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new QuestProvider(quests));
}
```

#### Violation 3: Caches with Game-Specific Knowledge

The cache implementations in `lib-actor/Caching/` also have direct knowledge of L3/L4 services:
- `PersonalityCache.cs` - knows about `ICharacterPersonalityClient`
- `EncounterCache.cs` - knows about `ICharacterEncounterClient`
- `QuestCache.cs` - knows about `IQuestClient`

### 8.3 Correct Architecture: Provider Registration Pattern

The correct architecture follows dependency inversion: **higher-layer services register their providers with Actor**.

#### Interface Design (in lib-actor or lib-behavior, L2)

```csharp
/// <summary>
/// Factory interface for creating variable providers for an entity.
/// Higher-layer services implement this to register their data with Actor.
/// </summary>
public interface IVariableProviderFactory
{
    /// <summary>Namespace this factory provides (e.g., "personality", "quest").</summary>
    string ProviderName { get; }

    /// <summary>Creates a provider for the given entity.</summary>
    Task<IVariableProvider> CreateProviderAsync(Guid entityId, CancellationToken ct);
}
```

#### Registration by Higher-Layer Services (L3/L4)

Each game-feature plugin would register its provider factory on startup:

```csharp
// In lib-character-personality (L3) - registers itself with Actor (L2)
public class CharacterPersonalityServiceEvents : IEventConsumer
{
    private readonly IActorProviderRegistry _providerRegistry;

    public Task OnStartupAsync(CancellationToken ct)
    {
        // Register personality provider factory
        _providerRegistry.RegisterFactory(new PersonalityProviderFactory(_client));
        _providerRegistry.RegisterFactory(new CombatPreferencesProviderFactory(_client));
        return Task.CompletedTask;
    }
}

// In lib-quest (L4) - registers itself with Actor (L2)
public class QuestServiceEvents : IEventConsumer
{
    public Task OnStartupAsync(CancellationToken ct)
    {
        _providerRegistry.RegisterFactory(new QuestProviderFactory(_client));
        return Task.CompletedTask;
    }
}
```

#### ActorRunner Would Query Registered Factories

```csharp
// CORRECT: Actor knows nothing about specific game features
private async Task<ExecutionScope> BuildExecutionScopeAsync(CancellationToken ct)
{
    var scope = new ExecutionScope();

    // Query all registered provider factories
    foreach (var factory in _providerRegistry.GetFactories())
    {
        var provider = await factory.CreateProviderAsync(EntityId, ct);
        scope.RegisterProvider(provider);
    }

    return scope;
}
```

### 8.4 Why This Matters: Templates and Data Access

The original design envisioned that **game-feature plugins would provide both**:
1. **Data Providers** - how to access the data (`PersonalityProviderFactory`)
2. **Templates** - ABML behavior patterns that use that data (`narrative-god.yaml`)

Example: The Storyline plugin (L4) would:
1. Register a `StorylineProviderFactory` that exposes `${storyline.*}` variables
2. Provide behavior templates like `regional-watcher.yaml` that use those variables
3. Games would extend those templates with game-specific preferences

The Actor plugin just executes whatever behaviors it's given, using whatever providers are registered. It's like the JVM - it doesn't know about Java programs, it just executes bytecode.

### 8.5 Migration Path

#### Phase 1: Define Provider Registry Interface (Non-Breaking)

1. Create `IActorProviderRegistry` in lib-actor
2. Create `IVariableProviderFactory` in lib-actor (or lib-behavior for ABML integration)
3. Implement registry storage in ActorService

#### Phase 2: Move Provider Implementations (Breaking)

1. Move `PersonalityProvider.cs` → `lib-character-personality/Providers/`
2. Move `CombatPreferencesProvider.cs` → `lib-character-personality/Providers/`
3. Move `BackstoryProvider.cs` → `lib-character-history/Providers/`
4. Move `EncountersProvider.cs` → `lib-character-encounter/Providers/`
5. Move `QuestProvider.cs` → `lib-quest/Providers/`

Each moved provider becomes a factory registered on service startup.

#### Phase 3: Update ActorRunner (Breaking)

1. Remove hardcoded provider loading from `ActorRunner.cs`
2. Replace with factory-based provider creation
3. Remove direct imports of L3/L4 service types

#### Phase 4: Move Caches (Breaking)

1. Move `PersonalityCache` → `lib-character-personality/Caching/`
2. Move `EncounterCache` → `lib-character-encounter/Caching/`
3. Move `QuestCache` → `lib-quest/Caching/`

Each service owns its own cache and exposes it via the provider factory.

### 8.6 Resource Plugin Integration

The Resource plugin (L1) was designed as a low-level archival system that Actor (as L2) can consume. This allows:

1. **Compressed character data** to be retrieved without Actor knowing about CharacterPersonality/CharacterHistory
2. **Archived realm data** to be retrieved without Actor knowing about Realm services
3. **Generic data providers** that work with Resource's opaque archive format

When a character is compressed, higher-layer services register their data with Resource. Actor can later query Resource for archived character data without knowing what services contributed that data.

### 8.7 Files Requiring Changes

| Current Location | Target Location | Change Type |
|------------------|-----------------|-------------|
| `lib-actor/Runtime/PersonalityProvider.cs` | `lib-character-personality/Providers/PersonalityProvider.cs` | Move |
| `lib-actor/Runtime/CombatPreferencesProvider.cs` | `lib-character-personality/Providers/CombatPreferencesProvider.cs` | Move |
| `lib-actor/Runtime/BackstoryProvider.cs` | `lib-character-history/Providers/BackstoryProvider.cs` | Move |
| `lib-actor/Runtime/EncountersProvider.cs` | `lib-character-encounter/Providers/EncountersProvider.cs` | Move |
| `lib-actor/Runtime/QuestProvider.cs` | `lib-quest/Providers/QuestProvider.cs` | Move |
| `lib-actor/Caching/PersonalityCache.cs` | `lib-character-personality/Caching/PersonalityCache.cs` | Move |
| `lib-actor/Caching/EncounterCache.cs` | `lib-character-encounter/Caching/EncounterCache.cs` | Move |
| `lib-actor/Caching/QuestCache.cs` | `lib-quest/Caching/QuestCache.cs` | Move |
| `lib-actor/Runtime/ActorRunner.cs` | N/A | Refactor (remove hardcoded providers) |
| `lib-actor/ActorService.cs` | N/A | Add `IActorProviderRegistry` |
| NEW: `lib-actor/Registry/IVariableProviderFactory.cs` | N/A | Create interface |
| NEW: `lib-actor/Registry/IActorProviderRegistry.cs` | N/A | Create interface |
| NEW: `lib-actor/Registry/ActorProviderRegistry.cs` | N/A | Create implementation |

### 8.8 Related Issues

- **Issue #147**: "Implement Phase 2 Variable Providers (Currency, Inventory, Relationship)" - This issue describes adding MORE hardcoded providers. It should be updated to follow the correct registration pattern.
- **Issue #145**: "Implement spatial context providers for actor decision-making" - Same concern.
- **Issue #148**: "Implement GoapWorldStateProvider for batched external state queries" - Places this in lib-behavior, which is closer to correct (lib-behavior is L2-adjacent).

A new GitHub issue should be created to track this architectural correction work.

---

*This document establishes the foundational patterns for actor data access. Future implementations should reference these patterns to maintain architectural consistency.*
