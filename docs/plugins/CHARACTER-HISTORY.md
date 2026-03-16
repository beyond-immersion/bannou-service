# Character History Plugin Deep Dive

> **Plugin**: lib-character-history
> **Schema**: schemas/character-history-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Store**: character-history-statestore (MySQL)
> **Implementation Map**: [docs/maps/CHARACTER-HISTORY.md](../maps/CHARACTER-HISTORY.md)
> **Short**: Historical event participation and machine-readable backstory for behavior system consumption

---

## Overview

Historical event participation and backstory management (L4 GameFeatures) for characters. Tracks when characters participate in world events (wars, disasters, political upheavals) with role and significance tracking, and maintains machine-readable backstory elements (origin, occupation, training, trauma, fears, goals) for behavior system consumption. Provides template-based text summarization for character compression via lib-resource. Shares storage helper abstractions with the realm-history service.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor | Reads backstory via `IVariableProviderFactory` (`BackstoryProviderFactory`) for `${backstory.*}` ABML expressions |
| lib-analytics | Subscribes to `character-history.participation.batch-created`, `character-history.backstory.created`, `character-history.backstory.updated` for historical analytics |
| lib-resource | Receives direct API calls for reference registration; calls `/character-history/delete-all` endpoint on cascade delete |
| lib-storyline | Soft L4 dependency via `ICharacterHistoryClient` for `BackstoryAdd` mutations (graceful degradation if unavailable) |

**Note**: lib-character (L2) does **not** call this service per SERVICE_HIERARCHY - L2 cannot depend on L4. The character service explicitly notes it cannot call CharacterHistory. Callers needing history data must call this service directly.

---

## ABML Expression Support

The plugin registers `BackstoryProviderFactory` as an `IVariableProviderFactory` for the Actor service to discover via DI collection. This enables ABML expressions like:
- `${backstory.origin}` — Returns the value of the ORIGIN element
- `${backstory.fear.strength}` — Returns the strength of the FEAR element
- `${backstory.elements.TRAUMA}` — Returns all TRAUMA elements as a list

The `CharacterHistoryTemplate` provides compile-time validation for `${candidate.history.*}` paths during ABML semantic analysis.

---

## Visual Aid

```
Backstory Element Model
========================

 SetBackstory(characterId=C1, elements=[...], replaceExisting=false)
 │
 ▼
 ┌─────────────────────────────────────────┐
 │ backstory-C1 │
 │ │
 │ Elements: │
 │ ┌─────────┬──────────┬─────────┬──────┐│
 │ │ Type │ Key │ Value │Streng││
 │ ├─────────┼──────────┼─────────┼──────┤│
 │ │ ORIGIN │ homeland │ north.. │ 0.9 ││
 │ │ TRAUMA │ battle │ siege.. │ 0.7 ││
 │ │ GOAL │ revenge │ avenge..│ 0.8 ││
 │ │ FEAR │ fire │ burns..│ 0.6 ││
 │ └─────────┴──────────┴─────────┴──────┘│
 │ │
 │ CreatedAtUnix: 1706000000 │
 │ UpdatedAtUnix: 1706500000 │
 └─────────────────────────────────────────┘

 Merge Logic (replaceExisting=false):
 New element with type=ORIGIN, key=homeland → UPDATE existing
 New element with type=BELIEF, key=honor → APPEND new


Text Summarization
===================

 SummarizeHistory(characterId=C1, maxBackstoryPoints=3, maxLifeEvents=2)
 │
 ▼
 Backstory (top 3 by strength):
 "From the northlands" ← ORIGIN: homeland → northlands
 "Seeks to avenge their kin" ← GOAL: revenge → avenge their kin
 "Experienced the siege" ← TRAUMA: battle → the siege

 Participations (top 2 by significance):
 "led the Battle of Stormgate" ← LEADER + "Battle of Stormgate"
 "survived the Great Plague" ← SURVIVOR + "Great Plague"
```

---

## Stubs & Unimplemented Features

None. The service is feature-complete for its scope.

---

## Potential Extensions

1. **Batch reference unregistration in DeleteAll**: `DeleteAllHistoryAsync` makes N individual `UnregisterReferenceAsync` API calls before bulk deletion. The DualIndexHelper bulk path is already optimized (~7 bulk operations regardless of N), but the reference unregistration loop remains O(N) API calls. Blocked on lib-resource batch unregister endpoint.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/351 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None.

### Intentional Quirks

1. **UpdatedAt null semantics**: `GetBackstory` returns `UpdatedAt = null` when backstory has never been modified after initial creation (CreatedAtUnix == UpdatedAtUnix).

2. **Helper abstractions constructed inline**: `DualIndexHelper` and `BackstoryStorageHelper` are instantiated directly in the constructor rather than registered in DI. This is intentional: helpers require service-specific configuration (key prefixes, data access lambdas) that can't be generalized. Both helpers accept `IDistributedLockProvider` and `IndexLockTimeoutSeconds` for multi-instance safe write operations. Lock failure returns `StatusCodes.Conflict` to callers. Testing works via `IStateStoreFactory` and `IDistributedLockProvider` mocking.

3. **Summarize is a read-only operation (no event)**: `SummarizeHistoryAsync` does not publish any event because it's a pure read operation that doesn't modify state. Per FOUNDATION TENETS (Event-Driven Architecture), events notify about state changes - read operations don't trigger events. This is consistent with other read operations like `GetBackstory` and `GetParticipation`.

4. **Summarization switch expressions have unreachable defaults**: `GenerateBackstorySummary` and `GenerateParticipationSummary` both use switch expressions with `_ =>` defaults that are unreachable at runtime. The defaults (`"{element.Key}: {element.Value}"` and `"participated in"`) exist because: (1) C# requires exhaustive switches, and (2) they provide graceful degradation if enum values are added to the schema but the switch isn't updated. This is defensive programming, not a bug.

5. **FormatValue only handles snake_case**: The `FormatValue` helper (`value.Replace("_", " ").ToLowerInvariant()`) only converts snake_case to readable text. This is intentional: the schema documents snake_case as the convention for machine-readable backstory values (e.g., `knights_guild`, `northlands`). Values using camelCase or PascalCase would render incorrectly, but such values don't follow the documented convention.

6. **GetBulkAsync silently drops missing records**: `DualIndexHelper.GetRecordsByIdsAsync` returns only records that exist. If an index contains stale record IDs (records deleted without index cleanup), they are silently excluded from results. This is self-healing behavior - throwing would crash callers for rare edge cases. Callers receive valid data; counts may be slightly fewer than expected if data inconsistency exists.

7. **BackstoryCache returns stale data on load failure**: If loading fresh data from the service fails (any exception except 404), the cache returns previously cached data even if expired. This is deliberate graceful degradation - stale backstory data is better than no data for NPC behavior execution.

8. **Case-insensitive ABML variable path resolution**: `BackstoryProvider` stores elements keyed by both original and lowercase type names. This means `${backstory.ORIGIN}`, `${backstory.Origin}`, and `${backstory.origin}` all resolve identically, using `StringComparer.OrdinalIgnoreCase` for dictionary lookups.

9. **BackstoryCache uses self-mesh call**: `BackstoryCache` loads data by obtaining `ICharacterHistoryClient` from DI and calling `GetBackstoryAsync` - the service calling itself via lib-mesh. This adds mesh overhead but maintains clean separation between cache and service implementation layers.

### Design Considerations (Requires Planning)

None.

---

## Work Tracking

### Pending Design Review
- **2026-02-08**: [#351](https://github.com/beyond-immersion/bannou-service/issues/351) - Batch reference unregistration for DeleteAll (blocked on lib-resource batch unregister endpoint; O(N) API calls for N participations)
