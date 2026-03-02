# Cryptic Trails: Distributed Knowledge Puzzles Through Emergent Clue Composition

> **Status**: Design
> **Created**: 2026-02-27
> **Author**: Lysander (design) + Claude (analysis)
> **Category**: Cross-cutting mechanic (behavioral + Lexicon/Hearsay extension)
> **Related Services**: Lexicon (L4), Hearsay (L4), Collection (L2), Seed (L2), Actor (L2), Disposition (L4), Quest (L2), Agency (L4), Gardener (L4), Affix (L4), Loot (L4), Item (L2), Inventory (L2), Puppetmaster (L4)
> **Related Plans**: [LOGOS-RESONANCE-ITEMS.md](LOGOS-RESONANCE-ITEMS.md), [MEMENTO-INVENTORIES.md](MEMENTO-INVENTORIES.md), [COMPRESSION-GAMEPLAY-PATTERNS.md](COMPRESSION-GAMEPLAY-PATTERNS.md), [DUNGEON-EXTENSIONS-NOTES.md](DUNGEON-EXTENSIONS-NOTES.md)
> **Related Docs**: [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md), [VISION.md](../reference/VISION.md), [PLAYER-VISION.md](../reference/PLAYER-VISION.md)
> **Related Deep Dives**: [LEXICON.md](../plugins/LEXICON.md), [HEARSAY.md](../plugins/HEARSAY.md), [DISPOSITION.md](../plugins/DISPOSITION.md), [QUEST.md](../plugins/QUEST.md), [AFFIX.md](../plugins/AFFIX.md), [AGENCY.md](../plugins/AGENCY.md), [SEED.md](../plugins/SEED.md), [COLLECTION.md](../plugins/COLLECTION.md)
> **Related arcadia-kb**: True Names, Dungeon System, Underworld and Soul Currency System

---

## Executive Summary

A dungeon spawns beneath an ancient city. The entrance is hidden. No quest marker points to it. No NPC says "go here." Instead, a character examining an old sword notices an affix description referencing "depths that predate the city above." A death memento at a tavern records someone who "fell into a hidden passage." A rumor circulates about scratching sounds beneath the inn. A historical text in a library describes darkspawn tunnel construction techniques. Each clue alone is a curiosity. Together, they form a hypothesis: **the old quarter sits atop darkspawn tunnels.**

The character who connects these clues does so through their own knowledge systems -- Lexicon associations becoming visible as discovery tiers advance, Hearsay beliefs compounding from multiple sources, hypotheses self-generating from accumulated evidence. The "aha" moment is not a scripted trigger or a quest giver's dialogue. It is a **Lexicon hypothesis generation event** where the character's accumulated knowledge crosses a threshold and a hidden association becomes visible, producing an inference that enters Hearsay as a self-generated belief and feeds Disposition as an investigation drive.

This system -- Cryptic Trails -- provides the architecture for complex dynamic world-puzzles ranging from trivially simple (noises under floorboards: A leads directly to B) to extraordinarily difficult (requiring specific skills, accumulated knowledge across multiple domains, persistence over time, collaboration with other investigators, and sometimes luck). It requires **zero new plugins**. It composes entirely from existing services through three new primitives: an "inference" channel type for Hearsay, skill-gated tier offsets for Lexicon hypothesis generation, and Discovery Templates as seeded Lexicon/Seed/ABML configuration that define investigation methodology patterns.

---

## The Core Insight: Knowledge as Gameplay

### The Problem with Traditional Discovery

Traditional game puzzles fall into two categories:

| Type | How It Works | Why It Fails for Arcadia |
|------|-------------|-------------------------|
| **Authored puzzles** | Designer places clues in specific locations with scripted connections | Finite content; once solved, dead; doesn't scale with world age |
| **Procedural generation** | Algorithm generates puzzle topology, fills with random content | Feels hollow; clues lack historical grounding; no flywheel connection |

Arcadia's world generates real history through simulation. Characters live, die, fight, build, betray, and create. This history leaves traces: mementos at locations (MEMENTO-INVENTORIES), embedded memories in items (LOGOS-RESONANCE-ITEMS), rumors propagating through social networks (Hearsay), and structured knowledge accumulating in the world's ontology (Lexicon). The raw material for infinitely complex, historically grounded puzzles already exists. What's missing is the mechanism for characters to **connect the dots**.

### The Solution: Emergent Hypothesis Generation

Cryptic Trails don't author puzzles. They author **investigation methodologies** -- the types of clues that exist, the skills that help find and interpret them, the red herrings that mislead, and the association structures that connect disparate evidence into realizations. The specific puzzle instances emerge from world state: which dungeons spawned where, which characters died and left mementos, which items carry embedded memories, which rumors circulate. The trails are as unique and unreproducible as the history that created them.

The character's realization is genuine inference, not a scripted reveal:

```
Clue accumulation (Hearsay beliefs + Collection discovery tiers)
    → Discovery threshold crossed (skill-gated Lexicon association visibility)
    → Hypothesis generated (Lexicon inference from newly visible associations)
    → Belief formed (Hearsay "inference" channel, self-generated)
    → Drive formed (Disposition: "investigate_mystery")
    → Behavior changes (GOAP prioritizes investigation goals)
    → Character acts on their realization autonomously
```

No god-actor triggers the "aha." The god-actor's role is earlier: structuring the Lexicon associations, distributing clues through the world, and optionally responding to the character's investigation with catalysts or complications. The realization itself emerges from the character's own cognitive pipeline.

---

## The Two-Layer Knowledge Model

### Character Knowledge vs Player Perception

Item knowledge, environmental clues, and investigation progress operate on two independent layers:

```
CHARACTER LAYER (NPC brain):                    PLAYER LAYER (UI):
  "What does the character understand?"           "What can the spirit perceive?"
  ┌────────────────────────────────┐              ┌────────────────────────────────┐
  │ Analysis skill level           │              │ Agency manifest level          │
  │ + Lexicon discovery tiers      │              │ + Guardian spirit experience   │
  │ + Seed domain capabilities     │              │ + Omega UX module overrides    │
  │ = what the character KNOWS     │              │ = what the player SEES         │
  └────────────┬───────────────────┘              └────────────┬───────────────────┘
               │                                               │
               │  INDEPENDENT LAYERS                           │
               │                                               │
               ▼                                               ▼
  Affects: ABML behavior,                        Affects: UI rendering,
  Hearsay sharing, GOAP                          tooltip detail, activation
  decisions, trade valuations,                   bars, prerequisite visibility,
  hypothesis generation                          investigation journal display
```

These layers decouple deliberately. A character may understand an item's hidden affix (high analysis skill) while the player's spirit lacks the Agency progression to perceive that understanding in the UI. Conversely, in the Omega realm, a player can "hack" the UX to see information that the character hasn't analyzed -- the spirit sees through the glass but can't hand the knowledge over. The character must still perform the analysis to act on what the spirit sees.

### Item Analysis as Active Discovery

By default, item affixes are **not revealed** to the character. A vendor describes what they claim the item does -- which enters Hearsay as a `social_contact` or `trusted_contact` belief about the item, at the vendor's credibility level. A master smith gives high-confidence descriptions. A shady market dealer might exaggerate, omit, or lie.

To know what an item actually does, a character must **analyze** it -- an explicit action whose depth depends on the character's capabilities:

| Analysis Capability | Source | What's Revealed |
|---|---|---|
| Basic inspection | Default (any character) | Item template, visible physical properties, vendor-claimed stats |
| Surface analysis | Seed capability: `analysis.basic` | Implicit affixes (always-active, low-tier), item origin type |
| Deep analysis | Seed capability: `analysis.intermediate` | Explicit affixes with partial prerequisite hints, resonance origin |
| Expert analysis | Seed capability: `analysis.expert` | Full affix details, activation prerequisites, predecessor memories |
| Master analysis | Seed capability: `analysis.master` | Fidelity predictions, optimal build paths, hidden Lexicon associations embedded in the item |

Each analysis level advances Collection discovery for the Lexicon entries referenced by the item's affixes. An item with a "darkspawn architecture" themed affix advances discovery for the `darkspawn_architecture` Lexicon entry when analyzed at sufficient depth. **Analysis is a clue source.** Examining items is how characters discover embedded knowledge.

### Trade and Appraisal Consequences

The analysis model creates a natural economy for knowledge:

- **NPC merchants without analysis skills** underprice complex resonance items. They see surface stats only.
- **Master appraisers** command fees for deep analysis. They reveal hidden affixes and can estimate activation fidelity for potential buyers.
- **Deceptive vendors** can claim an item has properties it doesn't. The buyer's only protection is their own analysis skill or a trusted appraiser.
- **Treasure hunters** analyze found items not just for combat value but for **embedded clues** -- a resonance sword from a dungeon might carry architectural knowledge in its affix descriptions that advances a separate investigation.

### Player UX Gating (Agency/Gardener)

The player's view of all this is controlled by the Agency service's UX capability manifest, following the progressive revelation model from PLAYER-VISION:

| Spirit Agency Level | Investigation UX |
|---|---|
| Minimal | "This item has hidden properties" (no detail) |
| Low | Affix names visible when character has analyzed; investigation journal shows active clue count |
| Medium | Prerequisite hints visible; investigation journal shows clue categories and missing gaps |
| High | Full fidelity percentages; investigation journal shows hypotheses and confidence levels |
| Mastery | Lexicon association previews; investigation planner showing optimal analysis paths |

The Gardener service orchestrates investigation-related UX events: prompting the player when their character is near a clue source, highlighting items worth analyzing, and surfacing the investigation journal when relevant. The Omega "hack" mechanic can unlock higher investigation UX tiers without the spirit having earned them -- but the character still needs skills to act on what the spirit sees.

---

## Discovery Templates: Investigation Methodology Patterns

### What Templates Are

A Discovery Template is **not** a specific puzzle. It is a pattern that defines:

1. **Clue taxonomy**: What categories of evidence contribute to this type of investigation
2. **Skill gates**: What capabilities accelerate or enable discovery within each category
3. **Analysis actions**: What a character can do to gather and interpret clues
4. **Red herring types**: What false trails exist and how they're recognized
5. **Collaboration patterns**: How characters share and pool investigation progress
6. **Difficulty parameters**: How many clue categories, what confidence thresholds, what discovery tiers

The specific puzzle instance is generated from world state -- which dungeons, treasures, resources, or secrets exist in the world and what historical traces they've left. The template provides the **structure** of investigation; the world provides the **content**.

### Template as Seeded Data Composition

Discovery Templates compose from three existing primitives:

**A. Lexicon category hierarchies** define the clue ontology:

```
discovery (root category)
├── treasure_hunting
│   ├── cartographic_clues
│   │   traits: [spatial_reference, coordinate_system, scale_indicator]
│   │   analysis_skill: "discovery.cartography"
│   │   discovery_tiers: { basic: 1, detailed: 3, expert: 5 }
│   ├── historical_clues
│   │   traits: [temporal_reference, named_individual, event_description]
│   │   analysis_skill: "discovery.history"
│   │   discovery_tiers: { basic: 1, detailed: 3, expert: 5 }
│   ├── geological_clues
│   │   traits: [mineral_composition, terrain_anomaly, subsurface_indicator]
│   │   analysis_skill: "discovery.geology"
│   │   discovery_tiers: { basic: 2, detailed: 4, expert: 6 }
│   ├── architectural_clues
│   │   traits: [construction_technique, material_signature, spatial_pattern]
│   │   analysis_skill: "discovery.architecture"
│   │   discovery_tiers: { basic: 2, detailed: 4, expert: 6 }
│   ├── rumor_clues
│   │   traits: [witness_account, secondhand_report, local_legend]
│   │   analysis_skill: null  (no skill needed to hear rumors)
│   │   discovery_tiers: { basic: 1, detailed: 2, expert: 3 }
│   └── red_herrings
│       ├── decoy_markers
│       │   traits: [suspicious_provenance, inconsistent_dating, anachronistic_material]
│       │   detection_capability: "detect_simple_red_herrings"
│       ├── misleading_inscriptions
│       │   traits: [deliberate_misdirection, planted_by_entity, coded_warning]
│       │   detection_capability: "detect_complex_red_herrings"
│       └── outdated_information
│           traits: [temporal_decay, superseded_by_events, partial_truth]
│           detection_capability: "detect_simple_red_herrings"
│
├── resource_prospecting
│   ├── geological_survey_clues
│   ├── hydrological_clues
│   ├── biological_indicators
│   ├── historical_extraction
│   └── red_herrings
│       ├── surface_deposits
│       ├── contamination_traces
│       └── exhausted_sites
│
├── arcane_research
│   ├── textual_evidence
│   ├── resonance_traces
│   ├── experimental_results
│   ├── mentor_knowledge
│   └── red_herrings
│       ├── corrupted_texts
│       ├── false_correlations
│       └── trap_knowledge
│
├── criminal_investigation
│   ├── witness_testimony
│   ├── physical_evidence
│   ├── behavioral_patterns
│   ├── financial_trails
│   └── red_herrings
│       ├── planted_evidence
│       ├── false_witnesses
│       └── coincidental_patterns
│
└── nature_lore
    ├── behavioral_observation
    ├── environmental_traces
    ├── seasonal_patterns
    ├── ecological_relationships
    └── red_herrings
        ├── mimicry_species
        ├── coincidental_cohabitation
        └── anthropomorphic_projection
```

Each leaf node is a Lexicon entry with traits that define what data is associated with that clue type. When a character encounters a clue, the god-actor (or the analysis action) maps it to the appropriate Lexicon category and advances Collection discovery.

**B. Seed type definitions** provide skill progression:

```yaml
# Seeded via POST /seed/type/register
seed_types:
  - code: "discovery_treasure"
    displayName: "Treasure Hunter's Instinct"
    domains:
      - code: "cartography"
        description: "Map reading, spatial inference, coordinate interpretation"
      - code: "history"
        description: "Historical text interpretation, dating, provenance analysis"
      - code: "geology"
        description: "Terrain reading, mineral identification, subsurface inference"
      - code: "architecture"
        description: "Construction technique recognition, hidden passage detection"
      - code: "appraisal"
        description: "Item analysis depth and accuracy"
    phases:
      - label: "Curious"
        minGrowth: 0
        capabilities:
          - "basic_clue_recognition"
      - label: "Seeker"
        minGrowth: 5
        capabilities:
          - "cross_reference_clues"
          - "detect_simple_red_herrings"
      - label: "Investigator"
        minGrowth: 15
        capabilities:
          - "deep_analysis"
          - "hypothesis_generation"
          - "share_findings_effectively"
      - label: "Expert"
        minGrowth: 40
        capabilities:
          - "detect_complex_red_herrings"
          - "intuitive_connections"
          - "teach_methodology"
      - label: "Master"
        minGrowth: 100
        capabilities:
          - "master_synthesis"
          - "create_investigation_guides"
          - "predict_red_herrings"

  - code: "discovery_prospecting"
    displayName: "Prospector's Eye"
    domains:
      - code: "geological_survey"
      - code: "hydrology"
      - code: "biology"
      - code: "extraction_history"
    # Similar phase structure...

  - code: "discovery_arcane"
    displayName: "Scholar's Insight"
    domains:
      - code: "textual_analysis"
      - code: "magical_resonance"
      - code: "experimentation"
      - code: "theoretical_framework"
    # Similar phase structure...
```

**C. ABML behavior templates** define god-actor investigation orchestration:

These are behavior document templates that regional watchers and specialized god-actors use to distribute clues, evaluate character progress, plant red herrings, and respond to investigation milestones. See [God-Actor Evaluation Behavior](#god-actor-evaluation-behavior) below.

### Example Templates

| Template | Clue Categories | Key Skills | Typical Difficulty | Red Herring Types |
|----------|----------------|-----------|-------------------|-------------------|
| **Treasure Hunting** | Cartographic, historical, geological, architectural, rumor | `discovery_treasure.*` | Medium-Hard | Decoy markers, misleading inscriptions, outdated maps |
| **Resource Prospecting** | Geological survey, hydrological, biological, extraction history | `discovery_prospecting.*` | Medium | Surface deposits, contamination, exhausted sites |
| **Arcane Research** | Textual, resonance, experimental, mentor | `discovery_arcane.*` | Hard-Extreme | Corrupted texts, false correlations, trap knowledge |
| **Criminal Investigation** | Witness, physical, behavioral, financial | `discovery_criminal.*` | Medium-Hard | Planted evidence, false witnesses, coincidences |
| **Nature Lore** | Behavioral, environmental, seasonal, ecological | `discovery_nature.*` | Easy-Medium | Mimicry species, coincidental patterns |
| **Dungeon Delving** | Architectural, historical, geological, rumor, memento | `discovery_treasure.*` + `discovery_arcane.*` | Hard | Decoy passages, misleading carvings, false echo chambers |

---

## Skill-Gated Hypothesis Generation

### The Tier Offset Model

Lexicon associations have base discovery tiers that determine when they become visible to a character. Skill-gated hypothesis generation modifies these tiers based on the character's relevant Seed capabilities:

```
effective_discovery_tier = base_tier - skill_offset

Where:
  skill_offset = floor(relevant_seed_domain_growth / tier_offset_threshold)
  tier_offset_threshold = configurable per discovery template (default: 10.0)
```

This means the same mystery has genuinely different difficulty per character:

| Character | Relevant Seed Growth | Skill Offset | Effective Tier for base-4 Association |
|-----------|---------------------|-------------|--------------------------------------|
| Master historian | `discovery_treasure.history: 25.0` | -2 | Tier 2 (sees connections early) |
| Journeyman scholar | `discovery_treasure.history: 10.0` | -1 | Tier 3 (standard discovery) |
| Experienced warrior | `discovery_treasure.history: 0.0` | 0 | Tier 4 (needs all the evidence) |
| Same warrior, combat context | `combat.general: 20.0` | -2 (for combat-related associations) | Tier 2 (recognizes weapon techniques) |

The skill offset applies **per Lexicon association**, matched to the most relevant seed domain for that association's category. A `darkspawn_architecture ↔ old_quarter_foundations` association is categorized under `discovery.treasure_hunting.architectural_clues`, which maps to the `discovery_treasure.architecture` seed domain. A character with high architectural discovery growth sees this association sooner.

### Automatic Fallback

When no relevant seed capability exists for an association's category, the character falls back to the base discovery tier -- they need the maximum evidence to make the connection. The system is **additive**: skills make discovery easier, but any character can eventually get there with enough clues. This prevents hard locks where content is permanently inaccessible.

### Personality Interaction

Hypothesis generation is further modulated by personality traits:

- **High curiosity**: Generates hypotheses more readily (reduces effective tier by additional 0-1)
- **High conscientiousness**: Produces higher-confidence hypotheses (better initial belief confidence in Hearsay)
- **High openness**: More willing to act on hypotheses before full confirmation
- **Low curiosity**: May have all the evidence but never bother connecting it (won't form investigation drives)

A character can be skilled but incurious -- they have the knowledge to solve the puzzle but lack the motivation. A character can be unskilled but curious -- they notice patterns but can't interpret them without help. Both are valid character states that create different gameplay experiences and collaboration opportunities.

---

## The Hearsay "Inference" Channel

### A New Information Channel

Hearsay currently defines six channels for how NPCs acquire beliefs: `direct_observation`, `official_decree`, `trusted_contact`, `social_contact`, `rumor`, and `cultural_osmosis`. Cryptic Trails add a seventh:

| Channel | Initial Confidence | Source | Trigger |
|---------|-------------------|--------|---------|
| `inference` | 0.3-0.8 (computed) | Self-generated | Lexicon hypothesis generation event |

When a character's discovery tier for a Lexicon entry crosses a threshold where a previously hidden association becomes visible, the Lexicon variable provider generates a hypothesis. This hypothesis enters Hearsay as a self-generated belief through the `inference` channel.

### Inference Confidence Computation

```
inference_confidence =
    association_strength                    # How strong the Lexicon association is (0.0-1.0)
  * source_discovery_confidence             # How well the character knows the source entry (0.0-1.0)
  * personality_factor                      # curiosity * conscientiousness modifier
  * skill_factor                            # relevant seed capability modifier
```

A character with high discovery in `darkspawn_architecture` (confidence 0.9) who sees an association of strength 0.7 to `old_quarter_foundations` generates a hypothesis at confidence ~0.63. A character with low discovery (confidence 0.4) seeing the same association generates it at ~0.28. The same logical connection, weighted by how well the character understands the source material.

### Inference Channel Properties

- **Decay rate**: Medium (hypotheses fade without reinforcement, faster than norm beliefs, slower than rumors)
- **Reinforcement**: Can be reinforced by additional evidence from ANY channel. Finding physical evidence that confirms a hypothesis boosts it dramatically.
- **Correction**: If a hypothesis is contradicted by direct observation, it drops sharply -- the character was wrong.
- **Sharing**: When shared via Hearsay (`trusted_contact` or `social_contact`), the recipient receives the hypothesis at reduced confidence. They can independently verify by advancing their own discovery tiers.
- **Personality gating**: Characters with very low curiosity (below a configurable threshold) do not generate inference beliefs at all. They have the knowledge but don't make the leap. Another character might need to point out the connection.

---

## The Investigation Flow: End to End

### Simple Case (A → B): Noises Under the Floorboards

```
1. Character enters tavern with high-perception capability
2. Actor perceives environmental clue: "strange_sounds_below"
   (this is a memento or location property set by the dungeon's existence)
3. Single Hearsay belief forms:
   domain: "location", claimCode: "underground_activity"
   confidence: 0.7 (direct_observation)
4. Character with ANY discovery seed recognizes this as a clue
   Collection: discovery tier 1 for the relevant Lexicon entry
5. Lexicon association at tier 1: "underground_activity → hidden_passage"
   (trivially visible, no skill gate needed)
6. Hypothesis generated: "There might be a hidden passage here"
   Hearsay inference belief, confidence: 0.5+
7. Disposition drive: "investigate_discovery" forms at low intensity
8. Character searches, finds the entrance

Total clues needed: 1
Skill requirement: None (association visible at tier 1)
Time: Minutes
```

### Complex Case: The Darkspawn Tunnels

```
Phase 1: Passive Clue Accumulation (days to weeks)
─────────────────────────────────────────────────
  Clue A: Death memento at tavern
    A miner "fell into a hidden passage" 6 months ago
    → Character examines memento (analysis action)
    → Hearsay belief: "hidden_passages_old_quarter" (conf 0.6, observation)
    → Collection: tier 1 for "old_quarter_foundations"

  Clue B: Resonance sword affix displayHint
    "Forged in depths that predate the city above"
    → Character analyzes item (requires analysis.intermediate capability)
    → Hearsay belief: "pre_settlement_depths" (conf 0.4, observation)
    → Collection: tier 1 for "darkspawn_architecture"

  Clue C: Hearsay rumor (injected by Typhon regional watcher)
    "Miners report scratching sounds under the inn"
    → Hearsay belief: "underground_activity_old_quarter" (conf 0.2, rumor)
    → Reinforces Clue A's belief (compound confidence: 0.68)

  Clue D: NPC historian encounter
    "Valdris the Darkspawn Hunter was buried in this district"
    → Hearsay belief: "valdris_burial_local" (conf 0.5, trusted_contact)
    → Collection: tier 2 for "valdris_tomb"

Phase 2: Active Investigation (days)
─────────────────────────────────────
  Clue E: Ancient text in library (requires literacy + access)
    Contains darkspawn architectural descriptions with illustrations
    → Character studies text (analysis action, requires discovery.architecture)
    → Collection: tier 3 for "darkspawn_architecture"
    → Lexicon traits revealed: "symmetrical_layout", "obsidian_lining",
      "depth_oriented_construction"

  Clue F: Examining cellar walls near the inn
    → Character with architecture skill recognizes construction technique
    → Collection: tier 4 for "darkspawn_architecture"!
    → THIS IS THE THRESHOLD

Phase 3: The "Aha!" Moment (automatic, skill-gated)
────────────────────────────────────────────────────
  Lexicon association NOW VISIBLE:
    "darkspawn_architecture" ↔ "old_quarter_foundations"
    (base tier: 5, but character has skill offset -1 from
     discovery_treasure.architecture growth = effective tier 4)

  Hypothesis generated:
    "The old quarter foundations may be darkspawn construction"
    → Hearsay inference belief (conf 0.55, inference channel)
    → Compounds with existing beliefs about hidden passages,
      underground activity, and pre-settlement depths
    → Total compound confidence across related beliefs: 0.75+

  Disposition response:
    → Curiosity spike (base feeling synthesis incorporates
       inference excitement)
    → Drive formed: "investigate_mystery"
       intensity: 0.6 (proportional to compound belief confidence
                       + personality.curiosity)
       originType: "inference"

Phase 4: Investigation (character-driven behavior)
──────────────────────────────────────────────────
  Character's GOAP planner prioritizes investigation goals:
    → Seeks access to the old quarter's lower levels
    → Looks for the specific cellar/passage
    → May share findings with allies (Hearsay propagation)

Phase 5: God-Actor Response (optional orchestration)
────────────────────────────────────────────────────
  Regional watcher perceives disposition.drive.formed event:
    → Evaluates: character has sufficient evidence and drive
    → Option A: Create formal Quest via /quest/definition/create
      "Investigate the Darkspawn Tunnels"
    → Option B: Manifest catalyst (ground trembles, revealing a crack)
    → Option C: Let the character find it naturally through exploration
    → Option D: Create complications (dungeon defenses activate,
      a rival investigator appears)
```

### Extreme Case: The Valdris Tomb Connection

Building on the darkspawn tunnel discovery, the **full** mystery requires connecting three Lexicon entries through multi-hop associations:

```
darkspawn_architecture ─(tier 4)─► old_quarter_foundations
old_quarter_foundations ─(tier 5)─► valdris_tomb
darkspawn_architecture ─(tier 6)─► valdris_tomb  (the deep connection)
```

The tier-6 association reveals: Valdris didn't just fight darkspawn -- he was buried IN their tunnels, and the tomb itself is constructed in darkspawn style because he learned their techniques. This requires a character to reach discovery tier 6 for `darkspawn_architecture`, which means:

- A master historian (skill offset -2) needs tier 4 worth of evidence
- A journeyman (offset -1) needs tier 5
- An unskilled character needs tier 6 -- practically every clue in the template

The reward scales with difficulty: finding the tomb entrance (not just the tunnels) leads to deeper content, richer resonance items, and more historical material for the content flywheel.

---

## Red Herrings: Template-Defined False Trails

### How Red Herrings Work

Each Discovery Template defines red herring categories as Lexicon entries with specific traits. When a character encounters a red herring clue, it enters their knowledge system identically to a real clue -- same Hearsay belief formation, same Collection discovery advancement. The distinction is in the Lexicon traits:

**Real clue Lexicon entry:**
```
entry: "architectural_anomaly_inn_cellar"
  category: "discovery.treasure_hunting.architectural_clues"
  traits:
    - "pre_settlement_construction" (confidence: 1.0)
    - "darkspawn_technique_match" (confidence: 0.9)
    - "structural_integrity_maintained" (confidence: 1.0)
  associations:
    → "darkspawn_architecture" (strength: 0.7, discovery_tier: 3)
```

**Red herring Lexicon entry:**
```
entry: "architectural_anomaly_market_cellar"
  category: "discovery.treasure_hunting.architectural_clues"
  traits:
    - "pre_settlement_construction" (confidence: 0.8)  # Looks right...
    - "natural_cave_formation" (confidence: 0.6)        # But actually natural
    - "suspicious_provenance" (confidence: 0.4,
       discovery_tier: 3)                                # Detectable at higher skill
    - "inconsistent_dating" (confidence: 0.5,
       discovery_tier: 4)                                # Clearly false at expert level
  associations:
    → "darkspawn_architecture" (strength: 0.2, discovery_tier: 2)  # Weak, visible early
    → "natural_geology" (strength: 0.8, discovery_tier: 4)         # True association, hidden
```

### Detection Through Skill

Red herrings are detectable, not arbitrary:

| Skill Phase | Red Herring Interaction |
|---|---|
| **Curious** (no detection capability) | Accepts red herring clues at face value. Forms false beliefs. Pursues dead ends. |
| **Seeker** (`detect_simple_red_herrings`) | Notices `suspicious_provenance` traits (discovery tier 3). Realizes "this doesn't quite fit" for obvious red herrings. |
| **Investigator** (`hypothesis_generation`) | Can cross-reference red herring with other evidence. Conflicting hypotheses flag suspicious clues. |
| **Expert** (`detect_complex_red_herrings`) | Sees `inconsistent_dating` traits (discovery tier 4). Can definitively identify planted evidence and misleading inscriptions. |
| **Master** (`predict_red_herrings`) | Anticipates red herring patterns based on template knowledge. Can warn others about likely false trails. |

### The Red Herring Lifecycle

```
1. God-actor distributes red herring alongside real clues
   (ratio configurable per template, default ~30% of distributed clues)

2. Unskilled character accepts red herring:
   → Hearsay belief formed with moderate confidence
   → May pursue false lead (GOAP investigation)
   → Time spent, possibly danger encountered
   → Eventually: contradiction discovered via direct observation
   → Hearsay belief CORRECTED (confidence drops sharply)
   → Disposition: frustration increases for investigation drive
   → Character learns to be more careful (personality evolution)

3. Skilled character detects red herring:
   → Analysis reveals `suspicious_provenance` trait
   → Hearsay belief formed at LOW confidence with "suspected_false" flag
   → Or: belief correction triggered immediately
   → Disposition: satisfaction from catching deception
   → Seed growth: discovery skill advances from successful detection
   → Character can warn others (Hearsay propagation of the correction)
```

Red herrings serve the content flywheel: the experience of being fooled and learning from it creates character development moments (personality evolution), seeds for future encounters (the character who was tricked becomes cautious), and investigation skill growth (the failure teaches detection).

---

## Collaboration: Sharing Investigation Progress

### The Treasure Hunter's Guild Pattern

Discovery Templates define **clue categories**. A character knows what categories exist in their investigation (from their seed's domain list) and can identify which categories they have evidence for and which they lack. This creates natural collaboration:

```
Hunter A (strong in cartography + history):
  ✓ cartographic_clues (tier 3)
  ✓ historical_clues (tier 4)
  ✗ geological_clues (tier 0)
  ✗ architectural_clues (tier 1)
  ✓ rumor_clues (tier 2)

Hunter B (strong in geology + architecture):
  ✗ cartographic_clues (tier 1)
  ✗ historical_clues (tier 0)
  ✓ geological_clues (tier 4)
  ✓ architectural_clues (tier 3)
  ✓ rumor_clues (tier 3)
```

When Hunter A shares historical findings with Hunter B via conversation:

```
Hearsay propagation:
  A's historical knowledge → B receives as "trusted_contact" belief
  Confidence: proportional to A's discovery tier × relationship trust

Collection advancement:
  B's discovery for historical Lexicon entries advances by 1 tier
  (verbal teaching = reduced-rate advancement per Lexicon deep dive)

Result:
  B now has tier 1 historical clues from A's testimony
  B would need to verify (examine the actual texts) for higher tiers
  But the shared knowledge may be enough to cross an association threshold
```

A **guild** of treasure hunters pools Hearsay beliefs. Each member contributes their specialty. The guild's collective knowledge exceeds any individual's -- but each member still needs personal skill to generate hypotheses from shared information. A character who receives all the clues secondhand through Hearsay has lower confidence per belief than someone who discovered them firsthand. Verification (examining the evidence yourself) increases confidence.

This creates three tiers of investigator:

| Tier | How They Investigate | Effectiveness |
|---|---|---|
| **Solo** | Finds all clues personally. Slow but high confidence. Maximum skill growth. | Thorough but time-intensive |
| **Collaborative** | Shares clues with allies. Faster coverage. Moderate confidence (must verify key findings). | Balanced speed and reliability |
| **Social** | Relies on secondhand Hearsay from many sources. Fast but low confidence. Vulnerable to red herrings propagated through the social network. | Fast but unreliable |

---

## Service Integration Map

### Which Services Handle What

```
                    Discovery Template
                    (Lexicon categories + Seed types + ABML behaviors)
                              │
              ┌───────────────┼───────────────┐
              │               │               │
              ▼               ▼               ▼
       Lexicon (L4)    Seed (L2)       God-Actor ABML
       Entry ontology  Skill tracking  Clue distribution
       Associations    Capabilities    Red herring placement
       Hypothesis gen  Tier offsets    Progress evaluation
              │               │               │
              ├───────┬───────┤               │
              │       │       │               │
              ▼       ▼       ▼               ▼
       Collection (L2)    Hearsay (L4)    Disposition (L4)
       Discovery tiers    Belief storage  Drive formation
       Per-character      Inference chan.  Investigation
       progression        Sharing/prop.   motivation
              │               │               │
              └───────────────┼───────────────┘
                              │
                              ▼
                        Actor (L2)
                        ${lexicon.*}, ${hearsay.*},
                        ${disposition.*} variables
                        GOAP investigation goals
                              │
                    ┌─────────┼─────────┐
                    │         │         │
                    ▼         ▼         ▼
              Quest (L2)  Agency (L4)  Gardener (L4)
              Formal       Player UX    Investigation
              objectives   gating       experience
              (optional)   (spirit)     orchestration
```

### Clue Sources (Existing Channels)

| Source | Service | How It Becomes a Clue | Example |
|--------|---------|----------------------|---------|
| Memento items at locations | Inventory (L2) + Item (L2) | Character examines memento → analysis action → Collection advancement + Hearsay belief | Death memento: "fell into hidden passage" |
| Resonance item affix displayHints | Affix (L4) | Character analyzes item → affix revealed → embedded Lexicon references advance discovery | "Forged in depths that predate the city" |
| NPC conversation | Hearsay (L4) | Encounter triggers belief propagation → `trusted_contact` or `social_contact` belief | Historian: "Valdris was buried here" |
| Environmental observation | Actor (L2) perception | Actor perceives clue → `direct_observation` belief → Collection advancement | Strange sounds beneath floorboards |
| Written documents | Item (L2) customStats | Character reads document → analysis action → high-confidence belief + Collection advancement | Ancient text describing darkspawn construction |
| Rumor propagation | Hearsay (L4) | God-actor injects rumor → propagation wave → multiple characters receive low-confidence beliefs | "Scratching sounds under the inn" |
| Self-generated hypothesis | Lexicon (L4) → Hearsay (L4) | Discovery tier crosses association threshold → inference belief auto-generated | "The foundations may be darkspawn tunnels" |

### Service-Level Changes Required

| Service | Change Type | Description |
|---------|------------|-------------|
| **Hearsay (L4)** | New channel type | Add `inference` to channel configuration: self-generated beliefs from Lexicon hypothesis events |
| **Hearsay (L4)** | New event subscription | Subscribe to `lexicon.hypothesis.generated` events to create inference beliefs |
| **Lexicon (L4)** | New event | Publish `lexicon.hypothesis.generated` when a character's discovery tier crosses an association's visibility threshold |
| **Lexicon (L4)** | Tier offset support | Accept optional skill offset parameter in variable provider and discovery queries, sourced from Seed capabilities |
| **Lexicon (L4)** | Personality gating | Hypothesis generation conditioned on personality.curiosity exceeding configurable threshold |
| **Collection (L2)** | No code change | Discovery tier tracking already exists; new discovery types are seeded data |
| **Seed (L2)** | No code change | New seed types (`discovery_treasure`, etc.) are seeded data |
| **Disposition (L4)** | No code change | Drive formation from accumulated belief convergence is existing behavior; `investigate_mystery` drive code is seeded data |
| **Actor (L2)** | No code change | Variable providers already discovered via DI; new variables come from existing Lexicon/Hearsay/Disposition providers |
| **Quest (L2)** | No code change | Quest creation from god-actor ABML is existing behavior |
| **Agency (L4)** | No code change | Investigation UX modules are seeded domain/module definitions |
| **Gardener (L4)** | No code change | Investigation experience orchestration uses existing garden event routing |

### What Is NOT Changed

- **No new plugins**: Everything composes from existing services
- **No changes to code generation scripts**: All extensions are manual service code within existing plugins
- **No hierarchy violations**: All data flows respect the layer model. Lexicon (L4) generates hypothesis events; Hearsay (L4) consumes them; Collection (L2) and Seed (L2) provide data upward via existing provider patterns.
- **No new state stores**: Inference beliefs use existing Hearsay belief storage. Discovery tiers use existing Collection storage. Skill progression uses existing Seed storage.
- **No changes to Item, Inventory, Affix, or Loot**: Clue data is carried in existing `customStats` fields, existing `displayHint` fields, and existing memento item structures

---

## God-Actor Evaluation Behavior

### Clue Distribution

God-actors (regional watchers, dungeon cores) distribute clues through existing channels. The ABML behavior template handles both organic and orchestrated distribution:

```yaml
# Regional watcher distributing treasure hunting clues
distribute_investigation_clues:
  when:
    condition: |
      ${event.type} == 'worldstate.day-period.changed'
      AND ${event.dayPeriod} == 'dawn'
    # Periodic evaluation: once per game-day
  actions:
    # What active mysteries exist in my region?
    - query:
        service: inventory
        endpoint: /inventory/container/list-items
        params:
          container_id: ${my_region.mystery_seed_container}
          template_code: "MYSTERY_SEED"
        result_var: active_mysteries

    - for_each: ${active_mysteries}
      as: mystery
      actions:
        # Who in my region might find clues for this mystery?
        - query:
            service: character
            endpoint: /character/list-by-realm
            params:
              realmId: ${my_region.realm_id}
              locationId: ${mystery.custom_stats.location_id}
            result_var: nearby_characters

        - for_each: ${nearby_characters}
          as: character
          actions:
            # What clue categories is this character missing?
            - query:
                service: collection
                endpoint: /collection/entry/list-by-owner
                params:
                  ownerId: ${character.character_id}
                  ownerType: "character"
                  collectionType: "lexicon_discovery"
                  entryCodePrefix: "discovery.${mystery.custom_stats.template}"
                result_var: char_progress

            - compute:
                missing_categories: |
                  ${mystery.custom_stats.clue_categories}
                  | filter(c => !${char_progress}.has_entry_with_prefix(c))

            # Should we distribute a clue or red herring?
            - cond:
                # Character is actively investigating (has investigation drive)
                - when: |
                    ${missing_categories.count} > 0
                    AND ${random(0, 100)} < 20  # 20% chance per day
                  then:
                    - random:
                        - weight: ${100 - mystery.custom_stats.red_herring_rate}
                          action: place_real_clue
                          params:
                            character_id: ${character.character_id}
                            category: ${random_choice(missing_categories)}
                            mystery: ${mystery}
                        - weight: ${mystery.custom_stats.red_herring_rate}
                          action: place_red_herring
                          params:
                            character_id: ${character.character_id}
                            category: ${random_choice(missing_categories)}
                            mystery: ${mystery}

place_real_clue:
  actions:
    # Choose a clue delivery method appropriate to the category
    - cond:
        - when: "${params.category} == 'rumor_clues'"
          then:
            # Inject a rumor about the mystery
            - service_call:
                service: hearsay
                endpoint: /hearsay/rumor/inject
                params:
                  locationId: ${params.mystery.custom_stats.location_id}
                  domain: "location"
                  claimCode: ${params.mystery.custom_stats.rumor_claim_codes
                              | random_choice()}
                  believedValue: 0.6
                  targetRadius: 500
                  propagationSpeed: "medium"

        - when: "${params.category} == 'historical_clues'"
          then:
            # Place a document item in a library or scholar's shop
            - service_call:
                service: item
                endpoint: /item/instance/create
                params:
                  templateCode: "historical_document"
                  gameServiceId: ${game_service_id}
                  customStats:
                    mystery_id: ${params.mystery.item_instance_id}
                    clue_category: "historical"
                    lexicon_entry: ${params.mystery.custom_stats.historical_entry}
                    content_summary: ${generate_clue_text(params.mystery, "historical")}
                result_var: document_item
            - service_call:
                service: inventory
                endpoint: /inventory/item/move
                params:
                  itemInstanceId: ${document_item.item_instance_id}
                  targetContainerId: ${find_nearby_library(params.mystery)}

        # ... similar for other categories

place_red_herring:
  actions:
    # Same delivery mechanisms, but clue content is misleading
    - compute:
        herring_type: ${params.mystery.custom_stats.red_herring_types
                       | filter(t => t.category == params.category)
                       | random_choice()}
    # Generate misleading version of the clue
    # Uses the red herring Lexicon entries with suspicious_provenance traits
    # ...
```

### Progress Evaluation

```yaml
# God-actor evaluates when a character has enough evidence
evaluate_investigation_progress:
  when:
    condition: |
      ${event.type} == 'disposition.drive.formed'
      AND ${event.driveCode} == 'investigate_mystery'
  actions:
    # A character just formed an investigation drive!
    # Check if they've accumulated enough for a formal quest
    - query:
        service: hearsay
        endpoint: /hearsay/belief/query
        params:
          characterId: ${event.characterId}
          domain: "location"
          minConfidence: 0.3
        result_var: location_beliefs

    - query:
        service: seed
        endpoint: /seed/get-by-owner
        params:
          ownerId: ${event.characterId}
          ownerType: "character"
          typeCode: "discovery_treasure"
        result_var: discovery_skill

    # Decide response based on skill + evidence quality
    - cond:
        # Expert investigator with strong evidence: offer the quest
        - when: |
            ${discovery_skill.currentPhase} IN ['Expert', 'Master']
            AND ${location_beliefs | filter(b => b.confidence > 0.5) | count()} >= 3
          then:
            - goto: create_investigation_quest

        # Moderate investigator: provide a catalyst
        - when: |
            ${discovery_skill.currentPhase} IN ['Investigator', 'Seeker']
            AND ${location_beliefs | filter(b => b.confidence > 0.3) | count()} >= 4
          then:
            - goto: manifest_catalyst

        # Novice or insufficient evidence: let them keep searching
        - otherwise:
            - noop  # Character continues investigating autonomously

create_investigation_quest:
  actions:
    - service_call:
        service: quest
        endpoint: /quest/definition/create
        params:
          code: "investigate_${mystery_id}"
          gameServiceId: ${game_service_id}
          displayName: "The Hidden Depths"
          description: "Your investigation points to something beneath the old quarter..."
          objectives:
            - code: "find_entrance"
              description: "Locate the entrance to the tunnels"
              # ... quest structure
```

---

## Hierarchy Compliance

All data flows respect the service hierarchy:

| Component | Layer | Depends On | Direction | Compliance |
|-----------|-------|-----------|-----------|------------|
| Lexicon | L4 | Collection (L2) | L4 → L2 | Allowed (downward) |
| Lexicon | L4 | Hearsay (L4) | L4 → L4 | Allowed (soft, graceful degradation) |
| Hearsay | L4 | Character (L2) | L4 → L2 | Allowed (downward) |
| Hearsay | L4 | Lexicon (L4) | L4 → L4 | Allowed (soft, graceful degradation) |
| Disposition | L4 | Hearsay (L4) | L4 → L4 | Allowed (soft, graceful degradation) |
| Disposition | L4 | Character-Personality (L4) | L4 → L4 | Allowed (soft) |
| Actor | L2 | Lexicon/Hearsay/Disposition providers | L2 ← L4 via DI | Provider pattern (allowed) |
| Collection | L2 | Nothing new | - | No new dependencies |
| Seed | L2 | Nothing new | - | No new dependencies |
| Quest | L2 | Nothing new | - | No new dependencies |

The `lexicon.hypothesis.generated` event is published by Lexicon (L4) and consumed by Hearsay (L4) -- same-layer event consumption is allowed with graceful degradation. If Hearsay is not loaded, hypotheses are generated but don't enter the belief system (they remain as Lexicon variable provider data only). If Lexicon is not loaded, no hypotheses are generated and investigation falls back to pure Hearsay belief accumulation (the system degrades gracefully to simpler discovery patterns).

---

## Connection to the Content Flywheel

### The Investigation Flywheel

Cryptic Trails add a quaternary flywheel loop to the existing three:

```
Primary Flywheel (Narrative):
  Character Dies → Archive → Storyline → Quest → Player Experiences → Loop

Secondary Flywheel (Spiritual, from MEMENTO-INVENTORIES):
  Character Dies → Memento at Location → Spiritual Ecology → Encounters → Loop

Tertiary Flywheel (Equipment, from LOGOS-RESONANCE-ITEMS):
  Formative Event → Resonance Item → Prerequisites → Gameplay Goals → Loop

Quaternary Flywheel (Knowledge):
  World Events → Clue Traces (mementos, items, rumors, documents)
      → Investigation → Discovery → New Content Unlocked
      → Content generates NEW events → More clue traces
      → Richer investigations → Deeper discoveries → Loop
```

### Year 1 vs Year 5

| Metric | Year 1 | Year 5 |
|--------|--------|--------|
| Average clue density per location | 0-2 | 5-20 |
| Average Lexicon association depth | 2-3 hops | 5-8 hops |
| Active mystery seeds per region | 2-5 | 20-50 |
| Red herring sophistication | Simple (decoys) | Complex (multi-layered misdirection) |
| Multi-investigator collaboration | Rare (few specialists) | Common (guilds, schools, academies) |
| Cross-generational clue chains | Impossible | Natural (ancestor's notes, family archives) |
| NPC investigation behavior | Basic (simple A→B) | Rich (NPCs pursuing multi-step investigations autonomously) |

A Year 5 world has dungeons whose secrets are layered in decades of historical sediment. A character investigating a hidden dungeon might find clues left by three generations of failed investigators -- each attempt adding mementos, rumors, and partial discoveries that make the next attempt richer. The puzzle literally improves with age.

### Cross-Generational Knowledge

When a character dies and is compressed via lib-resource, their Hearsay beliefs and Collection discoveries are archived. A descendant character in the same household might inherit:

- **Family documents**: Items in household inventory containing the ancestor's investigation notes (customStats with Lexicon entry references)
- **Family rumors**: Hearsay beliefs seeded at low confidence through `cultural_osmosis` channel -- "grandmother always said there was something under the old quarter"
- **Inherited seed growth**: Guardian spirit's discovery seed capabilities carry across generations (the spirit learned investigation skills that persist)

The ancestor's partial investigation becomes a clue source for the descendant. A mystery that took three generations to solve -- each adding pieces, each dying before completion -- is peak content flywheel. The puzzle IS the family's multi-generational story.

---

## Client-Side Presentation

### Investigation Journal

When a player's Agency manifest includes investigation UX modules, the client renders an investigation journal:

```
╔══════════════════════════════════════════════════════╗
║  INVESTIGATION: The Old Quarter Mystery               ║
║  Template: Treasure Hunting                           ║
║  Status: Active (5 clues gathered)                    ║
╠══════════════════════════════════════════════════════╣
║                                                       ║
║  EVIDENCE GATHERED:                                   ║
║                                                       ║
║  ◆ Cartographic Clues         [████████░░░░] Tier 3  ║
║    ○ Old map fragment from library       ✓ Verified   ║
║    ○ Miner's sketch of cave system       ✓ Verified   ║
║                                                       ║
║  ◆ Historical Clues           [██████████░░] Tier 4  ║
║    ○ Valdris burial account              ✓ Verified   ║
║    ○ Darkspawn campaign records          ✓ Verified   ║
║    ○ Ancient construction treatise       ✓ Verified   ║
║                                                       ║
║  ◇ Geological Clues           [██░░░░░░░░░░] Tier 1  ║
║    ○ "Unusual soil composition nearby"   ◐ Rumor      ║
║                                                       ║
║  ◆ Architectural Clues        [████████████] Tier 4  ║
║    ○ Cellar wall analysis                ✓ Verified   ║
║    ○ Construction technique matched      ✓ Verified   ║
║                                                       ║
║  ◇ Rumors & Hearsay           [████░░░░░░░░] Tier 2  ║
║    ○ Scratching sounds (innkeeper)       ◐ Rumor      ║
║    ○ Missing miners (tavern gossip)      ◐ Rumor      ║
║                                                       ║
║  ⚠ SUSPECTED FALSE LEADS:                            ║
║    ✗ "Treasure in the market cellars"    Debunked     ║
║      (Natural cave formation, not constructed)        ║
║                                                       ║
╠══════════════════════════════════════════════════════╣
║  HYPOTHESES:                                          ║
║                                                       ║
║  ★ "The old quarter foundations are darkspawn          ║
║     construction"                                     ║
║     Confidence: ████████░░ 78%                        ║
║     Based on: architectural analysis + historical     ║
║     records + rumor corroboration                     ║
║                                                       ║
║  ☆ "Valdris's tomb may be accessible through           ║
║     the tunnel network"                               ║
║     Confidence: ████░░░░░░ 42%                        ║
║     Based on: burial account + location proximity     ║
║     Needs: more geological or historical evidence     ║
║                                                       ║
╠══════════════════════════════════════════════════════╣
║  DISCOVERY SKILL: Treasure Hunter's Instinct          ║
║    Phase: Investigator (growth: 18.4)                 ║
║    Capabilities: Deep analysis, Hypothesis gen,       ║
║                  Share findings effectively            ║
║    Next phase: Expert at 40.0                         ║
╚══════════════════════════════════════════════════════╝
```

### Progressive Revelation (Agency-Gated)

| Spirit Agency Level | Journal View |
|---|---|
| Minimal | No journal. Character "has a feeling about this place" (vague nudge only). |
| Low | Clue count visible. "You've noticed 5 curious things about the old quarter." No categories or details. |
| Medium | Categories visible with tier bars. Hypotheses shown as vague intuitions ("Something about the foundations..."). |
| High | Full journal as shown above. Hypothesis confidence, clue details, suspected false leads. |
| Mastery | Optimal investigation paths suggested. "Geological evidence would strengthen the tunnel hypothesis." Cross-reference suggestions between investigations. |

---

## Implementation Sequence

### Phase 0: Prerequisites

Lexicon and Hearsay must be implemented to their baseline specifications before Cryptic Trails functionality is possible. Collection, Seed, and Disposition must be implemented to their current specifications. This phase is implicit -- it represents the existing implementation roadmap for these services.

**Result**: The foundation services exist and provide their standard functionality.

### Phase 1: Hearsay Inference Channel

1. Add `inference` channel type to Hearsay's channel configuration model
2. Define inference channel properties: confidence computation, decay rate, personality gating
3. Add `HandleHypothesisGeneratedAsync` event handler for `lexicon.hypothesis.generated` events
4. Inference beliefs stored identically to other beliefs, distinguished by `sourceChannel: "inference"`

**Result**: Hearsay can receive and store self-generated beliefs from Lexicon hypothesis events.

### Phase 2: Lexicon Hypothesis Events and Skill Gating

5. Add `lexicon.hypothesis.generated` event to Lexicon event schema
6. Implement hypothesis generation trigger: when a character's effective discovery tier crosses an association's visibility threshold, publish the event
7. Implement skill-gated tier offset: accept Seed capability data in discovery tier calculations
8. Implement personality gating: hypothesis generation conditioned on `personality.curiosity` threshold
9. Extend Lexicon variable provider to include hypothesis data (`${lexicon.TARGET.hypothesis.*}`)

**Result**: Characters generate hypotheses from Lexicon associations, gated by skill and personality. Hypotheses flow to Hearsay as inference beliefs.

### Phase 3: Discovery Template Seeding Infrastructure

10. Design and seed Lexicon category hierarchies for initial discovery templates (treasure hunting, resource prospecting, arcane research)
11. Design and seed Seed type definitions for discovery domains (`discovery_treasure`, `discovery_prospecting`, `discovery_arcane`)
12. Design and seed Collection types for discovery tracking per template
13. Define red herring Lexicon entries with detection traits per template

**Result**: The investigation methodology patterns exist as seeded data. Characters can grow discovery skills and track investigation progress.

### Phase 4: Item Analysis Action

14. Define "analyze" ABML action handler that queries item customStats and affix data, maps to Lexicon entries, and advances Collection discovery
15. Define skill-gated analysis depth (what tiers of information are revealed based on Seed capabilities)
16. Integrate with Agency UX manifest for player-side information gating

**Result**: Characters can examine items to extract clues. Analysis depth scales with skill.

### Phase 5: God-Actor Investigation Behaviors

17. Author ABML behavior templates for regional watchers distributing investigation clues
18. Author ABML behavior templates for dungeon core actors seeding mystery clues in their environment
19. Author ABML behavior templates for progress evaluation and quest/catalyst creation
20. Define red herring distribution logic in ABML behaviors

**Result**: God-actors autonomously distribute clues, plant red herrings, and respond to investigation progress.

### Phase 6: Collaboration and Sharing

21. Ensure Hearsay belief sharing via `trusted_contact` correctly propagates investigation-relevant beliefs
22. Extend Lexicon's verbal teaching model to handle investigation clue sharing (reduced-tier advancement)
23. Author ABML behaviors for NPC investigator characters who pursue mysteries and share findings

**Result**: Characters can collaborate on investigations through natural social interaction.

### Phase 7: Client-Side Integration

24. Design investigation journal UI module for Agency
25. Implement progressive revelation based on Agency manifest level
26. Integrate Gardener orchestration for investigation experience events

**Result**: Players see investigation progress through the spirit's UX, gated by Agency progression.

---

## Open Questions

1. **Mystery seed lifecycle**: When a dungeon spawns beneath a city, who creates the "mystery seed" that god-actors reference for clue distribution? Is it the dungeon's own actor (it wants to be found, or doesn't), the regional watcher (orchestrating content), or an automatic process triggered by the dungeon spawn event? Each has different implications for mystery character -- a dungeon that WANTS to lure adventurers would distribute different clues than a watcher who wants to challenge investigators.

2. **Investigation drive specificity**: Should the `investigate_mystery` Disposition drive be generic or specific to a particular mystery? A character investigating two mysteries simultaneously would either have two specific drives competing for GOAP priority, or one generic drive with the GOAP planner choosing which mystery to pursue based on current evidence quality. The specific model is richer but increases Disposition state per character.

3. **Cross-template clue overlap**: A geological clue might be relevant to both a treasure hunting investigation and a resource prospecting investigation. Should clues advance discovery for multiple templates simultaneously, or should they be categorized under one template? Simultaneous advancement is more realistic (the same rock formation tells you about both minerals and construction) but complicates progress tracking.

4. **NPC investigator competition**: When NPCs pursue investigations autonomously, should they compete with or complement player characters? A master NPC historian might solve a mystery before the player gets there. This could be frustrating or fascinating depending on execution. The NPC's solution could generate a new mystery (what did they find?) or the NPC could become a collaborator/rival depending on relationship state.

5. **Analysis action frequency and cost**: How often can a character analyze an item? Is there a time cost, a material cost (reagents for deep analysis), or a skill cooldown? Too cheap and characters analyze everything they touch. Too expensive and investigation becomes tedious. The answer likely varies by analysis depth -- basic inspection is free, deep analysis requires tools and time, master analysis requires rare materials or special locations (a scholar's workshop, a mage's laboratory).

6. **Hypothesis confidence calibration**: How should inference confidence be tuned to avoid either "characters figure everything out instantly" (too generous) or "characters never make connections" (too stingy)? This likely needs playtesting, but the configurable parameters (association strengths, tier offset thresholds, personality factors) provide tuning knobs. The default should err toward "too stingy" -- missing a connection is a natural character limitation, while premature certainty breaks mystery pacing.

7. **Template extensibility at runtime**: Can game designers add new Discovery Templates after launch through the existing seeding APIs (Lexicon category creation, Seed type registration, Collection type registration), or does a new template require code changes? The architecture suggests pure seeding should work, but the ABML behavior templates for god-actors would need to reference the new template's categories. This might require uploading new ABML behaviors via the Asset service.

---

*This document describes a design that composes from existing Bannou primitives. The only service-level extensions are the Hearsay inference channel, Lexicon hypothesis events with skill-gated tier offsets, and Lexicon personality gating for hypothesis generation. Everything else -- Discovery Templates, clue distribution, red herrings, collaboration, investigation progression -- is seeded data and ABML behavior authoring. The puzzles are as unique and unreproducible as the world history that creates them.*
