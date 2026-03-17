# Divine Implementation Map

> **Plugin**: lib-divine
> **Schema**: schemas/divine-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/DIVINE.md](../plugins/DIVINE.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-divine |
| Layer | L4 GameFeatures |
| Endpoints | 25 (25 generated) |
| State Stores | divine-deities (MySQL), divine-blessings (MySQL), divine-attention (Redis), divine-lock (Redis) |
| Events Published | 11 (divine.deity.created, divine.deity.updated, divine.deity.deleted, divine.blessing.granted, divine.blessing.revoked, divine.divinity.credited, divine.divinity.debited, divine.follower.registered, divine.follower.removed, divine.deity.activated, divine.deity.dormant) |
| Events Consumed | 2 (character.created, character.updated — for patron deity auto-bonding) |
| Client Events | 0 |
| Background Services | 1 |

## State

**Store**: `divine-deities` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `deity:{deityId}` | `DeityModel` | Primary deity record lookup by ID |
| `deity-code:{gameServiceId}:{code}` | `DeityModel` | Code-uniqueness lookup within game service |

**Store**: `divine-blessings` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `blessing:{blessingId}` | `BlessingModel` | Primary blessing record lookup by ID |

Paginated queries by entityId+entityType or deityId+tier use `IJsonQueryableStateStore<BlessingModel>.JsonQueryPagedAsync()`.

**Store**: `divine-attention` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `attention:{deityId}:{characterId}` | `AttentionSlotModel` | Active attention slot per deity-character pair |

**Store**: `divine-lock` (Backend: Redis, via `IDistributedLockProvider`)

| Key Pattern | Purpose |
|-------------|---------|
| `divine:lock:deity:{deityId}` | Deity mutation lock |
| `divine:lock:blessing:{blessingId}` | Blessing mutation lock |
| `divine:lock:attention-worker` | Attention decay worker singleton lock |

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | 3 constructor-cached stores (deity, blessing, attention) |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Deity/blessing mutation locks, worker singleton lock |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing all 11 divine event topics |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Registering character.created and character.updated handlers (patron deity auto-bonding) |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation |
| lib-resource (`IResourceClient`) | L1 | Hard | Cleanup callback registration (character CASCADE, game-service CASCADE) |
| lib-currency (`ICurrencyClient`) | L2 | Hard | Divinity credit, debit, balance queries, transaction history |
| lib-relationship (`IRelationshipClient`) | L2 | Hard | Follower bonds (deity_follower type), rivalry bonds (deity_rivalry type) |
| lib-character (`ICharacterClient`) | L2 | Hard | Character existence validation for follower registration |
| lib-game-service (`IGameServiceClient`) | L2 | Hard | Game service existence validation during deity creation |
| lib-seed (`ISeedClient`) | L2 | Hard | Domain power seed creation, bond operations for patron deity auto-bonding |
| lib-genesis (`IGenesisClient`) | L2 | Hard | Deity entity creation via "deity_domain" genesis template with external seedId |
| lib-collection (`ICollectionClient`) | L2 | Hard | Permanent blessing grants (Greater/Supreme tier) |
| lib-status (`IStatusClient`) | L4 | Soft | Temporary blessings (Minor/Standard tier); graceful degradation if absent |
| lib-puppetmaster (`IPuppetmasterClient`) | L4 | Soft | Start/stop deity watcher actors; graceful degradation if absent |

**Notes:**
- Divine creates the deity's seed externally via `ISeedClient`, then passes the seedId to Genesis via `IGenesisClient.CreateEntityAsync(seedId: ...)`. This allows Divine to retain the seedId for seed bond operations while Genesis manages the lifecycle. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md) § Genesis Interaction.
- The deep dive describes subscribing to `genesis.entity.phase-changed` for deity lifecycle transitions. This subscription will be added when genesis is implemented.
- `IServiceProvider` is stored as `_serviceProvider` for runtime resolution of L4 soft dependencies.
- `IAnalyticsClient` has been **removed** — divinity generation flows through Seed bond propagation at L2, not Analytics events.

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `divine.deity.created` | `DeityCreatedEvent` | Deity entity created (lifecycle) |
| `divine.deity.updated` | `DeityUpdatedEvent` | Deity entity updated (lifecycle, includes changedFields) |
| `divine.deity.deleted` | `DeityDeletedEvent` | Deity entity deleted (lifecycle) |
| `divine.blessing.granted` | `DivineBlessingGrantedEvent` | A god granted a blessing to an entity |
| `divine.blessing.revoked` | `DivineBlessingRevokedEvent` | A blessing was revoked |
| `divine.divinity.credited` | `DivineDivinityCreditedEvent` | Divinity was earned (mortal action, manual credit, or batch generation) |
| `divine.divinity.debited` | `DivineDivinityDebitedEvent` | Divinity was spent (blessing grant, miracle) |
| `divine.follower.registered` | `DivineFollowerRegisteredEvent` | Character became a follower of a deity |
| `divine.follower.removed` | `DivineFollowerRemovedEvent` | Character removed as follower |
| `divine.deity.activated` | `DivineDeityActivatedEvent` | Deity became active in the world |
| `divine.deity.dormant` | `DivineDeityDormantEvent` | Deity went dormant |

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `character.created` | `HandleCharacterCreatedAsync` | If character has `patronDeityCode`, look up deity in bond template registry, auto-initiate seed bonds between character's domain seeds and patron god's matching seeds |
| `character.updated` | `HandleCharacterUpdatedAsync` | If `changedFields` includes `patronDeityCode`, dissolve old patron seed bonds (if any), look up new deity in bond template registry, auto-initiate new seed bonds |

**Future**: `genesis.entity.phase-changed` will be consumed when lib-genesis is implemented — handler will initialize attention slots at Stirring and activate divinity economy at Awakened.

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<DivineService>` | Structured logging |
| `DivineServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 3 stores: deity, blessing, attention) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription registration (character.created, character.updated) |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `IResourceClient` | Resource cleanup callback registration |
| `ICurrencyClient` | Divinity wallet operations |
| `IRelationshipClient` | Follower/rivalry bond management |
| `ICharacterClient` | Character existence validation for follower registration |
| `IGameServiceClient` | Game service existence validation |
| `ISeedClient` | Domain power seed creation, seed bond operations for patron deity auto-bonding |
| `IGenesisClient` | Deity entity creation via genesis template with external seedId |
| `ICollectionClient` | Permanent blessing grants |
| `IServiceProvider` | Runtime resolution of L4 soft dependencies |

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CreateDeity | POST /divine/deity/create | generated | [] | deity, deity-code | divine.deity.created |
| GetDeity | POST /divine/deity/get | generated | [] | - | - |
| GetDeityByCode | POST /divine/deity/get-by-code | generated | [] | - | - |
| ListDeities | POST /divine/deity/list | generated | [] | - | - |
| UpdateDeity | POST /divine/deity/update | generated | [] | deity | divine.deity.updated |
| ActivateDeity | POST /divine/deity/activate | generated | [] | deity | divine.deity.activated |
| DeactivateDeity | POST /divine/deity/deactivate | generated | [] | deity, attention | divine.deity.dormant |
| DeprecateDeity | POST /divine/deity/deprecate | generated | [] | deity | divine.deity.updated (changedFields: deprecation) |
| UndeprecateDeity | POST /divine/deity/undeprecate | generated | [] | deity | divine.deity.updated (changedFields: deprecation) |
| MergeDeity | POST /divine/deity/merge | generated | [role: admin] | deity, blessing, attention | divine.deity.updated (merged), divine.deity.deleted |
| DeleteDeity | POST /divine/deity/delete | generated | [] | deity, deity-code, blessing, attention | divine.deity.deleted |
| GetDivinityBalance | POST /divine/divinity/get-balance | generated | [] | - | - |
| CreditDivinity | POST /divine/divinity/credit | generated | [] | - | divine.divinity.credited |
| DebitDivinity | POST /divine/divinity/debit | generated | [] | - | divine.divinity.debited |
| GetDivinityHistory | POST /divine/divinity/get-history | generated | [] | - | - |
| GrantBlessing | POST /divine/blessing/grant | generated | [] | blessing | divine.blessing.granted, divine.divinity.debited |
| RevokeBlessing | POST /divine/blessing/revoke | generated | [] | blessing | divine.blessing.revoked |
| ListBlessingsByEntity | POST /divine/blessing/list-by-entity | generated | [] | - | - |
| ListBlessingsByDeity | POST /divine/blessing/list-by-deity | generated | [] | - | - |
| GetBlessing | POST /divine/blessing/get | generated | [] | - | - |
| RegisterFollower | POST /divine/follower/register | generated | [] | deity, attention | divine.follower.registered |
| UnregisterFollower | POST /divine/follower/unregister | generated | [] | deity, attention | divine.follower.removed |
| GetFollowers | POST /divine/follower/get-followers | generated | [] | - | - |
| CleanupByCharacter | POST /divine/cleanup-by-character | generated | [] | blessing, attention, deity | divine.blessing.revoked, divine.follower.removed |
| CleanupByGameService | POST /divine/cleanup-by-game-service | generated | [] | deity, deity-code, blessing, attention | divine.deity.deleted |

## Methods

### CreateDeity
POST /divine/deity/create | Roles: []

CALL _gameServiceClient.GetGameServiceAsync(body.GameServiceId)       -> 404 if not found
READ _deityStore:deity-code:{body.GameServiceId}:{body.Code}          -> 409 if exists
// Create seed externally so Divine retains seedId for bond operations
CALL _seedClient.CreateSeedAsync(seedTypeCode: config.DeitySeedTypeCode, ...) -> seedId
// Create genesis entity with external seed — Genesis adopts it for lifecycle management
CALL _genesisClient.CreateEntityAsync(templateCode: "deity_domain", seedId: seedId, ...)
  // Genesis provisions divinity wallet, inventories from template; manages seed lifecycle
WRITE _deityStore:deity:{newDeityId} <- DeityModel from request
  // Status = Dormant, FollowerCount = 0, MaxAttentionSlots from config
  // SeedId = seedId (retained for bond operations)
  // GenesisEntityId = genesis entity ID (for lifecycle queries)
WRITE _deityStore:deity-code:{body.GameServiceId}:{body.Code} <- DeityModel
// Register in bond template registry (in-memory, for patron deity auto-bonding)
PUBLISH divine.deity.created { deityId, gameServiceId, code, displayName, domains, status }
RETURN (200, DeityResponse)

### GetDeity
POST /divine/deity/get | Roles: []

READ _deityStore:deity:{body.DeityId}                                 -> 404 if null
RETURN (200, DeityResponse)

### GetDeityByCode
POST /divine/deity/get-by-code | Roles: []

QUERY _deityStore WHERE $.gameServiceId = body.GameServiceId AND $.code = body.Code
                                                                      -> 404 if empty
RETURN (200, DeityResponse)

### ListDeities
POST /divine/deity/list | Roles: []

QUERY _deityStore WHERE $.gameServiceId = body.GameServiceId
  [AND $.domainCode = body.DomainCode if provided]
  [AND $.status = body.Status if provided]
  [AND $.isDeprecated = false UNLESS body.IncludeDeprecated == true]   // T31: default excludes deprecated
  PAGED(body.Page, body.PageSize)
RETURN (200, ListDeitiesResponse)

### UpdateDeity
POST /divine/deity/update | Roles: []

LOCK divine:lock:deity:{body.DeityId}                                 -> 409 if lock fails
  READ _deityStore:deity:{body.DeityId} [with ETag]                   -> 404 if null
  // Apply non-null fields from request (partial update)
  // Note: code is IMMUTABLE after creation — not in UpdateDeityRequest
  // Updatable fields: displayName, description, domains, divineAffectations, maxAttentionSlots
  ETAG-WRITE _deityStore:deity:{body.DeityId} <- updated model
  PUBLISH divine.deity.updated { deityId, changedFields }
RETURN (200, DeityResponse)

### ActivateDeity
POST /divine/deity/activate | Roles: []

LOCK divine:lock:deity:{body.DeityId}                                 -> 409 if lock fails
  READ _deityStore:deity:{body.DeityId}                               -> 404 if null
  // Set Status = Active
  IF actorId == null
    // Soft dependency: Puppetmaster may not be available
    IF IPuppetmasterClient available
      CALL _puppetmasterClient.StartWatcherAsync(config.DeityActorTypeCode)
      // Update actorId on model
  WRITE _deityStore:deity:{body.DeityId} <- updated model
  PUBLISH divine.deity.activated { deityId, gameServiceId }
RETURN (200, DeityResponse)

### DeactivateDeity
POST /divine/deity/deactivate | Roles: []

LOCK divine:lock:deity:{body.DeityId}                                 -> 409 if lock fails
  READ _deityStore:deity:{body.DeityId}                               -> 404 if null
  IF actorId != null AND IPuppetmasterClient available
    CALL _puppetmasterClient.StopActorAsync(actorId)
  // Set Status = Dormant, clear ActorId
  // Clear all attention slots for this deity
  // Note: scan pattern attention:{deityId}:* — requires iteration or query
  WRITE _deityStore:deity:{body.DeityId} <- updated model
  PUBLISH divine.deity.dormant { deityId, gameServiceId }
RETURN (200, DeityResponse)

### DeprecateDeity
POST /divine/deity/deprecate | Roles: []

LOCK divine:lock:deity:{body.DeityId}                                 -> 409 if lock fails
  READ _deityStore:deity:{body.DeityId} [with ETag]                   -> 404 if null
  IF deity.IsDeprecated == true                                        -> 200 OK (idempotent)
  // Set IsDeprecated = true, DeprecatedAt = now, DeprecationReason = body.Reason
  ETAG-WRITE _deityStore:deity:{body.DeityId} <- updated model
  PUBLISH divine.deity.updated { deityId, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, DeityResponse)

### UndeprecateDeity
POST /divine/deity/undeprecate | Roles: []

LOCK divine:lock:deity:{body.DeityId}                                 -> 409 if lock fails
  READ _deityStore:deity:{body.DeityId} [with ETag]                   -> 404 if null
  IF deity.IsDeprecated == false                                       -> 200 OK (idempotent)
  // Clear IsDeprecated = false, DeprecatedAt = null, DeprecationReason = null
  ETAG-WRITE _deityStore:deity:{body.DeityId} <- updated model
  PUBLISH divine.deity.updated { deityId, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, DeityResponse)

### MergeDeity
POST /divine/deity/merge | Roles: [role: admin]

READ _deityStore:deity:{body.SourceDeityId}                           -> 404 if null
IF source.IsDeprecated == false                                       -> 400 (must be deprecated first)
READ _deityStore:deity:{body.TargetDeityId}                           -> 404 if null
IF target.IsDeprecated == true                                        -> 400 (cannot merge into deprecated)
// Transfer followers: query relationships, re-create pointing to target deity
CALL _relationshipClient.ListByEntityAsync(sourceDeityId, config.FollowerRelationshipTypeCode)
FOREACH follower in results (per-item error isolation)
  // Dissolve old seed bonds (source deity → follower character domain seeds)
  FOREACH seedId in source deity's domain seeds
    CALL _seedClient.DissolveBondAsync(seedId) // if bonded to this follower's seed
  // Create new follower relationship to target
  CALL _relationshipClient.CreateRelationshipAsync(targetDeityId, follower.characterId, ...)
  // Auto-bond to target deity per target's bond template
  // (same logic as patron deity auto-bonding)
// Transfer blessings: update deityId on blessing records
QUERY _blessingStore WHERE $.deityId = body.SourceDeityId
FOREACH blessing in results (per-item error isolation)
  blessing.DeityId = body.TargetDeityId
  WRITE _blessingStore:blessing:{blessingId} <- updated
// Delete source deity (calls own DeleteDeity logic)
PUBLISH divine.deity.deleted { sourceDeityId, deletedReason: "merged into {targetDeityId}" }
DELETE source deity records
RETURN (200, MergeDeprecatedResponse { mergedCount, targetId })

### DeleteDeity
POST /divine/deity/delete | Roles: []

LOCK divine:lock:deity:{body.DeityId}                                 -> 409 if lock fails
  READ _deityStore:deity:{body.DeityId}                               -> 404 if null
  IF deity.IsDeprecated == false                                       -> 400 (must deprecate first per T31)
  IF status == Active
    // Deactivate: stop watcher actor if Puppetmaster available
  // Revoke all blessings for this deity (per-item error isolation)
  QUERY _blessingStore WHERE $.deityId = body.DeityId
  FOREACH blessing in results
    // Revoke via collection or status service depending on tier
    DELETE _blessingStore:blessing:{blessingId}
  // Remove all follower relationships
  CALL _relationshipClient.DeleteByEntityAsync(deityId, config.FollowerRelationshipTypeCode)
  // Clear all attention slots
  // Coordinate lib-resource cleanup
  CALL _resourceClient.ExecuteCleanupAsync(resourceType: "deity", resourceId: deityId)
  DELETE _deityStore:deity:{body.DeityId}
  DELETE _deityStore:deity-code:{gameServiceId}:{code}
  PUBLISH divine.deity.deleted { deityId, full model snapshot }
RETURN (200, null)

### GetDivinityBalance
POST /divine/divinity/get-balance | Roles: []

READ _deityStore:deity:{body.DeityId}                                 -> 404 if null
CALL _currencyClient.GetBalanceAsync(deity.CurrencyWalletId, config.DivinityCurrencyCode)
RETURN (200, DivinityBalanceResponse)

### CreditDivinity
POST /divine/divinity/credit | Roles: []

READ _deityStore:deity:{body.DeityId}                                 -> 404 if null
CALL _currencyClient.CreditAsync(deity.CurrencyWalletId, body.Amount, config.DivinityCurrencyCode)
PUBLISH divine.divinity.credited { deityId, amount, source, sourceEventId }
RETURN (200, DivinityBalanceResponse)

### DebitDivinity
POST /divine/divinity/debit | Roles: []

READ _deityStore:deity:{body.DeityId}                                 -> 404 if null
CALL _currencyClient.DebitAsync(deity.CurrencyWalletId, body.Amount, config.DivinityCurrencyCode)
                                                                      -> 400 if insufficient
PUBLISH divine.divinity.debited { deityId, amount, purpose }
RETURN (200, DivinityBalanceResponse)

### GetDivinityHistory
POST /divine/divinity/get-history | Roles: []

READ _deityStore:deity:{body.DeityId}                                 -> 404 if null
CALL _currencyClient.GetTransactionHistoryAsync(deity.CurrencyWalletId, body.Page, body.PageSize)
RETURN (200, DivinityHistoryResponse)

### GrantBlessing
POST /divine/blessing/grant | Roles: []

READ _deityStore:deity:{body.DeityId}                                 -> 404 if null
IF deity.Status != Active                                             -> 400
// No entity existence validation — polymorphic references are caller-responsibility
// (matches Collection, Relationship, Seed, Currency, Status, Escrow pattern; #675 resolved)
COUNT _blessingStore WHERE $.entityId = body.EntityId
  AND $.entityType = body.EntityType AND $.status = Active
IF count >= config.MaxBlessingsPerEntity                              -> 409
// Calculate divinity cost from tier config
CALL _currencyClient.DebitAsync(deity.CurrencyWalletId, cost)         -> 400 if insufficient
// Compensation: if grant fails after debit, re-credit divinity (per Implementation Tenets)
IF tier is Greater or Supreme
  CALL _collectionClient.UnlockAsync(body.EntityId, body.EntityType, config.BlessingCollectionType, body.ItemTemplateCode)
ELSE // Minor or Standard
  // Soft dependency: IStatusClient resolved via _serviceProvider
  CALL _statusClient.GrantStatusAsync(body.EntityId, body.EntityType, config.BlessingStatusCategory, body.ItemTemplateCode)
WRITE _blessingStore:blessing:{newBlessingId} <- BlessingModel
PUBLISH divine.blessing.granted { deityId, entityId, entityType, blessingId, tier, itemInstanceId }
RETURN (200, BlessingResponse)

### RevokeBlessing
POST /divine/blessing/revoke | Roles: []

LOCK divine:lock:blessing:{body.BlessingId}                           -> 409 if lock fails
  READ _blessingStore:blessing:{body.BlessingId}                      -> 404 if null
  IF blessing.Status == Revoked                                       -> 409
  IF tier is Minor or Standard AND IStatusClient available
    CALL _statusClient.RevokeStatusAsync(blessing.ItemInstanceId)
  IF tier is Greater or Supreme
    CALL _collectionClient.RevokeUnlockAsync(blessing.EntityId, blessing.EntityType, blessing.ItemInstanceId)
  // Set Status = Revoked, RevokedAt = now
  WRITE _blessingStore:blessing:{body.BlessingId} <- updated model
  PUBLISH divine.blessing.revoked { blessingId, deityId, entityId, entityType, reason }
RETURN (200, BlessingResponse)

### ListBlessingsByEntity
POST /divine/blessing/list-by-entity | Roles: []

QUERY _blessingStore WHERE $.entityId = body.EntityId AND $.entityType = body.EntityType
  PAGED(body.Page, body.PageSize)
RETURN (200, ListBlessingsResponse)

### ListBlessingsByDeity
POST /divine/blessing/list-by-deity | Roles: []

QUERY _blessingStore WHERE $.deityId = body.DeityId
  [AND $.tier = body.Tier if provided]
  PAGED(body.Page, body.PageSize)
RETURN (200, ListBlessingsResponse)

### GetBlessing
POST /divine/blessing/get | Roles: []

READ _blessingStore:blessing:{body.BlessingId}                        -> 404 if null
RETURN (200, BlessingResponse)

### RegisterFollower
POST /divine/follower/register | Roles: []

READ _deityStore:deity:{body.DeityId}                                 -> 404 if null
CALL _characterClient.GetCharacterAsync(body.CharacterId)             -> 404 if not found
CALL _relationshipClient.CreateRelationshipAsync(deityId, characterId, config.FollowerRelationshipTypeCode)
// Increment FollowerCount on deity
IF FollowerCount < MaxAttentionSlots
  WRITE _attentionStore:attention:{deityId}:{characterId} <- new AttentionSlotModel
WRITE _deityStore:deity:{body.DeityId} <- updated model (FollowerCount + 1)
PUBLISH divine.follower.registered { deityId, characterId, relationshipId }
RETURN (200, FollowerResponse)

### UnregisterFollower
POST /divine/follower/unregister | Roles: []

READ _deityStore:deity:{body.DeityId}                                 -> 404 if null
CALL _relationshipClient.DeleteRelationshipAsync(deityId, characterId, config.FollowerRelationshipTypeCode)
DELETE _attentionStore:attention:{body.DeityId}:{body.CharacterId}
// Decrement FollowerCount on deity
WRITE _deityStore:deity:{body.DeityId} <- updated model (FollowerCount - 1)
PUBLISH divine.follower.removed { deityId, characterId }
RETURN (200, null)

### GetFollowers
POST /divine/follower/get-followers | Roles: []

CALL _relationshipClient.ListRelationshipsAsync(deityId, config.FollowerRelationshipTypeCode, body.Page, body.PageSize)
RETURN (200, ListFollowersResponse)

### CleanupByCharacter
POST /divine/cleanup-by-character | Roles: []

// Called by lib-resource when a character is deleted (CASCADE)
QUERY _blessingStore WHERE $.entityId = body.CharacterId AND $.entityType = Character
FOREACH blessing in results (per-item error isolation)
  // Revoke via collection or status depending on tier
  DELETE _blessingStore:blessing:{blessingId}
  PUBLISH divine.blessing.revoked { blessingId, deityId, entityId, entityType, reason: "character_deleted" }
// Remove follower relationships and attention slots across all deities
// For each affected deity: decrement FollowerCount, delete attention slot
RETURN (200, null)

### CleanupByGameService
POST /divine/cleanup-by-game-service | Roles: []

// Called by lib-resource when a game service is deleted (CASCADE)
QUERY _deityStore WHERE $.gameServiceId = body.GameServiceId
FOREACH deity in results (per-item error isolation)
  LOCK divine:lock:deity:{deity.DeityId}
    // Full delete sequence per deity: deactivate, revoke blessings,
    // remove followers, delete attention slots, delete deity record
    DELETE _deityStore:deity:{deity.DeityId}
    DELETE _deityStore:deity-code:{deity.GameServiceId}:{deity.Code}
    PUBLISH divine.deity.deleted { deityId, full model snapshot }
RETURN (200, null)

## Background Services

### DivineAttentionWorker
**Interval**: `config.AttentionWorkerIntervalSeconds` (default: 60s)
**Purpose**: Decays attention slots for inactive followers; frees capacity when impression drops below threshold

LOCK divine:lock:attention-worker                                     // singleton lock
  // Scan all attention slots
  FOREACH slot in attention slots (per-item error isolation)
    decayedImpression = ComputeDecay(slot.Impression, slot.LastInteractionAt, config.AttentionDecayIntervalMinutes)
    IF decayedImpression < config.AttentionImpressionThreshold
      DELETE _attentionStore:attention:{slot.DeityId}:{slot.CharacterId}
      // Note: FollowerCount NOT decremented — attention and follower are separate concepts
    ELSE
      WRITE _attentionStore:attention:{slot.DeityId}:{slot.CharacterId} <- updated impression

**Note**: DivineDivinityGenerationWorker has been **removed**. Divinity generation flows through Seed bond propagation at L2 — no Divine batch processing needed. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md).

## Event Handlers

### HandleCharacterCreatedAsync
**Trigger**: `character.created` event via IEventConsumer

IF event.PatronDeityCode is null → return (no patron deity)
LOOKUP bond template registry for event.PatronDeityCode
IF not found → log warning, return (deity not registered or not loaded)
READ deity by code from _deityStore
FOREACH bondTemplate in deity's bond templates (per-item error isolation)
  // Find character's seed matching bondTemplate.SeedTypeCode
  CALL _seedClient.GetSeedByOwnerAsync(event.CharacterId, bondTemplate.SeedTypeCode)
  IF character seed not found → skip (character doesn't have this domain seed yet)
  // Initiate and auto-confirm bond between character seed and deity seed
  CALL _seedClient.InitiateBondAsync(characterSeedId, deitySeedId,
    propagationDirection: bondTemplate.Direction, propagationRatio: bondTemplate.Ratio)
  CALL _seedClient.ConfirmBondAsync(bondId) // auto-confirm for service-initiated bonds

### HandleCharacterUpdatedAsync
**Trigger**: `character.updated` event via IEventConsumer

IF "patronDeityCode" NOT IN event.ChangedFields → return
// Dissolve old patron bonds if character had a previous patron
IF previousPatronDeityCode is available (from event's previous state or deity lookup)
  // Dissolve all seed bonds between character's seeds and old deity's seeds
  FOREACH old bond (per-item error isolation)
    CALL _seedClient.DissolveBondAsync(bondId)
// Create new patron bonds (same logic as HandleCharacterCreatedAsync)
IF event.PatronDeityCode is not null
  LOOKUP bond template registry, create bonds per template

## Non-Standard Implementation Patterns

#### OnRunningAsync

```
CALL DivineService.RegisterResourceCleanupCallbacksAsync(_resourceClient)
  // Registers cleanup-by-character (CASCADE) and cleanup-by-game-service (CASCADE)
  // with lib-resource for character and game-service deletion coordination

// Build bond template registry from deity records
QUERY _deityStore for all active deities
FOREACH deity → register in _bondTemplateRegistry (ConcurrentDictionary<string, BondTemplate[]>)
  // Maps deityCode → array of { seedTypeCode, propagationDirection, propagationRatio }
  // Registry invalidated via self-event-subscription on divine.deity.created/updated/deleted
```

Planned (not yet implemented — Stubs item #10): seed type registration, currency definition, relationship type registration, collection/status template registration. Pattern established by Gardener/Faction: inline definitions in `OnRunningAsync`, catch 409 for idempotent restart.
