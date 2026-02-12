# Implementation Plan: Divine Service (L4 Game Features)

> **Source**: Synthesized from `~/repos/arcadia-kb/Claude/systems-mining-2026-02-10/` (5 agent reports + synthesis)
> **Related**: `docs/plugins/PUPPETMASTER.md`, `docs/plugins/ACTOR.md`, `docs/reference/SERVICE-HIERARCHY.md`
> **Prerequisites**: lib-seed, lib-currency, lib-collection, lib-contract, lib-relationship, lib-actor, lib-puppetmaster must be operational
> **Status**: APPROVED -- All design decisions resolved

---

## Resolved Design Decisions

All open questions have been resolved. These decisions are final and inform the implementation below.

### Q1: God Entity Ownership -> lib-divine Owns Deity Entities Directly

Gods are **completely distinct from characters** -- there are far more differences than similarities. Gods don't have physical bodies, don't belong to realms in the character sense, have domain power, accumulate divinity, and act through their own motivations. They share more in common with the Actors that make characters grow/change, minus all the "humanity/character" parts. A god whispering in someone's ear does so **through the character's own actor**, not the character themselves, and to the god's own ends, not the character's. lib-divine owns the DeityModel; each deity has an associated actorId linking to its Actor brain.

### Q2: Puppetmaster Integration -> One-Way Dependency (Divine Calls Puppetmaster)

lib-divine calls into Puppetmaster to start/stop deity watcher actors. This avoids circularity -- if Puppetmaster knew about divine directly, divine would still need to know about Puppetmaster anyway. The one-way call mirrors Puppetmaster's own pattern with Actor. lib-divine owns "what a god is" (domain, divinity, blessings, followers). Puppetmaster owns "how a god acts" (watcher lifecycle, behavior execution, event monitoring). lib-divine registers custom ABML action handlers (e.g., `grant_blessing`, `spend_divinity`) that deity behavior documents invoke.

### Q3: DanMachi Leveling Gate -> Deferred

Not needed yet -- Character has no leveling mechanic. This is very implementation-specific and will be added when leveling exists. The Variable Provider Factory pattern is documented as the future integration path.

### Q4: God Scoping -> Game-Service-Scoped (No Arcadia-Specific Content)

Deities are scoped to game services, consistent with every other L2/L4 entity. The service itself contains zero Arcadia-specific content -- Arcadia's 18 Old Gods are seeded via behaviors and templates at deployment time, not baked into lib-divine. The service just needs to support what Arcadia requires with minimal effort (a few behavior docs and item templates).

### Q5: Divinity as Currency -> Shared "divinity" Currency Type, Per-Entity Wallets

One "divinity" currency definition per game service, with wallets per entity. Gods get wallets, but **humans can also get and use divinity** -- it's used differently but there's no reason it wouldn't be the same currency type (gods give divinity to humans to begin with). God-to-god divinity transfers work naturally as Currency transfers. This gives full Currency primitives (credit, debit, hold, transfer, history, idempotency) for free.

### Q6: Blessings -> Existing Item Patterns (#286 / #282)

Blessings map to existing "itemize anything" patterns:
- **Greater Blessings** (permanent rank-ups, unlocks) -> lib-collection (#286) -- universal content unlock and archive pattern. A Greater Blessing is a permanent unlock entry.
- **Minor Blessings** (temporary buffs, timed effects) -> Status Inventory (#282) -- status items with contract-based lifecycle. A Minor Blessing is a status item with a duration contract.

lib-divine orchestrates the granting/revoking ceremony (deity evaluation, divinity debit, worthiness check) and delegates the actual blessing storage to whichever pattern fits the tier. This avoids inventing a new storage model.

### Q7: MVP Scope -> Deity + Divinity + Blessings (~22 endpoints)

Core identity, economy, and primary output. No holy magic invocations, no leveling gate, no god personality simulation (ABML behavior docs handle that). God personality and attention patterns are behavior authoring concerns, not lib-divine API concerns.

---

## Context

The Divine service is a new L4 Game Features plugin that manages the pantheon -- deity entities, divinity economy, and blessing orchestration. It is a thin orchestration layer (like Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver divine game mechanics.

**The composability thesis** (from systems mining synthesis):
- **God identity** -> DeityModel in lib-divine (owns the entity -- completely distinct from characters)
- **God behavior** -> Actor (event brain type) managed via Puppetmaster (owns the how)
- **God domain power** -> Seed (progressive growth of a deity's influence)
- **Divinity resource** -> Currency (shared "divinity" type, per-entity wallets -- gods AND humans can hold it)
- **Greater Blessings** -> lib-collection (#286) -- permanent unlocks (rank-ups, ability access)
- **Minor Blessings** -> Status Inventory (#282) -- temporary buffs with contract lifecycle
- **Holy magic invocations** -> Contract (micro-agreements between deity and caster) [DEFERRED to follow-up]
- **God-follower bonds** -> Relationship (deity-character)
- **God-god rivalries** -> Relationship (deity-deity)

**Critical architectural insight**: Gods influence characters **through the character's own Actor**, not directly. A god deciding to bless or curse a character publishes events that the character's Actor brain consumes and acts upon. The god's Actor (regional watcher) monitors event streams and makes decisions; the character's Actor receives the consequences. This is fundamental -- gods act to their own ends, through intermediaries.

**Why L4**: Divine depends on Currency, Item, Inventory (L2), and Puppetmaster, Behavior (L4). It is optional -- games without divine systems work fine. It orchestrates content from multiple layers. Classic L4 -- optional, feature-rich, maximum connectivity.

**Why not just composable primitives?** The synthesis concluded: "Seed (domain power) + Currency (divinity) + Contract (blessings) gets close but may not be enough." The unique domain logic lib-divine owns:
1. **Deity-as-entity CRUD** -- gods aren't characters, items, or seeds. They have domains, attention capacity, and follower mechanics.
2. **Blessing orchestration ceremony** -- the multi-step flow of a god noticing a character, evaluating worthiness, spending divinity, and granting a blessing. No existing primitive handles this workflow.
3. **Divinity generation rules** -- mapping mortal actions in a domain to divinity credit for the domain's god. This is divine-specific event routing logic.
4. **Attention economy** -- gods have limited attention capacity and personality-driven patterns for which characters they notice. This feeds into Actor behavior but the attention tracking lives in lib-divine.

**Design principle**: Puppetmaster orchestrates what NPCs experience. Gardener orchestrates what players experience. **Divine orchestrates what gods experience.** Each is a thin L4 layer over shared primitives, with its own domain-specific semantics.

**Zero Arcadia-specific content**: lib-divine is a generic pantheon management service. It knows about deities, domains, divinity, and blessings. It does NOT know about Mnemosyne, Nexius, or any specific god. Arcadia's 18 Old Gods are configured through behaviors and templates at deployment time.

---

## Implementation Steps

### Step 1: Create Schema Files

#### 1a. `schemas/divine-api.yaml`

- **Header**: `x-service-layer: GameFeatures`, `servers: [{ url: http://localhost:5012 }]`
- All properties have `description` fields (CS1591 compliance)
- NRT compliance: optional ref types have `nullable: true`, required fields in `required` arrays

**`x-references`** (resource reference tracking for lib-resource cleanup):
```yaml
info:
  x-references:
    - target: character
      sourceType: divine
      field: characterId
      onDelete: cascade
      cleanup:
        endpoint: /divine/cleanup-by-character
        payloadTemplate: '{"characterId": "{{resourceId}}"}'
    - target: game-service
      sourceType: divine
      field: gameServiceId
      onDelete: cascade
      cleanup:
        endpoint: /divine/cleanup-by-game-service
        payloadTemplate: '{"gameServiceId": "{{resourceId}}"}'
```

**~22 POST endpoints** across five API groups:

| Group | Endpoints | Permissions |
|-------|-----------|-------------|
| Deity Management (8) | create, get, get-by-code, list, update, activate, deactivate, delete | `developer` |
| Divinity Economy (4) | get-balance, credit, debit, get-history | `developer` |
| Blessing Orchestration (5) | grant, revoke, list-by-character, list-by-deity, get | `developer` |
| Follower Management (3) | register-follower, unregister-follower, get-followers | `developer` |
| Resource Cleanup (2) | cleanup-by-character, cleanup-by-game-service | `developer` |

**Enums** (defined in `components/schemas`):
- `DeityStatus`: Active, Dormant, Archived
- `BlessingTier`: Minor, Standard, Greater, Supreme
- `BlessingStatus`: Active, Revoked
- `AttentionPriority`: Low, Medium, High, Critical

**Note on domain categories**: Domain codes are **opaque strings**, not enums. Different games define different domains (Arcadia uses War, Knowledge, Nature, etc.; another game might use completely different categories). Domain codes follow the same pattern as seed type codes, collection type codes, and relationship type codes -- extensible without schema changes. Deities register their domains via the `domains` array on creation.

**Shared sub-types** (defined in `components/schemas`):
- `DomainInfluence` -- domain (string, opaque domain code) + weight (double) pair for primary/secondary domains
- `DeityPersonalityTraits` -- temperament (string), attentionBias (string), generosity (double 0-1), jealousy (double 0-1)
- `BlessingSummary` -- blessingId (uuid), deityId (uuid), characterId (uuid), tier (BlessingTier), itemInstanceId (uuid), grantedAt (date-time)

**Request models** (one per endpoint):
- `CreateDeityRequest`: gameServiceId (uuid), code (string), displayName (string), description (string), domains (array DomainInfluence, min 1), personalityTraits (DeityPersonalityTraits), maxAttentionSlots (int, default 10), realmId (uuid, nullable -- home realm)
- `GetDeityRequest`: deityId (uuid)
- `GetDeityByCodeRequest`: gameServiceId (uuid), code (string)
- `ListDeitiesRequest`: gameServiceId (uuid), domainCode (nullable string -- filter by domain code), status (nullable DeityStatus), page (int, default 1), pageSize (int, default 50)
- `UpdateDeityRequest`: deityId (uuid), displayName (nullable string), description (nullable string), domains (nullable array DomainInfluence), personalityTraits (nullable DeityPersonalityTraits), maxAttentionSlots (nullable int)
- `ActivateDeityRequest`: deityId (uuid)
- `DeactivateDeityRequest`: deityId (uuid)
- `GetDivinityBalanceRequest`: deityId (uuid)
- `CreditDivinityRequest`: deityId (uuid), amount (double), source (string), sourceEventId (uuid, nullable), description (string)
- `DebitDivinityRequest`: deityId (uuid), amount (double), purpose (string), targetCharacterId (uuid, nullable), description (string)
- `GetDivinityHistoryRequest`: deityId (uuid), page (int, default 1), pageSize (int, default 50)
- `GrantBlessingRequest`: deityId (uuid), characterId (uuid), tier (BlessingTier), itemTemplateCode (string), reason (string)
- `RevokeBlessingRequest`: blessingId (uuid), reason (string)
- `ListBlessingsByCharacterRequest`: characterId (uuid), page (int, default 1), pageSize (int, default 50)
- `ListBlessingsByDeityRequest`: deityId (uuid), tier (nullable BlessingTier), page (int, default 1), pageSize (int, default 50)
- `GetBlessingRequest`: blessingId (uuid)
- `DeleteDeityRequest`: deityId (uuid)
- `RegisterFollowerRequest`: deityId (uuid), characterId (uuid)
- `UnregisterFollowerRequest`: deityId (uuid), characterId (uuid)
- `GetFollowersRequest`: deityId (uuid), page (int, default 1), pageSize (int, default 50)
- `CleanupByCharacterRequest`: characterId (uuid)
- `CleanupByGameServiceRequest`: gameServiceId (uuid)

**Response models**:
- `DeityResponse`: deityId, gameServiceId, code, displayName, description, domains, personalityTraits, maxAttentionSlots, actorId (nullable uuid), seedId (nullable uuid), currencyWalletId (nullable uuid), realmId (nullable uuid), status, followerCount (int), createdAt, updatedAt
- `ListDeitiesResponse`: deities (array DeityResponse), totalCount, page, pageSize
- `DivinityBalanceResponse`: deityId (uuid), balance (double), currencyCode (string), walletId (uuid)
- `DivinityHistoryResponse`: transactions (array -- reuse Currency's transaction format), totalCount, page, pageSize
- `BlessingResponse`: blessingId, deityId, characterId, tier (BlessingTier), itemTemplateCode, itemInstanceId, reason, grantedAt, revokedAt (nullable), status (BlessingStatus)
- `ListBlessingsResponse`: blessings (array BlessingSummary), totalCount, page, pageSize
- `FollowerResponse`: characterId (uuid), deityId (uuid), registeredAt (date-time), relationshipId (uuid)
- `ListFollowersResponse`: followers (array FollowerResponse), totalCount, page, pageSize

#### 1b. `schemas/divine-events.yaml`

**x-lifecycle** for `Deity` entity (generates created/updated/deleted events):
- Model fields: deityId (primary), gameServiceId, code, displayName, description, status, followerCount, createdAt, updatedAt
- Sensitive: personalityTraits (exclude -- internal simulation data)

**x-event-subscriptions** (consumed events):
- `analytics.score.updated` -> `AnalyticsScoreUpdatedEvent` -> `HandleAnalyticsScoreUpdated` -- detect domain-relevant achievements for divinity generation [SOFT: Analytics is L4]

> **Note**: Character deletion cleanup is handled via `x-references` + cleanup endpoints (T28), NOT via `character.deleted` event subscription. See Step 1a for `x-references` declaration and Step 7 for cleanup endpoint implementations.

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
    amount: { type: number, format: double, description: Amount of divinity credited }
    source: { type: string, description: Source of the divinity gain }
    sourceEventId: { type: string, format: uuid, nullable: true, description: The triggering event if applicable }

DivineDivinityDebitedEvent:
  type: object
  required: [eventId, deityId, amount, purpose]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    deityId: { type: string, format: uuid, description: The deity spending divinity }
    amount: { type: number, format: double, description: Amount of divinity spent }
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

    DivinityCostMinor:
      type: number
      format: double
      env: DIVINE_DIVINITY_COST_MINOR
      minimum: 0.0
      default: 10.0
      description: Divinity cost for granting a Minor tier blessing

    DivinityCostStandard:
      type: number
      format: double
      env: DIVINE_DIVINITY_COST_STANDARD
      minimum: 0.0
      default: 50.0
      description: Divinity cost for granting a Standard tier blessing

    DivinityCostGreater:
      type: number
      format: double
      env: DIVINE_DIVINITY_COST_GREATER
      minimum: 0.0
      default: 200.0
      description: Divinity cost for granting a Greater tier blessing

    DivinityCostSupreme:
      type: number
      format: double
      env: DIVINE_DIVINITY_COST_SUPREME
      minimum: 0.0
      default: 1000.0
      description: Divinity cost for granting a Supreme tier blessing

    DivinityGenerationMultiplier:
      type: number
      format: double
      env: DIVINE_DIVINITY_GENERATION_MULTIPLIER
      minimum: 0.0
      default: 1.0
      description: Global multiplier applied to all divinity generation from mortal actions

    # Blessing integration
    BlessingCollectionType:
      type: string
      env: DIVINE_BLESSING_COLLECTION_TYPE
      default: divine_blessings
      description: Collection type code for permanent blessings via lib-collection

    BlessingStatusCategory:
      type: string
      env: DIVINE_BLESSING_STATUS_CATEGORY
      default: divine_blessing
      description: Status category code for temporary blessings via Status Inventory

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

    AttentionImpressionThreshold:
      type: number
      format: double
      env: DIVINE_ATTENTION_IMPRESSION_THRESHOLD
      minimum: 0.0
      default: 0.1
      description: Minimum cumulative impression below which an attention slot is freed during decay

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
      description: Actor type code used when starting deity watcher actors via Puppetmaster

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
  4. Register blessing collection type and status templates if lib-collection / Status Inventory are available (soft -- skip if not enabled).

### Step 4: Fill In Internal Models

#### 4a. `plugins/lib-divine/DivineServiceModels.cs` (generated template -> fill in)

Internal storage models (not API-facing):

- **`DeityModel`**: DeityId (Guid), GameServiceId (Guid), Code (string), DisplayName (string), Description (string), Domains (List\<DomainInfluenceModel\>), PersonalityTraits (DeityPersonalityTraitsModel), MaxAttentionSlots (int), ActorId (Guid?), SeedId (Guid?), CurrencyWalletId (Guid?), RealmId (Guid?), Status (DeityStatus), FollowerCount (int), CreatedAt (DateTimeOffset), UpdatedAt (DateTimeOffset)
- **`DomainInfluenceModel`**: Domain (string, opaque domain code), Weight (double)
- **`DeityPersonalityTraitsModel`**: Temperament (string), AttentionBias (string), Generosity (double), Jealousy (double)
- **`BlessingModel`**: BlessingId (Guid), DeityId (Guid), CharacterId (Guid), Tier (BlessingTier), ItemTemplateCode (string), ItemInstanceId (Guid), Reason (string), GrantedAt (DateTimeOffset), RevokedAt (DateTimeOffset?), Status (BlessingStatus)
- **`AttentionSlotModel`**: DeityId (Guid), CharacterId (Guid), Priority (AttentionPriority), LastActionAt (DateTimeOffset), CumulativeImpression (double)
- **`DivinityEventModel`**: EventId (Guid), DeityId (Guid), CharacterId (Guid?), Domain (string, opaque domain code), Amount (double), Source (string), SourceEventId (Guid?), CreatedAt (DateTimeOffset)

All models use proper types per T25 (enums, Guids, DateTimeOffset). Nullable for optional fields per T26.

### Step 5: Create Event Handlers

#### 5a. `plugins/lib-divine/DivineServiceEvents.cs` (manual - not auto-generated)

Partial class of DivineService. The generated `DivineEventsController.cs` handles event subscription registration from `x-event-subscriptions` -- do NOT manually register events via `IEventConsumer` or add `IEventConsumer` as a constructor dependency.

**Handler implementations** (methods called by generated `DivineEventsController`):

- `HandleAnalyticsScoreUpdatedAsync(AnalyticsScoreUpdatedEvent evt)`:
  1. Determine which domain(s) the score update relates to (map analytics category -> domain code)
  2. For each relevant domain, find active deities with that domain
  3. Queue a `DivinityEventModel` in `divine-divinity-events` Redis store for batch processing
  4. The background worker will process these in batches, crediting divinity to deity wallets

> **Note**: Character deletion cleanup is NOT handled via event subscription. It is handled via the `x-references` cleanup endpoints (`/divine/cleanup-by-character` and `/divine/cleanup-by-game-service`) defined in Step 1a. See `CleanupByCharacterAsync` and `CleanupByGameServiceAsync` in Step 7 for the implementations.

### Step 6: Create Background Worker Services

#### 6a. `plugins/lib-divine/Services/DivineAttentionWorker.cs` (manual)

A `BackgroundService` that manages deity attention slot decay.

**Loop** (every `AttentionWorkerIntervalSeconds`):
1. Acquire distributed lock `divine:lock:attention-worker` (T9 multi-instance safety -- only one instance processes at a time)
2. Load all active deities from MySQL
3. For each deity with attention slots in Redis:
   a. Check each slot's `LastActionAt` against `AttentionDecayIntervalMinutes`
   b. If a follower hasn't performed domain-relevant actions within the decay window, reduce their `CumulativeImpression`
   c. If impression drops below `AttentionImpressionThreshold` config value, free the attention slot
   d. This creates turnover in which characters a god is "watching"
4. Release lock

#### 6b. `plugins/lib-divine/Services/DivineDivinityGenerationWorker.cs` (manual)

A `BackgroundService` that batches divinity generation events into currency credits.

**Loop** (every `DivinityGenerationWorkerIntervalSeconds`):
1. Acquire distributed lock `divine:lock:divinity-generation-worker` (T9 multi-instance safety -- only one instance processes at a time)
2. Drain pending `DivinityEventModel` entries from `divine-divinity-events` Redis store
3. Aggregate by deityId (sum amounts per deity)
4. For each deity with pending divinity:
   a. Credit the deity's currency wallet via `ICurrencyClient.CreditAsync` with `DivinityGenerationMultiplier` applied
   b. Publish `divine.divinity.credited` event with aggregated amount and source summary
5. Clear processed events from Redis
6. Release lock

### Step 7: Implement Service Business Logic

#### 7a. `plugins/lib-divine/DivineService.cs` (generated template -> fill in)

Partial class with `[BannouService("divine", typeof(IDivineService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]`:

**Constructor dependencies**:
- `IStateStoreFactory` - for all state stores
- `IMessageBus` - event publishing
- `IDistributedLockProvider` - concurrent modification safety
- `ILogger<DivineService>` - structured logging
- `DivineServiceConfiguration` - typed config
- `ICurrencyClient` - divinity wallet management (L2 hard dependency)
- `IRelationshipClient` - follower/rivalry bond management (L2 hard dependency)
- `ICharacterClient` - validate character existence (L2 hard dependency)
- `IGameServiceClient` - validate game service existence (L2 hard dependency)
- `ISeedClient` - deity domain power seed management (L2 hard dependency)
- `ICollectionClient` - permanent blessing grants via lib-collection (#286) (L2 hard dependency)
- `IServiceProvider` - for optional L4 soft dependencies

> **Note**: `IEventConsumer` is NOT a constructor dependency. Event subscriptions declared via `x-event-subscriptions` in the events schema are handled by the generated `DivineEventsController.cs`, which calls methods on the service class directly.

**Soft dependencies** (resolved at runtime via `IServiceProvider`, null-checked):
- `IStatusClient` - temporary blessing grants via Status Inventory (#282) (L4)
- `IPuppetmasterClient` - start/stop deity watcher actors (L4)
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
| `CreateDeityAsync` | Validate gameServiceId via `IGameServiceClient`. Validate code uniqueness per game service. Create DeityModel in MySQL. Create divinity currency wallet via `ICurrencyClient`. Create deity domain seed via `ISeedClient`. Optionally start deity watcher actor via `IPuppetmasterClient` (soft). Save walletId/seedId/actorId back to DeityModel. Publish lifecycle created event. |
| `GetDeityAsync` | Load from MySQL by ID. 404 if not found. |
| `GetDeityByCodeAsync` | JSON query by gameServiceId + code. 404 if not found. |
| `ListDeitiesAsync` | Paged JSON query with optional filters (gameServiceId required, domain, status). |
| `UpdateDeityAsync` | Lock, load, validate, update non-null fields. Publish lifecycle updated event. |
| `ActivateDeityAsync` | Lock, load, set status Active. If actorId is null and Puppetmaster available, start watcher. Publish `divine.deity.activated` event. |
| `DeactivateDeityAsync` | Lock, load, set status Dormant. If Puppetmaster available, stop watcher. Clear attention slots. Publish `divine.deity.dormant` event. |
| `GetDivinityBalanceAsync` | Load deity, get walletId, call `ICurrencyClient.GetBalanceAsync`. |
| `CreditDivinityAsync` | Validate deity exists. Call `ICurrencyClient.CreditAsync` on deity's wallet. Publish `divine.divinity.credited` event. |
| `DebitDivinityAsync` | Validate deity exists. Validate sufficient balance via `ICurrencyClient.GetBalanceAsync`. Call `ICurrencyClient.DebitAsync`. Publish `divine.divinity.debited` event. |
| `GetDivinityHistoryAsync` | Load deity, get walletId, call `ICurrencyClient.GetTransactionHistoryAsync`. |
| `GrantBlessingAsync` | Lock deity. Validate deity is Active. Validate character exists via `ICharacterClient`. Validate character blessing count < `MaxBlessingsPerCharacter`. Calculate divinity cost from tier using per-tier config properties (`DivinityCostMinor`/`Standard`/`Greater`/`Supreme`). Debit divinity from deity wallet. **For Greater/Supreme blessings**: grant permanent unlock via lib-collection (#286) pattern. **For Minor/Standard blessings**: grant status item via Status Inventory (#282) pattern with contract-based duration. Create BlessingModel in MySQL linking to the granted item/status. Publish `divine.blessing.granted` event. |
| `RevokeBlessingAsync` | Lock. Load blessing record. For status-type blessings: remove status item (triggers contract cleanup). For permanent blessings: mark as revoked in collection. Mark BlessingModel as Revoked with timestamp. Publish `divine.blessing.revoked` event. |
| `ListBlessingsByCharacterAsync` | Paged JSON query on `divine-blessings` by characterId. |
| `ListBlessingsByDeityAsync` | Paged JSON query on `divine-blessings` by deityId, optional tier filter. |
| `GetBlessingAsync` | Load from MySQL by blessingId. 404 if not found. |
| `DeleteDeityAsync` | Lock deity. Validate exists. Deactivate if Active (stop watcher if Puppetmaster available). Revoke all active blessings. Remove all follower relationships. Delete attention slots. Call `IResourceClient.ExecuteCleanupAsync` for deity's owned resources (wallet, seed). Delete DeityModel from MySQL. Publish lifecycle deleted event. |
| `RegisterFollowerAsync` | Validate deity and character exist. Create relationship via `IRelationshipClient.CreateRelationshipAsync` (type: `deity_follower`). Increment deity's FollowerCount. Add character to deity's attention slots if capacity available. Publish `divine.follower.registered` event. |
| `UnregisterFollowerAsync` | Validate deity and character exist. Delete relationship via `IRelationshipClient.DeleteRelationshipAsync`. Decrement deity's FollowerCount. Remove character from deity's attention slots. Publish `divine.follower.removed` event. |
| `GetFollowersAsync` | Query relationships by deityId and type `deity_follower` via `IRelationshipClient`. Paginate results. |
| `CleanupByCharacterAsync` | Called by lib-resource when a character is deleted (T28 cleanup endpoint). Query blessings by characterId -- revoke all active blessings. Remove follower relationships for this character from all deities. Update follower counts. Clear attention slots. Publish `divine.follower.removed` and `divine.blessing.revoked` events as applicable. |
| `CleanupByGameServiceAsync` | Called by lib-resource when a game service is deleted (T28 cleanup endpoint). Query all deities for this gameServiceId. For each deity: deactivate, revoke blessings, remove followers, delete deity record. Publish lifecycle deleted events. |

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
- `DeleteDeity_Active_DeactivatesAndDeletesAllDependentData`
- `DeleteDeity_NotFound_ReturnsNotFound`

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
- `UnregisterFollower_ValidRequest_RemovesRelationshipAndDecrementsCount`
- `UnregisterFollower_NotFollowing_ReturnsNotFound`
- `RegisterFollower_AttentionCapacityFull_RegistersButNoAttentionSlot`
- `GetFollowers_ReturnsPagedFollowers`

**Cleanup endpoint tests** (T28):
- `CleanupByCharacter_WithBlessings_RevokesAllAndRemovesFollower`
- `CleanupByCharacter_NotFollower_NoOp`
- `CleanupByGameService_DeletesAllDeitiesAndDependents`

**Event handler tests**:
- `HandleAnalyticsScoreUpdated_DomainRelevant_QueuesDivinityEvent`
- `HandleAnalyticsScoreUpdated_IrrelevantDomain_NoOp`

All tests use the capture pattern (Callback on mock setups) to verify saved state and published events, not just Verify calls.

---

## Files Created/Modified Summary

| File | Action |
|------|--------|
| `schemas/divine-api.yaml` | Create (~22 endpoints across 5 groups) |
| `schemas/divine-events.yaml` | Create (lifecycle + 1 subscription + 8 custom events) |
| `schemas/divine-configuration.yaml` | Create (18 configuration properties) |
| `schemas/state-stores.yaml` | Modify (add 5 divine stores) |
| `plugins/lib-divine/DivineService.cs` | Fill in (auto-generated template) |
| `plugins/lib-divine/DivineServiceModels.cs` | Fill in (auto-generated template) |
| `plugins/lib-divine/DivineServicePlugin.cs` | Fill in (auto-generated template) |
| `plugins/lib-divine/DivineServiceEvents.cs` | Create (NOT auto-generated -- partial class with 1 event handler) |
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
| `ICurrencyClient` | L2 | Divinity wallet creation, credit, debit, balance, history |
| `IRelationshipClient` | L2 | Follower bonds (deity-character), rivalry bonds (deity-deity) |
| `ICharacterClient` | L2 | Validate character existence for blessings and followers |
| `IGameServiceClient` | L2 | Validate game service existence for deity scoping |
| `ISeedClient` | L2 | Deity domain power seed creation and growth tracking |
| `ICollectionClient` | L2 | Permanent blessing grants (Greater/Supreme tiers, #286) |

### Soft Dependencies (runtime resolution -- graceful degradation)

| Dependency | Layer | Usage | Behavior When Missing |
|------------|-------|-------|-----------------------|
| `IStatusClient` | L4 | Temporary blessing grants (Minor/Standard tiers, #282) | Temporary blessings unavailable; only permanent blessings work |
| `IPuppetmasterClient` | L4 | Start/stop deity watcher actors | Deities have no active behavior; blessings still work via API |
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

### Divine -> Collection (L2, hard dependency -- #286)

| Interaction | API Call |
|-------------|----------|
| Grant permanent blessing (Greater/Supreme) | `ICollectionClient.GrantEntryAsync` (collection: "divine_blessings", entry: blessing template code) |
| Revoke permanent blessing | `ICollectionClient.RevokeEntryAsync` or mark as revoked |
| Check permanent blessings | `ICollectionClient.QueryAsync` by collection type |

### Divine -> Status Inventory (L4, soft dependency -- #282)

| Interaction | API Call |
|-------------|----------|
| Grant temporary blessing (Minor/Standard) | `IStatusClient.GrantAsync` (status template: blessing code, entity: characterId) |
| Revoke temporary blessing | `IStatusClient.RemoveAsync` (triggers contract cleanup) |
| Check active temporary blessings | `IStatusClient.ListAsync` by category "divine_blessing" |
| Count active blessings (both types) | Query both Collection and Status for `MaxBlessingsPerCharacter` check |

### Divine -> Relationship (L2, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Create follower bond | `IRelationshipClient.CreateRelationshipAsync` (type: config.FollowerRelationshipTypeCode, entityA: deityId, entityB: characterId) |
| Query followers | `IRelationshipClient.ListRelationshipsAsync` by deityId + type |
| Remove follower on character deletion | `IRelationshipClient.DeleteRelationshipAsync` |

### Divine -> Puppetmaster (L4, soft dependency)

| Interaction | API Call |
|-------------|----------|
| Start deity watcher on activation | `IPuppetmasterClient.StartWatcherAsync` (actorType: config.DeityActorTypeCode, ABML behavior doc) |
| Stop deity watcher on deactivation | `IPuppetmasterClient.StopWatcherAsync` |

### Divine -> Seed (L2, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Register deity_domain seed type on startup | `ISeedClient.RegisterSeedTypeAsync` |
| Create domain power seed for deity | `ISeedClient.CreateSeedAsync` (ownerType: "deity", seedTypeCode: config.DeitySeedTypeCode) |

### Character -> Divine (via lib-resource cleanup, T28)

| Trigger | Endpoint | Action |
|---------|----------|--------|
| Character deleted | `/divine/cleanup-by-character` | Revoke all blessings, remove follower bonds, clear attention slots |
| Game service deleted | `/divine/cleanup-by-game-service` | Delete all deities and dependent data for the game service |

> **Note**: Cleanup is triggered by lib-resource calling the cleanup endpoints declared in `x-references`, NOT via event subscription to `character.deleted`. This follows T28 (Resource-Managed Cleanup).

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

## Implementation-Time Design Questions

These are smaller questions that should be resolved during implementation, not blocking the plan:

1. **Blessing item/status template management**: Does lib-divine create templates on startup or expect them to pre-exist? Recommendation: Create them in `OnRunningAsync` if missing. Greater/Supreme tiers create collection entry templates (#286); Minor/Standard tiers create status templates (#282).

2. **Divinity cost scaling**: Start with flat costs per tier from `DivinityCostMinor`/`Standard`/`Greater`/`Supreme` config properties. Add scaling (by follower count, realm conditions, etc.) in a follow-up.

3. **Attention slot semantics**: Start internal-only (god "notices" characters for behavior purposes). Add client events in follow-up if players should see divine attention.

4. **Domain-to-analytics mapping**: The `HandleAnalyticsScoreUpdated` handler needs analytics-category-to-domain-code mapping. Recommendation: config property `DomainAnalyticsMappings` as a JSON string, or a dedicated mapping state store.

5. **Multi-deity follower rules**: Allow multiple followings; `MaxBlessingsPerCharacter` is the natural limit. The KB suggests monotheistic tendencies ("a character's patron deity") but doesn't forbid polytheism -- the game design (ABML behaviors, blessing costs, god jealousy) creates soft monotheistic pressure without hard-coding it.

---

## Verification

1. `dotnet build` -- compiles without errors or warnings
2. `dotnet test plugins/lib-divine.tests/` -- all unit tests pass
3. Verify no CS1591 warnings (all schema properties have descriptions)
4. Verify `StateStoreDefinitions.cs` contains all 5 Divine constants after generation
5. Verify `DivineClient.cs` generated in `bannou-service/Generated/Clients/` for other services to call Divine
6. Verify event subscription handlers generated in `DivineEventsController.cs` for consumed events
7. Verify the `IDivineService` interface has methods for all ~22 endpoints
8. Manual verification: confirm `ICurrencyClient`, `ICollectionClient`, `ISeedClient`, `IRelationshipClient`, `ICharacterClient`, `IGameServiceClient` are available via constructor injection (L2 loads before L4)
9. Verify blessing grant flow end-to-end in unit tests: debit divinity -> grant via Collection (#286) or Status Inventory (#282) based on tier -> record BlessingModel -> publish event
