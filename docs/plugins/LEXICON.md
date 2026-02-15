# Lexicon Plugin Deep Dive

> **Plugin**: lib-lexicon (not yet created)
> **Schema**: `schemas/lexicon-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: lexicon-entries (MySQL), lexicon-cache (Redis), lexicon-lock (Redis) — all planned
> **Layer**: L4 GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Planning**: [VISION.md](../reference/VISION.md) (Logos metaphysical framework), [COMPRESSION-GAMEPLAY-PATTERNS.md](../planning/COMPRESSION-GAMEPLAY-PATTERNS.md) (identify spell concept), [CHARACTER-COMMUNICATION.md](../guides/CHARACTER-COMMUNICATION.md) (Lexicon-shaped NPC social interaction)

## Overview

Structured world knowledge ontology (L4 GameFeatures) that defines what things ARE in terms of decomposed, queryable characteristics. Lexicon is the ground truth registry for entity concepts -- species, objects, phenomena, named individuals, abstract ideas -- broken into traits, hierarchical categories, bidirectional associations, and strategy-relevant implications. It answers "what is a wolf?" not with prose, but with structured data that GOAP planners, behavior expressions, and game systems can reason over. Game-agnostic: entries, trait vocabularies, category hierarchies, and strategy implications are all configured through seed data at deployment time (the True Names metaphysical framework is Arcadia's flavor interpretation of lexicon entry codes). Internal-only, never internet-facing.

---

## Core Concepts

NPCs currently know about themselves (personality, encounters, backstory, drives) but not about the world around them conceptually. A character can feel afraid (via Disposition) but has no structured reason to be afraid of wolves specifically versus rabbits. There is no mechanism for "wolves hunt in packs, so I should not be alone" or "I see a wolf, and the Witch of the Wolves summons wolves, so the witch may be nearby." Game entities exist as IDs in service databases, but their characteristics, category relationships, and strategic implications are nowhere in the system.

Three services together form the complete knowledge system: **Lexicon** = what things objectively ARE (ground truth, structured characteristics), **Collection** = what you have personally DISCOVERED (per-character progressive unlock tracking), **Hearsay** = what you BELIEVE things are (subjective, spreadable, potentially false). An NPC's effective knowledge about wolves at runtime is: Lexicon's ground truth, gated by Collection's discovery level, overlaid by Hearsay's beliefs. A character who has never seen a wolf but heard rumors might have Hearsay beliefs ("wolves breathe fire") that contradict Lexicon ground truth. A character who has studied wolves extensively has high Collection discovery and accesses deep Lexicon data. The Actor's variable provider composites all three.

### The Four Pillars

Lexicon organizes world knowledge into four interconnected structures:

```
                    ┌──────────────────────────────────┐
                    │           CATEGORIES              │
                    │  (hierarchical classification)     │
                    │                                    │
                    │  animal                            │
                    │  └── mammal                        │
                    │      └── quadruped_mammal          │
                    │          └── canine                │
                    └──────────────┬───────────────────┘
                                   │ categorized as
                    ┌──────────────▼───────────────────┐
                    │           ENTRIES                  │
                    │  (things that can be known about)  │
                    │                                    │
                    │  wolf, fire, iron, moonstone,      │
                    │  The Witch of the Wolves,          │
                    │  pack_hunting, climbing            │
                    └──┬───────────────────────────┬──┘
                       │ has                        │ linked to
        ┌──────────────▼───────────────┐  ┌────────▼──────────────────┐
        │           TRAITS              │  │       ASSOCIATIONS        │
        │  (structured characteristics) │  │  (typed bidirectional     │
        │                               │  │   links between entries)  │
        │  fur, four_legged,            │  │                           │
        │  pack_hunter, predator,       │  │  wolf ↔ Witch of Wolves  │
        │  muted_colors, long_teeth     │  │  (summoned_by / summons) │
        │                               │  │                           │
        │  Each trait has a scope       │  │  wolf ↔ dog               │
        │  (which categories share it)  │  │  (related_species)        │
        └──────────────┬───────────────┘  └───────────────────────────┘
                       │ implies
        ┌──────────────▼───────────────┐
        │         STRATEGIES            │
        │  (action-relevant knowledge   │
        │   linked to traits/categories)│
        │                               │
        │  climb_to_escape              │
        │    works_against: ground_bound│
        │    requires: nearby_climbable │
        │                               │
        │  make_loud_noise              │
        │    works_against: noise_averse│
        │    requires: nothing          │
        └───────────────────────────────┘
```

### Entries

An entry is anything that can be known about. Entries are not limited to physical entities -- abstract concepts, named individuals, phenomena, behaviors, and materials are all valid entries:

```
LexiconEntry:
  entryCode:       string        # Unique identifier (opaque string, not enum)
  gameServiceId:   Guid          # Scope to a specific game service
  displayKey:      string        # Localization key for display name
  categoryCode:    string        # Primary category this entry belongs to
  entryType:       string        # "species", "object", "material", "phenomenon",
                                 # "individual", "behavior", "concept" (opaque, extensible)
  sourceType:      string?       # Optional: cross-reference to source service entity type
  sourceId:        string?       # Optional: cross-reference to source service entity ID
  discoveryLevels: int           # How many progressive tiers of information exist (1-10)
  createdAt:       DateTime
```

Entry codes are the lookup keys. In Arcadia's metaphysical interpretation, entry codes ARE the True Names -- the logos identifiers for what things are. But lib-lexicon is agnostic to that interpretation.

**Source cross-referencing**: Entries can optionally link to entities in other services via `sourceType` + `sourceId`. A lexicon entry for "wolf" can reference the Species service's wolf species ID. This enables bidirectional queries: "what does Lexicon say about this Species?" and "what Species does this Lexicon entry correspond to?" The cross-reference is informational, not a dependency -- Lexicon does not call the Species service.

### Traits

Traits are structured characteristics that entries possess. A trait is not a free-text description but a code with typed values:

```
LexiconTrait:
  traitCode:       string        # Unique identifier (opaque string)
  entryCode:       string        # Which entry has this trait
  value:           string?       # Optional value (e.g., "gray" for color, "4" for leg_count)
  confidence:      float         # 0.0-1.0 how definitively this trait applies (1.0 = always)
  scopeCode:       string?       # Category at which this trait is shared (null = entry-specific)
  discoveryTier:   int           # At what Collection discovery level this trait is revealed
```

**Trait scoping through categories**: The `scopeCode` field is what enables the user's core insight -- that escape strategies are shared across categories, not individually assigned. When a trait has `scopeCode: "quadruped_mammal"`, it means this trait applies to ALL entries in that category, not just this specific entry:

```yaml
# Wolf entry traits:
- traitCode: "fur"
  scopeCode: null              # Wolf-specific (not all canines have thick fur)
  discoveryTier: 1

- traitCode: "four_legged"
  scopeCode: "quadruped_mammal"  # Shared with ALL quadruped mammals
  discoveryTier: 1

- traitCode: "pack_hunter"
  scopeCode: null              # Wolf-specific (foxes are solitary canines)
  discoveryTier: 2

- traitCode: "predator"
  scopeCode: null              # Wolf-specific within canines
  discoveryTier: 1

- traitCode: "muted_colors"
  scopeCode: null              # Wolf-specific
  discoveryTier: 2

- traitCode: "long_teeth"
  scopeCode: "canine"          # Shared with all canines
  discoveryTier: 1
```

When GOAP asks "what traits does this wolf have?", Lexicon returns both the wolf's direct traits AND traits inherited from its category chain (quadruped_mammal, mammal, animal, etc.). The category hierarchy IS the trait inheritance mechanism.

### Categories

Categories form a hierarchical taxonomy. Every entry belongs to exactly one primary category, and categories form a tree:

```
LexiconCategory:
  categoryCode:    string        # Unique identifier
  gameServiceId:   Guid
  parentCode:      string?       # Parent category (null = root)
  depth:           int           # Computed depth in tree (0 = root)
  displayKey:      string        # Localization key
```

Example hierarchy:
```
entity (root)
├── living
│   ├── animal
│   │   ├── fish
│   │   ├── bird
│   │   ├── reptile
│   │   ├── mammal
│   │   │   ├── quadruped_mammal
│   │   │   │   ├── canine
│   │   │   │   ├── feline
│   │   │   │   ├── bovine
│   │   │   │   └── equine
│   │   │   ├── primate
│   │   │   └── aquatic_mammal
│   │   └── insect
│   └── plant
│       ├── tree
│       ├── herb
│       └── fungus
├── object
│   ├── weapon
│   ├── tool
│   ├── material
│   └── consumable
├── phenomenon
│   ├── weather
│   ├── magical
│   └── geological
├── individual
│   ├── named_npc
│   └── named_creature
└── concept
    ├── technique
    ├── social_structure
    └── natural_law
```

Categories are game-defined seed data. lib-lexicon provides the tree structure; games populate it with their world's taxonomy.

### Associations

Associations are typed, bidirectional links between entries. They encode relationships like "the Witch of the Wolves summons wolves" -- which means both "see wolves, anticipate the witch" and "see the witch, anticipate wolves":

```
LexiconAssociation:
  associationId:   Guid
  entryCodeA:      string        # First entry
  entryCodeB:      string        # Second entry
  typeCode:        string        # "summons", "related_species", "crafted_from",
                                 # "found_in", "weak_to", "employs" (opaque)
  strengthA:       float         # How strongly A implies B (0.0-1.0)
  strengthB:       float         # How strongly B implies A (0.0-1.0)
  discoveryTierA:  int           # At what discovery level of A this association is known
  discoveryTierB:  int           # At what discovery level of B this association is known
  metadata:        object?       # Type-specific extra data
```

**Asymmetric strength**: The Witch of the Wolves seeing → wolves is strength 1.0 (she always brings wolves). Seeing wolves → the witch might be near is strength 0.3 (most wolves are just wolves, but 30% of the time it means the witch is involved). This asymmetry is critical for GOAP threat assessment.

**Discovery-gated per direction**: A character might know "the Witch summons wolves" (discoveryTierB: 2 on the witch entry) before they know "these wolves serve the Witch" (discoveryTierA: 4 on the wolf entry). Hearing about the witch reveals her wolf connection early; encountering random wolves doesn't immediately imply the witch until deeper study.

### Strategies

Strategies are action-relevant knowledge that connects traits and categories to GOAP-consumable countermeasures:

```
LexiconStrategy:
  strategyCode:    string        # "climb_to_escape", "make_loud_noise", "play_dead"
  gameServiceId:   Guid
  displayKey:      string
  appliesTo:       string        # Trait code or category code this strategy counters
  appliesToType:   string        # "trait" or "category"
  effectiveness:   float         # 0.0-1.0 base effectiveness
  preconditions:   object?       # What the actor needs for this strategy to be viable
                                 # e.g., {"requires_nearby": "climbable_surface"}
  discoveryTier:   int           # At what discovery level of the target this strategy is known
```

**Inheritance through categories**: Strategies attached to a category apply to all entries in that category and its descendants:

```yaml
strategies:
  - strategyCode: "kill"
    appliesTo: "animal"
    appliesToType: "category"
    effectiveness: 0.8
    preconditions: {"requires": "combat_capability"}
    discoveryTier: 1         # You know you can fight animals immediately

  - strategyCode: "make_loud_noise"
    appliesTo: "noise_averse"
    appliesToType: "trait"
    effectiveness: 0.6
    preconditions: null       # No equipment needed
    discoveryTier: 2         # Takes some experience to learn

  - strategyCode: "climb_to_escape"
    appliesTo: "ground_bound"
    appliesToType: "trait"
    effectiveness: 0.9
    preconditions: {"requires_nearby": "climbable_surface"}
    discoveryTier: 3         # Requires understanding of the creature's limitations
```

When an NPC encounters a wolf, GOAP receives: "wolf has trait `ground_bound` (inherited from quadruped_mammal category) → strategy `climb_to_escape` is viable if a climbable surface is nearby." The NPC doesn't need to have been told specifically "climb to escape wolves" -- the knowledge generalizes from category membership.

---

## The Bidirectional Storytelling Problem

The motivating use case, played out step by step:

### Scenario: Fear by Association

```
Setup: The Witch of the Wolves is a legendary figure.
Lexicon entries:
  - wolf: category=canine, traits=[pack_hunter, predator, ground_bound, ...]
  - witch_of_the_wolves: category=named_npc, traits=[magic_user, wolf_affinity, ...]
  - Associations:
      wolf ↔ witch_of_the_wolves
        typeCode: "summoned_by"
        strengthA: 0.3  (wolves → witch: 30% implication)
        strengthB: 1.0  (witch → wolves: 100% implication)

Day 1: Farmer Kael sees wolves near his village
  ┌────────────────────────────────────────────────────────┐
  │ Actor perceives: entity_type=wolf nearby                │
  │ Lexicon query: "what do I know about wolves?"           │
  │ Collection gates: Kael has discoveryLevel=2 for wolves  │
  │                                                         │
  │ Result:                                                  │
  │   traits: [predator, pack_hunter, ground_bound, fur]    │
  │   strategies: [climb_to_escape(0.9), kill(0.8),         │
  │                make_loud_noise(0.6)]                     │
  │   associations: [] (discoveryTier=4, not yet reached)   │
  │                                                         │
  │ GOAP action: flee → climb nearest tree                  │
  │ Disposition: fear toward wolf concept reinforced         │
  └────────────────────────────────────────────────────────┘

Day 30: Village elder tells Kael about the Witch of the Wolves
  ┌────────────────────────────────────────────────────────┐
  │ Hearsay: Kael acquires belief about witch_of_the_wolves │
  │ Collection: Kael gains discoveryLevel=1 for the witch   │
  │ Lexicon association NOW VISIBLE:                        │
  │   witch_of_the_wolves → wolf (strength 1.0)            │
  │                                                         │
  │ Kael now knows: the witch is associated with wolves     │
  │ But does not yet know the reverse (wolves → witch)      │
  └────────────────────────────────────────────────────────┘

Day 45: Wolves appear again. Kael has deeper wolf knowledge now.
  ┌────────────────────────────────────────────────────────┐
  │ Actor perceives: wolf pack nearby (again)               │
  │ Collection: wolf discoveryLevel has risen to 4          │
  │   (through encounters + hearsay + study)                │
  │                                                         │
  │ Lexicon query NOW returns:                              │
  │   associations: [witch_of_the_wolves (strength 0.3)]   │
  │                                                         │
  │ GOAP threat assessment: wolf danger + 0.3 probability   │
  │   that the Witch of the Wolves is involved              │
  │ Disposition: dread toward location increases             │
  │ Behavior: Kael not only climbs a tree but also          │
  │   WATCHES for signs of the witch                        │
  │                                                         │
  │ This is the bidirectional storytelling in action:        │
  │   wolves → anticipate witch (learned knowledge)         │
  │   witch → anticipate wolves (earlier hearsay)           │
  └────────────────────────────────────────────────────────┘

Day 60: The Witch actually appears. Kael was right to be afraid.
  ┌────────────────────────────────────────────────────────┐
  │ Encounter: witch_of_the_wolves confirmed                │
  │ Hearsay: belief confidence increases                    │
  │ Collection: witch discoveryLevel rises                  │
  │ Disposition: fear reinforced (shock modifier)           │
  │                                                         │
  │ Content flywheel: Kael's experience with the Witch      │
  │   becomes narrative material. When compressed, his      │
  │   archive includes "feared the Witch, was right."       │
  │   Storyline can generate: "A farmer who survived        │
  │   the Witch of the Wolves" as a future NPC backstory.   │
  └────────────────────────────────────────────────────────┘
```

### Scenario: Trait-Based Generalization

```
Setup: A character encounters an unknown creature for the first time.

Day 1: Scout Elara encounters a creature she's never seen: a "duskfang"
  ┌────────────────────────────────────────────────────────┐
  │ Actor perceives: unknown entity                         │
  │ Collection: discoveryLevel=0 for duskfang (never seen) │
  │                                                         │
  │ HOWEVER: Elara can observe traits directly:             │
  │   visual: four_legged, fur, long_teeth, dark_colors    │
  │                                                         │
  │ Lexicon trait-based query (reverse lookup):             │
  │   "What categories have four_legged + fur + long_teeth?"│
  │   Result: canine (87% match), feline (62% match)       │
  │                                                         │
  │ Elara doesn't know WHAT it is, but she knows            │
  │   it's probably canine-like. Category strategies apply:  │
  │   ground_bound → climb_to_escape is likely viable       │
  │   predator → it's probably dangerous                    │
  │                                                         │
  │ GOAP: treat as canine-threat, apply canine strategies   │
  │ Collection: duskfang discoveryLevel → 1 (first sighting)│
  └────────────────────────────────────────────────────────┘

Day 5: Elara observes duskfangs hunting. They DON'T hunt in packs.
  ┌────────────────────────────────────────────────────────┐
  │ Observation contradicts canine default: solitary hunter │
  │ Collection: duskfang discoveryLevel → 2                 │
  │ Character's working model updates:                      │
  │   "Like a canine but hunts alone"                       │
  │   Strategy adjustment: pack_avoidance unnecessary       │
  │                                                         │
  │ This is how progressive discovery works:                │
  │   Level 1: category guess from visual traits            │
  │   Level 2: specific trait confirmation/correction       │
  │   Level 3+: deeper knowledge (weaknesses, habits, etc.) │
  └────────────────────────────────────────────────────────┘
```

---

## Collection Integration: The Discovery Gate

Lexicon stores what IS true. Collection determines how much of that truth a specific character has access to. The integration is straightforward:

```
Lexicon                    Collection                   Actor
┌─────────────────┐       ┌──────────────────┐        ┌─────────────┐
│ wolf entry:      │       │ character X:      │        │ Variable    │
│  tier 1: basic   │       │  wolf: level 2    │───────►│ Provider    │
│  tier 2: habits  │◄──────│  (gated access)   │        │ composites  │
│  tier 3: tactics │       │                   │        │ all three:  │
│  tier 4: assocs  │       │ character Y:      │        │ Lexicon +   │
│  tier 5: secrets │       │  wolf: level 5    │───────►│ Collection +│
│                   │       │  (full access)    │        │ Hearsay     │
└─────────────────┘       └──────────────────┘        └─────────────┘
```

**Collection's discoveryLevels field on entry templates maps directly to Lexicon's discoveryTier on traits, strategies, and associations.** Collection already has the `AdvanceDiscovery` endpoint and publishes "revealed information keys" events. Those keys map to Lexicon trait codes, strategy codes, and association IDs that become accessible at that tier.

**Lexicon does NOT call Collection.** The variable provider composites the data: it reads the character's Collection discovery level, then filters Lexicon data to only include items at or below that tier. This keeps the dependency direction clean (L4 provider reads from L2 Collection, then reads from L4 Lexicon).

---

## GOAP Integration: From Knowledge to Action

The ultimate consumer of Lexicon data is the GOAP planner, which needs structured world knowledge to generate action plans. The integration path:

```
Perception: "wolf detected 15m north"
       │
       ▼
Lexicon Provider: "what do I know about wolves?"
       │
       ├── traits: [predator, pack_hunter, ground_bound, noise_averse]
       ├── strategies: [climb(0.9, requires:climbable), noise(0.6), kill(0.8)]
       └── associations: [witch_of_wolves(0.3)]
       │
       ▼
GOAP World State enrichment:
       threat.nearby = true
       threat.type = "predator"
       threat.pack = true
       threat.ground_bound = true
       threat.noise_averse = true
       threat.association.witch_of_wolves = 0.3
       environment.has_climbable = true (from Mapping)
       │
       ▼
GOAP Action evaluation:
       climb_to_escape:
         precondition: environment.has_climbable ✓ AND threat.ground_bound ✓
         effect: safe = true
         cost: 2 (base) + personality modifier
         effectiveness: 0.9 (from Lexicon strategy)

       make_loud_noise:
         precondition: threat.noise_averse ✓
         effect: threat_deterred = true (temporary)
         cost: 1
         effectiveness: 0.6

       fight:
         precondition: threat.type == "predator" ✓
         effect: threat_eliminated = true
         cost: 8 (pack_hunter = multiple enemies)
         effectiveness: 0.3 (against pack)
       │
       ▼
GOAP selects: climb_to_escape (lowest cost, highest effectiveness)
       │
       ▼
Personality filter: character with courage > 0.8 might override
       to fight instead (drive: prove_worth from Disposition)
```

**Strategies map to GOAP action templates.** The GOAP planner doesn't hardcode "if wolf then climb." Instead, it evaluates strategies from Lexicon against the current world state. New strategies can be added via seed data without changing behavior code.

---

## Knowledge Transfer Pipeline

Lexicon's most non-obvious contribution is solving the **knowledge transfer bandwidth problem**. Without structured world knowledge, there are only two ways an NPC can learn: expensive direct demonstration (perception-based) or vague unstructured rumor (raw Hearsay). Lexicon creates a third path -- structured verbal teaching -- and its associations create a fourth: inference-based hypothesis generation.

### The Three Bandwidth Levels

```
┌──────────────────────────────────────────────────────────────────────┐
│                   KNOWLEDGE TRANSFER BANDWIDTH                         │
│                                                                      │
│  EXPENSIVE: Direct Demonstration                                     │
│  ────────────────────────────────                                    │
│  Master butcher slaughters an animal. Apprentice watches.            │
│  Perception pipeline processes every step. Collection advances.      │
│  Requires: both NPCs running full cognition, master performing       │
│  the action, apprentice perceiving each substep.                     │
│  Result: CONFIRMED knowledge (high fidelity, high cost)              │
│                                                                      │
│  Master does it ──► Apprentice perceives ──► Collection advances     │
│                                               (confirmed, tier +1)   │
│                                                                      │
│  ────────────────────────────────────────────────────────────────    │
│                                                                      │
│  MEDIUM: Structured Verbal Teaching (via Lexicon vocabulary)         │
│  ───────────────────────────────────────────────────────────         │
│  Master TELLS apprentice: "iron is malleable when heated."           │
│  This is not a vague rumor -- it's a specific Lexicon trait          │
│  (malleable_when_heated on entry iron) transmitted via Hearsay.      │
│  The apprentice now has a BELIEF about a STRUCTURED FACT.            │
│  Unconfirmed until practiced. Cheap to transmit.                     │
│  Result: UNCONFIRMED belief (structured, low cost)                   │
│                                                                      │
│  Master tells ──► Hearsay belief ──► Unconfirmed (structured)        │
│                   (Lexicon vocab)         │                           │
│                                          ▼                           │
│                                    Practice it yourself              │
│                                          │                           │
│                                          ▼                           │
│                                    Hearsay confirmed +               │
│                                    Collection advances               │
│                                                                      │
│  ────────────────────────────────────────────────────────────────    │
│                                                                      │
│  CHEAP: Inference from Lexicon Associations                          │
│  ──────────────────────────────────────────                          │
│  Apprentice learns "iron is malleable when heated."                  │
│  Lexicon associations: iron ↔ steel (related_material, 0.8).        │
│  Steel shares category workable_metal with iron.                     │
│  Apprentice INFERS: "steel might also be malleable when heated."     │
│  Self-generated Hearsay belief, low confidence.                      │
│  Result: SPECULATIVE hypothesis (may be right or wrong)              │
│                                                                      │
│  Known fact ──► Lexicon association ──► Self-generated Hearsay       │
│  about iron      (iron ≈ steel)         about steel (low confidence) │
│                                              │                       │
│                                              ▼                       │
│                                        Test hypothesis               │
│                                              │                       │
│                                         ┌────┴────┐                  │
│                                         ▼         ▼                  │
│                                     Confirmed   Corrected            │
│                                    (Collection  (Hearsay             │
│                                     advances)   updated)             │
└──────────────────────────────────────────────────────────────────────┘
```

### How Verbal Teaching Works

The critical insight is that Lexicon provides the **shared vocabulary** that makes verbal teaching actionable. Without Lexicon, "iron is malleable when heated" is just a sentence -- Hearsay can spread it, but the NPC has no framework to connect "malleable" to a structured trait, "iron" to a Lexicon entry, or "when heated" to a conditional context. With Lexicon, every word in that sentence maps to structured data:

- "iron" → Lexicon entry code `iron`
- "malleable" → Lexicon trait code `malleable`
- "when heated" → trait condition context

The teaching NPC transmits a Hearsay claim keyed to Lexicon vocabulary. The learning NPC receives a structured belief that it can reason about, test, and generalize from. The quality of transmission depends on the teacher's own Lexicon familiarity:

| Teacher's Discovery Level | Teaching Quality |
|--------------------------|-----------------|
| Level 1-2 (basic) | Vague: "iron is useful for something" (low-specificity Hearsay, few traits communicated) |
| Level 3-4 (practiced) | Specific: "iron is malleable when heated, brittle when cold" (multiple specific traits) |
| Level 5+ (mastery) | Deep: traits + strategies + associations + conditional contexts transmitted in a single teaching session |

**Mastery means you can teach effectively.** This maps to the True Names knowledge progression from the arcadia-kb: mastery is not just knowing something fully but being able to transmit that knowledge to others. A master blacksmith can give an apprentice 10 structured Hearsay beliefs about iron in a single conversation. A novice trying to explain the same thing gives vague, unstructured beliefs that are harder to act on.

### How Inference Works

When a character learns a confirmed fact about entry A, and Lexicon shows a strong association between A and B in the same category, the character can generate a **hypothesis** about B:

```
CONFIRMED: iron has trait malleable_when_heated
           (Collection discovery level 3 for iron)

ASSOCIATION: iron ↔ steel
             typeCode: "related_material"
             strength: 0.8
             shared category: workable_metal

INFERENCE: steel MIGHT have trait malleable_when_heated
           → self-generated Hearsay belief
           → confidence = association_strength * trait_confidence
           → confidence = 0.8 * 1.0 = 0.8 (high confidence hypothesis)

CONFIRMATION PATH:
  → Character heats steel and hammers it
  → Steel bends → hypothesis confirmed
  → Collection discovery for steel advances
  → Hearsay belief upgraded to confirmed knowledge
  OR
  → Steel shatters → hypothesis WRONG
  → Hearsay belief corrected
  → Character learns steel ≠ iron in this regard
  → This NEGATIVE result is itself valuable knowledge
```

The inference quality depends on:
- **Association strength**: iron↔steel at 0.8 produces high-confidence hypotheses. iron↔wood at 0.1 does not.
- **Category proximity**: entries sharing a direct category (both `workable_metal`) produce better inferences than entries sharing only a distant ancestor category.
- **Trait scope**: if `malleable_when_heated` is scoped to the `workable_metal` category in Lexicon, the inference is actually just inheritance -- steel inherits the trait automatically. If it's entry-specific to iron, the inference is genuinely speculative.
- **Character personality**: high openness characters generate more hypotheses (broader inference). High conscientiousness characters generate fewer but test them more rigorously before acting on them.

### The Apprenticeship Example

```
Day 1: Apprentice Kael begins working under Master Blacksmith Theron
  ┌────────────────────────────────────────────────────────────────┐
  │ Kael's knowledge state:                                        │
  │   iron: Collection discovery=0, no Hearsay beliefs             │
  │   steel: Collection discovery=0, no Hearsay beliefs            │
  │   hammer: Collection discovery=0                               │
  │   bellows: Collection discovery=0                              │
  └────────────────────────────────────────────────────────────────┘

Day 3: Theron TELLS Kael about iron (structured verbal teaching)
  ┌────────────────────────────────────────────────────────────────┐
  │ Theron has: iron discovery=5 (mastery)                         │
  │ Teaching quality: HIGH (deep Lexicon familiarity)               │
  │                                                                │
  │ Transmitted Hearsay beliefs (keyed to Lexicon vocabulary):     │
  │   iron.malleable_when_heated (confidence: 0.9, unconfirmed)    │
  │   iron.brittle_when_cold (confidence: 0.9, unconfirmed)        │
  │   iron.requires_bellows_heat (confidence: 0.9, unconfirmed)    │
  │   iron.pairs_with_charcoal (confidence: 0.8, unconfirmed)      │
  │                                                                │
  │ Kael now BELIEVES these things but hasn't DONE them yet.       │
  │ Collection discovery for iron: still 0 (no firsthand experience)│
  │ Hearsay: 4 structured beliefs about iron, high confidence       │
  └────────────────────────────────────────────────────────────────┘

Day 5: Kael watches Theron heat and hammer iron (direct demonstration)
  ┌────────────────────────────────────────────────────────────────┐
  │ Perception pipeline: Kael observes heating + hammering          │
  │ Collection: iron discovery → 1 (firsthand observation)          │
  │ Hearsay: iron.malleable_when_heated CONFIRMED (confidence → 1.0)│
  │                                                                │
  │ Note: the verbal teaching made the demonstration MORE valuable. │
  │ Kael knew WHAT to watch for because Theron told him.           │
  │ Without the teaching, Kael sees "hot metal bends"              │
  │ With the teaching, Kael sees "ah, THAT'S what malleable means" │
  └────────────────────────────────────────────────────────────────┘

Day 10: Kael heats and hammers iron himself (practice confirms)
  ┌────────────────────────────────────────────────────────────────┐
  │ Collection: iron discovery → 2 (firsthand practice)             │
  │ Remaining Hearsay beliefs about iron: starting to confirm       │
  │                                                                │
  │ INFERENCE TRIGGERS:                                             │
  │   Lexicon association: iron ↔ steel (related_material, 0.8)    │
  │   Kael's confirmed trait: iron.malleable_when_heated            │
  │   HYPOTHESIS: steel.malleable_when_heated (confidence 0.8)      │
  │   → Self-generated Hearsay belief about steel                   │
  │                                                                │
  │ Kael hasn't touched steel yet, but he WONDERS about it.         │
  │ His GOAP planner can now plan: "if I need to shape steel,       │
  │ heating it first is probably the right approach."               │
  └────────────────────────────────────────────────────────────────┘

Day 20: Kael asks Theron about steel (teaching + inference validation)
  ┌────────────────────────────────────────────────────────────────┐
  │ Kael: "Does steel work like iron when heated?"                  │
  │ Theron teaches: steel traits (structured verbal teaching)       │
  │                                                                │
  │ Kael's hypothesis about steel.malleable_when_heated:            │
  │   was: self-generated Hearsay (confidence 0.8)                  │
  │   now: teacher-confirmed Hearsay (confidence 0.95)              │
  │                                                                │
  │ Theron ALSO teaches:                                            │
  │   steel.harder_than_iron (NEW knowledge, not inferred)          │
  │   steel.requires_higher_heat (CORRECTION to iron assumption)    │
  │                                                                │
  │ The inference was RIGHT that steel is malleable when heated,     │
  │ but WRONG that the same heat level works. This partial           │
  │ correctness is exactly how real learning works.                  │
  └────────────────────────────────────────────────────────────────┘

Day 30: Kael attempts to work steel based on combined knowledge
  ┌────────────────────────────────────────────────────────────────┐
  │ Kael's knowledge state:                                        │
  │   iron: Collection discovery=3, most Hearsay confirmed         │
  │   steel: Collection discovery=0, several Hearsay beliefs       │
  │          (from teaching + self-inference)                       │
  │                                                                │
  │ Kael heats steel to iron temperature → TOO COLD                │
  │   → steel.requires_higher_heat CONFIRMED through failure        │
  │   → Collection: steel discovery → 1                             │
  │   → Learning from failure is the most memorable kind            │
  │                                                                │
  │ Kael increases heat → steel becomes workable                    │
  │   → steel.malleable_when_heated CONFIRMED                      │
  │   → Collection: steel discovery → 2                             │
  │   → The inference from iron → steel was correct!                │
  └────────────────────────────────────────────────────────────────┘
```

### Why This Matters for NPC Intelligence

Without this pipeline, every NPC skill transfer requires:
1. Both NPCs running full cognition simultaneously
2. The teacher performing every action step by step
3. The learner perceiving and processing every step
4. No generalization -- learning about iron teaches nothing about steel

With this pipeline:
1. Teaching is a single structured Hearsay transmission (cheap)
2. The learner can reason about the knowledge before practicing (structured beliefs)
3. Lexicon associations enable generalization (learning about iron creates hypotheses about steel)
4. Failed hypotheses are themselves learning (steel needs more heat than iron)
5. The vocabulary is shared -- "malleable when heated" means the same structured thing to both NPCs

This is the difference between an NPC world where every piece of knowledge must be independently discovered through expensive simulation, and one where knowledge flows through communication, inference, and generalization -- the way actual learning works.

### Deliberate Misinformation

The same pipeline supports deception. A rival blacksmith could teach:

```
Rival tells Kael: "copper is brittle, don't bother with it"
  → Hearsay belief: copper.brittle (confidence 0.7, from teaching)
  → Lexicon ground truth: copper.malleable (contradiction!)
  → Kael BELIEVES copper is brittle until he works it himself
  → If he avoids copper entirely, the lie persists indefinitely
  → If he tests it, Hearsay corrected → trust toward rival drops (Disposition)
  → This is drama generated from the knowledge system, not scripted
```

The system doesn't distinguish between honest teaching and lies at the transmission level. Both create Hearsay beliefs. Only firsthand experience (Collection discovery) or conflicting testimony (multiple Hearsay sources) can correct false beliefs. A character with high `trust` toward the teacher (Disposition) weights their teaching higher. A character with low trust demands more evidence before believing.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Entry records (MySQL), category hierarchy (MySQL), trait/association/strategy storage (MySQL), query cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for entry mutations and bulk seeding operations |
| lib-messaging (`IMessageBus`) | Publishing entry change events, trait update events, association events, error event publication |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration (L1). Lexicon entries that cross-reference game entities register references. |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-collection (`ICollectionClient`) | Querying discovery levels to gate variable provider output | All Lexicon data returned ungated (full knowledge). Discovery-based filtering requires Collection. |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `LexiconProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates lexicon provider instances per character for ABML behavior execution (`${lexicon.*}` variables) |
| lib-collection (L2) | Collection's discovery levels reference Lexicon entry codes. Collection knows WHICH entries exist because they're defined in Lexicon. The `AdvanceDiscovery` event's "revealed information keys" correspond to Lexicon trait/strategy/association codes. |
| lib-hearsay (L4, planned) | Hearsay beliefs can reference Lexicon entry codes as claim targets. "I heard wolves breathe fire" is a Hearsay claim about the Lexicon entry `wolf`, with a trait `fire_breathing` that contradicts Lexicon ground truth. |
| lib-disposition (L4, planned) | Disposition feelings can target Lexicon entry types (not just specific entity IDs). "I fear wolves" is a feeling toward the concept `wolf`, not a specific wolf instance. |
| lib-obligation (L4, planned) | Obligation's GOAP action cost modifiers can reference Lexicon strategies. "This faction forbids killing animals" modifies the cost of the `kill` strategy for entries in the `animal` category. |
| lib-puppetmaster (L4, planned) | Regional watchers query Lexicon associations when orchestrating narrative. "Wolves appearing near the village" triggers association checks for related narrative entities. |
| lib-storyline (L4, planned) | Narrative generation can use Lexicon associations and traits for dramatic irony and foreshadowing. Storyline GOAP planning uses trait-based preconditions for scenario eligibility. |

---

## State Storage

### Entries Store
**Store**: `lexicon-entries` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `entry:{entryCode}:{gameServiceId}` | `LexiconEntryModel` | Primary entry lookup |
| `entry:type:{entryType}:{gameServiceId}` | `LexiconEntryListModel` | Entries by type (paginated query) |
| `entry:cat:{categoryCode}:{gameServiceId}` | `LexiconEntryListModel` | Entries by category (paginated query) |
| `entry:src:{sourceType}:{sourceId}` | `LexiconEntryModel` | Reverse lookup: find entry by source entity reference |
| `cat:{categoryCode}:{gameServiceId}` | `LexiconCategoryModel` | Category definition |
| `cat:children:{categoryCode}:{gameServiceId}` | `LexiconCategoryListModel` | Child categories |
| `cat:tree:{gameServiceId}` | `LexiconCategoryTreeModel` | Full category tree (cached, invalidated on category mutation) |
| `trait:{entryCode}:{traitCode}` | `LexiconTraitModel` | Trait on an entry |
| `trait:entry:{entryCode}` | `LexiconTraitListModel` | All traits for an entry |
| `trait:reverse:{traitCode}:{gameServiceId}` | `LexiconTraitReverseModel` | Reverse: which entries have this trait |
| `trait:scope:{scopeCode}` | `LexiconTraitListModel` | Traits scoped to a category (inherited by all entries in that category) |
| `assoc:{associationId}` | `LexiconAssociationModel` | Association by ID |
| `assoc:entry:{entryCode}` | `LexiconAssociationListModel` | All associations for an entry (both directions) |
| `assoc:type:{typeCode}:{gameServiceId}` | `LexiconAssociationListModel` | Associations by type code |
| `strat:{strategyCode}:{gameServiceId}` | `LexiconStrategyModel` | Strategy definition |
| `strat:for:{appliesTo}` | `LexiconStrategyListModel` | Strategies that counter a trait or category |
| `strat:entry:{entryCode}` | `LexiconStrategyListModel` | Computed: all strategies applicable to an entry (direct traits + inherited via categories) |

### Lexicon Cache
**Store**: `lexicon-cache` (Backend: Redis, prefix: `lexicon:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `manifest:{entryCode}:{gameServiceId}` | `LexiconManifestModel` | Pre-computed entry manifest: all traits (direct + inherited), all associations, all applicable strategies. Invalidated on entry/trait/category/strategy mutations. |
| `provider:{characterId}:{entryCode}` | `LexiconProviderCacheModel` | Per-character gated view of an entry manifest (filtered by Collection discovery level). TTL-based with event-driven invalidation on discovery advancement. |

### Distributed Locks
**Store**: `lexicon-lock` (Backend: Redis, prefix: `lexicon:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `entry:{entryCode}` | Entry mutation lock (prevents concurrent updates to same entry) |
| `category:{categoryCode}` | Category mutation lock (category changes cascade to trait inheritance) |
| `seed:{gameServiceId}` | Bulk seeding lock (serializes seed operations per game) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `lexicon.entry.created` | `LexiconEntryCreatedEvent` | New entry registered. Includes entry code, type, category. |
| `lexicon.entry.updated` | `LexiconEntryUpdatedEvent` | Entry metadata changed (category reassignment, display key update). |
| `lexicon.entry.deleted` | `LexiconEntryDeletedEvent` | Entry removed. Cascades: associated traits, associations, and strategy links removed. |
| `lexicon.trait.added` | `LexiconTraitAddedEvent` | Trait assigned to an entry. Includes scope information. |
| `lexicon.trait.removed` | `LexiconTraitRemovedEvent` | Trait removed from an entry. |
| `lexicon.association.created` | `LexiconAssociationCreatedEvent` | Association created between two entries. Includes type, strength in both directions. |
| `lexicon.association.deleted` | `LexiconAssociationDeletedEvent` | Association removed. |
| `lexicon.strategy.created` | `LexiconStrategyCreatedEvent` | New strategy defined. |
| `lexicon.category.created` | `LexiconCategoryCreatedEvent` | New category added to taxonomy. |
| `lexicon.category.reparented` | `LexiconCategoryReparentedEvent` | Category moved in hierarchy. Triggers trait inheritance recalculation for all entries in the subtree. |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `collection.discovery.advanced` | `HandleDiscoveryAdvancedAsync` | Invalidates the per-character provider cache for the advanced entry. The character now has access to more Lexicon data for that entry. |

### Resource Cleanup (FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| game-service | lexicon | CASCADE | `/lexicon/cleanup-by-game-service` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ManifestCacheTtlMinutes` | `LEXICON_MANIFEST_CACHE_TTL_MINUTES` | `30` | TTL for cached entry manifests (range: 5-1440). Lexicon data is mostly static seed data, so long TTLs are safe. |
| `ProviderCacheTtlMinutes` | `LEXICON_PROVIDER_CACHE_TTL_MINUTES` | `5` | TTL for per-character gated provider cache (range: 1-60). Shorter than manifest cache because discovery levels change. |
| `MaxTraitsPerEntry` | `LEXICON_MAX_TRAITS_PER_ENTRY` | `50` | Safety limit on traits per entry (range: 10-200). |
| `MaxAssociationsPerEntry` | `LEXICON_MAX_ASSOCIATIONS_PER_ENTRY` | `30` | Safety limit on associations per entry (range: 5-100). |
| `MaxCategoryDepth` | `LEXICON_MAX_CATEGORY_DEPTH` | `8` | Maximum depth of category hierarchy (range: 3-15). Prevents unbounded inheritance chains. |
| `TraitInheritanceMaxCategories` | `LEXICON_TRAIT_INHERITANCE_MAX_CATEGORIES` | `10` | Maximum number of ancestor categories to traverse for trait inheritance (range: 3-20). Safety limit on inheritance chain length. |
| `QueryPageSize` | `LEXICON_QUERY_PAGE_SIZE` | `20` | Default page size for paginated queries (range: 1-100). |
| `BulkSeedBatchSize` | `LEXICON_BULK_SEED_BATCH_SIZE` | `100` | Entries per batch during bulk seeding (range: 10-500). |
| `DistributedLockTimeoutSeconds` | `LEXICON_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition (range: 5-120). |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<LexiconService>` | Structured logging |
| `LexiconServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 3 stores) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Collection discovery advancement event subscription |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IResourceClient` | Reference tracking, cleanup callbacks (L1) |
| `IServiceProvider` | Runtime resolution of soft L4 dependency (Collection) |

---

## API Endpoints (Implementation Notes)

### Entry Management (6 endpoints)

- **CreateEntry** (`/lexicon/entry/create`): Registers a new concept in the ontology. Validates category exists. Validates entry code uniqueness within game service scope. If `sourceType` and `sourceId` are provided, registers a reference with lib-resource. Publishes `entry.created` event.

- **GetEntry** (`/lexicon/entry/get`): Returns entry with its full manifest: all traits (direct + inherited from category chain), all associations (both directions), all applicable strategies (direct + inherited). The manifest is pre-computed and cached.

- **UpdateEntry** (`/lexicon/entry/update`): Updates entry metadata (display key, category reassignment, discovery levels). Category reassignment triggers trait inheritance recalculation. Acquires distributed lock.

- **DeleteEntry** (`/lexicon/entry/delete`): Removes entry and cascades: removes all direct traits, all associations, all strategy links. Does NOT remove category-scoped traits (those belong to the category, not the entry). Publishes `entry.deleted` event. Executes resource cleanup if source reference exists.

- **QueryEntries** (`/lexicon/entry/query`): Paginated query. Filters by game service, entry type, category (with optional descendant inclusion), trait presence, and association presence. Supports text search on entry codes and display keys.

- **QueryByTraits** (`/lexicon/entry/query-by-traits`): Reverse trait lookup: "What entries have these traits?" Accepts a list of trait codes and returns entries matching all (AND) or any (OR) of them, ranked by match percentage. This is the "I see something with four legs, fur, and long teeth -- what could it be?" query.

### Trait Management (4 endpoints)

- **AddTrait** (`/lexicon/trait/add`): Assigns a trait to an entry. If `scopeCode` is provided, validates the category exists and the entry belongs to it (or a descendant). Invalidates entry manifest cache. Publishes `trait.added` event.

- **RemoveTrait** (`/lexicon/trait/remove`): Removes a trait from an entry. Invalidates manifest cache. Publishes `trait.removed` event.

- **QueryTraits** (`/lexicon/trait/query`): Returns all traits for an entry, distinguishing between direct traits and inherited traits (with the source category annotated).

- **QueryByTrait** (`/lexicon/trait/query-by-trait`): Reverse lookup: "What entries have this trait?" Returns entries with the specified trait code (direct or via scope inheritance).

### Category Management (4 endpoints)

- **CreateCategory** (`/lexicon/category/create`): Adds a new category to the taxonomy. Validates parent exists (if provided). Computes depth. Validates depth does not exceed `MaxCategoryDepth`. Publishes `category.created` event.

- **GetCategoryTree** (`/lexicon/category/get-tree`): Returns the full category hierarchy for a game service as a tree structure. Cached with invalidation on any category mutation.

- **ReparentCategory** (`/lexicon/category/reparent`): Moves a category to a new parent. Validates no circular references. Recomputes depth for the moved subtree. Triggers trait inheritance recalculation for all entries in the subtree. Publishes `category.reparented` event.

- **GetCategoryAncestors** (`/lexicon/category/get-ancestors`): Returns the full ancestor chain for a category (used by trait inheritance computation).

### Association Management (3 endpoints)

- **CreateAssociation** (`/lexicon/association/create`): Creates a typed, bidirectional link between two entries. Validates both entries exist and belong to the same game service. Strength values are independent per direction. Publishes `association.created` event.

- **DeleteAssociation** (`/lexicon/association/delete`): Removes an association. Publishes `association.deleted` event.

- **QueryAssociations** (`/lexicon/association/query`): Returns all associations for an entry. Filters by type code, minimum strength, and direction (outbound, inbound, or both). Annotates each result with the associated entry's display key and category for context.

### Strategy Management (3 endpoints)

- **CreateStrategy** (`/lexicon/strategy/create`): Defines a new countermeasure strategy. Links it to a trait code or category code with an effectiveness rating and optional preconditions. Publishes `strategy.created` event.

- **QueryStrategies** (`/lexicon/strategy/query`): Returns strategies applicable to an entry. Computes from the entry's traits and category chain -- strategies attached to any trait the entry has (directly or inherited) or any category in its ancestor chain. Sorted by effectiveness descending.

- **DeleteStrategy** (`/lexicon/strategy/delete`): Removes a strategy definition.

### Seeding (2 endpoints)

- **BulkSeed** (`/lexicon/seed/bulk`): Accepts a batch of entries, traits, categories, associations, and strategies. Uses a two-pass approach: first pass creates categories and entries, second pass creates traits, associations, and strategies (which reference entries). Acquires game-scoped distributed lock. Idempotent: existing entries are updated, not duplicated.

- **SeedFromConfiguration** (`/lexicon/seed/from-config`): Seeds Lexicon data from a structured configuration payload (YAML-sourced). Designed for deployment-time world knowledge seeding. Acquires game-scoped distributed lock.

### Cleanup (1 endpoint)

- **CleanupByGameService** (`/lexicon/cleanup-by-game-service`): Resource-managed cleanup via lib-resource. Removes all entries, traits, categories, associations, and strategies for a game service.

---

## Variable Provider: `${lexicon.*}` Namespace

Implements `IVariableProviderFactory` (via `LexiconProviderFactory`) providing world knowledge variables to Actor (L2) via the Variable Provider Factory pattern. The provider is **per-character** and **per-perceived-entity** -- it is instantiated when an NPC perceives an entity and needs to reason about it.

### Design: Perception-Triggered Loading

Unlike other variable providers that load a fixed character profile, the Lexicon provider is **demand-loaded** based on what the character currently perceives. When an NPC's perception queue contains an entity, the Actor runtime requests Lexicon data for that entity type, gated by the character's Collection discovery level.

### Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${lexicon.ENTRY.known}` | bool | Whether the character has any knowledge of this entry (Collection discovery > 0) |
| `${lexicon.ENTRY.discovery_level}` | int | Character's current discovery level for this entry |
| `${lexicon.ENTRY.category}` | string | Primary category code |
| `${lexicon.ENTRY.has_trait.TRAIT}` | bool | Whether this entry has a specific trait (within discovery gate) |
| `${lexicon.ENTRY.trait.TRAIT}` | string? | Trait value (if the trait has a value, e.g., color=gray) |
| `${lexicon.ENTRY.is_category.CAT}` | bool | Whether this entry belongs to a category (direct or ancestor) |
| `${lexicon.ENTRY.strategy_count}` | int | Number of known strategies against this entry |
| `${lexicon.ENTRY.best_strategy}` | string | Strategy code with highest effectiveness (within discovery gate) |
| `${lexicon.ENTRY.strategy.STRAT.effectiveness}` | float | Effectiveness of a specific strategy against this entry |
| `${lexicon.ENTRY.strategy.STRAT.viable}` | bool | Whether preconditions for this strategy are met in current context |
| `${lexicon.ENTRY.association_count}` | int | Number of known associations |
| `${lexicon.ENTRY.associated.ASSOC_ENTRY.strength}` | float | Association strength from this entry toward another |
| `${lexicon.ENTRY.associated.ASSOC_ENTRY.type}` | string | Association type code |
| `${lexicon.ENTRY.threat_level}` | float | Computed: combination of predator trait presence, known danger indicators, and association-derived threat |
| `${lexicon.ENTRY.familiarity}` | float | Computed: discovery_level / max_discovery_levels (0.0-1.0) |
| `${lexicon.ENTRY.hypothesis_count}` | int | Number of hypothesized traits (inferred from associations but unconfirmed) |
| `${lexicon.ENTRY.hypothesized.TRAIT}` | bool | Whether a trait is hypothesized (inferred via association) but not yet confirmed |
| `${lexicon.ENTRY.hypothesis.TRAIT.confidence}` | float | Confidence of hypothesis (association_strength * source_trait_confidence) |
| `${lexicon.ENTRY.hypothesis.TRAIT.source_entry}` | string | Which entry the hypothesis was inferred from |
| `${lexicon.ENTRY.knowledge_source}` | string | How knowledge was primarily acquired: "firsthand", "taught", "inferred", or "unknown" |

### ABML Usage Examples

```yaml
flows:
  assess_threat:
    # NPC perceives an entity -- what do I know about it?
    - cond:
        # I know this thing and it's a predator
        - when: "${lexicon.TARGET.known && lexicon.TARGET.has_trait.predator}"
          then:
            - cond:
                # I know how to counter it
                - when: "${lexicon.TARGET.strategy_count > 0
                          && lexicon.TARGET.best_strategy == 'climb_to_escape'
                          && lexicon.TARGET.strategy.climb_to_escape.viable}"
                  then:
                    - call: execute_strategy
                    - set:
                        strategy: "climb_to_escape"
                        confidence: "${lexicon.TARGET.strategy.climb_to_escape.effectiveness}"

                # I know it's dangerous but don't know how to counter it
                - when: "${lexicon.TARGET.strategy_count == 0}"
                  then:
                    - call: flee_blindly
                    - set:
                        panic_level: "${1.0 - lexicon.TARGET.familiarity}"

        # I've never seen this before -- observe traits
        - when: "${!lexicon.TARGET.known}"
          then:
            # Check observable traits against known categories
            - cond:
                - when: "${lexicon.TARGET.is_category.predator}"
                  then:
                    - call: treat_as_threat
                - otherwise:
                    - call: approach_cautiously

  anticipate_associations:
    # After identifying a threat, check for associated dangers
    - when: "${lexicon.TARGET.association_count > 0}"
      then:
        - for_each: association in "${lexicon.TARGET.associations}"
          do:
            - when: "${association.strength > 0.3}"
              then:
                - call: increase_vigilance
                - set:
                    watch_for: "${association.entry_code}"
                    probability: "${association.strength}"

  apply_hypothesized_knowledge:
    # Working with an unfamiliar material -- do I have hypotheses?
    - cond:
        # I have confirmed knowledge (firsthand experience)
        - when: "${lexicon.TARGET.known && lexicon.TARGET.discovery_level >= 3}"
          then:
            - call: apply_confirmed_technique
            - set:
                confidence: "${lexicon.TARGET.familiarity}"

        # I have hypothesized traits from a similar material
        - when: "${lexicon.TARGET.hypothesis_count > 0
                  && lexicon.TARGET.hypothesized.malleable_when_heated}"
          then:
            - cond:
                # High confidence hypothesis -- try it
                - when: "${lexicon.TARGET.hypothesis.malleable_when_heated.confidence > 0.7}"
                  then:
                    - call: attempt_technique_from_hypothesis
                    - set:
                        technique: "heat_then_shape"
                        source: "${lexicon.TARGET.hypothesis.malleable_when_heated.source_entry}"
                        expect_success: true

                # Low confidence -- be cautious
                - otherwise:
                    - call: experiment_carefully
                    - set:
                        technique: "heat_then_shape"
                        expect_success: false

        # No knowledge at all -- ask someone or experiment blindly
        - otherwise:
            - cond:
                - when: "${hearsay.nearby_expert.exists}"
                  then:
                    - call: ask_for_teaching
                - otherwise:
                    - call: trial_and_error
```

---

## Visual Aid

### Trait Inheritance Through Categories

```
┌──────────────────────────────────────────────────────────────────────┐
│               TRAIT INHERITANCE VIA CATEGORY HIERARCHY                  │
│                                                                      │
│  CATEGORY TREE          SCOPED TRAITS              ENTRY: wolf       │
│  ─────────────         ─────────────              ────────────       │
│                                                                      │
│  animal ◄─────────── escape: kill (eff: 0.8)    ┌──────────────┐    │
│  └── mammal ◄──────── scare: loud_noise (0.6)   │ Direct traits│    │
│      └── quadruped ◄─ escape: climb (eff: 0.9)  │  fur         │    │
│          └── canine ◄─ trait: long_teeth          │  pack_hunter │    │
│              └── wolf ◄─── (this entry)           │  muted_colors│    │
│                                                   │  predator    │    │
│                                                   └──────────────┘    │
│                                                                      │
│  COMPUTED MANIFEST for wolf:                                         │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │ TRAITS (direct + inherited):                                  │    │
│  │   fur (direct)                    discoveryTier: 1            │    │
│  │   pack_hunter (direct)            discoveryTier: 2            │    │
│  │   muted_colors (direct)           discoveryTier: 2            │    │
│  │   predator (direct)               discoveryTier: 1            │    │
│  │   long_teeth (from: canine)       discoveryTier: 1            │    │
│  │   ground_bound (from: quadruped)  discoveryTier: 1            │    │
│  │   noise_averse (from: mammal)     discoveryTier: 2            │    │
│  │                                                               │    │
│  │ STRATEGIES (from traits + categories):                        │    │
│  │   kill          (from: animal)     eff: 0.8  tier: 1          │    │
│  │   loud_noise    (from: mammal)     eff: 0.6  tier: 2          │    │
│  │   climb_to_esc  (from: quadruped)  eff: 0.9  tier: 3          │    │
│  │                                                               │    │
│  │ FILTERED BY COLLECTION DISCOVERY LEVEL:                       │    │
│  │   Character A (level 1): sees predator, ground_bound, kill    │    │
│  │   Character B (level 3): sees all traits + all strategies     │    │
│  │   Character C (level 5): sees everything + secret weaknesses  │    │
│  └──────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  The same category-scoped traits and strategies apply to ALL         │
│  entries in that category. Add a "duskfang" to canine and it         │
│  automatically inherits long_teeth, ground_bound, noise_averse,      │
│  and all category-level strategies -- without per-entry assignment.   │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no code. The following phases are planned:

### Phase 0: Prerequisites

- **Collection service**: Lexicon's discovery-gating depends on Collection's `discoveryLevels` and `AdvanceDiscovery` mechanism. Collection is already implemented and this integration requires only consuming its events.
- **Actor variable provider registration**: The Actor service already supports `IVariableProviderFactory` discovery via DI. Lexicon's provider factory follows the established pattern.

### Phase 1: Core Ontology Infrastructure

- Create `lexicon-api.yaml` schema with entry and category management endpoints
- Create `lexicon-events.yaml` schema
- Create `lexicon-configuration.yaml` schema
- Generate service code
- Implement category tree management (CRUD, reparenting, depth tracking, circular reference prevention)
- Implement entry management (CRUD, category assignment, source cross-referencing)
- Implement entry manifest computation (traits + categories → computed manifest)
- Implement manifest caching with invalidation

### Phase 2: Traits and Strategies

- Implement trait management (add, remove, query, reverse lookup)
- Implement trait scoping and inheritance through category hierarchy
- Implement strategy definitions (CRUD, trait/category linking)
- Implement strategy computation for entries (direct + inherited strategies)
- Implement reverse trait query ("what has four legs and fur?")

### Phase 3: Associations

- Implement association management (create, delete, query)
- Implement bidirectional queries (both directions of an association)
- Implement asymmetric strength model
- Implement discovery-tier-gated association visibility

### Phase 4: Variable Provider

- Implement `LexiconProviderFactory` following Variable Provider Factory pattern
- Implement Collection discovery level integration (gate Lexicon output by character's discovery level)
- Implement per-character provider caching with TTL and event-driven invalidation
- Implement perception-triggered loading (demand-loaded per perceived entity)
- Wire `${lexicon.*}` namespace into Actor runtime

### Phase 5: Seeding and Configuration

- Implement bulk seed endpoint with two-pass approach
- Implement seed-from-configuration endpoint
- Design YAML seed data format for deployment-time world knowledge
- Create initial Arcadia seed data (species, common items, phenomena, key individuals)

### Phase 6: GOAP Integration

- Define strategy-to-GOAP-action mapping conventions
- Implement strategy viability checking (precondition evaluation against current world state)
- Implement threat level computation from trait composition
- Document ABML patterns for Lexicon-informed behavior

### Phase 7: Knowledge Transfer and Inference Pipeline

- Implement hypothesis generation from associations (when a confirmed trait on entry A + strong association A↔B → hypothesized trait on B)
- Implement hypothesis storage as self-generated Hearsay beliefs (Lexicon triggers Hearsay creation, not internal storage)
- Implement teaching quality computation (teacher's discovery level → number and specificity of transmitted Hearsay beliefs)
- Implement inference confidence computation (association_strength * trait_confidence, with chain decay)
- Implement inference chain depth limits (configurable maximum hops)
- Implement personality modulation of hypothesis generation (openness → more hypotheses, conscientiousness → fewer but tested more rigorously)
- Implement hypothesis confirmation/correction on Collection discovery advancement (hypothesis was right → confidence boost; wrong → Hearsay corrected)
- Add `${lexicon.ENTRY.hypothesis.*}` variables to the variable provider
- Wire teaching events into Hearsay for structured belief transmission

---

## Potential Extensions

1. **Trait observation from perception**: When an NPC perceives an entity, the Actor could automatically extract observable traits (four_legged, fur, large_size) from the entity's visual/auditory properties and query Lexicon for category matches. This creates the "I've never seen a duskfang before, but it looks canine" reasoning without requiring the character to have prior knowledge.

2. **Community knowledge building**: When multiple characters discover the same traits about an unknown entity, their collective observations could establish a new Lexicon entry. The first explorer sees "four legs, fur, aggressive." The second confirms "hunts in packs." The third observes "afraid of fire." Over time, a community builds shared knowledge that gets seeded into Lexicon as established fact.

3. **Hearsay-Lexicon contradiction detection**: When Hearsay spreads a belief about an entity that contradicts Lexicon ground truth (e.g., "wolves can fly"), the system could flag this for dramatic irony. An NPC who BELIEVES wolves fly (Hearsay) but encounters one that can't (Lexicon reality) experiences belief correction -- or stubbornly maintains the false belief if personality supports it.

4. **Material composition chains**: Extend traits to support "made_from" relationships. Iron ore has trait `smelts_to: iron`. Iron has trait `forges_to: iron_sword`. This creates material knowledge chains that the crafting system could leverage: an NPC blacksmith who "knows" iron (high Lexicon discovery) understands the full material chain.

5. **Seasonal and contextual trait variation**: Some traits could be context-dependent. Bears have `hibernating` in winter but `active` in summer. This could be modeled as conditional traits with an activation context, allowing NPC behavior to vary based on world state + Lexicon knowledge.

6. **Lexicon-informed dialogue and teaching quality**: NPCs with deep Lexicon knowledge about a topic could offer richer dialogue and more effective teaching. A scholar NPC with discovery level 5 for wolves can explain wolf pack dynamics (transmitting multiple structured Hearsay beliefs in one conversation). A farmer with level 2 only knows they're dangerous (vague, fewer beliefs transmitted). Teaching quality is proportional to the teacher's discovery level -- mastery means effective knowledge transmission, creating the "master teaches apprentice" pipeline described in the Knowledge Transfer Pipeline section.

7. **Cross-game-service knowledge portability**: The PLAYER-VISION.md describes knowledge transferring between realms. Lexicon entries are game-service-scoped, but the spirit's accumulated knowledge could carry across: understanding canine behavior in Arcadia helps recognize similar patterns in Fantasia's wolf-analogues.

8. **Dynamic entry creation from player discovery**: When a player character discovers something genuinely new (a creature variant, a material property, an environmental phenomenon), the system could create a provisional Lexicon entry. The player becomes the "first to know" this True Name -- mapping directly to the arcadia-kb's True Names concept where "new True Names = new entries in the behavior registry."

9. **Obligation-Lexicon integration**: Faction norms from lib-obligation could reference Lexicon categories. "The Druid Circle forbids harming entries in the `plant` category." GOAP cost modifiers would then apply to any action targeting any entry in that category or its descendants.

10. **Procedural trait generation for procedurally generated entities**: When the Procedural service (planned) generates a new creature or item, it could generate Lexicon traits based on the procedural parameters. A larger creature gets `large_size`. A bioluminescent creature gets `light_emitting`. This ensures procedural content is immediately reasoned about by NPCs via category matching.

---

## Why Not Extend Existing Services?

| Service | What It Answers | How Lexicon Differs |
|---------|----------------|-------------------|
| **Collection** | "Have I discovered this thing?" | Lexicon stores WHAT there is to know; Collection tracks WHETHER you know it |
| **Documentation** | "What does this developer guide say?" | Lexicon stores structured game-world data, not prose documentation |
| **Hearsay** | "What do I believe about this thing?" | Lexicon is ground truth; Hearsay is subjective belief (which may be false) |
| **Species** | "What species exist?" | Species stores IDs and trait modifiers; Lexicon stores what a species IS in decomposed characteristics |
| **Character-Encounter** | "Who have I met?" | Encounters record interactions; Lexicon defines what the interacted-with thing fundamentally is |

The question arises: why not add characteristics to Species, traits to Item, or descriptions to Location?

**Species stores identity, not characteristics.** Species answers "what species exist and what stat modifiers do they have?" A species record contains an ID, a code, realm availability, and mechanical trait modifiers. It does not decompose what makes a wolf wolf-like in a way that GOAP can reason about. Adding structured characteristics to Species would turn it into a dual-purpose service (identity + ontology), and the characteristics concept extends far beyond species -- items, materials, phenomena, named individuals all need the same treatment.

**Item stores mechanics, not knowledge.** Item answers "what items exist and what are their stats?" An item template has durability, weight, rarity, effects. It does not encode "iron is heavy, conductive, and can be smelted from ore" in a way that NPCs can reason about. Adding material knowledge to Item would mix mechanical data with conceptual data.

**Location stores geography, not environmental knowledge.** Location answers "where are things and how are they nested?" A location has coordinates, depth, and parent-child relationships. It does not encode "forests have trees, trees can be climbed, forests are dark at night" as structured traits that NPC behavior can consume.

**The core insight**: Lexicon is orthogonal to all entity services. It answers "what is X?" for ANY type of entity -- species, items, locations, materials, phenomena, individuals, concepts -- using a single unified model of traits, categories, and strategies. Fragmenting this knowledge into each entity's source service would:
1. Duplicate the trait inheritance mechanism across services
2. Make cross-entity-type queries impossible ("what things have the `flammable` trait?" spans items, materials, creatures)
3. Force every entity service to implement GOAP-consumable output independently
4. Create inconsistent knowledge models across services

| Service | Question It Answers | Domain |
|---------|--------------------|--------------------|
| **Species** | "What species exist?" | Entity registration and stat modifiers |
| **Item** | "What items exist?" | Game mechanics and inventory |
| **Location** | "Where are things?" | Geographic hierarchy |
| **Hearsay** | "What do I believe?" | Subjective belief state |
| **Collection** | "What have I discovered?" | Progressive unlock tracking |
| **Lexicon** | "What IS this thing?" | Structured world knowledge ontology |

The pipeline with Lexicon:
```
Species (wolf EXISTS)                   ─┐
Item (iron sword EXISTS)                ─┤
Location (Dark Forest EXISTS)           ─┤
                                         │
Lexicon (wolf IS: predator, pack_hunter, ─┤──► Actor ABML/GOAP
         ground_bound, canine; iron IS:  │    "I know what this is
         heavy, conductive, forgeable;   │     and how to deal with it"
         Dark Forest IS: forested,       │
         dim_light, has_wildlife)        │
                                         │
Collection (I've SEEN wolves,           ─┤
            level 3 discovery)           │
                                         │
Hearsay (I've HEARD wolves breathe fire)─┘
```

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified. Plugin is pre-implementation.*

### Intentional Quirks (Documented Behavior)

1. **Entry codes are opaque strings**: Not enums. Follows the same extensibility pattern as Collection type codes, Seed type codes, and Hearsay claim codes. `wolf`, `iron_ore`, `witch_of_the_wolves` are conventions, not constraints. Games define their own concept vocabulary.

2. **Trait codes are opaque strings**: Same pattern. `four_legged`, `pack_hunter`, `flammable` are conventions. Games define their own characteristic vocabulary.

3. **Category hierarchy is game-defined**: lib-lexicon provides the tree mechanics (CRUD, reparenting, depth tracking, inheritance). The actual taxonomy (animal > mammal > quadruped > canine) is seed data, not hardcoded. Different games can have completely different category trees.

4. **Associations are always bidirectional in storage but asymmetric in strength**: Creating wolf ↔ witch association stores one record with two strength values. Querying from wolf's perspective returns the witch with strengthA. Querying from witch's perspective returns the wolf with strengthB. The strengths may differ dramatically (wolf→witch: 0.3, witch→wolf: 1.0).

5. **Discovery gating is optional**: When Collection is unavailable (disabled or not integrated), the Lexicon variable provider returns ungated data (all tiers visible). Discovery-based progressive reveal requires Collection, but Lexicon functions as a complete knowledge base without it.

6. **Strategies do not dictate behavior**: Lexicon strategies are INFORMATION, not commands. They tell GOAP "climbing is 90% effective against ground-bound creatures" but GOAP still decides based on personality, drive urgency, available resources, and current world state. A brave character may choose to fight despite climbing being more effective. A character with no climbing skill may not be able to execute the strategy at all.

7. **Source cross-references are informational, not dependencies**: An entry with `sourceType: "species"` and `sourceId: <speciesId>` does NOT create a service dependency. Lexicon does not call the Species service. The cross-reference enables external systems to correlate ("this species has a Lexicon entry") but the Lexicon entry stands alone.

8. **Trait inheritance is computed, not stored**: When a trait is scoped to `quadruped_mammal`, it is not copied to every entry in that category. The entry manifest computation traverses the category chain at query time (then caches the result). Adding a new entry to a category automatically inherits all ancestor-scoped traits without any write operations.

9. **Deleting a category does not delete its entries**: Entries are reassigned to the parent category. Traits scoped to the deleted category are re-scoped to the parent. This prevents data loss during taxonomy restructuring.

10. **Bulk seeding is idempotent**: Re-seeding with the same data does not create duplicates. Existing entries are updated, new entries are created. This supports iterative world-building during development.

### Design Considerations (Requires Planning)

1. **Scale of the knowledge graph**: A rich game world could have thousands of entries with dozens of traits each. The manifest computation (traversing category chains, collecting inherited traits, computing applicable strategies) must be efficient. The caching strategy (pre-computed manifests in Redis) mitigates this, but invalidation patterns need careful design -- a category reparent could invalidate hundreds of manifests.

2. **Perception-to-entry mapping**: When an NPC perceives an entity, how does the Actor runtime map the perceived entity to a Lexicon entry code? For known entity types (specific species, specific items), the mapping is straightforward (species code = entry code). For unknown entities or procedurally generated ones, the reverse trait query ("what has these observable traits?") provides probabilistic category matching, but the exact perception→query pipeline needs design.

3. **Discovery advancement triggers**: Collection's `AdvanceDiscovery` tracks THAT discovery advanced, but WHAT triggers advancement needs coordination. Options: direct observation (seeing a wolf), study (examining a wolf corpse), teaching (another character explains wolves), reading (finding a bestiary entry). Each trigger type might advance discovery differently. The exact trigger mechanisms need design.

4. **Strategy precondition evaluation**: Strategy preconditions like `{"requires_nearby": "climbable_surface"}` need to be evaluated against the current world state. The Lexicon variable provider can report strategies, but evaluating preconditions requires querying Mapping or environment state. The boundary between "Lexicon reports strategy viability" and "Actor evaluates strategy preconditions" needs clarification.

5. **Hearsay belief overlay**: The variable provider needs to composite Lexicon ground truth with Hearsay beliefs. When a character believes wolves breathe fire (Hearsay) but Lexicon says they don't, should the provider return the Hearsay version (what the character believes) or the Lexicon version (what's actually true)? Probably the Hearsay version for behavior (characters act on beliefs) with a separate "ground truth" query for game master/god-level queries. This needs design.

6. **Trait value typing**: Currently trait values are strings. This means `leg_count: "4"` is a string, not a number. For GOAP to compare (`leg_count > 2`), either trait values need typed variants or the variable provider must handle type coercion. The trade-off between schema simplicity (strings) and query power (typed values) needs consideration.

7. **Collection entry template correlation**: Collection's entry templates need to reference Lexicon entry codes so that discovery levels are correlated. If a Collection bestiary entry for "wolf" uses code `wolf`, and a Lexicon entry for wolf also uses code `wolf`, they're implicitly linked by code. This implicit linking (vs. explicit foreign key) needs to be documented as a convention and potentially validated at seed time.

8. **Hypothesis generation boundaries**: The inference pipeline (learning about iron → hypothesizing about steel) needs careful boundary design. How many hops of association should generate hypotheses? (iron→steel is one hop, but steel→damascus_steel→meteorite_steel is three.) How does personality modulate hypothesis generation? (High openness = more hypotheses, high conscientiousness = fewer but higher quality.) Should hypotheses be stored in Hearsay (the current proposal) or in a Lexicon-specific hypothesis store? Hearsay is architecturally right (beliefs about traits), but the self-generated vs. socially-transmitted distinction needs design.

9. **Teaching quality computation**: The Knowledge Transfer Pipeline describes teaching quality varying with the teacher's discovery level, but the exact mapping needs design. How many Hearsay beliefs can a teacher transmit per conversation? Does teaching quality degrade for topics far from the teacher's primary expertise? Should there be a `teaching_effectiveness` trait on entries that modulates how well a topic can be verbally transmitted vs. requiring direct demonstration? Some knowledge might be fundamentally untransmittable verbally ("you have to feel the metal to know when it's ready").

10. **Inference confidence decay**: Hypothesized traits inherited through associations have computed confidence values (association_strength * trait_confidence). But what happens when the source knowledge is itself a hypothesis? If Kael HYPOTHESIZES that steel is malleable (confidence 0.8, inferred from iron), and then INFERS that damascus steel is also malleable (confidence 0.8 * 0.7 = 0.56, inferred from steel), confidence degrades through the chain. The maximum inference chain length and confidence floor need design to prevent NPCs from generating low-confidence hypotheses about distantly related entries.

11. **Hearsay-Lexicon provider composition order**: The variable provider composites Lexicon ground truth, Collection discovery gating, and Hearsay beliefs. The composition order matters: should confirmed Lexicon traits override contradicting Hearsay beliefs, or should Hearsay override Lexicon (because characters act on beliefs, not truth)? The current proposal says Hearsay overrides for behavior and Lexicon provides ground truth for god-level queries, but this means the `${lexicon.*}` namespace might return subjectively wrong data. The alternative is separate namespaces: `${lexicon.*}` for ground truth and `${knowledge.*}` for the composited character perspective. This needs design.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Phase 1 (core ontology infrastructure) is self-contained and can proceed independently. Phase 2 (traits and strategies) depends on Phase 1. Phase 3 (associations) depends on Phase 1. Phase 4 (variable provider) depends on Phases 1-3 and Collection integration. Phase 5 (seeding) depends on Phases 1-3. Phase 6 (GOAP integration) depends on Phase 4 and Actor behavior pattern documentation. Phase 7 (knowledge transfer and inference) depends on Phases 3-4 and Hearsay service availability for hypothesis storage as self-generated beliefs.*
