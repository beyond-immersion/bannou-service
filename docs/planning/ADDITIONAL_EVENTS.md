# Events Gap Analysis

This document tracks events that need to be added to complete the eventing coverage across services.

---

## lib-character-personality

| Event | Topic | Purpose | Priority |
|-------|-------|---------|----------|
| `CombatPreferencesDeletedEvent` | `combat-preferences.deleted` | Published when combat preferences are deleted (symmetry with other CRUD events) | P2 |

---

## lib-actor

| Event | Topic | Purpose | Priority | Status |
|-------|-------|---------|----------|--------|
| `ActorInstanceStartedEvent` | `actor.instance.started` | When actor actually begins running (distinct from created) | P2 | **Done** |
| `ActorInstancePausedEvent` | `actor.instance.paused` | When actor is paused | P2 | Needs Pause API |
| `ActorInstanceResumedEvent` | `actor.instance.resumed` | When actor resumes from pause | P2 | Needs Resume API |
| `ActorStatePersistedEvent` | `actor.instance.state-persisted` | When auto-save persists state | P3 | **Done** |
| `ActorEncounterStartedEvent` | `actor.encounter.started` | When encounter starts on Event Brain | P2 | **Done** |
| `ActorEncounterEndedEvent` | `actor.encounter.ended` | When encounter ends | P2 | **Done** |
| `ActorEncounterPhaseChangedEvent` | `actor.encounter.phase-changed` | When encounter phase changes | P3 | **Done** |

> **Note**: ActorInstancePausedEvent and ActorInstanceResumedEvent require adding Pause/Resume methods to IActorRunner interface first.

---

## lib-behavior

| Event | Topic | Purpose | Priority | Status |
|-------|-------|---------|----------|--------|
| `BehaviorCompilationFailedEvent` | `behavior.compilation-failed` | When ABML compilation fails (for monitoring/alerting) | P2 | **Done** |
| `BehaviorBundleCreatedEvent` | `behavior.bundle.created` | When a behavior bundle is created | P3 | Needs IMessageBus in BundleManager |
| `BehaviorBundleUpdatedEvent` | `behavior.bundle.updated` | When bundle metadata changes | P3 | Needs IMessageBus in BundleManager |
| `BehaviorBundleDeletedEvent` | `behavior.bundle.deleted` | When a behavior bundle is deleted | P3 | Needs IMessageBus in BundleManager |
| `GoapPlanGeneratedEvent` | `behavior.goap.plan-generated` | When GOAP planner generates new plan | P3 | **Done** |

> **Note**: BehaviorBundle events (created/updated/deleted) are defined via x-lifecycle but require injecting IMessageBus into BehaviorBundleManager to publish.

---

## lib-mapping

| Event | Topic | Purpose | Priority |
|-------|-------|---------|----------|
| `MappingAuthorityGrantedEvent` | `mapping.authority.granted` | When authority is granted to a channel | P2 |
| `MappingAuthorityReleasedEvent` | `mapping.authority.released` | When authority is explicitly released | P2 |
| `MappingAuthorityExpiredEvent` | `mapping.authority.expired` | When authority expires naturally (timeout) | P3 |

---

## Other Services (Pre-existing from original analysis)

| Service | Event Type | Purpose | Priority |
|---------|-----------|---------|----------|
| Permission | Matrix change events | When permission matrix changes | P3 |
| Orchestrator | Additional lifecycle events | Orchestrator operational events | P3 |
| Documentation | Bulk update/delete events | When bulk operations occur | P3 |
| Documentation | Archive restore events | When archived docs are restored | P3 |

---

## Implementation Status

- [x] CombatPreferencesDeletedEvent (lib-character-personality)
- [x] ActorInstanceStartedEvent (lib-actor)
- [ ] ~~ActorInstancePausedEvent~~ (lib-actor) - **Deferred: needs Pause API**
- [ ] ~~ActorInstanceResumedEvent~~ (lib-actor) - **Deferred: needs Resume API**
- [x] ActorStatePersistedEvent (lib-actor)
- [x] ActorEncounterStartedEvent (lib-actor)
- [x] ActorEncounterEndedEvent (lib-actor)
- [x] ActorEncounterPhaseChangedEvent (lib-actor)
- [x] BehaviorCompilationFailedEvent (lib-behavior)
- [ ] ~~BehaviorBundleCreatedEvent~~ (lib-behavior) - **Deferred: needs IMessageBus in BundleManager**
- [ ] ~~BehaviorBundleUpdatedEvent~~ (lib-behavior) - **Deferred: needs IMessageBus in BundleManager**
- [ ] ~~BehaviorBundleDeletedEvent~~ (lib-behavior) - **Deferred: needs IMessageBus in BundleManager**
- [x] GoapPlanGeneratedEvent (lib-behavior)
- [ ] MappingAuthorityGrantedEvent (lib-mapping)
- [ ] MappingAuthorityReleasedEvent (lib-mapping)
- [ ] MappingAuthorityExpiredEvent (lib-mapping)
