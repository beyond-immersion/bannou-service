# Regional Watchers and Event Agent Spawning

> **Status**: Design / Partial Implementation
> **Priority**: Medium
> **Related**: `docs/planning/CASE_STUDY_-_MONSTER_ARENA.md`

## Overview

Regional Watchers are long-running Event Actors that monitor event streams and manage their domain of responsibility. In Arcadia, these are the **gods** - divine entities that oversee specific aspects of the game world.

This document describes two patterns for Event Agent orchestration:
1. **God Pattern** - Domain-specific watchers that spawn Event Agents when needed
2. **Direct Coordinator Pattern** - Simple Event Agents spawned for specific encounters

## Pattern 1: Gods as Regional Watchers

### Core Concept

In Arcadia, "gods" are the managers of the game world. Each god has:
- **A domain of responsibility** (death, forest, monsters, war, commerce, etc.)
- **Event stream subscriptions** matching their domain
- **Capabilities** specific to their role
- **Realms of influence** where they operate

Gods are **long-running network tasks** that:
- Start lazily when a realm they influence becomes active
- Listen to event streams for domain-relevant events
- Execute APIs (game server, other services)
- Spawn Event Agents for specific situations
- Trigger encounters for Character Agents

### Example: God of Death

```yaml
# Actor template for the God of Death
metadata:
  id: god-of-death
  type: event_brain
  description: "Oversees death, undeath, and the transition between life and death"

# Domain configuration
config:
  domain: "death"
  realms_of_influence: ["underworld", "graveyards", "battlefields"]

  # Event subscriptions (what this god cares about)
  subscriptions:
    - "character.*.died"           # Any character death
    - "combat.*.fatality"          # Combat fatalities
    - "ritual.necromancy.*"        # Necromantic rituals
    - "location.graveyard.*"       # Graveyard events
    - "undead.*.spawned"           # Undead spawning

  # Capabilities (what this god can do)
  capabilities:
    - spawn_undead                 # Can raise undead
    - empower_death_magic          # Can boost death magic in area
    - trigger_death_encounter      # Can create death-themed encounters
    - mark_for_death               # Can mark characters for special death events

flows:
  main:
    # Process incoming domain events
    - foreach:
        collection: "${pending_events}"
        as: "event"
        do:
          - call: evaluate_event

  evaluate_event:
    # Route based on event type
    - switch:
        value: "${event.type}"
        cases:
          "character.died":
            - call: handle_character_death
          "ritual.necromancy.started":
            - call: monitor_necromancy
          "combat.fatality":
            - call: evaluate_battle_deaths

  handle_character_death:
    # Significant death? Spawn a death-themed Event Agent
    - cond:
        - when: "${event.character.is_vip || event.witnesses.length > 5}"
          then:
            # Spawn a "Death Scene" Event Agent
            - spawn_event_agent:
                template: "death-scene-coordinator"
                context:
                  deceased_id: "${event.character.id}"
                  location: "${event.location}"
                  witnesses: "${event.witnesses}"
                  cause_of_death: "${event.cause}"
```

### Example: God of Monsters

```yaml
metadata:
  id: god-of-monsters
  type: event_brain
  description: "Controls monster spawning and monster-related events"

config:
  domain: "monsters"

  subscriptions:
    - "region.*.population_low"    # Monster population dropped
    - "player.*.entered_zone"      # Players entering zones
    - "monster.*.killed"           # Monster deaths
    - "ecology.*.imbalanced"       # Ecosystem changes

  capabilities:
    - spawn_monsters               # Primary responsibility
    - create_monster_event         # Special monster encounters
    - evolve_monster               # Upgrade monster variants
    - trigger_migration            # Move monster populations

flows:
  handle_population_low:
    # Region needs more monsters
    - set:
        variable: spawn_count
        value: "${calculate_spawn_needs(event.region, event.current_population)}"

    - call_game_server:
        endpoint: "/monsters/spawn-batch"
        data:
          region_id: "${event.region.id}"
          count: "${spawn_count}"
          tier: "${event.region.difficulty_tier}"
```

### God Lifecycle

```
Realm Activation
       │
       ▼
┌──────────────────────────────────────────────────────┐
│  God Actor Started (if has influence in realm)        │
│                                                       │
│  1. Subscribe to domain event streams                 │
│  2. Query initial realm state                         │
│  3. Enter main processing loop                        │
└──────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────────────────────────────────────────────┐
│  Event Processing Loop (long-running)                 │
│                                                       │
│  - Receive domain events from subscriptions           │
│  - Evaluate against god's interests/responsibilities  │
│  - Execute capabilities (spawn, trigger, call APIs)   │
│  - Spawn Event Agents for specific situations         │
└──────────────────────────────────────────────────────┘
       │
       ▼
Realm Deactivation → God Actor Stopped
```

### Domain-Specific Filtering (Not Generic "Interestingness")

Previous designs emphasized generic "interestingness scoring." This is the wrong abstraction.

**Wrong approach:**
```yaml
# Too generic - what makes something "interesting"?
interestingness:
  power_proximity: 0.3
  antagonism: 0.4
  spawn_threshold: 0.7
```

**Right approach:**
```yaml
# Domain-specific - each god knows what THEY care about
god_of_death:
  cares_about:
    - deaths (especially dramatic or witnessed)
    - necromancy (rituals, undead creation)
    - graveyards (desecration, activity)

god_of_war:
  cares_about:
    - battles (especially large scale)
    - duels (honor combat)
    - military movements

god_of_commerce:
  cares_about:
    - major transactions
    - market disruptions
    - trade route events
```

Each god's "interest filter" is simply their domain subscriptions + evaluation logic.

---

## Pattern 2: Direct Coordinator (Simple Case)

### Core Concept

For many situations, no Regional Watcher/God is needed. The Event Agent is spawned directly with known parameters:

- **Known participants** from the start
- **Clear role** (coordinate this specific thing)
- **Simple lifecycle** (start → do job → end)
- **No spawning of sub-agents** needed

### Example: Monster Arena Fight Coordinator

See `docs/planning/CASE_STUDY_-_MONSTER_ARENA.md` for full details.

```yaml
metadata:
  id: arena-fight-coordinator
  type: event_brain
  description: "Coordinates a single arena fight between two participants"

# Spawned with known context - no guesswork
context:
  variables:
    fighter_a:
      source: "${spawn_context.fighter_a}"
    fighter_b:
      source: "${spawn_context.fighter_b}"
    arena_id:
      source: "${spawn_context.arena_id}"

flows:
  main:
    # Already know exactly what to do
    - call: initialize_fight
    - call: coordinate_rounds
    - call: declare_winner
    - call: cleanup
```

**Spawning is explicit:**
```csharp
// Game server code when arena fight starts
await _actorClient.StartActorAsync(new StartActorRequest
{
    TemplateId = "arena-fight-coordinator",
    ActorId = $"arena-fight-{fightId}",
    InitialContext = new Dictionary<string, object?>
    {
        ["fighter_a"] = fighterAId,
        ["fighter_b"] = fighterBId,
        ["arena_id"] = arenaId
    }
});
```

### When to Use Each Pattern

| Scenario | Pattern | Why |
|----------|---------|-----|
| Monster spawning | God Pattern | Continuous responsibility, realm-wide |
| Arena fight | Direct Coordinator | Known participants, clear lifecycle |
| Death events | God Pattern | Domain-wide monitoring needed |
| Cutscene | Direct Coordinator | Triggered by game, known participants |
| Quest boss encounter | Either | Could be god-triggered or game-triggered |
| World event (festival) | God Pattern | Scheduled, spawns many sub-events |
| Duel between players | Direct Coordinator | Explicit trigger, two participants |

---

## Implementation Considerations

### God Actor Startup

Gods start when realms they influence become active:

```csharp
// When realm activates
public async Task OnRealmActivatedAsync(string realmId)
{
    // Find gods with influence in this realm
    var gods = await _godRegistry.GetGodsForRealmAsync(realmId);

    foreach (var god in gods)
    {
        // Start god actor if not already running
        await _actorService.EnsureActorRunningAsync(
            templateId: god.TemplateId,
            actorId: $"god-{god.Id}-realm-{realmId}");
    }
}
```

### Event Subscriptions

Gods subscribe to domain-specific topics:

```csharp
// In god actor initialization
await _messageSubscriber.SubscribeAsync<CharacterDiedEvent>(
    "character.*.died",
    async (evt, ct) => await OnCharacterDiedAsync(evt, ct),
    filterPredicate: evt => IsInMyDomain(evt));
```

### Capability Execution

Gods execute capabilities via existing infrastructure:

```csharp
// Spawn monsters (God of Monsters)
await _gameServerClient.SpawnMonstersAsync(region, count, tier);

// Trigger encounter (any god)
await _actorService.StartActorAsync(eventAgentTemplate, context);

// Call game server API
await _meshClient.InvokeMethodAsync<Req, Resp>("game-server", "endpoint", request);
```

---

## Arcadia God Registry (Design)

```yaml
# Configuration for Arcadia's pantheon
gods:
  thanatos:
    id: god-of-death
    template: god-death-watcher
    domains: [death, undeath, afterlife]
    realms: [underworld, graveyards, battlefields, temples_of_death]

  silvanus:
    id: god-of-forest
    template: god-forest-watcher
    domains: [forest, nature, wildlife, growth]
    realms: [ancient_forest, druid_groves, wilderness]

  ares:
    id: god-of-war
    template: god-war-watcher
    domains: [war, combat, military, conquest]
    realms: [battlefields, military_camps, arenas, war_torn_regions]

  typhon:
    id: god-of-monsters
    template: god-monster-watcher
    domains: [monsters, beasts, spawning]
    realms: [all]  # Monsters everywhere

  hermes:
    id: god-of-commerce
    template: god-commerce-watcher
    domains: [trade, commerce, travel, messages]
    realms: [markets, trade_routes, cities]
```

---

## Relationship to Existing Infrastructure

| Existing Component | Role in God Pattern |
|-------------------|---------------------|
| `ActorRunner` | Runs god actors (long-running Event Brains) |
| `IMessageBus` | Gods subscribe to domain event streams |
| Event Brain handlers | Gods use `emit_perception`, `schedule_event`, etc. |
| `ActorService.StartActorAsync` | Gods spawn Event Agents |
| lib-mesh | Gods call game server APIs |
| Encounter system | Gods trigger encounters via existing endpoints |

**No new infrastructure needed** - gods are Event Brain actors using existing capabilities.

---

## Next Steps

1. **Define god templates** for Arcadia's pantheon
2. **Implement god registry** (which gods, which realms)
3. **Create example god behaviors** (start with God of Monsters for demo)
4. **Wire realm activation** to god startup
5. **Test with Monster Arena** case study (simple coordinator pattern)

---

## Open Questions

1. **God persistence** - Do gods maintain state between realm sessions?
2. **Multi-realm gods** - Can one god instance serve multiple realms, or one per realm?
3. **God cooperation** - How do gods coordinate (e.g., death + war during battle)?
4. **Player interaction** - Can players interact with gods directly (prayers, offerings)?
