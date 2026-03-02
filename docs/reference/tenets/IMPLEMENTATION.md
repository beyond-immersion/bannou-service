# Implementation Tenets (Index)

> **Source Code Category**: `IMPLEMENTATION TENETS`
> **When to Reference**: While actively writing service code

The implementation tenets have been split into two focused documents:

## [Service Behavior & Contracts](IMPLEMENTATION-BEHAVIOR.md)

How services communicate, respond, and manage lifecycles.

| # | Name | Core Rule |
|---|------|-----------|
| **T3** | Event Consumer Fan-Out | Use IEventConsumer for multi-plugin event handling |
| **T7** | Error Handling | Generated controller provides catch-all boundary; ApiException catch only for inter-service calls; TryPublishErrorAsync |
| **T8** | Return Pattern | All methods return `(StatusCodes, TResponse?)` tuples; null payload for errors; no filler properties |
| **T9** | Multi-Instance Safety | No in-memory authoritative state; use distributed locks |
| **T17** | Client Event Schema Pattern | Use IClientEventPublisher for WebSocket push; not IMessageBus |
| **T30** | Telemetry Span Instrumentation | All async methods get `StartActivity` spans; zero-signature-change via `Activity.Current` |
| **T31** | Deprecation Lifecycle | Two categories (definitions vs templates); idempotent deprecation; standardized storage/events/behavior |

## [Data Modeling & Code Discipline](IMPLEMENTATION-DATA.md)

How data is typed, structured, and code is written.

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

*For the complete tenet index, see [TENETS.md](../TENETS.md).*
