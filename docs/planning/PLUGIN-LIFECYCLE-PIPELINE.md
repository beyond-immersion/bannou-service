# Plugin Lifecycle Pipeline: From Idea to Production-Ready

> **Type**: Design
> **Status**: Implemented
> **Created**: 2026-03-05
> **Last Updated**: 2026-03-09
> **North Stars**: #4
> **Related Plugins**: N/A

## Summary

Formalizes the end-to-end development lifecycle for Bannou plugins across seven stages, from deep dive concept through production-ready implementation. Defines readiness levels (L0-L7), the skill commands that drive progression, and ordering constraints that prevent architectural rework. All seven stages now have corresponding skill commands or established manual processes.

---

## The Core Finding

Bannou's existing documentation and skill infrastructure already covers most of the plugin development lifecycle, but the stages aren't formalized into a pipeline with clear readiness gates. This document defines that pipeline.

**The key insight**: TDD and schema-first development are not alternatives -- they are sequential phases that serve different purposes. The implementation map IS the test specification, but tests can't be written until schemas produce generated C# types. The pipeline is:

```
Deep Dive --> Implementation Map --> Pre-Implementation Audit
    --> Schema Creation --> Code Generation --> Tests --> Implementation
```

Each stage has a clear input, output, and quality gate. Existing skills handle several stages; new skills are needed for the rest.

---

## The Seven Stages

### Stage 1: Deep Dive (Concept & Architecture)

**Input**: Service concept, vision alignment, layer classification.
**Output**: `docs/plugins/{SERVICE}.md` -- comprehensive architectural specification.
**Gate**: All sections populated; dependencies classified by layer/hardness; state stores designed; events defined; type fields classified per IMPLEMENTATION TENETS decision tree; known quirks documented.

**What it answers**:
- What does this service DO? (overview, architecture)
- What does it DEPEND on? (hard/soft, by layer)
- What DEPENDS on it? (consumers, variable providers)
- What STATE does it manage? (store layout, key patterns, backends)
- What EVENTS does it publish/consume?
- What are the KNOWN QUIRKS? (intentional behaviors, edge cases)
- What DESIGN QUESTIONS remain open?

**Existing skill**: `/maintain-plugin` -- creates and updates deep dives, ensures structure matches template, preserves work tracking markers.

**Quality standard**: Account's deep dive (111 lines, zero stubs, zero open design questions, 10 documented intentional quirks). A deep dive at this quality level leaves no architectural ambiguity.

---

### Stage 2: Pre-Implementation Audit

**Input**: Deep dive document.
**Output**: Audit findings added to deep dive's Known Quirks section; GitHub issues for genuine design questions; inline fixes for simple tenet violations.

**Gate**: All Critical findings resolved (either fixed in deep dive or tracked as blocking issues). Warning-level findings documented for schema creation phase.

**What it catches**:
- Tenet violations in the design (missing x-lifecycle, x-permissions, wrong event patterns)
- Hierarchy violations (dependency direction, communication mechanism choice)
- Type safety issues (wrong polymorphic classification, missing enums)
- Multi-instance safety concerns (in-memory state, non-distributed debouncing)
- Missing lib-resource integration for dependent data
- Schema rule violations (naming, validation keywords, reference hierarchy)

**Existing skill**: `/audit-plugin` -- audits deep dives for implementation gaps, executes fixes inline, creates issues for genuine design questions.

**Quality standard**: Agency's audit found 21 findings before any schema existed. All Critical findings (5) must be resolved before proceeding to schemas. This prevented 5 implementation bugs that would have been expensive to fix after code generation.

**Evidence this stage works**: Agency's audit caught a hierarchy violation (Actor L2 publishing to Agency L4 namespace), missing x-lifecycle events on 3 entity types, and missing lib-resource integration -- all things that would have become schema errors or implementation bugs.

---

### Stage 3: Implementation Map

**Input**: Deep dive (post-audit), schemas (if they exist).
**Output**: `docs/maps/{SERVICE}.md` -- pseudocode-level specification for every endpoint.

**Gate**: Every endpoint has pseudocode showing exact state operations, lock acquisitions, condition checks, status code returns, and event publications.

**What it contains**:
- Summary table (plugin, layer, endpoints, stores, events published/consumed, client events, background services)
- State store layout (key patterns, data types, purpose)
- Dependency table (service, layer, type, usage)
- Events published and consumed (topic, type, trigger)
- DI services table
- Method index (route, roles, mutations, publications)
- Per-method pseudocode with exact state operations and control flow

**Existing skill**: `/map-plugin` -- creates implementation maps from existing code, deep dives, and schemas.

**Quality standard**: Account's implementation map (408 lines, 18 endpoints fully specified). Each method shows exact key patterns, lock scopes, ETag operations, index management, and event payloads. Someone could implement the service from the map alone.

**Ordering note**: For EXISTING services, the map is created FROM code + schemas (documenting what exists). For NEW services, the map is created FROM the deep dive (specifying what should be built). The map skill needs to handle both cases.

---

### Stage 4: Schema Creation

**Input**: Deep dive (post-audit) + implementation map.
**Output**: `schemas/{service}-api.yaml`, `{service}-events.yaml`, `{service}-configuration.yaml`, `{service}-client-events.yaml` (if applicable), state store entries in `schemas/state-stores.yaml`.

**Gate**: All schemas pass validation; `x-service-layer`, `x-permissions`, `x-lifecycle`, `x-event-subscriptions`, `x-references` all present; SCHEMA-RULES.md compliance verified.

**What it produces**:
- API schema with all endpoints, request/response models, validation keywords
- Events schema with lifecycle events (x-lifecycle) and custom events
- Configuration schema with all properties, defaults, env var keys, validation
- Client events schema (if service pushes to WebSocket clients)
- State store definitions in the shared schema

**No existing skill**: This is currently manual work guided by SCHEMA-RULES.md. A `/schema-plugin` skill could be created to generate initial schema scaffolding from the deep dive + implementation map, but schema creation involves enough judgment calls (validation keywords, type classifications, reference hierarchy) that full automation may not be desirable. **Recommendation**: Keep schema creation manual for now; consider a skill later if patterns stabilize.

**Critical constraint**: Schema-First Development is inviolable. Schemas MUST exist before any generated code, tests, or implementation. This is the hard gate between "design" and "code."

---

### Stage 5: Code Generation

**Input**: Schemas.
**Output**: Generated controllers, interfaces, models, clients, configuration classes, event models.

**Gate**: `make generate` succeeds; `dotnet build` succeeds with generated code.

**What it produces**:
- `plugins/lib-{service}/Generated/` -- controller, interface, configuration, permission registration
- `bannou-service/Generated/Models/` -- request/response models
- `bannou-service/Generated/Clients/` -- service client for mesh calls
- `bannou-service/Generated/Events/` -- event models

**Existing command**: `cd scripts && ./generate-service.sh {service}` (or granular scripts per schema type).

**No skill needed**: This is a make/script command, not a skill. The skill pipeline should call generation commands when appropriate.

---

### Stage 6: Test Writing (TDD Phase)

**Input**: Generated code (interfaces, models) + implementation map (test specification).
**Output**: `plugins/lib-{service}.tests/` -- unit tests for all endpoints.

**Gate**: All tests compile; all tests FAIL (because implementation returns NotImplemented or doesn't exist yet). This is the TDD "red" phase.

**What it produces**:
- Constructor validation test (ServiceConstructorValidator)
- Per-endpoint tests derived from the implementation map's pseudocode
- State store capture tests (verifying saved data matches map expectations)
- Event capture tests (verifying published events match map expectations)
- Error path tests (verifying status codes for each failure condition in the map)

**Existing skill**: `/test-plugin` -- bridges the implementation map (specification) with unit tests (verification). The skill:
1. Read the implementation map
2. Read the generated interface (method signatures, models)
3. Read TESTING-PATTERNS.md for required patterns (Capture Pattern, ServiceConstructorValidator)
4. Generate unit tests that assert the map's specified behavior
5. Verify tests compile but fail (red phase)

**Quality standard**: Tests should follow the Capture Pattern (never Verify-only), test size limits (15/40/30/60 lines), and the Unit Test Scope Decision Tree from TESTING-PATTERNS.md.

**Critical constraint**: Tests are written FROM the implementation map, not invented. Every assertion should trace to a specific line in the map's pseudocode.

---

### Stage 7: Implementation

**Input**: Generated code + failing tests + implementation map.
**Output**: `{Service}Service.cs`, `{Service}ServiceModels.cs`, `{Service}ServiceEvents.cs`, helper services in `Services/`.

**Gate**: All tests pass; `dotnet build` succeeds; implementation map updated with any edge cases discovered.

**What it produces**:
- Business logic implementation matching the map's pseudocode
- Internal models (ServiceModels.cs) for state store entities
- Event handler implementations (ServiceEvents.cs) if consuming events
- Helper services (Services/*.cs) for complex decomposition

**Existing skill**: `/implement-plugin` -- implements service logic from the map and failing tests. The skill:
1. Read the implementation map (the specification)
2. Read failing tests (the acceptance criteria)
3. Read generated interfaces and models
4. Implement each method to pass its tests
5. Verify all tests pass
6. Flag any deviations from the map for review

**Quality standard**: Implementation must follow all tenets (infrastructure libs, service pattern, error handling, return pattern, multi-instance safety, logging, XML docs, async pattern, type safety, no sentinel values, telemetry spans).

**Edge case handling**: When implementation discovers something the map didn't anticipate (race condition, Redis behavior nuance, ETag failure path), the implementation should:
1. Implement the fix
2. Add a test for the discovered edge case
3. Update the implementation map to reflect reality
4. Add to the deep dive's Known Quirks if architecturally significant

---

## Existing Skills vs New Skills

| Stage | Skill | Status |
|-------|-------|--------|
| 1. Deep Dive | `/maintain-plugin` | EXISTS |
| 2. Pre-Implementation Audit | `/audit-plugin` | EXISTS |
| 3. Implementation Map | `/map-plugin` | EXISTS |
| 4. Schema Creation | (manual, guided by SCHEMA-RULES.md) | Manual |
| 5. Code Generation | (make/script commands) | Manual |
| 6. Test Writing | `/test-plugin` | EXISTS |
| 7. Implementation | `/implement-plugin` | EXISTS |
| **Orchestrator** | `/check-plugin` | EXISTS |

### `/check-plugin` (Orchestrator Skill)

**Purpose**: Determines the current readiness level of a plugin and recommends the next action.

**What it checks**:
1. Does the deep dive exist? Is it complete (no stubs, no unresolved design questions)?
2. Has the audit been run? Are all Critical findings resolved?
3. Does the implementation map exist? Is it complete (every endpoint has pseudocode)?
4. Do schemas exist? Do they pass validation?
5. Has code been generated? Do generated files exist?
6. Do tests exist? Do they compile?
7. Does implementation exist? Is it stubbed or real?
8. Do tests pass?

**Output**: A readiness level (L0-L7) and a recommended next command.

| Level | Name | Meaning | Next Action |
|-------|------|---------|-------------|
| L0 | Concept | No deep dive exists | `/maintain-plugin` |
| L1 | Designed | Deep dive exists, may have gaps | `/audit-plugin` |
| L2 | Audited | Audit complete, findings resolved | `/map-plugin` |
| L3 | Mapped | Implementation map complete | Schema creation (manual) |
| L4 | Schema'd | Schemas exist, code generated | `/test-plugin` |
| L5 | Tested | Tests exist and compile (red phase) | `/implement-plugin` |
| L6 | Implemented | Implementation exists, tests may not all pass | Fix implementation |
| L7 | Production-Ready | All tests pass, map reflects reality | Done |

**Special cases**:
- Services that already have implementation (most L0-L2 services): Start at L6, then `/map-plugin` + `/audit-plugin` to verify and document
- Services with schemas but no implementation (Divine): Start at L4
- Services with no schemas (Agency): Start at L1 or L2 depending on deep dive quality

---

## The Pipeline in Practice

### New Plugin (e.g., Agency)

```
/check-plugin agency
  -> L1 (Designed): Deep dive exists, 21 audit findings unresolved
  -> Recommends: /audit-plugin agency

/audit-plugin agency
  -> Resolves Critical findings, creates issues for design questions
  -> Updates deep dive

/check-plugin agency
  -> L2 (Audited): All Critical findings resolved
  -> Recommends: /map-plugin agency

/map-plugin agency
  -> Creates docs/maps/AGENCY.md from deep dive (no existing code)
  -> Pseudocode for all 22 endpoints

/check-plugin agency
  -> L3 (Mapped): Map complete, no schemas
  -> Recommends: Schema creation (manual)

[User creates schemas, runs generation]

/check-plugin agency
  -> L4 (Schema'd): Schemas exist, code generated
  -> Recommends: /test-plugin agency

/test-plugin agency
  -> Reads map + generated interfaces
  -> Creates lib-agency.tests/ with tests for all 22 endpoints
  -> Tests compile but fail (NotImplemented stubs)

/check-plugin agency
  -> L5 (Tested): Tests exist, all red
  -> Recommends: /implement-plugin agency

/implement-plugin agency
  -> Reads map + failing tests
  -> Implements each endpoint to pass tests
  -> Updates map with edge cases discovered

/check-plugin agency
  -> L7 (Production-Ready): All tests pass
```

### Existing Stubbed Plugin (e.g., Divine)

```
/check-plugin divine
  -> L4 (Schema'd): Schemas exist, all endpoints return NotImplemented
  -> No implementation map exists
  -> Recommends: /map-plugin divine

/map-plugin divine
  -> Creates docs/maps/DIVINE.md from deep dive + schemas

/check-plugin divine
  -> L4 (Schema'd) + Map exists
  -> Recommends: /test-plugin divine

/test-plugin divine
  -> Creates lib-divine.tests/
  -> Tests compile, fail against stubs

/implement-plugin divine
  -> Implements endpoints to pass tests
```

### Already-Implemented Plugin (e.g., Account)

```
/check-plugin account
  -> L7 (Production-Ready): Deep dive complete, map complete, tests pass
  -> No action needed
```

---

## Change Categories During Implementation

Implementation will discover things the design couldn't predict. The key distinction:

| Change Category | Expected? | Handling |
|---|---|---|
| **Architectural** (new dependency, layer violation, wrong entity classification) | Should NOT happen if deep dive + audit were thorough | HARD STOP. Update deep dive, re-audit, potentially re-schema |
| **Tenet compliance** (missing validation, wrong event pattern) | Should be caught by audit but occasionally slips through | Fix schema/implementation, update map |
| **Edge cases** (race condition the map missed, Redis nuance, ETag failure path) | YES -- normal and healthy | Update map, add test, add to Known Quirks |
| **Practical adjustments** (batching strategy, lock scope, key prefix) | YES -- implementation reveals optimal approaches | Update map to match reality |

Categories 3 and 4 are embraced and documented. Categories 1 and 2 indicate insufficient pre-implementation work and should trigger the HARD STOP rule from CLAUDE.md.

---

## Why This Works for North Stars

**Living Game Worlds / Content Flywheel**: The aspirational plugins (Divine, Agency, Disposition, Hearsay, Lexicon, etc.) are the services that make the content flywheel spin. A formalized pipeline that gets them to production-ready quality predictably and safely accelerates the most critical part of the vision.

**100K Concurrent AI NPCs**: Every new variable provider (${spirit.*}, ${disposition.*}, ${hearsay.*}) must be multi-instance safe, hierarchy compliant, and performance-validated. The audit stage catches multi-instance safety, communication discipline, and cleanup violations before they become distributed system bugs at scale.

**Ship Games Fast**: A repeatable pipeline means each new service follows the same path. No architectural surprises. No rework cycles. The implementation map + TDD approach ensures the first implementation is correct.

**Emergent Over Authored**: The pipeline itself is emergent -- deep dives capture what the service IS, maps capture HOW it works, tests verify it DOES what it claims. The content (behavior documents, seed data, game configuration) is authored separately. The pipeline ensures the infrastructure for emergence is rock-solid.

---

## Next Steps

All skill commands referenced in this pipeline have been created. Remaining work:

1. Validate the pipeline end-to-end on a new plugin (Agency is the best candidate -- deep dive exists, no schemas yet)
2. Evaluate whether `/map-plugin` handles the "new plugin from deep dive" case well enough, or if it needs enhancement for pre-schema mapping
3. Gather feedback from pipeline usage to identify friction points between stages
