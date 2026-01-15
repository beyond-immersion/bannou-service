# Bannou Codebase Deduplication Audit

> **Created**: 2026-01-15
> **Status**: Mostly Complete (P3 optional items remaining)
> **Scope**: All non-Generated code in the Bannou repository

This document tracks identified code duplication patterns and their resolution status.

---

## Executive Summary

### Existing Infrastructure (Already Consolidated)

The codebase has **existing shared infrastructure** that is underutilized:

| Component | Location | Purpose | Adoption |
|-----------|----------|---------|----------|
| `BannouServiceBase` | `bannou-service/Services/` | Base class with InstanceId | All services (minimal) |
| `BannouService<T>` | `bannou-service/Services/` | Configuration-aware base | Available but underused |
| `ServiceTestBase<TConfig>` | `bannou-service/Testing/` | Test base with assertions | Used by lib-*.tests |
| `PaginationHelper` | `bannou-service/History/` | Pagination utilities | **Only 2 services use it** |
| `MetadataHelper` | `bannou-service/` | JSON metadata conversion | Properly adopted |
| `TimestampHelper` | `bannou-service/History/` | Timestamp conversions | Available |
| `DualIndexHelper` | `bannou-service/History/` | Two-way index management | Used by history services |

### Test Infrastructure Split

Test utilities are split across three locations:

| Location | Referenced By | Purpose |
|----------|---------------|---------|
| `test-utilities/` | **ALL lib-*.tests** (37 projects) | Constructor validation, JWT config |
| `plugins/lib-testing/` | Only 4 projects (location, species, realm, character) | Test result types, IServiceTestHandler |
| `bannou-service/Testing/` | Most lib-*.tests | ServiceTestBase, assertions, data generators |

---

## Priority 1: Existing Helper Adoption (Low Effort, High Impact)

### 1.1 PaginationHelper Underutilization

**Status**: [x] Reviewed - Low Priority

**Issue**: `PaginationHelper` exists with comprehensive pagination support but only 2 of 7+ services use it.

**Currently Using PaginationHelper**:
- `plugins/lib-character-history/CharacterHistoryService.cs`
- `plugins/lib-realm-history/RealmHistoryService.cs`

**Manually Implementing `.Skip().Take()`**:
- `plugins/lib-scene/SceneService.cs`
- `plugins/lib-documentation/DocumentationService.cs`
- `plugins/lib-account/AccountService.cs`
- `plugins/lib-save-load/SaveLoadService.cs`
- Additional services with inline pagination

**Analysis**: After review, retrofitting existing services has:
- **Low benefit**: Existing code works correctly and is readable
- **Medium effort**: Each service has its own response structure requiring adaptation
- **Low risk**: PaginationHelper is well-tested

**Decision**:
- **New code** should use PaginationHelper for consistency
- **Existing services** will NOT be retrofitted - working code with clear logic
- Manual Skip/Take patterns (3-5 lines each) are acceptable when integrated with service-specific response structures

**Estimated Savings**: Deferred (~50-100 lines potential, but not worth the churn).

---

## Priority 2: SDK Helper Consolidation (Low Effort, Medium Impact)

### 2.1 Duplicated ParseRealm Methods

**Status**: [x] COMPLETED

**Files with duplication**:
- `sdks/asset-bundler/Bundles/BundleClient.cs:376-389` - Returns `null` for unknown
- `sdks/asset-bundler/Metabundles/MetabundleClient.cs:97-110` - Returns `Omega` for empty, throws for unknown

**Solution Implemented**: Created `sdks/asset-bundler/Helpers/AssetApiHelpers.cs` with:
- `ParseRealm(string? realm, Realm? defaultValue = null)` - Returns null for unknown
- `ParseRealmRequired(string? realm, Realm defaultValue = Realm.Omega)` - Throws for unknown
- `ParseBundleType(string? bundleType)` - Returns null for unknown
- `ParseLifecycle(string? status)` - Returns null for unknown

**Files updated**:
- `BundleClient.cs` - Now uses `AssetApiHelpers.ParseRealm/ParseBundleType/ParseLifecycle`
- `MetabundleClient.cs` - Now uses `AssetApiHelpers.ParseRealmRequired`

### 2.2 Duplicated Error Exception Creation

**Status**: [x] COMPLETED

**Pattern** (was repeated in 6+ locations):
```csharp
var errorCode = response.Error?.ResponseCode ?? 500;
var errorMessage = response.Error?.Message ?? "Unknown error";
throw new InvalidOperationException($"Failed to {operation}: {errorCode} - {errorMessage}");
```

**Solution Implemented**: Added to `sdks/asset-bundler/Helpers/AssetApiHelpers.cs`:
- `CreateApiException<TResponse>(string operation, ApiResponse<TResponse> response)` - Creates exception from error
- `EnsureSuccess<TResponse>(ApiResponse<TResponse> response, string operation)` - Throws if not success, returns result

**Files updated**:
- `BundleClient.cs` - Now uses `AssetApiHelpers.EnsureSuccess` (6 locations)
- `MetabundleClient.cs` - Now uses `AssetApiHelpers.EnsureSuccess` (1 location)
- `BannouUploader.cs` - Now uses `AssetApiHelpers.EnsureSuccess` and `CreateApiException` (2 locations)

---

## Priority 3: New Utility Helpers (Medium Effort, High Impact)

### 3.1 Case-Insensitive Dictionary Creation

**Status**: [ ] Not Started

**Pattern** (20+ occurrences across codebase):
```csharp
new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
new ConcurrentDictionary<string, T>(StringComparer.OrdinalIgnoreCase)
```

**Action**: Create `bannou-service/Helpers/DictionaryFactory.cs`:
```csharp
public static class DictionaryFactory
{
    public static Dictionary<string, T> CreateCaseInsensitive<T>()
        => new(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, T> CreateCaseInsensitive<T>(IDictionary<string, T> source)
        => new(source, StringComparer.OrdinalIgnoreCase);

    public static ConcurrentDictionary<string, T> CreateConcurrentCaseInsensitive<T>()
        => new(StringComparer.OrdinalIgnoreCase);
}
```

### 3.2 Key Building Patterns

**Status**: [ ] Not Started

**Pattern** (35+ instances across services):
```csharp
private static string BuildRealmKey(string realmId) => $"{REALM_KEY_PREFIX}{realmId}";
private static string BuildCodeIndexKey(string code) => $"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}";
```

**Affected services**: Realm, Location, Species, Character, RelationshipType, etc.

**Action**: Create `bannou-service/Helpers/KeyBuilder.cs`:
```csharp
public static class KeyBuilder
{
    public static string Build(string prefix, string id) => $"{prefix}{id}";
    public static string BuildCodeIndex(string prefix, string code) => $"{prefix}{code.ToUpperInvariant()}";
    public static string BuildComposite(string prefix, params string[] parts) => $"{prefix}{string.Join(":", parts)}";
}
```

**Decision**: This may be over-abstraction. The current inline pattern is clear and type-safe. Consider deferring.

---

## Priority 4: Test Code Consolidation (Medium Effort, Medium Impact)

### 4.1 CreateTestRealmAsync Helper Duplication

**Status**: [x] COMPLETED

**Identical helper in 3 HTTP test handlers**:
- `http-tester/Tests/SpeciesTestHandler.cs:50-59`
- `http-tester/Tests/LocationTestHandler.cs:59-68`
- `http-tester/Tests/CharacterTestHandler.cs:42-51`

**Solution Implemented**: Added shared helper to `http-tester/Tests/BaseHttpTestHandler.cs`:
```csharp
protected static async Task<RealmResponse> CreateTestRealmAsync(string prefix, string description, string suffix)
{
    var realmClient = GetServiceClient<IRealmClient>();
    return await realmClient.CreateRealmAsync(new CreateRealmRequest
    {
        Code = $"{prefix}_{DateTime.Now.Ticks}_{suffix}",
        Name = $"{description} Test Realm {suffix}",
        Category = "TEST"
    });
}
```

**Files updated**:
- `BaseHttpTestHandler.cs` - Added shared helper method
- `SpeciesTestHandler.cs` - Removed private helper, updated 5 usages
- `LocationTestHandler.cs` - Removed private helper, updated 17 usages
- `CharacterTestHandler.cs` - Removed private helper, updated 8 usages

**Lines saved**: ~30 lines removed (3 x ~10-line helper methods)

---

## Priority 5: Service Implementation Patterns (High Effort, High Impact)

### 5.1 Analysis Summary

**Comprehensive analysis completed** by exploration agents found ~8,500-10,000 lines of similar patterns.

**However**, creating generic abstractions has trade-offs:
- **Pros**: Reduced duplication, single source of truth for common logic
- **Cons**: Increased complexity, reduced code clarity, harder debugging

### 5.2 Recommended Approach

**DO NOT create** a generic CRUD base service class. The current pattern where each service:
1. Uses `IStateStoreFactory` to get typed stores
2. Implements explicit CRUD methods
3. Has explicit error handling with `TryPublishErrorAsync`

...is **intentional and preferred**. It provides:
- Clear, readable service code
- Easy debugging (no base class abstractions)
- Service-specific customization without fighting inheritance

**INSTEAD, focus on**:
1. Ensuring existing helpers (`PaginationHelper`, `MetadataHelper`) are universally adopted
2. Extracting truly duplicated utility methods (enum parsing, error creation)
3. Keeping service implementations explicit and self-contained

---

## Findings Summary

### What's NOT Duplicated (Good Architecture)

| Category | Status | Notes |
|----------|--------|-------|
| DTO/Models | ✅ Clean | Generated vs internal models properly separated |
| Event Models | ✅ Clean | All generated from schema |
| State Store Access | ✅ Clean | Uses generated `StateStoreDefinitions` |
| Event Consumer Registration | ✅ Clean | Uses shared `EventConsumerExtensions` |
| Service Client Registration | ✅ Clean | Uses shared `ServiceClientExtensions` |

### What IS Duplicated (Action Items)

| Category | Priority | Effort | Action |
|----------|----------|--------|--------|
| PaginationHelper adoption | P1 | Low | **DEFERRED** - existing code works, not worth churn |
| SDK enum parsers | P2 | Low | **COMPLETED** - AssetApiHelpers.cs |
| SDK error creation | P2 | Low | **COMPLETED** - AssetApiHelpers.cs |
| Case-insensitive dict factory | P3 | Low | Create helper (optional) |
| Test realm creation helper | P4 | Low | **COMPLETED** - BaseHttpTestHandler |

---

## Resolution Log

Track completed items here:

### Completed

| Item | Completed | Notes |
|------|-----------|-------|
| P2.1: SDK ParseRealm consolidation | 2026-01-15 | Created `AssetApiHelpers.cs` with `ParseRealm/ParseRealmRequired/ParseBundleType/ParseLifecycle` |
| P2.2: SDK Error creation consolidation | 2026-01-15 | Added `CreateApiException` and `EnsureSuccess` to `AssetApiHelpers.cs` |
| P4.1: Test realm creation helper | 2026-01-15 | Added `CreateTestRealmAsync` to `BaseHttpTestHandler`, updated 3 test handlers |
| P1.1: PaginationHelper review | 2026-01-15 | Reviewed and **DEFERRED** - existing manual patterns work correctly |

### Deferred

| Item | Decision | Rationale |
|------|----------|-----------|
| PaginationHelper retrofitting | Not worth churn | Existing Skip/Take patterns are clear, working, and service-specific |
| Key building patterns (P3.2) | Over-abstraction | Current inline patterns are clear and type-safe |

### Not Started

| Item | Priority | Notes |
|------|----------|-------|
| Case-insensitive dict factory | P3 | Optional - 20+ occurrences but pattern is clear |

---

## Notes

- **Generated code in `*/Generated/`** is excluded from this audit - duplication there is from schema, not manual code
- **Intentional layer separation** (internal models vs API models) is NOT duplication
- **Service-specific patterns** that look similar but have semantic differences should NOT be abstracted
