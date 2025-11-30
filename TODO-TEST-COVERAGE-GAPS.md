# Test Coverage Gaps and Issues

**Created**: 2025-11-30
**Status**: Active - needs immediate attention

## High Priority: Stub/Placeholder Tests

These test files contain TODO comments with no actual tests:

### 1. lib-auth.tests/AuthServiceTests.cs:80
```csharp
// TODO: Add service-specific unit tests here
```
**Missing**: Unit tests for AuthService business logic

### 2. lib-behavior.tests/BehaviorServiceTests.cs:35
```csharp
// TODO: Add service-specific unit tests here
```
**Missing**: Unit tests for BehaviorService business logic

### 3. lib-accounts.tests/AccountsServiceTests.cs:39
```csharp
// TODO: Add service-specific unit tests here
```
**Missing**: Unit tests for AccountsService business logic

### 4. lib-permissions.tests/PermissionsServiceTests.cs:39
```csharp
// TODO: Add service-specific unit tests here
```
**Missing**: Unit tests for PermissionsService business logic

### 5. lib-game-session.tests/GameSessionServiceTests.cs:24
```csharp
// TODO: Add service-specific tests based on schema operations
```
**Missing**: Unit tests for GameSessionService business logic

---

## Medium Priority: Half-Assed Tests

### 1. ConnectTestHandler.cs:94 - Accepting ANY API Exception as Success
```csharp
catch (ApiException ex)
{
    // API exceptions are expected for proxy calls without proper auth
    return TestResult.Successful($"Internal proxy responded with expected API error: {ex.StatusCode}");
}
```
**Problem**: Returns success for ANY API exception instead of checking for expected status codes
**Fix**: Should validate specific expected error codes (401, 403) not just "any error"

### 2. AccountTestHandler.cs:220 - Skipped Test Marked as Successful
```csharp
return TestResult.Successful("SKIPPED: Event-driven session invalidation not yet implemented");
```
**Problem**: A skipped test should not be reported as "Successful"
**Fix**: Add a proper TestResult.Skipped() result type or fail the test with explanation

### 3. AccountTestHandler.cs:294-299 - Accepting Connection Errors as Success
```csharp
catch (Exception ex) when (ex.Message.Contains("Connection refused") ||
                          ex.Message.Contains("SecurityTokenSignatureKeyNotFoundException") ||
                          ex.Message.Contains("Signature validation failed"))
{
    return TestResult.Successful($"Account deletion → session invalidation flow verified: JWT validation failed");
}
```
**Problem**: Treating various error messages as success - these could be legitimate failures
**Fix**: Be specific about expected error conditions, don't accept arbitrary errors

### 4. ✅ FIXED: OrchestratorTestHandler.cs:268-276 - API Errors Marked as Partial Success
~~```csharp
if (ex.StatusCode == 400 || ex.StatusCode == 500 || ex.StatusCode == 501)
{
    return TestResult.Successful(...);
}
```~~
**Problem**: 400/500/501 errors were marked as successful
**Resolution**: Fixed on 2025-11-30 - now API errors properly fail the test

---

## Infrastructure Issues

### 1. lib-testing/TestClientFactory.cs - Multiple NotImplementedException
```csharp
throw new NotImplementedException("HttpTestClient needs to be moved from http-tester project");
throw new NotImplementedException("WebSocketTestClient belongs in service integration testing");
```
**Status**: Test infrastructure is incomplete

### 2. unit-tests/PluginLoaderTests.cs:310 - Test Skipped Due to Architecture
```csharp
// Skip the global test for now as it requires Program.Configuration to be reinitialized
```
**Problem**: Test skipped due to coupling with global state

---

## Actions Required

1. **Immediate**: Investigate Docker Compose container detection issue
2. **High Priority**: Implement actual unit tests for all stub test files
3. **High Priority**: Fix half-assed error handling in integration tests
4. **Medium Priority**: Add TestResult.Skipped() support for legitimate skip scenarios
5. **Medium Priority**: Complete TestClientFactory infrastructure

---

## Notes

- All tests marked as "Successful" should actually validate expected behavior
- Error conditions should be explicit and specific, not catch-all
- Skipped tests should be clearly marked as skipped, not successful
- Integration tests need to test actual functionality, not just "endpoint is reachable"
