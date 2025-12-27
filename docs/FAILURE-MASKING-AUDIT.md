# Failure Masking Audit Report

> **Generated**: 2025-12-26
> **Scope**: All service implementations in bannou-service and lib-* directories
> **Purpose**: Identify cases where methods return success/OK when they should return errors

---

## Executive Summary

This audit identified **70+ failure masking patterns** across the Bannou codebase where methods catch exceptions or encounter errors but return success/empty data instead of propagating the error. These patterns can cause silent data loss, incorrect assumptions by callers, and difficult-to-debug production issues.

### Severity Distribution

| Severity | Count | Description |
|----------|-------|-------------|
| **CRITICAL** | 8 | Returns OK/200 on actual failure, stub methods return OK |
| **HIGH** | 35+ | Empty lists/null on error, validation bypassed on exception |
| **MEDIUM** | 20+ | Silent exception swallowing with logging |
| **LOW** | 10+ | Intentional degradation patterns (may be acceptable) |

---

## CRITICAL SEVERITY ISSUES

### 1. BehaviorService - All Stub Methods Return OK

**File**: `/home/lysander/repos/bannou/lib-behavior/BehaviorService.cs`

All 6 unimplemented methods return `StatusCodes.OK` with null:

| Method | Lines |
|--------|-------|
| `CompileAbmlBehaviorAsync` | 47-48 |
| `CompileBehaviorStackAsync` | 70-72 |
| `ValidateAbmlAsync` | 94-96 |
| `GetCachedBehaviorAsync` | 118-120 |
| `ResolveContextVariablesAsync` | 143-145 |
| `InvalidateCachedBehaviorAsync` | 167-169 |

```csharp
_logger.LogWarning("Method CompileAbmlBehaviorAsync called but not implemented");
return (StatusCodes.OK, null);  // WRONG: Should be NotImplemented (501)
```

**Fix**: Return `StatusCodes.NotImplemented` or `StatusCodes.ServiceUnavailable`

---

### 2. WebsiteService - All 17 Stub Methods Return OK

**File**: `/home/lysander/repos/bannou/lib-website/WebsiteService.cs`

Every method returns `StatusCodes.OK` with null data despite being unimplemented. Lines: 47-48, 71-72, 96-97, 120-121, 145-146, 170-171, 194-195, 219-220, 241-242, 266-267, 291-292, 315-316, 339-340, 363-364, 387-388, 411-412, 436-437

**Fix**: Return `StatusCodes.NotImplemented` (501)

---

### 3. MessagingService.ListTopicsAsync - Returns OK on Exception

**File**: `/home/lysander/repos/bannou/lib-messaging/MessagingService.cs`
**Lines**: 243-258

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to list topics");
    await _messageBus.TryPublishErrorAsync(/* ... */);
    return (StatusCodes.OK, new ListTopicsResponse { Topics = new List<TopicInfo>() });  // WRONG!
}
```

**Fix**: Return `StatusCodes.InternalServerError`

---

### 4. LocationService.SeedLocationsAsync - Returns OK After Exception

**File**: `/home/lysander/repos/bannou/lib-location/LocationService.cs`
**Lines**: 1193-1208

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error seeding locations");
    errors.Add($"Unexpected error: {ex.Message}");
    return (StatusCodes.OK, new SeedLocationsResponse { /* partial data */ });  // WRONG!
}
```

**Fix**: Return `StatusCodes.InternalServerError` or `StatusCodes.PartialContent`

---

### 5. VoiceService.AnswerPeerAsync - Returns OK When Operation Cannot Complete

**File**: `/home/lysander/repos/bannou/lib-voice/VoiceService.cs`
**Lines**: 554-558

```csharp
if (_clientEventPublisher == null)
{
    _logger.LogDebug("Client event publisher not available, cannot send answer to target");
    return (StatusCodes.OK, null);  // WRONG: Answer was NOT sent!
}
```

**Fix**: Return `StatusCodes.ServiceUnavailable`

---

### 6. CharacterService.ListCharactersAsync - Returns OK with Empty When Feature Incomplete

**File**: `/home/lysander/repos/bannou/lib-character/CharacterService.cs`
**Lines**: 366-379

```csharp
// Without realm filter, we need to scan all realms (less efficient)
// For now, return empty - in production you'd want a global index
_logger.LogWarning("ListCharacters called without realmId filter - returning empty for efficiency");
return (StatusCodes.OK, new CharacterListResponse { Characters = new List<CharacterResponse>(), TotalCount = 0 });
```

**Fix**: Return `StatusCodes.BadRequest` indicating realm filter is required, or implement the feature

---

## HIGH SEVERITY ISSUES

### Session/Auth Services - Empty Lists on Redis Failure

These mask Redis failures as "no data exists":

| File | Method | Lines | Issue |
|------|--------|-------|-------|
| `lib-auth/Services/SessionService.cs` | `GetAccountSessionsAsync` | 111-115 | Returns empty list on Redis failure |
| `lib-auth/AuthService.cs` | `GetAccountSessionsAsync` | 1559-1563 | Returns empty list on Redis failure |
| `lib-accounts/AccountsService.cs` | `GetAuthMethodsForAccountAsync` | 793-798 | Returns empty list on exception |

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to get sessions for account {AccountId}", accountId);
    return new List<SessionInfo>();  // Caller can't distinguish "no sessions" from "Redis down"
}
```

**Fix**: Propagate exception or use result type `(List<T>?, Exception?)`

---

### Connect Service - Null Returns Mask Errors

| File | Method | Lines | Issue |
|------|--------|-------|-------|
| `lib-connect/BannouSessionManager.cs` | `GetSessionServiceMappingsAsync` | 88-92 | Returns null on error |
| `lib-connect/BannouSessionManager.cs` | `GetConnectionStateAsync` | 139-143 | Returns null on error |
| `lib-connect/BannouSessionManager.cs` | `ValidateReconnectionTokenAsync` | 221-225 | Returns null on error |
| `lib-connect/BannouSessionManager.cs` | `RestoreSessionFromReconnectionAsync` | 327-332 | Returns null on error |

**Impact**: Valid reconnection tokens rejected during transient Redis issues

**Fix**: Propagate exceptions

---

### Connect Service - Event Queue Failures

**File**: `/home/lysander/repos/bannou/lib-connect/ClientEvents/ClientEventQueueManager.cs`

| Method | Lines | Issue |
|--------|-------|-------|
| `DequeueEventsAsync` | 154-159 | Returns empty list on failure - events may be lost |
| `GetQueuedEventCountAsync` | 185-188 | **SILENT CATCH** - returns 0, no logging at all |

```csharp
catch
{
    return 0;  // WORST CASE: No logging, masks ALL exceptions as "0 events"
}
```

**Fix**: At minimum log the exception. Return -1 or throw to indicate error.

---

### Validation Methods Return Success on Service Unavailability

These bypass validation when dependent services are down:

| File | Method | Lines | Dangerous Behavior |
|------|--------|-------|-------------------|
| `lib-character/CharacterService.cs` | `ValidateRealmAsync` | 461-466 | Returns `(true, true)` when RealmService unavailable |
| `lib-character/CharacterService.cs` | `ValidateSpeciesAsync` | 493-498 | Returns `(true, true)` when SpeciesService unavailable |
| `lib-species/SpeciesService.cs` | `ValidateRealmAsync` | 87-92 | Returns `(true, true)` when RealmService unavailable |

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Could not validate realm {RealmId} - proceeding with operation", realmId);
    return (true, true);  // DANGEROUS: Assumes realm exists and is active!
}
```

**Fix**: Return error status or use a `ValidationResult` type that indicates "validation skipped"

---

### Species Service - Deletes Without Validation

**File**: `/home/lysander/repos/bannou/lib-species/SpeciesService.cs`

| Method | Lines | Issue |
|--------|-------|-------|
| `DeleteSpeciesAsync` | 556-561 | Deletes species without checking character usage when CharacterService unavailable |
| `RemoveFromRealmAsync` | 707-713 | Removes species from realm without checking character usage |

**Impact**: Data integrity - species deleted while characters still reference them

**Fix**: Fail the operation when validation service unavailable

---

### MeshRedisManager - Returns Empty on Redis Failure

**File**: `/home/lysander/repos/bannou/lib-mesh/Services/MeshRedisManager.cs`

| Method | Lines | Issue |
|--------|-------|-------|
| `GetEndpointsForAppIdAsync` | 316-321 | Returns empty list on Redis failure |
| `GetAllEndpointsAsync` | 367-372 | Returns empty list on Redis failure |
| `GetServiceMappingsAsync` | 395-399 | Returns empty dict on Redis failure |
| `GetMappingsVersionAsync` | 474-478 | Returns 0 on Redis failure |

**Impact**: Service discovery fails silently, routing breaks

**Fix**: Throw or use result type

---

## MEDIUM SEVERITY ISSUES

### OrchestratorStateManager - 26+ Silent Exception Catches

**File**: `/home/lysander/repos/bannou/lib-orchestrator/OrchestratorStateManager.cs`

Pattern repeated throughout the file:

```csharp
if (_heartbeatStore == null || _heartbeatIndexStore == null)
{
    _logger.LogWarning("State stores not initialized. Cannot write heartbeat.");
    return;  // Silent return
}
// ... and ...
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to write heartbeat for instance {AppId}", heartbeat.AppId);
    // Exception swallowed, no indication to caller
}
```

**Affected Methods** (partial list):
- `WriteServiceHeartbeatAsync` (145-149, 179-182)
- `UpdateHeartbeatIndexAsync` (190-191, 209-212)
- `GetServiceHeartbeatsAsync` (220-224) - returns empty list
- `GetServiceHeartbeatAsync` (316-320) - returns null
- `WriteServiceRoutingAsync` (359-363, 380-382)
- `GetServiceRoutingsAsync` (422-426) - returns empty dict
- `GetConfigVersionAsync` (589-593, 600-604) - returns 0
- `SaveConfigurationVersionAsync` (612-616, 644-648) - returns 0

**Fix**: Use result types or throw exceptions

---

### Event Publishing Failures Swallowed

Multiple services swallow event publishing failures:

| File | Lines | Pattern |
|------|-------|---------|
| `lib-character/CharacterService.cs` | 722-725, 745-748, 767-770, 789-792, 810-814 | Event failures logged but swallowed |
| `lib-orchestrator/OrchestratorEventManager.cs` | 37-40 | Heartbeat event errors swallowed |
| `lib-orchestrator/ServiceHealthMonitor.cs` | 160-165, 292-295 | Various event failures swallowed |
| `bannou-service/Events/EventPublisherBase.cs` | 41-58 | Returns false instead of throwing |
| `bannou-service/ClientEvents/MessageBusClientEventPublisher.cs` | 84-92, 139-145 | Returns false/count |

**Consideration**: This may be intentional for fire-and-forget, but callers cannot distinguish "event sent" from "event failed"

---

### bannou-service Core - Silent Catches

| File | Lines | Issue |
|------|-------|-------|
| `bannou-service/Controllers/Messages/ApiRequestT.cs` | 34 | Empty catch block during type cast |
| `bannou-service/Plugins/BaseBannouPlugin.cs` | 133-147 | Returns null on logger factory exception |
| `bannou-service/Services/ServiceHeartbeatManager.cs` | 417-427 | `OnHeartbeat` exceptions swallowed without logging |

---

### Background Service Silent Failures

**File**: `/home/lysander/repos/bannou/lib-subscriptions/SubscriptionExpirationService.cs`
**Lines**: 97-101

```csharp
if (stateStoreFactory == null || messageBus == null)
{
    _logger.LogWarning("IStateStoreFactory or IMessageBus not available, skipping expiration check");
    return;  // Expiration check NEVER runs, subscriptions never expire
}
```

**Fix**: This should be an error, not warning - missing infrastructure is critical

---

## RECOMMENDED FIXES BY PRIORITY

### Immediate (P0) - Data Loss / Integrity Risk

1. **ClientEventQueueManager.GetQueuedEventCountAsync** (line 185-188) - Add logging to silent catch
2. **Species deletion without validation** - Fail operation when CharacterService unavailable
3. **Validation methods returning (true, true)** - Return error or validation-skipped status

### High (P1) - Incorrect Status Codes

4. **BehaviorService stub methods** - Return `StatusCodes.NotImplemented`
5. **WebsiteService stub methods** - Return `StatusCodes.NotImplemented`
6. **MessagingService.ListTopicsAsync** - Return `StatusCodes.InternalServerError` on exception
7. **LocationService.SeedLocationsAsync** - Return error status on exception
8. **VoiceService.AnswerPeerAsync** - Return `StatusCodes.ServiceUnavailable`

### Medium (P2) - Ambiguous Results

9. **Session/Auth empty list returns** - Use result type or throw
10. **Connect service null returns** - Propagate exceptions
11. **MeshRedisManager empty returns** - Throw or use result type
12. **OrchestratorStateManager** - Implement consistent error handling pattern

### Low (P3) - Logging Improvements

13. **ServiceHeartbeatManager.OnHeartbeat** - Add logging to catch block
14. **ApiRequestT.cs** - Add logging to empty catch
15. **Event publishing failures** - Document fire-and-forget behavior or return error details

---

## Pattern Recommendations

### For Methods Returning Collections

```csharp
// BAD: Can't distinguish "empty" from "failed"
catch (Exception ex)
{
    _logger.LogError(ex, "Failed");
    return new List<T>();
}

// GOOD: Propagate exception
catch (Exception ex)
{
    _logger.LogError(ex, "Failed");
    throw;
}

// ALTERNATIVE: Result type
public async Task<(List<T>? Data, string? Error)> GetDataAsync()
```

### For Validation Methods

```csharp
// BAD: Assumes success when validation can't run
catch (Exception ex)
{
    _logger.LogWarning(ex, "Could not validate");
    return (true, true);  // Dangerous assumption!
}

// GOOD: Fail closed
catch (Exception ex)
{
    _logger.LogError(ex, "Validation failed");
    throw new ServiceUnavailableException("Validation service unavailable");
}
```

### For Stub Methods

```csharp
// BAD: Appears successful
return (StatusCodes.OK, null);

// GOOD: Clearly unimplemented
return (StatusCodes.NotImplemented, null);
// Or throw:
throw new NotImplementedException("Method not yet implemented");
```

---

## Files Requiring Changes

| File | Issue Count | Priority |
|------|-------------|----------|
| `lib-orchestrator/OrchestratorStateManager.cs` | 26+ | P2 |
| `lib-website/WebsiteService.cs` | 17 | P1 |
| `lib-behavior/BehaviorService.cs` | 6 | P1 |
| `lib-connect/BannouSessionManager.cs` | 4 | P2 |
| `lib-connect/ClientEvents/ClientEventQueueManager.cs` | 2 | P0 |
| `lib-mesh/Services/MeshRedisManager.cs` | 4 | P2 |
| `lib-character/CharacterService.cs` | 7 | P0/P2 |
| `lib-species/SpeciesService.cs` | 3 | P0 |
| `lib-auth/AuthService.cs` | 1 | P2 |
| `lib-auth/Services/SessionService.cs` | 1 | P2 |
| `lib-accounts/AccountsService.cs` | 1 | P2 |
| `lib-messaging/MessagingService.cs` | 1 | P1 |
| `lib-location/LocationService.cs` | 1 | P1 |
| `lib-voice/VoiceService.cs` | 1 | P1 |
| `lib-subscriptions/SubscriptionExpirationService.cs` | 1 | P2 |
| `bannou-service/` (various) | 5 | P3 |
