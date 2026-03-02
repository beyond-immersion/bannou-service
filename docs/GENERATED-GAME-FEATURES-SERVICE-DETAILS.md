# Generated Game Features (L4) Service Details

> **Source**: `docs/plugins/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Services in the **Game Features (L4)** layer.

## Achievement {#achievement}

**Version**: 1.0.0 | **Schema**: `schemas/achievement-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/ACHIEVEMENT.md](plugins/ACHIEVEMENT.md)

The Achievement plugin (L4 GameFeatures) provides a multi-entity achievement and trophy system with progressive/binary unlock types, prerequisite chains, rarity calculations, and platform synchronization (Steam, Xbox, PlayStation). Achievements are scoped to game services, support event-driven auto-unlock from Analytics and Leaderboard events, and include a background service for periodic rarity recalculation.

## Affix {#affix}

**Deep Dive**: [docs/plugins/AFFIX.md](plugins/AFFIX.md)

Item modifier definition, instance management, and stat computation service (L4 GameFeatures). Owns two layers of data: **definitions** (modifier templates with tiers, mod groups, spawn weights, stat grants) and **instances** (per-item applied modifier state stored in Affix's own state store). Any system that needs to answer "what modifiers does this item have?" or "what is this item worth?" queries lib-affix's typed API. Game-agnostic (PoE-style prefix/suffix tiers, Diablo-style legendary affixes, or simple "quality stars" are all valid configurations). Internal-only, never internet-facing.

## Agency {#agency}

**Deep Dive**: [docs/plugins/AGENCY.md](plugins/AGENCY.md)

The Agency service (L4 GameFeatures) manages the guardian spirit's progressive agency system -- the bridge between Seed's abstract capability data and the client's concrete UX module rendering. It answers the question: "Given this guardian spirit's accumulated experience, what can the player perceive and do?" Game-agnostic: domain codes, modules, and influence types are registered per game service, not hardcoded. Internal-only, never internet-facing.

## Analytics {#analytics}

**Version**: 1.0.0 | **Schema**: `schemas/analytics-api.yaml` | **Endpoints**: 9 | **Deep Dive**: [docs/plugins/ANALYTICS.md](plugins/ANALYTICS.md)

The Analytics plugin (L4 GameFeatures) is the central event aggregation point for all game-related statistics. Handles event ingestion, entity summary computation, Glicko-2 skill rating calculations, and controller history tracking. Publishes score updates and milestone events consumed by Achievement and Leaderboard for downstream processing. Subscribes to game session lifecycle and character/realm history events for automatic ingestion. Unlike typical L4 services, Analytics only observes via event subscriptions -- it does not invoke L2/L4 service APIs and should not be called by L1/L2/L3 services.

## Arbitration {#arbitration}

**Deep Dive**: [docs/plugins/ARBITRATION.md](plugins/ARBITRATION.md)

Authoritative dispute resolution service (L4 GameFeatures) for competing claims that need jurisdictional ruling and enforcement. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver adjudication game mechanics. Game-agnostic: procedural templates, arbiter selection rules, and cultural attitudes toward litigation are configured through contract templates and faction governance data at deployment time. Internal-only, never internet-facing.

## Behavior {#behavior}

**Version**: 3.0.0 | **Schema**: `schemas/behavior-api.yaml` | **Endpoints**: 6 | **Deep Dive**: [docs/plugins/BEHAVIOR.md](plugins/BEHAVIOR.md)

ABML (Arcadia Behavior Markup Language) compiler and GOAP (Goal-Oriented Action Planning) runtime (L4 GameFeatures) for NPC behavior management. Provides three core subsystems: a multi-phase ABML compiler producing portable stack-based bytecode, an A*-based GOAP planner for action sequence generation from world state and goals, and a 5-stage cognition pipeline for NPC perception and intention formation. Compiled bytecode is interpreted by both the server-side ActorRunner (L2) and client SDKs. Supports streaming composition, variant-based model caching with fallback chains, and behavior bundling through the Asset service.

## Character Encounter {#character-encounter}

**Version**: 1.0.0 | **Schema**: `schemas/character-encounter-api.yaml` | **Endpoints**: 21 | **Deep Dive**: [docs/plugins/CHARACTER-ENCOUNTER.md](plugins/CHARACTER-ENCOUNTER.md)

Character encounter tracking service (L4 GameFeatures) for memorable interactions between characters, enabling NPC memory, dialogue triggers, grudges/alliances, and quest hooks. Manages encounters (shared interaction records) with per-participant perspectives, time-based memory decay, weighted sentiment aggregation, and configurable encounter type codes. Features automatic pruning per-character and per-pair limits, and provides `${encounters.*}` ABML variables to the Actor service's behavior system via the Variable Provider Factory pattern.

## Character History {#character-history}

**Version**: 1.0.0 | **Schema**: `schemas/character-history-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/CHARACTER-HISTORY.md](plugins/CHARACTER-HISTORY.md)

Historical event participation and backstory management (L4 GameFeatures) for characters. Tracks when characters participate in world events (wars, disasters, political upheavals) with role and significance tracking, and maintains machine-readable backstory elements (origin, occupation, training, trauma, fears, goals) for behavior system consumption. Provides template-based text summarization for character compression via lib-resource. Shares storage helper abstractions with the realm-history service.

## Character Lifecycle {#character-lifecycle}

**Deep Dive**: [docs/plugins/CHARACTER-LIFECYCLE.md](plugins/CHARACTER-LIFECYCLE.md)

Generational cycle orchestration and genetic heritage service (L4 GameFeatures) for character aging, marriage, procreation, death processing, and cross-generational trait inheritance. The temporal engine that drives the content flywheel by ensuring characters are born, live, age, reproduce, and die -- and that each death produces archives feeding future content. Game-agnostic: lifecycle stages, genetic trait definitions, marriage customs, and death processing are configured through lifecycle configuration and seed data at deployment time. Internal-only, never internet-facing.

## Character Personality {#character-personality}

**Version**: 1.0.0 | **Schema**: `schemas/character-personality-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/CHARACTER-PERSONALITY.md](plugins/CHARACTER-PERSONALITY.md)

Machine-readable personality traits and combat preferences (L4 GameFeatures) for NPC behavior decisions. Features probabilistic personality evolution based on character experiences and combat preference adaptation based on battle outcomes. Traits are floating-point values on bipolar axes that shift based on experience intensity. Provides `${personality.*}` and `${combat.*}` ABML variables to the Actor service via the Variable Provider Factory pattern.

## Craft {#craft}

**Deep Dive**: [docs/plugins/CRAFT.md](plugins/CRAFT.md)

Recipe-based crafting orchestration service (L4 GameFeatures) for production workflows, item modification, and skill-gated crafting execution. A thin orchestration layer that composes existing Bannou primitives: lib-item for storage, lib-inventory for material consumption and output placement, lib-contract for multi-step session state machines, lib-currency for costs, and lib-affix for modifier operations on existing items. Game-agnostic: recipe types, proficiency domains, station types, tool categories, and quality formulas are all opaque strings defined per game at deployment time through recipe seeding. Internal-only, never internet-facing.

## Director {#director}

**Deep Dive**: [docs/plugins/DIRECTOR.md](plugins/DIRECTOR.md)

Human-in-the-loop orchestration service (L4 GameFeatures) for developer-driven event coordination, actor observation, and player audience management. The Director is to the development team what Puppetmaster is to NPC behavior and Gardener is to player experience: an orchestration layer that coordinates major world events through existing service primitives, ensuring the right players witness the right moments. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver live event management mechanics.

Three control tiers define the developer's relationship to the actor system: **Observe** (tap into any actor's perception stream and cognitive state), **Steer** (inject perceptions and adjust GOAP priorities while actors run autonomously), and **Drive** (replace an actor's ABML cognition with human decision-making, issuing API calls through the same action handlers actors use). The developer never bypasses game rules -- every action goes through the same pipelines the autonomous system uses, simultaneously testing actor mechanisms while orchestrating live content.

Game-agnostic: event categories, steering strategies, and broadcast coordination rules are configured through director configuration and event templates at deployment time. Internal-only, never internet-facing. All endpoints require the `developer` role.

## Disposition {#disposition}

**Deep Dive**: [docs/plugins/DISPOSITION.md](plugins/DISPOSITION.md)

Emotional synthesis and aspirational drive service (L4 GameFeatures) for NPC inner life. Maintains per-character **feelings** about specific entities (other characters, locations, factions, organizations, and the guardian spirit) and **drives** (long-term aspirational goals that shape behavior priorities). Feelings are the personality-filtered, experience-weighted, hearsay-colored subjective emotional state that a character carries; drives are intrinsic motivations that emerge from personality, circumstance, and accumulated experience. Game-agnostic: feeling axes, drive types, guardian spirit mechanics, and synthesis weights are configured through disposition configuration and seed data at deployment time. Internal-only, never internet-facing.

## Divine {#divine}

**Version**: 1.0.0 | **Schema**: `schemas/divine-api.yaml` | **Endpoints**: 22 | **Deep Dive**: [docs/plugins/DIVINE.md](plugins/DIVINE.md)

Pantheon management service (L4 GameFeatures) for deity entities, divinity economy, and blessing orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver divine game mechanics: god identity is owned here, behavior runs via Actor/Puppetmaster, domain power via Seed, divinity resource via Currency, blessings via Collection/Status, and follower bonds via Relationship. Gods influence characters indirectly through the character's own Actor -- a god's Actor monitors event streams and makes decisions, but the character's Actor receives the consequences. Blessings are entity-agnostic (characters, accounts, deities, or any entity type can receive them). All endpoints are currently stubbed (return `NotImplemented`); see the implementation plan at `docs/plans/DIVINE.md` for the full specification.

## Dungeon {#dungeon}

**Deep Dive**: [docs/plugins/DUNGEON.md](plugins/DUNGEON.md)

Dungeon lifecycle orchestration service (L4 GameFeatures) for living dungeon entities that perceive, grow, and act autonomously within the Bannou actor system. A thin orchestration layer (like Divine over Currency/Seed/Collection, Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver dungeon-as-actor game mechanics. Game-agnostic: dungeon personality types, creature species, and narrative manifestation styles are configured through ABML behaviors and seed type definitions at deployment time. Internal-only, never internet-facing.

## Environment {#environment}

**Deep Dive**: [docs/plugins/ENVIRONMENT.md](plugins/ENVIRONMENT.md)

Environmental state service (L4 GameFeatures) providing weather simulation, temperature modeling, atmospheric conditions, and ecological resource availability for game worlds. Consumes temporal data from Worldstate (L2) -- season, time of day, calendar boundaries -- and translates it into environmental conditions that affect NPC behavior, production, trade, loot generation, and player experience. The missing ecological layer between Worldstate's clock and the behavioral systems that already reference environmental data that doesn't exist. Game-agnostic: biome types, weather distributions, temperature curves, and resource availability are configured through climate template seeding at deployment time -- a space game could model atmospheric composition, a survival game could model wind chill, the service stores float values against string-coded condition axes. Internal-only, never internet-facing.

## Escrow {#escrow}

**Version**: 1.0.0 | **Schema**: `schemas/escrow-api.yaml` | **Endpoints**: 22 | **Deep Dive**: [docs/plugins/ESCROW.md](plugins/ESCROW.md)

Full-custody orchestration layer (L4 GameFeatures) for multi-party asset exchanges. Manages the complete escrow lifecycle from creation through deposit collection, consent gathering, condition verification, and final release or refund. Supports four escrow types (two-party, multi-party, conditional, auction) with three trust modes and a 13-state finite state machine. Handles currency, items, contracts, and extensible custom asset types -- calling lib-currency and lib-inventory directly for asset movements. Integrates with lib-contract for conditional releases where contract fulfillment triggers escrow completion. See Release Modes section below for configurable confirmation flows.

## Ethology {#ethology}

**Deep Dive**: [docs/plugins/ETHOLOGY.md](plugins/ETHOLOGY.md)

Species-level behavioral archetype registry and nature resolution service (L4 GameFeatures) for providing structured behavioral defaults to any entity that runs through the Actor behavior system. The missing middle ground between "hardcoded behavior document defaults" (every wolf is identical) and "full character cognitive stack" (9 variable providers, per-entity persistent state). A structured definition of species-level behavioral baselines with hierarchical overrides (realm, location) and per-individual deterministic noise, exposed as a variable provider to the Actor behavior system. Game-agnostic: behavioral axes are opaque strings -- a horror game could define `stalking_patience` and `ambush_preference`, a farming sim could define `tamability` and `herd_cohesion`. Internal-only, never internet-facing.

## Faction {#faction}

**Version**: 1.0.0 | **Schema**: `schemas/faction-api.yaml` | **Endpoints**: 31 | **Deep Dive**: [docs/plugins/FACTION.md](plugins/FACTION.md)

The Faction service (L4 GameFeatures) models factions as seed-based living entities whose capabilities emerge from growth, not static assignment. As a faction's seed grows through phases (nascent, established, influential, dominant), capabilities unlock: norm definition, enforcement tiers, territory claiming, and trade regulation. Its primary consumer is lib-obligation, which queries faction norms to produce GOAP action cost modifiers for NPC cognition -- resolving a hierarchy of guild, location, and realm baseline norms into a merged norm set. Supports guild memberships with role hierarchy, parent/child organizational structure, territory claims, and inter-faction political connections modeled as seed bonds via lib-seed. Internal-only, never internet-facing.

## Gardener {#gardener}

**Version**: 1.0.0 | **Schema**: `schemas/gardener-api.yaml` | **Endpoints**: 24 | **Deep Dive**: [docs/plugins/GARDENER.md](plugins/GARDENER.md)

Player experience orchestration service (L4 GameFeatures) and the player-side counterpart to Puppetmaster: where Puppetmaster orchestrates what NPCs experience, Gardener orchestrates what players experience. A "garden" is an abstract conceptual space (lobby, in-game, housing, void/discovery) that a player inhabits, with Gardener managing their gameplay context, entity associations, and event routing. Provides the APIs and infrastructure that divine actors (running via Puppetmaster on the L2 Actor runtime) use to manipulate player experiences -- behavior-agnostic, providing primitives not policy. Currently implements the void/discovery garden type only; the broader garden concept (multiple types, garden-to-garden transitions) is the architectural target. Internal-only, never internet-facing.

## Hearsay {#hearsay}

**Deep Dive**: [docs/plugins/HEARSAY.md](plugins/HEARSAY.md)

Social information propagation and belief formation service (L4 GameFeatures) for NPC cognition. Maintains per-character **beliefs** about norms, other characters, and locations -- what an NPC *thinks* they know vs. what is objectively true. Beliefs are acquired through information channels (direct observation, official decree, social contact, rumor, cultural osmosis), carry confidence levels, converge toward reality over time, and can be intentionally manipulated by external actors (gods, propagandists, gossip networks). Game-agnostic: propagation speeds, confidence thresholds, convergence rates, and rumor injection patterns are configured through hearsay configuration and seed data at deployment time. Internal-only, never internet-facing.

## Leaderboard {#leaderboard}

**Version**: 1.0.0 | **Schema**: `schemas/leaderboard-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/LEADERBOARD.md](plugins/LEADERBOARD.md)

Real-time leaderboard management (L4 GameFeatures) built on Redis Sorted Sets. Supports polymorphic entity types (Account, Character, Guild, Actor, Custom), multiple score update modes, seasonal rotation with archival, and automatic score ingestion from Analytics events. Definitions are scoped per game service with configurable sort order and entity type restrictions. Provides percentile calculations, neighbor queries, and batch score submission.

## Lexicon {#lexicon}

**Deep Dive**: [docs/plugins/LEXICON.md](plugins/LEXICON.md)

Structured world knowledge ontology (L4 GameFeatures) that defines what things ARE in terms of decomposed, queryable characteristics. Lexicon is the ground truth registry for entity concepts -- species, objects, phenomena, named individuals, abstract ideas -- broken into traits, hierarchical categories, bidirectional associations, and strategy-relevant implications. It answers "what is a wolf?" not with prose, but with structured data that GOAP planners, behavior expressions, and game systems can reason over. Game-agnostic: entries, trait vocabularies, category hierarchies, and strategy implications are all configured through seed data at deployment time (the True Names metaphysical framework is Arcadia's flavor interpretation of lexicon entry codes). Internal-only, never internet-facing.

## License {#license}

**Version**: 1.0.0 | **Schema**: `schemas/license-api.yaml` | **Endpoints**: 20 | **Deep Dive**: [docs/plugins/LICENSE.md](plugins/LICENSE.md)

The License service (L4 GameFeatures) provides grid-based progression boards (skill trees, license boards, tech trees) inspired by Final Fantasy XII's License Board system. It is a thin orchestration layer that combines Inventory (containers for license items), Items (license nodes as item instances), and Contracts (unlock behavior via prebound API execution) to manage entity progression across a grid. Boards support polymorphic ownership via `ownerType` + `ownerId` — characters, accounts, guilds, and locations can all own boards. Internal-only, never internet-facing. See [GitHub Issue #281](https://github.com/beyond-immersion/bannou-service/issues/281) for the original design specification.

## Loot {#loot}

**Deep Dive**: [docs/plugins/LOOT.md](plugins/LOOT.md)

Loot table management and generation service (L4 GameFeatures) for weighted drop determination, contextual modifier application, and group distribution orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver loot acquisition mechanics. Game-agnostic: table structures, entry weights, context modifiers, distribution modes, and pity thresholds are all opaque configuration defined per game at deployment time through table seeding. Internal-only, never internet-facing.

## Mapping {#mapping}

**Version**: 1.0.0 | **Schema**: `schemas/mapping-api.yaml` | **Endpoints**: 18 | **Deep Dive**: [docs/plugins/MAPPING.md](plugins/MAPPING.md)

Spatial data management service (L4 GameFeatures) for Arcadia game worlds. Provides authority-based channel ownership for exclusive write access to spatial regions, high-throughput ingest via dynamic RabbitMQ subscriptions, 3D spatial indexing with affordance queries, and design-time authoring workflows (checkout/commit/release). Purely a spatial data store -- does not perform rendering or physics. Game servers and NPC brains publish spatial data to and query from it.

## Market {#market}

**Deep Dive**: [docs/plugins/MARKET.md](plugins/MARKET.md)

Marketplace orchestration service (L4 GameFeatures) for auctions, NPC vendor management, and price discovery. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver game economy exchange mechanics. Game-agnostic: auction house rules, vendor personality templates, fee structures, and pricing modes are configured through market definitions, ABML behaviors, and seed type definitions at deployment time. Internal-only, never internet-facing.

## Matchmaking {#matchmaking}

**Version**: 1.0.0 | **Schema**: `schemas/matchmaking-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/MATCHMAKING.md](plugins/MATCHMAKING.md)

Ticket-based matchmaking (L4 GameFeatures) with skill windows, query matching, party support, and configurable accept/decline flow. A background service processes queues at configurable intervals, expanding skill windows over time until matches form or tickets timeout. On full acceptance, creates a matchmade game session via lib-game-session with reservation tokens and publishes join shortcuts via Connect. Supports immediate match checks on ticket creation, auto-requeue on decline, and pending match state restoration on reconnection.

## Music {#music}

**Version**: 1.0.0 | **Schema**: `schemas/music-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/MUSIC.md](plugins/MUSIC.md)

Pure computation music generation (L4 GameFeatures) using formal music theory rules and narrative-driven composition. Leverages two internal SDKs: `MusicTheory` (harmony, melody, pitch, MIDI-JSON output) and `MusicStoryteller` (narrative templates, emotional state planning). Generates complete compositions, chord progressions, melodies, and voice-led voicings. Deterministic when seeded, enabling Redis caching for repeat requests. No external service dependencies -- fully self-contained computation.

## Obligation {#obligation}

**Version**: 1.0.0 | **Schema**: `schemas/obligation-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/OBLIGATION.md](plugins/OBLIGATION.md)

Contract-aware obligation tracking for NPC cognition (L4 GameFeatures), bridging the Contract service's behavioral clauses and the GOAP planner's action cost system to enable NPCs to have "second thoughts" before violating obligations. Provides dynamically-updated action cost modifiers based on active contracts (guild charters, trade agreements, quest oaths), working standalone with raw contract penalties or enriched with personality-weighted moral reasoning when character-personality data is available. Implements `IVariableProviderFactory` providing the `${obligations.*}` namespace to Actor (L2) via the Variable Provider Factory pattern. See [GitHub Issue #410](https://github.com/beyond-immersion/bannou-service/issues/410) for the original design specification ("Second Thoughts" feature).

## Organization {#organization}

**Deep Dive**: [docs/plugins/ORGANIZATION.md](plugins/ORGANIZATION.md)

Legal entity management service (L4 GameFeatures) for organizations that own assets, employ characters, enter contracts, and participate in the economy as first-class entities. A structural layer that gives economic and social entities a legal identity — shops, guilds, households, trading companies, temples, military units, criminal enterprises, and any other group that acts as a collective within the game world. Game-agnostic: organization types, role templates, governance relationships, seed growth phases, and charter requirements are all configured through seed type definitions, contract templates, and faction governance data at deployment time. Internal-only, never internet-facing.

## Procedural {#procedural}

**Deep Dive**: [docs/plugins/PROCEDURAL.md](plugins/PROCEDURAL.md)

On-demand procedural 3D asset generation service (L4 GameFeatures) using headless Houdini Digital Assets (HDAs) — self-contained parametric procedural tools packaged as `.hda` files with exposed parameter interfaces (sliders, menus, toggles, ramps) that generate infinite geometry variations from a single authored template — as parametric generation templates. A thin orchestration layer that composes existing Bannou primitives (Asset service for HDA storage and output bundling, Orchestrator for Houdini worker pool management) to deliver procedural geometry generation as an API. Game-agnostic — the service knows nothing about what it generates, it executes HDAs and returns geometry. Internal-only, never internet-facing.

## Puppetmaster {#puppetmaster}

**Version**: 1.0.0 | **Schema**: `schemas/puppetmaster-api.yaml` | **Endpoints**: 6 | **Deep Dive**: [docs/plugins/PUPPETMASTER.md](plugins/PUPPETMASTER.md)

The Puppetmaster service (L4 GameFeatures) orchestrates dynamic behaviors, regional watchers, and encounter coordination for the Arcadia game system. Provides the bridge between the behavior execution runtime (lib-actor at L2) and the asset service (lib-asset at L3), enabling dynamic ABML behavior loading that would otherwise violate the service hierarchy. Implements `IBehaviorDocumentProvider` to supply runtime-loaded behaviors to actors via the provider chain pattern. Also manages regional watcher lifecycle and resource snapshot caching for Event Brain actors. Divine actors launched as regional watchers via Puppetmaster also serve as gardener behavior actors for player experience orchestration -- see [DIVINE.md](DIVINE.md) for the architectural rationale unifying realm-tending and garden-tending under a single divine actor identity.

## Realm History {#realm-history}

**Version**: 1.0.0 | **Schema**: `schemas/realm-history-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/REALM-HISTORY.md](plugins/REALM-HISTORY.md)

Historical event participation and lore management (L4 GameFeatures) for realms. Tracks when realms participate in world events (wars, treaties, cataclysms) with role and impact tracking, and maintains machine-readable lore elements (origin myths, cultural practices, political systems) for behavior system consumption. Provides text summarization for realm archival via lib-resource. Shares storage helper abstractions with the character-history service.

## Save Load {#save-load}

**Version**: 1.0.0 | **Schema**: `schemas/save-load-api.yaml` | **Endpoints**: 26 | **Deep Dive**: [docs/plugins/SAVE-LOAD.md](plugins/SAVE-LOAD.md)

Generic save/load system (L4 GameFeatures) for game state persistence with polymorphic ownership (accounts, characters, sessions, realms). Manages save slots, versioned writes with automatic compression, delta/incremental saves via JSON Patch (RFC 6902), schema migration with forward migration paths, and rolling cleanup by save category. Uses a two-tier storage architecture: Redis hot cache for immediate acknowledgment, with async upload to MinIO via the Asset service for durable storage. Supports export/import via ZIP archives and multi-device cloud sync with conflict detection.

## Scene {#scene}

**Version**: 1.0.0 | **Schema**: `schemas/scene-api.yaml` | **Endpoints**: 19 | **Deep Dive**: [docs/plugins/SCENE.md](plugins/SCENE.md)

Hierarchical composition storage (L4 GameFeatures) for game worlds. Stores scene documents as node trees with support for multiple node types (group, mesh, marker, volume, emitter, reference, custom), scene-to-scene references with recursive resolution, an exclusive checkout/commit/discard workflow, game-specific validation rules, full-text search, and version history. Does not compute world transforms or interpret node behavior at runtime -- consumers decide what nodes mean.

## Showtime {#showtime}

**Deep Dive**: [docs/plugins/SHOWTIME.md](plugins/SHOWTIME.md)

In-game streaming metagame service (L4 GameFeatures) for simulated audience pools, hype train mechanics, streamer career progression, and real-simulated audience blending. The game-facing layer of the streaming stack -- everything that makes streaming a game mechanic rather than just a platform integration. Game-agnostic: audience personality types, hype train escalation, and streaming milestones are all configured through seed types, collection types, and configuration. Internal-only, never internet-facing.

## Status {#status}

**Version**: 1.0.0 | **Schema**: `schemas/status-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/STATUS.md](plugins/STATUS.md)

Unified entity effects query layer (L4 GameFeatures) aggregating temporary contract-managed statuses and passive seed-derived capabilities into a single query point. Any system needing "what effects does this entity have" -- combat buffs, death penalties, divine blessings, subscription benefits -- queries lib-status. Follows the "items in inventories" pattern: status templates define effect definitions, status containers hold per-entity inventory containers, and granting a status creates an item instance in that container. Contract integration is optional per-template for complex lifecycle; simple TTL-based statuses use lib-item's native decay system. Internal-only, never internet-facing.

## Storyline {#storyline}

**Version**: 1.0.0 | **Schema**: `schemas/storyline-api.yaml` | **Endpoints**: 15 | **Deep Dive**: [docs/plugins/STORYLINE.md](plugins/STORYLINE.md)

The Storyline service (L4 GameFeatures) wraps the `storyline-theory` and `storyline-storyteller` SDKs to provide HTTP endpoints for seeded narrative generation from compressed archives. Plans describe narrative arcs with phases, actions, and entity requirements -- callers (gods/regional watchers) decide whether to instantiate them. Internal-only, requires the `developer` role for all endpoints.

## Trade {#trade}

**Deep Dive**: [docs/plugins/TRADE.md](plugins/TRADE.md)

The Trade service (L4 GameFeatures) is the economic logistics and supply orchestration layer for Bannou. It provides the mechanisms for moving goods across distances over game-time, enforcing border policies, calculating supply/demand dynamics, and enabling NPC economic decision-making. Trade is to the economy what Puppetmaster is to NPC behavior -- an orchestration layer that composes lower-level primitives (Transit for movement, Currency for payments, Item/Inventory for cargo, Escrow for custody) into higher-level economic flows. Internal-only, never internet-facing.

## Utility {#utility}

**Deep Dive**: [docs/plugins/UTILITY.md](plugins/UTILITY.md)

The Utility service (L4 GameFeatures) manages infrastructure networks that continuously distribute resources across location hierarchies. It provides the topology, capacity modeling, and flow calculation that transforms Workshop point-production into location-wide service coverage -- answering "does this location have water, and where does it come from?" Where Workshop produces resources at a single point and Trade moves discrete shipments between locations, Utility models **continuous flow through persistent infrastructure** (aqueducts, sewer systems, power grids, magical conduits, messenger networks). The key gameplay consequence: when infrastructure breaks, downstream locations lose service, and the cascade of discovery, investigation, and repair creates emergent content. Internal-only, never internet-facing.

## Workshop {#workshop}

**Deep Dive**: [docs/plugins/WORKSHOP.md](plugins/WORKSHOP.md)

Time-based automated production service (L4 GameFeatures) for continuous background item generation: assign workers to blueprints, consume materials from source inventories, place outputs in destination inventories over game time. Uses lazy evaluation with piecewise rate segments for accurate production tracking across worker count changes, and a background materialization worker with fair per-entity scheduling. Game-agnostic: blueprint structures, production categories, worker types, and proficiency domains are all opaque configuration defined per game at deployment time through blueprint seeding. Internal-only, never internet-facing.

## Summary

- **Services in layer**: 42
- **Endpoints in layer**: 344

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
