# Document Template: Operations Documents

> ⛔ **FROZEN DOCUMENT** — Defines an authoritative template. AI agents MUST NOT modify any content without explicit user instruction. See CLAUDE.md § "Reference Documents Are Frozen."

> This document defines the structure for operations, deployment, testing, and CI/CD documents.
> Each document lives at: `docs/operations/{DOCUMENT-NAME}.md`

---

## Header Format

All operations documents MUST use this header format:

```markdown
# {Document Title}

> **Last Updated**: {YYYY-MM-DD}
> **Scope**: {Brief description of what this document covers}
```

**Required fields:**
- **Last Updated**: Date of last substantive content change (YYYY-MM-DD format)
- **Scope**: One-line description of the document's coverage area

---

## Summary Section

Immediately after the header, every operations document MUST have a `## Summary` section:

```markdown
## Summary

{2-4 sentences describing what this document covers, who should read it, and
when to reference it. Must be self-contained — a reader of only this section
should understand whether this document is relevant to their task.}
```

**Rules:**
1. **2-4 sentences maximum** — this section is extracted by the documentation generation pipeline for `GENERATED-OPERATIONS-CATALOG.md`
2. Must answer: What operational concern does this cover? Who needs this? When should they read it?
3. **No markdown links, code blocks, or formatting** — plain text only
4. **No self-references** — write as a third-person description

**Example:**
```markdown
## Summary

Three-tier testing architecture defining plugin isolation boundaries, test
placement decision guide, and CI/CD pipeline integration. Required reading
before writing, modifying, or debugging any tests. Covers unit tests
(lib-*.tests), HTTP integration tests (http-tester), and WebSocket edge tests
(edge-tester) with their respective project reference constraints.
```

---

## Document Body

After the Summary section, the document body is **free-form** but typically includes:

- **Architecture / Overview** — How the operational system works
- **Commands / Procedures** — Step-by-step instructions with exact commands
- **Configuration** — Required environment variables, file locations
- **Troubleshooting** — Common failures and their fixes
- **CI/CD Integration** — How this relates to automated pipelines

**Guidelines:**
- Operations documents describe **procedures**, not architecture — keep them actionable
- Include exact commands that can be copied and run
- Reference the Makefile for established command patterns
- Update `Last Updated` when commands, procedures, or configurations change

---

## Maintenance Workflow

The `/maintain-operations-doc` skill maintains these documents by:
1. Verifying the header matches this template's format
2. Verifying the Summary section exists and follows the rules above
3. Checking that referenced commands still exist in the Makefile
4. Verifying referenced file paths still exist
5. Checking that CI/CD references match current `.github/workflows/` configuration
6. Updating `Last Updated` when changes are made
