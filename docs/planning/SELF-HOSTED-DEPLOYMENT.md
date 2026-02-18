# Self-Hosted Deployment: Single-Player and Local Server Experiences

> **Status**: Design
> **Created**: 2026-02-18
> **Author**: Lysander (design) + Claude (analysis)
> **Category**: Cross-cutting architecture (deployment, infrastructure)
> **Related Services**: State (L0), Messaging (L0), Mesh (L0), Workshop (L4), all consumer services
> **Related Plans**: [LOCATION-BOUND-PRODUCTION.md](LOCATION-BOUND-PRODUCTION.md), [MEMENTO-INVENTORIES.md](MEMENTO-INVENTORIES.md)
> **Related Docs**: [BANNOU-DESIGN.md](../BANNOU-DESIGN.md), [VISION.md](../reference/VISION.md), [PLAYER-VISION.md](../reference/PLAYER-VISION.md)
> **Related Issues**: [#442](https://github.com/beyond-immersion/bannou-service/issues/442) (SQLite state store backend), [#409](https://github.com/beyond-immersion/bannou-service/issues/409) (Game Engine Actor transport)

---

## Executive Summary

Bannou's architecture -- schema-first code generation, plugin loading, in-memory infrastructure backends, environment-driven service selection, and lazy evaluation -- already supports self-hosted deployment with zero code changes. A game built on Bannou can ship as a local dedicated server alongside the game client (the Satisfactory model), giving players a single-player or LAN multiplayer experience powered by the full service stack.

The production chain composability documented in [LOCATION-BOUND-PRODUCTION.md](LOCATION-BOUND-PRODUCTION.md) demonstrates this concretely: a Factorio-style factory game is entirely described by Workshop blueprints (seed data), Inventory containers, Location hierarchies, Transit connections, and ABML behaviors for NPC workers -- no game-specific server code. The game engine provides rendering and input. Bannou provides everything between "player clicks" and "world state changes." Different games are different seed data sets on identical infrastructure.

Three investments unlock this as a shipping product: a SQLite state store backend for local persistence ([#442](https://github.com/beyond-immersion/bannou-service/issues/442)), SDK convenience for local-server connection defaults, and documentation. An optional fourth investment -- lib-mesh in-process routing -- eliminates HTTP overhead entirely for .NET engines.

---

## The Satisfactory Model

The recommended deployment model mirrors Satisfactory, Valheim, Palworld, and other games that ship a dedicated server binary:

```
Game Installation Directory
├── Game.exe                  (game client / rendering engine)
├── BannouServer/
│   ├── bannou-service        (the Bannou binary)
│   ├── plugins/              (subset of compiled plugins)
│   │   ├── lib-state/
│   │   ├── lib-messaging/
│   │   ├── lib-mesh/
│   │   ├── lib-workshop/
│   │   ├── lib-inventory/
│   │   ├── lib-item/
│   │   ├── lib-location/
│   │   └── ...               (game-specific plugin subset)
│   ├── data/                 (SQLite databases, save files)
│   ├── seeds/                (game-specific seed data)
│   └── server.env            (self-hosted configuration)
└── SDK/
    └── (generated client libraries for game engine)
```

### Player Experience

1. Player launches game
2. Game starts BannouServer as a background process with `server.env` configuration
3. Game waits for BannouServer readiness (health endpoint or stdout signal)
4. Game client connects via WebSocket to `localhost:5012` using the generated SDK
5. Normal gameplay -- all service calls go through the SDK to the local server
6. On game exit, BannouServer persists state to SQLite and shuts down gracefully

For multiplayer, the same server accepts remote connections:

1. Host player starts game normally (local server spins up)
2. Host shares their IP/port (or uses a relay service)
3. Remote players connect to the host's server instead of localhost
4. Same Bannou binary, same services, same game logic -- now multiplayer

### Configuration

```bash
# server.env -- self-hosted single-player configuration
SERVICES_ENABLED=true
STATE_USE_SQLITE=true
STATE_SQLITE_DATA_PATH=./data
BANNOU_HEARTBEAT_ENABLED=false
BANNOU_HTTP_WEB_HOST_PORT=5012

# Disable services not needed for the specific game
TELEMETRY_SERVICE_DISABLED=true
ORCHESTRATOR_SERVICE_DISABLED=true
WEBSITE_SERVICE_DISABLED=true
DOCUMENTATION_SERVICE_DISABLED=true
VOICE_SERVICE_DISABLED=true
BROADCAST_SERVICE_DISABLED=true
# ... etc based on game requirements
```

---

## Why This Works Today

### In-Memory Infrastructure

All three L0 infrastructure dependencies are eliminable:

| Infrastructure | Cloud Mode | Self-Hosted Mode | Status |
|---------------|-----------|-----------------|--------|
| **State (L0)** | Redis + MySQL | `STATE_USE_INMEMORY=true` or `STATE_USE_SQLITE=true` ([#442](https://github.com/beyond-immersion/bannou-service/issues/442)) | InMemory: exists. SQLite: planned. |
| **Messaging (L0)** | RabbitMQ | `InMemoryMessageBus` (local pub/sub) | Exists |
| **Mesh (L0)** | YARP + Redis service discovery | Default "bannou" omnipotent routing (all services co-located) | Exists |

With `BANNOU_HEARTBEAT_ENABLED=false`, no Orchestrator connectivity is required. The default mesh routing sends all inter-service calls to the local process. The InMemoryMessageBus delivers events to local subscribers. The only gap is durable persistence, which #442 (SQLite) addresses.

### Selective Plugin Loading

PluginLoader already supports fine-grained service selection:

```bash
# Enable everything, then disable what you don't need
SERVICES_ENABLED=true
VOICE_SERVICE_DISABLED=true

# Or disable everything, then enable what you need
SERVICES_ENABLED=false
STATE_SERVICE_ENABLED=true
MESSAGING_SERVICE_ENABLED=true
MESH_SERVICE_ENABLED=true
INVENTORY_SERVICE_ENABLED=true
WORKSHOP_SERVICE_ENABLED=true
# ...
```

A Factorio-style game might need ~15 plugins out of 45+, reducing memory footprint significantly.

### Lazy Evaluation

Workshop's lazy evaluation is the killer feature for self-hosted deployment. Production materializes retroactively from rate segments and elapsed game time. A player who closes the game and returns a week later finds all production computed on the next query -- no server ticks needed during absence.

This is mechanically superior to Factorio's "world pauses when you exit" model. The production math runs on game-time (Worldstate), not real-time. The server doesn't need to be running for production to "happen" -- it just needs to compute the result when asked.

### HTTP Is Fine

The entire system is built on OpenAPI schemas. HTTP is not overhead to eliminate -- it's the architectural foundation. Localhost HTTP round-trips add ~0.5-2ms per call. For factory games, farming games, and most game interactions, this is invisible. A player clicking "smelt iron" waits for Workshop to process the request -- the 1ms HTTP round-trip to localhost is noise compared to the meaningful game-time production duration.

The WebSocket gateway (Connect service) provides persistent connections with binary routing headers. The generated SDK connects to `localhost:5012` instead of a remote server. Everything else is identical to the cloud deployment path.

---

## The Architecture in Self-Hosted Mode

```
┌─────────────────────────────────────────────────────┐
│ Game Client (Rendering Engine)                       │
│                                                      │
│  Generated SDK ──WebSocket──► localhost:5012          │
│  (typed service calls)       (binary protocol)       │
└──────────────────────┬──────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────┐
│ BannouServer (Single Process)                        │
│                                                      │
│  Connect (L1) ── binary routing ──► Service Plugins  │
│                                                      │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────┐  │
│  │ L0: State   │  │ L0: Messaging│  │ L0: Mesh   │  │
│  │ (SQLite)    │  │ (InMemory)   │  │ (Local)    │  │
│  └─────────────┘  └──────────────┘  └────────────┘  │
│                                                      │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────┐  │
│  │ Workshop    │  │ Inventory    │  │ Location   │  │
│  │ (L4)        │  │ (L2)         │  │ (L2)       │  │
│  └─────────────┘  └──────────────┘  └────────────┘  │
│                                                      │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────┐  │
│  │ Item (L2)   │  │ Transit (L2) │  │ Etc.       │  │
│  └─────────────┘  └──────────────┘  └────────────┘  │
│                                                      │
│  data/                                               │
│  ├── state.db    (SQLite: entity state)              │
│  ├── saves/      (Save-Load snapshots)               │
│  └── seeds/      (game-specific seed data)           │
└─────────────────────────────────────────────────────┘
```

### Service-to-Service Calls

In cloud mode, lib-mesh routes inter-service calls through YARP with Redis-backed service discovery, circuit breakers, and retry logic. In self-hosted mode, the default "bannou" omnipotent routing sends everything to the local process. Mesh features like circuit breakers and load balancing are irrelevant when all services are co-located -- there's nothing to circuit-break or balance.

The generated `I{Service}Client` types use lib-mesh underneath. In self-hosted mode, these clients make HTTP calls to `localhost:5012`, which Kestrel routes to the co-located controller, which calls the service implementation. The round-trip is in-process memory copies with HTTP serialization overhead. For the vast majority of game interactions, this is sub-millisecond.

---

## Persistence: The SQLite Backend

The only missing infrastructure for a shipping self-hosted product. See [#442](https://github.com/beyond-immersion/bannou-service/issues/442) for the full design.

### Why SQLite Over InMemory + Save-Load

| Approach | Durability | Startup Time | Complexity | Data Loss Risk |
|----------|-----------|-------------|------------|----------------|
| **InMemory + Save-Load snapshots** | Periodic | Cold start deserialization | Low | Data since last snapshot |
| **SQLite** | Every write | Instant (DB already on disk) | Medium | None (WAL mode) |
| **Redis + MySQL (cloud)** | Every write | Requires external services | High | None |

InMemory + periodic snapshots works for prototyping but risks data loss on crash. SQLite with WAL (Write-Ahead Logging) mode provides crash-safe persistence with zero external dependencies.

### Backend Selection

```
server.env:
  STATE_USE_INMEMORY=true   → InMemoryStateStore (testing, prototyping)
  STATE_USE_SQLITE=true     → SqliteStateStore (self-hosted, durable)
  (neither)                 → Redis/MySQL (cloud deployment)
```

### What SQLite Covers

The `IStateStore<T>` interface (basic CRUD, ETag concurrency, TTL) maps directly to a SQLite table. `ICacheableStateStore<T>` (sets, sorted sets) maps to auxiliary tables. This covers the needs of all services in a self-hosted deployment.

Redis-specific operations (`IRedisOperations`, Lua scripts) and MySQL-specific operations (`IQueryableStateStore<T>`, LINQ) are not available in SQLite mode. Services using those specialized interfaces would need graceful fallback -- but in practice, most services use only the base `IStateStore<T>` interface.

---

## The Game-Type Spectrum

Self-hosted Bannou supports different game types with different service subsets and different expectations about world liveness:

### Production-Centric (Factorio, Satisfactory)

**Plugins needed**: State, Messaging, Mesh, Location, Inventory, Item, Workshop, Transit, Utility, Environment, Worldstate, Seed, Currency, Craft, License

**World when offline**: Production continues via lazy evaluation. Environment conditions frozen at last state. No NPC decisions. World "time" advances; world "thought" pauses.

**Player expectation**: "My factory kept running while I was away." Workshop delivers this perfectly.

**What makes this work**: The game is deterministic production math. Given inputs, rates, and elapsed time, the output is a closed-form calculation. No NPC cognition needed. The game client renders the factory; Bannou computes the production.

### Farming/Life Sim (Stardew Valley, Harvest Moon)

**Additional plugins**: Character, Species, Relationship, Collection, Actor (for NPC behaviors)

**World when offline**: Crops grow (Workshop lazy eval). Seasons advance (Worldstate). NPC daily routines could be approximated statistically ("the blacksmith made ~5 items per game-day based on historical rate"). Relationships don't evolve. No new events.

**Player expectation**: "My crops grew while I was away, but the NPCs waited for me." This is exactly what Stardew Valley does -- time-based production advances, but social simulation pauses.

### Living World Lite (Single-Player RPG with Autonomous NPCs)

**Additional plugins**: Actor, Behavior, Character-Personality, Character-Encounter, Character-History, Quest, Puppetmaster, Storyline, Faction, plus domain-specific L4 services

**World when offline**: This is where the tension lives. Options:

1. **Pause on exit** (simplest): Worldstate's game clock stops. Everything freezes. Honest about the limitation.

2. **Compressed catch-up** (ambitious): On login, run an abbreviated simulation pass. For each NPC, evaluate their GOAP planner once per game-day of missed time (instead of every 100-500ms). NPCs make coarser decisions but the world evolves. Workshop handles production; a "catch-up scheduler" handles NPC state advancement. Lossy but plausible.

3. **Always-online personal server** (cloud): The server runs in the cloud. World continues 24/7. Player connects from any device. This isn't self-hosted but it's the only way to get a truly living single-player world.

**Player expectation**: Depends on the game's promise. If marketed as "a living world," option 2 or 3 is needed. If marketed as "your adventure," option 1 is fine.

### Multiplayer Dedicated Server (Valheim, Palworld)

**All plugins**: Full stack, selected per game requirements.

**World when offline**: Server runs continuously (hosted by a player or on a VPS). World is always alive. This is the standard dedicated server model -- Bannou is already built for it.

---

## Future Investment: In-Process Mesh Routing

> **Status**: Idea. Not required for the Satisfactory model. Would benefit .NET game engines (Stride) that embed Bannou as a library.

### The Concept

All inter-service calls flow through generated `I{Service}Client` types, which use lib-mesh to resolve the target endpoint and make HTTP calls. In self-hosted mode, these HTTP calls go to localhost -- functional but redundant when all services are in the same process.

An in-process routing mode could short-circuit the HTTP path:

```
Current (all modes):
  IWorkshopClient.CreateTaskAsync(request)
    → lib-mesh resolves "workshop" → http://localhost:5012
    → HTTP POST /workshop/task/create
    → Kestrel → WorkshopController → WorkshopService.CreateTaskAsync
    → HTTP response → deserialize → return

In-process mode:
  IWorkshopClient.CreateTaskAsync(request)
    → lib-mesh detects "local" routing mode
    → Resolves IWorkshopService from DI container
    → Direct method invocation (reflection or compiled delegate)
    → return
```

### How It Could Work

The generated `I{Service}Client` types already know their target service methods -- they're generated from the same OpenAPI schemas as the controllers. Two approaches:

**Approach A: Client-side reflection bypass.** Each generated client carries metadata about the corresponding service interface method signatures. When mesh configuration indicates "local" routing, the client resolves the service from DI and invokes directly via cached reflection delegates. lib-mesh is bypassed entirely.

- **Pro**: Simplest implementation. No mesh changes needed.
- **Con**: Bypasses circuit breakers, retry logic, telemetry instrumentation. Acceptable for self-hosted (single process, no network failures), but means two code paths.

**Approach B: Mesh-aware in-process invocation.** The generated clients provide reflection metadata to lib-mesh. Mesh uses its normal pipeline (circuit breaker evaluation, telemetry, retry policy) but invokes via reflection instead of HTTP at the transport layer. The mesh routing table maps "workshop" → "in-process" instead of "workshop" → "http://localhost:5012".

- **Pro**: Preserves mesh instrumentation, single code path. Circuit breakers could still function (protecting against service exceptions rather than network failures). Telemetry still captures inter-service call metrics.
- **Con**: More complex. The mesh transport abstraction needs a new "in-process" transport alongside "HTTP."

### Assessment

Approach B is architecturally cleaner and preserves the mesh's observability and fault-tolerance patterns. But it's an optimization, not a requirement. The Satisfactory model works today with HTTP to localhost. The 0.5-2ms per-call overhead is irrelevant for the game types this targets.

This investment makes sense if/when:
- A .NET game engine (Stride) wants to embed Bannou as a library without running Kestrel
- Profiling shows HTTP serialization overhead matters for a specific game's call patterns (unlikely for factory/farming games, possible for high-frequency NPC action dispatching)
- We want to offer a "zero-network" embedded deployment as a product differentiator

---

## SDK Considerations

### Local Server Connection Defaults

The generated SDKs should support a "local mode" that defaults to `localhost:5012`:

```csharp
// Current: explicit server URL required
var client = new BannouClient("wss://my-server.example.com:5013");

// Local mode: defaults to localhost
var client = new BannouClient(BannouConnectionMode.Local);
// Equivalent to: new BannouClient("ws://localhost:5012")
```

This is a trivial SDK change but significantly improves the self-hosted developer experience.

### Game-Specific SDK Profiles

For a Factorio-style game, the SDK doesn't need clients for Voice, Broadcast, Matchmaking, etc. A build-time profile system could generate only the clients the game uses:

```bash
# Generate SDK with only production-game clients
scripts/generate-sdk.sh --profile factory-game \
  --services workshop,inventory,item,location,transit,utility,currency,craft,seed,license
```

This reduces SDK size and compile time for game developers.

### Server Lifecycle Management

The SDK should include utilities for managing the local server process:

```csharp
// Start local server
var server = await BannouLocalServer.StartAsync(new LocalServerOptions
{
    PluginDirectory = "./BannouServer/plugins",
    DataDirectory = "./BannouServer/data",
    SeedDirectory = "./BannouServer/seeds",
    Port = 5012,
    UseSqlite = true,
});

// Wait for readiness
await server.WaitForReadyAsync(timeout: TimeSpan.FromSeconds(10));

// Connect game client
var client = new BannouClient(server.ConnectionUrl);

// On game exit
await server.StopAsync(graceful: true);
```

---

## What This Means for Game Development

### A Factorio Game in Seed Data

A factory automation game built on Bannou ships with:

| Component | Format | What It Contains |
|-----------|--------|-----------------|
| **Workshop blueprints** | YAML seed data | Every recipe: inputs, outputs, rates, worker requirements, reactive triggers |
| **Item templates** | YAML seed data | Every material, intermediate, and final product |
| **Transit modes** | YAML seed data | Belt speeds, cart capacities, train schedules |
| **Utility networks** | YAML seed data | Power grid types, pipe throughput, fluid types |
| **Environment templates** | YAML seed data | Biomes, weather patterns, resource distribution |
| **License boards** | YAML seed data | Research trees, technology unlocks |
| **ABML behaviors** | ABML files | NPC worker routing, factory management, logistics |
| **Location templates** | YAML seed data | Factory station layouts, mining site structures |
| **Currency definitions** | YAML seed data | In-game currencies, exchange rates |

The game developer authors seed data and ABML behaviors. Bannou runs the simulation. The game engine renders it. No server-side game code. No custom backend development.

### "Same Systems, Different Games"

This is the "Ship Games Fast" north star applied to self-hosted single-player:

| Game Type | Same Bannou Stack | Different Seed Data |
|-----------|------------------|-------------------|
| **Factory automation** | Workshop, Inventory, Transit, Utility | Recipes, belts, power grids |
| **Farming simulation** | Workshop, Inventory, Environment, Worldstate | Crop growth stages, seasons, soil types |
| **City builder** | Workshop, Location, Utility, Currency, Transit | Construction stages, infrastructure networks, budgets |
| **Crafting RPG** | Craft, Inventory, Item, License, Quest | Recipes, materials, skill trees, quest chains |
| **Trading sim** | Trade, Currency, Transit, Market, Inventory | Goods, routes, price models, NPC merchants |
| **Colony management** | All of the above + Actor, Character, Faction | NPC personalities, faction governance, social dynamics |

Each game type is a different configuration of the same plugins, different seed data, different ABML behaviors, and a different game engine rendering layer. The Bannou server binary is identical.

---

## The Edges: What Doesn't Work

### Real-Time Combat at Frame Rate

If a game requires frame-rate combat input (60fps action combat, fighting games), the localhost HTTP round-trip of 0.5-2ms per call limits responsiveness. At 60fps, you have 16.67ms per frame -- a 2ms service call consumes 12% of frame budget. For turn-based, strategy, or factory games, this is irrelevant. For action combat, the game engine would need to handle combat locally with server reconciliation -- a common pattern in multiplayer games, equally applicable here.

### Living World During Offline Time

As discussed above, NPC cognition cannot be retroactively computed like production math. For games that promise a living world, either:
- Accept the world pauses when the server stops (honest, acceptable for many game types)
- Implement compressed catch-up simulation (ambitious, lossy, research-grade)
- Use always-online cloud hosting (eliminates "self-hosted" but solves the problem)

### Non-.NET Game Engines Without Interop

Unreal Engine (C++) cannot embed Bannou as a library. The sidecar process model works but means all game-server communication goes through HTTP/WebSocket. This is fine -- it's how dedicated servers work in most games -- but it means the "embedded library" optimization isn't available.

### State Store Feature Parity

SQLite doesn't support Redis Sorted Sets natively or MySQL LINQ queries. Services using `IRedisOperations` or `IQueryableStateStore<T>` would need fallback behavior. In practice, most services use only `IStateStore<T>` (basic CRUD), which SQLite handles perfectly.

### Firewall Prompts on Windows

Starting a local HTTP server on Windows triggers Windows Defender Firewall prompts. This can be mitigated by:
- Binding to `127.0.0.1` only (loopback, no external access) for single-player
- Registering firewall exceptions during game installation
- Using a named pipe or Unix domain socket instead of TCP (future optimization)

---

## Implementation Roadmap

### Phase 1: Core Infrastructure (Enables Prototyping)

| Task | Effort | Dependency |
|------|--------|------------|
| **SQLite state store backend** ([#442](https://github.com/beyond-immersion/bannou-service/issues/442)) | Medium | None |
| **Self-hosted server.env template** | Trivial | None |
| **SDK local connection mode** | Trivial | None |

After Phase 1, a developer can build a Factorio-style game on Bannou with local persistence.

### Phase 2: Developer Experience (Enables Shipping)

| Task | Effort | Dependency |
|------|--------|------------|
| **SDK server lifecycle management** (start/stop/wait) | Small | Phase 1 |
| **SDK game-specific build profiles** | Small | None |
| **Seed data loading from local directory** | Small | None |
| **Graceful shutdown with state flush** | Small | #442 |
| **Self-hosted deployment guide** (documentation) | Small | Phase 1 |

After Phase 2, a game studio can ship a self-hosted game on Bannou with professional-quality developer experience.

### Phase 3: Optimization (Optional)

| Task | Effort | Dependency |
|------|--------|------------|
| **In-process mesh routing (Approach B)** | Medium | None |
| **Compressed NPC catch-up simulation** | Large | Actor, Behavior |
| **Platform-specific packaging** (Steam, Epic) | Medium | Phase 2 |
| **Loopback-only binding (skip firewall)** | Trivial | None |

Phase 3 is optional polish. The Satisfactory model ships without any of it.

---

## Relationship to Vision Principles

| Vision Principle | How Self-Hosted Deployment Serves It |
|-----------------|--------------------------------------|
| **Ship Games Fast** (North Star #4) | A factory game is seed data + ABML behaviors + rendering engine. No custom backend code. Ship in weeks, not months. |
| **Living Game Worlds** (North Star #1) | NPC behaviors run identically in self-hosted and cloud modes. A local dedicated server IS a living world -- just for one player. |
| **100K+ Concurrent NPCs** (North Star #3) | Self-hosted targets fewer NPCs (hundreds to thousands), but the same Actor runtime scales down gracefully. |
| **Emergent Over Authored** (North Star #5) | Workshop lazy eval, NPC GOAP decisions, and environment-driven production create emergent factory behavior from seed data configuration. |
| **Same Systems, Different Games** (PLAYER-VISION) | Factory games, farming sims, city builders, and crafting RPGs are different seed data sets on the same infrastructure. |

---

## Conclusion

The Bannou architecture is already a self-hosted game backend. The in-memory infrastructure, selective plugin loading, lazy evaluation, and environment-driven configuration mean that a Satisfactory-style deployment works today with zero code changes. The SQLite backend (#442) provides the missing persistence layer. SDK conveniences improve developer experience. In-process mesh routing is an optional optimization.

The most significant implication: **Bannou becomes a game engine backend-as-a-library.** A studio building a Factorio-style game doesn't write backend code. They author Workshop blueprints, Item templates, ABML behaviors, and environment configurations. They build a rendering engine. Bannou handles everything between player input and world state. The game IS the seed data.

---

*This document describes the design for self-hosted Bannou deployment. For production chain mechanics, see [LOCATION-BOUND-PRODUCTION.md](LOCATION-BOUND-PRODUCTION.md). For memento systems, see [MEMENTO-INVENTORIES.md](MEMENTO-INVENTORIES.md). For the SQLite backend, see [#442](https://github.com/beyond-immersion/bannou-service/issues/442). For vision context, see [VISION.md](../reference/VISION.md).*
