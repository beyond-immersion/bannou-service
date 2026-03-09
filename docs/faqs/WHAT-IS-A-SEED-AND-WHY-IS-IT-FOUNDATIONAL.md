# What Is a Seed and Why Is It Foundational?

> **Last Updated**: 2026-03-08
> **Related Plugins**: Seed (L2), Collection (L2), Status (L4), Gardener (L4), Faction (L4), Dungeon (L4), Actor (L2), Quest (L2), Currency (L2), Item (L2), Relationship (L2), Puppetmaster (L4)
> **Short Answer**: A Seed is a generic progressive growth primitive that starts empty and
> gains capabilities as it accumulates experience across named domains. It is foundational
> (L2) because progressive growth is a core game mechanic that multiple L4 features depend
> on, and because Seeds are agnostic to what they represent -- guardian spirits, dungeon
> cores, faction governance, and crafting specializations are all Seeds configured differently.

---

## What Problem Seeds Solve

Games are full of things that grow. Skill trees grow. Reputation grows. Proficiency grows. Guardian spirits grow. Dungeon cores grow. Guild influence grows. Relationship depth grows. In a traditional architecture, each of these gets its own bespoke progression system:

- Skills have a skill point system with a skill tree.
- Reputation has a faction reputation bar with thresholds.
- Crafting proficiency has a separate XP counter per discipline.
- Guardian spirits have... something custom.

Each system reinvents the same fundamental pattern: start with nothing, accumulate progress across one or more dimensions, unlock capabilities at thresholds, and expose those capabilities to gate actions elsewhere in the game. The math differs (linear vs. logarithmic vs. step functions), the thresholds differ, the capability types differ -- but the pattern is identical.

Seeds extract this pattern into a single, configurable primitive. Instead of building five progression systems, you build one and configure it five ways.

---

## How Seeds Work

A Seed has:

- **A type** (string code, not enum -- so new types require no schema changes): `"guardian"`, `"dungeon_core"`, `"combat_archetype"`, `"crafting_specialization"`, `"governance_role"`.
- **An owner** (polymorphic -- accounts, actors, realms, characters, relationships): who or what this seed belongs to.
- **Named domains** of accumulated metadata: a guardian spirit seed might have domains for `combat_experience`, `social_experience`, `crafting_experience`, each tracking different kinds of growth input.
- **Growth phases** with configurable thresholds: as domain values cross thresholds, the seed transitions through phases (e.g., "dormant" -> "awakening" -> "active" -> "flourishing").
- **A capability manifest**: at each phase, the seed grants specific capabilities that external systems can query to gate actions.

The Seed service itself does not know or care what a "guardian spirit" is. It knows that seed type `"guardian"` has these phases, these domains, these threshold rules, and these capability outputs. The semantic meaning is entirely in the consumer's interpretation.

---

## Why L2 and Not L4

The classification question comes down to: is "progressive growth" a foundational game concept or an optional feature?

### The Foundational Argument

Consider what depends on Seeds:

- **Guardian spirits** are the player's meta-progression across character lifetimes. This is the core loop of Arcadia -- the guardian spirit grows by feeding it experiences through gameplay. If Seeds were L4, the guardian spirit model would be an optional feature, which contradicts the game's fundamental design.

- **Faction governance** uses Seeds for capability-gated progression. Faction (L4) models factions as seed-based entities whose governance capabilities (norm definition, enforcement, territory claiming) unlock through growth phases. If Seeds were L4, Faction would have a same-layer dependency that works -- but other L2 services like Quest also need growth-gated prerequisites, which would create forbidden upward dependencies.

- **Any L2 service that needs progression** would have a forbidden upward dependency if Seeds were L4. A quest reward that grows a character's seed would require Quest (L2) to call Seed (L4) -- a hierarchy violation.

### The Agnosticism Argument

Seeds are deliberately type-agnostic. The service has no concept of "guardian spirit" or "dungeon core" in its code. It processes seed types, domains, thresholds, and capabilities. This is the same design philosophy as the Item service (which has no concept of "sword" or "potion" -- it processes templates and instances) or the Relationship service (which has no concept of "friendship" or "marriage" -- it processes entity-to-entity bonds with types).

Services that are agnostic to their content belong at the foundation layer. They provide primitives that higher-layer services imbue with meaning. A "guardian spirit" is a specific configuration of the Seed primitive, just as a "friendship" is a specific configuration of the Relationship primitive.

### The Alternative Is Worse

If Seeds were L4, every system that needs progressive growth at L2 would need its own progression logic:

- Quest would need custom prerequisite tracking for growth-gated content.
- Actor would need custom state for dungeon core progression.
- Character would need custom fields for any character-level growth.

Each implementation would be slightly different, slightly incompatible, and independently maintained. The Seed service exists precisely to prevent this proliferation.

---

## Seed Types Are Registered, Not Hardcoded

A critical design choice: seed types are string codes, not schema-defined enums. New seed types are registered at runtime via API:

```
POST /seed/register-type
{
  "type_code": "combat_archetype",
  "growth_phases": ["novice", "adept", "expert", "master"],
  "domains": ["offensive", "defensive", "tactical"],
  "capability_rules": { ... }
}
```

This means a new L4 service can introduce a new kind of progressive growth without any schema changes, code generation, or modifications to the Seed service. The Seed service just stores and processes the configuration.

This is why Seeds are sometimes described as "the game's answer to a generic key-value store, but for progression." The data model is simple and generic. The semantic richness comes from how consumers use it.

---

## The Collection -> Seed -> Status Pipeline

Seeds do not grow in isolation. They are the middle stage of a three-service pipeline that mechanizes the PLAYER-VISION's core principle: **experience -> understanding -> manifestation**.

```
Experience happens (combat, discovery, blessing, crafting, etc.)
    |
    v
Collection (L2) -- permanent record of "what you've experienced"
    |
    | dispatches to ICollectionUnlockListener (DI listener, in-process)
    v
Seed (L2) -- "how you've grown" from accumulated experiences
    |
    | queried by Status (L4 querying L2, valid hierarchy direction)
    v
Status (L4) -- "what effects you currently have" based on growth state
```

**Collection is the upstream source.** When a collection entry is unlocked (creature encountered, music heard, blessing received), Collection dispatches notifications to registered `ICollectionUnlockListener` implementations via DI. The `SeedCollectionUnlockListener` in lib-seed matches entry tags against seed type `collectionGrowthMappings` to drive growth in relevant domains. The seed type's registration defines which collection tags map to which growth domains, so the same collection entry can feed different seeds in different ways.

**Status is the downstream manifestation.** Status (L4) queries Seed (L2) for growth state and capability data, then computes actual gameplay effects -- resistances, buffs, UX capability unlocks. Any system that needs to know "what can this entity do / resist / access" queries Status.

### Cross-Seed Pollination

PLAYER-VISION explicitly states: *"Accumulated experience cross-pollinates between seeds"* and *"The spirit's accumulated understanding crosses seed boundaries."*

**Collection is the cross-pollination medium.** One collection entry unlock dispatches to the `SeedCollectionUnlockListener` which matches against EVERY seed the entity owns. Each seed interprets the growth through its own type rules. A combat-focused guardian seed and a dungeon master seed both receive growth from the same "battle-hardened" collection entry, but grow in different domains based on their type definitions.

Seeds never talk to each other. They share a common upstream source. The cross-pollination is a natural consequence of the data flow, not an explicit feature.

### Cross-Seed Growth and Feedback Loops

Seeds amplify each other through two mechanisms. First, the Seed service's built-in `SameOwnerGrowthMultiplier` cross-pollinates growth to sibling seeds of the same owner during `RecordGrowth` -- this is direct Seed-to-Seed, not through Collection. Second, L4 consumers implementing `ISeedEvolutionListener` (Gardener, Faction, Status) can react to phase changes and capability updates, potentially triggering new collection entries that feed back into the pipeline.

A crafting-focused seed suddenly gets a tiny nudge in combat resilience because its sibling seed recorded combat growth and cross-pollination applied. The feedback loop operates without any cross-seed logic -- same-owner growth sharing and the Collection listener pipeline handle it naturally.

### Seed Cloning

When a seed is cloned to a new owner, the current growth state is copied but the upstream collection source changes. The new owner has different collection entries flowing downstream. Domains where the new owner has fewer relevant experiences begin to decay. Domains where the new owner has MORE relevant experiences grow faster. The cloned seed organically reshapes itself to match its new owner's actual experience profile -- no adaptation logic required, just the pipeline doing what it always does.

---

## The Guardian Spirit Connection

The most important Seed type in Arcadia is the guardian spirit (seed type code: `guardian`). The player's account (L1) bonds to a guardian seed (L2) which bonds to characters (L2). The guardian spirit grows through the Collection -> Seed pipeline:

1. Player possesses a character and plays the game.
2. Gameplay generates experiences that unlock collection entries (creatures encountered, recipes learned, places discovered, blessings received).
3. Collection dispatches unlock notifications via `ICollectionUnlockListener` to the guardian seed's growth domains.
4. As domains cross thresholds, the guardian spirit evolves -- gaining new phases, new capabilities, new bonding options.
5. Threshold crossings may generate new collection entries, feeding back into the pipeline and cross-pollinating into the player's other seeds.
6. When a character dies, the quality of their life (the "Fulfillment Principle") determines how much logos flows to the guardian spirit.
7. The guardian spirit carries forward across character lifetimes, creating the player's true long-term progression.

A player with three seeds -- a combat character, a dungeon master, and a merchant dynasty -- sees them all growing from shared collection entries. The warrior's combat experiences trickle into the dungeon master's encounter design capabilities. The merchant's trade experiences trickle into the warrior's economic awareness. None of this requires cross-seed logic. The Collection layer is the shared medium and Seeds just react to what arrives.

Without Seeds at L2, this entire model would either live in a custom one-off system (violating the "extract common patterns" principle) or live at L4 (making the player's core progression loop an optional feature). Neither is acceptable.

---

## The Composability Payoff

Because Seeds are a generic primitive at L2, higher-layer services compose them freely:

| Service | Layer | Role in Pipeline | What It Does |
|---------|-------|-------------------|-------------|
| Collection | L2 | Upstream source | Dispatches to `ICollectionUnlockListener` when entries are unlocked |
| Seed | L2 | Core primitive | Processes growth, manages phases and capabilities |
| Status | L4 | Downstream consumer | Queries seed state, computes gameplay effects |

Seed types are registered by their consumers:

| Consumer | Seed Type | What It Represents |
|----------|-----------|-------------------|
| Gardener (L4) | `"guardian"` | Player meta-progression |
| Dungeon (L4, planned) | `"dungeon_core"` | Dungeon intelligence growth |
| Faction (L4) | faction-specific codes | Faction governance progression |
| (future) Streaming | `"streamer"` | Streamer career progression |
| (future) Skills | `"combat_archetype"` | Weapon/style mastery |
| (future) Crafting | `"crafting_specialization"` | Discipline proficiency |

Each consumer registers its seed type, contributes growth via Collection entries, and queries capability manifests. The Seed service handles phase transitions, threshold math, and persistence. The consumer handles meaning.

This is the same architectural pattern as Currency (L2) -- Currency does not know what "gold" or "silver" means, it just manages multi-currency wallets with exchange rates. Seeds do not know what "guardian spirit" means, they just manage multi-domain growth with phase thresholds. Both are generic primitives at the foundation layer, given semantic meaning by their consumers.
