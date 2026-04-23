---
description: "Create OpenAPI schemas for a plugin from its deep dive and implementation map. Writes api.yaml, events.yaml, configuration.yaml, client-events.yaml, and state store entries. Validates against SCHEMA-RULES.md. Requires L3 (Mapped) readiness."
argument-hint: "[plugin-name] - Plugin to create schemas for (e.g., 'agency', 'disposition'). Must have deep dive and implementation map (L3+)."
disable-model-invocation: true
---

# Plugin Schema Creator

Creates the complete set of OpenAPI schemas for a Bannou plugin from its deep dive and implementation map. Schemas are the source of truth from which all generated code flows.

## Prerequisites

This workflow requires **L3 (Mapped)** or higher:
1. **Deep dive** at `docs/plugins/{PLUGIN_NAME}.md`
2. **Implementation map** at `docs/maps/{PLUGIN_NAME}.md`

If either is missing → STOP and recommend `/check-plugin {service}`.

## Rules

1. **SCHEMA-RULES.md is law.** Every schema decision must comply. When in doubt, re-read the relevant section.
2. **The implementation map IS the schema specification.** Every endpoint, event, state store, and configuration property in the map becomes a schema definition. If it's in the map, it's in the schema. If it's not in the map, it's not in the schema.
3. **Study reference schemas before writing.** Read 2-3 similar plugins' schemas to match established patterns. Do not invent new conventions.
4. **All enum values are PascalCase.** `TwoParty`, not `two_party` or `TWO_PARTY`.
5. **All properties MUST have `description` fields.** Missing descriptions cause CS1591 warnings.
6. **$ref direction: API schemas are source of truth.** Events $ref INTO api schemas, never the reverse.
7. **Topic naming: Pattern A or C only.** Pattern B (service name embedded via hyphens in entity) is FORBIDDEN.
8. **NRT compliance is mandatory.** See SCHEMA-RULES.md § NRT Compliance for the required/nullable matrix.
9. **Do not create schemas that duplicate types from other services.** Use `$ref` to `common-api.yaml` or define in the owning service's API schema.

---

## Phase 1: Context & Selection

**Step 1a:** Load schema context (dev + schema rules + specifications):
```
prepare_context(profile: "schema")
```

**Step 1b:** Load plugin context:
```
prepare_context(profile: "plugin", service: "{plugin-name}")
```

**Step 1c:** Validate prerequisites:
```bash
ls docs/plugins/{PLUGIN_NAME}.md docs/maps/{PLUGIN_NAME}.md
```
If either missing → STOP with prerequisite failure.

**Step 1d:** Normalize names: `PLUGIN_NAME`, `service_name`, `ServiceName`.

## Phase 2: Study Reference Schemas

Before writing anything, read 2-3 existing schemas from plugins with similar characteristics:

```bash
ls schemas/*-api.yaml | head -20
```

Choose plugins that share traits with the target (similar layer, similar endpoint count, similar patterns — e.g., if the target has deprecation lifecycle, pick a plugin that does too).

For each reference plugin, read:
```
get_schema(name: "{reference-service}")
```

Note the patterns: how endpoints are structured, how x-permissions are declared, how enums are defined, how events reference API types, how configuration properties are formatted.

## Phase 3: Analyze Map for Schema Requirements

Read the implementation map systematically. Extract:

### 3a: Endpoints
From the Method Index, for each endpoint:
- Route, HTTP method, roles (from x-permissions)
- Request model fields (from pseudocode — every field read from `request.*`)
- Response model fields (from pseudocode — every field in the RETURN)
- Error status codes (from each conditional RETURN)

### 3b: Events
From Events Published:
- Topic name, event type name, payload fields
- Whether `x-lifecycle` applies (created/updated/deleted patterns)
- Whether `x-event-publications` with `topic-params` is needed (parameterized topics)

From Events Consumed (for `x-event-subscriptions`):
- Topic patterns, source services

### 3c: Configuration
From the map's DI Services or deep dive's Configuration section:
- Property names, types, defaults, env var names
- Range constraints (min/max for timeouts, batch sizes)

### 3d: State Stores
From the map's State section:
- Store names, backing types (Redis/MySQL)
- Key patterns (informational — not in schema, but guides model design)

### 3e: Shared Types
Identify types that should reference `common-api.yaml` vs being defined locally:
- `EntityType`, `CleanDeprecatedRequest/Response`, `MergeDeprecatedRequest/Response` → always from common
- Service-specific enums → define in this service's api.yaml

### 3f: Client Events
From the map's Events Published (client events section, if any):
- Event names, payload types

## Phase 4: Write API Schema

Create `schemas/{service}-api.yaml` using `write_file`.

**Structure:**
```yaml
openapi: 3.0.3
info:
  title: {Service Name} API
  version: "1.0"
  description: {Brief description from deep dive overview}

paths:
  /{service}/{entity}/{action}:
    post:
      operationId: {MethodName}
      summary: {From map method description}
      x-permissions:
        - role: {from map Method Index}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/{MethodName}Request'
      responses:
        '200':
          description: Success
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/{MethodName}Response'
        {error responses from map's conditional RETURNs}

components:
  schemas:
    {Request/Response/Entity models}
```

**Checklist during writing:**
- [ ] Every endpoint from the map's Method Index has a path entry
- [ ] Every request model has fields matching what the map's pseudocode reads from `request.*`
- [ ] Every response model has fields matching the map's RETURN payloads
- [ ] Every property has a `description`
- [ ] Enums are PascalCase
- [ ] `x-permissions` matches the map's Method Index roles column
- [ ] NRT compliance: required array set correctly, nullable markers on optional fields
- [ ] No echoed request fields in responses (per IMPLEMENTATION TENETS)
- [ ] No success booleans, confirmation messages, or action timestamps in responses

## Phase 5: Write Events Schema

Create `schemas/{service}-service-events.yaml` if the map lists published or consumed events.

**Structure:**
```yaml
openapi: 3.0.3
info:
  title: {Service Name} Service Events
  version: "1.0"

x-event-publications:
  - topic: "{service}.{entity}.{action}"
    schema: {EventType}
    {topic-params if parameterized}

x-event-subscriptions:
  - topic: "{producer}.{entity}.{action}"
    service: {producer-service}

x-lifecycle:
  {entity}:
    fields:
      {standard lifecycle fields per SCHEMA-RULES.md}
    {instanceEntity: {name} for Category B deprecation}

components:
  schemas:
    {Event models — $ref to api.yaml for shared types}
```

**Checklist:**
- [ ] Topic names follow Pattern A or C (dot-separated, no underscores)
- [ ] Event types $ref to api.yaml for shared types, NOT duplicate definitions
- [ ] `x-lifecycle` used for standard CRUD event patterns
- [ ] `x-event-publications` lists every PUBLISH from the map
- [ ] `topic-params` included for parameterized topics with name/type entries
- [ ] `x-event-subscriptions` lists every consumed event

## Phase 6: Write Configuration Schema

Create `schemas/{service}-configuration.yaml` if the map/deep dive lists configuration properties.

**Structure per SCHEMA-RULES.md:**
```yaml
openapi: 3.0.3
info:
  title: {Service Name} Configuration
  version: "1.0"

x-service-configuration:
  envPrefix: "{SERVICE}_"

components:
  schemas:
    {ServiceName}ServiceConfiguration:
      type: object
      properties:
        {PropertyName}:
          type: {type}
          description: {description}
          default: {value}
          env: {SERVICE}_{PROPERTY_NAME}
          {minimum/maximum for numeric constraints}
```

**Checklist:**
- [ ] Every tunable from the map has a config property (no hardcoded magic numbers)
- [ ] Env var naming: `{SERVICE}_{PROPERTY}` pattern (underscores, not hyphens)
- [ ] Defaults provided for all properties
- [ ] Range constraints for numeric values (timeouts, batch sizes, thresholds)
- [ ] No comma-delimited strings for structured config — use typed arrays or individual properties

## Phase 7: Write Client Events Schema

Create `schemas/{service}-client-events.yaml` ONLY if the map lists client events (WebSocket push to clients).

Follow the same pattern as reference schemas. Client event models use `ClientEvent` suffix.

## Phase 8: Add State Store Entries

Edit `schemas/state-stores.yaml` to add entries for this plugin's state stores (from the map's State section).

Read the file first:
```
read_file("schemas/state-stores.yaml")
```

Then add entries following the established pattern in the file.

## Phase 9: Schema Audit

**Perform this audit yourself — do not delegate.**

Re-read SCHEMA-RULES.md (already in context from `prepare_context(profile: "schema")`).

For EACH schema file you wrote, verify:

| Check | Rule Source |
|---|---|
| All properties have `description` | SCHEMA-RULES.md |
| Enum values are PascalCase | SCHEMA-RULES.md |
| NRT compliance (required array, nullable) | SCHEMA-RULES.md § NRT |
| $ref direction correct (events → api, not reverse) | SCHEMA-RULES.md § Reference Hierarchy |
| Topic names follow Pattern A or C | SCHEMA-RULES.md § Topic Naming |
| x-permissions on every endpoint | TENETS T13 |
| Env var naming `{SERVICE}_{PROPERTY}` | SCHEMA-RULES.md § Configuration |
| No shared type duplication | SCHEMA-RULES.md |
| Event publications have `topic-params` where needed | SCHEMA-RULES.md |
| Response models have no filler properties | TENETS T8 |

Fix any violations immediately with `edit_file`.

## Phase 10: Code Generation

Run the generation script:
```bash
cd scripts && ./generate-service.sh {service_name} > /tmp/{service}-generate.txt 2>&1
```

Read the output. If generation fails, fix the schema issue and retry.

After successful generation, build:
```bash
dotnet build plugins/lib-{service_name}/lib-{service_name}.csproj --no-restore > /tmp/{service}-build.txt 2>&1
```

If build fails, read errors, fix schema, regenerate, rebuild. Max 3 attempts → STOP and report.

## Phase 11: Report

```markdown
## Schema Creation Complete: {PLUGIN_NAME}

### Files Created
| File | Type | Endpoints/Events/Properties |
|------|------|---------------------------|
| `schemas/{service}-api.yaml` | API | {N} endpoints |
| `schemas/{service}-service-events.yaml` | Events | {N} published, {N} consumed |
| `schemas/{service}-configuration.yaml` | Config | {N} properties |
| `schemas/{service}-client-events.yaml` | Client Events | {N} events (or "N/A") |
| `schemas/state-stores.yaml` | State Stores | {N} entries added |

### Generation & Build
- Generation: {PASS/FAIL}
- Build: {PASS/FAIL}

### Audit Results
- Violations found: {N}
- Violations fixed: {N}

### Map Coverage
- Endpoints in map: {N} | In schema: {N}
- Events in map: {N} | In schema: {N}
- Config properties in map: {N} | In schema: {N}

### Next Step
**Command:** `/test-plugin {service_name}` or `/check-plugin {service_name}`
```
