# Implementation Tenets: Data Modeling & Code Discipline

> **Category**: How data is typed, structured, and code is written
> **When to Reference**: While writing the actual lines of code — data modeling, serialization, configuration, async patterns, disposables, type safety, null representation
> **Tenets**: T14, T20, T21, T23, T24, T25, T26
> **Source Code Category**: `IMPLEMENTATION TENETS` (shared with [Service Behavior & Contracts](IMPLEMENTATION-BEHAVIOR.md))

These tenets define how the **code is written** — modeling patterns, type discipline, and coding conventions.

---

## Tenet 14: Polymorphic Associations (STANDARDIZED)

**Rule**: When entities reference multiple entity types, use **Entity ID + Type Column** in schemas and **composite string keys** for state store operations.

```yaml
# Schema pattern
CreateRelationshipRequest:
  type: object
  additionalProperties: false
  required: [entity1Id, entity1Type]
  properties:
    entity1Id:
      type: string
      format: uuid
      description: First entity in the relationship
    entity1Type:
      $ref: '#/components/schemas/EntityType'
      description: Type of the first entity
```

```csharp
// Composite keys for state store
private static string BuildEntityRef(Guid id, EntityType type)
    => $"{type.ToString().ToLowerInvariant()}:{id}";

private static string BuildCompositeKey(...)
    => $"composite:{BuildEntityRef(id1, type1)}:{BuildEntityRef(id2, type2)}:{relationshipTypeId}";
```

Since lib-state stores cannot enforce foreign key constraints, implement validation in service logic and subscribe to `entity.deleted` events for cascade handling.

### Polymorphic Type Field Classification (MANDATORY)

Polymorphic type discriminator fields (`ownerType`, `entityType`, `partyType`, etc.) fall into exactly three categories. Apply the decision tree below mechanically — no judgment calls.

**Category A — "What entity is this?"**: Identifies which kind of Bannou entity a polymorphic ID references.
- Default type: `$ref: 'common-api.yaml#/components/schemas/EntityType'`
- Exception: Use a service-specific enum if the valid set includes non-entity roles (see test 3 below)
- Examples: `ownerType` in Seed, Collection, Currency; `entityType` in Divine blessings; `partyType` in Escrow

**Category B — "What game content type?"**: Game-configurable domain content codes extensible without schema changes.
- Type: Opaque `string`
- Examples: `collectionType`, `seedType`, `questType`, `encounterType`, `roomType`

**Category C — "What system state/mode?"**: Finite, system-owned behavioral/lifecycle modes.
- Type: Service-specific enum
- Examples: `constraintModel` (Inventory), `quantityModel` (Item), `bondType` (Contract), `escrowType` (Escrow)

**Decision tree** (apply tests in order, stop at first match):

| Test | Condition | Result | Example |
|------|-----------|--------|---------|
| 1 | Game designers define new values at deployment time? | Opaque `string` (Category B) | `seedType`, `collectionType` |
| 2 | L1 service would need to enumerate L2+ entity types? | Opaque `string` (hierarchy isolation) | Resource `resourceType`/`sourceType` |
| 3 | Valid values include non-entity roles? | Service-specific enum (Category A exception) | Inventory `ContainerOwnerType` includes `escrow`, `mail`, `vehicle` |
| 4 | All valid values are Bannou entity types? | `$ref: EntityType` (Category A) | Seed `ownerType`, Currency wallet owner, Divine blessing target |

**Key clarification**: Hierarchy isolation (test 2) applies ONLY when a lower-layer service would need to enumerate types from HIGHER layers. L2 services referencing entity types within L1/L2 (e.g., Currency referencing `account`, `character`, `guild`) is NOT a hierarchy violation — use `EntityType`.

---

## Tenet 20: JSON Serialization (MANDATORY)

**Rule**: All JSON serialization/deserialization MUST use `BannouJson` (from `BeyondImmersion.Bannou.Core`). Direct `JsonSerializer` use is forbidden except in unit tests specifically testing serialization behavior (with `BannouJson.Options`).

```csharp
// CORRECT
var model = BannouJson.Deserialize<MyModel>(jsonString);
var json = BannouJson.Serialize(model);
// Extension methods: jsonString.FromJson<MyModel>(), model.ToJson()

// FORBIDDEN
var model = JsonSerializer.Deserialize<MyModel>(jsonString);
```

### JsonDocument Navigation is Allowed

`JsonDocument.Parse()` and `JsonElement` navigation are acceptable for external API responses, metadata dictionaries, and JSON introspection. Only **typed model deserialization** must use BannouJson.

### Key Serialization Behaviors

| Behavior | Setting |
|----------|---------|
| **Enums** | PascalCase strings matching C# names |
| **Property matching** | Case-insensitive |
| **Null values** | Ignored when writing |
| **Numbers** | Strict parsing (no string coercion) |

---

## Tenet 21: Configuration-First Development (MANDATORY)

**Rule**: All runtime configuration MUST be defined in `schemas/{service}-configuration.yaml` and accessed through generated configuration classes. Direct `Environment.GetEnvironmentVariable` is forbidden except for documented exceptions.

### Requirements

1. **Define in Schema**: All configuration in `schemas/{service}-configuration.yaml`
2. **Use Injected Configuration**: Access via `{Service}ServiceConfiguration` class
3. **Fail-Fast Required Config**: Required values without defaults MUST throw at startup
4. **No Hardcoded Credentials**: Never fall back to hardcoded credentials or connection strings
5. **Use AppConstants**: Shared defaults use `AppConstants` constants
6. **No Dead Configuration**: Every defined config property MUST be referenced in the plugin (service, cache, provider, worker, etc.)
7. **No Hardcoded Tunables**: Any limit, timeout, threshold, or capacity MUST be a configuration property
8. **Use Defined Infrastructure**: If `schemas/state-stores.yaml` defines a cache store for the service, implement cache read-through using that store
9. **No Secondary Fallbacks**: If a config property has a schema default, NEVER add `??` fallback in code. The default exists in the generated class. If it's null, that's a critical infrastructure failure - throw, don't mask it.

```csharp
// FORBIDDEN: Hardcoded tunables
var maxResults = Math.Min(body.Limit, 1000);           // Define MaxResultsPerQuery in config
await Task.Delay(TimeSpan.FromSeconds(30));            // Define RetryDelaySeconds in config

// CORRECT: All tunables from configuration
var maxResults = Math.Min(body.Limit, _configuration.MaxResultsPerQuery);
```

**Mathematical constants** (epsilon, golden ratio, bits-per-byte) are NOT tunables and are acceptable as hardcoded values.

**Stub scaffolding**: Config properties for unimplemented features may exist unreferenced if documented in the plugin's Stubs section.

**Defined state stores**: If `schemas/state-stores.yaml` defines a Redis cache for your service, implement read-through caching. If genuinely unnecessary, remove it from the schema.

### Allowed Exceptions (4 Categories)

Document with code comments explaining the exception:

1. **Assembly Loading Control**: `*_SERVICE_ENABLED` in `PluginLoader.cs`/`IBannouService.cs` (required before DI container available; master kill switch `BANNOU_SERVICES_ENABLED` reads from `Program.Configuration`)
2. **Test Harness Control**: `DAEMON_MODE`, `PLUGIN` in test projects
3. **Orchestrator Environment Forwarding**: `Environment.GetEnvironmentVariables()` in `OrchestratorService.cs` (forwards config to deployed containers via strict whitelist)
4. **Integration Test Runners**: `BANNOU_HTTP_ENDPOINT`, `BANNOU_APP_ID` in `http-tester/`/`edge-tester/` (standalone harnesses without DI access, use `AppConstants.ENV_*`)

```csharp
// CORRECT: Injected config with fail-fast
_connectionString = config.ConnectionString
    ?? throw new InvalidOperationException("SERVICE_CONNECTION_STRING required");

// FORBIDDEN
Environment.GetEnvironmentVariable("...");  // Use config class
?? "amqp://guest:guest@localhost";          // Masks config issues
```

---

## Tenet 23: Async Method Pattern (MANDATORY)

**Rule**: All methods returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` MUST use `async` and contain at least one `await`. Non-async methods returning these types have different exception handling (synchronous throw vs captured in task), incomplete stack traces, and broken `using` semantics. This applies equally to `ValueTask` variants — `ValueTask.FromResult` in a non-async method has the same problems as `Task.FromResult`.

```csharp
// CORRECT
public async Task<AccountResponse> GetAccountAsync(Guid accountId, CancellationToken ct)
{
    var account = await _stateStore.GetAsync($"account:{accountId}", ct);
    return MapToResponse(account);
}

// WRONG: Blocks thread, wrong exception semantics
public Task<AccountResponse> GetAccountAsync(Guid accountId)
{
    var account = _stateStore.GetAsync($"account:{accountId}").Result; // BLOCKS!
    return Task.FromResult(MapToResponse(account));
}

// WRONG: Exceptions thrown before the return propagate synchronously
public ValueTask<ActionResult> ExecuteAsync(ActionNode action, CancellationToken ct)
{
    var param = GetParam(action) ?? throw new InvalidOperationException("missing"); // Synchronous throw!
    DoWork(param);
    return ValueTask.FromResult(ActionResult.Continue);
}

// CORRECT: async ensures exceptions are captured in the ValueTask
public async ValueTask<ActionResult> ExecuteAsync(ActionNode action, CancellationToken ct)
{
    var param = GetParam(action) ?? throw new InvalidOperationException("missing"); // Captured in ValueTask
    DoWork(param);
    await Task.CompletedTask;
    return ActionResult.Continue;
}
```

### Synchronous Implementation of Async Interface

When implementing an async interface with synchronous logic, use `await Task.CompletedTask`:

```csharp
public async Task DoWorkAsync()
{
    _logger.LogInformation("Working");
    await Task.CompletedTask;
}
```

---

## Tenet 24: Using Statement Pattern (MANDATORY)

**Rule**: All disposable objects with method-scoped lifetimes MUST use `using` statements. Manual `.Dispose()` misses exception paths and leaks resources.

```csharp
// CORRECT
using var connection = await _connectionFactory.CreateAsync(ct);
await connection.SendAsync(data, ct);

// WRONG: If SendAsync throws, connection leaks
var connection = await _connectionFactory.CreateAsync(ct);
await connection.SendAsync(data, ct);
connection.Dispose();
```

### When Manual Dispose is Acceptable

Only when disposal scope extends beyond the creating method:

1. **Class-owned resources** - Fields disposed in the class's `Dispose()` method
2. **Conditional ownership transfer** - Resource sometimes returned to caller
3. **Async disposal constraints** - When `await using` is impossible due to framework limitations

Enforced via `CA2000` (warning) and `IDE0063` (suggestion) in `.editorconfig`.

---

## Tenet 25: Type Safety Across All Models (MANDATORY)

**Rule**: ALL models (requests, responses, events, configuration, internal POCOs) MUST use the strongest available C# type. String representations of typed values are **forbidden**.

### There Are No "JSON Boundaries"

"Strings are needed because JSON is involved" is FALSE. NSwag generates typed models from schemas with enum properties. Configuration generator creates enum properties from YAML. Event schemas define enum types. BannouJson handles all serialization automatically. By the time your service method receives a request, it's already a fully-typed C# object.

### Requirements

1. **Request/Response/Event models**: Generated with proper enum types by NSwag
2. **Configuration classes**: Generated with proper enum types
3. **Internal POCOs**: MUST mirror types from generated models (enum→enum, not enum→string)
4. **GUIDs**: Always `Guid`, never `string`
5. **Dates**: Always `DateTimeOffset`, never `string`
6. **No Enum.Parse in business logic**: If you're parsing enums, your model definition is wrong

```csharp
// CORRECT: Typed throughout the entire flow
var model = new ItemTemplateModel
{
    TemplateId = Guid.NewGuid(),
    Category = body.Category,        // ItemCategory enum -> ItemCategory enum
    Rarity = body.Rarity,            // ItemRarity enum -> ItemRarity enum
};

// FORBIDDEN
public string Category { get; set; } = string.Empty;  // Use ItemCategory enum
Category = body.Category.ToString();                    // Assign enum directly
if (model.Status == "active") { ... }                   // Use enum equality
var rarity = Enum.Parse<ItemRarity>(someString);        // Model is wrong
public string OwnerId { get; set; } = string.Empty;    // Use Guid
```

### Acceptable String Conversions (3 Cases Only)

1. **State Store Set APIs**: Generic `TItem` parameters serialize via BannouJson automatically - `Guid`, `enum`, etc. work directly.

2. **External Third-Party APIs**: Parsing responses from Steam, Discord, payment processors that we don't control. Does NOT apply to Bannou-to-Bannou calls.

3. **Intentionally Generic Services (Hierarchy Isolation)**: Lower-layer services that must NOT enumerate higher-layer types use opaque string identifiers. See T14 above for the authoritative decision tree (test 2: "L1 service would need to enumerate L2+ entity types?"). Does NOT apply to L2 services referencing L1/L2 entity types — those MUST use `EntityType`.

```csharp
// ACCEPTABLE: lib-resource (L1) generated model uses strings to avoid enumerating L2+ services
// (Generated code — NSwag initializes required strings per QUALITY TENETS T22 NSwag exception)
public class RegisterReferenceRequest
{
    /// <summary>Opaque resource type identifier — caller provides.</summary>
    public string ResourceType { get; set; }
    /// <summary>Opaque source type identifier — caller provides.</summary>
    public string SourceType { get; set; }
}

// NOT ACCEPTABLE: L2 service using string for entity types within L1/L2
// Seed (L2) referencing account, character, faction — all L1/L2 entities
// → Use EntityType enum, not string
```

4. **Client-Opaque Metadata (T29 Exemption)**: Schema fields with `additionalProperties: true` that represent client-only metadata bags (per T29) are correctly generated as `object?` by NSwag. This is the **right type** — do NOT narrow it to `JsonElement?`, `Dictionary<string, object>?`, or any other type.

```csharp
// CORRECT: Client metadata is opaque pass-through — object? is the right type
public object? Metadata { get; set; }  // additionalProperties: true in schema

// FORBIDDEN: Type-narrowing client metadata violates T29
public JsonElement? Metadata { get; set; }         // Implies inspection
public Dictionary<string, object>? Metadata { get; set; }  // Implies structure

// FORBIDDEN: Inspecting, converting, or pattern-matching client metadata
Metadata = metadata is JsonElement je ? je : null;  // Service must not inspect
var dict = BannouJson.Deserialize<Dictionary<string, object>>(metadata);  // No
```

**Why this matters**: T29 says `additionalProperties: true` is NEVER a data contract between services. Client metadata is stored and returned unchanged — the service MUST NOT inspect, type-narrow, pattern-match, or make any assumption about its structure. Applying T25 type-narrowing to these fields actively violates T29 by implying the service understands the data's shape.

**When to apply**: Field is `additionalProperties: true` in schema + described as client/caller metadata + no Bannou plugin reads specific keys from it by convention.

Tests follow the same rules. `DeploymentMode = "bannou"` in a test is wrong - use `DeploymentMode.Bannou`.

---

## Tenet 26: No Sentinel Values (MANDATORY)

**Rule**: Never use "magic values" to represent absence. If a value can be absent, make it nullable.

Sentinel values (`Guid.Empty` for "none", `-1` for "no index", `string.Empty` for "absent", `DateTime.MinValue` for "not set") circumvent NRT, hide bugs that compile silently, create ambiguity, and break JSON serialization semantics (`null` means absent; an empty GUID is a value).

```csharp
// CORRECT: Nullable types - compiler-enforced, unambiguous
public Guid? ContainerId { get; set; }          // null = no container
public int? SlotIndex { get; set; }              // null = no slot
public DateTimeOffset? ExpiresAt { get; set; }   // null = never expires

// FORBIDDEN: Sentinel values
public Guid ContainerId { get; set; }            // Guid.Empty = "none"? Bug? Uninitialized?
public int SlotIndex { get; set; } = -1;         // -1 = "no slot"
```

### Schema & Model Consistency

If a field can be absent, the schema MUST declare `nullable: true`, and internal storage models MUST also use nullable types:

```yaml
containerId:
  type: string
  format: uuid
  nullable: true
  description: Container holding this item, null if unplaced
```

```csharp
// Internal model MUST match schema nullability
internal class ItemInstanceModel
{
    public Guid? ContainerId { get; set; }  // Nullable - matches schema
}
```

### Migration Path

For existing sentinel values: update schema to nullable → regenerate → update POCOs → replace sentinel comparisons with `HasValue`/null checks → migrate stored data.

---

## Quick Reference

For the consolidated violations table covering all implementation tenets, see [TENETS.md Quick Reference: Common Violations](../TENETS.md#quick-reference-common-violations). Schema-related violations are covered in [SCHEMA-RULES.md](../SCHEMA-RULES.md).

---

*This document covers tenets T14, T20, T21, T23, T24, T25, T26. See [TENETS.md](../TENETS.md) for the complete index and [IMPLEMENTATION-BEHAVIOR.md](IMPLEMENTATION-BEHAVIOR.md) for service behavior & contracts tenets (T3, T7, T8, T9, T17, T30, T31).*
