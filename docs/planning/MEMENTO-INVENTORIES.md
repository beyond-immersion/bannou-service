# Memento Inventories: Location-Based Spiritual Ecology

> **Status**: Design
> **Created**: 2026-02-18
> **Author**: Lysander (design) + Claude (analysis)
> **Category**: Cross-cutting mechanic (behavioral, not plugin)
> **Related Services**: Item (L2), Inventory (L2), Location (L2), Actor (L2), Character-History (L4), Character-Encounter (L4), Character-Personality (L4), Agency (L4), Collection (L2), Seed (L2)
> **Related Plans**: [DEATH-AND-PLOT-ARMOR.md](DEATH-AND-PLOT-ARMOR.md), [CINEMATIC-SYSTEM.md](../plans/CINEMATIC-SYSTEM.md), [LOGOS-RESONANCE-ITEMS.md](LOGOS-RESONANCE-ITEMS.md)
> **Related Docs**: [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md), [VISION.md](../reference/VISION.md)
> **Related Issues**: [#164](https://github.com/beyond-immersion/bannou-service/issues/164) (Item Removal/Drop Behavior), [#274](https://github.com/beyond-immersion/bannou-service/issues/274) (Location ground containers)
> **Related arcadia-kb**: Underworld and Soul Currency System, Dungeon System

---

## Executive Summary

Every death leaves a mark. Every battle scars the earth. Every act of love, betrayal, creation, or destruction imprints itself on the place where it happened. Memento inventories generalize the dungeon's memory inventory pattern -- already designed as a dual Collection/Inventory system for dungeon cores -- to **all locations in the game world**. Each location accumulates memento items generated from real gameplay events: deaths, battles, emotional moments, masterwork creations. These mementos contain compressed real data from real characters and real events, not authored content.

Characters with the right skills interact with this spiritual residue:

- **Necromancers** consume death mementos to summon spirits or raise ghouls built from the dead character's actual personality and capabilities
- **Mediums** read mementos to hear whispers from the dead -- fragments of real final thoughts, real secrets, real unfinished business
- **Historians** reconstruct what actually happened at a location from event mementos
- **Detectives** analyze death and emotional mementos as forensic evidence in genuine (not scripted) investigations
- **Craftsmen** imbue creations with the spiritual weight of real events
- **Dungeon cores** absorb mementos from locations within their expanding domain

This requires **zero new services, zero new plugins**. It composes entirely from Item (templates and instances), Inventory (containers), Location (spatial anchoring), Actor (spirit/ghoul spawning), and ABML behavior documents. The game client and server handle the interaction UX. The architecture already supports everything described here.

The consequence: **locations develop spiritual character through simulation, not authoring**. A crossroads where three battles happened and a healer died saving children is genuinely haunted -- not because a designer placed ghost markers, but because the simulation left real imprints. Older worlds are spiritually richer. Year 5's necromancer has incomparably more material than Year 1's. This is the content flywheel applied to the spiritual layer of the world.

---

## The Core Insight: Dungeons Aren't Special

The dungeon memory system (documented in DUNGEON.md and DUNGEON-EXTENSIONS-NOTES.md) already implements a dual-system design:

- **Collection** (permanent knowledge): "I've experienced first combat victory." Cannot be removed. Feeds seed growth. The dungeon's logos completion record.
- **Inventory** (consumable creative resources): Every notable event creates a memory item with custom stats -- significance score, event type, participants, emotional context. These are **consumed** when the dungeon manifests something: a unique loot item, a painting, an environmental echo, a phantom replay.

The dungeon planning doc explicitly calls out: "Memory items get the full Item system benefits (templates, instances, rarity, custom stats, queries). Inventory constraints provide natural limits. Standard item movement, query, and management APIs."

But a dungeon is just a location that's self-aware. The metaphysical principle -- that significant events leave logos imprints on the places where they occur -- applies everywhere. The dungeon is special only in that it has a cognitive actor that can *perceive and utilize* its own mementos. Every other location accumulates the same residue; it just sits there, waiting for someone with the right skills to interact with it.

The generalization: **every location has a memento inventory, populated by the same event-driven process that populates dungeon memories, consumable by any character with spiritual perception abilities**.

---

## Memento Item Types

Mementos are standard Item instances using the existing template/instance system. Each template defines a category of spiritual imprint; each instance carries the specific data from the specific event.

### Templates

| Template Code | Generated From | Quantity Model | Notes |
|--------------|---------------|----------------|-------|
| `DEATH_MEMENTO` | Character death at location | Unique | One per death event |
| `COMBAT_MEMENTO` | Significant combat at location | Unique | One per engagement above significance threshold |
| `EVENT_MEMENTO` | Historical event at location | Unique | Realm-history-recorded events |
| `EMOTIONAL_MEMENTO` | Intense emotional moment | Unique | High-intensity relationship moments |
| `CRAFT_MEMENTO` | Masterwork creation | Unique | Items created above quality threshold |
| `RITUAL_MEMENTO` | Significant magical working | Unique | Major spellcasting, divine manifestation |
| `BETRAYAL_MEMENTO` | Contract breach or treachery | Unique | Contract service breach events |
| `OATH_MEMENTO` | Binding promise or vow | Unique | Contract creation with high significance |

### Custom Stats per Template

**Death Memento** (`DEATH_MEMENTO`):

| Stat | Type | Source | Purpose |
|------|------|--------|---------|
| `source_character_id` | Guid | Character (L2) | Who died |
| `source_character_name` | string | Character (L2) | Display name at time of death |
| `species_code` | string | Character (L2) | What species they were |
| `cause_of_death` | string | Death event | How they died |
| `killer_entity_id` | Guid? | Combat/event context | Who/what killed them (nullable for natural death) |
| `personality_aggression` | float | Character-Personality (L4) | Personality axis snapshot |
| `personality_mercy` | float | Character-Personality (L4) | Personality axis snapshot |
| `personality_curiosity` | float | Character-Personality (L4) | Personality axis snapshot |
| `personality_pride` | float | Character-Personality (L4) | Personality axis snapshot |
| `personality_sociability` | float | Character-Personality (L4) | Personality axis snapshot |
| `aspiration_primary` | string | Aspiration system | Primary life goal |
| `aspiration_fulfillment` | float | Aspiration system | 0.0-1.0 fulfillment at death |
| `significance_score` | float | Event evaluation | How historically significant this death was |
| `age_at_death` | int | Character (L2) | Character's age |
| `had_unfinished_business` | bool | Aspiration system | Active uncompleted aspirations |
| `final_emotion` | string | Disposition (L4) | Dominant emotion at moment of death |
| `witnesses_count` | int | Event context | How many characters witnessed the death |

**Combat Memento** (`COMBAT_MEMENTO`):

| Stat | Type | Source | Purpose |
|------|------|--------|---------|
| `engagement_type` | string | Combat event | Battle, duel, ambush, siege, raid |
| `participant_count` | int | Event context | Scale of combat |
| `casualty_count` | int | Event context | Deaths resulting from combat |
| `victor_faction` | string? | Faction (L4) | Which faction won (nullable for draws) |
| `duration_game_hours` | float | Worldstate (L2) | How long the combat lasted in game time |
| `notable_participant_ids` | string | Event context | Comma-delimited IDs of significant combatants |
| `tactics_used` | string | Combat event | Environmental interactions, formations, special moves |
| `significance_score` | float | Event evaluation | Historical significance |

**Emotional Memento** (`EMOTIONAL_MEMENTO`):

| Stat | Type | Source | Purpose |
|------|------|--------|---------|
| `primary_character_id` | Guid | Character (L2) | Primary participant |
| `secondary_character_id` | Guid? | Character (L2) | Other participant (nullable for solo moments) |
| `emotion_type` | string | Disposition (L4) | Love, grief, rage, joy, despair, revelation |
| `intensity` | float | Disposition (L4) | 0.0-1.0 emotional magnitude |
| `relationship_type` | string? | Relationship (L2) | Relationship between participants |
| `context` | string | Event evaluation | Brief description of what triggered the moment |
| `significance_score` | float | Event evaluation | Significance |

Additional template custom stats follow the same pattern -- real data from real events, compressed into queryable item stats.

### Significance Scoring

Not every event generates a memento. A threshold prevents trivial events from cluttering the inventory:

| Event Type | Significance Factors | Threshold |
|-----------|---------------------|-----------|
| **Death** | Character level, aspiration fulfillment, witness count, narrative arc position, plot armor state at death | Configurable per location type (lower for sacred sites, higher for mundane locations) |
| **Combat** | Participant count, casualty count, faction involvement, duration, environmental destruction | Higher threshold -- minor skirmishes don't register |
| **Emotional** | Intensity, relationship depth, number of prior encounters between participants | High threshold -- only genuinely intense moments |
| **Craft** | Item rarity created, technique difficulty, cultural significance of the craft | Only masterwork-quality creations |
| **Ritual** | Mana expenditure, divine involvement, effect magnitude | Only significant magical workings |
| **Betrayal** | Contract significance, relationship depth, consequences | Most breaches are minor; only dramatic betrayals register |
| **Oath** | Contract significance, parties' status, consequences of breach | Only binding vows with real stakes |

The god-actor or event brain overseeing the location evaluates significance and decides whether to generate a memento. This is an ABML behavior decision, not a hardcoded rule -- different gods weight different event types differently. Ares generates combat mementos more readily. Moira generates emotional mementos at lower thresholds. Thanatos generates death mementos for almost every death in locations under his watch.

---

## Location Memento Containers

### Integration with Issue #164

Issue #164 tracks the design for location-attached inventories (ground containers). The memento inventory is a SECOND container type on locations, distinct from the ground container:

| Container Type | Purpose | Interaction Model | Visibility |
|---------------|---------|-------------------|------------|
| **Ground container** | Physical items dropped/lost/placed | Anyone can see and pick up | Physical (normal perception) |
| **Memento container** | Spiritual imprints from events | Requires spiritual perception ability | Metaphysical (spiritual perception only) |

Both use the same lib-inventory Container primitive. They differ in access policy and visibility, which is enforced by the game client and ABML behaviors, not by the inventory service itself.

### Location Model Extension

The Location model would gain a `mementoContainerId` field (alongside the planned `groundContainerId` from Issue #164):

| Field | Type | Purpose |
|-------|------|---------|
| `groundContainerId` | Guid? | Physical item container (Issue #164) |
| `groundContainerCreationPolicy` | enum | on-demand, explicit, inherit-parent, disabled |
| `mementoContainerId` | Guid? | Spiritual memento container |
| `mementoContainerCreationPolicy` | enum | on-demand, explicit, inherit-parent, disabled |
| `mementoCapacity` | int? | Maximum mementos before pruning (null = inherit from parent or default) |

### Container Creation Policy

- **on-demand**: Container created when the first memento is generated at this location. Most locations use this -- no overhead until something significant happens.
- **explicit**: Container created by seed data or admin action. Used for locations known to be significant (capital cities, temples, ancient battlefields seeded during world creation).
- **inherit-parent**: Uses the nearest ancestor location's memento container. A room inside a building uses the building's container. A building in a city district uses the district's container unless overridden. This prevents container proliferation for minor locations.
- **disabled**: No mementos accumulate. Used for locations where spiritual residue is canonically impossible (heavily warded areas, divine sanctuaries that cleanse spiritual imprints, locations inside anti-magic zones).

### Capacity and Pruning

When a memento container reaches capacity, the lowest-significance mementos are pruned first. This creates a natural selection effect: over centuries of simulation, only the most dramatic events persist. An ancient battlefield retains mementos from its most heroic deaths and decisive moments while mundane skirmishes fade.

Pruning behavior:
1. Sort mementos by `significance_score` ascending
2. Remove lowest until capacity is restored
3. Pruned mementos can optionally feed the leyline system (becoming potential death echoes -- see [DEATH-AND-PLOT-ARMOR.md](DEATH-AND-PLOT-ARMOR.md#death-echoes))

Capacity defaults scale with location type:

| Location Type | Default Capacity | Rationale |
|--------------|-----------------|-----------|
| Major city | 1000 | Dense population, frequent significant events |
| Town/village | 200 | Moderate activity |
| Wilderness landmark | 100 | Sparse but potentially dramatic events |
| Building/room | 50 | Localized events |
| Sacred site / temple | 500 | Spiritually significant, attracts dramatic events |
| Battlefield (tagged) | 2000 | Massive event density during conflicts |

These are configurable per location or per location type via seed data.

---

## Consumer Roles

Characters interact with memento inventories based on their abilities, which gate through the progressive agency system (Agency service L4) and character skill progression.

### Necromancer: Spirit Type

**Archetype**: Living or undead practitioners who bind and control the spirits of the dead.

**Core interaction**: Consume death mementos to summon spirit echoes -- temporary actors built from the dead character's actual data.

**Mechanic**:
1. Necromancer is at a location with death mementos in the memento inventory
2. Spiritual perception ability reveals available death mementos (filtered by necromancer's skill level -- low skill sees only high-significance mementos)
3. Necromancer selects a memento and initiates summoning ritual
4. The death memento item is consumed from the inventory
5. A temporary Actor is spawned with:
   - Personality axes from the memento's personality snapshot
   - Aspiration state from the memento
   - Knowledge limited to what the character knew at death (queryable from Character-History if the character's archive still exists)
   - A duration timer (spirit energy depletes over time)
   - The dead character's speech patterns and relationship memories

**What the spirit IS**: A pneuma echo -- the same mechanism dungeons use for monster spawning -- given structure by the memento's logos data. It is NOT the character. It is an echo shaped by their imprint. It recognizes people the original character knew. It has opinions based on the original character's personality. It might be angry about unfinished business. But it lacks the full depth of the original consciousness. Think: a photograph that can talk, not a resurrection.

**What the spirit CAN do**:
- Answer questions about its life, death, relationships, knowledge
- Identify its killer (if applicable)
- Reveal secrets it held in life (debts, hidden relationships, stolen items)
- Express emotional states consistent with its personality and death context
- Fight briefly if compelled (using the original character's combat capabilities at reduced effectiveness)
- Interact with living characters who knew it (creating new character-encounter records for the living witnesses)

**What the spirit CANNOT do**:
- Persist beyond the summoning duration
- Gain new memories or change personality
- Physically affect the world beyond the summoner's control
- Access information the original character didn't have
- Override the summoner's binding (though stronger personalities resist more)

### Necromancer: Ghoul Type

**Archetype**: Practitioners who bind and control the bodies (pneuma shells) of the dead.

**Core interaction**: Consume death mementos to raise ghouls -- temporary combatants built from the dead character's physical capabilities.

**Mechanic**:
1. Same location and perception requirements as spirit necromancy
2. Ghoul raising requires a death memento AND proximity to physical remains (a body, bones, or grave)
3. The death memento provides the logos template; the physical remains provide the pneuma substrate
4. A temporary combatant Actor is spawned with:
   - Physical capabilities from the original character (species, combat stats)
   - NO personality -- the ghoul is a body without a mind
   - Duration limited by the necromancer's sustained mana expenditure
   - Quality scaling with the memento's significance and the necromancer's skill

**The ghoul/spirit distinction matters mechanically**: Spirit necromancers get information, social interaction, and limited combat utility. Ghoul necromancers get pure combat power but no information. A necromancer skilled in both gets both -- a spirit advising while a ghoul fights.

### Dual-Type Necromancer

A practitioner who binds both body and spirit creates something more than either alone: a **revenant** -- a ghoul with the spirit's personality driving it. The revenant has the physical capabilities of the ghoul AND the personality, memories, and tactical intelligence of the spirit. This is the closest thing to resurrection that necromancy produces, and it's deeply unsettling to witnesses who knew the original character.

Revenants consume TWO death mementos (one for the spirit template, one for the body template -- they can be from different characters, creating chimeric revenants with one character's mind in another's body) or one death memento at double cost.

### Medium / Spirit Talker

**Archetype**: Characters who perceive and communicate with spiritual residue without binding or controlling it.

**Core interaction**: Read death and emotional mementos to extract information without consuming them.

**Mechanic**:
1. Medium perceives mementos at the location (spiritual perception, same as necromancers)
2. Medium "touches" a memento, initiating a communion
3. The game client renders whispers, visions, emotional impressions drawn from the memento's custom stats:
   - Death mementos: Final thoughts, unfinished business, cause of death, relationships
   - Emotional mementos: The intensity and context of the moment, who was involved
   - Oath mementos: The terms of the vow, the parties involved, whether it was kept
4. Quality of information scales with medium skill and memento significance
5. **Mementos are NOT consumed** -- the medium reads, not takes

**Why mediums matter**: They're the information economy's spiritual wing. A medium who visits the site of a mysterious death can extract real forensic data. A medium at an ancient oath site can learn the terms of a forgotten treaty. The information is real because it came from real events.

### Historian / Archaeologist

**Archetype**: Scholars who reconstruct the past from physical and spiritual evidence.

**Core interaction**: Read event and combat mementos to produce historical records.

**Mechanic**:
1. Historian perceives mementos (requires lower spiritual ability than necromancers -- historical perception is more analytical than spiritual)
2. Historian "studies" mementos at a location, cross-referencing multiple mementos to reconstruct events
3. Can produce **historical documents** (Item instances) that record their findings -- creating a tangible artifact from spiritual research
4. Higher skill reveals connections between mementos: "This combat memento and this death memento are from the same battle" or "This emotional memento's secondary participant is the same character as this death memento's killer"

### Detective / Investigator

**Archetype**: Characters who analyze recent mementos as forensic evidence.

**Core interaction**: Analyze death, emotional, and betrayal mementos at crime scenes.

**Mechanic**:
1. Investigator examines recent mementos (spiritual perception with a forensic focus)
2. Death mementos provide: cause of death, killer entity (if present), witnesses, emotional state at death
3. Emotional mementos in the surrounding area provide context: relationship tensions, recent arguments, fear or despair leading up to the death
4. Betrayal mementos identify contract breaches that may have motivated the crime
5. The investigation is genuine -- the data comes from real events. The player is solving an actual emergent mystery, not a scripted puzzle.

**Why this transforms gameplay**: Murder mysteries in most games are pre-authored. The clues are placed. The solution is fixed. In Arcadia, a murder happens because an NPC with the personality axis for it decided to kill someone, in a specific location, for specific reasons rooted in their actual relationship history. The memento inventory at that location contains the real evidence. A detective character following the trail is engaged in genuine investigation.

### Shrine Keeper / Consecrator

**Archetype**: Characters who transmute spiritual residue into permanent location effects.

**Core interaction**: Consume mementos to create lasting Status effects anchored to the location.

**Mechanic**:
1. Shrine keeper gathers mementos from a location
2. Performs a consecration ritual, consuming the mementos
3. Creates a permanent or long-duration Status effect on the location itself (via Status service -- locations are valid polymorphic entity targets)
4. The effect's nature reflects the consumed mementos:
   - Death mementos from heroic warriors: combat buff for defenders at this location
   - Emotional mementos of love: relationship bonus for interactions here
   - Oath mementos: contract enforcement bonus (harder to breach oaths at this site)
   - Craft mementos: quality bonus for crafting performed here

Shrine keepers are the spiritual equivalent of urban planners -- they deliberately shape the metaphysical character of locations using the raw material of accumulated history. A temple built at a site of great sacrifice, consecrated with the mementos of those who fell, becomes a genuinely sacred place -- not because it was designated sacred, but because the spiritual energy of real sacrifice was transmuted into lasting effect.

### Bard / Storyteller

**Archetype**: Characters who compose performances from the spiritual record.

**Core interaction**: Read mementos to create songs, stories, and performances based on real events.

**Mechanic**:
1. Bard perceives mementos at a location
2. Composes a performance (using the Music service for musical compositions, or Storyline for narrative compositions)
3. The performance contains encoded references to real events -- NPCs who were present at the original event recognize the accuracy
4. High-quality performances based on high-significance mementos become Collection entries for listeners (unlocking "heard the true ballad of the Battle of Ashenmoor")
5. **Mementos are not consumed** -- bards preserve history, they don't extract it

### Craftsman / Artificer

**Archetype**: Characters who imbue creations with spiritual resonance.

**Core interaction**: Consume mementos during crafting to enhance items with historical significance.

**Mechanic**:
1. Craftsman performs crafting at a location with relevant mementos (via Craft service)
2. Optionally consumes a memento during the crafting process
3. The resulting item gains bonus properties reflecting the consumed memento:
   - Death memento of a warrior: weapon gains combat stats related to the warrior's specialization
   - Craft memento of a master artisan: item gains quality bonus reflecting the artisan's technique
   - Emotional memento of devotion: item gains a protective property for the bearer's loved ones
4. The item's provenance records the memento source -- "Forged at the site where Commander Aldric fell, imbued with his final defiance"

---

## NPC Autonomous Behavior

All consumer roles apply equally to NPCs, who interact with memento inventories through their ABML behaviors. This creates autonomous spiritual ecology without any designer intervention.

### NPC Necromancer

```yaml
# NPC necromancer evaluating locations for spiritual harvesting
evaluate_travel_destinations:
  - for_each: ${known_locations}
    as: loc
    evaluate:
      - memento_density: "${loc.memento_count.death} + ${loc.memento_count.combat} * 0.5"
      - distance_cost: "${loc.distance_from_current} * 0.1"
      - score: "${memento_density} - ${distance_cost}"
    sort_by: score desc
    limit: 3
    result_var: candidate_destinations

# At a location with mementos
attempt_spirit_summoning:
  when:
    condition: "${location.memento_count.death} > 0 AND ${self.mana} > ${necromancy.summon_cost}"
    actions:
      - query:
          service: inventory
          endpoint: /inventory/container/list-items
          params:
            container_id: ${location.memento_container_id}
            template_code: DEATH_MEMENTO
          result_var: available_mementos
      - compute:
          best_target: |
            sort(${available_mementos},
              '${item.custom_stats.significance_score}
               * (1.0 + ${item.custom_stats.had_unfinished_business} * 0.5)',
              'desc')[0]
      - when:
          condition: "${best_target.custom_stats.significance_score} > 0.5"
          actions:
            # Consume the memento
            - service_call:
                service: inventory
                endpoint: /inventory/item/remove
                params:
                  container_id: ${location.memento_container_id}
                  item_instance_id: ${best_target.item_id}
            # Spawn the spirit echo
            - service_call:
                service: actor
                endpoint: /actor/spawn
                params:
                  template_code: spirit_echo
                  behavior_document: spirit_echo_behavior.abml
                  initial_variables:
                    personality.aggression: ${best_target.custom_stats.personality_aggression}
                    personality.mercy: ${best_target.custom_stats.personality_mercy}
                    personality.curiosity: ${best_target.custom_stats.personality_curiosity}
                    personality.pride: ${best_target.custom_stats.personality_pride}
                    personality.sociability: ${best_target.custom_stats.personality_sociability}
                    source.character_id: ${best_target.custom_stats.source_character_id}
                    source.character_name: ${best_target.custom_stats.source_character_name}
                    source.cause_of_death: ${best_target.custom_stats.cause_of_death}
                    source.aspiration_primary: ${best_target.custom_stats.aspiration_primary}
                    source.aspiration_fulfillment: ${best_target.custom_stats.aspiration_fulfillment}
                    source.had_unfinished_business: ${best_target.custom_stats.had_unfinished_business}
                    source.final_emotion: ${best_target.custom_stats.final_emotion}
                  duration_seconds: 600
                result_var: spawned_spirit
            # Record the event
            - emit_perception:
                target: nearby_characters
                type: spiritual_manifestation
                data:
                  summoner: ${self.character_id}
                  spirit_name: ${best_target.custom_stats.source_character_name}
                  location: ${world.current_location_id}
```

### NPC Medium at a Shrine

```yaml
# NPC medium communing with mementos at a sacred site
commune_with_dead:
  when:
    condition: |
      ${location.memento_count.death} > 3
      AND ${self.skills.spiritual_perception} > 0.4
      AND ${self.current_action} == 'idle'
    actions:
      - query:
          service: inventory
          endpoint: /inventory/container/list-items
          params:
            container_id: ${location.memento_container_id}
            template_code: DEATH_MEMENTO
          result_var: death_mementos
      # Medium processes mementos without consuming them
      - for_each: ${death_mementos}
        as: memento
        limit: 5
        actions:
          - when:
              # If the dead had unfinished business, the medium may feel compelled to act
              condition: "${memento.custom_stats.had_unfinished_business} == true"
              actions:
                - compute:
                    urgency: "${memento.custom_stats.significance_score} * (1.0 - ${memento.custom_stats.aspiration_fulfillment})"
                - when:
                    condition: "${urgency} > 0.7"
                    actions:
                      # Medium's GOAP planner adds "resolve unfinished business" as a goal
                      - add_goal:
                          type: resolve_spiritual_unrest
                          priority: ${urgency}
                          context:
                            dead_character: ${memento.custom_stats.source_character_name}
                            aspiration: ${memento.custom_stats.aspiration_primary}
                            location: ${world.current_location_id}
```

### The Autonomous Spiritual Ecology

When NPC necromancers, mediums, shrine keepers, and bards all operate autonomously on memento inventories, an emergent spiritual ecology develops:

1. **Characters die** in the simulation (combat, age, disease, murder, sacrifice)
2. **Mementos accumulate** at locations where significant events occurred
3. **NPC mediums** visit spiritually dense locations and sense unfinished business, adding "resolve spiritual unrest" goals to their GOAP planners
4. **NPC necromancers** travel to memento-rich locations, consume death mementos, and summon spirits that interact with the living world
5. **NPC shrine keepers** consecrate locations with accumulated mementos, creating lasting spiritual effects that attract or repel certain character types
6. **NPC bards** compose songs about real events witnessed through mementos, spreading the stories to other locations
7. The spirits summoned by necromancers create NEW encounters (recorded by Character-Encounter), generate new emotional mementos for witnesses, and potentially new death mementos if the spirit is destroyed violently
8. Consecrated shrines attract pilgrims, mediums, and other spiritually-oriented characters, creating social hubs at historically significant locations

None of this is authored. It is the spiritual dimension of the content flywheel -- more deaths produce more mementos, which produce more spiritual activity, which produces more encounters, which produce more mementos. The spiritual world gets richer the longer the simulation runs, exactly as the physical world does.

---

## Connection to the Content Flywheel

Memento inventories create a secondary flywheel loop that runs in parallel with the primary content flywheel:

### Primary Flywheel (Narrative)
```
Character Dies → Archive Compressed → Storyline Composer → Regional Watcher
    → Quest/Scenario Created → Player Experiences → More Deaths → Loop
```

### Secondary Flywheel (Spiritual)
```
Character Dies → Memento Generated at Location → Location Accumulates Spiritual Density
    → NPC Necromancer/Medium/Bard Interacts with Mementos
    → Spirits Summoned / Secrets Revealed / Songs Composed / Shrines Consecrated
    → New Encounters Generated → New Mementos Generated → Loop
```

### How They Interconnect

The two flywheels feed each other:

- A ghost quest generated by the primary flywheel leads players to a location where the secondary flywheel has accumulated mementos. The player's medium companion reads the mementos and discovers the ghost's unfinished business -- information that came from a real character's real aspiration state, not from the quest's authored text.
- An NPC necromancer summoning spirits at a battlefield creates dramatic encounters that god-actors perceive. The god-actor recognizes the narrative potential and commissions a storyline scenario involving the necromancer, the summoned spirits, and whatever unfinished business the spirits carry.
- A bard who composed a song from mementos performs it at a tavern. An NPC who was present at the original event recognizes the accuracy (because Character-Encounter has the record) and reacts -- creating a new emotional memento at the tavern, and potentially a new narrative thread the god-actor can exploit.

---

## Connection to Death System

Memento inventories extend the death system documented in [DEATH-AND-PLOT-ARMOR.md](DEATH-AND-PLOT-ARMOR.md):

### Death Echoes vs. Mementos

| Aspect | Death Echoes | Memento Items |
|--------|-------------|---------------|
| **Trigger** | Involuntary leyline current catch | Deliberate generation on significant event |
| **Visibility** | Anyone can see (atmospheric) | Requires spiritual perception ability |
| **Interaction** | None (observe only) | Full read/consume/transmute |
| **Persistence** | Brief, fading | Permanent until consumed or pruned |
| **Data richness** | Single habitual action loop | Full personality, aspirations, cause of death |
| **Purpose** | Atmosphere and world flavor | Gameplay mechanic and resource |
| **Generation** | Random (leyline physics) | Deterministic (event evaluation) |

Both emerge from the same metaphysical event: a character's logos scattering at the moment of death. Death echoes are the involuntary, atmospheric residue. Mementos are the structured, interactive residue. They coexist at the same locations -- a haunted battlefield has both flickering apparitions (death echoes) AND deep memento inventories that necromancers can harvest.

### Memento Generation During Death Phases

The death phases from DEATH-AND-PLOT-ARMOR.md produce mementos at specific moments:

| Death Phase | Memento Generated | Notes |
|-------------|------------------|-------|
| **The Fall** | `DEATH_MEMENTO` (primary) | Generated at the moment of death, contains full character snapshot |
| **The Haunting** | `EMOTIONAL_MEMENTO` (witnesses) | Witnesses' grief/shock generates emotional mementos at the death site |
| **The Last Stand** | `COMBAT_MEMENTO` (if combat continues) | Spirit-form combat during Last Stand generates combat mementos |
| **Resurrection** | `RITUAL_MEMENTO` | Divine resurrection is a significant magical event -- generates ritual memento |
| **Passage** | None | Logos have fully departed; no new imprint |

### Necromancy and the Underworld

Memento inventories create a surface-world parallel to the underworld's soul currency system:

- **Underworld**: The dead character's logos persist (degrading) in the leyline network. The character experiences an afterlife. Soul currency is spent on blessings, artifacts, and shards.
- **Surface**: The death memento persists (until consumed or pruned) at the location of death. Living characters interact with the memento. Necromancers consume it to create spirit echoes.

A spirit summoned by a necromancer from a memento is NOT the same entity as the character's actual logos in the underworld. It's a copy -- a pneuma echo structured by the memento's data snapshot. The real character may be fighting through Valhalla while a necromancer on the surface summons an echo of them from their death site. The echo doesn't know what the real character is experiencing in the underworld. This distinction matters narratively: the echo can be questioned about its life and death, but not about what happened after death.

This creates interesting scenarios:
- A living character uses the Orpheus Journey to descend to the underworld to rescue someone, while simultaneously a necromancer on the surface summons that same person's echo from a death memento. The echo and the real spirit are separate entities with diverging experiences.
- A necromancer summons an echo of someone who was already resurrected. The echo reflects the character at the moment of death -- it doesn't know it was brought back. Meeting the living resurrected character creates a deeply uncanny encounter for both.

---

## Accumulation Over Time

### The Spiritual Age of the World

A world's spiritual richness is a function of its simulation age:

| World Age | Memento Density | Spiritual Ecology |
|-----------|----------------|-------------------|
| **Year 1** | Sparse | Few mementos. Necromancers struggle to find material. Mediums sense little. Sacred sites are mostly empty. |
| **Year 2** | Growing | Significant battle sites and major cities accumulate meaningful mementos. First consecrated shrines appear. NPC necromancers begin traveling circuits. |
| **Year 3-5** | Rich | Ancient battlefields saturated. City centers have layered centuries of mementos. Established spiritual trade routes. NPC mediums run thriving practices. Consecrated shrines are pilgrimage destinations. |
| **Year 5+** | Saturated | The spiritual layer of the world is as rich as the physical. Every old location has character. Memento pruning means only the most significant events persist at ancient sites, creating a natural "greatest hits" of history. Necromancers at ancient locations summon echoes of legendary figures. |

This mirrors the content flywheel's compounding effect: Year 5's spiritual content is not 5x Year 1's -- it's orders of magnitude richer because the secondary flywheel compounds on itself.

### Location Character Through History

A location's memento inventory tells its story:

**Example: A Crossroads**

| Year | Events | Memento Inventory |
|------|--------|-------------------|
| Year 1 | Trade route established. Minor bandit skirmish. | 1 combat memento (minor). |
| Year 2 | Merchant caravan ambushed. 3 deaths. A love confession between guards the night before. | 3 death mementos, 1 combat memento, 1 emotional memento. |
| Year 3 | Major battle as two factions contest the crossroads. 40 deaths. A hero's last stand. | 12 death mementos (high significance pruned from 40), 3 combat mementos, 1 oath memento (treaty signed here after the battle). |
| Year 5 | NPC shrine keeper consecrates the site with battle mementos. Becomes a memorial. Pilgrims visit. A bard composes "The Ballad of the Crossroads." | Combat buff status effect on location. High foot traffic. Cultural significance. The location has *character* that emerged entirely from simulation. |

A player visiting this crossroads in Year 5 encounters a consecrated memorial with a combat buff, a bard performing a song about a real battle, pilgrim NPCs paying respects, and -- if they have spiritual perception -- a deep layer of mementos they can interact with. A necromancer here could summon the echo of the hero who fell in the Year 3 battle. A medium could hear the love confession from Year 2. None of this was designed. It accumulated.

---

## Dungeon Core Integration

Dungeon cores already have a memory system. Memento inventories extend this in two ways:

### Absorption

When a dungeon core's domain expands to engulf a location with existing mementos, the dungeon can **absorb** those mementos into its own memory inventory. This is mechanically identical to the dungeon's existing memory capture -- the mementos transfer from the location's inventory to the dungeon's inventory, and the dungeon's `memory_depth.capture` seed domain grows.

A dungeon that expands to engulf an ancient battlefield inherits centuries of mementos. Its manifestations (paintings, environmental echoes, phantom replays) draw from real historical events that predated the dungeon's existence. The dungeon didn't create this content -- it consumed it.

### Emission

When creatures die inside a dungeon, the dungeon's memory system captures the event (as it already does). But now, death mementos are ALSO generated at the specific room/location within the dungeon. Adventurers with spiritual perception can sense the accumulated death residue in heavily-fought dungeon corridors. NPC mediums hired to scout a dungeon can report on the spiritual density of different areas -- indicating where the most fighting has occurred and where the dungeon core is likely concentrating its defenses.

### The Necromancer-Dungeon Relationship

Necromancers and dungeon cores are natural allies or rivals:

- **Ally**: A necromancer provides the dungeon with high-quality death mementos from the surface world (rare, significant deaths the dungeon couldn't generate itself). The dungeon provides the necromancer with a steady supply of low-quality death mementos from adventurer deaths (volume over quality). A symbiotic exchange.
- **Rival**: A necromancer raiding a dungeon's memento inventory steals the raw material the dungeon needs for manifestation. The dungeon treats this as a threat and deploys defenses to protect its spiritual resources. A new kind of dungeon raid -- not for gold, but for memories.

---

## Relationship to Vision Principles

| Vision Principle | How Memento Inventories Serve It |
|-----------------|----------------------------------|
| **Living Game Worlds** (North Star #1) | Locations develop spiritual character through simulation. The world's metaphysical layer is alive and evolving. |
| **The Content Flywheel** (North Star #2) | Secondary flywheel -- deaths generate mementos, mementos fuel spiritual interactions, spiritual interactions generate new content. Compounds over time. |
| **100K+ Concurrent NPCs** (North Star #3) | NPC necromancers, mediums, bards, and shrine keepers autonomously interact with mementos, creating emergent spiritual ecology at scale. |
| **Emergent Over Authored** (North Star #5) | Haunted battlefields, sacred shrines, and spiritually dense locations emerge from simulation, not from designer placement. |
| **Characters Are Independent Entities** (Design Principle #1) | Summoned spirit echoes have the original character's personality and act accordingly -- they resist commands that conflict with their nature. |
| **Death Creates, Not Destroys** (Design Principle #4) | Every death creates memento items that fuel future gameplay across multiple roles (necromancy, investigation, craft, performance). Death is literally a resource. |
| **World-State Drives Everything** (Design Principle #3) | Memento availability is world state. Spiritual ecology is driven by simulated events. Which locations are haunted, sacred, or historically significant is emergent. |
| **Authentic Simulation** (Design Principle #5) | Spiritual residue follows consistent metaphysical rules (logos scattering, pneuma shells, leyline physics). Necromancy is grounded in the same metaphysics as all magic. |

---

## Implementation: No New Services Required

| Component | Service | What's Needed |
|-----------|---------|---------------|
| Memento item templates | Item (L2) | `DEATH_MEMENTO`, `COMBAT_MEMENTO`, etc. template seed data |
| Location memento containers | Inventory (L2) + Location (L2) | `mementoContainerId` on location model; container creation policy. Extends Issue #164 scope. |
| Memento generation | God-actor / Event brain ABML | Behaviors that create memento items on death/significant events. Significance evaluation logic. |
| Spiritual perception | Agency (L4) | Domain capability gating memento visibility per spirit |
| Spirit echo spawning | Actor (L2) | Spawn temporary actor with personality data from memento custom stats |
| Ghoul spawning | Actor (L2) | Spawn temporary combatant with physical stats from memento |
| Memento queries | Inventory (L2) | Already exists -- standard container item queries |
| Capacity/pruning | Inventory (L2) or ABML | Periodic pruning behavior or inventory capacity enforcement |
| Consecration effects | Status (L4) | Location-anchored status effects from consumed mementos |
| NPC spiritual roles | ABML behavior documents | Necromancer, medium, bard, shrine keeper, detective behaviors |
| Performance composition | Music (L4) + Storyline (L4) | Bard composes from memento data (inputs to existing systems) |
| Craft enhancement | Craft (L4) | Memento consumption as optional crafting input |
| Dungeon absorption | Dungeon (L4) | Existing memory capture, extended to absorb location mementos on domain expansion |
| Client rendering | Game client | Spiritual perception UX, memento visualization, spirit/ghoul rendering |
| Memento -> Death Echo pipeline | Leyline simulation | Pruned mementos optionally feed death echo generation |

**Total new code**: Zero services, zero plugins. Item templates (seed data). Location model extension for `mementoContainerId` (schema change + generation). ABML behavior documents for memento generation and NPC spiritual roles. Client-side UX for spiritual perception and memento interaction.

The heaviest lift is the Location schema change to add `mementoContainerId` and `mementoContainerCreationPolicy`, which is already partially designed under Issue #164's ground container concept. Everything else is seed data, behavior documents, and client rendering.

---

*This document describes the design for location-based memento inventories and spiritual ecology. For death and plot armor mechanics, see [DEATH-AND-PLOT-ARMOR.md](DEATH-AND-PLOT-ARMOR.md). For dungeon memory systems, see [DUNGEON.md](../plugins/DUNGEON.md). For vision context, see [VISION.md](../reference/VISION.md). For orchestration patterns, see [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md).*
