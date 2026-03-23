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

**Divine actors are both puppetmasters and gardeners**: A god tending a physical realm region (spawning encounters, adjusting NPC moods, orchestrating narrative opportunities) and a god tending a player's conceptual garden space (spawning POIs, managing scenario selection, guiding discovery) are the same operation from different perspectives -- two sides of the same coin. The divine actor launched via Puppetmaster as a regional watcher also serves as the gardener behavior actor for player experience orchestration via Gardener's APIs. Whether the "space" being tended is a physical location in the game world or an abstract conceptual space (a void garden, a lobby, player housing) is a behavioral distinction encoded in the god's ABML behavior document, not a structural difference in the actor type. This means: (1) the same god (e.g., Moira) that creates emergent content in the physical world also curates which experiences reach players through their gardens, directly connecting the content flywheel to the player experience; (2) any conceptual space can potentially become a physical space and vice versa, because the transition is just the god shifting focus between garden types; (3) lib-gardener provides the tools (garden instances, POIs, scenarios, entity associations), lib-puppetmaster provides the actor lifecycle, and lib-divine provides the identity and economy of the entity doing the tending.

**Dynamic character binding via lib-genesis**: Deity entities are created as genesis entities using the "deity_domain" template. Divine creates the deity's seed externally (via Seed API) and passes the seedId to Genesis via the nullable `seedId` parameter on `/genesis/entity/create` — this allows Divine to retain the seedId for seed bond operations while Genesis manages the lifecycle. [lib-genesis](GENESIS.md) (L2) handles the full Actor-Bound Entity lifecycle — wallet provisioning, actor spawning at Stirring phase, character creation and binding at Awakened phase. Divine subscribes to `genesis.entity.phase-changed` for divine-specific post-transition work (attention slot initialization, follower management setup, divinity economy activation). The ABML behavior document supports both pre- and post-binding states — before binding, `${personality.*}` expressions resolve to null and the god uses default decision paths; after binding, the god reasons with its full personality, history, and encounter memory. This means deity creation doesn't need to block on character provisioning, and the god can begin its duties immediately. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md) for the full external seed adoption pattern.

**Divinity generation via seed bond propagation**: Divinity is NOT generated from Analytics events. Instead, mortal domain activity flows to gods through Seed's bond propagation mechanism. Characters have domain seeds (e.g., "war", "craft") that can be bonded to a god's matching domain seed. When a character's seed grows (from wallet credits via Genesis growth mappings), the bond propagates a configurable ratio of that growth to the god's seed. This operates entirely at L2 with no Divine involvement — Seed handles propagation, Genesis handles phase transitions. Direct divinity (prayer, offerings) credits the god's wallet directly. The god-actor perceives accumulated resources via variable providers and makes autonomous GOAP decisions. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md) for the full architecture.

**Patron deity and auto-bonding**: Characters have a `patronDeityCode` field (opaque string, nullable) on the Character record (L2). Character Lifecycle (L4) provides generational inheritance — children inherit their parents' patron deity. When Divine detects a `patronDeityCode` change (via `character.updated` event), it looks up the deity code in a runtime bond template registry (built from deity actor seed data at startup) and auto-initiates seed bonds between the character's domain seeds and the patron god's matching seeds. Bond `PropagationDirection` and `PropagationRatio` are per-bond, configured in the deity's bond template — different gods offer different bond characteristics (e.g., a war-domain god: aggressive one-way `AToB`; a wisdom-domain god: balanced `Bidirectional`). Patron (god favors character) and follower (character favors god) are separate relationships that map to bond direction.

**Deity deprecation lifecycle (Category A)**: Deities are world-building definitions referenced by blessings and followers. Per T31, they support Category A deprecation: deprecate (with reason), undeprecate (gods can return), and merge (transfer followers/blessings to another deity). Merge auto-dissolves follower seed bonds to the deprecated deity and creates new bonds to the target deity per its bond template. Delete requires `IsDeprecated == true`. `IDeprecateAndMergeEntity` marker interface.

**Zero Arcadia-specific content**: lib-divine is a generic pantheon management service. Arcadia's 18 Old Gods are configured through behaviors and templates at deployment time, not baked into lib-divine.

**Domain codes are opaque strings**: Different games define different domains (War, Knowledge, Nature, etc.). Domain codes follow the same extensibility pattern as seed type codes, collection type codes, and relationship type codes.

### Divine Economy as Real Economy (Itemized Blessing Model)

Gods are characters in the PANTHEON realm. The PANTHEON is a realm. Every economic primitive — Currency, Item, Inventory, Craft, Workshop, Market, Trade, Escrow — already works in realms. The divine economy IS a real economy running on the same infrastructure as the mortal economy. Zero new services needed.

**The core pattern**: Divine operations (blessings, miracles, divine artifacts) are **items** — produced by Workshop/Craft from divinity currency, stored in the god's inventory, consumed via Contract prebound APIs that fire the actual effects. This replaces flat config-based cost properties with a living production economy.

**Faith → domain divinity → blessing items**:

```
Follower activity → seed bond propagation → god's faith wallet credited
    ↓
Currency exchange: raw faith → domain-specific divinity
  (war god: 100 faith → 80 war divinity + 20 general)
  (wisdom god: 100 faith → 30 war divinity + 70 knowledge)
    ↓
Workshop blueprints: domain divinity → blessing items (automated, lazy-evaluated)
  (10 war divinity per cycle → 1 Minor War Blessing item)
  (50 knowledge divinity per cycle → 1 Standard Knowledge Blessing item)
    ↓
Blessing items land in god's inventory (ready for distribution)
    ↓
God-actor ABML behavior decides: grant blessing (consume item → Contract fires → effect applied)
```

**Workshop lazy evaluation** means thousands of gods accumulate production without server ticks — blessings materialize on next query, just like factory output. The god-actor's GOAP planner evaluates "do I have blessings to give?" and "is this mortal worthy?" independently.

**Per-deity cost variation emerges from exchange rates and recipes**, not config properties:
- Different gods get different exchange rates for faith→divinity (a war god's war-faith is more efficient)
- Different Craft recipes per deity template define what each god can produce and at what cost
- DivineAffectations `generosity` drives the god's ABML spending behavior — how freely it distributes stockpiled blessings — not the production cost

**The Dungeon Core parallel**: This is architecturally identical to how dungeon cores work:

| Concern | Dungeon Core | God |
|---|---|---|
| Resource accumulation | Mana (Currency, autogain + harvested) | Faith (Currency, bond propagation + prayer) |
| Resource specialization | Mana types | Faith → domain divinities (Currency exchange) |
| Automated production | Workshop → creatures, traps | Workshop → blessings, miracles |
| Manual crafting | Craft → special items | Craft → unique blessings, divine artifacts |
| Inventory management | Trap/creature inventory | Blessing/miracle inventory |
| Strategic deployment | ABML decides placement | ABML decides who receives |
| Trading | Dungeon-to-dungeon exchange | God-to-god trading (Market/Trade/Escrow) |
| Cognitive progression | Genesis: Dormant → Stirring → Awakened | Genesis: Dormant → Stirring → Awakened |

Both follow the Genesis template pattern: entities that accumulate resources, produce items on cycles, deploy them strategically, and make autonomous decisions via ABML behaviors. The only difference is the realm they operate in and the semantic meaning of their products.

**Inter-deity economics** emerge naturally from Market/Trade:
- Gods can trade blessing items with each other
- A war god with excess war blessings trades with a wisdom god for knowledge blessings
- Market price discovery creates emergent scarcity and divine specialization
- Rival gods can manipulate the divine market
- Alliance gods get preferential trade terms (Relationship + Escrow)

**What this eliminates from Divine service**: The flat `DivinityCostMinor/Standard/Greater/Supreme` config properties are superseded. Costs are defined in Craft recipes and Workshop blueprints (game-configurable seed data). Divine service does not enforce costs — Currency/Craft/Workshop handle production economics. Divine remains a thin orchestration layer: deity CRUD, follower management, attention mechanics. The economy runs on existing primitives.

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
| ~~`DivinityCostMinor/Standard/Greater/Supreme`~~ | ~~`DIVINE_DIVINITY_COST_*`~~ | — | **Removed** — superseded by the itemized blessing model (§ Divine Economy as Real Economy). Blessing costs are defined in Craft recipes and Workshop blueprints, not flat config properties. |
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
│  Character fights → wallet credit → Genesis in-memory filter         │
│    → buffered → flush worker batches → Seed.RecordGrowthBatch        │
│    → character's "war" seed grows                                    │
│    → Seed bond propagation → god's "war" seed grows                  │
│    → Genesis ISeedEvolutionListener → god awakens progressively      │
│  Prayer → credits god's wallet directly → Genesis batched flush      │
│    → god's seed grows → god-actor perceives → decides autonomously   │
│                                                                      │
│  Note: Genesis batches wallet credits into periodic flush cycles     │
│  (default 5s) for Seed lock efficiency at 100K+ entity scale.       │
│  Phase transitions spanning hours/days are unaffected by the delay.  │
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

1. **Holy Magic Invocations**: A game implementation pattern, not a Divine service feature. Per the FAQ "Why Are There No Skill, Magic, Or Combat Plugins?", magic decomposes entirely into existing primitives: spell proficiency (Seed), spell unlocks (License/Collection), active effects (Status), NPC casting decisions (Actor + ABML). The "divine worthiness check" is a god-actor ABML behavior: the god perceives the invocation request, evaluates alignment and follower status via variable providers (`${personality.*}`, `${divine.*}`), and grants or denies via the perception system. No Divine API endpoints needed. Per-invocation Contracts at 100K NPC scale would generate millions of instances — Contract is designed for meaningful multi-party agreements, not per-action micro-approvals. **Exception**: Complex ritual magic (multi-participant, time-consuming, interruptible) IS a valid Contract use case, but that's a game-authored Contract template + ABML content orchestrated by the game engine or an L5 extension, not a Divine service responsibility.
2. **DanMachi Leveling Gate**: A game implementation pattern, not a Divine infrastructure feature. No `ILevelGateProviderFactory` needed — "leveling" in Bannou is a Seed capability that manifests in Status's unified effects layer (`SeedEffectsEnabled`), and divine blessings are Status effects (temporary) or Collection entries (permanent). Quest prerequisite templates check Status for required effects: "does this character have the rank/blessing/capability needed?" The DanMachi blessing-gated rank-up is just another Status prerequisite — the quest template says "requires Greater Blessing status from this deity's domain." Quest's existing `CHARACTER_LEVEL` prerequisite type should tie into Status as the unified query point for all character capabilities (seed-derived + item-based + contract-based), not route through a separate `IPrerequisiteProviderFactory`. The gate is a matter of quest template configuration and Status query, not Divine service code.
3. **God Personality Simulation & Choreographic Preferences**: Two distinct concerns bundled as one extension.
   - **Non-choreographic (attention patterns, jealousy, inter-deity politics)**: Pure ABML behavior authoring with no service code changes. The infrastructure is fully in place: DivineAffectations (temperament, attentionBias, generosity, jealousy) are configured behavioral parameters in the schema; `${personality.*}` provides emergent personality via the PANTHEON character brain; the [Behavioral Bootstrap](../guides/BEHAVIORAL-BOOTSTRAP.md) guide § "God Behavior Document Structure" and § "Puppetmaster God Behavior Template" describe exactly the patterns needed (domain perception filter, archive evaluation, blessing economy, NPC guidance). How `temperament` drives god decision-making, how `jealousy` triggers rivalry escalation, and how `attentionBias` shapes which events a god prioritizes are all ABML content design authored against the existing variable provider infrastructure.
   - **Choreographic (combat extension preferences)**: Deferred pending cinematic system implementation. Tracked via [#695](https://github.com/beyond-immersion/bannou-service/issues/695) (aspirational — all dependencies unimplemented). DivineAffectations-to-choreography mapping (temperament → aggressive extensions, attentionBias → narrative callbacks, generosity → redemptive saves) and FCFS competition dynamics are sound design. See [VIDEO-DIRECTOR.md](../planning/VIDEO-DIRECTOR.md), [COMPOSITIONAL-CINEMATICS.md § 4.5](../planning/COMPOSITIONAL-CINEMATICS.md#45-multi-producer-composition-fcfs-extension-competition), [#694](https://github.com/beyond-immersion/bannou-service/issues/694) / [#695](https://github.com/beyond-immersion/bannou-service/issues/695).
4. **Domain Contests**: Emergent from the bond propagation architecture (#712), not a service implementation task. A character's domain seed bonds to ONE god's matching seed (one bond per seed constraint). When two war gods exist, characters choose who to follow — the god with more followers accumulates more growth and becomes more powerful. Gods' ABML behaviors perceive each other's power levels via variable providers and react autonomously (grant extra blessings to retain followers, sabotage rivals, challenge for supremacy). Active contest mechanics are ABML behavioral content authored for god-actors. The infrastructure already supports this: Divine (follower tracking), Seed (competitive growth via bond ratios — different gods offer different bond terms), Relationship (`deity_rivalry` bonds per Extension #5), Actor (variable providers for perceiving rival power). See also Extension #5 (Deity-Deity Rivalries) which shares the same behavioral foundation.
5. **Deity-Deity Rivalries**: Pure ABML behavior authoring with no service code changes beyond Divine's own startup seed data registration. The `deity_rivalry` Relationship type is registered by Divine during `OnRunningAsync` via `IRelationshipClient.SeedRelationshipTypesAsync` (config property `RivalryRelationshipTypeCode`, already planned in Stub #10) — zero Relationship service changes needed. Behavioral consequences decompose into existing primitives: gods perceive rival state via variable providers (`${divine.*}` Extension #7), GOAP evaluation weighs jealousy axis from DivineAffectations, sabotage manifests as debuffs via Status or competitive bond propagation via Seed (#712), protection manifests as extra blessings via Divine grant API. Shares behavioral foundation with Extension #4 (Domain Contests) — rivalries ARE domain contests with a personal dimension. See also [Behavioral Bootstrap](../guides/BEHAVIORAL-BOOTSTRAP.md) § "Puppetmaster God Behavior Template" for the god-actor ABML structure these behaviors would follow.
6. **Client Events**: `divine-client-events.yaml` for pushing blessing notifications, divine attention alerts, and divinity milestones to connected WebSocket clients. **Note on duplication**: Most divine state changes that affect a character are already published as client events by the underlying L2 primitives — temporary blessings via Status (`status.effect.changed`), permanent blessings via Collection, and patron deity changes via Character (`character.updated` with `changedFields: [patronDeityCode]`). Divine client events provide divine-context enrichment (god name, domain, blessing flavor) that the primitive events lack, but the player-facing notification pipeline is Gardener/Agency's responsibility — it gates which events reach the player based on the UX capability manifest and transforms presentation fidelity per the spirit's progressive agency level. Divine client events exist as a richer signal source for that pipeline, not as the primary player notification channel. The broader pattern of how character-affecting L4 events reach player sessions via Gardener/Agency event transformation is a cross-cutting design concern not yet firmly established — when resolved, it will define the canonical relationship between L4 service client events, L2 primitive client events, and the Gardener/Agency presentation layer.
7. **Variable Provider Factory**: `IDivineVariableProviderFactory` for ABML behavior expressions exposing divine-specific data to non-deity actors. **Scope**: Gods as character brains in system realms get `${personality.*}`, `${encounters.*}`, `${backstory.*}` for free — the `${divine.*}` provider serves OTHER actors (NPCs, dungeon cores, guardian spirits) checking divine state. Concrete variable set: `${divine.patron_deity}` (patron deity code), `${divine.patron_deity.mood}` (maps to deity's personality traits via character brain), `${divine.blessing_count}` (active blessings for confidence weighting), `${divine.has_blessing.<code>}` (specific blessing check for risk assessment), `${divine.divinity_earned}` (one god checking another's power level). Follows the standard `IVariableProviderFactory` pattern per [Helpers § Variable Provider Cache](../reference/HELPERS-AND-COMMON-PATTERNS.md#5-variable-provider-cache).
8. **Economic Deity Behaviors**: Pure ABML behavior authoring with no service code changes. The [Economy System Guide § 5 (Divine Economic Intervention)](../guides/ECONOMY-SYSTEM.md#5-divine-economic-intervention) provides the complete design: five god typologies (Commerce, Thieves, Harvest, Fortune, Balance) with distinct intervention styles, ABML pseudocode showing velocity threshold evaluation, hoarding detection, and personality-modulated intervention costs (`subtlety`, `chaos_affinity`, `interventionFrequency`). Economic deities observe velocity through **Currency data directly** (`${currency.*}` variable providers, balance/transaction queries) — NOT through Analytics. Analytics is the most optional plugin (per [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md)) and cannot be in the critical path for game mechanics. Analytics may provide richer aggregate metrics when available, but the core intervention mechanism must work without it. Interventions manifest as narrative events (business opportunities, dropped wallets, thefts, treasure discoveries) that NPCs react to through their own GOAP evaluation — gods preserve NPC autonomy by creating conditions, not forcing behavior.
9. **Deity Realm Economic Assignment**: Already architecturally covered by existing infrastructure — no new service endpoints or tracking mechanisms needed. Which realms each deity watches is handled by the Puppetmaster Manager's Phase 4 regional assignment ([Behavioral Bootstrap](../guides/BEHAVIORAL-BOOTSTRAP.md) § Phase 4: "for each realm... if deity.domains overlap realm.active_domains: spawn actor"). Per-deity personality parameters (intervention frequency, subtlety, favored targets, chaos affinity) are DivineAffectations + `${personality.*}` via the PANTHEON character brain. Multiple gods per realm is inherent in the architecture — any god whose domain overlaps the realm gets an actor there, and competing intervention styles emerge from different gods' ABML behaviors running simultaneously. Per-realm stagnation policies (whether a dead town should stay dead) are ABML behavioral decisions per god, not service configuration — a Nature god might actively prevent stagnation in forests but allow it in deserts, all encoded in the god's behavior document.
10. **Housing Garden Divine Behaviors**: Pure ABML behavior authoring with no Divine service changes. The [Gardener deep dive](GARDENER.md) is the authoritative design owner for housing gardens — its [Housing Garden Pattern](GARDENER.md#housing-garden-pattern-no-plugin-required) decomposes every housing concern into existing primitives, and its [Gardener Behavior Actor Pattern](GARDENER.md#gardener-behavior-actor-pattern-divine-actor-unification) explicitly states that tending a housing garden and tending a physical realm are "the same operation with different tools" from the divine actor's perspective. Housing ABML behavior design (item placement, voxel permissions, visitor management, item↔scene binding) is tracked in Gardener's Work Tracking as Design #5, with housing as the first garden type validation target (Stub #13) and the divine actor gardener pattern as Stub #11. Divine provides the god's identity and economy; Gardener provides the housing APIs; the ABML behavior connects them. See also [VOXEL-BUILDER-SDK.md](../planning/VOXEL-BUILDER-SDK.md).
11. **Avatar Manifestation API + Behaviors**: Deferred per Q7 in [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md) — core Divine ships first; avatar endpoints are purely additive. Detailed 9-step orchestration design in [Avatar Manifestation Pattern](#avatar-manifestation-pattern) above. Divine API endpoints (`/divine/avatar/manifest`, `/divine/avatar/on-death`, `/divine/avatar/recall`) orchestrate Character creation, Relationship linking, Actor spawning, and watch registration with divinity cost mechanics (base cost scaled by recency of last avatar death). `DeityModel` fields `activeAvatarCharacterId` and `lastAvatarDeathAt` added when implemented (additive schema change). **Note**: Avatars are NOT Genesis entities — they're created fully formed via direct `ICharacterClient` + `IActorClient` calls, bypassing the dormant→stirring→awakened progressive lifecycle. Avatars are divine manifestations at full cognitive capacity from creation. The deity itself uses Genesis; its avatars don't. ABML behavior documents define when and why a god manifests. See also [Behavioral Bootstrap § Avatar Manifestation in the Flywheel](../guides/BEHAVIORAL-BOOTSTRAP.md#avatar-manifestation-in-the-flywheel).
12. **Underworld System Realm**: A system realm (`isSystemType: true`) for afterlife gameplay, configured via `/realm/seed`. Dead characters are transferred (realm transfer via Character API, not deletion) to the UNDERWORLD realm with a `deceased` flag — they still exist as character records but are now "out-of-system" for normal character/actor management. What happens to them in the underworld is a **game implementation detail**, not a Bannou-native concern: the game server decides whether actors stop on death (most NPCs — their data lingers to enrich the content flywheel) or continue running afterlife behaviors (player characters, narratively significant NPCs). Both options are already supported through existing infrastructure — divine actor behaviors orchestrate the experience, the realm transfer endpoint moves the character, and Actor's spawn/stop APIs manage the actor lifecycle. Per VISION.md's soul architecture and [DEATH-AND-PLOT-ARMOR.md](../planning/DEATH-AND-PLOT-ARMOR.md), death is transformation: aspiration-based pathway selection (Valhalla/Purgatory/peaceful dissipation), soul currency economy, and the Orpheus Journey are all game-authored content using existing primitives. **Scale**: No significant concern if most NPC actors stop on death — only their character data persists in the underworld realm. Player characters and narratively important NPCs running afterlife actors are a small fraction. Character Lifecycle ([Extension #1](CHARACTER-LIFECYCLE.md#potential-extensions)) owns the death processing pipeline and afterlife pathway data.
13. **Divine Private Spatial Infrastructure & CalendarEvent Self-Notification**: Gods in the PANTHEON system realm (or in per-god private realms like `MOIRA_SANCTUM`) have their own Location hierarchy — private locations where they are the sole watcher. The unified CalendarEvent system ([#538](https://github.com/beyond-immersion/bannou-service/issues/538)) enables durable self-notification: a god registers a `Once` CalendarEvent scoped to its private location as a narrative checkpoint ("return to this storyline in 3 game-months"). The clock worker fires `worldstate.calendar-event.reached` to that location; only the owning god perceives it. Combined with Scene (personal workspace), Asset (divine artifacts), Seed (domain growth), and Gardener (experience orchestration), each god has an isolated personal world built from standard L2 primitives. Per-god private realms in distributed deployment (`DEPLOYMENT-MODES` Phase 2) achieve near-complete isolation on separate nodes. **Divine wrapping**: `POST /divine/reminder/create` wraps `IWorldstateClient` CalendarEvent creation with divinity cost (Currency debit) and domain authority validation — the L2 primitive is generic and free; the L4 wrapper adds game-mechanical constraints. This parallels Quest wrapping Contract and Craft wrapping Item+Inventory+Currency.
<!-- AUDIT:CONFIRMED:2026-03-23:https://github.com/beyond-immersion/bannou-service/issues/538 — CalendarEvent design fully resolved in #538 (2026-03-20); Divine wrapper follows established L4-over-L2 wrapping pattern; per-god private realms deferred to DEPLOYMENT-MODES Phase 2 -->

14. **Guardian Spirit Characters (Nexius Realm)**: Guardian spirits as Character records in a NEXIUS system realm, following the same Genesis-driven progressive awakening pattern as deities in PANTHEON. The spirit's seed would be pre-paired and fed into Genesis via the external seed adoption pattern (#714), so the spirit **grows from a simpler management entity into a full character over time** — Dormant (account-level seed tracking only) → Stirring (event brain actor, basic spirit behaviors) → Awakened (Character in NEXIUS realm, full personality, memories, autonomy). This makes the guardian spirit an autonomous entity in its own right: `${personality.*}` gives the spirit its own evolving personality, `${encounters.*}` gives it memories of characters it's possessed, Character History gives it a narrative arc across generations, and Relationships give pair bonds from [PLAYER-VISION.md](../reference/PLAYER-VISION.md) concrete implementation (twin spirits as linked NEXIUS characters). At full awakening, the spirit could develop opinions about its characters, resist player choices conflicting with its personality, and progressively increase its independence — directly implementing PLAYER-VISION.md's "agency is earned context" gradient. Architecturally identical to the PANTHEON pattern; all infrastructure exists. Gated by Agency (aspirational — no schema or code) and Disposition (aspirational) which would consume the spirit's character data for the UX capability manifest and compliance computations. See [Agency deep dive](AGENCY.md) § `${spirit.*}` Variable Provider.

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

1. ~~**Stale `analytics.score.updated` event subscription**~~: **FIXED** (2026-03-23) - Removed the `analytics.score.updated` event registration and `HandleAnalyticsScoreUpdatedAsync` handler method from `DivineService.Events.cs`. The analytics-based divinity generation pipeline was replaced by seed bond propagation; the schema already reflected this but the code had not been cleaned up.

2. ~~**`DivinityEventModel` is dead code**~~: **FIXED** (2026-03-23) - Removed the `DivinityEventModel` internal class from `DivineService.Models.cs`. The analytics-based divinity generation pipeline it served was replaced by seed bond propagation; the Redis store (`divine-divinity-events`) was already removed from `state-stores.yaml`.

3. ~~**Stale XML documentation on event handlers**~~: **FIXED** (2026-03-23) - Updated XML docs and TODO comments on `HandleCharacterCreatedAsync` and `HandleCharacterUpdatedAsync` in `DivineService.Events.cs` to match the patronDeityCode-based auto-bonding design specified in the deep dive and implementation map.

### Intentional Quirks (Documented Behavior)

1. **Entity-agnostic blessings**: Unlike the original plan which was character-specific, the implemented schema uses `entityId` + `entityType` polymorphism for blessings. Characters, accounts, deities, or any entity type can receive blessings, matching the entity-agnostic patterns of lib-collection and lib-status.

2. **Followers are character-only**: While blessings are entity-agnostic, followers are always characters (RegisterFollowerRequest takes `characterId`, not `entityId`+`entityType`). This is intentional -- only characters can be "watched" by gods in the attention system.

3. **Attention slot allocation is visible to callers**: RegisterFollower returns `attentionSlotAllocated: boolean` indicating whether the deity had capacity to actively watch this follower. Attention slots are a finite resource (`MaxAttentionSlots` on the deity); when full, new followers are registered but not actively monitored until the attention decay worker frees a slot.

4. **Domain codes are opaque strings, not enums**: Different games define different domains. Domain codes follow the same extensibility pattern as seed type codes, collection type codes, and relationship type codes -- extensible without schema changes.

5. **Dual-tier blessing storage**: Greater/Supreme blessings are permanent unlocks via lib-collection. Minor/Standard blessings are temporary status items via Status Inventory with contract-based lifecycle. The tier determines the storage mechanism, and lib-divine owns the `BlessingModel` record linking the two.

6. **Divinity is a shared currency type**: One "divinity" currency definition per game service, but wallets are per-entity. Gods AND humans can hold divinity -- it's used differently but is the same currency type. God-to-god divinity transfers work naturally as Currency transfers.

7. **Divine affectations excluded from lifecycle events**: `divineAffectations` (renamed from `personalityTraits`) is marked as sensitive in `x-lifecycle`, so it is excluded from `DeityCreatedEvent`, `DeityUpdatedEvent`, and `DeityDeletedEvent`. This prevents leaking internal simulation data through broadcast events. `DivineAffectations` (temperament, attentionBias, generosity, jealousy) is divine-specific behavioral config for god-actor ABML decisions — separate from Character Personality (`${personality.*}`) which provides emergent personality via the system realm character brain. Both exist and serve different purposes.

8. **~~Batched divinity generation~~**: **Removed (2026-03-16).** The analytics-based divinity generation pipeline has been replaced by seed bond propagation. Divinity flows to gods through Seed bonds at L2 with no Divine involvement. The `divine-divinity-events` Redis store and `DivineDivinityGenerationWorker` are no longer needed. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md). **Note**: All analytics pipeline code remnants fully removed (Bug #1 handler FIXED, Bug #2 model class FIXED, both 2026-03-23).

### Design Considerations (Requires Planning)

1. **~~Domain-to-analytics mapping~~**: **Dissolved (2026-03-16).** The analytics-based divinity generation pipeline has been replaced by seed bond propagation. Mortal domain activity flows to gods through Seed bonds at L2 — no Analytics dependency, no domain-to-analytics mapping needed. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md). #636 can be closed.
<!-- AUDIT:RESOLVED:2026-03-16:https://github.com/beyond-immersion/bannou-service/issues/636 -->

2. **~~No owner validation for blessings~~**: **Resolved (2026-03-16).** Skip entity existence validation for polymorphic `entityId + entityType` on `GrantBlessing`. This matches the established codebase pattern: all 6 L2 services that accept polymorphic entity references (Collection, Relationship, Seed, Currency, Status, Escrow) skip existence validation — callers are trusted services (`x-permissions: []`). Follower registration validates character existence because it uses a typed `characterId` (non-polymorphic), which is a different pattern. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md) Q5 and [#675](https://github.com/beyond-immersion/bannou-service/issues/675).
<!-- AUDIT:RESOLVED:2026-03-16:https://github.com/beyond-immersion/bannou-service/issues/675 -->

## Work Tracking

### Active
- **Batch lifecycle events** (2026-03-15): Switch to `batch: true` for high-frequency blessing instance lifecycle events. Tracked via [#655](https://github.com/beyond-immersion/bannou-service/issues/655). **Implementation-ready** — all design decisions resolved (Q4 in DIVINITY-GENERATION-ARCHITECTURE), follows BATCH-LIFECYCLE-EVENTS.md pattern, reference implementation exists (`lib-item/Services/ItemInstanceEventBatcher.cs`). Mode 1 (accumulating) — each grant is a unique event.

### Prerequisites (Cross-Service)

The divinity generation architecture ([DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md)) identifies 4 independent parallel tracks of prerequisites:

| Track | Issues | Gates |
|-------|--------|-------|
| **Seed** | [#712](https://github.com/beyond-immersion/bannou-service/issues/712) (propagation), [#713](https://github.com/beyond-immersion/bannou-service/issues/713) (batch events), [#362](https://github.com/beyond-immersion/bannou-service/issues/362) (dissolution) | Divinity generation via bond propagation, patron deity changes |
| **Character** | [#715](https://github.com/beyond-immersion/bannou-service/issues/715) (patronDeityCode), [#716](https://github.com/beyond-immersion/bannou-service/issues/716) (generational inheritance) | Patron deity auto-bonding |
| **Genesis** | [#714](https://github.com/beyond-immersion/bannou-service/issues/714) (external seedId) | Deity entity creation with bond capability |
| **Status** | ~~[#415](https://github.com/beyond-immersion/bannou-service/issues/415)~~ (EntityType hardcoding — **FIXED** 2026-03-07, CLOSED) | Entity-agnostic Minor/Standard blessings — **unblocked** |

**Divine core CRUD** (deity create/get/list/update/delete, deprecation lifecycle) can start immediately — no prerequisites block basic deity management.

### Completed
**L4 audit completed (2026-03-06)**:
- Schema: Custom events converted to flat structure (inline eventId/timestamp), `topic_prefix: divine` added for Pattern C lifecycle topics (`divine.deity.created/updated/deleted`)
- Code: Constructor rebuilt with all 9 hard dependencies, constructor-cached state stores, IEventConsumer wired, internal data models defined (5 models with proper C# types), event handler async/telemetry compliant, structured logging on all stubs
- Skeleton is now tenet-compliant; all 22 endpoints remain stubbed pending implementation per `docs/plans/DIVINE.md`
- External blockers: #383/#388 (Puppetmaster watcher-actor integration) needed for god-actors

**Bug fixes (2026-03-23)**: All 3 bugs fixed — stale analytics event subscription removed, dead DivinityEventModel removed, stale XML docs updated.
