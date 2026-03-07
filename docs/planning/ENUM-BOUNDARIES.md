# Enum Boundary Analysis & Remediation Plan

> **Status**: Investigation Complete — Ready for Execution
> **Created**: 2026-03-07
> **Updated**: 2026-03-07 (all open questions resolved via targeted codebase investigation)
> **Scope**: All enum-to-enum mappings, string-to-enum parsing, and duplicate enum definitions across the codebase

---

## Table of Contents

1. [Problem Statement](#problem-statement)
2. [Governing Rules](#governing-rules)
3. [Enum Sharing Infrastructure](#enum-sharing-infrastructure)
4. [OpenAPI Spec Research: Enum Subsets/Supersets](#openapi-spec-research-enum-subsetsupersets)
5. [Boundary Classification Framework](#boundary-classification-framework)
6. [Full Inventory](#full-inventory)
7. [Remediation Plan](#remediation-plan)
8. [Task Summary](#task-summary)
9. [Resolved Questions](#resolved-questions)
10. [Investigation Details](#investigation-detail-orchestrator-containerstatus-resolved)
11. [Broader Audit Sweep Results](#broader-audit-sweep-results)
12. [Forward-Looking Rules](#forward-looking-rules)

---

## Problem Statement

A codebase-wide audit of mapping methods (enum-to-enum conversions, `Enum.Parse`/`Enum.TryParse` calls, and string-to-enum coercions) found 27 instances across 20+ files. Some are legitimate boundary crossings (third-party SDK, external data, infrastructure abstraction); others are symptoms of duplicate enum definitions, missing `$ref` reuse, or schema fields typed as `string` where an enum should be.

The goal is to classify each instance, determine the correct fix (or confirm it's acceptable), and produce a remediation task list.

---

## Governing Rules

These are the tenet and schema rules that define what's correct. Fixes must comply with all of them.

### T25: Type Safety Across All Models (IMPLEMENTATION-DATA.md)

> ALL models (requests, responses, events, configuration, internal POCOs) MUST use the strongest available C# type. String representations of typed values are **forbidden**.

**Key points**:
- No `Enum.Parse` in business logic — if you're parsing enums, your model definition is wrong
- Internal POCOs must mirror types from generated models (enum to enum, not enum to string)
- Exception: Intentionally generic services (hierarchy isolation) — lower-layer services that must NOT enumerate higher-layer types use opaque strings. But this applies ONLY when a lower-layer service would enumerate types from HIGHER layers.

### T14: Polymorphic Associations (IMPLEMENTATION-DATA.md)

**Polymorphic Type Field Classification** — three categories:
- **Category A** ("What entity is this?"): Use `$ref: EntityType` from common-api.yaml. Exception: service-specific enum if valid set includes non-entity roles.
- **Category B** ("What game content type?"): Opaque `string` — game-configurable codes extensible without schema changes.
- **Category C** ("What system state/mode?"): Service-specific enum.

**Decision tree** (tests in order, stop at first match):
1. Game designers define new values at deployment time? -> Opaque string (Category B)
2. L1 service would need to enumerate L2+ entity types? -> Opaque string (hierarchy isolation)
3. Valid values include non-entity roles? -> Service-specific enum (Category A exception)
4. All valid values are Bannou entity types? -> `$ref: EntityType` (Category A)

### T1/SCHEMA-RULES: Mandatory Type Reuse via $ref

- Each `{service}-events.yaml` MUST contain ONLY canonical definitions for events that service PUBLISHES
- No `$ref` references to other service event files (causes duplicate types)
- API schemas can `$ref` from `common-api.yaml` only (not from other services' API schemas)
- Event schemas can `$ref` from same service's `-api.yaml`, `common-api.yaml`, and `common-events.yaml`

### T1/SCHEMA-RULES: x-sdk-type (External SDK Type Mapping)

> **Restriction: Core SDK types only.** `x-sdk-type` may ONLY reference types from the Core SDK (`BeyondImmersion.Bannou.Core`). Domain-specific SDKs must define their own types in the service API schema and map between generated and SDK types at the plugin boundary.

**Known violation**: `music-api.yaml` currently uses `x-sdk-type` with 15 types from `BeyondImmersion.Bannou.MusicTheory.*`, which is a domain-specific SDK, NOT the Core SDK. This is a tenet violation — the fact that it exists in the codebase does not make it correct. See [Music x-sdk-type Violation](#8-music-x-sdk-type-violation) for remediation.

### T16: Naming Conventions (QUALITY.md)

Schema enum values must be **PascalCase**: `TwoParty`, `Active`, `JsonPath`.

### T21: Configuration-First (IMPLEMENTATION-DATA.md)

All runtime configuration MUST be defined in configuration schemas and accessed through generated classes. Includes typed enums where applicable.

---

## Enum Sharing Infrastructure

### How Bannou Shares Types Across Services

Bannou has **exactly two mechanisms** for sharing types across service schemas:

#### 1. common-api.yaml (Cross-Service Shared Types)

Types defined in `schemas/common-api.yaml` are generated once to `bannou-service/Generated/CommonApiModels.cs` in the `BeyondImmersion.BannouService` namespace. All service model generation excludes these types and imports the namespace.

**Currently shared enums**:
| Enum | Values | Purpose |
|------|--------|---------|
| `EntityType` | System, Account, Character, Actor, Guild, Organization, Government, Faction, Location, Realm, Item, Monster, Relationship, Session, Deity, Dungeon, Custom, Other | Polymorphic entity identifier |
| `ServiceHealthStatus` | Healthy, Degraded, Unavailable | Service-level health |
| `InstanceHealthStatus` | Healthy, Degraded, Overloaded, ShuttingDown, Unavailable | Instance-level health (superset of ServiceHealthStatus) |
| `SentimentCategory` | Excited, Supportive, Critical, Curious, Surprised, Amused, Bored, Hostile | Standardized sentiment |
| `CapabilityUpdateType` | Full, Delta | Capability manifest update type |
| `CapabilityUpdateReason` | SessionCreated, SessionStateChanged, RoleChanged, ServiceRegistered, ManualRefresh | Capability update reason |

**How it works in generation**:
1. `generate-common-api.sh` generates `CommonApiModels.cs` with all common types
2. For each service, `generate-models.sh` calls `extract_common_api_refs` on the service schema
3. Any `$ref` to `common-api.yaml` types are added to the NSwag exclusion list
4. Generated service models import `BeyondImmersion.BannouService` and reference the shared types
5. Result: one definition, used by all services, no duplication

**Usage pattern in schemas**:
```yaml
# Any service's -api.yaml can reference:
ownerType:
  $ref: 'common-api.yaml#/components/schemas/EntityType'
```

#### 2. x-sdk-type (Pre-Compiled Core SDK Types)

For types in the Core SDK (`BeyondImmersion.Bannou.Core`) only. The schema marks the type with `x-sdk-type`, NSwag excludes it during generation, and the generated code imports the Core SDK namespace.

**Constraint**: x-sdk-type may ONLY reference Core SDK types. This is because all generated models (request/response types, event models, client classes) are generated INTO `bannou-service/Generated/`. The Core SDK is a foundational dependency of bannou-service. Domain-specific SDKs (MusicTheory, StorylineTheory, etc.) are NOT — they are plugin-level dependencies.

**Known violation**: `music-api.yaml` currently uses `x-sdk-type` with 15 types from `BeyondImmersion.Bannou.MusicTheory.*`. This works mechanically only because bannou-service happens to reference MusicTheory as a NuGet dependency — but this reference itself is wrong. Domain SDKs should be plugin dependencies, not bannou-service dependencies. The MusicTheory x-sdk-type usage is a violation that should be remediated by defining those types in the music-api.yaml schema and mapping at the plugin boundary (A2 pattern).

**x-sdk-type CANNOT be used for**:
- Domain-specific SDK types (MusicTheory, StorylineTheory, BehaviorTheory, etc.)
- Plugin-specific types
- Types from other services' schemas

### What This Means for Plugin-Specific SDKs

Plugins like lib-music, lib-storyline, and lib-behavior use domain-specific SDKs (MusicTheory, StorylineTheory, etc.). These SDKs define their own enum types.

**The valid locations for enum types in Bannou**:

| Location | Available To | Example |
|----------|-------------|---------|
| `common-api.yaml` | All services (generated once, excluded + imported by all) | `EntityType`, `ServiceHealthStatus` |
| `{service}-api.yaml` | That service's generated models | `SaveCategory`, `CompressionType` |
| Core SDK (via x-sdk-type) | All generated models (Core SDK is a bannou-service dependency) | Core types only (e.g., `EntityType` if it were SDK-defined) |

**Plugin-specific SDKs** (MusicTheory, StorylineTheory, etc.) are NOT in this table. Plugins that use domain SDKs must:
1. Define their own version of the enum in their `-api.yaml` schema
2. Map between the schema enum and the SDK enum at the plugin boundary (in `{Service}Service.cs`)

This is a **legitimate A2 boundary** and mapping code is expected. The alternative (making bannou-service reference every domain SDK) would collapse the plugin architecture — bannou-service would need to know about every domain SDK, which is the opposite of the plugin model.

---

## OpenAPI Spec Research: Enum Subsets/Supersets

### Summary: No Native Support

OpenAPI (both 3.0.x and 3.1.x) has **no built-in mechanism** for defining enum subsets or supersets. This is a fundamental constraint of the JSON Schema underpinning: JSON Schema is a constraint system that only ADDS constraints, never removes them.

### What Was Investigated

The OpenAPI specification repository was checked for:
- Enum inheritance or composition mechanisms
- `oneOf`/`allOf`/`anyOf` for combining enum definitions
- How `enum` interacts with `$ref`
- Discriminator-based patterns
- OpenAPI 3.1.x improvements (JSON Schema 2020-12 alignment)

### What IS Possible

| Mechanism | Available In | What It Does | Limitation |
|-----------|-------------|--------------|------------|
| Basic `enum` keyword | 3.0.x, 3.1.x | Define inline enum values | No composition |
| `$ref` to shared enum | 3.0.x, 3.1.x | Reuse an enum definition as-is | Cannot restrict values |
| `discriminator` + `oneOf` | 3.0.x, 3.1.x | Type narrowing by property value | For polymorphic objects, not enum values |
| `allOf` composition | 3.0.x, 3.1.x | Combine schema constraints | Can only ADD constraints; cannot narrow enum |
| Sibling keywords next to `$ref` | 3.1.x only | Add description/summary to $ref | Cannot override `enum` values |
| `if`/`then`/`else` conditional | 3.1.x only | Apply different constraints based on context | Verbose; requires tooling support |
| `x-*` extensions | 3.0.x, 3.1.x | Custom metadata for code generators | Non-standard; needs custom processing |

### What IS NOT Possible

1. **`$ref` + enum restriction**: Cannot reference `AuthProvider` and then restrict to `[Google, Discord, Twitch, Steam]`
2. **Enum inheritance**: Cannot define `OAuthProvider extends AuthProvider minus Email`
3. **Enum composition**: Cannot define `AuthProvider = OAuthProvider + [Email]`
4. **Implicit intersection**: `oneOf` with enum properties unions values, doesn't intersect them

### Implications for Bannou

**Separate enum definitions per service are the correct pattern** when:
- Services need different subsets of the same concept
- Hierarchy isolation requires it (T14 test 2)
- The enum includes non-entity roles (T14 test 3)

**Mapping code at boundaries is unavoidable** — OpenAPI provides no way to express "this enum is a subset of that enum." The spec simply doesn't support it.

**Recommended approach for the Auth/Account provider case** and similar subset/superset relationships:

1. If the enums should be **identical**, move to `common-api.yaml` and `$ref` from both services
2. If one is a **strict subset**, define both separately, document the relationship in schema `description`, and maintain mapping code at the boundary
3. If one is a **superset**, same as above — separate definitions, documented relationship, boundary mapping

The OpenAPI spec provides no better option. Any schema-level "subset" annotation would need to be a custom `x-*` extension with custom code generation support.

### Spec References

| Feature | OpenAPI 3.0.3 | OpenAPI 3.1.0 |
|---------|--------------|--------------|
| Enum keyword | [Data Types - enum](https://spec.openapis.org/oas/v3.0.3#data-types) | [Schema Object - enum](https://spec.openapis.org/oas/v3.1.0#schema-object) |
| allOf composition | [Composition - allOf](https://spec.openapis.org/oas/v3.0.3#composition) | allOf with JSON Schema 2020-12 |
| Discriminator | [Discriminator Object](https://spec.openapis.org/oas/v3.0.3#discriminator-object) | [Discriminator Object](https://spec.openapis.org/oas/v3.1.0#discriminator-object) |
| $ref siblings | NOT ALLOWED | [Allowed in 3.1+](https://spec.openapis.org/oas/v3.1.0#properties) (informational only) |
| Conditional schemas | NOT AVAILABLE | if/then/else keywords |

---

## Boundary Classification Framework

Every enum mapping falls into one of these categories:

### Acceptable Boundaries (No Fix Needed)

**A1 — Third-Party Library Boundary**: Mapping between a Bannou enum and an external library's type (RabbitMQ, LibGit2Sharp, .NET framework types, LINQ expression trees, Redis). These are genuine abstraction boundaries.

**A2 — Plugin SDK Boundary**: Mapping between a schema-generated enum and a domain-specific SDK enum (MusicTheory, StorylineTheory) where the SDK isn't referenced by bannou-service. The plugin defines its own enum in its schema and maps at the boundary.

**A3 — Domain Decision Mapping**: Mapping between genuinely different domain concepts (e.g., ScenarioCategory to PoiType in Gardener). These aren't duplicate enums — they're different concepts with an intentional lossy transformation.

**A4 — Protocol Boundary**: Mapping between HTTP/WebSocket protocol types and internal types (HttpMethodType to HttpMethod, HttpStatusCode to ResponseCodes). These cross a protocol abstraction boundary.

### Violations (Fix Required)

**V1 — Duplicate Enum (Same Concept, Same Values)**: Two or more enums represent the same concept with identical or near-identical values. Fix: consolidate to one definition (in common-api.yaml or one service's schema with $ref).

**V2 — Duplicate Enum (Subset/Superset)**: One enum is a strict subset or superset of another, both representing the same domain concept. Fix: if the values should genuinely differ (different services need different subsets), document the relationship and keep separate. If they should be identical, consolidate.

**V3 — String Where Enum Should Be**: A schema field is typed as `string` when the valid values are a finite, system-owned set. Fix: define an enum in the schema and use `$ref` if shared.

**V4 — Internal Model Duplicates API Enum**: Service model (`*ServiceModels.cs`) defines an enum identical to the generated API enum. Fix: use the generated enum directly in internal models.

**V5 — String Configuration for Enum Values**: Configuration uses a string field that's parsed at runtime into enum values. Fix: use typed enum in configuration schema via `$ref` to the API schema's enum.

---

## Full Inventory

### Category 1: Enum-to-Enum Conversions

| # | File | Mapping | Classification | Severity | Action |
|---|------|---------|---------------|----------|--------|
| 1 | lib-auth/Services/OAuthProviderService.cs:769 | Provider -> OAuthProvider | **V2** (subset) | High | See [Auth/Account Provider Enums](#1-authaccount-provider-enums) |
| 2 | lib-account/AccountService.cs:797 | OAuthProvider -> AuthProvider | **V2** (superset) | High | See [Auth/Account Provider Enums](#1-authaccount-provider-enums) |
| 3 | lib-music/MusicService.cs:864-917 | KeySignatureMode <-> ModeType, ChordSymbolQuality <-> ChordQuality | **A2** (plugin SDK boundary) + **V1** (x-sdk-type violation) | High (x-sdk-type), None (remaining mappings) | MusicTheory is a domain SDK — the two remaining enum mappings ARE the correct A2 plugin boundary pattern. However, music-api.yaml's 15 x-sdk-type mappings to MusicTheory are a SCHEMA-RULES violation (Core SDK only). See [Music x-sdk-type Violation](#8-music-x-sdk-type-violation). |
| 4 | lib-license/LicenseService.cs:96 | EntityType -> ContainerOwnerType | **V2** (subset with non-entity roles) | High | See [License/Inventory Owner Type](#2-licenseinventory-owner-type) |
| 5 | lib-mesh/MeshServiceEvents.cs:206 | InstanceHealthStatus -> EndpointStatus | **V2** (subset) | Medium | See [Health Status Enums](#3-meshorchestratorhealth-status-enums) |
| 6 | lib-orchestrator/OrchestratorService.cs:2206 | ContainerStatusType -> DeployedServiceStatus | **A1** (third-party) | None | **RESOLVED**: Both enums are schema-defined with different values. ContainerStatusType normalizes Docker/Portainer/K8s API status strings; DeployedServiceStatus is the clean outbound API model (includes `Healthy`, excludes `Stopping`). Lossy mapping is intentional. See [Orchestrator ContainerStatusType (RESOLVED)](#4a-orchestrator-containerstatus-resolved). |
| 7 | lib-documentation/DocumentationService.cs:3092-3153 | BindingStatusInternal <-> BindingStatus, SyncStatusInternal <-> SyncStatus, OwnerTypeInternal <-> DocumentationOwnerType | **V4** (internal duplicates) | High | See [Documentation Internal Enums](#4-documentation-internal-enums) |
| 8 | lib-messaging/RabbitMQMessageBus.cs:553 | ExchangeType -> RmqExchangeType | **A1** (third-party) | None | RabbitMQ client library's ExchangeType is a static string class, not a Bannou enum. Genuine library boundary. |
| 9 | lib-messaging/RabbitMQMessageSubscriber.cs:226 | SubscriptionExchangeType -> RmqExchangeType | **V1** (code-only duplicate) | Medium | **RESOLVED**: SubscriptionExchangeType is a code-only enum in `bannou-service/Services/IMessageBus.cs:226-241` with identical values to schema-defined ExchangeType (Fanout, Direct, Topic). Used in 14+ call sites across lib-mapping, lib-actor, lib-connect, lib-messaging. Should be unified to use schema-generated ExchangeType. See [Messaging SubscriptionExchangeType (RESOLVED)](#9a-messaging-subscriptionexchangetype-resolved). |
| 10 | lib-transit/TransitService.cs:4163 | SettableConnectionStatus -> ConnectionStatus | **V2** (subset) | Medium | See [Transit Connection Status](#5-transit-settable-connection-status) |
| 11 | lib-gardener/GardenerGardenOrchestratorWorker.cs:666 | ScenarioCategory -> PoiType | **A3** (domain decision) | None | Different domain concepts with intentional lossy mapping. Acceptable. |
| 12 | lib-connect/ConnectService.cs:244 | HttpMethodType -> HttpMethod | **A4** (protocol) | None | Schema enum to .NET framework type. Legitimate protocol boundary. |
| 13 | lib-connect/ConnectService.cs:3077 | HttpStatusCode -> ResponseCodes | **A4** (protocol) | None | .NET HttpStatusCode to Bannou WebSocket ResponseCodes. Protocol boundary. |
| 14 | lib-documentation/Services/GitSyncService.cs:166 | ChangeKind -> GitChangeType | **A1** (third-party) | None | LibGit2Sharp external library enum. Genuine library boundary. |
| 15 | lib-state/SqliteStateStore.cs:1289, MySqlStateStore.cs:1358 | ExpressionType -> QueryOperator | **A1** (third-party) | None | LINQ expression tree enum to internal query model. Genuine abstraction boundary. |

### Category 2: String-to-Enum Parsing

| # | File | Parsing | Classification | Severity | Action |
|---|------|---------|---------------|----------|--------|
| 16 | lib-storyline/StorylineService.cs:2098 | string -> CharacterPersonality.ExperienceType | **A2** (authored content boundary) | None | **RECLASSIFIED**: Scenario mutation definitions are authored content (like ABML YAML). The string-to-enum parsing is at a legitimate external data boundary. See [Storyline String Fields (REVISED)](#6-storyline-string-fields-revised). |
| 17 | lib-storyline/StorylineService.cs:2138 | string -> CharacterHistory.BackstoryElementType | **A2** (authored content boundary) | None | **RECLASSIFIED**: Same as item 16. See [Storyline String Fields (REVISED)](#6-storyline-string-fields-revised). |
| 18 | lib-transit/TransitService.cs:795 | string entityType -> Inventory.ContainerOwnerType | **See note** | Low | Transit's `entityType` is intentionally Category B (opaque string). The parse to ContainerOwnerType is at a cross-service boundary where Transit calls Inventory. This is acceptable — Transit stores the opaque string, only parses when making the Inventory API call. |
| 19 | lib-actor/Handlers/ActorQueryHandler.cs:100 | string -> OptionsQueryType | **A2** (ABML boundary) | None | ABML interpreter boundary. Strings come from authored YAML behavior params. This is at the T1 exception boundary (ABML is a standalone runtime/interpreter). |
| 20 | lib-actor/Handlers/QueryOptionsHandler.cs:89,96 | string -> OptionsQueryType, OptionsFreshness | **A2** (ABML boundary) | None | Same ABML handler boundary. |
| 21 | lib-actor/Handlers/EmitPerceptionHandler.cs:109 | string -> PerceptionSourceType | **A2** (ABML boundary) | None | Same ABML handler boundary. |
| 22 | lib-save-load/SaveLoadService.cs:2476 | string -> SaveCategory, CompressionType | **V5** (string config) | Medium | See [Save-Load Configuration](#7-save-load-configuration-parsing) |
| 23 | lib-save-load/SaveExportImportManager.cs:417 | string -> SaveCategory | **A1** (external data) | Low | ZIP archive manifest is external file format. Parsing at system boundary is acceptable. |
| 24 | lib-achievement/AchievementPrerequisiteProviderFactory.cs:80 | object? -> EntityType | **A2** (DI boundary) | None | `IReadOnlyDictionary<string, object?>` is the DI provider interface contract. Enum arrives boxed. `is EntityType` check + `.ToString()` fallback is defensive and correct. |
| 25 | lib-documentation/ContentTransformService.cs:140 | string -> DocumentCategory | **A1** (external data) | None | Parsing frontmatter YAML from git-synced markdown files. External user content boundary. |
| 26 | lib-mesh/DistributedCircuitBreaker.cs:369,393,408 | string -> CircuitState | **A1** (infrastructure) | None | Parsing JSON from Redis Lua scripts. Redis has no enum type. Genuine infrastructure boundary. |
| 27 | lib-asset/AudioProcessor.cs:318 | string -> generic T enum | **A1** (external data) | None | Parsing audio metadata from third-party files. External data boundary. |

---

## Remediation Plan

### 1. Auth/Account Provider Enums

**Current state**:
- `auth-api.yaml` defines `Provider`: `[Google, Discord, Twitch, Steam]`
- `account-api.yaml` defines `OAuthProvider`: `[Google, Discord, Twitch, Steam]` (identical to Provider)
- `account-api.yaml` defines `AuthProvider`: `[Email, Google, Discord, Twitch, Steam]` (superset; adds Email)

**Analysis**:
- `Provider` and `OAuthProvider` are identical — clear V1 duplication
- `AuthProvider` is the superset with `Email` added (non-OAuth method)
- The semantic distinction is real: OAuth providers are a subset of all auth providers
- OpenAPI has no subset mechanism, so two definitions are required if both need to exist

**Investigation results** (agent audit):
- Only **Auth and Account** directly reference provider enums in code
- **Achievement** (L4) uses `AuthProvider.Steam` via generated Account client (indirect, no mapping code)
- Connect, Permission, Subscription, Game-Session: **zero references**
- No other service references any provider enum type

**Options**:
1. **Consolidate to common-api.yaml**: Define `OAuthProvider` (4 values) and `AuthProvider` (5 values) in common-api.yaml. Both Auth and Account `$ref` from there. Auth removes its `Provider` enum. Mapping code stays (subset relationship is real).
2. **Consolidate to one service**: Define both in Account (the domain owner of authentication methods), Auth $refs them. This is problematic because event schemas can only $ref their own service's API or common schemas.
3. **Keep separate, document relationship**: Keep distinct definitions but add `description` documenting the subset relationship.

**Recommended**: Option 1. Despite only 2 direct users, these are identity-boundary concepts (T32) used by L1 foundation services. common-api.yaml is the correct location for cross-service identity primitives. Auth's `Provider` enum (identical to `OAuthProvider`) is eliminated entirely — Auth `$ref`s `OAuthProvider` from common. The subset mapping code (OAuthProvider → AuthProvider) stays because the relationship is real and OpenAPI has no subset mechanism.

### 2. License/Inventory Owner Type

**Current state**:
- `common-api.yaml` defines `EntityType`: 18 values
- `inventory-api.yaml` defines `ContainerOwnerType`: 8 values (Character, Account, Location, Vehicle, Guild, Escrow, Mail, Other)
- ContainerOwnerType includes non-entity roles: `Vehicle`, `Escrow`, `Mail`

**Analysis**:
- Per T14 decision tree, test 3: "Valid values include non-entity roles?" -> Service-specific enum
- `ContainerOwnerType` is correctly a service-specific enum (T14 Category A exception) because it includes Escrow, Mail, Vehicle which are NOT in EntityType
- The mapping in LicenseService (EntityType -> ContainerOwnerType) crosses the correct boundary

**Recommended**: No schema change needed. ContainerOwnerType is correctly a Category A exception per T14 test 3. The mapping code is legitimate. Document this decision in the inventory-api.yaml description.

### 3. Mesh/Orchestrator Health Status Enums

**Current state**:
- `common-api.yaml` defines `ServiceHealthStatus`: `[Healthy, Degraded, Unavailable]`
- `common-api.yaml` defines `InstanceHealthStatus`: `[Healthy, Degraded, Overloaded, ShuttingDown, Unavailable]`
- `mesh-api.yaml` defines `EndpointStatus`: `[Healthy, Degraded, Unavailable, ShuttingDown]`

**Analysis**:
- `EndpointStatus` is `InstanceHealthStatus` minus `Overloaded`
- `ServiceHealthStatus` is a subset of both
- Mesh maps InstanceHealthStatus -> EndpointStatus with lossy conversion (Overloaded -> Degraded)
- The lossy mapping is intentional (endpoints don't report "overloaded" state)

**Options**:
1. **Consolidate EndpointStatus into common-api.yaml**: Use `InstanceHealthStatus` directly, document that Overloaded is not applicable for endpoints
2. **Keep separate**: EndpointStatus intentionally excludes Overloaded because endpoints don't have that concept

**Recommended**: Keep separate. The lossy mapping is intentional — `Overloaded` is an instance-level concept that doesn't apply to individual endpoints. Document the relationship in the EndpointStatus description. The mapping code in MeshServiceEvents.cs is the correct boundary.

### 4. Documentation Internal Enums

**Current state**:
- `documentation-api.yaml` defines `BindingStatus`, `SyncStatus`, `DocumentationOwnerType`
- `DocumentationService.cs:3092-3153` defines internal versions and six mapping methods

**Analysis**:
- This is a V4 violation. The internal enums are identical to the API enums.
- The service should use the generated API enums directly in its internal models.

**Recommended**: Remove internal enum definitions from DocumentationServiceModels.cs. Use generated API enums in state store models. Remove the six mapping methods. This is a straightforward cleanup.

### 5. Transit Settable Connection Status

**Current state**:
- `transit-api.yaml` defines `ConnectionStatus`: includes `Destroyed`
- `transit-api.yaml` defines `SettableConnectionStatus`: excludes `Destroyed`

**Analysis**:
- `SettableConnectionStatus` is a deliberate API-level restriction — users can't set a connection to "Destroyed" via the API
- This is a legitimate subset for API safety (the full ConnectionStatus is used in responses/events)

**Recommended**: Keep separate. This is an intentional API design pattern (writable subset of readable enum). Document the relationship. The mapping code is the correct boundary.

### 6. Storyline String Fields (REVISED)

**Current state**:
- StorylineService.cs parses string fields to `CharacterPersonality.ExperienceType` and `CharacterHistory.BackstoryElementType`
- Comments in code say "schema design limitation - should use enum type"
- `ExperienceType` (9 values): Trauma, Betrayal, Loss, Victory, Friendship, Redemption, Corruption, Enlightenment, Sacrifice
- `BackstoryElementType` (9 values): Origin, Occupation, Training, Trauma, Achievement, Secret, Goal, Fear, Belief

**Investigation results** (agent audit):
- Only **3 services** use these enums: Character-Personality (owner), Character-History (owner), Storyline (only external consumer)
- Disposition, Hearsay, Ethology, Character-Lifecycle: **zero references**
- The parsing occurs in `ScenarioMutation` execution — scenario mutations are **authored content** (created by developers/god-actors via API, stored in state store, executed later)
- The data path: Developer/god-actor authors scenario → Storyline stores it → Storyline executes it → parses string to enum → calls Character-Personality/History API with typed request

**Reclassification: V3 → A2 (authored content boundary)**

The original classification as V3 ("string where enum should be") assumed the strings were a schema design error. Investigation reveals they're at a legitimate external data boundary:

1. Scenario mutation definitions are **authored content** — equivalent to ABML YAML behavior parameters, which are also parsed from strings at execution time (items 19-21, all classified A2)
2. The `ScenarioMutation` schema stores mutation parameters as JSON with string-typed fields because mutations reference concepts from **multiple different services** (personality types, backstory types, item codes, quest IDs). Making each one a typed enum would require duplicating every referenced service's enums
3. The parsing happens at execution time with `Enum.TryParse` + validation error on failure — the correct pattern for untrusted/authored data
4. Only Storyline + the 2 owning services use these enums (below the 3+ service threshold for common-api.yaml)

**Recommended**: No action needed. The existing code is the correct A2 boundary pattern. Document in storyline-api.yaml's `ScenarioMutation` description that `experienceType` and `backstoryElementType` string fields are parsed at execution time into their respective enum types from the owning services.

### 7. Save-Load Configuration Parsing

**Current state**:
- Configuration field `DefaultCompressionByCategory` is `type: string, nullable: true`
- Parsed at runtime: `"QUICK_SAVE:NONE,AUTO_SAVE:GZIP,..."` -> pairs of (SaveCategory, CompressionType)
- Both `SaveCategory` and `CompressionType` are properly defined as enums in save-load-api.yaml

**Analysis**:
- T21 requires typed configuration. A comma-delimited string requiring runtime parsing is a T21 violation.
- The configuration should be structured with typed enum fields.

**Investigation results** (agent audit):
- SCHEMA-RULES Rule 4 forbids `type: object` but does NOT forbid `type: array`
- Config arrays ARE supported by the generator — puppetmaster, permission, achievement schemas use `type: array` with `items: type: string`, generating `string[]`
- However, arrays only generate `string[]` regardless of `items:` declaration — typed enum arrays are not supported
- Save-Load already uses the individual per-category property pattern for `DefaultMaxVersions*` properties
- Individual `$ref` to `CompressionType` generates compile-time typed enum properties with zero runtime parsing

**Recommended**: Individual properties per category (definitively confirmed). Each property is a `$ref` to `CompressionType` with env vars like `SAVE_LOAD_DEFAULT_COMPRESSION_QUICK_SAVE`. This matches the existing `DefaultMaxVersions*` pattern in the same schema and eliminates runtime parsing entirely.

### 8. Music x-sdk-type Violation

**Current state**:
- music-api.yaml uses `x-sdk-type` for 15 types mapped to `BeyondImmersion.Bannou.MusicTheory.*`
- This includes: MidiJson, MidiHeader, MidiTrack, MidiEvent, MidiEventType, Pitch, PitchClass, PitchRange, TempoEvent, TimeSignatureEvent, KeySignatureEvent, ModeType, ModeDistribution, VoiceLeadingRules, VoiceLeadingViolationType
- Two additional enum mappings exist without x-sdk-type: KeySignatureMode <-> ModeType, ChordSymbolQuality <-> ChordQuality
- bannou-service references MusicTheory as a NuGet dependency (enabling this to compile)

**Analysis**:
- SCHEMA-RULES.md is clear: "**Restriction: Core SDK types only.** `x-sdk-type` may ONLY reference types from the Core SDK (`BeyondImmersion.Bannou.Core`)."
- MusicTheory is a domain-specific SDK, NOT the Core SDK. The 15 x-sdk-type usages are violations.
- The fact that bannou-service references MusicTheory is itself wrong — domain SDKs should be plugin dependencies, not bannou-service dependencies.
- The two remaining enum mappings (KeySignatureMode <-> ModeType, ChordSymbolQuality <-> ChordQuality) are actually the **correct pattern** — they're A2 plugin SDK boundary mappings.

**Recommended**:
1. Define all 15 types natively in music-api.yaml (remove x-sdk-type annotations)
2. Remove the MusicTheory NuGet reference from bannou-service (add it to lib-music only)
3. Add boundary mapping code in MusicService.cs for the 15 types (same pattern as the existing two mappings)
4. The existing KeySignatureMode/ChordSymbolQuality mappings are already correct — they become the model for all 15

**Effort**: Large — touches schemas, generation, bannou-service project references, and lib-music service code. Should be its own dedicated task, not mixed with other enum fixes.

---

## Task Summary

| Priority | Item | Type | Effort | Status |
|----------|------|------|--------|--------|
| **High** | Music: remove 15 x-sdk-type violations, define types in schema, remove MusicTheory from bannou-service deps | V1 (SCHEMA-RULES) | Large (schema + regen + project refs + mapping code) | Pending |
| **High** | Auth/Account provider enum consolidation to common-api.yaml | V1/V2 | Medium (schema + regen + update both services) | Pending |
| **High** | Documentation: remove internal enum duplicates, use API enums | V4 | Small (code cleanup only) | Pending |
| **Medium** | Save-Load: replace string config with typed enum properties | V5 | Small (schema + config + regen) | Pending |
| **Medium** | Messaging: unify SubscriptionExchangeType to schema-generated ExchangeType | V1 (code-only duplicate) | Medium (14+ call sites across 4 plugins) | Pending |
| **None** | ~~Storyline: define ExperienceType/BackstoryElementType enums in schema~~ | ~~V3~~ → A2 | N/A | **RESOLVED** — Reclassified as acceptable A2 boundary (authored content) |
| **None** | ~~Orchestrator: investigate ContainerStatusType -> DeployedServiceStatus~~ | ~~V4~~ → A1 | N/A | **RESOLVED** — Confirmed A1 (Docker/Portainer/K8s API boundary) |
| **None** | License/Inventory ContainerOwnerType | Confirmed correct (T14 test 3) | Document only | No action |
| **None** | Transit SettableConnectionStatus | Confirmed correct (API subset) | Document only | No action |
| **None** | Health status enums (3 tiers) | Confirmed correct (intentional granularity) | Document only | No action |
| **None** | All A1/A2/A3/A4 items (excluding music x-sdk-type) | Acceptable boundaries | No action | No action |

---

## Execution Principle

Every remediation item in the task summary will be executed. Downstream changes (event consumer updates, deserialization verification, project reference cleanup, etc.) are discovered and handled during execution — they are work, not open questions. The only open questions are ones where the **answer changes what we build**.

---

## Resolved Questions

All five original open questions have been investigated and resolved.

### Q1. Auth/Account — common-api.yaml or keep service-local? **RESOLVED: common-api.yaml**

**Investigation**: Agent audited all 55 services' schemas and C# code for provider enum references.

**Findings**: Only Auth and Account directly use provider enums. Achievement uses `AuthProvider.Steam` via generated Account client (indirect). Connect, Permission, Subscription, Game-Session, and all other services: zero references.

**Decision**: Move to common-api.yaml. Despite only 2 direct consumers, these are identity-boundary concepts (T32) used by L1 foundation services. The identity boundary is a cross-cutting concern that belongs in shared infrastructure types. Auth's `Provider` enum (identical to `OAuthProvider`) is eliminated. Both services `$ref` from common-api.yaml.

### Q2. Storyline — common-api.yaml or duplicate? **RESOLVED: Neither — reclassified as A2**

**Investigation**: Agent audited all L4 services for ExperienceType/BackstoryElementType usage.

**Findings**: Only 3 services use these enums: Character-Personality (owner), Character-History (owner), Storyline (only consumer). Disposition, Hearsay, Ethology, Character-Lifecycle: zero references. The Storyline parsing occurs at a legitimate external data boundary — scenario mutations are authored content equivalent to ABML YAML behavior parameters.

**Decision**: No action. The existing string-to-enum parsing is the correct A2 boundary pattern for authored content. Items 16-17 reclassified from V3 to A2. Removed from the remediation task list.

### Q3. Save-Load — config schema typed arrays? **RESOLVED: Individual properties**

**Investigation**: Agent read SCHEMA-RULES.md Rule 4, examined config generation scripts, and checked existing configuration schemas.

**Findings**: SCHEMA-RULES Rule 4 forbids `type: object` but not `type: array`. Arrays ARE supported (puppetmaster, permission, achievement use them), but generate `string[]` only — the generator maps all arrays to `string[]` regardless of `items:` type. Typed enum arrays are not supported without generator changes. Save-Load already uses per-category individual properties for `DefaultMaxVersions*`.

**Decision**: Individual properties per category. Each `$ref` to `CompressionType` with env vars like `SAVE_LOAD_DEFAULT_COMPRESSION_QUICK_SAVE`. Matches existing Save-Load convention and eliminates runtime parsing.

### Q4. Orchestrator ContainerStatusType — A1 or V4? **RESOLVED: A1 (no action)**

**Investigation**: Agent traced ContainerStatusType through all backend implementations.

**Findings**: ContainerStatusType normalizes external API status strings:
- Docker: `"running"`, `"exited"`, `"dead"`, `"paused"`, `"created"`, `"restarting"` (from `Docker.DotNet.Models.ContainerListResponse.State`)
- Portainer: Same Docker state values via REST API
- Kubernetes: Pod phase strings (`"Running"`, `"Pending"`, `"Succeeded"`, `"Failed"`, `"Unknown"`)

DeployedServiceStatus is a different enum with different values (includes `Healthy`, excludes `Stopping`). The lossy mapping is intentional — different granularity for different purposes.

**Decision**: A1 (third-party boundary). No action needed. Both enums are schema-defined, both are correct, the mapping is the expected normalization pattern.

### Q5. Follow-up audit sweep? **RESOLVED: Completed — no new violations**

**Investigation**: Agent searched plugins/ (excluding Generated/ and tests/) for switch expressions, direct enum casts `(TargetEnum)(int)source`, `Enum.ToString()` assignments, and string comparisons against enum names.

**Findings**:
- **6 direct enum casts** in `lib-storyline/StorylineServiceModels.cs` (SdkTypeMapper class): All A2 boundary mappings (ArcType, SpectrumType, PlanningUrgency, EffectCardinality ↔ SDK equivalents). These are the **correct reference implementation** for A2 plugin SDK boundary mapping.
- **1 switch expression** in `lib-mapping/Helpers/AffordanceScorer.cs`: AffordanceType → List<MapKind>. A3 (domain decision mapping).
- **1 HTTP status mapping** in `lib-inventory/InventoryService.cs`: int pattern → StatusCodes. A4 (protocol boundary).
- No new V1-V5 violations discovered.

**Decision**: Original inventory is comprehensive. The broader sweep confirmed all additional patterns are acceptable A1-A4 boundaries. The SdkTypeMapper in lib-storyline should be referenced as the canonical A2 implementation example.

---

## Investigation Detail: Orchestrator ContainerStatusType (RESOLVED) {#4a-orchestrator-containerstatus-resolved}

**Value mapping from external APIs**:

| External Source | External Value | ContainerStatusType | DeployedServiceStatus |
|---|---|---|---|
| Docker State | `"running"` | Running | Running |
| Docker State | `"restarting"` | Starting | Starting |
| Docker State | `"exited"` | Stopped | Stopped |
| Docker State | `"dead"` | Unhealthy | Unhealthy |
| Docker State | `"created"` | Stopped | Stopped |
| Docker State | `"paused"` | Stopping | Stopped (lossy) |
| K8s Phase | `"Running"` | Running | Running |
| K8s Phase | `"Pending"` | Starting | Starting |
| K8s Phase | `"Succeeded"` | Stopped | Stopped |
| K8s Phase | `"Failed"` | Unhealthy | Unhealthy |

Key: `ContainerStatusType` = `[Running, Starting, Stopping, Stopped, Unhealthy]` (container runtime state).
`DeployedServiceStatus` = `[Starting, Running, Healthy, Unhealthy, Stopped]` (service-level health — includes `Healthy`, excludes `Stopping`).

---

## Investigation Detail: Messaging SubscriptionExchangeType (RESOLVED) {#9a-messaging-subscriptionexchangetype-resolved}

**Current state**:
- `SubscriptionExchangeType`: Code-only enum in `bannou-service/Services/IMessageBus.cs:226-241`
- `ExchangeType`: Schema-defined in `messaging-api.yaml`, generated to `MessagingModels.cs`
- Both have identical values: `[Fanout, Direct, Topic]`
- SubscriptionExchangeType is used in `IMessageSubscriber.SubscribeDynamicAsync()` and `SubscribeDynamicRawAsync()`
- 14+ call sites across lib-mapping, lib-actor, lib-connect, lib-messaging

**Classification**: V1 (code-only enum duplicating schema-defined enum). This violates schema-first principle (T1).

**Remediation**:
1. Update `IMessageSubscriber` interface to use schema-generated `ExchangeType` instead of `SubscriptionExchangeType`
2. Update all 14+ call sites to use `ExchangeType`
3. Remove the `SubscriptionExchangeType` enum from `IMessageBus.cs`
4. The mapping to `RmqExchangeType` in RabbitMQMessageSubscriber stays (A1 boundary to RabbitMQ client)

---

## Broader Audit Sweep Results

A comprehensive sweep beyond the original search patterns (which used "map", "mapping", `Enum.Parse`, `Enum.TryParse`) was performed covering switch expressions, direct enum casts, `Enum.ToString()`, and string comparisons against enum names.

**New items found** (all acceptable, no remediation needed):

| # | File | Pattern | Classification |
|---|------|---------|---------------|
| 28-33 | lib-storyline/StorylineServiceModels.cs (SdkTypeMapper) | `(SdkEnum)(int)schemaEnum` and reverse for ArcType, SpectrumType, PlanningUrgency, EffectCardinality | **A2** — Reference implementation for plugin SDK boundary mapping |
| 34 | lib-mapping/Helpers/AffordanceScorer.cs | AffordanceType switch → List<MapKind> | **A3** — Domain decision mapping |
| 35 | lib-inventory/InventoryService.cs | int HTTP status pattern → StatusCodes | **A4** — Protocol boundary |

**Conclusion**: The original 27-item inventory is comprehensive. No new violations exist beyond what was already catalogued.

---

## Forward-Looking Rules

Based on this analysis, the following rules should govern future enum work:

### Rule 1: Enum Location Decision Tree

When adding a new enum, apply in order:

1. **Is it a system-wide primitive used by 3+ services?** → `common-api.yaml`
2. **Is it an identity-boundary concept (T32)?** → `common-api.yaml`
3. **Is it a domain SDK type?** → Define in service `-api.yaml`, map at plugin boundary (A2 pattern)
4. **Is it service-specific?** → Define in the owning service's `-api.yaml`
5. **Is it game-configurable content?** → Opaque `string` (T14 Category B)

### Rule 2: Enum Duplication Is Sometimes Correct

Separate definitions are the correct pattern when:
- **API safety subsets**: Writable enum excludes dangerous values (Transit's SettableConnectionStatus)
- **Granularity tiers**: Different levels of detail for different concerns (ServiceHealthStatus vs InstanceHealthStatus vs EndpointStatus)
- **Non-entity role inclusion**: Service-specific enum includes roles not in EntityType (Inventory's ContainerOwnerType)
- **Cross-service boundary**: Schemas cannot `$ref` other services' API schemas

In all cases, document the relationship in the enum's schema `description`.

### Rule 3: Authored Content Parsing Is A2, Not V3

String-to-enum parsing at authored content boundaries (ABML behavior parameters, scenario mutation definitions, configuration YAML) is an acceptable A2 boundary, NOT a V3 violation. The test: "Did a developer/designer/god-actor author this string value, or did the system generate it from a typed model?" If authored → A2. If system-generated → V3.

### Rule 4: Code-Only Enums Are Always Wrong

If an enum exists only in C# code (not in any schema), it violates schema-first principle (T1). Either:
- Define it in the appropriate schema and generate it, OR
- Use an existing schema-defined enum

The only exception is enums that genuinely cannot be schema-defined (e.g., internal compiler states in ABML).

### Rule 5: Internal Model Enums Must Use Generated Types

Service models (`*ServiceModels.cs`) must use generated API enums directly. Defining an internal enum with identical values to a generated enum (V4 pattern) creates unnecessary mapping code and drift risk. The generated enum IS the type.

### Rule 6: Configuration Must Be Typed End-to-End

Configuration properties that represent enum values must use `$ref` to the enum type in the configuration schema. Comma-delimited strings parsed at runtime (V5 pattern) bypass all T21 guarantees. When per-category properties are needed, define individual typed properties rather than string bags.

---

*This document is the remediation reference. Execute from the Task Summary above. All original open questions are resolved.*
