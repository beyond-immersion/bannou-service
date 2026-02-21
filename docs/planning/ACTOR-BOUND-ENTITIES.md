# Actor-Bound Entities: Living Things That Grow Into Autonomous Agents

> **Status**: Vision Document (architectural analysis, no implementation)
> **Priority**: High (Living Game Worlds -- North Star #1, Content Flywheel -- North Star #2)
> **Related**: `docs/plugins/ACTOR.md`, `docs/plugins/DIVINE.md`, `docs/plugins/DUNGEON.md`, `docs/plugins/SEED.md`, `docs/plugins/COLLECTION.md`
> **Supersedes**: `docs/planning/DUNGEON-EXTENSIONS-NOTES.md` (incorporated and expanded)
> **External Inspiration**: *Tales of Destiny* (Swordians), *Xenoblade Chronicles 2* (Blades), *Persona* (bond = power), *DanMachi* (Falna/blessing system), Dungeon Core LitRPG genre

---

## Executive Summary

Bannou's actor system, seed growth, and dynamic character binding combine to enable a powerful pattern: **entities that begin as inert objects and progressively grow into autonomous agents with personalities, memories, and the full cognitive stack**. This document unifies two implementations of this pattern -- **dungeon cores** and **living weapons** -- and demonstrates that they are structurally identical at the infrastructure level, differing only in domain-specific ceremony.

The critical insight: **living weapons require zero new Bannou services**. They compose entirely from existing primitives (Item, Seed, Collection, Actor, Character, Relationship). The game engine/SDK drives growth events; the infrastructure handles everything else. This is the strongest possible validation of Bannou's composability thesis.

### The Unified Pattern

```
Inert Object (seed phase: Dormant)
    |
    | [Growth events accumulate -- game-specific triggers]
    |
    v
Reactive Entity (seed phase: Stirring -- event brain actor spawned)
    |
    | [More growth -- actor runs ABML behavior with seed variable providers]
    |
    v
Sentient Entity (seed phase: Awakened -- character created, actor binds to it)
    |
    | [Full L2/L4 character stack activates: personality, history, encounters]
    |
    v
Living Entity (seed phase: Ancient -- rich inner life, personality evolution)
    |
    | [Content flywheel integration: archives feed future content on "death"]
```

This progression is identical for dungeon cores, living weapons, and any future entity type that follows the pattern (sentient ships, awakened forests, haunted buildings, intelligent golems).

---

## Part 1: The Pattern (Shared Infrastructure)

### What Every Actor-Bound Entity Needs

| Primitive | Service | Role |
|-----------|---------|------|
| **Identity** | Domain-specific (lib-dungeon, or no plugin for weapons) | What the entity IS -- its type, name, scoping |
| **Growth** | lib-seed | Progressive capability accumulation across named domains |
| **Knowledge** | lib-collection | Permanent experience records ("I have seen X") |
| **Behavior** | lib-actor + lib-behavior | Autonomous decision-making via ABML |
| **Character** | lib-character (system realm) | Full entity identity once sentient |
| **Personality** | lib-character-personality | Quantified traits on bipolar axes |
| **Memory** | lib-character-encounter | Memories of interactions with others |
| **History** | lib-character-history | Backstory and significant events |
| **Bond** | lib-relationship or lib-contract | Connection to a partner entity |
| **Economy** | lib-currency (optional) | Resource management if applicable |
| **Physical Form** | lib-item, lib-mapping, lib-scene (varies) | How the entity exists in the world |

### The Three Cognitive Stages

All actor-bound entities progress through the same three stages. The stages are defined by seed growth phases, and the transitions use existing infrastructure with zero new service code.

#### Stage 1: Dormant (No Actor)

The entity exists only as a seed and its physical form (a dungeon location, an item instance). No actor is running. Responses to stimuli are pre-scripted or handled by Workshop (automated production). Growth accumulates passively from game events.

**System cost**: Near zero. No actor runtime resources. A world can have thousands of dormant seeds.

**Growth driver**: Game-engine-specific events reported via existing APIs (Collection grants, Seed growth recording).

#### Stage 2: Event Brain Actor (No Character)

When the seed reaches the Stirring phase, an actor is spawned via Puppetmaster as an event brain (no character binding). The entity can now perceive, decide, and act autonomously via ABML behavior documents, but without the rich cognitive data that comes from having a character identity.

ABML expressions like `${personality.*}` resolve to null -- the behavior falls through to instinct-driven default paths. The entity has preferences (from seed metadata) but not a rich inner life.

**Trigger**: `ISeedEvolutionListener` fires in the owning plugin (or any registered listener) when the seed reaches the Stirring phase. The listener grants a Collection entry (`has_behavior`), which can trigger actor spawn via `ICollectionUnlockListener` or direct Puppetmaster API call.

**Variable providers active**: `${seed.*}` (growth domains, capabilities), domain-specific volatile state.

#### Stage 3: Character Brain Actor (Full Cognitive Stack)

When the seed reaches the Awakened phase, the entity has accumulated enough complexity to warrant a full character identity. The owning system:

1. Creates a **Character record** in a system realm (`isSystemType: true`)
2. Seeds personality traits from the entity's type/configuration
3. Calls `/actor/bind-character` to bind the running actor to the new character -- **no actor relaunch needed**
4. Variable providers activate on the next behavior tick

After binding, the entity has the full L2/L4 character entity stack -- personality traits, encounter memories, backstory, quest data, obligation awareness, and worldstate context. The ABML behavior document is the same one used in Stage 2; the difference is that expressions like `${personality.patience}` now return real values instead of null.

**This is the same pattern used by Divine actors** (gods bind to divine system realm characters) and is enabled by Actor's `BindCharacterAsync` API (implemented 2026-02-16).

### System Realms: Conceptual Namespaces for Non-Physical Entities

Each category of actor-bound entity gets its own system realm, following the pattern established for divine characters:

| System Realm | Purpose | Character Species |
|---|---|---|
| **PANTHEON** | Divine characters (gods) | Per-deity: "Fate Weaver", "War God", etc. |
| **DUNGEON_CORES** | Dungeon characters | Per-personality-type: "Martial Core", "Memorial Core", etc. |
| **SENTIENT_ARMS** | Living weapon characters | Per-weapon-type or lineage: "Flame Blade", "Storm Bow", etc. |
| **UNDERWORLD** | Dead characters (afterlife) | Transferred from physical realms on death |
| **NEXIUS** | Guardian spirits | Player metaphysical entities |

System realms are seeded via `/realm/seed` with `isSystemType: true` -- configuration, not code. Services that list characters in physical realms naturally exclude system realm characters without code changes.

### The Bond Pattern

All actor-bound entities have a bond with a partner. The bond mechanism varies by domain but serves the same function: asymmetric partnership between two entities.

| Entity Type | Bond Mechanism | Partner | Agency Pattern |
|-------------|---------------|---------|----------------|
| **Divine actor** | Relationship (deity-follower) | Characters (many) | God observes many, influences indirectly |
| **Dungeon core** | Contract (master bond) | Character or account (one) | Core and master grow paired seeds |
| **Living weapon** | Relationship (wielder bond) + Item equip state | Character (one) | Weapon whispers to wielder's actor |

The bond communication pattern is identical in all cases: the entity's actor sends perceptions to the partner's character actor via the Actor perception injection API. The partner's ABML behavior processes these perceptions through its cognition pipeline. The entity influences; it does not control.

### Content Flywheel Integration

When an actor-bound entity is destroyed or goes permanently dormant, its accumulated data feeds the content flywheel:

1. Character history/personality/encounters from the system realm character
2. Encounter records (who interacted with it, what happened)
3. All of this feeds lib-resource compression and archival
4. Regional watcher gods evaluate archives via Storyline
5. Future entities, quests, or lore reference the dead entity's legacy

A legendary living weapon that was shattered in battle becomes a story seed. A destroyed dungeon's memories manifest as haunted ruins. The content flywheel treats all actor-bound entities the same.

---

## Part 2: Dungeon Cores (The Spatial Implementation)

> **Full specification**: `docs/plugins/DUNGEON.md`
> **Discussion notes**: `docs/planning/DUNGEON-EXTENSIONS-NOTES.md` (preserved for historical context)

Dungeon cores are the first and most complex implementation of the actor-bound entity pattern. They add spatial domain management, creature production, a mana economy, and a master bond system with two mastery patterns (full split vs. bonded role).

### What Dungeon Cores Add Beyond the Base Pattern

| Concern | How It Extends the Pattern |
|---------|---------------------------|
| **Spatial domain** | Rooms, corridors, floors as Location hierarchy in a system realm. Transit connections. Mapping for spatial data. Scene for visual composition. |
| **Mana economy** | Currency wallet for mana. Spend mana to spawn creatures, activate traps, shift layout. Earn mana from deaths within domain, leyline proximity. |
| **Creature production** | Two tracks: pneuma echoes (instant, mana-cost, dumb) and habitat creatures (Workshop-produced, grow over game time, smarter). |
| **Dual mastery patterns** | Pattern A (account-level, dungeon IS the garden) and Pattern B (character-level, dungeon layers onto gameplay). Governed by household split mechanic. |
| **Physical construction** | Cross-service: Save-Load for persistence, Mapping for spatial index, Scene for visual, Procedural for generated geometry. |
| **Floor system** | Each floor is a Location with its own Environment configuration. Strategic environment selection (desert for resource exhaustion, jungle for ambush concealment). Floor creation gated by having an active actor. |

### Dungeon Seed Growth Domains

| Domain | Purpose |
|--------|---------|
| `mana_reserves` | Accumulated mana capacity |
| `genetic_library.{species}` | Logos completion per species for spawn quality |
| `trap_complexity` | Trap design sophistication |
| `domain_expansion` | Spatial control capability |
| `memory_depth` | Memory accumulation and manifestation quality |

### The Dungeon Memory Dual-System

An improvement over the original DUNGEON.md custom memory MySQL store:

**Collection** (permanent knowledge, "logos"):
- First combat victory, first boss kill, first adventurer death, etc.
- Cannot be removed -- permanent record of what the dungeon has experienced
- Feeds Seed growth to `memory_depth.capture`
- The "set" of experiences the dungeon has had (analogous to logos completion)

**Inventory** (consumable creative resources, "inspiration"):
- Every notable event creates a "memory item" in the dungeon's memory inventory
- Items have custom stats: significance score, event type, participants, emotional context
- Consumable -- when the dungeon manifests something, it spends the memory item
- Stacking for similar event types (5x "combat victory" memory items)
- Inventory capacity scales with `memory_depth` seed growth
- The dungeon can only create as many unique manifestations as it has memory items
- Memory items could even be traded between dungeons (logos trading)

When consumed, memory items produce: unique loot items (via lib-item), paintings/murals (via lib-scene), environmental echoes (via lib-mapping/environment), or phantom replays (inhabitant spawn, special type).

### Workshop Integration for Habitat Creatures

Workshop (L4) provides time-based automated production with lazy evaluation. For dungeons:

| Workshop Concept | Dungeon Equivalent |
|------------------|--------------------|
| Blueprint | Creature spawning template (species + quality tier) |
| Workers | Mana channels (abstract slots, scaled by `mana_reserves` seed growth) |
| Source inventory | Genetic library (logos seeds) |
| Output inventory | Room-specific inhabitant containers |
| Production rate | Modified by `genetic_library.{species}` seed growth |

Two creature tracks:
- **Pneuma echoes**: Direct spawn via ABML `spawn_monster:` action. Instant. Costs mana. Dies and disperses. The current DUNGEON.md inhabitant system.
- **Habitat creatures**: Workshop blueprints assigned to a floor's environment. Eggs/babies grow over game time via Workshop's lazy evaluation. Once mature, potentially their own simple actors. Cheaper in mana but takes real game-time.

Workshop is a soft L4 dependency. Without it, dungeons can only spawn pneuma echoes. The adapter pattern maps dungeon concepts to Workshop's API without modifying Workshop's schema.

### The Dungeon-Spawning God

A regional watcher deity (Typhon/Monsters, or a god of Transformation/Stagnation-Breaking) monitors mana accumulation via Environment/Worldstate events:

```yaml
# In the spawning god's ABML behavior document
perception_filter:
  - event: "environment.mana_density.threshold"
    conditions:
      - check: "${event.density} > ${divine.stagnation_threshold}"
      - check: "${event.duration_days} > ${divine.minimum_stagnation_days}"
```

The god evaluates location characteristics, decides what kind of dungeon to create based on formative conditions (battlefield pneuma = martial dungeon, mass grief = memorial dungeon), and calls `/dungeon/create` with personality type derived from the formative event. No additional service design needed -- this is pure ABML behavior authoring.

Vault level classification (1-10 based on pneuma density) maps to the `dungeon_core` seed's initial growth -- higher-level formations start with more initial growth in `mana_reserves`.

---

## Part 3: Living Weapons (The Item-Bound Implementation)

### The Thesis: Zero New Services

Living weapons require **no new Bannou plugin**. They compose entirely from existing primitives:

| Primitive | Service | How Weapons Use It |
|-----------|---------|-------------------|
| Physical form | **lib-item** | The weapon IS an item instance (template + instance with seed reference) |
| Growth | **lib-seed** | `sentient_weapon` seed type with growth domains |
| Knowledge | **lib-collection** | Permanent experience records ("first blood", "slayer of X", "survived Y") |
| Behavior | **lib-actor** | At Stirring phase, actor spawned; runs weapon-specific ABML |
| Character | **lib-character** | At Awakened phase, character created in SENTIENT_ARMS system realm |
| Personality | **lib-character-personality** | Quantified traits: `aggression`, `loyalty`, `pride`, `curiosity` |
| Memory | **lib-character-encounter** | Memories of wielders, significant battles, enemies |
| History | **lib-character-history** | Forging origin, previous wielders, legendary deeds |
| Bond | **lib-relationship** | Wielder bond (weapon-character relationship type) |
| Combat effects | **lib-status** (optional) | Buffs/effects the weapon grants its wielder based on seed capabilities |

The game engine / SDK drives the growth events. The services handle everything else.

### Why No Plugin?

lib-dungeon exists because dungeons have **domain-specific APIs** that need orchestration: spawn monster, activate trap, seal passage, shift layout, manifest memory, form bond, dissolve bond, manage inhabitants. These are dungeon-specific operations that compose multiple service calls atomically.

Living weapons have **no domain-specific operations** beyond what existing services already provide:
- Creating the weapon: `POST /item/instance/create` (with seed reference in metadata)
- Growing: `POST /seed/record-growth` and `POST /collection/grant` (called by game engine)
- Spawning actor: `POST /actor/spawn` via Puppetmaster (triggered by seed phase listener)
- Creating character: `POST /character/create` (triggered by seed phase listener)
- Binding actor: `POST /actor/bind-character` (triggered after character creation)
- Equipping: standard item/inventory operations
- Communicating with wielder: Actor perception injection (standard ABML `emit_perception:`)
- Granting effects: `POST /status/grant` based on seed capabilities

Every operation is a single existing API call or a standard ABML action. There is no multi-service orchestration that needs atomicity guarantees beyond what the game engine can provide.

**The game engine is the orchestrator.** The SDK coordinates the calls. If a future version needs server-side orchestration (e.g., automated combat effects triggered by weapon perception of battle events), a thin L4 service could be added later -- but the core pattern works without one.

### The Growth Model

#### sentient_weapon Seed Type

| Property | Value |
|----------|-------|
| **SeedTypeCode** | `sentient_weapon` |
| **DisplayName** | Sentient Weapon |
| **MaxPerOwner** | 1 (one seed per item instance) |
| **AllowedOwnerTypes** | `["item"]` (the seed is owned by the item instance) |
| **BondCardinality** | 0 (Relationship handles the wielder bond) |

**Growth Phases** (cognitive stages map 1:1 to the unified pattern):

| Phase | MinTotalGrowth | Cognitive Stage | Behavior |
|-------|---------------|-----------------|----------|
| **Dormant** | 0.0 | Stage 1: No actor | Inert weapon. Has a seed but no autonomous behavior. Growth accumulates from game events. May have passive effects (seed capability-gated stat bonuses applied by the game engine). |
| **Stirring** | 15.0 | Stage 2: Event brain actor | Actor spawned. The weapon "wakes up." Can perceive combat events, form impressions. Communicates with wielder as vague impulses (emotion-only perception injection). No character identity. |
| **Awakened** | 75.0 | Stage 3: Character brain actor | Character created in SENTIENT_ARMS system realm. Actor binds. Full personality, memories, history. The weapon speaks, advises, develops opinions. Rich inner life. |
| **Legendary** | 300.0 | Stage 3 (mature) | Deep personality evolution. The weapon has lived through many wielders, many battles. Rich backstory. Personality traits shift from accumulated experience. A unique living entity with centuries of memory. |

**Growth Domains**:

| Domain | Purpose | Example Growth Events |
|--------|---------|----------------------|
| `combat_experience` | Battle participation and victory | Kills, critical hits, boss victories, near-death survivals |
| `wielder_bond` | Relationship depth with current wielder | Time equipped, shared dangers, alignment of purpose |
| `elemental_mastery` | Affinity with the weapon's element/domain | Elemental kills, exposure to aligned environments |
| `legend` | Narrative significance and renown | Participating in historically significant events, famous victories |
| `awakening` | Consciousness development | First sentient moment, first communication, first refusal |

**Capability Rules** (examples):

| Capability Code | Domain | Threshold | Description |
|----------------|--------|-----------|-------------|
| `passive.stat_bonus` | `combat_experience` | 1.0 | Basic stat enhancement to wielder |
| `passive.elemental` | `elemental_mastery` | 3.0 | Elemental damage/resistance bonus |
| `active.impulse` | `awakening` | 5.0 | Vague emotional impulses to wielder (danger sense) |
| `active.speak` | `awakening` | 15.0 | Can communicate as words, not just feelings |
| `active.advise` | `combat_experience` | 25.0 | Tactical suggestions during combat |
| `active.refuse` | `wielder_bond` | 50.0 | Can refuse actions that violate its nature |
| `active.protect` | `wielder_bond` | 75.0 | Autonomous defensive actions |
| `resonance.burst` | `legend` | 100.0 | Combined wielder-weapon ultimate technique |

### The Wielder Bond

Unlike dungeon bonds (Contract-based with explicit Pattern A/B), living weapon bonds are simpler -- closer to the divine follower pattern:

1. **Equip the weapon**: Standard item/inventory operation. This is the physical prerequisite.
2. **Create wielder relationship**: `POST /relationship/create` with type `weapon_wielder`. The weapon's actor (if active) perceives the new wielder.
3. **Compatibility evaluation**: The weapon's ABML behavior evaluates the wielder over time. `${personality.compatibility}` against the wielder's character data (loaded via `load_snapshot:`). Early in the bond, the weapon may be neutral or resistant.
4. **Bond deepens**: The `wielder_bond` growth domain accumulates. Higher bond growth unlocks more capabilities. The weapon's personality shifts based on the wielder's actions (via CharacterPersonality's experience-driven trait evolution).
5. **Bond breaks**: When the wielder unequips the weapon or dies. The weapon remembers the previous wielder via CharacterEncounter records. A new wielder must build trust from scratch -- but the weapon's accumulated growth and personality persist.

**Design principle from Tales of Destiny**: The weapon chooses, not the wielder. A weapon with high `pride` may refuse an unworthy wielder (via the `active.refuse` capability). The weapon's ABML behavior document encodes compatibility criteria. This creates emergent scarcity -- even if you find a sentient weapon, it might not accept you.

### How Living Weapons Differ from Swordians (and Why)

The Tales of Destiny analysis reveals a key design gap: **Swordians have deep narrative personality but zero mechanical expression of that personality**. The weapon never refuses a command, never fights differently based on mood, never mechanically rewards relationship depth. Bannou's architecture closes this gap:

| Swordian Weakness | Bannou Solution |
|-------------------|-----------------|
| Personality is narrative-only | Actor + CharacterPersonality = personality that mechanically affects decisions |
| Bond depth is not mechanized | Seed `wielder_bond` domain = quantified bond that unlocks capabilities |
| No independent growth | Seed growth is autonomous (accumulates from game events) |
| Weapon never refuses | ABML behavior with `active.refuse` capability gated by personality traits |
| Power is wielder-driven only | Symbiotic: weapon seed grows independently AND influences wielder via status effects |
| Fixed six weapons, no evolution | Personality evolution via CharacterPersonality's experience-driven trait shifts |
| No memory of past wielders | CharacterEncounter records persist across wielder changes |

**What Bannou achieves that no existing game has**: The "ideal living weapon" that combines Tales of Destiny's narrative presence, Xenoblade 2's affinity mechanics, and Persona's "bond = power" progression -- all emerging naturally from composing existing services.

### Scarcity and Significance

Living weapons should be genuinely rare. Design principles from Tales of Destiny:

- **Fixed scarcity with narrative weight**: Only a handful exist at any time. Not a loot drop.
- **Creation is significant**: A god, a cataclysmic event, or centuries of accumulated pneuma at a location. The spawning pattern is the same as dungeons: a deity (or other regional watcher) detects the conditions and orchestrates the creation.
- **Loss has weight**: When a sentient weapon is destroyed, its archive feeds the content flywheel. Future weapons, quests, or NPC memories reference the dead weapon's legacy.
- **No replacements**: You don't find a "better" sentient weapon. You deepen your relationship with the one you have.
- **The weapon outlives wielders**: A Legendary weapon has had many wielders across generations. Its CharacterEncounter records span centuries. Each new wielder inherits the weapon's accumulated personality and memories.

Scarcity is enforced by the game engine / ABML behaviors of creating gods -- not by service-level constraints. The service infrastructure is agnostic to how many sentient weapons exist.

### The Living Weapon Actor's Variable Providers

When a living weapon reaches Stage 3 (character brain), it gets all standard character variable providers plus weapon-specific ones:

```
Weapon-actor (character brain, bound to SENTIENT_ARMS system realm character)
+-- ${personality.*}     <- CharacterPersonality (the weapon's quantified personality)
|   e.g., ${personality.loyalty} for wielder attachment
|   e.g., ${personality.aggression} for combat eagerness
+-- ${encounters.*}      <- CharacterEncounter (memories of wielders and battles)
|   e.g., ${encounters.sentiment.current_wielder} for relationship state
+-- ${backstory.*}       <- CharacterHistory (forging origin, legendary deeds)
+-- ${world.*}           <- Worldstate (time context)
+-- ${seed.*}            <- SentientWeaponSeedVariableProviderFactory (growth domains)
+-- ${wielder.*}         <- WielderVariableProviderFactory (current wielder data)
|   e.g., ${wielder.health_percent} for protection triggers
|   e.g., ${wielder.in_combat} for combat mode activation
+-- ...can use load_snapshot: for ad-hoc data about nearby entities
```

The weapon-specific variable providers (`${seed.*}` and `${wielder.*}`) follow the standard `IVariableProviderFactory` DI pattern. They do NOT require a new plugin -- they can be registered from any plugin, or even from the game-specific SDK layer.

### The Communication Channel

A sentient weapon communicates with its wielder the same way gods and dungeon cores do: via the wielder's character Actor perception pipeline.

```
Weapon Actor (character brain, SENTIENT_ARMS system realm)
    |
    | [ABML behavior decides to communicate]
    | emit_perception:
    |   target: ${wielder.character_id}
    |   topic: character.{wielder_id}.perceptions
    |   type: weapon_whisper
    |   urgency: 0.6
    |   data:
    |     message: "danger_ahead"
    |     intensity: 0.8
    |
    v
Wielder's Character Actor (character brain, physical realm)
    |
    | [Perception enters bounded queue]
    | [Cognition pipeline processes it]
    | [Wielder's ABML behavior reacts]
    |
    v
Wielder's behavior: "My weapon senses danger. I should be cautious."
```

The communication fidelity is gated by the weapon's `awakening` growth domain:
- Below `active.impulse` threshold: no communication at all
- `active.impulse`: vague emotional data (urgency, valence)
- `active.speak`: structured messages (type, content)
- `active.advise`: tactical recommendations with context

The wielder's ABML behavior decides how much to trust the weapon's input -- based on `wielder_bond` data in the wielder's own variable providers (if an obligation or disposition variable provider tracks weapon trust).

### Example: The Life of a Sentient Weapon

```
1. A divine smith-god (actor, regional watcher) detects that a master
   blacksmith has been forging a blade for 40 game-years with
   extraordinary devotion. The god-actor's ABML behavior triggers:

   - POST /item/instance/create (the weapon item, flagged as "awakening-capable")
   - POST /seed/create (sentient_weapon seed, owned by item instance)
   - POST /seed/record-growth (initial growth from the 40-year forging devotion)

   The weapon is Dormant. It exists as an exceptionally fine sword with
   passive stat bonuses (seed capability-gated by the game engine).

2. Over years of combat use, the game engine reports events:
   - 100 kills: POST /collection/grant (first_hundred_kills)
   - Boss victory: POST /collection/grant (boss_slayer) + POST /seed/record-growth
   - Near-death survival: POST /seed/record-growth (combat_experience, wielder_bond)

   The ICollectionUnlockListener -> ISeedEvolutionListener pipeline fires.
   The seed grows. Growth crosses the Stirring threshold (15.0).

3. Stirring phase reached. ISeedEvolutionListener fires:
   - Puppetmaster spawns an event brain actor for the weapon
   - The weapon "wakes up" -- begins perceiving combat events
   - Its ABML behavior is simple: form impressions, send impulses
   - The wielder's Actor receives vague "danger sense" perceptions

4. Continued growth. The weapon develops preferences through its ABML
   behavior -- it has "feelings" stored in actor state (joy from victory,
   anger from dishonor, satisfaction from protecting). These emerge from
   ABML behavior execution, not from any personality service yet.

5. Awakened phase reached (75.0). The event handler:
   - Creates Character in SENTIENT_ARMS system realm
   - Seeds personality traits from accumulated actor feelings
   - Calls POST /actor/bind-character
   - The weapon is now a character brain with full variable providers

   It SPEAKS. It has opinions. It remembers every battle.
   ${personality.loyalty} is 0.9 because of long service.
   ${encounters.sentiment.current_wielder} is deeply positive.

6. The wielder dies in battle. The weapon's CharacterEncounter records
   preserve the memory. The wielder_bond domain resets for a new
   wielder, but combat_experience, elemental_mastery, and legend persist.
   The weapon grieves (personality shift via experience-driven evolution).

7. A new wielder picks up the weapon. The weapon evaluates compatibility
   via ABML. ${personality.loyalty} to the dead wielder is high -- the
   weapon is resistant to the newcomer. The new wielder must prove
   themselves. wielder_bond grows slowly.

8. Centuries later, the weapon is Legendary (300.0+). It has had seven
   wielders. It has participated in three wars. It has killed a god's
   avatar. Its personality has evolved across centuries of experience.
   Its CharacterHistory contains a rich backstory spanning generations.
   It is, in every meaningful sense, a person trapped in a sword.

9. When the weapon is finally destroyed (shattered in the climactic
   battle of a content flywheel narrative arc), its character is
   compressed via lib-resource. The archive feeds:
   - Storyline generates quests about "the lost blade"
   - Future blacksmiths attempt to reforge it (guided by archive data)
   - NPCs who wielded it have memories that persist
   - Dungeon cores that witnessed battles involving it manifest memories
```

---

## Part 4: Comparative Architecture

### The Structural Isomorphism

| Aspect | Divine Actor | Dungeon Core | Living Weapon |
|--------|-------------|-------------|---------------|
| **Plugin** | lib-divine (L4) | lib-dungeon (L4) | None (composed from primitives) |
| **Physical form** | Immaterial observer | Spatial domain (rooms, floors) | Item instance |
| **System realm** | PANTHEON | DUNGEON_CORES | SENTIENT_ARMS |
| **Seed type** | `deity_domain` | `dungeon_core` | `sentient_weapon` |
| **Economy** | Divinity currency | Mana currency | None (or optional) |
| **Bond type** | Relationship (followers) | Contract (master) | Relationship (wielder) |
| **Bond cardinality** | Many followers | One master | One wielder |
| **Cognitive stages** | Event brain -> character brain | Dormant -> event brain -> character brain | Dormant -> event brain -> character brain |
| **Content flywheel** | Archive on deactivation | Archive on destruction | Archive on destruction |
| **Garden integration** | Tends realms AND player gardens | Pattern A: IS the garden. Pattern B: layers onto garden. | No garden (influences through wielder's actor) |
| **Workshop integration** | None | Habitat creature production | None |
| **Spawning agent** | Puppetmaster (admin/startup) | Spawning god ABML behavior | Smith-god or cataclysmic event ABML behavior |
| **Scarcity** | 18 Old Gods (Arcadia-specific) | Tied to mana stagnation events | Extremely rare (handful per world) |
| **Personality expression** | Blessings, interventions, aesthetic preferences | Trap placement, creature choice, layout design | Combat advice, refusal, protection, resonance |
| **Master communication** | Indirect (through character actor) | Perception injection (gated by master seed) | Perception injection (gated by awakening domain) |

### What Each Adds Beyond the Base Pattern

**Divine actors add**: Divinity economy, blessing orchestration, follower management, attention slots, domain contests, avatar manifestation. These are complex enough to warrant a dedicated L4 plugin.

**Dungeon cores add**: Spatial domain management, mana economy, creature production (pneuma + habitat), master bond with dual patterns (A/B), floor system, trap/layout manipulation, memory inventory system. These are complex enough to warrant a dedicated L4 plugin.

**Living weapons add**: Nothing that requires a new plugin. The game engine orchestrates existing API calls. The weapon's behavior is ABML. The bond is a Relationship. The effects are Status grants. The growth is Seed. The knowledge is Collection. The identity is Character. All existing.

### When Would a Living Weapon Plugin Become Necessary?

If future requirements introduce operations that need **atomic multi-service orchestration** on the server side:

- Weapon-to-weapon duels (two sentient weapons fighting through their wielders, coordinated by a shared event brain)
- Weapon merging/fusion (consuming one weapon to enhance another -- requires atomic item + seed + character operations)
- Weapon curse/corruption mechanics (a weapon slowly corrupting its wielder -- requires coordinated status + personality + seed mutations)
- Weapon economy (sentient weapons as economic actors -- buying materials, commissioning repairs)

Until then, the game engine + ABML behaviors are sufficient orchestrators.

---

## Part 5: Design Considerations

### Already Resolved (by existing architecture)

| Concern | Resolution |
|---------|------------|
| Event brain -> character brain transition | Actor `BindCharacterAsync` API (2026-02-16). No actor relaunch needed. |
| System realm provisioning | `/realm/seed` with `isSystemType: true`. Configuration, not code. |
| Variable provider activation on binding | Standard DI-discovered `IVariableProviderFactory`. Providers detect `characterId` on next tick. |
| ABML behavior working with both modes | Null-safe `${personality.*}` expressions fall through to defaults before binding. |
| Content flywheel integration | Standard lib-resource compression pipeline for system realm characters. |
| Scarcity enforcement | Game engine / ABML behavior decision, not service-level constraint. |

### Needs Design Work

1. **Workshop adapter for dungeon habitat creatures**: Mapping Workshop's worker/blueprint/inventory concepts to dungeon's mana channels/genetic library/rooms. Adapter pattern preferred (no Workshop schema changes). See DUNGEON-EXTENSIONS-NOTES.md for details.

2. **Dual memory system for dungeons (Collection + Inventory)**: Replacing DUNGEON.md's custom memory MySQL store with Collection for permanent knowledge and Inventory for consumable creative resources. Design needed for memory item templates, significance scoring, and manifestation consumption.

3. **Household split mechanic (cross-cutting, dungeon Pattern A)**: General household fragmentation involving Contract, Faction, Obligation, Relationship, Seed, and Organization. Not dungeon-specific but required for Pattern A seed promotion. Tracked at [#436](https://github.com/beyond-immersion/bannou-service/issues/436).

4. **Seed promotion mechanic (character-owned to account-owned)**: Required for dungeon Pattern A. Tracked at [#437](https://github.com/beyond-immersion/bannou-service/issues/437). Not relevant to living weapons.

5. **Seed-to-item ownership type**: lib-seed currently supports `AllowedOwnerTypes` of `account`, `character`, and `actor`. Living weapon seeds need `item` as an owner type. This is a schema change to lib-seed (`AllowedOwnerTypes` enum addition) and a minor code change to seed validation. Alternatively, the weapon's seed could be actor-owned once the actor spawns, with a pre-actor ownership convention (game-engine-tracked).

6. **Weapon-wielder variable provider registration**: The `${wielder.*}` and `${seed.*}` providers for living weapons need to be registered somewhere. Options:
   - Register from a game-specific plugin (cleanest separation)
   - Register from lib-character-personality or lib-actor (if generic enough)
   - Register from a minimal `lib-sentient-weapon` that contains ONLY the variable provider factories (no API endpoints, no state stores -- just DI registrations)

7. **Cross-generational weapon memory**: When a wielder dies, the weapon's `wielder_bond` growth domain should partially reset (new wielder, new relationship) but the weapon's character encounter records persist. The weapon should gain backstory elements recording the dead wielder. This is all standard CharacterEncounter + CharacterHistory API calls, but the game engine needs to know when to make them (on wielder death event). If there's no lib-sentient-weapon, this coordination lives in the game engine.

8. **Dungeon-to-dungeon communication**: Mega-dungeon coordination and logos trading require actor-to-actor communication via Puppetmaster's `actor_command:` and `actor_query:` ABML actions. Blocked on [#388](https://github.com/beyond-immersion/bannou-service/issues/388) (watcher-actor integration).

### Explicitly Not Needed

| Suggestion | Why Not |
|------------|---------|
| lib-living-weapon plugin | No domain-specific operations requiring server-side orchestration |
| New variable provider interface | Standard `IVariableProviderFactory` works |
| New event types | Standard seed/collection/actor events cover everything |
| New state stores | Weapon data lives in Item (physical), Seed (growth), Character (identity) |
| New bond mechanism | Relationship type `weapon_wielder` + item equip state |
| Weapon-specific cognition template | `creature_base` (same as dungeon) or game-specific template via config |

---

## Part 6: Future Actor-Bound Entity Types

The pattern is general. Any entity that should progressively awaken into an autonomous agent can follow it:

| Entity Type | Physical Form | System Realm | Seed Type | Bond Pattern | Likely Plugin? |
|-------------|--------------|-------------|-----------|-------------|---------------|
| **Sentient ship** | Item (vehicle) | SENTIENT_VESSELS | `sentient_vessel` | Captain (Relationship) | Maybe (navigation orchestration) |
| **Awakened forest** | Location (region) | NATURE_SPIRITS | `nature_spirit` | Druid (Relationship) | Maybe (ecosystem management) |
| **Haunted building** | Location (structure) | RESTLESS_DEAD | `haunted_site` | Ghost (Character archive, not living) | No (composed from primitives) |
| **Intelligent golem** | Item + Actor | CONSTRUCTS | `sentient_construct` | Creator (Relationship) | No (same as living weapon) |
| **Familiar spirit** | Actor-only | FAMILIARS | `familiar_bond` | Summoner (Relationship) | No (pure actor pattern) |

The key decision for each: does it need **domain-specific multi-service orchestration** that can't be game-engine-coordinated? If yes, it warrants a plugin. If no, it composes from primitives.

---

## Appendix: Tales of Destiny Swordian Analysis (Design Principles Extracted)

### What Makes Living Weapons Work as a Game System

The Tales of Destiny Swordian system (1997, PS1; 2006, PS2 remake) is the definitive implementation of living weapons in JRPGs. Six sentient swords containing transplanted consciousness of ancient heroes, each with distinct personality, opinions, and relationships.

**Design principles worth adopting**:

1. **Fixed scarcity with narrative weight**: Exactly six Swordians. No copies, no replacements. Each is unique and irreplaceable. Loss has genuine emotional impact because you are losing a person, not a sword.

2. **Weapon as character**: Swordians participate in dialogue, argue with each other, have prior relationships (Dymlos and Atwight had a romantic history). The weapon IS a party member in the narrative sense.

3. **Mutual selection**: Swordians choose their wielders. Compatibility is character-based, not stat-based. This creates emergent scarcity -- even if you find one, it might not accept you.

4. **Distinct relationship dynamics per pairing**: Stahn/Dymlos (student/teacher), Rutee/Atwight (pragmatist/conscience), Leon/Chaltier (codependent tragedy). Each bond has unique flavor.

5. **Weapon identity replaces equipment treadmill**: You don't find "better swords." You deepen your relationship with the one you have. The PS2 remake's SD system (grid-based skill board per Swordian) is the closest to mechanized progression.

**Design gaps to address (Swordian weaknesses)**:

1. **No mechanical expression of personality**: The weapon never mechanically refuses, never fights differently based on mood, never rewards relationship depth with unique capabilities. Personality exists only in cutscenes.

2. **No autonomous growth**: The Swordian grows because the player allocates points, not because the relationship deepens or the weapon accumulates experience independently.

3. **No memory across wielders**: Swordians remember their ancient masters but the game doesn't mechanize this. There's no system where the weapon's history with past wielders affects future relationships.

4. **No bond = power mechanization**: The Blast Caliber system (ultimate attacks requiring both wielder and Swordian development) is the only hint of "bond depth unlocks power," and it's threshold-based, not continuous.

**Comparative systems**: Xenoblade 2's Blade affinity charts mechanize the bond far better but lose narrative intimacy (most Blades are gacha drops). Persona's Social Links perfectly mechanize "bond = power" but apply to relationships, not weapons. No game has achieved the synthesis of Swordian narrative depth + Xenoblade mechanics + Persona power scaling. Bannou's architecture enables it.

---

*This document is a vision/planning document. No schema, generated code, or implementation exists for the patterns described. The dungeon core specification is detailed in `docs/plugins/DUNGEON.md`. Living weapon implementation is entirely game-engine-driven using existing Bannou APIs.*
