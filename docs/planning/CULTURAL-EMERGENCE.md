# Cultural Emergence: Divine Curation of Organic Identity and Custom

> **Status**: Vision Document (design analysis, no implementation)
> **Priority**: High (Living Game Worlds -- North Star #1, Emergent Over Authored -- North Star #5)
> **Related**: `docs/planning/SANCTUARIES-AND-SPIRIT-DENS.md`, `docs/planning/LOCATION-BOUND-PRODUCTION.md`, `docs/planning/COMPRESSION-GAMEPLAY-PATTERNS.md`, `docs/planning/DEATH-AND-PLOT-ARMOR.md`, `docs/reference/ORCHESTRATION-PATTERNS.md`
> **Services**: Divine, Faction, Obligation, Hearsay, Disposition, Actor, Puppetmaster, Workshop, Trade, Environment, Location, Collection, Item, Inventory, Contract, Currency, Worldstate, Seed, Agency
> **External Inspiration**: *Ascendance of a Bookworm* (Myne's coming-of-age ceremony, church citizenship rituals, class-differentiated customs), *By the Grace of the Gods* / *Blessed by the Gods* (status boards as cultural artifact), general isekai "status board" trope

---

## Executive Summary

A fishing village is more than a village that sustains itself off fish. It is a place where the inhabitants **know** they are a fishing village -- where that identity shapes their pride, their customs, their coming-of-age rituals, their relationships with neighboring settlements, and how strangers perceive them. This document describes the pattern by which settlement identities and cultural customs **emerge organically** from observed conditions and are **crystallized into shared reality** by divine actors -- requiring zero new services.

The pattern has three layers:

1. **Identity Crystallization**: A divine actor observes that objective conditions at a location have stabilized into a recognizable pattern (dominant production, geographic significance, factional influence, population density) and injects a **named identity** into the social fabric via Hearsay. The god does not invent the fishing village -- it *recognizes* it and gives it a name. Once named, the identity propagates as belief and begins to influence NPC behavior, trade relationships, and self-perception.

2. **Custom Generation**: A divine actor evaluates a settlement's identity, resources, governance structure, and cultural maturity, then creates **Faction norms** representing culturally appropriate practices. Coming-of-age gifts, harvest festivals, mourning rites, trade ceremonies, marriage customs, craft guild traditions -- these are norms with parameters selected from what is actually available and valued in the community.

3. **Artifact Determination**: For customs that involve physical objects (gifts, ceremonial items, status symbols), the divine actor's GOAP evaluation selects specific items based on local production, abundance, utility, and cultural significance. A fishing village gives children their first net. A scholarly city provides a status board. A martial outpost presents a weapon. The gift *fits* because it was derived from actual conditions, not randomly assigned.

The fundamental insight: **culture is divine curation of emergent reality**. The god does not author culture from nothing. It observes what the world has already produced, names it, and formalizes it. The resulting customs feel organic because they *are* organic -- they match what was already true.

---

## Inspiration: The Status Board and the Flower Offering

### The Isekai Status Board

Across dozens of isekai titles (*Blessed by the Gods*, *So I'm a Spider, So What?*, *Mushoku Tensei*, *Danmachi*), the "status board" is a cultural artifact -- a mechanism by which individuals can inspect their own capabilities, often received during a coming-of-age ceremony or through institutional registration (an adventurer's guild, a church, a god's blessing). The specific form varies:

- A physical card or stone tablet that displays stats when channeled
- A mental interface visible only to the individual
- A church-administered blessing that reveals divine assessment
- A guild-issued credential that doubles as identification

What makes this interesting is not the status board itself (Bannou's Agency UX capability manifest already represents self-knowledge mechanically) but the **cultural apparatus** surrounding it. The ceremony, the expectations, the social implications of what your board reveals, the customs about showing or hiding it -- these are cultural norms, not game mechanics.

### Ascendance of a Bookworm

*Ascendance of a Bookworm* provides the richest example of **class-differentiated customs** emerging from material conditions:

- **Commoner coming-of-age**: Children receive a simple woven flower wreath and a plain dress/tunic, made from whatever grows locally. The materials are humble because the community is humble. There is no ceremony beyond family acknowledgment.
- **Noble coming-of-age**: Children receive formal blessing from the church, citizenship documentation, and are presented at court. The materials are lavish because the institution is lavish.
- **Church coming-of-age**: Orphans raised by the temple receive only a citizenship card -- acknowledgment of adulthood with minimal personal investment, reflecting the institution's detachment.

The *same cultural concept* (marking the transition to adulthood) manifests completely differently based on economic class, institutional affiliation, and available resources. The customs are not random -- they are shaped by material reality. A poor community cannot give expensive gifts. A wealthy one would not give cheap ones. The gift reveals the giver as much as it honors the recipient.

### Design Lesson

The pattern is: **material conditions shape what customs are possible, and divine/institutional curation selects which possibilities become tradition**. A fishing village *could* give children anything -- but the divine actor, evaluating what is abundant, valued, and useful, selects "a fishing net" or "a carved fish amulet" because that is what fits. The custom then persists as a Faction norm, enforced through Obligation's GOAP cost modifiers, propagated through Hearsay as shared cultural knowledge.

---

## Part 1: Identity Crystallization

### What Settlement Identity Is

A settlement identity is a **Hearsay belief** with high confidence, broad propagation, and self-reinforcing feedback loops. It is not a flag or enum on a Location record. It is a social phenomenon -- what the inhabitants believe about themselves, what outsiders believe about the settlement, and how those beliefs shape behavior.

Examples of settlement identities:

| Identity | Observable Basis | Behavioral Consequence |
|---|---|---|
| "Fishing village" | Workshop: dominant fish processing; Trade: fish export routes; Location: coastal | NPCs specialize in maritime skills; children learn fishing; trade caravans seek fish; settlement attracts fishermen |
| "Mining town" | Workshop: ore extraction dominant; Environment: mineral-rich terrain; Trade: metal exports | Hard labor culture; smithing concentration; dust/health concerns; wealth inequality patterns |
| "Market crossroads" | Transit: high route connectivity; Trade: high volume diverse goods; Location: intersection geography | Cosmopolitan attitudes; inn/tavern concentration; information hub; cultural mixing |
| "Temple city" | Divine: high blessing density; Faction: theocratic governance; Collection: religious knowledge concentration | Pilgrim infrastructure; religious customs; divine economy prominence; ceremonial calendar |
| "Frontier outpost" | Location: border/edge position; Faction: military governance; Environment: harsh conditions | Martial culture; survival customs; suspicion of strangers; communal solidarity |

### How Identity Forms (The Divine Observer)

Identity does not appear spontaneously. It requires a divine actor to observe, evaluate, and name.

**Why divine actors?** Because identity crystallization requires cross-domain synthesis that no single NPC possesses. An individual fisherman knows he fishes. A merchant knows the town exports fish. A child knows the sea smells. But recognizing "this settlement's dominant economic activity, geographic position, population trends, and social structures combine to form a coherent identity" requires the omniscient perspective that only a regional watcher god possesses -- reading `${location.*}`, `${trade.*}` (planned), `${workshop.*}` (planned), `${faction.*}`, and `${environment.*}` simultaneously.

**The observation phase** (conceptual ABML):

```yaml
# Regional watcher evaluates settlements for identity crystallization
flow: evaluate_settlement_identity
  trigger:
    periodic: true
    interval_game_days: 90  # Quarterly evaluation

  conditions:
    # Settlement must be established enough to have an identity
    - ${location.population} > 50
    - ${location.age_game_years} > 5
    # No existing strong identity belief
    - ${hearsay.settlement_identity.confidence} < 0.3 OR null

  evaluate:
    # Gather objective indicators
    dominant_production: query workshop output categories for location
    trade_significance: query transit route volume through location
    geographic_character: query environment biome and terrain
    governance_type: query faction governance structure
    blessing_density: query divine blessing concentration

  decide:
    # GOAP evaluates which identity pattern best fits observations
    # Multiple identity archetypes scored against observed conditions
    goal: crystallize_settlement_identity
    actions:
      - score_identity_fit: { archetype: "fishing_village", weight: production_marine }
      - score_identity_fit: { archetype: "mining_town", weight: production_mineral }
      - score_identity_fit: { archetype: "market_crossroads", weight: trade_volume }
      - score_identity_fit: { archetype: "temple_city", weight: blessing_density }
      - score_identity_fit: { archetype: "frontier_outpost", weight: border_proximity }
      # ... extensible via seed data, not hardcoded
```

**The naming act** -- the divine actor injects the identity into the social fabric:

```yaml
  act:
    # Inject identity as a high-confidence belief via Hearsay
    - service_call:
        target: hearsay
        method: inject_belief
        params:
          subject_type: "location"
          subject_id: ${location.id}
          belief_key: "settlement_identity"
          belief_value: ${decided_identity}
          confidence: 0.9
          channel: "cultural_osmosis"  # Slow spread, deep roots
          propagation_radius: regional

    # Also create/strengthen faction identity norm
    - service_call:
        target: faction
        method: norm/create
        params:
          faction_id: ${location.governing_faction}
          norm_code: "settlement_identity"
          norm_value: ${decided_identity}
          enforcement: "social"  # Peer pressure, not law
```

### Identity Reinforcement and Drift

Once crystallized, a settlement identity is self-reinforcing but not permanent:

**Reinforcement loop**: Identity → NPC behavior alignment → more production/activity matching identity → stronger objective basis → divine actor confirms identity → higher Hearsay confidence → stronger identity.

**Drift conditions**: If the objective basis changes (mines run dry, trade routes shift, a plague kills the fishing fleet), the divine actor's next evaluation may find the identity no longer fits. The response is not instant replacement but gradual erosion -- Hearsay confidence decays as the belief stops matching observed reality, and NPCs who can see the discrepancy begin to doubt. Eventually the old identity fades and conditions are ripe for a new one.

**Contested identity**: Two divine actors with different aesthetic preferences might disagree on what a settlement "is." A settlement with both mining and farming might receive "mining town" from Typhon and "agricultural commune" from Silvanus. The resolution is Hearsay propagation competition -- whichever identity has stronger objective support and more believers wins the cultural consensus. This is emergent conflict, not hardcoded resolution.

---

## Part 2: Custom Generation

### What a Custom Is

A custom is a **Faction norm** with specific parameters that define a recurring cultural practice. In the service model:

| Custom Component | Service | Representation |
|---|---|---|
| The practice itself | Faction norm | Norm code, parameters, enforcement level |
| Individual compliance motivation | Obligation | GOAP action cost modifier (violating custom has social cost) |
| Shared knowledge of custom | Hearsay | Belief: "in our village, children receive X at age Y" |
| Emotional investment | Disposition | Drive: "participate in community traditions" |
| Physical manifestation | Item + Inventory | The gift, the ceremonial object, the artifact |
| Lifecycle trigger | Contract | Coming-of-age as milestone (age threshold reached) |
| Institutional backing | Faction governance | Who funds it, who administers it |

### Categories of Customs

Divine actors generate customs across several categories, each triggered by different settlement maturity conditions:

#### Coming-of-Age Customs

**Trigger**: Settlement has stable population with children reaching maturity age, governing faction with social cohesion above threshold.

**Parameters the divine actor selects**:

| Parameter | Options | Selection Basis |
|---|---|---|
| **Gift type** | Practical tool, symbolic artifact, credential/document, blessing/status, none | Settlement wealth, identity, governance |
| **Gift source** | State-provided, family-provided, community-pooled, self-earned | Governance type, economic model |
| **Ceremony type** | Public celebration, private family event, institutional registration, religious rite | Faction type, divine influence level |
| **Uniformity** | All receive identical gift, gift personalized to recipient, recipient chooses | Cultural values (collectivism vs. individualism) |
| **Prerequisite** | Age only, age + trial/test, age + sponsorship, age + contribution | Martial/scholarly/economic culture |

**Example outcomes by settlement identity**:

| Settlement | Gift | Source | Ceremony | Basis |
|---|---|---|---|---|
| Fishing village | First fishing net | Family-crafted | Dawn ceremony at the docks, child casts first net into the sea | Maritime identity, family craft tradition, practical value |
| Mining town | Iron-cap helmet and pickaxe token | Guild-provided | Registry at the miners' guild hall | Guild governance, danger acknowledgment, institutional identity |
| Temple city | Blessing stone (Collection unlock: self-inspection) | Church-administered | Temple ceremony with divine invocation | Theocratic governance, divine economy, the "status board" variant |
| Market crossroads | Trader's seal and first coin purse (Currency wallet creation) | Community fund | Market square celebration with merchant sponsors | Commercial identity, economic participation |
| Frontier outpost | Short blade and garrison assignment | State-issued | Military muster and oath-taking | Martial governance, survival necessity |
| Scholarly city | Apprenticeship contract and personal journal (Collection access) | Academy | Examination and presentation of research topic | Knowledge culture, institutional mentorship |

#### Harvest and Seasonal Customs

**Trigger**: Workshop agricultural output follows seasonal cycles (via Worldstate calendar), settlement has experienced at least 3 successful harvests.

The divine actor evaluates the peak production period and creates a Faction norm marking a celebration tied to it. The specific form depends on what is harvested (grain festival vs. grape festival vs. fishing season end), who participates (entire settlement vs. farming families only), and what the celebration involves (feasting, competition, offering to gods, trade fair).

#### Mourning and Memorial Customs

**Trigger**: Character death frequency in a settlement exceeds a threshold within a time window, OR a high-significance character dies (settlement leader, beloved elder, war hero).

Custom parameters include: mourning period duration, memorial practices (burial, cremation, sky burial -- based on species and terrain), community obligations during mourning, and memorial item creation (via Item templates derived from the deceased's profession or significance).

#### Trade and Diplomatic Customs

**Trigger**: Transit route significance between two settlements crosses threshold, OR two factions establish formal relations via Contract.

Custom parameters include: greeting protocols, gift exchange norms (what is considered an appropriate diplomatic gift between these specific settlements -- always derived from what each produces), trade fair timing, and hospitality obligations.

#### Craft and Professional Customs

**Trigger**: Workshop specialization concentration produces a critical mass of practitioners in a single craft category at one location.

Custom parameters include: guild formation norms, apprenticeship structures (formal contract vs. informal mentorship), quality marking traditions, and professional coming-of-age (journeyman's piece, masterwork requirement).

#### Marriage and Union Customs

**Trigger**: Relationship bond density at a settlement reaches threshold, settlement has stable governance with social norms.

Custom parameters include: courtship protocols, bride/groom price or dowry customs (derived from economic model), ceremony format, and inter-settlement marriage rules (endogamy vs. exogamy based on settlement size and connectivity).

---

## Part 3: The Status Board Pattern

The isekai "status board" is one specific outcome of the coming-of-age custom generation system, produced when a settlement's conditions align:

### Preconditions

For a status board custom to emerge, the divine actor needs to observe:

1. **Theocratic or knowledge-oriented governance**: The settlement values systematic understanding (temple city, scholarly city, advanced civilization)
2. **Divine blessing infrastructure**: The Collection and Agency services are actively granting perception capabilities to inhabitants (the mechanical basis for self-inspection exists)
3. **Institutional capacity**: A faction with the authority and resources to administer a formal ceremony and maintain records
4. **Cultural value of self-knowledge**: Settlement identity emphasizes growth, learning, or spiritual development (not pure survival or commerce)

### What the "Status Board" Actually Is

In Bannou terms, the status board is **not a new item or mechanic**. It is a **cultural framing of existing systems**:

| Status Board Feature | Bannou Mechanism |
|---|---|
| "See your stats" | Agency UX capability manifest (guardian spirit's accumulated understanding rendered as perceivable data) |
| "See your skills" | Seed domain growth depths (combat.melee.sword: 1.8) surfaced through Agency |
| "See your level" | Seed phase progression (Dormant → Stirring → Awakened) |
| "Show others your board" | Collection entries that are publicly queryable vs. private |
| "The board itself" | An Item instance (stone tablet, crystal, blessed parchment) that serves as the cultural vessel for the ceremony |
| "Church administers it" | Contract milestone: coming-of-age ceremony performed by faction authority |
| "Registration grants citizenship" | Faction membership created as part of the Contract's prebound API execution |

The divine actor does not create a "status board system." It creates a **custom** where the coming-of-age ceremony includes receiving an item that culturally represents access to self-knowledge that the game systems already provide. The item is a cultural symbol, not a mechanical unlock.

### Varieties of the Pattern

The same underlying mechanism (self-inspection + cultural ceremony) manifests differently based on settlement conditions:

| Context | Cultural Form | Mechanical Basis |
|---|---|---|
| Theocratic temple city | "Divine Blessing Stone" received in church ceremony | Collection unlock: spiritual_perception + Item: blessed stone |
| Scholarly academy | "Scholar's Lens" earned through examination | Collection unlock: analytical_perception + Item: crystal lens |
| Adventurer's guild | "Guild Card" issued on registration with displayed rank | Faction membership + Seed phase displayed on Item |
| Tribal society | "Spirit Walk" vision quest revealing inner self | Contract: solo trial + Agency perception unlock (no physical item) |
| Military state | "Service Dossier" issued at conscription | Faction membership + Item: official document |
| No formal system | Self-knowledge emerges gradually through experience | Agency's default progressive unlock (no ceremony, no item) |

The last row is critical: **the status board custom is not required for the mechanics to work**. Agency provides progressive self-knowledge regardless. The custom is cultural packaging -- it makes the mechanical truth into a shared social experience. Settlements without the custom still have inhabitants who gradually understand themselves; they just don't have a ceremony about it.

---

## Part 4: The Divine Evaluation Engine

### GOAP-Driven Custom Selection

The divine actor's custom generation is not a lookup table. It is GOAP planning where the goal is "this settlement should have culturally appropriate customs" and the actions are "create norm X with parameters Y":

**World state inputs**:
- `${location.*}`: Geography, population, age, child count approaching maturity
- `${faction.*}`: Governance type, economic model, social cohesion, existing norms
- `${workshop.*}` (planned variable provider): Dominant production categories, seasonal patterns, specialization concentration
- `${trade.*}` (planned variable provider): Route significance, trade volume, partner settlements
- `${environment.*}`: Biome, climate, seasonal extremes, resource availability
- `${hearsay.settlement_identity.*}`: Current identity beliefs and confidence
- `${divine.blessing_density}`: Religious infrastructure presence
- `${seed.*}`: Cultural maturity (settlement governance seed growth)

**Goal**: Settlement has culturally coherent customs matching its identity and material conditions.

**Actions available to the planner**:
- `create_norm`: Define a new Faction norm with parameters
- `inject_belief`: Propagate knowledge of the custom via Hearsay
- `create_template`: Define an Item template for ceremonial objects
- `sponsor_ceremony`: Fund the first instance of the custom via Currency
- `model_custom`: Create a Contract template for the ceremony lifecycle

**Constraints**:
- Gift items must be producible from locally available materials (Workshop can make them)
- Ceremony must be administrable by existing governance structure (Faction has the authority)
- Custom must be sustainable without ongoing divine investment (self-reinforcing once established)
- Custom must not contradict existing norms (Obligation consistency check)

### God Personality Shapes Culture

Different divine actors produce different cultural flavors from the same objective conditions:

| God | Cultural Aesthetic | Custom Tendency |
|---|---|---|
| **Moira / Fate** | Cyclical, ritual, emphasizes transitions | Elaborate ceremony, prophecy elements, destiny-themed gifts |
| **Silvanus / Forest** | Natural, organic, seasonal | Nature-aligned gifts, seasonal timing, outdoor ceremonies |
| **Ares / War** | Martial, trial-based, proving | Combat trial prerequisite, weapon gifts, oath-taking |
| **Hermes / Commerce** | Practical, transactional, social | Practical gifts, market-timed ceremonies, community-funded |
| **Thanatos / Death** | Somber, memorial, ancestral | Ancestor-honoring elements, inheritance themes, remembrance |
| **Typhon / Monsters** | Survival, danger-aware, protective | Protective gifts, danger-awareness customs, warding traditions |

The same fishing village might develop:
- Under Silvanus: A dawn ceremony where children weave their first net from sacred reeds and cast it into the sea as an offering
- Under Hermes: A practical affair where the guild master presents a quality net and the child's first market stall assignment
- Under Ares: A trial where the child must catch their first fish alone in open water before receiving their adult name

**The settlement's identity is the same. The cultural expression varies by divine personality.** This is not randomness -- it is the aesthetic preference of whichever god's domain encompasses the settlement, producing different but equally valid cultural expressions of the same material reality.

---

## Part 5: Custom Lifecycle

### Establishment

1. Divine actor evaluates settlement conditions (GOAP planning)
2. Divine actor creates Faction norm with parameters
3. Divine actor injects knowledge of custom into Hearsay
4. Divine actor optionally sponsors first ceremony (Currency, Item creation)
5. Contract template registered for ceremony lifecycle

### Propagation

6. Hearsay spreads knowledge of custom through settlement (cultural osmosis channel -- slow, deep)
7. Obligation service creates GOAP cost modifiers for non-compliance ("everyone does this; not doing it is socially costly")
8. Disposition service: NPC drives align with community participation
9. First generation of recipients experience the custom
10. Their positive/negative reactions feed back into Hearsay (reinforcing or weakening)

### Maturation

11. Custom becomes "the way things are done" -- high Hearsay confidence, low Obligation cost to comply
12. Variations emerge as individual families add their own touches (within norm parameters)
13. Professional customs spawn secondary norms (guild customs, apprenticeship standards)
14. Outsiders learn of the custom through Trade/Transit contact (Hearsay inter-settlement propagation)

### Evolution

15. Material conditions change (new resources, depleted resources, population shift)
16. Divine actor re-evaluates: custom still fits? Parameters need adjustment?
17. Gradual parameter drift (gift type evolves as technology/resources change)
18. Settlement identity shift may trigger custom overhaul over generations

### Obsolescence

19. If material basis disappears entirely (fishing village loses access to sea due to geological event)
20. Custom becomes increasingly disconnected from reality
21. Hearsay confidence in the custom slowly decays
22. Two outcomes: **tradition** (custom persists as heritage despite changed conditions -- "we still give nets even though we farm now, because that's who we were") or **replacement** (divine actor creates new custom matching new reality)

The **tradition outcome** is particularly interesting -- it is how cultural identity outlives its material basis. A former fishing village that became a farming community but still gives children nets at their coming-of-age is richer and more believable than one that instantly updated its customs. The lag between reality and culture is where character and history live.

---

## Part 6: Service Composition (No New Plugins Required)

Every element of cultural emergence maps to existing services:

| Element | Service | Mechanism |
|---|---|---|
| Identity observation | Actor (L2) | Divine actor reads variable providers for settlement data |
| Identity naming | Hearsay (L4) | Belief injection: `settlement_identity` with high confidence |
| Custom definition | Faction (L4) | Norm creation with typed parameters |
| Compliance motivation | Obligation (L4) | GOAP cost modifiers for norm violation |
| Knowledge propagation | Hearsay (L4) | Cultural osmosis channel for slow, deep spread |
| Emotional investment | Disposition (L4) | Drives aligned with community belonging |
| Ceremony lifecycle | Contract (L1) | Milestone-based ceremony with prebound actions |
| Physical gifts | Item + Inventory (L2) | Templates for ceremonial items, placed on completion |
| Gift determination | Actor GOAP (L2) | Divine actor evaluates what is abundant/valued/useful |
| Gift funding | Currency (L2) | Community fund, family contribution, or state provision |
| Self-knowledge framing | Agency (L4) | UX capability manifest as cultural artifact |
| Record-keeping | Collection (L2) | Ceremony participation as permanent record |
| Seasonal timing | Worldstate (L2) | Calendar system for ceremony scheduling |
| Inter-settlement customs | Transit (L2) + Trade (L4) | Route significance triggers diplomatic customs |

### Required Variable Providers (Status)

| Provider | Status | Needed For |
|---|---|---|
| `${personality.*}` | Implemented | God aesthetic preferences |
| `${faction.*}` | Implemented | Governance type, existing norms |
| `${location.*}` | Implemented | Geography, population |
| `${world.*}` | Implemented | Calendar, seasons |
| `${hearsay.*}` | Planned | Existing beliefs, identity confidence |
| `${workshop.*}` | Not yet planned | Dominant production, specialization |
| `${trade.*}` | Not yet planned | Route significance, trade volume |
| `${environment.*}` | Planned | Biome, climate, resources |

The `${workshop.*}` and `${trade.*}` variable providers are the most significant gaps for this pattern. Without them, divine actors cannot evaluate production dominance or trade significance -- the two most important inputs for settlement identity crystallization. These should be added to the variable provider roadmap as dependencies of the cultural emergence pattern.

---

## Part 7: Broader Pattern Recognition

Cultural emergence is not a single feature. It is a **pattern category** of divine behavior -- the general capability of divine actors to observe emergent conditions and formalize them into shared cultural reality. The coming-of-age custom is one instance. The full pattern generates:

| Custom Category | Trigger Conditions | Settlement Impact |
|---|---|---|
| **Coming-of-age** | Stable population, children approaching maturity | Identity reinforcement, intergenerational continuity |
| **Harvest festival** | Seasonal production peaks (Worldstate + Workshop) | Community cohesion, seasonal economic rhythm |
| **Mourning rites** | Death frequency or high-significance death | Grief processing, memorial infrastructure, content flywheel input |
| **Trade ceremony** | Transit route significance threshold | Inter-settlement relations, diplomatic infrastructure |
| **Marriage customs** | Relationship density threshold | Social structure formalization, family economics |
| **Craft traditions** | Workshop specialization concentration | Professional identity, quality standards, apprenticeship |
| **Religious observance** | Divine blessing density + calendar alignment | Spiritual infrastructure, divine economy participation |
| **Justice customs** | Faction governance maturity + Obligation density | Dispute resolution norms, Arbitration integration |
| **Hospitality norms** | Transit connectivity + outsider frequency | Tourism infrastructure, stranger treatment customs |
| **Memorial traditions** | Compression archive density at location | Ancestor veneration, content flywheel amplification |

Each category follows the same pattern: divine observation of material conditions → GOAP evaluation of appropriate customs → Faction norm creation → Hearsay propagation → Obligation enforcement → cultural self-reinforcement.

The divine actor's ABML behavior document for cultural curation would be a large, rich document -- but it is a **behavior document**, not code. A game developer who wants different cultural emergence patterns for their game world writes different ABML behaviors for their divine actors. The platform provides the primitives; the behaviors provide the culture.

---

## Summary

Culture in Bannou is not designed. It is **observed, named, and formalized** by divine actors who possess the cross-domain perspective to recognize what a settlement has already become. The customs that emerge match their context because they are derived from it. The status board is not a feature -- it is one possible cultural artifact that appears when material conditions, governance structures, and divine aesthetics align to produce a society that values formalized self-knowledge.

No new services. No new plugins. Just divine actors doing what they do: watching the world, understanding what it has become, and giving it the cultural vocabulary to know itself.

---

*This document describes design patterns for cultural emergence in Bannou game worlds. For the divine actor system, see [DIVINE.md](../plugins/DIVINE.md). For the behavioral bootstrap sequence, see [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md). For the content flywheel, see [COMPRESSION-GAMEPLAY-PATTERNS.md](COMPRESSION-GAMEPLAY-PATTERNS.md). For the NPC intelligence stack, see [VISION.md](../reference/VISION.md).*
