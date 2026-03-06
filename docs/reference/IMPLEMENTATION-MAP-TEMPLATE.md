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
| Endpoints* | {count} ({N} generated + {M} non-standard) |
| State Stores | {store-name} ({backend}), ... |
| Events Published* | {count} ({topic1}, {topic2}, ...) |
| Events Consumed* | {count} |
| Client Events* | {count} |
| Background Services | {count} |
```

When non-standard endpoints exist (controller-only, manual controller, manually-registered routes), break out the count. When all endpoints are standard generated endpoints, use just the total count.


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

Constructor dependencies, collection-injected providers, and internal helper services.

```markdown
#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<{Service}>` | Structured logging |
| `{Service}Configuration` | Typed configuration access |
| `IStateStoreFactory` | State store access |
| `IMessageBus` | Event publishing |
| `{HelperService}` | {What it does} |

#### Collection-Injected Providers (if any)

| Interface | Injection | Source | Role |
|-----------|-----------|--------|------|
| `IEnumerable<IVariableProviderFactory>` | Collection | External (L4 services register implementations) | Provides `${personality.*}`, `${encounters.*}`, etc. to behavior execution |
| `IEnumerable<IBehaviorDocumentProvider>` | Collection | External (Puppetmaster L4) | Supplies runtime-loaded ABML behaviors |
| `IEnumerable<IActionHandler>` | Collection | Internal (this plugin registers 9 handlers) | ABML action handler dispatch |

#### DI Interfaces Implemented by This Plugin (if any)

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `ISeededResourceProvider` | `Singleton` | L4→L1 pull | Resource (L1) discovers seeded data |
| `IBehaviorDocumentProvider` | `Singleton` | L4→L2 pull | Actor (L2) discovers behavior documents |
```

The **Collection-Injected Providers** sub-table captures `IEnumerable<T>` dependencies where DI discovers multiple implementations at runtime. The **Source** column distinguishes whether implementations come from this plugin (`Internal`) or are registered by other plugins (`External`).

The **DI Interfaces Implemented** sub-table captures interfaces this plugin implements for consumption by other services. This is the inverse: what this plugin provides TO the DI system.

Omit either sub-table if the plugin has no collection-injected providers or implements no DI interfaces. Most plugins will only have the Constructor Dependencies sub-table.

---

### 8. Method Index

One row per endpoint, providing a scannable overview. Generated endpoints can be auto-populated from the OpenAPI schema; non-standard endpoints must be added manually.

```markdown
| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CreateEntity | POST /entity/create | generated | admin | entity, index | entity.created |
| GetEntity | POST /entity/get | generated | user | - | - |
| DeleteEntity | POST /entity/delete | generated | admin | entity, index | entity.deleted |
| HandleWebhook | POST /webhooks/minio | manual | internal | - | asset.uploaded |
| ViewDocument | GET /docs/{slug} | x-manual | public | - | - |
| ConnectWS | GET /ws | x-controller-only | user | session | - |
```

**Source** column: How the endpoint is implemented.
- `generated` — Standard: on the generated `I{Service}Service` interface, routed by generated controller
- `x-controller-only` — In schema with `x-controller-only: true`, implemented directly in the generated controller (not on the service interface)
- `x-manual` — In schema with `x-manual-implementation: true`, implemented in a manual partial controller class
- `manual` — NOT in schema at all. Registered via `MapPost`/`MapGet` in plugin startup code or a standalone handler class

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

### 11. Non-Standard Implementation Patterns

This section captures implementation patterns that fall outside the standard generated-interface workflow. Most plugins will have none of these; include only the sub-sections that apply. If none apply, state: "No non-standard patterns."

#### Plugin Lifecycle (OnStartAsync / OnRunningAsync / OnShutdownAsync)

When the plugin startup/shutdown code contains non-trivial logic that affects service behavior (not just DI registration), document it using the same pseudo-code notation.

```markdown
#### OnStartAsync
// Non-trivial startup behavior that affects service state or external systems
CALL IExternalService.WaitForConnectivity()      // blocks until dependency available
FOREACH bucket in RequiredBuckets
  CALL IStorageClient.CreateBucketIfNotExists(bucket)
WRITE store:default-routes <- DefaultRouteTable
```

Omit trivial startup (DI registration, store acquisition) — those are implied by the template.

#### Non-Schema HTTP Endpoints

Endpoints registered via `MapPost`/`MapGet` in plugin code, not generated from schemas. Document each with a full pseudo-code block in Section 9, using the `manual` source marker in the Method Index.

```markdown
### HandleAuthEvents
POST /events/auth-events | Source: manual | Roles: [internal]

// Manually registered in OnStartAsync via MapPost
READ body as AuthEventPayload
IF event.Type == "session.expired"
  DELETE store:session-{sessionId}
  PUBLISH connect.session-expired { sessionId }
RETURN (200, null)
```

#### Manual Controller Implementations

Endpoints in the schema marked `x-manual-implementation` or `x-controller-only`, implemented in manual partial controller classes or directly in the generated controller. These follow normal pseudo-code conventions but use the appropriate source marker in the Method Index.

#### Custom Generated-Code Overrides

When a plugin manually implements methods that are normally auto-generated (e.g., `IBannouService.RegisterServicePermissionsAsync`), document the custom behavior and why the override exists.

```markdown
#### RegisterServicePermissionsAsync (override)
// Overrides generated permission registration with conditional logic
IF Configuration.SecureWebsocket
  CALL base.RegisterServicePermissionsAsync()   // standard matrix
ELSE
  // Register empty matrix — no endpoints exposed to WebSocket (service-to-service only)
  CALL PermissionClient.Register(emptyMatrix)
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
2. **Every endpoint must appear** — schema-generated endpoints AND non-standard endpoints (controller-only, manual controller, manually-registered routes). Each gets a full pseudo-code block in the Methods section
3. **Pseudo-code must be verifiable** — every `READ`/`WRITE`/`CALL`/`PUBLISH` must correspond to actual operations in the implementation (or intended operations for aspirational maps)
4. **State key patterns must match** — key patterns used in method pseudo-code must appear in the State table (section 3)
5. **Events must match** — `PUBLISH` topics in method pseudo-code must appear in the Events Published table (section 5)
6. **No implementation language syntax** — only the notation vocabulary above. No C#, no variable declarations, no helper method internals (inline the logic or note `// see helper: MethodName`)
7. **Aspirational maps are first-class** — for plugins without implementation, the pseudo-code IS the design specification. Mark with `> **Status**: Aspirational` in the header
8. **Preserve AUDIT markers** — same rule as deep dives. HTML comment markers are machine-managed
9. **Cross-reference deep dive** — header must link to the deep dive document. The deep dive header must link back to the implementation map
10. **Non-standard patterns are first-class** — plugin lifecycle behavior, non-schema endpoints, manual controller implementations, and custom generated-code overrides are captured in Section 11 when present. The Method Index marks each endpoint's source type

---

## Relationship to Other Documents

| Document | Purpose | Source of Truth For |
|----------|---------|---------------------|
| **OpenAPI Schema** | API contracts | Endpoints, request/response models, validation |
| **Implementation Map** | Behavioral specification | How each method works (state, calls, events, locks), non-standard patterns |
| **Deep Dive** | Architecture & context | Why it's designed this way, who uses it, known quirks |

When an implementation map exists for a plugin, operational sections (state, dependencies, events, DI, endpoint behavior) belong in the map. The deep dive focuses on architectural context (overview, dependents, configuration, visual aids, quirks, work tracking).

Until a map is created for a plugin, existing deep dive sections serve as the canonical reference. Migration is incremental.
