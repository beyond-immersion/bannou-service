# Bannou Service Hierarchy

> **Version**: 2.0
> **Last Updated**: 2026-02-02
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

## The Five Layers

```
┌─────────────────────────────────────────────────────────────┐
│ L4: GAME FEATURES (Optional game-specific capabilities)     │
│ Depends on: L0, L1, L2, L3*, L4*    (* = graceful degrade)  │
├─────────────────────────────────────────────────────────────┤
│ L3: APP FEATURES (Optional non-game capabilities)           │
│ Depends on: L0, L1, L3*             (* = graceful degrade)  │
├─────────────────────────────────────────────────────────────┤
│ L2: GAME FOUNDATION (Required for game deployments)         │
│ Depends on: L0, L1                                          │
├─────────────────────────────────────────────────────────────┤
│ L1: APP FOUNDATION (Required for ANY deployment)            │
│ Depends on: L0                                              │
├─────────────────────────────────────────────────────────────┤
│ L0: INFRASTRUCTURE (Always on - not plugins)                │
│ Depends on: Nothing                                         │
└─────────────────────────────────────────────────────────────┘

     ▲ Dependencies flow DOWNWARD only
     │ Higher layers depend on lower layers
     │ Lower layers NEVER depend on higher layers
```

---

## Layer 0: Infrastructure (Always On)

These are not services - they are infrastructure libraries that all plugins depend on implicitly. They cannot be disabled and have no service-layer dependencies.

| Component | Role |
|-----------|------|
| **lib-state** | State persistence (Redis, MySQL) via `IStateStoreFactory` |
| **lib-messaging** | Pub/sub messaging (RabbitMQ) via `IMessageBus` |
| **lib-mesh** | Service-to-service invocation via generated clients |

**Rules**:
- Every plugin implicitly depends on Layer 0
- Layer 0 components have no dependencies on any service layer
- These are always available - no need to check for availability

**Use Case**: "I want to build cloud services with Dapr-like communication primitives."

---

## Layer 1: App Foundation (Required for ANY Deployment)

These services provide the core application infrastructure that ANY Bannou deployment needs - authentication, real-time communication, authorization, and agreement management. They are **not game-specific** and would be required even for a non-game cloud service.

| Service | Role |
|---------|------|
| **account** | User account management (CRUD, OAuth linking) |
| **auth** | Authentication, JWT tokens, session management |
| **connect** | WebSocket gateway, binary protocol routing |
| **permission** | RBAC permission management, capability manifests |
| **contract** | Binding agreements, lifecycle management, template execution |
| **resource** | Reference tracking, cleanup coordination for foundational resources |

**Rules**:
- May depend on Layer 0 and other L1 services
- May NOT depend on L2, L3, or L4
- Must always be enabled - other layers assume these exist
- Missing L1 service = crash (not graceful degradation)

**Use Case**: "I need a real-time authenticated cloud service with authorization and agreement management."

**Why Resource is L1**: Resource provides the machinery for reference tracking and cleanup coordination. Higher-layer services (L3/L4) publish reference events when they create/delete references to foundational resources (L2). This service maintains reference counts and coordinates cleanup callbacks, enabling safe deletion of foundational resources without hierarchy violations.

**Why Contract is L1**: Contract has zero dependencies on other plugins (only infrastructure libs) and provides reusable FSM + consent flow machinery that any layer can leverage. For example, Escrow (L4) uses Contract under-the-hood for its state machine and multi-party consent logic. This makes Contract application-level infrastructure rather than game-specific.

---

## Layer 2: Game Foundation (Required for Game Deployments)

These services provide the core game infrastructure - worlds, characters, species, economies, items. They are **game-specific** but foundational - every game deployment needs them, and Game Features (L4) depend on them heavily.

| Service | Role |
|---------|------|
| **game-service** | Registry of available games/applications |
| **realm** | Persistent world management |
| **character** | Game world characters |
| **species** | Character type definitions |
| **location** | Hierarchical places within realms |
| **relationship-type** | Relationship taxonomy definitions |
| **relationship** | Entity-to-entity relationships |
| **subscription** | Account-to-game access mapping |
| **currency** | Multi-currency economy management |
| **item** | Item templates and instances |
| **inventory** | Container and slot management |

**Rules**:
- May depend on Layer 0, Layer 1, and other L2 services
- No internal sub-layering enforced within L2
- May NOT depend on L3 or L4
- When Game Features (L4) are enabled, all L2 services must be running
- Missing L2 service when L4 is enabled = crash (not graceful degradation)

**Use Case**: "I need a game backend with characters, worlds, economies, and items."

**The Character Service Rule**:
> Character is a foundational entity. It knows about realms and species (what a character IS), but knows nothing about encounters, history, personality, or actors (what references a character). Those are L4 concerns.

---

## Layer 3: App Features (Optional Non-Game Capabilities)

These services provide optional capabilities that enhance ANY Bannou deployment - observability, asset storage, deployment orchestration. They are **not game-specific** and useful for both game and non-game deployments.

| Service | Role |
|---------|------|
| **asset** | Binary asset storage (MinIO/S3), pre-signed URLs |
| **telemetry** | Distributed tracing, metrics, log correlation |
| **orchestrator** | Environment management, deployment orchestration |
| **documentation** | Knowledge base for AI agents |
| **website** | Public web interface (registration, news, status) |
| **testing** | Test harness for service validation |
| **mesh** | HTTP API for service discovery (wrapper around lib-mesh) |
| **messaging** | HTTP API for pub/sub (wrapper around lib-messaging) |
| **state** | HTTP API for state stores (wrapper around lib-state) |

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

These services provide optional game-specific capabilities - NPCs, matchmaking, voice chat, achievements. They depend on Game Foundation (L2) and may optionally use App Features (L3).

| Service | Role |
|---------|------|
| **actor** | NPC brains, behavior execution |
| **analytics** | Event aggregation, statistics, skill ratings (see note below) |
| **behavior** | ABML compiler, GOAP planner |
| **mapping** | Spatial data management |
| **scene** | Hierarchical composition storage |
| **matchmaking** | Player matching and queue management |
| **leaderboard** | Competitive rankings |
| **achievement** | Trophy/achievement system |
| **voice** | WebRTC voice communication |
| **save-load** | Game state persistence |
| **music** | Procedural music generation |
| **game-session** | Active game session management |
| **escrow** | Multi-party asset exchanges |
| **character-personality** | Personality traits, combat preferences |
| **character-history** | Historical events, backstory |
| **character-encounter** | Memorable interactions tracking |
| **realm-history** | Realm historical events, lore |

**Analytics Note**: Analytics is classified as L4 not because it *depends* on game services, but because it *observes* them via event subscriptions. It subscribes to events from L2/L4 services (game-session, character-history, realm-history) for aggregation. Unlike typical L4 services:
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

**Use Case**: "I want NPCs, matchmaking, voice chat, and achievements for my game."

---

## Dependency Rules Summary

| Layer | Can Depend On | Cannot Depend On | If Missing |
|-------|---------------|------------------|------------|
| L0 Infrastructure | - | Everything | N/A (always on) |
| L1 App Foundation | L0, L1 | L2, L3, L4 | Crash |
| L2 Game Foundation | L0, L1, L2 | L3, L4 | Crash (when L4 enabled) |
| L3 App Features | L0, L1, L3* | L2, L4 | Graceful degradation |
| L4 Game Features | L0, L1, L2, L3*, L4* | - | Graceful degradation |

\* Must handle absence gracefully - check availability, use events, or provide reduced functionality.

---

## Deployment Modes

The layer system enables meaningful deployment presets:

```bash
# Minimal cloud service (non-game)
BANNOU_ENABLE_APP_FOUNDATION=true   # L1
BANNOU_ENABLE_APP_FEATURES=true     # L3
# Result: account, auth, connect, permission, contract, asset, analytics, etc.
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

### The Wrong Way (Dependency Inversion)

```csharp
// IN CHARACTER SERVICE (L2) - FORBIDDEN!
public class CharacterService
{
    private readonly IActorClient _actorClient;           // NO! Actor is L4
    private readonly ICharacterEncounterClient _encounter; // NO! Encounter is L4

    public async Task<bool> CanDeleteCharacter(Guid id)
    {
        // Character shouldn't know about its consumers!
        var actors = await _actorClient.ListActorsAsync(...);
        var encounters = await _encounterClient.QueryByCharacterAsync(...);
    }
}
```

### Also Wrong (Subscribing to Higher-Layer Events)

```yaml
# character-events.yaml - STILL WRONG!
x-event-subscriptions:
  - topic: actor.created        # Character now "knows about" Actor - VIOLATION
  - topic: encounter.created    # Character now "knows about" Encounter - VIOLATION
```

Even with events, if Character subscribes to `actor.created`, Character has a dependency on Actor's event schema. The dependency is inverted.

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

components:
  schemas:
    CharacterReferenceRegisteredEvent:
      type: object
      required: [characterId, sourceType, sourceId]
      properties:
        characterId:
          type: string
          format: uuid
        sourceType:
          type: string
          description: Type of entity holding the reference (e.g., "actor", "encounter")
        sourceId:
          type: string
          format: uuid
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

### Alternative: Cleanup Responsibility at Consumer Layer

Higher-layer services handle their own cleanup when foundational entities are deleted:

```csharp
// Actor service (L4) - cleans up when character is deleted
public class ActorServiceEvents
{
    [EventSubscription("character.deleted")]  // Actor depends on Character's event - correct!
    public async Task HandleCharacterDeleted(CharacterDeletedEvent evt, ...)
    {
        await CleanupActorsForCharacter(evt.CharacterId);
    }
}
```

This is simpler but means the foundational service can't gate deletion on active references.

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
| **L0** | lib-state, lib-messaging, lib-mesh |
| **L1** | account, auth, connect, permission, contract, resource |
| **L2** | game-service, realm, character, species, location, relationship-type, relationship, subscription, currency, item, inventory |
| **L3** | asset, telemetry, orchestrator, documentation, website, testing, mesh, messaging, state |
| **L4** | actor, analytics*, behavior, mapping, scene, matchmaking, leaderboard, achievement, voice, save-load, music, game-session, escrow, character-personality, character-history, character-encounter, realm-history |

\* Analytics is L4 for event consumption reasons only - it observes but doesn't invoke game services. See L4 section for details.

**Not in hierarchy** (shared libraries, not runtime services):
- **lib-common**: Shared type definitions only
- **lib-*.tests**: Test projects, not runtime services

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-02-01 | 1.0 | Initial version with 7 sub-layers |
| 2026-02-02 | 2.0 | Simplified to 5-layer model with domain separation (app/game) and optionality (foundation/feature) |

---

*This document is referenced by the development tenets. Violations must be explicitly approved.*
