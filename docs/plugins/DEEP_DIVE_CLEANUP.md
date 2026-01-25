# Deep Dive Document Cleanup Guide

> **Purpose**: Track and guide the cleanup of plugin deep dive documents
> **Status**: In Progress
> **Last Updated**: 2025-01-25

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
| Account | DONE | Added `<inheritdoc/>`, XML docs to AccountModel, cleaned violations |
| Achievement | DONE | All violations were false positives, restructured quirks |
| Actor | DONE | Removed false positives (T20 JsonElement, T24 class dispose, T19 internal classes), moved architectural items to Design Considerations, kept 5 real bugs |
| Analytics | PENDING | |
| Asset | PENDING | |
| Auth | PENDING | |
| Behavior | PENDING | |
| Character | PENDING | |
| Character-Encounter | PENDING | |
| Character-History | PENDING | |
| Character-Personality | PENDING | |
| Connect | PENDING | |
| Contract | PENDING | |
| Currency | PENDING | |
| Documentation | PENDING | |
| Escrow | PENDING | |
| Game-Service | PENDING | |
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
| Save-Load | PENDING | |
| Scene | PENDING | |
| Species | PENDING | |
| State | PENDING | |
| Subscription | PENDING | |
| Voice | PENDING | |
| Website | PENDING | |

---

## Summary of TENET Clarifications Made

The following clarifications were added to `docs/reference/tenets/IMPLEMENTATION.md`:

1. **T20 - JsonDocument Navigation is Allowed**: Added section explicitly permitting `JsonDocument.Parse()` and `JsonElement` for DOM navigation, external API parsing, and metadata reading.

2. **T21 - Mathematical Constants are Not Tunables**: Added section clarifying epsilon values and algorithm constants are acceptable as hardcoded values. Also noted stub scaffolding config is acceptable.

3. **T25 - State Store API Boundaries**: Added section clarifying that `ToString()` for state store set APIs and `Guid.TryParse` on read is acceptable boundary handling.

T7 already had the inter-service call clarification from a previous update.

---

*This document guides the cleanup effort. Update the progress tracker as each plugin is completed.*
