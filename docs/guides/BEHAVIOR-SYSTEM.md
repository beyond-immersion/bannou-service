# Behavior System - Bringing Worlds to Life

> **Version**: 3.0
> **Status**: Production-ready (all core systems implemented)
> **Key Plugins**: `lib-actor` (L2), `lib-behavior` (L4), `lib-puppetmaster` (L4), `lib-character-personality` (L4), `lib-character-encounter` (L4), `lib-character-history` (L4), `lib-quest` (L2)
> **Deep Dives**: [Actor](../plugins/ACTOR.md), [Behavior](../plugins/BEHAVIOR.md), [Puppetmaster](../plugins/PUPPETMASTER.md), [Character Personality](../plugins/CHARACTER-PERSONALITY.md), [Character Encounter](../plugins/CHARACTER-ENCOUNTER.md), [Character History](../plugins/CHARACTER-HISTORY.md), [Quest](../plugins/QUEST.md)
> **Related Guides**: [ABML](./ABML.md), [Mapping System](./MAPPING_SYSTEM.md), [Seed System](./SEED-SYSTEM.md)

The Actor System is the cognitive layer that makes Arcadia's worlds alive. It gives NPCs personality, memory, and growth. It orchestrates dramatic encounters. It implements the gods who curate regional flavor. It is the engine behind the vision's promise: **living worlds where content emerges from accumulated play history, not hand-authored content**.

---

## Table of Contents

1. [Why Actors Are Foundation](#1-why-actors-are-foundation)
2. [Architecture Overview](#2-architecture-overview)
3. [The NPC Intelligence Stack](#3-the-npc-intelligence-stack)
4. [NPC Brain Actors](#4-npc-brain-actors)
5. [Character Data Layer](#5-character-data-layer)
6. [Event Brains and Regional Watchers](#6-event-brains-and-regional-watchers)
7. [Cognition System](#7-cognition-system)
8. [GOAP Planning](#8-goap-planning)
9. [Behavior Integration](#9-behavior-integration)
10. [Cutscene and QTE Orchestration](#10-cutscene-and-qte-orchestration)
11. [Behavior Provider Chain](#11-behavior-provider-chain)
12. [Scaling and Distribution](#12-scaling-and-distribution)
13. [Game Server Integration](#13-game-server-integration)
- [Appendix A: Actor Categories](#appendix-a-actor-categories)
- [Appendix B: API Reference](#appendix-b-api-reference)

---

## 1. Why Actors Are Foundation

### 1.1 Serving the Vision

Arcadia's five north stars demand autonomous intelligence at scale:

- **Living Game Worlds**: NPCs think, remember, evolve, and form relationships whether or not a player is watching. Actors are the cognitive processes that make this happen.
- **The Content Flywheel**: Characters accumulate history through lived experience. When they die, compressed archives seed future content. Actors drive this accumulation.
- **100,000+ Concurrent AI NPCs**: Each NPC is a long-running cognitive process making decisions every 100-500ms. The actor pool architecture enables horizontal scaling to meet this target.
- **Emergent Over Authored**: Quests, economies, social dynamics, and combat choreography emerge from autonomous systems interacting -- not from scripted triggers.

### 1.2 Actors Are L2 GameFoundation

Actor is classified as **L2 GameFoundation** in the service hierarchy. This is deliberate: actors are not optional flavor -- they are the mechanism through which characters grow, remember, and evolve. Without actors, characters are fully functional via their behavior stacks (they can fight, navigate, interact), but they are static. They don't change. They don't learn. They don't become *individuals*.

The service hierarchy enables this without layer violations through the **Variable Provider Factory** pattern: L4 services (personality, encounters, history) provide data *to* the L2 Actor runtime via DI interfaces, not the other way around. Actor never depends on L4 -- it discovers whatever providers are registered at runtime and gracefully degrades when they're absent.

### 1.3 What Actors Do and Don't Do

| Actors Provide | Actors Do NOT Provide |
|----------------|----------------------|
| **Growth** -- characters learn and evolve | Core moment-to-moment decisions (behavior stacks handle this) |
| **Spontaneity** -- unexpected reactions to stimuli | Frame-by-frame combat execution (bytecode VM handles this) |
| **Personality** -- feelings, moods, memories | Required infrastructure (games work without actors, just statically) |
| **Orchestration** -- Event Brains coordinate dramatic moments | Rendering, physics, animation (game engine handles this) |

The actor never controls a character directly. It emits **state** (feelings, goals, memories) which the behavior stack reads and uses to modulate already-running behaviors. The actor is the "why"; the behavior stack is the "what"; intent channels are the "how."

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           BANNOU NETWORK                                │
│                                                                         │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │              CONTROL PLANE (lib-actor in bannou-service)          │  │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐           │  │
│  │  │ Actor        │  │ Actor        │  │ Pool         │           │  │
│  │  │ Registry     │  │ Templates    │  │ Manager      │           │  │
│  │  └──────────────┘  └──────────────┘  └──────────────┘           │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │               BEHAVIOR PROVIDER CHAIN                             │   │
│  │  DynamicBehaviorProvider (100) ──from Asset Service via          │   │
│  │  │                               Puppetmaster                    │   │
│  │  SeededBehaviorProvider (50)  ──pre-defined behaviors            │   │
│  │  │                                                               │   │
│  │  FallbackBehaviorProvider (0) ──embedded defaults                │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│       lib-mesh (routing)  +  lib-messaging (events)                     │
│                     │                                                   │
│    ┌────────────────┼────────────────┐                                  │
│    ▼                ▼                ▼                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                    │
│  │ Actor Pool  │  │ Actor Pool  │  │ Game Server  │                    │
│  │  Node A     │  │  Node B     │  │  (Stride)    │                    │
│  │             │  │             │  │              │                    │
│  │ NPC Brains  │  │ Event       │  │ Physics,     │                    │
│  │ + Watchers  │  │ Brains      │  │ Bytecode VM, │                    │
│  └─────────────┘  └─────────────┘  │ Cinematics   │                    │
│                                     └─────────────┘                    │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.1 Actor Types

| Actor Type | Scope | Lifecycle | Primary Outputs |
|------------|-------|-----------|-----------------|
| **NPC Brain** | Single character | Long-running while character is active | Feelings, goals, memories → behavior stack |
| **Event Brain** | Region or situation | Spawned on trigger, dies when event concludes | Cinematics, QTE prompts, choreography |
| **Regional Watcher** | Region (god-level) | Long-running, always on | Spawn Event Brains, orchestrate drama |
| **Administrative** | System-wide | Singleton or scheduled | Maintenance, economic simulation |

All actor types share the same `ActorRunner` infrastructure -- the difference is what behaviors they execute and what perception events they subscribe to.

---

## 3. The NPC Intelligence Stack

This is the architectural keystone that makes autonomous NPCs possible within the service hierarchy:

```
Character Personality (L4) ──provides ${personality.*}──┐
Character Encounter (L4)   ──provides ${encounters.*}───┤
Character History (L4)     ──provides ${backstory.*}────┤
Quest (L2)                 ──provides ${quest.*}─────────┤
Combat Preferences (L4)    ──provides ${combat.*}────────┤
                                                         ▼
                                            Variable Provider Factory
                                            (DI: IEnumerable<IVariableProviderFactory>)
                                                         │
                                                         ▼
Behavior (L4) ──compiles ABML──▶ Actor (L2) ──executes ABML──▶ NPC Actions
                                     ▲
                                     │
                               Puppetmaster (L4)
                          (dynamic behavior loading
                           via Asset Service L3)
```

NPC behavior expressions like `${personality.aggression > 0.7 && encounters.last_hostile_days < 3}` require data from multiple L4 services flowing into the L2 Actor runtime. The Variable Provider Factory pattern makes this possible without hierarchy violations -- Actor depends on the shared `IVariableProviderFactory` interface, not on the L4 services that implement it.

---

## 4. NPC Brain Actors

### 4.1 The Character Co-Pilot Pattern

In Arcadia, players don't "control" characters -- they **possess** them as guardian spirits. The character has its own NPC brain (actor) that:

- Is always running, always perceiving, always computing
- Has intimate knowledge of capabilities, personality, and preferences
- Makes decisions when the player doesn't (or can't respond fast enough)
- Maintains personality and behavioral patterns across sessions

When a player connects, they take **priority** over the agent's decisions, but the agent doesn't stop -- it watches, computes, and waits. When the player misses a QTE window, the character's pre-computed answer executes, creating emergent personality expression: an aggressive character attacks anyway, a cautious one takes a defensive stance, a loyal one protects an ally first.

### 4.2 Perception Flow

Perceptions flow **directly** from Game Server to Actor via RabbitMQ event subscription -- the control plane is not in the path:

```
Game Server                      RabbitMQ                       Actor Pool Node
(character sees enemy)  ──publish──▶  topic: character.{id}.perception  ──deliver──▶  ActorRunner
                                                                                      │
                                                                         enqueue in bounded
                                                                         perception channel
                                                                         (DropOldest policy)
                                                                                      │
                                                                               ▼ next tick ▼
                                                                         drain → filter → assess
                                                                         → remember → replan
```

The bounded perception queue uses urgency-based filtering: high-urgency perceptions (threats) fast-track through the pipeline while low-urgency ones (ambient observations) go through full deliberation. This prevents perception floods from overwhelming the cognition pipeline.

### 4.3 State Updates Flow to Behavior Stack

Actor cognition produces state updates that flow back to the game server's behavior stack:

```
ACTOR (Pool Node)                           GAME SERVER (Stride)
Cognition produces:                         Behavior Stack reads:
  feelings: { angry: 0.9 }                   if angry > 0.7 AND enemy_nearby
  goals: { target: entityX }       ──emit──▶    → choose aggressive_combo
  memories: { betrayed_by: [X] }              if fearful > 0.8 AND low_health
                                                → choose flee_behavior
                                              EmitIntent opcodes produce:
                                                action, locomotion, stance,
                                                attention, expression, vocalization
```

The separation is clean:
- **Actor**: "Why" (feelings, goals, memories)
- **Behavior Stack**: "What" (which actions to take based on state + perceptions)
- **Intent Channels**: "How" (animation, movement, camera execution on game client)

### 4.4 Actor Output Types

| Output Type | Frequency | Effect |
|-------------|-----------|--------|
| **State Update** | Every few ticks | Updates feelings/mood variables read by behavior stack |
| **Goal Update** | When goals change | Updates goal-related inputs for GOAP planning |
| **Memory Update** | On significant events | Stores/retrieves memories affecting future behavior |
| **Behavior Change** | Rare (learning/growth) | Modifies the composed behavior stack itself |

---

## 5. Character Data Layer

Five variable provider factories supply character data to the Actor runtime via the DI-based Variable Provider Factory pattern. Each is implemented by its owning L4 service and registered with DI at startup.

### 5.1 Provider Registry

| Provider | Service | Namespace | Example Variables |
|----------|---------|-----------|-------------------|
| **PersonalityProviderFactory** | lib-character-personality (L4) | `${personality.*}` | `${personality.openness}`, `${personality.aggression}` |
| **CombatPreferencesProviderFactory** | lib-character-personality (L4) | `${combat.*}` | `${combat.style}`, `${combat.riskTolerance}`, `${combat.protectAllies}` |
| **BackstoryProviderFactory** | lib-character-history (L4) | `${backstory.*}` | `${backstory.origin}`, `${backstory.fear.value}` |
| **EncountersProviderFactory** | lib-character-encounter (L4) | `${encounters.*}` | `${encounters.last_hostile_days}`, `${encounters.sentiment}` |
| **QuestProviderFactory** | lib-quest (L2) | `${quest.*}` | `${quest.active}`, `${quest.objectives}` |

### 5.2 How Providers Load

When an actor starts for a character, `ActorRunner` discovers all registered `IVariableProviderFactory` implementations via DI and creates providers for the character:

```csharp
// ActorRunner receives factories via constructor injection
private readonly IEnumerable<IVariableProviderFactory> _providerFactories;

// On each tick, build execution scope with character data
foreach (var factory in _providerFactories)
{
    try
    {
        var provider = await factory.CreateAsync(CharacterId, ct);
        scope.RegisterProvider(provider);
    }
    catch (Exception ex)
    {
        // Graceful degradation -- provider failure doesn't crash the actor
        _logger.LogWarning(ex, "Failed to create {Provider}", factory.ProviderName);
    }
}
```

Providers handle their own caching internally (typically 5-minute TTL with stale-if-error fallback). When providers are absent (L4 services disabled), the actor continues with reduced data -- it just can't reference those variable namespaces in ABML expressions.

### 5.3 Using Character Data in ABML

```yaml
flows:
  evaluate_combat_approach:
    - cond:
        if: "${personality.aggression > 0.7 && combat.riskTolerance > 0.6}"
        then:
          - set: combat_approach = "aggressive"
          - emit_intent:
              channel: stance
              stance: "aggressive"
              urgency: 0.9

    # Backstory-driven fear responses
    - cond:
        if: "${backstory.fear.key == 'FIRE' && environment.has_fire}"
        then:
          - modify_emotion:
              emotion: fear
              delta: 0.3

    # Encounter-driven grudges
    - cond:
        if: "${encounters.last_hostile_days < 3 && encounters.sentiment < -0.5}"
        then:
          - set: disposition = "hostile"
```

### 5.4 Personality Evolution

Characters evolve through experiences. The `RecordExperience` API triggers probabilistic trait changes:

| Experience | Potential Effects |
|------------|-------------------|
| `TRAUMA` | Neuroticism up, Agreeableness down |
| `BETRAYAL` | Honesty down, Agreeableness down |
| `VICTORY` | Confidence up |
| `FRIENDSHIP` | Extraversion up, Agreeableness up |
| `NEAR_DEATH` | Risk tolerance down, Retreat threshold up |
| `ALLY_SAVED` | Protect allies tendency up |

Combat preferences also evolve: repeated near-death experiences lower risk tolerance, successful aggressive tactics reinforce aggressive style. See [CHARACTER-PERSONALITY.md](../plugins/CHARACTER-PERSONALITY.md) for the full evolution model.

---

## 6. Event Brains and Regional Watchers

### 6.1 The Invisible Director

Event Brains are drama coordinators for significant situations. They don't fight or act directly -- they **script** the action in real-time: discovering environmental affordances, tracking combat state, generating option sets, presenting QTE choices, and choreographing the result.

Event Brains run as standard actors using the same `ActorRunner` infrastructure as NPC brains. The differences are:
- Perception subscriptions are region-level, not character-level
- They query other actors via `query_options` rather than maintaining their own personality
- They emit choreography as perception events to participants (not direct RPC)

### 6.2 Regional Watchers as Gods

Regional Watchers implement Arcadia's god system. Each god (Moira/Fate, Thanatos/Death, Silvanus/Forest, Ares/War, Typhon/Monsters, Hermes/Commerce) monitors event streams within their domain with aesthetic preferences and spawns Event Brains when "interestingness" thresholds are crossed.

```
┌─────────────────────────────────────────────────────────┐
│            REGIONAL WATCHER (e.g., Ares/War)            │
│                                                         │
│  Monitors: combat events, antagonism, power proximity   │
│  Aesthetic: prefers dramatic reversals, honorable duels  │
│  Spawns: Event Brains when thresholds crossed           │
│                                                         │
│  Always-on Event Actors for VIPs:                       │
│  Kings, god avatars, elder dragons                      │
└─────────────────────┬───────────────────────────────────┘
                      │ spawns
          ┌───────────┼───────────┐
          ▼           ▼           ▼
    ┌───────────┐ ┌──────────┐ ┌──────────────┐
    │ FESTIVAL  │ │ DISASTER │ │CONFRONTATION │
    │ DAY EVENT │ │ EVENT    │ │ EVENT        │
    └─────┬─────┘ └──────────┘ └──────────────┘
          │ spawns sub-events
          ▼
    ┌───────────┐
    │ Market    │
    │ Brawl     │
    └───────────┘
```

### 6.3 Interestingness Triggers

| Trigger | Description |
|---------|-------------|
| **Power level proximity** | Matched combatants (interesting fight potential) |
| **Antagonism score** | Relationship service indicates hatred/rivalry |
| **Environmental drama** | Near hazards, in public, at significant location |
| **Story flags** | Characters with narrative significance |
| **Player involvement** | Human players make things interesting by default |
| **VIP presence** | Some characters always have an Event Actor |

### 6.4 Event Brain Capabilities

Event Brains use specialized ABML actions in addition to standard actions:

**Coordination Actions (lib-actor):**

| Action | Description |
|--------|-------------|
| `query_options` | Query another actor's available options (with freshness control) |
| `query_actor_state` | Read another actor's state from local registry |
| `emit_perception` | Send choreography instruction to a character |
| `schedule_event` | Schedule a delayed event |
| `set_encounter_phase` | Transition encounter to a new phase |
| `end_encounter` | End the current encounter |

**Freshness levels** for `query_options`: `fresh` (force re-evaluation), `cached` (accept recently cached), `stale_ok` (accept any cached value). The requester determines freshness because the Event Brain knows urgency better than the system.

### 6.5 Combat Without Event Actors

Fights happen constantly without Event Actors -- and that's fine. Normal combat is handled entirely by the game engine with direct bytecode evaluation each frame. **Event Actors enhance already-good combat when the situation warrants it.** Most fights are just fights. The special ones become cinematic events.

### 6.6 Connection to the Content Flywheel

Event Brains are a key component of the content flywheel loop. Regional Watchers consume narrative seeds generated from character archives (via Storyline service) and orchestrate them into scenarios. The experiences generated by those scenarios become new character history, which eventually becomes new archives when characters die, which generates new seeds. The loop accelerates with world age.

---

## 7. Cognition System

### 7.1 The Five-Stage Pipeline

Each tick, NPC brains process perceptions through a cognition pipeline:

```
1. filter_attention    → Budget-limited perception filtering (urgency-based)
2. query_memory        → Retrieve relevant memories for context
3. assess_significance → Score perceptions for memory storage
4. store_memory        → Persist significant experiences
5. evaluate_goal_impact + trigger_goap_replan → Goal reassessment
```

High-urgency perceptions (threats above fast-track threshold) bypass the full pipeline and trigger immediate reactions.

### 7.2 Cognition Templates

Three embedded templates define which stages run and with what parameters:

| Template | Use Case | Stages |
|----------|----------|--------|
| `humanoid_base` | Humanoid NPCs | All 5 stages, attention budget 100, memory limit 20 |
| `creature_base` | Animals/creatures | Filter + memory + intention only, budget 50, limit 5 |
| `object_base` | Interactive objects | Filter + intention only, budget 10, no memory |

Creatures skip significance assessment -- they react instinctively. Objects skip memory entirely -- they're stateless responders.

### 7.3 Character-Specific Overrides

The `CognitionBuilder` constructs pipelines from templates with optional overrides:

```csharp
// A battle-hardened veteran: less reactive to threats
var overrides = new CognitionOverrides
{
    Overrides =
    [
        new ParameterOverride
        {
            Stage = "filter",
            HandlerId = "attention_filter",
            Parameters = new Dictionary<string, object>
            {
                ["threat_fast_track_threshold"] = 0.95f
            }
        }
    ]
};
var pipeline = builder.Build("humanoid_base", overrides);
```

Override types: `ParameterOverride`, `DisableHandlerOverride`, `AddHandlerOverride`, `ReplaceHandlerOverride`, `ReorderHandlerOverride`.

### 7.4 Cognition Handler Locations

| Component | Location |
|-----------|----------|
| `CognitionPipeline` | `lib-behavior/Cognition/CognitionBuilder.cs` |
| `CognitionTemplateRegistry` | `lib-behavior/Cognition/CognitionTemplateRegistry.cs` |
| Cognition Handlers | `bannou-service/Abml/Cognition/Handlers/` |
| Handler Registration | `bannou-service/Abml/DocumentExecutorFactory.cs` |

### 7.5 Memory System

Current implementation uses keyword-based relevance matching with multiple indices:

| Index | Purpose | Lookup |
|-------|---------|--------|
| **entity_memories** | "What do I know about this person?" | O(1) by entity ID |
| **type_index** | "What threats have I seen?" | O(1) by category |
| **spatial_index** | "What happened near here?" | O(1) by region |
| **recent** | Time-ordered fallback | Circular buffer, oldest dropped |

The `IMemoryStore` interface is designed for swappable implementations. The keyword-based MVP is sufficient for structured game events (combat encounters, dialogue exchanges, entity sightings). An embedding-based implementation can be added later for semantic similarity matching without changing the cognition pipeline or handlers. See [BEHAVIOR.md](../plugins/BEHAVIOR.md) for trade-offs.

### 7.6 Emotional State Model

Actors maintain 8 emotional dimensions (stress, alertness, fear, anger, joy, sadness, comfort, curiosity) on a 0.0-1.0 scale. Emotions decay toward personality-defined baselines over time -- a guard doesn't stay angry forever, but an anxious character has a higher stress baseline. The dominant emotion simplifies behavior selection by providing a single value to check rather than all eight dimensions.

---

## 8. GOAP Planning

Goal-Oriented Action Planning (GOAP) is the A* search planner that enables NPCs to autonomously discover action sequences to achieve their goals. Instead of hand-crafting behavior trees, you define **what NPCs want** (goals) and **what they can do** (actions with GOAP annotations on ABML flows), and the planner figures out **how** to get there.

### 8.1 Core Concepts

| Concept | Description |
|---------|-------------|
| **WorldState** | Immutable key-value store representing the NPC's current world (hunger, gold, location, etc.). Operations return new instances for safe A* backtracking. |
| **Goal** | Desired world state with priority and conditions (e.g., `hunger: "<= 0.3"` at priority 100). |
| **Action** | An ABML flow with `goap:` metadata: preconditions, effects, and cost. |
| **Plan** | Ordered action sequence that transforms current state → goal state, minimizing total cost. |

Goals and GOAP annotations are authored in ABML -- see [ABML Guide section 10](./ABML.md#10-goap-integration) for the YAML syntax.

### 8.2 How the Planner Works

The planner uses A* search with world states as nodes:

```
1. Start node = current world state
2. While open set not empty:
   a. Pop node with lowest F-cost (G + H)
   b. If satisfies goal → reconstruct plan, return
   c. For each applicable action:
      - Apply effects to get new state
      - If not in closed set, add to open set
3. No plan found
```

**Cost calculation:**
- **G-cost**: Sum of action costs from start to current node
- **H-cost**: Heuristic distance to goal (sum of unsatisfied condition distances)
- **F-cost**: G + H (lower F-cost nodes explored first)

### 8.3 Planning Options

Search behavior is controlled by `PlanningOptions`:

| Parameter | Default | Description |
|-----------|---------|-------------|
| `MaxDepth` | 10 | Maximum actions in a plan |
| `MaxNodesExpanded` | 1000 | Prevent runaway searches |
| `TimeoutMs` | 100 | Planning time limit |
| `HeuristicWeight` | 1.0 | A* weight (>1 = faster but less optimal) |
| `AllowDuplicateActions` | true | Whether the same action can appear twice in a plan |

**Urgency-based presets** (used by the `trigger_goap_replan` cognition handler):

| Urgency | Threshold | MaxDepth | Timeout | MaxNodes | Use Case |
|---------|-----------|----------|---------|----------|----------|
| Low | < 0.3 | 10 | 100ms | 1000 | Full deliberation |
| Medium | 0.3 - 0.7 | 6 | 50ms | 500 | Quick decision |
| High | >= 0.7 | 3 | 20ms | 200 | Immediate reaction |

High urgency = shallower search. Fight-or-flight decisions need to be fast, not optimal.

### 8.4 Plan Validation

Plans can become invalid as the world changes. The planner supports validation before continuing execution:

| Reason | Description | Suggestion |
|--------|-------------|------------|
| `None` | Plan is valid | Continue |
| `PreconditionInvalidated` | Current action's preconditions not met | Replan |
| `BetterGoalAvailable` | Higher-priority goal now unsatisfied | Replan |
| `GoalAlreadySatisfied` | Goal already achieved | Abort |
| `PlanCompleted` | All actions executed | Abort |

The cognition pipeline (section 7) triggers replan automatically when goal impact evaluation detects affected goals. Urgency determines planning depth.

### 8.5 Best Practices

**Designing Goals:**
- Reserve high priorities (90+) for survival; 1-25 for background desires
- Keep conditions simple (1-3 per goal)
- Avoid overlapping goals; they should be distinct

**Designing Actions:**
- Single responsibility per action
- Only preconditions that are actually required
- Effects should directly impact goal conditions
- Lower cost = more preferred by the planner

**Cost Tuning:**

| Action Type | Suggested Cost | Rationale |
|-------------|----------------|-----------|
| Cheap/easy actions | 1-2 | Preferred by planner |
| Standard actions | 3-5 | Normal options |
| Expensive/risky actions | 6-10 | Only when no better option |
| Emergency actions | 10+ | Last resort |

**World State Design:**
- Flat structure (avoid deep nesting)
- Numeric for gradients (hunger, health: 0.0-1.0)
- Boolean for flags (has_weapon, enemy_nearby)
- String for categories (location, mood)
- 10-20 actions per behavior document is usually sufficient

### 8.6 Implementation

The GOAP planner lives in `sdks/behavior-compiler/Goap/` and is registered as `IGoapPlanner` (singleton). ABML GOAP metadata is extracted during compilation by `GoapMetadataConverter` and cached with the compiled behavior. For implementation details, class signatures, and unit test patterns, see [BEHAVIOR.md](../plugins/BEHAVIOR.md).

---

## 9. Behavior Integration

### 8.1 The Behavior Stack

Characters have layered behavior stacks:

```
Character Behavior Stack
├── Base Layer (species/type fundamentals)
├── Cultural Layer (faction, background)
├── Professional Layer (class, occupation)
├── Personal Layer (individual quirks)
└── Situational Layer (current context overrides)
```

Each layer produces intent emissions. The stack merges them using archetype-defined strategies across six intent channels:

| Channel | Purpose | Merge Strategy |
|---------|---------|----------------|
| Locomotion | Movement | Highest urgency wins |
| Action | Combat/interaction | Highest urgency wins |
| Attention | Focus targets | Blended by weight |
| Stance | Body positioning | Highest urgency wins |
| Expression | Facial animations | Blended |
| Vocalization | Speech/sounds | Priority queue |

### 8.2 Behavior Types and Variants

Characters compose behaviors from types and variants:

```
Character Behaviors
├── combat (type)
│   ├── sword-and-shield (variant)
│   ├── dual-wield (variant)
│   └── unarmed (variant)
├── movement (type)
│   ├── standard (variant)
│   └── mounted (variant)
└── interaction (type)
    └── default (variant)
```

The `BehaviorModelCache` in lib-behavior supports variant fallback chains -- if a specific variant isn't found, it falls back through the chain until a match is found.

### 8.3 ABML Compilation and Execution

ABML behaviors are compiled through a multi-phase pipeline: YAML parsing → AST construction → semantic analysis → bytecode emission. The compiled bytecode can be executed by:

- **Tree-walking `DocumentExecutor`**: Server-side execution for cloud cognition (actors)
- **Bytecode `BehaviorModelInterpreter`**: Client-side execution on game servers for frame-rate behavior evaluation

Both produce the same results; tree-walking is used where flexibility matters, bytecode where performance matters.

---

## 10. Cutscene and QTE Orchestration

### 10.1 Coordination Infrastructure

lib-behavior provides coordination infrastructure for multi-participant cinematics:

| Component | Purpose |
|-----------|---------|
| `CutsceneCoordinator` | Thread-safe session management |
| `SyncPointManager` | Cross-entity synchronization with timeouts |
| `InputWindowManager` | QTE/choice windows with server adjudication |

Located in `lib-behavior/Coordination/`.

### 10.2 Multi-Channel Execution

Cutscenes use parallel channels with sync points:

```yaml
channels:
  camera:
    - fade_in: { duration: 1s }
    - move_to: { shot: wide_throne_room }
    - emit: establishing_complete
    - wait_for: @hero.at_mark
  hero:
    - wait_for: @camera.establishing_complete
    - walk_to: { mark: hero_mark_1, speed: cautious }
    - emit: at_mark
    - speak: "Your reign ends today!"
  audio:
    - play: { track: ambient_throne_room }
    - wait_for: @camera.establishing_complete
    - crossfade_to: { track: boss_theme }
```

### 10.3 QTE Input Windows

The `InputWindowManager` handles timed input collection. Each window has a default option (the character agent's pre-computed choice). If the player doesn't respond within the timeout, the default executes -- creating the "personality through failure" effect described in the co-pilot pattern.

### 10.4 Streaming Composition

Cinematics can be extended mid-execution:

```
0s      Game Server receives Cinematic A (complete, can execute independently)
0-10s   Executes Cinematic A
8s      Event Brain decides to extend based on player action
8.5s    Game Server receives Extension B
10s     Cinematic A hits continuation point → seamlessly transitions to B
```

Key properties: initial delivery is complete (game server can execute independently), extensions are additive (don't modify what's executing), missing extensions are fine (original completes gracefully).

### 10.5 Control Handoff

When cinematics complete, state synchronization follows a clear boundary:

| Style | Server Action | Client Action |
|-------|---------------|---------------|
| `Instant` | Write target state | Snap immediately |
| `Blend` | Write target state | Smoothly interpolate |
| `Explicit` | Write target state | Handoff handled externally |

The server writes the **target state** and signals the **handoff style**. The client applies the transition. Server-side blending would be wrong -- the server doesn't render and doesn't know the client's current visual state.

---

## 11. Behavior Provider Chain

### 11.1 The Problem

Actor (L2) needs to load ABML behavior documents. Some behaviors come from the Asset Service (L3). But L2 cannot depend on L3 -- that's a hierarchy violation.

### 11.2 The Solution: IBehaviorDocumentProvider

The `IBehaviorDocumentProvider` interface (in `bannou-service/Providers/`) defines a priority-ordered provider chain. Multiple providers register via DI, each with a different priority. When loading a document, providers are checked highest-priority-first:

| Priority | Provider | Source | Service |
|----------|----------|--------|---------|
| 100 | `DynamicBehaviorProvider` | Asset Service via presigned URLs | lib-puppetmaster (L4) |
| 50 | `SeededBehaviorProvider` | Pre-defined embedded behaviors | lib-actor (L2) |
| 0 | `FallbackBehaviorProvider` | Stub/default behaviors | lib-actor (L2) |

The `BehaviorDocumentLoader` in lib-actor aggregates all registered providers and queries them in priority order. The first provider that can serve the requested behavior reference wins.

### 11.3 Puppetmaster's Role

Puppetmaster (L4) bridges Actor (L2) and Asset (L3) by implementing `IBehaviorDocumentProvider`. It:

- Loads behaviors from the Asset Service using presigned URLs
- Caches them in a Redis-backed `BehaviorDocumentCache`
- Handles hot-reload by subscribing to `behavior.updated` events and invalidating the cache
- Manages Regional Watcher lifecycle and resource snapshot caching

This is the architectural mechanism that enables dynamic behavior loading without hierarchy violations. See [PUPPETMASTER.md](../plugins/PUPPETMASTER.md) for details.

### 11.4 Behavior Storage and Distribution

Behaviors follow the Asset Service pattern -- large behavior files are never transferred directly through the system:

```
ABML YAML → Behavior Service (compile) → Binary bytecode (.bbm)
                                              │
                        ┌─────────────────────┼────────────────────┐
                        ▼                     ▼                    ▼
                  Asset Service         State Store          Message Bus
                  (presigned upload     (metadata:           (behavior.created
                   to MinIO/S3)         behaviorId,          behavior.updated)
                                        assetId, name)
```

Retrieval: Game servers request behavior via API → Behavior Service looks up metadata → returns presigned download URL → game server downloads directly from MinIO. Related behaviors can be grouped into bundles for efficient bulk download.

---

## 12. Scaling and Distribution

### 12.1 Pool Node Architecture

Actor pool nodes are peers on the Bannou network with unique app-ids. They can send/receive events via lib-messaging and make mesh API calls via lib-mesh. The control plane only handles lifecycle (spawn, stop, migrate) -- it is never in the perception event path.

### 12.2 Deployment Modes

| Mode | Description | Status |
|------|-------------|--------|
| `Local` | All actors in the main process | Implemented |
| `PoolPerType` | Dedicated pool per actor category | Implemented |
| `SharedPool` | Shared pool across categories | Implemented |
| `AutoScale` | Dynamic scaling based on load | Declared, not yet implemented |

### 12.3 Horizontal Scaling

For 10,000 NPCs at 10 events/second = 100,000 events/second:
- Direct RabbitMQ subscription scales horizontally with pool nodes
- Control plane only handles lifecycle operations
- No bottleneck in event routing
- Pool health monitored via `PoolHealthMonitor` with heartbeat tracking

### 12.4 Event Tap Pattern

Event Brains can "tap" specific characters to receive their perception events by subscribing to the same RabbitMQ fanout exchange:

```
RabbitMQ Fanout Exchange (character-{id})
         │
   ┌─────┼─────┐
   ▼     ▼     ▼
 NPC   Player  Event
 Brain  Client  Brain
                (tap)
```

---

## 13. Game Server Integration

The game server (Stride) completes the perception-cognition-action loop. It has four responsibilities:

### 13.1 Publish Perceptions

Fire-and-forget broadcasts when characters experience something significant:

```csharp
await _messageBus.PublishAsync(
    $"character.{characterId}.perceptions",
    new CharacterPerceptionEvent
    {
        CharacterId = characterId,
        PerceptionType = PerceptionType.Visual,
        SourceId = enemyId,
        SourceType = SourceType.Character,
        Data = new { distance = 10.5, threat_level = 0.8 },
        Urgency = 0.9f,
        Timestamp = DateTimeOffset.UtcNow
    });
```

Publish perceptions for: entity enters range, combat events, environmental changes, social events, inventory changes.

### 13.2 Handle State Updates

Receive state updates from actors via lib-mesh and apply them to behavior stack input slots (feelings, goals, memories).

### 13.3 Apply to Behavior Stack

Write received state to behavior stack input slots so the bytecode VM can read them during frame-by-frame evaluation.

### 13.4 Lizard Brain Fallback

Characters must function autonomously when no NPC brain actor is connected. The behavior stack evaluates with default personality and no actor enrichment. Characters still respond to threats, navigate, fight, and interact socially -- they just don't grow or evolve.

This fallback is not a degraded mode -- it's the **default state**. The actor system layers growth and personality on top of already-functional characters. The guardian spirit model depends on this: characters are always autonomous, and the spirit (player + actor) gradually learns to collaborate with that autonomy.

---

## Appendix A: Actor Categories

| Category | Purpose | Auto-Spawn | Persistence |
|----------|---------|------------|-------------|
| `npc-brain` | Character cognition | On character load | Full state |
| `event-combat` | Combat orchestration | On trigger | Session only |
| `event-regional` | Regional events | On trigger | Session only |
| `world-admin` | World maintenance | Singleton | Metrics only |
| `scheduled-task` | CRON-like jobs | On schedule | None |

Template configuration:

```yaml
category: npc-brain
behaviorRef: "asset://behaviors/npc-brain-v1"
autoSpawnPattern: "character-{characterId}"
autoSpawnEnabled: true
autoSaveIntervalSeconds: 30
maxInstancesPerPool: 500
```

---

## Appendix B: API Reference

### Actor Templates

| Endpoint | Description |
|----------|-------------|
| `POST /actor/template/create` | Create actor template |
| `POST /actor/template/get` | Get template by ID or category |
| `POST /actor/template/list` | List all templates |
| `POST /actor/template/update` | Update template |
| `POST /actor/template/delete` | Delete template |

### Actor Instances

| Endpoint | Description |
|----------|-------------|
| `POST /actor/spawn` | Spawn new actor from template |
| `POST /actor/get` | Get actor (instantiate-on-access if template allows) |
| `POST /actor/stop` | Stop running actor |
| `POST /actor/list` | List actors with filters |
| `POST /actor/send-message` | Send message to actor |

### Messaging Topics

**Control Plane → Pool Node:**
- `actor.node.{poolAppId}.spawn` → SpawnActorCommand
- `actor.node.{poolAppId}.stop` → StopActorCommand
- `actor.node.{poolAppId}.message` → SendMessageCommand

**Pool Node → Control Plane:**
- `actor.pool-node.heartbeat` → PoolNodeHeartbeatEvent
- `actor.instance.status-changed` → ActorStatusChangedEvent
- `actor.instance.completed` → ActorCompletedEvent

**Perception Events:**
- `character.{characterId}.perception` → CharacterPerceptionEvent
- `character.{characterId}.state` → StateUpdateEvent

---

*For implementation details, see the per-plugin deep dive documents: [Actor](../plugins/ACTOR.md), [Behavior](../plugins/BEHAVIOR.md), [Puppetmaster](../plugins/PUPPETMASTER.md), [Character Personality](../plugins/CHARACTER-PERSONALITY.md), [Character Encounter](../plugins/CHARACTER-ENCOUNTER.md), [Character History](../plugins/CHARACTER-HISTORY.md), [Quest](../plugins/QUEST.md).*
