# Bannou Service Development Tenets

> **Version**: 7.0
> **Last Updated**: 2026-02-01
> **Scope**: All Bannou microservices and related infrastructure

This document is the authoritative index for Bannou development standards. All service implementations, tests, and infrastructure MUST adhere to these tenets. Tenets must not be changed or added without EXPLICIT approval, without exception.

> **AI ASSISTANTS**: All tenets apply with heightened scrutiny to AI-generated code and suggestions. AI assistants MUST NOT bypass, weaken, or work around any tenet without explicit human approval. This includes modifying tests to pass with buggy implementations, adding fallback mechanisms, or any other "creative solutions" that violate the spirit of these tenets.

---

## Tenet 0: Never Reference Tenet Numbers in Source Code

When documenting tenet compliance in source code comments, **NEVER use specific tenet numbers** (e.g., "T9", "Tenet 21", "TENET T4"). Tenet numbers change over time as tenets are added, removed, or reorganized.

**Instead, use category names:**
- `FOUNDATION TENETS` - for T4, T5, T6, T13, T15, T18
- `IMPLEMENTATION TENETS` - for T3, T7, T8, T9, T14, T17, T20, T21, T23, T24, T25, T26
- `QUALITY TENETS` - for T10, T11, T12, T16, T19, T22
- `SERVICE HIERARCHY` - for Tenet 2 (service layer dependencies)

> **Note**: Tenets 1 and 2 are special - they reference external documents ([SCHEMA-RULES.md](SCHEMA-RULES.md) and [SERVICE_HIERARCHY.md](SERVICE_HIERARCHY.md)) for detailed rules. In source code, reference them as `FOUNDATION TENETS` or by their specific names ("per schema-first development", "per service hierarchy").

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
├── Models/{Service}Models.cs        # Request/response models
├── Clients/{Service}Client.cs       # Service client for mesh calls
└── Events/{Service}EventsModels.cs  # Service event models
```

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

Before adding ANY service client dependency, you MUST read [SERVICE_HIERARCHY.md](SERVICE_HIERARCHY.md). This is not optional.

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

If a foundational service needs to know about references from higher layers (for cleanup eligibility), use **event-driven reference registration** - higher-layer services publish reference events, and the foundational service consumes them. See [SERVICE_HIERARCHY.md](SERVICE_HIERARCHY.md) for the full pattern.

---

## Tenet Categories

Tenets are organized into categories based on when they're needed:

| Category | Tenets | When to Reference |
|----------|--------|-------------------|
| [**Schema Rules**](SCHEMA-RULES.md) | Tenet 1 | Before creating or modifying any schema file |
| [**Service Hierarchy**](SERVICE_HIERARCHY.md) | Tenet 2 | Before adding any service client dependency |
| [**Foundation**](tenets/FOUNDATION.md) | T4, T5, T6, T13, T15, T18 | Before starting any new service or feature |
| [**Implementation**](tenets/IMPLEMENTATION.md) | T3, T7, T8, T9, T14, T17, T20, T21, T23, T24, T25, T26 | While actively writing service code |
| [**Quality**](tenets/QUALITY.md) | T10, T11, T12, T16, T19, T22 | During code review or before PR submission |

> **Note**: Tenets 1 and 2 reference standalone documents (SCHEMA-RULES.md and SERVICE_HIERARCHY.md) that contain their own detailed rules.

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

---

## Implementation Tenets (Coding Patterns)

*Full details: [tenets/IMPLEMENTATION.md](tenets/IMPLEMENTATION.md)*

| # | Name | Core Rule |
|---|------|-----------|
| **T3** | Event Consumer Fan-Out | Use IEventConsumer for multi-plugin event handling |
| **T7** | Error Handling | Try-catch with ApiException vs Exception distinction; TryPublishErrorAsync |
| **T8** | Return Pattern | All methods return `(StatusCodes, TResponse?)` tuples |
| **T9** | Multi-Instance Safety | No in-memory authoritative state; use distributed locks |
| **T14** | Polymorphic Associations | Entity ID + Type columns; composite string keys |
| **T17** | Client Event Schema Pattern | Use IClientEventPublisher for WebSocket push; not IMessageBus |
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
| Layer 2 service depending on Layer 3 | T2 | Remove dependency; use events instead (see [SERVICE_HIERARCHY.md](SERVICE_HIERARCHY.md)) |
| Layer 2 service depending on Layer 4 | T2 | Remove dependency; higher layer should publish events |
| Foundation service calling extension client | T2 | Invert the dependency; extension consumes foundation events |
| Circular service dependencies | T2 | Restructure to respect layer hierarchy |
| Direct Redis/MySQL connection | T4 | Use IStateStoreFactory via lib-state |
| Direct RabbitMQ connection | T4 | Use IMessageBus via lib-messaging |
| Direct HTTP service calls | T4 | Use generated clients via lib-mesh |
| Graceful degradation for L0/L1/L2 dependency | T4 | Use constructor injection; see SERVICE_HIERARCHY.md |
| Anonymous event objects | T5 | Define typed event in schema |
| Manually defining lifecycle events | T5 | Use `x-lifecycle` in events schema |
| Service class missing `partial` | T6 | Add `partial` keyword |
| Missing x-permissions on endpoint | T13 | Add to schema (even if empty array) |
| GPL library in NuGet package | T18 | Use MIT/BSD alternative |
| Missing event consumer registration | T3 | Add RegisterEventConsumers call |
| Generic catch returning 500 | T7 | Catch ApiException specifically |
| Emitting error events for user errors | T7 | Only emit for unexpected/internal failures |
| Using Microsoft.AspNetCore.Http.StatusCodes | T8 | Use BeyondImmersion.BannouService.StatusCodes |
| Plain Dictionary for cache | T9 | Use ConcurrentDictionary |
| Per-instance salt/key generation | T9 | Use shared/deterministic values |
| Wrong exchange for client events | T17 | Use IClientEventPublisher, not IMessageBus |
| Direct `JsonSerializer` usage | T20 | Use `BannouJson.Serialize/Deserialize` |
| Direct `Environment.GetEnvironmentVariable` | T21 | Use service configuration class |
| Unused configuration property | T21 | Wire up in service or remove from schema |
| Hardcoded magic number for tunable | T21 | Define in configuration schema, use config |
| Defined cache store not used | T21 | Implement cache read-through or remove store |
| Secondary fallback for defaulted config property | T21 | Remove fallback; if null, throw (infrastructure failure) |
| Non-async Task-returning method | T23 | Add async keyword and await |
| `Task.FromResult` without async | T23 | Use async method with await |
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
| Using `Guid.Empty` to mean "none" | T26 | Make field `Guid?` nullable |
| Using `-1` to mean "no index" | T26 | Make field `int?` nullable |
| Using empty string for "absent" | T26 | Make field `string?` nullable |
| Non-nullable model field when value can be absent | T26 | Make field nullable, update schema |
| `[TAG]` prefix in logs | T10 | Remove brackets, use structured logging |
| Emojis in log messages | T10 | Plain text only (scripts excepted) |
| HTTP fallback in tests | T12 | Remove fallback, fix root cause |
| Changing test to pass with buggy impl | T12 | Keep test, fix implementation |
| Using `null!` to test non-nullable params | T12 | Remove test - tests impossible scenario |
| Adding null checks for NRT-protected params | T12 | Don't add - NRT provides compile-time safety |
| Missing XML documentation | T19 | Add `<summary>`, `<param>`, `<returns>` |
| `#pragma warning disable` without exception | T22 | Fix the warning instead of suppressing |
| Blanket GlobalSuppressions.cs | T22 | Remove file, fix warnings individually |
| Suppressing CS8602/CS8603/CS8604 in non-generated | T22 | Fix the null safety issue |
| CS1591 warning on schema property/class | T1, T22 | Add `description` to schema (enums auto-suppressed) |

---

## Enforcement

- **Code Review**: All PRs checked against tenets
- **CI/CD**: Automated validation where possible
- **Schema Regeneration**: Must pass after any schema changes
- **Test Coverage**: 100% of meaningful scenarios

---

*This document is the authoritative source for Bannou service development standards. Updates require explicit approval.*

*For detailed rules and examples, see the category-specific documents in [tenets/](tenets/).*
