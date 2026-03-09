# Generated FAQ Catalog

> **Source**: `docs/faqs/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Architectural rationale FAQ documents explaining key design decisions in Bannou.

## How Do NPCs Actually Think? (The ABML/GOAP Behavior Stack) {#how-do-npcs-think}

**Last Updated**: 2026-03-08 | **Related Plugins**: Actor (L2), Behavior (L4), Puppetmaster (L4), Character Personality (L4), Character Encounter (L4), Character History (L4), Storyline (L4), Music (L4) | [Full FAQ](faqs/HOW-DO-NPCS-THINK.md)

NPCs run a 5-stage cognition pipeline (perceive, appraise, remember, evaluate goals, form intentions) powered by two complementary systems: ABML (Arcadia Behavior Markup Language) for reactive scripted behaviors compiled to portable bytecode, and GOAP (Goal-Oriented Action Planning) for dynamic goal-seeking through A* search over action spaces. The Actor service (L2) executes the bytecode; the Behavior service (L4) compiles it and runs the planner.

## How Does the Content Flywheel Actually Work? {#how-does-the-content-flywheel-actually-work}

**Last Updated**: 2026-03-08 | **Related Plugins**: Character History (L4), Character Personality (L4), Character Encounter (L4), Realm History (L4), Resource (L1), Storyline (L4), Puppetmaster (L4), Actor (L2), Quest (L2), Contract (L1), Procedural (L4) | [Full FAQ](faqs/HOW-DOES-THE-CONTENT-FLYWHEEL-ACTUALLY-WORK.md)

When characters die, their life data is compressed into archives. The Storyline service generates narrative seeds from those archives. Regional Watchers orchestrate those seeds into new scenarios. New players encounter those scenarios, live their own lives, die, and become new archives. The loop accelerates: Year 1 produces roughly 1,000 story seeds; Year 5 produces roughly 500,000.

## How Does the Economy Work Without Players? {#how-does-the-economy-work-without-players}

**Last Updated**: 2026-03-08 | **Related Plugins**: Currency (L2), Item (L2), Inventory (L2), Actor (L2), Escrow (L4), Trade (L4), Market (L4), Workshop (L4) | [Full FAQ](faqs/HOW-DOES-THE-ECONOMY-WORK-WITHOUT-PLAYERS.md)

NPCs drive the economy autonomously. The Currency, Item, and Inventory
services (all L2) provide wallets, goods, and containers, while Actor (L2) runs NPC brains
with GOAP planning that make economic decisions based on needs, personality, and world state.
If every player logs off, the economy continues because the participants are world citizens,
not player avatars.

## How Does One Binary Actually Deploy as Both a Monolith and Microservices? {#how-does-the-monoservice-actually-deploy}

**Last Updated**: 2026-03-08 | **Related Plugins**: Mesh (L0), State (L0), Messaging (L0), Orchestrator (L3) | [Full FAQ](faqs/HOW-DOES-THE-MONOSERVICE-ACTUALLY-DEPLOY.md)

Every service is a plugin assembly loaded at startup based on environment
variables. The same compiled binary runs all 74 services in one process for development,
or one service per container in production. The code does not know or care how it is
deployed.

## Is Arcadia a Metaverse? {#is-arcadia-a-metaverse}

**Last Updated**: 2026-03-08 | **Related Plugins**: Actor (L2), Resource (L1), Storyline (L4), Currency (L2), Escrow (L4), Character Lifecycle (L4), Divine (L4) | [Full FAQ](faqs/IS-ARCADIA-A-METAVERSE.md)

No. A metaverse is a virtual world designed around its users. Arcadia is a virtual
world that has users. Metaverses are anthropocentric platforms for human activity; Arcadia is a
cosmocentric simulation with 100,000+ autonomous NPCs that runs whether players are present or not.

## Isn't Bannou Just Way Too Big / Exceptionally Over-Engineered? {#isnt-bannou-over-engineered}

**Last Updated**: 2026-03-08 | **Related Plugins**: All Services (L0-L4) | [Full FAQ](faqs/ISNT-BANNOU-OVER-ENGINEERED.md)

No. The scope is a direct consequence of what it's trying to do -- autonomous
NPCs, a content flywheel, and a reusable game platform each independently require dozens of
orthogonal services. The engineering discipline exists because this is a solo developer project,
and a solo developer managing 73 services without structural enforcement would be insane.

## What Happens When a Client Connects to Bannou? {#what-happens-when-a-client-connects}

**Last Updated**: 2026-03-08 | **Related Plugins**: Auth (L1), Account (L1), Connect (L1), Permission (L1) | [Full FAQ](faqs/WHAT-HAPPENS-WHEN-A-CLIENT-CONNECTS.md)

The client authenticates via HTTP through Auth, receives a JWT and
WebSocket URL, establishes a persistent WebSocket connection through Connect, receives
a permission-filtered capability manifest compiled by Permission and delivered by
Connect, and from that point forward all communication flows through the binary-framed
WebSocket protocol. Four L1 services coordinate to make this happen.

## What Is a Seed and Why Is It Foundational? {#what-is-a-seed-and-why-is-it-foundational}

**Last Updated**: 2026-03-08 | **Related Plugins**: Seed (L2), Collection (L2), Status (L4), Gardener (L4), Faction (L4), Dungeon (L4), Actor (L2), Quest (L2), Currency (L2), Item (L2), Relationship (L2), Puppetmaster (L4) | [Full FAQ](faqs/WHAT-IS-A-SEED-AND-WHY-IS-IT-FOUNDATIONAL.md)

A Seed is a generic progressive growth primitive that starts empty and
gains capabilities as it accumulates experience across named domains. It is foundational
(L2) because progressive growth is a core game mechanic that multiple L4 features depend
on, and because Seeds are agnostic to what they represent -- guardian spirits, dungeon
cores, faction governance, and crafting specializations are all Seeds configured differently.

## What Is the Difference Between License and Collection? {#what-is-the-difference-between-license-and-collection}

**Last Updated**: 2026-03-08 | **Related Plugins**: License (L4), Collection (L2), Item (L2), Inventory (L2), Contract (L1), Seed (L2) | [Full FAQ](faqs/WHAT-IS-THE-DIFFERENCE-BETWEEN-LICENSE-AND-COLLECTION.md)

License manages progression boards where unlocking nodes costs something and follows structured rules (skill trees, tech trees, license boards). Collection manages content archives where entries are granted by experiencing them (bestiaries, music galleries, recipe books). License is about earning and spending. Collection is about discovering and cataloging.

## What Is the Variable Provider Factory Pattern and Why Does It Matter? {#what-is-the-variable-provider-factory-pattern}

**Last Updated**: 2026-03-08 | **Related Plugins**: Actor (L2), Character Personality (L4), Character Encounter (L4), Character History (L4), Quest (L2), Obligation (L4), Faction (L4), Seed (L2), Location (L2), Transit (L2), Worldstate (L2), Currency (L2), Inventory (L2), Relationship (L2), Puppetmaster (L4) | [Full FAQ](faqs/WHAT-IS-THE-VARIABLE-PROVIDER-FACTORY-PATTERN.md)

It is the dependency inversion mechanism that allows the Actor runtime (Layer 2)
to access data from Layer 4 services like Character Personality, Character Encounter, and Obligation
without depending on them. L4 services implement a shared interface and register via DI; Actor
discovers providers at runtime. Without it, either the service hierarchy breaks or NPCs cannot think.

## Why Are Character Personality, History, and Encounters THREE Separate Services? {#why-are-character-traits-three-services}

**Last Updated**: 2026-03-08 | **Related Plugins**: Character Personality (L4), Character History (L4), Character Encounter (L4), Character (L2), Actor (L2) | [Full FAQ](faqs/WHY-ARE-CHARACTER-TRAITS-THREE-SERVICES.md)

Because they have different data lifecycles, different scaling profiles,
different consumers, and different eviction strategies. Merging them would either couple
the Actor runtime to a monolithic dependency or force a single service to juggle three
incompatible state management patterns.

## Why Are Items and Inventory Separate Services? {#why-are-items-and-inventory-separate-services}

**Last Updated**: 2026-03-08 | **Related Plugins**: Item (L2), Inventory (L2), Escrow (L4), License (L4), Collection (L2), Status (L4), Loot (L4), Craft (L4) | [Full FAQ](faqs/WHY-ARE-ITEMS-AND-INVENTORY-SEPARATE-SERVICES.md)

Because "what a thing is" and "where a thing is" are fundamentally different
concerns with different consumers, different scaling characteristics, and different mutation
patterns. Item manages definitions and instances. Inventory manages containers and placement.
Merging them creates a service that does two unrelated jobs and prevents higher-layer services
from reusing the primitives independently.

## Why Are Realms Flat and Not Hierarchical? {#why-are-realms-flat-not-hierarchical}

**Last Updated**: 2026-03-08 | **Related Plugins**: Realm (L2), Location (L2), Game Service (L2), Subscription (L2), Seed (L2), Species (L2), Realm History (L4), Currency (L2) | [Full FAQ](faqs/WHY-ARE-REALMS-FLAT-NOT-HIERARCHICAL.md)

Because realms are parallel worlds, not nested subdivisions. Arcadia and
Fantasia are not regions of a larger world -- they are independent universes with different
rules, different species, different cultures, and different histories. Hierarchy implies
containment and shared context. Flatness reflects the actual relationship: peer worlds with
no structural dependency on each other.

## Why Are There No Skill, Magic, Or Combat Plugins? {#why-are-there-no-skill-magic-or-combat-plugins}

**Last Updated**: 2026-03-08 | **Related Plugins**: Seed (L2), License (L4), Status (L4), Collection (L2), Character Personality (L4), Ethology (L4), Actor (L2), Obligation (L4), Divine (L4), Character Lifecycle (L4), Faction (L4), Mapping (L4) | [Full FAQ](faqs/WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md)

Because combat, skills, magic, and character classes are not standalone
domains in Bannou -- they are behaviors that emerge from the interaction of existing
primitives. Character mastery is Seed, ability unlocks are License, active effects are
Status, content discovery is Collection, combat preferences are Character Personality,
and behavioral decisions are Actor plus ABML. A dedicated plugin for any of these would
duplicate what existing services already compose together.

## Why Can't the Website Show Character Profiles or Game Data? {#why-cant-the-website-show-game-data}

**Last Updated**: 2026-03-08 | **Related Plugins**: Website (L3), Character (L2) | [Full FAQ](faqs/WHY-CANT-THE-WEBSITE-SHOW-GAME-DATA.md)

Because Website is L3 (App Features) and character data lives in L2 (Game
Foundation). L3 services cannot depend on L2 — that is a hard hierarchy rule. If the website
imported character data, it could not be deployed without the entire game stack, which kills
Bannou's ability to deploy as a non-game cloud service platform.

## Why Does Bannou Use Client-Salted GUIDs Instead of Endpoint URLs? {#why-client-salted-guids-not-endpoint-urls}

**Last Updated**: 2026-03-08 | **Related Plugins**: Connect (L1), Permission (L1) | [Full FAQ](faqs/WHY-CLIENT-SALTED-GUIDS-NOT-ENDPOINT-URLS.md)

Because fixed endpoint URLs are a security liability in a
persistent-connection architecture. If every client uses the same URL for the
same endpoint, a captured message from one session can be replayed against
another. Client-salted GUIDs make each session's endpoint identifiers unique,
ephemeral, and cryptographically useless outside their originating session.

## Why Does Bannou Have Its Own Documentation Service Instead of Using a Wiki or CMS? {#why-does-bannou-have-its-own-documentation-service}

**Last Updated**: 2026-03-08 | **Related Plugins**: Documentation (L3), Website (L3) | [Full FAQ](faqs/WHY-DOES-BANNOU-HAVE-ITS-OWN-DOCUMENTATION-SERVICE.md)

Because the primary consumer of Bannou's documentation is not a human with a browser -- it is an AI agent with an HTTP client. The Documentation service is a knowledge base API, not a wiki.

## Why Does the Escrow Service Need a 13-State State Machine? {#why-does-escrow-need-13-states}

**Last Updated**: 2026-03-08 | **Related Plugins**: Escrow (L4), Currency (L2), Inventory (L2), Contract (L1) | [Full FAQ](faqs/WHY-DOES-ESCROW-NEED-13-STATES.md)

Because multi-party asset exchanges in a living economy have failure modes at every stage -- creation, deposit, consent, condition verification, release, and refund all need distinct states to prevent asset loss, double-spending, and deadlocks. The 13 states are the minimum needed to handle every real failure scenario, not an exercise in over-specification.

## Why Does Bannou Have an Orchestrator Service When Kubernetes Already Exists? {#why-does-orchestrator-exist-alongside-kubernetes}

**Last Updated**: 2026-03-08 | **Related Plugins**: Orchestrator (L3), Actor (L2), Mesh (L0) | [Full FAQ](faqs/WHY-DOES-ORCHESTRATOR-EXIST-ALONGSIDE-KUBERNETES.md)

Kubernetes orchestrates containers. The Orchestrator orchestrates
Bannou services. These are different problems at different abstraction levels, and
Kubernetes is one of several backends the Orchestrator can use.

## Why Does the Relationship Service Handle Both Types and Instances? {#why-does-relationship-handle-types-and-instances}

**Last Updated**: 2026-03-08 | **Related Plugins**: Relationship (L2) | [Full FAQ](faqs/WHY-DOES-RELATIONSHIP-HANDLE-TYPES-AND-INSTANCES.md)

Because relationship types and relationship instances are two sides of the same domain, and separating them created more problems than it solved. Relationship was originally two services (Relationship and Relationship-Type) that were consolidated because the type taxonomy is meaningless without instances to classify, and instances are meaningless without the type taxonomy to give them structure. Splitting them forced every consumer to import two clients for what is conceptually one operation.

## Why Does the Resource Service Exist at L1 Instead of Letting Services Manage Their Own Cleanup? {#why-does-resource-exist-at-l1}

**Last Updated**: 2026-03-08 | **Related Plugins**: Resource (L1), Character (L2), Actor (L2), Character Personality (L4), Character History (L4), Character Encounter (L4), Storyline (L4) | [Full FAQ](faqs/WHY-DOES-RESOURCE-EXIST-AT-L1.md)

Because foundational services (L2) cannot know about the higher-layer services
(L3/L4) that reference their entities. Without a layer-agnostic intermediary at L1, you either
violate the service hierarchy or accept orphaned data and unsafe deletions. Resource also
centralizes hierarchical compression for the content flywheel, providing unified archives that
Storyline uses to generate narrative seeds from accumulated play history.

## Why Don't Assets Route Through the WebSocket Gateway Like Everything Else? {#why-doesnt-asset-route-through-websocket}

**Last Updated**: 2026-03-08 | **Related Plugins**: Asset (L3), Connect (L1) | [Full FAQ](faqs/WHY-DOESNT-ASSET-ROUTE-THROUGH-WEBSOCKET.md)

Because routing a 500MB texture file through a 31-byte binary header protocol designed for JSON messages would be architecturally insane. Assets use pre-signed URLs so clients upload and download directly to object storage. The WebSocket gateway never touches the bytes.

## Why Doesn't Bannou Build Anti-Cheat, Replays, or Game Hosting? {#why-doesnt-bannou-build-anti-cheat-replays-or-game-hosting}

**Last Updated**: 2026-03-08 | **Related Plugins**: Connect (L1), Permission (L1), Chat (L1), Contract (L1), Game Session (L2), Analytics (L4), Save Load (L4), Mapping (L4), Orchestrator (L3), Mesh (L0), Telemetry (L0), Documentation (L3), Website (L3) | [Full FAQ](faqs/WHY-DOESNT-BANNOU-BUILD-ANTI-CHEAT-REPLAYS-OR-GAME-HOSTING.md)

Bannou builds composable primitives that create emergent gameplay, not
commodity infrastructure better served by specialists. Anti-cheat, game server hosting,
replays, content moderation, push notifications, and admin dashboards all fail the
composability test -- Bannou provides the integration hooks and server-authoritative
primitives while specialist providers handle the commodity layer.

## Why Doesn't Bannou Use AI/LLM for Content Generation? {#why-doesnt-bannou-use-ai-for-content-generation}

**Last Updated**: 2026-03-08 | **Related Plugins**: Music (L4), Behavior (L4), Storyline (L4), Character History (L4), Actor (L2), Resource (L1) | [Full FAQ](faqs/WHY-DOESNT-BANNOU-USE-AI-FOR-CONTENT-GENERATION.md)

Every content-producing system in Bannou uses formal theory and
deterministic rules instead of neural inference -- music uses formal music theory,
behavior uses ABML and GOAP planning, storyline uses formal narrative theory, and
compression uses deterministic templates. This is a core architectural decision
enabling Redis caching, test reproducibility, zero external dependencies, and
constant-cost scaling to 100,000+ concurrent NPCs.

## Why Don't Characters Belong To Player Accounts? {#why-dont-characters-belong-to-accounts}

**Last Updated**: 2026-03-08 | **Related Plugins**: Character (L2), Account (L1), Actor (L2), Seed (L2), Relationship (L2), Resource (L1), Storyline (L4), Subscription (L2), Game Service (L2) | [Full FAQ](faqs/WHY-DONT-CHARACTERS-BELONG-TO-ACCOUNTS.md)

Because characters are world citizens, not player possessions. The player's guardian spirit possesses and influences characters, but characters exist independently in the world with their own lives, relationships, and agency. Tying characters to accounts would break the living world, the content flywheel, and the guardian spirit model.

## Why Does Bannou Generate Music Procedurally Instead of Using Audio Files? {#why-generate-music-procedurally}

**Last Updated**: 2026-03-08 | **Related Plugins**: Music (L4), Collection (L2), Actor (L2), Behavior (L4) | [Full FAQ](faqs/WHY-GENERATE-MUSIC-PROCEDURALLY.md)

Because music in Arcadia is a game system, not ambiance. NPCs compose
it, players collect it, areas theme it, and the Collection service gates which tracks are
available. Pre-recorded audio files cannot participate in the content flywheel or respond
to world state. Procedural music can.

## Why Is Actor at Layer 2 Instead of Layer 4? {#why-is-actor-at-layer-2}

**Last Updated**: 2026-03-08 | **Related Plugins**: Actor (L2), Behavior (L4), Character Personality (L4), Character Encounter (L4), Character History (L4), Puppetmaster (L4) | [Full FAQ](faqs/WHY-IS-ACTOR-AT-LAYER-2.md)

Because behavior execution is foundational infrastructure, not an optional feature. If every NPC in the world runs an actor brain -- shopkeepers, guards, farmers, dungeon cores, regional watchers -- then the runtime that executes those brains is as fundamental as characters, realms, or items. The distinction is between the execution engine (foundational) and the content it executes (optional).

## Why Is the Contract Service at L1 (App Foundation) Instead of a Game Layer? {#why-is-contract-l1-not-game-layer}

**Last Updated**: 2026-03-08 | **Related Plugins**: Contract (L1), Quest (L2), Escrow (L4), License (L4) | [Full FAQ](faqs/WHY-IS-CONTRACT-L1-NOT-GAME-LAYER.md)

Because Contract provides a generic finite state machine with consent flows and milestone-based progression. It knows nothing about games, quests, escrow, or any specific domain. It is reusable infrastructure -- like a database or a message broker, but for multi-party agreements.

## Why Is Game Session at Layer 2 Instead of Layer 4? {#why-is-game-session-at-l2-not-l4}

**Last Updated**: 2026-03-08 | **Related Plugins**: Game Session (L2), Matchmaking (L4), Subscription (L2), Permission (L1), Connect (L1), Gardener (L4) | [Full FAQ](faqs/WHY-IS-GAME-SESSION-AT-L2-NOT-L4.md)

Because "characters are in a game session" is a foundational fact about the game world, not an optional feature. Game Session tracks which characters are actively playing, manages lobby entry points for subscribed accounts, and coordinates permission state transitions. If Game Session were L4, the foundational services (Character, Actor, Quest) could not assume characters have active sessions, and the Matchmaking service (L4) would have no guaranteed session infrastructure to create matches into.

## Why Is Layer 3 a Separate Branch From Layer 2 Instead of Stacked On Top? {#why-is-l3-a-separate-branch-from-l2}

**Last Updated**: 2026-03-08 | **Related Plugins**: Asset (L3), Orchestrator (L3), Documentation (L3), Website (L3), Voice (L3), Puppetmaster (L4), Actor (L2), Behavior (L4), Save Load (L4), Mapping (L4) | [Full FAQ](faqs/WHY-IS-L3-A-SEPARATE-BRANCH-FROM-L2.md)

Because App Features and Game Foundation solve problems in different domains. Stacking them would force a false dependency: either operational tools would need game services to function, or game services would need operational tools. Neither makes sense.

## Why Is Puppetmaster Separate from Behavior? {#why-is-puppetmaster-separate-from-behavior}

**Last Updated**: 2026-03-08 | **Related Plugins**: Actor (L2), Asset (L3), Behavior (L4), Puppetmaster (L4) | [Full FAQ](faqs/WHY-IS-PUPPETMASTER-SEPARATE-FROM-BEHAVIOR.md)

Behavior compiles ABML and runs the GOAP planner -- it is a
computation service. Puppetmaster orchestrates which behaviors run where, loads
them dynamically from the Asset service, manages Regional Watchers, and
coordinates encounters. They solve fundamentally different problems: Behavior
answers "what should this NPC do?", Puppetmaster answers "what should be
happening in this region?"

## Why Is Quest a Contract Wrapper? {#why-is-quest-a-contract-wrapper}

**Last Updated**: 2026-03-08 | **Related Plugins**: Quest (L2), Contract (L1), Escrow (L4), License (L4), Actor (L2) | [Full FAQ](faqs/WHY-IS-QUEST-A-CONTRACT-WRAPPER.md)

Because quests ARE contracts. A quest is a binding agreement between
parties (quest giver and quest taker) with milestones (objectives), terms (rewards and
penalties), consent flows (accepting the quest), and state machine progression (objective
completion sequence). Building a bespoke quest engine would mean reimplementing Contract's
state machine, cleanup orchestration, and prebound API execution -- all of which already
exist and are battle-tested. Quest translates game-flavored semantics into Contract
infrastructure and adds quest-specific concerns (prerequisites, quest giver roles, reward
distribution).

## Why Is There No Player Housing Plugin? {#why-is-there-no-player-housing-plugin}

**Last Updated**: 2026-03-08 | **Related Plugins**: Actor (L2), Agency (L4), Asset (L3), Behavior (L4), Connect (L1), Craft (L4), Divine (L4), Game Session (L2), Gardener (L4), Inventory (L2), Item (L2), Permission (L1), Puppetmaster (L4), Save Load (L4), Scene (L4), Seed (L2) | [Full FAQ](faqs/WHY-IS-THERE-NO-PLAYER-HOUSING-PLUGIN.md)

Player housing is not a dedicated service but a garden type that composes
entirely from existing primitives. Gardener provides the conceptual space, Seed provides
progressive capability unlocks, Scene stores the layout, Item and Inventory handle furnishing,
and a divine god-actor via Puppetmaster orchestrates the experience.

## Why Does Bannou Precompile Permission Manifests Instead of Checking Permissions Per-Request? {#why-precompile-permissions-not-check-per-request}

**Last Updated**: 2026-03-08 | **Related Plugins**: Permission (L1), Connect (L1), Auth (L1), Game Session (L2), Matchmaking (L4), Voice (L3) | [Full FAQ](faqs/WHY-PRECOMPILE-PERMISSIONS-NOT-CHECK-PER-REQUEST.md)

Because the system routes 100,000+ concurrent NPC decisions and player actions
through the WebSocket gateway. Checking a multi-dimensional permission matrix on every message
would add latency to every single operation. Precompiling the manifest once and pushing it to
the client turns permission enforcement into a local lookup at the gateway with zero additional
latency per message.

## Why Is Authentication Separated From Account Management? {#why-separate-auth-from-account}

**Last Updated**: 2026-03-08 | **Related Plugins**: Account (L1), Auth (L1), Connect (L1) | [Full FAQ](faqs/WHY-SEPARATE-AUTH-FROM-ACCOUNT.md)

Because the two services have fundamentally different trust
boundaries, scaling characteristics, and failure modes. Merging them creates a
single service that is simultaneously internet-facing and the authoritative
data store -- a security and operational anti-pattern.

## Why Does Bannou Route Everything Through a WebSocket Gateway Instead of Using REST? {#why-websocket-gateway-not-rest}

**Last Updated**: 2026-03-08 | **Related Plugins**: Connect (L1), Auth (L1), Permission (L1), Mesh (L0), Game Session (L2), Matchmaking (L4), Asset (L3), Voice (L3), Website (L3), Behavior (L4) | [Full FAQ](faqs/WHY-WEBSOCKET-GATEWAY-NOT-REST.md)

The system needs persistent, bidirectional, low-latency connections to support
100,000+ concurrent AI NPCs pushing real-time state to clients. REST is request-response so the
server cannot push to the client. WebSocket is bidirectional so the server pushes capability
updates, game events, NPC actions, and permission changes without the client polling.

## Summary

- **Documents in catalog**: 36

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
