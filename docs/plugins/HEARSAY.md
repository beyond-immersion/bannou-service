# Hearsay Plugin Deep Dive

> **Plugin**: lib-hearsay
> **Schema**: schemas/hearsay-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: hearsay-beliefs (MySQL), hearsay-propagation (MySQL), hearsay-cache (Redis), hearsay-lock (Redis)
> **Status**: Pre-implementation (architectural specification)
> **Planning**: [MORALITY-SYSTEM.md](../guides/MORALITY-SYSTEM.md), [STORY-SYSTEM.md](../guides/STORY-SYSTEM.md), [CHARACTER-COMMUNICATION.md](../guides/CHARACTER-COMMUNICATION.md)

## Overview

Social information propagation and belief formation service (L4 GameFeatures) for NPC cognition. Maintains per-character **beliefs** about norms, other characters, and locations -- what an NPC *thinks* they know vs. what is objectively true. Beliefs are acquired through information channels (direct observation, official decree, social contact, rumor, cultural osmosis), carry confidence levels, converge toward reality over time, and can be intentionally manipulated by external actors (gods, propagandists, gossip networks). Game-agnostic: propagation speeds, confidence thresholds, convergence rates, and rumor injection patterns are configured through hearsay configuration and seed data at deployment time. Internal-only, never internet-facing.

---

## Core Mechanics

The current morality pipeline (Faction norms -> Obligation costs -> GOAP modifiers) is a perfect-information system. When a new faction takes over, every NPC instantly knows and internalizes all new rules. When a character commits a crime, only direct witnesses know about it. NPCs have no mechanism for forming impressions of characters they've never met, learning about distant events, or being influenced by misinformation. Real social knowledge is delayed, fuzzy, manipulable, and self-correcting. Hearsay models this.

| Domain | Subject | Example Belief | What It Modulates |
|--------|---------|---------------|-------------------|
| **Norm beliefs** | Faction norms at locations | "Theft is severely punished here" | Perceived obligation costs (parallel to `${obligations.*}`) |
| **Character beliefs** | Other characters | "Mira is dangerous and untrustworthy" | Social interaction decisions, approach/avoid, trade willingness |
| **Location beliefs** | Places | "The swamp is cursed and deadly" | Travel cost, anxiety state, avoidance behavior |

In Persona 5, rumors literally change reality. In Bannou, rumors change NPC *perception* of reality, which changes NPC behavior, which can change actual reality (self-fulfilling prophecy). A rumor that "the market is dangerous" causes NPCs to avoid it, which reduces traffic, which makes it actually more dangerous for those who remain. A god of mischief can destabilize a district by injecting false beliefs about draconian enforcement, causing merchants to flee before any enforcement actually happens.

### Belief Model

Every belief has the same structure regardless of domain:

```
BeliefEntry:
  beliefId:       Guid          # Unique identifier
  characterId:    Guid          # Who holds this belief
  domain:         string        # "norm", "character", "location"
  subjectId:      string        # What the belief is about (composite key)
  claimCode:      string        # What they believe (opaque string)
  believedValue:  float         # Magnitude (penalty, danger, trust, etc.)
  confidence:     float         # 0.0 (pure hearsay) to 1.0 (directly observed)
  valence:        float         # -1.0 (negative) to +1.0 (positive)
  sourceChannel:  string        # How they learned it
  sourceEntityId: Guid?         # Who told them (null for osmosis/observation)
  acquiredAt:     DateTime      # When they first heard it
  lastReinforcedAt: DateTime    # When confidence was last boosted
  decayRate:      float         # How fast confidence degrades (domain-specific)
```

### Subject Keys by Domain

| Domain | SubjectId Format | ClaimCode Examples | BeliefValue Meaning |
|--------|-----------------|-------------------|-------------------|
| **norm** | `{locationId}:{violationType}` | `theft`, `violence`, `contraband` | Believed penalty magnitude |
| **character** | `{targetCharacterId}` | `trustworthy`, `dangerous`, `dishonest`, `generous`, `skilled_fighter` | Impression strength (-1.0 to +1.0) |
| **location** | `{locationId}` | `dangerous`, `profitable`, `cursed`, `lawless`, `sacred` | Perceived intensity (0.0 to 1.0) |

### Information Channels

How NPCs acquire beliefs, ranked by confidence and accuracy:

| Channel | Initial Confidence | Accuracy | Speed | Trigger |
|---------|-------------------|----------|-------|---------|
| `direct_observation` | 0.85-1.0 | Exact | Instant | NPC witnesses event firsthand |
| `official_decree` | 0.7-0.8 | High | Event-driven | Sovereign faction announces change |
| `trusted_contact` | 0.5-0.7 | Good | Encounter-driven | Close relationship shares information |
| `social_contact` | 0.3-0.5 | Variable | Encounter-driven | Casual acquaintance mentions something |
| `rumor` | 0.1-0.3 | Low/manipulable | Propagation wave | Heard from friend-of-friend, injected externally |
| `cultural_osmosis` | Background | Medium | Time-based (convergence worker) | Living in an area, gradually absorbing customs |

### Confidence Mechanics

Confidence is not static -- it changes over time:

**Reinforcement**: Hearing the same claim from a different source increases confidence. The formula prevents trivial stacking:
```
newConfidence = oldConfidence + (1.0 - oldConfidence) * reinforcementFactor * sourceCredibility
```
Where `reinforcementFactor` depends on the new source's channel type and `sourceCredibility` is based on the source character's relationship to the believer (trusted friend vs. stranger).

**Decay**: Beliefs decay toward uncertainty over time when not reinforced. Decay rate is domain-specific:
- Norm beliefs: Slow decay (social rules persist in memory)
- Character beliefs: Medium decay (impressions fade without reinforcement)
- Location beliefs: Fast decay for dynamic claims (`dangerous`), slow for static claims (`sacred`)

**Correction**: When an NPC's belief is contradicted by direct observation (they expected punishment for theft but nothing happened, or they thought someone was dangerous but witnessed them being kind), confidence in the old belief drops sharply and a corrected belief forms at high confidence.

**Convergence**: A background worker drifts beliefs toward reality for NPCs in proximity to the belief subject. An NPC living in a faction's territory slowly converges toward the actual norms of that faction. An NPC living near a "dangerous" location gradually learns the actual danger level. Convergence speed depends on:
- Proximity (living there vs. visiting vs. far away)
- Social connectedness (more encounters = faster norm propagation)
- Personality traits (openness affects receptivity to new information; conscientiousness affects how carefully they verify claims)

---

## The Regime Change Scenario

The motivating use case, played out step by step:

```
Day 0: New faction (Harbor Authority) takes over Docks District
       replacing Old Guard
  ┌────────────────────────────────────────────────────────┐
  │ FACTION (actual):  theft: 15, contraband: 12          │
  │ HEARSAY (NPC in Docks): theft: 7, contraband: 3      │  ← still Old Guard values
  │ HEARSAY (NPC in countryside): (no beliefs about Docks)│
  └────────────────────────────────────────────────────────┘

Day 1-3: Official decree propagation
  - Harbor Authority publishes faction.territory.claimed event
  - Hearsay listens and creates "official_decree" channel beliefs
    for NPCs physically present in the Docks District
  - Confidence: 0.7 (they heard the announcement but haven't
    internalized the specifics yet)
  - NPCs in adjacent districts get "rumor" channel beliefs
    (confidence 0.3) -- they heard something changed
  - NPCs far away: unaware

Day 7-14: Social propagation via encounters
  - When NPC A (who knows) has an encounter with NPC B (who doesn't),
    the encounter event triggers belief propagation
  - NPC B receives the claim at "social_contact" confidence (0.3-0.5)
  - NPCs who traveled to the Docks and returned bring higher-confidence
    knowledge (0.6-0.7) to their home district
  - Telephone-game distortion: some NPCs hear exaggerated penalties
    ("they're hanging smugglers!") creating beliefs with inflated
    believedValue but low confidence

Day 14-30: Convergence
  - Background worker drifts beliefs toward reality for proximate NPCs
  - NPCs in the Docks converge to actual norms (confidence → 0.9+)
  - NPCs who visited once converge more slowly
  - NPCs who only heard rumors may still have distorted beliefs

Day 30+: Steady state
  - Most local NPCs have accurate beliefs
  - Distant NPCs who haven't visited may still operate on old info
  - The transition period creates emergent narrative: NPCs who
    violated "new" laws they didn't know about, NPCs who fled
    based on exaggerated rumors, NPCs who exploited the confusion
```

---

## Character Beliefs: The Missing Perception Layer

The current system answers "what has Kael directly experienced with Mira" (character-encounter) but cannot answer "what has Kael heard about Mira." This is a critical gap.

### Current State (Without Hearsay)

```
Kael has never met Mira.
Encounter sentiment: 0.0 (no data)
Relationship: none
Personality assessment: impossible (personality is self-model only)

Result: Kael has ZERO information about Mira.
He cannot decide whether to approach, avoid, trust, or fear her.
```

### With Hearsay

```
Kael has never met Mira, but:

1. His guildmate told him "Mira is a skilled healer" (trusted_contact, 0.6)
   → Belief: { subject: Mira, claim: "skilled_healer", value: 0.8, confidence: 0.6 }

2. A stranger in the tavern said "Mira killed a man in cold blood" (rumor, 0.2)
   → Belief: { subject: Mira, claim: "dangerous", value: 0.7, confidence: 0.2 }

3. Nobody has mentioned her honesty
   → No belief about trustworthiness (absence of information)

Result: Kael knows Mira is probably a good healer (moderate confidence)
and has heard she might be dangerous (low confidence). He has no
information about her trustworthiness. His behavior toward her will
be cautiously positive (healer reputation) but wary (danger rumor).
```

### Composition with Existing Systems

When Kael eventually meets Mira, the systems compose:

```
BEFORE meeting:
  ${hearsay.character.MIRA.dangerous}     = 0.7 (value) * 0.2 (confidence) = 0.14
  ${hearsay.character.MIRA.skilled_healer} = 0.8 * 0.6 = 0.48
  ${encounters.sentiment.MIRA}           = 0.0 (never met)

AFTER meeting (positive encounter):
  ${hearsay.character.MIRA.dangerous}     = CORRECTED: confidence drops to 0.05
  ${hearsay.character.MIRA.skilled_healer} = REINFORCED: confidence rises to 0.85
  ${encounters.sentiment.MIRA}           = 0.6 (positive encounter)
  ${hearsay.character.MIRA.trustworthy}   = NEW: 0.7 confidence from observation

ABML can compose both:
  effective_trust = ${encounters.sentiment.MIRA} * 0.6
                  + ${hearsay.character.MIRA.trustworthy} * 0.3
                  + ${hearsay.character.MIRA.dangerous} * -0.1
```

---

## Location Beliefs: Fear, Danger, and Travel Costs

Locations currently have no "perceived danger" or "reputation" attached to them. A character walks into a dragon's lair with the same behavioral cost as walking into a bakery. Hearsay fixes this.

### How Location Beliefs Work

```
NPC "Aldric" has heard about the Blackmire Swamp:

1. His mother warned him as a child: "Never go to the swamp"
   → { subject: blackmire, claim: "dangerous", value: 0.9, confidence: 0.5,
      source: "trusted_contact", decayRate: 0.01 }  ← slow decay (childhood memory)

2. A traveling merchant said: "I lost my horse in the swamp"
   → { subject: blackmire, claim: "dangerous", value: 0.6, confidence: 0.3,
      source: "social_contact" }

3. A rumor says there's buried treasure there:
   → { subject: blackmire, claim: "profitable", value: 0.8, confidence: 0.15,
      source: "rumor" }

Effect on behavior:
  - GOAP travel cost to Blackmire: base_cost + (danger_belief * danger_weight)
  - Anxiety state modification: stress increases proportional to
    believed danger * confidence as NPC approaches
  - Personality interaction: low-courage NPC avoids entirely;
    high-courage NPC may seek it out (face your fears)
  - The treasure rumor creates a cost/benefit tension:
    danger says "avoid", greed says "go"
```

### Anxiety and the Fear Gradient

Location beliefs don't just modify GOAP travel costs -- they affect the NPC's emotional state as they approach or inhabit a location. This creates the "face your fears" mechanic:

```
NPC approaches a location they believe is dangerous:

Distance: 1000m  → Mild unease (stress +0.05)
Distance: 500m   → Growing anxiety (stress +0.15, comfort -0.1)
Distance: 100m   → High anxiety (stress +0.3, comfort -0.2, fear +0.2)
At location       → Peak stress, unless direct observation corrects the belief

The gradient is proportional to:
  believedDanger * confidence * (1.0 - personality.courage)

A brave NPC with low-confidence beliefs feels minor stress.
A timid NPC with high-confidence beliefs may turn around and flee.
A NPC who faces their fear and finds the location safe gets a
  confidence CORRECTION and a personality boost (courage +0.05).
```

This creates natural character development moments: the timid NPC who forces themselves through the scary swamp and discovers it's not so bad grows as a character. The overconfident NPC who ignores rumors of danger and gets attacked learns to respect hearsay. These arcs emerge from the belief system without any scripted narrative.

---

## Storyline Integration: Rumors as Narrative Seeds

Hearsay naturally feeds the scenario generation system. Regional watchers can query hearsay to find characters operating on outdated or manipulated beliefs -- prime targets for scenarios. "This character still thinks the old king's laws apply" is a scenario hook. "This character heard a rumor about buried treasure in the mountains" is a quest seed. "This character believes their friend betrayed them based on a false rumor" is dramatic irony the storyline composer can exploit. Hearsay data enriches the `NarrativeState` extraction that drives storyline composition, adding the `belief_deltas` dimension (what the character believes vs. what is true) that formal narrative theory calls "dramatic irony."

Hearsay provides the Storyline system with three powerful new capabilities:

### 1. Dramatic Irony Detection

The delta between belief and reality is the formal definition of dramatic irony. Hearsay provides this delta as queryable data:

```
Regional Watcher query:
  "Find characters whose beliefs significantly diverge from reality"

Results:
  - Aldric believes Mira killed a man (confidence 0.2, reality: false)
    → DRAMATIC IRONY: Mira is actually a pacifist healer
    → Scenario hook: "The False Accusation"

  - Kael believes the Docks are still under Old Guard rules (confidence 0.8)
    → DRAMATIC IRONY: New regime will arrest him for activities
      that were legal last week
    → Scenario hook: "Ignorance of the Law"

  - Elara heard treasure lies in the Blackmire (confidence 0.15)
    → DRAMATIC IRONY: The rumor was planted by bandits to lure victims
    → Scenario hook: "The Trap"
```

### 2. Rumor-Driven Scenario Triggering

Rumors can be scenario preconditions. The Storyline plugin's scenario definition format gains a new condition type:

```yaml
# Scenario: "The Witch Hunt"
conditions:
  characterRequirements:
    - archetype: accused
      constraints:
        hearsay:
          # At least 3 NPCs must believe the accused is "cursed"
          beliefAbout: "cursed"
          minBelievers: 3
          minAverageConfidence: 0.4

  worldRequirements:
    - type: hearsay_saturation
      # The rumor must have spread to at least 30% of local NPCs
      claim: "cursed"
      locationId: "${accused.current_location}"
      saturationThreshold: 0.3
```

When enough NPCs believe a character is cursed, a witch hunt scenario becomes available. The regional watcher can choose to trigger it. The scenario is emergent from social dynamics, not scripted.

### 3. Narrative State Enrichment

The storyline composer extracts `NarrativeState` from compressed archives for storyline planning. Hearsay adds a `belief_deltas` dimension:

```yaml
narrative_state_extraction:
  # Existing: personality, history, encounters
  character_personality: { honesty: 0.8, courage: -0.3 }
  character_history: { trauma: "lost_family", goals: ["find_truth"] }

  # NEW from hearsay: what did they believe vs. reality?
  belief_deltas:
    - subject: "village_elder"
      believed: { trustworthy: 0.9 }
      reality: { betrayer: true }
      narrative_weight: HIGH  # Major dramatic irony -- story seed

    - subject: "blackmire_swamp"
      believed: { cursed: 0.7 }
      reality: { abandoned_settlement: true }
      narrative_weight: MEDIUM  # Mystery seed
```

These belief deltas become GOAP preconditions for storyline planning. A `betrayal_discovery` story action requires a character who trusted someone who actually betrayed them. A `face_fears` action requires a character with high-confidence location danger beliefs. The story system's A* planner finds action sequences that resolve these dramatic tensions.

### 4. God-Injected Rumors as Narrative Tools

Regional watcher gods can inject rumors to create narrative conditions:

```yaml
# God of Mischief behavior document (ABML)
flows:
  stir_trouble:
    # Find a prosperous, stable community
    - api_call:
        service: faction
        endpoint: /faction/norm/query-applicable
        data:
          characterId: "${random_local}"
          locationId: "${my_region.center}"
        result: norms

    - cond:
        - when: "${norms.mergedMap.size > 5}"  # Well-governed area
          then:
            # Inject a destabilizing rumor
            - api_call:
                service: hearsay
                endpoint: /hearsay/rumor/inject
                data:
                  locationId: "${my_region.center}"
                  domain: "norm"
                  claimCode: "execution_for_minor_offense"
                  believedValue: 50.0  # Extremely harsh
                  targetRadius: 500  # meters
                  propagationSpeed: "fast"
                  sourceAttribution: null  # Anonymous rumor

            # Watch what happens (check back in a few days)
            - set:
                "pending_observations.${my_region.center}":
                  injected_at: "${now}"
                  expected_effect: "merchant_flight"
                  check_after: "P3D"
```

The god creates narrative opportunities by manipulating social information. If merchants flee based on false rumors of harsh enforcement, the economy shifts, which creates real consequences, which feeds back into the content flywheel.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Belief records (MySQL), propagation records (MySQL), belief cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for belief mutations and propagation operations |
| lib-messaging (`IMessageBus`) | Publishing belief change events, error event publication |
| lib-messaging (`IEventConsumer`) | Subscribing to faction territory/norm events, encounter events for propagation triggers |
| lib-character (`ICharacterClient`) | Validating character existence, querying character location for proximity-based convergence (L2) |
| lib-location (`ILocationClient`) | Resolving location hierarchy for proximity calculations and location belief subjects (L2) |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration, compression callback registration (L1) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-faction (`IFactionClient`) | Querying actual norms for convergence worker (ground truth for norm beliefs) | Norm beliefs never converge -- they persist at whatever confidence they were acquired at, and decay normally |
| lib-character-encounter (`ICharacterEncounterClient`) | Querying actual sentiment for convergence of character beliefs | Character beliefs never converge toward encounter-based truth |
| lib-character-personality (`ICharacterPersonalityClient`) | Personality-mediated belief receptivity (openness affects how readily NPCs accept new claims) | All NPCs have equal receptivity to new beliefs |
| lib-puppetmaster (`IPuppetmasterClient`) | Divine rumor injection coordination, regional watcher notification of belief saturation events | Divine rumor injection unavailable; saturation events not notified |
| lib-storyline (`IStorylineClient`) | Querying active storylines to prevent belief corrections that would break active narrative arcs | No narrative protection -- beliefs converge freely even if a storyline depends on the misconception |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `HearsayProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates hearsay provider instances per character for ABML behavior execution (`${hearsay.*}` variables) |
| lib-obligation (L4, planned optional) | Obligation could optionally query hearsay for belief-filtered norm costs instead of raw Faction norms, providing a "fuzzy obligation" mode. This is an OPTIONAL integration -- obligation works perfectly without hearsay. |
| lib-disposition (L4, planned) | Soft dependency (`IHearsayClient`) for hearsay component of base feeling synthesis (`hearsayTrustSignal`, etc.). Also subscribes to `hearsay.belief.acquired` and `hearsay.belief.corrected` events to trigger feeling resynthesis when beliefs change. Gracefully degrades: hearsay weight redistributes to other sources when unavailable. |
| lib-trade (L4, planned) | Soft dependency (`IHearsayClient`) for belief-filtered price knowledge in NPC market analysis (imperfect information about prices and trade conditions) |
| lib-transit (L4, planned) | Registers `"hearsay"` `ITransitCostModifierProvider` to inject risk perception from believed dangers along transit connections into route cost calculations (e.g., "believes mountain road is cursed" → `RiskDelta: 0.4`) |
| lib-lexicon (L4, planned) | Hearsay claims can reference Lexicon entry codes as claim targets. "I heard wolves breathe fire" is a Hearsay claim about Lexicon entry `wolf` with a trait `fire_breathing` that contradicts Lexicon ground truth. Bidirectional relationship. |
| lib-character-lifecycle (L4, planned) | Soft dependency (`IHearsayClient`) for seeding initial beliefs in newborn characters from family cultural context (what the family "knows"). When unavailable, characters start with no inherited beliefs. |
| lib-storyline (L4, planned) | Storyline can query hearsay for dramatic irony detection (belief vs. reality deltas) and use belief saturation as scenario preconditions |
| lib-puppetmaster (L4, planned) | Regional watcher god actors inject rumors via hearsay API and monitor belief saturation for narrative opportunities |
| lib-gardener (L4, planned) | Player experience orchestration can use hearsay to surface "what your character has heard" as a player-facing information feed |

---

## State Storage

### Belief Store
**Store**: `hearsay-beliefs` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `belief:{beliefId}` | `BeliefEntryModel` | Primary lookup by belief ID |
| `belief:char:{characterId}` | `BeliefListModel` | All beliefs held by a character (paginated query) |
| `belief:char:{characterId}:{domain}` | `BeliefListModel` | Domain-filtered beliefs per character |
| `belief:char:{characterId}:{domain}:{subjectId}` | `BeliefListModel` | All beliefs about a specific subject (a character may hold multiple beliefs about the same subject, e.g., "dangerous" AND "skilled_fighter") |
| `belief:subject:{domain}:{subjectId}` | `BeliefSaturationModel` | Reverse index: how many characters believe something about this subject (for saturation queries) |

### Propagation Store
**Store**: `hearsay-propagation` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `prop:{propagationId}` | `PropagationRecordModel` | Tracks a rumor's spread: origin, current reach, propagation pattern, injection source. Used by convergence worker and analytics. |
| `prop:active:{locationId}` | `ActivePropagationListModel` | Active propagation waves affecting a location (for propagation worker processing) |

### Belief Cache
**Store**: `hearsay-cache` (Backend: Redis, prefix: `hearsay:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `manifest:{characterId}` | `BeliefManifestModel` | Cached composite belief manifest per character: pre-aggregated beliefs across all domains, organized for fast variable provider reads. TTL-based expiry with event-driven invalidation. |
| `saturation:{domain}:{subjectId}:{claimCode}` | `SaturationCacheModel` | Cached belief saturation: percentage of local NPCs holding this belief. Used by storyline scenario conditions. TTL-based. |

### Distributed Locks
**Store**: `hearsay-lock` (Backend: Redis, prefix: `hearsay:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `belief:{characterId}` | Belief mutation lock (prevents concurrent updates to same character's beliefs) |
| `prop:{propagationId}` | Propagation processing lock (serializes propagation wave advancement) |
| `converge:{characterId}` | Convergence processing lock (serializes belief convergence for a character) |
| `inject:{locationId}` | Rumor injection lock (prevents duplicate injections at same location) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `hearsay.belief.acquired` | `HearsayBeliefAcquiredEvent` | Character acquires a new belief (any channel). Includes full belief context. |
| `hearsay.belief.reinforced` | `HearsayBeliefReinforcedEvent` | Existing belief's confidence increased by a new source. Includes old/new confidence. |
| `hearsay.belief.corrected` | `HearsayBeliefCorrectedEvent` | Belief contradicted by direct observation. Includes old belief, correction source. |
| `hearsay.belief.decayed` | `HearsayBeliefDecayedEvent` | Belief confidence dropped below threshold via time-based decay (batch event from decay worker). |
| `hearsay.belief.converged` | `HearsayBeliefConvergedEvent` | Belief converged toward ground truth via proximity/osmosis (from convergence worker). |
| `hearsay.rumor.injected` | `HearsayRumorInjectedEvent` | External actor injected a rumor. Includes injection parameters, source attribution. |
| `hearsay.rumor.spread` | `HearsayRumorSpreadEvent` | Propagation wave reached new characters (batch event from propagation worker). |
| `hearsay.saturation.reached` | `HearsaySaturationReachedEvent` | A belief claim crossed the saturation threshold at a location. Useful for storyline scenario triggering. |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `faction.territory.claimed` | `HandleTerritoryClaimedAsync` | Inject "official_decree" norm beliefs for NPCs present in the claimed territory. Start propagation wave for adjacent areas. |
| `faction.territory.released` | `HandleTerritoryReleasedAsync` | Mark existing norm beliefs for this territory as stale (increase decay rate). |
| `faction.norm.defined` | `HandleNormDefinedAsync` | Inject "official_decree" beliefs for NPCs in the norm's faction territory. |
| `faction.norm.updated` | `HandleNormUpdatedAsync` | Update believed values for NPCs who already hold beliefs about this norm. |
| `faction.norm.deleted` | `HandleNormDeletedAsync` | Mark beliefs about this norm as stale. |
| `faction.realm-baseline.designated` | `HandleRealmBaselineDesignatedAsync` | Start slow propagation of realm baseline norm beliefs. |
| `character-encounter.created` | `HandleEncounterCreatedAsync` | Propagation trigger: when two characters meet, beliefs may propagate between them based on relationship closeness, encounter context, and personality receptivity. |
| `obligation.violation.reported` | `HandleViolationReportedAsync` | If the violation was witnessed, create "direct_observation" beliefs about the violating character for witnesses. Potential propagation trigger if witnesses share what they saw. |

### Resource Cleanup (FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| character | hearsay | CASCADE | `/hearsay/cleanup-by-character` |
| realm | hearsay | CASCADE | `/hearsay/cleanup-by-realm` |
| location | hearsay | CASCADE | `/hearsay/cleanup-by-location` |

### Compression Callback (via x-compression-callback)

| Resource Type | Source Type | Priority | Compress Endpoint | Decompress Endpoint |
|--------------|-------------|----------|-------------------|---------------------|
| character | hearsay | 30 | `/hearsay/get-compress-data` | `/hearsay/restore-from-archive` |

Archives belief state for character compression. On restore, beliefs are NOT restored from archive (they would be stale). Instead, convergence worker rebuilds beliefs from current ground truth at low confidence, simulating "returning from a long absence."

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `BeliefCacheTtlMinutes` | `HEARSAY_BELIEF_CACHE_TTL_MINUTES` | `5` | TTL for cached belief manifests per character (range: 1-60) |
| `MaxBeliefsPerCharacter` | `HEARSAY_MAX_BELIEFS_PER_CHARACTER` | `500` | Safety limit on total beliefs per character across all domains (range: 50-2000) |
| `MaxBeliefsPerSubject` | `HEARSAY_MAX_BELIEFS_PER_SUBJECT` | `10` | Max distinct claim codes per subject per character (range: 3-50) |
| `NormDecayRatePerDay` | `HEARSAY_NORM_DECAY_RATE_PER_DAY` | `0.02` | Confidence decay rate per day for norm beliefs (range: 0.001-0.1) |
| `CharacterDecayRatePerDay` | `HEARSAY_CHARACTER_DECAY_RATE_PER_DAY` | `0.05` | Confidence decay rate per day for character beliefs (range: 0.001-0.2) |
| `LocationDecayRatePerDay` | `HEARSAY_LOCATION_DECAY_RATE_PER_DAY` | `0.03` | Confidence decay rate per day for location beliefs (range: 0.001-0.15) |
| `ConvergenceIntervalMinutes` | `HEARSAY_CONVERGENCE_INTERVAL_MINUTES` | `30` | How often the convergence worker processes beliefs (range: 5-120) |
| `ConvergenceProximityRadius` | `HEARSAY_CONVERGENCE_PROXIMITY_RADIUS` | `1000` | Distance in meters within which proximity convergence applies (range: 100-10000) |
| `ConvergenceRatePerCycle` | `HEARSAY_CONVERGENCE_RATE_PER_CYCLE` | `0.05` | How much beliefs drift toward ground truth per convergence cycle (range: 0.01-0.2) |
| `PropagationIntervalMinutes` | `HEARSAY_PROPAGATION_INTERVAL_MINUTES` | `15` | How often the propagation worker advances rumor waves (range: 5-60) |
| `PropagationBatchSize` | `HEARSAY_PROPAGATION_BATCH_SIZE` | `50` | Characters per batch in propagation worker (range: 10-200) |
| `DecayIntervalMinutes` | `HEARSAY_DECAY_INTERVAL_MINUTES` | `60` | How often the decay worker degrades belief confidence (range: 15-1440) |
| `DecayBatchSize` | `HEARSAY_DECAY_BATCH_SIZE` | `100` | Beliefs per batch in decay worker (range: 10-500) |
| `SaturationThreshold` | `HEARSAY_SATURATION_THRESHOLD` | `0.3` | Percentage of local NPCs needed to trigger saturation event (range: 0.1-0.9) |
| `MinConfidenceForProvider` | `HEARSAY_MIN_CONFIDENCE_FOR_PROVIDER` | `0.05` | Beliefs below this confidence are excluded from variable provider (range: 0.01-0.5) |
| `EncounterPropagationChance` | `HEARSAY_ENCOUNTER_PROPAGATION_CHANCE` | `0.3` | Probability that beliefs propagate during a character encounter (range: 0.0-1.0) |
| `RumorDistortionFactor` | `HEARSAY_RUMOR_DISTORTION_FACTOR` | `0.15` | Maximum percentage distortion applied to believedValue during rumor propagation (range: 0.0-0.5) |
| `DistributedLockTimeoutSeconds` | `HEARSAY_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition (range: 5-120) |
| `QueryPageSize` | `HEARSAY_QUERY_PAGE_SIZE` | `20` | Default page size for paged queries (range: 1-100) |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<HearsayService>` | Structured logging |
| `HearsayServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 4 stores) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Faction, encounter, and obligation event subscriptions |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `ICharacterClient` | Character existence validation and location queries (L2) |
| `ILocationClient` | Location hierarchy resolution for proximity (L2) |
| `IResourceClient` | Reference tracking, cleanup callbacks, compression callbacks (L1) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies (Faction, Encounter, Personality, Puppetmaster, Storyline) |

### Background Workers

| Worker | Interval Config | Lock Key | Purpose |
|--------|----------------|----------|---------|
| `HearsayConvergenceWorkerService` | `ConvergenceIntervalMinutes` | `hearsay:lock:convergence-worker` | Drifts beliefs toward ground truth for proximate characters. Queries actual norms (Faction), actual sentiment (Encounter), and actual location data (Location) to compute convergence direction. |
| `HearsayPropagationWorkerService` | `PropagationIntervalMinutes` | `hearsay:lock:propagation-worker` | Advances active propagation waves. For each active propagation, finds characters within the wave's current radius and creates new beliefs (with distortion). Expands radius each cycle. |
| `HearsayDecayWorkerService` | `DecayIntervalMinutes` | `hearsay:lock:decay-worker` | Degrades belief confidence over time. Removes beliefs that fall below `MinConfidenceForProvider`. Publishes batch decay events. |

---

## API Endpoints (Implementation Notes)

### Belief Management (6 endpoints)

- **RecordBelief** (`/hearsay/belief/record`): Creates or reinforces a belief for a character. If a belief with matching `characterId + domain + subjectId + claimCode` exists, reinforces confidence. Otherwise creates new belief. Validates character existence. Acquires distributed lock. Invalidates belief cache. Publishes `belief.acquired` or `belief.reinforced` event. Primary API for external systems to feed information to NPCs.

- **CorrectBelief** (`/hearsay/belief/correct`): Records a direct observation that contradicts an existing belief. Drops confidence on the old belief, creates or reinforces the corrected belief at high confidence. Publishes `belief.corrected` event. Used when an NPC directly witnesses something that contradicts what they've heard.

- **QueryBeliefs** (`/hearsay/belief/query`): Paginated query of a character's beliefs. Filters by domain, subject, claim code, minimum confidence, and time range. Returns beliefs sorted by confidence descending.

- **GetBeliefManifest** (`/hearsay/belief/get-manifest`): Returns the cached composite belief manifest for a character across all domains. Pre-aggregated for fast provider reads. Same data the variable provider uses. Useful for external callers who want the full belief picture.

- **QueryBeliefDelta** (`/hearsay/belief/query-delta`): **The dramatic irony endpoint.** Given a character and domain, returns beliefs alongside ground truth (queried from Faction/Encounter/Location), computed delta, and narrative weight classification (LOW/MEDIUM/HIGH based on delta magnitude and belief confidence). Used by Storyline for scenario condition evaluation.

- **GetBeliefSaturation** (`/hearsay/belief/get-saturation`): Returns what percentage of characters at a location hold a specific belief. Used by Storyline scenario conditions and regional watcher behavior. Cached in Redis with TTL.

### Rumor Management (3 endpoints)

- **InjectRumor** (`/hearsay/rumor/inject`): External rumor injection. Creates a propagation record and seeds initial beliefs in characters within `targetRadius` of the specified location. Supports optional `sourceAttribution` (null = anonymous rumor). Initial confidence based on `rumor` channel (0.1-0.3). Acquires injection lock to prevent duplicates. Publishes `rumor.injected` event. Requires `developer` role.

- **ListActiveRumors** (`/hearsay/rumor/list-active`): Paginated list of active propagation waves within a game service or location scope. Shows origin, current radius, propagation speed, affected character count, injection source if attributed.

- **ExpireRumor** (`/hearsay/rumor/expire`): Manually stops a propagation wave. Does NOT remove existing beliefs (they decay naturally). Prevents further spread. Requires `developer` role.

### Propagation Configuration (2 endpoints)

- **SetChannelConfig** (`/hearsay/channel/set`): Configures per-channel parameters for a game service: base confidence range, distortion factor, propagation speed, personality receptivity weights. Allows games to tune how information spreads. Requires `developer` role.

- **ListChannelConfigs** (`/hearsay/channel/list`): Returns channel configurations for a game service. Returns defaults if no custom config exists.

### Convergence Management (2 endpoints)

- **ForceConvergence** (`/hearsay/convergence/force`): Administrative endpoint to force immediate convergence for a character or location. Bypasses the background worker schedule. Requires `developer` role.

- **GetConvergenceStatus** (`/hearsay/convergence/status`): Returns convergence statistics: beliefs converged, average drift, characters processed in last cycle.

### Cleanup Endpoints (3 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByCharacter** (`/hearsay/cleanup-by-character`): Removes all beliefs held BY and ABOUT a character. Removes propagation records originated by the character. Returns cleanup counts.
- **CleanupByRealm** (`/hearsay/cleanup-by-realm`): Removes all beliefs, propagation records, and saturation caches for characters/locations in the realm.
- **CleanupByLocation** (`/hearsay/cleanup-by-location`): Removes location-domain beliefs about this location and associated propagation records.

### Compression Endpoints (2 endpoints)

- **GetCompressData** (`/hearsay/get-compress-data`): Returns a `HearsayArchive` (extends `ResourceArchiveBase`) containing the character's belief snapshot: high-confidence beliefs only (confidence > 0.5), organized by domain. Used by Storyline for `belief_deltas` extraction in narrative state computation.
- **RestoreFromArchive** (`/hearsay/restore-from-archive`): Does NOT restore beliefs (they would be stale). Instead marks the character for accelerated convergence on next worker cycle, simulating "relearning" the current state of affairs.

---

## Variable Provider: `${hearsay.*}` Namespace

Implements `IVariableProviderFactory` (via `HearsayProviderFactory`) providing the following variables to Actor (L2) via the Variable Provider Factory pattern. Loads from the cached belief manifest.

### Norm Beliefs

| Variable | Type | Description |
|----------|------|-------------|
| `${hearsay.norm.count}` | int | Total norm beliefs held |
| `${hearsay.norm.believed_cost.<violationType>}` | float | Believed penalty for a violation type (weighted by confidence). Returns 0.0 if no belief. |
| `${hearsay.norm.confidence.<violationType>}` | float | Confidence in the believed cost (0.0-1.0). Returns 0.0 if no belief. |
| `${hearsay.norm.has_belief.<violationType>}` | bool | Whether the character has ANY belief about this violation type |
| `${hearsay.norm.highest_believed_cost}` | string | Violation type with highest believed penalty |
| `${hearsay.norm.awareness_ratio}` | float | Ratio of actual norms the NPC knows about vs. total actual norms (requires Faction soft dependency for computation; returns 1.0 if Faction unavailable) |

### Character Beliefs

| Variable | Type | Description |
|----------|------|-------------|
| `${hearsay.character.<id>.reputation}` | float | Composite reputation score for a character: weighted average of all belief values about them, weighted by confidence. Range -1.0 to +1.0. |
| `${hearsay.character.<id>.has_heard_of}` | bool | Whether the NPC has heard ANYTHING about this character |
| `${hearsay.character.<id>.<claimCode>}` | float | Believed value for a specific claim (e.g., `dangerous`, `trustworthy`, `skilled_fighter`). Returns 0.0 if no belief. |
| `${hearsay.character.<id>.<claimCode>.confidence}` | float | Confidence in a specific character claim |
| `${hearsay.character.<id>.claim_count}` | int | Number of distinct claims held about this character |

### Location Beliefs

| Variable | Type | Description |
|----------|------|-------------|
| `${hearsay.location.<id>.danger}` | float | Believed danger level (0.0-1.0), confidence-weighted. Combines all negative-valence beliefs. |
| `${hearsay.location.<id>.appeal}` | float | Believed appeal level (0.0-1.0), confidence-weighted. Combines all positive-valence beliefs. |
| `${hearsay.location.<id>.<claimCode>}` | float | Specific claim belief (e.g., `cursed`, `profitable`, `sacred`). |
| `${hearsay.location.<id>.<claimCode>.confidence}` | float | Confidence in a specific location claim |
| `${hearsay.location.<id>.has_heard_of}` | bool | Whether the NPC has heard ANYTHING about this location |
| `${hearsay.location.fear_gradient}` | float | Current fear/anxiety modifier based on proximity to locations the NPC believes are dangerous. Computed from all known dangerous locations weighted by distance. |

### Meta Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${hearsay.total_beliefs}` | int | Total beliefs across all domains |
| `${hearsay.average_confidence}` | float | Average confidence across all beliefs (crude measure of "how informed is this NPC") |
| `${hearsay.recent_corrections}` | int | Beliefs corrected in the last convergence cycle (indicates how much the NPC is "learning") |

### ABML Usage Examples

```yaml
flows:
  evaluate_travel:
    # Should I take the shortcut through the swamp?
    - cond:
        # I've heard it's very dangerous and I believe it
        - when: "${hearsay.location.BLACKMIRE.danger > 0.6
                  && hearsay.location.BLACKMIRE.danger.confidence > 0.4}"
          then:
            - cond:
                # But I'm brave enough to handle it
                - when: "${personality.courage > 0.5}"
                  then:
                    - call: take_shortcut  # Face your fears
                    - set:
                        stress: "${stress + hearsay.location.BLACKMIRE.danger * 0.3}"
                # I'm not brave enough -- take the long way
                - otherwise:
                    - call: take_safe_route

        # I haven't heard anything about it (no belief = no fear)
        - when: "${!hearsay.location.BLACKMIRE.has_heard_of}"
          then:
            - call: take_shortcut  # Ignorance is bliss

        # I heard it's fine (low danger belief)
        - otherwise:
            - call: take_shortcut

  evaluate_trade_partner:
    # Should I trade with this person?
    - cond:
        # I've heard they're dishonest
        - when: "${hearsay.character.TRADER.dishonest > 0.5
                  && hearsay.character.TRADER.dishonest.confidence > 0.3}"
          then:
            - cond:
                # But I've also dealt with them personally
                - when: "${encounters.sentiment.TRADER > 0.3}"
                  then:
                    - call: trade_cautiously  # Personal experience overrides hearsay
                - otherwise:
                    - call: decline_trade  # Trust the rumor

        # I've heard they're trustworthy
        - when: "${hearsay.character.TRADER.trustworthy > 0.5}"
          then:
            - call: trade_willingly

        # No information -- use personality to decide
        - otherwise:
            - cond:
                - when: "${personality.agreeableness > 0.3}"
                  then:
                    - call: trade_willingly  # Generally trusting
                - otherwise:
                    - call: trade_cautiously  # Generally suspicious

  consider_norm_compliance:
    # Use hearsay for fuzzy norms instead of perfect-knowledge obligations
    - cond:
        # I believe theft is very costly here
        - when: "${hearsay.norm.believed_cost.theft > 10
                  && hearsay.norm.confidence.theft > 0.5}"
          then:
            - call: buy_item  # Don't risk it

        # I'm not sure about the rules here
        - when: "${hearsay.norm.believed_cost.theft > 5
                  && hearsay.norm.confidence.theft < 0.3}"
          then:
            - cond:
                - when: "${personality.honesty > 0.5}"
                  then:
                    - call: buy_item  # Better safe than sorry
                - otherwise:
                    - call: steal_item  # Low confidence? Risk it.

        # I haven't heard of any theft penalties here
        - when: "${!hearsay.norm.has_belief.theft}"
          then:
            - call: steal_item  # Ignorance enables crime
```

---

## Visual Aid

Belief identity and propagation are owned here. Actual norm data comes from Faction. Actual character data comes from Character-Encounter (sentiment), Character-Personality (traits), and Relationship (structural bonds). Actual location data comes from Location. Hearsay does not replace any of these -- it provides the NPC's *perceived* version, which may lag behind, exaggerate, or completely misrepresent reality. Hearsay provides `${hearsay.*}` variables alongside `${obligations.*}`: Obligation gives the exact, personality-weighted cost of violating known norms; Hearsay gives the NPC's uncertain, belief-filtered perception of the social landscape. When lib-hearsay is disabled, NPCs fall back to perfect-information behavior -- existing systems work unchanged.

### Information Flow

```
┌──────────────────────────────────────────────────────────────────────┐
│                   HEARSAY INFORMATION FLOW                           │
│                                                                      │
│  GROUND TRUTH SOURCES:                                               │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐            │
│  │ Faction  │  │Character │  │ Location │  │Obligation│            │
│  │  (norms) │  │Encounter │  │  (data)  │  │(violations)│           │
│  │          │  │(sentiment)│  │          │  │          │            │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘            │
│       │              │              │              │                  │
│       │    events trigger           │              │                  │
│       │    propagation              │              │                  │
│       ▼              ▼              ▼              ▼                  │
│  ┌───────────────────────────────────────────────────────────┐       │
│  │                    HEARSAY SERVICE                         │       │
│  │                                                            │       │
│  │  ┌─────────────────────┐  ┌──────────────────────────┐   │       │
│  │  │   BELIEF STORE      │  │   PROPAGATION ENGINE     │   │       │
│  │  │                     │  │                          │   │       │
│  │  │ Per-character cache  │  │  Propagation waves       │   │       │
│  │  │ of what they THINK  │  │  (rumors spreading       │   │       │
│  │  │ they know about:    │  │   through social network) │   │       │
│  │  │  - norms            │  │                          │   │       │
│  │  │  - other characters │  │  Convergence worker      │   │       │
│  │  │  - locations        │  │  (beliefs drift toward   │   │       │
│  │  │                     │  │   reality over time)     │   │       │
│  │  └──────────┬──────────┘  │                          │   │       │
│  │             │              │  Decay worker            │   │       │
│  │             │              │  (old beliefs fade)      │   │       │
│  │             │              └──────────────────────────┘   │       │
│  └─────────────┼────────────────────────────────────────────┘       │
│                │                                                     │
│                ▼                                                     │
│  ┌───────────────────────────────────────────────────────────┐       │
│  │              HearsayProviderFactory                        │       │
│  │              IVariableProviderFactory                      │       │
│  │                                                            │       │
│  │  ${hearsay.norm.believed_cost.<type>}                     │       │
│  │  ${hearsay.character.<id>.reputation}                     │       │
│  │  ${hearsay.location.<id>.danger}          ──────► Actor   │       │
│  │  ${hearsay.location.fear_gradient}                (L2)    │       │
│  │  ${hearsay.norm.awareness_ratio}                  ABML    │       │
│  │                                                   GOAP    │       │
│  └───────────────────────────────────────────────────────────┘       │
│                                                                      │
│  INJECTION POINTS:                                                   │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐                          │
│  │ God      │  │ NPC      │  │ Player   │                          │
│  │ Actors   │  │ Gossip   │  │ Spread   │                          │
│  │ (inject  │  │ (natural │  │ (actions │                          │
│  │  rumor)  │  │  spread) │  │  create  │                          │
│  │          │  │          │  │  beliefs) │                          │
│  └──────────┘  └──────────┘  └──────────┘                          │
└──────────────────────────────────────────────────────────────────────┘
```

### Belief Lifecycle

```
┌──────────────────────────────────────────────────────────────────────┐
│                    BELIEF LIFECYCLE                                    │
│                                                                      │
│  ACQUISITION                                                         │
│  ┌─────────┐   ┌─────────────┐   ┌─────────────┐                   │
│  │ Direct  │   │ Official   │   │ Rumor      │                     │
│  │ Observe │   │ Decree     │   │ Injection  │                     │
│  │ c: 0.9  │   │ c: 0.75   │   │ c: 0.2    │                     │
│  └────┬────┘   └─────┬─────┘   └─────┬─────┘                     │
│       │               │               │                              │
│       └───────────────┼───────────────┘                              │
│                       ▼                                              │
│                 ┌───────────┐                                        │
│                 │  BELIEF   │                                        │
│                 │  ENTRY    │                                        │
│                 │  c: 0.xx  │                                        │
│                 └─────┬─────┘                                        │
│                       │                                              │
│       ┌───────────────┼───────────────┐                              │
│       ▼               ▼               ▼                              │
│  REINFORCEMENT   DECAY          CORRECTION                          │
│  ┌─────────┐   ┌─────────┐   ┌─────────┐                          │
│  │ Same    │   │ Time    │   │ Direct  │                           │
│  │ claim   │   │ passes, │   │ observe │                           │
│  │ new     │   │ no      │   │ contra- │                           │
│  │ source  │   │ reinf.  │   │ diction │                           │
│  │ c: ↑    │   │ c: ↓    │   │ c: ↓↓↓  │                          │
│  └─────────┘   └─────────┘   └────┬────┘                          │
│                       │            │                                 │
│                       ▼            ▼                                 │
│                 ┌───────────┐ ┌───────────┐                         │
│                 │ EXPIRED   │ │ NEW BELIEF │                         │
│                 │ (below    │ │ (corrected │                         │
│                 │ threshold)│ │  high conf)│                         │
│                 └───────────┘ └───────────┘                         │
│                                                                      │
│  CONVERGENCE (background worker)                                     │
│  ┌──────────────────────────────────────────────┐                   │
│  │ For NPCs proximate to belief subject:         │                   │
│  │   believed_value drifts toward actual_value   │                   │
│  │   confidence increases toward ground truth    │                   │
│  │   Rate depends on:                            │                   │
│  │     - distance to subject                     │                   │
│  │     - social connectedness                    │                   │
│  │     - personality.openness                    │                   │
│  └──────────────────────────────────────────────┘                   │
└──────────────────────────────────────────────────────────────────────┘
```

### Storyline Integration

```
┌──────────────────────────────────────────────────────────────────────┐
│                  HEARSAY → STORYLINE BRIDGE                          │
│                                                                      │
│  Regional Watcher (God of Tragedy)                                   │
│  ┌──────────────────────────────────────────────┐                   │
│  │ "I want to find dramatic irony..."            │                   │
│  │                                                │                   │
│  │ api_call:                                      │                   │
│  │   service: hearsay                            │                   │
│  │   endpoint: /hearsay/belief/query-delta       │                   │
│  │   data:                                        │                   │
│  │     domain: "character"                        │                   │
│  │     locationId: "${my_region}"                 │                   │
│  │     minDeltaMagnitude: 0.5                    │                   │
│  │     minNarrativeWeight: "MEDIUM"              │                   │
│  └──────────────────────┬───────────────────────┘                   │
│                          │                                           │
│                          ▼                                           │
│  ┌──────────────────────────────────────────────┐                   │
│  │ Results:                                      │                   │
│  │                                                │                   │
│  │ 1. Kael believes Mira is dangerous (0.7)      │                   │
│  │    Reality: Mira is a pacifist healer          │                   │
│  │    Delta: 1.4 | Weight: HIGH                  │                   │
│  │    → Scenario seed: "The False Accusation"    │                   │
│  │                                                │                   │
│  │ 2. Aldric believes old laws apply (0.8)        │                   │
│  │    Reality: New faction, new laws              │                   │
│  │    Delta: 0.6 | Weight: MEDIUM                │                   │
│  │    → Scenario seed: "Ignorance of the Law"    │                   │
│  │                                                │                   │
│  │ 3. Elara believes treasure in swamp (0.15)    │                   │
│  │    Reality: Bandit trap                        │                   │
│  │    Delta: 0.95 | Weight: HIGH                 │                   │
│  │    → Scenario seed: "The Trap"                │                   │
│  └──────────────────────┬───────────────────────┘                   │
│                          │                                           │
│                          ▼                                           │
│  ┌──────────────────────────────────────────────┐                   │
│  │ Watcher queries Storyline plugin:             │                   │
│  │   /storyline/scenario/find-matching           │                   │
│  │   with hearsay-derived candidate data         │                   │
│  │                                                │                   │
│  │ Storyline's lazy phase evaluation uses         │                   │
│  │ current hearsay state in each phase:           │                   │
│  │   "Has the character learned the truth yet?"   │                   │
│  │   If yes → storyline adapts (twist resolved)  │                   │
│  │   If no → storyline escalates (deeper irony)  │                   │
│  └──────────────────────────────────────────────┘                   │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no code. The following phases are planned:

### Phase 0: Prerequisites (changes to existing services)

- **EncountersProviderFactory bug fix**: The factory currently doesn't pre-load sentiment, hasMet, or pairEncounters data. This must be fixed before hearsay can compose with encounter sentiment. See CHARACTER-ENCOUNTER deep dive.
- **Encounter event enhancement**: `character-encounter.created` events must include participant IDs and context for hearsay propagation triggers.

### Phase 1: Core Belief Infrastructure

- Create `hearsay-api.yaml` schema with belief management endpoints
- Create `hearsay-events.yaml` schema
- Create `hearsay-configuration.yaml` schema
- Generate service code
- Implement belief CRUD (record, correct, query, get-manifest)
- Implement belief cache with event-driven invalidation
- Implement variable provider factory (`${hearsay.*}` namespace)
- Implement resource cleanup and compression callbacks

### Phase 2: Propagation Engine

- Implement encounter-triggered belief propagation (HandleEncounterCreatedAsync)
- Implement faction event-driven belief injection (HandleTerritoryClaimedAsync, etc.)
- Implement rumor injection API
- Implement propagation worker (advancing rumor waves through social networks)
- Implement distortion mechanics (telephone-game effect during propagation)

### Phase 3: Convergence and Decay

- Implement convergence worker (drift beliefs toward ground truth)
- Implement decay worker (degrade confidence over time)
- Implement correction mechanics (direct observation contradicts belief)
- Implement personality-mediated receptivity (openness affects belief acquisition)

### Phase 4: Storyline Integration

- Implement `QueryBeliefDelta` endpoint (dramatic irony detection)
- Implement `GetBeliefSaturation` endpoint (scenario preconditions)
- Implement saturation threshold events
- Define storyline scenario condition types for hearsay (`beliefAbout`, `hearsay_saturation`)
- Implement narrative protection (prevent convergence from breaking active storylines -- soft dependency on lib-storyline)

### Phase 5: Location Beliefs and Fear Gradient

- Implement location belief domain
- Implement fear gradient computation (proximity to believed-dangerous locations)
- Implement anxiety state modification in variable provider
- Wire location beliefs into GOAP travel cost modifiers

### Phase 6: Character Beliefs and Reputation

- Implement character belief domain
- Implement composite reputation score computation
- Wire into encounter-driven propagation (witnesses share information about people)
- Implement obligation violation propagation (witnessed violations create beliefs about the violator)

---

## Potential Extensions

1. **Gossip networks**: Characters form implicit social networks based on encounter frequency. Information propagates faster through dense networks (urban areas) and slower through sparse ones (rural). Network topology affects which rumors reach which characters.

2. **Propaganda system**: Factions can systematically inject beliefs through their governance capabilities (unlocked by seed growth like norm definition). A sovereign faction can broadcast official decrees. A nascent faction can only spread rumors through its members. Propaganda is just structured, faction-backed rumor injection.

3. **Credibility tracking**: NPCs learn which information sources are reliable. If NPC A told NPC B something that turned out to be wrong, B's trust in A as an information source decreases. Future claims from A are received at lower confidence. This creates emergent information ecosystems where some NPCs become trusted information brokers and others become known liars.

4. **Belief reasoning**: Higher-intelligence NPCs could perform simple logical reasoning on their beliefs. "I heard Mira is a healer AND I heard she killed someone. These are contradictory. Which do I believe more?" This would require a lightweight contradiction detection system operating on belief valence.

5. **Cultural memory**: Some beliefs persist at the community level even when individual holders die. "Everyone in this village knows the swamp is dangerous" -- the belief exists in the collective, not just in individuals. Community beliefs could be modeled as location-scoped beliefs that new NPCs inherit at low confidence upon being created/moving in.

6. **Obligation integration mode**: An optional configuration flag on lib-obligation that routes norm cost queries through hearsay instead of directly to Faction. When enabled, `${obligations.violation_cost.<type>}` would reflect the NPC's *believed* norm costs rather than actual norm costs. This makes the existing obligation variable provider automatically fuzzy without behavior authors needing to reference `${hearsay.*}` separately.

7. **Client events for player information**: `hearsay-client-events.yaml` for pushing "your character has heard..." notifications to connected WebSocket clients. Player-facing rumors, warnings about dangerous locations, and character reputation updates as the guardian spirit's information feed.

8. **Hearsay as content flywheel input**: Belief archives from compressed characters become narrative material. "This character died believing a lie about their friend" is a tragedy seed. "This character discovered the truth about a rumor and acted on it" is a heroism seed. The delta between belief-at-death and reality enriches the storyline composer's narrative state extraction.

9. **Belief visualization for Gardener**: The Gardener service could use hearsay data to show players a "rumor map" -- what their character has heard about the world, with confidence levels as visual opacity. This makes the information landscape tangible and creates player motivation to investigate rumors.

10. **Inter-service rumor injection hooks**: Allow any service to inject beliefs via a standardized event format. When a character completes a dramatic quest, the quest system publishes an event that hearsay picks up and propagates as "word of heroic deeds." This is the organic reputation system -- fame emerges from actions propagating through the social network.

---

## Why Not Extend Obligation or Faction?

The question arises: why not add belief filtering to Obligation or temporal propagation to Faction?

**Obligation handles cost computation, not information propagation.** Obligation answers "what does it cost?" given known constraints. Adding beliefs to Obligation would conflate two concerns: "what do I know?" and "what does it cost?" Hearsay handles knowledge; Obligation handles cost. They compose cleanly.

**Faction handles social structure, not individual perception.** Faction answers "what norms exist?" for the world. Adding per-character perception to Faction would mean every norm query needs a character context, fundamentally changing Faction's API from "world truth" to "perceived truth." Faction should remain the authoritative source of actual norms.

**Hearsay is a separate domain.** Information propagation, belief formation, confidence decay, rumor injection, convergence mechanics -- these are a coherent, independent domain with their own state, events, workers, and configuration. They have independent value (character beliefs and location beliefs exist regardless of norms) and independent optionality (disable hearsay → perfect-information NPCs, everything else works unchanged).

| Service | Question It Answers | Authoritative Over |
|---------|--------------------|--------------------|
| **Faction** | "What norms actually exist here?" | Social structure, territory, governance |
| **Obligation** | "What does it cost to violate known rules?" | Cost computation, personality weighting |
| **Hearsay** | "What does this NPC *think* they know?" | Information propagation, belief state, perception |

The pipeline with hearsay:
```
Faction (what IS true) ─────────────────────────────────► Convergence target
                                                              │
Hearsay (what NPC THINKS is true) ──► ${hearsay.*} ──► Actor cognition
                                                              │
Obligation (what it COSTS to violate) ── ${obligations.*} ────┘

Behavior authors choose:
  - ${obligations.*} only  → perfect-information moral reasoning (current behavior)
  - ${hearsay.*} only      → fuzzy social perception without formal costs
  - both                   → full pipeline with uncertainty and consequences
```

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified. Plugin is pre-implementation.*

### Intentional Quirks (Documented Behavior)

1. **Claim codes are opaque strings**: Not enums. Follows the same extensibility pattern as violation type codes, collection type codes, and seed type codes. `dangerous`, `trustworthy`, `cursed`, `profitable` are conventions, not constraints. Games define their own vocabulary.

2. **Beliefs are per-character, not shared**: Two NPCs standing next to each other may have completely different beliefs about the same subject. There is no "shared knowledge" store -- all knowledge is individual, propagated through social encounters.

3. **Compression does NOT restore beliefs**: When a compressed character is restored, they start with accelerated convergence, not their old beliefs. This simulates "returning after a long absence" -- they need to relearn the current state of affairs.

4. **Rumor distortion is configurable**: The telephone-game effect (`RumorDistortionFactor`) can be set to 0.0 for perfectly accurate rumor propagation or to 0.5 for heavily distorted rumors. Games choose their narrative flavor.

5. **Convergence requires soft dependencies**: Norm convergence needs Faction (to know actual norms). Character convergence needs Character-Encounter (to know actual sentiment). Without these soft dependencies, beliefs never converge -- they persist until they decay below threshold. This is acceptable: a game running without Faction has no norms to converge toward.

6. **Saturation events are location-scoped**: The saturation threshold considers NPCs within a radius of the belief subject's location. This means urban areas (many NPCs) reach saturation faster than rural areas, which naturally models how rumors spread faster in cities.

7. **Fear gradient is real-time computed**: `${hearsay.location.fear_gradient}` is not cached -- it's computed on each provider creation based on the character's current location and their believed-dangerous locations. This ensures the gradient updates as the character moves.

8. **Multiple beliefs about same subject are allowed**: A character can believe "Mira is dangerous" AND "Mira is a skilled healer" simultaneously. These are not contradictory -- they represent different facets of impression. The composite `reputation` score aggregates them.

### Design Considerations (Requires Planning)

1. **Encounter event schema**: The `character-encounter.created` event must include enough data for hearsay propagation (participant IDs, encounter context, witness list). Current event schema may need enhancement.

2. **Location awareness for convergence**: The convergence worker needs to know where characters are to apply proximity-based convergence. Character location tracking may not be real-time in all deployments. May need to use last-known-location from a periodic update.

3. **Scale considerations**: With 100,000+ NPCs, belief storage and propagation processing could become significant. Per-character belief limits (`MaxBeliefsPerCharacter`) and worker batch sizes are the primary safety valves. May need spatial partitioning for propagation waves.

4. **Storyline narrative protection**: Preventing convergence from correcting beliefs that an active storyline depends on requires hearsay to know about active storylines (soft dependency on lib-storyline). The boundary between "dramatic irony preservation" and "stale belief prevention" needs careful design.

5. **Variable provider performance**: The belief manifest cache must be fast enough for Actor's 100-500ms decision cycle. Pre-aggregation in Redis is the current plan. May need character-scoped sub-caching for frequently accessed beliefs (e.g., norm beliefs at current location).

6. **Personality integration depth**: How much should personality affect belief mechanics? Current plan: openness affects receptivity, conscientiousness affects verification tendency. Could extend to: neuroticism amplifies negative beliefs, agreeableness biases toward positive interpretations, etc. The mapping should be data-driven (like obligation's trait-to-violation mapping), not hardcoded.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Phase 0 prerequisite (EncountersProviderFactory bug fix) can be done independently. Core implementation phases 1-3 are self-contained. Storyline integration (phase 4) depends on lib-storyline's scenario system being further developed. Location beliefs (phase 5) and character beliefs (phase 6) can proceed in parallel with core infrastructure.*
