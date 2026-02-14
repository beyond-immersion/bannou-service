# Why Is There No Skill Plugin?

> **Short Answer**: Because "skills" in Arcadia are a composite concept, not a single system. Character mastery is Seed (progressive growth across domains). Ability unlocks are License (grid-based progression boards). Active effects are Status (unified entity effects layer). Content unlocks are Collection (discovery-based cataloging). A dedicated skill plugin would duplicate what these four services already compose together.

---

## The SERVICE-HIERARCHY Mentions Skills

The Prerequisite Provider Factory pattern in SERVICE-HIERARCHY.md lists `skill` and `magic` as planned dynamic prerequisite providers for Quest:

| Category | Type | Service | How Quest Handles |
|---|---|---|---|
| Built-in | `quest_completed` | Quest (L2) | Direct check |
| Built-in | `currency` | Currency (L2) | `ICurrencyClient` |
| Dynamic | `skill` | ??? | Provider factory |
| Dynamic | `magic` | ??? | Provider factory |
| Dynamic | `achievement` | Achievement (L4) | Provider factory |

Achievement exists as a full L4 service because it has its own domain (trophies, rarity, platform sync, event-driven auto-unlock). But `skill` and `magic` don't need dedicated services -- they need thin `IPrerequisiteProviderFactory` adapter implementations (~50 lines each) that query Seed capabilities.

---

## What a "Skill Plugin" Would Need to Provide

Traditional game skill systems handle six concerns:

| Concern | What It Means | Already Covered By |
|---|---|---|
| Progressive mastery | Proficiency improves with practice | **Seed** -- domain depth grows with use, fidelity formulas map depth to capability |
| Ability trees | Structured unlock paths with costs | **License** -- literally described as "skill trees, license boards, tech trees" |
| Active/passive effects | Unlocked abilities modify stats or behavior | **Status** -- two-source architecture merges item-based effects + seed-derived capabilities |
| Content catalog | Learned recipes, discovered techniques | **Collection** -- opaque types support `"combat_techniques"`, `"recipes"`, `"spells"` |
| Behavior variables | NPCs react to their own skill state | **Seed** -- `SeedProviderFactory` provides `${seed.*}` to Actor via Variable Provider Factory |
| Quest prerequisites | "Requires sword skill 5" | **Seed** -- `IPrerequisiteProviderFactory` adapter queries capability manifest |

There is no orphaned concept that requires its own service.

---

## How the Composition Works in Practice

Consider a character learning swordsmanship in Arcadia:

The character owns a `combat_archetype` seed with domains like `melee.sword`, `melee.axe`, `defense.block`. Every time the character fights with a sword, `melee.sword` depth increases. The seed type's capability rules define thresholds:

- Depth 1.0 unlocks `basic_strikes` (fidelity: 0.0 -> 1.0 over depth 1-2)
- Depth 3.0 unlocks `advanced_techniques` (fidelity: 0.0 -> 1.0 over depth 3-6)
- Depth 5.0 unlocks `master_forms` (fidelity: 0.0 -> 1.0 over depth 5-10)

Fidelity is continuous, not binary. A character with `melee.sword` depth 4.0 has `basic_strikes` at full fidelity and `advanced_techniques` at partial fidelity. The character is competent but not yet a master.

### Ability Unlocks (License)

The character's class or profession has a License board. Spending LP unlocks specific ability nodes -- "Two-Handed Grip", "Riposte", "Whirlwind Strike". These are discrete choices with costs and adjacency constraints. The player decides which path to take through the grid.

License boards are authored content (designed by game designers). Seed mastery is emergent (grows from play). A character can have high sword mastery from extensive practice without having unlocked specific technique nodes on their license board, and vice versa. The two dimensions combine: mastery determines how well you perform; unlocks determine what you can perform.

### Effects (Status)

Status aggregates both sources into a single query:

- Seed-derived capabilities: `basic_strikes` at fidelity 0.85 (passive, persistent)
- Item-based statuses: "Blessed Blade" buff (+10% damage, 5 min TTL, from divine blessing)

Any system asking "what effects does this character have?" gets a unified answer from `GetEffects`.

### Behavior (Seed -> Actor)

The character's NPC brain (running via Actor) accesses `${seed.combat_archetype.melee.sword}` in ABML behavior expressions. An NPC with high sword depth preferentially chooses sword-based combat actions. The behavior logic doesn't need a skill service -- it reads seed data through the Variable Provider Factory pattern that already exists.

### Prerequisites (Seed -> Quest)

When a quest requires "sword skill >= 5", the `skill` prerequisite provider factory checks the character's `combat_archetype` seed capability manifest. This is a thin adapter, so no dedicated skill service needed. The adapter lives wherever it makes sense (likely a future L4 consumer that registers seed types for character progression).

---

## "Magic" Is the Same Story

Arcadia's magic system is metaphysically grounded -- thermodynamic pneuma control with six-stage spellcasting. The mechanical representation maps identically:

- **Spell proficiency** -> Seed domain depths (`magic.fire`, `magic.pneuma_control`, `magic.divination`)
- **Spell unlocks** -> License board nodes or Collection entries
- **Active spell effects** -> Status effects (buffs, channeling states, cooldowns)
- **Spell prerequisites** -> `IPrerequisiteProviderFactory` adapter over Seed capabilities
- **NPC spellcasting decisions** -> `${seed.magic_discipline.*}` in ABML behavior expressions

A `magic` prerequisite provider factory works identically to the `skill` one, querying a different seed type code.

---

## Why Not Build It Anyway?

The strongest argument for a skill plugin: "Players think in terms of skills, not seeds and license boards. A skill service would present a player-friendly abstraction."

This argument confuses the player-facing API with the data model. Players don't call Bannou services directly -- they interact through game clients that present whatever UX the game designers choose. The client can display "Sword Skill: Level 5" while the backend stores `seed.combat_archetype.melee.sword.depth = 5.3`. The abstraction layer belongs in the client UX, not in a redundant backend service.

Building a skill plugin would mean:

1. **Duplicating Seed's growth model** (progressive depth with thresholds) but calling it "skill levels"
2. **Duplicating License's unlock model** (structured ability trees) but calling them "skill trees"
3. **Duplicating Status's effects model** (active buffs from abilities) but calling them "skill effects"
4. **Adding a coordination burden** (keeping skill state synchronized with seed state, license state, and status state)

Every duplicate creates a synchronization problem. When the seed's domain depth changes, the skill level must update. When a license node is unlocked, the skill's ability list must update. This coordination overhead is pure cost with no benefit -- the existing services already compose without synchronization because they are the authoritative sources, not mirrors.

---

## The Composition Is the Design

Arcadia's approach to character capabilities is deliberately decomposed:

| Primitive | Service | What It Answers |
|---|---|---|
| "How good am I at this?" | Seed | Continuous mastery via domain depth and fidelity |
| "What can I do?" | License | Discrete ability unlocks via grid-based progression |
| "What's affecting me right now?" | Status | Unified effects from items, seeds, contracts |
| "What have I discovered?" | Collection | Content catalog from gameplay experience |

This decomposition is more powerful than a monolithic skill service because the primitives compose freely. A dungeon core's "skill" progression uses the same Seed/License/Status primitives as a character's. A guardian spirit's progressive agency uses the same Seed primitives as an NPC's combat mastery. A faction's governance capabilities use Seeds. None of these require a "skill" concept -- they all use the same generic growth and unlock infrastructure.

The answer to "where is the skill plugin?" is: it's Seed + License + Status + Collection, composed. That composition is the skill system.
