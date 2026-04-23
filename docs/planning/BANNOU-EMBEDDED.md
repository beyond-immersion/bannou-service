# Bannou Embedded Mode: In-Process Service Invocation

> **Type**: Design
> **Status**: Aspirational
> **Created**: 2026-02-24
> **Last Updated**: 2026-03-09
> **North Stars**: #4
> **Related Plugins**: Mesh, Connect, State, Messaging

## Summary

Describes how Bannou services can run fully in-process without HTTP, WebSocket, or external infrastructure, enabling embedded deployment on Android, desktop, and console. The investigation found that infrastructure libs already support embedded backends, the ASP.NET Core coupling is phantom, and the total implementation is approximately a 2-3 day effort across plugin system decoupling, client generation template changes, and a new embedded host composition root. No implementation has been started.

---

## Problem Statement

Bannou's current inter-service invocation path is:

```
ICollectionClient.CreateEntryTemplateAsync(request)
  → IMeshInvocationClient (serialize to JSON, build HttpRequestMessage)
    → SocketsHttpHandler → TCP loopback → Kestrel
      → ASP.NET Core routing → model binding
        → CollectionController (error boundary, telemetry)
          → ICollectionService.CreateEntryTemplateAsync(request)
            → return (StatusCodes.OK, response)
          ← controller translates to HTTP 200 + JSON body
        ← Kestrel writes HTTP response
      ← SocketsHttpHandler reads response
    ← IMeshInvocationClient (deserialize JSON to response)
  ← client returns response or throws ApiException
```

For an in-process embedded scenario (Android game, desktop self-hosted), this entire HTTP round-trip is waste. The goal is to collapse it to:

```
ICollectionClient.CreateEntryTemplateAsync(request)
  → resolve ICollectionService from DI (same scope)
    → ICollectionService.CreateEntryTemplateAsync(request)
      → return (StatusCodes.OK, response)
    ← translate (StatusCodes, TResponse?) → TResponse or throw ApiException
  ← client returns response or throws ApiException
```

Zero JSON serialization. Zero HTTP. Zero network I/O. Same `ICollectionClient` interface — callers don't know or care.

---

## Investigation Findings

### 1. Infrastructure Libs: Already Support Embedded Mode

Every infrastructure layer has a working embedded backend. No code changes needed.

| Infrastructure | Server Backend | Embedded Backend | Status |
|---------------|---------------|-----------------|--------|
| **lib-state** (persistence) | Redis + MySQL | InMemory + SQLite | Works today (`UseSqlite=true`) |
| **lib-messaging** (pub/sub) | RabbitMQ | DirectDispatchMessageBus | Works today (`UseDirectDispatch=true`) |
| **lib-state** (distributed locks) | Redis SETNX | ConcurrentDictionary fallback | Works today (single-process safe) |
| **lib-state** (sorted sets) | Redis sorted sets | InMemory sorted set impl | Full feature parity |
| **lib-state** (LINQ queries) | MySQL via EF Core | SQLite via EF Core | Full feature parity |
| **lib-mesh** (endpoint discovery) | Redis-backed registry | LocalMeshStateManager | Works today (`UseLocalRouting=true`) |

**Configuration for embedded mode:**
```csharp
StateStoreFactoryConfiguration:
  UseSqlite = true           // MySQL stores → SQLite; Redis stores → InMemory
  SqliteDataPath = "./data"  // Persistent storage location

MessagingServiceConfiguration:
  UseDirectDispatch = true   // RabbitMQ → direct IEventConsumer dispatch (zero overhead)

MeshServiceConfiguration:
  UseLocalRouting = true     // No Redis endpoint registry
```

**IRedisOperations**: Returns null when Redis unavailable. Both services that use it (lib-permission, lib-mesh circuit breaker) already check for null and fall back gracefully. No changes needed.

### 2. ASP.NET Core Coupling: Thinner Than It Appears

**The phantom coupling**: Every plugin overrides `ConfigureApplication(WebApplication app)`, but every single one does the same thing:

```csharp
_serviceProvider = app.Services;  // That's it. That's the whole method.
```

No plugin uses HTTP routing, middleware, Kestrel, or any other ASP.NET Core feature from the `WebApplication` parameter. The only exception is `TelemetryServicePlugin`, which maps a Prometheus metrics endpoint — irrelevant in embedded mode.

**The real dependency chain:**

```
IBannouPlugin.ConfigureApplication(WebApplication app)     ← ONLY ASP.NET Core type
  └── BaseBannouPlugin.ConfigureApplication(WebApplication app)
       └── StandardServicePlugin.ConfigureApplication(WebApplication app)
            └── { ServiceProvider = app.Services; }        ← only uses IServiceProvider
                 └── 21 plugin overrides: same pattern
```

**The fix is mechanical**: Change `ConfigureApplication(WebApplication app)` → `ConfigureApplication(IServiceProvider services)` across:

| File | Change |
|------|--------|
| `bannou-service/Plugins/IBannouPlugin.cs` | Interface signature: `WebApplication` → `IServiceProvider` |
| `bannou-service/Plugins/BaseBannouPlugin.cs` | Base implementation: same |
| `bannou-service/Plugins/StandardServicePlugin.cs` | `ServiceProvider = app.Services` → `ServiceProvider = services` |
| `bannou-service/Plugins/PluginLoader.cs` | `ConfigureApplication(WebApplication app)` → `ConfigureApplication(IServiceProvider services)` |
| 21 plugin `*ServicePlugin.cs` files | `app.Services` → `services` (1-line change each) |
| `bannou-service/Program.cs` | `PluginLoader.ConfigureApplication(webApp)` → `PluginLoader.ConfigureApplication(webApp.Services)` |

**No conditional compilation needed.** The refactored code works identically for both web and embedded modes — it's just receiving `IServiceProvider` instead of `WebApplication`. The web mode passes `webApp.Services`, the embedded mode passes `host.Services`.

**What about generated controllers?** They reference `ControllerBase`, `[HttpPost]`, `[FromBody]` — all ASP.NET Core. But in embedded mode, controllers are simply never loaded. The `PluginLoader` already conditionally loads assemblies. Controllers exist in the assembly but are never instantiated because there's no ASP.NET Core routing pipeline to discover them. The controller types compile (they're in the same assembly as the service), but they're dead code in embedded mode — no runtime cost.

**Assembly-level concern**: Plugin `.csproj` files may transitively reference `Microsoft.AspNetCore.App` through the controller types. If tree-shaking/trimming is needed for Android APK size, the generated controller files could be excluded from an embedded-specific build configuration via `<Compile Remove="Generated/*Controller*.cs" />` in the `.csproj`. This is a build optimization, not a code change.

### 3. Generated Clients: Inline Direct Dispatch Is Viable

**Signature mismatch between client and service interfaces:**

```csharp
// Client interface (generated from schema)
Task<EntryTemplateResponse> CreateEntryTemplateAsync(CreateEntryTemplateRequest body, CancellationToken ct);
// Returns TResponse directly, throws ApiException on error

// Service interface (generated from schema)
Task<(StatusCodes, EntryTemplateResponse?)> CreateEntryTemplateAsync(CreateEntryTemplateRequest body, CancellationToken ct);
// Returns (StatusCodes, TResponse?) tuple, never throws for business errors
```

The adapter between these two is trivial and well-understood:

```csharp
var (status, result) = await service.MethodAsync(body, ct);
if (status == StatusCodes.OK || status == StatusCodes.Created)
    return result!;
throw new ApiException("Service returned error", (int)status, null, null, null);
```

**Client DI facts:**
- Generated clients are registered as **Scoped** (via `ServiceClientExtensions`)
- Service implementations are registered as **Scoped** (via `[BannouService]` attribute)
- Scoped → Scoped resolution within the same scope is safe
- Clients currently receive `IMeshInvocationClient`, `IServiceAppMappingResolver`, `ILogger`
- Clients are `partial` (can add fields/methods in companion file)
- Client methods are `virtual` (can be overridden in subclass)

**The approach: modify client generation template to add inline dispatch.**

Each generated client method becomes:

```csharp
public virtual async Task<EntryTemplateResponse> CreateEntryTemplateAsync(
    CreateEntryTemplateRequest body, CancellationToken ct = default)
{
    // Direct dispatch path: resolve service and call directly
    if (_directDispatch)
    {
        return await InvokeDirectAsync<CreateEntryTemplateRequest, EntryTemplateResponse>(
            body,
            static (svc, req, c) => ((ICollectionService)svc).CreateEntryTemplateAsync(req, c),
            ct);
    }

    // Existing HTTP mesh invocation path (unchanged)
    var urlBuilder_ = new StringBuilder();
    urlBuilder_.Append("collection/entry-template/create");
    // ... existing generated code ...
}
```

Where `InvokeDirectAsync` is a shared helper (written once in a partial companion or base class):

```csharp
private async Task<TResponse> InvokeDirectAsync<TRequest, TResponse>(
    TRequest request,
    Func<object, TRequest, CancellationToken, Task<(StatusCodes, TResponse?)>> serviceMethod,
    CancellationToken ct)
{
    // Resolve service from DI scope
    var service = _serviceProvider!.GetRequiredService<IServiceInterface>();

    var (status, result) = await serviceMethod(service, request, ct);

    return status switch
    {
        StatusCodes.OK or StatusCodes.Created => result!,
        _ => throw new ApiException(
            $"Service returned {status}", (int)status, null, null, null)
    };
}
```

**What this preserves from the controller boundary:**
- Status code → exception translation (matching HTTP client behavior)
- Scoped service lifetime (resolved from same DI scope)

**What this intentionally drops:**
- JSON serialization/deserialization (zero-copy: same objects passed by reference)
- HTTP request/response construction
- ASP.NET Core routing and model binding
- Controller-level telemetry spans (can add lightweight spans in the dispatch helper if desired)
- Controller-level error event publishing (service-level errors still work; the catch-all boundary moves to the caller)

**Decision logic for `_directDispatch`:**
- Set once at construction time from configuration (not checked per-call)
- Configured via `IServiceConfiguration` or a dedicated embedded mode flag
- When true: client constructor also receives `IServiceProvider` from DI

### 4. Background Services: Work Without ASP.NET Core

All background workers use `Microsoft.Extensions.Hosting.BackgroundService`, which is **not** an ASP.NET Core type. It works with `Microsoft.Extensions.Hosting.Host` (the generic host).

Confirmed workers across relevant services: `CurrencyAutogainTaskService`, `ContractExpirationService`, `SubscriptionExpirationService`, `SeedDecayWorkerService`, `EntityPresenceCleanupWorker`, and 13+ others.

All use the same pattern:
1. `ExecuteAsync` loop with `Task.Delay`
2. Distributed lock acquisition (InMemory fallback works)
3. Queryable state store access (SQLite works)

**For embedded mode**: These register into `IServiceCollection` and run via the generic `IHost`. No ASP.NET Core hosting needed.

### 5. Services That Don't Apply in Embedded Mode

| Service | Why Skip | Impact |
|---------|----------|--------|
| **Connect** | WebSocket gateway — game IS the client | None (no external connections) |
| **Auth** | JWT/OAuth — single-player, no external auth | Skip or stub (local identity) |
| **Mesh** (HTTP API) | Service discovery — everything is in-process | Skip (clients dispatch directly) |
| **Telemetry** | OpenTelemetry export — no collector target | NullTelemetryProvider (already exists) |
| **Orchestrator** | Deployment management | Not applicable |
| **Website** | Browser-facing | Not applicable |
| **Voice** | WebRTC rooms | Not applicable |
| **Broadcast** | Streaming integration | Not applicable |

---

## Scope of Changes

### Layer 1: Plugin System Decoupling (prerequisite, no behavior change)

**Goal**: Remove phantom `WebApplication` coupling so plugins work with any `IServiceProvider`.

| Change | Files | Complexity |
|--------|-------|-----------|
| `IBannouPlugin.ConfigureApplication` signature | 1 | Trivial |
| `BaseBannouPlugin.ConfigureApplication` implementation | 1 | Trivial |
| `StandardServicePlugin.ConfigureApplication` implementation | 1 | Trivial |
| `PluginLoader.ConfigureApplication` + stored field type | 1 | Trivial |
| 21 plugin `ConfigureApplication` overrides | 21 | Mechanical (`app.Services` → `services`) |
| `Program.cs` call site | 1 | Trivial (`.ConfigureApplication(webApp.Services)`) |
| **Total** | **~26 files** | **~30 minutes of mechanical refactoring** |

**Risk**: Zero. Exact same `IServiceProvider` instance passed. Behavior is identical.

### Layer 2: Client Generation Template Change (core feature)

**Goal**: Generated clients check a flag and dispatch directly to the service interface when in embedded mode.

| Change | Files | Complexity |
|--------|-------|-----------|
| NSwag client template (`Client.Class.liquid`) | 1 template | Medium — add direct dispatch branch per method |
| Client constructor template | Same template | Add optional `IServiceProvider` parameter |
| Service client registration (`ServiceClientExtensions.cs`) | 1 | Pass `IServiceProvider` when in embedded mode |
| Configuration flag | 1 schema + 1 generated config | Add `EmbeddedMode` to mesh or app config |
| Shared dispatch helper | 1 new file or partial | `InvokeDirectAsync` adapter method |
| Regenerate all clients | `scripts/generate-all-services.sh` | Run once after template change |
| **Total** | **~3 manual files + 1 template + regeneration** | **~1-2 days of template work** |

**Note**: This requires modifying files in `scripts/` (generation template). Per CLAUDE.md, this needs explicit approval since generation scripts are frozen.

### Layer 3: Embedded Host (composition root)

**Goal**: A `Program.cs`-equivalent that boots Bannou without ASP.NET Core.

```csharp
// EmbeddedBannouHost.cs — the Android/desktop entry point
public class EmbeddedBannouHost
{
    private IHost _host;
    private PluginLoader _pluginLoader;

    public async Task StartAsync(EmbeddedConfiguration config)
    {
        // 1. Discover and load plugins (same PluginLoader)
        _pluginLoader = new PluginLoader(...);
        await _pluginLoader.DiscoverAndLoadPluginsAsync();

        // 2. Build generic host (NOT WebApplication)
        var builder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Register state, messaging, mesh in embedded mode
                services.AddSingleton(new StateStoreFactoryConfiguration
                {
                    UseSqlite = true,
                    SqliteDataPath = config.DataPath
                });
                services.AddSingleton(new MessagingServiceConfiguration
                {
                    UseInMemory = true
                });

                // Register all service clients with embedded dispatch
                services.AddBannouServiceClients(embedded: true);

                // Let plugins register their services
                _pluginLoader.ConfigureServices(services);
            });

        _host = builder.Build();

        // 3. Initialize plugins (same lifecycle, just IServiceProvider instead of WebApplication)
        _pluginLoader.ConfigureApplication(_host.Services);
        await _pluginLoader.InitializeAsync();
        await _pluginLoader.StartAsync();
        await _pluginLoader.InvokeRunningAsync();

        // 4. Start hosted services (background workers)
        await _host.StartAsync();
    }

    // Game code resolves clients from DI:
    public T GetClient<T>() where T : class
    {
        using var scope = _host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    public async Task StopAsync()
    {
        await _pluginLoader.ShutdownAsync();
        await _host.StopAsync();
    }
}
```

| Change | Files | Complexity |
|--------|-------|-----------|
| `EmbeddedBannouHost` class | 1 new file | Medium |
| `EmbeddedConfiguration` model | 1 new file | Simple |
| **Total** | **2 files** | **~half day** |

### Layer 4: Service Compatibility Verification

No changes needed for service implementations. All core game services work with SQLite + InMemory backends. Verified services for the Defenders use case:

| Service | MySQL → SQLite | Redis → InMemory | Locks → InMemory | Workers | Verdict |
|---------|---------------|-----------------|-------------------|---------|---------|
| Account | Yes | N/A | Yes | No | Works |
| Contract | Yes | Yes (cache) | Yes | Yes (expiration) | Works |
| Resource | Yes | Yes (cache) | Yes | No | Works |
| Permission | Yes | Yes (sessions) | Yes | No | Works |
| Character | Yes | Yes (cache) | Yes | No | Works |
| Species | Yes | N/A | N/A | No | Works |
| Realm | Yes | Yes (cache) | N/A | No | Works |
| Location | Yes | Yes (entity sets) | Yes | Yes (cleanup) | Works |
| Currency | Yes | Yes (cache) | Yes | Yes (autogain) | Works |
| Item | Yes | Yes (cache) | Yes | No | Works |
| Inventory | Yes | Yes (cache) | Yes | No | Works |
| Quest | Yes | Yes (cache) | Yes | No | Works |
| Seed | Yes | Yes (cache) | Yes | Yes (decay) | Works |
| Collection | Yes | Yes (cache) | Yes | No | Works |
| Worldstate | Yes | Yes (cache) | N/A | No | Works |
| Subscription | Yes | Yes (cache) | N/A | Yes (expiration) | Works |
| Game Service | Yes | N/A | N/A | No | Works |
| Workshop | Yes | Yes (cache) | Yes | Yes (materialization) | Works |
| Status | Yes | Yes (cache) | Yes | No | Works |

---

## Total Scope Summary

| Layer | What | Files Changed | New Files | Effort |
|-------|------|--------------|-----------|--------|
| 1. Plugin decoupling | `WebApplication` → `IServiceProvider` | ~26 | 0 | Half day |
| 2. Client generation | Direct dispatch in template | ~3 + 1 template | 1 helper | 1-2 days |
| 3. Embedded host | Composition root without ASP.NET Core | 0 | 2 | Half day |
| 4. Service verification | (None — already works) | 0 | 0 | 0 |
| **Total** | | **~29 changed** | **3 new** | **~2-3 days** |

Plus regeneration of all clients (automated, ~45 files of generated output).

---

## iOS / NativeAOT Support

Embedded Bannou must ship on iOS for Godot-based consumer games (committed target — per the 2026-04-22 Defenders of Ba'hara engine pivot; see GitHub issue [#724](https://github.com/beyond-immersion/bannou-service/issues/724) for the tracked Bannou-side work). iOS introduces AOT constraints that Android and PC do not:

### The constraint

- **Apple prohibits JIT** on iOS. All managed code must be AOT-compiled.
- **Godot 4.6 iOS uses NativeAOT** (not Mono AOT). NativeAOT trimming is aggressive — types and members not statically reachable are stripped from the shipped binary.
- **Runtime reflection over trimmed types fails** — whatever patterns pass under CoreCLR (server / PC / Android) may throw or silently return empty results under NativeAOT.
- Stride consumers (Stride 4.3 today) use Mono AOT for iOS, which is more permissive than NativeAOT — basic reflection works with linker preservation. **If the PluginLoader refactor satisfies NativeAOT, Mono AOT is automatically satisfied.** Tackling the stricter target first is strategic.

### Current blocker

The existing `PluginLoader` uses `Assembly.LoadFrom(path)` with runtime-discovered paths to load plugin assemblies. This is **AOT-blocking under both NativeAOT and Mono AOT** — the compiler cannot pre-compile code in an assembly whose identity is not known at build time.

**See T34 in [`docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md`](../reference/tenets/IMPLEMENTATION-BEHAVIOR.md) for the full Bannou AOT tenet.** Forbidden patterns in `bannou-service/` and `plugins/*/` code paths include: `Assembly.LoadFrom` with dynamic paths, `Reflection.Emit`, `Expression.Compile()` in hot paths, Roslyn scripting, `MakeGenericType`/`MakeGenericMethod` with runtime-discovered types, and runtime generic construction generally.

### Required refactors

Until the Bannou-side PluginLoader refactor (issue [#724](https://github.com/beyond-immersion/bannou-service/issues/724)) ships, embedded Bannou cannot ship on iOS for any consumer:

1. **Source-generated plugin registration manifest** — replace runtime `Assembly.LoadFrom` discovery with a build-time source generator that produces a static `PluginRegistrationManifest.g.cs` containing all `[BannouService]`-marked types. Issue #724 tracks the design options (see the issue for Option A/B/C/D tradeoffs).
2. **Trimming-safe annotations on generated controller entry points** — even though controllers are dead code in embedded mode (never instantiated in-process), their types exist in the assembly. NativeAOT trimming must preserve any type whose runtime-reachable code path includes it. `[DynamicallyAccessedMembers]` annotations on relevant types ensure trimming keeps what's needed.
3. **Audit sweep** — the rest of the codebase must be audited for AOT-blocking patterns per the Phase 3 section of issue #724.

### Layer 5 preview (iOS / NativeAOT specifics)

Once issue #724 lands:

| Change | Files | Notes |
|--------|-------|-------|
| Source-gen plugin manifest | 1 source generator + generated output | Replaces `Assembly.LoadFrom` dispatch |
| `[DynamicallyAccessedMembers]` annotations on source-generated entry points | generated output | Keeps trimmer honest |
| Per-consumer iOS build template | consumer-side (Godot export preset) | Consumer responsibility; Bannou verifies support |
| CI iOS AOT compilation pass | CI runner on macOS | Catches AOT regressions early |

### What this affects

- **Godot consumers (Defenders of Ba'hara)**: iOS ship gated on #724 — treat as medium-term roadmap, not immediate blocker for Android/PC.
- **Stride consumers** (future): also benefit — Mono AOT consumers inherit a NativeAOT-clean PluginLoader automatically.
- **Unity / Unreal future integrations**: face looser constraints by construction.

The refactor is validated once against the strictest target (NativeAOT) and all consumer paths benefit.

---

## What This Does NOT Cover

1. **Android-specific build configuration**: .NET for Android project setup, APK packaging, assembly trimming. These are Stride/Godot/MAUI concerns, not Bannou concerns.

2. **Sync layer for online/offline**: The Defenders proposal's Pattern B/C sync architecture (local-first with cloud sync on reconnect). This is game-side logic that uses Bannou as a local or remote backend interchangeably.

3. **Selective service loading**: PluginLoader already supports `{SERVICE}_SERVICE_ENABLED=false` per service. No changes needed — just configure which services to load.

4. **Test infrastructure**: How to test embedded mode. The existing unit test pattern (mock service interfaces) works identically. Integration tests would need an `EmbeddedBannouHost` equivalent in the test harness.

---

## Key Insight

The investigation revealed that Bannou is **much closer to embedded-ready than expected**:

- Infrastructure libs already have full embedded backends (SQLite, InMemory, InMemory locks)
- The ASP.NET Core coupling is phantom — plugins only need `IServiceProvider`, not `WebApplication`
- Generated controllers are dead code in embedded mode (never instantiated, no runtime cost)
- The only real gap is the HTTP invocation path in generated clients, which is a template change
- No conditional compilation needed anywhere in the current codebase

The "Satisfactory improvement" — going from "run your own local server" to "everything in-process" — is a 2-3 day template/infrastructure change, not an architectural overhaul.
