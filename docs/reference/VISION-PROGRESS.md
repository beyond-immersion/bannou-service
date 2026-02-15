# Vision Progress: Cross-Service Architectural Audit

> **Version**: 1.1
> **Last Updated**: 2026-02-15
> **Scope**: High-level alignment between VISION.md, PLAYER-VISION.md, BEHAVIORAL-BOOTSTRAP.md, and the 75-service plugin architecture
> **Purpose**: Capture disconnects, resolved patterns, and open questions before proceeding to aspirational plugin specification

This document records the results of a full cross-service audit comparing the vision documents against the generated service details and deep dive documentation. Issues are categorized as resolved (with architectural rationale), open (with priority and impact), or questions (requiring human judgment).

---

## Methodology

Three investigation axes were pursued across `docs/plugins/` deep dives (documentation only, not source code):

1. **Economy Chain**: Currency, Item, Inventory, Escrow, Trade, Market, Craft, Workshop, Loot, Affix, Organization -- item/currency flow consistency, NPC economic decision-making, hierarchy compliance
2. **NPC Cognition Pipeline**: Actor, Behavior, Disposition, Ethology, Hearsay, Obligation, Lexicon, Agency, Character-Personality, Character-Encounter, Worldstate -- variable provider completeness, overlap analysis, behavioral bootstrap alignment
3. **Content Flywheel**: Resource, Storyline, Character-Lifecycle, Character-History, Realm-History, Gardener, Seed, Collection, Divine, Puppetmaster, Status, Dungeon, Faction -- archive-to-content pipeline, seed growth consistency, generational cycle integration

Findings were evaluated against the five North Stars (VISION.md), the seven Design Principles (VISION.md), and the player experience gradient model (PLAYER-VISION.md).

---

## Key Architectural Pattern: System Realms

A critical pattern that resolved multiple issues is the **System Realm pattern** documented in [DIVINE.md: God Characters in System Realms](../plugins/DIVINE.md#god-characters-in-system-realms). Realm's `isSystemType` flag creates conceptual namespaces for entities that exist outside physical reality.

### The Pattern

When an entity exists as a Character in a system realm, the entire L2/L4 entity stack activates with zero new infrastructure:

| Existing Service | What System Realm Characters Get |
|---|---|
| **Character** (L2) | Identity record, realm binding, species association, alive/dead lifecycle |
| **Species** (L2) | Type taxonomy within the system realm |
| **Relationship** (L2) | Bonds between system realm characters (genealogy, rivalry, pair bonds) |
| **Character Personality** (L4) | Quantified personality traits, available as `${personality.*}` |
| **Character History** (L4) | Historical events and backstory |
| **Character Encounter** (L4) | Memories of notable interactions |
| **Seed** (L2) | Progressive growth primitives, seed bonds |
| **Collection** (L2) | Content unlock and archive access |
| **Actor** (L2) | Character brain with full variable provider chain |

### Planned System Realms

| System Realm | Purpose | Status |
|---|---|---|
| **PANTHEON** | God characters, divine genealogy, deity personality, domain power | Documented in DIVINE.md (Extension #0 -- core pattern) |
| **NEXIUS** | Guardian spirit characters, pair bonds, spirit evolution, spirit personality | Documented in DIVINE.md (Extension #13) |
| **UNDERWORLD** | Dead characters post-mortem, afterlife gameplay, cross-boundary narrative | Documented in DIVINE.md (Extension #12) |
| **VOID** | Already exists as seeding convention; could house void-specific entities | Mentioned in DIVINE.md |

### Why This Matters

The system realm pattern means metaphysical entities (gods, guardian spirits, the dead) are not special cases requiring new infrastructure. They are characters in conceptual spaces, and every service that works with characters works with them automatically. This eliminates entire categories of "how does X integrate with Y" questions.

---

## Resolved Issues

### R1. Guardian Spirit Growth Gap

**Original concern**: No documented mechanism for player responses to character death to feed guardian spirit seed growth. The Fulfillment Principle ("more fulfilled in life = more logos flow to the guardian spirit") had no implementation path.

**Resolution**: The Nexius system realm pattern. If the guardian spirit IS a Character in the Nexius realm with a Seed:

- Gardener awards Collection entries when players complete scenarios, make meaningful choices, or respond to character death
- Collection unlocks trigger `ICollectionUnlockListener` -> `SeedCollectionUnlockListener`
- Guardian seed grows through the **same pipeline** that Faction, Divine, and Dungeon already use
- The Fulfillment Principle becomes a tag-mapping configuration: character-history participation events map to guardian seed growth domains (`wisdom`, `resilience`, `loss-processing`, etc.)

No new plumbing required. The Collection -> Seed pipeline is the answer.

### R2. Twin Spirits (Pair System) Service Assignment

**Original concern**: PLAYER-VISION.md describes twin spirits with innate communication, linked households, shared void experience, and discovery awareness. No service claimed ownership of pair bond mechanics.

**Resolution**: The Nexius system realm pattern. Every requirement maps to an existing L2 service:

| Pair Bond Requirement | Implementation |
|---|---|
| Pair bond entity | Relationship between two Nexius characters (type: `twin_spirits`) |
| Communication channel | Seed bond between guardian seeds -- strength grows with shared experience |
| Shared void experience | Gardener reads relationship data, coordinates paired gardener-gods |
| Discovery/proximity awareness | Relationship query -- always know where your pair's characters are |
| Linked households | Relationship between the Nexius characters' possessed physical-realm characters |

No new service needed. The pair bond is a relationship type code + a seed bond, same as divine inter-deity bonds.

### R3. Agency's Concrete Data Source

**Original concern**: Agency provides `${spirit.*}` variables but the source of concrete data was unclear.

**Resolution**: With a Nexius character, Agency reads:
- Guardian spirit's **Seed** (capability domains, growth phase)
- Guardian spirit's **Character Personality** (spirit personality evolution over lifetimes)
- Guardian spirit's **Character Encounter** (memories of possessed characters, notable interactions)

DIVINE.md Extension #13 states: "Would give the Agency service concrete character data to work with for manifest computation."

### R4. NPC Cognition Layer Differentiation

**Original concern**: With 13+ variable provider namespaces across the cognition pipeline, potential overlaps between Disposition/Personality, Hearsay/Encounters, and Lexicon/Ethology could create confusion.

**Resolution**: Investigation confirmed all layers are clearly orthogonal:

| Concern | Service A | Service B | Boundary |
|---|---|---|---|
| Self-model vs. directed emotions | **Personality**: "What kind of person am I?" (undirected traits) | **Disposition**: "How do I feel about entity X?" (directed feelings) | Personality is input to Disposition's synthesis, not overlapping |
| Factual memories vs. subjective beliefs | **Encounters**: What happened (raw sentiment, time, participants) | **Hearsay**: What I *think* I know (may differ from truth, decays, can be manipulated) | Encounters feed Hearsay's base values; Hearsay adds confidence and convergence |
| Species behavior vs. world knowledge | **Ethology**: "How does this species behave?" (behavioral baselines) | **Lexicon**: "What IS this thing?" (traits, categories, strategies) | Ethology = `${nature.territoriality}`; Lexicon = `${lexicon.wolf.trait.ground_bound}` |

### R5. Economy Item Flow Consistency

**Original concern**: Whether Trade, Market, Workshop, Loot, Craft, and Affix describe consistent patterns for item flow.

**Resolution**: All economy services follow a highly consistent orchestration pattern:

- Services define **templates** (loot tables, recipes, blueprints, catalogs, affix definitions)
- Services generate/materialize **instances** via orchestration
- Services write to L2 storage (Item, Inventory, Currency)
- Services publish events for analytics/economy feedback

NPC economic decision-making has a clear hierarchy: NPCs make discrete economic decisions (craft, restock, claim loot) through GOAP planning, informed by variable providers exposed by each service. Higher-layer services (Market, Craft, Loot, Workshop) provide data; lower-layer services (Item, Currency, Inventory) execute transactions.

### R6. Seed Growth Pipeline Consistency

**Original concern**: Whether the Seed growth pipeline works uniformly across its consumers.

**Resolution**: All consumers follow the same pattern:

| Service | Seed Type | Growth Trigger | Growth Domain |
|---|---|---|---|
| **Faction** | `faction` | Collection unlocks (governance, commerce, military tags) | Governance, territory, economic power |
| **Divine** | `deity_domain` | Analytics score updates (domain-relevant) | War, knowledge, commerce (per deity) |
| **Agency** | `guardian` | Account-level collection unlocks | Spirit manifestation capability |
| **Dungeon** | `dungeon_core` / `dungeon_master` | Monster kills, memory captures, master commands | Mana reserves, genetic library, domain expansion |

All use the Collection -> `ICollectionUnlockListener` -> Seed `RecordGrowthAsync` pipeline. No ad-hoc growth sources within L4 services (the only exception is analytics-driven divinity generation, which feeds Currency, not Seed directly).

### R7. Character-Lifecycle Aging Mechanism

**Original concern**: Whether aging is triggered by a background worker or worldstate events, and whether it's consistently described.

**Resolution**: Character-Lifecycle aging is event-driven via **Worldstate temporal boundary events**, not a background worker:

1. Worldstate publishes `worldstate.season.changed` or `worldstate.year.changed`
2. Character-Lifecycle subscribes, queries living characters with age tracking enabled
3. Applies age ticks, checks phase transitions (infant -> child -> adolescent -> adult -> elder)
4. Publishes `character.lifecycle.phase-changed` for downstream consumers

This ensures multi-instance consistency and makes death/birth rates tunable per game-time ratio.

### R8. Resource-Managed Cleanup Compliance

**Original concern**: Whether economic entities and character-dependent data clean up correctly.

**Resolution**: All L4 services correctly use lib-resource `x-references` cleanup callbacks (per T28) instead of event subscriptions. No L4 service subscribes to `character.deleted`. Resource orchestrates cleanup atomically via prebound APIs with distributed locks.

### R9. The Combat Dream: Cinematic Plugin + ABML as Universal Authoring Language

**Original concern** (formerly O2): VISION.md describes the "Combat Dream" (Event Brain -> Mapping affordances -> Character capabilities -> Cinematic Interpreter -> Three-Version Temporal Desync) but no service owns cinematic interpretation, combat choreography, or temporal desync.

**Resolution**: The answer emerges from two insights:

**Insight 1: Combat decisions are behavioral, but choreography is a distinct computational domain.**

[WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md](../faqs/WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md) correctly argues that combat *decisions* are behavioral compositions (Actor + variable providers + GOAP). But the *choreography* -- "wind up, fake left, strike right, follow through" -- is a different temporal scale (5-30s real-time sequences vs. 100-500ms cognitive ticks) and a different computational domain (dramatic composition, not decision-making).

This parallels existing domains exactly:

| Domain | SDK (Pure Computation) | Plugin (API Wrapper) | ABML Decides | SDK Expands Into |
|---|---|---|---|---|
| **Music** | MusicTheory + MusicStoryteller | lib-music (L4) | "play music for this mood" | Full composition (MIDI-JSON) |
| **Narrative** | StorylineTheory + StorylineStoryteller | lib-storyline (L4) | "compose a story from this archive" | Narrative plan with phases |
| **Spatial** | VoxelBuilder | consumed by lib-procedural | "expand this dungeon wing" | Voxel geometry |
| **Choreography** | CinematicTheory (extension of behavior-compiler SDK) | **lib-cinematic (L4, new)** | "initiate combat encounter" | Choreographic sequence with QTE windows |

**Insight 2: ABML is the universal authoring language, not just a behavior language.**

ABML already serves multiple execution modes through the existing SDK and shared infrastructure:

| Execution Mode | Where It Lives | Used For | Temporal Scale |
|---|---|---|---|
| **Compiled bytecode** (BehaviorModelInterpreter) | `sdks/behavior-compiler/Runtime/` | Performance-critical NPC behavior loops where higher-level decisions set inputs | 100-500ms ticks |
| **Step-through document** (DocumentExecutor) | `bannou-service/Abml/Execution/` | Complex behaviors requiring variable providers and GOAP planning | Seconds to hours |
| **Cinematic streaming** (CinematicInterpreter) | `sdks/behavior-compiler/Runtime/` | Streaming choreographic composition with continuation points | 5-30s real-time |

The CinematicInterpreter **already exists** in the behavior-compiler SDK. It wraps BehaviorModelInterpreter with streaming composition and continuation point support. What's missing is:

1. **Higher-level composition logic**: affordance evaluation, capability matching, dramatic pacing, agency-gated QTE insertion -- the "CinematicComposer" layer that takes encounter context and produces authored ABML cinematic documents
2. **lib-cinematic plugin**: thin API wrapper so god-actors and Event Brain actors can call `/cinematic/compose`, `/cinematic/extend`, and `/cinematic/resolve-input` from their ABML behaviors

The plugin would be a thin orchestration layer (same pattern as lib-music wrapping MusicTheory):

| Endpoint | What It Does | Called By |
|---|---|---|
| `/cinematic/compose` | Participants + environment + constraints -> choreographic sequence | Event Brain actors via ABML |
| `/cinematic/extend` | Existing sequence + continuation point + new context -> extension | Event Brain actors mid-encounter |
| `/cinematic/resolve-input` | QTE definition + player input -> outcome + branch selection | Gardener (routing player influence into active cinematic) |
| `/cinematic/list-templates` | Available action templates for a capability set | Behavior authoring tools |

**Connection to Progressive Agency**: QTE/decision points in cinematics ARE the combat domain of Agency's progressive agency:

| Spirit Fidelity | Combat UX | Cinematic System Behavior |
|---|---|---|
| None (new spirit) | Watch character fight autonomously | Full choreography, zero QTEs |
| Low | Occasional "approach/retreat" nudges | Rare, simple decision points |
| Medium | Timing windows appear | QTEs with moderate windows |
| High | Stance + combo direction | Rich decision points, tight windows |
| Master | Full martial choreography | Dense QTE sequences, style direction |

The cinematic plugin reads `${spirit.domain.combat.fidelity}` (from Agency) to determine QTE density -- same choreography computation, different interaction windows based on the spirit's earned understanding. This directly implements the PLAYER-VISION.md gradient.

**ABML as universal authoring format**: Beyond cinematics, ABML documents can author:

| Content Type | Execution Backend | Consumer |
|---|---|---|
| NPC behaviors | Bytecode VM (Actor runtime) | lib-actor |
| Choreography/timelines | CinematicInterpreter (streaming) | lib-cinematic (new) |
| Dialogue/scripted exchanges | DocumentExecutor (step-through) | Any plugin via bannou-service |
| Story scenarios | DocumentExecutor | lib-storyline |
| Music composition parameters | DocumentExecutor | lib-music |
| Dialing plans (SIP/voice) | DocumentExecutor | lib-voice |

The key architectural property: **DocumentExecutor is in `bannou-service/`** (shared code), not in any plugin. Any service can execute ABML documents without depending on lib-behavior or lib-actor. The bytecode VM and CinematicInterpreter are in the behavior-compiler SDK, also independently referenceable. This means ABML authoring and execution are already decoupled from any specific plugin.

### R10. Event Brain Actor Type Definition

**Original concern** (formerly O8): Event Brain actors appear in Vision, Dungeon, and Behavioral Bootstrap docs but aren't formally defined in Actor or Behavior.

**Resolution**: The investigation revealed that Event Brain is not a separate actor type but a **behavioral pattern** -- a character brain actor with extended perception scope. God-actors with the system realm pattern are character brain actors (bound to their divine character) that can also use `load_snapshot:` for ad-hoc data about arbitrary entities. The CinematicInterpreter already exists in the SDK to handle the streaming choreographic composition that Event Brains perform.

The documentation gap is real (Actor's deep dive should describe this pattern), but the implementation architecture is sound. Event Brain = character brain + global event perception + load_snapshot + CinematicInterpreter for choreographic output. No new actor type needed.

---

## Open Issues

### O1. Divine Is Completely Stubbed [CRITICAL]

**Priority**: P0 -- blocks the behavioral bootstrap, content flywheel orchestration, and the system realm proof-of-concept

**Details**: All 22 Divine endpoints return `NotImplemented`. The behavioral bootstrap requires Divine for deity creation (Phase 3), divinity economy, blessing orchestration, and avatar manifestation. The system realm pattern (which resolves R1, R2, R3) depends on Divine's implementation proving out the concept that other system realms (Nexius, Underworld) can follow.

**Impact**: Blocks North Star #1 (Living Game Worlds), #2 (Content Flywheel), and #5 (Emergent Over Authored). The entire god-actor orchestration layer is non-functional without it.

**Question**: What is the implementation timeline for Divine? Should it be the next major implementation target given how many systems depend on it?

### O2. Affix Metadata Bag Convention (T29 Violation) [SIGNIFICANT]

**Priority**: P1 -- acknowledged tenet violation that must be resolved before implementation

**Details**: The Affix service stores affix instance data in `ItemInstance.instanceMetadata.affixes` and documents a convention for Craft, Loot, and Market to read it by key name. The deep dive itself acknowledges this is a T29 violation ("No Metadata Bag Contracts").

This is the exact anti-pattern the tenets exist to prevent: cross-service data sharing via untyped JSON with key-name conventions. No schema enforcement, no compile-time safety, no lifecycle management.

**Possible resolutions**:
1. Affix owns its own state store for affix instance data, with its own API for "what affixes does this item have?" Other services query Affix, not Item's metadata blob
2. The affix data becomes typed schema properties on ItemInstance (but this couples Item to Affix concepts)
3. A hybrid: Affix writes to its own store keyed by itemInstanceId, but also writes a summary to Item metadata for client rendering (read-only convention, not a data contract)

**Question**: Which resolution approach should Affix follow?

### O3. Transit and Environment Lack Variable Providers [MODERATE]

**Priority**: P2 -- NPCs cannot make spatially or environmentally aware decisions

**Details**: NPCs need to reason about:
- **Movement**: "Can I get from A to B? How long? Is the route safe?" -- requires `${transit.*}`
- **Weather/Environment**: "Is it raining? Too cold? Are resources available?" -- requires `${environment.*}`

Neither Transit nor Environment describe a Variable Provider Factory implementation. Yet:
- The Vision's NPC intelligence stack depends on autonomous movement decisions
- The Behavioral Bootstrap shows god-actors orchestrating environmental changes
- Workshop and Trade both reference environmental conditions affecting production/routes

**Impact**: Without these providers, NPC behavior can't react to geography or weather. The "living world" feels indoor and static. NPCs can't decide "it's too dangerous to travel in this storm" or "the mountain pass is faster but harder."

**Question**: Should Transit and Environment add `${transit.*}` and `${environment.*}` variable provider implementations to their designs?

### O4. Organization Lacks Economic GOAP Integration [MODERATE]

**Priority**: P2 -- the organizational layer of the economy has no behavioral integration path

**Details**: Organization is described as "legal entities that own assets, employ characters, enter contracts." But:

- No `${organization.*}` variable provider exists for Actor
- Craft, Workshop, Market, and Trade deep dives don't mention Organization
- NPC economic GOAP has no mechanism to distinguish "acting on behalf of org" vs. "acting personally"
- Workshop's `${workshop.*}` variables are character-scoped, not org-scoped

The Vision describes NPCs that "buy/sell/craft/trade based on needs, aspirations, personality." But guilds running workshops, merchant companies controlling trade routes, and temples managing divine economies have no behavioral integration.

**Question**: Should Organization provide `${organization.*}` variables? How should NPC GOAP distinguish personal vs. organizational economic activity?

### O5. Loot Treats L2 Dependencies as Soft [MINOR]

**Priority**: P3 -- hierarchy violation, small fix

**Details**: Loot's deep dive lists Currency (L2) and Character (L2) as soft dependencies with graceful degradation. Per SERVICE-HIERARCHY.md, L4 services MUST use constructor injection for L0/L1/L2 dependencies. Graceful degradation for guaranteed-available layers is explicitly forbidden. The deep dive flags this as "Design Consideration #9."

**Resolution**: Change Currency and Character to hard dependencies (constructor injection). Small documentation fix.

### O6. Variable Provider Scaling at 100K NPCs [MINOR]

**Priority**: P3 -- performance concern, not a correctness issue

**Details**: The NPC cognition pipeline now has 13+ confirmed variable provider namespaces:

| Namespace | Service | Status |
|---|---|---|
| `${personality.*}` | Character-Personality (L4) | Implemented |
| `${encounters.*}` | Character-Encounter (L4) | Implemented |
| `${backstory.*}` | Character-History (L4) | Implemented |
| `${quest.*}` | Quest (L2) | Implemented |
| `${world.*}` | Worldstate (L2) | Implemented |
| `${obligations.*}` | Obligation (L4) | Implemented |
| `${disposition.*}` | Disposition (L4) | Pre-implementation |
| `${nature.*}` | Ethology (L4) | Pre-implementation |
| `${hearsay.*}` | Hearsay (L4) | Pre-implementation |
| `${lexicon.*}` | Lexicon (L4) | Pre-implementation |
| `${spirit.*}` | Agency (L4) | Pre-implementation |
| `${craft.*}` | Craft (L4) | Pre-implementation |
| `${market.*}` | Market (L4) | Pre-implementation |

At 100,000 concurrent NPCs with 100-500ms cognitive cycles, loading 13+ provider data sources per tick is significant. Each provider caches data per-character, but the cache invalidation story (pub/sub events per provider per character state change across 100K characters) needs explicit capacity planning.

**Mitigating factors**: Not all NPCs need all providers. A blacksmith NPC needs `${craft.*}` and `${market.*}` but probably not `${hearsay.*}`. Behavior documents only reference the providers they use, so the Actor runtime can lazy-load only the required providers per actor. This is an optimization concern, not an architectural problem.

**Question**: Should the Variable Provider Factory pattern document a lazy-loading strategy, or is the current "load all registered providers" approach sufficient for the target scale?

---

## Illuminated but Unresolved Patterns

These are not issues but patterns that the audit surfaced as worth considering.

### Guardian Spirits as Actors

If the guardian spirit is a Character in the Nexius realm, could it have an Actor? A dormant cognitive process representing accumulated instinct would mean:

- The spirit has `${personality.*}` of its own (not just the possessed character's)
- Spirit "resistance" to player commands that violate the spirit's nature becomes mechanically real
- The Omega "hacking" mechanic (forcing UX modules the spirit hasn't earned) has a concrete resistance model
- The spirit's personality evolves over generations based on choices, not just seed growth

This isn't required but the system realm pattern makes it trivially possible.

### The Underworld as Content Flywheel Amplifier

DIVINE.md Extension #12 proposes the Underworld as a system realm. If implemented:

- Dead characters **continue as actors** running afterlife behaviors instead of being archived
- They generate encounters and history that feed back into the living world
- The content flywheel gets a second loop: death -> underworld gameplay -> afterlife events -> living world narrative
- The Vision's soul architecture (logos bundle + pneuma shell + sense of self) becomes literal, not metaphorical

This would significantly enrich the flywheel and answer "Death Creates, Not Destroys" more completely than compression + archives alone. It also creates a new gameplay mode (afterlife) that emerges naturally from the system realm architecture.

### System Realm Character Filtering

Multiple services that list or query characters may need to exclude system realms by default. Character's `listByRealm` already filters by realm, so queries against physical realms naturally exclude system realm entities. But services that query characters globally (e.g., analytics aggregation, leaderboards) should be aware that gods, guardian spirits, and underworld characters are also "characters" in the database. This is a deployment-time concern, not an architectural problem -- filter by `realm.isSystemType == false` for player-facing queries.

---

## Key Architectural Pattern: ABML as Universal Authoring Language

A second cross-cutting pattern emerged from resolving the Combat Dream (R9): ABML is not just a behavior language -- it is a universal authoring format for any domain that needs autonomous decision-making or time-sequenced composition. The existing infrastructure already supports this:

### Execution Backends

| Mode | Location | Consumer | When to Use |
|---|---|---|---|
| **Compiled bytecode** | `sdks/behavior-compiler/Runtime/BehaviorModelInterpreter.cs` | lib-actor (NPC brain loops), client SDKs | Performance-critical loops where higher-level decisions set the inputs; no GOAP needed at runtime |
| **Step-through document** | `bannou-service/Abml/Execution/DocumentExecutor.cs` | Any plugin (shared code) | Complex behaviors requiring variable providers, GOAP planning, service API calls |
| **Cinematic streaming** | `sdks/behavior-compiler/Runtime/CinematicInterpreter.cs` | lib-cinematic (new), lib-behavior | Streaming choreographic composition with continuation points and real-time extensions |

### Authoring Domains

| Domain | ABML Decides | Execution Backend | Plugin |
|---|---|---|---|
| NPC cognition | "What should I do?" | Bytecode VM (100-500ms ticks) | lib-actor |
| Combat choreography | "How should this fight play out?" | CinematicInterpreter (5-30s sequences) | lib-cinematic (new) |
| Narrative composition | "What story emerges from this archive?" | DocumentExecutor (hours of game-time) | lib-storyline |
| Music authoring | "What mood should this music convey?" | DocumentExecutor -> MusicTheory SDK | lib-music |
| Dialogue exchanges | "What should this character say?" | DocumentExecutor (seconds) | Any plugin |
| God orchestration | "Should I intervene in this region?" | DocumentExecutor (days of game-time) | lib-puppetmaster / lib-gardener |
| Dialing plans (SIP) | "How should this voice call be routed?" | DocumentExecutor | lib-voice |

### Architectural Property

**DocumentExecutor is in `bannou-service/`** (shared code), not in any plugin. Any service can execute ABML documents without depending on lib-behavior or lib-actor. The bytecode VM and CinematicInterpreter are in the behavior-compiler SDK, independently referenceable. This means ABML authoring and execution are decoupled from any specific plugin -- the language is infrastructure, not a feature.

---

## Overall Assessment

The 75-service architecture is remarkably coherent. The audit resolved 10 of the original concerns:

- **System realm pattern** (R1-R3): Collapses guardian spirit growth, twin spirits, and Agency data source into existing L2 infrastructure
- **NPC cognition differentiation** (R4): All 13+ variable provider namespaces are clearly orthogonal
- **Economy consistency** (R5-R6, R8): Item flows, seed growth pipelines, and cleanup all follow uniform patterns
- **Temporal mechanisms** (R7): Character-Lifecycle aging is correctly event-driven via Worldstate
- **Combat dream** (R9-R10): Decomposes into CinematicTheory SDK extension + thin lib-cinematic plugin, following the Music/Storyline pattern; ABML recognized as universal authoring language

The remaining open issues are:
- **One critical blocker** (O1: Divine is stubbed -- blocks behavioral bootstrap, system realms, and content flywheel)
- **One significant design decision** (O2: Affix metadata T29 violation)
- **Two moderate gaps** (O3: Transit/Environment variable providers; O4: Organization economic GOAP)
- **Two minor items** (O5: Loot hierarchy fix; O6: Variable provider scaling plan)

None of the open issues represent architectural contradictions. They are design gaps -- places where pieces fit together logically but haven't been specified yet. The single most impactful action is implementing Divine (O1), which unblocks the behavioral bootstrap, proves out the system realm pattern for Nexius/Underworld, and enables the content flywheel's orchestration layer.

---

*This document is a point-in-time audit. Update it as issues are resolved or new concerns surface during aspirational plugin specification.*
