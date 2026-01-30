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
| **Real-time state** (positions, health) | Event subscription + local cache | High-frequency updates, eventual consistency OK |

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

### Current Variable Providers (lib-behavior)

ABML documents access character data through registered Variable Providers:

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
| Real-time, high-velocity | Event + local cache | entity positions |

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

### Pattern 4: Real-Time State (Events + Cache)

**Use case**: High-velocity data like entity positions, combat state, active effects

**Implementation**: Event subscriptions populate local cache

```csharp
public class ActorPerceptionCache : IEventConsumer
{
    private readonly ConcurrentDictionary<string, EntityPosition> _positions = new();

    public void RegisterSubscriptions(IMessageSubscriber subscriber)
    {
        subscriber.SubscribeAsync<EntityMovedEvent>("entity.moved", HandleEntityMoved);
        subscriber.SubscribeAsync<CombatStateChangedEvent>("combat.state.changed", HandleCombatState);
    }

    private Task HandleEntityMoved(EntityMovedEvent evt, CancellationToken ct)
    {
        _positions[evt.EntityId] = new EntityPosition(evt.X, evt.Y, evt.Z, evt.Timestamp);
        return Task.CompletedTask;
    }

    // GOAP/ABML reads from cache (eventual consistency OK for positions)
    public EntityPosition? GetPosition(string entityId) =>
        _positions.TryGetValue(entityId, out var pos) ? pos : null;
}
```

**Rationale**: Real-time data changes too frequently for API polling, eventual consistency is acceptable, events ensure freshness.

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

Does this data change frequently (>1/sec)?
    YES → Pattern 4: Event + Cache
    NO  ↓

Is consistency critical (currency, ownership)?
    YES → Pattern 3: API with Batching
    NO  ↓

Is this character attribute data for ABML?
    YES → Pattern 2: Variable Provider
    NO  → Pattern 3: API with Batching
```

### Variable Provider Registration

All Variable Providers should be registered in lib-behavior's DI setup:

```csharp
// lib-behavior service registration
services.AddSingleton<IVariableProvider, PersonalityVariableProvider>();
services.AddSingleton<IVariableProvider, CombatPreferencesVariableProvider>();
services.AddSingleton<IVariableProvider, BackstoryVariableProvider>();
services.AddSingleton<IVariableProvider, CurrencyVariableProvider>();      // NEW
services.AddSingleton<IVariableProvider, InventoryVariableProvider>();     // NEW
services.AddSingleton<IVariableProvider, RelationshipVariableProvider>();  // NEW
```

### Cache TTL Guidelines

| Data Type | Recommended TTL | Rationale |
|-----------|-----------------|-----------|
| Personality traits | 5 minutes | Changes very slowly |
| Backstory elements | 10 minutes | Nearly immutable |
| Currency balances | 30 seconds | May change from player actions |
| Inventory contents | 1 minute | Changes less frequently than currency |
| Relationships | 5 minutes | Changes via explicit actions only |
| Positions (event-driven) | N/A (real-time) | Overwritten on each event |

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

### Phase 2: Variable Providers (Short-term)

1. Implement `CurrencyVariableProvider` for ABML access to wallet balances
2. Implement `InventoryVariableProvider` for item/container queries
3. Implement `RelationshipVariableProvider` for social state
4. Add cache TTL configuration to `BehaviorServiceConfiguration`

### Phase 3: GOAP Integration (Medium-term)

1. Create `GoapWorldStateProvider` with batched external queries
2. Integrate with existing GOAP planner in lib-behavior
3. Add configuration for which data sources to include in world state

### Phase 4: Event-Driven Cache (Future)

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

*This document establishes the foundational patterns for actor data access. Future implementations should reference these patterns to maintain architectural consistency.*
