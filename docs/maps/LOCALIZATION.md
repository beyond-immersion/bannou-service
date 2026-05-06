# Localization Implementation Map

> **Plugin**: lib-localization
> **Schema**: schemas/localization-api.yaml
> **Layer**: AppFoundation
> **Deep Dive**: [docs/plugins/LOCALIZATION.md](../plugins/LOCALIZATION.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-localization |
| Layer | L1 AppFoundation |
| Endpoints | 12 (12 generated) |
| State Stores | localization-category-store (MySQL), localization-entry-store (MySQL), localization-compiled-cache (Redis), localization-lock (Redis) |
| Events Published | 3 (localization.category.created, localization.category.updated, localization.category.deleted) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `localization-category-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `category:{categoryId}` | `LocalizationCategoryModel` | Category definition (schema-defined or runtime) |
| `category-code:{code}` | `string` | Reverse index: code → categoryId for uniqueness and code-based lookup |

**Store**: `localization-entry-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `entry:{categoryId}:{language}:{key}` | `LocalizationEntryModel` | Single translation entry |

**Store**: `localization-compiled-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `compiled:{categoryId}:{language}` | `string` (serialized JSON) | Compiled export bundle for a category + language, TTL from CacheExpirationMinutes |
| `compiled:all:{language}` | `string` (serialized JSON) | Compiled export bundle for all categories + language |

**Store**: `localization-lock` (Backend: Redis)

Used via `IDistributedLockProvider` for category and entry write coordination.

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Category store (MySQL), entry store (MySQL), compiled cache (Redis), lock store (Redis) |
| lib-state (IDistributedLockProvider) | L0 | Hard | Category write locks, entry write locks |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing localization.category.* lifecycle events |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on all async methods |

No service client dependencies. L1 cannot depend on L2+. The `ILocalizationKeyValidator` DI interface inverts the dependency direction — L2+ services pull validation from this plugin.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `localization.category.created` | `LocalizationCategoryCreatedEvent` | CreateCategory, OnStartAsync (schema-defined seeding, first deploy only) |
| `localization.category.updated` | `LocalizationCategoryUpdatedEvent` | UpdateCategory, SetEntry, DeleteEntry, BulkSetEntries — carries `changedFields` (entry mutations use `["entries"]`) |
| `localization.category.deleted` | `LocalizationCategoryDeletedEvent` | DeleteCategory (runtime categories only) |

---

## Events Consumed

This plugin does not consume external events. No `account.deleted` handler needed (system data, not per-account). No `x-references` cleanup callbacks.

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<LocalizationService>` | Structured logging |
| `LocalizationServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access — resolves 6 typed stores in constructor, not stored as field |
| `IDistributedLockProvider` | Distributed locking for category/entry write operations |
| `IMessageBus` | Publishing `localization.category.*` lifecycle events |
| `ITelemetryProvider` | Span instrumentation |
| `IEventConsumer` | Event consumer registration (empty body — no events consumed) |

**Store references acquired in constructor** (from `IStateStoreFactory`):
- `_categoryStore` — `GetStore<LocalizationCategoryModel>(StateStoreDefinitions.LocalizationCategoryStore)`
- `_categoryQueryStore` — `GetQueryableStore<LocalizationCategoryModel>(StateStoreDefinitions.LocalizationCategoryStore)`
- `_categoryCodeStore` — `GetStore<string>(StateStoreDefinitions.LocalizationCategoryStore)`
- `_entryStore` — `GetStore<LocalizationEntryModel>(StateStoreDefinitions.LocalizationEntryStore)`
- `_entryQueryStore` — `GetQueryableStore<LocalizationEntryModel>(StateStoreDefinitions.LocalizationEntryStore)`
- `_compiledCache` — `GetStore<string>(StateStoreDefinitions.LocalizationCompiledCache)`

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `ILocalizationKeyValidator` | `Singleton` | L2+→L1 pull | L2+ services discover via `IEnumerable<ILocalizationKeyValidator>` to validate localizationKeyPrefix fields at entity creation |
| `ILocalizationSource` | `Singleton` | L4(aggregator)→L1 pull | `FileLocalizationProvider` (in lib-behavior) discovers all sources via `IEnumerable<ILocalizationSource>`. This plugin's `LocalizationServiceSource` registers at priority 100 (overrides embedded YAML sources at priority 50). Implements `GetText(key, locale)` for the 3-part dotted form `{categoryCode}.{key}` against per-locale `ExportLocalizationAsync` bundles cached in memory. Cache invalidation via dual event subscriptions: `localization.category.updated` invalidates the affected language; `localization.category.deleted` invalidates ALL cached bundles (cascade-deleted entries span every language the category had — the event payload doesn't enumerate them). Both subscriptions ensure multi-node correctness. See `LocalizationServiceSource.cs`. |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CreateCategory | POST /localization/category/create | generated | admin | category, category-code | localization.category.created |
| GetCategory | POST /localization/category/get | generated | [] | - | - |
| ListCategories | POST /localization/category/list | generated | [] | - | - |
| UpdateCategory | POST /localization/category/update | generated | admin | category | localization.category.updated |
| DeleteCategory | POST /localization/category/delete | generated | admin | category, category-code, entries, compiled-cache | localization.category.deleted |
| SetEntry | POST /localization/entry/set | generated | admin | entry, category (entryCount), compiled-cache | localization.category.updated |
| GetEntry | POST /localization/entry/get | generated | [] | - | - |
| ListEntries | POST /localization/entry/list | generated | [] | - | - |
| DeleteEntry | POST /localization/entry/delete | generated | admin | entry, category (entryCount), compiled-cache | localization.category.updated |
| BulkSetEntries | POST /localization/entry/bulk-set | generated | admin | entries, category (entryCount), compiled-cache | localization.category.updated |
| Export | POST /localization/export | generated | [] | compiled-cache (write-through) | - |
| ExportPls | POST /localization/export-pls | generated | [] | - | - |

---

## Methods

### CreateCategory
POST /localization/category/create | Roles: [admin]

```
LOCK lockProvider:"category-code:{body.Code}"
  READ categoryStore:category-code:{body.Code}            -> 409 if exists
  categoryId = NewGuid
  WRITE categoryStore:category:{categoryId}               <- LocalizationCategoryModel from request
    { IsSchemaDefinition = false,
      ValidationMode = body.ValidationMode ?? config.DefaultValidationMode,
      DefaultLanguage = body.DefaultLanguage ?? config.DefaultLanguage,
      EntryCount = 0 }
  WRITE categoryStore:category-code:{body.Code}           <- categoryId string
  PUBLISH localization.category.created { categoryId, code, validationMode, defaultLanguage, entryCount: 0 }
RETURN (200, CategoryResponse { categoryId })
```

### GetCategory
POST /localization/category/get | Roles: []

```
IF body.CategoryId != null
  READ categoryStore:category:{body.CategoryId}           -> 404 if null
ELSE IF body.Code != null
  READ categoryStore:category-code:{body.Code}            -> 404 if null
  READ categoryStore:category:{resolved categoryId}       -> 404 if null
ELSE
  RETURN (400, null)  // neither provided
RETURN (200, CategoryResponse)
```

### ListCategories
POST /localization/category/list | Roles: []

```
IF body.IncludeSchemaDefinitions
  predicate = null  // all categories
ELSE
  predicate = c => !c.IsSchemaDefinition
QUERY categoryQueryStore WHERE predicate PAGED(body.Page - 1, body.PageSize)
RETURN (200, ListCategoriesResponse { categories, totalCount, page, pageSize })
```

### UpdateCategory
POST /localization/category/update | Roles: [admin]

```
LOCK lockProvider:"category:{body.CategoryId}"              -> 409 if fails
  READ categoryStore:category:{body.CategoryId}             -> 404 if null
  // Apply partial update: description, validationMode, defaultLanguage only
  // code and isSchemaDefinition are immutable
  changedFields = [detect changed properties from non-null request fields]
  model.UpdatedAt = UtcNow
  WRITE categoryStore:category:{body.CategoryId}            <- updated model
  // Always publishes even if changedFields is empty
  PUBLISH localization.category.updated { categoryId, code, changedFields }
RETURN (200, CategoryResponse)
```

### DeleteCategory
POST /localization/category/delete | Roles: [admin]

```
LOCK lockProvider:"category:{body.CategoryId}"
  READ categoryStore:category:{body.CategoryId}           -> 404 if null
  IF model.IsSchemaDefinition
    RETURN (400, null)  // schema-defined categories cannot be deleted
  entryCount = model.EntryCount
  // Cascade: delete all entries for this category
  QUERY entryStore WHERE $.CategoryId == body.CategoryId
  FOREACH entry in results
    DELETE entryStore:entry:{body.CategoryId}:{entry.Language}:{entry.Key}
    // per-item try-catch, LogWarning on failure
  DELETE categoryStore:category:{body.CategoryId}
  DELETE categoryStore:category-code:{model.Code}
  // Invalidate all compiled cache entries for this category
  // Delete known cache keys by language enumeration
  PUBLISH localization.category.deleted { categoryId, code, entryCount }
RETURN (200, DeleteCategoryResponse)
```

### SetEntry
POST /localization/entry/set | Roles: [admin]

```
LOCK lockProvider:"category:{body.CategoryId}:entries"       -> 409 if fails
  READ categoryStore:category:{body.CategoryId}              -> 404 if null
  READ entryStore:entry:{body.CategoryId}:{body.Language}:{body.Key}
  isNew = (existing == null)
  IF isNew AND category.EntryCount >= config.MaxEntriesPerCategory
    RETURN (409, null)
  WRITE entryStore:entry:{body.CategoryId}:{body.Language}:{body.Key}
    <- LocalizationEntryModel { EntryId = existing?.EntryId ?? NewGuid, ... }
  IF isNew
    READ categoryStore:category:{body.CategoryId} [with ETag]
    model.EntryCount += 1
    ETAG-WRITE categoryStore:category:{body.CategoryId}
  DELETE compiledCache:compiled:{body.CategoryId}:{body.Language}
  DELETE compiledCache:compiled:all:{body.Language}
  PUBLISH localization.category.updated
    { categoryId, entryCount, lastEntryUpdateLanguage: body.Language, changedFields: ["entries"] }
RETURN (200, EntryResponse { entryId })
```

### GetEntry
POST /localization/entry/get | Roles: []

```
READ categoryStore:category:{body.CategoryId}             -> 404 if null
READ entryStore:entry:{body.CategoryId}:{body.Language}:{body.Key}  -> 404 if null
RETURN (200, EntryResponse)
```

### ListEntries
POST /localization/entry/list | Roles: []

```
READ categoryStore:category:{body.CategoryId}               -> 404 if null
// Predicate built incrementally from provided filters
IF body.Language != null AND body.KeyPrefix != null
  predicate = e.CategoryId == categoryId AND e.Language == language AND e.Key.StartsWith(keyPrefix)
ELSE IF body.Language != null
  predicate = e.CategoryId == categoryId AND e.Language == language
ELSE IF body.KeyPrefix != null
  predicate = e.CategoryId == categoryId AND e.Key.StartsWith(keyPrefix)
ELSE
  predicate = e.CategoryId == categoryId
QUERY entryQueryStore WHERE predicate PAGED(body.Page - 1, body.PageSize)
RETURN (200, ListEntriesResponse { entries, totalCount, page, pageSize })
```

### DeleteEntry
POST /localization/entry/delete | Roles: [admin]

```
READ categoryStore:category:{body.CategoryId}               -> 404 if null
READ entryStore:entry:{body.CategoryId}:{body.Language}:{body.Key}  -> 404 if null
// Entry existence check is BEFORE lock — benign TOCTOU (delete is idempotent)
LOCK lockProvider:"category:{body.CategoryId}:entries"       -> 409 if fails
  DELETE entryStore:entry:{body.CategoryId}:{body.Language}:{body.Key}
  READ categoryStore:category:{body.CategoryId} [with ETag]
  model.EntryCount = Math.Max(0, model.EntryCount - 1)
  ETAG-WRITE categoryStore:category:{body.CategoryId}
  DELETE compiledCache:compiled:{body.CategoryId}:{body.Language}
  DELETE compiledCache:compiled:all:{body.Language}
  PUBLISH localization.category.updated
    { categoryId, entryCount, lastEntryUpdateLanguage: body.Language, changedFields: ["entries"] }
RETURN (200, DeleteEntryResponse)
```

### BulkSetEntries
POST /localization/entry/bulk-set | Roles: [admin]

```
READ categoryStore:category:{body.CategoryId}               -> 404 if null
LOCK lockProvider:"category:{body.CategoryId}:entries"       -> 409 if fails
  READ categoryStore:category:{body.CategoryId}              // fresh read under lock for cap check
  // Worst-case cap check: if all entries are new, would we exceed?
  IF category.EntryCount + body.Entries.Count > config.MaxEntriesPerCategory
    RETURN (409, null)
  successCount = 0
  failureCount = 0
  newCount = 0
  FOREACH item in body.Entries
    // per-item try-catch per T7
    READ entryStore:entry:{body.CategoryId}:{body.Language}:{item.Key}
    isNew = (existing == null)
    WRITE entryStore:entry:{body.CategoryId}:{body.Language}:{item.Key}
      <- LocalizationEntryModel from item
    IF isNew: newCount++
    successCount++
    // catch: LogWarning, failureCount++
  IF newCount > 0
    READ categoryStore:category:{body.CategoryId} [with ETag]
    model.EntryCount += newCount
    ETAG-WRITE categoryStore:category:{body.CategoryId}
  DELETE compiledCache:compiled:{body.CategoryId}:{body.Language}
  DELETE compiledCache:compiled:all:{body.Language}
  // Single event for entire batch
  PUBLISH localization.category.updated
    { categoryId, entryCount: category.EntryCount + newCount, lastEntryUpdateLanguage: body.Language, changedFields: ["entries"] }
RETURN (200, BulkSetEntriesResponse { succeededCount, failedCount })
```

### Export
POST /localization/export | Roles: []

```
cacheKey = body.CategoryId != null
  ? "compiled:{body.CategoryId}:{body.Language}"
  : "compiled:all:{body.Language}"
READ compiledCache:{cacheKey}
IF cached != null
  // BannouJson.Deserialize — falls through gracefully if null/corrupted
  RETURN (200, ExportResponse { deserialized cached bundle })
// Cache miss — compile from MySQL
IF body.CategoryId != null
  READ categoryStore:category:{body.CategoryId}             -> 404 if null
  categories = [model]
ELSE
  QUERY categoryQueryStore WHERE true  // all categories
entries = []
FOREACH category in categories
  page = 0
  hasMore = true
  WHILE hasMore
    QUERY entryQueryStore WHERE $.CategoryId == catId AND $.Language == body.Language
      PAGED(page, config.ExportPageSize)
    entries.AddRange(results)
    hasMore = (results.Count == config.ExportPageSize)
    page++
bundle = CompileBundle(entries, categories)
WRITE compiledCache:{cacheKey} <- BannouJson.Serialize(bundle)
  WITH TTL = config.CacheExpirationMinutes * 60 seconds
RETURN (200, ExportResponse { language, entries, entryCount })
```

### ExportPls
POST /localization/export-pls | Roles: []

```
// No caching — compiles directly from MySQL on every call
IF body.CategoryId != null
  READ categoryStore:category:{body.CategoryId}             -> 404 if null
  categories = [model]
ELSE
  QUERY categoryQueryStore WHERE true  // all categories
pronunciationEntries = []
FOREACH category in categories
  QUERY entryQueryStore WHERE $.CategoryId == catId
                          AND $.Language == body.Language
                          AND $.Pronunciation != null
  pronunciationEntries.AddRange(results)
// Build W3C PLS XML using SecurityElement.Escape for XML entity escaping
// <lexicon version="1.0" alphabet="ipa" xml:lang="{language}">
//   <lexeme><grapheme>{key}</grapheme><phoneme>{pronunciation}</phoneme></lexeme>
plsXml = BuildPlsXml(pronunciationEntries, body.Language)
RETURN (200, ExportPlsResponse { language, plsXml, entryCount })
```

---

## Background Services

No background services.

---

## Non-Standard Implementation Patterns

#### OnStartAsync — Schema-Defined Category Seeding

Implemented directly on `LocalizationService` (not on the plugin wrapper). Uses `LocalizationCategoryDefinitions.Metadata` dictionary.

```
// Seeds categories from LocalizationCategoryDefinitions.Metadata — idempotent on every deploy
definitions = LocalizationCategoryDefinitions.Metadata
FOREACH definition in definitions
  // per-item try-catch per T7
  READ categoryStore:category-code:{definition.Code}
  IF null  // not yet seeded
    categoryId = NewGuid
    WRITE categoryStore:category:{categoryId} <- LocalizationCategoryModel
      { IsSchemaDefinition = true, Code = definition.Code,
        ValidationMode = definition.ValidationMode,
        DefaultLanguage = definition.DefaultLanguage, EntryCount = 0 }
    WRITE categoryStore:category-code:{definition.Code} <- categoryId string
    PUBLISH localization.category.created { categoryId, code, isSchemaDefinition: true }
  // If already exists: no-op (idempotent)
```

#### ILocalizationKeyValidator Implementation

Separate class: `Services/LocalizationKeyValidator.cs`, registered as `Singleton`.
Holds its own store references to `LocalizationCategoryStore` and `LocalizationEntryStore`.

```
// Discovered by L2+ via IEnumerable<ILocalizationKeyValidator>
ValidateLocalizationKeyAsync(categoryCode, keyPrefix, keyId?, ct):
  READ categoryCodeStore:category-code:{categoryCode}
  IF null: return true  // category unknown, lenient fallback
  GUID parse result                                         -> return true if fails
  READ categoryStore:category:{categoryId}
  IF null: return true
  IF category.ValidationMode == None: return true
  IF keyId != null
    READ entryStore:entry:{categoryId}:{category.DefaultLanguage}:{keyPrefix}.{keyId}
    exists = (result != null)
  ELSE
    QUERY entryQueryStore WHERE $.CategoryId == categoryId
                            AND $.Language == category.DefaultLanguage
                            AND $.Key.StartsWith(keyPrefix)
      PAGED(0, 1)  // existence check only
    exists = (any result)
  IF !exists AND category.ValidationMode == WarnOnMissing
    // LogWarning, return true
  return exists  // false only for RejectOnMissing + key absent
```

Distributed safety: always safe — reads from MySQL/Redis distributed state.
