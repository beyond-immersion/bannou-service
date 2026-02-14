# Divine Plugin Deep Dive

> **Plugin**: lib-divine
> **Schema**: schemas/divine-api.yaml
> **Version**: 1.0.0
> **State Stores**: divine-deities (MySQL), divine-blessings (MySQL), divine-attention (Redis), divine-divinity-events (Redis), divine-lock (Redis)

## Overview

Pantheon management service (L4 GameFeatures) for deity entities, divinity economy, and blessing orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver divine game mechanics: god identity is owned here, behavior runs via Actor/Puppetmaster, domain power via Seed, divinity resource via Currency, blessings via Collection/Status, and follower bonds via Relationship. Gods influence characters indirectly through the character's own Actor -- a god's Actor monitors event streams and makes decisions, but the character's Actor receives the consequences. Blessings are entity-agnostic (characters, accounts, deities, or any entity type can receive them). All endpoints are currently stubbed (return `NotImplemented`); see the implementation plan at `docs/plans/DIVINE.md` for the full specification.

---

## Composability & Architecture

**Divine actors are both puppetmasters and gardeners**: A god tending a physical realm region (spawning encounters, adjusting NPC moods, orchestrating narrative opportunities) and a god tending a player's conceptual garden space (spawning POIs, managing scenario selection, guiding discovery) are the same operation from different perspectives -- two sides of the same coin. The divine actor launched via Puppetmaster as a regional watcher also serves as the gardener behavior actor for player experience orchestration via Gardener's APIs. Whether the "space" being tended is a physical location in the game world or an abstract conceptual space (a void garden, a lobby, player housing) is a behavioral distinction encoded in the god's ABML behavior document, not a structural difference in the actor type. This means: (1) the same god (e.g., Moira/Fate) that creates emergent content in the physical world also curates which experiences reach players through their gardens, directly connecting the content flywheel to the player experience; (2) any conceptual space can potentially become a physical space and vice versa, because the transition is just the god shifting focus between garden types; (3) lib-gardener provides the tools (garden instances, POIs, scenarios, entity associations), lib-puppetmaster provides the actor lifecycle, and lib-divine provides the identity and economy of the entity doing the tending.

**Zero Arcadia-specific content**: lib-divine is a generic pantheon management service. Arcadia's 18 Old Gods are configured through behaviors and templates at deployment time, not baked into lib-divine.

**Domain codes are opaque strings**: Different games define different domains (War, Knowledge, Nature, etc.). Domain codes follow the same extensibility pattern as seed type codes, collection type codes, and relationship type codes.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Deity records (MySQL), blessing records (MySQL), attention slots (Redis), divinity event queue (Redis), distributed locks (Redis) |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events (deity created/updated/deleted), blessing events, divinity events, follower events, deity status events |
| `IDistributedLockProvider` | Concurrent modification safety for deity mutations, blessing grants/revocations |
| lib-currency (`ICurrencyClient`) | Divinity wallet creation, credit, debit, balance queries, transaction history (L2) |
| lib-relationship (`IRelationshipClient`) | Follower bonds (deity-character), rivalry bonds (deity-deity) (L2) |
| lib-character (`ICharacterClient`) | Validate character existence for follower registration (L2) |
| lib-game-service (`IGameServiceClient`) | Validate game service existence for deity scoping (L2) |
| lib-seed (`ISeedClient`) | Deity domain power seed creation and growth tracking (L2) |
| lib-collection (`ICollectionClient`) | Permanent blessing grants for Greater/Supreme tiers (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-status (`IStatusClient`) | Temporary blessing grants for Minor/Standard tiers | Temporary blessings unavailable; only permanent blessings (Greater/Supreme) work |
| lib-puppetmaster (`IPuppetmasterClient`) | Start/stop deity watcher actors on activation/deactivation | Deities have no active behavior; blessings and economy still work via API |
| lib-analytics (`IAnalyticsClient`) | Domain-relevant score queries for divinity generation | Divinity generation from analytics events disabled; manual credit still works |

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Divine is a new L4 service with no current consumers. Future dependents may include Variable Provider Factory implementations for ABML behavior expressions (`${divine.*}`) |

## State Storage

### Deity Store
**Store**: `divine-deities` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `deity:{deityId}` | `DeityModel` | Primary lookup by ID |
| `deity-code:{gameServiceId}:{code}` | `DeityModel` | Code-uniqueness lookup within game service |

### Blessing Store
**Store**: `divine-blessings` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `blessing:{blessingId}` | `BlessingModel` | Primary lookup by ID |

Paginated queries by entityId+entityType or deityId+tier use `IJsonQueryableStateStore<BlessingModel>.JsonQueryPagedAsync()`.

### Attention Store
**Store**: `divine-attention` (Backend: Redis, prefix: `divine:attention`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `attention:{deityId}:{characterId}` | `AttentionSlotModel` | Active attention slot per deity-character pair |

### Divinity Event Queue
**Store**: `divine-divinity-events` (Backend: Redis, prefix: `divine:divevt`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `divevt:{eventId}` | `DivinityEventModel` | Pending divinity generation event awaiting batch processing |

### Distributed Locks
**Store**: `divine-lock` (Backend: Redis, prefix: `divine:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `divine:lock:deity:{deityId}` | Deity mutation lock |
| `divine:lock:blessing:{blessingId}` | Blessing mutation lock |
| `divine:lock:attention-worker` | Attention decay worker singleton lock |
| `divine:lock:divinity-generation-worker` | Divinity generation worker singleton lock |

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `deity.created` | `DeityCreatedEvent` | Deity entity created (lifecycle) |
| `deity.updated` | `DeityUpdatedEvent` | Deity entity updated (lifecycle) |
| `deity.deleted` | `DeityDeletedEvent` | Deity entity deleted (lifecycle) |
| `divine.blessing.granted` | `DivineBlessingGrantedEvent` | A god granted a blessing to an entity |
| `divine.blessing.revoked` | `DivineBlessingRevokedEvent` | A blessing was revoked |
| `divine.divinity.credited` | `DivineDivinityCreditedEvent` | Divinity was earned (mortal action in domain, manual credit) |
| `divine.divinity.debited` | `DivineDivinityDebitedEvent` | Divinity was spent (blessing, miracle) |
| `divine.follower.registered` | `DivineFollowerRegisteredEvent` | Character became a follower of a deity |
| `divine.follower.removed` | `DivineFollowerRemovedEvent` | Character removed as follower |
| `divine.deity.activated` | `DivineDeityActivatedEvent` | Deity became active in the world |
| `divine.deity.dormant` | `DivineDeityDormantEvent` | Deity went dormant |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `analytics.score.updated` | `HandleAnalyticsScoreUpdated` | Maps analytics categories to domain codes, queues divinity generation events for domain-relevant deities (soft dependency -- no-op if Analytics disabled) |

### Resource Cleanup (T28)

Character and game-service deletion cleanup is handled via `x-references` cleanup endpoints, NOT via event subscriptions.

| Trigger | Cleanup Endpoint | Action |
|---------|------------------|--------|
| Character deleted | `/divine/cleanup-by-character` | Revoke all blessings targeting this character, remove follower relationships from all deities, update follower counts, clear attention slots |
| Game service deleted | `/divine/cleanup-by-game-service` | Delete all deities for the game service along with their blessings, followers, attention slots, and associated resources |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DivinityCurrencyCode` | `DIVINE_DIVINITY_CURRENCY_CODE` | `divinity` | Currency code for divinity economy within each game service |
| `DivinityCostMinor` | `DIVINE_DIVINITY_COST_MINOR` | `10.0` | Divinity cost for Minor tier blessing |
| `DivinityCostStandard` | `DIVINE_DIVINITY_COST_STANDARD` | `50.0` | Divinity cost for Standard tier blessing |
| `DivinityCostGreater` | `DIVINE_DIVINITY_COST_GREATER` | `200.0` | Divinity cost for Greater tier blessing |
| `DivinityCostSupreme` | `DIVINE_DIVINITY_COST_SUPREME` | `1000.0` | Divinity cost for Supreme tier blessing |
| `DivinityGenerationMultiplier` | `DIVINE_DIVINITY_GENERATION_MULTIPLIER` | `1.0` | Global multiplier for all divinity generation from mortal actions |
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
| `DivinityGenerationWorkerIntervalSeconds` | `DIVINE_DIVINITY_GENERATION_WORKER_INTERVAL_SECONDS` | `30` | Seconds between divinity generation worker cycles |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<DivineService>` | Structured logging |
| `DivineServiceConfiguration` | Typed configuration access (18 properties) |
| `IStateStoreFactory` | State store access (creates 5 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `ICurrencyClient` | Divinity wallet management (L2 hard) |
| `IRelationshipClient` | Follower/rivalry bond management (L2 hard) |
| `ICharacterClient` | Character existence validation (L2 hard) |
| `IGameServiceClient` | Game service existence validation (L2 hard) |
| `ISeedClient` | Deity domain power seed management (L2 hard) |
| `ICollectionClient` | Permanent blessing grants (L2 hard) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

### Background Workers

| Worker | Purpose | Interval Config | Lock Key |
|--------|---------|-----------------|----------|
| `DivineAttentionWorker` | Decays attention slots for inactive followers, freeing capacity for new characters | `AttentionWorkerIntervalSeconds` (60s) | `divine:lock:attention-worker` |
| `DivineDivinityGenerationWorker` | Drains pending divinity events from Redis, aggregates by deity, credits currency wallets in batches | `DivinityGenerationWorkerIntervalSeconds` (30s) | `divine:lock:divinity-generation-worker` |

Both workers acquire distributed locks before processing to ensure multi-instance safety (only one instance processes at a time).

## API Endpoints (Implementation Notes)

**Current status**: All 22 endpoints return `NotImplemented`. Implementation plan at `docs/plans/DIVINE.md`.

### Deity Management (8 endpoints)

All endpoints require `developer` role.

- **Create** (`/divine/deity/create`): Validates game service existence, code uniqueness per game service. Provisions divinity currency wallet via `ICurrencyClient`, domain power seed via `ISeedClient`, and optionally starts a deity watcher actor via `IPuppetmasterClient` (soft). Saves under both ID and code lookup keys.
- **Get** (`/divine/deity/get`): Load from MySQL by deityId. 404 if not found.
- **GetByCode** (`/divine/deity/get-by-code`): JSON query by gameServiceId + code. 404 if not found.
- **List** (`/divine/deity/list`): Paged JSON query with required gameServiceId filter, optional domainCode and status filters.
- **Update** (`/divine/deity/update`): Acquires distributed lock. Partial update -- only non-null fields applied. Publishes lifecycle updated event.
- **Activate** (`/divine/deity/activate`): Lock, set status Active. If actorId is null and Puppetmaster available, start watcher. Publishes `divine.deity.activated` event.
- **Deactivate** (`/divine/deity/deactivate`): Lock, set status Dormant. If Puppetmaster available, stop watcher. Clears all attention slots. Publishes `divine.deity.dormant` event.
- **Delete** (`/divine/deity/delete`): Lock. Deactivate if active. Revoke all blessings. Remove all follower relationships. Delete attention slots. Coordinate cleanup via lib-resource. Delete deity record. Publishes lifecycle deleted event.

### Divinity Economy (4 endpoints)

All endpoints require `developer` role. All operations proxy through `ICurrencyClient` using the deity's `currencyWalletId`.

- **GetBalance** (`/divine/divinity/get-balance`): Load deity, get walletId, query `ICurrencyClient.GetBalanceAsync`.
- **Credit** (`/divine/divinity/credit`): Validate deity exists, credit wallet. Publishes `divine.divinity.credited` event.
- **Debit** (`/divine/divinity/debit`): Validate deity exists, validate sufficient balance, debit wallet. Publishes `divine.divinity.debited` event.
- **GetHistory** (`/divine/divinity/get-history`): Load deity, get walletId, query `ICurrencyClient.GetTransactionHistoryAsync`.

### Blessing Orchestration (5 endpoints)

All endpoints require `developer` role. Blessings are entity-agnostic (entityId + entityType polymorphism).

- **Grant** (`/divine/blessing/grant`): Full ceremony -- validate deity is Active, validate entity exists, check blessing count < `MaxBlessingsPerEntity`, calculate divinity cost from tier config, debit divinity, grant via lib-collection (Greater/Supreme) or Status Inventory (Minor/Standard), create BlessingModel record. Publishes `divine.blessing.granted` event.
- **Revoke** (`/divine/blessing/revoke`): Lock. For status-type blessings: remove status item. For permanent blessings: mark revoked in collection. Update BlessingModel with revocation timestamp. Publishes `divine.blessing.revoked` event.
- **ListByEntity** (`/divine/blessing/list-by-entity`): Paged JSON query on `divine-blessings` by entityId + entityType.
- **ListByDeity** (`/divine/blessing/list-by-deity`): Paged JSON query on `divine-blessings` by deityId, optional tier filter.
- **Get** (`/divine/blessing/get`): Load from MySQL by blessingId. 404 if not found.

### Follower Management (3 endpoints)

All endpoints require `developer` role. Followers are always characters (not entity-agnostic).

- **Register** (`/divine/follower/register`): Validate deity and character exist. Create relationship via `IRelationshipClient` (type: `FollowerRelationshipTypeCode`). Increment deity FollowerCount. Add to attention slots if capacity available. Publishes `divine.follower.registered` event.
- **Unregister** (`/divine/follower/unregister`): Delete relationship. Decrement FollowerCount. Remove from attention slots. Publishes `divine.follower.removed` event.
- **GetFollowers** (`/divine/follower/get-followers`): Query relationships by deityId and type via `IRelationshipClient`. Paginate results.

### Resource Cleanup (2 endpoints)

All endpoints require `developer` role. Called by lib-resource when referenced entities are deleted (FOUNDATION TENETS: Resource-Managed Cleanup).

- **CleanupByCharacter** (`/divine/cleanup-by-character`): Revoke all blessings where entityType=character and entityId matches. Remove follower relationships. Update follower counts. Clear attention slots.
- **CleanupByGameService** (`/divine/cleanup-by-game-service`): Query all deities for the gameServiceId. For each: deactivate, revoke blessings, remove followers, delete deity record.

## Visual Aid

```
┌──────────────────────────────────────────────────────────────────────┐
│                     Divine Service Composability                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  lib-divine (L4) ── "What a god IS"                                  │
│  ┌──────────────┐                                                    │
│  │ DeityModel    │──── identity, domains, personality, status         │
│  │ BlessingModel │──── blessing records linking deities to entities   │
│  │ AttentionSlot │──── which characters a god is "watching"           │
│  └──────┬───────┘                                                    │
│         │ orchestrates                                                │
│         ▼                                                            │
│  ┌─────────────────────────────────────────────────────────────┐     │
│  │ Existing Primitives (L0/L1/L2)                               │     │
│  │                                                               │     │
│  │  Currency ──── divinity wallets (credit/debit/balance)        │     │
│  │  Seed ──────── domain power growth (progressive influence)    │     │
│  │  Relationship ─ follower bonds (deity↔character)              │     │
│  │  Collection ── permanent blessings (Greater/Supreme)          │     │
│  └─────────────────────────────────────────────────────────────┘     │
│         │ soft dependencies (L4)                                      │
│         ▼                                                            │
│  ┌─────────────────────────────────────────────────────────────┐     │
│  │ Optional Features (L4, graceful degradation)                  │     │
│  │                                                               │     │
│  │  Puppetmaster ─ deity watcher actor lifecycle                 │     │
│  │  Status ─────── temporary blessings (Minor/Standard)          │     │
│  │  Analytics ──── domain-relevant divinity generation            │     │
│  └─────────────────────────────────────────────────────────────┘     │
│                                                                      │
│  Background Workers                                                  │
│  ┌─────────────────────┐  ┌───────────────────────────┐             │
│  │ AttentionWorker      │  │ DivinityGenerationWorker   │             │
│  │ Decays idle slots    │  │ Batches events → credits   │             │
│  │ Frees capacity       │  │ Aggregates per deity       │             │
│  └─────────────────────┘  └───────────────────────────┘             │
│                                                                      │
│  God Influence Paths (Two Sides of the Same Coin)                    │
│                                                                      │
│  Realm-Tending (via Puppetmaster):                                   │
│  God's Actor monitors realm events → decides → publishes             │
│  Character's Actor consumes consequences → adjusts behavior          │
│  (gods act through intermediaries, never directly)                    │
│                                                                      │
│  Garden-Tending (via Gardener):                                      │
│  God's Actor monitors player drift/events → decides → calls          │
│  Gardener APIs (spawn POI, manage transitions, shift bindings)       │
│  (same actor, same decision-making, different toolbox)               │
└──────────────────────────────────────────────────────────────────────┘
```

## Stubs & Unimplemented Features

All 22 endpoints are currently stubbed (return `NotImplemented`). The following are planned but not yet implemented:

1. **All deity CRUD operations**: Create, get, get-by-code, list, update, activate, deactivate, delete
2. **All divinity economy operations**: Get balance, credit, debit, get history
3. **All blessing orchestration**: Grant, revoke, list-by-entity, list-by-deity, get
4. **All follower management**: Register, unregister, get followers
5. **All cleanup endpoints**: Cleanup-by-character, cleanup-by-game-service
6. **Background workers**: Attention decay worker and divinity generation worker
7. **Event handler**: `HandleAnalyticsScoreUpdated` (analytics score -> divinity generation)
8. **Plugin startup registration**: Seed type, currency definition, relationship types, collection/status templates

## Potential Extensions

1. **Holy Magic Invocations (via Contract)**: Short-lived micro-contracts between deity and caster for spell invocations. Each invocation creates a Contract instance, the deity's Actor evaluates worthiness, and the Contract resolves with success/failure.
2. **DanMachi Leveling Gate**: `ILevelGateProviderFactory` interface where Greater Blessings serve as rank-up authorization. Deferred until Character has a leveling mechanic.
3. **God Personality Simulation**: ABML behavior documents modeling god attention patterns, jealousy mechanics, and inter-deity politics. A behavior authoring task, not a service implementation task.
4. **Domain Contests**: When two gods share domain influence, divinity generation splits by relative power with challenge mechanics.
5. **Deity-Deity Rivalries**: Active rivalry mechanics where gods sabotage each other's followers. Relationship records exist; behavioral consequences are deferred.
6. **Client Events**: `divine-client-events.yaml` for pushing blessing notifications, divine attention alerts, and divinity milestones to connected WebSocket clients.
7. **Variable Provider Factory**: `IDivineVariableProviderFactory` for ABML behavior expressions (`${divine.blessing_tier}`, `${divine.patron_deity}`, `${divine.divinity_earned}`).
8. **Economic Deity Behaviors**: Specialized ABML behavior documents for economic deities that monitor money velocity via analytics, spawn narrative intervention events (business opportunities, dropped wallets, thefts, treasure discoveries) to maintain healthy velocity, and respect location-level stagnation policies. God personalities (subtlety, chaos affinity, favored targets) modulate intervention style and frequency. GOAP flows evaluate velocity thresholds, hoarding detection, and intervention cooldowns. See [Economy System Guide](../guides/ECONOMY-SYSTEM.md#5-divine-economic-intervention) for the full design.
9. **Deity Realm Economic Assignment**: Track which realms each economic deity watches, with per-deity personality parameters (intervention frequency, subtlety, favored targets, chaos affinity) that affect how they maintain economic health. Multiple gods per realm creates emergent economic dynamics from competing intervention styles.

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Entity-agnostic blessings**: Unlike the original plan which was character-specific, the implemented schema uses `entityId` + `entityType` polymorphism for blessings. Characters, accounts, deities, or any entity type can receive blessings, matching the entity-agnostic patterns of lib-collection and lib-status.

2. **Followers are character-only**: While blessings are entity-agnostic, followers are always characters (RegisterFollowerRequest takes `characterId`, not `entityId`+`entityType`). This is intentional -- only characters can be "watched" by gods in the attention system.

3. **Domain codes are opaque strings, not enums**: Different games define different domains. Domain codes follow the same extensibility pattern as seed type codes, collection type codes, and relationship type codes -- extensible without schema changes.

4. **Dual-tier blessing storage**: Greater/Supreme blessings are permanent unlocks via lib-collection. Minor/Standard blessings are temporary status items via Status Inventory with contract-based lifecycle. The tier determines the storage mechanism, and lib-divine owns the `BlessingModel` record linking the two.

5. **Divinity is a shared currency type**: One "divinity" currency definition per game service, but wallets are per-entity. Gods AND humans can hold divinity -- it's used differently but is the same currency type. God-to-god divinity transfers work naturally as Currency transfers.

6. **Personality traits excluded from lifecycle events**: `personalityTraits` is marked as sensitive in `x-lifecycle`, so it is excluded from `DeityCreatedEvent`, `DeityUpdatedEvent`, and `DeityDeletedEvent`. This prevents leaking internal simulation data through broadcast events.

7. **Batched divinity generation**: The `HandleAnalyticsScoreUpdated` handler does not credit divinity immediately. It queues `DivinityEventModel` entries in the Redis event store, which the `DivineDivinityGenerationWorker` processes in batches. This prevents high-frequency analytics events from overwhelming the currency service with individual credit calls.

### Design Considerations (Requires Planning)

1. **Domain-to-analytics mapping**: The `HandleAnalyticsScoreUpdated` handler needs a mapping from analytics categories to domain codes. The plan suggests either a config property (`DomainAnalyticsMappings` as JSON string) or a dedicated mapping state store. This must be resolved during implementation.

2. **Blessing template management on startup**: `OnRunningAsync` should create collection entry templates and status templates if they don't exist. The exact set of templates depends on game configuration, not lib-divine itself.

3. **No owner validation for blessings**: Entity existence validation depends on the `entityType` -- the service would need to resolve the correct client dynamically. The plan validates characters via `ICharacterClient`, but entity-agnostic blessings may need a pluggable validation strategy.

## Work Tracking

*No active work items. All endpoints are stubbed pending implementation per `docs/plans/DIVINE.md`.*
