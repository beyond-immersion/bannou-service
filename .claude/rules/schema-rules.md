---
globs: schemas/**
---

# Schema Work — Mandatory Reference

You are working in the `schemas/` directory. Before creating or modifying ANY schema file, you MUST read `docs/reference/SCHEMA-RULES.md`. This is inviolable (Tenet 1).

SCHEMA-RULES.md covers:
- Schema file types and naming conventions
- Generation pipeline and script selection
- Extension attributes (x-permissions, x-lifecycle, x-event-subscriptions, x-event-publications, x-references)
- NRT (Nullable Reference Types) compliance rules
- Schema reference hierarchy ($ref rules — which files can reference which)
- Configuration validation keywords
- Topic naming conventions (Pattern A vs Pattern C, forbidden Pattern B)
- Enum value formatting (PascalCase only)
- Parameterized topics (topic-params)

## Quick Reminders

- **All enum values**: PascalCase (`TwoParty`, not `two_party` or `TWO_PARTY`)
- **All properties**: MUST have `description` fields (causes CS1591 if missing)
- **$ref direction**: API schemas are source of truth. Events $ref INTO api schemas, never the reverse.
- **Topic naming**: Use Pattern A (`entity.action`) for single-entity services, Pattern C (`service.entity.action`) for multi-entity. Pattern B (service name embedded via hyphens in entity) is FORBIDDEN.
- **After schema changes**: Use the `generate` MCP tool — run `generate()` with no args to see all available generators with triggers and ordering rules. Always use the most granular generator possible.
- **Never edit `*/Generated/` files** — fix the schema and regenerate.
