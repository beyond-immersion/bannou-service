# Faction Plugin Deep Dive

> **Plugin**: lib-faction
> **Schema**: schemas/faction-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Store**: faction-statestore (MySQL), faction-membership-statestore (MySQL), faction-territory-statestore (MySQL), faction-norm-statestore (MySQL), faction-cache (Redis), faction-lock (Redis)
> **Guide**: [Morality System](../guides/MORALITY-SYSTEM.md) (cross-service integration with lib-obligation and lib-faction)
> **Implementation Map**: [docs/maps/FACTION.md](../maps/FACTION.md)
> **Short**: Seed-based faction growth with norms, enforcement tiers, territory, guild hierarchy, and political bonds

## Overview

The Faction service (L4 GameFeatures) models factions as seed-based living entities whose capabilities emerge from growth, not static assignment. As a faction's seed grows through phases (nascent, established, influential, dominant), capabilities unlock: norm definition, enforcement tiers, territory claiming, and trade regulation. Its primary consumer is lib-obligation, which queries faction norms to produce GOAP action cost modifiers for NPC cognition -- resolving a hierarchy of guild, location, and realm baseline norms into a merged norm set. Supports guild memberships with role hierarchy, parent/child organizational structure, territory claims, and inter-faction political connections modeled as seed bonds via lib-seed. Internal-only, never internet-facing.

---

## Key Concepts

**Norm Resolution Hierarchy** (most specific wins):
1. Guild faction norms (character's direct memberships)
2. Location faction norms (controlling faction at character's current location)
3. Realm baseline faction norms (realm-wide cultural context)

**Faction Types**:
- **Realm baseline faction**: provides realm-wide cultural norms (honor codes, taboos)
- **Location controlling faction**: provides local norms (lawless district, temple sanctity)
- **Guild factions**: character memberships with role hierarchy (Leader, Officer, Member, Recruit)
- **Parent/child hierarchy**: organizational structure with configurable max depth

**Political Connections**: Inter-faction political relationships (alliances, rivalries, treaties) are modeled as seed bonds via lib-seed's existing bond API, NOT through lib-relationship. A bond between two faction seeds represents the alliance/rivalry as a growable entity with its own capability manifest. Joint member activities grow the bonded seed, unlocking alliance capabilities.

**violationType as Opaque String**: Norm violation types (e.g., "theft", "deception", "violence") are opaque strings, not enums. The vocabulary is defined by contract templates and action tag mappings in lib-obligation; lib-faction stores whatever violation type strings callers provide. Adding new violation types never requires a schema change.

---

## Factions as Emergent Governance (Architectural Target)

> **Status**: 31 of 37 endpoints are fully implemented (CRUD, membership, territory, norms, cleanup, compression). 6 governance endpoints are schema-defined but implementation is pending. The seed-based growth pipeline and norm resolution hierarchy work end-to-end. The broader vision described below -- emergent governance, economic regulation, and the morality pipeline role -- is the architectural target that these mechanics serve.

### Factions Are the Emergent Governance Layer of the Living World

The vision is not "factions with stats" but emergent political systems that arise organically from NPC activity. NPCs trade → faction grows its commerce seed domain → faction unlocks trade regulation capabilities → NPCs within that faction's territory experience different economic norms → the economy shifts. The seed growth system is the delivery mechanism: governance power is EARNED through member activity, not statically assigned by a designer. A nascent thieves' guild literally cannot define "honor among thieves" as an enforceable norm because it hasn't grown enough governance capability yet. A sovereign merchant guild can regulate pricing, enforce trade agreements, and claim territory. This mirrors real-world governance where authority comes from established legitimacy, not from declaration.

### Faction Is the "Social Landscape" Layer of the Morality Pipeline

Faction norms define what behaviors are costly WHERE and for WHOM. This feeds into Obligation (the "cost landscape") which computes personality-weighted costs, which feeds into Actor cognition where costs become GOAP action cost modifiers. Without Faction, the morality system is purely contractual -- only explicit agreements (guild charters, trade deals) create obligations. WITH Faction, ambient cultural and social norms become enforceable: a realm-wide honor code, a temple district's sanctity rules, a merchant quarter's trade regulations all modify NPC behavior without requiring individual contracts. See [Morality System guide](../guides/MORALITY-SYSTEM.md) for the full pipeline architecture.

### Factions Are Economic Actors in a Living Economy

The economy must be NPC-driven, not player-driven. Supply, demand, pricing, and trade routes emerge from NPC behavior -- what they need, what they produce, what they aspire to. Factions are how economic regulation emerges: trade regulation capabilities unlocked through faction growth, tariff systems, market influence, divine economic intervention (e.g., a Commerce regional watcher boosting trade) that works THROUGH faction mechanisms. Player economies layer on top of this NPC economic substrate. If the economy is just player-to-player, the world feels dead when players are offline.

### Political Connections Are Growable Entities, Not Boolean Flags

Alliances between factions are modeled as seed bonds via lib-seed's bond API. A bond between two faction seeds represents the alliance/rivalry as a growable entity with its own capability manifest and phase progression. Joint member activities (trade between allied factions, military cooperation) grow the bonded seed, unlocking alliance capabilities: joint territory claims, mutual defense pacts, trade agreements, shared governance. This means political relationships are living things that strengthen or atrophy based on actual inter-faction activity, not static diplomatic flags.

### The Emergent Narrative of Faction Evolution

Over simulated time, factions should undergo recognizable arcs. A street gang grows through member activities → becomes an established criminal organization with enforceable codes → claims territory → regulates the underground economy. A merchant guild forms around a marketplace → trade activity grows its commerce seed → it unlocks pricing regulation → eventually competes with the ruling faction for political influence. These arcs are not scripted; they emerge from the seed growth system, the Collection-to-Seed pipeline, and capability-gated operations. The game designer's role is defining seed types, growth mappings, and capability thresholds -- the specific arcs that emerge are unique to each world's simulated history.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-obligation (L4) | **Planned** primary consumer: will query `/faction/norm/query-applicable` to resolve merged norm sets for NPC cognition cost modifiers. Not yet wired up -- lib-obligation currently resolves faction context through contracts only. See Design Considerations #4-5. |

## Variable Provider: `${faction.*}` Namespace

Implements `IVariableProviderFactory` (via `FactionProviderFactory`) providing the following variables to Actor (L2) via the Variable Provider Factory pattern for ABML behavior expressions. Loads the character's faction memberships from the membership list store, enriches with faction details, and resolves membership-scoped norms (highest penalty per violation type across all membership factions).

**Aggregate variables:**

| Variable | Type | Description |
|----------|------|-------------|
| `${faction.count}` | int | Number of factions the character belongs to |
| `${faction.names}` | List&lt;string&gt; | Names of all factions |
| `${faction.codes}` | List&lt;string&gt; | Codes of all factions |
| `${faction.primary_faction}` | string? | Code of the highest-role faction (Leader > Officer > Member) |
| `${faction.norm_count}` | int | Total unique violation types with norms across all membership factions |

**Norm variables** (accessed by violation type code, case-insensitive):

| Variable | Type | Description |
|----------|------|-------------|
| `${faction.has_norm.TYPE}` | bool | Whether any membership faction defines a norm for this violation type |
| `${faction.norm_penalty.TYPE}` | float | Highest penalty across all membership factions for this violation type (0 if none) |

**Per-faction variables** (accessed by faction code, case-insensitive):

| Variable | Type | Description |
|----------|------|-------------|
| `${faction.CODE.name}` | string | Faction display name |
| `${faction.CODE.status}` | string | Faction operational status (e.g., "Active", "Dissolved") |
| `${faction.CODE.phase}` | string? | Current seed growth phase |
| `${faction.CODE.is_realm_baseline}` | bool | Whether this is the realm baseline faction |
| `${faction.CODE.member_count}` | int | Total member count |
| `${faction.CODE.role}` | string | Character's role in this faction (e.g., "Leader", "Member") |

Returns `FactionProvider.Empty` for non-character actors or characters with no faction memberships.

**Remaining Gap: Territory Context Variable**

| Planned Variable | Status | Impact |
|-----------------|--------|--------|
| `${faction.in_controlled_territory}` | **Not yet implemented** | ABML cannot check territory control context |

The territory variable requires knowing the character's current location, which is not available through the `IVariableProviderFactory.CreateAsync(Guid? entityId)` interface. Implementing this requires either: (a) subscribing to `location.entity.arrived`/`location.entity.departed` events to maintain a character-location cache in Faction's own state stores, or (b) enhancing the provider factory interface to accept additional execution context. The membership-scoped norm variables above are sufficient for lib-obligation's `evaluate_consequences` cognition stage; territory-scoped norms are resolved by the full `QueryApplicableNorms` API endpoint which accepts a `locationId` parameter.

## Visual Aid

```
┌──────────────────────────────────────────────────────────────────────┐
│ Norm Resolution Hierarchy │
├──────────────────────────────────────────────────────────────────────┤
│ │
│ Character "Kael" at Location "Docks District" │
│ │
│ 1. Guild Factions (direct memberships): │
│ ┌─────────────────────────────┐ │
│ │ Merchant Guild (member) │ → theft: 15, deception: 10 │
│ │ Dockworkers Union (recruit) │ → violence: 5 │
│ └─────────────────────────────┘ │
│ │
│ 2. Location Controlling Faction: │
│ ┌─────────────────────────────┐ │
│ │ Harbor Authority │ → contraband: 12, trespass: 8 │
│ │ (controls Docks District) │ → theft: 10 (overridden by #1) │
│ └─────────────────────────────┘ │
│ │
│ 3. Realm Baseline Faction: │
│ ┌─────────────────────────────┐ │
│ │ Arcadian Cultural Council │ → disrespect: 5, violence: 3 │
│ │ (realm baseline) │ → theft: 7 (overridden by #1) │
│ └─────────────────────────────┘ violence: 3 (overridden by #1)│
│ │
│ Merged Norm Map (most specific wins): │
│ ┌─────────────────────────────────────────────────┐ │
│ │ theft: 15 (Merchant Guild - membership) │ │
│ │ deception: 10 (Merchant Guild - membership) │ │
│ │ violence: 5 (Dockworkers Union - membership)│ │
│ │ contraband: 12 (Harbor Authority - territory) │ │
│ │ trespass: 8 (Harbor Authority - territory) │ │
│ │ disrespect: 5 (Cultural Council - baseline) │ │
│ └─────────────────────────────────────────────────┘ │
│ │
│ → Passed to lib-obligation for personality-weighted cost modifiers │
│ → Fed into GOAP planner as dynamic action cost adjustments │
└──────────────────────────────────────────────────────────────────────┘
```

```
┌──────────────────────────────────────────────────────────────────────┐
│ Collection→Seed Growth Pipeline │
├──────────────────────────────────────────────────────────────────────┤
│ │
│ Member NPC completes trade │
│ │ │
│ ▼ │
│ Collection entry unlocked ("faction-deeds", tag: "commerce:trade") │
│ │ │
│ ├──► Member's personal seed growth (existing pipeline) │
│ │ │
│ └──► ICollectionUnlockListener (lib-faction) │
│ Tag prefix "commerce" matches faction seed mapping │
│ │ │
│ ▼ │
│ lib-seed API: RecordGrowth(factionSeedId, "commerce", 1.5)│
│ │ │
│ ▼ │
│ ISeedEvolutionListener fires: │
│ - Phase changed: nascent → established │
│ - Capability unlocked: "norm.define" │
│ │ │
│ ▼ │
│ Faction can now define enforceable norms │
└──────────────────────────────────────────────────────────────────────┘
```

## Stubs & Unimplemented Features

### Governance Endpoints (6 endpoints — schema defined, implementation pending)

Sovereignty and governance data management. Schema added by #601 (2026-03-16). Service code implementation is pending.

- **SetGovernanceEntry** (`/faction/governance/set`): Upsert governance entry (domain + template code + parameters). Requires Sovereign/Delegated authority and `governance.arbitrate.*` seed capability.
- **RemoveGovernanceEntry** (`/faction/governance/remove`): Remove governance entry for a domain.
- **ListGovernanceEntries** (`/faction/governance/list`): All governance entries for a faction.
- **QueryGovernanceData** (`/faction/governance/query`): Critical arbitration integration endpoint. Walks sovereignty hierarchy to resolve jurisdictional faction and procedural template for a location + domain. Returns 404 when no sovereign has jurisdiction.
- **DelegateAuthority** (`/faction/governance/delegate`): Sovereign grants per-domain delegated authority to a child faction.
- **RevokeAuthority** (`/faction/governance/revoke`): Revoke delegation (reverts to Influence when all domains revoked).

### Category A Merge Endpoint (pending)

Faction is a Category A entity per Implementation Tenets (deprecation lifecycle). `FactionService` already implements the `IDeprecateAndMergeEntity` marker interface, but the merge endpoint logic (`MergeFactionAsync`) is not yet implemented. When implemented, the merge endpoint should use shared `MergeDeprecatedRequest`/`MergeDeprecatedResponse` models from `common-api.yaml` and migrate faction references (memberships, territory claims, norms, governance entries) from a deprecated source faction to a target faction, then optionally delete the source.

## Potential Extensions

1. **Faction diplomacy system**: Formalized alliance/rivalry mechanics through seed bonds with capability-gated treaty operations.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/413 -->
2. **Faction economy**: Trade regulation capabilities unlocked at seed growth thresholds, integrating with lib-currency for tariffs and trade agreements.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/414 -->
3. **Faction reputation system**: Per-character standing within a faction affecting norm enforcement intensity and available roles.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/416 -->
4. **Client events for real-time faction notifications**: Define `faction-client-events.yaml` to push membership changes, territory shifts, and norm updates to connected WebSocket clients via `IClientEventPublisher`.
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/418 -->
5. **Faction governance elections**: Member voting for leadership positions using Contract-backed consent flows.
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/420 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Cascading delete does not publish individual sub-entity events**: `DeleteFactionAsync` cascades norm, territory, and governance deletions by directly deleting from stores without publishing individual `faction.norm.deleted`, `faction.territory.released`, or `faction.governance.deleted` events for each removed sub-entity. Per Foundation Tenets (Event-Driven Architecture), all meaningful state changes MUST publish events even without current consumers. Each norm deletion, territory release, and governance entry removal is a meaningful state change. The fix is to publish the appropriate events during cascade iteration, similar to how `RemoveMemberInternalAsync` already publishes `faction.member.removed` for each cascaded member removal.

2. **CleanupByRealm fails for non-deprecated factions**: The `CleanupByRealm` endpoint calls `DeleteFactionAsync` internally, which rejects deletion with `400 BadRequest` when `IsDeprecated == false` (Category A lifecycle guard). However, cleanup callbacks from lib-resource fire when the parent realm is deleted — factions in that realm are typically not deprecated first. This means realm cleanup silently fails for every non-deprecated faction, leaving orphaned faction data. The cleanup endpoint must bypass the deprecation guard, or the internal cascade path must use a force-delete that skips the deprecation check.

### Intentional Quirks (Documented Behavior)

1. **violationType is an opaque string**: Not an enum. Follows the same pattern as Collection's `collectionType` and Seed's `seedTypeCode`. The vocabulary is externally defined by lib-obligation's violation type taxonomy and ABML action tags. lib-faction stores whatever strings callers provide.

2. **Norm cache invalidation is write-triggered**: Cache entries for norm resolution are invalidated on write operations (member add/remove, norm define/update/delete, territory claim/release, baseline designation). Between writes, stale data may be served until TTL expires. lib-obligation can use `forceRefresh` to bypass cache when it detects staleness via contract lifecycle events.

3. **Seed capability gating is runtime-checked**: Norm definition requires `norm.define` capability, territory claiming requires `territory.claim` capability. These are checked at request time by querying the faction's seed capability manifest via lib-seed. A nascent faction's requests will be rejected until sufficient growth is achieved.

4. **Realm baseline is exclusive**: Only one faction per realm can be the baseline cultural faction. Designating a new baseline replaces the previous one and publishes an event with the previous baseline ID.

5. **Territory claims are exclusive per location**: One controlling faction per location. Claiming a location that is already controlled returns a conflict. The `Contested` status exists in the schema for future dispute mechanics but is not currently used.

6. **Dual-key storage pattern**: Factions are saved under both a primary key (by ID) and a lookup key (by game service + code), following the established Collection/Seed pattern.

7. **No event subscriptions -- DI listeners only**: Cross-service integration uses `ISeedEvolutionListener` and `ICollectionUnlockListener` DI provider patterns (per FOUNDATION TENETS,) instead of broadcast event subscriptions. Resource cleanup uses `x-references` callbacks (per FOUNDATION TENETS,), not `character.deleted` / `realm.deleted` event subscriptions.

8. **SeedFactions allows setting IsRealmBaseline directly**: Unlike `CreateFactionAsync` (which always sets `IsRealmBaseline = false`), the `SeedFactionsAsync` endpoint passes through `def.IsRealmBaseline` from the seed definition, allowing baseline designation during bulk seeding without a separate `DesignateRealmBaseline` call.

10. **Sovereignty is emergent, not required**: `authorityLevel` defaults to `Influence` on every faction. Sovereignty is only acquired through `DesignateRealmBaseline` (which sets Sovereign automatically) or delegation endpoints. If no sovereign exists in a territory, `QueryGovernanceData` returns 404 and `QueryApplicableNorms` continues with the existing "most specific wins" behavior where all norms are social/personal. The legal channel only activates when a Sovereign faction exists.

11. **authorityLevel is not directly mutable via UpdateFaction**: Sovereignty is managed exclusively through `DesignateRealmBaseline` (→ Sovereign), `DelegateAuthority` (→ Delegated), and `RevokeAuthority` (→ Influence). This prevents accidental sovereignty assignment via the general update endpoint.

### Design Considerations (Requires Planning)

1. **No owner validation for territory claims**: Like Collection/Seed, faction trusts that callers pass valid entity IDs. Location existence is validated via lib-location, but no check that the faction "should" be able to claim that location beyond seed capability gating.
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/424 -->

2. **Norm query performance at scale**: `QueryApplicableNorms` performs up to 3 aggregation passes (guild factions, location faction, realm baseline). With many memberships or large norm sets, this could become expensive. The Redis cache (TTL-based) mitigates reads but cold-start queries for characters with many memberships need profiling.

3. **Seed bond mechanics for alliances**: The schema description references seed bonds for inter-faction alliances, but no API endpoints exist for bond management. These would be managed directly through lib-seed's bond API. May need faction-level wrapper endpoints for ergonomic alliance management.

4. **Variable Provider missing territory context variable**: The Variable Provider section documents `${faction.in_controlled_territory}` as not yet implemented. This requires knowing the character's current location, which is not available through the `IVariableProviderFactory.CreateAsync(Guid? entityId)` interface. Implementing this requires either: (a) subscribing to location events to maintain a character-location cache, or (b) enhancing the provider factory interface to accept additional execution context. The membership-scoped norm variables (`has_norm`, `norm_penalty`) and aggregate/per-faction variables are documented as implemented. The territory-scoped norm resolution remains available through the full `QueryApplicableNorms` API endpoint.

5. **Missing lib-contract integration for guild charters (plan gap)**: Issue #410 decision Q3 states: "When a character joins a faction (formal guild membership), the guild contract is created explicitly through lib-contract." The plan lists `lib-contract (L1) — formal membership agreements, guild charters` as a dependency. The current implementation does not use `IContractClient` at all -- membership is managed directly without contract backing. This means guild charters are not formalized as binding agreements, and lib-obligation cannot discover faction-sourced contractual obligations through lib-contract.

## Work Tracking

### Active
- **Governance implementation**: 6 governance endpoints are schema-defined (#601) but service code is pending. See Stubs § Governance Endpoints.
- **Category A merge endpoint**: `IDeprecateAndMergeEntity` interface is implemented but `MergeFactionAsync` logic is pending. See Stubs § Category A Merge Endpoint.

### History
- **2026-03-16**: Maintenance pass — corrected DC #4 (variable provider scope), deleted confirmed-FIXED DC #6 (governance schema designed via #601), corrected Category A merge stub (interface already implemented).
- **2026-03-16**: Issue #601 — Faction sovereignty schema designed. Design Consideration #6 resolved.
- **Origin**: [#410 - Feature: Second Thoughts -- Prospective Consequence Evaluation for NPC Cognition](https://github.com/beyond-immersion/bannou-service/issues/410) — lib-faction extracted from original lib-moral proposal during architecture review. Part of the larger lib-obligation + lib-faction morality pipeline. All 37 endpoints (31 original + 6 governance) are now schema-defined; 31 are fully implemented.
