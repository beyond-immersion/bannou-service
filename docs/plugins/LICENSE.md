# License Plugin Deep Dive

> **Plugin**: lib-license
> **Schema**: schemas/license-api.yaml
> **Version**: 1.0.0
> **State Stores**: license-board-templates (MySQL), license-definitions (MySQL), license-boards (MySQL), license-board-cache (Redis), license-lock (Redis)

---

## Overview

The License service (L4 GameFeatures) provides grid-based progression boards (skill trees, license boards, tech trees) inspired by Final Fantasy XII's License Board system. It is a thin orchestration layer that combines Inventory (containers for license items), Items (license nodes as item instances), and Contracts (unlock behavior via prebound API execution) to manage character progression across a grid. Internal-only, never internet-facing. See [GitHub Issue #281](https://github.com/BeyondImmersion/bannou-service/issues/281) for the original design specification.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for board templates, definitions, board instances (MySQL) and board cache, locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Board-level and template-level distributed locks for mutation operations |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events (template/board CRUD) and gameplay events (unlock, unlock-failed) |
| lib-messaging (`IEventConsumer`) | Subscribing to `character.deleted` for board cleanup |
| lib-contract (`IContractClient`) | Creating contract instances, setting template values, proposing, consenting, and completing milestones during unlock execution (L1 hard dependency) |
| lib-character (`ICharacterClient`) | Validating character existence during board creation and fetching realm context during unlock (L2 hard dependency) |
| lib-inventory (`IInventoryClient`) | Creating/deleting containers for board instances and reading container contents for cache rebuild (L2 hard dependency) |
| lib-item (`IItemClient`) | Creating item instances when licenses are unlocked (L2 hard dependency) |
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
| `board:{boardId}` | `BoardInstanceModel` | Character-specific board instance linking character to template with container |
| `board-char:{characterId}:{boardTemplateId}` | `BoardInstanceModel` | Uniqueness key enforcing one board per template per character (stores same data as `board:{boardId}`) |

### Store: `license-board-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cache:{boardId}` | `BoardCacheModel` | Unlocked positions cache with TTL; rebuilt from inventory on cache miss |

### Store: `license-lock` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `board:{boardId}` | Distributed lock | Board-level mutex for unlock and delete operations |
| `tpl:{boardTemplateId}` | Distributed lock | Template-level mutex for template and definition mutations |

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
| `license.unlocked` | `LicenseUnlockedEvent` | License successfully unlocked (includes boardId, characterId, licenseCode, position, itemInstanceId, contractInstanceId, lpCost) |
| `license.unlock-failed` | `LicenseUnlockFailedEvent` | License unlock failed (includes boardId, characterId, licenseCode, reason enum) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `character.deleted` | `HandleCharacterDeletedAsync` | Queries all boards for the deleted character, deletes inventory containers (destroying contained license items), deletes board instance records, deletes character-template uniqueness keys, and invalidates board caches |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxBoardsPerCharacter` | `LICENSE_MAX_BOARDS_PER_CHARACTER` | 10 | Maximum active boards a single character can have; enforced in `CreateBoardAsync` by counting existing boards |
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
| `IEventConsumer` | Event subscription registration (character.deleted) |
| `IContractClient` | Contract lifecycle operations during unlock |
| `ICharacterClient` | Character validation and realm context |
| `IInventoryClient` | Container CRUD and content queries |
| `IItemClient` | Item instance creation |
| `ICurrencyClient` | Advisory LP balance checks |
| `IGameServiceClient` | Game service existence validation |

**Internal Helpers:**
- `IsAdjacent(x1, y1, x2, y2, mode)` - Static method computing grid adjacency for FourWay (Manhattan distance = 1) and EightWay (Chebyshev distance <= 1)
- `LoadOrRebuildBoardCacheAsync(board, definitions, ct)` - Cache read-through: tries Redis first, on miss rebuilds from inventory container contents by matching item template IDs to definitions
- `PublishUnlockFailedAsync(boardId, characterId, licenseCode, reason, ct)` - Helper for publishing `license.unlock-failed` events
- `MapTemplateToResponse`, `MapDefinitionToResponse`, `MapBoardToResponse` - Static model-to-response mapping helpers
- `LicenseTopics` - Static class with topic string constants for `license.unlocked` and `license.unlock-failed`

---

## API Endpoints (Implementation Notes)

### Board Template Management (6 endpoints, developer role)

Standard CRUD on board templates with `CreateBoardTemplateAsync` validating game service existence and defaulting adjacency mode from config. `UpdateBoardTemplateAsync` acquires a template-level distributed lock before mutation and publishes a lifecycle event. `DeleteBoardTemplateAsync` also acquires a template lock, checks for active board instances, and performs a hard delete.

`ListBoardTemplatesAsync` supports paginated listing filtered by `GameServiceId` using `QueryAsync` with configurable page size.

`SeedBoardTemplateAsync` (developer-only) bulk-creates definitions for a template under a template-level distributed lock. Validates grid bounds, duplicate codes (against existing definitions and within the batch), duplicate positions (against existing definitions and within the batch), and `MaxDefinitionsPerBoard` limits (existing + new). Out-of-bounds and duplicate definitions are skipped with warning logs.

### License Definition Management (5 endpoints, developer role)

Standard CRUD on license definitions keyed by `{boardTemplateId}:{code}`. `AddLicenseDefinitionAsync` validates: template exists, definition code is unique within template, position is within grid bounds, and total definitions don't exceed `MaxDefinitionsPerBoard`. Uses a template-level lock.

`UpdateLicenseDefinitionAsync` acquires a template lock before updating. Position changes are allowed.

`RemoveLicenseDefinitionAsync` checks all board instances for the template and uses `LoadOrRebuildBoardCacheAsync` to verify no boards have the definition unlocked before allowing removal. On cache miss (TTL expiry), the cache is rebuilt from inventory (authoritative source) before checking.

### Board Instance Management (4 endpoints, mixed roles)

`CreateBoardAsync` (user role): Validates character exists, template exists and is active, game service matches, enforces one board per template per character via uniqueness key, enforces `MaxBoardsPerCharacter` limit by counting existing boards. Creates an Inventory container of type `LicenseBoard` and initializes an empty Redis cache entry.

`GetBoardAsync` (developer role): Simple key lookup.

`ListBoardsByCharacterAsync` (user role): Queries all boards for a character, optionally filtered by game service.

`DeleteBoardAsync` (developer role): Acquires distributed lock, deletes inventory container (which destroys contained items), deletes board record, uniqueness key, and cache entry. Publishes lifecycle event.

### Gameplay Operations (3 endpoints, user role)

**`UnlockLicenseAsync`** - The core operation. 15-step flow under distributed lock:
1. Acquire board lock
2. Load board instance
3. Load board template
4. Load license definition by code
5. Load all definitions for adjacency/prerequisite checks
6. Load/rebuild board cache from inventory
7. Check not already unlocked
8. Validate adjacency (starting node bypass or adjacent to unlocked node)
9. Validate non-adjacent prerequisites (codes that must be unlocked anywhere on board)
10. Create contract instance with licensee (character) and licensor (game service) parties
11. Set contract template values (characterId, boardId, lpCost, licenseCode, itemTemplateId, gameServiceId, plus definition metadata)
12. Auto-propose, auto-consent both parties, complete "unlock" milestone
13. Load character for realm context, create item instance in board container
14. Update board cache with optimistic concurrency retry (ETag-based, up to `MaxConcurrencyRetries`)
15. Publish `license.unlocked` event

**`CheckUnlockableAsync`** - Read-only advisory check. Evaluates adjacency, prerequisites, and LP balance. LP check sums all wallet balances as an approximation; actual LP deduction is handled by the contract template's prebound API execution.

**`GetBoardStateAsync`** - Computes the full board state including per-node status (`Locked`, `Unlockable`, `Unlocked`). Loads template, all definitions, and cache, then classifies each node based on adjacency to unlocked positions.

---

## Visual Aid

```
Unlock License Flow (under distributed lock)
═════════════════════════════════════════════

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
  │  9. Prerequisites met (all required codes unlocked)     │
  └──────────────────────────┬──────────────────────────────┘
                             │
                    10-12. Contract Flow
                             │
         ┌───────────────────▼────────────────────┐
         │         IContractClient (L1)            │
         │  Create instance → Set values →         │
         │  Propose → Consent×2 → Complete         │
         │  "unlock" milestone                     │
         │  (prebound APIs handle LP deduction)    │
         └───────────────────┬────────────────────┘
                             │
                    13. Create item
                             │
         ┌───────────────────▼────────────────────┐
         │          IItemClient (L2)               │
         │  Create instance in board container     │
         └───────────────────┬────────────────────┘
                             │
                    14. Update cache
                             │
         ┌───────────────────▼────────────────────┐
         │     Board Cache (Redis, ETag retry)     │
         │  Add unlocked entry → Save with TTL     │
         └───────────────────┬────────────────────┘
                             │
                    15. Publish event
                             │
                             ▼
                   license.unlocked
```

---

## Stubs & Unimplemented Features

- **`license-board.updated` event**: Declared in `license-events.yaml` as a lifecycle event with `LicenseBoardUpdatedEvent` model generated, but never published anywhere in the service code. No board update endpoint exists either -- boards are immutable after creation (only licenses within them change).

---

## Potential Extensions

- **Board reset/respec**: Allow characters to reset all unlocked licenses on a board, returning LP and removing items. Would require a new endpoint, contract template for refund execution, and inventory bulk-delete.
- **Board sharing/copying**: Allow copying a board state from one character to another, e.g., for cloning NPC progression.
- **Resource reference integration**: Register boards as references to characters via lib-resource for proper cleanup coordination instead of direct event handling.
- **Achievement integration**: Publish events when specific board completion thresholds are reached (e.g., "50% unlocked", "full board clear") for the Achievement service to consume.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

- **Board cache is non-authoritative**: The Redis cache is a read-through cache with TTL. Inventory is the authoritative source of truth. On cache miss, `LoadOrRebuildBoardCacheAsync` rebuilds from inventory container contents by matching item template IDs to definitions. The `UnlockedAt` timestamp is approximated as `board.CreatedAt` during rebuild since actual unlock times are not stored in inventory.

- **Cache rebuild matching is first-match**: When multiple definitions share the same `ItemTemplateId`, the cache rebuild uses `FirstOrDefault` against unmatched definitions. This means if two definitions use the same item template and both are unlocked, the rebuild assigns them in definition-iteration order, which may not match the original unlock order. This only affects display metadata (timestamps), not correctness.

- **CheckUnlockable LP check is advisory**: The LP balance check sums ALL wallet balances across all currencies as an approximation. The actual LP deduction is handled by the contract template's prebound API execution (which knows the specific currency). The advisory check can give false positives (sufficient total balance but wrong currency) or false negatives (sufficient in one currency but low total).

- **Contract flow is synchronous and auto-consented**: The unlock flow creates a contract instance, auto-proposes it, auto-consents both parties, and completes the milestone in a single synchronous sequence. This is intentional -- the contract is used for its prebound API execution (LP deduction, etc.), not for multi-party negotiation. The "licensor" party is the game service entity ID.

- **Delete is hard delete**: `DeleteBoardTemplateAsync` performs `BoardTemplateStore.DeleteAsync()` (hard delete), blocked by active board instances. The `IsActive` flag on `BoardTemplateModel` exists for preventing new board creation from inactive templates, not for soft-delete semantics.

- **One board per template per character**: Enforced via the `board-char:{characterId}:{boardTemplateId}` uniqueness key. A character cannot have two instances of the same board template. This is stored as a full `BoardInstanceModel` duplicate in the same MySQL store.

### Design Considerations (Requires Planning)

- **No rollback on partial unlock failure**: The unlock flow performs multiple external calls sequentially (contract creation, contract propose/consent/complete, item creation, cache update). If item creation fails after the contract has been completed, the contract is orphaned in a completed state with no corresponding item. Similarly, if cache update fails after item creation, the item exists but the cache doesn't reflect it (mitigated by cache rebuild on next access). A compensation/saga pattern would be needed for full transactional guarantees.

- **`license-board.updated` event declared but never published**: The events schema declares a `license-board.updated` lifecycle event and the `LicenseBoardUpdatedEvent` model is generated, but no code path publishes it. Decision needed: should unlock operations publish board-updated events (since unlocking changes board state), or should this event be removed from the schema?

---

## Work Tracking

No active work items.
