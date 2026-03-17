# Director Implementation Map

> **Plugin**: lib-director
> **Schema**: schemas/director-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/DIRECTOR.md](../plugins/DIRECTOR.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-director |
| Layer | L4 GameFeatures |
| Endpoints | 26 (24 generated + 2 cleanup callbacks pending schema) |
| State Stores | director-sessions (MySQL), director-events (MySQL), director-actor-taps (Redis), director-overrides (Redis), director-player-targets (Redis), director-lock (Redis) |
| Events Published | 9 (director.session.created/updated/deleted, director.event.created/updated/deleted, director.actor.tapped/driven/released) |
| Events Consumed | 3 (actor.instance.deleted, actor.instance.status-changed, session.disconnected) |
| Client Events | 3 planned (tap relay, approval request, event status — schema deferred to Phase 7) |
| Background Services | 2 (DirectorSessionTimeoutWorker, DirectorEventTimeoutWorker) |

---

## State

**Store**: `director-sessions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `dsess:{directorSessionId}` | `DirectorSessionModel` | Director session record (developer session, status, tap/drive counts, associated events) |
| `dsess-ws:{webSocketSessionId}` | `DirectorSessionModel` | Active session lookup by WebSocket session (one per connection) |

**Store**: `director-events` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `devt:{eventId}` | `DirectedEventModel` | Directed event record (name, game service scope, realm scope, status, actors, targets, broadcast config) |
| `devt-active:{gameServiceId}` | `DirectedEventModel` | Active events by game service (dashboard listing, capacity check) |

**Store**: `director-actor-taps` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tap:{sessionId}:{actorId}` | `ActorTapModel` | Active tap subscription (sampling rate, subscribed streams, created timestamp) |
| `tap-actor:{actorId}` | `Set<Guid>` | Reverse index: which sessions are tapping this actor (cleanup on actor deletion) |

**Store**: `director-overrides` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ovr:{actorId}:priority` | `Dictionary<string, float>` | GOAP action cost modifiers per action type |
| `ovr:{actorId}:gates` | `HashSet<string>` | Action types requiring developer approval before execution |
| `ovr:{actorId}:drive` | `DriveSessionModel` | Active drive session (developer sessionId, bound timestamp, last command timestamp) |

**Store**: `director-player-targets` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tgt:{eventId}:{targetId}` | `PlayerTargetModel` | Player targeting record (type, identifier, method, priority, status) |

**Store**: `director-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `director:lock:session:{webSocketSessionId}` | Director session create/end (one per connection) |
| `director:lock:event:{eventId}` | Directed event lifecycle transition |
| `director:lock:drive:{actorId}` | Actor drive binding (one driver per actor) |
| `director:lock:override:{actorId}` | Override modification |
| `director:lock:session-timeout-worker` | Worker cycle exclusion |
| `director:lock:event-timeout-worker` | Worker cycle exclusion |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Persistence for sessions (MySQL), events (MySQL), taps/overrides/targets (Redis) |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Distributed locks for session, event, drive, override mutations |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing director lifecycle and domain events |
| lib-messaging (`IMessageSubscriber`) | L0 | Hard | Dynamic subscriptions to `director.tap.{actorId}` topics |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span creation for all async methods |
| lib-connect (`IConnectClient`) | L1 | Hard | Session awareness, shortcut publishing, session-to-developer resolution |
| lib-permission (`IPermissionClient`) | L1 | Hard | Developer role verification |
| lib-actor (`IActorClient`) | L2 | Hard | Perception injection, actor state queries, actor listing |
| lib-character (`ICharacterClient`) | L2 | Hard | Character existence validation for event scoping |
| lib-realm (`IRealmClient`) | L2 | Hard | Realm existence validation for event scoping |
| lib-game-service (`IGameServiceClient`) | L2 | Hard | Game service existence validation for event scoping |
| lib-quest (`IQuestClient`) | L2 | Hard | Event-related quest creation for player targeting |
| lib-puppetmaster (`IPuppetmasterClient`) | L4 | Soft | Watcher lifecycle, behavior cache queries (degradation: actor-level ops still work) |
| lib-gardener (`IGardenerClient`) | L4 | Soft | POI injection, scenario boosting (degradation: player steering unavailable) |
| lib-broadcast (`IBroadcastClient`) | L3 | Soft | Platform session coordination, broadcast priority (degradation: no streaming) |
| lib-showtime (`IShowtimeClient`) | L4 | Soft | Audience priming, hype amplification (degradation: no metagame effects) |
| lib-divine (`IDivineClient`) | L4 | Soft | Deity state queries, blessing coordination (degradation: actor-level observation works) |
| lib-hearsay (`IHearsayClient`) | L4 | Soft | Rumor injection for organic player awareness (degradation: other targeting methods available) |
| lib-analytics (`IAnalyticsClient`) | L4 | Soft | Post-event metrics (degradation: event completion still works) |

**Note**: Director identifies developers by `webSocketSessionId`, not `accountId` — compliant with Foundation Tenets (Account Identity Boundary). No `cleanup-by-account` needed.

**DI Provider**: Director registers `DirectorOverrideProviderFactory` implementing `IVariableProviderFactory` (L4→L2 pull). Provides `${director.priority.<action_type>}` cost modifiers to Actor's GOAP planner. Returns null for all paths when no overrides active (zero overhead for non-directed actors).

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `director.session.created` | `DirectorSessionCreatedEvent` | StartSession |
| `director.session.updated` | `DirectorSessionUpdatedEvent` | Any session state change (taps, drives, status) |
| `director.session.deleted` | `DirectorSessionDeletedEvent` | EndSession, HandleSessionDisconnectedAsync timeout |
| `director.event.created` | `DirectorEventCreatedEvent` | CreateEvent |
| `director.event.updated` | `DirectorEventUpdatedEvent` | ActivateEvent, SetPhase, CompleteEvent, AddActor, AddTarget |
| `director.event.deleted` | `DirectorEventDeletedEvent` | CleanupByGameService resource callback |
| `director.actor.tapped` | `DirectorActorTappedEvent` | TapActor |
| `director.actor.driven` | `DirectorActorDrivenEvent` | DriveActor |
| `director.actor.released` | `DirectorActorReleasedEvent` | ReleaseActor, DirectorSessionTimeoutWorker auto-release |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `actor.instance.deleted` | `HandleActorDeletedAsync` | Clean up Redis-ephemeral taps, overrides, drive sessions for the actor. MySQL actor removal handled by x-references cleanup callback. |
| `actor.instance.status-changed` | `HandleActorStatusChangedAsync` | Update directed event actor status. Auto-release driven actor if errored; alert developer. |
| `session.disconnected` | `HandleSessionDisconnectedAsync` | Release all driven actors (fail-open), suspend taps. Session resumable on reconnect. |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<DirectorService>` | Structured logging |
| `DirectorServiceConfiguration` | Typed configuration access (17 properties) |
| `IStateStoreFactory` | State store access (creates 6 stores; not stored as field) |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `IMessageBus` | Event publishing |
| `IMessageSubscriber` | Dynamic tap topic subscriptions |
| `IEventConsumer` | Event consumer registration |
| `IActorClient` | Actor perception injection, state queries, listing |
| `ICharacterClient` | Character validation for event scoping |
| `IRealmClient` | Realm validation for event scoping |
| `IGameServiceClient` | Game service validation for event scoping |
| `IConnectClient` | Session awareness, shortcut publishing |
| `IPermissionClient` | Developer role verification |
| `IQuestClient` | Event-related quest creation |
| `IClientEventPublisher` | Push tap data and approval requests to developer WebSocket |
| `IServiceProvider` | Runtime resolution of soft L3/L4 dependencies |
| `ITelemetryProvider` | Span creation |
| `ITapRelayManager` | RabbitMQ tap subscription management (internal helper) |
| `IDriveSessionManager` | Actor cognition binding management (internal helper) |
| `ITargetingOrchestrator` | Multi-method player targeting coordination (internal helper) |

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `IVariableProviderFactory` (as `DirectorOverrideProviderFactory`) | Singleton | L4→L2 pull | Actor (L2) discovers via `IEnumerable<IVariableProviderFactory>` for `${director.*}` namespace. Provides `${director.priority.<actionType>}` cost modifiers AND `${director.gate.<actionType>}` prohibitive costs for gated actions. |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| StartSession | POST /director/session/start | generated | developer | session, session-ws-index | director.session.created |
| GetSession | POST /director/session/get | generated | developer | - | - |
| EndSession | POST /director/session/end | generated | developer | session, session-ws-index, taps, overrides | director.session.deleted, director.actor.released |
| TapActor | POST /director/actor/tap | generated | developer | tap, tap-actor-index, session | director.actor.tapped |
| UntapActor | POST /director/actor/untap | generated | developer | tap, tap-actor-index, session | - |
| GetActorState | POST /director/actor/get-state | generated | developer | - | - |
| ListActors | POST /director/actor/list | generated | developer | - | - |
| InjectPerception | POST /director/actor/inject-perception | generated | developer | - | - |
| SetOverrides | POST /director/actor/set-overrides | generated | developer | overrides-priority | - |
| ClearOverrides | POST /director/actor/clear-overrides | generated | developer | overrides-priority, overrides-gates | - |
| SetActionGates | POST /director/actor/set-action-gates | generated | developer | overrides-gates | - |
| DriveActor | POST /director/actor/drive | generated | developer | overrides-drive, session | director.actor.driven |
| ExecuteAction | POST /director/actor/execute-action | generated | developer | overrides-drive (lastCommandAt) | - |
| ReleaseActor | POST /director/actor/release | generated | developer | overrides-drive, session | director.actor.released |
| EvaluateExpression | POST /director/actor/evaluate-expression | generated | developer | - | - |
| CreateEvent | POST /director/event/create | generated | developer | event, event-active-index | director.event.created |
| GetEvent | POST /director/event/get | generated | developer | - | - |
| ListEvents | POST /director/event/list | generated | developer | - | - |
| AddActor | POST /director/event/add-actor | generated | developer | event | director.event.updated |
| AddTarget | POST /director/event/add-target | generated | developer | target | director.event.updated |
| ActivateEvent | POST /director/event/activate | generated | developer | event, targets | director.event.updated |
| SetPhase | POST /director/event/set-phase | generated | developer | event | director.event.updated |
| CompleteEvent | POST /director/event/complete | generated | developer | event, overrides, targets | director.event.updated, director.actor.released |
| CleanupByGameService | POST /director/cleanup-by-game-service | generated | [] | event, session, taps, overrides, targets | director.event.deleted, director.session.deleted |
| CleanupByActor | POST /director/cleanup-by-actor | generated | [] | event (actor removal) | director.event.updated |
| CleanupByRealm | POST /director/cleanup-by-realm | generated | [] | event (realmId nulled) | director.event.updated |

---

## Methods

### StartSession
POST /director/session/start | Roles: [developer]

```
LOCK director:lock:session:{body.WebSocketSessionId}           -> 409 if fails
  CALL IConnectClient.GetSessionAsync(body.WebSocketSessionId) -> 404 if not found
  READ _sessionStore:dsess-ws:{body.WebSocketSessionId}        -> 409 if exists (one per connection)
  // Check MaxConcurrentDirectorSessions capacity
  WRITE _sessionStore:dsess:{newSessionId} <- DirectorSessionModel { status: Active, createdAt: now }
  WRITE _sessionStore:dsess-ws:{body.WebSocketSessionId} <- DirectorSessionModel
  PUBLISH director.session.created { directorSessionId, webSocketSessionId }
RETURN (200, StartSessionResponse { directorSessionId })
```

### GetSession
POST /director/session/get | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null
RETURN (200, GetSessionResponse { session state })
```

### EndSession
POST /director/session/end | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null
IF session.Status == Ended                                     -> 200 (idempotent)
LOCK director:lock:session:{session.WebSocketSessionId}        -> 409 if fails
  // Release all driven actors
  FOREACH actorId in session active drives
    CALL IDriveSessionManager.ReleaseAsync(actorId)
    DELETE _overrideStore:ovr:{actorId}:drive
    PUBLISH director.actor.released { sessionId, actorId, reason: SessionEnded }
  // Close all taps
  CALL ITapRelayManager.RemoveAllAsync(sessionId)
  FOREACH tap in session taps
    DELETE _tapStore:tap:{sessionId}:{actorId}
    // Update tap-actor reverse index
  WRITE _sessionStore:dsess:{sessionId} <- status: Ended, endedAt: now
  DELETE _sessionStore:dsess-ws:{webSocketSessionId}
  PUBLISH director.session.deleted { directorSessionId }
RETURN (200, empty)
```

### TapActor
POST /director/actor/tap | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
// Check MaxTapsPerSession                                     -> 409 if at limit
READ _tapStore:tap:{sessionId}:{body.ActorId}                  -> 409 if already tapped
CALL IActorClient.InjectPerceptionAsync({ actorId, type: director_tap_start })
CALL ITapRelayManager.AddRelayAsync(sessionId, actorId, samplingRate, streams, webSocketSessionId)
WRITE _tapStore:tap:{sessionId}:{body.ActorId} <- ActorTapModel { samplingRate, streams, createdAt }
// Add sessionId to tap-actor:{actorId} reverse index
// Update session activeTapCount
CALL IActorClient.GetActorAsync(body.ActorId)                  -> initial snapshot
PUBLISH director.actor.tapped { sessionId, actorId, samplingRate }
RETURN (200, TapActorResponse { initialActorStateSnapshot })
```

### UntapActor
POST /director/actor/untap | Roles: [developer]

```
READ _tapStore:tap:{body.DirectorSessionId}:{body.ActorId}
IF null                                                        -> 200 (idempotent)
CALL IActorClient.InjectPerceptionAsync({ actorId, type: director_tap_stop })
CALL ITapRelayManager.RemoveRelayAsync(sessionId, actorId)
DELETE _tapStore:tap:{sessionId}:{actorId}
// Remove sessionId from tap-actor:{actorId} reverse index
// Update session activeTapCount
RETURN (200, empty)
```

### GetActorState
POST /director/actor/get-state | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null
CALL IActorClient.GetActorAsync(body.ActorId)                  -> 404 if not found
READ _tapStore:tap:{sessionId}:{body.ActorId}                  // isTapped flag
READ _overrideStore:ovr:{body.ActorId}:drive                   // isDriven flag
RETURN (200, GetActorStateResponse { actorState, isTapped, isDriven, drivingSessionId })
```

### ListActors
POST /director/actor/list | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null
CALL IActorClient.ListActorsAsync({ filters })
// Decorate each actor with director metadata (tapped/driven state)
RETURN (200, ListActorsResponse { actors with director metadata })
```

### InjectPerception
POST /director/actor/inject-perception | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
CALL IActorClient.InjectPerceptionAsync({ actorId, perceptionData })
// Record injection in directed event log if actor is in active event
RETURN (200, empty)
```

### SetOverrides
POST /director/actor/set-overrides | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
LOCK director:lock:override:{body.ActorId}                     -> 409 if fails
  WRITE _overrideStore:ovr:{body.ActorId}:priority <- Dictionary<string, float> from body
  // DirectorOverrideProviderFactory reads on next GOAP cycle
RETURN (200, empty)
```

### ClearOverrides
POST /director/actor/clear-overrides | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null
LOCK director:lock:override:{body.ActorId}                     -> 409 if fails
  DELETE _overrideStore:ovr:{body.ActorId}:priority
  DELETE _overrideStore:ovr:{body.ActorId}:gates
RETURN (200, empty)                                            // idempotent
```

### SetActionGates
POST /director/actor/set-action-gates | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
LOCK director:lock:override:{body.ActorId}                     -> 409 if fails
  WRITE _overrideStore:ovr:{body.ActorId}:gates <- HashSet<string> from body
  // DirectorOverrideProviderFactory reads gates and returns prohibitive cost
  // for gated action types via ${director.gate.<actionType>}
  // GOAP planner naturally avoids gated actions; no approval topic needed
  // Stale gates auto-cleared by DirectorSessionTimeoutWorker after ActionGateTimeoutSeconds
RETURN (200, empty)
```

### DriveActor
POST /director/actor/drive | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
// Check MaxDrivesPerSession                                   -> 409 if at limit
LOCK director:lock:drive:{body.ActorId}                        -> 409 if fails
  READ _overrideStore:ovr:{body.ActorId}:drive                 -> 409 if already driven
  CALL IDriveSessionManager.BindAsync(actorId, sessionId)      // pauses ABML, binds developer
  WRITE _overrideStore:ovr:{body.ActorId}:drive <- DriveSessionModel { sessionId, boundAt, lastCommandAt }
  // Update session activeDriveCount
  CALL IActorClient.GetActorAsync(body.ActorId)                -> current state + handlers
  PUBLISH director.actor.driven { sessionId, actorId }
RETURN (200, DriveActorResponse { actorState, availableActionHandlers })
```

### ExecuteAction
POST /director/actor/execute-action | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
READ _overrideStore:ovr:{body.ActorId}:drive                   -> 404 if no drive session
IF drive.DeveloperSessionId != body.DirectorSessionId          -> 403 (driven by different session)
CALL IDriveSessionManager.ExecuteActionAsync(actorId, actionYaml)
// Routes through same IActionHandler pipeline as ABML bytecode
WRITE _overrideStore:ovr:{body.ActorId}:drive <- updated lastCommandAt
RETURN (200, ExecuteActionResponse { result })
```

### ReleaseActor
POST /director/actor/release | Roles: [developer]

```
READ _overrideStore:ovr:{body.ActorId}:drive
IF null                                                        -> 200 (idempotent)
LOCK director:lock:drive:{body.ActorId}                        -> 409 if fails
  CALL IDriveSessionManager.ReleaseAsync(actorId)              // resumes ABML from checkpoint
  DELETE _overrideStore:ovr:{body.ActorId}:drive
  // Update session activeDriveCount
  PUBLISH director.actor.released { sessionId, actorId }
RETURN (200, empty)
```

### EvaluateExpression
POST /director/actor/evaluate-expression | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
CALL IDriveSessionManager.EvaluateExpressionAsync(actorId, expression)
// Evaluates ABML expression against actor's live variable providers
// Works in any tier (Observe, Steer, Drive) — read-only
RETURN (200, EvaluateExpressionResponse { resolvedValue })
```

### CreateEvent
POST /director/event/create | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
CALL IGameServiceClient.GetGameServiceAsync(body.GameServiceId) -> 404 if not found
IF body.RealmId != null
  CALL IRealmClient.GetRealmAsync(body.RealmId)                -> 404 if not found
// Check MaxConcurrentDirectedEvents for gameServiceId         -> 409 if at limit
WRITE _eventStore:devt:{newEventId} <- DirectedEventModel { status: Planned, createdAt: now }
WRITE _eventStore:devt-active:{body.GameServiceId}             // active events index
PUBLISH director.event.created { eventId, name, gameServiceId }
RETURN (200, CreateEventResponse { eventId })
```

### GetEvent
POST /director/event/get | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null
READ _eventStore:devt:{body.EventId}                           -> 404 if null
RETURN (200, GetEventResponse { full event state })
```

### ListEvents
POST /director/event/list | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null
// Query events by gameServiceId with optional status filter, paginated
RETURN (200, ListEventsResponse { events, totalCount, page })
```

### AddActor
POST /director/event/add-actor | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
LOCK director:lock:event:{body.EventId}                        -> 409 if fails
  READ _eventStore:devt:{body.EventId}                         -> 404 if null
  CALL IActorClient.GetActorAsync(body.ActorId)                -> 404 if not found
  // Append DirectedEventActor { actorId, role, controlTier } to event.actors
  WRITE _eventStore:devt:{body.EventId} <- updated model
  PUBLISH director.event.updated { eventId, changedFields: ["actors"] }
RETURN (200, empty)
```

### AddTarget
POST /director/event/add-target | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
LOCK director:lock:event:{body.EventId}                        -> 409 if fails
  READ _eventStore:devt:{body.EventId}                         -> 404 if null
  WRITE _targetStore:tgt:{body.EventId}:{targetId} <- PlayerTargetModel { status: Pending }
  PUBLISH director.event.updated { eventId, changedFields: ["playerTargets"] }
RETURN (200, empty)
// Targeting does not begin until ActivateEvent
```

### ActivateEvent
POST /director/event/activate | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
LOCK director:lock:event:{body.EventId}                        -> 409 if fails
  READ _eventStore:devt:{body.EventId}                         -> 404 if null
  IF event.Status != Planned                                   -> 400 (invalid transition)
  CALL ITargetingOrchestrator.BeginTargetingAsync(eventId, targets)
  // Dispatches: Gardener POIs, Hearsay rumors, Quest hooks, Connect shortcuts, etc.
  IF event.BroadcastPriority > 0
    CALL (soft) IBroadcastClient?.NotifyEventPriorityAsync(...)
  WRITE _eventStore:devt:{body.EventId} <- status: Active, startedAt: now
  PUBLISH director.event.updated { eventId, changedFields: ["status", "startedAt"] }
RETURN (200, empty)
```

### SetPhase
POST /director/event/set-phase | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
LOCK director:lock:event:{body.EventId}                        -> 409 if fails
  READ _eventStore:devt:{body.EventId}                         -> 404 if null
  // Validate phase transition: Active->Climax, Climax->WindDown, etc.
  IF invalid transition                                        -> 400
  CALL (soft) IBroadcastClient?.UpdatePriorityAsync(...)       // phase-based priority
  CALL (soft) IShowtimeClient?.AmplifyAsync(...)               // phase-based hype
  WRITE _eventStore:devt:{body.EventId} <- status: body.Phase
  PUBLISH director.event.updated { eventId, changedFields: ["status"] }
RETURN (200, empty)
```

### CompleteEvent
POST /director/event/complete | Roles: [developer]

```
READ _sessionStore:dsess:{body.DirectorSessionId}              -> 404 if null, 400 if inactive
LOCK director:lock:event:{body.EventId}                        -> 409 if fails
  READ _eventStore:devt:{body.EventId}                         -> 404 if null
  IF event.Status in (Completed, Cancelled)                    -> 409
  // Release all driven actors
  FOREACH actor in event.Actors where driven
    CALL IDriveSessionManager.ReleaseAsync(actorId)
    DELETE _overrideStore:ovr:{actorId}:drive
    PUBLISH director.actor.released { sessionId, actorId, reason: EventCompleted }
  // Remove all overrides for event actors
  FOREACH actor in event.Actors
    DELETE _overrideStore:ovr:{actorId}:priority
    DELETE _overrideStore:ovr:{actorId}:gates
  // Stop targeting
  CALL ITargetingOrchestrator.StopTargetingAsync(eventId)
  FOREACH target in event targets
    DELETE _targetStore:tgt:{eventId}:{targetId}
  CALL (soft) IBroadcastClient?.EndPrioritySessionAsync(...)
  CALL (soft) IShowtimeClient?.EndAmplificationAsync(...)
  WRITE _eventStore:devt:{body.EventId} <- status: Completed, completedAt: now
  PUBLISH director.event.updated { eventId, changedFields: ["status", "completedAt"] }
RETURN (200, empty)
```

### CleanupByGameService
POST /director/cleanup-by-game-service | Roles: [] (service-to-service, lib-resource CASCADE)

```
// Per-item try-catch per Foundation Tenets batch isolation
FOREACH event in events for body.GameServiceId
  try
    // Same logic as CompleteEvent: release drivers, remove overrides, stop targeting
    DELETE _eventStore:devt:{eventId}
    PUBLISH director.event.deleted { eventId, gameServiceId }
  catch -> LOG Warning, continue
DELETE _eventStore:devt-active:{body.GameServiceId}
// End affected director sessions
FOREACH session in affected sessions
  try
    // Same logic as EndSession: release drives, close taps
    WRITE _sessionStore:dsess:{sessionId} <- status: Ended
    DELETE _sessionStore:dsess-ws:{webSocketSessionId}
    PUBLISH director.session.deleted { sessionId }
  catch -> LOG Warning, continue
RETURN (200, empty)
```

### CleanupByActor
POST /director/cleanup-by-actor | Roles: [] (service-to-service, lib-resource DETACH)

```
// Remove actor from all active directed events (DETACH: event continues without actor)
FOREACH event containing body.ActorId in actors list
  try
    LOCK director:lock:event:{eventId}
      // Remove actor from event.Actors array
      WRITE _eventStore:devt:{eventId} <- updated model
      PUBLISH director.event.updated { eventId, changedFields: ["actors"] }
  catch -> LOG Warning, continue
RETURN (200, empty)
// Redis-ephemeral cleanup (taps, overrides, drives) handled by actor.instance.deleted event
```

### CleanupByRealm
POST /director/cleanup-by-realm | Roles: [] (service-to-service, lib-resource DETACH)

```
// Null realmId on events scoped to the deleted realm (DETACH: event continues)
FOREACH event with realmId == body.RealmId
  try
    LOCK director:lock:event:{eventId}
      // Set event.RealmId = null
      WRITE _eventStore:devt:{eventId} <- updated model
      PUBLISH director.event.updated { eventId, changedFields: ["realmId"] }
  catch -> LOG Warning, continue
RETURN (200, empty)
```

---

## Background Services

### DirectorSessionTimeoutWorker
**Interval**: `SessionTimeoutWorkerIntervalSeconds` (default: 30s)
**Purpose**: Detect idle/timed-out drive sessions and stale director sessions

```
LOCK director:lock:session-timeout-worker                      -> skip cycle if fails
// Scan for timed-out drive sessions
FOREACH drive session where lastCommandAt < (now - DriveIdleTimeoutMinutes)
    OR boundAt < (now - DriveSessionTimeoutMinutes)
  try
    CALL IDriveSessionManager.ReleaseAsync(actorId)
    DELETE _overrideStore:ovr:{actorId}:drive
    PUSH IClientEventPublisher -> notify developer of auto-release
    PUBLISH director.actor.released { sessionId, actorId, reason: Timeout }
  catch -> LOG Warning, continue
```

### DirectorEventTimeoutWorker
**Interval**: `EventTimeoutWorkerIntervalSeconds` (default: 60s)
**Purpose**: Auto-complete directed events exceeding EventDefaultTimeoutHours

```
LOCK director:lock:event-timeout-worker                        -> skip cycle if fails
FOREACH event with status in (Active, Climax, WindDown)
    AND startedAt < (now - EventDefaultTimeoutHours)
  try
    // Same logic as CompleteEvent: release drivers, remove overrides, stop targeting
    WRITE _eventStore:devt:{eventId} <- status: Completed, completedAt: now
    PUBLISH director.event.updated { eventId, changedFields: ["status", "completedAt"] }
  catch -> LOG Warning, continue
```

---

## Non-Standard Implementation Patterns

#### OnRunningAsync

```
// Register x-references cleanup callbacks via lib-resource
CALL RegisterResourceCleanupCallbacksAsync()
  // game-service (CASCADE) -> /director/cleanup-by-game-service
  // actor (DETACH) -> /director/cleanup-by-actor
  // realm (DETACH) -> /director/cleanup-by-realm
```

No other non-standard patterns. All endpoints are generated interface methods. No manual controllers, no custom overrides.
