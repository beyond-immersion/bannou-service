# Bannou Embedded Mode: In-Process Service Invocation

> **Status**: Investigation complete, implementation not started
> **Date**: 2026-02-24
> **Context**: Enabling Bannou services to run fully in-process (e.g., Android game) without HTTP, WebSocket, or external infrastructure

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
| **lib-messaging** (pub/sub) | RabbitMQ | InMemoryMessageBus | Works today |
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
  UseInMemory = true         // RabbitMQ → in-process pub/sub

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

## What This Does NOT Cover

1. **Android-specific build configuration**: .NET for Android project setup, APK packaging, assembly trimming. These are Stride/MAUI concerns, not Bannou concerns.

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
