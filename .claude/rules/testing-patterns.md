---
globs: **/*.tests/**, bannou-service.tests/**, structural-tests/**, test-utilities/**, tools/http-tester/**, tools/edge-tester/**
---

# Testing Work — Mandatory Reference

You are working in a test project. Before writing, modifying, reviewing, or debugging ANY test, you MUST read `docs/reference/tenets/TESTING-PATTERNS.md` and confirm with "I have referred to TESTING-PATTERNS."

## Critical Rules (from TESTING-PATTERNS.md)

### Test Placement — Plugin Isolation

| Test Project | Can Reference | Cannot Reference |
|-------------|---------------|------------------|
| `bannou-service.tests/` | `bannou-service` only | ANY `lib-*` plugin |
| `lib-*.tests/` | Own `lib-*` plugin + `bannou-service` | Other `lib-*` plugins |
| `structural-tests/` | All assemblies via reflection | No direct plugin code execution |

### The Capture Pattern (REQUIRED for State/Events)

**Never use `.Verify()` alone.** Always capture and assert on actual data:

```csharp
// BAD: Passes even if implementation publishes wrong data
_mockMessageBus.Verify(x => x.TryPublishAsync(
    It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
    Times.Once);

// GOOD: Capture and assert on actual published content
string? capturedTopic = null;
object? capturedEvent = null;
_mockMessageBus.Setup(x => x.TryPublishAsync(
        It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
    .Callback<string, object, CancellationToken>((topic, evt, _) =>
    {
        capturedTopic = topic;
        capturedEvent = evt;
    })
    .ReturnsAsync(true);
```

### Test Integrity (ABSOLUTE)

- **NEVER** change `Times.Never` to `Times.AtLeastOnce` to make a test pass
- **NEVER** weaken assertions or add fallbacks to bypass failures
- **NEVER** use `null!` to test non-nullable parameters (tests impossible scenario)
- A failing test with correct assertions = **implementation bug**, not test bug

### Structural Tests

Most structural validators are centralized in `structural-tests/StructuralTests.cs`. Individual plugin test projects do NOT duplicate these. Only plugin-specific validations (EnumMappingValidator tests, business logic) remain in `lib-*.tests/`.

### Enum Mapping Tests

Every plugin mapping between enum types MUST have `EnumMappingValidator` tests. Use `AssertFullCoverage` for identical enums, `AssertSupersetToSubsetMapping` for superset→subset, `AssertSwitchCoversAllValues` for lossy switches.
