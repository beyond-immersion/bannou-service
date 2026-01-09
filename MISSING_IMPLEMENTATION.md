# Missing Implementations Audit

**Generated**: 2026-01-09
**Scope**: lib-character, lib-character-personality, lib-character-history, lib-realm-history, lib-behavior, lib-actor

This document catalogs ALL incomplete implementations, stubs, TODOs, and "future" claims found in the codebase.

---

## Critical: API Endpoints Returning NotImplemented

### lib-behavior/BehaviorService.cs

**Line 474-481: CompileBehaviorStackAsync**
```csharp
/// Compiles a stack of behaviors with priority resolution. Not yet implemented - planned for future release.
public async Task<(StatusCodes, CompileBehaviorResponse?)> CompileBehaviorStackAsync(...)
{
    _logger.LogWarning("Method CompileBehaviorStackAsync called but not implemented");
    return (StatusCodes.NotImplemented, null);
}
```
- **Status**: Returns 501 Not Implemented
- **Justification in code**: "planned for future release"
- **Impact**: API endpoint exists but does nothing

**Line 670-677: ResolveContextVariablesAsync**
```csharp
/// Resolves context variables and cultural adaptations. Not yet implemented - planned for future release.
public async Task<(StatusCodes, ResolveContextResponse?)> ResolveContextVariablesAsync(...)
{
    _logger.LogWarning("Method ResolveContextVariablesAsync called but not implemented");
    return (StatusCodes.NotImplemented, null);
}
```
- **Status**: Returns 501 Not Implemented
- **Justification in code**: "planned for future release"
- **Impact**: API endpoint exists but does nothing

---

## High: Features Documented But Not Implemented

### lib-behavior/Control/StateSync.cs

**Line 123-138: Blend Handoff Not Implemented**
```csharp
// For blend handoff, we'd need to interpolate state over time
// For now, treat all styles as instant sync
case HandoffStyle.Blend:
    // TODO: Implement blend interpolation over handoff.BlendDuration
    // For MVP, fall through to instant
    await SyncInstantAsync(entityId, finalCinematicState, ct);
    _logger.LogDebug(
        "Blend handoff not yet implemented, using instant for entity {EntityId}",
        entityId);
    break;
```
- **Status**: Falls through to instant sync
- **Justification in code**: "For MVP", "TODO"
- **Impact**: HandoffStyle.Blend does not actually blend

**Line 174-184: SyncInstantAsync Is Placeholder**
```csharp
// In a full implementation, this would:
// 1. Update the entity's position/rotation/etc in the world state
// 2. Update the behavior stack's knowledge of the entity's current state
// 3. Trigger any necessary re-evaluations

// For now, this is a placeholder that completes immediately
```
- **Status**: Does nothing except log
- **Justification in code**: "In a full implementation, this would"
- **Impact**: State sync is a no-op

### lib-behavior/BehaviorService.cs

**Line 693-699: Asset Deletion Not Supported**
```csharp
/// Currently not fully implemented - the asset service does not support asset deletion.
/// This endpoint will verify the asset exists but cannot remove it from storage.
/// Future implementation will support soft-delete or versioning to mark assets as invalid.
```
- **Status**: Partial implementation
- **Justification in code**: "asset service does not support asset deletion", "Future implementation"
- **Impact**: InvalidateCachedBehaviorAsync cannot actually delete

---

## Medium: Methods That Do Nothing / Return Immediately

### lib-actor/ActorServiceEvents.cs

**Line 146-164: HandleSessionDisconnectedAsync**
```csharp
public async Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
{
    await Task.CompletedTask;
    _logger.LogInformation(...);

    // Note: In the current implementation, actors are not directly tied to sessions.
    // This handler is here for future use cases where actors might be session-bound
    // (e.g., player-controlled actors that should stop when the player disconnects).

    // For NPC brain actors, they continue running even when players disconnect.
    // For session-bound actors (future), we would stop them here.
}
```
- **Status**: Logs message only
- **Justification in code**: "for future use cases", "we would stop them here"
- **Impact**: Session disconnect does not stop any actors

**Line 172-182: HandlePersonalityEvolvedAsync**
```csharp
public Task HandlePersonalityEvolvedAsync(PersonalityEvolvedEvent evt)
{
    _logger.LogInformation(...);
    _personalityCache.Invalidate(evt.CharacterId);
    return Task.CompletedTask;
}
```
- **Status**: Only invalidates cache (may be intentional, but returns Task.CompletedTask)

**Line 190-200: HandleCombatPreferencesEvolvedAsync**
```csharp
public Task HandleCombatPreferencesEvolvedAsync(CombatPreferencesEvolvedEvent evt)
{
    _logger.LogInformation(...);
    _personalityCache.Invalidate(evt.CharacterId);
    return Task.CompletedTask;
}
```
- **Status**: Only invalidates cache

### lib-actor/Runtime/ActorRunner.cs

**Line 511: ProcessPerceptionsAsync ends with Task.CompletedTask**
```csharp
await Task.CompletedTask;
```
- **Status**: Unnecessary await at end of method

**Line 983: HandlePerceptionEventAsync returns Task.CompletedTask**
```csharp
return Task.CompletedTask;
```
- **Status**: Method works but returns synchronously

### lib-actor/PoolNode/ActorPoolNodeWorker.cs

**Line 324: HandleMessageCommandAsync starts with Task.CompletedTask**
```csharp
await Task.CompletedTask;
```
- **Status**: Unnecessary await at start of method

### lib-behavior/Coordination/InputWindowManager.cs

**Line 113: OpenWindowAsync ends with Task.CompletedTask**
```csharp
await Task.CompletedTask;
return window;
```
- **Status**: Unnecessary await

**Line 173: SubmitAsync section with Task.CompletedTask**
- Similar pattern

### lib-behavior/Coordination/SyncPointManager.cs

**Line 141: Similar Task.CompletedTask pattern**

### lib-behavior/Coordination/CutsceneCoordinator.cs

**Line 97: Similar Task.CompletedTask pattern**

### lib-behavior/Coordination/CutsceneSession.cs

**Lines 236, 246, 256, 267: Multiple Task.CompletedTask returns**

---

## Medium: Features Returning Empty/No Data

### lib-character/CharacterService.cs

**Line 381-395: ListCharacters Without Realm Returns Empty**
```csharp
// Without realm filter, we need to scan all realms (less efficient)
// For now, return empty - in production you'd want a global index
_logger.LogWarning("ListCharacters called without realmId filter - returning empty for efficiency");

var response = new CharacterListResponse
{
    Characters = new List<CharacterResponse>(),
    TotalCount = 0,
    ...
};
```
- **Status**: Returns empty list
- **Justification in code**: "in production you'd want a global index"
- **Impact**: API returns no results without realm filter

---

## Low: MVP/Future Implementation Notes

### lib-behavior/Cognition/ActorLocalMemoryStore.cs

**Line 3, 15-16: MVP Implementation**
```csharp
// MVP implementation using lib-state for actor-local memory storage.
/// This MVP implementation uses keyword matching for relevance.
/// The interface allows future migration to a dedicated Memory service with embeddings.
```
- **Status**: Working but limited
- **Justification in code**: "MVP implementation", "future migration"
- **Impact**: Keyword matching instead of semantic embeddings

**Line 387:**
```csharp
/// This is an MVP implementation using keyword matching. Future versions may use
```

### lib-behavior/Cognition/IMemoryStore.cs

**Line 3, 11-12:**
```csharp
// Actor-local memory storage interface for MVP.
/// This MVP implementation uses actor-local storage via lib-state.
/// The interface allows future migration to a dedicated Memory service.
```

### lib-behavior/BehaviorServicePlugin.cs

**Line 54:**
```csharp
// Register memory store for cognition pipeline (actor-local MVP)
```

### lib-behavior/Handlers/AssessSignificanceHandler.cs

**Line 235:**
```csharp
// Simple keyword matching for MVP
```

---

## Remote Actor Support Not Implemented

### lib-actor/ActorService.cs

**Line 1555-1556:**
```csharp
// Remote actor invocation would go here in a future implementation
// For now, we only support local actors
```
- **Status**: Only local actors supported
- **Justification in code**: "future implementation", "For now"
- **Impact**: Distributed actor invocation does not work

---

## "For Now" / Temporary Implementations

### lib-behavior/Compiler/Actions/OutputCompilers.cs

**Line 177:**
```csharp
// For now, we treat known domain actions as output signals
```

### lib-behavior/Compiler/Actions/ControlFlowCompilers.cs

**Line 200:**
```csharp
// For now, we treat it as a jump since our simple VM doesn't have a call stack
```

### lib-behavior/Intent/BehaviorOutput.cs

**Line 203:**
```csharp
// For now, we interpret as entity ID lookup (game provides actual mapping)
```

### lib-actor/Runtime/ActorRunnerFactory.cs

**Line 106:**
```csharp
// For now, just return the template - override logic would parse the object
```

---

## Reserved/Unused Fields

### lib-behavior/Runtime/BehaviorModelHeader.cs

**Line 49:**
```csharp
/// Reserved for future use.
```

---

## Interface Notes About Future

### lib-behavior/Runtime/IBehaviorModelInterpreterFactory.cs

**Line 10:**
```csharp
/// Enables DI injection and potentially pooling in the future.
```

---

## Operators Not Supported

### lib-behavior/Compiler/Expressions/StackExpressionCompiler.cs

**Line 200-201:**
```csharp
BinaryOperator.In => throw new NotSupportedException("'in' operator not supported in behavior bytecode"),
_ => throw new NotSupportedException($"Unknown binary operator: {node.Operator}")
```
- **Status**: Will throw at runtime
- **Impact**: 'in' operator in ABML expressions will fail

---

## Summary

| Category | Count | Priority |
|----------|-------|----------|
| API Endpoints Returning 501 | 2 | CRITICAL |
| Features Documented But Not Implemented | 3 | HIGH |
| Methods That Do Nothing | 10+ | MEDIUM |
| Features Returning Empty Data | 1 | MEDIUM |
| MVP/Limited Implementations | 5+ | LOW |
| Remote Actor Support Missing | 1 | MEDIUM |
| "For Now" Temporary Code | 5+ | LOW |
| Unsupported Operators | 2 | LOW |

---

## Action Required

1. **CRITICAL**: Decide if CompileBehaviorStackAsync and ResolveContextVariablesAsync should be implemented or removed from API
2. **HIGH**: Implement blend handoff or document that it won't be supported
3. **HIGH**: Implement SyncInstantAsync properly or document limitations
4. **MEDIUM**: Implement session-bound actor cleanup or remove the handler
5. **MEDIUM**: Implement global character index or document the limitation
6. **MEDIUM**: Plan for remote actor support or document single-node limitation
