# Localization Plugin Deep Dive

> **Plugin**: lib-localization (not yet created)
> **Schema**: `schemas/localization-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: localization-category-store (MySQL), localization-entry-store (MySQL), localization-compiled-cache (Redis), localization-lock (Redis) — all planned
> **Layer**: AppFoundation
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Short**: Multi-language translation tables with category lifecycle, pronunciation annotations, bulk export, and DI-based key validation for cross-service localization

---

## Overview

The Localization service (L1 AppFoundation) manages structured translation tables that map language × category × key to translated text with optional pronunciation annotations (IPA phonemes for TTS consumption). Categories organize translation entries by domain (items, quests, locations, UI, lexicon codes). The service provides bulk export for client-side caching (Pattern C distribution via Asset bundles), W3C PLS pronunciation lexicon export for TTS engines, and a DI-based key validation interface (`ILocalizationKeyValidator`) that L2+ services optionally use to verify localization keys exist at entity creation time. When the localization plugin is not loaded, validation is silently skipped — higher-layer services are unaware of whether validation is active.

L1 placement is deliberate: localization is infrastructure that any layer needs (L2 game entities have display names, L3 documentation/website need translations, L4 features reference localized text). Like Chat and Permission, it is a thin data service that everything above can reference without hierarchy concerns.

TTS rendering is explicitly **not** a Localization service concern — it is a client-side operation. The service stores pronunciation data; clients consume it via Kokoro (Apache 2.0), Azure Cognitive Services, or any SSML-consuming TTS engine. This follows the established principle that all AI/neural inference stays client-side (per FAQ: WHY-DOESNT-BANNOU-USE-AI-FOR-CONTENT-GENERATION).

---

## Localization Architecture

### Data Model

```
Category (organizational container)
├── categoryId: Guid
├── code: string (unique, e.g., "items", "quests", "lexicon", "ui")
├── description: string
├── isSchemaDefinition: bool (true = seeded from localization-categories.yaml, cannot be deleted via API)
├── validationMode: ValidationMode enum (None | WarnOnMissing | RejectOnMissing)
├── defaultLanguage: string (e.g., "en")
└── entries: Entry[]

Entry (single translated string)
├── entryId: Guid
├── categoryId: Guid (FK)
├── key: string (dot-separated: "{prefix}.{suffix}", e.g., "direwolf.name", "rescue-princess.title")
├── language: string (BCP 47: "en", "ja-JP", "fr-FR")
├── text: string (the translated value, supports {0} parameter placeholders)
├── pronunciation: string? (IPA phonemes, e.g., "ˈdaɪ.ɚ.wʊlf")
├── ruby: RubyAnnotation[]? (for CJK: base text + reading pairs)
└── updatedAt: DateTimeOffset

L2 templates store localizationKeyPrefix (e.g., "direwolf").
Multiple entries per prefix: direwolf.name, direwolf.description, direwolf.tooltip, etc.
Client resolves {prefix}.{suffix} against cached localization table.
```

### Key Design: Schema-First Category Registry

Categories are defined in a central schema file `schemas/localization-categories.yaml`, following the same pattern as `schemas/state-stores.yaml` for state store definitions. This generates:

1. **`LocalizationCategoryDefinitions.cs`** — `public const string` fields for each category (e.g., `LocalizationCategoryDefinitions.Items`, `.Quests`, `.Locations`)
2. **Generated documentation** — Category descriptions, validation modes, and consumer plugins appear in `docs/GENERATED-LOCALIZATION-CATEGORIES.md` (same as `GENERATED-STATE-STORES.md` for state stores)

```
schemas/localization-categories.yaml
├── items:
│   description: Item template display names and descriptions
│   validationMode: RejectOnMissing
│   consumers: [lib-item]
├── quests:
│   description: Quest definition names and descriptions
│   validationMode: WarnOnMissing
│   consumers: [lib-quest]
├── locations:
│   description: Location display names
│   validationMode: RejectOnMissing
│   consumers: [lib-location]
├── lexicon:
│   description: Lexicon concept display names for client rendering
│   validationMode: None
│   consumers: [lib-lexicon]
└── ... (species, seeds, collections, realms, relationships, ui)
```

**Structural enforcement** (two-sided validation, same pattern as state store structural tests):

| Test | What It Validates |
|------|------------------|
| `LocalizationCategories_AreAllReferenced` | Every constant in `LocalizationCategoryDefinitions` is used by at least one declared consumer plugin |
| `LocalizationValidator_UsesGeneratedConstants` | Every call to `ILocalizationKeyValidator.ValidateKeyExistsAsync` passes a `LocalizationCategoryDefinitions` constant as the category argument (no hardcoded strings) |

**Hybrid lifecycle**: Schema-defined categories are seeded on startup (like Chat's built-in room types — idempotent, cannot be deleted via API). The API also supports runtime category CRUD for L5 extensions creating game-specific categories. Lifecycle events (`localization.category.created`, `.updated`, `.deleted`) apply to runtime categories only. Schema-defined categories publish `.updated` events when their entries change.

Individual entries do NOT have their own lifecycle events — entry mutations are reflected in `localization.category.updated` via `changedFields` (e.g., `["entries"]`), keeping the event surface minimal. The `x-lifecycle` model for Category includes `entryCount` (integer) and `lastEntryUpdateLanguage` (string, nullable) so lifecycle events carry actionable metadata about entry changes without including all entries.

### Key Design: Validation Mode

Each category has a `validationMode` controlling how the DI validation interface behaves:

| Mode | Behavior |
|------|----------|
| `None` | Key validation always returns true (no checking) |
| `WarnOnMissing` | Returns true but logs a warning when a key doesn't exist |
| `RejectOnMissing` | Returns false when a key doesn't exist (caller gets BadRequest) |

This makes validation configurable per category — strict for production-critical categories (item names), lenient for categories still being populated.

### Key Design: Pronunciation as Localization Data

Pronunciation annotations are per-entry, per-language fields stored alongside the translated text. This is correct because:

1. Fantasy terms may be pronounced differently in different languages (Japanese players say "ダイアウルフ" not "Direwolf")
2. Ruby text (furigana) is language-specific (Japanese uses hiragana/katakana readings, Chinese uses pinyin)
3. The pronunciation dictionary IS localization data, not a separate concern

The `/localization/export-pls` endpoint compiles pronunciation entries into W3C PLS XML format for consumption by SSML-supporting TTS engines. Kokoro consumes IPA directly via inline notation.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none currently — all planned)* | |

> **Planned consumers**:
> - **Item** (L2): Item templates gain a `localizationKeyPrefix` field. On `CreateTemplate`, validates key exists in the "items" category via `ILocalizationKeyValidator`.
> - **Quest** (L2): Quest definitions gain a `localizationKeyPrefix` field for quest names/descriptions.
> - **Species** (L2): Species definitions gain a `localizationKeyPrefix` for species display names.
> - **Location** (L2): Location records gain a `localizationKeyPrefix` for location names.
> - **Seed** (L2): Seed type definitions gain a `localizationKeyPrefix` for seed type display names.
> - **Collection** (L2): Collection types gain a `localizationKeyPrefix` for collection display names.
> - **Realm** (L2): Realm definitions gain a `localizationKeyPrefix` for realm names.
> - **Relationship** (L2): Relationship type definitions gain a `localizationKeyPrefix` for display names.
> - **Lexicon** (L4, planned): Lexicon concept entries map to localization keys — the `displayKey` field already envisioned in issue #508.
> - **Documentation** (L3): Voice summaries could reference localization for multi-language document titles.
> - **Website** (L3): Browser-facing content localization.
> - **Game clients**: Bulk download localization tables on connect, cache locally, use for all display text rendering and TTS pronunciation injection.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxRuntimeCategories` | `LOCALIZATION_MAX_RUNTIME_CATEGORIES` | 100 | Maximum runtime-created categories (schema-defined categories are unlimited) |
| `MaxEntriesPerCategory` | `LOCALIZATION_MAX_ENTRIES_PER_CATEGORY` | 10000 | Maximum entries per category (safety cap) |
| `MaxLanguages` | `LOCALIZATION_MAX_LANGUAGES` | 50 | Maximum supported languages |
| `DefaultLanguage` | `LOCALIZATION_DEFAULT_LANGUAGE` | "en" | Fallback language when requested language has no entry |
| `DefaultValidationMode` | `LOCALIZATION_DEFAULT_VALIDATION_MODE` | None | Default validation mode for new categories (None, WarnOnMissing, RejectOnMissing) |
| `CacheExpirationMinutes` | `LOCALIZATION_CACHE_EXPIRATION_MINUTES` | 60 | Redis TTL for compiled export cache |
| `LockExpirySeconds` | `LOCALIZATION_LOCK_EXPIRY_SECONDS` | 15 | Distributed lock expiry timeout |
| `ExportPageSize` | `LOCALIZATION_EXPORT_PAGE_SIZE` | 5000 | Entries per page when building compiled export |
| `ServerSalt` | `LOCALIZATION_SERVER_SALT` | (dev default) | Server salt for session shortcut GUID generation |

---

## Visual Aid

```
Entry Lifecycle & Export Flow
===============================

 Entry Mutation (set/delete)
 │
 ┌───────┴───────┐
 │ MySQL         │
 │ (entry-store) │
 └───────┬───────┘
         │
    ┌────┴──────────────┐
    │                   │
    ▼                   ▼
 Invalidate         Publish
 Redis cache        localization.category.updated
 (per-language)     { changedFields: ["entries"],
                      entryCount, language }

 Client Export Request
 │
 ┌───┴────────┐
 │ Redis      │  hit? → return cached bundle
 │ compiled-  │
 │ cache      │  miss? → compile from MySQL
 └───┬────────┘         → cache with TTL
     │                  → return compiled bundle
     ▼
 Client caches locally
 │
 ├── Display text: resolve {prefix}.{suffix} → localized string
 ├── TTS: inject pronunciation IPA → render via Kokoro/Azure
 └── CJK: overlay ruby annotations → furigana display

 Schema-First Category Registry
 │
 schemas/localization-categories.yaml
 │
 ├──generates──▶ LocalizationCategoryDefinitions.cs
 │               (Items, Quests, Locations, Lexicon, ...)
 │
 ├──generates──▶ GENERATED-LOCALIZATION-CATEGORIES.md
 │               (documentation with consumers, modes)
 │
 └──enforces───▶ Structural Tests
                 ├── every constant used by declared consumer
                 └── every validator call uses a constant

 DI Validation (cross-layer, optional)
 │
 Item.CreateTemplate(localizationKeyPrefix: "direwolf")
 │
 ├── IEnumerable<ILocalizationKeyValidator> empty?
 │   └── YES → skip validation (plugin not loaded)
 │   └── NO  → validate(Categories.Items, "direwolf", keyId: null)
 │            → prefix check: any entry "direwolf.*" exists?
 │
 └── proceed or BadRequest
```

---

## Cross-Layer Validation Interface

The localization validation interface follows the established DI Provider pattern (same as `IPrerequisiteProviderFactory`, `IVariableProviderFactory`). Interface lives in `bannou-service/Providers/`, lib-localization implements it, and L2+ services discover via `IEnumerable<T>` constructor injection.

| Aspect | Detail |
|--------|--------|
| **Interface** | `ILocalizationKeyValidator` in `bannou-service/Providers/` |
| **Method** | `ValidateLocalizationKeyAsync(categoryCode, keyPrefix, keyId?, cancellationToken)` → `ValueTask<bool>` |
| **Category argument** | Must be a `LocalizationCategoryDefinitions` constant (structural test enforced) |
| **keyPrefix** | The localization key prefix stored on the entity (e.g., `"direwolf"`) |
| **keyId** | Optional specific suffix (e.g., `"name"`, `"description"`). When null → validates prefix has at least one entry. When provided → validates exact entry `{keyPrefix}.{keyId}` exists |
| **Implementor** | lib-localization (checks `validationMode` per category from schema registry) |
| **Consumers** | L2+ services via `IEnumerable<ILocalizationKeyValidator>` constructor injection |
| **When not loaded** | `IEnumerable` is empty → validation skipped → creation proceeds |
| **Distributed safety** | Always safe — reads from distributed state (MySQL/Redis) |

The three-argument signature makes the validation intent explicit: callers know whether they're validating "this prefix is populated" vs. "this specific key exists," with no room for ambiguity. Consumer plugins never hardcode category strings — they use generated constants, and structural tests enforce this on both sides.

### Permission Model

Localization endpoints are **not directly client-facing**. Entry management is `role: admin` / `role: developer`. Export and query endpoints are service-only (`[]`) or `role: admin`. Clients access localized data through **Agency's shortcut API**, which surfaces localization content through the spirit's UX capability manifest — the same pattern Agency uses for all player-facing data. This keeps localization as internal infrastructure and delegates client-facing access control to the service that owns the player experience context.

---

## Entity Lifecycle & Cleanup Analysis

### Deprecation (T31 Decision Tree)

**Schema-defined categories**: Seeded on startup from `schemas/localization-categories.yaml`. Cannot be deleted via API (same as Chat built-in room types). → **No deprecation, no deletion — they are infrastructure constants.**

**Runtime categories**: Created via API for L5 extensions or game-specific needs. Do other services store category IDs? No — L2 services store `localizationKeyPrefix` strings via generated `LocalizationCategoryDefinitions` constants, not category GUIDs. Runtime categories are referenced only by their own entries. → **No deprecation. Immediate hard delete.**

**Entries**: Sub-entity data within categories. Cascaded on category deletion. Not referenced by ID from other services. → **No deprecation. Immediate hard delete (cascading with parent category).**

### Resource Cleanup (T28)

**Account deletion**: Localization stores system/admin data, not per-account data. No `ownerType: Account` fields. → **No `account.deleted` handler needed.**

**Cross-service references**: L2 services hold `localizationKeyPrefix` strings as soft references. If a localization key is deleted, the L2 entity is unaffected — client-side resolution falls back to the default language. No referential integrity requirement. → **No `x-references` declarations needed. No lib-resource cleanup callbacks.**

### Client Events (T17)

Localization follows Pattern C distribution (client-initiated pull). Clients download and cache localization tables. No real-time server push of localization changes is needed — the user confirmed "this isn't something we're going to auto-push." → **No client events schema (`localization-client-events.yaml`) needed.** Category lifecycle events are for server-side consumers (cache invalidation, asset bundle recompilation).

### Topic Naming (T16)

Localization has one lifecycle entity type (Category) and entries as sub-entity data. "Category" alone is ambiguous — multiple services could have categories. → **Pattern C** (dot-separated service namespace): `localization.category.created`, `localization.category.updated`, `localization.category.deleted`.

### Ruby Annotation Type

The `ruby` field on entries is an array of structured annotations for CJK text rendering:

| Field | Type | Description |
|-------|------|-------------|
| `base` | string | The base text segment being annotated (e.g., kanji characters) |
| `reading` | string | The phonetic reading (e.g., hiragana for Japanese, pinyin for Chinese) |
| `startIndex` | integer | Character position in the translated `text` where this annotation begins |

This model is defined in the localization API schema as `RubyAnnotation`. Ruby annotations are optional — most entries (non-CJK, or CJK entries where all characters are common) will have `null` for the ruby field.

---

## Stubs & Unimplemented Features

*(Aspirational — no schema or code exists yet)*

---

## Potential Extensions

1. **Asset bundle integration**: Instead of direct API export, compile localization tables into `.bannou` asset bundles via lib-asset. Clients download localization as a versioned asset bundle, cached via CDN. Category lifecycle events trigger bundle recompilation.

2. **Fallback chain configuration**: Per-category fallback chains (e.g., `ja-JP` → `ja` → `en`) instead of a single `defaultLanguage`. Follows BCP 47 language tag hierarchy.

3. **Import formats**: XLIFF and PO/POT import endpoints for professional translation workflows. Translators work in their preferred CAT tools; results are imported via API.

4. **Parameterized entry validation**: Template entries with `{0}`, `{1}` placeholders — validate that all language variants have the same parameter count.

5. **Localization completeness report**: Admin endpoint reporting percentage coverage per category × language. Useful for tracking translation progress.

6. **Scenario-bound localization**: Storyline scenarios compose dynamic content from template-based localization entries. Scenario data includes localization keys for quest titles, NPC dialogue templates, and objective descriptions. Client-side admin/developer tools add translations at a delay; untranslated strings fall back to `defaultLanguage`.

7. **ABML `ILocalizationProvider` bridge**: The behavior compiler already has `ILocalizationProvider` + `FileLocalizationProvider` for ABML `${localization()}` expressions. lib-localization implements `ILocalizationProvider` backed by its state stores, replacing file-based resolution with centralized management.

8. **Structural test: localization key coverage**: CI test that scans all entity template schemas for `localizationKeyPrefix` fields, extracts referenced prefixes, and validates they have at least one entry in the default language's localization tables. Catches dangling references at build time.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(Aspirational — no implementation exists)*

### Intentional Quirks (Documented Behavior)

1. **Entries are NOT lifecycle entities**: Individual translation entries have no lifecycle events. Entry mutations (create/update/delete) are reflected in the parent category's `localization.category.updated` event via `changedFields`. This keeps the event surface minimal — a bulk import of 5,000 entries produces one category update event, not 5,000 entry events.

2. **Pronunciation and ruby are parallel fields, not embedded markup**: IPA phonemes and ruby annotations are stored as structured data alongside the translated text, not as inline SSML or HTML markup. This allows different consumers (TTS engines, game UIs, accessibility tools) to use the data in format-appropriate ways without parsing embedded markup.

3. **W3C PLS export is read-only compilation**: The `/export-pls` endpoint compiles pronunciation entries into standard PLS XML. It does not support PLS import — pronunciation data is authored via the entry API. PLS is an output format for TTS engines (Azure, Kokoro SSML mode), not an interchange format.

4. **Schema-defined categories cannot be deleted**: Categories seeded from `schemas/localization-categories.yaml` reject delete requests (same as Chat built-in room types). Only runtime-created categories can be deleted. Deleting a runtime category cascades all entries in that category. The `.deleted` event carries the category code and entry count, not individual entries.

5. **Compiled cache invalidation is per-language**: When entries change, only the compiled cache for the affected language is invalidated. Exports for other languages remain cached.

6. **No real-time push of localization changes**: Localization follows Pattern C (client-initiated pull), not Pattern A (server push). Clients download localization tables and cache locally. To pick up changes, clients re-fetch. Category lifecycle events are for server-side consumers (e.g., cache invalidation, asset bundle recompilation), not client notification.

### Design Considerations (Requires Planning)

1. **`localizationKeyPrefix` field addition to L2 schemas**: Adding `localizationKeyPrefix` (string, nullable) to Item, Quest, Species, Location, Seed, Collection, Realm, and Relationship template schemas requires coordinated schema changes across 8+ services. This should be batched as a single cross-cutting change after lib-localization is implemented. The field is nullable — localization is optional even when the service is loaded. At template creation, the service calls `ValidateLocalizationKeyAsync(LocalizationCategoryDefinitions.Items, prefix, null)` to verify the prefix has at least one entry in the default language.
<!-- AUDIT:NEEDS_DESIGN:2026-03-16:https://github.com/beyond-immersion/bannou-service/issues/508 -->

2. **Compiled export size at scale**: A game with 50 categories × 10,000 entries × 30 languages = 15M entries. The compiled export endpoint must handle this efficiently — likely via category-filtered exports rather than full-database dumps. Consider whether the export should be per-category rather than per-language.

3. **Localization for procedurally-generated content**: God-actor-composed quest titles and NPC dialogue use template-based localization (`"Rescue {0} from {1}"`). The parameter substitution happens at the client from localized template strings, not at the server. This means localization keys for scenario-composed content must reference templates, not fully-resolved strings. The Storyline/Quest composition pipeline needs to emit localization keys, not display text.

4. **Relationship to existing ABML `ILocalizationProvider`**: The behavior compiler has its own `ILocalizationProvider` with `FileLocalizationProvider`. lib-localization should implement this interface so ABML expressions like `${localization("KEY", locale)}` resolve against centralized localization tables instead of file-based lookups. This bridges the existing ABML pattern with the new service without breaking backward compatibility (file-based provider remains available for embedded/sidecar deployments without localization service).

5. **Machine-readable identifier format standardization**: Localization keys use `^[a-z][a-z0-9._-]*$` (lowercase, dot-separated hierarchy, kebab-case segments). This same class of constraint — "machine-readable string identifier" — appears across 6+ services with ad-hoc variations: save-load (`^[a-z][a-z0-9-]*$`), item (`^[a-z][a-z0-9_]{1,63}$`), currency (`^[a-z][a-z0-9_]{1,31}$`), contract (`^[a-z0-9_]+$`), leaderboard/achievement (`^[a-z0-9_-]+$`), website (`^[a-z0-9-]+$`). SCHEMA-RULES.md should define standardized identifier format classes (e.g., `x-string-format: slug`, `x-string-format: code`, `x-string-format: hierarchical-key`) so the constraint is mechanical based on the field's semantic role, not ad-hoc per service. This is a cross-cutting concern beyond localization — flagging for SCHEMA-RULES.md consideration.

6. **Generation script for localization-categories.yaml**: Needs a new generation script (`scripts/generate-localization-categories.sh`) that reads `schemas/localization-categories.yaml` and produces `bannou-service/Generated/LocalizationCategoryDefinitions.cs` and `docs/GENERATED-LOCALIZATION-CATEGORIES.md`. Follow the same pattern as `generate-state-stores.sh` / `StateStoreDefinitions.cs`. Also needs two structural tests in `structural-tests/StructuralTests.cs`: one validating all constants are referenced by declared consumer assemblies, and one validating all `ILocalizationKeyValidator` calls use generated constants.

---

## Work Tracking

*(No active items)*
