# Storyline Plugin Deep Dive

> **Plugin**: lib-storyline
> **Schema**: schemas/storyline-api.yaml
> **Version**: 1.0.0
> **State Store**: storyline-plans (Redis), storyline-plan-index (Redis)

---

## Overview

The Storyline service wraps the `storyline-theory` and `storyline-storyteller` SDKs to provide HTTP endpoints for seeded narrative generation from compressed archives. Plans describe narrative arcs with phases, actions, and entity requirements - callers (gods/regional watchers) decide whether to instantiate them. The service is internal-only (Layer 4 Game Features) and requires the `developer` role for all endpoints.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | Plan caching and realm-based index storage |
| lib-messaging (IMessageBus) | Publishing `storyline.composed` events |
| lib-resource (IResourceClient) | Fetching archive and snapshot data for composition |
| storyline-theory SDK | Archive types, arc types, spectrum types, actant roles |
| storyline-storyteller SDK | StorylineComposer, GOAP planning, templates, phases |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| None currently | No services consume `storyline.composed` events yet |

> **Note**: The events schema notes that future phases may have consumers subscribing to `storyline.composed` for monitoring storyline activity or indexing for search.

---

## State Storage

**Store 1**: `storyline-plans` (Backend: Redis, Prefix: `storyline:plan`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{planId}` | `CachedPlan` | Cached composed storyline plans with TTL |
| `cache:{cacheKey}` | `CachedPlan` | Deterministic plan cache (when seed provided) |

**Store 2**: `storyline-plan-index` (Backend: Redis, Prefix: `storyline:idx`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm:{realmId}` | Sorted Set | Plan index by realm for list queries (score = creation timestamp) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `storyline.composed` | `StorylineComposedEvent` | After successful plan composition |

### Consumed Events

This plugin does not consume external events. The events schema notes that future phases may subscribe to `resource.compressed` for discovery.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `PlanCacheTtlSeconds` | `STORYLINE_PLAN_CACHE_TTL_SECONDS` | 3600 | TTL in seconds for cached composed plans |
| `DefaultPlanningUrgency` | `STORYLINE_DEFAULT_PLANNING_URGENCY` | Medium | Default GOAP planning urgency tier |
| `PlanCacheEnabled` | `STORYLINE_PLAN_CACHE_ENABLED` | true | Whether to cache deterministic plans |
| `DefaultGenre` | `STORYLINE_DEFAULT_GENRE` | "drama" | Default genre when not specified |
| `MaxSeedSources` | `STORYLINE_MAX_SEED_SOURCES` | 10 | Maximum seed sources per compose request |
| `ConfidenceBaseScore` | `STORYLINE_CONFIDENCE_BASE_SCORE` | 0.5 | Base confidence score before bonuses |
| `ConfidencePhaseThreshold` | `STORYLINE_CONFIDENCE_PHASE_THRESHOLD` | 3 | Min phases for phase count bonus |
| `ConfidencePhaseBonus` | `STORYLINE_CONFIDENCE_PHASE_BONUS` | 0.2 | Bonus when phase threshold met |
| `ConfidenceCoreEventBonus` | `STORYLINE_CONFIDENCE_CORE_EVENT_BONUS` | 0.15 | Bonus for core events in plan |
| `ConfidenceActionCountBonus` | `STORYLINE_CONFIDENCE_ACTION_COUNT_BONUS` | 0.15 | Bonus for action count in range |
| `ConfidenceMinActionCount` | `STORYLINE_CONFIDENCE_MIN_ACTION_COUNT` | 5 | Min actions for action count bonus |
| `ConfidenceMaxActionCount` | `STORYLINE_CONFIDENCE_MAX_ACTION_COUNT` | 20 | Max actions for action count bonus |
| `RiskMinActionThreshold` | `STORYLINE_RISK_MIN_ACTION_THRESHOLD` | 3 | Min actions before "thin_content" risk |
| `RiskMinPhaseThreshold` | `STORYLINE_RISK_MIN_PHASE_THRESHOLD` | 2 | Min phases before "flat_arc" risk |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<StorylineService>` | Structured logging |
| `StorylineServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for plans and indexes |
| `IMessageBus` | Event publishing |
| `IResourceClient` | Fetching archives and snapshots |
| `StorylineComposer` | SDK entry point for composition (directly instantiated) |

---

## API Endpoints (Implementation Notes)

### Composition

**POST /storyline/compose**: Core composition endpoint that:
1. Validates seed source count (0 < count ≤ MaxSeedSources)
2. Checks cache for deterministic requests (when seed provided and cache enabled)
3. Fetches archive/snapshot data via `IResourceClient`
4. Decompresses gzip+base64 encoded entries
5. Populates `ArchiveBundle` with typed archive data (character, character-history, character-encounter, character-personality)
6. Resolves arc type from request or goal mapping
7. Resolves primary spectrum from goal
8. Builds Greimas actant assignments from seed source roles
9. Calls SDK `StorylineComposer.Compose()` with urgency level
10. Calculates confidence score using config-driven thresholds
11. Identifies risks (thin_content, missing_obligatory_scenes, flat_arc)
12. Caches plan with TTL and updates realm index
13. Publishes `storyline.composed` event

**Goal to Arc Type Mapping**:
- `Revenge` → `Oedipus` (fall-rise-fall)
- `Resurrection` → `ManInHole` (fall then rise)
- `Legacy` → `RagsToRiches` (monotonic rise)
- `Mystery` → `Cinderella` (rise-fall-rise)
- `Peace` → `ManInHole` (fall then rise)
- Default → `ManInHole`

**Goal to Spectrum Mapping**:
- `Revenge` → `JusticeInjustice`
- `Resurrection` → `LifeDeath`
- `Legacy` → `SuccessFailure`
- `Mystery` → `WisdomIgnorance`
- `Peace` → `LoveHate`
- Default → `JusticeInjustice`

### Plans

**POST /storyline/plan/get**: Retrieves cached plan by ID. Returns `found: false` if plan expired or doesn't exist.

**POST /storyline/plan/list**: Lists plans filtered by realm using sorted set range queries. Returns empty results if no realm filter provided (full scan would be expensive).

---

## Visual Aid

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Compose Request Flow                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│   ComposeRequest                                                    │
│   ├─ seedSources[] ───────┬─► IResourceClient.GetArchiveAsync()    │
│   │  (archiveId/snapshotId)   IResourceClient.GetSnapshotAsync()   │
│   │                       │                                         │
│   ├─ goal ───────────────►│   ┌─────────────────────┐              │
│   ├─ genre? ──────────────┼──►│   ArchiveBundle     │              │
│   ├─ arcType? ────────────┤   │  ├─ character       │              │
│   ├─ urgency? ────────────┤   │  ├─ character-history│             │
│   └─ seed? ───────────────┤   │  ├─ character-encounter│           │
│                           │   │  └─ character-personality│          │
│   Cache Check ◄───────────┤   └──────────┬──────────┘              │
│   (if seed provided)      │              │                          │
│                           │              ▼                          │
│                           │   ┌─────────────────────┐              │
│                           │   │  StorylineComposer  │              │
│                           │   │  (SDK)              │              │
│                           │   │  ├─ Template        │              │
│                           │   │  ├─ GOAP Planner    │              │
│                           │   │  └─ ArchiveExtractor│              │
│                           │   └──────────┬──────────┘              │
│                           │              │                          │
│                           │              ▼                          │
│                           │   ┌─────────────────────┐              │
│                           │   │   StorylinePlan     │              │
│                           │   │   └─ Phases[]       │              │
│                           │   │      └─ Actions[]   │              │
│                           │   └──────────┬──────────┘              │
│                           │              │                          │
│   ┌───────────────────────┼──────────────┴───────────────────────┐ │
│   │                       │                                       │ │
│   │  ┌─────────────┐      │      ┌─────────────────────┐         │ │
│   │  │ Plan Cache  │◄─────┼──────│ ComposeResponse     │         │ │
│   │  │ (Redis TTL) │      │      │ ├─ planId           │         │ │
│   │  └─────────────┘      │      │ ├─ confidence       │         │ │
│   │                       │      │ ├─ phases[]         │         │ │
│   │  ┌─────────────┐      │      │ ├─ risks[]          │         │ │
│   │  │ Realm Index │◄─────┼──────│ └─ themes[]         │         │ │
│   │  │ (Sorted Set)│      │      └─────────────────────┘         │ │
│   │  └─────────────┘      │                                       │ │
│   │                       │      ┌─────────────────────┐         │ │
│   │  ┌─────────────┐      │      │ storyline.composed  │         │ │
│   │  │ MessageBus  │◄─────┼──────│ (Event)             │         │ │
│   │  └─────────────┘      │      └─────────────────────┘         │ │
│   └───────────────────────┴───────────────────────────────────────┘ │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## SDK Architecture

The Storyline service wraps two internal SDKs:

### storyline-theory
- **Arcs**: ArcType enum (RagsToRiches, Tragedy, ManInHole, Icarus, Cinderella, Oedipus)
- **Spectrums**: SpectrumType enum (10 Life Value spectrums from Story Grid)
- **Actants**: ActantRole enum (Greimas actantial model - Subject, Object, Sender, Receiver, Helper, Opponent)
- **Archives**: ArchiveBundle, ArchiveExtractor, archive model types

### storyline-storyteller
- **Composition**: StorylineComposer (main entry point)
- **Planning**: StoryGoapPlanner, StorylinePlan, StorylinePlanPhase, StorylinePlanAction, PlanningUrgency
- **Templates**: StoryTemplate, PhasePosition, PhaseTargetState, TemplateRegistry
- **Actions**: ActionEffect, NarrativeEffect, EffectCardinality

The plugin bridges HTTP requests to SDK calls. SDK types are exposed directly in API responses via `x-sdk-type` annotations in the schema.

---

## Stubs & Unimplemented Features

1. **ContinuePhase**: The SDK has a `ContinuePhase` method for multi-phase composition but the HTTP API only exposes single-call composition. No endpoint exists for iterative phase generation.

2. **EntitiesToSpawn**: The `ComposeResponse.entitiesToSpawn` field is always null. The comment indicates "MVP: callers provide archive IDs, no entity spawning."

3. **Links extraction**: The `ComposeResponse.links` field is always null. Comment: "MVP: no link extraction."

4. **Event subscription**: The events schema notes "Future phases may subscribe to `resource.compressed` for discovery" but no subscriptions are implemented.

---

## Potential Extensions

1. **ListPlans without realm filter**: Currently returns empty results when no realm filter provided. Could implement paginated full scan or require realm filter (breaking change).

2. **Plan invalidation**: No mechanism to invalidate cached plans when source archives change. Could subscribe to `resource.archive.updated` events.

3. **Streaming composition**: The SDK supports iterative phase generation via `ContinuePhase`. Could expose a streaming endpoint for long-running compositions.

4. **Plan validation**: No validation that plan actions are achievable given the world state. Could add a validation endpoint.

5. **Multi-realm plans**: Current design assumes single realm anchor. Could support cross-realm storylines.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Empty ListPlans without realm filter**: Returns empty results rather than error when no `realmId` provided. This is intentional to avoid expensive full scans - callers should always filter by realm.

2. **Gzip decompression assumed**: Archive entry data is expected to be gzip-compressed and base64-encoded. No validation or fallback for uncompressed data.

3. **Character ID from archive ID**: The actant assignment logic attempts to map archive IDs to character IDs directly, which works for character archives but may not work for other archive types.

4. **First character as default Subject**: If no role hints provided, the first character in the archive bundle becomes the Subject actant by default.

5. **GetPlan returns OK with found=false**: Unlike typical 404 patterns, GetPlan always returns 200 OK with a `found` boolean to indicate whether the plan exists.

### Design Considerations (Requires Planning)

1. **Archive type handling**: Only handles `character`, `character-history`, `character-encounter`, `character-personality` archive types. Unknown types are logged and skipped. Should realm archives be supported?

2. **Cache key stability**: The cache key includes seed, goal, arc type, genre, and sorted archive/snapshot IDs. Changes to this formula would invalidate existing cached plans.

3. **Plan index cleanup**: The realm index sorted set entries are not cleaned up when plans expire. Over time this could accumulate stale entries pointing to expired plans.

---

## Work Tracking

No active work items.
