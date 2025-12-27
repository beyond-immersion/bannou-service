# TopicAttribute and Dead Event Controller Cleanup Plan

## Executive Summary

After the Dapr removal, the `[Topic]` attribute was preserved as a stub to maintain compilation of legacy HTTP event controllers. However, **events no longer arrive via HTTP** - they now flow directly through RabbitMQ via MassTransit. This creates a significant dead code problem and a critical bug where some events are never handled.

### Key Finding: Critical Bug

**`session.invalidated` events are published but never received:**
1. Auth service publishes to `session.invalidated` when sessions are invalidated
2. `ConnectEventsController` has handler code - but nothing calls it (dead HTTP endpoint)
3. Connect doesn't register any handler with `IEventConsumer`
4. `session.invalidated` is NOT registered in `EventSubscriptionRegistry`
5. Result: Session invalidation events are lost - WebSocket connections aren't closed

Same issue exists with `service.error` topic (Orchestrator has dead handler).

## Current Architecture

### Two Event Flow Paths (One Dead)

```
ACTIVE PATH (IEventConsumer + NativeEventConsumerBackend):
┌─────────────────┐     ┌──────────────────────┐     ┌─────────────────────┐
│  *ServiceEvents │────►│ EventConsumerBackend │────►│ MassTransit/RabbitMQ│
│  RegisterHandler│     │   (IHostedService)   │     │ (Direct Subscribe)  │
└─────────────────┘     └──────────────────────┘     └─────────────────────┘

DEAD PATH (HTTP Controllers with [Topic]):
┌─────────────────┐     ┌──────────────────────┐     ┌─────────────────────┐
│ *EventsController│────►│   [Topic] Attribute  │────►│  NOTHING CALLS THIS │
│   [HttpPost]     │     │   (Dapr Placeholder) │     │  (Dead Code)        │
└─────────────────┘     └──────────────────────┘     └─────────────────────┘
```

### Files to Delete

**Dead EventsController Files (Generated):**
- `lib-auth/Generated/AuthEventsController.cs`
- `lib-permissions/Generated/PermissionsEventsController.cs`
- `lib-game-session/Generated/GameSessionEventsController.cs`
- `lib-documentation/Generated/DocumentationEventsController.cs` (if exists)

**Dead EventsController Files (Manual):**
- `lib-connect/ConnectEventsController.cs` (has business logic to preserve!)
- `lib-orchestrator/OrchestratorEventsController.cs`
- `lib-orchestrator/Events/ServiceErrorEventsController.cs`
- `bannou-service/Controllers/ServiceMappingEventsController.cs` (has debug endpoints!)

### Files to Preserve (Partial Cleanup)

**BannouEventHelper.cs:**
- `ReadEventAsync<T>(HttpRequest)` - ONLY used by dead controllers - REMOVE
- `UnwrapCloudEventsEnvelope(byte[])` - Used by ConnectService - KEEP

**TopicAttribute.cs:**
- `bannou-service/Attributes/TopicAttribute.cs` - No longer needed - DELETE

### Code Generation Script

**`scripts/generate-event-subscriptions.sh`:**
- Currently generates BOTH EventsController.cs AND ServiceEvents.cs
- Should ONLY generate ServiceEvents.cs (handler registrations)
- Stop generating the useless EventsController files

## Implementation Plan

### Phase 1: Fix Critical Bugs (HIGH PRIORITY)

These events are published but never handled:

#### 1.1 Fix `session.invalidated` Handling

**Problem:** Connect needs to receive session.invalidated events to disconnect WebSocket clients.

**Solution:**
1. Create `schemas/connect-events.yaml` with subscription config:
   ```yaml
   info:
     x-event-subscriptions:
       - topic: session.invalidated
         event: SessionInvalidatedEvent
         handler: HandleSessionInvalidated
   ```
2. Add to `EventSubscriptionRegistry` (via generate-event-subscription-registry.sh)
3. Create `lib-connect/ConnectServiceEvents.cs`:
   ```csharp
   public partial class ConnectService
   {
       protected void RegisterEventConsumers(IEventConsumer eventConsumer)
       {
           eventConsumer.RegisterHandler<IConnectService, SessionInvalidatedEvent>(
               "session.invalidated",
               async (svc, evt) => await ((ConnectService)svc).HandleSessionInvalidatedAsync(evt));
       }

       public async Task HandleSessionInvalidatedAsync(SessionInvalidatedEvent evt)
       {
           // Logic from ConnectEventsController.HandleSessionInvalidatedAsync
           // ...
       }
   }
   ```
4. Call `RegisterEventConsumers(eventConsumer)` from ConnectService constructor
5. Delete `lib-connect/ConnectEventsController.cs`

#### 1.2 Fix `service.error` Handling

**Problem:** ServiceErrorEventsController has dead handler code.

**Solution:**
1. Evaluate if service.error handling is actually needed
2. If yes: Create `OrchestratorServiceEvents.cs` with proper handler registration
3. If no: Just delete `ServiceErrorEventsController.cs`

### Phase 2: Remove Dead Generated Files

#### 2.1 Delete Generated EventsControllers

```bash
# These are generated and never called
rm lib-auth/Generated/AuthEventsController.cs
rm lib-permissions/Generated/PermissionsEventsController.cs
rm lib-game-session/Generated/GameSessionEventsController.cs
# Check for others in lib-*/Generated/*EventsController.cs
```

#### 2.2 Modify Code Generation Script

Update `scripts/generate-event-subscriptions.sh`:
- Remove all EventsController generation code (lines 173-284)
- Keep only ServiceEvents.cs generation (lines 286-374)
- Rename script to `generate-service-events.sh` for clarity

#### 2.3 Update generate-all-services.sh

Remove calls that generate EventsControllers, keep ServiceEvents generation.

### Phase 3: Remove Dead Manual Files

#### 3.1 Handle ServiceMappingEventsController

**Note:** Has useful debug endpoints (`/mappings`, `/health`) - preserve these!

**Solution:**
1. Create `bannou-service/Controllers/ServiceMappingDebugController.cs`
   - Move `/mappings` and `/health` endpoints there
2. Delete `bannou-service/Controllers/ServiceMappingEventsController.cs`
3. Delete dead `HandleFullServiceMappingsAsync` method

#### 3.2 Handle OrchestratorEventsController

1. Evaluate if `FullServiceMappingsEvent` handling is needed
2. If yes: Move to `OrchestratorServiceEvents.cs`
3. Delete `lib-orchestrator/OrchestratorEventsController.cs`

### Phase 4: Remove Infrastructure

#### 4.1 Delete TopicAttribute

```bash
rm bannou-service/Attributes/TopicAttribute.cs
```

#### 4.2 Clean BannouEventHelper

Remove `ReadEventAsync<T>(HttpRequest request)` method - only used by dead controllers.
Keep `UnwrapCloudEventsEnvelope(byte[] data)` - used by ConnectService.

### Phase 5: Verification

1. `dotnet build` - Ensure no compilation errors
2. `make test` - Unit tests pass
3. `make test-http` - HTTP integration tests pass
4. `make test-edge` - WebSocket edge tests pass (especially session invalidation!)
5. Grep for any remaining `[Topic]` usages
6. Grep for any remaining `BannouEventHelper.ReadEventAsync` calls

## File Inventory

### Files to DELETE (Dead Code)

| File | Type | Reason |
|------|------|--------|
| `bannou-service/Attributes/TopicAttribute.cs` | Attribute | Stub with no runtime effect |
| `lib-auth/Generated/AuthEventsController.cs` | Generated Controller | Dead HTTP path |
| `lib-permissions/Generated/PermissionsEventsController.cs` | Generated Controller | Dead HTTP path |
| `lib-game-session/Generated/GameSessionEventsController.cs` | Generated Controller | Dead HTTP path |
| `lib-connect/ConnectEventsController.cs` | Manual Controller | Dead HTTP path (move logic first!) |
| `lib-orchestrator/OrchestratorEventsController.cs` | Manual Controller | Dead HTTP path |
| `lib-orchestrator/Events/ServiceErrorEventsController.cs` | Manual Controller | Dead HTTP path |
| `bannou-service/Controllers/ServiceMappingEventsController.cs` | Manual Controller | Dead HTTP path (preserve debug endpoints!) |

### Files to CREATE

| File | Purpose |
|------|---------|
| `schemas/connect-events.yaml` | Define Connect's event subscriptions |
| `lib-connect/ConnectServiceEvents.cs` | Handler registrations for Connect |
| `bannou-service/Controllers/ServiceMappingDebugController.cs` | Preserve debug endpoints |

### Files to MODIFY

| File | Change |
|------|--------|
| `scripts/generate-event-subscriptions.sh` | Remove EventsController generation |
| `bannou-service/BannouEventHelper.cs` | Remove ReadEventAsync method |
| `lib-connect/ConnectService.cs` | Call RegisterEventConsumers in constructor |

## Risks and Mitigations

### Risk: Breaking Existing Functionality

**Mitigation:**
- Phase 1 FIRST - ensures session.invalidated works before removing other code
- Comprehensive testing at each phase
- Edge tests specifically test session invalidation flow

### Risk: Missing Other Event Subscriptions

**Mitigation:**
- Audit all `x-event-subscriptions` in schema files
- Verify each has corresponding `*ServiceEvents.cs` with `RegisterHandler` calls
- Check `EventSubscriptionRegistry.Generated.cs` covers all topics

### Risk: Debug Endpoints Lost

**Mitigation:**
- ServiceMappingEventsController debug endpoints explicitly preserved
- Create new debug controller before deleting old one

## Estimated Effort

| Phase | Effort | Priority |
|-------|--------|----------|
| Phase 1: Fix Bugs | 2-3 hours | CRITICAL |
| Phase 2: Generated Files | 30 minutes | High |
| Phase 3: Manual Files | 1 hour | High |
| Phase 4: Infrastructure | 30 minutes | Medium |
| Phase 5: Verification | 1 hour | Required |

**Total: ~5-6 hours**

## Success Criteria

1. No `[Topic]` attribute usage anywhere in codebase
2. No `*EventsController.cs` files (generated or manual)
3. All event subscriptions work via `IEventConsumer` + `*ServiceEvents.cs`
4. `session.invalidated` events properly disconnect WebSocket clients
5. All tests pass (unit, HTTP, WebSocket edge)
6. Debug endpoints preserved and functional
