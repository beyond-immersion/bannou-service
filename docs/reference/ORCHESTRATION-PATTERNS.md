# Orchestration Patterns: The Game World's "Main Thread"

> **Scope**: How Bannou's decomposed services form living gameplay loops without a central orchestrator.
> **Sources**: DIVINE.md, DUNGEON.md, BEHAVIORAL-BOOTSTRAP.md, ACTOR-BOUND-ENTITIES.md, VISION.md, PLAYER-VISION.md

---

## The Core Problem

Bannou has 45+ orthogonal services (Storyline composes narratives, Quest tracks objectives, Currency manages economies, etc.) but no service owns the cross-cutting gameplay loop: "character dies -> archive compressed -> god evaluates -> storyline composed -> quests spawned -> new player experiences -> more deaths -> loop." This is the **content flywheel** from VISION.md. It has no `Main()`.

**The solution**: God-actors. Long-running ABML behavior documents executed by the Actor runtime. Orchestration is **authored content** (YAML behavior files), not compiled code. Adding a new gameplay pathway means writing a new behavior document, not a new service. Different gods evaluate the same events differently (Moira/Fate cares about fulfillment, Ares/War cares about combat). The Actor runtime's existing infrastructure (perception queues, GOAP planning, variable providers, pool scaling) handles execution.

**Why not a "gameplay loop" plugin**: It would centralize logic that varies per deity/realm/game, become a dependency magnet collapsing the service hierarchy, require code changes for every new orchestration pattern, and violate "Emergent Over Authored" (North Star #5).

---

## The Actor-Bound Entity Pattern (Unified)

Any entity that grows from inert object to autonomous agent follows these three cognitive stages:

```
Stage 1: DORMANT (No Actor)
  Entity = seed + physical form (item, location, nothing). No runtime cost.
  Growth accumulates passively from game events.
  Thousands can exist per world simultaneously.

Stage 2: EVENT BRAIN (Actor, No Character)
  Trigger: seed reaches Stirring phase -> Puppetmaster spawns event brain actor.
  Actor runs ABML behavior. ${personality.*} resolves to null -> instinct defaults.
  Entity perceives, decides, acts -- but without rich cognitive data.
  Variable providers: ${seed.*}, domain-specific volatile state only.

Stage 3: CHARACTER BRAIN (Actor + Character in System Realm)
  Trigger: seed reaches Awakened phase -> handler creates Character in system realm,
    calls POST /actor/bind-character. NO actor relaunch needed.
  Full L2/L4 variable providers activate on next tick:
    ${personality.*}, ${encounters.*}, ${backstory.*}, ${quest.*},
    ${world.*}, ${obligations.*}, plus domain-specific providers.
  Same ABML behavior document -- null-safe expressions now return real data.
  Entity has personality, memories, grudges, aspirations. A living thing.
```

**System realms** (`isSystemType: true`) give non-physical entities character records without polluting physical realm queries: PANTHEON (gods), DUNGEON_CORES (dungeons), SENTIENT_ARMS (weapons), UNDERWORLD (dead characters), NEXIUS (guardian spirits).

---

## Bootstrap Sequence (How the Loop Starts)

```
Server startup (all plugins loaded)
    |
Phase 1: Seeded behaviors loaded into Resource (L1)
    puppetmaster-manager.abml, gardener-manager.abml,
    per-deity behavior templates (moira-fate.abml, etc.)
    |
Phase 2: Singleton manager actors spawned
    Puppetmaster plugin -> IActorClient.SpawnAsync(puppetmaster-manager.abml)
    Gardener plugin    -> IActorClient.SpawnAsync(gardener-manager.abml)
    |
Phase 3: Managers initialize deities via Divine API
    For each deity template: /divine/deity/get-by-code or /divine/deity/create
    Divine creates deity + character in PANTHEON system realm + divinity wallet + seed
    |
Phase 4: God-actors spawned per realm
    Puppetmaster Manager: for each realm x deity with relevant domains:
      /actor/spawn { templateCode: "regional_watcher", characterId: deity.characterId }
    Gardener Manager: on player connect:
      Assigns/spawns gardener god-actor for the player's session
    |
Phase 5: Steady state
    Puppetmaster gods: perceive world events, evaluate archives,
      commission storylines, spawn quests, grant blessings
    Gardener gods: tend player experiences, spawn POIs, manage scenarios,
      route spirit influences, adapt difficulty
    Managers: health monitoring, restart, load balance, scaling
```

**God-actor count at scale**: ~50 puppetmaster gods (6 deities x 8 realms) + ~200 gardener gods (1 per 50 players) = ~250 actors alongside 100,000 NPCs = 0.25% overhead.

---

## The Content Flywheel in Motion

```
Step  Actor/Service          Action                          API Called
----  -------------------    ----------------------------    -------------------------
1     Character-Lifecycle    Character dies                  (publishes lifecycle.death)
2     Resource (L1)          Compresses archive              (publishes resource.compressed)
3     Moira (god-actor)      Perceives archive, GOAP eval    (internal)
4     Moira                  Archive interesting -> compose   POST /storyline/compose
5     Moira                  StorylinePlan received           (response processing)
6     Moira                  Phase 1 -> quest                POST /quest/definition/create
7     Moira                  Spawn ghost NPC from archive    POST /actor/spawn
8     Moira                  Create narrative items           POST /item/instance/create
9     Moira                  Bless character to discover      POST /divine/blessing/grant
10    Player                 Encounters quest, plays, dies    (loop continues)
```

Each arrow between services is a god-actor's ABML behavior calling a service API. Different gods produce different narratives from the same archive. The loop is driven by data, not code.

---

## Divine: God Identity & Economy

**What it owns**: Deity identity, divinity economy, blessing orchestration, follower management.
**What it composes**: Currency (divinity wallets), Seed (domain power), Relationship (followers, rivalries), Collection (permanent blessings), Status (temporary blessings), Puppetmaster (actor lifecycle).

**God-actors are character brains** bound to divine system realm characters. This gives them `${personality.*}`, `${encounters.*}`, `${backstory.*}` for free via standard variable providers. A god's ABML behavior references `${personality.mercy}` to decide intervention thresholds, `${encounters.last_hostile_days}` for grudges. No custom `IDivineVariableProviderFactory` needed for god self-data.

**Avatar manifestation**: Gods can manifest physical-realm avatars via `/divine/avatar/manifest`. Costs divinity (scales with recency of last avatar death). Creates a separate Character + separate Actor in a physical realm. God's binding to its divine character is permanent and unaffected. Avatar death feeds the content flywheel. Economy layer prevents spam.

**Blessing tiers**: Minor/Standard = temporary (Status Inventory), Greater/Supreme = permanent (Collection). Divinity cost per tier is configurable. Blessings are entity-agnostic (characters, accounts, deities can all receive them).

**Gardener integration**: The same god-actor tending a physical realm region also tends player garden spaces. Whether the "space" being tended is a forest region or a void lobby is a behavioral distinction in the ABML document, not a structural difference. Realm-tending and garden-tending are two sides of one coin.

---

## Dungeon: Living Spatial Entities

**What it owns**: Dungeon core identity, bond management, inhabitants, memories.
**What it composes**: Seed (dungeon_core + dungeon_master growth), Currency (mana), Contract (master bond), Actor (cognitive runtime), Character (system realm identity at Stage 3), Mapping/Scene/Save-Load (physical form), Puppetmaster (actor lifecycle), Gardener (Pattern A).

**Cognitive progression** follows the unified 3-stage pattern:
- Dormant (seed < 10.0): no actor, passive growth, thousands can exist cheaply
- Stirring (seed >= 10.0): event brain actor spawned, instinct-driven ABML
- Awakened (seed >= 50.0): character created in DUNGEON_CORES realm, actor binds, full personality
- Ancient (seed >= 200.0): rich inner life, personality evolution, memory manifestation

**Dual mastery patterns** (who owns the dungeon_master seed):
- **Pattern B (default)**: Character-owned seed. Dungeon layers onto gameplay while controlling bonded character. No account seed slot consumed. Dungeon UX appears/disappears on character switch.
- **Pattern A (full split)**: Account-owned seed after household split. Dungeon IS the garden, selectable from void. Consumes 1 of 3 account seed slots. Character leaves household permanently.

lib-dungeon is pattern-agnostic -- it doesn't know or care which pattern is active. The distinction emerges from seed owner type and how Gardener/Permission respond to it.

**Bond types**: Priest (direction + mana, seed archived on death), Paladin (combat channeling, growth halved on death), Corrupted (monster dominated, Pattern B only, minimal agency).

**Memory system**: Collection for permanent knowledge ("has experienced first adventurer death") + Inventory for consumable creative resources (memory items spent on manifestation as paintings, items, environmental echoes). Growth to `memory_depth.*` seed domain.

**Dungeon spawning**: Not random -- a regional watcher god (e.g., Typhon/Monsters) detects mana stagnation via Environment/Worldstate events, evaluates location characteristics, and calls `/dungeon/create` with personality type derived from formative conditions. Pure ABML behavior authoring.

---

## Living Weapons: Zero-Plugin Validation

Living weapons require **no new Bannou plugin**. They compose entirely from: Item (physical form), Seed (`sentient_weapon` type), Collection (permanent knowledge), Actor (behavior), Character (SENTIENT_ARMS system realm at Awakened), Relationship (`weapon_wielder` bond), Status (wielder effects).

Same 3-stage progression: Dormant (fine sword with passive bonuses) -> Stirring (wakes up, sends impulses to wielder) -> Awakened (speaks, advises, has opinions, refuses unworthy wielders) -> Legendary (centuries of memory, personality evolution across many wielders).

**Why no plugin**: Dungeons need orchestration APIs (spawn monster, activate trap, seal passage, manifest memory) that compose multiple services atomically. Weapons need only single-API calls that the game engine/SDK coordinates. Every weapon operation maps to one existing endpoint.

**Key design principle**: The weapon chooses, not the wielder. `${personality.pride}` + `active.refuse` capability = emergent scarcity where finding a sentient weapon doesn't mean it accepts you.

---

## Interaction Patterns Summary

| Pattern | Mechanism | Example |
|---------|-----------|---------|
| **God -> Content flywheel** | God perceives archive event, GOAP evaluates, calls Storyline/Quest/Actor APIs | Moira composes ghost quest from death archive |
| **God -> Player experience** | Gardener god tends player garden, spawns POIs, routes spirit influences | Moira adjusts void scenario offerings based on player profile |
| **God -> NPC behavior** | God injects perceptions into NPC actors via Actor perception API | Ares amplifies aggression in nearby warriors before battle |
| **God -> Economy** | God monitors velocity via Analytics, spawns intervention events | Hermes drops treasure near stagnating economy |
| **God -> Deity self** | God calls Divine API for blessings, divinity spending, follower management | Silvanus blesses a druid who pleased him |
| **God -> Avatar** | God calls `/divine/avatar/manifest` to enter physical world | Moira manifests as "The Veiled Oracle" |
| **Dungeon -> Combat** | Dungeon core actor spawns monsters, activates traps via ABML actions | Ancient dungeon deploys tailored defense based on ${encounters.*} grudges |
| **Dungeon -> Master** | Core injects perceptions into master's character Actor, gated by seed capabilities | Master perceives threat level via `perception.tactical` capability |
| **Weapon -> Wielder** | Weapon actor emits perceptions to wielder's Actor via `emit_perception:` | Legendary blade warns of danger via `active.advise` capability |
| **Seed phase -> Cognitive transition** | `ISeedEvolutionListener` fires, triggers actor spawn or character binding | Dungeon seed hits Awakened -> character created, actor binds |

---

## Why This Matters for Development

**When implementing any L4 service**, ask: "Does a god-actor need to interact with this?" If a service produces events that affect the game world (deaths, economy changes, faction shifts, environmental changes), god-actors will consume those events and compose them into gameplay. Design event schemas with god-actor consumption in mind.

**When designing new entity types**, ask: "Does this follow the actor-bound entity pattern?" If something should progressively awaken (sentient ships, haunted buildings, awakened forests), it uses the same 3 stages with a system realm, seed type, and ABML behavior -- potentially with zero new plugins.

**When writing ABML behaviors**, remember that god-actors call service APIs through standard ABML action handlers. The same `call:` syntax that NPCs use for local actions, gods use for world-spanning orchestration. The Actor runtime treats them identically.

**The loop is invisible in code** because it lives in behavior documents, not in any service's implementation. To understand the gameplay loop, read the ABML behaviors in Resource's seeded data and Asset's uploaded behaviors -- not the service code. The services are the piano keys; the behaviors are the music.

---

*For full specifications: [DIVINE.md](../plugins/DIVINE.md), [DUNGEON.md](../plugins/DUNGEON.md), [BEHAVIORAL-BOOTSTRAP.md](../guides/BEHAVIORAL-BOOTSTRAP.md), [ACTOR-BOUND-ENTITIES.md](../planning/ACTOR-BOUND-ENTITIES.md)*
