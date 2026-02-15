# Agency Plugin Deep Dive

> **Plugin**: lib-agency
> **Schema**: `schemas/agency-api.yaml` (pre-implementation)
> **Version**: Pre-implementation. No schema, no code.
> **State Store**: agency-domains (MySQL), agency-modules (MySQL), agency-influences (MySQL), agency-manifests (Redis), agency-manifest-history (MySQL), agency-seed-config (Redis)

---

## Overview

The Agency service (L4 GameFeatures) manages the guardian spirit's progressive agency system -- the bridge between Seed's abstract capability data and the client's concrete UX module rendering. It answers the question: "Given this guardian spirit's accumulated experience, what can the player perceive and do?" Game-agnostic: domain codes, modules, and influence types are registered per game service, not hardcoded. Internal-only, never internet-facing.

---

## Architecture

Three subsystems:

1. **UX Module Registry**: Definitions of available UX modules, their capability requirements, and fidelity curves. A module is a discrete UI element or interaction mode (stance selector, timing windows, material chooser, tone suggestion) that the client loads based on the spirit's capabilities.

2. **Manifest Engine**: Computes per-seed UX manifests from seed capabilities, caches results in Redis, and publishes updates when capabilities change. The manifest tells the client exactly which modules to load and at what fidelity level.

3. **Influence Registry**: Definitions of spirit influence types (nudges) that the player can send to their possessed character. Each influence maps to an Actor perception type and has compliance factors that determine how likely the character is to accept it.

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
| ComplianceFactors | dict | Personality/disposition axes that affect character acceptance |
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
compliance_base = guardian_trust * (1.0 - guardian_resentment * 0.7) * familiarity_modifier
where familiarity_modifier = min(1.0, guardian_familiarity * 1.2 + 0.2)
```

If Disposition is unavailable, compliance defaults to the configured `DefaultComplianceBase` (0.5).

### How It Works

```
1. Game designers register domains, modules, and influence types via API
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

## Dependencies

### Hard Dependencies (L0, L1, L2)

| Service | Layer | Usage |
|---------|-------|-------|
| lib-state | L0 | MySQL for definitions (domains, modules, influences), Redis for cached manifests |
| lib-messaging | L0 | Event publishing (manifest.updated, influence.executed/resisted) and subscription (seed events) |
| lib-mesh | L0 | Service-to-service calls (Seed, Disposition) |
| lib-seed | L2 | Read capability manifests, subscribe to capability/growth events |

### Soft Dependencies (L3, L4)

| Service | Layer | Usage | If Missing |
|---------|-------|-------|------------|
| lib-disposition | L4 | Guardian feelings (trust, resentment, familiarity) for compliance computation | Compliance defaults to `DefaultComplianceBase` (0.5) |
| lib-gardener | L4 | Garden context for manifest routing and influence execution | Manifests computed but not routed to gardens; influences not executable |

### DI Provider Registrations

| Interface | Direction | Purpose |
|-----------|-----------|---------|
| `IVariableProviderFactory` | Agency -> Actor (L2) | Provides `${spirit.*}` namespace for ABML behavior evaluation |

---

## Dependents

| Dependent | Relationship |
|-----------|-------------|
| lib-gardener (L4) | Calls `/agency/manifest/get` and `/agency/influence/execute` for player experience routing and spirit influence validation |
| lib-actor (L2) | Consumes `${spirit.*}` variables via `IVariableProviderFactory` for ABML behavior evaluation in character cognition pipelines |
| lib-disposition (L4) | Receives guardian feeling updates triggered by influence outcomes (compliance increases trust, forced overrides increase resentment) |

---

## State Storage

**Status**: Pre-implementation. No state stores defined in `schemas/state-stores.yaml` yet.

| Store Name | Backend | Contents |
|------------|---------|----------|
| `agency-domains` | MySQL | Domain definitions (code, display name, seed path mappings) |
| `agency-modules` | MySQL | Module definitions (code, domain, threshold, fidelity curve, sort order) |
| `agency-influences` | MySQL | Influence type definitions (code, domain, threshold, perception type, compliance factors) |
| `agency-manifests` | Redis | Cached computed manifests per seed (JSON, TTL-based) |
| `agency-manifest-history` | MySQL | Manifest change log (seedId, timestamp, delta, previous/new module states) |
| `agency-seed-config` | Redis | Guardian seed type configuration per game service |

---

## Events

### Published Events

| Topic | Event Model | Description |
|-------|-------------|-------------|
| `agency.manifest.updated` | `AgencyManifestUpdatedEvent` | Manifest changed for a seed. Includes seedId, changed modules (enabled/disabled, fidelity changes), changed influences. Consumed by Gardener for client push. |
| `agency.influence.executed` | `AgencyInfluenceExecutedEvent` | Spirit influence validated and enriched. Includes seedId, influenceCode, perceptionType, complianceFactors. Consumed by Gardener god-actor for modulation. |
| `agency.influence.resisted` | `AgencyInfluenceResistedEvent` | Character resisted spirit influence. Includes seedId, characterId, influenceCode, complianceBase, resistanceReason. Published by Actor's ABML evaluation, relayed by Agency. |
| `agency.influence.rejected` | `AgencyInfluenceRejectedEvent` | Influence not available in current manifest or rate-limited. Includes seedId, influenceCode, rejectionReason. |
| `agency.domain.registered` | `AgencyDomainRegisteredEvent` | New UX domain registered. |
| `agency.module.registered` | `AgencyModuleRegisteredEvent` | New UX module registered. Triggers manifest recomputation for all seeds with capabilities in this domain. |

### Consumed Events

| Topic | Event Model | Reaction |
|-------|-------------|----------|
| `seed.capability.updated` | `SeedCapabilityUpdatedEvent` | Recompute manifest for the affected seed. Debounced by `ManifestRecomputeDebounceMs`. |
| `seed.growth.recorded` | `SeedGrowthRecordedEvent` | Check if any capability depth thresholds were crossed. If so, trigger manifest recomputation. |
| `connect.session.disconnected` | `SessionDisconnectedEvent` | Optional: clear cached manifest for the disconnected seed (prevents stale cache). |

---

## Configuration

**Status**: Pre-implementation. No configuration schema defined yet.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ManifestCacheTtlMinutes` | int | 30 | TTL for cached manifests in Redis |
| `ManifestRecomputeDebounceMs` | int | 500 | Debounce interval for recomputation on rapid seed growth events |
| `DefaultComplianceBase` | float | 0.5 | Default compliance when Disposition is unavailable |
| `MaxModulesPerDomain` | int | 50 | Maximum modules per domain |
| `MaxInfluencesPerDomain` | int | 30 | Maximum influence types per domain |
| `InfluenceRateLimitPerSecond` | int | 5 | Max spirit influences per second per seed |
| `FidelityLevels` | int | 5 | Number of discrete fidelity levels (1-N) |
| `PushManifestOnCapabilityChange` | bool | true | Auto-push manifest updates when seed capabilities change |
| `SeedTypeCode` | string | `guardian` | Default seed type code for guardian spirits |
| `ComplianceResistanceDecayRate` | float | 0.1 | How quickly resistance-from-overuse decays per game-hour |
| `ManifestHistoryRetentionDays` | int | 30 | How long to retain manifest change history |
| `CrossSeedPollinationEnabled` | bool | true | Whether manifest draws capabilities from all account seeds |
| `CrossSeedPollinationFactor` | float | 0.3 | Depth multiplier for cross-pollinated seed capabilities |

---

## DI Services & Helpers

**Status**: Pre-implementation. Planned DI dependencies:

| Service | Role |
|---------|------|
| `ILogger<AgencyService>` | Structured logging |
| `AgencyServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (MySQL definitions, Redis manifests) |
| `IMessageBus` | Event publishing and subscription |
| `ISeedClient` | Read capability manifests from Seed (L2) |
| `IServiceProvider` | Runtime resolution of soft dependencies (Disposition, Gardener) |
| `SpiritProviderFactory` | `IVariableProviderFactory` implementation providing `${spirit.*}` namespace |

---

## API Endpoints

### Domain Management (4 endpoints)

| Endpoint | Description |
|----------|-------------|
| POST /agency/domain/register | Register a UX domain with seed domain path mappings |
| POST /agency/domain/get | Get domain definition by code |
| POST /agency/domain/list | List all registered domains with module/influence counts |
| POST /agency/domain/delete | Remove a domain (cascades to modules and influences) |

### Module Management (5 endpoints)

| Endpoint | Description |
|----------|-------------|
| POST /agency/module/register | Register a UX module definition with fidelity curve |
| POST /agency/module/update | Update module definition (threshold, fidelity curve, sort order) |
| POST /agency/module/get | Get module definition by code |
| POST /agency/module/list | List modules, filterable by domain code |
| POST /agency/module/delete | Remove a module definition |

### Influence Management (5 endpoints)

| Endpoint | Description |
|----------|-------------|
| POST /agency/influence/register | Register an influence type with compliance factors |
| POST /agency/influence/update | Update influence definition |
| POST /agency/influence/get | Get influence definition by code |
| POST /agency/influence/list | List influences, filterable by domain code |
| POST /agency/influence/delete | Remove an influence type |

### Manifest Operations (4 endpoints)

| Endpoint | Description |
|----------|-------------|
| POST /agency/manifest/get | Get current UX manifest for a seed |
| POST /agency/manifest/recompute | Force manifest recomputation (after bulk registration changes) |
| POST /agency/manifest/diff | Compare two manifests and return delta (for change notifications) |
| POST /agency/manifest/history | Get manifest change history for a seed (paged) |

### Spirit Influence Operations (2 endpoints)

| Endpoint | Description |
|----------|-------------|
| POST /agency/influence/evaluate | Evaluate if an influence is available and estimate acceptance likelihood |
| POST /agency/influence/execute | Execute a spirit influence: validate against manifest, enrich with compliance factors, return perception payload for Actor injection |

### Seed Configuration (2 endpoints)

| Endpoint | Description |
|----------|-------------|
| POST /agency/seed-config/set | Set the seed type code used for guardian spirits (per game service) |
| POST /agency/seed-config/get | Get the current guardian seed type configuration |

**Total: 22 endpoints**

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
| `spirit.compliance_base` | float | Disposition | Base willingness to accept influence. Formula: `trust * (1 - resentment * 0.7) * familiarity_modifier` |
| `spirit.resentment` | float | Disposition | Current resentment toward the guardian spirit (0.0-1.0) |
| `spirit.trust` | float | Disposition | Current trust toward the guardian spirit (0.0-1.0) |
| `spirit.familiarity` | float | Disposition | How well the character knows the spirit (0.0-1.0) |
| `spirit.domain.<domain>.fidelity` | float | Manifest | Spirit's overall fidelity in a domain (0.0-1.0), computed as average module fidelity |
| `spirit.domain.<domain>.depth` | float | Seed | Raw seed depth for the domain's primary capability path |
| `spirit.influence.last_type` | string | Influence history | Code of the most recent influence attempt |
| `spirit.influence.last_accepted` | bool | Influence history | Whether the most recent influence was accepted by the character |
| `spirit.influence.frequency` | float | Rolling window | Influences per minute over a rolling 5-minute window |
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
    - publish: agency.influence.resisted
    # Resistance may increase resentment (via Disposition)
    - call: /disposition/feeling/record
      with: { target: "guardian", axis: "resentment", delta: 0.02 }
```

---

## Background Workers

### ManifestRecomputeWorker

**Trigger**: `seed.capability.updated` and `seed.growth.recorded` events, debounced by `ManifestRecomputeDebounceMs`.

**Process**:
1. Receive seed capability change event
2. Check debounce timer -- if another event for this seed arrived within the debounce window, reset timer
3. After debounce settles, fetch seed's full capability manifest from Seed service
4. If `CrossSeedPollinationEnabled`, also fetch capabilities from other seeds owned by the same account (apply `CrossSeedPollinationFactor` multiplier to cross-seed depths)
5. For each registered module, evaluate whether depth meets threshold and compute fidelity level
6. For each registered influence, evaluate whether depth meets threshold
7. Compare with cached manifest
8. If changed: update Redis cache, write to history store, publish `agency.manifest.updated`

---

## Implementation Plan

### Phase 1: Core Registry (6 endpoints)
- Create schemas for domains, modules, influences
- Generate code
- Implement domain CRUD (register, get, list, delete)
- Implement module register and list
- Add state stores (agency-domains, agency-modules, agency-influences)

### Phase 2: Manifest Engine (4 endpoints)
- Implement manifest computation from seed capabilities
- Add Redis caching (agency-manifests)
- Implement manifest get, recompute, diff, history
- Subscribe to seed.capability.updated events
- Implement ManifestRecomputeWorker with debouncing

### Phase 3: Influence System (7 endpoints)
- Implement influence register, update, get, list, delete
- Implement influence evaluate and execute
- Add rate limiting via Redis counters
- Publish influence execution/rejection/resistance events

### Phase 4: Variable Provider Factory
- Implement SpiritProviderFactory and SpiritProvider
- Register ${spirit.*} namespace
- Integrate Disposition for compliance computation
- Implement influence history tracking in Redis

### Phase 5: Integration
- Integrate with Gardener for manifest push routing
- Integrate with Entity Session Registry when available
- Seed from configuration support
- Cross-seed pollination computation
- Manifest history retention worker

### Prerequisites
- lib-seed (L2): Must be running for capability manifest reads
- lib-disposition (L4): Should be running for compliance computation (soft dependency)
- lib-gardener (L4): Should be running for manifest push and influence execution routing
- Entity Session Registry ([Connect Issue #426](https://github.com/beyond-immersion/bannou-service/issues/426)): Needed for session-aware manifest push

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(None -- pre-implementation)*

### Intentional Quirks (Documented Behavior)

1. **Fidelity levels are integers, not floats.** The client receives fidelity 1-5, not a continuous value. This is intentional -- UX modules should have discrete behavior modes (e.g., "show 3 stance options" vs "show 5 stance options"), not infinitely variable rendering. The continuous seed fidelity (0.0-1.0) is quantized into discrete module fidelity levels.

2. **Compliance base is computed by Agency, not by ABML.** The formula for `${spirit.compliance_base}` is in Agency's variable provider, not in the behavior document. This means the compliance formula is service code, not authored content. This is deliberate: the base compliance is a system-level mechanic that should be consistent across all characters. ABML behaviors can modify the effective compliance via additional checks (personality alignment, resistance buildup, situational modifiers), but the base value is authoritative.

3. **Influence execution does not directly call Actor.** Agency validates and enriches the influence, then returns the payload. Gardener (or the gardener god-actor, per BEHAVIORAL-BOOTSTRAP.md) is responsible for injecting the perception into Actor. This separation allows god-actors to modulate or intercept influences before they reach the character.

4. **Cross-seed pollination is multiplicative, not additive.** A spirit with combat depth 10.0 on Seed 1 gets `10.0 * 0.3 = 3.0` combat depth contribution to Seed 2's manifest computation. This means cross-pollination provides a head start, not full capability transfer. The spirit still needs to develop domain depth on each seed for high-fidelity modules.

5. **Rate limiting is per-seed, not per-session.** `InfluenceRateLimitPerSecond` applies to the seed (guardian spirit), not the WebSocket session. If a player reconnects rapidly, the rate limit persists because it's tracked against the seed ID in Redis, not the session.

### Design Considerations (Requires Planning)

1. **Manifest push mechanism.** Agency publishes `agency.manifest.updated` as an event. Gardener subscribes and routes the manifest to the correct client session via the Entity Session Registry ([Connect Issue #426](https://github.com/beyond-immersion/bannou-service/issues/426)). This keeps Agency session-unaware. Alternative: Agency pushes directly via Connect's per-session RabbitMQ queue, but this couples Agency to session infrastructure. Recommendation: event-based routing via Gardener.

2. **Influence execution path.** The current design has Agency validate, Gardener god-actor modulate, and Actor inject. This is three hops. Alternative: Agency injects directly into Actor, bypassing the god-actor. This is faster but prevents gods from intercepting spirit nudges. The god-modulation path is architecturally important for the vision (gods curate player experience), so the three-hop path is correct despite latency.

3. **Module definition ownership.** Options: (a) game designers register via admin API, (b) services self-register on startup (like Permission's service registration), (c) seed from configuration. Recommendation: seed from configuration with admin API for runtime additions. Module definitions are game design decisions, not service infrastructure.

4. **Fidelity curve format.** The fidelity curve is a float array of depth thresholds for levels 1-N. Example: `[2.0, 3.5, 5.0, 7.0, 9.0]` means depth 2.0=level 1, depth 3.5=level 2, etc. Per-module arrays provide maximum flexibility. Alternative: a formula (linear, logarithmic). Arrays are simpler and more explicit.

5. **Cross-seed capability aggregation.** PLAYER-VISION says "accumulated experience crosses seed boundaries." The `CrossSeedPollinationFactor` (default 0.3) determines how much cross-seed depth contributes. This should be tunable per game service -- some games may want 0.0 (no cross-pollination), others may want 0.5 (significant transfer). Stored in `agency-seed-config` per game service.

6. **Compliance computation vs. raw data.** Agency computes `compliance_base` from Disposition's raw guardian feelings. Alternative: expose raw feelings and let ABML compute compliance. Tradeoff: Agency-computed gives a consistent base; ABML-computed gives full author control. Current design: Agency provides the base, ABML can modify via additional conditions. Both approaches have merit.

7. **Entity Session Registry dependency.** Manifest push requires mapping seed -> account -> active session. Without the Entity Session Registry ([Connect Issue #426](https://github.com/beyond-immersion/bannou-service/issues/426)), Agency needs Gardener to maintain session-to-seed mappings. This is acceptable for initial implementation but the Entity Session Registry is the long-term solution.

8. **Influence persistence.** Should influence history (last_type, last_accepted, frequency) be persisted in Redis or held in-memory by the variable provider? Redis survives Actor restarts. In-memory is faster. Recommendation: Redis with a simple counter/last-value pattern (not full history -- that's in `agency-manifest-history`).

9. **Realm-specific module sets.** Different realms may expose different UX modules (Omega cyberpunk has hacking modules, Arcadia has magic modules). GameServiceId on module definitions scopes modules to specific games. Realm-specific scoping would require a RealmId field. Deferred -- game-service scoping is sufficient for now.

---

## Work Tracking

*(No issues yet -- pre-implementation)*

---

*This document describes a planned L4 GameFeatures service. For the behavioral bootstrap pattern that Agency interacts with, see [BEHAVIORAL-BOOTSTRAP.md](../guides/BEHAVIORAL-BOOTSTRAP.md). For the guardian seed that Agency reads capabilities from, see [SEED.md](SEED.md). For the disposition feelings that feed compliance computation, see [DISPOSITION.md](DISPOSITION.md).*
