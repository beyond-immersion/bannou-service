# Quality Tenets

> **Category**: Standards & Verification
> **When to Reference**: During code review or before PR submission
> **Tenets**: T10, T11, T12, T16, T19

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
_logger.LogDebug($"Getting account {body.AccountId}");  // NO!
```

### Forbidden Log Formatting

**No bracket tag prefixes** - the logger already includes service/class context:

```csharp
// WRONG: Manual tag prefixes
_logger.LogInformation("[AUTH-EVENT] Processing account.deleted");  // NO!
_logger.LogDebug("[PERMISSIONS] Checking capabilities");  // NO!

// CORRECT: Let structured logging handle context
_logger.LogInformation("Processing account.deleted event for {AccountId}", evt.AccountId);
_logger.LogDebug("Checking capabilities for session {SessionId}", sessionId);
```

**No emojis in log messages** - emojis are acceptable only in `/scripts/` console output:

```csharp
// WRONG: Emojis in service logs
_logger.LogInformation("Service starting up");  // NO!
_logger.LogError("Failed to connect");  // NO!

// CORRECT: Plain text
_logger.LogInformation("Service starting up");
_logger.LogError("Failed to connect to {Endpoint}", endpoint);
```

### Retry Mechanism Logging

Use **Debug** for individual retry attempts (expected during transient failures), **Error** only when all retries exhausted:

```csharp
_logger.LogDebug(ex, "Attempt {Attempt}/{MaxRetries} failed, retrying", attempt, maxRetries);
// ... only after all retries exhausted:
_logger.LogError(ex, "All {MaxRetries} attempts exhausted", maxRetries);
```

### Forbidden Logging

**NEVER log**: Passwords, JWT tokens (log length only), API keys, secrets, PII.

---

## Tenet 11: Testing Requirements (THREE-TIER)

**Rule**: All services MUST have tests at appropriate tiers.

**For detailed testing architecture and plugin isolation boundaries, see [TESTING.md](../TESTING.md).**

### Testing Philosophy

**Test at the lowest appropriate level** - don't repeat the same test at multiple tiers:
- Unit tests verify business logic with mocked dependencies
- HTTP tests verify service integration and generated code
- Edge tests verify the client experience through WebSocket protocol

### Test Naming Convention

Follow the Osherove naming standard: `UnitOfWork_StateUnderTest_ExpectedBehavior`

Examples:
- `GetAccount_WhenAccountExists_ReturnsAccount`
- `CreateSession_WhenTokenExpired_ReturnsUnauthorized`

### Test Data Guidelines

- **Create new resources** for each test - don't rely on pre-existing state
- **Don't assert specific counts** - only verify expected items are present
- **Tests should be independent** - previous test failures shouldn't impact current test

### Tier 1 - Unit Tests (`lib-{service}.tests/`)

Test business logic with mocked dependencies.

### Tier 2 - HTTP Integration Tests (`http-tester/Tests/`)

Test actual service-to-service calls via lib-mesh, verify generated code works.

### Tier 3 - Edge/WebSocket Tests (`edge-tester/Tests/`)

Test client perspective through Connect service and binary protocol.

---

## Tenet 12: Test Integrity (ABSOLUTE)

**Rule**: Tests MUST validate the intended behavior path and assert CORRECT expected behavior.

### The Golden Rule

**A failing test with correct assertions = implementation bug that needs fixing.**

**NEVER**:
- Change `Times.Never` to `Times.AtLeastOnce` to make tests pass
- Remove assertions that "inconveniently" fail
- Weaken test conditions to accommodate wrong behavior
- Add HTTP fallbacks to bypass pub/sub issues
- Claim success when tests fail

### Retries vs Fallbacks

**Retries are acceptable** - retrying the same operation for transient failures.

**Fallbacks are forbidden** - switching to a different mechanism when the intended one fails masks real issues.

```csharp
// BAD: Fallback to different mechanism
try {
    await _messageBus.PublishAsync("entity.action", evt);
}
catch {
    await httpClient.PostAsync("http://rabbitmq/...");  // MASKS THE REAL ISSUE
}
```

### When a Test Fails

1. **Verify the test is correct** - Does it assert the expected behavior?
2. **If test is correct**: The IMPLEMENTATION is wrong - fix the implementation
3. **If test is wrong**: Fix the test, then ensure implementation passes
4. **NEVER**: Change a correct test to pass with buggy implementation

---

## Tenet 16: Naming Conventions (CONSOLIDATED)

**Rule**: All identifiers MUST follow consistent naming patterns.

### Quick Reference

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

### Configuration Property Requirements

- PascalCase for all property names
- Include units in time-based names: `TimeoutSeconds`, `HeartbeatIntervalSeconds`
- Document environment variable in XML comment

---

## Tenet 19: XML Documentation Standards (REQUIRED)

**Rule**: All public classes, interfaces, methods, and properties MUST have XML documentation comments.

### Minimum Requirements

- `<summary>` on all public types and members
- `<param>` for all method parameters
- `<returns>` for methods with return values
- `<exception>` for explicitly thrown exceptions

### Configuration Properties

Configuration properties MUST document their environment variable:

```csharp
/// <summary>
/// JWT signing secret for token generation and validation.
/// Environment variable: AUTH_JWT_SECRET
/// </summary>
public string JwtSecret { get; set; } = "default-dev-secret";
```

### When to Use `<inheritdoc/>`

Use when implementing an interface where the base documentation is sufficient. Do NOT use when the implementation has important differences.

### Generated Code

Generated files in `*/Generated/` directories do not require manual documentation - they inherit from schemas via NSwag.

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
| Wrong naming pattern | T16 | Follow category-specific pattern |
| Missing XML documentation | T19 | Add `<summary>`, `<param>`, `<returns>` |
| Missing env var in config doc | T19 | Add environment variable to summary |

---

*This document covers tenets T10, T11, T12, T16, T19. See [TENETS.md](../TENETS.md) for the complete index.*
