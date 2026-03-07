# Plugin Deep Dive Template

> ⛔ **FROZEN DOCUMENT** — Defines an authoritative template. AI agents MUST NOT modify any content without explicit user instruction. See CLAUDE.md § "Reference Documents Are Frozen."

> This document defines the structure for per-plugin deep-dive documents.
> Each plugin gets one document: `docs/plugins/{service-name}.md`

---

## Process Instructions

When building a deep dive for a plugin, follow this process:

1. **Read all source code thoroughly**: Read the service implementation, helper services, interfaces, models, events, configuration, and tests. Do not skim - read every method body, every branch, every error handler.

2. **Build the document structure**: Fill in sections 1-7 based on verifiable source code. Every statement must trace back to a specific line of code. If an implementation map exists for this plugin, operational sections (dependencies, state, events, DI services, endpoint behavior) belong in the map -- do not duplicate them here.

3. **Compile a comprehensive quirks list**: After understanding the full codebase, identify every non-obvious behavior, potential issue, and design decision. Be thorough - it is better to flag something that turns out to be intentional than to miss a real bug. Look for:
   - Encoding mismatches, type confusion, off-by-one errors
   - Dead code, unused parameters, unused stores
   - Missing cleanup (orphaned state, leaked indexes)
   - Inconsistent error handling (swallowed exceptions, wrong status codes)
   - Race conditions, missing synchronization, stale data propagation
   - Missing validation, unchecked nulls, silent failures
   - Naming confusion (field names that don't match semantics)
   - Hardcoded values that should be configurable
   - Backward compatibility concerns for new fields

4. **Categorize each quirk** into one of three sections:
   - **Bugs (Fix Immediately)**: Clear defects that will cause incorrect behavior, data corruption, crashes, or security issues in production. These have unambiguous fixes.
   - **Intentional Quirks (Documented Behavior)**: Non-obvious behaviors that are deliberate design decisions. They may surprise developers but are correct. These stay as permanent documentation.
   - **Design Considerations (Requires Planning)**: Issues that are likely bugs or missing implementation, but where the fix involves architectural decisions, cross-service coordination, or trade-offs that need discussion before proceeding.

5. **Verify with a build**: After identifying bugs, confirm the document's accuracy by checking that identified code patterns actually exist. Cross-reference with tests to understand intended behavior vs. actual behavior.

---

## Document Structure

### 1. Header

Two standard header formats exist depending on implementation status. All deep dive documents MUST use one of these two patterns exactly. Do not invent custom header fields (`Depends On`, `Hard Dependencies`, `Soft Dependencies`, `Referenced By`, `Service`, etc.) -- dependencies belong in the implementation map.

#### Implemented Plugin Header

For plugins that have a schema and generated code (whether fully implemented, partially stubbed, or pre-implementation with a schema):

```markdown
# {Service Name} Plugin Deep Dive

> **Plugin**: lib-{service}
> **Schema**: schemas/{service}-api.yaml
> **Version**: {version from schema}
> **Layer**: {Infrastructure / AppFoundation / GameFoundation / AppFeatures / GameFeatures}
> **State Store**: {store name} ({backend})
```

Optional additional fields (add after State Store, in this order):
- `> **Implementation Map**: [docs/maps/{SERVICE-NAME}.md](../maps/{SERVICE-NAME}.md)` — link to the implementation map if one exists
- `> **Status**: Pre-implementation (architectural specification)` — for plugins with schemas but no service logic yet
- `> **Planning**: [{doc}]({path})` — link to a planning/design document if one exists
- `> **Guide**: [{doc}]({path})` — link to a cross-service integration guide if one exists

#### Aspirational Plugin Header

For plugins with NO schema and NO code (pure design specifications):

```markdown
# {Service Name} Plugin Deep Dive

> **Plugin**: lib-{service} (not yet created)
> **Schema**: `schemas/{service}-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: {planned stores} — all planned
> **Layer**: {Infrastructure / AppFoundation / GameFoundation / AppFeatures / GameFeatures}
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
```

The `Layer` field is mandatory in both header formats. It provides at-a-glance layer identification and is used by the doc generation pipeline to split services into per-layer reference documents.

**Common header mistakes to avoid:**
- `# {Name} Service (lib-{name})` — use `# {Name} Plugin Deep Dive`
- `# {Name} Service Deep Dive` — use `Plugin`, not `Service`
- `> **Service**: lib-{name}` — use `**Plugin**`, not `**Service**`
- Listing dependencies in the header — they belong in the implementation map

---

### 2. Overview

A concise description of what the plugin does, its role in the system, and its access scope (internal-only, internet-facing, etc). This should answer: "Why does this plugin exist and what problem does it solve?" in 2-4 sentences.

---

### 3. Dependents (What Relies On This Plugin)

A table of other plugins that consume this plugin's data or events.

```markdown
| Dependent | Relationship |
|-----------|-------------|
| lib-{other} | Calls {endpoint} via I{Service}Client for {purpose} |
| lib-{other} | Subscribes to `{topic}` event to {reaction} |
```

---

### 4. Configuration

Full list of configuration properties from the configuration schema, with environment variable names, defaults, and what each controls in the service logic.

```markdown
| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `{Name}` | `{SERVICE}_{PROPERTY}` | {value or "required"} | {What it controls} |
```

---

### 5. Visual Aid

One ASCII or Mermaid diagram per document that illustrates something **not already obvious from the tables above**. The Dependencies/Dependents tables already show what connects to what, so don't repeat that.

Good candidates for the visual:
- **State store key relationships**: How keys reference each other, what gets cleaned up on delete vs. what's orphaned
- **Multi-step operation flow**: How a complex operation (e.g., registration) flows through multiple internal steps
- **Internal data model structure**: How model fields relate across different key patterns in the same store

Do **not** create a diagram that just draws arrows between this plugin, its dependents, and its dependencies - that's what the Dependents table and implementation map already say.

---

### 6. Stubs & Unimplemented Features

Things that have scaffolding, configuration, or partial code but are not yet functional. Each entry should note what exists and what's missing to complete it.

---

### 7. Potential Extensions

Technical observations about ways the plugin could be improved or extended. No stubs exist for these - they are forward-looking ideas. Technical description only (no priority/effort estimates).

---

### Service-Specific Sections (Optional)

Plugins may include additional `##` sections for service-specific content that doesn't fit the standard template structure. These sections can be placed in **two positions** depending on their role:

**Position A: Between Overview (2) and Dependents (3)** — for foundational architectural context that the rest of the document builds on. Use this when the section explains a core design principle, privacy model, protocol, or subsystem that readers need to understand before the standard sections make sense. Examples:

- **Privacy Boundary**: Data flow and PII handling model (e.g., lib-stream's sentiment anonymization)
- **Core Subsystem Design**: A subsystem architecture that drives the rest of the plugin (e.g., lib-streaming's simulated audience system)
- **Protocol Details**: Wire protocol or binary format that the plugin implements

**Position B: Between Potential Extensions (7) and Known Quirks (8)** — for supplementary content that adds detail but isn't prerequisite context. Examples:

- **Duration/Format Reference**: ISO 8601 duration formats, encoding details, data format specifications
- **License Compliance**: Third-party dependency licensing analysis
- **Integration Guides**: How external systems interact with this plugin
- **Composability Maps**: How this plugin composes other Bannou primitives

**Rules for extra sections**:
1. Use `##` headers (same level as other numbered sections)
2. Must NOT duplicate content that belongs in standard sections above
3. Content must be specific to this plugin's implementation, not general patterns
4. Do not add extra sections to the overview/summary (that gets copied to GENERATED-SERVICE-DETAILS.md)
5. Position A sections should be limited to 1-3 sections maximum — if you need more, some belong in Position B

---

### 8. Known Quirks & Caveats

Organized into three categories based on the nature and urgency of each finding.

#### Bugs (Fix Immediately)

Clear defects that will cause incorrect behavior, data corruption, crashes, or security issues in production. These have unambiguous fixes that can be implemented without design discussion.

#### Intentional Quirks (Documented Behavior)

Non-obvious behaviors that are deliberate design decisions. They may surprise developers but are correct as implemented. These serve as permanent documentation to prevent "fixing" things that aren't broken.

#### Design Considerations (Requires Planning)

Issues that are likely bugs, missing implementation, or design oversights, but where the fix involves architectural trade-offs, cross-service coordination, or decisions that need discussion before proceeding. Each entry should note what the concern is and what makes the fix non-trivial.

---

### 9. Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

#### Marker Format

Work tracking uses HTML comment markers placed immediately after the item being tracked:

```markdown
- Some bug that needs fixing
  <!-- AUDIT:IN_PROGRESS:2026-01-29 -->

- Design issue that needs human decisions
  <!-- AUDIT:NEEDS_DESIGN:2026-01-28:https://github.com/org/repo/issues/42 -->

- Item blocked on external dependency
  <!-- AUDIT:BLOCKED:2026-01-27:https://github.com/org/repo/issues/41 -->
```

**Marker statuses:**
| Status | Meaning | Issue Link |
|--------|---------|------------|
| `IN_PROGRESS` | Currently being worked on by automation or developer | Optional |
| `NEEDS_DESIGN` | Investigated but requires human design decisions | **Required** |
| `BLOCKED` | Waiting on external dependency or other issue | Optional |

**Important**: These markers are machine-managed. Do not remove or modify them during document maintenance - the `/audit-plugin` workflow manages their lifecycle.

---

## Rules

1. **One visual aid maximum** per document unless the plugin genuinely has two independent subsystems that cannot be combined into one readable diagram. The visual must add information not already present in the tables.
2. **No speculative information** - every statement must be verified against the source code.
3. **Keep it scannable** - tables over prose where possible, prose only where context requires narrative explanation.
4. **Configuration must be complete** - every property in the configuration schema must appear in the configuration table.
5. **Preserve AUDIT markers** - Never remove or modify `<!-- AUDIT:... -->` markers during document maintenance. These are managed by the `/audit-plugin` workflow and track active development work.
6. **Cross-reference implementation map** - If an implementation map exists at `docs/maps/{SERVICE-NAME}.md`, the header must include the `Implementation Map` field linking to it. Operational sections (dependencies, state storage, events, DI services, endpoint behavior) belong in the map, not in the deep dive.
7. **Incremental migration** - Existing deep dives keep their operational sections until an implementation map is created. When a map is created, follow the migration checklist below to move operational content without losing information.

---

## Migration Checklist (Deep Dive → Map)

When creating an implementation map for a plugin that already has a deep dive, follow these steps in order to avoid information loss:

### Step 1: Create the map
Build `docs/maps/{SERVICE-NAME}.md` following the [Implementation Map Template](IMPLEMENTATION-MAP-TEMPLATE.md). Populate all 10 sections from source code.

### Step 2: Identify what moves
These deep dive sections move to the map (their content is now covered by map sections):

| Deep Dive Section (old) | Map Section (new) |
|-------------------------|-------------------|
| Dependencies | § 4. Dependencies |
| State Storage | § 3. State |
| Events (Published/Consumed) | § 5-6. Events Published/Consumed |
| DI Services & Helpers | § 7. DI Services |
| API Endpoints (Implementation Notes) | § 8-9. Method Index + Methods |

### Step 3: Preserve what stays
Before removing any deep dive section, check for content that belongs in the deep dive even after migration:

- **Visual aids**: Diagrams that show internal data model relationships, multi-step operation flows, or state store key patterns belong in the deep dive's Visual Aid section (§ 5), NOT in the map. If a diagram was embedded in an operational section (e.g., a key relationship diagram inside the State Storage section), move it to the Visual Aid section before removing the operational section.
- **Architectural context notes**: Notes about design decisions, privacy exceptions, tenet compliance rationale, or "why" explanations belong in the deep dive's Overview or as custom sections (Position A). If such notes lived inside an operational section, relocate them before removing the section.
- **Special behavioral notes**: Quirks-level observations discovered while documenting operational sections (e.g., "PasswordHash intentionally included in by-email response only") should already be in Known Quirks. Verify they are before removing the operational section.

### Step 4: Remove and cross-reference
1. Add the `Implementation Map` header field to the deep dive
2. Remove the operational sections identified in Step 2
3. Renumber remaining sections to match this template
4. Verify no content was lost by comparing the old deep dive (via git diff) against the map + updated deep dive

### Common Information Loss Patterns

| What Gets Lost | Why | Prevention |
|----------------|-----|------------|
| Visual aids embedded in operational sections | Entire section removed without checking for diagrams | Step 3: Move diagrams to Visual Aid section first |
| Architectural context notes in Dependencies | "Privacy exception" or "leaf node" notes removed with section | Step 3: Move to Overview or custom Position A section |
| Endpoint behavior quirks in API Endpoints | Notes about non-obvious behavior removed with section | Step 3: Verify all quirks are in Known Quirks section |
| Model field details in State Storage | Key relationship notes removed with section | Step 3: Move structural details to Visual Aid section |
