# Character Lifecycle Plugin Deep Dive

> **Plugin**: lib-character-lifecycle (not yet created)
> **Schema**: `schemas/character-lifecycle-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: lifecycle-characters (MySQL), lifecycle-heritage (MySQL), lifecycle-bloodlines (MySQL), lifecycle-cache (Redis), lifecycle-lock (Redis) — all planned
> **Layer**: L4 GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.

## Overview

Generational cycle orchestration and genetic heritage service (L4 GameFeatures) for character aging, marriage, procreation, death processing, and cross-generational trait inheritance. The temporal engine that drives the content flywheel by ensuring characters are born, live, age, reproduce, and die -- and that each death produces archives feeding future content. Game-agnostic: lifecycle stages, genetic trait definitions, marriage customs, and death processing are configured through lifecycle configuration and seed data at deployment time. Internal-only, never internet-facing.

---

## Core Mechanics: Lifecycle Engine

**Two complementary subsystems**:

| Subsystem | What It Answers | Persistence | Update Pattern |
|-----------|----------------|-------------|----------------|
| **Lifecycle** | "Where is this character in their life journey?" | Persistent with temporal advancement | Event-driven (worldstate year/season events) |
| **Heritage** | "What did this character inherit from their parents?" | Persistent, set at creation, immutable | Write-once at procreation |

The pieces are in place -- Worldstate provides the game clock, Character provides CRUD, Relationship has PARENT/SPOUSE/CHILD types, Organization models households with succession, Resource compresses archives, Character-History tracks backstory, Disposition models drives and fulfillment, Storyline generates narratives from archives. But nobody orchestrates the lifecycle itself. Characters don't age. Marriage is a Relationship record without ceremony or household consequences. Procreation doesn't exist -- there is zero mechanism for two characters to produce a child with inherited traits. Death is a deletion event, not a transformation. The content flywheel has all its components but no ignition switch.

### Lifecycle Profile

Every character with lifecycle tracking has a profile:

```
LifecycleProfile:
  characterId:         Guid          # The character being tracked
  gameServiceId:       Guid          # Game service scope
  realmId:             Guid          # Realm for worldstate time queries
  speciesCode:         string        # Species determines stage thresholds and longevity
  birthGameYear:       int           # Game year of birth (from worldstate)
  birthSeason:         string        # Season of birth
  currentAge:          int           # Current age in game years
  currentStage:        string        # Current lifecycle stage code
  causeOfCreation:     string        # "procreation", "seeded", "divine", "spawned"
  parentAId:           Guid?         # Null for seeded/spawned characters
  parentBId:           Guid?
  householdOrgId:      Guid?         # Organization ID of household
  marriageContractId:  Guid?         # Active marriage Contract ID (null if unmarried)
  spouseCharacterId:   Guid?         # Current spouse (from Relationship, cached)
  childCount:          int           # Living children count
  totalChildCount:     int           # All children ever (including deceased)
  fertilityModifier:   float         # Species + age + heritage modifier
  healthModifier:      float         # Age-based health decline
  naturalDeathYear:    int?          # Projected natural death year (null = not yet calculated)
  fulfillmentScore:    float?        # Calculated at death from Disposition drives
  deathGameYear:       int?          # Game year of death (null = alive)
  deathCause:          string?       # "natural", "combat", "disease", "accident", "divine", etc.
  afterlifePath:       string?       # Determined at death processing
  status:              string        # "alive", "dying", "dead", "archived"
```

### Lifecycle Stages

Stages are species-configurable strings (not enums), following the same extensibility pattern as seed type codes, collection type codes, and organization type codes. These are Arcadia conventions, not constraints:

| Stage | Typical Age Range (Human) | Characteristics |
|-------|--------------------------|-----------------|
| `infant` | 0-2 | Fully dependent. Cannot be possessed by guardian spirit. No autonomous behavior beyond basic needs. |
| `child` | 3-14 | Dependent role in household. Limited autonomous behavior. Learning phase -- heritage aptitudes begin to express. Can be possessed but with minimal agency (the tutorial generation from Player Vision). |
| `adolescent` | 15-17 | Transitional. Coming-of-age triggers household role change (Dependent → potential Heir). Heritage traits fully expressed. Personality crystallizes from heritage + childhood experiences. |
| `adult` | 18-59 | Full agency. Can marry, work, own organizations, hold any role. Peak fertility window (species-dependent). Primary gameplay phase. |
| `elder` | 60-79 | Declining health modifier. Reduced fertility. Wisdom bonus (experience-based aptitude boost). Succession planning urgency increases. Can trigger voluntary retirement from household leadership. |
| `dying` | 80+ or triggered | Natural death approaching. Health modifier severely reduced. Final fulfillment calculation begins. Can trigger last-will contract execution. Disposition drives evaluated for closure. |

**Species longevity variation**: Each species defines its own stage thresholds and maximum age range. Elves might have `adult` from 50-500 and `elder` from 500-800. Halflings might compress everything into 60% of human ranges. Stage boundaries are defined in a `LifecycleTemplate` per species, seeded via configuration.

### Lifecycle Template

```
LifecycleTemplate:
  templateCode:        string        # Species code or custom template
  gameServiceId:       Guid
  stages:
    - code:            string        # "infant", "child", "adolescent", "adult", "elder", "dying"
      minAge:          int           # Game years
      maxAge:          int?          # Null = no upper bound (dying stage)
      healthModifier:  float         # Multiplier applied to character health
      fertilityBase:   float         # Base fertility at this stage (0.0-1.0)
      canMarry:        bool          # Whether marriage is permitted
      canProcreate:    bool          # Whether procreation is permitted
      canOwnOrg:      bool          # Whether character can own organizations
      canBePossessed:  bool          # Whether guardian spirit can possess
  naturalDeathRange:
    minAge:            int           # Earliest natural death (e.g., 75)
    maxAge:            int           # Latest natural death (e.g., 95)
    distribution:      string        # "uniform", "normal", "weighted_late"
  fertilityWindow:
    peakStartAge:      int           # When fertility peaks
    peakEndAge:        int           # When peak ends
    declineRate:       float         # How fast fertility drops after peak
```

### Aging

The aging system subscribes to `worldstate.year-changed` events and advances all characters in the affected realm:

```
On worldstate.year-changed (realmId, yearsCrossed):
  1. Query all alive LifecycleProfiles for this realm
  2. For each character:
     a. Advance currentAge by yearsCrossed
     b. Check stage transitions (has age crossed a stage boundary?)
     c. If stage changed:
        - Update healthModifier from template
        - Update fertilityModifier
        - Publish lifecycle.stage-changed event
        - Trigger stage-specific side effects (see below)
     d. Check natural death (has age exceeded naturalDeathYear?)
     e. If natural death:
        - Transition to "dying" status
        - Begin death processing pipeline
  3. Batch processing with configurable batch size
```

**Stage transition side effects**:

| Transition | Side Effects |
|------------|-------------|
| child → adolescent | Heritage traits fully express. Personality initialization from heritage phenotype → Character-Personality. Coming-of-age event published. |
| adolescent → adult | Can now marry, work, own organizations. Organization household role eligible for upgrade (Dependent → active member). `lifecycle.adult-reached` event published. |
| adult → elder | Health modifier begins declining. Wisdom modifier increases. Succession planning urgency. `lifecycle.elder-reached` event consumed by Disposition for drive evaluation (legacy drives intensify). |
| elder → dying | Natural death year reached. Final fulfillment calculation begins. Last-will contract triggered if exists. `lifecycle.dying` event published. |

### Marriage Orchestration

Marriage composes Contract + Relationship + Organization into a coherent ceremony:

```
Marriage Flow:
  1. Validate eligibility:
     - Both characters in "adult" or "elder" stage
     - Neither currently married (active SPOUSE relationship)
     - No prohibited relationship types between them (configurable)
     - Species compatibility check (if cross-species marriage allowed)

  2. Create marriage contract (via IContractClient):
     - Template: configurable per game (e.g., "marriage-standard", "marriage-noble")
     - Parties: character A, character B (and optionally their households)
     - Behavioral clauses: configurable (fidelity, mutual support, etc.)
     - Milestones: anniversary milestones (optional, for contract-based benefits)
     - Breach consequences: configurable (divorce eligibility, reputation impact)

  3. Create SPOUSE relationship (via IRelationshipClient):
     - Bidirectional SPOUSE bond between the two characters

  4. Household resolution (via Organization):
     - Option A: Character B joins Character A's household
     - Option B: Character A joins Character B's household
     - Option C: New household created for the couple
     - Determined by: cultural norms (faction governance), family status,
       household succession rules, or explicit choice
     - Dowry/bride-price: optional Currency/Item transfer between households

  5. Seed Disposition feelings:
     - Warmth modifier between spouses (positive, from ceremony)
     - Trust modifier between spouses
     - Drive formation check: "protect_family" drive may form

  6. Publish lifecycle.marriage event
```

### Procreation

The most complex orchestration flow in the service. Creates a new character entity with inherited traits:

```
Procreation Flow:
  1. Validate eligibility:
     - Both parents in a stage where canProcreate = true
     - Both parents alive
     - Fertility check: combined fertility modifier (age + species + heritage)
       produces a probability. Not every attempt succeeds.
     - Pregnancy duration: species-configurable game-time period

  2. Heritage computation (Heritage Engine):
     a. Load parent A genetic profile
     b. Load parent B genetic profile
     c. For each heritable trait:
        - Select alleles: one from each parent (random per Mendelian rules)
        - Apply dominance rules (dominant, recessive, blending, codominant)
        - Apply mutation chance (configurable per species)
     d. Compute phenotype from genotype (expression rules per species)
     e. Compute bloodline membership (union of parent bloodlines + mutation chance)
     f. Generate aptitude values from phenotype traits

  3. Create character entity (via ICharacterClient):
     - Species: inherited (same as parents, or hybrid rules if cross-species)
     - Realm: same as parents
     - Initial stats: from heritage phenotype
     - Name: configurable (cultural naming rules, family name inheritance)

  4. Create relationships (via IRelationshipClient):
     - PARENT bonds: parentA → child, parentB → child
     - CHILD bonds: child → parentA, child → parentB
     - SIBLING bonds: child ↔ each existing child of parents

  5. Create lifecycle profile:
     - birthGameYear: current game year from worldstate
     - currentStage: "infant"
     - causeOfCreation: "procreation"
     - parentAId, parentBId: set
     - naturalDeathYear: computed from species template + heritage longevity traits

  6. Store genetic profile (Heritage Engine):
     - Genotype, phenotype, bloodlines, mutations
     - Immutable after creation

  7. Add to household (via Organization):
     - Add child as member with "dependent" role
     - Register child as potential heir (if succession mode permits)

  8. Seed Character-History backstory (via ICharacterHistoryClient):
     - Origin: born to [parentA] and [parentB] in [location]
     - Family context: household status, sibling count, family trade

  9. Register with Resource (via IResourceClient):
     - Character reference tracking for cleanup coordination

  10. Publish lifecycle.birth event:
      - Includes characterId, parentIds, speciesCode, realmId, bloodlines
      - Consumed by: Disposition (family drive seeds), Analytics, Storyline
```

### Death Processing

Death is the content flywheel's ignition moment. Every character death produces generative material:

```
Death Processing Pipeline:
  1. Determine cause of death:
     - "natural": age exceeded naturalDeathYear
     - "combat": combat system reports lethal damage
     - "disease": disease/status system reports terminal condition
     - "accident": environmental hazard
     - "divine": god intervention
     - "execution": legal system (faction enforcement)
     - External systems call lifecycle's RecordDeath endpoint with cause

  2. Calculate fulfillment (from Disposition):
     a. Query all drives for this character
     b. For each drive:
        - fulfillmentContribution = drive.satisfaction * drive.intensity
        - frustrationPenalty = drive.frustration * 0.5
        - driveScore = fulfillmentContribution - frustrationPenalty
     c. totalFulfillment = average(driveScores) normalized to 0.0-1.0
     d. If character had no drives (drifter): fulfillment = 0.3 (neutral)

  3. Calculate guardian spirit contribution:
     a. baseLogos = totalFulfillment * speciesLogosMultiplier
     b. bonusLogos = guardian trust modifier (from Disposition guardian feelings)
        - Character who trusted the spirit contributes more
        - Character who resented the spirit contributes less
     c. totalLogos = baseLogos + bonusLogos
     d. Apply to guardian spirit seed (via ISeedClient):
        - RecordGrowth on the account's guardian seed
        - Growth domain: "character_lifecycle"

  4. Trigger archive compression (via IResourceClient):
     - Resource coordinates compression callbacks across all character-* services
     - Character-History: biographical events
     - Character-Personality: final trait values
     - Character-Encounter: significant encounters
     - Disposition: feeling snapshot + drive fulfillment/frustration state
     - Hearsay: high-confidence belief snapshot
     - Character-Lifecycle: heritage profile, lifecycle summary, fulfillment score
     - Compressed archive stored in MySQL via Resource

  5. Process inheritance:
     a. Trigger Organization succession (if character was household head)
     b. Process testament contract if exists (via IContractClient)
     c. Heirloom items: transfer designated items to heir (via IInventoryClient)
     d. Family legacy: if household seed has "dynasty.establish" capability,
        heritage data contributes to household prestige

  6. Determine afterlife pathway:
     - Based on: fulfillment score, cause of death, personality traits, divine favor
     - Pathway codes are opaque strings (e.g., "elysium", "purgatory", "restless")
     - Pathway determines what kind of content the archive generates:
       * High fulfillment → inspiration seeds, heroic legacy, mentor ghosts
       * Low fulfillment → unfinished business seeds, restless spirits, quest hooks
       * Traumatic death → vengeance seeds, haunting, warning tales

  7. Update lifecycle profile:
     - status: "dead"
     - deathGameYear, deathCause, fulfillmentScore, afterlifePath

  8. Publish lifecycle.death event:
     - Includes all death processing results
     - Consumed by: Storyline (archive → narrative seeds), Puppetmaster
       (regional watchers notified), Analytics (population statistics),
       Organization (succession trigger), Disposition (household grief)

  9. Content flywheel activated:
     - Storyline's GOAP planner receives the archive
     - Regional watchers evaluate the death for narrative opportunity
     - The archive becomes a permanent story seed in the world's history
```

### The Fulfillment Principle (Visual)

```
┌──────────────────────────────────────────────────────────────────────┐
│                    THE FULFILLMENT PRINCIPLE                          │
│                                                                      │
│  CHARACTER LIFETIME                                                  │
│  ┌──────────────────────────────────────────────────────┐           │
│  │  Drives form:        Drives pursued:    Drives resolve:│          │
│  │  master_craft (0.3)  practice daily     satisfaction 0.9│          │
│  │  protect_family(0.4) guard household    satisfaction 0.7│          │
│  │  prove_worth  (0.4)  face rival         frustration 0.6│          │
│  └────────────────────────────────────┬─────────────────┘           │
│                                        │                             │
│                                        ▼                             │
│  DEATH PROCESSING                                                    │
│  ┌──────────────────────────────────────────────────────┐           │
│  │  master_craft:  0.9 sat * 0.8 int = 0.72            │           │
│  │  protect_family: 0.7 sat * 0.6 int = 0.42           │           │
│  │  prove_worth:   0.3 sat * 0.5 int - 0.6 frus = -0.15│          │
│  │                                                       │           │
│  │  fulfillment = avg(0.72, 0.42, -0.15) = 0.33         │           │
│  │  normalized to 0.0-1.0 range: 0.55                    │           │
│  └────────────────────────────────────┬─────────────────┘           │
│                                        │                             │
│                    ┌───────────────────┼───────────────────┐        │
│                    ▼                   ▼                   ▼         │
│  GUARDIAN SPIRIT           ARCHIVE TYPE           AFTERLIFE PATH    │
│  ┌─────────────┐    ┌─────────────────┐    ┌─────────────────┐     │
│  │ Logos flow:  │    │ Mixed:          │    │ "reflection"    │     │
│  │ moderate     │    │ - Mastery       │    │ - Achieved dream│     │
│  │ (0.55 * mult)│    │   achieved!     │    │   but left      │     │
│  │             │    │ - Family safe   │    │   unfinished    │     │
│  │ Spirit seed  │    │ - Worth         │    │   business      │     │
│  │ grows by     │    │   unproven      │    │ - Story seeds:  │     │
│  │ proportional │    │                 │    │   "the master   │     │
│  │ amount       │    │ Content seeds:  │    │   who never     │     │
│  └─────────────┘    │ inspiration +   │    │   proved         │     │
│                      │ unfinished      │    │   themselves"   │     │
│                      │ business        │    └─────────────────┘     │
│                      └─────────────────┘                            │
│                                                                      │
│  HIGH FULFILLMENT (>0.8):                                           │
│    Spirit grows significantly. Archive is "inspiration" type.        │
│    Afterlife: peaceful. Content: mentor ghosts, legacy quests.       │
│                                                                      │
│  LOW FULFILLMENT (<0.3):                                            │
│    Spirit grows minimally. Archive is "unfinished_business" type.    │
│    Afterlife: restless. Content: haunting, vengeance quests,         │
│    cautionary tales. "This spirit died with dreams unfulfilled."     │
│                                                                      │
│  PLAYER INCENTIVE:                                                   │
│    Guide characters toward their dreams, not just toward power.      │
│    A fulfilled blacksmith contributes more to the spirit than         │
│    an unfulfilled king.                                              │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Core Mechanics: Heritage Engine

### Genetic Profile

Every character created through procreation has an immutable genetic profile:

```
GeneticProfile:
  characterId:     Guid
  speciesCode:     string             # Primary species
  secondarySpecies: string?           # Non-null for hybrids
  parentAId:       Guid?              # Null for seeded/first-generation
  parentBId:       Guid?
  bloodlines:      BloodlineEntry[]   # Named lineages
  genotype:        GenotypeEntry[]    # Inherited allele pairs
  phenotype:       PhenotypeEntry[]   # Expressed trait values
  aptitudes:       AptitudeEntry[]    # Derived skill aptitudes
  mutations:       MutationEntry[]    # Random mutations applied
  generationDepth: int                # Generations from earliest tracked ancestor
```

### Genotype and Phenotype

The heritage engine uses a simplified genetic model:

**Genotype** (what was inherited):
```
GenotypeEntry:
  traitCode:     string        # "physical_strength", "magical_aptitude", etc.
  alleleA:       float         # Value from parent A (0.0-1.0)
  alleleB:       float         # Value from parent B (0.0-1.0)
  dominance:     string        # How alleles combine to produce phenotype
```

**Phenotype** (what is expressed):
```
PhenotypeEntry:
  traitCode:     string        # Same code as genotype
  value:         float         # The expressed value
  expressionRule: string       # How it was computed
```

**Dominance models**:

| Model | Computation | Example |
|-------|------------|---------|
| `dominant_high` | `max(alleleA, alleleB)` | Physical strength: the stronger allele dominates |
| `dominant_low` | `min(alleleA, alleleB)` | Magical sensitivity: the weaker allele dominates (recessive magic) |
| `blending` | `(alleleA + alleleB) / 2.0` | Temperament: average of both parents |
| `codominant` | Both expressed, stored as range | Hair color: both pigments visible |
| `maternal` | `alleleA` (convention: A = birth parent) | Mitochondrial traits |
| `random` | Random selection of one allele | Eye color: one or the other |

**Mutation**: During recombination, each trait has a configurable mutation chance (default: 2%). A mutation shifts the selected allele value by a random amount within a configurable range. Mutations are recorded for bloodline tracking ("the Blackthorn red hair originated from a mutation in generation 3").

### Heritable Trait Definitions

Which traits are heritable and how they combine is defined per species in a trait template:

```
HeritableTraitTemplate:
  speciesCode:     string
  gameServiceId:   Guid
  traits:
    - traitCode:       string         # "physical_strength"
      displayName:     string         # "Physical Strength"
      category:        string         # "physical", "mental", "magical", "social"
      dominanceModel:  string         # How alleles combine
      mutationChance:  float          # 0.0-1.0 (default 0.02)
      mutationRange:   float          # Max shift on mutation (default 0.1)
      expressionDelay: string?        # Stage at which trait fully expresses
                                      # null = birth, "adolescent" = puberty
      personalityMapping: string?     # Which personality trait this feeds
                                      # e.g., "aggression" → initial aggression bias
      aptitudeMapping:  string?       # Which aptitude domain this feeds
                                      # e.g., "smithing" → smithing aptitude
```

**Expression delay**: Some traits don't fully express until a certain lifecycle stage. A child's magical aptitude might be latent until adolescence. Physical strength doesn't peak until adulthood. The heritage engine stores the full genotype at birth but computes phenotype expression progressively, publishing `lifecycle.trait-expressed` events when latent traits activate.

### Bloodline Tracking

Bloodlines are named genetic lineages that track inheritance across generations:

```
BloodlineEntry:
  bloodlineId:    Guid
  bloodlineCode:  string         # "blackthorn", "ironforge", "moonwhisper"
  originCharId:   Guid           # Character who founded/was first tracked member
  originGameYear: int            # When the bloodline was first identified
  traitSignature: string[]       # Trait codes that characterize this bloodline
  generationFrom: int            # Generations from origin to this character
```

**Bloodline formation**: Bloodlines can form organically. When a family line produces 3+ generations with a distinctive trait pattern (e.g., consistently high `magical_aptitude` + low `physical_strength`), the system identifies this as a bloodline signature. Bloodlines can also be manually established (noble houses declaring their lineage, divine intervention marking a family).

**Bloodline mechanics**: Bloodline membership is queryable by other services:
- Quest prerequisites: "Only a descendant of Ironforge can lift this hammer"
- Divine blessings: "Bless the Moonwhisper bloodline with enhanced magical sensitivity"
- Storyline: "The last living Blackthorn must discover their heritage"
- Actor behavior: `${heritage.has_bloodline.blackthorn}` in ABML expressions

### Species Hybridization

When two characters of different species procreate (if the game permits):

1. Validate cross-species compatibility (configurable compatibility matrix per game)
2. Use the `HybridTraitTemplate` for the species pair (defines which parent's traits dominate for which categories)
3. Child's `speciesCode` is the primary species, `secondarySpecies` is set
4. Hybrid-specific traits may emerge (neither parent has them, but the combination produces them)
5. Fertility modifier for hybrids: configurable (some hybrids are sterile, some are normal, some are enhanced)

Hybridization is optional. Games that don't want cross-species characters simply don't define compatibility entries.

### Aptitude Derivation

Heritage phenotype traits feed into skill aptitudes:

```
aptitude("smithing") =
    phenotype("physical_strength") * 0.4
  + phenotype("hand_eye_coordination") * 0.3
  + phenotype("heat_tolerance") * 0.2
  + phenotype("patience") * 0.1
```

Aptitude mappings are defined in the heritable trait template. The resulting aptitude values are exposed via the `${heritage.aptitude.*}` variable namespace. These represent **innate potential**, not learned skill. A character with high smithing aptitude learns faster and has a higher skill ceiling, but still needs to practice. A character with low aptitude can still become competent through dedication (Disposition's `master_craft` drive overcoming genetic limitation is a compelling character arc).

---

## The Generational Arc

From the [Vision](../../arcadia-kb/VISION.md): "Characters age, marry, have children, and die" and "Death transforms, not ends -- the underworld offers its own gameplay." Every north star depends on lifecycle working:
- **Living Game Worlds**: Generational dynasties, family businesses persisting across lifetimes, NPCs pursuing long-term aspirations that span beyond their own mortality
- **Content Flywheel**: Death produces archives. Archives feed Storyline. Storyline produces scenarios. Scenarios produce new experiences. More characters dying = more content generated. Year 1 yields ~1,000 story seeds; Year 5 yields ~500,000 -- but only if characters actually complete life cycles
- **Design Principle 4** ("Death Creates, Not Destroys"): Fulfillment calculation determines how much logos flows to the guardian spirit. A fulfilled life enriches the player's seed. An unfulfilled life creates unfinished-business story seeds. Death is the mechanism that converts lived experience into generative material

From the [Player Vision](../../arcadia-kb/PLAYER-VISION.md): "more fulfilled in life = more logos flow to the guardian spirit." Fulfillment is computed from Disposition's drive system -- a character who achieved their `master_craft` drive (satisfaction 0.9) contributed more to the guardian spirit than one who died with all drives frustrated. This creates a player incentive to guide characters toward their aspirations, not just toward power or wealth. The guardian spirit seed grows proportionally to the sum of fulfilled drives across all characters in the household's history.

The motivating use case, played out step by step:

```
GENERATION 1: Erik and Marta (seeded characters)
  ┌────────────────────────────────────────────────────────┐
  │ Erik: physical_strength=0.8, magical_aptitude=0.2      │
  │        heritage: seeded (no parents), blacksmith        │
  │ Marta: physical_strength=0.4, magical_aptitude=0.7     │
  │         heritage: seeded, herbalist                     │
  │                                                         │
  │ Marriage: Contract created, SPOUSE bond, household org  │
  │ Household "Erikson" created (Organization, type:household)│
  └────────────────────────────────────────────────────────┘

Year 5: First child born (Kael)
  Heritage computation:
    physical_strength: alleleA=0.8(Erik), alleleB=0.4(Marta)
      dominance: blending → phenotype: 0.6
    magical_aptitude: alleleA=0.2(Erik), alleleB=0.7(Marta)
      dominance: dominant_high → phenotype: 0.7
    mutation: hand_eye_coordination shifted +0.08 (lucky mutation)

  Result: Kael has moderate strength AND high magic -- unusual for
  a blacksmith's son. Aptitude for enchanted smithing emerges.

  Disposition: Kael seeded with latent "master_craft" drive
  (family tradition, reduced intensity 0.15 -- may or may not activate)
  Character-History: backstory seeded "born to Erik the blacksmith
  and Marta the herbalist in the market district"

Year 8: Second child born (Lena)
  Heritage computation:
    physical_strength: alleleA=0.8(Erik), alleleB=0.4(Marta)
      dominance: blending → phenotype: 0.6
    magical_aptitude: alleleA=0.2(Erik), alleleB=0.7(Marta)
      dominance: dominant_high → phenotype: 0.2 (drew Erik's allele)
    No mutations.

  Result: Lena has moderate strength, low magic. Traditional blacksmith
  profile. Sibling has very different genetic expression from same parents.

Year 15: Kael reaches adolescence
  - Heritage traits fully express (magical_aptitude was delayed)
  - Character-Personality initialized: aggression baseline from heritage,
    modified by childhood encounters
  - Coming-of-age event → Organization role change (dependent → active)
  - Kael's latent master_craft drive activates (conscientiousness 0.7
    from heritage + domain engagement threshold met)

Year 18: Kael reaches adulthood
  - Can now marry, work, own organizations
  - Aptitude for enchanted smithing visible to other services
  - Quest prerequisites check: "heritage.aptitude.enchanting > 0.5" → true

Year 25: Kael marries Sera (from another family)
  - Marriage contract, SPOUSE bond, household decision
  - Sera joins Erikson household (Organization member add)
  - Bloodline tracking: Sera's "Moonwhisper" bloodline enters the family

Year 28: Kael and Sera have a child (Thane)
  Heritage: combines Erikson strength/magic with Moonwhisper magic/grace
  Bloodlines: [Erikson, Moonwhisper]
  Aptitudes: very high enchanting (both parents have magical aptitude)
  → "The Erikson-Moonwhisper line of enchanter-smiths begins"

Year 65: Erik dies of natural causes
  Fulfillment: master_craft satisfied (0.85), protect_family satisfied (0.7)
  → fulfillment score: 0.72 (high)
  → Guardian spirit receives significant logos
  → Archive: "Erik the master blacksmith, founder of the Erikson line,
     died fulfilled having passed his craft to his son"
  → Storyline: inspiration seed (mentor ghost, legacy quest hook)
  → Organization: succession triggers, Kael becomes household head
  → Content flywheel: Erik's archive enriches the world's history

GENERATION 3: Thane grows up carrying both bloodlines
  - Heritage: high enchanting aptitude from genetic convergence
  - Disposition: latent drive for "transcend_family_legacy" (identity category)
  - The Erikson enchanted weapons become a known commodity
  - Storyline can generate: "The Erikson-Moonwhisper dynasty has produced
    three generations of enchanter-smiths. Their workshop is legendary."
  - Content from generation 1's archives feeds generation 3's world state
```

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Lifecycle profiles (MySQL), genetic profiles (MySQL), bloodline records (MySQL), lifecycle cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for lifecycle mutations, procreation operations, death processing |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events (birth, death, marriage, stage change), error event publication |
| lib-messaging (`IEventConsumer`) | Subscribing to worldstate boundary events (aging), character deletion, combat death |
| lib-character (`ICharacterClient`) | Creating child characters during procreation, validating character existence, querying character realm/species (L2) |
| lib-relationship (`IRelationshipClient`) | Creating PARENT/CHILD/SIBLING/SPOUSE bonds during procreation and marriage (L2) |
| lib-species (`ISpeciesClient`) | Querying species data for lifecycle template resolution, longevity ranges, trait definitions (L2) |
| lib-worldstate (`IWorldstateClient`) | Querying current game time for birth year recording, subscribing to year-changed for aging (L2) |
| lib-contract (`IContractClient`) | Creating marriage contracts, testament/will contracts, processing contract-backed death rituals (L1) |
| lib-resource (`IResourceClient`) | Triggering archive compression on death, reference tracking, cleanup callback registration (L1) |
| lib-seed (`ISeedClient`) | Recording guardian spirit growth on character death (fulfillment → logos), querying household seed (L2) |
| lib-game-service (`IGameServiceClient`) | Game service scope validation for templates (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-organization (`IOrganizationClient`) | Household management: adding children as members, triggering succession on death, creating households on marriage | No household management. Characters exist without organizational context. Marriage creates only Relationship bonds, not household changes. |
| lib-disposition (`IDispositionClient`) | Querying drives for fulfillment calculation, seeding drives in newborns from family tradition, querying guardian feelings for spirit contribution | Fulfillment defaults to 0.5 (neutral). No drive inheritance. Spirit contribution uses flat rate. |
| lib-character-personality (`ICharacterPersonalityClient`) | Seeding initial personality traits from heritage phenotype during procreation | Personality starts at species defaults, not heritage-derived values. |
| lib-character-history (`ICharacterHistoryClient`) | Seeding backstory elements for newborn characters (family context, birth circumstances) | No backstory seeded at birth. Characters start with blank history. |
| lib-character-encounter (`ICharacterEncounterClient`) | Creating "met at birth" encounter records between newborn and family members | No birth encounters recorded. |
| lib-hearsay (`IHearsayClient`) | Seeding initial beliefs in newborn from family cultural context (what the family "knows") | No inherited beliefs. Characters start with no hearsay. |
| lib-inventory (`IInventoryClient`) | Heirloom transfer during inheritance processing | No heirloom transfer on death. Items remain in deceased's inventory for manual handling. |
| lib-currency (`ICurrencyClient`) | Inheritance: wallet transfer or division during death processing | No currency inheritance. Wallets remain with deceased character's organization. |
| lib-analytics (`IAnalyticsClient`) | Population statistics: birth rates, death rates, average lifespan, bloodline distribution | No population analytics. |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `HeritageProviderFactory` and `LifecycleProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates provider instances per character for ABML behavior execution (`${heritage.*}` and `${lifecycle.*}` variables) |
| lib-organization (L4) | Organization Phase 5 awaits lifecycle events: `lifecycle.marriage` triggers household creation, `lifecycle.birth` triggers member addition, `lifecycle.death` triggers succession. Organization subscribes to these events for household management. |
| lib-disposition (L4, planned) | Disposition Extension 2 (drive inheritance) can consume `lifecycle.birth` events to seed latent drives in newborn characters based on parent drive history. `lifecycle.stage-changed` events trigger drive evaluation checks (elder stage → legacy drives intensify). |
| lib-storyline (L4) | Storyline consumes death archives for narrative generation. Birth events create story hooks ("a child born under unusual circumstances"). Marriage events create alliance/rivalry narratives. Bloodline data enriches scenario preconditions. |
| lib-puppetmaster (L4) | Regional watchers monitor lifecycle events for narrative opportunities: significant deaths, unusual births (powerful heritage convergence), marriage alliances between rival families. |
| lib-gardener (L4) | Player experience orchestration uses lifecycle to surface "your character is aging," "your heir is ready," "succession is approaching" as player-facing progression indicators. The household-as-garden context depends on lifecycle events for state transitions. |
| lib-quest (L2) | Quest prerequisites can check `${heritage.*}` and `${lifecycle.*}` variables. "Only an adult of the Ironforge bloodline can accept this quest." |
| lib-divine (L4, planned) | Gods can target blessings/curses at bloodlines. Birth events in blessed bloodlines trigger divine attention. Fulfillment scores at death affect divine favor calculations. |

---

## State Storage

### Lifecycle Profile Store
**Store**: `lifecycle-characters` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `profile:{characterId}` | `LifecycleProfileModel` | Primary lookup. Full lifecycle state for a character. |
| `profile:realm:{realmId}` | `LifecycleProfileModel` | Realm-scoped query for batch aging processing |
| `profile:realm:{realmId}:stage:{stageCode}` | `LifecycleProfileModel` | Stage-filtered query within realm (e.g., all adults for marriage eligibility) |
| `profile:household:{orgId}` | `LifecycleProfileModel` | All characters in a household (for succession, family queries) |
| `profile:parent:{characterId}` | `LifecycleProfileModel` | Reverse lookup: all children of a character |

### Heritage Store
**Store**: `lifecycle-heritage` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `genetic:{characterId}` | `GeneticProfileModel` | Full genetic profile for a character. Immutable after creation. |
| `genetic:parents:{parentAId}:{parentBId}` | `GeneticProfileModel` | Lookup children by parent pair |
| `trait-template:{speciesCode}:{gameServiceId}` | `HeritableTraitTemplateModel` | Species-specific heritable trait definitions |
| `lifecycle-template:{speciesCode}:{gameServiceId}` | `LifecycleTemplateModel` | Species-specific lifecycle stage definitions |
| `hybrid-template:{speciesA}:{speciesB}:{gameServiceId}` | `HybridTraitTemplateModel` | Cross-species hybridization rules |

### Bloodline Store
**Store**: `lifecycle-bloodlines` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bloodline:{bloodlineId}` | `BloodlineModel` | Bloodline definition: code, origin, trait signature, member count |
| `bloodline:code:{gameServiceId}:{bloodlineCode}` | `BloodlineModel` | Code-based lookup within game scope |
| `bloodline:member:{characterId}` | `BloodlineMembershipModel` | All bloodlines a character belongs to |
| `bloodline:members:{bloodlineId}` | `BloodlineMemberListModel` | All living members of a bloodline (for targeting, queries) |

### Lifecycle Cache
**Store**: `lifecycle-cache` (Backend: Redis, prefix: `lifecycle:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `manifest:{characterId}` | `LifecycleManifestModel` | Cached composite manifest per character: lifecycle stage, age, heritage phenotype, aptitudes, bloodlines. Pre-computed for fast variable provider reads. TTL-based with event-driven invalidation. |
| `realm-pop:{realmId}` | `RealmPopulationModel` | Cached population statistics per realm: total alive, births this year, deaths this year, age distribution. Updated by aging worker. |

### Distributed Locks
**Store**: `lifecycle-lock` (Backend: Redis, prefix: `lifecycle:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `aging:{realmId}` | Aging batch lock (prevents concurrent aging for same realm across nodes) |
| `procreation:{parentAId}:{parentBId}` | Procreation lock (prevents concurrent births from same parents) |
| `death:{characterId}` | Death processing lock (prevents concurrent death processing) |
| `marriage:{characterAId}:{characterBId}` | Marriage lock (prevents concurrent marriage processing) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `lifecycle.birth` | `LifecycleBirthEvent` | Character born through procreation. Includes characterId, parentIds, speciesCode, realmId, bloodlineIds, heritagePheno summary. |
| `lifecycle.stage-changed` | `LifecycleStageChangedEvent` | Character transitioned lifecycle stage. Includes characterId, previousStage, newStage, age, realmId. |
| `lifecycle.adult-reached` | `LifecycleAdultReachedEvent` | Character reached adult stage. Includes characterId, realmId. (Convenience event for services that only care about adulthood.) |
| `lifecycle.marriage` | `LifecycleMarriageEvent` | Characters married. Includes characterAId, characterBId, contractId, householdOrgId, realmId. |
| `lifecycle.divorce` | `LifecycleDivorceEvent` | Marriage ended. Includes characterAId, characterBId, contractId, cause. |
| `lifecycle.dying` | `LifecycleDyingEvent` | Character entered dying state. Includes characterId, projectedDeathYear, cause. |
| `lifecycle.death` | `LifecycleDeathEvent` | Character died. Includes characterId, deathCause, fulfillmentScore, afterlifePath, realmId, guardianSpiritContribution, archiveId. |
| `lifecycle.trait-expressed` | `LifecycleTraitExpressedEvent` | Latent heritage trait activated (e.g., at adolescence). Includes characterId, traitCode, phenotypeValue. |
| `lifecycle.bloodline-formed` | `LifecycleBloodlineFormedEvent` | New bloodline identified from consistent trait patterns across generations. Includes bloodlineCode, originCharacterId, traitSignature. |
| `lifecycle.inheritance-processed` | `LifecycleInheritanceProcessedEvent` | Inheritance distributed after death. Includes deceasedId, heirId, assetsTransferred summary. |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `worldstate.year-changed` | `HandleYearChangedAsync` | Batch-advance character ages for the affected realm. Detect stage transitions, natural deaths. Publish stage-changed and dying events. Primary aging driver. |
| `worldstate.season-changed` | `HandleSeasonChangedAsync` | Update seasonal fertility modifiers. Seasonal birth rate adjustments (if configured). |
| `character.deleted` | `HandleCharacterDeletedAsync` | Clean up lifecycle profile, genetic profile, and bloodline memberships for the deleted character. Update parent records (decrement child counts). |
| `contract.terminated` | `HandleContractTerminatedAsync` | If a marriage contract is terminated: process as divorce. Update lifecycle profiles for both spouses. Publish `lifecycle.divorce`. |
| `contract.breached` | `HandleContractBreachedAsync` | If a marriage contract is breached: evaluate breach severity. May trigger divorce proceedings or reconciliation period (configurable). |
| `seed.phase.changed` | `HandleSeedPhaseChangedAsync` | If a household seed gains `dynasty.establish` capability: evaluate whether the family qualifies for bloodline formation. |

### Resource Cleanup (FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| character | lifecycle | CASCADE | `/character-lifecycle/cleanup-by-character` |
| realm | lifecycle | CASCADE | `/character-lifecycle/cleanup-by-realm` |

### Compression Callback (via x-compression-callback)

| Resource Type | Source Type | Priority | Compress Endpoint | Decompress Endpoint |
|--------------|-------------|----------|-------------------|---------------------|
| character | lifecycle | 50 | `/character-lifecycle/get-compress-data` | `/character-lifecycle/restore-from-archive` |

Archives lifecycle summary (stages traversed, marriages, children, fulfillment) and heritage profile (genetic data, bloodlines, aptitudes) for character compression. Priority 50 ensures lifecycle data is archived after personality/history/encounters (which it references) but before final Resource compression.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `AgingBatchSize` | `CHARACTER_LIFECYCLE_AGING_BATCH_SIZE` | `200` | Characters per batch in aging worker (range: 50-1000) |
| `AgingStartupDelaySeconds` | `CHARACTER_LIFECYCLE_AGING_STARTUP_DELAY_SECONDS` | `30` | Delay before aging responds to first year-changed event, allowing worldstate to stabilize (range: 0-120) |
| `DefaultMutationChance` | `CHARACTER_LIFECYCLE_DEFAULT_MUTATION_CHANCE` | `0.02` | Default probability of mutation per trait during procreation (range: 0.0-0.2) |
| `DefaultMutationRange` | `CHARACTER_LIFECYCLE_DEFAULT_MUTATION_RANGE` | `0.1` | Maximum allele shift on mutation (range: 0.01-0.3) |
| `BloodlineFormationGenerations` | `CHARACTER_LIFECYCLE_BLOODLINE_FORMATION_GENERATIONS` | `3` | Consecutive generations with signature traits required to form a bloodline (range: 2-5) |
| `BloodlineFormationTraitThreshold` | `CHARACTER_LIFECYCLE_BLOODLINE_FORMATION_TRAIT_THRESHOLD` | `0.7` | Minimum trait value consistency across generations to qualify as signature (range: 0.5-0.95) |
| `FulfillmentNeutralDefault` | `CHARACTER_LIFECYCLE_FULFILLMENT_NEUTRAL_DEFAULT` | `0.3` | Fulfillment score for characters with no drives (drifters) (range: 0.0-0.5) |
| `FulfillmentFrustrationWeight` | `CHARACTER_LIFECYCLE_FULFILLMENT_FRUSTRATION_WEIGHT` | `0.5` | How much drive frustration reduces fulfillment contribution (range: 0.0-1.0) |
| `GuardianSpiritBaseMultiplier` | `CHARACTER_LIFECYCLE_GUARDIAN_SPIRIT_BASE_MULTIPLIER` | `1.0` | Base multiplier for fulfillment → guardian spirit logos (range: 0.1-10.0) |
| `GuardianTrustBonusWeight` | `CHARACTER_LIFECYCLE_GUARDIAN_TRUST_BONUS_WEIGHT` | `0.3` | How much guardian trust modifies spirit contribution (range: 0.0-1.0) |
| `MarriageContractTemplateCode` | `CHARACTER_LIFECYCLE_MARRIAGE_CONTRACT_TEMPLATE_CODE` | `marriage-standard` | Default contract template for marriage ceremonies |
| `TestamentContractTemplateCode` | `CHARACTER_LIFECYCLE_TESTAMENT_CONTRACT_TEMPLATE_CODE` | `testament-standard` | Default contract template for last-will testaments |
| `PregnancyDurationGameDays` | `CHARACTER_LIFECYCLE_PREGNANCY_DURATION_GAME_DAYS` | `270` | Game-days between procreation and birth (range: 1-730) |
| `MaxChildrenPerPair` | `CHARACTER_LIFECYCLE_MAX_CHILDREN_PER_PAIR` | `12` | Maximum children from a single parent pair (range: 1-20) |
| `MaxChildrenPerCharacter` | `CHARACTER_LIFECYCLE_MAX_CHILDREN_PER_CHARACTER` | `20` | Maximum total children for a single character across all partners (range: 1-50) |
| `InheritanceHeirloomSlots` | `CHARACTER_LIFECYCLE_INHERITANCE_HEIRLOOM_SLOTS` | `3` | Maximum designated heirloom items transferable on death (range: 0-10) |
| `DeathProcessingTimeoutSeconds` | `CHARACTER_LIFECYCLE_DEATH_PROCESSING_TIMEOUT_SECONDS` | `60` | Timeout for the full death processing pipeline (range: 10-300) |
| `NaturalDeathCheckBatchSize` | `CHARACTER_LIFECYCLE_NATURAL_DEATH_CHECK_BATCH_SIZE` | `100` | Characters per batch in natural death checking (range: 10-500) |
| `CacheTtlMinutes` | `CHARACTER_LIFECYCLE_CACHE_TTL_MINUTES` | `5` | TTL for cached lifecycle manifests per character (range: 1-60) |
| `DistributedLockTimeoutSeconds` | `CHARACTER_LIFECYCLE_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition (range: 5-120) |
| `QueryPageSize` | `CHARACTER_LIFECYCLE_QUERY_PAGE_SIZE` | `20` | Default page size for paged queries (range: 1-100) |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<CharacterLifecycleService>` | Structured logging |
| `CharacterLifecycleServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 5 stores) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Worldstate, character, contract, seed event subscriptions |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `ICharacterClient` | Character creation and queries (L2) |
| `IRelationshipClient` | Family bond creation (L2) |
| `ISpeciesClient` | Species data for template resolution (L2) |
| `IWorldstateClient` | Game time queries for birth year, aging (L2) |
| `IContractClient` | Marriage, testament, inheritance contracts (L1) |
| `IResourceClient` | Archive compression trigger, reference tracking (L1) |
| `ISeedClient` | Guardian spirit growth recording (L2) |
| `IGameServiceClient` | Game service scope validation (L2) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

### Background Workers

| Worker | Trigger | Lock Key | Purpose |
|--------|---------|----------|---------|
| `LifecycleAgingWorkerService` | `worldstate.year-changed` event | `lifecycle:lock:aging:{realmId}` | Batch-advances character ages for a realm when the game year changes. Detects stage transitions and natural deaths. Not interval-based -- triggered by worldstate events. |
| `LifecyclePregnancyWorkerService` | `worldstate.day-changed` event | `lifecycle:lock:pregnancy-worker` | Checks pending pregnancies against current game time. When pregnancy duration is reached, executes the birth flow (heritage computation, character creation, relationship bonds, household addition). |
| `LifecycleBloodlineWorkerService` | `lifecycle.birth` event (batched) | `lifecycle:lock:bloodline-worker` | On births, evaluates whether the newborn's lineage qualifies for bloodline formation (3+ generations with consistent trait signature). Batched to avoid per-birth evaluation overhead. |

### Variable Provider Factories

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `HeritageProviderFactory` | `${heritage.*}` | Reads genetic profile from MySQL (via lifecycle cache). Provides phenotype traits, aptitudes, bloodline membership, species, generation depth. | `IVariableProviderFactory` (DI singleton) |
| `LifecycleProviderFactory` | `${lifecycle.*}` | Reads lifecycle profile from MySQL (via lifecycle cache). Provides age, stage, fertility, health modifier, marriage status, child count, fulfillment. | `IVariableProviderFactory` (DI singleton) |

---

## API Endpoints (Implementation Notes)

### Lifecycle Management (8 endpoints)

All endpoints require `developer` role.

- **InitiateMarriage** (`/character-lifecycle/marriage/initiate`): Validates eligibility (adult/elder stage, not already married, no prohibited relationship). Creates marriage contract via `IContractClient`. Creates SPOUSE relationship via `IRelationshipClient`. Resolves household (configurable: join existing, merge, or create new via Organization). Seeds Disposition feelings. Publishes `lifecycle.marriage`.

- **InitiateProcreation** (`/character-lifecycle/procreation/initiate`): Validates eligibility (fertile stage, fertility check). Creates a pending pregnancy record with expected birth date (current game time + `PregnancyDurationGameDays`). The pregnancy worker handles actual birth when the date is reached. Returns pregnancy ID and expected birth date.

- **RecordDeath** (`/character-lifecycle/death/record`): External death reporting. Accepts characterId and cause of death. Acquires death processing lock. Executes the full death processing pipeline (fulfillment calculation, guardian spirit contribution, archive trigger, inheritance, afterlife path). Publishes `lifecycle.death`. Returns fulfillment score and afterlife path.

- **GetLifecycleProfile** (`/character-lifecycle/profile/get`): Returns full lifecycle profile for a character: age, stage, heritage summary, marriage status, children, fulfillment (if dead).

- **QueryByStage** (`/character-lifecycle/profile/query-by-stage`): Paged query for characters in a specific lifecycle stage within a realm. Used by behavior systems to find eligible marriage partners, by storyline to find elders nearing death, etc.

- **QueryByBloodline** (`/character-lifecycle/profile/query-by-bloodline`): Returns all living characters with a specific bloodline membership. Used by Quest prerequisites, Divine targeting, Storyline scenario matching.

- **SetNaturalDeathYear** (`/character-lifecycle/profile/set-death-year`): Administrative override for a character's projected natural death year. Used for divine intervention (extending/shortening lifespan) or testing. Publishes no event (silent adjustment).

- **SeedLifecycleProfile** (`/character-lifecycle/profile/seed`): Bulk creation of lifecycle profiles for seeded (first-generation) characters. Accepts character IDs with birth years, stages, and optional heritage data. Used during world initialization.

### Heritage Management (6 endpoints)

- **GetGeneticProfile** (`/character-lifecycle/heritage/get-genetic-profile`): Returns full genetic profile for a character: genotype, phenotype, aptitudes, bloodlines, mutations, parent references.

- **GetPhenotype** (`/character-lifecycle/heritage/get-phenotype`): Returns only the expressed phenotype traits and aptitudes for a character. Lightweight version of GetGeneticProfile for consumers who don't need full genotype data.

- **QueryByAptitude** (`/character-lifecycle/heritage/query-by-aptitude`): Find characters with a specific aptitude above a threshold. Used by Storyline ("find characters with natural enchanting talent") and Quest.

- **SeedGeneticProfile** (`/character-lifecycle/heritage/seed-genetic-profile`): Creates a genetic profile for a seeded (first-generation) character. Accepts explicit phenotype values or generates from species defaults with random variation. Used during world initialization.

- **SimulateOffspring** (`/character-lifecycle/heritage/simulate-offspring`): Given two parent character IDs, simulates what a child's genetic profile WOULD look like without actually creating one. Returns probable phenotype ranges with variation. Used by behavior systems (NPC evaluating a potential partner's genetic compatibility) and by testing.

- **GetFamilyTree** (`/character-lifecycle/heritage/get-family-tree`): Returns a multi-generational family tree rooted at a character: parents, grandparents, siblings, children, grandchildren. Includes heritage trait summaries per generation for bloodline visualization. Depth configurable (default: 3 generations up, 2 down).

### Template Management (6 endpoints)

All require `developer` role.

- **SeedLifecycleTemplate** (`/character-lifecycle/template/seed-lifecycle`): Creates lifecycle stage definitions for a species. Validates stage boundaries are contiguous (no age gaps or overlaps).

- **SeedHeritableTraitTemplate** (`/character-lifecycle/template/seed-heritable-traits`): Creates heritable trait definitions for a species. Defines which traits exist, their dominance models, mutation parameters, expression delays, and personality/aptitude mappings.

- **SeedHybridTemplate** (`/character-lifecycle/template/seed-hybrid`): Creates cross-species hybridization rules for a species pair. Defines trait dominance overrides, fertility modifiers, and hybrid-specific traits.

- **GetLifecycleTemplate** (`/character-lifecycle/template/get-lifecycle`): Returns lifecycle template for a species.

- **GetHeritableTraitTemplate** (`/character-lifecycle/template/get-heritable-traits`): Returns heritable trait template for a species.

- **ListTemplates** (`/character-lifecycle/template/list`): Returns all lifecycle and heritage templates for a game service.

### Bloodline Management (4 endpoints)

- **GetBloodline** (`/character-lifecycle/bloodline/get`): Returns bloodline definition: origin, trait signature, member count, generation span.

- **ListBloodlines** (`/character-lifecycle/bloodline/list`): Paged list of bloodlines within a game service. Filterable by trait signature, minimum generation depth, minimum member count.

- **EstablishBloodline** (`/character-lifecycle/bloodline/establish`): Manually establishes a bloodline (divine intervention, noble declaration). Creates bloodline record and assigns to specified character and their ancestors (retroactive). Publishes `lifecycle.bloodline-formed`. Requires `developer` role.

- **QueryBloodlineMembers** (`/character-lifecycle/bloodline/query-members`): Returns living members of a bloodline with their generation depth and trait expression.

### Cleanup Endpoints (2 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByCharacter** (`/character-lifecycle/cleanup-by-character`): Removes lifecycle profile, genetic profile, and bloodline memberships. Updates parent records (decrement child counts). Does NOT cascade-delete children (they exist independently).
- **CleanupByRealm** (`/character-lifecycle/cleanup-by-realm`): Removes all lifecycle data for characters in the realm.

### Compression Endpoints (2 endpoints)

- **GetCompressData** (`/character-lifecycle/get-compress-data`): Returns a `LifecycleArchive` (extends `ResourceArchiveBase`) containing: lifecycle summary (stages traversed, marriages, children born/surviving, fulfillment score, afterlife path), heritage profile (phenotype, aptitudes, bloodlines), and generational context (parent summaries, notable ancestors). This is key content flywheel material.
- **RestoreFromArchive** (`/character-lifecycle/restore-from-archive`): Restores lifecycle profile and genetic data from archive. Heritage is restored as-is (genetic data doesn't change). Lifecycle stage is set to the archived stage. Fulfillment score preserved.

---

## Variable Provider: `${heritage.*}` Namespace

Implements `IVariableProviderFactory` (via `HeritageProviderFactory`). Loads from the cached lifecycle manifest.

### Genetic Traits

| Variable | Type | Description |
|----------|------|-------------|
| `${heritage.species}` | string | Character's primary species code |
| `${heritage.hybrid}` | bool | Whether character is a species hybrid |
| `${heritage.secondary_species}` | string | Secondary species code (empty if not hybrid) |
| `${heritage.generation}` | int | Generation depth from earliest tracked ancestor |
| `${heritage.trait.<traitCode>}` | float | Expressed phenotype value for a specific trait (0.0-1.0) |
| `${heritage.trait.<traitCode>.latent}` | bool | Whether the trait is still latent (not yet expressed due to expression delay) |

### Aptitudes

| Variable | Type | Description |
|----------|------|-------------|
| `${heritage.aptitude.<domain>}` | float | Inherited aptitude for a skill domain (0.0-1.0). Represents innate potential, not learned skill. |
| `${heritage.aptitude.strongest}` | string | Domain code of the character's highest aptitude |
| `${heritage.aptitude.weakest}` | string | Domain code of the character's lowest aptitude |

### Bloodlines

| Variable | Type | Description |
|----------|------|-------------|
| `${heritage.bloodline.count}` | int | Number of bloodlines the character belongs to |
| `${heritage.has_bloodline.<code>}` | bool | Whether character belongs to a named bloodline |
| `${heritage.bloodline.<code>.generation}` | int | Generations from bloodline origin to this character |
| `${heritage.bloodline.codes}` | string | Comma-separated list of bloodline codes (for display/logging) |

### Parentage

| Variable | Type | Description |
|----------|------|-------------|
| `${heritage.has_parents}` | bool | Whether character was born through procreation (vs. seeded) |
| `${heritage.parent_a_species}` | string | Parent A's species code |
| `${heritage.parent_b_species}` | string | Parent B's species code |

---

## Variable Provider: `${lifecycle.*}` Namespace

Implements `IVariableProviderFactory` (via `LifecycleProviderFactory`). Loads from the cached lifecycle manifest.

### Age and Stage

| Variable | Type | Description |
|----------|------|-------------|
| `${lifecycle.age}` | int | Character's current age in game years |
| `${lifecycle.stage}` | string | Current lifecycle stage code |
| `${lifecycle.stage_progress}` | float | Progress through current stage (0.0-1.0) |
| `${lifecycle.is_infant}` | bool | Convenience: stage == "infant" |
| `${lifecycle.is_child}` | bool | Convenience: stage == "child" |
| `${lifecycle.is_adolescent}` | bool | Convenience: stage == "adolescent" |
| `${lifecycle.is_adult}` | bool | Convenience: stage == "adult" |
| `${lifecycle.is_elder}` | bool | Convenience: stage == "elder" |
| `${lifecycle.is_dying}` | bool | Convenience: stage == "dying" |

### Health and Fertility

| Variable | Type | Description |
|----------|------|-------------|
| `${lifecycle.health_modifier}` | float | Age-based health multiplier (1.0 = peak, declining in elder/dying) |
| `${lifecycle.fertility}` | float | Current fertility modifier (species + age + heritage) |
| `${lifecycle.can_marry}` | bool | Whether current stage permits marriage |
| `${lifecycle.can_procreate}` | bool | Whether current stage and fertility permit procreation |

### Family

| Variable | Type | Description |
|----------|------|-------------|
| `${lifecycle.is_married}` | bool | Whether character has an active SPOUSE relationship |
| `${lifecycle.spouse_id}` | string | Spouse character ID (empty if unmarried) |
| `${lifecycle.child_count}` | int | Number of living children |
| `${lifecycle.total_children}` | int | Total children ever born (including deceased) |
| `${lifecycle.has_children}` | bool | Whether character has any living children |
| `${lifecycle.household_role}` | string | Role in primary household Organization (empty if no household) |
| `${lifecycle.is_household_head}` | bool | Whether character leads their household |

### Fulfillment (alive characters)

| Variable | Type | Description |
|----------|------|-------------|
| `${lifecycle.fulfillment_projection}` | float | Projected fulfillment if the character died right now (computed from current Disposition drives). Useful for behavior: "Am I living a fulfilled life?" |
| `${lifecycle.years_remaining}` | int | Estimated years until natural death (naturalDeathYear - currentAge, clamped to 0) |
| `${lifecycle.mortality_awareness}` | float | Combined urgency signal: `1.0 - (years_remaining / species_max_age)`. Higher as death approaches. Feeds into elder behavior patterns. |

### ABML Usage Examples

```yaml
flows:
  elder_contemplation:
    - cond:
        # Getting old, no heir designated
        - when: "${lifecycle.is_elder && !organization.primary.has_successor}"
          then:
            - call: evaluate_succession_options
            - set:
                urgency: "${lifecycle.mortality_awareness}"

        # High fulfillment projection -- content with life
        - when: "${lifecycle.fulfillment_projection > 0.7 && lifecycle.is_elder}"
          then:
            - call: mentor_younger_generation
            - set:
                mood: "content"

        # Low fulfillment, dying -- desperate for closure
        - when: "${lifecycle.fulfillment_projection < 0.3 && lifecycle.is_dying}"
          then:
            - call: pursue_unfinished_business
            - set:
                urgency: "1.0"
                desperation: "${1.0 - lifecycle.fulfillment_projection}"

  consider_parenthood:
    - cond:
        # Married, fertile, drive to protect family
        - when: "${lifecycle.is_married && lifecycle.can_procreate
                  && disposition.drive.has_drive.protect_family
                  && lifecycle.child_count < 3}"
          then:
            - call: consider_having_child
            - set:
                desire_strength: "${disposition.drive.protect_family.intensity
                                    * lifecycle.fertility}"

        # Heritage aptitude alignment -- want a child to carry the legacy
        - when: "${lifecycle.is_married && lifecycle.can_procreate
                  && heritage.aptitude.strongest == 'enchanting'
                  && disposition.drive.has_drive.master_craft}"
          then:
            - call: consider_legacy_child
            # "I want a child who inherits my gift"

  heritage_awareness:
    - cond:
        # Member of a notable bloodline
        - when: "${heritage.has_bloodline.ironforge && lifecycle.is_adult}"
          then:
            - cond:
                # Living up to the family name
                - when: "${heritage.aptitude.smithing > 0.6
                          && disposition.drive.has_drive.honor_legacy}"
                  then:
                    - call: pursue_family_tradition
                - otherwise:
                    # Breaking from tradition
                    - call: forge_own_path
                    - set:
                        identity_conflict: "${heritage.aptitude.smithing
                                             - disposition.drive.master_craft.intensity}"
```

---

## Visual Aid

Lifecycle identity, aging, and heritage computation are owned here. Marriage ceremony is Contract (L1). Spousal bond is Relationship (L2). Household structure is Organization (L4). Emotional state is Disposition (L4). Genetic trait expression feeds into Character-Personality (L4). Backstory seeding feeds into Character-History (L4). Archive compression is Resource (L1). Narrative generation from archives is Storyline (L4). Guardian spirit evolution is Seed (L2). This service composes them all into a coherent generational lifecycle, following the same orchestration pattern as Quest (over Contract), Escrow (over Currency + Item), and Organization (over Currency + Inventory + Contract + Relationship).

### Lifecycle Flow

```
┌──────────────────────────────────────────────────────────────────────┐
│                    CHARACTER LIFECYCLE FLOW                            │
│                                                                      │
│  CREATION                                                            │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐                    │
│  │ Procreation│  │ Seeded     │  │ Divine     │                    │
│  │ (parents)  │  │ (world init│  │ (god act)  │                    │
│  └─────┬──────┘  └─────┬──────┘  └─────┬──────┘                    │
│        │               │               │                             │
│        └───────────────┼───────────────┘                             │
│                        ▼                                             │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ INFANT (0-2)                                                 │    │
│  │ Fully dependent. Heritage genotype set but latent.           │    │
│  │ Cannot be possessed by guardian spirit.                      │    │
│  └──────────────────────┬──────────────────────────────────────┘    │
│                          │ worldstate.year-changed (age >= 3)        │
│                          ▼                                           │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ CHILD (3-14)                                                 │    │
│  │ Dependent role. Learning phase. Guardian spirit can possess   │    │
│  │ with minimal agency. Heritage aptitudes begin to emerge.     │    │
│  │ THE TUTORIAL GENERATION (from Player Vision).                │    │
│  └──────────────────────┬──────────────────────────────────────┘    │
│                          │ worldstate.year-changed (age >= 15)       │
│                          ▼                                           │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ ADOLESCENT (15-17)                                           │    │
│  │ Heritage traits fully express. Personality initializes from  │    │
│  │ heritage phenotype. Coming-of-age event. Role change in     │    │
│  │ household (dependent → potential heir).                       │    │
│  └──────────────────────┬──────────────────────────────────────┘    │
│                          │ worldstate.year-changed (age >= 18)       │
│                          ▼                                           │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ ADULT (18-59)                                                │    │
│  │ FULL AGENCY. Can marry, work, own organizations, hold any   │    │
│  │ role. Peak fertility window. PRIMARY GAMEPLAY PHASE.         │    │
│  │                                                               │    │
│  │ Events: marriage, procreation, career, adventures, drives    │    │
│  │ Interactions: Quest, Organization, Disposition, Hearsay      │    │
│  └──────────────────────┬──────────────────────────────────────┘    │
│                          │ worldstate.year-changed (age >= 60)       │
│                          ▼                                           │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ ELDER (60-79)                                                │    │
│  │ Health declining. Fertility reduced. Wisdom bonus.           │    │
│  │ Succession planning urgency increases. Legacy drives         │    │
│  │ intensify. Mentoring younger generation.                     │    │
│  └──────────────────────┬──────────────────────────────────────┘    │
│                          │ worldstate.year-changed (age >= death yr) │
│                          ▼                                           │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ DYING                                                        │    │
│  │ Final fulfillment calc begins. Last-will contract triggers.  │    │
│  │ Disposition drives evaluated for closure. Health severely    │    │
│  │ reduced. The clock is running out.                           │    │
│  └──────────────────────┬──────────────────────────────────────┘    │
│                          │ Death processing triggered                │
│                          ▼                                           │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ DEATH → CONTENT FLYWHEEL                                    │    │
│  │                                                               │    │
│  │ 1. Fulfillment score calculated from Disposition drives      │    │
│  │ 2. Guardian spirit receives logos (proportional to fulfillment│   │
│  │ 3. Archive compression triggered across all character-* svcs │    │
│  │ 4. Organization succession executes                          │    │
│  │ 5. Inheritance processed (heirlooms, testament)              │    │
│  │ 6. Afterlife pathway determined                              │    │
│  │ 7. Storyline receives archive → narrative seeds generated    │    │
│  │ 8. Regional watchers evaluate for narrative opportunity      │    │
│  │                                                               │    │
│  │ "Death creates, not destroys."                               │    │
│  └─────────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────┘
```

### Heritage Computation

```
┌──────────────────────────────────────────────────────────────────────┐
│                    HERITAGE COMPUTATION                                │
│                                                                      │
│  PARENT A (Erik)                    PARENT B (Marta)                 │
│  ┌─────────────────────┐           ┌─────────────────────┐          │
│  │ Genotype:            │           │ Genotype:            │          │
│  │  strength: [0.8, 0.6]│           │  strength: [0.4, 0.3]│          │
│  │  magic:    [0.2, 0.1]│           │  magic:    [0.7, 0.8]│          │
│  │  patience: [0.6, 0.5]│           │  patience: [0.7, 0.4]│          │
│  │                       │           │                       │          │
│  │ Bloodlines: [Erikson] │           │ Bloodlines: [Meadow] │          │
│  └──────────┬────────────┘           └──────────┬────────────┘          │
│             │                                    │                     │
│             │    ┌───────────────────────┐       │                     │
│             └───►│  RECOMBINATION        │◄──────┘                     │
│                  │                       │                             │
│                  │  For each trait:       │                             │
│                  │  1. Pick one allele    │                             │
│                  │     from each parent   │                             │
│                  │     (random selection) │                             │
│                  │                       │                             │
│                  │  2. Check mutation     │                             │
│                  │     (2% chance per     │                             │
│                  │      trait)            │                             │
│                  │                       │                             │
│                  │  3. Apply dominance    │                             │
│                  │     model to produce   │                             │
│                  │     phenotype          │                             │
│                  └───────────┬───────────┘                             │
│                              │                                         │
│                              ▼                                         │
│  CHILD (Kael)                                                         │
│  ┌─────────────────────────────────────────────────────┐              │
│  │ Genotype:                                            │              │
│  │   strength: alleleA=0.8(Erik), alleleB=0.4(Marta)   │              │
│  │   magic:    alleleA=0.2(Erik), alleleB=0.7(Marta)   │              │
│  │   patience: alleleA=0.6(Erik), alleleB=0.7(Marta)   │              │
│  │                                                       │              │
│  │ Phenotype (after dominance):                          │              │
│  │   strength: 0.6  (blending: (0.8+0.4)/2)            │              │
│  │   magic:    0.7  (dominant_high: max(0.2, 0.7))     │              │
│  │   patience: 0.65 (blending: (0.6+0.7)/2)            │              │
│  │                                                       │              │
│  │ Aptitudes (derived from phenotype):                   │              │
│  │   smithing:    0.55 (str*0.4 + patience*0.4 + ...)   │              │
│  │   enchanting:  0.68 (magic*0.6 + patience*0.3 + ...) │              │
│  │   ← Kael has inherited a gift for enchanted smithing! │              │
│  │                                                       │              │
│  │ Bloodlines: [Erikson, Meadow]                         │              │
│  │ Mutations: hand_eye +0.08 (lucky!)                    │              │
│  └─────────────────────────────────────────────────────┘              │
│                                                                      │
│  WHAT THIS FEEDS:                                                    │
│  ┌──────────────────────────────────────────────┐                   │
│  │ Character-Personality: initial trait bias      │                   │
│  │   (patience 0.65 → conscientiousness bias)    │                   │
│  │                                                │                   │
│  │ Disposition: family drive seeds                │                   │
│  │   (blacksmith family → latent master_craft)    │                   │
│  │                                                │                   │
│  │ Actor: ${heritage.aptitude.enchanting} = 0.68  │                   │
│  │   (behavior expressions use inherited talent)  │                   │
│  │                                                │                   │
│  │ Quest: prerequisite checks                     │                   │
│  │   ("heritage.aptitude.enchanting > 0.5" ✓)     │                   │
│  └──────────────────────────────────────────────┘                   │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no code. The following phases are planned:

### Phase 0: Prerequisites (changes to existing services)

- **Worldstate**: Must exist and publish `worldstate.year-changed` events. Lifecycle cannot function without the game clock. Worldstate is the highest-priority prerequisite.
- **Organization Phase 5**: Household pattern must be at least partially implemented for household creation/member management during marriage and procreation.
- **Disposition**: Must exist for fulfillment calculation during death processing. Without Disposition, fulfillment defaults to neutral (0.3) -- functional but the content flywheel loses its richest dimension.

### Phase 1: Lifecycle Templates and Profiles

- Create `character-lifecycle-api.yaml` schema with all endpoints
- Create `character-lifecycle-events.yaml` schema
- Create `character-lifecycle-configuration.yaml` schema
- Generate service code
- Implement lifecycle template CRUD (seed, get, list per species)
- Implement lifecycle profile CRUD (seed, get, query)
- Implement basic aging: subscribe to `worldstate.year-changed`, advance ages, detect stage transitions
- Implement stage change event publishing
- Implement resource cleanup and compression callbacks

### Phase 2: Heritage Engine

- Implement heritable trait template CRUD
- Implement genetic profile storage
- Implement recombination algorithm (allele selection, dominance, mutation)
- Implement phenotype expression (immediate and delayed)
- Implement aptitude derivation from phenotype
- Implement heritage variable provider factory (`${heritage.*}` namespace)
- Implement lifecycle variable provider factory (`${lifecycle.*}` namespace)
- Implement SeedGeneticProfile for first-generation characters
- Implement SimulateOffspring endpoint

### Phase 3: Procreation

- Implement fertility calculation (species + age + heritage modifier)
- Implement pregnancy tracking (pending births with expected dates)
- Implement pregnancy worker (worldstate.day-changed → birth processing)
- Implement full procreation flow (heritage computation → character creation → relationships → household → backstory → events)
- Implement MaxChildrenPerPair and MaxChildrenPerCharacter limits
- Wire Character-Personality initialization from heritage phenotype
- Wire Character-History backstory seeding from birth context

### Phase 4: Marriage

- Implement marriage eligibility validation
- Implement marriage contract creation via IContractClient
- Implement household resolution (join, merge, create new)
- Implement Disposition feeling seeds between spouses
- Implement divorce processing (contract termination → lifecycle update)
- Wire Organization household events

### Phase 5: Death Processing

- Implement fulfillment calculation from Disposition drives
- Implement guardian spirit contribution calculation
- Implement archive compression trigger via Resource
- Implement inheritance processing (testament contracts, heirloom transfers)
- Implement afterlife pathway determination
- Implement full death event publishing
- Wire to content flywheel (Storyline archive consumption)
- Implement RecordDeath endpoint for external death reporters

### Phase 6: Bloodlines

- Implement bloodline storage and queries
- Implement bloodline formation worker (automatic detection from generational trait patterns)
- Implement manual bloodline establishment (EstablishBloodline endpoint)
- Implement bloodline variable provider (`${heritage.has_bloodline.*}`)
- Implement family tree query endpoint

### Phase 7: Species Hybridization

- Implement hybrid trait template storage
- Implement cross-species compatibility matrix
- Implement hybrid recombination rules
- Implement hybrid fertility modifiers
- Implement hybrid-specific trait emergence

---

## Potential Extensions

1. **Underworld gameplay**: Characters who die enter an afterlife phase determined by their fulfillment and pathway. The underworld is not just a narrative concept -- it could be a game space where dead characters exist as spirits, pursuing unfinished business, mentoring descendants, or earning their way to a higher afterlife state. lib-character-lifecycle tracks the afterlife pathway; a future `lib-underworld` service could provide the gameplay layer.

2. **Epigenetic modification**: Environmental factors during childhood could modify gene expression without changing the genotype. A child raised in a magical academy might have their `magical_aptitude` phenotype enhanced beyond what their genotype predicts. A child raised in harsh conditions might have enhanced `physical_resilience`. These modifications are heritable at reduced effect (Lamarckian inheritance as a game mechanic).

3. **Genetic diseases and conditions**: Some allele combinations could produce conditions that affect gameplay. A recessive trait that only expresses when both alleles are low could create genetic diseases. This adds depth to marriage decisions (NPC actors evaluating genetic compatibility) and creates medical gameplay (herbalists treating genetic conditions). Requires careful design to avoid uncomfortable real-world parallels.

4. **Prophetic births**: Rare genetic combinations (multiple high-value traits converging from distinct bloodlines) could be flagged as "prophetic births" -- characters marked for narrative significance. Regional watchers and Storyline would prioritize these characters for dramatic scenarios. "A child born of three ancient bloodlines" is a classic fantasy trope that emerges naturally from the genetics system.

5. **Ancestral memory**: Heritage data from compressed ancestor archives could occasionally surface as "ancestral memories" in descendant characters -- flashes of insight or skill from a forebear. This bridges Heritage and the content flywheel: a descendant blacksmith briefly channels their great-grandmother's master technique. Implemented as a Disposition modifier triggered by heritage + domain engagement conditions.

6. **Selective breeding programs**: Factions or organizations could implement eugenics programs as a game mechanic (with appropriate moral weight via the Obligation system). An NPC noble house's GOAP planner evaluates potential marriages based on genetic compatibility with house bloodline goals. This is an NPC behavior pattern, not a service feature -- the heritage variable provider gives behavior authors the data to express it.

7. **Twin births**: Procreation could produce multiple children simultaneously. Twins share the same recombination event but with independent mutation rolls, creating characters who are genetically similar but not identical. Triplets, etc. at very low probability. Twin bonds could be a special Relationship type.

8. **Age acceleration/deceleration zones**: Integration with Worldstate's planned "magical time dilation zones" extension. Characters in accelerated zones age faster. Characters in decelerated zones age slower. This creates geographic gameplay: the elven forest where time moves slowly, the cursed wasteland where characters age rapidly.

9. **Reincarnation**: Instead of or in addition to the content flywheel, a fulfilled character's archive could be partially reincarnated in a new character (born to a different family, different era). The reincarnated character has fragments of the previous life's heritage + some drive echoes. This connects the generational system to the guardian spirit concept -- the spirit carries understanding across lives while the character is genuinely new.

10. **Client events for lifecycle milestones**: `character-lifecycle-client-events.yaml` for pushing lifecycle notifications to connected WebSocket clients: "Your character is approaching elder age," "A child has been born in your household," "Your character's fulfillment projection has improved." Makes the generational system visible to the player through the Gardener system.

---

## Why Not Extend Character or Character-History?

This service is deliberately character-specific. Characters are the entity type that opts into the full cognitive stack (personality, encounters, history, disposition, hearsay, lifecycle). Animals, creatures, and other living entities that don't have guardian spirits, personality profiles, or generational continuity don't need this service. A future `lib-habitat` or `lib-ecology` service could handle population dynamics and creature breeding at a simpler level, but that is a separate concern. If an entity is complex enough to need lifecycle management -- if it has personality, drives, social relationships, and generational continuity -- it IS a character, whether it looks like a human, an elf, a sentient dragon, or a talking cat.

Heritage (genetic trait inheritance) lives within this service rather than as a standalone plugin. The primary write path for genetic data is procreation -- the moment this service orchestrates. The read path is thin (a variable provider and occasional queries from Quest/Divine). This follows the MusicTheory/Music parallel: complex computation, thin API surface. If heritage grows complex enough to warrant its own service boundary (species hybridization becomes massive, bloodline politics becomes a full system), it can be extracted later.

The question arises: why not add aging to Character or genetics to Character-History?

**Character (L2) handles entity CRUD, not temporal progression.** Character answers "does this character exist and what are its core properties?" Adding aging, lifecycle stages, marriage orchestration, and death processing to Character would turn a foundational CRUD service into a complex orchestration layer with L4 dependencies (Disposition, Organization, Storyline). Per the service hierarchy, L2 services cannot depend on L4. Character must remain layer-appropriate: it knows what a character IS (realm, species, name), not what lifecycle stage they're in or what they've inherited.

**Character-History (L4) handles biographical records, not biological inheritance.** History answers "what events happened in this character's past?" Genetics is not an event -- it's a biological property set at conception. Adding genotype/phenotype data to History conflates "what happened" with "what you were born with." History should remain the record of experience; Heritage handles the record of nature.

**Character-Personality (L4) handles the self-model, not inherited potential.** Personality answers "what kind of person am I right now?" Heritage answers "what potential was I born with?" A character's personality evolves from experience (Disposition drives, encounter trauma, aging effects). Their heritage is immutable. Mixing mutable personality traits with immutable genetic traits in the same service creates confusion about what changes and what doesn't.

**Species (L2) handles species-level definitions, not individual genetics.** Species answers "what are elves like in general?" Heritage answers "what did THIS elf inherit from THEIR parents?" Species provides the template; Heritage provides the instance.

| Service | Question It Answers | Domain |
|---------|--------------------|--------------------|
| **Character** | "Does this character exist?" | Entity CRUD (L2) |
| **Species** | "What are this species' general traits?" | Species-level templates (L2) |
| **Character-Personality** | "What kind of person are they now?" | Mutable self-model (L4) |
| **Character-History** | "What happened in their past?" | Biographical events (L4) |
| **Character-Lifecycle** | "Where are they in life, and what were they born with?" | Temporal progression + genetic inheritance (L4) |

The pipeline with lifecycle:
```
Species (species-level template)  ─────────────────────────────────────┐
Heritage (individual genetic inheritance)  ────────────────────────────┤
                                                                       ▼
                                                            CHARACTER-LIFECYCLE
                                                            (what they're born with
                                                             + where they are in life)
                                                                       │
                         ┌─────────────────┬───────────────┬──────────┘
                         ▼                 ▼               ▼
                   Personality        Disposition      Actor ABML
                   (initial traits)   (initial drives) (${heritage.*}
                                                        ${lifecycle.*})
                         │                 │
                         ▼                 ▼
                   Experience shapes    Experience shapes
                   who they become      what they aspire to
                   (mutable)            (mutable)
```

Heritage provides the NATURE. Personality/Disposition/History provide the NURTURE. Lifecycle provides the CLOCK that connects them across generations.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified. Plugin is pre-implementation.*

### Intentional Quirks (Documented Behavior)

1. **Stage codes are opaque strings**: Not enums. Follows the same extensibility pattern as seed type codes, collection type codes, and organization type codes. `infant`, `child`, `adolescent`, `adult`, `elder`, `dying` are Arcadia conventions. A game with immortal characters might define only `young` and `mature`. A game with insect-like characters might have `larva`, `pupa`, `adult`, `breeder`, `queen`.

2. **Heritage is immutable after creation**: Once a character's genetic profile is stored, it never changes. This is intentional -- genetics don't change in life. What changes is phenotype expression (latent traits activating at later stages) and how heritage interacts with experience (personality evolving from heritage baselines through encounters).

3. **Natural death year is predetermined but hidden**: When a character is created, their natural death year is computed from species longevity + heritage + randomness. This is stored but not exposed to behavior systems or players. The character doesn't "know" when they'll die naturally. The `${lifecycle.years_remaining}` variable estimates based on the hidden value but is presented as a rough sense of mortality, not a countdown.

4. **Pregnancy is tracked as a pending record, not a status effect**: Pregnancy uses a simple pending-birth record with an expected date, not the Status service. This avoids coupling lifecycle to the status system and keeps the implementation simple. If games want pregnancy-as-status-effect (buffs, debuffs, visible state), they can add a Status template that lifecycle applies during the pregnancy period.

5. **First-generation characters have no genotype**: Characters created via seeding (world initialization) have phenotype values but no allele pairs. They are effectively "genotype = phenotype" -- their expressed traits are their genetic contribution to children. This simplifies world initialization (no need to define full genotypes for thousands of starting characters) while preserving full genetics for all subsequent generations.

6. **Death processing is idempotent**: Calling RecordDeath for a character that has already been processed returns the existing result without re-executing the pipeline. This prevents duplicate archive compression, duplicate inheritance processing, and duplicate spirit contributions if multiple systems report the same death.

7. **Fulfillment score is permanent**: Once calculated at death, fulfillment is never recalculated. Even if Disposition data is later corrected or the character is restored from archive, the fulfillment score reflects the moment of death. This is philosophically intentional: fulfillment is the judgment of a completed life, not a retroactive assessment.

8. **Children exist independently of parents**: Deleting a parent character does NOT cascade-delete children. Children are independent entities with their own lifecycle profiles. The heritage data records parentage, but the child's existence doesn't depend on the parent's continued existence in the system.

9. **Bloodline formation is probabilistic, not guaranteed**: Having 3+ generations of high-strength characters doesn't automatically create an "Ironforge Strength" bloodline. The formation check evaluates trait consistency above `BloodlineFormationTraitThreshold` across `BloodlineFormationGenerations`. Random variation in recombination means not every strong family becomes a named bloodline. This makes bloodlines rare and meaningful.

10. **Cross-species hybridization is opt-in per game**: If no `HybridTraitTemplate` exists for a species pair, cross-species procreation is denied. Games explicitly enable cross-species combinations they want to support. This prevents unexpected species combinations from creating characters with undefined trait interactions.

### Design Considerations (Requires Planning)

1. **Scale of aging processing**: With 100,000+ NPCs, the `worldstate.year-changed` handler must process all living characters in a realm. At 24:1 time ratio, this fires every ~12 real days. The batch size (`AgingBatchSize`) and distributed lock ensure this doesn't overload the system, but the total processing time for a full realm must be measured. May need spatial partitioning (process characters by location batch).

2. **Pregnancy timing precision**: The pregnancy worker triggers on `worldstate.day-changed` events. At 24:1 ratio, game days pass every ~1 real hour. Pregnancy duration of 270 game days = ~11 real days. The precision of birth timing depends on how frequently day-changed events fire and whether catch-up events handle pregnancies that should have completed during downtime.

3. **Heritage-to-Personality pipeline**: When heritage phenotype is used to seed initial personality traits (Phase 3), the mapping between heritage traits and personality axes needs to be data-driven and configurable. The trait template's `personalityMapping` field handles this, but the actual computation (how `patience: 0.65` translates to `conscientiousness: 0.6`) needs design.

4. **Death processing atomicity**: The death pipeline involves multiple service calls (Disposition query, Resource trigger, Organization succession, Inventory transfer, Seed growth). If any step fails, the character should not be left in an inconsistent state. Need a saga pattern or compensating transactions. The distributed lock prevents concurrent processing, but individual step failures need graceful handling.

5. **Archive format for content flywheel**: The lifecycle archive (get-compress-data) must produce data that Storyline can consume for narrative generation. The format needs to include not just raw data but narrative-ready summaries: "achieved mastery through dedication," "died with unfulfilled ambitions," "founded a dynasty of enchanter-smiths." The summary generation is a lifecycle concern (it has all the data), not a Storyline concern.

6. **Household-Lifecycle-Organization triangle**: Marriage and procreation involve three services (Lifecycle orchestrates, Organization manages household structure, Relationship tracks bonds). The ordering and failure handling of these three-way interactions needs careful design. Who is the authority if Organization rejects a household change that Lifecycle initiated?

7. **Guardian spirit seed growth integration**: The fulfillment → logos → guardian spirit seed growth pipeline requires that the account's guardian seed type and growth domain are defined. This is a Seed configuration concern that must be established before death processing can contribute to spirit evolution.

8. **NPC marriage decision-making**: NPC actors need ABML behaviors that evaluate potential marriage partners based on lifecycle/heritage variables. This is an ABML authoring concern, not a service concern, but the variable providers must expose enough data for sophisticated partner evaluation (genetic compatibility, fertility window, household status, drive alignment).

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Worldstate is the primary prerequisite (Phase 0) -- lifecycle cannot function without the game clock. Organization's household pattern (Phase 5 of Organization) is the secondary prerequisite for full marriage/procreation flows but not for basic aging (Phase 1). Disposition enhances death processing but is not a blocker (fulfillment defaults to neutral without it). Phase 1 (templates and aging) is self-contained once Worldstate exists. Phase 2 (heritage engine) is self-contained. Phases 3-5 progressively integrate with more services.*
