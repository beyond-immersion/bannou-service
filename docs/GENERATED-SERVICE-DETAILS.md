# Generated Service Details Reference

> **Source**: `docs/plugins/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

This document provides a compact reference of all Bannou services.

## Account {#account}

**Version**: 2.0.0 | **Schema**: `schemas/account-api.yaml` | **Endpoints**: 18 | **Deep Dive**: [docs/plugins/ACCOUNT.md](plugins/ACCOUNT.md)

The Account plugin is an internal-only CRUD service (L1 AppFoundation) for managing user accounts. It is never exposed directly to the internet -- all external account operations go through the Auth service, which calls Account via lib-mesh. Handles account creation, lookup (by ID, email, or OAuth provider), updates, soft-deletion, and authentication method management (linking/unlinking OAuth providers). Email is optional -- accounts created via OAuth or Steam may have no email address, identified solely by their linked authentication methods.

## Achievement {#achievement}

**Version**: 1.0.0 | **Schema**: `schemas/achievement-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/ACHIEVEMENT.md](plugins/ACHIEVEMENT.md)

The Achievement plugin (L4 GameFeatures) provides a multi-entity achievement and trophy system with progressive/binary unlock types, prerequisite chains, rarity calculations, and platform synchronization (Steam, Xbox, PlayStation). Achievements are scoped to game services, support event-driven auto-unlock from Analytics and Leaderboard events, and include a background service for periodic rarity recalculation.

## Actor {#actor}

**Version**: 1.0.0 | **Schema**: `schemas/actor-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/ACTOR.md](plugins/ACTOR.md)

Distributed actor management and execution (L2 GameFoundation) for NPC brains, event coordinators, and long-running behavior loops. Actors output behavioral state (feelings, goals, memories) to characters -- not directly visible to players. Supports multiple deployment modes (local, pool-per-type, shared-pool, auto-scale), ABML behavior document execution with hot-reload, GOAP planning integration, and bounded perception queues with urgency filtering. Receives data from L4 services (personality, encounters, history) via the Variable Provider Factory pattern without depending on them.

## Affix {#affix}

**Deep Dive**: [docs/plugins/AFFIX.md](plugins/AFFIX.md)

Item modifier definition and generation service (L4 GameFeatures) for affix definitions, weighted random generation, validated application primitives, and stat computation. A structured layer above lib-item's opaque `instanceMetadata` that gives meaning to item modifiers: typed definitions with tiers, mod groups for exclusivity, spawn weights for probabilistic generation, and stat grants for computed item power. Any system that needs to answer "what modifiers can this item have?" or "what is this item worth?" queries lib-affix.

**Composability**: Affix definitions and generation rules are owned here. Item storage is lib-item (L2) -- affixes are written as structured JSON to `ItemInstance.instanceMetadata`. Crafting workflows that orchestrate modifier application (recipes, currency effects, multi-step enchanting) are lib-craft (L4, future) -- lib-craft calls lib-affix primitives. Loot generation that creates pre-affixed items is lib-loot (L4, future). Market search filtering by modifier properties is lib-market (L4, future). NPC item evaluation for GOAP economic decisions uses lib-affix's Variable Provider Factory.

**The foundational distinction**: lib-affix manages WHAT modifiers exist (definitions, tiers, rules, valid pools) and provides validated primitives for applying/removing them. HOW those primitives are orchestrated into gameplay-meaningful operations (crafting recipes, currency effects, multi-step enchanting processes) is lib-craft's domain. WHO creates affixed items at scale (loot tables, vendor stock, quest rewards) is lib-loot's domain. This separation means lib-affix has three independent consumer categories: generators (lib-loot) call pool generation and batch set creation; orchestrators (lib-craft) call validated application/removal primitives within workflow sessions; readers (lib-market, NPC GOAP, UI) call queries and stat computation.

**The affix metadata convention**: lib-affix defines a versioned JSON schema for `ItemInstance.instanceMetadata.affixes` that all consumers read without importing lib-affix. This convention enables lib-craft and lib-loot to read affix data from items directly, and lib-market to index affix properties for search -- without coupling those services to lib-affix at the code level. The convention is a documented data format, not an API dependency.

**Zero game-specific content**: lib-affix is a generic item modifier service. Arcadia's logos inscription metaphysics, PoE-style prefix/suffix tiers, Diablo-style legendary affixes, or a simple "quality stars" system are all equally valid configurations. Affix slot types, mod groups, generation tags, influence types, and tier structures are all opaque strings defined per game at deployment time through definition seeding.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on a Path of Exile item system complexity benchmark and the [Item & Economy Plugin Landscape](../plans/ITEM-ECONOMY-PLUGINS.md). Internal-only, never internet-facing.

## Agency {#agency}

**Deep Dive**: [docs/plugins/AGENCY.md](plugins/AGENCY.md)

The Agency service (L4 GameFeatures) manages the guardian spirit's progressive agency system -- the bridge between Seed's abstract capability data and the client's concrete UX module rendering. It answers the question: "Given this guardian spirit's accumulated experience, what can the player perceive and do?"

Three subsystems:

1. **UX Module Registry**: Definitions of available UX modules, their capability requirements, and fidelity curves. A module is a discrete UI element or interaction mode (stance selector, timing windows, material chooser, tone suggestion) that the client loads based on the spirit's capabilities.

2. **Manifest Engine**: Computes per-seed UX manifests from seed capabilities, caches results in Redis, and publishes updates when capabilities change. The manifest tells the client exactly which modules to load and at what fidelity level.

3. **Influence Registry**: Definitions of spirit influence types (nudges) that the player can send to their possessed character. Each influence maps to an Actor perception type and has compliance factors that determine how likely the character is to accept it.

Agency is the player-facing expression of PLAYER-VISION.md's core thesis: "The guardian spirit starts as nearly inert. Through accumulated experience, it gains understanding. Understanding manifests as increased control fidelity and richer UX surface area."

### What Agency Is NOT

- **Not a permission system.** Permission (L1) gates API endpoint access based on roles and states. Agency gates UX module visibility based on spirit growth. They use similar push mechanisms but serve orthogonal purposes.
- **Not a skill system.** Seed (L2) tracks capability depth and fidelity. Agency translates those numbers into UX decisions. Agency does not own growth, thresholds, or capability computation -- Seed does.
- **Not a behavior system.** Actor (L2) executes character cognition. Agency provides `${spirit.*}` variables that Actor's ABML behaviors evaluate, but Agency does not execute behaviors or make character decisions.

## Analytics {#analytics}

**Version**: 1.0.0 | **Schema**: `schemas/analytics-api.yaml` | **Endpoints**: 9 | **Deep Dive**: [docs/plugins/ANALYTICS.md](plugins/ANALYTICS.md)

The Analytics plugin (L4 GameFeatures) is the central event aggregation point for all game-related statistics. Handles event ingestion, entity summary computation, Glicko-2 skill rating calculations, and controller history tracking. Publishes score updates and milestone events consumed by Achievement and Leaderboard for downstream processing. Subscribes to game session lifecycle and character/realm history events for automatic ingestion. Unlike typical L4 services, Analytics only observes via event subscriptions -- it does not invoke L2/L4 service APIs and should not be called by L1/L2/L3 services.

## Arbitration {#arbitration}

**Deep Dive**: [docs/plugins/ARBITRATION.md](plugins/ARBITRATION.md)

Authoritative dispute resolution service (L4 GameFeatures) for competing claims that need jurisdictional ruling and enforcement. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver adjudication game mechanics.

**Composability**: Case identity and lifecycle are owned here. Jurisdiction determination uses Faction (sovereignty, territory control, authority level). Procedural workflow is Contract (the arbitration case IS a contract instance created from a procedural template). Asset division is Escrow (when rulings involve property). Ongoing obligations from rulings are Obligation (alimony, probation, reparations feed into GOAP action costs). Relationship status changes from rulings (married -> divorced, member -> exiled) use Relationship. Sovereignty disputes may involve Seed (capability-gated claims). Divine arbitration uses Puppetmaster (regional watcher gods as arbiters).

**The Quest/Escrow parallel**: Arbitration follows the same structural pattern as Quest and Escrow -- a game-flavored API layer over Contract's state machine. Quest translates "complete this objective" into contract milestones. Escrow translates "exchange these assets" into contract-guarded custody. Arbitration translates "resolve this dispute" into contract-tracked procedural steps (filing, evidence, hearing, ruling, enforcement). They are parallel orchestration layers composing the same underlying primitive (Contract), not the same service.

**Critical architectural insight**: Arbitration does not adjudicate -- it orchestrates the adjudication process. The arbiter (an NPC, a faction leader, a divine actor) makes the actual ruling decision. Arbitration provides the procedural framework, tracks the case state, enforces deadlines, and executes the ruling's consequences via prebound API calls. This is the same "orchestration not intelligence" principle that governs Quest (quest doesn't decide when objectives are complete -- the world does) and Escrow (escrow doesn't decide if conditions are met -- the arbiter does).

**Sovereignty is prerequisite**: Arbitration is meaningful only when factions distinguish between legal authority (Sovereign/Delegated) and social influence. Without sovereignty, there is no principled way to determine who has jurisdiction, whose procedures apply, or what weight a ruling carries. The `authorityLevel` field on FactionModel (described in [DISSOLUTION-DESIGN.md](../planning/DISSOLUTION-DESIGN.md)) must exist before arbitration can function. See the [Faction Sovereignty Dependency](#faction-sovereignty-dependency) section for details.

**Case types are opaque strings**: `dissolution`, `property_dispute`, `criminal_proceeding`, `trade_dispute`, `custody_inheritance`, `sovereignty_recognition`, `contract_conflict` are all just case types with different procedural templates. The arbitration service doesn't hardcode any case-type-specific logic -- it provides the framework for any authoritative resolution process. New case types require only a new procedural template in Contract and a governance data entry in the jurisdictional faction.

**NPC agency in arbitration**: An NPC with the `evaluate_consequences` cognition stage can autonomously decide to initiate, contest, or cooperate with arbitration proceedings. An unhappy NPC in a bad marriage evaluates the cost of continuing vs. filing for dissolution vs. fleeing to a permissive jurisdiction. A merchant NPC evaluates whether to contest a trade dispute ruling or accept the loss. This is emergent narrative from the intersection of sovereignty + arbitration + cognition.

**Zero Arcadia-specific content**: lib-arbitration is a generic dispute resolution service. Arcadia's specific procedural templates (dissolution-standard, dissolution-religious-annulment, exile-punitive, criminal-trial-standard), arbiter selection rules, and cultural attitudes toward litigation are configured through contract templates and faction governance data at deployment time, not baked into lib-arbitration.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on [DISSOLUTION-DESIGN.md](../planning/DISSOLUTION-DESIGN.md) and the broader orchestration patterns established by lib-quest, lib-escrow, and lib-divine. Internal-only, never internet-facing.

## Asset {#asset}

**Version**: 1.0.0 | **Schema**: `schemas/asset-api.yaml` | **Endpoints**: 20 | **Deep Dive**: [docs/plugins/ASSET.md](plugins/ASSET.md)

The Asset service (L3 AppFeatures) provides storage, versioning, and distribution of large binary assets (textures, audio, 3D models) using MinIO/S3-compatible object storage. Issues pre-signed URLs so clients upload/download directly to the storage backend, never routing raw asset data through the WebSocket gateway. Also manages bundles (grouped assets in a custom `.bannou` format with LZ4 compression), metabundles (merged super-bundles), and a distributed processor pool for content-type-specific transcoding. Used by lib-behavior, lib-save-load, lib-mapping, and lib-documentation for binary storage needs.

## Auth {#auth}

**Version**: 4.0.0 | **Schema**: `schemas/auth-api.yaml` | **Endpoints**: 19 | **Deep Dive**: [docs/plugins/AUTH.md](plugins/AUTH.md)

The Auth plugin is the internet-facing authentication and session management service (L1 AppFoundation). Handles email/password login, OAuth provider integration (Discord, Google, Twitch), Steam session ticket verification, JWT token generation/validation, password reset flows, TOTP-based MFA, and session lifecycle management. It is the primary gateway between external users and the internal service mesh -- after authenticating, clients receive a JWT and a WebSocket connect URL to establish persistent connections via lib-connect.

## Behavior {#behavior}

**Version**: 3.0.0 | **Schema**: `schemas/behavior-api.yaml` | **Endpoints**: 6 | **Deep Dive**: [docs/plugins/BEHAVIOR.md](plugins/BEHAVIOR.md)

ABML (Arcadia Behavior Markup Language) compiler and GOAP (Goal-Oriented Action Planning) runtime (L4 GameFeatures) for NPC behavior management. Provides three core subsystems: a multi-phase ABML compiler producing portable stack-based bytecode, an A*-based GOAP planner for action sequence generation from world state and goals, and a 5-stage cognition pipeline for NPC perception and intention formation. Compiled bytecode is interpreted by both the server-side ActorRunner (L2) and client SDKs. Supports streaming composition, variant-based model caching with fallback chains, and behavior bundling through the Asset service.

## Broadcast {#broadcast}

**Deep Dive**: [docs/plugins/BROADCAST.md](plugins/BROADCAST.md)

Platform streaming integration and RTMP output management service (L3 AppFeatures) for linking external streaming platforms (Twitch, YouTube, custom RTMP), ingesting real audience data, and broadcasting server-side content. The bridge between Bannou's internal world and external streaming platforms -- everything that touches a third-party streaming service goes through lib-broadcast.

**Privacy-first architecture**: This is a load-bearing design decision. Real audience data (chat messages, usernames, platform IDs) NEVER leaves lib-broadcast's process boundary as identifiable data. Raw platform events are reduced to **batched sentiment pulses** -- arrays of anonymous sentiment values with optional opaque tracking GUIDs for consistency. No platform user IDs, no message content, no personally identifiable information enters the event system. This eliminates GDPR/CCPA data deletion obligations for downstream consumers entirely.

**Two distinct broadcast modes**: Server-side content broadcasting (game cameras, game audio) requires no player consent -- it's game content. Voice room broadcasting to external platforms requires explicit consent from ALL room participants via lib-voice's broadcast consent flow. lib-broadcast subscribes to voice consent events and acts accordingly; it never initiates voice broadcasting directly.

**Composability**: Platform identity linking is owned here. Sentiment processing is owned here. RTMP output management is owned here. Audience behavior and the in-game metagame are lib-showtime (L4). Voice room management is lib-voice (L3). lib-broadcast is the privacy boundary and platform integration layer -- it touches external APIs so nothing else has to.

**The three-service principle**: lib-broadcast delivers value independently. It can broadcast game content to Twitch whether or not there's voice involved (lib-voice) or an in-game metagame (lib-showtime). It can ingest platform audience data and publish sentiment pulses whether or not anything consumes them. Each service in the voice/broadcast/showtime trio composes beautifully but never requires the others.

**Zero Arcadia-specific content**: lib-broadcast is a generic platform integration service. Which platforms are enabled, how sentiment categories map to game emotions, and what content gets broadcast are all configured via environment variables and API calls, not baked into lib-broadcast.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md). Internal-only for sentiment/broadcast management; webhook endpoints are internet-facing for platform callbacks.

## Character {#character}

**Version**: 1.0.0 | **Schema**: `schemas/character-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/CHARACTER.md](plugins/CHARACTER.md)

The Character service (L2 GameFoundation) manages game world characters for Arcadia. Characters are independent world assets (not owned by accounts) with realm-based partitioning for scalable queries. Provides standard CRUD, enriched retrieval with family tree data (from lib-relationship), and compression/archival for dead characters via lib-resource. Per the service hierarchy, Character cannot depend on L4 services (personality, history, encounters) -- callers needing that data should aggregate from L4 services directly.

## Character Encounter {#character-encounter}

**Version**: 1.0.0 | **Schema**: `schemas/character-encounter-api.yaml` | **Endpoints**: 21 | **Deep Dive**: [docs/plugins/CHARACTER-ENCOUNTER.md](plugins/CHARACTER-ENCOUNTER.md)

Character encounter tracking service (L4 GameFeatures) for memorable interactions between characters, enabling NPC memory, dialogue triggers, grudges/alliances, and quest hooks. Manages encounters (shared interaction records) with per-participant perspectives, time-based memory decay, weighted sentiment aggregation, and configurable encounter type codes. Features automatic pruning per-character and per-pair limits, and provides `${encounters.*}` ABML variables to the Actor service's behavior system via the Variable Provider Factory pattern.

## Character History {#character-history}

**Version**: 1.0.0 | **Schema**: `schemas/character-history-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/CHARACTER-HISTORY.md](plugins/CHARACTER-HISTORY.md)

Historical event participation and backstory management (L4 GameFeatures) for characters. Tracks when characters participate in world events (wars, disasters, political upheavals) with role and significance tracking, and maintains machine-readable backstory elements (origin, occupation, training, trauma, fears, goals) for behavior system consumption. Provides template-based text summarization for character compression via lib-resource. Shares storage helper abstractions with the realm-history service.

## Character Lifecycle {#character-lifecycle}

**Deep Dive**: [docs/plugins/CHARACTER-LIFECYCLE.md](plugins/CHARACTER-LIFECYCLE.md)

Generational cycle orchestration and genetic heritage service (L4 GameFeatures) for character aging, marriage, procreation, death processing, and cross-generational trait inheritance. The temporal engine that drives the content flywheel by ensuring characters are born, live, age, reproduce, and die -- and that each death produces archives feeding future content. Without this service, characters exist in a timeless limbo where nobody ages, no children are born, no natural deaths occur, and the "more play produces more content" thesis cannot function.

**Two complementary subsystems**:

| Subsystem | What It Answers | Persistence | Update Pattern |
|-----------|----------------|-------------|----------------|
| **Lifecycle** | "Where is this character in their life journey?" | Persistent with temporal advancement | Event-driven (worldstate year/season events) |
| **Heritage** | "What did this character inherit from their parents?" | Persistent, set at creation, immutable | Write-once at procreation |

**The problem this solves**: The pieces are in place -- Worldstate provides the game clock, Character provides CRUD, Relationship has PARENT/SPOUSE/CHILD types, Organization models households with succession, Resource compresses archives, Character-History tracks backstory, Disposition models drives and fulfillment, Storyline generates narratives from archives. But nobody orchestrates the lifecycle itself. Characters don't age. Marriage is a Relationship record without ceremony or household consequences. Procreation doesn't exist -- there is zero mechanism for two characters to produce a child with inherited traits. Death is a deletion event, not a transformation. The content flywheel has all its components but no ignition switch.

**The content flywheel ignition**: From the [Vision](../../arcadia-kb/VISION.md): "Characters age, marry, have children, and die" and "Death transforms, not ends -- the underworld offers its own gameplay." Every north star depends on lifecycle working:
- **Living Game Worlds**: Generational dynasties, family businesses persisting across lifetimes, NPCs pursuing long-term aspirations that span beyond their own mortality
- **Content Flywheel**: Death produces archives. Archives feed Storyline. Storyline produces scenarios. Scenarios produce new experiences. More characters dying = more content generated. Year 1 yields ~1,000 story seeds; Year 5 yields ~500,000 -- but only if characters actually complete life cycles
- **Design Principle 4** ("Death Creates, Not Destroys"): Fulfillment calculation determines how much logos flows to the guardian spirit. A fulfilled life enriches the player's seed. An unfulfilled life creates unfinished-business story seeds. Death is the mechanism that converts lived experience into generative material

**Character identity is the opt-in**: This service is deliberately character-specific. Characters are the entity type that opts into the full cognitive stack (personality, encounters, history, disposition, hearsay, lifecycle). Animals, creatures, and other living entities that don't have guardian spirits, personality profiles, or generational continuity don't need this service. A future `lib-habitat` or `lib-ecology` service could handle population dynamics and creature breeding at a simpler level, but that is a separate concern. If an entity is complex enough to need lifecycle management -- if it has personality, drives, social relationships, and generational continuity -- it IS a character, whether it looks like a human, an elf, a sentient dragon, or a talking cat.

**Heritage as embedded subsystem**: Heritage (genetic trait inheritance) lives within this service rather than as a standalone plugin. The primary write path for genetic data is procreation -- the moment this service orchestrates. The read path is thin (a variable provider and occasional queries from Quest/Divine). This follows the MusicTheory/Music parallel: complex computation, thin API surface. If heritage grows complex enough to warrant its own service boundary (species hybridization becomes massive, bloodline politics becomes a full system), it can be extracted later.

**The Fulfillment Principle**: From the [Player Vision](../../arcadia-kb/PLAYER-VISION.md): "more fulfilled in life = more logos flow to the guardian spirit." Fulfillment is computed from Disposition's drive system -- a character who achieved their `master_craft` drive (satisfaction 0.9) contributed more to the guardian spirit than one who died with all drives frustrated. This creates a player incentive to guide characters toward their aspirations, not just toward power or wealth. The guardian spirit seed grows proportionally to the sum of fulfilled drives across all characters in the household's history.

**Composability**: Lifecycle identity, aging, and heritage computation are owned here. Marriage ceremony is Contract (L1). Spousal bond is Relationship (L2). Household structure is Organization (L4). Emotional state is Disposition (L4). Genetic trait expression feeds into Character-Personality (L4). Backstory seeding feeds into Character-History (L4). Archive compression is Resource (L1). Narrative generation from archives is Storyline (L4). Guardian spirit evolution is Seed (L2). This service composes them all into a coherent generational lifecycle, following the same orchestration pattern as Quest (over Contract), Escrow (over Currency + Item), and Organization (over Currency + Inventory + Contract + Relationship).

**Zero Arcadia-specific content**: lib-character-lifecycle is a generic generational lifecycle service. Arcadia's specific lifecycle stages (child thresholds, elder onset, species longevity), genetic trait definitions (which traits are heritable, dominance rules), marriage customs (ceremony contract templates, dowry mechanics), and death processing (underworld pathways, fulfillment thresholds) are configured through lifecycle configuration and seed data at deployment time, not baked into lib-character-lifecycle. A game with immortal characters could disable aging entirely. A game with cloning instead of procreation could use the heritage engine with a single-parent recombination mode.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on analysis of the generational gap across the entire codebase -- the missing lifecycle orchestrator that Organization Phase 5 explicitly awaits, the content flywheel loop that cannot turn without death processing, and the genetic inheritance system named in the Vision but absent from any existing or planned service. Internal-only, never internet-facing.

## Character Personality {#character-personality}

**Version**: 1.0.0 | **Schema**: `schemas/character-personality-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/CHARACTER-PERSONALITY.md](plugins/CHARACTER-PERSONALITY.md)

Machine-readable personality traits and combat preferences (L4 GameFeatures) for NPC behavior decisions. Features probabilistic personality evolution based on character experiences and combat preference adaptation based on battle outcomes. Traits are floating-point values on bipolar axes that shift based on experience intensity. Provides `${personality.*}` and `${combat.*}` ABML variables to the Actor service via the Variable Provider Factory pattern.

## Chat {#chat}

**Version**: 1.0.0 | **Schema**: `schemas/chat-api.yaml` | **Endpoints**: 28 | **Deep Dive**: [docs/plugins/CHAT.md](plugins/CHAT.md)

The Chat service (L1 AppFoundation) provides universal typed message channel primitives for real-time communication. Room types determine valid message formats (text, sentiment, emoji, custom-validated payloads), with rooms optionally governed by Contract instances for lifecycle management. Supports ephemeral (Redis TTL) and persistent (MySQL) message storage, participant moderation (kick/ban/mute), rate limiting via atomic Redis counters, and automatic idle room cleanup. Three built-in room types (text, sentiment, emoji) are registered on startup. Internal-only, never internet-facing.

## Collection {#collection}

**Version**: 1.0.0 | **Schema**: `schemas/collection-api.yaml` | **Endpoints**: 20 | **Deep Dive**: [docs/plugins/COLLECTION.md](plugins/COLLECTION.md)

The Collection service (L2 GameFoundation) manages universal content unlock and archive systems for collectible content: voice galleries, scene archives, music libraries, bestiaries, recipe books, and custom types. Follows the "items in inventories" pattern: entry templates define what can be collected, collection instances create inventory containers per owner, and granting an entry creates an item instance in that container. Unlike License (which orchestrates contracts for LP deduction), Collection uses direct grants without contract delegation. Features dynamic content selection based on unlocked entries and area theme configurations. Collection types are opaque strings (not enums), allowing new types without schema changes. Dispatches unlock notifications to registered `ICollectionUnlockListener` implementations via DI for guaranteed in-process delivery (e.g., Seed growth pipeline). Internal-only, never internet-facing.

## Connect {#connect}

**Version**: 2.0.0 | **Schema**: `schemas/connect-api.yaml` | **Endpoints**: 6 | **Deep Dive**: [docs/plugins/CONNECT.md](plugins/CONNECT.md)

WebSocket-first edge gateway (L1 AppFoundation) providing zero-copy binary message routing between game clients and backend services. Manages persistent connections with client-salted GUID generation for cross-session security, three connection modes (external, relayed, internal), session shortcuts for game-specific flows, reconnection windows, and per-session RabbitMQ subscriptions for server-to-client event delivery. Internet-facing (the primary client entry point alongside Auth). Registered as Singleton (unusual for Bannou) because it maintains in-memory connection state.

## Contract {#contract}

**Version**: 1.0.0 | **Schema**: `schemas/contract-api.yaml` | **Endpoints**: 30 | **Deep Dive**: [docs/plugins/CONTRACT.md](plugins/CONTRACT.md)

Binding agreement management (L1 AppFoundation) between entities with milestone-based progression, consent flows, and prebound API execution on state transitions. Contracts are reactive: external systems report condition fulfillment via API calls; contracts store state, emit events, and execute callbacks. Templates define structure (party roles, milestones, terms, enforcement mode); instances track consent, sequential progression, and breach handling. Used as infrastructure by lib-quest (quest objectives map to contract milestones) and lib-escrow (asset-backed contracts via guardian locking). Has a known L1-to-L2 hierarchy violation: depends on lib-location for territory constraint checking.

## Craft {#craft}

**Deep Dive**: [docs/plugins/CRAFT.md](plugins/CRAFT.md)

Recipe-based crafting orchestration service (L4 GameFeatures) for production workflows, item modification, and skill-gated crafting execution. A thin orchestration layer that composes existing Bannou primitives: lib-item for storage, lib-inventory for material consumption and output placement, lib-contract for multi-step session state machines, lib-currency for costs, and lib-affix for modifier operations on existing items. Any system that needs to answer "can this entity craft this recipe?" or "what happens when step 3 completes?" queries lib-craft.

**Composability**: Recipe definitions and crafting session management are owned here. Item storage is lib-item (L2). Container placement is lib-inventory (L2). Session state machines are lib-contract (L1). Currency costs are lib-currency (L2). Modifier definitions and application primitives are lib-affix (L4, soft). Loot generation that creates pre-crafted items at scale is lib-loot (L4, future). NPC crafting decisions via GOAP use lib-craft's Variable Provider Factory.

**The foundational distinction**: lib-craft manages HOW items are created and transformed -- recipes, steps, materials, skill requirements, station constraints, quality formulas, discovery. WHAT modifiers exist and their generation rules is lib-affix's domain. WHO triggers crafting (players, NPCs, automated systems) is the caller's concern. This means lib-craft has two primary consumer patterns: production consumers (NPC blacksmiths, player crafters, automated factories) that create new items from materials, and modification consumers (NPC enchanters, player crafters using currency items) that transform existing items using lib-affix primitives.

**Two recipe paradigms**:

| Paradigm | What Happens | Example | Delegates To |
|----------|-------------|---------|-------------|
| **Production** | Inputs consumed, outputs created | Smelt ore into ingots, forge sword from steel | lib-item (create), lib-inventory (consume/place) |
| **Modification** | Existing item transformed in place | Reroll affixes, add enchantment, corrupt | lib-affix (apply/remove/reroll/state), lib-item (metadata write) |
| **Extraction** | Existing item destroyed, components recovered | Salvage weapon for materials, disenchant for reagents | lib-item (destroy), lib-inventory (place outputs) |

**Zero game-specific content**: lib-craft is a generic recipe execution engine. Arcadia's 37+ authentic crafting processes (smelting, tanning, weaving, alchemy, enchanting), PoE-style currency modification (chaos orbs, exalted orbs, fossils), a simple mobile game's "combine 3 items" mechanic, or an idle game's automated production chains are all equally valid recipe configurations. Recipe types, proficiency domains, station types, tool categories, and quality formulas are all opaque strings defined per game at deployment time through recipe seeding.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on the Arcadia crafting vision (37+ authentic processes, NPC-driven economy, progressive player agency) and the [Item & Economy Plugin Landscape](../plans/ITEM-ECONOMY-PLUGINS.md). Internal-only, never internet-facing.

## Currency {#currency}

**Version**: 1.0.0 | **Schema**: `schemas/currency-api.yaml` | **Endpoints**: 32 | **Deep Dive**: [docs/plugins/CURRENCY.md](plugins/CURRENCY.md)

Multi-currency management service (L2 GameFoundation) for game economies. Handles currency definitions with scope/realm restrictions, wallet lifecycle management, balance operations (credit/debit/transfer with idempotency-key deduplication), authorization holds (reserve/capture/release), currency conversion via exchange-rate-to-base pivot, and escrow integration (deposit/release/refund endpoints consumed by lib-escrow). Features a background autogain worker for passive income and transaction history with configurable retention. All mutating balance operations use distributed locks for multi-instance safety.

## Disposition {#disposition}

**Deep Dive**: [docs/plugins/DISPOSITION.md](plugins/DISPOSITION.md)

Emotional synthesis and aspirational drive service (L4 GameFeatures) for NPC inner life. Maintains per-character **feelings** about specific entities (other characters, locations, factions, organizations, and the guardian spirit) and **drives** (long-term aspirational goals that shape behavior priorities). Feelings are not raw data -- they are the personality-filtered, experience-weighted, hearsay-colored subjective emotional state that a character carries. Drives are not quest objectives -- they are intrinsic motivations that emerge from personality, circumstance, and accumulated experience.

**The problem this solves**: The current NPC cognition pipeline has all the raw data needed to infer emotional state -- encounter sentiment, personality traits, relationship bonds, backstory elements, hearsay beliefs -- but no service that synthesizes these into a coherent directed emotional response. An NPC either has direct encounter data (cold mathematical sentiment) or has nothing. There is no mechanism for "Kael feels grateful toward Mira" as a persistent, evolving emotional state that colors future behavior, nor for "Kael aspires to become the greatest blacksmith in the realm" as an intrinsic motivation that shapes daily decisions.

**Two complementary subsystems**:

| Subsystem | What It Answers | Persistence | Update Pattern |
|-----------|----------------|-------------|----------------|
| **Feelings** | "How do I feel about X right now?" | Persistent with decay | Event-driven + periodic synthesis |
| **Drives** | "What do I aspire to become/achieve?" | Persistent with evolution | Experience-driven + periodic evaluation |

**Feelings are directed emotional states**: Not personality traits (self-model) and not encounter records (factual data). Feelings are the subjective interpretation layer where personality meets experience. A character with high aggression interprets a neutral encounter differently than a peaceful character. A character who heard rumors of danger feels differently about a location than one who has visited it. Feelings persist beyond their causes -- resentment lingers after the betrayer is gone, gratitude endures after the helper has moved on.

**Drives are intrinsic motivations**: Not quest objectives (externally assigned) and not backstory elements (historical context). Drives emerge from the intersection of personality, circumstance, and accumulated experience. A character who grew up poor may drive toward wealth. A character who was betrayed may drive toward self-reliance. A character who witnessed injustice may drive toward becoming a protector. Drives shape GOAP goal priorities, making characters pursue long-term aims without needing explicit quest chains to motivate every decision.

**The base + modifier model**: Feelings use a dual-layer computation:
- **Base**: Computed from source services (personality-filtered encounter sentiment + hearsay beliefs + relationship-type defaults). Drifts as source data changes.
- **Modifier**: Persistent emotional residue that overlays the computed base. Betrayal trauma, lingering gratitude, spirit resentment. Decays slowly but can be reinforced by events.
- **Effective value**: `clamp(base + modifier, -1.0, 1.0)`

This means feelings are never purely computed (they have emotional memory) and never purely static (they respond to changing circumstances).

**Composability**: Disposition does not replace any existing service. Encounter sentiment, personality traits, hearsay beliefs, and relationship bonds continue to provide their own raw variable namespaces. Disposition synthesizes them into a higher-level emotional state that behavior authors can use when they want characters to "feel" rather than "compute." When lib-disposition is disabled, NPCs fall back to raw data composition in ABML expressions -- existing systems work unchanged.

**The guardian spirit dimension**: Characters feeling about the player/guardian spirit is the mechanical implementation of Design Principle 1 ("Characters Are Independent Entities"). A character pushed against their nature by the spirit develops resentment. A character guided well develops trust. This feeds directly into the compliance/resistance system -- the character's willingness to follow the spirit's nudges is proportional to their feelings about the spirit.

**The drive dimension**: Without drives, characters are reactive -- they respond to stimuli but don't pursue long-term goals. An adventurer doesn't just want to be an adventurer; they want to be an S-Class adventurer. A blacksmith doesn't just forge; they aspire to create a masterwork. Some characters lack drives entirely (layabouts, doing the minimum to survive), which is its own depth. Drives create the "chasing your dreams" mechanic from the vision, where characters have intrinsic motivations that shape their daily decisions without requiring quest chains to orchestrate every step.

**Zero Arcadia-specific content**: lib-disposition is a generic emotional synthesis and aspirational drive service. Arcadia's specific feeling axes, drive types, guardian spirit mechanics, and synthesis weights are configured through disposition configuration and seed data at deployment time, not baked into lib-disposition.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on analysis of the character perception gap across lib-character-encounter, lib-character-personality, lib-character-history, lib-relationship, and the planned lib-hearsay service. Internal-only, never internet-facing.

## Divine {#divine}

**Version**: 1.0.0 | **Schema**: `schemas/divine-api.yaml` | **Endpoints**: 22 | **Deep Dive**: [docs/plugins/DIVINE.md](plugins/DIVINE.md)

Pantheon management service (L4 GameFeatures) for deity entities, divinity economy, and blessing orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver divine game mechanics: god identity is owned here, behavior runs via Actor/Puppetmaster, domain power via Seed, divinity resource via Currency, blessings via Collection/Status, and follower bonds via Relationship. Gods influence characters indirectly through the character's own Actor -- a god's Actor monitors event streams and makes decisions, but the character's Actor receives the consequences. Blessings are entity-agnostic (characters, accounts, deities, or any entity type can receive them). All endpoints are currently stubbed (return `NotImplemented`); see the implementation plan at `docs/plans/DIVINE.md` for the full specification.

## Documentation {#documentation}

**Version**: 1.0.0 | **Schema**: `schemas/documentation-api.yaml` | **Endpoints**: 27 | **Deep Dive**: [docs/plugins/DOCUMENTATION.md](plugins/DOCUMENTATION.md)

Knowledge base API (L3 AppFeatures) designed for AI agents (SignalWire SWAIG, OpenAI function calling, Claude tool use) with full-text search, natural language query, and voice-friendly summaries. Manages documentation within namespaces, supporting manual CRUD and automated git repository synchronization (git-bound namespaces reject mutations, enforcing git as single source of truth). Features browser-facing GET endpoints that render markdown to HTML (unusual exception to Bannou's POST-only pattern). Two background services handle index rebuilding and periodic repository sync.

## Dungeon {#dungeon}

**Deep Dive**: [docs/plugins/DUNGEON.md](plugins/DUNGEON.md)

Dungeon lifecycle orchestration service (L4 GameFeatures) for living dungeon entities that perceive, grow, and act autonomously within the Bannou actor system. A thin orchestration layer (like Divine over Currency/Seed/Collection, Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver dungeon-as-actor game mechanics.

**Composability**: Dungeon core identity is owned here. Dungeon behavior is Actor (event brain) via Puppetmaster. Dungeon growth is Seed (`dungeon_core` seed type). Dungeon master bond is Contract. Mana economy is Currency. Physical layout is Save-Load + Mapping. Visual composition is Scene. Memory items are Item. Monster spawning and trap activation are dungeon-specific APIs orchestrated by lib-dungeon. Player-facing dungeon master experience is Gardener (dungeon garden type). Procedural chamber generation is a future integration with lib-procedural (Houdini backend).

**The divine actor parallel**: Dungeon cores follow the same structural pattern as divine actors -- event brain actors launched via Puppetmaster, backed by seeds for progressive growth, with a currency-based economy and bonded relationships. The difference is in the *ceremony*: lib-divine orchestrates blessings, divinity economy, and follower management; lib-dungeon orchestrates monster spawning, trap activation, memory manifestation, and master bonds. They are parallel orchestration layers composing the same underlying primitives (Actor, Seed, Currency, Contract, Gardener), not the same service. This mirrors how Quest and Escrow both compose Contract but provide different game-flavored APIs.

**Critical architectural insight**: Dungeon cores influence characters through the character's own Actor, not directly. A dungeon core's Actor (event brain) monitors domain events and makes decisions; the bonded master's character Actor receives commands as perceptions, gated by the master's `dungeon_master` seed capabilities. This is the same indirect influence pattern used by divine actors (gods influence through the character's Actor, not by controlling the character directly).

**Two mastery patterns**: When a character bonds with a dungeon core, the relationship takes one of two forms depending on the player's choice and the household context. **Pattern A (Full Split)**: The character separates from their household (a contractual split governed by faction norms and obligation), and the player commits one of their 3 account seed slots to a `dungeon_master` seed. The dungeon becomes a separate game -- selectable from the void as an independent experience with its own garden. **Pattern B (Bonded Role)**: The character stays in their household and gains a character-level `dungeon_master` seed. The dungeon influence layers onto gameplay while the player is actively controlling that character, but drops away when switching to another household member. Pattern A is a specific case of the general household split mechanic (which applies to branch families, divorces, and any household fragmentation). Pattern B is a "side gig" that doesn't change the account structure.

**The dungeon as garden**: In Pattern A, the dungeon IS the player's garden -- a full conceptual space with its own UX surface, entity associations, and gardener behavior (the dungeon core actor). In Pattern B, the dungeon influence is transient -- it layers onto the existing garden while the bonded character is selected but doesn't replace it. For adventurers entering the dungeon, it is always a physical game location regardless of which pattern the master chose.

**Two seed types, one pair**: The dungeon system introduces two seed types that grow in parallel: `dungeon_core` (the dungeon's own progressive growth -- mana capacity, genetic library, trap sophistication, spatial control, memory depth) and `dungeon_master` (the bonded entity's growth in the mastery role -- perception, command, channeling, coordination). The `dungeon_master` seed can be account-owned (Pattern A -- the spirit's relationship to the dungeon, persisting across character death) or character-owned (Pattern B -- one character's role, tied to that character's lifecycle). Seeds track growth in *roles*, not growth in *entities*.

**Zero Arcadia-specific content**: lib-dungeon is a generic dungeon management service. Arcadia's personality types (martial, memorial, festive, scholarly), specific creature species, and narrative manifestation styles are configured through ABML behaviors and seed type definitions at deployment time, not baked into lib-dungeon.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on [DUNGEON-AS-ACTOR.md](../planning/DUNGEON-AS-ACTOR.md) and the broader architectural patterns established by lib-divine, lib-gardener, and lib-puppetmaster. Internal-only, never internet-facing.

## Environment {#environment}

**Deep Dive**: [docs/plugins/ENVIRONMENT.md](plugins/ENVIRONMENT.md)

Environmental state service (L4 GameFeatures) providing weather simulation, temperature modeling, atmospheric conditions, and ecological resource availability for game worlds. Consumes temporal data from Worldstate (L2) -- season, time of day, calendar boundaries -- and translates it into environmental conditions that affect NPC behavior, production, trade, loot generation, and player experience. The missing ecological layer between Worldstate's clock and the behavioral systems that already reference environmental data that doesn't exist.

**The problem this solves**: The codebase references environmental data everywhere with no provider. ABML behaviors check `${world.weather.temperature}` -- phantom variable, no provider. Mapping has a `TtlWeatherEffects` spatial layer (600s TTL) ready to receive weather data that nobody publishes. Ethology's environmental overrides were designed to compose with ecological conditions that don't exist. Loot tables reference seasonal resource availability with no authority to consult. Trade routes reference environmental conditions for route activation. Market's economy planning documents reference seasonal trade and harvest cycles. Workshop's production modifiers reference seasonal rates. All of these point at a service that was never built.

**What this service IS**:
1. A **weather simulation** -- per-realm, per-location weather computed from climate templates, season, time of day, and deterministic noise
2. A **temperature model** -- base temperature from season + time-of-day curves + altitude/biome modifiers + weather modifiers
3. An **atmospheric condition provider** -- precipitation type/intensity, wind speed/direction, humidity, visibility, cloud cover
4. A **resource availability authority** -- seasonal ecological abundance per biome, affected by weather and divine intervention
5. A **variable provider** -- `${environment.*}` namespace for ABML behavior expressions
6. A **weather event system** -- time-bounded environmental phenomena (storms, droughts, blizzards, heat waves) registered by divine actors or scheduled from climate templates

**What this service is NOT**:
- **Not a clock** -- time is Worldstate's concern. Environment consumes time, it doesn't define it.
- **Not a spatial engine** -- where things are is Location and Mapping's concern. Environment answers "what are conditions like HERE?"
- **Not a physics simulation** -- rain doesn't pool, wind doesn't blow objects, snow doesn't accumulate. Those are client-side rendering concerns. Environment provides the DATA that clients render.
- **Not a biome definition service** -- biome types are Environment's own domain data, stored as location-climate bindings in Environment's own state stores. Environment computes conditions WITHIN biomes, it doesn't define biome boundaries. Location doesn't know about biomes.
- **Not Ethology** -- Ethology provides behavioral archetypes and nature values. Environment provides the conditions that Ethology's environmental overrides react to. They compose, they don't overlap.

**Deterministic weather**: Weather is not random. Given a realm, a location, a game-day, and a season, the weather is deterministic -- hash the realm ID + game-day number + climate template to produce consistent weather. The same location on the same game-day always has the same weather across all server nodes, across restarts, across lazy evaluation windows. This follows the same principle as Ethology's deterministic individual noise: consistency without per-instance storage.

**Why deterministic?** Random weather would produce different conditions on different nodes in a multi-instance deployment, break lazy evaluation (weather between two timestamps must be reproducible), and make NPC weather-aware decisions inconsistent. Deterministic weather means a farmer NPC deciding whether to plant today gets the same answer regardless of which server node processes the request.

**Divine weather manipulation**: Gods (via Puppetmaster/Actor) can register weather overrides that replace or modify the deterministic baseline for a location or realm. A storm god summons a thunderstorm. A nature deity blesses a drought-stricken region with rain. These overrides are time-bounded and stored as explicit records, layered on top of the deterministic baseline. When the override expires, weather returns to the deterministic pattern.

**Zero game-specific content**: lib-environment is a generic environmental state engine. Arcadia's specific biome types (temperate_forest, alpine, desert), weather distributions (70% chance of rain in spring forests), and temperature curves are configured through climate template seeding at deployment time. A space game could model atmospheric composition and radiation levels. A survival game could model wind chill and hypothermia risk. The service stores float values against string-coded condition axes -- it doesn't care what the conditions mean.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on analysis of the environmental gap across the codebase -- the phantom `${world.weather.temperature}` ABML references, the `TtlWeatherEffects` Mapping layer, the seasonal availability flags in economy architecture, the environmental overrides in Ethology designed to compose with data that doesn't exist, and Worldstate's explicit exclusion of weather from its scope. Internal-only, never internet-facing.

## Escrow {#escrow}

**Version**: 1.0.0 | **Schema**: `schemas/escrow-api.yaml` | **Endpoints**: 22 | **Deep Dive**: [docs/plugins/ESCROW.md](plugins/ESCROW.md)

Full-custody orchestration layer (L4 GameFeatures) for multi-party asset exchanges. Manages the complete escrow lifecycle from creation through deposit collection, consent gathering, condition verification, and final release or refund. Supports four escrow types (two-party, multi-party, conditional, auction) with three trust modes and a 13-state finite state machine. Handles currency, items, contracts, and extensible custom asset types -- calling lib-currency and lib-inventory directly for asset movements. Integrates with lib-contract for conditional releases where contract fulfillment triggers escrow completion. See Release Modes section below for configurable confirmation flows.

## Ethology {#ethology}

**Deep Dive**: [docs/plugins/ETHOLOGY.md](plugins/ETHOLOGY.md)

Species-level behavioral archetype registry and nature resolution service (L4 GameFeatures) for providing structured behavioral defaults to any entity that runs through the Actor behavior system. The missing middle ground between "hardcoded behavior document defaults" (every wolf is identical) and "full character cognitive stack" (8+ variable providers, per-entity persistent state). Without this service, non-character entities have zero individuality -- a wolf behaves exactly like every other wolf, a bear uses the same defaults as a boar, and the living world feels mechanical rather than alive at the ecosystem level.

**The gap this fills**: The Actor behavior system has a clean, rich cognitive stack for characters:

| Provider | Service | What It Answers |
|----------|---------|----------------|
| `${personality.*}` | Character-Personality (L4) | "What kind of temperament does this character have?" |
| `${heritage.*}` | Character-Lifecycle (L4) | "What did this character inherit genetically?" |
| `${disposition.*}` | Disposition (L4) | "How does this character feel and what do they aspire to?" |
| `${encounters.*}` | Character-Encounter (L4) | "What memorable interactions has this character had?" |
| `${backstory.*}` | Character-History (L4) | "What biographical events shaped this character?" |
| `${obligations.*}` | Obligation (L4) | "What contractual commitments constrain this character?" |
| `${quest.*}` | Quest (L2) | "What objectives is this character pursuing?" |
| `${seed.*}` | Seed (L2) | "What capabilities has this entity grown into?" |

For non-character entities -- wolves, bears, monsters, dungeon creatures, wildlife -- **none of these providers fire.** The actor loads `creature_base.yaml` and gets hardcoded context defaults:

```yaml
context:
  aggression_level: 0.5     # Same for every creature
  territory_radius: 50.0    # Same for every creature
  fear_threshold: 0.3       # Same for every creature
  hunger_threshold: 0.6     # Same for every creature
```

Every wolf is identical. Every bear is identical. A wolf and a bear sharing the same behavior document have the same defaults. The only differentiation mechanism is which ABML document loads (via the variant chain), not what values feed into that document. This is the gap.

**What this is NOT**: This is not a genome service. Dungeon creatures are pneuma echoes -- they don't have DNA, don't breed, don't pass traits to offspring. Character genetics belongs to Character-Lifecycle's Heritage Engine. This is not a personality service -- personality is individual experience-driven temperament (nurture), while nature is species-defined behavioral tendency (what you ARE by birth). This is not a seed type -- seeds are progressive growth that starts empty and accumulates over time; a wolf doesn't progressively earn being territorial, it IS territorial from the moment it exists.

**What this IS**: A structured definition of species-level behavioral baselines with hierarchical overrides (realm, location) and per-individual deterministic noise, exposed as a variable provider to the Actor behavior system. Ethology is the scientific study of animal behavior, especially innate behavioral patterns -- exactly the concept that's missing.

**Three-layer resolution**: Nature values are computed from three layers:

| Layer | Source | Example |
|-------|--------|---------|
| **Species archetype** (base) | Species-level behavioral template | "Wolves are aggressive (0.7), pack-social (0.9)" |
| **Environmental override** | Realm and location modifications | "Ironpeak wolves: aggression +0.15 (harsh environment)" |
| **Individual noise** | Deterministic hash from entity ID | "This wolf: aggression +0.03 (consistent per-entity)" |

Individual noise is **deterministic** -- hash the entity ID + trait code to get a consistent offset within a configured noise amplitude. No per-entity storage needed. The same wolf always has the same noise. This is cheap, stateless, and supports 100,000+ creatures without per-entity state store entries.

**Character delegation**: When the nature provider encounters a character ID, it checks whether Heritage data is available. If Heritage is loaded (the full cognitive stack is active), Heritage values take precedence -- genetics are more specific than species archetypes. If Heritage is unavailable (character created without lifecycle tracking, or Character-Lifecycle plugin not enabled), the provider falls back to species archetype + noise. This makes `${nature.*}` the universal baseline that Heritage refines for characters.

**The living ecosystem thesis**: From the [Vision](../../arcadia-kb/VISION.md): "The world is alive whether or not a player is watching." For this to feel true at the ecosystem level, animals and creatures need perceptible individuality. The alpha wolf that's slightly more aggressive and territorial than the omega. The old bear that's less curious and more cautious. The pack of wild dogs where each has distinct behavioral tendencies. Without per-individual variation, ecosystems feel like tiled patterns rather than living populations.

**Dungeon creature integration**: VISION.md describes `${dungeon.genetic_library.*}` -- the dungeon core's catalog of monster types. When a dungeon spawns a creature, the creature needs behavioral defaults. lib-ethology provides those defaults via the species archetype. The dungeon core can optionally apply its own modifications (empowered monsters, mutated behaviors) as overrides registered at the dungeon-instance level, layering dungeon aesthetics on top of species baselines.

**Zero Arcadia-specific content**: lib-ethology is a generic behavioral archetype service. Arcadia's specific axes (aggression, territoriality, pack behavior) are configured through archetype definitions at deployment time, not baked into lib-ethology. A horror game could define axes like `stalking_patience` and `ambush_preference`. A farming sim could define `tamability` and `herd_cohesion`. The service is axis-agnostic -- it stores float values against string-coded behavioral axes, not a fixed set of traits.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on analysis of the behavioral gap identified across creature cognition templates, the archetype definition system, and the variable provider landscape. The `creature_base.yaml` behavior and `archetype-definitions.yaml` schema already exist with creature support, but lack a structured data source for per-species, per-environment, per-individual behavioral variation. Internal-only, never internet-facing.

## Faction {#faction}

**Version**: 1.0.0 | **Schema**: `schemas/faction-api.yaml` | **Endpoints**: 31 | **Deep Dive**: [docs/plugins/FACTION.md](plugins/FACTION.md)

The Faction service (L4 GameFeatures) models factions as seed-based living entities whose capabilities emerge from growth, not static assignment. As a faction's seed grows through phases (nascent, established, influential, dominant), capabilities unlock: norm definition, enforcement tiers, territory claiming, and trade regulation. Its primary consumer is lib-obligation, which queries faction norms to produce GOAP action cost modifiers for NPC cognition -- resolving a hierarchy of guild, location, and realm baseline norms into a merged norm set. Supports guild memberships with role hierarchy, parent/child organizational structure, territory claims, and inter-faction political connections modeled as seed bonds via lib-seed. Internal-only, never internet-facing.

## Game Service {#game-service}

**Version**: 1.0.0 | **Schema**: `schemas/game-service-api.yaml` | **Endpoints**: 5 | **Deep Dive**: [docs/plugins/GAME-SERVICE.md](plugins/GAME-SERVICE.md)

The Game Service is a minimal registry (L2 GameFoundation) that maintains a catalog of available games/applications (e.g., Arcadia, Fantasia) that users can subscribe to. Provides simple CRUD operations for managing service definitions, with stub-name-based lookup for human-friendly identifiers. Internal-only, never internet-facing. Referenced by nearly all L2/L4 services for game-scoping operations.

## Game Session {#game-session}

**Version**: 2.0.0 | **Schema**: `schemas/game-session-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/GAME-SESSION.md](plugins/GAME-SESSION.md)

Hybrid lobby/matchmade game session management (L2 GameFoundation) with subscription-driven shortcut publishing and voice integration. Manages two session types: **lobby** sessions (persistent, per-game-service entry points auto-created for subscribed accounts) and **matchmade** sessions (pre-created by matchmaking with reservation tokens and TTL-based expiry). Integrates with Permission for `in_game` state tracking, Voice for room lifecycle, and Subscription for account eligibility. Publishes WebSocket shortcuts to connected clients for one-click game join and supports per-game horizontal scaling via `SupportedGameServices` partitioning.

## Gardener {#gardener}

**Version**: 1.0.0 | **Schema**: `schemas/gardener-api.yaml` | **Endpoints**: 24 | **Deep Dive**: [docs/plugins/GARDENER.md](plugins/GARDENER.md)

Player experience orchestration service (L4 GameFeatures) and the player-side counterpart to Puppetmaster: where Puppetmaster orchestrates what NPCs experience, Gardener orchestrates what players experience. A "garden" is an abstract conceptual space (lobby, in-game, housing, void/discovery) that a player inhabits, with Gardener managing their gameplay context, entity associations, and event routing. Provides the APIs and infrastructure that divine actors (running via Puppetmaster on the L2 Actor runtime) use to manipulate player experiences -- behavior-agnostic, providing primitives not policy. Currently implements the void/discovery garden type only; the broader garden concept (multiple types, garden-to-garden transitions) is the architectural target. Internal-only, never internet-facing.

## Hearsay {#hearsay}

**Deep Dive**: [docs/plugins/HEARSAY.md](plugins/HEARSAY.md)

Social information propagation and belief formation service (L4 GameFeatures) for NPC cognition. Maintains per-character **beliefs** about norms, other characters, and locations -- what an NPC *thinks* they know vs. what is objectively true. Beliefs are acquired through information channels (direct observation, official decree, social contact, rumor, cultural osmosis), carry confidence levels, converge toward reality over time, and can be intentionally manipulated by external actors (gods, propagandists, gossip networks).

**The problem this solves**: The current morality pipeline (Faction norms -> Obligation costs -> GOAP modifiers) is a perfect-information system. When a new faction takes over, every NPC instantly knows and internalizes all new rules. When a character commits a crime, only direct witnesses know about it. NPCs have no mechanism for forming impressions of characters they've never met, learning about distant events, or being influenced by misinformation. Real social knowledge is delayed, fuzzy, manipulable, and self-correcting. Hearsay models this.

**Three belief domains**:

| Domain | Subject | Example Belief | What It Modulates |
|--------|---------|---------------|-------------------|
| **Norm beliefs** | Faction norms at locations | "Theft is severely punished here" | Perceived obligation costs (parallel to `${obligations.*}`) |
| **Character beliefs** | Other characters | "Mira is dangerous and untrustworthy" | Social interaction decisions, approach/avoid, trade willingness |
| **Location beliefs** | Places | "The swamp is cursed and deadly" | Travel cost, anxiety state, avoidance behavior |

**Composability**: Belief identity and propagation are owned here. Actual norm data comes from Faction. Actual character data comes from Character-Encounter (sentiment), Character-Personality (traits), and Relationship (structural bonds). Actual location data comes from Location. Hearsay does not replace any of these -- it provides the NPC's *perceived* version, which may lag behind, exaggerate, or completely misrepresent reality.

**The Obligation parallel**: Hearsay provides `${hearsay.*}` variables alongside `${obligations.*}`. Obligation gives the exact, personality-weighted cost of violating known norms. Hearsay gives the NPC's uncertain, belief-filtered perception of the social landscape. Behavior authors choose which to use: games that want perfect-information NPCs ignore hearsay; games that want realistic social knowledge use hearsay to modulate or replace obligation costs in ABML expressions. When lib-hearsay is disabled, NPCs fall back to perfect-information behavior -- existing systems work unchanged.

**The Persona parallel**: In Persona 5, rumors literally change reality. In Bannou, rumors change NPC *perception* of reality, which changes NPC behavior, which can change actual reality (self-fulfilling prophecy). A rumor that "the market is dangerous" causes NPCs to avoid it, which reduces traffic, which makes it actually more dangerous for those who remain. A god of mischief can destabilize a district by injecting false beliefs about draconian enforcement, causing merchants to flee before any enforcement actually happens.

**The Storyline bridge**: Hearsay naturally feeds the scenario generation system. Regional watchers can query hearsay to find characters operating on outdated or manipulated beliefs -- prime targets for scenarios. "This character still thinks the old king's laws apply" is a scenario hook. "This character heard a rumor about buried treasure in the mountains" is a quest seed. "This character believes their friend betrayed them based on a false rumor" is dramatic irony the storyline composer can exploit. Hearsay data enriches the `NarrativeState` extraction that drives storyline composition, adding the `belief_deltas` dimension (what the character believes vs. what is true) that formal narrative theory calls "dramatic irony."

**Zero Arcadia-specific content**: lib-hearsay is a generic social information propagation service. Arcadia's specific propagation speeds, confidence thresholds, convergence rates, and rumor injection patterns are configured through hearsay configuration and seed data at deployment time, not baked into lib-hearsay.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on the morality system analysis (lib-obligation + lib-faction pipeline), the story system architecture, and the identified gap in inter-character perception. Internal-only, never internet-facing.

## Inventory {#inventory}

**Version**: 1.0.0 | **Schema**: `schemas/inventory-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/INVENTORY.md](plugins/INVENTORY.md)

Container and item placement management (L2 GameFoundation) for games. Handles container lifecycle (CRUD), item movement between containers, stacking operations (split/merge), and inventory queries. Does NOT handle item definitions or instances directly -- delegates to lib-item for all item-level operations. Supports multiple constraint models (slot-only, weight-only, grid, volumetric, unlimited), category restrictions, and nesting depth limits. Designed as the placement layer that orchestrates lib-item.

## Item {#item}

**Version**: 1.0.0 | **Schema**: `schemas/item-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/ITEM.md](plugins/ITEM.md)

Dual-model item management (L2 GameFoundation) with templates (definitions/prototypes) and instances (individual occurrences). Templates define item properties (code, game scope, quantity model, stats, effects, rarity); instances represent actual items in the game world with quantity, durability, custom stats, and binding state. Supports multiple quantity models (discrete stacks, continuous weights, unique items). Designed to pair with lib-inventory for container placement management.

## Leaderboard {#leaderboard}

**Version**: 1.0.0 | **Schema**: `schemas/leaderboard-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/LEADERBOARD.md](plugins/LEADERBOARD.md)

Real-time leaderboard management (L4 GameFeatures) built on Redis Sorted Sets. Supports polymorphic entity types (Account, Character, Guild, Actor, Custom), multiple score update modes, seasonal rotation with archival, and automatic score ingestion from Analytics events. Definitions are scoped per game service with configurable sort order and entity type restrictions. Provides percentile calculations, neighbor queries, and batch score submission.

## Lexicon {#lexicon}

**Deep Dive**: [docs/plugins/LEXICON.md](plugins/LEXICON.md)

Structured world knowledge ontology (L4 GameFeatures) that defines what things ARE in terms of decomposed, queryable characteristics. Lexicon is the ground truth registry for entity concepts -- species, objects, phenomena, named individuals, abstract ideas -- broken into traits, hierarchical categories, bidirectional associations, and strategy-relevant implications. It answers "what is a wolf?" not with prose, but with structured data that GOAP planners, behavior expressions, and game systems can reason over: a wolf has fur, runs on four legs, hunts in packs, tends toward muted colors, has long teeth, and the ways to escape one are to kill it (shared with all animals), scare it with loud noises (shared with fish and mammals), or climb something (shared with quadruped mammals).

**The problem this solves**: NPCs currently know about themselves (personality, encounters, backstory, drives) but not about the world around them conceptually. A character can feel afraid (via Disposition) but has no structured reason to be afraid of wolves specifically versus rabbits. There is no mechanism for "wolves hunt in packs, so I should not be alone" or "I see a wolf, and the Witch of the Wolves summons wolves, so the witch may be nearby." Game entities exist as IDs in service databases, but their characteristics, category relationships, and strategic implications are nowhere in the system. An NPC encountering an unknown creature cannot reason about it based on observable traits -- it has no framework for "this thing has four legs and long teeth, therefore it is probably dangerous and I can escape by climbing."

**What Lexicon is NOT**:

| Service | What It Answers | How Lexicon Differs |
|---------|----------------|-------------------|
| **Collection** | "Have I discovered this thing?" | Lexicon stores WHAT there is to know; Collection tracks WHETHER you know it |
| **Documentation** | "What does this developer guide say?" | Lexicon stores structured game-world data, not prose documentation |
| **Hearsay** | "What do I believe about this thing?" | Lexicon is ground truth; Hearsay is subjective belief (which may be false) |
| **Species** | "What species exist?" | Species stores IDs and trait modifiers; Lexicon stores what a species IS in decomposed characteristics |
| **Character-Encounter** | "Who have I met?" | Encounters record interactions; Lexicon defines what the interacted-with thing fundamentally is |

**The three-service knowledge stack**: These three services together form the complete knowledge system:
- **Lexicon** = what things objectively ARE (ground truth, structured characteristics)
- **Collection** = what you have personally DISCOVERED (per-character progressive unlock tracking)
- **Hearsay** = what you BELIEVE things are (subjective, spreadable, potentially false)

An NPC's effective knowledge about wolves at runtime is: Lexicon's ground truth, gated by Collection's discovery level, overlaid by Hearsay's beliefs. A character who has never seen a wolf but heard rumors might have Hearsay beliefs ("wolves breathe fire") that contradict Lexicon ground truth. A character who has studied wolves extensively has high Collection discovery and accesses deep Lexicon data. The Actor's variable provider composites all three.

**Zero Arcadia-specific content**: lib-lexicon is a generic concept ontology service. Arcadia's specific entries (wolves, the Witch of the Wolves, pneuma, mana), trait vocabularies (four_legged, pack_hunter), category hierarchies (canine < quadruped_mammal < mammal < animal), and strategy implications are all configured through seed data at deployment time, not baked into lib-lexicon. The True Names metaphysical framework is Arcadia's flavor interpretation of lexicon entry codes.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on analysis of the NPC world-knowledge gap across the Actor cognition pipeline, the Collection discovery system, and the GOAP planner's need for trait-based reasoning. Internal-only, never internet-facing.

## License {#license}

**Version**: 1.0.0 | **Schema**: `schemas/license-api.yaml` | **Endpoints**: 20 | **Deep Dive**: [docs/plugins/LICENSE.md](plugins/LICENSE.md)

The License service (L4 GameFeatures) provides grid-based progression boards (skill trees, license boards, tech trees) inspired by Final Fantasy XII's License Board system. It is a thin orchestration layer that combines Inventory (containers for license items), Items (license nodes as item instances), and Contracts (unlock behavior via prebound API execution) to manage entity progression across a grid. Boards support polymorphic ownership via `ownerType` + `ownerId` — characters, accounts, guilds, and locations can all own boards. Internal-only, never internet-facing. See [GitHub Issue #281](https://github.com/beyond-immersion/bannou-service/issues/281) for the original design specification.

## Location {#location}

**Version**: 1.0.0 | **Schema**: `schemas/location-api.yaml` | **Endpoints**: 24 | **Deep Dive**: [docs/plugins/LOCATION.md](plugins/LOCATION.md)

Hierarchical location management (L2 GameFoundation) for the Arcadia game world. Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking. Each location belongs to exactly one realm and optionally has a parent location. Supports deprecation, circular reference prevention, cascading depth updates, code-based lookups, and bulk seeding with two-pass parent resolution.

## Loot {#loot}

**Deep Dive**: [docs/plugins/LOOT.md](plugins/LOOT.md)

Loot table management and generation service (L4 GameFeatures) for weighted drop determination, contextual modifier application, and group distribution orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver loot acquisition mechanics.

**Composability**: Loot table definitions and generation rules are owned here. Item creation is lib-item (L2) -- loot generates item instances with metadata. Item placement is lib-inventory (L2) -- loot places generated items into containers. Modifier generation on drops is lib-affix (L4, soft) -- loot requests affix sets for items that should be randomly modified. Currency drops are lib-currency (L2) -- loot credits wallets directly. NPC looting behavior is Actor (L2) via the Variable Provider Factory pattern -- NPCs perceive lootable sources and decide whether to loot based on GOAP planning. Divine intervention in loot outcomes is lib-divine (L4, soft) -- gods can manipulate drop probabilities through narrative events without lib-loot knowing.

**The foundational distinction**: lib-loot manages WHAT can drop from sources (table definitions, weighted entries, probability curves) and orchestrates the HOW of generation (rolling, context application, instantiation). WHO triggers loot generation is external -- combat systems report kills, quest systems grant rewards, world events produce spoils, chest interactions trigger rolls. lib-loot is a pure generation engine: give it a table ID and a context, and it returns concrete items.

**Critical architectural insight**: Loot is not a reward -- it is a *consequence*. In Arcadia's living world, loot emerges from the simulation, not from a designer's reward spreadsheet. When a pneuma echo is destroyed in a dungeon, the logos seed disperses and the physical manifestation fragments into recoverable materials -- that is loot. When a divine actor decides a character deserves recognition, the divine blessing might manifest as a rare drop from the next monster they kill -- that is loot. When a merchant NPC's caravan is raided by bandits, the scattered goods become lootable -- that is loot. lib-loot provides the probabilistic generation engine; the simulation provides the meaning.

**The dual-table model**: Loot tables come in two flavors that serve different purposes. **Static tables** are designer-authored definitions seeded at deployment time -- the baseline drop rates for monster species, chest tiers, quest reward pools. **Dynamic tables** are runtime-constructed by actors and systems -- a divine actor composing a custom reward table for a blessed character, a dungeon core adjusting its treasure rooms based on intruder capabilities, an NPC merchant deciding what to stock by "looting" a supplier's catalog. Both models use the same LootTable structure; the distinction is in who creates them and when.

**Three generation tiers**: Not all loot requires the same computational effort. **Tier 1 (Lightweight)** generates item template references only -- "this source can drop iron ore, leather scraps, or wolf fangs." No instances created; the caller decides when and how to instantiate. Used for previews, bestiary entries, tooltip generation, and NPC GOAP evaluation at scale. **Tier 2 (Standard)** generates concrete item instances placed into a target container -- the normal loot drop flow. **Tier 3 (Enriched)** generates item instances with full affix sets, custom metadata, and quality modifiers -- the "rare drop" flow that coordinates with lib-affix for modifier generation. The tier is per-entry in the table, not per-table -- a single table can have Tier 1 common drops and Tier 3 legendary drops.

**NPC interaction with loot**: At 100K concurrent NPCs, loot generation must be efficient. NPCs interact with loot in three ways: as **sources** (NPC death triggers loot generation from their species table), as **claimants** (NPC GOAP evaluates whether to loot a nearby source based on need, greed, and personality), and as **evaluators** (NPC GOAP uses Tier 1 preview to assess whether a source is worth the effort). The Variable Provider Factory exposes `${loot.*}` variables for all three roles.

**The pity system as divine mechanism**: lib-loot tracks per-entity failure counters for configurable "pity" entries -- items guaranteed to drop after N failed rolls. But the pity system is deliberately minimal at the loot layer. In Arcadia, extended bad luck is a *narrative opportunity*: a divine actor observing a character's mounting frustration can intervene by temporarily modifying the character's loot context (boosting weight modifiers through a blessing), making the next drop feel earned rather than mechanically guaranteed. lib-loot's pity counter is the fallback for games without divine actors; Arcadia's gods make it nearly redundant by turning bad luck into story.

**Zero game-specific content**: lib-loot is a generic loot generation service. Arcadia's pneuma-based drop metaphysics, PoE-style deterministic crafting drops, Diablo-style legendary showers, or a simple "kill monster, get gold" system are all equally valid configurations. Table structures, entry weights, context modifiers, distribution modes, and pity thresholds are all opaque configuration defined per game at deployment time through table seeding.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on a Path of Exile item system complexity benchmark, the [Item & Economy Plugin Landscape](../plans/ITEM-ECONOMY-PLUGINS.md), and the broader architectural patterns established by lib-divine, lib-dungeon, lib-affix, and lib-market. Internal-only, never internet-facing.

## Mapping {#mapping}

**Version**: 1.0.0 | **Schema**: `schemas/mapping-api.yaml` | **Endpoints**: 18 | **Deep Dive**: [docs/plugins/MAPPING.md](plugins/MAPPING.md)

Spatial data management service (L4 GameFeatures) for Arcadia game worlds. Provides authority-based channel ownership for exclusive write access to spatial regions, high-throughput ingest via dynamic RabbitMQ subscriptions, 3D spatial indexing with affordance queries, and design-time authoring workflows (checkout/commit/release). Purely a spatial data store -- does not perform rendering or physics. Game servers and NPC brains publish spatial data to and query from it.

## Market {#market}

**Deep Dive**: [docs/plugins/MARKET.md](plugins/MARKET.md)

Marketplace orchestration service (L4 GameFeatures) for auctions, NPC vendor management, and price discovery. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver game economy exchange mechanics.

**Composability**: Market identity (auction houses, vendor catalogs, market locations) is owned here. Item custody during auctions is Escrow. Financial operations (fees, bids, payments) are Currency. Item definitions and instances are Item. Item placement is Inventory. Bid reservation is Currency authorization holds. Auction settlement is a background worker coordinating Escrow release. NPC vendor behavior is Actor (via the Variable Provider Factory pattern). Price analytics feed NPC GOAP decisions and divine economic intervention. lib-market orchestrates the ceremony connecting these primitives.

**Critical architectural insight**: NPCs are economic actors, not UI facades. A vendor NPC's catalog, pricing, restock behavior, and buy/sell willingness emerge from the character's Actor brain running GOAP planning with `${market.*}` variables -- not from static configuration tables. lib-market provides the data infrastructure (catalogs, stock levels, price history) that the Actor runtime consumes through the Variable Provider Factory pattern. The NPC decides what to stock, how to price, and whether to haggle. lib-market records the outcomes.

**The divine economic connection**: Economic deities (Hermes/Commerce, Laverna/Thieves) monitor market health through analytics events published by lib-market (listings created, auctions sold, prices changed). When velocity stagnates or overheats, divine actors spawn narrative events that affect NPC economic behavior -- a traveling merchant appears, a trade festival is announced, a robbery disrupts hoarding. lib-market sees these as normal NPC transactions; the divine intervention is invisible at the market layer. This is the same indirect influence pattern used throughout the system: gods act through the world, not on it.

**Two market models, one service**: lib-market supports two fundamentally different exchange patterns. **Auction houses** are player/NPC-to-player/NPC exchanges mediated by escrow -- items listed, bids placed, settlement orchestrated. **Vendor catalogs** are NPC-managed storefronts with pricing and stock -- buy from vendor, sell to vendor, personality-driven behavior. Both models use the same underlying Currency/Item/Inventory/Escrow primitives but present different game-flavored APIs. A game can use either or both.

**Three pricing modes**: Vendor pricing supports three modes that cover the spectrum from simple to autonomous. **Static**: prices defined at catalog creation, never change. **Dynamic**: prices adjust based on configurable formulas (supply/demand signals, time of day, regional modifiers). **Personality-driven**: the NPC vendor's Actor brain sets prices via GOAP economic decisions, consulting market data and personality traits. The mode is per-vendor, not per-market -- a bustling city might have formula-driven shops alongside GOAP-driven haggling merchants.

**Fee structures as deliberate sinks**: Every auction listing incurs a non-refundable listing fee (deducted on creation). Successful sales incur a transaction fee (percentage of final price). Both fees are currency sinks -- removed from circulation entirely, not transferred to a fee recipient. This is a deliberate inflation control mechanism. Games can configure fee rates per market definition, including zero for fee-free markets.

**Zero game-specific content**: lib-market is a generic marketplace service. Arcadia's auction house rules, vendor personality templates, and fee structures are configured through market definitions, ABML behaviors, and seed type definitions at deployment time, not baked into lib-market. A cyberpunk game's black market, a medieval fantasy's guild trading post, and a space sim's orbital station exchange all use the same lib-market primitives differently.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on [ECONOMY-CURRENCY-ARCHITECTURE.md](../planning/ECONOMY-CURRENCY-ARCHITECTURE.md) and the patterns established by lib-divine, lib-dungeon, lib-escrow, and the broader economy vision. Internal-only, never internet-facing.

## Matchmaking {#matchmaking}

**Version**: 1.0.0 | **Schema**: `schemas/matchmaking-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/MATCHMAKING.md](plugins/MATCHMAKING.md)

Ticket-based matchmaking (L4 GameFeatures) with skill windows, query matching, party support, and configurable accept/decline flow. A background service processes queues at configurable intervals, expanding skill windows over time until matches form or tickets timeout. On full acceptance, creates a matchmade game session via lib-game-session with reservation tokens and publishes join shortcuts via Connect. Supports immediate match checks on ticket creation, auto-requeue on decline, and pending match state restoration on reconnection.

## Mesh {#mesh}

**Version**: 1.0.0 | **Schema**: `schemas/mesh-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/MESH.md](plugins/MESH.md)

Native service mesh (L0 Infrastructure) providing direct in-process service-to-service calls with YARP-based HTTP routing and Redis-backed service discovery. Provides endpoint registration with TTL-based health tracking, configurable load balancing, a distributed per-appId circuit breaker, and retry logic with exponential backoff. Includes proactive health checking with automatic deregistration and event-driven auto-registration from Orchestrator heartbeats for zero-configuration discovery.

## Messaging {#messaging}

**Version**: 1.0.0 | **Schema**: `schemas/messaging-api.yaml` | **Endpoints**: 4 | **Deep Dive**: [docs/plugins/MESSAGING.md](plugins/MESSAGING.md)

The Messaging service (L0 Infrastructure) is the native RabbitMQ pub/sub infrastructure for Bannou. Operates in a dual role: as the `IMessageBus`/`IMessageSubscriber` infrastructure library used by all services for event publishing and subscription, and as an HTTP API providing dynamic subscription management with HTTP callback delivery. Supports in-memory mode for testing, direct RabbitMQ with channel pooling, and aggressive retry buffering with crash-fast philosophy for unrecoverable failures.

## Music {#music}

**Version**: 1.0.0 | **Schema**: `schemas/music-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/MUSIC.md](plugins/MUSIC.md)

Pure computation music generation (L4 GameFeatures) using formal music theory rules and narrative-driven composition. Leverages two internal SDKs: `MusicTheory` (harmony, melody, pitch, MIDI-JSON output) and `MusicStoryteller` (narrative templates, emotional state planning). Generates complete compositions, chord progressions, melodies, and voice-led voicings. Deterministic when seeded, enabling Redis caching for repeat requests. No external service dependencies -- fully self-contained computation.

## Obligation {#obligation}

**Version**: 1.0.0 | **Schema**: `schemas/obligation-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/OBLIGATION.md](plugins/OBLIGATION.md)

Contract-aware obligation tracking for NPC cognition (L4 GameFeatures), bridging the Contract service's behavioral clauses and the GOAP planner's action cost system to enable NPCs to have "second thoughts" before violating obligations. Provides dynamically-updated action cost modifiers based on active contracts (guild charters, trade agreements, quest oaths), working standalone with raw contract penalties or enriched with personality-weighted moral reasoning when character-personality data is available. Implements `IVariableProviderFactory` providing the `${obligations.*}` namespace to Actor (L2) via the Variable Provider Factory pattern. See [GitHub Issue #410](https://github.com/beyond-immersion/bannou-service/issues/410) for the original design specification ("Second Thoughts" feature).

## Orchestrator {#orchestrator}

**Version**: 3.0.0 | **Schema**: `schemas/orchestrator-api.yaml` | **Endpoints**: 22 | **Deep Dive**: [docs/plugins/ORCHESTRATOR.md](plugins/ORCHESTRATOR.md)

Central intelligence (L3 AppFeatures) for Bannou environment management and service orchestration. Manages distributed service deployments including preset-based topologies, live topology updates, processing pools for on-demand worker containers (used by lib-actor for NPC brains), service health monitoring via heartbeats, versioned deployment configurations with rollback, and service-to-app-id routing broadcasts consumed by lib-mesh. Features a pluggable backend architecture supporting Docker Compose, Docker Swarm, Portainer, and Kubernetes. Operates in a secure mode making it inaccessible via WebSocket (admin-only service-to-service calls).

## Organization {#organization}

**Deep Dive**: [docs/plugins/ORGANIZATION.md](plugins/ORGANIZATION.md)

Legal entity management service (L4 GameFeatures) for organizations that own assets, employ characters, enter contracts, and participate in the economy as first-class entities. A structural layer that gives economic and social entities a legal identity -- shops, guilds, households, trading companies, temples, military units, criminal enterprises, and any other group that acts as a collective within the game world.

**Composability**: Organization identity and structure are owned here. Treasury is Currency (organizations own wallets). Inventory is Inventory/Item (organizations own containers and goods). Contracts are Contract (organizations are contract parties). Employment and membership are Relationship (member-to-organization bonds). Physical presence is Location (organizations control or occupy locations). Governance capabilities are Seed (organizational growth determines what the organization can do). Legal status comes from Faction (the sovereign determines whether an organization is chartered, licensed, tolerated, or outlawed). Internal roles and succession are organization-specific concerns owned by this service.

**The living economy substrate**: From the [Vision](../../arcadia-kb/VISION.md): "The economy must be NPC-driven, not player-driven. Supply, demand, pricing, and trade routes emerge from NPC behavior -- what they need, what they produce, what they want." Without lib-organization, "NPC runs a shop" is a behavior pattern with no structural backing. With lib-organization, the shop is a legal entity with inventory, a currency wallet, employees, trade agreements, and a succession plan -- and when the shopkeeper dies, succession rules determine what happens to it. Organizations are the structural skeleton that NPC economic behavior hangs on.

**Family-as-organization**: A household is an organization. It has shared assets (family home, savings, heirlooms), internal roles (head of household, heir, dependents, elders), succession rules (primogeniture, equal division, matrilineal, elective), and legal status within the sovereign's framework (recognized family, noble house, outlawed clan). The [Dungeon deep dive](DUNGEON.md)'s "household split" mechanic and the [Dissolution design](../planning/DISSOLUTION-DESIGN.md)'s divorce/exile patterns are all organization dissolution -- breaking apart a legal entity's structure, dividing its assets, and managing the aftermath.

**The Quest/Escrow/Arbitration parallel**: Organization follows the same pattern as other L4 orchestration layers -- it composes L0/L1/L2 primitives into a higher-level game concept. Quest composes Contract into objectives. Escrow composes Contract + Currency + Item into exchanges. Arbitration composes Contract + Faction into dispute resolution. Organization composes Currency + Inventory + Contract + Relationship + Location into legal entities.

**Critical architectural insight**: Organizations do not replace characters as economic actors. Characters own and operate organizations. An NPC blacksmith owns a "blacksmith shop" organization. The NPC's Actor brain makes economic decisions (what to buy, what to sell, what to craft). The organization provides the structural container for those decisions -- the wallet from which purchases are made, the inventory in which goods are stored, the contracts under which trades are executed. The NPC's GOAP planner considers organizational assets when evaluating economic actions.

**Legal status from sovereign**: The sovereign authority (via lib-faction's `authorityLevel`) determines an organization's legal standing. A Chartered organization has legal protections. An Outlawed organization operates illegally, and conducting business with it carries obligation costs. Legal status feeds into the organization's own seed growth: legitimate commerce grows a Chartered organization faster; underground economy grows an Outlawed one differently. The chartering mechanism is itself a Contract -- the sovereign grants a charter with behavioral clauses (tax compliance, regulatory adherence), and breach triggers status downgrade.

**Seed-based organizational growth**: Each organization owns a seed that grows through member activities and economic transactions, following the same Collection-to-Seed pipeline that powers faction growth. As the organization's seed grows, capabilities unlock: hiring more employees, opening branches, entering complex contracts, participating in trade regulation. A nascent street vendor literally cannot hire employees -- it hasn't grown enough organizational capability yet.

**Organization type codes are opaque strings**: `household`, `shop`, `guild`, `trading_company`, `temple`, `military_unit`, `criminal_enterprise`, `noble_house` are all just organization types. lib-organization doesn't hardcode any type-specific logic -- different types have different seed type definitions (growth phases, capability rules) and different governance treatment from sovereigns (chartering requirements, tax rates). New organization types require only a seed type registration and governance data entries.

**Zero Arcadia-specific content**: lib-organization is a generic organizational entity service. Arcadia's specific organization types (clans, guilds, noble houses), their governance relationships with factions, and their economic roles are configured through seed types, contract templates, and faction governance data at deployment time, not baked into lib-organization.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on [DISSOLUTION-DESIGN.md](../planning/DISSOLUTION-DESIGN.md) and the broader architectural patterns established by lib-faction, lib-divine, and lib-dungeon. Internal-only, never internet-facing.

## Permission {#permission}

**Version**: 3.0.0 | **Schema**: `schemas/permission-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/PERMISSION.md](plugins/PERMISSION.md)

Redis-backed RBAC permission system (L1 AppFoundation) for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via the Connect service's per-session RabbitMQ queues.

## Procedural {#procedural}

**Deep Dive**: [docs/plugins/PROCEDURAL.md](plugins/PROCEDURAL.md)

On-demand procedural 3D asset generation service (L4 GameFeatures) using headless Houdini Digital Assets (HDAs) as parametric generation templates. A thin orchestration layer that composes existing Bannou primitives (Asset service for HDA storage and output bundling, Orchestrator for Houdini worker pool management) to deliver procedural geometry generation as an API.

**Composability**: Template storage and output bundling are Asset (L3). Worker pool lifecycle is Orchestrator (L3). Job status tracking is internal. Generation execution is delegated to headless Houdini containers running hwebserver. lib-procedural orchestrates the pipeline: receive request, fetch HDA from Asset, acquire worker from Orchestrator, execute generation, upload result to Asset, optionally bundle, return reference.

**Critical architectural insight**: lib-procedural is a **bridge between content authoring and runtime generation**. Artists author HDAs (parametric procedural tools with exposed controls) in Houdini's GUI. Those HDAs are uploaded to the Asset service as templates. At runtime, any service (dungeon cores exercising domain_expansion, regional watchers sculpting terrain, NPC builders constructing buildings, world seeding during realm creation) can request generation by providing a template ID, parameters, and a seed. The same HDA with different parameters produces dramatically different geometry -- infinite variations from a single authored template.

**Deterministic generation**: Same template + same parameters + same seed = identical output. This enables Redis-cached generation results (keyed by hash of template_id + parameters + seed), reproducible dungeon layouts, and predictable world seeding. The cache key is canonical -- if the same generation has been requested before, the cached result is returned without invoking Houdini.

**Zero game-specific content**: lib-procedural is a generic procedural generation service. Arcadia's dungeon chambers, terrain chunks, building facades, and vegetation are authored as HDAs by artists at content-creation time, not baked into lib-procedural. The service knows nothing about what it generates -- it executes HDAs and returns geometry.

**Current status**: Pre-implementation. No schema, no code. The feasibility study ([HOUDINI-PROCEDURAL-GENERATION.md](../planning/HOUDINI-PROCEDURAL-GENERATION.md)) is complete, confirming Houdini provides built-in HTTP server (hwebserver), containerized deployment, deterministic execution, and free licensing for headless indie use.

## Puppetmaster {#puppetmaster}

**Version**: 1.0.0 | **Schema**: `schemas/puppetmaster-api.yaml` | **Endpoints**: 6 | **Deep Dive**: [docs/plugins/PUPPETMASTER.md](plugins/PUPPETMASTER.md)

The Puppetmaster service (L4 GameFeatures) orchestrates dynamic behaviors, regional watchers, and encounter coordination for the Arcadia game system. Provides the bridge between the behavior execution runtime (lib-actor at L2) and the asset service (lib-asset at L3), enabling dynamic ABML behavior loading that would otherwise violate the service hierarchy. Implements `IBehaviorDocumentProvider` to supply runtime-loaded behaviors to actors via the provider chain pattern. Also manages regional watcher lifecycle and resource snapshot caching for Event Brain actors. Divine actors launched as regional watchers via Puppetmaster also serve as gardener behavior actors for player experience orchestration -- see [DIVINE.md](DIVINE.md) for the architectural rationale unifying realm-tending and garden-tending under a single divine actor identity.

## Quest {#quest}

**Version**: 1.0.0 | **Schema**: `schemas/quest-api.yaml` | **Endpoints**: 17 | **Deep Dive**: [docs/plugins/QUEST.md](plugins/QUEST.md)

The Quest service (L2 GameFoundation) provides objective-based gameplay progression as a thin orchestration layer over lib-contract. Translates game-flavored quest semantics (objectives, rewards, quest givers) into Contract infrastructure (milestones, prebound APIs, parties), leveraging Contract's state machine and cleanup orchestration while presenting a player-friendly API. Agnostic to prerequisite sources: L4 services (skills, magic, achievements) implement `IPrerequisiteProviderFactory` for validation without Quest depending on them. Exposes quest data to the Actor service via the Variable Provider Factory pattern for ABML behavior expressions.

## Realm {#realm}

**Version**: 1.0.0 | **Schema**: `schemas/realm-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/REALM.md](plugins/REALM.md)

The Realm service (L2 GameFoundation) manages top-level persistent worlds in the Arcadia game system. Realms are peer worlds (e.g., Omega, Arcadia, Fantasia) with no hierarchical relationships between them. Each realm operates as an independent world with distinct species populations and cultural contexts. Provides CRUD with deprecation lifecycle and seed-from-configuration support. Internal-only.

## Realm History {#realm-history}

**Version**: 1.0.0 | **Schema**: `schemas/realm-history-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/REALM-HISTORY.md](plugins/REALM-HISTORY.md)

Historical event participation and lore management (L4 GameFeatures) for realms. Tracks when realms participate in world events (wars, treaties, cataclysms) with role and impact tracking, and maintains machine-readable lore elements (origin myths, cultural practices, political systems) for behavior system consumption. Provides text summarization for realm archival via lib-resource. Shares storage helper abstractions with the character-history service.

## Relationship {#relationship}

**Version**: 2.0.0 | **Schema**: `schemas/relationship-api.yaml` | **Endpoints**: 21 | **Deep Dive**: [docs/plugins/RELATIONSHIP.md](plugins/RELATIONSHIP.md)

A unified relationship management service (L2 GameFoundation) combining entity-to-entity relationships (character friendships, alliances, rivalries) with hierarchical relationship type taxonomy definitions. Supports bidirectional uniqueness enforcement, polymorphic entity types, soft-deletion with recreate capability, type deprecation with merge, and bulk seeding. Used by the Character service for inter-character bonds and family tree categorization, and by the Storyline service for narrative generation. Consolidated from the former separate relationship and relationship-type plugins.

## Resource {#resource}

**Version**: 1.0.0 | **Schema**: `schemas/resource-api.yaml` | **Endpoints**: 17 | **Deep Dive**: [docs/plugins/RESOURCE.md](plugins/RESOURCE.md)

Resource reference tracking, lifecycle management, and hierarchical compression service (L1 AppFoundation) for foundational resources. Enables safe deletion of L2 resources by tracking references from higher-layer consumers (L3/L4) without hierarchy violations, coordinates cleanup callbacks with CASCADE/RESTRICT/DETACH policies, and centralizes compression of resources and their dependents into unified MySQL-backed archives. Placed at L1 so all layers can use it; uses opaque string identifiers for resource/source types to avoid coupling to higher layers. Currently integrated by lib-character (L2) for deletion checks, and by lib-actor, lib-character-encounter, lib-character-history, and lib-character-personality (L4) as reference publishers.

## Save Load {#save-load}

**Version**: 1.0.0 | **Schema**: `schemas/save-load-api.yaml` | **Endpoints**: 26 | **Deep Dive**: [docs/plugins/SAVE-LOAD.md](plugins/SAVE-LOAD.md)

Generic save/load system (L4 GameFeatures) for game state persistence with polymorphic ownership (accounts, characters, sessions, realms). Manages save slots, versioned writes with automatic compression, delta/incremental saves via JSON Patch (RFC 6902), schema migration with forward migration paths, and rolling cleanup by save category. Uses a two-tier storage architecture: Redis hot cache for immediate acknowledgment, with async upload to MinIO via the Asset service for durable storage. Supports export/import via ZIP archives and multi-device cloud sync with conflict detection.

## Scene {#scene}

**Version**: 1.0.0 | **Schema**: `schemas/scene-api.yaml` | **Endpoints**: 19 | **Deep Dive**: [docs/plugins/SCENE.md](plugins/SCENE.md)

Hierarchical composition storage (L4 GameFeatures) for game worlds. Stores scene documents as node trees with support for multiple node types (group, mesh, marker, volume, emitter, reference, custom), scene-to-scene references with recursive resolution, an exclusive checkout/commit/discard workflow, game-specific validation rules, full-text search, and version history. Does not compute world transforms or interpret node behavior at runtime -- consumers decide what nodes mean.

## Seed {#seed}

**Version**: 1.0.0 | **Schema**: `schemas/seed-api.yaml` | **Endpoints**: 24 | **Deep Dive**: [docs/plugins/SEED.md](plugins/SEED.md)

Generic progressive growth primitive (L2 GameFoundation) for game entities. Seeds start empty and grow by accumulating metadata across named domains, progressively gaining capabilities at configurable thresholds. Seeds are polymorphically owned (accounts, actors, realms, characters, relationships) and agnostic to what they represent -- guardian spirits, dungeon cores, combat archetypes, crafting specializations, and governance roles are all equally valid seed types. Seed types are string codes (not enums), allowing new types without schema changes. Each seed type defines its own growth phase labels, capability computation rules, and bond semantics. Consumers register seed types via API, contribute growth via the record API or DI provider listeners (e.g., Collection→Seed pipeline), and query capability manifests to gate actions.

## Showtime {#showtime}

**Deep Dive**: [docs/plugins/SHOWTIME.md](plugins/SHOWTIME.md)

In-game streaming metagame service (L4 GameFeatures) for simulated audience pools, hype train mechanics, streamer career progression, and real-simulated audience blending. The game-facing layer of the streaming stack -- everything that makes streaming a game mechanic rather than just a platform integration.

**Composability**: Stream session identity and audience simulation are owned here. Platform integration is lib-broadcast (L3). Voice rooms are lib-voice (L3). Streamer career growth is Seed (`streamer` seed type). Streaming milestone unlocks are Collection. Virtual tips are Currency (`stream_tip` currency type). Sponsorship deals are Contract. Streamer-follower bonds are Relationship. lib-showtime orchestrates the metagame connecting these primitives.

**The divine actor parallel**: The streaming metagame follows the same structural pattern as lib-divine -- an L4 orchestration layer that composes existing Bannou primitives (Seed, Currency, Collection, Contract, Relationship) to deliver game mechanics. Where lib-divine orchestrates blessings and divinity economy, lib-showtime orchestrates audience dynamics and streamer career. They are parallel orchestration layers composing the same underlying primitives, not the same service. This mirrors how Quest and Escrow both compose Contract but provide different game-flavored APIs.

**Simulated audiences are always available**: lib-showtime works without lib-broadcast (L3) entirely. When no real platform data is available, the service operates on 100% simulated audiences. When lib-broadcast is available, real audience sentiment pulses are blended seamlessly into the simulated pool. This makes the metagame testable, deployable, and playable without any external platform dependency.

**The natural Turing test**: Simulated audience members behave predictably within their personality parameters. Real-derived audience members inherit the genuine unpredictability of human behavior -- unexpected excitement, inexplicable departures, returning after long absences. The game NEVER reveals which audience members are real. Keen players may develop theories, and that speculation IS the metagame.

**Realm-specific manifestation**: In Omega (cyberpunk meta-dashboard), streaming is explicit -- players see audience stats, manage their stream, and compete with other streamers. In Arcadia, the same mechanics manifest as "performing for a crowd" -- a bard performing at a tavern, a gladiator entertaining an arena, a craftsman demonstrating mastery. The underlying system is identical; the UX presentation varies by realm. lib-showtime provides the mechanics; the client renders realm-appropriate UX.

**Zero Arcadia-specific content**: lib-showtime is a generic audience simulation and streamer career service. Which audience personality types exist, how hype trains escalate, and what streaming milestones unlock are all configured through seed types, collection types, and configuration, not baked into lib-showtime.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md). Internal-only, never internet-facing.

## Species {#species}

**Version**: 2.0.0 | **Schema**: `schemas/species-api.yaml` | **Endpoints**: 13 | **Deep Dive**: [docs/plugins/SPECIES.md](plugins/SPECIES.md)

Realm-scoped species management (L2 GameFoundation) for the Arcadia game world. Manages playable and NPC races with trait modifiers, realm-specific availability, and a full deprecation lifecycle (deprecate, merge, delete). Species are globally defined but assigned to specific realms, enabling different worlds to offer different playable options. Supports bulk seeding from configuration and cross-service character reference checking to prevent orphaned data.

## State {#state}

**Version**: 1.0.0 | **Schema**: `schemas/state-api.yaml` | **Endpoints**: 9 | **Deep Dive**: [docs/plugins/STATE.md](plugins/STATE.md)

The State service (L0 Infrastructure) provides all Bannou services with unified access to Redis and MySQL backends through a repository-pattern API. Operates in a dual role: as the `IStateStoreFactory` infrastructure library used by every service for state persistence, and as an HTTP API for debugging and administration. Supports three backends (Redis for ephemeral/session data, MySQL for durable/queryable data, InMemory for testing) with optimistic concurrency via ETags, TTL support, and specialized interfaces for cache operations, LINQ queries, JSON path queries, and full-text search. See the Interface Hierarchy section for the full interface tree and backend support matrix.

## Status {#status}

**Version**: 1.0.0 | **Schema**: `schemas/status-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/STATUS.md](plugins/STATUS.md)

Unified entity effects query layer (L4 GameFeatures) aggregating temporary contract-managed statuses and passive seed-derived capabilities into a single query point. Any system needing "what effects does this entity have" -- combat buffs, death penalties, divine blessings, subscription benefits -- queries lib-status. Follows the "items in inventories" pattern: status templates define effect definitions, status containers hold per-entity inventory containers, and granting a status creates an item instance in that container. Contract integration is optional per-template for complex lifecycle; simple TTL-based statuses use lib-item's native decay system. Internal-only, never internet-facing.

## Storyline {#storyline}

**Version**: 1.0.0 | **Schema**: `schemas/storyline-api.yaml` | **Endpoints**: 15 | **Deep Dive**: [docs/plugins/STORYLINE.md](plugins/STORYLINE.md)

The Storyline service (L4 GameFeatures) wraps the `storyline-theory` and `storyline-storyteller` SDKs to provide HTTP endpoints for seeded narrative generation from compressed archives. Plans describe narrative arcs with phases, actions, and entity requirements -- callers (gods/regional watchers) decide whether to instantiate them. Internal-only, requires the `developer` role for all endpoints.

## Subscription {#subscription}

**Version**: 1.0.0 | **Schema**: `schemas/subscription-api.yaml` | **Endpoints**: 7 | **Deep Dive**: [docs/plugins/SUBSCRIPTION.md](plugins/SUBSCRIPTION.md)

The Subscription service (L2 GameFoundation) manages user subscriptions to game services, controlling which accounts have access to which games/applications with time-limited access. Publishes `subscription.updated` events consumed by GameSession for real-time shortcut publishing. Includes a background expiration worker that periodically deactivates expired subscriptions. Internal-only, serves as the canonical source for subscription state.

## Telemetry {#telemetry}

**Version**: 1.0.0 | **Schema**: `schemas/telemetry-api.yaml` | **Endpoints**: 2 | **Deep Dive**: [docs/plugins/TELEMETRY.md](plugins/TELEMETRY.md)

The Telemetry service (L0 Infrastructure, optional) provides unified observability infrastructure for Bannou using OpenTelemetry standards. Operates in a dual role: as the `ITelemetryProvider` interface that lib-state, lib-messaging, and lib-mesh use for instrumentation, and as an HTTP API providing health and status endpoints. Unique among Bannou services: uses no state stores and publishes no events. When disabled, other L0 services receive a `NullTelemetryProvider` (all methods are no-ops).

## Trade {#trade}

**Deep Dive**: [docs/plugins/TRADE.md](plugins/TRADE.md)

The Trade service (L4 GameFeatures) is the economic logistics and supply orchestration layer for Bannou. It provides the mechanisms for moving goods across distances over game-time, enforcing border policies, calculating supply/demand dynamics, and enabling NPC economic decision-making. Trade is to the economy what Puppetmaster is to NPC behavior -- an orchestration layer that composes lower-level primitives (Transit for movement, Currency for payments, Item/Inventory for cargo, Escrow for custody) into higher-level economic flows.

Where Market handles exchange **at** a location (auctions, vendor catalogs, price discovery), Trade handles the logistics of moving goods **between** locations. Where Transit handles the raw mechanics of movement (connections, modes, journeys), Trade layers economic meaning onto that movement (cargo value, tariff liability, profit margins, supply chains). Distance creates value: iron costs 10g at the mine and 25g in the capital because someone paid the transit cost, bore the risk, and waited the travel time.

Trade absorbs the "lib-economy" monitoring concept from the Economy Architecture planning document. Velocity tracking, NPC economic profiles, and supply/demand signals live here because they are inseparable from logistics -- you cannot monitor economic health without understanding how goods flow through the geography. Internal-only, never internet-facing.

**What Trade IS**: Trade routes, shipments, tariffs, taxation, supply/demand dynamics, NPC economic intelligence, velocity monitoring.

**What Trade is NOT**: An auction house (that's Market), a crafting system (that's Craft), a production automator (that's Workshop), a currency ledger (that's Currency), a movement calculator (that's Transit). Trade orchestrates across all of these.

## Transit {#transit}

**Deep Dive**: [docs/plugins/TRANSIT.md](plugins/TRANSIT.md)

The Transit service (L2 GameFoundation) is the geographic connectivity and movement primitive for Bannou. It completes the spatial model by adding **edges** (connections between locations) to Location's **nodes** (the hierarchical place tree), then provides a type registry for **how** things move (transit modes) and temporal tracking for **when** they arrive (journeys computed against Worldstate's game clock). Transit is to movement what Seed is to growth and Collection is to unlocks -- a generic, reusable primitive that higher-layer services orchestrate for domain-specific purposes. Internal-only, never internet-facing.

**What Transit IS**: A movement capability registry, a connectivity graph, and a travel time calculator.

**What Transit is NOT**: A trade system, a cargo manager, a pathfinding engine, or a combat movement controller. It doesn't know about goods, tariffs, supply chains, or real-time spatial positioning. Those concerns belong to Trade (L4), Inventory (L2), and Mapping (L4) respectively.

## Utility {#utility}

**Deep Dive**: [docs/plugins/UTILITY.md](plugins/UTILITY.md)

The Utility service (L4 GameFeatures) manages infrastructure networks that continuously distribute resources across location hierarchies. It provides the topology, capacity modeling, and flow calculation that transforms Workshop point-production into location-wide service coverage -- answering "does this location have water, and where does it come from?" Where Workshop produces resources at a single point and Trade moves discrete shipments between locations, Utility models **continuous flow through persistent infrastructure** (aqueducts, sewer systems, power grids, magical conduits, messenger networks). The key gameplay consequence: when infrastructure breaks, downstream locations lose service, and the cascade of discovery, investigation, and repair creates emergent content.

**What Utility IS**: Infrastructure network topology, continuous flow calculation, service coverage per location, capacity constraints, failure cascading, maintenance lifecycle.

**What Utility is NOT**: A production system (that's Workshop), a goods transport system (that's Trade), a movement system (that's Transit), a regulatory authority (that's Faction), an infrastructure operator (that's Organization). Utility provides the network graph and flow mechanics that these services compose around.

Internal-only, never internet-facing.

## Voice {#voice}

**Version**: 2.0.0 | **Schema**: `schemas/voice-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/VOICE.md](plugins/VOICE.md)

Voice room coordination service (L3 AppFeatures) providing pure voice rooms as a platform primitive: P2P mesh topology for small groups, Kamailio/RTPEngine-based SFU for larger rooms, automatic tier upgrade, WebRTC SDP signaling, broadcast consent flows for streaming integration, and participant TTL enforcement via background worker. Agnostic to games, sessions, and subscriptions -- voice rooms are generic containers identified by Connect/Auth session IDs. Part of a planned three-service stack (voice, broadcast, showtime) where each delivers value independently; voice provides audio infrastructure while higher layers decide when and why to use it. Moved from L4 to L3 to eliminate a hierarchy violation where GameSession (L2) previously depended on Voice (L4) for room lifecycle.

## Website {#website}

**Version**: 1.0.0 | **Schema**: `schemas/website-api.yaml` | **Endpoints**: 14 | **Deep Dive**: [docs/plugins/WEBSITE.md](plugins/WEBSITE.md)

Public-facing website service (L3 AppFeatures) for browser-based access to news, account profiles, game downloads, CMS pages, and contact forms. Intentionally does NOT access game data (characters, subscriptions, realms) to respect the service hierarchy. Uses traditional REST HTTP methods (GET, PUT, DELETE) with path parameters for browser compatibility, which is an explicit exception to Bannou's POST-only pattern. **Currently a complete stub** -- every endpoint returns `NotImplemented`. When implemented, will require lib-account integration and state stores for CMS data.

## Workshop {#workshop}

**Deep Dive**: [docs/plugins/WORKSHOP.md](plugins/WORKSHOP.md)

Time-based automated production service (L4 GameFeatures) that transforms inputs from source inventories into outputs placed in destination inventories over game time. Production tasks run continuously in the background, producing items at rates determined by assigned workers. Supports variable worker counts that dynamically adjust production rate, with piecewise rate history enabling accurate lazy evaluation across rate changes. Blueprints define input/output transformations and can reference lib-craft recipes or specify custom transformations directly. A background materialization worker processes pending production per-entity with fair scheduling, preventing heavy users from affecting others' throughput. Covers crafting automation, farming, mining, resource extraction, manufacturing, training, and any time-based production loop. Internal-only, never internet-facing.

**The problem this solves**: Bannou has interactive crafting (lib-craft's Contract-backed step-by-step sessions), but no mechanism for continuous automated production. An NPC blacksmith who forges swords all day shouldn't need a GOAP action for every single sword -- they should have a running production line that produces swords at their skill level, consuming iron and leather from a supply chest and filling a shop inventory. A player who sets up a mining operation shouldn't need to click each ore extraction -- they assign workers to the mine and collect output periodically. An idle game's "auto-factory" shouldn't need per-item interaction. Workshop provides the "set it, check it later" production paradigm.

**The autogain pattern, generalized**: Currency's autogain worker is the direct architectural precedent. It runs on a timer, computes elapsed periods, and applies passive generation. Workshop applies the same lazy-evaluation-with-materialization pattern to item production instead of currency generation. The key differences: Workshop supports variable rates (workers join/leave), operates in game-time (via lib-worldstate instead of real-time), and handles material consumption/inventory capacity constraints.

**Why not actors**: Actors have cognitive overhead: perception queues, ABML bytecode execution, GOAP planning, variable provider resolution, behavior documents. An automation task is deterministic: "produce X items at rate Y consuming materials Z." There's no perception, no decision-making, no emergent behavior. The 100ms actor tick is orders of magnitude too frequent for a task that produces one item every few game-minutes. Using actors for automation would dilute cognitive processing resources meant for NPC brains. Workshop uses a background worker with fair per-entity scheduling -- simpler, cheaper, equally isolated.

**Why not extend lib-craft**: lib-craft is an interactive crafting engine with step-by-step progression, quality skill checks, Contract-backed sessions, and proficiency tracking. Workshop is a passive production engine with continuous output, lazy evaluation, and worker-based rate scaling. They compose well (Workshop can reference Craft recipes for input/output definitions) but serve fundamentally different interaction patterns. Craft answers "the player/NPC is actively crafting this item right now." Workshop answers "this production line has been running for 3 game-days, how much was produced?" Combining them would burden the interactive crafting flow with rate-segment history, worker management, and lazy evaluation complexity, while burdening the automation flow with step-by-step progression, quality formulas, and Contract overhead.

**Two production paradigms**:

| Paradigm | Input Source | Output Source | Example |
|----------|-------------|---------------|---------|
| **Recipe-referenced** | Derived from lib-craft recipe `inputs` | Derived from lib-craft recipe `outputs` | "Run the 'forge_iron_sword' recipe continuously" |
| **Custom** | Defined directly on the blueprint | Defined directly on the blueprint | "Mine: consumes nothing (time only), produces iron_ore" |

**Composability**: Blueprints are owned here. Item creation and destruction are lib-item (L2). Container operations are lib-inventory (L2). Game time is lib-worldstate (L2). Recipe definitions, when referenced, are lib-craft (L4, soft). Worker proficiency, when relevant, is lib-seed (L2) or lib-craft (L4, soft). Workshop orchestrates these into a continuous production loop.

**Zero game-specific content**: lib-workshop is a generic automated production engine. Arcadia's NPC blacksmith forges, player mining operations, and faction lumber mills are configured through blueprint seeding and task creation at deployment time, not baked into lib-workshop. An idle game's auto-factory, a farming sim's crop field, and a strategy game's resource harvester are all equally valid blueprint configurations.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on analysis of the crafting automation gap in the [Craft deep dive](CRAFT.md) (design considerations #1 "Crafting queues" and #7 "Offline NPC crafting"), the Currency autogain worker pattern, and the temporal gap now addressed by [lib-worldstate](WORLDSTATE.md). Internal-only, never internet-facing.

## Worldstate {#worldstate}

**Deep Dive**: [docs/plugins/WORLDSTATE.md](plugins/WORLDSTATE.md)

Per-realm game time authority, calendar system, and temporal event broadcasting service (L2 GameFoundation). Maps real-world time to configurable game-time progression with per-realm time ratios, calendar templates (configurable days, months, seasons, years as opaque strings), and day-period cycles (dawn, morning, afternoon, evening, night). Publishes boundary events at game-time transitions (new day, season, year, period) consumed by other services for time-aligned processing. Provides the `${world.*}` variable namespace to the Actor service's behavior system via the Variable Provider Factory pattern, enabling NPCs to make time-aware decisions. Also provides a time-elapsed query API for lazy evaluation patterns (computing game-time duration between two real timestamps accounting for ratio changes and pauses). Internal-only, never internet-facing.

**The problem this solves**: The codebase is haunted by a clock that doesn't exist. ABML behaviors reference `${world.time.period}` with no provider. Storyline declares `TimeOfDay` trigger conditions with nothing to evaluate them. Encounters and relationships have `inGameTime` fields that callers provide with no authority to consult. Trade routes have `seasonalAvailability` flags with no seasons. Market explicitly punts game-time analysis to callers. Economy planning documents reference seasonal trade, deity harvest cycles, and tick-based processing -- all assuming a clock service that was never built. Worldstate fills this gap as the single authoritative source of "what time is it in the game world?"

**Note on weather variables**: Some early ABML behavior examples (e.g., `humanoid-base.abml.yml`) reference `${world.weather.temperature}` and `${world.weather.raining}`. These references are **incorrect** -- weather and atmospheric conditions are the `${environment.*}` namespace owned by lib-environment (L4), not the `${world.*}` namespace owned by worldstate. Similarly, `guard-patrol.abml.yml` references `${world.patrol_routes[...]}` which is not temporal data and does not belong in the `world` namespace. These example behavior files predate the final namespace design and need updating as a cleanup item.

**What this service IS**:
1. A **game clock** -- per-realm time that advances as a configurable multiple of real time
2. A **calendar** -- days, months, seasons, years as configurable templates (opaque strings, not hardcoded)
3. A **variable provider** -- `${world.*}` namespace for ABML behavior expressions
4. A **boundary event broadcaster** -- publishes `worldstate.day-changed`, `worldstate.season-changed`, etc. for service-level tick processing
5. A **time-elapsed calculator** -- answers "how much game-time passed between these two real timestamps?" for lazy evaluation patterns used by autogain, seed decay, workshop production, and similar time-based processing

**What this service is NOT**:
- **Not weather simulation** -- weather, temperature, atmospheric conditions are L4 concerns that consume season/time-of-day data from worldstate
- **Not ecology** -- resource availability, deforestation, biome state are L4 concerns
- **Not a world simulation engine** -- it's a clock, a calendar, and a broadcast system
- **Not a spatial service** -- "where" things happen is Location and Mapping's concern

**The default Arcadia time scale** (configurable per realm, per game):

| Real Time | Game Time | Ratio |
|-----------|-----------|-------|
| 1 real second | 24 game seconds | 24:1 |
| 1 real minute | 24 game minutes | 24:1 |
| 1 real hour | 1 game day (24 game hours) | 24:1 |
| 1 real day | 24 game days ≈ 1 game month | 24:1 |
| 12 real days | 1 game year (12 months) | 24:1 |
| ~2.6 real years (960 days) | 80 game years (1 saeculum) | 24:1 |
| ~8 real months | 1 turning (20 game years) | 24:1 |

At a ratio of 24:1, a server running for 5 real years experiences nearly 2 full saeculums (160 game years). Generational play, seasonal cycles, and the content flywheel all operate at viable cadences without acceleration tricks.

**Zero game-specific content**: lib-worldstate is a generic temporal service. Arcadia's specific calendar (month names, season names, day-period boundaries), time ratio, and saeculum concept are configured through calendar template seeding and configuration at deployment time. A mobile farming game might use a 1:1 ratio with 4 real-time seasons. An idle game might use 1000:1 with 2-minute "days." All equally valid configurations.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on analysis of the temporal gap across the entire codebase -- the `${world.*}` variable references in ABML behaviors, the `TimeOfDay` trigger in Storyline, the `inGameTime` fields in Encounter and Relationship schemas, the `seasonalAvailability` in economy architecture, and the Currency autogain/Seed decay workers that currently use real-world time exclusively. Internal-only, never internet-facing.

## Summary

- **Total services**: 75
- **Total endpoints**: 833

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
