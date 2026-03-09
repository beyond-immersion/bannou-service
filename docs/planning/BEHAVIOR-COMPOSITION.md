# Behavior Composition: Fingerprinted Components and the Plan Cache

> **Type**: Design
> **Status**: Aspirational
> **Created**: 2026-03-08
> **Last Updated**: 2026-03-09
> **North Stars**: #3, #5
> **Related Plugins**: Behavior, Actor, Puppetmaster, Asset

## Summary

Defines the compositional model for fingerprinted, catalogued, reusable ABML behavior components that can be discovered by similarity, strung together via continuation points, and cached as pre-computed GOAP plans to avoid redundant planning across thousands of similar agents. Builds on four existing systems (ABML compiler, continuation points, GOAP planner, Asset service) to connect them into a unified component registry and plan cache targeting 100K+ concurrent agent scale. No implementation exists yet; all described components (IComponentRegistry, PlanCache, CompositeAssembler, PlanFingerprint) are proposed architecture.

---

**Priority**: High (performance-critical for 100K+ agent scale)
**Related Documents**: [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md) (continuation points, streaming composition), [ABML-GOAP-OPPORTUNITIES.md](ABML-GOAP-OPPORTUNITIES.md) (GOAP expansion domains), [BEHAVIOR.md](../plugins/BEHAVIOR.md) (compiler, planner, cache), [ACTOR.md](../plugins/ACTOR.md) (runtime, pool deployment)
**Inspiration**: HTN plan caching (Maastricht University research), Fluid HTN's partial planning, the Theory/Storyteller/Plugin pattern

---

## Executive Summary

Bannou's behavior system already has the infrastructure for composable behaviors: continuation points create named seams in bytecode, streaming composition injects extensions at those seams, the Asset service stores compiled behaviors for cross-node/client distribution, and content hashing (SHA256) uniquely identifies every compiled behavior. What's missing is the **compositional model** -- the formalized system that treats behaviors as fingerprinted, catalogued, reusable components that can be discovered by similarity, strung together via continuation points, and cached as pre-computed plans to avoid redundant GOAP planning across thousands of similar agents.

This is not a new system. It is the connective layer between four things that already exist: the ABML compiler (which produces fingerprinted bytecode), the continuation point system (which provides composition seams), the GOAP planner (which selects action sequences), and the Asset service (which distributes compiled artifacts). The vision: **GOAP selects components from a fingerprinted library, continuation points are the composition seams, and the resulting composite plan is cached so that thousands of agents in similar states can reuse it instead of re-planning from scratch.**

This directly addresses the 100,000+ concurrent agent scale target. If 5,000 NPC farmers are all planning "find food" from similar world states, they should share one cached plan -- not run 5,000 independent A* searches that produce near-identical results.

---

## Part 1: What Already Exists

### 1.1 Content-Addressed Behavior Storage

Every compiled ABML behavior is identified by the SHA256 hash of its bytecode. This is already a fingerprint -- two behaviors with identical bytecode produce the same ID regardless of how they were authored or where they were compiled.

```
ABML YAML source
    │
    ▼
[Compiler Pipeline]  (parse → semantic analysis → flow compilation → bytecode emission)
    │
    ▼
byte[] bytecode  →  SHA256  →  behaviorId (content fingerprint)
    │
    ▼
Asset Service (stores bytecode by behaviorId, distributable to any node/client)
```

Current state: behaviors are fingerprinted and stored, but the fingerprint is used only for cache invalidation and deduplication. It is not used for discovery, similarity matching, or plan-level caching.

### 1.2 Continuation Points (Composition Seams)

Continuation points are named locations in ABML bytecode where execution pauses and external extensions can be injected. They have three properties that make them ideal composition seams:

| Property | Value for Composition |
|----------|----------------------|
| **Named** | Components can declare what kind of extension they expect at each seam |
| **Timeout with default** | If no extension is attached, the default flow executes -- graceful degradation |
| **Stackable** | Extensions themselves can contain continuation points, enabling recursive composition |

Bytecode opcodes (0x70-0x7F): `ContinuationPoint`, `ExtensionAvailable`, `YieldToExtension`.

Current state: continuation points are designed for cinematic interactivity (QTE insertion). They are not yet used as general-purpose composition seams for stringing behavior components together.

### 1.3 DocumentMerger (Compile-Time Composition)

`DocumentMerger` in `lib-behavior/Compiler/` provides ABML document composition -- merging multiple behavior documents into one before compilation. This is compile-time composition: the merger produces a single combined AST that the compiler then processes as a unit.

Current state: code exists and is registered in DI as a singleton. Not actively used in production flows.

### 1.4 BehaviorModelCache (Variant Fallback)

`BehaviorModelCache` caches compiled `BehaviorModelInterpreter` instances per character with variant fallback chains:

```
GetInterpreter(characterId, type="combat", variant="sword-and-shield")
    │
    ├── Try exact: "sword-and-shield"
    ├── Fallback: "one-handed"
    └── Fallback: "default"
```

Current state: caches complete compiled behaviors per character. Does not cache at the plan level (which components to compose) or across characters (plan reuse for similar agents).

### 1.5 BehaviorBundleManager (Grouping)

`BehaviorBundleManager` tracks membership of behaviors in bundles and creates asset bundles for distribution. Bundles group related behaviors for batch transfer.

Current state: bundles are distribution units, not composition units. They group behaviors for download efficiency, not for semantic assembly.

---

## Part 2: The Vision

### 2.1 Behavior Components

A **behavior component** is a compiled ABML behavior designed to be composed with other components rather than executed standalone. Components are:

- **Self-contained**: Each component has its own bytecode, constant pool, and string table
- **Fingerprinted**: Identified by SHA256 content hash (already the case for all behaviors)
- **Typed**: Tagged with metadata describing what the component does, what inputs it expects, and what continuation points it exposes
- **Catalogued**: Stored in a searchable registry indexed by type, tags, capability requirements, and content fingerprint

```yaml
# Example: A combat exchange component
metadata:
  component_type: combat_exchange
  tags: [melee, aggressive, overhead_strike]
  requires:
    participants: 2
    capabilities: [melee_attack, block_high]
    spatial: [facing, within_melee_range]
  exposes:
    continuation_points:
      - name: pre_strike
        description: "Moment before the attack lands"
        default: attacker_commits
      - name: post_contact
        description: "Moment after strike connects or is blocked"
        default: recover_neutral

flows:
  main:
    - animate:
        entity: ${attacker}
        animation: overhead_strike_windup
        duration: 0.8
    - continuation_point:
        name: pre_strike
        timeout: 2000
        default: attacker_commits
    - sync: strike_contact
    - animate:
        entity: ${defender}
        animation: block_high
        blend: 0.3
    - continuation_point:
        name: post_contact
        timeout: 1500
        default: recover_neutral

  attacker_commits:
    - animate:
        entity: ${attacker}
        animation: overhead_strike_commit
        duration: 0.6

  recover_neutral:
    - animate:
        entity: ${attacker}
        animation: recover_stance
    - animate:
        entity: ${defender}
        animation: recover_stance
```

### 2.2 The Component Registry

A searchable catalog of behavior components, stored in Redis with the Asset service holding the actual bytecode. The registry enables:

| Query Type | Example | Use Case |
|-----------|---------|----------|
| **By type** | "all combat_exchange components" | Building a combat choreography library |
| **By tag** | "melee AND aggressive" | Filtering for personality-appropriate exchanges |
| **By capability** | "requires melee_attack AND dodge" | Matching components to character abilities |
| **By spatial requirement** | "requires elevation_advantage" | Matching components to environmental context |
| **By fingerprint** | "SHA256:abc123..." | Exact lookup of a known component |
| **By similarity** | "similar to SHA256:abc123..." | Finding variants of a known component |

The registry is metadata-only -- component bytecode lives in the Asset service. The registry stores the fingerprint, type tags, capability requirements, exposed continuation points, and a reference to the asset ID.

### 2.3 GOAP-Driven Component Selection

When the GOAP planner (or CinematicStoryteller, or MusicStoryteller) needs to compose a multi-step sequence, it queries the component registry for available components that match the current context. Components become GOAP actions:

```
GOAP World State:
  attacker_stance: aggressive
  defender_stance: defensive
  attacker_has: [melee_attack, overhead_strike, sweep]
  defender_has: [block_high, dodge_roll, parry]
  spatial: facing, within_melee_range, near_wall
  tension: 0.6

GOAP Goal:
  tension: ">= 0.8"
  exchange_count: ">= 3"

Component-as-Action:
  "overhead_strike_exchange" (component fingerprint: abc123)
    preconditions: attacker_has melee_attack, defender_has block_high, facing
    effects: tension += 0.15, exchange_count += 1
    cost: 2

  "wall_slam_exchange" (component fingerprint: def456)
    preconditions: near_wall, attacker_has melee_attack
    effects: tension += 0.25, exchange_count += 1, spatial -= near_wall
    cost: 3

GOAP Plan: [overhead_strike, overhead_strike, wall_slam]
  → String components via continuation points
  → Cache the composite plan fingerprint
```

The planner selects a sequence of components. Each component's `post_contact` continuation point becomes the injection site for the next component's entry. The composite is itself fingerprintable -- the ordered list of component fingerprints produces a composite fingerprint.

### 2.4 The Plan Cache

This is the critical performance optimization for 100K+ agents.

**Observation**: Agents in similar world states pursuing similar goals produce similar plans. Instead of re-planning from scratch, cache the plan and reuse it.

**Cache key**: A hash of `(goal_fingerprint, world_state_fingerprint, available_actions_fingerprint)`.

**World state fingerprinting**: Not every world state variable affects every plan. The planner's action set determines which variables are decision-relevant. A "find food" plan only cares about hunger, gold, and nearby food sources -- not combat state or relationship data. The fingerprint is computed over the **decision-relevant subset** of the world state (the union of all precondition keys across available actions for the current goal).

```
Plan Cache Lookup:
  1. Compute goal fingerprint (goal conditions + priority)
  2. Identify decision-relevant state variables (from available actions' preconditions)
  3. Compute state fingerprint (hash of relevant variable values, quantized)
  4. Compute action set fingerprint (hash of available action IDs)
  5. Cache key = hash(goal_fp, state_fp, action_fp)
  6. Cache hit? → Return cached plan (list of component fingerprints)
  7. Cache miss? → Run GOAP, cache result, return plan
```

**Quantization**: Continuous values are quantized to reduce cache fragmentation. An NPC with hunger=0.31 and one with hunger=0.33 should share the same cached plan. Quantization granularity is configurable per variable (e.g., hunger quantized to 0.1 increments, gold quantized to nearest 10).

**Cache invalidation**: Plans are cached with a TTL proportional to their urgency tier. Low-urgency routine plans (eating, sleeping, commuting) cache for minutes. High-urgency reactive plans (combat, threats) cache for seconds or not at all.

| Urgency | Cache TTL | Rationale |
|---------|-----------|-----------|
| Low (routine) | 5-10 minutes | Routine behaviors change slowly |
| Medium | 30-60 seconds | Moderate reactivity needed |
| High (combat) | 0-5 seconds | World state changes rapidly |

**Scale impact**: At 100K agents, if 60% are performing routine behaviors (farming, crafting, commuting, socializing) with 100 distinct behavioral archetypes, that is ~600 agents per archetype. With plan caching, each archetype needs ~1 GOAP evaluation per cache window instead of ~600. That is a **~600x reduction in aggregate planning cost** for routine behaviors.

### 2.5 Composite Behavior Assembly

Once the planner produces a plan (sequence of component fingerprints), the composite behavior is assembled:

```
Plan: [component_A, component_B, component_C]

Assembly:
  1. Fetch component_A bytecode from Asset cache
  2. Fetch component_B bytecode from Asset cache
  3. Fetch component_C bytecode from Asset cache
  4. Link: component_A.post_contact → component_B.entry
  5. Link: component_B.post_contact → component_C.entry
  6. Produce composite BehaviorModel with merged continuation point table
  7. Fingerprint the composite (hash of ordered component fingerprints)
  8. Cache the composite in BehaviorModelCache
```

The linking step uses the existing `CinematicInterpreter` extension injection mechanism. Component B is registered as an extension at component A's terminal continuation point. Component C is registered at component B's terminal continuation point. The interpreter evaluates through the chain naturally.

**Alternative: DocumentMerger path**. For cases where compile-time composition is preferred (the full component set is known before execution), `DocumentMerger` can merge the ABML documents before compilation, producing a single monolithic bytecode. This avoids runtime extension injection overhead but requires recompilation when the component set changes. The tradeoff: runtime composition is more flexible; compile-time composition is faster to execute.

| Composition Mode | When to Use |
|-----------------|-------------|
| **Runtime** (continuation point linking) | Dynamic plans, per-encounter cinematic composition, QTE-rich sequences |
| **Compile-time** (DocumentMerger) | Stable behavioral routines, pre-computed daily schedules, static archetypes |

---

## Part 3: Architecture

### 3.1 New Components

| Component | Location | Purpose |
|-----------|----------|---------|
| `IComponentRegistry` | `bannou-service/Providers/` | Interface for component discovery and metadata queries |
| `ComponentMetadata` | `bannou-service/Behavior/` | Component type, tags, requirements, continuation point descriptors |
| `ComponentRegistryService` | `lib-behavior/Services/` | Redis-backed registry implementation |
| `PlanCache` | `lib-behavior/Services/` | Redis-backed plan cache with quantized state fingerprinting |
| `PlanFingerprint` | `sdks/behavior-compiler/Goap/` | Deterministic fingerprint computation for goals, states, and action sets |
| `CompositeAssembler` | `lib-behavior/Services/` | Links component chain via continuation point extension injection |

### 3.2 Integration Points

```
                    Component Registry (Redis)
                         │ metadata queries
                         ▼
GOAP Planner ──selects──► Component fingerprints (plan)
     │                         │
     │                    Plan Cache (Redis)
     │                    key: (goal_fp, state_fp, actions_fp)
     │                    value: [component_fp_1, component_fp_2, ...]
     │                         │
     │                    Composite Assembler
     │                         │ fetches bytecode
     │                         ▼
     │                    Asset Service (bytecode storage)
     │                         │
     │                         ▼
     │                    BehaviorModelCache
     │                    key: (composite_fp)
     │                    value: linked BehaviorModel
     │                         │
     └──────────────────────────┘
                                │
                           ActorRunner
                           (executes composite behavior)
```

### 3.3 Interaction with Existing Systems

**BehaviorBundleManager**: Bundles become "component packs" -- curated sets of components for a domain (combat exchanges, social interactions, crafting sequences). The bundle membership tracking already handles this grouping.

**Puppetmaster / DynamicBehaviorProvider**: Dynamic behaviors loaded from Asset already flow through the provider chain. Composite behaviors assembled from components use the same path -- the ActorRunner doesn't know or care whether its behavior is a monolithic document or a linked component chain.

**CinematicStoryteller**: The cinematic composition layer (from CINEMATIC-SYSTEM.md) is the first and most natural consumer of behavior components. Combat exchanges, environmental interactions, dramatic pauses, and QTE windows are all component types. CinematicStoryteller's GOAP planner selects from the component registry to compose encounter choreography.

**MusicStoryteller**: Musical phrases, chord progressions, and cadences can be modeled as components. The existing music GOAP already plans sequences of musical actions -- these become component selections from a music component registry.

---

## Part 4: The HTN Connection

### 4.1 What We're Borrowing

This architecture borrows HTN's two key scalability advantages without adopting HTN as a planning paradigm:

**Plan caching**: HTN planners naturally produce cacheable plans because decomposition paths through a shared domain are deterministic for identical world states. Our plan cache achieves the same result for GOAP: identical goal + similar state + same available actions = same plan. The quantized state fingerprinting approximates what HTN gets for free from its hierarchical structure.

**Hierarchical decomposition without hierarchy**: HTN's compound tasks decompose into subtask sequences. Our component composition achieves the same structural result: a high-level behavioral goal (GOAP) decomposes into an ordered sequence of components (each a self-contained ABML behavior) linked via continuation points. The "hierarchy" is the plan-to-component-to-bytecode stack, not an authored task network.

### 4.2 What We're NOT Borrowing

**Authored decomposition paths**: HTN requires designers to author every valid decomposition of every compound task. Our approach lets GOAP discover component compositions emergently from the component registry. A new combat exchange component added to the registry is immediately available for GOAP selection without authoring new decomposition rules.

**Predictability over emergence**: HTN sacrifices emergent plan generation for predictability. Our approach retains GOAP's emergent character -- the planner can discover novel component combinations the designer never explicitly authored. This aligns with North Star #5 (Emergent Over Authored).

### 4.3 The Hybrid Insight

The result is neither pure GOAP nor HTN but a hybrid that takes the best properties of each:

| Property | Pure GOAP | Pure HTN | Component Composition |
|----------|-----------|----------|----------------------|
| Plan generation | Emergent | Authored | Emergent (GOAP selects from registry) |
| Plan caching | Difficult (flat action space) | Natural (hierarchical paths) | Enabled (quantized state fingerprinting) |
| Composition unit | Individual actions | Compound task methods | Fingerprinted components |
| Composition seam | N/A | Method boundaries | Continuation points |
| New content | Add actions to planner | Author new decomposition paths | Add components to registry |
| Predictability | Low | High | Medium (bounded by component design) |

---

## Part 5: Domain Applications

### 5.1 Combat Choreography (Primary)

The cinematic system is the primary consumer. Component types:

| Component Type | Example | Typical Duration |
|---------------|---------|-----------------|
| `combat_exchange` | Overhead strike with high block | 2-4 seconds |
| `combat_counter` | Parry into riposte | 1-3 seconds |
| `environmental_interaction` | Wall slam, table flip, elevation leap | 2-5 seconds |
| `dramatic_pause` | Stare-down, weapon readying | 1-3 seconds |
| `group_action` | Flanking maneuver, formation shift | 3-6 seconds |
| `qte_window` | Player choice/timing moment | 1-5 seconds |
| `transition` | Stance change, weapon switch, repositioning | 0.5-2 seconds |

A 30-second cinematic combat encounter might compose 8-12 components. With 200 combat components in the registry, the combinatorial space is vast enough for emergent variety while each individual component is hand-crafted for cinematic quality.

### 5.2 Daily Routines (Scale-Critical)

The plan cache has its highest impact on routine NPC behaviors. Component types:

| Component Type | Example | Cache Characteristics |
|---------------|---------|----------------------|
| `commute` | Walk from home to workplace | High cache rate, long TTL |
| `work_shift` | Perform occupation tasks | High cache rate, medium TTL |
| `meal` | Find food, eat, socialize | High cache rate, medium TTL |
| `social_interaction` | Greeting, gossip, trade | Medium cache rate, short TTL |
| `rest` | Return home, sleep | High cache rate, long TTL |

A farmer's daily routine might compose 6-8 components. With plan caching, computing this routine once serves every farmer with similar world state (same village, similar hunger/gold, same occupation). At 100K agents with 60% in routine mode, this is the difference between 60,000 GOAP evaluations per cache window and ~300.

### 5.3 Music Composition

Musical phrases as components, linked via harmonic continuation points:

| Component Type | Example | Continuation Point |
|---------------|---------|-------------------|
| `chord_progression` | I-IV-V-I in C major | `harmonic_arrival` |
| `melodic_phrase` | 4-bar melody over progression | `phrase_end` |
| `modulation` | Pivot chord to relative minor | `new_key_established` |
| `cadence` | Perfect authentic cadence | `resolution` |
| `development` | Thematic variation on motif | `development_peak` |

### 5.4 Narrative Sequences

Story beats as components, linked via narrative continuation points:

| Component Type | Example | Continuation Point |
|---------------|---------|-------------------|
| `inciting_incident` | Discovery of threat/opportunity | `stakes_established` |
| `rising_action` | Investigation, preparation, ally gathering | `confrontation_ready` |
| `climax` | Direct confrontation with central tension | `outcome_determined` |
| `resolution` | Consequences, rewards, world state changes | `story_complete` |

---

## Part 6: Implementation Phases

### Phase 1: Component Metadata and Registry

Define the component metadata schema and build the Redis-backed registry.

1. Define `ComponentMetadata` in `bannou-service/Behavior/` (type, tags, requirements, continuation point descriptors)
2. Define `IComponentRegistry` interface in `bannou-service/Providers/`
3. Implement `ComponentRegistryService` in `lib-behavior/Services/` with Redis backing
4. Add component registration to the ABML compilation pipeline (extract metadata from `metadata:` block, register on successful compilation with caching enabled)
5. Add schema for component registry endpoints to `behavior-api.yaml`

Deliverable: Components can be compiled, registered, and queried by type/tag/capability.

### Phase 2: Plan Fingerprinting and Caching

Build the plan cache with quantized state fingerprinting.

1. Implement `PlanFingerprint` in `sdks/behavior-compiler/Goap/` (deterministic hashing for goals, quantized states, and action sets)
2. Implement `PlanCache` in `lib-behavior/Services/` with Redis backing and urgency-tiered TTL
3. Integrate plan cache into `GoapPlanner.PlanAsync` (cache check before A* search, cache write after successful plan)
4. Add quantization configuration to `BehaviorServiceConfiguration` (per-variable granularity)
5. Add cache hit/miss metrics to telemetry

Deliverable: GOAP plans are cached and reused across agents with similar states. Cache hit rate is measurable.

### Phase 3: Composite Assembly

Build the runtime linking of component chains via continuation points.

1. Implement `CompositeAssembler` in `lib-behavior/Services/` (fetches component bytecode, links via continuation point extension injection)
2. Wire `DocumentMerger` into DI for compile-time composition path
3. Add composite fingerprint computation (ordered hash of component fingerprints)
4. Integrate composite caching into `BehaviorModelCache`
5. Add component chain assembly to `ActorRunner` plan execution path

Deliverable: Plans produce linked component chains that execute as unified behaviors.

### Phase 4: Combat Component Library

Populate the component registry with the first domain-specific component set.

1. Author 50-100 combat exchange components covering melee, ranged, magic, and environmental interactions
2. Author 20-30 transition components (stance changes, repositioning, weapon switches)
3. Author 10-20 dramatic pause components (stare-downs, taunts, rallying cries)
4. Author 10-15 QTE window components with progressive agency integration
5. Integration test: CinematicStoryteller composes encounter from registry, executes via CinematicInterpreter

Deliverable: End-to-end combat choreography from component composition.

### Phase 5: Routine Component Library and Scale Validation

Populate routine components and validate plan cache performance at scale.

1. Author routine components (commute, work, meal, social, rest) for 10+ occupation types
2. Load test: 100K simulated agents with plan caching enabled vs. disabled
3. Measure and tune: cache hit rate, aggregate planning cost, quantization granularity
4. Profile: memory footprint of component registry, plan cache, and composite cache

Deliverable: Validated plan cache performance at target scale. Quantified speedup from component reuse.

---

## Part 7: Open Questions

### 7.1 Component Versioning

When a component is updated (new bytecode, same semantic intent), should cached plans using the old version be invalidated immediately or aged out? Immediate invalidation is safer but causes a thundering herd of re-planning. Age-out is smoother but means some agents briefly use stale components.

### 7.2 Cross-Domain Component Sharing

Can a "dramatic_pause" component be shared between combat choreography and narrative sequences? The continuation point names and input requirements may differ. Polymorphic components (same bytecode, different metadata interpretations) vs. domain-specific copies.

### 7.3 Component Quality Scoring

How do we rank components for GOAP selection? Pure cost-based selection works for functional behavior, but cinematic quality is subjective. A "wall_slam" exchange might be higher quality than a "basic_swing" exchange in terms of spectacle, but both satisfy the same GOAP preconditions/effects. Quality-as-cost-modifier? Separate quality dimension? God-actor aesthetic preferences as cost modifiers?

### 7.4 Client-Side Component Library

Game clients need components for client-side prediction and smoothing. Should clients maintain their own component registry (synced from server), or fetch components on-demand from Asset? Bandwidth vs. responsiveness tradeoff. Pre-loading component bundles for the player's current context (combat components when in a dangerous area, social components when in a town) is likely the right approach -- and this is exactly what `BehaviorBundleManager` already supports.

### 7.5 Plan Cache Consistency in Distributed Deployments

In multi-node deployments, each pool node could maintain its own local plan cache (fast, no network hop) or share a centralized Redis cache (consistent, higher latency). Hybrid approach: local L1 cache with short TTL backed by Redis L2 cache with longer TTL? The plan cache is read-heavy and tolerant of staleness (a slightly stale plan is still valid, just potentially suboptimal), making eventual consistency acceptable.

---

*This document captures the architectural vision for fingerprinted behavior component composition and plan caching. For the runtime execution infrastructure that this builds on, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). For the GOAP planner that drives component selection, see [BEHAVIOR.md](../plugins/BEHAVIOR.md). For the distributed actor runtime that executes composites at scale, see [ACTOR.md](../plugins/ACTOR.md).*
