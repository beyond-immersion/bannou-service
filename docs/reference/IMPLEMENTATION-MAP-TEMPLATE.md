# Plugin Implementation Map Template

> This document defines the structure for per-plugin implementation maps.
> Each plugin gets one map: `docs/maps/{SERVICE-NAME}.md`

---

## What Is an Implementation Map?

An implementation map is a pseudo-code-level behavioral specification for a Bannou plugin. It describes **what each method does** — what state it reads and writes, what services it calls, what events it publishes, what locks it holds, and what it returns — using a structured notation that maps directly to Bannou's infrastructure abstractions.

Implementation maps serve three purposes:

1. **Single-file reference** for implemented plugins — eliminates the need to read 5+ source files to understand a service
2. **Design specification** for pre-implementation plugins — the pseudo-code IS the implementation plan
3. **Agent orientation** — gives AI agents complete behavioral context without launching multiple exploration subagents

Implementation maps complement deep dives. The **deep dive** covers WHAT the plugin is, WHY it's designed that way, WHO uses it, and WHAT's weird about it. The **implementation map** covers HOW it works, method by method.

---

## Process Instructions

### For Implemented Plugins

1. **Read all source code thoroughly**: Service implementation, event handlers, helper services, models. Every method body, every branch.
2. **Build the document structure**: Fill in sections 1-10 based on verifiable source code. Every `READ`/`WRITE`/`CALL`/`PUBLISH` must correspond to an actual operation in the code.
3. **Verify completeness**: Every endpoint in the schema must appear in the Method Index and Methods sections. Every state key pattern used in code must appear in the State table.

### For Aspirational Plugins (Pre-Implementation)

1. **Read the schema and deep dive**: Understand the intended endpoints, state stores, events, and dependencies.
2. **Build the document structure**: Fill in sections 1-10 based on the schema and design intent.
3. **Write pseudo-code as specification**: The Methods section describes what the implementation SHOULD do. This IS the implementation plan — when the design is satisfactory, implement it as-shown.

---

## Document Structure

### 1. Header

Two header formats depending on implementation status.

#### Implemented Plugin Header

```markdown
# {Service Name} Implementation Map

> **Plugin**: lib-{service}
> **Schema**: schemas/{service}-api.yaml
> **Layer**: {Infrastructure / AppFoundation / GameFoundation / AppFeatures / GameFeatures}
> **Deep Dive**: [docs/plugins/{SERVICE-NAME}.md](../plugins/{SERVICE-NAME}.md)
```

#### Aspirational Plugin Header

```markdown
# {Service Name} Implementation Map

> **Plugin**: lib-{service}
> **Schema**: schemas/{service}-api.yaml
> **Layer**: {Infrastructure / AppFoundation / GameFoundation / AppFeatures / GameFeatures}
> **Deep Dive**: [docs/plugins/{SERVICE-NAME}.md](../plugins/{SERVICE-NAME}.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation
```

---

### 2. Summary Table

A quick-reference overview of the plugin's footprint. Fields marked with `*` can be auto-generated from schemas.

```markdown
| Field | Value |
|-------|-------|
| Plugin* | lib-{service} |
| Layer* | L{n} {LayerName} |
| Endpoints* | {count} |
| State Stores | {store-name} ({backend}), ... |
| Events Published* | {count} ({topic1}, {topic2}, ...) |
| Events Consumed* | {count} |
| Client Events* | {count} |
| Background Services | {count} |
```

---

### 3. State

Document every state store and key pattern used by the service.

```markdown
**Store**: `{store-name}` (Backend: MySQL/Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{prefix}{id}` | `{ModelType}` | {What this key stores} |
| `{prefix}{field}` | `string` | {Index or lookup purpose} |
```

For services with multiple stores (e.g., a primary MySQL store and a Redis lock store), list each store with its own table.

---

### 4. Dependencies

What this plugin relies on at runtime. Include layer and dependency type.

```markdown
| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Persistence for {data type} |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing {event} events |
| lib-{other} (I{Other}Client) | L{n} | Hard | {What it calls the other service for} |
| lib-{other} (I{Other}Client) | L{n} | Soft | {Purpose} (graceful degradation if absent) |
```

**Type** values:
- **Hard**: Constructor injection. Service fails at startup if missing. Used for L0/L1/L2 dependencies.
- **Soft**: Runtime resolution via `GetService<T>()` with null check. Used for L3/L4 optional dependencies.

Include notes about special dependency patterns (e.g., T28 privacy exceptions, DI Provider/Listener interfaces) below the table.

---

### 5. Events Published

```markdown
| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `{entity}.{action}` | `{EventType}` | {When this fires and what it includes} |
```

---

### 6. Events Consumed

```markdown
| Topic | Handler | Action |
|-------|---------|--------|
| `{entity}.{action}` | `Handle{Event}Async` | {What happens when received} |
```

If the plugin does not consume external events, state: "This plugin does not consume external events."

---

### 7. DI Services

Constructor dependencies and internal helper services.

```markdown
| Service | Role |
|---------|------|
| `ILogger<{Service}>` | Structured logging |
| `{Service}Configuration` | Typed configuration access |
| `IStateStoreFactory` | State store access |
| `IMessageBus` | Event publishing |
| `{HelperService}` | {What it does} |
```

---

### 8. Method Index

One row per endpoint, providing a scannable overview. This section can be auto-generated from the OpenAPI schema.

```markdown
| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateEntity | POST /entity/create | admin | entity, index | entity.created |
| GetEntity | POST /entity/get | user | - | - |
| DeleteEntity | POST /entity/delete | admin | entity, index | entity.deleted |
```

**Mutates** column: List which state key patterns are written or deleted (use short names, not full patterns). `-` if read-only.

**Publishes** column: List event topics published. `-` if none.

---

### 9. Methods

The core of the implementation map. Every endpoint gets a structured pseudo-code block.

#### Method Block Format

```markdown
### MethodName
POST /service/endpoint | Roles: [role1, role2]

READ store:key-pattern [with ETag]           -> 404 if null
CALL IClient.Method(params)                  -> 400 if condition
WRITE store:key-pattern <- ModelType from request
PUBLISH topic { relevant fields }
RETURN (200, ResponseType)
```

#### Formatting Rules

- **One block per endpoint** — no grouping or abbreviating "simple" methods
- **Method header**: `### MethodName` followed by `POST /route | Roles: [roles]`
- **Pseudo-code body**: Uses the notation vocabulary defined below
- **Indentation**: 2 spaces for scope (lock body, loop body, conditional branches)
- **Comments**: `//` prefix for clarifying notes
- **Omit**: Logging calls, generated controller boundary, CancellationToken, telemetry spans — all are implied by tenets

#### Ordering

Methods should appear in the same order as the Method Index table, which should follow the schema's endpoint ordering.

---

### 10. Background Services

For plugins with background workers or hosted services. If none, state: "No background services."

```markdown
### {WorkerClassName}
**Interval**: {duration from configuration property}
**Purpose**: {What the worker does}

// Pseudo-code using the same notation
QUERY store WHERE condition
FOREACH item in results
  {processing logic}
  WRITE/DELETE/PUBLISH as needed
```

---

## Pseudo-Code Notation Reference

Every keyword maps to a specific Bannou infrastructure abstraction. This notation is the only vocabulary permitted in method blocks.

### State Operations

| Keyword | Infrastructure | Description |
|---------|---------------|-------------|
| `READ` | `IStateStore<T>.GetAsync` | Read a single record by key |
| `READ ... [with ETag]` | `IStateStore<T>.GetWithETagAsync` | Read with ETag for optimistic concurrency |
| `QUERY ... WHERE ... [ORDER BY ...] [PAGED(...)]` | `IJsonQueryableStateStore<T>.JsonQueryPagedAsync` | Paginated JSON query (MySQL). Use `$.Path` for JSON conditions, `PAGED(page, pageSize)` for pagination |
| `COUNT ... WHERE ...` | `IJsonQueryableStateStore<T>.JsonCountAsync` | Count query (MySQL). Same condition syntax as `QUERY` |
| `WRITE` | `IStateStore<T>.SaveAsync` | Write/overwrite a record |
| `ETAG-WRITE` | `IStateStore<T>.TrySaveAsync` (with etag) | Write with optimistic concurrency check |
| `DELETE` | `IStateStore<T>.DeleteAsync` | Delete a record by key |

### Service Communication

| Keyword | Infrastructure | Description |
|---------|---------------|-------------|
| `CALL` | Generated `I{Service}Client` via lib-mesh | Synchronous inter-service call |
| `PUBLISH` | `IMessageBus.TryPublishAsync` | Publish a service event |
| `PUSH` | `IClientEventPublisher.PublishToSessionAsync` | Push a client event via WebSocket |

### Concurrency

| Keyword | Infrastructure | Description |
|---------|---------------|-------------|
| `LOCK` | `IDistributedLockProvider.LockAsync` | Acquire distributed lock (indent body) |

### Flow Control

| Keyword | Description |
|---------|-------------|
| `RETURN` | Method return as `(StatusCode, TResponse?)` tuple |
| `IF` / `ELSE` | Conditional branch |
| `FOREACH` | Iteration (note `(parallel)` for `Task.WhenAll` patterns) |

### Formatting Symbols

| Symbol | Meaning | Example |
|--------|---------|---------|
| `->` | Early return on condition | `-> 404 if null` |
| `<-` | Data source for write | `WRITE key <- ModelType from request` |
| `[with ETag]` | ETag-based read | `READ key [with ETag]` |
| `{ ... }` | Event payload highlights | `PUBLISH topic { action: Created, entityId }` |
| `//` | Comment | `// Rollback on failure` |

---

## Rules

1. **One map per plugin** — placed at `docs/maps/{SERVICE-NAME}.md`
2. **Every endpoint must appear** — no skipping "simple" methods. Each gets a full pseudo-code block in the Methods section
3. **Pseudo-code must be verifiable** — every `READ`/`WRITE`/`CALL`/`PUBLISH` must correspond to actual operations in the implementation (or intended operations for aspirational maps)
4. **State key patterns must match** — key patterns used in method pseudo-code must appear in the State table (section 3)
5. **Events must match** — `PUBLISH` topics in method pseudo-code must appear in the Events Published table (section 5)
6. **No implementation language syntax** — only the notation vocabulary above. No C#, no variable declarations, no helper method internals (inline the logic or note `// see helper: MethodName`)
7. **Aspirational maps are first-class** — for plugins without implementation, the pseudo-code IS the design specification. Mark with `> **Status**: Aspirational` in the header
8. **Preserve AUDIT markers** — same rule as deep dives. HTML comment markers are machine-managed
9. **Cross-reference deep dive** — header must link to the deep dive document. The deep dive header must link back to the implementation map

---

## Relationship to Other Documents

| Document | Purpose | Source of Truth For |
|----------|---------|---------------------|
| **OpenAPI Schema** | API contracts | Endpoints, request/response models, validation |
| **Implementation Map** | Behavioral specification | How each method works (state, calls, events, locks) |
| **Deep Dive** | Architecture & context | Why it's designed this way, who uses it, known quirks |
| **Implementation Rules** | Pattern reference | Consolidated tenet guidelines for method design |

When an implementation map exists for a plugin, operational sections (state, dependencies, events, DI, endpoint behavior) belong in the map. The deep dive focuses on architectural context (overview, dependents, configuration, visual aids, quirks, work tracking).

Until a map is created for a plugin, existing deep dive sections serve as the canonical reference. Migration is incremental.
