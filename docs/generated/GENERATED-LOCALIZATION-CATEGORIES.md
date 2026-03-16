# Generated Localization Category Reference

> **Source**: `schemas/localization-categories.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all localization categories used by Bannou services.
Categories organize translation entries by domain. Schema-defined categories
are seeded on startup and cannot be deleted via API.

## Localization Categories

| Category Code | Validation Mode | Consumers | Description |
|---------------|-----------------|-----------|-------------|
| `collections` | WarnOnMissing | `lib-collection` | Collection type display names |
| `items` | RejectOnMissing | `lib-item` | Item template display names and descriptions |
| `lexicon` | None | `lib-lexicon` | Lexicon concept display names for client rendering |
| `locations` | RejectOnMissing | `lib-location` | Location display names |
| `quests` | WarnOnMissing | `lib-quest` | Quest definition names and descriptions |
| `realms` | WarnOnMissing | `lib-realm` | Realm definition display names |
| `relationships` | WarnOnMissing | `lib-relationship` | Relationship type definition display names |
| `seeds` | WarnOnMissing | `lib-seed` | Seed type definition display names |
| `species` | WarnOnMissing | `lib-species` | Species definition display names and trait labels |
| `ui` | None | `lib-website` | General UI labels, buttons, tooltips, and system messages |

**Total**: 10 categories (2 RejectOnMissing, 6 WarnOnMissing, 2 None)

## Validation Modes

| Mode | Behavior |
|------|----------|
| `None` | Key validation always returns true (no checking) |
| `WarnOnMissing` | Returns true but logs a warning when a key doesn't exist |
| `RejectOnMissing` | Returns false when a key doesn't exist (caller gets BadRequest) |

Validation mode is configured per category and controls the behavior of
`ILocalizationKeyValidator.ValidateLocalizationKeyAsync()` when called by
consumer plugins at entity creation time.

## How Categories Work

1. **Schema**: Categories are defined in `schemas/localization-categories.yaml`
2. **Constants**: Generated to `LocalizationCategoryDefinitions` in `bannou-service/Generated/`
3. **Seeding**: Schema-defined categories are seeded on startup (idempotent, cannot be deleted via API)
4. **Validation**: Consumer plugins call `ILocalizationKeyValidator` with generated constants
5. **Runtime**: The API also supports runtime category CRUD for L5 extensions

## Generated Code

Localization category definitions are generated to `bannou-service/Generated/LocalizationCategoryDefinitions.cs`,
providing:

- **Code constants**: `LocalizationCategoryDefinitions.Items`, `LocalizationCategoryDefinitions.Quests`, etc.
- **Metadata**: `LocalizationCategoryDefinitions.Metadata` with description, validation mode, and consumers

## Structural Enforcement

Two structural tests validate the localization category registry:

| Test | What It Validates |
|------|-------------------|
| `LocalizationCategories_AreAllReferenced` | Every constant has at least one declared consumer in the schema |
| `LocalizationValidator_UsesGeneratedConstants` | Every `ValidateLocalizationKeyAsync` call uses a `LocalizationCategoryDefinitions` constant |

---

*This file is auto-generated. See [TENETS.md](../reference/TENETS.md) for architectural context.*
