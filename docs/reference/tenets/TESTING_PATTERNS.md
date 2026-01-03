# Testing Patterns & Anti-Patterns

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

## Constructor Tests: Use ServiceConstructorValidator

### Purpose

Constructor validation catches AI-introduced DI mistakes before expensive integration testing. These issues include missing null checks, wrong parameter names, optional parameters, and constructor overloads.

### Recommended Pattern (One Line Per Service)

```csharp
// Add test-utilities reference to your test project
using BeyondImmersion.BannouService.TestUtilities;

public class SpeciesServiceTests
{
    /// <summary>
    /// Validates constructor pattern: single public constructor,
    /// no optional params, no defaults, proper null checks.
    /// </summary>
    [Fact]
    public void SpeciesService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<SpeciesService>();
}
```

### What ServiceConstructorValidator Catches

1. **Multiple constructors** - DI might pick wrong one (AI added overload)
2. **Optional parameters** - AI accidentally made dependency optional
3. **Default values** - AI added `= null` or similar defaults
4. **Missing null checks** - AI forgot `?? throw new ArgumentNullException(...)`
5. **Wrong param names** - AI typo in `nameof(parameter)`

### Migration from Old Pattern

**Before** (11+ tests per service):
```csharp
[Fact] public void Constructor_NullStateStoreFactory_Throws() { ... }
[Fact] public void Constructor_NullMessageBus_Throws() { ... }
[Fact] public void Constructor_NullLogger_Throws() { ... }
// ... 8 more tests
```

**After** (1 test per service):
```csharp
[Fact]
public void MyService_ConstructorIsValid() =>
    ServiceConstructorValidator.ValidateServiceConstructor<MyService>();
```

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

| Scenario | Unit Test? | HTTP Test? | Edge Test? |
|----------|------------|------------|------------|
| Input validation | YES | Implicit | Implicit |
| Business rule (uniqueness) | YES | Verify with real DB | No |
| CRUD operations | Minimal | YES | Verify client sees it |
| Event publishing | YES (capture) | Verify delivery | Verify client receives |
| Cross-service calls | NO | YES | If client-facing |
| Permission checks | NO | Verify 403s | YES (client experience) |
| WebSocket protocol | NO | NO | YES |
| Error messages | YES (content) | YES (propagation) | YES (client sees) |

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
