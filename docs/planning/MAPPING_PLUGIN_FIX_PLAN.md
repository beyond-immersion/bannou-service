# Mapping Plugin Fix Plan (Post-Review)

## Purpose
Provide a concrete, actionable fix plan for lib-mapping based on the current implementation vs the planning doc.
This includes: hard bugs, missing behavior, schema gaps, eventing behavior, indexing rules, and testing gaps.

## Decisions Locked In
- Authority tokens are opaque. Do NOT parse expiry from token; validate only against stored AuthorityRecord.
- Non-authority handling applies to ALL publish paths (RPC, publish-objects, ingest) and to all actions (create/update/delete).
- Ingest is an upsert-capable path with delete support; it is not an authority establishment mechanism.
- Authority takeover default is preserve_and_diff (in-place replacement; map identity stays intact).
- Ingest and publish-objects should emit both layer-level updates and object-level delta events when objects change.

## Current Issues ("you did this wrong")

### Authority + Token Validation
1) Token expiry check is wrong; publish will fail after the original token expires even if heartbeat extended authority.
   - Fix: validate only against AuthorityRecord.ExpiresAt.
   - File: lib-mapping/MappingService.cs

### Ingest Path
2) Ingest ignores action and never broadcasts to consumer topics.
   - Needs to honor create/update/delete and emit map.* events.
   - File: lib-mapping/MappingService.cs

### Non-Authority Handling
3) PublishObjectChangesAsync ignores NonAuthorityHandlingMode.
   - Must mirror PublishMapUpdateAsync behavior.
   - File: lib-mapping/MappingService.cs

4) accept_and_alert path writes data but does not publish events.
   - Consumers never receive updates for accepted non-authority publishes.
   - File: lib-mapping/MappingService.cs

### Snapshot / Query Behavior
5) QueryObjectsInRegionAsync returns empty when bounds are null.
   - RequestSnapshotAsync without bounds is broken.
   - File: lib-mapping/MappingService.cs

### Indexing / Consistency
6) Index cleanup is missing on update/delete.
   - Stale spatial and type indexes will accumulate forever.
   - File: lib-mapping/MappingService.cs

7) CreatedAt is not preserved on upsert.
   - Updates should keep CreatedAt and only update UpdatedAt.
   - File: lib-mapping/MappingService.cs

### Large Payloads
8) payloadAssetRef / InlinePayloadMaxBytes are defined but ignored.
   - Large payload and snapshot flow via lib-asset is not implemented.
   - File: lib-mapping/MappingService.cs, schemas/mapping-api.yaml

### Affordance Scoring
9) candidate.Data is usually JsonElement, but scoring expects IDictionary<string, object>.
   - Most scoring paths silently fall back to base score.
   - File: lib-mapping/MappingService.cs

### Event Metadata
10) sourceAppId / attemptedPublisher / currentAuthority are not populated.
   - No auditing or warning attribution.
   - File: lib-mapping/MappingService.cs, schemas/mapping-events.yaml

### Subscriptions
11) Ingest subscription overwrite can leak old subscriptions.
   - If re-creating a channel, prior subscription is not disposed.
   - File: lib-mapping/MappingService.cs

### Implementation vs Planning Gaps
12) No takeover policy on channel creation.
   - Should be configured at create time (preserve/diff vs reset vs require-consume).
   - File: schemas/mapping-api.yaml, lib-mapping/MappingService.cs

13) No map definition CRUD or layer authoring APIs beyond checkout/commit.
   - Planning doc calls out map definition CRUD and layer format support.
   - Files: schemas/mapping-api.yaml, lib-mapping/MappingService.cs

14) Event aggregation window and per-kind TTL behavior are not implemented.
   - Config exists but is unused.
   - Files: schemas/mapping-configuration.yaml, lib-mapping/MappingService.cs

## Fix Plan (Ordered by Impact)

### 1) Authority Token Validation (Opaque Tokens)
- Remove token parsing for expiry checks.
- Validate authority using AuthorityRecord.ExpiresAt only.
- Update:
  - ValidateAuthorityAsync
  - ReleaseAuthorityAsync
  - AuthorityHeartbeatAsync
- Files:
  - lib-mapping/MappingService.cs

### 2) Ingest Path (Upsert + Delete + Broadcast)
- Respect IngestPayload.action (create/update/delete) instead of always upserting.
- Route ingest through the same NonAuthorityHandlingMode as RPC publishes.
- Emit:
  - map.{region}.{kind}.updated (layer-level)
  - map.{region}.{kind}.objects.changed (object deltas)
- Enforce MaxPayloadsPerPublish on ingest.
- Files:
  - lib-mapping/MappingService.cs
  - schemas/mapping-events.yaml

### 3) Non-Authority Handling Parity (Publish Objects)
- Apply NonAuthorityHandlingMode for PublishObjectChangesAsync.
- accept_and_alert should emit events just like authority publishes.
- Files:
  - lib-mapping/MappingService.cs

### 4) Index Integrity
- Update spatial/type index entries on update and delete.
- Remove object IDs from old cells/types if position/bounds/type change.
- Preserve CreatedAt on update/upsert.
- Files:
  - lib-mapping/MappingService.cs

### 5) Snapshot / Query Without Bounds
- Add a region-level index (or per-channel object list) so snapshots and region queries work without bounds.
- Use this index for RequestSnapshotAsync and QueryObjectsInRegionAsync.
- Files:
  - lib-mapping/MappingService.cs

### 6) Large Payload Handling (lib-asset)
- If payload exceeds InlinePayloadMaxBytes, store it via lib-asset and return payloadAssetRef.
- RequestSnapshotAsync should return PayloadRef if size is large.
- Files:
  - lib-mapping/MappingService.cs
  - schemas/mapping-api.yaml

### 7) Affordance Scoring for JsonElement
- Introduce helpers to read numeric/string properties from JsonElement.
- Use these helpers in ScoreAffordance and ExtractFeatures.
- Files:
  - lib-mapping/MappingService.cs

### 8) Takeover Policy
- Add AuthorityTakeoverMode to CreateChannelRequest and ChannelRecord.
  - preserve_and_diff (default)
  - reset
  - require_consume
- Apply during channel creation when authority expired.
- Files:
  - schemas/mapping-api.yaml
  - lib-mapping/MappingService.cs

### 9) Event Metadata
- Populate sourceAppId, attemptedPublisher, currentAuthority.
- Requires pulling appId from request/session context.
- Files:
  - lib-mapping/MappingService.cs
  - schemas/mapping-events.yaml

### 10) Subscription Lifecycle
- Dispose prior ingest subscription before overwriting in dictionary.
- Files:
  - lib-mapping/MappingService.cs

### 11) Authoring + Map Definition APIs (Planning Gap)
- Add map definition CRUD and layer format support per planning doc.
- Files:
  - schemas/mapping-api.yaml
  - lib-mapping/MappingService.cs

### 12) Event Aggregation + TTL Behavior (Planning Gap)
- Implement EventAggregationWindowMs and per-kind TTLs for ephemeral kinds.
- Files:
  - schemas/mapping-configuration.yaml
  - lib-mapping/MappingService.cs

## Eventing Rules (Canonical Behavior)
- map.{region}.{kind}.updated: layer-level notification, may include snapshot or delta.
- map.{region}.{kind}.objects.changed: per-object delta list.
- Ingest/publish-objects should emit BOTH when objects change.
- PublishMapUpdateAsync emits map.*.updated and is allowed to omit object deltas.

## Authority Takeover Modes (New)
- preserve_and_diff (default): new authority can send a large update; map identity remains.
- reset: new authority clears map state (objects + indexes) before publishing.
- require_consume: new authority must RequestSnapshot and then Publish updates explicitly.

## Testing Gaps (Add These)
- Authority token expiry after heartbeat (opaque tokens).
- Ingest delete action and non-authority handling modes.
- Ingest emits both updated + objects.changed events.
- Index cleanup on update/delete.
- Snapshot without bounds returns data.
- payloadAssetRef and PayloadRef behavior.

## Files Referenced
- lib-mapping/MappingService.cs
- lib-mapping/MappingServiceEvents.cs
- lib-mapping.tests/MappingServiceTests.cs
- http-tester/Tests/MappingTestHandler.cs
- schemas/mapping-api.yaml
- schemas/mapping-events.yaml
- schemas/mapping-configuration.yaml

