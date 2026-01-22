# Prebound API Execution Plan

> **Status**: Implementation Planning
> **Created**: 2025-01-22
> **Purpose**: Unified prebound API execution for ServiceNavigator, enabling lib-connect routing and lib-contract clause validation

---

## Problem Statement

Bannou needs a unified way to execute "prebound APIs" - service calls defined as JSON templates with variable substitution. Two systems currently need this:

1. **lib-connect**: Routes WebSocket client messages to services, including "session shortcuts" with pre-bound payloads
2. **lib-contract**: Needs to execute milestone actions (`onComplete`/`onExpire`) and validate contract clauses against external service state

Currently, lib-connect has inline HTTP routing code in `ConnectService.RouteToServiceAsync()`. lib-contract has a `PreboundApi` schema defined but no execution implementation.

---

## The Two "Prebound API" Concepts

### 1. Connect Service "Session Shortcuts" (`SessionShortcutData`)

Located in: `plugins/lib-connect/Protocol/ConnectionState.cs`

Session shortcuts are pre-bound payloads that WebSocket clients can invoke via a single GUID:

```csharp
public class SessionShortcutData
{
    public Guid RouteGuid { get; set; }           // Client-salted GUID for invocation
    public Guid TargetGuid { get; set; }          // Actual service endpoint GUID
    public byte[] BoundPayload { get; set; }      // Pre-serialized JSON (opaque bytes)
    public string? TargetService { get; set; }    // e.g., "game-session"
    public string? TargetMethod { get; set; }     // e.g., "POST"
    public string? TargetEndpoint { get; set; }   // e.g., "/sessions/join"
    public DateTimeOffset? ExpiresAt { get; set; }
    // ... other metadata
}
```

When a client sends a message with a shortcut GUID:
1. `MessageRouter.AnalyzeMessage()` recognizes it as a shortcut
2. Injects the `BoundPayload` into the message
3. Routes to `TargetService/TargetMethod/TargetEndpoint` via `RouteToServiceAsync()`

### 2. lib-contract "PreboundApi" (Schema Defined, Not Implemented)

Located in: `schemas/contract-api.yaml`

```yaml
PreboundApi:
  type: object
  properties:
    serviceName:
      type: string
      description: Target service name (e.g., "currency")
    endpoint:
      type: string
      description: Target endpoint path (e.g., "/currency/transfer")
    payloadTemplate:
      type: string
      description: JSON payload with {{variable}} placeholders
    executionMode:
      type: string
      enum: [sync, async, fire_and_forget]
```

Used in milestone definitions (`onComplete`, `onExpire`) but never actually executed.

---

## Unified Solution Architecture

Both concepts need the same core capability: **Execute raw JSON against a service/endpoint and get the response.**

```
┌─────────────────────────────────────────────────────────────────┐
│                    IServiceNavigator                             │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │   ExecuteRawApiAsync(service, endpoint, jsonPayload)        │ │
│  │   - Takes raw JSON string (or byte[])                       │ │
│  │   - Returns RawApiResult (status, response, headers)        │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                              │                                   │
│                              ▼                                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │   ExecutePreboundApiAsync(api, context)                     │ │
│  │   - Takes PreboundApiDefinition + context dict              │ │
│  │   - Substitutes {{variables}} from context                  │ │
│  │   - Calls ExecuteRawApiAsync                                │ │
│  │   - Returns PreboundApiResult                               │ │
│  └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │
           ┌──────────────────┼──────────────────┐
           │                  │                  │
           ▼                  ▼                  ▼
    ┌─────────────┐    ┌─────────────┐    ┌──────────────────┐
    │ lib-connect │    │ lib-contract│    │ Future services  │
    │             │    │             │    │                  │
    │ FIRST USER  │    │ Milestone   │    │ Execute dynamic  │
    │ Validates   │    │ onComplete  │    │ service calls    │
    │ the new     │    │ onExpire    │    │                  │
    │ path works  │    │ Clause      │    │                  │
    │             │    │ validation  │    │                  │
    └─────────────┘    └─────────────┘    └──────────────────┘
```

---

## Component Design

### Component 1: TemplateSubstitutor

**Location**: `bannou-service/Utilities/TemplateSubstitutor.cs`

**Purpose**: Substitutes `{{variable}}` placeholders in JSON templates.

**Features**:
- Dot-path navigation: `{{party.wallet_id}}`
- Array indexing: `{{parties[0].entity_id}}`
- Preserves JSON types (strings quoted, numbers/bools/nulls unquoted)
- **Fails explicitly** on missing variables - no silent empty strings
- Works with `Dictionary<string, object?>` - values can be primitives or nested objects

```csharp
public static class TemplateSubstitutor
{
    /// <summary>
    /// Substitutes variables in a JSON template string.
    /// </summary>
    public static string Substitute(string template, IReadOnlyDictionary<string, object?> context);

    /// <summary>
    /// Validates a template without substituting.
    /// </summary>
    public static TemplateValidationResult Validate(string template, IReadOnlyDictionary<string, object?> context);

    /// <summary>
    /// Extracts all variable paths from a template.
    /// </summary>
    public static IReadOnlyList<string> ExtractVariables(string template);
}

public class TemplateSubstitutionException : Exception
{
    public string VariablePath { get; }
    public string Template { get; }
    public SubstitutionErrorType ErrorType { get; }
}

public enum SubstitutionErrorType { MissingVariable, InvalidPath }

public class TemplateValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<string> MissingVariables { get; }
    public IReadOnlyList<string> InvalidPaths { get; }
}
```

### Component 2: Prebound API Models

**Location**: `bannou-service/ServiceClients/PreboundApiModels.cs`

```csharp
/// <summary>
/// Definition of a prebound API call (decoupled from generated contract models).
/// </summary>
public class PreboundApiDefinition
{
    public string ServiceName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string PayloadTemplate { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.Sync;
}

public enum ExecutionMode { Sync, Async, FireAndForget }
public enum BatchExecutionMode { Parallel, Sequential, SequentialStopOnFailure }

/// <summary>
/// Result of executing a raw API call.
/// </summary>
public class RawApiResult
{
    public int StatusCode { get; init; }
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public string? ResponseBody { get; init; }
    public JsonDocument? ResponseDocument { get; init; }
    public IReadOnlyDictionary<string, IEnumerable<string>>? Headers { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
}

/// <summary>
/// Result of executing a prebound API with variable substitution.
/// </summary>
public class PreboundApiResult
{
    public PreboundApiDefinition Api { get; init; } = default!;
    public string SubstitutedPayload { get; init; } = string.Empty;
    public RawApiResult Result { get; init; } = default!;
    public bool SubstitutionSucceeded { get; init; }
    public string? SubstitutionError { get; init; }
}
```

### Component 3: IServiceNavigator Extensions

**Location**: `bannou-service/ServiceClients/IServiceNavigator.cs` (the manual partial interface)

Add to the existing partial interface:

```csharp
public partial interface IServiceNavigator
{
    // ... existing methods (GetRequesterSessionId, PublishToRequesterAsync, etc.) ...

    #region Raw API Execution

    /// <summary>
    /// Executes a raw JSON payload against a service endpoint.
    /// This is the low-level method used by both Connect shortcuts and Contract prebound APIs.
    /// </summary>
    Task<RawApiResult> ExecuteRawApiAsync(
        string serviceName,
        string endpoint,
        string jsonPayload,
        HttpMethod? method = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a raw byte payload against a service endpoint.
    /// Used by Connect for zero-copy shortcut forwarding.
    /// </summary>
    Task<RawApiResult> ExecuteRawApiAsync(
        string serviceName,
        string endpoint,
        ReadOnlyMemory<byte> payload,
        HttpMethod? method = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a prebound API with variable substitution.
    /// </summary>
    Task<PreboundApiResult> ExecutePreboundApiAsync(
        PreboundApiDefinition api,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct = default);

    /// <summary>
    /// Executes multiple prebound APIs in batch.
    /// </summary>
    Task<IReadOnlyList<PreboundApiResult>> ExecutePreboundApiBatchAsync(
        IEnumerable<PreboundApiDefinition> apis,
        IReadOnlyDictionary<string, object?> context,
        BatchExecutionMode mode = BatchExecutionMode.Parallel,
        CancellationToken ct = default);

    #endregion
}
```

### Component 4: ServiceNavigator Implementation

**Location**: Modify `bannou-service/Generated/ServiceNavigator.cs` generation OR add manual partial class

The implementation extracts the core logic from `ConnectService.RouteToServiceAsync()`:

1. Resolve app-id via `IServiceAppMappingResolver.GetAppIdForService(serviceName)`
2. Build URL: `{effectiveHttpEndpoint}/{endpoint.TrimStart('/')}`
3. Create HTTP request with:
   - `bannou-app-id` header
   - `X-Bannou-Session-Id` header (from `ServiceRequestContext.SessionId`)
   - JSON/byte payload as content
4. Send via `IHttpClientFactory`
5. Return `RawApiResult` with status, body, headers, duration

**Key**: ServiceNavigator needs access to:
- `IHttpClientFactory` (add to constructor)
- `IServiceAppMappingResolver` (already available via service clients)
- `Program.Configuration.EffectiveHttpEndpoint` (or inject endpoint configuration)

### Component 5: lib-connect Migration

**Location**: `plugins/lib-connect/ConnectService.cs`

Migrate `RouteToServiceAsync()` to use `ServiceNavigator.ExecuteRawApiAsync()`:

**Before** (current inline code ~180 lines):
```csharp
private async Task RouteToServiceAsync(BinaryMessage message, ...)
{
    // Parse endpoint key
    // Resolve app-id
    // Build URL
    // Create HTTP request
    // Send via HttpClient
    // Handle response
    // Send to WebSocket client
}
```

**After** (using ServiceNavigator):
```csharp
private async Task RouteToServiceAsync(BinaryMessage message, ...)
{
    var parts = endpointKey.Split(':', 3);
    var serviceName = parts[0];
    var httpMethod = parts[1];
    var path = parts[2];

    var result = await _nav.ExecuteRawApiAsync(
        serviceName,
        path,
        message.Payload,
        new HttpMethod(httpMethod),
        cancellationToken);

    if (routeInfo.RequiresResponse)
    {
        var responseCode = MapHttpStatusToResponseCode(result.StatusCode);
        var responseMessage = BinaryMessage.CreateResponse(
            message, responseCode, Encoding.UTF8.GetBytes(result.ResponseBody ?? "{}"));
        await _connectionManager.SendMessageAsync(sessionId, responseMessage, cancellationToken);
    }
}
```

**Note**: ConnectService constructor needs `IServiceNavigator _nav` added.

---

## Response Validation for lib-contract

### Purpose

When lib-contract executes a prebound API to validate a clause condition, HTTP 200 doesn't mean "success" - it means "the API call worked." The **response content** determines whether the clause condition is satisfied.

Example: Checking if a party has sufficient funds
- API: `POST /currency/wallet/balance`
- Response: `{"balance": 250, "currency_id": "gold"}`
- Clause requires: balance >= 500
- Result: **Validation failed** (even though HTTP was 200)

### Three-Outcome Model

```
┌─────────────────────────────────────────────────────────────────┐
│                    Validation Outcomes                          │
├─────────────────────────────────────────────────────────────────┤
│ SUCCESS           │ All success criteria passed                 │
│                   │ → Clause condition is satisfied             │
├───────────────────┼─────────────────────────────────────────────┤
│ PERMANENT_FAILURE │ Matched a permanent failure criterion       │
│                   │ → Stop retrying, clause cannot be satisfied │
│                   │ Examples: 404 wallet not found, account     │
│                   │ closed, entity deleted                      │
├───────────────────┼─────────────────────────────────────────────┤
│ TRANSIENT_FAILURE │ Neither success nor permanent failure       │
│                   │ → May retry later (service down, timeout,   │
│                   │ insufficient funds that could change)       │
└───────────────────┴─────────────────────────────────────────────┘
```

### Schema Additions to contract-api.yaml

```yaml
ContractClauseValidation:
  type: object
  description: |
    Defines how to validate a contract clause condition via API call.
  additionalProperties: false
  required:
    - clauseCode
    - validationApi
    - validationSchema
  properties:
    clauseCode:
      type: string
      description: Identifies which clause this validates

    checkMode:
      type: string
      enum: [manual, periodic, lazy]
      default: lazy
      description: |
        manual = only check when explicitly requested
        periodic = check on configured schedule
        lazy = check when accessed if stale (default)

    staleAfterSeconds:
      type: integer
      minimum: 5
      default: 15
      description: For lazy mode, seconds before validation is stale

    periodicIntervalSeconds:
      type: integer
      minimum: 30
      description: For periodic mode, interval between checks

    validationApi:
      $ref: '#/components/schemas/PreboundApi'

    validationSchema:
      $ref: '#/components/schemas/ResponseValidationSchema'

ResponseValidationSchema:
  type: object
  description: Defines how to interpret API response for validation
  additionalProperties: false
  required:
    - successCriteria
  properties:
    successCriteria:
      type: array
      description: ALL must pass for success
      items:
        $ref: '#/components/schemas/ValidationCriterion'
      minItems: 1

    permanentFailureCriteria:
      type: array
      description: ANY match means permanent failure
      items:
        $ref: '#/components/schemas/ValidationCriterion'
      nullable: true

ValidationCriterion:
  type: object
  additionalProperties: false
  required:
    - type
  properties:
    type:
      type: string
      enum:
        - status_code          # Check HTTP status
        - json_path_equals     # $.path == expected
        - json_path_exists     # $.path exists and is truthy
        - json_path_absent     # $.path doesn't exist or is null
        - json_path_gte        # $.path >= expected (numeric)
        - json_path_lte        # $.path <= expected (numeric)
        - json_path_contains   # $.path contains expected

    path:
      type: string
      nullable: true
      description: JSONPath expression (e.g., "$.data.balance")

    expected:
      description: |
        Expected value. Can use {{variable}} for dynamic values.
        status_code: array of integers [200, 201]
        json_path_equals: value to match
        json_path_gte/lte: numeric value
      nullable: true

    negate:
      type: boolean
      default: false
```

### ResponseValidator Utility

**Location**: `bannou-service/Utilities/ResponseValidator.cs`

```csharp
public static class ResponseValidator
{
    /// <summary>
    /// Validates an API response against a validation schema.
    /// </summary>
    public static ClauseValidationResult Validate(
        RawApiResult apiResult,
        ResponseValidationSchema schema,
        IReadOnlyDictionary<string, object?>? context = null);
}

public enum ValidationOutcome { Success, PermanentFailure, TransientFailure }

public class ClauseValidationResult
{
    public string ClauseCode { get; init; } = string.Empty;
    public ValidationOutcome Outcome { get; init; }
    public PreboundApiResult ApiResult { get; init; } = default!;
    public IReadOnlyList<CriterionEvaluationResult> CriteriaResults { get; init; } = [];
    public string? FailureReason { get; init; }
    public DateTimeOffset ValidatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }  // For caching
}

public class CriterionEvaluationResult
{
    public ValidationCriterion Criterion { get; init; } = default!;
    public bool Passed { get; init; }
    public object? ActualValue { get; init; }
    public string? Message { get; init; }
}
```

**JSONPath**: Use `JsonPath.Net` NuGet package (MIT licensed, version 2.2.0).

### Clause Validation in ContractService

**Location**: `plugins/lib-contract/ContractService.cs`

```csharp
/// <summary>
/// Validates a contract clause, using cached result if not stale.
/// </summary>
public async Task<ClauseValidationResult> ValidateClauseAsync(
    Guid contractId,
    string clauseCode,
    bool forceRefresh = false,
    CancellationToken ct = default)
{
    // 1. Load contract instance
    // 2. Find clause validation definition
    // 3. Check cached result if not forceRefresh
    //    - If checkMode == lazy && lastValidation < staleAfterSeconds ago, return cached
    // 4. Build context from contract/parties/terms
    // 5. Execute via _nav.ExecutePreboundApiAsync(clause.ValidationApi, context)
    // 6. Evaluate response via ResponseValidator.Validate()
    // 7. Cache result
    // 8. Return result
}

/// <summary>
/// Background job for periodic clause validation.
/// Called by a timer/scheduler.
/// </summary>
public async Task ProcessPeriodicClauseValidationsAsync(CancellationToken ct)
{
    // 1. Query active contracts with periodic clause validations
    // 2. Filter to those due for validation (nextCheckAt <= now)
    // 3. Execute validations
    // 4. Publish events for failures (contract.clause.validation.failed)
    // 5. Update nextCheckAt timestamps
}
```

**Cached Validation State** (add to contract instance):

```yaml
# In contract instance model
clauseValidations:
  type: object
  additionalProperties:
    $ref: '#/components/schemas/CachedClauseValidation'
  nullable: true

CachedClauseValidation:
  type: object
  properties:
    clauseCode:
      type: string
    outcome:
      type: string
      enum: [success, permanent_failure, transient_failure]
    validatedAt:
      type: string
      format: date-time
    nextCheckAt:
      type: string
      format: date-time
      nullable: true
    failureReason:
      type: string
      nullable: true
```

---

## Implementation Order

**CRITICAL**: lib-connect is the FIRST consumer. It validates the new ServiceNavigator path works before lib-contract adopts it.

### Phase 1: Core Infrastructure

1. **TemplateSubstitutor** - `bannou-service/Utilities/TemplateSubstitutor.cs`
   - Pure utility, no dependencies
   - Regex-based `{{variable}}` substitution
   - Dot-path and array index support
   - Explicit failure on missing variables

2. **Prebound API Models** - `bannou-service/ServiceClients/PreboundApiModels.cs`
   - `PreboundApiDefinition`
   - `RawApiResult`
   - `PreboundApiResult`
   - `ExecutionMode`, `BatchExecutionMode` enums

3. **IServiceNavigator Extensions** - `bannou-service/ServiceClients/IServiceNavigator.cs`
   - Add `ExecuteRawApiAsync` (string payload)
   - Add `ExecuteRawApiAsync` (byte[] payload)
   - Add `ExecutePreboundApiAsync`
   - Add `ExecutePreboundApiBatchAsync`

4. **ServiceNavigator Implementation**
   - Either modify generator or add manual partial class
   - Extract HTTP execution logic from ConnectService
   - Needs: `IHttpClientFactory`, endpoint configuration

### Phase 2: lib-connect Migration (FIRST USER)

5. **Inject ServiceNavigator into ConnectService**
   - Add `IServiceNavigator _nav` to constructor

6. **Migrate RouteToServiceAsync**
   - Replace inline HTTP code with `_nav.ExecuteRawApiAsync()`
   - Keep WebSocket response handling

7. **Test WebSocket routing**
   - Verify all existing functionality works
   - Session shortcuts, regular routing, meta endpoints

### Phase 3: lib-contract Clause Validation

8. **Add ResponseValidationSchema to contract-api.yaml**
   - `ContractClauseValidation`
   - `ResponseValidationSchema`
   - `ValidationCriterion`
   - `CachedClauseValidation`

9. **Add JsonPath.Net dependency**
   - `dotnet add bannou-service package JsonPath.Net`

10. **Create ResponseValidator** - `bannou-service/Utilities/ResponseValidator.cs`
    - Status code checking
    - JSONPath evaluation
    - Three-outcome determination

11. **Implement ValidateClauseAsync in ContractService**
    - Lazy validation with staleness check
    - Context building from contract data
    - Result caching

12. **Implement ProcessPeriodicClauseValidationsAsync**
    - Query due validations
    - Execute and publish events
    - Update timestamps

### Phase 4: Regenerate and Test

13. **Run code generation**
    - `scripts/generate-all-services.sh`

14. **Build and verify**
    - `dotnet build`
    - Fix any compilation errors

15. **Test**
    - Unit tests for TemplateSubstitutor
    - Unit tests for ResponseValidator
    - Integration test via lib-connect WebSocket routing

---

## File Locations Summary

| Component | Location |
|-----------|----------|
| TemplateSubstitutor | `bannou-service/Utilities/TemplateSubstitutor.cs` |
| PreboundApiModels | `bannou-service/ServiceClients/PreboundApiModels.cs` |
| IServiceNavigator (manual) | `bannou-service/ServiceClients/IServiceNavigator.cs` |
| ServiceNavigator (generated) | `bannou-service/Generated/ServiceNavigator.cs` |
| ResponseValidator | `bannou-service/Utilities/ResponseValidator.cs` |
| Contract schema updates | `schemas/contract-api.yaml` |
| ConnectService migration | `plugins/lib-connect/ConnectService.cs` |
| ContractService validation | `plugins/lib-contract/ContractService.cs` |

---

## Key Design Decisions

1. **lib-connect FIRST** - Validates infrastructure before lib-contract uses it

2. **Explicit failure on missing variables** - No silent empty strings. If a template has `{{foo}}` and context lacks `foo`, throw `TemplateSubstitutionException`

3. **Three-outcome validation** - Success/PermanentFailure/TransientFailure is critical for contracts. Don't breach someone because their wallet service was temporarily down.

4. **Lazy validation with configurable staleness** - Default 15 seconds. When accessing a clause's validation status, if the cached result is older than `staleAfterSeconds`, re-validate automatically.

5. **Variable substitution in validation criteria** - `expected: "{{terms.required_amount}}"` allows dynamic thresholds based on contract terms.

6. **Declarative validation schema** - The validation schema is data, not code. Can be stored in templates, edited without code changes, validated at template creation.

7. **ServiceNavigator as execution hub** - All service-to-service calls already go through ServiceNavigator. Prebound APIs are another way to make those calls.

8. **JsonPath.Net for path queries** - MIT licensed, well-maintained, implements RFC 9535.

---

## Dependencies

### New NuGet Package

```xml
<PackageReference Include="JsonPath.Net" Version="2.2.0" />
```

Add to `bannou-service/bannou-service.csproj`.

### Existing Dependencies Used

- `System.Text.Json` - JSON parsing
- `System.Text.RegularExpressions` - Template variable matching
- `IHttpClientFactory` - HTTP client pooling
- `IServiceAppMappingResolver` - Service-to-app-id resolution

---

## Connect Service Current Implementation Reference

The code to migrate is in `plugins/lib-connect/ConnectService.cs`, method `RouteToServiceAsync()` (lines ~1129-1310).

Key aspects:
- Parses endpoint key format: `"servicename:METHOD:/path"`
- Resolves app-id via `_appMappingResolver.GetAppIdForService(serviceName)`
- Builds URL: `{bannouHttpEndpoint}/{path.TrimStart('/')}`
- Sets headers: `bannou-app-id`, `X-Bannou-Session-Id`
- Uses `ByteArrayContent` for zero-copy payload forwarding
- Creates `HttpClient` via `IHttpClientFactory.CreateClient(HttpClientName)`
- Handles timeouts, cancellation, HTTP errors
- Maps HTTP status to `ResponseCodes` for WebSocket response

This logic moves to `ServiceNavigator.ExecuteRawApiAsync()`, and ConnectService calls that instead.

---

## Example Usage

### Template Substitution

```csharp
var template = @"{
  ""source_wallet"": ""{{employer.wallet_id}}"",
  ""target_wallet"": ""{{employee.wallet_id}}"",
  ""amount"": {{terms.payment_amount}},
  ""currency"": ""{{terms.currency_id}}"",
  ""reference"": ""contract:{{contract.id}}""
}";

var context = new Dictionary<string, object?>
{
    ["employer"] = new { wallet_id = "wallet-abc-123" },
    ["employee"] = new { wallet_id = "wallet-def-456" },
    ["terms"] = new { payment_amount = 500, currency_id = "gold" },
    ["contract"] = new { id = "contract-789" }
};

var json = TemplateSubstitutor.Substitute(template, context);
// Result:
// {
//   "source_wallet": "wallet-abc-123",
//   "target_wallet": "wallet-def-456",
//   "amount": 500,
//   "currency": "gold",
//   "reference": "contract:contract-789"
// }
```

### Clause Validation

```yaml
# In contract template
clauseValidations:
  - clauseCode: employer_has_funds
    checkMode: lazy
    staleAfterSeconds: 15
    validationApi:
      serviceName: currency
      endpoint: /currency/wallet/balance
      payloadTemplate: |
        {
          "wallet_id": "{{employer.wallet_id}}",
          "currency_id": "{{terms.payment_currency}}"
        }
    validationSchema:
      successCriteria:
        - type: status_code
          expected: [200]
        - type: json_path_gte
          path: $.balance
          expected: "{{terms.payment_amount}}"
      permanentFailureCriteria:
        - type: status_code
          expected: [404]
        - type: json_path_equals
          path: $.error_code
          expected: WALLET_CLOSED
```

---

## Open Questions (Resolved)

1. **JsonPath library** - JsonPath.Net (MIT licensed) ✓
2. **Validation schema location** - In `contract-api.yaml`, NOT a common schema ✓
3. **lib-connect as first user** - YES, mandatory, validates the path works ✓
4. **Lazy validation staleness** - Default 15 seconds, configurable ✓
