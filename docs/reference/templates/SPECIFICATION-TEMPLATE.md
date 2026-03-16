# Document Template: Extension Attribute Specifications

> ⛔ **FROZEN DOCUMENT** — Defines an authoritative template. AI agents MUST NOT modify any content without explicit user instruction. See CLAUDE.md § "Reference Documents Are Frozen."

> This document defines the structure for extension attribute specification documents.
> Each specification lives at: `docs/reference/specifications/X-{ATTRIBUTE-NAME}.md`

---

## Header Format

All specification documents MUST use this header format:

```markdown
# x-{attribute-name}

> **Version**: {version number}
> **Status**: {Implemented | Draft | Proposed}
> **Last Updated**: {YYYY-MM-DD}
> **Schema Scope**: {which schema types use this attribute, e.g., *-configuration.yaml}
> **Generated Output**: {brief description of what the generator produces}
```

**Required fields:**
- **Version**: Semantic version reflecting specification maturity (e.g., `1.0`, `0.1`)
- **Status**: Current implementation state
  - `Implemented` — generator, runtime validation, and structural tests all exist
  - `Draft` — specification is complete but implementation is in progress
  - `Proposed` — specification is under review, not yet implemented
- **Last Updated**: Date of last substantive content change (YYYY-MM-DD format)
- **Schema Scope**: Which schema file types this attribute applies to (e.g., `*-configuration.yaml`, `*-api.yaml`, `*-events.yaml`)
- **Generated Output**: One-line summary of what the code generator produces from this attribute

**Optional fields** (add after Generated Output, in this order):
- `> **Related Specifications**: [x-{name}]({path}), [x-{name}]({path})` — cross-references to related specs
- `> **Depends On**: [x-{name}]({path})` — specs that must be understood first
- `> **Tenet References**: T{n} ({category})` — tenets this specification implements or enforces

---

## Summary Section

Immediately after the header, every specification MUST have a `## Summary` section:

```markdown
## Summary

{2-4 sentences describing what this extension attribute does, what problem it
solves, and when developers should use it. Must be self-contained — a reader of
only this section should understand whether this specification is relevant to
their work.}
```

**Rules:**
1. **2-4 sentences maximum** — this section is extracted by the documentation generation pipeline for `generated/GENERATED-SPECIFICATIONS-CATALOG.md`
2. Must answer: What does this attribute do? What problem does it solve? When should a developer use it?
3. **No markdown links, code blocks, or formatting** — plain text only
4. **No self-references** — write as a third-person description

**Example:**
```markdown
## Summary

Declares cross-property validation constraints for configuration schemas,
enabling groups of properties to be validated collectively at startup. Solves
the gap where individual property validation keywords cannot express
relationships between properties, such as weights that must sum to 1.0 or
mutually exclusive provider selections. Use when configuration properties have
collective invariants that cannot be expressed per-property.
```

---

## Document Body

After the Summary section, the specification body MUST include these sections in order:

### Schema Syntax (REQUIRED)

Canonical YAML examples showing all valid forms of the attribute. Include both minimal and full examples. Use realistic service names and property names.

### Field Reference (REQUIRED)

Table of all fields/values the attribute accepts, with types, required/optional status, defaults, and descriptions. For attributes with enumerated constraint or mode types, include a sub-table for each valid value.

### Generated Output (REQUIRED)

Detailed description of what the code generator produces from this attribute:
- File paths and class names
- C# attribute signatures
- Method signatures
- How generated code integrates with existing infrastructure

Include actual C# code examples of generated output.

### Runtime Behavior (REQUIRED)

What happens when the generated code executes:
- Startup validation behavior (fail-fast semantics, error message format)
- Request-time behavior (if applicable)
- Edge cases (null values, missing properties, type mismatches)

### Structural Tests (REQUIRED)

Table of structural tests that enforce this specification, with test name and what each validates. These tests live in `structural-tests/` and validate schema correctness at build time.

### Examples (REQUIRED)

At least two complete worked examples showing the attribute in realistic service configurations. Include both the schema YAML and the generated C# output.

### Edge Cases & Restrictions (REQUIRED)

Explicitly document:
- What is forbidden (invalid combinations, unsupported types)
- Scoping rules (does the attribute cross file boundaries?)
- Interaction with other extension attributes
- Known limitations

---

## Guidelines

- Specifications define **declarative contracts** — schema syntax in, generated code + runtime behavior out
- Keep the specification self-contained — a developer should be able to implement from the spec alone
- Use plugin deep dives and implementation maps as source of truth for service-specific examples
- When a specification changes, update the Version and Last Updated fields
- Specifications are **normative** — structural tests enforce them; violations are bugs

---

## File Naming Convention

Specification files use the extension attribute name: `X-{ATTRIBUTE-NAME}.md` (uppercase, matching the `x-` prefix convention).

**Examples:**
- `X-CONSTRAINT-GROUP.md`
- `X-REFERENCES.md`
- `X-LIFECYCLE.md`
- `X-PERMISSIONS.md`
- `X-EVENT-PUBLICATIONS.md`

---

## Maintenance Workflow

The `/maintain-specification` skill maintains these documents by:
1. Verifying the header matches this template's format
2. Verifying the Summary section exists and follows the rules above
3. Cross-referencing schema syntax against actual generator behavior
4. Checking that structural tests listed in the spec exist in `structural-tests/`
5. Verifying generated output examples match actual generator output
6. Updating `Last Updated` when changes are made
