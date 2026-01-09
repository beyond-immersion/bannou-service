# Events Gap Analysis

This document tracks events that need to be added to complete the eventing coverage across services.

---

## lib-character-personality

| Event | Topic | Purpose | Priority |
|-------|-------|---------|----------|
| `CombatPreferencesDeletedEvent` | `combat-preferences.deleted` | Published when combat preferences are deleted (symmetry with other CRUD events) | P2 |

---

## lib-actor

| Event | Topic | Purpose | Priority |
|-------|-------|---------|----------|
| `ActorInstanceStartedEvent` | `actor.instance.started` | When actor actually begins running (distinct from created) | P2 |
| `ActorInstancePausedEvent` | `actor.instance.paused` | When actor is paused | P2 |
| `ActorInstanceResumedEvent` | `actor.instance.resumed` | When actor resumes from pause | P2 |
| `ActorStatePersistedEvent` | `actor.instance.state-persisted` | When auto-save persists state | P3 |
| `ActorEncounterStartedEvent` | `actor.encounter.started` | When encounter starts on Event Brain | P2 |
| `ActorEncounterEndedEvent` | `actor.encounter.ended` | When encounter ends | P2 |
| `ActorEncounterPhaseChangedEvent` | `actor.encounter.phase-changed` | When encounter phase changes | P3 |

---

## lib-behavior

| Event | Topic | Purpose | Priority |
|-------|-------|---------|----------|
| `BehaviorCompilationFailedEvent` | `behavior.compilation-failed` | When ABML compilation fails (for monitoring/alerting) | P2 |
| `BehaviorBundleCreatedEvent` | `behavior.bundle.created` | When a behavior bundle is created | P3 |
| `BehaviorBundleUpdatedEvent` | `behavior.bundle.updated` | When bundle metadata changes | P3 |
| `BehaviorBundleDeletedEvent` | `behavior.bundle.deleted` | When a behavior bundle is deleted | P3 |
| `GoapPlanGeneratedEvent` | `behavior.goap.plan-generated` | When GOAP planner generates new plan | P3 |

> **Note**: BehaviorBundle events (created/updated/deleted) could be implemented as x-lifecycle events for consistency.

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
- [ ] ActorInstanceStartedEvent (lib-actor)
- [ ] ActorInstancePausedEvent (lib-actor)
- [ ] ActorInstanceResumedEvent (lib-actor)
- [ ] ActorStatePersistedEvent (lib-actor)
- [ ] ActorEncounterStartedEvent (lib-actor)
- [ ] ActorEncounterEndedEvent (lib-actor)
- [ ] ActorEncounterPhaseChangedEvent (lib-actor)
- [ ] BehaviorCompilationFailedEvent (lib-behavior)
- [ ] BehaviorBundleCreatedEvent (lib-behavior)
- [ ] BehaviorBundleUpdatedEvent (lib-behavior)
- [ ] BehaviorBundleDeletedEvent (lib-behavior)
- [ ] GoapPlanGeneratedEvent (lib-behavior)
- [ ] MappingAuthorityGrantedEvent (lib-mapping)
- [ ] MappingAuthorityReleasedEvent (lib-mapping)
- [ ] MappingAuthorityExpiredEvent (lib-mapping)
