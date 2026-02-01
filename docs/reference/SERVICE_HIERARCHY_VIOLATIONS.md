# Service Hierarchy Violations Tracker

> **Created**: 2026-02-01
> **Purpose**: Track and remediate violations of the service dependency hierarchy defined in SERVICE_HIERARCHY.md

This document tracks known violations of the service hierarchy, their intended purpose, and suggested remediation patterns.

---

## Violation Categories

1. **Client Injection Violations**: Service A (lower layer) injects IServiceBClient where B is a higher layer
2. **Event Subscription Violations**: Service A subscribes to events defined by higher-layer Service B
3. **Suggested/Planned Violations**: Deep dive docs or issues suggesting patterns that would violate hierarchy

---

## Quick Reference: Layer Assignments

| Layer | Services |
|-------|----------|
| **L0** | lib-state, lib-messaging, lib-mesh |
| **L1** | telemetry, orchestrator, analytics |
| **L2a** | account, game-service, relationship-type, species, connect |
| **L2b** | auth, realm, relationship, permission |
| **L2c** | character, location, currency, contract |
| **L3** | character-personality, character-history, character-encounter, realm-history, subscription, game-session, escrow, inventory, item |
| **L4** | actor, behavior, mapping, scene, matchmaking, leaderboard, achievement, voice, save-load, asset, music, documentation, website |

---

## Confirmed Violations

### 1. Character → Actor/Encounter/Contract (CONFIRMED)

**Service**: lib-character (Layer 2c)
**Violating Dependencies**:
- `IActorClient` (Layer 4)
- `ICharacterEncounterClient` (Layer 3)
- `IContractClient` (Layer 2c - peer, questionable)

**Location**: `CharacterService.cs` constructor, `CheckCharacterReferencesAsync` method

**Original Intent**: Count references to a character for cleanup eligibility. Character needs to know if any actors, encounters, or contracts reference it before allowing deletion.

**Can this be inverted?**: Yes

**Suggested Fix**:
Character defines `character.reference.registered` and `character.reference.unregistered` events in its schema. Actor, Encounter, and Contract services publish to these topics when they create/delete references to characters. Character maintains reference counts by consuming its own events.

**Status**: Documented in CHARACTER.md, fix pending

---

### 2. Character → CharacterPersonality/CharacterHistory (CONFIRMED)

**Service**: lib-character (Layer 2c)
**Violating Dependencies**:
- `ICharacterPersonalityClient` (Layer 3)
- `ICharacterHistoryClient` (Layer 3)

**Location**: `CharacterService.cs` constructor, `GetEnrichedCharacterAsync` and `CompressCharacterAsync` methods

**Is this a clear violation?**: Yes. Layer 2c depending on Layer 3.

**Original Intent**:
1. **Enrichment**: When fetching a character, optionally include personality traits, combat preferences, and backstory from extension services.
2. **Compression**: When archiving a dead character, summarize personality/history data and optionally delete source data.

**Can this be inverted?**: Partially.

**Suggested Fix**:
- **For enrichment**: This is an aggregation pattern. Consider creating a dedicated "CharacterAggregator" in Layer 4 that fetches from Character + extensions. Clients call the aggregator, not Character directly for enriched data.
- **For compression**: Character publishes `character.compression.requested` event. CharacterPersonality and CharacterHistory subscribe, generate their summaries, and publish `character.compression.data-ready` with the summary text. Character collects responses and creates archive.

**Status**: Needs design discussion

---

### 3. Auth → Subscription (CONFIRMED)

**Service**: lib-auth (Layer 2b)
**Violating Dependencies**:
- `ISubscriptionClient` (Layer 3)

**Location**: `AuthService.cs` line 1210, `PropagateSubscriptionChangesAsync` method

**Is this a clear violation?**: Yes. Layer 2b depending on Layer 3.

**Original Intent**: When subscription state changes, Auth needs to update session authorization state. It queries Subscription service to get current subscriptions and updates all sessions for that account.

**Can this be inverted?**: Yes.

**Suggested Fix**:
The `subscription.updated` event (which Auth already subscribes to) should include ALL the authorization data Auth needs in the event payload itself - specifically the `stubName:authorized` formatted strings. Auth shouldn't need to call back to Subscription; the event should be self-contained.

Current flow: Auth receives `subscription.updated` → calls `ISubscriptionClient.QueryCurrentSubscriptionsAsync` → formats data
Fixed flow: Subscription publishes `subscription.updated` with formatted authorizations included → Auth just uses event data

**Status**: Needs fix

---

### 4. Species → Character (CONFIRMED)

**Service**: lib-species (Layer 2a)
**Violating Dependencies**:
- `ICharacterClient` (Layer 2c)

**Location**: `SpeciesService.cs` lines 547, 698, 1057, 1072

**Is this a clear violation?**: Yes. Layer 2a depending on Layer 2c (higher sub-layer).

**Original Intent**:
1. Check if any characters use a species before allowing deletion (reference counting)
2. During species merge, update all characters from old species to new species

**Can this be inverted?**: Yes, same pattern as Character reference counting.

**Suggested Fix**:
Species defines `species.reference.registered` and `species.reference.unregistered` events. Character publishes to these when creating/updating/deleting characters. Species maintains reference counts.

For species merge: Species publishes `species.merge.requested` event with old/new species IDs. Character subscribes and handles the migration of its own data.

**Status**: Needs fix

---

### 5. RelationshipType → Relationship (CONFIRMED)

**Service**: lib-relationship-type (Layer 2a)
**Violating Dependencies**:
- `IRelationshipClient` (Layer 2b)

**Location**: `RelationshipTypeService.cs` lines 624, 974, 994

**Is this a clear violation?**: Yes. Layer 2a depending on Layer 2b.

**Original Intent**:
1. Check if any relationships use a type before deletion (reference counting)
2. During type merge, update all relationships from old type to new type

**Can this be inverted?**: Yes, same pattern.

**Suggested Fix**:
RelationshipType defines `relationship-type.reference.registered/unregistered` events. Relationship publishes to these. For merge: RelationshipType publishes `relationship-type.merge.requested`, Relationship subscribes and migrates.

**Status**: Needs fix

---

### 6. GameSession → Voice (CONFIRMED)

**Service**: lib-game-session (Layer 3)
**Violating Dependencies**:
- `IVoiceClient` (Layer 4)

**Location**: `GameSessionService.cs` lines 301-306, 796-840, 1205-1239

**Is this a clear violation?**: Yes. Layer 3 depending on Layer 4.

**Original Intent**: Game sessions can optionally have voice chat rooms. When creating/ending sessions, voice rooms are created/destroyed.

**Can this be inverted?**: Yes.

**Suggested Fix**:
Voice subscribes to `game-session.created` and `game-session.ended` events. When a session is created with `VoiceEnabled=true`, Voice autonomously creates a room and publishes `voice.room.created` with the room ID. GameSession subscribes to capture the room ID for its state.

Alternative: Re-classify Voice as Layer 3 if it's tightly coupled to game sessions.

**Status**: Needs design discussion

---

### 7. Analytics → Multiple L2/L3 Services (DESIGN DISCUSSION)

**Service**: lib-analytics (Layer 1)
**Dependencies**:
- `IGameServiceClient` (Layer 2a)
- `IGameSessionClient` (Layer 3)
- `IRealmClient` (Layer 2b)
- `ICharacterClient` (Layer 2c)

**Location**: `AnalyticsService.cs` constructor, lines 1312, 1411, 1480

**Is this a clear violation?**: Nuanced. L1 is "observability" and should be optional.

**Original Intent**: Analytics receives events and needs to look up additional context (which game service a session belongs to, which realm a character is in) for proper categorization.

**Can this be inverted?**: Yes, via event enrichment.

**Suggested Fix**:
Events should be "fat" - include all context Analytics needs at publish time. Instead of:
```csharp
// Publisher sends minimal event
await _messageBus.PublishAsync("game-session.created", new { SessionId = id });
// Analytics has to call back to get GameServiceId, RealmId, etc.
```

Do:
```csharp
// Publisher includes all relevant context
await _messageBus.PublishAsync("game-session.created", new {
    SessionId = id,
    GameServiceId = gsId,
    RealmId = realmId,
    // etc.
});
// Analytics has everything it needs
```

This eliminates L1 dependencies on L2/L3 clients.

**Status**: Needs design discussion - significant change to event schemas

---

### 8. Connect → Auth (QUESTIONABLE)

**Service**: lib-connect (Layer 2a)
**Dependency**: `IAuthClient` (Layer 2b)

**Location**: `ConnectService.cs` line 481

**Is this a clear violation?**: Questionable.

**Original Intent**: When a WebSocket connection is established with a Bearer token, Connect calls Auth to validate the JWT and get session info.

**Analysis**:
Connect is the edge gateway. Auth handles authentication. When someone connects, their token must be validated. This seems like a fundamental flow where the gateway (Connect) needs authentication (Auth).

Options:
1. **Accept it**: Token validation is a fundamental cross-cutting concern
2. **Move Connect to L2b**: Peers with Auth
3. **Invert via shared infrastructure**: Token validation could be infrastructure (L0) rather than a service call

**Status**: Needs discussion - may be acceptable as-is

---

## Event Subscription Violations

### 9. Auth subscribes to `subscription.updated` (CONFIRMED)

**Service**: lib-auth (Layer 2b)
**Subscribes to**: `subscription.updated` from Subscription (Layer 3)

**Location**: `schemas/auth-events.yaml` line 434

**Is this a clear violation?**: Yes - same issue as #3 above.

**Original Intent**: When subscriptions change, Auth needs to update session authorizations.

**Suggested Fix**: Same as #3 - the event payload should include all data Auth needs. Auth shouldn't need to know about Subscription's API, just its event schema.

**Note**: Subscribing to an event is LESS of a violation than injecting a client, because the coupling is weaker (just the event schema, not the full API). But it still creates a dependency on the higher layer's event contract.

**Status**: Part of fix for #3

---

### 10. Analytics subscribes to L3 events (DESIGN DISCUSSION)

**Service**: lib-analytics (Layer 1)
**Subscribes to**:
- `game-session.action.performed` from GameSession (Layer 3)
- `game-session.created` from GameSession (Layer 3)
- `game-session.deleted` from GameSession (Layer 3)
- `character-history.*` events from CharacterHistory (Layer 3)

**Location**: `schemas/analytics-events.yaml`

**Is this a clear violation?**: This is the approved pattern for L1 - observability services SHOULD consume events from all layers. The issue is they shouldn't INJECT clients (issue #7).

**Status**: Event subscriptions are OK; client injections need fixing

---

---

## Potential Issues (Need Discussion)

### Layer 2 Sub-Layer Dependencies

Several L2a services depend on L2b services. This might indicate the sub-layer assignments need adjustment:

| Service | Layer | Depends On | Their Layer |
|---------|-------|------------|-------------|
| species | L2a | realm | L2b |
| connect | L2a | auth | L2b |

**Question**: Should L2a services be allowed to depend on L2b, or do we need to:
1. Re-classify some services
2. Apply the same strict "no upward dependency" rule within Layer 2

---

## Investigation Queue

---

## Deep Dive Suggestions That Would Violate Hierarchy

*To be populated after scanning deep dive documents*

---

## GitHub Issues Suggesting Violations

*To be populated after scanning open issues*

---

## Remediation Progress

| Violation | Service | Status | Issue/PR |
|-----------|---------|--------|----------|
| Reference counting | character | Documented | - |

