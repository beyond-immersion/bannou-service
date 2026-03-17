# Disposition Plugin Deep Dive

> **Plugin**: lib-disposition (not yet created)
> **Schema**: `schemas/disposition-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: disposition-feelings (MySQL), disposition-drives (MySQL), disposition-cache (Redis), disposition-lock (Redis) — all planned
> **Layer**: GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Implementation Map**: [docs/maps/DISPOSITION.md](../maps/DISPOSITION.md)
> **Planning**: [STORY-SYSTEM.md](../guides/STORY-SYSTEM.md), [MORALITY-SYSTEM.md](../guides/MORALITY-SYSTEM.md), [CHARACTER-COMMUNICATION.md](../guides/CHARACTER-COMMUNICATION.md)
> **Short**: Emotional synthesis — per-character feelings about entities and long-term aspirational drives

## Overview

Emotional synthesis and aspirational drive service (L4 GameFeatures) for NPC inner life. Maintains per-character **feelings** about specific entities (other characters, locations, factions, organizations, and the guardian spirit) and **drives** (long-term aspirational goals that shape behavior priorities). Feelings are the personality-filtered, experience-weighted, hearsay-colored subjective emotional state that a character carries; drives are intrinsic motivations that emerge from personality, circumstance, and accumulated experience. Game-agnostic: feeling axes, drive types, guardian spirit mechanics, and synthesis weights are configured through disposition configuration and seed data at deployment time. Internal-only, never internet-facing.

---

## Core Mechanics: Feelings

The current NPC cognition pipeline has all the raw data needed to infer emotional state -- encounter sentiment, personality traits, relationship bonds, backstory elements, hearsay beliefs -- but no service that synthesizes these into a coherent directed emotional response. An NPC either has direct encounter data (cold mathematical sentiment) or has nothing. There is no mechanism for "Kael feels grateful toward Mira" as a persistent, evolving emotional state that colors future behavior, nor for "Kael aspires to become the greatest blacksmith in the realm" as an intrinsic motivation that shapes daily decisions.

| Subsystem | What It Answers | Persistence | Update Pattern |
|-----------|----------------|-------------|----------------|
| **Feelings** | "How do I feel about X right now?" | Persistent with decay | Event-driven + periodic synthesis |
| **Drives** | "What do I aspire to become/achieve?" | Persistent with evolution | Experience-driven + periodic evaluation |

Feelings are directed emotional states: not personality traits (self-model) and not encounter records (factual data). Feelings are the subjective interpretation layer where personality meets experience. A character with high aggression interprets a neutral encounter differently than a peaceful character. A character who heard rumors of danger feels differently about a location than one who has visited it. Feelings persist beyond their causes -- resentment lingers after the betrayer is gone, gratitude endures after the helper has moved on.

Feelings use a dual-layer computation: a **base** value computed from source services (personality-filtered encounter sentiment + hearsay beliefs + relationship-type defaults) that drifts as source data changes, plus a persistent **modifier** (emotional residue -- betrayal trauma, lingering gratitude, spirit resentment) that decays slowly but can be reinforced by events. The effective value is `clamp(base + modifier, -1.0, 1.0)`. This means feelings are never purely computed (they have emotional memory) and never purely static (they respond to changing circumstances).

### Feeling Model

Every feeling has the same structure regardless of target entity type:

```
FeelingEntry:
 feelingId: Guid # Unique identifier
 characterId: Guid # Who feels this
 targetType: EntityType # $ref: EntityType (Character, Location, Faction, Organization, etc.)
 targetId: Guid # Entity ID of the target (e.g., characterId, locationId, factionId)
 axis: string # Emotional axis (opaque string, e.g., "trust", "fear", "warmth")
 effectiveValue: float # Computed: clamp(baseValue + modifier, -1.0, 1.0) — range [-1.0, 1.0]
 baseValue: float # Computed from source services — range [-1.0, 1.0]
 modifier: float # Persistent emotional residue — range [-1.0, 1.0]
 modifierDecayRate: float # How fast the modifier decays toward 0 (per day) — range [0.0, 1.0]
 lastSynthesizedAt: DateTimeOffset # When base was last recomputed from sources
 lastModifiedAt: DateTimeOffset # When modifier was last changed
 sourceBreakdown: SynthesisBreakdown # Typed breakdown of source contributions to base value
```

### SynthesisBreakdown Model

Typed breakdown of source contributions to a feeling's base value (per FOUNDATION TENETS — no metadata bag contracts):

```
SynthesisBreakdown:
 encounterContribution: float # Weighted encounter sentiment signal — range [-1.0, 1.0]
 hearsayContribution: float # Weighted hearsay belief signal — range [-1.0, 1.0]
 relationshipContribution: float # Weighted relationship type signal — range [-1.0, 1.0]
 personalityContribution: float # Weighted personality bias signal — range [-1.0, 1.0]
 encounterWeight: float # Effective weight used (after redistribution) — range [0.0, 1.0]
 hearsayWeight: float # Effective weight used — range [0.0, 1.0]
 relationshipWeight: float # Effective weight used — range [0.0, 1.0]
 personalityWeight: float # Effective weight used — range [0.0, 1.0]
```

Each contribution field stores the signal value from that source BEFORE weighting. The effective weights reflect redistribution when sources are unavailable (e.g., if hearsay is missing, its weight redistributes to other sources proportionally). `base = sum(contribution_i * weight_i)`.

### Feeling Axes by Target Type

Axes are opaque strings (not enums), following the same extensibility pattern as violation type codes, collection type codes, and seed type codes. These are conventions, not constraints:

| Target Type (EntityType) | Common Axes | Value Meaning |
|--------------------------|-------------|---------------|
| **Character** | `trust`, `fear`, `warmth`, `respect`, `resentment`, `admiration`, `jealousy` | -1.0 (opposite) to +1.0 (strong) |
| **Character** (guardian spirit) | `trust`, `resentment`, `familiarity`, `gratitude`, `defiance` | -1.0 to +1.0 |
| **Location** | `comfort`, `dread`, `nostalgia`, `curiosity`, `belonging` | -1.0 to +1.0 |
| **Faction** | `loyalty`, `fear`, `respect`, `resentment`, `belonging` | -1.0 to +1.0 |
| **Organization** | `loyalty`, `pride`, `resentment`, `trust` | -1.0 to +1.0 |

**Guardian spirit feelings**: The guardian spirit is a Character entity in the NEXIUS system realm (per the actor-bound entity pattern). Feelings about the guardian spirit use `targetType: Character` and `targetId: <guardian spirit's characterId>`. The guardian-specific axes (trust, resentment, familiarity, gratitude, defiance) are distinguished by axis codes, not by target type. The service identifies guardian spirit targets by checking whether the target character exists in the NEXIUS system realm.

### Base Value Synthesis

The base value is computed from available source services. Each source contributes a weighted component, and the weights are configurable per axis:

```
base("trust", character, MIRA) =
 w_encounter * encounterTrustSignal(MIRA)
 + w_hearsay * hearsayTrustSignal(MIRA)
 + w_relationship * relationshipTrustSignal(MIRA)
 + w_personality * personalityTrustBias()
```

Where:
- **encounterTrustSignal**: Derived from encounter sentiment. Positive encounters → positive trust signal. Weighted by memory strength (recent encounters matter more).
- **hearsayTrustSignal**: Derived from hearsay beliefs about the target (`${hearsay.character.MIRA.trustworthy}`). Confidence-weighted.
- **relationshipTrustSignal**: Derived from relationship type. FRIEND → positive base. RIVAL → negative. FAMILY → strong positive. Configurable per relationship type code.
- **personalityTrustBias**: Derived from the character's own personality. High `agreeableness` → positive trust bias toward strangers. High `neuroticism` → negative trust bias. This is the "personality filter" that makes the same raw data produce different feelings for different characters. Disposition's personality filter is stage 3 of a three-stage personality pipeline: (1) Hearsay applies personality to belief **reception** and **inference** (what the NPC thinks happened), (2) Hearsay applies personality to belief **retransmission** (what they tell others), (3) Disposition applies personality to **feeling synthesis** (how they feel about what they believe). See [Hearsay § Personality-Driven Information Processing](../plugins/HEARSAY.md#personality-driven-information-processing) for stages 1-2.

**Graceful degradation**: Each source is a soft dependency resolved at synthesis time. If encounter data is unavailable, its weight redistributes to other sources. If hearsay is disabled, feelings are computed from encounters + relationships + personality only. If ALL sources are unavailable, base defaults to 0.0 (neutral) and only the persistent modifier contributes.

### Modifier Mechanics

The modifier represents emotional residue -- persistent feelings that outlast their causes:

**Reinforcement**: Events that align with the modifier's sign strengthen it:
```
newModifier = oldModifier + reinforcementAmount * (1.0 - abs(oldModifier))
```
The `(1.0 - abs(oldModifier))` term creates diminishing returns -- strong feelings are harder to intensify further.

**Decay**: Modifiers decay toward zero over time when not reinforced:
```
decayedModifier = modifier * (1.0 - modifierDecayRate * daysSinceLastReinforcement)
```

**Shock events**: Certain events cause immediate, large modifier shifts regardless of base value. Betrayal by a trusted character. Witnessing a heroic sacrifice. Being saved from death. These are the "formative emotional experiences" that create lasting feelings.

### Guardian Spirit Feelings

Guardian spirit feelings use `targetType: Character` with `targetId` set to the guardian spirit's characterId (a Character entity in the NEXIUS system realm). This models the character's relationship with the player's guardian spirit. Unlike feelings toward other characters, guardian feelings have unique source signals:

| Axis | Source Signals |
|------|---------------|
| `trust` | Outcomes of spirit guidance (did following the nudge lead to good results?), alignment between spirit direction and personality |
| `resentment` | Frequency of being pushed against personality, forced actions that caused negative outcomes, loss of autonomy |
| `familiarity` | Accumulated time under spirit possession, number of successful collaborations |
| `gratitude` | Spirit guidance that led to positive outcomes the character wouldn't have achieved alone |
| `defiance` | Accumulated resentment that crosses a threshold, personality traits like high aggression or low agreeableness amplify |

**Spirit alignment signal**: Computed by comparing the spirit's recent nudge directions with the character's personality profile. If the spirit consistently pushes a peaceful character toward violence, the alignment signal is strongly negative. If the spirit guides an ambitious character toward achievements, alignment is positive. This creates the mechanical backing for "the character can resist or resent being pushed against their nature."

**Compliance modulation**: The `${disposition.guardian.*}` variables feed into the character's compliance system. An ABML behavior document can express:

```yaml
- when: "${disposition.guardian.resentment > 0.6 && disposition.guardian.trust < 0.3}"
 then:
 - call: resist_spirit_nudge
 - set:
 compliance_multiplier: "${0.2}" # Barely responsive to the spirit
```

---

## Core Mechanics: Drives

Drives are intrinsic motivations: not quest objectives (externally assigned) and not backstory elements (historical context). Drives emerge from the intersection of personality, circumstance, and accumulated experience. A character who grew up poor may drive toward wealth. A character who was betrayed may drive toward self-reliance. A character who witnessed injustice may drive toward becoming a protector. Drives shape GOAP goal priorities, making characters pursue long-term aims without needing explicit quest chains to motivate every decision.

Without drives, characters are reactive -- they respond to stimuli but don't pursue long-term goals. An adventurer doesn't just want to be an adventurer; they want to be an S-Class adventurer. A blacksmith doesn't just forge; they aspire to create a masterwork. Some characters lack drives entirely (layabouts, doing the minimum to survive), which is its own depth. Drives create the "chasing your dreams" mechanic from the vision, where characters have intrinsic motivations that shape their daily decisions without requiring quest chains to orchestrate every step.

### Drive Model

Drives represent intrinsic, long-term aspirational goals:

```
DriveEntry:
 driveId: Guid # Unique identifier
 characterId: Guid # Who has this drive
 driveCode: string # Opaque string (e.g., "master_craft", "protect_family", "gain_wealth")
 category: string # "mastery", "social", "survival", "identity", "exploration"
 intensity: float # 0.0 (dormant) to 1.0 (consuming obsession) — range [0.0, 1.0]
 satisfaction: float # 0.0 (unfulfilled) to 1.0 (fully satisfied) — range [0.0, 1.0]
 frustration: float # 0.0 (no obstacles) to 1.0 (blocked at every turn) — range [0.0, 1.0]
 originType: DriveOriginType # How this drive formed: Personality, Experience, Backstory, Circumstance
 originEventId: Guid? # The event/experience that catalyzed this drive (null for personality-innate)
 acquiredAt: DateTimeOffset # When the drive formed
 lastProgressAt: DateTimeOffset? # When satisfaction last increased
 lastFrustrationAt: DateTimeOffset? # When frustration last increased
```

### Drive Categories

| Category | Example Drives | What Triggers Formation |
|----------|---------------|------------------------|
| **mastery** | `master_craft`, `become_strongest`, `achieve_rank`, `perfect_technique` | Repeated engagement with a domain, personality traits (conscientiousness, openness) |
| **social** | `protect_family`, `earn_respect`, `find_love`, `build_community` | Relationship bonds, personality traits (agreeableness, loyalty), backstory elements |
| **survival** | `gain_wealth`, `secure_territory`, `ensure_safety`, `stockpile_resources` | Economic circumstances, personality traits (neuroticism), traumatic experiences |
| **identity** | `prove_worth`, `redeem_past`, `honor_legacy`, `defy_expectations` | Backstory trauma, personality conflicts, formative experiences |
| **exploration** | `see_the_world`, `discover_secrets`, `chart_unknown`, `collect_rarities` | Personality traits (openness, curiosity), encounter variety |

### Drive Formation

Drives are not assigned -- they emerge from the intersection of personality, circumstance, and experience:

**Personality-innate drives**: Characters with extreme personality traits develop corresponding drives naturally. High conscientiousness + domain engagement → mastery drive. High agreeableness + social bonds → protection drive. High neuroticism + resource scarcity → survival drive. These form automatically when personality conditions are met and relevant context exists.

**Experience-catalyzed drives**: Significant events create new drives. Witnessing injustice → `seek_justice` drive. Being betrayed → `self_reliance` drive. Losing a competition → `prove_worth` drive. The experience type and intensity determine drive category and initial intensity.

**Backstory-seeded drives**: When characters are created with backstory elements, drives can be seeded from them. `GOAL: restore_guild_honor` → `honor_legacy` drive. `TRAUMA: lost_family` → `protect_family` drive (inverted trauma response) or `gain_power` drive (prevent future loss).

**Circumstance-derived drives**: Current life circumstances generate drives. Poverty → `gain_wealth`. Success → `maintain_status`. Isolation → `find_community`. These recalculate as circumstances change.

### Drive Dynamics

**Satisfaction**: Increases when the character's actions or experiences align with the drive. A character with `master_craft` gains satisfaction each time they craft something successfully. Satisfaction decays slowly -- past achievements fade, creating renewed motivation.

**Frustration**: Increases when the character's drive is blocked or undermined. A character with `protect_family` gains frustration when family members are harmed. High frustration + high intensity can trigger behavioral shifts: desperation, risk-taking, abandoning other priorities.

**Intensity evolution**: Drives intensify when pursued and satisfied (positive feedback loop up to a cap). Drives diminish when consistently frustrated without resolution (the character "gives up") or when satisfied to the point of completion (the drive is fulfilled and fades). A drive that has been dormant for a long time (no satisfaction, no frustration) slowly decays toward removal.

**Drive absence as character depth**: Characters with no active drives above a minimum intensity threshold are "drifters" -- reactive rather than proactive. This is a valid character state: the layabout who does the minimum, the disillusioned veteran who lost their purpose, the content farmer who has everything they need. Drive absence is not a bug; it's a character trait that the behavior system can leverage.

### Drive-GOAP Integration

Drives modify GOAP goal priorities without replacing the existing goal system:

```yaml
# Without drives: GOAP evaluates goals purely on immediate utility
goals:
 - find_food: utility = hunger * 10
 - patrol_area: utility = duty * 5
 - practice_craft: utility = skill_gap * 3

# With drives: drive intensity adds bonus utility to aligned goals
goals:
 - find_food: utility = hunger * 10 + (drive.survival.gain_wealth ? intensity * 2 : 0)
 - patrol_area: utility = duty * 5 + (drive.social.protect_family ? intensity * 4 : 0)
 - practice_craft: utility = skill_gap * 3 + (drive.mastery.master_craft ? intensity * 8 : 0)
```

The `master_craft` drive doesn't create a quest. It makes the character CHOOSE to practice crafting more often, prioritize craft-related opportunities, and feel frustrated when unable to craft. This is the "chasing your dreams" mechanic: intrinsic motivation expressed through goal priority modulation.

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `targetType` (on FeelingEntry) | A (Entity Reference) | `$ref: EntityType` | Valid values are all Bannou entity types (Character, Location, Faction, Organization, etc.). Guardian spirits ARE Characters in the NEXIUS system realm — no special target type needed. Per T14 test 4: all valid values are Bannou entity types → use `$ref: EntityType`. |
| `targetId` (on FeelingEntry) | — | `Guid` | Entity ID of the target. For guardian spirit feelings, this is the spirit's characterId in the NEXIUS system realm. Always a proper Guid, never a sentinel value (per Implementation Tenets). |
| `axis` (on FeelingEntry) | B (Content Code) | Opaque string | Emotional axis codes ("trust", "fear", "warmth", "respect", "resentment", etc.); game-configurable per target type, extensible without schema changes |
| `driveCode` (on DriveEntry) | B (Content Code) | Opaque string | Drive type codes ("master_craft", "protect_family", "gain_wealth"); emergent from gameplay, extensible without schema changes |
| `category` (on DriveEntry) | B (Content Code) | Opaque string | Drive category codes ("mastery", "social", "survival", "identity", "exploration"); game-configurable grouping for GOAP priority mapping |
| `originType` (on DriveEntry) | C (System State) | `DriveOriginType` enum | Finite set of drive formation mechanisms: `Personality`, `Experience`, `Backstory`, `Circumstance` (PascalCase per SCHEMA-RULES). System-owned classification of how drives form. |

---

## The Aspirational Character Arc

The motivating use case, played out step by step:

```
Day 1: Kael becomes a blacksmith's apprentice
 ┌────────────────────────────────────────────────────────┐
 │ PERSONALITY: conscientiousness=0.7, openness=0.5 │
 │ BACKSTORY: origin=mining_village, occupation=apprentice│
 │ DRIVES: none (new character, no strong aspirations) │
 │ FEELINGS: warmth toward master (0.3, relationship) │
 └────────────────────────────────────────────────────────┘

Day 7: Kael forges his first blade successfully
 - Experience event: CRAFT_SUCCESS, intensity=0.6
 - Drive formation check: conscientiousness > 0.5 AND
 domain_engagement("craft") > 3 sessions
 - NEW DRIVE: master_craft (category: mastery,
 intensity: 0.3, origin: experience)
 - Feeling shift: warmth toward master +0.1 (gratitude modifier)

Day 30: Kael sees a master-crafted sword at the market
 - Experience event: WITNESSED_MASTERY, intensity=0.8
 - Drive reinforcement: master_craft intensity 0.3 → 0.5
 - Drive frustration: satisfaction low (0.1), gap between
 current skill and witnessed mastery creates frustration 0.3
 - GOAP impact: practice_craft utility bonus = 0.5 * 8 = +4.0
 Kael now CHOOSES to practice more, without any quest telling him to

Day 90: Master praises Kael's work publicly
 - Experience event: SOCIAL_RECOGNITION, intensity=0.7
 - Drive satisfaction: master_craft satisfaction 0.1 → 0.3
 - Feeling shift: respect toward master +0.15 (earned admiration)
 - NEW DRIVE: earn_respect (category: social, intensity: 0.2,
 catalyzed by the recognition experience)

Day 180: Rival apprentice steals credit for Kael's design
 - Experience event: BETRAYAL, intensity=0.6
 - Feeling shift: resentment toward rival +0.4 (shock modifier,
 high decay rate -- Kael isn't the grudge-holding type)
 - Feeling shift: trust toward rival -0.5 (shock modifier,
 slow decay rate -- trust is hard to rebuild)
 - Drive intensification: prove_worth (NEW, identity category,
 intensity: 0.4, catalyzed by betrayal)
 - Drive frustration: master_craft frustration +0.2
 (stolen credit undermines progress toward mastery)

Year 2: Kael creates his masterwork
 - Experience event: CRAFT_MASTERY, intensity=1.0
 - Drive satisfaction: master_craft satisfaction → 0.9
 - Drive intensity begins to decay (goal nearly achieved)
 - Feeling shift: warmth toward master → deep gratitude
 - Content flywheel: when Kael eventually dies, his drive
 history ("aspired to mastery, achieved it through
 perseverance despite betrayal") becomes narrative material
 for the Storyline composer
```

This entire arc emerged from system interaction. No quest chain orchestrated it. No scenario script authored "Kael should aspire to mastery." The drive formed because his personality (high conscientiousness) met his circumstance (apprentice) and his experience (successful crafting). The drive shaped his daily behavior (choosing to practice), his emotional responses (frustration at the gap, gratitude for recognition), and ultimately his life story (the masterwork).

---

## The Guardian Spirit Relationship

A character's feeling about the player/guardian spirit is the mechanical implementation of Design Principle 1 ("Characters Are Independent Entities"). A character pushed against their nature by the spirit develops resentment. A character guided well develops trust. This feeds directly into the compliance/resistance system -- the character's willingness to follow the spirit's nudges is proportional to their feelings about the spirit.

The guardian spirit dimension deserves special attention because it is the mechanical implementation of the vision's "dual-agency" system.

### How Spirit Feelings Evolve

```
Session 1: Spirit possesses a new character (Elara, personality: peaceful, creative)
 ┌────────────────────────────────────────────────────────┐
 │ disposition.guardian.trust = 0.0 (stranger) │
 │ disposition.guardian.resentment = 0.0 (no history) │
 │ disposition.guardian.familiarity = 0.0 (just met) │
 │ disposition.guardian.gratitude = 0.0 (nothing yet) │
 │ disposition.guardian.defiance = 0.0 (no reason) │
 └────────────────────────────────────────────────────────┘

Sessions 2-5: Spirit guides Elara toward crafting (aligned with creativity)
 - Spirit alignment signal: POSITIVE (creative character → creative pursuits)
 - trust: 0.0 → 0.2 (the spirit's guidance feels right)
 - familiarity: 0.0 → 0.15 (accumulating shared experience)
 - gratitude: 0.0 → 0.1 (good outcomes from guidance)
 - Compliance: HIGH (character trusts the spirit's direction)

Sessions 6-8: Spirit pushes Elara into a bar fight (against personality)
 - Spirit alignment signal: NEGATIVE (peaceful character → violence)
 - resentment: 0.0 → 0.3 (SHOCK modifier -- being forced into violence)
 - trust: 0.2 → 0.1 (the spirit's guidance feels wrong)
 - defiance: 0.0 → 0.15 (beginning to resist)
 - Compliance: REDUCED (character hesitates before following)

Sessions 9-10: Spirit guides Elara to help an injured traveler (aligned)
 - Spirit alignment signal: POSITIVE (peaceful character → helping)
 - trust: 0.1 → 0.2 (recovering, but scar tissue from the fight)
 - resentment: 0.3 → 0.25 (modifier decay -- time heals)
 - gratitude: 0.1 → 0.15 (good outcome again)
 - Compliance: MODERATE (trust rebuilding, but slower than initial)
 - Note: resentment modifier decays slowly -- the memory of
 being forced to fight lingers even as trust rebuilds

Long-term: Consistent aligned guidance
 - familiarity: → 0.6+ (deep bond)
 - trust: → 0.5+ (earned through consistent alignment)
 - resentment: → 0.0 (fully decayed if not reinforced)
 - Compliance: HIGH (strong collaborative relationship)
 - The character WANTS to follow the spirit's guidance because
 it consistently leads to outcomes aligned with who they are
```

### Guilt as Composite Feeling (GH #410)

[GH #410](https://github.com/beyond-immersion/bannou-service/issues/410) asks whether guilt should be a new emotion dimension or a composite. **Disposition models guilt as a composite feeling, not a separate axis.** When a character commits a knowing violation (reported by lib-obligation's `report-violation` endpoint), the violation handler applies modifier shifts across existing axes:

- **resentment** toward self (targetType: Character, targetId: own characterId): positive shift proportional to violation severity
- **trust** toward self: negative shift (self-doubt from broken oath)
- **respect** toward the wronged party (if applicable): positive shift (recognizing what was violated)
- **fear** toward the norm-enforcing faction (if applicable): positive shift (fear of consequences)

The composite feeling pattern means guilt is detectable in ABML via:
```yaml
- when: "${disposition.character.SELF.resentment > 0.4 && disposition.character.SELF.trust < -0.2}"
 then:
 - call: seek_redemption # Guilt-driven behavior
```

This avoids adding a dedicated "guilt" axis while still providing mechanically distinct guilty behavior. The `obligation.violation.reported` consumed event triggers these modifier shifts (see Consumed Events). Personality traits modulate the magnitude: high conscientiousness → stronger guilt modifiers, low conscientiousness → weaker.

### Spirit Alignment Computation

The spirit alignment signal is computed by comparing the spirit's recent nudge directions (tracked by the Actor service as input events) against the character's personality profile:

```
alignmentScore = 0.0
for each recentNudge in last N spirit actions:
 nudgeCategory = categorize(nudge) # "violence", "social", "craft", "trade", etc.
 personalityMatch = personalityAffinityForCategory(character, nudgeCategory)
 outcome = nudge.outcome # "positive", "negative", "neutral"
 alignmentScore += personalityMatch * outcomeWeight(outcome)
alignmentScore /= N
```

A nudge toward violence for a peaceful character has negative `personalityMatch`. If it also leads to a negative outcome (character gets hurt), the alignment score is doubly negative. If the spirit consistently pushes aligned actions with positive outcomes, alignment is strongly positive.

---

## Storyline Integration: Feelings and Drives as Narrative Material

Disposition provides the Storyline system with three powerful capabilities beyond what Hearsay offers:

### 1. Emotional State as Scenario Precondition

Feelings create preconditions for emotionally-driven scenarios:

```yaml
# Scenario: "The Reckoning"
conditions:
 characterRequirements:
 - archetype: protagonist
 constraints:
 disposition:
 # Character must harbor deep resentment toward someone
 feeling:
 targetType: "character"
 axis: "resentment"
 minValue: 0.6
 # AND have a drive for justice or redemption
 drive:
 category: "identity"
 codes: ["seek_justice", "redeem_past", "prove_worth"]
 minIntensity: 0.4
```

### 2. Drive Fulfillment as Narrative Arc Resolution

The Storyline composer can identify characters whose drives are near fulfillment or near abandonment -- both are dramatically potent:

```yaml
# Regional Watcher query:
# "Find characters at emotional turning points"

results:
 - Kael: master_craft drive at satisfaction 0.85
 → Near-fulfillment scenario: "The Final Test"
 → Will he achieve mastery or fall at the last hurdle?

 - Elara: protect_family drive at frustration 0.9
 → Near-abandonment scenario: "The Breaking Point"
 → Will she give up or find a new way?

 - Aldric: no active drives (intensity all < 0.1)
 → Catalyst scenario: "The Call to Adventure"
 → Something must shake him out of complacency
```

### 3. Feeling Archives in the Content Flywheel

When characters are compressed, their feeling and drive history becomes narrative material:

```yaml
character_archive_enrichment:
 # Existing: personality, history, encounters
 character_personality: { honesty: 0.8, courage: -0.3 }
 character_history: { trauma: "lost_family", goals: ["find_truth"] }

 # NEW from disposition: how they FELT and what they ASPIRED TO
 disposition_feelings:
 - target: "guild_master"
 axis: "gratitude"
 value: 0.9
 narrative_weight: HIGH # Deep emotional bond
 - target: "guardian"
 axis: "trust"
 value: 0.7
 narrative_weight: MEDIUM # Strong spirit-character bond

 disposition_drives:
 - drive: "master_craft"
 final_satisfaction: 0.9
 final_intensity: 0.2 # Decaying -- nearly fulfilled
 narrative_weight: HIGH # Life's work achieved
 - drive: "prove_worth"
 final_satisfaction: 0.3
 final_frustration: 0.7
 narrative_weight: HIGH # Unfinished business -- story seed!
```

"This character died with an unfulfilled drive to prove their worth" is a tragedy seed. "This character achieved mastery through decades of dedication" is an inspiration seed. The Storyline composer's GOAP planner can use drive fulfillment/frustration as preconditions for narrative actions.

### 4. Hearsay-Disposition Bridge

[Hearsay](HEARSAY.md) provides what a character has HEARD. Disposition provides how they FEEL about it. The bridge between them creates the dramatic irony → emotional response pipeline:

```
Hearsay: Kael heard Mira is dangerous (confidence 0.2)
Disposition: Kael feels mild wariness toward Mira (fear: 0.1)

Hearsay: Kael now meets Mira, she heals him
Encounter: positive encounter recorded
Hearsay: "dangerous" belief CORRECTED (confidence drops)
Disposition: warmth toward Mira RISES (encounter + correction)
 fear toward Mira DROPS (direct experience overrides)

Hearsay: Kael hears Mira killed someone (confidence 0.5)
Disposition: fear RISES AGAIN (hearsay signal)
 trust DROPS (conflicting signals)
 But modifier from positive encounter persists --
 Kael's personal experience resists the rumor

This is how the same external information produces different
feelings in different characters. A paranoid character would
weight the murder rumor higher. A trusting character would
weight their personal experience higher.
```

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `DispositionProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates disposition provider instances per character for ABML behavior execution (`${disposition.*}` variables) |
| lib-storyline (L4, planned) | Storyline can query disposition for emotional state preconditions (feeling thresholds) and drive-based scenario matching (drive categories, intensity, satisfaction) |
| lib-puppetmaster (L4, planned) | Regional watcher god actors query disposition for characters at emotional turning points (near drive fulfillment, high frustration, strong feelings about other entities) |
| lib-gardener (L4, planned) | Player experience orchestration can use disposition to surface "how your character feels" as a player-facing emotional state indicator |
| lib-agency (L4) | Reads guardian feeling axes (trust, resentment, familiarity) for compliance computation in the spirit influence pipeline; records feeling updates after influence outcomes |
| lib-director (L4, planned) | Drive session checkpoints may read/write drive state when a developer takes and releases control of an actor |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `CacheTtlMinutes` | `DISPOSITION_CACHE_TTL_MINUTES` | `5` | TTL for cached disposition manifests per character (range: 1-60) |
| `MaxFeelingsPerCharacter` | `DISPOSITION_MAX_FEELINGS_PER_CHARACTER` | `200` | Safety limit on total feelings per character across all targets (range: 50-1000) |
| `MaxDrivesPerCharacter` | `DISPOSITION_MAX_DRIVES_PER_CHARACTER` | `10` | Maximum concurrent active drives per character (range: 3-20) |
| `ModifierDecayIntervalMinutes` | `DISPOSITION_MODIFIER_DECAY_INTERVAL_MINUTES` | `60` | How often the modifier decay worker runs (range: 15-1440) |
| `ModifierDecayBatchSize` | `DISPOSITION_MODIFIER_DECAY_BATCH_SIZE` | `100` | Feelings per batch in decay worker (range: 10-500) |
| `SynthesisIntervalMinutes` | `DISPOSITION_SYNTHESIS_INTERVAL_MINUTES` | `30` | How often the synthesis worker resynthesizes base values (range: 10-120) |
| `SynthesisBatchSize` | `DISPOSITION_SYNTHESIS_BATCH_SIZE` | `50` | Characters per batch in synthesis worker (range: 10-200) |
| `DriveEvaluationIntervalMinutes` | `DISPOSITION_DRIVE_EVALUATION_INTERVAL_MINUTES` | `60` | How often drives are evaluated for formation, evolution, and removal (range: 15-240) |
| `DriveMinIntensityThreshold` | `DISPOSITION_DRIVE_MIN_INTENSITY_THRESHOLD` | `0.05` | Drives below this intensity are removed (range: 0.01-0.2) |
| `DriveFulfillmentThreshold` | `DISPOSITION_DRIVE_FULFILLMENT_THRESHOLD` | `0.9` | Satisfaction level at which a drive is considered fulfilled (range: 0.7-1.0) |
| `DriveSatisfactionDecayRatePerDay` | `DISPOSITION_DRIVE_SATISFACTION_DECAY_RATE_PER_DAY` | `0.01` | How fast satisfaction decays without reinforcement (range: 0.001-0.05) |
| `FeelingChangeThreshold` | `DISPOSITION_FEELING_CHANGE_THRESHOLD` | `0.05` | Minimum effective value change to publish feeling.changed event (range: 0.01-0.2) |
| `ShockModifierMagnitude` | `DISPOSITION_SHOCK_MODIFIER_MAGNITUDE` | `0.4` | Default modifier shift for shock events (range: 0.1-0.8) |
| `ShockModifierDecayRate` | `DISPOSITION_SHOCK_MODIFIER_DECAY_RATE` | `0.02` | Decay rate per day for shock-induced modifiers (range: 0.005-0.1) |
| `DefaultModifierDecayRate` | `DISPOSITION_DEFAULT_MODIFIER_DECAY_RATE` | `0.03` | Default decay rate per day for non-shock modifiers (range: 0.005-0.1) |
| `EncounterWeightDefault` | `DISPOSITION_ENCOUNTER_WEIGHT_DEFAULT` | `0.35` | Default weight for encounter component in base synthesis (range: 0.0-1.0) |
| `HearsayWeightDefault` | `DISPOSITION_HEARSAY_WEIGHT_DEFAULT` | `0.25` | Default weight for hearsay component in base synthesis (range: 0.0-1.0) |
| `RelationshipWeightDefault` | `DISPOSITION_RELATIONSHIP_WEIGHT_DEFAULT` | `0.25` | Default weight for relationship component in base synthesis (range: 0.0-1.0) |
| `PersonalityWeightDefault` | `DISPOSITION_PERSONALITY_WEIGHT_DEFAULT` | `0.15` | Default weight for personality bias component in base synthesis (range: 0.0-1.0) |
| `GuardianAlignmentWindowSize` | `DISPOSITION_GUARDIAN_ALIGNMENT_WINDOW_SIZE` | `20` | Number of recent spirit nudges to consider for alignment computation (range: 5-50) |
| `DistributedLockTimeoutSeconds` | `DISPOSITION_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition (range: 5-120) |
| `QueryPageSize` | `DISPOSITION_QUERY_PAGE_SIZE` | `20` | Default page size for paged queries (range: 1-100) |
| `MinConfidenceForProvider` | `DISPOSITION_MIN_CONFIDENCE_FOR_PROVIDER` | `0.05` | Feelings with effective value below this magnitude are excluded from variable provider (range: 0.01-0.3) |

---

## Variable Provider: `${disposition.*}` Namespace

Implements `IVariableProviderFactory` (via `DispositionProviderFactory`) providing the following variables to Actor (L2) via the Variable Provider Factory pattern. Loads from the cached disposition manifest.

### Feelings

| Variable | Type | Description |
|----------|------|-------------|
| `${disposition.character.<id>.trust}` | float | How much the character trusts a specific character (-1.0 to +1.0) |
| `${disposition.character.<id>.fear}` | float | How much the character fears a specific character |
| `${disposition.character.<id>.warmth}` | float | How warmly the character feels toward another |
| `${disposition.character.<id>.respect}` | float | How much the character respects another |
| `${disposition.character.<id>.resentment}` | float | Resentment toward a specific character |
| `${disposition.character.<id>.<axis>}` | float | Any feeling axis (opaque string extensibility) |
| `${disposition.character.<id>.composite}` | float | Weighted aggregate across all axes for this target. Positive = favorable, negative = hostile. |
| `${disposition.character.<id>.has_feelings}` | bool | Whether any feelings exist about this character |
| `${disposition.location.<id>.comfort}` | float | Comfort level at a location |
| `${disposition.location.<id>.dread}` | float | Dread of a location |
| `${disposition.location.<id>.<axis>}` | float | Any location feeling axis |
| `${disposition.faction.<id>.loyalty}` | float | Loyalty toward a faction |
| `${disposition.faction.<id>.<axis>}` | float | Any faction feeling axis |
| `${disposition.guardian.trust}` | float | How much the character trusts the guardian spirit |
| `${disposition.guardian.resentment}` | float | Resentment toward the guardian spirit |
| `${disposition.guardian.familiarity}` | float | How well the character knows the spirit |
| `${disposition.guardian.defiance}` | float | Active defiance of the spirit's guidance |
| `${disposition.guardian.<axis>}` | float | Any guardian feeling axis |
| `${disposition.guardian.compliance}` | float | Computed compliance multiplier (0.0-1.0). Combines trust, resentment, familiarity, defiance into a single "how responsive is this character to spirit input" value. |

### Drives

| Variable | Type | Description |
|----------|------|-------------|
| `${disposition.drive.active_count}` | int | Number of active drives |
| `${disposition.drive.has_drive.<code>}` | bool | Whether the character has this specific drive |
| `${disposition.drive.<code>.intensity}` | float | Intensity of a specific drive (0.0-1.0) |
| `${disposition.drive.<code>.satisfaction}` | float | How satisfied the drive is (0.0-1.0) |
| `${disposition.drive.<code>.frustration}` | float | How frustrated the drive is (0.0-1.0) |
| `${disposition.drive.<code>.urgency}` | float | Computed: `intensity * (1.0 - satisfaction) * (1.0 + frustration)`. High urgency = intense, unsatisfied, frustrated drive. |
| `${disposition.drive.strongest}` | string | Drive code with highest urgency |
| `${disposition.drive.most_frustrated}` | string | Drive code with highest frustration |
| `${disposition.drive.most_satisfied}` | string | Drive code with highest satisfaction |

### Meta Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${disposition.emotional_volatility}` | float | How rapidly feelings are changing (rolling average of recent modifier shifts). High volatility = emotionally turbulent period. |
| `${disposition.strongest_positive}` | string | Target ID with highest composite positive feeling |
| `${disposition.strongest_negative}` | string | Target ID with highest composite negative feeling |
| `${disposition.drive_alignment}` | float | How aligned the character's current activities are with their drives (-1.0 to +1.0). Positive = pursuing their dreams. Negative = trapped in unfulfilling circumstances. |

### ABML Usage Examples

```yaml
flows:
 evaluate_social_interaction:
 # Should I approach this person?
 - cond:
 # I have strong positive feelings about them
 - when: "${disposition.character.TARGET.composite > 0.4}"
 then:
 - call: approach_warmly
 - set:
 dialogue_tone: "friendly"

 # I resent them deeply
 - when: "${disposition.character.TARGET.resentment > 0.6}"
 then:
 - cond:
 # But I'm not confrontational by nature
 - when: "${personality.aggression < 0.3}"
 then:
 - call: avoid_interaction
 - otherwise:
 - call: confront

 # I've never met them but have heard things (hearsay + disposition)
 - when: "${disposition.character.TARGET.has_feelings
 && !encounters.has_met.TARGET}"
 then:
 - call: approach_cautiously
 - set:
 dialogue_tone: "guarded"

 # No information at all
 - otherwise:
 - cond:
 - when: "${personality.agreeableness > 0.3}"
 then:
 - call: approach_neutrally
 - otherwise:
 - call: ignore

 pursue_drive:
 # Do I practice my craft today?
 - cond:
 # My master_craft drive is urgent
 - when: "${disposition.drive.has_drive.master_craft
 && disposition.drive.master_craft.urgency > 0.5}"
 then:
 # Drive urgency overrides other low-priority goals
 - call: practice_crafting
 - set:
 activity_reason: "driven_by_aspiration"

 # My drive is satisfied -- I can relax
 - when: "${disposition.drive.has_drive.master_craft
 && disposition.drive.master_craft.satisfaction > 0.8}"
 then:
 - call: do_something_else
 - set:
 mood: "content"

 # No craft drive -- do it only if assigned
 - otherwise:
 - call: check_work_obligations

 respond_to_spirit:
 # How do I respond to the guardian spirit's nudge?
 - cond:
 # High trust, low resentment -- happy to comply
 - when: "${disposition.guardian.compliance > 0.7}"
 then:
 - call: follow_spirit_guidance
 - set:
 response_delay: "${0.1}" # Quick response

 # Moderate compliance -- hesitate but follow
 - when: "${disposition.guardian.compliance > 0.3}"
 then:
 - call: hesitate_then_follow
 - set:
 response_delay: "${1.0 - disposition.guardian.compliance}"

 # Low compliance -- resist
 - when: "${disposition.guardian.compliance < 0.3
 && disposition.guardian.defiance > 0.5}"
 then:
 - call: resist_spirit_nudge
 - emit:
 channel: "spirit_resistance"
 value: "character_refused"

 # Very low compliance -- act autonomously
 - otherwise:
 - call: act_on_own_judgment
 - set:
 autonomy_mode: true

 layabout_behavior:
 # Character with no drives -- reactive, minimal effort
 - cond:
 - when: "${disposition.drive.active_count == 0}"
 then:
 - cond:
 # Not even hungry? Do nothing.
 - when: "${hunger < 0.3 && fatigue < 0.5}"
 then:
 - call: idle_lounge
 # Basic survival only
 - otherwise:
 - call: meet_basic_needs
```

---

## Visual Aid

Disposition does not replace any existing service. Encounter sentiment, personality traits, hearsay beliefs, and relationship bonds continue to provide their own raw variable namespaces. Disposition synthesizes them into a higher-level emotional state that behavior authors can use when they want characters to "feel" rather than "compute." When lib-disposition is disabled, NPCs fall back to raw data composition in ABML expressions -- existing systems work unchanged.

### Feeling Synthesis Pipeline

```
┌──────────────────────────────────────────────────────────────────────┐
│ FEELING SYNTHESIS PIPELINE │
│ │
│ SOURCE DATA: │
│ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ │
│ │Encounter │ │ Hearsay │ │Relation- │ │Personal- │ │
│ │Sentiment │ │ Beliefs │ │ ship │ │ ity │ │
│ │(factual) │ │ (heard) │ │ (bonds) │ │ (self) │ │
│ └────┬─────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘ │
│ │ w=0.35 │ w=0.25 │ w=0.25 │ w=0.15 │
│ └──────────────┼─────────────┼─────────────┘ │
│ ▼ │
│ ┌───────────────┐ │
│ │ BASE VALUE │ ← Weighted sum of source signals │
│ │ (computed) │ Recomputed periodically or on event │
│ └───────┬───────┘ │
│ │ │
│ │ + ┌───────────────┐ │
│ │ │ MODIFIER │ ← Persistent emotional │
│ │ │ (residue) │ residue from events. │
│ │ │ │ Decays toward 0 over │
│ │ │ Reinforced │ time unless reinforced. │
│ │ │ by events │ │
│ │ └───────┬───────┘ │
│ │ │ │
│ ▼ ▼ │
│ ┌───────────────────────────┐ │
│ │ EFFECTIVE VALUE │ │
│ │ clamp(base + modifier, │ │
│ │ -1.0, 1.0) │ ──────► ${disposition.*} │
│ └───────────────────────────┘ Variable Provider │
│ │
│ CHARACTER A (aggressive, paranoid): │
│ encounter(neutral) + personality(distrust) = base: -0.2 │
│ + modifier(betrayal shock): -0.4 │
│ = effective: -0.6 (distrustful) │
│ │
│ CHARACTER B (agreeable, trusting): │
│ encounter(neutral) + personality(trust) = base: +0.2 │
│ + modifier(none): 0.0 │
│ = effective: +0.2 (mildly trusting) │
│ │
│ Same encounter data → different feelings. That's the point. │
└──────────────────────────────────────────────────────────────────────┘
```

### Drive Lifecycle

```
┌──────────────────────────────────────────────────────────────────────┐
│ DRIVE LIFECYCLE │
│ │
│ FORMATION │
│ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ │
│ │Personality│ │Experience│ │Backstory │ │Circum- │ │
│ │ -innate │ │-catalyzed│ │ -seeded │ │ stance │ │
│ │ (traits │ │ (event │ │ (GOAL or │ │ (poverty,│ │
│ │ + domain│ │ triggers│ │ TRAUMA │ │ success,│ │
│ │ engage) │ │ new aim)│ │ → drive)│ │ etc.) │ │
│ └────┬─────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘ │
│ └──────────────┼─────────────┼─────────────┘ │
│ ▼ │
│ ┌─────────────┐ │
│ │ NEW DRIVE │ intensity: 0.2-0.5 │
│ │ satisfaction: 0.0 │
│ │ frustration: 0.0 │
│ └──────┬──────┘ │
│ │ │
│ ┌──────────────┼──────────────┐ │
│ ▼ ▼ ▼ │
│ SATISFACTION FRUSTRATION DORMANCY │
│ ┌─────────┐ ┌─────────┐ ┌─────────┐ │
│ │Progress │ │Blocked │ │No events│ │
│ │toward │ │undermined│ │neither │ │
│ │the goal │ │can't act│ │progress │ │
│ │ │ │ │ │nor block│ │
│ │sat: ↑ │ │frus: ↑ │ │int: ↓ │ │
│ │int: ↑ │ │int: ↑↑ │ │ │ │
│ └────┬────┘ └────┬────┘ └────┬────┘ │
│ │ │ │ │
│ ▼ ▼ ▼ │
│ ┌─────────┐ ┌─────────┐ ┌─────────┐ │
│ │FULFILLED│ │ABANDONED │ │ REMOVED │ │
│ │sat > 0.9│ │frus > 0.9│ │int < 0.05│ │
│ │int → ↓ │ │int → ↓↓ │ │ │ │
│ │(mission │ │(gave up) │ │(forgot) │ │
│ │ complete)│ │ │ │ │ │
│ └─────────┘ └─────────┘ └─────────┘ │
│ │
│ GOAP INTEGRATION: │
│ ┌──────────────────────────────────────────────┐ │
│ │ urgency = intensity * (1 - sat) * (1 + frus) │ │
│ │ │ │
│ │ High urgency → GOAP goal priority bonus │ │
│ │ Character CHOOSES to pursue their dream │ │
│ │ No quest needed. No script. Just motivation. │ │
│ └──────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

### Guardian Spirit Relationship

```
┌──────────────────────────────────────────────────────────────────────┐
│ GUARDIAN SPIRIT RELATIONSHIP │
│ │
│ SPIRIT ACTIONS CHARACTER RESPONSE │
│ ┌─────────────┐ │
│ │ Spirit nudge │───────┐ │
│ │ "go fight" │ │ │
│ └─────────────┘ │ │
│ ▼ │
│ ┌─────────────────┐ │
│ │ ALIGNMENT CHECK │ │
│ │ │ │
│ │ personality: │ │
│ │ peaceful=0.8 │ │
│ │ │ │
│ │ nudge category: │ │
│ │ violence │ │
│ │ │ │
│ │ alignment: │ │
│ │ NEGATIVE │ │
│ └────────┬────────┘ │
│ │ │
│ ▼ │
│ ┌─────────────────┐ │
│ │ FEELING UPDATE │ │
│ │ │ │
│ │ trust: -0.05 │ │
│ │ resentment: +0.1│ │
│ │ defiance: +0.05 │ │
│ └────────┬────────┘ │
│ │ │
│ ▼ │
│ ┌─────────────────┐ │
│ │ COMPLIANCE │ │
│ │ COMPUTATION │ │
│ │ │ │
│ │ compliance = │ │
│ │ trust * 0.4 │ │
│ │ + fam * 0.2 │ │
│ │ - resent * 0.3 │ │
│ │ - defiance * 0.1│ │
│ │ │ │
│ │ = 0.35 (LOW) │ │
│ └────────┬────────┘ │
│ │ │
│ ▼ │
│ ┌─────────────────┐ │
│ │ BEHAVIOR RESULT │ │
│ │ │ │
│ │ compliance 0.35:│ │
│ │ → HESITATE │ │
│ │ → 65% chance │ │
│ │ to resist │ │
│ │ → Visible delay │ │
│ │ before acting │ │
│ └─────────────────┘ │
│ │
│ OVER TIME: │
│ ┌──────────────────────────────────────────────┐ │
│ │ Aligned guidance → trust ↑, resentment ↓ │ │
│ │ Misaligned guidance → trust ↓, resentment ↑ │ │
│ │ Good outcomes → gratitude ↑, trust ↑ │ │
│ │ Bad outcomes → trust ↓ │ │
│ │ Consistent alignment → familiarity ↑ │ │
│ │ Prolonged misalignment → defiance ↑ │ │
│ │ │ │
│ │ The player must EARN the character's trust. │ │
│ │ This IS the dual-agency training system. │ │
│ └──────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no code. The following phases are planned:

### Phase 0: Prerequisites (changes to existing services)

- **Hearsay service**: Disposition's full synthesis pipeline benefits greatly from Hearsay providing belief data. Without Hearsay, the synthesis pipeline still works (encounters + relationships + personality) but is less rich. Hearsay is not a blocker but is a significant enhancer.
- **Actor spirit input tracking**: The Actor service needs to track spirit nudge events (what the spirit asked the character to do, what category, what outcome) for guardian alignment computation. This may require a small extension to the Actor service.

### Phase 1: Core Feeling Infrastructure

- Create `disposition-api.yaml` schema with feeling management endpoints
- Create `disposition-service-events.yaml` schema
- Create `disposition-configuration.yaml` schema
- Generate service code
- Implement feeling CRUD (record, record-shock, query, get-manifest, query-about)
- Implement base value synthesis from available sources
- Implement modifier mechanics (reinforcement, decay)
- Implement variable provider factory (`${disposition.*}` feeling namespace)
- Implement resource cleanup and compression callbacks
- Implement feeling cache with event-driven invalidation

### Phase 2: Drive System

- Implement drive model and storage
- Implement drive formation logic (personality-innate, experience-catalyzed, backstory-seeded, circumstance-derived)
- Implement drive dynamics (satisfaction, frustration, intensity evolution)
- Implement drive evaluation background worker
- Implement drive variable provider (`${disposition.drive.*}` namespace)
- Implement drive API endpoints (seed, query, record-progress, record-frustration, find-by-drive)

### Phase 3: Guardian Spirit Integration

- Implement guardian feeling axes (trust, resentment, familiarity, gratitude, defiance)
- Implement spirit alignment computation
- Implement compliance computation
- Implement spirit action recording endpoint
- Implement guardian variable provider (`${disposition.guardian.*}` namespace)
- Wire into Actor service for spirit input processing

### Phase 4: Event-Driven Synthesis

- Implement encounter event handlers (feeling updates on new encounters)
- Implement personality evolution handlers (resynthesis on personality change)
- Implement relationship event handlers (seeding feelings from new relationships)
- Implement obligation violation handlers (guilt, resentment from violations)
- Implement backstory event handlers (drive formation from backstory elements)
- Implement hearsay event handlers (feeling resynthesis on new beliefs)

### Phase 5: Storyline Integration

- Implement feeling-based scenario preconditions
- Implement drive-based scenario matching
- Implement narrative state enrichment (feeling archives + drive archives)
- Wire disposition data into Storyline's NarrativeState extraction

### Phase 6: Background Workers

- Implement modifier decay worker
- Implement periodic synthesis worker
- Implement drive evaluation worker

---

## Potential Extensions

1. **Feeling contagion**: Characters in proximity to someone experiencing strong emotions may develop sympathetic feelings. An NPC witnessing their friend's grief feels sadness. A crowd's anger can sweep up individual NPCs. This creates emergent mob dynamics and emotional communities.

2. **Drive inheritance**: When characters have children (generational play), some drives can be inherited at reduced intensity. The child of a master blacksmith may have a latent `master_craft` drive that activates if they ever pick up a hammer. This connects disposition to the generational cycle and content flywheel.

3. **Feeling visualization for Gardener**: The Gardener service could use disposition data to show players a "feelings compass" -- who their character likes, fears, resents, and admires, with visual intensity. This makes the character's inner life tangible to the player.

4. **Drive conflict system**: When two active drives conflict (e.g., `gain_wealth` vs. `protect_family` when wealth requires dangerous work), the character experiences internal tension. This tension could manifest as increased emotional volatility, decision paralysis (GOAP cost ties), or personality evolution (the resolution shapes who they become).

5. **Collective disposition**: Factions or organizations could have aggregate dispositions derived from their members' feelings. A guild where most members resent the guild leader has collective resentment that could trigger faction events (coups, splits, reforms).

6. **Disposition as Obligation input**: An optional integration where `${obligations.violation_cost.*}` is modulated by the character's feelings about the norm-enforcing faction. A character who resents a faction feels lower obligation costs for violating their norms. This bridges the cold cost system with emotional reasoning.

7. **Spirit communication channel**: Guardian spirit feelings could feed into the pair system's communication channel. A character with high trust toward the spirit "hears" spirit guidance more clearly (richer communication). A character with high defiance "blocks out" the spirit (reduced communication fidelity).

8. **Drive-based NPC archetypes**: Pre-configured drive packages that create recognizable character archetypes. "The Ambitious Merchant" has `gain_wealth` + `earn_respect`. "The Reluctant Hero" has `protect_family` but no `prove_worth`. These are starting configurations, not fixed -- drives evolve from there.

9. **Emotional memory for content flywheel**: Feeling archives from compressed characters become narrative material. "This character died with deep resentment toward the god who abandoned them" is a tragedy seed. "This character's trust in their guardian spirit never wavered despite hardship" is a heroism seed. The delta between drive aspiration and final satisfaction enriches narrative state extraction.

10. **Cross-service feeling triggers**: Allow any service to trigger feeling updates via a standardized event format. When a character completes a dramatic quest (Quest), their feelings toward the quest giver shift. When they join a faction (Faction), initial feelings are seeded. This makes feelings responsive to all game events, not just encounters.

---

## Why Not Extend Existing Services?

The question arises: why not add feelings to Character-Encounter or drives to Character-History?

**Character-Encounter handles factual records, not subjective interpretation.** Encounter answers "what happened between us?" Adding feelings conflates two concerns: "what occurred" and "how I feel about it." The same encounter produces different feelings for different characters based on personality. Encounter should remain the factual record; Disposition should be the emotional interpretation.

**Character-Personality handles self-model, not directed emotions.** Personality answers "what kind of person am I?" Adding directed feelings (toward specific entities) would turn a self-model into a relational model, fundamentally changing Personality's API. Personality should remain the static-ish character traits; Disposition handles how those traits color perception of specific entities.

**Character-History handles backstory, not aspirations.** History answers "what happened in my past?" Drives are forward-looking: "what do I aspire to become?" Adding drives to History conflates past and future. History should remain the record of what was; Disposition handles what the character wants to become.

**Quest handles externally-assigned objectives, not intrinsic motivation.** Quest answers "what have I been asked to do?" Drives answer "what do I WANT to do?" A character might have a `master_craft` drive without any quest. A quest to deliver a package doesn't require a drive. Quests are external obligations; drives are internal desires.

| Service | Question It Answers | Domain |
|---------|--------------------|--------------------|
| **Encounter** | "What happened between us?" | Factual interaction records |
| **Personality** | "What kind of person am I?" | Self-model (undirected traits) |
| **History** | "What happened in my past?" | Biographical record |
| **Hearsay** | "What have I heard about X?" | Information state (belief) |
| **Quest** | "What have I been asked to do?" | External objectives |
| **Disposition** | "How do I feel about X?" + "What do I aspire to?" | Emotional synthesis + intrinsic motivation |

The pipeline with disposition:
```
Encounter (what HAPPENED) ─┐
Hearsay (what I've HEARD) ─┤
Personality (who I AM) ─┤──► DISPOSITION ──► ${disposition.*} ──► Actor
Relationship (formal BONDS) ─┤ (how I FEEL) ABML
History (my PAST) ─┤ (what I WANT) GOAP
 │
 └──► DRIVES ──► GOAP goal priority modulation
 (what I ASPIRE to)
```

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified. Plugin is pre-implementation.*

### Intentional Quirks (Documented Behavior)

1. **Feeling axes are opaque strings**: Not enums. Follows the same extensibility pattern as violation type codes, collection type codes, seed type codes, and hearsay claim codes. `trust`, `fear`, `warmth`, `resentment` are conventions, not constraints. Games define their own emotional vocabulary.

2. **Drive codes are opaque strings**: Same extensibility pattern. `master_craft`, `protect_family`, `gain_wealth` are conventions. Games define their own aspiration vocabulary.

3. **Feelings persist after relationship ends**: When a relationship is deleted (hard delete per Foundation Tenets), the feelings toward that entity remain. The relationship component of the base value drops to 0, but the modifier persists. Emotional residue outlasts formal bonds.

4. **Guardian feelings are per-character, not per-account**: Each character has an independent emotional relationship with the guardian spirit. A spirit's second character starts with zero familiarity even though the spirit has experience from the first character. The spirit knows the character; the character doesn't know the spirit.

5. **Drives can be seeded directly**: The `SeedDrive` endpoint allows direct drive creation (for backstory seeding during character creation, divine intervention, or experience-catalyzed formation). This bypasses the organic formation pipeline. The endpoint uses `x-permissions: []` (service-to-service only — called by character creation handlers and event handlers).

6. **Compression restores modifiers at 50% strength**: When a compressed character is restored, their feeling modifiers are halved. This simulates "emotional distance" -- after a long absence, feelings are muted but not erased. Base values are resynthesized from current data (the world may have changed).

7. **Drive absence is valid**: A character with no active drives (`active_count == 0`) is not broken. They are reactive and minimally motivated. This is a character trait, not a system failure.

8. **Multiple feelings about the same target coexist**: A character can feel `trust: 0.7` AND `fear: 0.3` toward the same character simultaneously. These are not contradictory -- they represent different emotional dimensions. "I trust them but they also scare me" is a coherent emotional state.

9. **Synthesis weights redistribute proportionally**: The four source weights (encounter, hearsay, relationship, personality) are configured independently. If a source is unavailable, its weight redistributes proportionally to remaining sources (`new_w_i = w_i / (1 - w_unavailable)`). Sum validation is enforced at runtime startup (decided 2026-03-16); OpenAPI's `x-constraint-groups` with `sum-equals` constraint will express this in the configuration schema when implemented.

10. **Guardian compliance is a derived value, not a feeling**: `${disposition.guardian.compliance}` is computed from the feeling axes (trust, resentment, familiarity, defiance) using a weighted formula. It is not stored independently. This ensures compliance always reflects current emotional state.

### Design Considerations (Requires Planning)

1. ~~**Scale considerations**~~: **RESOLVED** (2026-03-17) — Solvable with existing patterns, not a design blocker. Three mechanisms: (a) staleness-based synthesis — SynthesisWorker only resynthesizes characters whose `lastSynthesizedAt` exceeds a configurable threshold AND who have had relevant events since last synthesis, (b) realm-scoped worker processing — each node's worker processes only characters in its realm assignment, co-located with Actor pool and L4 data services for in-process DI resolution (no network calls for synthesis source queries), (c) manifest cache as the hot-path lever — Actor reads `${disposition.*}` from Redis manifest cache (O(1)), expensive synthesis runs rarely. Add `SynthesisStalenessThresholdMinutes` and `MaxConcurrencyRetries` config properties. See DEPLOYMENT-MODES.md § realm-to-node affinity for the co-location pattern. Close GH #616.

2. ~~**Guardian spirit input tracking**~~: **RESOLVED** (2026-03-17) — Actor (caller) passes `guardianCharacterId` in the `RecordSpiritAction` request. Actor already knows the guardian spirit's characterId from the actor-character binding context. Spirit nudge history stored as an embedded `recentNudges` list on guardian feeling entries (same store, same read — always accessed together). Rolling window trimmed to `GuardianAlignmentWindowSize` on each write. Close GH #621.

3. ~~**Game-time vs real-time for decay rates**~~: **RESOLVED** (2026-03-17) — Use game-time, no delay. Worldstate's `GetElapsedGameTimeAsync(fromRealTime, toRealTime, realmId)` API is fully implemented and production-ready. `IWorldstateClient` added as hard L2 dependency. All three background workers use game-time elapsed for decay/satisfaction computations. Characters always have a realm → realm context always available. No `TimeSource` config enum needed — game-time is the only correct option for a living world simulation. GH #545 (Currency/Seed migration) is the same pattern but independent of Disposition. Close GH #623.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Core feeling infrastructure (Phase 1) is self-contained and can proceed independently. Drive system (Phase 2) depends on Phase 1. Guardian spirit integration (Phase 3) depends on Phase 1 and Actor service extension for spirit input tracking. Event-driven synthesis (Phase 4) depends on Phase 1 and benefits from Hearsay (also pre-implementation). Storyline integration (Phase 5) depends on Phases 1-2 and lib-storyline's scenario system being further developed.*
