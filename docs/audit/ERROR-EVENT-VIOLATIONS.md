# Error Event Violations Audit

Generated: 2026-01-03
Status: In Progress

This document tracks all locations where errors are logged or 5xx status codes are returned
WITHOUT accompanying error event publishing via `TryPublishErrorAsync`.

## Exclusions (Not Violations)

These are correctly excluded from needing error events:
- `lib-messaging/` - Circular dependency (can't publish events about event publishing)
- `bannou-service/Program.cs` - Before messaging starts
- `lib-state/` internal services - Infrastructure level
- `lib-mesh/` internal services - Infrastructure level
- Orchestrator backends - Control plane level
- `LogWarning` for expected conditions - User errors, validation failures

---

## HIGH PRIORITY

### 1. lib-auth/Services/TokenService.cs

| Line | LogError Call | Status |
|------|---------------|--------|
| 87 | `LogError(ex, "Failed to fetch subscriptions for account...")` | [ ] TODO |
| 172 | `LogError(ex, "Failed to validate refresh token")` | [ ] TODO |
| 268 | `LogError(ex, "Error during token validation")` | [ ] TODO |
| 286 | `LogError(ex, "Error extracting session_key from JWT")` | [ ] TODO |

### 2. lib-auth/Services/SessionService.cs

| Line | LogError Call | Status |
|------|---------------|--------|
| 113 | `LogError(ex, "Failed to get sessions for account...")` | [ ] TODO |
| 143 | `LogError(ex, "Failed to add session to account index...")` | [ ] TODO |
| 179 | `LogError(ex, "Failed to remove session from account index...")` | [ ] TODO |
| 199 | `LogError(ex, "Failed to add reverse index...")` | [ ] TODO |
| 237 | `LogError(ex, "Error finding session key...")` | [ ] TODO |
| 328 | `LogError(ex, "Failed to invalidate sessions...")` | [ ] TODO |
| 354 | `LogError(ex, "Failed to publish SessionInvalidatedEvent...")` | [ ] TODO |

### 3. lib-auth/Services/OAuthProviderService.cs

| Line | LogError Call | Status |
|------|---------------|--------|
| 116 | OAuth error | [ ] TODO |
| 182 | OAuth error | [ ] TODO |
| 250 | OAuth error | [ ] TODO |
| 263 | OAuth error | [ ] TODO |
| 306 | OAuth error | [ ] TODO |
| 377 | OAuth error | [ ] TODO |
| 383 | OAuth error | [ ] TODO |
| 390 | OAuth error | [ ] TODO |
| 407 | OAuth error | [ ] TODO |
| 423 | OAuth error | [ ] TODO |
| 432 | OAuth error | [ ] TODO |
| 441 | OAuth error | [ ] TODO |

### 4. lib-permissions/PermissionsServiceEvents.cs

| Line | LogError Call | Status |
|------|---------------|--------|
| 151 | `LogError("Failed to register permissions for service...")` | [ ] TODO |
| 158 | `LogError(ex, "Error handling service registration event...")` | [ ] TODO |
| 197 | `LogError(ex, "Error handling session state change event...")` | [ ] TODO |
| 285 | `LogError(ex, "Failed to process session.updated event...")` | [ ] TODO |
| 360 | `LogError(ex, "Failed to process session.connected event...")` | [ ] TODO |
| 397 | `LogError(ex, "Failed to process session.disconnected event...")` | [ ] TODO |

---

## MEDIUM PRIORITY

### 5. lib-connect/BannouSessionManager.cs

| Line | LogError Call | Status |
|------|---------------|--------|
| 67 | `LogError(..., "Failed to store session service mappings")` | [ ] TODO |
| 91 | `LogError(..., "Failed to retrieve session service mappings")` | [ ] TODO |
| 118 | `LogError(..., "Failed to store connection state")` | [ ] TODO |
| 142 | `LogError(..., "Failed to retrieve connection state")` | [ ] TODO |
| 209 | `LogError(..., "Failed to store reconnection token")` | [ ] TODO |
| 233 | `LogError(..., "Failed to validate reconnection token")` | [ ] TODO |

### 6. lib-connect/ConnectService.cs

| Line | LogError Call | Status |
|------|---------------|--------|
| 533 | `LogError("Auth service returned null validation response")` | [ ] TODO |
| 597 | `LogError(ex, "JWT validation failed with exception")` | [ ] TODO |
| 1102 | `LogError("SessionShortcut route missing TargetGuid...")` | [ ] TODO |

### 7. lib-documentation/DocumentationService.cs

| Line | Method | Status |
|------|--------|--------|
| 1968 | `BindRepositoryAsync` | [ ] TODO |
| 2027 | `UnbindRepositoryAsync` | [ ] TODO |
| 2071 | `SyncRepositoryAsync` | [ ] TODO |
| 2113 | `GetRepositoryStatusAsync` | [ ] TODO |
| 2165 | `ListRepositoryBindingsAsync` | [ ] TODO |
| 2221 | `UpdateRepositoryBindingAsync` | [ ] TODO |

### 8. lib-servicedata/ServicedataService.cs

| Line | Method | Status |
|------|--------|--------|
| 92 | `ListServicesAsync` | [ ] TODO |
| 139 | `GetServiceAsync` | [ ] TODO |
| 211 | `CreateServiceAsync` | [ ] TODO |
| 268 | `UpdateServiceAsync` | [ ] TODO |

---

## ACTOR SYSTEM

### 9. lib-actor/Runtime/ActorRunner.cs

| Line | LogError Call | Status |
|------|---------------|--------|
| 323 | `LogError(ex, "Error in actor behavior loop iteration...")` | [ ] TODO |
| 411 | `LogError(ex, "Actor failed to load behavior...")` | [ ] TODO |
| 644 | `LogError(ex, "Actor failed to persist state")` | [ ] TODO |

### 10. lib-actor/PoolNode/ActorPoolNodeWorker.cs

| Line | LogError Call | Status |
|------|---------------|--------|
| 71 | `LogError("PoolNodeId and PoolNodeAppId must be configured...")` | [ ] TODO |
| 173 | `LogError(ex, "Failed to spawn actor...")` | [ ] TODO |
| 232 | `LogError(ex, "Failed to stop actor...")` | [ ] TODO |

### 11. lib-actor/ActorServiceEvents.cs

| Line | LogError Call | Status |
|------|---------------|--------|
| 121 | Event handler error | [ ] TODO |
| 175 | Event handler error | [ ] TODO |
| 202 | Event handler error | [ ] TODO |
| 229 | Event handler error | [ ] TODO |
| 256 | Event handler error | [ ] TODO |
| 287 | Event handler error | [ ] TODO |

---

## LOWER PRIORITY

### 12. lib-documentation/Services/GitSyncService.cs

| Line | Status |
|------|--------|
| 62 | [ ] TODO |
| 67 | [ ] TODO |
| 143 | [ ] TODO |
| 189 | [ ] TODO |
| 215 | [ ] TODO |
| 242 | [ ] TODO |
| 271 | [ ] TODO |
| 330 | [ ] TODO |
| 393 | [ ] TODO |

### 13. lib-documentation/Services/RedisSearchIndexService.cs

| Line | Status |
|------|--------|
| 114 | [ ] TODO |
| 161 | [ ] TODO |
| 255 | [ ] TODO |

### 14. lib-voice/Clients/KamailioClient.cs

| Line | Status |
|------|--------|
| 70 | [ ] TODO |
| 94 | [ ] TODO |
| 109 | [ ] TODO |
| 141 | [ ] TODO |

### 15. lib-voice/Clients/RtpEngineClient.cs

| Line | Status |
|------|--------|
| 490 | [ ] TODO |

### 16. lib-voice/Services/ScaledTierCoordinator.cs

| Line | Status |
|------|--------|
| 153 | [ ] TODO |

---

## Correct Pattern Reference

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed operation {Operation}", operationName);
    await _messageBus.TryPublishErrorAsync(
        serviceName: "myservice",
        operation: operationName,
        errorType: ex.GetType().Name,
        message: ex.Message,
        dependency: "state",
        endpoint: "post:/myservice/method",
        stack: ex.StackTrace);
    return (StatusCodes.InternalServerError, null);
}
```

For helper services that don't have direct IMessageBus access, they need to either:
1. Get IMessageBus injected
2. Use a callback/delegate pattern to report errors to parent service
3. Throw and let parent service handle error event publishing
