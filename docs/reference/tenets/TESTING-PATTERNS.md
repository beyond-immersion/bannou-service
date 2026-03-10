# Testing Patterns & Anti-Patterns

> ⛔ **FROZEN DOCUMENT** — Defines authoritative testing patterns enforced across the codebase. AI agents MUST NOT add, remove, modify, or reinterpret any content without explicit user instruction. If you believe something is incorrect, report the concern and wait — do not "fix" it. See CLAUDE.md § "Reference Documents Are Frozen."

> **Category**: Quality & Verification
> **When to Reference**: Before writing any test, during test review
> **Related Tenets**: T11 (Testing Requirements), T12 (Test Integrity)

This document expands on T11/T12 with specific patterns for WHAT to test at each tier and HOW to mock correctly.

---

## The Fundamental Rule

**If deleting the implementation would still pass the test, the test is worthless.**

Before committing any test, mentally delete the code being tested. Would the test fail? If not, add assertions until it would.

---

## Unit Test Scope Decision Tree

```
Is the code...
├── Pure business logic (calculations, transformations, validation)?
│   └── UNIT TEST - Mock all dependencies, test the logic directly
│
├── State store CRUD with no complex logic?
│   └── SKIP UNIT TEST - HTTP test will cover this with real infrastructure
│
├── Event publishing only?
│   └── UNIT TEST with CAPTURE - Verify event content, not just that it was called
│
├── Cross-service calls via generated clients?
│   └── SKIP UNIT TEST - HTTP test will verify with real services
│
├── WebSocket protocol handling?
│   └── SKIP UNIT TEST - Edge test required for binary protocol
│
├── Complex multi-step orchestration?
│   └── UNIT TEST core logic + HTTP TEST for integration
│
└── Constructor/DI wiring?
    └── UNIT TEST null checks - Catches AI mistakes before integration testing
```

---

## What To Mock vs What To Test With Infrastructure

### MOCK These (Unit Tests)

| Dependency | Why Mock? | How to Verify |
|------------|-----------|---------------|
| `IStateStoreFactory` / `IStateStore<T>` | Avoid Redis dependency | **CAPTURE** saved data with Callback |
| `IMessageBus` | Avoid RabbitMQ dependency | **CAPTURE** published events with Callback |
| `ILogger<T>` | Logging is side effect | Mock with no verification needed |
| `Generated service clients` | Avoid HTTP calls | Return expected responses |
| `IEventConsumer` | Avoid pub/sub setup | Verify handler registration |

### DON'T Mock These (Let Infrastructure Tests Cover)

| Component | Why Not Mock? | Which Tier Tests It? |
|-----------|---------------|----------------------|
| Generated controller routing | Tests mock behavior, not real routing | HTTP Tests |
| JSON serialization edge cases | Real serialization differs from mocks | HTTP Tests |
| Service-to-service error propagation | Mock hides real error behavior | HTTP Tests |
| WebSocket binary protocol | Cannot mock binary framing | Edge Tests |
| Permission enforcement via Connect | Requires real session state | Edge Tests |
| Multi-service transaction flows | Mocking hides timing/ordering issues | HTTP Tests |

---

## The Capture Pattern (REQUIRED for State/Events)

**Never use `.Verify()` alone.** Always capture and assert on actual data.

### BAD: Verify-Only

```csharp
// This passes even if implementation publishes wrong event type or data
_mockMessageBus.Verify(x => x.TryPublishAsync(
    It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
    Times.Once);
```

### GOOD: Capture and Assert

```csharp
// Capture the actual published event
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

// Act
var (status, response) = await service.CreateSpeciesAsync(request);

// Assert on captured data
Assert.Equal("species.created", capturedTopic);
Assert.NotNull(capturedEvent);
var typedEvent = Assert.IsType<SpeciesCreatedEvent>(capturedEvent);
Assert.Equal(response.SpeciesId, typedEvent.SpeciesId);
Assert.Equal(request.Code.ToUpperInvariant(), typedEvent.Code);
Assert.True(typedEvent.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-5));
```

### State Store Capture

```csharp
// Capture saved state
string? savedKey = null;
SpeciesModel? savedModel = null;

_mockStore.Setup(x => x.SaveAsync(
        It.IsAny<string>(), It.IsAny<SpeciesModel>(), It.IsAny<CancellationToken>()))
    .Callback<string, SpeciesModel, CancellationToken>((k, m, _) =>
    {
        savedKey = k;
        savedModel = m;
    })
    .ReturnsAsync("etag-123");

// Act
var (status, response) = await service.CreateSpeciesAsync(request);

// Assert on saved data
Assert.Equal($"species:{response.SpeciesId}", savedKey);
Assert.NotNull(savedModel);
Assert.Equal(request.Code.ToUpperInvariant(), savedModel.Code);
Assert.Equal(request.DisplayName, savedModel.DisplayName);
```

---

## Structural Validators (Centralized)

> See [Helpers & Common Patterns § Test Validators](../HELPERS-AND-COMMON-PATTERNS.md#13-test-validators) for the complete catalog of structural validators.

Most structural validators are **centralized in `structural-tests/StructuralTests.cs`** and run automatically for every service discovered via `[BannouService]` attribute reflection. Individual plugin test projects do **not** need to duplicate these tests — adding a project reference to `structural-tests.csproj` is sufficient.

### What Structural Tests Cover (Per Service, Auto-Discovered)

| Validator | Structural Test | What It Catches |
|-----------|----------------|-----------------|
| `ServiceConstructorValidator` | `Service_HasValidConstructor` | Multiple constructors, optional params, defaults |
| `StateStoreKeyValidator` | `Service_HasValidKeyBuilders` | Missing const prefixes, wrong key builder visibility |
| `ServiceHierarchyValidator` | `Service_RespectsHierarchy` | Layer dependency violations (L2→L4, etc.) |
| `ControllerValidator` | `Service_HasValidController` | Missing IBannouController, wrong attributes |
| `EventPublishingValidator` | `Service_CallsAllGeneratedEventPublishers` | Declared events never published |
| `ResourceCleanupValidator` | `Service_HasRequiredCleanupMethods` | Missing cleanup methods for x-references |
| `PermissionMatrixValidator` | `Service_PermissionRegistrationEndpointCountMatchesSchema` | Permission count mismatches |
| *(schema scan)* | `Services_WithDeprecation_MustImplementDeprecationInterface` | Services with `deprecation: true` missing `IDeprecateAndMergeEntity` (Cat A) or `ICleanDeprecatedEntity` (Cat B) |

Additionally, IL-level scanning validates no direct `JsonSerializer`, `Environment.GetEnvironmentVariable`, infrastructure client, or `Microsoft.AspNetCore.Http.StatusCodes` usage.

### What Plugins Still Test Themselves

Only **plugin-specific** validations remain in `lib-*.tests/`:
- **`EnumMappingValidator`** tests — each plugin specifies its own enum pairs (structural tests enforce these tests *exist*)
- **Business logic** unit tests — service-specific behavior

---

## Enum Boundary Mapping Tests (REQUIRED)

Every plugin that maps between enum types — SDK boundary (A2), subset/superset, or lossy switch — MUST have corresponding unit tests using `EnumMappingValidator` from `test-utilities`. These tests catch value drift at compile/test time rather than at runtime. See [Helpers & Common Patterns § Enum Mapping](../HELPERS-AND-COMMON-PATTERNS.md#9-enum-mapping) for the mapping extension methods.

### Infrastructure

- **`bannou-service/EnumMapping.cs`** — Shared extension methods:
  - `MapByName<TSource, TTarget>()` — name-matching conversion (throws on no match)
  - `MapByNameOrDefault<TSource, TTarget>(fallback)` — name-matching with fallback for superset→subset
  - `TryMapByName<TSource, TTarget>(out result)` — non-throwing name-matching
- **`test-utilities/EnumMappingValidator.cs`** — Test assertion helpers

### Validation Patterns

| Pattern | Validator Method | When to Use |
|---------|-----------------|-------------|
| **Identical enums** | `AssertFullCoverage<TA, TB>()` | A2 boundaries where schema and SDK have the same values |
| **Superset→subset** | `AssertSupersetToSubsetMapping<TSuper, TSub>(extras...)` | SDK has extra values not in schema; validates relationship AND enumerates expected extras |
| **Subset only** | `AssertSubset<TSubset, TSuperset>()` | One-directional check that all subset values exist in superset |
| **Lossy switch** | `AssertSwitchCoversAllValues<TSource>(func)` | Explicit switch expression where values map to different names |

### Which Runtime Helper + Which Test

| Mapping Type | Runtime Helper | Test Pattern |
|-------------|---------------|-------------|
| Identical names, no lossy mapping | `MapByName` | `AssertFullCoverage` |
| Superset→subset (shared values match by name) | `MapByName` (subset→super), `MapByNameOrDefault` (super→subset) | `AssertSupersetToSubsetMapping` |
| Lossy or name-mismatched | Explicit `switch` expression | `AssertSwitchCoversAllValues` |

### Example

```csharp
[Fact]
public void ArcType_FullCoverage()
{
    EnumMappingValidator.AssertFullCoverage<ArcType, StorylineTheory.ArcType>();
}

[Fact]
public void ChordQuality_SupersetMapping()
{
    EnumMappingValidator.AssertSupersetToSubsetMapping<
        MusicTheory.ChordQuality, ChordSymbolQuality>(
        MusicTheory.ChordQuality.MinorMajor7,
        MusicTheory.ChordQuality.Power);
}

[Fact]
public void KeyMode_LossySwitch()
{
    EnumMappingValidator.AssertSwitchCoversAllValues<KeySignatureMode>(
        mode => MusicService.MapKeyModeToModeType(mode));
}
```

> **Boundary classification**: A1 (third-party library), A2 (plugin SDK), A3 (domain decision), A4 (protocol) are acceptable boundaries needing mapping tests. V1–V5 are violations to fix, not test around. See SCHEMA-RULES.md § "Enum Boundary Classification" for full definitions.

---

## Test Structure Guidelines

### Arrange:Assert Ratio

**Target: Arrange < 50% of test code**

If Arrange exceeds 50%, consider:
1. Creating a helper method for common mock setups
2. Questioning if this should be a unit test at all
3. Splitting into multiple focused tests

### Test Size Limits

| Test Type | Max Lines | If Exceeded |
|-----------|-----------|-------------|
| Guard clause test | 15 | Split or simplify |
| Happy path test | 40 | Extract setup helpers |
| Error path test | 30 | Extract setup helpers |
| Complex logic test | 60 | Split into multiple tests |

### Helper Method Pattern

```csharp
private (SpeciesService service, Mock<IStateStore<SpeciesModel>> storeMock) CreateServiceWithCapture()
{
    var storeFactory = new Mock<IStateStoreFactory>();
    var speciesStore = new Mock<IStateStore<SpeciesModel>>();
    var indexStore = new Mock<IStateStore<List<string>>>();
    var codeStore = new Mock<IStateStore<string>>();

    storeFactory.Setup(f => f.GetStore<SpeciesModel>("species")).Returns(speciesStore.Object);
    storeFactory.Setup(f => f.GetStore<List<string>>("species")).Returns(indexStore.Object);
    storeFactory.Setup(f => f.GetStore<string>("species")).Returns(codeStore.Object);

    // Default returns - override in specific tests
    indexStore.Setup(s => s.GetAsync("all-species", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<string>());
    codeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string?)null);

    var service = new SpeciesService(
        storeFactory.Object,
        Mock.Of<IMessageBus>(),
        Mock.Of<ILogger<SpeciesService>>(),
        new SpeciesServiceConfiguration(),
        Mock.Of<IEventConsumer>());

    return (service, speciesStore);
}
```

---

## What Each Tier Tests

### Tier 0: Structural & Schema Validation Tests (`structural-tests/`)

Cross-cutting compliance tests that validate conventions, schemas, and assembly metadata without running any service code. These catch authoring mistakes at build time rather than at runtime.

**Tests**:
- Service conventions via reflection (constructor patterns, partial class structure, DI attributes)
- Service layer hierarchy compliance (L0-L5 dependency rules via `ServiceHierarchyValidator`)
- State store key builder patterns (const prefixes, `Build*Key` naming)
- IL-level import scanning (direct Redis/RabbitMQ/MySQL/JsonSerializer usage detection)
- Source code pattern enforcement (null-forgiving operators, Environment.GetEnvironmentVariable)
- OpenAPI schema compliance (enum PascalCase, x-permissions presence, env var naming)
- Lifecycle schema hygiene (duplicate auto-injected fields in x-lifecycle models)
- Client event schema structure (allOf with BaseClientEvent)

**Does NOT Test**:
- Business logic correctness
- Runtime behavior of any kind
- HTTP routing or serialization
- State persistence or event delivery

**Two scanning approaches**:

| Approach | Tool | When to Use |
|----------|------|-------------|
| Line-based YAML scanning | `File.ReadAllLines` + regex | Simple pattern matching (enum casing, key presence, indentation rules) |
| Structured YAML parsing | `SchemaParser` (YamlDotNet) | Semantic validation requiring nested structure traversal (x-lifecycle model fields, allOf references) |

**Adding new schema validations**: Use `SchemaParser` for anything that needs to understand YAML structure (nested mappings, cross-references, conditional field sets). Use line-based scanning only for flat pattern matching where the line position fully determines meaning.

### Tier 1: Unit Tests (`lib-{service}.tests/`)

**Tests**:
- Input validation (null, empty, invalid format)
- Business rule enforcement (uniqueness, authorization)
- State transformations (model mapping, calculations)
- Event content correctness (via capture pattern)
- Error handling paths
- Constructor guard clauses

**Does NOT Test**:
- Actual HTTP routing
- Real JSON serialization
- Cross-service error propagation
- Database/cache behavior
- Message queue delivery

### Tier 2: HTTP Integration Tests (`http-tester/Tests/`)

**Tests**:
- Generated controller routing works
- Request/response serialization
- Service-to-service calls via lib-mesh
- State persistence with real Redis
- Event publishing with real RabbitMQ
- Error propagation across services

**Does NOT Test**:
- WebSocket binary protocol
- Client-side experience
- Permission enforcement via Connect

### Tier 3: Edge Tests (`edge-tester/Tests/`)

**Tests**:
- WebSocket connection lifecycle
- Binary message framing
- Capability manifest generation
- Permission enforcement
- Session state management
- Client event delivery
- Error response codes client sees

---

## Forbidden Patterns

### 1. Mocking the Class Under Test

```csharp
// NEVER DO THIS
var service = new SomeService(...);
typeof(SomeService).GetField("_privateField", BindingFlags.NonPublic)
    .SetValue(service, mockObject);
```

If you need internal access, the design is wrong.

### 2. It.IsAny Everywhere

```csharp
// BAD: Accepts anything
_mockStore.Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Model>(), ...))

// GOOD: Validate key format at minimum
_mockStore.Setup(x => x.SaveAsync(
    It.Is<string>(k => k.StartsWith("species:")),
    It.IsAny<SpeciesModel>(),
    It.IsAny<CancellationToken>()))
```

### 3. Assert.True(true)

```csharp
// NEVER - This is not an assertion
catch (Exception)
{
    Assert.True(true); // "test passes if no exception"
}
```

### 4. Response-Only Verification

```csharp
// BAD: Only checks response, not side effects
var (status, response) = await service.CreateAsync(request);
Assert.Equal(StatusCodes.Created, status);
Assert.NotNull(response);
// WHERE ARE THE SIDE EFFECT ASSERTIONS?
```

---

## Quick Reference: Test Placement

| Scenario | Structural? | Unit Test? | HTTP Test? | Edge Test? |
|----------|-------------|------------|------------|------------|
| Schema enum casing | YES | No | No | No |
| Schema x-permissions presence | YES | No | No | No |
| Lifecycle field duplication | YES | No | No | No |
| Service hierarchy violations | YES | No | No | No |
| Forbidden import detection | YES | No | No | No |
| Constructor DI patterns | YES | Also per-plugin | No | No |
| Input validation | No | YES | Implicit | Implicit |
| Business rule (uniqueness) | No | YES | Verify with real DB | No |
| CRUD operations | No | Minimal | YES | Verify client sees it |
| Event publishing | No | YES (capture) | Verify delivery | Verify client receives |
| Cross-service calls | No | NO | YES | If client-facing |
| Permission checks | No | NO | Verify 403s | YES (client experience) |
| WebSocket protocol | No | NO | NO | YES |
| Error messages | No | YES (content) | YES (propagation) | YES (client sees) |

---

## Applying To Existing Tests

When reviewing existing tests, ask:

1. **Would deleting the implementation pass this test?** If yes, add assertions.
2. **Is Arrange > 50%?** If yes, extract helpers or question unit test appropriateness.
3. **Are events/state changes verified via capture?** If `.Verify()` only, add capture.
4. **Does this duplicate HTTP/Edge test coverage?** If yes, consider deletion.
5. **Is the mock setup testing mock behavior?** If yes, refactor or delete.

---

*This document supplements T11/T12 in QUALITY.md with specific implementation patterns.*
