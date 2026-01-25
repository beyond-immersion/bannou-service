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
| Account | DONE | Added `<inheritdoc/>`, XML docs to AccountModel, cleaned violations. | PHASE2: Fixed 2 (anonymous role auto-mgmt, BatchGet per-item errors), remaining: none |
| Achievement | DONE | All violations were false positives, restructured quirks. Design Consideration (caching) requires architectural planning. | PHASE2: No actionable bugs, remaining: event handler definition caching (requires cache invalidation design) |
| Actor | DONE | Fixed all 5 bugs: (1) anonymous memory value → PerceptionData, (2) hardcoded tunables → config properties + wired, (3) ApiException handling → added, (4) error event publishing → added, (5) regex caching → added with timeout. DeploymentMode now enum. Bugs section cleared. | PHASE2: No actionable bugs, remaining: 14 design considerations (multi-instance arch, thread safety, scheduler redesign, etc.) |
| Analytics | DONE | Fixed T10 logging (ingestion → Debug, validation → Debug). Added comment for `?? string.Empty` compiler satisfaction. Removed false positives (T9 local dict protected by lock, T19 private methods). | PHASE2: No actionable bugs, remaining: POCO string.Empty defaults (data modeling convention) |
| Asset | DONE | Fixed T21 hardcoded constant → config. Added compiler satisfaction comments to `?? string.Empty`. Removed T19 false positives (internal classes, private methods). Moved T25 type improvements to Design Considerations. | PHASE2: No actionable bugs, remaining: T25 types, async patterns, index/queue architecture |
| Auth | DONE | Fixed T21 (removed DEFAULT_CONNECT_URL, hardcoded 60min fallback, added MockTwitchId to schema, removed dead "000000" fallback, extracted Unknown constants), T7 (ApiException catch), T10 (LogInfo → Debug for routine ops, mock email log). Removed T19 false positives (internal class, Generated/, base class overrides). Moved SessionDataModel type issues to Design Considerations. | PHASE2: No actionable bugs, remaining: T25 SessionDataModel types, email null handling in OAuth flows |
| Behavior | DONE | Already clean - all violations addressed previously. Design considerations properly documented (ValueTask, IHttpClientFactory, static CognitionConstants, in-memory runtime state). | PHASE2: No actionable bugs, remaining: GOAP/compiler design, GC pressure, validation optimization |
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
| Game-Session | DONE | Fixed T21 (added LockTimeoutSeconds config). Removed false positives (T6 NRT, T19 private/internal, T25 event boundaries). Moved T25 POCO types, T7, T5, T10 to Design Considerations. |
| Inventory | DONE | Already compliant. Added Bugs section (empty). All tunables from config, proper T7 ApiException handling for mesh calls. |
| Item | DONE | Already compliant (leaf node, no mesh calls). Added Bugs section (empty). T25 POCO type issues documented in Design Considerations. |
| Leaderboard | DONE | Already compliant (leaf node, no mesh calls). Added Bugs section (empty). Analytics event matching and season lifecycle properly documented. |
| Location | DONE | Fixed T21 (added 3 depth limit config props), T7 (ApiException handling for IRealmClient). Updated config and stubs sections. |
| Mapping | DONE | Already compliant. T7 ApiException handled for IAssetClient. T21 hardcoded affordance scoring values kept in Design Considerations (many values, complex change). |
| Matchmaking | DONE | Already compliant. T7 ApiException handled for IGameSessionClient, IPermissionClient. Well-structured document. |
| Mesh | DONE | Already compliant (leaf node, no mesh calls). Well-structured document. |
| Messaging | DONE | Already compliant (infrastructure, no mesh calls). Well-structured document. Dead config properties noted in Design Considerations. |
| Music | DONE | Removed false positive (T19 private methods). Restructured violations into Design Considerations (unused state store, hardcoded tunables, plugin lifecycle). |
| Orchestrator | DONE | Restructured violations into Design Considerations (hardcoded image/tunables, in-memory state, external API JSON). T20 external API is acceptable boundary exception. |
| Permission | DONE | Removed false positives (T6, T7, T19, T21 role hierarchy, T16 naming). Moved T9 multi-instance set ops and T25 anonymous type to Design Considerations. |
| Realm | DONE | Removed false positives (T6, T7, T19, T21, T16). Moved T9 multi-instance and T25 RealmId type to Design Considerations. Leaf node - no mesh calls. |
| Realm-History | DONE | Removed false positives (T6, T7). Moved T25 POCO types and T9 multi-instance to Design Considerations. Leaf node - no mesh calls. |
| Relationship | DONE | Fixed T19 (XML param name mismatch). Removed false positives (T6, T7). Moved T9, T25, T10 to Design Considerations. Leaf node - no mesh calls. |
| Relationship-Type | PENDING | |
| Save-Load | DONE | Fixed T20 (BannouJson), T10 logging levels. Removed T6 false positives. T25 → Design Considerations. |
| Scene | DONE | Fixed T10 logging (operation entry → Debug, expected outcomes → Debug). Moved T9/T25/T21 to Design Considerations. |
| Species | DONE | Fixed T10 logging (operation entry → Debug, expected outcomes → Debug). Moved T25/T9/T7/T21/T5 to Design Considerations. |
| State | DONE | Fixed T10 logging (operation entry → Debug) and T7 (Warning → Error for index failure). Moved T21 config issues to Design Considerations. |
| Subscription | DONE | Fixed T10 logging (operation entry → Debug) and T23 (empty catch → Debug log). Removed T6 false positives. Moved T25/T9/T21/T7 to Design Considerations. |
| Voice | DONE | Fixed T10 logging (operation entry → Debug). Removed T6 false positives, T23 borderline compliant patterns. Moved T25/T21 to Design Considerations. |
| Website | DONE | Fixed T10 logging (Warning → Debug for stub calls). Removed T6 false positive. Moved T21/T19 (stub-acceptable) to Design Considerations. |

---

## Phase 2: Actionable Bug Pass

After the initial cleanup (removing false positives, fixing obvious violations, restructuring documents), a second pass identifies which remaining issues can be fixed NOW vs which require a full planning session.

**Neither category means "production-ready"** - both A and B are issues that prevent production deployment. The difference is whether we can fix them in this session.

### Goal

For each plugin, categorize remaining Design Considerations and Bugs:

**Category A - Complex Issues (Requires Planning Session)**:
- Requires multiple decisions or significant design discussion
- Architectural changes affecting multiple components
- Cross-service coordination needed
- Schema changes requiring migration strategy
- Unclear solution path requiring exploration

**Category B - Actionable Issues (Requires ≤1 Decision)**:
- Solution path is clear OR requires only one quick decision
- Decision can be presented to user and answered immediately
- Can be fixed in current session once decision is made
- Does NOT mean "simple" or "one-line" - can be substantial work

The key question: "Can I fix this with at most one question to the user, or does it need a whole planning conversation?"

### Process

1. Read the plugin's deep dive document, focusing on Design Considerations and any remaining Bugs
2. For each item, ask: "Does this require more than one decision?"
3. For Category B items: Present the decision (if any), get answer, move to `### Bugs (Fix Immediately)`, fix the code, verify build
4. Continue until only Category A (complex) issues remain
5. Update progress tracker noting what was fixed and what complex issues remain

### Progress Notation

In the Progress Tracker, append to existing notes:
- `| PHASE2: Fixed [X], remaining: [brief description of Category A items]`
- `| PHASE2: No actionable bugs, remaining: [brief description of Category A items]`

---

*This document guides the cleanup effort. Update the progress tracker as each plugin is completed.*
