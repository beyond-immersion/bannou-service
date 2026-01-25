# Deep Dive Document Cleanup Guide

> **Purpose**: Track and guide the cleanup of plugin deep dive documents
> **Status**: In Progress
> **Last Updated**: 2026-01-25

---

## Overview

A previous audit pass added "Tenet Violations" sections to each plugin deep dive document. Many of these were:
- **False positives** due to misunderstanding tenet scope
- **Already fixed** items that should be removed (no historical records needed)
- **Improperly categorized** items that belong in Intentional Quirks or Design Considerations

This cleanup effort:
1. Investigates each identified violation
2. Fixes legitimate issues in the code
3. Removes false positives and fixed items from documentation
4. Restructures documents to match the template format
5. Updates any missing configuration or endpoint documentation

---

## False Positive Patterns

These patterns were incorrectly flagged as violations. The TENETS have been clarified to make these explicitly acceptable.

### T6 (Null Checks) - NOT A VIOLATION

**Pattern**: "Missing null checks on constructor parameters"

**Why it's wrong**: Bannou uses Nullable Reference Types (NRTs). Non-nullable parameters (`ILogger<T> logger` not `ILogger<T>? logger`) have compile-time guarantees. Adding runtime null checks for NRT-protected parameters is unnecessary defensive code.

**Action**: Remove these violations. Do NOT add null checks.

### T7 (ApiException) - ONLY FOR INTER-SERVICE CALLS

**Pattern**: "Missing ApiException catch clause in try-catch blocks"

**Why it's often wrong**: T7's ApiException handling is ONLY required when making inter-service calls using:
- Generated service clients (`IItemClient`, `IAuthClient`, etc.)
- `IServiceNavigator` or `IMeshClient`

**NOT required for**:
- State store operations (`IStateStore<T>`)
- Message bus operations (`IMessageBus`)
- Lock provider operations (`IDistributedLockProvider`)
- Local business logic

**How to verify**: Check if the plugin's Dependencies section shows mesh client usage. If it's a "leaf node" with no downstream service calls, T7 ApiException doesn't apply.

### T19 (XML Documentation) - PUBLIC MEMBERS ONLY

**Pattern**: "Missing XML documentation on internal/private classes"

**Why it's wrong**: T19 explicitly states "All **public** classes, interfaces, methods, and properties." Internal classes, private methods, and private helper methods do not require XML documentation.

**Action**: Remove violations for non-public members.

### T20 (JSON Serialization) - JsonDocument is Allowed

**Pattern**: "Direct JsonDocument.Parse usage instead of BannouJson"

**Why it's wrong**: T20 targets `JsonSerializer.Deserialize/Serialize` for typed model (de)serialization. `JsonDocument.Parse()` and `JsonElement` navigation are acceptable for:
- External API response parsing
- Metadata dictionary value reading
- JSON structure introspection

**Action**: Remove violations for JsonDocument/JsonElement usage.

### T21 (Configuration-First) - Mathematical Constants Allowed

**Pattern**: "Hardcoded epsilon tolerance" or similar mathematical constants

**Why it's wrong**: Mathematical constants (epsilon for floating-point comparison, pi, golden ratio, etc.) are not runtime tunables. They have mathematical meaning and should not be configurable.

**Also acceptable**: Configuration properties for unimplemented stub features (scaffolding for future implementation). Document in Stubs section.

### T25 (Internal Model Type Safety) - State Store API Boundaries

**Pattern**: "GameServiceId stored as string in set operations"

**Why it's wrong**: State store APIs like `AddToSetAsync` require string values. Converting `Guid.ToString()` at this boundary and parsing back with `Guid.TryParse` on read is acceptable. The tenet targets internal POCO field types, not API boundary constraints.

---

## Legitimate Violation Patterns

These are real issues that need code fixes.

### T5 (Event-Driven Architecture) - Anonymous Objects

**Pattern**: Anonymous objects passed to `TryPublishErrorAsync` details parameter

**Fix**: Create typed error detail classes or use existing event models.

### T9 (Multi-Instance Safety) - In-Memory Authoritative State

**Pattern**: `ConcurrentDictionary` used as the only source of truth (not backed by distributed state)

**Fix**: Use lib-state for authoritative data. In-memory is only acceptable for caches backed by events or API calls.

### T21 (Configuration-First) - Hardcoded Tunables

**Pattern**: Magic numbers for timeouts, retry delays, thresholds, capacities

**Fix**: Add configuration property to schema, regenerate, use `_configuration.PropertyName`.

### T23 (Async Method Pattern) - Fire-and-Forget Tasks

**Pattern**: `_ = SomeMethodAsync()` discarding Task without proper handling

**Fix**: Either await the task, use proper fire-and-forget patterns with error logging, or document as intentional in quirks.

---

## Cleanup Process Per Plugin

For each plugin deep dive document:

1. **Read the violations section** - Identify each flagged item

2. **Categorize each violation**:
   - FALSE POSITIVE → Remove from document
   - ALREADY FIXED → Remove from document (no historical records)
   - LEGITIMATE BUG → Fix code, then remove from document
   - DESIGN CONSIDERATION → Move to "Design Considerations" section
   - INTENTIONAL QUIRK → Move to "Intentional Quirks" section

3. **Fix legitimate code issues**:
   - Add `<inheritdoc/>` to public interface method implementations
   - Add XML docs to public model properties
   - Fix actual tenet violations in code

4. **Restructure document**:
   - Remove "Tenet Violations" or "Bugs (Fix Immediately)" noise
   - Use template structure:
     - `### Bugs (Fix Immediately)` - "No bugs identified." if empty
     - `### Intentional Quirks (Documented Behavior)`
     - `### Design Considerations (Requires Planning)`

5. **Update documentation**:
   - Add any missing configuration properties to Configuration table
   - Add any new endpoints to API Endpoints section
   - Update Stubs section if scaffolding config exists

6. **Verify build** (if code was changed):
   - `dotnet build plugins/lib-{service}/lib-{service}.csproj --no-restore`

---

## Progress Tracker

| Plugin | Status | Notes |
|--------|--------|-------|
| Account | DONE | Added `<inheritdoc/>`, XML docs to AccountModel, cleaned violations. Design Considerations are legitimate architectural choices. |
| Achievement | DONE | All violations were false positives, restructured quirks. Design Consideration (caching) requires architectural planning. |
| Actor | DONE | Fixed all 5 bugs: (1) anonymous memory value → PerceptionData, (2) hardcoded tunables → config properties + wired, (3) ApiException handling → added, (4) error event publishing → added, (5) regex caching → added with timeout. DeploymentMode now enum. Bugs section cleared. |
| Analytics | DONE | Fixed T10 logging (ingestion → Debug, validation → Debug). Added comment for `?? string.Empty` compiler satisfaction. Removed false positives (T9 local dict protected by lock, T19 private methods). |
| Asset | DONE | Fixed T21 hardcoded constant → config. Added compiler satisfaction comments to `?? string.Empty`. Removed T19 false positives (internal classes, private methods). Moved T25 type improvements to Design Considerations. |
| Auth | DONE | Fixed T21 (removed DEFAULT_CONNECT_URL, hardcoded 60min fallback, added MockTwitchId to schema, removed dead "000000" fallback, extracted Unknown constants), T7 (ApiException catch), T10 (LogInfo → Debug for routine ops, mock email log). Removed T19 false positives (internal class, Generated/, base class overrides). Moved SessionDataModel type issues to Design Considerations. |
| Behavior | DONE | Already clean - all violations addressed previously. Design considerations properly documented (ValueTask, IHttpClientFactory, static CognitionConstants, in-memory runtime state). |
| Character | DONE | Removed false positives (T21 stub config, T19 interface methods, T19 private helpers). Moved T9 concurrency issues to Design Considerations (require architectural planning for distributed locking). |
| Character-Encounter | DONE | Removed false positives (T7 - leaf node, T9 method-local dict, T19 internal/interface, T5 diagnostic metadata, T21 stub config). Moved T25 POCO type issues to Design Considerations. |
| Character-History | DONE | Removed false positives (T7 - leaf node, T19 internal classes). Consolidated T25 POCO type issues into Design Considerations section. |
| Character-Personality | DONE | Removed false positives (T7 - leaf node). Consolidated T25 POCO type issues into Design Considerations section. |
| Connect | DONE | Identified 1 bug (empty catch blocks). Removed T5 false positives (internal protocol, not cross-service events). Moved T23/T9 patterns to Additional Design Considerations. |
| Contract | DONE | Identified 1 bug (silent catch-all). T7/T21/T5 issues require planning (mesh calls, config, schema events). Removed T19 false positives (private methods). |
| Currency | DONE | Moved T25 POCO types and T16/T8 to Design Considerations. Removed T10/T19 false positives (entry logging not mandatory, private members don't need docs). |
| Documentation | DONE | Fixed T23 (GitSyncService → fire-and-forget, RedisSearchIndexService → Error logging + discard). Removed false positives (T23 interface contract, T5 diagnostics, T19 internal, T10 controller). Moved T16 return type to Design Considerations. |
| Escrow | DONE | All violations were false positives or already fixed. Removed T6/T9/T7/T10/T25 false positives. Moved POCO string defaults and EscrowExpiredEvent to Design Considerations. |
| Game-Service | DONE | All violations previously fixed (T25 ServiceId→Guid, T10 logging, T9 ETag concurrency). Removed false positives (T6 NRT, T7 no mesh calls, T19 internal, T21 framework boilerplate). |
| Game-Session | PENDING | |
| Inventory | PENDING | |
| Item | PENDING | |
| Leaderboard | PENDING | |
| Location | PENDING | |
| Mapping | PENDING | |
| Matchmaking | PENDING | |
| Mesh | PENDING | |
| Messaging | PENDING | |
| Music | PENDING | |
| Orchestrator | PENDING | |
| Permission | PENDING | |
| Realm | PENDING | |
| Realm-History | PENDING | |
| Relationship | PENDING | |
| Relationship-Type | PENDING | |
| Save-Load | DONE | Fixed T20 (BannouJson), T10 logging levels. Removed T6 false positives. T25 → Design Considerations. |
| Scene | DONE | Fixed T10 logging (operation entry → Debug, expected outcomes → Debug). Moved T9/T25/T21 to Design Considerations. |
| Species | DONE | Fixed T10 logging (operation entry → Debug, expected outcomes → Debug). Moved T25/T9/T7/T21/T5 to Design Considerations. |
| State | DONE | Fixed T10 logging (operation entry → Debug) and T7 (Warning → Error for index failure). Moved T21 config issues to Design Considerations. |
| Subscription | DONE | Fixed T10 logging (operation entry → Debug) and T23 (empty catch → Debug log). Removed T6 false positives. Moved T25/T9/T21/T7 to Design Considerations. |
| Voice | DONE | Fixed T10 logging (operation entry → Debug). Removed T6 false positives, T23 borderline compliant patterns. Moved T25/T21 to Design Considerations. |
| Website | DONE | Fixed T10 logging (Warning → Debug for stub calls). Removed T6 false positive. Moved T21/T19 (stub-acceptable) to Design Considerations. |

---

*This document guides the cleanup effort. Update the progress tracker as each plugin is completed.*
