# Plugin Deep Dive Template

> This document defines the structure for per-plugin deep-dive documents.
> Each plugin gets one document: `docs/plugins/{service-name}.md`

---

## Document Structure

### 1. Header

```markdown
# {Service Name} Plugin Deep Dive

> **Plugin**: lib-{service}
> **Schema**: schemas/{service}-api.yaml
> **Version**: {version from schema}
> **State Store**: {store name} ({backend})
```

---

### 2. Overview

A concise description of what the plugin does, its role in the system, and its access scope (internal-only, internet-facing, etc). This should answer: "Why does this plugin exist and what problem does it solve?" in 2-4 sentences.

---

### 3. Dependencies (What This Plugin Relies On)

A table of other plugins/infrastructure this plugin calls or depends on, with a brief explanation of what it uses each for.

```markdown
| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | Persistence for {data type} |
| lib-messaging (IMessageBus) | Publishing {event} events |
| lib-{other} (I{Other}Client) | {What it calls the other service for} |
```

---

### 4. Dependents (What Relies On This Plugin)

A table of other plugins that consume this plugin's data or events.

```markdown
| Dependent | Relationship |
|-----------|-------------|
| lib-{other} | Calls {endpoint} via I{Service}Client for {purpose} |
| lib-{other} | Subscribes to `{topic}` event to {reaction} |
```

---

### 5. State Storage

Describe the state store used, the backend type, and document each key pattern with its purpose and data type.

```markdown
**Store**: `{store-name}` (Backend: MySQL/Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{prefix}{id}` | `{ModelType}` | {What this key stores} |
```

---

### 6. Events

#### Published Events

Table of events this plugin publishes and when.

```markdown
| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `{entity}.{action}` | `{EventType}` | {When this fires} |
```

#### Consumed Events

Table of events this plugin subscribes to and what it does with them. If none, state "This plugin does not consume external events."

```markdown
| Topic | Handler | Action |
|-------|---------|--------|
| `{entity}.{action}` | `Handle{Event}Async` | {What happens} |
```

---

### 7. Configuration

Full list of configuration properties from the configuration schema, with environment variable names, defaults, and what each controls in the service logic.

```markdown
| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `{Name}` | `{SERVICE}_{PROPERTY}` | {value or "required"} | {What it controls} |
```

---

### 8. DI Services & Helpers

List of injected dependencies and internal helper classes, with their roles.

```markdown
| Service | Role |
|---------|------|
| `ILogger<{Service}>` | Structured logging |
| `{Service}Configuration` | Typed configuration access |
| `IStateStoreFactory` | State store access |
| `IMessageBus` | Event publishing |
| `{HelperClass}` | {What it does} |
```

---

### 9. API Endpoints (Implementation Notes)

Brief implementation-level notes on endpoint groups. Not a repeat of the generated docs, but notes on internal behavior, edge cases, and non-obvious logic. Group by tag as in the schema.

**Endpoint groups that are straightforward CRUD can be described collectively** (e.g., "Standard CRUD operations on {entity} with optimistic concurrency via ETags"). Only call out individual endpoints when they have non-obvious behavior, special edge cases, or implementation quirks worth documenting.

---

### 10. Interconnection Diagram

One Mermaid diagram showing how this plugin connects to its dependencies and dependents. Limit to **one diagram per document** unless an exceptional case warrants a second (e.g., a plugin with two completely independent subsystems that would be unreadable in a single diagram).

```markdown
```mermaid
graph LR
    ...
`` `
```

---

### 11. Stubs & Unimplemented Features

Things that have scaffolding, configuration, or partial code but are not yet functional. Each entry should note what exists and what's missing to complete it.

---

### 12. Potential Extensions

Technical observations about ways the plugin could be improved or extended. No stubs exist for these - they are forward-looking ideas. Technical description only (no priority/effort estimates).

---

### 13. Known Quirks & Caveats

Non-obvious behaviors, workarounds, or implementation decisions that a developer should be aware of when working with this plugin. Things that "work but might surprise you."

---

## Rules

1. **One diagram maximum** per document unless the plugin genuinely has two independent subsystems that cannot be combined into one readable diagram.
2. **No speculative information** - every statement must be verified against the source code.
3. **No generated docs repetition** - the API endpoint section adds implementation context, not a copy of the schema descriptions.
4. **Keep it scannable** - tables over prose where possible, prose only where context requires narrative explanation.
5. **Configuration must be complete** - every property in the configuration schema must appear in the configuration table.
6. **State key patterns must be complete** - every key prefix used in the service code must be documented.
7. **Event coverage must be complete** - every published and consumed event topic must be listed.
