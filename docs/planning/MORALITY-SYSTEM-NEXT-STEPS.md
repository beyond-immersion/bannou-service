# Morality System - NPC Conscience and Social Norms

> **Version**: 1.0
> **Status**: Individual services implemented; cross-service integration incomplete
> **Location**: `plugins/lib-faction/`, `plugins/lib-obligation/`
> **Related**: [Faction Deep Dive](../plugins/FACTION.md), [Obligation Deep Dive](../plugins/OBLIGATION.md), [Behavior System](./BEHAVIOR-SYSTEM.md), [Seed System](./SEED-SYSTEM.md), [Service Hierarchy](../reference/SERVICE-HIERARCHY.md)
> **Origin**: [GitHub Issue #410 - Second Thoughts: Prospective Consequence Evaluation for NPC Cognition](https://github.com/beyond-immersion/bannou-service/issues/410)

The Morality System enables NPCs to have "second thoughts" -- to evaluate the moral, social, and contractual consequences of their intended actions before committing to them. An honest merchant hesitates before deceiving a customer. A loyal knight resists betraying their lord even when tactically advantageous. A character in lawless territory acts with less restraint than one in a temple district. This is the bridge between Arcadia's rule-based behavior system (GOAP) and its emergent social simulation.

---

## 8. The Actor Cognition Integration

### Current 5-Stage Pipeline

The cognition pipeline processes perceptions into intentions through five stages, each containing one or more handlers. Stage names are constants defined in `CognitionStages` (`bannou-service/Behavior/ICognitionTemplate.cs`); handler names are what ABML behavior authors reference in YAML:

```
Stage (constant)  │  ABML Handlers                  │  Purpose
──────────────────┼─────────────────────────────────┼──────────────────────────────────
1. filter         │  filter_attention               │  What events did the NPC notice?
2. memory_query   │  memory_query                   │  Retrieve relevant memories
3. significance   │  assess_significance            │  How significant is each perception?
4. storage        │  store_memory                   │  Record significant perceptions
5. intention      │  evaluate_goal_impact           │  How do events affect current goals?
                  │  trigger_goap_replan            │  Should the GOAP planner re-evaluate?
```

Note: The `intention` stage contains two handlers (`evaluate_goal_impact` + `trigger_goap_replan`) within one stage container. Different cognition templates include different subsets of stages -- the creature template skips `memory_query` and `storage`, while the event_brain template uses only `filter` and `intention`.

### Planned 6-Stage Pipeline (with evaluate_consequences)

```
Stage (constant)          │  ABML Handlers                  │  Purpose
──────────────────────────┼─────────────────────────────────┼───────────────────────────────
1. filter                 │  filter_attention               │  What events did the NPC notice?
2. memory_query           │  memory_query                   │  Retrieve relevant memories
3. significance           │  assess_significance            │  How significant is each?
4. storage                │  store_memory                   │  Record significant perceptions
5. evaluate_consequences  │  evaluate_consequences          │  What are the moral costs?  ← NEW
6. intention              │  evaluate_goal_impact           │  How do events affect goals?
                          │  trigger_goap_replan            │  Should GOAP re-evaluate?
```

The `evaluate_consequences` stage would:

1. **Proactive evaluation**: Read `${obligations.violation_cost.<type>}` for all action tags in the current GOAP plan
2. **Reactive detection**: If violation costs changed significantly since the plan was created (new contract, new faction membership, moved to different territory), force a replan
3. **Cost modifier injection**: Write dynamic cost modifiers to the GOAP execution context for the planner to consume during A* expansion
4. **Knowing violation flagging**: Mark actions where the NPC is aware of the moral cost -- this flag triggers post-violation feedback (personality drift, guilt composite, encounter memory) after execution

### GOAP Planner Cost Modification

The GOAP planner's A* search expansion currently uses static action costs defined in ABML metadata. The modification adds an optional dynamic cost modifier via `PlanningOptions.CostModifier` — it's a per-invocation planning configuration parameter, alongside `MaxDepth`, `TimeoutMs`, and `HeuristicWeight`:

```csharp
// Current: static cost only
var gCost = current.GCost + action.Cost;

// With CostModifier on PlanningOptions: static + dynamic obligation cost
var dynamicCost = options.CostModifier?.Invoke(action, current.State) ?? 0f;
if (dynamicCost < 0f) dynamicCost = 0f;
var gCost = current.GCost + action.Cost + dynamicCost;
```

The `evaluate_consequences` handler builds the cost modifier function from obligation data and stores it in the execution scope as `"conscience_cost_modifier"`. The `trigger_goap_replan` handler reads it from scope and passes it to `PlanningOptions` when invoking the planner.

`PlannedAction` carries a `DynamicCost` property so that post-plan code can identify which actions had obligation penalties applied. This is how `knowing_violation` flagging works — the replan handler compares `PlannedAction.DynamicCost` against the `conscience_violations` dictionary from scope.

**Performance target** (from #410): <5ms per tick for characters with ~10 active obligations and a 3-5 action GOAP plan. This is a premium cognition feature for named NPCs, faction leaders, and player companions -- not for 100,000 ambient NPCs.

### Opt-In via ABML Metadata

Not all actors need moral reasoning. A mindless dungeon creature doesn't hesitate before attacking. The `conscience` ABML metadata flag controls whether the `evaluate_consequences` stage runs:

```yaml
# ABML behavior with moral reasoning
metadata:
  conscience: true   # Enable evaluate_consequences stage

# ABML behavior without (default)
metadata:
  conscience: false  # Skip -- no moral evaluation
```

When `conscience: false` (or absent), the handler self-short-circuits at runtime: it checks the behavior document's `Conscience` metadata from the execution context and returns immediately with no output. This is effectively zero-cost (one boolean check per tick). After #422 lands (wiring `CognitionBuilder` into `ActorRunner`), the template system can eliminate the stage entirely via `DisableHandlerOverride` — true compile-time pipeline elimination. Both mechanisms coexist as defense-in-depth. Even when `conscience: true`, the stage dynamically short-circuits when:

- The `ObligationProviderFactory` returns an empty provider (no obligations for this character)
- The current GOAP plan contains no actions with obligation-relevant tags
- **Urgency bypass**: Perception urgency exceeds a threshold (survival instinct overrides moral deliberation -- a character in mortal danger doesn't pause to consider guild obligations; this mirrors real human psychology where panic overrides moral reasoning)

---

## 9. Design Decisions

These decisions were made in #410 and remain the architectural direction:

| Decision | Rationale |
|----------|-----------|
| **Social norms are NOT contract instances** | Creating implicit contracts for every NPC's faction norms would produce hundreds of thousands of contract instances for 100K NPCs. Norms are faction configuration, not contract state. Obligation queries faction norms directly via the `/faction/norm/query-applicable` API, bypassing the contract pipeline entirely. |
| **Guilt is a composite emotion, not a new dimension** | Guilt = high stress + elevated sadness + low comfort + reduced joy. The existing 8-dimension emotional model is sufficient. Adding a 9th dimension would ripple through every emotional system. |
| **Player characters DO experience second thoughts** | From PLAYER-VISION.md: "Characters can resist or resent being pushed against their nature." The character co-pilot's moral reasoning is progressive agency made tangible. |
| **NPC-to-NPC norm enforcement via encounter recording** | When an NPC witnesses another violating a shared norm, it creates encounter memories with negative sentiment. This feeds back into relationship dynamics. |
| **Faction norm vocabulary is opaque strings** | Violation types are not enums. The vocabulary grows organically as new norms are authored. This matches the Collection/Seed pattern of opaque type codes. |
| **lib-moral was eliminated** | Personality-weighted moral reasoning is built into obligation's two-layer design. The social/cultural norm framework is provided by lib-faction. No separate service needed. |
| **Obligation reads faction norms, faction doesn't know about obligation** | Obligation has an optional soft dependency on Faction (L4→L4, permitted with graceful degradation per SERVICE-HIERARCHY.md). Faction has no knowledge of Obligation. This preserves clean separation -- faction serves multiple consumers, not just moral reasoning. |

---

## 10. Integration Gaps (Current State)

Each service works internally, but the cross-service integration that makes the morality system function as a coherent pipeline is incomplete.

### Gap 1: Faction Norms Do Not Reach Obligation (Critical)

**What should happen**: Obligation queries Faction's `QueryApplicableNorms` to get the merged norm map, then includes those norms in its violation cost computation alongside contractual obligations.

**Current state**: Obligation has ZERO references to lib-faction. It only reads from lib-contract. Faction norms exist in isolation -- the entire social/cultural layer is invisible to the obligation cost pipeline.

**Impact**: NPCs only consider formal contractual obligations (guild charters, trade agreements). Ambient social norms (realm honor codes, local laws, cultural taboos) are invisible to the cognition pipeline. This is the most fundamental gap: without it, the "two-layer design" described in the morality system guide only has one layer.

**Bridge mechanism**: Obligation should soft-depend on Faction (L4→L4 optional). When available, obligation's cache rebuild queries `QueryApplicableNorms` with the character's current location context and merges norm penalties into the violation cost map. When Faction is disabled, obligation functions with contracts alone.

**Implementation details to consider**:
- Obligation needs `IFactionClient` resolved via `IServiceProvider.GetService<T>()` with null check (graceful degradation)
- The `QueryApplicableNorms` call requires `characterId` + `locationId` -- obligation must determine the character's current location (possibly via a lib-location call, a new parameter on the cache rebuild trigger, or reading from the `${location.*}` provider context)
- Obligation's cache is currently keyed by `{characterId}` only. With location-dependent norms, the cache either: (a) includes location in the key (`{characterId}:{locationId}`) for accuracy but higher cardinality, or (b) stores a location snapshot alongside the manifest that's invalidated on movement. Option (b) is simpler but may serve stale norms during movement -- acceptable if `CacheTtlMinutes` is short enough.
- Obligation must subscribe to faction events for cache invalidation. This requires new entries in `obligation-events.yaml` under `x-event-subscriptions`: `faction.norm.defined`, `faction.norm.updated`, `faction.norm.deleted`, `faction.member.added`, `faction.member.removed`, `faction.territory.claimed`, `faction.territory.released`, `faction.realm-baseline.designated`

### Gap 2: Faction Variable Provider Missing Norm/Territory Data (Critical)

**What should happen**: The `${faction.*}` namespace includes norm and territory awareness variables for ABML behavior expressions.

**Current state**: `FactionProviderFactory` only loads membership data (count, names, codes, per-faction details like role/phase/status). It doesn't query norm stores or territory stores.

**Impact**: ABML behaviors cannot check `${faction.has_norm.theft}` or `${faction.in_controlled_territory}`. The `evaluate_consequences` handler can't reason about social context in behavior expressions.

**Missing variables** (per original plan):

| Planned Variable | Impact |
|-----------------|--------|
| `${faction.primary_faction}` | No "highest-role faction" identification for behavior shortcuts |
| `${faction.primary_faction_phase}` | Depends on primary faction concept |
| `${faction.has_norm.<type>}` | ABML cannot check norm existence per violation type |
| `${faction.norm_penalty.<type>}` | ABML cannot read base penalty per violation type |
| `${faction.in_controlled_territory}` | ABML cannot check territory control context |

**Fix**: `FactionProviderFactory` needs to query the norm resolution endpoint and territory store during `CreateAsync`. This requires the character's current location -- the `IVariableProviderFactory.CreateAsync` signature currently only receives `entityId`. Options:
- (a) Extend the factory interface to accept a context dictionary (cleanest if multiple providers need location — faction clearly does, others may)
- (b) Have the faction provider call lib-location to determine the character's current position (adds L4→L2 dependency, which is permitted)
- (c) Accept that norm/territory variables require a location provider to have already populated the execution scope's shared state -- the faction provider reads location from there

### Gap 3: No evaluate_consequences Cognition Stage (Important)

**What should happen**: A new cognition stage between `storage` and `intention` that reads obligation costs and modifies GOAP action costs.

**Current state**: The stage doesn't exist. The cognition pipeline has 5 stages: `filter`, `memory_query`, `significance`, `storage`, `intention` (defined in `CognitionStages` constants and registered in `CognitionTemplateRegistry`). The obligation variable provider exposes data, but nothing in the Actor pipeline reads it to modify GOAP planning.

**Impact**: Even when obligation costs are computed correctly, they don't affect NPC behavior because the GOAP planner never sees them.

**Architecture note**: Cognition handlers are standard ABML action handlers registered in `DocumentExecutorFactory.RegisterCognitionHandlers()` (in lib-actor) and dispatched by the tree-walking `DocumentExecutor` when ABML documents list them as actions. Currently, `ActorRunner` executes ABML flows directly — it does not consume `CognitionBuilder` or the `CognitionTemplateRegistry` (#422 tracks wiring this). The template override system (`DisableHandlerOverride`, `ParameterOverride`, etc.) is built, DI-registered, and tested, but has no runtime application point until #422 lands. In the meantime, the handler self-gates on the behavior document's `conscience` metadata flag. A new `CognitionStages.EvaluateConsequences = "evaluate_consequences"` constant, a corresponding handler, and a stage definition in the humanoid template (forward-compatible for #422) would slot in between `Storage` and `Intention`. The handler receives `IActionConsequenceEvaluator` implementations via DI (same inversion pattern as `IVariableProviderFactory`) and would:

1. Check for `conscience: true` metadata (self-short-circuit if absent — primary gate pre-#422)
2. Call registered `IActionConsequenceEvaluator` instances (e.g., ObligationConsequenceEvaluator) with action tags
3. Build a cost modifier function from the aggregated consequence results
4. Store the cost modifier in scope as `"conscience_cost_modifier"` for the GOAP replan handler to read
5. Flag `knowing_violation` for actions where the NPC proceeds despite moral cost

### Gap 4: Guild Charter Contract Integration (Moderate)

**What should happen**: When a character joins a guild faction, a formal contract instance is created via lib-contract with behavioral clauses matching the faction's norms.

**Current state**: Faction membership is managed directly without contract backing. `IContractClient` is not referenced anywhere in lib-faction.

**Impact**: Guild membership obligations don't flow through the contract pipeline, which means they're invisible to obligation's contract-based cost computation. **Partially mitigated if Gap 1 is resolved** -- when obligation reads faction norms directly, guild norms become visible without contract intermediation. However, the lack of contract backing means:
- No formal breach/cure mechanics for guild rule violations
- No contract audit trail for membership obligations
- Guild charters can't use contract milestones for progressive discipline

**Decision needed**: Is direct norm reading (Gap 1 fix) sufficient, or do guild charters need formal contract backing for enforcement mechanics? The #410 design originally proposed faction membership as implicit contracts, but Decision 1 (above) rejected implicit contracts at scale. A hybrid approach is possible: formal contracts for guild leadership/officer roles only, direct norms for regular members.

### Gap 5: Personality Trait Mapping is Static (Minor)

**What should happen**: The mapping from violation types to personality traits should be data-driven (part of action mapping store or configurable).

**Current state**: A static dictionary (`ViolationTypeTraitMap`) maps 10 known violation types to trait combinations:

| Violation Type | Mapped Traits |
|---------------|---------------|
| `theft` | Honesty + Conscientiousness |
| `deception` | Honesty |
| `violence` | Agreeableness |
| `honor_combat` | Conscientiousness + Loyalty |
| `betrayal` | Loyalty |
| `exploitation` | Agreeableness + Honesty |
| `oath_breaking` | Loyalty + Conscientiousness |
| `trespass` | Conscientiousness |
| `disrespect` | Agreeableness |
| `contraband` | Conscientiousness |
| *unknown type* | Conscientiousness (default fallback) |

**Impact**: New violation types silently degrade to the default mapping. Not a blocker -- the core vocabulary covers primary use cases -- but creates a maintenance burden as the violation type vocabulary grows organically through faction norms. Adding a norm type like `blasphemy` would fall through to conscientiousness alone when it arguably should map to loyalty + agreeableness.

---

## 11. Implementation Roadmap

### Phase 1: Connect Faction Norms to Obligation (Critical Path)

**Goal**: Faction norms flow into obligation's violation cost map.

**Why this is Phase 1**: Without this, obligation only sees contract obligations. The entire social/cultural layer (realm honor codes, local laws, guild norms as faction-defined norms) is invisible. This is the single most impactful integration gap.

1. Add `IFactionClient` as soft dependency in ObligationService (runtime resolution via `IServiceProvider.GetService<IFactionClient>()` with null check)
2. In `RebuildObligationCacheAsync`, after querying contracts, also query `/faction/norm/query-applicable` with `characterId` + `locationId` when Faction is available
3. Merge faction norm penalties into the `ViolationCostMap` alongside contract penalties -- same data structure, different source
4. Apply personality weighting to faction norms the same as contract obligations (the `ComputeWeightedPenalty` method is source-agnostic -- it only cares about violation type and personality traits)
5. Add event subscriptions to `obligation-events.yaml` for faction cache invalidation:
   - `faction.norm.defined`, `faction.norm.updated`, `faction.norm.deleted` → rebuild cache for affected faction's members
   - `faction.member.added`, `faction.member.removed` → rebuild cache for affected character
   - `faction.territory.claimed`, `faction.territory.released` → rebuild cache for characters at affected location
   - `faction.realm-baseline.designated` → rebuild cache for all characters in affected realm (broad invalidation -- consider rate-limiting)
6. Determine location context strategy: either accept location as an API parameter (caller provides), resolve via lib-location client, or use a location snapshot cached alongside the obligation manifest

### Phase 2: Complete Faction Variable Provider

**Goal**: ABML behaviors can reason about social context.

1. Add `${faction.primary_faction}` / `${faction.primary_faction_phase}` (faction where character holds highest role)
2. Add `${faction.has_norm.<type>}` by calling the norm resolution endpoint during provider creation (uses merged norm map from `QueryApplicableNorms`)
3. Add `${faction.norm_penalty.<type>}` for direct norm penalty access from the merged map
4. Add `${faction.in_controlled_territory}` by checking territory claims against character's current location
5. Resolve location context -- determine whether to extend `IVariableProviderFactory.CreateAsync` signature, add a lib-location dependency, or read from shared execution scope state

### Phase 3: Add evaluate_consequences Cognition Stage

**Goal**: Obligation costs actually affect NPC behavior.

**Why this is Phase 3, not Phase 1**: The stage is useless without data flowing through the variable providers. Phases 1-2 ensure the data is there; this phase makes the actor read it.

1. Add `CognitionStages.EvaluateConsequences = "evaluate_consequences"` constant to `ICognitionTemplate.cs`
2. Add `CostModifier` property to `PlanningOptions` — it's a per-invocation planning configuration parameter alongside `MaxDepth`, `TimeoutMs`, and `HeuristicWeight`.
3. Create `IActionConsequenceEvaluator` interface in `bannou-service/Providers/` — same DI inversion pattern as `IVariableProviderFactory`. L4 services (lib-obligation, eventually lib-faction) implement this to provide consequence data to the L2 Actor runtime.
4. Implement `evaluate_consequences` handler in a new `EvaluateConsequencesHandler` class:
   - Self-gates on `Conscience` metadata flag from execution context (primary gate mechanism pre-#422; defense-in-depth after)
   - Calls registered `IActionConsequenceEvaluator` instances with action tags, aggregates results
   - Builds cost modifier function and stores in scope as `"conscience_cost_modifier"` for the GOAP replan handler
   - Flags `knowing_violation` in working memory for actions where cost exceeds a threshold but the NPC proceeds anyway
5. Register the handler: DI singleton in `ActorServicePlugin.cs`, handler registration in `DocumentExecutorFactory.RegisterCognitionHandlers()` (between StoreMemory and EvaluateGoalImpact)
6. Add `conscience: true/false` ABML metadata flag — handler self-short-circuits when `false` or absent. After #422, `ActorRunner` also injects `DisableHandlerOverride` to eliminate the stage from the built cognition pipeline entirely.
7. Add stage definition to humanoid cognition template in `CognitionTemplateRegistry` (forward-compatible preparation — takes effect once #422 wires `CognitionBuilder` into `ActorRunner`)
8. Implement dynamic short-circuits (in addition to the conscience metadata gate):
   - No `IActionConsequenceEvaluator` instances registered in DI → skip (zero cost)
   - Evaluators return empty results → skip (zero cost)
   - No obligation-relevant action tags in current plan → skip
   - Urgency bypass: perception urgency exceeds threshold → skip (survival overrides morality)
9. **Performance budget**: Configurable `ConscienceEvaluationTimeoutMs` (default: 5ms). If evaluation exceeds budget, use last-known costs rather than blocking the cognition tick.

### Phase 4: Post-Violation Feedback Loop

**Goal**: Moral choices have lasting consequences that feed back into future behavior.

**Why this matters for the vision**: The content flywheel depends on rich character archives. A character who "knowingly broke their guild oath and carried guilt for years" produces richer story seeds than "character was in guild, character left guild." The feedback loop is what makes moral choices narratively generative.

1. **Personality drift**: Subscribe to `obligation.violation.reported` events in lib-character-personality:
   - `OATH_BROKEN` experience type → honesty decreases, conscientiousness decreases (magnitude proportional to violation cost)
   - `RESISTED_TEMPTATION` experience type (when obligation cost prevented action) → honesty increases, conscientiousness increases
   - `GUILTY_CONSCIENCE` experience type (post-violation stress) → neuroticism increases
2. **Emotional composite**: Modify emotional state on knowing violation:
   - Guilt pattern: stress ↑, sadness ↑, comfort ↓, joy ↓ (composite of existing dimensions, not a new dimension per Decision 2)
   - Intensity proportional to violation cost -- a minor transgression produces a flicker, a major betrayal produces a lasting emotional shift
3. **Encounter memory**: Subscribe to `obligation.violation.reported` in lib-character-encounter:
   - If violation involved another character (theft from a friend, betrayal of a companion): record encounter with negative sentiment
   - Witness encounters: if another NPC witnessed the violation, they also record a memory (NPC-to-NPC norm enforcement per Decision 4)
4. **Contract breach**: Already partially implemented -- `BreachReportEnabled` config controls whether `ReportViolationAsync` auto-reports to lib-contract
5. **Player character "second thoughts"**: The `evaluate_consequences` stage runs for player characters too (their NPC brain is always active per Decision 3). When the guardian spirit pushes toward an action with high moral cost:
   - Character resists proportional to violation cost x personality traits
   - Co-pilot suggests "Your character is uncomfortable with this"
   - If forced, the knowing violation feedback applies -- the character changes over time

### Phase 5: Data-Driven Trait Mapping

**Goal**: Violation-to-trait mapping is extensible without code changes.

1. Move personality trait mapping from the static dictionary in `ObligationService` to the action mapping store (alongside GOAP action tag → violation type mappings) or to a dedicated configuration section
2. Allow faction norms to optionally specify their own trait associations (e.g., a faction norm for `blasphemy` could declare `["loyalty", "agreeableness"]` as relevant traits)
3. Default fallback to conscientiousness preserved for types without explicit mapping
4. Validate trait names against known personality dimensions at mapping registration time

---

*This document captures the morality system implementation plan as of its writing date. For design details, see the [Morality System Guide](../guides/MORALITY-SYSTEM.md). For implementation details, see the individual deep dives: [Faction](../plugins/FACTION.md), [Obligation](../plugins/OBLIGATION.md). For the original design specification, see [GitHub Issue #410](https://github.com/beyond-immersion/bannou-service/issues/410).*
