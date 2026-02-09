# Quality Tenets

> **Category**: Standards & Verification
> **When to Reference**: During code review or before PR submission
> **Tenets**: T10, T11, T12, T16, T19, T22

These tenets define quality standards to verify before merging code. Use them as a review checklist.

---

## Tenet 10: Logging Standards (REQUIRED)

**Rule**: All operations MUST include appropriate logging with structured data.

### Required Log Points

1. **Operation Entry** (Debug): Log input parameters
2. **External Calls** (Debug): Log before infrastructure/service calls
3. **Expected Outcomes** (Debug): Resource not found, validation failures
4. **Business Decisions** (Information): Significant state changes
5. **Security Events** (Warning): Auth failures, permission denials
6. **Errors** (Error): Unexpected exceptions with context

### Structured Logging Format

**ALWAYS use message templates**, not string interpolation:

```csharp
// CORRECT: Message template with named placeholders
_logger.LogDebug("Getting account {AccountId}", body.AccountId);

// WRONG: String interpolation loses structure
_logger.LogDebug($"Getting account {body.AccountId}");
```

**Forbidden formatting**: No bracket tag prefixes (`[AUTH-EVENT]`, `[PERMISSIONS]`) - the logger already includes service/class context. No emojis in log messages (acceptable only in `/scripts/` console output).

### Retry Logging

Use **Debug** for individual retry attempts, **Error** only when all retries exhausted.

**NEVER log**: Passwords, JWT tokens (log length only), API keys, secrets, PII.

---

## Tenet 11: Testing Requirements (THREE-TIER)

**Rule**: All services MUST have tests at appropriate tiers.

**For detailed testing architecture and plugin isolation boundaries, see [TESTING.md](../../guides/TESTING.md).**

### Testing Philosophy

**Test at the lowest appropriate level** - don't repeat the same test at multiple tiers:
- Unit tests verify business logic with mocked dependencies
- HTTP tests verify service integration and generated code
- Edge tests verify the client experience through WebSocket protocol

### Test Naming Convention

Osherove standard: `UnitOfWork_StateUnderTest_ExpectedBehavior` (e.g., `GetAccount_WhenAccountExists_ReturnsAccount`).

### Test Data Guidelines

- Create new resources for each test - don't rely on pre-existing state
- Don't assert specific counts - only verify expected items are present
- Tests should be independent - previous failures shouldn't impact current test

---

## Tenet 12: Test Integrity (ABSOLUTE)

**Rule**: Tests MUST validate the intended behavior path and assert CORRECT expected behavior. A failing test with correct assertions = implementation bug that needs fixing.

**NEVER**: Change `Times.Never` to `Times.AtLeastOnce` to pass, remove "inconvenient" assertions, weaken conditions, add fallbacks to bypass issues, or claim success when tests fail.

**Retries are acceptable** (retrying same operation for transient failures). **Fallbacks are forbidden** (switching mechanisms when the intended one fails masks real issues).

### When a Test Fails

1. Verify the test is correct (does it assert expected behavior?)
2. If test is correct → fix the implementation
3. If test is wrong → fix the test
4. NEVER change a correct test to pass with buggy implementation

### Nullable Reference Types and null! Tests

Tests MUST NOT use `null!` to bypass compile-time null safety on non-nullable parameters. When a parameter is non-nullable, the type system guarantees callers cannot pass null. Tests using `null!` validate an impossible code path.

```csharp
// INVALID: Tests impossible scenario - remove the test, don't add null checks
await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetAccountAsync(null!));

// VALID: Parameter is explicitly nullable by design
public async Task<Response> GetAccountAsync(string? optionalFilter)
var result = await _service.GetAccountAsync(null);  // null is allowed by type signature
```

---

## Tenet 16: Naming Conventions (CONSOLIDATED)

**Rule**: All identifiers MUST follow consistent naming patterns.

| Category | Pattern | Example |
|----------|---------|---------|
| Async methods | `{Action}Async` | `GetAccountAsync` |
| Request models | `{Action}Request` | `CreateAccountRequest` |
| Response models | `{Entity}Response` | `AccountResponse` |
| Event models | `{Entity}{Action}Event` | `AccountCreatedEvent` |
| Event topics | `{entity}.{action}` | `account.created` |
| State keys | `{entity-prefix}{id}` | `account-{guid}` |
| Config properties | PascalCase + units | `TimeoutSeconds` |
| Test methods | `UnitOfWork_State_Result` | `GetAccount_WhenExists_Returns` |

Configuration properties: PascalCase, include units in time-based names (`HeartbeatIntervalSeconds`), document environment variable in XML comment.

---

## Tenet 19: XML Documentation Standards (REQUIRED)

**Rule**: All public classes, interfaces, methods, and properties MUST have XML documentation.

**Minimum**: `<summary>` on all public types/members, `<param>` for parameters, `<returns>` for return values, `<exception>` for explicitly thrown exceptions.

Configuration properties MUST document their environment variable in the summary. Use `<inheritdoc/>` when the base interface documentation is sufficient; don't use it when the implementation has important differences.

Generated files get XML docs from OpenAPI schema `description` fields (NSwag converts to `<summary>`). Therefore **all schema properties MUST have `description` fields** - see T1.

---

## Tenet 22: Warning Suppression (FORBIDDEN)

**Rule**: Warning suppressions (`#pragma warning disable`, `[SuppressMessage]`, `NoWarn`) are FORBIDDEN except for specific documented exceptions.

**When warnings appear**: (1) understand what it's telling you, (2) fix the underlying issue, (3) ONLY suppress if it's from generated code you cannot control AND is in the allowed exceptions.

If generated code has fixable issues, add post-processing to generation scripts rather than suppressing.

### Allowed Exceptions

**Moq Generic Inference (Tests Only)**:
- CS8620, CS8619 - Unavoidable nullability mismatches from Moq generics. Suppress per-file or in test `Directory.Build.props` only.

**NSwag Generated Files Only**:
- CS8618, CS8625 - NSwag's `= default!` pattern. Configure in `.editorconfig` for `**/Generated/*.cs` paths only.

**Enum Member Documentation (Generated Code Only)**:
- CS1591 for enum members auto-suppressed via post-processing in generation scripts. NOT manual suppression. All other CS1591 warnings MUST be fixed by adding `description` fields to schemas.

**Intentional Obsolete Testing**:
- CS0618 - Testing obsolete API detection. Scoped `#pragma warning disable/restore` only.

**Ownership Transfer Pattern (CA2000)**:
- When passing a disposable to a constructor/method that takes ownership, use try-finally with null assignment instead of suppressing CA2000:

```csharp
SocketsHttpHandler? handler = null;
try
{
    handler = new SocketsHttpHandler { ... };
    _httpClient = new HttpMessageInvoker(handler);
    handler = null; // Ownership transferred - prevents dispose in finally
}
finally
{
    handler?.Dispose(); // Only executes if transfer failed
}
```

For `IAsyncDisposable`, same pattern with `await` in finally. `#pragma warning disable CA2000` is NOT allowed.

**Legacy Binding Redirect Warnings**:
- CS1701, CS1702 - .NET Framework assumptions that don't apply to .NET Core/.NET 5+. Suppress via `<NoWarn>` in project files.

**Reflection-Based Test Fixtures**:
- CA1822, IDE0051, IDE0052 - Members accessed via reflection that analyzers can't track. Inline `#pragma warning disable/restore` with comment explaining reflection access. Assembly-level suppressions NOT allowed.

---

## Quick Reference: Quality Violations

| Violation | Tenet | Fix |
|-----------|-------|-----|
| `[TAG]` prefix in logs | T10 | Remove brackets, use structured logging |
| Emojis in log messages | T10 | Plain text only (scripts excepted) |
| String interpolation in logs | T10 | Use message templates with placeholders |
| Logging passwords/tokens | T10 | Redact or log length only |
| HTTP fallback in tests | T12 | Remove fallback, fix root cause |
| Changing test to pass with buggy impl | T12 | Keep test, fix implementation |
| Using `null!` to test non-nullable params | T12 | Remove test - tests impossible scenario |
| Adding null checks for NRT-protected params | T12 | Don't add - NRT provides compile-time safety |
| Wrong naming pattern | T16 | Follow category-specific pattern |
| Missing XML documentation | T19 | Add `<summary>`, `<param>`, `<returns>` |
| Missing env var in config doc | T19 | Add environment variable to summary |
| `#pragma warning disable` without exception | T22 | Fix the warning instead of suppressing |
| Blanket GlobalSuppressions.cs | T22 | Remove file, fix warnings individually |
| Suppressing CS8602/CS8603/CS8604 in non-generated | T22 | Fix the null safety issue |

---

*This document covers tenets T10, T11, T12, T16, T19, T22. See [TENETS.md](../TENETS.md) for the complete index.*
