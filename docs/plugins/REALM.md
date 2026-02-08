# Realm Plugin Deep Dive

> **Plugin**: lib-realm
> **Schema**: schemas/realm-api.yaml
> **Version**: 1.0.0
> **State Store**: realm-statestore (MySQL)

---

## Overview

The Realm service (L2 GameFoundation) manages top-level persistent worlds in the Arcadia game system. Realms are peer worlds (e.g., Omega, Arcadia, Fantasia) with no hierarchical relationships between them. Each realm operates as an independent world with distinct species populations and cultural contexts. Provides CRUD with deprecation lifecycle and seed-from-configuration support. Internal-only.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for realm definitions and indexes |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events and error events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no current handlers) |
| lib-resource (`IResourceClient`) | Reference checking before deletion to verify no external dependencies |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-location | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before location creation |
| lib-species | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before species operations |
| lib-character | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before character creation |
| lib-analytics | Subscribes to `realm.updated` event for cache invalidation (realm-to-gameService resolution cache) |
| lib-puppetmaster | Subscribes to `realm.created` event to auto-start regional watchers for newly created realms |

---

## State Storage

**Store**: `realm-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm:{realmId}` | `RealmModel` | Full realm definition (name, code, game service, deprecation state) |
| `code-index:{CODE}` | `string` | Code → realm ID lookup (uppercase normalized) |
| `all-realms` | `List<Guid>` | Master list of all realm IDs |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `realm.created` | `RealmCreatedEvent` | New realm created |
| `realm.updated` | `RealmUpdatedEvent` | Realm metadata modified, deprecated, or undeprecated (includes `changedFields`) |
| `realm.deleted` | `RealmDeletedEvent` | Realm permanently deleted |

Events are auto-generated from `x-lifecycle` in `realm-events.yaml`.

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| — | — | — | No service-specific configuration properties |

The generated `RealmServiceConfiguration` contains only the framework-level `ForceServiceId` property.

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RealmService>` | Scoped | Structured logging |
| `RealmServiceConfiguration` | Singleton | Framework config (empty) |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event registration (no handlers) |
| `IResourceClient` | Scoped | Reference checking before deletion |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### Read Operations (5 endpoints)

Standard read operations with two lookup strategies. **GetByCode** uses a two-step lookup: code index → realm ID → realm data. **Exists** returns an `exists` + `isActive` pair (always returns 200, never 404) for fast validation by dependent services. **ExistsBatch** validates multiple realm IDs in a single call using `GetBulkAsync`, returning per-realm results plus convenience flags (`allExist`, `allActive`) and lists of invalid/deprecated IDs. **List** supports filtering by category, active status, and deprecation, with pagination.

### Write Operations (3 endpoints)

- **Create**: Codes normalized to uppercase (`ToUpperInvariant()`). Validates code uniqueness via index. Stores realm data, code index, and appends to master list. Publishes `realm.created`.
- **Update**: Smart field tracking — only modifies fields where the new value differs from current. Tracks changed field names. Does not publish event if nothing changed. `GameServiceId` is mutable (can be updated).
- **Delete**: Three-step safety — realm MUST be deprecated first (returns 409 Conflict otherwise), then checks external references via `IResourceClient` (returns 409 Conflict if active references with RESTRICT policy exist), executes cleanup callbacks for CASCADE/DETACH sources, and only then removes all three keys (data, code index, list entry). Publishes `realm.deleted`. Fails closed if lib-resource is unavailable (returns 503 ServiceUnavailable).

### Deprecation Operations (2 endpoints)

- **Deprecate**: Sets `isDeprecated = true`, records timestamp and optional reason. Publishes `realm.updated` with deprecation fields in `changedFields`.
- **Undeprecate**: Clears deprecation state. Returns 400 Bad Request if realm is not currently deprecated.

### Seed Operation (1 endpoint)

Idempotent bulk creation with optional `updateExisting` flag. Processes each realm independently with per-item error handling (failures don't stop the batch). Returns counts of created, updated, skipped, and error messages.

---

## Visual Aid

```
Realm Deletion Safety Chain
=============================

  [Active Realm]
       │
       │ POST /realm/deprecate
       │   (requires admin)
       ▼
  [Deprecated Realm]
   isDeprecated: true
   deprecatedAt: timestamp
   deprecationReason: "..."
       │
       ├──── POST /realm/undeprecate ───► [Active Realm again]
       │       (reversal path)
       │
       │ POST /realm/delete
       │   (requires admin + deprecated)
       ▼
  [Permanently Deleted]
   - realm:{id} removed
   - code-index:{CODE} removed
   - all-realms list updated

  [Active Realm] ─── POST /realm/delete ──► 409 Conflict
                     (cannot skip deprecation step)
```

**Deletion also updates the visual aid above** — the resource check step sits between "Deprecated Realm" and "Permanently Deleted", returning 409 Conflict if active L4 references (e.g., realm-history) exist with RESTRICT policy.

---

## Stubs & Unimplemented Features

None identified.

---

## Potential Extensions

1. **Realm merge**: Consolidate deprecated realms into active ones, migrating all associated entities (characters, locations, species). Design questions resolved — see [#167](https://github.com/beyond-immersion/bannou-service/issues/167) for implementation plan.
<!-- AUDIT:READY:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/167 -->

---

## Known Quirks & Caveats

### Bugs

None identified.

### Intentional Quirks

1. **VOID realm is a convention, not enforced**: The API schema documents a "VOID realm" concept as a sink for entities whose realm should be removed. This is **intentionally not enforced** in realm service code — it's a seeding convention parallel to Species (VOID species) and RelationshipType (VOID type). The VOID realm should be seeded via `/realm/seed` with metadata `isSystemType: true`. Enforcement of VOID semantics (e.g., preventing new character creation in VOID realm) belongs to consuming services (Character, Location, Species), not the realm registry itself.

2. **Exists endpoint never returns 404**: Always returns `StatusCodes.OK` with `exists: true/false` and `isActive: true/false`. Callers must check both flags to determine usability. This avoids 404 semantics for a "check" operation.

3. **IsActive in Exists response is computed**: Returns `IsActive = model.IsActive && !model.IsDeprecated`. A realm with `IsActive=true` and `IsDeprecated=true` will return `IsActive=false` in the Exists response. The two properties are conflated.

4. **Metadata update has no equality check**: Unlike Name/Description/Category which check `body.X != model.X`, Metadata only checks `body.Metadata != null`. Sending identical metadata still triggers an update event with "metadata" in changedFields.

5. **Seed updates are silent (no events)**: When `UpdateExisting=true`, seed operations directly update the model without calling `PublishRealmUpdatedEventAsync`. Regular updates publish events; seed updates don't.

6. **LoadRealmsByIdsAsync silently drops missing realms**: If the all-realms list contains an ID that doesn't exist in data store, it's silently excluded from results.

7. **Deprecate is not idempotent**: Returns `StatusCodes.Conflict` if realm is already deprecated. Calling deprecate twice fails the second time rather than being a no-op.

8. **Event publishing uses aggressive retry with fail-loud crash semantics**: State store writes and event publishing are separate operations (no transactional outbox). However, lib-messaging's `TryPublishAsync` implements a sophisticated retry system via `MessageRetryBuffer`:
   - **On publish failure**: Messages are buffered in-memory and `TryPublishAsync` returns `true` (because delivery WILL be retried)
   - **Retry processing**: Every 5 seconds (configurable), the buffer is processed and failed messages are re-attempted
   - **Fail-loud thresholds**: If RabbitMQ stays down too long, the node **intentionally crashes** via `Environment.FailFast()`:
     - Buffer exceeds 10,000 messages (default `MESSAGING_RETRY_BUFFER_MAX_SIZE`)
     - Oldest message exceeds 5 minutes (default `MESSAGING_RETRY_BUFFER_MAX_AGE_SECONDS`)
   - **Why crash?**: Crashing triggers orchestrator restart, makes the failure visible in monitoring, and prevents silent data loss or unbounded memory growth

   **True loss scenarios** (rare):
   - Node dies (power failure, OOM kill) before buffer flushes
   - Clean shutdown with non-empty buffer (logged as warning)
   - Serialization failure (programming error, not retryable)

   The `PublishRealm*EventAsync` wrapper methods add an extra try/catch that logs warnings, but the underlying `TryPublishAsync` handles retry automatically. This is the **standard Bannou architecture** used by all services.

9. **GameServiceId is mutable**: `UpdateRealmRequest` allows changing `GameServiceId`, which reassigns the realm to a different game service. This is intentional - realms can be reorganized (e.g., game service consolidation, re-branding). Dependent services handle this via event-driven cache invalidation: Analytics subscribes to `realm.updated` and invalidates its `realm-to-gameService` cache, ensuring subsequent lookups fetch the new `GameServiceId`. Other services (Location, Species, Character) validate realm existence on creation but don't cache `GameServiceId`, so they're unaffected.

10. **All-realms list stored as single key**: The `all-realms` key stores a `List<Guid>` of all realm IDs. Create/Delete operations read the entire list, modify it, and write it back. This is intentional: realms are top-level game worlds (expected count: single-digit to ~50 max), not high-volume entities. The list serializes to <5KB even at 100 realms, and realm creation/deletion are rare administrative operations. Alternative patterns (Redis SCAN, secondary index tables) would add complexity without benefit at this scale. The pattern matches game-service which has similar constraints.

### Design Considerations

None outstanding. Reference counting for safe deletion was resolved via lib-resource integration (see [#170](https://github.com/beyond-immersion/bannou-service/issues/170), closed). Realm statistics was evaluated and closed as wrong-layer — entity statistics belong in Analytics (L4), not Realm (L2) (see [#169](https://github.com/beyond-immersion/bannou-service/issues/169), closed).

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Completed

- **2026-02-08**: Reference counting for safe deletion implemented via lib-resource integration. See [#170](https://github.com/beyond-immersion/bannou-service/issues/170) (closed). DeleteRealm now checks references and executes cleanup callbacks before proceeding.
- **2026-02-08**: Realm statistics evaluated and closed — entity count tracking belongs in Analytics (L4), not Realm (L2). See [#169](https://github.com/beyond-immersion/bannou-service/issues/169) (closed).

### Ready for Implementation

- **2026-02-08**: Realm merge feature — all design questions resolved from existing architecture and Species merge precedent. See [#167](https://github.com/beyond-immersion/bannou-service/issues/167) for resolved design answers. Ready to implement when prioritized.
