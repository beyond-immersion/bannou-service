# Mapping Plugin Fix Plan (Post-Review)

**Status**: IN PROGRESS (13/14 core issues resolved, 1 deferred)
**Last Updated**: 2025-01-08
**Reviewed By**: Claude Code

## Purpose
Provide a concrete, actionable fix plan for lib-mapping based on the current implementation vs the planning doc.
This includes: hard bugs, missing behavior, schema gaps, eventing behavior, indexing rules, and testing gaps.

## Decisions Locked In
- Authority tokens are opaque. Do NOT parse expiry from token; validate only against stored AuthorityRecord.
- Non-authority handling applies to ALL publish paths (RPC, publish-objects, ingest) and to all actions (create/update/delete).
- Ingest is an upsert-capable path with delete support; it is not an authority establishment mechanism.
- Authority takeover default is preserve_and_diff (in-place replacement; map identity stays intact).
- Ingest and publish-objects should emit both layer-level updates and object-level delta events when objects change.

---

## Implementation Status Summary

| # | Issue | Status | Notes |
|---|-------|--------|-------|
| 1 | Authority Token Validation | ✅ COMPLETE | ValidateAuthorityAsync uses AuthorityRecord.ExpiresAt only |
| 2 | Ingest Path (Actions + Broadcast) | ✅ COMPLETE | HandleIngestEventAsync handles create/update/delete and emits events |
| 3 | PublishObjectChanges NonAuthorityHandling | ✅ COMPLETE | HandleNonAuthorityObjectChangesAsync mirrors PublishMapUpdate behavior |
| 4 | accept_and_alert Event Publishing | ✅ COMPLETE | HandleNonAuthorityPublishAsync processes payload and publishes events |
| 5 | Snapshot/Query Without Bounds | ✅ COMPLETE | RequestSnapshotAsync works without bounds via QueryObjectsInRegionAsync |
| 6 | Index Cleanup on Update/Delete | ✅ COMPLETE | ProcessObjectChangeWithIndexCleanupAsync handles index maintenance |
| 7 | CreatedAt Preservation on Update | ✅ COMPLETE | Existing CreatedAt preserved, only UpdatedAt modified |
| 8 | Large Payload Handling (lib-asset) | ❌ INCOMPLETE | RequestSnapshot uploads to lib-asset, but PublishMapUpdate REJECTS instead of uploading |
| 9 | Affordance Scoring for JsonElement | ✅ COMPLETE | TryGetJsonDouble, TryGetJsonInt, TryGetJsonString helpers |
| 10 | Event Metadata (sourceAppId) | ✅ COMPLETE | sourceAppId populated and passed through events |
| 11 | Subscription Lifecycle | ✅ COMPLETE | SubscribeToIngestTopicAsync disposes prior subscription |
| 12 | Takeover Policy | ✅ COMPLETE | AuthorityTakeoverMode (preserve_and_diff, reset, require_consume) |
| 13 | Map Definition CRUD | ✅ COMPLETE | All 5 endpoints implemented with unit tests |
| 14 | Event Aggregation Window | ✅ COMPLETE | EventAggregationBuffer coalesces MapObjectsChangedEvent within window |

---

## Remaining Work

### Issue #8: Large Payload Handling for Publishing (USER DEFERRED)

**Status**: User has deferred this issue - they don't recall specifically requesting large payload handling.

**Current Behavior**: `PublishMapUpdateAsync` at line 494-505 REJECTS payloads exceeding `InlinePayloadMaxBytes` with an error message: "Use smaller payloads or chunking."

**Planning Doc Requirement**:
> If payload exceeds InlinePayloadMaxBytes, store it via lib-asset and return payloadAssetRef.

**What Works**: `RequestSnapshotAsync` correctly uploads large responses to lib-asset via `UploadLargePayloadToAssetAsync`.

**Required Fix (if pursued)**: `PublishMapUpdateAsync` should upload to lib-asset instead of rejecting, mirroring the pattern used in `RequestSnapshotAsync`.

**File**: `lib-mapping/MappingService.cs:494-505`

---

## Recently Completed (2025-01-08)

### Issue #3: PublishObjectChanges NonAuthorityHandling

**Fix Applied**: Added `HandleNonAuthorityObjectChangesAsync` and `PublishUnauthorizedObjectChangesWarningAsync` methods that mirror the `PublishMapUpdateAsync` behavior for the three modes:
- `Reject_silent`: Return Unauthorized
- `Reject_and_alert`: Return Unauthorized AND publish warning event
- `Accept_and_alert`: Process changes AND publish warning event

Also added `Warning` property to `PublishObjectChangesResponse` schema.

**Files Modified**:
- `lib-mapping/MappingService.cs` - Added NonAuthorityHandling support
- `schemas/mapping-api.yaml` - Added warning property to response

### Issue #14: Event Aggregation Window

**Fix Applied**: Implemented `EventAggregationBuffer` class with timer-based coalescing:
- When `EventAggregationWindowMs > 0`, `MapObjectsChangedEvent` events are buffered per channel
- Timer fires after the configured window, coalesces all pending changes, publishes single event
- Thread-safe using locks and `ConcurrentDictionary`
- Buffer auto-cleans after flush

**Files Modified**:
- `lib-mapping/MappingService.cs` - Added EventAggregationBuffer class and integration

---

## Testing Coverage

### Unit Tests (lib-mapping.tests/MappingServiceTests.cs)
- **40+ tests** covering:
  - Constructor validation
  - Configuration defaults
  - Permission registration (18 endpoints)
  - Channel CRUD (create, conflict, save records, publish events, subscribe to ingest)
  - Authority release/heartbeat
  - Publishing (valid/invalid authority)
  - Queries (point, bounds, type)
  - Affordance queries (fresh, cached)
  - Authoring (checkout, conflict, commit, release)
  - Ingest event handling
  - Definition CRUD (create, conflict, get, list, filter, update, delete)

### HTTP Integration Tests (http-tester/Tests/MappingTestHandler.cs)
- **17 tests** covering:
  - Channel Management: CreateChannel, CreateChannelConflict, ReleaseAuthority, AuthorityHeartbeat
  - Publishing: PublishMapUpdate, PublishObjectChanges, PublishWithInvalidToken, RequestSnapshot
  - Queries: QueryPoint, QueryBounds, QueryObjectsByType
  - Affordance: QueryAffordance, QueryAffordanceWithActorCapabilities
  - Authoring: AuthoringCheckout, AuthoringConflict, AuthoringCommit, AuthoringRelease

### Missing HTTP Tests
- Definition CRUD endpoints not tested via HTTP:
  - CreateDefinition
  - GetDefinition
  - ListDefinitions
  - UpdateDefinition
  - DeleteDefinition

---

## Current Issues (Original List with Status)

### Authority + Token Validation
1) ✅ **COMPLETE** - Token expiry check is wrong; publish will fail after the original token expires even if heartbeat extended authority.
   - Fix: validate only against AuthorityRecord.ExpiresAt.
   - File: lib-mapping/MappingService.cs
   - **Implementation**: `ValidateAuthorityAsync` checks `authority.ExpiresAt > DateTimeOffset.UtcNow`

### Ingest Path
2) ✅ **COMPLETE** - Ingest ignores action and never broadcasts to consumer topics.
   - Needs to honor create/update/delete and emit map.* events.
   - File: lib-mapping/MappingService.cs
   - **Implementation**: `HandleIngestEventAsync` processes actions via `MapIngestActionToObjectAction`, emits `MapUpdatedEvent` and `MapObjectsChangedEvent`

### Non-Authority Handling
3) ✅ **COMPLETE** - PublishObjectChangesAsync ignores NonAuthorityHandlingMode.
   - Must mirror PublishMapUpdateAsync behavior.
   - File: lib-mapping/MappingService.cs
   - **Implementation**: `HandleNonAuthorityObjectChangesAsync` and `PublishUnauthorizedObjectChangesWarningAsync` added

4) ✅ **COMPLETE** - accept_and_alert path writes data but does not publish events.
   - Consumers never receive updates for accepted non-authority publishes.
   - File: lib-mapping/MappingService.cs
   - **Implementation**: `HandleNonAuthorityPublishAsync` calls `ProcessPayloadsAsync` then `PublishMapUpdatedEventAsync`

### Snapshot / Query Behavior
5) ✅ **COMPLETE** - QueryObjectsInRegionAsync returns empty when bounds are null.
   - RequestSnapshotAsync without bounds is broken.
   - File: lib-mapping/MappingService.cs
   - **Implementation**: `QueryObjectsInRegionAsync` handles null bounds by querying all cells

### Indexing / Consistency
6) ✅ **COMPLETE** - Index cleanup is missing on update/delete.
   - Stale spatial and type indexes will accumulate forever.
   - File: lib-mapping/MappingService.cs
   - **Implementation**: `ProcessObjectChangeWithIndexCleanupAsync` removes old index entries before adding new ones

7) ✅ **COMPLETE** - CreatedAt is not preserved on upsert.
   - Updates should keep CreatedAt and only update UpdatedAt.
   - File: lib-mapping/MappingService.cs
   - **Implementation**: `ObjectAction.Updated` preserves `existing.CreatedAt`

### Large Payloads
8) ❌ **INCOMPLETE** - payloadAssetRef / InlinePayloadMaxBytes are defined but ignored.
   - Large payload and snapshot flow via lib-asset is not implemented.
   - File: lib-mapping/MappingService.cs, schemas/mapping-api.yaml
   - **Partial Implementation**: `RequestSnapshotAsync` correctly uploads to lib-asset, but `PublishMapUpdateAsync` REJECTS large payloads with "MVP" excuse comment instead of uploading

### Affordance Scoring
9) ✅ **COMPLETE** - candidate.Data is usually JsonElement, but scoring expects IDictionary<string, object>.
   - Most scoring paths silently fall back to base score.
   - File: lib-mapping/MappingService.cs
   - **Implementation**: `TryGetJsonDouble`, `TryGetJsonInt`, `TryGetJsonString` helpers for JsonElement

### Event Metadata
10) ✅ **COMPLETE** - sourceAppId / attemptedPublisher / currentAuthority are not populated.
   - No auditing or warning attribution.
   - File: lib-mapping/MappingService.cs, schemas/mapping-events.yaml
   - **Implementation**: `AuthorityRecord.AuthorityAppId` captured at creation, passed through events

### Subscriptions
11) ✅ **COMPLETE** - Ingest subscription overwrite can leak old subscriptions.
   - If re-creating a channel, prior subscription is not disposed.
   - File: lib-mapping/MappingService.cs
   - **Implementation**: `SubscribeToIngestTopicAsync` disposes existing subscription before creating new one

### Implementation vs Planning Gaps
12) ✅ **COMPLETE** - No takeover policy on channel creation.
   - Should be configured at create time (preserve/diff vs reset vs require-consume).
   - File: schemas/mapping-api.yaml, lib-mapping/MappingService.cs
   - **Implementation**: `AuthorityTakeoverMode` enum, `TakeoverMode` in `ChannelRecord`, `ClearChannelDataAsync` for reset

13) ✅ **COMPLETE** - No map definition CRUD or layer authoring APIs beyond checkout/commit.
   - Planning doc calls out map definition CRUD and layer format support.
   - Files: schemas/mapping-api.yaml, lib-mapping/MappingService.cs
   - **Implementation**: `CreateDefinitionAsync`, `GetDefinitionAsync`, `ListDefinitionsAsync`, `UpdateDefinitionAsync`, `DeleteDefinitionAsync`

14) ✅ **COMPLETE** - Event aggregation window and per-kind TTL behavior are not implemented.
   - Config exists but is unused.
   - Files: schemas/mapping-configuration.yaml, lib-mapping/MappingService.cs
   - **Implementation**: Per-kind TTL via `GetTtlSecondsForKind`. Event aggregation via `EventAggregationBuffer` class with timer-based coalescing for `MapObjectsChangedEvent`

---

## Eventing Rules (Canonical Behavior)
- map.{region}.{kind}.updated: layer-level notification, may include snapshot or delta.
- map.{region}.{kind}.objects.changed: per-object delta list.
- Ingest/publish-objects should emit BOTH when objects change.
- PublishMapUpdateAsync emits map.*.updated and is allowed to omit object deltas.

## Authority Takeover Modes
- preserve_and_diff (default): new authority can send a large update; map identity remains.
- reset: new authority clears map state (objects + indexes) before publishing.
- require_consume: new authority must RequestSnapshot and then Publish updates explicitly.

---

## Files Referenced
- lib-mapping/MappingService.cs
- lib-mapping/MappingServiceEvents.cs
- lib-mapping.tests/MappingServiceTests.cs
- http-tester/Tests/MappingTestHandler.cs
- schemas/mapping-api.yaml
- schemas/mapping-events.yaml
- schemas/mapping-configuration.yaml

---

## Related Documents
- **[docs/guides/MAPPING_SYSTEM.md](../guides/MAPPING_SYSTEM.md)**: Comprehensive reference guide for the mapping system (created 2026-01-08)
- **UPCOMING_-_MAPPING_PLUGIN.md**: Detailed feature specification with advanced affordance system design (retained for future reference)
- **~~UPCOMING_-_MAP_SERVICE.md~~**: Superseded architecture document (deleted 2025-01-08)
