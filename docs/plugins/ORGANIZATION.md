# Organization Plugin Deep Dive

> **Plugin**: lib-organization (not yet created)
> **Schema**: `schemas/organization-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: organization-entities (MySQL), organization-members (MySQL), organization-roles (MySQL), organization-assets (MySQL), organization-succession (MySQL), organization-cache (Redis), organization-lock (Redis) — all planned
> **Layer**: GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.

## Overview

Legal entity management service (L4 GameFeatures) for organizations that own assets, employ characters, enter contracts, and participate in the economy as first-class entities. A structural layer that gives economic and social entities a legal identity — shops, guilds, households, trading companies, temples, military units, criminal enterprises, and any other group that acts as a collective within the game world. Game-agnostic: organization types, role templates, governance relationships, seed growth phases, and charter requirements are all configured through seed type definitions, contract templates, and faction governance data at deployment time. Internal-only, never internet-facing.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Organization entities (MySQL), member records (MySQL), role definitions (MySQL), asset registrations (MySQL), succession rules (MySQL), capability cache (Redis), lock store (Redis) |
| `IDistributedLockProvider` | Distributed locks for organization mutations, member changes, asset operations, succession execution (L0) |
| lib-messaging (`IMessageBus`) | Publishing organization lifecycle events, member events, succession events, legal status events |
| lib-messaging (`IEventConsumer`) | Registering handlers for contract breach (charter downgrade), faction territory changes (legal status re-evaluation), seed phase/capability changes |
| lib-currency (`ICurrencyClient`) | Organization wallet creation, treasury management, payroll execution, charter tax payments (L2) |
| lib-inventory (`IInventoryClient`) | Organization inventory container creation, asset tracking (L2) |
| lib-contract (`IContractClient`) | Charter contracts, employment contracts, trade agreements, succession wills, dissolution terms (L1) |
| lib-character (`ICharacterClient`) | Member validation, character existence checks, household association (L2) |
| lib-location (`ILocationClient`) | Organization location assignment, property management (L2) |
| lib-game-service (`IGameServiceClient`) | Game service scope validation (L2) |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration (L1) |
| lib-seed (`ISeedClient`) | Organizational seed type registration, growth recording, capability manifest queries (L2) |
| lib-relationship (`IRelationshipClient`) | Member-to-organization bonds, inter-organization relationships, family bonds within households (L2) |
| lib-collection (`ICollectionClient`) | Organizational deed collection for seed growth pipeline (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-faction (`IFactionClient`) | Legal status determination from sovereign, charter contract integration, governance queries | Legal status tracking disabled; organizations operate without sovereign framework |
| lib-arbitration (`IArbitrationClient`) | Succession contests, dissolution proceedings, charter disputes | Succession contests resolve by deterministic fallback (highest role priority wins); dissolution requires manual intervention |
| lib-escrow (`IEscrowClient`) | Asset division during dissolution, inter-organization exchanges | Asset division unavailable; dissolution limited to simple transfer-to-successor |
| lib-obligation (`IObligationClient`) | Post-dissolution ongoing obligations, employment obligation costs | No obligation cost tracking for organizational commitments |
| lib-status (`IStatusClient`) | Employment status effects on members (employed buff, unemployment debuff) | Status effects not applied |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Organization is a new L4 service with no current consumers. Future dependents: lib-arbitration (organizational parties in cases, asset identification for division), lib-escrow (organizational asset custody), Faction (chartered organization registry for governance), Gardener (household as garden context -- the player's household IS their primary garden), Actor (NPC economic decisions reference organizational assets via variable provider), Storyline (organizational histories and succession events as narrative material) |

---

## State Storage

### Entity Store
**Store**: `organization-entities` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `org:{organizationId}` | `OrganizationModel` | Primary lookup by org ID. Stores org type code, display name, game service ID, owner entity reference (type + ID), legal status, legal status grantor (faction ID), jurisdiction (location ID), seed ID, wallet ID (currency), primary inventory ID, succession mode, founding date, status (Active, Suspended, Dissolved, Archived). |
| `org-code:{gameServiceId}:{code}` | `OrganizationModel` | Human-readable code lookup within game service scope (e.g., `kaels-forge`) |
| `org-owner:{entityType}:{entityId}` | `OrganizationModel` | Lookup by owner entity (character who owns the shop, family who owns the household) |
| `org-location:{locationId}` | `OrganizationModel` | Lookup by primary location (all orgs at a location) |

### Member Store
**Store**: `organization-members` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `mem:{organizationId}:{characterId}` | `OrganizationMemberModel` | Primary lookup. Stores role code, join date, department code (optional), employment contract ID (optional), permission overrides (optional). |
| `mem:char:{characterId}` | `OrganizationMemberModel` | Reverse lookup: all organizations a character belongs to |
| `mem:role:{organizationId}:{roleCode}` | `OrganizationMemberModel` | Role-based lookup: all members in a specific role |

### Role Store
**Store**: `organization-roles` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `role:{orgTypeCode}:{roleCode}` | `OrganizationRoleModel` | Role definition per organization type. Stores display name, permissions set, priority, max members. |

### Asset Registration Store
**Store**: `organization-assets` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `asset:{organizationId}:{assetType}:{assetId}` | `OrganizationAssetModel` | Registered asset. Stores asset type (wallet, inventory, location, contract, custom), asset ID, registration date, department code (optional). |
| `asset:type:{organizationId}:{assetType}` | `OrganizationAssetModel` | Type-based lookup: all assets of a specific type for an org |

### Succession Store
**Store**: `organization-succession` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `succ:{organizationId}` | `OrganizationSuccessionModel` | Succession configuration. Stores succession mode, designated heir (optional, entity type + ID), testament contract ID (optional), eligible role codes for voting, conquest arbitration template code. |

### Capability Cache
**Store**: `organization-cache` (Backend: Redis, prefix: `org:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cap:{organizationId}` | `CachedCapabilityManifest` | Cached organizational seed capability manifest for fast operation gating |
| `legal:{organizationId}` | `CachedLegalStatusModel` | Cached legal status with grantor faction details for fast access |

### Distributed Locks
**Store**: `organization-lock` (Backend: Redis, prefix: `org:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `org:{organizationId}` | Organization entity mutation lock (create, update, dissolve, archive) |
| `member:{organizationId}` | Membership mutation lock (add, remove, role change) |
| `asset:{organizationId}` | Asset registration lock (register, deregister) |
| `succession:{organizationId}` | Succession execution lock (prevents concurrent succession) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `organization.created` | `OrganizationCreatedEvent` | Organization entity created (lifecycle) |
| `organization.updated` | `OrganizationUpdatedEvent` | Organization entity updated (lifecycle) |
| `organization.dissolved` | `OrganizationDissolvedEvent` | Organization enters dissolution (lifecycle) |
| `organization.archived` | `OrganizationArchivedEvent` | Organization archived after dissolution (lifecycle) |
| `organization.deleted` | `OrganizationDeletedEvent` | Organization permanently deleted (lifecycle) |
| `organization.member.added` | `OrganizationMemberAddedEvent` | Character joins organization |
| `organization.member.removed` | `OrganizationMemberRemovedEvent` | Character leaves or is removed from organization |
| `organization.member.role_changed` | `OrganizationMemberRoleChangedEvent` | Member's role within organization changes (promotion, demotion) |
| `organization.succession.triggered` | `OrganizationSuccessionTriggeredEvent` | Succession process initiated (leader death, retirement, removal) |
| `organization.succession.completed` | `OrganizationSuccessionCompletedEvent` | New leader installed |
| `organization.succession.contested` | `OrganizationSuccessionContestedEvent` | Multiple claimants; escalated to arbitration |
| `organization.legal_status.changed` | `OrganizationLegalStatusChangedEvent` | Legal status changed (chartered, licensed, tolerated, outlawed) |
| `organization.asset.registered` | `OrganizationAssetRegisteredEvent` | Asset registered to organization |
| `organization.asset.deregistered` | `OrganizationAssetDeregisteredEvent` | Asset removed from organization registry |
| `organization.phase.changed` | `OrganizationPhaseChangedEvent` | Organization seed transitioned to new growth phase |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.breached` | `HandleContractBreachedAsync` | For charter contracts: evaluate breach severity. Minor breach: warning. Major breach: downgrade legal status. Severe breach: outlaw. |
| `contract.terminated` | `HandleContractTerminatedAsync` | For charter contracts: revert legal status to Tolerated. For employment contracts: remove member. |
| `contract.fulfilled` | `HandleContractFulfilledAsync` | For charter contracts with renewal milestones: process renewal. For employment contracts with completion: update member status. |
| `faction.territory.claimed` | `HandleTerritoryClaimedAsync` | New sovereign at this location. Organizations in the territory enter re-evaluation grace period. |
| `faction.territory.released` | `HandleTerritoryReleasedAsync` | Sovereign released territory. Organizations' charter status may need re-evaluation (falls to realm baseline sovereign). |
| `seed.phase.changed` | `HandleSeedPhaseChangedAsync` | For organization seeds: update cached phase, publish `organization.phase.changed`, re-evaluate available capabilities. |
| `seed.capability.updated` | `HandleSeedCapabilityUpdatedAsync` | Invalidate cached capability manifest for affected organization. |

### Resource Cleanup (FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| character | organization | CASCADE | `/organization/cleanup-by-character` — removes member from all organizations, triggers succession if leader, initiates dissolution if sole member |
| realm | organization | CASCADE | `/organization/cleanup-by-realm` |
| location | organization | CASCADE | `/organization/cleanup-by-location` |

### DI Listener Patterns

| Pattern | Interface | Action |
|---------|-----------|--------|
| Seed evolution | `ISeedEvolutionListener` | Receives growth, phase change, and capability notifications for organization seeds. Updates cached manifests. Writes to distributed state for multi-node safety. |
| Collection unlock | `ICollectionUnlockListener` | Receives collection unlock events tagged with organization deed categories. Routes growth to the member's organization seed. |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultOrganizationType` | `ORGANIZATION_DEFAULT_ORGANIZATION_TYPE` | `household` | Default org type when none specified |
| `MaxMembersDefault` | `ORGANIZATION_MAX_MEMBERS_DEFAULT` | `20` | Default max members per organization (overridden by seed capability) |
| `MaxOrganizationsPerCharacter` | `ORGANIZATION_MAX_ORGANIZATIONS_PER_CHARACTER` | `5` | Maximum organizations a single character can belong to |
| `CharteredGracePeriodDays` | `ORGANIZATION_CHARTERED_GRACE_PERIOD_DAYS` | `30` | Grace period for re-chartering when sovereignty changes |
| `SuccessionVotingPeriodDays` | `ORGANIZATION_SUCCESSION_VOTING_PERIOD_DAYS` | `7` | Voting window for elective succession |
| `SuccessionContestArbitrationTemplateCode` | `ORGANIZATION_SUCCESSION_CONTEST_ARBITRATION_TEMPLATE_CODE` | `succession-contest` | Contract template code for contested successions |
| `DissolutionContractTemplateCode` | `ORGANIZATION_DISSOLUTION_CONTRACT_TEMPLATE_CODE` | `org-dissolution-standard` | Contract template code for organization dissolution |
| `CapabilityCacheTtlSeconds` | `ORGANIZATION_CAPABILITY_CACHE_TTL_SECONDS` | `300` | TTL for cached seed capability manifests |
| `DistributedLockTimeoutSeconds` | `ORGANIZATION_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |
| `SuccessionCheckIntervalMinutes` | `ORGANIZATION_SUCCESSION_CHECK_INTERVAL_MINUTES` | `60` | How often the succession worker checks for pending successions |
| `SuccessionCheckDelayMinutes` | `ORGANIZATION_SUCCESSION_CHECK_DELAY_MINUTES` | `5` | Initial delay before succession worker starts |
| `SuccessionCheckBatchSize` | `ORGANIZATION_SUCCESSION_CHECK_BATCH_SIZE` | `50` | Organizations per batch in succession worker |
| `QueryPageSize` | `ORGANIZATION_QUERY_PAGE_SIZE` | `20` | Default page size for paged queries |
| `DefaultLegalStatus` | `ORGANIZATION_DEFAULT_LEGAL_STATUS` | `Tolerated` | Legal status assigned to new organizations |
| `SeedBulkPageSize` | `ORGANIZATION_SEED_BULK_PAGE_SIZE` | `100` | Page size for bulk seed operations |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<OrganizationService>` | Structured logging |
| `OrganizationServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 7 stores) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Contract, faction, seed event subscriptions |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `ICurrencyClient` | Organization wallet management (L2) |
| `IInventoryClient` | Organization inventory management (L2) |
| `IContractClient` | Charter, employment, succession, dissolution contracts (L1) |
| `ICharacterClient` | Member character validation (L2) |
| `ILocationClient` | Organization location assignment (L2) |
| `IGameServiceClient` | Game service scope validation (L2) |
| `ISeedClient` | Organization seed type registration, growth recording, capability queries (L2) |
| `IResourceClient` | Reference tracking, cleanup callbacks (L1) |
| `IRelationshipClient` | Member-to-organization bonds, inter-organization relationships (L2) |
| `ICollectionClient` | Organizational deed collection for seed growth pipeline (L2) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

### Background Workers

| Worker | Interval Config | Lock Key | Purpose |
|--------|----------------|----------|---------|
| `OrganizationSuccessionWorkerService` | `SuccessionCheckIntervalMinutes` | `org:lock:succession-worker` | Periodically checks for organizations with pending succession (leader role vacancy). Executes deterministic succession modes (primogeniture, designated). Opens voting periods for elective modes. Flags contested successions for arbitration. |
| `OrganizationCharterRenewalWorkerService` | (uses Contract milestone system) | `org:lock:charter-worker` | Monitors charter contract milestones for renewal deadlines. Publishes warnings before expiry. Handles grace period expiry by downgrading legal status. |

### Variable Provider Factories

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `OrganizationVariableProviderFactory` | `${organization.*}` | Organization membership, role, assets, capabilities, legal status | `IVariableProviderFactory` (DI singleton) |

---

## API Endpoints (Implementation Notes)

### Organization Management (10 endpoints)

All endpoints require `developer` role.

- **Create** (`/organization/create`): Validates game service existence. Validates owner entity existence via `ICharacterClient`. Provisions organizational seed via `ISeedClient` (type matches org type code). Provisions organization wallet via `ICurrencyClient`. Provisions organization inventory via `IInventoryClient`. Registers role definitions from org type template. Sets default legal status (`Tolerated`). Saves under ID, code, owner, and location lookup keys. Publishes `organization.created`.

- **Get** (`/organization/get`): Load from MySQL by organizationId. Enriches with cached capability manifest, member count, and current leader.

- **GetByCode** (`/organization/get-by-code`): JSON query by gameServiceId + code.

- **List** (`/organization/list`): Paged JSON query with required gameServiceId filter, optional org type, legal status, location, owner, and growth phase filters.

- **Update** (`/organization/update`): Acquires distributed lock. Partial update (display name, location, succession mode, department configuration). Publishes lifecycle updated event.

- **Suspend** (`/organization/suspend`): Lock. Set status Suspended. Freezes wallet (Currency authorization hold on full balance). Publishes updated event with suspension reason.

- **Dissolve** (`/organization/dissolve`): Initiates dissolution. If lib-arbitration available: files dissolution case per sovereign's procedures. If not available: immediate dissolution with asset transfer to designated successor or equal division among members. Sets status Dissolved. Publishes `organization.dissolved`.

- **Archive** (`/organization/archive`): Post-dissolution. Compresses organization data for the content flywheel. Preserves historical record. Removes active state. Publishes `organization.archived`.

- **Delete** (`/organization/delete`): Lock. Dissolve if active. Remove all members. Deregister all assets. Coordinate cleanup via lib-resource. Delete record. Publishes `organization.deleted`.

- **Seed** (`/organization/seed`): Bulk creation from configuration data. Two-pass: first pass creates orgs without inter-org references, second pass resolves relationships and asset assignments.

### Member Management (7 endpoints)

- **AddMember** (`/organization/member/add`): Validates org capability allows hiring (seed-gated `employ.*`). Validates character exists. Validates character not already at `MaxOrganizationsPerCharacter`. Assigns role (default: lowest-priority role). Optionally creates employment contract via `IContractClient`. Publishes `organization.member.added`.

- **RemoveMember** (`/organization/member/remove`): Validates requesting entity has `fire` permission. If member is leader and org has other members: triggers succession. Terminates employment contract if exists. Removes from Relationship bonds if tracked. Publishes `organization.member.removed`.

- **ChangeRole** (`/organization/member/change-role`): Validates requesting entity has `promote` permission. Validates target role has capacity. Updates member role. Publishes `organization.member.role_changed`.

- **ListMembers** (`/organization/member/list`): Paged query by organizationId, optional role filter.

- **GetMember** (`/organization/member/get`): Load by organizationId + characterId.

- **ListByCharacter** (`/organization/member/list-by-character`): Returns all organizations a character belongs to with their roles.

- **TransferOwnership** (`/organization/member/transfer-ownership`): Transfers owner entity reference. Assigns new owner the highest-priority role. Optionally backed by a contract (purchase agreement, inheritance).

### Asset Management (4 endpoints)

- **RegisterAsset** (`/organization/asset/register`): Validates requesting entity has `manage_assets` permission. Registers an asset reference (wallet ID, inventory ID, location ID, contract ID, or custom type + ID) to the organization. Publishes `organization.asset.registered`.

- **DeregisterAsset** (`/organization/asset/deregister`): Removes asset reference. Asset itself is not deleted -- only the organization's claim on it. Publishes `organization.asset.deregistered`.

- **ListAssets** (`/organization/asset/list`): Returns all registered assets for an organization, optional type filter.

- **GetAssetSummary** (`/organization/asset/get-summary`): Returns aggregated financial summary: wallet balance (via Currency), inventory value estimation (via Item), property count, active contracts count.

### Legal Status Management (3 endpoints)

- **SetLegalStatus** (`/organization/legal-status/set`): Validates caller is sovereign or delegated authority for the organization's jurisdiction. Updates legal status. If upgrading to Chartered/Licensed: creates charter contract. If downgrading to Outlawed: terminates existing charter. Publishes `organization.legal_status.changed`.

- **GetLegalStatus** (`/organization/legal-status/get`): Returns current legal status with grantor faction, jurisdiction, charter contract reference.

- **QueryByLegalStatus** (`/organization/legal-status/query`): Paged query for organizations by legal status within a jurisdiction. Used by sovereign faction governance to audit chartered organizations.

### Succession Management (4 endpoints)

- **SetSuccessionRules** (`/organization/succession/set`): Validates requesting entity has `succession` permission. Updates succession mode, designated heir, eligible roles. Optionally creates testament contract.

- **GetSuccessionRules** (`/organization/succession/get`): Returns succession configuration for an organization.

- **TriggerSuccession** (`/organization/succession/trigger`): Manually triggers succession (retirement, voluntary abdication). If deterministic mode: executes immediately. If elective: opens voting period. If contested: escalates to arbitration.

- **CastVote** (`/organization/succession/cast-vote`): For elective successions during voting period. Validates voter is member with eligible role. Records vote. If quorum reached: resolves election.

### Role Definition Management (3 endpoints)

- **DefineRoles** (`/organization/roles/define`): Registers role definitions for an organization type. Stores as template for all organizations of that type.

- **ListRoles** (`/organization/roles/list`): Returns role definitions for an organization type.

- **UpdateRole** (`/organization/roles/update`): Modifies a role definition (permissions, priority, max members). Changes apply to all organizations of that type.

### Cleanup Endpoints (3 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByCharacter** (`/organization/cleanup-by-character`): Removes character from all organizations. Triggers succession where applicable. For owned organizations with no other members: dissolve.
- **CleanupByRealm** (`/organization/cleanup-by-realm`): Dissolves and archives all organizations in the realm.
- **CleanupByLocation** (`/organization/cleanup-by-location`): Updates organizations at this location (remove location reference, not dissolve -- organizations can relocate).

### Compression Endpoints (2 endpoints)

- **GetCompressData** (`/organization/get-compress-data`): Returns organization data suitable for archival -- member history, transaction summary, succession history, legal status changes. For content flywheel consumption.
- **RestoreFromArchive** (`/organization/restore-from-archive`): Restores an archived organization from compressed data. Used for narrative generation where historical organizations are referenced.

---

## Visual Aid

Organization identity and structure are owned here. Treasury is Currency (organizations own wallets). Inventory is Inventory/Item (organizations own containers and goods). Contracts are Contract (organizations are contract parties). Employment and membership are Relationship (member-to-organization bonds). Physical presence is Location (organizations control or occupy locations). Governance capabilities are Seed (organizational growth determines what the organization can do). Legal status comes from Faction (the sovereign determines whether an organization is chartered, licensed, tolerated, or outlawed). Internal roles and succession are organization-specific concerns owned by this service.

### Organization Structure

```
+-----------------------------------------------------------------------+
|                    ORGANIZATION STRUCTURE                               |
|                                                                        |
|   ORGANIZATION ENTITY                                                  |
|   +--------------------------------------------------------------+    |
|   |  organizationId: guid                                        |    |
|   |  type: "shop" (opaque string)                                |    |
|   |  code: "kaels-forge"                                         |    |
|   |  gameServiceId: guid                                         |    |
|   |  owner: { type: "character", id: guid }                      |    |
|   |  legalStatus: Chartered                                      |    |
|   |  legalStatusGrantor: factionId (sovereign)                   |    |
|   |  jurisdiction: locationId                                    |    |
|   |  seedId: guid (type: "shop")                                 |    |
|   |  walletId: guid (Currency)                                   |    |
|   |  primaryInventoryId: guid (Inventory)                        |    |
|   |  successionMode: Designated                                  |    |
|   |  status: Active                                              |    |
|   +--------------------------+-----------------------------------+    |
|                              |                                         |
|          +-------------------+-------------------+                    |
|          v                   v                   v                     |
|   +----------+       +----------+       +----------+                 |
|   | MEMBERS  |       | ASSETS   |       | SEED     |                 |
|   |          |       |          |       | (growth) |                 |
|   | Kael     |       | Wallet   |       |          |                 |
|   | (owner)  |       | 500g     |       | Phase:   |                 |
|   |          |       |          |       | Prominent|                 |
|   | Thane    |       | Inventory|       | Business |                 |
|   | (manager)|       | 47 items |       |          |                 |
|   |          |       |          |       | Caps:    |                 |
|   | Pip      |       | Location |       | employ.  |                 |
|   | (appren- |       | Market   |       | expanded |                 |
|   |  tice)   |       | District |       | branch.  |                 |
|   |          |       |          |       | open     |                 |
|   +----------+       | Charter  |       +----------+                 |
|                      | Contract |                                     |
|                      +----------+                                     |
|                                                                        |
|   SOVEREIGN RELATIONSHIP                                               |
|   +--------------------------------------------------------------+    |
|   | Kingdom of Arcadia (Sovereign)                                |   |
|   |   |                                                           |   |
|   |   +-- Charter contract: "Kael's Forge is a licensed          |   |
|   |   |   blacksmith. Tax: 10% revenue. Annual review."          |   |
|   |   |                                                           |   |
|   |   +-- Behavioral clauses: no_weapons_to_outlaws,             |   |
|   |   |   quality_standards, guild_membership_required            |   |
|   |   |                                                           |   |
|   |   +-- Breach consequence: Chartered -> Licensed -> Tolerated |   |
|   +--------------------------------------------------------------+    |
+-----------------------------------------------------------------------+
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 0: Prerequisites
- Faction sovereignty (`authorityLevel`) must be implemented (see [Faction deep dive Design Consideration #6](FACTION.md#design-considerations-requires-planning))
- lib-arbitration should exist for dissolution and succession contests (can proceed without -- uses deterministic fallbacks)

### Phase 1: Core Organization Infrastructure
- Create `organization-api.yaml` schema with all endpoints
- Create `organization-events.yaml` schema
- Create `organization-configuration.yaml` schema
- Generate service code
- Implement organization CRUD (create provisions seed + wallet + inventory)
- Implement member management (add, remove, role change)
- Implement role definition system (per-type templates)
- Implement asset registration (wallet, inventory, location, contract references)

### Phase 2: Seed Integration
- Register organization seed types (household, shop, guild, trading_company)
- Implement Collection-to-Seed growth pipeline via `ICollectionUnlockListener`
- Implement capability-gated operations (hiring requires `employ.*` capability)
- Implement variable provider factory for `${organization.*}` namespace
- Implement `ISeedEvolutionListener` for cached capability invalidation

### Phase 3: Legal Status System
- Implement legal status management
- Implement charter-as-contract pattern (charter creation, renewal, breach handling)
- Implement sovereignty change re-evaluation (grace period, re-chartering)
- Wire faction territory events for jurisdiction tracking

### Phase 4: Succession System
- Implement succession rule storage
- Implement deterministic succession modes (primogeniture, designated, dissolution)
- Implement elective succession (voting period, quorum, tie-breaking)
- Implement succession worker background service
- Wire character death events as succession triggers
- Implement contested succession escalation to lib-arbitration (soft dependency)

### Phase 5: Household Pattern
- Author `household` seed type definition with growth phases and capabilities
- Author role template for household type (head, heir, dependent, elder, servant)
- Implement household-specific succession modes (primogeniture, matrilineal, equal)
- Wire household creation into character lifecycle (marriage, coming of age)
- Wire household dissolution into arbitration (divorce, exile, branch family)

### Phase 6: Economy Integration
- Author `shop` and `trading_company` seed type definitions
- Implement payroll mechanics (periodic salary distribution from org wallet)
- Implement trade-on-behalf (contracts in organization's name)
- Wire NPC economic GOAP actions to use organizational assets
- Implement department system for large organizations

### Phase 7: Dissolution Integration
- Wire organization dissolution into lib-arbitration case types
- Implement asset identification for escrow division
- Implement member redistribution on dissolution
- Implement seed splitting on organization split (proportional growth transfer)

---

## Potential Extensions

1. **Inter-organization relationships**: Organizations forming alliances, joint ventures, parent-subsidiary structures. Uses lib-relationship for org-to-org bonds and lib-seed bonds for alliance growth. A "trade guild" is an organization of organizations.

2. **Organizational contracts as first-class entities**: Organizations entering contracts as parties rather than their leader-as-proxy. Requires lib-contract to support organization entity type as a party. Charter, employment, trade, and dissolution contracts all use this.

3. **Payroll and recurring expenses**: Background worker that processes periodic salary payments (org wallet -> member wallets) and recurring expenses (rent, tax, supplies). Delinquent payroll triggers obligation events (employees unhappy, may quit). Delinquent tax triggers charter breach.

4. **Organizational reputation system**: Beyond seed growth, organizations develop reputation scores per domain (quality, reliability, fairness) that affect NPC willingness to trade, join, or charter. Reputation feeds into the `${organization.*}` variable provider.

5. **Criminal enterprise specialization**: Outlawed organizations grow through `underground.*` seed domains instead of `commerce.*`. Capabilities unlock like `fence.stolen_goods`, `bribe.officials`, `smuggle.contraband`. The underground economy is a parallel growth path, not a punishment.

6. **Organizational politics**: Within large organizations, factions form (members aligned with different leaders or visions). Internal politics modeled as faction-within-organization, using the same norm/obligation pipeline but scoped to organizational membership.

7. **Franchise/branch system**: When an organization has `branch.open` capability, it can create subsidiary organizations in other locations. The parent org maintains partial ownership and revenue sharing via contracts. Branch organizations have their own seeds but are linked to the parent.

8. **Organizational memory for content flywheel**: Organizations accumulate history (founding, growth milestones, leadership changes, crises, triumphs). This history feeds into Storyline for narrative generation. A 100-year-old guild has richer story potential than a new one.

9. **Client events**: `organization-client-events.yaml` for pushing organizational notifications (succession alerts, charter warnings, financial summaries, member changes) to the owner's WebSocket client.

10. **Variable provider enrichment for economic GOAP**: Expanded `${organization.*}` variables for economic decision-making: `${organization.primary.wallet_balance}`, `${organization.primary.inventory_value}`, `${organization.primary.monthly_revenue}`, `${organization.primary.monthly_expenses}`, `${organization.primary.profit_margin}`. These enable NPC actors to make sophisticated economic decisions through ABML behavior expressions.

---

## Why Not lib-faction?

Factions and organizations overlap conceptually -- both have members, both have internal structure, both participate in governance. The question arises: should organizations simply be a *type* of faction?

**The answer is no -- factions govern, organizations operate.**

| Concern | lib-faction | lib-organization |
|---------|------------|-----------------|
| **Primary purpose** | Social norms, territory control, governance, cultural identity | Economic participation, asset ownership, employment, commerce |
| **Authority** | Sovereign/Delegated/Influence -- determines legal framework | Subject to authority -- operates within a faction's legal framework |
| **Membership semantics** | Cultural affiliation, political allegiance | Employment, ownership, contractual role |
| **Asset ownership** | Factions don't own wallets or inventories (they govern who can) | Organizations own wallets, inventories, locations, contracts |
| **Growth domains** | Governance capabilities (norm definition, territory control, arbitration) | Economic capabilities (hiring, branches, trade complexity, contract capacity) |
| **Dissolution** | Faction dissolution is a political event (power vacuum) | Organization dissolution is an economic/legal event (asset division, succession) |
| **Legal status** | Factions ARE the legal authority (or subject to it, but for governance) | Organizations are granted legal status BY factions |

**The relationship between them is clear**: A faction (sovereign) charters an organization. The organization operates within the faction's legal framework. The faction's norms constrain the organization's behavior (tax obligations, trade regulations, employment laws). The organization's legal status is determined by the faction. If the faction changes (new sovereign conquers the territory), the organization's charter may be re-evaluated.

**Membership overlap is intentional**: A character can be both a Merchant Guild faction member (cultural/political affiliation that determines norms and GOAP action costs) and a "Kael's Forge" organization employee (economic role that determines income, work obligations, and access to organizational assets). These are orthogonal axes.

---

## The Household Pattern

A household is an organization. It has shared assets (family home, savings, heirlooms), internal roles (head of household, heir, dependents, elders), succession rules (primogeniture, equal division, matrilineal, elective), and legal status within the sovereign's framework (recognized family, noble house, outlawed clan). The [Dungeon deep dive](DUNGEON.md)'s "household split" mechanic and the [Arbitration deep dive](ARBITRATION.md)'s divorce/exile case types are all organization dissolution — breaking apart a legal entity's structure, dividing its assets, and managing the aftermath.

Households are the most important organization type in Arcadia because every player character exists within one. Understanding households as organizations unlocks several critical game mechanics.

### Why Households Are Organizations

From the [Player Vision](../../arcadia-kb/PLAYER-VISION.md): "The player possesses a household member and participates in the world." The household is the player's primary management unit -- a collection of characters with shared assets and generational continuity. This maps exactly to an organization:

| Household Concept | Organization Concept |
|-------------------|---------------------|
| Family members | Organization members with roles (head, heir, dependent, elder) |
| Family home, savings, heirlooms | Organization assets (location, wallet, inventory) |
| Inheritance rules | Succession rules on the organization entity |
| Head of household | Leadership role with administrative permissions |
| Branch family split | Organization dissolution via lib-arbitration |
| Arranged marriage | Inter-organization contract (merging households or creating new ones) |
| Family reputation | Organization seed growth (household prestige) |
| Noble house status | Organization legal status (Chartered = recognized nobility) |

### Household Lifecycle

```
CHARACTER CREATES HOUSEHOLD (marriage, coming of age, land grant)
        |
        v
Organization created: type "household"
  - Founding members registered
  - Household wallet created (Currency)
  - Household inventory created (Inventory)
  - Household seed created (type: household)
  - Location assigned (if applicable)
  - Legal status: Tolerated (default) or Chartered (if sovereignty recognizes)
        |
        v
HOUSEHOLD OPERATES (the living game)
  - Members work -> income to household wallet
  - Members craft -> goods to household inventory
  - Members trade -> contracts in household's name
  - Children born -> new members with Dependent role
  - Elders age -> role transitions (Head -> Elder)
  - Seed grows -> capabilities unlock (hire servants, open business, claim land)
        |
        +----------------------------------------------+
        v                                               v
SUCCESSION EVENT                              DISSOLUTION EVENT
(head of household dies)                      (divorce, exile, split)
        |                                               |
        v                                               v
Succession rules execute:                     Arbitration case filed:
  - Primogeniture: eldest child               - Asset division via Escrow
  - Equal: members vote                       - Custody of children
  - Matrilineal: eldest daughter              - Ongoing obligations
  - Elective: designated heir                 - Seed impact
  - Testament: per will contract              - Two organizations result
        |                                               |
        v                                               v
New head assumes leadership                   Split organizations operate
Organization continues                        independently
```

### Household as Organization

```
+-----------------------------------------------------------------------+
|                    HOUSEHOLD AS ORGANIZATION                           |
|                                                                        |
|   ACCOUNT (guardian seed)                                              |
|     |                                                                  |
|     | spirit possesses household                                       |
|     |                                                                  |
|     v                                                                  |
|   HOUSEHOLD (Organization, type: "household")                          |
|   +--------------------------------------------------------------+    |
|   |                                                               |   |
|   |   MEMBERS (with roles)         ASSETS                         |   |
|   |   +-----------------+         +-----------------+           |   |
|   |   | Erik (Head)     |         | Family Home     |           |   |
|   |   | Marta (Spouse)  |         | (Location)      |           |   |
|   |   | Kael (Heir)     |         |                 |           |   |
|   |   | Lena (Dependent)|         | Savings: 1200g  |           |   |
|   |   | Old Bjorn (Elder|         | (Wallet)        |           |   |
|   |   +-----------------+         |                 |           |   |
|   |                                | Family Chest    |           |   |
|   |   SUCCESSION                   | (Inventory)     |           |   |
|   |   +-----------------+         |                 |           |   |
|   |   | Mode: Primogeni-|         | Heirloom Sword  |           |   |
|   |   | ture (eldest    |         | (Item in chest)  |           |   |
|   |   | child inherits) |         +-----------------+           |   |
|   |   |                 |                                        |   |
|   |   | Heir: Kael      |         SEED (household)              |   |
|   |   | (auto-determined|         +-----------------+           |   |
|   |   |  from priority) |         | Phase: Prominent|           |   |
|   |   +-----------------+         | Caps: branch.   |           |   |
|   |                                | family, trade.  |           |   |
|   |                                | family_business |           |   |
|   +--------------------------------------------------------------+    |
|                                                                        |
|   LIFECYCLE EVENTS:                                                    |
|                                                                        |
|   Erik dies --> Succession: Kael becomes Head                          |
|                  Marta becomes Elder                                    |
|                  Family assets transfer to Kael's management           |
|                                                                        |
|   Kael wants to split --> Arbitration: dissolution case filed          |
|                            under sovereign's procedures                 |
|                            Asset division via Escrow                    |
|                            New household created for Kael               |
|                            Original household continues with Marta     |
|                                                                        |
|   Lena marries outsider --> Inter-household contract                   |
|                              Dowry negotiation                          |
|                              Member transfer (or new household)         |
|                              Ongoing family trade agreement             |
+-----------------------------------------------------------------------+
```

### Branch Family Pattern

When a household grows too large or members want independence, a **branch family** forms through organization dissolution:

1. A member (or group of members) petitions to split via lib-arbitration
2. The sovereign's dissolution procedure determines terms
3. Assets are divided via lib-escrow
4. A new organization (the branch family) is created with the departing members
5. Ongoing obligations (family tithe, trade agreements, mutual defense) are created as new contracts
6. The original household continues with remaining members
7. Both organizations' seeds are affected (the original loses growth proportional to departing members; the branch starts with a portion of the original's growth)

This is the same mechanism used for the [Dungeon deep dive](DUNGEON.md)'s Pattern A household split and for divorce proceedings.

---

## Seed-Based Organizational Growth

Each organization owns a seed that grows through member activities and economic transactions, following the same Collection-to-Seed pipeline that powers faction growth. As the organization's seed grows, capabilities unlock: hiring more employees, opening branches, entering complex contracts, participating in trade regulation. A nascent street vendor literally cannot hire employees — it hasn't grown enough organizational capability yet.

Each organization owns a seed whose type code matches the organization type (e.g., `household`, `shop`, `guild`, `trading_company`). Growth reflects what the organization's members actually do.

### Growth Pipeline

```
Member performs economic action
  (trade, craft, service, combat, governance)
        |
        v
Collection entry unlocked
  ("org-deeds", tag: "{activity}:{domain}")
        |
        v
ICollectionUnlockListener (lib-organization)
        |
        v
lib-seed: RecordGrowth
  (orgSeedId, "{domain}", amount)
        |
        v
ISeedEvolutionListener (lib-organization):
  Phase changed -> new capabilities unlocked
  (e.g., nascent -> established -> influential)
```

### Example: Shop Seed Type

| Property | Value |
|----------|-------|
| **SeedTypeCode** | `shop` |
| **DisplayName** | Shop |
| **MaxPerOwner** | 1 (one organizational seed per org) |
| **AllowedOwnerTypes** | `["organization"]` |

**Growth Phases**:

| Phase | MinTotalGrowth | Capabilities Unlocked |
|-------|---------------|----------------------|
| Street Vendor | 0.0 | Single operator. No employees. Portable inventory only. |
| Established Shop | 25.0 | `employ.basic` -- hire up to 2 employees. Fixed location. |
| Prominent Business | 100.0 | `employ.expanded` -- up to 5 employees. `branch.open` -- open a second location. `contract.complex` -- enter multi-party trade agreements. |
| Trade House | 500.0 | `employ.unlimited`. `branch.multiple`. `regulation.participate` -- participate in trade regulation governance. `charter.upgrade` -- petition for higher charter status. |

**Growth Domains**:

| Domain | Subdomain | Purpose |
|--------|-----------|---------|
| `commerce` | `.trade`, `.crafting`, `.service` | Economic transaction volume and value |
| `employment` | `.hiring`, `.retention`, `.training` | Workforce management effectiveness |
| `reputation` | `.quality`, `.reliability`, `.innovation` | Customer and peer perception |
| `governance` | `.compliance`, `.regulation`, `.arbitration` | Legal and regulatory participation |

### Example: Household Seed Type

| Property | Value |
|----------|-------|
| **SeedTypeCode** | `household` |
| **DisplayName** | Household |
| **MaxPerOwner** | 1 |
| **AllowedOwnerTypes** | `["organization"]` |

**Growth Phases**:

| Phase | MinTotalGrowth | Capabilities Unlocked |
|-------|---------------|----------------------|
| New Family | 0.0 | Basic household operations. Up to 4 members. |
| Established | 30.0 | `property.acquire` -- purchase family home. `employ.servants` -- hire household servants. Up to 8 members. |
| Prominent | 150.0 | `branch.family` -- member can petition to form branch family. `trade.family_business` -- operate a family business (org-within-org). `political.petition` -- petition sovereign for recognition. |
| Noble House | 800.0 | `political.title` -- eligible for noble title (requires sovereign charter). `estate.manage` -- manage multiple properties. `dynasty.establish` -- formal dynastic succession rules. |

---

## Legal Status from Sovereign

The sovereign authority (via lib-faction's `authorityLevel`) determines an organization's legal standing. A Chartered organization has legal protections. An Outlawed organization operates illegally, and conducting business with it carries obligation costs. Legal status feeds into the organization's own seed growth: legitimate commerce grows a Chartered organization faster; underground economy grows an Outlawed one differently. The chartering mechanism is itself a Contract — the sovereign grants a charter with behavioral clauses (tax compliance, regulatory adherence), and breach triggers status downgrade.

This creates a dynamic legal landscape where the same organization can be legitimate in one territory and criminal in another.

### Status Levels

| Status | Meaning | Mechanical Effect |
|--------|---------|-------------------|
| **Chartered** | Officially recognized and protected by law | Full legal protections. Contracts are enforceable. Property is protected. Tax obligations apply. Seed growth bonus (legitimate economy feeds growth faster). |
| **Licensed** | Permitted to operate in specific domains | Operates legally within licensed scope. Operating outside scope is a legal violation. License may have renewal requirements. |
| **Tolerated** | Not officially recognized but not outlawed | No legal protections but no penalties for existing. Cannot enter contracts under the sovereign's jurisdiction that require legal standing. Default status for new organizations. |
| **Outlawed** | Operating illegally, subject to enforcement | Conducting business with this org is a legal violation (adds obligation costs). Property can be seized without legal consequence. Members may face arrest. Cannot use the formal legal system (contracts, arbitration). Seed grows through underground economy domain instead of commerce domain. |

### Charter as Contract

The chartering mechanism is itself a Contract, following the established orchestration pattern:

```
Organization petitions sovereign for charter
        |
        v
Sovereign evaluates (NPC sovereign uses GOAP):
  - Organization's seed growth (is it significant enough?)
  - Organization's reputation domain
  - Political considerations (faction alliances, economic need)
  - Bribery/corruption possibilities (obligation costs)
        |
        v
If approved: Charter contract created
  - Party roles: sovereign_faction, organization
  - Behavioral clauses: tax compliance, regulatory adherence,
    domain restrictions, reporting requirements
  - Milestones: annual review, charter renewal
  - Prebound API: on creation -> set legal status to Chartered
  - Breach consequences: status downgrade (Chartered -> Licensed -> Tolerated)
        |
        v
Charter contract active -> organization operates as Chartered
  - Breach (tax evasion, regulation violation) detected via obligation
  - Cure period allows correction
  - Repeated breach -> status downgrade via prebound API
  - Severe breach -> immediate outlawing
```

### Sovereignty Changes

When a new sovereign takes territory (conquest, treaty, divine mandate), all organizations in that territory face re-evaluation:

1. New sovereign's governance data includes charter requirements for each organization type
2. Organizations with existing charters from the old sovereign enter a **grace period** (configurable governance parameter)
3. During grace period, organizations must petition the new sovereign for re-chartering
4. Organizations that don't petition (or are denied) revert to Tolerated status
5. Organizations the new sovereign actively opposes become Outlawed

This creates emergent political upheaval: a regime change isn't just a faction flag swap -- every organization in the territory faces an existential question about their legal standing.

### Legal Status Lifecycle

```
+-----------------------------------------------------------------------+
|                    LEGAL STATUS LIFECYCLE                               |
|                                                                        |
|   New organization created                                             |
|        |                                                               |
|        v                                                               |
|   TOLERATED (default)                                                  |
|        |                                                               |
|        +-- Petition sovereign for charter --+                          |
|        |   (seed capability: charter.*)     |                          |
|        |                                    v                          |
|        |                              Sovereign evaluates              |
|        |                              (GOAP: growth, reputation,       |
|        |                               political considerations)       |
|        |                                    |                          |
|        |                         +----------+----------+              |
|        |                         v                     v              |
|        |                    APPROVED              DENIED               |
|        |                    Charter contract       Stays Tolerated     |
|        |                    created                                    |
|        |                         |                                     |
|        |                         v                                     |
|        |                    CHARTERED / LICENSED                        |
|        |                         |                                     |
|        |              +----------+----------+                         |
|        |              v          v          v                          |
|        |         Compliance  Minor breach  Major breach                |
|        |         (renew)     (warning)     (downgrade)                 |
|        |              |          |              |                      |
|        |              v          v              v                      |
|        |         CHARTERED   CHARTERED     LICENSED/TOLERATED          |
|        |         (renewed)   (warned)      (demoted)                   |
|        |                                        |                      |
|        |                              Severe breach or                  |
|        |                              sovereign hostility               |
|        |                                        |                      |
|        |                                        v                      |
|        |                                   OUTLAWED                    |
|        |                                        |                      |
|        |                              +---------+---------+           |
|        |                              v         v         v           |
|        |                         Continues  Seized by  Dissolved       |
|        |                         underground sovereign  (members       |
|        |                         (crime      (asset      scatter)      |
|        |                          economy)   forfeiture)               |
|        |                                                               |
|        +-- Sovereignty changes --> Grace period                        |
|             (new sovereign takes    Re-petition required                |
|              territory)             Or revert to Tolerated             |
+-----------------------------------------------------------------------+
```

---

## Internal Structure

Organizations have internal structure -- roles, departments, and hierarchy. This is intentionally simpler than faction governance because organizations are economic entities, not political ones.

### Roles

Roles are organization-type-specific strings, not a global enum. A household has `head`, `heir`, `dependent`, `elder`, `servant`. A shop has `owner`, `manager`, `apprentice`, `employee`. A guild has `guildmaster`, `officer`, `journeyman`, `apprentice`.

| Property | Description |
|----------|-------------|
| `roleCode` | Opaque string identifier (e.g., `head`, `owner`, `guildmaster`) |
| `displayName` | Human-readable name |
| `permissions` | Set of organization-level permission codes (e.g., `manage_assets`, `hire`, `fire`, `trade`, `represent`) |
| `priority` | Numeric ordering for succession (lower = higher priority) |
| `maxMembers` | Maximum members in this role (nullable = unlimited) |

Role definitions are stored per organization type, not per organization instance. When an organization is created with type `shop`, it inherits the role template for shops. Customization per instance is limited to permission overrides (a shop owner might restrict their manager's trading permission).

### Role Permissions

| Permission Code | Description |
|----------------|-------------|
| `manage_assets` | Access to organization wallet and inventory |
| `hire` | Add new members to the organization |
| `fire` | Remove members from the organization |
| `trade` | Enter contracts and execute trades on behalf of the organization |
| `represent` | Act as the organization's representative in arbitration, governance |
| `promote` | Change member roles within the organization |
| `dissolve` | Initiate organization dissolution |
| `succession` | Modify succession rules |
| `charter` | Petition for or modify legal status |

### Departments (Extension)

For large organizations (trade houses, noble houses, military units), departments provide sub-structure:

| Property | Description |
|----------|-------------|
| `departmentCode` | Opaque string identifier (e.g., `logistics`, `sales`, `security`) |
| `headRole` | Role code of the department head |
| `budget` | Allocated portion of organizational wallet (tracked as a hold via Currency authorization holds) |
| `inventory` | Allocated inventory container (sub-container within the organization's main inventory) |

Departments are optional and only relevant for organizations whose seed has grown past the `branch.open` capability threshold. A street vendor has no departments. A trade house might have logistics, sales, and security departments.

---

## Succession Rules

When an organization's leader dies, retires, or is removed, succession determines continuity. Succession rules are stored on the organization entity and executed automatically when triggered.

### Succession Modes

| Mode | Mechanism | Common For |
|------|-----------|-----------|
| **Primogeniture** | Eldest child (or eldest of a specified gender) inherits | Households, noble houses |
| **Equal Division** | All eligible members vote; majority wins | Guilds, cooperatives |
| **Designated** | Current leader explicitly names successor (stored as a relationship) | Shops, military units |
| **Testament** | Per a will contract created by the leader | Noble houses, wealthy households |
| **Elective** | Members with sufficient role priority vote | Guilds, trading companies |
| **Conquest** | Strongest member claims leadership (resolved via Arbitration) | Criminal enterprises, some military |
| **Dissolution** | No succession -- organization dissolves on leader death | Single-owner shops, personal enterprises |

### Succession Flow

```
Leader death/removal event received
        |
        v
Organization's succession mode checked
        |
        +-- Primogeniture/Designated/Testament:
        |     Deterministic. New leader identified.
        |     Role transition executed.
        |     Contracts transferred.
        |     Events published.
        |
        +-- Equal Division/Elective:
        |     Voting period opened (Contract milestone).
        |     Members with eligible roles vote.
        |     Deadline enforced. Majority wins.
        |     Tie-breaking: highest role priority.
        |     New leader installed on vote completion.
        |
        +-- Conquest:
        |     Arbitration case filed (type: succession_contest).
        |     Claimants submit claims.
        |     Resolved by sovereign's procedures.
        |
        +-- Dissolution:
              Organization enters dissolution.
              Assets divided per dissolution rules.
              Members become unaffiliated.
              Organization archived.
```

### Succession and the Content Flywheel

Succession events are rich content flywheel material. A contested succession in a noble house produces:
- Factional alignment (which claimant do NPCs support?)
- Arbitration cases (if multiple claimants contest)
- Relationship changes (loyalties shift, alliances form)
- Economic disruption (trade agreements in limbo during vacancy)
- Narrative seeds (the disinherited heir who becomes a bandit, the reluctant successor who must grow into leadership)

NPCs with the `evaluate_consequences` cognition stage consider succession implications in their decisions. An NPC head of household who is aging evaluates the cost of various succession plans based on their personality, family relationships, and faction norms.

---

## Variable Provider: `${organization.*}`

The organization variable provider exposes organizational context to ABML behavior expressions, enabling NPCs to reason about their economic and social organizational context.

| Variable | Type | Description |
|----------|------|-------------|
| `${organization.count}` | int | Number of organizations the character belongs to |
| `${organization.CODE.name}` | string | Display name for a specific organization (by code) |
| `${organization.CODE.type}` | string | Organization type code |
| `${organization.CODE.role}` | string | Character's role in this organization |
| `${organization.CODE.legal_status}` | string | Organization's legal status (Chartered/Licensed/Tolerated/Outlawed) |
| `${organization.CODE.phase}` | string | Organization seed growth phase |
| `${organization.CODE.has_capability.<cap>}` | bool | Whether the organization has a specific seed capability |
| `${organization.primary}` | string | Code of the organization where character holds highest-priority role |
| `${organization.primary_type}` | string | Type code of the primary organization |
| `${organization.primary_legal_status}` | string | Legal status of the primary organization |
| `${organization.is_leader}` | bool | Whether the character is a leader (role with `dissolve` permission) in any organization |
| `${organization.total_treasury}` | float | Sum of wallet balances across all organizations the character manages |
| `${organization.employee_count}` | int | Total employees across organizations the character leads |

### ABML Usage Example

```yaml
flows:
  evaluate_business_decision:
    - cond:
        # Running an outlawed organization? Consider going legit.
        - when: "${organization.primary_legal_status == 'Outlawed' && obligations.violation_cost.contraband > 15.0}"
          then:
            - call: consider_chartering

        # Business is growing? Evaluate hiring.
        - when: "${organization.primary.has_capability.employ.expanded && organization.employee_count < 5}"
          then:
            - call: evaluate_hiring

        # Can't hire yet? Focus on growing the business.
        - when: "${!organization.primary.has_capability.employ.basic}"
          then:
            - call: focus_on_trade

        # Organization can open a branch? Evaluate expansion.
        - when: "${organization.primary.has_capability.branch.open}"
          then:
            - call: evaluate_expansion
```

---

## Economy Integration

From the [Vision](../../arcadia-kb/VISION.md): "The economy must be NPC-driven, not player-driven. Supply, demand, pricing, and trade routes emerge from NPC behavior — what they need, what they produce, what they want." Without lib-organization, "NPC runs a shop" is a behavior pattern with no structural backing. With lib-organization, the shop is a legal entity with inventory, a currency wallet, employees, trade agreements, and a succession plan — and when the shopkeeper dies, succession rules determine what happens to it. Organizations are the structural skeleton that NPC economic behavior hangs on.

Organizations do not replace characters as economic actors. Characters own and operate organizations. An NPC blacksmith owns a "blacksmith shop" organization. The NPC's Actor brain makes economic decisions (what to buy, what to sell, what to craft). The organization provides the structural container for those decisions — the wallet from which purchases are made, the inventory in which goods are stored, the contracts under which trades are executed. The NPC's GOAP planner considers organizational assets when evaluating economic actions.

How organizations participate in the NPC-driven economy through GOAP-based economic decision-making.

```
+-----------------------------------------------------------------------+
|                    ORGANIZATION IN THE ECONOMY                         |
|                                                                        |
|   NPC ACTOR BRAIN (GOAP planner)                                      |
|        |                                                               |
|        |  ${organization.primary.has_capability.employ.basic}          |
|        |  ${organization.primary_legal_status}                         |
|        |  ${organization.total_treasury}                               |
|        |                                                               |
|        v                                                               |
|   Economic Decision:                                                   |
|   "I need iron ingots for my forge"                                    |
|        |                                                               |
|        +-- Check org wallet (Currency): 500g available                 |
|        |                                                               |
|        +-- Check supplier (another NPC org):                           |
|        |   Is supplier Chartered? (legal to trade with)                |
|        |   Is supplier Outlawed? (obligation cost for trade)           |
|        |                                                               |
|        +-- GOAP evaluates:                                             |
|        |   buy_from_legal_supplier:  cost = 12g + 0 obligation        |
|        |   buy_from_black_market:    cost = 8g + 15 obligation        |
|        |   mine_own_iron:            cost = 20 (time) + 0 obligation  |
|        |                                                               |
|        v                                                               |
|   Execute via organization:                                            |
|   1. Contract created (org as party, not character)                    |
|   2. Payment from org wallet (Currency debit)                          |
|   3. Goods to org inventory (Inventory transfer)                       |
|   4. Org seed grows (commerce.trade domain)                            |
|   5. If trade with Outlawed org: obligation violation reported         |
|        |                                                               |
|        v                                                               |
|   Downstream effects:                                                  |
|   - Supplier org seed grows (commerce.trade)                           |
|   - Local sovereign collects trade tax (if Chartered)                  |
|   - Analytics tracks economic activity                                 |
|   - Trade volume affects local faction territory norms                 |
|   - God of Commerce (Hermes) may notice exceptional trades             |
+-----------------------------------------------------------------------+
```

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs to report. Plugin is in pre-implementation phase.*

### Intentional Quirks (Documented Behavior)

1. **Organization type is opaque string**: Not an enum. Follows the same extensibility pattern as seed type codes, collection type codes, faction codes. `household`, `shop`, `guild`, `trading_company`, `temple`, `military_unit`, `criminal_enterprise`, `noble_house` are all just type codes. lib-organization stores whatever type string is provided. New types require a seed type registration and role template definition.

2. **Legal status is the only enum**: Unlike organization types and role codes (which are opaque strings), legal status IS an enum (`Chartered`, `Licensed`, `Tolerated`, `Outlawed`). This is because legal status has mechanical consequences that differ categorically -- the distinction between "contracts are enforceable" and "contracts are not enforceable" is binary, not a spectrum. New legal statuses would require code changes.

3. **Organizations don't directly participate in arbitration**: Organizations are parties to arbitration cases through their representative members (characters with `represent` permission). lib-arbitration sees entity type + entity ID, which can be an organization ID, but the representing character presents the case. This follows the real-world pattern where organizations have legal personhood but act through representatives.

4. **Seed type matches organization type**: The organization's seed type code is always the same as its organization type code. A `shop` organization has a `shop` seed. This means every new organization type requires a corresponding seed type definition. This is intentional -- different organization types grow differently and have different capability progressions.

5. **Asset registration is referential, not custodial**: Registering an asset to an organization creates a reference, not a transfer. The wallet/inventory/location is owned by the organization conceptually, but the underlying service (Currency, Inventory, Location) still uses its own ownership model. lib-organization tracks the association; the asset service manages the asset. Dissolution asset division works by updating these references (re-pointing the wallet owner, transferring inventory items).

6. **Household creation is external to lib-organization**: lib-organization provides the infrastructure for households but doesn't decide when households form. Character lifecycle events (marriage, coming of age, land grant) trigger household creation through game-specific logic (ABML behaviors, Gardener actions, quest rewards). lib-organization's `Create` endpoint is called by whatever system decides a household should exist.

7. **Members can belong to multiple organizations**: A character can be a member of a household AND an employee of a shop AND a member of a guild (up to `MaxOrganizationsPerCharacter`). These are orthogonal organizational relationships. The `${organization.*}` variable provider aggregates across all memberships.

8. **Succession is organization-level, not type-level**: Each organization instance has its own succession rules. Two households in the same realm can have different succession modes (one primogeniture, one elective). The org type provides defaults; the instance overrides.

9. **Charter contracts are between faction and organization**: The sovereign faction is one party and the organization is the other. This requires lib-contract to support faction entity type as a contract party, which may not yet be implemented. If not, the faction leader character serves as proxy (same pattern as organizational representation in arbitration).

### Design Considerations (Requires Planning)

1. **Contract party entity types**: lib-contract currently supports character and account entity types as parties. Organizations and factions as contract parties requires extending the party model. This is a prerequisite for charter contracts, organizational trade agreements, and employment contracts.

2. **Wallet ownership model**: lib-currency's wallet ownership model needs to support organization entity type. Currently wallets are scoped to character/account. Organizational wallets require `ownerType: "organization"` + `ownerId: organizationId`. This is the same polymorphic ownership pattern used by Seed (`AllowedOwnerTypes`).

3. **Inventory ownership model**: Same as wallet -- lib-inventory needs to support organization-owned containers. Characters with `manage_assets` role permission can access the organization's inventory.

4. **Seed splitting on dissolution**: When an organization splits (branch family, business partition), the seed growth should be divided. This requires lib-seed to support growth transfer between seeds -- taking a portion of one seed's accumulated growth and crediting it to a new seed. The proportion is determined by the dissolution terms (governance parameters from the sovereign's procedural template).

5. **NPC economic behavior integration**: NPC actors need ABML behaviors that reference organizational context for economic decisions. This is an ABML authoring task -- behavior documents for shopkeepers, traders, crafters, and other economic actors need to use `${organization.*}` variables. The Actor variable provider factory must be implemented first.

6. **Role permission enforcement**: Role permissions (manage_assets, hire, fire, trade, represent) need enforcement at the API level. Each organization mutation endpoint checks whether the requesting character has the required permission in the target organization. This is internal permission checking (not the WebSocket-level RBAC that lib-permission handles).

7. **Household-Gardener integration**: The player's household IS their primary garden context in the Gardener system. This means household creation/dissolution/succession events need to propagate to Gardener for UX updates. The integration boundary between "organization manages structure" and "gardener manages player experience" needs clear delineation.

8. **Tax collection mechanism**: Sovereign factions collect taxes from Chartered organizations. The mechanism: periodic milestone on the charter contract triggers a Currency transfer from org wallet to sovereign treasury. Tax rate is a governance parameter. This requires a background worker or the Contract milestone expiration system to handle periodic obligations.

9. **Mass re-chartering on sovereignty change**: When a new sovereign takes territory, potentially hundreds of organizations need re-evaluation. This should be a background process with configurable batch size and rate limiting to avoid overwhelming downstream services. The grace period configuration determines urgency.

10. **Organization dissolution and the content flywheel**: Organization histories (founding, growth, crises, dissolution) should feed into the content flywheel via lib-resource compression. A dissolved guild's history becomes narrative material for Storyline -- "the legendary Ironworkers' Guild that was disbanded when the dwarven enclave fell." The compression format needs design.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. This is the largest of the three changes in the sovereignty/arbitration/organization triad and the most independent -- it can be built incrementally without waiting for sovereignty or arbitration, starting with basic organization CRUD and member management. See the phased implementation plan above.*
