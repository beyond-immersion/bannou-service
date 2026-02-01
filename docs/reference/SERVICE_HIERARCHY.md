# Bannou Service Hierarchy

> **Version**: 1.0
> **Last Updated**: 2026-02-01
> **Scope**: All Bannou service plugins and their inter-dependencies

This document defines the authoritative service dependency hierarchy for Bannou. Services are organized into layers, and **dependencies may only flow downward** - a service may depend on services in its own layer or lower layers, never on services in higher layers.

---

## Why This Matters

Bannou's plugin architecture allows any service to technically call any other service via lib-mesh. This flexibility is powerful but dangerous - without discipline, services become tangled in circular dependencies that:

1. **Break optional deployment**: If ServiceA depends on ServiceB depends on ServiceA, neither can be disabled independently
2. **Invert ownership**: Core entities shouldn't need to know about every consumer
3. **Create fragile systems**: Changes ripple unpredictably through the dependency graph
4. **Prevent scaling**: Tightly coupled services can't be distributed effectively

The hierarchy ensures that:
- Infrastructure is always available
- Observability is truly optional
- Core entities remain stable foundations
- Extended services can be added/removed without breaking foundations

---

## The Golden Rule

> **A service may ONLY depend on services in its own layer or lower layers. Dependencies on higher layers are FORBIDDEN.**

"Depend on" means:
- Injecting a service client (e.g., `ICharacterClient`)
- Calling a service via lib-mesh
- Requiring a service to be running for correct operation

"Depend on" does NOT mean:
- Publishing events that higher-layer services consume (this is fine - it's the consumer's dependency, not the publisher's)
- Being referenced by higher-layer services (that's their dependency, not yours)

---

## Layer 0: Infrastructure (Always On)

These are not services in the traditional sense - they are infrastructure libraries that all services depend on. They cannot be disabled and have no service-layer dependencies.

| Component | Role |
|-----------|------|
| **lib-state** | State persistence (Redis, MySQL) via `IStateStoreFactory` |
| **lib-messaging** | Pub/sub messaging (RabbitMQ) via `IMessageBus` |
| **lib-mesh** | Service-to-service invocation via generated clients |

**Rules**:
- Every service implicitly depends on Layer 0
- Layer 0 components have no dependencies on any service layer
- These are always available - no need to check for availability

---

## Layer 1: Observability & Scalability (Optional)

These services provide operational capabilities but are entirely optional. The system functions correctly without them - they enhance observability and enable advanced deployment patterns.

| Service | Role | Dependencies |
|---------|------|--------------|
| **telemetry** | Distributed tracing, metrics, log correlation | Layer 0 only |
| **orchestrator** | Environment management, service orchestration | Layer 0 only |
| **analytics** | Event aggregation, statistics, skill ratings | Layer 0 only |

**Rules**:
- No service in any layer may require Layer 1 services for correct operation
- Layer 1 services may subscribe to events from any layer (consuming, not depending)
- Disabling all Layer 1 services must not break any other service
- These services are "observe and enhance" - never "required for operation"

**Anti-patterns**:
```csharp
// FORBIDDEN: Foundational service depending on observability
public class CharacterService
{
    private readonly IAnalyticsClient _analytics; // NO! Character doesn't need Analytics
}

// ALLOWED: Analytics consuming Character events (Analytics depends on Character, not vice versa)
public class AnalyticsServiceEvents
{
    [EventSubscription("character.updated")]
    public async Task HandleCharacterUpdated(...) { } // Fine - Analytics depends on Character
}
```

---

## Layer 2: Foundational Services

These are the core entity services that form the stable foundation of Bannou. They represent fundamental concepts (accounts, characters, realms) that higher layers build upon.

### Layer 2a: Root Foundations (No Layer 2 Dependencies)

These services have no dependencies on other Layer 2 services - they are the absolute foundation.

| Service | Role | Layer 2 Dependencies |
|---------|------|---------------------|
| **account** | User account management | None |
| **game-service** | Game/application registry | None |
| **relationship-type** | Relationship taxonomy definitions | None |
| **species** | Species definitions | None |
| **connect** | WebSocket gateway, session management | None |

### Layer 2b: Core Foundations (Minimal Layer 2 Dependencies)

These services depend on one or two Layer 2a services.

| Service | Role | Layer 2 Dependencies |
|---------|------|---------------------|
| **auth** | Authentication, session tokens | account |
| **realm** | Persistent world management | game-service |
| **relationship** | Entity-to-entity relationships | relationship-type |
| **permission** | RBAC permission management | connect (sessions) |

### Layer 2c: Extended Foundations

These services depend on multiple Layer 2 services but remain foundational.

| Service | Role | Layer 2 Dependencies |
|---------|------|---------------------|
| **character** | Game world characters | realm, species |
| **location** | Hierarchical locations | realm |
| **currency** | Multi-currency management | game-service |
| **contract** | Binding agreements | None (parties are polymorphic) |

**Rules for Layer 2**:
- May depend on Layer 0 (infrastructure) and Layer 1 (observability) freely
- May depend on other Layer 2 services as documented above
- May NOT depend on Layer 3 or higher
- Should be stable - changes here affect everything above

**The Character Service Rule**:
> Character is a foundational entity. It knows about realms and species (what a character IS), but knows nothing about encounters, history, personality, or actors (what references a character). Those are Layer 3 concerns.

---

## Layer 3: Extended Services

These services extend foundational concepts with additional capabilities. They depend heavily on Layer 2 but can be enabled/disabled independently.

### Character Extensions

| Service | Role | Dependencies |
|---------|------|--------------|
| **character-personality** | Personality traits, combat preferences | character |
| **character-history** | Historical events, backstory | character |
| **character-encounter** | Memorable interactions | character |

### Realm Extensions

| Service | Role | Dependencies |
|---------|------|--------------|
| **realm-history** | Realm historical events, lore | realm |

### Session & Subscription

| Service | Role | Dependencies |
|---------|------|--------------|
| **subscription** | User subscriptions to games | account, game-service |
| **game-session** | Active game sessions | connect, subscription |

### Economy Extensions

| Service | Role | Dependencies |
|---------|------|--------------|
| **escrow** | Multi-party asset exchanges | currency, contract (optional) |
| **inventory** | Container management | item |
| **item** | Item templates and instances | game-service |

**Rules for Layer 3**:
- May depend on Layer 0, 1, and 2 freely
- May depend on other Layer 3 services where logical (e.g., inventory → item)
- May NOT depend on Layer 4 or higher
- Can be disabled without breaking Layer 2 services

---

## Layer 4: Application Services

These services provide game-specific or advanced functionality. They sit at the top of the hierarchy and may depend on anything below.

| Service | Role | Key Dependencies |
|---------|------|------------------|
| **actor** | NPC brains, behavior execution | character, behavior |
| **behavior** | ABML compiler, GOAP planner | game-service |
| **mapping** | Spatial data management | realm, location |
| **scene** | Hierarchical composition storage | game-service, asset |
| **matchmaking** | Player matching | game-session |
| **leaderboard** | Competitive rankings | game-service, analytics (optional) |
| **achievement** | Trophy/achievement system | game-service, analytics (optional) |
| **voice** | WebRTC voice communication | game-session |
| **save-load** | Game state persistence | asset |
| **asset** | Binary asset storage | game-service |
| **music** | Procedural music generation | None (pure computation) |
| **documentation** | Knowledge base for AI agents | asset (optional) |
| **website** | Public web interface | auth, account, character, etc. |

**Rules for Layer 4**:
- May depend on any lower layer
- Should not be dependencies for lower layers
- Can be enabled/disabled based on game requirements

---

## Reference Counting & Cleanup

A common need is determining if a foundational entity (like Character) can be safely deleted by checking if higher-layer services still reference it. This creates a tension with the hierarchy rules.

### The Wrong Way (Dependency Inversion)

```csharp
// IN CHARACTER SERVICE - FORBIDDEN!
public class CharacterService
{
    private readonly IActorClient _actorClient;           // NO! Actor is Layer 4
    private readonly ICharacterEncounterClient _encounter; // NO! Encounter is Layer 3

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

The foundational service defines an event it **consumes** without knowing who publishes to it. Higher-layer services publish to that topic. This pattern is used by Permission and Mapping services.

**Step 1: Foundational service defines the event it accepts**

```yaml
# character-events.yaml
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
          description: The character being referenced
        sourceType:
          type: string
          description: Type of entity holding the reference (e.g., "actor", "encounter")
        sourceId:
          type: string
          format: uuid
          description: ID of the entity holding the reference
```

**Step 2: Higher-layer services publish to the foundational service's topic**

```csharp
// Actor service (Layer 4) - publishes TO Character's event topic
public class ActorService
{
    public async Task CreateActorAsync(CreateActorRequest request, ...)
    {
        // Create actor...

        // Actor knows about Character (correct direction!)
        // Publishes to Character's defined event topic
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
// Character service (Layer 2) - consumes its OWN event definition
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

Higher-layer services handle their own cleanup when foundational entities are deleted. No reference counting needed.

```csharp
// Actor service (Layer 4) - cleans up when character is deleted
public class ActorServiceEvents
{
    [EventSubscription("character.deleted")]  // Actor depends on Character's event - correct!
    public async Task HandleCharacterDeleted(CharacterDeletedEvent evt, ...)
    {
        // Actor's responsibility to clean up actors for this character
        await CleanupActorsForCharacter(evt.CharacterId);
    }
}
```

This is simpler but means the foundational service can't gate deletion on active references - it just publishes deletion and consumers react.

---

## Visual Hierarchy

```
┌─────────────────────────────────────────────────────────────────────┐
│ LAYER 4: Application Services                                       │
│ actor, behavior, mapping, scene, matchmaking, leaderboard,          │
│ achievement, voice, save-load, asset, music, documentation, website │
├─────────────────────────────────────────────────────────────────────┤
│ LAYER 3: Extended Services                                          │
│ character-personality, character-history, character-encounter,      │
│ realm-history, subscription, game-session, escrow, inventory, item  │
├─────────────────────────────────────────────────────────────────────┤
│ LAYER 2c: Extended Foundations                                      │
│ character (→realm,species), location (→realm), currency, contract   │
├─────────────────────────────────────────────────────────────────────┤
│ LAYER 2b: Core Foundations                                          │
│ auth (→account), realm (→game-service), relationship (→rel-type),   │
│ permission (→connect)                                               │
├─────────────────────────────────────────────────────────────────────┤
│ LAYER 2a: Root Foundations                                          │
│ account, game-service, relationship-type, species, connect          │
├─────────────────────────────────────────────────────────────────────┤
│ LAYER 1: Observability (Optional)                                   │
│ telemetry, orchestrator, analytics                                  │
├─────────────────────────────────────────────────────────────────────┤
│ LAYER 0: Infrastructure (Always On)                                 │
│ lib-state, lib-messaging, lib-mesh                                  │
└─────────────────────────────────────────────────────────────────────┘

     ▲ Dependencies flow DOWNWARD only
     │ Higher layers depend on lower layers
     │ Lower layers NEVER depend on higher layers
```

---

## Enforcement

### During Development

Before adding a service client dependency, ask:
1. What layer is my service in?
2. What layer is the target service in?
3. Is the target layer equal or lower? If not, **STOP**.

### In Code Review

Check `using` statements and constructor parameters for service clients. Flag any that violate the hierarchy.

### In Deep Dive Documents

Each plugin's deep dive document must list its dependencies. Cross-reference with this hierarchy to catch violations.

### Automated (Future)

Consider adding a build-time check that parses project references and validates against the hierarchy.

---

## Exceptions

There are no exceptions. If you think you need one, you're likely:

1. **Solving the wrong problem**: Maybe the higher-layer service should publish events instead
2. **Missing an abstraction**: Maybe a reference registry or mediator pattern is needed
3. **In the wrong layer**: Maybe your service belongs in a different layer

Discuss with the team before violating the hierarchy. Document any approved exceptions here (there should be very few, if any).

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-02-01 | 1.0 | Initial version |

---

*This document is referenced by the development tenets. Violations must be explicitly approved.*
