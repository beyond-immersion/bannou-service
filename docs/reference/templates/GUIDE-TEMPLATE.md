# Document Template: Developer Guides

> ⛔ **FROZEN DOCUMENT** — Defines an authoritative template. AI agents MUST NOT modify any content without explicit user instruction. See CLAUDE.md § "Reference Documents Are Frozen."

> This document defines the structure for developer guide documents.
> Each guide lives at: `docs/guides/{GUIDE-NAME}.md`

---

## Header Format

All guide documents MUST use this header format:

```markdown
# {Guide Title}

> **Version**: {version number}
> **Status**: {Production | Implemented | Aspirational | Draft}
> **Last Updated**: {YYYY-MM-DD}
> **Key Plugins**: {comma-separated plugin names with layer, e.g., lib-actor (L2), lib-behavior (L4)}
```

**Required fields:**
- **Version**: Semantic version reflecting content maturity (e.g., `1.0`, `2.1`)
- **Status**: Current accuracy state
  - `Production` — describes implemented, production-ready systems
  - `Implemented` — describes implemented systems not yet production-validated
  - `Aspirational` — describes planned systems not yet implemented
  - `Draft` — incomplete, work in progress
- **Last Updated**: Date of last substantive content change (YYYY-MM-DD format)
- **Key Plugins**: Which plugins this guide primarily covers. Use `lib-{name} (L{n})` format.

**Optional fields** (add after Key Plugins, in this order):
- `> **Related Guides**: [{title}]({path}), [{title}]({path})` — cross-references to related guides
- `> **Prerequisites**: [{title}]({path})` — guides that should be read first

---

## Summary Section

Immediately after the header, every guide MUST have a `## Summary` section:

```markdown
## Summary

{2-4 sentences describing what this guide covers, who should read it, and what
they will learn. Must be self-contained — a reader of only this section should
understand the guide's scope and value.}
```

**Rules:**
1. **2-4 sentences maximum** — this section is extracted by the documentation generation pipeline for `GENERATED-GUIDES-CATALOG.md`
2. Must answer: What does this guide teach? Who is the audience? What will they be able to do after reading it?
3. **No markdown links, code blocks, or formatting** — plain text only
4. **No self-references** — write as a third-person description, not "this document explains..."

**Example:**
```markdown
## Summary

Comprehensive guide to the NPC intelligence stack covering ABML compilation,
GOAP planning, the 5-stage cognition pipeline, variable providers, and behavior
document authoring. Intended for developers writing or modifying NPC behaviors.
After reading, developers will understand the full path from ABML YAML to
compiled bytecode executing in the Actor runtime.
```

---

## Document Body

After the Summary section, the guide body is **free-form**. Common sections include:

- **Overview / Architecture** — High-level system description with diagrams
- **Getting Started** — Quick start for the target audience
- **Core Concepts** — Key abstractions and patterns
- **Usage Examples** — Practical code/configuration examples
- **Integration Points** — How this system connects to others
- **Troubleshooting** — Common issues and solutions
- **Further Reading** — Links to deep dives, implementation maps, related guides

**Guidelines:**
- Use plugin deep dives (`docs/plugins/*.md`) as source of truth for plugin capabilities
- Use implementation maps (`docs/maps/*.md`) as source of truth for API details
- Keep guides cross-cutting — if content is specific to one plugin, it belongs in the deep dive
- Prefer diagrams and examples over prose
- Update `Last Updated` and `Version` when making substantive changes

---

## Maintenance Workflow

The `/maintain-guide` skill maintains these documents by:
1. Verifying the header matches this template's format
2. Verifying the Summary section exists and follows the rules above
3. Cross-referencing claims against plugin deep dives for accuracy
4. Checking that Status reflects current implementation state
5. Updating `Last Updated` when changes are made
