❯ Big Brain Mode! Read DUNGEON and PROCEDURAL deep dives yourself- launch 2 agents to summarize the: PUPPETMASTER, DIVINE, GARDENER deep dives, and the BEHAVIORAL-BOOTSTRAP.md guide.
Launch ONE agent to summarize the 3 dungeon system design docs in ~/repos/arcadia-kb/04 - Game Systems/ (ensure you launch that one with permissions to actually see into another
repo-  or do a 1 line read to ensure you trigger the authorization first before launching them). That's your knowledgebase- don't bother with anything more (and no code, for you or
the agents). Let's discuss dungeon specifics- in particular, what kinds of things we should be aiming to handle for Arcadia's dungeon implementations. Let's dig DEEP. What kinds of
aspirations do we have later with dungeons? Do any of these aspirations require additional design considerations? Let's assume there's a "god" who is responsible for actually
spawning dungeons in the world when the conditions are right (the task that turns "large amounts of stagnant mana" into "a manifestation of some other place or reality or state of
being"), the one who creates the cores, which then later spawn or are bound to masters. What things are covered from the aspirations already? Dungeon cores are essentially seeds,
they exert influence THROUGH the pair bond at first + mechanical automatic reactions (built-in- "I'm in danger, ALERT! ACTIVATE NEARBY TRAPS!", and spitting out monsters that it can
generate from the materials it has collected (DNA/logos) / blueprints that it has, like a factory. We could even potentially UTILIZE the factory plugin (perform actions
automatically over time, more efficient than actors) as a part of this. As the seed grows, maybe one of the things built-into dungeon seeds is that growth hitting a certain point in
some levels creates a collection item for "has behavior", and then this can launch an actor (no character associated, running a dungeon core "behavior"), and that dungeon core
behavior would start to use the seed variable provider data itself as the basis for its behaviors (not a character personality the way a person would), as well as the current state
of dungeon functions and its "bonded partner". If the dungeon's bonded partner is a character, then it can use exactly the same avenues "gods" do for "whispering" to the character's
actor, querying it, giving it instructions. The dungeon actor would be able to set and activate traps with more precision, choose creatures to spawn more intelligently, and more.
Once the growth hits a certain point, it could even be possible for the dungeon to spawn a CHARACTER identity (simple API call to create a character) which would then be tied to
their core/seed all of that retroactively. This would then allow, just like the gods who can have character identities in their own "pantheon" system region, a similar system region
for just the subset of 'living dungeons', so they won't show up in normal character lists but can still provide the personality data, history, etc which enriches the actor runner.
Now that it would have a characterId it could attach to, the behavior it runs would be capable of getting and setting all sorts of data- the game server would of course need to
start publishing perception events for the new character (needs to "spawn" in the game), but as long as we have appropriate service eventing, that's not a problem. What else is
there? Oh yeah, the dungeon developing new items and momentos and portait drops based on events that occurred in it. I initially had the thought that we might want to add a new
history plugin just for the dungeon plugin, but now I actually don't think we need something so long-term, more, I think we should consider how the seed is going to "grow" based off
of some of these same events that later trigger other items and events. In that case, why don't we just itemize the memories and store them in an inventory- notably, NOT a
collection- the seed uses a collection but collections can't have items removed from them, by design, where as an inventory you get essentially X uses. The seed collection could
still just be essentially the same types of items, but only storing "first time" instead of generating consumables. Whenever a notable event would happen, it would get stored as a
"notable" item ("event" item, "historical" item?) or something of that nature, and a copy of that would get set to the seed, if it hasn't been already, and a copy would need to be
stashed away in the "memory inventory" for the dungeon instance. These could then get consumed when generating loot later, or for manifesting some kind-of phenomena, but the point
being that they're consumed on use, so the dungeon can only create as many unique items as it actually has unique historical events stored. Does this make sense at all? Let me know
what you think about the shape that starts taking. Then there's the dungeon aspects with like "floors", where you go down a level and the whole environment changes- I had the idea
this should essentially be the dungeon trying to create the ideal environment for the types of defenses that it wants- a desert floor where the defense is heat + cold environmental
extremes + distance to travel (resource exhaustion), or a jungle to support plant-type defense mechanism, or simply as food to support the larger creatures. Monsters created from
magic are dumb, and limited in number based on magic, but if you create the right environment, you can create a habitat to support ACTUAL creature defenses- while new creatures
couldn't be "spawned" as easily, having to start as eggs / babies that grow quickly, that would be one path to being more economical over time, and then hiding the entrance to the
next floor could become a part of the complexity. Each floor would be its own location in the dungeon "system realm", like any dungeon character identities- these are essentially
"other worlds" in the both figurative AND literal sense. Of course, the growth of a dungeon that hasn't obtained an actor yet, much less its own personality, would be pretty
limited- we could even say the ability to create a deeper "floor" is an ability unlocked by HAVING an actor, so that we don't need to worry about any of the complex there without
having an agent driving it (nothing we need to build into the dungeon plugin itself).

---
Dungeon Design Deep Dive

You've laid out a remarkably coherent vision here. Let me work through what the architecture already covers, where your new ideas extend it brilliantly, and where there are genuine
design considerations to think through.

What's Already Covered

The existing deep dives and arcadia-kb docs handle a LOT of the foundational shape:

- Dungeon cores as seeds: Fully designed (dungeon_core seed with 5 growth domains, 4 phases, 11+ capability rules)
- Bond patterns: Priest/Paladin/Corrupted with Pattern A (account-level) and Pattern B (character-level)
- Cognition pipeline: Simplified 5-stage creature_base template (Filter Attention → Memory Query → Capability Check → Master Communication → Intention Formation)
- Memory capture/manifestation: Significance-scored event capture with item/scene/environmental manifestation forms
- Variable providers: 4 factories (${seed.*}, ${dungeon.*}, ${master.seed.*}, ${master.*})
- Physical construction: Cross-service (Mapping for spatial, Scene for visual, Save-Load for persistence, Procedural for generation)
- God influence pattern: Same indirect-through-Actor pattern as Divine

The "Dungeon-Spawning God"

This fits perfectly into the existing divine architecture. One of the 18 Old Gods (Typhon/Monsters is the obvious candidate, but you could also frame it as a more neutral force -- a god
of Transformation or Stagnation-Breaking) would be a regional watcher whose ABML behavior includes:

perception_filter:
- event: "environment.mana_density.threshold"
    conditions:
    - check: "${event.density} > ${divine.stagnation_threshold}"
    - check: "${event.duration_days} > ${divine.minimum_stagnation_days}"

The god would:
1. Monitor mana accumulation via Environment/Worldstate events
2. Evaluate the location's characteristics (ley line proximity, existing dungeon density, population)
3. Decide what kind of dungeon to create based on the formative conditions (from the Nageki framework -- a battlefield saturated with combat pneuma → martial dungeon, a site of mass
grief → memorial dungeon)
1. Call /dungeon/create with personality type derived from the formative event
2. The dungeon core gets provisioned (seed + mana wallet) but starts Dormant -- purely reactive

No additional service design needed. This is just an ABML behavior document for the spawning god.

The Nageki docs add an important layer here: vault level classification (1-10) based on pneuma density. The spawning god doesn't just decide IF a dungeon forms, but at what intensity. A
Level 1 dungeon from mild mana accumulation vs a Level 8 from a catastrophic battlefield. This could map to the dungeon_core seed's initial growth -- higher-level formations start with
more initial growth in mana_reserves, giving them earlier access to capabilities.

Workshop as Production Factory -- This Is Great

This is one of the most elegant ideas in your outline. Workshop already provides:
- Time-based automated production with lazy evaluation
- Worker slot assignment to blueprints
- Source inventory consumption → output placement
- Piecewise rate segments for accurate tracking across rate changes
- Background materialization worker with fair scheduling

For dungeons, this maps to:

┌──────────────────┬────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ Workshop Concept │                                                           Dungeon Equivalent                                                           │
├──────────────────┼────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Blueprint        │ Creature spawning template (species + quality tier)                                                                                    │
├──────────────────┼────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Workers          │ Mana channels (abstract slots, scaled by mana_reserves seed growth)                                                                    │
├──────────────────┼────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Source inventory │ Genetic library (logos seeds) -- consumed on use for pneuma echoes, or NOT consumed for habitat creatures that just need the blueprint │
├──────────────────┼────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Output inventory │ Room-specific inhabitant containers                                                                                                    │
├──────────────────┼────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Production rate  │ Modified by dungeon_core seed growth in genetic_library.{species}                                                                      │
└──────────────────┴────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘

The key distinction you're making between pneuma echoes (instant mana-cost spawn, dumb, limited by mana) and habitat creatures (Workshop-produced, grow over game time, smarter, more
economical long-term) is mechanically rich:

- Pneuma echoes: Direct spawn via the existing SpawnMonsterHandler ABML action. Instant. Costs mana. Dies and disperses, logos seed remains. The current deep dive's inhabitant system.
- Habitat creatures: Workshop blueprints assigned to a floor's environment. Eggs/babies placed by the production system, grow over game time via Workshop's lazy evaluation. Once mature,
they're real entities -- potentially with their own simple actors for complex species. Cheaper in mana but takes real game-time.

Design consideration: Workshop is L4, dungeon is L4, so this is a soft dependency with graceful degradation. If Workshop isn't enabled, dungeons can only spawn pneuma echoes. If Workshop
IS enabled, the dungeon actor can assign production blueprints strategically.

The only extension Workshop might need: support for non-character "worker" concepts. Currently Workshop tracks workers as entities with proficiency. Dungeon mana channels are abstract.
Either we model mana channels as a special worker type, or we allow Workshop to accept abstract worker counts without entity backing. Worth exploring but not a blocker -- the simplest
path is mana channels as opaque worker entities.

Seed Growth → Collection → Actor Launch Pipeline

Your progression model is cleaner than the existing deep dive's "optionally start actor at creation" approach:

Phase 1: Dormant Core (No Actor)
- Dungeon exists as pure seed + mana wallet + mechanical reactions
- Built-in reactive responses: intrusion → alert nearby traps, activate automated defenses
- Growth happens from events (kills within domain, mana accumulation)
- Workshop handles routine creature production if enabled
- This is the dungeon's "unconscious" phase

Phase 2: Seed Growth → Collection Item → Actor Spawn
- dungeon_core seed reaches Stirring phase (MinTotalGrowth: 10.0)
- ISeedEvolutionListener fires in lib-dungeon
- Dungeon grants a Collection entry: type dungeon_capability, code has_behavior
- ICollectionUnlockListener fires → dungeon plugin creates the actor via Puppetmaster
- Now the dungeon has an event brain running the creature_base cognition template
- Uses ${seed.*} and ${dungeon.*} variable providers for ABML expressions

This is beautiful because:
- No actor overhead for the majority of dungeons (most are small, dormant, Levels 1-3)
- The transition is a real progression event that the world notices
- It uses existing infrastructure (Seed → Collection → Listener pipeline) with zero new services
- The mechanical reactions in Phase 1 could literally be Workshop + simple event handlers, no ABML needed

Phase 3: Character Identity (The System Realm Promotion)

This is where your vision gets really interesting and intersects directly with the Divine pattern.

When dungeon_core seed reaches Awakened phase (MinTotalGrowth: 50.0):

1. Create a Character record in the UNDERWORLD system realm (isSystemType: true)
2. Assign the "Dungeon Core" species within that system realm
3. Link the dungeon entity to the character via Relationship
4. Rebind the actor from event brain → character brain

Once the dungeon has a characterId:

┌───────────────────────────┬─────────────────────────────────────────────────────────────────────────────┐
│          Service          │                            What the Dungeon Gets                            │
├───────────────────────────┼─────────────────────────────────────────────────────────────────────────────┤
│ Character (L2)            │ Identity, lifecycle state                                                   │
├───────────────────────────┼─────────────────────────────────────────────────────────────────────────────┤
│ Species (L2)              │ "Dungeon Core" species with trait modifiers                                 │
├───────────────────────────┼─────────────────────────────────────────────────────────────────────────────┤
│ CharacterPersonality (L4) │ Quantified personality axes -- aggression, cunning, humor, patience         │
├───────────────────────────┼─────────────────────────────────────────────────────────────────────────────┤
│ CharacterHistory (L4)     │ Backstory elements (formative event, notable battles, master relationships) │
├───────────────────────────┼─────────────────────────────────────────────────────────────────────────────┤
│ CharacterEncounter (L4)   │ Memories of adventurer interactions, grudges, favorites                     │
├───────────────────────────┼─────────────────────────────────────────────────────────────────────────────┤
│ Actor (L2)                │ Full NPC cognition with automatic variable provider binding                 │
└───────────────────────────┴─────────────────────────────────────────────────────────────────────────────┘

This is the EXACT same pattern Divine uses for gods. The dungeon core was an event brain (using load_snapshot: for ad-hoc data). Now it's a character brain with automatic
${personality.*}, ${encounters.*}, ${backstory.*} providers. The behavior document could even be the SAME document -- it just gains access to richer variable namespaces as the dungeon
matures.

The master bond communication at this stage becomes exactly what gods do: the dungeon's actor can "whisper" to the bonded master's character actor via perception injection. The dungeon
has opinions, memories, personality. It genuinely develops a relationship with its master.

Memory System: Inventory vs Collection

Your insight here is architecturally sharp. Let me lay out the dual-system:

Collection (permanent record, "knowledge"):
- First combat victory in the dungeon → Collection entry first_combat_victory
- First boss kill → Collection entry first_boss_kill
- First death of an adventurer → Collection entry
- These CANNOT be removed or consumed -- they're the dungeon's permanent knowledge
- They feed Seed growth to memory_depth.capture
- They serve as the "set" of experiences the dungeon has had (akin to logos completion from the Nageki docs)

Inventory (consumable creative resources, "inspiration"):
- Every notable event creates a "memory item" in the dungeon's memory inventory
- These items have custom stats: significance score, event type, participants, emotional context
- They're CONSUMABLE -- when the dungeon manifests something, it spends the memory item
- Stacking for similar event types (5x "combat victory" memory items)
- Inventory capacity scales with memory_depth seed growth
- When consumed for manifestation, the dungeon creates:
- A unique loot item (via lib-item, provenance from memory data)
- A painting/mural (via lib-scene, style from personality)
- An environmental echo (via lib-mapping/environment)
- A phantom replay (via inhabitant spawn, special type)

Why this is better than the existing DUNGEON deep dive's custom memory MySQL store:
- Memory items get the full Item system benefits (templates, instances, rarity, custom stats, queries)
- Inventory constraints provide natural limits (no unbounded growth)
- Standard item movement, query, and management APIs
- The dungeon could even TRADE memory items (logos trading from Nageki!) with other dungeons
- Memory manifestation is just "consume item, create output" -- a pattern Workshop/Craft already understand

The seed-collection side is the "permanent" version. The Collection entry for "first_combat_victory" never goes away. Even if the dungeon consumes all its combat victory memory items
from inventory, it still KNOWS what a combat victory is. The next combat victory generates a new memory item. The collection tracks what the dungeon has experienced (logos knowledge).
The inventory tracks what the dungeon has available to manifest (creative fuel).

Floor System: Locations as Environments

Each floor as a Location in the dungeon hierarchy is architecturally clean:

UNDERWORLD (System Realm)
└── Thornhold (Location, depth: 0, "dungeon root")
    ├── Floor 1: Fungal Caves (Location, depth: 1)
    │   ├── Entry Hall (Location, depth: 2)
    │   ├── Mushroom Garden (Location, depth: 2)
    │   └── Hidden: Mycelium Network (Location, depth: 2, discoverable)
    ├── Floor 2: Scorched Wastes (Location, depth: 1)
    │   ├── Sandstorm Corridor
    │   └── Mirrage Oasis (trap)
    └── Floor 3: Deep Canopy (Location, depth: 1)
        ├── Root Bridges
        └── Predator Nests

Each floor integrates with:

┌──────────────────┬──────────────────────────────────────────────────────────────────────┐
│     Service      │                            Role per Floor                            │
├──────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Location (L2)    │ Hierarchy, parent-child, depth tracking                              │
├──────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Environment (L4) │ Temperature, humidity, weather, ecological resources                 │
├──────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Transit (L2)     │ Connections between rooms, hidden passages, stairways between floors │
├──────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Mapping (L4)     │ Spatial boundaries, room geometry, affordance queries                │
├──────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Scene (L4)       │ Visual composition, memory manifestation decorations                 │
├──────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Procedural (L4)  │ Generate floor geometry via HDA templates                            │
└──────────────────┴──────────────────────────────────────────────────────────────────────┘

The dungeon actor's strategic choice of what environment to create is the design richness:

┌─────────────────────────────┬──────────────────────────┬──────────────────────────────────────────────────────────────────────┐
│      Defense Strategy       │    Floor Environment     │                               Mechanic                               │
├─────────────────────────────┼──────────────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Resource exhaustion         │ Desert/tundra extreme    │ Heat/cold damage over time, long distances, scarce water             │
├─────────────────────────────┼──────────────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Ambush/concealment          │ Dense jungle/forest      │ Line-of-sight blocking, plant-based traps, camouflaged creatures     │
├─────────────────────────────┼──────────────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Habitat for large creatures │ Savanna/grassland        │ Open spaces for large predators, food chain support                  │
├─────────────────────────────┼──────────────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Aquatic advantage           │ Flooded caves/underwater │ Submersion hazards, amphibious creature advantage, current mechanics │
├─────────────────────────────┼──────────────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Magical suppression         │ High pneuma density zone │ Mana interference, spell disruption, anti-magic effects              │
├─────────────────────────────┼──────────────────────────┼──────────────────────────────────────────────────────────────────────┤
│ Puzzle/misdirection         │ Shifting architecture    │ Transit connections that change, rooms that rotate, false exits      │
└─────────────────────────────┴──────────────────────────┴──────────────────────────────────────────────────────────────────────┘

Gating floor creation behind actor existence is clean design:
- Pre-actor dungeons are single-floor, single-environment
- The create_floor capability requires both domain_expansion.rooms threshold AND having an active actor
- The actor's behavior document contains the strategic logic for choosing floor types
- lib-procedural generates the geometry parameterized by environment type
- This means ALL complex floor management is actor-driven, nothing the dungeon plugin needs to handle autonomously

Aspirations That Need Additional Design

Here's what I think genuinely requires new design beyond what's already specified:

1. The Event Brain → Character Brain Transition

When the dungeon creates a Character identity at Awakened phase, the actor needs to rebind. This isn't a pattern anyone has built yet. Options:
- Kill the event brain actor, spawn a new character brain actor (loses in-flight state)
- Hot-swap the actor's binding type (requires ActorRunner changes)
- Design the behavior document to work with BOTH binding types (flexible variable references that degrade gracefully when character providers aren't available)

The third option is probably best and aligns with how the existing behavior system works (variable providers are discovered via DI; if personality providers aren't registered,
expressions referencing ${personality.*} gracefully return null/default).

2. Workshop Integration for Habitat Creatures

Workshop expects specific worker entities and material inventories. Dungeon mana channels and genetic libraries are abstract concepts. Two approaches:
- Adapter pattern: Dungeon plugin registers Workshop blueprints and manages the translation layer between dungeon concepts and Workshop's API
- Virtual workers: Workshop accepts abstract worker counts without entity backing (simpler but requires Workshop schema change)

I'd lean toward the adapter pattern since it doesn't require modifying Workshop at all.

3. Logos Completion Tracking (from Nageki)

The Nageki framework adds a hidden progression system for creature spawning quality:
- Base echoes from partial logos
- Beta echoes at 50%/75% completion per species
- Alpha echoes at 100% completion (full logos set)
- Apex echoes from cross-species mastery

This maps naturally to the genetic_library.{species} seed subdomain. The growth value already represents "how much logos the dungeon has absorbed for this species." You'd add capability
rules like:

┌─────────────────────┬─────────────────────────────┬───────────┬─────────────────────────────────────────┐
│     Capability      │           Domain            │ Threshold │               Description               │
├─────────────────────┼─────────────────────────────┼───────────┼─────────────────────────────────────────┤
│ spawn_monster.beta  │ genetic_library.{species}   │ 10.0      │ 50% completion, enhanced echoes         │
├─────────────────────┼─────────────────────────────┼───────────┼─────────────────────────────────────────┤
│ spawn_monster.alpha │ genetic_library.{species}   │ 20.0      │ Full completion, perfect echoes         │
├─────────────────────┼─────────────────────────────┼───────────┼─────────────────────────────────────────┤
│ spawn_monster.apex  │ genetic_library (aggregate) │ 50.0      │ Cross-species mastery, chimera creation │
└─────────────────────┴─────────────────────────────┴───────────┴─────────────────────────────────────────┘

No new service needed -- this is just seed capability configuration.

4. Dungeon-to-Dungeon Communication

The mega-dungeon concept (multiple cores coordinating) and logos trading between dungeons both need actor-to-actor communication. Puppetmaster's "Actor-to-Actor Communication Commands"
(ABML actor_command: and actor_query:) are designed for exactly this but are blocked on watcher-actor integration (GH #388). Once that's implemented, dungeons could communicate through
the same channels.

5. The Content Flywheel Integration

When a dungeon is destroyed or goes permanently dormant, its accumulated data should feed the flywheel:
- Character history (from the system realm Character, if it reached that stage)
- Memory inventory contents (all unused memory items → compressed archive)
- Encounter records (who visited, what happened)
- All of this → lib-resource compression → archive
- Regional watcher gods evaluate the archive → Storyline generates narrative seeds
- Future dungeons or quests reference the dead dungeon's legacy

This is already covered by the existing Resource/Character compression patterns. The only design work is ensuring dungeon Character entities participate in the standard compression
pipeline.

Summary of What's Covered vs What Needs Design

┌───────────────────────────────────────────────────────────┬───────────────────────────────────────────────────────────────────────┐
│                        Aspiration                         │                                Status                                 │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Spawning god creates dungeons from stagnant mana          │ Covered -- ABML behavior for a deity regional watcher                 │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Dungeon cores as seeds with progressive growth            │ Covered -- dungeon_core seed fully designed                           │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Mechanical reactions before actor exists                  │ Covered -- Workshop + event handlers                                  │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Workshop as creature production factory                   │ Needs adapter design -- mapping Workshop concepts to dungeon concepts │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Seed → Collection → Actor launch pipeline                 │ Covered -- existing Seed/Collection/Listener infrastructure           │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Dungeon actor using seed variable providers               │ Covered -- 4 variable provider factories designed                     │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Master bond communication (same as gods)                  │ Covered -- CommunicateMasterHandler, perception injection             │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Character identity at high growth (system realm)          │ Needs design -- system realm creation, actor rebinding pattern        │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Memory as inventory (consumable) + collection (permanent) │ Needs design -- dual-system replacing custom memory store             │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Floor/level system as Location hierarchy                  │ Covered -- Location + Environment + Transit + Procedural              │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Environment-based defense strategy                        │ Covered -- Environment service configured per floor                   │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Actor-gated floor creation                                │ Clean design constraint -- no implementation work, just enforcement   │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Logos completion tracking (Alpha/Beta/Apex)               │ Covered -- seed capability rules per species subdomain                │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Habitat creatures via Workshop                            │ Needs design -- Workshop blueprint adapter                            │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Dungeon-to-dungeon communication                          │ Blocked on GH #388 (watcher-actor integration)                        │
├───────────────────────────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
│ Content flywheel integration (dungeon archives)           │ Covered -- standard Resource compression pipeline                     │
└───────────────────────────────────────────────────────────┴───────────────────────────────────────────────────────────────────────┘

The shape you're describing is remarkably well-supported by the existing architecture. The major new design work is:
1. The UNDERWORLD system realm and actor rebinding pattern
2. The dual memory system (Collection + Inventory replacing custom store)
3. Workshop adapter for habitat creature production

Everything else is configuration, ABML behavior authoring, and seed type definition.
