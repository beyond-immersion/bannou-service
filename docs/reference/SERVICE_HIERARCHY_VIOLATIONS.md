# Service Hierarchy Violations Tracker

> **Created**: 2026-02-01
> **Updated**: 2026-02-03 (Contract → Location violation discovered)
> **Purpose**: Track and remediate violations of the service dependency hierarchy

This document tracks known violations of the service hierarchy, their severity under the 5-layer model, and suggested remediation patterns.

---

## Quick Reference: Layer Assignments (v2.0)

| Layer | Name | Services |
|-------|------|----------|
| **L0** | Infrastructure | lib-state, lib-messaging, lib-mesh |
| **L1** | App Foundation | account, auth, connect, permission, contract, resource |
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

### 1. Character → Actor/Encounter (RESOLVED)

**Service**: lib-character (L2 Game Foundation)
**Former Violating Dependencies**:
- `IActorClient` (L4)
- `ICharacterEncounterClient` (L4)

**Location**: `CharacterService.cs` constructor, `CheckCharacterReferencesAsync` method

**Violation Type**: L2 → L4 (Foundation depending on Feature)

**Original Intent**: Count references to a character for cleanup eligibility.

**Resolution**: Removed `IActorClient` and `ICharacterEncounterClient` from CharacterService. The `CheckCharacterReferencesAsync` method now only checks references from same-layer or lower services:
- Relationships (L2) - allowed
- Contracts (L1) - allowed
- Actor/Encounter references (L4) - **no longer checked** (per SERVICE_HIERARCHY)

For comprehensive reference tracking including L4 services, the recommended pattern is event-driven reference registration where L4 services publish to `character.reference.registered/unregistered` topics. This can be implemented in the future if needed.

**Status**: ✅ RESOLVED by removing L4 dependencies

---

### 2. Character → CharacterPersonality/CharacterHistory (RESOLVED)

**Service**: lib-character (L2 Game Foundation)
**Former Violating Dependencies**:
- `ICharacterPersonalityClient` (L4)
- `ICharacterHistoryClient` (L4)

**Location**: `CharacterService.cs` constructor, `GetEnrichedCharacterAsync` and `CompressCharacterAsync` methods

**Violation Type**: L2 → L4 (Foundation depending on Feature)

**Original Intent**:
1. **Enrichment**: Optionally include personality/history when fetching character
2. **Compression**: Summarize data when archiving dead character

**Resolution**: Removed `ICharacterPersonalityClient` and `ICharacterHistoryClient` from CharacterService.

- **GetEnrichedCharacterAsync**: Now returns only base character data and family tree (via L2 Relationships). Include flags for personality/backstory/combatPreferences are logged but ignored. Callers needing enriched data should aggregate from L4 services directly or use a future L4 aggregator service.

- **CompressCharacterAsync**: Archives now include only family summary (L2 Relationships). Personality and history data are NOT summarized. The `character.compressed` event is published so L4 services can subscribe to handle their own cleanup. The `deleteSourceData` flag cannot delete L4 service data per SERVICE_HIERARCHY.

**Future Enhancement**: If enrichment is needed, create "CharacterAggregator" in L4 that fetches from Character + extensions. For compression summaries, L4 services can subscribe to `character.compressed` and generate/store summaries independently.

**Status**: ✅ RESOLVED by removing L4 dependencies

---

### 3. Auth → Subscription (RESOLVED)

**Service**: lib-auth (L1 App Foundation)
**Former Violating Dependencies**:
- `ISubscriptionClient` (L2)

**Resolution**: Deleted all subscription-related code from Auth:
- Removed `ISubscriptionClient` from both `AuthService.cs` and `TokenService.cs`
- Deleted `PropagateSubscriptionChangesAsync` method
- Deleted `HandleSubscriptionUpdatedAsync` event handler
- Removed `subscription.updated` from `auth-events.yaml`

**Rationale**: Auth (L1) is app-level infrastructure that should work for ANY deployment. Subscription (L2) is game-specific. Services that need subscription state (like GameSession) already handle their own `subscription.updated` event subscriptions independently.

**Status**: ✅ RESOLVED by deletion

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

### 5. Website → Game Services (RESOLVED)

**Service**: lib-website (L3 App Features)
**Former Violating Schema Elements**:
- `GetAccountCharactersAsync` → needed `ICharacterClient` (L2)
- `GetServerStatusAsync` → needed realm data (L2)
- `GetAccountSubscriptionAsync` → needed `ISubscriptionClient` (L2)

**Resolution**: Applied Option 1 - Removed game-related endpoints from Website schema. Website now serves as pure CMS (pages, news, downloads, contact forms) with no game data dependencies.

**Removed from schema**:
- `/website/server-status` endpoint
- `/website/account/characters` endpoint
- `/website/account/subscription` endpoint
- `ServerStatusResponse`, `RealmStatus`, `CharacterListResponse`, `CharacterSummary`, `SubscriptionResponse` schemas
- `characterSlots`, `usedSlots` fields from `AccountProfile`

**Status**: ✅ RESOLVED by schema redesign

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

### 11. Contract → Location (CRITICAL - NEW)

**Service**: lib-contract (L1 App Foundation)
**Violating Dependencies**:
- `ILocationClient` (L2)

**Location**: `ContractService.cs` line 40 (field), line 201 (constructor), line 1889 (usage)

**Violation Type**: L1 → L2 (App Foundation depending on Game Foundation)

**Original Intent**: Get location ancestry for contract clause validation (e.g., "within this region").

**Impact**: Contract service cannot function in non-game deployments because it requires L2 Location service.

**Suggested Remediation Options**:

1. **Option A: Remove location validation** - If location-based contract clauses are rare, remove the feature entirely and use prebound APIs that callers validate.

2. **Option B: Make location validation optional** - Check for LocationClient availability at runtime; skip location validation if unavailable. Log warning when location clauses are used without Location service.

3. **Option C: Move location clause logic to L4** - Create a "ContractExtensions" service in L4 that subscribes to `contract.clause.validating` events and provides location-based validation. Contract (L1) publishes the event with location ID; ContractExtensions (L4) validates and publishes result.

**Recommended**: Option B (graceful degradation) or Option C (event-driven extension)

**Status**: P1 - CRITICAL (L1 → L2 violation prevents non-game deployments)

---

## Confirmed Event Subscription Violations

### 7. Auth subscribes to subscription.updated (RESOLVED)

**Former Subscriber**: lib-auth (L1 App Foundation)
**Topic**: `subscription.updated`
**Publisher**: lib-subscription (L2 Game Foundation)

**Resolution**: Removed `subscription.updated` from `schemas/auth-events.yaml` as part of #3.

**Status**: ✅ RESOLVED by deletion (part of #3)

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
| Website → L2 | L3 → L2 | **OK** | Schema redesigned to pure CMS (no game endpoints) |

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
- (none remaining)

### Resolved Event Subscriptions

**Auth subscription removed**:
- ✅ Auth no longer subscribes to `subscription.updated` (was L1 → L2)

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
| **P1** | 11 | Contract → Location | CRITICAL: L1 → L2 prevents non-game deployments |
| **P3** | 6 | GameSession → Voice | Design improvement - invert dependency |
| ✅ | 1 | Character → Actor/Encounter | Resolved by removing L4 dependencies |
| ✅ | 2 | Character → Personality/History | Resolved by removing L4 dependencies |
| ✅ | 3, 7 | Auth → Subscription | Resolved by deletion |
| ✅ | 4, 8-10 | Analytics | Resolved by reclassification to L4 |
| ✅ | 5 | Website schema | Resolved by removing game-related endpoints |

---

## Summary

| Category | Count | Status |
|----------|-------|--------|
| L1 → L2 (App Foundation → Game Foundation) | 1 | **P1 - Contract → Location** |
| L2 → L4 (Game Foundation → Game Features) | 0 | ✅ All resolved |
| L4 → L4 (Design issues) | 1 | P3 - GameSession → Voice |
| **Total active violations** | **2** | **1 P1, 1 P3** |
| ✅ Resolved by removal | 2 | Character → Actor/Encounter/Personality/History |
| ✅ Resolved by deletion | 2 | Auth → Subscription removed |
| ✅ Resolved by reclassification | 4 | Analytics moved to L4 |
| ✅ Resolved by schema redesign | 1 | Website game endpoints removed |

---

## Next Steps

1. **P1**: Remove Contract → Location dependency (#11) - L1 cannot depend on L2
2. **P2**: Implement missing event cascades (#152)
3. **P3**: Invert GameSession → Voice dependency
4. **Optional**: Implement event-driven reference registration for L4 reference tracking (if comprehensive cleanup eligibility checking is needed)

**Completed**:
- ✅ Character → Actor/Encounter dependencies removed (IActorClient and ICharacterEncounterClient removed from CharacterService; CheckCharacterReferencesAsync now only checks L2/L1 services)
- ✅ Character → Personality/History dependencies removed (ICharacterPersonalityClient and ICharacterHistoryClient removed from CharacterService; GetEnrichedCharacterAsync returns base data only, CompressCharacterAsync archives without L4 summaries)
- ✅ Auth → Subscription dependency deleted (ISubscriptionClient removed from AuthService and TokenService, event subscription removed)
- ✅ Analytics reclassified to L4 (event-consumption-only service, most optional plugin)
- ✅ Website schema redesigned - removed game-related endpoints (characters, subscription, server-status)

---

*This document is updated alongside SERVICE_HIERARCHY.md. All violations must be tracked here until remediated.*
