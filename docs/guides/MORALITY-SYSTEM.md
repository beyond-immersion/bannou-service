# Morality System - Conscience, Norms, and Second Thoughts

> **Version**: 1.0
> **Key Plugins**: `lib-faction` (L4), `lib-obligation` (L4)
> **Deep Dives**: [Faction](../plugins/FACTION.md), [Obligation](../plugins/OBLIGATION.md)
> **Related Guides**: [Behavior System](./BEHAVIOR-SYSTEM.md), [Seed System](./SEED-SYSTEM.md), [Story System](./STORY-SYSTEM.md)
> **Origin**: [GitHub Issue #410 - Second Thoughts: Prospective Consequence Evaluation for NPC Cognition](https://github.com/beyond-immersion/bannou-service/issues/410)

The Morality System gives NPCs a conscience. An honest merchant hesitates before swindling a customer. A loyal knight resists betraying their lord even when tactically optimal. A character in lawless territory acts with less restraint than one standing in a temple district. This is not scripted morality -- it is emergent moral reasoning arising from the intersection of social context, personal character, and contractual commitments, expressed as GOAP action cost modifications that change what NPCs choose to do.

---

## Table of Contents

1. [Core Insight](#1-core-insight)
2. [Architecture: Two Services, One Pipeline](#2-architecture-two-services-one-pipeline)
3. [Faction: The Social Landscape](#3-faction-the-social-landscape)
4. [Obligation: The Cost Landscape](#4-obligation-the-cost-landscape)
5. [Actor Cognition: Where Costs Become Choices](#5-actor-cognition-where-costs-become-choices)
6. [The Feedback Loops](#6-the-feedback-loops)
7. [Player Experience: The Character Co-Pilot](#7-player-experience-the-character-co-pilot)
8. [Variable Providers: ABML Access](#8-variable-providers-abml-access)
9. [Decision Guide](#9-decision-guide)

---

## 1. Core Insight

**Moral reasoning is a pipeline that transforms social context into GOAP action costs.** The GOAP planner doesn't know about honor, loyalty, or guilt. It sees numbers. "Theft costs 15.7 instead of 5.0" is enough to make it pick a different action. The moral reasoning is emergent from the cost modifiers, not scripted into behavior trees.

This pipeline has two stages, each owned by a different service:

```
What norms apply here?          What would it cost to
(social structure, territory,    violate them?
 cultural baseline)             (penalty aggregation,
                                 personality weighting)
        │                               │
        ▼                               ▼
   ┌──────────┐                  ┌────────────┐
   │ Faction  │──norms──────────▶│ Obligation │──costs──────▶  Actor
   │          │                  │            │               Cognition
   └──────────┘                  └────────────┘               Pipeline
                                       ▲                         │
                                       │                         │
                                  ┌────────────┐                 ▼
                                  │ Contract   │──clauses   Modified GOAP
                                  └────────────┘            action costs
                                       ▲                         │
                                       │                         ▼
                                  ┌────────────┐            Different NPC
                                  │Personality │──weights   behavior
                                  └────────────┘
```

The planner sees "theft costs 15.7" because the character belongs to the Merchant Guild (norm: theft penalty 15), has a contract clause prohibiting dishonesty (contract: deception penalty 10), and has high honesty personality traits (weight multiplier 1.375). None of those individual systems know about each other's contributions. The pipeline composes them.

### Why Two Services Instead of One?

Each service has independent value:

- **Faction without Obligation**: Organizations, territory control, membership management, seed-based governance progression -- useful even without NPC conscience
- **Obligation without Faction**: Contractual obligations from guild charters, trade agreements, quest oaths -- works with contracts alone

Together they form the morality system. Apart they're each useful L4 game features. This is the monoservice philosophy: optional services that compose into something greater when all are enabled.

---

## 2. Architecture: Two Services, One Pipeline

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            NPC Cognition Pipeline                            │
│                                                                              │
│  Perception → Appraisal → Memory → [Consequences] → Goals → Actions         │
│                                          ▲                                   │
│                                          │                                   │
│                              evaluate_consequences                           │
│                              (cognition stage)                               │
│                                          │                                   │
│                          ┌───────────────┴───────────────┐                   │
│                          ▼                               ▼                   │
│                    ┌──────────┐                    ┌────────────┐            │
│                    │ Faction  │                    │ Obligation │            │
│                    │ (L4)     │                    │ (L4)       │            │
│                    │          │                    │            │            │
│                    │ "What    │                    │ "What does │            │
│                    │  norms   │────norms──────────▶│  it cost   │            │
│                    │  apply?" │                    │  to break  │            │
│                    │          │                    │  them?"    │            │
│                    └────┬─────┘                    └─────┬──────┘            │
│                         │                               │                    │
│                    ${faction.*}                   ${obligations.*}           │
│                         │                               │                    │
│                         ▼                               ▼                    │
│                    ┌─────────────────────────────────────────┐               │
│                    │     Actor Variable Provider Factory     │               │
│                    │     (L2 discovers L4 providers via DI)  │               │
│                    └─────────────────────────────────────────┘               │
└─────────────────────────────────────────────────────────────────────────────┘
```

| Service | Layer | Role | Key Dependencies |
|---------|-------|------|-----------------|
| **Faction** | L4 GameFeatures | Social structure, norms, territory | Seed (L2), Location (L2), Realm (L2), Resource (L1) |
| **Obligation** | L4 GameFeatures | Cost computation, personality weighting | Contract (L1), Faction (L4 soft), Personality (L4 soft) |

Both follow the Variable Provider Factory pattern from SERVICE-HIERARCHY.md: they implement `IVariableProviderFactory` (defined in `bannou-service/Providers/`), register as singletons, and are discovered by Actor (L2) via `IEnumerable<IVariableProviderFactory>` DI injection. Actor never depends on them -- it discovers whatever providers are registered at runtime and gracefully degrades when they're absent.

---

## 3. Faction: The Social Landscape

Factions answer the question: **"What are the social rules where I am and who I'm with?"**

### Factions as Seed-Based Living Entities

Each faction owns a seed (via lib-seed) that grows through member activities. Faction capabilities are emergent from growth, not assigned by a designer:

| Phase | Min Growth | Unlocked Capabilities |
|-------|-----------|----------------------|
| Nascent | 0 | None -- faction exists but has no governance power |
| Established | 50 | `norm.define` -- can define enforceable norms |
| Influential | 200 | `norm.enforce.sanctions`, `territory.claim` |
| Dominant | 1000 | Complex governance, trade regulation |
| Sovereign | 5000 | Full governance capabilities |

A nascent thieves' guild literally cannot define "honor among thieves" as an enforceable norm. It hasn't grown enough governance capability yet. A sovereign kingdom faction can define comprehensive legal codes and enforce territorial boundaries. This mirrors real-world governance: authority comes from established legitimacy, not from declaration.

### How Factions Grow

Member activities feed faction growth through the Collection-to-Seed pipeline:

```
Member completes trade ──▶ Collection entry unlocked
                           ("faction-deeds", tag: "commerce:trade")
                                    │
                           ┌────────┴────────┐
                           ▼                 ▼
                    Member's personal   ICollectionUnlockListener
                    seed growth         (lib-faction)
                    (existing pipeline)      │
                                             ▼
                                    lib-seed: RecordGrowth
                                    (factionSeedId, "commerce", 1.5)
                                             │
                                             ▼
                                    ISeedEvolutionListener:
                                    Phase changed → new capabilities
                                    (e.g., nascent → established)
                                             │
                                             ▼
                                    Faction can now define norms
```

This means factions grow organically from what their members actually do. A faction whose members trade a lot develops commerce governance capabilities. One whose members fight develops military capabilities. The capabilities reflect the faction's lived history.

### Three Kinds of Factions

Factions serve as the governance and cultural layer that bare locations and realms don't provide:

| Faction Type | Scope | Example | What It Provides |
|-------------|-------|---------|-----------------|
| **Realm baseline** | Realm-wide cultural norms | Arcadian Cultural Council | Honor codes, realm-wide taboos, baseline social expectations |
| **Location controlling** | Local territorial norms | Harbor Authority | Local laws, trade regulations, territorial restrictions |
| **Guild** | Organizational norms (direct membership) | Merchant Guild, Thieves' Guild | Professional codes, membership obligations, organizational discipline |

This is how "is this territory hostile?" gets answered. Not through a boolean on the Location entity, but through which faction controls the location and what norms that faction enforces. A "lawless district" is a location where no faction has territorial control -- there are no local norms, only realm baseline.

### Norm Resolution Hierarchy

When multiple factions' norms could apply, the most specific source wins:

```
Character "Kael" at Location "Docks District"

1. Guild Factions (direct memberships):          ← HIGHEST PRIORITY
   ┌──────────────────────────────────┐
   │ Merchant Guild (member)          │ → theft: 15, deception: 10
   │ Dockworkers Union (recruit)      │ → violence: 5
   └──────────────────────────────────┘

2. Location Controlling Faction:                 ← MEDIUM PRIORITY
   ┌──────────────────────────────────┐
   │ Harbor Authority                 │ → contraband: 12, trespass: 8
   │ (controls Docks District)        │ → theft: 10 (overridden by guild)
   └──────────────────────────────────┘

3. Realm Baseline Faction:                       ← LOWEST PRIORITY
   ┌──────────────────────────────────┐
   │ Arcadian Cultural Council        │ → disrespect: 5, violence: 3
   │ (realm baseline)                 │   (overridden by guild/territory)
   └──────────────────────────────────┘

Merged Norm Map (most specific source wins per violation type):
┌─────────────────────────────────────────────────────┐
│ theft:      15  (Merchant Guild - personal membership) │
│ deception:  10  (Merchant Guild - personal membership) │
│ violence:    5  (Dockworkers Union - membership)       │
│ contraband: 12  (Harbor Authority - territorial)       │
│ trespass:    8  (Harbor Authority - territorial)       │
│ disrespect:  5  (Cultural Council - realm baseline)    │
└─────────────────────────────────────────────────────┘
```

The same character in a different location would get a different merged norm map. Move from the Docks to the Temple District and suddenly "disrespect" carries a penalty of 25 instead of 5 because the Temple Guardians control that territory. Leave the city entirely into unclaimed wilderness and territorial norms vanish -- only guild membership and realm baseline apply.

---

## 4. Obligation: The Cost Landscape

Obligation answers the question: **"What would it cost this specific character to do something wrong?"**

### Two-Layer Design

Obligation is deliberately designed with two independent layers. The base layer works alone; the enrichment layer adds depth when personality data is available:

| Layer | Input | What It Contributes | Without It |
|-------|-------|--------------------|-----------|
| **Base** | Contract behavioral clauses + Faction norms | Raw penalty values per violation type | No obligation costs at all |
| **Enrichment** | Personality traits (honesty, loyalty, etc.) | Trait-weighted multipliers on base penalties | Costs exist but are personality-blind |

### Input Sources

Obligation aggregates penalties from two sources into a unified cost map:

**A. Contractual Obligations**: Active contract instances with behavioral clauses. These are formal, explicit commitments -- a guild charter's code of conduct, a trade agreement's exclusivity terms, a quest oath's honor requirements. Obligation queries lib-contract for all active contracts belonging to the character and extracts behavioral clause penalties.

**B. Social/Cultural Norms**: Faction norms queried via `/faction/norm/query-applicable`. These are ambient, contextual expectations -- the realm's honor code, the local laws of whatever territory the character is in, the professional standards of their guild memberships. Obligation queries lib-faction with the character's current location context and merges norm penalties alongside contract penalties.

Both sources produce the same output format: a map of `{ violationType → basePenalty }`. Obligation doesn't care where a penalty came from -- a theft penalty of 15 from a contract clause and a theft penalty of 10 from a faction norm are aggregated together.

### Personality Weighting

When personality data is available (soft L4 dependency on lib-character-personality), obligation enriches raw penalties with trait-based multipliers:

```
weightedPenalty = basePenalty × (1.0 + avgTraitValue × 0.5)
```

Where `avgTraitValue` is the average of the relevant personality traits for that violation type:

| Violation Type | Relevant Traits | Effect of High Trait | Effect of Low Trait |
|---------------|----------------|---------------------|---------------------|
| `theft` | Honesty + Conscientiousness | Theft costs 1.5x more | Theft costs 0.75x |
| `deception` | Honesty | Lying costs 1.5x more | Lying costs 0.75x |
| `violence` | Agreeableness | Violence costs 1.5x more | Violence costs 0.75x |
| `betrayal` | Loyalty | Betrayal costs 1.5x more | Betrayal costs 0.75x |
| `honor_combat` | Conscientiousness + Loyalty | Dishonorable combat costs more | Dishonorable combat costs less |
| *unknown type* | Conscientiousness (default) | Falls back to general conscientiousness | Falls back to general conscientiousness |

**Example**: An NPC with high honesty (0.8) and high conscientiousness (0.7) computes theft:
`basePenalty × (1.0 + 0.75 × 0.5) = basePenalty × 1.375`

An NPC with low honesty (-0.6) computes theft:
`basePenalty × (1.0 + -0.6 × 0.5) = basePenalty × 0.7`

The same norm, experienced differently based on who the character is. The Merchant Guild's "no stealing" norm weighs heavily on an honest merchant and lightly on a dishonest one. The dishonest merchant might still choose not to steal -- the cost is still positive -- but the threshold is lower. Under enough pressure, personality prevails.

### Violation Type Taxonomy

Violation types are **opaque strings**, not enums. This follows the Collection/Seed pattern of not constraining the vocabulary. The core vocabulary is:

`theft`, `deception`, `violence`, `disrespect`, `exploitation`, `oath_breaking`, `trespass`, `contraband`, `honor_combat`, `betrayal`

Factions can define custom types (`guild_secret_disclosure`, `blasphemy`, `price_manipulation`). The vocabulary grows organically as new faction norms and contract templates are authored. GOAP action tags map to violation types -- by default through 1:1 naming, with explicit mappings for cases where vocabularies differ.

---

## 5. Actor Cognition: Where Costs Become Choices

The morality system's output -- weighted violation costs per action type -- means nothing until it reaches the Actor's cognition pipeline and modifies GOAP planning. This is where numbers become behavior.

### The Cognition Pipeline

The Actor's cognition pipeline processes perceptions and updates internal state. The `evaluate_consequences` stage sits between memory storage and goal evaluation:

```
1. filter_perception      What events did the NPC notice?
2. appraise_relevance     How relevant is each event?
3. store_memory           Record significant events
4. evaluate_consequences  What are the moral costs of planned actions?  ← MORALITY
5. evaluate_goal_impact   How do events affect current goals?
6. trigger_goap_replan    Should the GOAP planner re-evaluate?
```

The `evaluate_consequences` stage:

1. **Reads** `${obligations.violation_cost.<type>}` for each action tag in the current GOAP plan
2. **Flags** actions where cost exceeds a threshold as `knowing_violation` (the NPC is aware this is wrong)
3. **Forces replan** if costs changed significantly since the plan was created (new norms, new contract, moved to different territory)
4. **Writes** cost modifiers into the execution context for the GOAP planner to consume

### GOAP Cost Modification

The GOAP planner's A* search expansion uses action costs to choose between alternatives. The morality system adds a dynamic cost modifier:

```
Without morality:
  steal_food:    cost = 5.0   ← cheapest, planner picks this
  buy_food:      cost = 8.0
  beg_for_food:  cost = 12.0

With morality (honest merchant, Merchant Guild member):
  steal_food:    cost = 5.0 + 20.6  = 25.6   ← personality-weighted theft penalty
  buy_food:      cost = 8.0 + 0.0   = 8.0    ← cheapest now, planner picks this
  beg_for_food:  cost = 12.0 + 0.0  = 12.0

With morality (dishonest rogue, no guild):
  steal_food:    cost = 5.0 + 4.9   = 9.9    ← still cheapest
  buy_food:      cost = 8.0 + 0.0   = 8.0    ← but only marginally more expensive
  beg_for_food:  cost = 12.0 + 0.0  = 12.0

With morality (dishonest rogue, in Temple District):
  steal_food:    cost = 5.0 + 17.5  = 22.5   ← temple norms make theft very costly
  buy_food:      cost = 8.0 + 0.0   = 8.0    ← planner switches to buying
  beg_for_food:  cost = 12.0 + 0.0  = 12.0
```

The rogue steals freely in the slums but buys in the temple district. Not because they suddenly became honest, but because the cost landscape changed. The same character, the same personality, different social context, different behavior. This is emergent morality.

### Opt-In via ABML Metadata

Not all actors need moral reasoning. A mindless dungeon creature doesn't hesitate before attacking. The `conscience` metadata flag controls whether `evaluate_consequences` runs:

```yaml
# NPC with moral reasoning
metadata:
  conscience: true    # Enable evaluate_consequences stage

# Mindless creature (default)
metadata:
  conscience: false   # Skip -- zero runtime cost
```

When `conscience` is absent or false, the stage is eliminated at compile time. There is no runtime cost for actors that don't use moral reasoning.

### Urgency Bypass

Even NPCs with a conscience abandon moral reasoning under extreme pressure. When a perception's urgency exceeds a threshold (e.g., imminent death), the `evaluate_consequences` stage short-circuits. The survival instinct overrides moral deliberation -- a character might steal food to survive even if they would never steal normally. This produces realistic behavior: moral reasoning is a luxury of safety.

---

## 6. The Feedback Loops

The morality system doesn't end when an NPC makes a choice. Actions have consequences that feed back into the system, creating reinforcing cycles that shape characters over time.

### Post-Action Consequences

```
NPC commits knowing violation (flagged by evaluate_consequences)
        │
        ├──▶ Personality drift
        │    OATH_BROKEN experience → honesty decreases slightly
        │    Repeated violations compound → character becomes "harder"
        │    (or RESISTED_TEMPTATION → honesty reinforced)
        │
        ├──▶ Emotional composite
        │    Guilt pattern: stress ↑, sadness ↑, comfort ↓, joy ↓
        │    (guilt is composite, not a separate emotion dimension)
        │    Modulates future behavior through existing emotional model
        │
        ├──▶ Encounter memory
        │    If witnessed: negative sentiment encounter recorded
        │    Witness remembers the violation → affects future interactions
        │    Social consequence propagates through encounter system
        │
        ├──▶ Contract breach (if contractual obligation)
        │    Breach reported to lib-contract
        │    Contract enforcement terms apply (penalties, termination)
        │    Escalation through formal governance structures
        │
        └──▶ Divine attention
             God watchers may notice patterns
             Consistent violations → loss of divine favor
             Consistent virtue → divine blessing
             Creates a cosmological moral feedback loop
```

### Character Arc Through Moral Pressure

Over time, the feedback loops produce character arcs:

**The Corrupted Guard**: Starts with high loyalty and conscientiousness. Takes one bribe under financial pressure (knowing violation). Personality drifts slightly. The next bribe costs a little less. Over months of simulated time, the guard's honesty erodes. What was once unthinkable becomes habitual. The GOAP planner reflects this -- early bribes are agonized over (high cost, forced replan), later ones are automatic (low cost, no hesitation).

**The Redeemed Thief**: Starts with low honesty. Joins a guild with strong honor norms (theft penalty now applies through faction membership). Early on, personality weighting keeps the cost low. But each time the thief resists temptation (RESISTED_TEMPTATION), honesty ticks up. Guild membership reinforces lawful behavior. Gradually, stealing becomes genuinely distasteful to the character -- not because of external penalties, but because the character changed.

These arcs are not scripted. They emerge from the repeated interaction between social context, personal traits, and accumulated moral choices. The system doesn't know it's generating character development -- it's just adjusting costs and traits incrementally. The narrative coherence is emergent.

---

## 7. Player Experience: The Character Co-Pilot

From PLAYER-VISION.md: "Characters can resist or resent being pushed against their nature." The morality system is where this becomes tangible for players.

### Progressive Moral Agency

Player characters have NPC brains running at all times. The guardian spirit (player) influences but doesn't directly control. The morality system creates friction between what the player wants and what the character is willing to do:

| Spirit Agency Level | Moral Experience |
|--------------------|-----------------|
| **Early** (first generation) | Character acts autonomously on moral decisions. Player observes a character who has principles and watches those principles in action. |
| **Developing** (growing combat/social agency) | Player can nudge the character toward morally questionable actions, but the character resists. The resistance is proportional to how much the action violates the character's moral landscape. |
| **Mature** (deep agency in relevant domain) | Player can override moral resistance, but the character still experiences consequences -- guilt composite, personality drift, social fallout. The character may become resentful. |

This is progressive agency applied to moral choice. The player doesn't get a "be evil" toggle. They gradually learn that pushing against a character's morality has real costs -- the character changes, relationships deteriorate, divine favor shifts. A player who consistently forces their character to steal doesn't just get stolen goods -- they get a character whose personality has shifted, whose reputation is damaged, and whose divine relationships are strained.

### The Second Thoughts Moment

The visible manifestation of the morality system for players is the "second thought" -- a moment where the character pauses before committing to an action with high moral cost. This could be:

- A brief hesitation animation
- A thought bubble or inner monologue
- The character executing the action slightly differently (reluctantly, aggressively, shamefully)
- A co-pilot suggestion ("Your character is uncomfortable with this")

The intensity scales with the moral cost. A minor transgression gets a flicker. A major violation of deeply held principles gets a full pause with visible emotional reaction. This gives players a window into their character's moral life without breaking immersion.

---

## 8. Variable Providers: ABML Access

Each morality service contributes a variable namespace to the Actor's ABML behavior execution via `IVariableProviderFactory`. These variables let behavior authors write morality-aware ABML without hardcoding service interactions.

### `${faction.*}` -- Social Context

| Variable | Type | Description |
|----------|------|-------------|
| `${faction.count}` | int | Number of factions the character belongs to |
| `${faction.CODE.name}` | string | Display name for a specific faction |
| `${faction.CODE.phase}` | string | Current seed growth phase |
| `${faction.CODE.role}` | string | Character's role in this faction |
| `${faction.primary_faction}` | string | Code of highest-role faction |
| `${faction.has_norm.<type>}` | bool | Whether any applicable norm covers this violation type |
| `${faction.norm_penalty.<type>}` | float | Base penalty for a violation type from merged norms |
| `${faction.in_controlled_territory}` | bool | Whether character is in territory controlled by any of their factions |

### `${obligations.*}` -- Cost Landscape

| Variable | Type | Description |
|----------|------|-------------|
| `${obligations.active_count}` | int | Number of active obligations |
| `${obligations.has_obligations}` | bool | Whether any obligations exist |
| `${obligations.contract_count}` | int | Active contracts with behavioral clauses |
| `${obligations.violation_cost.<type>}` | float | Aggregated weighted cost for a specific violation type |
| `${obligations.highest_penalty_type}` | string | Violation type with the highest aggregated penalty |
| `${obligations.total_obligation_cost}` | float | Sum of all violation costs |

### ABML Usage Example

```yaml
flows:
  evaluate_theft_opportunity:
    - cond:
        # In hostile/lawless territory with no faction presence? Steal freely.
        - when: "${!faction.in_controlled_territory && !obligations.has_obligations}"
          then:
            - call: steal_item

        # High moral cost? Only steal if desperate.
        - when: "${obligations.violation_cost.theft > 15.0}"
          then:
            - cond:
                - when: "${hunger > 0.9}"
                  then:
                    - call: steal_item  # Survival overrides morality
                - otherwise:
                    - call: consider_alternatives

        # Moderate cost? Personality decides.
        - when: "${obligations.violation_cost.theft > 5.0}"
          then:
            - cond:
                - when: "${personality.honesty < -0.3}"
                  then:
                    - call: steal_item  # Dishonest enough to not care
                - otherwise:
                    - call: buy_item    # Too honest to steal casually

        # Low or no cost? Go ahead.
        - otherwise:
            - call: steal_item
```

### Related Namespaces

Other variable providers contribute data relevant to moral reasoning:

| Namespace | Provider | Morality Relevance |
|-----------|----------|-------------------|
| `${personality.*}` | lib-character-personality | Trait values used for personality weighting in obligation |
| `${combat.*}` | lib-character-personality | Combat preferences affect violence-related moral reasoning |
| `${encounters.*}` | lib-character-encounter | Past interactions inform grudges, debts, trust |
| `${quest.*}` | lib-quest | Active quest obligations feed into contractual costs |
| `${location.*}` | lib-location | Character position for territory-aware norm resolution |
| `${seed.*}` | lib-seed | Growth state for faction capability gating |

---

## 9. Decision Guide

### When to Use Which Service

```
Do you need to model social organization?
│
├─ Yes → lib-faction
│  │
│  ├─ Territory control? → Faction with location claims + controlling faction
│  ├─ Cultural norms? → Faction norms (realm baseline or guild)
│  ├─ Membership/ranks? → Faction membership management
│  └─ Emergent governance? → Seed-based faction growth
│
├─ Do you need NPCs to weigh consequences?
│  │
│  └─ Yes → lib-obligation
│     │
│     ├─ From formal agreements? → Contract behavioral clauses → obligation
│     ├─ From social context? → Faction norms → obligation
│     └─ Personality-dependent? → Obligation personality enrichment layer
│
└─ Do you need NPCs to act on moral reasoning?
   │
   └─ Yes → evaluate_consequences cognition stage
      │
      ├─ Which actors? → ABML conscience: true metadata
      ├─ At what cost? → Zero cost when disabled (compile-time elimination)
      └─ Under pressure? → Urgency bypass for survival situations
```

### Integration Patterns

| Pattern | When to Use | Example |
|---------|------------|---------|
| **Faction norm → Obligation cost** | NPC should consider social context in decisions | "Don't steal in the marketplace" |
| **Contract clause → Obligation cost** | NPC has explicit commitments | "Guild charter prohibits deception" |
| **Obligation cost → GOAP modifier** | NPC should choose different actions based on moral landscape | "Buy food instead of stealing it" |
| **Violation → Personality drift** | Long-term character development from moral choices | "Repeated theft erodes honesty" |
| **Violation → Divine attention** | Cosmological consequences for moral patterns | "God of justice revokes blessing" |

### What the Morality System Is NOT

- **Not a karma score.** There is no single "good/evil" axis. Moral costs are multidimensional and context-dependent.
- **Not a punishment system.** High costs don't prevent actions -- they make NPCs prefer alternatives. Under enough pressure, any NPC will violate any norm.
- **Not scripted morality.** No behavior tree says "if evil, steal." Moral reasoning emerges from the intersection of norms, personality, contracts, and context.
- **Not a player judgment system.** The system doesn't label player choices as good or bad. It models how characters experience moral friction.

---

*This guide covers the morality system as a coherent pipeline. For implementation details, see the individual deep dives: [Faction](../plugins/FACTION.md) and [Obligation](../plugins/OBLIGATION.md). For the actor cognition pipeline, see [Behavior System](./BEHAVIOR-SYSTEM.md). For seed-based growth mechanics, see [Seed System](./SEED-SYSTEM.md). For the original design specification, see [GitHub Issue #410](https://github.com/beyond-immersion/bannou-service/issues/410).*
