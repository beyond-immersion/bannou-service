# Vision Progress: Cross-Service Architectural Audit

> **Version**: 1.2
> **Last Updated**: 2026-03-02
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
| **DUNGEON_CORES** | Dungeon entity characters, cognitive progression, spatial personality | Documented in DUNGEON.md |
| **SENTIENT_ARMS** | Living weapon characters, wielder bonds, memory manifestation | Documented in ACTOR-BOUND-ENTITIES.md |
| **UNDERWORLD** | Dead characters post-mortem, afterlife gameplay, cross-boundary narrative | Documented in DIVINE.md (Extension #12) |

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

### R11. Transit and Location Variable Providers

**Original concern** (formerly part of O3): NPCs cannot make spatially aware decisions -- no `${transit.*}` or `${location.*}` providers existed for movement reasoning or location context.

**Resolution**: Both providers are now fully implemented and registered via DI:

| Provider | Plugin | Namespace | Backing State | Status |
|---|---|---|---|---|
| `TransitVariableProviderFactory` | lib-transit (L2) | `${transit.*}` | Redis + MySQL stores | Functional |
| `LocationContextProviderFactory` | lib-location (L2) | `${location.*}` | ILocationDataCache | Functional |

Transit provides journey data, route discovery, and movement cost calculations. Location provides hierarchy context, presence data, and location metadata. Both are discovered by Actor via `IEnumerable<IVariableProviderFactory>`. Transit also defines `ITransitCostModifierProvider` for L4 services to enrich movement costs, though no implementations exist yet (see O8).

NPCs can now reason about "Can I get from A to B? How long will it take?" and "Where am I? What's nearby?" The environmental half (weather, temperature) remains open as O3.

---

## Open Issues

### O1. Divine Is Completely Stubbed [CRITICAL]

**Priority**: P0 -- blocks the behavioral bootstrap, content flywheel orchestration, and the system realm proof-of-concept

**Details**: All 22 Divine endpoints return `NotImplemented`. The behavioral bootstrap requires Divine for deity creation (Phase 3), divinity economy, blessing orchestration, and avatar manifestation. The system realm pattern (which resolves R1, R2, R3) depends on Divine's implementation proving out the concept that other system realms (Nexius, Underworld) can follow.

**Impact**: Blocks North Star #1 (Living Game Worlds), #2 (Content Flywheel), and #5 (Emergent Over Authored). The entire god-actor orchestration layer is non-functional without it.

**Status (2026-03-02)**: Unchanged. Schema complete (22 endpoints, 5 state stores, 11 events, 18 config properties), but all 22 endpoints still return `NotImplemented`. Zero implementation logic. Score remains 25% (schema quality, not code). Note: the first-order blocker is actually Puppetmaster watcher-actor integration (watchers can't spawn actors), which gates Phase 2 of the behavioral bootstrap. Divine is the second-order blocker (Phase 3). See "Behavioral Bootstrap Critical Path" section below.

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

### O3. Environment Lacks Variable Provider [MODERATE]

**Priority**: P2 -- NPCs cannot make environmentally aware decisions

**Details**: NPCs need to reason about weather and ecological conditions: "Is it raining? Too cold? Are resources available?" -- requires `${environment.*}`. Environment (L4) is at 0% pre-implementation with no variable provider designed.

**Partially resolved (2026-03-02)**: Transit and Location variable providers are now fully implemented (see R11). NPCs can reason about movement, routes, and spatial context. The remaining gap is environmental awareness only.

**Impact**: Without `${environment.*}`, NPC behavior can't react to weather or ecological conditions. Workshop and Trade both reference environmental conditions affecting production/routes. God-actors (Behavioral Bootstrap) orchestrate environmental changes that NPCs should perceive. A merchant NPC can't decide "it's too dangerous to travel in this storm" -- they can plan routes (Transit), but can't factor in weather.

**Question**: Should Environment add `${environment.*}` variable provider implementation to its design? What variables should it expose (temperature, precipitation, wind, ecological resource availability)?

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

**Details**: The NPC cognition pipeline has 19 confirmed variable provider namespaces, of which **11 are fully implemented** (verified 2026-03-02 via codebase audit of `IVariableProviderFactory` implementations):

| Namespace | Service | Status |
|---|---|---|
| `${personality.*}` | Character-Personality (L4) | Implemented — PersonalityProviderFactory, backed by IPersonalityDataCache |
| `${combat.*}` | Character-Personality (L4) | Implemented — CombatPreferencesProviderFactory, backed by IPersonalityDataCache |
| `${encounters.*}` | Character-Encounter (L4) | Implemented — EncountersProviderFactory, backed by IEncounterDataCache |
| `${backstory.*}` | Character-History (L4) | Implemented — BackstoryProviderFactory, backed by IBackstoryCache |
| `${quest.*}` | Quest (L2) | Implemented — QuestProviderFactory, backed by IQuestDataCache |
| `${world.*}` | Worldstate (L2) | Implemented — WorldProviderFactory, backed by IRealmClockCache + ICalendarTemplateCache |
| `${seed.*}` | Seed (L2) | Implemented — SeedProviderFactory, backed by ISeedDataCache |
| `${obligations.*}` | Obligation (L4) | Implemented — ObligationProviderFactory, backed by Redis obligation cache store |
| `${faction.*}` | Faction (L4) | Implemented — FactionProviderFactory, backed by Redis/MySQL stores |
| `${transit.*}` | Transit (L2) | Implemented — TransitVariableProviderFactory, backed by Redis + MySQL stores |
| `${location.*}` | Location (L2) | Implemented — LocationContextProviderFactory, backed by ILocationDataCache |
| `${disposition.*}` | Disposition (L4) | Pre-implementation (service at 0%) |
| `${nature.*}` | Ethology (L4) | Pre-implementation (service at 0%) |
| `${hearsay.*}` | Hearsay (L4) | Pre-implementation (service at 0%) |
| `${lexicon.*}` | Lexicon (L4) | Pre-implementation (service at 0%) |
| `${spirit.*}` | Agency (L4) | Pre-implementation (service at 0%) |
| `${social.*}` | Lexicon/Chat bridge (L4/L1) | Pre-implementation — described in CHARACTER-COMMUNICATION.md; reads recent Lexicon-typed Chat messages for social cognition |
| `${craft.*}` | Craft (L4) | Pre-implementation (service at 0%) |
| `${market.*}` | Market (L4) | Pre-implementation (service at 0%) |

At 100,000 concurrent NPCs with 100-500ms cognitive cycles, loading 19 provider data sources per tick is significant. Each provider caches data per-character, but the cache invalidation story (pub/sub events per provider per character state change across 100K characters) needs explicit capacity planning.

**Mitigating factors**: Not all NPCs need all providers. A blacksmith NPC needs `${craft.*}` and `${market.*}` but probably not `${hearsay.*}`. Behavior documents only reference the providers they use, so the Actor runtime can lazy-load only the required providers per actor. This is an optimization concern, not an architectural problem.

**Question**: Should the Variable Provider Factory pattern document a lazy-loading strategy, or is the current "load all registered providers" approach sufficient for the target scale?

### O7. Social Variable Provider Not Yet Designed [MODERATE]

**Priority**: P2 -- NPCs cannot perceive or react to social communication without it

**Details**: [CHARACTER-COMMUNICATION.md](../guides/CHARACTER-COMMUNICATION.md) describes a `${social.*}` variable provider namespace that would read recent Chat messages from joined Lexicon rooms and expose them to Actor cognition. This is the missing bridge between NPC social interaction (Chat L1 + Lexicon L4) and NPC behavioral response (Actor L2). Without it, NPCs can send structured messages but cannot perceive or react to messages sent by others.

**Proposed variables**:

| Variable | Type | Description |
|---|---|---|
| `${social.message_count}` | int | Messages in joined Lexicon rooms within cache window |
| `${social.recent.N.intent}` | string | Intent code of Nth most recent message (WARN, REQUEST, OFFER, etc.) |
| `${social.recent.N.elements}` | string[] | Lexicon entry codes in Nth message |
| `${social.recent.N.sender_id}` | Guid | Character who sent Nth message |
| `${social.has_warnings}` | bool | Any recent WARNING-intent messages in range |
| `${social.has_requests}` | bool | Any recent REQUEST-intent messages in range |
| `${social.topic_frequency}` | dict | Most-discussed Lexicon entries by frequency |
| `${social.ambient_mood}` | float | Aggregate sentiment of recent social messages |
| `${social.unanswered_questions}` | int | QUESTION-intent messages without ANSWER follow-ups |

**Implementation path**: The provider would be registered by whichever service owns the Lexicon room type integration (likely lib-lexicon L4 or a dedicated social bridge service). It reads from Chat (L1) state stores using the character's joined Lexicon rooms, with a configurable TTL cache window (30-60s per CHARACTER-COMMUNICATION.md). The provider infrastructure (`IVariableProviderFactory`) is fully operational with 11 existing implementations -- this is a domain-specific implementation gap, not an architectural one.

**Dependencies**: Requires Lexicon (L4, 0%) for concept ontology and the Lexicon room type registration with Chat (L1). Chat itself is production-ready (97%) and already supports custom room types.

**Impact on North Star #1 (Living Game Worlds)**: Without `${social.*}`, NPC ABML behaviors cannot reference social context. NPCs can't respond to warnings ("a villager just shouted about wolves"), answer questions ("someone asked where the blacksmith is"), react to ambient social mood ("the market is buzzing with excitement"), or notice unanswered pleas ("a child has been calling for help"). The social communication layer described in CHARACTER-COMMUNICATION.md is structurally complete but cognitively disconnected from behavior until this provider exists.

**Question**: Should this provider live in lib-lexicon (which owns the concept ontology and room type), or in a dedicated social-bridge helper service? lib-lexicon is the natural owner since it registers the Lexicon room type with Chat and understands message structure, but the provider reads Chat state stores directly (L4 reading L1 state -- architecturally valid but worth noting).

### O8. ITransitCostModifierProvider Has Zero Implementations [MINOR]

**Priority**: P3 -- movement cost enrichment is optional but valuable for NPC spatial reasoning

**Details**: Transit's `TransitVariableProviderFactory` injects `IEnumerable<ITransitCostModifierProvider>` to allow L4 services to enrich movement cost calculations with dynamic, context-sensitive modifiers. The interface exists and the injection is wired, but the codebase audit (2026-03-02) found **zero implementations** registered anywhere. Transit gracefully degrades -- uses base connection costs without dynamic modifiers -- so the system is functional but spatially naive.

**The interface pattern** (defined in `bannou-service/Providers/`):

```csharp
public interface ITransitCostModifierProvider
{
    string ProviderName { get; }
    Task<TransitCostModification> GetCostModifierAsync(
        Guid connectionId, Guid characterId, CancellationToken ct);
}
```

**Candidate implementations by L4 service**:

| Service | Provider Name | Modifier Logic | Example |
|---|---|---|---|
| **Environment** (L4, 0%) | `weather` | Weather conditions affect travel speed/safety | "Mountain pass in blizzard: cost × 3.0, danger × 5.0" |
| **Faction** (L4, 80%) | `territory` | Faction control and hostility affect perceived danger | "Enemy faction territory: danger × 2.0, stealth cost + 50%" |
| **Hearsay** (L4, 0%) | `rumors` | Belief-based modifiers from rumored threats | "NPC heard bandits on this road: perceived danger × 1.5 (may be outdated)" |
| **Status** (L4, 78%) | `effects` | Active status effects that modify movement | "Cursed: all travel costs × 1.2" |
| **Obligation** (L4, 85%) | `contracts` | Contractual restrictions on movement | "Guild charter forbids entering rival territory: cost = ∞ (soft block)" |

**Impact on NPC behavior**: Without cost modifiers, NPC GOAP pathfinding always picks the geometrically shortest/cheapest route regardless of conditions. A merchant NPC can't choose the longer but safer route to avoid a storm. A soldier can't prefer routes through allied territory. A superstitious NPC can't avoid the "haunted forest" based on rumors. Route decisions are mechanically correct but contextually flat.

**Impact on North Star #5 (Emergent Over Authored)**: Dynamic route selection is a key emergent behavior driver. When storms redirect trade, factions block passes, and rumors reshape travel patterns, the economy and social dynamics shift organically. Without cost modifiers, these cascading effects don't emerge.

**Question**: Should `ITransitCostModifierProvider` implementations be specified as part of the Environment, Faction, Hearsay, Status, and Obligation service designs? This is a cross-cutting concern that touches 5+ services -- should it be tracked as individual service enhancements or as a single cross-service initiative?

---

## Behavioral Bootstrap Critical Path

The behavioral bootstrap sequence (documented in [BEHAVIORAL-BOOTSTRAP.md](../guides/BEHAVIORAL-BOOTSTRAP.md) and [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md)) is the mechanism that activates the content flywheel: god-actors running ABML behaviors orchestrate the macro-level gameplay loop. The bootstrap is currently **blocked at Phase 2**.

### The Blocking Chain

VISION-PROGRESS previously identified Divine (O1) as the critical blocker, but the actual dependency chain reveals Puppetmaster is the **first-order** blocker:

```
Puppetmaster watcher-actor integration (55%, core actor spawn stubbed)
  → Phase 2 blocked: Manager actors can't be spawned as actual Actor instances
    → Phase 3 blocked: Managers can't call Divine API (even if Divine were implemented)
      → Phase 4 blocked: God-actors can't be spawned per realm
        → Phase 5 blocked: Content flywheel orchestration non-functional
```

This distinction matters: Puppetmaster's gap is arguably smaller (integrate `IActorClient.SpawnAsync`, track ActorId) while Divine's gap is larger (all 22 endpoints, 5 state stores, background workers).

### Current State Per Service

| Service | Score | What Works | What's Blocked |
|---|---|---|---|
| **Puppetmaster** (L4, 55%) | Behavior document cache (TTL-based via IAssetClient), watch system (dual-indexed WatchRegistry), event handlers (realm lifecycle, behavior hot-reload, actor cleanup), resource snapshot cache, ABML action handlers (LoadSnapshot, PrefetchSnapshots, SpawnWatcher, StopWatcher, ListWatchers) | **Watcher-actor spawning**: `ActorId` on `WatcherInfo` is always null (TODO at line 213). Watchers are data structures only — `StartWatcherAsync` creates in-memory `ConcurrentDictionary` entries but spawns no actors and executes no behavior. All watcher state is in-memory (lost on restart, no multi-instance coordination). |
| **Divine** (L4, 25%) | Complete schema (22 endpoints), 5 state store definitions (MySQL + Redis), configuration class (18 properties), event schema (11 lifecycle/domain events), type field classification (T25 compliant), resource cleanup endpoints defined | **ALL 22 endpoints return `NotImplemented`**. Zero implementation logic. No background workers (`DivineAttentionWorker`, `DivinityGenerationWorker` undefined). No event handlers. No plugin startup registration for seed types, currencies, relationship types, or collection/status templates. |
| **Gardener** (L4, 62%) | Void/discovery garden: enter/leave with distributed lock, POI system (weighted scoring: affinity + diversity + narrative + random), scenario lifecycle (enter, complete, abandon, chain, enter-together for pairs), template CRUD with deprecation, deployment phase config, 15 event types published, `ISeedEvolutionListener` for growth notifications, background workers (5s garden tick, 30s scenario lifecycle) | No divine actor integration (fixed-interval workers instead of ABML-driven orchestration), no client events schema (players must poll), no garden-to-garden transitions, no multiple garden types (only void/discovery), no entity session registry integration, no Puppetmaster notification on scenario start, no prerequisite validation on scenario entry. |

### Recommended Implementation Sequence

1. **Puppetmaster watcher-actor spawning** (Medium complexity): Integrate `IActorClient.SpawnAsync()` in `StartWatcherAsync`, store `ActorId` on `WatcherInfo`, add actor lifecycle tracking (restart on crash, health monitoring). This is the smallest change that unblocks the most — the entire behavioral bootstrap gates on it.
2. **Puppetmaster distributed watcher state** (Medium complexity): Move watcher registry from in-memory `ConcurrentDictionary` to Redis for multi-instance consistency and persistence across restarts. Currently only one Puppetmaster instance can be active.
3. **Divine deity creation** (Medium complexity): Implement deity CRUD — create deity record, divinity currency wallet via `ICurrencyClient`, domain power seed via `ISeedClient`, character in PANTHEON system realm via `ICharacterClient`.
4. **Divine character binding** (Medium complexity): Store `characterId` on `DeityModel`. Design decision required — Option A (character-first: create divine Character before spawning actor, spawn with `characterId` already set) vs. Option B (bind-later: spawn actor as event brain first, create divine Character during deity setup, then call `/actor/bind-character` at runtime).
5. **Gardener divine actor integration** (High complexity): Replace background worker ticks with ABML-driven gardener behavior documents. Requires: gardener behavior documents authored, actor spawning at garden entry, dynamic character binding for personality-driven gardening, garden-to-garden transitions, client events schema for real-time UX.

### Phase-by-Phase Bootstrap Status

| Phase | Description | Status | Blocker |
|---|---|---|---|
| **Phase 1**: Seeded behaviors loaded | Resource loads ABML templates via `ISeededResourceProvider` | PARTIAL — `BehaviorSeededResourceProvider` exists and is wired, but 0 seeded .yaml behavior files exist in lib-actor/Behaviors/ yet | Need actual behavior documents authored |
| **Phase 2**: Manager actors spawned | Puppetmaster/Gardener spawn singleton manager actors | **BLOCKED** | Puppetmaster `StartWatcherAsync` doesn't call `IActorClient.SpawnAsync()` — creates in-memory watcher entry only |
| **Phase 3**: Deity initialization | Manager behaviors call Divine API to create/retrieve deities | **BLOCKED** | Divine implementation (all 22 endpoints stubbed) + Phase 2 prerequisite |
| **Phase 4**: God-actors per realm | Per realm x deity: regional watcher actors spawned with deity-specific ABML | **BLOCKED** | Phase 2 (actor spawning) + Phase 3 (deity identity) |
| **Phase 5**: Steady state | Puppetmaster gods perceive world events, evaluate archives, orchestrate narratives; Gardener gods tend player experiences | **BLOCKED** | All above phases |

### What IS Ready

Despite the orchestration layer being blocked, the **execution infrastructure** is complete:

- **11 variable providers** registered and functional — NPCs have personality, memory, world awareness, progress tracking, and social structures wired (see O6 table above)
- **3-tier behavior document chain** — `DynamicBehaviorProvider` (Asset/L3, priority 100) → `SeededBehaviorProvider` (embedded, priority 50) → `FallbackBehaviorProvider` (graceful degradation, priority 0)
- **Actor runtime** — two-phase tick, dynamic character binding, pool deployment modes, bounded perception queues, GOAP integration, ~80 telemetry spans
- **DI inversion framework** — `ICollectionUnlockListener` (2 implementations: Seed, Faction), `ISeedEvolutionListener` (3 implementations: Gardener, Faction, Status), all writing to distributed state
- **Watch system** — Puppetmaster's `WatchRegistry`, `ResourceEventMapping`, lifecycle event subscriptions, watch perception injection all functional

The piano keys work. The conductor is missing.

---

## Illuminated but Unresolved Patterns

These are not issues but patterns that the audit surfaced as worth considering.

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

## Design Recommendations

These are patterns that the audit identified as strong candidates for formal commitment. They are architecturally sound, infrastructure-ready, and serve multiple North Stars. Each includes a recommended path and any design questions requiring human judgment.

### DR1. Guardian Spirits as Actors [RECOMMENDED]

**Recommendation**: Commit to giving guardian spirits their own Actor in the Nexius system realm, following the same 3-stage cognitive progression (Dormant → Stirring → Awakened) used by dungeons and living weapons.

**Why this pattern should be formalized**:

1. **Infrastructure cost is near-zero.** With 11 functional variable providers, the system realm pattern proven by DIVINE.md, and the unified 3-stage progression documented in [ACTOR-BOUND-ENTITIES.md](ACTOR-BOUND-ENTITIES.md), adding a guardian spirit Actor requires no new architectural patterns or DI inversion interfaces. The guardian spirit is a Character in the Nexius realm; Actor binds to that character; all existing variable providers activate automatically.

2. **Mechanically implements progressive agency resistance.** PLAYER-VISION.md describes a core design principle: "The character is ALWAYS autonomous. The player does not 'control' a character -- the player gradually learns to *collaborate with* that character's autonomy." With a spirit Actor, the spirit itself has `${personality.*}` — its own traits, preferences, and accumulated tendencies. Spirit "resistance" to player commands that violate the spirit's nature becomes a numerical comparison (spirit personality alignment vs. requested action), not a hand-authored threshold. The spirit develops genuine preferences over generations of play.

3. **Concretizes the Omega hacking mechanic.** PLAYER-VISION.md describes players in Omega forcing UX modules the spirit hasn't earned, with consequences: "the character may resist more strongly (borrowed understanding vs. earned understanding)." With a spirit Actor, this becomes mechanically precise: the spirit's `${personality.*}` traits reflect earned understanding, and forced modules operate against a resistance derived from the gap between the spirit's actual experience and the module's requirements. Higher gap = more resistance = worse results.

4. **Creates the "your guardian spirit IS your save file" feeling.** Spirit personality evolving over generations based on choices (not just seed growth) means the spirit accumulates character — a player who consistently pursues justice develops a spirit that gravitates toward justice-adjacent scenarios. A player who betrays allies develops a spirit that attracts betrayal narratives. The guardian spirit becomes the most persistent expression of player identity, surviving across all character lifetimes.

5. **Completes the Fulfillment Principle.** R1 resolved the growth mechanism (Collection → Seed pipeline), but growth only changes capabilities. Spirit personality evolution changes *who the spirit is*. A spirit that guided a character through a deeply fulfilling life doesn't just get stronger — it develops traits reflecting that fulfillment (wisdom, serenity, confidence). A spirit whose characters die unfulfilled accumulates restlessness, urgency, regret. This is the qualitative complement to seed growth's quantitative progression.

**Implementation path** (brief sketch, not a full plan):

| Step | Action | Service | Dependency |
|---|---|---|---|
| 1 | Guardian spirit Character created in NEXIUS system realm | Character (L2) | Realm with `isSystemType: true` and code `NEXIUS` must exist |
| 2 | Guardian seed registered as `guardian` type | Seed (L2) | Seed type registration (already referenced in Agency and R6) |
| 3 | Spirit personality record initialized | Character-Personality (L4) | Character must exist (Step 1) |
| 4 | Spirit Actor spawned when seed reaches Stirring phase | Actor (L2) via `ISeedEvolutionListener` | Seed growth sufficient; actor spawning functional |
| 5 | Actor binds to Nexius character (full variable providers activate) | Actor (L2) `/actor/bind-character` | Character exists (Step 1); Actor exists (Step 4) |
| 6 | Agency reads spirit personality + seed capabilities for UX manifest | Agency (L4) | Steps 2-3 provide the data; Agency computes the manifest |

**Steps 1-3** can be done today with existing infrastructure (no blocked dependencies).
**Steps 4-5** require Puppetmaster watcher-actor spawning to be functional (see Behavioral Bootstrap Critical Path).
**Step 6** requires Agency service implementation (currently 0%).

**The entity stack the guardian spirit gains** (via system realm pattern — all automatic, zero new code):

| Service | What the Spirit Gets |
|---|---|
| **Character** (L2) | Identity record bound to NEXIUS realm, alive/dead lifecycle |
| **Character Personality** (L4) | Bipolar trait axes evolving based on player choices across generations |
| **Character Encounter** (L4) | Memories of possessed characters, notable interactions, generational highlights |
| **Character History** (L4) | Backstory elements accumulated from player journey (not character journey) |
| **Seed** (L2) | `guardian` seed with growth domains (wisdom, combat, crafting, social, etc.) |
| **Collection** (L2) | Permanent knowledge unlocks — scenarios experienced, skills mastered, discoveries made |
| **Relationship** (L2) | Pair bonds (`twin_spirits` type), bonds to possessed characters, bonds to deities |
| **Actor** (L2) | Full variable provider chain — spirit cognition with personality, memory, progress awareness |

**Design question requiring human judgment**:

> **Should the spirit Actor run continuously or only activate during specific moments?**
>
> | Mode | Cost | Benefit | Trade-off |
> |---|---|---|---|
> | **Always-on** | ~1 actor per player. At 10K concurrent players = 10K additional actors (~10% overhead on the 100K NPC target). At 1K concurrent players = negligible. | Spirit has real-time opinions during gameplay. Can inject perceptions into the possessed character's Actor ("the spirit feels uneasy about this decision"). Resistance is immediate, not retroactive. Enables the "spirit co-pilot" experience described in PLAYER-VISION.md. | Resource cost scales linearly with player count. Must be accounted for in pool sizing. |
> | **Moment-based** | Spawns only during death processing, generation transitions, major choices, Omega hacking, pair bond events. Dormant between moments. | Near-zero steady-state cost. Spirit actors exist for seconds to minutes, not hours. | Loses real-time co-pilot feeling. Spirit resistance is event-based, not continuous. The "spirit has opinions" experience becomes punctuated rather than ambient. Player never feels the spirit's personality during routine play — only at inflection points. |
> | **Hybrid (recommended)** | Low-frequency tick (every 5-30s instead of 100-500ms). Spirit doesn't need millisecond cognition — it processes at a "spiritual" temporal scale. | Spirit maintains ambient awareness and can inject rare perceptions ("something feels wrong"), but doesn't consume the per-tick resources of a full NPC brain. Cost is ~1/50th to 1/100th of an NPC actor per player. At 10K players = equivalent to 100-200 NPCs. | Requires a new tick rate tier for actors (currently all actors tick at the same configurable rate). Minor Actor runtime enhancement. |
>
> The hybrid approach most closely matches the metaphysical model: a guardian spirit is a divine shard, not a mortal brain. It perceives at a cosmic scale, not a human one. A 10-30 second tick rate for spirit actors is narratively justified and computationally efficient.

**Connection to other patterns**:
- **Twin Spirits** (R2): Pair bond between two Nexius characters with spirit Actors enables ambient awareness of each other's state — one spirit perceives the other's distress even across realms.
- **Underworld** (see below): When a character dies, the spirit Actor perceives the death and processes it emotionally. Spirit personality shifts based on the death context (fulfilled vs. tragic). This IS the Fulfillment Principle in action.
- **Content Flywheel**: Spirit personality evolution creates player-specific narrative preferences that god-actors (Gardener) can read to personalize scenario offerings. The flywheel becomes player-attuned, not just world-attuned.

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

The 75-service architecture is remarkably coherent. The audit has resolved 11 of the original concerns:

- **System realm pattern** (R1-R3): Collapses guardian spirit growth, twin spirits, and Agency data source into existing L2 infrastructure
- **NPC cognition differentiation** (R4): All 15+ variable provider namespaces are clearly orthogonal
- **Economy consistency** (R5-R6, R8): Item flows, seed growth pipelines, and cleanup all follow uniform patterns
- **Temporal mechanisms** (R7): Character-Lifecycle aging is correctly event-driven via Worldstate
- **Combat dream** (R9-R10): Decomposes into CinematicTheory SDK extension + thin lib-cinematic plugin, following the Music/Storyline pattern; ABML recognized as universal authoring language
- **Spatial awareness** (R11): Transit and Location variable providers now fully implemented, giving NPCs movement reasoning and location context

The remaining open issues are:
- **One critical blocker** (O1: Divine is stubbed -- blocks behavioral bootstrap, system realms, and content flywheel; note: Puppetmaster watcher-actor spawning is the first-order blocker, Divine is second-order)
- **One significant design decision** (O2: Affix metadata T29 violation)
- **Three moderate gaps** (O3: Environment variable provider; O4: Organization economic GOAP; O7: Social variable provider not yet designed -- NPCs can't perceive or react to social communication)
- **Three minor items** (O5: Loot hierarchy fix; O6: Variable provider scaling plan; O8: ITransitCostModifierProvider has zero implementations -- NPC route decisions ignore dynamic conditions)

None of the open issues represent architectural contradictions. They are design gaps -- places where pieces fit together logically but haven't been specified yet. The NPC cognition infrastructure is far more complete than previously documented: 11 of 15+ planned variable providers are fully implemented and wired, covering personality, memory, world awareness, progress, and social structures. The remaining pre-implementation providers (disposition, hearsay, lexicon, social, and others) await their owning L4 services. The two new items (O7, O8) represent cross-cutting integration hooks -- `${social.*}` bridges Chat/Lexicon into Actor cognition, while `ITransitCostModifierProvider` bridges Environment/Faction/Hearsay into spatial reasoning -- that should be incorporated into the respective L4 service designs when those services are specified.

The single most impactful action sequence is: (1) Puppetmaster watcher-actor integration (unblocking the behavioral bootstrap's Phase 2), then (2) Divine implementation (Phase 3), which together prove out the system realm pattern and enable the content flywheel's orchestration layer.

---

*This document is a point-in-time audit. Update it as issues are resolved or new concerns surface during aspirational plugin specification.*
