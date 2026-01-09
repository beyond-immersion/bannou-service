# Actor Plugin V3 - Status Summary

> **Status**: PHASE 5 IN PROGRESS (Phases 0-4 COMPLETE)
> **Updated**: 2026-01-08

## Implementation Status

| Phase | Status | Description |
|-------|--------|-------------|
| **0. Bytecode Runtime Migration** | COMPLETE | Pre-requisite complete |
| **1. Core Infrastructure** | COMPLETE | ActorService, ActorRunner, ActorTemplate |
| **2. ActorRunner & State Transport** | COMPLETE | Tick loop, state persistence, messaging |
| **3. Pool Node Deployment** | COMPLETE | Container deployment, worker registration |
| **4. Orchestration Integration** | MOSTLY COMPLETE | ScalePoolAsync, preset support |
| **5. NPC Brain Integration** | IN PROGRESS | Bannou-side COMPLETE, Stride-side NEXT |

---

## Reference Documentation

All completed functionality is documented in guides:

- **Actor System**: [`docs/guides/ACTOR_SYSTEM.md`](../guides/ACTOR_SYSTEM.md) - Architecture, NPC brains, event actors, perception, cognition
- **ABML Language**: [`docs/guides/ABML.md`](../guides/ABML.md) - Behavior authoring
- **GOAP Planning**: [`docs/guides/GOAP.md`](../guides/GOAP.md) - Goal-oriented action planning
- **Behavior Plugin**: [`BEHAVIOR_PLUGIN_V2.md`](./ONGOING_-_BEHAVIOR_PLUGIN_V2.md) - Cognition handlers
- **Mapping System**: [`docs/guides/MAPPING_SYSTEM.md`](../guides/MAPPING_SYSTEM.md) - Spatial data for event actors

---

## Remaining Work: Phase 5 NPC Brain Integration

### Bannou-side (lib-actor): COMPLETE

Completed 2026-01-05:
- [x] Subscribe to `character.{characterId}.perceptions` when actor spawns
- [x] Unsubscribe when actor stops (cleanup in `StopAsync` and `DisposeAsync`)
- [x] Publish `CharacterStateUpdateEvent` via lib-mesh to game server
- [x] Define `CharacterPerceptionEvent` schema in `actor-events.yaml`
- [x] Track `_lastSourceAppId` from perception events for state update routing
- [x] All 6 cognition handlers registered in `DocumentExecutorFactory`
- [x] 124 unit tests passing for lib-actor

### Stride-side (game server): PENDING

This is the remaining work to enable NPC brain actors in production:

- [ ] **Publish perceptions**: Emit `CharacterPerceptionEvent` to `character.{characterId}.perceptions` fanout
  - When character sees something, takes damage, finds items, etc.
  - Fire-and-forget broadcast (no required subscribers)

- [ ] **Handle state updates**: Implement `character/state-update` endpoint for lib-mesh invocations
  - Receive `CharacterStateUpdateEvent` with feelings, goals, memories
  - Apply to character's behavior stack input slots

- [ ] **Apply to behavior inputs**: Make behavior stack read actor state
  - `if feelings.angry > 0.7 → choose aggressive behaviors`
  - `if goals.target exists → prioritize that target`
  - `if memories.betrayed_by[X] → react differently to X`

- [ ] **Lizard brain fallback**: Basic behavior when no state updates received
  - Characters function but don't evolve without actor
  - Graceful degradation if actor system unavailable

---

## Data Flow Architecture

```
┌────────────────────────────────────────────────────────────────┐
│                    STRIDE GAME SERVER                          │
│                                                                │
│  Character experiences something (sees enemy, finds item)      │
│                            │                                   │
│                            │ BROADCAST (fire and forget)       │
│                            │ Topic: character.{id}.perceptions │
│                            ▼                                   │
└────────────────────────────┼───────────────────────────────────┘
                             │
                             │ lib-messaging (RabbitMQ fanout)
                             │
┌────────────────────────────┼───────────────────────────────────┐
│                    NPC BRAIN ACTOR                             │
│                                                                │
│  Subscribed to perceptions → cognition pipeline → state update │
│                            │                                   │
│                            │ PUBLISH (direct)                  │
│                            │ Topic: character.{id}.state-updates│
│                            ▼                                   │
└────────────────────────────┼───────────────────────────────────┘
                             │
                             │ lib-mesh invocation
                             │
┌────────────────────────────┼───────────────────────────────────┐
│                    STRIDE GAME SERVER                          │
│                                                                │
│  Apply state updates to behavior stack inputs                  │
│  - feelings.angry = 0.8                                        │
│  - goals.target = entityX                                      │
│  - memories.betrayed_by = [X]                                  │
│                                                                │
│  BehaviorModelInterpreter reads these and adjusts behavior     │
└────────────────────────────────────────────────────────────────┘
```

---

## Code Locations

| Component | Location |
|-----------|----------|
| ActorService | `lib-actor/ActorService.cs` |
| ActorRunner | `lib-actor/ActorRunner.cs` |
| ActorPoolNodeWorker | `lib-actor/ActorPoolNodeWorker.cs` |
| Cognition Handlers | `lib-behavior/Handlers/` |
| Event Schemas | `schemas/actor-events.yaml` |
| Tests | `lib-actor.tests/` |

---

*Original ~30K-token document condensed 2026-01-08. Implementation details now in ACTOR_SYSTEM.md guide and codebase.*
