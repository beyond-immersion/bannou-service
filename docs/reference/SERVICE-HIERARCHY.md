# Bannou Service Hierarchy

> **Version**: 2.7
> **Last Updated**: 2026-02-11
> **Scope**: All Bannou service plugins and their inter-dependencies

This document defines the authoritative service dependency hierarchy for Bannou. Services are organized into five layers based on their **domain** (application vs game) and **optionality** (foundation vs feature). Dependencies may only flow downward.

---

## Why This Matters

Bannou's plugin architecture allows any service to technically call any other service via lib-mesh. This flexibility is powerful but dangerous - without discipline, services become tangled in circular dependencies that:

1. **Break optional deployment**: If ServiceA depends on ServiceB depends on ServiceA, neither can be disabled independently
2. **Invert ownership**: Core entities shouldn't need to know about every consumer
3. **Create fragile systems**: Changes ripple unpredictably through the dependency graph
4. **Prevent scaling**: Tightly coupled services can't be distributed effectively
5. **Leak data unexpectedly**: Feature services calling foundation services is safe; the reverse exposes internal data

The hierarchy ensures that:
- Infrastructure is always available
- Foundations are stable and predictable
- Features can be enabled/disabled without breaking foundations
- Domain boundaries (app vs game) are respected

---

## The Golden Rule

> **A service may ONLY depend on services in lower layers. Dependencies on higher layers are FORBIDDEN.**

"Depend on" means:
- Injecting a service client (e.g., `ICharacterClient`)
- Calling a service via lib-mesh or `IServiceNavigator`
- Requiring a service to be running for correct operation

"Depend on" does NOT mean:
- Publishing events that higher-layer services consume (this is fine - it's the consumer's dependency, not the publisher's)
- Being referenced by higher-layer services (that's their dependency, not yours)

---

## The Six Layers

```
┌─────────────────────────────────────────────────────────────┐
│ L5: EXTENSIONS (Third-party plugins, meta-services)         │
│ Depends on: L0, L1, L2*, L3*, L4*   (* = graceful degrade)  │
├─────────────────────────────────────────────────────────────┤
│ L4: GAME FEATURES (Optional game-specific capabilities)     │
│ Depends on: L0, L1, L2, L3*, L4*    (* = graceful degrade)  │
├─────────────────────────────────────────────────────────────┤
│ L3: APP FEATURES (Optional non-game capabilities)           │
│ Depends on: L0, L1, L3*             (* = graceful degrade)  │
├─────────────────────────────────────────────────────────────┤
│ L2: GAME FOUNDATION (Required for game deployments)         │
│ Depends on: L0, L1, L2                                      │
├─────────────────────────────────────────────────────────────┤
│ L1: APP FOUNDATION (Required for ANY deployment)            │
│ Depends on: L0, L1                                          │
├─────────────────────────────────────────────────────────────┤
│ L0: INFRASTRUCTURE (Plugins loaded first)                   │
│ Depends on: Nothing                                         │
└─────────────────────────────────────────────────────────────┘

     ▲ Dependencies flow DOWNWARD only
     │ Higher layers depend on lower layers
     │ Lower layers NEVER depend on higher layers
```

---

## Layer 0: Infrastructure (Loaded First)

These are infrastructure plugins that load before all other plugins and provide core primitives. The **required** components cannot be disabled and have no service-layer dependencies. The **optional** component (telemetry) can be disabled but when enabled, it loads FIRST so that required components can use its instrumentation.

| Plugin | Role | Required |
|--------|------|----------|
| **telemetry** | Distributed tracing, metrics via `ITelemetryProvider` | **Optional** |
| **state** | State persistence (Redis, MySQL) via `IStateStoreFactory` | **Required** |
| **messaging** | Pub/sub messaging (RabbitMQ) via `IMessageBus` | **Required** |
| **mesh** | Service-to-service invocation via generated clients | **Required** |

**Load Order** (enforced by PluginLoader):
1. **telemetry** (-1) - First when enabled, so `ITelemetryProvider` is available
2. **state** (0) - State management foundation
3. **messaging** (1) - Depends on state being available
4. **mesh** (2) - May depend on messaging for events

**Rules**:
- Every plugin implicitly depends on Layer 0
- Layer 0 components have no dependencies on any service layer
- **Required** components (state, messaging, mesh) are always available - startup fails if missing
- **Optional** component (telemetry) can be disabled via `TELEMETRY_SERVICE_DISABLED=true`

**Use Case**: "I want to build HTTP API-based cloud services with Dapr-like communication primitives."

---

## Layer 1: App Foundation (Required for ANY Deployment)

These services provide the core application infrastructure that ANY Bannou deployment needs - authentication, real-time communication, authorization, and agreement management. They are **not game-specific** and would be required even for a non-game cloud service.

| Service | Role |
|---------|------|
| **account** | User account management (CRUD, OAuth linking) |
| **auth** | Authentication, JWT tokens, session management |
| **chat** | Universal typed message channel primitives |
| **connect** | WebSocket gateway, binary protocol routing |
| **permission** | RBAC permission management, capability manifests |
| **contract** | Binding agreements, term validation, prebound template execution |
| **resource** | Reference tracking, cleanup coordination for foundational resources |

**Rules**:
- May depend on Layer 0 and other L1 services
- May NOT depend on L2, L3, or L4
- Must always be enabled - other layers assume these exist
- Missing L1 service = crash (not graceful degradation)

**Use Case**: "I need a real-time authenticated cloud service with authorization and automated resource management."

**Why Resource is L1**: Resource provides the machinery for reference tracking and cleanup coordination. Higher-layer services (L3/L4) publish reference events when they create/delete references to foundational resources (L2). This service maintains reference counts and coordinates cleanup callbacks, enabling safe deletion of foundational resources without hierarchy violations.

**Why Contract is L1**: Contract has zero dependencies on other plugins (only infrastructure libs) and provides reusable FSM + consent flow machinery that any layer can leverage. For example, Escrow (L4) uses Contract under-the-hood for its state machine and multi-party consent logic. This makes Contract application-level infrastructure rather than game-specific.

---

## Layer 2: Game Foundation (Required for Game Deployments)

These services provide the core game infrastructure - worlds, characters, species, items, inventories. They are **game-specific** but foundational - every game deployment needs them, and Game Features (L4) depend on them heavily.

| Service | Role |
|---------|------|
| **game-service** | Registry of available games/applications |
| **realm** | Persistent world management |
| **character** | Game world characters |
| **species** | Character type definitions |
| **location** | Hierarchical places within realms |
| **relationship** | Entity-to-entity relationships and type taxonomy |
| **subscription** | Account-to-game access mapping |
| **currency** | Multi-currency economy management |
| **item** | Item templates and instances |
| **inventory** | Container and slot management |
| **game-session** | Active game session management |
| **actor** | NPC brains, behavior execution runtime |
| **quest** | Objective-based progression system |
| **seed** | Generic progressive growth primitives |
| **collection** | Universal content unlock and archive system |
| **transit** | Geographic connectivity graph, transit modes, journey tracking |
| **worldstate** | Per-realm game clock, calendar system, temporal event broadcasting |

**Rules**:
- May depend on Layer 0, Layer 1, and other L2 services
- No internal sub-layering enforced within L2
- May NOT depend on L3 or L4
- When Game Features (L4) are enabled, all L2 services must be running
- Missing L2 service when L4 is enabled = crash (not graceful degradation)

**Use Case**: "I need a game backend with characters, worlds, economies, and items."

**The Character Service Rule**:
> Character is a foundational entity. It knows about realms and species (what a character IS), but knows nothing about encounters, history, personality, or actors (what references a character). Those are L4 concerns.

**The Quest Service Rule**:
> Quest is a foundational entity. It knows about objectives, rewards, and milestones (what a quest IS), but is agnostic to prerequisite sources. L4 services (skills, magic, achievements) implement `IPrerequisiteProviderFactory` to provide prerequisite validation without Quest depending on them.

---

## Layer 3: App Features (Optional Non-Game Capabilities)

These services provide optional capabilities that enhance ANY Bannou deployment - asset storage, deployment orchestration, documentation. They are **not game-specific** and useful for both game and non-game deployments.

| Service | Role |
|---------|------|
| **asset** | Binary asset storage (MinIO/S3), pre-signed URLs |
| **orchestrator** | Environment management, deployment orchestration |
| **documentation** | Knowledge base for users, developers, and AI agents |
| **website** | Public web interface (registration, news, status) |
| **voice** | Voice room coordination (P2P and scaled tier) |
| **broadcast** | Streaming platform integration |

**Rules**:
- May depend on Layer 0 and Layer 1
- May depend on other Layer 3 services (with graceful degradation)
- May NOT depend on L2 or L4 (if it does, it becomes L4 by definition)
- Missing L3 service = graceful degradation (reduced functionality, not crash)
- No service in L1 or L2 may require L3 for correct operation

**Use Case**: "I want observability, asset storage, and deployment management for my cloud service."

**Critical Rule**: If an App Feature starts depending on Game Foundation (L2), it **becomes** a Game Feature (L4). Reclassify it. This prevents game data from leaking into application-level services.

**Website Note**: Website is intentionally L3 to prevent it from accessing game data (characters, realms, etc.). Any game-specific portal functionality should be a separate L4 service.

---

## Layer 4: Game Features (Optional Game-Specific Capabilities)

These services provide optional game-specific capabilities - NPCs, matchmaking, achievements. They depend on Game Foundation (L2) and may optionally use App Features (L3).

| Service | Role |
|---------|------|
| **analytics** | Event aggregation, statistics, skill ratings (see note below) |
| **behavior** | ABML compiler, GOAP planner |
| **puppetmaster** | Dynamic behavior orchestration, regional watchers, encounter coordination |
| **mapping** | Spatial data management |
| **scene** | Hierarchical composition storage |
| **matchmaking** | Player matching and queue management |
| **leaderboard** | Competitive rankings |
| **achievement** | Trophy/achievement system |
| **save-load** | Game state persistence |
| **music** | Procedural music generation |
| **escrow** | Multi-party asset exchanges |
| **character-personality** | Personality traits, combat preferences |
| **character-history** | Historical events, backstory |
| **character-encounter** | Memorable interactions tracking |
| **realm-history** | Realm historical events, lore |
| **license** | Grid-based progression boards via itemized contracts |
| **storyline** | Seeded narrative generation from compressed archives |
| **divine** | Pantheon management, divinity economy, blessing orchestration |
| **faction** | Seed-based faction growth, norms, territory, guild memberships |
| **gardener** | Player experience orchestration, garden lifecycle |
| **obligation** | Contract-aware obligation tracking for NPC cognition |
| **status** | Unified entity effects query layer (buffs, blessings, penalties) |
| **affix** | Item modifier definition and generation |
| **agency** | Guardian spirit progressive agency, UX manifest engine |
| **arbitration** | Dispute resolution orchestration |
| **character-lifecycle** | Generational cycle orchestration, genetic heritage |
| **craft** | Recipe-based crafting orchestration |
| **disposition** | Emotional synthesis and aspirational drives |
| **dungeon** | Dungeon-as-actor lifecycle orchestration |
| **environment** | Weather simulation, temperature modeling, ecological resources |
| **ethology** | Species-level behavioral archetype registry, nature resolution |
| **hearsay** | Social information propagation, belief formation |
| **lexicon** | Structured world knowledge ontology, concept decomposition |
| **loot** | Loot table management and generation |
| **market** | Marketplace orchestration (auctions, NPC vendors) |
| **organization** | Legal entity management |
| **procedural** | Houdini-based procedural 3D asset generation |
| **showtime** | In-game streaming metagame |
| **trade** | Economic logistics, trade routes, supply/demand dynamics |
| **utility** | Infrastructure network topology, flow calculation, coverage cascading |
| **workshop** | Time-based automated production, lazy evaluation |

**Analytics Note**: Analytics is classified as L4 not because it *depends* on game services, but because it *observes* them via event subscriptions. It subscribes to events from L2 services (game-session) and L4 services (character-history, realm-history) for aggregation. Unlike typical L4 services:
- Analytics does NOT invoke L2/L4 service APIs (it only consumes events)
- Analytics should NOT be called by L1/L2/L3 services (it's the most optional plugin)
- If Analytics is disabled, no other service should break
- Only L4 services like Matchmaking have legitimate reasons to call Analytics APIs

**Rules**:
- May depend on Layer 0, Layer 1, and Layer 2 (required - crash if missing)
- May depend on Layer 3 (optional - graceful degradation if missing)
- May depend on other Layer 4 services (optional - graceful degradation if missing)
- Missing L4 service = graceful degradation for L4 consumers
- L4 requires ALL of L1 and L2 to be running

**Use Case**: "I want NPCs, matchmaking, and achievements for my game."

---

## Layer 5: Extensions (Third-Party & Meta-Services)

This layer is reserved for third-party plugins and internal meta-services that need maximum flexibility. Extensions load last and can depend on any layer.

| Service | Role |
|---------|------|
| *(reserved)* | Third-party game plugins |
| *(reserved)* | Custom integrations |
| *(reserved)* | Meta-services spanning multiple domains |

**Rules**:
- May depend on any layer (L0, L1, L2, L3, L4)
- Loads after all core plugins
- Must handle absence of L3/L4 dependencies gracefully
- Should not be depended upon by L0-L4 services (they can't know about extensions)

**Use Case**: "I'm building a third-party plugin that extends Bannou with custom game mechanics."

**Why L5 Exists**: Third-party developers shouldn't need to understand the full hierarchy to build plugins. By placing all extensions at L5, they can safely depend on any core service without accidentally creating hierarchy violations. The validator will catch any issues.

---

## Dependency Rules Summary

| Layer | Can Depend On | Cannot Depend On | If Missing |
|-------|---------------|------------------|------------|
| L0 Infrastructure (required) | - | Everything | Crash |
| L0 Infrastructure (telemetry) | - | Everything | Graceful degradation (NullTelemetryProvider) |
| L1 App Foundation | L0, L1 | L2, L3, L4, L5 | Crash |
| L2 Game Foundation | L0, L1, L2 | L3, L4, L5 | Crash (when L4 enabled) |
| L3 App Features | L0, L1, L3* | L2, L4, L5 | Graceful degradation |
| L4 Game Features | L0, L1, L2, L3*, L4* | L5 | Graceful degradation |
| L5 Extensions | L0, L1, L2, L3*, L4* | - | Graceful degradation |

\* Must handle absence gracefully - check availability, use events, or provide reduced functionality.

---

## Schema-First Layer Declaration

Service layers are declared in the API schema using the `x-service-layer` extension attribute. This follows the schema-first principle: the schema is the source of truth, and the code generator applies the layer to the generated `[BannouService]` attribute.

```yaml
# In location-api.yaml
openapi: 3.0.0
info:
  title: Location Service API
  version: 1.0.0
x-service-layer: GameFoundation  # Declares this as L2

servers:
  - url: http://localhost:5012
```

**Generated attribute**:
```csharp
[BannouService("location", typeof(ILocationService),
    lifetime: ServiceLifetime.Scoped,
    layer: ServiceLayer.GameFoundation)]
```

**Valid layer values**: `Infrastructure` (L0), `AppFoundation` (L1), `GameFoundation` (L2), `AppFeatures` (L3), `GameFeatures` (L4, default if omitted), `Extensions` (L5). See [SCHEMA-RULES.md](SCHEMA-RULES.md#x-service-layer-service-hierarchy-layer) for complete documentation and the [Quick Reference](#quick-reference-all-services-by-layer) for which services belong to each layer.

**Plugin Load Order**: PluginLoader reads the `ServiceLayer` from each service's `[BannouService]` attribute and sorts plugins accordingly:
1. L0 plugins first (with internal ordering: telemetry → state → messaging → mesh)
2. L1 plugins second (alphabetical within layer)
3. L2, L3, L4, L5 in order (alphabetical within each layer)

This ensures that when a service's constructor runs, all lower-layer services are already registered in DI.

---

## Deployment Modes

The layer system enables meaningful deployment presets:

```bash
# Minimal cloud service (non-game)
BANNOU_ENABLE_APP_FOUNDATION=true   # L1
BANNOU_ENABLE_APP_FEATURES=true     # L3
# Result: L0 infra + account, auth, connect, permission, contract + asset, orchestrator, etc.
# No game concepts - useful for any real-time cloud service

# Minimal game backend (foundations only)
BANNOU_ENABLE_APP_FOUNDATION=true   # L1
BANNOU_ENABLE_GAME_FOUNDATION=true  # L2
# Result: L1 + realm, character, species, location, currency, item, etc.
# Core game entities without optional features

# Full game deployment
BANNOU_ENABLE_APP_FOUNDATION=true   # L1
BANNOU_ENABLE_GAME_FOUNDATION=true  # L2
BANNOU_ENABLE_APP_FEATURES=true     # L3
BANNOU_ENABLE_GAME_FEATURES=true    # L4
# Result: Everything enabled
```

**Enforcement Rules**:
- `GAME_FOUNDATION=true` requires `APP_FOUNDATION=true`
- `GAME_FEATURES=true` requires `APP_FOUNDATION=true` AND `GAME_FOUNDATION=true`
- `APP_FEATURES=true` requires `APP_FOUNDATION=true` (L2 optional)
- Individual plugin ENVs (`{SERVICE}_ENABLED`) still work for fine-grained control

---

## Reference Counting & Cleanup

A common need is determining if a foundational entity (like Character) can be safely deleted by checking if higher-layer services still reference it. This creates a tension with the hierarchy rules.

### Wrong Ways

**Direct dependency** (L2 injecting L4 clients) and **subscribing to higher-layer events** (Character subscribing to `actor.created`) are both forbidden — even event subscriptions create a dependency on the higher-layer's event schema.

### The Right Way (Foundational Service Defines Its Own Contract)

The foundational service defines an event it **consumes** without knowing who publishes to it. Higher-layer services publish to that topic.

**Step 1: Foundational service defines the event it accepts**

```yaml
# character-events.yaml (L2 defines its own event schema)
x-event-subscriptions:
  - topic: character.reference.registered    # Character's own topic
    event: CharacterReferenceRegisteredEvent
  - topic: character.reference.unregistered
    event: CharacterReferenceUnregisteredEvent
# CharacterReferenceRegisteredEvent has: characterId, sourceType, sourceId
```

**Step 2: Higher-layer services publish to the foundational service's topic**

```csharp
// Actor service (L4) - publishes TO Character's event topic
public class ActorService
{
    public async Task CreateActorAsync(CreateActorRequest request, ...)
    {
        // Create actor...

        // Actor knows about Character (correct direction!)
        await _messageBus.PublishAsync("character.reference.registered",
            new CharacterReferenceRegisteredEvent
            {
                CharacterId = request.CharacterId,
                SourceType = "actor",
                SourceId = actor.ActorId
            });
    }
}
```

**Step 3: Foundational service consumes its own event**

```csharp
// Character service (L2) - consumes its OWN event definition
public class CharacterServiceEvents
{
    [EventSubscription("character.reference.registered")]
    public async Task HandleReferenceRegistered(CharacterReferenceRegisteredEvent evt, ...)
    {
        // Character doesn't know WHO sent this - just that a reference was registered
        await IncrementRefCount(evt.CharacterId, evt.SourceType, evt.SourceId);
    }
}
```

**Key Insight**: Character defines `character.reference.registered` in its own schema. It has no knowledge of Actor, Encounter, or any other service. Those services have the dependency - they must know about Character's event contract to publish to it.

---

## Variable Provider Factory Pattern

When a foundational service (L2) needs data from higher-layer services (L4) at runtime, it cannot inject those service clients (that would be a hierarchy violation). Instead, the foundational service defines an interface that higher-layer services implement and register.

### The Actor Runtime Example

The Actor service (L2) executes behavior models that need character data (personality, combat preferences, encounter history) owned by L4 services. Injecting L4 clients would be a hierarchy violation. Instead, use interface inversion.

**Step 1: Foundational service defines the interface it needs**

```csharp
// bannou-service/Providers/IVariableProviderFactory.cs (shared, layer-agnostic)
namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Factory for creating variable providers for behavior execution.
/// Higher-layer services implement this to provide their data to Actor (L2).
/// </summary>
public interface IVariableProviderFactory
{
    /// <summary>The variable namespace this factory provides (e.g., "personality").</summary>
    string ProviderName { get; }

    /// <summary>Creates a provider instance for the given character.</summary>
    Task<IVariableProvider> CreateAsync(Guid characterId, CancellationToken ct);
}

public interface IVariableProvider
{
    /// <summary>The variable namespace (e.g., "personality" for ${personality.*}).</summary>
    string Namespace { get; }

    /// <summary>Gets a variable value by name within this namespace.</summary>
    object? GetVariable(string name);
}
```

**Step 2: Higher-layer services implement and register the interface**

```csharp
// lib-character-personality (L4) - implements the interface
public class PersonalityProviderFactory : IVariableProviderFactory
{
    private readonly IPersonalityDataCache _cache;

    public string ProviderName => "personality";

    public async Task<IVariableProvider> CreateAsync(Guid characterId, CancellationToken ct)
    {
        var data = await _cache.GetOrLoadAsync(characterId, ct);
        return new PersonalityProvider(data);
    }
}

// In plugin registration
services.AddSingleton<IVariableProviderFactory, PersonalityProviderFactory>();
```

**Step 3: Foundational service discovers providers via DI collection**

```csharp
// lib-actor (L2) - discovers providers at runtime
public class ActorRunner
{
    private readonly IEnumerable<IVariableProviderFactory> _providerFactories;

    public ActorRunner(
        IEnumerable<IVariableProviderFactory> providerFactories,  // DI collection
        ...)
    {
        _providerFactories = providerFactories;
    }

    private async Task<ExecutionScope> CreateExecutionScopeAsync(...)
    {
        var scope = new ExecutionScope();

        // Actor doesn't know WHO provides these - just that they exist
        foreach (var factory in _providerFactories)
        {
            try
            {
                var provider = await factory.CreateAsync(CharacterId, ct);
                scope.RegisterProvider(provider);
            }
            catch (Exception ex)
            {
                // Graceful degradation - some providers may fail
                _logger.LogWarning(ex, "Failed to create {Provider}", factory.ProviderName);
            }
        }

        return scope;
    }
}
```

### Key Insights

1. **The interface lives in shared code** (bannou-service), not in Actor or the L4 services
2. **Actor depends on the interface** (allowed - it's shared code, not a service client)
3. **L4 services implement the interface** (allowed - they depend on the shared contract)
4. **DI collection injection** discovers implementations at runtime
5. **Graceful degradation** handles missing providers (L4 may not be enabled)

### When to Use This Pattern

Use Variable Provider Factory when:
- A foundational service needs data from optional higher-layer services
- The data is needed frequently during runtime execution
- Different deployments may have different providers enabled
- The foundational service shouldn't crash when providers are unavailable

### Related Pattern: Cache Ownership

When using this pattern, the cache for each data type should live with its owning service:

| Provider | Cache | Owned By |
|----------|-------|----------|
| PersonalityProvider | IPersonalityDataCache | lib-character-personality (L4) |
| CombatPreferencesProvider | IPersonalityDataCache | lib-character-personality (L4) |
| EncountersProvider | IEncounterDataCache | lib-character-encounter (L4) |
| BackstoryProvider | - (no cache) | lib-character-history (L4) |
| BehaviorDocumentCache | IBehaviorDocumentCache | lib-actor (L2) |

Cache invalidation events are handled by each owning service, not by Actor.

---

## Prerequisite Provider Factory Pattern

Quest (L2) validates prerequisites before accepting quests. Some prerequisites require data from L4 services (skills, magic, achievements). Like the Variable Provider Factory pattern above, Quest defines an interface that L4 services implement.

### The Interface

```csharp
// bannou-service/Providers/IPrerequisiteProviderFactory.cs
public interface IPrerequisiteProviderFactory
{
    /// <summary>The prerequisite namespace (e.g., "skill", "magic", "achievement")</summary>
    string ProviderName { get; }

    /// <summary>Check if character meets prerequisite</summary>
    Task<PrerequisiteResult> CheckAsync(
        Guid characterId,
        string prerequisiteCode,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct);
}

public record PrerequisiteResult(
    bool Satisfied,
    string? FailureReason,
    object? CurrentValue,
    object? RequiredValue
);
```

### Built-in vs Dynamic Prerequisites

| Category | Type | Service | How Quest Handles |
|----------|------|---------|-------------------|
| **Built-in** | `quest_completed` | Quest (L2) | Direct check of own state |
| **Built-in** | `currency` | Currency (L2) | Call `ICurrencyClient` |
| **Built-in** | `item` | Inventory (L2) | Call `IInventoryClient` |
| **Built-in** | `character_level` | Character (L2) | Call `ICharacterClient` |
| **Dynamic** | `skill` | Skills (L4) | Provider factory |
| **Dynamic** | `magic` | Magic (L4) | Provider factory |
| **Dynamic** | `achievement` | Achievement (L4) | Provider factory |

L4 implementation follows the same pattern as Variable Provider Factory: implement the interface, register via `services.AddSingleton<IPrerequisiteProviderFactory, SkillPrerequisiteProviderFactory>()`, and Quest discovers providers via `IEnumerable<IPrerequisiteProviderFactory>` injection with graceful degradation.

---

## DI Provider vs Listener: Distributed Safety (MANDATORY)

The DI inversion patterns above come in two forms: **Providers** (pull) and **Listeners** (push). They have fundamentally different behavior in distributed multi-node deployments and MUST NOT be treated as interchangeable.

### The Core Distinction

| Mechanism | Direction | Distributed? | How It Works |
|-----------|-----------|-------------|--------------|
| **DI Provider** (pull) | L4 data → L2 consumer | **Always safe** | Consumer initiates request on whichever node needs data. The provider runs co-located but reads from distributed state. |
| **DI Listener** (push) | L2 notification → L4 consumer | **Local-only fan-out** | Producer calls co-located listeners after a mutation. Only listeners on the node that processed the API request are called. Other nodes are NOT notified. |
| **Events** (IMessageBus) | Broadcast | **Always safe** | RabbitMQ delivers to all subscribers on all nodes. Genuine distributed fan-out. |

### The Safety Test

> **"If this listener fires on Node A but never fires on Node B, is the system in a consistent state?"**

- **YES → Safe**: The listener writes to distributed state (Redis/MySQL). All nodes read from the same store. Node B will see the updated state on its next read even though its listener never fired.
- **NO → Broken**: The listener updates local in-memory state. Node B's local state is stale. This creates silent inconsistencies.

### Current DI Inversion Patterns

| Pattern | Direction | Type | Distributed-Safe? | Why |
|---------|-----------|------|--------------------|-----|
| `IVariableProviderFactory` | L4→L2 pull | Provider | Always safe | Consumer initiates; reads distributed state |
| `IPrerequisiteProviderFactory` | L4→L2 pull | Provider | Always safe | Consumer initiates; reads distributed state |
| `IBehaviorDocumentProvider` | L4→L2 pull | Provider | Always safe | Consumer initiates; reads distributed state |
| `ISeededResourceProvider` | L4→L1 pull | Provider | Always safe | Consumer initiates; reads distributed state |
| `ISeedEvolutionListener` | L2→L4 push | Listener | Safe\* | \*Only when reaction writes to distributed state |
| `ICollectionUnlockListener` | L2→L4 push | Listener | Safe\* | \*Only when reaction writes to distributed state |

### Rules for Listener Patterns

1. **Listeners are an optimization, not a replacement for events.** The producing service MUST still publish broadcast events via IMessageBus. Listeners dispatch AFTER events are published.

2. **Listener reactions MUST write to distributed state.** If a listener needs to update something, that "something" must be in Redis or MySQL — never local in-memory state that other nodes also maintain.

3. **If per-node awareness is required** (e.g., invalidating a `ConcurrentDictionary` cache on every node), the consumer MUST subscribe to the broadcast event via IEventConsumer. The DI listener alone is insufficient.

4. **Document the safety envelope.** Every listener interface MUST include a `<remarks>` block in its XML documentation that warns about local-only fan-out and states the distributed safety requirements.

5. **Never assume listeners replace event subscriptions.** A consumer that needs both fast local notification AND guaranteed distributed delivery should use both: the listener for the fast path, the event subscription as the distributed guarantee.

### When to Use Each Pattern

| Need | Use |
|------|-----|
| L2 needs data from L4 at runtime | **Provider** (pull) — always safe |
| L2 wants to notify L4 of mutations | **Listener** (push) + events — listener is optimization |
| Any service needs to react to another's state changes | **Events** (IMessageBus) — distributed guarantee |
| L4 needs to invalidate local cache across all nodes | **Events only** — listeners cannot reach other nodes |

---

## Dependency Handling Patterns (MANDATORY)

The hierarchy dictates not just WHAT services can depend on, but HOW those dependencies should be handled in code. This prevents silent degradation that hides deployment configuration errors.

### Hard Dependencies (L0, L1, L2)

Dependencies on L0, L1, and L2 services are **guaranteed available** by the hierarchy. When your service runs, these MUST be running. The goal is **constructor injection** - the DI container fails at startup if not registered, catching configuration errors immediately.

> **Layer-Based Loading**: PluginLoader sorts all plugins by their `ServiceLayer` attribute before loading. This ensures that when a service's constructor runs, all services in lower layers are already registered in DI. L0 plugins have additional internal ordering (telemetry → state → messaging → mesh). Within each layer, plugins load alphabetically.

```csharp
// Constructor injection for guaranteed dependencies
// DI container fails at startup if IContractClient isn't registered
// Layer-based loading ensures L1 services are registered before L2 constructors run
public class LocationService : ILocationService
{
    private readonly IContractClient _contractClient;  // L1 - guaranteed available

    public LocationService(
        IContractClient contractClient,  // Hard dependency - will throw if missing
        ...)
    {
        _contractClient = contractClient;
    }
}

// For startup-time API calls (not just storing references), use OnRunningAsync
protected override async Task OnRunningAsync(CancellationToken ct)
{
    // Contract client is guaranteed available (L1 loaded before L2)
    await _contractClient.RegisterClauseTypeAsync(...);
}
```

**FORBIDDEN: Graceful degradation for guaranteed dependencies**

```csharp
// WRONG: GetService + null check that silently returns
var contractClient = serviceProvider.GetService<IContractClient>();
if (contractClient == null)
{
    _logger.LogDebug("Contract not available, skipping...");  // NO!
    return;  // Silent degradation hides deployment errors - THROW INSTEAD
}
```

This pattern is **forbidden** because the hierarchy guarantees L1 is running when L2 runs. Silent degradation hides deployment configuration errors — fail fast at startup instead.

### Soft Dependencies (L3, L4)

Dependencies on L3 and L4 services are **optional** - they may or may not be enabled. These MUST implement graceful degradation using runtime resolution with null checks.

```csharp
// CORRECT: Runtime resolution with graceful degradation for optional dependencies
public async Task DoSomethingAsync(...)
{
    var analyticsClient = _serviceProvider.GetService<IAnalyticsClient>();
    if (analyticsClient == null)
    {
        // L4 service may not be enabled - this is expected, not an error
        _logger.LogDebug("Analytics not enabled, skipping metrics publication");
        return;
    }
    await analyticsClient.PublishMetricsAsync(...);
}
```

### Summary Table

| Dependency Layer | DI Pattern | On Missing | Example |
|------------------|------------|------------|---------|
| L0 Infrastructure | Constructor injection | Crash at startup | `IStateStoreFactory`, `IMessageBus` |
| L1 App Foundation | Constructor injection* | Crash at startup | `IContractClient`, `IAuthClient` |
| L2 Game Foundation | Constructor injection* | Crash at startup | `IRealmClient`, `ICharacterClient` |
| L3 App Features | `GetService<T>()` + null check | Graceful degradation | `IAssetClient`, `IOrchestratorClient` |
| L4 Game Features | `GetService<T>()` + null check | Graceful degradation | `IAnalyticsClient`, `IActorClient` |

\* Layer-based plugin loading ensures lower-layer services are registered before higher-layer constructors run. Constructor injection is now the standard pattern for L0/L1/L2 dependencies.

### Why This Distinction Matters

- **L0/L1/L2 are deployment prerequisites** — a missing dependency is a deployment bug. Fail-fast at startup gives a clear error instead of silent partial operation.
- **L3/L4 are truly optional** — these can be disabled independently. Silent degradation is expected and correct for optional services.

---

## Enforcement

### During Development

Before adding a service client dependency, ask:
1. What layer is my service in?
2. What layer is the target service in?
3. Is the target layer lower? If not, **STOP**.

### Guardrail Effect

Layer assignments prevent scope creep. If Website (L3) wants to display character data, the answer is "no" - L3 cannot depend on L2. This is intentional:
- Prevents data leakage
- Forces clean domain boundaries
- Makes agents pause before adding inappropriate dependencies

### In Code Review

Check `using` statements and constructor parameters for service clients. Flag any that violate the hierarchy.

### In Deep Dive Documents

Each plugin's deep dive document must list its dependencies. Cross-reference with this hierarchy to catch violations.

### Automated Validation (ServiceHierarchyValidator)

The `ServiceHierarchyValidator` in `test-utilities/` provides automated hierarchy enforcement:

**How It Works**:
1. **Reflection-based discovery**: Scans all loaded assemblies for `[BannouService]` attributes
2. **Builds layer cache**: Maps service names to their declared `ServiceLayer`
3. **Analyzes constructors**: Finds all `I*Client` parameters (service client dependencies)
4. **Checks violations**: Verifies each dependency is in an allowed layer

Validation rules match the [Dependency Rules Summary](#dependency-rules-summary) table.

**Usage in Unit Tests**:
```csharp
[Fact]
public void CharacterService_RespectsDependencyHierarchy()
{
    ServiceHierarchyValidator.ValidateServiceHierarchy<CharacterService>();
}
```

**Runtime Validation**: PluginLoader calls `GetHierarchyViolations()` during startup and logs violations as errors. This catches third-party plugins that violate the hierarchy.

**Key Insight**: The validator uses reflection to read `[BannouService]` attributes directly - no hardcoded registry. This ensures the validator always matches what's actually in the code.

---

## Exceptions

There are no exceptions. If you think you need one, you're likely:

1. **Solving the wrong problem**: Maybe the higher-layer service should publish events instead
2. **Missing an abstraction**: Maybe a reference registry or mediator pattern is needed
3. **In the wrong layer**: Maybe your service belongs in a different layer
4. **Conflating domains**: Maybe you need a game-specific version of an app feature

Discuss with the team before violating the hierarchy. Document any approved exceptions here (there should be very few, if any).

---

## Quick Reference: All Services by Layer

| Layer | Services |
|-------|----------|
| **L0** | state, messaging, mesh (required); telemetry (optional)† |
| **L1** | account, auth, chat, connect, permission, contract, resource |
| **L2** | game-service, realm, character, species, location, relationship, subscription, currency, item, inventory, game-session, actor, quest, seed, collection, transit, worldstate |
| **L3** | asset, orchestrator, documentation, website, voice, broadcast |
| **L4** | analytics*, behavior, puppetmaster, mapping, scene, matchmaking, leaderboard, achievement, save-load, music, escrow, character-personality, character-history, character-encounter, character-lifecycle, realm-history, license, storyline, divine, faction, gardener, obligation, status, affix, agency, arbitration, craft, disposition, dungeon, environment, ethology, hearsay, lexicon, loot, market, organization, procedural, showtime, trade, utility, workshop |
| **L5** | (reserved for third-party plugins and internal meta-services) |

† Telemetry is the only optional L0 component. When enabled, it loads FIRST so infrastructure plugins can use `ITelemetryProvider` for instrumentation. When disabled, they receive `NullTelemetryProvider`.

\* Analytics is L4 for event consumption reasons only - it observes but doesn't invoke game services. See L4 section for details.

**Not in hierarchy** (shared libraries, not runtime services):
- **common**: Shared type definitions only (lib-common)
- **testing**: Test infrastructure (lib-testing)
- **\*.tests**: Test projects, not runtime services

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-02-01 | 1.0 | Initial version with 7 sub-layers |
| 2026-02-02 | 2.0 | Simplified to 5-layer model with domain/optionality separation |
| 2026-02-03 | 2.1–2.4 | Telemetry to L0; dependency handling patterns (hard vs soft); schema-first layer declaration with `x-service-layer`; L5 Extensions layer; ServiceHierarchyValidator; layer-based plugin loading |
| 2026-02-06 | 2.5 | Moved Actor from L4 to L2. Added Variable Provider Factory pattern. |
| 2026-02-11 | 2.6 | Moved Collection from L4 to L2 (ICollectionUnlockListener DI pattern). |
| 2026-02-11 | 2.7 | Added "DI Provider vs Listener: Distributed Safety" section. |

---

*This document is referenced by the development tenets. Violations must be explicitly approved.*
