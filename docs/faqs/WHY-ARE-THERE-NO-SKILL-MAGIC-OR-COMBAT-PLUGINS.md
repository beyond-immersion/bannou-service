# Why Is There No Combat / Skill / Magic / Class Plugin?

> **Short Answer**: Because combat, skills, magic, and character classes are not *things* in Bannou — they are *behaviors* that emerge from the interaction of existing primitives. Character mastery is Seed (progressive growth). Ability unlocks are License (grid-based progression boards). Active effects are Status (unified entity effects). Content discovery is Collection (cataloging). Combat preferences are Character-Personality. Species instincts are Ethology. Behavioral decisions are Actor + ABML. A dedicated plugin for any of these concepts would duplicate what existing services already compose together.

---

## The Pattern: Composite Behavioral Systems

Traditional game backends treat combat, skills, magic, and classes as discrete systems with dedicated databases, APIs, and business logic. Bannou rejects this approach because these concepts are not standalone domains — they are behavioral outcomes that arise from the interaction of orthogonal primitives.

Consider what "a mage" actually means in Bannou:

| Aspect of "Being a Mage" | Primitive That Provides It | Layer |
|---|---|---|
| Magic aptitude (genetics) | **Character-Lifecycle** — `${heritage.aptitude.magic}` | L4 |
| Magic proficiency (growth) | **Seed** — a "magic" seed type with phase-based capabilities | L2 |
| Spell repertoire (progression) | **License** — grid-based board with spell nodes as items | L4 |
| Spell effects (buffs/debuffs) | **Status** — unified effects query layer | L4 |
| Casting behavior (decisions) | **Actor** — ABML behavior documents referencing `${seed.magic.*}` | L2 |
| Casting personality (style) | **Character-Personality** — `${combat.preferredRange}`, risk tolerance | L4 |
| Species affinity (nature) | **Ethology** — `${nature.occult}` behavioral axis | L4 |
| Divine empowerment | **Divine** — blessings granted as Status effects via Collection | L4 |
| Moral hesitation | **Obligation** — "should I cast this forbidden spell?" GOAP cost modifiers | L4 |

No single plugin "owns" magic. A character is a mage because nine independent systems converge to make them behave like one. The same decomposition applies to every composite concept that agents or designers might propose as a "missing" plugin.

---

## The Six Concerns and Where They Live

Traditional game systems for combat, skills, and magic all handle the same six concerns. Every one of them is already covered:

| Concern | What It Means | Already Covered By |
|---|---|---|
| Progressive mastery | Proficiency improves with practice | **Seed** — domain depth grows with use, fidelity formulas map depth to capability |
| Ability trees | Structured unlock paths with costs | **License** — literally described as "skill trees, license boards, tech trees" |
| Active/passive effects | Unlocked abilities modify stats or behavior | **Status** — two-source architecture merges item-based effects + seed-derived capabilities |
| Content catalog | Learned recipes, discovered techniques | **Collection** — opaque types support `"combat_techniques"`, `"recipes"`, `"spells"` |
| Behavior variables | NPCs react to their own capability state | **Seed** — `SeedProviderFactory` provides `${seed.*}` to Actor via Variable Provider Factory |
| Quest prerequisites | "Requires sword skill 5" | **Seed** — `IPrerequisiteProviderFactory` adapter queries capability manifest |

There is no orphaned concept that requires its own service.

---

## Combat Is Not a Plugin

The most common "missing plugin" suggestion. Here is why combat is not a service:

**Combat is a behavior, not a data domain.** It is the emergent result of actors making decisions in a spatial environment using their capabilities, preferences, and constraints. There is nothing to store that isn't already stored elsewhere:

| "Combat" Aspect | Where It Lives |
|---|---|
| What actions are available | **Seed** capabilities + **License** unlocks |
| What the character prefers | **Character-Personality** `${combat.*}` variables (style, range, group role, risk tolerance) |
| What the species does instinctively | **Ethology** `${nature.aggression}`, `${nature.fear_threshold}`, `${nature.persistence}` |
| Active buffs and debuffs | **Status** effects (spell buffs, divine blessings, equipment bonuses, environmental effects) |
| Spatial awareness and affordances | **Mapping** ("what objects within 5m can be grabbed?", "are there elevation changes?") |
| Moral constraints on actions | **Obligation** cost modifiers from active contracts, guild charters, oaths |
| The actual decision-making | **Actor** executing ABML behavior documents that read all of the above via Variable Provider Factory |
| Choreographic composition | **Event Brain** actors streaming cinematic sequences from environment + capability data |

A "combat plugin" would have to either (a) duplicate all of this data into its own store and keep it synchronized, or (b) be a pure query aggregator that calls these services and returns combined results. Option (a) creates a massive synchronization burden for zero benefit. Option (b) is what the Actor runtime already does — it reads from all variable providers and makes decisions.

### The Combat Dream (from VISION.md)

The vision for combat is explicitly architectural, not service-based:

> Event Brain actors query Mapping for spatial affordances, Character Agents for capabilities and personality, compose streaming cinematics via the Cinematic Interpreter, and coordinate three-version temporal desync (canonical past, participant present, spectator projection).

Every component in this pipeline already exists or is planned as part of an existing service. Combat is the *interaction* of these systems, not a system unto itself.

---

## Skills Are Not a Plugin

Consider a character learning swordsmanship:

The character owns a `combat_archetype` seed with domains like `melee.sword`, `melee.axe`, `defense.block`. Every time the character fights with a sword, `melee.sword` depth increases. The seed type's capability rules define thresholds:

- Depth 1.0 unlocks `basic_strikes` (fidelity: 0.0 -> 1.0 over depth 1-2)
- Depth 3.0 unlocks `advanced_techniques` (fidelity: 0.0 -> 1.0 over depth 3-6)
- Depth 5.0 unlocks `master_forms` (fidelity: 0.0 -> 1.0 over depth 5-10)

Fidelity is continuous, not binary. A character with `melee.sword` depth 4.0 has `basic_strikes` at full fidelity and `advanced_techniques` at partial fidelity. The character is competent but not yet a master.

**Ability unlocks (License)**: The character's class or profession has a License board. Spending LP unlocks specific ability nodes — "Two-Handed Grip", "Riposte", "Whirlwind Strike". These are discrete choices with adjacency constraints. Mastery determines how well you perform; unlocks determine what you can perform.

**Effects (Status)**: Status aggregates both sources into a single query — seed-derived capabilities (`basic_strikes` at fidelity 0.85) and item-based statuses ("Blessed Blade" buff, +10% damage, 5 min TTL). Any system asking "what effects does this character have?" gets a unified answer.

**Behavior (Seed -> Actor)**: The character's NPC brain accesses `${seed.combat_archetype.melee.sword}` in ABML expressions. An NPC with high sword depth preferentially chooses sword-based combat actions. No skill service needed — it reads seed data through the Variable Provider Factory.

**Prerequisites (Seed -> Quest)**: When a quest requires "sword skill >= 5", the `skill` prerequisite provider factory checks the character's seed capability manifest. This is a thin adapter (~50 lines), not a service.

---

## Magic Is Not a Plugin

Arcadia's magic is metaphysically grounded — thermodynamic pneuma control with six-stage spellcasting. The mechanical representation maps identically to skills:

- **Spell proficiency** -> Seed domain depths (`magic.fire`, `magic.pneuma_control`, `magic.divination`)
- **Spell unlocks** -> License board nodes or Collection entries
- **Active spell effects** -> Status effects (buffs, channeling states, cooldowns)
- **Spell prerequisites** -> `IPrerequisiteProviderFactory` adapter over Seed capabilities
- **NPC spellcasting decisions** -> `${seed.magic_discipline.*}` in ABML behavior expressions

A `magic` prerequisite provider factory works identically to a `skill` one — it queries a different seed type code.

---

## Classes Are Not a Plugin

"Warrior", "mage", "healer" are not service-level classifications. A character's class identity emerges from:

- **Heritage aptitudes** (Character-Lifecycle) — genetic predisposition toward physical or magical domains
- **Seed growth** — which domains the character has invested time in
- **License boards** — which ability trees they've progressed through
- **Character-Personality** combat preferences — defensive vs aggressive, melee vs ranged, solo vs support
- **Ethology** species nature — some species are naturally inclined toward certain roles

There is no "class registry" because class is not a stored property — it is a description of the intersection of a character's growth, unlocks, preferences, and nature. Two characters with identical license boards but different personality traits and seed depths will feel like different classes in practice.

---

## The SERVICE-HIERARCHY Mentions Skills and Magic

The Prerequisite Provider Factory pattern in SERVICE-HIERARCHY.md lists `skill` and `magic` as planned dynamic prerequisite providers for Quest:

| Category | Type | Service | How Quest Handles |
|---|---|---|---|
| Built-in | `quest_completed` | Quest (L2) | Direct check |
| Built-in | `currency` | Currency (L2) | `ICurrencyClient` |
| Dynamic | `skill` | ??? | Provider factory |
| Dynamic | `magic` | ??? | Provider factory |
| Dynamic | `achievement` | Achievement (L4) | Provider factory |

Achievement exists as a full L4 service because it has its own domain (trophies, rarity, platform sync, event-driven auto-unlock). But `skill` and `magic` don't need dedicated services — they need thin `IPrerequisiteProviderFactory` adapter implementations (~50 lines each) that query Seed capabilities. These adapters will live in whichever L4 consumer eventually registers seed types for character progression.

---

## Why Not Build Them Anyway?

The strongest argument: "Players think in terms of skills and combat, not seeds and license boards. A dedicated service would present a player-friendly abstraction."

This confuses the player-facing API with the data model. Players don't call Bannou services directly — they interact through game clients that present whatever UX the game designers choose. The client can display "Sword Skill: Level 5" while the backend stores `seed.combat_archetype.melee.sword.depth = 5.3`. The abstraction layer belongs in the client UX, not in a redundant backend service.

Building a combat/skill/magic plugin would mean:

1. **Duplicating Seed's growth model** (progressive depth with thresholds) but calling it "skill levels" or "combat stats"
2. **Duplicating License's unlock model** (structured ability trees) but calling them "skill trees" or "spell books"
3. **Duplicating Status's effects model** (active buffs from abilities) but calling them "combat effects"
4. **Duplicating Character-Personality's preferences** but calling them "combat configuration"
5. **Adding a coordination burden** (keeping the duplicate state synchronized with the authoritative sources)

Every duplicate creates a synchronization problem. When the seed's domain depth changes, the skill level must update. When a license node is unlocked, the skill's ability list must update. This coordination overhead is pure cost with no benefit — the existing services already compose without synchronization because they are the authoritative sources, not mirrors.

---

## When IS a New Plugin Warranted?

Some gameplay concepts do warrant their own plugin — but only when they need to **orchestrate** existing primitives in a way that no existing service handles. These are "thin orchestration layers":

| Orchestrator | What It Composes | Why It Needs to Exist |
|---|---|---|
| **Quest** | Contract milestones + rewards + prerequisites | Quest-flavored API over Contract FSM |
| **Escrow** | Currency + Items + Contracts | Multi-party atomic exchange coordination |
| **License** | Inventory + Items + Contracts | Grid-based progression board UX |
| **Divine** | Relationship + Seed + Currency + Status + Collection | Deity economy and blessing orchestration |
| **Faction** | Seed + Relationship + Obligation | Organizational growth and norm enforcement |

If a proposed plugin is a thin orchestration layer that composes existing primitives into a new player-facing experience — and no existing orchestrator covers the use case — then it may be warranted. But if it's trying to *own* a concept that's inherently composite (combat, skills, magic, classes), it's the wrong approach.

---

## The Litmus Test

Before proposing a new plugin for a gameplay system, answer these questions:

1. **Can the concept be decomposed into existing primitives?** If yes, it's not a plugin — it's a composition.
2. **Does the concept need its own state that no existing store covers?** If everything lives in Seed/License/Status/Collection/Character-Personality already, there's no new state to manage.
3. **Does the concept need orchestration logic that no existing orchestrator provides?** Quest, Escrow, License, Divine, and Faction already cover most multi-primitive coordination patterns.
4. **Would the plugin just be a query aggregator?** Status already aggregates effects from all sources. Actor already aggregates variable providers. Adding another aggregator creates confusion about which is authoritative.
5. **Is this a behavior or a thing?** Behaviors emerge from Actor + variable providers. Things are stored in existing data primitives. Neither requires a new plugin.

If all five answers point to existing primitives, the "missing plugin" is not missing — it was never needed.

---

## The Composition Is the Design

Arcadia's approach to gameplay systems is deliberately decomposed:

| Primitive | Service | What It Answers |
|---|---|---|
| "How good am I at this?" | Seed | Continuous mastery via domain depth and fidelity |
| "What can I do?" | License | Discrete ability unlocks via grid-based progression |
| "What's affecting me right now?" | Status | Unified effects from items, seeds, contracts |
| "What have I discovered?" | Collection | Content catalog from gameplay experience |
| "What do I prefer?" | Character-Personality | Combat style, risk tolerance, group role |
| "What am I naturally?" | Ethology | Species-level behavioral instincts |
| "What should I do?" | Actor + ABML | Behavioral decisions from all variable providers |

This decomposition is more powerful than monolithic gameplay plugins because the primitives compose freely. A dungeon core's capabilities use the same Seed/Status primitives as a character's combat abilities. A guardian spirit's progressive agency uses the same Seed primitives as an NPC's magic proficiency. A faction's governance capabilities use Seeds. A god's domain power uses Seeds. None of these require a "skill" or "combat" or "magic" concept — they all use the same generic infrastructure, composed differently.

The answer to "where is the combat/skill/magic plugin?" is: it's Seed + License + Status + Collection + Character-Personality + Ethology + Actor, composed. That composition is the gameplay system.
