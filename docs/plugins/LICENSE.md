# License Plugin Deep Dive

> **Plugin**: lib-license
> **Schema**: schemas/license-api.yaml
> **Version**: 1.0.0
> **State Stores**: license-board-templates (MySQL), license-definitions (MySQL), license-boards (MySQL), license-board-cache (Redis), license-lock (Redis)

---

## Overview

The License service (L4 GameFeatures) provides grid-based progression boards (skill trees, license boards, tech trees) inspired by Final Fantasy XII's License Board system. It is a thin orchestration layer that combines Inventory (containers for license items), Items (license nodes as item instances), and Contracts (unlock behavior via prebound API execution) to manage entity progression across a grid. Boards support polymorphic ownership via `ownerType` + `ownerId` — characters, accounts, guilds, and locations can all own boards. Internal-only, never internet-facing. See [GitHub Issue #281](https://github.com/BeyondImmersion/bannou-service/issues/281) for the original design specification.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for board templates, definitions, board instances (MySQL) and board cache, locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Board-level and template-level distributed locks for mutation operations |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events (template/board CRUD) and gameplay events (unlock, unlock-failed) |
| lib-messaging (`IEventConsumer`) | Event consumer registration (currently unused — cleanup via lib-resource) |
| lib-contract (`IContractClient`) | Creating contract instances, setting template values, proposing, consenting, and completing milestones during unlock execution (L1 hard dependency) |
| lib-character (`ICharacterClient`) | Validating character existence and resolving realm context during board creation for character-type owners (L2 hard dependency) |
| lib-inventory (`IInventoryClient`) | Creating/deleting containers for board instances and reading container contents for cache rebuild (L2 hard dependency) |
| lib-item (`IItemClient`) | Creating/destroying item instances during unlock flow and compensation (L2 hard dependency) |
| lib-currency (`ICurrencyClient`) | Advisory LP balance checks in `CheckUnlockable` (L2 hard dependency) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence during template creation (L2 hard dependency) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-item | References lib-license in `ItemServiceModels.cs` documentation comment describing `ContractBindingType.Lifecycle` as managed by external orchestrators including lib-license |

No other services currently inject `ILicenseClient` or subscribe to license events. The generated `LicenseClient` exists in `bannou-service/Generated/Clients/` but has no consumers yet.

---

## State Storage

### Store: `license-board-templates` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `board-tpl:{boardTemplateId}` | `BoardTemplateModel` | Grid layout configuration, contract reference, adjacency settings, active flag |

### Store: `license-definitions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `lic-def:{boardTemplateId}:{code}` | `LicenseDefinitionModel` | Individual node on a template: position, LP cost, item template, prerequisites, metadata |

### Store: `license-boards` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `board:{boardId}` | `BoardInstanceModel` | Owner-specific board instance linking ownerType+ownerId to template with container and realm context |
| `board-owner:{ownerType}:{ownerId}:{boardTemplateId}` | `BoardInstanceModel` | Uniqueness key enforcing one board per template per owner (stores same data as `board:{boardId}`) |

### Store: `license-board-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cache:{boardId}` | `BoardCacheModel` | Unlocked positions cache with TTL; rebuilt from inventory on cache miss |

### Store: `license-lock` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `board:{boardId}` | Distributed lock | Board-level mutex for unlock and delete operations |
| `tpl:{boardTemplateId}` | Distributed lock | Template-level mutex for template updates/deletes and all definition mutations (add, update, remove, seed) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `license-board-template.created` | `LicenseBoardTemplateCreatedEvent` | Board template created via `CreateBoardTemplateAsync` |
| `license-board-template.updated` | `LicenseBoardTemplateUpdatedEvent` | Board template updated via `UpdateBoardTemplateAsync` |
| `license-board-template.deleted` | `LicenseBoardTemplateDeletedEvent` | Board template deleted via `DeleteBoardTemplateAsync` |
| `license-board.created` | `LicenseBoardCreatedEvent` | Board instance created via `CreateBoardAsync` |
| `license-board.deleted` | `LicenseBoardDeletedEvent` | Board instance deleted via `DeleteBoardAsync` |
| `license-board.cloned` | `LicenseBoardClonedEvent` | Board unlock state cloned to new owner via `CloneBoardAsync` (includes sourceBoardId, targetBoardId, targetOwnerType, targetOwnerId, targetGameServiceId, licensesCloned) |
| `license.unlocked` | `LicenseUnlockedEvent` | License successfully unlocked (includes boardId, ownerType, ownerId, licenseCode, position, itemInstanceId, contractInstanceId, lpCost) |
| `license.unlock-failed` | `LicenseUnlockFailedEvent` | License unlock failed (includes boardId, ownerType, ownerId, licenseCode, reason enum) |

**Note**: `license-board.updated` is NOT published — boards are immutable after creation. The `LicenseBoardUpdatedEvent` model exists as an unavoidable byproduct of `x-lifecycle` auto-generation but is intentionally excluded from `x-event-publications`.

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| *(via lib-resource cleanup callback)* | `CleanupByOwnerAsync` | Queries all boards for the deleted owner (by ownerType + ownerId), deletes inventory containers (destroying contained license items), deletes board instance records, deletes owner-template uniqueness keys, and invalidates board caches |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxBoardsPerOwner` | `LICENSE_MAX_BOARDS_PER_OWNER` | 10 | Maximum active boards a single owner entity can have; enforced in `CreateBoardAsync` by counting existing boards for the ownerType+ownerId combination |
| `MaxDefinitionsPerBoard` | `LICENSE_MAX_DEFINITIONS_PER_BOARD` | 200 | Maximum license definitions per board template; enforced in `AddLicenseDefinitionAsync` and `SeedBoardTemplateAsync` |
| `LockTimeoutSeconds` | `LICENSE_LOCK_TIMEOUT_SECONDS` | 30 | Distributed lock TTL for board and template mutations |
| `BoardCacheTtlSeconds` | `LICENSE_BOARD_CACHE_TTL_SECONDS` | 300 | Redis TTL for board state cache; cache is rebuilt from inventory on miss |
| `DefaultPageSize` | `LICENSE_DEFAULT_PAGE_SIZE` | 20 | Default page size for `ListBoardTemplatesAsync` paginated queries |
| `MaxConcurrencyRetries` | `LICENSE_MAX_CONCURRENCY_RETRIES` | 3 | Maximum ETag-based retry attempts when updating board cache after unlock |
| `DefaultAdjacencyMode` | `LICENSE_DEFAULT_ADJACENCY_MODE` | `EightWay` | Default grid traversal mode for new board templates when not specified |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<LicenseService>` | Structured logging |
| `LicenseServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (5 stores) |
| `IDistributedLockProvider` | Distributed locks via `license-lock` store |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription registration (unused — cleanup via lib-resource) |
| `IContractClient` | Contract lifecycle operations during unlock |
| `ICharacterClient` | Character validation and realm context resolution for character-type owners |
| `IInventoryClient` | Container CRUD and content queries |
| `IItemClient` | Item instance creation and destruction (saga compensation) |
| `ICurrencyClient` | Advisory LP balance checks |
| `IGameServiceClient` | Game service existence validation |

**Internal Helpers:**
- `IsAdjacent(x1, y1, x2, y2, mode)` - Static method computing grid adjacency for FourWay (Manhattan distance = 1) and EightWay (Chebyshev distance <= 1)
- `LoadOrRebuildBoardCacheAsync(board, definitions, ct)` - Cache read-through: tries Redis first, on miss rebuilds from inventory container contents by matching item template IDs to definitions
- `CompensateItemCreationAsync(itemInstanceId, boardId, licenseCode, ct)` - Saga compensation: destroys an item created during unlock if the subsequent contract fails. Publishes error event on compensation failure.
- `PublishUnlockFailedAsync(boardId, ownerType, ownerId, licenseCode, reason, ct)` - Helper for publishing `license.unlock-failed` events
- `MapToContainerOwnerType(ownerType)` - Maps opaque owner type string to `ContainerOwnerType` for inventory operations (character, account, location, guild → mapped; others → null)
- `MapToWalletOwnerType(ownerType)` - Maps owner type string to `WalletOwnerType` for currency operations (character, account, guild, npc → mapped; others → null)
- `MapToEntityType(ownerType)` - Maps owner type string to `EntityType` for contract parties (character, account, realm, guild, location, actor → mapped; others → null)
- `IsValidOwnerType(ownerType)` - Validates owner type string is non-empty and doesn't contain the `:` key separator
- `MapTemplateToResponse`, `MapDefinitionToResponse`, `MapBoardToResponse` - Static model-to-response mapping helpers
- `LicenseTopics` - Static class with topic string constants for `license.unlocked`, `license.unlock-failed`, and `license-board.cloned`

---

## API Endpoints (Implementation Notes)

### Board Template Management (6 endpoints, developer role)

Standard CRUD on board templates with `CreateBoardTemplateAsync` validating game service existence, contract template existence, starting nodes within grid bounds, `allowedOwnerTypes` mapping to `ContainerOwnerType` (two-gate validation: gate 1 at template creation, gate 2 at board creation), and defaulting adjacency mode from config. `UpdateBoardTemplateAsync` acquires a template-level distributed lock before mutation, validates any updated `allowedOwnerTypes`, and publishes a lifecycle event. `DeleteBoardTemplateAsync` also acquires a template lock, checks for active board instances, and performs a hard delete.

`ListBoardTemplatesAsync` supports paginated listing filtered by `GameServiceId` using `QueryAsync` with configurable page size.

`SeedBoardTemplateAsync` (developer-only) bulk-creates definitions for a template under a template-level distributed lock. Validates grid bounds, duplicate codes (against existing definitions and within the batch), duplicate positions (against existing definitions and within the batch), and `MaxDefinitionsPerBoard` limits (existing + new). Out-of-bounds and duplicate definitions are skipped with warning logs.

### License Definition Management (5 endpoints, developer role)

Standard CRUD on license definitions keyed by `{boardTemplateId}:{code}`. `AddLicenseDefinitionAsync` acquires a template-level lock, then validates: template exists, definition code is unique within template, position is unique within template, position is within grid bounds, item template exists, and total definitions don't exceed `MaxDefinitionsPerBoard`.

`UpdateLicenseDefinitionAsync` acquires a template lock before updating. Mutable fields: `LpCost`, `Prerequisites`, `Description`, `Metadata`. Position is immutable after creation.

`RemoveLicenseDefinitionAsync` acquires a template lock, then checks all board instances for the template and uses `LoadOrRebuildBoardCacheAsync` to verify no boards have the definition unlocked before allowing removal. On cache miss (TTL expiry), the cache is rebuilt from inventory (authoritative source) before checking.

### Board Instance Management (5 endpoints, mixed roles)

`CreateBoardAsync` (user role): Validates ownerType format (no `:` separator), validates ownerType is in template's `allowedOwnerTypes`, performs owner-type-specific validation (character owners: validates character exists via ICharacterClient, resolves realm context; other owners: accepts provided realmId). Enforces one board per template per owner via uniqueness key (`board-owner:{ownerType}:{ownerId}:{boardTemplateId}`), enforces `MaxBoardsPerOwner` limit by counting existing boards. Creates an Inventory container with mapped `ContainerOwnerType` and initializes an empty Redis cache entry. Registers character-type owners as resource references via lib-resource.

`GetBoardAsync` (developer role): Simple key lookup.

`ListBoardsByOwnerAsync` (user role): Queries all boards for an owner (by ownerType + ownerId), optionally filtered by game service.

`DeleteBoardAsync` (developer role): Acquires distributed lock, deletes inventory container (which destroys contained items), deletes board record, uniqueness key, and cache entry. Unregisters character-type resource references. Publishes lifecycle event.

`CloneBoardAsync` (developer role): Developer-only NPC tooling for bulk state initialization. Reads unlock state from source board, validates target owner (type format, allowed types, character existence for character owners, uniqueness, max boards), creates new inventory container, bulk-creates item instances with `ItemOriginType.Spawn` for each unlocked license, saves board record and cache with cloned unlock state. Skips contracts entirely (admin tooling, not gameplay). Publishes both `license-board.created` lifecycle event and `license-board.cloned` custom event. Registers character-type resource references. On item creation failure, cleans up the container (cascading to any items created) and returns error.

### Gameplay Operations (3 endpoints, user role)

**`UnlockLicenseAsync`** - The core operation. 14-step flow under distributed lock with saga compensation:
1. Acquire board lock
2. Load board instance
3. Load board template
4. Load license definition by code
5. Load all definitions for adjacency/prerequisite checks
6. Load/rebuild board cache from inventory
7. Check not already unlocked
8. Validate adjacency (starting node bypass or adjacent to unlocked node)
9. Validate realm context available on board (stored at creation time — no character load needed)
10. **Create item instance** in board container using `board.RealmId` (easily reversible — saga ordering)
11. **Contract lifecycle**: create instance with `MapToEntityType(board.OwnerType)` for party entity type, set template values (ownerType, ownerId, boardId, lpCost, etc.), propose, consent both parties, complete "unlock" milestone (LP deduction via prebound API). If contract fails, **compensate by destroying the item** from step 10.
12. Update board cache with optimistic concurrency retry (ETag-based, up to `MaxConcurrencyRetries`)
13. Publish `license.unlocked` event (includes ownerType + ownerId)
14. Return success

**`CheckUnlockableAsync`** - Read-only advisory check. Evaluates adjacency, prerequisites, and LP balance. LP check maps `board.OwnerType` via `MapToWalletOwnerType()` — if mappable, sums all wallet balances as an approximation; if unmappable, returns `lpSufficient = null` (not applicable for this owner type). Actual LP deduction is handled by the contract template's prebound API execution.

**`GetBoardStateAsync`** - Computes the full board state including per-node status (`Locked`, `Unlockable`, `Unlocked`). Loads template, all definitions, and cache, then classifies each node based on adjacency to unlocked positions.

---

## Visual Aid

```
Unlock License Flow (under distributed lock, saga-ordered)
══════════════════════════════════════════════════════════

  ┌─────────────┐     ┌──────────────┐     ┌───────────────┐
  │  Board Store │     │  Definition  │     │  Board Cache   │
  │   (MySQL)    │     │   Store      │     │   (Redis)      │
  │              │     │   (MySQL)    │     │                │
  └──────┬───────┘     └──────┬───────┘     └───────┬────────┘
         │                    │                     │
    2. Load board        4-5. Load def       6. Try cache
         │              + all defs                  │
         ▼                    ▼                     ▼
  ┌─────────────────────────────────────────────────────────┐
  │                    Validation Phase                      │
  │  7. Not already unlocked (cache)                        │
  │  8. Adjacent to unlocked OR starting node               │
  │  9. Realm context available (stored on board)           │
  └──────────────────────────┬──────────────────────────────┘
                             │
                    10. Create item (reversible)
                             │
         ┌───────────────────▼────────────────────┐
         │          IItemClient (L2)               │
         │  Create instance in board container     │
         │  using board.RealmId                    │
         └───────────────────┬────────────────────┘
                             │
                    11. Contract Flow
                             │
         ┌───────────────────▼────────────────────┐
         │         IContractClient (L1)            │
         │  Create instance → Set values →         │
         │  Propose → Consent×2 → Complete         │
         │  "unlock" milestone                     │
         │  (prebound APIs handle LP deduction)    │
         │                                         │
         │  ON FAILURE: Destroy item (compensate)  │
         └───────────────────┬────────────────────┘
                             │
                    12. Update cache
                             │
         ┌───────────────────▼────────────────────┐
         │     Board Cache (Redis, ETag retry)     │
         │  Add unlocked entry → Save with TTL     │
         └───────────────────┬────────────────────┘
                             │
                    13. Publish event
                             │
                             ▼
                   license.unlocked
```

---

## Stubs & Unimplemented Features

- ~~**`license-board.updated` event**: Declared in `license-events.yaml` as a lifecycle event with `LicenseBoardUpdatedEvent` model generated, but never published anywhere in the service code.~~ RESOLVED: Removed from `x-event-publications` in schema. The generated model is an unavoidable byproduct of `x-lifecycle` but is intentionally not published. Boards are immutable after creation. See [#355](https://github.com/beyond-immersion/bannou-service/issues/355).

---

## Potential Extensions

- **Board reset/respec**: Allow owners to reset all unlocked licenses on a board, returning LP and removing items. Would require a new endpoint, contract template for refund execution, and inventory bulk-delete.
<!-- AUDIT:NEEDS_DESIGN:2026-02-09:https://github.com/beyond-immersion/bannou-service/issues/356 -->
- ~~**Board sharing/copying**: Developer-only endpoint for cloning NPC progression.~~ FIXED: Implemented `CloneBoardAsync` — reads unlock state from source board, creates new board for target owner, bulk-creates item instances with `ItemOriginType.Spawn`. Skips contracts entirely (admin tooling). Publishes single `license-board.cloned` event. Validates target owner type, uniqueness, max boards. Cleanup on item creation failure. See [#357](https://github.com/beyond-immersion/bannou-service/issues/357).
- ~~**Resource reference integration**: Register boards as references to characters via lib-resource for proper cleanup coordination instead of direct event handling.~~ FIXED: Added `x-references` to schema, generated reference tracking helpers, implemented `CleanupByOwnerAsync` endpoint, registered cleanup callbacks in plugin startup, removed direct `character.deleted` event subscription.
- ~~**Achievement integration**: Publish events when specific board completion thresholds are reached.~~ CLOSED: Not a License concern. License already publishes `license.unlocked` events with all data needed for milestone derivation. Achievement integration belongs in Analytics (Source → Analytics → Achievement pipeline). See [#358](https://github.com/beyond-immersion/bannou-service/issues/358).
- ~~**Polymorphic ownership**: Replace `characterId` with `ownerType` + `ownerId` to support non-character boards.~~ FIXED: Implemented polymorphic ownership with `ownerType` (opaque string) + `ownerId` (Guid) + `realmId` (nullable). Templates declare `allowedOwnerTypes` with two-gate validation (template creation + board creation). Owner type mapping functions bridge to ContainerOwnerType, WalletOwnerType, and EntityType. Realm context stored at board creation time — no character load during unlock. LP check returns `null` for unmappable owner types. Day-one character x-references via lib-resource; other owner types deferred. See [#368](https://github.com/beyond-immersion/bannou-service/issues/368).

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No known bugs at this time.

~~**`AddLicenseDefinitionAsync` missing distributed lock**~~ FIXED: Template-level lock now acquired, matching `UpdateLicenseDefinitionAsync` and `SeedBoardTemplateAsync` patterns.

~~**`RemoveLicenseDefinitionAsync` missing distributed lock**~~ FIXED: Template-level lock now acquired before cache checks and definition deletion.

~~**`SeedBoardTemplateAsync` skips item template validation**~~ FIXED: Each definition's `ItemTemplateId` is now validated via `_itemClient.GetItemTemplateAsync`. Invalid item templates are skipped (consistent with the seed method's skip-on-invalid pattern for bounds and duplicates).

### Intentional Quirks (Documented Behavior)

- **Board cache is non-authoritative**: The Redis cache is a read-through cache with TTL. Inventory is the authoritative source of truth. On cache miss, `LoadOrRebuildBoardCacheAsync` rebuilds from inventory container contents by matching item template IDs to definitions. The `UnlockedAt` timestamp is approximated as `board.CreatedAt` during rebuild since actual unlock times are not stored in inventory.

- **Cache rebuild matching is first-match**: When multiple definitions share the same `ItemTemplateId`, the cache rebuild uses `FirstOrDefault` against unmatched definitions. This means if two definitions use the same item template and both are unlocked, the rebuild assigns them in definition-iteration order, which may not match the original unlock order. This only affects display metadata (timestamps), not correctness.

- **CheckUnlockable LP check is advisory and owner-type-aware**: The LP balance check first maps `board.OwnerType` to `WalletOwnerType`. If the mapping exists, it sums all wallet balances as an approximation. If unmappable (e.g., location owners don't have wallets), `lpSufficient` is `null` (not applicable). The actual LP deduction is handled by the contract template's prebound API execution (which knows the specific currency). The advisory check can give false positives (sufficient total balance but wrong currency) or false negatives (sufficient in one currency but low total). On currency service failure, `lpSufficient` defaults to `false` (conservative).

- **Contract flow is synchronous and auto-consented**: The unlock flow creates a contract instance, auto-proposes it, auto-consents both parties, and completes the milestone in a single synchronous sequence. This is intentional -- the contract is used for its prebound API execution (LP deduction, etc.), not for multi-party negotiation. The "licensor" party is the game service entity ID.

- **Delete is hard delete**: `DeleteBoardTemplateAsync` performs `BoardTemplateStore.DeleteAsync()` (hard delete), blocked by active board instances. The `IsActive` flag on `BoardTemplateModel` exists for preventing new board creation from inactive templates, not for soft-delete semantics.

- **One board per template per owner**: Enforced via the `board-owner:{ownerType}:{ownerId}:{boardTemplateId}` uniqueness key. An owner entity cannot have two instances of the same board template. This is stored as a full `BoardInstanceModel` duplicate in the same MySQL store.

### Design Considerations (Requires Planning)

- ~~**No rollback on partial unlock failure**~~ FIXED: Unlock flow reordered with saga compensation. Item creation now happens BEFORE contract completion (easily reversible action first). If contract fails after item creation, the item is destroyed via `CompensateItemCreationAsync`. No failure mode results in LP deducted without item granted. See [#360](https://github.com/beyond-immersion/bannou-service/issues/360).

- ~~**`license-board.updated` event declared but never published**~~ FIXED: Removed from `x-event-publications`. Boards are immutable — the Updated concept doesn't apply. The `x-lifecycle`-generated model remains as harmless dead code. See [#355](https://github.com/beyond-immersion/bannou-service/issues/355).

---

## Work Tracking

| Issue | Status | Summary |
|-------|--------|---------|
| [#355](https://github.com/beyond-immersion/bannou-service/issues/355) | Fixed | Removed unused `license-board.updated` from event publications |
| [#356](https://github.com/beyond-immersion/bannou-service/issues/356) | Open (needs game design) | Board reset/respec — refund %, partial vs full, cooldown |
| [#357](https://github.com/beyond-immersion/bannou-service/issues/357) | Fixed | Board clone for NPC progression — `CloneBoardAsync` with bulk item creation, event publishing, cleanup on failure |
| [#360](https://github.com/beyond-immersion/bannou-service/issues/360) | Fixed | Saga-ordered unlock flow with item-before-contract compensation |
| [#368](https://github.com/beyond-immersion/bannou-service/issues/368) | Fixed | Polymorphic ownership (OwnerType + OwnerId + RealmId) with two-gate validation, owner type mapping, and nullable LP checks |
