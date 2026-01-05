# Bannou Service Development Tenets

> **Version**: 5.0
> **Last Updated**: 2025-12-28
> **Scope**: All Bannou microservices and related infrastructure

This document is the authoritative index for Bannou development standards. All service implementations, tests, and infrastructure MUST adhere to these tenets. Tenets must not be changed or added without EXPLICIT approval, without exception.

> **AI ASSISTANTS**: All tenets apply with heightened scrutiny to AI-generated code and suggestions. AI assistants MUST NOT bypass, weaken, or work around any tenet without explicit human approval. This includes modifying tests to pass with buggy implementations, adding fallback mechanisms, or any other "creative solutions" that violate the spirit of these tenets.

---

## Tenet 0: Never Reference Tenet Numbers in Source Code

When documenting tenet compliance in source code comments, **NEVER use specific tenet numbers** (e.g., "T9", "Tenet 21", "TENET T4"). Tenet numbers change over time as tenets are added, removed, or reorganized.

**Instead, use category names:**
- `FOUNDATION TENETS` - for T1, T2, T4, T5, T6, T13, T15, T18
- `IMPLEMENTATION TENETS` - for T3, T7, T8, T9, T14, T17, T20, T21, T23
- `QUALITY TENETS` - for T10, T11, T12, T16, T19, T22

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

## Tenet Categories

Tenets are organized into three categories based on when they're needed:

| Category | Tenets | When to Reference |
|----------|--------|-------------------|
| [**Foundation**](tenets/FOUNDATION.md) | T1, T2, T4, T5, T6, T13, T15, T18 | Before starting any new service or feature |
| [**Implementation**](tenets/IMPLEMENTATION.md) | T3, T7, T8, T9, T14, T17, T20, T21, T23 | While actively writing service code |
| [**Quality**](tenets/QUALITY.md) | T10, T11, T12, T16, T19, T22 | During code review or before PR submission |

---

## Foundation Tenets (Architecture & Design)

*Full details: [tenets/FOUNDATION.md](tenets/FOUNDATION.md)*

| # | Name | Core Rule |
|---|------|-----------|
| **T1** | Schema-First Development | All APIs, models, events, and configs defined in OpenAPI YAML before code |
| **T2** | Code Generation System | 8-component pipeline generates code from schemas; never edit Generated/ |
| **T4** | Infrastructure Libs Pattern | MUST use lib-state, lib-messaging, lib-mesh; direct DB/queue access forbidden |
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
| **T21** | Configuration-First | Use generated config classes; no direct Environment.GetEnvironmentVariable |
| **T23** | Async Method Pattern | Task-returning methods must be async with await |

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
| Editing Generated/ files | T1, T2 | Edit schema, regenerate |
| Missing `description` on schema property | T1 | Add description field (causes CS1591) |
| Wrong env var format (`JWTSECRET`) | T2 | Use `{SERVICE}_{PROPERTY}` pattern |
| Missing `env:` key in config schema | T2 | Add explicit `env:` with proper naming |
| Missing service prefix (`REDIS_CONNECTION_STRING`) | T2 | Add prefix (e.g., `STATE_REDIS_CONNECTION_STRING`) |
| Hyphen in env var prefix (`GAME-SESSION_`) | T2 | Use underscore (`GAME_SESSION_`) |
| Direct Redis/MySQL connection | T4 | Use IStateStoreFactory via lib-state |
| Direct RabbitMQ connection | T4 | Use IMessageBus via lib-messaging |
| Direct HTTP service calls | T4 | Use generated clients via lib-mesh |
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
| Non-async Task-returning method | T23 | Add async keyword and await |
| `Task.FromResult` without async | T23 | Use async method with await |
| `.Result` or `.Wait()` on Task | T23 | Use await instead |
| `[TAG]` prefix in logs | T10 | Remove brackets, use structured logging |
| Emojis in log messages | T10 | Plain text only (scripts excepted) |
| HTTP fallback in tests | T12 | Remove fallback, fix root cause |
| Changing test to pass with buggy impl | T12 | Keep test, fix implementation |
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
