# Agency Plugin Deep Dive

> **Plugin**: lib-agency (not yet created)
> **Schema**: `schemas/agency-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: agency-domains (MySQL), agency-modules (MySQL), agency-influences (MySQL), agency-manifest-cache (Redis), agency-manifest-history (MySQL), agency-seed-config (Redis), agency-lock (Redis) — all planned
> **Layer**: L4 GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists. L4-audited (2026-03-03).
> **Implementation Map**: [docs/maps/AGENCY.md](../maps/AGENCY.md)
> **Short**: Guardian spirit progressive agency and UX manifest engine for player capability unlocking

---

## Overview

The Agency service (L4 GameFeatures) manages the guardian spirit's progressive agency system -- the bridge between Seed's abstract capability data and the client's concrete UX module rendering. It answers the question: "Given this guardian spirit's accumulated experience, what can the player perceive and do?" Game-agnostic: domain codes, modules, and influence types are registered per game service, not hardcoded. Internal-only, never internet-facing.

---

## Architecture

Three subsystems:

1. **UX Module Registry**: Definitions of available UX modules, their capability requirements, and fidelity curves. A module is a discrete UI element or interaction mode (stance selector, timing windows, material chooser, tone suggestion) that the client loads based on the spirit's capabilities.

2. **Manifest Engine**: Computes per-seed UX manifests from seed capabilities, caches results in Redis, and publishes updates when capabilities change. The manifest tells the client exactly which modules to load and at what fidelity level.

3. **Influence Registry**: Definitions of spirit influence types (nudges) that the player can send to their possessed character. Each influence maps to an Actor perception type and has compliance factors that determine how likely the character is to accept it.

**Cross-generational persistence**: Guardian seed growth persists across character lifetimes because seeds are account-owned, not character-owned. When a character dies, the spirit's accumulated capabilities (and therefore its UX manifest) are unaffected. The spirit carries its full agency into whatever character it possesses next.

**What Agency is NOT**:
- **Not a permission system.** Permission (L1) gates API endpoint access based on roles and states. Agency gates UX module visibility based on spirit growth. They use similar push mechanisms but serve orthogonal purposes.
- **Not a skill system.** Seed (L2) tracks capability depth and fidelity. Agency translates those numbers into UX decisions. Agency does not own growth, thresholds, or capability computation -- Seed does.
- **Not a behavior system.** Actor (L2) executes character cognition. Agency provides `${spirit.*}` variables that Actor's ABML behaviors evaluate, but Agency does not execute behaviors or make character decisions.

### Key Concepts

**UX Domain**: A category of player experience. Opaque string codes, registered via API. Each domain maps to one or more Seed growth domain paths.

| Domain Code | Description | Seed Domain Paths |
|------------|-------------|-------------------|
| `combat` | Combat perception and influence | `combat.stance`, `combat.timing`, `combat.combo`, `combat.style` |
| `crafting` | Crafting process interaction | `crafting.material`, `crafting.technique`, `crafting.quality` |
| `social` | Social perception and communication | `social.tone`, `social.topic`, `social.negotiation` |
| `trade` | Economic interaction | `trade.pricing`, `trade.partner`, `trade.inventory` |
| `exploration` | Navigation and environment perception | `exploration.route`, `exploration.landmark`, `exploration.danger` |
| `magic` | Spellcasting interaction | `magic.stage`, `magic.pneuma`, `magic.composition` |

Domain codes are opaque strings. New domains (e.g., `governance`, `husbandry`, `music`) can be registered without schema changes.

**UX Module**: A specific UI element or interaction mode within a domain. Defined by:

| Field | Type | Description |
|-------|------|-------------|
| Code | string | Unique identifier (e.g., `combat/stance_selector`) |
| DomainCode | string | Parent domain |
| DisplayName | string | Human-readable name for admin tools |
| Description | string | What this module provides to the player |
| RequiredCapability | string | Seed capability path that gates this module |
| DepthThreshold | float | Minimum seed depth to enable this module |
| FidelityCurve | float[] | Depth thresholds for fidelity levels 1-N |
| SortOrder | int | Display order within domain |
| GameServiceId | Guid? | Optional game-service scope |

Example modules for the combat domain:

| Module Code | Depth Threshold | Fidelity Curve [1,2,3,4,5] | Description |
|-------------|----------------|---------------------------|-------------|
| `combat/intention` | 0.0 | [0, 0, 0, 0, 0] | Basic approach/retreat/hold (always available) |
| `combat/stance_selector` | 2.0 | [2.0, 3.5, 5.0, 7.0, 9.0] | Defensive/aggressive/balanced stance selection |
| `combat/timing_windows` | 4.0 | [4.0, 5.5, 7.0, 8.5, 10.0] | Dodge/parry/strike timing visibility |
| `combat/combo_direction` | 6.0 | [6.0, 7.0, 8.0, 9.0, 10.0] | Sequence choreography direction |
| `combat/style_mastery` | 8.0 | [8.0, 8.5, 9.0, 9.5, 10.0] | Martial discipline specialization |

**Fidelity Level**: An integer (1-5) representing how much detail/control a UX module provides. Computed from the guardian seed's depth in the required capability path, mapped through the module's fidelity curve. At depth 5.0 in `combat.stance`, `combat/stance_selector` is at fidelity 3 (thresholds [2.0, 3.5, **5.0**, 7.0, 9.0] -- depth 5.0 crosses the third threshold). Higher fidelity = more granular control, more information displayed.

**Influence Type**: A specific nudge the player can send to their possessed character. Defined by:

| Field | Type | Description |
|-------|------|-------------|
| Code | string | Unique identifier (e.g., `combat/approach`) |
| DomainCode | string | Parent domain |
| RequiredCapability | string | Seed capability path that gates this influence |
| DepthThreshold | float | Minimum seed depth to enable |
| PerceptionType | string | Actor perception type this maps to (e.g., `spirit_combat_nudge`) |
| ComplianceFactors | array of `{axisCode: string, weight: float}` | Personality/disposition axes that affect character acceptance. Typed array, NOT `additionalProperties: true` — Agency reads specific keys. |
| Intensity | float | How strongly this nudge pushes the character (0.0 gentle suggestion, 1.0 forceful command) |

Example influences:

| Influence Code | Depth Threshold | Perception Type | Compliance Factors |
|---------------|----------------|-----------------|-------------------|
| `combat/approach` | 0.0 | `spirit_directional` | stubbornness: -0.3, aggression: +0.2 |
| `combat/retreat` | 0.0 | `spirit_directional` | courage: -0.4, self_preservation: +0.3 |
| `combat/use_stance` | 2.0 | `spirit_combat_nudge` | discipline: +0.3, stubbornness: -0.2 |
| `social/greet` | 1.0 | `spirit_social_nudge` | sociability: +0.4, shyness: -0.3 |
| `trade/buy` | 3.0 | `spirit_economic_nudge` | frugality: -0.3, impulsiveness: +0.2 |

**Spirit Manifest**: The computed output for a specific guardian seed. Cached in Redis, invalidated on seed capability changes. Contains:

```json
{
 "seedId": "...",
 "computedAt": "2026-02-14T12:00:00Z",
 "domains": {
 "combat": {
 "overallFidelity": 0.65,
 "modules": [
 { "code": "combat/intention", "enabled": true, "fidelityLevel": 5 },
 { "code": "combat/stance_selector", "enabled": true, "fidelityLevel": 3 },
 { "code": "combat/timing_windows", "enabled": true, "fidelityLevel": 1 },
 { "code": "combat/combo_direction", "enabled": false },
 { "code": "combat/style_mastery", "enabled": false }
 ],
 "influences": [
 { "code": "combat/approach", "available": true },
 { "code": "combat/retreat", "available": true },
 { "code": "combat/use_stance", "available": true }
 ]
 }
 }
}
```

**Compliance Base**: The character's willingness to accept spirit influence, derived from Disposition's guardian feelings. This is the `${spirit.compliance_base}` variable, computed as:

```
compliance_base = guardian_trust * (1.0 - guardian_resentment * ComplianceResentmentWeight) * familiarity_modifier
where familiarity_modifier = min(1.0, guardian_familiarity * ComplianceFamiliarityScale + ComplianceFamiliarityFloor)
```

All formula coefficients are configurable: `ComplianceResentmentWeight` (default 0.7), `ComplianceFamiliarityScale` (default 1.2), `ComplianceFamiliarityFloor` (default 0.2). If Disposition is unavailable, compliance defaults to the configured `DefaultComplianceBase` (0.5).

### How It Works

```
1. Game designers create domains, modules, and influence types via API
 (or seed from configuration on startup)

2. Player connects -> Gardener determines their active seed

3. Agency computes UX manifest:
 For each registered module:
 - Get seed's depth for module's RequiredCapability
 - If depth >= DepthThreshold: module enabled
 - Compute fidelity level from FidelityCurve
 For each registered influence:
 - Get seed's depth for influence's RequiredCapability
 - If depth >= DepthThreshold: influence available

4. Manifest cached in Redis, published as agency.manifest.updated event

5. Gardener receives event, pushes manifest to client
 via Entity Session Registry / Connect per-session RabbitMQ queue

6. Client loads/unloads UX modules based on manifest

7. When player sends a nudge:
 - Connect routes to Gardener
 - Gardener calls Agency to validate influence against manifest
 - If valid, Gardener god-actor (see BEHAVIORAL-BOOTSTRAP.md) modulates and forwards
 - Actor injects perception into character's cognition pipeline
 - Character's ABML evaluates against ${spirit.*} compliance variables
 - Character complies or resists
```

### The Progressive UX Gradient

Agency is the player-facing expression of PLAYER-VISION.md's core thesis: "The guardian spirit starts as nearly inert. Through accumulated experience, it gains understanding. Understanding manifests as increased control fidelity and richer UX surface area." PLAYER-VISION.md describes a gradient from "barely any influence" to "deep domain mastery." Agency implements this as a continuous expansion of the manifest:

| Guardian Seed Depth | Combat UX | Social UX | Crafting UX |
|--------------------|-----------|-----------|-------------|
| 0 (new spirit) | Intention only (approach/retreat/hold) | Observe conversations | Watch character work |
| 2-3 | Stance selection appears | Tone suggestion appears | Material selection appears |
| 4-5 | Timing windows visible | Topic steering available | Technique choice available |
| 6-7 | Combo direction available | Negotiation strategy | Quality targeting |
| 8-10 | Style mastery specialization | Relationship management | Process optimization |

The same spirit might be at depth 7 in combat and depth 2 in social -- they see a rich combat UX but minimal social UX. The domains develop independently based on where the spirit invests experience (via Seed growth). Cross-pollination across seeds (PLAYER-VISION: "accumulated experience crosses seed boundaries") means a spirit with combat mastery from Seed 1 carries some combat agency into Seed 2.

---

## Dependents

| Dependent | Relationship |
|-----------|-------------|
| lib-gardener (L4) | Calls `/agency/manifest/get` and `/agency/influence/execute` for player experience routing and spirit influence validation |
| lib-actor (L2) | Consumes `${spirit.*}` variables via `IVariableProviderFactory` for ABML behavior evaluation in character cognition pipelines |
| lib-disposition (L4) | Receives guardian feeling updates triggered by influence outcomes (compliance increases trust, forced overrides increase resentment) |

> **Note**: Dependencies, state storage, events, configuration, DI services, and API endpoints are documented in the [Implementation Map](../maps/AGENCY.md).

---

## Visual Aid

The Co-Pilot Pattern flow in the Architecture section above illustrates the end-to-end influence path (Player -> Connect -> Gardener -> Agency -> Gardener god-actor -> Actor -> Character ABML). The Progressive UX Gradient table shows how manifest content evolves with seed depth. These two diagrams capture the core data flow that is not obvious from the dependency and event tables alone.

---

## Stubs & Unimplemented Features

Pre-implementation. No schema, no code. All features described in this document are aspirational.

---

## Potential Extensions

1. **Influence combos.** Sequential influences within a time window combine into higher-level commands. Approach + Strike within 500ms = Charge. Combo definitions as registrable entities with sequence patterns and result perception types.

2. **Character-specific manifest modifiers.** A character's personality or species modifies the effective manifest. A stubborn character reduces social domain fidelity by 20%. A magically-gifted species boosts magic domain fidelity by 30%. Applied as post-computation modifiers before caching.

3. **Manifest presets.** Pre-computed manifest configurations for specific scenarios (tutorial, arena combat, crafting session). Gardener or Puppetmaster god-actors can temporarily apply a preset, overriding the normal seed-based computation.

4. **Spirit-to-spirit influence.** In the twin spirits / pair system (PLAYER-VISION.md), one spirit could influence the other's character. Requires cross-seed manifest evaluation, bond-based compliance modifiers (Seed bonds), and a communication channel through Gardener.

5. **Spectator manifests.** Reduced-fidelity manifests for observing characters the spirit doesn't control. Useful for multiplayer observation and the Showtime streaming metagame. Could be a fixed "observer" manifest preset per domain.

6. **Influence analytics dashboard.** Track acceptance/resistance rates per influence type, per domain, per character. Aggregate into gameplay balance metrics. Feed into Analytics (L4) for game designer insights.

7. **Domain-specific compliance.** Instead of a single `compliance_base`, per-domain compliance based on influence history. A spirit that repeatedly forces combat against personality gets combat-specific resentment. Requires per-domain feeling tracking in Disposition.

8. **Adaptive difficulty via manifest.** Gardener god-actors could temporarily boost or reduce module fidelity based on player performance, without changing the underlying seed depth. "Training wheels" that the god applies when the player is struggling.

9. **Hacking mechanic (Omega realm).** PLAYER-VISION describes "hacking" as using UX modules on characters/seeds that haven't earned them. Mechanically: temporarily override the manifest with modules the seed hasn't naturally unlocked. The character resists more strongly (compliance penalty), results may be inferior (forced fidelity reduction), and there are social/ethical consequences.

---

## Variable Provider Factory

### `${spirit.*}` Namespace

Registered via `IVariableProviderFactory` as `SpiritProviderFactory`. Consumed by Actor (L2) for ABML behavior evaluation in character cognition pipelines.

| Variable | Type | Source | Description |
|----------|------|--------|-------------|
| `spirit.active` | bool | Gardener | Whether the guardian spirit is actively possessing this character right now |
| `spirit.compliance_base` | float | Disposition | Base willingness to accept influence. Formula uses configurable coefficients (see Compliance Base section above) |
| `spirit.resentment` | float | Disposition | Current resentment toward the guardian spirit (0.0-1.0) |
| `spirit.trust` | float | Disposition | Current trust toward the guardian spirit (0.0-1.0) |
| `spirit.familiarity` | float | Disposition | How well the character knows the spirit (0.0-1.0) |
| `spirit.domain.<domain>.fidelity` | float | Manifest | Spirit's overall fidelity in a domain (0.0-1.0), computed as average module fidelity |
| `spirit.domain.<domain>.depth` | float | Seed | Raw seed depth for the domain's primary capability path |
| `spirit.influence.last_type` | string | Influence history | Code of the most recent influence attempt |
| `spirit.influence.last_accepted` | bool | Influence history | Whether the most recent influence was accepted by the character |
| `spirit.influence.frequency` | float | Rolling window (Redis) | Influences per minute over a rolling window (`InfluenceFrequencyWindowMinutes`, default 5) |
| `spirit.influence.resistance_buildup` | float | Computed | Accumulated resistance from frequent overrides. Decays at `ComplianceResistanceDecayRate` per game-hour. |

### Usage in ABML Behavior Documents

```yaml
# Character evaluates a spirit combat nudge
perception_handler:
 type: spirit_combat_nudge
 evaluation:
 # Base compliance check
 - condition: "${spirit.compliance_base} > 0.3"
 weight: 1.0
 # Not being spammed
 - condition: "${spirit.influence.frequency} < 4.0"
 weight: 0.5
 # Nudge aligns with personality (aggressive character accepts attack nudges)
 - condition: "nudge_alignment(${personality.*}, nudge.compliance_factors) > 0.0"
 weight: 0.8
 # Not building up resistance from overuse
 - condition: "${spirit.influence.resistance_buildup} < 0.7"
 weight: 0.6
 on_accept:
 - execute_nudged_action
 - update: spirit.influence.last_accepted = true
 on_resist:
 - continue_autonomous_action
 - update: spirit.influence.last_accepted = false
 - publish: actor.spirit-nudge.resisted
 # Actor (L2) publishes to its own namespace; Agency (L4) subscribes and
 # re-publishes as agency.influence.resisted (hierarchy-safe relay)
 # Resistance may increase resentment (via Disposition)
 # NOTE: Disposition's actual recording API shape must be confirmed during
 # implementation — the endpoint may accept structured feeling records rather
 # than raw axis+delta pairs. Coordinate with Disposition's schema design.
 - call: /disposition/feeling/record
 with: { targetType: "Character", targetId: guardian_character_id, axis: "resentment", delta: 0.02 }
```

---

## Background Workers

### ManifestRecomputeWorker

**Trigger**: `seed.capability.updated`, `seed.growth.recorded`, and `disposition.guardian.shifted` events, debounced by `ManifestRecomputeDebounceMs`.

The `disposition.guardian.shifted` trigger is necessary because `${spirit.compliance_base}` depends on Disposition's guardian feelings (trust, resentment, familiarity). When guardian feelings change (e.g., after repeated forced overrides or positive compliance), the compliance base changes, which affects which influences are effectively available and how the character responds to nudges. Without this subscription, compliance variables would go stale until the next seed growth event.

**Multi-instance safety**: Debounce timers MUST be Redis-based (e.g., Redis key with TTL per seed), not in-memory. In-memory timers are per-instance and would cause duplicate recomputation across nodes.

**Process**:
1. Receive seed capability change event or disposition guardian shift event
2. Check Redis debounce key -- if another event for this seed arrived within the debounce window, reset TTL
3. After debounce settles, fetch seed's full capability manifest from Seed service
4. If `CrossSeedPollinationEnabled`, also fetch capabilities from other seeds owned by the same account (apply `CrossSeedPollinationFactor` multiplier to cross-seed depths)
5. Fetch guardian feelings from Disposition (soft dependency -- use `DefaultComplianceBase` if unavailable)
6. For each registered module, evaluate whether depth meets threshold and compute fidelity level
7. For each registered influence, evaluate whether depth meets threshold
8. Compare with cached manifest
9. If changed: update Redis cache, write to history store, publish `agency.manifest.updated`

---

## Implementation Plan

### Phase 1: Core Registry (6 endpoints)
- Create schemas for domains, modules, influences
- Generate code
- Implement domain CRUD (create, get, list, delete)
- Implement module create and list
- Add state stores (agency-domains, agency-modules, agency-influences)

### Phase 2: Manifest Engine (4 endpoints)
- Implement manifest computation from seed capabilities
- Add Redis caching (agency-manifest-cache)
- Implement manifest get, recompute, diff, history
- Subscribe to seed.capability.updated events
- Implement ManifestRecomputeWorker with debouncing

### Phase 3: Influence System (7 endpoints)
- Implement influence create, update, get, list, delete
- Implement influence evaluate and execute
- Add rate limiting via Redis counters
- Publish influence execution/rejection/resistance events

### Phase 4: Variable Provider Factory
- Implement SpiritProviderFactory and SpiritProvider
- Register ${spirit.*} namespace
- Integrate Disposition for compliance computation
- Implement influence history tracking in Redis

### Phase 5: Integration
- Integrate with Gardener for manifest push routing via Entity Session Registry (available)
- Seed from configuration support
- Cross-seed pollination computation
- Manifest history retention worker

### Prerequisites
- lib-seed (L2): Must be running for capability manifest reads (hard dependency)
- lib-disposition (L4): Should be running for compliance computation (soft dependency — graceful degradation to `DefaultComplianceBase`)
- lib-gardener (L4): Should be running for manifest push and influence execution routing (soft dependency)

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(None -- pre-implementation)*

### Pre-Implementation Audit Findings (2026-03-03)

**Critical (must resolve before writing schemas):**

1. ~~**Missing x-lifecycle events**~~: **FIXED** (2026-03-08) - Implementation map now specifies 9 lifecycle events (created/updated/deleted for domain, module, influence) using `x-lifecycle` with `topic_prefix: agency` for Pattern C topic naming (`agency.domain.created`, etc.).

2. ~~**Missing x-permissions on all 22 endpoints**~~: **FIXED** (2026-03-08) - Implementation map specifies roles for all 22 endpoints: `developer` for admin CRUD (domain/module/influence management, seed-config, recompute), `user` for runtime queries (manifest get/diff/history, influence evaluate/execute).

3. ~~**ComplianceFactors schema must be typed**~~: **FIXED** (2026-03-08) - ComplianceFactors defined as typed array of `{axisCode: string, weight: float}` objects. Agency reads specific keys per tenets; `additionalProperties: true` pattern avoided.

4. ~~**Actor→Agency event hierarchy violation**~~: **FIXED** (2026-03-08) - Actor (L2) now publishes `actor.spirit-nudge.resisted` to its own namespace; Agency (L4) subscribes and relays as `agency.influence.resisted`. Hierarchy-safe event relay pattern.

5. **Missing lib-resource integration for seed-keyed data.** `agency-manifest-history` (MySQL) is persistent data keyed by seedId. Must implement `ISeededResourceProvider` and declare `x-references` with `target: seed, onDelete: cascade` per tenets.

**Warnings (address during schema creation):**

6. **ManifestRecomputeWorker debouncing must use Redis**, not in-memory timers. Per-instance timers cause duplicate recomputation across nodes.

7. ~~**Compliance formula magic numbers**~~: **FIXED** (2026-03-08) - All formula coefficients (`ComplianceResentmentWeight`, `ComplianceFamiliarityScale`, `ComplianceFamiliarityFloor`, `DefaultComplianceBase`) documented as configurable properties in deep dive and referenced throughout implementation map pseudo-code.

8. ~~**Rolling window for influence frequency**~~: **FIXED** (2026-03-08) - `InfluenceFrequencyWindowMinutes` configurable property documented in deep dive (default 5 minutes) and referenced in implementation map pseudo-code for rolling window calculation.

9. **Influence frequency counters must use Redis** (sorted sets or atomic counters), not in-memory state, for multi-instance safety.

10. ~~**Custom events must use flat structure**~~: **FIXED** (2026-03-08) - Original finding was incorrect. SCHEMA-RULES.md explicitly requires custom events to use `allOf` composition with `BaseServiceEvent` (line 962: "Custom service events MUST use `allOf` composition with `BaseServiceEvent`"). The `allOf` pattern produces C# inheritance, `IBannouEvent` implementation, and `EventName` for message tap forwarding. `eventId` and `timestamp` are inherited from `BaseServiceEvent` and must NOT be redefined as properties (only listed in `required`).

11. **FidelityCurve needs validation keywords**: `minItems: 1`, reasonable `maxItems`, `minimum: 0.0` on items.

12. ~~**State store naming**~~: **FIXED** (2026-03-08) - Header and implementation map updated to `agency-manifest-cache`; `agency-lock` added for distributed locking. Redis `prefix` declarations will be added when `state-stores.yaml` entries are created during implementation.

13. **All config properties need `env:` keys** with `AGENCY_` prefix in configuration schema.

14. **Float config properties need min/max validation**: `DefaultComplianceBase` (0.0-1.0), `CrossSeedPollinationFactor` (0.0-1.0), etc.

15. **Must declare `x-references`** for seed-keyed data cleanup.

16. **Must register `spirit` in `schemas/variable-providers.yaml`.**

17. **Must list all events in `x-event-publications`.**

18. **Manifest nested types** (`ManifestDomain`, `ManifestModule`, `ManifestInfluence`) must be defined as named schemas for `$ref` reuse.

19. ~~**Inconsistent create/delete naming**~~: **FIXED** (2026-03-08) - Standardized to `create/delete` throughout deep dive and implementation map. Agency's entities are Category A definitions with full CRUD lifecycle (not type registrations), matching the established pattern (item templates, quest definitions, currency definitions).

20. **Telemetry spans** required on all async helpers per tenets (add `ITelemetryProvider` to DI services).

21. **Game service cleanup**: game-service-scoped definitions become orphaned if a game service is deleted. Consider lib-resource integration for game-service-keyed data.

### Intentional Quirks (Documented Behavior)

1. **Fidelity levels are integers, not floats.** The client receives fidelity 1-5, not a continuous value. This is intentional -- UX modules should have discrete behavior modes (e.g., "show 3 stance options" vs "show 5 stance options"), not infinitely variable rendering. The continuous seed fidelity (0.0-1.0) is quantized into discrete module fidelity levels.

2. **Compliance base is computed by Agency, not by ABML.** The formula for `${spirit.compliance_base}` is in Agency's variable provider, not in the behavior document. This means the compliance formula is service code, not authored content. This is deliberate: the base compliance is a system-level mechanic that should be consistent across all characters. ABML behaviors can modify the effective compliance via additional checks (personality alignment, resistance buildup, situational modifiers), but the base value is authoritative.

3. **Influence execution does not directly call Actor.** Agency validates and enriches the influence, then returns the payload. Gardener (or the gardener god-actor, per BEHAVIORAL-BOOTSTRAP.md) is responsible for injecting the perception into Actor. This separation allows god-actors to modulate or intercept influences before they reach the character.

4. **Cross-seed pollination is multiplicative, not additive.** A spirit with combat depth 10.0 on Seed 1 gets `10.0 * 0.3 = 3.0` combat depth contribution to Seed 2's manifest computation. This means cross-pollination provides a head start, not full capability transfer. The spirit still needs to develop domain depth on each seed for high-fidelity modules.

5. **Rate limiting is per-seed, not per-session.** `InfluenceRateLimitPerSecond` applies to the seed (guardian spirit), not the WebSocket session. If a player reconnects rapidly, the rate limit persists because it's tracked against the seed ID in Redis, not the session.

6. **Deprecation lifecycle.** Domains, modules, and influence types are **Category A** definition entities per tenets: they are world-building definitions that need deprecation→delete lifecycle with reason tracking, idempotent deprecation, and `includeDeprecated` on list endpoints. Manifests are computed output (not entities) and do not need deprecation.

### Design Considerations (Requires Planning)

1. **Cross-seed capability aggregation.** PLAYER-VISION says "accumulated experience crosses seed boundaries." Same-type cross-seed sharing is implemented in Seed ([#353](https://github.com/beyond-immersion/bannou-service/issues/353), closed). Cross-type transfer deferred ([#354](https://github.com/beyond-immersion/bannou-service/issues/354), open). **Needs clarification**: does Agency apply its own `CrossSeedPollinationFactor` on top of what Seed already handles internally, or does it just read the already-pollinated capabilities from Seed? If Seed's `SameOwnerGrowthMultiplier` already handles same-type pollination, Agency's `CrossSeedPollinationFactor` may be redundant or may only apply to cross-type scenarios.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/610 -->

2. **Realm-specific module sets.** Game-service scoping is sufficient for now. Realm-specific scoping would require a RealmId field on module definitions.

---

## Work Tracking

No Agency-specific issues exist yet (pre-implementation). The following cross-cutting issues inform Agency's design:

### Related Issues (Cross-Service)

| Issue | Title | Status | Relevance |
|-------|-------|--------|-----------|
| [#426](https://github.com/beyond-immersion/bannou-service/issues/426) | Entity Session Registry | **Closed** | Prerequisite for manifest push routing — complete. |
| [#497](https://github.com/beyond-immersion/bannou-service/issues/497) | Seed: Client events for guardian spirit progression | Open | Complements Agency's manifest push — Seed pushes growth events, Agency pushes manifest updates. |
| [#502](https://github.com/beyond-immersion/bannou-service/issues/502) | Meta: Client Event Rollout for L1/L2 Plugins | Open | Agency's client events should be included in rollout plan. |
| [#353](https://github.com/beyond-immersion/bannou-service/issues/353) | lib-seed: Cross-seed growth sharing (same owner, same type) | **Closed** | Implemented. Feeds Agency's capability reads. May make `CrossSeedPollinationFactor` redundant for same-type scenarios. |
| [#354](https://github.com/beyond-immersion/bannou-service/issues/354) | lib-seed: Cross-seed-type growth transfer matrix | Open | Future enhancement. Agency's cross-type pollination depends on this. |
| [#361](https://github.com/beyond-immersion/bannou-service/issues/361) | Seed variable provider | **Closed** | Pattern Agency must follow for `IVariableProviderFactory`. |
| [#191](https://github.com/beyond-immersion/bannou-service/issues/191) | Actor: Session-Bound Actor Support | Open | `${spirit.active}` variable depends on session-bound actor awareness. |
| [#410](https://github.com/beyond-immersion/bannou-service/issues/410) | Second Thoughts (lib-obligation) | Open | Compliance interacts with obligation cost modifiers — potential cross-pollination between `${spirit.compliance_base}` and `${obligations.*}`. |
| [#386](https://github.com/beyond-immersion/bannou-service/issues/386) | Gardener: Bond communication via Chat | Open | Alternative spirit-character communication channel alongside influence system. |
| [#375](https://github.com/beyond-immersion/bannou-service/issues/375) | Collection→Seed→Status pipeline | **Closed** | Foundational growth pipeline that feeds Agency's capability reads. |

### Issues to Create

1. **Agency: Implementation tracking** — Master issue for the 5-phase implementation plan.
2. **Agency: Client events design** — Define how manifest updates route through Gardener to clients.
3. **Agency: Disposition integration** — Compliance feedback loop: influence outcomes → guardian feelings → compliance base.
4. **Agency: Gardener integration** — Manifest routing, influence validation, god-actor modulation path.
5. **Agency: `${spirit.*}` variable provider coordination** — Ensure no namespace collision with `${seed.*}` provider.

---

*This document describes a planned L4 GameFeatures service. For the behavioral bootstrap pattern that Agency interacts with, see [BEHAVIORAL-BOOTSTRAP.md](../guides/BEHAVIORAL-BOOTSTRAP.md). For the guardian seed that Agency reads capabilities from, see [SEED.md](SEED.md). For the disposition feelings that feed compliance computation, see [DISPOSITION.md](DISPOSITION.md).*
