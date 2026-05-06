# Leyline Composition Framework: Realms as Influence Vectors, Dungeons as Intersection Pressure

> **Type**: Design
> **Status**: Aspirational
> **Created**: 2026-04-28
> **Last Updated**: 2026-04-28
> **North Stars**: #1, #2, #5
> **Related Plugins**: Realm, Environment, Mapping, Location, Worldstate, Seed, Actor, Character, Divine, Resource, Storyline, Workshop

## Summary

Defines a unifying framework where leylines are not generic "magic rivers" but conduits carrying influence FROM specific source realms; where realms (player-facing and concept-space) are parameterized by their leyline composition rather than authored aesthetics; and where dungeons manifest at multi-vector leyline intersections when pressure exceeds local containment thresholds. Subsumes existing Sanctuary, Spirit Den, Logos Resonance Item, and Underworld designs as special cases of leyline-composition dynamics. Composes from existing primitives (Realm, Environment, Mapping, Location, Resource, Workshop) with no new plugins required. Adds `leyline_composition` metadata to Realm and Location records and a Mapping-fidelity-gated visualization layer over the existing affordance map system. Concept-space realms (deity domains, Underworld, Demon Realms, Void/Seal, named cosmological forces) become first-class design objects whose influence is felt without requiring runtime servers.

---

## Problem Statement

Multiple existing Arcadia design documents reference leylines, leyline convergences, leyline networks, or pneuma flows without specifying the upstream architecture:

- **SANCTUARIES-AND-SPIRIT-DENS.md** describes spirit dens as forming at "leyline convergence points" with "ambient mana density," but never specifies *what the leyline carries from where*. The Environment service tracks `leyline_proximity` as a scalar but the upstream realms feeding the leyline are unspecified.
- **LOGOS-RESONANCE-ITEMS.md** has items crystallizing from "ambient logos and dispersing pneuma" with affixes drawn from "fight circumstances + memento accumulation" — but the cosmological signature that determines *which logos* are ambient at a given location is unspecified.
- **arcadia-kb Underworld and Soul Currency System** establishes that "the underworld is not a separate dimension - it is the **inside of the leyline network**" with leylines having "dual nature" (pneuma surface, logos river depths). It explicitly distinguishes the underworld from "Demon Realms," "the Void/Seal," and "Divine Barriers" — but leaves "How do different underworld regions reflect the characteristics of different realms?" as an open question.
- **DUNGEON-MANA-ABSORPTION.md** has dungeons absorbing pneuma without specifying the ecology of supply (where does the pneuma come from?) and demand (why do dungeons form here and not elsewhere?).
- The multi-realm architecture (Omega/Arcadia/Fantasia, plus implicitly the Demon Realms, Void/Seal, deity domains, and any future concept-space realms) is described by aesthetic differentiation. Cyberpunk Omega, fantasy Arcadia, post-apocalyptic Fantasia — but no structural mechanism explains why each realm has its specific character beyond authorial choice.

The framework presented here resolves all of these open questions with a single unifying claim:

> **Leylines are inter-realm influence vectors. Each leyline carries pneuma and logos from a specific source realm. A given realm's character is determined by which source-realm leylines flow through it, in what proportions, following what spatial algorithm. Dungeons manifest where multiple source-realm leylines converge under sufficient pressure to bleed through local containment.**

This is not a new system. It is the explicit naming of an architecture already implicit across multiple locked design documents.

---

## Core Concept

### Leylines as Source-Realm Vectors

A leyline is not a generic conduit of "mana" or "pneuma" — it is a directional vector carrying *the specific spiritual output of a specific source realm* through whatever intermediate space it traverses. Every leyline has:

- **A source realm** (the upstream — where the pneuma/logos originates)
- **A pneuma profile** (volatility, density gradient, decay function over distance)
- **A logos pattern bias** (the kinds of concept-information that crystallize from this leyline's flow)
- **A trajectory** (the spatial path through realms)
- **A pressure rating** (how much energy the leyline is carrying at a given point along its trajectory)

A region of any realm receives the *integral of all leylines passing through it*. A forest in Arcadia might be receiving 30% Pneuma-Pure leyline flow + 25% Spirit-Origin leyline flow + 20% Pantheon (nature-domain god) leyline flow + 15% Underworld leyline flow + 10% Demon-Realm bleed-through. Its character emerges from this composition. Move 50 km north and the composition shifts; the forest character shifts with it.

### Realms as Leyline-Composition Profiles

Every realm — including ones with no playable territory (concept-space realms) — has a `leyline_composition` profile that characterizes:

- Which source realms feed into it (and at what aggregate weights across its territory)
- The spatial algorithm that distributes those weights (uniform, gradient from a node, fractal noise, deterministic from realm geography, stochastic with a seed)
- The interaction modifiers between source realms within this realm (which combinations reinforce, which interfere, which generate emergent third-order effects)
- The temporal dynamics (how composition shifts across saecula and in response to events)

**This makes realms parameterizable rather than authored.** Cyberpunk Omega is not "described as cyberpunk by a designer" — its character emerges because its leyline composition is dominated by Logos-Pure (precision, information, structural clarity) and a constrained Pantheon subset (limited deity domains), with explicit interference rules suppressing high-volatility Pneuma flow (which is why electronics work in Omega and not in Arcadia).

### Dungeons as Multi-Vector Intersection Pressure

A dungeon is not a designer-placed level — it is what manifests when **two or more source-realm leylines converge at sufficient density to exceed the local realm's pneuma/logos containment threshold**. The convergence creates a bleed-through region where the foreign realm influences begin physically reshaping local space, attracting accumulated mementos, spawning pneuma echoes (monsters), and becoming a Dungeon Core's domain.

The dungeon's *character* — its monster catalogue, its loot signature, its memory manifestations, its environmental hazards — is determined by *which* source-realm leylines are crossing there and in what proportions. A Demon-Realm × War-God-Realm × concept-realm-of-betrayal intersection produces a dungeon with a specific signature. A Spirit-Origin × Pneuma-Pure × concept-realm-of-knowledge intersection produces a different one. The signature is legible to characters with sufficient Mapping fidelity.

---

## How This Unifies Existing Designs

### Sanctuaries Are Pantheon-Leyline-Spike Locations

A sanctuary (per SANCTUARIES-AND-SPIRIT-DENS.md) is a region where a god's miracle has imprinted "standing resonance" on the local pneuma field. In the leyline composition framework: a sanctuary is a region where divine action has **deliberately spiked the originating deity's domain-leyline weight** in the local composition, suppressing default ambient flows.

| Existing concept | Reframed as leyline composition |
|------------------|-------------------------------|
| Divine imprint | Deity-domain-realm leyline weight spike at this location |
| Sanctuary decay | Spike thinning back into ambient default composition |
| Shrine keeper consecration | Re-weighting composition toward the deity-domain leyline (memento → leyline-weight transmutation via Workshop) |
| Pilgrimage maintenance | Slow continuous re-weighting via accumulated faith mementos |
| Failed sanctuary collapsed | Composition has fully equilibrated; the deity's leyline weight has returned to ambient |
| The "convergence case" (sanctuary on a leyline node) | A region where deity-domain leyline weight is spiked AND multiple other source-realm leylines also flow strongly — exceptional in both ways |

The sanctuary's `sanctuary_suppression` field becomes a derived metric from leyline composition: when deity-domain leyline weight exceeds a threshold, aggression-suppression effects emerge.

### Spirit Dens Are Species-Logos × Pneuma-Pure Intersections

A spirit den (per SANCTUARIES-AND-SPIRIT-DENS.md) is where leyline convergence + species-memento accumulation + undisturbed time crystallizes a supernatural species exemplar. In the leyline composition framework: a spirit den forms where **Pneuma-Pure leyline flow** intersects with **species-specific logos density** (accumulated through memento curation) and the local composition meets specific stability conditions.

The "ambient leyline energy" in the existing design becomes specifically the Pneuma-Pure leyline contribution. The "memento accumulation" becomes the local Logos-Current contribution from accumulated species deaths (logos that haven't yet flowed downstream into the Underworld leyline). When both reach threshold, the species-specific manifestation crystallizes.

This explains the existing design's open question: spirit dens form at locations with high `leyline_proximity` because that proximity specifically means high Pneuma-Pure leyline weight. Different species' spirit dens preferentially form in regions whose ambient leyline composition resonates with that species' logos pattern.

### Logos Resonance Items Carry the Local Leyline Signature

A logos resonance item (per LOGOS-RESONANCE-ITEMS.md) is created at a formative event, with affixes derived from "fight circumstances + memento accumulation." In the leyline composition framework: the item's logos signature is *the leyline composition at the moment and location of crystallization*, plus the predecessor mementos accumulated under that composition.

The activation prerequisites — "lightning_mastery," "fought_storm_wyrm," "courage > 0.6" — become **source-realm-affinity prerequisites**. "Lightning mastery" maps to familiarity with Storm-Concept-Realm leylines. "Storm Wyrm slayer" maps to having operated in regions where Storm-Concept-Realm bleed manifested entities. "Courage > 0.6" maps to a personality affinity for high-pressure leyline regions.

Two visually identical resonance swords forged at different intersections have different cosmological provenance. A Storm-Concept × War-God intersection produces a different signature than Storm-Concept × Underworld. The difference is legible in the item's `customStats` provenance fields and in the activation prerequisites the affixes encode.

### The Underworld IS the Logos-Current Side

The Underworld doc establishes that leylines have dual nature (pneuma surface, logos river depths) and that the underworld is "inside the leyline network." In the leyline composition framework: **the Underworld is itself a source realm**. Its leyline output is the logos-current that flows back into the network when characters die and their bundles dissipate. What we call "the leyline network's logos current" is the Underworld's leyline output flowing through every region in proportion to local death-memento accumulation.

This resolves the existing open question: "How do different underworld regions reflect the characteristics of different realms?" Different underworld regions reflect *which other source-realm leylines flow into them*. An underworld region downstream of a region heavily fed by Pantheon (war-god) leylines develops a different character (Valhalla-shaped) than one downstream of regions fed by Pneuma-Pure + concept-realm-of-grief leylines (Purgatory-shaped). The aspiration-based pathways in the existing design are emergent from leyline composition, not authored.

### The Already-Implicit Source-Realm Catalogue

The Underworld doc's distinction list — "**Demon Realms (pneuma-rich, spiritual beings/monsters/majin)**, **The Void/Seal (counter-logos containment)**, **Divine Barriers (block pneuma, not logos)**" — is already a partial source-realm catalogue. Each is a different upstream realm with a distinct output profile. The framework just names what was already there.

---

## Source Realm Catalogue (Starter Set)

The catalogue is the load-bearing decision of the framework. Each entry below specifies output type, decay characteristics, and interaction notes. This is a *starter set* for design discussion — final catalogue and parameterization belong in Realm-service-deep-dive companion docs.

### Foundational Source Realms

**Logos-Pure**
- *Output*: Stable logos patterns; minimal pneuma volatility
- *Effect on flow-through regions*: Crisp definitions, structural clarity, magic that operates on True Names with high fidelity, electronics-compatible local fields
- *Decay function*: Slow attenuation with distance (logos is information; it doesn't lose intensity geometrically)
- *Interactions*: Reinforces all directed-magic systems; interferes with high-pneuma realms (pure-pneuma with pure-logos creates uncomfortable tension — neither dominant)
- *Visibility*: Direct presence is rare; mostly known by its effects on adjacent realms

**Pneuma-Pure**
- *Output*: High-volatility pneuma; raw transformation potential; minimal logos pattern bias
- *Effect on flow-through regions*: Dense ambient mana, magic-rich environments, electronics interference, higher rate of monster manifestation, accelerated growth (vegetation, animal vitality)
- *Decay function*: Geometric decay with distance from primary node
- *Interactions*: Reinforces nature-domain effects; combined with Underworld produces ghosts and hauntings; combined with Demon-Realm produces hostile manifestations
- *Visibility*: Strongly visible to Mapping at low fidelity (mana density is a primary perceptible signal)

**Underworld (logos-current)**
- *Output*: Decaying logos with memorial bias; spirits and echoes
- *Effect on flow-through regions*: Memento preservation, ghost manifestation possibility, communion accessibility, deeper spiritual perception layers
- *Decay function*: Localized to memento-density gradients; flows toward high-death-memento regions
- *Interactions*: Reinforces Pneuma-Pure to produce ghosts; combined with Pantheon produces afterlife pathways; combined with Demon-Realm produces undead
- *Visibility*: Visible at medium Mapping fidelity; reading the logos-current requires necromancy or medium archetype skills

**Spirit-Origin (Nexius shard source)**
- *Output*: Pneuma with binding affinity; logos with relational pattern bias
- *Effect on flow-through regions*: Guardian spirit clarity, pair-bond fidelity, household-bond reinforcement, contract integrity
- *Decay function*: Distance-decaying but persistent (relational logos doesn't fully attenuate)
- *Interactions*: Reinforces sanctuary effects; combined with Pantheon produces strong divine connection moments
- *Visibility*: Always present at low intensity in player-realms (the substrate of guardian-spirit play); visible only at high Mapping fidelity except at concentrated nodes

### Pantheon Realms (one per major deity)

Each major deity has their own concept-space realm whose leyline output is "this deity's domain logos at high purity." A war-domain god's realm contributes combat-resonance, tactical-clarity logos, and high-pressure pneuma. A nature-domain god contributes growth-bias pneuma and ecological-stability logos. A commerce-domain god contributes exchange-affinity logos and currency-velocity-reinforcement pneuma.

- *Output*: Domain-specific logos pattern bias; pneuma weighted toward the deity's preferred manifestation modes
- *Decay function*: Strong at deity-blessed nodes; weak elsewhere unless the deity invests divinity to create a leyline node
- *Interactions*: Each deity's realm interacts differently with others (rival deities' leylines may interfere; allied deities reinforce)
- *Visibility*: Visible to characters with appropriate religious/divine perception; visible at higher Mapping fidelity tiers
- *Special property*: Deities can spend divinity to deliberately reshape their leyline output in target regions (sanctuary creation, miracle performance, divine intervention)

**Constraint**: No single Pantheon realm's leyline weight may exceed a configured cap (proposed 60%) at any location without triggering catastrophic-bleed responses. This prevents god-realm-as-trump-card collapse.

### Antagonist Source Realms

**Demon Realms**
- *Output*: Volatile pneuma with hostile-manifestation logos bias; "monstrous" pattern templates
- *Effect on flow-through regions*: Monster spawn rates elevated, environmental corruption visible at high pressure, hostile dungeon manifestation likely
- *Decay function*: Geometric decay; tends to flow along weakened-containment paths
- *Interactions*: Reinforced by absence of Pantheon/Spirit-Origin; combined with Pneuma-Pure produces monster-ecology dungeons; combined with Underworld produces undead-bias dungeons
- *Visibility*: Visible at low Mapping fidelity (the corruption is meant to be perceptible as warning)
- *Special property*: Demon-Realm leylines are explicitly destabilizing; the framework treats them as substrate-degraders, not substitute substrates

**Void/Seal**
- *Output*: Counter-logos (information cancellation); pneuma-suppression
- *Effect on flow-through regions*: Magic-suppression zones, information loss, "dead spots" where memory degrades
- *Decay function*: Localized; expands when reinforced (containment failure)
- *Interactions*: Counters all other source realms; can be used to *contain* Demon-Realm bleed (Sealing rituals); over-application creates information-poor regions
- *Visibility*: Visible by absence — characters notice their magic and perception failing

### Concept-Space Realms (Examples — Catalogue Open)

Concept-space realms are realms-without-servers — defined by their leyline output spec, not by territory players visit. Their influence is felt through the leylines they contribute to other realms. **Each concept-space realm requires concrete output specs at the same standard as the foundational realms above.** Vague "realm of grief" is not acceptable; "grief realm: high-volatility pneuma + memory-fragment logos with weight scaling 0.3-0.7 against local death-memento density, decay-function distance-squared, reinforces Underworld leylines" is.

Starting candidates (each pending detailed specification):

- **Realm of Unfinished Labor** — output: persistence-bias logos; pneuma flowing toward incomplete-craft sites; reinforces Workshop production-vs-decay dynamics
- **Realm of Forgotten Oaths** — output: contract-decay logos; pneuma flowing toward contract-violation sites; can be invoked for retroactive obligation effects
- **Realm of First Dawns** — output: rebirth-pattern logos; pneuma flowing toward generational transitions; reinforces saeculum boundaries
- **Realm of Standing Stones** — output: stability-bias logos; pneuma flowing toward fixed-position landmarks; counterforce to leyline drift
- **Storm-Concept Realm, Flame-Concept Realm, Tide-Concept Realm, Stone-Concept Realm** — output: elemental-bias logos with corresponding pneuma profiles; each reinforces specific magic-domain capabilities
- **Realm of Pure Hunger** — output: drive-amplification logos; pneuma flowing toward predator territories; reinforces Disposition-driven behavior intensity (per Disposition L4 spec)

### Player Realms as Source Realms (Cross-Realm Bleed)

Each player-facing realm (Omega, Arcadia, Fantasia) is *also* a source realm whose leyline output bleeds into the others. Cross-realm bleed is what makes "cross-realm knowledge transfer" structurally grounded — the knowledge transfers because Arcadia leylines flow into Omega leyline composition (at low percentages), carrying logos signatures from Arcadia events.

This explains why Omega has fantasy elements bleeding through despite being primarily Logos-Pure and Industrial-Logic dominant. Why Fantasia shows traces of Arcadia's pantheon. Why characters in one realm can develop intuitions about another despite never visiting.

Cross-realm bleed weights are typically small (1-15%) in normal regions and increase at intentional bridge nodes (per Realm-service deep dive on Realm relationships).

---

## The Leyline Composition Data Model

### Per-Location

```
LeylineCompositionAtLocation:
  contributions: [
    {
      source_realm_id: string         # "logos_pure", "pantheon_war_god_kratos", etc.
      weight: decimal                  # 0.0 - 1.0 (sum across contributions ≤ 1.0)
      gradient: GradientPattern        # how this contribution varies in nearby space
      pressure: decimal                # absolute output intensity (separate from weight)
    }
  ]
  total_pressure: decimal              # sum of all contribution pressures
  bleed_threshold: decimal             # local containment threshold; exceeding this triggers manifestation
  composition_history: [Snapshot]      # time-series for saeculum tracking
```

The composition is computed lazily — when a location is queried (by NPC, player, system), the current composition materializes from the underlying realm-level leyline algorithm + per-location modifiers + active divine-act spikes + saeculum-cycle phase. The Workshop-style lazy evaluation model from SANCTUARIES-AND-SPIRIT-DENS.md applies directly: zero server cost when no one is looking.

### Per-Realm

```
RealmLeylineProfile:
  realm_id: string
  source_realm_weights: { source_realm_id: aggregate_weight }    # canonical mix
  spatial_algorithm: AlgorithmSpec                                # fractal_noise / gradient_from_node / uniform / etc.
  algorithm_seed: int                                             # deterministic generation
  interaction_modifiers: [                                        # how source realms blend HERE
    { realms: [a_id, b_id], modifier: SUPRESS|REINFORCE|INTERFERE, weight: decimal }
  ]
  saeculum_drift_function: DriftSpec                              # how composition shifts over time
```

A realm's composition is therefore a deterministic function of:
- Its canonical source-realm weights
- Its spatial algorithm + seed
- Its interaction modifiers
- The current saeculum cycle phase
- Active divine spike events
- Cumulative cross-realm bleed from neighboring realms

Two realms can share the exact same source-realm catalogue and produce wildly different worlds because of different spatial algorithms and interaction modifiers.

### Per-Source-Realm

```
SourceRealmDefinition:
  realm_id: string
  output_pneuma_profile: PneumaSpec          # volatility, density, transformation potential
  output_logos_pattern: LogosPatternSpec     # what kinds of crystallization this leyline favors
  decay_function: DecayFunction              # output(distance) → intensity
  interaction_defaults: [                    # default cross-realm interaction rules
    { partner_realm: id, modifier_default: REINFORCE|INTERFERE|NEUTRAL }
  ]
  bleed_threshold_default: decimal           # how much pressure triggers manifestation
  visibility_tier_required: int              # Mapping fidelity required to perceive this realm's leyline directly
```

This makes adding a new concept-space realm a *metadata change*. Define the source-realm spec; every region that has any of its leyline weight automatically gets the contribution. The system propagates without code changes.

---

## Manifestation Gradient: Pressure to Outcomes

Local pressure (sum of leyline contributions exceeding local containment) produces escalating manifestations:

| Pressure tier | Manifestation | Example outcomes | Cadence |
|---------------|---------------|------------------|---------|
| **Trickle** | Ambient mana, normal magic, sanctuary maintenance | Magic works; Workshop production runs | Continuous |
| **Local convergence** | Spirit dens crystallize, minor resonance items can form, sanctuaries stabilize | Spirit Stag manifests in deep wood | Once per generation per intersection |
| **Pressure surge** | Dungeon manifestation; multi-day to multi-year arc | New dungeon appears at intersection; bleed-through becomes physically traversable | Once per several years per region |
| **Bleed crisis** | Realm-distortion zone; environmental anomalies; multi-Dungeon-Core cluster | Aparida-style "city inside a dungeon"; weather distortions; temporal slippage | Once per saeculum per realm |
| **Catastrophic flood** | Realm-overwrite threat; multi-generational containment campaign | The Demon-Realm Bleed of the 47th Saeculum; Realm History entry for thousands of game-years | Once per several saecula globally |

The thresholds between tiers are realm-specific (configurable per realm per source-realm-pair). A region's `bleed_threshold` plus current `total_pressure` determines tier; tier transitions trigger Storyline-system narrative seeds and god-actor responses.

This gives Arcadia a stakes-vocabulary that scales legibly. Catastrophic events aren't "the designer decided to author one"; they emerge when leyline composition shifts (driven by saecula, divine acts, accumulated mementos, player actions) push pressure past containment.

---

## Mapping Service Integration

The Mapping service already supports queryable spatial layers (per VISION.md "Map Layers"). Leyline composition becomes one of those layers, with fidelity gating per the Agency progressive-UX-expansion model:

| Mapping fidelity tier | What's visible |
|----------------------|----------------|
| **Minimal** | Nothing about leylines. Region looks ordinary. |
| **Low** | "There is a leyline here" — bare presence indication; mana density shimmer |
| **Medium** | Source-realm identification (this leyline is Pneuma-Pure / Pantheon / etc.) without weights |
| **High** | Full composition with weight percentages; rough trajectories visible |
| **Master** | Composition + predictive trajectory (saeculum-aware forecasting); intersection-pressure modeling; can predict where dungeons will form years before they manifest |

This satisfies the Fiona Rule (the system is observable from day 1; fidelity gates depth of perception, not existence) and the agency-as-earned-context principle (a Mapping-domain spirit earns visibility through domain investment).

A character with master Mapping fidelity is *cosmologically literate*. They look at a region and read its leyline composition like a chemist reads a periodic-table reaction. This is gameplay-relevant: master Mappers can predict dungeon emergence, advise sponsors on resonance-item-likely intersections, and warn settlements of approaching bleed crises before manifestation.

---

## Saeculum Dynamics: Composition Shifts Over Time

Generational cycles (the 80-100 year saecula in VISION.md) are tied to slow leyline composition shifts. Several drivers operate concurrently:

1. **Random walk** — small stochastic shifts within configured bounds; the world doesn't sit perfectly still
2. **Divine-act spikes and decay** — gods spend divinity to spike specific leyline weights (sanctuary creation, miracle); spikes decay at configured rates
3. **Cumulative-event-driven shifts** — accumulated mementos at intersections gradually shift composition (high death-memento density pulls Underworld leyline weight upward over centuries)
4. **War and mass-event shifts** — large-scale events can cause sharp shifts (a war zone accumulates Demon-Realm bleed proportional to violence intensity)
5. **Cross-realm pressure transfers** — when one realm's composition changes, adjacent-realm bleed weights shift accordingly
6. **Saeculum-cycle drivers** — the four-turning model from VISION.md drives configured composition shifts at each turning boundary

A region that was Pantheon-rich in the 47th saeculum may shift toward Demon-Realm dominance by the 50th. Buildings, sanctuaries, and infrastructure built during the prior composition feel "off" in the new one. This is Frieren-weight at the cosmological level — long-lived characters feel the world drifting under their feet.

This gives the saecula real teeth. They aren't just calendar markers; they're cosmological transitions visible in Mapping data and felt in environmental drift.

---

## Failure-Pattern Check

Running this framework through the immune-system catalogue:

| Pattern | Risk | Status |
|---------|------|--------|
| **Nanomachine Trap** | Late retroactive reveal that "the REAL realms behind everything are X" | ⚠️ **Defended by commitment**: the visible source-realm catalogue must be the working catalogue. New concept-space realms can be DISCOVERED through gameplay, but their existence must become observable to Mapping at the moment they become load-bearing. No hidden realm may retroactively explain everything. The Fiona Rule applies to source-realm reveals. |
| **Power Creep Eclipse** | Some source realm becomes a trump card overwhelming compositions | ✅ **Defended by cap**: per-realm-leyline weight caps (proposed 60%); even Pantheon realms cannot exceed without triggering catastrophic-bleed response (which is itself an event, not a free win). |
| **Cheat Shortcut** | Concept-space realms become magical-thinking sinks | 🔴 **Real risk requiring discipline**: every concept-space realm must have concrete output specs at the same standard as the foundational realms. Vague "Realm of Power" outputs are not allowed. Catalogue evolution requires per-entry specification. |
| **Static Factory** | Once defined, leyline compositions never change | ✅ **Defended by saeculum drift**: composition shift is built into the system. Multiple drivers ensure ongoing change. The Static Factory failure mode is impossible by design. |
| **Fiona Rule** | Hidden information advantage that's load-bearing | ✅ **Compliant**: leyline composition is queryable via Mapping Service from day 1. Fidelity gates depth, not existence. The system commits to "the catalogue is visible" from the start. |
| **Distributed-Spirit Principle** | Single cosmic guardian | ✅ **Defended**: multiple source realms with weight caps. No realm has metaphysical authority over the others. The framework structurally rejects monism. |
| **Substrate-Degrader Principle** | Antagonists who replace rather than parasitize | ✅ **Defended**: Demon-Realm and Void/Seal are explicitly substrate-degraders, not substitute substrates. Their leylines disrupt; they cannot replace upstream realms. Antagonists are agents OF source-realm bleed, not the realms themselves. |
| **Visible Dependency Chain** | Effects without traceable causes | ✅ **Defended**: dungeon properties ← leyline composition ← source-realm specifications. Fully traceable via Mapping at high fidelity. |

The watchpoints are real but tractable. The framework passes the immune-system check.

---

## Service Composition

### Implemented Services (available today)

| Service | Role |
|---------|------|
| **Realm (L2)** | Holds `RealmLeylineProfile` metadata; manages cross-realm relationships |
| **Location (L2)** | Holds per-location composition cache and modifier overrides |
| **Mapping (L4)** | Visualization layer; affordance queries extended with leyline-composition queries |
| **Environment (L4)** | Existing leyline_proximity field becomes derived from composition framework |
| **Worldstate (L2)** | Saeculum cycle phase; drives composition shift triggers |
| **Resource (L4)** | Compression of historical compositions for Realm History |
| **Workshop (L4)** | Lazy-evaluation engine for divine-spike decay, sanctuary maintenance, sanctuary-suppression-as-derived-metric |
| **Divine (L4)** | Divine-act spikes drive temporary weight boosts; consumed divinity = spike magnitude × duration |
| **Storyline (L4)** | Generates narrative seeds from tier-transition events |
| **Actor (L2)** | God-actor and dungeon-core-actor decisions reference local composition |
| **Character (L2) / Character-Personality (L4)** | Personality affinities for source-realm types; affinity gates Mapping fidelity progression in the source-realm dimension |
| **Seed (L2)** | Spirit accumulates source-realm familiarity through play; familiarity feeds resonance item activation prerequisites |

### What This Framework Adds

| Addition | Service | Type |
|----------|---------|------|
| `RealmLeylineProfile` schema | Realm | Schema field |
| `LeylineCompositionAtLocation` schema | Location | Schema field with lazy materialization |
| `SourceRealmDefinition` schema | Realm (or new sibling) | Catalogue entry schema |
| Mapping query endpoints for composition retrieval | Mapping | New endpoints |
| Composition shift event types | Worldstate / Realm | New event types |
| God-actor ABML behaviors for divine spike management | Divine + ABML content | Behavior authoring |
| Source-realm catalogue seed data | Realm + content | Seed data |

### What Is NOT Changed

- **No new plugins** — every schema and endpoint extends an existing service
- **No service hierarchy violations** — composition data flows L2 (Realm, Location) → L4 (Mapping, Environment) via existing variable provider patterns
- **No code-generation script changes** — additions are within existing plugin boundaries
- **Existing Sanctuary, Spirit Den, Logos Resonance Item designs continue to function** — they become queryable via the leyline composition framework with no breaking changes

---

## Hierarchy Compliance

The composition data lives at L2 (Realm and Location services). Higher-layer services consume it via the existing variable provider pattern:

| Consumer | Layer | Access pattern |
|----------|-------|----------------|
| Mapping | L4 | Queries Realm and Location for composition; aggregates into spatial visualization |
| Environment | L4 | Computes derived fields (`leyline_proximity`, `mana_density`) from composition |
| Divine | L4 | Reads composition before computing spike effects; writes spike events back through Realm |
| Storyline | L4 | Subscribes to tier-transition events; generates narrative seeds |
| Actor (god-actor / dungeon-core ABML) | L2 → L4 via Variable Provider | Reads `${realm.leyline_composition.*}` and `${location.leyline_composition.*}` |
| Workshop | L4 | Sanctuary / Spirit Den blueprints reference composition-derived rate modifiers |

No service depends upward. The composition data flows down through the hierarchy following established patterns.

---

## Implementation Sequence

The framework can be implemented incrementally:

### Phase 1: Source Realm Catalogue + Schema

1. Define `SourceRealmDefinition` schema with seed data for the foundational realms (Logos-Pure, Pneuma-Pure, Underworld, Spirit-Origin)
2. Add `RealmLeylineProfile` to Realm schema (initially empty for existing realms)
3. Add `LeylineCompositionAtLocation` to Location schema with lazy materialization

**Result**: The catalogue exists; nothing computes from it yet.

### Phase 2: Composition Computation

4. Implement composition materialization (Realm-level algorithm + Location-level overrides)
5. Implement Mapping query endpoints for composition retrieval at variable fidelity
6. Add composition data to existing Environment derived fields

**Result**: Composition is queryable; existing Environment behavior is unchanged but now derived from composition.

### Phase 3: Manifestation Tier Mapping

7. Define manifestation tier thresholds per realm
8. Implement tier-transition event emission
9. Storyline subscribes; god-actors subscribe; Dungeon Core spawning logic considers composition

**Result**: Pressure-driven manifestation emerges from composition.

### Phase 4: Drift and Divine Spikes

10. Implement saeculum-driven composition shifts
11. Divine-act spike management via Workshop lazy decay
12. Cross-realm bleed weight transfer mechanics

**Result**: Compositions shift over time; the world drifts.

### Phase 5: Existing System Integration

13. Sanctuary `sanctuary_suppression` becomes derived from Pantheon-leyline-spike weight
14. Spirit Den crystallization conditions reference composition
15. Logos Resonance Item activation prerequisites reference source-realm signatures
16. Underworld region characteristics reference upstream composition

**Result**: Existing systems are unified under the framework. No breaking changes; semantics are clarified.

### Phase 6: Concept-Space Realms

17. Author concept-space realm catalogue entries (Realm of Unfinished Labor, Realm of Forgotten Oaths, etc.)
18. Author elemental concept-realm entries (Storm, Flame, Tide, Stone)
19. Define realm-interaction modifiers between concept realms and player realms

**Result**: The catalogue grows. New concept realms become metadata changes.

---

## Open Questions

1. **Catalogue completeness threshold**: How many source realms are "enough" for launch? Foundational + Pantheon + ~5-8 concept-space realms = a starter catalogue rich enough to differentiate Omega/Arcadia/Fantasia meaningfully. But the catalogue is open-ended; later additions are metadata changes. What's the minimum for a coherent first launch?

2. **Spatial algorithm specificity**: Each realm needs a spatial algorithm (fractal_noise / gradient_from_node / etc.) for distributing source-realm weights across its territory. How parameterized should these be? Realm-author-specified per realm, or selected from a small canonical set with parameters?

3. **Divine spike pricing**: How much divinity does a god spend to spike a leyline weight by what amount for what duration? Sanctuaries at the existing-design's cost rates can serve as a calibration anchor.

4. **Catastrophic-bleed containment design**: When pressure crosses the catastrophic threshold, what's the containment system? Multi-Dungeon-Core coordination? Pantheon emergency intervention? Multi-character generational arc? The framework establishes the trigger condition; the response infrastructure needs design.

5. **Cross-realm bleed asymmetry**: Should cross-realm bleed be symmetric (Arcadia → Omega flow = Omega → Arcadia flow) or asymmetric (different realms have different "permeability" to others)? Asymmetric is more flexible but introduces more parameters per realm-pair.

6. **Mapping fidelity mechanics for Master tier**: The Master tier promises predictive trajectory ("predict where dungeons will form"). What's the algorithmic basis? Saeculum-projected composition + known divine spike timetables + cumulative memento accumulation + interaction modifiers. The simulation can compute this; the question is how the simulation surfaces it to the player without trivializing exploration.

7. **Catalogue governance**: Source-realm definitions are load-bearing. Who can add new ones (designers only? per-realm authors?), and what's the review process to ensure new entries meet the "concrete output specs" standard?

8. **Memento-leyline relationship specifics**: The Underworld leyline current is partially driven by accumulated mementos. The exact transmutation function (memento density → Underworld leyline weight contribution) needs specification. Workshop blueprints can encode it but the math needs design.

9. **Hearthworks alignment opportunity**: The Hearthworks LN's basin is currently "high-mana node" as a discrete fact. Aligning the basin with this framework (as a specific multi-realm leyline intersection, with mythril/fairy steel/crystal turtle traits derived from composition) is optional. If pursued, it gives the LN narrative weight as the canonical first-iteration demonstration of the framework.

10. **Player-realm-as-source-realm self-reference**: When Arcadia bleeds into Omega, the bleed weight is computed from Arcadia's composition. But Arcadia's composition includes Omega bleed. Is this stable (fixed-point convergence) or does it require explicit ordering rules to avoid recursive computation issues?

---

## Related Documents

- [VISION.md](../reference/VISION.md) — Multi-realm architecture (Omega/Arcadia/Fantasia), System Realms, Logos/Pneuma metaphysics, Five North Stars
- [PLAYER-VISION.md](../reference/PLAYER-VISION.md) — Agency progressive-UX-expansion model (drives Mapping fidelity gating)
- [SANCTUARIES-AND-SPIRIT-DENS.md](SANCTUARIES-AND-SPIRIT-DENS.md) — Sanctuaries become Pantheon-leyline-spike locations; spirit dens become Pneuma-Pure × species-logos intersections
- [LOGOS-RESONANCE-ITEMS.md](LOGOS-RESONANCE-ITEMS.md) — Resonance items carry the local leyline signature; activation prerequisites become source-realm-affinity prerequisites
- [DUNGEON-MANA-ABSORPTION.md](DUNGEON-MANA-ABSORPTION.md) — Dungeon mana intake becomes pressure-tier-derived
- [MEMENTO-INVENTORIES.md](MEMENTO-INVENTORIES.md) — Memento accumulation drives Underworld-leyline-weight shifts
- [PREDATOR-ECOLOGY-PATTERNS.md](PREDATOR-ECOLOGY-PATTERNS.md) — Ecological patterns interact with leyline composition (high Pneuma-Pure → enhanced predator/prey vitality)
- [LOCATION-BOUND-PRODUCTION.md](LOCATION-BOUND-PRODUCTION.md) — Location production rates may include leyline-composition rate modifiers
- [DEATH-AND-PLOT-ARMOR.md](DEATH-AND-PLOT-ARMOR.md) — Death feeds Underworld leyline current
- [arcadia-kb: Underworld and Soul Currency System](~/repos/arcadia-kb/03%20-%20Character%20Systems/Underworld%20and%20Soul%20Currency%20System.md) — The Underworld is a source realm; its leyline current is the logos-river side of the network

---

## Design Principles

1. **Realms are parameterizable, not authored.** Every realm's character emerges from its leyline composition. Authorial choice happens at the catalogue level (defining source realms) and the realm-profile level (specifying weights and algorithms), not at the per-region aesthetic level.

2. **The catalogue is open but disciplined.** New source realms can be added as metadata changes, but each must meet the concrete-output-specs standard. Vague concept realms are rejected.

3. **Visible composition, gated fidelity.** The leyline composition exists from day 1 and is queryable. What varies between players is fidelity of perception, per the agency-as-earned-context principle. This satisfies the Fiona Rule.

4. **Pressure produces manifestation.** Dungeons, spirit dens, sanctuaries, and bleed crises are emergent results of composition + pressure + time. None are authored placement.

5. **Source realms are first-class design objects.** Each is specified to the same standard as a major service. Concept-space realms cost minimal infrastructure but pay rich design dividends.

6. **Drift is permanent.** Compositions shift across saecula. No region remains static. The world is geologically alive in its spiritual substrate.

7. **No realm dominates.** Per-realm-leyline weight caps prevent any single source realm from collapsing the system into its own trump card. Even Pantheon realms operate within the cap.

8. **Substrate-degraders parasitize, do not replace.** Demon-Realm and Void/Seal disrupt local composition but cannot become the foundational substrate. The system structurally rejects "evil realms" that operate equivalently to the foundational realms.

9. **Existing designs are subsumed, not replaced.** Sanctuaries, Spirit Dens, Resonance Items, and the Underworld continue to function with their existing semantics. The framework explains what was previously implicit.

10. **Zero new plugins.** Every schema, endpoint, and behavior composes from existing services. The composability thesis applies at the cosmology layer.

---

*This document describes the design vision for the leyline composition framework. Implementation details (Realm/Location schema specifics, source-realm catalogue seed data, Mapping query endpoints, ABML behavior templates for divine actors managing leyline spikes) belong in the relevant service deep dives and implementation plans. The framework's load-bearing design decision is the source-realm catalogue — that catalogue's curation determines whether the framework produces structurally rich realms or magical-thinking sinks.*
