# Dungeon Plugin Deep Dive

> **Plugin**: lib-dungeon
> **Schema**: schemas/dungeon-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: dungeon-cores (MySQL), dungeon-bonds (MySQL), dungeon-inhabitants (Redis), dungeon-memories (MySQL), dungeon-cache (Redis), dungeon-lock (Redis)
> **Status**: Pre-implementation (architectural specification)

## Overview

Dungeon lifecycle orchestration service (L4 GameFeatures) for living dungeon entities that perceive, grow, and act autonomously within the Bannou actor system. A thin orchestration layer (like Divine over Currency/Seed/Collection, Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver dungeon-as-actor game mechanics. Game-agnostic: dungeon personality types, creature species, and narrative manifestation styles are configured through ABML behaviors and seed type definitions at deployment time. Internal-only, never internet-facing.

---

## Why Not lib-divine? (Architectural Rationale)

Dungeon cores and divine actors share the same structural pattern. Both are event brain actors launched via Puppetmaster, backed by seeds, with currency economies and bonded relationships. The question arises: should dungeon cores simply be a *type* of deity in lib-divine?

**The answer is no -- same pattern, different ceremony.**

| Concern | lib-divine Ceremony | lib-dungeon Ceremony |
|---------|---------------------|----------------------|
| **Identity** | Deity with domains, personality traits | Dungeon core with personality type, core location, domain radius |
| **Economy** | Divinity (earned from mortal actions in domain) | Mana (harvested from deaths, ambient leyline proximity) |
| **Bonds** | Followers (many characters per deity) via Relationship | Master (one entity per dungeon) via Contract |
| **Effects on others** | Blessings granted via Status/Collection | Monster spawning, trap activation, layout shifting, memory manifestation |
| **Growth** | `deity_domain` seed (domain influence) | `dungeon_core` seed (mana, genetics, traps, expansion, memory) |
| **Garden** | Tends player discovery/lobby/in-game spaces | Tends dungeon space for the bonded master |
| **Physical presence** | None (gods are immaterial observers) | Yes -- rooms, corridors, traps, inhabitants, manifested memories |

lib-divine's APIs (blessings, follower management, attention slots, divinity generation) don't map to dungeon mechanics. Dungeon-specific APIs (spawn monster, activate trap, seal passage, shift layout, manifest memory) have no analogue in the divine service. Forcing both into one service would bloat lib-divine with dungeon mechanics or require the dungeon to shoehorn its operations into blessing/follower semantics.

**What they share is infrastructure, not API surface**:
- Both launch actors via Puppetmaster (event brain type)
- Both use dynamic character binding to transition from event brain to character brain as they develop (gods bind to divine system realm characters, dungeons bind to dungeon system realm characters)
- Both use Seed for progressive growth
- Both use Currency for their economy
- Both use Gardener for tending conceptual spaces
- Both influence characters indirectly through the character's Actor

This shared infrastructure is already factored into L0/L1/L2 services. lib-divine and lib-dungeon are both L4 orchestration layers that compose these primitives differently. The dynamic character binding pattern (start as event brain, create character profile, bind at runtime) is the same in both services -- the dungeon cognitive progression mirrors the divine actor lifecycle.

---

## Dungeon Cognitive Progression (via Dynamic Character Binding)

The dynamic character binding feature (Actor's `BindCharacterAsync` API) enables dungeons to progress through three distinct cognitive stages as their `dungeon_core` seed grows. Each stage represents a qualitative leap in the dungeon's decision-making capability, not just a quantitative increase in available actions.

### Stage 1: Dormant Seed (No Actor)

**Seed Phase**: Dormant (MinTotalGrowth: 0.0)

The dungeon exists only as a `dungeon_core` seed and a MySQL record. No actor is running. The dungeon is purely reactive -- intrusions trigger pre-scripted responses defined by the seed's growth phase, but there is no autonomous decision-making. Growth accumulates passively (ambient mana from leyline proximity, deaths within the dungeon's domain reported by other systems).

This is the cheapest state for the system: no actor runtime resources consumed, no perception subscriptions, no behavior loop ticks. A world can have thousands of dormant dungeon seeds scattered across its geography, waiting to awaken.

### Stage 2: Event Brain Actor (No Character)

**Seed Phase**: Stirring (MinTotalGrowth: 10.0) or triggered by first significant event

When the dungeon_core seed reaches the Stirring phase (or a significant event triggers activation), the dungeon service starts an actor via Puppetmaster. The actor runs as an **event brain** -- no character binding, operating with the `creature_base` cognition template. The dungeon can:

- Perceive domain events (intrusions, deaths, combat)
- Make autonomous decisions via ABML behavior documents
- Spawn monsters, activate traps, manipulate layout (gated by seed capabilities)
- Communicate with bonded masters (if any)
- Capture memories from significant events

The ABML behavior document can already reference `${personality.*}`, `${encounters.*}`, etc. -- these expressions simply resolve to null because there is no character to provide them. The behavior falls through to instinct-driven default paths. The dungeon has preferences (from its personality type stored in seed metadata) but not a rich inner life.

### Stage 3: Character Brain Actor (Full Cognitive Stack)

**Seed Phase**: Awakened (MinTotalGrowth: 50.0)

When the dungeon_core seed reaches the Awakened phase, the dungeon has accumulated enough complexity to warrant a full character identity. The dungeon service:

1. Creates a **Character record** in a dungeon system realm (`isSystemType: true`, analogous to the divine system realm for gods -- see [DIVINE.md: God Characters in System Realms](DIVINE.md#god-characters-in-system-realms))
2. Creates a dungeon species in that realm (e.g., "dungeon core", "aberrant nexus", "living labyrinth")
3. Calls `/actor/bind-character` to bind the running actor to the new character -- **no actor relaunch needed**
4. The character's personality traits are seeded from the dungeon's personality type
5. Variable providers activate on the next behavior tick

After binding, the dungeon has the full L2/L4 character entity stack:

```
Dungeon-actor (character brain, bound to dungeon system realm character)
├── ${personality.*}     ← CharacterPersonality (the dungeon's quantified personality)
│   e.g., ${personality.cruelty} for trap placement style
│   e.g., ${personality.patience} for ambush timing
├── ${encounters.*}      ← CharacterEncounter (memories of adventurer interactions)
│   e.g., ${encounters.last_hostile_days} for grudge tracking
│   e.g., ${encounters.sentiment.player_xyz} for per-adventurer feelings
├── ${backstory.*}       ← CharacterHistory (the dungeon's origin and significant events)
│   e.g., ${backstory.origin} for creation mythology
├── ${quest.*}           ← Quest (dungeon-originated quests, if applicable)
├── ${world.*}           ← Worldstate (game time, season -- affects mana generation)
├── ${obligations.*}     ← Obligation (any contracts the dungeon is bound by)
├── ${dungeon.*}         ← DungeonActorVariableProviderFactory (dungeon-specific volatile state)
├── ${seed.*}            ← DungeonCoreSeedVariableProviderFactory (growth domains, capabilities)
├── ${master.seed.*}     ← DungeonMasterSeedVariableProviderFactory (master capabilities)
├── ${master.*}          ← DungeonMasterCharacterVariableProviderFactory (master character data)
└── ...can still use load_snapshot: for ad-hoc adventurer data
```

The ABML behavior document is the same one used in Stage 2 -- no swap needed. The difference is that expressions like `${personality.cruelty}` now return real values instead of null, enabling qualitatively different decision-making. A martial dungeon with high cruelty places traps at chokepoints; a scholarly dungeon with high patience creates elaborate puzzles. The behavior document encodes both instinct paths (Stage 2, null-safe defaults) and personality-driven paths (Stage 3, rich data).

### Stage Transition Flow

```
Dungeon core created
    │
    ├── dungeon_core seed: Dormant (growth: 0.0)
    │   No actor running. Passive growth only.
    │
    │   [Seed reaches Stirring phase (growth ≥ 10.0)]
    │
    ├── dungeon.phase.changed event triggers actor spawn
    │   Actor runs as EVENT BRAIN (no character)
    │   creature_base cognition template
    │   ${personality.*} = null, instinct-driven behavior
    │
    │   [Seed reaches Awakened phase (growth ≥ 50.0)]
    │
    ├── dungeon.phase.changed handler:
    │   1. Create Character in dungeon system realm
    │   2. Seed personality traits from dungeon personality type
    │   3. POST /actor/bind-character (actorId, dungeonCharacterId)
    │   4. Actor transitions to CHARACTER BRAIN
    │   5. ${personality.*}, ${encounters.*} etc. activate
    │   6. Store characterId on DungeonCoreModel
    │
    │   [Seed reaches Ancient phase (growth ≥ 200.0)]
    │
    └── Full cognitive depth: memories, grudges, personality evolution
        Memory manifestation, event coordination, master synergy
        The dungeon is now a living entity with a rich inner life
```

### System Realm for Dungeon Characters

Dungeon characters live in a **dungeon system realm** (e.g., `DUNGEON_CORES` with `isSystemType: true`), following the same pattern established for divine characters in the divine system realm. This keeps dungeon character records separate from player/NPC characters in physical realms while giving them full access to the character entity stack.

| System Realm | Purpose | Character Species |
|---|---|---|
| **PANTHEON** | Divine characters (gods) | "Fate Weaver", "War God", etc. |
| **DUNGEON_CORES** | Dungeon characters | Personality-type-based: "Martial Core", "Memorial Core", etc. |

The system realm is seeded on startup via `/realm/seed` -- configuration, not code. A dungeon species is registered in that realm. Services that list characters in physical realms naturally exclude system realm characters without code changes.

---

## The Dual Mastery Patterns

The player controls a household -- a family, a clan, a dynasty -- not a single character. When one character bonds with a dungeon core, the question isn't just "what happens to this character?" but "what happens to the rest of the household?" The answer depends on which mastery pattern the player chooses, and that choice is governed by the same contractual/social mechanics that govern any household fragmentation.

### Pattern A: Full Split (Account-Level Dungeon Master)

The character **separates from their household** through a contractual split -- the same general mechanic that applies when branch families split off, when divorce occurs, or when any household member permanently departs. The split is governed by:

- **Contract**: Terms of the separation (asset division, ongoing obligations, territorial claims). Can be explicitly negotiated or implicitly determined by character personalities and history.
- **Faction norms**: Cultural context determines whether the split is amicable, contentious, or hostile. A culture that reveres dungeon bonds might celebrate the departure; one that values family cohesion might treat it as betrayal.
- **Obligation**: Post-split obligations modify GOAP action costs for both parties. A contentious split makes interactions between the separated character and former family carry higher moral weight.
- **Relationship**: Family bonds change type (parent-child might become estranged, formal, or hostile depending on the split character).

After the split, the player **commits one of their 3 account seed slots** to the `dungeon_master` seed. This creates a fundamentally separate experience:

- The `dungeon_master` seed is **account-owned** (the spirit's relationship to the dungeon, persisting across character death)
- Selectable from the void as an independent game alongside the guardian seed
- The dungeon IS the garden -- full UX surface, entity associations, dungeon core actor as gardener behavior
- The character is gone from the household roster (the player can no longer switch to them from the guardian seed)
- Cross-pollination between dungeon_master and guardian seeds happens at the spirit level (combat/strategy mastery feeds broader spirit growth)
- The household the character left may gain passive benefits from the split (income, political connections, trade access) depending on contract terms

**This is the PLAYER-VISION model**: "Dungeon Master: The spirit IS a dungeon. Your 'household' is your dungeon ecosystem."

### Pattern B: Bonded Role (Character-Level Dungeon Master)

The character **stays in their household** and gains a dungeon bond as a role, not an identity. No household split occurs. No account seed slot is consumed.

- The `dungeon_master` seed is **character-owned** (this character's role growth, tied to that character's lifecycle)
- The dungeon influence **layers onto gameplay while the player is actively controlling that character**
- The dungeon core actor pushes perceptions into the character's Actor (gated by the character's dungeon_master seed capabilities)
- The player gets dungeon-adjacent UX while playing this character (dungeon sight, limited commands based on mastery phase)
- **Switch to another household character** and the dungeon layer drops off -- back to normal household gameplay
- The dungeon influences the character far more than the player -- the character's Actor processes dungeon perceptions constantly (even when the player is controlling someone else), but the player only experiences the dungeon UX while connected to that character
- The guardian seed is **incidentally** affected: growth in the dungeon_master seed feeds into the guardian seed only while the player is actively connected and only at a reduced cross-pollination rate
- If the character dies, the character-owned seed follows character death rules (archived for Priest bonds, growth-halved for Paladin bonds, destroyed for Corrupted bonds)

**This is the "side gig" model**: The character has a dungeon relationship, but the player's primary experience remains household management.

### The General Household Split Mechanic

Pattern A is not dungeon-specific. It's one instance of a general mechanic that the game needs for any household fragmentation:

| Scenario | Trigger | Contract Terms | Result |
|----------|---------|---------------|--------|
| **Branch family** | Household too large, members want independence | Asset division, territorial rights, trade agreements | Branch becomes NPC-managed (player loses direct control), passive income/political benefits |
| **Divorce** | Character relationship dissolution | Property division, child custody, ongoing obligations | Characters split; personality/history/faction norms determine amicability |
| **Dungeon mastery** (Pattern A) | Character bonds with dungeon core, player commits seed slot | Departure terms, household compensation, ongoing ties | Character leaves household; player gains dungeon_master account seed |
| **Exile/banishment** | Faction norm violation, family dishonor | Punitive terms, asset forfeiture, social stigma | Forced split; contentious by definition |
| **Religious vocation** | Character joins a divine order (similar to dungeon bonding) | Service terms, family tithe, visitation rights | Character serves deity; similar to dungeon Pattern A but with lib-divine integration |

All of these use the same underlying machinery: **Contract** (terms), **Faction** (cultural norms), **Obligation** (ongoing moral costs), **Relationship** (bond type changes), and **Seed** (potential account seed creation if the player commits a slot). The specifics of what happens after the split differ (dungeon management, branch family passive income, divine service), but the split mechanism itself is universal.

**lib-dungeon doesn't implement the split** -- it consumes the result. The household split is a cross-cutting mechanic involving Contract, Faction, Obligation, Relationship, and Seed. lib-dungeon's FormBond endpoint accepts the outcome (character + bond type) and proceeds with dungeon-specific setup. Whether the character underwent a household split (Pattern A) or stayed in the household (Pattern B) is determined before FormBond is called.

### Pattern Selection Flow

```
Character touches dungeon core
    |
    v
Bond Contract created (lib-contract)
    |
    +--> dungeon_master seed created on CHARACTER (always, initially)
    |
    v
Player presented with choice (via Gardener UX / game-specific flow):
    |
    +---> "Commit to this dungeon" (Pattern A)
    |         |
    |         +--> Household split mechanic triggered
    |         |    (Contract + Faction norms + Obligation)
    |         |
    |         +--> dungeon_master seed PROMOTED from character to account
    |         |    (consumes one of 3 account seed slots)
    |         |
    |         +--> Character leaves household roster
    |         |
    |         +--> Dungeon garden created as top-level experience
    |
    +---> "Keep my family" (Pattern B)
              |
              +--> dungeon_master seed stays on character
              |
              +--> Dungeon influence transient (active while
              |    playing this character only)
              |
              +--> No household change, no account seed consumed
```

**The choice doesn't have to happen immediately.** A character can bond with a dungeon (Pattern B by default) and the player can later choose to promote the relationship to Pattern A through the household split mechanic. The reverse (Pattern A back to B) is not possible -- once the character has left the household, they're gone.

---

## The Dungeon as Garden

The Gardener deep dive defines a garden as "a conceptual space that defines a player's current gameplay context." Every player is always in some garden, and the gardener behavior manages their experience. The dungeon extends this pattern differently depending on the mastery pattern:

### Pattern A: The Dungeon IS the Garden

| Perspective | Experience | System |
|-------------|-----------|--------|
| **Dungeon master** (Pattern A) | The dungeon IS their garden -- they perceive through the dungeon's senses, issue commands, and experience the dungeon's emotional state. This is their primary gameplay context. | Gardener (dungeon garden type, top-level) |
| **Adventurers** (visitors) | The dungeon is a physical game location with monsters, traps, puzzles, and loot | Normal game mechanics (combat, inventory, mapping) |
| **Dungeon core** (the actor) | The dungeon tends its own space AND the master's garden experience simultaneously | Actor + Gardener APIs + Dungeon APIs |

When a player commits an account seed slot (Pattern A):

1. A "dungeon" garden type is created as a **top-level garden** -- selectable from the void alongside the guardian seed's garden
2. The dungeon core actor becomes the gardener behavior for that garden
3. Entity associations bind the master's character, the dungeon's inhabitants, and relevant inventories to the garden context
4. The master's perception of the dungeon (what they "see") is managed by the garden's entity session registrations
5. Commands from the master arrive as perceptions in the dungeon core's actor, gated by the `dungeon_master` seed capabilities
6. The UX capability manifest expands fully with dungeon-specific modules (dungeon sight, spawning, traps, layout, memory, channeling, coordination)

### Pattern B: The Dungeon Layers Onto the Garden

In Pattern B, the dungeon is not the garden. The player's garden remains their household garden (the guardian seed's context). When the player switches to the bonded character:

1. The dungeon core actor pushes perceptions into the character's Actor (just like a regional watcher/god does)
2. Dungeon-specific UX modules appear **transiently** -- gated by the character-owned dungeon_master seed's capabilities, visible while this character is selected
3. The player can issue commands to the dungeon through the character (gated by command.* capabilities)
4. Switch to another household character and the dungeon UX drops away
5. The dungeon continues to influence the bonded character's Actor regardless of who the player is controlling -- the character processes dungeon perceptions autonomously

No dungeon garden instance is created in Pattern B. The dungeon influence is routed through the character's Actor perception pipeline, not through Gardener.

### When No Master Is Bonded

The dungeon core actor still runs autonomously -- it just has no garden to tend and no partner receiving its experience. It continues to perceive, decide, and act within its domain based on its cognitive stage: in Stage 2 (event brain), purely instinct-driven with seed capabilities; in Stage 3 (character brain), personality-driven with the full variable provider stack. A masterless Ancient dungeon in Stage 3 is a formidable autonomous entity -- it has personality, grudges against past adventurers, memories of significant events, and the full cognitive depth of the character brain pipeline.

**Multi-game variability**: The dungeon garden behavior document varies per game. In Arcadia, the Pattern A dungeon master experience uses perception-gated awareness, command-gated actions, and channeling-gated power flows. A different game might use different mechanics for the master-dungeon relationship, might only support Pattern B (no full dungeon master game), or might omit dungeon bonding entirely (autonomous dungeons with no master partnership). lib-dungeon provides primitives, not policy.

---

## The Asymmetric Bond Pattern

Dungeon cores influence characters through the character's own Actor, not directly. A dungeon core's Actor (event brain) monitors domain events and makes decisions; the bonded master's character Actor receives commands as perceptions, gated by the master's `dungeon_master` seed capabilities. This is the same indirect influence pattern used by divine actors (gods influence through the character's Actor, not by controlling the character directly).

The dungeon-master relationship follows the same structural pattern as the player-character relationship, but manifests differently depending on the mastery pattern:

```
PATTERN A: NESTED ASYMMETRY (spirit → character → dungeon)
===========================================================

Account (guardian seed)                 Account (dungeon_master seed)
    |                                        |
    | spirit possesses household             | spirit IS the dungeon master
    | (guardian garden)                      | (dungeon garden)
    |                                        |
    v                                        v
Household of characters              Dungeon (autonomous Actor brain)
    autonomous NPC brains                  always running
    (player lost this character            master character lives here now
     in the household split)

    The same spirit has TWO relationships to the world,
    selectable from the void as separate games.


PATTERN B: LAYERED INFLUENCE (spirit → character ↔ dungeon)
===========================================================

Account (guardian seed only)
    |
    | spirit possesses household
    |
    v
Household of characters
    |
    +-- Character A (no dungeon bond) -- normal gameplay
    +-- Character B (dungeon_master seed) -- dungeon influence ON
    +-- Character C (no dungeon bond) -- normal gameplay
    |
    | When playing Character B:
    | dungeon perceptions layer onto character's Actor
    | dungeon UX appears transiently
    | dungeon_master seed grows
    |
    | When playing Character A or C:
    | dungeon influence on player drops away
    | Character B still processes dungeon perceptions autonomously
```

Both patterns share these properties:
- **Asymmetric agency**: One party has broader context, the other has deeper local knowledge (dungeon knows its domain)
- **Graceful absence**: The dungeon continues functioning when its partner disengages
- **Progressive depth**: The relationship deepens with shared experience (dungeon_master seed grows)
- **Contractual terms**: Governed by explicit Contract with consequences

The partner is not always a character. Corrupted bonds use a monster (actor-managed entity). When the partner isn't a character, the dungeon core has fewer tools -- no character Actor to send commands to as perceptions. The paired seeds still grow, but the dungeon core exerts influence primarily through the seed bond alone (almost no active control), and the monster avatar operates with limited agency to grow its `dungeon_master` seed deliberately. Corrupted bonds are always Pattern B (monsters don't have account seeds).

---

## Seed Types

The dungeon system introduces two seed types that grow in parallel: `dungeon_core` (the dungeon's own progressive growth -- mana capacity, genetic library, trap sophistication, spatial control, memory depth) and `dungeon_master` (the bonded entity's growth in the mastery role -- perception, command, channeling, coordination). The `dungeon_master` seed can be account-owned (Pattern A -- the spirit's relationship to the dungeon, persisting across character death) or character-owned (Pattern B -- one character's role, tied to that character's lifecycle). Seeds track growth in *roles*, not growth in *entities*.

### dungeon_core (The Dungeon's Growth)

| Property | Value |
|----------|-------|
| **SeedTypeCode** | `dungeon_core` |
| **DisplayName** | Dungeon Core |
| **MaxPerOwner** | 1 |
| **AllowedOwnerTypes** | `["actor"]` |
| **BondCardinality** | 0 (Contract handles the relationship, not seed bonds) |

**Growth Phases** (ordered by MinTotalGrowth):

| Phase | MinTotalGrowth | Cognitive Stage | Behavior |
|-------|---------------|-----------------|----------|
| Dormant | 0.0 | Stage 1: No actor | Reactive only -- responds to intrusions with pre-scripted responses. No autonomous decision-making. Passive growth only. |
| Stirring | 10.0 | Stage 2: Event brain actor | Actor spawned via Puppetmaster. Proactive spawning, basic trap usage. ABML behavior with instinct-driven defaults (`${personality.*}` = null). Personality preferences emerge through seed metadata. |
| Awakened | 50.0 | Stage 3: Character brain actor | Character created in dungeon system realm, bound to running actor via `/actor/bind-character`. Full variable provider activation. Layout manipulation, complex tactics, memory capture. Strong personality-driven decisions via `${personality.*}`, grudge tracking via `${encounters.*}`. |
| Ancient | 200.0 | Stage 3 (mature) | Full cognitive depth. Memory manifestation, event coordination, full master synergy. Personality evolution via CharacterPersonality's experience-driven trait shifts. A unique living entity with rich inner life. |

**Growth Domains**:

| Domain | Subdomain | Purpose |
|--------|-----------|---------|
| `mana_reserves` | `.ambient`, `.harvested` | Accumulated mana capacity (ambient from leyline, harvested from deaths) |
| `genetic_library` | `.{species}` | Logos completion per species for monster spawning quality |
| `trap_complexity` | `.mechanical`, `.magical`, `.puzzle` | Trap design sophistication by type |
| `domain_expansion` | `.radius`, `.rooms`, `.extension` | Spatial control capability |
| `memory_depth` | `.capture`, `.manifestation` | Memory accumulation and manifestation quality |

**Capability Rules** (gated by growth domain thresholds):

| Capability Code | Domain | Threshold | Formula | Description |
|----------------|--------|-----------|---------|-------------|
| `spawn_monster.basic` | `genetic_library` | 1.0 | linear | Basic pneuma echo creation |
| `spawn_monster.enhanced` | `genetic_library` | 5.0 | logarithmic | Enhanced quality spawning |
| `spawn_monster.alpha` | `genetic_library` | 20.0 | step | Alpha-tier creature spawning |
| `activate_trap.basic` | `trap_complexity` | 1.0 | linear | Basic trap activation |
| `activate_trap.complex` | `trap_complexity` | 10.0 | logarithmic | Complex trap activation |
| `seal_passage` | `domain_expansion` | 5.0 | step | Block/unblock passages |
| `shift_layout` | `domain_expansion` | 15.0 | step | Minor structural changes |
| `emit_miasma` | `mana_reserves` | 3.0 | linear | Ambient mana density control |
| `spawn_event_agent` | `mana_reserves` | 10.0 | step | Encounter coordinator creation |
| `evolve_inhabitant` | `genetic_library` | 10.0 | logarithmic | Monster echo upgrades |
| `manifest_memory` | `memory_depth` | 8.0 | step | Crystallize memories into physical form |

### dungeon_master (The Master's Role Growth)

| Property | Value |
|----------|-------|
| **SeedTypeCode** | `dungeon_master` |
| **DisplayName** | Dungeon Master |
| **MaxPerOwner** | 1 (can only master one dungeon at a time) |
| **AllowedOwnerTypes** | `["account", "character", "actor"]` (account for Pattern A full split, character for Pattern B bonded role, actor for Corrupted monster avatars) |
| **BondCardinality** | 0 (Contract handles the relationship) |

**Growth Phases** (ordered by MinTotalGrowth):

| Phase | MinTotalGrowth | Communication Fidelity |
|-------|---------------|----------------------|
| Bonded | 0.0 | Basic emotional awareness -- crude impressions of dungeon state |
| Attuned | 5.0 | Clear communication, basic commands accepted |
| Symbiotic | 25.0 | Rich command vocabulary, power channeling, tactical coordination |
| Transcendent | 100.0 | Near-perfect coordination, shared consciousness |

**Growth Domains**:

| Domain | Subdomain | Purpose |
|--------|-----------|---------|
| `perception` | `.emotional`, `.spatial`, `.tactical` | Shared awareness with the dungeon core |
| `command` | `.spawning`, `.traps`, `.layout`, `.strategy` | Directing the dungeon's actions |
| `channeling` | `.passive`, `.combat`, `.regeneration` | Mana/power flow between core and master |
| `coordination` | `.defenders`, `.encounters`, `.manifestation` | Directing autonomous dungeon systems |

**Capability Rules** (gated by growth domain thresholds):

| Capability Code | Domain | Threshold | Formula | Description |
|----------------|--------|-----------|---------|-------------|
| `perception.basic` | `perception` | 0.0 | linear | Sense dungeon's emotional state from bonding moment |
| `perception.tactical` | `perception` | 5.0 | logarithmic | Perceive intruder details, room states |
| `command.basic` | `command` | 2.0 | linear | Simple directives: "spawn defenders", "activate trap" |
| `command.tactical` | `command` | 10.0 | logarithmic | Multi-wave defense, ambush coordination |
| `channeling.basic` | `channeling` | 3.0 | linear | Receive mana/power from core |
| `channeling.combat` | `channeling` | 15.0 | logarithmic | Channel core's combat abilities in battle |
| `coordination.event_brain` | `coordination` | 20.0 | step | Direct encounter coordinators personally |

### Growth Contribution Events

| Event | Core Seed Domain | Master Seed Domain | Source |
|-------|-----------------|-------------------|--------|
| Monster killed within domain | `mana_reserves.harvested` +0.1 | -- | Dungeon cognition |
| Logos absorbed from death | `genetic_library.{species}` +varies | -- | Dungeon cognition |
| Trap successfully triggers | `trap_complexity.*` +0.2 | `command.traps` +0.1 (if master directed) | Dungeon cognition |
| Domain boundary expanded | `domain_expansion.*` +1.0 | -- | Domain management |
| Master command executed | -- | `command.*` +0.1 | Bond communication |
| Master perceives dungeon state | -- | `perception.*` +0.05 | Bond communication |
| Master channels power | -- | `channeling.*` +0.2 | Bond communication |
| Significant memory stored | `memory_depth.capture` +0.5 | `perception.emotional` +0.1 (if master present) | Memory system |
| Memory manifested | `memory_depth.manifestation` +1.0 | `coordination.manifestation` +0.5 (if master guided) | Memory system |
| Adventurers defeated | `mana_reserves` +0.5, `genetic_library` +0.1 | `command.strategy` +0.3 (if master directed) | Analytics milestone |

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Dungeon core records (MySQL), bond records (MySQL), inhabitant state (Redis), memory records (MySQL), dungeon cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for dungeon mutations, bond formation, spawn operations |
| lib-messaging (`IMessageBus`) | Publishing dungeon lifecycle events, memory events, bond events, inhabitant events |
| lib-messaging (`IEventConsumer`) | Registering handlers for domain-scoped combat, death, intrusion, and seed phase events |
| lib-seed (`ISeedClient`) | `dungeon_core` and `dungeon_master` seed type registration, growth recording, capability manifest queries (L2) |
| lib-currency (`ICurrencyClient`) | Mana wallet creation, credit/debit for spawn costs and trap charges (L2) |
| lib-contract (`IContractClient`) | Dungeon-master bond management -- creation, milestone tracking, termination (L1) |
| lib-actor (`IActorClient`) | Injecting perceptions into the bonded master's character Actor for indirect influence (L2) |
| lib-character (`ICharacterClient`) | Validating character existence for willing bond formation; creating dungeon character in system realm at Awakened phase for dynamic binding (L2) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence for dungeon scoping (L2) |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration (L1) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-mapping (`IMappingClient`) | Domain boundary registration, room connectivity queries, spatial affordance queries | Dungeon operates without spatial awareness; room-based features disabled |
| lib-scene (`ISceneClient`) | Memory manifestation as visual decorations (paintings, environmental effects) | Memory manifestation limited to item-based forms only |
| lib-save-load (`ISaveLoadClient`) | Persistent dungeon construction state (room layout, trap placement, structural data) | Dungeon layout resets on actor restart; volatile-only operation |
| lib-item (`IItemClient`) | Memory item creation (data crystals, memory fragments), loot generation | Memory items and loot spawning disabled |
| lib-inventory (`IInventoryClient`) | Trap/treasure container management within dungeon rooms | Container-based loot management disabled |
| lib-puppetmaster (`IPuppetmasterClient`) | Starting/stopping dungeon core actors on creation/deactivation | Dungeon actors must be managed manually via Actor APIs |
| lib-gardener (`IGardenerClient`) | Creating dungeon garden instances for bonded masters | Master experience orchestration disabled; bond provides seed growth only |
| lib-analytics (`IAnalyticsClient`) | Event significance scoring for memory capture thresholds | Memory capture uses local significance calculation only |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Dungeon is a new L4 service with no current consumers. Future dependents: combat systems (subscribe to dungeon inhabitant events), Storyline (consume dungeon memory archives for narrative generation), Gardener (dungeon as garden type) |

---

## State Storage

### Dungeon Core Store
**Store**: `dungeon-cores` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `core:{dungeonId}` | `DungeonCoreModel` | Primary lookup by dungeon ID. Stores identity, personality type, status, seed references, economy references, core location, domain radius, characterId (null until Awakened phase triggers character creation and dynamic binding). |
| `core-code:{gameServiceId}:{code}` | `DungeonCoreModel` | Code-uniqueness lookup within game service scope |

### Bond Store
**Store**: `dungeon-bonds` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bond:{bondId}` | `DungeonBondModel` | Primary lookup by bond ID. Stores contract reference, bond type (Priest/Paladin/Corrupted), master entity reference (type + ID), master seed ID, formation timestamp. |
| `bond-dungeon:{dungeonId}` | `DungeonBondModel` | Active bond lookup by dungeon (at most one active bond per dungeon) |
| `bond-master:{entityType}:{entityId}` | `DungeonBondModel` | Active bond lookup by master entity |

### Inhabitant Store
**Store**: `dungeon-inhabitants` (Backend: Redis, prefix: `dungeon:inhab`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `inhab:{dungeonId}:{inhabitantId}` | `InhabitantModel` | Individual monster/creature state: species, quality level, room location, stats, soul slot usage |
| `inhab-counts:{dungeonId}` | `InhabitantCountsModel` | Denormalized species count map for fast capability checks |

### Memory Store
**Store**: `dungeon-memories` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `mem:{dungeonId}:{memoryId}` | `DungeonMemoryModel` | Stored memory: event type, significance score, participants, location, outcome, emotional context, manifestation status |

### Dungeon Cache
**Store**: `dungeon-cache` (Backend: Redis, prefix: `dungeon:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cap:{dungeonId}` | `CachedCapabilityManifest` | Cached dungeon_core seed capability manifest for fast action gating |
| `mastercap:{dungeonId}` | `CachedCapabilityManifest` | Cached dungeon_master seed capability manifest for communication gating |
| `vitals:{dungeonId}` | `DungeonVitalsCache` | Cached volatile state: core integrity, current mana, mana generation rate, threat level |

### Distributed Locks
**Store**: `dungeon-lock` (Backend: Redis, prefix: `dungeon:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `core:{dungeonId}` | Dungeon core mutation lock (create, update, deactivate, delete) |
| `bond:{dungeonId}` | Bond formation/dissolution lock (one bond at a time) |
| `spawn:{dungeonId}` | Spawn operation lock (prevent concurrent mana overdraft) |
| `memory:{dungeonId}` | Memory capture/manifestation lock |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `dungeon.created` | `DungeonCreatedEvent` | Dungeon core entity created (lifecycle) |
| `dungeon.updated` | `DungeonUpdatedEvent` | Dungeon core entity updated (lifecycle) |
| `dungeon.deleted` | `DungeonDeletedEvent` | Dungeon core entity deleted (lifecycle) |
| `dungeon.bond.formed` | `DungeonBondFormedEvent` | Character or monster bonds with dungeon core |
| `dungeon.bond.dissolved` | `DungeonBondDissolvedEvent` | Bond terminated (master death, contract breach, voluntary) |
| `dungeon.inhabitant.spawned` | `DungeonInhabitantSpawnedEvent` | Monster spawned within dungeon domain |
| `dungeon.inhabitant.killed` | `DungeonInhabitantKilledEvent` | Monster killed within dungeon domain |
| `dungeon.memory.captured` | `DungeonMemoryCapturedEvent` | Significant event stored as dungeon memory |
| `dungeon.memory.manifested` | `DungeonMemoryManifestedEvent` | Memory crystallized into physical form (item, painting, environmental) |
| `dungeon.trap.triggered` | `DungeonTrapTriggeredEvent` | Trap activated within dungeon domain |
| `dungeon.layout.changed` | `DungeonLayoutChangedEvent` | Passage sealed/unsealed or structural shift occurred |
| `dungeon.phase.changed` | `DungeonPhaseChangedEvent` | Dungeon core seed transitioned to new growth phase |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `seed.phase.changed` | `HandleSeedPhaseChangedAsync` | For `dungeon_core` seeds: update cached phase, publish `dungeon.phase.changed`, re-evaluate available actions. **At Stirring phase**: start dungeon core actor via Puppetmaster (event brain, no character). **At Awakened phase**: create Character in dungeon system realm, call `/actor/bind-character` to transition running actor to character brain mode with full variable providers, store characterId on DungeonCoreModel. For `dungeon_master` seeds: advance bond contract milestones. |
| `seed.capability.updated` | `HandleSeedCapabilityUpdatedAsync` | Invalidate cached capability manifests for affected dungeon or master |
| `contract.terminated` | `HandleContractTerminatedAsync` | Clean up bond record when dungeon-master contract ends; archive master seed if configured |

### Resource Cleanup (T28)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| character | dungeon | CASCADE | `/dungeon/cleanup-by-character` |
| realm | dungeon | CASCADE | `/dungeon/cleanup-by-realm` |

### DI Listener Patterns

| Pattern | Interface | Action |
|---------|-----------|--------|
| Seed evolution | `ISeedEvolutionListener` | Receives growth, phase change, and capability notifications for dungeon core and master seeds. Updates cached manifests. Writes to distributed state for multi-node safety. |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `CoreSeedTypeCode` | `DUNGEON_CORE_SEED_TYPE_CODE` | `dungeon_core` | Seed type code for dungeon core growth |
| `MasterSeedTypeCode` | `DUNGEON_MASTER_SEED_TYPE_CODE` | `dungeon_master` | Seed type code for dungeon master role growth |
| `ManaCurrencyCode` | `DUNGEON_MANA_CURRENCY_CODE` | `mana` | Currency code for mana economy within each game service |
| `BondContractTemplateCode` | `DUNGEON_BOND_CONTRACT_TEMPLATE_CODE` | `dungeon-master-bond` | Contract template code for master bonds |
| `MaxInhabitantsPerDungeon` | `DUNGEON_MAX_INHABITANTS_PER_DUNGEON` | `100` | Maximum concurrent creature instances per dungeon |
| `MaxMemoriesPerDungeon` | `DUNGEON_MAX_MEMORIES_PER_DUNGEON` | `500` | Maximum stored memories before oldest are pruned |
| `MemorySignificanceThreshold` | `DUNGEON_MEMORY_SIGNIFICANCE_THRESHOLD` | `0.5` | Minimum significance score for memory capture |
| `MemoryManifestationThreshold` | `DUNGEON_MEMORY_MANIFESTATION_THRESHOLD` | `0.8` | Minimum significance to queue for physical manifestation |
| `CapabilityCacheTtlSeconds` | `DUNGEON_CAPABILITY_CACHE_TTL_SECONDS` | `300` | TTL for cached seed capability manifests |
| `SpawnCostMultiplier` | `DUNGEON_SPAWN_COST_MULTIPLIER` | `1.0` | Global multiplier for monster spawn mana costs |
| `DefaultDomainRadius` | `DUNGEON_DEFAULT_DOMAIN_RADIUS` | `500.0` | Default domain radius (meters from core) for new dungeons |
| `DungeonGardenTypeCode` | `DUNGEON_GARDEN_TYPE_CODE` | `dungeon` | Garden type code registered with Gardener for master experience |
| `DistributedLockTimeoutSeconds` | `DUNGEON_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |
| `MasterSeedArchiveOnDissolve` | `DUNGEON_MASTER_SEED_ARCHIVE_ON_DISSOLVE` | `true` | Whether to archive (true) or delete (false) master seeds on bond dissolution |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<DungeonService>` | Structured logging |
| `DungeonServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 6 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `ISeedClient` | Seed type registration, growth recording, capability queries (L2) |
| `ICurrencyClient` | Mana wallet management (L2) |
| `IContractClient` | Bond contract lifecycle (L1) |
| `IActorClient` | Perception injection into master's character Actor (L2) |
| `ICharacterClient` | Character validation for willing bonds (L2) |
| `IGameServiceClient` | Game service validation (L2) |
| `IResourceClient` | Reference tracking, cleanup callbacks (L1) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

### Variable Provider Factories

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `DungeonCoreSeedVariableProviderFactory` | `${seed.*}` | dungeon_core seed growth domains and capability manifest | `IVariableProviderFactory` (DI singleton) |
| `DungeonActorVariableProviderFactory` | `${dungeon.*}` | Volatile actor state (core integrity, mana, inhabitants, feelings, intruders) | `IVariableProviderFactory` (DI singleton) |
| `DungeonMasterSeedVariableProviderFactory` | `${master.seed.*}` | dungeon_master seed phases and capabilities (active when bonded) | `IVariableProviderFactory` (DI singleton) |
| `DungeonMasterCharacterVariableProviderFactory` | `${master.*}` | Master character data -- health, location, combat state (active when bonded to character) | `IVariableProviderFactory` (DI singleton) |

### ABML Action Handlers

| Handler | Action | Description |
|---------|--------|-------------|
| `SpawnMonsterHandler` | `spawn_monster:` | Create pneuma echo from genetic library, gated by `spawn_monster.*` capability |
| `ActivateTrapHandler` | `activate_trap:` | Trigger trap system, gated by `activate_trap.*` capability |
| `SealPassageHandler` | `seal_passage:` | Block/unblock passage, gated by `seal_passage` capability |
| `ShiftLayoutHandler` | `shift_layout:` | Minor structural change, gated by `shift_layout` capability |
| `EmitMiasmaHandler` | `emit_miasma:` | Adjust ambient mana density, gated by `emit_miasma` capability |
| `ManifestMemoryHandler` | `manifest_memory:` | Crystallize memory into physical form, gated by `manifest_memory` capability |
| `CommunicateMasterHandler` | `communicate_master:` | Send perception to bonded master's Actor, gated by master's perception capabilities |
| `SpawnEventAgentHandler` | `spawn_event_agent:` | Create encounter coordinator, gated by `spawn_event_agent` capability |

---

## API Endpoints (Implementation Notes)

### Dungeon Core Management (8 endpoints)

All endpoints require `developer` role.

- **Create** (`/dungeon/create`): Validates game service existence. Provisions mana currency wallet via `ICurrencyClient`, `dungeon_core` seed via `ISeedClient`. Personality type stored in seed metadata. Optionally starts dungeon core actor via `IPuppetmasterClient` (soft). Optionally persists initial layout via `ISaveLoadClient` (soft). Saves under both ID and code lookup keys.
- **Get** (`/dungeon/get`): Load from MySQL by dungeonId. Enriches with cached capability manifest if available.
- **GetByCode** (`/dungeon/get-by-code`): JSON query by gameServiceId + code.
- **List** (`/dungeon/list`): Paged JSON query with required gameServiceId filter, optional status, personality type, and growth phase filters.
- **Update** (`/dungeon/update`): Acquires distributed lock. Partial update. Publishes lifecycle updated event.
- **Activate** (`/dungeon/activate`): Lock, set status Active. Start dungeon core actor via Puppetmaster (soft). Publishes activation event.
- **Deactivate** (`/dungeon/deactivate`): Lock, set status Dormant. Stop actor via Puppetmaster (soft). Dissolve active bond if any. Publishes dormancy event.
- **Delete** (`/dungeon/delete`): Lock. Deactivate if active. Dissolve bond. Remove inhabitants. Delete memories. Coordinate cleanup via lib-resource. Delete record. Publishes lifecycle deleted event.

### Bond Management (4 endpoints)

All endpoints require `developer` role.

- **FormBond** (`/dungeon/bond/form`): Validates dungeon is Active and has no active bond. Validates master entity exists. Creates Contract instance from bond template with bond type terms (Priest/Paladin/Corrupted). Contract prebound API triggers `dungeon_master` seed creation for the master entity (character-owned initially -- Pattern B). Updates dungeon core's bond references. Publishes `dungeon.bond.formed`. Note: the seed always starts character-owned; promotion to account-owned (Pattern A) happens through the household split mechanic, which is external to lib-dungeon.
- **DissolveBond** (`/dungeon/bond/dissolve`): Lock. Terminates Contract. Archives or deletes `dungeon_master` seed per `MasterSeedArchiveOnDissolve` config. Clears bond references. Destroys dungeon garden if active. Publishes `dungeon.bond.dissolved`.
- **GetBond** (`/dungeon/bond/get`): Returns active bond details for a dungeon, including master seed phase and capability summary.
- **GetBondByMaster** (`/dungeon/bond/get-by-master`): Lookup active bond by master entity type + ID.

### Inhabitant Management (4 endpoints)

All endpoints require `developer` role.

- **Spawn** (`/dungeon/inhabitant/spawn`): Validates dungeon `spawn_monster.*` seed capability for requested quality tier. Validates sufficient mana. Deducts spawn cost from mana wallet. Creates inhabitant record in Redis. Updates denormalized counts. Records seed growth to `genetic_library.{species}`. If master directed: records growth to master's `command.spawning`. Publishes `dungeon.inhabitant.spawned`.
- **Kill** (`/dungeon/inhabitant/kill`): Removes inhabitant. Credits mana from death (`mana_reserves.harvested` growth). Absorbs logos to `genetic_library.{species}`. Evaluates memory capture significance. Publishes `dungeon.inhabitant.killed`.
- **List** (`/dungeon/inhabitant/list`): Returns all inhabitants for a dungeon with optional species filter.
- **GetCounts** (`/dungeon/inhabitant/get-counts`): Returns denormalized species count map.

### Memory Management (4 endpoints)

All endpoints require `developer` role.

- **CaptureMemory** (`/dungeon/memory/capture`): Calculates significance score from event properties. If above `MemorySignificanceThreshold`: stores memory, records seed growth to `memory_depth.capture`. If master present: records growth to master's `perception.emotional`. If above `MemoryManifestationThreshold` and `manifest_memory` capability unlocked: queues for manifestation. Publishes `dungeon.memory.captured`.
- **ManifestMemory** (`/dungeon/memory/manifest`): Validates `manifest_memory` capability. Manifests as item (via `IItemClient`), scene decoration (via `ISceneClient`), or environmental effect (via `IMappingClient`) based on manifestation type. Records seed growth to `memory_depth.manifestation`. If master guided: records growth to master's `coordination.manifestation`. Publishes `dungeon.memory.manifested`.
- **ListMemories** (`/dungeon/memory/list`): Paged query with optional significance, event type, and manifestation status filters.
- **GetMemory** (`/dungeon/memory/get`): Load by memoryId.

### Domain Management (2 endpoints)

- **GetVitals** (`/dungeon/vitals`): Returns volatile dungeon state -- core integrity, current mana, mana generation rate, threat level, inhabitant summary, active bond summary.
- **GetDomainInfo** (`/dungeon/domain`): Returns domain boundaries, room count, extension core locations, active trap count. Queries Mapping (soft) for spatial details.

### Cleanup Endpoints (2 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByCharacter** (`/dungeon/cleanup-by-character`): Dissolves any bond where the character is the master. Removes character-specific memories.
- **CleanupByRealm** (`/dungeon/cleanup-by-realm`): Deactivates and deletes all dungeons in the realm. Cascades bond dissolution, inhabitant removal, memory deletion.

---

## Dungeon Cognition Pipeline

The dungeon core actor uses a simplified cognition pipeline (`creature_base` template) with fewer stages than the humanoid pipeline. The pipeline operates identically in Stage 2 (event brain) and Stage 3 (character brain) -- the difference is in the **data available**, not the pipeline structure. In Stage 2, memory queries and capability checks work against dungeon-specific stores only. In Stage 3, the same pipeline also has access to `${personality.*}`, `${encounters.*}`, and `${backstory.*}` from the bound character, enabling richer attention filtering (grudges affect priority), more nuanced intention formation (personality modulates aggression), and memory queries that include character encounter history.

```
+-----------------------------------------------------------------------+
|                    DUNGEON COGNITION PIPELINE                          |
|                                                                        |
|   1. FILTER ATTENTION                                                  |
|   -------------------                                                  |
|   Priority: intrusion (10), combat (8), death (7), loot (5)           |
|   Threat fast-track: urgency > 0.8 bypasses to action                 |
|   Attention budget: configurable perceptions per tick                  |
|                                                                        |
|   2. MEMORY QUERY (simplified)                                         |
|   ----------------------------                                         |
|   "Have I seen these intruders before?"                                |
|   "What happened last time in this room?"                              |
|   Entity-indexed lookup for adventurer history                         |
|                                                                        |
|   3. CAPABILITY CHECK (via dungeon_core seed manifest)                 |
|   ----------------------------------------------------                 |
|   Query seed capability manifest for available actions                 |
|   Higher fidelity = better execution quality                           |
|   Unlocked capabilities gate which intentions can form                 |
|                                                                        |
|   4. MASTER COMMUNICATION CHECK (via dungeon_master seed manifest)     |
|   ----------------------------------------------------------------     |
|   If bonded: check master's perception.* capabilities                  |
|   Higher perception fidelity = richer information shared               |
|   If master has command.tactical: accept complex directives            |
|   If no bond or Corrupted bond: skip (dungeon acts autonomously)       |
|                                                                        |
|   5. INTENTION FORMATION                                               |
|   ----------------------                                               |
|   Apply master commands (if any, and if command capability allows)      |
|   Evaluate threats -> spawn defenders or activate traps                |
|   Evaluate opportunities -> absorb corpses, claim resources            |
|   Evaluate memories -> store significant events                        |
|   Record seed growth contributions for significant actions             |
|                                                                        |
|   SKIPPED STAGES (unlike humanoid_base):                               |
|   No significance assessment (dungeon cares about everything)          |
|   No goal impact evaluation (goals are fixed: survive, grow)           |
+-----------------------------------------------------------------------+
```

---

## Physical Construction

Dungeon physical form is a cross-service concern distributed across multiple services:

| Aspect | Service | Purpose |
|--------|---------|---------|
| **Spatial data** | Mapping (L4, soft) | Room boundaries, corridors, connectivity graph, affordance queries (e.g., "what objects can be thrown in room X?") |
| **Visual composition** | Scene (L4, soft) | Node trees for room decorations, memory manifestation paintings, environmental effects |
| **Persistent state** | Save-Load (L4, soft) | Versioned dungeon construction snapshots: room properties, trap placements, structural modifications |
| **Procedural generation** | lib-procedural (L4, future) | Houdini-backed generation of new chambers, corridors, and environmental features when domain_expansion capabilities are exercised. See [PROCEDURAL.md](PROCEDURAL.md) for the full deep dive. |
| **Inhabitant tracking** | Dungeon (this service) | Monster/creature positions, species, quality, soul slot usage |

As the `dungeon_core` seed grows, `domain_expansion.*` capabilities unlock. When the dungeon core actor exercises `shift_layout` or expands its domain, the physical changes are:
1. Persisted via Save-Load (structural state)
2. Registered in Mapping (spatial index)
3. Composed in Scene (visual decorations)
4. Future: generated via lib-procedural (Houdini HDA execution for geometry)

**Deterministic generation**: When procedural generation is available, dungeon growth uses deterministic seeds (same growth parameters = identical chamber geometry), enabling reproducible dungeon layouts and Redis-cached generation results.

---

## Bond Types

| Bond Type | Master Entity | Patterns | Relationship | Death Behavior | Master Seed Effect |
|-----------|--------------|----------|-------------|----------------|--------------------|
| **Priest** | Character (willing) | A or B | Core provides mana, master provides direction | Pattern A: account seed persists (spirit survives character death). Pattern B: character seed archived, bond preserved for next character. | Full growth tracking; seed archived on dissolution, retains all growth |
| **Paladin** | Character (willing) | A or B | Core channels combat abilities through master | Pattern A: account seed persists, growth halved. Pattern B: character seed growth halved on death; bond can be re-formed. | Full growth; `channeling.combat` domains grow faster |
| **Corrupted** | Monster (dominated) | B only | Core dominates, master is avatar | Core dies if avatar destroyed | Minimal agency -- growth happens passively, monster cannot deliberately direct growth |

**Pattern A death behavior note**: Because the `dungeon_master` seed is account-owned in Pattern A, it naturally survives character death -- the spirit's relationship to the dungeon persists even when the vessel dies. The character that left the household can be replaced (the dungeon may find a new physical partner, or the spirit may inhabit the dungeon more directly). This is a significant advantage of Pattern A.

Bond formation is entirely Contract-driven. The contract template (`dungeon-master-bond`) includes:
- Party roles: `dungeon_core` (actor entity) and `dungeon_master` (account, character, or actor entity)
- Bond type term (Priest/Paladin/Corrupted)
- Prebound API on contract creation: create `dungeon_master` seed for the bonded entity
- Milestones linked to master seed phase transitions (Bonded -> Attuned -> Symbiotic -> Transcendent)
- Enforcement mode: consequence-based

---

## Visual Aid

Dungeon core identity is owned here. Dungeon behavior starts as Actor event brain (Stage 2) via Puppetmaster, then transitions to character brain (Stage 3) via dynamic binding when the Awakened phase is reached. Dungeon growth is Seed (`dungeon_core` seed type). Dungeon character identity lives in a dungeon system realm (Stage 3 only). Dungeon master bond is Contract. Mana economy is Currency. Physical layout is Save-Load + Mapping. Visual composition is Scene. Memory items are Item. Monster spawning and trap activation are dungeon-specific APIs orchestrated by lib-dungeon. Player-facing dungeon master experience is Gardener (dungeon garden type). Procedural chamber generation is a future integration with lib-procedural (Houdini backend).

### Pattern A: Full Split (Account-Level)

```
+-----------------------------------------------------------------------+
|                    DUNGEON STATE — PATTERN A                           |
|                                                                        |
|   ACCOUNT SEEDS (up to 3 slots)                                       |
|   +------------------+   +------------------+   +------------------+  |
|   | Slot 1: guardian |   | Slot 2: dungeon_ |   | Slot 3: (empty)  |  |
|   |  (household)     |   |  master (THIS)   |   |                  |  |
|   +--------+---------+   +--------+---------+   +------------------+  |
|            |                       |                                   |
|     guardian garden          dungeon garden                            |
|    (household mgmt)    (full dungeon master UX)                       |
|                               |                                       |
|   DUNGEON_CORE SEED     DUNGEON_MASTER SEED    DUNGEON GARDEN         |
|   (Progressive, MySQL)  (Progressive, MySQL)   (Gardener)             |
|   +------------------+  +------------------+  +------------------+    |
|   | Growth Domains:  |  | Growth Domains:  |  | Garden type:     |   |
|   |  mana_reserves   |  |  perception      |  |  dungeon         |   |
|   |  genetic_lib.*   |  |  command         |  | Player: master   |   |
|   |  trap_complex.*  |  |  channeling      |  | Entities: char,  |   |
|   |  domain_exp.*    |  |  coordination    |  |  inhabitants,    |   |
|   |  memory_depth.*  |  |                  |  |  inventory,      |   |
|   |                  |  | Owner: ACCOUNT   |  |  mana wallet     |   |
|   | Phase: Awakened  |  | Phase: Symbiotic |  | Tended by:       |   |
|   +------------------+  +------------------+  |  dungeon core    |   |
|                                                |  actor           |   |
|        CONTRACT          ACTOR STATE           +------------------+   |
|   +------------------+  +------------------+                          |
|   | Bond Type:       |  | CoreIntegrity    |   Cross-pollination:     |
|   |  Paladin         |  | CurrentMana      |   dungeon_master seed    |
|   | Milestones:      |  | Feelings         |   ──► guardian seed      |
|   |  transcendent    |  | ActiveIntruders  |   (spirit-level growth)  |
|   +------------------+  +------------------+                          |
|                                                                        |
|   PHYSICAL FORM                                                        |
|   +------+  +-------+  +---------+  +------------+                    |
|   |Mapping|  | Scene |  |Save-Load|  |Procedural  |                    |
|   |spatial|  |visual |  |persist  |  |(future)    |                    |
|   +------+  +-------+  +---------+  +------------+                    |
+-----------------------------------------------------------------------+
```

### Pattern B: Bonded Role (Character-Level)

```
+-----------------------------------------------------------------------+
|                    DUNGEON STATE — PATTERN B                           |
|                                                                        |
|   ACCOUNT SEEDS                                                       |
|   +------------------+   +------------------+   +------------------+  |
|   | Slot 1: guardian |   | Slot 2: (empty)  |   | Slot 3: (empty)  |  |
|   |  (household)     |   |  no dungeon slot  |   |                  |  |
|   +--------+---------+   +------------------+   +------------------+  |
|            |                                                           |
|     guardian garden (always the player's garden)                       |
|            |                                                           |
|   HOUSEHOLD                                                           |
|   +------+  +------+  +------+                                       |
|   |Char A|  |Char B|  |Char C|                                       |
|   |      |  |DM    |  |      |                                       |
|   +------+  +--+---+  +------+                                       |
|                |                                                       |
|   CHAR B's DUNGEON BOND                                               |
|   +------------------+  +------------------+                          |
|   | dungeon_master   |  | CONTRACT         |                          |
|   | seed             |  | Bond Type:       |                          |
|   |                  |  |  Priest           |                          |
|   | Owner: CHARACTER |  | Milestones:      |                          |
|   | Phase: Attuned   |  |  attuned         |                          |
|   +------------------+  +------------------+                          |
|         |                                                              |
|         |  While player controls Char B:                               |
|         |  +-- dungeon perceptions layer onto Char B's Actor           |
|         |  +-- dungeon UX appears transiently                          |
|         |  +-- dungeon_master seed grows actively                      |
|         |  +-- guardian seed cross-pollinated (reduced rate)            |
|         |                                                              |
|         |  While player controls Char A or C:                          |
|         |  +-- dungeon perceptions still reach Char B's Actor          |
|         |  |   (character processes them autonomously)                  |
|         |  +-- dungeon UX NOT visible to player                        |
|         |  +-- dungeon_master seed still grows (from char activity)    |
|         |  +-- no cross-pollination to guardian seed                   |
|         |                                                              |
|   DUNGEON CORE (same as Pattern A)                                    |
|   +------------------+  +------------------+                          |
|   | dungeon_core     |  | ACTOR STATE      |                          |
|   | seed             |  | (always running) |                          |
|   | Phase: Awakened  |  +------------------+                          |
|   +------------------+                                                |
+-----------------------------------------------------------------------+
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 0: Seed Foundation (Prerequisite)
- Register `dungeon_core` seed type with growth phases, domains, and capability rules
- Register `dungeon_master` seed type with growth phases, domains, and capability rules
- Create `dungeon-master-bond` contract template with bond type variants

### Phase 1: Core Infrastructure (Actor + Seed Integration)
- Create dungeon-api.yaml schema with all endpoints
- Create dungeon-events.yaml schema
- Create dungeon-configuration.yaml schema
- Generate service code
- Implement dungeon core CRUD (create provisions seed + wallet; actor NOT started at creation -- starts at Stirring phase)
- Implement variable provider factories for ABML expression access
- Implement ABML action handlers for dungeon capabilities
- Seed the dungeon system realm (`DUNGEON_CORES` with `isSystemType: true`) and dungeon species

### Phase 1.5: Cognitive Progression (Dynamic Character Binding)
- Implement `HandleSeedPhaseChangedAsync` handler for cognitive stage transitions:
  - **Stirring phase**: Start dungeon core actor via Puppetmaster (event brain, no character)
  - **Awakened phase**: Create Character in dungeon system realm, seed personality traits from dungeon personality type, call `/actor/bind-character` to transition actor to character brain, store characterId on DungeonCoreModel
- Handle failure cases: retry binding if character exists but binding failed (idempotency via characterId on model)
- Optionally seed backstory elements from dungeon creation history via ICharacterHistoryClient (soft)

### Phase 2: Dungeon Master Bond (Contract + Master Seed)
- Implement bond formation flow (Contract creation triggers master seed creation)
- Implement bond communication (perception injection gated by master capabilities)
- Implement bond dissolution with seed archival
- Implement Corrupted bond variant for monster avatars

### Phase 3: Capabilities (Seed-Gated Actions)
- Implement spawn capabilities with quality tiers gated by seed
- Implement trap capabilities
- Implement event coordinator spawning
- Wire growth contributions for all capability executions

### Phase 4: Memory System
- Implement memory capture with significance scoring
- Implement memory manifestation (item, scene, environmental) gated by seed capability
- Implement personality-based manifestation style preferences
- Wire growth contributions for memory system events

### Phase 5: Physical Construction
- Integrate with Save-Load for persistent dungeon layout
- Integrate with Mapping for spatial domain registration
- Integrate with Scene for visual composition
- Future: integrate with lib-procedural for Houdini-based chamber generation

### Phase 6: Garden Integration (Pattern A)
- Register dungeon garden type with Gardener
- Implement dungeon garden creation on seed promotion (Pattern A -- when character-owned seed becomes account-owned via household split)
- Implement entity session registration for master's dungeon experience
- Create ABML gardener behavior for dungeon master experience orchestration

### Phase 7: Transient UX Routing (Pattern B)
- Implement dynamic UX capability manifest updates on character switch (dungeon UX appears/disappears)
- Implement dungeon perception routing through character Actor pipeline (no garden needed)
- Implement transient cross-pollination (dungeon_master -> guardian, active only while player controls bonded character)

---

## Potential Extensions

1. **Mana as Currency wallet vs. virtual resource**: The dungeon_core seed tracks `mana_reserves` growth (long-term capacity), but volatile mana balance needs a home. Currency wallet enables NPC economic participation (dungeon trades with merchants for materials). Virtual resource in actor state is simpler but isolated from the economy.

2. **Mega-dungeon coordination**: Multiple dungeon cores in a mega-dungeon complex. Each core has its own `dungeon_core` seed and actor. Coordination via shared territory Contracts or a parent-child dungeon hierarchy.

3. **Cross-realm aberrant dungeons**: Dungeons that span realm boundaries. Requires design decisions about seed GameServiceId constraints and cross-realm actor communication.

4. **Seed cross-pollination mechanism**: Cross-pollination between dungeon_master and guardian seeds is core to both patterns but the mechanism needs design. Pattern A: account-level cross-pollination (both seeds are on the same account, growth in one feeds the other at a configurable rate). Pattern B: transient cross-pollination (only while the player is controlling the bonded character, at a reduced rate). Requires a cross-pollination API or listener in lib-seed -- configurable multiplier per seed-type pair (default 0.0, enabled for dungeon_master -> guardian).

5. **Dungeon political integration**: Officially-sanctioned dungeons interact with faction/political systems. `dungeon_core` seed growth phases could map to political recognition tiers.

6. **Client events**: `dungeon-client-events.yaml` for pushing dungeon state changes (intrusion alerts, memory manifestations, bond communication) to the master's WebSocket client.

7. **Variable provider for Status**: NPCs inside a dungeon need to know dungeon-specific effects (`${dungeon.miasma_level}`, `${dungeon.room_hazard}`) for GOAP decision-making.

8. **Dungeon economy integration**: Dungeons as economic actors -- trading monster parts, selling access, purchasing materials for trap construction through the Currency/Escrow system.

9. **Procedural generation via Houdini**: When lib-procedural is implemented, dungeon growth (domain_expansion capabilities) triggers HDA execution to generate chamber geometry. Deterministic seeds enable cached, reproducible layouts. HDA templates define visual style; dungeon personality + parameters customize output.

10. **Dungeon personality evolution**: Once a dungeon has a character record (Stage 3), CharacterPersonality's experience-driven trait evolution applies. Defeating adventurers might shift the dungeon toward cruelty; a dungeon that repeatedly loses might shift toward cunning or patience. This creates emergent dungeon personalities that change based on what happens to them -- the same system that makes NPC personalities evolve over time.

11. **Dungeon encounter memory integration**: Stage 3 dungeons with CharacterEncounter records can develop grudges against specific adventurers, remember which tactics worked against which party compositions, and adjust their behavior accordingly. A dungeon that was defeated by a fire-heavy party might invest more heavily in fire-resistant monsters next time.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Personality type is opaque string**: Not an enum. Follows the same extensibility pattern as seed type codes, collection type codes, faction codes. Arcadia defines "martial", "memorial", "festive", "scholarly"; other games define their own. lib-dungeon stores whatever personality string is provided.

2. **Bond uniqueness is one-active-per-dungeon**: A dungeon core can only have one active bond (one master at a time). The master entity can also only master one dungeon at a time (`MaxPerOwner: 1` on `dungeon_master` seed). These constraints are enforced by both the seed system and the bond lookup keys.

3. **Master seed archival is configurable**: On bond dissolution, the `dungeon_master` seed is archived (preserving experience for future bonds) or deleted (every new bond starts fresh), controlled by `MasterSeedArchiveOnDissolve` config. Archival is the default because prior mastery experience creates richer gameplay (experienced masters are valuable to new dungeons). In Pattern A, seed archival means the account seed slot is freed but the growth data is preserved for a future bond. In Pattern B, the character-owned seed follows character lifecycle rules.

4. **Corrupted bonds have minimal master agency**: When a monster serves as dungeon master, the monster has limited ability to deliberately grow its `dungeon_master` seed. Growth happens passively through the bond. This is intentional -- corrupted bonds represent domination, not partnership.

5. **No seed-to-seed bonds**: The dungeon_core and dungeon_master seeds are NOT bonded via the seed bond system (BondCardinality: 0). The Contract is the relationship mechanism. Seeds grow independently in parallel, connected by the Contract. This is deliberate -- the dungeon can outgrow its master (Ancient dungeon with Bonded master) or vice versa (Transcendent master of a Stirring dungeon), creating interesting asymmetric dynamics. Cross-pollination between dungeon_master and guardian seeds happens at the spirit/account level, not through seed bonds.

6. **Dungeon personality stored in seed metadata**: Personality type is a permanent characteristic stored in the `dungeon_core` seed's metadata at creation time, not in the dungeon core record. This follows the established pattern of seeds carrying permanent entity characteristics.

7. **Physical form is cross-service, not owned by lib-dungeon**: lib-dungeon owns identity, bond, inhabitants, and memories. Physical layout (rooms, corridors) is owned by Mapping + Save-Load. Visual appearance is owned by Scene. lib-dungeon orchestrates but does not store spatial or visual data directly.

8. **Pattern B is always the default**: Bond formation always creates a character-owned dungeon_master seed (Pattern B). Promotion to Pattern A (account-owned) happens externally through the household split mechanic. lib-dungeon does not need to know which pattern is active -- it interacts with the dungeon_master seed regardless of owner type. The distinction matters to Gardener (Pattern A creates a dungeon garden), the UX capability manifest system (Pattern A has full dungeon UX, Pattern B has transient UX), and the cross-pollination system (Pattern A has persistent cross-pollination, Pattern B has transient).

9. **lib-dungeon is pattern-agnostic**: lib-dungeon itself does not implement or enforce the Pattern A vs. Pattern B distinction. It creates bonds, manages seeds, and orchestrates dungeon mechanics identically in both cases. The pattern distinction is an emergent property of which entity owns the dungeon_master seed (account vs. character) and how the broader system (Gardener, Permission, household management) responds to that ownership.

### Design Considerations (Requires Planning)

1. **Mana economy model**: Should dungeons have their own Currency wallet (enabling NPC economic participation) or use `current_mana` as a virtual resource in actor state? Currency wallet is richer but adds complexity. The planning document leaves this open.

2. **Dungeon garden type design (Pattern A only)**: The dungeon-as-garden concept requires: a registered garden type in Gardener, entity association rules for the dungeon context, ABML action handlers for Gardener APIs (analogous to Puppetmaster's `spawn_watcher:`, `watch:` handlers), and a gardener behavior document for the dungeon core actor. Pattern B does not use a dungeon garden -- the dungeon influence is routed through the character's Actor perception pipeline. This is a cross-service design effort.

3. **Actor type registration**: The dungeon core actor template (event_brain, category: dungeon_core, domain: "dungeon") needs to be registered in the Actor system. This includes the cognition template (`creature_base`), event subscriptions, and capability references. Design decisions: is `creature_base` an existing template or does it need creation?

4. **Growth contribution debouncing**: Dungeons generate many growth events per tick (every monster kill, every trap trigger). Growth contributions to lib-seed need debouncing (configurable, planning doc suggests 5000ms default) to avoid overwhelming the seed service with individual growth API calls.

5. **Memory-to-archive pipeline**: Dungeon memories should feed into the Content Flywheel -- when a dungeon is destroyed or goes dormant, its accumulated memories become generative input for Storyline. This requires design decisions about the compression/archive format and the handoff to lib-resource.

6. **Entity Session Registry for dungeon master (Pattern A only)**: The dungeon master's garden needs entity session registrations (dungeon -> session, inhabitants -> session, master character -> session) via the Entity Session Registry in Connect (L1). This depends on the Entity Session Registry being implemented first (see [Gardener Design #7](GARDENER.md)). Pattern B does not need entity session registration for the dungeon -- the character's existing entity session registrations suffice.

7. **Household split mechanic (cross-cutting dependency)**: Pattern A depends on a general household split mechanic that doesn't exist yet. This mechanic is needed for the game regardless of dungeons (branch families, divorces, exile) and involves Contract (split terms), Faction (cultural norms determining amicability), Obligation (post-split moral costs), Relationship (bond type changes), Seed (potential account seed creation), and Organization (the household IS an organization per Organization.md -- the split is an organization dissolution). lib-dungeon consumes the result of a household split but does not implement it. The split mechanic must be designed as a cross-cutting feature involving multiple services. Pattern B has no dependency on the household split mechanic. Additional implications from cross-service analysis:
   - **Temporal delay**: Arbitration's procedural templates impose timelines (`waitingPeriodDays` governance parameter). Pattern A is not instant -- the split proceeds through filing, service, response, evidence, ruling, appeal window, and enforcement phases. The `dungeon_master` seed promotion (Design Consideration #8) cannot occur until the ruling is enforced. Character death during proceedings, player withdrawal (`WithdrawCase`), and the intermediate state (Pattern B remains active during proceedings) all need design.
   - **Household seed impact**: Per Organization.md Design Consideration #4, the household organization's own seed (type `household`) should lose growth proportional to the departing member. This is a separate concern from the `dungeon_master` seed promotion (#437) -- it requires lib-seed to support growth transfer between seeds.
   - **Disposition consequences**: Remaining household members develop emotional responses via Disposition (resentment, grief, relief). The departed character may develop feelings about their former family (guilt, relief, longing). The guardian spirit's relationship with the departed character changes. These emotional states feed into NPC GOAP decisions and the content flywheel when characters are archived.
   - **Asset division**: Organization.md tracks registered assets (wallets, inventories, locations, contracts). The departing character takes their share per the arbitration ruling's terms, orchestrated through Escrow.
<!-- AUDIT:NEEDS_DESIGN:2026-02-15:https://github.com/beyond-immersion/bannou-service/issues/436 -->

8. **Seed promotion mechanic**: Pattern A requires a mechanism to "promote" a character-owned dungeon_master seed to account-owned. The seed always starts character-owned (Pattern B is the default on bond formation). Promotion happens when the player commits an account seed slot through the household split flow. This requires lib-seed to support re-parenting a seed from one owner type to another (character -> account) while preserving all growth data. Alternatively, the promotion could create a new account-owned seed and transfer growth from the character-owned one. Additional constraints from cross-service analysis:
   - **Timing dependency on #7**: Seed promotion is a consequence of the household split completing (Design Consideration #7), not a standalone operation. The promotion should execute as a prebound API call in the arbitration ruling's enforcement phase. The player's choice initiates the process but does not trigger immediate promotion.
   - **Account seed slot validation**: The account must have an available seed slot (out of the 3 maximum). If all slots are full, Pattern A cannot proceed until the player releases a slot. This validation must occur before the arbitration case is filed, not at enforcement time (to avoid wasted proceedings).
   - **Distinct from household seed splitting**: The `dungeon_master` seed being promoted (owner type change) is separate from the household organization seed being split (growth transfer). Both happen in the same flow but are different seed operations requiring different lib-seed APIs. See Organization.md Design Consideration #4.
   - **Reverse promotion is deliberately unsupported**: Once the character has left the household, they're gone. The reverse (Pattern A back to B) is not possible -- the household split is permanent. This is a deliberate non-requirement.
<!-- AUDIT:NEEDS_DESIGN:2026-02-15:https://github.com/beyond-immersion/bannou-service/issues/437 -->

9. **Pattern B transient UX routing**: When the player switches characters within a garden, the dungeon UX modules need to appear/disappear based on whether the currently-controlled character has a dungeon_master seed. This requires the client's UX capability manifest to be dynamically updated on character switch -- likely via the same Permission/Connect capability manifest push mechanism, extended to include seed-derived UX capabilities.

10. **Dungeon system realm provisioning**: The dungeon system realm (e.g., `DUNGEON_CORES` with `isSystemType: true`) must be seeded on startup, analogous to the divine system realm for gods. Design decisions: Is there one global dungeon system realm or one per game service? What dungeon species are registered (one per personality type, or a generic "dungeon core" species)? How does species assignment map to personality type? The system realm is seeded via `/realm/seed` -- configuration, not code. See [DIVINE.md: Broader System Realm Implications](DIVINE.md#broader-system-realm-implications) for the established pattern.

11. **Character creation timing at Awakened phase**: When `HandleSeedPhaseChangedAsync` detects the transition to Awakened, it must orchestrate: character creation (ICharacterClient), personality trait seeding (ICharacterPersonalityClient, soft), backstory initialization (ICharacterHistoryClient, soft), and actor binding (IActorClient). This is a multi-service orchestration that needs failure handling -- if character creation succeeds but binding fails, the dungeon should retry binding on the next event rather than creating duplicate characters. The characterId stored on DungeonCoreModel serves as the idempotency check.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase.*
