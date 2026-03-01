# Bannou Service Development Tenets

> **Version**: 9.1
> **Last Updated**: 2026-02-28
> **Scope**: All Bannou microservices and related infrastructure

This document is the authoritative index for Bannou development standards. All service implementations, tests, and infrastructure MUST adhere to these tenets. Tenets must not be changed or added without EXPLICIT approval, without exception.

> **AI ASSISTANTS**: All tenets apply with heightened scrutiny to AI-generated code and suggestions. AI assistants MUST NOT bypass, weaken, or work around any tenet without explicit human approval. This includes modifying tests to pass with buggy implementations, adding fallback mechanisms, or any other "creative solutions" that violate the spirit of these tenets.
>
> **Equally forbidden**: (1) **False tenet citation** — attributing a requirement to a specific tenet when that tenet does not actually require it. If you claim code is "per T7" or "per IMPLEMENTATION TENETS," you MUST be able to point to the specific rule in that tenet that mandates it. Fabricating tenet requirements poisons the codebase with phantom obligations that mislead future developers. (2) **Existing violations as precedent** — "the existing code also violates this" is NEVER a justification for a new violation. Existing violations are tech debt to be tracked and fixed, not patterns to replicate. Each new line of code must comply with the tenets as written, regardless of what surrounds it. (3) **Inventing exceptions** — if a tenet does not define an exception, carve-out, or judgment call for a specific situation, then none exists. You may NOT declare a violation to be a "false positive," "acceptable," "borderline," or a "judgment call" based on circumstances the tenet does not address. If T8 says "echoed request fields are forbidden" and does not say "except for complex batch operations," then complex batch operations are not an exception. If T6 shows constructor-cached store references and does not list cases where inline calls are acceptable, then inline calls are not acceptable. When you believe a tenet should have an exception it doesn't currently have, you MUST present the conflict to the user and wait for direction — you do not have authority to create exceptions.

---

## Tenet 0: Never Reference Tenet Numbers in Source Code

When documenting tenet compliance in source code comments, **NEVER use specific tenet numbers** (e.g., "T9", "Tenet 21", "TENET T4"). Tenet numbers change over time as tenets are added, removed, or reorganized.

**Instead, use category names:**
- `FOUNDATION TENETS` - for T4, T5, T6, T13, T15, T18, T27, T28, T29
- `IMPLEMENTATION TENETS` - for T3, T7, T8, T9, T14, T17, T20, T21, T23, T24, T25, T26, T30, T31
- `QUALITY TENETS` - for T10, T11, T12, T16, T19, T22
- `SERVICE HIERARCHY` - for Tenet 2 (service layer dependencies)

> **Note**: Tenets 1 and 2 are special - they reference external documents ([SCHEMA-RULES.md](SCHEMA-RULES.md) and [SERVICE-HIERARCHY.md](SERVICE-HIERARCHY.md)) for detailed rules. In source code, reference them as `FOUNDATION TENETS` or by their specific names ("per schema-first development", "per service hierarchy").

**Examples:**
```csharp
// WRONG: Uses specific tenet number (will become stale)
// Use distributed state per T9
// Tenet 21 compliant

// CORRECT: Uses category name (stable)
// Use distributed state per IMPLEMENTATION TENETS
// IMPLEMENTATION TENETS compliant
```

This rule applies to: source code comments, schema descriptions, generator scripts, and any non-documentation files. The tenet definition documents (`docs/reference/tenets/*.md`) are the only place where specific numbers should appear.

---

## Tenet 1: Schema-First Development (INVIOLABLE)

**All service code is generated from OpenAPI schemas. This rule is inviolable.**

Before creating or modifying ANY schema file, you MUST read [SCHEMA-RULES.md](SCHEMA-RULES.md). This is not optional.

### Generated File Structure

```
schemas/                              # SOURCE OF TRUTH - edit these
├── {service}-api.yaml               # API endpoints with x-permissions
├── {service}-events.yaml            # Service events with x-lifecycle, x-event-subscriptions
├── {service}-configuration.yaml     # Configuration with x-service-configuration
├── {service}-client-events.yaml     # Server→client WebSocket events
├── common-api.yaml                  # System-wide shared types
├── common-events.yaml               # Base event schemas
├── state-stores.yaml                # State store definitions
└── Generated/                       # NEVER EDIT - auto-generated
    └── {service}-lifecycle-events.yaml

plugins/lib-{service}/
├── Generated/                       # NEVER EDIT - regenerated on make generate
│   ├── {Service}Controller.cs       # HTTP routing (from api.yaml)
│   ├── {Service}Controller.Meta.cs  # Runtime schema introspection
│   ├── I{Service}Service.cs         # Service interface
│   ├── {Service}ServiceConfiguration.cs  # Configuration class
│   ├── {Service}PermissionRegistration.cs
│   ├── {Service}EventsController.cs # Event subscription handlers
│   └── {Service}ClientEventsModels.cs    # Client event models (if applicable)
├── {Service}Service.cs              # MANUAL - business logic only
├── {Service}ServiceEvents.cs        # MANUAL - event handler implementations
└── Services/                        # MANUAL - optional helper services

bannou-service/Generated/
├── Models/{Service}Models.cs        # Request/response models (use make print-models to inspect)
├── Clients/{Service}Client.cs       # Service client for mesh calls
└── Events/{Service}EventsModels.cs  # Service event models
```

> **Model Inspection**: Use `make print-models PLUGIN="service"` to view compact model shapes instead of reading generated files directly. If print-models fails, generate first — never guess at model definitions.

### The Cardinal Rules

1. **NEVER edit files in `*/Generated/` directories** - they are overwritten on regeneration
2. **ALWAYS define contracts in schemas first** - then generate code
3. **ALWAYS read [SCHEMA-RULES.md](SCHEMA-RULES.md) before touching schemas** - it covers NRT compliance, validation keywords, extension attributes, and reference hierarchies
4. **Run `make generate` after schema changes** - then verify the build passes

### Generation Command

```bash
make generate                        # Full generation pipeline
scripts/generate-all-services.sh     # Alternative direct invocation
```

### Allowed Exceptions (Shared Specifications & SDKs)

Schema-first applies to **HTTP APIs and service contracts**. Some components are exempt because they represent **shared specifications** or **standalone SDKs** that plugins consume rather than define:

| Exception | What It Is | Why Exempt |
|-----------|-----------|------------|
| **WebSocket Binary Protocol** | 31-byte header format for client↔server routing | Binary wire protocol shared with game clients; defined in protocol spec, not OpenAPI |
| **ABML Bytecode Format** | Stack-based bytecode for behavior models | Compiler output consumed by interpreters; format spec is the contract |
| **Asset Bundle Format** | `.bannou` archive format with LZ4 compression | Binary format shared with game dev tools; clients create bundles independently |
| **Music SDKs** | `MusicTheory` and `MusicStoryteller` libraries | Standalone computation libraries; plugins use them, they don't define plugin APIs |
| **Runtime Interpreters** | `BehaviorModelInterpreter`, `CinematicInterpreter` | Execute compiled artifacts; analogous to JVM/CLR - not generated from schemas |

**Key Principle**: These are like the JVM or .NET CLR - they execute or consume artifacts but aren't themselves generated from schemas. The specification document (protocol spec, format spec, SDK documentation) serves as the contract.

**When to use an exception**:
- You're defining a **binary format** shared with clients (not an HTTP API)
- You're building a **runtime/interpreter** that executes compiled output
- You're creating a **standalone SDK** that plugins consume (inverted dependency)

**When NOT to use an exception**:
- HTTP endpoints → use OpenAPI schemas
- Service-to-service events → use event schemas
- Configuration → use configuration schemas
- Request/response models → use API schemas

---

## Tenet 2: Service Hierarchy (INVIOLABLE)

**Services are organized into layers. Dependencies may only flow downward.**

Before adding ANY service client dependency, you MUST read [SERVICE-HIERARCHY.md](SERVICE-HIERARCHY.md). This is not optional.

### The Hierarchy Layers

```
Layer 4: Application Services (actor, behavior, mapping, scene, etc.)
Layer 3: Extended Services (character-personality, character-history, etc.)
Layer 2: Foundational Services (account, auth, character, realm, etc.)
Layer 1: Observability (telemetry, orchestrator, analytics) - OPTIONAL
Layer 0: Infrastructure (lib-state, lib-messaging, lib-mesh) - ALWAYS ON
```

### The Cardinal Rule

> **A service may ONLY depend on services in its own layer or lower layers. Dependencies on higher layers are FORBIDDEN.**

### Why This Matters

- **Layer 2 services are foundations** - they don't know about their consumers
- **Layer 1 services are optional** - nothing breaks if they're disabled
- **Higher layers extend lower layers** - not the other way around

### Common Violation Pattern

```csharp
// FORBIDDEN: Foundation service depending on extension service
public class CharacterService  // Layer 2
{
    private readonly IActorClient _actorClient;  // Layer 4 - VIOLATION!
}

// CORRECT: Extension service depending on foundation
public class ActorService  // Layer 4
{
    private readonly ICharacterClient _characterClient;  // Layer 2 - OK
}
```

### Reference Counting the Right Way

If a foundational service needs to know about references from higher layers (for cleanup eligibility), higher-layer services call **lib-resource's API directly** to register/unregister references, and lib-resource coordinates cleanup with policies (CASCADE/RESTRICT/DETACH). See [SERVICE-HIERARCHY.md](SERVICE-HIERARCHY.md) for the full pattern and T27/T28 in [FOUNDATION.md](tenets/FOUNDATION.md) for the communication and cleanup rules.

---

## Tenet Categories

Tenets are organized into categories based on when they're needed:

| Category | Tenets | When to Reference |
|----------|--------|-------------------|
| [**Schema Rules**](SCHEMA-RULES.md) | Tenet 1 | Before creating or modifying any schema file |
| [**Service Hierarchy**](SERVICE-HIERARCHY.md) | Tenet 2 | Before adding any service client dependency |
| [**Foundation**](tenets/FOUNDATION.md) | T4, T5, T6, T13, T15, T18, T27, T28, T29 | Before starting any new service or feature |
| [**Implementation: Behavior**](tenets/IMPLEMENTATION-BEHAVIOR.md) | T3, T7, T8, T9, T17, T30, T31 | While designing service method behavior |
| [**Implementation: Data**](tenets/IMPLEMENTATION-DATA.md) | T14, T20, T21, T23, T24, T25, T26 | While writing code and modeling data |
| [**Quality**](tenets/QUALITY.md) | T10, T11, T12, T16, T19, T22 | During code review or before PR submission |

> **Note**: Tenets 1 and 2 reference standalone documents (SCHEMA-RULES.md and SERVICE-HIERARCHY.md) that contain their own detailed rules.

---

## Foundation Tenets (Architecture & Design)

*Full details: [tenets/FOUNDATION.md](tenets/FOUNDATION.md)*

| # | Name | Core Rule |
|---|------|-----------|
| **T4** | Infrastructure Libs Pattern | MUST use lib-state, lib-messaging, lib-mesh; direct DB/queue access forbidden; L0/L1/L2 dependencies are hard (fail at startup) |
| **T5** | Event-Driven Architecture | All state changes publish typed events; no anonymous objects |
| **T6** | Service Implementation Pattern | Partial class structure with standardized dependencies |
| **T13** | X-Permissions Usage | All endpoints declare x-permissions; enforced for WebSocket clients |
| **T15** | Browser-Facing Endpoints | GET/path-params only for OAuth, Website, WebSocket upgrade (exceptional) |
| **T18** | Licensing Requirements | MIT/BSD/Apache only; GPL forbidden for linked code |
| **T27** | Cross-Service Communication Discipline | Direct API for higher→lower; DI interfaces for lower↔higher; events for broadcast only; inverted subscriptions forbidden |
| **T28** | Resource-Managed Cleanup | Dependent data cleanup via lib-resource only; never subscribe to lifecycle events for destruction; Account exempt for privacy |
| **T29** | No Metadata Bag Contracts | `additionalProperties: true` is NEVER a data contract between services; metadata bags are client-only; services own their own domain data in their own schemas |

---

## Implementation Tenets: Service Behavior & Contracts

*Full details: [tenets/IMPLEMENTATION-BEHAVIOR.md](tenets/IMPLEMENTATION-BEHAVIOR.md)*

| # | Name | Core Rule |
|---|------|-----------|
| **T3** | Event Consumer Fan-Out | Use IEventConsumer for multi-plugin event handling |
| **T7** | Error Handling | Generated controller provides catch-all boundary (do not duplicate in service methods); ApiException catch only for inter-service calls; service try-catch only for specific recovery logic; TryPublishErrorAsync; instance identity from IMeshInstanceIdentifier only |
| **T8** | Return Pattern | All methods return `(StatusCodes, TResponse?)` tuples; null payload for errors; no filler properties in success responses |
| **T9** | Multi-Instance Safety | No in-memory authoritative state; use distributed locks |
| **T17** | Client Event Schema Pattern | Use IClientEventPublisher for WebSocket push; not IMessageBus |
| **T30** | Telemetry Span Instrumentation | All async methods get `StartActivity` spans; zero-signature-change via `Activity.Current` ambient context |
| **T31** | Deprecation Lifecycle | Two categories (definitions vs templates); idempotent deprecation; standardized storage/events/behavior; no deprecation on instances |

---

## Implementation Tenets: Data Modeling & Code Discipline

*Full details: [tenets/IMPLEMENTATION-DATA.md](tenets/IMPLEMENTATION-DATA.md)*

| # | Name | Core Rule |
|---|------|-----------|
| **T14** | Polymorphic Associations | Entity ID + Type columns; composite string keys |
| **T20** | JSON Serialization | Always use BannouJson; never direct JsonSerializer |
| **T21** | Configuration-First | Use generated config classes; no dead config; no hardcoded tunables |
| **T23** | Async Method Pattern | Task-returning methods must be async with await |
| **T24** | Using Statement Pattern | Use `using` for disposables; manual Dispose only for class-owned resources |
| **T25** | Type Safety Across All Models | ALL models use proper types (enums, Guids); "JSON requires strings" is FALSE |
| **T26** | No Sentinel Values | Never use magic values (Guid.Empty, -1, empty string) for absence; use nullable types |

---

## Quality Tenets (Standards & Verification)

*Full details: [tenets/QUALITY.md](tenets/QUALITY.md)*

| # | Name | Core Rule |
|---|------|-----------|
| **T10** | Logging Standards | Structured logging with message templates; no [TAGS] or emojis |
| **T11** | Testing Requirements | Three-tier testing: unit, HTTP integration, WebSocket edge |
| **T12** | Test Integrity | Never weaken tests to pass; failing test = fix implementation |
| **T16** | Naming Conventions | Consistent patterns for methods, models, events, topics |
| **T19** | XML Documentation | All public APIs documented with summary, params, returns |
| **T22** | Warning Suppression | Forbidden except Moq/NSwag/enum exceptions; fix warnings, don't hide them |

---

## Quick Reference: Common Violations

| Violation | Tenet | Fix |
|-----------|-------|-----|
| Using "T9" or "Tenet 21" in source code | T0 | Use category name: FOUNDATION/IMPLEMENTATION/QUALITY TENETS |
| Editing Generated/ files | T1 | Edit schema, regenerate (see [SCHEMA-RULES.md](SCHEMA-RULES.md)) |
| Missing `description` on schema property | T1 | Add description field (causes CS1591) |
| Wrong env var format (`JWTSECRET`) | T1 | Use `{SERVICE}_{PROPERTY}` pattern (see [SCHEMA-RULES.md](SCHEMA-RULES.md)) |
| Missing `env:` key in config schema | T1 | Add explicit `env:` with proper naming |
| Missing service prefix (`REDIS_CONNECTION_STRING`) | T1 | Add prefix (e.g., `STATE_REDIS_CONNECTION_STRING`) |
| Hyphen in env var prefix (`GAME-SESSION_`) | T1 | Use underscore (`GAME_SESSION_`) |
| Shared type defined in events schema | T1 | Move type to `-api.yaml`, use `$ref` in events |
| API schema `$ref` to events schema | T1 | Reverse the dependency - API is source of truth |
| Events `$ref` to different service's API | T1 | Use common schema or duplicate the type |
| Layer 2 service depending on Layer 3 | T2 | Remove dependency; use DI Provider/Listener interfaces (see T27) |
| Layer 2 service depending on Layer 4 | T2 | Remove dependency; use DI Provider/Listener interfaces (see T27) |
| Foundation service calling extension client | T2 | Invert with DI interfaces; extension implements provider, foundation discovers via `IEnumerable<T>` |
| Circular service dependencies | T2 | Restructure to respect layer hierarchy |
| Direct Redis/MySQL connection | T4 | Use IStateStoreFactory via lib-state |
| Direct RabbitMQ connection | T4 | Use IMessageBus via lib-messaging |
| Direct HTTP service calls | T4 | Use generated clients via lib-mesh |
| Graceful degradation for L0/L1/L2 dependency | T4 | Use constructor injection; see SERVICE-HIERARCHY.md |
| Anonymous event objects | T5 | Define typed event in schema |
| Manually defining lifecycle events | T5 | Use `x-lifecycle` in events schema |
| Service class missing `partial` | T6 | Add `partial` keyword |
| Missing x-permissions on endpoint | T13 | Add to schema (even if empty array) |
| GPL library in NuGet package | T18 | Use MIT/BSD alternative |
| Missing event consumer registration | T3 | Add RegisterEventConsumers call |
| Adding top-level try-catch to service endpoint methods | T7 | Generated controller already provides catch-all boundary with logging, error events, and 500 response; do not duplicate |
| Generic catch returning 500 in service method | T7 | Let it propagate to the generated controller; only catch for specific recovery logic or inter-service `ApiException` |
| Using IErrorEventEmitter | T7 | Use IMessageBus.TryPublishErrorAsync instead |
| Emitting error events for user errors | T7 | Only emit for unexpected/internal failures |
| Constructing `ServiceErrorEvent` directly | T7 | Use `TryPublishErrorAsync`; only `RabbitMQMessageBus` constructs the event |
| Passing instance ID to `TryPublishErrorAsync` | T7 | Instance identity injected internally from `IMeshInstanceIdentifier` |
| Using `Guid.NewGuid()` or fixed string for error event `ServiceId` | T7 | `ServiceId` comes from `IMeshInstanceIdentifier` (process-stable) |
| Using Microsoft.AspNetCore.Http.StatusCodes | T8 | Use BeyondImmersion.BannouService.StatusCodes |
| Success boolean in response (`locked: true`, `deleted: true`) | T8 | Remove from schema; 200 OK already confirms success |
| Confirmation message string in response | T8 | Remove from schema; status code communicates result |
| Action timestamp in response (`executedAt`, `registeredAt`) | T8 | Remove unless it represents stored entity state |
| Request field echoed back in response | T8 | Remove from schema; caller already knows what they sent |
| Observability metrics in non-diagnostics response | T8 | Remove or move to dedicated diagnostics endpoint |
| Plain Dictionary for cache | T9 | Use ConcurrentDictionary |
| Per-instance salt/key generation | T9 | Use shared/deterministic values |
| Wrong exchange for client events | T17 | Use IClientEventPublisher, not IMessageBus |
| Direct `JsonSerializer` usage | T20 | Use `BannouJson.Serialize/Deserialize` |
| Direct `Environment.GetEnvironmentVariable` | T21 | Use service configuration class |
| Hardcoded credential fallback | T21 | Remove default, require configuration |
| Unused configuration property | T21 | Wire up in service or remove from schema |
| Hardcoded magic number for tunable | T21 | Define in configuration schema, use config |
| Defined cache store not used | T21 | Implement cache read-through or remove store |
| Secondary fallback for defaulted config property | T21 | Remove fallback; if null, throw (infrastructure failure) |
| Non-async Task-returning method | T23 | Add `async` keyword and `await Task.CompletedTask` if no other await exists |
| Non-async ValueTask-returning method | T23 | Add `async` keyword; return value directly instead of `ValueTask.FromResult` |
| `Task.FromResult` without async | T23 | Use `async` method with `await Task.CompletedTask` |
| `ValueTask.FromResult` without async | T23 | Use `async` method with `await Task.CompletedTask` |
| `.Result` or `.Wait()` on Task | T23 | Use await instead |
| Manual `.Dispose()` in method scope | T24 | Use `using` statement instead |
| try/finally for disposal | T24 | Use `using` statement instead |
| String field for enum in ANY model | T25 | Use the generated enum type |
| String field for GUID in ANY model | T25 | Use `Guid` type |
| `Enum.Parse` anywhere in service code | T25 | Your model definition is wrong - fix it |
| `.ToString()` when assigning enum | T25 | Assign enum directly |
| String comparison for enum value | T25 | Use enum equality operator |
| Claiming "JSON requires strings" | T25 | FALSE - BannouJson handles serialization |
| String in request/response/event model | T25 | Schema should define enum type |
| String in configuration class | T25 | Config schema should define enum type |
| Type-narrowing `object?` on `additionalProperties: true` field | T25, T29 | Keep `object?`; client metadata is opaque pass-through; do not inspect |
| `JsonElement?` for client metadata field | T25, T29 | Use `object?`; type-narrowing implies inspection which violates T29 |
| Pattern-matching or casting client metadata (`is JsonElement`) | T25, T29 | Store and return unchanged; service must not inspect structure |
| String `ownerType`/`entityType` for L2+ entity references | T14, T25 | Use `$ref: EntityType` — hierarchy isolation only applies to L1 enumerating L2+ types |
| Service-specific enum duplicating EntityType values | T14 | Use `$ref: EntityType` unless valid set includes non-entity roles (see T14 decision tree) |
| L2 service using opaque string for entity types within L1/L2 | T14, T25 | EntityType is appropriate — hierarchy isolation does not apply within same layer or lower |
| Using `Guid.Empty` to mean "none" | T26 | Make field `Guid?` nullable |
| Using `-1` to mean "no index" | T26 | Make field `int?` nullable |
| Using empty string for "absent" | T26 | Make field `string?` nullable |
| Non-nullable model field when value can be absent | T26 | Make field nullable, update schema |
| `[TAG]` prefix in logs | T10 | Remove brackets, use structured logging |
| String interpolation in log messages | T10 | Use message templates with named placeholders |
| Logging passwords/tokens/PII | T10 | Redact or log length only |
| Emojis in log messages | T10 | Plain text only (scripts excepted) |
| HTTP fallback in tests | T12 | Remove fallback, fix root cause |
| Changing test to pass with buggy impl | T12 | Keep test, fix implementation |
| Using `null!` to test non-nullable params | T12 | Remove test - tests impossible scenario |
| Adding null checks for NRT-protected params | T12 | Don't add - NRT provides compile-time safety |
| Wrong naming pattern (method, model, event, topic) | T16 | Follow category-specific pattern in T16 |
| Service name embedded in event topic entity via hyphens (Pattern B) | T16 | Use dot-separated namespace: `transit-connection.created` → `transit.connection.created` (Pattern C) |
| Underscores in event topic strings | T16 | Use kebab-case: `currency.exchange_rate.updated` → `currency.exchange-rate.updated` |
| Client event model missing `ClientEvent` suffix | T16 | Use `{Entity}{Action}ClientEvent` to avoid collision with service event names |
| Missing XML documentation | T19 | Add `<summary>`, `<param>`, `<returns>` |
| Missing env var in config XML doc | T19 | Document environment variable in summary |
| `#pragma warning disable` without exception | T22 | Fix the warning instead of suppressing |
| Blanket GlobalSuppressions.cs | T22 | Remove file, fix warnings individually |
| Suppressing CS8602/CS8603/CS8604 in non-generated | T22 | Fix the null safety issue |
| CS1591 warning on schema property/class | T1, T22 | Add `description` to schema (enums auto-suppressed) |
| Service is `x-references` target but doesn't call `ExecuteCleanupAsync` | T1 | Add Resource cleanup to delete flow (see SCHEMA-RULES.md) |
| Event handlers duplicate `x-references` cleanup callbacks | T1 | Remove event handlers; use Resource pattern only |
| Adding event cleanup when `x-references` callbacks exist | T1 | Fix producer to call `ExecuteCleanupAsync` instead |
| Using `additionalProperties: true` as cross-service data contract | T29 | Owning service defines its own schema, stores its own data |
| Reading metadata keys from another service's response by convention | T29 | Query the service that owns the domain concept via API |
| Storing higher-layer domain data in lower-layer metadata bags | T29 | Higher-layer service owns binding table, references lower-layer entity by ID |
| Documentation specifying "put X in service Y's metadata" | T29 | X belongs in the schema of the service that owns concept X |
| Publishing event to lower-layer's topic instead of calling API | T27 | Use generated client directly (hierarchy permits the call) |
| Lower-layer subscribing to higher-layer events | T27 | Use DI Provider/Listener interface in `bannou-service/Providers/` |
| Publishing registration events at startup | T27 | Use DI Provider interface discovered via `IEnumerable<T>` |
| Defining event schema to receive data from callers | T27 | Remove event; expose API endpoint; callers use generated client |
| Lower-layer caching higher-layer data and subscribing to invalidation events | T27 | Provider owns its cache; lower layer calls provider interface for fresh data |
| Subscribing to `*.deleted` for dependent data cleanup | T28 | Register with lib-resource; implement cleanup callback via `ISeededResourceProvider` |
| Event-based cleanup for persistent dependent data | T28 | Use lib-resource with CASCADE/RESTRICT/DETACH policy |
| Cleanup handler in `*ServiceEvents.cs` for another service's entity | T28 | Move to lib-resource cleanup callback; remove event subscription |
| Async helper method without `StartActivity` span | T30 | Add `using var activity = _telemetryProvider.StartActivity(...)` |
| Manually adding spans to generated code | T30 | Add to code generation templates, not generated files |
| Missing `ITelemetryProvider` in helper service constructor | T30 | Add constructor parameter for span creation |
| Span on non-async synchronous method | T30 | Only async methods need spans |
| Span name not following `{component}.{class}.{method}` pattern | T30 | Use `"bannou.{service}", "{Class}.{Method}"` format |
| Adding deprecation to instance data (characters, sessions, etc.) | T31 | Use immediate hard delete; deprecation is for definitions only |
| Bare boolean for deprecation (no timestamp or reason) | T31 | Use triple-field model: `IsDeprecated`, `DeprecatedAt`, `DeprecationReason` |
| Missing `DeprecationReason` on Category A entity | T31 | Add `DeprecationReason` field; audit context is mandatory for world-building definitions |
| Non-idempotent deprecation (returning Conflict) | T31 | Return `OK` when already deprecated; caller's intent is satisfied |
| Non-idempotent undeprecation (returning BadRequest/Conflict) | T31 | Return `OK` when not deprecated (Category A only) |
| Delete without requiring deprecation (Category A) | T31 | Reject delete with `BadRequest` if `IsDeprecated == false` |
| Dedicated deprecation event (`item-template.deprecated`) | T31 | Use `*.updated` event with `changedFields` containing deprecation fields |
| Missing `includeDeprecated` on list endpoint | T31 | Add parameter with `default: false` to all list/query endpoints |
| Undeprecate endpoint on Category B entity | T31 | Remove; Category B deprecation is one-way |
| Delete endpoint on Category B entity | T31 | Remove; Category B templates persist forever |
| Not checking deprecation before creating referencing entity | T31 | Check target's `Exists` or deprecation status; reject with `BadRequest` if deprecated |
| Category B entity missing instance creation guard | T31 | Check `IsDeprecated` before creating instances; reject with `BadRequest` |

---

## Enforcement

- **Code Review**: All PRs checked against tenets
- **CI/CD**: Automated validation where possible
- **Schema Regeneration**: Must pass after any schema changes
- **Test Coverage**: 100% of meaningful scenarios

---

*This document is the authoritative source for Bannou service development standards. Updates require explicit approval.*

*For detailed rules and examples, see the category-specific documents in [tenets/](tenets/).*
