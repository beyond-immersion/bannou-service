# Contract Plugin Quirks Analysis

> **Date**: 2026-02-01
> **Status**: Issues Created, Documentation Updated
> **Purpose**: Track reorganization of quirks into proper categories with actionable items

---

## Summary of Actions

### Bugs to Fix (4 items)

| # | Issue | Action | Status |
|---|-------|--------|--------|
| B1 | `ClauseValidationCacheStalenessSeconds` is dead config (T21) | Delete from schema OR promote cache to singleton | Pending |
| B2 | `MaxActiveContractsPerEntity` counts ALL contracts, not active | Rename to `MaxTotalContractsPerEntity` | Pending |
| B3 | Missing actual per-entity ACTIVE contract limit | Add new `MaxActiveContractsPerEntity` that filters by status | Pending |
| B4 | `ParseClauseAmount` returns 0 on missing base_amount | Return failure result instead of silent 0 | Pending |

### Missing Configuration (3 items)

| # | Config | Purpose | Default | Status |
|---|--------|---------|---------|--------|
| C1 | `IndexLockFailureMode` | `warn` (log and continue) vs `fail` (throw) | `warn` | Pending |
| C2 | `TermsMergeMode` | `shallow` vs `deep` for CustomTerms merge | `shallow` | Pending |
| C3 | Per-milestone `onApiFailure` | `continue` vs `fail` milestone on callback failure | `continue` | Pending |

### Scalability Improvements (2 items)

| # | Area | Action | Status |
|---|------|--------|--------|
| S1 | Template listing | Implement cursor-based pagination (always, not configurable) | Pending |
| S2 | Index operations | Implement cursor-based pagination for party/status/template indexes | Pending |

### Documentation Reorganization (5 items)

| # | Item | From | To | Status |
|---|------|------|-----|--------|
| D1 | ISO 8601 duration format | Quirk #3 | New "Duration Format Reference" section | **Done** |
| D2 | Breach threshold behavior | Quirk #5 | Part of Intentional Design Decisions | **Done** |
| D3 | Wildcard matching | Design #8 | Removed (standard behavior, documented in endpoint) | **Done** |
| D4 | Milestone deadline architecture | Quirk #4 | New "Milestone Deadline Architecture" section | **Done** |
| D5 | Partial execution & reconciliation | Design #4 | New "Partial Execution & Reconciliation" section | **Done** |

---

## Detailed Analysis

### B1: Dead Configuration - ClauseValidationCacheStalenessSeconds

**Current Behavior**: The `ConcurrentDictionary<string, CachedValidationResult>` is an instance field on a Scoped service. Each HTTP request creates a new service instance with an empty cache. The staleness threshold is never checked across requests.

**Options**:
1. **Delete the config** - If clause validation caching isn't needed cross-request, remove the config (simpler)
2. **Promote to singleton** - Make the cache static or inject a singleton cache service (more complex)

**Recommendation**: Option 1 (delete). Clause validation is lightweight and caching across requests adds complexity without clear benefit. If caching becomes needed, implement properly with Redis.

**Files to modify**:
- `schemas/contract-configuration.yaml` - Remove property
- `plugins/lib-contract/ContractServiceClauseValidation.cs` - Remove cache logic
- `docs/plugins/CONTRACT.md` - Remove from config table and Design Considerations

---

### B2 & B3: MaxActiveContractsPerEntity Misnaming and Missing Limit

**Current Behavior**:
- `MaxActiveContractsPerEntity` checks the party index size
- Party indexes contain ALL contract IDs (active, terminated, fulfilled, expired)
- The limit is effectively "max total contracts ever" not "max active"

**Action Plan**:
1. **Rename existing**: `MaxActiveContractsPerEntity` → `MaxTotalContractsPerEntity`
2. **Add new**: `MaxActiveContractsPerEntity` that filters by Active status at check time
3. **Update CreateContractInstance**: Check both limits

**Files to modify**:
- `schemas/contract-configuration.yaml` - Rename + add new property
- `plugins/lib-contract/ContractService.cs` - Update limit checking logic
- `docs/plugins/CONTRACT.md` - Update config table

---

### B4: ParseClauseAmount Silent Zero on Missing base_amount

**Current Behavior**: When `amount_type: "percentage"` and `base_amount` is missing or unparseable, returns 0.

**Problem**: A fee clause could execute as a zero-amount transfer, silently charging nothing.

**Action Plan**: Return a failure result with clear error message instead of 0.

**Files to modify**:
- `plugins/lib-contract/ContractServiceEscrowIntegration.cs` - Change ParseClauseAmount to return `(decimal?, string? error)`
- Callers handle the error by recording failed distribution

---

### C1: IndexLockFailureMode Configuration

**Current Behavior**: Index lock failures log a warning and continue. The index may become stale.

**Use Cases**:
- `warn` (current): Best-effort, prioritizes availability
- `fail`: Strict consistency, throws on lock failure

**Schema Addition**:
```yaml
IndexLockFailureMode:
  type: string
  enum: [warn, fail]
  default: warn
  description: Behavior when index lock acquisition fails. 'warn' logs and continues (index may be stale), 'fail' throws an exception.
  env: CONTRACT_INDEX_LOCK_FAILURE_MODE
```

---

### C2: TermsMergeMode Configuration

**Current Behavior**: Template terms and instance terms are merged shallowly. CustomTerms dictionary values are replaced, not deep-merged.

**Use Cases**:
- `shallow` (current): Instance values override template values by key
- `deep`: Nested objects are recursively merged

**Schema Addition**:
```yaml
TermsMergeMode:
  type: string
  enum: [shallow, deep]
  default: shallow
  description: How instance terms merge with template terms. 'shallow' replaces values by key, 'deep' recursively merges nested objects.
  env: CONTRACT_TERMS_MERGE_MODE
```

---

### C3: Per-Milestone onApiFailure Flag

**Current Behavior**: Prebound API failures publish failure events but don't fail the milestone.

**Use Cases**:
- `continue` (current): Milestone completes even if callbacks fail
- `fail`: Milestone fails if any callback fails

**This is per-milestone, not global config**. Add to milestone definition in template schema.

**Schema Addition** (in contract-api.yaml MilestoneDefinition):
```yaml
onApiFailure:
  type: string
  enum: [continue, fail]
  default: continue
  description: Behavior when onComplete/onExpire API calls fail. 'continue' marks milestone complete anyway, 'fail' fails the milestone.
```

---

### S1 & S2: Pagination for Listings and Indexes

**Current Behavior**:
- `ListContractTemplates` loads all template IDs, bulk-fetches all, then filters/paginates in memory
- `QueryContractInstances` loads entire party/template/status indexes

**Problem**: O(n) memory for n items, doesn't scale beyond ~10k.

**Solution**: Cursor-based pagination using Redis sorted sets.

**Action Plan**:
1. Change index storage from `List<string>` to Redis sorted sets (score = creation timestamp)
2. Use `ZRANGEBYSCORE` with cursor for pagination
3. Update all listing/query methods to accept cursor and return next cursor

**Note**: This is a significant change. May warrant its own issue/PR.

---

### D1-D5: Documentation Reorganization

These are doc-only changes to move items from "quirks" to proper documentation locations.

**D1 - Duration Format Reference**: Add section explaining XmlConvert.ToTimeSpan supported formats with examples.

**D2 - Breach Threshold**: Move to TerminateContractInstance endpoint description, noting auto-termination behavior.

**D3 - Wildcard Matching**: Move to QueryActiveContracts endpoint description, documenting the `*` suffix pattern.

**D4 - Milestone Deadline Architecture**: New section explaining:
- Why hybrid (lazy + background) was chosen
- Trade-offs vs pure polling vs pure lazy
- Configuration knobs (interval, startup delay)

**D5 - Partial Execution & Reconciliation**: New section explaining:
- Why cross-service transactions are avoided
- How partial failures are recorded
- Escrow's role in reconciliation
- Recommended retry/compensation patterns

---

## Issue Tracking

### Created Issues

| # | Title | Issue |
|---|-------|-------|
| B1 | Remove dead ClauseValidationCacheStalenessSeconds config (T21) | [#241](https://github.com/beyond-immersion/bannou-service/issues/241) |
| B2+B3 | Fix MaxActiveContractsPerEntity to actually count active contracts | [#242](https://github.com/beyond-immersion/bannou-service/issues/242) |
| B4 | ParseClauseAmount should fail on missing base_amount, not return 0 | [#243](https://github.com/beyond-immersion/bannou-service/issues/243) |
| C1 | Add IndexLockFailureMode configuration | [#244](https://github.com/beyond-immersion/bannou-service/issues/244) |
| C2 | Add TermsMergeMode configuration | [#245](https://github.com/beyond-immersion/bannou-service/issues/245) |
| C3 | Add per-milestone onApiFailure flag | [#246](https://github.com/beyond-immersion/bannou-service/issues/246) |
| S1+S2 | Implement cursor-based pagination for listings and indexes | [#247](https://github.com/beyond-immersion/bannou-service/issues/247) |

---

## Execution Order

1. ~~Create all GitHub issues (reference numbers for tracking)~~ **Done** - Issues #241-#247
2. ~~Update CONTRACT.md with reorganized documentation~~ **Done** - Added Duration Format Reference, Milestone Deadline Architecture, Partial Execution & Reconciliation sections
3. Update `contract-configuration.yaml` schema with new configs - **Pending** (per issue fixes)
4. Update `contract-api.yaml` schema with onApiFailure milestone flag - **Pending** (Issue #246)
5. Regenerate contract service - **Pending** (after schema changes)
6. Implement configuration usage in service code - **Pending** (per issue fixes)
7. Pagination (S1/S2) is larger scope - **Pending** (Issue #247)

---

## Notes

- Pagination change (S1/S2) affects multiple services, may need lib-state enhancement for sorted set pagination helpers
- The "always paginate" approach means we need reasonable defaults (page size ~100?)
- Consider whether pagination cursors should be opaque tokens or explicit (offset, timestamp)
