# x-service-layer

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-api.yaml`
> **Generated Output**: `layer` parameter on `[BannouService]` attribute in service registration
> **Tenet References**: T2 (FOUNDATION -- Service Hierarchy)

---

## Summary

Declares a service's position in the six-layer hierarchy, controlling plugin load order and enabling safe cross-layer constructor injection. Services at lower layers load first, ensuring dependencies are available before dependents initialize. Defaults to GameFeatures if omitted.

---

## Schema Syntax

### Root-Level Declaration

`x-service-layer` is declared at the root level of the service's API schema, alongside `openapi`, `info`, and `servers`:

```yaml
openapi: 3.0.4
info:
  title: Location Service
  version: 1.0.0
x-service-layer: GameFoundation
servers:
  - url: /location
```

### Minimal Example

```yaml
openapi: 3.0.4
info:
  title: Puppetmaster Service
  version: 1.0.0
x-service-layer: GameFeatures
```

### Omitted (Default Behavior)

When `x-service-layer` is omitted, the service defaults to `GameFeatures`:

```yaml
openapi: 3.0.4
info:
  title: My Custom Service
  version: 1.0.0
# x-service-layer not specified -- defaults to GameFeatures
```

---

## Field Reference

### Valid Values

| Value (String) | Numeric Equivalent | Layer | Description |
|---|---|---|---|
| `Infrastructure` | `0` | L0 | State, Messaging, Mesh, Telemetry. Always loaded first. |
| `AppFoundation` | `100` | L1 | Account, Auth, Chat, Connect, Permission, Contract, Resource. |
| `GameFoundation` | `200` | L2 | Character, Realm, Species, Location, Currency, Item, Inventory, and other game primitives. |
| `AppFeatures` | `300` | L3 | Asset, Orchestrator, Documentation, Website, Voice, Broadcast. |
| `GameFeatures` | `400` | L4 | Behavior, Faction, Divine, Dungeon, Craft, and 37+ other game feature services. **Default if omitted.** |
| `Extensions` | `500` | L5 | Developer-created game-specific vocabulary and simplified APIs. |

Both the string name and the numeric value are accepted in the schema. The string form is preferred for readability.

---

## Generated Output

### [BannouService] Attribute

The `x-service-layer` value is emitted as the `layer` parameter on the `[BannouService]` attribute in the service's implementation class:

```csharp
[BannouService("location", typeof(ILocationService),
    lifetime: ServiceLifetime.Scoped,
    layer: ServiceLayer.GameFoundation)]
public partial class LocationService : ILocationService
{
    // ...
}
```

### ServiceLayer Enum

The `ServiceLayer` enum mirrors the valid schema values:

```csharp
public enum ServiceLayer
{
    Infrastructure = 0,
    AppFoundation = 100,
    GameFoundation = 200,
    AppFeatures = 300,
    GameFeatures = 400,
    Extensions = 500
}
```

### Examples by Layer

```csharp
// L0 Infrastructure
[BannouService("mesh", typeof(IMeshService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.Infrastructure)]

// L1 App Foundation
[BannouService("account", typeof(IAccountService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFoundation)]

// L2 Game Foundation
[BannouService("worldstate", typeof(IWorldstateService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]

// L3 App Features
[BannouService("documentation", typeof(IDocumentationService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFeatures)]

// L4 Game Features (default)
[BannouService("obligation", typeof(IObligationService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
```

---

## Runtime Behavior

### Plugin Load Order

At startup, the assembly loader discovers all plugins with `[BannouService]` attributes and sorts them by `ServiceLayer` numeric value. Lower layers register their DI services first:

1. **L0 Infrastructure** (0) -- State stores, messaging, mesh, telemetry
2. **L1 App Foundation** (100) -- Account, Auth, Connect, etc.
3. **L2 Game Foundation** (200) -- Character, Realm, Item, etc.
4. **L3 App Features** (300) -- Asset, Orchestrator, etc.
5. **L4 Game Features** (400) -- Behavior, Faction, Divine, etc.
6. **L5 Extensions** (500) -- Developer game-specific services

This ordering guarantees that when a Game Features service's constructor requests an `ICharacterClient` (Game Foundation), the dependency is already registered.

### Dependency Enforcement

The layer value enforces the dependency rules from SERVICE-HIERARCHY.md:

- A service may depend on services in its own layer or lower layers
- Dependencies on higher layers are forbidden
- L0/L1/L2 dependencies use constructor injection (hard -- fails at startup if missing)
- L3/L4 dependencies use `GetService<T>()` with null checks (soft -- graceful degradation)

### Layer-Level Enablement

Layer flags control which layers are active per deployment node:

- `BANNOU_ENABLE_APP_FOUNDATION` (default: `true`)
- `BANNOU_ENABLE_GAME_FOUNDATION` (default: `true`)
- `BANNOU_ENABLE_APP_FEATURES` (default: `true`)
- `BANNOU_ENABLE_GAME_FEATURES` (default: `true`)
- `BANNOU_ENABLE_EXTENSIONS` (default: `true`)

Infrastructure (L0) is always enabled. When a layer is disabled, all services at that layer are skipped during assembly loading.

---

## Structural Tests

| Test Name | Validates |
|---|---|
| `Services_WithEventSubscriptions_MustRegisterConsumers` | Indirectly validates layer correctness by ensuring services that consume events from other layers have proper subscription registration |

Layer dependency violations are primarily enforced at the schema level through code review and by the dependency injection system at startup (constructor injection fails if a required lower-layer service is not loaded). The `x-service-layer` value itself is validated during code generation.

---

## Examples

### Example 1: Game Foundation Service (Location)

Location is a core game primitive at L2, depended on by many L4 services.

**Schema** (`location-api.yaml`):
```yaml
openapi: 3.0.4
info:
  title: Location Service
  description: Hierarchical location tree within realms.
  version: 1.0.0
x-service-layer: GameFoundation
servers:
  - url: /location
```

**Generated attribute**:
```csharp
[BannouService("location", typeof(ILocationService),
    lifetime: ServiceLifetime.Scoped,
    layer: ServiceLayer.GameFoundation)]
public partial class LocationService : ILocationService
```

### Example 2: App Features Service (Documentation)

Documentation is an L3 service, optional in game deployments.

**Schema** (`documentation-api.yaml`):
```yaml
openapi: 3.0.4
info:
  title: Documentation Service
  description: Knowledge base API for AI agents.
  version: 1.0.0
x-service-layer: AppFeatures
servers:
  - url: /documentation
```

**Generated attribute**:
```csharp
[BannouService("documentation", typeof(IDocumentationService),
    lifetime: ServiceLifetime.Scoped,
    layer: ServiceLayer.AppFeatures)]
public partial class DocumentationService : IDocumentationService
```

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| L2 service declaring `x-service-layer: GameFeatures` (or higher) | Would violate service hierarchy if it has hard dependencies on L2 peers. Layer must match actual dependency profile. |
| L4 service declaring `x-service-layer: GameFoundation` (or lower) | Would load before its actual peers and could cause dependency ordering issues. |
| Invalid string value (e.g., `x-service-layer: Custom`) | Only the six defined values are accepted. Generation fails on unrecognized values. |

### Scoping Rules

- **One declaration per service**: `x-service-layer` is declared once in the service's `*-api.yaml` file
- **Not used in events or configuration schemas**: Only `*-api.yaml` files support this attribute
- **Affects the entire plugin**: The layer applies to the service class, all its endpoints, and its DI registration

### Interaction with Other Extension Attributes

- **x-permissions**: Layer affects which roles can access endpoints. L0 infrastructure endpoints typically have `[]` (service-only) permissions.
- **Layer enablement flags**: The `x-service-layer` value determines which `BANNOU_ENABLE_*` flag controls the service's activation in deployment configurations.

### Known Limitations

- **No sub-layer ordering**: Services within the same layer have no guaranteed load order relative to each other. If two L4 services depend on each other, they must handle initialization order gracefully.
- **Default is GameFeatures**: Omitting `x-service-layer` places the service at L4. This is appropriate for most game feature services but incorrect for foundation or infrastructure services. Always declare explicitly for non-L4 services.
