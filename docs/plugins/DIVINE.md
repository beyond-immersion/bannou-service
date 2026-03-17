# Divine Plugin Deep Dive

> **Plugin**: lib-divine
> **Schema**: schemas/divine-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: divine-deities (MySQL), divine-blessings (MySQL), divine-attention (Redis), divine-lock (Redis)
> **Implementation Map**: [docs/maps/DIVINE.md](../maps/DIVINE.md)
> **Planning**: [docs/plans/DIVINE.md](../plans/DIVINE.md)
> **Short**: Pantheon management, divinity economy, blessing orchestration (composes Currency/Seed/Collection/Status)

## Overview

Pantheon management service (L4 GameFeatures) for deity entities, divinity economy, and blessing orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver divine game mechanics: god identity is owned here, behavior runs via Actor/Puppetmaster, domain power via Seed, divinity resource via Currency, blessings via Collection/Status, and follower bonds via Relationship. Gods influence characters indirectly through the character's own Actor -- a god's Actor monitors event streams and makes decisions, but the character's Actor receives the consequences. Blessings are entity-agnostic (characters, accounts, deities, or any entity type can receive them). All endpoints are currently stubbed (return `NotImplemented`); see the implementation plan at `docs/plans/DIVINE.md` for the full specification.

---

## Composability & Architecture

**Divine actors are both puppetmasters and gardeners**: A god tending a physical realm region (spawning encounters, adjusting NPC moods, orchestrating narrative opportunities) and a god tending a player's conceptual garden space (spawning POIs, managing scenario selection, guiding discovery) are the same operation from different perspectives -- two sides of the same coin. The divine actor launched via Puppetmaster as a regional watcher also serves as the gardener behavior actor for player experience orchestration via Gardener's APIs. Whether the "space" being tended is a physical location in the game world or an abstract conceptual space (a void garden, a lobby, player housing) is a behavioral distinction encoded in the god's ABML behavior document, not a structural difference in the actor type. This means: (1) the same god (e.g., Moira/Fate) that creates emergent content in the physical world also curates which experiences reach players through their gardens, directly connecting the content flywheel to the player experience; (2) any conceptual space can potentially become a physical space and vice versa, because the transition is just the god shifting focus between garden types; (3) lib-gardener provides the tools (garden instances, POIs, scenarios, entity associations), lib-puppetmaster provides the actor lifecycle, and lib-divine provides the identity and economy of the entity doing the tending.

**Dynamic character binding via lib-genesis**: Deity entities are created as genesis entities using the "deity_domain" template. Divine creates the deity's seed externally (via Seed API) and passes the seedId to Genesis via the nullable `seedId` parameter on `/genesis/entity/create` — this allows Divine to retain the seedId for seed bond operations while Genesis manages the lifecycle. [lib-genesis](GENESIS.md) (L2) handles the full Actor-Bound Entity lifecycle — wallet provisioning, actor spawning at Stirring phase, character creation and binding at Awakened phase. Divine subscribes to `genesis.entity.phase-changed` for divine-specific post-transition work (attention slot initialization, follower management setup, divinity economy activation). The ABML behavior document supports both pre- and post-binding states — before binding, `${personality.*}` expressions resolve to null and the god uses default decision paths; after binding, the god reasons with its full personality, history, and encounter memory. This means deity creation doesn't need to block on character provisioning, and the god can begin its duties immediately. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md) for the full external seed adoption pattern.

**Divinity generation via seed bond propagation**: Divinity is NOT generated from Analytics events. Instead, mortal domain activity flows to gods through Seed's bond propagation mechanism. Characters have domain seeds (e.g., "war", "craft") that can be bonded to a god's matching domain seed. When a character's seed grows (from wallet credits via Genesis growth mappings), the bond propagates a configurable ratio of that growth to the god's seed. This operates entirely at L2 with no Divine involvement — Seed handles propagation, Genesis handles phase transitions. Direct divinity (prayer, offerings) credits the god's wallet directly. The god-actor perceives accumulated resources via variable providers and makes autonomous GOAP decisions. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md) for the full architecture.

**Patron deity and auto-bonding**: Characters have a `patronDeityCode` field (opaque string, nullable) on the Character record (L2). Character Lifecycle (L4) provides generational inheritance — children inherit their parents' patron deity. When Divine detects a `patronDeityCode` change (via `character.updated` event), it looks up the deity code in a runtime bond template registry (built from deity actor seed data at startup) and auto-initiates seed bonds between the character's domain seeds and the patron god's matching seeds. Bond `PropagationDirection` and `PropagationRatio` are per-bond, configured in the deity's bond template — different gods offer different bond characteristics (Ares: aggressive one-way `AToB`; Athena: balanced `Bidirectional`). Patron (god favors character) and follower (character favors god) are separate relationships that map to bond direction.

**Deity deprecation lifecycle (Category A)**: Deities are world-building definitions referenced by blessings and followers. Per T31, they support Category A deprecation: deprecate (with reason), undeprecate (gods can return), and merge (transfer followers/blessings to another deity). Merge auto-dissolves follower seed bonds to the deprecated deity and creates new bonds to the target deity per its bond template. Delete requires `IsDeprecated == true`. `IDeprecateAndMergeEntity` marker interface.

**Zero Arcadia-specific content**: lib-divine is a generic pantheon management service. Arcadia's 18 Old Gods are configured through behaviors and templates at deployment time, not baked into lib-divine.

**Domain codes are opaque strings**: Different games define different domains (War, Knowledge, Nature, etc.). Domain codes follow the same extensibility pattern as seed type codes, collection type codes, and relationship type codes.

---

## God Characters in System Realms

> **Status**: Architectural concept. All dependencies are existing services; no new service code required beyond Divine's planned implementation and authored ABML behavior documents. This section documents a design pattern enabled by Realm's `isSystemType` flag.

### The Insight

Realm's `isSystemType` flag creates conceptual namespaces for entities that exist outside physical reality. A divine system realm (e.g., `PANTHEON` with `isSystemType: true`) allows every deity to have a **Character record** -- and the moment a god is a character, the entire L2/L4 entity stack activates for them with zero new infrastructure:

| Existing Service | What Gods Get For Free |
|---|---|
| **Character** (L2) | Identity record, realm binding, species association, alive/dead lifecycle |
| **Species** (L2) | A "divine" species in the system realm -- type taxonomy, trait modifiers |
| **Relationship** (L2) | Divine genealogy: parent/child, spouse, sibling, rival between gods |
| **Character Personality** (L4) | God personality traits on bipolar axes -- quantified, evolvable, available as `${personality.*}` |
| **Character History** (L4) | Divine historical events -- wars between gods, realm creation, betrayals |
| **Character Encounter** (L4) | Gods' memories of notable mortal interactions, grudges, favorites |
| **Seed** (L2) | Deity domain power seed can be character-owned (already planned) |
| **Collection** (L2) | Already used for permanent blessings -- gods could also collect divine artifacts |
| **Actor** (L2) | God-actors bind to their character, becoming **character brain** actors with all variable providers |

### God-Actors as Character Brains (via Dynamic Binding)

The [Behavioral Bootstrap](../guides/BEHAVIORAL-BOOTSTRAP.md) pattern originally conceptualized god-actors as event brain actors that use `load_snapshot:` for ad-hoc data about arbitrary entities. With a divine character record, god-actors become **character brain actors** with automatic variable provider binding. **Dynamic character binding** (Actor's `BindCharacterAsync` API) makes this transition seamless: a god-actor can start as an event brain, create its divine character profile, and then bind to it at runtime without relaunching.

```
God-actor lifecycle (dynamic binding):

1. Actor spawned (event brain, no character)
 ├── ABML behavior document references ${personality.*} etc.
 │ (resolves to null/empty -- no character to load from)
 ├── Uses load_snapshot: for ad-hoc mortal data
 └── Operates as regional watcher / garden tender

2. Divine character created in system realm
 ├── Character record, species: "divine", realm: PANTHEON
 ├── Personality traits seeded from deity configuration
 └── Backstory/history from deity mythology

3. POST /actor/bind-character (actorId, divineCharacterId)
 ├── CharacterId set on running ActorRunner
 ├── Per-character perception subscription established
 ├── ActorCharacterBoundEvent published
 └── Next tick: variable providers activate

4. God-actor (character brain, bound to divine system realm character)
 ├── ${personality.*} ← CharacterPersonality (the god's own quantified personality)
 ├── ${encounters.*} ← CharacterEncounter (memories of mortal interactions)
 ├── ${backstory.*} ← CharacterHistory (the god's mythology and divine history)
 ├── ${quest.*} ← Quest (divine quests the god is tracking)
 ├── ${world.*} ← Worldstate (current game time, season)
 ├── ${obligations.*} ← Obligation (divine contracts the god is bound by)
 └── ...can still use load_snapshot: for ad-hoc mortal data (event brain capability)
```

The ABML behavior document supports both modes from the start. Before binding, expressions like `${personality.mercy}` evaluate to null and the behavior falls through to default paths. After binding, the same expressions return real data and the god makes richer, personality-driven decisions. No behavior document swap is needed -- the same document, progressively richer data.

No new variable providers are needed. The standard character brain infrastructure provides everything. A god's ABML behavior can naturally reference `${personality.mercy}` to decide intervention thresholds, `${encounters.last_hostile_days}` to check grudges against specific mortals, and `${backstory.origin}` to reason about its own mythology.

This also obsoletes Potential Extension #7 (`IDivineVariableProviderFactory`) -- gods don't need a custom variable provider because they ARE characters and get all character-based providers automatically. A separate `${divine.*}` provider may still be useful for exposing divine-specific data (blessing counts, divinity balance, domain power) to other actors' behaviors, but gods themselves get their own cognition data through the standard character brain path.

### Avatar Manifestation Pattern

Gods in the system realm can manifest avatars in physical realms -- separate Character records with their own independent actors. The god-actor remains permanently bound to its divine system realm character; the avatar is a wholly separate entity created and managed through the god's runtime behavior.

```
DIVINE_REALM (system realm, isSystemType: true)
├── Moira (Character, species: "Fate Weaver", alive)
│ └── Actor (character brain, long-running, PERMANENT binding)
│ ├── Perceives world events globally via watch system
│ ├── Decides to manifest in the physical world
│ └── Calls /divine/avatar/manifest (spends divinity, creates avatar)
│
ARCADIA (physical realm)
└── "The Veiled Oracle" (Character, species: "Human", alive)
 ├── Created by Divine service (orchestrates character + relationship + actor)
 ├── Linked to Moira via Relationship (divine_manifestation type)
 └── Actor (character brain, SEPARATE independent instance)
 ├── Personality derived from Moira's, filtered through mortal form
 ├── Interacts with players and NPCs as any character would
 ├── Can be killed → death archive feeds content flywheel
 └── Moira perceives avatar death via watch system, reacts
```

The god-actor's character brain binding to its divine character is **permanent once established and never changes**. The binding can be established either at spawn time (if the divine character already exists) or at runtime via `BindCharacterAsync` (if the character is created after the actor starts). The avatar is a separate character with its own separate actor. When the avatar dies, the avatar's actor stops, but the god-actor continues uninterrupted with full access to its own `${personality.*}`, `${encounters.*}`, etc.

**Avatar creation is a Divine API operation, not raw service composition.** The god-actor calls `/divine/avatar/manifest`, which orchestrates the multi-service creation and enforces divine economy rules:

1. Validate deity is Active and has no existing avatar (or enforce max concurrent avatars)
2. Calculate divinity cost -- base cost scaled by recency of last avatar death (spawning a replacement immediately after one was killed costs significantly more than waiting)
3. Debit divinity from the deity's wallet
4. Create Character in the target physical realm
5. Create `divine_manifestation` Relationship linking deity character to avatar character
6. Spawn Actor for the avatar character with the specified behavior document
7. Register watch on avatar character (so the god perceives avatar events)
8. Track active avatar on `DeityModel` (`activeAvatarCharacterId`, `lastAvatarDeathAt`)
9. Publish `divine.avatar.manifested` event

When the god's watch system detects the avatar's death, it should call `/divine/avatar/on-death` to update tracking state (`lastAvatarDeathAt`, clear `activeAvatarCharacterId`), which feeds the cooldown cost calculation for the next manifestation.

```yaml
# Avatar manifestation (in god's behavior document)
- call: /divine/avatar/manifest
 with:
 deityId: ${self.deity_id}
 realmId: ${target_realm_id}
 name: ${avatar_name}
 speciesId: ${mortal_species_id}
 behaviorDocumentId: ${avatar_behavior_ref}
 into: avatar_result

# Watch the avatar for lifecycle events
- watch:
 resource_type: character
 resource_id: ${avatar_result.avatarCharacterId}
```

This keeps avatar lifecycle within Divine's orchestration domain -- costs, cooldowns, tracking, and economy are all mediated by the service that owns the god's persistent state, not scattered across raw API calls in behavior documents.

### Divinity Currency Convention

With system realms, divinity currency should use `realm_specific` scope pointed at the divine system realm. This is a deployment convention, not a schema change -- the existing `CurrencyScope` enum already supports it:

- God wallets are scoped to the divine system realm (semantically correct -- divinity "lives" there)
- Querying "all wallets in the divine realm" yields all god wallets naturally
- Global and multi-realm currencies remain unaffected (`realmId` stays optional on currency)

### Broader System Realm Implications

The divine system realm establishes a reusable pattern for other conceptual entity spaces:

| System Realm | Purpose | Entities |
|---|---|---|
| **DIVINE / PANTHEON** | The gods | God characters, divine species, divine genealogy, divine history |
| **VOID** | The between-space | Already exists as seeding convention; could house void-specific entities |
| **UNDERWORLD** | Death/afterlife | Dead characters transferred here instead of archived; afterlife gameplay per VISION.md soul architecture |
| **NEXIUS** | Goddess of connections | Guardian spirits as characters; pair bonds as Relationships; spirit evolution as character growth |

The **Underworld** is particularly relevant to the content flywheel. VISION.md describes death as transformation with aspiration-based afterlife pathways. If the underworld is a system realm, dead characters are "transferred" there (realm transfer, not deletion), continue as actors running afterlife behaviors, and generate encounters and history that feed back into the living world's narrative generation.

**Guardian spirits as Nexius characters** would give the pair bond from PLAYER-VISION.md a concrete implementation: a Relationship between two characters in the Nexius realm. Spirit evolution becomes character growth via Seed. Twin spirits are linked via relationship types. Same pattern as divine characters applied to the player's own metaphysical entity.

### What Needs to Change

Almost nothing in the codebase. The changes are:

1. **Register "deity_domain" genesis template** — seed data defining the deity seed type, divinity wallet with growth mappings, system realm code (PANTHEON), and phase-specific ABML behavior references. Genesis handles the full lifecycle from this template.
2. **Seed the divine system realm** via `/realm/seed` with `isSystemType: true` — configuration, not code
3. **Register a divine species** in that realm — seed data, not code
4. **Divine implementation**: Create deity → create genesis entity → store `genesisEntityId` on `DeityModel`. Subscribe to `genesis.entity.phase-changed` for divine-specific post-transition work (attention slot initialization, follower management, divinity economy activation). Genesis handles actor spawning, character creation, and binding automatically as divinity accumulates.
5. **Avatar behaviors**: Author ABML behavior documents for manifestation lifecycle — pure content authoring
6. **Seed data**: A `divine_manifestation` relationship type, a `divine` species, divine personality trait profiles — all registered on startup

Some services that list characters may want to exclude system realms by default. Character's `listByRealm` already filters by realm, so queries against physical realms naturally exclude gods without code changes.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Divine is a new L4 service with no current consumers. Future dependents may include Variable Provider Factory implementations for ABML behavior expressions (`${divine.*}`) |
| lib-director *(planned)* | During directed events, Director queries deity state (domain power, active blessings, follower counts) for divine actor observation context, and coordinates divine actors during god-driven events. Director interacts with divine actors primarily through Actor APIs (tap, steer, drive), but calls Divine APIs for deity-specific state enrichment. See [DIRECTOR.md](DIRECTOR.md) |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DivinityCurrencyCode` | `DIVINE_DIVINITY_CURRENCY_CODE` | `divinity` | Currency code for divinity economy within each game service |
| `DivinityCostMinor` | `DIVINE_DIVINITY_COST_MINOR` | `10.0` | Divinity cost for Minor tier blessing |
| `DivinityCostStandard` | `DIVINE_DIVINITY_COST_STANDARD` | `50.0` | Divinity cost for Standard tier blessing |
| `DivinityCostGreater` | `DIVINE_DIVINITY_COST_GREATER` | `200.0` | Divinity cost for Greater tier blessing |
| `DivinityCostSupreme` | `DIVINE_DIVINITY_COST_SUPREME` | `1000.0` | Divinity cost for Supreme tier blessing |
| `BlessingCollectionType` | `DIVINE_BLESSING_COLLECTION_TYPE` | `divine_blessings` | Collection type code for permanent blessings via lib-collection |
| `BlessingStatusCategory` | `DIVINE_BLESSING_STATUS_CATEGORY` | `divine_blessing` | Status category code for temporary blessings via Status Inventory |
| `MaxBlessingsPerEntity` | `DIVINE_MAX_BLESSINGS_PER_ENTITY` | `10` | Maximum active blessings an entity can hold simultaneously |
| `FollowerRelationshipTypeCode` | `DIVINE_FOLLOWER_RELATIONSHIP_TYPE_CODE` | `deity_follower` | Relationship type code for deity-character follower bonds |
| `RivalryRelationshipTypeCode` | `DIVINE_RIVALRY_RELATIONSHIP_TYPE_CODE` | `deity_rivalry` | Relationship type code for deity-deity rivalry bonds |
| `DefaultMaxAttentionSlots` | `DIVINE_DEFAULT_MAX_ATTENTION_SLOTS` | `10` | Default max characters a deity can actively monitor |
| `AttentionDecayIntervalMinutes` | `DIVINE_ATTENTION_DECAY_INTERVAL_MINUTES` | `60` | Minutes between attention slot decay evaluations |
| `AttentionImpressionThreshold` | `DIVINE_ATTENTION_IMPRESSION_THRESHOLD` | `0.1` | Minimum impression below which an attention slot is freed |
| `DeitySeedTypeCode` | `DIVINE_DEITY_SEED_TYPE_CODE` | `deity_domain` | Seed type code for deity domain power growth |
| `DeityActorTypeCode` | `DIVINE_DEITY_ACTOR_TYPE_CODE` | `deity_watcher` | Actor type code for deity watcher actors via Puppetmaster |
| `AttentionWorkerIntervalSeconds` | `DIVINE_ATTENTION_WORKER_INTERVAL_SECONDS` | `60` | Seconds between attention decay worker cycles |

## Visual Aid

```
┌──────────────────────────────────────────────────────────────────────┐
│ Divine Service Composability                                         │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  lib-divine (L4) ── "What a god IS"                                  │
│  ┌──────────────┐                                                    │
│  │ DeityModel   │──── identity, domains, affectations, status        │
│  │ BlessingModel│──── blessing records linking deities to entities   │
│  │ AttentionSlot│──── which characters a god is "watching"           │
│  └──────┬───────┘                                                    │
│         │ orchestrates                                               │
│         ▼                                                            │
│  ┌─────────────────────────────────────────────────────────────┐     │
│  │ Existing Primitives (L0/L1/L2)                              │     │
│  │                                                             │     │
│  │ Currency ──── divinity wallets (credit/debit/balance)       │     │
│  │ Seed ──────── domain power growth + bond propagation        │     │
│  │ Genesis ───── entity awakening lifecycle (wallet→seed)      │     │
│  │ Relationship ─ follower bonds (deity↔character)             │     │
│  │ Collection ── permanent blessings (Greater/Supreme)         │     │
│  │ Character ─── patronDeityCode field (opaque, nullable)      │     │
│  └─────────────────────────────────────────────────────────────┘     │
│         │ soft dependencies (L4)                                     │
│         ▼                                                            │
│  ┌─────────────────────────────────────────────────────────────┐     │
│  │ Optional Features (L4, graceful degradation)                │     │
│  │                                                             │     │
│  │ Puppetmaster ─ deity watcher actor lifecycle                │     │
│  │ Status ─────── temporary blessings (Minor/Standard)         │     │
│  │ Char Lifecycle  generational patron deity inheritance       │     │
│  └─────────────────────────────────────────────────────────────┘     │
│                                                                      │
│  Background Workers                                                  │
│  ┌─────────────────────┐                                             │
│  │ AttentionWorker     │                                             │
│  │ Decays idle slots   │                                             │
│  │ Frees capacity      │                                             │
│  └─────────────────────┘                                             │
│                                                                      │
│  Divinity Generation (L2, no Divine involvement)                     │
│                                                                      │
│  Character fights → wallet credit → Genesis growth mapping           │
│    → character's "war" seed grows                                    │
│    → Seed bond propagation → god's "war" seed grows                  │
│    → Genesis ISeedEvolutionListener → god awakens progressively      │
│  Prayer → credits god's wallet directly → Genesis growth mapping     │
│    → god's seed grows → god-actor perceives → decides autonomously   │
│                                                                      │
│  God Influence Paths (Two Sides of the Same Coin)                    │
│                                                                      │
│  Realm-Tending (via Puppetmaster):                                   │
│    God's Actor monitors realm events → decides → publishes           │
│    Character's Actor consumes consequences → adjusts behavior        │
│    (gods act through intermediaries, never directly)                 │
│                                                                      │
│  Garden-Tending (via Gardener):                                      │
│    God's Actor monitors player drift/events → decides → calls        │
│    Gardener APIs (spawn POI, manage transitions, shift bindings)     │
│    (same actor, same decision-making, different toolbox)             │
└──────────────────────────────────────────────────────────────────────┘
```

## Stubs & Unimplemented Features

All endpoints are currently stubbed (return `NotImplemented`). The following are planned but not yet implemented:

1. **All deity CRUD operations**: Create, get, get-by-code, list, update, activate, deactivate, delete
2. **Deity deprecation lifecycle (Category A)**: Deprecate, undeprecate, merge (using shared `MergeDeprecatedRequest`/`MergeDeprecatedResponse`). Delete requires `IsDeprecated == true`.
3. **All divinity economy operations**: Get balance, credit, debit, get history
4. **All blessing orchestration**: Grant, revoke, list-by-entity, list-by-deity, get
5. **All follower management**: Register, unregister, get followers
6. **All cleanup endpoints**: Cleanup-by-character, cleanup-by-game-service
7. **Background workers**: Attention decay worker
8. **Event handlers**: `HandleCharacterUpdated` (patron deity auto-bonding — when `changedFields` includes `patronDeityCode`, look up deity in bond template registry, dissolve old bonds, create new bonds), `HandleCharacterCreated` (initial patron deity auto-bonding at birth)
9. **Bond template registry**: In-memory `ConcurrentDictionary` mapping deity codes to seed bond templates (direction, ratio per domain). Built from deity actor seed data in `OnRunningAsync`. Invalidated via self-event-subscription on deity created/updated/deleted events.
10. **Plugin startup registration**: Seed type, currency definition, relationship types, collection/status templates. Pattern established by Gardener/Faction: call `RegisterSeedTypeAsync` etc. in `OnRunningAsync` with inline definitions, catch 409 for idempotent restart, use config code properties (`DeitySeedTypeCode`, `BlessingCollectionType`, `FollowerRelationshipTypeCode`, `RivalryRelationshipTypeCode`)

## Potential Extensions

1. **Holy Magic Invocations (via Contract)**: Short-lived micro-contracts between deity and caster for spell invocations. Each invocation creates a Contract instance, the deity's Actor evaluates worthiness, and the Contract resolves with success/failure.
2. **DanMachi Leveling Gate**: `ILevelGateProviderFactory` interface where Greater Blessings serve as rank-up authorization. Deferred until Character has a leveling mechanic.
3. **God Personality Simulation & Choreographic Preferences**: ABML behavior documents modeling god attention patterns, jealousy mechanics, inter-deity politics, and **choreographic extension preferences for combat cinematics**. DivineAffectations (temperament, attentionBias, generosity, jealousy) map to choreographic style: high temperament gods compose aggressive, decisive extensions quickly; high attentionBias gods compose narrative-callback extensions that reference encounter history; high generosity gods compose redemptive extensions with heroic saves. When multiple god-actors register extensions at the same combat continuation point, FCFS (first-come, first-served) determines which god shapes the choreography -- simpler/aggressive extensions arrive first and win more often, creating emergent regional combat flavor. See [VIDEO-DIRECTOR.md § Initiative-Driven Combat](../planning/VIDEO-DIRECTOR.md#initiative-driven-combat-the-interactive-genre) for the initiative model, [COMPOSITIONAL-CINEMATICS.md § 4.5](../planning/COMPOSITIONAL-CINEMATICS.md#45-multi-producer-composition-fcfs-extension-competition) for the multi-producer model, and [#694](https://github.com/beyond-immersion/bannou-service/issues/694) / [#695](https://github.com/beyond-immersion/bannou-service/issues/695) for the FCFS registry and preference mapping designs. A behavior authoring task, not a service implementation task.
4. **Domain Contests**: When two gods share domain influence, divinity generation splits by relative power with challenge mechanics.
5. **Deity-Deity Rivalries**: Active rivalry mechanics where gods sabotage each other's followers. Relationship records exist; behavioral consequences are deferred.
6. **Client Events**: `divine-client-events.yaml` for pushing blessing notifications, divine attention alerts, and divinity milestones to connected WebSocket clients.
7. **Variable Provider Factory**: `IDivineVariableProviderFactory` for ABML behavior expressions (`${divine.blessing_tier}`, `${divine.patron_deity}`, `${divine.divinity_earned}`). **Note**: The [System Realm pattern](#god-characters-in-system-realms) partially obsoletes this -- gods as character brains get `${personality.*}`, `${encounters.*}`, `${backstory.*}` etc. for free. A `${divine.*}` provider remains useful for exposing divine-specific data (divinity balance, domain power, blessing counts) to *other* actors' behaviors (e.g., an NPC checking its patron god's mood).
8. **Economic Deity Behaviors**: Specialized ABML behavior documents for economic deities that monitor money velocity via analytics, spawn narrative intervention events (business opportunities, dropped wallets, thefts, treasure discoveries) to maintain healthy velocity, and respect location-level stagnation policies. God personalities (subtlety, chaos affinity, favored targets) modulate intervention style and frequency. GOAP flows evaluate velocity thresholds, hoarding detection, and intervention cooldowns. See [Economy System Guide](../guides/ECONOMY-SYSTEM.md#5-divine-economic-intervention) for the full design.
9. **Deity Realm Economic Assignment**: Track which realms each economic deity watches, with per-deity personality parameters (intervention frequency, subtlety, favored targets, chaos affinity) that affect how they maintain economic health. Multiple gods per realm creates emergent economic dynamics from competing intervention styles.
10. **Housing Garden Divine Behaviors**: ABML behavior documents for gods tending housing gardens -- managing seasonal decoration changes, NPC servant arrivals, visitor events, environmental reactions (storms damaging roofs, gardens blooming), and gating voxel construction capabilities based on the housing seed's growth phase. The same divine actor that tends a physical realm region can also tend a player's housing garden, deciding what happens in the home (a visiting merchant NPC, a stray cat adopting the player, a gift left by a divine patron). This is a behavior authoring task composed entirely from existing service APIs (Gardener, Scene, Item/Inventory, Seed), not a service implementation task. See [Gardener: Housing Garden Pattern](GARDENER.md#housing-garden-pattern-no-plugin-required) and [VOXEL-BUILDER-SDK.md](../planning/VOXEL-BUILDER-SDK.md).
11. **Avatar Manifestation API + Behaviors**: Divine API endpoints (`/divine/avatar/manifest`, `/divine/avatar/on-death`, `/divine/avatar/recall`) for gods to create physical-realm avatars with divinity cost mechanics. Base cost scaled by recency of last avatar death -- spawning a replacement immediately is expensive, waiting is cheap. `DeityModel` tracks `activeAvatarCharacterId` and `lastAvatarDeathAt`. The API orchestrates Character creation, Relationship linking, Actor spawning, and watch registration. ABML behavior documents then define when and why a god chooses to manifest (domain relevance, narrative opportunity, follower need). See [Avatar Manifestation Pattern](#avatar-manifestation-pattern).
12. **Underworld System Realm**: A system realm for afterlife gameplay. Dead characters are transferred (realm transfer, not deletion) to the underworld realm, continue as actors running afterlife behaviors, and generate history/encounters that feed back into the living world's narrative generation. Per VISION.md's soul architecture (logos bundle + pneuma shell + sense of self), death is transformation. The underworld system realm makes this literal: the character persists in a different conceptual space.
13. **Guardian Spirit Characters (Nexius Realm)**: Guardian spirits as Character records in a Nexius system realm. Pair bonds from PLAYER-VISION.md become Relationships between Nexius characters. Spirit evolution becomes character growth via Seed. The same pattern as divine characters applied to the player's metaphysical entity. Would give the Agency service concrete character data to work with for manifest computation.

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `entityType` | A (Entity Reference) | `EntityType` enum (from `common-api.yaml`) | Identifies which first-class Bannou entity receives a blessing; all valid values (`character`, `account`, `deity`, etc.) are first-class Bannou entities |
| `status` (DeityResponse) | C (System State/Mode) | `DeityStatus` enum (`Active`, `Dormant`) | Deity lifecycle state machine position; system-managed lifecycle, not game content. `Archived` removed — deprecation triple-field replaces it (Category A per T31). |
| `tier` (BlessingResponse) | C (System State/Mode) | `BlessingTier` enum (`minor`, `standard`, `greater`, `supreme`) | Determines divinity cost, storage mechanism (Status vs Collection), and power level; service-specific classification |
| `status` (BlessingResponse) | C (System State/Mode) | `BlessingStatus` enum (`active`, `revoked`) | Current blessing lifecycle state |
| `domainCode` | B (Game Content Type) | Opaque string | Domain of divine influence (e.g., `"war"`, `"knowledge"`, `"nature"`); different games define different domains without schema changes |

**Notes**:
- `entityType` was recently migrated from plain string to the shared `EntityType` enum in `common-api.yaml`, confirming its Category A classification.
- `domainCode` follows the same extensibility pattern as seed type codes, collection type codes, and relationship type codes -- opaque strings that are game-configurable at deployment time.
- Follower management uses `characterId` directly (not `entityId` + `entityType` polymorphism) because only characters can be "watched" by gods in the attention system.

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(No active bugs)*

### Intentional Quirks (Documented Behavior)

1. **Entity-agnostic blessings**: Unlike the original plan which was character-specific, the implemented schema uses `entityId` + `entityType` polymorphism for blessings. Characters, accounts, deities, or any entity type can receive blessings, matching the entity-agnostic patterns of lib-collection and lib-status.

2. **Followers are character-only**: While blessings are entity-agnostic, followers are always characters (RegisterFollowerRequest takes `characterId`, not `entityId`+`entityType`). This is intentional -- only characters can be "watched" by gods in the attention system.

3. **Attention slot allocation is visible to callers**: RegisterFollower returns `attentionSlotAllocated: boolean` indicating whether the deity had capacity to actively watch this follower. Attention slots are a finite resource (`MaxAttentionSlots` on the deity); when full, new followers are registered but not actively monitored until the attention decay worker frees a slot.

4. **Domain codes are opaque strings, not enums**: Different games define different domains. Domain codes follow the same extensibility pattern as seed type codes, collection type codes, and relationship type codes -- extensible without schema changes.

4. **Dual-tier blessing storage**: Greater/Supreme blessings are permanent unlocks via lib-collection. Minor/Standard blessings are temporary status items via Status Inventory with contract-based lifecycle. The tier determines the storage mechanism, and lib-divine owns the `BlessingModel` record linking the two.

5. **Divinity is a shared currency type**: One "divinity" currency definition per game service, but wallets are per-entity. Gods AND humans can hold divinity -- it's used differently but is the same currency type. God-to-god divinity transfers work naturally as Currency transfers.

6. **Divine affectations excluded from lifecycle events**: `divineAffectations` (renamed from `personalityTraits`) is marked as sensitive in `x-lifecycle`, so it is excluded from `DeityCreatedEvent`, `DeityUpdatedEvent`, and `DeityDeletedEvent`. This prevents leaking internal simulation data through broadcast events. `DivineAffectations` (temperament, attentionBias, generosity, jealousy) is divine-specific behavioral config for god-actor ABML decisions — separate from Character Personality (`${personality.*}`) which provides emergent personality via the system realm character brain. Both exist and serve different purposes.

7. **~~Batched divinity generation~~**: **Removed (2026-03-16).** The analytics-based divinity generation pipeline (HandleAnalyticsScoreUpdated → DivinityEventModel Redis queue → DivineDivinityGenerationWorker) has been replaced by seed bond propagation. Divinity flows to gods through Seed bonds at L2 with no Divine involvement. The `divine-divinity-events` Redis store and `DivineDivinityGenerationWorker` are no longer needed. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md).

### Design Considerations (Requires Planning)

1. **~~Domain-to-analytics mapping~~**: **Dissolved (2026-03-16).** The analytics-based divinity generation pipeline has been replaced by seed bond propagation. Mortal domain activity flows to gods through Seed bonds at L2 — no Analytics dependency, no domain-to-analytics mapping needed. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md). #636 can be closed.
<!-- AUDIT:RESOLVED:2026-03-16:https://github.com/beyond-immersion/bannou-service/issues/636 -->

2. **~~No owner validation for blessings~~**: **Resolved (2026-03-16).** Skip entity existence validation for polymorphic `entityId + entityType` on `GrantBlessing`. This matches the established codebase pattern: all 6 L2 services that accept polymorphic entity references (Collection, Relationship, Seed, Currency, Status, Escrow) skip existence validation — callers are trusted services (`x-permissions: []`). Follower registration validates character existence because it uses a typed `characterId` (non-polymorphic), which is a different pattern. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md) Q5 and [#675](https://github.com/beyond-immersion/bannou-service/issues/675).
<!-- AUDIT:RESOLVED:2026-03-16:https://github.com/beyond-immersion/bannou-service/issues/675 -->

## Work Tracking

### Active
- **Batch lifecycle events** (2026-03-15): Switch to batch: true for high-frequency instance lifecycle events. Tracked via [#655](https://github.com/beyond-immersion/bannou-service/issues/655).

**L4 audit completed (2026-03-06)**:
- Schema: Custom events converted to flat structure (inline eventId/timestamp), `topic_prefix: divine` added for Pattern C lifecycle topics (`divine.deity.created/updated/deleted`)
- Code: Constructor rebuilt with all 9 hard dependencies, constructor-cached state stores, IEventConsumer wired, internal data models defined (5 models with proper C# types), event handler async/telemetry compliant, structured logging on all stubs
- Skeleton is now tenet-compliant; all 22 endpoints remain stubbed pending implementation per `docs/plans/DIVINE.md`
- External blockers: #383/#388 (Puppetmaster watcher-actor integration) needed for god-actors
- Upstream dependency: #415 (Status EntityType.Character hardcoding) blocks entity-agnostic blessings
