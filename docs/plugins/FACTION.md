# Faction Plugin Deep Dive

> **Plugin**: lib-faction
> **Schema**: schemas/faction-api.yaml
> **Version**: 1.0.0
> **State Store**: faction-statestore (MySQL), faction-membership-statestore (MySQL), faction-territory-statestore (MySQL), faction-norm-statestore (MySQL), faction-cache (Redis), faction-lock (Redis)
> **Guide**: [Morality System](../guides/MORALITY-SYSTEM.md) (cross-service integration with lib-obligation and lib-faction)

## Overview

The Faction service (L4 GameFeatures) models factions as seed-based living entities whose capabilities emerge from growth, not static assignment. Each faction owns a seed (via lib-seed) that grows through member activities fed by the Collection-to-Seed pipeline. As the faction's seed grows through phases (nascent, established, influential, dominant), capabilities unlock: norm definition, enforcement tiers, territory claiming, and trade regulation. A nascent faction literally CANNOT enforce norms -- it hasn't grown enough governance capability yet.

**Primary Purpose**: Store and serve social norms for lib-obligation. lib-obligation is the PRIMARY CONSUMER of faction norm data -- it queries `/faction/norm/query-applicable` to resolve the full norm hierarchy (guild faction -> location faction -> realm baseline faction) into a merged norm set, then applies personality-weighted moral reasoning to produce GOAP action cost modifiers for NPC cognition. Without lib-faction, lib-obligation only has contractual obligations (guild charters, trade agreements). With lib-faction, ambient social and cultural norms become enforceable through the same cognition pipeline.

**Norm Resolution Hierarchy** (most specific wins):
1. Guild faction norms (character's direct memberships)
2. Location faction norms (controlling faction at character's current location)
3. Realm baseline faction norms (realm-wide cultural context)

**Faction Concepts**:
- **Realm baseline faction**: provides realm-wide cultural norms (honor codes, taboos)
- **Location controlling faction**: provides local norms (lawless district, temple sanctity)
- **Guild factions**: character memberships with role hierarchy (Leader, Officer, Member, Recruit)
- **Parent/child hierarchy**: organizational structure with configurable max depth

**Political Connections**: Inter-faction political relationships (alliances, rivalries, treaties) are modeled as seed bonds via lib-seed's existing bond API, NOT through lib-relationship. A bond between two faction seeds represents the alliance/rivalry as a growable entity with its own capability manifest. Joint member activities grow the bonded seed, unlocking alliance capabilities.

**violationType as Opaque String**: Norm violation types (e.g., "theft", "deception", "violence") are opaque strings, not enums. The vocabulary is defined by contract templates and action tag mappings in lib-obligation; lib-faction stores whatever violation type strings callers provide. Adding new violation types never requires a schema change.

Internal-only, never internet-facing.

---

## Factions as Emergent Governance (Architectural Target)

> **Status**: All 31 faction endpoints are fully implemented (CRUD, membership, territory, norms, cleanup, compression). The seed-based growth pipeline and norm resolution hierarchy work end-to-end. The broader vision described below -- emergent governance, economic regulation, and the morality pipeline role -- is the architectural target that these mechanics serve.

### Factions Are the Emergent Governance Layer of the Living World

The vision is not "factions with stats" but emergent political systems that arise organically from NPC activity. NPCs trade → faction grows its commerce seed domain → faction unlocks trade regulation capabilities → NPCs within that faction's territory experience different economic norms → the economy shifts. The seed growth system is the delivery mechanism: governance power is EARNED through member activity, not statically assigned by a designer. A nascent thieves' guild literally cannot define "honor among thieves" as an enforceable norm because it hasn't grown enough governance capability yet. A sovereign merchant guild can regulate pricing, enforce trade agreements, and claim territory. This mirrors real-world governance where authority comes from established legitimacy, not from declaration.

### Faction Is the "Social Landscape" Layer of the Morality Pipeline

Faction norms define what behaviors are costly WHERE and for WHOM. This feeds into Obligation (the "cost landscape") which computes personality-weighted costs, which feeds into Actor cognition where costs become GOAP action cost modifiers. Without Faction, the morality system is purely contractual -- only explicit agreements (guild charters, trade deals) create obligations. WITH Faction, ambient cultural and social norms become enforceable: a realm-wide honor code, a temple district's sanctity rules, a merchant quarter's trade regulations all modify NPC behavior without requiring individual contracts. See [Morality System guide](../guides/MORALITY-SYSTEM.md) for the full pipeline architecture.

### Factions Are Economic Actors in a Living Economy

The economy must be NPC-driven, not player-driven. Supply, demand, pricing, and trade routes emerge from NPC behavior -- what they need, what they produce, what they aspire to. Factions are how economic regulation emerges: trade regulation capabilities unlocked through faction growth, tariff systems, market influence, divine economic intervention (e.g., a Commerce regional watcher boosting trade) that works THROUGH faction mechanisms. Player economies layer on top of this NPC economic substrate. If the economy is just player-to-player, the world feels dead when players are offline.

### Political Connections Are Growable Entities, Not Boolean Flags

Alliances between factions are modeled as seed bonds via lib-seed's bond API. A bond between two faction seeds represents the alliance/rivalry as a growable entity with its own capability manifest and phase progression. Joint member activities (trade between allied factions, military cooperation) grow the bonded seed, unlocking alliance capabilities: joint territory claims, mutual defense pacts, trade agreements, shared governance. This means political relationships are living things that strengthen or atrophy based on actual inter-faction activity, not static diplomatic flags.

### The Emergent Narrative of Faction Evolution

Over simulated time, factions should undergo recognizable arcs. A street gang grows through member activities → becomes an established criminal organization with enforceable codes → claims territory → regulates the underground economy. A merchant guild forms around a marketplace → trade activity grows its commerce seed → it unlocks pricing regulation → eventually competes with the ruling faction for political influence. These arcs are not scripted; they emerge from the seed growth system, the Collection-to-Seed pipeline, and capability-gated operations. The game designer's role is defining seed types, growth mappings, and capability thresholds -- the specific arcs that emerge are unique to each world's simulated history.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for factions (MySQL), memberships (MySQL), territory claims (MySQL), norm definitions (MySQL), faction cache (Redis), distributed locks (Redis) |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events (faction.created/updated/deleted), membership events, territory events, norm events, realm baseline designation events |
| lib-seed (`ISeedClient`) | Creating seeds for new factions, recording growth, querying capabilities for gating norm/territory operations; seed bonds for inter-faction alliances |
| lib-location (`ILocationClient`) | Validating location existence for territory claims (L2 hard dependency) |
| lib-realm (`IRealmClient`) | Validating realm existence for faction creation and realm baseline designation (L2 hard dependency) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence during faction creation (L2 hard dependency) |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration, and compression callback registration (L1 hard dependency) |
| `IDistributedLockProvider` | Distributed locks for faction, membership, territory, and norm mutations (L0 hard dependency) |

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-obligation (L4) | **Planned** primary consumer: will query `/faction/norm/query-applicable` to resolve merged norm sets for NPC cognition cost modifiers. Not yet wired up -- lib-obligation currently resolves faction context through contracts only. See Design Considerations #4-5. |

## State Storage

### Faction Store
**Store**: `faction-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `fac:{factionId}` | `FactionModel` | Primary lookup by ID |
| `fac:{gameServiceId}:{code}` | `FactionModel` | Code-uniqueness lookup within game service scope |

### Membership Store
**Store**: `faction-membership-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `mem:{factionId}:{characterId}` | `FactionMemberModel` | Primary lookup by faction + character (uniqueness) |
| `mem:char:{characterId}` | `MembershipListModel` | All memberships for a character (cross-faction query) |

### Territory Store
**Store**: `faction-territory-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tcl:{claimId}` | `TerritoryClaimModel` | Primary lookup by claim ID |
| `tcl:loc:{locationId}` | `TerritoryClaimModel` | Controlling faction lookup by location |
| `tcl:fac:{factionId}` | `TerritoryClaimListModel` | All territory claims for a faction |

### Norm Store
**Store**: `faction-norm-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `nrm:{normId}` | `NormDefinitionModel` | Primary lookup by norm ID |
| `nrm:fac:{factionId}` | `NormListModel` | All norms for a faction |

### Faction Cache
**Store**: `faction-cache` (Backend: Redis, prefix: `faction:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ncache:{characterId}:{locationId}` | `ResolvedNormCacheModel` | Cached resolved norm set per character+location (with TTL). Location is "none" when no location specified. |

### Distributed Locks
**Store**: `faction-lock` (Backend: Redis, prefix: `faction:lock`)

Used for faction create/update/delete, membership add/remove/role-change, territory claim/release, norm define/update/delete, and realm baseline designation.

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `faction.created` | `FactionCreatedEvent` | Faction created (single or via seed) |
| `faction.updated` | `FactionUpdatedEvent` | Faction fields updated |
| `faction.deleted` | `FactionDeletedEvent` | Faction deleted |
| `faction.member.added` | `FactionMemberAddedEvent` | Character joins a faction |
| `faction.member.removed` | `FactionMemberRemovedEvent` | Character leaves or is removed |
| `faction.member.role-changed` | `FactionMemberRoleChangedEvent` | Member's role updated |
| `faction.territory.claimed` | `FactionTerritoryClaimedEvent` | Faction claims a location |
| `faction.territory.released` | `FactionTerritoryReleasedEvent` | Faction releases a territory claim |
| `faction.norm.defined` | `FactionNormDefinedEvent` | New norm defined for a faction |
| `faction.norm.updated` | `FactionNormUpdatedEvent` | Norm definition modified |
| `faction.norm.deleted` | `FactionNormDeletedEvent` | Norm definition removed |
| `faction.realm-baseline.designated` | `FactionRealmBaselineDesignatedEvent` | Faction designated as realm baseline |

### Consumed Events

None via `x-event-subscriptions`. Cross-service integration uses DI listener patterns instead:

| Pattern | Interface | Action |
|---------|-----------|--------|
| Seed growth/phase/capability notifications | `ISeedEvolutionListener` | Receives growth, phase change, and capability unlock notifications for faction seeds. Updates faction's `currentPhase` and gates operations based on unlocked capabilities. Writes to distributed state for multi-node safety. |
| Member activity collection unlocks | `ICollectionUnlockListener` | Converts member activity collection entry unlocks into faction seed growth via tag prefix matching (governance, commerce, military) against seed type `collectionGrowthMappings`. Writes to distributed state via lib-seed API. |

### Resource Cleanup (via x-references)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| character | faction | CASCADE | `/faction/cleanup-by-character` |
| realm | faction | CASCADE | `/faction/cleanup-by-realm` |
| location | faction | CASCADE | `/faction/cleanup-by-location` |

### Compression Callback (via x-compression-callback)

| Resource Type | Source Type | Priority | Compress Endpoint | Decompress Endpoint |
|--------------|-------------|----------|-------------------|---------------------|
| character | faction | 20 | `/faction/get-compress-data` | `/faction/restore-from-archive` |

Archives faction memberships, roles, and applicable norm context for character compression via lib-resource.

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `SeedTypeCode` | `FACTION_SEED_TYPE_CODE` | `faction` | Seed type code for faction growth |
| `DefaultMemberRole` | `FACTION_DEFAULT_MEMBER_ROLE` | `Member` | Default role for new members when unspecified |
| `MaxHierarchyDepth` | `FACTION_MAX_HIERARCHY_DEPTH` | `5` | Max parent/child nesting depth |
| `MaxNormsPerFaction` | `FACTION_MAX_NORMS_PER_FACTION` | `50` | Max norm definitions per faction |
| `MaxTerritoriesPerFaction` | `FACTION_MAX_TERRITORIES_PER_FACTION` | `20` | Max territory claims per faction |
| `NormQueryCacheTtlSeconds` | `FACTION_NORM_QUERY_CACHE_TTL_SECONDS` | `300` | TTL for cached norm resolution results (5 minutes) |
| `DistributedLockTimeoutSeconds` | `FACTION_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |
| `SeedBulkPageSize` | `FACTION_SEED_BULK_PAGE_SIZE` | `100` | Page size for bulk seed operations |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<FactionService>` | Structured logging |
| `FactionServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 4 MySQL stores + cache store); also creates `IJsonQueryableStateStore<FactionModel>` for paginated listing, `IJsonQueryableStateStore<FactionMemberModel>` for member queries and norm cache invalidation, and `IJsonQueryableStateStore<TerritoryClaimModel>` for territory claim listing |
| `IMessageBus` | Event publishing |
| `IResourceClient` | Reference tracking, cleanup callbacks, compression callbacks (L1) |
| `ISeedClient` | Seed creation, growth recording, capability queries (L2) |
| `ILocationClient` | Location existence validation for territory claims (L2) |
| `IRealmClient` | Realm existence validation (L2) |
| `IGameServiceClient` | Game service existence validation (L2) |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |

DI listener implementations:
- `ISeedEvolutionListener` -- receives seed growth/phase/capability notifications
- `ICollectionUnlockListener` -- converts member activity unlocks into faction seed growth

Variable provider:
- `IVariableProviderFactory` -- provides `${faction.*}` namespace to Actor (L2)

## API Endpoints (Implementation Notes)

### Faction CRUD (11 endpoints)

Standard CRUD on faction entities with code-uniqueness enforcement per game service scope. All endpoints require `developer` role.

- **Create** (`/faction/create`): Validates game service and realm existence. Creates a seed via lib-seed for growth tracking. Saves under both ID and code lookup keys.
- **Get** (`/faction/get`): Primary lookup by faction ID.
- **GetByCode** (`/faction/get-by-code`): Lookup by game service + code.
- **List** (`/faction/list`): Paginated cursor-based listing. Filters by game service, realm, status, parent faction, top-level only, and realm baseline flags.
- **Update** (`/faction/update`): Acquires distributed lock. Handles partial updates (null fields skipped). Publishes updated event with `changedFields` list.
- **Deprecate** (`/faction/deprecate`): Sets status to `Deprecated`. Prevents new member additions.
- **Undeprecate** (`/faction/undeprecate`): Restores status to `Active`.
- **Delete** (`/faction/delete`): Acquires lock, cascades member removals, territory releases, and norm deletions. Publishes deleted event.
- **Seed** (`/faction/seed`): Bulk create with two-pass parent resolution (like Location). Skips duplicates by code lookup key. Returns created/skipped/failed counts.
- **DesignateRealmBaseline** (`/faction/designate-realm-baseline`): Marks a faction as the realm baseline cultural faction. Replaces previous baseline if one existed. Publishes designation event.
- **GetRealmBaseline** (`/faction/get-realm-baseline`): Returns the realm baseline faction for a given realm.

### Membership Management (6 endpoints)

- **AddMember** (`/faction/member/add`): Validates faction exists and is Active. Assigns default role if none specified. Publishes member added event. Invalidates norm cache for character.
- **RemoveMember** (`/faction/member/remove`): Removes membership. Publishes member removed event. Invalidates norm cache.
- **ListMembers** (`/faction/member/list`): Paginated members within a faction with optional role filter.
- **ListMembershipsByCharacter** (`/faction/member/list-by-character`): All factions a character belongs to with optional game service filter.
- **UpdateMemberRole** (`/faction/member/update-role`): Changes member's role. Publishes role changed event.
- **CheckMembership** (`/faction/member/check`): Quick membership check returning boolean + role.

### Territory Management (4 endpoints)

- **ClaimTerritory** (`/faction/territory/claim`): Validates location exists, faction has `territory.claim` seed capability. One controlling faction per location. Publishes claim event. Invalidates norm cache for location.
- **ReleaseTerritory** (`/faction/territory/release`): Releases claim. Publishes release event. Invalidates norm cache.
- **ListTerritoryClaims** (`/faction/territory/list`): Paginated claims for a faction with optional status filter.
- **GetControllingFaction** (`/faction/territory/get-controlling`): Returns the faction controlling a location.

### Norm Management (5 endpoints)

- **DefineNorm** (`/faction/norm/define`): Requires `norm.define` seed capability. Validates max norms limit. Publishes norm defined event. Invalidates norm cache.
- **UpdateNorm** (`/faction/norm/update`): Partial updates to base penalty, severity, scope, description. Publishes norm updated event. Invalidates norm cache.
- **DeleteNorm** (`/faction/norm/delete`): Removes norm definition. Publishes norm deleted event. Invalidates norm cache.
- **ListNorms** (`/faction/norm/list`): All norms for a faction with optional severity/scope filters.
- **QueryApplicableNorms** (`/faction/norm/query-applicable`): **Core endpoint for lib-obligation.** Resolves the full norm hierarchy for a character at a location: aggregates guild faction norms, location controlling faction norms, and realm baseline norms. Returns both raw applicable norms and a merged norm map (most specific wins per violation type). Cached in Redis with configurable TTL; `forceRefresh` bypasses cache.

### Cleanup Endpoints (3 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS, T28):

- **CleanupByCharacter** (`/faction/cleanup-by-character`): Removes all memberships for a character. Returns count of memberships removed.
- **CleanupByRealm** (`/faction/cleanup-by-realm`): Removes all factions in a realm and cascades (memberships, territory claims, norms).
- **CleanupByLocation** (`/faction/cleanup-by-location`): Removes all territory claims for a location.

### Compression Endpoints (2 endpoints)

- **GetCompressData** (`/faction/get-compress-data`): Returns a `FactionArchive` (extends `ResourceArchiveBase`) with character's memberships for archival.
- **RestoreFromArchive** (`/faction/restore-from-archive`): Restores memberships from a compressed archive.

## Variable Provider: `${faction.*}` Namespace

Implements `IVariableProviderFactory` (via `FactionProviderFactory`) providing the following variables to Actor (L2) via the Variable Provider Factory pattern for ABML behavior expressions. Loads the character's faction memberships from the membership list store and enriches with faction details.

**Aggregate variables:**

| Variable | Type | Description |
|----------|------|-------------|
| `${faction.count}` | int | Number of factions the character belongs to |
| `${faction.names}` | List&lt;string&gt; | Names of all factions |
| `${faction.codes}` | List&lt;string&gt; | Codes of all factions |

**Per-faction variables** (accessed by faction code, case-insensitive):

| Variable | Type | Description |
|----------|------|-------------|
| `${faction.CODE.name}` | string | Faction display name |
| `${faction.CODE.status}` | string | Faction status (e.g., "Active", "Deprecated") |
| `${faction.CODE.phase}` | string? | Current seed growth phase |
| `${faction.CODE.is_realm_baseline}` | bool | Whether this is the realm baseline faction |
| `${faction.CODE.member_count}` | int | Total member count |
| `${faction.CODE.role}` | string | Character's role in this faction (e.g., "Leader", "Member") |

Returns `FactionProvider.Empty` for non-character actors or characters with no faction memberships.

**Implementation Gap vs. Plan**: The original plan (Issue #410 comment + `~/.claude/plans/glittery-jingling-meadow.md`) specified additional variables that are **not yet implemented**:

| Planned Variable | Status | Impact |
|-----------------|--------|--------|
| `${faction.primary_faction}` | **Missing** | No "highest-role faction" identification for behavior shortcuts |
| `${faction.primary_faction_phase}` | **Missing** | Depends on primary faction concept |
| `${faction.has_norm.<type>}` | **Missing** | ABML cannot check norm existence per violation type |
| `${faction.norm_penalty.<type>}` | **Missing** | ABML cannot read base penalty per violation type |
| `${faction.in_controlled_territory}` | **Missing** | ABML cannot check territory control context |

The norm and territory variables are the critical integration point for lib-obligation's `evaluate_consequences` cognition stage. Without them, the GOAP cost modifier pipeline cannot access faction norm data through ABML expressions. The current provider only exposes membership data. Implementing the missing variables requires the provider to query norm stores and territory stores (with the character's current location as context, which may need to be passed through the `CreateAsync` call).

## Visual Aid

```
┌──────────────────────────────────────────────────────────────────────┐
│                   Norm Resolution Hierarchy                          │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Character "Kael" at Location "Docks District"                       │
│                                                                      │
│  1. Guild Factions (direct memberships):                             │
│     ┌─────────────────────────────┐                                  │
│     │ Merchant Guild (member)     │ → theft: 15, deception: 10       │
│     │ Dockworkers Union (recruit) │ → violence: 5                    │
│     └─────────────────────────────┘                                  │
│                                                                      │
│  2. Location Controlling Faction:                                    │
│     ┌─────────────────────────────┐                                  │
│     │ Harbor Authority            │ → contraband: 12, trespass: 8    │
│     │ (controls Docks District)   │ → theft: 10 (overridden by #1)  │
│     └─────────────────────────────┘                                  │
│                                                                      │
│  3. Realm Baseline Faction:                                          │
│     ┌─────────────────────────────┐                                  │
│     │ Arcadian Cultural Council   │ → disrespect: 5, violence: 3    │
│     │ (realm baseline)            │ → theft: 7 (overridden by #1)   │
│     └─────────────────────────────┘   violence: 3 (overridden by #1)│
│                                                                      │
│  Merged Norm Map (most specific wins):                               │
│  ┌─────────────────────────────────────────────────┐                 │
│  │ theft:      15  (Merchant Guild - membership)   │                 │
│  │ deception:  10  (Merchant Guild - membership)   │                 │
│  │ violence:    5  (Dockworkers Union - membership)│                 │
│  │ contraband: 12  (Harbor Authority - territory)  │                 │
│  │ trespass:    8  (Harbor Authority - territory)  │                 │
│  │ disrespect:  5  (Cultural Council - baseline)   │                 │
│  └─────────────────────────────────────────────────┘                 │
│                                                                      │
│  → Passed to lib-obligation for personality-weighted cost modifiers  │
│  → Fed into GOAP planner as dynamic action cost adjustments          │
└──────────────────────────────────────────────────────────────────────┘
```

```
┌──────────────────────────────────────────────────────────────────────┐
│                Collection→Seed Growth Pipeline                       │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Member NPC completes trade                                          │
│       │                                                              │
│       ▼                                                              │
│  Collection entry unlocked ("faction-deeds", tag: "commerce:trade")  │
│       │                                                              │
│       ├──► Member's personal seed growth (existing pipeline)         │
│       │                                                              │
│       └──► ICollectionUnlockListener (lib-faction)                   │
│            Tag prefix "commerce" matches faction seed mapping         │
│                 │                                                     │
│                 ▼                                                     │
│            lib-seed API: RecordGrowth(factionSeedId, "commerce", 1.5)│
│                 │                                                     │
│                 ▼                                                     │
│            ISeedEvolutionListener fires:                              │
│            - Phase changed: nascent → established                     │
│            - Capability unlocked: "norm.define"                       │
│                 │                                                     │
│                 ▼                                                     │
│            Faction can now define enforceable norms                   │
└──────────────────────────────────────────────────────────────────────┘
```

## Stubs & Unimplemented Features

None. All 31 endpoints are fully implemented with business logic.

## Potential Extensions

1. **Faction diplomacy system**: Formalized alliance/rivalry mechanics through seed bonds with capability-gated treaty operations.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/413 -->
2. **Faction economy**: Trade regulation capabilities unlocked at seed growth thresholds, integrating with lib-currency for tariffs and trade agreements.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/414 -->
3. **Faction reputation system**: Per-character standing within a faction affecting norm enforcement intensity and available roles.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/416 -->
4. **Client events for real-time faction notifications**: Define `faction-client-events.yaml` to push membership changes, territory shifts, and norm updates to connected WebSocket clients via `IClientEventPublisher`.
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/418 -->
5. **Faction governance elections**: Member voting for leadership positions using Contract-backed consent flows.
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/420 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently.

### Intentional Quirks (Documented Behavior)

1. **violationType is an opaque string**: Not an enum. Follows the same pattern as Collection's `collectionType` and Seed's `seedTypeCode`. The vocabulary is externally defined by lib-obligation's violation type taxonomy and ABML action tags. lib-faction stores whatever strings callers provide.

2. **Norm cache invalidation is write-triggered**: Cache entries for norm resolution are invalidated on write operations (member add/remove, norm define/update/delete, territory claim/release, baseline designation). Between writes, stale data may be served until TTL expires. lib-obligation can use `forceRefresh` to bypass cache when it detects staleness via contract lifecycle events.

3. **Seed capability gating is runtime-checked**: Norm definition requires `norm.define` capability, territory claiming requires `territory.claim` capability. These are checked at request time by querying the faction's seed capability manifest via lib-seed. A nascent faction's requests will be rejected until sufficient growth is achieved.

4. **Realm baseline is exclusive**: Only one faction per realm can be the baseline cultural faction. Designating a new baseline replaces the previous one and publishes an event with the previous baseline ID.

5. **Territory claims are exclusive per location**: One controlling faction per location. Claiming a location that is already controlled returns a conflict. The `Contested` status exists in the schema for future dispute mechanics but is not currently used.

6. **Dual-key storage pattern**: Factions are saved under both a primary key (by ID) and a lookup key (by game service + code), following the established Collection/Seed pattern.

7. **No event subscriptions -- DI listeners only**: Cross-service integration uses `ISeedEvolutionListener` and `ICollectionUnlockListener` DI provider patterns (per FOUNDATION TENETS, T27) instead of broadcast event subscriptions. Resource cleanup uses `x-references` callbacks (per FOUNDATION TENETS, T28), not `character.deleted` / `realm.deleted` event subscriptions.

8. **Cascading delete does not publish individual norm/territory events**: `DeleteFactionAsync` cascades norm and territory deletions by directly deleting from stores without publishing individual `faction.norm.deleted` or `faction.territory.released` events. The `faction.deleted` lifecycle event is published to signal that the entire entity (and all child data) was removed. Consumers that need to react to norm deletions should listen for `faction.deleted` and treat it as implying all norms are gone.

9. **SeedFactions allows setting IsRealmBaseline directly**: Unlike `CreateFactionAsync` (which always sets `IsRealmBaseline = false`), the `SeedFactionsAsync` endpoint passes through `def.IsRealmBaseline` from the seed definition, allowing baseline designation during bulk seeding without a separate `DesignateRealmBaseline` call.

### Design Considerations (Requires Planning)

1. **No owner validation for territory claims**: Like Collection/Seed, faction trusts that callers pass valid entity IDs. Location existence is validated via lib-location, but no check that the faction "should" be able to claim that location beyond seed capability gating.
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/424 -->

2. **Norm query performance at scale**: `QueryApplicableNorms` performs up to 3 aggregation passes (guild factions, location faction, realm baseline). With many memberships or large norm sets, this could become expensive. The Redis cache (TTL-based) mitigates reads but cold-start queries for characters with many memberships need profiling.

3. **Seed bond mechanics for alliances**: The schema description references seed bonds for inter-faction alliances, but no API endpoints exist for bond management. These would be managed directly through lib-seed's bond API. May need faction-level wrapper endpoints for ergonomic alliance management.

4. **Variable Provider missing norm/territory variables (plan gap)**: The plan (Issue #410 + `glittery-jingling-meadow.md`) specified `${faction.has_norm.<type>}`, `${faction.norm_penalty.<type>}`, `${faction.in_controlled_territory}`, and `${faction.primary_faction}` variables. The current `FactionProviderFactory` only provides membership data (count, names, codes, per-code details). The norm and territory variables are the critical integration point for lib-obligation's `evaluate_consequences` cognition stage. Implementing them requires the provider to query norm and territory stores, and may need the character's current location passed through `CreateAsync` (currently only receives `entityId`).

5. **Missing lib-contract integration for guild charters (plan gap)**: Issue #410 decision Q3 states: "When a character joins a faction (formal guild membership), the guild contract is created explicitly through lib-contract." The plan lists `lib-contract (L1) — formal membership agreements, guild charters` as a dependency. The current implementation does not use `IContractClient` at all -- membership is managed directly without contract backing. This means guild charters are not formalized as binding agreements, and lib-obligation cannot discover faction-sourced contractual obligations through lib-contract.

## Work Tracking

- **GitHub Issue**: [#410 - Feature: Second Thoughts -- Prospective Consequence Evaluation for NPC Cognition](https://github.com/beyond-immersion/bannou-service/issues/410)
  - lib-faction was extracted from the original lib-moral proposal during architecture review
  - Part of the larger lib-obligation + lib-faction system for NPC "second thoughts" cognition
  - All 31 endpoints are now fully implemented (faction CRUD, membership, territory, norms, cleanup, compression)
