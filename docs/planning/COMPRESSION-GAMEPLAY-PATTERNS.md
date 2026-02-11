# Compression Gameplay Patterns: Emergent Gameplay from Archived Entities

> **Status**: Vision Document (foundation implemented, gameplay patterns pending)
> **Priority**: High (Content Flywheel -- North Star #2)
> **Related**: `docs/plugins/RESOURCE.md`, `docs/plugins/CHARACTER.md`, `docs/plugins/STORYLINE.md`, `docs/planning/DUNGEON_AS_ACTOR.md`
> **Services**: lib-resource, lib-character, lib-character-personality, lib-character-history, lib-character-encounter, lib-storyline, lib-scene, lib-realm
> **External Inspiration**: *Shangri-La Frontier* (Setsuna of Faraway Days, Wezaemon the Tombguard)

## Executive Summary

When a character dies and is compressed, their entire life story -- personality, memories, relationships, history, encounters -- crystallizes into a rich archive. This isn't just data cleanup; it's **generative input for emergence**.

This document explores the gameplay patterns that emerge from treating compressed archives as generative inputs rather than terminal states:

1. **Resurrection Variants** - Ghosts, zombies, revenants, clones using compressed data
2. **Quest Generation** - Procedural hooks from unfinished business
3. **NPC Memory Seeding** - Living characters who remember the dead
4. **Legacy Mechanics** - Descendants influenced by ancestral data
5. **Live Snapshots** - Summarized data for AI consumption without deletion
6. **Cross-Entity Patterns** - Scenes, realms, items as compressible entities

The fundamental insight: **compression is not the end of a lifecycle, but the beginning of a new one**.

### Implementation Status

| Area | Status | Details |
|------|--------|---------|
| Compression infrastructure | **Done** | lib-resource: `ExecuteCompressAsync`, archive storage (MySQL), callback registration |
| Ephemeral snapshots | **Done** | lib-resource: `CreateSnapshotAsync` (Redis TTL, non-destructive) |
| Character compression callbacks | **Done** | lib-character, lib-character-personality, lib-character-history, lib-character-encounter all register callbacks |
| Storyline consuming archives | **Done** | lib-storyline accepts both archives and snapshots as composition seed sources |
| Resurrection variants | **Vision** | No implementation -- this document describes the design patterns |
| Quest generation from archives | **Vision** | No implementation |
| NPC memory seeding | **Vision** | No implementation |
| Legacy mechanics | **Vision** | No implementation |
| Cross-entity compression | **Vision** | Only characters currently; scenes/realms/items pending |

For the implemented compression data model and API, see the [Resource deep dive](../plugins/RESOURCE.md). This document focuses on the **gameplay patterns** that consume that infrastructure.

---

## Inspiration: Setsuna of Faraway Days

Before diving into technical details, consider how existing media has explored these themes with emotional resonance.

### The Tombguard's Vigil

In *Shangri-La Frontier*, **Setsuna of Faraway Days** (Setsuna Amatsuki) exists as a ghost NPC - the deceased lover of **Wezaemon the Tombguard**, one of the game's legendary bosses. Her compressed existence demonstrates several principles this document explores:

**Location-Bound Persistence**: Setsuna appears only in the **Hidden Garden of Prismatic Forest Grotto**, sitting beside a dead tree - her grave. She isn't a wandering spirit but is anchored to a place of significance. Her compressed data (memories, personality, relationships) manifests at the location where her physical form ended.

**Temporal Gating**: She only appears during **full moon nights**. This cyclical resurrection pattern transforms encountering her from a random event into a ritual pilgrimage. Players must intentionally seek her when conditions align.

**Preserved Relationships as Quest Hooks**: Setsuna enables the quest *"From the Living World, With Love"* - a title that speaks to the liminal space between the living and the dead. Her preserved bond with Wezaemon becomes playable content: she gives players the opportunity to help Wezaemon "escape from his own death."

**The Tombguard Pattern**: Wezaemon himself exemplifies how compressed character data generates ongoing world presence. He became the Tombguard - literally guarding her grave for eternity. His entire identity, combat behavior, and boss mechanics derive from his relationship to Setsuna's compressed existence. He is what he is *because* she died and he preserved her memory.

### Design Lessons

| Setsuna Pattern | Arcadia Implementation |
|-----------------|----------------------|
| Ghost appears at grave during full moon | Ghosts manifest at `death_location` under configurable conditions |
| Preserved love generates quest | `encounter.sentiment > 0.8` + unresolved = quest hook |
| Tombguard protects memory | Living NPCs with strong `encounter.significance` become guardians |
| "From this shore to the far shore" | Compression as bridge between life states, not termination |
| Hidden Garden liminal space | Compression archives as liminal data - not alive, not deleted |

The emotional weight of Setsuna's story comes not from complex AI, but from **preserving the right data**: her relationship to Wezaemon, her location significance, and the unfinished nature of their bond. This is exactly what character compression captures.

**"From the living world, with love"** - every compressed character archive is a letter from the living to the dead, and potentially, a way for the dead to write back.

---

## Part 1: What Gets Archived

When a character dies and is compressed via lib-resource's `ExecuteCompressAsync`, each registered compression callback contributes its data bundle. For the full archive data model and API, see the [Resource deep dive](../plugins/RESOURCE.md).

A single compressed character archive contains:

| Source Type | Data Contributed | Example Content |
|-------------|-----------------|-----------------|
| `character` | Core identity | Name, species, realm, birth/death dates, family summary |
| `character-personality` | Trait values | OPENNESS: 0.7, AGGRESSION: -0.3, LOYALTY: 0.9 |
| `character-personality` | Combat preferences | Style: DEFENSIVE, Risk: 0.2, Protect allies: true |
| `character-history` | Backstory elements | ORIGIN: northlands, TRAUMA: witnessed_massacre, GOAL: avenge_family |
| `character-history` | Event participation | Fought in Battle of Stormgate (HERO, significance: 0.95) |
| `character-encounter` | Memorable meetings | Met Aldric 47 times, sentiment: 0.8 (positive) |
| `character-encounter` | Perspectives | "Aldric saved my life at the bridge" |
| `storyline` | Scenario participations | Active arcs, completed narratives |

This is not just metadata - it's a **complete character prompt** suitable for:
- Procedural narrative creation (Storyline service already does this)
- Behavior system initialization
- Quest graph seeding
- Environmental storytelling
- LLM dialogue generation

---

## Part 2: Resurrection Variants

### The Spectrum of Return

Death in Arcadia need not be final. The compression archive enables a spectrum of resurrection mechanics, each with different fidelity to the original:

```
Full Restoration ──────────────────────────────────────────────► Empty Shell
     │                    │                     │                    │
     │              Partial/Corrupted      Memory Fragment        Name Only
     │                    │                     │                    │
┌────┴────┐        ┌──────┴──────┐        ┌─────┴─────┐        ┌────┴────┐
│ TRUE    │        │ REVENANT    │        │ GHOST     │        │ ZOMBIE  │
│ REVIVAL │        │ CLONE       │        │ ECHO      │        │ CORPSE  │
│         │        │ SIMULACRUM  │        │ MEMORY    │        │ VESSEL  │
└─────────┘        └─────────────┘        └───────────┘        └─────────┘
```

### 2.1: Ghosts and Spirits

**Concept**: A spectral entity manifests from compressed character data, retaining memories and personality but existing in a liminal state.

**Data Usage**:
```yaml
ghost_initialization:
  # Pull from archive
  personality: archive.entries["character-personality"].traits
  memories: archive.entries["character-encounter"].perspectives
  unfinished_business: archive.entries["character-history"].backstory
    .filter(element => element.type == "GOAL" && !element.resolved)

  # Modify for ghostly existence
  modifications:
    - remove: physical_combat_preferences
    - amplify: emotional_memories (multiply significance by 1.5)
    - add: spectral_abilities (phase, possess, manifest)
    - set: mortality = false, aging = false

  # Ghost-specific behaviors
  haunting_triggers:
    - location: death_location (from archive.character.deathContext)
    - people: high_significance_encounters (sentiment < -0.5 or > 0.8)
    - objects: items_mentioned_in_backstory
```

**Gameplay Emergence**:
- Ghost haunts their death location, triggered by familiar faces
- Can be communicated with - dialogue draws from encounter memories
- May provide quest information based on unfinished goals
- Emotional encounters with family members (knows them from archive)
- Can potentially be laid to rest by completing their unfinished business

**The Setsuna Pattern**: Like Setsuna of Faraway Days, ghosts should be:
- **Location-anchored** (Hidden Garden = grave site = death_location)
- **Temporally-gated** (full moon = configurable manifestation conditions)
- **Relationship-driven** (her bond with Wezaemon = high-sentiment encounters)
- **Quest-enabling** ("From the Living World, With Love" = unfinished business resolution)

The emotional impact comes from the specificity of preserved data, not from AI complexity.

### 2.2: Zombies and Undead

**Concept**: Physical resurrection with corrupted/degraded cognition. The body returns but the mind is fragmented.

**Data Usage**:
```yaml
zombie_initialization:
  # Selective archive extraction with corruption
  fragments:
    - combat_preferences: 70% preserved (instinct remains)
    - personality: 20% preserved (emotional echoes)
    - memories: 5% preserved (random fragments surface)
    - goals: 0% preserved (mindless)

  corruption_patterns:
    - trait_inversion: AGGRESSION *= -1 (pacifist becomes violent)
    - memory_scrambling: encounter_targets randomized
    - identity_erosion: name recognition at 30%

  # Zombie-specific behaviors
  behavior:
    - drawn_to: locations_from_backstory (homeland, workplace)
    - recognizes: high_frequency_encounters (spouse, children) at 50%
    - hostile_toward: former_enemies (from negative_sentiment_encounters)
```

**Gameplay Emergence**:
- Zombie shambles toward their homeland - players can deduce origin
- May pause when encountering family members (fragment of recognition)
- Combat style echoes their living preferences (former warrior zombies fight differently)
- Can be "reminded" of their past through encounter triggers

### 2.3: Revenants and Intelligent Undead

**Concept**: Willful return from death, driven by unresolved purpose. Full personality, goal-oriented, but twisted by the experience of death.

**Data Usage**:
```yaml
revenant_initialization:
  # Full archive restoration with modifications
  base: decompress_full_archive()

  modifications:
    # Amplify death-related elements
    - if: backstory.TRAUMA contains death-adjacent
      then: amplify significance by 2x
    - if: encounter.sentiment < -0.7
      then: promote to primary_target

    # Single-minded purpose
    - extract: strongest_unresolved_goal from backstory
    - set: primary_directive = goal
    - suppress: conflicting_goals

    # Personality distortion
    - AGGRESSION: min(1.0, original + 0.5)
    - LOYALTY: preserved for allies, inverted for betrayers
    - add: OBSESSION trait (new axis for revenants)

  # Revenant-specific state
  vengeance_list:
    - source: encounters where sentiment < -0.5 AND significance > 0.6
    - priority: sorted by (significance * |sentiment|)
```

**Gameplay Emergence**:
- Revenant has full conversation capability, remembers everything
- Relentlessly pursues their goal (derived from archived backstory)
- May form temporary alliances with former friends (recognized from encounters)
- Can be negotiated with if you address their unfinished business
- Combat is tactical (preserved preferences) but reckless (death indifference)

### 2.4: Clones and Simulacra

**Concept**: A new entity created from archived data - not the original returned, but a copy initialized from their template.

**Data Usage**:
```yaml
clone_initialization:
  # Archive as initialization template (not restoration)
  template: archive

  # New identity
  new_character_id: generate_new()
  new_name: derived_name_variant(original_name)

  # Selective trait copying
  inheritance:
    personality: 90% fidelity with 10% random variance
    backstory: ORIGIN preserved, other elements regenerated
    encounters: empty (new life, no memories)
    combat_preferences: preserved (muscle memory)

  # Clone-specific complications
  complications:
    - identity_crisis: knows they're a copy (optional)
    - incomplete_memories: dream fragments from archive
    - cellular_degradation: clone_quality determines lifespan
```

**Gameplay Emergence**:
- Clone NPCs can serve as stand-ins for dead important characters
- Players might encounter "echoes" of dead characters in unexpected places
- Clone identity crisis becomes narrative fuel
- Could be used maliciously (clone army of a powerful warrior)

---

## Part 3: Quest Generation from Compressed Data

### The Unfinished Business Pattern

Every compressed character is a potential quest generator. Their archive contains:

1. **Unresolved Goals** - What they wanted but never achieved
2. **Negative Encounters** - Who wronged them, who they wronged
3. **Hidden Knowledge** - Secrets that died with them
4. **Valuable Relationships** - People who would pay for closure

### Quest Template Extraction

```yaml
quest_extractor:
  input: CharacterArchive

  extractors:
    # "Avenge the Fallen"
    revenge_quests:
      source: encounters.where(sentiment < -0.7 AND significance > 0.5)
      template: |
        {dead_character.name} was {encounter.description} by {target.name}.
        Find {target.name} and bring justice.
      reward_basis: relationship_strength with quest_giver

    # "Finish What They Started"
    unfinished_business:
      source: backstory.where(type == "GOAL" AND !resolved)
      template: |
        {dead_character.name} always wanted to {goal.value}.
        They never got the chance. Will you complete their dream?
      reward_basis: goal_significance

    # "Lost Inheritance"
    recovery_quests:
      source: backstory.where(type == "ACHIEVEMENT" OR type == "SECRET")
      template: |
        {dead_character.name} was known for {achievement.value}.
        Their {secret_knowledge/technique/treasure} was lost when they died.
        Legends say it can be found at {inferred_location}.
      reward_basis: achievement_significance

    # "Tell My Family"
    closure_quests:
      source: family_tree + high_sentiment_encounters
      template: |
        {dead_character.name} never got to say goodbye to {family_member.name}.
        Deliver their final message: {generated_from_relationship_context}
      reward_basis: emotional_impact

    # "The Truth Died With Them"
    mystery_quests:
      source: backstory.where(type == "SECRET") + event_participation
      template: |
        {dead_character.name} knew the truth about {historical_event}.
        Others who participated in {event.name} might know fragments.
        Piece together the truth.
      reward_basis: event_significance
```

### Quest Complexity Scaling

A single compressed character can generate quest chains:

```
Archive of "Aldric the Bold"
├── Backstory: GOAL = "find lost brother"
│   └── Quest 1: "The Search Continues" (find clues to brother)
│       ├── Clue from: encounter with merchant (knows brother's route)
│       ├── Clue from: historical event participation (war scattered family)
│       └── Resolution: Find brother (alive? dead? also compressed?)
│
├── Encounter: negative sentiment with "Lord Vex" (significance 0.9)
│   └── Quest 2: "Justice Delayed" (confront Lord Vex)
│       ├── Context from: encounter perspectives
│       ├── Allies from: positive sentiment encounters
│       └── Complication: Lord Vex has own archive sympathizers
│
├── Backstory: SECRET = "location of elven artifact"
│   └── Quest 3: "What Aldric Knew" (retrieve artifact)
│       ├── Location inference from: ORIGIN + event_participation
│       ├── Danger level from: combat_preferences (he was cautious = dangerous)
│       └── Artifact purpose from: GOAL + BELIEF backstory elements
│
└── Family: spouse still alive, children orphaned
    └── Quest 4: "A Father's Legacy" (help Aldric's family)
        ├── Spouse quest: deliver heirloom + final words
        ├── Children quest: train them (Aldric's combat preferences as template)
        └── Resolution: Family remembers Aldric (encounter created in their data)
```

---

## Part 4: NPC Memory Seeding

### Living Characters Remember the Dead

When a character is compressed, the living characters who knew them retain memories. But what about **new NPCs** who should have known the dead?

**Pattern**: Use compressed archives to seed memories in newly created NPCs.

### Memory Injection from Archives

```yaml
npc_creation_with_memories:
  # New NPC is created
  new_npc: create_character(species, realm, ...)

  # Find compressed characters they should know
  relevant_archives:
    - same_realm AND overlapping_lifespan
    - backstory.ORIGIN matches new_npc.origin
    - backstory.OCCUPATION matches new_npc.occupation
    - event_participation.event_id in new_npc.potential_events

  # Inject fabricated encounters
  for archive in relevant_archives:
    plausible_encounter = generate_encounter(
      participants: [new_npc.id, archive.character_id],
      type: inferred_from(archive.backstory.OCCUPATION),
      sentiment: random_weighted(archive.personality),
      significance: based_on(overlap_duration),
      perspectives: generate_from(archive.personality, relationship_type)
    )
    # Store as new_npc's memory (archive character is dead, won't reciprocate)
    character_encounter.create(plausible_encounter)

  # Result: New NPC "remembers" dead characters
```

### Dialogue Integration

```yaml
dialogue_triggers:
  # When NPC mentions dead character organically
  - condition: player_asks_about(topic) AND topic in dead_character.backstory
    response: |
      "Ah, that reminds me of {dead_character.name}. They used to say..."
      # Draws from: dead_character.personality + encounter.perspectives

  # When player brings up dead character's name
  - condition: player_mentions(dead_character.name)
    response_positive: |
      "{dead_character.name}! I remember them well. We {encounter.type}
      together at {encounter.location}. They were always {personality_adjective}."

    response_negative: |
      "{dead_character.name}... don't speak that name to me.
      What they did at {event.location} was unforgivable."

  # When NPC is in dead character's significant location
  - condition: location == dead_character.death_location
    response: |
      "This is where {dead_character.name} fell. I still remember
      {encounter_memory_fragment}..."
```

### The Tombguard Pattern

**Concept**: Living NPCs whose entire identity becomes defined by their relationship to a compressed character.

Wezaemon the Tombguard in *Shangri-La Frontier* exemplifies this: he guards Setsuna's grave for eternity. His boss mechanics, combat patterns, and very name derive from his preserved bond with her compressed existence.

```yaml
tombguard_generation:
  # Identify candidates: living NPCs with extreme archived relationships
  candidates:
    source: living_characters.where(
      encounters.any(
        target.is_compressed AND
        sentiment > 0.9 AND
        significance > 0.8
      )
    )

  # Transform NPC based on grief/devotion
  transformation:
    for candidate in candidates:
      deceased = candidate.encounters.where(target.is_compressed).max_by(sentiment)

      # NPC becomes guardian of the dead
      modifications:
        - rename: derive_title_from_role(candidate, deceased)
          # "the Tombguard", "the Keeper", "the Mourning"
        - location_binding: deceased.death_location
          # NPC drawn to significant location
        - goal_override: protect_memory_of(deceased)
          # Primary motivation becomes preservation
        - combat_amplification: if loyalty was high, boost defensive abilities
        - dialogue_injection: add deceased memories to conversation triggers

  # Gameplay outcomes
  outcomes:
    - boss_generation: Tombguard NPCs become location bosses
    - quest_gatekeeping: Must understand their loss to pass
    - redemption_arcs: Help them move on = release from duty
    - tragedy_perpetuation: Some refuse to let go
```

**Why This Works**: The compressed character never acts, never speaks, never fights - yet they define another character's entire existence. The archive data flows *through* the living to affect the world.

---

## Part 5: Legacy Mechanics

### Ancestral Influence on Descendants

When a character dies, their compressed data becomes a **genetic and memetic template** for descendants.

### 5.1: Personality Inheritance

```yaml
child_personality_generation:
  # Parents' compressed archives (or live data if still alive)
  parent_archives: [mother_archive, father_archive]

  trait_inheritance:
    for trait in PERSONALITY_TRAITS:
      # Weighted average with variance
      base_value = (
        parent_archives[0].personality[trait] * 0.35 +
        parent_archives[1].personality[trait] * 0.35 +
        random_variance(-0.3, 0.3)
      )

      # Environmental modifiers from backstory
      if parent_archives.any(a => a.backstory.TRAUMA.high_significance):
        # Child may inherit trauma response
        base_value += trauma_modifier(trait)

      child.personality[trait] = clamp(base_value, -1.0, 1.0)
```

### 5.2: Inherited Memories (Ancestral Dreams)

```yaml
ancestral_memory_mechanics:
  # Child occasionally "remembers" ancestor experiences
  trigger: sleep, meditation, significant_stress, location_matching

  memory_selection:
    pool: ancestor_archives.flatMap(a => a.event_participation + a.encounters)
    weights:
      - significance: higher = more likely to surface
      - emotional_intensity: trauma surfaces more
      - relevance: matching current_situation increases chance

  presentation:
    # In-game as dream sequences or deja vu
    - "You dream of a battle you've never fought..."
      # Data from: ancestor.event_participation where type == COMBAT
    - "This place feels familiar, though you've never been here..."
      # Data from: ancestor.encounter where location == current_location

  gameplay_effects:
    - skill_unlock: ancestor combat_preferences may grant technique hints
    - quest_clues: ancestor knowledge surfaces when relevant
    - relationship_modifier: recognize ancestor's friends/enemies subconsciously
```

### 5.3: Family Reputation System

```yaml
reputation_inheritance:
  # Dead character's actions affect living descendants
  reputation_events:
    source: ancestor_archives.event_participation + ancestor_archives.encounters

  faction_standing:
    for faction in game.factions:
      ancestor_standing = aggregate(
        events.where(faction_involved(faction)).map(e =>
          e.role_contribution * e.outcome_for_faction
        )
      )
      descendant_modifier = ancestor_standing * decay_factor(generations) * 0.5

  # "Your grandmother was a hero of the Stormgate. You're welcome here."
  # "Your grandfather betrayed us. You'd better prove yourself different."

  npc_reactions:
    if ancestor_encounter.sentiment.strong:
      initial_reaction = sentiment * familiarity_recognition_chance
      # "You look just like... no, never mind. What do you want?"
```

---

## Part 6: Live Snapshots

> **Implementation note**: The core concept described here has been implemented as lib-resource's ephemeral snapshot system (`/resource/snapshot/execute` and `/resource/snapshot/get`). Snapshots are stored in Redis with configurable TTL (1-24 hours) and are non-destructive. Storyline already consumes both permanent archives and ephemeral snapshots as composition seed sources.

### The Key Insight

Compression doesn't require deletion. The **snapshot system** creates summarized views of living entities for:

1. **AI Context Windows** - Feed character summaries to LLMs
2. **Cross-Service Data Sharing** - Single blob instead of N queries
3. **Player Dashboards** - "Your character at a glance"
4. **NPC Brain Initialization** - Quick-load character context
5. **Save/Load Summarization** - Human-readable state descriptions

### AI-Friendly Character Summaries

```yaml
live_snapshot_for_ai:
  trigger: actor_brain_initialization, dialogue_generation, quest_npc_context

  output_format:
    # Optimized for LLM consumption
    summary: |
      **{character.name}** ({species.name}, {age} years old)

      **Personality**: {personality_descriptor} (Traits: {top_3_traits})
      **Background**: {origin_summary} who {occupation_summary}.
      **Defining Experiences**: {top_3_backstory_elements}
      **Combat Style**: {combat_preferences_summary}

      **Key Relationships**:
      {top_5_encounters.map(e => "- {e.target}: {e.relationship_summary}")}

      **Current Goals**: {active_goals}
      **Fears**: {fears_from_backstory}

  size: ~1-2KB (fits in any context window)
  update_frequency: on_significant_change OR periodic_refresh
```

### Actor Brain Quick-Load

```yaml
actor_brain_initialization:
  # Instead of querying 5 services sequentially
  old_pattern:
    1. character_service.get(id)          # 50ms
    2. personality_service.get(id)        # 50ms
    3. history_service.get_backstory(id)  # 50ms
    4. encounter_service.query(id)        # 100ms
    5. relationship_service.query(id)     # 50ms
    # Total: ~300ms, 5 service calls

  new_pattern:
    1. resource_service.create_snapshot("character", id)  # Executes callbacks
    2. resource_service.get_snapshot(snapshotId)           # ~30ms
    # Total: ~80ms first time, ~30ms on cache hit
```

### Remaining Work

The snapshot infrastructure is in place. What's still needed:

- **Snapshot invalidation**: Event-driven cache busting when personality/history/encounters change
- **AI-summary generation**: Template-based text summaries optimized for LLM context windows
- **Actor brain integration**: Using snapshots as the fast path for Variable Provider initialization
- **Event context enrichment**: Including snapshot data in published events for downstream consumers

---

## Part 7: Cross-Entity Compression Patterns

### Beyond Characters: Compressible Entities

The compression pattern applies to **any entity with rich associated data**. Only characters currently implement compression callbacks; the following are future patterns.

### 7.1: Scene Compression

**Use Case**: Ruins, abandoned areas, temporal mechanics

```yaml
scene_compression:
  sourceTypes:
    - scene: node_tree, metadata, version_history
    - scene-assets: texture_refs, model_refs, audio_refs
    - mapping: spatial_data, affordances, pathfinding
    - scene-history: edit_history, creators, purpose

  gameplay_applications:
    ruins_generation:
      # Start with compressed vibrant scene, procedurally decay
      - original: compressed_city_scene
      - apply: decay_algorithm(intensity=0.7)
      - result: ruined_city with recognizable elements

    temporal_mechanics:
      # Time travel shows compressed past state
      - present: current_scene
      - past: decompressed_historical_version
      - transition: morphing_based_on_compression_diff

    archaeology_system:
      # Players piece together history from fragments
      - clue: partial_decompression_from_artifact
      - full_picture: complete_decompression after enough clues
```

### 7.2: Realm Compression

**Use Case**: Lost civilizations, alternate dimensions, mythology generation

```yaml
realm_compression:
  sourceTypes:
    - realm: metadata, cultural_context, political_system
    - location: all_locations_in_realm (hierarchical)
    - species: realm_native_species
    - realm-history: historical_events, lore_elements
    - character: notable_figures (compressed when dead)

  gameplay_applications:
    lost_civilizations:
      # Compressed realm = historical record
      - artifact: contains fragment of realm_compression
      - study: reveals {partial_summary}
      - quest: piece together full realm history

    alternate_dimensions:
      # Compressed realm as template for parallel world
      - base: realm_compression
      - modify: key_historical_events
      - generate: alternate_realm with divergent history

    mythology_generation:
      # Realm compression becomes in-game mythology
      - source: compressed_ancient_realm
      - distortion: apply oral_tradition_degradation()
      - result: in-game myths with partial truth
```

### 7.3: Item Compression (Artifact Histories)

```yaml
item_compression:
  sourceTypes:
    - item: template, current_stats, enchantments
    - item-history: ownership_chain, notable_uses, modifications
    - item-associations: characters_who_wielded, events_involved

  gameplay_applications:
    identify_spell:
      # Partial decompression reveals history
      - basic: item_name, type
      - moderate: creator, age, notable_owner
      - full: complete history, hidden properties

    artifact_sentience:
      # Powerful items with enough history develop personality
      - if: historical_significance > threshold
      - then: generate_personality_from_ownership_patterns
      - result: item that "remembers" its past wielders

    legacy_weapons:
      # Weapon adapts to lineage
      - wielder: descendant of previous_owner
      - bonus: based on ancestor's compressed combat_preferences
      - dialogue: weapon "recognizes" lineage
```

---

## Part 8: Design Considerations

### Privacy and Consent

For player characters:
- Archive compression requires player consent (or character death)
- Player controls ghost/resurrection permissions
- "Legacy Mode" opt-in for descendant influence
- Archive deletion (true death) always available

### Performance

- Snapshots cached in Redis with TTL-based expiration
- Archive retrieval O(1) by resourceId from MySQL
- Bulk archive queries for realm-wide analysis
- Background processing for heavy operations (quest generation)

### Data Integrity

- Archives are immutable after creation (versioned updates)
- Checksum verification on decompression
- Source data preservation optional (sourceDataDeleted flag)
- Audit trail for resurrection/quest generation

### Narrative Coherence

- Generated content reviewed for consistency
- Template system allows game-specific customization
- Significance thresholds prevent noise
- Human oversight for major resurrections

---

## Conclusion

What started as a cleanup mechanism has become **the narrative DNA of Arcadia**. When a character dies, they don't simply disappear - they become:

1. **A potential ghost** haunting meaningful locations
2. **A zombie threat** with fragmented memories
3. **A revenant nemesis** driven by unfinished business
4. **A clone template** for new entities
5. **A quest generator** producing meaningful content
6. **A memory seed** for living NPCs
7. **An ancestor** influencing future generations
8. **An AI context** for dialogue and behavior (already working via Storyline)

The compression archive is not an obituary - it's a **character prompt**, a **narrative seed**, and a **gameplay resource**. Every death enriches the world rather than diminishing it.

The same pattern extends to scenes (ruins, temporal mechanics), realms (lost civilizations, mythology), and items (artifact histories, sentient weapons). Compression becomes the bridge between past and present, death and emergence, memory and new life.

**The dead are never truly gone. They're just compressed.**

---

*This document is part of the Bannou planning documentation. For the compression infrastructure API and data model, see [Resource deep dive](../plugins/RESOURCE.md). For narrative generation from archives, see [Storyline deep dive](../plugins/STORYLINE.md).*
