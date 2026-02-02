# Service Hierarchy Violations Tracker

> **Created**: 2026-02-01
> **Updated**: 2026-02-02 (comprehensive audit with new 5-layer model)
> **Purpose**: Track and remediate violations of the service dependency hierarchy

This document tracks known violations of the service hierarchy, their severity under the 5-layer model, and suggested remediation patterns.

---

## Quick Reference: Layer Assignments (v2.0)

| Layer | Name | Services |
|-------|------|----------|
| **L0** | Infrastructure | lib-state, lib-messaging, lib-mesh |
| **L1** | App Foundation | account, auth, connect, permission, contract |
| **L2** | Game Foundation | game-service, realm, character, species, location, relationship-type, relationship, subscription, currency, item, inventory |
| **L3** | App Features | asset, telemetry, orchestrator, documentation, website, testing, mesh, messaging, state |
| **L4** | Game Features | actor, analytics*, behavior, mapping, scene, matchmaking, leaderboard, achievement, voice, save-load, music, game-session, escrow, character-personality, character-history, character-encounter, realm-history |

\* Analytics is L4 for event consumption only - observes but doesn't invoke game services. Nothing should depend on Analytics.

**Not in hierarchy**: lib-common (shared types only), lib-*.tests (test projects)

---

## Violation Categories

1. **Foundation → Feature**: L1/L2 service depends on L3/L4 service (CRITICAL)
2. **App Feature → Game**: L3 service depends on L2/L4 service (CRITICAL - domain violation)
3. **Feature → Feature (no graceful handling)**: L3/L4 depends on L3/L4 without null-checking (MODERATE)
4. **Event Subscription Violations**: Lower layer subscribes to higher layer events (CRITICAL)

---

## Confirmed Client Injection Violations

### 1. Character → Actor/Encounter (CRITICAL)

**Service**: lib-character (L2 Game Foundation)
**Violating Dependencies**:
- `IActorClient` (L4)
- `ICharacterEncounterClient` (L4)

**Location**: `CharacterService.cs` constructor, `CheckCharacterReferencesAsync` method

**Violation Type**: L2 → L4 (Foundation depending on Feature)

**Original Intent**: Count references to a character for cleanup eligibility.

**Suggested Fix**: Character defines `character.reference.registered/unregistered` events. Actor and Encounter publish to these topics. Character maintains reference counts by consuming its own events.

**Status**: Documented in #259, fix pending

---

### 2. Character → CharacterPersonality/CharacterHistory (CRITICAL)

**Service**: lib-character (L2 Game Foundation)
**Violating Dependencies**:
- `ICharacterPersonalityClient` (L4)
- `ICharacterHistoryClient` (L4)

**Location**: `CharacterService.cs` constructor, `GetEnrichedCharacterAsync` and `CompressCharacterAsync` methods

**Violation Type**: L2 → L4 (Foundation depending on Feature)

**Original Intent**:
1. **Enrichment**: Optionally include personality/history when fetching character
2. **Compression**: Summarize data when archiving dead character

**Suggested Fix**:
- **Enrichment**: Create "CharacterAggregator" in L4 that fetches from Character + extensions. Clients call aggregator for enriched data.
- **Compression**: Character publishes `character.compression.requested`. Extensions subscribe, generate summaries, publish `character.compression.data-ready`.

**Status**: Documented in #259, needs design discussion

---

### 3. Auth → Subscription (CRITICAL - DELETE)

**Service**: lib-auth (L1 App Foundation)
**Violating Dependencies**:
- `ISubscriptionClient` (L2)

**Location**:
- `AuthService.cs` constructor - `ISubscriptionClient` injection
- `AuthService.cs` `PropagateSubscriptionChangesAsync` method
- `AuthServiceEvents.cs` `HandleSubscriptionUpdatedAsync` method
- `Services/TokenService.cs` constructor - `ISubscriptionClient` injection
- `schemas/auth-events.yaml` subscription to `subscription.updated`

**Violation Type**: L1 → L2 (App Foundation depending on Game Foundation) - **ARCHITECTURAL ERROR**

**Why This Is Especially Wrong**:
Auth (L1) is app-level infrastructure that should work for ANY deployment, including non-game deployments. Subscription (L2) is game-specific. Auth has NO business with subscriptions.

**Suggested Fix**: **DELETE** all subscription-related code from Auth:
- Remove `ISubscriptionClient` from both `AuthService.cs` and `TokenService.cs`
- Delete `PropagateSubscriptionChangesAsync` method
- Delete `HandleSubscriptionUpdatedAsync` event handler
- Remove `subscription.updated` from `auth-events.yaml`

**Status**: **P0 - Needs immediate deletion**

---

### 4. Analytics → Multiple L2/L4 Services (RESOLVED - RECLASSIFIED)

**Service**: lib-analytics (now L4 Game Features)
**Former Violating Dependencies**:
- `IGameServiceClient` (L2)
- `IGameSessionClient` (L4)
- `IRealmClient` (L2)
- `ICharacterClient` (L2)

**Resolution**: Analytics reclassified from L3 to L4.

**Rationale**: Analytics is L4 not because it *depends* on game services in the traditional sense, but because it *observes* them via event subscriptions. Key characteristics:
- Analytics consumes events from L2/L4 (game-session, character-history, realm-history)
- Analytics does NOT invoke L2/L4 service APIs for its core function (only event consumption)
- If any "dependency" is disabled, Analytics gracefully continues (events just stop arriving)
- Analytics should be the most optional plugin - nothing in L1/L2/L3 should call it
- Only L4 services like Matchmaking have legitimate reasons to call Analytics APIs

**Status**: ✅ RESOLVED by reclassification to L4

---

### 5. Website → Game Services (DESIGN VIOLATION - STUB HIDES IT)

**Service**: lib-website (L3 App Features)
**Current Implementation**: Complete stub - all methods return `NotImplemented`

**Violation Type**: L3 → L2 (App Feature depending on Game Foundation) - **DESIGN VIOLATION**

**Why It's Hidden**: The service is currently a stub with no client injections. Every method just logs and returns `NotImplemented`. The violation will manifest when someone implements these endpoints.

**Schema-Defined Endpoints That Require L2 Dependencies**:
- `GetAccountCharactersAsync` → needs `ICharacterClient` (L2)
- `GetSubscriptionAsync` → needs `ISubscriptionClient` (L2)
- `GetAccountSubscriptionAsync` → needs `ISubscriptionClient` (L2)

**The Problem**: The API schema (`website-api.yaml`) was designed without considering hierarchy constraints. When implemented, Website (L3) would need to call Character and Subscription (L2), which violates the domain boundary.

**Options**:
1. **Remove game endpoints from Website schema**: Website only handles CMS pages, news, downloads, contact forms - no game data
2. **Create game-portal (L4)**: Move character/subscription endpoints to a dedicated L4 service
3. **Redesign as aggregation via events**: Website subscribes to events for cached views (complex)

**Suggested Fix**: Option 1 or 2 - Remove these endpoints from Website schema, or create a separate "game-portal" L4 service for game-specific web views.

**Status**: P1 - Schema needs redesign before implementation

---

### 6. GameSession → Voice (MODERATE - DESIGN ISSUE)

**Service**: lib-game-session (L4 Game Features)
**Dependency**: `IVoiceClient` (L4)

**Location**: `GameSessionService.cs` lines 301-306, 796-840, 1205-1239

**Violation Type**: L4 → L4 (technically allowed with graceful degradation)

**Issue**: While L4 → L4 is technically allowed, this dependency should be inverted. GameSession shouldn't know about Voice - Voice should react to GameSession events.

**Original Intent**: Create/destroy voice rooms when sessions start/end.

**Suggested Fix**: Voice subscribes to `game-session.created` and `game-session.ended` events. When session has `VoiceEnabled=true`, Voice creates room and publishes `voice.room.created`. GameSession optionally subscribes to capture room ID.

**Status**: P3 - Design improvement

---

## Confirmed Event Subscription Violations

### 7. Auth subscribes to subscription.updated (CRITICAL)

**Subscriber**: lib-auth (L1 App Foundation)
**Topic**: `subscription.updated`
**Publisher**: lib-subscription (L2 Game Foundation)

**Location**: `schemas/auth-events.yaml`

**Violation Type**: L1 subscribing to L2 event

**Why This Is Wrong**: Same as #3 above - Auth has no business with subscriptions.

**Status**: Part of #3 deletion - **P0**

---

### 8-10. Analytics event subscriptions (RESOLVED - RECLASSIFIED)

**Former Violations**: Analytics (when L3) subscribing to L4 events:
- `game-session.action.performed`, `game-session.created`, `game-session.deleted`
- `character-history.participation.recorded`, `character-history.backstory.*`
- `realm-history.participation.recorded`, `realm-history.lore.*`

**Resolution**: Analytics reclassified to L4. As an L4 service, it can freely subscribe to L2/L4 events.

**Status**: ✅ RESOLVED by reclassification (part of #4)

---

## Resolved in v2.0 (No Longer Violations)

| Old Concern | Old Status | New Status | Reason |
|-------------|------------|------------|--------|
| Species → Character | L2a → L2c | **OK** | Both L2, same-layer allowed |
| RelationshipType → Relationship | L2a → L2b | **OK** | Both L2, same-layer allowed |
| Connect → Auth | L2a → L2b | **OK** | Both L1, same-layer allowed |
| Character → Contract | Peer dependency | **OK** | L2 → L1, correct direction |
| Subscription → GameService | - | **OK** | Both L2, same-layer allowed |
| Inventory → Item | - | **OK** | Both L2, same-layer allowed |
| Escrow → Currency/Item | - | **OK** | L4 → L2, correct direction |
| Actor → Character | - | **OK** | L4 → L2, correct direction |
| Matchmaking → GameSession | - | **OK** | L4 → L4, with graceful degradation |
| Analytics → L2/L4 | L3 → L2/L4 | **OK** | Reclassified to L4 (event-consumption-only) |
| Analytics event subs | L3 → L4 events | **OK** | Reclassified to L4, can subscribe to any events |

**Note**: Website is NOT resolved - the stub implementation hides a design-level violation. See #5.

---

## Event Subscription Analysis

### Acceptable Patterns

**L4 subscribing to L1/L2/L3 events**: Correct direction
- Achievement subscribes to `analytics.score.updated` (L4 → L3) ✓
- Leaderboard subscribes to `analytics.rating.updated` (L4 → L3) ✓
- Actor subscribes to `character.deleted` (L4 → L2) ✓
- Actor subscribes to `session.disconnected` (L4 → L1) ✓
- GameSession subscribes to `subscription.updated` (L4 → L2) ✓
- CharacterEncounter subscribes to `character.deleted` (L4 → L2) ✓
- Escrow subscribes to `contract.fulfilled/terminated` (L4 → L1) ✓

**L3 subscribing to L1 events**: Correct direction
- (none currently, but would be acceptable)

**L2 subscribing to L1/L2 events**: Correct direction
- (most L2 services don't subscribe to external events)

### Violation Patterns

**L1 subscribing to L2 events**: Wrong direction
- ❌ Auth subscribes to `subscription.updated` (L1 → L2) - **DELETE**

### Resolved Event Subscriptions

**Analytics (now L4) subscribing to L2/L4 events**: Now OK
- ✅ Analytics subscribes to `game-session.*` (L4 → L4)
- ✅ Analytics subscribes to `character-history.*` (L4 → L4)
- ✅ Analytics subscribes to `realm-history.*` (L4 → L4)

---

## Related GitHub Issues

### Issue #259: Resource Lifecycle Management (Root Cause)

The fundamental architecture problem: foundational services need to check consumer references for safe deletion, which drives them to take reverse dependencies.

**Proposed Solutions**:
- Pattern A: Resource Reference Registry (L0 infrastructure)
- Pattern E: x-references Schema Extension
- Pattern G: Register/Unregister Events with Prebound Cleanup

**Recommended**: Hybrid of A+E+G

### Issue #152: Missing Deletion Event Cascades

**Missing event consumers**:
- `character.deleted` → character-history (not consumed)
- `character.deleted` → character-personality (not consumed)
- `realm.deleted` → realm-history (not consumed)
- `account.deleted` → permission (not consumed)

### Issue #153: Escrow Asset Transfer Events Unconsumed

Escrow publishes `escrow.released` and `escrow.refunded` but no service consumes them, causing assets to remain locked.

### Issue #154: Session Lifecycle Events Incomplete

- Voice doesn't consume `session.disconnected`
- Matchmaking doesn't republish shortcuts on reconnect

### Issue #170: Realm Reference Counting

Realm deletion needs to verify no Location/Character references, but shouldn't query them directly.

---

## Remediation Priority

| Priority | # | Violation | Reason |
|----------|---|-----------|--------|
| **P0** | 3, 7 | Auth → Subscription | Architectural error - breaks app/game domain boundary |
| **P1** | 5 | Website schema → L2 | Schema defines endpoints requiring L2 deps (hidden by stub) |
| **P2** | 1 | Character → Actor/Encounter | Foundation depending on feature |
| **P2** | 2 | Character → Personality/History | Foundation depending on feature |
| **P3** | 6 | GameSession → Voice | Design improvement - invert dependency |
| ✅ | 4, 8-10 | Analytics | Resolved by reclassification to L4 |

---

## Summary

| Category | Count | Status |
|----------|-------|--------|
| L1 → L2 (App Foundation → Game Foundation) | 1 | **P0 - Auth → Subscription** |
| L3 → L2 (App Feature → Game Foundation) | 1 | **P1 - Website schema design** |
| L2 → L4 (Game Foundation → Game Features) | 2 | P2 - Character violations |
| L4 → L4 (Design issues) | 1 | P3 - GameSession → Voice |
| **Total active violations** | **5** | **1 P0, 1 P1, 2 P2, 1 P3** |
| ✅ Resolved by reclassification | 4 | Analytics moved to L4 |

---

## Next Steps

1. **P0**: Delete Auth's subscription dependencies immediately
2. **P1**: Redesign Website schema - remove character/subscription endpoints or create game-portal (L4)
3. **P2**: Implement reference registration pattern for Character (#259)
4. **P2**: Implement missing event cascades (#152)
5. **P3**: Invert GameSession → Voice dependency

**Completed**:
- ✅ Analytics reclassified to L4 (event-consumption-only service, most optional plugin)

---

*This document is updated alongside SERVICE_HIERARCHY.md. All violations must be tracked here until remediated.*
