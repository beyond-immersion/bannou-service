# Faction Implementation Map

> **Plugin**: lib-faction
> **Schema**: schemas/faction-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/FACTION.md](../plugins/FACTION.md)

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-faction |
| Layer | L4 GameFeatures |
| Endpoints | 37 |
| State Stores | faction-statestore (MySQL), faction-membership-statestore (MySQL), faction-territory-statestore (MySQL), faction-norm-statestore (MySQL), faction-governance-statestore (MySQL), faction-cache (Redis), faction-lock (Redis) |
| Events Published | 16 (faction.created, faction.updated, faction.deleted, faction.member.added, faction.member.removed, faction.member.role-changed, faction.territory.claimed, faction.territory.released, faction.norm.defined, faction.norm.updated, faction.norm.deleted, faction.realm-baseline.designated, faction.governance.defined, faction.governance.deleted, faction.authority.delegated, faction.authority.revoked) |
| Events Consumed | 0 (DI listeners only) |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `faction-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `fac:{factionId}` | `FactionModel` | Primary lookup by ID |
| `fac:{gameServiceId}:{code}` | `FactionModel` | Code-uniqueness lookup within game service scope |

**Store**: `faction-membership-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `mem:{factionId}:{characterId}` | `FactionMemberModel` | Primary lookup by faction + character (uniqueness) |
| `mem:char:{characterId}` | `MembershipListModel` | All memberships for a character (cross-faction query) |

**Store**: `faction-territory-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tcl:{claimId}` | `TerritoryClaimModel` | Primary lookup by claim ID |
| `tcl:loc:{locationId}` | `TerritoryClaimModel` | Controlling faction lookup by location |
| `tcl:fac:{factionId}` | `TerritoryClaimListModel` | All territory claims for a faction |

**Store**: `faction-norm-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `nrm:{normId}` | `NormDefinitionModel` | Primary lookup by norm ID |
| `nrm:fac:{factionId}` | `NormListModel` | All norms for a faction |

**Store**: `faction-governance-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `gov:{governanceId}` | `GovernanceEntryModel` | Primary lookup by governance entry ID |
| `gov:fac:{factionId}` | `GovernanceEntryListModel` | All governance entries for a faction |
| `gov:fac:{factionId}:dom:{domain}` | `GovernanceEntryModel` | Domain uniqueness lookup within a faction |

**Store**: `faction-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ncache:{characterId}:{locationId\|"none"}` | `ResolvedNormCacheModel` | Cached resolved norm set per character+location (with TTL) |
| `govcache:{locationId}:{domain}` | `CachedGovernanceResolution` | Cached governance resolution per location+domain (with TTL) |

**Store**: `faction-lock` (Backend: Redis)

Distributed locks for faction, membership, territory, norm, and governance mutations.

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 5 MySQL stores + Redis cache + Redis locks |
| lib-state (IDistributedLockProvider) | L0 | Hard | Locks on all mutation operations |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 16 event topics |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on helpers/providers |
| lib-resource (IResourceClient) | L1 | Hard | Reference tracking (character, location), cleanup callbacks, compression callbacks |
| lib-seed (ISeedClient) | L2 | Hard | Seed creation, capability queries, growth recording, seed type registration |
| lib-location (ILocationClient) | L2 | Hard | Location existence validation for territory claims |
| lib-realm (IRealmClient) | L2 | Hard | Realm existence validation for faction creation |
| lib-game-service (IGameServiceClient) | L2 | Hard | Game service existence validation for faction creation |

**DI listeners (L2→L4 push)**: ISeedEvolutionListener (phase change updates), ICollectionUnlockListener (member activity → seed growth).

**DI provider (L4→L2 pull)**: IVariableProviderFactory (`${faction.*}` namespace for Actor runtime).

**No event subscriptions**: All cross-service integration uses DI listener/provider patterns. Resource cleanup uses `x-references` callbacks.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `faction.created` | `FactionCreatedEvent` | CreateFaction, SeedFactions |
| `faction.updated` | `FactionUpdatedEvent` | UpdateFaction, DeprecateFaction, UndeprecateFaction, DesignateRealmBaseline, FactionSeedEvolutionListener.OnPhaseChanged |
| `faction.deleted` | `FactionDeletedEvent` | DeleteFaction |
| `faction.member.added` | `FactionMemberAddedEvent` | AddMember |
| `faction.member.removed` | `FactionMemberRemovedEvent` | RemoveMember, DeleteFaction (cascade), CleanupByCharacter |
| `faction.member.role-changed` | `FactionMemberRoleChangedEvent` | UpdateMemberRole |
| `faction.territory.claimed` | `FactionTerritoryClaimedEvent` | ClaimTerritory |
| `faction.territory.released` | `FactionTerritoryReleasedEvent` | ReleaseTerritory, DeleteFaction (cascade) |
| `faction.norm.defined` | `FactionNormDefinedEvent` | DefineNorm |
| `faction.norm.updated` | `FactionNormUpdatedEvent` | UpdateNorm |
| `faction.norm.deleted` | `FactionNormDeletedEvent` | DeleteNorm, DeleteFaction (cascade) |
| `faction.realm-baseline.designated` | `FactionRealmBaselineDesignatedEvent` | DesignateRealmBaseline |
| `faction.governance.defined` | `FactionGovernanceDefinedEvent` | SetGovernanceEntry |
| `faction.governance.deleted` | `FactionGovernanceDeletedEvent` | RemoveGovernanceEntry, RevokeAuthority (cascade), DeleteFaction (cascade) |
| `faction.authority.delegated` | `FactionAuthorityDelegatedEvent` | DelegateAuthority |
| `faction.authority.revoked` | `FactionAuthorityRevokedEvent` | RevokeAuthority |

---

## Events Consumed

This plugin does not consume external events. Cross-service integration uses DI listener patterns:

| Pattern | Interface | Action |
|---------|-----------|--------|
| Seed phase changes | `ISeedEvolutionListener` | Updates faction `CurrentPhase`, publishes `faction.updated` |
| Collection entry unlocks | `ICollectionUnlockListener` | Converts `faction:*` tags into seed growth via `ISeedClient.RecordGrowthBatchAsync` |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<FactionService>` | Structured logging |
| `FactionServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (not stored as field — used in constructor only) |
| `IMessageBus` | Event publishing |
| `IResourceClient` | Reference tracking and cleanup/compression callbacks |
| `ISeedClient` | Seed creation, capability queries, growth recording |
| `ILocationClient` | Location existence validation |
| `IRealmClient` | Realm existence validation |
| `IGameServiceClient` | Game service existence validation |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `ITelemetryProvider` | Span instrumentation |
| `IEventConsumer` | Event consumer registration (empty — no subscriptions) |

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `IVariableProviderFactory` | `Singleton` (FactionProviderFactory) | L4→L2 pull | Actor (L2) discovers `${faction.*}` variables |
| `ISeedEvolutionListener` | `Singleton` (FactionSeedEvolutionListener) | L2→L4 push | Seed (L2) dispatches phase/growth/capability notifications |
| `ICollectionUnlockListener` | `Singleton` (FactionCollectionUnlockListener) | L2→L4 push | Collection (L2) dispatches entry unlock notifications |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CreateFaction | POST /faction/create | generated | developer | faction, faction-code | faction.created |
| GetFaction | POST /faction/get | generated | user | - | - |
| GetFactionByCode | POST /faction/get-by-code | generated | user | - | - |
| ListFactions | POST /faction/list | generated | user | - | - |
| UpdateFaction | POST /faction/update | generated | developer | faction, faction-code | faction.updated |
| DeprecateFaction | POST /faction/deprecate | generated | developer | faction, faction-code | faction.updated |
| UndeprecateFaction | POST /faction/undeprecate | generated | developer | faction, faction-code | faction.updated |
| DeleteFaction | POST /faction/delete | generated | admin | faction, faction-code, members, territory, norms, governance | faction.deleted, faction.member.removed, faction.territory.released, faction.norm.deleted, faction.governance.deleted |
| SeedFactions | POST /faction/seed | generated | admin | faction, faction-code | faction.created |
| DesignateRealmBaseline | POST /faction/designate-realm-baseline | generated | developer | faction, faction-code, norm-cache | faction.realm-baseline.designated, faction.updated |
| GetRealmBaseline | POST /faction/get-realm-baseline | generated | user | - | - |
| AddMember | POST /faction/member/add | generated | user | member, member-list, faction, norm-cache | faction.member.added |
| RemoveMember | POST /faction/member/remove | generated | user | member, member-list, faction, norm-cache | faction.member.removed |
| ListMembers | POST /faction/member/list | generated | user | - | - |
| ListMembershipsByCharacter | POST /faction/member/list-by-character | generated | user | - | - |
| UpdateMemberRole | POST /faction/member/update-role | generated | user | member, member-list | faction.member.role-changed |
| CheckMembership | POST /faction/member/check | generated | user | - | - |
| ClaimTerritory | POST /faction/territory/claim | generated | developer | territory, territory-loc, territory-list, norm-cache | faction.territory.claimed |
| ReleaseTerritory | POST /faction/territory/release | generated | developer | territory, territory-loc, territory-list, norm-cache | faction.territory.released |
| ListTerritoryClaims | POST /faction/territory/list | generated | user | - | - |
| GetControllingFaction | POST /faction/territory/get-controlling | generated | user | - | - |
| DefineNorm | POST /faction/norm/define | generated | developer | norm, norm-list, norm-cache | faction.norm.defined |
| UpdateNorm | POST /faction/norm/update | generated | developer | norm, norm-cache | faction.norm.updated |
| DeleteNorm | POST /faction/norm/delete | generated | developer | norm, norm-list, norm-cache | faction.norm.deleted |
| ListNorms | POST /faction/norm/list | generated | user | - | - |
| QueryApplicableNorms | POST /faction/norm/query-applicable | generated | [] | norm-cache | - |
| SetGovernanceEntry | POST /faction/governance/set | generated | developer | governance, governance-list, governance-domain | faction.governance.defined |
| RemoveGovernanceEntry | POST /faction/governance/remove | generated | developer | governance, governance-list, governance-domain | faction.governance.deleted |
| ListGovernanceEntries | POST /faction/governance/list | generated | user | - | - |
| QueryGovernanceData | POST /faction/governance/query | generated | [] | governance-cache | - |
| DelegateAuthority | POST /faction/governance/delegate | generated | developer | faction, faction-code | faction.authority.delegated, faction.updated |
| RevokeAuthority | POST /faction/governance/revoke | generated | developer | faction, faction-code, governance, governance-list | faction.authority.revoked, faction.updated, faction.governance.deleted |
| CleanupByCharacter | POST /faction/cleanup-by-character | generated | [] | member, member-list, faction, norm-cache | faction.member.removed |
| CleanupByRealm | POST /faction/cleanup-by-realm | generated | [] | faction (all), members, territory, norms, governance | faction.deleted, faction.member.removed, faction.territory.released |
| CleanupByLocation | POST /faction/cleanup-by-location | generated | [] | territory, territory-loc, territory-list, norm-cache | faction.territory.released |
| GetCompressData | POST /faction/get-compress-data | generated | [] | - | - |
| RestoreFromArchive | POST /faction/restore-from-archive | generated | [] | member, member-list, faction | - |

---

## Methods

### CreateFaction
POST /faction/create | Roles: [developer]

CALL _gameServiceClient.GetServiceAsync({ ServiceId })            -> 4xx passthrough if ApiException
CALL _realmClient.RealmExistsAsync({ RealmId })                   -> 4xx passthrough if ApiException
READ faction-store:BuildFactionCodeKey(gameServiceId, code)       -> 409 if exists
IF parentFactionId specified
  READ faction-store:BuildFactionKey(parentFactionId)             -> 400 if null
  // Walk hierarchy chain up to MaxHierarchyDepth
  WHILE current.ParentFactionId != null
    READ faction-store:BuildFactionKey(current.ParentFactionId)
    depth++                                                       -> 400 if depth >= MaxHierarchyDepth
CALL _seedClient.CreateSeedAsync({ OwnerId, OwnerType: Faction, SeedTypeCode, GameServiceId, DisplayName })
  // ApiException → passthrough; generic Exception → error event + 500
WRITE faction-store:BuildFactionKey(factionId) <- FactionModel from request
WRITE faction-store:BuildFactionCodeKey(gameServiceId, code) <- FactionModel
PUBLISH faction.created { full faction data }
RETURN (200, FactionResponse)

---

### GetFaction
POST /faction/get | Roles: [user]

READ faction-store:BuildFactionKey(factionId)                     -> 404 if null
RETURN (200, FactionResponse)

---

### GetFactionByCode
POST /faction/get-by-code | Roles: [user]

READ faction-store:BuildFactionCodeKey(gameServiceId, code)       -> 404 if null
RETURN (200, FactionResponse)

---

### ListFactions
POST /faction/list | Roles: [user]

QUERY faction-query-store WHERE $.FactionId EXISTS
  AND (optional) $.GameServiceId = gameServiceId
  AND (optional) $.RealmId = realmId
  AND (optional) $.Status = status
  AND (optional) $.ParentFactionId = parentFactionId
  AND (optional if topLevelOnly) $.ParentFactionId NOT EXISTS
  AND (optional) $.IsRealmBaseline = isRealmBaseline
  AND (if !includeDeprecated) $.IsDeprecated = "false"
  PAGED(cursorOffset, pageSize)
RETURN (200, ListFactionsResponse { Factions, NextCursor, HasMore })

---

### UpdateFaction
POST /faction/update | Roles: [developer]

LOCK faction-lock:"faction:{factionId}"                           -> 409 if fails
  READ faction-store:BuildFactionKey(factionId)                   -> 404 if null
  // Partial update: name, code, description (null fields skipped)
  IF code changed
    READ faction-store:BuildFactionCodeKey(gameServiceId, newCode) -> 409 if taken by different faction
  IF changedFields.Count == 0
    RETURN (200, FactionResponse)                                 // no-op
  WRITE faction-store:BuildFactionKey(factionId) <- updated model
  WRITE faction-store:BuildFactionCodeKey(gameServiceId, code) <- updated model
  IF code changed
    DELETE faction-store:BuildFactionCodeKey(gameServiceId, oldCode)
  PUBLISH faction.updated { changedFields }
RETURN (200, FactionResponse)

---

### DeprecateFaction
POST /faction/deprecate | Roles: [developer]

LOCK faction-lock:"faction:{factionId}"                           -> 409 if fails
  READ faction-store:BuildFactionKey(factionId)                   -> 404 if null
  IF status == Dissolved                                          -> 409
  IF already deprecated                                           -> 200 (idempotent)
  // Set IsDeprecated, DeprecatedAt, DeprecationReason
  WRITE faction-store:BuildFactionKey(factionId) <- updated model
  WRITE faction-store:BuildFactionCodeKey(gameServiceId, code) <- updated model
  PUBLISH faction.updated { changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, FactionResponse)

---

### UndeprecateFaction
POST /faction/undeprecate | Roles: [developer]

LOCK faction-lock:"faction:{factionId}"                           -> 409 if fails
  READ faction-store:BuildFactionKey(factionId)                   -> 404 if null
  IF status == Dissolved                                          -> 409
  IF not deprecated                                               -> 200 (idempotent)
  // Clear IsDeprecated, DeprecatedAt, DeprecationReason
  WRITE faction-store:BuildFactionKey(factionId) <- updated model
  WRITE faction-store:BuildFactionCodeKey(gameServiceId, code) <- updated model
  PUBLISH faction.updated { changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, FactionResponse)

---

### DeleteFaction
POST /faction/delete | Roles: [admin]

LOCK faction-lock:"faction:{factionId}"                           -> 409 if fails
  READ faction-store:BuildFactionKey(factionId)                   -> 404 if null
  IF not deprecated                                               -> 400 (Category A lifecycle)
  // Cascade: members (paginated loop with per-item try-catch)
  FOREACH page of members (QUERY member-query-store WHERE $.FactionId = factionId PAGED(offset, SeedBulkPageSize))
    FOREACH member
      CALL RemoveMemberInternalAsync(factionId, characterId)      // per-item try-catch
  // Cascade: territory claims
  READ territory-list-store:BuildFactionClaimsKey(factionId)
  FOREACH claimId in list
    READ territory-store:BuildClaimKey(claimId)
    IF active: CALL ReleaseTerritoryInternalAsync(claim)          // per-item try-catch
  DELETE territory-list-store:BuildFactionClaimsKey(factionId)
  // Cascade: norms (publish events per FOUNDATION TENETS)
  READ norm-list-store:BuildFactionNormsKey(factionId)
  FOREACH normId in list
    READ norm-store:BuildNormKey(normId)
    DELETE norm-store:BuildNormKey(normId)
    PUBLISH faction.norm.deleted { factionId, normId, violationType }  // per-item try-catch
  DELETE norm-list-store:BuildFactionNormsKey(factionId)
  // Cascade: governance entries (publish events per FOUNDATION TENETS)
  READ governance-list-store:BuildFactionGovernanceKey(factionId)
  FOREACH govId in list
    READ governance-store:BuildGovernanceKey(govId)
    DELETE governance-store:BuildFactionGovernanceDomainKey(factionId, domain)
    DELETE governance-store:BuildGovernanceKey(govId)
    PUBLISH faction.governance.deleted { factionId, govId, domain }    // per-item try-catch
  DELETE governance-list-store:BuildFactionGovernanceKey(factionId)
  // Delete faction itself
  DELETE faction-store:BuildFactionKey(factionId)
  DELETE faction-store:BuildFactionCodeKey(gameServiceId, code)
  PUBLISH faction.deleted { full faction data }
RETURN (200, null)

---

### SeedFactions
POST /faction/seed | Roles: [admin]

// Pass 1: Create factions without parent resolution
FOREACH def in request.factions
  READ faction-store:BuildFactionCodeKey(gameServiceId, def.Code)
  IF exists → increment skipped, continue
  CALL _seedClient.CreateSeedAsync({ OwnerId, OwnerType: Faction, SeedTypeCode })
    // ApiException → add error entry, continue; Exception → error event + continue
  WRITE faction-store:BuildFactionKey(factionId) <- FactionModel from def
  WRITE faction-store:BuildFactionCodeKey(gameServiceId, def.Code) <- FactionModel
  PUBLISH faction.created { full faction data }
// Pass 2: Resolve parent codes
FOREACH def with ParentCode != null AND child in createdFactions
  IF parentCode found in createdFactions
    WRITE faction-store:BuildFactionKey(child.FactionId) <- model with parent set
    WRITE faction-store:BuildFactionCodeKey(gameServiceId, child.Code) <- model
  ELSE → add error entry, increment failed
RETURN (200, SeedFactionsResponse { Created, Skipped, Failed, Errors })

---

### DesignateRealmBaseline
POST /faction/designate-realm-baseline | Roles: [developer]

LOCK faction-lock:"faction:{factionId}"                           -> 409 if fails
  READ faction-store:BuildFactionKey(factionId)                   -> 404 if null
  // Find and clear previous baseline
  QUERY faction-query-store WHERE $.RealmId = realmId AND $.IsRealmBaseline = "true"
  FOREACH existing baseline (not this faction)
    WRITE faction-store:BuildFactionKey(existing.FactionId) <- IsRealmBaseline = false
    WRITE faction-store:BuildFactionCodeKey(existing.GameServiceId, existing.Code) <- updated
  // Set this faction as baseline + Sovereign
  WRITE faction-store:BuildFactionKey(factionId) <- IsRealmBaseline = true, AuthorityLevel = Sovereign
  WRITE faction-store:BuildFactionCodeKey(gameServiceId, code) <- updated
  // Invalidate norm caches for this and previous baseline factions
  CALL InvalidateNormCacheForFactionAsync(factionId)
  IF previousBaseline: CALL InvalidateNormCacheForFactionAsync(previousBaseline.FactionId)
  PUBLISH faction.realm-baseline.designated { factionId, realmId, previousBaselineFactionId }
  PUBLISH faction.updated { changedFields: [isRealmBaseline, authorityLevel] }
RETURN (200, FactionResponse)

---

### GetRealmBaseline
POST /faction/get-realm-baseline | Roles: [user]

QUERY faction-query-store WHERE $.RealmId = realmId AND $.IsRealmBaseline = "true" PAGED(0, 1)
IF empty                                                          -> 404
RETURN (200, FactionResponse)

---

### AddMember
POST /faction/member/add | Roles: [user]

LOCK faction-lock:"faction-membership:{factionId}"                -> 409 if fails
  READ faction-store:BuildFactionKey(factionId)                   -> 404 if null
  IF deprecated or status != Active                               -> 400
  READ member-store:BuildMemberKey(factionId, characterId)        -> 409 if exists
  // Role defaults to config.DefaultMemberRole if not specified
  WRITE member-store:BuildMemberKey(factionId, characterId) <- FactionMemberModel
  READ member-list-store:BuildCharacterMembershipsKey(characterId)
  // Create new list if null
  WRITE member-list-store:BuildCharacterMembershipsKey(characterId) <- list with entry added
  // Increment MemberCount on faction
  WRITE faction-store:BuildFactionKey(factionId) <- MemberCount++
  WRITE faction-store:BuildFactionCodeKey(gameServiceId, code) <- updated
  CALL _resourceClient.RegisterReferenceAsync({ character, characterId, faction, factionId })
    // ApiException swallowed with warning
  CALL InvalidateNormCacheForCharacterAsync(characterId)
  PUBLISH faction.member.added { factionId, characterId, role }
RETURN (200, FactionMemberResponse)

---

### RemoveMember
POST /faction/member/remove | Roles: [user]

LOCK faction-lock:"faction-membership:{factionId}"                -> 409 if fails
  CALL RemoveMemberInternalAsync(factionId, characterId)
RETURN (200, null)

// RemoveMemberInternalAsync:
READ member-store:BuildMemberKey(factionId, characterId)          -> 404 if null
DELETE member-store:BuildMemberKey(factionId, characterId)
CALL _resourceClient.UnregisterReferenceAsync({ character, characterId, faction, factionId })
  // ApiException swallowed with warning
READ member-list-store:BuildCharacterMembershipsKey(characterId)
WRITE member-list-store:BuildCharacterMembershipsKey(characterId) <- list with entry removed
READ faction-store:BuildFactionKey(factionId)
IF faction found
  WRITE faction-store:BuildFactionKey(factionId) <- MemberCount = Max(0, count - 1)
  WRITE faction-store:BuildFactionCodeKey(gameServiceId, code) <- updated
CALL InvalidateNormCacheForCharacterAsync(characterId)
PUBLISH faction.member.removed { factionId, characterId }

---

### ListMembers
POST /faction/member/list | Roles: [user]

QUERY member-query-store WHERE $.FactionId EXISTS AND $.FactionId = factionId
  AND (optional) $.Role = role
  PAGED(cursorOffset, pageSize)
RETURN (200, ListMembersResponse { Members, NextCursor, HasMore })

---

### ListMembershipsByCharacter
POST /faction/member/list-by-character | Roles: [user]

READ member-list-store:BuildCharacterMembershipsKey(characterId)
IF null → RETURN (200, { Memberships: [] })
FOREACH membership in list
  READ faction-store:BuildFactionKey(membership.FactionId)
  IF null or gameServiceId mismatch → skip
  // Include faction name, code, role, joinedAt
RETURN (200, ListMembershipsByCharacterResponse { Memberships })

---

### UpdateMemberRole
POST /faction/member/update-role | Roles: [user]

LOCK faction-lock:"membership:{factionId}:{characterId}"          -> 409 if fails
  READ member-store:BuildMemberKey(factionId, characterId)        -> 404 if null
  IF role already equals requested                                -> 200 (idempotent, no write)
  WRITE member-store:BuildMemberKey(factionId, characterId) <- updated role
  READ member-list-store:BuildCharacterMembershipsKey(characterId)
  IF list and matching entry found
    WRITE member-list-store:BuildCharacterMembershipsKey(characterId) <- role updated
  PUBLISH faction.member.role-changed { factionId, characterId, previousRole, newRole }
RETURN (200, FactionMemberResponse)

---

### CheckMembership
POST /faction/member/check | Roles: [user]

READ member-store:BuildMemberKey(factionId, characterId)
RETURN (200, CheckMembershipResponse { IsMember: member != null, Role: member?.Role })

---

### ClaimTerritory
POST /faction/territory/claim | Roles: [developer]

LOCK faction-lock:"territory:{locationId}"                        -> 409 if fails
  READ faction-store:BuildFactionKey(factionId)                   -> 404 if null
  IF deprecated or status != Active                               -> 400
  CALL HasCapabilityAsync(seedId, "territory.claim")              -> 403 if lacks capability
  CALL _locationClient.LocationExistsAsync({ LocationId })        -> 4xx passthrough if ApiException
  READ territory-store:BuildLocationClaimKey(locationId)          -> 409 if active claim exists
  READ territory-list-store:BuildFactionClaimsKey(factionId)
  IF claimCount >= MaxTerritoriesPerFaction                       -> 400
  WRITE territory-store:BuildClaimKey(claimId) <- TerritoryClaimModel
  WRITE territory-store:BuildLocationClaimKey(locationId) <- TerritoryClaimModel
  WRITE territory-list-store:BuildFactionClaimsKey(factionId) <- list with claimId added
  CALL _resourceClient.RegisterReferenceAsync({ location, locationId, faction, factionId })
    // ApiException swallowed with warning
  CALL InvalidateNormCacheForFactionAsync(factionId)
  PUBLISH faction.territory.claimed { factionId, locationId, claimId }
RETURN (200, TerritoryClaimResponse)

---

### ReleaseTerritory
POST /faction/territory/release | Roles: [developer]

READ territory-store:BuildClaimKey(claimId)                       -> 404 if null
LOCK faction-lock:"territory:{claim.LocationId}"                  -> 409 if fails
  CALL ReleaseTerritoryInternalAsync(claim)

// ReleaseTerritoryInternalAsync:
WRITE territory-store:BuildClaimKey(claimId) <- Status = Released, ReleasedAt = now
DELETE territory-store:BuildLocationClaimKey(locationId)
CALL _resourceClient.UnregisterReferenceAsync({ location, locationId, faction, factionId })
  // ApiException swallowed
READ territory-list-store:BuildFactionClaimsKey(factionId)
WRITE territory-list-store:BuildFactionClaimsKey(factionId) <- list with claimId removed
CALL InvalidateNormCacheForFactionAsync(factionId)
PUBLISH faction.territory.released { factionId, locationId, claimId }
RETURN (200, null)

---

### ListTerritoryClaims
POST /faction/territory/list | Roles: [user]

QUERY territory-query-store WHERE $.FactionId EXISTS AND $.FactionId = factionId
  AND (optional) $.Status = status
  PAGED(cursorOffset, pageSize)
RETURN (200, ListTerritoryClaimsResponse { Claims, NextCursor, HasMore })

---

### GetControllingFaction
POST /faction/territory/get-controlling | Roles: [user]

READ territory-store:BuildLocationClaimKey(locationId)            -> 404 if null or not Active
READ faction-store:BuildFactionKey(claim.FactionId)               -> 404 if null
RETURN (200, ControllingFactionResponse { LocationId, Faction, ClaimId, ClaimedAt })

---

### DefineNorm
POST /faction/norm/define | Roles: [developer]

LOCK faction-lock:"norm:{factionId}"                              -> 409 if fails
  READ faction-store:BuildFactionKey(factionId)                   -> 404 if null
  IF deprecated or status != Active                               -> 400
  CALL HasCapabilityAsync(seedId, "norm.define")                  -> 403 if lacks capability
  READ norm-list-store:BuildFactionNormsKey(factionId)
  IF normCount >= MaxNormsPerFaction                              -> 400
  WRITE norm-store:BuildNormKey(normId) <- NormDefinitionModel from request
  WRITE norm-list-store:BuildFactionNormsKey(factionId) <- list with normId added
  CALL InvalidateNormCacheForFactionAsync(factionId)
  PUBLISH faction.norm.defined { factionId, normId, violationType, basePenalty, severity, scope }
RETURN (200, NormDefinitionResponse)

---

### UpdateNorm
POST /faction/norm/update | Roles: [developer]

READ norm-store:BuildNormKey(normId)                              -> 404 if null
LOCK faction-lock:"norm:{norm.FactionId}"                         -> 409 if fails
  // Partial update: basePenalty, severity, scope, description
  WRITE norm-store:BuildNormKey(normId) <- updated model
  CALL InvalidateNormCacheForFactionAsync(norm.FactionId)
  PUBLISH faction.norm.updated { factionId, normId, violationType, basePenalty?, severity?, scope? }
RETURN (200, NormDefinitionResponse)

---

### DeleteNorm
POST /faction/norm/delete | Roles: [developer]

READ norm-store:BuildNormKey(normId)                              -> 404 if null
LOCK faction-lock:"norm:{norm.FactionId}"                         -> 409 if fails
  DELETE norm-store:BuildNormKey(normId)
  READ norm-list-store:BuildFactionNormsKey(factionId)
  WRITE norm-list-store:BuildFactionNormsKey(factionId) <- list with normId removed
  CALL InvalidateNormCacheForFactionAsync(factionId)
  PUBLISH faction.norm.deleted { factionId, normId, violationType }
RETURN (200, null)

---

### ListNorms
POST /faction/norm/list | Roles: [user]

READ norm-list-store:BuildFactionNormsKey(factionId)
IF null → RETURN (200, { FactionId, Norms: [] })
FOREACH normId in normList.NormIds
  READ norm-store:BuildNormKey(normId)                            // skip if null
  IF severity filter specified and mismatches → skip
  IF scope filter specified and mismatches → skip
RETURN (200, ListNormsResponse { FactionId, Norms })

---

### QueryApplicableNorms
POST /faction/norm/query-applicable | Roles: []

// Check cache
IF NormQueryCacheTtlSeconds > 0 AND !forceRefresh
  READ norm-cache:BuildNormCacheKey(characterId, locationId)
  IF cached → RETURN (200, cached response)

// Tier 1: Membership norms
READ member-list-store:BuildCharacterMembershipsKey(characterId)
FOREACH membership
  READ faction-store:BuildFactionKey(membership.FactionId)        // skip if null/deprecated/inactive
  READ norm-list-store:BuildFactionNormsKey(faction.FactionId)
  FOREACH normId
    READ norm-store:BuildNormKey(normId)
    // Add to applicableNorms with source = Membership
    // Add to mergedNormMap (first writer wins per violationType)

// Tier 2: Territory controlling faction norms (if locationId provided)
IF locationId != null
  READ territory-store:BuildLocationClaimKey(locationId)
  IF active claim
    READ faction-store:BuildFactionKey(claim.FactionId)
    READ norm-list-store:BuildFactionNormsKey(claim.FactionId)
    FOREACH normId
      READ norm-store:BuildNormKey(normId)
      // Add to applicableNorms with source = Territory
      // Add to mergedNormMap only if violationType not already present

// Tier 3: Realm baseline norms
QUERY faction-query-store WHERE $.RealmId = realmId AND $.IsRealmBaseline = "true" PAGED(0, 1)
IF baseline found and active
  READ norm-list-store:BuildFactionNormsKey(baseline.FactionId)
  FOREACH normId
    READ norm-store:BuildNormKey(normId)
    // Add to applicableNorms with source = RealmBaseline
    // Add to mergedNormMap only if violationType not already present

// Cache result
IF NormQueryCacheTtlSeconds > 0
  WRITE norm-cache:BuildNormCacheKey(characterId, locationId) <- ResolvedNormCacheModel (TTL)

RETURN (200, QueryApplicableNormsResponse { ApplicableNorms, MergedNormMap, MembershipFactionCount, TerritoryFactionResolved, RealmBaselineResolved })

---

### SetGovernanceEntry
POST /faction/governance/set | Roles: [developer]

LOCK faction-lock:"governance:{factionId}"                        -> 409 if fails
  READ faction-store:BuildFactionKey(factionId)                   -> 404 if null
  IF authorityLevel not Sovereign or Delegated                    -> 400
  CALL HasCapabilityAsync(seedId, "governance.arbitrate")         -> 403 if lacks capability
  READ governance-store:BuildFactionGovernanceDomainKey(factionId, domain)
  IF existing entry (update path)
    WRITE governance-store:BuildGovernanceKey(existing.GovernanceId) <- updated entry
    WRITE governance-store:BuildFactionGovernanceDomainKey(factionId, domain) <- updated
    PUBLISH faction.governance.defined { changedFields }
  ELSE (create path)
    READ governance-list-store:BuildFactionGovernanceKey(factionId)
    IF count >= MaxGovernanceEntriesPerFaction                    -> 400
    WRITE governance-store:BuildGovernanceKey(governanceId) <- new GovernanceEntryModel
    WRITE governance-store:BuildFactionGovernanceDomainKey(factionId, domain) <- new entry
    WRITE governance-list-store:BuildFactionGovernanceKey(factionId) <- list with id added
    PUBLISH faction.governance.defined { no changedFields }
RETURN (200, GovernanceEntryResponse)

---

### RemoveGovernanceEntry
POST /faction/governance/remove | Roles: [developer]

LOCK faction-lock:"governance:{factionId}"                        -> 409 if fails
  READ governance-store:BuildFactionGovernanceDomainKey(factionId, domain) -> 404 if null
  DELETE governance-store:BuildGovernanceKey(entry.GovernanceId)
  DELETE governance-store:BuildFactionGovernanceDomainKey(factionId, domain)
  READ governance-list-store:BuildFactionGovernanceKey(factionId)
  WRITE governance-list-store:BuildFactionGovernanceKey(factionId) <- list with id removed
  PUBLISH faction.governance.deleted { factionId, governanceId, domain }
RETURN (200, null)

---

### ListGovernanceEntries
POST /faction/governance/list | Roles: [user]

READ faction-store:BuildFactionKey(factionId)                     -> 404 if null
READ governance-list-store:BuildFactionGovernanceKey(factionId)
FOREACH govId in list
  READ governance-store:BuildGovernanceKey(govId)                  // skip if null
RETURN (200, ListGovernanceEntriesResponse { Entries })

---

### QueryGovernanceData
POST /faction/governance/query | Roles: []

// Check cache
IF GovernanceCacheTtlSeconds > 0
  READ governance-cache:BuildGovernanceCacheKey(locationId, domain)
  IF cached and resolved → RETURN (200, GovernanceDataResponse from cache)
  IF cached and !resolved → RETURN (404, null)

// Resolution hierarchy (6 steps):
// 1. Sovereign controlling faction has governance for domain
READ territory-store:BuildLocationClaimKey(locationId)
IF active claim
  READ faction-store:BuildFactionKey(claim.FactionId)
  IF authority == Sovereign
    READ governance-store:BuildFactionGovernanceDomainKey(factionId, domain)
    IF found → cache positive → RETURN (200, GovernanceDataResponse)
  // 2. Delegated controlling faction has governance
  IF authority == Delegated
    READ governance-store:BuildFactionGovernanceDomainKey(factionId, domain)
    IF found → cache positive → RETURN (200, GovernanceDataResponse)
    // 3. Delegated's sovereign has governance
    CALL FindSovereignAsync(faction) // walks parent chain via faction-store reads
    IF sovereign found
      READ governance-store:BuildFactionGovernanceDomainKey(sovereign.FactionId, domain)
      IF found → cache positive → RETURN (200, GovernanceDataResponse)
  // 4. Influence faction → find sovereign in parent chain
  IF authority == Influence
    CALL FindSovereignAsync(faction)
    IF sovereign found
      READ governance-store:BuildFactionGovernanceDomainKey(sovereign.FactionId, domain)
      IF found → cache positive → RETURN (200, GovernanceDataResponse)
// 5. Realm baseline sovereign has governance
QUERY faction-query-store WHERE $.RealmId = realmId AND $.IsRealmBaseline = "true" PAGED(0, 1)
IF baseline found
  READ governance-store:BuildFactionGovernanceDomainKey(baseline.FactionId, domain)
  IF found → cache positive → RETURN (200, GovernanceDataResponse)
// 6. No match
WRITE governance-cache:BuildGovernanceCacheKey(locationId, domain) <- negative cache (TTL)
RETURN (404, null)

---

### DelegateAuthority
POST /faction/governance/delegate | Roles: [developer]

LOCK faction-lock:"faction:{targetFactionId}"                     -> 409 if fails
  READ faction-store:BuildFactionKey(sovereignFactionId)          -> 404 if null
  IF sovereign.AuthorityLevel != Sovereign                        -> 400
  READ faction-store:BuildFactionKey(targetFactionId)             -> 404 if null
  // Verify target is descendant of sovereign (walk parent chain)
  WHILE current.ParentFactionId != null (up to MaxHierarchyDepth)
    READ faction-store:BuildFactionKey(current.ParentFactionId)
    IF matches sovereign → descendant confirmed
  IF not descendant                                               -> 400
  IF target.AuthorityLevel != Delegated
    WRITE faction-store:BuildFactionKey(targetFactionId) <- AuthorityLevel = Delegated
    WRITE faction-store:BuildFactionCodeKey(target.GameServiceId, target.Code) <- updated
    PUBLISH faction.updated { changedFields: [authorityLevel] }
  PUBLISH faction.authority.delegated { sovereignFactionId, targetFactionId, domains }
RETURN (200, FactionResponse)

---

### RevokeAuthority
POST /faction/governance/revoke | Roles: [developer]

LOCK faction-lock:"faction:{targetFactionId}"                     -> 409 if fails
  READ faction-store:BuildFactionKey(sovereignFactionId)          -> 404 if null
  READ faction-store:BuildFactionKey(targetFactionId)             -> 404 if null
  IF target.AuthorityLevel != Delegated                           -> 400
  IF specific domains provided
    FOREACH domain in domains
      READ governance-store:BuildFactionGovernanceDomainKey(targetFactionId, domain)
      IF found
        DELETE governance-store:BuildGovernanceKey(entry.GovernanceId)
        DELETE governance-store:BuildFactionGovernanceDomainKey(targetFactionId, domain)
        READ governance-list-store:BuildFactionGovernanceKey(targetFactionId)
        WRITE governance-list-store:BuildFactionGovernanceKey(targetFactionId) <- id removed
  ELSE (blanket revocation)
    READ governance-list-store:BuildFactionGovernanceKey(targetFactionId)
    FOREACH govId
      READ governance-store:BuildGovernanceKey(govId)
      DELETE governance-store:BuildFactionGovernanceDomainKey(targetFactionId, entry.Domain)
      DELETE governance-store:BuildGovernanceKey(govId)            // per-item try-catch
    WRITE governance-list-store:BuildFactionGovernanceKey(targetFactionId) <- cleared
  // Check if any governance entries remain
  READ governance-list-store:BuildFactionGovernanceKey(targetFactionId)
  IF empty → set AuthorityLevel = Influence
    WRITE faction-store:BuildFactionKey(targetFactionId) <- updated
    WRITE faction-store:BuildFactionCodeKey(target.GameServiceId, target.Code) <- updated
    PUBLISH faction.updated { changedFields: [authorityLevel] }
  PUBLISH faction.authority.revoked { sovereignFactionId, targetFactionId, revokedDomains, resultingAuthorityLevel }
RETURN (200, FactionResponse)

---

### CleanupByCharacter
POST /faction/cleanup-by-character | Roles: []

READ member-list-store:BuildCharacterMembershipsKey(characterId)
FOREACH membership
  CALL RemoveMemberInternalAsync(factionId, characterId)
RETURN (200, CleanupByCharacterResponse { MembershipsRemoved })

---

### CleanupByRealm
POST /faction/cleanup-by-realm | Roles: []

// Paginated loop over all factions in realm
FOREACH page (QUERY faction-query-store WHERE $.RealmId = realmId PAGED(offset, SeedBulkPageSize))
  FOREACH faction (per-item try-catch, T7 batch isolation)
    // Count members, norms, claims for reporting
    CALL DeleteFactionCascadeInternalAsync(faction)   // Bypasses deprecation guard
    factionsRemoved++
RETURN (200, CleanupByRealmResponse { FactionsRemoved, MembershipsRemoved, TerritoryClaimsRemoved, NormsRemoved })

### DeleteFactionCascadeInternalAsync (private helper)
Extracted cascade deletion logic shared by DeleteFactionAsync and CleanupByRealmAsync.
Accepts a FactionModel directly (no deprecation guard, no lock — caller handles both).

// Cascade: remove all members (paginated)
FOREACH page (QUERY member-query-store WHERE $.FactionId = factionId)
  FOREACH member (per-item try-catch)
    CALL RemoveMemberInternalAsync(factionId, characterId)
// Cascade: release all territory claims
READ territory-list-store:BuildFactionClaimsKey(factionId)
FOREACH claimId (per-item try-catch)
  READ territory-store:BuildClaimKey(claimId)
  IF Active → CALL ReleaseTerritoryInternalAsync(claim)
DELETE territory-list-store:BuildFactionClaimsKey(factionId)
// Cascade: delete all norms (publish events)
READ norm-list-store:BuildFactionNormsKey(factionId)
FOREACH normId (per-item try-catch)
  READ norm-store:BuildNormKey(normId)
  DELETE norm-store:BuildNormKey(normId)
  PUBLISH faction.norm.deleted
DELETE norm-list-store:BuildFactionNormsKey(factionId)
// Cascade: delete all governance entries (publish events)
READ governance-list-store:BuildFactionGovernanceKey(factionId)
FOREACH govId (per-item try-catch)
  READ governance-store:BuildGovernanceKey(govId)
  DELETE governance-store:BuildFactionGovernanceDomainKey(factionId, domain)
  DELETE governance-store:BuildGovernanceKey(govId)
  PUBLISH faction.governance.deleted
DELETE governance-list-store:BuildFactionGovernanceKey(factionId)
// Delete faction entity + code index
DELETE faction-store:BuildFactionKey(factionId)
DELETE faction-store:BuildFactionCodeKey(gameServiceId, code)
// Invalidate norm cache entries
PUBLISH faction.deleted
CALL _resourceClient.ExecuteCleanupAsync({ faction, factionId })   // Cascade sub-references

---

### CleanupByLocation
POST /faction/cleanup-by-location | Roles: []

READ territory-store:BuildLocationClaimKey(locationId)
IF claim found
  CALL ReleaseTerritoryInternalAsync(claim)
RETURN (200, CleanupByLocationResponse { ClaimsRemoved })

---

### GetCompressData
POST /faction/get-compress-data | Roles: []

READ member-list-store:BuildCharacterMembershipsKey(characterId)
FOREACH membership entry
  READ member-store:BuildMemberKey(entry.FactionId, characterId)
  IF found → include in archive
RETURN (200, FactionArchive { CharacterId, HasMemberships, MembershipCount, Memberships })

---

### RestoreFromArchive
POST /faction/restore-from-archive | Roles: []

// Deserialize archive from body.Data
IF deserialization fails                                          -> 400
FOREACH membership in archive.Memberships
  READ faction-store:BuildFactionKey(membership.FactionId)
  IF null or deprecated or not Active → skip
  READ member-store:BuildMemberKey(factionId, characterId)
  IF already exists → skip
  WRITE member-store:BuildMemberKey(factionId, characterId) <- FactionMemberModel
  READ member-list-store:BuildCharacterMembershipsKey(characterId)
  WRITE member-list-store:BuildCharacterMembershipsKey(characterId) <- list with entry added
  WRITE faction-store:BuildFactionKey(factionId) <- MemberCount++
  WRITE faction-store:BuildFactionCodeKey(gameServiceId, code) <- updated
  CALL _resourceClient.RegisterReferenceAsync({ character, characterId, faction, factionId })
    // ApiException swallowed
RETURN (200, RestoreFromArchiveResponse { MembershipsRestoredCount })

---

## Background Services

No background services.

---

## Non-Standard Implementation Patterns

### Plugin Lifecycle (OnRunningAsync)

```
CALL FactionCompressionCallbacks.RegisterAsync(resourceClient)
  // Registers GetCompressData / RestoreFromArchive callbacks

CALL FactionService.RegisterResourceCleanupCallbacksAsync(resourceClient)
  // Registers CleanupByCharacter / CleanupByRealm / CleanupByLocation callbacks

CALL _seedClient.RegisterSeedTypeAsync({
  SeedTypeCode: config.SeedTypeCode,  // "faction"
  DisplayName: "Faction Spirit",
  AllowedOwnerTypes: [Faction],
  MaxPerOwner: 1,
  GrowthPhases: [
    { "nascent", 0 }, { "established", 50 },
    { "influential", 200 }, { "dominant", 1000 }, { "sovereign", 5000 }
  ]
})
// 409 (already registered) silently swallowed
```

### DI Listener: FactionSeedEvolutionListener

Implements `ISeedEvolutionListener`. Registered as Singleton.

**OnPhaseChangedAsync**: When a seed with `SeedTypeCode == config.SeedTypeCode` changes phase:
- READ faction-store where seed.OwnerId matches (via `_factionQueryStore` or direct key — listener uses `GetAsync`)
- Updates faction's `CurrentPhase` field
- WRITE faction-store with updated model (both keys)
- PUBLISH faction.updated { changedFields: [currentPhase] }

**OnGrowthUpdatedAsync / OnCapabilitiesChangedAsync**: No-op (growth and capabilities are queried on-demand from lib-seed).

### DI Listener: FactionCollectionUnlockListener

Implements `ICollectionUnlockListener`. Registered as Singleton.

**OnEntryUnlockedAsync**: When a character unlocks a collection entry with tags containing `faction:` prefix:
- READ member-list-store:BuildCharacterMembershipsKey(characterId)
- Extracts growth domain from tag (e.g., `faction:commerce` → domain `commerce`)
- FOREACH faction membership:
  - CALL _seedClient.RecordGrowthBatchAsync({ growthEntries with domain and config.CollectionGrowthAmount })

### DI Provider: FactionProviderFactory

Implements `IVariableProviderFactory`. Registered as Singleton. Provides `${faction.*}` namespace.

**CreateAsync(characterId, realmId, locationId)**: Returns `FactionProvider.Empty` for null characterId.
- READ member-list-store:BuildCharacterMembershipsKey(characterId)
- FOREACH membership: READ faction-store:BuildFactionKey(factionId)
- FOREACH membership faction: READ norm-list-store:BuildFactionNormsKey(factionId)
- FOREACH norm: READ norm-store:BuildNormKey(normId)
- IF locationId provided: READ territory-store:BuildLocationClaimKey(locationId) → check if controlling faction is in membership set
- Builds provider with aggregate variables, per-faction variables, norm variables, and territory context (`in_controlled_territory`).
