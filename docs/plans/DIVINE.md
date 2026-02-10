# Implementation Plan: Divine Service (L4 Game Features)

> **Source**: Synthesized from `~/repos/arcadia-kb/Claude/systems-mining-2026-02-10/` (5 agent reports + synthesis)
> **Related**: `docs/plugins/PUPPETMASTER.md`, `docs/plugins/ACTOR.md`, `docs/reference/SERVICE-HIERARCHY.md`
> **Prerequisites**: lib-seed, lib-currency, lib-item, lib-inventory, lib-contract, lib-relationship, lib-actor, lib-puppetmaster must be operational
> **Status**: DRAFT -- Open questions require human decision before implementation

---

## Open Questions Requiring Human Decision

These questions must be answered before this plan can be finalized and executed. Each is marked with its impact on the plan.

### Q1: God Entity Ownership -- Who Owns the God Record?

**Context**: Gods are persistent entities with names, domains, personalities, rivalries, follower counts, and divinity pools. The KB describes them as both world-lore entities and active event-monitoring actors.

**Options**:
- **(A) lib-divine owns god entities directly** -- Gods are DeityModel records in divine's own state store. Each god also has an associated Actor (event brain type) for behavior, but the canonical identity lives in lib-divine. This is the Quest pattern: Quest owns quest definitions, Contract owns the machinery.
- **(B) Gods are Characters** -- Gods are special Character records (species: "deity"?) with lib-divine providing the overlay. This reuses Character's CRUD but couples divine to character semantics.
- **(C) Gods are Actors only** -- No separate entity; the Actor record IS the god. lib-divine provides API sugar over Actor management.

**Recommendation**: **(A)** -- Gods are fundamentally different from characters (they don't have physical bodies, don't belong to a realm in the same way, have domain power, accumulate divinity). lib-divine owns the DeityModel; each deity has an associated actorId linking to its Actor brain.

**Impact**: Determines the primary state store model, CRUD endpoints, and lifecycle events.

### Q2: Relationship to Puppetmaster Regional Watchers

**Context**: VISION.md states "Regional Watchers are the game-system implementation of gods." Puppetmaster already manages regional watcher lifecycle (spawn/stop), resource snapshot caching, and dynamic behavior loading.

**Options**:
- **(A) lib-divine creates watchers via Puppetmaster** -- When a deity is created/activated for a realm, lib-divine calls Puppetmaster to spawn a regional watcher with the deity's ABML behavior document. Puppetmaster owns the Actor lifecycle; lib-divine owns the deity semantics.
- **(B) lib-divine manages its own watcher lifecycle** -- lib-divine directly creates Actor entities and manages their behavior, bypassing Puppetmaster. This duplicates Puppetmaster's watcher management.
- **(C) Puppetmaster owns everything, lib-divine is unnecessary** -- Regional watchers ARE gods; Puppetmaster handles all divine logic through ABML behaviors and action handlers.

**Recommendation**: **(A)** -- Clean separation. lib-divine owns "what a god is" (domain, divinity, blessings, followers). Puppetmaster owns "how a god acts" (watcher lifecycle, behavior execution, event monitoring). lib-divine calls Puppetmaster to spawn/stop watchers, and registers custom ABML action handlers (e.g., `grant_blessing`, `spend_divinity`) that the deity's behavior document can invoke.

**Impact**: Determines whether lib-divine needs Puppetmaster as a soft dependency or if it manages Actors directly.

### Q3: DanMachi Blessing Gate -- Is Leveling Hard-Gated?

**Context**: The KB (agent1, Intentional Inequality doc) states "Characters cannot level up without receiving a divine blessing from a deity NPC." This is a DanMachi-inspired mechanic where Greater Blessings = rank increases (exponential divinity cost).

**Options**:
- **(A) Hard gate** -- Character level-up requires an active Greater Blessing. Character service needs to check blessing status before allowing level increase. This requires the Variable Provider Factory pattern (Character defines `ILevelGateProviderFactory`, lib-divine implements it) since Character (L2) cannot depend on Divine (L4).
- **(B) Soft gate** -- Blessings provide bonus XP multipliers and unlock advanced abilities, but characters can technically level without them (just much slower). No L2 integration needed.
- **(C) Deferred** -- Start without the leveling gate. Add it later when character leveling is actually implemented (Character service currently has no leveling mechanic).

**Recommendation**: **(C)** for MVP, with the Variable Provider Factory pattern documented as the future integration path. Character leveling isn't implemented yet, so building the gate now would have nothing to gate.

**Impact**: Determines whether to define `ILevelGateProviderFactory` in bannou-service/ and implement it in lib-divine.

### Q4: The 18 Gods -- Hardcoded or Game-Scoped?

**Context**: The KB describes 18 specific Old Gods for Arcadia (Mnemosyne, Nexius, etc.) with detailed domains and relationships. But Bannou is a platform that supports multiple game services.

**Options**:
- **(A) Game-service-scoped, Arcadia seeds 18** -- DeityModel includes a gameServiceId. The 18 Arcadia gods are seeded via configuration or API. Other games define their own pantheons.
- **(B) Global, 18 fixed** -- The 18 gods are universal across all game services.

**Recommendation**: **(A)** -- Consistent with every other L2/L4 entity (items, species, realms, currencies are all game-service-scoped). Arcadia's 18 gods are seeded as initial data.

**Impact**: Minor -- adds gameServiceId to DeityModel and query filters.

### Q5: Divinity as Currency -- Shared or Dedicated Pool?

**Context**: Each god accumulates "divinity" when mortals perform actions in their domain. Gods spend divinity on blessings. The synthesis identifies Currency as the direct mapping.

**Options**:
- **(A) Each god has a Currency wallet** -- lib-divine creates a currency definition per god (e.g., "divinity_mnemosyne") and a wallet. Divinity credits/debits go through lib-currency, getting transaction history, holds, and idempotency for free.
- **(B) Divinity is a float field on DeityModel** -- Simple, no external dependency, but loses Currency's distributed lock safety, transaction history, and hold mechanics.
- **(C) Shared "divinity" currency, per-god wallets** -- One currency type ("divinity"), each god gets a wallet. Simpler currency management but all gods share the same currency definition.

**Recommendation**: **(C)** -- One "divinity" currency definition per game service, with a wallet per deity. This gives full Currency primitives (credit, debit, hold, transfer, history) while keeping currency management simple. God-to-god divinity transfers (e.g., domain disputes) work naturally as Currency transfers.

**Impact**: Determines whether lib-divine creates N currency definitions or 1 per game service.

### Q6: Blessing Representation -- Items or Custom Model?

**Context**: Blessings are tangible things characters receive from gods. The synthesis maps them to Items in a character's inventory.

**Options**:
- **(A) Blessings are Items in a dedicated "blessing" container** -- lib-divine creates a BlessingTemplate as an Item template (category: "blessing", with stats representing the blessing effects). Characters get a special "divine_blessings" inventory container. Granting a blessing = creating an item instance and placing it in the container.
- **(B) Blessings are a custom BlessingModel in lib-divine's own state store** -- Full control over blessing semantics but duplicates Item/Inventory's management patterns.
- **(C) Hybrid: BlessingModel in lib-divine, backed by Item instances** -- lib-divine owns the semantic record (which god, blessing tier, granted date), and creates a corresponding Item instance for game-mechanical effects (stats, buffs).

**Recommendation**: **(A)** -- Pure composability. Blessings ARE items, just a special category. This means blessings automatically work with:
  - Inventory queries ("show me all my blessings")
  - Item stats ("this blessing grants +10 wisdom")
  - Escrow (blessings could theoretically be traded if game design allows)
  - Save/Load (blessing state persists automatically)
  - The only divine-specific logic is the granting/revoking ceremony, which lib-divine orchestrates.

**Impact**: Determines primary blessing storage and query patterns.

### Q7: MVP Scope

**Context**: The full divine system encompasses deity management, divinity economy, blessing orchestration, holy magic invocations, domain attention mechanics, god personality/rivalry simulation, follower tracking, and leveling gates. That's a large surface area.

**Options**:
- **(A) Full system** -- All of the above in one implementation pass.
- **(B) Deity + Divinity + Blessings** -- Core identity, economy, and primary output. No holy magic, no leveling gate, no god personality simulation (behavior docs handle that). ~15 endpoints.
- **(C) Deity + Blessings only** -- Even smaller. Divinity tracked as a simple field, not through Currency. ~10 endpoints.

**Recommendation**: **(B)** -- Deity management, divinity economy via Currency, and blessing orchestration via Item/Inventory. This covers the core "gods exist, earn divinity, grant blessings" loop. Holy magic invocations (via Contract) and advanced features come in a follow-up. God personality and attention patterns are ABML behavior concerns, not lib-divine API concerns.

**Impact**: Determines endpoint count, event count, and implementation scope below.

---

## Context

The Divine service is a new L4 Game Features plugin that manages the pantheon -- deity entities, divinity economy, and blessing orchestration. It is a thin orchestration layer (like Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver divine game mechanics.

**The composability thesis** (from systems mining synthesis):
- **God identity** -> DeityModel in lib-divine (owns the entity)
- **God behavior** -> Actor (event brain type) managed via Puppetmaster (owns the how)
- **God domain power** -> Seed (progressive growth of a deity's influence)
- **Divinity resource** -> Currency (divine energy pool per god)
- **Blessings** -> Item + Inventory (tangible effects granted to characters)
- **Holy magic invocations** -> Contract (micro-agreements between deity and caster) [DEFERRED to follow-up]
- **God-follower bonds** -> Relationship (deity-character)
- **God-god rivalries** -> Relationship (deity-deity)

**Why L4**: Divine depends on Currency, Item, Inventory (L2), and Puppetmaster, Behavior (L4). It is optional -- games without divine systems work fine. It orchestrates content from multiple layers. Classic L4 -- optional, feature-rich, maximum connectivity.

**Why not just composable primitives?** The synthesis concluded: "Seed (domain power) + Currency (divinity) + Contract (blessings) gets close but may not be enough." The unique domain logic lib-divine owns:
1. **Deity-as-entity CRUD** -- gods aren't characters, items, or seeds. They have domains, attention capacity, and follower mechanics.
2. **Blessing orchestration ceremony** -- the multi-step flow of a god noticing a character, evaluating worthiness, spending divinity, and granting a blessing as an item. No existing primitive handles this workflow.
3. **Divinity generation rules** -- mapping mortal actions in a domain to divinity credit for the domain's god. This is divine-specific event routing logic.
4. **Attention economy** -- gods have limited attention capacity and personality-driven patterns for which characters they notice. This feeds into Actor behavior but the attention tracking lives in lib-divine.

**Design principle**: Puppetmaster orchestrates what NPCs experience. Gardener orchestrates what players experience. **Divine orchestrates what gods experience.** Each is a thin L4 layer over shared primitives, with its own domain-specific semantics.

---

## Implementation Steps

*Note: This plan assumes Q1=A, Q2=A, Q3=C, Q4=A, Q5=C, Q6=A, Q7=B as the recommended answers. Adjust if decisions differ.*

### Step 1: Create Schema Files

#### 1a. `schemas/divine-api.yaml`

- **Header**: `x-service-layer: GameFeatures`, `servers: [{ url: http://localhost:5012 }]`
- All properties have `description` fields (CS1591 compliance)
- NRT compliance: optional ref types have `nullable: true`, required fields in `required` arrays

**~18 POST endpoints** across four API groups:

| Group | Endpoints | Permissions |
|-------|-----------|-------------|
| Deity Management (7) | create, get, get-by-code, list, update, activate, deactivate | `developer` |
| Divinity Economy (4) | get-balance, credit, debit, get-history | `developer` |
| Blessing Orchestration (5) | grant, revoke, list-by-character, list-by-deity, get | `developer` |
| Follower Management (2) | register-follower, get-followers | `developer` |

**Enums** (defined in `components/schemas`):
- `DeityStatus`: Active, Dormant, Archived
- `BlessingTier`: Minor, Standard, Greater, Supreme
- `DomainCategory`: War, Knowledge, Nature, Commerce, Death, Life, Magic, Craft, Sea, Sky, Earth, Fire, Time, Fate, Chaos, Order, Love, Trickery
- `AttentionPriority`: Low, Medium, High, Critical

**Shared sub-types** (defined in `components/schemas`):
- `DomainInfluence` -- domain (DomainCategory) + weight (float) pair for primary/secondary domains
- `DeityPersonalityTraits` -- temperament (string), attentionBias (string), generosity (float 0-1), jealousy (float 0-1)
- `BlessingSummary` -- blessingId (uuid), deityId (uuid), characterId (uuid), tier (BlessingTier), itemInstanceId (uuid), grantedAt (date-time)

**Request models** (one per endpoint):
- `CreateDeityRequest`: gameServiceId (uuid), code (string), displayName (string), description (string), domains (array DomainInfluence, min 1), personalityTraits (DeityPersonalityTraits), maxAttentionSlots (int, default 10), realmId (uuid, nullable -- home realm)
- `GetDeityRequest`: deityId (uuid)
- `GetDeityByCodeRequest`: gameServiceId (uuid), code (string)
- `ListDeitiesRequest`: gameServiceId (uuid), domainCategory (nullable DomainCategory), status (nullable DeityStatus), page (int, default 1), pageSize (int, default 50)
- `UpdateDeityRequest`: deityId (uuid), displayName (nullable string), description (nullable string), domains (nullable array DomainInfluence), personalityTraits (nullable DeityPersonalityTraits), maxAttentionSlots (nullable int)
- `ActivateDeityRequest`: deityId (uuid)
- `DeactivateDeityRequest`: deityId (uuid)
- `GetDivinityBalanceRequest`: deityId (uuid)
- `CreditDivinityRequest`: deityId (uuid), amount (float), source (string), sourceEventId (uuid, nullable), description (string)
- `DebitDivinityRequest`: deityId (uuid), amount (float), purpose (string), targetCharacterId (uuid, nullable), description (string)
- `GetDivinityHistoryRequest`: deityId (uuid), page (int, default 1), pageSize (int, default 50)
- `GrantBlessingRequest`: deityId (uuid), characterId (uuid), tier (BlessingTier), itemTemplateCode (string), reason (string)
- `RevokeBlessingRequest`: blessingId (uuid), reason (string)
- `ListBlessingsByCharacterRequest`: characterId (uuid), page (int, default 1), pageSize (int, default 50)
- `ListBlessingsByDeityRequest`: deityId (uuid), tier (nullable BlessingTier), page (int, default 1), pageSize (int, default 50)
- `GetBlessingRequest`: blessingId (uuid)
- `RegisterFollowerRequest`: deityId (uuid), characterId (uuid)
- `GetFollowersRequest`: deityId (uuid), page (int, default 1), pageSize (int, default 50)

**Response models**:
- `DeityResponse`: deityId, gameServiceId, code, displayName, description, domains, personalityTraits, maxAttentionSlots, actorId (nullable uuid), seedId (nullable uuid), currencyWalletId (nullable uuid), realmId (nullable uuid), status, followerCount (int), createdAt, updatedAt
- `ListDeitiesResponse`: deities (array DeityResponse), totalCount, page, pageSize
- `DivinityBalanceResponse`: deityId (uuid), balance (float), currencyCode (string), walletId (uuid)
- `DivinityHistoryResponse`: transactions (array -- reuse Currency's transaction format), totalCount, page, pageSize
- `BlessingResponse`: blessingId, deityId, characterId, tier, itemTemplateCode, itemInstanceId, reason, grantedAt, revokedAt (nullable), status (Active/Revoked)
- `ListBlessingsResponse`: blessings (array BlessingSummary), totalCount, page, pageSize
- `FollowerResponse`: characterId (uuid), deityId (uuid), registeredAt (date-time), relationshipId (uuid)
- `ListFollowersResponse`: followers (array FollowerResponse), totalCount, page, pageSize

#### 1b. `schemas/divine-events.yaml`

**x-lifecycle** for `Deity` entity (generates created/updated/deleted events):
- Model fields: deityId (primary), gameServiceId, code, displayName, description, status, followerCount, createdAt, updatedAt
- Sensitive: personalityTraits (exclude -- internal simulation data)

**x-event-subscriptions** (consumed events):
- `character.deleted` -> `CharacterDeletedEvent` -> `HandleCharacterDeleted` -- clean up follower relationships and revoke blessings for deleted character
- `analytics.score.updated` -> `AnalyticsScoreUpdatedEvent` -> `HandleAnalyticsScoreUpdated` -- detect domain-relevant achievements for divinity generation [SOFT: Analytics is L4]

**x-event-publications** (published events):
- Lifecycle events from x-lifecycle: `deity.created`, `deity.updated`, `deity.deleted`
- Custom events:
  - `divine.blessing.granted` -> `DivineBlessingGrantedEvent` -- a god granted a blessing to a character
  - `divine.blessing.revoked` -> `DivineBlessingRevokedEvent` -- a blessing was revoked
  - `divine.divinity.credited` -> `DivineDivinityCreditedEvent` -- divinity was earned (mortal action in domain)
  - `divine.divinity.debited` -> `DivineDivinityDebitedEvent` -- divinity was spent (blessing, miracle, etc.)
  - `divine.follower.registered` -> `DivineFollowerRegisteredEvent` -- character became a follower
  - `divine.follower.removed` -> `DivineFollowerRemovedEvent` -- character removed as follower
  - `divine.deity.activated` -> `DivineDeityActivatedEvent` -- deity became active in the world
  - `divine.deity.dormant` -> `DivineDeityDormantEvent` -- deity went dormant

**Custom event schemas** (in `components/schemas`):

```yaml
DivineBlessingGrantedEvent:
  type: object
  required: [eventId, deityId, characterId, blessingId, tier, itemInstanceId]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    deityId: { type: string, format: uuid, description: The deity granting the blessing }
    characterId: { type: string, format: uuid, description: The character receiving the blessing }
    blessingId: { type: string, format: uuid, description: The blessing record identifier }
    tier: { $ref: 'divine-api.yaml#/components/schemas/BlessingTier' }
    itemInstanceId: { type: string, format: uuid, description: The item instance created for this blessing }

DivineBlessingRevokedEvent:
  type: object
  required: [eventId, blessingId, deityId, characterId, reason]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    blessingId: { type: string, format: uuid, description: The revoked blessing }
    deityId: { type: string, format: uuid, description: The deity revoking the blessing }
    characterId: { type: string, format: uuid, description: The character losing the blessing }
    reason: { type: string, description: Why the blessing was revoked }

DivineDivinityCreditedEvent:
  type: object
  required: [eventId, deityId, amount, source]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    deityId: { type: string, format: uuid, description: The deity receiving divinity }
    amount: { type: number, format: float, description: Amount of divinity credited }
    source: { type: string, description: Source of the divinity gain }
    sourceEventId: { type: string, format: uuid, nullable: true, description: The triggering event if applicable }

DivineDivinityDebitedEvent:
  type: object
  required: [eventId, deityId, amount, purpose]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    deityId: { type: string, format: uuid, description: The deity spending divinity }
    amount: { type: number, format: float, description: Amount of divinity spent }
    purpose: { type: string, description: What the divinity was spent on }
    targetCharacterId: { type: string, format: uuid, nullable: true, description: Target character if blessing-related }

DivineFollowerRegisteredEvent:
  type: object
  required: [eventId, deityId, characterId, relationshipId]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    deityId: { type: string, format: uuid, description: The deity gaining a follower }
    characterId: { type: string, format: uuid, description: The new follower }
    relationshipId: { type: string, format: uuid, description: The Relationship record for this bond }

DivineFollowerRemovedEvent:
  type: object
  required: [eventId, deityId, characterId]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    deityId: { type: string, format: uuid, description: The deity losing a follower }
    characterId: { type: string, format: uuid, description: The removed follower }

DivineDeityActivatedEvent:
  type: object
  required: [eventId, deityId, gameServiceId]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    deityId: { type: string, format: uuid, description: The activated deity }
    gameServiceId: { type: string, format: uuid, description: The game service scope }

DivineDeityDormantEvent:
  type: object
  required: [eventId, deityId, gameServiceId]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    deityId: { type: string, format: uuid, description: The dormant deity }
    gameServiceId: { type: string, format: uuid, description: The game service scope }
```

#### 1c. `schemas/divine-configuration.yaml`

All properties with `env: DIVINE_{PROPERTY}` format, single-line descriptions:

```yaml
x-service-configuration:
  properties:
    # Divinity economy
    DivinityCurrencyCode:
      type: string
      env: DIVINE_DIVINITY_CURRENCY_CODE
      default: divinity
      description: Currency code used for divinity economy within each game service

    DefaultDivinityPerBlessingTier:
      type: string
      env: DIVINE_DEFAULT_DIVINITY_PER_BLESSING_TIER
      default: "10,50,200,1000"
      description: Comma-separated divinity costs for Minor,Standard,Greater,Supreme blessing tiers

    DivinityGenerationMultiplier:
      type: number
      format: float
      env: DIVINE_DIVINITY_GENERATION_MULTIPLIER
      minimum: 0.0
      default: 1.0
      description: Global multiplier applied to all divinity generation from mortal actions

    # Blessing items
    BlessingItemCategory:
      type: string
      env: DIVINE_BLESSING_ITEM_CATEGORY
      default: divine_blessing
      description: Item category code used when creating blessing item templates

    BlessingContainerCode:
      type: string
      env: DIVINE_BLESSING_CONTAINER_CODE
      default: divine_blessings
      description: Inventory container code for character blessing storage

    MaxBlessingsPerCharacter:
      type: integer
      env: DIVINE_MAX_BLESSINGS_PER_CHARACTER
      minimum: 1
      maximum: 100
      default: 10
      description: Maximum active blessings a character can hold simultaneously

    # Follower management
    FollowerRelationshipTypeCode:
      type: string
      env: DIVINE_FOLLOWER_RELATIONSHIP_TYPE_CODE
      default: deity_follower
      description: Relationship type code for deity-to-character follower bonds

    RivalryRelationshipTypeCode:
      type: string
      env: DIVINE_RIVALRY_RELATIONSHIP_TYPE_CODE
      default: deity_rivalry
      description: Relationship type code for deity-to-deity rivalry bonds

    # Attention mechanics
    DefaultMaxAttentionSlots:
      type: integer
      env: DIVINE_DEFAULT_MAX_ATTENTION_SLOTS
      minimum: 1
      maximum: 1000
      default: 10
      description: Default maximum characters a deity can actively monitor simultaneously

    AttentionDecayIntervalMinutes:
      type: integer
      env: DIVINE_ATTENTION_DECAY_INTERVAL_MINUTES
      minimum: 1
      maximum: 1440
      default: 60
      description: Minutes between attention slot decay evaluations for inactive followers

    # Seed integration
    DeitySeedTypeCode:
      type: string
      env: DIVINE_DEITY_SEED_TYPE_CODE
      default: deity_domain
      description: Seed type code for deity domain power growth

    # Actor integration
    DeityActorTypeCode:
      type: string
      env: DIVINE_DEITY_ACTOR_TYPE_CODE
      default: deity_watcher
      description: Actor type code used when spawning deity watcher actors via Puppetmaster

    # Background workers
    AttentionWorkerIntervalSeconds:
      type: integer
      env: DIVINE_ATTENTION_WORKER_INTERVAL_SECONDS
      minimum: 10
      maximum: 600
      default: 60
      description: Seconds between attention decay worker evaluation cycles

    DivinityGenerationWorkerIntervalSeconds:
      type: integer
      env: DIVINE_DIVINITY_GENERATION_WORKER_INTERVAL_SECONDS
      minimum: 10
      maximum: 600
      default: 30
      description: Seconds between divinity generation worker processing cycles
```

#### 1d. Update `schemas/state-stores.yaml`

Add under `x-state-stores:`:

```yaml
divine-deities:
  backend: mysql
  service: Divine
  purpose: Deity entity records (durable, queryable by game service, domain, status)

divine-blessings:
  backend: mysql
  service: Divine
  purpose: Blessing grant records linking deities to characters via items (durable, queryable)

divine-attention:
  backend: redis
  prefix: "divine:attention"
  service: Divine
  purpose: Active attention slot tracking per deity (ephemeral, high-frequency reads)

divine-divinity-events:
  backend: redis
  prefix: "divine:divevt"
  service: Divine
  purpose: Pending divinity generation events awaiting batch processing (ephemeral queue)

divine-lock:
  backend: redis
  prefix: "divine:lock"
  service: Divine
  purpose: Distributed locks for deity and blessing mutations
```

### Step 2: Generate Service (creates project, code, and templates)

```bash
cd scripts && ./generate-service.sh divine
```

This single command bootstraps the entire plugin. It auto-creates:

**Plugin project infrastructure** (via `generate-project.sh`):
- `plugins/lib-divine/` directory
- `plugins/lib-divine/lib-divine.csproj` (with ServiceLib.targets import)
- `plugins/lib-divine/AssemblyInfo.cs` (ApiController, InternalsVisibleTo)
- Adds `lib-divine` to `bannou-service.sln` via `dotnet sln add`

**Generated code** (in `plugins/lib-divine/Generated/`):
- `IDivineService.cs` - interface
- `DivineController.cs` - HTTP routing
- `DivineController.Meta.cs` - runtime schema introspection
- `DivineServiceConfiguration.cs` - typed config class
- `DivinePermissionRegistration.cs` - permissions
- `DivineEventsController.cs` - event subscription handlers (from x-event-subscriptions)

**Generated code** (in `bannou-service/Generated/`):
- `Models/DivineModels.cs` - request/response models
- `Clients/DivineClient.cs` - client for other services to call Divine
- `Events/DivineEventsModels.cs` - event models
- Updated `StateStoreDefinitions.cs` with Divine store constants

**Template files** (created once if missing, never overwritten):
- `plugins/lib-divine/DivineService.cs` - business logic template with TODO stubs
- `plugins/lib-divine/DivineServiceModels.cs` - internal models template
- `plugins/lib-divine/DivineServicePlugin.cs` - plugin registration template

**Test project** (via `generate-tests.sh`):
- `plugins/lib-divine.tests/` directory, `.csproj`, `AssemblyInfo.cs`, `GlobalUsings.cs`
- `DivineServiceTests.cs` template with basic tests
- Adds `lib-divine.tests` to `bannou-service.sln` via `dotnet sln add`

**Build check**: `dotnet build` to verify generation succeeded.

### Step 3: Fill In Plugin Registration

#### 3a. `plugins/lib-divine/DivineServicePlugin.cs` (generated template -> fill in)

The generator creates the skeleton. Fill in following the GameSessionServicePlugin pattern:

- Extends `BaseBannouPlugin`
- `PluginName => "divine"`, `DisplayName => "Divine Service"`
- Standard lifecycle: ConfigureServices, ConfigureApplication, OnStartAsync (creates scope), OnRunningAsync, OnShutdownAsync
- **ConfigureServices**: Register `DivineAttentionWorker` and `DivineDivinityGenerationWorker` as hosted services
- **OnRunningAsync**:
  1. Register the `deity_domain` seed type via `ISeedClient.RegisterSeedTypeAsync` if it does not already exist. Use the `DeitySeedTypeCode` config property.
  2. Ensure the `divinity` currency definition exists via `ICurrencyClient`. Use the `DivinityCurrencyCode` config property.
  3. Ensure the `deity_follower` and `deity_rivalry` relationship types exist via `IRelationshipClient`. Use config properties.
  4. Ensure the `divine_blessing` item category exists for blessing items.

### Step 4: Fill In Internal Models

#### 4a. `plugins/lib-divine/DivineServiceModels.cs` (generated template -> fill in)

Internal storage models (not API-facing):

- **`DeityModel`**: DeityId (Guid), GameServiceId (Guid), Code (string), DisplayName (string), Description (string), Domains (List\<DomainInfluenceModel\>), PersonalityTraits (DeityPersonalityTraitsModel), MaxAttentionSlots (int), ActorId (Guid?), SeedId (Guid?), CurrencyWalletId (Guid?), RealmId (Guid?), Status (DeityStatus), FollowerCount (int), CreatedAt (DateTimeOffset), UpdatedAt (DateTimeOffset)
- **`DomainInfluenceModel`**: Domain (DomainCategory), Weight (float)
- **`DeityPersonalityTraitsModel`**: Temperament (string), AttentionBias (string), Generosity (float), Jealousy (float)
- **`BlessingModel`**: BlessingId (Guid), DeityId (Guid), CharacterId (Guid), Tier (BlessingTier), ItemTemplateCode (string), ItemInstanceId (Guid), Reason (string), GrantedAt (DateTimeOffset), RevokedAt (DateTimeOffset?), Status (BlessingStatus enum: Active, Revoked)
- **`AttentionSlotModel`**: DeityId (Guid), CharacterId (Guid), Priority (AttentionPriority), LastActionAt (DateTimeOffset), CumulativeImpression (float)
- **`DivinityEventModel`**: EventId (Guid), DeityId (Guid), CharacterId (Guid?), Domain (DomainCategory), Amount (float), Source (string), SourceEventId (Guid?), CreatedAt (DateTimeOffset)

All models use proper types per T25 (enums, Guids, DateTimeOffset). Nullable for optional fields per T26.

### Step 5: Create Event Handlers

#### 5a. `plugins/lib-divine/DivineServiceEvents.cs` (manual - not auto-generated)

Partial class of DivineService:

- `RegisterEventConsumers(IEventConsumer eventConsumer)` - registers handlers for all consumed events:
  - `character.deleted`
  - `analytics.score.updated` (soft -- Analytics may not be enabled)

**Handler implementations**:

- `HandleCharacterDeletedAsync(CharacterDeletedEvent evt)`:
  1. Query blessings by characterId from `divine-blessings` store
  2. For each active blessing: revoke it (remove item instance via `IInventoryClient`, update blessing record)
  3. Remove follower relationships via `IRelationshipClient`
  4. Update follower counts on affected deities
  5. Remove character from any deity attention slots
  6. Publish `divine.follower.removed` and `divine.blessing.revoked` events as applicable

- `HandleAnalyticsScoreUpdatedAsync(AnalyticsScoreUpdatedEvent evt)`:
  1. Determine which domain(s) the score update relates to (map analytics category -> DomainCategory)
  2. For each relevant domain, find active deities with that domain
  3. Queue a `DivinityEventModel` in `divine-divinity-events` Redis store for batch processing
  4. The background worker will process these in batches, crediting divinity to deity wallets

### Step 6: Create Background Worker Services

#### 6a. `plugins/lib-divine/Services/DivineAttentionWorker.cs` (manual)

A `BackgroundService` that manages deity attention slot decay.

**Loop** (every `AttentionWorkerIntervalSeconds`):
1. Load all active deities from MySQL
2. For each deity with attention slots in Redis:
   a. Check each slot's `LastActionAt` against `AttentionDecayIntervalMinutes`
   b. If a follower hasn't performed domain-relevant actions within the decay window, reduce their `CumulativeImpression`
   c. If impression drops below a threshold, free the attention slot
   d. This creates turnover in which characters a god is "watching"

#### 6b. `plugins/lib-divine/Services/DivineDivinityGenerationWorker.cs` (manual)

A `BackgroundService` that batches divinity generation events into currency credits.

**Loop** (every `DivinityGenerationWorkerIntervalSeconds`):
1. Drain pending `DivinityEventModel` entries from `divine-divinity-events` Redis store
2. Aggregate by deityId (sum amounts per deity)
3. For each deity with pending divinity:
   a. Credit the deity's currency wallet via `ICurrencyClient.CreditAsync` with `DivinityGenerationMultiplier` applied
   b. Publish `divine.divinity.credited` event with aggregated amount and source summary
4. Clear processed events from Redis

### Step 7: Implement Service Business Logic

#### 7a. `plugins/lib-divine/DivineService.cs` (generated template -> fill in)

Partial class with `[BannouService("divine", typeof(IDivineService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]`:

**Constructor dependencies**:
- `IStateStoreFactory` - for all state stores
- `IMessageBus` - event publishing
- `IDistributedLockProvider` - concurrent modification safety
- `ILogger<DivineService>` - structured logging
- `DivineServiceConfiguration` - typed config
- `IEventConsumer` - event handler registration
- `ICurrencyClient` - divinity wallet management (L2 hard dependency)
- `IItemClient` - blessing item template/instance management (L2 hard dependency)
- `IInventoryClient` - blessing container management (L2 hard dependency)
- `IRelationshipClient` - follower/rivalry bond management (L2 hard dependency)
- `ICharacterClient` - validate character existence (L2 hard dependency)
- `IGameServiceClient` - validate game service existence (L2 hard dependency)
- `IServiceProvider` - for optional L4 soft dependencies

**Soft dependencies** (resolved at runtime via `IServiceProvider`, null-checked):
- `ISeedClient` - deity domain power seed management (L2 hard in practice, but deferred seed creation is acceptable)
- `IPuppetmasterClient` - spawn deity watcher actors (L4)
- `IAnalyticsClient` - query domain-relevant score data (L4)

**Store initialization** (in constructor):
- `_deityStore` = GetStore\<DeityModel\>(StateStoreDefinitions.DivineDeities)
- `_blessingStore` = GetStore\<BlessingModel\>(StateStoreDefinitions.DivineBlessings)
- `_attentionStore` = GetStore\<AttentionSlotModel\>(StateStoreDefinitions.DivineAttention)
- `_divinityEventStore` = GetStore\<DivinityEventModel\>(StateStoreDefinitions.DivineDivinityEvents)
- `_lockProvider` for distributed locks

**Key method implementations** (all follow T7 error handling, T8 return pattern):

| Method | Key Logic |
|--------|-----------|
| `CreateDeityAsync` | Validate gameServiceId via `IGameServiceClient`. Validate code uniqueness per game service. Create DeityModel in MySQL. Create divinity currency wallet via `ICurrencyClient`. Optionally create deity domain seed via `ISeedClient`. Optionally spawn deity watcher actor via `IPuppetmasterClient`. Save walletId/seedId/actorId back to DeityModel. Publish lifecycle created event. |
| `GetDeityAsync` | Load from MySQL by ID. 404 if not found. |
| `GetDeityByCodeAsync` | JSON query by gameServiceId + code. 404 if not found. |
| `ListDeitiesAsync` | Paged JSON query with optional filters (gameServiceId required, domain, status). |
| `UpdateDeityAsync` | Lock, load, validate, update non-null fields. Publish lifecycle updated event. |
| `ActivateDeityAsync` | Lock, load, set status Active. If actorId is null and Puppetmaster available, spawn watcher. Publish `divine.deity.activated` event. |
| `DeactivateDeityAsync` | Lock, load, set status Dormant. If Puppetmaster available, stop watcher. Clear attention slots. Publish `divine.deity.dormant` event. |
| `GetDivinityBalanceAsync` | Load deity, get walletId, call `ICurrencyClient.GetBalanceAsync`. |
| `CreditDivinityAsync` | Validate deity exists. Call `ICurrencyClient.CreditAsync` on deity's wallet. Publish `divine.divinity.credited` event. |
| `DebitDivinityAsync` | Validate deity exists. Validate sufficient balance via `ICurrencyClient.GetBalanceAsync`. Call `ICurrencyClient.DebitAsync`. Publish `divine.divinity.debited` event. |
| `GetDivinityHistoryAsync` | Load deity, get walletId, call `ICurrencyClient.GetTransactionHistoryAsync`. |
| `GrantBlessingAsync` | Lock deity. Validate deity is Active. Validate character exists via `ICharacterClient`. Validate character blessing count < `MaxBlessingsPerCharacter`. Calculate divinity cost from tier using `DefaultDivinityPerBlessingTier` config. Debit divinity from deity wallet. Create item instance via `IItemClient.CreateItemInstanceAsync` with blessing template. Place item in character's blessing container via `IInventoryClient.AddItemAsync`. Create BlessingModel in MySQL. Increment deity follower count if not already a follower. Publish `divine.blessing.granted` event. |
| `RevokeBlessingAsync` | Lock. Load blessing record. Remove item instance from character's inventory via `IInventoryClient.RemoveItemAsync`. Mark blessing as Revoked with timestamp. Publish `divine.blessing.revoked` event. |
| `ListBlessingsByCharacterAsync` | Paged JSON query on `divine-blessings` by characterId. |
| `ListBlessingsByDeityAsync` | Paged JSON query on `divine-blessings` by deityId, optional tier filter. |
| `GetBlessingAsync` | Load from MySQL by blessingId. 404 if not found. |
| `RegisterFollowerAsync` | Validate deity and character exist. Create relationship via `IRelationshipClient.CreateRelationshipAsync` (type: `deity_follower`). Increment deity's FollowerCount. Add character to deity's attention slots if capacity available. Publish `divine.follower.registered` event. |
| `GetFollowersAsync` | Query relationships by deityId and type `deity_follower` via `IRelationshipClient`. Paginate results. |

**State key patterns**:
- Deity: `deity:{deityId}`
- Deity by code: `deity-code:{gameServiceId}:{code}`
- Blessing: `blessing:{blessingId}`
- Attention slot: `attention:{deityId}:{characterId}`
- Divinity event: `divevt:{eventId}`
- Locks: `divine:lock:deity:{deityId}`, `divine:lock:blessing:{blessingId}`

### Step 8: Build and Verify

```bash
dotnet build
```

Verify no compilation errors, all generated code resolves, no CS1591 warnings.

### Step 9: Unit Tests

The test project and template `DivineServiceTests.cs` were auto-created in Step 2. Fill in with comprehensive tests:

#### 9a. `plugins/lib-divine.tests/DivineServiceTests.cs` (generated template -> fill in)

Following testing patterns from TESTING-PATTERNS.md:

**Constructor validation**:
- `DivineService_ConstructorIsValid()` via `ServiceConstructorValidator`

**Deity CRUD tests** (capture pattern for state saves and event publishing):
- `CreateDeity_ValidRequest_SavesDeityAndCreatesWalletAndPublishesEvent`
- `CreateDeity_DuplicateCode_ReturnsConflict`
- `CreateDeity_InvalidGameServiceId_ReturnsNotFound`
- `GetDeity_Exists_ReturnsDeity`
- `GetDeity_NotFound_ReturnsNotFound`
- `GetDeityByCode_Exists_ReturnsDeity`
- `ListDeities_WithDomainFilter_ReturnsFiltered`
- `ListDeities_Paginated_ReturnsCorrectPage`
- `UpdateDeity_PartialUpdate_OnlyUpdatesProvidedFields`
- `ActivateDeity_Dormant_SetsActiveAndPublishesEvent`
- `DeactivateDeity_Active_SetsDormantAndClearsAttention`

**Divinity economy tests**:
- `GetDivinityBalance_ValidDeity_ReturnsCurrencyBalance`
- `CreditDivinity_ValidAmount_CreditsWalletAndPublishesEvent`
- `DebitDivinity_SufficientBalance_DebitsWalletAndPublishesEvent`
- `DebitDivinity_InsufficientBalance_ReturnsBadRequest`
- `GetDivinityHistory_ReturnsTransactionHistory`

**Blessing orchestration tests**:
- `GrantBlessing_ValidRequest_DebitsDeityCreditsCharacterPublishesEvent`
- `GrantBlessing_InsufficientDivinity_ReturnsBadRequest`
- `GrantBlessing_DeityDormant_ReturnsBadRequest`
- `GrantBlessing_CharacterAtMaxBlessings_ReturnsConflict`
- `GrantBlessing_CharacterNotFound_ReturnsNotFound`
- `RevokeBlessingActive_RemovesItemAndPublishesEvent`
- `RevokeBlessing_AlreadyRevoked_ReturnsBadRequest`
- `ListBlessingsByCharacter_ReturnsBlessings`
- `ListBlessingsByDeity_WithTierFilter_ReturnsFiltered`
- `GetBlessing_Exists_ReturnsBlessing`

**Follower management tests**:
- `RegisterFollower_ValidRequest_CreatesRelationshipAndIncrementsCount`
- `RegisterFollower_AlreadyFollowing_ReturnsConflict`
- `RegisterFollower_AttentionCapacityFull_RegistersButNoAttentionSlot`
- `GetFollowers_ReturnsPagedFollowers`

**Event handler tests**:
- `HandleCharacterDeleted_WithBlessings_RevokesAllAndRemovesFollower`
- `HandleCharacterDeleted_NotFollower_NoOp`
- `HandleAnalyticsScoreUpdated_DomainRelevant_QueuesDivinityEvent`
- `HandleAnalyticsScoreUpdated_IrrelevantDomain_NoOp`

All tests use the capture pattern (Callback on mock setups) to verify saved state and published events, not just Verify calls.

---

## Files Created/Modified Summary

| File | Action |
|------|--------|
| `schemas/divine-api.yaml` | Create (~18 endpoints across 4 groups) |
| `schemas/divine-events.yaml` | Create (lifecycle + 2 subscriptions + 8 custom events) |
| `schemas/divine-configuration.yaml` | Create (14 configuration properties) |
| `schemas/state-stores.yaml` | Modify (add 5 divine stores) |
| `plugins/lib-divine/DivineService.cs` | Fill in (auto-generated template) |
| `plugins/lib-divine/DivineServiceModels.cs` | Fill in (auto-generated template) |
| `plugins/lib-divine/DivineServicePlugin.cs` | Fill in (auto-generated template) |
| `plugins/lib-divine/DivineServiceEvents.cs` | Create (NOT auto-generated -- partial class with 2 event handlers) |
| `plugins/lib-divine/Services/DivineAttentionWorker.cs` | Create (background service for attention slot decay) |
| `plugins/lib-divine/Services/DivineDivinityGenerationWorker.cs` | Create (background service for batched divinity credit) |
| `plugins/lib-divine.tests/DivineServiceTests.cs` | Fill in (auto-generated template) |
| `plugins/lib-divine/lib-divine.csproj` | Auto-generated by `generate-service.sh` |
| `plugins/lib-divine/AssemblyInfo.cs` | Auto-generated by `generate-service.sh` |
| `plugins/lib-divine/Generated/*` | Auto-generated (do not edit) |
| `bannou-service/Generated/*` | Auto-generated (updated) |
| `bannou-service.sln` | Auto-updated by `generate-service.sh` |
| `plugins/lib-divine.tests/*` | Auto-generated test project |

---

## Dependency Summary

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Layer | Usage |
|------------|-------|-------|
| `IStateStoreFactory` | L0 | All state stores (MySQL deities/blessings, Redis attention/events/locks) |
| `IDistributedLockProvider` | L0 | Concurrent modification safety for deity and blessing mutations |
| `IMessageBus` | L0 | Event publishing for all 8+ custom events |
| `IEventConsumer` | L0 | Event subscription registration for 2 consumed events |
| `ICurrencyClient` | L2 | Divinity wallet creation, credit, debit, balance, history |
| `IItemClient` | L2 | Blessing item template and instance creation |
| `IInventoryClient` | L2 | Blessing container placement, item add/remove |
| `IRelationshipClient` | L2 | Follower bonds (deity-character), rivalry bonds (deity-deity) |
| `ICharacterClient` | L2 | Validate character existence for blessings and followers |
| `IGameServiceClient` | L2 | Validate game service existence for deity scoping |

### Soft Dependencies (runtime resolution -- graceful degradation)

| Dependency | Layer | Usage | Behavior When Missing |
|------------|-------|-------|-----------------------|
| `ISeedClient` | L2 | Deity domain power seed creation | Deity created without seed; domain growth tracking disabled |
| `IPuppetmasterClient` | L4 | Spawn/stop deity watcher actors | Deities have no active behavior; blessings still work via API |
| `IAnalyticsClient` | L4 | Domain-relevant score queries for divinity generation | Divinity generation from analytics events disabled; manual credit still works |

---

## Integration Points

### Divine -> Currency (L2, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Create divinity wallet for deity | `ICurrencyClient.CreateWalletAsync` (currency: config.DivinityCurrencyCode) |
| Check divinity balance | `ICurrencyClient.GetBalanceAsync` |
| Credit divinity from mortal actions | `ICurrencyClient.CreditAsync` with idempotency key |
| Debit divinity for blessing grants | `ICurrencyClient.DebitAsync` |
| Query divinity transaction history | `ICurrencyClient.GetTransactionHistoryAsync` |

### Divine -> Item (L2, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Create blessing item instance | `IItemClient.CreateItemInstanceAsync` (template: blessing item code, category: config.BlessingItemCategory) |
| Destroy blessing item on revoke | `IItemClient.DeleteItemInstanceAsync` |

### Divine -> Inventory (L2, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Ensure character has blessing container | `IInventoryClient.GetOrCreateContainerAsync` (code: config.BlessingContainerCode) |
| Place blessing item in container | `IInventoryClient.AddItemAsync` |
| Remove blessing item on revoke | `IInventoryClient.RemoveItemAsync` |
| Count active blessings | `IInventoryClient.GetContainerItemsAsync` for MaxBlessingsPerCharacter check |

### Divine -> Relationship (L2, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Create follower bond | `IRelationshipClient.CreateRelationshipAsync` (type: config.FollowerRelationshipTypeCode, entityA: deityId, entityB: characterId) |
| Query followers | `IRelationshipClient.ListRelationshipsAsync` by deityId + type |
| Remove follower on character deletion | `IRelationshipClient.DeleteRelationshipAsync` |

### Divine -> Puppetmaster (L4, soft dependency)

| Interaction | API Call |
|-------------|----------|
| Spawn deity watcher on activation | `IPuppetmasterClient.SpawnWatcherAsync` (actorType: config.DeityActorTypeCode, ABML behavior doc) |
| Stop deity watcher on deactivation | `IPuppetmasterClient.StopWatcherAsync` |

### Divine -> Seed (L2, soft in practice)

| Interaction | API Call |
|-------------|----------|
| Register deity_domain seed type on startup | `ISeedClient.RegisterSeedTypeAsync` |
| Create domain power seed for deity | `ISeedClient.CreateSeedAsync` (ownerType: "deity", seedTypeCode: config.DeitySeedTypeCode) |

### Character -> Divine (consumed events)

| Event | Action |
|-------|--------|
| `character.deleted` | Revoke all blessings, remove follower bonds, clear attention slots |

### Analytics -> Divine (consumed events, soft)

| Event | Action |
|-------|--------|
| `analytics.score.updated` | Queue divinity generation events for domain-relevant deities |

---

## Future Extensions (Not in MVP)

These are explicitly deferred from the initial implementation:

1. **Holy Magic Invocations (via Contract)** -- Short-lived micro-contracts between deity and caster for spell invocations. Each invocation creates a Contract instance, the deity's Actor evaluates worthiness, and the Contract resolves with success/failure. Requires Contract integration and Actor behavior document support.

2. **DanMachi Leveling Gate** -- `ILevelGateProviderFactory` interface in bannou-service/ that lib-divine implements. Character (L2) discovers level gate providers via DI collection injection. Greater Blessings serve as rank-up authorization. Deferred until Character has a leveling mechanic.

3. **God Personality Simulation** -- ABML behavior documents that model god attention patterns, jealousy mechanics, and inter-deity politics. This is a behavior authoring task, not a service implementation task. lib-divine provides the data; the behavior documents provide the intelligence.

4. **Domain Contests** -- When two gods share domain influence, divinity generation splits based on relative power. Loser can challenge via a divine duel mechanic. Requires game design specification.

5. **Deity-Deity Rivalries** -- Active rivalry mechanics where gods sabotage each other's followers. The Relationship records exist in MVP; the behavioral consequences are deferred.

6. **Client Events** -- `divine-client-events.yaml` for pushing blessing notifications, divine attention alerts, and divinity milestones to connected clients. Create as follow-up once core service is working.

7. **Variable Provider Factory** -- `IDivineVariableProviderFactory` for ABML behavior expressions (`${divine.blessing_tier}`, `${divine.patron_deity}`, `${divine.divinity_earned}`). Enables Actor behavior documents to reference divine state.

---

## Open Design Questions

These are questions identified during plan extraction that need resolution during implementation:

1. **Blessing item template management**: Does lib-divine create Item templates on startup (one per BlessingTier?) or expect them to already exist? Recommendation: Create them in `OnRunningAsync` if missing, using `BlessingItemCategory` config and tier-specific stat definitions.

2. **Divinity cost scaling**: The `DefaultDivinityPerBlessingTier` config provides base costs, but should costs scale with deity follower count, character level, or realm conditions? Start with flat costs per tier; add scaling in follow-up.

3. **Attention slot semantics**: Are attention slots purely internal (the god "notices" characters for behavior purposes) or visible to players? If visible, need client events. Start internal-only.

4. **Domain-to-analytics mapping**: The `HandleAnalyticsScoreUpdated` handler needs a mapping from analytics categories to DomainCategory. Where does this mapping live? Recommendation: config property `DomainAnalyticsMappings` as a JSON string, or a dedicated mapping state store.

5. **Multi-deity follower rules**: Can a character follow multiple deities? If yes, how does this interact with blessing limits? The KB suggests monotheistic tendencies ("a character's patron deity") but doesn't explicitly forbid polytheism. Recommendation: Allow multiple followings; `MaxBlessingsPerCharacter` is the natural limit.

---

## Verification

1. `dotnet build` -- compiles without errors or warnings
2. `dotnet test plugins/lib-divine.tests/` -- all unit tests pass
3. Verify no CS1591 warnings (all schema properties have descriptions)
4. Verify `StateStoreDefinitions.cs` contains all 5 Divine constants after generation
5. Verify `DivineClient.cs` generated in `bannou-service/Generated/Clients/` for other services to call Divine
6. Verify event subscription handlers generated in `DivineEventsController.cs` for consumed events
7. Verify the `IDivineService` interface has methods for all 18 endpoints
8. Manual verification: confirm `ICurrencyClient`, `IItemClient`, `IInventoryClient`, `IRelationshipClient` are available via constructor injection (L2 loads before L4)
9. Verify blessing grant flow end-to-end in unit tests: debit divinity -> create item -> place in container -> record blessing -> publish event
