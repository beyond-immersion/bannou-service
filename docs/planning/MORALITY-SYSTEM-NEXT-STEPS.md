# Morality System - NPC Conscience and Social Norms

> **Version**: 1.0
> **Status**: Individual services implemented; cross-service integration incomplete
> **Location**: `plugins/lib-faction/`, `plugins/lib-obligation/`, `plugins/lib-status/`
> **Related**: [Faction Deep Dive](../plugins/FACTION.md), [Obligation Deep Dive](../plugins/OBLIGATION.md), [Status Deep Dive](../plugins/STATUS.md), [Behavior System](./BEHAVIOR-SYSTEM.md), [Seed System](./SEED-SYSTEM.md), [Service Hierarchy](../reference/SERVICE-HIERARCHY.md)
> **Origin**: [GitHub Issue #410 - Second Thoughts: Prospective Consequence Evaluation for NPC Cognition](https://github.com/beyond-immersion/bannou-service/issues/410)

The Morality System enables NPCs to have "second thoughts" -- to evaluate the moral, social, and contractual consequences of their intended actions before committing to them. An honest merchant hesitates before deceiving a customer. A loyal knight resists betraying their lord even when tactically advantageous. A character in lawless territory acts with less restraint than one in a temple district. This is the bridge between Arcadia's rule-based behavior system (GOAP) and its emergent social simulation.

---

## 8. The Actor Cognition Integration

### Current 5-Stage Pipeline

```
1. filter_perception    → What events did the NPC notice?
2. appraise_relevance   → How relevant is each event?
3. store_memory         → Record significant events
4. evaluate_goal_impact → How do events affect current goals?
5. trigger_goap_replan  → Should the GOAP planner re-evaluate?
```

### Planned 6-Stage Pipeline (with evaluate_consequences)

```
1. filter_perception    → What events did the NPC notice?
2. appraise_relevance   → How relevant is each event?
3. store_memory         → Record significant events
4. evaluate_consequences → What are the moral costs of planned actions? ← NEW
5. evaluate_goal_impact → How do events affect current goals?
6. trigger_goap_replan  → Should the GOAP planner re-evaluate?
```

The `evaluate_consequences` stage would:

1. **Proactive**: Read `${obligations.violation_cost.<type>}` for all actions in the current GOAP plan
2. **Reactive**: If costs changed significantly since plan was created, force replan
3. **Store modifiers**: Write cost modifiers to execution state for GOAP planner to use
4. **Flag knowing violations**: Mark actions where the NPC is aware of the moral cost

### GOAP Planner Cost Modification

The GOAP planner's A* expansion currently uses static action costs:

```csharp
var gCost = current.GCost + action.Cost;  // action.Cost is immutable
```

The planned modification adds an optional cost modifier:

```csharp
var dynamicCost = costModifier?.Invoke(action, current.State) ?? 0f;
var gCost = current.GCost + action.Cost + dynamicCost;
```

This is backward-compatible (null modifier = current behavior). The `evaluate_consequences` stage builds the cost modifier function from obligation data and passes it through the execution context.

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

When `conscience: false` (or absent), the stage is entirely skipped at compile time -- zero runtime cost.

---

## 9. Design Decisions

These decisions were made in #410 and remain the architectural direction:

| Decision | Rationale |
|----------|-----------|
| **Social norms are NOT contract instances** | Creating implicit contracts for every NPC's faction norms would produce hundreds of thousands of contract instances for 100K NPCs. Norms are faction configuration, not contract state. |
| **Guilt is a composite emotion, not a new dimension** | Guilt = high stress + elevated sadness + low comfort + reduced joy. The existing 8-dimension emotional model is sufficient. Adding a 9th dimension would ripple through every emotional system. |
| **Player characters DO experience second thoughts** | From PLAYER-VISION.md: "Characters can resist or resent being pushed against their nature." The character co-pilot's moral reasoning is progressive agency made tangible. |
| **NPC-to-NPC norm enforcement via encounter recording** | When an NPC witnesses another violating a shared norm, it creates encounter memories with negative sentiment. This feeds back into relationship dynamics. |
| **Faction norm vocabulary is opaque strings** | Violation types are not enums. The vocabulary grows organically as new norms are authored. This matches the Collection/Seed pattern of opaque type codes. |
| **lib-moral was eliminated** | Personality-weighted moral reasoning is built into obligation's two-layer design. The norm framework is provided by lib-faction. No separate service needed. |
| **Obligation reads faction norms, faction doesn't know about obligation** | Obligation has an optional soft dependency on Faction. Faction has no knowledge of Obligation. This preserves clean separation. |

---

## 10. Integration Gaps (Current State)

Each service works internally, but the cross-service integration that makes the morality system function as a coherent pipeline is incomplete.

### Gap 1: Faction Norms Do Not Reach Obligation (Critical)

**What should happen**: Obligation queries Faction's `QueryApplicableNorms` to get the merged norm map, then includes those norms in its violation cost computation alongside contractual obligations.

**Current state**: Obligation has ZERO references to lib-faction. It only reads from lib-contract. Faction norms exist in isolation.

**Impact**: NPCs only consider formal contractual obligations (guild charters, trade agreements). Ambient social norms (realm honor codes, local laws, cultural taboos) are invisible to the cognition pipeline.

**Bridge mechanism**: Obligation should soft-depend on Faction (L4 optional). When available, obligation's cache rebuild queries `QueryApplicableNorms` with the character's current location context and merges norm penalties into the violation cost map. When Faction is disabled, obligation functions with contracts alone.

### Gap 2: Faction Variable Provider Missing Norm/Territory Data (Critical)

**What should happen**: The `${faction.*}` namespace includes norm and territory awareness variables for ABML behavior expressions.

**Current state**: FactionProviderFactory only loads membership data. It doesn't query norm stores or territory stores.

**Impact**: ABML behaviors cannot check `${faction.has_norm.theft}` or `${faction.in_controlled_territory}`. The cognition pipeline can't reason about social context in behavior expressions.

**Fix**: FactionProviderFactory needs to query the norm store and territory store during `CreateAsync`. This may require the character's current location (from `${location.*}` provider or passed through the factory interface).

### Gap 3: No evaluate_consequences Cognition Stage (Important)

**What should happen**: A 6th cognition stage between `store_memory` and `evaluate_goal_impact` that reads obligation costs and modifies GOAP action costs.

**Current state**: The stage doesn't exist. Obligation data is available via the variable provider but nothing in the Actor pipeline reads it to modify GOAP planning.

**Impact**: Even when obligation costs are computed correctly, they don't affect NPC behavior because the GOAP planner never sees them.

### Gap 4: No Status Variable Provider (Moderate)

**What should happen**: `${status.*}` namespace available in ABML for querying active effects.

**Current state**: Listed as a potential extension in the Status deep dive. Not implemented.

**Impact**: ABML behaviors can't reference status effects. Moral reasoning can't consider "under divine protection" or "marked as criminal" states.

### Gap 5: Guild Charter Contract Integration (Moderate)

**What should happen**: When a character joins a guild faction, a formal contract instance is created via lib-contract with behavioral clauses matching the faction's norms.

**Current state**: Faction membership is managed directly without contract backing. `IContractClient` is not referenced anywhere in lib-faction.

**Impact**: Guild membership obligations don't flow through the contract pipeline, which means they're invisible to obligation's contract-based cost computation. Partially mitigated if Gap 1 is resolved (obligation reads faction norms directly).

### Gap 6: Personality Trait Mapping is Hardcoded (Minor)

**What should happen**: The mapping from violation types to personality traits should be data-driven (part of action mapping store or configurable).

**Current state**: Hardcoded switch statement mapping 10 known types to traits, with a default fallback to conscientiousness.

**Impact**: New violation types silently degrade to the default mapping. Not a blocker but creates maintenance burden.

---

## 11. Implementation Roadmap

### Phase 1: Connect Faction Norms to Obligation (Critical Path)

**Goal**: Faction norms flow into obligation's violation cost map.

1. Add `IFactionClient` as soft dependency in ObligationService (runtime resolution via `IServiceProvider`)
2. In `RebuildObligationCacheAsync`, after querying contracts, also query `/faction/norm/query-applicable` when Faction is available
3. Merge faction norm penalties into the `ViolationCostMap` alongside contract penalties
4. Apply personality weighting to faction norms the same as contract obligations
5. Invalidate obligation cache on relevant faction events (norm defined/updated/deleted, membership changed, territory changed)

### Phase 2: Complete Faction Variable Provider

**Goal**: ABML behaviors can reason about social context.

1. Add `${faction.primary_faction}` / `${faction.primary_faction_phase}` (highest-role faction)
2. Add `${faction.has_norm.<type>}` by querying the norm store during provider creation
3. Add `${faction.norm_penalty.<type>}` for direct norm cost access
4. Add `${faction.in_controlled_territory}` by checking territory claims against character's current location
5. Consider passing location context through the `CreateAsync` factory interface

### Phase 3: Add evaluate_consequences Cognition Stage

**Goal**: Obligation costs actually affect NPC behavior.

1. Add optional `costModifier` parameter to `IGoapPlanner.PlanAsync`
2. Implement `evaluate_consequences` handler in the cognition pipeline
3. Add `conscience: true/false` ABML metadata flag
4. Compile-time stage elimination when `conscience: false`
5. Dynamic short-circuit when no obligations exist (zero runtime cost for non-obligated NPCs)
6. Urgency bypass (high urgency situations skip moral evaluation)

### Phase 4: Status Variable Provider

**Goal**: ABML behaviors can query active effects.

1. Implement `StatusProviderFactory : IVariableProviderFactory` providing `${status.*}` namespace
2. Register in `StatusServicePlugin.ConfigureServices`
3. Cache-backed (use existing active status cache)
4. Graceful degradation when Status is disabled

### Phase 5: Post-Violation Feedback Loop

**Goal**: Moral choices have lasting consequences.

1. Personality drift on `obligation.violation.reported` events (`OATH_BROKEN`, `RESISTED_TEMPTATION`)
2. Emotional composite modification (guilt pattern: stress up, sadness up, comfort down, joy down)
3. Encounter memory for witnessed violations (negative sentiment)
4. Contract breach reporting for knowing contractual violations
5. Player character "second thoughts" (co-pilot moral resistance via progressive agency)

### Phase 6: Data-Driven Trait Mapping

**Goal**: Violation-to-trait mapping is extensible.

1. Move personality trait mapping from hardcoded switch to action mapping store or configuration
2. Allow faction norms to specify their own trait associations
3. Default fallback to conscientiousness preserved for unknown types

---

*This document captures the morality system architecture as of its writing date. For implementation details, see the individual deep dives: [Faction](../plugins/FACTION.md), [Obligation](../plugins/OBLIGATION.md), [Status](../plugins/STATUS.md). For the original design specification, see [GitHub Issue #410](https://github.com/beyond-immersion/bannou-service/issues/410).*
