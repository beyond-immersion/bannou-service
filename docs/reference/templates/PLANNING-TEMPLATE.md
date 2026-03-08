# Document Template: Planning Documents

> ⛔ **FROZEN DOCUMENT** — Defines an authoritative template. AI agents MUST NOT modify any content without explicit user instruction. See CLAUDE.md § "Reference Documents Are Frozen."

> This document defines the structure for planning, design, research, and implementation plan documents.
> Each document lives at: `docs/planning/{DOCUMENT-NAME}.md`

---

## Header Format

All planning documents MUST use this header format:

```markdown
# {Document Title}

> **Type**: {Vision Document | Design | Research | Implementation Plan | Architectural Analysis}
> **Status**: {Active | Implemented | Superseded | Aspirational | Draft}
> **Created**: {YYYY-MM-DD}
> **Last Updated**: {YYYY-MM-DD}
> **North Stars**: {#1, #2, etc. from VISION.md, or N/A}
> **Related Plugins**: {comma-separated plugin names, e.g., Actor, Seed, Divine}
```

**Required fields:**
- **Type**: What kind of planning document this is
  - `Vision Document` — describes what the future should look like, no implementation details
  - `Design` — proposes a specific design with architectural decisions
  - `Research` — compiles external research, precedent analysis, or formal theory
  - `Implementation Plan` — phased plan for building something specific (formerly in `docs/plans/`)
  - `Architectural Analysis` — analyzes existing architecture for gaps or opportunities
- **Status**: Current state
  - `Active` — actively informing design/development decisions
  - `Implemented` — the described system has been built (link to deep dive/map)
  - `Superseded` — replaced by a newer document (link to replacement)
  - `Aspirational` — describes something we want but haven't started
  - `Draft` — incomplete, work in progress
- **Created**: When the document was first written (YYYY-MM-DD)
- **Last Updated**: Date of last substantive content change (YYYY-MM-DD)
- **North Stars**: Which of the five North Stars from VISION.md this serves. Use `#1` through `#5` or `N/A` if infrastructure/tooling.
- **Related Plugins**: Which plugins this document is relevant to. Just names, no `lib-` prefix.

**Optional fields** (add after Related Plugins, in this order):
- `> **Superseded By**: [{title}]({path})` — if Status is Superseded
- `> **Parent Plan**: [{title}]({path})` — for phased plans that are part of a larger plan
- `> **Prerequisites**: {description}` — what must exist before this can be implemented

---

## Summary Section

Immediately after the header, every planning document MUST have a `## Summary` section:

```markdown
## Summary

{2-4 sentences describing the core idea, what problem it solves, and its current
state. Must be self-contained — a reader of only this section should understand
whether this document is relevant to their work.}
```

**Rules:**
1. **2-4 sentences maximum** — this section is extracted by the documentation generation pipeline for `GENERATED-PLANNING-CATALOG.md`
2. Must answer: What is the core idea? What problem does it solve? What is its current state?
3. **No markdown links, code blocks, or formatting** — plain text only
4. **No self-references** — write as a third-person description

**Example:**
```markdown
## Summary

Defines the unified three-stage cognitive progression (Dormant, Stirring,
Awakened) for any entity that grows from inert object to autonomous agent.
Covers gods, dungeons, weapons, and guardian spirits using system realms and the
Variable Provider Factory pattern. Validates the architecture with living weapons
as a zero-plugin proof case. No implementation exists yet; this is the
architectural specification that individual plugin deep dives reference.
```

---

## Document Body

After the Summary section, the document body varies by Type:

### Vision Documents & Design Documents
- **Problem Statement** — What gap or opportunity this addresses
- **Proposed Solution** — The design, with diagrams
- **Service Composition** — Which existing services compose to deliver this
- **Open Questions** — Unresolved design decisions
- **Related Documents** — Cross-references

### Research Documents
- **Research Question** — What we're investigating
- **Sources** — Papers, games, precedents analyzed
- **Findings** — Structured analysis results
- **Implications for Bannou** — How findings map to our architecture

### Implementation Plans
- **Context** — What is being built and why
- **Implementation Steps** — Numbered phases with specific files, schemas, and commands
- **Files Created/Modified** — Summary of all changes
- Follow [PLAN-EXAMPLE.md](PLAN-EXAMPLE.md) for the established format

**Guidelines:**
- Use plugin deep dives as source of truth for current plugin capabilities
- Planning documents describe the FUTURE; deep dives describe the PRESENT
- When a planning document's vision is implemented, update Status to `Implemented`
- Update `Last Updated` when making substantive changes

---

## Maintenance Workflow

The `/maintain-planning-doc` skill maintains these documents by:
1. Verifying the header matches this template's format
2. Verifying the Summary section exists and follows the rules above
3. Checking if Status should change (e.g., schemas/code now exist for `Aspirational` docs)
4. Cross-referencing claims against plugin deep dives for accuracy
5. Checking North Star references are still valid
6. Updating `Last Updated` when changes are made
