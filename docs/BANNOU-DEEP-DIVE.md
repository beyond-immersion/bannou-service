# bannou-service Deep Dive

> **Project**: `bannou-service/`
> **Role**: Shared runtime substrate — interfaces, abstractions, helpers, and the ABML execution engine consumed by all 78 plugins
> **Lines of Code**: ~30,700 manual (145 files) + ~305,000 generated
> **Not a Plugin**: bannou-service has no schema, no endpoints, no state stores. It IS the platform that plugins plug into.

## Overview

bannou-service is the shared .NET project that every plugin references. It defines:

- **Infrastructure abstractions** (state stores, messaging, mesh, telemetry) that L0 plugins implement
- **Service framework** (IBannouService, plugin loading, DI registration, configuration) that all plugins use
- **DI inversion interfaces** (12 Provider/Listener interfaces + 5 provider-like types) that enable cross-layer communication
- **ABML execution runtime** (tree-walking interpreter, channel scheduler, cognition pipeline) used by lib-actor
- **Behavior system interfaces** (14 interfaces) for the behavior stacking, control gating, and cinematic systems
- **Shared helpers** (deprecation cleanup, error responses, event batching, pagination, enum mapping) used across plugins

For usage patterns and code examples, see [HELPERS-AND-COMMON-PATTERNS.md](reference/HELPERS-AND-COMMON-PATTERNS.md). This document maps what EXISTS and where to find it; HELPERS shows how to USE it.

---

## Subsystem Map

```
bannou-service/
├── Services/          Core infrastructure interfaces + service framework
├── Providers/         DI inversion interfaces for cross-layer communication
├── Events/            Event fan-out, publisher base, template registry
├── Helpers/           DeprecationCleanupHelper, ErrorResponses
├── Abml/
│   ├── Execution/     ABML document executor + 12 action handlers
│   │   ├── Channel/   Multi-channel cooperative scheduler
│   │   └── Handlers/  Cond, ForEach, Repeat, Set, Goto, Call, Return, Log, etc.
│   └── Cognition/     5-stage cognition pipeline + 6 handlers
│       └── Handlers/  FilterAttention, QueryMemory, AssessSignificance, etc.
├── Behavior/          14 behavior system interfaces (stacks, gates, cinematics)
├── Attributes/        Service registration + config validation attributes
├── Plugins/           Plugin loader + base classes
├── ServiceClients/    Mesh client infrastructure + session context
├── ClientEvents/      WebSocket client event publishing
├── History/           Backstory, dual-index, pagination, compression helpers
├── Controllers/       Controller framework + health endpoint
├── Configuration/     App config + env var normalization
├── ResourceTemplates/ ABML resource template validation
├── Archives/          IResourceArchive marker interface
├── Storage/           Asset storage provider abstraction
├── Protocol/          Client-salted GUID generation
├── Meta/              Runtime schema introspection
├── Logging/           Serilog configuration
├── Testing/           ServiceTestBase + assertion helpers
├── Utilities/         TemplateSubstitutor, ResponseValidator
└── (top-level)        Enums, ExtensionMethods, AppConstants, Program.cs, etc.
```

---

## 1. Services/ — Core Infrastructure Interfaces

The heart of bannou-service. Every plugin depends on these interfaces; L0 plugins implement them.

### State Store System

| Type | Purpose | Backends |
|------|---------|----------|
| `IStateStore<T>` | Base CRUD + bulk operations + ETag concurrency | All |
| `IStateStoreFactory` | Factory for creating typed stores from `StateStoreDefinitions` constants | All |
| `ICacheableStateStore<T>` | Sets, sorted sets, counters, hashes | Redis, InMemory |
| `IQueryableStateStore<T>` | LINQ expression queries, paged queries | MySQL, SQLite |
| `IJsonQueryableStateStore<T>` | Server-side MySQL JSON functions (JSON_EXTRACT, JSON_CONTAINS) | MySQL |
| `ISearchableStateStore<T>` | Full-text search via RedisSearch (FT.* commands) | Redis+Search |
| `IRedisOperations` | Escape hatch: Lua scripts, raw INCR/DECR, hash ops, TTL | Redis only |
| `StateStoreExtensions` | `UpdateWithRetryAsync` (2 overloads), string list index helpers | Extension methods |
| `IDistributedLockProvider` | Redis SET NX EX distributed locks | Redis |

**Documented in HELPERS**: Yes, §1 (State Store Helpers). Complete and accurate.

### Messaging System

| Type | Purpose |
|------|---------|
| `IMessageBus` | Service-to-service event publishing (TryPublishAsync, TryPublishRawAsync, TryPublishErrorAsync) |
| `IMessageSubscriber` | Static and dynamic event subscriptions |
| `IMessageTap` | Event forwarding between exchanges (character event tapping) |
| `IFlushable` | Common flush interface for event batchers |

**WARNING**: `IMessageBus` is for service events ONLY. Client events use `IClientEventPublisher`.

**Documented in HELPERS**: Partially — §2 covers publishing patterns but not IMessageTap.

### Mesh System

| Type | Purpose |
|------|---------|
| `IMeshInvocationClient` | Service-to-service HTTP calls with circuit breaker, retries |
| `IServiceAppMappingResolver` | Service name → app-id routing (dynamic via RabbitMQ) |
| `ServiceAppMappingResolver` | Default implementation with infrastructure service pinning |
| `IMeshInstanceIdentifier` | Process-stable node identity (MESH_INSTANCE_ID env or random) |
| `MeshInvocationException` | Typed exception with app-id, method, status code |
| `ServiceHeartbeatManager` | Periodic heartbeat publishing with service suppression |

**Documented in HELPERS**: Partially — §14 covers IMeshInstanceIdentifier. ServiceHeartbeatManager undocumented.

### Telemetry System

| Type | Purpose |
|------|---------|
| `ITelemetryProvider` | Spans, counters, histograms, gauges, store wrapping |
| `NullTelemetryProvider` | No-op implementation when telemetry disabled |
| `TelemetryComponents` | Standard component name constants (bannou.state, etc.) |
| `TelemetryMetrics` | Standard metric name constants |

**Documented in HELPERS**: Yes, §10. Accurate.

### Service Framework

| Type | Purpose |
|------|---------|
| `IBannouService` | Base interface: lifecycle (OnStart/OnRunning/OnShutdown), discovery, heartbeat, permissions, event consumers |
| `BannouServiceBase` | Abstract base with InstanceId |
| `BannouService<T>` | Generic base with typed configuration |
| `ServiceResponse` / `ServiceResponse<T>` | Standard `(StatusCodes, TResponse?)` return pattern |
| `StatusCodes` | 8-value enum: OK, BadRequest, Unauthorized, Forbidden, NotFound, Conflict, InternalServerError, NotImplemented, ServiceUnavailable |
| `ServiceLayer` | 6-value enum: Infrastructure(0), AppFoundation(100), ..., Extensions(500) |

**Documented in HELPERS**: Partially — §11 covers StatusCodes and ServiceLayer, §16 covers the plugin lifecycle (Initialize → Start → Running → Shutdown). IBannouService interface details remain here.

### Background Worker Helpers

| Type | Purpose |
|------|---------|
| `EventBatcher<TEntry>` | Mode 1: accumulating (append-all, ConcurrentBag) |
| `DeduplicatingEventBatcher<TKey, TEntry>` | Mode 2: deduplicating (last-write-wins, ConcurrentDictionary) |
| `EventBatcherWorker` | BackgroundService that flushes IFlushable[] per cycle |
| `WorkerErrorPublisher` | Scope-creating error event publisher for BackgroundService catch blocks |

**Documented in HELPERS**: Yes, §2 and §3. Complete and accurate.

### Other Services

| Type | Purpose |
|------|---------|
| `IEmailService` | Email abstraction (Console, SendGrid, SES, SMTP implementations) |
| `IPermissionRegistry` | Push-based permission matrix registration |
| `IControlPlaneServiceProvider` | Control plane health reporting for orchestrator |
| `IAccountDeletionCleanupRequired` | Marker: service stores account-owned data |
| `ICleanDeprecatedEntity` | Marker: Category B deprecation support |
| `IDeprecateAndMergeEntity` | Marker: Category A deprecation + merge |
| `IUnhandledExceptionDispatcher` | Composite exception handler dispatch |
| `LoggingUnhandledExceptionHandler` | Default handler (always first, logs at Error) |

---

## 2. Providers/ — DI Inversion Interfaces

All live in `bannou-service/Providers/`. Interface defined here, higher layer implements, lower layer discovers via `IEnumerable<T>`.

### Pull Providers (Always Distributed-Safe)

| Interface | Direction | Consumer | Purpose |
|-----------|-----------|----------|---------|
| `IVariableProviderFactory` | L4→L2 | Actor runtime | Create `IVariableProvider` for ABML expression evaluation |
| `IPrerequisiteProviderFactory` | L4→L2 | Quest service | Check dynamic prerequisites (skill, reputation, etc.) |
| `IBehaviorDocumentProvider` | L4→L2 | Actor runtime | Priority-ordered behavior document loading |
| `IBehaviorDocumentLoader` | — | Actor runtime | Aggregates multiple providers into one loader |
| `ISeededResourceProvider` | L2/L3/L4→L1 | Resource service | Embedded/static resource discovery and loading |
| `ITransitCostModifierProvider` | L4→L2 | Transit service | Speed/preference/risk modifiers for travel costs |
| `ILocalizationKeyValidator` | L1→L2+ | Entity services | Validate localization keys at creation time |
| `IServiceMappingReceiver` | L3→L0 | Mesh service | Orchestrator pushes routing table updates |

**Documented in HELPERS**: Yes, §4 provider table includes both `ILocalizationKeyValidator` and `IServiceMappingReceiver`.

### Push Listeners (Local-Only Fan-Out)

| Interface | Direction | Consumer | Distributed-Safe When |
|-----------|-----------|----------|----------------------|
| `ISeedEvolutionListener` | L2→L4 | Status, Divine, etc. | Reaction writes to Redis/MySQL |
| `ICollectionUnlockListener` | L2→L2 | Seed service | Always (both L2, co-located) |
| `IItemInstanceDestructionListener` | L2→L4 | Affix, etc. | Reaction writes to Redis/MySQL + orphan worker |
| `ISessionActivityListener` | L1→L1 | Permission service | Always (both L1, co-located) |

### Other Provider-Like Interfaces

| Interface | Purpose |
|-----------|---------|
| `IEntitySessionRegistry` | Entity→session mapping for client event routing |
| `IUnhandledExceptionHandler` | Plugin hook for unhandled exception dispatch |
| `EmbeddedResourceProvider` | Base class for assembly-embedded resource providers |
| `VariableProviderCacheBucket<TKey, TData>` | Cache composition helper with TTL, stale fallback, invalidation |
| `SeededResource` | Immutable record representing a loaded embedded resource |

**Documented in HELPERS**: Yes, §5. Constructor and `GetOrLoadAsync` signatures are accurate.

---

## 3. Events/ — Event Infrastructure

| Type | Purpose |
|------|---------|
| `IEventConsumer` | Application-level fan-out (multiple plugins handle same event topic) |
| `EventConsumer` | ConcurrentDictionary-backed implementation with failure isolation |
| `EventConsumerExtensions` | DI registration + `RegisterHandler<TService, TEvent>()` helper |
| `EventPublisherBase` | Abstract base for generated typed event publishers |
| `EventSubscriptionRegistry` | Static topic→Type mapping for NativeEventConsumerBackend deserialization |
| `EventTemplate` | Definition of ABML-publishable event templates |
| `EventTemplateRegistry` / `IEventTemplateRegistry` | Thread-safe singleton registry with validation |

**Documented in HELPERS**: Yes, §2 (IEventConsumer, error publishing). EventTemplate system undocumented.

---

## 4. Abml/ — ABML Execution Runtime

**Not in HELPERS** (infrastructure internals, not a plugin-facing pattern). HELPERS header cross-references the deep dive for ABML runtime coverage.

### Execution Engine (Abml/Execution/)

The tree-walking interpreter for ABML documents. Used by lib-actor's ActorRunner.

| Type | Purpose |
|------|---------|
| `IDocumentExecutor` / `DocumentExecutor` | Executes ABML documents from a start flow |
| `ExecutionContext` | Runtime state: document, scope, call stack, loaded imports, logs |
| `ExecutionResult` | Success/failure with return value and collected logs |
| `ActionResult` | Continue, Complete, Goto (tail call), Return, Error |
| `IActionHandler` / `IActionHandlerRegistry` | Pluggable action handler system |
| `ValueEvaluator` | Shared utility for evaluating `${...}` expressions vs literal values |

**Error handling chain**: Action-level → Flow-level → Document-level `on_error`, with `_error_handled` continuation flag.

**Built-in action handlers** (registered via `ActionHandlerRegistry.CreateWithBuiltins()`):

| Handler | ABML Action | Purpose |
|---------|-------------|---------|
| `CondHandler` | `cond:` | Conditional branching (if/else-if/else) |
| `ForEachHandler` | `for_each:` | Collection iteration with child scope |
| `RepeatHandler` | `repeat:` | Bounded repetition |
| `SetHandler` | `set:` | Variable assignment (current scope) |
| `LocalHandler` | `local:` | Local variable (shadows parent) |
| `GlobalHandler` | `global:` | Global variable (writes to root scope) |
| `ClearHandler` | `clear:` | Set variable to null |
| `GotoHandler` | `goto:` | Flow transfer (tail call, no return) |
| `CallHandler` | `call:` | Flow call (subroutine with return, `_result` capture) |
| `ReturnHandler` | `return:` | Return from current flow |
| `LogHandler` | `log:` | Emit log entry with expression interpolation |
| `NumericOperationHandler` | `increment:`/`decrement:` | Atomic numeric operations |
| `EmitEventHandler` | `emit_event:` | Publish typed events via EventTemplateRegistry |
| `DomainActionHandler` | `{any domain action}` | Routes to Intent Emitters (catch-all, registered last) |

### Channel Scheduler (Abml/Execution/Channel/)

Cooperative scheduling for multi-channel ABML execution (cinematic choreography).

| Type | Purpose |
|------|---------|
| `ChannelScheduler` | Round-robin execution with signal passing, sync points, deadlock detection |
| `ChannelState` | Per-channel state: scope, call stack, pending signals, wait state |
| `ChannelSignal` | Inter-channel signal with payload |
| `ChannelExecutionResult` | Multi-channel result with per-channel return values |

**Scheduling model**: Channels yield on `wait_for:` (signals) and `sync:` (barriers). Global timeout, per-wait timeout, and cycle-count limits prevent infinite execution.

### Cognition Pipeline (Abml/Cognition/)

5-stage perception processing pipeline for NPC decision-making. Configurable per-character via templates and overrides.

| Stage | Handler | Purpose |
|-------|---------|---------|
| 1. Filter | `FilterAttentionHandler` | Priority-weighted attention filtering with budget constraints |
| 2. Memory | `QueryMemoryHandler` | Retrieve relevant memories via `IMemoryStore` |
| 3. Significance | `AssessSignificanceHandler` | Weighted emotional/goal/relationship scoring |
| 4. Storage | `StoreMemoryHandler` | Persist significant experiences as memories |
| 5. Intention | `EvaluateGoalImpactHandler` + `TriggerGoapReplanHandler` | Goal impact assessment + urgency-based GOAP replanning |

**Supporting types**:
- `CognitionConfiguration` / `CognitionConstants`: Configurable thresholds (urgency bands, attention weights, significance weights, memory relevance)
- `IMemoryStore`: Abstraction for memory storage (keyword-based MVP, embedding-based future)
- `Perception`, `Memory`: Core data types
- `AttentionBudget`, `AttentionWeights`, `SignificanceWeights`: Per-character tuning
- `UrgencyBasedPlanningOptions`: Maps urgency (0-1) to GOAP search depth/timeout/nodes

**Threat fast-track**: High-urgency threats (>0.8) skip Stages 2-4 and go directly to intention formation (fight-or-flight response). Configurable per-character.

---

## 5. Behavior/ — Behavior System Interfaces

14 interfaces defining the behavior stacking, control gating, cinematic, and dialogue systems. Implementations live in lib-behavior (L4). bannou-service defines the contracts.

### Behavior Stacking

| Interface | Purpose |
|-----------|---------|
| `IBehaviorStack` | Manages layered behavior evaluation with priority merging |
| `IBehaviorLayer` | Single layer: evaluate, activate/deactivate, category/priority |
| `IBehaviorStackRegistry` | Entity→stack mapping |
| `IIntentStackMerger` | Per-channel emission merging (category priority × layer priority) |
| `IIntentEmitter` / `IIntentEmitterRegistry` | ABML action → Intent Channel emission translation |

### Control Gating

| Interface | Purpose |
|-----------|---------|
| `IControlGate` / `IControlGateRegistry` | Per-entity control source management (Behavior → Opportunity → Player → Cinematic) |

**Control sources** (ascending priority): Behavior(0), Opportunity(1), Player(2), Cinematic(3). Higher sources override lower. `FilterEmissions()` applies the gate to behavior stack output.

### Cinematic System

| Interface | Purpose |
|-----------|---------|
| `ICutsceneCoordinator` | Multi-participant cutscene session management |
| `ICutsceneSession` | Active session: sync points, input windows, state tracking |
| `ISyncPointManager` | Cross-entity synchronization barriers |
| `IInputWindowManager` | Timed input windows (QTE, choices) with timeout defaults |
| `IEntityResolver` | Semantic binding resolution (hero → entity ID) |
| `IEntityStateRegistry` | Entity state tracking for cinematic→behavior handoff |
| `ITemporalManager` | Time dilation for multiplayer QTE sequences |

### Dialogue System

| Interface | Purpose |
|-----------|---------|
| `IDialogueResolver` | 3-step resolution: override → localization → inline default |
| `IExternalDialogueLoader` | Load external YAML dialogue files with localizations and conditional overrides |
| `ILocalizationProvider` | Multi-locale text lookup with fallback chains |

### Cognition Pipeline Interfaces

| Interface | Purpose |
|-----------|---------|
| `ICognitionBuilder` | Build pipelines from templates with per-character overrides |
| `ICognitionPipeline` / `ICognitionStage` / `ICognitionHandler` | Execution interfaces |
| `ICognitionTemplate` (record) | Pipeline template definition (stages → handlers) |
| `ICognitionTemplateRegistry` | Template storage and lookup |

**Standard templates**: `humanoid-cognition-base`, `creature-cognition-base`, `object-cognition-base`.

---

## 6. Attributes/ — Service Registration & Validation

| Attribute | Purpose | Generated? |
|-----------|---------|------------|
| `BannouServiceAttribute` | Marks service classes for DI discovery (name, interface, lifetime, layer) | Yes |
| `BannouHelperServiceAttribute` | Marks helper services within a plugin | No (manual) |
| `BannouControllerAttribute` | Marks controller classes with route template | Yes |
| `ServiceConfigurationAttribute` | Marks config classes with env prefix binding | Yes |
| `IServiceAttribute` | Static discovery: `GetClassesWithAttribute<T>()` across all assemblies | N/A |
| `HeaderArrayAttribute` | Maps HTTP headers to/from properties | No |
| `ParameterizedTopicAttribute` | Marks generated parameterized topic publishers | Yes |
| `ResourceCleanupRequiredAttribute` | Declares required cleanup method names | Yes |
| `ConfigRequiredAttribute` | Required config property | Yes |
| `ConfigRangeAttribute` | Numeric range (min/max, exclusive bounds) | Yes |
| `ConfigStringLengthAttribute` | String length (min/max) | Yes |
| `ConfigPatternAttribute` | Regex pattern with timeout protection | Yes |
| `ConfigMultipleOfAttribute` | Numeric multiple-of constraint | Yes |
| `ConfigConstraintGroupAttribute` | Group membership for cross-property validation | Yes |
| `ConfigConstraintGroupDefinitionAttribute` | Group constraint definition (ExactlyOne, AllOrNone, SumEquals, etc.) | Yes |

**Documented in HELPERS**: Yes, §12. Accurate.

---

## 7. Plugins/ — Plugin Loading System

| Type | Purpose |
|------|---------|
| `IBannouPlugin` | Plugin interface: ConfigureServices, Initialize, Start, Running, Shutdown |
| `BaseBannouPlugin` | Abstract base with lifecycle hooks |
| `StandardServicePlugin<TService>` | Generic base eliminating lifecycle boilerplate |
| `PluginLoader` | Discovery, loading, DI registration, service resolution, lifecycle orchestration |

**PluginLoader** is the largest file (~1,800 lines). Key responsibilities:
- Assembly scanning from `plugins/` directory
- Layer-based load ordering (L0 first, L5 last)
- Infrastructure plugin requirement enforcement (state, messaging, mesh must load)
- 6-stage DI registration (plugin.ConfigureServices → auto-register services → helpers → clients → configs → event consumers)
- Service resolution and lifecycle (Initialize → Start → Running → Shutdown)
- Permission registration orchestration
- Variable provider validation against schema definitions

**Documented in HELPERS**: Yes, §16 (Plugin Loading System). Covers base classes, lifecycle, and registration attributes. Full PluginLoader internals remain here.

---

## 8. ServiceClients/ — Mesh Client Infrastructure

| Type | Purpose |
|------|---------|
| `IServiceClient` / `IServiceClient<TSelf>` | Base marker + fluent API for generated clients |
| `IServiceNavigator` | Aggregates all clients + session context + raw/prebound API execution |
| `ServiceNavigator.RawApi` | Implements raw JSON/byte API calls and prebound template execution |
| `DirectDispatchHelper` | Zero-serialization in-process dispatch for embedded/sidecar mode |
| `ServiceRequestContext` | AsyncLocal ambient context (session ID, correlation ID) |
| `ServiceRequestContextMiddleware` | Extracts context headers from incoming requests |
| `SessionIdForwardingHandler` | HTTP handler that forwards session ID to downstream calls |
| `ServiceClientExtensions` | DI registration helpers for typed HTTP clients |
| `ServiceClientsDependencyInjection` | Core infrastructure registration (telemetry, event consumer, ABML runtime, etc.) |
| `PreboundApiModels` | PreboundApiDefinition, RawApiResult, PreboundApiResult, batch modes |

**Documented in HELPERS**: Yes, §8 covers ServiceNavigator raw/prebound API and DirectDispatchHelper.

---

## 9. ClientEvents/ — WebSocket Event Publishing

| Type | Purpose |
|------|---------|
| `IClientEventPublisher` | Publish events to specific WebSocket sessions via `bannou-client-events` direct exchange |
| `MessageBusClientEventPublisher` | Implementation: session-specific routing keys, whitelist validation |
| `ClientEventNormalizer` | Fixes NSwag enum mangling (system_notification → system.notification) |
| `ClientEventsDependencyInjection` | DI registration |

**Architecture**: Uses dedicated `bannou-client-events` direct exchange (not the `bannou` fanout exchange). Routing key = `CONNECT_SESSION_{sessionId}`. Event name whitelist validation prevents unknown events from reaching clients.

**Documented in HELPERS**: Yes, §7. Accurate.

---

## 10. History/ — Shared History Helpers

| Type | Purpose |
|------|---------|
| `IBackstoryStorageHelper<TBackstory, TElement>` | Backstory element storage with merge/replace semantics + distributed locking |
| `BackstoryStorageHelper<TBackstory, TElement>` | Implementation with delegate-based configuration |
| `IDualIndexHelper<TRecord>` | Dual-index storage: primary (entity→records) + secondary (related→records) |
| `DualIndexHelper<TRecord>` | Implementation with distributed locking on primary key only |
| `CompressionHelper` | Gzip decompression for archived entity data |
| `PaginationHelper` + `PaginationResult<T>` | Offset-based pagination (defaults: 20/page, max 100) |
| `TimestampHelper` | Unix timestamp conversions (seconds/milliseconds) |
| `LockableResult<T>` | Result wrapper indicating lock acquisition status |
| `HistoryIndexData` | Generic index model (entity ID + record ID list) |

**Used by**: character-personality, character-history, character-encounter, realm-history.

**Documented in HELPERS**: Yes, §6. Signature and usage pattern are accurate.

---

## 11. Remaining Subsystems

### Configuration/ (AppConfiguration, IServiceConfiguration)

- `AppConfiguration`: Master config with layer enables, JWT, heartbeat, ports, app-id
- `BaseServiceConfiguration`: Common base (ForceServiceId, ServiceEnabled)
- `IServiceConfiguration`: Config building from .env + JSON + env vars + CLI args, with UPPER_SNAKE_CASE→PascalCase normalization and comprehensive validation (required, range, length, pattern, multipleOf, constraint groups)

### Controllers/ (Framework)

- `IBannouController`: Controller discovery via attribute reflection
- `HealthController`: GET/POST `/health` endpoint
- `ServiceMappingDebugController`: Debug endpoint for routing inspection
- `ApiMessage` / `ApiRequest` / `ApiResponse`: Header array binding system for HTTP header propagation

### Protocol/ (GuidGenerator)

Client-salted GUID generation with version-tagged UUIDs (v5 for services, v7 for shortcuts). SHA256-based, deterministic for same inputs. Server salt MUST come from configuration (not per-instance random).

### ResourceTemplates/

- `ResourceTemplateBase`: Base class for ABML resource template validation (sourceType → namespace → valid paths)
- `ResourceTemplateRegistry` / `IResourceTemplateRegistry`: Singleton registry with dual indexes (by sourceType, by namespace)

Plugins register templates during `OnRunningAsync`. The ABML semantic analyzer validates resource access paths against these templates at compile time.

**Not in HELPERS.** Gap.

### Archives/ (IResourceArchive)

Marker interface for resource archive types consumed by the storyline SDK's ArchiveExtractor. Defines: ResourceId, ResourceType, ArchivedAt, SchemaVersion, NestedArchives.

### Storage/ (IAssetStorageProvider)

Abstraction for binary asset storage (MinIO, S3, R2, Azure Blob, local filesystem). Pre-signed URLs, multipart upload (client-side and server-side streaming), versioning, copy, metadata.

### Meta/ (MetaResponseBuilder)

Runtime schema introspection for generated Controller.Meta.cs files. Builds endpoint-info, request-schema, response-schema, and full-schema responses from pre-embedded JSON.

### Utilities/

- `TemplateSubstitutor`: `{{variable}}` substitution in JSON templates (dot-path, array indexing, type-preserving)
- `ResponseValidator`: Contract response validation (status codes, JsonPath conditions, three-outcome model)

### Top-Level

- `Program.cs`: Application bootstrap — plugin loading, WebSocket setup, JWT auth, heartbeat management, service mapping import, permission registration. ~735 lines.
- `EnumMapping.cs`: Generic enum-to-enum and string-to-enum mapping with `MapByName`/`MapByNameOrDefault`/`TryMapByName`
- `Enums.cs`: StatusCodes, ServiceLayer, HttpMethodTypes, AppRunningStates
- `ExtensionMethods.cs`: Slug generation, bearer token extraction, type introspection, status code conversion
- `AppConstants.cs`: DEFAULT_APP_NAME, infrastructure constants, env var names, BROADCAST_GUID
- `MetadataHelper.cs`: JsonElement↔Dictionary conversion for opaque client metadata
- `Usings.cs`: Global using declarations

---

## HELPERS-AND-COMMON-PATTERNS Sync Status

Tracking what's documented in HELPERS vs. what lives only in this deep dive.

| Item | Status | Notes |
|------|--------|-------|
| ABML Execution Runtime | **Deep dive only** | Infrastructure internals; HELPERS cross-references here |
| Cognition Pipeline | **Deep dive only** | Infrastructure internals; HELPERS cross-references here |
| Behavior System Interfaces | **Deep dive only** | 14 interfaces; consumed by lib-behavior, not typical plugin dev |
| ResourceTemplate system | **Deep dive only** | ABML semantic analysis only |
| EventTemplate system | **Deep dive only** | ABML event publishing only |
| IMessageTap | **Deep dive only** | Event forwarding between exchanges |
| ServiceHeartbeatManager | **Deep dive only** | Periodic heartbeat publishing |
| Plugin System (PluginLoader) | **Synced** | HELPERS §16 covers base classes, lifecycle, attributes. Full internals here. |
| DirectDispatchHelper | **Synced** | HELPERS §8 |
| ServiceNavigator raw/prebound API | **Synced** | HELPERS §8 |
| ILocalizationKeyValidator | **Synced** | HELPERS §4 provider table |
| IServiceMappingReceiver | **Synced** | HELPERS §4 provider table |
| VariableProviderCacheBucket | **Synced** | HELPERS §5 constructor and GetOrLoadAsync corrected |
| CompressionHelper | **Synced** | HELPERS §6 signature corrected |

---

*This document is a navigation map. For usage patterns and code examples, see [HELPERS-AND-COMMON-PATTERNS.md](reference/HELPERS-AND-COMMON-PATTERNS.md). For architectural rules, see [TENETS.md](reference/TENETS.md).*
