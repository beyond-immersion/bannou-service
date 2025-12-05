# AccountDeletionSessionInvalidation Test Failure Analysis

**Date**: 2025-12-04
**Test**: `TestAccountDeletionSessionInvalidation` in `http-tester/Tests/AccountTestHandler.cs`
**Status**: PASSES locally, FAILS consistently in CI
**Error Message**: `"Sessions still exist after account deletion after 30s: 1 sessions found"`

---

## Critical Constraint: All Other Tests Pass

**This is the ONLY failing test.** Every other test passes:
- ✅ All other HTTP tests pass locally AND in CI
- ✅ All WebSocket edge tests pass locally AND in CI
- ✅ All unit tests pass
- ✅ All infrastructure tests pass

**This constraint eliminates entire categories of potential causes:**
- ❌ NOT schema/generation issues (would break multiple tests)
- ❌ NOT plugin loading issues (would break multiple tests)
- ❌ NOT networking issues (would break multiple tests)
- ❌ NOT configuration differences (would break multiple tests)
- ❌ NOT serialization issues (would fail locally too)
- ❌ NOT component availability (would break multiple tests)

**The issue MUST be specific to what THIS test does that NO OTHER test does.**

---

## Test Breakdown: Line-by-Line Analysis

### Location: `http-tester/Tests/AccountTestHandler.cs:699-805`

```csharp
private static async Task<TestResult> TestAccountDeletionSessionInvalidation(ITestClient client, string[] args)
{
    // Tests the complete event chain:
    // 1. Account deleted → AccountsService publishes account.deleted
    // 2. AuthService receives account.deleted → invalidates all sessions → publishes session.invalidated
    // 3. Session validation should fail after account deletion
```

### Step 1: Create Clients (Lines 707-710)
```csharp
var accountsClient = new AccountsClient();
var authClient = new AuthClient();
```
- Uses NSwag-generated clients
- Same pattern as ALL other passing tests
- **NOT THE ISSUE** (other tests use identical pattern)

### Step 2: Create Account (Lines 712-727)
```csharp
var testUsername = $"sessiontest_{DateTime.Now.Ticks}";
var testPassword = "TestPassword123!";

var createRequest = new CreateAccountRequest
{
    DisplayName = testUsername,
    Email = $"{testUsername}@example.com",
    PasswordHash = BCrypt.Net.BCrypt.HashPassword(testPassword)
};

var createResponse = await accountsClient.CreateAccountAsync(createRequest);
```
- Creates unique test account
- Same pattern as `TestDeleteAccount` which **PASSES**
- **NOT THE ISSUE** (other tests use identical pattern)

### Step 3: Login to Create Session (Lines 729-740)
```csharp
var loginRequest = new LoginRequest
{
    Email = $"{testUsername}@example.com",
    Password = testPassword
};

var loginResponse = await authClient.LoginAsync(loginRequest);
var accessToken = loginResponse.AccessToken;
```
- Standard login flow
- Same pattern as `TestLogout`, `TestCompleteAuthFlow` which **PASS**
- **NOT THE ISSUE** (other tests use identical pattern)

### Step 4: Verify Session Exists (Lines 742-749)
```csharp
var sessionsResponse = await ((AuthClient)authClient)
    .WithAuthorization(accessToken)
    .GetSessionsAsync();
if (sessionsResponse.Sessions == null || !sessionsResponse.Sessions.Any())
    return TestResult.Failed("No sessions found after login");

var sessionCount = sessionsResponse.Sessions.Count;
```
- Confirms session was created
- Same pattern as other auth tests which **PASS**
- **NOT THE ISSUE** (other tests use identical pattern)

### Step 5: Delete Account (Lines 751-752) ⚠️ **TRIGGER POINT**
```csharp
await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });
```
- This calls `AccountsService.DeleteAccountAsync()`
- Which publishes `AccountDeletedEvent` via `PublishAccountDeletedEventAsync()`
- Same deletion pattern as `TestDeleteAccount` which **PASSES**
- **BUT `TestDeleteAccount` does NOT verify session invalidation**

### Step 6: Wait for Event Processing (Lines 754-793) ⚠️ **UNIQUE BEHAVIOR**
```csharp
const int maxRetries = 15;
const int retryDelayMs = 2000;

for (var attempt = 1; attempt <= maxRetries; attempt++)
{
    await Task.Delay(retryDelayMs);

    try
    {
        var sessionsAfterDeletion = await ((AuthClient)authClient)
            .WithAuthorization(accessToken)
            .GetSessionsAsync();

        // Check if sessions were invalidated
        if (sessionsAfterDeletion.Sessions == null || !sessionsAfterDeletion.Sessions.Any())
        {
            return TestResult.Successful(...);
        }
        // Sessions still exist, continue retrying
    }
    catch (ApiException ex) when (ex.StatusCode == 401)
    {
        // Expected - token is now invalid
        return TestResult.Successful(...);
    }
}
```

**THIS IS THE UNIQUE BEHAVIOR!** This test:
1. Deletes an account
2. Waits for an **asynchronous Dapr pub/sub event chain** to complete
3. Polls to verify sessions were deleted by the event handler

**Total wait time: 15 retries × 2 seconds = 30 seconds**

---

## What Makes This Test Unique

### Comparison with ALL Other Tests

| Test | Uses Pub/Sub Events | Has Fallback | Passes CI |
|------|--------------------| -------------|-----------|
| `TestDeleteAccount` | No (just deletes) | N/A | ✅ |
| `TestLogout` | No (direct API) | N/A | ✅ |
| `TestLogoutAllSessions` | No (direct API) | N/A | ✅ |
| `TestDaprEventSubscription` (Permissions) | Yes | **YES (HTTP fallback)** | ✅ |
| `TestSessionStateChangeEvent` (Permissions) | Yes | **YES (HTTP fallback)** | ✅ |
| **`TestAccountDeletionSessionInvalidation`** | Yes | **NO** | ❌ |

### The Permissions Tests Have a Fallback

Looking at `PermissionsTestHandler.cs:1509-1525`:
```csharp
// Try direct HTTP POST to the event endpoint as a fallback diagnostic
try
{
    using var httpClient = new HttpClient();
    var eventJson = System.Text.Json.JsonSerializer.Serialize(serviceRegistrationEvent);
    var content = new StringContent(eventJson, System.Text.Encoding.UTF8, "application/json");
    var directResponse = await httpClient.PostAsync(
        "http://127.0.0.1:3500/v1.0/invoke/bannou/method/PermissionsEvents/handle-service-registration",
        content);
}
```

**The permissions tests POST directly to the event handler endpoint, bypassing Dapr pub/sub!**

This is why they pass: they don't actually rely on pub/sub delivery working correctly.

### The Edge-Tester Also Tests Account Deletion → Session Invalidation

`ConnectWebSocketTestHandler.cs:533-800` has `TestAccountDeletionDisconnectsWebSocket`:
- Creates account, establishes WebSocket, deletes account, waits for WebSocket disconnect

**But it verifies a DIFFERENT outcome:** WebSocket close, not session data deletion.

The WebSocket close is triggered by `SessionInvalidatedEvent` → `ConnectService`.

If sessions are found but not properly indexed, `SessionInvalidatedEvent` would still be published, WebSocket would close, but the session data would remain in Redis.

---

## The Event Chain Under Test

```
AccountsService.DeleteAccountAsync()
    │
    ├─▶ Soft-delete account in MySQL
    ├─▶ Remove indexes
    └─▶ PublishAccountDeletedEventAsync()
            │
            └─▶ _daprClient.PublishEventAsync("bannou-pubsub", "account.deleted", event)
                    │
                    ▼
            [RabbitMQ via Dapr]
                    │
                    ▼
            AuthController.HandleAccountDeletedEvent()
                    │
                    └─▶ Deserialize event
                    └─▶ _implementation.OnEventReceivedAsync("account.deleted", typedEventData)
                            │
                            ▼
                    AuthService.HandleAccountDeletedEventAsync()
                            │
                            └─▶ InvalidateAllSessionsForAccountAsync(accountId)
                                    │
                                    ▼
                            [Look up sessions by account]
                            var sessionKeys = await _daprClient.GetStateAsync<List<string>>(
                                REDIS_STATE_STORE,
                                "account-sessions:{accountId}");
                                    │
                                    ▼
                            [If sessions found, delete them]
                            [If NO sessions found, RETURN EARLY - no deletion!]
```

---

## Critical Code Path: Session Index Lookup

### In `InvalidateAllSessionsForAccountAsync` (AuthService.cs:2159-2206):
```csharp
private async Task InvalidateAllSessionsForAccountAsync(Guid accountId, ...)
{
    // Get session keys directly from the account index
    var indexKey = $"account-sessions:{accountId}";
    var sessionKeys = await _daprClient.GetStateAsync<List<string>>(
        REDIS_STATE_STORE,
        indexKey,
        cancellationToken: CancellationToken.None);

    if (sessionKeys == null || !sessionKeys.Any())
    {
        _logger.LogInformation("No sessions found for account {AccountId}", accountId);
        return;  // ⚠️ EARLY RETURN - sessions not deleted!
    }
    // ... delete sessions ...
}
```

**If the `account-sessions:{accountId}` index is empty or doesn't exist when the event handler runs, sessions are NOT deleted.**

---

## Key Questions for Investigation

### 1. When is `account-sessions:{accountId}` populated?

The session must be indexed under this key for `InvalidateAllSessionsForAccountAsync` to find it.

- Does `LoginAsync` write to this index?
- Is the write synchronous or eventually consistent?
- Is there a race condition where the index write hasn't completed when the event handler runs?

### 2. What does `GetSessionsAsync` use for lookup?

The test verifies sessions exist via:
```csharp
var sessionsResponse = await authClient.GetSessionsAsync();
```

- Does this use the same `account-sessions:{accountId}` index?
- Or does it use a different lookup mechanism (e.g., JWT claims)?

**If `GetSessionsAsync` uses JWT claims but `InvalidateAllSessionsForAccountAsync` uses the Redis index, they could return different results.**

### 3. Why would this work locally but fail in CI?

Possible timing-related scenarios:
1. **Dapr subscription not ready**: Event published before consumer is set up
2. **Redis index not written**: Session created but index write hasn't propagated
3. **Event delivery timing**: Faster execution in CI causes race condition

---

## Comparison: WebSocket Test That PASSES

The edge-tester `TestAccountDeletionDisconnectsWebSocket` also tests the account deletion flow:

```csharp
// Step 4: Delete the account via WebSocket binary protocol
var deleteRequestBody = new { accountId = accountId.ToString() };
// ... sends via WebSocket ...

// Step 5: Wait for user's WebSocket to be closed by the server
while (userWebSocket.State == WebSocketState.Open)
{
    var result = await userWebSocket.ReceiveAsync(receiveBuffer, receiveCts.Token);
    if (result.MessageType == WebSocketMessageType.Close)
    {
        // SUCCESS - WebSocket was closed
        return true;
    }
}
```

**Key difference:** This test verifies the WebSocket was closed, NOT that session data was deleted from Redis.

The WebSocket close is triggered by `ConnectService` receiving `SessionInvalidatedEvent`, which is published by `AuthService` even if it finds sessions to invalidate.

---

## Hypothesis: Session Index Race Condition

**Theory**: The `account-sessions:{accountId}` Redis index is written asynchronously or with eventual consistency. In CI (faster execution), the event handler runs before the index is fully written.

**Evidence supporting this theory**:
1. The test passes locally (slower execution gives time for index write)
2. The test fails in CI (faster execution causes race)
3. Other tests don't rely on this specific index
4. The edge-tester test doesn't verify session DATA deletion

**Evidence against this theory**:
1. The test has a 30-second retry loop - should be enough time
2. Dapr state operations should be synchronous

---

## Next Steps for Investigation

1. **Trace session creation**: Find where `account-sessions:{accountId}` is populated during login

2. **Compare GetSessionsAsync vs InvalidateAllSessionsForAccountAsync**: Verify they use the same lookup mechanism

3. **Add logging**: Add debug logging to `InvalidateAllSessionsForAccountAsync` to see what it finds

4. **Check Redis directly**: Query Redis in CI to see if the index exists when the event handler runs

5. **Verify event delivery**: Add logging to confirm the event IS being delivered in CI

---

## ❌ DISPROVEN THEORY: CloudEvents Unwrapping

**This theory was investigated and disproven.**

The hypothesis was that CloudEvents format wasn't being unwrapped properly in generated event handlers. However, this theory **fails the critical constraint test**:

1. **Would fail locally too** - CloudEvents format is used in both environments with identical code
2. **Would affect other event handlers** - All generated handlers use the same pattern

The CloudEvents theory cannot explain why the test passes locally but fails only in CI.

---

## 🔍 Discovery: HTTP Fallback Pattern Was Obscuring Test Results

During investigation, we discovered that the permissions tests had a **direct HTTP POST fallback** that was calling event handler endpoints directly, bypassing Dapr pub/sub entirely:

```csharp
// This was in PermissionsTestHandler.cs - REMOVED
var directResponse = await httpClient.PostAsync(
    "http://127.0.0.1:3500/v1.0/invoke/bannou/method/PermissionsEvents/handle-service-registration",
    content);
```

This fallback meant the permissions tests would **pass regardless of whether Dapr pub/sub actually worked**.

**Cleanup performed:**
1. Removed `PermissionsEventsController.cs` entirely (was never intended as an API)
2. Removed HTTP fallback patterns from permission tests
3. Removed `TestDaprEventSubscription` and `TestSessionStateChangeEvent` tests (no handler exists)

---

## Investigation Continues

The actual root cause for why this specific test passes locally but fails in CI remains unidentified.

**What we know:**
- This is the ONLY test that tests the full account deletion → session invalidation event chain without fallbacks
- The event chain: AccountsService → `account.deleted` → AuthService → `InvalidateAllSessionsForAccountAsync`
- The 30-second retry loop should be sufficient for event delivery
- Other Dapr pub/sub operations work (ConnectEventsController handles `session.invalidated` and `permissions.capabilities-updated`)

**Key questions remaining:**
1. Is the `account.deleted` event being delivered to AuthController in CI?
2. Is the event being deserialized correctly?
3. Is `InvalidateAllSessionsForAccountAsync` being called with the correct accountId?
4. Is the `account-sessions:{accountId}` index populated when the event handler runs?

---

## Files for Reference

- **Failing test**: `http-tester/Tests/AccountTestHandler.cs:699-805`
- **Event handler**: `lib-auth/AuthService.cs:2105-2206`
- **Event publisher**: `lib-accounts/AccountsService.cs:1131-1152`
- **Session invalidation**: `lib-auth/AuthService.cs:2159-2206`
- **Generated subscription**: `lib-auth/Generated/AuthController.cs:429-461`
- **Passing WebSocket test**: `edge-tester/Tests/ConnectWebSocketTestHandler.cs:533-800`
